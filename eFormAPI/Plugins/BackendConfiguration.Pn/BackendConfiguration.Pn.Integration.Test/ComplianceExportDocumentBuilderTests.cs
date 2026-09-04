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
/// asks for rather than one locale's translation of it.
/// </para>
/// </summary>
[Parallelizable(ParallelScope.All)]
[TestFixture]
public class ComplianceExportDocumentBuilderTests
{
    private readonly BackendConfigurationLocalizationService _localization = new();

    private const string Dash = "–"; // U+2013, the single empty-cell glyph

    // ==================================================================
    // Oversigt
    // ==================================================================

    /// <summary>
    /// The Oversigt column set is exactly three columns. "Opgaver i alt" and
    /// "Udført" are computed by the service and deliberately NOT rendered, and a
    /// prototype test pins their absence — so the export must not leak them in
    /// either.
    /// </summary>
    [Test]
    public void Overview_HasExactlyThreeColumns_AndNoTotalOrDoneColumn()
    {
        var document = ComplianceExportDocumentBuilder.BuildOverview(
            new ComplianceReportOverviewModel(), "01.01.2026 - 31.03.2026", _localization);

        Assert.That(document.Tables, Has.Count.EqualTo(1));
        var headers = document.Tables[0].Columns.Select(c => c.Header).ToList();
        Assert.That(headers, Is.EqualTo(new[] { "Property", "Overdue", "CompliancePercentage" }));
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
        // 1, not 50: weighted, taken verbatim from the service's Totals.
        Assert.That(totals.Cells[2].Number, Is.EqualTo(1));
    }

    /// <summary>
    /// A property whose work has not fallen due has a NULL percentage, never 0.
    /// It renders as the en dash — rendering it as a red 0 % would be a lie, and
    /// rendering it as an empty string would be indistinguishable from a bug.
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
    /// are not data.
    /// </summary>
    [Test]
    public void Details_HasTheEightPrototypeColumnsInOrder()
    {
        var document = ComplianceExportDocumentBuilder.BuildDetails([], "p", _localization);
        var headers = document.Tables[0].Columns.Select(c => c.Header).ToList();

        Assert.That(headers, Is.EqualTo(new[]
        {
            "Date", "Property", "CalendarBoard", "StartTime", "Task", "Worker", "Tags", "Status"
        }));
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
    }

    /// <summary>
    /// An all-day occurrence has no clock time, so "Kl." is the en dash rather than
    /// a fabricated 00:00. A timed occurrence renders its fractional start hour as
    /// HH:mm.
    /// </summary>
    [Test]
    public void Details_AllDayHasNoClockTimeAndATimedRowRendersHhMm()
    {
        var document = ComplianceExportDocumentBuilder.BuildDetails(
        [
            new ComplianceReportRowModel { TaskDate = "2026-03-09", IsAllDay = true, StartHour = 9.0 },
            new ComplianceReportRowModel { TaskDate = "2026-03-09", IsAllDay = false, StartHour = 9.5 }
        ], "p", _localization);

        Assert.That(document.Tables[0].Rows[0].Cells[3].Text, Is.EqualTo(Dash));
        Assert.That(document.Tables[0].Rows[1].Cells[3].Text, Is.EqualTo("09:30"));
    }

    /// <summary>
    /// Status is the localised done / not-done pair, and empty worker and tag lists
    /// collapse to the en dash rather than to an empty cell.
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

        var open = document.Tables[0].Rows[1];
        Assert.That(open.Cells[5].Text, Is.EqualTo(Dash));
        Assert.That(open.Cells[6].Text, Is.EqualTo(Dash));
        Assert.That(open.Cells[7].Text, Is.EqualTo("NotDone"));
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
    // Rapport
    // ==================================================================

