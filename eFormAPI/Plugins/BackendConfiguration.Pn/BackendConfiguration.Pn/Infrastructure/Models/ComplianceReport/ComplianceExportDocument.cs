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
/// (Dato … Status), neither of which contains that preamble. The Excel one
/// additionally dereferences <c>MicrotingSdkCaseDoneAt!.Value</c> unconditionally
/// (<c>ExcelService.cs:417</c>), which NULL-refs on every not-yet-completed row —
/// and Detaljer is dominated by open tasks. (The compliance export's own Excel
/// renderer was removed by product request in #1189; the reasoning above still
/// explains why Word/PDF is not <c>GenerateWordDashboard</c>.)
/// </para>
///
/// <para>
/// Extending those two methods was rejected because both are live on
/// <c>GET report/reports/file</c> and #1169 forbids regressing them. So the
/// MECHANISMS are reused verbatim — <c>WordProcessor</c> over the same two
/// embedded resources (<c>Resources/Templates/WordExport/page.html</c> and
/// <c>file.docx</c>), the same <c>MagickImage</c> +
/// <c>Core.GetFileFromS3Storage</c>/local-disk image embedding, and the same
/// docx → <c>soffice</c> route to PDF — while the model they consume is replaced
/// by this one, which can express an arbitrary column set.
/// </para>
///
/// <para>
/// The GROUPING is <c>ReportEformModel</c>'s original one, restored by #1188:
/// Rapport produces one table per REPORT HEADLINE
/// (<c>Planning.ReportGroupPlanningTagId</c> there,
/// <c>AreaRulePlanning.ItemPlanningTagId</c> here), not the tag-per-template
/// split of #1160 decision 5 that sat in between. What is NOT carried forward is
/// positional cell addressing —
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
    /// "Period" line, <c>dd.MM.yyyy – dd.MM.yyyy</c> (en dash, the mock-ups'
    /// separator; the file NAME keeps hyphen-separated date parts). Rendered in the
    /// Word/PDF page header ONLY (#1189). The CSV writes no preamble at all — its
    /// first line is the header row, see <c>ComplianceExportCsvWriter</c> — and the
    /// file name already carries the period.
    /// </summary>
    public string Period { get; set; }

    /// <summary>
    /// Display label of the property the filter names, or the localised "All"
    /// when it names none (#1189). Resolved ONCE by the export service and reused
    /// for both the Word/PDF page header and the file name, so the two can never
    /// disagree. Ignored by CSV.
    /// </summary>
    public string PropertyLabel { get; set; }

    /// <summary>
    /// Display label of the single board the filter names, or the localised
    /// "All" for no board or a multi-board selection (#1189). Same
    /// resolve-once-reuse-twice rule as <see cref="PropertyLabel"/>. Ignored by
    /// CSV.
    /// </summary>
    public string BoardLabel { get; set; }

    /// <summary>
    /// One table per rendered section. Oversigt and Detaljer produce exactly one;
    /// Rapport produces one per report headline (#1188).
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
    /// idiom the per-case cap already uses on a block caption; CSV carries no
    /// appendix and ignores both counters.
    /// </para>
    /// </summary>
    public int AppendixImagesRequested { get; set; }
}

/// <summary>One titled table of typed cells.</summary>
public class ComplianceExportTable
{
    /// <summary>
    /// Small caption line rendered ABOVE <see cref="Title"/> when non-empty
    /// (#1188): for Rapport it is the group's tag names joined <c>" - "</c>
    /// (<c>ComplianceReportHeadlineGroupModel.TagsCaption</c>). Word/PDF print it
    /// as a small grey paragraph (9 pt, <c>#666666</c> — #1192's restyle of
    /// #1188's plain line); CSV ignores it. Empty for Oversigt and Detaljer.
    /// </summary>
    public string Caption { get; set; } = string.Empty;

