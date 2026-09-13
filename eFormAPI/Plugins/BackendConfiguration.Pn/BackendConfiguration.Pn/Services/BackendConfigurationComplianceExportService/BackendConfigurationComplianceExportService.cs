using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.ComplianceReport;
using BackendConfiguration.Pn.Services.BackendConfigurationComplianceReportService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Sentry;

namespace BackendConfiguration.Pn.Services.BackendConfigurationComplianceExportService;

/// <summary>
/// Server-side CSV / PDF export for the standalone Compliance page (#1169; Excel
/// removed by product request, #1189).
///
/// <para>
/// <b>This service owns no data access for the report itself.</b> It calls
/// <c>BackendConfigurationComplianceReportService</c> — the same three entry points
/// the screen calls, on the same shared <c>BuildCandidateSet</c> — and renders what
/// comes back. That is the whole point of #1162 having run server-side: the export
/// and the screen cannot disagree because they are the same computation. Not one
/// number in the output is recomputed here.
/// </para>
///
/// <para>
/// The pipeline is: <c>view model</c> →
/// <see cref="ComplianceExportDocumentBuilder"/> → <see cref="ComplianceExportDocument"/>
/// → one of two renderers. PDF is the docx renderer plus
/// <see cref="ComplianceExportPdfConverter"/>; there is no second document builder
/// for it and no client-side <c>html2pdf</c> anywhere in the diff.
/// </para>
///
/// <para>
/// <b>The only database reads here are two name lookups</b> — the property's
/// name when the filter names one property, and the board's name when it names
/// exactly one board. Both are display-only; each is resolved ONCE and reused for
/// the file name and for the Word/PDF page header (#1189).
/// </para>
/// </summary>
public class BackendConfigurationComplianceExportService(
    IBackendConfigurationComplianceReportService complianceReportService,
    IBackendConfigurationLocalizationService localizationService,
    IEFormCoreService coreHelper,
    BackendConfigurationPnDbContext backendConfigurationPnDbContext,
    ILogger<BackendConfigurationComplianceExportService> logger)
    : IBackendConfigurationComplianceExportService
{
    public const string ViewModeOverview = "overview";
    public const string ViewModeDetails = "details";
    public const string ViewModeReport = "report";

    public const string FormatCsv = "csv";
    public const string FormatPdf = "pdf";

    private const string MimeCsv = "text/csv";
    private const string MimePdf = "application/pdf";

    /// <summary>
    /// The separator inside the document's period line (U+2013, the mock-ups'
    /// <c>01.01.2026 – 05.09.2026</c>). The FILE NAME keeps hyphen-separated date
    /// parts — see <see cref="ComplianceExportFileNaming.BuildFileName"/>.
    /// </summary>
    private const string PeriodSeparator = " – ";

    /// <inheritdoc />
    public async Task<OperationDataResult<ComplianceExportFileModel>> Export(
        ComplianceReportExportRequestModel requestModel, CancellationToken cancellationToken = default)
    {
        try
        {
            if (requestModel == null)
            {
                return Fail("InvalidExportRequest");
            }

            var viewMode = Normalise(requestModel.ViewMode);
            var format = Normalise(requestModel.Format);

            // Neither is defaulted. A wrong default hands the user a file of the
            // wrong shape (or the wrong type) with nothing saying so.
            if (viewMode is not (ViewModeOverview or ViewModeDetails or ViewModeReport))
            {
                return Fail("InvalidExportRequest");
            }

            if (format is not (FormatCsv or FormatPdf))
            {
                return Fail("InvalidExportRequest");
            }

            var period = requestModel.DateFrom.Date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)
                         + PeriodSeparator
                         + requestModel.DateTo.Date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

            // Resolved once, here, and reused twice: for the page header the Word
            // writer puts on every page, and for the file name. Neither the
            // builders nor the file naming look anything up themselves.
            // Both label lookups run BEFORE the report call and are display-only reads,
            // so their ordering relative to the report query is harmless.
            var (propertyLabel, boardLabel) = await ResolveLabels(requestModel);

            ComplianceExportDocument document;
            switch (viewMode)
            {
                case ViewModeOverview:
                {
                    var result = await complianceReportService.Overview(new ComplianceReportOverviewRequestModel
                    {
                        PropertyId = requestModel.PropertyId,
                        BoardIds = requestModel.BoardIds ?? [],
                        TagIds = requestModel.TagIds ?? [],
                        SiteIds = requestModel.SiteIds ?? [],
                        DateFrom = requestModel.DateFrom,
                        DateTo = requestModel.DateTo
                        // No Status: Oversigt counts done and not-done together and
                        // its request model has no such property. The export must
                        // not invent one.
                    });

                    if (!result.Success)
                    {
                        return new OperationDataResult<ComplianceExportFileModel>(false, result.Message);
                    }

                    document = ComplianceExportDocumentBuilder.BuildOverview(
                        result.Model, period, localizationService);
                    break;
                }

                case ViewModeDetails:
                {
                    // PageSize 0 = unpaged: the export covers the full filtered set
                    // regardless of what is paginated on screen (#1169 §3,
                    // behaviour 1, deliberately kept). The service's 5000-row
                    // ceiling still applies — logged there — instead of pulling an
                    // unbounded set into memory.
                    var result = await complianceReportService.Index(BuildReportRequest(requestModel));

                    if (!result.Success)
                    {
                        return new OperationDataResult<ComplianceExportFileModel>(false, result.Message);
                    }

                    document = ComplianceExportDocumentBuilder.BuildDetails(
                        result.Model?.Entities, period, localizationService);
                    break;
                }

                default:
                {
                    var result = await complianceReportService.EformColumns(BuildReportRequest(requestModel));

                    if (!result.Success)
                    {
                        return new OperationDataResult<ComplianceExportFileModel>(false, result.Message);
                    }

                    document = ComplianceExportDocumentBuilder.BuildReport(
                        result.Model, period,
                        ShouldIncludeImageAppendix(viewMode, format, requestModel.IncludeImageAppendix),
                        localizationService);
                    break;
                }
            }

            document.PropertyLabel = propertyLabel;
            document.BoardLabel = boardLabel;

            var fileName = BuildFileName(requestModel, viewMode, format, propertyLabel, boardLabel);

            switch (format)
            {
                case FormatCsv:
                    return Ok(ComplianceExportCsvWriter.Write(document), fileName, MimeCsv);

                default:
                {
                    var core = await coreHelper.GetCore().ConfigureAwait(false);
                    var writer = new ComplianceExportWordWriter(localizationService, logger);
                    await using var docx = await writer.WriteAsync(document, core);

                    var pdfBytes = await ComplianceExportPdfConverter.ConvertAsync(
                        docx, logger, cancellationToken);
                    if (pdfBytes == null)
                    {
                        // soffice missing, wedged past the timeout, or non-zero
                        // exit. Everything is already logged by the converter; the
                        // user gets a 400 with the platform's existing
                        // report-generation message rather than a truncated file.
                        return Fail("ErrorWhileGeneratingReportFile");
                    }

                    return Ok(new MemoryStream(pdfBytes), fileName, MimePdf);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The user navigated away mid-download. Routine, not an application
            // error: it is NOT captured to Sentry and NOT turned into a 400 — the
            // cancellation propagates so the framework can abort the response.
            logger.LogInformation(
                "BackendConfigurationComplianceExportService.Export: cancelled by the client.");
            throw;
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            logger.LogError(e, "BackendConfigurationComplianceExportService.Export: {Message}", e.Message);
            return new OperationDataResult<ComplianceExportFileModel>(false,
                $"{localizationService.GetString("ErrorWhileGeneratingReportFile")}: {e.Message}");
        }
    }

    /// <summary>
    /// The image appendix gate, as its own predicate so it can be pinned for all
    /// six (view mode × format) combinations without rendering a document — the
    /// appendix is invisible in CSV output, so an inline <c>&amp;&amp;</c> here
    /// could be flipped to <c>||</c> without any renderer test noticing.
    ///
    /// <para>
    /// True for EXACTLY ONE combination: <c>report</c> + <c>pdf</c>, and only when
    /// the caller asked for it (#1169 §6 — opt-in, default off). A CSV cell cannot
    /// hold a photograph, so the flag is ignored for csv rather than silently
    /// producing nothing, and Oversigt and Detaljer carry no case images at all.
    /// </para>
    /// </summary>
    public static bool ShouldIncludeImageAppendix(string viewMode, string format, bool requested) =>
        requested && viewMode == ViewModeReport && format == FormatPdf;

    /// <summary>
    /// The export request's filter set as the report service's own request model.
    /// <c>PageSize = 0</c> is the unpaged path; <c>Sort</c> is left at its default
    /// so Detaljer exports in the same order the screen's first page uses
    /// (taskDate descending), and <c>EformColumns</c> ignores both regardless.
    /// </summary>
    private static ComplianceReportRequestModel BuildReportRequest(
        ComplianceReportExportRequestModel requestModel) => new()
    {
        PropertyId = requestModel.PropertyId,
        BoardIds = requestModel.BoardIds ?? [],
        TagIds = requestModel.TagIds ?? [],
        SiteIds = requestModel.SiteIds ?? [],
        Status = requestModel.Status,
        DateFrom = requestModel.DateFrom,
        DateTo = requestModel.DateTo,
        PageIndex = 0,
        PageSize = 0
    };

    /// <summary>
    /// The property and board display labels, resolved server-side from the ids on
    /// the request — the client sends no display strings, so a hand-edited request
    /// cannot inject a file name or a page header. "Alle" stands in for "no
    /// property filter" and for a multi-board selection, where no single board
    /// names the export.
    /// </summary>
    private async Task<(string PropertyLabel, string BoardLabel)> ResolveLabels(
        ComplianceReportExportRequestModel requestModel)
    {
        var allLabel = localizationService.GetString("All");

        var propertyLabel = allLabel;
        if (requestModel.PropertyId.HasValue)
        {
            var name = await backendConfigurationPnDbContext.Properties
                .Where(p => p.Id == requestModel.PropertyId.Value)
                // Same guard as the board lookup below: a soft-deleted property must
                // not name the export.
                .Where(p => p.WorkflowState != Constants.WorkflowStates.Removed)
                .Select(p => p.Name)
                .FirstOrDefaultAsync();
            if (!string.IsNullOrWhiteSpace(name)) propertyLabel = name;
        }

        var boardLabel = allLabel;
        var boardIds = requestModel.BoardIds ?? [];
        if (boardIds.Count == 1)
        {
            var name = await backendConfigurationPnDbContext.CalendarBoards
                .Where(b => b.Id == boardIds[0])
                .Where(b => b.WorkflowState != Constants.WorkflowStates.Removed)
                .Select(b => b.Name)
                .FirstOrDefaultAsync();
            if (!string.IsNullOrWhiteSpace(name)) boardLabel = name;
        }

        return (propertyLabel, boardLabel);
    }

    /// <summary>
    /// <c>{view}-{property}-{board}-{from}-{to}.{ext}</c> (#1169 §4), from the
    /// labels <see cref="ResolveLabels"/> already produced — no lookup happens here.
    /// </summary>
    private string BuildFileName(
        ComplianceReportExportRequestModel requestModel, string viewMode, string format,
        string propertyLabel, string boardLabel)
    {
        var viewLabel = viewMode switch
        {
            ViewModeOverview => localizationService.GetString("ComplianceOverview"),
            ViewModeDetails => localizationService.GetString("ComplianceDetails"),
            _ => localizationService.GetString("ComplianceReport")
        };

        return ComplianceExportFileNaming.BuildFileName(
            viewLabel, propertyLabel, boardLabel,
            requestModel.DateFrom.Date, requestModel.DateTo.Date, format);
    }

    private static string Normalise(string value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant();

    private OperationDataResult<ComplianceExportFileModel> Fail(string key) =>
        new(false, localizationService.GetString(key));

    private static OperationDataResult<ComplianceExportFileModel> Ok(
        Stream content, string fileName, string mimeType) =>
        new(true, new ComplianceExportFileModel
        {
            Content = content,
            FileName = fileName,
            MimeType = mimeType
        });
}
