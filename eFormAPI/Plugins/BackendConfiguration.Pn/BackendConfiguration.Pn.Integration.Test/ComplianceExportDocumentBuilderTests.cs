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

using BackendConfiguration.Pn.Infrastructure.Models.ComplianceReport;
using BackendConfiguration.Pn.Services.BackendConfigurationComplianceExportService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;

/// <summary>
/// Coverage for <see cref="ComplianceExportDocumentBuilder"/> — the mapping from
/// each of the three compliance view models onto the export intermediate (#1169 §2).
///
/// <para>
/// <b>No database and no container.</b> The builder is pure, which is the point of
/// splitting it out: the column sets, the en-dash empty glyph, the weighted totals
/// row, the typed date column and the keyed answer cells are all assertable from
/// hand-built read models. The read models themselves are already covered by the
/// 86 tests on <c>Index</c>, <c>Overview</c> and <c>EformColumns</c>, none of which
/// this work touches.
/// </para>
///
/// <para>
/// <c>BackendConfigurationLocalizationService</c> here is the test double declared
/// in <c>BackendConfigurationAssignmentWorkerServiceHelperTest.cs</c>: it returns
/// the KEY for every lookup, so a header assertion below pins the key the builder
/// asks for rather than one locale's translation of it. The Oversigt cases that
/// #1190 pins by their DANISH text (the mock-up's literal
/// <c>Virksomhed | Overskredet | Compliance %</c> and <c>Compliance oversigt</c>)
/// and the Detaljer cases #1191 pins the same way (<c>Compliance</c>,
/// <c>… | Tags | Status</c>) use <see cref="DanishExportLocalizer"/> instead,
/// which answers the Oversigt and Detaljer keys with the values in
/// <c>Resources/localization.json</c>.
/// </para>
/// </summary>
[Parallelizable(ParallelScope.All)]
[TestFixture]
public class ComplianceExportDocumentBuilderTests
{
    private readonly BackendConfigurationLocalizationService _localization = new();

    private readonly DanishExportLocalizer _danish = new();

    private const string Dash = "–"; // U+2013, the single empty-cell glyph

    // ==================================================================
    // Oversigt
    // ==================================================================

    /// <summary>
    /// The Oversigt column set is exactly three columns. "Opgaver i alt" and
    /// "Udført" are computed by the service and deliberately NOT rendered, and a
    /// prototype test pins their absence — so the export must not leak them in
    /// either.
    ///
    /// <para>
    /// The first header is the NEW <c>Company</c> key (#1190), not <c>Property</c>:
    /// the export says "Virksomhed" while the screen keeps "Ejendom". The
    /// <c>Property</c> key is untouched because Detaljer, Rapport and the page
    /// header still read it.
    /// </para>
    /// </summary>
    [Test]
    public void Overview_HasExactlyThreeColumns_AndNoTotalOrDoneColumn()
    {
        var document = ComplianceExportDocumentBuilder.BuildOverview(
            new ComplianceReportOverviewModel(), "01.01.2026 - 31.03.2026", _localization);

        Assert.That(document.Tables, Has.Count.EqualTo(1));
        var headers = document.Tables[0].Columns.Select(c => c.Header).ToList();
        Assert.That(headers, Is.EqualTo(new[] { "Company", "Overdue", "CompliancePercentage" }));
    }

    /// <summary>
    /// The mock-up's literal header row and title (#1190): <c>Virksomhed |
    /// Overskredet | Compliance %</c> under <c>Compliance oversigt</c>. Pinned by
    /// the Danish TEXT rather than the keys, because the header wording is the
    /// whole point of the issue — and because it must be "Virksomhed" even though
    /// the on-screen table says "Ejendom".
    /// </summary>
    [Test]
    public void Overview_DanishHeaderIsVirksomhedOverskredetCompliancePercent_AndTitleIsComplianceOversigt()
    {
        var document = ComplianceExportDocumentBuilder.BuildOverview(
            new ComplianceReportOverviewModel(), "p", _danish);

        Assert.That(document.Title, Is.EqualTo("Compliance oversigt"));
        var headers = document.Tables[0].Columns.Select(c => c.Header).ToList();
        Assert.That(headers, Is.EqualTo(new[] { "Virksomhed", "Overskredet", "Compliance %" }));
        Assert.That(headers, Does.Not.Contain("Ejendom"));
    }

    /// <summary>
    /// The title uses the NEW <c>ComplianceOverviewTitle</c> key, not the view
    /// label <c>ComplianceOverview</c> ("Oversigt") — that one is what
    /// <c>BuildFileName</c> prefixes the download with and it must stay as it is.
    /// </summary>
    [Test]
    public void Overview_TitleComesFromTheTitleKeyNotTheViewLabelKey()
    {
        var document = ComplianceExportDocumentBuilder.BuildOverview(
            new ComplianceReportOverviewModel(), "p", _localization);

        Assert.That(document.Title, Is.EqualTo("ComplianceOverviewTitle"));
    }

    /// <summary>
    /// <c>Compliance %</c> is a TEXT column whose cells carry the sign — <c>25</c>
    /// renders as <c>25%</c> — so CSV and PDF both print it. A typed Number cell
    /// would be printed bare by every renderer. <c>Overskredet</c> stays a Number
    /// column, and the value itself is the read model's, not recomputed.
    /// </summary>
    [Test]
    public void Overview_CompliancePercentIsATextCellCarryingThePercentSign()
    {
        var model = new ComplianceReportOverviewModel
        {
            Rows =
            [
                new ComplianceReportOverviewRowModel
                {
                    PropertyId = 9, PropertyName = "Ejendom 9", Overdue = 6,
                    DueTotal = 8, DueDone = 2, CompliancePct = 25
                }
            ],
            Totals = new ComplianceReportOverviewRowModel
            {
                Overdue = 70, DueTotal = 100, DueDone = 78, CompliancePct = 78
            }
        };

        var table = ComplianceExportDocumentBuilder.BuildOverview(model, "p", _danish).Tables[0];

        Assert.That(table.Columns[1].Type, Is.EqualTo(ComplianceExportCellType.Number));
        Assert.That(table.Columns[2].Type, Is.EqualTo(ComplianceExportCellType.Text));

        var row = table.Rows[0];
        Assert.That(row.Cells.Select(c => c.Text), Is.EqualTo(new[] { "Ejendom 9", "6", "25%" }));
        Assert.That(row.Cells[1].Number, Is.EqualTo(6));
        Assert.That(row.Cells[2].Number, Is.Null, "the percent cell is text, not a typed number");

        var totals = table.Rows[1];
        Assert.That(totals.IsTotal, Is.True);
        Assert.That(totals.Cells.Select(c => c.Text), Is.EqualTo(new[] { "I alt", "70", "78%" }));
    }

