/*
The MIT License (MIT)

Copyright (c) 2007 - 2026 Microting A/S

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.
*/

namespace BackendConfiguration.Pn.Integration.Test;

using System.Text;
using BackendConfiguration.Pn.Infrastructure.Models.ComplianceReport;
using BackendConfiguration.Pn.Services.BackendConfigurationComplianceExportService;
using BackendConfiguration.Pn.Services.BackendConfigurationComplianceReportService;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using NSubstitute;

/// <summary>
/// Coverage for <see cref="BackendConfigurationComplianceExportService"/> itself —
/// the ORCHESTRATION between the request model, the report service and the three
/// renderers (#1169). The document builder and the renderers have their own
/// fixtures; this one is about what the service decides.
///
/// <para>
/// <b>No database and no container.</b> The report service is an NSubstitute
/// stub — the whole point of the design is that the export owns no data access for
/// the report, so there is nothing to seed for it. The <c>BackendConfigurationPnDbContext</c>
/// is only ever touched by the two FILE-NAME lookups, and only when the request
/// names a property or names exactly one board; every request built here does
/// neither, so the context is passed as <c>null</c> and demonstrably never
/// dereferenced. The SDK core is likewise only reached on the PDF path, which none
/// of these tests take.
/// </para>
///
/// <para>
/// <b>Stated gap.</b> Nothing here covers the PDF arm end to end (it needs
/// <c>soffice</c>, which CI does not have) or the two id → name lookups (they need
/// a seeded database). The appendix gate that only the PDF arm can observe is
/// pinned through <see cref="BackendConfigurationComplianceExportService.ShouldIncludeImageAppendix"/>
/// instead, which is why that predicate is named and public rather than an inline
/// <c>&amp;&amp;</c>: the appendix is invisible in CSV and XLSX output, so no
/// renderer assertion could tell a <c>&amp;&amp;</c> from a <c>||</c> there.
/// </para>
///
/// <para>
/// <c>BackendConfigurationLocalizationService</c> is the key-returning test double
/// declared in <c>BackendConfigurationAssignmentWorkerServiceHelperTest.cs</c>, so
/// the assertions below pin the KEY the service asks for rather than one locale's
/// translation of it.
/// </para>
/// </summary>
[Parallelizable(ParallelScope.All)]
[TestFixture]
public class ComplianceExportServiceTests
{
    // ==================================================================
    // The image-appendix gate
    // ==================================================================

    /// <summary>
    /// The appendix is on for EXACTLY ONE of the nine (view mode × format)
    /// combinations — <c>report</c> + <c>pdf</c> — and only when the caller asked
    /// for it (#1169 §6: opt-in, default off). A spreadsheet cell cannot hold a
    /// photograph, and Oversigt and Detaljer carry no case images at all.
    /// </summary>
    [Test]
    [TestCase("report", "pdf", true, ExpectedResult = true)]
    [TestCase("report", "pdf", false, ExpectedResult = false)]
    [TestCase("report", "csv", true, ExpectedResult = false)]
    [TestCase("report", "xlsx", true, ExpectedResult = false)]
    [TestCase("details", "pdf", true, ExpectedResult = false)]
    [TestCase("details", "csv", true, ExpectedResult = false)]
    [TestCase("details", "xlsx", true, ExpectedResult = false)]
    [TestCase("overview", "pdf", true, ExpectedResult = false)]
    [TestCase("overview", "csv", true, ExpectedResult = false)]
    [TestCase("overview", "xlsx", true, ExpectedResult = false)]
    public bool AppendixGate_IsOnOnlyForReportPlusPdfAndOnlyWhenAskedFor(
        string viewMode, string format, bool requested) =>
        BackendConfigurationComplianceExportService.ShouldIncludeImageAppendix(
            viewMode, format, requested);

    // ==================================================================
    // Validation — "never silently defaulted"
    // ==================================================================

