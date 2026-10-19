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

namespace BackendConfiguration.Pn.Infrastructure.Models.Chemicals;

using System.Collections.Generic;

public sealed record ChemicalExportFile(byte[] Content, string FileName, string ContentType);

/// <summary>W1 "Excel af aktuel filtrering": the ids the page shows; null = every open placement (Plan E decision 4).</summary>
public sealed record ChemicalInventoryExportBody(List<int> PlacementIds);

public sealed record ChemicalAuthorityListRow(
    string ProductName, string RegistrationNo, string Status, string SignalWord, string Pictograms, string HazardStatements,
    string Location, string Placement, string Deadlines, string Quantity, string SdsUrl)
{
    /// <summary>The printed text cells in Headers order (without the SDS column): the one column set the PDF renders.</summary>
    public IReadOnlyList<string> Cells { get; init; } = [];
}

public sealed record ChemicalAuthorityListDocument(
    string Title, string Subtitle, IReadOnlyList<string> Headers, IReadOnlyList<ChemicalAuthorityListRow> Rows,
    string SdsLinkText);