    /// <summary>
    /// The sign is attached to the exact integer the service produced — no
    /// rounding, no padding, no decimals — including at both ends of the range.
    /// </summary>
    [Test]
    [TestCase(0, "0%")]
    [TestCase(7, "7%")]
    [TestCase(100, "100%")]
    public void Overview_PercentSuffixWrapsTheServiceValueVerbatim(int pct, string expected)
    {
        var model = new ComplianceReportOverviewModel
        {
            Rows = [new ComplianceReportOverviewRowModel { PropertyName = "A", CompliancePct = pct }],
            Totals = new ComplianceReportOverviewRowModel { CompliancePct = pct }
        };

        var table = ComplianceExportDocumentBuilder.BuildOverview(model, "p", _localization).Tables[0];

        Assert.That(table.Rows[0].Cells[2].Text, Is.EqualTo(expected));
        Assert.That(table.Rows[1].Cells[2].Text, Is.EqualTo(expected));
    }

    /// <summary>
    /// The totals row is the LAST DATA ROW, marked <c>IsTotal</c>, and its numbers
    /// come STRAIGHT FROM <c>Totals</c> — the weighted value the service computed.
    /// The pinned case is the one that discriminates: one property at 1/1 and one
    /// at 0/100 must give 1, not 50. Averaging the two row percentages would give
    /// 50, so this test fails the moment the export starts recomputing.
    /// </summary>
    [Test]
    public void Overview_TotalsRowIsLastAndCarriesTheWeightedValueUnchanged()
    {
        var model = new ComplianceReportOverviewModel
        {
            Rows =
            [
                new ComplianceReportOverviewRowModel
                {
                    PropertyId = 1, PropertyName = "A", Overdue = 0,
                    DueTotal = 1, DueDone = 1, CompliancePct = 100
                },
                new ComplianceReportOverviewRowModel
                {
                    PropertyId = 2, PropertyName = "B", Overdue = 100,
                    DueTotal = 100, DueDone = 0, CompliancePct = 0
                }
            ],
            Totals = new ComplianceReportOverviewRowModel
            {
                Overdue = 100, DueTotal = 101, DueDone = 1, CompliancePct = 1
            }
        };

        var table = ComplianceExportDocumentBuilder
            .BuildOverview(model, "p", _localization).Tables[0];

        Assert.That(table.Rows, Has.Count.EqualTo(3));
        Assert.That(table.Rows[0].IsTotal, Is.False);
        Assert.That(table.Rows[1].IsTotal, Is.False);

        var totals = table.Rows[2];
        Assert.That(totals.IsTotal, Is.True);
        Assert.That(totals.Cells[0].Text, Is.EqualTo("Total"));
        Assert.That(totals.Cells[1].Number, Is.EqualTo(100));
        // 1%, not 50%: weighted, taken verbatim from the service's Totals (the
        // percent cell is text since #1190, so the value is read off its Text).
        Assert.That(totals.Cells[2].Text, Is.EqualTo("1%"));
    }

    /// <summary>
    /// A property whose work has not fallen due has a NULL percentage, never 0.
    /// It renders as the en dash — rendering it as a red 0 % would be a lie, and
    /// rendering it as an empty string would be indistinguishable from a bug. With
    /// the sign attached to the text cell (#1190) that also means never <c>0%</c>
    /// and never a bare <c>%</c>: the glyph is the whole cell, on the data row and
    /// on the totals row alike. The cell is also FLAGGED empty, which is what lets
    /// the CSV writer blank it under #1191's rule while Word/PDF keep the glyph.
    /// </summary>
    [Test]
    public void Overview_NullCompliancePercentRendersAsEnDashNotZero()
    {
        var model = new ComplianceReportOverviewModel
        {
            Rows =
            [
                new ComplianceReportOverviewRowModel
                {
                    PropertyId = 1, PropertyName = "A", Overdue = 0,
                    DueTotal = 0, DueDone = 0, CompliancePct = null
                }
            ],
            Totals = new ComplianceReportOverviewRowModel { CompliancePct = null }
        };

        var table = ComplianceExportDocumentBuilder
            .BuildOverview(model, "p", _localization).Tables[0];

        Assert.That(table.Rows[0].Cells[2].Number, Is.Null);
        Assert.That(table.Rows[0].Cells[2].Text, Is.EqualTo(Dash));
        Assert.That(table.Rows[1].Cells[2].Text, Is.EqualTo(Dash));
        Assert.That(table.Rows[0].Cells[2].Text, Does.Not.Contain("%"));
        Assert.That(table.Rows[1].Cells[2].Text, Does.Not.Contain("%"));
        Assert.That(table.Rows[0].Cells[2].IsEmpty, Is.True);
        Assert.That(table.Rows[1].Cells[2].IsEmpty, Is.True);
        // The valued cells on the same rows are NOT flagged.
        Assert.That(table.Rows[0].Cells[0].IsEmpty, Is.False);
        Assert.That(table.Rows[0].Cells[1].IsEmpty, Is.False);
    }

    /// <summary>
    /// An empty result still emits the totals row. #1164's empty state depends on
    /// <c>Totals</c> being present, and a reader who sees no "I alt" line cannot
    /// tell an empty report from a truncated one.
    /// </summary>
    [Test]
    public void Overview_EmptyResultStillEmitsTheTotalsRow()
    {
        var table = ComplianceExportDocumentBuilder
            .BuildOverview(new ComplianceReportOverviewModel(), "p", _localization).Tables[0];

        Assert.That(table.Rows, Has.Count.EqualTo(1));
        Assert.That(table.Rows[0].IsTotal, Is.True);
    }

    // ==================================================================
    // Detaljer
    // ==================================================================

    /// <summary>
    /// The eight Detaljer columns, in order. <c>Handlinger</c> is absent — buttons
    /// are not data. The seventh header reads the NEW <c>TagsPlain</c> key (#1191),
    /// not <c>Tags</c>: the export says "Tags" while the <c>Tags</c> key keeps
    /// "Etiketter" for the screens that read it.
    /// </summary>
    [Test]
    public void Details_HasTheEightPrototypeColumnsInOrder()
    {
        var document = ComplianceExportDocumentBuilder.BuildDetails([], "p", _localization);
        var headers = document.Tables[0].Columns.Select(c => c.Header).ToList();

        Assert.That(headers, Is.EqualTo(new[]
        {
            "Date", "Property", "CalendarBoard", "StartTime", "Task", "Worker", "TagsPlain", "Status"
        }));
    }

    /// <summary>
    /// Mock-up p8, by its Danish text: the title is <c>Compliance</c> — the new
    /// <c>ComplianceDetailsTitle</c> key, NOT the view label <c>Detaljer</c>
    /// (<c>ComplianceDetails</c>), which stays the file-name prefix — and the
    /// header row is exactly <c>Dato | Ejendom | Kalender | Kl. | Opgave |
    /// Medarbejder | Tags | Status</c>.
    /// </summary>
    [Test]
    public void Details_DanishTitleIsComplianceAndHeaderRowMatchesTheMockUp()
    {
        var document = ComplianceExportDocumentBuilder.BuildDetails([], "p", _danish);

        Assert.That(document.Title, Is.EqualTo("Compliance"));
        Assert.That(document.Title, Is.Not.EqualTo("Detaljer"));
        Assert.That(document.Tables[0].Columns.Select(c => c.Header), Is.EqualTo(new[]
        {
            "Dato", "Ejendom", "Kalender", "Kl.", "Opgave", "Medarbejder", "Tags", "Status"
        }));
        Assert.That(document.Tables[0].Columns.Select(c => c.Header), Does.Not.Contain("Etiketter"));
    }

