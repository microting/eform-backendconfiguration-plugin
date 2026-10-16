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

using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>Reads back the single-sheet workbooks ChemicalXlsxWriter produces.</summary>
public static class XlsxTestReader
{
    public static (string SheetName, List<List<string>> Rows) Read(byte[] content)
    {
        using var stream = new MemoryStream(content);
        using var document = SpreadsheetDocument.Open(stream, false);
        var (sheet, data) = SheetOf(document);
        return (sheet.Name!.Value!, data.Elements<Row>().Select(r => r.Elements<Cell>().Select(Text).ToList()).ToList());
    }

    /// <summary>"n" for a number cell, "inlineStr" for text.</summary>
    public static string CellType(byte[] content, int row, int column)
    {
        using var stream = new MemoryStream(content);
        using var document = SpreadsheetDocument.Open(stream, false);
        var cell = SheetOf(document).Data.Elements<Row>().ElementAt(row).Elements<Cell>().ElementAt(column);
        return cell.DataType!.InnerText;
    }

    private static (Sheet Sheet, SheetData Data) SheetOf(SpreadsheetDocument document)
    {
        var workbook = document.WorkbookPart!;
        var sheet = workbook.Workbook.Sheets!.Elements<Sheet>().Single();
        var part = (WorksheetPart)workbook.GetPartById(sheet.Id!.Value!);
        return (sheet, part.Worksheet.GetFirstChild<SheetData>()!);
    }

    private static string Text(Cell cell) => cell.InlineString?.InnerText ?? cell.CellValue?.Text ?? string.Empty;
}
