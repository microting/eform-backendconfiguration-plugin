using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BackendConfiguration.Pn.Infrastructure.Helpers;
using BackendConfiguration.Pn.Infrastructure.Models.ComplianceReport;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace BackendConfiguration.Pn.Services.BackendConfigurationComplianceExportService;

/// <summary>
/// The <c>.xlsx</c> renderer — a REAL OpenXml workbook, not the prototype's
/// SpreadsheetML-2003-under-an-<c>.xls</c>-extension
/// (<c>compliance.js:653-679</c>). #1169 calls that an improvement rather than a
/// regression, and the acceptance criteria require it.
///
/// <para>
/// The workbook, stylesheet and theme parts come from the SHARED
/// <c>OpenXMLHelper</c> that <c>ExcelService</c> already uses, so the three cell
/// styles are the ones this plugin's spreadsheets have always had:
/// index 0 default, index 1 bold, index 2 the <c>NumberFormatId = 14</c> date
/// format (<c>OpenXMLHelper.cs:125-128</c>).
/// </para>
///
/// <para>
/// One worksheet per table. The date column is a REAL date cell (an OADate under
/// style 2), so sorting and filtering on it work in Excel — the acceptance
/// criterion the prototype's string dates could not meet.
/// </para>
///
/// <para>
/// Rendered to a <see cref="MemoryStream"/>, not to
/// <c>Path.GetTempPath()/results</c> as every existing generator does. #1169's
/// pitfall list notes nothing ever cleans that directory up; not writing to it is
/// the cheapest way not to add to the problem, and the 5000-row ceiling bounds the
/// memory.
/// </para>
/// </summary>
public static class ComplianceExportExcelWriter
{
    public static Stream Write(ComplianceExportDocument document)
    {
        // The package is written into `buffer`, and the RESULT is a fresh stream
        // over buffer.ToArray(). SpreadsheetDocument only flushes the part DOMs on
        // Dispose, and whether disposing it also disposes the stream it was created
        // over is an implementation detail of the OpenXml SDK — ToArray() is
        // documented to work on a disposed MemoryStream, so this cannot break on a
        // package upgrade.
        var buffer = new MemoryStream();

        using (var spreadsheet = SpreadsheetDocument.Create(buffer, SpreadsheetDocumentType.Workbook))
        {
            var tables = document.Tables.Count > 0
                ? document.Tables
                // A workbook with no sheet is not a valid package. An empty result
                // still has to open, so it gets one empty sheet rather than a
                // corrupt file or an error.
                : [new ComplianceExportTable { Title = document.Title }];

            var sheetNames = BuildUniqueSheetNames(document, tables);

            var workbookPart = spreadsheet.AddWorkbookPart();
            OpenXMLHelper.GenerateWorkbookPart1Content(workbookPart, sheetNames);

            var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>($"rId{sheetNames.Count + 2}");
            OpenXMLHelper.GenerateWorkbookStylesPart1Content(stylesPart);

            var themePart = workbookPart.AddNewPart<ThemePart>($"rId{sheetNames.Count + 1}");
            OpenXMLHelper.GenerateThemePart1Content(themePart);

            for (var t = 0; t < tables.Count; t++)
            {
                var table = tables[t];
                var worksheetPart = workbookPart.AddNewPart<WorksheetPart>($"rId{t + 1}");
                worksheetPart.Worksheet = BuildWorksheet(table);
            }
        }

        return new MemoryStream(buffer.ToArray());
    }

    private static Worksheet BuildWorksheet(ComplianceExportTable table)
    {
        var sheetData = new SheetData();

        var headerRow = new Row();
        foreach (var column in table.Columns)
        {
            headerRow.Append(new Cell
            {
                CellValue = new CellValue(column.Header ?? string.Empty),
                DataType = CellValues.String,
                StyleIndex = (UInt32Value)1U // bold
            });
        }

        sheetData.Append(headerRow);

        foreach (var row in table.Rows)
        {
            var dataRow = new Row();
            for (var i = 0; i < row.Cells.Count; i++)
            {
                var type = i < table.Columns.Count ? table.Columns[i].Type : ComplianceExportCellType.Text;
                dataRow.Append(BuildCell(row.Cells[i], type, row.IsTotal));
            }

            sheetData.Append(dataRow);
        }

        var worksheet = new Worksheet();
        worksheet.Append(new SheetFormatProperties { DefaultRowHeight = 15D });
        worksheet.Append(sheetData);

        if (table.Columns.Count > 0)
        {
            worksheet.Append(new AutoFilter
            {
                Reference = $"A1:{GetColumnLetter(table.Columns.Count)}{table.Rows.Count + 1}"
            });
        }

        worksheet.Append(new PageMargins
        {
            Left = 0.7D, Right = 0.7D, Top = 0.75D, Bottom = 0.75D, Header = 0.3D, Footer = 0.3D
        });

        return worksheet;
    }