    /// <summary>
    /// The date column is TYPED, which is what lets one source row produce the
    /// prototype's three renderings: an Excel date cell, an ISO CSV value and a
    /// <c>dd.MM.yyyy</c> PDF value. The row model carries the date as a string, so
    /// this pins that it is parsed back rather than passed through as text.
    /// </summary>
    [Test]
    public void Details_DateColumnIsTypedAndParsedFromTheIsoRowValue()
    {
        var document = ComplianceExportDocumentBuilder.BuildDetails(
            [new ComplianceReportRowModel { TaskDate = "2026-03-09" }], "p", _localization);

        Assert.That(document.Tables[0].Columns[0].Type, Is.EqualTo(ComplianceExportCellType.Date));
        Assert.That(document.Tables[0].Rows[0].Cells[0].Date, Is.EqualTo(new DateTime(2026, 3, 9)));
    }

    /// <summary>
    /// A date the row model carries in some other shape must NOT be dropped and
    /// must NOT be guessed at with a second format: it degrades to a text cell
    /// carrying the original string.
    ///
    /// <para>
    /// <b>What this does NOT cover, deliberately.</b> <c>Index</c> formats
    /// <c>TaskDate</c> with the CURRENT culture while this builder parses with
    /// <c>InvariantCulture</c>, so under a non-Gregorian server culture the two
    /// disagree — but that case does not land here. A Buddhist-calendar culture
    /// renders the same <c>yyyy-MM-dd</c> SHAPE, e.g. <c>"2569-03-09"</c>, which
    /// <c>TryParseExact</c> parses HAPPILY into a Gregorian date 543 years in the
    /// future. The failure mode there is a parseable but WRONG date, not a degrade
    /// to text, and no assertion here would catch it. The real fix is one word in
    /// <c>BackendConfigurationComplianceReportService</c> — out of scope for #1169,
    /// which certifies that file byte-identical to <c>stable</c>. All 26 shipped
    /// locales are Gregorian, so it is unreachable today.
    /// </para>
    ///
    /// <para>
    /// The input below is therefore one that genuinely cannot parse under any
    /// culture, which is what this test is actually about.
    /// </para>
    /// </summary>
    [Test]
    public void Details_UnparseableDateDegradesToTextAndKeepsTheOriginalString()
    {
        var document = ComplianceExportDocumentBuilder.BuildDetails(
            [new ComplianceReportRowModel { TaskDate = "9. marts 2026" }], "p", _localization);

        var cell = document.Tables[0].Rows[0].Cells[0];
        Assert.That(cell.Date, Is.Null);
        Assert.That(cell.Text, Is.EqualTo("9. marts 2026"));
        // No date, so no weekday text to derive: Word/PDF print the original string.
        Assert.That(cell.DisplayText, Is.Null);
        Assert.That(cell.IsEmpty, Is.False);
    }

    /// <summary>
    /// The <c>Dato</c> cell carries the screen's long weekday form as its Word/PDF
    /// display text (#1191): <c>Tirsdag 21. juli</c> for 2026-07-21 under a Danish
    /// request culture — weekday capitalised, month lower-case, no year. The typed
    /// date and the <c>dd.MM.yyyy</c> text underneath it are unchanged, which is
    /// what keeps the CSV ISO. The culture is <c>CurrentCulture</c>, the one the
    /// JSON localizer resolves the headers through, so weekday and headers agree.
    /// </summary>
    [Test]
    [SetCulture("da-DK")]
    public void Details_DateCarriesTheDanishWeekdayDisplayTextForWordAndPdf()
    {
        var document = ComplianceExportDocumentBuilder.BuildDetails(
            [new ComplianceReportRowModel { TaskDate = "2026-07-21" }], "p", _danish);

        var cell = document.Tables[0].Rows[0].Cells[0];
        Assert.That(cell.DisplayText, Is.EqualTo("Tirsdag 21. juli"));
        Assert.That(cell.Date, Is.EqualTo(new DateTime(2026, 7, 21)));
        Assert.That(cell.Text, Is.EqualTo("21.07.2026"));
        Assert.That(cell.IsEmpty, Is.False);
    }

    /// <summary>
    /// The weekday text follows the request culture (<c>CurrentCulture</c>, the
    /// one the JSON localizer reads) — a non-Danish user gets their own language,
    /// exactly as the screen does — and the capitalisation rule is applied
    /// regardless of what the culture's own casing is.
    /// </summary>
    [Test]
    [SetCulture("en-US")]
    public void Details_WeekdayDisplayTextFollowsTheRequestCulture()
    {
        var document = ComplianceExportDocumentBuilder.BuildDetails(
            [new ComplianceReportRowModel { TaskDate = "2026-07-21" }], "p", _localization);

        Assert.That(document.Tables[0].Rows[0].Cells[0].DisplayText, Is.EqualTo("Tuesday 21. July"));
    }

    /// <summary>
    /// <see cref="ComplianceExportDocumentBuilder.FormatWeekdayDate"/> on its own:
    /// first letter upper-cased, the rest as the culture gives it. Danish month
    /// and weekday names are lower-case in ICU, which is why the capitalisation
    /// is explicit.
    /// </summary>
    [Test]
    [SetCulture("da-DK")]
    [TestCase(2026, 7, 21, "Tirsdag 21. juli")]
    [TestCase(2026, 3, 9, "Mandag 9. marts")]
    [TestCase(2026, 1, 1, "Torsdag 1. januar")]
    public void FormatWeekdayDate_IsCapitalisedWeekdayDayDotMonth(int y, int m, int d, string expected)
    {
        Assert.That(ComplianceExportDocumentBuilder.FormatWeekdayDate(new DateTime(y, m, d)),
            Is.EqualTo(expected));
    }

    /// <summary>
    /// An all-day occurrence has no clock time, so "Kl." is the empty cell (the en
    /// dash in Word/PDF, blank in CSV) rather than a fabricated 00:00. A timed
    /// occurrence renders the RANGE <c>start - end</c> from its start hour and
    /// duration (#1191), as the screen does.
    /// </summary>
    [Test]
    public void Details_AllDayHasNoClockTimeAndATimedRowRendersTheRange()
    {
        var document = ComplianceExportDocumentBuilder.BuildDetails(
        [
            new ComplianceReportRowModel { TaskDate = "2026-03-09", IsAllDay = true, StartHour = 9.0, Duration = 1.0 },
            new ComplianceReportRowModel { TaskDate = "2026-03-09", IsAllDay = false, StartHour = 9.5, Duration = 1.0 },
            new ComplianceReportRowModel { TaskDate = "2026-03-09", IsAllDay = false, StartHour = 13.0, Duration = 1.0 }
        ], "p", _localization);

        var allDay = document.Tables[0].Rows[0].Cells[3];
        Assert.That(allDay.Text, Is.EqualTo(Dash));
        Assert.That(allDay.IsEmpty, Is.True);

        Assert.That(document.Tables[0].Rows[1].Cells[3].Text, Is.EqualTo("09:30 - 10:30"));
        Assert.That(document.Tables[0].Rows[2].Cells[3].Text, Is.EqualTo("13:00 - 14:00"));
        Assert.That(document.Tables[0].Rows[2].Cells[3].IsEmpty, Is.False);
    }

