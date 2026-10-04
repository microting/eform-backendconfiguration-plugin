#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.AssignmentWorker;
using BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;

namespace BackendConfiguration.Pn.Infrastructure.Helpers;

/// <summary>
/// #1380: adds or removes worker tags (SDK <c>SiteTags</c>) on several workers in one
/// request. Writes the same rows the one-worker edit dialog writes
/// (<see cref="BackendConfigurationAssignmentWorkerServiceHelper.UpdateDeviceUser"/>):
/// a new <c>SiteTag</c> per added pair, a soft delete per removed one. It is
/// idempotent: a pair that already has a live row is not added again, and a pair
/// without one is not "removed".
/// </summary>
public static class WorkerTagsBulkUpdateHelper
{
    public const string SuccessKey = "WorkerTagsUpdated";
    public const string FailureKey = "WorkerTagsCouldNotBeUpdated";

    public static async Task<OperationResult> BulkUpdate(
        WorkerTagsBulkUpdateModel model,
        MicrotingDbContext sdkDbContext,
        ICalendarAssignmentReconciliationService? reconciliationService,
        ILogger logger)
    {
        var siteIds = (model.SiteIds ?? []).Distinct().ToList();
        var tagIds = (model.TagIds ?? []).Distinct().ToList();
        if (siteIds.Count == 0 || tagIds.Count == 0)
        {
            logger.LogWarning("[BulkUpdateWorkerTags] refused: {SiteCount} workers and {TagCount} tags selected",
                siteIds.Count, tagIds.Count);
            return new OperationResult(false, FailureKey);
        }

        if (model.Mode != WorkerTagsBulkMode.Add && model.Mode != WorkerTagsBulkMode.Remove)
        {
            logger.LogWarning("[BulkUpdateWorkerTags] refused: unknown mode {Mode}", model.Mode);
            return new OperationResult(false, FailureKey);
        }

        var committed = false;
        try
        {
            // A removed worker or tag in the request (a stale page) refuses the whole
            // request before anything is written, instead of applying it to the rest.
            var liveSiteCount = await sdkDbContext.Sites
                .Where(x => siteIds.Contains(x.Id) && x.WorkflowState != Constants.WorkflowStates.Removed)
                .CountAsync();
            var liveTagCount = await sdkDbContext.Tags
                .Where(x => tagIds.Contains(x.Id) && x.WorkflowState != Constants.WorkflowStates.Removed)
                .CountAsync();
            if (liveSiteCount != siteIds.Count || liveTagCount != tagIds.Count)
            {
                logger.LogWarning(
                    "[BulkUpdateWorkerTags] refused: {LiveSites}/{Sites} workers and {LiveTags}/{Tags} tags exist",
                    liveSiteCount, siteIds.Count, liveTagCount, tagIds.Count);
                return new OperationResult(false, FailureKey);
            }

            var changedTagIds = await ApplyInTransaction(sdkDbContext, siteIds, tagIds, model.Mode);
            committed = true;

            // After the commit and once for the whole batch: events assigned by team
            // pick up (or drop) the changed members.
            if (changedTagIds.Count > 0 && reconciliationService != null)
            {
                await reconciliationService.ReconcileEventsForWorkerTagsAsync(changedTagIds);
            }

            logger.LogInformation(
                "[BulkUpdateWorkerTags] {Mode} tags {TagIds} on {SiteCount} workers; changed tags {Changed}",
                model.Mode, tagIds, siteIds.Count, changedTagIds);
            return new OperationResult(true, SuccessKey);
        }
        catch (Exception ex)
        {
            // After the commit the tags ARE saved; only the event reconciliation failed.
            logger.LogError(ex,
                committed
                    ? "[BulkUpdateWorkerTags] {Mode}: tags saved, but reconciling team-assigned events failed"
                    : "[BulkUpdateWorkerTags] {Mode} failed; no tag was changed",
                model.Mode);
            return new OperationResult(false, FailureKey);
        }
    }

    /// <summary>
    /// Writes every add or remove in one transaction, so a failure part-way leaves
    /// no worker half-tagged. Run through the execution strategy because the SDK
    /// context retries on failure, which refuses a user-initiated transaction
    /// otherwise. Returns the tags that actually changed.
    /// </summary>
    private static async Task<HashSet<int>> ApplyInTransaction(
        MicrotingDbContext sdkDbContext, List<int> siteIds, List<int> tagIds, WorkerTagsBulkMode mode)
    {
        var strategy = sdkDbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            // Recomputed on every attempt: a retried attempt starts from the database again.
            var changedTagIds = new HashSet<int>();
            await using var tx = await sdkDbContext.Database.BeginTransactionAsync();

            var liveRows = await sdkDbContext.SiteTags
                .Where(x => x.SiteId != null && siteIds.Contains(x.SiteId.Value))
                .Where(x => x.TagId != null && tagIds.Contains(x.TagId.Value))
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .ToListAsync();

            if (mode == WorkerTagsBulkMode.Add)
            {
                var existing = liveRows.Select(x => (x.SiteId!.Value, x.TagId!.Value)).ToHashSet();
                foreach (var siteId in siteIds)
                {
                    foreach (var tagId in tagIds)
                    {
                        if (existing.Contains((siteId, tagId)))
                        {
                            continue;
                        }

                        await new SiteTag { SiteId = siteId, TagId = tagId }.Create(sdkDbContext);
                        changedTagIds.Add(tagId);
                    }
                }
            }
            else
            {
                foreach (var row in liveRows)
                {
                    await row.Delete(sdkDbContext);
                    changedTagIds.Add(row.TagId!.Value);
                }
            }

            await tx.CommitAsync();
            return changedTagIds;
        });
    }
}
