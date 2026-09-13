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

using System.Reflection;
using BackendConfiguration.Pn.Infrastructure.Models.ComplianceReport;
using BackendConfiguration.Pn.Services.BackendConfigurationComplianceExportService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using NSubstitute;

/// <summary>
/// DB-backed coverage for <c>BackendConfigurationComplianceExportService.ResolveLabels</c>
/// — the two id → name lookups behind the compliance export's file name and its
/// Word/PDF page header (#1189).
///
/// <para>
/// <b>Why a second export fixture.</b> <see cref="ComplianceExportServiceTests"/>
/// deliberately passes <c>backendConfigurationPnDbContext</c> as <c>null</c> and
/// says so in its own summary: every request it builds names no property and never
/// names exactly one board, so neither lookup runs and no database is needed
/// there. That leaves <c>ResolveLabels</c> itself — the only data access the export
/// service owns — with no coverage at all. This fixture is that coverage, and it is
/// separate rather than folded into the other one precisely so the DB-free fixture
/// stays DB-free (a <c>TestBaseSetup</c> subclass spins its own MariaDB container).
/// </para>
///
/// <para>
/// <b>What regresses if this is missing.</b> Two failure modes, both silent: the
/// labels falling back to the localised "all" for a filter that really does name
/// one property or one board (a file called <c>Detaljer-Alle-Alle-…</c> and a page
/// header reading <c>Ejendom: Alle</c> for a single-property export), and — if the
/// <c>WorkflowState != Removed</c> guard is dropped — a SOFT-DELETED property or
/// board naming the export instead.
/// </para>
///
/// <para>
/// <b>How the docx half is reached without <c>soffice</c>.</b> The page header only
/// exists on the Word/PDF arm, and the PDF arm shells out to LibreOffice, which CI
/// does not have (<see cref="ComplianceExportWriterTests"/> states that boundary).
/// So each test drives the resolution twice over the SAME seeded rows: once through
/// the public <c>Export</c> on the CSV arm, whose <c>FileName</c> is built from the
/// resolved labels and nothing else, and once through <c>ResolveLabels</c> directly
/// (private, reached by reflection — the same idiom
/// <c>PushNotificationServiceTests</c> uses), feeding the resolved pair into the
/// real <see cref="ComplianceExportWordWriter"/> and reading the header part back
/// out of a real <c>WordprocessingDocument</c>. Both halves therefore assert real
/// output, not a mocked return value.
/// </para>
///
/// <para>
/// <b>Stated gap.</b> The one seam neither half covers is the two-line hand-off
/// inside <c>Export</c> (<c>document.PropertyLabel = propertyLabel;</c> /
/// <c>document.BoardLabel = boardLabel;</c>): a document rendered end to end can
/// only be observed on the PDF arm, which needs <c>soffice</c>. What is pinned is
/// that the values <c>ResolveLabels</c> produces reach the file name (through
/// <c>Export</c>) and that such values reach the header (through the writer);
/// <c>ComplianceExportWriterTests.Word_HeaderIsReferencedAndCarriesTheFilterLine</c>
/// pins the writer side independently.
/// </para>
///
/// <para>
/// <b>Localizer.</b> <see cref="DanishShellLocalizer"/> — a Danish shell, as in
/// <see cref="ComplianceExportWriterTests"/>, not the key-returning double the
/// other export fixtures use. Two reasons: the header line the user actually reads
/// is <c>Ejendom:</c> / <c>Kalender:</c> / <c>Periode:</c>, and the fallback
/// assertions then pin a real word ("Alle") rather than the spelling of the key
/// <c>All</c>, which would read as an accident if the key ever changed.
/// </para>
///
/// <para>
/// <c>TestBaseSetup.ResetDatabasePerTest</c> is false, so rows accumulate across
/// this fixture's tests. Nothing here counts or enumerates: every assertion is
/// scoped to the ids and the GUID-suffixed names this test seeded, so accumulated
/// rows (and the seed file's own properties) cannot affect it. No cleanup hook is
/// therefore needed.
/// </para>
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class ComplianceExportLabelResolutionTests : TestBaseSetup
{
    private const string AllLabel = "Alle";

    /// <summary>The Danish view label for <c>details</c>, used by every file name below.</summary>
    private const string DetailsLabel = "Detaljer";

    private const string SamplePeriod = "01.01.2026 – 31.03.2026";

    private static readonly DateTime DateFrom = new(2026, 1, 1);
    private static readonly DateTime DateTo = new(2026, 3, 31);

    /// <summary>The date half of every file name asserted here.</summary>
    private const string DateParts = "01.01.2026-31.03.2026";

    // ==================================================================
    // The property lookup
    // ==================================================================

    /// <summary>
    /// A live property id resolves to that property's NAME — in the download's file
    /// name and in the docx page header. This is the positive case the whole
    /// feature exists for: without the lookup both would say "Alle" for a
    /// single-property export and the user could not tell two downloads apart.
    /// </summary>
    [Test]
    public async Task ResolveLabels_LivePropertyNamesTheFileAndTheHeader()
    {
        var name = UniqueName("Ejendom Nord");
        var property = await SeedProperty(name);

        var request = Request(propertyId: property.Id);
        var result = await BuildService().Export(request);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Model.FileName,
            Is.EqualTo($"{DetailsLabel}-{name}-{AllLabel}-{DateParts}.csv"));

        var (propertyLabel, boardLabel) = await ResolveLabels(request);
        Assert.That(propertyLabel, Is.EqualTo(name));
        Assert.That(boardLabel, Is.EqualTo(AllLabel));

        var header = await HeaderTextFor(propertyLabel, boardLabel);
        Assert.That(header, Does.Contain($"Ejendom: {name}"));
        Assert.That(header, Does.Contain($"Kalender: {AllLabel}"));
    }

    /// <summary>
    /// <b>The <c>WorkflowState != Removed</c> guard.</b> A SOFT-DELETED property
    /// must not name the export: the row is still in the table and still matches on
    /// id, so dropping the guard would put a deleted property's name on a live
    /// user's download and in the header of every page of the PDF. The filter is
    /// not rejected — the export still runs — it simply falls back to "Alle", and
    /// the removed name appears NOWHERE in either output.
    /// </summary>
    [Test]
    public async Task ResolveLabels_SoftDeletedPropertyFallsBackToAllAndNeverNamesTheExport()
    {
        var name = UniqueName("Ejendom Slettet");
        var property = await SeedProperty(name);
        await property.Delete(BackendConfigurationPnDbContext!);

        // The row is still there, still matching on id — only its workflow state changed.
        Assert.That(
            BackendConfigurationPnDbContext!.Properties.Single(p => p.Id == property.Id).WorkflowState,
            Is.EqualTo(Constants.WorkflowStates.Removed));

        var request = Request(propertyId: property.Id);
        var result = await BuildService().Export(request);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Model.FileName,
            Is.EqualTo($"{DetailsLabel}-{AllLabel}-{AllLabel}-{DateParts}.csv"));
        Assert.That(result.Model.FileName, Does.Not.Contain(name));

        var (propertyLabel, _) = await ResolveLabels(request);
        Assert.That(propertyLabel, Is.EqualTo(AllLabel));

        var header = await HeaderTextFor(propertyLabel, AllLabel);
        Assert.That(header, Does.Contain($"Ejendom: {AllLabel}"));
        Assert.That(header, Does.Not.Contain(name));
    }

    /// <summary>
    /// A property id that matches no row at all — a stale bookmark, a property
    /// hard-deleted since the page was loaded — falls back to "Alle" rather than
    /// producing an empty part (<c>Detaljer--Alle-…</c>) or throwing. The lookup is
    /// <c>FirstOrDefaultAsync</c> plus a blank check, and this pins the blank check.
    /// </summary>
    [Test]
    public async Task ResolveLabels_UnknownPropertyIdFallsBackToAll()
    {
        var request = Request(propertyId: int.MaxValue);

        var result = await BuildService().Export(request);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Model.FileName,
            Is.EqualTo($"{DetailsLabel}-{AllLabel}-{AllLabel}-{DateParts}.csv"));
    }

    // ==================================================================
    // The board lookup
    // ==================================================================

    /// <summary>
    /// EXACTLY ONE board id resolves to that board's name. The count is the whole
    /// rule — one board names the export, anything else does not — so this is the
    /// only arm where a database read happens for the board at all.
    /// </summary>
    [Test]
    public async Task ResolveLabels_SingleBoardNamesTheFileAndTheHeader()
    {
        var propertyName = UniqueName("Ejendom Med Kalender");
        var property = await SeedProperty(propertyName);
        var boardName = UniqueName("Miljøtilsyn");
        var board = await SeedBoard(property.Id, boardName);

        var request = Request(propertyId: property.Id, boardIds: [board.Id]);
        var result = await BuildService().Export(request);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Model.FileName,
            Is.EqualTo($"{DetailsLabel}-{propertyName}-{boardName}-{DateParts}.csv"));

        var (propertyLabel, boardLabel) = await ResolveLabels(request);
        Assert.That(boardLabel, Is.EqualTo(boardName));

        var header = await HeaderTextFor(propertyLabel, boardLabel);
        Assert.That(header, Does.Contain($"Kalender: {boardName}"));
        Assert.That(header, Does.Contain($"Ejendom: {propertyName}"));
    }

    /// <summary>
    /// The board lookup carries the SAME soft-delete guard as the property one, and
    /// it is a separate <c>Where</c> clause in a separate query — removing one does
    /// not fail the other's test. A removed board therefore never names the export
    /// either.
    /// </summary>
    [Test]
    public async Task ResolveLabels_SoftDeletedBoardFallsBackToAll()
    {
        var property = await SeedProperty(UniqueName("Ejendom Slettet Kalender"));
        var boardName = UniqueName("Slettet Kalender");
        var board = await SeedBoard(property.Id, boardName);
        await board.Delete(BackendConfigurationPnDbContext!);

        var request = Request(boardIds: [board.Id]);
        var result = await BuildService().Export(request);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Model.FileName, Does.Not.Contain(boardName));
        Assert.That(result.Model.FileName,
            Is.EqualTo($"{DetailsLabel}-{AllLabel}-{AllLabel}-{DateParts}.csv"));

        var (_, boardLabel) = await ResolveLabels(request);
        Assert.That(boardLabel, Is.EqualTo(AllLabel));

        var header = await HeaderTextFor(AllLabel, boardLabel);
        Assert.That(header, Does.Contain($"Kalender: {AllLabel}"));
        Assert.That(header, Does.Not.Contain(boardName));
    }

    /// <summary>
    /// TWO board ids fall back to "Alle": no single board names the export, and
    /// neither name may leak into the file name or the header — a lookup written as
    /// "take the first id" instead of "only when there is exactly one" would put an
    /// arbitrary one of them there, which is exactly what a user cannot verify.
    /// Both boards are LIVE, so only the count can produce the fallback.
    /// </summary>
    [Test]
    public async Task ResolveLabels_TwoBoardsFallBackToAllAndNameNeither()
    {
        var property = await SeedProperty(UniqueName("Ejendom To Kalendere"));
        var firstName = UniqueName("Kalender En");
        var secondName = UniqueName("Kalender To");
        var first = await SeedBoard(property.Id, firstName);
        var second = await SeedBoard(property.Id, secondName);

        var request = Request(boardIds: [first.Id, second.Id]);
        var result = await BuildService().Export(request);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Model.FileName,
            Is.EqualTo($"{DetailsLabel}-{AllLabel}-{AllLabel}-{DateParts}.csv"));
        Assert.That(result.Model.FileName, Does.Not.Contain(firstName));
        Assert.That(result.Model.FileName, Does.Not.Contain(secondName));

        var (_, boardLabel) = await ResolveLabels(request);
        Assert.That(boardLabel, Is.EqualTo(AllLabel));

        var header = await HeaderTextFor(AllLabel, boardLabel);
        Assert.That(header, Does.Contain($"Kalender: {AllLabel}"));
        Assert.That(header, Does.Not.Contain(firstName));
        Assert.That(header, Does.Not.Contain(secondName));
    }

    /// <summary>
    /// ZERO board ids — an empty list and a null one, which the client sends
    /// interchangeably — fall back to "Alle" as well, with a live board present in
    /// the database to make sure the fallback comes from the request and not from
    /// an empty table.
    /// </summary>
    [Test]
    public async Task ResolveLabels_NoBoardIdsFallBackToAll([Values] bool nullInsteadOfEmpty)
    {
        var property = await SeedProperty(UniqueName("Ejendom Uden Kalenderfilter"));
        var boardName = UniqueName("Ufiltreret Kalender");
        await SeedBoard(property.Id, boardName);

        var request = Request(boardIds: nullInsteadOfEmpty ? null : new List<int>());
        var result = await BuildService().Export(request);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Model.FileName,
            Is.EqualTo($"{DetailsLabel}-{AllLabel}-{AllLabel}-{DateParts}.csv"));
        Assert.That(result.Model.FileName, Does.Not.Contain(boardName));

        var (_, boardLabel) = await ResolveLabels(request);
        Assert.That(boardLabel, Is.EqualTo(AllLabel));
    }

    // ==================================================================
    // Seeding
    // ==================================================================

    /// <summary>
    /// GUID-suffixed so the assertions stay valid with
    /// <c>ResetDatabasePerTest == false</c> (rows from earlier tests in this
    /// fixture are still in the table) and so a <c>Does.Not.Contain</c> cannot pass
    /// or fail because of some other row's name.
    /// </summary>
    private static string UniqueName(string prefix) => $"{prefix} {Guid.NewGuid():N}";

    private async Task<Property> SeedProperty(string name)
    {
        var property = new Property
        {
            Name = name,
            ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await property.Create(BackendConfigurationPnDbContext!);
        return property;
    }

    private async Task<CalendarBoard> SeedBoard(int propertyId, string name)
    {
        var board = new CalendarBoard
        {
            Name = name,
            Color = "#111111",
            PropertyId = propertyId,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await board.Create(BackendConfigurationPnDbContext!);
        return board;
    }

    // ==================================================================
    // Wiring
    // ==================================================================

    /// <summary>
    /// The REAL export service on the REAL seeded context. Only the report service
    /// is a stub — the export owns no data access for the report itself, so there
    /// is nothing to seed for it and an empty result set is enough to reach the file
    /// naming. The SDK core helper is <c>null</c> on purpose: every test here takes
    /// the CSV arm, and a null reference from it would mean the PDF arm had started
    /// running unconditionally.
    /// </summary>
    private BackendConfigurationComplianceExportService BuildService() =>
        new(StubReportService(),
            new DanishShellLocalizer(),
            null!,
            BackendConfigurationPnDbContext!,
            TestContextLogger<BackendConfigurationComplianceExportService>.Instance);

    private static IBackendConfigurationComplianceReportService StubReportService()
    {
        var reportService = Substitute.For<IBackendConfigurationComplianceReportService>();

        reportService.Index(Arg.Any<ComplianceReportRequestModel>()).Returns(
            Task.FromResult(new OperationDataResult<ComplianceReportPagedModel>(
                true, new ComplianceReportPagedModel { Entities = [] })));

        return reportService;
    }

    private static ComplianceReportExportRequestModel Request(
        int? propertyId = null, List<int>? boardIds = null) => new()
    {
        ViewMode = BackendConfigurationComplianceExportService.ViewModeDetails,
        Format = BackendConfigurationComplianceExportService.FormatCsv,
        PropertyId = propertyId,
        BoardIds = boardIds!,
        DateFrom = DateFrom,
        DateTo = DateTo
    };

    /// <summary>
    /// <c>ResolveLabels</c> is private — it has no reason to be public, and the two
    /// callers that consume it are both inside <c>Export</c>. Reflection here is the
    /// same trade <c>PushNotificationServiceTests</c> makes: <c>GetMethod</c> with a
    /// hard failure if the name ever changes, rather than widening production
    /// visibility for a test. The returned tuple's element names are erased at
    /// runtime, so the cast is to the plain <c>Task&lt;(string, string)&gt;</c>.
    /// </summary>
    private static readonly MethodInfo ResolveLabelsMethod =
        typeof(BackendConfigurationComplianceExportService)
            .GetMethod("ResolveLabels", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException(
            "BackendConfigurationComplianceExportService.ResolveLabels was not found — "
            + "it was renamed or its visibility changed.");

    private async Task<(string PropertyLabel, string BoardLabel)> ResolveLabels(
        ComplianceReportExportRequestModel request)
    {
        var invocation = (Task<(string, string)>)ResolveLabelsMethod.Invoke(
            BuildService(), [request])!;
        return await invocation;
    }

    /// <summary>
    /// Renders a real docx through the real Word writer with the two resolved
    /// labels on it and hands back the page header's text. No SDK core is needed —
    /// the core is only reached for appendix images, which a Detaljer document has
    /// none of — and no <c>soffice</c>, because the assertion stops at the docx
    /// boundary (CI has no LibreOffice).
    /// </summary>
    private static async Task<string> HeaderTextFor(string propertyLabel, string boardLabel)
    {
        var document = new ComplianceExportDocument
        {
            Title = DetailsLabel,
            Period = SamplePeriod,
            PropertyLabel = propertyLabel,
            BoardLabel = boardLabel,
            Tables =
            [
                new ComplianceExportTable
                {
                    Columns = [new ComplianceExportColumn { Header = "Dato" }],
                    Rows =
                    [
                        new ComplianceExportRow { Cells = [ComplianceExportCell.FromText("09.03.2026")] }
                    ]
                }
            ]
        };

        var writer = new ComplianceExportWordWriter(new DanishShellLocalizer(), TestContextLogger.Instance);
        await using var stream = await writer.WriteAsync(document, null);
        using var word = WordprocessingDocument.Open(stream, false);

        return word.MainDocumentPart!.HeaderParts.Single().Header!.InnerText;
    }

    /// <summary>
    /// Danish for the handful of keys this fixture's output can contain — the page
    /// header's three labels, the "All" fallback and the three view labels the file
    /// name uses. Every other key comes back as itself, like the shared
    /// key-returning double. Deliberately a Danish shell rather than that double:
    /// <c>Ejendom:</c> / <c>Kalender:</c> / <c>Periode:</c> and <c>Alle</c> are what
    /// the user reads, and asserting them makes a fallback regression read as a
    /// wrong WORD rather than as a wrong key spelling.
    /// </summary>
    private sealed class DanishShellLocalizer : IBackendConfigurationLocalizationService
    {
        private static readonly Dictionary<string, string> Danish = new()
        {
            ["All"] = AllLabel,
            ["Property"] = "Ejendom",
            ["CalendarBoard"] = "Kalender",
            ["Period"] = "Periode",
            ["ComplianceOverview"] = "Oversigt",
            ["ComplianceDetails"] = DetailsLabel,
            ["ComplianceReport"] = "Rapport",
            ["ComplianceDetailsTitle"] = "Compliance",
            ["Date"] = "Dato",
            ["StartTime"] = "Kl.",
            ["Task"] = "Opgave",
            ["Worker"] = "Medarbejder",
            ["TagsPlain"] = "Tags",
            ["Status"] = "Status",
            ["Done"] = "Udført",
            ["NotDone"] = "Ikke udført"
        };

        public string GetString(string key) => Danish.TryGetValue(key, out var value) ? value : key;

        public string GetString(string format, params object[] args) => string.Format(GetString(format), args);

        /// <summary>
        /// Mirrors the real service: formats ONLY when arguments are supplied, so a
        /// value containing a <c>{0}</c> does not throw on an argument-less call.
        /// </summary>
        public string GetStringWithFormat(string format, params object[] args)
        {
            var value = GetString(format);
            return args?.Length > 0 ? string.Format(value, args) : value;
        }
    }
}
