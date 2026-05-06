using Sentry;

namespace BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using BackendConfigurationLocalizationService;
using BackendConfigurationTaskWizardService;
using Infrastructure.Models.Calendar;
using Infrastructure.Models.TaskWizard;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Dto;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Helpers;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.eForm.Infrastructure.Models;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Data;
using SdkUploadedData = Microting.eForm.Infrastructure.Data.Entities.UploadedData;

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
                .Include(x => x.AreaRulePlanningFiles)
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

            // Batch-load occurrence exceptions for this week
            var exceptionsInWeek = await backendConfigurationPnDbContext.CalendarOccurrenceExceptions
                .Where(x => arpIds.Contains(x.AreaRulePlanningId))
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x =>
                    (x.OriginalDate >= weekStart && x.OriginalDate <= weekEnd) ||
                    (x.NewDate.HasValue && x.NewDate.Value >= weekStart && x.NewDate.Value <= weekEnd))
                .Include(x => x.ExceptionSites)
                .ToListAsync();

            var exceptionsByArp = exceptionsInWeek
                .GroupBy(x => x.AreaRulePlanningId)
                .ToDictionary(
                    g => g.Key,
                    g => g.ToDictionary(x => x.OriginalDate.Date));

            var movedInExceptions = exceptionsInWeek
                .Where(x => x.NewDate.HasValue
                    && !x.IsDeleted
                    && (x.OriginalDate < weekStart || x.OriginalDate > weekEnd)
                    && x.NewDate.Value >= weekStart && x.NewDate.Value <= weekEnd)
                .ToList();

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

                // Compute all occurrence dates within the requested week.
                // Pass arp.RepeatWeekdaysCsv so multi-day weekly rules
                // (e.g. "1,3,5") expand to multiple occurrences per week.
                var occurrences = GetOccurrencesInWeek(planning, weekStart, weekEnd, arp.RepeatWeekdaysCsv);

                // Filter by repeat end mode
                if (arp.RepeatEndMode == 2 && arp.RepeatUntilDate.HasValue)
                    occurrences.RemoveAll(d => d > arp.RepeatUntilDate.Value);
                else if (arp.RepeatEndMode == 1 && arp.RepeatOccurrences.HasValue)
                {
                    // Use EnumerateOccurrences (week-loop iterator) instead of
                    // GetOccurrencesInWeek for the cumulative count: the latter's
                    // multi-day weekly branch emits at most one matching week, so
                    // the after-cap would never fire for CSV rules. Upper bound
                    // on EnumerateOccurrences is exclusive — add a day.
                    var allOccsSince = EnumerateOccurrences(planning,
                        planning.StartDate.Date, weekEnd.AddDays(1),
                        arp.RepeatWeekdaysCsv).ToList();
                    var maxOcc = arp.RepeatOccurrences.Value;
                    if (allOccsSince.Count > maxOcc)
                    {
                        var cutoff = allOccsSince[maxOcc - 1];
                        occurrences.RemoveAll(d => d > cutoff);
                    }
                }

                // Even when the rule generates no occurrences for this week,
                // we still need to consider per-occurrence exceptions whose
                // OriginalDate falls inside the requested window — they
                // render via the orphan-anchor pass below.
                var hasInWeekExceptions = exceptionsByArp.TryGetValue(arp.Id, out var inWeekArpExceptions)
                    && inWeekArpExceptions.Values.Any(x =>
                        !x.IsDeleted
                        && x.OriginalDate >= weekStart && x.OriginalDate <= weekEnd
                        && (!x.NewDate.HasValue || x.NewDate.Value.Date == x.OriginalDate.Date));

                if (occurrences.Count == 0 && !hasInWeekExceptions)
                    continue;

                calConfigsDict.TryGetValue(arp.Id, out var calConfig);
                var isRepeatAlways = arp.RepeatType.HasValue && arp.RepeatType.Value == 1 && (arp.RepeatEvery ?? 0) == 0;
                var hasNonAlwaysRepeat = arp.RepeatType.HasValue && arp.RepeatType.Value > 0 && !isRepeatAlways;
                var isAllDay = calConfig == null && !hasNonAlwaysRepeat;

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
                    if (compliancePlanningIdsInWeek.Contains(arp.ItemPlanningId))
                        continue;

                    CalendarOccurrenceException exception = null;
                    if (exceptionsByArp.TryGetValue(arp.Id, out var arpExceptions))
                    {
                        arpExceptions.TryGetValue(occurrenceDate.Date, out exception);
                    }

                    if (exception is { IsDeleted: true })
                        continue;

                    var effectiveDate = exception?.NewDate?.Date ?? occurrenceDate;
                    var effectiveStartHour = exception?.StartHour ?? (isAllDay ? 0 : calConfig?.StartHour ?? 9.0);
                    var effectiveDuration = exception?.Duration ?? (isAllDay ? 0 : calConfig?.Duration ?? 1.0);
                    var effectiveAssignees = exception?.ExceptionSites is { Count: > 0 }
                        ? exception.ExceptionSites
                            .Where(s => s.WorkflowState != Constants.WorkflowStates.Removed)
                            .Select(s => s.SiteId)
                            .ToList()
                        : assigneeIds;

                    var model = new CalendarTaskResponseModel
                    {
                        Id = arp.Id,
                        Title = title,
                        StartHour = effectiveStartHour,
                        Duration = effectiveDuration,
                        TaskDate = effectiveDate.ToString("yyyy-MM-dd"),
                        Tags = tags,
                        AssigneeIds = effectiveAssignees,
                        BoardId = calConfig?.BoardId ?? defaultBoardId,
                        Color = calConfig?.Color,
                        RepeatType = arp.RepeatType ?? 0,
                        RepeatEvery = arp.RepeatEvery ?? 1,
                        RepeatEndMode = arp.RepeatEndMode,
                        RepeatOccurrences = arp.RepeatOccurrences,
                        RepeatUntilDate = arp.RepeatUntilDate,
                        DayOfWeek = arp.DayOfWeek,
                        DayOfMonth = arp.DayOfMonth,
                        RepeatWeekdaysCsv = arp.RepeatWeekdaysCsv,
                        Completed = false,
                        PropertyId = arp.PropertyId,
                        IsFromCompliance = false,
                        NextExecutionTime = planning.NextExecutionTime,
                        PlanningId = planning.Id,
                        IsAllDay = isAllDay,
                        ExceptionId = exception?.Id,
                        EformId = arp.AreaRule?.EformId,
                        ItemPlanningTagId = arp.ItemPlanningTagId,
                        DescriptionHtml = planning.Description,
                        Attachments = MapAttachments(arp)
                    };

                    if (ShouldIncludeTask(model, requestModel))
                    {
                        result.Add(model);
                    }
                }

                // Render any in-week exceptions whose OriginalDate is NOT
                // covered by the recurrence rule (e.g. past anchors created
                // when a 'thisAndFollowing' move shifted planning.StartDate
                // forward). Without this, those past occurrences would
                // silently disappear from the calendar view.
                if (exceptionsByArp.TryGetValue(arp.Id, out var allArpExceptions))
                {
                    var renderedDates = new HashSet<DateTime>(occurrences.Select(o => o.Date));
                    foreach (var orphan in allArpExceptions.Values)
                    {
                        if (orphan.IsDeleted) continue;
                        if (renderedDates.Contains(orphan.OriginalDate.Date)) continue;
                        if (orphan.OriginalDate < weekStart || orphan.OriginalDate > weekEnd) continue;
                        // Skip exceptions whose NewDate moves them to a
                        // different date — those are handled by the
                        // movedInExceptions pass at the destination week.
                        if (orphan.NewDate.HasValue && orphan.NewDate.Value.Date != orphan.OriginalDate.Date) continue;

                        var orphanStartHour = orphan.StartHour ?? (isAllDay ? 0 : calConfig?.StartHour ?? 9.0);
                        var orphanDuration = orphan.Duration ?? (isAllDay ? 0 : calConfig?.Duration ?? 1.0);
                        var orphanAssignees = orphan.ExceptionSites is { Count: > 0 }
                            ? orphan.ExceptionSites
                                .Where(s => s.WorkflowState != Constants.WorkflowStates.Removed)
                                .Select(s => s.SiteId)
                                .ToList()
                            : assigneeIds;

                        var orphanModel = new CalendarTaskResponseModel
                        {
                            Id = arp.Id,
                            Title = title,
                            StartHour = orphanStartHour,
                            Duration = orphanDuration,
                            TaskDate = orphan.OriginalDate.ToString("yyyy-MM-dd"),
                            Tags = tags,
                            AssigneeIds = orphanAssignees,
                            BoardId = calConfig?.BoardId ?? defaultBoardId,
                            Color = calConfig?.Color,
                            RepeatType = arp.RepeatType ?? 0,
                            RepeatEvery = arp.RepeatEvery ?? 1,
                            RepeatEndMode = arp.RepeatEndMode,
                            RepeatOccurrences = arp.RepeatOccurrences,
                            RepeatUntilDate = arp.RepeatUntilDate,
                            DayOfWeek = arp.DayOfWeek,
                            DayOfMonth = arp.DayOfMonth,
                            RepeatWeekdaysCsv = arp.RepeatWeekdaysCsv,
                            Completed = false,
                            PropertyId = arp.PropertyId,
                            IsFromCompliance = false,
                            NextExecutionTime = planning.NextExecutionTime,
                            PlanningId = planning.Id,
                            IsAllDay = isAllDay,
                            ExceptionId = orphan.Id,
                            EformId = arp.AreaRule?.EformId,
                            ItemPlanningTagId = arp.ItemPlanningTagId,
                            DescriptionHtml = planning.Description,
                            Attachments = MapAttachments(arp)
                        };

                        if (ShouldIncludeTask(orphanModel, requestModel))
                        {
                            result.Add(orphanModel);
                        }
                    }
                }
            }

            // Add occurrences that were moved INTO this week from outside
            foreach (var movedIn in movedInExceptions)
            {
                var arp = areaRulePlannings.FirstOrDefault(a => a.Id == movedIn.AreaRulePlanningId);
                if (arp == null) continue;
                if (!planningsDict.TryGetValue(arp.ItemPlanningId, out var movedPlanning)) continue;

                calConfigsDict.TryGetValue(arp.Id, out var movedCalConfig);
                var isRepeatAlways = arp.RepeatType.HasValue && arp.RepeatType.Value == 1 && (arp.RepeatEvery ?? 0) == 0;
                var hasNonAlwaysRepeat = arp.RepeatType.HasValue && arp.RepeatType.Value > 0 && !isRepeatAlways;
                var isAllDay = movedCalConfig == null && !hasNonAlwaysRepeat;

                var title = arp.AreaRule?.AreaRuleTranslations?
                    .Where(t => t.LanguageId == userLanguageId)
                    .Select(t => t.Name)
                    .FirstOrDefault() ?? arp.AreaRule?.AreaRuleTranslations?.FirstOrDefault()?.Name ?? "";

                var movedTags = allArpTags
                    .Where(x => x.AreaRulePlanningId == arp.Id)
                    .Select(x => planningTagNames.TryGetValue(x.ItemPlanningTagId, out var name) ? name : null)
                    .Where(x => x != null)
                    .ToList();

                var movedAssignees = movedIn.ExceptionSites is { Count: > 0 }
                    ? movedIn.ExceptionSites
                        .Where(s => s.WorkflowState != Constants.WorkflowStates.Removed)
                        .Select(s => s.SiteId)
                        .ToList()
                    : arp.PlanningSites?
                        .Where(ps => ps.WorkflowState != Constants.WorkflowStates.Removed)
                        .Select(ps => (int)ps.SiteId)
                        .ToList() ?? [];

                var movedModel = new CalendarTaskResponseModel
                {
                    Id = arp.Id,
                    Title = title,
                    StartHour = movedIn.StartHour ?? (isAllDay ? 0 : movedCalConfig?.StartHour ?? 9.0),
                    Duration = movedIn.Duration ?? (isAllDay ? 0 : movedCalConfig?.Duration ?? 1.0),
                    TaskDate = movedIn.NewDate!.Value.ToString("yyyy-MM-dd"),
                    Tags = movedTags,
                    AssigneeIds = movedAssignees,
                    BoardId = movedCalConfig?.BoardId ?? defaultBoardId,
                    Color = movedCalConfig?.Color,
                    RepeatType = arp.RepeatType ?? 0,
                    RepeatEvery = arp.RepeatEvery ?? 1,
                    RepeatEndMode = arp.RepeatEndMode,
                    RepeatOccurrences = arp.RepeatOccurrences,
                    RepeatUntilDate = arp.RepeatUntilDate,
                    DayOfWeek = arp.DayOfWeek,
                    DayOfMonth = arp.DayOfMonth,
                    RepeatWeekdaysCsv = arp.RepeatWeekdaysCsv,
                    Completed = false,
                    PropertyId = arp.PropertyId,
                    IsFromCompliance = false,
                    NextExecutionTime = movedPlanning.NextExecutionTime,
                    PlanningId = movedPlanning.Id,
                    IsAllDay = isAllDay,
                    ExceptionId = movedIn.Id,
                    EformId = arp.AreaRule?.EformId,
                    ItemPlanningTagId = arp.ItemPlanningTagId,
                    DescriptionHtml = movedPlanning.Description,
                    Attachments = MapAttachments(arp)
                };

                if (ShouldIncludeTask(movedModel, requestModel))
                {
                    result.Add(movedModel);
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
                .Include(x => x.AreaRulePlanningFiles)
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

            // Batch-load compliance plannings so we can read description from Planning
            var compliancePlanningsDict = await itemsPlanningPnDbContext.Plannings
                .Where(x => compliancePlanningIds.Contains(x.Id))
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .ToDictionaryAsync(x => x.Id);

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

                var compIsRepeatAlways = arp?.RepeatType.HasValue == true && arp.RepeatType.Value == 1 && (arp.RepeatEvery ?? 0) == 0;
                var compHasNonAlwaysRepeat = arp?.RepeatType.HasValue == true && arp.RepeatType.Value > 0 && !compIsRepeatAlways;
                var compIsAllDay = calConfig == null && !compHasNonAlwaysRepeat;
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
                    RepeatEndMode = arp?.RepeatEndMode,
                    RepeatOccurrences = arp?.RepeatOccurrences,
                    RepeatUntilDate = arp?.RepeatUntilDate,
                    DayOfWeek = arp?.DayOfWeek,
                    DayOfMonth = arp?.DayOfMonth,
                    RepeatWeekdaysCsv = arp?.RepeatWeekdaysCsv,
                    Completed = false,
                    PropertyId = compliance.PropertyId,
                    ComplianceId = compliance.Id,
                    IsFromCompliance = true,
                    Deadline = compliance.Deadline,
                    PlanningId = compliance.PlanningId,
                    IsAllDay = compIsAllDay,
                    EformId = arp?.AreaRule?.EformId,
                    SdkCaseId = compliance.MicrotingSdkCaseId,
                    ItemPlanningTagId = arp?.ItemPlanningTagId,
                    DescriptionHtml = compliancePlanningsDict.TryGetValue(compliance.PlanningId, out var cp)
                        ? cp.Description
                        : null,
                    Attachments = MapAttachments(arp)
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

    public async Task<OperationDataResult<int>> CreateTask(CalendarTaskCreateRequestModel createModel)
    {
        try
        {
            // Validate: cannot create task in the past
            var taskDateTime = createModel.StartDate.AddHours(createModel.StartHour);
            if (taskDateTime < DateTime.UtcNow)
            {
                return new OperationDataResult<int>(false,
                    localizationService.GetString("CannotCreateTaskInThePast"));
            }

            // Validate: at least one worker must be assigned. Events without
            // an assignee would be downgraded to NotActive by task-wizard and
            // vanish from the calendar view (GetTasksForWeek filters Status),
            // so creation is rejected here with a clear error rather than
            // silently producing an invisible event.
            if (createModel.Sites is null || createModel.Sites.Count == 0)
            {
                return new OperationDataResult<int>(false,
                    localizationService.GetString("AtLeastOneWorkerMustBeAssigned"));
            }

            // Resolve FolderId: if not provided, find or create the "00. Logbøger" folder
            var resolvedFolderId = createModel.FolderId;
            if (resolvedFolderId is null or 0)
            {
                resolvedFolderId = await ResolveOrCreateLogbøgerFolderAsync(createModel.PropertyId);
            }

            // Build TaskWizardCreateModel from the calendar request
            var wizardModel = new TaskWizardCreateModel
            {
                PropertyId = createModel.PropertyId,
                FolderId = resolvedFolderId,
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
                return new OperationDataResult<int>(false, result.Message);
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
                // Persist repeat-end and weekday-CSV fields. The CSV column is
                // always written (including null) so changing a multi-day
                // weekly back to a single-day rule clears the stale list.
                var hasRepeatEndChange = createModel.RepeatEndMode.HasValue;
                latestArp.RepeatWeekdaysCsv = createModel.RepeatWeekdaysCsv;
                if (hasRepeatEndChange)
                {
                    latestArp.RepeatEndMode = createModel.RepeatEndMode;
                    latestArp.RepeatOccurrences = createModel.RepeatOccurrences;
                    latestArp.RepeatUntilDate = createModel.RepeatUntilDate;
                }
                await latestArp.Update(backendConfigurationPnDbContext);

                // Persist description on the linked Planning row (not on ARP)
                var planning = await itemsPlanningPnDbContext.Plannings
                    .FirstOrDefaultAsync(x => x.Id == latestArp.ItemPlanningId
                        && x.WorkflowState != Constants.WorkflowStates.Removed);
                if (planning != null)
                {
                    planning.Description = createModel.DescriptionHtml ?? string.Empty;
                    planning.UpdatedByUserId = userService.UserId;
                    await planning.Update(itemsPlanningPnDbContext);
                }

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

            // latestArp may be null in the rare edge case where TaskWizard
            // succeeded but did not produce an ARP we can correlate to (e.g.
            // EformId resolution skew). Return success with id=0 — frontend
            // treats 0 as "no id, skip post-save uploads".
            return new OperationDataResult<int>(true,
                localizationService.GetString("CalendarTaskCreatedSuccessfully"),
                latestArp?.Id ?? 0);
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            logger.LogError(e, "BackendConfigurationCalendarService.CreateTask: {Message}", e.Message);
            return new OperationDataResult<int>(false,
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

            // Validate: at least one worker must remain assigned. Clearing
            // assignees would downgrade the task to NotActive and hide it
            // from the calendar view (same as the Create path).
            if (updateModel.Sites is null || updateModel.Sites.Count == 0)
            {
                return new OperationResult(false,
                    localizationService.GetString("AtLeastOneWorkerMustBeAssigned"));
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

            // Persist description on the linked Planning row (not on ARP),
            // plus the repeat-end + multi-day-weekday CSV fields on the ARP.
            // CSV is written unconditionally so switching from a custom
            // multi-day rule back to a single-day rule clears the stale list.
            var arp = await backendConfigurationPnDbContext.AreaRulePlannings
                .FirstOrDefaultAsync(x => x.Id == updateModel.Id
                    && x.WorkflowState != Constants.WorkflowStates.Removed);
            if (arp != null)
            {
                // Write end-mode fields unconditionally so switching from
                // 'after 10' or 'until <date>' back to 'never' clears the
                // stale cap. Same rationale as RepeatWeekdaysCsv above.
                arp.RepeatWeekdaysCsv = updateModel.RepeatWeekdaysCsv;
                arp.RepeatEndMode = updateModel.RepeatEndMode;
                arp.RepeatOccurrences = updateModel.RepeatOccurrences;
                arp.RepeatUntilDate = updateModel.RepeatUntilDate;
                await arp.Update(backendConfigurationPnDbContext);

                var planning = await itemsPlanningPnDbContext.Plannings
                    .FirstOrDefaultAsync(x => x.Id == arp.ItemPlanningId
                        && x.WorkflowState != Constants.WorkflowStates.Removed);
                if (planning != null)
                {
                    planning.Description = updateModel.DescriptionHtml ?? string.Empty;
                    planning.UpdatedByUserId = userService.UserId;
                    await planning.Update(itemsPlanningPnDbContext);
                }
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

    public async Task<OperationResult> DeleteTask(CalendarTaskDeleteRequestModel deleteModel)
    {
        try
        {
            var scope = deleteModel.Scope ?? "all";

            if (scope == "this" && !string.IsNullOrEmpty(deleteModel.OriginalDate))
            {
                var originalDate = DateTime.Parse(deleteModel.OriginalDate, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal).Date;

                var existing = await backendConfigurationPnDbContext.CalendarOccurrenceExceptions
                    .Where(x => x.AreaRulePlanningId == deleteModel.Id)
                    .Where(x => x.OriginalDate == originalDate)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .FirstOrDefaultAsync();

                if (existing != null)
                {
                    existing.IsDeleted = true;
                    existing.UpdatedByUserId = userService.UserId;
                    await existing.Update(backendConfigurationPnDbContext);
                }
                else
                {
                    var exception = new CalendarOccurrenceException
                    {
                        AreaRulePlanningId = deleteModel.Id,
                        OriginalDate = originalDate,
                        IsDeleted = true,
                        CreatedByUserId = userService.UserId,
                        UpdatedByUserId = userService.UserId
                    };
                    await exception.Create(backendConfigurationPnDbContext);
                }

                return new OperationResult(true,
                    localizationService.GetString("CalendarTaskDeletedSuccessfully"));
            }
            else if (scope == "thisAndFollowing" && !string.IsNullOrEmpty(deleteModel.OriginalDate))
            {
                var originalDate = DateTime.Parse(deleteModel.OriginalDate, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal).Date;

                var arp = await backendConfigurationPnDbContext.AreaRulePlannings
                    .Where(x => x.Id == deleteModel.Id)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .FirstOrDefaultAsync();

                if (arp == null)
                {
                    return new OperationResult(false,
                        localizationService.GetString("AreaRulePlanningNotFound"));
                }

                if (originalDate <= arp.StartDate)
                {
                    return await DeleteEntireSeries(deleteModel.Id);
                }

                arp.EndDate = originalDate.AddDays(-1);
                arp.UpdatedByUserId = userService.UserId;
                await arp.Update(backendConfigurationPnDbContext);

                var planning = await itemsPlanningPnDbContext.Plannings
                    .Where(x => x.Id == arp.ItemPlanningId)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .FirstOrDefaultAsync();

                if (planning != null)
                {
                    planning.RepeatUntil = originalDate.AddDays(-1);
                    planning.UpdatedByUserId = userService.UserId;
                    await planning.Update(itemsPlanningPnDbContext);
                }

                var staleExceptions = await backendConfigurationPnDbContext.CalendarOccurrenceExceptions
                    .Where(x => x.AreaRulePlanningId == deleteModel.Id)
                    .Where(x => x.OriginalDate >= originalDate)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .ToListAsync();

                foreach (var stale in staleExceptions)
                {
                    await stale.Delete(backendConfigurationPnDbContext);
                }

                return new OperationResult(true,
                    localizationService.GetString("CalendarTaskDeletedSuccessfully"));
            }
            else
            {
                return await DeleteEntireSeries(deleteModel.Id);
            }
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            logger.LogError(e, "BackendConfigurationCalendarService.DeleteTask: {Message}", e.Message);
            return new OperationResult(false,
                $"{localizationService.GetString("ErrorWhileDeletingCalendarTask")}: {e.Message}");
        }
    }

    private async Task<OperationResult> DeleteEntireSeries(int arpId)
    {
        var calConfig = await backendConfigurationPnDbContext.CalendarConfigurations
            .Where(x => x.AreaRulePlanningId == arpId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .FirstOrDefaultAsync();

        if (calConfig != null)
        {
            await calConfig.Delete(backendConfigurationPnDbContext);
        }

        var exceptions = await backendConfigurationPnDbContext.CalendarOccurrenceExceptions
            .Where(x => x.AreaRulePlanningId == arpId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync();

        foreach (var ex in exceptions)
        {
            await ex.Delete(backendConfigurationPnDbContext);
        }

        var wizardResult = await taskWizardService.DeleteTask(arpId);
        if (!wizardResult.Success)
        {
            return wizardResult;
        }

        return new OperationResult(true,
            localizationService.GetString("CalendarTaskDeletedSuccessfully"));
    }

    public async Task<OperationResult> MoveTask(CalendarTaskMoveRequestModel moveModel)
    {
        try
        {
            var newDate = DateTime.Parse(moveModel.NewDate, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

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

            var scope = moveModel.Scope ?? "all";

            if (scope == "this" && !string.IsNullOrEmpty(moveModel.OriginalDate))
            {
                var originalDate = DateTime.Parse(moveModel.OriginalDate, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal).Date;

                var exception = await backendConfigurationPnDbContext.CalendarOccurrenceExceptions
                    .Where(x => x.AreaRulePlanningId == moveModel.Id)
                    .Where(x => x.OriginalDate == originalDate)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .FirstOrDefaultAsync();

                if (exception != null)
                {
                    exception.NewDate = newDate.Date != originalDate ? newDate : null;
                    exception.StartHour = moveModel.NewStartHour;
                    exception.UpdatedByUserId = userService.UserId;
                    await exception.Update(backendConfigurationPnDbContext);
                }
                else
                {
                    exception = new CalendarOccurrenceException
                    {
                        AreaRulePlanningId = moveModel.Id,
                        OriginalDate = originalDate,
                        IsDeleted = false,
                        NewDate = newDate.Date != originalDate ? newDate : null,
                        StartHour = moveModel.NewStartHour,
                        CreatedByUserId = userService.UserId,
                        UpdatedByUserId = userService.UserId
                    };
                    await exception.Create(backendConfigurationPnDbContext);
                }
            }
            else if (scope == "thisAndFollowing" && !string.IsNullOrEmpty(moveModel.OriginalDate))
            {
                var originalDate = DateTime.Parse(moveModel.OriginalDate, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal).Date;

                // Anchor every PAST occurrence with an exception holding the
                // OLD calConfig values BEFORE we shift planning.StartDate
                // forward. Past occurrences are then rendered by
                // GetTasksForWeek's orphan-exception branch (the recurrence
                // rule will no longer generate them after the shift).
                var oldPlanning = await itemsPlanningPnDbContext.Plannings
                    .Where(x => x.Id == arp.ItemPlanningId)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .FirstOrDefaultAsync();
                var oldCalConfig = await backendConfigurationPnDbContext.CalendarConfigurations
                    .Where(x => x.AreaRulePlanningId == moveModel.Id)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .FirstOrDefaultAsync();

                if (oldPlanning != null)
                {
                    var oldStartHour = oldCalConfig?.StartHour ?? 9.0;
                    var oldDuration = oldCalConfig?.Duration ?? 1.0;

                    var existingPastDates = await backendConfigurationPnDbContext.CalendarOccurrenceExceptions
                        .Where(x => x.AreaRulePlanningId == moveModel.Id)
                        .Where(x => x.OriginalDate < originalDate)
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Select(x => x.OriginalDate)
                        .ToListAsync();
                    var existingSet = new HashSet<DateTime>(existingPastDates);

                    foreach (var occDate in EnumerateOccurrences(oldPlanning, oldPlanning.StartDate.Date, originalDate, arp.RepeatWeekdaysCsv))
                    {
                        if (existingSet.Contains(occDate)) continue;
                        var anchor = new CalendarOccurrenceException
                        {
                            AreaRulePlanningId = moveModel.Id,
                            OriginalDate = occDate,
                            IsDeleted = false,
                            NewDate = null,
                            StartHour = oldStartHour,
                            Duration = oldDuration,
                            CreatedByUserId = userService.UserId,
                            UpdatedByUserId = userService.UserId
                        };
                        await anchor.Create(backendConfigurationPnDbContext);
                    }
                }
                else
                {
                    logger.LogWarning(
                        "MoveTask thisAndFollowing backfill skipped: planning {ItemPlanningId} for AreaRulePlanning {ArpId} not found",
                        arp.ItemPlanningId, moveModel.Id);
                }

                arp.StartDate = newDate;
                arp.UpdatedByUserId = userService.UserId;
                await arp.Update(backendConfigurationPnDbContext);

                if (oldPlanning != null)
                {
                    oldPlanning.StartDate = newDate;
                    oldPlanning.UpdatedByUserId = userService.UserId;
                    await oldPlanning.Update(itemsPlanningPnDbContext);
                }

                if (oldCalConfig != null)
                {
                    oldCalConfig.StartHour = moveModel.NewStartHour;
                    oldCalConfig.UpdatedByUserId = userService.UserId;
                    await oldCalConfig.Update(backendConfigurationPnDbContext);
                }
                else
                {
                    var calConfig = new CalendarConfiguration
                    {
                        AreaRulePlanningId = moveModel.Id,
                        StartHour = moveModel.NewStartHour,
                        Duration = 1.0,
                        CreatedByUserId = userService.UserId,
                        UpdatedByUserId = userService.UserId
                    };
                    await calConfig.Create(backendConfigurationPnDbContext);
                }

                var staleExceptions = await backendConfigurationPnDbContext.CalendarOccurrenceExceptions
                    .Where(x => x.AreaRulePlanningId == moveModel.Id)
                    .Where(x => x.OriginalDate >= originalDate)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .ToListAsync();

                foreach (var stale in staleExceptions)
                {
                    await stale.Delete(backendConfigurationPnDbContext);
                }
            }
            else
            {
                arp.StartDate = newDate;
                arp.UpdatedByUserId = userService.UserId;
                await arp.Update(backendConfigurationPnDbContext);

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

                var allExceptions = await backendConfigurationPnDbContext.CalendarOccurrenceExceptions
                    .Where(x => x.AreaRulePlanningId == moveModel.Id)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .ToListAsync();

                foreach (var ex in allExceptions)
                {
                    await ex.Delete(backendConfigurationPnDbContext);
                }
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

    public async Task<OperationResult> ResizeTask(CalendarTaskResizeRequestModel resizeModel)
    {
        try
        {
            // No past-time check here on purpose: resize on an existing task
            // is legitimate even when the start is in the past (e.g. the user
            // is extending an event that's currently running). The task
            // already exists; we are not creating a new one.

            var arp = await backendConfigurationPnDbContext.AreaRulePlannings
                .Where(x => x.Id == resizeModel.Id)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .FirstOrDefaultAsync();

            if (arp == null)
            {
                return new OperationResult(false,
                    localizationService.GetString("AreaRulePlanningNotFound"));
            }

            var scope = resizeModel.Scope ?? "all";

            if (scope == "this" && !string.IsNullOrEmpty(resizeModel.OriginalDate))
            {
                var originalDate = DateTime.Parse(resizeModel.OriginalDate, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal).Date;

                var exception = await backendConfigurationPnDbContext.CalendarOccurrenceExceptions
                    .Where(x => x.AreaRulePlanningId == resizeModel.Id)
                    .Where(x => x.OriginalDate == originalDate)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .FirstOrDefaultAsync();

                if (exception != null)
                {
                    exception.StartHour = resizeModel.NewStartHour;
                    exception.Duration = resizeModel.NewDuration;
                    exception.UpdatedByUserId = userService.UserId;
                    await exception.Update(backendConfigurationPnDbContext);
                }
                else
                {
                    exception = new CalendarOccurrenceException
                    {
                        AreaRulePlanningId = resizeModel.Id,
                        OriginalDate = originalDate,
                        IsDeleted = false,
                        NewDate = null, // resize does not change the date
                        StartHour = resizeModel.NewStartHour,
                        Duration = resizeModel.NewDuration,
                        CreatedByUserId = userService.UserId,
                        UpdatedByUserId = userService.UserId
                    };
                    await exception.Create(backendConfigurationPnDbContext);
                }
            }
            else
            {
                // 'thisAndFollowing' and 'all' both update the series-wide
                // CalendarConfiguration; we deliberately do NOT touch
                // arp.StartDate or planning.StartDate (resize is not a move).
                var calConfig = await backendConfigurationPnDbContext.CalendarConfigurations
                    .Where(x => x.AreaRulePlanningId == resizeModel.Id)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .FirstOrDefaultAsync();

                // For 'thisAndFollowing', anchor every PAST occurrence to its
                // CURRENT (pre-resize) StartHour/Duration before we mutate
                // calConfig — otherwise past occurrences without their own
                // exception row would resolve through the new calConfig and
                // visually shift to the new times. (See GetTasksForWeek's
                // `exception ?? calConfig ?? defaults` resolution chain.)
                if (scope == "thisAndFollowing" && !string.IsNullOrEmpty(resizeModel.OriginalDate))
                {
                    var anchorDate = DateTime.Parse(resizeModel.OriginalDate, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal).Date;
                    var oldStartHour = calConfig?.StartHour ?? 9.0;
                    var oldDuration = calConfig?.Duration ?? 1.0;

                    var planning = await itemsPlanningPnDbContext.Plannings
                        .Where(x => x.Id == arp.ItemPlanningId)
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .FirstOrDefaultAsync();

                    if (planning == null)
                    {
                        // calConfig is about to be updated; without anchors, past
                        // occurrences will silently shift to the new times. Log so
                        // the issue is observable rather than invisible.
                        logger.LogWarning(
                            "ResizeTask thisAndFollowing backfill skipped: planning {ItemPlanningId} for AreaRulePlanning {ArpId} not found",
                            arp.ItemPlanningId, resizeModel.Id);
                    }
                    else
                    {
                        var existingPastDates = await backendConfigurationPnDbContext.CalendarOccurrenceExceptions
                            .Where(x => x.AreaRulePlanningId == resizeModel.Id)
                            .Where(x => x.OriginalDate < anchorDate)
                            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                            .Select(x => x.OriginalDate)
                            .ToListAsync();
                        var existingSet = new HashSet<DateTime>(existingPastDates);

                        foreach (var occDate in EnumerateOccurrences(planning, planning.StartDate.Date, anchorDate, arp.RepeatWeekdaysCsv))
                        {
                            if (existingSet.Contains(occDate)) continue;
                            var anchor = new CalendarOccurrenceException
                            {
                                AreaRulePlanningId = resizeModel.Id,
                                OriginalDate = occDate,
                                IsDeleted = false,
                                NewDate = null,
                                StartHour = oldStartHour,
                                Duration = oldDuration,
                                CreatedByUserId = userService.UserId,
                                UpdatedByUserId = userService.UserId
                            };
                            await anchor.Create(backendConfigurationPnDbContext);
                        }
                    }
                }

                if (calConfig != null)
                {
                    calConfig.StartHour = resizeModel.NewStartHour;
                    calConfig.Duration = resizeModel.NewDuration;
                    calConfig.UpdatedByUserId = userService.UserId;
                    await calConfig.Update(backendConfigurationPnDbContext);
                }
                else
                {
                    calConfig = new CalendarConfiguration
                    {
                        AreaRulePlanningId = resizeModel.Id,
                        StartHour = resizeModel.NewStartHour,
                        Duration = resizeModel.NewDuration,
                        CreatedByUserId = userService.UserId,
                        UpdatedByUserId = userService.UserId
                    };
                    await calConfig.Create(backendConfigurationPnDbContext);
                }

                // For thisAndFollowing, drop per-occurrence overrides from
                // OriginalDate forward — they are superseded by the new
                // series-wide values. For 'all', drop every override.
                IQueryable<CalendarOccurrenceException> staleQuery =
                    backendConfigurationPnDbContext.CalendarOccurrenceExceptions
                        .Where(x => x.AreaRulePlanningId == resizeModel.Id)
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed);

                if (scope == "thisAndFollowing" && !string.IsNullOrEmpty(resizeModel.OriginalDate))
                {
                    var originalDate = DateTime.Parse(resizeModel.OriginalDate, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal).Date;
                    staleQuery = staleQuery.Where(x => x.OriginalDate >= originalDate);
                }

                var stales = await staleQuery.ToListAsync();
                foreach (var stale in stales)
                {
                    await stale.Delete(backendConfigurationPnDbContext);
                }
            }

            return new OperationResult(true,
                localizationService.GetString("CalendarTaskUpdatedSuccessfully"));
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            logger.LogError(e, "BackendConfigurationCalendarService.ResizeTask: {Message}", e.Message);
            return new OperationResult(false,
                $"{localizationService.GetString("ErrorWhileUpdatingCalendarTask")}: {e.Message}");
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

    // Parses a comma-separated weekday CSV (e.g. "1,3,5") into a sorted,
    // de-duplicated array of JS-style weekday ints (0=Sun..6=Sat). Returns
    // an empty array on null/empty/all-invalid input — callers treat empty
    // as "no multi-day expansion, fall back to single-day weekly behavior".
    private static int[] ParseWeekdaysCsv(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return [];
        return csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var n) ? n : -1)
            .Where(n => n is >= 0 and <= 6)
            .Distinct()
            .OrderBy(n => n)
            .ToArray();
    }

    // Yields every occurrence of `planning` whose date is in
    // [fromInclusive, toExclusive). Unlike GetOccurrencesInWeek (which
    // assumes a week-sized range and caps Month/Year iteration), this is
    // safe for arbitrary multi-month / multi-year ranges. Used by
    // ResizeTask's 'thisAndFollowing' past-anchor backfill.
    //
    // Returns empty for non-recurring plannings (RepeatType.None / default
    // branch) — there are no past occurrences to anchor in that case.
    //
    // When repeatWeekdaysCsv is non-empty and the planning is RepeatType.Week,
    // the weekly branch emits one occurrence per matching weekday in each
    // matching week (anchored to startDate's week, every repeatEvery weeks).
    // Null/empty CSV preserves the legacy single-day-per-week behavior.
    private static IEnumerable<DateTime> EnumerateOccurrences(
        Microting.ItemsPlanningBase.Infrastructure.Data.Entities.Planning planning,
        DateTime fromInclusive, DateTime toExclusive,
        string? repeatWeekdaysCsv = null)
    {
        var startDate = planning.StartDate.Date;
        var rangeStart = fromInclusive.Date > startDate ? fromInclusive.Date : startDate;
        var rangeEnd = toExclusive.Date;
        if (rangeEnd <= rangeStart) yield break;
        var repeatEvery = Math.Max(planning.RepeatEvery, 1);

        switch (planning.RepeatType)
        {
            case Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType.Day:
            {
                var step = repeatEvery;
                var daysSinceStart = (rangeStart - startDate).Days;
                var skip = daysSinceStart > 0 ? (int)Math.Ceiling((double)daysSinceStart / step) : 0;
                var candidate = startDate.AddDays(skip * step);
                while (candidate < rangeEnd)
                {
                    if (candidate >= rangeStart) yield return candidate;
                    candidate = candidate.AddDays(step);
                }
                break;
            }
            case Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType.Week:
            {
                var weekdays = ParseWeekdaysCsv(repeatWeekdaysCsv);
                if (weekdays.Length == 0)
                {
                    // Legacy single-day path: step 7*repeatEvery from startDate.
                    var step = repeatEvery * 7;
                    var daysSinceStart = (rangeStart - startDate).Days;
                    var skip = daysSinceStart > 0 ? (int)Math.Ceiling((double)daysSinceStart / step) : 0;
                    var candidate = startDate.AddDays(skip * step);
                    while (candidate < rangeEnd)
                    {
                        if (candidate >= rangeStart) yield return candidate;
                        candidate = candidate.AddDays(step);
                    }
                }
                else
                {
                    // Multi-day path: anchor week is the Sunday-based week
                    // containing startDate (matches JS getDay() numbering).
                    // For each candidate day in [rangeStart, rangeEnd), emit
                    // it iff its weekday is in the CSV AND its week is a
                    // multiple of repeatEvery weeks from the anchor week.
                    var anchorWeekStart = startDate.AddDays(-(int)startDate.DayOfWeek);
                    var rangeStartWeek = rangeStart.AddDays(-(int)rangeStart.DayOfWeek);
                    // Align rangeStart back to its week-start so we iterate
                    // whole-week buckets cleanly.
                    var weeksFromAnchor = (rangeStartWeek - anchorWeekStart).Days / 7;
                    if (weeksFromAnchor < 0)
                    {
                        // Range begins before the anchor week — clamp.
                        weeksFromAnchor = 0;
                        rangeStartWeek = anchorWeekStart;
                    }
                    // Skip forward to the next "matching" week (k*repeatEvery
                    // weeks past the anchor).
                    var remainder = ((weeksFromAnchor % repeatEvery) + repeatEvery) % repeatEvery;
                    if (remainder != 0)
                    {
                        rangeStartWeek = rangeStartWeek.AddDays((repeatEvery - remainder) * 7);
                    }
                    var weekCursor = rangeStartWeek;
                    while (weekCursor < rangeEnd)
                    {
                        foreach (var wd in weekdays)
                        {
                            var candidate = weekCursor.AddDays(wd);
                            if (candidate < startDate) continue;
                            if (candidate < rangeStart) continue;
                            if (candidate >= rangeEnd) continue;
                            yield return candidate;
                        }
                        weekCursor = weekCursor.AddDays(repeatEvery * 7);
                    }
                }
                break;
            }
            case Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType.Month:
            {
                var dom = Math.Min(planning.DayOfMonth ?? startDate.Day, 28);
                var monthsSinceStart = (rangeStart.Year - startDate.Year) * 12 + rangeStart.Month - startDate.Month;
                var skip = monthsSinceStart > 0 ? (int)Math.Ceiling((double)monthsSinceStart / repeatEvery) : 0;
                var candidateMonth = startDate.AddMonths(skip * repeatEvery);
                while (true)
                {
                    var daysInMonth = DateTime.DaysInMonth(candidateMonth.Year, candidateMonth.Month);
                    var candidate = new DateTime(candidateMonth.Year, candidateMonth.Month,
                        Math.Min(dom, daysInMonth), 0, 0, 0, DateTimeKind.Utc);
                    if (candidate >= rangeEnd) break;
                    if (candidate >= rangeStart) yield return candidate;
                    candidateMonth = candidateMonth.AddMonths(repeatEvery);
                }
                break;
            }
            // NOTE: GetOccurrencesInWeek has a `(RepeatType)4 // Year` branch
            // but the RepeatType enum only defines Day/Week/Month — the cast
            // is dead code. Not propagating it here. Add a real Year case
            // when the enum gains a member.
            default:
                // Non-recurring (RepeatType.None) — no past occurrences to anchor.
                yield break;
        }
    }

    private static List<DateTime> GetOccurrencesInWeek(
        Microting.ItemsPlanningBase.Infrastructure.Data.Entities.Planning planning,
        DateTime weekStart, DateTime weekEnd,
        string? repeatWeekdaysCsv = null)
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
                var weekdays = ParseWeekdaysCsv(repeatWeekdaysCsv);
                if (weekdays.Length == 0)
                {
                    // Legacy single-day path: step 7*repeatEvery from startDate.
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
                }
                else
                {
                    // Multi-day path: only emit occurrences in this week if
                    // the requested week is a multiple of repeatEvery weeks
                    // past the anchor week (Sunday-based, matches JS getDay).
                    var anchorWeekStart = startDate.AddDays(-(int)startDate.DayOfWeek);
                    var weekStartAligned = weekStart.Date.AddDays(-(int)weekStart.Date.DayOfWeek);
                    var weeksFromAnchor = (weekStartAligned - anchorWeekStart).Days / 7;
                    if (weeksFromAnchor >= 0 && weeksFromAnchor % repeatEvery == 0)
                    {
                        foreach (var wd in weekdays)
                        {
                            var candidate = weekStartAligned.AddDays(wd);
                            if (candidate < startDate) continue;
                            if (candidate < weekStart || candidate > weekEnd) continue;
                            occurrences.Add(candidate);
                        }
                    }
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
            case (Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType)4: // Year
            {
                if (startDate > weekEnd) break;
                var yearDom = Math.Min(planning.DayOfMonth ?? startDate.Day, 28);
                var yearMonth = startDate.Month;
                var yearsSinceStart = weekStart.Year - startDate.Year;
                if (yearsSinceStart < 0) break;
                var yearPeriods = yearsSinceStart > 0 ? (int)Math.Ceiling((double)yearsSinceStart / repeatEvery) : 0;
                for (var i = 0; i < 2; i++)
                {
                    var candidateYear = startDate.Year + (yearPeriods + i) * repeatEvery;
                    var daysInMonth = DateTime.DaysInMonth(candidateYear, yearMonth);
                    var candidate = new DateTime(candidateYear, yearMonth,
                        Math.Min(yearDom, daysInMonth), 0, 0, 0, DateTimeKind.Utc);
                    if (candidate > weekEnd) break;
                    if (candidate >= weekStart)
                        occurrences.Add(candidate);
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

    private async Task<int?> ResolveOrCreateLogbøgerFolderAsync(int propertyId)
    {
        // 1) Folder already linked to this property? Use it.
        var existingFolder = await backendConfigurationPnDbContext.ProperyAreaFolders
            .Include(f => f.AreaProperty)
            .ThenInclude(a => a.Area)
            .ThenInclude(a => a.AreaTranslations)
            .Where(f => f.AreaProperty.PropertyId == propertyId)
            .Where(f => f.WorkflowState != Constants.WorkflowStates.Removed)
            .Where(f => f.AreaProperty.WorkflowState != Constants.WorkflowStates.Removed)
            .Where(f => f.AreaProperty.Area.AreaTranslations
                .Any(t => t.Name == "00. Logbøger"))
            .FirstOrDefaultAsync();

        if (existingFolder != null)
        {
            return existingFolder.FolderId;
        }

        // 2) Resolve the Logbøger Area — if it's missing (or its translations are),
        // seed from BackendConfigurationSeedAreas — same source the plugin-init
        // seed loop uses (EformBackendConfigurationPlugin.SeedDatabase).
        int areaId;
        List<Microting.eForm.Infrastructure.Models.CommonTranslationsModel> areaFolderTranslations;

        var existingAreaTranslations = await backendConfigurationPnDbContext.AreaTranslations
            .Where(x => x.Name == "00. Logbøger")
            .ToListAsync();

        if (existingAreaTranslations.Count > 0)
        {
            var sampleAreaId = existingAreaTranslations[0].AreaId;
            areaId = sampleAreaId;
            areaFolderTranslations = await backendConfigurationPnDbContext.AreaTranslations
                .Where(x => x.AreaId == sampleAreaId)
                .Select(x => new Microting.eForm.Infrastructure.Models.CommonTranslationsModel
                {
                    Name = x.Name,
                    LanguageId = x.LanguageId,
                    Description = ""
                })
                .ToListAsync();
        }
        else
        {
            var seededArea = Infrastructure.Data.Seed.Data.BackendConfigurationSeedAreas.AreasSeed
                .Where(a => a.IsDisabled == false)
                .FirstOrDefault(a => a.AreaTranslations != null
                    && a.AreaTranslations.Any(t => t.Name == "00. Logbøger"));
            if (seededArea == null)
            {
                logger.LogError("Logbøger area is missing from seed data — cannot resolve folder for property {PropertyId}", propertyId);
                return null;
            }

            var existingArea = await backendConfigurationPnDbContext.Areas
                .FirstOrDefaultAsync(a => a.Id == seededArea.Id);
            if (existingArea == null)
            {
                // Fresh Area + its translations — cascades via EF navigation.
                await seededArea.Create(backendConfigurationPnDbContext).ConfigureAwait(false);
                areaId = seededArea.Id;
            }
            else
            {
                // Area row exists but translations don't — reseed translations only.
                foreach (var translation in seededArea.AreaTranslations)
                {
                    var translationCopy = new AreaTranslation
                    {
                        AreaId = existingArea.Id,
                        LanguageId = translation.LanguageId,
                        Name = translation.Name,
                        Description = translation.Description,
                        InfoBox = translation.InfoBox,
                        Placeholder = translation.Placeholder,
                        NewItemName = translation.NewItemName
                    };
                    await translationCopy.Create(backendConfigurationPnDbContext).ConfigureAwait(false);
                }
                areaId = existingArea.Id;
            }

            areaFolderTranslations = seededArea.AreaTranslations
                .Select(t => new Microting.eForm.Infrastructure.Models.CommonTranslationsModel
                {
                    Name = t.Name,
                    LanguageId = t.LanguageId,
                    Description = ""
                })
                .ToList();
        }

        // 3) Inline only the creation portion of BackendConfigurationPropertyAreasServiceHelper.Update's
        // default branch — create AreaProperty + SDK folder + ProperyAreaFolder + seed AreaRules.
        // We skip the Update(...) call because it also computes assignmentsForDelete, which would
        // destroy any OTHER active AreaProperties this property already has.
        var core = await coreHelper.GetCore().ConfigureAwait(false);
        var sdkDbContext = core.DbContextHelper.GetDbContext();

        var property = await backendConfigurationPnDbContext.Properties
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .Where(x => x.Id == propertyId)
            .FirstAsync();

        var newAreaProperty = new AreaProperty
        {
            CreatedByUserId = userService.UserId,
            UpdatedByUserId = userService.UserId,
            AreaId = areaId,
            PropertyId = propertyId,
            Checked = true
        };
        await newAreaProperty.Create(backendConfigurationPnDbContext).ConfigureAwait(false);

        var folderId = await core.FolderCreate(areaFolderTranslations, property.FolderId).ConfigureAwait(false);

        var newAreaFolder = new ProperyAreaFolder
        {
            FolderId = folderId,
            ProperyAreaAsignmentId = newAreaProperty.Id
        };
        await newAreaFolder.Create(backendConfigurationPnDbContext).ConfigureAwait(false);

        foreach (var seedRule in Infrastructure.Data.Seed.Data.BackendConfigurationSeedAreas.AreaRules
                     .Where(x => x.AreaId == areaId))
        {
            seedRule.PropertyId = property.Id;
            seedRule.FolderId = folderId;
            seedRule.CreatedByUserId = userService.UserId;
            seedRule.UpdatedByUserId = userService.UserId;
            seedRule.ComplianceModifiable = true;
            seedRule.NotificationsModifiable = true;
            if (!string.IsNullOrEmpty(seedRule.EformName))
            {
                var eformId = await sdkDbContext.CheckListTranslations
                    .Where(x => x.Text == seedRule.EformName)
                    .Select(x => x.CheckListId)
                    .FirstOrDefaultAsync();
                if (eformId != 0)
                {
                    seedRule.EformId = eformId;
                }
            }

            await seedRule.Create(backendConfigurationPnDbContext).ConfigureAwait(false);
        }

        return folderId;
    }

    // ---------------------------------------------------------------------
    // Attachment-related helpers + endpoints (calendar event-attachments)
    // ---------------------------------------------------------------------

    private const long MaxAttachmentBytes = 25L * 1024 * 1024;
    private const int MaxAttachmentsPerPlanning = 10;

    private static readonly Dictionary<string, string[]> AllowedMimeExtensions = new()
    {
        ["application/pdf"] = new[] { ".pdf" },
        ["image/png"] = new[] { ".png" },
        ["image/jpeg"] = new[] { ".jpg", ".jpeg" }
    };

    /// <summary>
    /// Project the eager-loaded AreaRulePlanningFiles collection (filtered to
    /// non-removed rows) onto the calendar response DTO. Returns an empty
    /// list when the navigation is null or all rows are soft-deleted.
    /// </summary>
    private static List<CalendarTaskAttachmentDto> MapAttachments(AreaRulePlanning? arp)
    {
        if (arp?.AreaRulePlanningFiles == null) return new List<CalendarTaskAttachmentDto>();
        return arp.AreaRulePlanningFiles
            .Where(f => f.WorkflowState != Constants.WorkflowStates.Removed)
            .Select(f => new CalendarTaskAttachmentDto
            {
                Id = f.Id,
                OriginalFileName = f.OriginalFileName ?? string.Empty,
                MimeType = f.MimeType ?? string.Empty,
                SizeBytes = f.SizeBytes,
                DownloadUrl = $"/api/backend-configuration-pn/calendar/tasks/{arp.Id}/files/{f.Id}"
            })
            .ToList();
    }

    public async Task<OperationDataResult<CalendarTaskAttachmentDto>> UploadFile(int taskId, IFormFile file)
    {
        try
        {
            // Defensive: reject empty multipart parts immediately so the
            // remainder of the pipeline can assume a real binary.
            if (file == null || file.Length == 0)
            {
                return new OperationDataResult<CalendarTaskAttachmentDto>(false,
                    localizationService.GetString("FileNotFound"));
            }

            if (file.Length > MaxAttachmentBytes)
            {
                return new OperationDataResult<CalendarTaskAttachmentDto>(false,
                    localizationService.GetString("FileTooLarge"));
            }

            // Browsers may attach a parameter such as ";charset=binary" to the
            // Content-Type header — strip parameters before comparing against
            // the allow-list, otherwise legitimate uploads get rejected.
            var mimeType = (file.ContentType ?? string.Empty)
                .Split(';')[0]
                .Trim()
                .ToLowerInvariant();
            if (!AllowedMimeExtensions.TryGetValue(mimeType, out var allowedExts))
            {
                return new OperationDataResult<CalendarTaskAttachmentDto>(false,
                    localizationService.GetString("FileTypeNotAllowed"));
            }

            // Defence-in-depth: even when the MIME is one we accept, the file
            // extension must agree — otherwise an attacker could upload an
            // executable disguised as a PDF and rely on the browser sniffing
            // the content type back to something dangerous.
            var ext = Path.GetExtension(file.FileName ?? string.Empty).ToLowerInvariant();
            if (string.IsNullOrEmpty(ext) || !allowedExts.Contains(ext))
            {
                return new OperationDataResult<CalendarTaskAttachmentDto>(false,
                    localizationService.GetString("FileExtensionMimeMismatch"));
            }

            var planning = await backendConfigurationPnDbContext.AreaRulePlannings
                .Where(x => x.Id == taskId)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .FirstOrDefaultAsync();
            if (planning == null)
            {
                return new OperationDataResult<CalendarTaskAttachmentDto>(false,
                    localizationService.GetString("AreaRulePlanningNotFound"));
            }

            var existingCount = await backendConfigurationPnDbContext.AreaRulePlanningFiles
                .Where(x => x.AreaRulePlanningId == taskId)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .CountAsync();
            if (existingCount >= MaxAttachmentsPerPlanning)
            {
                return new OperationDataResult<CalendarTaskAttachmentDto>(false,
                    localizationService.GetString("AttachmentLimitReached"));
            }

            // We stage the upload to an intermediate file first so we can MD5
            // the on-disk copy — the same pattern used by
            // BackendConfigurationFilesService.Create and EFormFilesController.
            // Once we know the checksum we move the bytes to a deterministic
            // canonical path keyed on the checksum, then hand it to the SDK
            // for storage. The intermediate (ticks/guid-named) file is
            // *always* deleted in the finally block — that prevents the
            // disk leak that the previous implementation produced. The
            // canonical-named file is what FileLocation records and is what
            // the S3-disabled fallback in DownloadFile reads from, so it is
            // intentionally retained.
            var folder = Path.Combine(Path.GetTempPath(), "calendar-attachments");
            Directory.CreateDirectory(folder);
            var intermediatePath = Path.Combine(folder, $"{DateTime.UtcNow.Ticks}_{Guid.NewGuid():N}{ext}");

            try
            {
                await using (var stream = new FileStream(intermediatePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                string checksum;
                using (var md5 = MD5.Create())
                {
                    await using var stream = System.IO.File.OpenRead(intermediatePath);
                    var hashBytes = await md5.ComputeHashAsync(stream);
                    checksum = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                }

                var storageFileName = $"{checksum}{ext}";
                var canonicalPath = Path.Combine(folder, storageFileName);

                // Move staged bytes to the canonical path. If the canonical
                // file already exists (same checksum re-upload) keep it as-is.
                if (System.IO.File.Exists(canonicalPath))
                {
                    System.IO.File.Delete(intermediatePath);
                }
                else
                {
                    System.IO.File.Move(intermediatePath, canonicalPath);
                }

                var core = await coreHelper.GetCore().ConfigureAwait(false);
                var sdkDbContext = core.DbContextHelper.GetDbContext();

                // Mirror EFormFilesController.AddNewImage's UploadedData
                // shape — the SDK UploadedData does NOT carry the audit
                // fields (those live on the Backend-Configuration-side
                // UploadedData, a different entity). We attribute the
                // upload to the user via UploaderId; UploaderType is left
                // unset to match the canonical platform pattern (the
                // earlier "system" value mis-reported a user-initiated
                // upload as a background-system action).
                var uploadedData = new SdkUploadedData
                {
                    Checksum = checksum,
                    FileName = storageFileName,
                    FileLocation = canonicalPath,
                    Extension = ext.TrimStart('.'),
                    CurrentFile = storageFileName,
                    UploaderId = userService.UserId
                };
                await uploadedData.Create(sdkDbContext).ConfigureAwait(false);

                // SDK PutFileToStorageSystem is a no-op when S3 is disabled.
                // In that case the canonical file we just moved IS the
                // persistence layer, and DownloadFile reads it back via
                // FileLocation. When S3 is enabled the SDK uploads from the
                // canonical path; the canonical local file is left in place
                // (matching the existing platform behaviour in
                // BackendConfigurationFilesService.Create).
                await core.PutFileToStorageSystem(canonicalPath, storageFileName).ConfigureAwait(false);

                var arpFile = new AreaRulePlanningFile
                {
                    AreaRulePlanningId = taskId,
                    UploadedDataId = uploadedData.Id,
                    OriginalFileName = file.FileName ?? string.Empty,
                    MimeType = mimeType,
                    SizeBytes = file.Length,
                    CreatedByUserId = userService.UserId,
                    UpdatedByUserId = userService.UserId
                };
                await arpFile.Create(backendConfigurationPnDbContext).ConfigureAwait(false);

                return new OperationDataResult<CalendarTaskAttachmentDto>(true, new CalendarTaskAttachmentDto
                {
                    Id = arpFile.Id,
                    OriginalFileName = arpFile.OriginalFileName,
                    MimeType = arpFile.MimeType,
                    SizeBytes = arpFile.SizeBytes,
                    DownloadUrl = $"/api/backend-configuration-pn/calendar/tasks/{taskId}/files/{arpFile.Id}"
                });
            }
            finally
            {
                // Belt-and-braces: ensure the intermediate (ticks/guid-named)
                // staging file is gone regardless of which code path ran.
                // The canonical (checksum-named) file is the one we keep.
                try
                {
                    if (System.IO.File.Exists(intermediatePath))
                    {
                        System.IO.File.Delete(intermediatePath);
                    }
                }
                catch
                {
                    // Cleanup is best-effort — we don't want a stale-handle
                    // exception masking the original outcome.
                }
            }
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            logger.LogError(e, "BackendConfigurationCalendarService.UploadFile: {Message}", e.Message);
            return new OperationDataResult<CalendarTaskAttachmentDto>(false,
                $"{localizationService.GetString("ErrorWhileUploadingAttachment")}: {e.Message}");
        }
    }

    public async Task<OperationDataResult<List<CalendarTaskAttachmentDto>>> ListFiles(int taskId)
    {
        try
        {
            var files = await backendConfigurationPnDbContext.AreaRulePlanningFiles
                .Where(x => x.AreaRulePlanningId == taskId)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .OrderBy(x => x.Id)
                .Select(x => new CalendarTaskAttachmentDto
                {
                    Id = x.Id,
                    OriginalFileName = x.OriginalFileName ?? string.Empty,
                    MimeType = x.MimeType ?? string.Empty,
                    SizeBytes = x.SizeBytes,
                    DownloadUrl = $"/api/backend-configuration-pn/calendar/tasks/{taskId}/files/{x.Id}"
                })
                .ToListAsync();
            return new OperationDataResult<List<CalendarTaskAttachmentDto>>(true, files);
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            logger.LogError(e, "BackendConfigurationCalendarService.ListFiles: {Message}", e.Message);
            return new OperationDataResult<List<CalendarTaskAttachmentDto>>(false,
                $"{localizationService.GetString("ErrorWhileListingAttachments")}: {e.Message}");
        }
    }

    public async Task<CalendarFileDownload?> DownloadFile(int taskId, int fileId)
    {
        try
        {
            var arpFile = await backendConfigurationPnDbContext.AreaRulePlanningFiles
                .Where(x => x.Id == fileId)
                .Where(x => x.AreaRulePlanningId == taskId)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .FirstOrDefaultAsync();
            if (arpFile == null) return null;

            var core = await coreHelper.GetCore().ConfigureAwait(false);
            var sdkDbContext = core.DbContextHelper.GetDbContext();

            var uploadedData = await sdkDbContext.UploadedDatas
                .Where(x => x.Id == arpFile.UploadedDataId)
                .FirstOrDefaultAsync();
            if (uploadedData == null) return null;

            // Determine S3-vs-local through the SAME mechanism the SDK itself
            // uses for PutFileToStorageSystem (Core.GetSdkSetting). This way
            // the read path can never disagree with the write path: if the
            // SDK persisted to S3, we read from S3; if the SDK no-op'd, we
            // read the canonical local file that UploadFile retained at
            // FileLocation.
            var s3Setting = await core.GetSdkSetting(Settings.s3Enabled).ConfigureAwait(false);
            var s3Enabled = string.Equals(s3Setting, "true", StringComparison.OrdinalIgnoreCase);

            Stream content;
            if (s3Enabled)
            {
                var s3Response = await core.GetFileFromS3Storage(uploadedData.FileName);
                content = s3Response.ResponseStream;
            }
            else
            {
                if (!System.IO.File.Exists(uploadedData.FileLocation))
                {
                    return null;
                }
                content = new FileStream(uploadedData.FileLocation, FileMode.Open, FileAccess.Read);
            }

            return new CalendarFileDownload
            {
                Content = content,
                MimeType = string.IsNullOrEmpty(arpFile.MimeType) ? "application/octet-stream" : arpFile.MimeType,
                FileName = arpFile.OriginalFileName ?? string.Empty
            };
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            logger.LogError(e, "BackendConfigurationCalendarService.DownloadFile: {Message}", e.Message);
            return null;
        }
    }

    public async Task<OperationResult> DeleteFile(int taskId, int fileId)
    {
        try
        {
            var arpFile = await backendConfigurationPnDbContext.AreaRulePlanningFiles
                .Where(x => x.Id == fileId)
                .Where(x => x.AreaRulePlanningId == taskId)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .FirstOrDefaultAsync();
            if (arpFile == null)
            {
                return new OperationResult(false, localizationService.GetString("FileNotFound"));
            }

            arpFile.UpdatedByUserId = userService.UserId;
            // Soft-delete the join row; intentionally do NOT delete the SDK
            // UploadedData so the audit chain to the original blob survives.
            await arpFile.Delete(backendConfigurationPnDbContext).ConfigureAwait(false);

            return new OperationResult(true);
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            logger.LogError(e, "BackendConfigurationCalendarService.DeleteFile: {Message}", e.Message);
            return new OperationResult(false,
                $"{localizationService.GetString("ErrorWhileDeletingAttachment")}: {e.Message}");
        }
    }
}
