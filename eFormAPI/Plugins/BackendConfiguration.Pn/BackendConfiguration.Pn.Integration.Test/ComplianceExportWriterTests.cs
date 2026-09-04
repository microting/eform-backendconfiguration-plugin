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

using System.Globalization;
using System.Text;
using BackendConfiguration.Pn.Infrastructure.Models.ComplianceReport;
using BackendConfiguration.Pn.Services.BackendConfigurationComplianceExportService;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Coverage for the three compliance-export renderers and for the download naming
/// (#1169 §4, §5, §7).
///
/// <para>
/// <b>No database, no container, and no <c>soffice</c>.</b> The PDF path is
/// <c>docx → soffice --headless --convert-to pdf</c>, and #1169 is explicit that a
/// test must not shell out for it: LibreOffice is not installed on the CI image, so
/// a test that ran the converter would be asserting the environment rather than the
/// code. What IS asserted here is everything up to that boundary — that the PDF
/// path renders a real docx (an OpenXml <c>WordprocessingDocument</c> with the
/// report's text in it), that the converter's timeout constant is finite, and that
/// on a machine with no LibreOffice the converter returns <c>null</c> and leaves no
/// temp directory behind.
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

    private static ComplianceExportDocument SampleDocument() => new()
    {
        Title = "Detaljer",
        Period = "01.01.2026 - 31.03.2026",
        Tables =
        [
            new ComplianceExportTable
            {
                Title = "Miljøtilsyn – Aflæsning vand",
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

    // ==================================================================
    // CSV
    // ==================================================================

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
    /// The empty-cell glyph reaches the file as the en dash, not as an empty field.
    /// </summary>
    [Test]
    public void Csv_EmptyCellIsTheEnDash()
    {
        using var stream = ComplianceExportCsvWriter.Write(SampleDocument());
        var text = Encoding.UTF8.GetString(ReadAll(stream));

        Assert.That(text, Does.Contain($"I alt;{Dash};4\r\n"));
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
    ///
    /// <para>
    /// The XLSX path needs no equivalent: its cells are written as
    /// <c>CellValues.String</c> and it never emits an <c>&lt;f&gt;</c> element.
    /// </para>
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
    /// Rapport is several tables and CSV is one stream. Tables after the FIRST are
    /// separated by a blank line and then their own title line; the first table has
    /// none, which is what keeps the header on line 1. A reader that ignores the
    /// titles entirely still has the section in the first column of every Rapport
    /// row, so the first table's identity is not lost with its title line.
    /// </summary>
    [Test]
    public void Csv_SeparatesLaterTablesWithABlankLineAndTheirTitle()
    {
        var document = SampleDocument();
        document.Tables.Add(new ComplianceExportTable
        {
            Title = "Drift – Aflæsning el",
            Columns = [new ComplianceExportColumn { Header = "Kolonne" }],
            Rows = [new ComplianceExportRow { Cells = [ComplianceExportCell.FromText("v")] }]
        });

        using var stream = ComplianceExportCsvWriter.Write(document);
        var bytes = ReadAll(stream);
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        // Table one: header first, no title line ahead of it.
        Assert.That(text, Does.StartWith("Dato;Ejendom;Overskredet\r\n"));
        Assert.That(text, Does.Not.Contain("Miljøtilsyn – Aflæsning vand"));

        // Table two: blank line, its title, its header, its rows.
        Assert.That(text, Does.Contain("\r\n\r\nDrift – Aflæsning el\r\nKolonne\r\nv\r\n"));
    }

    // ==================================================================
    // XLSX
    // ==================================================================

    /// <summary>
    /// A REAL OpenXml workbook — it re-opens through the OpenXml SDK, which the
    /// prototype's SpreadsheetML-2003-under-an-<c>.xls</c>-extension would not.
    /// One worksheet per table.
    /// </summary>
    [Test]
    public void Xlsx_IsARealOpenXmlWorkbookWithOneSheetPerTable()
    {
        var document = SampleDocument();
        document.Tables.Add(new ComplianceExportTable
        {
            Title = "Drift – Aflæsning el",
            Columns = [new ComplianceExportColumn { Header = "Kolonne" }]
        });

        using var stream = ComplianceExportExcelWriter.Write(document);
        using var spreadsheet = SpreadsheetDocument.Open(stream, false);

        var sheets = spreadsheet.WorkbookPart!.Workbook.Sheets!.Elements<Sheet>().ToList();
        Assert.That(sheets, Has.Count.EqualTo(2));
        Assert.That(sheets[0].Name!.Value, Is.EqualTo("Miljøtilsyn – Aflæsning vand"));
        Assert.That(sheets[1].Name!.Value, Is.EqualTo("Drift – Aflæsning el"));
    }

    /// <summary>
    /// The date column is a TYPED date cell — a numeric OADate under the shared
    /// stylesheet's date style (index 2, <c>NumberFormatId 14</c>) — so sorting and
    /// filtering on it work in Excel. A string date would sort lexically.
    /// </summary>
    [Test]
    public void Xlsx_DateColumnIsANumericOaDateUnderTheDateStyle()
    {
        using var stream = ComplianceExportExcelWriter.Write(SampleDocument());
        using var spreadsheet = SpreadsheetDocument.Open(stream, false);

        var worksheetPart = spreadsheet.WorkbookPart!.WorksheetParts.First();
        var rows = worksheetPart.Worksheet.Descendants<Row>().ToList();
        var dateCell = rows[1].Elements<Cell>().First();

        Assert.That(dateCell.DataType!.Value, Is.EqualTo(CellValues.Number));
        Assert.That(dateCell.StyleIndex!.Value, Is.EqualTo(2U));
        Assert.That(double.Parse(dateCell.CellValue!.Text, CultureInfo.InvariantCulture),
            Is.EqualTo(new DateTime(2026, 3, 9).ToOADate()));
    }

    /// <summary>
    /// The header row and the totals row are BOLD (style index 1), which is how a
    /// reader tells the appended "I alt" row from the data rows above it — the
    /// acceptance criterion the screen meets with an <c>is-total</c> class.
    /// </summary>
    [Test]
    public void Xlsx_HeaderAndTotalsRowsAreBold()
    {
        using var stream = ComplianceExportExcelWriter.Write(SampleDocument());
        using var spreadsheet = SpreadsheetDocument.Open(stream, false);

        var rows = spreadsheet.WorkbookPart!.WorksheetParts.First()
            .Worksheet.Descendants<Row>().ToList();

        Assert.That(rows[0].Elements<Cell>().All(c => c.StyleIndex?.Value == 1U), Is.True);

        var totalsRow = rows[2];
        Assert.That(totalsRow.Elements<Cell>().First().StyleIndex!.Value, Is.EqualTo(1U));
        Assert.That(totalsRow.Elements<Cell>().Last().StyleIndex!.Value, Is.EqualTo(1U));
    }

    /// <summary>
    /// Duplicate sheet names make a workbook unopenable, and Rapport can produce two
    /// sections whose names collide after Excel's 31-character truncation. The
    /// writer de-duplicates and truncates, so the file still opens.
    /// </summary>
    [Test]
    public void Xlsx_DeduplicatesAndTruncatesSheetNames()
    {
        var longTitle = new string('x', 40);
        var document = new ComplianceExportDocument
        {
            Title = "T",
            Tables =
            [
                new ComplianceExportTable { Title = longTitle },
                new ComplianceExportTable { Title = longTitle }
            ]
        };

        using var stream = ComplianceExportExcelWriter.Write(document);
        using var spreadsheet = SpreadsheetDocument.Open(stream, false);

        var names = spreadsheet.WorkbookPart!.Workbook.Sheets!.Elements<Sheet>()
            .Select(s => s.Name!.Value!).ToList();

        Assert.That(names, Has.Count.EqualTo(2));
        Assert.That(names.Distinct().Count(), Is.EqualTo(2));
        Assert.That(names.All(n => n.Length <= 31), Is.True);
    }

    /// <summary>
    /// Excel refuses to open a workbook containing a sheet whose name begins or
    /// ends with an apostrophe. A sheet name here is <c>{tag} – {template}</c>, so
    /// a tag named <c>'Miljø'</c> reaches it directly.
    /// </summary>
    [Test]
    public void Xlsx_SheetNameLeadingAndTrailingApostrophesAreStripped()
    {
        var document = new ComplianceExportDocument
        {
            Title = "T",
            Tables =
            [
                new ComplianceExportTable { Title = "'Miljø – Aflæsning'" },
                new ComplianceExportTable { Title = "'''" }
            ]
        };

        using var stream = ComplianceExportExcelWriter.Write(document);
        using var spreadsheet = SpreadsheetDocument.Open(stream, false);

        var names = spreadsheet.WorkbookPart!.Workbook.Sheets!.Elements<Sheet>()
            .Select(s => s.Name!.Value!).ToList();

        Assert.That(names, Has.Count.EqualTo(2));
        Assert.That(names.Any(n => n.StartsWith('\'') || n.EndsWith('\'')), Is.False);
        Assert.That(names[0], Is.EqualTo("Miljø – Aflæsning"));
        // A title that is nothing BUT apostrophes leaves an empty name, which falls
        // back to the positional sheet name rather than to an invalid one.
        Assert.That(names[1], Is.EqualTo("Sheet2"));
    }

    /// <summary>
    /// <c>History</c> is reserved by Excel and a workbook containing a sheet with
    /// that name will not open. A template legitimately named "History" must not be
    /// able to produce one.
    /// </summary>
    [Test]
    public void Xlsx_ReservedHistorySheetNameIsRenamed()
    {
        var document = new ComplianceExportDocument
        {
            Title = "T",
            Tables =
            [
                new ComplianceExportTable { Title = "History" },
                new ComplianceExportTable { Title = "history" }
            ]
        };

        using var stream = ComplianceExportExcelWriter.Write(document);
        using var spreadsheet = SpreadsheetDocument.Open(stream, false);

        var names = spreadsheet.WorkbookPart!.Workbook.Sheets!.Elements<Sheet>()
            .Select(s => s.Name!.Value!).ToList();

        Assert.That(names, Has.Count.EqualTo(2));
        Assert.That(
            names.Any(n => string.Equals(n, "History", StringComparison.OrdinalIgnoreCase)),
            Is.False);
        Assert.That(names[0], Is.EqualTo("History_"));
        // The lower-case spelling is reserved too, and de-duplicates as usual.
        Assert.That(names.Distinct(StringComparer.OrdinalIgnoreCase).Count(), Is.EqualTo(2));
    }

    /// <summary>
    /// An empty Rapport (no tag groups matched) still has to produce an openable
    /// workbook. A package with no sheet is invalid, so one empty sheet is written.
    /// </summary>
    [Test]
    public void Xlsx_EmptyDocumentStillOpens()
    {
        using var stream = ComplianceExportExcelWriter.Write(
            new ComplianceExportDocument { Title = "Rapport" });
        using var spreadsheet = SpreadsheetDocument.Open(stream, false);

        Assert.That(spreadsheet.WorkbookPart!.Workbook.Sheets!.Elements<Sheet>().Count(), Is.EqualTo(1));
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
        var writer = new ComplianceExportWordWriter(
            new BackendConfigurationLocalizationService(), NullLogger.Instance);

        await using var stream = await writer.WriteAsync(SampleDocument(), null);
        using var word = WordprocessingDocument.Open(stream, false);

        var text = word.MainDocumentPart!.Document.InnerText;
        Assert.That(text, Does.Contain("Detaljer"));
        Assert.That(text, Does.Contain("Miljøtilsyn – Aflæsning vand"));
        Assert.That(text, Does.Contain("Gården"));
        // Dates render dd.MM.yyyy in the document, not ISO.
        Assert.That(text, Does.Contain("09.03.2026"));
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

        var writer = new ComplianceExportWordWriter(
            new BackendConfigurationLocalizationService(), NullLogger.Instance);
        await using var docx = await writer.WriteAsync(SampleDocument(), null);

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