    /// <summary>
    /// <see cref="ComplianceExportDocumentBuilder.FormatTimeRange"/>: hyphen-minus
    /// with spaces (the mock-up's separator, not the en dash), both ends through
    /// the same HH:mm rule, so the end is clamped to 23:59 like the start is and a
    /// zero duration gives a zero-width range rather than an invented end.
    /// </summary>
    [Test]
    [TestCase(13.0, 1.0, "13:00 - 14:00")]
    [TestCase(9.5, 0.5, "09:30 - 10:00")]
    [TestCase(23.5, 1.0, "23:30 - 23:59")]
    [TestCase(23.0, 2.0, "23:00 - 23:59")]
    [TestCase(8.0, 0.0, "08:00 - 08:00")]
    [TestCase(0.0, 24.0, "00:00 - 23:59")]
    public void FormatTimeRange_RendersStartHyphenEndAndClampsTheEnd(double start, double duration, string expected)
    {
        var range = ComplianceExportDocumentBuilder.FormatTimeRange(start, duration);

        Assert.That(range, Is.EqualTo(expected));
        Assert.That(range, Does.Contain(" - "));
        Assert.That(range, Does.Not.Contain(Dash));
    }

    /// <summary>
    /// Status is the localised done / not-done pair, and empty worker and tag lists
    /// collapse to the empty cell — the en dash as text, and FLAGGED so CSV can
    /// blank it — rather than to an empty string.
    /// </summary>
    [Test]
    public void Details_StatusLabelsAndEmptyListsUseTheSharedGlyph()
    {
        var document = ComplianceExportDocumentBuilder.BuildDetails(
        [
            new ComplianceReportRowModel
            {
                TaskDate = "2026-03-09", Completed = true,
                WorkerNames = ["Ann", "Bo"], Tags = ["Miljø"]
            },
            new ComplianceReportRowModel { TaskDate = "2026-03-09", Completed = false }
        ], "p", _localization);

        var done = document.Tables[0].Rows[0];
        Assert.That(done.Cells[5].Text, Is.EqualTo("Ann, Bo"));
        Assert.That(done.Cells[6].Text, Is.EqualTo("Miljø"));
        Assert.That(done.Cells[7].Text, Is.EqualTo("Done"));
        Assert.That(done.Cells[5].IsEmpty, Is.False);
        Assert.That(done.Cells[6].IsEmpty, Is.False);

        var open = document.Tables[0].Rows[1];
        Assert.That(open.Cells[5].Text, Is.EqualTo(Dash));
        Assert.That(open.Cells[6].Text, Is.EqualTo(Dash));
        Assert.That(open.Cells[7].Text, Is.EqualTo("NotDone"));
        Assert.That(open.Cells[5].IsEmpty, Is.True);
        Assert.That(open.Cells[6].IsEmpty, Is.True);
    }

    /// <summary>
    /// A completed row is marked <see cref="ComplianceExportRow.IsDone"/> (#1191)
    /// so Word/PDF can tint it; an open row is not. The mark is a row property
    /// next to <c>IsTotal</c>, and a Detaljer row is never a totals row.
    /// </summary>
    [Test]
    public void Details_CompletedRowsAreMarkedDoneAndOpenRowsAreNot()
    {
        var document = ComplianceExportDocumentBuilder.BuildDetails(
        [
            new ComplianceReportRowModel { TaskDate = "2026-03-09", Completed = true },
            new ComplianceReportRowModel { TaskDate = "2026-03-09", Completed = false },
            new ComplianceReportRowModel { TaskDate = "2026-03-10", Completed = true }
        ], "p", _localization);

        var rows = document.Tables[0].Rows;
        Assert.That(rows.Select(r => r.IsDone), Is.EqualTo(new[] { true, false, true }));
        Assert.That(rows.Select(r => r.IsTotal), Is.All.False);
    }

    /// <summary>
    /// The done mark is Detaljer's alone: Oversigt rows (including the totals row)
    /// never carry it, so the tint cannot leak into the other view.
    /// </summary>
    [Test]
    public void Overview_RowsAreNeverMarkedDone()
    {
        var model = new ComplianceReportOverviewModel
        {
            Rows = [new ComplianceReportOverviewRowModel { PropertyId = 1, PropertyName = "A", CompliancePct = 100 }],
            Totals = new ComplianceReportOverviewRowModel { CompliancePct = 100 }
        };

        var table = ComplianceExportDocumentBuilder.BuildOverview(model, "p", _localization).Tables[0];

        Assert.That(table.Rows.Select(r => r.IsDone), Is.All.False);
    }

    /// <summary>
    /// Every row handed in is exported. The builder has no page size and no row
    /// cap of its own — the export covers the full filtered set, and the only
    /// ceiling is the report service's documented <c>MaxRowsReturned</c>.
    /// </summary>
    [Test]
    public void Details_ExportsEveryRowItIsGiven()
    {
        var rows = Enumerable.Range(0, 137)
            .Select(_ => new ComplianceReportRowModel { TaskDate = "2026-03-09" })
            .ToList();

        var document = ComplianceExportDocumentBuilder.BuildDetails(rows, "p", _localization);

        Assert.That(document.Tables[0].Rows, Has.Count.EqualTo(137));
    }

    // ==================================================================
    // Rapport (#1188: one table per REPORT HEADLINE, tags as a caption)
    // ==================================================================

    /// <summary>
    /// One table per HEADLINE GROUP — the PDF's "Tabel_Rapport" rule — carrying the
    /// group's tags caption and the headline as its title. Two templates under one
    /// headline are ONE table (the service already unioned their columns), not two.
    /// This reverses #1160 decision 5 / #1167 / PR #1178's composite label.
    /// </summary>
    [Test]
    public void Report_ProducesOneTablePerHeadlineGroupWithCaptionAndHeadlineTitle()
    {
        var groups = new List<ComplianceReportHeadlineGroupModel>
        {
            Group(7, "Brandsikkerhed og beredskab", "Miljøtilsyn - Brand", 509, 511),
            Group(8, "Elinstallationer og eftersyn", "Miljøtilsyn - EL", 509)
        };

        var document = ComplianceExportDocumentBuilder.BuildReport(groups, "p", false, _localization);

        Assert.That(document.Tables, Has.Count.EqualTo(2));
        Assert.That(document.Tables.Select(t => t.Caption),
            Is.EqualTo(new[] { "Miljøtilsyn - Brand", "Miljøtilsyn - EL" }));
        Assert.That(document.Tables.Select(t => t.Title),
            Is.EqualTo(new[] { "Brandsikkerhed og beredskab", "Elinstallationer og eftersyn" }));
        // No template name anywhere in a title.
        Assert.That(document.Tables.Select(t => t.Title), Has.None.Contains("–"));
    }

