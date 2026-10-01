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

namespace BackendConfiguration.Pn.Services.ChemicalInventoryService;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Infrastructure.Models.Chemicals;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// GetMyInventory (spec §6). The placement is the unit of sync: it is resent,
/// with all its entries, when it or any of its entries changed. A property is
/// sent in full when its permission or PropertyWorker row changed after the token.
/// </summary>
public partial class ChemicalInventoryService
{
    /// <summary>Closed placements stay in the app's cache (and history) this long (spec §6).</summary>
    private const int ClosedPlacementMonths = 12;

    public async Task<ChemicalInventoryModel> GetInventoryAsync(ChemicalCaller caller, string since)
    {
        var now = UtcNow();
        var access = await permissions.ListVisiblePropertiesAsync(caller).ConfigureAwait(false);
        return await BuildInventoryAsync(access, ChemicalSyncToken.Parse(since, now), now, ChemicalSyncToken.Create(now))
            .ConfigureAwait(false);
    }

    public async Task<ChemicalInventoryModel> GetPropertyInventoryAsync(ChemicalCaller caller, int propertyId)
    {
        var access = (await permissions.ListVisiblePropertiesAsync(caller).ConfigureAwait(false))
            .Where(a => a.Access.PropertyId == propertyId)
            .ToList();
        if (access.Count == 0)
        {
            // Not visible: let the permission service pick NotFound or PermissionDenied.
            await permissions.RequireAsync(caller, propertyId, ChemicalPermission.View).ConfigureAwait(false);
            throw new ChemicalPermissionDeniedException($"No View permission on property {propertyId}.");
        }

        return await BuildInventoryAsync(access, null, UtcNow(), string.Empty).ConfigureAwait(false);
    }

    /// <summary>
    /// The visible properties (always complete), and the locations, placements,
    /// entries and register entries that changed after <paramref name="sinceUtc"/>
    /// or belong to a property whose access changed after it (sent in full).
    /// A null <paramref name="sinceUtc"/> is a full load.
    /// </summary>
    private async Task<ChemicalInventoryModel> BuildInventoryAsync(IReadOnlyList<ChemicalPropertyAccessRow> access,
        DateTime? sinceUtc, DateTime now, string syncToken)
    {
        var propertyIds = access.Select(a => a.Access.PropertyId).ToArray();
        var fullIds = access
            .Where(a => sinceUtc is null || a.AccessChangedAt > sinceUtc)
            .Select(a => a.Access.PropertyId)
            .ToArray();
        var closedCutoff = now.AddMonths(-ClosedPlacementMonths);

        // Archived locations are sent too (Archived = true) so the app can retire them.
        var locations = await dbContext.ChemicalLocations.AsNoTracking()
            .Where(l => propertyIds.Contains(l.PropertyId))
            .Where(l => sinceUtc == null || fullIds.Contains(l.PropertyId) || l.UpdatedAt > sinceUtc)
            .OrderBy(l => l.PropertyId).ThenBy(l => l.SortOrder).ThenBy(l => l.Id)
            .ToListAsync().ConfigureAwait(false);

        // Placements are never soft-deleted (removal sets RemovedAt and keeps the row),
        // so the WorkflowState filter only skips manual deletes and the delta sends no tombstones.
        var held = await (
                from placement in dbContext.ChemicalPlacements.AsNoTracking()
                join location in dbContext.ChemicalLocations.AsNoTracking() on placement.LocationId equals location.Id
                where propertyIds.Contains(location.PropertyId)
                      && placement.WorkflowState != Removed
                      && (placement.RemovedAt == null || placement.RemovedAt >= closedCutoff)
                select new
                {
                    placement.Id,
                    placement.ChemicalId,
                    location.PropertyId,
                    Changed = sinceUtc == null
                              || placement.UpdatedAt > sinceUtc
                              || dbContext.ChemicalStockEntries.Any(e => e.PlacementId == placement.Id && e.UpdatedAt > sinceUtc),
                })
            .ToListAsync().ConfigureAwait(false);

        var changedPlacementIds = held
            .Where(x => x.Changed || fullIds.Contains(x.PropertyId))
            .Select(x => x.Id)
            .ToList();
        var placements = await LoadPlacementModelsAsync(changedPlacementIds).ConfigureAwait(false);
        var entries = await LoadEntryModelsAsync(changedPlacementIds).ConfigureAwait(false);

        // Register entries travel with resent placements, plus any held chemical the nightly sync changed.
        var chemicalIds = placements.Select(p => p.ChemicalId).ToHashSet();
        if (sinceUtc is { } changedAfter)
        {
            var heldChemicalIds = held.Select(x => x.ChemicalId).Distinct().ToList();
            chemicalIds.UnionWith(await register.ChangedSinceAsync(heldChemicalIds, changedAfter).ConfigureAwait(false));
        }

        var registerEntries = await register.GetByIdsAsync(chemicalIds).ConfigureAwait(false);

        return new ChemicalInventoryModel(
            access.Select(a => a.Access).ToList(),
            locations.Select(MapLocation).ToList(),
            placements,
            entries,
            registerEntries,
            syncToken,
            sinceUtc is null);
    }
}
