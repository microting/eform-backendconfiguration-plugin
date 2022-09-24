using System.Text.RegularExpressions;
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

    public class BackendConfigurationAreaRulePlanningsService : IBackendConfigurationAreaRulePlanningsService
    {
        private readonly IEFormCoreService _coreHelper;
        private readonly IBackendConfigurationLocalizationService _backendConfigurationLocalizationService;
        private readonly IUserService _userService;
        private readonly BackendConfigurationPnDbContext _backendConfigurationPnDbContext;
        private readonly ItemsPlanningPnDbContext _itemsPlanningPnDbContext;
        private readonly PairItemWichSiteHelper _pairItemWichSiteHelper;

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
            _pairItemWichSiteHelper = new PairItemWichSiteHelper(itemsPlanningPnDbContext, coreHelper);
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

                            await CreatePlanningType9(areaRule, sdkDbContext, areaRulePlanningModel, core).ConfigureAwait(false);
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

                                        // var translation = planning.NameTranslations
                                        //     .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                        //     .Where(x => x.LanguageId == language.Id)
                                        //     .Select(x => x.Name)
                                        //     .FirstOrDefault();
                                        // var planningCase = await _itemsPlanningPnDbContext.PlanningCases
                                        //     .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                        //     .Where(x => x.WorkflowState != Constants.WorkflowStates.Retracted)
                                        //     .Where(x => x.PlanningId == planning.Id)
                                        //     .Where(x => x.MicrotingSdkeFormId == planning.RelatedEFormId)
                                        //     .FirstOrDefaultAsync().ConfigureAwait(false);
                                        //
                                        // if (planningCase == null)
                                        // {
                                        //     planningCase = new PlanningCase
                                        //     {
                                        //         PlanningId = planning.Id,
                                        //         Status = 66,
                                        //         MicrotingSdkeFormId = planning.RelatedEFormId
                                        //     };
                                        //     await planningCase.Create(_itemsPlanningPnDbContext).ConfigureAwait(false);
                                        // }
                                        //
                                        // mainElement.Label = string.IsNullOrEmpty(planning.PlanningNumber)
                                        //     ? ""
                                        //     : planning.PlanningNumber;
                                        // if (!string.IsNullOrEmpty(translation))
                                        // {
                                        //     mainElement.Label +=
                                        //         string.IsNullOrEmpty(mainElement.Label)
                                        //             ? $"{translation}"
                                        //             : $" - {translation}";
                                        // }
                                        //
                                        // if (!string.IsNullOrEmpty(planning.BuildYear))
                                        // {
                                        //     mainElement.Label += string.IsNullOrEmpty(mainElement.Label)
                                        //         ? $"{planning.BuildYear}"
                                        //         : $" - {planning.BuildYear}";
                                        // }
                                        //
                                        // if (!string.IsNullOrEmpty(planning.Type))
                                        // {
                                        //     mainElement.Label += string.IsNullOrEmpty(mainElement.Label)
                                        //         ? $"{planning.Type}"
                                        //         : $" - {planning.Type}";
                                        // }
                                        //
                                        // if (mainElement.ElementList.Count == 1)
                                        // {
                                        //     mainElement.ElementList[0].Label = mainElement.Label;
                                        // }
                                        //
                                        // var folder =
                                        //     await sdkDbContext.Folders.SingleAsync(x => x.Id == planning.SdkFolderId).ConfigureAwait(false);
                                        // var folderMicrotingId = folder.MicrotingUid.ToString();
                                        //
                                        // mainElement.CheckListFolderName = folderMicrotingId;
                                        // mainElement.StartDate = DateTime.Now.ToUniversalTime();
                                        // mainElement.EndDate = DateTime.Now.AddYears(10).ToUniversalTime();
                                        // var planningCaseSite = new PlanningCaseSite
                                        // {
                                        //     MicrotingSdkSiteId = i,
                                        //     MicrotingSdkeFormId = planning.RelatedEFormId,
                                        //     Status = 66,
                                        //     PlanningId = planning.Id,
                                        //     PlanningCaseId = planningCase.Id
                                        // };
                                        //
                                        // await planningCaseSite.Create(_itemsPlanningPnDbContext).ConfigureAwait(false);
                                        // mainElement.Repeated = 0;
                                        // var caseId = await core.CaseCreate(mainElement, "", (int) sdkSite.MicrotingUid,
                                        //     null).ConfigureAwait(false);
                                        // if (caseId != null)
                                        // {
                                        //     if (sdkDbContext.Cases.Any(x => x.MicrotingUid == caseId))
                                        //     {
                                        //         planningCaseSite.MicrotingSdkCaseId =
                                        //             sdkDbContext.Cases.Single(x => x.MicrotingUid == caseId).Id;
                                        //     }
                                        //     else
                                        //     {
                                        //         planningCaseSite.MicrotingCheckListSitId =
                                        //             sdkDbContext.CheckListSites.Single(x => x.MicrotingUid == caseId)
                                        //                 .Id;
                                        //     }
                                        //
                                        //     await planningCaseSite.Update(_itemsPlanningPnDbContext).ConfigureAwait(false);
                                        // }
                                        //
                                        // var now = DateTime.UtcNow;
                                        // var dbPlanning =
                                        //     await _itemsPlanningPnDbContext.Plannings.SingleAsync(x =>
                                        //         x.Id == planning.Id).ConfigureAwait(false);
                                        // dbPlanning.NextExecutionTime =
                                        //     new DateTime(now.Year, now.Month, now.Day, 0, 0, 0);
                                        // dbPlanning.LastExecutedTime =
                                        //     new DateTime(now.Year, now.Month, now.Day, 0, 0, 0);
                                        // await dbPlanning.Update(_itemsPlanningPnDbContext).ConfigureAwait(false);
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

                                    await CreatePlanningType10(areaRule, sdkDbContext, areaRulePlanningModel, core).ConfigureAwait(false);
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
                                    await rulePlanning.PlanningSites
                                        .First(x => x.SiteId == siteId)
                                        .Delete(_backendConfigurationPnDbContext).ConfigureAwait(false);
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
                                                    var planningForType2TypeTankOpen = await CreateItemPlanningObject(
                                                        eformId,
                                                        eformName, areaRule.AreaRulesPlannings[0].FolderId,
                                                        areaRulePlanningModel, areaRule).ConfigureAwait(false);
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
                                                    await _pairItemWichSiteHelper.Pair(
                                                        rulePlanning.PlanningSites.Select(x => x.SiteId).ToList(),
                                                        eformId,
                                                        planningForType2TypeTankOpen.Id,
                                                        areaRule.AreaRulesPlannings[0].FolderId).ConfigureAwait(false);
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
                                                        await DeleteItemPlanning(areaRule.AreaRulesPlannings[0]
                                                            .ItemPlanningId).ConfigureAwait(false);
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
                                                    var planningForType2AlarmYes = await CreateItemPlanningObject(
                                                        eformId,
                                                        eformName, areaRule.AreaRulesPlannings[1].FolderId,
                                                        areaRulePlanningModel, areaRule).ConfigureAwait(false);
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
                                                    await _pairItemWichSiteHelper.Pair(
                                                        rulePlanning.PlanningSites.Select(x => x.SiteId).ToList(),
                                                        eformId,
                                                        planningForType2AlarmYes.Id,
                                                        areaRule.AreaRulesPlannings[1].FolderId).ConfigureAwait(false);
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
                                                        await DeleteItemPlanning(areaRule.AreaRulesPlannings[1]
                                                            .ItemPlanningId).ConfigureAwait(false);
                                                        areaRule.AreaRulesPlannings[1].ItemPlanningId = 0;
                                                        await areaRule.AreaRulesPlannings[1]
                                                            .Update(_backendConfigurationPnDbContext).ConfigureAwait(false);
                                                    }
                                                }

                                                /*areaRule.EformName must be "03. Kontrol konstruktion"*/
                                                var planningForType2 = await CreateItemPlanningObject(
                                                    (int) areaRule.EformId,
                                                    areaRule.EformName, areaRule.AreaRulesPlannings[2].FolderId,
                                                    areaRulePlanningModel, areaRule).ConfigureAwait(false);
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
                                                await _pairItemWichSiteHelper.Pair(
                                                    rulePlanning.PlanningSites.Select(x => x.SiteId).ToList(),
                                                    (int) areaRule.EformId,
                                                    planningForType2.Id,
                                                    areaRule.AreaRulesPlannings[2].FolderId).ConfigureAwait(false);
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
                                                            await CreateItemPlanningObject(eformId, eformName,
                                                                areaRule.AreaRulesPlannings[0].FolderId,
                                                                areaRulePlanningModel,
                                                                areaRule).ConfigureAwait(false);
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
                                                            await DeleteItemPlanning(areaRule.AreaRulesPlannings[0]
                                                                .ItemPlanningId).ConfigureAwait(false);
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
                                                    var planningForType6One = await CreateItemPlanningObject(eformIdOne,
                                                        eformNameOne, areaRule.AreaRulesPlannings[1].FolderId,
                                                        areaRulePlanningModel, areaRule).ConfigureAwait(false);
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
                                                    var planningForType6Two = await CreateItemPlanningObject(eformIdTwo,
                                                        eformNameTwo, areaRule.AreaRulesPlannings[2].FolderId,
                                                        areaRulePlanningModel, areaRule).ConfigureAwait(false);
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
                                                    await _pairItemWichSiteHelper.Pair(
                                                        rulePlanning.PlanningSites.Select(x => x.SiteId).ToList(),
                                                        eformIdOne,
                                                        planningForType6One.Id,
                                                        areaRule.AreaRulesPlannings[1].FolderId).ConfigureAwait(false);
                                                    await _pairItemWichSiteHelper.Pair(
                                                        rulePlanning.PlanningSites.Select(x => x.SiteId).ToList(),
                                                        eformIdTwo,
                                                        planningForType6Two.Id,
                                                        areaRule.AreaRulesPlannings[2].FolderId).ConfigureAwait(false);
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

                                                var planning = await CreateItemPlanningObject((int) areaRule.EformId,
                                                    areaRule.EformName, areaRule.FolderId, areaRulePlanningModel,
                                                    areaRule).ConfigureAwait(false);
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
                                                await _pairItemWichSiteHelper.Pair(
                                                    rulePlanning.PlanningSites
                                                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                                        .Select(x => x.SiteId).ToList(),
                                                    (int) areaRule.EformId,
                                                    planning.Id,
                                                    areaRule.FolderId).ConfigureAwait(false);
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

                                            await DeleteItemPlanning(rulePlanning.ItemPlanningId).ConfigureAwait(false);

                                            rulePlanning.ItemPlanningId = 0;
                                            rulePlanning.Status = false;
                                            await rulePlanning.Update(_backendConfigurationPnDbContext).ConfigureAwait(false);
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
                                                            var clSites = await sdkDbContext.CheckListSites.SingleAsync(
                                                                x =>
                                                                    x.Id == planningCaseSite.MicrotingCheckListSitId).ConfigureAwait(false);

                                                            await core.CaseDelete(clSites.MicrotingUid).ConfigureAwait(false);
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
                                                await _pairItemWichSiteHelper.Pair(
                                                    sitesForCreate.Select(x => x.SiteId).ToList(),
                                                    planning.RelatedEFormId,
                                                    planning.Id,
                                                    (int) planning.SdkFolderId).ConfigureAwait(false);
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
                        await CreatePlanningType2(areaRule, sdkDbContext, areaRulePlanningModel, core).ConfigureAwait(false);
                        break;
                    }
                case AreaTypesEnum.Type3: // stables and tail bite
                    {
                        await CreatePlanningType3(areaRule, sdkDbContext, areaRulePlanningModel, core).ConfigureAwait(false);
                        break;
                    }
                case AreaTypesEnum.Type5: // recuring tasks(mon-sun)
                    {
                        await CreatePlanningType5(areaRule, areaRulePlanningModel).ConfigureAwait(false);
                        break;
                    }
                case AreaTypesEnum.Type6: // heat pumps
                    {
                        await CreatePlanningType6(areaRule, sdkDbContext, areaRulePlanningModel, core).ConfigureAwait(false);
                        break;
                    }
                case AreaTypesEnum.Type9: // chemical APV
                    {
                        await CreatePlanningType9(areaRule, sdkDbContext, areaRulePlanningModel, core).ConfigureAwait(false);
                        break;
                    }
                case AreaTypesEnum.Type10:
                {
                    await CreatePlanningType10(areaRule, sdkDbContext, areaRulePlanningModel, core).ConfigureAwait(false);
                    break;
                }
                default:
                    {
                        await CreatePlanningDefaultType(areaRule, sdkDbContext, areaRulePlanningModel, core).ConfigureAwait(false);
                        break;
                    }
            }

            return new OperationDataResult<AreaRuleModel>(true,
                _backendConfigurationLocalizationService.GetString("SuccessfullyCreatedPlanning"));
        }

        private async Task DeleteItemPlanning(int itemPlanningId)
        {
            if (itemPlanningId != 0)
            {
                var planning = await _itemsPlanningPnDbContext.Plannings
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Id == itemPlanningId)
                    .Include(x => x.PlanningSites)
                    .Include(x => x.NameTranslations)
                    .FirstAsync().ConfigureAwait(false);
                planning.UpdatedByUserId = _userService.UserId;
                foreach (var planningSite in planning.PlanningSites
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed))
                {
                    planningSite.UpdatedByUserId = _userService.UserId;
                    await planningSite.Delete(_itemsPlanningPnDbContext).ConfigureAwait(false);
                }

                var core = await _coreHelper.GetCore().ConfigureAwait(false);
                var sdkDbContext = core.DbContextHelper.GetDbContext();
                await using var _ = sdkDbContext.ConfigureAwait(false);
                var planningCases = await _itemsPlanningPnDbContext.PlanningCases
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.PlanningId == planning.Id)
                    .ToListAsync().ConfigureAwait(false);

                foreach (var planningCase in planningCases)
                {
                    var planningCaseSites = await _itemsPlanningPnDbContext.PlanningCaseSites
                        .Where(x => x.PlanningCaseId == planningCase.Id)
                        .Where(planningCaseSite => planningCaseSite.MicrotingSdkCaseId != 0 || planningCaseSite.MicrotingCheckListSitId != 0)
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .ToListAsync().ConfigureAwait(false);
                    foreach (var planningCaseSite in planningCaseSites)
                    {
                        var result =
                            await sdkDbContext.Cases.SingleOrDefaultAsync(x => x.Id == planningCaseSite.MicrotingSdkCaseId).ConfigureAwait(false);
                        if (result is {MicrotingUid: { }})
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

                var nameTranslationsPlanning =
                    planning.NameTranslations
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .ToList();

                foreach (var translation in nameTranslationsPlanning)
                {
                    await translation.Delete(_itemsPlanningPnDbContext).ConfigureAwait(false);
                }

                await planning.Delete(_itemsPlanningPnDbContext).ConfigureAwait(false);


                if (!_itemsPlanningPnDbContext.PlanningSites.AsNoTracking().Any(x => x.PlanningId == planning.Id && x.WorkflowState != Constants.WorkflowStates.Removed))
                {
                    var complianceList = await _backendConfigurationPnDbContext.Compliances
                        .Where(x => x.PlanningId == planning.Id).AsNoTracking().ToListAsync().ConfigureAwait(false);
                    foreach (var compliance in complianceList)
                    {
                        var dbCompliacne =
                            await _backendConfigurationPnDbContext.Compliances.SingleAsync(x => x.Id == compliance.Id).ConfigureAwait(false);
                        await dbCompliacne.Delete(_backendConfigurationPnDbContext).ConfigureAwait(false);
                        var property = await _backendConfigurationPnDbContext.Properties.SingleAsync(x => x.Id == compliance.PropertyId).ConfigureAwait(false);
                        if (_backendConfigurationPnDbContext.Compliances.Any(x => x.PropertyId == property.Id && x.Deadline < DateTime.UtcNow && x.WorkflowState != Constants.WorkflowStates.Removed))
                        {
                            property.ComplianceStatusThirty = 2;
                            property.ComplianceStatus = 2;
                        }
                        else
                        {
                            if (!_backendConfigurationPnDbContext.Compliances.Any(x =>
                                    x.PropertyId == property.Id && x.WorkflowState != Constants.WorkflowStates.Removed))
                            {
                                property.ComplianceStatusThirty = 0;
                                property.ComplianceStatus = 0;
                            }
                        }

                        await property.Update(_backendConfigurationPnDbContext).ConfigureAwait(false);
                    }
                }
            }
        }

        private async Task<Planning> CreateItemPlanningObject(int eformId, string eformName, int folderId,
            AreaRulePlanningModel areaRulePlanningModel, AreaRule areaRule)
        {
            var propertyItemPlanningTagId = await _backendConfigurationPnDbContext.Properties
                .Where(x => x.Id == areaRule.PropertyId)
                .Select(x => x.ItemPlanningTagId)
                .FirstAsync().ConfigureAwait(false);
            return new Planning
            {
                CreatedByUserId = _userService.UserId,
                Enabled = areaRulePlanningModel.Status,
                RelatedEFormId = eformId,
                RelatedEFormName = eformName,
                SdkFolderId = folderId,
                DaysBeforeRedeploymentPushMessageRepeat = false,
                DaysBeforeRedeploymentPushMessage = 5,
                PushMessageOnDeployment = areaRulePlanningModel.SendNotifications,
                StartDate = areaRulePlanningModel.StartDate,
                IsLocked = true,
                IsEditable = false,
                IsHidden = true,
                PlanningSites = areaRulePlanningModel.AssignedSites
                    .Select(x =>
                        new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.PlanningSite
                        {
                            SiteId = x.SiteId,
                        })
                    .ToList(),
                PlanningsTags = new List<PlanningsTags>
                {
                    new() {PlanningTagId = areaRule.Area.ItemPlanningTagId,},
                    new() {PlanningTagId = propertyItemPlanningTagId,},
                }
            };
        }

        private AreaRulePlanning CreateAreaRulePlanningObject(AreaRulePlanningModel areaRulePlanningModel,
            AreaRule areaRule, int planningId, int folderId)
        {
            var areaRulePlanning = new AreaRulePlanning
            {
                AreaId = areaRule.AreaId,
                CreatedByUserId = _userService.UserId,
                UpdatedByUserId = _userService.UserId,
                StartDate = areaRulePlanningModel.StartDate,
                Status = areaRulePlanningModel.Status,
                SendNotifications = areaRulePlanningModel.SendNotifications,
                AreaRuleId = areaRulePlanningModel.RuleId,
                ItemPlanningId = planningId,
                FolderId = folderId,
                PropertyId = areaRulePlanningModel.PropertyId,
                PlanningSites = areaRulePlanningModel.AssignedSites.Select(x => new PlanningSite
                {
                    SiteId = x.SiteId,
                    CreatedByUserId = _userService.UserId,
                    UpdatedByUserId = _userService.UserId,
                    AreaId = areaRule.AreaId,
                    AreaRuleId = areaRule.Id
                }).ToList(),
                ComplianceEnabled = areaRulePlanningModel.ComplianceEnabled,
            };
            if (areaRulePlanningModel.TypeSpecificFields != null)
            {
                areaRulePlanning.DayOfMonth = areaRulePlanningModel.TypeSpecificFields.DayOfMonth == 0
                    ? 1
                    : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                areaRulePlanning.DayOfWeek = areaRulePlanningModel.TypeSpecificFields.DayOfWeek == 0
                    ? 1
                    : areaRulePlanningModel.TypeSpecificFields.DayOfWeek;
                areaRulePlanning.HoursAndEnergyEnabled = areaRulePlanningModel.TypeSpecificFields.HoursAndEnergyEnabled;
                areaRulePlanning.EndDate = areaRulePlanningModel.TypeSpecificFields.EndDate;
                areaRulePlanning.RepeatEvery = areaRulePlanningModel.TypeSpecificFields.RepeatEvery;
                areaRulePlanning.RepeatType = areaRulePlanningModel.TypeSpecificFields.RepeatType;
            }

            if (areaRule.Type != null)
            {
                areaRulePlanning.Type = (AreaRuleT2TypesEnum)areaRule.Type;
            }

            if (areaRule.Alarm != null)
            {
                areaRulePlanning.Alarm = (AreaRuleT2AlarmsEnum)areaRule.Alarm;
            }

            return areaRulePlanning;
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

        private async Task CreatePlanningType2(AreaRule areaRule, MicrotingDbContext sdkDbContext, AreaRulePlanningModel areaRulePlanningModel, eFormCore.Core core)
        {
            var translatesForFolder = areaRule.AreaRuleTranslations
                .Select(x => new CommonTranslationsModel
                {
                    LanguageId = x.LanguageId,
                    Description = "",
                    Name = x.Name,
                }).ToList();
            // create folder with name tank
            var folderId = await core.FolderCreate(translatesForFolder, areaRule.FolderId).ConfigureAwait(false);
            var planningForType2TypeTankOpenId = 0;
            if (areaRule.Type == AreaRuleT2TypesEnum.Open)
            {
                const string eformName = "03. Kontrol flydelag";
                var eformId = await sdkDbContext.CheckListTranslations
                    .Where(x => x.Text == eformName)
                    .Select(x => x.CheckListId)
                    .FirstAsync().ConfigureAwait(false);

                if (areaRulePlanningModel.Status)
                {
                    var planningForType2TypeTankOpen = await CreateItemPlanningObject(eformId, eformName,
                        folderId, areaRulePlanningModel, areaRule).ConfigureAwait(false);
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
                                    .Where(x => x.LanguageId == 3)
                                    .Select(x => x.Name)
                                    .FirstOrDefault() + ": Schwimmende Ebene",
                            },
                            // new PlanningNameTranslation
                            // {
                            //     LanguageId = 4,// uk-ua
                            //     Name = areaRule.AreaRuleTranslations
                            //         .Where(x => x.LanguageId == 4)
                            //         .Select(x => x.Name)
                            //         .FirstOrDefault() + "Перевірте плаваючий шар",
                            // },
                        };
                    planningForType2TypeTankOpen.RepeatEvery = 1;
                    planningForType2TypeTankOpen.RepeatType = RepeatType.Month;
                    if (areaRulePlanningModel.TypeSpecificFields is not null)
                    {
                        planningForType2TypeTankOpen.RepeatUntil =
                            areaRulePlanningModel.TypeSpecificFields.EndDate;
                        planningForType2TypeTankOpen.DayOfWeek =
                            (DayOfWeek)areaRulePlanningModel.TypeSpecificFields.DayOfWeek == 0
                                ? DayOfWeek.Monday
                                : (DayOfWeek)areaRulePlanningModel.TypeSpecificFields.DayOfWeek;
                        planningForType2TypeTankOpen.DayOfMonth =
                            areaRulePlanningModel.TypeSpecificFields.DayOfMonth == 0 ? 1 : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                    }

                    await planningForType2TypeTankOpen.Create(_itemsPlanningPnDbContext).ConfigureAwait(false);
                    await _pairItemWichSiteHelper.Pair(
                        areaRulePlanningModel.AssignedSites.Select(x => x.SiteId).ToList(), eformId,
                        planningForType2TypeTankOpen.Id,
                        folderId).ConfigureAwait(false);
                    planningForType2TypeTankOpenId = planningForType2TypeTankOpen.Id;
                }
            }

            var areaRulePlanningForType2TypeTankOpen = CreateAreaRulePlanningObject(areaRulePlanningModel,
                areaRule, planningForType2TypeTankOpenId,
                folderId);
            await areaRulePlanningForType2TypeTankOpen.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);

            var planningForType2AlarmYesId = 0;
            if (areaRule.Type is AreaRuleT2TypesEnum.Open or AreaRuleT2TypesEnum.Closed
                && areaRule.Alarm is AreaRuleT2AlarmsEnum.Yes)
            {
                const string eformName = "03. Kontrol alarmanlæg gyllebeholder";
                var eformId = await sdkDbContext.CheckListTranslations
                    .Where(x => x.Text == eformName)
                    .Select(x => x.CheckListId)
                    .FirstAsync().ConfigureAwait(false);

                if (areaRulePlanningModel.Status)
                {
                    var planningForType2AlarmYes = await CreateItemPlanningObject(eformId, eformName, folderId,
                        areaRulePlanningModel, areaRule).ConfigureAwait(false);
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
                    if (areaRulePlanningModel.TypeSpecificFields != null)
                    {
                        planningForType2AlarmYes.RepeatUntil =
                            areaRulePlanningModel.TypeSpecificFields.EndDate;
                        planningForType2AlarmYes.DayOfWeek =
                            (DayOfWeek)areaRulePlanningModel.TypeSpecificFields.DayOfWeek == 0 ? DayOfWeek.Monday :
                                (DayOfWeek)areaRulePlanningModel.TypeSpecificFields.DayOfWeek;
                        planningForType2AlarmYes.DayOfMonth =
                            areaRulePlanningModel.TypeSpecificFields.DayOfMonth == 0 ? 1 : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                    }

                    await planningForType2AlarmYes.Create(_itemsPlanningPnDbContext).ConfigureAwait(false);
                    await _pairItemWichSiteHelper.Pair(
                        areaRulePlanningModel.AssignedSites.Select(x => x.SiteId).ToList(), eformId,
                        planningForType2AlarmYes.Id,
                        folderId).ConfigureAwait(false);
                    planningForType2AlarmYesId = planningForType2AlarmYes.Id;
                }
            }

            var areaRulePlanningForType2AlarmYes = CreateAreaRulePlanningObject(areaRulePlanningModel, areaRule,
                planningForType2AlarmYesId,
                folderId);
            await areaRulePlanningForType2AlarmYes.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);

            var planningForType2Id = 0;
            if (areaRulePlanningModel.Status)
            {
                //areaRule.EformName must be "03. Kontrol konstruktion"
                var planningForType2 = await CreateItemPlanningObject((int)areaRule.EformId,
                    areaRule.EformName, folderId, areaRulePlanningModel, areaRule).ConfigureAwait(false);
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
                if (areaRulePlanningModel.TypeSpecificFields != null)
                {
                    planningForType2.RepeatUntil =
                        areaRulePlanningModel.TypeSpecificFields.EndDate;
                    planningForType2.DayOfWeek =
                        (DayOfWeek)areaRulePlanningModel.TypeSpecificFields.DayOfWeek == 0 ? DayOfWeek.Monday :
                            (DayOfWeek)areaRulePlanningModel.TypeSpecificFields.DayOfWeek;
                    planningForType2.DayOfMonth =
                        areaRulePlanningModel.TypeSpecificFields.DayOfMonth == 0 ? 1 : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                }

                await planningForType2.Create(_itemsPlanningPnDbContext).ConfigureAwait(false);
                await _pairItemWichSiteHelper.Pair(
                    areaRulePlanningModel.AssignedSites.Select(x => x.SiteId).ToList(), (int)areaRule.EformId,
                    planningForType2.Id,
                    folderId).ConfigureAwait(false);
                planningForType2Id = planningForType2.Id;
            }

            var areaRulePlanningForType2 = CreateAreaRulePlanningObject(areaRulePlanningModel, areaRule,
                planningForType2Id,
                folderId);
            await areaRulePlanningForType2.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);
        }

        private async Task CreatePlanningType3(AreaRule areaRule, MicrotingDbContext sdkDbContext, AreaRulePlanningModel areaRulePlanningModel, eFormCore.Core core)
        {
            await CreatePlanningDefaultType(areaRule, sdkDbContext, areaRulePlanningModel, core).ConfigureAwait(false);

            var sites = areaRulePlanningModel.AssignedSites.Select(x => x.SiteId).ToList();
            if (areaRulePlanningModel.Status)
            {
                foreach (var siteId in sites)
                {
                    var site = await sdkDbContext.Sites.SingleOrDefaultAsync(x => x.Id == siteId).ConfigureAwait(false);
                    var language =
                        await sdkDbContext.Languages.SingleOrDefaultAsync(x => x.Id == site.LanguageId).ConfigureAwait(false);
                    var entityListUid = await _backendConfigurationPnDbContext.AreaProperties
                        .Where(x => x.PropertyId == areaRule.PropertyId)
                        .Where(x => x.AreaId == areaRule.AreaId)
                        .Select(x => x.GroupMicrotingUuid)
                        .FirstAsync().ConfigureAwait(false);
                    if (!sdkDbContext.CheckListSites
                            .Any(x =>
                                x.CheckListId == areaRule.EformId &&
                                x.SiteId == siteId &&
                                x.WorkflowState != Constants.WorkflowStates.Removed))
                    {
                        var mainElement = await core.ReadeForm((int)areaRule.EformId, language).ConfigureAwait(false);
                        // todo add group id to eform
                        var folder = await sdkDbContext.Folders.SingleAsync(x => x.Id == areaRule.FolderId).ConfigureAwait(false);
                        var folderMicrotingId = folder.MicrotingUid.ToString();
                        mainElement.Repeated = -1;
                        mainElement.CheckListFolderName = folderMicrotingId;
                        mainElement.StartDate = DateTime.Now.ToUniversalTime();
                        mainElement.EndDate = DateTime.Now.AddYears(10).ToUniversalTime();
                        mainElement.DisplayOrder = 10000000;
                        ((EntitySelect)((DataElement)mainElement.ElementList[0]).DataItemList[1]).Source = entityListUid;
                        /*var caseId = */
                        await core.CaseCreate(mainElement, "", (int)site.MicrotingUid, folder.Id).ConfigureAwait(false);
                    }
                }
            }
        }

        private async Task CreatePlanningType5(AreaRule areaRule, AreaRulePlanningModel areaRulePlanningModel)
        {

            var folderIds = await _backendConfigurationPnDbContext.ProperyAreaFolders
                .Include(x => x.AreaProperty)
                .Where(x => x.AreaProperty.PropertyId == areaRulePlanningModel.PropertyId)
                .Where(x => x.AreaProperty.AreaId == areaRule.AreaId)
                .Select(x => x.FolderId)
                .Skip(1)
                .ToListAsync().ConfigureAwait(false);
            var folderId = folderIds[areaRule.DayOfWeek];
            var planningId = 0;
            if (areaRulePlanningModel.Status)
            {
                areaRulePlanningModel.TypeSpecificFields ??=
                    new AreaRuleTypePlanningModel(); // if areaRulePlanningModel.TypeSpecificFields == null -> areaRulePlanningModel.TypeSpecificFields = new()
                areaRulePlanningModel.TypeSpecificFields.RepeatEvery =
                    areaRule.RepeatEvery; // repeat every mast be from area rule
                var planning = await CreateItemPlanningObject((int)areaRule.EformId, areaRule.EformName,
                    folderId, areaRulePlanningModel, areaRule).ConfigureAwait(false);
                planning.NameTranslations = areaRule.AreaRuleTranslations.Select(
                    areaRuleAreaRuleTranslation => new PlanningNameTranslation
                    {
                        LanguageId = areaRuleAreaRuleTranslation.LanguageId,
                        Name = areaRuleAreaRuleTranslation.Name,
                    }).ToList();

                if (areaRulePlanningModel.TypeSpecificFields != null) // it not need
                {
                    if (areaRulePlanningModel.TypeSpecificFields.RepeatType != null)
                    {
                        planning.RepeatType =
                            (RepeatType)areaRulePlanningModel.TypeSpecificFields.RepeatType;
                    }

                    if (areaRulePlanningModel.TypeSpecificFields.RepeatEvery != null)
                    {
                        planning.RepeatEvery =
                            (int)areaRulePlanningModel.TypeSpecificFields.RepeatEvery;
                    }

                    planning.RepeatUntil =
                        areaRulePlanningModel.TypeSpecificFields.EndDate;
                    planning.DayOfWeek =
                        (DayOfWeek)areaRulePlanningModel.TypeSpecificFields.DayOfWeek == 0 ? DayOfWeek.Sunday : (DayOfWeek)areaRulePlanningModel.TypeSpecificFields.DayOfWeek;
                    planning.DayOfMonth =
                        areaRulePlanningModel.TypeSpecificFields.DayOfMonth == 0 ? 1 : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                }

                await planning.Create(_itemsPlanningPnDbContext).ConfigureAwait(false);
                await _pairItemWichSiteHelper.Pair(
                    areaRulePlanningModel.AssignedSites.Select(x => x.SiteId).ToList(), (int)areaRule.EformId,
                    planning.Id,
                    folderId).ConfigureAwait(false);
                planningId = planning.Id;

            }
            var areaRulePlanning = CreateAreaRulePlanningObject(areaRulePlanningModel, areaRule, planningId,
                folderId);
            await areaRulePlanning.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);
        }

        private async Task CreatePlanningType6(AreaRule areaRule, MicrotingDbContext sdkDbContext, AreaRulePlanningModel areaRulePlanningModel, eFormCore.Core core)
        {
            // create folder with name heat pump
            var translatesForFolder = areaRule.AreaRuleTranslations
                .Select(x => new CommonTranslationsModel
                {
                    LanguageId = x.LanguageId,
                    Description = "",
                    Name = x.Name,
                }).ToList();
            var folderId = await core.FolderCreate(translatesForFolder, areaRule.FolderId).ConfigureAwait(false);

            var planningForType6HoursAndEnergyEnabledId = 0;
            var planningForType6IdOne = 0;
            var planningForType6IdTwo = 0;
            if (areaRulePlanningModel.TypeSpecificFields?.HoursAndEnergyEnabled is true &&
                areaRulePlanningModel.Status)
            {
                await areaRule.Update(_backendConfigurationPnDbContext).ConfigureAwait(false);
                const string eformName = "10. Varmepumpe timer og energi";
                var eformId = await sdkDbContext.CheckListTranslations
                    .Where(x => x.Text == eformName)
                    .Select(x => x.CheckListId)
                    .FirstAsync().ConfigureAwait(false);
                var planningForType6HoursAndEnergyEnabled = await CreateItemPlanningObject(eformId, eformName,
                    folderId, areaRulePlanningModel, areaRule).ConfigureAwait(false);
                planningForType6HoursAndEnergyEnabled.NameTranslations = new List<PlanningNameTranslation>
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
                planningForType6HoursAndEnergyEnabled.RepeatEvery = 12;
                planningForType6HoursAndEnergyEnabled.RepeatType = RepeatType.Month;
                if (areaRulePlanningModel.TypeSpecificFields != null)
                {
                    planningForType6HoursAndEnergyEnabled.DayOfWeek =
                        (DayOfWeek)areaRulePlanningModel.TypeSpecificFields.DayOfWeek == 0
                            ? DayOfWeek.Monday
                            : (DayOfWeek)areaRulePlanningModel.TypeSpecificFields.DayOfWeek;
                    planningForType6HoursAndEnergyEnabled.DayOfMonth =
                        areaRulePlanningModel.TypeSpecificFields.DayOfMonth == 0 ? 1 : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                }

                await planningForType6HoursAndEnergyEnabled.Create(_itemsPlanningPnDbContext).ConfigureAwait(false);
                await _pairItemWichSiteHelper.Pair(
                    areaRulePlanningModel.AssignedSites.Select(x => x.SiteId).ToList(), eformId,
                    planningForType6HoursAndEnergyEnabled.Id,
                    folderId).ConfigureAwait(false);
                planningForType6HoursAndEnergyEnabledId = planningForType6HoursAndEnergyEnabled.Id;
            }

            if (areaRulePlanningModel.Status)
            {
                const string eformNameOne = "10. Varmepumpe serviceaftale";
                var eformIdOne = await sdkDbContext.CheckListTranslations
                    .Where(x => x.Text == eformNameOne)
                    .Select(x => x.CheckListId)
                    .FirstAsync().ConfigureAwait(false);
                const string eformNameTwo = "10. Varmepumpe driftsstop";
                var eformIdTwo = await sdkDbContext.CheckListTranslations
                    .Where(x => x.Text == eformNameTwo)
                    .Select(x => x.CheckListId)
                    .FirstAsync().ConfigureAwait(false);
                var planningForType6One = await CreateItemPlanningObject(eformIdOne, eformNameOne, folderId,
                    areaRulePlanningModel, areaRule).ConfigureAwait(false);
                planningForType6One.NameTranslations = new List<PlanningNameTranslation>
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
                var planningForType6Two = await CreateItemPlanningObject(eformIdTwo, eformNameTwo, folderId,
                    areaRulePlanningModel, areaRule).ConfigureAwait(false);
                planningForType6Two.NameTranslations = new List<PlanningNameTranslation>
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
                planningForType6Two.RepeatEvery = 0;
                planningForType6Two.RepeatType = RepeatType.Day;
                if (areaRulePlanningModel.TypeSpecificFields is not null)
                {
                    planningForType6One.DayOfMonth =
                        areaRulePlanningModel.TypeSpecificFields.DayOfMonth == 0 ? 1 : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                    planningForType6One.DayOfWeek =
                        (DayOfWeek?)areaRulePlanningModel.TypeSpecificFields.DayOfWeek == 0 ? DayOfWeek.Monday : (DayOfWeek?)areaRulePlanningModel.TypeSpecificFields.DayOfWeek;
                    planningForType6One.RepeatUntil =
                        areaRulePlanningModel.TypeSpecificFields.EndDate;

                    planningForType6Two.DayOfMonth =
                        areaRulePlanningModel.TypeSpecificFields.DayOfMonth == 0 ? 1 : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                    planningForType6Two.DayOfWeek =
                        (DayOfWeek?)areaRulePlanningModel.TypeSpecificFields.DayOfWeek == 0 ? DayOfWeek.Monday : (DayOfWeek?)areaRulePlanningModel.TypeSpecificFields.DayOfWeek;
                    planningForType6Two.RepeatUntil =
                        areaRulePlanningModel.TypeSpecificFields.EndDate;
                }

                await planningForType6One.Create(_itemsPlanningPnDbContext).ConfigureAwait(false);
                await _pairItemWichSiteHelper.Pair(
                    areaRulePlanningModel.AssignedSites.Select(x => x.SiteId).ToList(), eformIdOne,
                    planningForType6One.Id,
                    folderId).ConfigureAwait(false);
                planningForType6IdOne = planningForType6One.Id;
                await planningForType6Two.Create(_itemsPlanningPnDbContext).ConfigureAwait(false);
                await _pairItemWichSiteHelper.Pair(
                    areaRulePlanningModel.AssignedSites.Select(x => x.SiteId).ToList(), eformIdTwo,
                    planningForType6Two.Id,
                    folderId).ConfigureAwait(false);
                planningForType6IdTwo = planningForType6Two.Id;
            }

            var areaRulePlanningForType6HoursAndEnergyEnabled = CreateAreaRulePlanningObject(
                areaRulePlanningModel, areaRule, planningForType6HoursAndEnergyEnabledId,
                folderId);
            var areaRulePlanningForType6One = CreateAreaRulePlanningObject(areaRulePlanningModel, areaRule,
                planningForType6IdOne,
                folderId);
            var areaRulePlanningForType6Two = CreateAreaRulePlanningObject(areaRulePlanningModel, areaRule,
                planningForType6IdTwo,
                folderId);
            await areaRulePlanningForType6HoursAndEnergyEnabled.Create(
                _backendConfigurationPnDbContext).ConfigureAwait(false);
            await areaRulePlanningForType6One.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);
            await areaRulePlanningForType6Two.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);
        }

        private async Task CreatePlanningType9(AreaRule areaRule, MicrotingDbContext sdkDbContext, AreaRulePlanningModel areaRulePlanningModel, Core core)
        {
            var sites = areaRulePlanningModel.AssignedSites.Select(x => x.SiteId).ToList();
            if (areaRulePlanningModel.Status)
            {
                var siteId = sites.First();
                var site = await sdkDbContext.Sites.SingleOrDefaultAsync(x => x.Id == siteId).ConfigureAwait(false);
                var language =
                    await sdkDbContext.Languages.SingleOrDefaultAsync(x => x.Id == site.LanguageId).ConfigureAwait(false);
                var property = await _backendConfigurationPnDbContext.Properties
                    .FirstAsync(x => x.Id == areaRule.PropertyId).ConfigureAwait(false);
                var entityListUid = (int)property.EntitySearchListChemicals!;
                var entityListUidRegNo = (int)property.EntitySearchListChemicalRegNos!;
                var entityListUidAreas = (int)property.EntitySelectListChemicalAreas!;

                var mainElement = await core.ReadeForm((int)areaRule.EformId!, language).ConfigureAwait(false);
                // todo add group id to eform
                var folder = await sdkDbContext.Folders.SingleAsync(x => x.Id == areaRule.FolderId).ConfigureAwait(false);
                var folderTranslation = await sdkDbContext.Folders.Join(sdkDbContext.FolderTranslations,
                    f => f.Id, translation => translation.FolderId, (f, translation) => new
                    {
                        f.Id,
                        f.ParentId,
                        translation.Name,
                        f.MicrotingUid
                    }).FirstAsync(x => x.Name == "25.01 Opret kemiprodukt" && x.ParentId == folder.Id);
                var folderMicrotingId = folderTranslation.MicrotingUid.ToString();
                mainElement.Repeated = 0;
                mainElement.CheckListFolderName = folderMicrotingId;
                mainElement.StartDate = DateTime.Now.ToUniversalTime();
                mainElement.EndDate = DateTime.Now.AddYears(10).ToUniversalTime();
                mainElement.DisplayOrder = 10000000;
                mainElement.Label = "25.01 Opret kemiprodukt";
                mainElement.ElementList.First().Label = mainElement.Label;
                mainElement.ElementList.First().Description.InderValue = property.Name;
                ((EntitySelect) ((DataElement) mainElement.ElementList[0]).DataItemList[0]).Source = entityListUidAreas;
                ((EntitySelect) ((DataElement) mainElement.ElementList[0]).DataItemList[0]).Label = "Vælg rum for produkt";
                ((EntitySearch)((DataElement)mainElement.ElementList[0]).DataItemList[1]).EntityTypeId = entityListUid;
                // ((EntitySearch) ((DataElement) mainElement.ElementList[0]).DataItemList[0]).DisplayOrder = 2;
                ((EntitySearch)((DataElement)mainElement.ElementList[0]).DataItemList[2]).EntityTypeId = entityListUidRegNo;
                // ((EntitySearch) ((DataElement) mainElement.ElementList[0]).DataItemList[1]).DisplayOrder = 3;
                ((EntitySearch)((DataElement)mainElement.ElementList[0]).DataItemList[3]).EntityTypeId = entityListUid;
                // ((EntitySearch) ((DataElement) mainElement.ElementList[0]).DataItemList[2]).DisplayOrder = 4;
                ((EntitySearch)((DataElement)mainElement.ElementList[0]).DataItemList[4]).EntityTypeId = entityListUidRegNo;
                // ((EntitySearch) ((DataElement) mainElement.ElementList[0]).DataItemList[3]).DisplayOrder = 5;
                ((EntitySearch)((DataElement)mainElement.ElementList[0]).DataItemList[5]).EntityTypeId = entityListUid;
                // ((EntitySearch) ((DataElement) mainElement.ElementList[0]).DataItemList[4]).DisplayOrder = 6;
                ((EntitySearch)((DataElement)mainElement.ElementList[0]).DataItemList[6]).EntityTypeId = entityListUidRegNo;
                // ((EntitySearch) ((DataElement) mainElement.ElementList[0]).DataItemList[5]).DisplayOrder = 7;
                ((EntitySearch)((DataElement)mainElement.ElementList[0]).DataItemList[7]).EntityTypeId = entityListUid;
                // ((EntitySearch) ((DataElement) mainElement.ElementList[0]).DataItemList[6]).DisplayOrder = 8;
                ((EntitySearch)((DataElement)mainElement.ElementList[0]).DataItemList[8]).EntityTypeId = entityListUidRegNo;
                // ((EntitySearch) ((DataElement) mainElement.ElementList[0]).DataItemList[7]).DisplayOrder = 9;
                ((EntitySearch)((DataElement)mainElement.ElementList[0]).DataItemList[9]).EntityTypeId = entityListUid;
                // ((EntitySearch) ((DataElement) mainElement.ElementList[0]).DataItemList[8]).DisplayOrder = 10;
                ((EntitySearch)((DataElement)mainElement.ElementList[0]).DataItemList[10]).EntityTypeId = entityListUidRegNo;
                // ((EntitySearch) ((DataElement) mainElement.ElementList[0]).DataItemList[9]).DisplayOrder = 11;
                ((EntitySearch)((DataElement)mainElement.ElementList[0]).DataItemList[11]).EntityTypeId = entityListUid;
                // ((EntitySearch) ((DataElement) mainElement.ElementList[0]).DataItemList[10]).DisplayOrder = 12;
                ((EntitySearch)((DataElement)mainElement.ElementList[0]).DataItemList[12]).EntityTypeId = entityListUidRegNo;
                // ((EntitySearch) ((DataElement) mainElement.ElementList[0]).DataItemList[11]).DisplayOrder = 13;
                ((EntitySearch)((DataElement)mainElement.ElementList[0]).DataItemList[13]).EntityTypeId = entityListUid;
                // ((EntitySearch) ((DataElement) mainElement.ElementList[0]).DataItemList[12]).DisplayOrder = 14;
                ((EntitySearch)((DataElement)mainElement.ElementList[0]).DataItemList[14]).EntityTypeId = entityListUidRegNo;
                // ((EntitySearch) ((DataElement) mainElement.ElementList[0]).DataItemList[13]).DisplayOrder = 15;
                ((EntitySearch)((DataElement)mainElement.ElementList[0]).DataItemList[15]).EntityTypeId = entityListUid;
                // ((EntitySearch) ((DataElement) mainElement.ElementList[0]).DataItemList[14]).DisplayOrder = 16;
                ((EntitySearch)((DataElement)mainElement.ElementList[0]).DataItemList[16]).EntityTypeId = entityListUidRegNo;
                // ((EntitySearch) ((DataElement) mainElement.ElementList[0]).DataItemList[15]).DisplayOrder = 17;
                ((EntitySearch)((DataElement)mainElement.ElementList[0]).DataItemList[17]).EntityTypeId = entityListUid;
                // ((EntitySearch) ((DataElement) mainElement.ElementList[0]).DataItemList[16]).DisplayOrder = 18;
                ((EntitySearch)((DataElement)mainElement.ElementList[0]).DataItemList[18]).EntityTypeId = entityListUidRegNo;
                // ((EntitySearch) ((DataElement) mainElement.ElementList[0]).DataItemList[17]).DisplayOrder = 19;
                ((EntitySearch)((DataElement)mainElement.ElementList[0]).DataItemList[19]).EntityTypeId = entityListUid;
                // ((EntitySearch) ((DataElement) mainElement.ElementList[0]).DataItemList[18]).DisplayOrder = 20;
                ((EntitySearch)((DataElement)mainElement.ElementList[0]).DataItemList[20]).EntityTypeId = entityListUidRegNo;
                // ((EntitySearch) ((DataElement) mainElement.ElementList[0]).DataItemList[19]).DisplayOrder = 21;
                // ((SaveButton) ((DataElement) mainElement.ElementList[0]).DataItemList[20]).DisplayOrder = 22;
                ((DataElement)mainElement.ElementList[0]).DataItemList.RemoveAt(19);
                ((DataElement)mainElement.ElementList[0]).DataItemList.RemoveAt(17);
                ((DataElement)mainElement.ElementList[0]).DataItemList.RemoveAt(15);
                ((DataElement)mainElement.ElementList[0]).DataItemList.RemoveAt(13);
                ((DataElement)mainElement.ElementList[0]).DataItemList.RemoveAt(11);
                ((DataElement)mainElement.ElementList[0]).DataItemList.RemoveAt(9);
                ((DataElement)mainElement.ElementList[0]).DataItemList.RemoveAt(7);
                ((DataElement)mainElement.ElementList[0]).DataItemList.RemoveAt(5);
                ((DataElement)mainElement.ElementList[0]).DataItemList.RemoveAt(3);
                ((DataElement)mainElement.ElementList[0]).DataItemList.RemoveAt(1);
                FieldContainer fieldContainer = new FieldContainer(0, "Hvordan opretter jeg produkter",
                    new CDataValue(), Constants.FieldColors.Yellow, -1, "", new List<DataItem>());
                ((DataElement)mainElement.ElementList[0]).DataItemGroupList.Add(new FieldGroup("0", "Hvordan opretter jeg produkter",
                    "", Constants.FieldColors.Yellow, -1, "", new List<DataItem>()));

                var description = $"<br><strong>Placering</strong><br>Ejendom: {areaRule.Property.Name}<br><br>" +
                                  "I Bekæmpelsesmiddeldatabasen (BMD) som vedligeholdes af Miljøstyrelsen, kan du finde alle godkendte sprøjtemidler og biocider.<br>" +
                                  "Når du opretter dine produkter, bliver du automatisk påmindet på din mobil og på email, når produktets tilladelse udløber.<br><br>" +
                                  "<strong>Gør følgende for at oprette dit produt:</strong><br><br>" +
                                  "1. Vælg først hvilket rum produktet befinder sig i (fx kemirum).<br>" +
                                  "2. Fremsøg derefter kemiproduktet, ved at indtaste registrerings-nr. som står på produktets etikette (fx 1-202)<br>" +
                                  "3. Gentag pkt. 2 indtil du har oprettet alle dine produkter.<br>" +
                                  "4. Tryk på Opret<br><br>" +
                                  "Nu oprettes dine kemiprodukter, som efter få minutter bliver gjort tilgængelig i mappen \"25.02 Mine kemiprodukter\".<br><br>" +
                                  "<strong>Bemærk</strong><br>" +
                                  "Du kan oprette op til 10 produkter ad gangen.<br><br>" +
                                  "Hvis det indtastede registreringsnummer ikke findes eller ikke kan aflæses, så fjern og bortskaf produktet.<br><br>" +
                                  "Aflevér rester og tom emballage til den kommunale affaldsordning.";

                None none = new None(1, false, false, " ", description, Constants.FieldColors.Yellow, -1, false);
                ((FieldContainer) ((DataElement) mainElement.ElementList[0]).DataItemGroupList[0]).DataItemList.Add(none);
                // ((DataElement)mainElement.ElementList[0]).DataItemList.Add(none);
                // EntitySelect entitySelect = new EntitySelect(1, true, false, "Vælg rum for kemiprodukt", " ",
                //     Constants.FieldColors.Yellow, 1, false, 0, entityListUidAreas);
                // ((DataElement)mainElement.ElementList[0]).DataItemList.Add(entitySelect);
                var caseId = await core.CaseCreate(mainElement, "", (int)site!.MicrotingUid!, folderTranslation.Id).ConfigureAwait(false);

                var planning = await CreateItemPlanningObject((int)areaRule.EformId, areaRule.EformName,
                    areaRule.FolderId, areaRulePlanningModel, areaRule).ConfigureAwait(false);
                planning.RepeatEvery = 0;
                planning.RepeatType = RepeatType.Day;
                planning.StartDate = DateTime.Now.ToUniversalTime();
                planning.SdkFolderId = folderTranslation.Id;
                var now = DateTime.UtcNow;
                planning.LastExecutedTime = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0);
                await planning.Create(_itemsPlanningPnDbContext).ConfigureAwait(false);
                var planningCase = new PlanningCase
                {
                    PlanningId = planning.Id,
                    Status = 66,
                    MicrotingSdkeFormId = (int)areaRule.EformId
                };
                await planningCase.Create(_itemsPlanningPnDbContext).ConfigureAwait(false);
                var checkListSite = await sdkDbContext.CheckListSites.SingleAsync(x => x.MicrotingUid == caseId).ConfigureAwait(false);
                var planningCaseSite = new PlanningCaseSite
                {
                    MicrotingSdkSiteId = siteId,
                    MicrotingSdkeFormId = (int)areaRule.EformId,
                    Status = 66,
                    PlanningId = planning.Id,
                    PlanningCaseId = planningCase.Id,
                    MicrotingSdkCaseId = (int)caseId,
                    MicrotingCheckListSitId = checkListSite.Id
                };

                await planningCaseSite.Create(_itemsPlanningPnDbContext).ConfigureAwait(false);

                var areaRulePlanning = CreateAreaRulePlanningObject(areaRulePlanningModel, areaRule, planning.Id,
                    areaRule.FolderId);


                await areaRulePlanning.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);
                // }
            }
        }

        private async Task CreatePlanningType10(AreaRule areaRule, MicrotingDbContext sdkDbContext,
            AreaRulePlanningModel areaRulePlanningModel, Core core)
        {
            //var sites = areaRulePlanningModel.AssignedSites.Select(x => x.SiteId).ToList();
            if (areaRulePlanningModel.Status)
            {
                var poolHours = await _backendConfigurationPnDbContext.PoolHours
                    .Where(x => x.AreaRuleId == areaRule.Id)
                    //.Where(x => x.IsActive == true)
                    .ToListAsync().ConfigureAwait(false);

                var property = await _backendConfigurationPnDbContext.Properties.SingleAsync(x => x.Id == areaRule.PropertyId).ConfigureAwait(false);

                var lookupName = areaRule.AreaRuleTranslations.First().Name;

                if (lookupName == "Morgenrundtur" || lookupName == "Morning tour")
                {
                    var globalPlanningTag = await _itemsPlanningPnDbContext.PlanningTags.SingleOrDefaultAsync(x =>
                            x.Name == $"{property.Name} - {areaRule.AreaRuleTranslations.First().Name}")
                        .ConfigureAwait(false);

                    if (globalPlanningTag == null)
                    {
                        globalPlanningTag = new PlanningTag
                        {
                            Name = $"{property.Name} - Morgenrundtur",
                            CreatedByUserId = _userService.UserId,
                            UpdatedByUserId = _userService.UserId,
                        };

                        await globalPlanningTag.Create(_itemsPlanningPnDbContext).ConfigureAwait(false);
                    }

                    var planning = await CreateItemPlanningObject((int) areaRule.EformId, areaRule.EformName,
                        areaRule.FolderId, areaRulePlanningModel, areaRule).ConfigureAwait(false);
                    planning.NameTranslations = areaRule.AreaRuleTranslations.Select(
                        areaRuleAreaRuleTranslation => new PlanningNameTranslation
                        {
                            LanguageId = areaRuleAreaRuleTranslation.LanguageId,
                            Name = areaRuleAreaRuleTranslation.Name,
                        }).ToList();

                    planning.RepeatEvery = 0;
                    planning.RepeatType = RepeatType.Day;
                    planning.PlanningsTags.Add(new() {PlanningTagId = globalPlanningTag.Id});

                    await planning.Create(_itemsPlanningPnDbContext).ConfigureAwait(false);
                    await _pairItemWichSiteHelper.Pair(
                        areaRulePlanningModel.AssignedSites.Select(x => x.SiteId).ToList(), (int) areaRule.EformId,
                        planning.Id,
                        areaRule.FolderId).ConfigureAwait(false);

                    var areaRulePlanning = CreateAreaRulePlanningObject(areaRulePlanningModel, areaRule, planning.Id,
                        areaRule.FolderId);

                    await areaRulePlanning.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);

                    return;
                }

                var subfolder = await sdkDbContext.Folders
                    .Include(x => x.FolderTranslations)
                    .Where(x=> x.ParentId == areaRule.FolderId)
                    .Where(x => x.FolderTranslations.Any(y => y.Name == lookupName))
                    .FirstOrDefaultAsync().ConfigureAwait(false);

                Regex regex = new Regex(@"(\d\.\s)");
                DayOfWeek? currentWeekDay= null;
                // var clTranslations = await sdkDbContext.CheckListTranslations.Where(x => x.CheckListId == clId).ToListAsync();

                var planningTag = await _itemsPlanningPnDbContext.PlanningTags.SingleOrDefaultAsync(x =>
                    x.Name == $"{property.Name} - Aflæsninger-Prøver").ConfigureAwait(false);

                if (planningTag == null)
                {
                    planningTag = new PlanningTag
                    {
                        Name = $"{property.Name} - Aflæsninger-Prøver",
                        CreatedByUserId = _userService.UserId,
                        UpdatedByUserId = _userService.UserId,
                    };

                    await planningTag.Create(_itemsPlanningPnDbContext).ConfigureAwait(false);
                }


                var globalPlanningTag1 = await _itemsPlanningPnDbContext.PlanningTags.SingleOrDefaultAsync(x =>
                    x.Name == $"{property.Name} - {areaRule.AreaRuleTranslations.First().Name}").ConfigureAwait(false);

                if (globalPlanningTag1 == null)
                {
                    globalPlanningTag1 = new PlanningTag
                    {
                        Name = $"{property.Name} - {areaRule.AreaRuleTranslations.First().Name}",
                        CreatedByUserId = _userService.UserId,
                        UpdatedByUserId = _userService.UserId,
                    };

                    await globalPlanningTag1.Create(_itemsPlanningPnDbContext).ConfigureAwait(false);
                }

                foreach (var poolHour in poolHours)
                {
                    var clId = sdkDbContext.CheckListTranslations.Where(x => x.Text == $"02. Fækale uheld - {property.Name}").Select(x => x.CheckListId).FirstOrDefault();
                    var innerLookupName = $"{(int)poolHour.DayOfWeek}. {poolHour.DayOfWeek.ToString().Substring(0, 3)}";
                    var poolDayFolder = await sdkDbContext.Folders
                        .Include(x => x.FolderTranslations)
                        .Where(x=> x.ParentId == subfolder.Id)
                        .Where(x => x.FolderTranslations.Any(y => y.Name == innerLookupName))
                        .FirstAsync().ConfigureAwait(false);


                    if (currentWeekDay == null || currentWeekDay != (DayOfWeek)poolHour.DayOfWeek)
                    {
                        var planningStatic = await CreateItemPlanningObject(clId, $"02. Fækale uheld - {property.Name}",
                            poolDayFolder.Id, areaRulePlanningModel, areaRule).ConfigureAwait(false);
                        planningStatic.RepeatEvery = 0;
                        planningStatic.RepeatType = RepeatType.Day;
                        planningStatic.SdkFolderName = innerLookupName;
                        planningStatic.PushMessageOnDeployment = false;
                        planningStatic.NameTranslations = areaRule.AreaRuleTranslations.Select(
                            areaRuleAreaRuleTranslation => new PlanningNameTranslation
                            {
                                LanguageId = areaRuleAreaRuleTranslation.LanguageId,
                                Name =
                                    $"24. Fækale uheld - {areaRuleAreaRuleTranslation.Name}",
                            }).ToList();

                        var planningTagStatic = await _itemsPlanningPnDbContext.PlanningTags.SingleOrDefaultAsync(x =>
                            x.Name == $"{property.Name} - Fækale uheld").ConfigureAwait(false);

                        if (planningTagStatic == null)
                        {
                            planningTagStatic = new PlanningTag
                            {
                                Name = $"{property.Name} - Fækale uheld",
                                CreatedByUserId = _userService.UserId,
                                UpdatedByUserId = _userService.UserId,
                            };

                            await planningTagStatic.Create(_itemsPlanningPnDbContext).ConfigureAwait(false);
                        }

                        planningStatic.PlanningsTags.Add(new() {PlanningTagId = planningTagStatic.Id});
                        planningStatic.PlanningsTags.Add(new() {PlanningTagId = globalPlanningTag1.Id});

                        await planningStatic.Create(_itemsPlanningPnDbContext).ConfigureAwait(false);
                        await _pairItemWichSiteHelper.Pair(
                            areaRulePlanningModel.AssignedSites.Select(x => x.SiteId).ToList(), clId, planningStatic.Id,
                            poolDayFolder.Id).ConfigureAwait(false);
                        var areaRulePlanningStatic = CreateAreaRulePlanningObject(areaRulePlanningModel, areaRule, planningStatic.Id,
                            areaRule.FolderId);
                        areaRulePlanningStatic.ComplianceEnabled = false;
                        areaRulePlanningStatic.RepeatEvery = 0;
                        areaRulePlanningStatic.RepeatType = (int)RepeatType.Day;
                        areaRulePlanningStatic.FolderId = poolDayFolder.Id;

                        await areaRulePlanningStatic.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);
                    }

                    currentWeekDay = (DayOfWeek)poolHour.DayOfWeek;

                    if (poolHour.IsActive)
                    {
                        clId = sdkDbContext.CheckListTranslations
                            .Where(x => x.Text == $"01. Aflæsninger - {property.Name}").Select(x => x.CheckListId)
                            .FirstOrDefault();
                        var planning = await CreateItemPlanningObject(clId, $"01. Aflæsninger - {property.Name}",
                            poolDayFolder.Id, areaRulePlanningModel, areaRule).ConfigureAwait(false);
                        planning.DayOfWeek = (DayOfWeek)poolHour.DayOfWeek;
                        planning.RepeatEvery = 1;
                        planning.RepeatType = RepeatType.Week;
                        var nextDay = GetNextWeekday(DateTime.UtcNow, (DayOfWeek)poolHour.DayOfWeek);
                        planning.StartDate = new DateTime(nextDay.Year, nextDay.Month, nextDay.Day, 0, 0, 0);
                        planning.SdkFolderName = innerLookupName;
                        planning.PushMessageOnDeployment = false;
                        planning.NameTranslations = areaRule.AreaRuleTranslations.Select(
                            areaRuleAreaRuleTranslation => new PlanningNameTranslation
                            {
                                LanguageId = areaRuleAreaRuleTranslation.LanguageId,
                                Name =
                                    $"{poolDayFolder.FolderTranslations.Where(x => x.LanguageId == areaRuleAreaRuleTranslation.LanguageId).Select(x => x.Name).First()} - {areaRuleAreaRuleTranslation.Name}",
                            }).ToList();
                        foreach (var planningNameTranslation in planning.NameTranslations)
                        {
                            planningNameTranslation.Name = regex.Replace(planningNameTranslation.Name, "");
                            planningNameTranslation.Name = $"{poolHour.Name}:00. {planningNameTranslation.Name}";
                        }

                        planning.PlanningsTags.Add(new() { PlanningTagId = planningTag.Id });
                        planning.PlanningsTags.Add(new() { PlanningTagId = globalPlanningTag1.Id });
                        planning.DaysBeforeRedeploymentPushMessageRepeat = false;

                        await planning.Create(_itemsPlanningPnDbContext).ConfigureAwait(false);

                        poolHour.ItemsPlanningId = planning.Id;
                        await poolHour.Update(_backendConfigurationPnDbContext).ConfigureAwait(false);

                        var areaRulePlanning = CreateAreaRulePlanningObject(areaRulePlanningModel, areaRule,
                            planning.Id,
                            poolDayFolder.Id);
                        areaRulePlanning.ComplianceEnabled = false;
                        areaRulePlanning.RepeatEvery = 0;
                        areaRulePlanning.RepeatType = (int)RepeatType.Day;
                        areaRulePlanning.DayOfWeek = (int)(DayOfWeek)poolHour.DayOfWeek;
                        areaRulePlanning.FolderId = poolDayFolder.Id;

                        await areaRulePlanning.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);
                    }
                }
            }
        }

        private static DateTime GetNextWeekday(DateTime start, DayOfWeek day)
        {
            // The (... + 7) % 7 ensures we end up with a value in the range [0, 6]
            int daysToAdd = ((int) day - (int) start.DayOfWeek + 7) % 7;
            return start.AddDays(daysToAdd);
        }

        private async Task CreatePlanningDefaultType(AreaRule areaRule, MicrotingDbContext sdkDbContext, AreaRulePlanningModel areaRulePlanningModel, eFormCore.Core core)
        {
            if (areaRule.FolderId == 0)
            {
                var folderId = await _backendConfigurationPnDbContext.ProperyAreaFolders
                    .Include(x => x.AreaProperty)
                    .Where(x => x.AreaProperty.PropertyId == areaRulePlanningModel.PropertyId)
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

            var planningId = 0;
            if (areaRulePlanningModel.Status)
            {
                var planning = await CreateItemPlanningObject((int)areaRule.EformId, areaRule.EformName,
                    areaRule.FolderId, areaRulePlanningModel, areaRule).ConfigureAwait(false);
                planning.NameTranslations = areaRule.AreaRuleTranslations.Select(
                    areaRuleAreaRuleTranslation => new PlanningNameTranslation
                    {
                        LanguageId = areaRuleAreaRuleTranslation.LanguageId,
                        Name = areaRuleAreaRuleTranslation.Name,
                    }).ToList();
                if (areaRulePlanningModel.TypeSpecificFields != null)
                {
                    planning.DayOfMonth = (int)areaRulePlanningModel.TypeSpecificFields?.DayOfMonth == 0
                        ? 1
                        : (int)areaRulePlanningModel.TypeSpecificFields?.DayOfMonth;
                    planning.RepeatUntil = areaRulePlanningModel.TypeSpecificFields?.EndDate;
                    planning.DayOfWeek =
                        (DayOfWeek?)areaRulePlanningModel.TypeSpecificFields?.DayOfWeek == 0
                            ? DayOfWeek.Monday
                            : (DayOfWeek?)areaRulePlanningModel.TypeSpecificFields?.DayOfWeek;
                    if (areaRulePlanningModel.TypeSpecificFields.RepeatEvery is not null)
                    {
                        planning.RepeatEvery =
                            (int)areaRulePlanningModel.TypeSpecificFields.RepeatEvery;
                    }

                    if (areaRulePlanningModel.TypeSpecificFields.RepeatType is not null)
                    {
                        planning.RepeatType =
                            (RepeatType)areaRulePlanningModel.TypeSpecificFields.RepeatType;
                    }
                }

                if (planning.NameTranslations.Any(x => x.Name == "13. APV Medarbejder"))
                {
                    planning.RepeatEvery = 0;
                    planning.RepeatType = RepeatType.Day;
                }

                await planning.Create(_itemsPlanningPnDbContext).ConfigureAwait(false);
                await _pairItemWichSiteHelper.Pair(
                    areaRulePlanningModel.AssignedSites.Select(x => x.SiteId).ToList(), (int)areaRule.EformId,
                    planning.Id,
                    areaRule.FolderId).ConfigureAwait(false);
                planningId = planning.Id;
            }

            var areaRulePlanning = CreateAreaRulePlanningObject(areaRulePlanningModel, areaRule, planningId,
                areaRule.FolderId);

            await areaRulePlanning.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);
        }
    }
}