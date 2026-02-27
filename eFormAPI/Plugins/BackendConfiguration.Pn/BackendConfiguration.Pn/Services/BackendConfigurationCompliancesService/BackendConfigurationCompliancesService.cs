/*
The MIT License (MIT)

Copyright (c) 2007 - 2021 Microting A/S

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


using BackendConfiguration.Pn.Controllers;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.TimePlanningBase.Infrastructure.Data;

namespace BackendConfiguration.Pn.Services.BackendConfigurationCompliancesService;

using BackendConfigurationLocalizationService;
using Infrastructure.Models.Compliances.Index;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eForm.Infrastructure.Models;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Delegates.CaseUpdate;
using Microting.eFormApi.BasePn.Infrastructure.Helpers;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.eFormApi.BasePn.Infrastructure.Models.Application.Case.CaseEdit;
using Microting.eFormApi.BasePn.Infrastructure.Models.Common;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.ItemsPlanningBase.Infrastructure.Data;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public class BackendConfigurationCompliancesService : IBackendConfigurationCompliancesService
{

    private readonly IEFormCoreService _coreHelper;
    private readonly IBackendConfigurationLocalizationService _localizationService;
    private readonly IUserService _userService;
    private readonly BackendConfigurationPnDbContext _backendConfigurationPnDbContext;
    private readonly ItemsPlanningPnDbContext _itemsPlanningPnDbContext;
    private readonly TimePlanningPnDbContext _timePlanningPnDbContext;

    public BackendConfigurationCompliancesService(
        ItemsPlanningPnDbContext itemsPlanningPnDbContext,
        BackendConfigurationPnDbContext backendConfigurationPnDbContext,
        IUserService userService,
        IBackendConfigurationLocalizationService localizationService,
        IEFormCoreService coreHelper,
        TimePlanningPnDbContext timePlanningPnDbContext
    )
    {
        _itemsPlanningPnDbContext = itemsPlanningPnDbContext;
        _backendConfigurationPnDbContext = backendConfigurationPnDbContext;
        _userService = userService;
        _localizationService = localizationService;
        _coreHelper = coreHelper;
        _timePlanningPnDbContext = timePlanningPnDbContext;
    }

    public async Task<OperationDataResult<Paged<CompliancesModel>>> Index(CompliancesRequestModel request)
    {
        var language = await _userService.GetCurrentUserLanguage().ConfigureAwait(false);
        var result = new Paged<CompliancesModel>
        {
            Entities = []
        };

        var core = await _coreHelper.GetCore().ConfigureAwait(false);
        var sdkDbContext = core.DbContextHelper.GetDbContext();

        var complianceList = _backendConfigurationPnDbContext.Compliances
            .Where(x => x.PropertyId == request.PropertyId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed);

        if (request.Days > 0)
        {
            complianceList = complianceList.Where(x => x.Deadline <= DateTime.Now.AddDays(request.Days));
        }

        var theList = await complianceList.AsNoTracking()
            .OrderBy(x => x.Deadline)
            .ToListAsync().ConfigureAwait(false);

        foreach (var compliance in theList)
        {
            var planningNameTranslation = await _itemsPlanningPnDbContext.PlanningNameTranslation
                .SingleOrDefaultAsync(x => x.PlanningId == compliance.PlanningId && x.LanguageId == language.Id).ConfigureAwait(false);

            if (planningNameTranslation == null)
            {
                continue;
            }
            var areaTranslation = await _backendConfigurationPnDbContext.AreaTranslations
                .SingleOrDefaultAsync(x => x.AreaId == compliance.AreaId && x.LanguageId == language.Id).ConfigureAwait(false);

            if (areaTranslation == null)
            {
                continue;
            }

            var planningSites = await _itemsPlanningPnDbContext.PlanningSites
                .Where(x => x.PlanningId == compliance.PlanningId)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Select(x => x.SiteId)
                .Distinct()
                .ToListAsync().ConfigureAwait(false);

            var sitesList = await sdkDbContext.Sites.Where(x => planningSites.Contains(x.Id)).ToListAsync().ConfigureAwait(false);

            var responsible = sitesList.Select(site => new KeyValuePair<int, string>(site.Id, site.Name)).ToList();

            var complianceModel = new CompliancesModel
            {
                CaseId = compliance.MicrotingSdkCaseId,
                CreatedAt = compliance.CreatedAt,
                Deadline = compliance.Deadline.AddDays(-1),
                ComplianceTypeId = null,
                ControlArea = areaTranslation.Name,
                EformId = compliance.MicrotingSdkeFormId,
                Id = compliance.Id,
                ItemName = planningNameTranslation.Name,
                PlanningId = compliance.PlanningId,
                Responsible = responsible
            };

            result.Entities.Add(complianceModel);
        }

        return new OperationDataResult<Paged<CompliancesModel>>(true, result);
    }

    public async Task<OperationDataResult<int>> ComplianceStatus(int propertyId)
    {
        var compliance = await Index(new CompliancesRequestModel
        {
            PropertyId = propertyId
        }).ConfigureAwait(false);

        return new OperationDataResult<int>(true, compliance.Model.Entities.Count == 0 ? 0 : 1);
    }

    public async Task<OperationDataResult<ReplyElement>> Read(int id)
    {
        try
        {
            var core = await _coreHelper.GetCore().ConfigureAwait(false);
            // var sdkDbContext = core.DbContextHelper.GetDbContext();
            // var caseDto = await sdkDbContext.Cases.SingleOrDefaultAsync(x => x.Id == id);
            // if (caseDto == null)
            // {
            // return new OperationDataResult<ReplyElement>(false, _localizationService.GetString("CaseNotFound"));
            // }
            var language = await _userService.GetCurrentUserLanguage().ConfigureAwait(false);
            var theCase = await core.CaseRead(id, language).ConfigureAwait(false);
            // theCase.Id = id;

            return !theCase.Equals(null)
                ? new OperationDataResult<ReplyElement>(true, theCase)
                : new OperationDataResult<ReplyElement>(false);
        }
        catch (Exception ex)
        {
            Log.LogException(ex.Message);
            Log.LogException(ex.StackTrace);
            return new OperationDataResult<ReplyElement>(false, ex.Message);
        }
    }

    public async Task<OperationResult> Update(ReplyRequest model)
    {
        var checkListValueList = new List<string>();
        var fieldValueList = new List<string>();
        var core = await _coreHelper.GetCore().ConfigureAwait(false);
        var language = await _userService.GetCurrentUserLanguage().ConfigureAwait(false);
        var currentUser = await _userService.GetCurrentUserAsync().ConfigureAwait(false);
        try
        {
            model.ElementList.ForEach(element =>
            {
                checkListValueList.AddRange(CaseUpdateHelper.GetCheckList(element));
                fieldValueList.AddRange(CaseUpdateHelper.GetFieldList(element));
            });
        }
        catch (Exception ex)
        {
            Log.LogException(ex.Message);
            Log.LogException(ex.StackTrace);
            return new OperationResult(false, $"{_localizationService.GetString("CaseCouldNotBeUpdated")} Exception: {ex.Message}");
        }

        try
        {
            var compliance = await _backendConfigurationPnDbContext.Compliances.SingleOrDefaultAsync(x => x.Id == model.ExtraId).ConfigureAwait(false);
            if (compliance != null)
            {
                await compliance.Delete(_backendConfigurationPnDbContext).ConfigureAwait(false);
            }
            else
            {
                return new OperationResult(false, $"{_localizationService.GetString("CaseCouldNotBeUpdated")}");
            }


            await core.CaseUpdate(model.Id, fieldValueList, checkListValueList).ConfigureAwait(false);
            await core.CaseUpdateFieldValues(model.Id, language).ConfigureAwait(false);

            var sdkDbContext = core.DbContextHelper.GetDbContext();

            var foundCase = await sdkDbContext.Cases
                .Where(x => x.Id == model.Id)
                .FirstOrDefaultAsync().ConfigureAwait(false);

            if(foundCase != null) {
                // var now = DateTime.UtcNow;
                var newDoneAt = new DateTime(model.DoneAt.Year, model.DoneAt.Month,
                    model.DoneAt.Day, 0, 0,
                    0, DateTimeKind.Utc);
                foundCase.DoneAtUserModifiable = newDoneAt;
                foundCase.DoneAt = newDoneAt;

                var site = await sdkDbContext.Sites
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .FirstOrDefaultAsync(x => x.Id == model.SiteId).ConfigureAwait(false);
                // if (site != null)
                // {
                //     foundCase.SiteId = site.Id;
                // }
                // else
                // {
                //     await core.SiteCreate($"{currentUser.FirstName} {currentUser.LastName}", currentUser.FirstName, currentUser.LastName,
                //         null, "da");
                //     site = await sdkDbContext.Sites
                //         .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                //         .FirstOrDefaultAsync(x => x.Name == $"{currentUser.FirstName} {currentUser.LastName}");
                //     foundCase.SiteId = site.Id;
                // }

                foundCase.SiteId = model.SiteId;
                foundCase.Status = 100;
                foundCase.WorkflowState = Constants.WorkflowStates.Created;
                await foundCase.Update(sdkDbContext).ConfigureAwait(false);

                if (CaseUpdateDelegates.CaseUpdateDelegate != null)
                {
                    var invocationList = CaseUpdateDelegates.CaseUpdateDelegate
                        .GetInvocationList();
                    foreach (var func in invocationList)
                    {
                        func.DynamicInvoke(model.Id);
                    }
                }
                // if (compliance.PlanningCaseSiteId != 0)
                // {
                //     var planningCaseSite = await _itemsPlanningPnDbContext.PlanningCaseSites
                //         .SingleOrDefaultAsync(x => x.Id == compliance.PlanningCaseSiteId).ConfigureAwait(false);
                //     if (planningCaseSite != null)
                //     {
                //         planningCaseSite.Status = 100;
                //         planningCaseSite = await SetFieldValue(planningCaseSite, foundCase.Id, language).ConfigureAwait(false);
                //
                //         planningCaseSite.MicrotingSdkCaseDoneAt = newDoneAt;
                //         planningCaseSite.MicrotingSdkCaseId = foundCase.Id;
                //         planningCaseSite.DoneByUserId = (int)foundCase.SiteId;
                //         planningCaseSite.DoneByUserName = $"{currentUser.FirstName} {currentUser.LastName}";
                //         await planningCaseSite.Update(_itemsPlanningPnDbContext).ConfigureAwait(false);
                //
                //         var planningCase = await _itemsPlanningPnDbContext.PlanningCases
                //             .SingleAsync(x => x.Id == planningCaseSite.PlanningCaseId).ConfigureAwait(false);
                //         if (planningCase.Status != 100)
                //         {
                //             planningCase.Status = 100;
                //             planningCase.MicrotingSdkCaseDoneAt = newDoneAt;
                //             planningCase.MicrotingSdkCaseId = foundCase.Id;
                //             planningCase.DoneByUserId = (int)foundCase.SiteId;
                //             planningCase.DoneByUserName = planningCaseSite.DoneByUserName;
                //             planningCase.WorkflowState = Constants.WorkflowStates.Processed;
                //
                //             planningCase = await SetFieldValue(planningCase, foundCase.Id, language).ConfigureAwait(false);
                //             await planningCase.Update(_itemsPlanningPnDbContext).ConfigureAwait(false);
                //         }
                //
                //         planningCaseSite.PlanningCaseId = planningCase.Id;
                //         await planningCaseSite.Update(_itemsPlanningPnDbContext).ConfigureAwait(false);
                //     }
                // }
                // else
                // {
                var planningCaseSite = await _itemsPlanningPnDbContext.PlanningCaseSites
                    .FirstOrDefaultAsync(x => x.CreatedAt.Date == compliance.StartDate.Date && x.PlanningId == compliance.PlanningId).ConfigureAwait(false);
                if (planningCaseSite != null)
                {
                    planningCaseSite.Status = 100;
                    planningCaseSite = await SetFieldValue(planningCaseSite, foundCase.Id, language).ConfigureAwait(false);

                    planningCaseSite.MicrotingSdkCaseId = foundCase.Id;
                    planningCaseSite.MicrotingSdkCaseDoneAt = foundCase.DoneAt;
                    planningCaseSite.DoneByUserId = (int)foundCase.SiteId;
                    planningCaseSite.DoneByUserName = site.Name;
                    await planningCaseSite.Update(_itemsPlanningPnDbContext).ConfigureAwait(false);

                    var planningCase = await _itemsPlanningPnDbContext.PlanningCases
                        .SingleAsync(x => x.Id == planningCaseSite.PlanningCaseId).ConfigureAwait(false);
                    if (planningCase.Status != 100)
                    {
                        planningCase.Status = 100;
                        planningCase.MicrotingSdkCaseDoneAt = foundCase.DoneAt;
                        planningCase.MicrotingSdkCaseId = foundCase.Id;
                        planningCase.DoneByUserId = (int)foundCase.SiteId;
                        planningCase.DoneByUserName = planningCaseSite.DoneByUserName;
                        planningCase.WorkflowState = Constants.WorkflowStates.Processed;

                        planningCase = await SetFieldValue(planningCase, foundCase.Id, language).ConfigureAwait(false);
                        await planningCase.Update(_itemsPlanningPnDbContext).ConfigureAwait(false);
                    }
                    planningCaseSite.PlanningCaseId = planningCase.Id;
                    await planningCaseSite.Update(_itemsPlanningPnDbContext).ConfigureAwait(false);
                }
                // }
            }
            else
            {
                return new OperationResult(false, _localizationService.GetString("CaseNotFound"));
            }

            var property = await _backendConfigurationPnDbContext.Properties.SingleAsync(x => x.Id == compliance.PropertyId).ConfigureAwait(false);

            if (_backendConfigurationPnDbContext.Compliances.AsNoTracking().Any(x =>
                    x.Deadline < DateTime.UtcNow && x.PropertyId == property.Id &&
                    x.WorkflowState != Constants.WorkflowStates.Removed))
            {
                property.ComplianceStatus = 2;
                property.ComplianceStatusThirty = 2;
                await property.Update(_backendConfigurationPnDbContext).ConfigureAwait(false);
            }
            else
            {
                if (!_backendConfigurationPnDbContext.Compliances.AsNoTracking().Any(x =>
                        x.Deadline < DateTime.UtcNow.AddDays(30) && x.PropertyId == property.Id &&
                        x.WorkflowState != Constants.WorkflowStates.Removed))
                {
                    property.ComplianceStatusThirty = 0;
                    await property.Update(_backendConfigurationPnDbContext).ConfigureAwait(false);
                }

                if (!_backendConfigurationPnDbContext.Compliances.AsNoTracking().Any(x =>
                        x.Deadline < DateTime.UtcNow && x.PropertyId == property.Id &&
                        x.WorkflowState != Constants.WorkflowStates.Removed))
                {
                    property.ComplianceStatus = 0;
                    await property.Update(_backendConfigurationPnDbContext).ConfigureAwait(false);
                }
            }
            
            try
            {
                if (foundCase.MicrotingUid != null)
                {
                    await core.CaseDelete((int)foundCase.MicrotingUid).ConfigureAwait(false);
                }
                else
                {
                    var checkListSite = await sdkDbContext.CheckListSites
                        .AsNoTracking()
                        .FirstOrDefaultAsync(x => x.Id == model.Id)
                        .ConfigureAwait(false);
                    if (checkListSite != null)
                    {
                        await core.CaseDelete(checkListSite.MicrotingUid).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.LogException(ex.Message);
                Log.LogException(ex.StackTrace);
            }

            return new OperationResult(true, _localizationService.GetString("CaseHasBeenUpdated"));
        }
        catch (Exception ex)
        {
            Log.LogException(ex.Message);
            Log.LogException(ex.StackTrace);
            return new OperationResult(false, _localizationService.GetString("CaseCouldNotBeUpdated") + $" Exception: {ex.Message}");
        }
    }

    public async Task<OperationResult> Delete(int id)
    {
        var compliance = await _backendConfigurationPnDbContext.Compliances.FirstOrDefaultAsync(x => x.Id == id).ConfigureAwait(false);
        if (compliance == null)
        {
            return new OperationResult(false, _localizationService.GetString("ComplianceNotFound"));
        }
        await compliance.Delete(_backendConfigurationPnDbContext).ConfigureAwait(false);

        return new OperationResult(true, _localizationService.GetString("TaskDeletedSuccessful"));
    }

    public async Task<OperationDataResult<CompliancesStatsModel>> Stats()
    {
        var envTag = await _itemsPlanningPnDbContext.PlanningTags.Where(x => x.Name == "Miljøtilsyn").FirstAsync();
        var complianceList = _backendConfigurationPnDbContext.Compliances;
        var oneWeekInTheFutureCount = await complianceList.CountAsync(x => x.Deadline >= DateTime.UtcNow && x.Deadline <= DateTime.UtcNow.AddDays(7));
        var todayCount = await complianceList.CountAsync(x => x.Deadline.Date <= DateTime.UtcNow.Date && x.WorkflowState != Constants.WorkflowStates.Removed);

        var numberOfPlannedEnvironmentInspectionTagTasks = await _backendConfigurationPnDbContext.AreaRulePlannings.Join(_backendConfigurationPnDbContext.AreaRulePlanningTags,
            planning => planning.Id,
            planningTag => planningTag.AreaRulePlanningId,
            (planning, planningTag) => new { Planning = planning, PlanningTag = planningTag })
            .Where(x => x.Planning.WorkflowState != Constants.WorkflowStates.Removed)
            .Where(x => x.Planning.Status)
            .Where(x => x.PlanningTag.ItemPlanningTagId == envTag.Id)
            .CountAsync();

        var numberOfPlannedEnvironmentInspectionTagPlanningsLast30Days = complianceList
            .Where(x => x.Deadline >= DateTime.UtcNow && x.Deadline >= DateTime.UtcNow.AddDays(-30)).ToList()
            .Where(x =>
            {
                var planningTags = _itemsPlanningPnDbContext.PlanningsTags
                    .Where(y => y.PlanningId == x.PlanningId && y.PlanningTagId == envTag.Id)
                    .ToList();
                return planningTags.Any();
            })
            .Count();

        var todayComplianceCountEnvironmentInspectionTag = await complianceList.Where(x => x.Deadline.Date <= DateTime.UtcNow.Date && x.WorkflowState != Constants.WorkflowStates.Removed).ToListAsync();
        var todayCountEnvironmentInspectionTag = todayComplianceCountEnvironmentInspectionTag.Where(x =>
        {
            var planningTags = _itemsPlanningPnDbContext.PlanningsTags
                .Where(y => y.PlanningId == x.PlanningId && y.PlanningTagId == envTag.Id)
                .ToList();
            return planningTags.Any();
        }).Count();
        var oldestEnvironmentInspectionTagPlannedTask = todayComplianceCountEnvironmentInspectionTag
            .Where(x =>
            {
                var planningTags = _itemsPlanningPnDbContext.PlanningsTags
                    .Where(y => y.PlanningId == x.PlanningId && y.PlanningTagId == envTag.Id)
                    .ToList();
                return planningTags.Any();
            })
            .OrderBy(x => x.Deadline)
            .FirstOrDefault()?.Deadline;
        var oneWeekCount = await complianceList.CountAsync(x => x.Deadline <= DateTime.UtcNow && x.Deadline >= DateTime.UtcNow.AddDays(-7));
        var twoWeeksCount = await complianceList.CountAsync(x => x.Deadline <= DateTime.UtcNow.AddDays(-7) && x.Deadline >= DateTime.UtcNow.AddDays(-14));
        var oneMonthCount = await complianceList.CountAsync(x => x.Deadline <= DateTime.UtcNow.AddDays(-14) && x.Deadline >= DateTime.UtcNow.AddDays(-30));
        var twoMonthsCount = await complianceList.CountAsync(x => x.Deadline <= DateTime.UtcNow.AddDays(-30) && x.Deadline >= DateTime.UtcNow.AddDays(-60));
        var threeMonthsCount = await complianceList.CountAsync(x => x.Deadline <= DateTime.UtcNow.AddDays(-60) && x.Deadline >= DateTime.UtcNow.AddDays(-90));
        var sixMonthsCount = await complianceList.CountAsync(x => x.Deadline <= DateTime.UtcNow.AddDays(-90) && x.Deadline >= DateTime.UtcNow.AddDays(-180));
        var moreThanSixMonthsCount = await complianceList.CountAsync(x => x.Deadline < DateTime.UtcNow.AddDays(-180));

        var numberOfWorkorderTasks = await _backendConfigurationPnDbContext.WorkorderCases
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .Where(x => x.CaseStatusesEnum != CaseStatusesEnum.NewTask)
            .Where(x => x.LeadingCase == true)
            .CountAsync();

        var oldestWorkorderTask = await _backendConfigurationPnDbContext.WorkorderCases
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .Where(x => x.CaseStatusesEnum != CaseStatusesEnum.Completed && x.CaseStatusesEnum != CaseStatusesEnum.NewTask)
            .Where(x => x.LeadingCase == true)
            .OrderBy(x => x.CreatedAt)
            .FirstOrDefaultAsync();

        var numberOfActiveAreaRulePlannings = _backendConfigurationPnDbContext.AreaRulePlannings
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .Select(x => x.Status)
            .Distinct()
            .ToList()
            .Count;

        var numberOfCompletedEnvironmentInspectionTagPlanningsLast30Days = _itemsPlanningPnDbContext.PlanningCases
            .Where(x => x.Status == 100)
            .Where(x => x.MicrotingSdkCaseDoneAt >= DateTime.UtcNow.AddDays(-30))
            .ToList()
            .Where(x =>
            {
                var planningTags = _itemsPlanningPnDbContext.PlanningsTags
                    .Where(y => y.PlanningId == x.PlanningId && y.PlanningTagId == envTag.Id)
                    .ToList();
                return planningTags.Any();
            })
            .Count();

        var numberOfWorkersWithTimeRegistrationEnabled = await _timePlanningPnDbContext.AssignedSites
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .Distinct().CountAsync();

        var numberOfFullDayPlanRegistrationsLastWeek = await _timePlanningPnDbContext.PlanRegistrations
            .Where(x => x.Start1StartedAt != null && x.Stop1StoppedAt != null)
            .Where(x => x.Date >= DateTime.UtcNow.AddDays(-7))
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .CountAsync();

        var totalCount = complianceList.Count();

        var statsModel = new CompliancesStatsModel
        {
            OneWeekInTheFutureCount = oneWeekInTheFutureCount,
            TodayCount = todayCount,
            TotalCount = totalCount,
            OneWeekCount = oneWeekCount,
            TwoWeeksCount = twoWeeksCount,
            OneMonthCount = oneMonthCount,
            TwoMonthsCount = twoMonthsCount,
            ThreeMonthsCount = threeMonthsCount,
            SixMonthsCount = sixMonthsCount,
            MoreThanSixMonthsCount = moreThanSixMonthsCount,
            TodayCountEnvironmentInspectionTag = todayCountEnvironmentInspectionTag,
            DateOfOldestEnvironmentInspectionTagPlannedTask = oldestEnvironmentInspectionTagPlannedTask,
            NumberOfAdHocTasks = numberOfWorkorderTasks,
            DateOfOldestAdHocTask = oldestWorkorderTask?.CreatedAt,
            NumberOfPlannedEnvironmentInspectionTagTasks = numberOfPlannedEnvironmentInspectionTagTasks,
            NumberOfPlannedTasks = numberOfActiveAreaRulePlannings,
            NumberOfCompletedEnvironmentInspectionTagPlanningsLast30Days = numberOfCompletedEnvironmentInspectionTagPlanningsLast30Days,
            NumberOfPlannedEnvironmentInspectionTagPlanningsLast30Days = numberOfPlannedEnvironmentInspectionTagPlanningsLast30Days,
            NumberOfWorkersWithTimeRegistrationEnabled = numberOfWorkersWithTimeRegistrationEnabled,
            NumberOfFullDayTimeRegistrationsLastWeek = numberOfFullDayPlanRegistrationsLastWeek
        };

        return new OperationDataResult<CompliancesStatsModel>(true, statsModel);
    }

    private async Task<PlanningCaseSite> SetFieldValue(PlanningCaseSite planningCaseSite, int caseId, Language language)
    {
        var planning = _itemsPlanningPnDbContext.Plannings
            .SingleOrDefault(x => x.Id == planningCaseSite.PlanningId);
        var caseIds = new List<int>
        {
            planningCaseSite.MicrotingSdkCaseId
        };

        var core = await _coreHelper.GetCore().ConfigureAwait(false);
        var fieldValues = await core.Advanced_FieldValueReadList(caseIds, language).ConfigureAwait(false);

        if (planning == null)
        {
            return planningCaseSite;
        }
        if (planning.NumberOfImagesEnabled)
        {
            planningCaseSite.NumberOfImages = fieldValues
                .Where(fieldValue => fieldValue.FieldType == Constants.FieldTypes.Picture)
                .Count(fieldValue => fieldValue.UploadedData != null);
        }

        return planningCaseSite;
    }

    private async Task<PlanningCase> SetFieldValue(PlanningCase planningCase, int caseId, Language language)
    {
        var core = await _coreHelper.GetCore().ConfigureAwait(false);
        var planning = await _itemsPlanningPnDbContext.Plannings
            .SingleOrDefaultAsync(x => x.Id == planningCase.PlanningId).ConfigureAwait(false);
        var caseIds = new List<int> { planningCase.MicrotingSdkCaseId };
        var fieldValues = await core.Advanced_FieldValueReadList(caseIds, language).ConfigureAwait(false);

        if (planning == null)
        {
            return planningCase;
        }
        if (planning.NumberOfImagesEnabled)
        {
            planningCase.NumberOfImages = fieldValues
                .Where(fieldValue => fieldValue.FieldType == Constants.FieldTypes.Picture)
                .Count(fieldValue => fieldValue.UploadedData != null);
        }

        return planningCase;
    }
}