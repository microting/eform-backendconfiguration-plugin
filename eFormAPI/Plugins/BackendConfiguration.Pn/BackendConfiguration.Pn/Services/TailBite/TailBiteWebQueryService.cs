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
using BackendConfiguration.Pn.Services.GrpcServices;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;

/// <summary>
/// Read-only queries for the tail-bite web admin (sub-project 3) that the foundation's services do not answer.
/// Properties and workers are plugin-admin reads (the routes that enable a property and set a manager are too);
/// everything else is checked against the property derived from the entity (spec §7.1), and an id that is
/// missing is refused exactly like a foreign one.
/// </summary>
public interface ITailBiteWebQueryService
{
    Task<IReadOnlyList<TailBitePropertyStatus>> ListPropertiesAsync();
    Task<IReadOnlyList<TailBitePropertyStatus>> ListCallerPropertiesAsync(int callerSiteId);
    Task<IReadOnlyList<TailBiteWorker>> ListWorkersAsync(int propertyId);
    Task<IReadOnlyList<TailBiteAssignableWorker>> ListAssignableWorkersAsync(int callerSiteId, int propertyId);
    Task<IReadOnlyList<RuleDto>> ListRulesAsync(int callerSiteId, int propertyId);
    Task<IReadOnlyList<RuleVersionDto>> RuleHistoryAsync(int callerSiteId, int ruleId);
    Task<IReadOnlyList<OccupancyDto>> CurrentOccupancyAsync(int callerSiteId, int propertyId);
    Task<OutbreakRegistrations> OutbreakRegistrationsAsync(int callerSiteId, int outbreakId);
}

