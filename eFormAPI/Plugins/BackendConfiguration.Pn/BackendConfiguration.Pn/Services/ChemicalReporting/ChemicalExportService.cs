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
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), ChemicalAlertRules.Copenhagen).ToString(format, CultureInfo.InvariantCulture);

    private string FileName(string titleKey, string propertyName, string extension) =>
        $"{ComplianceExportFileNaming.SanitiseFileNamePart(T(titleKey))} - {ComplianceExportFileNaming.SanitiseFileNamePart(propertyName)} - {Local(UtcNow, "yyyy-MM-dd")}.{extension}";

    public async Task<ChemicalExportFile> AuthorityListPdfAsync(int userId, int propertyId)
    {
        var model = await inventory.GetPropertyInventoryAsync(ChemicalCaller.Web(userId), propertyId).ConfigureAwait(false);
        var document = ChemicalAuthorityListBuilder.Build(model, propertyId, UtcNow, chemicalBase.Value.BaseUrl, T);
        return new ChemicalExportFile(ChemicalAuthorityListPdf.Render(document),
            FileName(K.ChemicalAuthorityListTitle, PropertyName(model, propertyId), "pdf"), PdfContentType);
    }

    public async Task<ChemicalExportFile> InventoryXlsxAsync(int userId, int propertyId, IReadOnlyCollection<int> placementIds)
    {
        var model = await inventory.GetPropertyInventoryAsync(ChemicalCaller.Web(userId), propertyId).ConfigureAwait(false);
        var stock = model.Properties.FirstOrDefault(p => p.PropertyId == propertyId)?.StockEnabled ?? false;
        var wanted = placementIds?.ToHashSet();
        var today = ChemicalAlertRules.CopenhagenToday(UtcNow);
        var (locations, entries) = Lookups(model);

        var headers = new List<string> { T(K.ChemicalColumnProduct), T(K.ChemicalColumnRegistrationNo), T(K.ChemicalColumnStatus), T(K.ChemicalColumnLocation), T(K.ChemicalColumnPlacement) };
        if (stock)
        {
            headers.AddRange([T(K.ChemicalColumnQuantity), T(K.ChemicalColumnUnit)]);
        }

        headers.AddRange([T(K.ChemicalColumnSalesDeadline), T(K.ChemicalColumnUseAndPossessionDeadline), T(K.ChemicalColumnRequiresAction), T(K.ChemicalColumnRegisteredBy), T(K.ChemicalColumnRegisteredAt)]);

        var rows = model.Placements
            .Where(p => p.RemovedAt == null && p.PropertyId == propertyId && (wanted == null || wanted.Contains(p.Id)))
            .OrderBy(p => locations.GetValueOrDefault(p.LocationId)?.SortOrder ?? int.MaxValue)
            .ThenBy(p => entries.GetValueOrDefault(p.ChemicalId)?.Name ?? string.Empty)
            .Select(p =>
            {
                var entry = entries.GetValueOrDefault(p.ChemicalId);
                var row = new List<object>
                {
                    entry?.Name ?? $"#{p.ChemicalId}", entry?.RegistrationNo ?? string.Empty, entry?.StatusText ?? string.Empty,
                    locations.GetValueOrDefault(p.LocationId)?.Name ?? string.Empty, p.PlacementNote ?? string.Empty,
                };
                if (stock)
                {
                    row.Add(p.Unit is null ? (object)string.Empty : p.Balance);
                    row.Add(ChemicalHistoryDetailsFormatter.Unit(p.Unit));
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

        return new ChemicalExportFile(ChemicalXlsxWriter.Write(T(K.ChemicalInventoryExportTitle), headers, rows),
            FileName(K.ChemicalInventoryExportTitle, PropertyName(model, propertyId), "xlsx"), XlsxContentType);
    }

    public async Task<ChemicalExportFile> RemovedPlacementsXlsxAsync(int userId, int propertyId)
    {
        var model = await inventory.GetPropertyInventoryAsync(ChemicalCaller.Web(userId), propertyId).ConfigureAwait(false);
        var (locations, entries) = Lookups(model);
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
                entries.GetValueOrDefault(p.ChemicalId)?.Name ?? $"#{p.ChemicalId}",
                entries.GetValueOrDefault(p.ChemicalId)?.RegistrationNo ?? string.Empty,
                locations.GetValueOrDefault(p.LocationId)?.Name ?? string.Empty,
                p.PlacementNote ?? string.Empty,
                T(ChemicalHistoryDetailsFormatter.ReasonKey(p.RemovalReason)),
                Local(p.RemovedAt!.Value, "dd-MM-yyyy"),
                p.RemovedByName ?? string.Empty,
                p.RemovalNote ?? string.Empty,
            });

        return new ChemicalExportFile(ChemicalXlsxWriter.Write(T(K.ChemicalRemovedPlacementsTitle), headers, rows),
            FileName(K.ChemicalRemovedPlacementsTitle, PropertyName(model, propertyId), "xlsx"), XlsxContentType);
    }

    public async Task<ChemicalExportFile> HistoryXlsxAsync(int propertyId, ChemicalHistoryQuery query)
    {
        var events = await history.GetAllAsync(propertyId, query).ConfigureAwait(false);
        var propertyName = await dbContext.Properties.AsNoTracking().Where(p => p.Id == propertyId)
            .Select(p => p.Name).FirstOrDefaultAsync().ConfigureAwait(false) ?? string.Empty;
        string[] headers =
        [
            T(K.ChemicalColumnTime), T(K.ChemicalColumnUser), T(K.ChemicalColumnAction), T(K.ChemicalColumnProduct),
            T(K.ChemicalColumnRegistrationNo), T(K.ChemicalColumnLocation), T(K.ChemicalColumnDetails),
        ];
        var rows = events.Select(e => (IReadOnlyList<object>)new object[]
        {
            Local(e.At, "dd-MM-yyyy HH:mm"),
            e.UserId == 0 ? T(K.ChemicalSystemUser) : e.UserName,
            T(ChemicalHistoryDetailsFormatter.ActionKey(e.Action)),
            e.ProductName,
            e.RegistrationNo,
            e.LocationName,
            ChemicalHistoryDetailsFormatter.Format(e, T),
        });

        return new ChemicalExportFile(ChemicalXlsxWriter.Write(T(K.ChemicalHistoryTitle), headers, rows),
            FileName(K.ChemicalHistoryTitle, propertyName, "xlsx"), XlsxContentType);
    }

    private static string PropertyName(ChemicalInventoryModel model, int propertyId) =>
        model.Properties.FirstOrDefault(p => p.PropertyId == propertyId)?.Name ?? string.Empty;

    private static (Dictionary<int, ChemicalLocationModel> Locations, Dictionary<int, ChemicalRegisterEntryModel> Entries) Lookups(ChemicalInventoryModel model) =>
        (model.Locations.ToDictionary(l => l.Id), model.RegisterEntries.ToDictionary(e => e.ChemicalId));
}
