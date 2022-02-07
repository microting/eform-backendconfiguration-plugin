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
                    if (complianceModel.CaseId == 0 && complianceModel.Deadline < DateTime.Now)
                    {
                        Case caseEntity = new Case()
                        {
                            CheckListId = complianceModel.EformId,
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
                await core.CaseUpdate(model.Id, fieldValueList, checkListValueList);
                await core.CaseUpdateFieldValues(model.Id, language);

                var sdkDbContext = core.DbContextHelper.GetDbContext();

                var foundCase = await sdkDbContext.Cases
                    .Where(x => x.Id == model.Id
                                && x.WorkflowState != Constants.WorkflowStates.Removed)
                    .FirstOrDefaultAsync();

                if(foundCase != null) {
                    var now = DateTime.UtcNow;
                    var newDoneAt = new DateTime(model.DoneAt.Year, model.DoneAt.Month,
                        model.DoneAt.AddDays(1).Day, now.Hour, now.Minute,
                        now.Second);
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
                }
                else
                {
                    return new OperationResult(false, _localizationService.GetString("CaseNotFound"));
                }

                var compliance = await _backendConfigurationPnDbContext.Compliances.SingleOrDefaultAsync(x => x.Id == model.ExtraId);
                if (compliance != null)
                {
                    await compliance.Delete(_backendConfigurationPnDbContext);
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
    }
}