    /// <summary>
    /// One table per TAG GROUP per TEMPLATE (#1160 decision 5), not one flat table
    /// and not a union of fields. Two templates under one tag give two tables.
    /// </summary>
    [Test]
    public void Report_ProducesOneTablePerTagGroupPerTemplate()
    {
        var groups = new List<ComplianceReportTagGroupModel>
        {
            new()
            {
                TagId = 7, TagName = "Miljøtilsyn",
                Templates =
                [
                    Template(509, "Aflæsning vand"),
                    Template(511, "Aflæsning el")
                ]
            },
            new()
            {
                TagId = 8, TagName = "Drift",
                Templates = [Template(509, "Aflæsning vand")]
            }
        };

        var document = ComplianceExportDocumentBuilder.BuildReport(groups, "p", false, _localization);

        Assert.That(document.Tables, Has.Count.EqualTo(3));
        Assert.That(document.Tables.Select(t => t.Title), Is.EqualTo(new[]
        {
            "Miljøtilsyn – Aflæsning vand",
            "Miljøtilsyn – Aflæsning el",
            "Drift – Aflæsning vand"
        }));
    }

    /// <summary>
    /// The fixed Rapport columns, then the template's REAL answer columns. The
    /// prototype's fabricated placeholders <c>Note</c>, <c>Option 1</c> and
    /// <c>Option 2</c> must not appear as hard-coded headers anywhere — an explicit
    /// acceptance criterion — and neither must <c>Handlinger</c>.
    /// </summary>
    [Test]
    public void Report_FixedColumnsThenRealAnswerColumnsAndNoPlaceholderHeaders()
    {
        var template = Template(509, "Aflæsning vand");
        template.Columns =
        [
            new ComplianceReportColumnModel { Key = "f10", Label = "Målerstand", FieldType = "Number" },
            new ComplianceReportColumnModel { Key = "f11", Label = "Bemærkning", FieldType = "Text" }
        ];

        var document = ComplianceExportDocumentBuilder.BuildReport(
            [new ComplianceReportTagGroupModel { TagId = 1, TagName = "T", Templates = [template] }],
            "p", false, _localization);

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
    }

    /// <summary>
    /// Cells are addressed BY KEY. A case that answered only the second of two
    /// columns must leave the first as the en dash and keep the second in its own
    /// column — the exact failure mode of #1160 finding 3, where a header list is
    /// zipped positionally against a value list and one excluded field shifts every
    /// later column by one.
    /// </summary>
    [Test]
    public void Report_UnansweredColumnGetsTheGlyphAndDoesNotShiftLaterColumns()
    {
        var template = Template(509, "Aflæsning vand");
        template.Columns =
        [
            new ComplianceReportColumnModel { Key = "f10", Label = "Målerstand" },
            new ComplianceReportColumnModel { Key = "f11", Label = "Bemærkning" }
        ];
        template.Cases =
        [
            new ComplianceReportCaseModel
            {
                SdkCaseId = 42, PropertyName = "Gården", Title = "Vand",
                Cells = new Dictionary<string, string> { ["f11"] = "Alt ok" }
            }
        ];

        var document = ComplianceExportDocumentBuilder.BuildReport(
            [new ComplianceReportTagGroupModel { TagId = 1, TagName = "T", Templates = [template] }],
            "p", false, _localization);

        var cells = document.Tables[0].Rows[0].Cells;
        Assert.That(cells[7].Text, Is.EqualTo(Dash));   // f10, unanswered
        Assert.That(cells[8].Text, Is.EqualTo("Alt ok")); // f11, still in ITS column
    }

