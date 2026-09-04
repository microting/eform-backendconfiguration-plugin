using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.ComplianceReport;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.WordService;
using eFormCore;
using ImageMagick;
using Microsoft.Extensions.Logging;
using Microting.eForm.Dto;
using Sentry;

namespace BackendConfiguration.Pn.Services.BackendConfigurationComplianceExportService;

/// <summary>
/// The <c>.docx</c> renderer, and therefore the PDF renderer too — PDF is this
/// document put through <c>soffice</c> (#1160 decision 4, #1169 §1).
///
/// <para>
/// The MECHANISM is <c>WordService.GenerateWordDashboard</c>'s, reused verbatim:
/// the same two embedded resources
/// (<c>BackendConfiguration.Pn.Resources.Templates.WordExport.page.html</c> as the
/// HTML shell with its <c>{%Content%}</c> placeholder, and <c>file.docx</c> as the
/// styled shell), the same <see cref="WordProcessor"/> wrapper over HtmlToOpenXml,
/// and the same image embedding. What is NOT reused is that method's body, which
/// emits a fixed six-column preamble no compliance view has — see
/// <see cref="ComplianceExportDocument"/> for the full reasoning.
/// </para>
///
/// <para>
/// <b>Pagination is Word's and LibreOffice's job.</b> Nothing here carries a row
/// budget, a page-height budget or a pixels-per-figure constant. The prototype's
/// client-side A4 paginator (<c>compliance.js:800-1143</c>, <c>PDF_ROWS_PER_PAGE</c>,
/// <c>PDF_BILAG_BUDGET_PX</c> and the rest) exists only because a browser was
/// rendering A4 sheets into the DOM, and it is a dual budget hand-synced with the
/// CSS. None of it is ported, and none of it should be reimplemented server-side.
/// </para>
///
/// <para>
/// <b>Images never travel over HTTP.</b> <see cref="InsertImage"/> takes a
/// <c>Stream</c> from either S3 (<c>Core.GetFileFromS3Storage</c>) or the local
/// picture directory (the <c>fileLocationPicture</c> SDK setting) and base64-encodes
/// it through ImageMagick into a <c>data:</c> URI. There is no
/// <c>HttpClient</c> here and no request to
/// <c>api/template-files/get-image/…</c>, which is exactly why server-side
/// generation sidesteps the bearer-only image endpoint.
/// </para>
/// </summary>
public class ComplianceExportWordWriter(
    IBackendConfigurationLocalizationService localizationService,
    ILogger logger)
{
    private const string PageResource = "BackendConfiguration.Pn.Resources.Templates.WordExport.page.html";
    private const string DocxResource = "BackendConfiguration.Pn.Resources.Templates.WordExport.file.docx";

    /// <summary>
    /// Renders the document. <paramref name="core"/> is used only to resolve image
    /// bytes; when the document carries no image blocks it is never touched.
    /// </summary>
    public async Task<Stream> WriteAsync(ComplianceExportDocument document, Core core)
    {
        var assembly = Assembly.GetExecutingAssembly();

        await using var htmlResourceStream = assembly.GetManifestResourceStream(PageResource)
                                             ?? throw new InvalidOperationException(
                                                 $"Embedded resource {PageResource} is missing");
        string shell;
        using (var reader = new StreamReader(htmlResourceStream))
        {
            shell = await reader.ReadToEndAsync();
        }

        await using var docxResourceStream = assembly.GetManifestResourceStream(DocxResource)
                                             ?? throw new InvalidOperationException(
                                                 $"Embedded resource {DocxResource} is missing");

        // Not disposed here: it is the return value, and WordProcessor.Dispose
        // saves the package into it without closing it.
        var docxStream = new MemoryStream();
        await docxResourceStream.CopyToAsync(docxStream);

        var s3Enabled = false;
        var basePicturePath = string.Empty;
        var needsImages = DocumentHasImages(document);
        if (needsImages && core != null)
        {
            s3Enabled = (await core.GetSdkSetting(Settings.s3Enabled) ?? string.Empty)
                .ToLowerInvariant() == "true";
            basePicturePath = await core.GetSdkSetting(Settings.fileLocationPicture);
        }

        var body = new StringBuilder();
        body.Append("<body>");
        body.Append(
            $@"<p style='font-size:20px;text-align:center;font-weight:700;'>{Esc(document.Title)}</p>");
        if (!string.IsNullOrEmpty(document.Period))
        {
            body.Append(
                $@"<p style='font-size:12px;text-align:center;'>{Esc(document.Period)}</p>");
        }

        foreach (var table in document.Tables)
        {
            if (!string.IsNullOrEmpty(table.Title))
            {
                body.Append(
                    $@"<p style='font-size:14px;text-align:left;font-weight:700;'>{Esc(table.Title)}</p>");
            }

            body.Append(@"<table width=""100%"" border=""1"">");
            body.Append(@"<tr style='background-color:#f5f5f5;font-weight:bold;font-size:7pt;'>");
            foreach (var column in table.Columns)
            {
                body.Append($@"<td>{Esc(column.Header)}</td>");
            }

            body.Append(@"</tr>");

            foreach (var row in table.Rows)
            {
                // The totals row is bold, so a reader can tell it from the data
                // rows it is appended to.
                body.Append(row.IsTotal
                    ? @"<tr style='font-size:7pt;font-weight:bold;'>"
                    : @"<tr style='font-size:7pt;'>");
                for (var i = 0; i < row.Cells.Count; i++)
                {
                    var type = i < table.Columns.Count ? table.Columns[i].Type : ComplianceExportCellType.Text;
                    body.Append($@"<td>{Esc(Render(row.Cells[i], type))}</td>");
                }

                body.Append(@"</tr>");
            }

            body.Append(@"</table>");
            body.Append(@"<br/>");

            if (table.ImageBlocks.Count == 0 || core == null) continue;

            foreach (var block in table.ImageBlocks)
            {
                var caption = block.TotalImages > block.ImageNames.Count
                    ? $"{localizationService.GetString("Appendix")}: {block.Caption} " +
                      $"({block.ImageNames.Count}/{block.TotalImages})"
                    : $"{localizationService.GetString("Appendix")}: {block.Caption}";

                body.Append(
                    $@"<p style='font-size:7pt;page-break-before:always'>{Esc(caption)}</p>");

                for (var i = 0; i < block.ImageNames.Count; i++)
                {
                    await InsertImage(block.ImageNames[i], body, 600, 650, core, basePicturePath, s3Enabled);

                    var geoLink = i < block.GeoLinks.Count ? block.GeoLinks[i] : null;
                    if (!string.IsNullOrEmpty(geoLink))
                    {
                        body.Append(
                            $@"<p style='font-size:7pt;'><a href=""{Esc(geoLink)}"">{Esc(geoLink)}</a></p>");
                    }
                }
            }
        }

        // The document-wide image ceiling, stated in the same (embedded/requested)
        // idiom a block caption uses for the per-case cap — a report that quietly
        // stops carrying photographs part way through is worse than one that says
        // so. Written once, at the end, rather than as an empty captioned block per
        // dropped case.
        if (document.AppendixImagesRequested > document.AppendixImagesEmbedded)
        {
            var note = $"{localizationService.GetString("Appendix")}: " +
                       $"{localizationService.GetString("ImageAppendixDocumentLimit")} " +
                       $"({document.AppendixImagesEmbedded}/{document.AppendixImagesRequested})";
            body.Append($@"<p style='font-size:7pt;'>{Esc(note)}</p>");
        }

        body.Append("</body>");

        var word = new WordProcessor(docxStream);
        word.AddHtml(shell.Replace("{%Content%}", body.ToString()));
        word.Dispose();
        docxStream.Position = 0;
        return docxStream;
    }

    private static bool DocumentHasImages(ComplianceExportDocument document)
    {
        foreach (var table in document.Tables)
        {
            if (table.ImageBlocks.Count > 0) return true;
        }

        return false;
    }

    /// <summary>
    /// <c>WordService.InsertImage</c>'s logic (<c>WordService.cs:956-1007</c>),
    /// with two behavioural differences and no third: the S3/local decision is
    /// passed in rather than read from a mutable field, and a failure is LOGGED
    /// rather than written to <c>Console</c>. A missing or unreadable image is
    /// skipped — one broken photograph must not fail a 200-page report.
    /// </summary>
    private async Task InsertImage(
        string imageName, StringBuilder html, int imageSize, int imageWidth,
        Core core, string basePicturePath, bool s3Enabled)
    {
        Stream stream = null;
        try
        {
            if (s3Enabled)
            {
                var storageResult = await core.GetFileFromS3Storage(imageName);
                stream = storageResult?.ResponseStream;
            }
            else
            {
                var filePath = Path.Combine(basePicturePath ?? string.Empty, imageName);
                if (!File.Exists(filePath)) return;
                stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            }

            if (stream == null) return;

            using var image = new MagickImage(stream);
            var ratio = image.Height / (decimal)image.Width;
            var newWidth = imageSize;
            var newHeight = (int)Math.Round(ratio * newWidth);
            image.Resize((uint)newWidth, (uint)newHeight);
            image.Crop((uint)newWidth, (uint)newHeight);

            html.Append(
                $@"<p><img src=""data:image/png;base64,{image.ToBase64()}"" width=""{imageWidth}px"" alt="""" /></p>");
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            logger.LogWarning(e,
                "ComplianceExportWordWriter.InsertImage: could not embed {ImageName}; skipped.", imageName);
        }
        finally
        {
            if (stream != null) await stream.DisposeAsync();
        }
    }

    private static string Render(ComplianceExportCell cell, ComplianceExportCellType type)
    {
        if (cell == null) return ComplianceExportCell.EmptyGlyph;

        return type switch
        {
            ComplianceExportCellType.Date when cell.Date.HasValue =>
                cell.Date.Value.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
            _ => cell.Text ?? ComplianceExportCell.EmptyGlyph
        };
    }

    /// <summary>
    /// Minimal HTML escaping. Every string in the document originates in the
    /// database — property names, task titles, tag names, worker names and eForm
    /// ANSWERS typed by a worker on a phone — and is interpolated into markup that
    /// HtmlToOpenXml parses. Without this, a <c>&lt;</c> in an answer silently
    /// swallows the rest of the row, and the existing report generators (which do
    /// not escape) are the reason to do it here rather than to match them.
    /// </summary>
    private static string Esc(string value) =>
        (value ?? string.Empty)
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;");
}
