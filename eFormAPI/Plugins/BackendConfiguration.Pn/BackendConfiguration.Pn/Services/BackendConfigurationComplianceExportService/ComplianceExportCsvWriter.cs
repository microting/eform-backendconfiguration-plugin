using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BackendConfiguration.Pn.Infrastructure.Models.ComplianceReport;

namespace BackendConfiguration.Pn.Services.BackendConfigurationComplianceExportService;

/// <summary>
/// The CSV renderer (#1169 §5). Written from scratch because there is nothing to
/// reuse: zero CSV writers exist in the BC plugin, in <c>eFormApi.BasePn</c> or as
/// a package in any of the three repos. The only precedent is the legacy inline
/// writer buried in <c>eform-sdk Core.cs:2100-2176</c>, which writes to a FILE
/// PATH and returns the path rather than a stream, and it establishes exactly one
/// thing that is carried forward: the <c>;</c> separator.
///
/// <para>
/// Format, matching the prototype's <c>buildCsvExport</c>
/// (<c>compliance.js:637-643</c>) and <c>escapeCsvCell</c> (<c>:617-623</c>):
/// <list type="bullet">
///   <item><b><c>;</c> separated</b> — a Danish Excel splits on the list separator, not the comma.</item>
///   <item><b>UTF-8 WITH a BOM</b> — without it Excel reads the file as the ANSI code page and mangles æøå.</item>
///   <item><b>CRLF line endings.</b></item>
///   <item><b>Quote on demand:</b> a cell is quoted only when it contains <c>;</c>, <c>"</c>, CR or LF, and embedded quotes are doubled.</item>
///   <item><b>Formula guard</b> (NOT the prototype's, deliberately added): a cell whose FIRST character is <c>=</c>, <c>+</c>, <c>-</c>, <c>@</c>, TAB or CR is prefixed with an apostrophe, so a worker-typed answer cannot become a formula Excel executes on open. See <see cref="Escape"/>.</item>
/// </list>
/// </para>
///
/// <para>
/// Dates are written ISO <c>yyyy-MM-dd</c> and numbers with the invariant decimal
/// point, so the file stays machine-readable regardless of who opens it. The
/// display strings on the cells are used for everything else — except an EMPTY
/// cell, which is a blank field (<c>;;</c>), not the en dash Word/PDF print
/// (#1191, the mock-ups' CSVs; the rule holds for all three views). A cell's
/// Word/PDF-only <c>DisplayText</c> is never read here: the CSV date stays ISO
/// while the page shows <c>Tirsdag 21. juli</c>.
/// </para>
///
/// <para>
/// <b>The FIRST line of the file is a header row.</b> No document title and no
/// period line are written — a CSV is consumed by "use first row as header",
/// <c>pandas.read_csv</c> and Power Query, all of which would need a manual
/// three-row skip for a preamble, and the file NAME already carries the view, the
/// period, the property and the board. The title page belongs to Word/PDF, which
/// keep theirs.
/// </para>
///
/// <para>
/// <b>ONE FLAT TABLE, whatever the document's shape (#1192, Rapport CSV mock-up
/// p10).</b> Rapport produces several tables and CSV is one flat stream, so the
/// file is the UNION of every table's columns — in first-seen order, keyed on
/// <see cref="ComplianceExportColumn.Key"/> (falling back to the header) — as the
/// single header on line 1, then every table's rows in document order, each row
/// blank under the columns its own table lacks. No blank separator lines, no
/// per-table title lines, no repeated headers: a section is identified by its
/// <c>Delrapport</c> cell alone. The flattening is unconditional rather than
/// keyed on the view or on the table count because for a single-table document
/// (Oversigt, Detaljer, a one-headline Rapport) the union IS that table's column
/// list and the output is byte-identical to the per-table shape it replaces —
/// one rule, no branch. The union assumes a Key is unique WITHIN a table as
/// well as being the identity ACROSS tables: two columns of one table sharing a
/// Key would map to the same union position and collapse into one field, the
/// later cell overwriting the earlier — unreachable today, since the fixed keys
/// are distinct localisation keys and the answer keys are the unique
/// <c>f{fieldId}</c> per group, but a new column must keep it that way.
/// </para>
///
/// <para>
/// <b>What the flat Rapport CSV does NOT carry.</b> The section captions and
/// headline titles (Word/PDF's two-line headings) are not in the file at all:
/// #1188 decision 4 follows the mock-up, which has no <c>Rapportoverskrift</c>
/// column, and #1192 keeps that. Columns marked
/// <see cref="ComplianceExportColumn.CsvOnly"/> ARE here — that flag is the Word
/// writer's to honour. Same-KEYED answer columns merge across sections (the same
/// eForm field answered under two headlines is one spreadsheet column); two
/// fields from different templates that merely share a label stay two columns,
/// because the key is <c>f{fieldId}</c>, not the label. A cell beyond its own
/// table's column count has no column to land in and is dropped.
/// </para>
///
/// <para>
/// <c>Udført dato</c> stays ISO like every other date here — the Rapport CSV
/// mock-up's <c>13.05.2026</c> is read as a mock-up slip, inconsistent with the
/// Detaljer CSV on the page before it (p8) and with #1169 §2's "the CSV date
/// must be unambiguous".
/// </para>
/// </summary>
public static class ComplianceExportCsvWriter
{
    public const string Separator = ";";
    public const string LineEnding = "\r\n";

