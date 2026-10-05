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

#nullable enable

namespace BackendConfiguration.Pn.Services.TailBite;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;

/// <summary>
/// Tail-bite setup: enabling a property, the location tree, occupancy, action types, rules with a dry-run preview,
/// and the manager toggle. Edits are manager-only; reading the tree and the action types needs a worker.
/// </summary>
public interface ITailBiteSetupService
{
    /// <summary>Web admin only (no site check): creates the lock row, root, default rule and action types. Idempotent.</summary>
    Task EnableAsync(int callerSiteId, int propertyId);
    Task<IReadOnlyList<(int PropertyId, string Name, bool IsManager)>> ListEnabledPropertiesAsync(int siteId);
    Task<LocationTree> GetTreeAsync(int callerSiteId, int propertyId);
    Task<int> CreateLocationAsync(int callerSiteId, int parentId, string name);
    Task<IReadOnlyList<int>> CreatePenRangeAsync(int callerSiteId, int parentId, string prefix, int from, int to);
    Task RenameLocationAsync(int callerSiteId, int locationId, string name);
    Task MoveLocationAsync(int callerSiteId, int locationId, int newParentId);
    Task DeleteLocationAsync(int callerSiteId, int locationId);
    Task SetOccupancyAsync(int callerSiteId, int locationId, int pigCount, DateTime validFromUtc);
    Task<IReadOnlyList<ActionTypeDto>> ListActionTypesAsync(int callerSiteId, int propertyId);
    Task<int> CreateActionTypeAsync(int callerSiteId, int propertyId, string name);
    Task RenameActionTypeAsync(int callerSiteId, int actionTypeId, string name);
    /// <summary>Soft delete; registrations that used the type keep their link.</summary>
    Task DeleteActionTypeAsync(int callerSiteId, int actionTypeId);
    Task<int> CreateRuleAsync(int callerSiteId, RuleInput input);
    Task UpdateRuleAsync(int callerSiteId, int ruleId, RuleInput input);
    Task DeleteRuleAsync(int callerSiteId, int ruleId);
    /// <summary>Replays the last <paramref name="days"/> days with <paramref name="input"/> as the rule on its location; writes nothing.</summary>
    Task<RulePreview> PreviewRuleAsync(int callerSiteId, RuleInput input, int days = 90);
    /// <summary>Web admin only; authorized by the controller policy.</summary>
    Task SetManagerAsync(int propertyWorkerId, bool isManager);
}

