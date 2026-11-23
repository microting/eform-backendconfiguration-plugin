#nullable enable
using System.Collections.Generic;
using BackendConfiguration.Pn.Hubs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microting.eForm.Infrastructure.Constants;
using BcAreaRulePlanning = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.AreaRulePlanning;
using BcAreaRulePlanningWorkerTag = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.AreaRulePlanningWorkerTag;
using BcAreaRulePlanningFile = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.AreaRulePlanningFile;
using BcAreaRulePlanningTag = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.AreaRulePlanningTag;
using BcCalendarBoard = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.CalendarBoard;
using BcCalendarConfiguration = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.CalendarConfiguration;
using BcCalendarOccurrenceException = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.CalendarOccurrenceException;
using BcCompliance = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.Compliance;
using BcPlanningSite = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.PlanningSite;
using BcPropertyWorker = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.PropertyWorker;
using IpPlanning = Microting.ItemsPlanningBase.Infrastructure.Data.Entities.Planning;
using IpPlanningCase = Microting.ItemsPlanningBase.Infrastructure.Data.Entities.PlanningCase;
using IpPlanningCaseSite = Microting.ItemsPlanningBase.Infrastructure.Data.Entities.PlanningCaseSite;
using IpPlanningNameTranslation = Microting.ItemsPlanningBase.Infrastructure.Data.Entities.PlanningNameTranslation;
using IpPlanningSite = Microting.ItemsPlanningBase.Infrastructure.Data.Entities.PlanningSite;

namespace BackendConfiguration.Pn.Services.LiveChanges;

/// <summary>A watched row as SaveChanges began: the entity (its generated id is read after the save) and its prior state.</summary>
internal readonly record struct CapturedEntry(object Entity, EntityState State, string? OriginalWorkflowState);

/// <summary>
/// The rows the mobile task list is built from, and how each becomes a
/// <see cref="LiveChange"/>. In-memory only: what is on the row, nothing
/// looked up. Everything else (versions, translations of other things, files,
/// boards, ...) is ignored.
/// </summary>
internal static class LiveChangeCapture
{
    private const string Removed = Constants.WorkflowStates.Removed;

    private static bool IsWatched(object entity) => entity is BcCompliance or BcAreaRulePlanning or BcPlanningSite
        or BcPropertyWorker or BcCalendarOccurrenceException or BcAreaRulePlanningWorkerTag
        or BcCalendarConfiguration or BcAreaRulePlanningFile or BcAreaRulePlanningTag or BcCalendarBoard
        or IpPlanning or IpPlanningSite or IpPlanningCase or IpPlanningCaseSite or IpPlanningNameTranslation;

    /// <summary>
    /// The tracked states as they are, WITHOUT running change detection:
    /// called right after EF's own detection pass of the save (or, with
    /// AutoDetectChanges off, when EF runs none). <c>Entries()</c> detects
    /// again when AutoDetectChanges is on, so it is switched off around the read.
    /// </summary>
    public static List<CapturedEntry> Snapshot(ChangeTracker tracker, bool propertyWorkersOnly)
    {
        var autoDetect = tracker.AutoDetectChangesEnabled;
        tracker.AutoDetectChangesEnabled = false;
        try
        {
            return Read(tracker, propertyWorkersOnly);
        }
        finally
        {
            tracker.AutoDetectChangesEnabled = autoDetect;
        }
    }

    private static List<CapturedEntry> Read(ChangeTracker tracker, bool propertyWorkersOnly)
    {
        var captured = new List<CapturedEntry>();
        foreach (var entry in tracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;
            if (propertyWorkersOnly ? entry.Entity is not BcPropertyWorker : !IsWatched(entry.Entity)) continue;
            string? original = null;
            if (entry.State != EntityState.Added && entry.Metadata.FindProperty("WorkflowState") is { } workflowState)
                original = entry.OriginalValues[workflowState] as string;
            captured.Add(new CapturedEntry(entry.Entity, entry.State, original));
        }

        return captured;
    }

    /// <summary>Called after the save: generated ids are known now.</summary>
    public static List<LiveChange> ToChanges(IReadOnlyList<CapturedEntry> captured)
    {
        var changes = new List<LiveChange>();
        foreach (var entry in captured)
        {
            var current = entry.State == EntityState.Deleted ? Removed : WorkflowStateOf(entry.Entity);
            // A row that was removed and stays removed (a second Delete, a version bump) changes nothing a client sees.
            if (entry.OriginalWorkflowState == Removed && current == Removed) continue;
            var removed = current == Removed;
            var kind = removed ? TaskChangeKinds.Removed : TaskChangeKinds.Changed;

            switch (entry.Entity)
            {
                case BcCompliance x:
                    changes.Add(LiveChange.ForPlanning(x.PlanningId, x.PropertyId, x.Id, kind));
                    break;
                case BcAreaRulePlanning x:
                    changes.Add(LiveChange.ForAreaRulePlanning(x.Id, x.ItemPlanningId, x.PropertyId, kind));
                    break;
                case BcPlanningSite x:
                    changes.Add(LiveChange.ForEvent(x.AreaRulePlanningsId, kind: kind));
                    break;
                case BcCalendarOccurrenceException x:
                    changes.Add(LiveChange.ForEvent(x.AreaRulePlanningId));
                    break;
                case BcAreaRulePlanningWorkerTag x:
                    changes.Add(LiveChange.ForEvent(x.AreaRulePlanningId));
                    break;
                // The task's time and board (ResizeTask scope "all" writes only this), its
                // attachments and tags: the task itself stays, so even a removal is a change.
                case BcCalendarConfiguration x:
                    changes.Add(LiveChange.ForEvent(x.AreaRulePlanningId));
                    break;
                case BcAreaRulePlanningFile x:
                    changes.Add(LiveChange.ForEvent(x.AreaRulePlanningId));
                    break;
                case BcAreaRulePlanningTag x:
                    changes.Add(LiveChange.ForEvent(x.AreaRulePlanningId));
                    break;
                // A board's name or colour shows on every task of the property placed on it.
                // A NEW board is on no task yet; GetBoards creates the default one lazily on
                // read paths (ListBoards, ListChanges), which must not announce.
                case BcCalendarBoard x when entry.State != EntityState.Added:
                    changes.Add(LiveChange.ForTask(x.PropertyId));
                    break;
                case BcPropertyWorker x:
                    // Only the assignment itself (added, removed, restored), not edits of its other columns.
                    if (entry.State != EntityState.Added && entry.OriginalWorkflowState == current) break;
                    if (removed) changes.Add(LiveChange.ForRevokeProperty(x.WorkerId, x.PropertyId));
                    changes.Add(LiveChange.ForAssignments(x.WorkerId));
                    break;
                case IpPlanning x:
                    changes.Add(LiveChange.ForPlanning(x.Id, kind: kind));
                    break;
                case IpPlanningSite x:
                    changes.Add(LiveChange.ForPlanning(x.PlanningId, kind: kind));
                    break;
                case IpPlanningCase x:
                    changes.Add(LiveChange.ForPlanning(x.PlanningId));
                    break;
                case IpPlanningCaseSite x:
                    changes.Add(LiveChange.ForPlanning(x.PlanningId));
                    break;
                case IpPlanningNameTranslation x:
                    changes.Add(LiveChange.ForPlanning(x.PlanningId));
                    break;
            }
        }

        return changes;
    }

    private static string? WorkflowStateOf(object entity) => entity switch
    {
        Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.PnBase x => x.WorkflowState,
        Microting.ItemsPlanningBase.Infrastructure.Data.Entities.PnBase x => x.WorkflowState,
        _ => null
    };
}
