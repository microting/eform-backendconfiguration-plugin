using System.Text.RegularExpressions;
using BackendConfiguration.Pn.Infrastructure.Helpers;
using eFormCore;
using Microsoft.Extensions.DependencyInjection;
using Microting.eForm.Dto;
using Microting.eForm.Infrastructure.Data.Entities;

namespace BackendConfiguration.Pn.Services.BackendConfigurationAreaRulePlanningsService
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using BackendConfigurationLocalizationService;
    using Infrastructure;
    using Infrastructure.Models.AreaRules;
    using Microsoft.EntityFrameworkCore;
    using Microting.eForm.Infrastructure;
    using Microting.eForm.Infrastructure.Constants;
    using Microting.eForm.Infrastructure.Models;
    using Microting.eFormApi.BasePn.Abstractions;
    using Microting.eFormApi.BasePn.Infrastructure.Helpers;
    using Microting.eFormApi.BasePn.Infrastructure.Models.API;
    using Microting.eFormApi.BasePn.Infrastructure.Models.Common;
    using Microting.EformBackendConfigurationBase.Infrastructure.Data;
    using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
    using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
    using Microting.ItemsPlanningBase.Infrastructure.Data;
    using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
    using Microting.ItemsPlanningBase.Infrastructure.Enums;
    using CommonTranslationsModel = Microting.eForm.Infrastructure.Models.CommonTranslationsModel;
    using PlanningSite = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.PlanningSite;

    public class BackendConfigurationAreaRulePlanningsService  : IBackendConfigurationAreaRulePlanningsService
    {
        private readonly IEFormCoreService _coreHelper;
        private readonly IBackendConfigurationLocalizationService _backendConfigurationLocalizationService;
        private readonly IUserService _userService;
        private readonly BackendConfigurationPnDbContext _backendConfigurationPnDbContext;
        private readonly ItemsPlanningPnDbContext _itemsPlanningPnDbContext;

        public BackendConfigurationAreaRulePlanningsService(
            IEFormCoreService coreHelper,
            IUserService userService,
            BackendConfigurationPnDbContext backendConfigurationPnDbContext,
            IBackendConfigurationLocalizationService backendConfigurationLocalizationService,
            ItemsPlanningPnDbContext itemsPlanningPnDbContext)
        {
            _coreHelper = coreHelper;
            _userService = userService;
            _backendConfigurationLocalizationService = backendConfigurationLocalizationService;
            _backendConfigurationPnDbContext = backendConfigurationPnDbContext;
            _itemsPlanningPnDbContext = itemsPlanningPnDbContext;
        }

        public async Task<OperationResult> UpdatePlanning(AreaRulePlanningModel areaRulePlanningModel)
        {
            try
            {
                var core = await _coreHelper.GetCore().ConfigureAwait(false);
                var sdkDbContext = core.DbContextHelper.GetDbContext();
                areaRulePlanningModel.AssignedSites =
                    areaRulePlanningModel.AssignedSites.Where(x => x.Checked).ToList();

                if (areaRulePlanningModel.TypeSpecificFields != null)
                {
                    if (areaRulePlanningModel.TypeSpecificFields.RepeatType == 1 && areaRulePlanningModel.TypeSpecificFields.RepeatEvery == 1)
                    {
                        areaRulePlanningModel.TypeSpecificFields.RepeatEvery = 0;
                    }
                }

                if (areaRulePlanningModel.Id.HasValue) // update planning
                {
                    var areaRule = await _backendConfigurationPnDbContext.AreaRules
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Where(x => x.Id == areaRulePlanningModel.RuleId)
                        .Include(x => x.AreaRulesPlannings)
                        .ThenInclude(x => x.PlanningSites)
                        .Include(x => x.Area)
                        .Include(x => x.AreaRuleTranslations)
                        .FirstAsync().ConfigureAwait(false);

                    var property = await _backendConfigurationPnDbContext.Properties.SingleAsync(x => x.Id == areaRule.PropertyId).ConfigureAwait(false);

                    if (areaRule.Area.Type == AreaTypesEnum.Type9)
                    {
                        var oldStatus = areaRule.AreaRulesPlannings.Last(x => x.WorkflowState != Constants.WorkflowStates.Removed).Status;
                        if (areaRulePlanningModel.Status && !oldStatus)
                        {
                            foreach (AreaRulePlanning areaRuleAreaRulesPlanning in areaRule.AreaRulesPlannings)
                            {
                                await areaRuleAreaRulesPlanning.Delete(_backendConfigurationPnDbContext).ConfigureAwait(false);
                            }

                            await BackendConfigurationAreaRulePlanningsServiceHelper.CreatePlanningType9(areaRule, sdkDbContext, areaRulePlanningModel, core, _userService.UserId, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext).ConfigureAwait(false);
                        }
                        if (!areaRulePlanningModel.Status && oldStatus)
                        {
                            var arps = areaRule.AreaRulesPlannings.Join(_backendConfigurationPnDbContext.PlanningSites
                                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed),
                            arp => arp.Id, planningSite => planningSite.AreaRulePlanningsId, (arp, planningSite) =>
                                new
                                {
                                    arp.Id,
                                    PlanningSiteId = planningSite.Id,
                                    planningSite.SiteId,
                                    arp.ItemPlanningId
                                }).ToList();
                            foreach (var arp in arps)
                            {
                                var planning = await _itemsPlanningPnDbContext.Plannings
                                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                    .Where(x => x.Id == arp.ItemPlanningId)
                                    .Include(x => x.PlanningSites)
                                    .FirstOrDefaultAsync().ConfigureAwait(false);

                                if (planning != null)
                                {
                                    foreach (var planningSite in planning.PlanningSites
                                                 .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed))
                                    {
                                        await planningSite.Delete(_itemsPlanningPnDbContext).ConfigureAwait(false);
                                        var someList = await _itemsPlanningPnDbContext.PlanningCaseSites
                                            .Where(x => x.PlanningId == planning.Id)
                                            .Where(x => x.MicrotingSdkSiteId == planningSite.SiteId)
                                            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                            .ToListAsync().ConfigureAwait(false);

                                        foreach (var planningCaseSite in someList)
                                        {
                                            var result =
                                                await sdkDbContext.Cases.SingleOrDefaultAsync(x =>
                                                    x.Id == planningCaseSite.MicrotingSdkCaseId).ConfigureAwait(false);
                                            if (result is { MicrotingUid: { } })
                                            {
                                                await core.CaseDelete((int)result.MicrotingUid).ConfigureAwait(false);
                                            }
                                            else
                                            {
                                                var clSites = await sdkDbContext.CheckListSites.SingleAsync(x =>
                                                    x.Id == planningCaseSite.MicrotingCheckListSitId).ConfigureAwait(false);

                                                await core.CaseDelete(clSites.MicrotingUid).ConfigureAwait(false);
                                            }
                                        }
                                    }
                                    await planning.Delete(_itemsPlanningPnDbContext).ConfigureAwait(false);
                                }
                                var areaRulePlanning = _backendConfigurationPnDbContext.AreaRulePlannings
                                    .Single(x => x.Id == arp.Id);
                                areaRulePlanning.Status = false;
                                await areaRulePlanning.Update(_backendConfigurationPnDbContext).ConfigureAwait(false);
                            }

                            foreach (AreaRulePlanning areaRuleAreaRulesPlanning in areaRule.AreaRulesPlannings)
                            {
                                areaRuleAreaRulesPlanning.ItemPlanningId = 0;
                                areaRuleAreaRulesPlanning.Status = false;
                                await areaRuleAreaRulesPlanning.Update(_backendConfigurationPnDbContext).ConfigureAwait(false);
                            }
                        }
                    }
                    else
                    {
                        if (areaRule.Area.Type == AreaTypesEnum.Type10)
                        {
                            var oldStatus = areaRule.AreaRulesPlannings
                                .Last(x => x.WorkflowState != Constants.WorkflowStates.Removed).Status;
                            var currentPlanningSites = await _backendConfigurationPnDbContext.PlanningSites
                                .Where(x => x.AreaRuleId == areaRulePlanningModel.RuleId)
                                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                .Select(x => x.SiteId).Distinct()
                                .ToListAsync().ConfigureAwait(false);
                            var forDelete = currentPlanningSites
                                .Except(areaRulePlanningModel.AssignedSites.Select(x => x.SiteId)).ToList();
                            var forAdd = areaRulePlanningModel.AssignedSites.Select(x => x.SiteId)
                                .Except(currentPlanningSites).ToList();
                            if (areaRulePlanningModel.Status && oldStatus)
                            {
                                var areaRulePlannings = areaRule.AreaRulesPlannings.Join(
                                    _backendConfigurationPnDbContext
                                        .PlanningSites
                                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed),
                                    arp => arp.Id, planningSite => planningSite.AreaRulePlanningsId,
                                    (arp, planningSite) =>
                                        new
                                        {
                                            arp.Id,
                                            PlanningSiteId = planningSite.Id,
                                            planningSite.SiteId,
                                            arp.ItemPlanningId
                                        }).ToList();

                                foreach (var i in forDelete)
                                {
                                    var planningSiteId = areaRulePlannings.Single(y => y.SiteId == i).PlanningSiteId;
                                    var itemsPlanningId = areaRulePlannings.Single(x => x.SiteId == i).ItemPlanningId;
                                    var backendPlanningSite = await _backendConfigurationPnDbContext.PlanningSites
                                        .SingleAsync(x => x.Id == planningSiteId).ConfigureAwait(false);
                                    await backendPlanningSite.Delete(_backendConfigurationPnDbContext).ConfigureAwait(false);
                                    var planning = await _itemsPlanningPnDbContext.Plannings
                                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                        .Where(x => x.Id == itemsPlanningId)
                                        .Include(x => x.PlanningSites)
                                        .FirstOrDefaultAsync().ConfigureAwait(false);

                                    if (planning != null)
                                    {
                                        foreach (var planningSite in planning.PlanningSites
                                                     .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed))
                                        {
                                            if (forDelete.Contains(planningSite.SiteId))
                                            {
                                                await planningSite.Delete(_itemsPlanningPnDbContext).ConfigureAwait(false);
                                                var someList = await _itemsPlanningPnDbContext.PlanningCaseSites
                                                    .Where(x => x.PlanningId == planning.Id)
                                                    .Where(x => x.MicrotingSdkSiteId == planningSite.SiteId)
                                                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                                    .ToListAsync().ConfigureAwait(false);

                                                foreach (var planningCaseSite in someList)
                                                {
                                                    var result =
                                                        await sdkDbContext.Cases.SingleOrDefaultAsync(x =>
                                                            x.Id == planningCaseSite.MicrotingSdkCaseId).ConfigureAwait(false);
                                                    if (result != null)
                                                    {
                                                        await core.CaseDelete((int) result.MicrotingUid).ConfigureAwait(false);
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }

                                foreach (var areaRulePlanning in areaRule.AreaRulesPlannings.Where(x => x.WorkflowState != Constants.WorkflowStates.Removed))
                                {
                                    var clId = sdkDbContext.CheckListTranslations
                                        .Where(x => x.Text == $"02. Fækale uheld - {property.Name}")
                                        .Select(x => x.CheckListId).FirstOrDefault();

                                    foreach (int i in forAdd)
                                    {
                                        var siteForCreate = new PlanningSite
                                        {
                                            AreaRulePlanningsId = areaRulePlanning.Id,
                                            SiteId = i,
                                            CreatedByUserId = _userService.UserId,
                                            UpdatedByUserId = _userService.UserId,
                                            AreaId = areaRule.AreaId,
                                            AreaRuleId = areaRule.Id,
                                        };
                                        await siteForCreate.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);
                                        var planningSite =
                                            new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.
                                                PlanningSite
                                                {
                                                    SiteId = i,
                                                    PlanningId = areaRulePlanning.ItemPlanningId,
                                                    CreatedByUserId = _userService.UserId,
                                                    UpdatedByUserId = _userService.UserId,
                                                };
                                        await planningSite.Create(_itemsPlanningPnDbContext).ConfigureAwait(false);
                                        var planning = await _itemsPlanningPnDbContext.Plannings
                                            .Where(x => x.Id == areaRulePlanning.ItemPlanningId)
                                            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                            .Include(x => x.NameTranslations)
                                            .Select(x => new
                                            {
                                                x.Id, x.Type, x.PlanningNumber, x.BuildYear, x.StartDate,
                                                x.PushMessageOnDeployment,
                                                x.SdkFolderId, x.NameTranslations, x.RepeatEvery, x.RepeatType,
                                                x.RelatedEFormId
                                            })
                                            .FirstAsync().ConfigureAwait(false);

                                        if (planning.RelatedEFormId == clId || areaRule.SecondaryeFormName == "Morgenrundtur")
                                        {
                                            var sdkSite = await sdkDbContext.Sites.SingleAsync(x => x.Id == i).ConfigureAwait(false);
                                            var language =
                                                await sdkDbContext.Languages.SingleAsync(x => x.Id == sdkSite.LanguageId).ConfigureAwait(false);
                                            var mainElement = await core.ReadeForm(planning.RelatedEFormId, language).ConfigureAwait(false);
                                            var translation = planning.NameTranslations
                                            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                            .Where(x => x.LanguageId == language.Id)
                                            .Select(x => x.Name)
                                            .FirstOrDefault();
                                            var planningCase = await _itemsPlanningPnDbContext.PlanningCases
                                                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                                .Where(x => x.WorkflowState != Constants.WorkflowStates.Retracted)
                                                .Where(x => x.PlanningId == planning.Id)
                                                .Where(x => x.MicrotingSdkeFormId == planning.RelatedEFormId)
                                                .FirstOrDefaultAsync().ConfigureAwait(false);

                                            if (planningCase == null)
                                            {
                                                planningCase = new PlanningCase
                                                {
                                                    PlanningId = planning.Id,
                                                    Status = 66,
                                                    MicrotingSdkeFormId = planning.RelatedEFormId
                                                };
                                                await planningCase.Create(_itemsPlanningPnDbContext).ConfigureAwait(false);
                                            }

                                            mainElement.Label = string.IsNullOrEmpty(planning.PlanningNumber)
                                                ? ""
                                                : planning.PlanningNumber;
                                            if (!string.IsNullOrEmpty(translation))
                                            {
                                                mainElement.Label +=
                                                    string.IsNullOrEmpty(mainElement.Label)
                                                        ? $"{translation}"
                                                        : $" - {translation}";
                                            }

                                            if (!string.IsNullOrEmpty(planning.BuildYear))
                                            {
                                                mainElement.Label += string.IsNullOrEmpty(mainElement.Label)
                                                    ? $"{planning.BuildYear}"
                                                    : $" - {planning.BuildYear}";
                                            }

                                            if (!string.IsNullOrEmpty(planning.Type))
                                            {
                                                mainElement.Label += string.IsNullOrEmpty(mainElement.Label)
                                                    ? $"{planning.Type}"
                                                    : $" - {planning.Type}";
                                            }

                                            if (mainElement.ElementList.Count == 1)
                                            {
                                                mainElement.ElementList[0].Label = mainElement.Label;
                                            }

                                            var folder =
                                                await sdkDbContext.Folders.SingleAsync(x => x.Id == planning.SdkFolderId).ConfigureAwait(false);
                                            var folderMicrotingId = folder.MicrotingUid.ToString();

                                            mainElement.CheckListFolderName = folderMicrotingId;
                                            mainElement.StartDate = DateTime.Now.ToUniversalTime();
                                            mainElement.EndDate = DateTime.Now.AddYears(10).ToUniversalTime();
                                            var planningCaseSite = new PlanningCaseSite
                                            {
                                                MicrotingSdkSiteId = i,
                                                MicrotingSdkeFormId = planning.RelatedEFormId,
                                                Status = 66,
                                                PlanningId = planning.Id,
                                                PlanningCaseId = planningCase.Id
                                            };

                                            await planningCaseSite.Create(_itemsPlanningPnDbContext).ConfigureAwait(false);
                                            mainElement.Repeated = 1;
                                            var caseId = await core.CaseCreate(mainElement, "", (int) sdkSite.MicrotingUid,
                                                null).ConfigureAwait(false);
                                            if (caseId != null)
                                            {
                                                if (sdkDbContext.Cases.Any(x => x.MicrotingUid == caseId))
                                                {
                                                    planningCaseSite.MicrotingSdkCaseId =
                                                        sdkDbContext.Cases.Single(x => x.MicrotingUid == caseId).Id;
                                                }
                                                else
                                                {
                                                    planningCaseSite.MicrotingCheckListSitId =
                                                        sdkDbContext.CheckListSites.Single(x => x.MicrotingUid == caseId)
                                                            .Id;
                                                }

                                                await planningCaseSite.Update(_itemsPlanningPnDbContext).ConfigureAwait(false);
                                            }
                                        }
                                    }
                                }
                            }
                            else
                            {
                                if (areaRulePlanningModel.Status && !oldStatus)
                                {
                                    foreach (AreaRulePlanning areaRuleAreaRulesPlanning in areaRule.AreaRulesPlannings)
                                    {
                                        await areaRuleAreaRulesPlanning.Delete(_backendConfigurationPnDbContext).ConfigureAwait(false);
                                    }

                                    await BackendConfigurationAreaRulePlanningsServiceHelper.CreatePlanningType10(areaRule, sdkDbContext, areaRulePlanningModel, core, _userService.UserId, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext).ConfigureAwait(false);
                                }

                                if (!areaRulePlanningModel.Status && oldStatus)
                                {

                                    var arps = areaRule.AreaRulesPlannings.Join(_backendConfigurationPnDbContext
                                            .PlanningSites
                                            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed),
                                        arp => arp.Id, planningSite => planningSite.AreaRulePlanningsId,
                                        (arp, planningSite) =>
                                            new
                                            {
                                                arp.Id,
                                                PlanningSiteId = planningSite.Id,
                                                planningSite.SiteId,
                                                arp.ItemPlanningId
                                            }).ToList();
                                    foreach (var arp in arps)
                                    {
                                        var planning = await _itemsPlanningPnDbContext.Plannings
                                            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                            .Where(x => x.Id == arp.ItemPlanningId)
                                            .Include(x => x.PlanningSites)
                                            .FirstOrDefaultAsync().ConfigureAwait(false);

                                        if (planning != null)
                                        {
                                            foreach (var planningSite in planning.PlanningSites
                                                         .Where(
                                                             x => x.WorkflowState != Constants.WorkflowStates.Removed))
                                            {
                                                await planningSite.Delete(_itemsPlanningPnDbContext).ConfigureAwait(false);
                                                var someList = await _itemsPlanningPnDbContext.PlanningCaseSites
                                                    .Where(x => x.PlanningId == planning.Id)
                                                    .Where(x => x.MicrotingSdkSiteId == planningSite.SiteId)
                                                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                                    .ToListAsync().ConfigureAwait(false);

                                                foreach (var planningCaseSite in someList)
                                                {
                                                    var result =
                                                        await sdkDbContext.Cases.SingleOrDefaultAsync(x =>
                                                            x.Id == planningCaseSite.MicrotingSdkCaseId).ConfigureAwait(false);
                                                    if (result is {MicrotingUid: { }})
                                                    {
                                                        await core.CaseDelete((int) result.MicrotingUid).ConfigureAwait(false);
                                                    }
                                                    else
                                                    {
                                                        var clSites = await sdkDbContext.CheckListSites.SingleOrDefaultAsync(x =>
                                                            x.Id == planningCaseSite.MicrotingCheckListSitId).ConfigureAwait(false);

                                                        if (clSites != null)
                                                        {
                                                            await core.CaseDelete(clSites.MicrotingUid)
                                                                .ConfigureAwait(false);
                                                        }
                                                    }
                                                }
                                            }

                                            await planning.Delete(_itemsPlanningPnDbContext).ConfigureAwait(false);
                                        }

                                        var areaRulePlanning = _backendConfigurationPnDbContext.AreaRulePlannings
                                            .Single(x => x.Id == arp.Id);
                                        areaRulePlanning.Status = false;
                                        await areaRulePlanning.Update(_backendConfigurationPnDbContext).ConfigureAwait(false);
                                    }

                                    foreach (AreaRulePlanning areaRuleAreaRulesPlanning in areaRule.AreaRulesPlannings)
                                    {
                                        areaRuleAreaRulesPlanning.ItemPlanningId = 0;
                                        areaRuleAreaRulesPlanning.Status = false;
                                        await areaRuleAreaRulesPlanning.Update(_backendConfigurationPnDbContext).ConfigureAwait(false);
                                    }
                                }
                            }
                        }
                        else
                        {
                            if (!areaRule.AreaRulesPlannings.Any())
                            {
                                return new OperationDataResult<AreaRulePlanningModel>(false,
                                    _backendConfigurationLocalizationService.GetString("ErrorWhileReadPlanning"));
                            }

                            for (var i = 0; i < areaRule.AreaRulesPlannings.Count; i++)
                            {
                                var rulePlanning = areaRule.AreaRulesPlannings[i];
                                var oldStatus = rulePlanning.Status;
                                rulePlanning.UpdatedByUserId = _userService.UserId;
                                rulePlanning.StartDate = areaRulePlanningModel.StartDate;
                                rulePlanning.Status = areaRulePlanningModel.Status;
                                rulePlanning.ComplianceEnabled = areaRulePlanningModel.ComplianceEnabled;
                                rulePlanning.SendNotifications = areaRulePlanningModel.SendNotifications;
                                if (rulePlanning.RepeatType == 1 && rulePlanning.RepeatEvery == 0)
                                {
                                    rulePlanning.ComplianceEnabled = false;
                                    rulePlanning.SendNotifications = false;
                                }

                                rulePlanning.AreaRuleId = areaRulePlanningModel.RuleId;
                                if (areaRulePlanningModel.TypeSpecificFields != null)
                                {
                                    rulePlanning.HoursAndEnergyEnabled =
                                        areaRulePlanningModel.TypeSpecificFields.HoursAndEnergyEnabled;
                                    rulePlanning.DayOfMonth = areaRulePlanningModel.TypeSpecificFields.DayOfMonth == 0
                                        ? 1
                                        : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                                    rulePlanning.EndDate = areaRulePlanningModel.TypeSpecificFields.EndDate;
                                    rulePlanning.DayOfWeek = areaRulePlanningModel.TypeSpecificFields.DayOfWeek == 0
                                        ? 1
                                        : areaRulePlanningModel.TypeSpecificFields.DayOfWeek;
                                    rulePlanning.RepeatEvery = areaRulePlanningModel.TypeSpecificFields.RepeatEvery;
                                    rulePlanning.RepeatType = areaRulePlanningModel.TypeSpecificFields.RepeatType;
                                }

                                // update assignments
                                var siteIdsForDelete = rulePlanning.PlanningSites
                                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                    .Select(x => x.SiteId)
                                    .Where(x => !areaRulePlanningModel.AssignedSites.Select(y => y.SiteId).Contains(x))
                                    .ToList();

                                var sitesForCreate = areaRulePlanningModel.AssignedSites
                                    .Select(x => x.SiteId)
                                    .Where(x => !rulePlanning.PlanningSites
                                        .Where(y => y.WorkflowState != Constants.WorkflowStates.Removed)
                                        .Select(y => y.SiteId)
                                        .Contains(x))
                                    .Select(siteId => new PlanningSite
                                    {
                                        AreaRuleId = rulePlanning.AreaRuleId,
                                        AreaId = rulePlanning.AreaId,
                                        AreaRulePlanningsId = rulePlanning.Id,
                                        SiteId = siteId,
                                        CreatedByUserId = _userService.UserId,
                                        UpdatedByUserId = _userService.UserId,
                                    })
                                    .ToList();

                                foreach (var assignedSite in sitesForCreate)
                                {
                                    await assignedSite.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);
                                }

                                foreach (var siteId in siteIdsForDelete)
                                {
                                    foreach (var planningSite in rulePlanning.PlanningSites
                                                 .Where(x => x.SiteId == siteId)
                                                 .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed))
                                    {
                                        await planningSite.Delete(_backendConfigurationPnDbContext).ConfigureAwait(false);
                                    }

                                    // await rulePlanning.PlanningSites
                                        // .First(x => x.SiteId == siteId)
                                        // .Delete(_backendConfigurationPnDbContext).ConfigureAwait(false);
                                }

                                await rulePlanning.Update(_backendConfigurationPnDbContext).ConfigureAwait(false);

                                switch (oldStatus)
                                {
                                    // create item planning
                                    case false when areaRulePlanningModel.Status:
                                        switch (areaRule.Area.Type)
                                        {
                                            case AreaTypesEnum.Type2: // tanks
                                            {
                                                if (areaRule.Type == AreaRuleT2TypesEnum.Open)
                                                {
                                                    const string eformName = "03. Kontrol flydelag";
                                                    var eformId = await sdkDbContext.CheckListTranslations
                                                        .Where(x => x.Text == eformName)
                                                        .Select(x => x.CheckListId)
                                                        .FirstAsync().ConfigureAwait(false);
                                                    var planningForType2TypeTankOpen = await BackendConfigurationAreaRulePlanningsServiceHelper.CreateItemPlanningObject(
                                                        eformId,
                                                        eformName, areaRule.AreaRulesPlannings[0].FolderId,
                                                        areaRulePlanningModel, areaRule, _userService.UserId, _backendConfigurationPnDbContext).ConfigureAwait(false);
                                                    planningForType2TypeTankOpen.NameTranslations =
                                                        new List<PlanningNameTranslation>
                                                        {
                                                            new()
                                                            {
                                                                LanguageId = 1, // da
                                                                Name = areaRule.AreaRuleTranslations
                                                                    .Where(x => x.LanguageId == 1)
                                                                    .Select(x => x.Name)
                                                                    .FirstOrDefault() + ": Flydelag",
                                                            },
                                                            new()
                                                            {
                                                                LanguageId = 2, // en
                                                                Name = areaRule.AreaRuleTranslations
                                                                    .Where(x => x.LanguageId == 2)
                                                                    .Select(x => x.Name)
                                                                    .FirstOrDefault() + ": Floating layer",
                                                            },
                                                            new()
                                                            {
                                                                LanguageId = 3, // ge
                                                                Name = areaRule.AreaRuleTranslations
                                                                    .Where(x => x.LanguageId == 2)
                                                                    .Select(x => x.Name)
                                                                    .FirstOrDefault() + ": Schwimmende Ebene",
                                                            },
                                                            // new PlanningNameTranslation
                                                            // {
                                                            //     LanguageId = 4,// uk-ua
                                                            //     Name = "Перевірте плаваючий шар",
                                                            // },
                                                        };
                                                    planningForType2TypeTankOpen.RepeatEvery = 1;
                                                    planningForType2TypeTankOpen.RepeatType = RepeatType.Month;
                                                    if (areaRulePlanningModel.TypeSpecificFields is not null)
                                                    {
                                                        planningForType2TypeTankOpen.RepeatUntil =
                                                            areaRulePlanningModel.TypeSpecificFields.EndDate;
                                                        planningForType2TypeTankOpen.DayOfWeek =
                                                            (DayOfWeek) areaRulePlanningModel.TypeSpecificFields
                                                                .DayOfWeek ==
                                                            0
                                                                ? DayOfWeek.Monday
                                                                : (DayOfWeek) areaRulePlanningModel.TypeSpecificFields
                                                                    .DayOfWeek;
                                                        planningForType2TypeTankOpen.DayOfMonth =
                                                            areaRulePlanningModel.TypeSpecificFields.DayOfMonth == 0
                                                                ? 1
                                                                : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                                                    }

                                                    await planningForType2TypeTankOpen.Create(
                                                        _itemsPlanningPnDbContext).ConfigureAwait(false);
                                                    await PairItemWithSiteHelper.Pair(
                                                        rulePlanning.PlanningSites.Select(x => x.SiteId).ToList(),
                                                        eformId,
                                                        planningForType2TypeTankOpen.Id,
                                                        areaRule.AreaRulesPlannings[0].FolderId, await _coreHelper.GetCore(), _itemsPlanningPnDbContext).ConfigureAwait(false);
                                                    areaRule.AreaRulesPlannings[0].DayOfMonth =
                                                        (int) areaRulePlanningModel.TypeSpecificFields?.DayOfMonth == 0
                                                            ? 1
                                                            : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                                                    areaRule.AreaRulesPlannings[0].ItemPlanningId =
                                                        planningForType2TypeTankOpen.Id;
                                                    areaRule.AreaRulesPlannings[0].Status = true;
                                                    await areaRule.AreaRulesPlannings[0]
                                                        .Update(_backendConfigurationPnDbContext).ConfigureAwait(false);
                                                }
                                                else
                                                {
                                                    if (areaRule.AreaRulesPlannings[0].ItemPlanningId != 0)
                                                    {
                                                        await BackendConfigurationAreaRulePlanningsServiceHelper.DeleteItemPlanning(areaRule.AreaRulesPlannings[0]
                                                            .ItemPlanningId, core, _userService.UserId, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext).ConfigureAwait(false);
                                                        areaRule.AreaRulesPlannings[0].ItemPlanningId = 0;
                                                        await areaRule.AreaRulesPlannings[0]
                                                            .Update(_backendConfigurationPnDbContext).ConfigureAwait(false);
                                                    }
                                                }

                                                if (areaRule.Type is AreaRuleT2TypesEnum.Open
                                                        or AreaRuleT2TypesEnum.Closed
                                                    && areaRule.Alarm is AreaRuleT2AlarmsEnum.Yes)
                                                {
                                                    const string eformName = "03. Kontrol alarmanlæg gyllebeholder";
                                                    var eformId = await sdkDbContext.CheckListTranslations
                                                        .Where(x => x.Text == eformName)
                                                        .Select(x => x.CheckListId)
                                                        .FirstAsync().ConfigureAwait(false);
                                                    var planningForType2AlarmYes = await BackendConfigurationAreaRulePlanningsServiceHelper.CreateItemPlanningObject(
                                                        eformId,
                                                        eformName, areaRule.AreaRulesPlannings[1].FolderId,
                                                        areaRulePlanningModel, areaRule, _userService.UserId, _backendConfigurationPnDbContext).ConfigureAwait(false);
                                                    planningForType2AlarmYes.NameTranslations =
                                                        new List<PlanningNameTranslation>
                                                        {
                                                            new()
                                                            {
                                                                LanguageId = 1, // da
                                                                Name = areaRule.AreaRuleTranslations
                                                                    .Where(x => x.LanguageId == 1)
                                                                    .Select(x => x.Name)
                                                                    .FirstOrDefault() + ": Alarm",
                                                            },
                                                            new()
                                                            {
                                                                LanguageId = 2, // en
                                                                Name = areaRule.AreaRuleTranslations
                                                                    .Where(x => x.LanguageId == 2)
                                                                    .Select(x => x.Name)
                                                                    .FirstOrDefault() + ": Alarm",
                                                            },
                                                            new()
                                                            {
                                                                LanguageId = 3, // ge
                                                                Name = areaRule.AreaRuleTranslations
                                                                    .Where(x => x.LanguageId == 3)
                                                                    .Select(x => x.Name)
                                                                    .FirstOrDefault() + ": Alarm",
                                                            },
                                                            // new ()
                                                            // {
                                                            //     LanguageId = 4,// uk-ua
                                                            //     Name = areaRule.AreaRuleTranslations
                                                            //        .Where(x => x.LanguageId == 4)
                                                            //        .Select(x => x.Name)
                                                            //        .FirstOrDefault() + "Перевірте сигналізацію",
                                                            // },
                                                        };
                                                    planningForType2AlarmYes.RepeatEvery = 1;
                                                    planningForType2AlarmYes.RepeatType = RepeatType.Month;
                                                    if (areaRulePlanningModel.TypeSpecificFields is not null)
                                                    {
                                                        planningForType2AlarmYes.RepeatUntil =
                                                            areaRulePlanningModel.TypeSpecificFields.EndDate;
                                                        planningForType2AlarmYes.DayOfWeek =
                                                            (DayOfWeek) areaRulePlanningModel.TypeSpecificFields
                                                                .DayOfWeek ==
                                                            0
                                                                ? DayOfWeek.Monday
                                                                : (DayOfWeek) areaRulePlanningModel.TypeSpecificFields
                                                                    .DayOfWeek;
                                                        planningForType2AlarmYes.DayOfMonth =
                                                            areaRulePlanningModel.TypeSpecificFields.DayOfMonth == 0
                                                                ? 1
                                                                : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                                                    }

                                                    await planningForType2AlarmYes.Create(_itemsPlanningPnDbContext).ConfigureAwait(false);
                                                    await PairItemWithSiteHelper.Pair(
                                                        rulePlanning.PlanningSites.Select(x => x.SiteId).ToList(),
                                                        eformId,
                                                        planningForType2AlarmYes.Id,
                                                        areaRule.AreaRulesPlannings[1].FolderId, await _coreHelper.GetCore(), _itemsPlanningPnDbContext).ConfigureAwait(false);
                                                    areaRule.AreaRulesPlannings[1].DayOfMonth =
                                                        (int) areaRulePlanningModel.TypeSpecificFields?.DayOfMonth == 0
                                                            ? 1
                                                            : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                                                    areaRule.AreaRulesPlannings[1].ItemPlanningId =
                                                        planningForType2AlarmYes.Id;
                                                    areaRule.AreaRulesPlannings[1].Status = true;
                                                    await areaRule.AreaRulesPlannings[1]
                                                        .Update(_backendConfigurationPnDbContext).ConfigureAwait(false);
                                                }
                                                else
                                                {
                                                    if (areaRule.AreaRulesPlannings[1].ItemPlanningId != 0)
                                                    {
                                                        await BackendConfigurationAreaRulePlanningsServiceHelper.DeleteItemPlanning(areaRule.AreaRulesPlannings[1]
                                                            .ItemPlanningId, core, _userService.UserId, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext).ConfigureAwait(false);
                                                        areaRule.AreaRulesPlannings[1].ItemPlanningId = 0;
                                                        await areaRule.AreaRulesPlannings[1]
                                                            .Update(_backendConfigurationPnDbContext).ConfigureAwait(false);
                                                    }
                                                }

                                                /*areaRule.EformName must be "03. Kontrol konstruktion"*/
                                                var planningForType2 = await BackendConfigurationAreaRulePlanningsServiceHelper.CreateItemPlanningObject(
                                                    (int) areaRule.EformId,
                                                    areaRule.EformName, areaRule.AreaRulesPlannings[2].FolderId,
                                                    areaRulePlanningModel, areaRule, _userService.UserId, _backendConfigurationPnDbContext).ConfigureAwait(false);
                                                planningForType2.NameTranslations = new List<PlanningNameTranslation>
                                                {
                                                    new()
                                                    {
                                                        LanguageId = 1, // da
                                                        Name = areaRule.AreaRuleTranslations
                                                            .Where(x => x.LanguageId == 1)
                                                            .Select(x => x.Name)
                                                            .FirstOrDefault() + ": Konstruktion",
                                                    },
                                                    new()
                                                    {
                                                        LanguageId = 2, // en
                                                        Name = areaRule.AreaRuleTranslations
                                                            .Where(x => x.LanguageId == 2)
                                                            .Select(x => x.Name)
                                                            .FirstOrDefault() + ": Construction",
                                                    },
                                                    new()
                                                    {
                                                        LanguageId = 3, // ge
                                                        Name = areaRule.AreaRuleTranslations
                                                            .Where(x => x.LanguageId == 3)
                                                            .Select(x => x.Name)
                                                            .FirstOrDefault() + ": Konstruktion",
                                                    },
                                                    // new PlanningNameTranslation
                                                    // {
                                                    //     LanguageId = 4,// uk-ua
                                                    //     Name = areaRule.AreaRuleTranslations
                                                    //      .Where(x => x.LanguageId == 4)
                                                    //      .Select(x => x.Name)
                                                    //      .FirstOrDefault() + "Перевірте конструкцію",
                                                    // },
                                                };
                                                planningForType2.RepeatEvery = 12;
                                                planningForType2.RepeatType = RepeatType.Month;
                                                if (areaRulePlanningModel.TypeSpecificFields is not null)
                                                {
                                                    planningForType2.RepeatUntil =
                                                        areaRulePlanningModel.TypeSpecificFields.EndDate;
                                                    planningForType2.DayOfWeek =
                                                        (DayOfWeek) areaRulePlanningModel.TypeSpecificFields
                                                            .DayOfWeek == 0
                                                            ? DayOfWeek.Monday
                                                            : (DayOfWeek) areaRulePlanningModel.TypeSpecificFields
                                                                .DayOfWeek;
                                                    planningForType2.DayOfMonth =
                                                        areaRulePlanningModel.TypeSpecificFields.DayOfMonth == 0
                                                            ? 1
                                                            : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                                                }

                                                await planningForType2.Create(_itemsPlanningPnDbContext).ConfigureAwait(false);
                                                await PairItemWithSiteHelper.Pair(
                                                    rulePlanning.PlanningSites.Select(x => x.SiteId).ToList(),
                                                    (int) areaRule.EformId,
                                                    planningForType2.Id,
                                                    areaRule.AreaRulesPlannings[2].FolderId, await _coreHelper.GetCore(), _itemsPlanningPnDbContext).ConfigureAwait(false);
                                                areaRule.AreaRulesPlannings[2].ItemPlanningId = planningForType2.Id;
                                                areaRule.AreaRulesPlannings[2].DayOfMonth =
                                                    (int) areaRulePlanningModel.TypeSpecificFields?.DayOfMonth == 0
                                                        ? 1
                                                        : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                                                areaRule.AreaRulesPlannings[2].Status = true;
                                                await areaRule.AreaRulesPlannings[2]
                                                    .Update(_backendConfigurationPnDbContext).ConfigureAwait(false);
                                                i = areaRule.AreaRulesPlannings.Count;
                                                break;
                                            }
                                            case AreaTypesEnum.Type6: // heat pumps
                                            {
                                                if (areaRulePlanningModel.TypeSpecificFields
                                                        ?.HoursAndEnergyEnabled is true)
                                                {
                                                    if (areaRulePlanningModel.TypeSpecificFields
                                                            ?.HoursAndEnergyEnabled is true)
                                                    {
                                                        areaRule.AreaRulesPlannings[0].HoursAndEnergyEnabled = true;
                                                        areaRule.AreaRulesPlannings[1].HoursAndEnergyEnabled = true;
                                                        areaRule.AreaRulesPlannings[2].HoursAndEnergyEnabled = true;
                                                        areaRule.AreaRulesPlannings[0].DayOfMonth =
                                                            (int) areaRulePlanningModel.TypeSpecificFields
                                                                ?.DayOfMonth == 0
                                                                ? 1
                                                                : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                                                        areaRule.AreaRulesPlannings[0].DayOfWeek =
                                                            (int) areaRulePlanningModel.TypeSpecificFields?.DayOfWeek ==
                                                            0
                                                                ? 1
                                                                : areaRulePlanningModel.TypeSpecificFields.DayOfWeek;
                                                        areaRule.AreaRulesPlannings[1].DayOfMonth =
                                                            (int) areaRulePlanningModel.TypeSpecificFields
                                                                ?.DayOfMonth == 0
                                                                ? 1
                                                                : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                                                        areaRule.AreaRulesPlannings[1].DayOfWeek =
                                                            (int) areaRulePlanningModel.TypeSpecificFields?.DayOfWeek ==
                                                            0
                                                                ? 1
                                                                : areaRulePlanningModel.TypeSpecificFields.DayOfWeek;
                                                        areaRule.AreaRulesPlannings[2].DayOfMonth =
                                                            (int) areaRulePlanningModel.TypeSpecificFields
                                                                ?.DayOfMonth == 0
                                                                ? 1
                                                                : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                                                        areaRule.AreaRulesPlannings[2].DayOfWeek =
                                                            (int) areaRulePlanningModel.TypeSpecificFields?.DayOfWeek ==
                                                            0
                                                                ? 1
                                                                : areaRulePlanningModel.TypeSpecificFields.DayOfWeek;
                                                        await areaRule.Update(_backendConfigurationPnDbContext).ConfigureAwait(false);
                                                        const string eformName = "10. Varmepumpe timer og energi";
                                                        var eformId = await sdkDbContext.CheckListTranslations
                                                            .Where(x => x.Text == eformName)
                                                            .Select(x => x.CheckListId)
                                                            .FirstAsync().ConfigureAwait(false);
                                                        var planningForType6HoursAndEnergyEnabled =
                                                            await BackendConfigurationAreaRulePlanningsServiceHelper.CreateItemPlanningObject(eformId, eformName,
                                                                areaRule.AreaRulesPlannings[0].FolderId,
                                                                areaRulePlanningModel,
                                                                areaRule, _userService.UserId, _backendConfigurationPnDbContext).ConfigureAwait(false);
                                                        planningForType6HoursAndEnergyEnabled.NameTranslations =
                                                            new List<PlanningNameTranslation>
                                                            {
                                                                new()
                                                                {
                                                                    LanguageId = 1, // da
                                                                    Name = areaRule.AreaRuleTranslations
                                                                        .Where(x => x.LanguageId == 1)
                                                                        .Select(x => x.Name)
                                                                        .FirstOrDefault() + ": Timer og energi",
                                                                },
                                                                new()
                                                                {
                                                                    LanguageId = 2, // en
                                                                    Name = areaRule.AreaRuleTranslations
                                                                        .Where(x => x.LanguageId == 2)
                                                                        .Select(x => x.Name)
                                                                        .FirstOrDefault() + ": Hours and energy",
                                                                },
                                                                new()
                                                                {
                                                                    LanguageId = 3, // ge
                                                                    Name = areaRule.AreaRuleTranslations
                                                                        .Where(x => x.LanguageId == 3)
                                                                        .Select(x => x.Name)
                                                                        .FirstOrDefault() + ": Stunden und Energie",
                                                                },
                                                            };
                                                        if (areaRulePlanningModel.TypeSpecificFields is not null)
                                                        {
                                                            if (areaRulePlanningModel.TypeSpecificFields.RepeatType !=
                                                                null)
                                                            {
                                                                planningForType6HoursAndEnergyEnabled.RepeatType =
                                                                    (RepeatType) areaRulePlanningModel
                                                                        .TypeSpecificFields
                                                                        .RepeatType;
                                                            }

                                                            if (areaRulePlanningModel.TypeSpecificFields?.RepeatEvery !=
                                                                null)
                                                            {
                                                                planningForType6HoursAndEnergyEnabled.RepeatEvery =
                                                                    (int) areaRulePlanningModel.TypeSpecificFields
                                                                        .RepeatEvery;
                                                            }

                                                            planningForType6HoursAndEnergyEnabled.DayOfMonth =
                                                                areaRulePlanningModel.TypeSpecificFields.DayOfMonth == 0
                                                                    ? 1
                                                                    : areaRulePlanningModel.TypeSpecificFields
                                                                        .DayOfMonth;
                                                            planningForType6HoursAndEnergyEnabled.DayOfWeek =
                                                                (DayOfWeek?) areaRulePlanningModel.TypeSpecificFields
                                                                    .DayOfWeek == 0
                                                                    ? DayOfWeek.Monday
                                                                    : (DayOfWeek?) areaRulePlanningModel
                                                                        .TypeSpecificFields
                                                                        .DayOfWeek;
                                                        }

                                                        await planningForType6HoursAndEnergyEnabled.Create(
                                                            _itemsPlanningPnDbContext).ConfigureAwait(false);
                                                        areaRule.AreaRulesPlannings[0].ItemPlanningId =
                                                            planningForType6HoursAndEnergyEnabled.Id;
                                                        await areaRule.AreaRulesPlannings[0]
                                                            .Update(_backendConfigurationPnDbContext).ConfigureAwait(false);
                                                    }
                                                    else
                                                    {
                                                        areaRule.AreaRulesPlannings[0].HoursAndEnergyEnabled = false;
                                                        areaRule.AreaRulesPlannings[1].HoursAndEnergyEnabled = false;
                                                        areaRule.AreaRulesPlannings[2].HoursAndEnergyEnabled = false;
                                                        if (areaRule.AreaRulesPlannings[0].ItemPlanningId != 0)
                                                        {
                                                            await BackendConfigurationAreaRulePlanningsServiceHelper.DeleteItemPlanning(areaRule.AreaRulesPlannings[0]
                                                                .ItemPlanningId, core, _userService.UserId, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext).ConfigureAwait(false);
                                                            areaRule.AreaRulesPlannings[0].ItemPlanningId = 0;
                                                            await areaRule.AreaRulesPlannings[0]
                                                                .Update(_backendConfigurationPnDbContext).ConfigureAwait(false);
                                                        }
                                                    }

                                                    const string eformNameOne = "10. Varmepumpe serviceaftale";
                                                    var eformIdOne = await sdkDbContext.CheckListTranslations
                                                        .Where(x => x.Text == eformNameOne)
                                                        .Select(x => x.CheckListId)
                                                        .FirstAsync().ConfigureAwait(false);
                                                    const string eformNameTwo = "10. Varmepumpe logbog";
                                                    var eformIdTwo = await sdkDbContext.CheckListTranslations
                                                        .Where(x => x.Text == eformNameTwo)
                                                        .Select(x => x.CheckListId)
                                                        .FirstAsync().ConfigureAwait(false);
                                                    var planningForType6One = await BackendConfigurationAreaRulePlanningsServiceHelper.CreateItemPlanningObject(eformIdOne,
                                                        eformNameOne, areaRule.AreaRulesPlannings[1].FolderId,
                                                        areaRulePlanningModel, areaRule, _userService.UserId, _backendConfigurationPnDbContext).ConfigureAwait(false);
                                                    planningForType6One.NameTranslations =
                                                        new List<PlanningNameTranslation>
                                                        {
                                                            new()
                                                            {
                                                                LanguageId = 1, // da
                                                                Name = areaRule.AreaRuleTranslations
                                                                    .Where(x => x.LanguageId == 1)
                                                                    .Select(x => x.Name)
                                                                    .FirstOrDefault() + ": Service",
                                                            },
                                                            new()
                                                            {
                                                                LanguageId = 2, // en
                                                                Name = areaRule.AreaRuleTranslations
                                                                    .Where(x => x.LanguageId == 2)
                                                                    .Select(x => x.Name)
                                                                    .FirstOrDefault() + ": Service",
                                                            },
                                                            new()
                                                            {
                                                                LanguageId = 3, // ge
                                                                Name = areaRule.AreaRuleTranslations
                                                                    .Where(x => x.LanguageId == 3)
                                                                    .Select(x => x.Name)
                                                                    .FirstOrDefault() + ": Service",
                                                            },
                                                        };
                                                    planningForType6One.RepeatEvery = 12;
                                                    planningForType6One.RepeatType = RepeatType.Month;
                                                    var planningForType6Two = await BackendConfigurationAreaRulePlanningsServiceHelper.CreateItemPlanningObject(eformIdTwo,
                                                        eformNameTwo, areaRule.AreaRulesPlannings[2].FolderId,
                                                        areaRulePlanningModel, areaRule, _userService.UserId, _backendConfigurationPnDbContext).ConfigureAwait(false);
                                                    planningForType6Two.NameTranslations =
                                                        new List<PlanningNameTranslation>
                                                        {
                                                            new()
                                                            {
                                                                LanguageId = 1, // da
                                                                Name = areaRule.AreaRuleTranslations
                                                                    .Where(x => x.LanguageId == 1)
                                                                    .Select(x => x.Name)
                                                                    .FirstOrDefault() + ": Logbog",
                                                            },
                                                            new()
                                                            {
                                                                LanguageId = 2, // en
                                                                Name = areaRule.AreaRuleTranslations
                                                                    .Where(x => x.LanguageId == 2)
                                                                    .Select(x => x.Name)
                                                                    .FirstOrDefault() + ": Logbook",
                                                            },
                                                            new()
                                                            {
                                                                LanguageId = 3, // ge
                                                                Name = areaRule.AreaRuleTranslations
                                                                    .Where(x => x.LanguageId == 3)
                                                                    .Select(x => x.Name)
                                                                    .FirstOrDefault() + ": Logbook",
                                                            },
                                                        };
                                                    planningForType6Two.RepeatEvery = 12;
                                                    planningForType6Two.RepeatType = RepeatType.Month;
                                                    if (areaRulePlanningModel.TypeSpecificFields is not null)
                                                    {
                                                        planningForType6One.DayOfMonth =
                                                            areaRulePlanningModel.TypeSpecificFields.DayOfMonth == 0
                                                                ? 1
                                                                : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                                                        planningForType6One.DayOfWeek =
                                                            (DayOfWeek?) areaRulePlanningModel.TypeSpecificFields
                                                                .DayOfWeek == 0
                                                                ? DayOfWeek.Monday
                                                                : (DayOfWeek?) areaRulePlanningModel.TypeSpecificFields
                                                                    .DayOfWeek;
                                                        planningForType6One.RepeatUntil =
                                                            areaRulePlanningModel.TypeSpecificFields.EndDate;
                                                        planningForType6Two.DayOfMonth =
                                                            areaRulePlanningModel.TypeSpecificFields.DayOfMonth == 0
                                                                ? 1
                                                                : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                                                        planningForType6Two.DayOfWeek =
                                                            (DayOfWeek?) areaRulePlanningModel.TypeSpecificFields
                                                                .DayOfWeek == 0
                                                                ? DayOfWeek.Monday
                                                                : (DayOfWeek?) areaRulePlanningModel.TypeSpecificFields
                                                                    .DayOfWeek;
                                                        planningForType6Two.RepeatUntil =
                                                            areaRulePlanningModel.TypeSpecificFields.EndDate;
                                                    }

                                                    await planningForType6One.Create(_itemsPlanningPnDbContext).ConfigureAwait(false);
                                                    await planningForType6Two.Create(_itemsPlanningPnDbContext).ConfigureAwait(false);
                                                    await PairItemWithSiteHelper.Pair(
                                                        rulePlanning.PlanningSites.Select(x => x.SiteId).ToList(),
                                                        eformIdOne,
                                                        planningForType6One.Id,
                                                        areaRule.AreaRulesPlannings[1].FolderId, await _coreHelper.GetCore(), _itemsPlanningPnDbContext).ConfigureAwait(false);
                                                    await PairItemWithSiteHelper.Pair(
                                                        rulePlanning.PlanningSites.Select(x => x.SiteId).ToList(),
                                                        eformIdTwo,
                                                        planningForType6Two.Id,
                                                        areaRule.AreaRulesPlannings[2].FolderId, await _coreHelper.GetCore(), _itemsPlanningPnDbContext).ConfigureAwait(false);
                                                    areaRule.AreaRulesPlannings[1].ItemPlanningId =
                                                        planningForType6One.Id;
                                                    areaRule.AreaRulesPlannings[2].ItemPlanningId =
                                                        planningForType6Two.Id;
                                                    await areaRule.AreaRulesPlannings[1]
                                                        .Update(_backendConfigurationPnDbContext).ConfigureAwait(false);
                                                    await areaRule.AreaRulesPlannings[2]
                                                        .Update(_backendConfigurationPnDbContext).ConfigureAwait(false);
                                                }

                                                i = areaRule.AreaRulesPlannings.Count;
                                                break;
                                            }
                                            case AreaTypesEnum.Type10:
                                            {
                                                break;
                                            }
                                            default:
                                            {
                                                if (areaRule.FolderId == 0)
                                                {
                                                    var folderId = await _backendConfigurationPnDbContext
                                                        .ProperyAreaFolders
                                                        .Include(x => x.AreaProperty)
                                                        .Where(x => x.AreaProperty.PropertyId ==
                                                                    areaRulePlanningModel.PropertyId)
                                                        .Where(x => x.AreaProperty.AreaId == areaRule.AreaId)
                                                        .Select(x => x.FolderId)
                                                        .FirstOrDefaultAsync().ConfigureAwait(false);
                                                    if (folderId != 0)
                                                    {
                                                        areaRule.FolderId = folderId;
                                                        areaRule.FolderName = await sdkDbContext.FolderTranslations
                                                            .Where(x => x.FolderId == folderId)
                                                            .Where(x => x.LanguageId == 1) // danish
                                                            .Select(x => x.Name)
                                                            .FirstAsync().ConfigureAwait(false);
                                                        await areaRule.Update(_backendConfigurationPnDbContext).ConfigureAwait(false);
                                                    }
                                                }

                                                if (areaRule.Area.Type ==
                                                    AreaTypesEnum.Type5) // recuring tasks(mon-sun)
                                                {
                                                    var folderIds = await _backendConfigurationPnDbContext
                                                        .ProperyAreaFolders
                                                        .Include(x => x.AreaProperty)
                                                        .Where(x => x.AreaProperty.PropertyId ==
                                                                    areaRulePlanningModel.PropertyId)
                                                        .Where(x => x.AreaProperty.AreaId == areaRule.AreaId)
                                                        .Select(x => x.FolderId)
                                                        .Skip(1)
                                                        .ToListAsync().ConfigureAwait(false);
                                                    areaRule.FolderId = folderIds[areaRule.DayOfWeek];
                                                    areaRule.FolderName = await sdkDbContext.FolderTranslations
                                                        .Where(x => x.FolderId == areaRule.FolderId)
                                                        .Where(x => x.LanguageId == 1) // danish
                                                        .Select(x => x.Name)
                                                        .FirstAsync().ConfigureAwait(false);
                                                    await areaRule.Update(_backendConfigurationPnDbContext).ConfigureAwait(false);
                                                    areaRulePlanningModel.TypeSpecificFields ??=
                                                        new AreaRuleTypePlanningModel(); // if areaRulePlanningModel.TypeSpecificFields == null -> areaRulePlanningModel.TypeSpecificFields = new()
                                                    areaRulePlanningModel.TypeSpecificFields.RepeatEvery =
                                                        areaRule.RepeatEvery; // repeat every mast be from area rule
                                                }

                                                var planning = await BackendConfigurationAreaRulePlanningsServiceHelper.CreateItemPlanningObject((int) areaRule.EformId,
                                                    areaRule.EformName, areaRule.FolderId, areaRulePlanningModel,
                                                    areaRule, _userService.UserId, _backendConfigurationPnDbContext).ConfigureAwait(false);
                                                planning.NameTranslations = areaRule.AreaRuleTranslations.Select(
                                                    areaRuleAreaRuleTranslation => new PlanningNameTranslation
                                                    {
                                                        LanguageId = areaRuleAreaRuleTranslation.LanguageId,
                                                        Name = areaRuleAreaRuleTranslation.Name,
                                                    }).ToList();
                                                if (areaRulePlanningModel.TypeSpecificFields is not null)
                                                {
                                                    planning.DayOfMonth =
                                                        areaRulePlanningModel.TypeSpecificFields.DayOfMonth == 0
                                                            ? 1
                                                            : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                                                    planning.DayOfWeek =
                                                        (DayOfWeek?) areaRulePlanningModel.TypeSpecificFields
                                                            .DayOfWeek == 0
                                                            ? DayOfWeek.Monday
                                                            : (DayOfWeek?) areaRulePlanningModel.TypeSpecificFields
                                                                .DayOfWeek;
                                                    planning.RepeatUntil =
                                                        areaRulePlanningModel.TypeSpecificFields.EndDate;
                                                    if (areaRulePlanningModel.TypeSpecificFields
                                                            .RepeatEvery is not null)
                                                    {
                                                        planning.RepeatEvery =
                                                            (int) areaRulePlanningModel.TypeSpecificFields.RepeatEvery;
                                                    }

                                                    if (areaRulePlanningModel.TypeSpecificFields.RepeatType is not null)
                                                    {
                                                        planning.RepeatType =
                                                            (RepeatType) areaRulePlanningModel.TypeSpecificFields
                                                                .RepeatType;
                                                    }
                                                }

                                                if (planning.NameTranslations.Any(x => x.Name == "13. APV Medarbejder"))
                                                {
                                                    planning.RepeatEvery = 0;
                                                    planning.RepeatType = RepeatType.Day;
                                                }

                                                await planning.Create(_itemsPlanningPnDbContext).ConfigureAwait(false);
                                                await PairItemWithSiteHelper.Pair(
                                                    rulePlanning.PlanningSites
                                                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                                        .Select(x => x.SiteId).ToList(),
                                                    (int) areaRule.EformId,
                                                    planning.Id,
                                                    areaRule.FolderId, await _coreHelper.GetCore(), _itemsPlanningPnDbContext).ConfigureAwait(false);
                                                rulePlanning.ItemPlanningId = planning.Id;
                                                await rulePlanning.Update(_backendConfigurationPnDbContext).ConfigureAwait(false);
                                                break;
                                            }
                                        }

                                        break;
                                    // delete item planning
                                    case true when !areaRulePlanningModel.Status:
                                        if (rulePlanning.ItemPlanningId != 0)
                                        {
                                            var complianceList = await _backendConfigurationPnDbContext.Compliances
                                                .Where(x => x.PlanningId == rulePlanning.ItemPlanningId
                                                            && x.WorkflowState != Constants.WorkflowStates.Removed)
                                                .ToListAsync().ConfigureAwait(false);
                                            foreach (var compliance in complianceList)
                                            {
                                                if (compliance != null)
                                                {
                                                    await compliance.Delete(_backendConfigurationPnDbContext).ConfigureAwait(false);
                                                }
                                            }

                                            await BackendConfigurationAreaRulePlanningsServiceHelper.DeleteItemPlanning(rulePlanning.ItemPlanningId, core, _userService.UserId, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext).ConfigureAwait(false);

                                            rulePlanning.ItemPlanningId = 0;
                                            rulePlanning.Status = false;
                                            await rulePlanning.Update(_backendConfigurationPnDbContext).ConfigureAwait(false);

                                            var planningSites = await _backendConfigurationPnDbContext.PlanningSites
                                                .Where(x => x.AreaRulePlanningsId == rulePlanning.Id)
                                                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                                .ToListAsync().ConfigureAwait(false);

                                            foreach (var planningSite in planningSites) // delete all planning sites
                                            {
                                                planningSite.Status = 0;
                                                await planningSite.Update(_backendConfigurationPnDbContext).ConfigureAwait(false);
                                                // await planningSite.Delete(_backendConfigurationPnDbContext).ConfigureAwait(false);
                                            }
                                        }

                                        break;
                                    // update item planning
                                    case true when areaRulePlanningModel.Status:
                                        if (areaRule.Area.Type == AreaTypesEnum.Type6
                                            && rulePlanning.Id ==
                                            areaRule.AreaRulesPlannings[0]
                                                .Id // for type 6 create 3 rulePlane and 0 - it's HoursAndEnergyEnabled
                                            && areaRule.AreaRulesPlannings[0].HoursAndEnergyEnabled == false
                                            && areaRule.AreaRulesPlannings[0].ItemPlanningId != 0)
                                        {
                                            var planningForType6HoursAndEnergyEnabled =
                                                await _itemsPlanningPnDbContext.Plannings
                                                    .FirstAsync(x =>
                                                        x.Id == areaRule.AreaRulesPlannings[0].ItemPlanningId).ConfigureAwait(false);
                                            await planningForType6HoursAndEnergyEnabled.Delete(
                                                _itemsPlanningPnDbContext).ConfigureAwait(false);
                                            areaRule.AreaRulesPlannings[0].ItemPlanningId = 0;
                                            await areaRule.AreaRulesPlannings[0]
                                                .Update(_backendConfigurationPnDbContext).ConfigureAwait(false);
                                            continue;
                                        }

                                        if (rulePlanning.ItemPlanningId !=
                                            0) // if item planning is create - need to update
                                        {
                                            var planning = await _itemsPlanningPnDbContext.Plannings
                                                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                                .Where(x => x.Id == rulePlanning.ItemPlanningId)
                                                .Include(x => x.PlanningSites)
                                                .FirstAsync().ConfigureAwait(false);
                                            planning.Enabled = areaRulePlanningModel.Status;
                                            planning.PushMessageOnDeployment = areaRulePlanningModel.SendNotifications;
                                            planning.StartDate = areaRulePlanningModel.StartDate;
                                            planning.DayOfMonth =
                                                (int) areaRulePlanningModel.TypeSpecificFields?.DayOfMonth == 0
                                                    ? 1
                                                    : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                                            planning.DayOfWeek =
                                                (DayOfWeek) areaRulePlanningModel.TypeSpecificFields?.DayOfWeek == 0
                                                    ? DayOfWeek.Friday
                                                    : (DayOfWeek) areaRulePlanningModel.TypeSpecificFields.DayOfWeek;
                                            foreach (var planningSite in planning.PlanningSites
                                                         .Where(
                                                             x => x.WorkflowState != Constants.WorkflowStates.Removed))
                                            {
                                                if (siteIdsForDelete.Contains(planningSite.SiteId))
                                                {
                                                    await planningSite.Delete(_itemsPlanningPnDbContext).ConfigureAwait(false);
                                                    var someList = await _itemsPlanningPnDbContext.PlanningCaseSites
                                                        .Where(x => x.PlanningId == planning.Id)
                                                        .Where(x => x.MicrotingSdkSiteId == planningSite.SiteId)
                                                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                                        .ToListAsync().ConfigureAwait(false);

                                                    // foreach (var planningCaseSite in someList)
                                                    // {
                                                    //     var microtingCase = await sdkDbContext.Cases
                                                    //         .SingleOrDefaultAsync(x =>
                                                    //             x.Id == planningCaseSite.MicrotingSdkCaseId);
                                                    //     await core.CaseDelete((int)microtingCase.MicrotingUid);
                                                    // }
                                                    foreach (var planningCaseSite in someList)
                                                    {
                                                        var result =
                                                            await sdkDbContext.Cases.SingleOrDefaultAsync(x =>
                                                                x.Id == planningCaseSite.MicrotingSdkCaseId).ConfigureAwait(false);
                                                        if (result is {MicrotingUid: { }})
                                                        {
                                                            await core.CaseDelete((int) result.MicrotingUid).ConfigureAwait(false);
                                                        }
                                                        else
                                                        {
                                                            var clSites = await sdkDbContext.CheckListSites.SingleOrDefaultAsync(
                                                                x =>
                                                                    x.Id == planningCaseSite.MicrotingCheckListSitId).ConfigureAwait(false);

                                                            if (clSites != null)
                                                            {
                                                                await core.CaseDelete(clSites.MicrotingUid)
                                                                    .ConfigureAwait(false);
                                                            }
                                                        }
                                                    }
                                                }
                                            }

                                            foreach (var siteId in sitesForCreate.Select(x => x.SiteId))
                                            {
                                                var planningSite =
                                                    new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.
                                                        PlanningSite
                                                        {
                                                            SiteId = siteId,
                                                            PlanningId = planning.Id,
                                                            CreatedByUserId = _userService.UserId,
                                                            UpdatedByUserId = _userService.UserId,
                                                        };
                                                await planningSite.Create(_itemsPlanningPnDbContext).ConfigureAwait(false);
                                            }

                                            if (sitesForCreate.Count > 0)
                                            {
                                                await PairItemWithSiteHelper.Pair(
                                                    sitesForCreate.Select(x => x.SiteId).ToList(),
                                                    planning.RelatedEFormId,
                                                    planning.Id,
                                                    (int) planning.SdkFolderId, await _coreHelper.GetCore(), _itemsPlanningPnDbContext).ConfigureAwait(false);
                                            }

                                            await planning.Update(_itemsPlanningPnDbContext).ConfigureAwait(false);
                                            if (!_itemsPlanningPnDbContext.PlanningSites.Any(x =>
                                                    x.PlanningId == planning.Id &&
                                                    x.WorkflowState != Constants.WorkflowStates.Removed) ||
                                                !rulePlanning.ComplianceEnabled)
                                            {
                                                var complianceList = await _backendConfigurationPnDbContext.Compliances
                                                    .Where(x => x.PlanningId == planning.Id)
                                                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                                    .ToListAsync().ConfigureAwait(false);
                                                foreach (var compliance in complianceList)
                                                {
                                                    await compliance.Delete(_backendConfigurationPnDbContext).ConfigureAwait(false);
                                                    if (_backendConfigurationPnDbContext.Compliances.Any(x =>
                                                            x.PropertyId == property.Id &&
                                                            x.Deadline < DateTime.UtcNow &&
                                                            x.WorkflowState != Constants.WorkflowStates.Removed))
                                                    {
                                                        property.ComplianceStatusThirty = 2;
                                                        property.ComplianceStatus = 2;
                                                    }
                                                    else
                                                    {
                                                        if (!_backendConfigurationPnDbContext.Compliances.Any(x =>
                                                                x.PropertyId == property.Id && x.WorkflowState !=
                                                                Constants.WorkflowStates.Removed))
                                                        {
                                                            property.ComplianceStatusThirty = 0;
                                                            property.ComplianceStatus = 0;
                                                        }
                                                    }

                                                    property.Update(_backendConfigurationPnDbContext).GetAwaiter()
                                                        .GetResult();
                                                }
                                            }
                                        }

                                        break;
                                    // nothing to do
                                    case false when !areaRulePlanningModel.Status:
                                        break;
                                }
                            }
                        }
                    }

                    return new OperationDataResult<AreaRuleModel>(true,
                        _backendConfigurationLocalizationService.GetString("SuccessfullyUpdatePlanning"));
                }

                return await CreatePlanning(areaRulePlanningModel).ConfigureAwait(false); // create planning
            }
            catch (Exception e)
            {
                Log.LogException(e.Message);
                Log.LogException(e.StackTrace);
                return new OperationDataResult<AreaRuleModel>(false,
                    $"{_backendConfigurationLocalizationService.GetString("ErrorWhileUpdatePlanning")}: {e.Message}");
            }
        }

        public async Task<OperationDataResult<AreaRulePlanningModel>> GetPlanningByRuleId(int ruleId)
        {
            try
            {
                var areaRule = await _backendConfigurationPnDbContext.AreaRules
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Id == ruleId)
                    .Include(x => x.AreaRulesPlannings)
                    .FirstOrDefaultAsync().ConfigureAwait(false);

                if (areaRule == null)
                {
                    return new OperationDataResult<AreaRulePlanningModel>(false,
                        _backendConfigurationLocalizationService.GetString("ErrorWhileReadPlanning"));
                }

                if (!areaRule.AreaRulesPlannings.Any())
                {
                    return new OperationDataResult<AreaRulePlanningModel>(true); // it's okay
                }

                var areaRuleId = areaRule.AreaRulesPlannings
                    .OrderByDescending(x => x.ItemPlanningId)
                    .First(y => y.WorkflowState != Constants.WorkflowStates.Removed).Id;
                var areaRulePlanning = await _backendConfigurationPnDbContext.AreaRulePlannings
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Id == areaRuleId)
                    .Include(x => x.PlanningSites)
                    .Select(x => new AreaRulePlanningModel
                    {
                        Id = x.Id,
                        RuleId = ruleId,
                        StartDate = (DateTime)x.StartDate,
                        Status = x.Status,
                        TypeSpecificFields = new AreaRuleTypePlanningModel
                        {
                            DayOfWeek = x.DayOfWeek,
                            EndDate = x.EndDate,
                            RepeatEvery = x.RepeatType == 1 && x.RepeatEvery == 0 ? 1 : x.RepeatEvery,
                            RepeatType = x.RepeatType,
                            Alarm = x.Alarm,
                            Type = x.Type,
                            HoursAndEnergyEnabled = x.HoursAndEnergyEnabled,
                            DayOfMonth = x.DayOfMonth,
                        },
                        SendNotifications = x.SendNotifications,
                        AssignedSites = x.PlanningSites
                            .Where(y => y.WorkflowState != Constants.WorkflowStates.Removed)
                            // .Where(y => y.Status > 0)
                            .Select(y => new AreaRuleAssignedSitesModel { SiteId = y.SiteId, Checked = true, Status = y.Status, PlanningSiteId = y.Id})
                            .ToList(),
                        ComplianceEnabled = x.ComplianceEnabled,
                        PropertyId = x.PropertyId,
                    })
                    .FirstOrDefaultAsync().ConfigureAwait(false);

                if (areaRulePlanning == null)
                {
                    return new OperationDataResult<AreaRulePlanningModel>(false,
                        _backendConfigurationLocalizationService.GetString("PlanningNotFound"));
                }

                return new OperationDataResult<AreaRulePlanningModel>(true, areaRulePlanning);
            }
            catch (Exception e)
            {
                Log.LogException(e.Message);
                Log.LogException(e.StackTrace);
                return new OperationDataResult<AreaRulePlanningModel>(false,
                    $"{_backendConfigurationLocalizationService.GetString("ErrorWhileReadPlanning")}: {e.Message}");
            }
        }

        private async Task<OperationDataResult<AreaRuleModel>> CreatePlanning(
            AreaRulePlanningModel areaRulePlanningModel)
        {
            var core = await _coreHelper.GetCore().ConfigureAwait(false);
            var sdkDbContext = core.DbContextHelper.GetDbContext();

            var areaRule = await _backendConfigurationPnDbContext.AreaRules
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.Id == areaRulePlanningModel.RuleId)
                .Include(x => x.AreaRuleTranslations)
                .Include(x => x.Area)
                .Include(x => x.Property)
                .FirstOrDefaultAsync().ConfigureAwait(false);

            if (areaRule == null)
            {
                return new OperationDataResult<AreaRuleModel>(true,
                    _backendConfigurationLocalizationService.GetString("AreaRuleNotFound"));
            }

            switch (areaRule.Area.Type)
            {
                case AreaTypesEnum.Type2: // tanks
                    {
                        await BackendConfigurationAreaRulePlanningsServiceHelper.CreatePlanningType2(areaRule, sdkDbContext, areaRulePlanningModel, core, _userService.UserId, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext).ConfigureAwait(false);
                        break;
                    }
                case AreaTypesEnum.Type3: // stables and tail bite
                    {
                        await BackendConfigurationAreaRulePlanningsServiceHelper.CreatePlanningType3(areaRule, sdkDbContext, areaRulePlanningModel, core, _userService.UserId, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext).ConfigureAwait(false);
                        break;
                    }
                case AreaTypesEnum.Type5: // recuring tasks(mon-sun)
                    {
                        await BackendConfigurationAreaRulePlanningsServiceHelper.CreatePlanningType5(areaRule, areaRulePlanningModel, core, _userService.UserId, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext).ConfigureAwait(false);
                        break;
                    }
                case AreaTypesEnum.Type6: // heat pumps
                    {
                        await BackendConfigurationAreaRulePlanningsServiceHelper.CreatePlanningType6(areaRule, sdkDbContext, areaRulePlanningModel, core, _userService.UserId, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext).ConfigureAwait(false);
                        break;
                    }
                case AreaTypesEnum.Type9: // chemical APV
                    {
                        await BackendConfigurationAreaRulePlanningsServiceHelper.CreatePlanningType9(areaRule, sdkDbContext, areaRulePlanningModel, core, _userService.UserId, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext).ConfigureAwait(false);
                        break;
                    }
                case AreaTypesEnum.Type10:
                {
                    await BackendConfigurationAreaRulePlanningsServiceHelper.CreatePlanningType10(areaRule, sdkDbContext, areaRulePlanningModel, core, _userService.UserId, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext).ConfigureAwait(false);
                    break;
                }
                default:
                    {
                        await BackendConfigurationAreaRulePlanningsServiceHelper.CreatePlanningDefaultType(areaRule, sdkDbContext, areaRulePlanningModel, core, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, _userService.UserId).ConfigureAwait(false);
                        break;
                    }
            }

            return new OperationDataResult<AreaRuleModel>(true,
                _backendConfigurationLocalizationService.GetString("SuccessfullyCreatedPlanning"));
        }

        public async Task<OperationDataResult<Paged<TaskWorkerModel>>> GetPlanningsBySiteId(int siteId, FilterAndSortModel filterAndSortModel)
        {
            try
            {
                var language = await _userService.GetCurrentUserLanguage().ConfigureAwait(false);
                var listTaskWorker = new List<TaskWorkerModel>();

                var propertyIds = await _backendConfigurationPnDbContext.PropertyWorkers
                    .Where(x =>
                        x.WorkerId == siteId
                        && x.WorkflowState != Constants.WorkflowStates.Removed).Select(x => x.PropertyId).ToListAsync().ConfigureAwait(false);

                var sitePlannings = await _backendConfigurationPnDbContext.AreaRulePlannings
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Status)
                    .Where(x => propertyIds.Contains(x.PropertyId))
                    .Where(x => x.PlanningSites.Where(y => y.WorkflowState != Constants.WorkflowStates.Removed && y.SiteId == siteId).Select(y => y.SiteId).Any())
                    .Include(x => x.AreaRule.AreaRuleTranslations)
                    .Select(x => new
                    {
                        x.Id,
                        x.ItemPlanningId,
                        x.PropertyId,
                        x.AreaRuleId,
                        x.AreaId,
                        x.AreaRule,
                        x.Status
                    })
                    .ToListAsync().ConfigureAwait(false);
                // var total = sitePlannings.Count;
                foreach (var sitePlanning in sitePlannings)
                {
                    var areaName = await _backendConfigurationPnDbContext.AreaTranslations
                        .Where(x => x.AreaId == sitePlanning.AreaId)
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Where(x => x.LanguageId == language.Id)
                        .Select(x => x.Name)
                        .FirstOrDefaultAsync().ConfigureAwait(false);

                    var areaRuleName = await _backendConfigurationPnDbContext.AreaRuleTranslations
                        .Where(x => x.AreaRuleId == sitePlanning.AreaRuleId)
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Where(x => x.LanguageId == language.Id)
                        .Select(x => x.Name)
                        .FirstOrDefaultAsync().ConfigureAwait(false);

                    var propertyName = await _backendConfigurationPnDbContext.Properties
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Where(x => x.Id == sitePlanning.PropertyId)
                        .Select(x => x.Name)
                        .FirstOrDefaultAsync().ConfigureAwait(false);

                    var itemPlanningName = await _itemsPlanningPnDbContext.PlanningNameTranslation
                        .Where(x => x.PlanningId == sitePlanning.ItemPlanningId)
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Where(x => x.LanguageId == language.Id)
                        .Select(x => x.Name)
                        .FirstOrDefaultAsync().ConfigureAwait(false);

                    if (itemPlanningName != null)
                    {
                        listTaskWorker.Add(new TaskWorkerModel
                        {
                            Id = sitePlanning.Id,
                            Path = $"{areaName} - {areaRuleName}",
                            PropertyName = propertyName,
                            ItemName = itemPlanningName,
                            PropertyId = sitePlanning.PropertyId,
                            AreaRule = new AreaRuleNameAndTypeSpecificFields
                            {
                                TranslatedName = sitePlanning.AreaRule.AreaRuleTranslations
                                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                    .Where(x => x.LanguageId == language.Id)
                                    .Select(x => x.Name)
                                    .FirstOrDefault(),
                                TypeSpecificFields = new TypeSpecificField
                                {
                                    EformId = sitePlanning.AreaRule.EformId,
                                    Type = sitePlanning.AreaRule.Type,
                                    Alarm = sitePlanning.AreaRule.Alarm,
                                    DayOfWeek = sitePlanning.AreaRule.DayOfWeek,
                                    RepeatEvery = sitePlanning.AreaRule.RepeatEvery,
                                },
                            }
                        });
                    }
                }
                if (listTaskWorker.Any())
                {
                    listTaskWorker = QueryHelper.AddSortToQuery(listTaskWorker.AsQueryable(),
                        filterAndSortModel.Sort,
                        filterAndSortModel.IsSortDsc).ToList();
                }

                return new OperationDataResult<Paged<TaskWorkerModel>>(true, new Paged<TaskWorkerModel> { Entities = listTaskWorker/*, Total = total*/});
            }
            catch (Exception e)
            {
                Log.LogException(e.Message);
                Log.LogException(e.StackTrace);
                return new OperationDataResult<Paged<TaskWorkerModel>>(false,
                    $"{_backendConfigurationLocalizationService.GetString("ErrorWhileReadPlanning")}: {e.Message}");
            }
        }

        public async Task<OperationDataResult<AreaRulePlanningModel>> GetPlanningById(int planningId)
        {
            try
            {
                var areaRulePlanning = await _backendConfigurationPnDbContext.AreaRulePlannings
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Id == planningId)
                    .Include(x => x.PlanningSites)
                    .Select(x => new AreaRulePlanningModel
                    {
                        Id = x.Id,
                        RuleId = x.AreaRuleId,
                        StartDate = (DateTime)x.StartDate,
                        Status = x.Status,
                        TypeSpecificFields = new AreaRuleTypePlanningModel
                        {
                            DayOfWeek = x.DayOfWeek,
                            EndDate = x.EndDate,
                            RepeatEvery = x.RepeatEvery,
                            RepeatType = x.RepeatType,
                            Alarm = x.Alarm,
                            Type = x.Type,
                            HoursAndEnergyEnabled = x.HoursAndEnergyEnabled,
                            DayOfMonth = x.DayOfMonth,
                        },
                        SendNotifications = x.SendNotifications,
                        AssignedSites = x.PlanningSites
                            .Where(y => y.WorkflowState != Constants.WorkflowStates.Removed)
                            .Select(y => new AreaRuleAssignedSitesModel { SiteId = y.SiteId, Checked = true })
                            .ToList()
                    })
                    .FirstOrDefaultAsync().ConfigureAwait(false);

                if (areaRulePlanning == null)
                {
                    return new OperationDataResult<AreaRulePlanningModel>(false,
                        _backendConfigurationLocalizationService.GetString("PlanningNotFound"));
                }

                return new OperationDataResult<AreaRulePlanningModel>(true, areaRulePlanning);
            }
            catch (Exception e)
            {
                Log.LogException(e.Message);
                Log.LogException(e.StackTrace);
                return new OperationDataResult<AreaRulePlanningModel>(false,
                    $"{_backendConfigurationLocalizationService.GetString("ErrorWhileReadPlanning")}: {e.Message}");
            }
        }

    }
}