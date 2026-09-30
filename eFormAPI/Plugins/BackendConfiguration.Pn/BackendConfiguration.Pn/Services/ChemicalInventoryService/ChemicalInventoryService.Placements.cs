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
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;

public partial class ChemicalInventoryService
{
    private const int MaxBatchLotLength = 100;

    /// <summary>Client clocks drift; a date this far ahead of the server is still "now".</summary>
    private static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(5);

    public async Task<ChemicalPlacementChangeModel> RegisterPlacementAsync(ChemicalCaller caller, ChemicalRegisterPlacementCommand command)
    {
        var location = await LoadActiveLocationAsync(command.LocationId).ConfigureAwait(false);
        await permissions.RequireAsync(caller, location.PropertyId, ChemicalPermission.Register).ConfigureAwait(false);
        var product = await register.FindProductAsync(command.ChemicalId, command.ProductId).ConfigureAwait(false);
        var note = RequireText(command.PlacementNote, "placement note", MaxNoteLength, required: false);
        var now = UtcNow();

        decimal initialAmount = 0;
        var initialAt = now;
        if (command.InitialStock != null)
        {
            await RequireStockEnabledAsync(location.PropertyId).ConfigureAwait(false);
            initialAmount = ChemicalQuantity.ResolveMovedAmount(command.InitialStock);
            initialAt = ResolveEntryTime(command.InitialStock.At, now);
            // Fail on the entry's texts before the placement row is written.
            EntryText(command.InitialStock);
        }

        var placementId = await InTransactionAsync(async () =>
        {
            var placement = new ChemicalPlacement
            {
                LocationId = location.Id,
                ChemicalId = product.ChemicalId,
                ProductId = product.ProductId,
                PlacementNote = note,
                RegisteredByUserId = caller.UserId,
                RegisteredAt = now,
                ObservedStatus = product.Status,
                CreatedByUserId = caller.UserId,
                UpdatedByUserId = caller.UserId,
            };
            await placement.Create(dbContext).ConfigureAwait(false);
            if (command.InitialStock != null)
            {
                await AddEntryAsync(placement.Id, ChemicalStockEntryKindEnum.Received, initialAmount,
                    command.InitialStock.Unit, caller, initialAt, command.InitialStock).ConfigureAwait(false);
            }

            return placement.Id;
        }).ConfigureAwait(false);

        return await ChangeResultAsync([placementId]).ConfigureAwait(false);
    }

    public async Task<ChemicalPlacementChangeModel> MovePlacementAsync(ChemicalCaller caller, ChemicalMovePlacementCommand command)
    {
        var (source, propertyId) = await LoadPlacementAsync(command.PlacementId).ConfigureAwait(false);
        await permissions.RequireAsync(caller, propertyId, ChemicalPermission.Register).ConfigureAwait(false);
        RequireOpen(source);
        var target = await LoadActiveLocationAsync(command.TargetLocationId).ConfigureAwait(false);
        if (target.PropertyId != propertyId)
        {
            throw new ArgumentException("A placement can only move to a location on the same property.");
        }

        if (target.Id == source.LocationId)
        {
            throw new ArgumentException("The placement is already at that location.");
        }

        var note = RequireText(command.TargetPlacementNote, "placement note", MaxNoteLength, required: false);
        var (balance, unit) = await StockStateAsync(source.Id).ConfigureAwait(false);

        var moved = balance;
        if (command.Amount is { } requested)
        {
            if (unit is null)
            {
                throw new ArgumentException("Only a placement with stock can be moved in part.");
            }

            await RequireStockEnabledAsync(propertyId).ConfigureAwait(false);
            ChemicalQuantity.RequireMoveAmount(requested);
            if (requested > balance)
            {
                throw new ArgumentException("The amount to move exceeds the balance.");
            }

            moved = requested;
        }

        var partial = moved < balance;
        var now = UtcNow();

        var ids = await InTransactionAsync(async () =>
        {
            var created = new ChemicalPlacement
            {
                LocationId = target.Id,
                ChemicalId = source.ChemicalId,
                ProductId = source.ProductId,
                PlacementNote = note,
                RegisteredByUserId = caller.UserId,
                RegisteredAt = now,
                MovedFromPlacementId = source.Id,
                ObservedStatus = source.ObservedStatus,
                CreatedByUserId = caller.UserId,
                UpdatedByUserId = caller.UserId,
            };
            await created.Create(dbContext).ConfigureAwait(false);

            // The balance travels with the product even while stock is switched
            // off, so the sums stay right if it is switched on again.
            if (moved != 0 && unit is { } stockUnit)
            {
                await AddEntryAsync(source.Id, ChemicalStockEntryKindEnum.MovedOut, -moved, stockUnit, caller, now).ConfigureAwait(false);
                await AddEntryAsync(created.Id, ChemicalStockEntryKindEnum.MovedIn, moved, stockUnit, caller, now).ConfigureAwait(false);
            }

            if (!partial)
            {
                await ClosePlacementAsync(source, ChemicalRemovalReasonEnum.Moved, now, null, caller).ConfigureAwait(false);
            }

            return new[] { source.Id, created.Id };
        }).ConfigureAwait(false);

        return await ChangeResultAsync(ids).ConfigureAwait(false);
    }

