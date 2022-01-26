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

            var core = await _coreHelper.GetCore();
            await using var dbContext = core.DbContextHelper.GetDbContext();

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

                var sitesList = await dbContext.Sites.Where(x => planningSites.Contains(x.Id)).ToListAsync();

                foreach (var site in sitesList)
                {
                    var kvp = new KeyValuePair<int,string>(site.Id, site.Name);
                    responsible.Add(kvp);
                }

                if (result.Entities.Any(x => x.PlanningId == compliance.PlanningId && x.Deadline == compliance.Deadline.AddDays(-1)))
                {
                    var dbCompliance = _backendConfigurationPnDbContext.Compliances.Single(x => x.Id == compliance.Id);
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
                        Id = compliance.PlanningId,
                        ItemName = planningNameTranslation.Name,
                        PlanningId = compliance.PlanningId,
                        Responsible = responsible,
                    };
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
    }
}