    /// <summary>
    /// The fixed Rapport columns, then the group's REAL answer columns (the
    /// service's union). The prototype's fabricated placeholders <c>Note</c>,
    /// <c>Option 1</c> and <c>Option 2</c> must not appear as hard-coded headers
    /// anywhere — an explicit acceptance criterion — and neither must
    /// <c>Handlinger</c>. Nor is there a <c>Rapportoverskrift</c> column: #1188
    /// decision 4 follows the mock-up, which has none.
    /// </summary>
    [Test]
    public void Report_FixedColumnsThenRealAnswerColumnsAndNoPlaceholderOrHeadlineHeaders()
    {
        var group = Group(1, "Overskrift", "T", 509);
        group.Columns =
        [
            new ComplianceReportColumnModel { Key = "f10", Label = "Målerstand", FieldType = "Number" },
            new ComplianceReportColumnModel { Key = "f11", Label = "Bemærkning", FieldType = "Text" }
        ];

        var document = ComplianceExportDocumentBuilder.BuildReport([group], "p", false, _localization);

        var headers = document.Tables[0].Columns.Select(c => c.Header).ToList();
        Assert.That(headers, Is.EqualTo(new[]
        {
            "SubReport", "CaseId", "Property", "DoneBy", "CompletedDate", "Area", "Images",
            "Målerstand", "Bemærkning"
        }));

        Assert.That(headers, Does.Not.Contain("Note"));
        Assert.That(headers, Does.Not.Contain("Option 1"));
        Assert.That(headers, Does.Not.Contain("Option 2"));
        Assert.That(headers, Does.Not.Contain("Handlinger"));
        Assert.That(headers, Does.Not.Contain("ReportHeadline"));
    }

    /// <summary>
    /// Cells are addressed BY KEY. A case that answered only the second of two
    /// columns must leave the first as the en dash and keep the second in its own
    /// column — the exact failure mode of #1160 finding 3, where a header list is
    /// zipped positionally against a value list and one excluded field shifts every
    /// later column by one. With #1188's union columns this is also how a case
    /// answered on template A renders under template B's columns: dash, in place.
    /// </summary>
    [Test]
    public void Report_UnansweredOrForeignTemplateColumnGetsTheGlyphAndDoesNotShiftLaterColumns()
    {
        var group = Group(1, "Overskrift", "T", 509, 511);
        group.Columns =
        [
            new ComplianceReportColumnModel { Key = "f10", Label = "Målerstand" }, // template 509
            new ComplianceReportColumnModel { Key = "f11", Label = "Bemærkning" }  // template 511
        ];
        group.Cases =
        [
            new ComplianceReportCaseModel
            {
                SdkCaseId = 42, CheckListId = 511, PropertyName = "Gården", Title = "Vand",
                Cells = new Dictionary<string, string> { ["f11"] = "Alt ok" }
            }
        ];

        var document = ComplianceExportDocumentBuilder.BuildReport([group], "p", false, _localization);

        var cells = document.Tables[0].Rows[0].Cells;
        Assert.That(cells[7].Text, Is.EqualTo(Dash));   // f10, the other template's column
        Assert.That(cells[7].IsEmpty, Is.True);
        Assert.That(cells[8].Text, Is.EqualTo("Alt ok")); // f11, still in ITS column
    }

    /// <summary>
    /// The Word/PDF table starts at <c>ID</c> (PDF page 9) while the CSV keeps
    /// <c>Delrapport</c> (page 10): the column is marked <c>CsvOnly</c> — and it
    /// is the ONLY one, so the writers' column sets differ by exactly it. Every
    /// column also carries the key the CSV writer unions sections on: the fixed
    /// ones their localisation key, the answer ones the service's
    /// <c>f{fieldId}</c> (#1192).
    /// </summary>
    [Test]
    public void Report_SubReportIsTheOnlyCsvOnlyColumnAndEveryColumnCarriesAKey()
    {
        var group = Group(1, "Overskrift", "T", 509);
        group.Columns =
        [
            new ComplianceReportColumnModel { Key = "f10", Label = "Målerstand" },
            new ComplianceReportColumnModel { Key = "f11", Label = "Bemærkning" }
        ];

        var document = ComplianceExportDocumentBuilder.BuildReport([group], "p", false, _localization);

        var columns = document.Tables[0].Columns;
        Assert.That(columns.Where(c => c.CsvOnly).Select(c => c.Header), Is.EqualTo(new[] { "SubReport" }));
        Assert.That(columns.Select(c => c.Key), Is.EqualTo(new[]
        {
            "SubReport", "CaseId", "Property", "DoneBy", "CompletedDate", "Area", "Images", "f10", "f11"
        }));
    }

    /// <summary>
    /// <c>Delrapport</c> carries the ROW's own tags joined <c>" - "</c> (PDF page
    /// 10) — hyphen-minus with spaces, not the en dash — and an untagged row gets
    /// the EMPTY cell, never the headline. <c>Billeder</c> is the image COUNT, not
    /// the images — a <c>Number</c> cell, so the CSV gets the bare figure — and
    /// <c>Udført dato</c> is case metadata (a typed date), never an answer field.
    /// </summary>
    [Test]
    public void Report_SubReportCellIsTheRowsOwnTagsAndImagesIsACount()
    {
        var group = Group(1, "Flydelag", "Brand - Miljøtilsyn", 509);
        group.Cases =
        [
            new ComplianceReportCaseModel
            {
                SdkCaseId = 42, PropertyName = "Gården", Title = "Vand",
                Tags = ["Miljøtilsyn", "Brand"],
                DoneAt = new DateTime(2026, 3, 9, 14, 30, 0),
                WorkerNames = ["Ann"],
                ImagesCount = 3,
                Images =
                [
                    new ComplianceReportImageModel { FileName = "1_700_a.jpg" },
                    new ComplianceReportImageModel { FileName = "2_700_b.jpg" },
                    new ComplianceReportImageModel { FileName = "3_700_c.jpg" }
                ]
            },
            new ComplianceReportCaseModel { SdkCaseId = 43, PropertyName = "Gården", Title = "Vand", Tags = [] }
        ];

        var document = ComplianceExportDocumentBuilder.BuildReport([group], "p", false, _localization);

        var tagged = document.Tables[0].Rows[0].Cells;
        Assert.That(tagged[0].Text, Is.EqualTo("Miljøtilsyn - Brand"));
        Assert.That(tagged[0].Text, Does.Not.Contain("Flydelag"), "the headline is not in the Delrapport cell");
        Assert.That(tagged[1].Number, Is.EqualTo(42));
        Assert.That(tagged[4].Date, Is.EqualTo(new DateTime(2026, 3, 9, 14, 30, 0)));
        Assert.That(tagged[6].Number, Is.EqualTo(3));

        var untagged = document.Tables[0].Rows[1].Cells;
        Assert.That(untagged[0].IsEmpty, Is.True);
        Assert.That(untagged[0].Text, Is.EqualTo(Dash));

        // Not requested, so no appendix at all — the images stay a count.
        Assert.That(document.Tables[0].ImageBlocks, Is.Empty);
    }