    public async Task<ChemicalPlacementChangeModel> RemovePlacementAsync(ChemicalCaller caller, ChemicalRemovePlacementCommand command)
    {
        if (command.Reason is not (ChemicalRemovalReasonEnum.Used or ChemicalRemovalReasonEnum.Disposed))
        {
            throw new ArgumentException("Removal needs the reason Used or Disposed; Moved is set by a move.");
        }

        var (placement, propertyId) = await LoadPlacementAsync(command.PlacementId).ConfigureAwait(false);
        await permissions.RequireAsync(caller, propertyId, ChemicalPermission.Remove).ConfigureAwait(false);
        RequireOpen(placement);
        var removedAt = ResolveEntryTime(command.RemovedAt, UtcNow());
        if (removedAt < placement.RegisteredAt)
        {
            throw new ArgumentException("The removal date is before the placement was registered.");
        }

        var note = RequireText(command.Note, "note", MaxNoteLength, required: false);
        var (balance, unit) = await StockStateAsync(placement.Id).ConfigureAwait(false);

        await InTransactionAsync(async () =>
        {
            if (balance != 0 && unit is { } stockUnit)
            {
                var writeOff = command.Reason == ChemicalRemovalReasonEnum.Used
                    ? ChemicalStockEntryKindEnum.Consumed
                    : ChemicalStockEntryKindEnum.Adjusted;
                await AddEntryAsync(placement.Id, writeOff, -balance, stockUnit, caller, removedAt).ConfigureAwait(false);
            }

            await ClosePlacementAsync(placement, command.Reason, removedAt, note, caller).ConfigureAwait(false);
            return true;
        }).ConfigureAwait(false);

        return await ChangeResultAsync([placement.Id]).ConfigureAwait(false);
    }

    public async Task<ChemicalPlacementChangeModel> UpdatePlacementNoteAsync(ChemicalCaller caller, int placementId, string placementNote)
    {
        var (placement, propertyId) = await LoadPlacementAsync(placementId).ConfigureAwait(false);
        await permissions.RequireAsync(caller, propertyId, ChemicalPermission.Register).ConfigureAwait(false);
        RequireOpen(placement);
        placement.PlacementNote = RequireText(placementNote, "placement note", MaxNoteLength, required: false);
        placement.UpdatedByUserId = caller.UserId;
        await placement.Update(dbContext).ConfigureAwait(false);
        return await ChangeResultAsync([placement.Id]).ConfigureAwait(false);
    }

