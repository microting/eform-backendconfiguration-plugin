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
using Infrastructure.Models.Chemicals;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

/// <summary>Landscape A4 table rendering of the authority list (QuestPDF, Plan E decision 6).</summary>
public static class ChemicalAuthorityListPdf
{
    public static byte[] Render(ChemicalAuthorityListDocument document) =>
        Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(1, Unit.Centimetre);
            page.DefaultTextStyle(style => style.FontSize(8));

            page.Header().PaddingBottom(8).Column(column =>
            {
                column.Item().Text(document.Title).FontSize(14).Bold();
                column.Item().Text(document.Subtitle);
            });

            page.Content().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    foreach (var _ in document.Headers)
                    {
                        columns.RelativeColumn();
                    }
                });

                table.Header(header =>
                {
                    foreach (var title in document.Headers)
                    {
                        header.Cell().Background(Colors.Grey.Lighten3).Padding(3).Text(title).Bold();
                    }
                });

                foreach (var row in document.Rows)
                {
                    foreach (var value in TextCells(row, document.ShowQuantity))
                    {
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3).Text(value);
                    }

                    var sds = table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3);
                    if (string.IsNullOrEmpty(row.SdsUrl))
                    {
                        sds.Text(string.Empty);
                    }
                    else
                    {
                        sds.Hyperlink(row.SdsUrl).Text(document.SdsLinkText).FontColor(Colors.Blue.Darken2).Underline();
                    }
                }
            });

            page.Footer().AlignRight().Text(text =>
            {
                text.CurrentPageNumber();
                text.Span(" / ");
                text.TotalPages();
            });
        })).GeneratePdf();

    private static IEnumerable<string> TextCells(ChemicalAuthorityListRow row, bool showQuantity)
    {
        yield return row.ProductName;
        yield return row.RegistrationNo;
        yield return row.Status;
        yield return row.SignalWord;
        yield return row.Pictograms;
        yield return row.HazardStatements;
        yield return row.Location;
        yield return row.Placement;
        yield return row.Deadlines;
        if (showQuantity)
        {
            yield return row.Quantity;
        }
    }
}
