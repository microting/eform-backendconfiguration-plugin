using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.ItemsPlanningBase.Infrastructure.Data;

namespace BackendConfiguration.Pn.Services.AreaRulePlanningTagPurgeService;

/// <summary>
/// Purges AreaRulePlanningTag rows that point at an items-planning PlanningTag
/// which no longer exists (hard-deleted) or has been soft-deleted.
///
/// Why this cannot be a foreign key or a single SQL statement:
/// AreaRulePlanningTag.ItemPlanningTagId is a bare int with no [ForeignKey] and no
/// navigation property, because the tag it names lives in the items-planning
/// DATABASE, not this one. ItemsPlanningTagsService.DeleteItemsPlanningTag soft-
/// deletes the PlanningTag and its PlanningsTags rows, but it has no
/// BackendConfigurationPnDbContext and therefore cannot touch this join at all —
/// so the join rows survive the delete still marked Created, pointing at a dead id.
/// Two databases also rules out an UPDATE ... JOIN: the live tag ids have to be
/// read from ItemsPlanningPnDbContext first and then applied here.
///
/// Deliberately NOT deduplicating: duplicate AreaRulePlanningTag rows for the same
/// (AreaRulePlanningId, ItemPlanningTagId) are a separate known defect in
/// BackendConfigurationTaskWizardService.UpdateTags. This purge only removes rows
/// whose tag is gone.
///
/// Lives in its own service rather than on BackendConfigurationTaskListService so
/// the plugin's startup hook can resolve it: the task-list service also depends on
/// IUserService (HttpContext-bound), IBackendConfigurationCalendarService and
/// IBackendConfigurationTaskWizardService, none of which are meaningful during
/// Configure(IApplicationBuilder), whereas this class needs exactly the two
/// DbContexts the work requires. Both callers — the startup hook via
/// <see cref="RunIfNeededAsync"/> and the task-list controller endpoint via
/// <see cref="PurgeOrphanedAreaRulePlanningTagsAsync"/> — run the same single
/// implementation; the startup entry point adds nothing but the run-once gate.
///
/// Registered as a bare concrete transient (no interface), matching the plugin's
/// two existing startup services (WorkorderCaseGroupIdBackfillService,
/// CalendarConfigurationBackfillService).
/// </summary>
public class AreaRulePlanningTagPurgeService(
    BackendConfigurationPnDbContext dbContext,
    ItemsPlanningPnDbContext itemsPlanningPnDbContext,
    ILogger<AreaRulePlanningTagPurgeService> logger)
{
    /// <summary>
    /// Marker recording that the one-time backlog purge has run, so startup never
    /// re-scans the join table on later boots.
    ///
    /// The prefix is the settings CLASS name, not the bound configuration section
    /// ("BackendConfigurationSettings"), matching
    /// CalendarConfigurationBackfillService.LegacyStartHourRepairMarkerName
    /// verbatim: the key lands in a section nothing reads, which is what keeps
    /// PluginConfigurationProvider from trying to bind it to a real property.
    /// Public so the integration fixture can clear it between tests.
    /// </summary>
    public const string BacklogPurgeMarkerName =
        "BackendConfigurationBaseSettings:OrphanedAreaRulePlanningTagsPurged";

    /// <summary>
    /// Startup entry point. Runs the purge once per database, then records a marker
    /// so every subsequent boot costs a single SELECT against
    /// PluginConfigurationValues (a table holding a handful of rows) instead of two
    /// scans of AreaRulePlanningTags. The endpoint keeps the ongoing case covered,
    /// so there is nothing for a later boot to find that the marker would hide.
    /// </summary>
    public async Task RunIfNeededAsync()
    {
        var alreadyPurged = await dbContext.PluginConfigurationValues
            .AnyAsync(x => x.Name == BacklogPurgeMarkerName);

        if (alreadyPurged)
        {
            return;
        }

        var purged = await PurgeOrphanedAreaRulePlanningTagsAsync();

        // Written even when nothing matched, so the scan is skipped from now on.
        //
        // Inserted as a single conditional statement rather than Add + SaveChanges,
        // for the reason spelled out in CalendarConfigurationBackfillService:
        // PluginConfigurationValues.Name is an unindexed longtext with no unique
        // constraint, so two instances starting together would both see no marker
        // and both insert one. BasePn's PluginConfigurationProvider.Load builds its
        // dictionary with ToDictionary(c => c.Name, ...), which throws on a
        // duplicate key — a second row would make the plugin fail to load on every
        // subsequent start, with nothing inside the plugin able to repair it.
        await dbContext.Database.ExecuteSqlRawAsync(
            @"INSERT INTO `PluginConfigurationValues`
                  (`Name`, `Value`, `CreatedAt`, `UpdatedAt`, `Version`,
                   `WorkflowState`, `CreatedByUserId`, `UpdatedByUserId`)
              SELECT {0}, 'true', {1}, {1}, 1, {2}, 1, 0 FROM DUAL
              WHERE NOT EXISTS (
                  SELECT 1 FROM `PluginConfigurationValues` `existing`
                  WHERE `existing`.`Name` = {0})",
            BacklogPurgeMarkerName,
            DateTime.UtcNow,
            Constants.WorkflowStates.Created);

        if (purged > 0)
        {
            logger.LogInformation(
                "AreaRulePlanningTagPurge: purged {Count} orphaned area-rule planning tags at startup",
                purged);
        }
    }

    /// <summary>
    /// Soft-deletes every non-removed AreaRulePlanningTag whose ItemPlanningTagId
    /// resolves to a removed PlanningTag, or to no PlanningTag row at all, and
    /// returns how many rows were purged.
    ///
    /// Ungated on purpose: the task-list page calls this straight after the
    /// Manage-tags dialog closes, so a tag deleted there takes effect immediately.
    /// Idempotent — a second call finds nothing, because already-removed rows are
    /// excluded by the WorkflowState filter.
    ///
    /// Soft delete, never hard delete: PnBase.Delete stamps WorkflowState = Removed,
    /// bumps Version and writes the *Versions audit row. Every read path in this
    /// plugin already filters AreaRulePlanningTags on
    /// WorkflowState != Removed, so a soft delete is as invisible to callers as a
    /// hard one while keeping the audit trail intact.
    /// </summary>
    public async Task<int> PurgeOrphanedAreaRulePlanningTagsAsync()
    {
        // Distinct tag ids on both sides first, so the set difference is computed
        // over two small lists instead of loading the whole join table into memory.
        // Only rows naming a genuinely orphaned tag are then materialized.
        var liveTagIds = await itemsPlanningPnDbContext.PlanningTags
            .AsNoTracking()
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .Select(x => x.Id)
            .ToListAsync();
        var liveTagIdSet = liveTagIds.ToHashSet();

        var referencedTagIds = await dbContext.AreaRulePlanningTags
            .AsNoTracking()
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .Select(x => x.ItemPlanningTagId)
            .Distinct()
            .ToListAsync();

        var orphanedTagIds = referencedTagIds
            .Where(id => !liveTagIdSet.Contains(id))
            .ToList();

        if (orphanedTagIds.Count == 0)
        {
            return 0;
        }

        // Tracking query (no AsNoTracking): PnBase.Delete only saves when the change
        // tracker sees the WorkflowState write, so these entities must be tracked.
        var orphanedRows = await dbContext.AreaRulePlanningTags
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .Where(x => orphanedTagIds.Contains(x.ItemPlanningTagId))
            .ToListAsync();

        var purged = 0;
        foreach (var row in orphanedRows)
        {
            try
            {
                await row.Delete(dbContext);
                purged++;
            }
            catch (Exception e)
            {
                // One bad row must not abort the whole pass — this runs inside the
                // synchronously blocking startup hook, where an escaping exception
                // would kill plugin startup.
                logger.LogError(e,
                    "AreaRulePlanningTagPurge: failed to purge AreaRulePlanningTag {AreaRulePlanningTagId}",
                    row.Id);
            }
        }

        return purged;
    }
}
