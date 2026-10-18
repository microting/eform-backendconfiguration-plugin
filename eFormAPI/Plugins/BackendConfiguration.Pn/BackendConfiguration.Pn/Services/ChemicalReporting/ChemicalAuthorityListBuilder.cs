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
    /// <summary>Text columns in print order; the SDS link column always follows.</summary>
    private static readonly (string HeaderKey, Func<ChemicalAuthorityListRow, string> Value, bool StockOnly)[] Columns =
    [
        (K.ChemicalColumnProduct, r => r.ProductName, false),
        (K.ChemicalColumnRegistrationNo, r => r.RegistrationNo, false),
        (K.ChemicalColumnStatus, r => r.Status, false),
        (K.ChemicalColumnSignalWord, r => r.SignalWord, false),
        (K.ChemicalColumnPictograms, r => r.Pictograms, false),
        (K.ChemicalColumnHazardStatements, r => r.HazardStatements, false),
        (K.ChemicalColumnLocation, r => r.Location, false),
        (K.ChemicalColumnPlacement, r => r.Placement, false),
        (K.ChemicalColumnDeadlines, r => r.Deadlines, false),
        (K.ChemicalColumnQuantity, r => r.Quantity, true),
    ];

    public static ChemicalAuthorityListDocument Build(
        ChemicalInventoryModel model, int propertyId, DateTime generatedAtUtc, string sdsBaseUrl, Func<string, string> text)
    {
        var lookup = new ChemicalInventoryLookup(model);
        var showQuantity = model.Properties.FirstOrDefault(p => p.PropertyId == propertyId)?.StockEnabled ?? false;
        var columns = Columns.Where(c => showQuantity || !c.StockOnly).ToList();

        var rows = model.Placements
            .Where(p => p.RemovedAt == null && p.PropertyId == propertyId)
            .Select(p => (Placement: p, Location: lookup.Location(p.LocationId), Entry: lookup.Entry(p.ChemicalId)))
            .OrderBy(x => x.Location?.SortOrder ?? int.MaxValue)
            .ThenBy(x => x.Entry?.Name ?? string.Empty, TagNameComparer.Danish)
            .ThenBy(x => x.Placement.LocationId)
            .ThenBy(x => x.Placement.Id)
            .Select(x => Row(x.Placement, x.Location, x.Entry, lookup, showQuantity, sdsBaseUrl, text))
            .Select(row => row with { Cells = columns.Select(c => c.Value(row)).ToList() })
            .ToList();

        var headers = columns.Select(c => text(c.HeaderKey)).Append(text(K.ChemicalColumnSds)).ToList();

        return new ChemicalAuthorityListDocument(
            text(K.ChemicalAuthorityListTitle),
            $"{text(K.ChemicalPropertyLabel)}: {lookup.PropertyName(propertyId)} · {text(K.ChemicalGeneratedLabel)}: "
            + ChemicalCopenhagen.ToCopenhagen(generatedAtUtc).ToString("dd-MM-yyyy HH:mm", CultureInfo.InvariantCulture),
            headers, rows, text(K.ChemicalSdsLink));
    }

    private static ChemicalAuthorityListRow Row(
        ChemicalPlacementModel placement, ChemicalLocationModel location, ChemicalRegisterEntryModel entry,
        ChemicalInventoryLookup lookup, bool showQuantity, string sdsBaseUrl, Func<string, string> text)
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

        var sds = entry?.Products.FirstOrDefault(p => p.ProductId == placement.ProductId && !string.IsNullOrEmpty(p.SdsFileName))
                  ?? entry?.Products.FirstOrDefault(p => !string.IsNullOrEmpty(p.SdsFileName));

        return new ChemicalAuthorityListRow(
            lookup.ProductName(placement.ChemicalId),
            entry?.RegistrationNo ?? string.Empty,
            entry?.StatusText ?? string.Empty,
            entry?.SignalWordText ?? string.Empty,
            string.Join(", ", entry?.HazardPictograms ?? []),
            string.Join("\n", (entry?.HazardStatements ?? []).Select(h => $"{h.Code} {h.Text}".Trim())),
            location?.Name ?? string.Empty,
            placement.PlacementNote ?? string.Empty,
            string.Join("\n", deadlines),
            showQuantity && placement.Unit is { } unit
                ? $"{ChemicalHistoryDetailsFormatter.Number(placement.Balance)} {ChemicalHistoryDetailsFormatter.Unit(unit)}"
                : string.Empty,
            sds is null
                ? string.Empty
                : $"{sdsBaseUrl.TrimEnd('/')}/{ChemicalBaseClient.SdsPath}?fileName={Uri.EscapeDataString(sds.SdsFileName)}");
    }
}