    /// <summary>
    /// An unknown view mode is REJECTED, not defaulted. A wrong default hands the
    /// user a file of the wrong shape with nothing saying so. The report service is
    /// never called for a request that does not validate.
    /// </summary>
    [Test]
    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("summary")]
    [TestCase("compliance")]
    public async Task Export_RejectsAnUnknownViewMode(string? viewMode)
    {
        var reportService = Substitute.For<IBackendConfigurationComplianceReportService>();
        var service = BuildService(reportService);

        var result = await service.Export(Request(viewMode!, "csv"));

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo("InvalidExportRequest"));
        await reportService.DidNotReceiveWithAnyArgs().Index(default!);
        await reportService.DidNotReceiveWithAnyArgs().Overview(default!);
        await reportService.DidNotReceiveWithAnyArgs().EformColumns(default!);
    }

    /// <summary>
    /// An unknown format is rejected on the same terms — including <c>docx</c>,
    /// which the PDF arm produces internally but which is deliberately not an
    /// offered format (#1169 names exactly three).
    /// </summary>
    [Test]
    [TestCase(null)]
    [TestCase("")]
    [TestCase("docx")]
    [TestCase("xls")]
    [TestCase("json")]
    public async Task Export_RejectsAnUnknownFormat(string? format)
    {
        var reportService = Substitute.For<IBackendConfigurationComplianceReportService>();
        var service = BuildService(reportService);

        var result = await service.Export(Request("details", format!));

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo("InvalidExportRequest"));
        await reportService.DidNotReceiveWithAnyArgs().Index(default!);
    }

    /// <summary>
    /// A null body reaches the service as a null request model (the controller does
    /// not guard it), and that is a rejection rather than a
    /// <c>NullReferenceException</c> turned into a 500 by the catch-all.
    /// </summary>
    [Test]
    public async Task Export_RejectsANullRequest()
    {
        var service = BuildService(Substitute.For<IBackendConfigurationComplianceReportService>());

        var result = await service.Export(null!);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo("InvalidExportRequest"));
    }

    /// <summary>
    /// Both fields are matched case-insensitively and after trimming, as the
    /// request contract promises — a client that sends <c>"Details"</c> or
    /// <c>"CSV "</c> gets its file, not a 400.
    /// </summary>
    [Test]
    [TestCase("Details", "CSV")]
    [TestCase(" details ", " csv ")]
    [TestCase("DETAILS", "Csv")]
    public async Task Export_MatchesViewModeAndFormatCaseInsensitively(string viewMode, string format)
    {
        var reportService = StubReportService();
        var service = BuildService(reportService);

        var result = await service.Export(Request(viewMode, format));

        Assert.That(result.Success, Is.True);
        Assert.That(result.Model.MimeType, Is.EqualTo("text/csv"));
    }

    // ==================================================================
    // The request handed to the report service
    // ==================================================================

    /// <summary>
    /// <b><c>PageSize = 0</c>.</b> The single most load-bearing line for #1169's
    /// "the export covers the full filtered set" acceptance criterion: the screen
    /// is paginated, the export is not. A non-zero page size here would silently
    /// truncate every Detaljer export to one page, and the file would look
    /// perfectly well-formed.
    /// </summary>
    [Test]
    public async Task Export_Details_CallsIndexUnpaged()
    {
        ComplianceReportRequestModel? captured = null;
        var reportService = StubReportService(onIndex: request => captured = request);

        var result = await BuildService(reportService).Export(Request("details", "csv"));

        Assert.That(result.Success, Is.True);
        Assert.That(captured, Is.Not.Null);
        Assert.That(captured!.PageSize, Is.EqualTo(0));
        Assert.That(captured.PageIndex, Is.EqualTo(0));
    }

    /// <summary>
    /// Rapport goes through the same unpaged request. <c>EformColumns</c> ignores
    /// paging itself, but the export must not be the reason it is ignored.
    /// </summary>
    [Test]
    public async Task Export_Report_CallsEformColumnsUnpaged()
    {
        ComplianceReportRequestModel? captured = null;
        var reportService = StubReportService(onEformColumns: request => captured = request);

        var result = await BuildService(reportService).Export(Request("report", "csv"));

        Assert.That(result.Success, Is.True);
        Assert.That(captured, Is.Not.Null);
        Assert.That(captured!.PageSize, Is.EqualTo(0));
    }

    /// <summary>
    /// The whole filter set travels through unchanged — the export re-runs the
    /// screen's filters rather than approximating them. Null collections normalise
    /// to empty ones so the report service never has to guard.
    /// </summary>
    [Test]
    public async Task Export_PassesTheFilterSetThroughUnchanged()
    {
        ComplianceReportRequestModel? captured = null;
        var reportService = StubReportService(onIndex: request => captured = request);

        var requestModel = Request("details", "csv");
        requestModel.PropertyId = null;
        requestModel.BoardIds = [7, 8];
        requestModel.TagIds = [3];
        requestModel.SiteIds = null!;
        requestModel.Status = "done";

        await BuildService(reportService).Export(requestModel);

        Assert.That(captured, Is.Not.Null);
        Assert.That(captured!.BoardIds, Is.EqualTo(new[] { 7, 8 }));
        Assert.That(captured.TagIds, Is.EqualTo(new[] { 3 }));
        Assert.That(captured.SiteIds, Is.Empty);
        Assert.That(captured.Status, Is.EqualTo("done"));
        Assert.That(captured.DateFrom, Is.EqualTo(requestModel.DateFrom));
        Assert.That(captured.DateTo, Is.EqualTo(requestModel.DateTo));
    }

    /// <summary>
    /// Oversigt counts done and not-done together and its request model has NO
    /// <c>Status</c> property at all, so the export must not invent one. This pins
    /// that the Oversigt arm calls <c>Overview</c> — not <c>Index</c> — with the
    /// filter set and nothing else.
    /// </summary>
    [Test]
    public async Task Export_Overview_CallsOverviewAndCarriesNoStatus()
    {
        ComplianceReportOverviewRequestModel? captured = null;
        var reportService = StubReportService(onOverview: request => captured = request);

        var requestModel = Request("overview", "csv");
        requestModel.Status = "done";

        var result = await BuildService(reportService).Export(requestModel);

        Assert.That(result.Success, Is.True);
        Assert.That(captured, Is.Not.Null);
        Assert.That(captured!.DateFrom, Is.EqualTo(requestModel.DateFrom));
        await reportService.DidNotReceiveWithAnyArgs().Index(default!);
        Assert.That(typeof(ComplianceReportOverviewRequestModel).GetProperty("Status"), Is.Null);
    }

    // ==================================================================
    // Failure pass-through
    // ==================================================================

    /// <summary>
    /// A failure inside the report service is passed through VERBATIM — its own
    /// message, not a generic export error. The user is told the actual reason, and
    /// no file is produced from a half-populated model.
    /// </summary>
    [Test]
    [TestCase("overview")]
    [TestCase("details")]
    [TestCase("report")]
    public async Task Export_PassesAReportServiceFailureThrough(string viewMode)
    {
        var reportService = FailingReportService("the underlying query failed");

        var result = await BuildService(reportService).Export(Request(viewMode, "csv"));

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo("the underlying query failed"));
        Assert.That(result.Model, Is.Null);
    }

    // ==================================================================
    // File naming
    // ==================================================================

    /// <summary>
    /// With no property filter and no single board, both parts fall back to the
    /// localised "All" — and the DATABASE IS NEVER TOUCHED, which is what makes the
    /// null context above legal. The view label is the view's own key, so the three
    /// view modes produce three different names.
    /// </summary>
    [Test]
    [TestCase("overview", "csv", "ComplianceOverview-All-All-01.01.2026-31.03.2026.csv")]
    [TestCase("details", "xlsx", "ComplianceDetails-All-All-01.01.2026-31.03.2026.xlsx")]
    [TestCase("report", "csv", "ComplianceReport-All-All-01.01.2026-31.03.2026.csv")]
    public async Task Export_FileNameUsesTheViewLabelAndTheAllFallbacks(
        string viewMode, string format, string expected)
    {
        var result = await BuildService(StubReportService()).Export(Request(viewMode, format));

        Assert.That(result.Success, Is.True);
        Assert.That(result.Model.FileName, Is.EqualTo(expected));
    }

    /// <summary>
    /// A MULTI-board selection also falls back to "All": no single board names the
    /// file, and the lookup is skipped entirely — again without a database.
    /// </summary>
    [Test]
    public async Task Export_FileNameFallsBackToAllForAMultiBoardSelection()
    {
        var requestModel = Request("details", "csv");
        requestModel.BoardIds = [4, 9];

        var result = await BuildService(StubReportService()).Export(requestModel);

        Assert.That(result.Model.FileName, Is.EqualTo("ComplianceDetails-All-All-01.01.2026-31.03.2026.csv"));
    }

    // ==================================================================
    // The rendered payload
    // ==================================================================

    /// <summary>
    /// The MIME type follows the format, and the stream is rewound and non-empty —
    /// the controller copies it straight to the response body, so a stream left at
    /// its end would download as a 0-byte file.
    /// </summary>
    [Test]
    [TestCase("csv", "text/csv")]
    [TestCase("xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    public async Task Export_ReturnsARewoundStreamWithTheMatchingMimeType(string format, string mime)
    {
        var result = await BuildService(StubReportService()).Export(Request("details", format));

        Assert.That(result.Success, Is.True);
        Assert.That(result.Model.MimeType, Is.EqualTo(mime));
        Assert.That(result.Model.Content.Position, Is.EqualTo(0));
        Assert.That(result.Model.Content.Length, Is.GreaterThan(0));
    }

    /// <summary>
    /// End to end for the CSV arm: the file the service hands back starts with the
    /// UTF-8 BOM and its FIRST line is the header row — no title, no period, no
    /// blank line — so "use first row as header" works without a manual skip.
    /// </summary>
    [Test]
    public async Task Export_CsvStartsWithTheBomAndThenTheHeaderRow()
    {
        var result = await BuildService(StubReportService()).Export(Request("details", "csv"));

        using var memory = new MemoryStream();
        await result.Model.Content.CopyToAsync(memory);
        var bytes = memory.ToArray();

        Assert.That(bytes[0], Is.EqualTo(0xEF));
        Assert.That(bytes[1], Is.EqualTo(0xBB));
        Assert.That(bytes[2], Is.EqualTo(0xBF));

        var firstLine = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3).Split("\r\n")[0];
        Assert.That(firstLine, Is.EqualTo(
            "Date;Property;CalendarBoard;StartTime;Task;Worker;Tags;Status"));
    }

    // ==================================================================
    // Helpers
    // ==================================================================

    /// <summary>
    /// The DbContext and the SDK core are <c>null</c> deliberately: every request
    /// built here leaves the property filter empty and never selects exactly one
    /// board, so <c>BuildFileName</c> takes neither lookup, and no test takes the
    /// PDF arm. A null-reference here would be a real regression — a name lookup
    /// that started running unconditionally — not a broken test.
    /// </summary>
    private static BackendConfigurationComplianceExportService BuildService(
        IBackendConfigurationComplianceReportService reportService) =>
        new(reportService,
            new BackendConfigurationLocalizationService(),
            null!,
            null!,
            NullLogger<BackendConfigurationComplianceExportService>.Instance);

    private static ComplianceReportExportRequestModel Request(string viewMode, string format) => new()
    {
        ViewMode = viewMode,
        Format = format,
        DateFrom = new DateTime(2026, 1, 1),
        DateTo = new DateTime(2026, 3, 31)
    };

    private static IBackendConfigurationComplianceReportService StubReportService(
        Action<ComplianceReportRequestModel>? onIndex = null,
        Action<ComplianceReportRequestModel>? onEformColumns = null,
        Action<ComplianceReportOverviewRequestModel>? onOverview = null)
    {
        var reportService = Substitute.For<IBackendConfigurationComplianceReportService>();

        reportService.Index(Arg.Any<ComplianceReportRequestModel>()).Returns(call =>
        {
            onIndex?.Invoke(call.Arg<ComplianceReportRequestModel>());
            return Task.FromResult(new OperationDataResult<ComplianceReportPagedModel>(
                true, new ComplianceReportPagedModel { Entities = [] }));
        });

        reportService.EformColumns(Arg.Any<ComplianceReportRequestModel>()).Returns(call =>
        {
            onEformColumns?.Invoke(call.Arg<ComplianceReportRequestModel>());
            return Task.FromResult(new OperationDataResult<List<ComplianceReportTagGroupModel>>(
                true, []));
        });

        reportService.Overview(Arg.Any<ComplianceReportOverviewRequestModel>()).Returns(call =>
        {
            onOverview?.Invoke(call.Arg<ComplianceReportOverviewRequestModel>());
            return Task.FromResult(new OperationDataResult<ComplianceReportOverviewModel>(
                true, new ComplianceReportOverviewModel()));
        });

        return reportService;
    }

    private static IBackendConfigurationComplianceReportService FailingReportService(string message)
    {
        var reportService = Substitute.For<IBackendConfigurationComplianceReportService>();

        reportService.Index(Arg.Any<ComplianceReportRequestModel>()).Returns(
            Task.FromResult(new OperationDataResult<ComplianceReportPagedModel>(false, message)));
        reportService.EformColumns(Arg.Any<ComplianceReportRequestModel>()).Returns(
            Task.FromResult(new OperationDataResult<List<ComplianceReportTagGroupModel>>(false, message)));
        reportService.Overview(Arg.Any<ComplianceReportOverviewRequestModel>()).Returns(
            Task.FromResult(new OperationDataResult<ComplianceReportOverviewModel>(false, message)));

        return reportService;
    }
}