    /// <summary>
    /// <c>Delrapport</c> is ONE composite column carrying <c>{tag} – {template}</c>,
    /// the same string as the table title, so the grouping survives flattening to a
    /// single CSV stream. <c>Billeder</c> is the image COUNT, not the images, and
    /// <c>Udført dato</c> is case metadata (a typed date), never an answer field.
    /// </summary>
    [Test]
    public void Report_SectionLabelIsCarriedPerRowAndImagesIsACount()
    {
        var template = Template(509, "Aflæsning vand");
        template.Cases =
        [
            new ComplianceReportCaseModel
            {
                SdkCaseId = 42, PropertyName = "Gården", Title = "Vand",
                DoneAt = new DateTime(2026, 3, 9, 14, 30, 0),
                WorkerNames = ["Ann"],
                ImagesCount = 3,
                Images =
                [
                    new ComplianceReportImageModel { FileName = "1_700_a.jpg" },
                    new ComplianceReportImageModel { FileName = "2_700_b.jpg" },
                    new ComplianceReportImageModel { FileName = "3_700_c.jpg" }
                ]
            }
        ];

        var document = ComplianceExportDocumentBuilder.BuildReport(
            [new ComplianceReportTagGroupModel { TagId = 1, TagName = "Miljø", Templates = [template] }],
            "p", false, _localization);

        var cells = document.Tables[0].Rows[0].Cells;
        Assert.That(cells[0].Text, Is.EqualTo("Miljø – Aflæsning vand"));
        Assert.That(cells[1].Number, Is.EqualTo(42));
        Assert.That(cells[4].Date, Is.EqualTo(new DateTime(2026, 3, 9, 14, 30, 0)));
        Assert.That(cells[6].Number, Is.EqualTo(3));

        // Not requested, so no appendix at all — the images stay a count.
        Assert.That(document.Tables[0].ImageBlocks, Is.Empty);
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
        var template = Template(509, "Aflæsning vand");
        template.Cases =
        [
            new ComplianceReportCaseModel
            {
                SdkCaseId = 42, Title = "Vand", TaskDate = "2026-03-09", ImagesCount = 9,
                Images = Enumerable.Range(1, 9)
                    .Select(i => new ComplianceReportImageModel { FileName = $"{i}_700_x.jpg" })
                    .ToList()
            }
        ];

        var document = ComplianceExportDocumentBuilder.BuildReport(
            [new ComplianceReportTagGroupModel { TagId = 1, TagName = "Miljø", Templates = [template] }],
            "p", true, _localization);

        Assert.That(document.Tables[0].ImageBlocks, Has.Count.EqualTo(1));
        var block = document.Tables[0].ImageBlocks[0];
        Assert.That(block.ImageNames,
            Has.Count.EqualTo(ComplianceExportDocumentBuilder.MaxAppendixImagesPerCase));
        Assert.That(block.TotalImages, Is.EqualTo(9));
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
        var template = Template(509, "T");
        template.Cases =
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

        var document = ComplianceExportDocumentBuilder.BuildReport(
            [new ComplianceReportTagGroupModel { TagId = 1, TagName = "M", Templates = [template] }],
            "p", true, _localization);

        Assert.That(document.Tables[0].ImageBlocks[0].ImageNames, Is.EqualTo(new[] { "2_700_b.jpg" }));
        Assert.That(document.Tables[0].ImageBlocks[0].TotalImages, Is.EqualTo(2));
    }

    /// <summary>
    /// The report service shares ONE case model by reference across every tag group
    /// the row belongs to. The ROWS are duplicated per section on purpose; the
    /// photographs must not be — a three-tag case would otherwise contribute three
    /// identical appendix blocks, and the per-case cap bounds a block rather than
    /// the document.
    /// </summary>
    [Test]
    public void Report_ImageAppendixIsEmittedOncePerCaseAcrossTagGroups()
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

        // The same instance in three tag groups, exactly as the service produces it.
        ComplianceReportTemplateGroupModel WithSharedCase()
        {
            var template = Template(509, "Aflæsning vand");
            template.Cases = [sharedCase];
            return template;
        }

        var document = ComplianceExportDocumentBuilder.BuildReport(
            [
                new ComplianceReportTagGroupModel { TagId = 1, TagName = "Miljø", Templates = [WithSharedCase()] },
                new ComplianceReportTagGroupModel { TagId = 2, TagName = "Drift", Templates = [WithSharedCase()] },
                new ComplianceReportTagGroupModel { TagId = null, TagName = null, Templates = [WithSharedCase()] }
            ],
            "p", true, _localization);

