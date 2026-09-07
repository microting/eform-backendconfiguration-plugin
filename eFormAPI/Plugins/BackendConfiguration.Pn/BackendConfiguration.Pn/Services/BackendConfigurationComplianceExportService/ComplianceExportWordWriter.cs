using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.ComplianceReport;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.WordService;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using eFormCore;
using ImageMagick;
using Microsoft.Extensions.Logging;
using Microting.eForm.Dto;
using Sentry;
// Aliased, not imported: the Wordprocessing namespace defines Settings, Text,
// Header and Footer, which collide with Microting.eForm.Dto.Settings (used below
// for the SDK picture settings) and read ambiguously next to the HTML body.
using W = DocumentFormat.OpenXml.Wordprocessing;

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
/// <b>The page shell — A4 landscape, a repeated filter header and a branded
/// footer (#1189)</b> — is applied to the IN-MEMORY copy of <c>file.docx</c>
/// before the HTML is converted, by <see cref="ApplyPageShell"/>. The embedded
/// template itself is untouched: it is shared by five <c>WordService</c>
/// generators behind <c>GET report/reports/file</c>, whose output must not
/// change. Doing it before <c>AddHtml</c> is what makes <c>width="100%"</c>
/// tables and the appendix images compute against the landscape width —
/// HtmlToOpenXml reads the orientation from the document it is handed.
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

    /// <summary>A4 landscape, in twips — the template's own A4 portrait values swapped.</summary>
    public const uint PageWidthTwips = 16834;

    public const uint PageHeightTwips = 11909;

    /// <summary>
    /// The template's left and right margins (<c>w:pgMar w:left="1440" w:right="1440"</c>),
    /// kept; they are what the footer's right tab stop is computed from.
    /// </summary>
    private const uint SideMarginTwips = 1440;

    /// <summary>
    /// Distance from the paper edge to the header. The template ships with
    /// <c>w:header="0"</c> because it has no header part; left at 0, the header
    /// line would sit on the paper's top edge.
    /// </summary>
    public const uint HeaderDistanceTwips = 720;

    /// <summary>Right tab stop for the footer's <c>p. n/N</c>: the text width, 16834 − 2 × 1440.</summary>
    public const int FooterRightTabTwips = (int)(PageWidthTwips - 2 * SideMarginTwips);

    /// <summary>Header and footer run size in half-points (9 pt).</summary>
    private const string ShellFontSizeHalfPoints = "18";

    /// <summary>The footer's left-hand brand. A literal, not a localisation key (#1189).</summary>
    private const string FooterBrand = "Microting";

    /// <summary>The footer's page-number prefix, <c>p. n/N</c>. A literal, not a key (#1189).</summary>
    private const string FooterPagePrefix = "p. ";

    /// <summary>
    /// The pale, print-safe green a completed Detaljer row is tinted with (#1191).
    /// Deliberately NOT the theme's <c>--status-aktiv-bg</c> (<c>#1b5e20</c> /
    /// <c>#2e7d32</c>), which is a dark fill behind white text on screen and would
    /// swallow 7 pt black text on paper.
    ///
    /// <para>
    /// Emitted on the <c>&lt;tr&gt;</c> only. HtmlToOpenXml 3.5.0 turns a row's
    /// <c>background-color</c> into a <c>w:shd</c> on EVERY cell of that row
    /// (<c>TableCellProperties/Shading</c>, fill <c>E8F5E9</c>) — the same
    /// mechanism the header row above already relies on — and leaves an untinted
    /// row with no shading element at all, which is what the writer tests pin.
    /// </para>
    /// </summary>
    public const string DoneRowFill = "#e8f5e9";

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

        // Landscape, header, footer — on the in-memory copy, before any HTML goes in.
        ApplyPageShell(docxStream, document);

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
        // The period, property and board are in the page header on every page
        // (#1189), so the body opens with the document title alone — left-aligned,
        // and only when there is one (the format issues set it; Rapport has none).
        if (!string.IsNullOrEmpty(document.Title))
        {
            body.Append(
                $@"<p style='font-size:16px;text-align:left;font-weight:700;'>{Esc(document.Title)}</p>");
        }

        foreach (var table in document.Tables)
        {
            // Rapport's tags caption sits ABOVE the bold headline (#1188, PDF page
            // 5): a plain small paragraph. Only emitted when there is one — Oversigt
            // and Detaljer carry none, and a Rapport group whose cases have no tags
            // gets no empty line ahead of its heading. #1192 restyles it.
            if (!string.IsNullOrEmpty(table.Caption))
            {
                body.Append(
                    $@"<p style='font-size:9pt;text-align:left;'>{Esc(table.Caption)}</p>");
            }

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
                // rows it is appended to; a completed Detaljer row is tinted
                // (#1191) so open work stands out. The two never coincide.
                body.Append(row.IsTotal
                    ? @"<tr style='font-size:7pt;font-weight:bold;'>"
                    : row.IsDone
                        ? $@"<tr style='font-size:7pt;background-color:{DoneRowFill};'>"
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

    /// <summary>
    /// Turns the template's A4-portrait, footer-only shell into the mock-ups' A4
    /// landscape page with a repeated filter header and a <c>Microting … p. n/N</c>
    /// footer (#1189). Opens and saves the package on <paramref name="docxStream"/>
    /// without closing it, and rewinds it, so <see cref="WordProcessor"/> can open
    /// it again afterwards.
    ///
    /// <para>
    /// The order inside <c>sectPr</c> matters: <c>headerReference</c> and
    /// <c>footerReference</c> must precede <c>pgSz</c>/<c>pgMar</c>, so the new
    /// header reference goes in at index 0 rather than being appended.
    /// </para>
    /// </summary>
    private void ApplyPageShell(MemoryStream docxStream, ComplianceExportDocument document)
    {
        using (var word = WordprocessingDocument.Open(docxStream, true))
        {
            var mainPart = word.MainDocumentPart
                           ?? throw new InvalidOperationException($"{DocxResource} has no main document part");
            var docBody = mainPart.Document?.Body
                          ?? throw new InvalidOperationException($"{DocxResource} has no body");

            var sectPr = docBody.GetFirstChild<W.SectionProperties>();
            if (sectPr == null)
            {
                sectPr = new W.SectionProperties();
                docBody.Append(sectPr);
            }

            // --- A4 landscape ---
            var pageSize = sectPr.GetFirstChild<W.PageSize>();
            if (pageSize == null)
            {
                pageSize = new W.PageSize();
                sectPr.Append(pageSize);
            }

            pageSize.Width = PageWidthTwips;
            pageSize.Height = PageHeightTwips;
            pageSize.Orient = W.PageOrientationValues.Landscape;

            // --- header: Ejendom / Kalender / Periode on every page ---
            var headerPart = mainPart.AddNewPart<HeaderPart>();
            headerPart.Header = BuildHeader(document);
            headerPart.Header.Save();
            sectPr.InsertAt(new W.HeaderReference
            {
                Type = W.HeaderFooterValues.Default,
                Id = mainPart.GetIdOfPart(headerPart)
            }, 0);

            var pageMargin = sectPr.GetFirstChild<W.PageMargin>();
            if (pageMargin == null)
            {
                pageMargin = new W.PageMargin
                {
                    Left = SideMarginTwips, Right = SideMarginTwips, Top = 1440, Bottom = 1440,
                    Footer = 720U, Gutter = 0U
                };
                sectPr.Append(pageMargin);
            }

            pageMargin.Header = HeaderDistanceTwips;

            // --- footer: Microting … p. n/N ---
            // The template already carries one default footer part (PAGE / NUMPAGES,
            // no brand); its content is replaced wholesale rather than edited.
            var footerPart = mainPart.FooterParts.FirstOrDefault();
            if (footerPart == null)
            {
                footerPart = mainPart.AddNewPart<FooterPart>();
                sectPr.InsertAt(new W.FooterReference
                {
                    Type = W.HeaderFooterValues.Default,
                    Id = mainPart.GetIdOfPart(footerPart)
                }, 1);
            }

            footerPart.Footer = BuildFooter();
            footerPart.Footer.Save();

            mainPart.Document.Save();
        }

        docxStream.Position = 0;
    }

    /// <summary>
    /// One paragraph: <c>**Ejendom:** {property}   **Kalender:** {board}   **Periode:** {period}</c>.
    /// Labels are the existing <c>Property</c>, <c>CalendarBoard</c> keys and the
    /// new <c>Period</c> one; a missing property/board label falls back to
    /// <c>All</c>, the same word the file name uses.
    /// </summary>
    private W.Header BuildHeader(ComplianceExportDocument document)
    {
        var allLabel = localizationService.GetString("All");
        var propertyLabel = string.IsNullOrWhiteSpace(document.PropertyLabel) ? allLabel : document.PropertyLabel;
        var boardLabel = string.IsNullOrWhiteSpace(document.BoardLabel) ? allLabel : document.BoardLabel;

        var paragraph = new W.Paragraph(new W.ParagraphProperties(new W.SpacingBetweenLines { After = "0" }));
        paragraph.Append(ShellRun($"{localizationService.GetString("Property")}:", bold: true));
        paragraph.Append(ShellRun($" {propertyLabel}   "));
        paragraph.Append(ShellRun($"{localizationService.GetString("CalendarBoard")}:", bold: true));
        paragraph.Append(ShellRun($" {boardLabel}   "));
        paragraph.Append(ShellRun($"{localizationService.GetString("Period")}:", bold: true));
        paragraph.Append(ShellRun($" {document.Period ?? string.Empty}"));

        return new W.Header(paragraph);
    }

    /// <summary>
    /// One paragraph: <c>Microting</c> at the left margin, then a right tab stop at
    /// the text width carrying <c>p. </c> + <c>PAGE</c> + <c>/</c> + <c>NUMPAGES</c>.
    /// The page numbers are <c>fldSimple</c> fields — HtmlToOpenXml has no HTML
    /// syntax for a field, so this part is SDK objects regardless of how the header
    /// is built. The cached results (<c>1</c>) are placeholders the renderer
    /// recomputes.
    /// </summary>
    private static W.Footer BuildFooter()
    {
        var paragraph = new W.Paragraph(new W.ParagraphProperties(
            new W.Tabs(new W.TabStop { Val = W.TabStopValues.Right, Position = FooterRightTabTwips }),
            new W.SpacingBetweenLines { Before = "0", After = "0" }));

        paragraph.Append(ShellRun(FooterBrand));
        paragraph.Append(new W.Run(ShellRunProperties(bold: false), new W.TabChar()));
        paragraph.Append(ShellRun(FooterPagePrefix));
        paragraph.Append(PageField("PAGE"));
        paragraph.Append(ShellRun("/"));
        paragraph.Append(PageField("NUMPAGES"));

        return new W.Footer(paragraph);
    }

    private static W.SimpleField PageField(string instruction) =>
        new(new W.Run(ShellRunProperties(bold: false), new W.Text("1")))
        {
            Instruction = $" {instruction} "
        };

    private static W.Run ShellRun(string text, bool bold = false) =>
        new(ShellRunProperties(bold), new W.Text(text) { Space = SpaceProcessingModeValues.Preserve });

    private static W.RunProperties ShellRunProperties(bool bold)
    {
        var properties = new W.RunProperties();
        if (bold) properties.Append(new W.Bold());
        properties.Append(new W.FontSize { Val = ShellFontSizeHalfPoints });
        properties.Append(new W.FontSizeComplexScript { Val = ShellFontSizeHalfPoints });
        return properties;
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

    /// <summary>
    /// The cell's Word/PDF text: an explicit
    /// <see cref="ComplianceExportCell.DisplayText"/> wins (Detaljer's weekday
    /// date, #1191), then a typed date renders <c>dd.MM.yyyy</c>, otherwise the
    /// display text — which is the en dash for an empty cell. Word/PDF keep the
    /// glyph; only CSV blanks it.
    /// </summary>
    private static string Render(ComplianceExportCell cell, ComplianceExportCellType type)
    {
        if (cell == null) return ComplianceExportCell.EmptyGlyph;
        if (!string.IsNullOrEmpty(cell.DisplayText)) return cell.DisplayText;

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