/// <remarks>
/// Every write runs under the property lock, which clears the change tracker: entities are (re-)queried inside the
/// locked work, never carried in from before it.
/// </remarks>
public class TailBiteSetupService(BackendConfigurationPnDbContext db, ITailBitePropertyLock propertyLock, ITailBiteAccess access,
    ITailBiteSnapshotLoader loader, TimeProvider clock) : ITailBiteSetupService
{
    public const int MaxPenRange = 500;
    private const int MaxNameLength = 240; // leaves room for " {n}" in the 250-character Name column
    private const string Removed = Constants.WorkflowStates.Removed;

    private sealed record OpenOutbreakRef(int Node, int RuleId, int RuleLocationId);

    // ---------- enabling and listing ----------

    public async Task EnableAsync(int callerSiteId, int propertyId)
    {
        if (!await db.Properties.AnyAsync(p => p.Id == propertyId && p.WorkflowState != Removed))
            throw new TailBiteNotFoundException("Property not found.");
        // The lock row may not exist yet: create it outside the lock, tolerating a concurrent duplicate on the unique PropertyId.
        if (!await db.TailBiteProperties.AnyAsync(p => p.PropertyId == propertyId))
        {
            try { await new TailBiteProperty { PropertyId = propertyId, Enabled = false }.Create(db); }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear();
                if (!await db.TailBiteProperties.AnyAsync(p => p.PropertyId == propertyId)) throw; // not the duplicate-key race
            }
        }
        await propertyLock.RunLockedAsync(propertyId, async () =>
        {
            if (!await db.TailBiteLocations.AnyAsync(l => l.PropertyId == propertyId && l.ParentId == null))
            {
                var propertyName = await db.Properties.Where(p => p.Id == propertyId).Select(p => p.Name).SingleAsync();
                var root = new TailBiteLocation { PropertyId = propertyId, ParentId = null, Name = propertyName, QrCode = TailBiteDefaults.NewQrCode() };
                await root.Create(db);
                await TailBiteDefaults.DefaultRule(root.Id).Create(db);
            }
            // Removed defaults count as existing: the farm deleted them, and (PropertyId, Code) is unique.
            var existing = await db.TailBiteActionTypes.Where(a => a.PropertyId == propertyId).Select(a => a.Code).ToListAsync();
            var order = existing.Count;
            foreach (var (code, name) in TailBiteDefaults.ActionTypes.Where(d => !existing.Contains(d.Code)))
                await new TailBiteActionType { PropertyId = propertyId, Code = code, Name = name, SortOrder = order++ }.Create(db);
            var prop = await db.TailBiteProperties.SingleAsync(p => p.PropertyId == propertyId);
            if (!prop.Enabled)
            {
                prop.Enabled = true;
                prop.EnabledAt = clock.GetUtcNow().UtcDateTime;
                await prop.Update(db);
            }
        });
    }

    public async Task<IReadOnlyList<(int PropertyId, string Name, bool IsManager)>> ListEnabledPropertiesAsync(int siteId)
    {
        var rows = await (from pw in db.PropertyWorkers
                          join tp in db.TailBiteProperties on pw.PropertyId equals tp.PropertyId
                          join p in db.Properties on pw.PropertyId equals p.Id
                          where pw.WorkerId == siteId && pw.WorkflowState != Removed && tp.Enabled && p.WorkflowState != Removed
                          select new { pw.PropertyId, p.Name, pw.TailBiteManager }).ToListAsync();
        // A worker can have more than one PropertyWorker row for a property; manager if any row says so.
        return rows.GroupBy(r => r.PropertyId)
            .Select(g => (g.Key, g.First().Name, g.Any(r => r.TailBiteManager)))
            .OrderBy(x => x.Name).ToList();
    }

    // ---------- tree ----------

    public async Task<LocationTree> GetTreeAsync(int callerSiteId, int propertyId)
    {
        await access.RequireWorkerAsync(callerSiteId, propertyId);
        if (!await db.TailBiteProperties.AnyAsync(p => p.PropertyId == propertyId))
            throw new TailBiteNotFoundException($"Tail bite is not set up for property {propertyId}.");
        var locations = await db.TailBiteLocations.AsNoTracking().Where(l => l.PropertyId == propertyId).ToListAsync();
        var locIds = locations.Select(l => l.Id).ToList();
        var ruleStamps = await db.TailBiteRules.Where(r => locIds.Contains(r.LocationId)).Select(r => r.UpdatedAt).ToListAsync();
        var actionTypeStamps = await db.TailBiteActionTypes.Where(a => a.PropertyId == propertyId).Select(a => a.UpdatedAt).ToListAsync();
        var actionTypes = await OrderedLiveActionTypes(propertyId).Select(a => new ActionTypeDto(a.Id, a.Code, a.Name, a.SortOrder)).ToListAsync();

        var shape = new EvaluationSnapshot(locations.ToDictionary(l => l.Id, l => new EvalLocation(l.Id, l.ParentId)), [], [], []);
        var nodes = locations.OrderBy(l => l.SortOrder).ThenBy(l => l.Name)
            .Select(l => new LocationNode(l.Id, l.ParentId, l.Name, l.SortOrder, l.QrCode, OutbreakEvaluator.Depth(shape, l.Id), l.WorkflowState == Removed))
            .ToList();
        // Removed rows keep their UpdatedAt, so a delete moves the version too.
        var treeVersion = locations.Select(l => l.UpdatedAt).Concat(ruleStamps).Concat(actionTypeStamps)
            .Select(d => d?.Ticks ?? 0L).DefaultIfEmpty(0L).Max();
        return new LocationTree(propertyId, treeVersion, nodes, actionTypes);
    }

    public async Task<int> CreateLocationAsync(int callerSiteId, int parentId, string name)
    {
        var propertyId = await PropertyOfLocationAsync(parentId);
        var trimmed = ValidName(name);
        return await ManagerLockedAsync(callerSiteId, propertyId, async () =>
        {
            await LiveLocationAsync(parentId);
            await RequireFreeLocationNamesAsync(propertyId, parentId, [trimmed], exceptId: null);
            var loc = new TailBiteLocation { PropertyId = propertyId, ParentId = parentId, Name = trimmed,
                SortOrder = await NextSortOrderAsync(parentId), QrCode = TailBiteDefaults.NewQrCode() };
            await loc.Create(db);
            return loc.Id;
        });
    }

    public async Task<IReadOnlyList<int>> CreatePenRangeAsync(int callerSiteId, int parentId, string prefix, int from, int to)
    {
        var propertyId = await PropertyOfLocationAsync(parentId);
        var trimmedPrefix = ValidName(prefix);
        if (from > to) throw new TailBiteValidationException("The range must run from the lower number to the higher.");
        if ((long)to - from + 1 > MaxPenRange) throw new TailBiteValidationException($"At most {MaxPenRange} pens can be created at once.");
        var names = Enumerable.Range(from, to - from + 1).Select(n => $"{trimmedPrefix} {n}").ToList();
        return await ManagerLockedAsync<IReadOnlyList<int>>(callerSiteId, propertyId, async () =>
        {
            await LiveLocationAsync(parentId);
            await RequireFreeLocationNamesAsync(propertyId, parentId, names, exceptId: null);
            var order = await NextSortOrderAsync(parentId);
            var ids = new List<int>();
            foreach (var n in names)
            {
                var loc = new TailBiteLocation { PropertyId = propertyId, ParentId = parentId, Name = n, SortOrder = order++,
                    QrCode = TailBiteDefaults.NewQrCode() };
                await loc.Create(db);
                ids.Add(loc.Id);
            }
            return ids;
        });
    }

    public async Task RenameLocationAsync(int callerSiteId, int locationId, string name)
    {
        var propertyId = await PropertyOfLocationAsync(locationId);
        var trimmed = ValidName(name);
        await ManagerLockedAsync(callerSiteId, propertyId, async () =>
        {
            var loc = await LiveLocationAsync(locationId);
            await RequireFreeLocationNamesAsync(propertyId, loc.ParentId, [trimmed], exceptId: loc.Id);
            loc.Name = trimmed;
            await loc.Update(db);
        });
    }

    public async Task MoveLocationAsync(int callerSiteId, int locationId, int newParentId)
    {
        var propertyId = await PropertyOfLocationAsync(locationId);
        await ManagerLockedAsync(callerSiteId, propertyId, async () =>
        {
            var loc = await LiveLocationAsync(locationId);
            if (loc.ParentId is null) throw new TailBiteValidationException("The property's top location cannot be moved.");
            if (loc.ParentId == newParentId) return;
            if (!await db.TailBiteLocations.AnyAsync(l => l.Id == newParentId && l.PropertyId == propertyId && l.WorkflowState != Removed))
                throw new TailBiteValidationException("The new parent is not a location on this property.");
            var (snap, open) = await OpenContextAsync(propertyId);
            if (OutbreakEvaluator.IsDescendantOrSelf(snap, newParentId, locationId))
                throw new TailBiteValidationException("A location cannot be moved under itself.");
            // §5/§6.4: refused when moving an open node or an ancestor of one, moving out of an open subtree, or into one.
            if (HasOpenOutbreakAtOrUnder(snap, open, locationId)
                || open.Any(o => OutbreakEvaluator.IsDescendantOrSelf(snap, locationId, o.Node) || OutbreakEvaluator.IsDescendantOrSelf(snap, newParentId, o.Node)))
                throw new TailBiteConflictException("A location with an open outbreak, or inside one, cannot be moved. Close the outbreak first.");
            // §5: a rule never sums above its own node, so a move that deepens the subtree must leave every rule in it deep enough.
            var depthChange = OutbreakEvaluator.Depth(snap, newParentId) + 1 - OutbreakEvaluator.Depth(snap, locationId);
            if (depthChange > 0 && snap.Rules.Any(r => OutbreakEvaluator.IsDescendantOrSelf(snap, r.LocationId, locationId)
                    && r.CountDepth < OutbreakEvaluator.Depth(snap, r.LocationId) + depthChange))
                throw new TailBiteConflictException(
                    "A rule under this location would sum above its own location after the move. Raise the rule's count depth first.");
            await RequireFreeLocationNamesAsync(propertyId, newParentId, [loc.Name], exceptId: loc.Id);
            loc.ParentId = newParentId;
            loc.SortOrder = await NextSortOrderAsync(newParentId);
            await loc.Update(db);
        });
    }

    public async Task DeleteLocationAsync(int callerSiteId, int locationId)
    {
        var propertyId = await PropertyOfLocationAsync(locationId);
        await ManagerLockedAsync(callerSiteId, propertyId, async () =>
        {
            var loc = await db.TailBiteLocations.SingleAsync(l => l.Id == locationId);
            if (loc.WorkflowState == Removed) return; // deleted while we waited: idempotent
            var (snap, open) = await OpenContextAsync(propertyId);
            // §5: refused only when the location or a descendant IS an open outbreak's summing node.
            if (HasOpenOutbreakAtOrUnder(snap, open, locationId))
                throw new TailBiteConflictException("This location, or a location under it, has an open outbreak. Close the outbreak first.");
            if (loc.ParentId is null) throw new TailBiteValidationException("The property's top location cannot be deleted.");
            var subtree = snap.Locations.Keys.Where(id => OutbreakEvaluator.IsDescendantOrSelf(snap, id, locationId)).ToList();
            foreach (var l in await db.TailBiteLocations.Where(l => subtree.Contains(l.Id) && l.WorkflowState != Removed).ToListAsync())
                await l.Delete(db);
            foreach (var r in await db.TailBiteRules.Where(r => subtree.Contains(r.LocationId) && r.WorkflowState != Removed).ToListAsync())
                await r.Delete(db);
        });
    }

    public async Task SetOccupancyAsync(int callerSiteId, int locationId, int pigCount, DateTime validFromUtc)
    {
        var propertyId = await PropertyOfLocationAsync(locationId);
        if (pigCount < 0) throw new TailBiteValidationException("The pig count cannot be negative.");
        var validFrom = DateTime.SpecifyKind(validFromUtc, DateTimeKind.Utc);
        await ManagerLockedAsync(callerSiteId, propertyId, async () =>
        {
            await LiveLocationAsync(locationId);
            var existing = await db.TailBiteOccupancies.FirstOrDefaultAsync(o => o.LocationId == locationId && o.ValidFrom == validFrom
                                                                                && o.WorkflowState != Removed);
            if (existing is null)
            {
                await new TailBiteOccupancy { LocationId = locationId, PigCount = pigCount, Source = TailBiteOccupancySource.Manual, ValidFrom = validFrom }.Create(db);
                return;
            }
            existing.PigCount = pigCount;
            existing.Source = TailBiteOccupancySource.Manual;
            await existing.Update(db);
        });
    }

    // ---------- action types ----------

    public async Task<IReadOnlyList<ActionTypeDto>> ListActionTypesAsync(int callerSiteId, int propertyId)
    {
        await access.RequireWorkerAsync(callerSiteId, propertyId);
        return await OrderedLiveActionTypes(propertyId).Select(a => new ActionTypeDto(a.Id, a.Code, a.Name, a.SortOrder)).ToListAsync();
    }

    public async Task<int> CreateActionTypeAsync(int callerSiteId, int propertyId, string name)
    {
        var trimmed = ValidName(name);
        return await ManagerLockedAsync(callerSiteId, propertyId, async () =>
        {
            await RequireFreeActionTypeNameAsync(propertyId, trimmed, exceptId: null);
            var maxOrder = await LiveActionTypes(propertyId).MaxAsync(a => (int?)a.SortOrder);
            // (PropertyId, Code) is unique and the Id is not known before the insert: a one-off placeholder, replaced right after.
            var type = new TailBiteActionType { PropertyId = propertyId, Code = $"CUSTOM_{Guid.NewGuid():N}", Name = trimmed,
                SortOrder = (maxOrder ?? -1) + 1 };
            await type.Create(db);
            type.Code = $"CUSTOM_{type.Id}";
            await type.Update(db);
            return type.Id;
        });
    }

    public async Task RenameActionTypeAsync(int callerSiteId, int actionTypeId, string name)
    {
        var propertyId = await PropertyOfActionTypeAsync(actionTypeId);
        var trimmed = ValidName(name);
        await ManagerLockedAsync(callerSiteId, propertyId, async () =>
        {
            var type = await LiveActionTypeAsync(actionTypeId);
            await RequireFreeActionTypeNameAsync(propertyId, trimmed, exceptId: actionTypeId);
            type.Name = trimmed;
            await type.Update(db);
        });
    }

    public async Task DeleteActionTypeAsync(int callerSiteId, int actionTypeId)
    {
        var propertyId = await PropertyOfActionTypeAsync(actionTypeId);
        await ManagerLockedAsync(callerSiteId, propertyId, async () =>
        {
            var type = await db.TailBiteActionTypes.SingleAsync(a => a.Id == actionTypeId);
            if (type.WorkflowState == Removed) return; // deleted while we waited: idempotent
            await type.Delete(db);
        });
    }

    // ---------- rules ----------

    public async Task<int> CreateRuleAsync(int callerSiteId, RuleInput input)
    {
        var propertyId = await PropertyOfLocationAsync(input.LocationId);
        return await ManagerLockedAsync(callerSiteId, propertyId, async () =>
        {
            await LiveLocationAsync(input.LocationId);
            var (snap, open) = await OpenContextAsync(propertyId);
            ValidateRule(snap, input);
            if (snap.Rules.Any(r => r.LocationId == input.LocationId))
                throw new TailBiteConflictException("This location already has a rule; edit it instead.");
            // §6.4: a new rule below the open outbreak's rule, on the path to or inside its summing node, would re-route its rows.
            var reroutesOpen = open.Any(o => input.LocationId != o.RuleLocationId
                && OutbreakEvaluator.IsDescendantOrSelf(snap, input.LocationId, o.RuleLocationId)
                && (OutbreakEvaluator.IsDescendantOrSelf(snap, o.Node, input.LocationId) || OutbreakEvaluator.IsDescendantOrSelf(snap, input.LocationId, o.Node)));
            if (reroutesOpen) throw new TailBiteConflictException("A rule cannot be added inside an open outbreak's area. Close the outbreak first.");
            var rule = new TailBiteRule { LocationId = input.LocationId, MinBittenPigs = input.MinBittenPigs, MinSevere = input.MinSevere,
                WindowDays = input.WindowDays, CountDepth = input.CountDepth };
            await rule.Create(db);
            return rule.Id;
        });
    }

    public async Task UpdateRuleAsync(int callerSiteId, int ruleId, RuleInput input)
    {
        var propertyId = await PropertyOfRuleAsync(ruleId);
        await ManagerLockedAsync(callerSiteId, propertyId, async () =>
        {
            var rule = await LiveRuleAsync(ruleId);
            if (input.LocationId != rule.LocationId)
                throw new TailBiteValidationException("A rule cannot move to another location; delete it and create a new one there.");
            var (snap, open) = await OpenContextAsync(propertyId);
            ValidateRule(snap, input);
            if (input.CountDepth != rule.CountDepth && open.Any(o => o.RuleId == ruleId))
                throw new TailBiteConflictException("The summing level cannot change while this rule has an open outbreak. Close the outbreak first.");
            (rule.MinBittenPigs, rule.MinSevere, rule.WindowDays, rule.CountDepth) = (input.MinBittenPigs, input.MinSevere, input.WindowDays, input.CountDepth);
            await rule.Update(db); // bumps Version; open outbreaks keep the RuleVersion they opened under
        });
    }

    public async Task DeleteRuleAsync(int callerSiteId, int ruleId)
    {
        var propertyId = await PropertyOfRuleAsync(ruleId);
        await ManagerLockedAsync(callerSiteId, propertyId, async () =>
        {
            var rule = await db.TailBiteRules.SingleAsync(r => r.Id == ruleId);
            if (rule.WorkflowState == Removed) return; // deleted while we waited: idempotent
            var (_, open) = await OpenContextAsync(propertyId);
            if (open.Any(o => o.RuleId == ruleId))
                throw new TailBiteConflictException("A rule with an open outbreak cannot be deleted. Close the outbreak first.");
            await rule.Delete(db);
        });
    }

    public async Task<RulePreview> PreviewRuleAsync(int callerSiteId, RuleInput input, int days = 90)
    {
        var propertyId = await PropertyOfLocationAsync(input.LocationId);
        await access.RequireManagerAsync(callerSiteId, propertyId);
        if (days is < 1 or > 365) throw new TailBiteValidationException("The preview period must be 1–365 days.");
        var now = clock.GetUtcNow().UtcDateTime;
        var snap = await loader.LoadAsync(propertyId, now.AddDays(-days), now);
        ValidateRule(snap, input);
        return RulePreviewSimulator.Simulate(snap, input);
    }

    // ---------- managers ----------

    public async Task SetManagerAsync(int propertyWorkerId, bool isManager)
    {
        var propertyId = await db.PropertyWorkers.Where(x => x.Id == propertyWorkerId && x.WorkflowState != Removed)
                             .Select(x => (int?)x.PropertyId).SingleOrDefaultAsync()
                         ?? throw new TailBiteNotFoundException("Property worker not found.");
        await propertyLock.RunLockedAsync(propertyId, async () =>
        {
            if (!await db.TailBiteProperties.AnyAsync(p => p.PropertyId == propertyId && p.Enabled))
                throw new TailBiteNotFoundException($"Tail bite is not enabled for property {propertyId}.");
            var pw = await db.PropertyWorkers.SingleAsync(x => x.Id == propertyWorkerId);
            if (pw.WorkflowState == Removed) throw new TailBiteNotFoundException("Property worker not found.");
            if (pw.TailBiteManager == isManager) return;
            pw.TailBiteManager = isManager;
            await pw.Update(db);
        });
    }

    // ---------- helpers ----------

    private Task ManagerLockedAsync(int callerSiteId, int propertyId, Func<Task> work)
        => TailBiteManagerLock.RunAsync(propertyLock, access, callerSiteId, propertyId, work);

    private Task<T> ManagerLockedAsync<T>(int callerSiteId, int propertyId, Func<Task<T>> work)
        => TailBiteManagerLock.RunAsync(propertyLock, access, callerSiteId, propertyId, work);

    // Tree + rules + open outbreaks, each open outbreak with its rule and the rule's location (§5, §6.4).
    private async Task<(EvaluationSnapshot Snap, List<OpenOutbreakRef> Open)> OpenContextAsync(int propertyId)
    {
        var snap = await loader.LoadTreeAsync(propertyId);
        var open = await db.TailBiteOutbreaks
            .Where(o => o.PropertyId == propertyId && o.ClosedAt == null && o.WorkflowState != Removed)
            .Join(db.TailBiteRules, o => o.RuleId, r => r.Id, (o, r) => new { o.LocationId, o.RuleId, RuleLocationId = r.LocationId })
            .ToListAsync();
        return (snap, open.Select(x => new OpenOutbreakRef(x.LocationId, x.RuleId, x.RuleLocationId)).ToList());
    }

    // The location itself, or a location under it, is the summing node of an open outbreak.
    private static bool HasOpenOutbreakAtOrUnder(EvaluationSnapshot snap, IEnumerable<OpenOutbreakRef> open, int locationId)
        => open.Any(o => OutbreakEvaluator.IsDescendantOrSelf(snap, o.Node, locationId));

    private async Task<int> PropertyOfLocationAsync(int locationId)
        => await db.TailBiteLocations.Where(l => l.Id == locationId && l.WorkflowState != Removed)
               .Select(l => (int?)l.PropertyId).SingleOrDefaultAsync()
           ?? throw new TailBiteNotFoundException("Location not found.");

    private async Task<int> PropertyOfRuleAsync(int ruleId)
        => await (from r in db.TailBiteRules
                  join l in db.TailBiteLocations on r.LocationId equals l.Id
                  where r.Id == ruleId && r.WorkflowState != Removed
                  select (int?)l.PropertyId).SingleOrDefaultAsync()
           ?? throw new TailBiteNotFoundException("Rule not found.");

    private async Task<int> PropertyOfActionTypeAsync(int actionTypeId)
        => await db.TailBiteActionTypes.Where(a => a.Id == actionTypeId && a.WorkflowState != Removed)
               .Select(a => (int?)a.PropertyId).SingleOrDefaultAsync()
           ?? throw new TailBiteNotFoundException("Action type not found.");

    private async Task<TailBiteLocation> LiveLocationAsync(int locationId)
        => await db.TailBiteLocations.SingleOrDefaultAsync(l => l.Id == locationId && l.WorkflowState != Removed)
           ?? throw new TailBiteNotFoundException("Location not found.");

    private async Task<TailBiteRule> LiveRuleAsync(int ruleId)
        => await db.TailBiteRules.SingleOrDefaultAsync(r => r.Id == ruleId && r.WorkflowState != Removed)
           ?? throw new TailBiteNotFoundException("Rule not found.");

    private async Task<TailBiteActionType> LiveActionTypeAsync(int actionTypeId)
        => await db.TailBiteActionTypes.SingleOrDefaultAsync(a => a.Id == actionTypeId && a.WorkflowState != Removed)
           ?? throw new TailBiteNotFoundException("Action type not found.");

    private IQueryable<TailBiteActionType> LiveActionTypes(int propertyId)
        => db.TailBiteActionTypes.Where(a => a.PropertyId == propertyId && a.WorkflowState != Removed);

    private IQueryable<TailBiteActionType> OrderedLiveActionTypes(int propertyId)
        => LiveActionTypes(propertyId).AsNoTracking().OrderBy(a => a.SortOrder).ThenBy(a => a.Id);

    private async Task RequireFreeLocationNamesAsync(int propertyId, int? parentId, IReadOnlyCollection<string> names, int? exceptId)
    {
        var taken = await db.TailBiteLocations
            .Where(l => l.PropertyId == propertyId && l.ParentId == parentId && l.WorkflowState != Removed && names.Contains(l.Name)
                        && (exceptId == null || l.Id != exceptId))
            .Select(l => l.Name).ToListAsync();
        if (taken.Count > 0) throw new TailBiteValidationException($"Already exists here: {string.Join(", ", taken.Order())}.");
    }

    private async Task RequireFreeActionTypeNameAsync(int propertyId, string name, int? exceptId)
    {
        if (await LiveActionTypes(propertyId).AnyAsync(a => a.Name == name && (exceptId == null || a.Id != exceptId)))
            throw new TailBiteValidationException($"The action '{name}' already exists.");
    }

    private async Task<int> NextSortOrderAsync(int parentId)
        => (await db.TailBiteLocations.Where(l => l.ParentId == parentId && l.WorkflowState != Removed).MaxAsync(l => (int?)l.SortOrder) ?? -1) + 1;

    private static string ValidName(string? name)
    {
        var trimmed = name?.Trim() ?? "";
        if (trimmed.Length is 0 or > MaxNameLength) throw new TailBiteValidationException($"A name must be 1–{MaxNameLength} characters.");
        return trimmed;
    }

    private static void ValidateRule(EvaluationSnapshot snap, RuleInput input)
    {
        if (input.MinBittenPigs is null && input.MinSevere is null) throw new TailBiteValidationException("Set at least one threshold.");
        if (input.MinBittenPigs is < 1 || input.MinSevere is < 1) throw new TailBiteValidationException("Thresholds must be at least 1.");
        if (input.WindowDays is < 1 or > 90) throw new TailBiteValidationException("The window must be 1–90 days.");
        if (input.CountDepth < OutbreakEvaluator.Depth(snap, input.LocationId)) throw new TailBiteValidationException("A rule cannot sum above its own location.");
    }
}
