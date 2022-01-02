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
using Microting.eFormApi.BasePn.Abstractions;
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
        private readonly IBackendConfigurationLocalizationService _backendConfigurationLocalizationService;
        private readonly IUserService _userService;
        private readonly BackendConfigurationPnDbContext _backendConfigurationPnDbContext;
        private readonly ItemsPlanningPnDbContext _itemsPlanningPnDbContext;

        public BackendConfigurationCompliancesService(ItemsPlanningPnDbContext itemsPlanningPnDbContext, BackendConfigurationPnDbContext backendConfigurationPnDbContext, IUserService userService, IBackendConfigurationLocalizationService backendConfigurationLocalizationService, IEFormCoreService coreHelper)
        {
            _itemsPlanningPnDbContext = itemsPlanningPnDbContext;
            _backendConfigurationPnDbContext = backendConfigurationPnDbContext;
            _userService = userService;
            _backendConfigurationLocalizationService = backendConfigurationLocalizationService;
            _coreHelper = coreHelper;
        }

        public async Task<OperationDataResult<Paged<CompliancesModel>>> Index(CompliancesRequestModel request)
        {
            var language = await _userService.GetCurrentUserLanguage();
            Paged<CompliancesModel> result = new Paged<CompliancesModel>
            {
                Entities = new List<CompliancesModel>()
            };

            var backendPlannings = await _backendConfigurationPnDbContext.AreaRulePlannings.Where(x => x.PropertyId == request.PropertyId).ToListAsync();

            result.Total = backendPlannings.Count;

            var core = await _coreHelper.GetCore();
            await using var dbContext = core.DbContextHelper.GetDbContext();

            var preList = new List<CompliancesModel>();

            foreach (AreaRulePlanning areaRulePlanning in backendPlannings)
            {
                var planningCases =
                    await _itemsPlanningPnDbContext.PlanningCases
                        .Where(x => x.PlanningId == areaRulePlanning.ItemPlanningId)
                        .Where(x => x.Status != 100)
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Retracted)
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .ToListAsync();

                foreach (PlanningCase planningCase in planningCases)
                {
                    var planning =
                        await _itemsPlanningPnDbContext.Plannings.Where(x =>
                            x.Id == planningCase.PlanningId)
                            .Where(x => x.RepeatEvery != 1 && x.RepeatType != RepeatType.Day)
                            .Where(x => x.RepeatEvery != 0 && x.RepeatType != RepeatType.Day)
                            .Where(x => x.StartDate < DateTime.UtcNow)
                            .SingleOrDefaultAsync();

                    if (planning == null)
                    {
                        continue;
                    }

                    var planningNameTranslation = await _itemsPlanningPnDbContext.PlanningNameTranslation.SingleOrDefaultAsync(x => x.PlanningId == planningCase.PlanningId && x.LanguageId == language.Id);

                    if (planningNameTranslation == null)
                    {
                        continue;
                    }

                    var areaTranslation = await _backendConfigurationPnDbContext.AreaTranslations.SingleOrDefaultAsync(x => x.AreaId == areaRulePlanning.AreaId && x.LanguageId == language.Id);

                    var planningSites = await _backendConfigurationPnDbContext.PlanningSites
                        .Where(x => x.AreaRulePlanningsId == areaRulePlanning.Id && x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Select(x => x.SiteId).ToListAsync();

                    var responsible = String.Join("<br>", dbContext.Sites.Where(x => planningSites.Contains(x.Id)).Select(x => x.Name).ToList());

                    // var site = await dbContext.Sites.SingleOrDefaultAsync(x => x.Id == planningCase.MicrotingSdkSiteId);

                    CompliancesModel complianceModel = new CompliancesModel
                    {
                        CaseId = planningCase.MicrotingSdkCaseId,
                        Deadline = planning.NextExecutionTime?.AddDays(-1),
                        ComplianceTypeId = null,
                        ControlArea = areaTranslation.Name,
                        EformId = planningCase.MicrotingSdkeFormId,
                        Id = planning.Id,
                        ItemName = planningNameTranslation.Name,
                        PlanningId = planningCase.PlanningId,
                        Responsible = responsible,
                    };
                    preList.Add(complianceModel);
                }
            }
            foreach (CompliancesModel compliancesModel in preList.OrderBy(x => x.Deadline))
            {
                result.Entities.Add(compliancesModel);
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
    }
}