    /// <summary>
    /// <c>Billeder</c> reads <c>3 billeder</c> on the page (#1192, PDF page 9 —
    /// the localised <c>ImagesCount</c> with its <c>{0}</c> filled in, and no
    /// emoji) while the cell stays a <c>Number</c> carrying <c>3</c> for the CSV
    /// (page 10 shows the bare figure). Exactly one image reads the singular
    /// <c>1 billede</c> (<c>ImagesCountOne</c>), not <c>1 billeder</c>, while the
    /// number still carries <c>1</c>. A case with NO images gets the EMPTY
    /// cell — the en dash in the PDF, a blank field in the CSV — never
    /// <c>0 billeder</c>.
    /// </summary>
    [Test]
    public void Report_ImagesCellIsTheLocalisedCountForWordAndTheNumberForCsvAndEmptyForZero()
    {
        var group = Group(1, "Overskrift", "T", 509);
        group.Cases =
        [
            new ComplianceReportCaseModel { SdkCaseId = 42, Title = "Vand", ImagesCount = 3 },
            new ComplianceReportCaseModel { SdkCaseId = 43, Title = "Vand", ImagesCount = 0 },
            new ComplianceReportCaseModel { SdkCaseId = 44, Title = "Vand", ImagesCount = 1 }
        ];

        var document = ComplianceExportDocumentBuilder.BuildReport([group], "p", false, _danish);

        var three = document.Tables[0].Rows[0].Cells[6];
        Assert.That(three.Text, Is.EqualTo("3 billeder"));
        Assert.That(three.Number, Is.EqualTo(3));
        Assert.That(three.IsEmpty, Is.False);
        Assert.That(three.Text, Does.Not.Contain("\U0001F4F7"), "no camera emoji");
        Assert.That(document.Tables[0].Columns[6].Type, Is.EqualTo(ComplianceExportCellType.Number));

        var none = document.Tables[0].Rows[1].Cells[6];
        Assert.That(none.IsEmpty, Is.True);
        Assert.That(none.Text, Is.EqualTo(Dash));
        Assert.That(none.Number, Is.Null);

        var one = document.Tables[0].Rows[2].Cells[6];
        Assert.That(one.Text, Is.EqualTo("1 billede"), "singular, not '1 billeder'");
        Assert.That(one.Number, Is.EqualTo(1));
        Assert.That(one.IsEmpty, Is.False);
    }

    /// <summary>
    /// The image appendix is OPT-IN and CAPPED per case (#1169 §6, decided). With
    /// the flag on, at most <c>MaxAppendixImagesPerCase</c> names are embedded and
    /// the block records the true total so the document can state its own
    /// truncation.
    /// </summary>
    [Test]
    public void Report_ImageAppendixIsOptInAndCappedPerCase()
    {
        var group = Group(1, "Overskrift", "Miljø", 509);
        group.Cases =
        [
            new ComplianceReportCaseModel
            {
                SdkCaseId = 42, Title = "Vand", TaskDate = "2026-03-09", ImagesCount = 9,
                Images = Enumerable.Range(1, 9)
                    .Select(i => new ComplianceReportImageModel { FileName = $"{i}_700_x.jpg" })
                    .ToList()
            }
        ];

        var document = ComplianceExportDocumentBuilder.BuildReport([group], "p", true, _localization);

        Assert.That(document.Tables[0].ImageBlocks, Has.Count.EqualTo(1));
        var block = document.Tables[0].ImageBlocks[0];
        Assert.That(block.ImageNames,
            Has.Count.EqualTo(ComplianceExportDocumentBuilder.MaxAppendixImagesPerCase));
        Assert.That(block.TotalImages, Is.EqualTo(9));
    }

    /// <summary>
    /// The appendix is grouped PER SECTION (#1192, PDF page 9): each table carries
    /// the label its <c>Bilag – …</c> page is headed with — the section's TAGS
    /// CAPTION, or the headline label when the group has no tags, so the page is
    /// still attributable to its section — and its own blocks. The section is
    /// therefore NOT repeated in the block captions any more: a block reads
    /// <c>Sag {SdkCaseId} · {Område} · {dd.MM.yyyy}</c>, the mock-up's line, with
    /// the localised <c>Case</c> key leading.
    /// </summary>
    [Test]
    public void Report_AppendixIsGroupedPerSectionWithTheTagsCaptionOrTheHeadlineAsItsLabel()
    {
        ComplianceReportCaseModel CaseWithImage(int id) => new()
        {
            SdkCaseId = id, Title = "Vand", TaskDate = "2026-03-09", ImagesCount = 1,
            Images = [new ComplianceReportImageModel { FileName = $"{id}_700_a.jpg" }]
        };

        var tagged = Group(1, "Brandsikkerhed", "Miljøtilsyn - Brand", 509);
        tagged.Cases = [CaseWithImage(42)];
        var untagged = Group(2, "Egenkontrol", "", 509);
        untagged.SchemaUnavailableCheckListIds = [509];
        untagged.Cases = [CaseWithImage(43)];
        var withoutImages = Group(3, "Rundering", "Miljø", 509);
        withoutImages.Cases = [new ComplianceReportCaseModel { SdkCaseId = 44, Title = "Gang" }];

        var document = ComplianceExportDocumentBuilder.BuildReport(
            [tagged, untagged, withoutImages], "p", true, _danish);

        Assert.That(document.Tables.Select(t => t.AppendixLabel),
            Is.EqualTo(new[] { "Miljøtilsyn - Brand", "Egenkontrol", "Miljø" }));
        // The headline label, NOT the title: the schema suffix stays off the
        // appendix page.
        Assert.That(document.Tables[1].Title, Does.Contain("("));
        Assert.That(document.Tables[1].AppendixLabel, Does.Not.Contain("("));

        Assert.That(document.Tables[0].ImageBlocks.Select(b => b.Caption),
            Is.EqualTo(new[] { "Sag 42 · Vand · 09.03.2026" }));
        Assert.That(document.Tables[1].ImageBlocks.Select(b => b.Caption),
            Is.EqualTo(new[] { "Sag 43 · Vand · 09.03.2026" }));
        Assert.That(document.Tables[2].ImageBlocks, Is.Empty, "a section without images has no blocks");
        Assert.That(document.Tables.SelectMany(t => t.ImageBlocks).Select(b => b.Caption),
            Has.None.Contains("Miljøtilsyn"), "the section is on the page heading, not in the block");
    }

