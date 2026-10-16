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
THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

namespace BackendConfiguration.Pn.Services.ChemicalReporting;

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

/// <summary>
/// Minimal one-sheet .xlsx for the Kemi exports. Text is written as inline
/// strings, never as formulas, so "=HYPERLINK(...)" in a note stays text. decimal
/// and int become number cells; dates arrive pre-formatted as text.
/// </summary>
public static class ChemicalXlsxWriter
{
    private const string ForbiddenSheetCharacters = "[]:*?/\\";

    public static byte[] Write(string sheetName, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object>> rows)
    {
        using var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            worksheetPart.Worksheet = new Worksheet(sheetData);
            sheetData.Append(ToRow(headers));
            foreach (var row in rows)
            {
                sheetData.Append(ToRow(row));
            }

            workbookPart.Workbook.AppendChild(new Sheets(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart), SheetId = 1, Name = SheetName(sheetName),
            }));
            workbookPart.Workbook.Save();
        }

        return stream.ToArray();
    }

    /// <summary>Excel sheet names: no []:*?/\, not empty, at most 31 characters.</summary>
    public static string SheetName(string name)
    {
        var clean = new string((name ?? string.Empty).Where(c => !ForbiddenSheetCharacters.Contains(c)).ToArray()).Trim();
        if (clean.Length == 0)
        {
            return "Sheet1";
        }

        return clean.Length > 31 ? clean[..31].TrimEnd() : clean;
    }

    private static Row ToRow(IEnumerable<object> values) => new(values.Select(ToCell));

    private static Cell ToCell(object value) => value switch
    {
        decimal number => new Cell { DataType = CellValues.Number, CellValue = new CellValue(number) },
        int number => new Cell { DataType = CellValues.Number, CellValue = new CellValue(number) },
        _ => new Cell
        {
            DataType = CellValues.InlineString,
            InlineString = new InlineString(new Text(Clean(value?.ToString())) { Space = SpaceProcessingModeValues.Preserve }),
        },
    };

    /// <summary>Drops what XML 1.0 cannot carry: control characters, unpaired surrogates, U+FFFE and U+FFFF.</summary>
    private static string Clean(string text)
    {
        var builder = new StringBuilder();
        text ??= string.Empty;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                builder.Append(c).Append(text[++i]);
            }
            else if (c is '\t' or '\n' or '\r' || (!char.IsControl(c) && !char.IsSurrogate(c) && c is not ('\uFFFE' or '\uFFFF')))
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