public class TailBiteWebQueryService(BackendConfigurationPnDbContext db, ITailBiteAccess access, IGrpcSiteResolver siteResolver,
    TimeProvider clock, ITailBiteOutbreakService outbreaks) : ITailBiteWebQueryService
{
    private const string Removed = Constants.WorkflowStates.Removed;

    private static DateTime Utc(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc);
    private static DateTime? Utc(DateTime? d) => d is { } v ? Utc(v) : null;

    public async Task<IReadOnlyList<TailBitePropertyStatus>> ListPropertiesAsync()
    {
        var enabled = (await db.TailBiteProperties.AsNoTracking().Where(t => t.Enabled).Select(t => t.PropertyId).ToListAsync()).ToHashSet();
        var properties = await db.Properties.AsNoTracking().Where(p => p.WorkflowState != Removed)
            .OrderBy(p => p.Name).ThenBy(p => p.Id).Select(p => new { p.Id, p.Name }).ToListAsync();
        return properties.Select(p => new TailBitePropertyStatus(p.Id, p.Name ?? "", enabled.Contains(p.Id))).ToList();
    }

    // The properties the caller can open in the web admin: tail biting enabled and the caller an active worker there.
    public async Task<IReadOnlyList<TailBitePropertyStatus>> ListCallerPropertiesAsync(int callerSiteId)
    {
        var properties = await (from p in db.Properties.AsNoTracking()
                                join t in db.TailBiteProperties on p.Id equals t.PropertyId
                                where t.Enabled && p.WorkflowState != Removed
                                      && db.PropertyWorkers.Any(pw => pw.PropertyId == p.Id && pw.WorkerId == callerSiteId
                                                                      && pw.WorkflowState != Removed)
                                orderby p.Name, p.Id
                                select new { p.Id, p.Name }).ToListAsync();
        return properties.Select(p => new TailBitePropertyStatus(p.Id, p.Name ?? "", true)).ToList();
    }

    public async Task<IReadOnlyList<TailBiteWorker>> ListWorkersAsync(int propertyId)
    {
        var rows = await db.PropertyWorkers.AsNoTracking()
            .Where(pw => pw.PropertyId == propertyId && pw.WorkflowState != Removed)
            .Select(pw => new { pw.Id, pw.WorkerId, pw.TailBiteManager }).ToListAsync();
        var names = await DisplayNamesAsync(rows.Select(r => r.WorkerId));
        return rows.GroupBy(r => r.WorkerId)
            .Select(g => new TailBiteWorker(g.Key, names[g.Key], g.Any(r => r.TailBiteManager),
                g.Select(r => r.Id).Order().ToList()))
            .OrderBy(w => w.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(w => w.SiteId)
            .ToList();
    }

    // The outbreak page's workers, for a tail-bite manager of the property: the same workers and refusal as the app's outbreak
    // ListWorkerSiteIds, with names, plus the resigned ones flagged not assignable (so past responsibles keep their names);
    // no PropertyWorker ids or manager flags.
    public async Task<IReadOnlyList<TailBiteAssignableWorker>> ListAssignableWorkersAsync(int callerSiteId, int propertyId)
    {
        var sites = await outbreaks.ListWorkerSitesAsync(callerSiteId, propertyId);
        var names = await DisplayNamesAsync(sites.Select(s => s.SiteId));
        return sites.Select(s => new TailBiteAssignableWorker(s.SiteId, names[s.SiteId], !s.Resigned))
            .OrderBy(w => w.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(w => w.SiteId)
            .ToList();
    }

    public async Task<IReadOnlyList<RuleDto>> ListRulesAsync(int callerSiteId, int propertyId)
    {
        await RequireWorkerOrNoAccessAsync(callerSiteId, propertyId);
        var rules = await (from r in db.TailBiteRules.AsNoTracking()
                           join l in db.TailBiteLocations on r.LocationId equals l.Id
                           where l.PropertyId == propertyId && r.WorkflowState != Removed && l.WorkflowState != Removed
                           orderby r.Id
                           select r).ToListAsync();
        return rules.Select(r => new RuleDto(r.Id, r.LocationId, r.MinBittenPigs, r.MinSevere, r.WindowDays, r.CountDepth, r.Version,
            Utc(r.UpdatedAt))).ToList();
    }

    // A deleted rule is still readable: a closed outbreak names the rule version that opened it.
    public async Task<IReadOnlyList<RuleVersionDto>> RuleHistoryAsync(int callerSiteId, int ruleId)
    {
        var propertyId = await (from r in db.TailBiteRules
                                join l in db.TailBiteLocations on r.LocationId equals l.Id
                                where r.Id == ruleId
                                select (int?)l.PropertyId).SingleOrDefaultAsync()
                         ?? throw TailBiteForbiddenException.NoAccess();
        await RequireWorkerOrNoAccessAsync(callerSiteId, propertyId);
        // The delete of a rule writes a version row too (WorkflowState removed); it repeats the last settings and is left out.
        var versions = await db.TailBiteRuleVersions.AsNoTracking()
            .Where(v => v.TailBiteRuleId == ruleId && v.WorkflowState != Removed)
            .OrderByDescending(v => v.Version).ThenByDescending(v => v.Id).ToListAsync();
        return versions.Select(v => new RuleVersionDto(v.Version, v.LocationId, v.MinBittenPigs, v.MinSevere, v.WindowDays, v.CountDepth,
            Utc(v.UpdatedAt))).ToList();
    }

    // The count valid now per location: the newest ValidFrom that is not in the future.
    public async Task<IReadOnlyList<OccupancyDto>> CurrentOccupancyAsync(int callerSiteId, int propertyId)
    {
        await RequireWorkerOrNoAccessAsync(callerSiteId, propertyId);
        var now = clock.GetUtcNow().UtcDateTime;
        var rows = await (from o in db.TailBiteOccupancies.AsNoTracking()
                          join l in db.TailBiteLocations on o.LocationId equals l.Id
                          where l.PropertyId == propertyId && l.WorkflowState != Removed && o.WorkflowState != Removed && o.ValidFrom <= now
                          select o).ToListAsync();
        return rows.GroupBy(o => o.LocationId)
            .Select(g => g.OrderByDescending(o => o.ValidFrom).ThenByDescending(o => o.Id).First())
            .OrderBy(o => o.LocationId)
            .Select(o => new OccupancyDto(o.LocationId, o.PigCount, o.Source, Utc(o.ValidFrom)))
            .ToList();
    }

    public async Task<OutbreakRegistrations> OutbreakRegistrationsAsync(int callerSiteId, int outbreakId)
    {
        var propertyId = await db.TailBiteOutbreaks.AsNoTracking().Where(o => o.Id == outbreakId && o.WorkflowState != Removed)
                             .Select(o => (int?)o.PropertyId).SingleOrDefaultAsync()
                         ?? throw TailBiteForbiddenException.NoAccess();
        await access.RequireManagerAsync(callerSiteId, propertyId);
        var rows = await (from link in db.TailBiteOutbreakLinks.AsNoTracking()
                          join row in db.TailBiteRegistrationLocations on link.RegistrationLocationId equals row.Id
                          join reg in db.TailBiteRegistrations on row.RegistrationId equals reg.Id
                          where link.OutbreakId == outbreakId && link.WorkflowState != Removed && row.WorkflowState != Removed && reg.WorkflowState != Removed
                          orderby reg.EffectiveAt, row.Id
                          select new { row, reg }).ToListAsync();
        var registrationIds = rows.Select(x => x.reg.Id).Distinct().ToList();
        var actionTypes = (await db.TailBiteRegistrationActions.AsNoTracking()
                .Where(a => registrationIds.Contains(a.RegistrationId) && a.WorkflowState != Removed)
                .Select(a => new { a.RegistrationId, a.ActionTypeId }).ToListAsync())
            .ToLookup(a => a.RegistrationId, a => a.ActionTypeId);
        // Names for every type the rows name, deleted ones included: the tree lists live types only.
        var actionTypeIds = actionTypes.SelectMany(g => g).Distinct().ToList();
        var actionTypeNames = await db.TailBiteActionTypes.AsNoTracking()
            .Where(a => a.PropertyId == propertyId && actionTypeIds.Contains(a.Id))
            .OrderBy(a => a.Id).Select(a => new ActionTypeName(a.Id, a.Name)).ToListAsync();
        // BelongsTo builds a predicate for one registration, which does not fit a batch over many; this applies the same rule
        // in two steps: IsStored and the property in the query, uuid and uploading site in the lookup key. Removed photos are not counted.
        var clientUuids = rows.Select(x => x.reg.ClientUuid).Distinct().ToList();
        var photos = (await db.TailBiteRegistrationPhotos.AsNoTracking()
            .Where(p => p.PropertyId == propertyId && clientUuids.Contains(p.RegistrationClientUuid) && p.WorkflowState != Removed)
            .Where(TailBitePhotoOwnership.IsStored)
            .Select(p => new { p.RegistrationClientUuid, p.UploadedBySiteId }).ToListAsync())
            .ToLookup(p => (p.RegistrationClientUuid, p.UploadedBySiteId));
        var names = await DisplayNamesAsync(rows.Select(x => x.reg.SiteId));
        return new OutbreakRegistrations(propertyId, rows.Select(x => new OutbreakRegistrationRow(
            x.reg.Id, x.row.Id, x.row.LocationId, Utc(x.reg.EffectiveAt), x.row.MinorCount, x.row.SevereCount,
            actionTypes[x.reg.Id].Order().ToList(), x.reg.SiteId, names[x.reg.SiteId],
            x.reg.CancelledAt != null, x.reg.CancelReason,
            photos[(x.reg.ClientUuid, x.reg.SiteId)].Count())).ToList(), actionTypeNames);
    }

    // The name the app shows for a site (TailBiteGrpcService uses the same lookup), so web and app agree; "#id" when unknown.
    private async Task<IReadOnlyDictionary<int, string>> DisplayNamesAsync(IEnumerable<int> siteIds)
    {
        var names = new Dictionary<int, string>();
        foreach (var id in siteIds.Distinct())
        {
            var name = await siteResolver.GetDisplayNameAsync(id);
            names[id] = string.IsNullOrEmpty(name) ? $"#{id}" : name;
        }
        return names;
    }

    // "Not a worker on that property" reads exactly like "no such id" (or no such property).
    private async Task RequireWorkerOrNoAccessAsync(int callerSiteId, int propertyId)
    {
        try { await access.RequireWorkerAsync(callerSiteId, propertyId); }
        catch (TailBiteForbiddenException) { throw TailBiteForbiddenException.NoAccess(); }
    }
}