        // The row IS in all three sections...
        Assert.That(document.Tables, Has.Count.EqualTo(3));
        Assert.That(document.Tables.Sum(t => t.Rows.Count), Is.EqualTo(3));

        // ...the appendix block is in exactly one, the first.
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
        var template = Template(509, "Aflæsning vand");
        // Four images each, so the per-case cap never bites: 100 cases want 400
        // images and the document ceiling is 200.
        template.Cases = Enumerable.Range(1, 100)
            .Select(c => new ComplianceReportCaseModel
            {
                SdkCaseId = c, Title = "Vand", TaskDate = "2026-03-09", ImagesCount = 4,
                Images = Enumerable.Range(1, 4)
                    .Select(i => new ComplianceReportImageModel { FileName = $"{c}_{i}_700_x.jpg" })
                    .ToList()
            })
            .ToList();

        var document = ComplianceExportDocumentBuilder.BuildReport(
            [new ComplianceReportTagGroupModel { TagId = 1, TagName = "Miljø", Templates = [template] }],
            "p", true, _localization);

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
    /// A tag whose NAME could not be resolved is still a NAMED group — the report
    /// service deliberately keeps a tag id whose name lives in the items-planning
    /// database with no foreign key to it
    /// (<c>BackendConfigurationComplianceReportService.cs:1061-1073</c>). Labelling
    /// it "Uden tag" would merge it with the genuinely untagged group that the
    /// service sorts last precisely to keep the two apart, collapsing two different
    /// Delrapport values into one. It gets the neutral <c>#{TagId}</c> form instead.
    /// </summary>
    [Test]
    public void Report_NamedGroupWithAnUnresolvableNameIsNotLabelledUntagged()
    {
        var document = ComplianceExportDocumentBuilder.BuildReport(
            [
                new ComplianceReportTagGroupModel { TagId = 77, TagName = null, Templates = [Template(509, "T")] },
                new ComplianceReportTagGroupModel { TagId = null, TagName = null, Templates = [Template(509, "T")] }
            ],
            "p", false, _localization);

        Assert.That(document.Tables[0].Title, Is.EqualTo("#77 – T"));
        Assert.That(document.Tables[1].Title, Is.EqualTo("WithoutTag – T"));
        // Two sections, two distinct labels — the Delrapport cell is this same
        // string, so a pivot over the column still reproduces the sections and the
        // two Excel sheet names cannot collide.
        Assert.That(document.Tables[0].Title, Is.Not.EqualTo(document.Tables[1].Title));
    }

    /// <summary>
    /// A template whose schema could not be derived says so in its heading. Zero
    /// columns because derivation FAILED and zero columns because the template has
    /// no answerable fields are different facts, and the export must not render
    /// them identically.
    /// </summary>
    [Test]
    public void Report_SchemaUnavailableIsStatedInTheSectionHeading()
    {
        var template = Template(509, "Aflæsning vand");
        template.SchemaUnavailable = true;

        var document = ComplianceExportDocumentBuilder.BuildReport(
            [new ComplianceReportTagGroupModel { TagId = 1, TagName = "Miljø", Templates = [template] }],
            "p", false, _localization);

        Assert.That(document.Tables[0].Title,
            Is.EqualTo("Miljø – Aflæsning vand (ColumnsUnavailable)"));
    }

    /// <summary>
    /// The untagged group carries no name from the API (#1166 leaves the label to
    /// the consumer), so the export supplies the localised "Uden tag".
    /// </summary>
    [Test]
    public void Report_UntaggedGroupGetsTheLocalisedLabel()
    {
        var document = ComplianceExportDocumentBuilder.BuildReport(
            [new ComplianceReportTagGroupModel { TagId = null, TagName = null, Templates = [Template(509, "T")] }],
            "p", false, _localization);

        Assert.That(document.Tables[0].Title, Is.EqualTo("WithoutTag – T"));
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

    private static ComplianceReportTemplateGroupModel Template(int checkListId, string name) => new()
    {
        CheckListId = checkListId,
        CheckListName = name,
        MergedCheckListIds = [checkListId]
    };
}
