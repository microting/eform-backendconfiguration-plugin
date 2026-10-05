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
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Infrastructure.Models.Chemicals;
using Microsoft.EntityFrameworkCore;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;

public partial class ChemicalInventoryService
{
    private const int MaxLocationNameLength = 250;

    public async Task<ChemicalLocationModel> CreateLocationAsync(ChemicalCaller caller, ChemicalCreateLocationCommand command)
    {
        await permissions.RequireAsync(caller, command.PropertyId, ChemicalPermission.ManageLocations).ConfigureAwait(false);
        var name = RequireText(command.Name, "location name", MaxLocationNameLength, required: true);
        await EnsureLocationNameFreeAsync(command.PropertyId, name, exceptLocationId: null).ConfigureAwait(false);
        var sortOrder = command.SortOrder is > 0
            ? command.SortOrder.Value
            : await NextSortOrderAsync(command.PropertyId).ConfigureAwait(false);

        var location = new ChemicalLocation
        {
            PropertyId = command.PropertyId,
            Name = name,
            Description = RequireText(command.Description, "description", MaxNoteLength, required: false),
            SortOrder = sortOrder,
            CreatedByUserId = caller.UserId,
            UpdatedByUserId = caller.UserId,
        };
        await location.Create(dbContext).ConfigureAwait(false);
        return MapLocation(location);
    }

    public async Task<ChemicalLocationModel> UpdateLocationAsync(ChemicalCaller caller, ChemicalUpdateLocationCommand command)
    {
        var location = await LoadManageableLocationAsync(caller, command.LocationId).ConfigureAwait(false);
        var name = RequireText(command.Name, "location name", MaxLocationNameLength, required: true);
        await EnsureLocationNameFreeAsync(location.PropertyId, name, location.Id).ConfigureAwait(false);

        location.Name = name;
        location.Description = RequireText(command.Description, "description", MaxNoteLength, required: false);
        if (command.SortOrder is > 0)
        {
            location.SortOrder = command.SortOrder.Value;
        }

        location.UpdatedByUserId = caller.UserId;
        await location.Update(dbContext).ConfigureAwait(false);
        return MapLocation(location);
    }

    public async Task<ChemicalLocationModel> ArchiveLocationAsync(ChemicalCaller caller, int locationId)
    {
        var location = await LoadManageableLocationAsync(caller, locationId).ConfigureAwait(false);
        var hasOpenPlacements = await dbContext.ChemicalPlacements
            .AnyAsync(p => p.LocationId == locationId && p.RemovedAt == null && p.WorkflowState != Removed)
            .ConfigureAwait(false);
        if (hasOpenPlacements)
        {
            throw new ChemicalPreconditionException("A location with open placements cannot be archived.");
        }

        location.UpdatedByUserId = caller.UserId;
        await location.Delete(dbContext).ConfigureAwait(false);
        return MapLocation(location);
    }

