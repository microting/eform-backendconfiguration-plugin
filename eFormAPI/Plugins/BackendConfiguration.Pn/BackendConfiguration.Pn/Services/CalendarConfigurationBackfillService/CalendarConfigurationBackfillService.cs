using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;

namespace BackendConfiguration.Pn.Services.CalendarConfigurationBackfillService;

public class CalendarConfigurationBackfillService(
    BackendConfigurationPnDbContext dbContext,
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
            .Select(x => new { x.Id, x.PropertyId })
            .ToListAsync();

        if (missing.Count == 0)
        {
            return;
        }

        logger.LogInformation(
            "CalendarConfigurationBackfill: attaching {Count} plannings to default boards",
            missing.Count);

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
                // No user context at startup — CreatedByUserId/UpdatedByUserId are
                // left at their int default (0), same fallback the plugin's other
                // startup backfill (WorkorderCaseGroupIdBackfillService) relies on.
                var configuration = new CalendarConfiguration
                {
                    AreaRulePlanningId = arp.Id,
                    StartHour = 0,
                    Duration = 1,
                    BoardId = board.Id,
                    Color = null
                };
                await configuration.Create(dbContext);
            }
        }
    }
}
