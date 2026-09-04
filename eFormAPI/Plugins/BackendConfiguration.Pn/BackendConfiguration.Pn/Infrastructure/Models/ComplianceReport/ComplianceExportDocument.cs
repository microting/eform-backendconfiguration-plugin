using System;
using System.Collections.Generic;

namespace BackendConfiguration.Pn.Infrastructure.Models.ComplianceReport;

/// <summary>
/// The format-agnostic intermediate every compliance export renders from: a
/// document of one or more titled tables of TYPED cells, plus an optional image
/// appendix (#1169).
///
/// <para>
/// <b>Why this is not <c>Infrastructure/Models/Report/ReportEformModel.cs</c>.</b>
/// #1169 §1 points at that model and its two renderers, and they were read first.
/// They cannot serve two of the three view modes: both
/// <c>ExcelService.GenerateExcelDashboard</c> (<c>ExcelService.cs:334-341</c>) and
/// <c>WordService.GenerateWordDashboard</c> (<c>WordService.cs:849-855</c>) emit a
/// HARD-CODED six-column preamble — Id / Property / SubmittedDate / DoneBy /
/// EmployeeNo / ItemName — before the per-template answer headers, and
/// <c>ReportEformItemModel</c> has a fixed field for each of them.
/// #1169's acceptance criteria require Oversigt to be exactly three columns
/// (Virksomhed / Overskredet / Compliance %) and Detaljer exactly eight
/// (Dato … Status), neither of which contains that preamble. Excel additionally
/// dereferences <c>MicrotingSdkCaseDoneAt!.Value</c> unconditionally
/// (<c>ExcelService.cs:417</c>), which NULL-refs on every not-yet-completed row —
/// and Detaljer is dominated by open tasks.
/// </para>
///
/// <para>
/// Extending those two methods was rejected because both are live on
/// <c>GET report/reports/file</c> and #1169 forbids regressing them. So the
/// MECHANISMS are reused verbatim — <c>OpenXMLHelper</c> for the workbook, styles
/// and theme parts, <c>WordProcessor</c> over the same two embedded resources
/// (<c>Resources/Templates/WordExport/page.html</c> and <c>file.docx</c>), the
/// same <c>MagickImage</c> + <c>Core.GetFileFromS3Storage</c>/local-disk image
/// embedding, and the same docx → <c>soffice</c> route to PDF — while the model
/// they consume is replaced by this one, which can express an arbitrary column
/// set.
/// </para>
///
/// <para>
/// The one thing carried forward from <c>ReportEformModel</c> is its GROUPING:
/// Rapport still produces one table per tag group per template
/// (<c>GroupTagName</c> → <c>CheckListName</c>, #1160 decision 5). What is NOT
/// carried forward is positional cell addressing —
/// <c>ReportEformItemModel.CaseFields</c> is a
/// <c>List&lt;KeyValuePair&lt;string,string&gt;&gt;</c> keyed on the field TYPE
/// tag, which is the root of the column-desync of #1160 finding 3. Cells here are
/// built by walking the template's own ordered column schema and looking each
/// column's stable key up in <c>ComplianceReportCaseModel.Cells</c>, so a header
/// and its cell cannot drift apart.
/// </para>
/// </summary>
public class ComplianceExportDocument
{
    /// <summary>Localised document title, e.g. the Oversigt/Detaljer/Rapport label.</summary>
    public string Title { get; set; }

    /// <summary>
    /// Localised "period" line, <c>dd.MM.yyyy - dd.MM.yyyy</c>. Rendered on the
    /// Word/PDF title page ONLY. The CSV writes no preamble at all — its first line
    /// is the header row, see <c>ComplianceExportCsvWriter</c> — and Excel carries
    /// the period in no cell either; in both cases the file NAME already carries it.
    /// </summary>
    public string Period { get; set; }

    /// <summary>
    /// One table per rendered section. Oversigt and Detaljer produce exactly one;
    /// Rapport produces one per (tag group × template group).
    /// </summary>
    public List<ComplianceExportTable> Tables { get; set; } = [];

    /// <summary>
    /// How many appendix images the document-wide ceiling
    /// (<c>ComplianceExportDocumentBuilder.MaxAppendixImages</c>) actually let
    /// through, across every <see cref="ComplianceExportTable.ImageBlocks"/>.
    /// </summary>
    public int AppendixImagesEmbedded { get; set; }

    /// <summary>
    /// How many appendix images the PER-CASE cap selected before the document-wide
    /// ceiling was applied — including the cases that ended up with no block at all
    /// because the ceiling was already spent.
    ///
    /// <para>
    /// Greater than <see cref="AppendixImagesEmbedded"/> means the ceiling bit.
    /// Word/PDF then state it as an <c>(embedded/requested)</c> line, the same
    /// idiom the per-case cap already uses on a block caption; CSV and Excel carry
    /// no appendix and ignore both counters.
    /// </para>
    /// </summary>
    public int AppendixImagesRequested { get; set; }
}

