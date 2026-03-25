using Sentry;

namespace BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BackendConfigurationLocalizationService;
using BackendConfigurationTaskWizardService;
using Infrastructure.Models.Calendar;
using Infrastructure.Models.TaskWizard;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Helpers;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Data;

public class BackendConfigurationCalendarService(
    IBackendConfigurationLocalizationService localizationService,
    IUserService userService,
    BackendConfigurationPnDbContext backendConfigurationPnDbContext,
    IEFormCoreService coreHelper,
    ItemsPlanningPnDbContext itemsPlanningPnDbContext,
    IBackendConfigurationTaskWizardService taskWizardService,
    ILogger<BackendConfigurationCalendarService> logger)
    : IBackendConfigurationCalendarService
{
    public async Task<OperationDataResult<List<CalendarTaskResponseModel>>> GetTasksForWeek(
        CalendarTaskRequestModel requestModel)
    {
        try
        {
            var weekStart = DateTime.Parse(requestModel.WeekStart, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            var weekEnd = DateTime.Parse(requestModel.WeekEnd, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

            var userLanguageId = (await userService.GetCurrentUserLanguage()).Id;
            var result = new List<CalendarTaskResponseModel>();

            // Get the default board for this property (first created board)
            var defaultBoard = await backendConfigurationPnDbContext.CalendarBoards
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.PropertyId == requestModel.PropertyId)
                .OrderBy(x => x.Id)
                .FirstOrDefaultAsync();
            var defaultBoardId = defaultBoard?.Id;

            // Pre-load compliance dates to avoid duplicates between occurrence expansion and compliances
            var compliancesInWeek = await backendConfigurationPnDbContext.Compliances
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.PropertyId == requestModel.PropertyId)
                .Where(x => x.Deadline >= weekStart && x.Deadline <= weekEnd)
                .ToListAsync();
            // Build sets for dedup: by exact date and by planningId (any compliance in week)
            var complianceDateSet = new HashSet<string>(
                compliancesInWeek.Select(c => $"{c.PlanningId}:{c.Deadline:yyyy-MM-dd}"));
            var compliancePlanningIdsInWeek = new HashSet<int>(
                compliancesInWeek.Select(c => c.PlanningId));

            // 1. Query AreaRulePlannings (future/active tasks)
            var areaRulePlannings = await backendConfigurationPnDbContext.AreaRulePlannings
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.Status)
                .Where(x => x.PropertyId == requestModel.PropertyId)
                .Include(x => x.AreaRule)
                    .ThenInclude(x => x.AreaRuleTranslations)
                .Include(x => x.PlanningSites)
                .Include(x => x.AreaRulePlanningTags)
                .ToListAsync();

            // Batch-load plannings to avoid N+1 queries
            var planningIds = areaRulePlannings.Select(x => x.ItemPlanningId).Distinct().ToList();
            var planningsDict = await itemsPlanningPnDbContext.Plannings
                .Where(x => planningIds.Contains(x.Id))
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .ToDictionaryAsync(x => x.Id);

            // Batch-load calendar configurations
            var arpIds = areaRulePlannings.Select(x => x.Id).ToList();
            var calConfigsDict = await backendConfigurationPnDbContext.CalendarConfigurations
                .Where(x => arpIds.Contains(x.AreaRulePlanningId))
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .ToDictionaryAsync(x => x.AreaRulePlanningId);

            // Batch-load tags for all ARPs
            var allArpTags = await backendConfigurationPnDbContext.AreaRulePlanningTags
                .Where(x => arpIds.Contains(x.AreaRulePlanningId))
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .ToListAsync();

            var tagItemIds = allArpTags.Select(x => x.ItemPlanningTagId).Distinct().ToList();
            var planningTagNames = await itemsPlanningPnDbContext.PlanningTags
                .Where(x => tagItemIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.Name);

            foreach (var arp in areaRulePlannings)
            {
                if (!planningsDict.TryGetValue(arp.ItemPlanningId, out var planning))
                    continue;

                // Compute all occurrence dates within the requested week
                var occurrences = GetOccurrencesInWeek(planning, weekStart, weekEnd);

                if (occurrences.Count == 0)
                    continue;

                calConfigsDict.TryGetValue(arp.Id, out var calConfig);
                var isAllDay = calConfig == null;

                var title = arp.AreaRule?.AreaRuleTranslations?
                    .Where(t => t.LanguageId == userLanguageId)
                    .Select(t => t.Name)
                    .FirstOrDefault() ?? arp.AreaRule?.AreaRuleTranslations?.FirstOrDefault()?.Name ?? "";

                var tags = allArpTags
                    .Where(x => x.AreaRulePlanningId == arp.Id)
                    .Select(x => planningTagNames.TryGetValue(x.ItemPlanningTagId, out var name) ? name : null)
                    .Where(x => x != null)
                    .ToList();

                var assigneeIds = arp.PlanningSites?
                    .Where(ps => ps.WorkflowState != Constants.WorkflowStates.Removed)
                    .Select(ps => (int)ps.SiteId)
                    .ToList() ?? [];

                foreach (var occurrenceDate in occurrences)
                {
                    // Skip if this planning already has a compliance in the week (avoid duplicates)
                    if (compliancePlanningIdsInWeek.Contains(arp.ItemPlanningId))
                        continue;

                    var model = new CalendarTaskResponseModel
                    {
                        Id = arp.Id,
                        Title = title,
                        StartHour = isAllDay ? 0 : calConfig?.StartHour ?? 9.0,
                        Duration = isAllDay ? 0 : calConfig?.Duration ?? 1.0,
                        TaskDate = occurrenceDate.ToString("yyyy-MM-dd"),
                        Tags = tags,
                        AssigneeIds = assigneeIds,
                        BoardId = calConfig?.BoardId ?? defaultBoardId,
                        Color = calConfig?.Color,
                        RepeatType = arp.RepeatType ?? 0,
                        RepeatEvery = arp.RepeatEvery ?? 1,
                        Completed = false,
                        PropertyId = arp.PropertyId,
                        IsFromCompliance = false,
                        NextExecutionTime = planning.NextExecutionTime,
                        PlanningId = planning.Id,
                        IsAllDay = isAllDay
                    };

                    if (ShouldIncludeTask(model, requestModel))
                    {
                        result.Add(model);
                    }
                }
            }

            // 2. Query Compliances (past/historical tasks) — reuse pre-loaded data
            var compliances = compliancesInWeek;

            // Batch-load AreaRulePlannings for compliances
            var compliancePlanningIds = compliances.Select(x => x.PlanningId).Distinct().ToList();
            var complianceArps = await backendConfigurationPnDbContext.AreaRulePlannings
                .Where(x => compliancePlanningIds.Contains(x.ItemPlanningId))
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Include(x => x.AreaRule)
                    .ThenInclude(x => x.AreaRuleTranslations)
                .Include(x => x.PlanningSites)
                .ToListAsync();
            var complianceArpDict = complianceArps.ToDictionary(x => x.ItemPlanningId);

            // Batch-load calendar configs for compliance ARPs
            var complianceArpIds = complianceArps.Select(x => x.Id).ToList();
            var complianceCalConfigs = await backendConfigurationPnDbContext.CalendarConfigurations
                .Where(x => complianceArpIds.Contains(x.AreaRulePlanningId))
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .ToDictionaryAsync(x => x.AreaRulePlanningId);

            // Batch-load tags for compliance ARPs
            var complianceArpTags = await backendConfigurationPnDbContext.AreaRulePlanningTags
                .Where(x => complianceArpIds.Contains(x.AreaRulePlanningId))
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .ToListAsync();

            var complianceTagItemIds = complianceArpTags.Select(x => x.ItemPlanningTagId).Distinct().ToList();
            var compliancePlanningTagNames = await itemsPlanningPnDbContext.PlanningTags
                .Where(x => complianceTagItemIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.Name);

            foreach (var compliance in compliances)
            {
                complianceArpDict.TryGetValue(compliance.PlanningId, out var arp);
                CalendarConfiguration calConfig = null;
                if (arp != null)
                    complianceCalConfigs.TryGetValue(arp.Id, out calConfig);

                var title = compliance.ItemName ?? "";
                if (arp?.AreaRule?.AreaRuleTranslations != null)
                {
                    title = arp.AreaRule.AreaRuleTranslations
                        .Where(t => t.LanguageId == userLanguageId)
                        .Select(t => t.Name)
                        .FirstOrDefault() ?? title;
                }

                var tags = arp != null
                    ? complianceArpTags
                        .Where(x => x.AreaRulePlanningId == arp.Id)
                        .Select(x => compliancePlanningTagNames.TryGetValue(x.ItemPlanningTagId, out var name) ? name : null)
                        .Where(x => x != null)
                        .ToList()
                    : [];

                var compIsAllDay = calConfig == null;
                var model = new CalendarTaskResponseModel
                {
                    Id = arp?.Id ?? 0,
                    Title = title,
                    StartHour = compIsAllDay ? 0 : calConfig?.StartHour ?? 9.0,
                    Duration = compIsAllDay ? 0 : calConfig?.Duration ?? 1.0,
                    TaskDate = compliance.Deadline.ToString("yyyy-MM-dd"),
                    Tags = tags,
                    AssigneeIds = arp?.PlanningSites?
                        .Where(ps => ps.WorkflowState != Constants.WorkflowStates.Removed)
                        .Select(ps => (int)ps.SiteId)
                        .ToList() ?? [],
                    BoardId = calConfig?.BoardId ?? defaultBoardId,
                    Color = calConfig?.Color,
                    RepeatType = arp?.RepeatType ?? 0,
                    RepeatEvery = arp?.RepeatEvery ?? 1,
                    Completed = compliance.Deadline < DateTime.UtcNow,
                    PropertyId = compliance.PropertyId,
                    ComplianceId = compliance.Id,
                    IsFromCompliance = true,
                    Deadline = compliance.Deadline,
                    PlanningId = compliance.PlanningId,
                    IsAllDay = compIsAllDay
                };

                if (ShouldIncludeTask(model, requestModel))
                {
                    result.Add(model);
                }
            }

            return new OperationDataResult<List<CalendarTaskResponseModel>>(true, result);
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            logger.LogError(e, "BackendConfigurationCalendarService.GetTasksForWeek: {Message}", e.Message);
            return new OperationDataResult<List<CalendarTaskResponseModel>>(false,
                $"{localizationService.GetString("ErrorWhileGettingCalendarTasks")}: {e.Message}");
        }
    }

    public async Task<OperationResult> CreateTask(CalendarTaskCreateRequestModel createModel)
    {
        try
        {
            // Validate: cannot create task in the past
            var taskDateTime = createModel.StartDate.AddHours(createModel.StartHour);
            if (taskDateTime < DateTime.UtcNow)
            {
                return new OperationResult(false,
                    localizationService.GetString("CannotCreateTaskInThePast"));
            }

            // Build TaskWizardCreateModel from the calendar request
            var wizardModel = new TaskWizardCreateModel
            {
                PropertyId = createModel.PropertyId,
                FolderId = createModel.FolderId,
                ItemPlanningTagId = createModel.ItemPlanningTagId,
                TagIds = createModel.TagIds,
                Translates = createModel.Translates,
                EformId = createModel.EformId,
                StartDate = createModel.StartDate,
                RepeatType = (Infrastructure.Enums.RepeatType)createModel.RepeatType,
                RepeatEvery = createModel.RepeatEvery,
                Status = (Infrastructure.Enums.TaskWizardStatuses)createModel.Status,
                Sites = createModel.Sites,
                ComplianceEnabled = createModel.ComplianceEnabled
            };

            var result = await taskWizardService.CreateTask(wizardModel);
            if (!result.Success)
            {
                return result;
            }

            // Find the AreaRulePlanning created by TaskWizard for this specific task
            var latestArp = await backendConfigurationPnDbContext.AreaRulePlannings
                .Where(x => x.PropertyId == createModel.PropertyId)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Include(x => x.AreaRule)
                .Where(x => x.AreaRule.CreatedInGuide == true)
                .Where(x => x.AreaRule.EformId == createModel.EformId)
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync();

            if (latestArp != null)
            {
                var calConfig = new CalendarConfiguration
                {
                    AreaRulePlanningId = latestArp.Id,
                    StartHour = createModel.StartHour,
                    Duration = createModel.Duration,
                    BoardId = createModel.BoardId,
                    Color = createModel.Color,
                    CreatedByUserId = userService.UserId,
                    UpdatedByUserId = userService.UserId
                };
                await calConfig.Create(backendConfigurationPnDbContext);
            }

            return new OperationResult(true,
                localizationService.GetString("CalendarTaskCreatedSuccessfully"));
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            logger.LogError(e, "BackendConfigurationCalendarService.CreateTask: {Message}", e.Message);
            return new OperationResult(false,
                $"{localizationService.GetString("ErrorWhileCreatingCalendarTask")}: {e.Message}");
        }
    }

    public async Task<OperationResult> UpdateTask(CalendarTaskUpdateRequestModel updateModel)
    {
        try
        {
            // Validate: cannot update task to the past
            var taskDateTime = updateModel.StartDate.AddHours(updateModel.StartHour);
            if (taskDateTime < DateTime.UtcNow)
            {
                return new OperationResult(false,
                    localizationService.GetString("CannotCreateTaskInThePast"));
            }

            // Delegate to TaskWizard service for full task field updates
            var wizardModel = new TaskWizardCreateModel
            {
                Id = updateModel.Id,
                PropertyId = updateModel.PropertyId,
                FolderId = updateModel.FolderId,
                ItemPlanningTagId = updateModel.ItemPlanningTagId,
                TagIds = updateModel.TagIds,
                Translates = updateModel.Translates,
                EformId = updateModel.EformId,
                StartDate = updateModel.StartDate,
                RepeatType = (Infrastructure.Enums.RepeatType)updateModel.RepeatType,
                RepeatEvery = updateModel.RepeatEvery,
                Status = (Infrastructure.Enums.TaskWizardStatuses)updateModel.Status,
                Sites = updateModel.Sites,
                ComplianceEnabled = updateModel.ComplianceEnabled
            };

            var wizardResult = await taskWizardService.UpdateTask(wizardModel);
            if (!wizardResult.Success)
            {
                return wizardResult;
            }

            // Update or create CalendarConfiguration for calendar-specific fields
            var calConfig = await backendConfigurationPnDbContext.CalendarConfigurations
                .Where(x => x.AreaRulePlanningId == updateModel.Id)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .FirstOrDefaultAsync();

            if (calConfig != null)
            {
                calConfig.StartHour = updateModel.StartHour;
                calConfig.Duration = updateModel.Duration;
                calConfig.BoardId = updateModel.BoardId;
                calConfig.Color = updateModel.Color;
                calConfig.UpdatedByUserId = userService.UserId;
                await calConfig.Update(backendConfigurationPnDbContext);
            }
            else
            {
                calConfig = new CalendarConfiguration
                {
                    AreaRulePlanningId = updateModel.Id,
                    StartHour = updateModel.StartHour,
                    Duration = updateModel.Duration,
                    BoardId = updateModel.BoardId,
                    Color = updateModel.Color,
                    CreatedByUserId = userService.UserId,
                    UpdatedByUserId = userService.UserId
                };
                await calConfig.Create(backendConfigurationPnDbContext);
            }

            return new OperationResult(true,
                localizationService.GetString("CalendarTaskUpdatedSuccessfully"));
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            logger.LogError(e, "BackendConfigurationCalendarService.UpdateTask: {Message}", e.Message);
            return new OperationResult(false,
                $"{localizationService.GetString("ErrorWhileUpdatingCalendarTask")}: {e.Message}");
        }
    }

    public async Task<OperationResult> DeleteTask(int id)
    {
        try
        {
            // Delete CalendarConfiguration if it exists
            var calConfig = await backendConfigurationPnDbContext.CalendarConfigurations
                .Where(x => x.AreaRulePlanningId == id)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .FirstOrDefaultAsync();

            if (calConfig != null)
            {
                await calConfig.Delete(backendConfigurationPnDbContext);
            }

            // Delete the underlying AreaRulePlanning/Planning via TaskWizard service
            var wizardResult = await taskWizardService.DeleteTask(id);
            if (!wizardResult.Success)
            {
                return wizardResult;
            }

            return new OperationResult(true,
                localizationService.GetString("CalendarTaskDeletedSuccessfully"));
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            logger.LogError(e, "BackendConfigurationCalendarService.DeleteTask: {Message}", e.Message);
            return new OperationResult(false,
                $"{localizationService.GetString("ErrorWhileDeletingCalendarTask")}: {e.Message}");
        }
    }

    public async Task<OperationResult> MoveTask(CalendarTaskMoveRequestModel moveModel)
    {
        try
        {
            var newDate = DateTime.Parse(moveModel.NewDate, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

            // Validate: cannot move task to the past
            var taskDateTime = newDate.AddHours(moveModel.NewStartHour);
            if (taskDateTime < DateTime.UtcNow)
            {
                return new OperationResult(false,
                    localizationService.GetString("CannotCreateTaskInThePast"));
            }

            var arp = await backendConfigurationPnDbContext.AreaRulePlannings
                .Where(x => x.Id == moveModel.Id)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .FirstOrDefaultAsync();

            if (arp == null)
            {
                return new OperationResult(false,
                    localizationService.GetString("AreaRulePlanningNotFound"));
            }

            // Update AreaRulePlanning start date
            arp.StartDate = newDate;
            arp.UpdatedByUserId = userService.UserId;
            await arp.Update(backendConfigurationPnDbContext);

            // Update Planning start date
            var planning = await itemsPlanningPnDbContext.Plannings
                .Where(x => x.Id == arp.ItemPlanningId)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .FirstOrDefaultAsync();

            if (planning != null)
            {
                planning.StartDate = newDate;
                planning.UpdatedByUserId = userService.UserId;
                await planning.Update(itemsPlanningPnDbContext);
            }

            // Update or create CalendarConfiguration
            var calConfig = await backendConfigurationPnDbContext.CalendarConfigurations
                .Where(x => x.AreaRulePlanningId == moveModel.Id)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .FirstOrDefaultAsync();

            if (calConfig != null)
            {
                calConfig.StartHour = moveModel.NewStartHour;
                calConfig.UpdatedByUserId = userService.UserId;
                await calConfig.Update(backendConfigurationPnDbContext);
            }
            else
            {
                calConfig = new CalendarConfiguration
                {
                    AreaRulePlanningId = moveModel.Id,
                    StartHour = moveModel.NewStartHour,
                    Duration = 1.0,
                    CreatedByUserId = userService.UserId,
                    UpdatedByUserId = userService.UserId
                };
                await calConfig.Create(backendConfigurationPnDbContext);
            }

            return new OperationResult(true,
                localizationService.GetString("CalendarTaskMovedSuccessfully"));
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            logger.LogError(e, "BackendConfigurationCalendarService.MoveTask: {Message}", e.Message);
            return new OperationResult(false,
                $"{localizationService.GetString("ErrorWhileMovingCalendarTask")}: {e.Message}");
        }
    }

    public async Task<OperationResult> ToggleComplete(int id, bool completed)
    {
        // TODO: Implement completion toggle via Compliance system
        return new OperationResult(true);
    }

    public async Task<OperationDataResult<List<CalendarBoardModel>>> GetBoards(int propertyId)
    {
        try
        {
            var boards = await backendConfigurationPnDbContext.CalendarBoards
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.PropertyId == propertyId)
                .Select(x => new CalendarBoardModel
                {
                    Id = x.Id,
                    Name = x.Name,
                    Color = x.Color,
                    PropertyId = x.PropertyId,
                })
                .ToListAsync();

            // Auto-create "Default" board if none exist
            if (boards.Count == 0)
            {
                var defaultBoard = new CalendarBoard
                {
                    Name = "Default",
                    Color = "#c30000",
                    PropertyId = propertyId,
                };
                await defaultBoard.Create(backendConfigurationPnDbContext);

                boards.Add(new CalendarBoardModel
                {
                    Id = defaultBoard.Id,
                    Name = defaultBoard.Name,
                    Color = defaultBoard.Color,
                    PropertyId = defaultBoard.PropertyId,
                });
            }

            return new OperationDataResult<List<CalendarBoardModel>>(true, boards);
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            logger.LogError(e, "BackendConfigurationCalendarService.GetBoards: {Message}", e.Message);
            return new OperationDataResult<List<CalendarBoardModel>>(false,
                $"{localizationService.GetString("ErrorWhileGettingCalendarBoards")}: {e.Message}");
        }
    }

    public async Task<OperationResult> CreateBoard(CalendarBoardCreateModel model)
    {
        try
        {
            var board = new CalendarBoard
            {
                Name = model.Name,
                Color = model.Color,
                PropertyId = model.PropertyId,
            };
            await board.Create(backendConfigurationPnDbContext);

            return new OperationResult(true,
                localizationService.GetString("CalendarBoardCreatedSuccessfully"));
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            logger.LogError(e, "BackendConfigurationCalendarService.CreateBoard: {Message}", e.Message);
            return new OperationResult(false,
                $"{localizationService.GetString("ErrorWhileCreatingCalendarBoard")}: {e.Message}");
        }
    }

    public async Task<OperationResult> UpdateBoard(CalendarBoardUpdateModel model)
    {
        try
        {
            var board = await backendConfigurationPnDbContext.CalendarBoards
                .Where(x => x.Id == model.Id)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .FirstOrDefaultAsync();

            if (board == null)
            {
                return new OperationResult(false,
                    localizationService.GetString("CalendarBoardNotFound"));
            }

            board.Name = model.Name;
            board.Color = model.Color;
            await board.Update(backendConfigurationPnDbContext);

            return new OperationResult(true,
                localizationService.GetString("CalendarBoardUpdatedSuccessfully"));
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            logger.LogError(e, "BackendConfigurationCalendarService.UpdateBoard: {Message}", e.Message);
            return new OperationResult(false,
                $"{localizationService.GetString("ErrorWhileUpdatingCalendarBoard")}: {e.Message}");
        }
    }

    public async Task<OperationResult> DeleteBoard(int id)
    {
        try
        {
            var board = await backendConfigurationPnDbContext.CalendarBoards
                .Where(x => x.Id == id)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .FirstOrDefaultAsync();

            if (board == null)
            {
                return new OperationResult(false,
                    localizationService.GetString("CalendarBoardNotFound"));
            }

            await board.Delete(backendConfigurationPnDbContext);

            return new OperationResult(true,
                localizationService.GetString("CalendarBoardDeletedSuccessfully"));
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            logger.LogError(e, "BackendConfigurationCalendarService.DeleteBoard: {Message}", e.Message);
            return new OperationResult(false,
                $"{localizationService.GetString("ErrorWhileDeletingCalendarBoard")}: {e.Message}");
        }
    }

    private static List<DateTime> GetOccurrencesInWeek(
        Microting.ItemsPlanningBase.Infrastructure.Data.Entities.Planning planning,
        DateTime weekStart, DateTime weekEnd)
    {
        var occurrences = new List<DateTime>();
        var startDate = planning.StartDate.Date;
        var repeatEvery = Math.Max(planning.RepeatEvery, 1);

        switch (planning.RepeatType)
        {
            case Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType.Day:
            {
                // Find the first occurrence on or after weekStart
                if (startDate > weekEnd) break;
                var daysSinceStart = (weekStart.Date - startDate).Days;
                var periods = daysSinceStart > 0 ? (int)Math.Ceiling((double)daysSinceStart / repeatEvery) : 0;
                var candidate = startDate.AddDays(periods * repeatEvery);
                while (candidate <= weekEnd)
                {
                    if (candidate >= weekStart)
                        occurrences.Add(candidate);
                    candidate = candidate.AddDays(repeatEvery);
                }
                break;
            }
            case Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType.Week:
            {
                if (startDate > weekEnd) break;
                var daysBetween = repeatEvery * 7;
                var daysSinceStart = (weekStart.Date - startDate).Days;
                var periods = daysSinceStart > 0 ? (int)Math.Ceiling((double)daysSinceStart / daysBetween) : 0;
                var candidate = startDate.AddDays(periods * daysBetween);
                while (candidate <= weekEnd)
                {
                    if (candidate >= weekStart)
                        occurrences.Add(candidate);
                    candidate = candidate.AddDays(daysBetween);
                }
                break;
            }
            case Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType.Month:
            {
                if (startDate > weekEnd) break;
                var dom = Math.Min(planning.DayOfMonth ?? startDate.Day, 28);
                // Find starting month
                var monthsSinceStart = (weekStart.Year - startDate.Year) * 12 + weekStart.Month - startDate.Month;
                var periods = monthsSinceStart > 0 ? (int)Math.Ceiling((double)monthsSinceStart / repeatEvery) : 0;
                var candidateMonth = startDate.AddMonths(periods * repeatEvery);
                for (var i = 0; i < 3; i++) // at most 3 months can overlap a week
                {
                    var candidate = new DateTime(candidateMonth.Year, candidateMonth.Month,
                        Math.Min(dom, DateTime.DaysInMonth(candidateMonth.Year, candidateMonth.Month)),
                        0, 0, 0, DateTimeKind.Utc);
                    if (candidate > weekEnd) break;
                    if (candidate >= weekStart)
                        occurrences.Add(candidate);
                    candidateMonth = candidateMonth.AddMonths(repeatEvery);
                }
                break;
            }
            default:
            {
                // No repeat — show on StartDate if it falls in the week
                if (startDate >= weekStart && startDate <= weekEnd)
                    occurrences.Add(startDate);
                break;
            }
        }

        // Respect RepeatUntil if set
        if (planning.RepeatUntil.HasValue)
            occurrences.RemoveAll(d => d > planning.RepeatUntil.Value);

        return occurrences;
    }

    private static bool ShouldIncludeTask(CalendarTaskResponseModel task, CalendarTaskRequestModel filter)
    {
        if (filter.BoardIds is { Count: > 0 } && task.BoardId.HasValue &&
            !filter.BoardIds.Contains(task.BoardId.Value))
        {
            return false;
        }

        if (filter.TagNames is { Count: > 0 } &&
            !task.Tags.Any(t => filter.TagNames.Contains(t)))
        {
            return false;
        }

        if (filter.SiteIds is { Count: > 0 } &&
            !task.AssigneeIds.Any(id => filter.SiteIds.Contains(id)))
        {
            return false;
        }

        return true;
    }
}
