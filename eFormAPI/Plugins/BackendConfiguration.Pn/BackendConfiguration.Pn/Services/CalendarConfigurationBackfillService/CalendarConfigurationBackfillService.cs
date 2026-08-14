using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Data;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Enums;

namespace BackendConfiguration.Pn.Services.CalendarConfigurationBackfillService;

public class CalendarConfigurationBackfillService(
    BackendConfigurationPnDbContext dbContext,
    ItemsPlanningPnDbContext itemsPlanningPnDbContext,
    ILogger<CalendarConfigurationBackfillService> logger)
{
    public async Task RunIfNeededAsync()
    {
        // Wizard/calendar plannings lacking a board link (CalendarConfiguration row)
        var configuredArpIds = await dbContext.CalendarConfigurations
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .Select(x => x.AreaRulePlanningId)
            .ToListAsync();

        // Mirrors BackendConfigurationCalendarService.CreateTask's ARP-to-AreaRule
        // correlation: navigate via the AreaRule FK/nav property and filter on
        // CreatedInGuide (task-wizard-created rules only).
        var missing = await dbContext.AreaRulePlannings
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .Include(x => x.AreaRule)
            .Where(x => x.AreaRule.CreatedInGuide)
            .Where(x => !configuredArpIds.Contains(x.Id))
            .ToListAsync();

        if (missing.Count == 0)
        {
            return;
        }

        logger.LogInformation(
            "CalendarConfigurationBackfill: converting {Count} plannings to calendar tasks",
            missing.Count);

        // Old-frequency source of truth is the linked items-planning Planning row.
        // No WorkflowState filter on purpose: soft-deleted Plannings (inactive
        // tasks) render dimmed in the calendar and need normalization too.
        var planningIds = missing.Select(x => x.ItemPlanningId).Distinct().ToList();
        var plannings = await itemsPlanningPnDbContext.Plannings
            .Where(x => planningIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id);

        // Default board per property = lowest-Id non-removed board; create if none.
        // "Default"/"#c30000" mirrors BackendConfigurationCalendarService.GetBoards'
        // auto-create-default-board values verbatim.
        foreach (var propertyId in missing.Select(x => x.PropertyId).Distinct())
        {
            var board = await dbContext.CalendarBoards
                .Where(b => b.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(b => b.PropertyId == propertyId)
                .OrderBy(b => b.Id)
                .FirstOrDefaultAsync();

            if (board == null)
            {
                board = new CalendarBoard
                {
                    Name = "Default",
                    Color = "#c30000",
                    PropertyId = propertyId
                };
                await board.Create(dbContext);
            }

            foreach (var arp in missing.Where(x => x.PropertyId == propertyId))
            {
                try
                {
                    // ARP with a missing Planning row: skip normalization but still
                    // create the config row so the ARP is never re-scanned.
                    if (plannings.TryGetValue(arp.ItemPlanningId, out var planning))
                    {
                        await NormalizeRecurrence(arp, planning);
                    }

                    // No user context at startup — CreatedByUserId/UpdatedByUserId are
                    // left at their int default (0), same fallback the plugin's other
                    // startup backfill (WorkorderCaseGroupIdBackfillService) relies on.
                    // Created last: its existence is the idempotency marker for the
                    // whole conversion of this ARP, so a crash mid-run resumes here.
                    var configuration = new CalendarConfiguration
                    {
                        AreaRulePlanningId = arp.Id,
                        StartHour = 9.0,
                        Duration = 1.0,
                        BoardId = board.Id,
                        Color = null
                    };
                    await configuration.Create(dbContext);
                }
                catch (Exception e)
                {
                    logger.LogError(e,
                        "CalendarConfigurationBackfill: conversion failed for AreaRulePlanning {AreaRulePlanningId}",
                        arp.Id);
                }
            }
        }
    }

    // Normalizes recurrence to the calendar encoding. All ARP writes are
    // unconditional re-derivations from Planning.StartDate/RepeatType/RepeatEvery,
    // so any interrupted pass converges to the same final state on the next run.
    private async Task NormalizeRecurrence(AreaRulePlanning arp, Planning planning)
    {
        // "Altid" (RepeatType 0) and legacy (Day, 0) both become daily.
        if (planning.RepeatType == 0 || planning.RepeatEvery == 0)
        {
            arp.RepeatType = 1;
            arp.RepeatEvery = 1;
            await arp.Update(dbContext);

            planning.RepeatType = RepeatType.Day;
            planning.RepeatEvery = 1;
            await planning.Update(itemsPlanningPnDbContext);
            return;
        }

        var dow = (int)planning.StartDate.DayOfWeek;

        switch (planning.RepeatType)
        {
            case RepeatType.Day:
                arp.RepeatType = 1;
                arp.RepeatEvery = planning.RepeatEvery;
                await arp.Update(dbContext);
                break;
            case RepeatType.Week:
                arp.RepeatType = 2;
                arp.RepeatEvery = planning.RepeatEvery;
                arp.DayOfWeek = dow;
                arp.RepeatWeekdaysCsv = dow.ToString();
                await arp.Update(dbContext);
                break;
            case RepeatType.Month:
                arp.RepeatType = 3;
                arp.RepeatEvery = planning.RepeatEvery;
                arp.DayOfWeek = dow;
                arp.RepeatOrdinalWeek = 1;
                arp.DayOfMonth = 0;
                await arp.Update(dbContext);

                planning.RepeatOrdinalWeek = 1;
                await planning.Update(itemsPlanningPnDbContext);
                break;
            // Year/unknown: not wizard-producible — pass through untouched.
        }
    }
}