    /// <summary>
    /// What the section's appendix page is headed with (#1192): Word/PDF print
    /// <c>Bilag – {AppendixLabel}</c> once, on a new page, ahead of the section's
    /// <see cref="ImageBlocks"/>. For Rapport it is <see cref="Caption"/>, falling
    /// back to the bare headline label when the group carries no tags (#1188's
    /// rule) — the HEADLINE label, not <see cref="Title"/>, so the
    /// "(Kolonner utilgængelige)" suffix never reaches the appendix. Empty on
    /// Oversigt and Detaljer, which have no appendix; a renderer falls back to
    /// <see cref="Caption"/>, then <see cref="Title"/>, should it be empty on a
    /// table that does carry blocks.
    /// </summary>
    public string AppendixLabel { get; set; } = string.Empty;

    /// <summary>
    /// Section heading. Empty for the single-table view modes (the document title
    /// already names them); for Rapport it is the REPORT HEADLINE's name (#1188)
    /// — or <c>#{id}</c> for a headline with no <c>PlanningTags</c> row, or the
    /// localised "Uden rapportoverskrift" for the fallback group — suffixed with
    /// "(Kolonner utilgængelige)" when a template's schema could not be derived.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    public List<ComplianceExportColumn> Columns { get; set; } = [];

    public List<ComplianceExportRow> Rows { get; set; } = [];

    /// <summary>
    /// Per-case image blocks, populated ONLY for Rapport when the request opted
    /// into the appendix. CSV ignores it; Word/PDF render it after ALL the tables,
    /// one page per section that has any (#1192), headed by
    /// <see cref="AppendixLabel"/>.
    /// </summary>
    public List<ComplianceExportImageBlock> ImageBlocks { get; set; } = [];
}

/// <summary>How a column's cells should be typed by a renderer that has types.</summary>
public enum ComplianceExportCellType
{
    Text = 0,

    /// <summary>
    /// CSV writes the invariant decimal form so the value stays machine-readable;
    /// Word/PDF write the cell's display text.
    /// </summary>
    Number = 1,

    /// <summary>
    /// CSV writes ISO <c>yyyy-MM-dd</c> (#1169 §2: the CSV date must be
    /// unambiguous), Word/PDF write <c>dd.MM.yyyy</c> — unless the cell carries a
    /// <see cref="ComplianceExportCell.DisplayText"/>, which Word/PDF then print
    /// instead (Detaljer's <c>Tirsdag 21. juli</c>, #1191). CSV never reads it.
    /// </summary>
    Date = 2
}

public class ComplianceExportColumn
{
    public string Header { get; set; }

    /// <summary>
    /// Stable identity of the column ACROSS tables, for the CSV writer's flat
    /// union (#1192): Rapport's fixed columns carry their localisation key
    /// (<c>CaseId</c>, <c>Images</c>, …) and each answer column carries the
    /// service's <c>f{fieldId}</c>, so two sections that answered the same field
    /// share one CSV column while two fields that merely share a LABEL stay two.
    /// Null falls back to <see cref="Header"/> — the single-table views never set
    /// it, and there is nothing for them to union with.
    /// </summary>
    public string Key { get; set; }

    /// <summary>
    /// True for a column the CSV carries but the Word/PDF table does not (#1192):
    /// Rapport's <c>Delrapport</c>, whose value is the section caption already
    /// printed above the table. The Word writer skips the column's header cell AND
    /// its cells; the CSV writer, which has no section captions, keeps it.
    /// </summary>
    public bool CsvOnly { get; set; }

    public ComplianceExportCellType Type { get; set; } = ComplianceExportCellType.Text;
}

public class ComplianceExportRow
{
    public List<ComplianceExportCell> Cells { get; set; } = [];

    /// <summary>
    /// True on the Oversigt "I alt" row. It is a DATA row, not a footer object —
    /// the prototype appends it to the rows (<c>compliance-overview.js:222-241</c>)
    /// — but renderers mark it so a reader can tell it apart: bold in Word/PDF,
    /// and prefixed in CSV by nothing at all (its first cell already reads
    /// "I alt").
    /// </summary>
    public bool IsTotal { get; set; }

    /// <summary>
    /// True on a Detaljer row whose occurrence is completed (#1191). Word/PDF tint
    /// the row pale green (<c>ComplianceExportWordWriter.DoneRowFill</c>) so a
    /// reader can scan for open work; CSV carries no styling and reads only the
    /// <c>Status</c> cell. Never set on Oversigt or Rapport rows.
    /// </summary>
    public bool IsDone { get; set; }
}