    /// <summary>
    /// Renders to a rewound <see cref="MemoryStream"/>. In memory rather than a
    /// temp file deliberately — CSV is the one format with no external toolchain,
    /// so there is nothing to clean up afterwards, and the 5000-row ceiling bounds
    /// the size.
    /// </summary>
    public static Stream Write(ComplianceExportDocument document)
    {
        var stream = new MemoryStream();

        // UTF-8 BOM, written explicitly rather than via StreamWriter's
        // encoderShouldEmitUTF8Identifier, so it is visible in the code that it is
        // a deliberate part of the format and not a framework default.
        var bom = Encoding.UTF8.GetPreamble();
        stream.Write(bom, 0, bom.Length);

        var sb = new StringBuilder();

        // The union of every table's columns, first-seen order, keyed on Key
        // (or Header). The first table to introduce a key supplies its header
        // text; a later table's header for the same key is by construction the
        // same localised string.
        var columns = new List<ComplianceExportColumn>();
        var positionByKey = new Dictionary<string, int>();
        foreach (var table in document.Tables)
        {
            foreach (var column in table.Columns)
            {
                var key = ColumnKey(column);
                if (positionByKey.ContainsKey(key)) continue;
                positionByKey[key] = columns.Count;
                columns.Add(column);
            }
        }

        // No preamble: the header row is line 1, and the only header. See the
        // type comment. A document with no table at all writes nothing but the
        // BOM, as before.
        if (document.Tables.Count > 0)
        {
            for (var i = 0; i < columns.Count; i++)
            {
                if (i > 0) sb.Append(Separator);
                sb.Append(Escape(columns[i].Header));
            }

            sb.Append(LineEnding);
        }

        foreach (var table in document.Tables)
        {
            // Where each of this table's columns lands in the union.
            var positions = new int[table.Columns.Count];
            for (var i = 0; i < table.Columns.Count; i++)
            {
                positions[i] = positionByKey[ColumnKey(table.Columns[i])];
            }

            foreach (var row in table.Rows)
            {
                var fields = new string[columns.Count];
                for (var i = 0; i < row.Cells.Count && i < positions.Length; i++)
                {
                    fields[positions[i]] = Render(row.Cells[i], table.Columns[i].Type);
                }

                for (var i = 0; i < fields.Length; i++)
                {
                    if (i > 0) sb.Append(Separator);
                    sb.Append(Escape(fields[i] ?? string.Empty));
                }

                sb.Append(LineEnding);
            }
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        stream.Write(bytes, 0, bytes.Length);
        stream.Position = 0;
        return stream;
    }

    /// <summary>
    /// The identity a column is unioned on: its <see cref="ComplianceExportColumn.Key"/>,
    /// or its header when the builder set none (the single-table views).
    /// </summary>
    private static string ColumnKey(ComplianceExportColumn column) =>
        !string.IsNullOrEmpty(column.Key) ? column.Key : column.Header ?? string.Empty;

    /// <summary>
    /// A blank field for an empty cell (#1191), ISO for dates, invariant decimal
    /// for numbers, the cell's display text otherwise.
    ///
    /// <para>
    /// Emptiness is read from <see cref="ComplianceExportCell.IsEmpty"/>, never
    /// inferred from the text: the factories bake the en dash into <c>Text</c> for
    /// Word/PDF, so a <c>null</c> check would still write the glyph for every
    /// empty text cell, and a glyph check would blank a genuine <c>–</c> answer.
    /// </para>
    /// </summary>
    private static string Render(ComplianceExportCell cell, ComplianceExportCellType type)
    {
        if (cell == null || cell.IsEmpty) return string.Empty;

        return type switch
        {
            ComplianceExportCellType.Date when cell.Date.HasValue =>
                cell.Date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ComplianceExportCellType.Number when cell.Number.HasValue =>
                cell.Number.Value.ToString(CultureInfo.InvariantCulture),
            _ => cell.Text ?? string.Empty
        };
    }

    /// <summary>
    /// The character prepended to a cell whose first character would otherwise make
    /// Excel, LibreOffice or Google Sheets treat the cell as a FORMULA. A leading
    /// apostrophe is the spreadsheet convention for "this is literal text".
    ///
    /// <para>
    /// <b>The trade, stated honestly.</b> Excel consumes the apostrophe on import,
    /// so there it is invisible. LibreOffice Calc does NOT: in an unquoted field it
    /// keeps the character, so a legitimate Danish answer of <c>-5 grader</c>
    /// displays as <c>'-5 grader</c>. Both applications are named in the acceptance
    /// criteria, so this is a real artifact and not a theoretical one. It is
    /// accepted anyway, because the alternative is a file of worker-typed text that
    /// a spreadsheet may execute as formulas — see <see cref="Escape"/>.
    /// </para>
    /// </summary>
    public const string FormulaGuard = "'";

    /// <summary>
    /// Quote only when needed — the prototype's rule, kept verbatim so the two
    /// implementations produce byte-identical cells for ordinary data — plus one
    /// thing the prototype does not do: neutralising a leading formula character.
    ///
    /// <para>
    /// <b>Why the guard is here and not only in the prototype's rule.</b> Every
    /// Rapport answer cell is worker-typed free text, and property, task and tag
    /// names are equally user-supplied. A worker who types
    /// <c>=cmd|'/c calc'!A0</c> would otherwise produce a file that a Danish Excel
    /// executes on open, arriving from the company's own compliance endpoint. This
    /// is the plugin's first user-facing CSV download.
    /// </para>
    ///
    /// <para>
    /// <b>Guarded characters:</b> <c>=</c>, <c>+</c>, <c>-</c>, <c>@</c>, TAB and
    /// CR, and only in FIRST position — a value that merely CONTAINS one (a
    /// template answer of <c>2+2=4</c>, a task named <c>A-B</c>) is left exactly as
    /// it was.
    /// </para>
    ///
    /// <para>
    /// <b>What the guard costs, precisely.</b> The NUMBER-typed cells cannot be
    /// affected: overdue counts, compliance percentages and image counts are all
    /// non-negative, an absent value is a blank field, and Detaljer's <c>Kl.</c>
    /// range (<c>13:00 - 14:00</c>) starts with a digit, never with its
    /// hyphen-minus. But those are the minority of guarded strings — most are
    /// Rapport answer cells, where a leading <c>-</c> or <c>+</c> is ordinary
    /// Danish free text (<c>-5 grader</c>, <c>+ tjek pumpe</c>). Such a value is
    /// guarded, and in LibreOffice Calc the apostrophe is then VISIBLE (see
    /// <see cref="FormulaGuard"/>). That artifact is accepted: a visible apostrophe
    /// on a minority of cells is a smaller harm than a compliance file from the
    /// company's own endpoint executing <c>=cmd|'/c calc'!A0</c> on open.
    /// </para>
    /// </summary>
    public static string Escape(string value)
    {
        var text = value ?? string.Empty;

        if (text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            text = FormulaGuard + text;
        }

        var needsQuoting = text.Contains(';') || text.Contains('"')
                                             || text.Contains('\n') || text.Contains('\r');
        return needsQuoting ? $"\"{text.Replace("\"", "\"\"")}\"" : text;
    }
}
