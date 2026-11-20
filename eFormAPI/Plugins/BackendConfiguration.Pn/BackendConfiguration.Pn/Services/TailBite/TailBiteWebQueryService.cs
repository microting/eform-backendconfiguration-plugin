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
    TimeProvider clock, ITailBiteOutbreakService outbreaks, ITailBitePhotoQuery photoQuery) : ITailBiteWebQueryService
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
        var names = await siteResolver.DisplayNamesAsync(rows.Select(r => r.WorkerId));
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
        var names = await siteResolver.DisplayNamesAsync(sites.Select(s => s.SiteId));
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
        await access.RequireEnabledManagerAsync(callerSiteId, propertyId);
        var rows = await TailBiteOutbreakRows.Of(db, [outbreakId])
            .OrderBy(x => x.Registration.EffectiveAt).ThenBy(x => x.Row.Id).ToListAsync();
        var registrationIds = rows.Select(x => x.Registration.Id).Distinct().ToList();
        var actionTypes = (await db.TailBiteRegistrationActions.AsNoTracking()
                .Where(a => registrationIds.Contains(a.RegistrationId) && a.WorkflowState != Removed)
                .Select(a => new { a.RegistrationId, a.ActionTypeId }).ToListAsync())
            .ToLookup(a => a.RegistrationId, a => a.ActionTypeId);
        // Names for every type the rows name, deleted ones included: the tree lists live types only.
        var actionTypeIds = actionTypes.SelectMany(g => g).Distinct().ToList();
        var actionTypeNames = await db.TailBiteActionTypes.AsNoTracking()
            .Where(a => a.PropertyId == propertyId && actionTypeIds.Contains(a.Id))
            .OrderBy(a => a.Id).Select(a => new ActionTypeName(a.Id, a.Name)).ToListAsync();
        var photos = await photoQuery.ForRegistrationsAsync(rows.Select(x => x.Registration).ToList());
        var photoCount = photos.GroupBy(p => p.RegistrationId).ToDictionary(g => g.Key, g => g.Count());
        var names = await siteResolver.DisplayNamesAsync(rows.Select(x => x.Registration.SiteId));
        return new OutbreakRegistrations(propertyId, rows.Select(x => new OutbreakRegistrationRow(
            x.Registration.Id, x.Row.Id, x.Row.LocationId, Utc(x.Registration.EffectiveAt), x.Row.MinorCount, x.Row.SevereCount,
            actionTypes[x.Registration.Id].Order().ToList(), x.Registration.SiteId, names[x.Registration.SiteId],
            x.Registration.CancelledAt != null, x.Registration.CancelReason,
            photoCount.GetValueOrDefault(x.Registration.Id))).ToList(), actionTypeNames, photos);
    }

    // "Not a worker on that property" reads exactly like "no such id" (or no such property).
    private async Task RequireWorkerOrNoAccessAsync(int callerSiteId, int propertyId)
    {
        try { await access.RequireWorkerAsync(callerSiteId, propertyId); }
        catch (TailBiteForbiddenException) { throw TailBiteForbiddenException.NoAccess(); }
    }
}