    public async Task<ChemicalPlacementChangeModel> AddStockEntryAsync(ChemicalCaller caller, ChemicalAddStockEntryCommand command)
    {
        if (command.Kind is not (ChemicalStockEntryKindEnum.Received or ChemicalStockEntryKindEnum.Consumed
            or ChemicalStockEntryKindEnum.Adjusted))
        {
            throw new ArgumentException("Only Received, Consumed and Adjusted entries can be added; moves write their own.");
        }

        var (placement, propertyId) = await LoadPlacementAsync(command.PlacementId).ConfigureAwait(false);
        await permissions.RequireAsync(caller, propertyId, ChemicalPermission.Stock).ConfigureAwait(false);
        RequireOpen(placement);
        await RequireStockEnabledAsync(propertyId).ConfigureAwait(false);

        var amount = command.Amount ?? throw new ArgumentException("An amount is required.");
        ChemicalQuantity.RequireUnit(amount.Unit);
        var (balance, unit) = await StockStateAsync(placement.Id).ConfigureAwait(false);
        if (unit is { } existing && existing != amount.Unit)
        {
            throw new ArgumentException($"This placement is counted in {existing}; use the same unit.");
        }

        var delta = command.Kind switch
        {
            ChemicalStockEntryKindEnum.Received => ChemicalQuantity.ResolveMovedAmount(amount),
            ChemicalStockEntryKindEnum.Consumed => -ChemicalQuantity.ResolveMovedAmount(amount),
            _ => ChemicalQuantity.ResolveCountedBalance(amount) - balance,
        };
        if (balance + delta < 0)
        {
            throw new ArgumentException("The consumption exceeds the balance.");
        }

        if (delta == 0)
        {
            throw new ArgumentException("The adjustment does not change the balance.");
        }

        var at = ResolveEntryTime(amount.At, UtcNow());
        await AddEntryAsync(placement.Id, command.Kind, delta, amount.Unit, caller, at, amount).ConfigureAwait(false);
        return await ChangeResultAsync([placement.Id]).ConfigureAwait(false);
    }

    // ---- shared with the sync (Task 13) ----

    private async Task<ChemicalPlacementChangeModel> ChangeResultAsync(IReadOnlyCollection<int> placementIds)
    {
        var placements = await LoadPlacementModelsAsync(placementIds).ConfigureAwait(false);
        var entries = await LoadEntryModelsAsync(placementIds).ConfigureAwait(false);
        var registerEntries = await register.GetByIdsAsync(placements.Select(p => p.ChemicalId).Distinct().ToList())
            .ConfigureAwait(false);
        return new ChemicalPlacementChangeModel(placements, entries, registerEntries);
    }

    private async Task<List<ChemicalPlacementModel>> LoadPlacementModelsAsync(IReadOnlyCollection<int> placementIds)
    {
        var ids = placementIds.Distinct().ToArray();
        var rows = await (
                from placement in dbContext.ChemicalPlacements.AsNoTracking()
                join location in dbContext.ChemicalLocations.AsNoTracking() on placement.LocationId equals location.Id
                where ids.Contains(placement.Id)
                orderby placement.Id
                select new { Placement = placement, location.PropertyId })
            .ToListAsync().ConfigureAwait(false);
        var stock = await dbContext.ChemicalStockEntries.AsNoTracking()
            .Where(e => ids.Contains(e.PlacementId) && e.WorkflowState != Removed)
            .Select(e => new { e.Id, e.PlacementId, e.Amount, e.Unit })
            .ToListAsync().ConfigureAwait(false);
        var stockByPlacement = stock
            .GroupBy(e => e.PlacementId)
            .ToDictionary(g => g.Key, g => (Balance: g.Sum(e => e.Amount), Unit: g.OrderByDescending(e => e.Id).First().Unit));
        var userNames = await names.UserNamesAsync(
                rows.SelectMany(r => new[] { r.Placement.RegisteredByUserId, r.Placement.RemovedByUserId ?? 0 }))
            .ConfigureAwait(false);

        return rows.Select(r =>
        {
            var p = r.Placement;
            var hasStock = stockByPlacement.TryGetValue(p.Id, out var s);
            return new ChemicalPlacementModel(
                p.Id, p.LocationId, r.PropertyId, p.ChemicalId, p.ProductId, p.PlacementNote ?? string.Empty,
                p.RegisteredByUserId, userNames.GetValueOrDefault(p.RegisteredByUserId, string.Empty), Utc(p.RegisteredAt),
                p.RemovedByUserId,
                p.RemovedByUserId is { } removedBy ? userNames.GetValueOrDefault(removedBy, string.Empty) : string.Empty,
                p.RemovedAt.HasValue ? Utc(p.RemovedAt) : null,
                p.RemovalReason, p.RemovalNote ?? string.Empty, p.MovedFromPlacementId,
                hasStock ? s.Balance : 0m,
                hasStock ? s.Unit : null,
                Utc(p.UpdatedAt));
        }).ToList();
    }

