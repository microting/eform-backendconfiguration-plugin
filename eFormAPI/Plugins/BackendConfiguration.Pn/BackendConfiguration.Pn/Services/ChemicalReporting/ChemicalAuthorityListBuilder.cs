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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ChemicalInventoryService;
using Infrastructure.Helpers;
using Infrastructure.Models.Chemicals;
using Microting.EformBackendConfigurationBase.Infrastructure.Helpers;
using K = Infrastructure.Models.Chemicals.ChemicalLocalizationKeys;

/// <summary>
/// "Kemikalieliste til Arbejdstilsynet": every OPEN placement of the property with
/// hazards, deadlines and a public SDS link (Plan E decision 7), in location order.
/// Pure: the service supplies the inventory, the clock and the texts.
/// </summary>
public static class ChemicalAuthorityListBuilder
{
    /// <summary>
    /// Text columns in print order with their relative widths; the SDS link column always follows. The widths let the
    /// longest Danish header word fit at the PDF's header size (no "Farepiktogramme|r", "Sikkerhedsdatabla|d") and give
    /// the hazard statements most of the room. The pictogram column has no text: the PDF draws Row.PictogramCodes.
    /// </summary>
    private static readonly (string HeaderKey, Func<ChemicalAuthorityListRow, string> Value, bool StockOnly, float Weight)[] Columns =
    [
        (K.ChemicalColumnProduct, r => r.ProductName, false, 2.4f),
        (K.ChemicalColumnRegistrationNo, r => r.RegistrationNo, false, 1.3f),
        (K.ChemicalColumnStatus, r => r.Status, false, 1.5f),
        (K.ChemicalColumnSignalWord, r => r.SignalWord, false, 1.3f),
        (K.ChemicalColumnPictograms, _ => string.Empty, false, 2.2f),
        (K.ChemicalColumnHazardStatements, r => r.HazardStatements, false, 5f),
        (K.ChemicalColumnLocation, r => r.Location, false, 1.6f),
        (K.ChemicalColumnPlacement, r => r.Placement, false, 1.6f),
        (K.ChemicalColumnDeadlines, r => r.Deadlines, false, 2.2f),
        (K.ChemicalColumnQuantity, r => r.Quantity, true, 1.1f),
    ];

    /// <summary>The SDS column prints only the short link text, never the address (a long URL wrapped over many lines).</summary>
    private const float SdsWeight = 2.4f;

    /// <summary>
    /// The label column exists only when a row has a label. Its width is taken from the hazard-statement column, so the
    /// table keeps its width and every header still fits (the SDS column must not shrink).
    /// </summary>
    private const float LabelWeight = 1.4f;

    public static ChemicalAuthorityListDocument Build(
        ChemicalInventoryModel model, int propertyId, DateTime generatedAtUtc, string sdsBaseUrl, Func<string, string> text,
        ChemicalAuthorityListOptions options = null)
    {
        var lookup = new ChemicalInventoryLookup(model);
        var showQuantity = model.Properties.FirstOrDefault(p => p.PropertyId == propertyId)?.StockEnabled ?? false;
        var columns = Columns.Where(c => showQuantity || !c.StockOnly).ToList();

        options ??= new ChemicalAuthorityListOptions();
        var rows = model.Placements
            .Where(p => p.RemovedAt == null && p.PropertyId == propertyId)
            .Select(p => (Placement: p, Location: lookup.Location(p.LocationId), Entry: lookup.Entry(p.ChemicalId)))
            .OrderBy(x => x.Location?.SortOrder ?? int.MaxValue)
            .ThenBy(x => x.Entry?.Name ?? string.Empty, TagNameComparer.Danish)
            .ThenBy(x => x.Placement.LocationId)
            .ThenBy(x => x.Placement.Id)
            .Select(x => Row(x.Placement, x.Location, x.Entry, lookup, showQuantity, sdsBaseUrl, text, options))
            .Select(row => row with { Cells = columns.Select(c => c.Value(row)).ToList() })
            .ToList();

        var hasLabels = rows.Any(r => r.LabelUrl.Length > 0);
        var headers = columns.Select(c => text(c.HeaderKey)).Append(text(K.ChemicalColumnSds)).ToList();
        var weights = columns.Select(c => c.Weight).Append(SdsWeight).ToList();
        if (hasLabels)
        {
            headers.Add(text(K.ChemicalColumnLabel));
            weights[columns.FindIndex(c => c.HeaderKey == K.ChemicalColumnHazardStatements)] -= LabelWeight;
            weights.Add(LabelWeight);
        }

        return new ChemicalAuthorityListDocument(
            text(K.ChemicalAuthorityListTitle),
            $"{text(K.ChemicalPropertyLabel)}: {lookup.PropertyName(propertyId)} · {text(K.ChemicalGeneratedLabel)}: "
            + ChemicalCopenhagen.ToCopenhagen(generatedAtUtc).ToString("dd-MM-yyyy HH:mm", CultureInfo.InvariantCulture),
            headers, rows, text(K.ChemicalSdsLink))
        {
            ColumnWeights = weights,
            PictogramColumn = columns.FindIndex(c => c.HeaderKey == K.ChemicalColumnPictograms),
            SdsNote = text(K.ChemicalSdsNote),
            LabelLinkText = hasLabels ? text(K.ChemicalLabelLink) : string.Empty,
        };
    }

