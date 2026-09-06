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
        Assert.That(text, Does.Contain("Miljøtilsyn – Aflæsning vand"));
        Assert.That(text, Does.Contain("Gården"));
        // Dates render dd.MM.yyyy in the document, not ISO.
        Assert.That(text, Does.Contain("09.03.2026"));
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

        var firstParagraph = word.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>()
            .First(p => !string.IsNullOrWhiteSpace(p.InnerText));
        Assert.That(firstParagraph.InnerText.Trim(), Is.EqualTo("Miljøtilsyn – Aflæsning vand"));
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
    /// Danish for the four keys the page header uses (the values in
    /// <c>Resources/localization.json</c>); every other key comes back as itself,
    /// like the shared key-returning double.
    /// </summary>
    private sealed class DanishShellLocalizer : IBackendConfigurationLocalizationService
    {
        private static readonly Dictionary<string, string> Danish = new()
        {
            ["Property"] = "Ejendom",
            ["CalendarBoard"] = "Kalender",
            ["Period"] = "Periode",
            ["All"] = "Alle"
        };

        public string GetString(string key) => Danish.TryGetValue(key, out var value) ? value : key;

        public string GetString(string format, params object[] args) => GetString(format);

        public string GetStringWithFormat(string format, params object[] args) => GetString(format);
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
