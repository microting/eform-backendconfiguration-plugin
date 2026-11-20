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
using System.Linq;
using System.Threading.Tasks;
using ChemicalInventoryService;
using Infrastructure.Models.Chemicals;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.EformBackendConfigurationBase.Infrastructure.Helpers;
using BmdTexts = Microting.EformBackendConfigurationBase.Infrastructure.Const.Constants;

/// <summary>
/// W6 "Historik" (spec §5.1: history is read from the version tables, stock
/// entries and ChemicalAlertLog; nothing is edited in place). Derived per request
/// for one property, or the picker's "All" (every visible property in one pass), and one period (Plan E decision 5).
/// </summary>
public class ChemicalHistoryService(
    BackendConfigurationPnDbContext dbContext,
    IChemicalRegisterReader register,
    IChemicalNameDirectory names) : IChemicalHistoryService
{
    public const int MaxPeriodDays = 366;
    public const int MaxPageSize = 100;

    private const string Removed = Constants.WorkflowStates.Removed;

    public async Task<ChemicalHistoryPageModel> GetPageAsync(int propertyId, ChemicalHistoryQuery query) =>
        Page(await LoadAsync([propertyId], query, mustExist: true).ConfigureAwait(false), query);

    public async Task<IReadOnlyList<ChemicalHistoryEventModel>> GetAllAsync(int propertyId, ChemicalHistoryQuery query) =>
        Filter(await LoadAsync([propertyId], query, mustExist: true).ConfigureAwait(false), query).ToList();

    public async Task<ChemicalHistoryPageModel> GetPageForPropertiesAsync(IReadOnlyCollection<int> propertyIds, ChemicalHistoryQuery query) =>
        Page(await LoadAsync(propertyIds, query, mustExist: false).ConfigureAwait(false), query);

    public async Task<IReadOnlyList<ChemicalHistoryEventModel>> GetAllForPropertiesAsync(IReadOnlyCollection<int> propertyIds, ChemicalHistoryQuery query) =>
        Filter(await LoadAsync(propertyIds, query, mustExist: false).ConfigureAwait(false), query).ToList();

    /// <summary>The users of the whole period (for the filter), then the filtered events of one page.</summary>
    private static ChemicalHistoryPageModel Page(IReadOnlyList<ChemicalHistoryEventModel> all, ChemicalHistoryQuery query)
    {
        var users = all.Where(e => e.UserId > 0)
            .GroupBy(e => e.UserId)
            .Select(g => new ChemicalHistoryUserModel(g.Key, g.First().UserName))
            .OrderBy(u => u.Name)
            .ToList();
        var filtered = Filter(all, query).ToList();
        var size = Math.Clamp(query.PageSize, 1, MaxPageSize);
        var page = Math.Max(query.Page, 0);
        return new ChemicalHistoryPageModel(filtered.Skip(page * size).Take(size).ToList(), filtered.Count, users);
    }

    private static IEnumerable<ChemicalHistoryEventModel> Filter(IEnumerable<ChemicalHistoryEventModel> events, ChemicalHistoryQuery query) =>
        events
            .Where(e => query.UserId is null || e.UserId == query.UserId)
            .Where(e => query.Action is null || e.Action == query.Action)
            .Where(e => query.LocationId is null || e.LocationId == query.LocationId || e.TargetLocationId == query.LocationId);

    /// <summary>The dates are Europe/Copenhagen calendar days; the result is the matching UTC interval [From, ToExclusive).</summary>
    private static (DateTime From, DateTime ToExclusive) Period(ChemicalHistoryQuery query)
    {
        if (query.From.TimeOfDay != TimeSpan.Zero || query.To.TimeOfDay != TimeSpan.Zero)
        {
            throw new ArgumentException("The period takes dates only, without a time of day.");
        }

        var days = (query.To.Date - query.From.Date).Days + 1;
        if (days < 1)
        {
            throw new ArgumentException("The period ends before it starts.");
        }

        if (days > MaxPeriodDays)
        {
            throw new ArgumentException($"The period is at most {MaxPeriodDays} days.");
        }

        return (ChemicalCopenhagen.LocalMidnightToUtc(query.From), ChemicalCopenhagen.LocalMidnightToUtc(query.To.AddDays(1)));
    }

    private static DateTime LocalDay(DateTime utc) => ChemicalCopenhagen.ToCopenhagen(utc).Date;

    /// <summary>
    /// The log of <paramref name="requestedIds"/> in one pass: every source is read once for all of them (the picker's
    /// "All" costs the same round-trips as one property), and each event names the property of its row. One property's
    /// events keep the order and the keys they have alone. Removed or unknown ids are skipped, unless
    /// <paramref name="mustExist"/> (one property asked by id): then NotFound.
    /// </summary>
    private async Task<List<ChemicalHistoryEventModel>> LoadAsync(IReadOnlyCollection<int> requestedIds, ChemicalHistoryQuery query, bool mustExist)
    {
        var (from, to) = Period(query);
        var requested = requestedIds.ToArray();
        var propertyNames = requested.Length == 0
            ? []
            : await dbContext.Properties.AsNoTracking()
                .Where(p => requested.Contains(p.Id) && p.WorkflowState != Removed)
                .Select(p => new { p.Id, p.Name })
                .ToDictionaryAsync(p => p.Id, p => p.Name ?? string.Empty).ConfigureAwait(false);
        if (propertyNames.Count == 0)
        {
            return mustExist ? throw new ChemicalNotFoundException($"Property {requested.FirstOrDefault()} not found.") : [];
        }

        var propertyIds = propertyNames.Keys.ToArray();

        bool InPeriod(DateTime? at) => at is { } value && value >= from && value < to;

        var locations = await dbContext.ChemicalLocations.AsNoTracking()
            .Where(l => propertyIds.Contains(l.PropertyId)).OrderBy(l => l.Id).ToDictionaryAsync(l => l.Id).ConfigureAwait(false);
        var locationIds = locations.Keys.ToArray();
        var placements = await dbContext.ChemicalPlacements.AsNoTracking()
            .Where(p => locationIds.Contains(p.LocationId) && p.WorkflowState != Removed).OrderBy(p => p.Id)
            .ToDictionaryAsync(p => p.Id).ConfigureAwait(false);
        var placementIds = placements.Keys.ToArray();
        var entries = await dbContext.ChemicalStockEntries.AsNoTracking()
            .Where(e => placementIds.Contains(e.PlacementId) && e.WorkflowState != Removed)
            .OrderBy(e => e.Id).ToListAsync().ConfigureAwait(false);
        var placementVersions = await dbContext.ChemicalPlacementVersions.AsNoTracking()
            .Where(v => placementIds.Contains(v.ChemicalPlacementId)).OrderBy(v => v.Id).ToListAsync().ConfigureAwait(false);
        var locationVersions = await dbContext.ChemicalLocationVersions.AsNoTracking()
            .Where(v => propertyIds.Contains(v.PropertyId)).OrderBy(v => v.Id).ToListAsync().ConfigureAwait(false);
        var permissionVersions = await dbContext.ChemicalWorkerPermissionVersions.AsNoTracking()
            .Where(v => propertyIds.Contains(v.PropertyId)).OrderBy(v => v.Id).ToListAsync().ConfigureAwait(false);
        var settingsVersions = await dbContext.ChemicalPropertySettingsVersions.AsNoTracking()
            .Where(v => propertyIds.Contains(v.PropertyId)).OrderBy(v => v.Id).ToListAsync().ConfigureAwait(false);
        var alerts = await dbContext.ChemicalAlertLogs.AsNoTracking()
            .Where(a => placementIds.Contains(a.PlacementId) && a.SentAt >= from && a.SentAt < to && a.WorkflowState != Removed)
            .OrderBy(a => a.Id).ToListAsync().ConfigureAwait(false);

        // Running balance over ALL entries, in Id order, with the helper the inventory itself uses.
        var entriesByPlacement = entries.ToLookup(e => e.PlacementId);
        var entriesById = entries.ToDictionary(e => e.Id);
        var balanceAfter = ChemicalInventoryService.BalanceAfterByEntry(entries);

        // E-06: the write-off entry of a stocked removal is folded into the Removed event.
        var writeOffs = placements.Values
            .Select(p => (p.Id, EntryId: ChemicalInventoryService.FindWriteOff(p, entriesByPlacement[p.Id], balanceAfter)))
            .Where(w => w.EntryId != null)
            .ToDictionary(w => w.Id, w => w.EntryId!.Value);
        var writeOffEntryIds = writeOffs.Values.ToHashSet();

        string LocationName(int? id) => id is { } value && locations.TryGetValue(value, out var l) ? l.Name : string.Empty;

        ChemicalHistoryEventModel ForPlacement(ChemicalPlacement p, DateTime at, int userId, ChemicalHistoryActionEnum action) => new()
        {
            At = at, UserId = userId, Action = action, PlacementId = p.Id, ChemicalId = p.ChemicalId,
            LocationId = p.LocationId, LocationName = LocationName(p.LocationId),
            // Placements are read through the locations, so the location is always there.
            PropertyId = locations[p.LocationId].PropertyId,
        };

        var events = new List<ChemicalHistoryEventModel>();

        foreach (var p in placements.Values)
        {
            if (InPeriod(p.RegisteredAt))
            {
                if (p.MovedFromPlacementId is { } sourceId)
                {
                    var source = placements.GetValueOrDefault(sourceId);
                    var movedIn = entriesByPlacement[p.Id].FirstOrDefault(e => e.Kind == ChemicalStockEntryKindEnum.MovedIn);
                    events.Add(ForPlacement(p, p.RegisteredAt, p.RegisteredByUserId, ChemicalHistoryActionEnum.Moved) with
                    {
                        LocationId = source?.LocationId, LocationName = LocationName(source?.LocationId),
                        TargetLocationId = p.LocationId, TargetLocationName = LocationName(p.LocationId),
                        Amount = movedIn?.Amount, Unit = movedIn?.Unit, Note = p.PlacementNote ?? string.Empty,
                    });
                }
                else
                {
                    events.Add(ForPlacement(p, p.RegisteredAt, p.RegisteredByUserId, ChemicalHistoryActionEnum.Registered) with
                    {
                        Note = p.PlacementNote ?? string.Empty,
                    });
                }
            }

            if (p.RemovedAt is { } removedAt && InPeriod(removedAt)
                && p.RemovalReason is ChemicalRemovalReasonEnum.Used or ChemicalRemovalReasonEnum.Disposed)
            {
                var writeOff = writeOffs.TryGetValue(p.Id, out var entryId) ? entriesById[entryId] : null;
                events.Add(ForPlacement(p, removedAt, p.RemovedByUserId ?? 0, ChemicalHistoryActionEnum.Removed) with
                {
                    RemovalReason = p.RemovalReason, Note = p.RemovalNote ?? string.Empty,
                    Amount = writeOff?.Amount, Unit = writeOff?.Unit,
                });
            }
        }

        foreach (var group in entriesByPlacement)
        {
            var placement = placements[group.Key];
            foreach (var entry in group)
            {
                ChemicalHistoryActionEnum? action = entry.Kind switch
                {
                    ChemicalStockEntryKindEnum.Received => ChemicalHistoryActionEnum.StockReceived,
                    ChemicalStockEntryKindEnum.Consumed => ChemicalHistoryActionEnum.StockConsumed,
                    ChemicalStockEntryKindEnum.Adjusted => ChemicalHistoryActionEnum.StockAdjusted,
                    _ => null,
                };
                if (action is { } stockAction && InPeriod(entry.At) && !writeOffEntryIds.Contains(entry.Id))
                {
                    events.Add(ForPlacement(placement, entry.At, entry.ByUserId, stockAction) with
                    {
                        Amount = entry.Amount, Unit = entry.Unit, BalanceAfter = balanceAfter[entry.Id], Note = entry.Note ?? string.Empty,
                    });
                }
            }
        }

        foreach (var (previous, current) in placementVersions.GroupBy(v => v.ChemicalPlacementId).SelectMany(g => Pairs(g)))
        {
            if (InPeriod(current.UpdatedAt) && previous.PlacementNote != current.PlacementNote)
            {
                events.Add(ForPlacement(placements[current.ChemicalPlacementId], current.UpdatedAt!.Value, current.UpdatedByUserId,
                    ChemicalHistoryActionEnum.NoteChanged) with
                {
                    OldValue = previous.PlacementNote ?? string.Empty, NewValue = current.PlacementNote ?? string.Empty,
                });
            }
        }

        ChemicalHistoryEventModel ForLocation(ChemicalLocationVersion v, ChemicalHistoryActionEnum action) => new()
        {
            At = v.UpdatedAt!.Value, UserId = v.UpdatedByUserId, Action = action, LocationId = v.ChemicalLocationId, LocationName = v.Name,
            PropertyId = v.PropertyId,
        };

        foreach (var versions in locationVersions.GroupBy(v => v.ChemicalLocationId))
        {
            var first = versions.First();
            if (InPeriod(first.UpdatedAt))
            {
                events.Add(ForLocation(first, ChemicalHistoryActionEnum.LocationCreated) with { NewValue = first.Name });
            }

            foreach (var (previous, current) in Pairs(versions))
            {
                if (!InPeriod(current.UpdatedAt))
                {
                    continue;
                }

                if (previous.Name != current.Name)
                {
                    events.Add(ForLocation(current, ChemicalHistoryActionEnum.LocationRenamed) with { OldValue = previous.Name, NewValue = current.Name });
                }

                if (previous.WorkflowState != Removed && current.WorkflowState == Removed)
                {
                    events.Add(ForLocation(current, ChemicalHistoryActionEnum.LocationArchived));
                }

                if (previous.PhotoFileName != current.PhotoFileName)
                {
                    events.Add(ForLocation(current, ChemicalHistoryActionEnum.LocationPhotoChanged));
                }
            }
        }

        var workerNames = await names.WorkerNamesAsync(permissionVersions.Select(v => v.WorkerId).Distinct()).ConfigureAwait(false);
        foreach (var versions in permissionVersions.GroupBy(v => v.ChemicalWorkerPermissionId))
        {
            ChemicalWorkerPermissionVersion previous = null;
            foreach (var current in versions)
            {
                var before = Flags(previous);
                var after = Flags(current);
                previous = current;
                var added = after.Except(before).ToList();
                var removed = before.Except(after).ToList();
                if (InPeriod(current.UpdatedAt) && (added.Count > 0 || removed.Count > 0))
                {
                    events.Add(new ChemicalHistoryEventModel
                    {
                        At = current.UpdatedAt!.Value, UserId = current.UpdatedByUserId, Action = ChemicalHistoryActionEnum.PermissionChanged,
                        WorkerName = workerNames.GetValueOrDefault(current.WorkerId, string.Empty),
                        PermissionsAdded = added, PermissionsRemoved = removed, PropertyId = current.PropertyId,
                    });
                }
            }
        }

        // Each property's settings history on its own: its first version is compared with nothing.
        foreach (var versions in settingsVersions.GroupBy(v => v.PropertyId))
        {
            ChemicalPropertySettingsVersion previousSettings = null;
            foreach (var current in versions)
            {
                var oldStock = previousSettings?.StockEnabled ?? false;
                var oldRecipients = previousSettings?.DigestRecipients ?? string.Empty;
                previousSettings = current;
                if (!InPeriod(current.UpdatedAt))
                {
                    continue;
                }

                if (oldStock != current.StockEnabled)
                {
                    events.Add(Setting(current, "StockEnabled", oldStock ? "true" : "false", current.StockEnabled ? "true" : "false"));
                }

                if (oldRecipients != (current.DigestRecipients ?? string.Empty))
                {
                    events.Add(Setting(current, "DigestRecipients", oldRecipients, current.DigestRecipients ?? string.Empty));
                }
            }
        }

        events.AddRange(alerts
            .GroupBy(a => new { a.PlacementId, a.Threshold, a.DeadlineKind, a.Status, Day = LocalDay(a.SentAt) })
            .Select(g => ForPlacement(placements[g.Key.PlacementId], g.Min(a => a.SentAt), 0,
                g.Key.Threshold == ChemicalAlertThresholdEnum.StatusChanged
                    ? ChemicalHistoryActionEnum.StatusChanged
                    : ChemicalHistoryActionEnum.AlertSent) with
            {
                Status = g.Key.Status,
                StatusText = g.Key.Status is { } status && BmdTexts.ProductStatusType.TryGetValue(status, out var text) ? text : string.Empty,
                Threshold = g.Key.Threshold, DeadlineKind = g.Key.DeadlineKind,
                PushCount = g.Count(a => a.Channel == ChemicalAlertChannelEnum.Push),
                EmailCount = g.Count(a => a.Channel == ChemicalAlertChannelEnum.Email),
            }));

        var chemicalIds = events.Where(e => e.ChemicalId.HasValue).Select(e => e.ChemicalId!.Value).Distinct().ToList();
        var products = (await register.GetByIdsAsync(chemicalIds).ConfigureAwait(false)).ToDictionary(e => e.ChemicalId);
        var userNames = await names.UserNamesAsync(events.Where(e => e.UserId > 0).Select(e => e.UserId).Distinct()).ConfigureAwait(false);

        string ProductName(int? id)
        {
            if (id is not { } chemicalId)
            {
                return string.Empty;
            }

            return products.TryGetValue(chemicalId, out var product) ? product.Name : ChemicalInventoryLookup.FallbackProductName(chemicalId);
        }

        string RegistrationNo(int? id) =>
            id is { } chemicalId && products.TryGetValue(chemicalId, out var product) ? product.RegistrationNo : string.Empty;

        string UserName(int userId) => userId > 0 ? userNames.GetValueOrDefault(userId, $"#{userId}") : string.Empty;

        // The final key is the source order (all sources are read in a fixed order), so paging is deterministic.
        return events
            .Select((e, seq) => (Event: e with
            {
                ProductName = ProductName(e.ChemicalId),
                RegistrationNo = RegistrationNo(e.ChemicalId),
                UserName = UserName(e.UserId),
                PropertyName = propertyNames[e.PropertyId],
            }, Seq: seq))
            .OrderByDescending(x => x.Event.At).ThenByDescending(x => x.Event.Action).ThenByDescending(x => x.Event.PlacementId)
            .ThenBy(x => x.Seq)
            .Select(x => x.Event)
            .ToList();
    }

    private static ChemicalHistoryEventModel Setting(ChemicalPropertySettingsVersion v, string field, string oldValue, string newValue) => new()
    {
        At = v.UpdatedAt!.Value, UserId = v.UpdatedByUserId, Action = ChemicalHistoryActionEnum.SettingsChanged,
        Field = field, OldValue = oldValue, NewValue = newValue, PropertyId = v.PropertyId,
    };

    private static List<string> Flags(ChemicalWorkerPermissionVersion v) =>
        v is null
            ? []
            : new (bool Set, string Name)[]
                {
                    (v.View, "View"), (v.Register, "Register"), (v.Remove, "Remove"),
                    (v.Stock, "Stock"), (v.ManageLocations, "ManageLocations"), (v.Admin, "Admin"),
                }
                .Where(f => f.Set).Select(f => f.Name).ToList();

    private static IEnumerable<(T Previous, T Current)> Pairs<T>(IEnumerable<T> ordered)
    {
        var list = ordered.ToList();
        return list.Zip(list.Skip(1));
    }
}