    public async Task<IReadOnlyList<ChemicalLocationModel>> ReorderLocationsAsync(
        ChemicalCaller caller, int propertyId, IReadOnlyList<int> orderedLocationIds)
    {
        // One transaction: a failure mid-loop must not leave a partial order. The
        // locations are loaded inside it so a retry starts from current rows.
        return await InTransactionAsync(async () =>
        {
            // Written before anything is read, like LockOpenPlacementAsync: a concurrent
            // reorder of the same property waits here for the other to commit, and the
            // rows read afterwards are current (rows already in place are skipped below,
            // so a stale snapshot would leave duplicate sort orders). On Galera the
            // written rows conflict at certification and the loser is retried. A refused
            // caller rolls the touch back.
            var touchedAt = DateTime.UtcNow;
            await dbContext.ChemicalLocations
                .Where(l => l.PropertyId == propertyId && l.WorkflowState != Removed)
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.UpdatedAt, touchedAt)).ConfigureAwait(false);
            await permissions.RequireAsync(caller, propertyId, ChemicalPermission.ManageLocations).ConfigureAwait(false);
            var locations = await dbContext.ChemicalLocations
                .Where(l => l.PropertyId == propertyId && l.WorkflowState != Removed)
                .ToListAsync().ConfigureAwait(false);
            if (orderedLocationIds.Count != locations.Count
                || !orderedLocationIds.ToHashSet().SetEquals(locations.Select(l => l.Id)))
            {
                throw new ArgumentException("The order must list every active location of the property exactly once.");
            }

            // Rows are updated in id order, not the client's order (a stable write order).
            var position = orderedLocationIds.Select((id, index) => (id, index)).ToDictionary(x => x.id, x => x.index + 1);
            foreach (var location in locations.OrderBy(l => l.Id))
            {
                if (location.SortOrder == position[location.Id])
                {
                    continue;
                }

                location.SortOrder = position[location.Id];
                location.UpdatedByUserId = caller.UserId;
                await location.Update(dbContext).ConfigureAwait(false);
            }

            return (IReadOnlyList<ChemicalLocationModel>)locations.OrderBy(l => l.SortOrder).Select(MapLocation).ToList();
        }).ConfigureAwait(false);
    }

    public Task RequireCanManageLocationAsync(ChemicalCaller caller, int locationId) =>
        LoadManageableLocationAsync(caller, locationId);

    public async Task<ChemicalLocationModel> SaveLocationPhotoAsync(
        ChemicalCaller caller, int locationId, byte[] content, string contentType)
    {
        var location = await LoadManageableLocationAsync(caller, locationId).ConfigureAwait(false);
        RequirePhoto(content, contentType);

        // A new name per upload: clients cache by photo_file_name.
        var fileName = $"chemical-location-{location.Id}-{Guid.NewGuid():N}.{PhotoExtensions[contentType]}";
        await using (var stream = new MemoryStream(content))
        {
            await fileStorage.PutAsync(fileName, stream).ConfigureAwait(false);
        }

        location.PhotoFileName = fileName;
        location.UpdatedByUserId = caller.UserId;
        await location.Update(dbContext).ConfigureAwait(false);
        return MapLocation(location);
    }

    public async Task<(byte[] Content, string ContentType)> GetLocationPhotoAsync(ChemicalCaller caller, int locationId)
    {
        // Deliberate exception to "archived ids return NOT_FOUND" (controller
        // ruling A-1): an archived location keeps its photo readable, because
        // the history of removed placements still shows where they stood. The
        // proto is unchanged; only this read skips the LoadActiveLocationAsync
        // filter. Every write still requires an active location.
        var location = await dbContext.ChemicalLocations.AsNoTracking()
                           .FirstOrDefaultAsync(l => l.Id == locationId).ConfigureAwait(false)
                       ?? throw new ChemicalNotFoundException($"Location {locationId} not found.");
        await permissions.RequireAsync(caller, location.PropertyId, ChemicalPermission.View).ConfigureAwait(false);
        if (string.IsNullOrEmpty(location.PhotoFileName))
        {
            throw new ChemicalNotFoundException($"Location {locationId} has no photo.");
        }

        await using var stream = await fileStorage.GetAsync(location.PhotoFileName).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer).ConfigureAwait(false);
        var extension = Path.GetExtension(location.PhotoFileName).TrimStart('.');
        var contentType = PhotoExtensions.First(p => p.Value.Equals(extension, StringComparison.OrdinalIgnoreCase)).Key;
        return (buffer.ToArray(), contentType);
    }

    /// <summary>An active location the caller may manage (ManageLocations on its property).</summary>
    private async Task<ChemicalLocation> LoadManageableLocationAsync(ChemicalCaller caller, int locationId)
    {
        var location = await LoadActiveLocationAsync(locationId).ConfigureAwait(false);
        await permissions.RequireAsync(caller, location.PropertyId, ChemicalPermission.ManageLocations).ConfigureAwait(false);
        return location;
    }

    private async Task EnsureLocationNameFreeAsync(int propertyId, string name, int? exceptLocationId)
    {
        // The column collation is case-insensitive, so "kemirum" collides with "Kemirum".
        var taken = await dbContext.ChemicalLocations
            .AnyAsync(l => l.PropertyId == propertyId && l.WorkflowState != Removed
                           && l.Name == name && l.Id != exceptLocationId)
            .ConfigureAwait(false);
        if (taken)
        {
            throw new ChemicalConflictException($"The property already has a location named \"{name}\".");
        }
    }

    private async Task<int> NextSortOrderAsync(int propertyId)
    {
        var last = await dbContext.ChemicalLocations
            .Where(l => l.PropertyId == propertyId && l.WorkflowState != Removed)
            .MaxAsync(l => (int?)l.SortOrder).ConfigureAwait(false);
        return (last ?? 0) + 1;
    }
}
