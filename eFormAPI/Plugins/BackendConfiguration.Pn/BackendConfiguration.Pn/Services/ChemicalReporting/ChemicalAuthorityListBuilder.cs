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
    public static ChemicalAuthorityListDocument Build(
        ChemicalInventoryModel inventory, int propertyId, DateTime generatedAtUtc, string sdsBaseUrl, Func<string, string> text)
    {
        var property = inventory.Properties.FirstOrDefault(p => p.PropertyId == propertyId);
        var showQuantity = property?.StockEnabled ?? false;
        var locations = inventory.Locations.ToDictionary(l => l.Id);
        var entries = inventory.RegisterEntries.ToDictionary(e => e.ChemicalId);
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(generatedAtUtc, DateTimeKind.Utc), ChemicalAlertRules.Copenhagen);

        var rows = inventory.Placements
            .Where(p => p.RemovedAt == null && p.PropertyId == propertyId)
            .Select(p => (Placement: p, Location: locations.GetValueOrDefault(p.LocationId), Entry: entries.GetValueOrDefault(p.ChemicalId)))
            .OrderBy(x => x.Location?.SortOrder ?? int.MaxValue)
            .ThenBy(x => x.Entry?.Name ?? string.Empty, TagNameComparer.Danish)
            .ThenBy(x => x.Placement.LocationId)
            .ThenBy(x => x.Placement.Id)
            .Select(x => Row(x.Placement, x.Location, x.Entry, showQuantity, sdsBaseUrl, text))
            .ToList();

        var headers = new List<string>
        {
            text(K.ChemicalColumnProduct), text(K.ChemicalColumnRegistrationNo), text(K.ChemicalColumnStatus),
            text(K.ChemicalColumnSignalWord), text(K.ChemicalColumnPictograms), text(K.ChemicalColumnHazardStatements),
            text(K.ChemicalColumnLocation), text(K.ChemicalColumnPlacement), text(K.ChemicalColumnDeadlines),
        };
        if (showQuantity)
        {
            headers.Add(text(K.ChemicalColumnQuantity));
        }

        headers.Add(text(K.ChemicalColumnSds));

        return new ChemicalAuthorityListDocument(
            text(K.ChemicalAuthorityListTitle),
            $"{text(K.ChemicalPropertyLabel)}: {property?.Name} · {text(K.ChemicalGeneratedLabel)}: {local.ToString("dd-MM-yyyy HH:mm", CultureInfo.InvariantCulture)}",
            headers, rows, showQuantity, text(K.ChemicalSdsLink));
    }

    private static ChemicalAuthorityListRow Row(
        ChemicalPlacementModel placement, ChemicalLocationModel location, ChemicalRegisterEntryModel entry,
        bool showQuantity, string sdsBaseUrl, Func<string, string> text)
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
            entry?.Name ?? $"#{placement.ChemicalId}",
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