    /// <summary>
    /// The block's date is <c>DoneAt</c> — the mock-up's date is the one the
    /// <c>Udført dato</c> column shows — as <c>dd.MM.yyyy</c>, falling back to the
    /// ISO <c>TaskDate</c> (reformatted the same way) for a case that is not
    /// completed. An unparseable <c>TaskDate</c> is passed through rather than
    /// guessed at, and a case with neither ends its caption at the area.
    /// </summary>
    [Test]
    public void Report_AppendixBlockDateIsDoneAtFallingBackToTaskDate()
    {
        ComplianceReportCaseModel Case(int id, DateTime? doneAt, string? taskDate) => new()
        {
            SdkCaseId = id, Title = "Vand", DoneAt = doneAt, TaskDate = taskDate, ImagesCount = 1,
            Images = [new ComplianceReportImageModel { FileName = $"{id}_700_a.jpg" }]
        };

        var group = Group(1, "Overskrift", "Miljø", 509);
        group.Cases =
        [
            Case(1, new DateTime(2026, 5, 13, 9, 15, 0), "2026-05-12"),
            Case(2, null, "2026-05-12"),
            Case(3, null, "12/05/2026"),
            Case(4, null, null)
        ];

        var document = ComplianceExportDocumentBuilder.BuildReport([group], "p", true, _danish);

        Assert.That(document.Tables[0].ImageBlocks.Select(b => b.Caption), Is.EqualTo(new[]
        {
            "Sag 1 · Vand · 13.05.2026",
            "Sag 2 · Vand · 12.05.2026",
            "Sag 3 · Vand · 12/05/2026",
            "Sag 4 · Vand"
        }));
    }

    /// <summary>
    /// An image the projector could not name (its <c>UploadedData.FileName</c>
    /// existence check failed, so <c>FileName</c> is null) has no file to read and
    /// is dropped rather than passed to the renderer as a null path.
    ///
    /// <para>
    /// <c>TotalImages</c> stays the case's OWN count, so the drop is VISIBLE: the
    /// Billeder column prints 2, the block carries 1, and the renderer's
    /// <c>(n/total)</c> note therefore fires. Taking the total from the filtered
    /// list instead would make the appendix hide a truncation it caused, and put
    /// the caption at odds with the table two lines above it.
    /// </para>
    /// </summary>
    [Test]
    public void Report_ImagesWithoutADerivedNameAreDropped()
    {
        var group = Group(1, "Overskrift", "M", 509);
        group.Cases =
        [
            new ComplianceReportCaseModel
            {
                SdkCaseId = 42, ImagesCount = 2,
                Images =
                [
                    new ComplianceReportImageModel { FileName = null },
                    new ComplianceReportImageModel { FileName = "2_700_b.jpg" }
                ]
            }
        ];

        var document = ComplianceExportDocumentBuilder.BuildReport([group], "p", true, _localization);

        Assert.That(document.Tables[0].ImageBlocks[0].ImageNames, Is.EqualTo(new[] { "2_700_b.jpg" }));
        Assert.That(document.Tables[0].ImageBlocks[0].TotalImages, Is.EqualTo(2));
    }

    /// <summary>
    /// Since #1188 a case is in exactly one group, so the per-<c>SdkCaseId</c>
    /// de-duplication of appendix blocks is redundant — but it is the invariant the
    /// document-wide ceiling was reasoned about with, and it is kept. Should the
    /// same case instance ever reach two groups again, it still gets ONE block,
    /// under the first section.
    /// </summary>
    [Test]
    public void Report_ImageAppendixIsEmittedOncePerCaseAcrossGroups()
    {
        var sharedCase = new ComplianceReportCaseModel
        {
            SdkCaseId = 42, Title = "Vand", TaskDate = "2026-03-09", ImagesCount = 2,
            Images =
            [
                new ComplianceReportImageModel { FileName = "1_700_a.jpg" },
                new ComplianceReportImageModel { FileName = "2_700_b.jpg" }
            ]
        };

        ComplianceReportHeadlineGroupModel WithSharedCase(int? headlineId, string name)
        {
            var group = Group(headlineId, name, "Miljø", 509);
            group.Cases = [sharedCase];
            return group;
        }

        var document = ComplianceExportDocumentBuilder.BuildReport(
            [WithSharedCase(1, "A"), WithSharedCase(2, "B"), WithSharedCase(null, null)],
            "p", true, _localization);

        Assert.That(document.Tables, Has.Count.EqualTo(3));
        Assert.That(document.Tables.Sum(t => t.Rows.Count), Is.EqualTo(3));

        Assert.That(document.Tables[0].ImageBlocks, Has.Count.EqualTo(1));
        Assert.That(document.Tables[1].ImageBlocks, Is.Empty);
        Assert.That(document.Tables[2].ImageBlocks, Is.Empty);
        Assert.That(document.AppendixImagesEmbedded, Is.EqualTo(2));
        Assert.That(document.AppendixImagesRequested, Is.EqualTo(2));
    }

    /// <summary>
    /// The per-case cap bounds a BLOCK; nothing in the Word path streams, so the
    /// DOCUMENT needs its own ceiling as well. When it bites, the later cases get
    /// no block at all — a captioned page with no image on it carries nothing — and
    /// the document records both counts so the renderer can state the truncation
    /// the same way a block caption states the per-case one.
    /// </summary>
    [Test]
    public void Report_ImageAppendixIsCappedForTheWholeDocument()
    {
        var group = Group(1, "Overskrift", "Miljø", 509);
        // Four images each, so the per-case cap never bites: 100 cases want 400
        // images and the document ceiling is 200.
        group.Cases = Enumerable.Range(1, 100)
            .Select(c => new ComplianceReportCaseModel
            {
                SdkCaseId = c, Title = "Vand", TaskDate = "2026-03-09", ImagesCount = 4,
                Images = Enumerable.Range(1, 4)
                    .Select(i => new ComplianceReportImageModel { FileName = $"{c}_{i}_700_x.jpg" })
                    .ToList()
            })
            .ToList();

        var document = ComplianceExportDocumentBuilder.BuildReport([group], "p", true, _localization);

        var blocks = document.Tables[0].ImageBlocks;
        Assert.That(blocks.Sum(b => b.ImageNames.Count),
            Is.EqualTo(ComplianceExportDocumentBuilder.MaxAppendixImages));
        Assert.That(document.AppendixImagesEmbedded,
            Is.EqualTo(ComplianceExportDocumentBuilder.MaxAppendixImages));
        // Every case counts towards the requested total, including the ones that
        // got no block — that is what makes the "n of m" honest.
        Assert.That(document.AppendixImagesRequested, Is.EqualTo(400));
        Assert.That(blocks, Has.Count.EqualTo(50));
        // Every row is still there; only the photographs were rationed.
        Assert.That(document.Tables[0].Rows, Has.Count.EqualTo(100));
    }