    /// <summary>
    /// A typed cell. The totals row is written BOLD (style 1) so it is visibly
    /// distinguishable from the data rows above it, which is an explicit
    /// acceptance criterion — the prototype achieves the same on screen with an
    /// <c>is-total</c> row class.
    /// </summary>
    private static Cell BuildCell(ComplianceExportCell cell, ComplianceExportCellType type, bool isTotal)
    {
        if (cell == null)
        {
            return new Cell
            {
                CellValue = new CellValue(ComplianceExportCell.EmptyGlyph),
                DataType = CellValues.String,
                StyleIndex = isTotal ? 1U : 0U
            };
        }

        switch (type)
        {
            case ComplianceExportCellType.Date when cell.Date.HasValue:
                return new Cell
                {
                    CellValue = new CellValue(
                        cell.Date.Value.ToOADate().ToString(CultureInfo.InvariantCulture)),
                    DataType = CellValues.Number,
                    // Style 2 is the date number format; a bold date style does not
                    // exist in the shared stylesheet and a totals row never carries
                    // a date, so the date format wins here.
                    StyleIndex = 2U
                };
            case ComplianceExportCellType.Number when cell.Number.HasValue:
                return new Cell
                {
                    CellValue = new CellValue(cell.Number.Value.ToString(CultureInfo.InvariantCulture)),
                    DataType = CellValues.Number,
                    StyleIndex = isTotal ? 1U : 0U
                };
            default:
                return new Cell
                {
                    CellValue = new CellValue(cell.Text ?? ComplianceExportCell.EmptyGlyph),
                    DataType = CellValues.String,
                    StyleIndex = isTotal ? 1U : 0U
                };
        }
    }

    /// <summary>
    /// Sheet names: the table title, else the document title, sanitised to Excel's
    /// rules (see <see cref="CreateSafeSheetName"/>) and de-duplicated with a
    /// numeric suffix. Duplicate sheet names make the workbook unopenable, and
    /// Rapport can legitimately produce two sections whose names collide after the
    /// 31-character truncation.
    /// </summary>
    private static List<KeyValuePair<string, string>> BuildUniqueSheetNames(
        ComplianceExportDocument document, IReadOnlyList<ComplianceExportTable> tables)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new List<KeyValuePair<string, string>>(tables.Count);

        for (var i = 0; i < tables.Count; i++)
        {
            var raw = string.IsNullOrWhiteSpace(tables[i].Title) ? document.Title : tables[i].Title;
            var name = CreateSafeSheetName(raw);
            if (string.IsNullOrEmpty(name)) name = $"Sheet{i + 1}";

            if (used.Contains(name))
            {
                var suffix = 2;
                string candidate;
                do
                {
                    var tail = $" ({suffix})";
                    var head = name.Length + tail.Length > 31
                        ? name.Substring(0, 31 - tail.Length)
                        : name;
                    candidate = head + tail;
                    suffix++;
                } while (used.Contains(candidate));

                name = candidate;
            }

            used.Add(name);
            names.Add(new KeyValuePair<string, string>(name, $"rId{i + 1}"));
        }

        return names;
    }

    /// <summary>
    /// Excel's sheet-name rules, all four of them. Beyond the forbidden characters
    /// and the 31-character limit, a name may not BEGIN OR END with an apostrophe,
    /// and <c>History</c> is reserved. Excel refuses to open a workbook that breaks
    /// either, and both names are reachable from user input here — a sheet name is
    /// <c>{tag} – {template}</c>, and a tag named <c>'Miljø'</c> or a template
    /// named <c>History</c> is nothing unusual.
    /// </summary>
    private static string CreateSafeSheetName(string sheetName)
    {
        var cleaned = (sheetName ?? string.Empty)
            .Replace(":", "")
            .Replace("\\", "")
            .Replace("/", "")
            .Replace("?", "")
            .Replace("*", "")
            .Replace("[", "")
            .Replace("]", "")
            .Trim();

        if (cleaned.Length > 31) cleaned = cleaned.Substring(0, 31);

        // AFTER the truncation: cutting at 31 characters can itself expose a
        // trailing apostrophe that was interior a moment ago.
        cleaned = cleaned.Trim('\'').Trim();

        // "History" is reserved by Excel (it is the name the workbook uses for
        // change tracking). The suffix keeps the name recognisable, stays inside
        // the 31-character limit and cannot collide with the reserved name again;
        // BuildUniqueSheetNames de-duplicates it against the other sheets as usual.
        if (string.Equals(cleaned, "History", StringComparison.OrdinalIgnoreCase))
        {
            cleaned += "_";
        }

        return cleaned;
    }

    private static string GetColumnLetter(int columnIndex)
    {
        var columnLetter = string.Empty;
        while (columnIndex > 0)
        {
            var modulo = (columnIndex - 1) % 26;
            columnLetter = Convert.ToChar(65 + modulo) + columnLetter;
            columnIndex = (columnIndex - modulo) / 26;
        }

        return columnLetter;
    }
}