/// <summary>
/// One cell. At most one of <see cref="Number"/> / <see cref="Date"/> is set;
/// <see cref="Text"/> is ALWAYS set and is what a renderer without types writes.
///
/// <para>
/// <b>Emptiness is a FLAG, not a value of <see cref="Text"/>.</b> The factories
/// bake the en dash into <see cref="Text"/> for an absent value, so that Word/PDF
/// print it without a lookup — but a renderer cannot recover "absent" from the
/// glyph alone, because a text value that happens to BE an en dash is data.
/// <see cref="IsEmpty"/> is what the CSV writer reads to emit a blank field
/// (#1191, which owns that rule for all three views); Word/PDF ignore it and
/// print <see cref="Text"/>, keeping the glyph. Adding the flag rather than
/// making <see cref="Text"/> nullable keeps every existing <c>Text == "–"</c>
/// assertion and every Word/PDF rendering exactly as it was.
/// </para>
/// </summary>
public class ComplianceExportCell
{
    /// <summary>
    /// Display text. Never null — an absent value is the en dash
    /// <c>–</c> (U+2013), normalised across all three views by #1160's
    /// post-filing correction. In CSV an absent value is a blank field instead
    /// (#1191); see <see cref="IsEmpty"/>.
    /// </summary>
    public string Text { get; set; } = ComplianceExportCell.EmptyGlyph;

    /// <summary>
    /// Optional Word/PDF-only rendering that overrides the typed default — used
    /// for Detaljer's <c>Dato</c>, where the page shows <c>Tirsdag 21. juli</c>
    /// (#1191) while the CSV keeps the ISO date from <see cref="Date"/>. Null for
    /// every other cell; CSV never reads it.
    /// </summary>
    public string DisplayText { get; set; }

    public double? Number { get; set; }

    public DateTime? Date { get; set; }

    /// <summary>
    /// True when the cell carries NO value. The parameterless constructor is the
    /// empty cell (so <c>new ComplianceExportCell()</c> is empty by construction,
    /// which is how the builders spell an absent value), and every factory clears
    /// the flag when it is handed a real value. Word/PDF print the en dash for it;
    /// CSV prints a blank field (#1191).
    /// </summary>
    /// <remarks>
    /// Init-only: only the factories (<see cref="FromText"/> / <see cref="FromNumber"/>
    /// / <see cref="FromDate"/>) and the parameterless constructor may build cells, so
    /// a caller cannot later flip the flag and produce a cell that is blank in CSV
    /// but printed in Word.
    /// </remarks>
    public bool IsEmpty { get; init; } = true;

    /// <summary>The en dash U+2013 — the empty-cell glyph for Word/PDF in all three views.</summary>
    public const string EmptyGlyph = "–";

    public static ComplianceExportCell FromText(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? new ComplianceExportCell()
            : new ComplianceExportCell { Text = value, IsEmpty = false };

    public static ComplianceExportCell FromNumber(double? value) =>
        value.HasValue
            ? new ComplianceExportCell
            {
                Number = value,
                Text = value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                IsEmpty = false
            }
            : new ComplianceExportCell();

    /// <summary>
    /// A number cell whose Word/PDF text is <paramref name="displayText"/> rather
    /// than the bare figure — Rapport's <c>Billeder</c> (#1192): the PDF prints
    /// <c>3 billeder</c> while the CSV, which writes <see cref="Number"/> for a
    /// <see cref="ComplianceExportCellType.Number"/> column, keeps the
    /// machine-readable <c>3</c> the mock-up's CSV shows. That split is exactly
    /// the <c>Number</c> type's stated contract, so no new cell type is needed.
    /// A null value is the empty cell, whatever the text.
    /// </summary>
    public static ComplianceExportCell FromNumber(double? value, string displayText) =>
        value.HasValue
            ? new ComplianceExportCell
            {
                Number = value,
                Text = string.IsNullOrWhiteSpace(displayText)
                    ? value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : displayText,
                IsEmpty = false
            }
            : new ComplianceExportCell();

    public static ComplianceExportCell FromDate(DateTime? value) =>
        value.HasValue
            ? new ComplianceExportCell
            {
                Date = value,
                Text = value.Value.ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture),
                IsEmpty = false
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