    /// <summary>
    /// A headline whose NAME could not be resolved is still a NAMED group — the
    /// report service deliberately keeps a headline id whose name lives in the
    /// items-planning database with no foreign key to it. Labelling it "Uden
    /// rapportoverskrift" would merge it with the genuinely headline-less fallback
    /// group that the service sorts last precisely to keep the two apart. It gets
    /// the neutral <c>#{HeadlineTagId}</c> form instead — the same the screen
    /// renders.
    /// </summary>
    [Test]
    public void Report_NamedGroupWithAnUnresolvableNameIsNotLabelledWithoutHeadline()
    {
        var document = ComplianceExportDocumentBuilder.BuildReport(
            [Group(77, null, "T", 509), Group(null, null, "T", 509)],
            "p", false, _localization);

        Assert.That(document.Tables[0].Title, Is.EqualTo("#77"));
        Assert.That(document.Tables[1].Title, Is.EqualTo("WithoutReportHeadline"));
        Assert.That(document.Tables[0].Title, Is.Not.EqualTo(document.Tables[1].Title));
    }

    /// <summary>
    /// A template whose schema could not be derived says so in the heading. Zero
    /// columns because derivation FAILED and zero columns because the template has
    /// no answerable fields are different facts, and the export must not render
    /// them identically. With union columns the notice is per template: the bare
    /// label when EVERY template in the group is affected, the affected ids when
    /// only some are — the rest of the table still carries real columns.
    /// </summary>
    [Test]
    public void Report_SchemaUnavailableIsStatedInTheSectionHeadingPerTemplate()
    {
        var all = Group(1, "Overskrift", "Miljø", 509);
        all.SchemaUnavailableCheckListIds = [509];

        var some = Group(2, "Anden overskrift", "Miljø", 509, 511);
        some.SchemaUnavailableCheckListIds = [511];

        var document = ComplianceExportDocumentBuilder.BuildReport([all, some], "p", false, _localization);

        Assert.That(document.Tables[0].Title, Is.EqualTo("Overskrift (ColumnsUnavailable)"));
        Assert.That(document.Tables[1].Title, Is.EqualTo("Anden overskrift (ColumnsUnavailable: #511)"));
    }

    /// <summary>
    /// The fallback group carries no name from the API (the label is the
    /// consumer's), so the export supplies the localised "Uden rapportoverskrift" —
    /// the <c>WithoutReportHeadline</c> key, not the retired <c>WithoutTag</c>.
    /// Its caption still renders when its cases carry tags.
    /// </summary>
    [Test]
    public void Report_FallbackGroupGetsTheLocalisedLabelAndKeepsItsCaption()
    {
        var document = ComplianceExportDocumentBuilder.BuildReport(
            [Group(null, null, "Aa tag", 509)], "p", false, _localization);

        Assert.That(document.Tables[0].Title, Is.EqualTo("WithoutReportHeadline"));
        Assert.That(document.Tables[0].Caption, Is.EqualTo("Aa tag"));
    }

    // ==================================================================
    // Shared helpers
    // ==================================================================

    /// <summary>
    /// <c>StartHour</c> is a free double on the occurrence-exception row with no
    /// database constraint, so out-of-range values are clamped rather than rendered
    /// as "25:00", and .5 becomes :30 rather than :50.
    /// </summary>
    [Test]
    [TestCase(0.0, "00:00")]
    [TestCase(9.0, "09:00")]
    [TestCase(9.5, "09:30")]
    [TestCase(13.25, "13:15")]
    [TestCase(-1.0, "00:00")]
    [TestCase(25.0, "23:59")]
    public void FormatStartHour_RendersHhMmAndClamps(double input, string expected)
    {
        Assert.That(ComplianceExportDocumentBuilder.FormatStartHour(input), Is.EqualTo(expected));
    }

    private static ComplianceReportHeadlineGroupModel Group(
        int? headlineTagId, string? headlineName, string tagsCaption, params int[] checkListIds) => new()
    {
        HeadlineTagId = headlineTagId,
        HeadlineName = headlineName,
        TagsCaption = tagsCaption,
        CheckListIds = checkListIds.ToList()
    };

    /// <summary>
    /// Danish for the keys Oversigt and Detaljer read — the values in
    /// <c>Resources/localization.json</c>, including the two #1190 added
    /// (<c>Company</c>, <c>ComplianceOverviewTitle</c>) and the two #1191 added
    /// (<c>TagsPlain</c>, <c>ComplianceDetailsTitle</c>). Both <c>Tags</c>
    /// ("Etiketter") and <c>TagsPlain</c> ("Tags") are here, and both
    /// <c>ComplianceDetails</c> ("Detaljer") and <c>ComplianceDetailsTitle</c>
    /// ("Compliance"), so a Detaljer assertion pins WHICH of each pair the builder
    /// reads. Every other key comes back as itself, like the shared key-returning
    /// double.
    /// </summary>
    private sealed class DanishExportLocalizer : IBackendConfigurationLocalizationService
    {
        private static readonly Dictionary<string, string> Danish = new()
        {
            ["Company"] = "Virksomhed",
            ["Overdue"] = "Overskredet",
            ["CompliancePercentage"] = "Compliance %",
            ["ComplianceOverviewTitle"] = "Compliance oversigt",
            ["Total"] = "I alt",
            ["ComplianceDetails"] = "Detaljer",
            ["ComplianceDetailsTitle"] = "Compliance",
            ["Date"] = "Dato",
            ["Property"] = "Ejendom",
            ["CalendarBoard"] = "Kalender",
            ["StartTime"] = "Kl.",
            ["Task"] = "Opgave",
            ["Worker"] = "Medarbejder",
            ["Tags"] = "Etiketter",
            ["TagsPlain"] = "Tags",
            ["Status"] = "Status",
            ["Done"] = "Udført",
            ["NotDone"] = "Ikke udført",
            // Rapport (#1192): the formatted image count, the appendix case label.
            ["ImagesCount"] = "{0} billeder",
            ["ImagesCountOne"] = "1 billede",
            ["Case"] = "Sag",
            ["Appendix"] = "Bilag"
        };

        public string GetString(string key) => Danish.TryGetValue(key, out var value) ? value : key;

        /// <summary>
        /// The real service's <c>string.Format</c> over the localised value — what
        /// turns <c>{0} billeder</c> into <c>3 billeder</c>.
        /// </summary>
        public string GetString(string format, params object[] args) => string.Format(GetString(format), args);

        /// <summary>
        /// The real <c>BackendConfigurationLocalizationService.GetStringWithFormat</c>:
        /// it formats ONLY when there are arguments and returns the localised
        /// value untouched otherwise. Delegating to <c>GetString(format, args)</c>
        /// instead would run <c>string.Format</c> over an empty argument list and
        /// throw <c>FormatException</c> for any value containing a <c>{0}</c> —
        /// a failure mode the real service does not have.
        /// </summary>
        public string GetStringWithFormat(string format, params object[] args)
        {
            var value = GetString(format);
            return args?.Length > 0 ? string.Format(value, args) : value;
        }
    }
}
