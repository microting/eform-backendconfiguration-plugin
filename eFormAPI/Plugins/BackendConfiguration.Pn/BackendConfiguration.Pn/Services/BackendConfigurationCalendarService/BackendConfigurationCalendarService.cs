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

            foreach (var arp in areaRulePlannings)
            {
                var planning = await itemsPlanningPnDbContext.Plannings
                    .Where(x => x.Id == arp.ItemPlanningId)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .FirstOrDefaultAsync();

                if (planning == null) continue;

                // Check if the planning's next execution overlaps with the requested week
                if (planning.NextExecutionTime.HasValue &&
                    planning.NextExecutionTime.Value >= weekStart &&
                    planning.NextExecutionTime.Value <= weekEnd)
                {
                    var calConfig = await backendConfigurationPnDbContext.CalendarConfigurations
                        .Where(x => x.AreaRulePlanningId == arp.Id)
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .FirstOrDefaultAsync();

                    var title = arp.AreaRule?.AreaRuleTranslations?
                        .Where(t => t.LanguageId == userLanguageId)
                        .Select(t => t.Name)
                        .FirstOrDefault() ?? arp.AreaRule?.AreaRuleTranslations?.FirstOrDefault()?.Name ?? "";

                    var tags = await backendConfigurationPnDbContext.AreaRulePlanningTags
                        .Where(x => x.AreaRulePlanningId == arp.Id)
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Join(itemsPlanningPnDbContext.PlanningTags,
                            tag => tag.ItemPlanningTagId,
                            pt => pt.Id,
                            (tag, pt) => pt.Name)
                        .ToListAsync();

                    var model = new CalendarTaskResponseModel
                    {
                        Id = arp.Id,
                        Title = title,
                        StartHour = calConfig?.StartHour ?? 9.0,
                        Duration = calConfig?.Duration ?? 1.0,
                        TaskDate = planning.NextExecutionTime.Value.ToString("yyyy-MM-dd"),
                        Tags = tags,
                        AssigneeIds = arp.PlanningSites?
                            .Where(ps => ps.WorkflowState != Constants.WorkflowStates.Removed)
                            .Select(ps => (int)ps.SiteId)
                            .ToList() ?? [],
                        BoardId = calConfig?.BoardId,
                        Color = calConfig?.Color,
                        RepeatType = arp.RepeatType ?? 0,
                        RepeatEvery = arp.RepeatEvery ?? 1,
                        Completed = false,
                        PropertyId = arp.PropertyId,
                        IsFromCompliance = false,
                        NextExecutionTime = planning.NextExecutionTime,
                        PlanningId = planning.Id
                    };

                    if (ShouldIncludeTask(model, requestModel))
                    {
                        result.Add(model);
                    }
                }
            }

            // 2. Query Compliances (past/historical tasks)
            var compliances = await backendConfigurationPnDbContext.Compliances
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.PropertyId == requestModel.PropertyId)
                .Where(x => x.Deadline >= weekStart && x.Deadline <= weekEnd)
                .ToListAsync();

            foreach (var compliance in compliances)
            {
                var arp = await backendConfigurationPnDbContext.AreaRulePlannings
                    .Where(x => x.ItemPlanningId == compliance.PlanningId)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Include(x => x.AreaRule)
                        .ThenInclude(x => x.AreaRuleTranslations)
                    .Include(x => x.PlanningSites)
                    .FirstOrDefaultAsync();

                var calConfig = arp != null
                    ? await backendConfigurationPnDbContext.CalendarConfigurations
                        .Where(x => x.AreaRulePlanningId == arp.Id)
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .FirstOrDefaultAsync()
                    : null;

                var title = compliance.ItemName ?? "";
                if (arp?.AreaRule?.AreaRuleTranslations != null)
                {
                    title = arp.AreaRule.AreaRuleTranslations
                        .Where(t => t.LanguageId == userLanguageId)
                        .Select(t => t.Name)
                        .FirstOrDefault() ?? title;
                }

                var tags = arp != null
                    ? await backendConfigurationPnDbContext.AreaRulePlanningTags
                        .Where(x => x.AreaRulePlanningId == arp.Id)
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Join(itemsPlanningPnDbContext.PlanningTags,
                            tag => tag.ItemPlanningTagId,
                            pt => pt.Id,
                            (tag, pt) => pt.Name)
                        .ToListAsync()
                    : [];

                var model = new CalendarTaskResponseModel
                {
                    Id = arp?.Id ?? 0,
                    Title = title,
                    StartHour = calConfig?.StartHour ?? 9.0,
                    Duration = calConfig?.Duration ?? 1.0,
                    TaskDate = compliance.Deadline.ToString("yyyy-MM-dd"),
                    Tags = tags,
                    AssigneeIds = arp?.PlanningSites?
                        .Where(ps => ps.WorkflowState != Constants.WorkflowStates.Removed)
                        .Select(ps => (int)ps.SiteId)
                        .ToList() ?? [],
                    BoardId = calConfig?.BoardId,
                    Color = calConfig?.Color,
                    RepeatType = arp?.RepeatType ?? 0,
                    RepeatEvery = arp?.RepeatEvery ?? 1,
                    Completed = true,
                    PropertyId = compliance.PropertyId,
                    ComplianceId = compliance.Id,
                    IsFromCompliance = true,
                    Deadline = compliance.Deadline,
                    PlanningId = compliance.PlanningId
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

            // Find the latest AreaRulePlanning for this property to link the calendar config
            var latestArp = await backendConfigurationPnDbContext.AreaRulePlannings
                .Where(x => x.PropertyId == createModel.PropertyId)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .OrderByDescending(x => x.CreatedAt)
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

            var calConfig = await backendConfigurationPnDbContext.CalendarConfigurations
                .Where(x => x.AreaRulePlanningId == updateModel.Id)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .FirstOrDefaultAsync();

            if (calConfig == null)
            {
                return new OperationResult(false,
                    localizationService.GetString("CalendarConfigurationNotFound"));
            }

            calConfig.StartHour = updateModel.StartHour;
            calConfig.Duration = updateModel.Duration;
            calConfig.BoardId = updateModel.BoardId;
            calConfig.Color = updateModel.Color;
            calConfig.UpdatedByUserId = userService.UserId;
            await calConfig.Update(backendConfigurationPnDbContext);

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
            var calConfig = await backendConfigurationPnDbContext.CalendarConfigurations
                .Where(x => x.AreaRulePlanningId == id)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .FirstOrDefaultAsync();

            if (calConfig == null)
            {
                return new OperationResult(false,
                    localizationService.GetString("CalendarConfigurationNotFound"));
            }

            await calConfig.Delete(backendConfigurationPnDbContext);

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

        return true;
    }
}
