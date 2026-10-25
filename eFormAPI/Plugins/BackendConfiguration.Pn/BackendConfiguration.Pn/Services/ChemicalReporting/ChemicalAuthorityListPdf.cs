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

using System.Collections.Concurrent;
using System.Collections.Generic;
using Infrastructure.Models.Chemicals;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

/// <summary>Landscape A4 table rendering of the authority list (QuestPDF, Plan E decision 6).</summary>
public static class ChemicalAuthorityListPdf
{
    /// <summary>Page margin on every side, in points (1 cm).</summary>
    private const float MarginPoints = 28.35f;

    /// <summary>Width the table may use: landscape A4 minus both margins, in points.</summary>
    public static readonly float ContentWidthPoints = PageSizes.A4.Landscape().Width - 2 * MarginPoints;

    /// <summary>Column headers are a step smaller than the rows, so a long Danish header word fits its column.</summary>
    public const float HeaderFontSize = 7;

    /// <summary>Edge of a GHS diamond in the pictogram column, in points.</summary>
    private const float PictogramSize = 20;

    /// <summary>Each artwork parsed once (at most nine), then reused by every row and document.</summary>
    private static readonly ConcurrentDictionary<string, SvgImage> SvgImages = new();

    /// <summary>Padding on every side of a table cell, in points.</summary>
    public const float CellPadding = 3;

    public static byte[] Render(ChemicalAuthorityListDocument document) =>
        Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(MarginPoints);
            page.DefaultTextStyle(style => style.FontSize(8));

            page.Header().PaddingBottom(8).Column(column =>
            {
                column.Item().Text(document.Title).FontSize(14).Bold();
                column.Item().Text(document.Subtitle);
                // The SDS column is a link only; a printed copy still says where the sheets are.
                if (!string.IsNullOrEmpty(document.SdsNote))
                {
                    column.Item().Text(document.SdsNote).Italic();
                }
            });

            page.Content().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    foreach (var weight in document.ColumnWeights)
                    {
                        columns.RelativeColumn(weight);
                    }
                });

                table.Header(header =>
                {
                    foreach (var title in document.Headers)
                    {
                        header.Cell().Background(Colors.Grey.Lighten3).Padding(CellPadding).Text(title).FontSize(HeaderFontSize).Bold();
                    }
                });

                foreach (var row in document.Rows)
                {
                    for (var i = 0; i < row.Cells.Count; i++)
                    {
                        var cell = BodyCell(table);
                        if (i == document.PictogramColumn)
                        {
                            Pictograms(cell, row.PictogramCodes);
                        }
                        else
                        {
                            cell.Text(row.Cells[i]);
                        }
                    }

                    // Only the short link text: the address is in the link (printed, it wrapped over many lines).
                    var sds = BodyCell(table);
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

    private static IContainer BodyCell(TableDescriptor table) =>
        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(CellPadding);

    /// <summary>The official diamond of each known code; a code without artwork is printed as its code.</summary>
    private static void Pictograms(IContainer cell, IReadOnlyList<string> codes)
    {
        if (codes.Count == 0)
        {
            cell.Text(string.Empty);
            return;
        }

        cell.Inlined(inlined =>
        {
            inlined.Spacing(2);
            foreach (var code in codes)
            {
                var svg = ChemicalGhsPictograms.Svg(code);
                if (svg is null)
                {
                    inlined.Item().Text(code);
                }
                else
                {
                    inlined.Item().Width(PictogramSize).Height(PictogramSize).Svg(SvgImages.GetOrAdd(svg, SvgImage.FromText));
                }
            }
        });
    }
}
