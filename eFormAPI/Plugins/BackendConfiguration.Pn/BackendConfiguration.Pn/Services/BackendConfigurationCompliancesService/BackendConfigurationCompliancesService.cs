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

using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eForm.Infrastructure.Models;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Delegates.CaseUpdate;
using Microting.eFormApi.BasePn.Infrastructure.Helpers;
using Microting.eFormApi.BasePn.Infrastructure.Models.Application.Case.CaseEdit;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Data;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Enums;

namespace BackendConfiguration.Pn.Services.BackendConfigurationCompliancesService
{
    using BackendConfiguration.Pn.Infrastructure.Models.Compliances.Index;
    using Microting.eFormApi.BasePn.Infrastructure.Models.API;
    using Microting.eFormApi.BasePn.Infrastructure.Models.Common;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    public class BackendConfigurationCompliancesService : IBackendConfigurationCompliancesService
    {

        private readonly IEFormCoreService _coreHelper;
        private readonly IBackendConfigurationLocalizationService _localizationService;
        private readonly IUserService _userService;
        private readonly BackendConfigurationPnDbContext _backendConfigurationPnDbContext;
        private readonly ItemsPlanningPnDbContext _itemsPlanningPnDbContext;

        public BackendConfigurationCompliancesService(ItemsPlanningPnDbContext itemsPlanningPnDbContext, BackendConfigurationPnDbContext backendConfigurationPnDbContext, IUserService userService, IBackendConfigurationLocalizationService localizationService, IEFormCoreService coreHelper)
        {
            _itemsPlanningPnDbContext = itemsPlanningPnDbContext;
            _backendConfigurationPnDbContext = backendConfigurationPnDbContext;
            _userService = userService;
            _localizationService = localizationService;
            _coreHelper = coreHelper;
        }

        public async Task<OperationDataResult<Paged<CompliancesModel>>> Index(CompliancesRequestModel request)
        {
            var language = await _userService.GetCurrentUserLanguage();
            Paged<CompliancesModel> result = new Paged<CompliancesModel>
            {
                Entities = new List<CompliancesModel>()
            };

            var core = await _coreHelper.GetCore();
            await using var sdkDbContext = core.DbContextHelper.GetDbContext();

            var complianceList = _backendConfigurationPnDbContext.Compliances
                .Where(x => x.PropertyId == request.PropertyId)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed);

            if (request.Days > 0)
            {
                complianceList = complianceList.Where(x => x.Deadline <= DateTime.Now.AddDays(request.Days));
            }

            var theList = await complianceList.AsNoTracking()
                .OrderBy(x => x.Deadline)
                .ToListAsync();

            foreach (Compliance compliance in theList)
            {
                var planningNameTranslation = await _itemsPlanningPnDbContext.PlanningNameTranslation.SingleOrDefaultAsync(x => x.PlanningId == compliance.PlanningId && x.LanguageId == language.Id);

                if (planningNameTranslation == null)
                {
                    continue;
                }
                var areaTranslation = await _backendConfigurationPnDbContext.AreaTranslations.SingleOrDefaultAsync(x => x.AreaId == compliance.AreaId && x.LanguageId == language.Id);

                if (areaTranslation == null)
                {
                    continue;
                }

                var planningSites = await _itemsPlanningPnDbContext.PlanningSites
                .Where(x => x.PlanningId == compliance.PlanningId)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Select(x => x.SiteId).Distinct().ToListAsync();

                List<KeyValuePair<int, string>> responsible = new List<KeyValuePair<int, string>>();

                var sitesList = await sdkDbContext.Sites.Where(x => planningSites.Contains(x.Id)).ToListAsync();

                foreach (var site in sitesList)
                {
                    var kvp = new KeyValuePair<int,string>(site.Id, site.Name);
                    responsible.Add(kvp);
                }

                var dbCompliance = _backendConfigurationPnDbContext.Compliances.Single(x => x.Id == compliance.Id);
                if (result.Entities.Any(x => x.PlanningId == compliance.PlanningId && x.Deadline == compliance.Deadline.AddDays(-1)))
                {
                    await dbCompliance.Delete(_backendConfigurationPnDbContext);
                }
                else
                {
                    CompliancesModel complianceModel = new CompliancesModel
                    {
                        CaseId = compliance.MicrotingSdkCaseId,
                        Deadline = compliance.Deadline.AddDays(-1),
                        ComplianceTypeId = null,
                        ControlArea = areaTranslation.Name,
                        EformId = compliance.MicrotingSdkeFormId,
                        Id = compliance.Id,
                        ItemName = planningNameTranslation.Name,
                        PlanningId = compliance.PlanningId,
                        Responsible = responsible,
                    };
                    if (complianceModel.CaseId == 0 && complianceModel.Deadline < DateTime.UtcNow)
                    {
                        if (dbCompliance.MicrotingSdkeFormId == 0)
                        {
                            var planning = await _itemsPlanningPnDbContext.Plannings.SingleAsync(x => x.Id == complianceModel.PlanningId);
                            dbCompliance.MicrotingSdkeFormId = planning.RelatedEFormId;
                        }
                        Case caseEntity = new Case()
                        {
                            CheckListId = dbCompliance.MicrotingSdkeFormId,
                        };

                        await caseEntity.Create(sdkDbContext);
                        complianceModel.CaseId = caseEntity.Id;
                        dbCompliance.MicrotingSdkCaseId = caseEntity.Id;
                        await dbCompliance.Update(_backendConfigurationPnDbContext);
                    }
                    result.Entities.Add(complianceModel);
                }
            }

            return new OperationDataResult<Paged<CompliancesModel>>(true, result);
        }