/// <summary>One titled table of typed cells.</summary>
public class ComplianceExportTable
{
    /// <summary>
    /// Section heading. Empty for the single-table view modes (the document title
    /// already names them); for Rapport it is the composite
    /// <c>{tag} – {template}</c> label — see
    /// <c>ComplianceExportDocumentBuilder</c> for why that is one column and one
    /// title rather than two.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    public List<ComplianceExportColumn> Columns { get; set; } = [];

    public List<ComplianceExportRow> Rows { get; set; } = [];

    /// <summary>
    /// Per-case image blocks, populated ONLY for Rapport when the request opted
    /// into the appendix. CSV and Excel ignore it; Word/PDF render it after the
    /// table.
    /// </summary>
    public List<ComplianceExportImageBlock> ImageBlocks { get; set; } = [];
}

/// <summary>How a column's cells should be typed by a renderer that has types.</summary>
public enum ComplianceExportCellType
{
    Text = 0,

    /// <summary>
    /// Excel writes a numeric cell; CSV writes the invariant decimal form so the
    /// value stays machine-readable.
    /// </summary>
    Number = 1,

    /// <summary>
    /// Excel writes an OADate cell under the date style, CSV writes ISO
    /// <c>yyyy-MM-dd</c> (#1169 §2: the CSV date must be unambiguous), Word/PDF
    /// write <c>dd.MM.yyyy</c>.
    /// </summary>
    Date = 2
}

public class ComplianceExportColumn
{
    public string Header { get; set; }

    public ComplianceExportCellType Type { get; set; } = ComplianceExportCellType.Text;
}

public class ComplianceExportRow
{
    public List<ComplianceExportCell> Cells { get; set; } = [];

    /// <summary>
    /// True on the Oversigt "I alt" row. It is a DATA row, not a footer object —
    /// the prototype appends it to the rows (<c>compliance-overview.js:222-241</c>)
    /// — but renderers mark it so a reader can tell it apart: bold in Excel and in
    /// Word/PDF, and prefixed in CSV by nothing at all (its first cell already
    /// reads "I alt").
    /// </summary>
    public bool IsTotal { get; set; }
}

/// <summary>
/// One cell. At most one of <see cref="Number"/> / <see cref="Date"/> is set;
/// <see cref="Text"/> is ALWAYS set and is what a renderer without types writes.
/// </summary>
public class ComplianceExportCell
{
    /// <summary>
    /// Display text. Never null — an absent value is the en dash
    /// <c>–</c> (U+2013), normalised across all three views by #1160's
    /// post-filing correction.
    /// </summary>
    public string Text { get; set; } = ComplianceExportCell.EmptyGlyph;

    public double? Number { get; set; }

    public DateTime? Date { get; set; }

    /// <summary>The en dash U+2013 — the single empty-cell glyph for all three views.</summary>
    public const string EmptyGlyph = "–";

    public static ComplianceExportCell FromText(string value) =>
        new() { Text = string.IsNullOrWhiteSpace(value) ? EmptyGlyph : value };

    public static ComplianceExportCell FromNumber(double? value) =>
        value.HasValue
            ? new ComplianceExportCell
            {
                Number = value,
                Text = value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }
            : new ComplianceExportCell();

    public static ComplianceExportCell FromDate(DateTime? value) =>
        value.HasValue
            ? new ComplianceExportCell
            {
                Date = value,
                Text = value.Value.ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture)
            }
            : new ComplianceExportCell();
}

/// <summary>
/// One case's images for the Word/PDF appendix. Carries FILE NAMES only — the
/// renderer resolves the bytes from S3 or the local picture directory through the
/// SDK <c>Core</c>, never over HTTP.
/// </summary>
public class ComplianceExportImageBlock
{
    /// <summary>Heading line, e.g. <c>Sag 1234 · Område · 01.02.2026</c>.</summary>
    public string Caption { get; set; }

    /// <summary>
    /// Derived display names (<c>{UploadedDataId}_700_{Checksum}{Extension}</c>),
    /// already capped by the builder. Entries the projector could not derive are
    /// dropped by the builder rather than passed through as null.
    /// </summary>
    public List<string> ImageNames { get; set; } = [];

    /// <summary>Parallel to <see cref="ImageNames"/>; an entry may be null.</summary>
    public List<string> GeoLinks { get; set; } = [];

    /// <summary>
    /// How many images this case actually has — the case's own
    /// <c>ImagesCount</c>, which is also what the <c>Billeder</c> column prints —
    /// before the per-case cap AND before the builder drops images whose display
    /// name could not be derived. Rendered alongside the caption when it exceeds
    /// what was embedded, so the document states its own truncation whichever of
    /// the two caused it.
    /// </summary>
    public int TotalImages { get; set; }
}