    private static ChemicalAuthorityListRow Row(
        ChemicalPlacementModel placement, ChemicalLocationModel location, ChemicalRegisterEntryModel entry,
        ChemicalInventoryLookup lookup, bool showQuantity, string sdsBaseUrl, Func<string, string> text,
        ChemicalAuthorityListOptions options)
    {
        var deadlines = new List<string>();
        if (entry?.SalesDeadline is { } sales)
        {
            deadlines.Add($"{text(K.ChemicalDeadlineSales)}: {sales.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture)}");
        }

        if (entry?.UseAndPossessionDeadline is { } use)
        {
            deadlines.Add($"{text(K.ChemicalDeadlineUseAndPossession)}: {use.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture)}");
        }

        var baseUrl = sdsBaseUrl.TrimEnd('/');
        var sdsFileName = SdsFileName(entry, placement.ProductId, options.SdsLanguage);
        var label = entry?.Labels.FirstOrDefault(l => l.Kind == LabelKind && !string.IsNullOrEmpty(l.FileName));

        return new ChemicalAuthorityListRow(
            lookup.ProductName(placement.ChemicalId),
            entry?.RegistrationNo ?? string.Empty,
            entry?.StatusText ?? string.Empty,
            entry?.SignalWordText ?? string.Empty,
            string.Join("\n", (entry?.HazardStatements ?? []).Select(h => $"{h.Code} {h.Text}".Trim())),
            location?.Name ?? string.Empty,
            placement.PlacementNote ?? string.Empty,
            string.Join("\n", deadlines),
            showQuantity && placement.Unit is { } unit
                ? $"{ChemicalHistoryDetailsFormatter.Number(placement.Balance)} {ChemicalHistoryDetailsFormatter.Unit(unit)}"
                : string.Empty,
            sdsFileName is null
                ? string.Empty
                : $"{baseUrl}/{ChemicalBaseClient.SdsPath}?fileName={Uri.EscapeDataString(sdsFileName)}")
        {
            PictogramCodes = entry?.HazardPictograms ?? [],
            LabelUrl = label is null
                ? string.Empty
                : $"{baseUrl}/{ChemicalBaseClient.LabelPath}?fileName={Uri.EscapeDataString(label.FileName)}",
            OtherSdsLanguages = options.ListOtherLanguages ? OtherLanguages(entry, options.SdsLanguage, text) : string.Empty,
        };
    }

    /// <summary>Proto numbering of ChemicalLabelModel.Kind: 1 = Label (minor-use instructions are never linked).</summary>
    private const int LabelKind = 1;

    private const string Danish = "da";

    /// <summary>
    /// The Danish SDS is per product, so the placement's product file comes first and the chemical-level "da" document
    /// is only the fallback. Another language is taken from the chemical's documents, falling back to the Danish one.
    /// </summary>
    private static string SdsFileName(ChemicalRegisterEntryModel entry, int? productId, string language)
    {
        if (entry is null)
        {
            return null;
        }

        if (!IsLanguage(language, Danish))
        {
            var wanted = entry.SdsDocuments.FirstOrDefault(d => IsLanguage(d.Language, language) && !string.IsNullOrEmpty(d.FileName));
            if (wanted is not null)
            {
                return wanted.FileName;
            }
        }

        return (entry.Products.FirstOrDefault(p => p.ProductId == productId && !string.IsNullOrEmpty(p.SdsFileName))
                ?? entry.Products.FirstOrDefault(p => !string.IsNullOrEmpty(p.SdsFileName)))?.SdsFileName
               ?? entry.SdsDocuments.FirstOrDefault(d => IsLanguage(d.Language, Danish) && !string.IsNullOrEmpty(d.FileName))?.FileName;
    }

    /// <summary>"Også: DE, EN": the languages other than the linked one, upper case and sorted; empty when there are none.</summary>
    private static string OtherLanguages(ChemicalRegisterEntryModel entry, string language, Func<string, string> text)
    {
        if (entry is null)
        {
            return string.Empty;
        }

        var others = entry.SdsDocuments.Where(d => !string.IsNullOrEmpty(d.FileName)).Select(d => d.Language.Trim())
            .Concat(entry.Products.Any(p => !string.IsNullOrEmpty(p.SdsFileName)) ? [Danish] : [])
            .Where(l => l.Length > 0 && !IsLanguage(l, language))
            .Select(l => l.ToUpperInvariant())
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();
        return others.Count == 0 ? string.Empty : $"{text(K.ChemicalSdsAlso)}: {string.Join(", ", others)}";
    }

    private static bool IsLanguage(string actual, string wanted) =>
        string.Equals(actual?.Trim(), wanted?.Trim(), StringComparison.OrdinalIgnoreCase);
}