        public async Task<OperationDataResult<int>> ComplianceStatus(int propertyId)
        {
            var compliance = await Index(new CompliancesRequestModel
            {
                PropertyId = propertyId
            });

            if (compliance.Model.Entities.Count == 0)
            {
                return new OperationDataResult<int>(true, 0);
            } else {
                return new OperationDataResult<int>(true, 1);
            }
        }

        public async Task<OperationDataResult<ReplyElement>> Read(int id)
        {
            try
            {
                var core = await _coreHelper.GetCore();
                // var sdkDbContext = core.DbContextHelper.GetDbContext();
                // var caseDto = await sdkDbContext.Cases.SingleOrDefaultAsync(x => x.Id == id);
                // if (caseDto == null)
                // {
                    // return new OperationDataResult<ReplyElement>(false, _localizationService.GetString("CaseNotFound"));
                // }
                var language = await _userService.GetCurrentUserLanguage();
                var theCase = await core.CaseRead(id, language);
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
            var core = await _coreHelper.GetCore();
            var language = await _userService.GetCurrentUserLanguage();
            var currentUser = await _userService.GetCurrentUserAsync();
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
                var compliance = await _backendConfigurationPnDbContext.Compliances.SingleOrDefaultAsync(x => x.Id == model.ExtraId);
                if (compliance != null)
                {
                    await compliance.Delete(_backendConfigurationPnDbContext);
                }
                else
                {
                    return new OperationResult(false, $"{_localizationService.GetString("CaseCouldNotBeUpdated")}");
                }


                await core.CaseUpdate(model.Id, fieldValueList, checkListValueList);
                await core.CaseUpdateFieldValues(model.Id, language);

                var sdkDbContext = core.DbContextHelper.GetDbContext();

                var foundCase = await sdkDbContext.Cases
                    .Where(x => x.Id == model.Id
                                && x.WorkflowState != Constants.WorkflowStates.Removed)
                    .FirstOrDefaultAsync();

                if(foundCase != null) {
                    var now = DateTime.UtcNow;
                    var newDoneAt = new DateTime(model.DoneAt.AddDays(1).Year, model.DoneAt.AddDays(1).Month,
                        model.DoneAt.AddDays(1).Day, now.Hour, now.Minute,
                        now.Second, DateTimeKind.Utc);
                    foundCase.DoneAtUserModifiable = newDoneAt;
                    foundCase.DoneAt = newDoneAt;

                    foundCase.SiteId = sdkDbContext.Sites
                        .Single(x => x.Name == $"{currentUser.FirstName} {currentUser.LastName}").Id;
                    foundCase.Status = 100;
                    await foundCase.Update(sdkDbContext);

                    if (CaseUpdateDelegates.CaseUpdateDelegate != null)
                    {
                        var invocationList = CaseUpdateDelegates.CaseUpdateDelegate
                            .GetInvocationList();
                        foreach (var func in invocationList)
                        {
                            func.DynamicInvoke(model.Id);
                        }
                    }
                    if (compliance.PlanningCaseSiteId != 0)
                    {
                        var planningCaseSite = await _itemsPlanningPnDbContext.PlanningCaseSites.SingleOrDefaultAsync(x => x.Id == compliance.PlanningCaseSiteId);
                        if (planningCaseSite != null)
                        {
                            planningCaseSite.Status = 100;
                            planningCaseSite = await SetFieldValue(planningCaseSite, foundCase.Id, language);

                            planningCaseSite.MicrotingSdkCaseDoneAt = newDoneAt;
                            planningCaseSite.DoneByUserId = (int)foundCase.SiteId;
                            planningCaseSite.DoneByUserName = $"{currentUser.FirstName} {currentUser.LastName}";
                            await planningCaseSite.Update(_itemsPlanningPnDbContext);

                            var planningCase = await _itemsPlanningPnDbContext.PlanningCases.SingleAsync(x => x.Id == planningCaseSite.PlanningCaseId);
                            if (planningCase.Status != 100)
                            {
                                planningCase.Status = 100;
                                planningCase.MicrotingSdkCaseDoneAt = newDoneAt;
                                planningCase.MicrotingSdkCaseId = foundCase.Id;
                                planningCase.DoneByUserId = (int)foundCase.SiteId;
                                planningCase.DoneByUserName = planningCaseSite.DoneByUserName;
                                planningCase.WorkflowState = Constants.WorkflowStates.Processed;

                                planningCase = await SetFieldValue(planningCase, foundCase.Id, language);
                                await planningCase.Update(_itemsPlanningPnDbContext);
                            }
                        }
                    }
                    else
                    {
                        var planningCaseSite = await _itemsPlanningPnDbContext.PlanningCaseSites.SingleOrDefaultAsync(x => x.CreatedAt.Date == compliance.StartDate.Date && x.PlanningId == compliance.PlanningId);
                        if (planningCaseSite != null)
                        {
                            planningCaseSite.Status = 100;
                            planningCaseSite = await SetFieldValue(planningCaseSite, foundCase.Id, language);

                            planningCaseSite.MicrotingSdkCaseId = foundCase.Id;
                            planningCaseSite.MicrotingSdkCaseDoneAt = foundCase.DoneAt;
                            planningCaseSite.DoneByUserId = (int)foundCase.SiteId;
                            planningCaseSite.DoneByUserName = $"{currentUser.FirstName} {currentUser.LastName}";
                            await planningCaseSite.Update(_itemsPlanningPnDbContext);

                            var planningCase = await _itemsPlanningPnDbContext.PlanningCases.SingleAsync(x => x.Id == planningCaseSite.PlanningCaseId);
                            if (planningCase.Status != 100)
                            {
                                planningCase.Status = 100;
                                planningCase.MicrotingSdkCaseDoneAt = foundCase.DoneAt;
                                planningCase.MicrotingSdkCaseId = foundCase.Id;
                                planningCase.DoneByUserId = (int)foundCase.SiteId;
                                planningCase.DoneByUserName = planningCaseSite.DoneByUserName;
                                planningCase.WorkflowState = Constants.WorkflowStates.Processed;

                                planningCase = await SetFieldValue(planningCase, foundCase.Id, language);
                                await planningCase.Update(_itemsPlanningPnDbContext);
                            }
                        }
                    }
                }
                else
                {
                    return new OperationResult(false, _localizationService.GetString("CaseNotFound"));
                }

                var property = await _backendConfigurationPnDbContext.Properties.SingleAsync(x => x.Id == compliance.PropertyId);

                if (_backendConfigurationPnDbContext.Compliances.Any(x =>
                        x.PropertyId == property.Id && x.WorkflowState != Constants.WorkflowStates.Removed))
                {
                    if (property is {ComplianceStatus: 0})
                    {
                        property.ComplianceStatus = 1;
                        await property.Update(_backendConfigurationPnDbContext);
                    }
                    else
                    {
                        if (_backendConfigurationPnDbContext.Compliances.Any(x => x.Deadline < DateTime.UtcNow && x.PropertyId == property.Id && x.WorkflowState != Constants.WorkflowStates.Removed))
                        {
                            property.ComplianceStatus = 2;
                            property.ComplianceStatusThirty = 2;
                            await property.Update(_backendConfigurationPnDbContext);
                        }
                        else
                        {
                            property.ComplianceStatus = 1;
                            property.ComplianceStatusThirty = 1;
                            await property.Update(_backendConfigurationPnDbContext);
                        }
                    }

                    if (property is {ComplianceStatusThirty: 0})
                    {
                        if (_backendConfigurationPnDbContext.Compliances.Any(x =>
                                x.Deadline < DateTime.UtcNow.AddDays(30) && x.PropertyId == property.Id &&
                                x.WorkflowState != Constants.WorkflowStates.Removed))
                        {
                            property.ComplianceStatusThirty = 1;
                            await property.Update(_backendConfigurationPnDbContext);
                        }
                    }
                    else
                    {
                        if (!_backendConfigurationPnDbContext.Compliances.Any(x =>
                                x.Deadline < DateTime.UtcNow.AddDays(30) && x.PropertyId == property.Id &&
                                x.WorkflowState != Constants.WorkflowStates.Removed))
                        {
                            property.ComplianceStatusThirty = 0;
                            await property.Update(_backendConfigurationPnDbContext);
                        }
                    }
                }
                else
                {
                    property.ComplianceStatus = 0;
                    property.ComplianceStatusThirty = 0;
                    await property.Update(_backendConfigurationPnDbContext);
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

        private async Task<PlanningCaseSite> SetFieldValue(PlanningCaseSite planningCaseSite, int caseId, Language language)
        {
            var planning = _itemsPlanningPnDbContext.Plannings.SingleOrDefault(x => x.Id == planningCaseSite.PlanningId);
            var caseIds = new List<int>
            {
                planningCaseSite.MicrotingSdkCaseId
            };

            var core = await _coreHelper.GetCore();
            var fieldValues = await core.Advanced_FieldValueReadList(caseIds, language);

            if (planning == null) return planningCaseSite;
            if (planning.NumberOfImagesEnabled)
            {
                planningCaseSite.NumberOfImages = 0;
                foreach (var fieldValue in fieldValues)
                {
                    if (fieldValue.FieldType == Constants.FieldTypes.Picture)
                    {
                        if (fieldValue.UploadedData != null)
                        {
                            planningCaseSite.NumberOfImages += 1;
                        }
                    }
                }
            }

            return planningCaseSite;
        }

        private async Task<PlanningCase> SetFieldValue(PlanningCase planningCase, int caseId, Language language)
        {
            var core = await _coreHelper.GetCore();
            var planning = await _itemsPlanningPnDbContext.Plannings.SingleOrDefaultAsync(x => x.Id == planningCase.PlanningId).ConfigureAwait(false);
            var caseIds = new List<int> { planningCase.MicrotingSdkCaseId };
            var fieldValues = await core.Advanced_FieldValueReadList(caseIds, language).ConfigureAwait(false);

            if (planning == null) return planningCase;
            if (planning.NumberOfImagesEnabled)
            {
                planningCase.NumberOfImages = 0;
                foreach (var fieldValue in fieldValues)
                {
                    if (fieldValue.FieldType == Constants.FieldTypes.Picture)
                    {
                        if (fieldValue.UploadedData != null)
                        {
                            planningCase.NumberOfImages += 1;
                        }
                    }
                }
            }

            return planningCase;
        }
    }
}