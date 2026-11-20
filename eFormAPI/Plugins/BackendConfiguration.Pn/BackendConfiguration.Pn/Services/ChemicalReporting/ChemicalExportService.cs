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
using System.Threading.Tasks;
using BackendConfigurationComplianceExportService;
using BackendConfigurationLocalizationService;
using ChemicalInventoryService;
using Infrastructure.Helpers;
using Infrastructure.Models.Chemicals;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Helpers;
using K = Infrastructure.Models.Chemicals.ChemicalLocalizationKeys;

public class ChemicalExportService(
    IChemicalInventoryService inventory,
    IChemicalHistoryService history,
    BackendConfigurationPnDbContext dbContext,
    IBackendConfigurationLocalizationService localization,
    IOptions<ChemicalBaseOptions> chemicalBase,
    TimeProvider time) : IChemicalExportService
{
    public const string PdfContentType = "application/pdf";
    public const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private string T(string key) => localization.GetString(key);

    private DateTime UtcNow => time.GetUtcNow().UtcDateTime;

    private static string Local(DateTime utc, string format) =>
        ChemicalCopenhagen.ToCopenhagen(utc).ToString(format, CultureInfo.InvariantCulture);

    private string FileName(string titleKey, string propertyName, string extension) =>
        $"{ComplianceExportFileNaming.SanitiseFileNamePart(T(titleKey))} - {ComplianceExportFileNaming.SanitiseFileNamePart(propertyName)} - {Local(UtcNow, "yyyy-MM-dd")}.{extension}";

    public async Task<ChemicalExportFile> AuthorityListPdfAsync(int userId, int propertyId)
    {
        var model = await LoadAsync(userId, propertyId).ConfigureAwait(false);
        var document = ChemicalAuthorityListBuilder.Build(model, propertyId, UtcNow, chemicalBase.Value.BaseUrl, T);
        return new ChemicalExportFile(ChemicalAuthorityListPdf.Render(document),
            FileName(K.ChemicalAuthorityListTitle, new ChemicalInventoryLookup(model).PropertyName(propertyId), "pdf"), PdfContentType);
    }

    public async Task<ChemicalExportFile> InventoryXlsxAsync(int userId, int propertyId, IReadOnlyCollection<int> placementIds)
    {
        var model = await LoadAsync(userId, propertyId).ConfigureAwait(false);
        return InventoryXlsx(model, p => p.PropertyId == propertyId, placementIds, new ChemicalInventoryLookup(model).PropertyName(propertyId));
    }

    public async Task<ChemicalExportFile> AllPropertiesInventoryXlsxAsync(int userId, IReadOnlyCollection<int> placementIds)
    {
        var model = await inventory.GetAllPropertiesInventoryAsync(ChemicalCaller.Web(userId)).ConfigureAwait(false);
        return InventoryXlsx(model, _ => true, placementIds, T(K.ChemicalAllProperties));
    }

    /// <summary>
    /// The W1 Excel: the open placements of <paramref name="model"/> that <paramref name="include"/> keeps (and, when
    /// given, whose id the page showed), each with its property (the grid's Ejendom column). The stock columns are
    /// there when any exported property keeps stock; a row of a property without stock leaves them empty.
    /// </summary>
    private ChemicalExportFile InventoryXlsx(ChemicalInventoryModel model, Func<ChemicalPlacementModel, bool> include,
        IReadOnlyCollection<int> placementIds, string fileNameProperty)
    {
        var stockByProperty = model.Properties.ToDictionary(p => p.PropertyId, p => p.StockEnabled);
        var stock = stockByProperty.Values.Any(enabled => enabled);
        var wanted = placementIds?.ToHashSet();
        var today = ChemicalAlertRules.CopenhagenToday(UtcNow);
        var lookup = new ChemicalInventoryLookup(model);

        var headers = new List<string>
        {
            T(K.ChemicalColumnProduct), T(K.ChemicalColumnRegistrationNo), T(K.ChemicalColumnStatus),
            T(K.ChemicalPropertyLabel), T(K.ChemicalColumnLocation), T(K.ChemicalColumnPlacement),
        };
        if (stock)
        {
            headers.AddRange([T(K.ChemicalColumnQuantity), T(K.ChemicalColumnUnit)]);
        }

        headers.AddRange([T(K.ChemicalColumnSalesDeadline), T(K.ChemicalColumnUseAndPossessionDeadline), T(K.ChemicalColumnRequiresAction), T(K.ChemicalColumnRegisteredBy), T(K.ChemicalColumnRegisteredAt)]);

        var rows = model.Placements
            .Where(p => p.RemovedAt == null && include(p) && (wanted == null || wanted.Contains(p.Id)))
            .OrderBy(p => lookup.PropertyName(p.PropertyId), TagNameComparer.Danish)
            .ThenBy(p => p.PropertyId)
            .ThenBy(p => lookup.Location(p.LocationId)?.SortOrder ?? int.MaxValue)
            .ThenBy(p => lookup.Entry(p.ChemicalId)?.Name ?? string.Empty, TagNameComparer.Danish)
            .ThenBy(p => p.LocationId)
            .ThenBy(p => p.Id)
            .Select(p =>
            {
                var entry = lookup.Entry(p.ChemicalId);
                var row = new List<object>
                {
                    lookup.ProductName(p.ChemicalId), entry?.RegistrationNo ?? string.Empty, entry?.StatusText ?? string.Empty,
                    lookup.PropertyName(p.PropertyId), lookup.Location(p.LocationId)?.Name ?? string.Empty, p.PlacementNote ?? string.Empty,
                };
                if (stock)
                {
                    var counted = stockByProperty.GetValueOrDefault(p.PropertyId) && p.Unit is not null;
                    row.Add(counted ? WithoutTrailingZeros(p.Balance) : string.Empty);
                    row.Add(counted ? ChemicalHistoryDetailsFormatter.Unit(p.Unit) : string.Empty);
                }

                row.AddRange([
                    entry?.SalesDeadline?.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture) ?? string.Empty,
                    entry?.UseAndPossessionDeadline?.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture) ?? string.Empty,
                    T(entry != null && ChemicalAlertRules.RequiresAction(entry.Status, entry.SalesDeadline, entry.UseAndPossessionDeadline, today) ? K.ChemicalYes : K.ChemicalNo),
                    p.RegisteredByName ?? string.Empty,
                    Local(p.RegisteredAt, "dd-MM-yyyy"),
                ]);
                return (IReadOnlyList<object>)row;
            });

        return Xlsx(K.ChemicalInventoryExportTitle, fileNameProperty, headers, rows);
    }

    public async Task<ChemicalExportFile> RemovedPlacementsXlsxAsync(int userId, int propertyId)
    {
        var model = await LoadAsync(userId, propertyId).ConfigureAwait(false);
        var lookup = new ChemicalInventoryLookup(model);
        string[] headers =
        [
            T(K.ChemicalColumnProduct), T(K.ChemicalColumnRegistrationNo), T(K.ChemicalColumnLocation), T(K.ChemicalColumnPlacement),
            T(K.ChemicalColumnRemovalReason), T(K.ChemicalColumnRemovedAt), T(K.ChemicalColumnRemovedBy), T(K.ChemicalColumnNote),
        ];
        var rows = model.Placements
            .Where(p => p.RemovedAt != null && p.PropertyId == propertyId)
            .OrderByDescending(p => p.RemovedAt)
            .Select(p => (IReadOnlyList<object>)new object[]
            {
                lookup.ProductName(p.ChemicalId),
                lookup.Entry(p.ChemicalId)?.RegistrationNo ?? string.Empty,
                lookup.Location(p.LocationId)?.Name ?? string.Empty,
                p.PlacementNote ?? string.Empty,
                T(ChemicalHistoryDetailsFormatter.ReasonKey(p.RemovalReason)),
                Local(p.RemovedAt!.Value, "dd-MM-yyyy"),
                p.RemovedByName ?? string.Empty,
                p.RemovalNote ?? string.Empty,
            });

        return Xlsx(K.ChemicalRemovedPlacementsTitle, lookup.PropertyName(propertyId), headers, rows);
    }

    public async Task<ChemicalExportFile> HistoryXlsxAsync(int propertyId, ChemicalHistoryQuery query)
    {
        var events = await history.GetAllAsync(propertyId, query).ConfigureAwait(false);
        var propertyName = await dbContext.Properties.AsNoTracking().Where(p => p.Id == propertyId)
            .Select(p => p.Name).FirstOrDefaultAsync().ConfigureAwait(false) ?? string.Empty;
        return HistoryXlsx(events, propertyName, withProperty: false);
    }

    public async Task<ChemicalExportFile> AllPropertiesHistoryXlsxAsync(IReadOnlyCollection<int> propertyIds, ChemicalHistoryQuery query)
    {
        var events = await history.GetAllForPropertiesAsync(propertyIds, query).ConfigureAwait(false);
        return HistoryXlsx(events, T(K.ChemicalAllProperties), withProperty: true);
    }

    /// <summary>The W6 Excel; <paramref name="withProperty"/> adds the Ejendom column the page shows under "All".</summary>
    private ChemicalExportFile HistoryXlsx(IReadOnlyList<ChemicalHistoryEventModel> events, string fileNameProperty, bool withProperty)
    {
        var headers = new List<string>
        {
            T(K.ChemicalColumnTime), T(K.ChemicalColumnUser), T(K.ChemicalColumnAction), T(K.ChemicalColumnProduct),
            T(K.ChemicalColumnRegistrationNo),
        };
        if (withProperty)
        {
            headers.Add(T(K.ChemicalPropertyLabel));
        }

        headers.AddRange([T(K.ChemicalColumnLocation), T(K.ChemicalColumnDetails)]);
        var rows = events.Select(e =>
        {
            var row = new List<object>
            {
                Local(e.At, "dd-MM-yyyy HH:mm"),
                e.UserId == 0 ? T(K.ChemicalSystemUser) : e.UserName,
                T(ChemicalHistoryDetailsFormatter.ActionKey(e.Action)),
                e.ProductName,
                e.RegistrationNo,
            };
            if (withProperty)
            {
                row.Add(e.PropertyName);
            }

            row.AddRange([e.LocationName, ChemicalHistoryDetailsFormatter.Format(e, T)]);
            return (IReadOnlyList<object>)row;
        });

        return Xlsx(K.ChemicalHistoryTitle, fileNameProperty, headers, rows);
    }

    /// <summary>The database column keeps a fixed scale ("2.000"); the sheet shows the number as typed ("2").</summary>
    private static decimal WithoutTrailingZeros(decimal value) => value / 1.000000000000000000000000000000000m;

    private Task<ChemicalInventoryModel> LoadAsync(int userId, int propertyId) =>
        inventory.GetPropertyInventoryAsync(ChemicalCaller.Web(userId), propertyId);

    /// <summary>The sheet name and the file name come from the same title key, so they cannot drift apart.</summary>
    private ChemicalExportFile Xlsx(string titleKey, string propertyName, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object>> rows) =>
        new(ChemicalXlsxWriter.Write(T(titleKey), headers, rows), FileName(titleKey, propertyName, "xlsx"), XlsxContentType);
}
