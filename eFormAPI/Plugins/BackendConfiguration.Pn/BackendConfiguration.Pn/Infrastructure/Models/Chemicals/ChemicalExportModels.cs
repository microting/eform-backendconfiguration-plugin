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
    string ProductName, string RegistrationNo, string Status, string SignalWord, string HazardStatements,
    string Location, string Placement, string Deadlines, string Quantity, string SdsUrl)
{
    /// <summary>The printed text cells in Headers order (without the SDS column): the one column set the PDF renders.</summary>
    public IReadOnlyList<string> Cells { get; init; } = [];

    /// <summary>The GHS codes of the pictogram column; the PDF draws each known one as its diamond (its Cells entry is empty).</summary>
    public IReadOnlyList<string> PictogramCodes { get; init; } = [];

    /// <summary>Link to the first BMD label; empty when the chemical has none.</summary>
    public string LabelUrl { get; init; } = string.Empty;

    /// <summary>"Også: EN, DE": the other SDS languages, no links; empty when there are none.</summary>
    public string OtherSdsLanguages { get; init; } = string.Empty;
}

/// <summary>What the Arbejdstilsynet list prints about SDS languages (a default the owner can change).</summary>
public sealed record ChemicalAuthorityListOptions
{
    /// <summary>The language of the SDS link; the list is a Danish legal document.</summary>
    public string SdsLanguage { get; init; } = DefaultSdsLanguage;

    public const string DefaultSdsLanguage = "da";

    /// <summary>Print a line with the other languages available under the SDS link.</summary>
    public bool ListOtherLanguages { get; init; } = true;
}

public sealed record ChemicalAuthorityListDocument(
    string Title, string Subtitle, IReadOnlyList<string> Headers, IReadOnlyList<ChemicalAuthorityListRow> Rows,
    string SdsLinkText)
{
    /// <summary>Relative width of every column in Headers order, the SDS column included.</summary>
    public IReadOnlyList<float> ColumnWeights { get; init; } = [];

    /// <summary>Index of the pictogram column in Headers (and Cells).</summary>
    public int PictogramColumn { get; init; } = -1;

    /// <summary>One line under the subtitle: where the safety data sheets are, for a printed copy.</summary>
    public string SdsNote { get; init; } = string.Empty;

    /// <summary>Text of the label link; the label column exists only when a row has a label.</summary>
    public string LabelLinkText { get; init; } = string.Empty;
}