    private async Task<List<ChemicalStockEntryModel>> LoadEntryModelsAsync(IReadOnlyCollection<int> placementIds)
    {
        var ids = placementIds.Distinct().ToArray();
        var entries = await dbContext.ChemicalStockEntries.AsNoTracking()
            .Where(e => ids.Contains(e.PlacementId) && e.WorkflowState != Removed)
            .OrderBy(e => e.At).ThenBy(e => e.Id)
            .ToListAsync().ConfigureAwait(false);
        var userNames = await names.UserNamesAsync(entries.Select(e => e.ByUserId)).ConfigureAwait(false);
        return entries.Select(e => new ChemicalStockEntryModel(
                e.Id, e.PlacementId, e.Kind, e.ContainerSize, e.Unit, e.Amount, e.ContainerCount,
                e.BatchLot ?? string.Empty, e.Note ?? string.Empty, e.ByUserId,
                userNames.GetValueOrDefault(e.ByUserId, string.Empty), Utc(e.At)))
            .ToList();
    }

    // ---- private helpers ----

    /// <summary>The tracked placement and its property. Permission is checked by the caller before any state is revealed.</summary>
    private async Task<(ChemicalPlacement Placement, int PropertyId)> LoadPlacementAsync(int placementId)
    {
        var row = await (
                      from placement in dbContext.ChemicalPlacements
                      join location in dbContext.ChemicalLocations on placement.LocationId equals location.Id
                      where placement.Id == placementId && placement.WorkflowState != Removed
                      select new { Placement = placement, location.PropertyId })
                  .FirstOrDefaultAsync().ConfigureAwait(false)
                  ?? throw new ChemicalNotFoundException($"Placement {placementId} not found.");
        return (row.Placement, row.PropertyId);
    }

    private static void RequireOpen(ChemicalPlacement placement)
    {
        if (placement.RemovedAt != null)
        {
            throw new ChemicalPreconditionException($"Placement {placement.Id} is already closed.");
        }
    }

    private async Task<(decimal Balance, ChemicalStockUnitEnum? Unit)> StockStateAsync(int placementId)
    {
        var entries = await dbContext.ChemicalStockEntries.AsNoTracking()
            .Where(e => e.PlacementId == placementId && e.WorkflowState != Removed)
            .OrderBy(e => e.Id)
            .Select(e => new { e.Amount, e.Unit })
            .ToListAsync().ConfigureAwait(false);
        return entries.Count == 0 ? (0m, null) : (entries.Sum(e => e.Amount), entries[^1].Unit);
    }

    private async Task ClosePlacementAsync(ChemicalPlacement placement, ChemicalRemovalReasonEnum reason, DateTime at,
        string note, ChemicalCaller caller)
    {
        placement.RemovedAt = at;
        placement.RemovedByUserId = caller.UserId;
        placement.RemovalReason = reason;
        placement.RemovalNote = note;
        placement.UpdatedByUserId = caller.UserId;
        await placement.Update(dbContext).ConfigureAwait(false);
    }

    private async Task AddEntryAsync(int placementId, ChemicalStockEntryKindEnum kind, decimal amount,
        ChemicalStockUnitEnum unit, ChemicalCaller caller, DateTime at, ChemicalStockAmountModel details = null)
    {
        var (batchLot, note) = EntryText(details);
        var entry = new ChemicalStockEntry
        {
            PlacementId = placementId,
            Kind = kind,
            Amount = amount,
            Unit = unit,
            ContainerSize = details?.ContainerSize,
            ContainerCount = details?.ContainerCount,
            BatchLot = batchLot,
            Note = note,
            ByUserId = caller.UserId,
            At = at,
            CreatedByUserId = caller.UserId,
            UpdatedByUserId = caller.UserId,
        };
        await entry.Create(dbContext).ConfigureAwait(false);
    }

    private static (string BatchLot, string Note) EntryText(ChemicalStockAmountModel details) =>
        (RequireText(details?.BatchLot, "batch/lot", MaxBatchLotLength, required: false),
            RequireText(details?.Note, "note", MaxNoteLength, required: false));

    private static DateTime ResolveEntryTime(DateTime? requested, DateTime now)
    {
        if (requested is not { } at)
        {
            return now;
        }

        var utc = at.Kind == DateTimeKind.Local ? at.ToUniversalTime() : DateTime.SpecifyKind(at, DateTimeKind.Utc);
        if (utc > now + FutureTolerance)
        {
            throw new ArgumentException("The date cannot be in the future.");
        }

        return utc;
    }
}
