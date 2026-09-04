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
/// display strings on the cells are used for everything else.
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
/// Rapport produces several tables and CSV is one flat stream, so tables after the
/// first are separated by a blank line followed by that table's title line. The
/// first table has no title line — that is what keeps the header on line 1 — and
/// nothing is lost by it, because every Rapport row carries its section in the
/// first <c>Delrapport</c> column. Oversigt and Detaljer are single-table and
/// carry no table title at all, so their files are simply header + rows.
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

        // No preamble: the header row is line 1. See the type comment.
        var isFirstTable = true;

        foreach (var table in document.Tables)
        {
            if (!isFirstTable)
            {
                sb.Append(LineEnding);
                if (!string.IsNullOrEmpty(table.Title))
                {
                    sb.Append(Escape(table.Title)).Append(LineEnding);
                }
            }

            isFirstTable = false;

            for (var i = 0; i < table.Columns.Count; i++)
            {
                if (i > 0) sb.Append(Separator);
                sb.Append(Escape(table.Columns[i].Header));
            }

            sb.Append(LineEnding);

            foreach (var row in table.Rows)
            {
                for (var i = 0; i < row.Cells.Count; i++)
                {
                    if (i > 0) sb.Append(Separator);
                    var type = i < table.Columns.Count
                        ? table.Columns[i].Type
                        : ComplianceExportCellType.Text;
                    sb.Append(Escape(Render(row.Cells[i], type)));
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
    /// ISO for dates, invariant decimal for numbers, the cell's display text
    /// otherwise (which is already the en dash when the value is absent).
    /// </summary>
    private static string Render(ComplianceExportCell cell, ComplianceExportCellType type)
    {
        if (cell == null) return ComplianceExportCell.EmptyGlyph;

        return type switch
        {
            ComplianceExportCellType.Date when cell.Date.HasValue =>
                cell.Date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ComplianceExportCellType.Number when cell.Number.HasValue =>
                cell.Number.Value.ToString(CultureInfo.InvariantCulture),
            _ => cell.Text ?? ComplianceExportCell.EmptyGlyph
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
    /// executes on open, arriving from the company's own compliance endpoint. The
    /// XLSX path is not affected — cells are written as <c>CellValues.String</c>,
    /// never as an <c>&lt;f&gt;</c> element — so the exposure is CSV-only, and this
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
    /// non-negative, and the absent-value glyph is the en dash U+2013 rather than
    /// the hyphen-minus. But those are the minority of guarded strings — most are
    /// Rapport answer cells, where a leading <c>-</c> or <c>+</c> is ordinary
    /// Danish free text (<c>-5 grader</c>, <c>+ tjek pumpe</c>). Such a value is
    /// guarded, and in LibreOffice Calc the apostrophe is then VISIBLE (see
    /// <see cref="FormulaGuard"/>). That artifact is accepted: a visible apostrophe
    /// on a minority of cells is a smaller harm than a compliance file from the
    /// company's own endpoint executing <c>=cmd|'/c calc'!A0</c> on open. The XLSX
    /// path needs no guard — cells are written as <c>CellValues.String</c>, never
    /// as an <c>&lt;f&gt;</c> element — so the exposure, and the artifact, are
    /// CSV-only.
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
