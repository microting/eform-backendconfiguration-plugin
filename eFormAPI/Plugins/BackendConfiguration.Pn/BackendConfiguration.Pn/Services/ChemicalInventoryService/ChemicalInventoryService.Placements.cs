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

    // Every write below runs its loads and checks inside InTransactionAsync: a
    // retried transaction must start from current rows, not from entities that
    // still hold the values of the rolled-back attempt.

    public async Task<ChemicalPlacementChangeModel> RegisterPlacementAsync(ChemicalCaller caller, ChemicalRegisterPlacementCommand command)
    {
        var placementId = await InTransactionAsync(async () =>
        {
            var location = await LoadActiveLocationAsync(command.LocationId).ConfigureAwait(false);
            await permissions.RequireAsync(caller, location.PropertyId, ChemicalPermission.Register).ConfigureAwait(false);
            var product = await register.FindProductAsync(command.ChemicalId, command.ProductId).ConfigureAwait(false);
            var note = RequireText(command.PlacementNote, "placement note", MaxNoteLength, required: false);
            var now = UtcNow();

            // Built and validated before the placement is written; its placement id is set once the placement exists.
            ChemicalStockEntry initialEntry = null;
            if (command.InitialStock is { } initial)
            {
                await RequireStockEnabledAsync(location.PropertyId).ConfigureAwait(false);
                initialEntry = NewEntry(ChemicalStockEntryKindEnum.Received, ChemicalQuantity.ResolveMovedAmount(initial),
                    initial.Unit, caller, ResolveEntryTime(initial.At, now), initial);
            }

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
            if (initialEntry != null)
            {
                initialEntry.PlacementId = placement.Id;
                await initialEntry.Create(dbContext).ConfigureAwait(false);
            }

            return placement.Id;
        }).ConfigureAwait(false);

        return await ChangeResultAsync([placementId]).ConfigureAwait(false);
    }

    public async Task<ChemicalPlacementChangeModel> MovePlacementAsync(ChemicalCaller caller, ChemicalMovePlacementCommand command)
    {
        var ids = await InTransactionAsync(async () =>
        {
            var (source, propertyId) = await LoadOpenPlacementAsync(caller, command.PlacementId, ChemicalPermission.Register)
                .ConfigureAwait(false);
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

        var placementId = await InTransactionAsync(async () =>
        {
            var (placement, _) = await LoadOpenPlacementAsync(caller, command.PlacementId, ChemicalPermission.Remove)
                .ConfigureAwait(false);
            var removedAt = ResolveEntryTime(command.RemovedAt, UtcNow());
            if (removedAt < placement.RegisteredAt)
            {
                throw new ArgumentException("The removal date is before the placement was registered.");
            }

            var note = RequireText(command.Note, "note", MaxNoteLength, required: false);
            var (balance, unit) = await StockStateAsync(placement.Id).ConfigureAwait(false);

            if (balance != 0 && unit is { } stockUnit)
            {
                // FindWriteOff recognises this entry by its kind, removedAt and the zero balance after it.
                await AddEntryAsync(placement.Id, WriteOffKind(command.Reason), -balance, stockUnit, caller, removedAt)
                    .ConfigureAwait(false);
            }

            await ClosePlacementAsync(placement, command.Reason, removedAt, note, caller).ConfigureAwait(false);
            return placement.Id;
        }).ConfigureAwait(false);

        return await ChangeResultAsync([placementId]).ConfigureAwait(false);
    }

    public async Task<ChemicalPlacementChangeModel> UpdatePlacementNoteAsync(ChemicalCaller caller, int placementId, string placementNote)
    {
        var (placement, _) = await LoadOpenPlacementAsync(caller, placementId, ChemicalPermission.Register).ConfigureAwait(false);
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

        var placementId = await InTransactionAsync(async () =>
        {
            var (placement, propertyId) = await LoadOpenPlacementAsync(caller, command.PlacementId, ChemicalPermission.Stock)
                .ConfigureAwait(false);
            await RequireStockEnabledAsync(propertyId).ConfigureAwait(false);

            var amount = command.Amount ?? throw new ArgumentException("An amount is required.");
            ChemicalQuantity.RequireUnit(amount.Unit);
            var (balance, unit) = await StockStateAsync(placement.Id).ConfigureAwait(false);
            if (unit is { } existing && existing != amount.Unit)
            {
                throw new ArgumentException($"This placement is counted in {existing}; use the same unit.");
            }

            decimal delta;
            switch (command.Kind)
            {
                case ChemicalStockEntryKindEnum.Received:
                    delta = ChemicalQuantity.ResolveMovedAmount(amount);
                    break;
                case ChemicalStockEntryKindEnum.Consumed:
                    var consumed = ChemicalQuantity.ResolveMovedAmount(amount);
                    if (consumed > balance)
                    {
                        throw new ArgumentException("The consumption exceeds the balance.");
                    }

                    delta = -consumed;
                    break;
                default:
                    delta = ChemicalQuantity.ResolveCountedBalance(amount) - balance;
                    if (delta == 0)
                    {
                        throw new ArgumentException("The adjustment does not change the balance.");
                    }

                    break;
            }

            var at = ResolveEntryTime(amount.At, UtcNow());
            await AddEntryAsync(placement.Id, command.Kind, delta, amount.Unit, caller, at, amount).ConfigureAwait(false);
            return placement.Id;
        }).ConfigureAwait(false);

        return await ChangeResultAsync([placementId]).ConfigureAwait(false);
    }

    // ---- shared with the sync (Task 13) ----

    private async Task<ChemicalPlacementChangeModel> ChangeResultAsync(IReadOnlyCollection<int> placementIds)
    {
        var (placements, entries) = await LoadPlacementsAndEntriesAsync(placementIds).ConfigureAwait(false);
        var registerEntries = await register.GetByIdsAsync(placements.Select(p => p.ChemicalId).Distinct().ToList())
            .ConfigureAwait(false);
        return new ChemicalPlacementChangeModel(placements, entries, registerEntries);
    }

    /// <summary>
    /// The placements and all their live entries (listed by At, then Id), with the
    /// ledger facts derived from those entries rather than stored, so rows written
    /// before the facts existed carry them too:
    /// <list type="bullet">
    /// <item>BalanceAfter: the running sum in id (write) order, the order each write
    /// was validated in. ADJUSTED therefore shows the counted balance, and the last
    /// value equals the placement's Balance.</item>
    /// <item>CounterpartPlacementId: MovedIn → the placement's MovedFromPlacementId;
    /// MovedOut → the placement that move created (see MoveCounterpartsAsync).</item>
    /// <item>WriteOffEntryId / Origin RemovalWriteOff: see FindWriteOff.</item>
    /// </list>
    /// </summary>
    private async Task<(List<ChemicalPlacementModel> Placements, List<ChemicalStockEntryModel> Entries)>
        LoadPlacementsAndEntriesAsync(IReadOnlyCollection<int> placementIds)
    {
        var ids = placementIds.Distinct().ToArray();
        var rows = await (
                from placement in dbContext.ChemicalPlacements.AsNoTracking()
                join location in dbContext.ChemicalLocations.AsNoTracking() on placement.LocationId equals location.Id
                where ids.Contains(placement.Id)
                orderby placement.Id
                select new { Placement = placement, location.PropertyId })
            .ToListAsync().ConfigureAwait(false);
        var entries = await LiveEntries(ids).OrderBy(e => e.Id).ToListAsync().ConfigureAwait(false);
        var entriesByPlacement = entries.ToLookup(e => e.PlacementId);

        var balanceAfter = new Dictionary<int, decimal>();
        foreach (var ledger in entriesByPlacement)
        {
            var balance = 0m;
            foreach (var entry in ledger)
            {
                balance += entry.Amount;
                balanceAfter[entry.Id] = balance;
            }
        }

        var writeOffs = rows
            .Select(r => (r.Placement.Id, EntryId: FindWriteOff(r.Placement, entriesByPlacement[r.Placement.Id], balanceAfter)))
            .Where(w => w.EntryId != null)
            .ToDictionary(w => w.Id, w => w.EntryId.Value);
        var writeOffEntryIds = writeOffs.Values.ToHashSet();
        var counterparts = await MoveCounterpartsAsync(entries, rows.Select(r => r.Placement)).ConfigureAwait(false);

        var userNames = await names.UserNamesAsync(rows
                .SelectMany(r => new[] { r.Placement.RegisteredByUserId, r.Placement.RemovedByUserId ?? 0 })
                .Concat(entries.Select(e => e.ByUserId)))
            .ConfigureAwait(false);

        var placements = rows.Select(r =>
        {
            var p = r.Placement;
            var ledger = entriesByPlacement[p.Id].ToList();
            return new ChemicalPlacementModel(
                p.Id, p.LocationId, r.PropertyId, p.ChemicalId, p.ProductId, p.PlacementNote ?? string.Empty,
                p.RegisteredByUserId, userNames.GetValueOrDefault(p.RegisteredByUserId, string.Empty), Utc(p.RegisteredAt),
                p.RemovedByUserId,
                p.RemovedByUserId is { } removedBy ? userNames.GetValueOrDefault(removedBy, string.Empty) : string.Empty,
                p.RemovedAt.HasValue ? Utc(p.RemovedAt) : null,
                p.RemovalReason, p.RemovalNote ?? string.Empty, p.MovedFromPlacementId,
                ledger.Sum(e => e.Amount),
                ledger.Count > 0 ? ledger[^1].Unit : null,
                Utc(p.UpdatedAt),
                writeOffs.TryGetValue(p.Id, out var writeOff) ? writeOff : null);
        }).ToList();

        var entryModels = entries
            .OrderBy(e => e.At).ThenBy(e => e.Id)
            .Select(e => new ChemicalStockEntryModel(
                e.Id, e.PlacementId, e.Kind, e.ContainerSize, e.Unit, e.Amount, e.ContainerCount,
                e.BatchLot ?? string.Empty, e.Note ?? string.Empty, e.ByUserId,
                userNames.GetValueOrDefault(e.ByUserId, string.Empty), Utc(e.At),
                balanceAfter[e.Id],
                e.Kind is ChemicalStockEntryKindEnum.MovedOut or ChemicalStockEntryKindEnum.MovedIn
                    ? ChemicalStockEntryOriginEnum.Move
                    : writeOffEntryIds.Contains(e.Id) ? ChemicalStockEntryOriginEnum.RemovalWriteOff : ChemicalStockEntryOriginEnum.Manual,
                counterparts.TryGetValue(e.Id, out var counterpart) ? counterpart : null))
            .ToList();

        return (placements, entryModels);
    }

    /// <summary>
    /// The entry RemovePlacementAsync wrote to zero the balance of a placement
    /// removed as Used or Disposed: the newest entry of the write-off kind dated
    /// exactly RemovedAt (both are written from one value) that took the balance
    /// down to zero. Null for an open or moved placement, or one removed with a
    /// zero balance. A hand-written entry is indistinguishable only if it has the
    /// write-off's kind, zeroed the balance and carries the removal's timestamp to
    /// the microsecond.
    /// </summary>
    private static int? FindWriteOff(ChemicalPlacement placement, IEnumerable<ChemicalStockEntry> ledger,
        IReadOnlyDictionary<int, decimal> balanceAfter)
    {
        if (placement.RemovedAt is not { } removedAt
            || placement.RemovalReason is not (ChemicalRemovalReasonEnum.Used or ChemicalRemovalReasonEnum.Disposed))
        {
            return null;
        }

        var kind = WriteOffKind(placement.RemovalReason.Value);
        return ledger.LastOrDefault(e => e.Kind == kind && e.At == removedAt && e.Amount < 0 && balanceAfter[e.Id] == 0)?.Id;
    }

    /// <summary>
    /// Entry id → the other placement of its move. A MovedIn entry belongs to the
    /// placement the move created, so its counterpart is MovedFromPlacementId. A
    /// MovedOut entry's counterpart is the placement created from its source at its
    /// instant: MovePlacementAsync writes the entry's At and that placement's
    /// RegisteredAt from one value. Several moves from one source at one instant are
    /// paired in id order (a move without stock writes no entry, but it closes the
    /// source, so it is always the last of them). The created placements may lie
    /// outside the loaded set, so they are looked up.
    /// </summary>
    private async Task<Dictionary<int, int>> MoveCounterpartsAsync(IReadOnlyList<ChemicalStockEntry> entries,
        IEnumerable<ChemicalPlacement> placements)
    {
        var movedFrom = placements.ToDictionary(p => p.Id, p => p.MovedFromPlacementId);
        var counterparts = entries
            .Where(e => e.Kind == ChemicalStockEntryKindEnum.MovedIn && movedFrom.GetValueOrDefault(e.PlacementId) != null)
            .ToDictionary(e => e.Id, e => movedFrom[e.PlacementId]!.Value);

        var movedOut = entries.Where(e => e.Kind == ChemicalStockEntryKindEnum.MovedOut).ToList();
        if (movedOut.Count == 0)
        {
            return counterparts;
        }

        var sourceIds = movedOut.Select(e => e.PlacementId).Distinct().ToArray();
        var created = (await dbContext.ChemicalPlacements.AsNoTracking()
                .Where(p => p.MovedFromPlacementId != null && sourceIds.Contains(p.MovedFromPlacementId.Value))
                .OrderBy(p => p.Id)
                .Select(p => new { p.Id, SourceId = p.MovedFromPlacementId.Value, p.RegisteredAt })
                .ToListAsync().ConfigureAwait(false))
            .ToLookup(p => (p.SourceId, p.RegisteredAt));
        foreach (var move in movedOut.GroupBy(e => (e.PlacementId, e.At)))
        {
            foreach (var (entry, target) in move.Zip(created[move.Key]))
            {
                counterparts[entry.Id] = target.Id;
            }
        }

        return counterparts;
    }

    // ---- private helpers ----

    /// <summary>
    /// The tracked, still-open placement and its property. The caller's permission is checked
    /// before the open state is revealed; a missing id is NotFound.
    /// </summary>
    private async Task<(ChemicalPlacement Placement, int PropertyId)> LoadOpenPlacementAsync(
        ChemicalCaller caller, int placementId, ChemicalPermission permission)
    {
        var row = await (
                      from placement in dbContext.ChemicalPlacements
                      join location in dbContext.ChemicalLocations on placement.LocationId equals location.Id
                      where placement.Id == placementId && placement.WorkflowState != Removed
                      select new { Placement = placement, location.PropertyId })
                  .FirstOrDefaultAsync().ConfigureAwait(false)
                  ?? throw new ChemicalNotFoundException($"Placement {placementId} not found.");
        await permissions.RequireAsync(caller, row.PropertyId, permission).ConfigureAwait(false);
        if (row.Placement.RemovedAt != null)
        {
            throw new ChemicalPreconditionException($"Placement {placementId} is already closed.");
        }

        return (row.Placement, row.PropertyId);
    }

    /// <summary>The placements' stock entries that count (not soft-deleted).</summary>
    private IQueryable<ChemicalStockEntry> LiveEntries(IReadOnlyCollection<int> placementIds)
    {
        var ids = placementIds.Distinct().ToArray();
        return dbContext.ChemicalStockEntries.AsNoTracking()
            .Where(e => ids.Contains(e.PlacementId) && e.WorkflowState != Removed);
    }

    /// <summary>
    /// Balance (sum of the live entries) and unit of one placement; the unit is that
    /// of the latest written entry (highest id), null without entries.
    /// </summary>
    private async Task<(decimal Balance, ChemicalStockUnitEnum? Unit)> StockStateAsync(int placementId)
    {
        var entries = await LiveEntries([placementId])
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
        var entry = NewEntry(kind, amount, unit, caller, at, details);
        entry.PlacementId = placementId;
        await entry.Create(dbContext).ConfigureAwait(false);
    }

    /// <summary>An unsaved entry with its texts validated; the caller sets PlacementId.</summary>
    private static ChemicalStockEntry NewEntry(ChemicalStockEntryKindEnum kind, decimal amount,
        ChemicalStockUnitEnum unit, ChemicalCaller caller, DateTime at, ChemicalStockAmountModel details) => new()
    {
        Kind = kind,
        Amount = amount,
        Unit = unit,
        ContainerSize = details?.ContainerSize,
        ContainerCount = details?.ContainerCount,
        BatchLot = RequireText(details?.BatchLot, "batch/lot", MaxBatchLotLength, required: false),
        Note = RequireText(details?.Note, "note", MaxNoteLength, required: false),
        ByUserId = caller.UserId,
        At = at,
        CreatedByUserId = caller.UserId,
        UpdatedByUserId = caller.UserId,
    };

    /// <summary>The kind of the entry that writes off the balance on removal: Used consumes it, Disposed adjusts it away.</summary>
    private static ChemicalStockEntryKindEnum WriteOffKind(ChemicalRemovalReasonEnum reason) =>
        reason == ChemicalRemovalReasonEnum.Used ? ChemicalStockEntryKindEnum.Consumed : ChemicalStockEntryKindEnum.Adjusted;

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
