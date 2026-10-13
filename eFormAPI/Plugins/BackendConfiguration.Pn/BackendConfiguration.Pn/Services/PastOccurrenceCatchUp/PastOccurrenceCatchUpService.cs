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

namespace BackendConfiguration.Pn.Services.PastOccurrenceCatchUp;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.CalendarPastSeriesBackfill;
using BackendConfiguration.Pn.Services.EventDeployService;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Dto;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Data;

#nullable enable

/// <summary>
/// Materialises missed backend-configuration occurrences on skipCloudDeploy
/// tenants since 2026-09-17. Orchestrates the read-only planner and the
/// event deploy service to create Compliance rows for every past occurrence
/// of every eligible series.
/// </summary>
public class PastOccurrenceCatchUpService(
    BackendConfigurationPnDbContext backendConfigurationPnDbContext,
    ItemsPlanningPnDbContext itemsPlanningPnDbContext,
    IEFormCoreService coreHelper,
    ICalendarPastSeriesBackfillService pastSeriesPlanner,
    IEventDeployService eventDeployService,
    ILogger<PastOccurrenceCatchUpService> logger) : IPastOccurrenceCatchUpService
{
    /// <summary>
    /// The day items-planning #1520 started leaving backend-configuration
    /// plannings to us when skipCloudDeploy is on; earlier gaps are a
    /// separate problem.
    /// </summary>
    public static readonly DateTime SkipCloudDeployGapStart = new(2026, 9, 17, 0, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan Throttle = TimeSpan.FromMinutes(10);
    private static readonly ConcurrentDictionary<int, DateTime> LastRunUtc = new();
    // Properties a catch-up is processing right now, so concurrent list reads for
    // the same property do not each run the whole pass.
    private static readonly ConcurrentDictionary<int, byte> InFlight = new();

    public async Task<PastOccurrenceCatchUpResult> CatchUpAsync(
        IReadOnlyCollection<int>? propertyIds, bool bypassThrottle, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var today = now.Date;

        // Cheapest exit first: a list read for properties caught up within the throttle
        // window costs no SDK or database round trip at all.
        if (!bypassThrottle && propertyIds is { Count: > 0 } && propertyIds.All(id => IsThrottled(id, now)))
            return new PastOccurrenceCatchUpResult(0, 0, 0, 0);

        if (!await IsSkipCloudDeployOnAsync().ConfigureAwait(false))
            return new PastOccurrenceCatchUpResult(0, 0, 0, 0);

        var arpQuery = backendConfigurationPnDbContext.AreaRulePlannings
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed
                        && x.Status && x.ComplianceEnabled && x.ItemPlanningId > 0);
        if (propertyIds is { Count: > 0 })
            arpQuery = arpQuery.Where(x => propertyIds.Contains(x.PropertyId));
        var arps = await arpQuery.ToListAsync(ct).ConfigureAwait(false);

        var planningIds = arps.Select(x => x.ItemPlanningId).Distinct().ToList();
        var ownedPlanningIds = (await itemsPlanningPnDbContext.Plannings.AsNoTracking()
                .Where(p => planningIds.Contains(p.Id)
                            && p.WorkflowState != Constants.WorkflowStates.Removed
                            && p.Enabled && p.IsLocked && !p.IsEditable)
                .Select(p => p.Id).ToListAsync(ct).ConfigureAwait(false))
            .ToHashSet();

        int created = 0, present = 0, failed = 0, properties = 0;
        foreach (var group in arps.Where(a => ownedPlanningIds.Contains(a.ItemPlanningId))
                     .GroupBy(a => a.PropertyId))
        {
            if (!bypassThrottle && IsThrottled(group.Key, now))
                continue;
            if (!InFlight.TryAdd(group.Key, 0))
                continue;
            try
            {
                properties++;

                foreach (var arp in group)
                {
                    ct.ThrowIfCancellationRequested();
                    PastSeriesBackfillPlan plan;
                    try
                    {
                        plan = await pastSeriesPlanner.PlanPastSeriesBackfillAsync(arp, null, ct).ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        failed++;
                        logger.LogError(e, "Past-occurrence catch-up could not plan AreaRulePlanning {ArpId}", arp.Id);
                        continue;
                    }
                    if (!plan.ComplianceEnabled) continue;

                    var skippedDays = await DaysToLeaveAloneAsync(arp, ct).ConfigureAwait(false);
                    foreach (var occurrence in plan.PastOccurrences
                                 .Where(d => d.Date >= SkipCloudDeployGapStart && d.Date < today
                                             && !skippedDays.Contains(d.Date)))
                    foreach (var sdkSiteId in plan.SiteIds)
                    {
                        try
                        {
                            var ensured = await eventDeployService
                                .EnsureComplianceForOccurrenceAsync(arp, occurrence, sdkSiteId, ct)
                                .ConfigureAwait(false);
                            if (ensured == null) failed++;
                            else if (ensured.Created) created++;
                            else present++;
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception e)
                        {
                            failed++;
                            logger.LogError(e,
                                "Past-occurrence catch-up failed for AreaRulePlanning {ArpId}, occurrence {Occurrence}, site {SiteId}",
                                arp.Id, occurrence.ToString("yyyy-MM-dd"), sdkSiteId);
                        }
                    }
                }

                // Marked only once the property's series have all been processed, so a
                // cancelled run does not leave a property looking caught up.
                LastRunUtc[group.Key] = now;
            }
            finally
            {
                InFlight.TryRemove(group.Key, out _);
            }
        }

        if (created + failed > 0)
            logger.LogInformation(
                "Past-occurrence catch-up: {Created} created, {Present} already present, {Failed} failed over {Properties} propert(y/ies)",
                created, present, failed, properties);
        return new PastOccurrenceCatchUpResult(created, present, failed, properties);
    }

    /// <summary>
    /// Days since the gap start this pass must not deploy for the series:
    /// <list type="bullet">
    ///   <item><description>any Compliance row for the day, whatever its WorkflowState. A removed
    ///   row is a completed or user-deleted occurrence, and IX_PlanningId_Deadline holds one row
    ///   per occurrence, so a redeploy would create an SDK case that either revives the deleted
    ///   task or is orphaned next to the completed one — on every run.</description></item>
    ///   <item><description>a live deleted-occurrence exception (calendar delete, scope "this").</description></item>
    /// </list>
    /// Read once per series, before any site is deployed, so the first site's new row does not
    /// hide the occurrence from the remaining sites.
    /// </summary>
    private async Task<HashSet<DateTime>> DaysToLeaveAloneAsync(
        AreaRulePlanning arp,
        CancellationToken ct)
    {
        var compliances = await backendConfigurationPnDbContext.Compliances.AsNoTracking()
            .Where(c => c.PlanningId == arp.ItemPlanningId && c.Deadline >= SkipCloudDeployGapStart)
            .Select(c => c.Deadline)
            .ToListAsync(ct).ConfigureAwait(false);
        var deleted = await backendConfigurationPnDbContext.CalendarOccurrenceExceptions.AsNoTracking()
            .Where(x => x.AreaRulePlanningId == arp.Id
                        && x.IsDeleted
                        && x.WorkflowState != Constants.WorkflowStates.Removed
                        && x.OriginalDate >= SkipCloudDeployGapStart)
            .Select(x => x.OriginalDate)
            .ToListAsync(ct).ConfigureAwait(false);
        return compliances.Concat(deleted).Select(d => d.Date).ToHashSet();
    }

    private static bool IsThrottled(int propertyId, DateTime now) =>
        LastRunUtc.TryGetValue(propertyId, out var last) && now - last < Throttle;

    private async Task<bool> IsSkipCloudDeployOnAsync()
    {
        try
        {
            var core = await coreHelper.GetCore().ConfigureAwait(false);
            return await core.GetSdkSetting(Settings.skipCloudDeploy).ConfigureAwait(false) == "true";
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Past-occurrence catch-up could not read skipCloudDeploy; assuming off");
            return false;
        }
    }

    /// <summary>
    /// Test-only helper to clear the throttle between fixtures so each test
    /// has an independent state.
    /// </summary>
    internal static void ResetThrottleForTests() => LastRunUtc.Clear();
}
