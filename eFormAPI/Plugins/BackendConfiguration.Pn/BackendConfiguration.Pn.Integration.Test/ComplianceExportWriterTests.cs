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
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Coverage for the two compliance-export renderers (CSV and Word/PDF — Excel was
/// removed by product request in #1189) and for the download naming (#1169 §4,
/// §5, §7), plus the Word page shell #1189 added: A4 landscape, a repeated filter
/// header and a branded footer.
///
/// <para>
/// <b>No database, no container, and no <c>soffice</c>.</b> The PDF path is
/// <c>docx → soffice --headless --convert-to pdf</c>, and #1169 is explicit that a
/// test must not shell out for it: LibreOffice is not installed on the CI image, so
/// a test that ran the converter would be asserting the environment rather than the
/// code. What IS asserted here is everything up to that boundary — that the PDF
/// path renders a real docx (an OpenXml <c>WordprocessingDocument</c> with the
/// report's text in it, landscape, with the header and footer parts wired into its
/// section properties), that the converter's timeout constant is finite, and that
/// on a machine with no LibreOffice the converter returns <c>null</c> and leaves no
/// temp directory behind.
/// </para>
///
/// <para>
/// The Word tests use <see cref="DanishShellLocalizer"/>, not the key-returning
/// <c>BackendConfigurationLocalizationService</c> double the other export fixtures
/// use: the page-header assertions are the mock-ups' literal <c>Ejendom:</c> /
/// <c>Kalender:</c> / <c>Periode:</c>, and pinning those (rather than the keys)
/// is the point — the header is the one place the user reads them.
/// </para>
///
/// <para>
/// <b>Stated gaps.</b> Nothing here exercises a RUNNING <c>soffice</c>, so the
/// timeout being honoured (rather than merely declared), the kill on timeout, the
/// bounded drain of the pipes and the client-abort-vs-timeout distinction are all
/// covered by manual verification only. The conversion itself likewise.
/// </para>
/// </summary>
[Parallelizable(ParallelScope.All)]
[TestFixture]
public class ComplianceExportWriterTests
{
    private const string Dash = "–";

    private const string SamplePeriod = "01.01.2026 – 31.03.2026";

    private static ComplianceExportDocument SampleDocument() => new()
    {
        Title = "Detaljer",
        Period = SamplePeriod,
        PropertyLabel = "Ejendom 9",
        BoardLabel = "Miljøtilsyn",
        Tables =
        [
            new ComplianceExportTable
            {
                // Rapport's shape since #1188: the tags caption above the headline.
                Caption = "Miljøtilsyn - Brand",
                Title = "Brandsikkerhed og beredskab",
                Columns =
                [
                    new ComplianceExportColumn { Header = "Dato", Type = ComplianceExportCellType.Date },
                    new ComplianceExportColumn { Header = "Ejendom" },
                    new ComplianceExportColumn { Header = "Overskredet", Type = ComplianceExportCellType.Number }
                ],
                Rows =
                [
                    new ComplianceExportRow
                    {
                        Cells =
                        [
                            ComplianceExportCell.FromDate(new DateTime(2026, 3, 9)),
                            ComplianceExportCell.FromText("Gården"),
                            ComplianceExportCell.FromNumber(4)
                        ]
                    },
                    new ComplianceExportRow
                    {
                        IsTotal = true,
                        Cells =
                        [
                            ComplianceExportCell.FromText("I alt"),
                            new ComplianceExportCell(),
                            ComplianceExportCell.FromNumber(4)
                        ]
                    }
                ]
            }
        ]
    };

    /// <summary>
    /// The #1190 Oversigt mock-up, built through the REAL
    /// <see cref="ComplianceExportDocumentBuilder.BuildOverview"/> with the Danish
    /// localizer, so the CSV and docx assertions below cover the builder-to-bytes
    /// pipe and not a hand-made document that happens to match. The numbers are
    /// the mock-up's: <c>Ejendom 9 | 6 | 25%</c>, totals <c>I alt | 70 | 78%</c>;
    /// the middle row has no due work and therefore a null percentage.
    /// </summary>
    private static ComplianceExportDocument OverviewDocument() =>
        ComplianceExportDocumentBuilder.BuildOverview(
            new ComplianceReportOverviewModel
            {
                Rows =
                [
                    new ComplianceReportOverviewRowModel
                    {
                        PropertyId = 9, PropertyName = "Ejendom 9", Overdue = 6,
                        DueTotal = 8, DueDone = 2, CompliancePct = 25
                    },
                    new ComplianceReportOverviewRowModel
                    {
                        PropertyId = 10, PropertyName = "Ejendom 10", Overdue = 0,
                        DueTotal = 0, DueDone = 0, CompliancePct = null
                    },
                    new ComplianceReportOverviewRowModel
                    {
                        PropertyId = 11, PropertyName = "Ejendom 11", Overdue = 64,
                        DueTotal = 92, DueDone = 76, CompliancePct = 83
                    }
                ],
                Totals = new ComplianceReportOverviewRowModel
                {
                    Overdue = 70, DueTotal = 100, DueDone = 78, CompliancePct = 78
                }
            },
            SamplePeriod,
            new DanishShellLocalizer());

    /// <summary>
    /// The #1191 Detaljer mock-up (p8), built through the REAL
    /// <see cref="ComplianceExportDocumentBuilder.BuildDetails"/> with the Danish
    /// localizer: a completed, timed, tagged row on Tuesday 21 July 2026 and an
    /// open all-day row with no worker and no tag the day before. Build it INSIDE
    /// a test carrying <c>[SetCulture("da-DK")]</c> — the weekday text is
    /// formatted with <c>CurrentCulture</c> at build time, before any <c>await</c>.
    /// </summary>
    private static ComplianceExportDocument DetailsDocument() =>
        ComplianceExportDocumentBuilder.BuildDetails(
            [
                new ComplianceReportRowModel
                {
                    TaskDate = "2026-07-21", StartHour = 13.0, Duration = 1.0, IsAllDay = false,
                    PropertyName = "Ejendom 9", BoardName = "Miljøtilsyn", Title = "Aflæsning vand",
                    WorkerNames = ["Ann Andersen"], Tags = ["Miljø"], Completed = true
                },
                new ComplianceReportRowModel
                {
                    TaskDate = "2026-07-20", IsAllDay = true,
                    PropertyName = "Ejendom 9", BoardName = "Miljøtilsyn", Title = "Rundering",
                    WorkerNames = [], Tags = [], Completed = false
                }
            ],
            SamplePeriod,
            new DanishShellLocalizer());

    /// <summary>
    /// The #1192 Rapport mock-up (p9 PDF, p10 CSV), built through the REAL
    /// <see cref="ComplianceExportDocumentBuilder.BuildReport"/> with the Danish
    /// localizer: two headline sections. "Brandsikkerhed og beredskab" (tags
    /// <c>Miljøtilsyn - Brand</c>) answers <c>f10 Målerstand</c> and
    /// <c>f11 Bemærkning</c> and holds the mock-up's case 2183 (three images,
    /// completed 13.05.2026) plus an open, workerless, imageless case 2184.
    /// "Elinstallationer og eftersyn" (tags <c>Miljøtilsyn - EL</c>) answers the
    /// SAME <c>f11</c> and a DIFFERENT field <c>f20</c> that merely shares the
    /// label "Bemærkning", and holds case 2185 with one image — so the flat CSV
    /// has to merge one column and keep the other apart.
    /// </summary>
    private static ComplianceExportDocument ReportDocument(bool includeImageAppendix)
    {
        static ComplianceReportImageModel Image(int id) => new() { FileName = $"{id}_700_a.jpg" };

        var brand = new ComplianceReportHeadlineGroupModel
        {
            HeadlineTagId = 7, HeadlineName = "Brandsikkerhed og beredskab", TagsCaption = "Miljøtilsyn - Brand",
            CheckListIds = [509],
            Columns =
            [
                new ComplianceReportColumnModel { Key = "f10", Label = "Målerstand" },
                new ComplianceReportColumnModel { Key = "f11", Label = "Bemærkning" }
            ],
            Cases =
            [
                new ComplianceReportCaseModel
                {
                    SdkCaseId = 2183, PropertyName = "Ejendom 9", Title = "Kontrol af arbejdsmiljø",
                    Tags = ["Miljøtilsyn", "Brand"], WorkerNames = ["Ann Andersen"],
                    DoneAt = new DateTime(2026, 5, 13, 10, 0, 0), TaskDate = "2026-05-13",
                    ImagesCount = 3, Images = [Image(1), Image(2), Image(3)],
                    Cells = new Dictionary<string, string> { ["f10"] = "12", ["f11"] = "Alt ok" }
                },
                new ComplianceReportCaseModel
                {
                    SdkCaseId = 2184, PropertyName = "Ejendom 9", Title = "Rundering",
                    Tags = ["Miljøtilsyn", "Brand"], WorkerNames = [], TaskDate = "2026-05-14",
                    ImagesCount = 0, Images = [],
                    Cells = new Dictionary<string, string> { ["f11"] = "Ok" }
                }
            ]
        };

        var el = new ComplianceReportHeadlineGroupModel
        {
            HeadlineTagId = 8, HeadlineName = "Elinstallationer og eftersyn", TagsCaption = "Miljøtilsyn - EL",
            CheckListIds = [511],
            Columns =
            [
                new ComplianceReportColumnModel { Key = "f11", Label = "Bemærkning" },
                new ComplianceReportColumnModel { Key = "f20", Label = "Bemærkning" }
            ],
            Cases =
            [
                new ComplianceReportCaseModel
                {
                    SdkCaseId = 2185, PropertyName = "Ejendom 9", Title = "El-tavle",
                    Tags = ["Miljøtilsyn", "EL"], WorkerNames = ["Bo"],
                    DoneAt = new DateTime(2026, 5, 20, 8, 0, 0), TaskDate = "2026-05-20",
                    ImagesCount = 1, Images = [Image(4)],
                    Cells = new Dictionary<string, string> { ["f11"] = "Fint", ["f20"] = "Tavle ok" }
                }
            ]
        };

        return ComplianceExportDocumentBuilder.BuildReport(
            [brand, el], SamplePeriod, includeImageAppendix, new DanishShellLocalizer());
    }

    // ==================================================================
    // CSV
    // ==================================================================

    /// <summary>
    /// The #1190 Oversigt CSV, line for line (mock-up p7): the header is line 1
    /// and reads <c>Virksomhed;Overskredet;Compliance %</c>, each data row carries
    /// the sign on its percentage, and the last line is the totals row. UTF-8 BOM,
    /// <c>;</c>, CRLF — the same invariants as every other CSV. <c>78%</c> is
    /// deliberately a text cell in a spreadsheet; that is what the mock-up shows.
    ///
    /// <para>
    /// The null percentage on the middle row is a BLANK field, not the en dash:
    /// #1191 owns the CSV rendering of an absent value for all three views, and
    /// the mock-ups' CSVs leave empty cells empty. Word/PDF keep the glyph — see
    /// <see cref="Word_OverviewHasTitleVirksomhedHeaderPercentCellsAndBoldTotalsRow"/>.
    /// </para>
    /// </summary>
    [Test]
    public void Csv_OverviewSnapshot_VirksomhedHeaderPercentCellsAndTotalsLast()
    {
        using var stream = ComplianceExportCsvWriter.Write(OverviewDocument());
        var bytes = ReadAll(stream);

        Assert.That(bytes.Take(3), Is.EqualTo(new byte[] { 0xEF, 0xBB, 0xBF }));
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        Assert.That(text, Is.EqualTo(
            "Virksomhed;Overskredet;Compliance %\r\n" +
            "Ejendom 9;6;25%\r\n" +
            "Ejendom 10;0;\r\n" +
            "Ejendom 11;64;83%\r\n" +
            "I alt;70;78%\r\n"));
        Assert.That(text, Does.Not.Contain(Dash));

        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.That(lines[0], Is.EqualTo("Virksomhed;Overskredet;Compliance %"));
        Assert.That(lines[^1], Is.EqualTo("I alt;70;78%"));
        // The screen's header wording does not leak into the export, and the
        // document title stays out of the CSV (it is in the file name).
        Assert.That(text, Does.Not.Contain("Ejendom;"));
        Assert.That(text, Does.Not.Contain("Compliance oversigt"));
    }

    /// <summary>
    /// The four format invariants that make a Danish Excel open the file with its
    /// columns split: a UTF-8 BOM, <c>;</c> separators, CRLF line endings, and
    /// non-ASCII preserved as UTF-8 rather than transliterated.
    /// </summary>
    [Test]
    public void Csv_HasBomSemicolonsCrlfAndUtf8()
    {
        using var stream = ComplianceExportCsvWriter.Write(SampleDocument());
        var bytes = ReadAll(stream);

        Assert.That(bytes[0], Is.EqualTo(0xEF));
        Assert.That(bytes[1], Is.EqualTo(0xBB));
        Assert.That(bytes[2], Is.EqualTo(0xBF));

        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        Assert.That(text, Does.Contain("Dato;Ejendom;Overskredet\r\n"));
        Assert.That(text, Does.Contain("Gården"));
        // Every newline is a CRLF: no bare LF survives.
        Assert.That(text.Replace("\r\n", string.Empty), Does.Not.Contain("\n"));
    }

    /// <summary>
    /// <b>The header row is LINE 1.</b> No document title, no period line, no blank
    /// separator ahead of it — Excel's "use first row as header",
    /// <c>pandas.read_csv</c> and Power Query all read the first line as the header,
    /// and a preamble would force every one of them into a manual three-row skip.
    /// The view, the period, the property and the board are already in the FILE
    /// NAME; the title page belongs to Word and PDF, which keep theirs.
    /// </summary>
    [Test]
    public void Csv_FirstLineIsTheHeaderRowWithNoPreamble()
    {
        var document = SampleDocument();
        using var stream = ComplianceExportCsvWriter.Write(document);
        var bytes = ReadAll(stream);

        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        var firstLine = text.Split("\r\n")[0];

        Assert.That(firstLine, Is.EqualTo("Dato;Ejendom;Overskredet"));
        // The document title and the period reach the file name, not the file.
        Assert.That(text, Does.Not.Contain(document.Title));
        Assert.That(text, Does.Not.Contain(document.Period!));
    }

    /// <summary>
    /// The CSV date is ISO <c>yyyy-MM-dd</c> — unambiguous regardless of who opens
    /// the file — while the same cell's display text is the Danish
    /// <c>dd.MM.yyyy</c>. This is the prototype's <c>dateIso</c> vs
    /// <c>dateDisplay</c> distinction, from ONE typed cell rather than three
    /// pre-rendered strings.
    /// </summary>
    [Test]
    public void Csv_WritesDatesAsIsoNotAsTheDisplayString()
    {
        using var stream = ComplianceExportCsvWriter.Write(SampleDocument());
        var text = Encoding.UTF8.GetString(ReadAll(stream));

        Assert.That(text, Does.Contain("2026-03-09;Gården;4\r\n"));
        Assert.That(text, Does.Not.Contain("09.03.2026;Gården"));
    }

    /// <summary>
    /// An empty cell is a BLANK field (<c>;;</c>) in the CSV, not the en dash the
    /// Word/PDF renderer prints (#1191). The writer reads the cell's
    /// <c>IsEmpty</c> flag, not its text — the text IS the en dash, baked in by the
    /// factories for Word/PDF — so every empty path is covered here: a bare
    /// <c>new ComplianceExportCell()</c>, <c>FromText(null)</c>,
    /// <c>FromText("  ")</c>, <c>FromNumber(null)</c> and <c>FromDate(null)</c>,
    /// while a genuine en-dash VALUE is still written out.
    /// </summary>
    [Test]
    public void Csv_EmptyCellIsABlankFieldNotTheEnDash()
    {
        using var stream = ComplianceExportCsvWriter.Write(SampleDocument());
        var text = Encoding.UTF8.GetString(ReadAll(stream));

        Assert.That(text, Does.Contain("I alt;;4\r\n"));
        Assert.That(text, Does.Not.Contain(Dash));

        var document = new ComplianceExportDocument
        {
            Tables =
            [
                new ComplianceExportTable
                {
                    Columns =
                    [
                        new ComplianceExportColumn { Header = "a" },
                        new ComplianceExportColumn { Header = "b" },
                        new ComplianceExportColumn { Header = "c" },
                        new ComplianceExportColumn { Header = "d", Type = ComplianceExportCellType.Number },
                        new ComplianceExportColumn { Header = "e", Type = ComplianceExportCellType.Date },
                        new ComplianceExportColumn { Header = "f" }
                    ],
                    Rows =
                    [
                        new ComplianceExportRow
                        {
                            Cells =
                            [
                                new ComplianceExportCell(),
                                ComplianceExportCell.FromText(null),
                                ComplianceExportCell.FromText("   "),
                                ComplianceExportCell.FromNumber(null),
                                ComplianceExportCell.FromDate(null),
                                ComplianceExportCell.FromText(Dash)
                            ]
                        }
                    ]
                }
            ]
        };

        using var stream2 = ComplianceExportCsvWriter.Write(document);
        var bytes = ReadAll(stream2);
        var text2 = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        Assert.That(text2, Is.EqualTo($"a;b;c;d;e;f\r\n;;;;;{Dash}\r\n"));
    }

    /// <summary>
    /// The #1191 Detaljer CSV, line for line (mock-up p8): line 1 is exactly
    /// <c>Dato;Ejendom;Kalender;Kl.;Opgave;Medarbejder;Tags;Status</c> —
    /// <c>Tags</c>, not <c>Etiketter</c> — the date is ISO (never the weekday
    /// text the docx shows), <c>Kl.</c> is the range, and the all-day row's empty
    /// time, worker and tag cells are blank fields (<c>;;</c>), not en dashes.
    /// No totals row.
    /// </summary>
    [Test]
    [SetCulture("da-DK")]
    public void Csv_DetailsSnapshot_HeaderIsoDateRangeAndBlankEmptyCells()
    {
        using var stream = ComplianceExportCsvWriter.Write(DetailsDocument());
        var bytes = ReadAll(stream);

        Assert.That(bytes.Take(3), Is.EqualTo(new byte[] { 0xEF, 0xBB, 0xBF }));
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        Assert.That(text, Is.EqualTo(
            "Dato;Ejendom;Kalender;Kl.;Opgave;Medarbejder;Tags;Status\r\n" +
            "2026-07-21;Ejendom 9;Miljøtilsyn;13:00 - 14:00;Aflæsning vand;Ann Andersen;Miljø;Udført\r\n" +
            "2026-07-20;Ejendom 9;Miljøtilsyn;;Rundering;;;Ikke udført\r\n"));

        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.That(lines[0], Is.EqualTo("Dato;Ejendom;Kalender;Kl.;Opgave;Medarbejder;Tags;Status"));
        Assert.That(lines, Has.Length.EqualTo(3));
        Assert.That(text, Does.Not.Contain(Dash));
        Assert.That(text, Does.Not.Contain("Tirsdag"));
        Assert.That(text, Does.Not.Contain("Etiketter"));
        Assert.That(text, Does.Not.Contain("I alt"));
        // The range's hyphen is never in first position, so the formula guard
        // leaves it alone.
        Assert.That(text, Does.Not.Contain("'13:00"));
    }

    /// <summary>
    /// Quote-on-demand, the prototype's <c>escapeCsvCell</c> rule kept verbatim: a
    /// cell is quoted only when it contains <c>;</c>, <c>"</c>, CR or LF, and an
    /// embedded quote is doubled. Over-quoting would be harmless but would make the
    /// two implementations produce different bytes for the same data.
    /// </summary>
    [Test]
    [TestCase("plain", "plain")]
    [TestCase("has;semicolon", "\"has;semicolon\"")]
    [TestCase("has\"quote", "\"has\"\"quote\"")]
    [TestCase("has\nnewline", "\"has\nnewline\"")]
    [TestCase("has\rreturn", "\"has\rreturn\"")]
    [TestCase("has,comma", "has,comma")]
    public void Csv_QuotesOnlyWhenNeeded(string input, string expected)
    {
        Assert.That(ComplianceExportCsvWriter.Escape(input), Is.EqualTo(expected));
    }

    /// <summary>
    /// <b>CSV formula injection.</b> Every Rapport answer cell is worker-typed free
    /// text and property, task and tag names are equally user-supplied, so a cell
    /// beginning <c>=</c>, <c>+</c>, <c>-</c>, <c>@</c>, TAB or CR would otherwise
    /// be executed as a formula by Excel, LibreOffice and Google Sheets when the
    /// file is opened — a file arriving from the company's own compliance endpoint.
    /// The writer prefixes those with an apostrophe, the spreadsheet convention for
    /// "literal text".
    /// </summary>
    [Test]
    [TestCase("=cmd|'/c calc'!A0", "'=cmd|'/c calc'!A0")]
    [TestCase("+1+cmd|'/c calc'!A0", "'+1+cmd|'/c calc'!A0")]
    [TestCase("-2+3", "'-2+3")]
    [TestCase("@SUM(1+1)*cmd|'/c calc'!A0", "'@SUM(1+1)*cmd|'/c calc'!A0")]
    [TestCase("\tleading tab", "'\tleading tab")]
    public void Csv_NeutralisesALeadingFormulaCharacter(string input, string expected)
    {
        Assert.That(ComplianceExportCsvWriter.Escape(input), Is.EqualTo(expected));
    }

    /// <summary>
    /// The guard is FIRST-CHARACTER ONLY. A value that merely CONTAINS a formula
    /// character is ordinary data — <c>2+2=4</c> is a plausible answer, <c>A-B</c>
    /// a plausible task name, an e-mail address a plausible worker field — and
    /// mangling it would corrupt the export to no purpose. The en dash the writer
    /// uses for an absent value (U+2013, not the hyphen-minus) is likewise
    /// untouched.
    /// </summary>
    [Test]
    [TestCase("2+2=4")]
    [TestCase("A-B")]
    [TestCase("rm@microting.dk")]
    [TestCase("–")]
    [TestCase("Gården")]
    public void Csv_LeavesAContainedFormulaCharacterAlone(string input)
    {
        Assert.That(ComplianceExportCsvWriter.Escape(input), Is.EqualTo(input));
    }

    /// <summary>
    /// The guard and the quote rule compose: the apostrophe goes on first, then the
    /// cell is quoted because it contains a separator. The apostrophe is inside the
    /// quotes, which is where a spreadsheet expects it.
    /// </summary>
    [Test]
    public void Csv_GuardAndQuotingCompose()
    {
        Assert.That(ComplianceExportCsvWriter.Escape("=A1;B1"), Is.EqualTo("\"'=A1;B1\""));
    }

    /// <summary>
    /// End to end: a malicious answer reaches the FILE guarded, not just the
    /// <c>Escape</c> helper.
    /// </summary>
    [Test]
    public void Csv_GuardsAFormulaThatArrivesAsACellValue()
    {
        var document = new ComplianceExportDocument
        {
            Title = "Rapport",
            Tables =
            [
                new ComplianceExportTable
                {
                    Columns = [new ComplianceExportColumn { Header = "Svar" }],
                    Rows =
                    [
                        new ComplianceExportRow
                        {
                            Cells = [ComplianceExportCell.FromText("=cmd|'/c calc'!A0")]
                        }
                    ]
                }
            ]
        };

        using var stream = ComplianceExportCsvWriter.Write(document);
        var bytes = ReadAll(stream);
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        Assert.That(text, Is.EqualTo("Svar\r\n'=cmd|'/c calc'!A0\r\n"));
    }

    /// <summary>
    /// Rapport is several tables and CSV is one stream, so the file is ONE flat
    /// table (#1192, mock-up p10): line 1 is the only header and carries the
    /// union of every table's columns in first-seen order, every table's rows
    /// follow in document order, and there is no blank separator line, no title
    /// line and no repeated header anywhere. A row is blank under a column its
    /// own table lacks. Neither table title nor caption reaches the file.
    /// </summary>
    [Test]
    public void Csv_FlattensSeveralTablesIntoOneHeaderAndAUnionOfColumns()
    {
        var document = SampleDocument();
        document.Tables.Add(new ComplianceExportTable
        {
            Caption = "Miljøtilsyn - EL",
            Title = "Elinstallationer og eftersyn",
            // Shares "Ejendom" with the first table; adds "Kolonne".
            Columns = [new ComplianceExportColumn { Header = "Ejendom" }, new ComplianceExportColumn { Header = "Kolonne" }],
            Rows =
            [
                new ComplianceExportRow
                {
                    Cells = [ComplianceExportCell.FromText("Huset"), ComplianceExportCell.FromText("v")]
                }
            ]
        });

        using var stream = ComplianceExportCsvWriter.Write(document);
        var bytes = ReadAll(stream);
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        Assert.That(text, Is.EqualTo(
            "Dato;Ejendom;Overskredet;Kolonne\r\n" +
            "2026-03-09;Gården;4;\r\n" +
            "I alt;;4;\r\n" +
            ";Huset;;v\r\n"));
        Assert.That(text, Does.Not.Contain("\r\n\r\n"), "no blank separator line");
        Assert.That(text, Does.Not.Contain("Brandsikkerhed og beredskab"));
        Assert.That(text, Does.Not.Contain("Elinstallationer og eftersyn"));
        Assert.That(text, Does.Not.Contain("Miljøtilsyn - EL"));
    }

    /// <summary>
    /// The #1192 Rapport CSV, line for line (mock-up p10), through the real
    /// builder: the header is <c>Delrapport;ID;Ejendom;Udført af;Udført dato;
    /// Område;Billeder;</c> + the union of the two sections' answer columns —
    /// keyed on the FIELD, so the shared <c>f11</c> is one column while
    /// <c>f20</c>, which only shares the label "Bemærkning", stays a second one.
    /// <c>Delrapport</c> IS here (the CsvOnly flag is the Word writer's), there
    /// is no <c>Rapportoverskrift</c> column and no headline anywhere (#1188
    /// decision 4), <c>Udført dato</c> is ISO (the Detaljer CSV's rule; p10's
    /// <c>13.05.2026</c> is a mock-up slip), <c>Billeder</c> is the bare count
    /// and BLANK for none, and every other absent value is blank too.
    /// </summary>
    [Test]
    public void Csv_RapportSnapshot_OneHeaderUnionColumnsIsoDateAndBlanks()
    {
        using var stream = ComplianceExportCsvWriter.Write(ReportDocument(includeImageAppendix: false));
        var bytes = ReadAll(stream);
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        Assert.That(text, Is.EqualTo(
            "Delrapport;ID;Ejendom;Udført af;Udført dato;Område;Billeder;Målerstand;Bemærkning;Bemærkning\r\n" +
            "Miljøtilsyn - Brand;2183;Ejendom 9;Ann Andersen;2026-05-13;Kontrol af arbejdsmiljø;3;12;Alt ok;\r\n" +
            "Miljøtilsyn - Brand;2184;Ejendom 9;;;Rundering;;;Ok;\r\n" +
            "Miljøtilsyn - EL;2185;Ejendom 9;Bo;2026-05-20;El-tavle;1;;Fint;Tavle ok\r\n"));

        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.That(lines, Has.Length.EqualTo(4), "one header line and one line per case");
        Assert.That(lines.Skip(1), Has.None.StartsWith("Delrapport"), "the header is not repeated");
        Assert.That(text, Does.Not.Contain("Rapportoverskrift"));
        Assert.That(text, Does.Not.Contain("Brandsikkerhed og beredskab"));
        Assert.That(text, Does.Not.Contain("billeder"), "the CSV carries the count, not the PDF's text");
        Assert.That(text, Does.Not.Contain("13.05.2026"));
        Assert.That(text, Does.Not.Contain(Dash));
    }

    /// <summary>
    /// The table <c>Caption</c> (#1188, Rapport's tags line) is a Word/PDF-only
    /// element. CSV never writes it — not as a line, not as a column.
    /// </summary>
    [Test]
    public void Csv_IgnoresTheTableCaption()
    {
        using var stream = ComplianceExportCsvWriter.Write(SampleDocument());
        var bytes = ReadAll(stream);
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        Assert.That(text, Does.Not.Contain("Miljøtilsyn - Brand"));
    }

    // ==================================================================
    // Word / PDF boundary
    // ==================================================================

    /// <summary>
    /// The PDF path's first half: a real <c>WordprocessingDocument</c> carrying the
    /// document's text. This is what <c>soffice</c> is handed; the conversion itself
    /// is not exercised, because LibreOffice is not on the CI image and #1169
    /// forbids shelling out from a test.
    ///
    /// <para>
    /// <c>core</c> is null, which is legal precisely because the document carries no
    /// image blocks — the writer only reaches the SDK when there are images to
    /// embed. That is also the evidence that a plain report never touches image
    /// storage at all, over HTTP or otherwise.
    /// </para>
    /// </summary>
    [Test]
    public async Task Word_RendersARealDocxCarryingTheReportText()
    {
        await using var stream = await NewWordWriter().WriteAsync(SampleDocument(), null);
        using var word = WordprocessingDocument.Open(stream, false);

        var text = word.MainDocumentPart!.Document!.InnerText;
        Assert.That(text, Does.Contain("Detaljer"));
        Assert.That(text, Does.Contain("Miljøtilsyn - Brand"));
        Assert.That(text, Does.Contain("Brandsikkerhed og beredskab"));
        Assert.That(text, Does.Contain("Gården"));
        // Dates render dd.MM.yyyy in the document, not ISO.
        Assert.That(text, Does.Contain("09.03.2026"));
    }

    /// <summary>
    /// The #1190 Oversigt docx — what <c>soffice</c> turns into mock-up p6: the
    /// title paragraph is <c>Compliance oversigt</c>, the table's header cells are
    /// <c>Virksomhed | Overskredet | Compliance %</c>, a data row reads
    /// <c>Ejendom 9 | 6 | 25%</c> with the sign in the cell, the null percentage
    /// is the en dash and not <c>0%</c>, and the LAST row is the bold totals row
    /// <c>I alt | 70 | 78%</c> while the data rows are not bold.
    /// </summary>
    [Test]
    public async Task Word_OverviewHasTitleVirksomhedHeaderPercentCellsAndBoldTotalsRow()
    {
        await using var stream = await NewWordWriter().WriteAsync(OverviewDocument(), null);
        using var word = WordprocessingDocument.Open(stream, false);

        var body = word.MainDocumentPart!.Document!.Body!;

        var title = body.Descendants<Paragraph>().First(p => !string.IsNullOrWhiteSpace(p.InnerText));
        Assert.That(title.InnerText.Trim(), Is.EqualTo("Compliance oversigt"));
        Assert.That(title.Descendants<Bold>().Any(), Is.True, "title is not bold");

        var table = body.Descendants<Table>().Single();
        var rows = table.Elements<TableRow>().ToList();
        static string[] CellTexts(TableRow row) =>
            row.Elements<TableCell>().Select(c => c.InnerText.Trim()).ToArray();

        Assert.That(CellTexts(rows[0]), Is.EqualTo(new[] { "Virksomhed", "Overskredet", "Compliance %" }));
        Assert.That(CellTexts(rows[1]), Is.EqualTo(new[] { "Ejendom 9", "6", "25%" }));
        Assert.That(CellTexts(rows[2]), Is.EqualTo(new[] { "Ejendom 10", "0", Dash }));
        Assert.That(CellTexts(rows[^1]), Is.EqualTo(new[] { "I alt", "70", "78%" }));
        Assert.That(rows, Has.Count.EqualTo(5));

        // The totals row is bold (wherever HtmlToOpenXml put the <w:b/>); the data
        // rows are not — that is what sets "I alt" apart on the page.
        Assert.That(rows[^1].Descendants<Bold>().Any(), Is.True, "totals row is not bold");
        Assert.That(rows[1].Descendants<Bold>().Any(), Is.False, "a data row is bold");
        Assert.That(rows[0].Descendants<Bold>().Any(), Is.True, "header row is not bold");

        // Never a bare percentage: every rendered percent cell ends with the sign,
        // and the null one is the glyph alone.
        var percentCells = rows.Skip(1).Select(r => CellTexts(r)[2]).ToList();
        Assert.That(percentCells.Where(t => t != Dash), Is.All.EndsWith("%"));
        Assert.That(percentCells, Does.Not.Contain("0%"));

        // No Oversigt row is tinted: the done tint is Detaljer's (#1191). The
        // header row's own grey shading is the only shading in the table.
        Assert.That(rows.Skip(1).SelectMany(r => r.Descendants<Shading>()), Is.Empty);
    }

    /// <summary>
    /// The #1191 Detaljer docx — what <c>soffice</c> turns into mock-up p8: the
    /// title paragraph is <c>Compliance</c> (not the view label <c>Detaljer</c>),
    /// the header cells are exactly <c>Dato | Ejendom | Kalender | Kl. | Opgave |
    /// Medarbejder | Tags | Status</c>, the <c>Dato</c> cell is the long Danish
    /// weekday form <c>Tirsdag 21. juli</c> (never <c>21.07.2026</c>), <c>Kl.</c>
    /// is the range, the empty cells are the en dash (Word/PDF keep it; only CSV
    /// blanks), <c>Status</c> is <c>Udført</c> / <c>Ikke udført</c>, and there is
    /// no totals row.
    ///
    /// <para>
    /// The done tint: HtmlToOpenXml 3.5.0 turns the writer's
    /// <c>&lt;tr style='background-color:#e8f5e9'&gt;</c> into a
    /// <c>TableCellProperties/Shading</c> with fill <c>E8F5E9</c> on EVERY cell of
    /// that row, and emits no <c>Shading</c> at all for an unstyled row — so the
    /// assertion is "every cell of the done row is shaded pale green, no cell of
    /// the open row is shaded".
    /// </para>
    /// </summary>
    [Test]
    [SetCulture("da-DK")]
    public async Task Word_DetailsHasTitleComplianceWeekdayDateRangeAndTintsDoneRowsOnly()
    {
        var document = DetailsDocument();
        await using var stream = await NewWordWriter().WriteAsync(document, null);
        using var word = WordprocessingDocument.Open(stream, false);

        var body = word.MainDocumentPart!.Document!.Body!;

        var title = body.Descendants<Paragraph>().First(p => !string.IsNullOrWhiteSpace(p.InnerText));
        Assert.That(title.InnerText.Trim(), Is.EqualTo("Compliance"));
        Assert.That(body.InnerText, Does.Not.Contain("Detaljer"));

        var table = body.Descendants<Table>().Single();
        var rows = table.Elements<TableRow>().ToList();
        static string[] CellTexts(TableRow row) =>
            row.Elements<TableCell>().Select(c => c.InnerText.Trim()).ToArray();

        Assert.That(rows, Has.Count.EqualTo(3), "header + two data rows, no totals row");
        Assert.That(CellTexts(rows[0]), Is.EqualTo(new[]
        {
            "Dato", "Ejendom", "Kalender", "Kl.", "Opgave", "Medarbejder", "Tags", "Status"
        }));
        Assert.That(CellTexts(rows[1]), Is.EqualTo(new[]
        {
            "Tirsdag 21. juli", "Ejendom 9", "Miljøtilsyn", "13:00 - 14:00",
            "Aflæsning vand", "Ann Andersen", "Miljø", "Udført"
        }));
        Assert.That(CellTexts(rows[2]), Is.EqualTo(new[]
        {
            "Mandag 20. juli", "Ejendom 9", "Miljøtilsyn", Dash,
            "Rundering", Dash, Dash, "Ikke udført"
        }));
        Assert.That(body.InnerText, Does.Not.Contain("21.07.2026"));
        Assert.That(body.InnerText, Does.Not.Contain("2026-07-21"));
        Assert.That(body.InnerText, Does.Not.Contain("I alt"));

        // Only the header row is bold — there is no totals row to set apart.
        Assert.That(rows[0].Descendants<Bold>().Any(), Is.True, "header row is not bold");
        Assert.That(rows[1].Descendants<Bold>().Any(), Is.False, "done row is bold");
        Assert.That(rows[2].Descendants<Bold>().Any(), Is.False, "open row is bold");

        // The done row is tinted on every cell; the open row on none.
        static string?[] CellFills(TableRow row) =>
            row.Elements<TableCell>()
                .Select(c => c.TableCellProperties?.GetFirstChild<Shading>()?.Fill?.Value?.ToUpperInvariant())
                .ToArray();

        Assert.That(CellFills(rows[1]), Is.All.EqualTo("E8F5E9"), "done row is not tinted on every cell");
        Assert.That(rows[2].Descendants<Shading>(), Is.Empty, "open row is tinted");
        Assert.That(CellFills(rows[2]), Is.All.Null);
        Assert.That(ComplianceExportWordWriter.DoneRowFill, Is.EqualTo("#e8f5e9"));
    }

    // ------------------------------------------------------------------
    // The page shell (#1189): landscape, header, footer
    // ------------------------------------------------------------------

    /// <summary>
    /// The mock-ups are "A4-liggende". The embedded template is A4 PORTRAIT
    /// (<c>w:w="11909" w:h="16834"</c>); the writer swaps the two on its in-memory
    /// copy and states the orientation explicitly, which is what HtmlToOpenXml and
    /// LibreOffice both read.
    /// </summary>
    [Test]
    public async Task Word_PageIsA4Landscape()
    {
        await using var stream = await NewWordWriter().WriteAsync(SampleDocument(), null);
        using var word = WordprocessingDocument.Open(stream, false);

        var pageSize = SectionProperties(word).GetFirstChild<PageSize>();

        Assert.That(pageSize, Is.Not.Null);
        Assert.That(pageSize!.Orient!.Value, Is.EqualTo(PageOrientationValues.Landscape));
        Assert.That(pageSize.Width!.Value, Is.EqualTo(16834U));
        Assert.That(pageSize.Height!.Value, Is.EqualTo(11909U));
    }

    /// <summary>
    /// A default-type header part is REFERENCED from <c>sectPr</c> — the template
    /// has none, so an unreferenced part would be silently ignored — and its text
    /// is the mock-ups' one line: the bold <c>Ejendom:</c> / <c>Kalender:</c> /
    /// <c>Periode:</c> labels, the resolved property and board labels (the same
    /// strings the file name uses) and the period joined with an en dash. The
    /// header distance is raised from the template's <c>w:header="0"</c>, which
    /// would otherwise print the line on the paper's edge.
    /// </summary>
    [Test]
    public async Task Word_HeaderIsReferencedAndCarriesTheFilterLine()
    {
        await using var stream = await NewWordWriter().WriteAsync(SampleDocument(), null);
        using var word = WordprocessingDocument.Open(stream, false);

        var sectPr = SectionProperties(word);
        var reference = sectPr.GetFirstChild<HeaderReference>();
        Assert.That(reference, Is.Not.Null, "sectPr has no headerReference");
        Assert.That(reference!.Type!.Value, Is.EqualTo(HeaderFooterValues.Default));
        // Header/footer references must precede pgSz/pgMar in sectPr.
        Assert.That(sectPr.Elements().First(), Is.InstanceOf<HeaderReference>());

        var headerPart = (HeaderPart)word.MainDocumentPart!.GetPartById(reference.Id!.Value!);
        var text = headerPart.Header!.InnerText;

        Assert.That(text, Does.Contain("Ejendom:"));
        Assert.That(text, Does.Contain("Kalender:"));
        Assert.That(text, Does.Contain("Periode:"));
        Assert.That(text, Does.Contain("Ejendom 9"));
        Assert.That(text, Does.Contain("Miljøtilsyn"));
        Assert.That(text, Does.Contain(SamplePeriod));
        Assert.That(text, Does.Contain($"01.01.2026 {Dash} 31.03.2026"));
        Assert.That(text, Does.Not.Contain("01.01.2026 - 31.03.2026"));

        // The labels are bold runs, the values are not.
        var boldRunTexts = headerPart.Header!.Descendants<Run>()
            .Where(r => r.RunProperties?.Bold != null)
            .Select(r => r.InnerText)
            .ToList();
        Assert.That(boldRunTexts, Is.EquivalentTo(new[] { "Ejendom:", "Kalender:", "Periode:" }));

        var pageMargin = sectPr.GetFirstChild<PageMargin>();
        Assert.That(pageMargin, Is.Not.Null);
        Assert.That(pageMargin!.Header!.Value, Is.GreaterThan(0U));
    }

    /// <summary>
    /// With no property filter and a multi-board selection the service resolves
    /// both labels to "Alle" — but a document can also reach the writer with the
    /// labels unset (older callers, the builder tests). Either way the header says
    /// <c>Alle</c>, never an empty value after a colon.
    /// </summary>
    [Test]
    public async Task Word_HeaderFallsBackToAlleWhenNoPropertyOrBoardLabelIsSet()
    {
        var document = SampleDocument();
        document.PropertyLabel = null;
        document.BoardLabel = string.Empty;

        await using var stream = await NewWordWriter().WriteAsync(document, null);
        using var word = WordprocessingDocument.Open(stream, false);

        var text = word.MainDocumentPart!.HeaderParts.Single().Header!.InnerText;

        Assert.That(text, Does.Contain("Ejendom: Alle"));
        Assert.That(text, Does.Contain("Kalender: Alle"));
        Assert.That(text, Does.Not.Contain("Ejendom 9"));
    }

    /// <summary>
    /// The footer is <c>Microting</c> left and <c>p. n/N</c> right, on every page.
    /// The numbers are FIELDS (<c>PAGE</c> and <c>NUMPAGES</c>), not literals —
    /// only a field can differ per page — and the two literals are exactly that,
    /// not localisation keys. The template's original footer (a bare
    /// <c>1 / 1</c>, no brand) is replaced, not appended to.
    /// </summary>
    [Test]
    public async Task Word_FooterCarriesTheBrandAndPageOfTotalFields()
    {
        await using var stream = await NewWordWriter().WriteAsync(SampleDocument(), null);
        using var word = WordprocessingDocument.Open(stream, false);

        var sectPr = SectionProperties(word);
        var reference = sectPr.GetFirstChild<FooterReference>();
        Assert.That(reference, Is.Not.Null, "sectPr has no footerReference");
        Assert.That(reference!.Type!.Value, Is.EqualTo(HeaderFooterValues.Default));

        var footerPart = (FooterPart)word.MainDocumentPart!.GetPartById(reference.Id!.Value!);
        var footer = footerPart.Footer!;
        var text = footer.InnerText;

        Assert.That(text, Does.StartWith("Microting"));
        Assert.That(text, Does.Contain("p. "));

        var instructions = footer.Descendants<SimpleField>()
            .Select(f => f.Instruction!.Value!.Trim())
            .Concat(footer.Descendants<FieldCode>().Select(f => f.Text.Trim()))
            .ToList();
        Assert.That(instructions, Does.Contain("PAGE"));
        Assert.That(instructions, Does.Contain("NUMPAGES"));

        // p. PAGE / NUMPAGES, in that order, after a tab to the right-hand stop.
        var pageIndex = text.IndexOf("p. ", StringComparison.Ordinal);
        var slashIndex = text.IndexOf('/', pageIndex);
        Assert.That(slashIndex, Is.GreaterThan(pageIndex));
        var tabStop = footer.Descendants<TabStop>().Single();
        Assert.That(tabStop.Val!.Value, Is.EqualTo(TabStopValues.Right));
        Assert.That(tabStop.Position!.Value, Is.EqualTo(16834 - 2 * 1440));
        Assert.That(footer.Descendants<TabChar>().Count(), Is.EqualTo(1));
    }

    /// <summary>
    /// The body no longer opens with a centred title and a centred period line:
    /// the period, property and board are in the page header on every page, and
    /// the title is one LEFT-aligned bold paragraph. Nothing in the body is
    /// centred, and the period appears in the body nowhere.
    /// </summary>
    [Test]
    public async Task Word_BodyHasNoCentredTitleOrPeriodParagraph()
    {
        var document = SampleDocument();
        await using var stream = await NewWordWriter().WriteAsync(document, null);
        using var word = WordprocessingDocument.Open(stream, false);

        var body = word.MainDocumentPart!.Document!.Body!;
        var paragraphs = body.Descendants<Paragraph>().ToList();

        Assert.That(paragraphs.Any(p =>
                p.ParagraphProperties?.Justification?.Val?.Value == JustificationValues.Center),
            Is.False, "a centred paragraph survived");
        Assert.That(body.InnerText, Does.Not.Contain(document.Period!));

        // The title is still there — the first paragraph with text, bold (wherever
        // HtmlToOpenXml chose to put the <w:b/>: run or paragraph mark), not centred.
        Assert.That(paragraphs.First(p => !string.IsNullOrWhiteSpace(p.InnerText)).InnerText.Trim(),
            Is.EqualTo("Detaljer"));
        var title = paragraphs.First(p => p.InnerText.Trim() == "Detaljer");
        Assert.That(title.Descendants<Bold>().Any(), Is.True);
        Assert.That(title.ParagraphProperties?.Justification?.Val?.Value ?? JustificationValues.Left,
            Is.Not.EqualTo(JustificationValues.Center));
    }

    /// <summary>
    /// A document with no title (Rapport, per the format issues) gets no title
    /// paragraph at all — not an empty bold line ahead of the first table.
    /// </summary>
    [Test]
    public async Task Word_OmitsTheTitleParagraphWhenTheDocumentHasNone()
    {
        var document = SampleDocument();
        document.Title = string.Empty;

        await using var stream = await NewWordWriter().WriteAsync(document, null);
        using var word = WordprocessingDocument.Open(stream, false);

        var paragraphs = word.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>()
            .Where(p => !string.IsNullOrWhiteSpace(p.InnerText))
            .Select(p => p.InnerText.Trim())
            .ToList();
        // The first table's caption, then its title — nothing ahead of them.
        Assert.That(paragraphs.Take(2), Is.EqualTo(new[] { "Miljøtilsyn - Brand", "Brandsikkerhed og beredskab" }));
    }

    /// <summary>
    /// Rapport's tags caption (#1188, PDF page 5; restyled by #1192, page 9) is a
    /// small GREY paragraph — 9 pt (<c>w:sz 18</c>), <c>#666666</c> — directly
    /// ABOVE the bold headline: not bold itself, and not part of the heading
    /// text. HtmlToOpenXml turns the writer's <c>color:#666666</c> into a
    /// <c>w:color</c> run property, which is what is pinned here.
    /// </summary>
    [Test]
    public async Task Word_RapportTableCaptionIsASmallGreyParagraphAboveTheBoldTitle()
    {
        await using var stream = await NewWordWriter().WriteAsync(SampleDocument(), null);
        using var word = WordprocessingDocument.Open(stream, false);

        var paragraphs = word.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>()
            .Where(p => !string.IsNullOrWhiteSpace(p.InnerText))
            .ToList();

        var caption = paragraphs.Single(p => p.InnerText.Trim() == "Miljøtilsyn - Brand");
        var title = paragraphs.Single(p => p.InnerText.Trim() == "Brandsikkerhed og beredskab");

        Assert.That(paragraphs.IndexOf(caption), Is.EqualTo(paragraphs.IndexOf(title) - 1),
            "the caption is the paragraph immediately above the title");
        Assert.That(caption.Descendants<Bold>().Any(), Is.False, "the caption is plain");
        Assert.That(caption.Descendants<FontSize>().Select(f => f.Val?.Value), Does.Contain("18"),
            "the caption is 9 pt");
        Assert.That(caption.Descendants<Color>().Select(c => c.Val?.Value?.ToUpperInvariant()),
            Does.Contain("666666"), "the caption is grey");

        Assert.That(title.Descendants<Bold>().Any(), Is.True, "the headline is bold");
        Assert.That(title.Descendants<Color>().Select(c => c.Val?.Value?.ToUpperInvariant()),
            Does.Not.Contain("666666"), "the headline is not grey");
    }

    /// <summary>
    /// The Word/PDF Rapport table starts at <c>ID</c> (#1192, PDF page 9): the
    /// <c>Delrapport</c> column — marked <c>CsvOnly</c> by the builder — has no
    /// header cell and no data cell in the docx, and the remaining cells keep
    /// their alignment with their headers. <c>Billeder</c> reads <c>3 billeder</c>
    /// and, for a case without images, the en dash; <c>Udført dato</c> is
    /// <c>dd.MM.yyyy</c>. The section's caption and headline sit above the
    /// table, which is why the column is redundant there and not in the CSV.
    /// </summary>
    [Test]
    public async Task Word_RapportTableStartsAtIdWithoutTheCsvOnlyDelrapportColumn()
    {
        await using var stream = await NewWordWriter().WriteAsync(ReportDocument(includeImageAppendix: false), null);
        using var word = WordprocessingDocument.Open(stream, false);

        var body = word.MainDocumentPart!.Document!.Body!;
        Assert.That(body.InnerText, Does.Not.Contain("Delrapport"));
        Assert.That(body.InnerText, Does.Not.Contain("Rapportoverskrift"));

        var tables = body.Descendants<Table>().ToList();
        Assert.That(tables, Has.Count.EqualTo(2), "one table per section, no appendix grid");

        static string[] CellTexts(TableRow row) =>
            row.Elements<TableCell>().Select(c => c.InnerText.Trim()).ToArray();

        var brand = tables[0].Elements<TableRow>().ToList();
        Assert.That(CellTexts(brand[0]), Is.EqualTo(new[]
        {
            "ID", "Ejendom", "Udført af", "Udført dato", "Område", "Billeder", "Målerstand", "Bemærkning"
        }));
        Assert.That(CellTexts(brand[1]), Is.EqualTo(new[]
        {
            "2183", "Ejendom 9", "Ann Andersen", "13.05.2026", "Kontrol af arbejdsmiljø", "3 billeder", "12", "Alt ok"
        }));
        Assert.That(CellTexts(brand[2]), Is.EqualTo(new[]
        {
            "2184", "Ejendom 9", Dash, Dash, "Rundering", Dash, Dash, "Ok"
        }));
        Assert.That(brand[1].Elements<TableCell>().Count(), Is.EqualTo(brand[0].Elements<TableCell>().Count()),
            "the data row drops exactly the cell whose header was dropped");

        var el = tables[1].Elements<TableRow>().ToList();
        Assert.That(CellTexts(el[0])[0], Is.EqualTo("ID"));
        Assert.That(CellTexts(el[1])[0], Is.EqualTo("2185"));
        Assert.That(CellTexts(el[1])[5], Is.EqualTo("1 billede"), "one image reads the singular");
    }

    /// <summary>
    /// The appendix layout (#1192, PDF page 9): after ALL the tables, exactly one
    /// page-break paragraph per section that has images, headed
    /// <c>Bilag – {tags caption}</c> (en dash); under it, per case, the caption
    /// <c>Sag {id} · {Område} · {dd.MM.yyyy}</c> and the images in a two-column
    /// grid — a table with two cells per row, an odd count ending on an empty
    /// cell. A section without images gets no page.
    ///
    /// <para>
    /// <c>core</c> is null, so no image BYTES are embedded — the writer needs the
    /// SDK for those, which is why the acceptance criterion for the rendered
    /// photographs is the once-only manual check in the PR. What the docx pins
    /// is the structure the bytes go into: the grid cells exist, two per row,
    /// whether or not a photograph could be resolved for them. HtmlToOpenXml may
    /// express a <c>page-break-before</c> either as a paragraph property or as a
    /// page <c>w:br</c> run; both count.
    /// </para>
    ///
    /// <para>
    /// The grid is BORDERLESS — a photo layout, not a data table. HtmlToOpenXml
    /// 3.5.0 gives every table the <c>TableGrid</c> style (visible borders) and
    /// ignores <c>border="0"</c>; only <c>style="border:none"</c> is honoured,
    /// as <c>w:tblBorders</c> on the table and <c>w:tcBorders</c> on each cell,
    /// every edge <c>w:val="none"</c>. The test pins exactly that, on both grids
    /// and on every grid cell, and pins that the SECTION tables do NOT carry it
    /// — they keep their <c>border="1"</c> lines.
    /// </para>
    /// </summary>
    [Test]
    public async Task Word_AppendixHasOnePageBreakPerSectionWithImagesCaseCaptionsAndATwoColumnGrid()
    {
        var document = ReportDocument(includeImageAppendix: true);
        Assert.That(document.Tables.Select(t => t.ImageBlocks.Count), Is.EqualTo(new[] { 1, 1 }),
            "premise: both sections carry a block");

        await using var stream = await NewWordWriter().WriteAsync(document, null);
        using var word = WordprocessingDocument.Open(stream, false);

        var body = word.MainDocumentPart!.Document!.Body!;
        var elements = body.Descendants().ToList();
        var paragraphs = body.Descendants<Paragraph>().ToList();

        static bool IsPageBreak(Paragraph p) =>
            p.ParagraphProperties?.PageBreakBefore != null
            || p.Descendants<Break>().Any(b => b.Type != null && b.Type.Value == BreakValues.Page);

        var breaks = paragraphs.Where(IsPageBreak).ToList();
        Assert.That(breaks, Has.Count.EqualTo(2), "one page break per section with images");

        // The heading is the break paragraph's own text, or — should the
        // converter have put the break in a paragraph of its own — the next
        // paragraph's.
        string HeadingOf(Paragraph breakParagraph) => paragraphs
            .Skip(paragraphs.IndexOf(breakParagraph))
            .Select(p => p.InnerText.Trim())
            .First(t => t.Length > 0);

        Assert.That(breaks.Select(HeadingOf),
            Is.EqualTo(new[] { "Bilag – Miljøtilsyn - Brand", "Bilag – Miljøtilsyn - EL" }));

        var tables = body.Descendants<Table>().ToList();
        Assert.That(tables, Has.Count.EqualTo(4), "two section tables, then two image grids");
        Assert.That(elements.IndexOf(breaks[0]), Is.GreaterThan(elements.IndexOf(tables[1])),
            "the appendix starts after the last section table");

        // Case captions, in order, each after its section heading.
        var captionParagraphs = paragraphs.Where(p => p.InnerText.Trim().StartsWith("Sag ")).ToList();
        var captions = captionParagraphs.Select(p => p.InnerText.Trim()).ToList();
        Assert.That(captions, Is.EqualTo(new[]
        {
            "Sag 2183 · Kontrol af arbejdsmiljø · 13.05.2026",
            "Sag 2185 · El-tavle · 20.05.2026"
        }));

        // ...and each is PINNED to the grid under it. A caption that ends a page
        // while its photographs open the next one leaves those photographs with
        // no case identity at all — the one thing an image appendix cannot do.
        // The CSS for it (`page-break-after:avoid` in `AppendixCaseStyle`) is
        // silently dropped by HtmlToOpenXml 3.5.0, so what the writer actually
        // emits — and what this pins — is `w:keepNext`, on the caption
        // paragraph, with the grid table as its next sibling.
        foreach (var caption in captionParagraphs)
        {
            var keepNext = caption.ParagraphProperties?.KeepNext;
            Assert.That(keepNext, Is.Not.Null,
                $"'{caption.InnerText.Trim()}' must carry w:keepNext");
            // `<w:keepNext/>` with no `w:val` is ON; an explicit `w:val="0"` is OFF.
            Assert.That(keepNext!.Val == null || keepNext.Val.Value, Is.True,
                $"'{caption.InnerText.Trim()}' carries w:keepNext w:val=\"0\"");
            Assert.That(caption.NextSibling(), Is.InstanceOf<Table>(),
                "the caption is immediately followed by its image grid, which is what keepNext binds it to");
        }
        Assert.That(body.InnerText, Does.Not.Contain("Bilag:"), "no per-block 'Bilag:' prefix, no truncation note");

        // Three images → two rows of two cells; one image → one row of two cells.
        static int[] CellsPerRow(Table grid) =>
            grid.Elements<TableRow>().Select(r => r.Elements<TableCell>().Count()).ToArray();
        Assert.That(CellsPerRow(tables[2]), Is.EqualTo(new[] { 2, 2 }));
        Assert.That(CellsPerRow(tables[3]), Is.EqualTo(new[] { 2 }));
        Assert.That(elements.IndexOf(tables[2]), Is.GreaterThan(elements.IndexOf(breaks[0])));
        Assert.That(elements.IndexOf(tables[3]), Is.GreaterThan(elements.IndexOf(breaks[1])));

        // Borderless: w:tblBorders on the grid and w:tcBorders on every cell,
        // each edge val="none" (border="0" would have emitted neither).
        static bool AllEdgesNone(OpenXmlCompositeElement? borders) =>
            borders != null
            && borders.Elements<BorderType>().Any()
            && borders.Elements<BorderType>().All(b => b.Val != null && b.Val.Value == BorderValues.None);

        foreach (var grid in new[] { tables[2], tables[3] })
        {
            Assert.That(AllEdgesNone(grid.GetFirstChild<TableProperties>()?.TableBorders), Is.True,
                "the image grid's table borders are all 'none'");
            Assert.That(grid.Descendants<TableCell>()
                    .All(c => AllEdgesNone(c.TableCellProperties?.TableCellBorders)), Is.True,
                "every grid cell's borders are all 'none'");
        }

        foreach (var section in new[] { tables[0], tables[1] })
        {
            Assert.That(AllEdgesNone(section.GetFirstChild<TableProperties>()?.TableBorders), Is.False,
                "the section tables keep their visible borders");
        }
    }

    /// <summary>
    /// The per-case cap still states itself on the case caption, in the same
    /// <c>(embedded/total)</c> idiom as before (#1169 §6), and the document-wide
    /// ceiling's note is still written once at the end — the layout change did
    /// not silence either.
    /// </summary>
    [Test]
    public async Task Word_AppendixStillStatesThePerCaseCapAndTheDocumentCeiling()
    {
        var document = ReportDocument(includeImageAppendix: true);
        var block = document.Tables[0].ImageBlocks[0];
        block.TotalImages = 9; // as if the case had nine and the per-case cap kept three
        document.AppendixImagesRequested = document.AppendixImagesEmbedded + 5;

        await using var stream = await NewWordWriter().WriteAsync(document, null);
        using var word = WordprocessingDocument.Open(stream, false);

        var text = word.MainDocumentPart!.Document!.Body!.InnerText;
        Assert.That(text, Does.Contain("Sag 2183 · Kontrol af arbejdsmiljø · 13.05.2026 (3/9)"));
        Assert.That(text, Does.Contain($"Bilag: ImageAppendixDocumentLimit ({document.AppendixImagesEmbedded}/{document.AppendixImagesRequested})"));
    }

    /// <summary>
    /// A table without a caption — Oversigt, Detaljer, or a Rapport group whose
    /// cases carry no tags — gets no empty paragraph ahead of its title.
    /// </summary>
    [Test]
    public async Task Word_OmitsTheCaptionParagraphWhenTheTableHasNone()
    {
        var document = SampleDocument();
        document.Title = string.Empty;
        document.Tables[0].Caption = string.Empty;

        await using var stream = await NewWordWriter().WriteAsync(document, null);
        using var word = WordprocessingDocument.Open(stream, false);

        var firstParagraph = word.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>()
            .First(p => !string.IsNullOrWhiteSpace(p.InnerText));
        Assert.That(firstParagraph.InnerText.Trim(), Is.EqualTo("Brandsikkerhed og beredskab"));
    }

    /// <summary>
    /// The page shell must not cost the shared template anything: the five
    /// <c>WordService</c> generators behind <c>GET report/reports/file</c> read the
    /// same <c>file.docx</c>, and it is asserted here to still be A4 portrait with
    /// no header part — i.e. the writer mutates its in-memory copy only.
    /// </summary>
    [Test]
    public void Word_SharedTemplateResourceIsUntouched()
    {
        using var resource = typeof(ComplianceExportWordWriter).Assembly.GetManifestResourceStream(
            "BackendConfiguration.Pn.Resources.Templates.WordExport.file.docx");
        Assert.That(resource, Is.Not.Null);
        using var copy = new MemoryStream();
        resource!.CopyTo(copy);
        copy.Position = 0;
        using var word = WordprocessingDocument.Open(copy, false);

        var pageSize = SectionProperties(word).GetFirstChild<PageSize>()!;
        Assert.That(pageSize.Width!.Value, Is.EqualTo(11909U));
        Assert.That(pageSize.Height!.Value, Is.EqualTo(16834U));
        Assert.That(word.MainDocumentPart!.HeaderParts, Is.Empty);
    }

    private static ComplianceExportWordWriter NewWordWriter() =>
        new(new DanishShellLocalizer(), NullLogger.Instance);

    private static SectionProperties SectionProperties(WordprocessingDocument word)
    {
        var sectPr = word.MainDocumentPart!.Document!.Body!.GetFirstChild<SectionProperties>();
        Assert.That(sectPr, Is.Not.Null, "the body has no sectPr");
        return sectPr!;
    }

    /// <summary>
    /// Danish for the four keys the page header uses plus the five Oversigt keys
    /// (#1190) and the Detaljer keys (#1191) — the values in
    /// <c>Resources/localization.json</c>; every other key comes back as itself,
    /// like the shared key-returning double. Note that <c>Property</c> ("Ejendom")
    /// and <c>Company</c> ("Virksomhed") are BOTH here: the page header line reads
    /// the former, the Oversigt column header the latter, and the docx test pins
    /// that they land in those two places. Likewise <c>Tags</c> ("Etiketter") and
    /// <c>TagsPlain</c> ("Tags"), and <c>ComplianceDetails</c> ("Detaljer") and
    /// <c>ComplianceDetailsTitle</c> ("Compliance"): the Detaljer tests pin which
    /// of each pair reaches the file.
    /// </summary>
    private sealed class DanishShellLocalizer : IBackendConfigurationLocalizationService
    {
        private static readonly Dictionary<string, string> Danish = new()
        {
            ["Property"] = "Ejendom",
            ["CalendarBoard"] = "Kalender",
            ["Period"] = "Periode",
            ["All"] = "Alle",
            ["Company"] = "Virksomhed",
            ["Overdue"] = "Overskredet",
            ["CompliancePercentage"] = "Compliance %",
            ["ComplianceOverviewTitle"] = "Compliance oversigt",
            ["Total"] = "I alt",
            ["ComplianceDetails"] = "Detaljer",
            ["ComplianceDetailsTitle"] = "Compliance",
            ["Date"] = "Dato",
            ["StartTime"] = "Kl.",
            ["Task"] = "Opgave",
            ["Worker"] = "Medarbejder",
            ["Tags"] = "Etiketter",
            ["TagsPlain"] = "Tags",
            ["Status"] = "Status",
            ["Done"] = "Udført",
            ["NotDone"] = "Ikke udført",
            // Rapport (#1188 / #1192): the fixed columns, the formatted image
            // count, the appendix heading and the case label.
            ["ComplianceReport"] = "Rapport",
            ["SubReport"] = "Delrapport",
            ["CaseId"] = "ID",
            ["DoneBy"] = "Udført af",
            ["CompletedDate"] = "Udført dato",
            ["Area"] = "Område",
            ["Images"] = "Billeder",
            ["ImagesCount"] = "{0} billeder",
            ["ImagesCountOne"] = "1 billede",
            ["Appendix"] = "Bilag",
            ["Case"] = "Sag"
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

    /// <summary>
    /// The converter has a FINITE timeout. <c>ReportHelper.ConvertToPdf</c> — the
    /// SDK method this path deliberately does not call — blocks in a bare
    /// <c>WaitForExit()</c>, so a wedged <c>soffice</c> would hold the request
    /// thread forever. This pins that a bound exists and is not accidentally
    /// <c>Timeout.InfiniteTimeSpan</c>.
    /// </summary>
    [Test]
    public void Pdf_ConverterHasAFiniteTimeout()
    {
        Assert.That(ComplianceExportPdfConverter.ConversionTimeout,
            Is.GreaterThan(TimeSpan.Zero).And.LessThan(TimeSpan.FromHours(1)));
    }

    /// <summary>
    /// The per-invocation LibreOffice profile is handed to <c>soffice</c> as a
    /// <c>file://</c> URL, which is what <c>-env:UserInstallation</c> requires — a
    /// bare POSIX path is silently ignored and the DEFAULT profile is used instead,
    /// which is exactly the shared-profile collision the argument exists to avoid.
    /// </summary>
    [Test]
    public void Pdf_ProfileDirectoryIsFormattedAsAFileUrl()
    {
        var path = Path.Combine(Path.GetTempPath(), "results", "compliance-export-abc", "lo-profile");
        var url = ComplianceExportPdfConverter.ToFileUrl(path);

        Assert.That(url, Does.StartWith("file:///"));
        Assert.That(new Uri(url).LocalPath, Is.EqualTo(path));
        // A temp path containing a space still produces a legal URL.
        Assert.That(ComplianceExportPdfConverter.ToFileUrl("/tmp/a b/lo-profile"),
            Is.EqualTo("file:///tmp/a%20b/lo-profile"));
    }

    /// <summary>
    /// The "LibreOffice is not installed" path, end to end and WITHOUT shelling out
    /// to a conversion: <c>Process.Start</c> throws <c>Win32Exception</c>, the
    /// converter logs and returns <c>null</c> — a clean failure the service turns
    /// into a 400 — and the per-export temp directory is gone afterwards, because
    /// the cleanup is in a <c>finally</c>. Every existing generator in this plugin
    /// leaves its temp file behind forever; this one does not.
    ///
    /// <para>
    /// The CI image has no LibreOffice, which is what makes this deterministic
    /// there. On a developer machine that DOES have it the assertion would be about
    /// the environment rather than the code — and the test would run a real
    /// conversion — so it steps aside instead.
    /// </para>
    ///
    /// <para>
    /// <b>If you are the person adding LibreOffice to the CI image: this test then
    /// becomes an UNCONDITIONAL <c>Assert.Ignore</c>, and it is the only coverage
    /// of two things</b> — that a failed conversion returns <c>null</c> rather than
    /// throwing, and that the <c>finally</c> in
    /// <c>ComplianceExportPdfConverter.ConvertAsync</c> deletes the per-export temp
    /// directory. Deleting it would take both assertions with it silently. Re-shape
    /// it instead: point the converter at an executable name that cannot exist so
    /// the <c>Win32Exception</c> path is reached regardless of what is installed,
    /// or split the temp-directory assertion onto the successful conversion that
    /// will then be available.
    /// </para>
    /// </summary>
    [Test]
    public async Task Pdf_WithoutLibreOfficeReturnsNullAndLeavesNoTempDirectory()
    {
        if (SofficeIsOnPath())
        {
            Assert.Ignore("LibreOffice is installed here; this test asserts the missing-soffice path.");
        }

        await using var docx = await NewWordWriter().WriteAsync(SampleDocument(), null);

        var before = ExportTempDirectories();

        var pdf = await ComplianceExportPdfConverter.ConvertAsync(docx, NullLogger.Instance);

        Assert.That(pdf, Is.Null);
        Assert.That(ExportTempDirectories(), Is.EquivalentTo(before));
    }

    private static string[] ExportTempDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), "results");
        return Directory.Exists(root)
            ? Directory.GetDirectories(root, "compliance-export-*")
            : [];
    }

    private static bool SofficeIsOnPath() =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Any(directory =>
        {
            try
            {
                return File.Exists(Path.Combine(directory, "soffice"))
                       || File.Exists(Path.Combine(directory, "soffice.exe"));
            }
            catch (ArgumentException)
            {
                // A malformed PATH entry is not a reason to fail the test.
                return false;
            }
        });

    // ==================================================================
    // File naming
    // ==================================================================

    /// <summary>
    /// Non-ASCII is PRESERVED — a property named <c>Miljøtilsyn</c> keeps its
    /// <c>ø</c> — while the characters that break a file name are replaced. The
    /// period's separator is a hyphen, not the screen's en dash.
    /// </summary>
    [Test]
    public void FileName_PreservesNonAsciiAndUsesOneSchemeForEveryFormat()
    {
        var name = ComplianceExportFileNaming.BuildFileName(
            "Rapport", "Alle", "Miljøtilsyn",
            new DateTime(2026, 1, 1), new DateTime(2026, 9, 3), "pdf");

        Assert.That(name, Is.EqualTo("Rapport-Alle-Miljøtilsyn-01.01.2026-03.09.2026.pdf"));
    }

    /// <summary>
    /// The sanitiser: reserved characters become hyphens, whitespace runs collapse
    /// to one space, and trailing dots go (Windows drops them silently, which would
    /// change the extension).
    /// </summary>
    [Test]
    [TestCase("a/b\\c:d*e?f\"g<h>i|j", "a-b-c-d-e-f-g-h-i-j")]
    [TestCase("  spaced   out  ", "spaced out")]
    [TestCase("trailing dots...", "trailing dots")]
    [TestCase("Miljøtilsyn", "Miljøtilsyn")]
    [TestCase(null, "")]
    public void FileName_SanitiserRules(string? input, string expected)
    {
        Assert.That(ComplianceExportFileNaming.SanitiseFileNamePart(input!), Is.EqualTo(expected));
    }

    /// <summary>
    /// <c>Content-Disposition</c> carries BOTH an ASCII fallback and an RFC 5987
    /// <c>filename*</c>, and it is an <c>attachment</c> (a download), not
    /// <c>inline</c>. Without the <c>filename*</c> half the <c>ø</c> is lost in
    /// every browser.
    /// </summary>
    [Test]
    public void ContentDisposition_HasAttachmentAsciiFallbackAndUtf8FileName()
    {
        var header = ComplianceExportFileNaming.BuildContentDisposition(
            "Rapport-Alle-Miljøtilsyn-01.01.2026-03.09.2026.pdf");

        Assert.That(header, Does.StartWith("attachment; "));
        Assert.That(header, Does.Contain("filename=\"Rapport-Alle-Milj_tilsyn-01.01.2026-03.09.2026.pdf\""));
        Assert.That(header, Does.Contain("filename*=UTF-8''"));
        Assert.That(header, Does.Contain(Uri.EscapeDataString("Miljøtilsyn")));
    }

    private static byte[] ReadAll(Stream stream)
    {
        using var memory = new MemoryStream();
        stream.Position = 0;
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
