using System.Text.RegularExpressions;
using eFormCore;
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
                var core = await _coreHelper.GetCore();
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
                        .FirstAsync();

                    if (areaRule.Area.Type == AreaTypesEnum.Type10)
                    {
                        var oldStatus = areaRule.AreaRulesPlannings.Last(x => x.WorkflowState != Constants.WorkflowStates.Removed).Status;
                        var currentPlanningSites = await _backendConfigurationPnDbContext.PlanningSites
                            .Where(x => x.AreaRuleId == areaRulePlanningModel.RuleId)
                            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                            .Select(x => x.SiteId)
                            .ToListAsync();
                        var forDelete = currentPlanningSites
                            .Except(areaRulePlanningModel.AssignedSites.Select(x => x.SiteId)).ToList();
                        var forAdd = areaRulePlanningModel.AssignedSites.Select(x => x.SiteId)
                            .Except(currentPlanningSites).ToList();
                        if (areaRulePlanningModel.Status && oldStatus)
                        {
                            var areaRulePlannings = areaRule.AreaRulesPlannings.Join(_backendConfigurationPnDbContext
                                    .PlanningSites
                                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed),
                                arp => arp.Id, planningSite => planningSite.AreaRulePlanningsId, (arp, planningSite) =>
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
                                    .SingleAsync(x => x.Id == planningSiteId);
                                await backendPlanningSite.Delete(_backendConfigurationPnDbContext);
                                var planning = await _itemsPlanningPnDbContext.Plannings
                                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                    .Where(x => x.Id == itemsPlanningId)
                                    .Include(x => x.PlanningSites)
                                    .FirstOrDefaultAsync();

                                if (planning != null)
                                {
                                    foreach (var planningSite in planning.PlanningSites
                                                 .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed))
                                    {
                                        if (forDelete.Contains(planningSite.SiteId))
                                        {
                                            await planningSite.Delete(_itemsPlanningPnDbContext);
                                            var someList = await _itemsPlanningPnDbContext.PlanningCaseSites
                                                .Where(x => x.PlanningId == planning.Id)
                                                .Where(x => x.MicrotingSdkSiteId == planningSite.SiteId)
                                                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                                .ToListAsync();

                                            foreach (var planningCaseSite in someList)
                                            {
                                                var result =
                                                    await sdkDbContext.Cases.SingleOrDefaultAsync(x =>
                                                        x.Id == planningCaseSite.MicrotingSdkCaseId);
                                                if (result is { MicrotingUid: { } })
                                                {
                                                    await core.CaseDelete((int)result.MicrotingUid);
                                                }
                                                else
                                                {
                                                    var clSites = await sdkDbContext.CheckListSites.SingleAsync(x =>
                                                        x.Id == planningCaseSite.MicrotingCheckListSitId);

                                                    await core.CaseDelete(clSites.MicrotingUid);
                                                }
                                            }
                                        }
                                    }
                                }
                            }

                            foreach (var areaRulePlanning in areaRule.AreaRulesPlannings)
                            {
                                foreach (int i in forAdd)
                                {
                                    var siteForCreate = new PlanningSite
                                    {
                                        AreaRulePlanningsId = areaRulePlanning.Id,
                                        SiteId = i,
                                        CreatedByUserId = _userService.UserId,
                                        UpdatedByUserId = _userService.UserId,
                                    };
                                    await siteForCreate.Create(_backendConfigurationPnDbContext);
                                    var planningSite =
                                        new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.
                                            PlanningSite
                                            {
                                                SiteId = i,
                                                PlanningId = areaRulePlanning.ItemPlanningId,
                                                CreatedByUserId = _userService.UserId,
                                                UpdatedByUserId = _userService.UserId,
                                            };
                                    await planningSite.Create(_itemsPlanningPnDbContext);
                                }
                            }

                        }
                        else
                        {
                            if (areaRulePlanningModel.Status && !oldStatus)
                            {
                                foreach (AreaRulePlanning areaRuleAreaRulesPlanning in areaRule.AreaRulesPlannings)
                                {
                                    await areaRuleAreaRulesPlanning.Delete(_backendConfigurationPnDbContext);
                                }

                                await CreatePlanningType10(areaRule, sdkDbContext, areaRulePlanningModel, core);
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
                                    .FirstOrDefaultAsync();

                                    if (planning != null)
                                    {
                                        foreach (var planningSite in planning.PlanningSites
                                                     .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed))
                                        {
                                            await planningSite.Delete(_itemsPlanningPnDbContext);
                                            var someList = await _itemsPlanningPnDbContext.PlanningCaseSites
                                                .Where(x => x.PlanningId == planning.Id)
                                                .Where(x => x.MicrotingSdkSiteId == planningSite.SiteId)
                                                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                                .ToListAsync();

                                            foreach (var planningCaseSite in someList)
                                            {
                                                var result =
                                                    await sdkDbContext.Cases.SingleOrDefaultAsync(x =>
                                                        x.Id == planningCaseSite.MicrotingSdkCaseId);
                                                if (result is { MicrotingUid: { } })
                                                {
                                                    await core.CaseDelete((int)result.MicrotingUid);
                                                }
                                                else
                                                {
                                                    var clSites = await sdkDbContext.CheckListSites.SingleAsync(x =>
                                                        x.Id == planningCaseSite.MicrotingCheckListSitId);

                                                    await core.CaseDelete(clSites.MicrotingUid);
                                                }
                                            }
                                        }
                                        await planning.Delete(_itemsPlanningPnDbContext);
                                    }
                                    var areaRulePlanning = _backendConfigurationPnDbContext.AreaRulePlannings
                                        .Single(x => x.Id == arp.Id);
                                    areaRulePlanning.Status = false;
                                    await areaRulePlanning.Update(_backendConfigurationPnDbContext);
                                }

                                foreach (AreaRulePlanning areaRuleAreaRulesPlanning in areaRule.AreaRulesPlannings)
                                {
                                    areaRuleAreaRulesPlanning.ItemPlanningId = 0;
                                    areaRuleAreaRulesPlanning.Status = false;
                                    await areaRuleAreaRulesPlanning.Update(_backendConfigurationPnDbContext);
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
                                await assignedSite.Create(_backendConfigurationPnDbContext);
                            }

                            foreach (var siteId in siteIdsForDelete)
                            {
                                await rulePlanning.PlanningSites
                                    .First(x => x.SiteId == siteId)
                                    .Delete(_backendConfigurationPnDbContext);
                            }

                            await rulePlanning.Update(_backendConfigurationPnDbContext);

                            var property =
                                await _backendConfigurationPnDbContext.Properties
                                    .SingleOrDefaultAsync(x => x.Id == areaRule.PropertyId);
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
                                                    .FirstAsync();
                                                var planningForType2TypeTankOpen = await CreateItemPlanningObject(
                                                    eformId,
                                                    eformName, areaRule.AreaRulesPlannings[0].FolderId,
                                                    areaRulePlanningModel, areaRule);
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
                                                        (DayOfWeek)areaRulePlanningModel.TypeSpecificFields.DayOfWeek ==
                                                        0
                                                            ? DayOfWeek.Monday
                                                            : (DayOfWeek)areaRulePlanningModel.TypeSpecificFields
                                                                .DayOfWeek;
                                                    planningForType2TypeTankOpen.DayOfMonth =
                                                        areaRulePlanningModel.TypeSpecificFields.DayOfMonth == 0
                                                            ? 1
                                                            : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                                                }

                                                await planningForType2TypeTankOpen.Create(_itemsPlanningPnDbContext);
                                                await _pairItemWichSiteHelper.Pair(
                                                    rulePlanning.PlanningSites.Select(x => x.SiteId).ToList(), eformId,
                                                    planningForType2TypeTankOpen.Id,
                                                    areaRule.AreaRulesPlannings[0].FolderId);
                                                areaRule.AreaRulesPlannings[0].DayOfMonth =
                                                    (int)areaRulePlanningModel.TypeSpecificFields?.DayOfMonth == 0
                                                        ? 1
                                                        : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                                                areaRule.AreaRulesPlannings[0].ItemPlanningId =
                                                    planningForType2TypeTankOpen.Id;
                                                areaRule.AreaRulesPlannings[0].Status = true;
                                                await areaRule.AreaRulesPlannings[0]
                                                    .Update(_backendConfigurationPnDbContext);
                                            }
                                            else
                                            {
                                                if (areaRule.AreaRulesPlannings[0].ItemPlanningId != 0)
                                                {
                                                    await DeleteItemPlanning(areaRule.AreaRulesPlannings[0]
                                                        .ItemPlanningId);
                                                    areaRule.AreaRulesPlannings[0].ItemPlanningId = 0;
                                                    await areaRule.AreaRulesPlannings[0]
                                                        .Update(_backendConfigurationPnDbContext);
                                                }
                                            }

                                            if (areaRule.Type is AreaRuleT2TypesEnum.Open or AreaRuleT2TypesEnum.Closed
                                                && areaRule.Alarm is AreaRuleT2AlarmsEnum.Yes)
                                            {
                                                const string eformName = "03. Kontrol alarmanlæg gyllebeholder";
                                                var eformId = await sdkDbContext.CheckListTranslations
                                                    .Where(x => x.Text == eformName)
                                                    .Select(x => x.CheckListId)
                                                    .FirstAsync();
                                                var planningForType2AlarmYes = await CreateItemPlanningObject(eformId,
                                                    eformName, areaRule.AreaRulesPlannings[1].FolderId,
                                                    areaRulePlanningModel, areaRule);
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
                                                        (DayOfWeek)areaRulePlanningModel.TypeSpecificFields.DayOfWeek ==
                                                        0
                                                            ? DayOfWeek.Monday
                                                            : (DayOfWeek)areaRulePlanningModel.TypeSpecificFields
                                                                .DayOfWeek;
                                                    planningForType2AlarmYes.DayOfMonth =
                                                        areaRulePlanningModel.TypeSpecificFields.DayOfMonth == 0
                                                            ? 1
                                                            : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                                                }

                                                await planningForType2AlarmYes.Create(_itemsPlanningPnDbContext);
                                                await _pairItemWichSiteHelper.Pair(
                                                    rulePlanning.PlanningSites.Select(x => x.SiteId).ToList(), eformId,
                                                    planningForType2AlarmYes.Id,
                                                    areaRule.AreaRulesPlannings[1].FolderId);
                                                areaRule.AreaRulesPlannings[1].DayOfMonth =
                                                    (int)areaRulePlanningModel.TypeSpecificFields?.DayOfMonth == 0
                                                        ? 1
                                                        : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                                                areaRule.AreaRulesPlannings[1].ItemPlanningId =
                                                    planningForType2AlarmYes.Id;
                                                areaRule.AreaRulesPlannings[1].Status = true;
                                                await areaRule.AreaRulesPlannings[1]
                                                    .Update(_backendConfigurationPnDbContext);
                                            }
                                            else
                                            {
                                                if (areaRule.AreaRulesPlannings[1].ItemPlanningId != 0)
                                                {
                                                    await DeleteItemPlanning(areaRule.AreaRulesPlannings[1]
                                                        .ItemPlanningId);
                                                    areaRule.AreaRulesPlannings[1].ItemPlanningId = 0;
                                                    await areaRule.AreaRulesPlannings[1]
                                                        .Update(_backendConfigurationPnDbContext);
                                                }
                                            }

                                            /*areaRule.EformName must be "03. Kontrol konstruktion"*/
                                            var planningForType2 = await CreateItemPlanningObject((int)areaRule.EformId,
                                                areaRule.EformName, areaRule.AreaRulesPlannings[2].FolderId,
                                                areaRulePlanningModel, areaRule);
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
                                                    (DayOfWeek)areaRulePlanningModel.TypeSpecificFields.DayOfWeek == 0
                                                        ? DayOfWeek.Monday
                                                        : (DayOfWeek)areaRulePlanningModel.TypeSpecificFields.DayOfWeek;
                                                planningForType2.DayOfMonth =
                                                    areaRulePlanningModel.TypeSpecificFields.DayOfMonth == 0
                                                        ? 1
                                                        : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                                            }

                                            await planningForType2.Create(_itemsPlanningPnDbContext);
                                            await _pairItemWichSiteHelper.Pair(
                                                rulePlanning.PlanningSites.Select(x => x.SiteId).ToList(),
                                                (int)areaRule.EformId,
                                                planningForType2.Id,
                                                areaRule.AreaRulesPlannings[2].FolderId);
                                            areaRule.AreaRulesPlannings[2].ItemPlanningId = planningForType2.Id;
                                            areaRule.AreaRulesPlannings[2].DayOfMonth =
                                                (int)areaRulePlanningModel.TypeSpecificFields?.DayOfMonth == 0
                                                    ? 1
                                                    : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                                            areaRule.AreaRulesPlannings[2].Status = true;
                                            await areaRule.AreaRulesPlannings[2]
                                                .Update(_backendConfigurationPnDbContext);
                                            i = areaRule.AreaRulesPlannings.Count;
                                            break;
                                        }
                                        /*case AreaTypesEnum.Type3: // stables and tail bite
                                            {
                                                if (areaRule.ChecklistStable is true)
                                                {
                                                    var planningForType3ChecklistStable =
                                                        await CreateItemPlanningObject((int)areaRule.EformId,
                                                            areaRule.EformName, areaRule.FolderId, areaRulePlanningModel,
                                                            areaRule);
                                                    planningForType3ChecklistStable.NameTranslations = areaRule
                                                        .AreaRuleTranslations
                                                        .Select(x =>
                                                            new PlanningNameTranslation
                                                            {
                                                                LanguageId = x.LanguageId,
                                                                Name = x.Name,
                                                            })
                                                        .ToList();
                                                    if (areaRulePlanningModel.TypeSpecificFields is not null)
                                                    {
                                                        if (areaRulePlanningModel.TypeSpecificFields.RepeatType != null)
                                                        {
                                                            planningForType3ChecklistStable.RepeatType =
                                                                (RepeatType)areaRulePlanningModel.TypeSpecificFields
                                                                    .RepeatType;
                                                        }

                                                        if (areaRulePlanningModel.TypeSpecificFields?.RepeatEvery != null)
                                                        {
                                                            planningForType3ChecklistStable.RepeatEvery =
                                                                (int)areaRulePlanningModel.TypeSpecificFields.RepeatEvery;
                                                        }

                                                        planningForType3ChecklistStable.DayOfMonth =
                                                            areaRulePlanningModel.TypeSpecificFields.DayOfMonth == 0
                                                                ? 1
                                                                : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                                                        planningForType3ChecklistStable.DayOfWeek =
                                                            (DayOfWeek?)areaRulePlanningModel.TypeSpecificFields.DayOfWeek == 0
                                                                ? DayOfWeek.Monday
                                                                : (DayOfWeek?)areaRulePlanningModel.TypeSpecificFields
                                                                    .DayOfWeek;
                                                    }

                                                    await planningForType3ChecklistStable.Create(_itemsPlanningPnDbContext);
                                                    await _pairItemWichSiteHelper.Pair(
                                                        rulePlanning.PlanningSites.Select(x => x.SiteId).ToList(),
                                                        (int)areaRule.EformId,
                                                        planningForType3ChecklistStable.Id,
                                                        areaRule.FolderId);
                                                    areaRule.AreaRulesPlannings[0].ItemPlanningId =
                                                        planningForType3ChecklistStable.Id;
                                                    areaRule.AreaRulesPlannings[0].DayOfMonth =
                                                        (int)areaRulePlanningModel.TypeSpecificFields?.DayOfMonth == 0
                                                            ? 1
                                                            : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                                                    areaRule.AreaRulesPlannings[0].DayOfWeek =
                                                        (int)areaRulePlanningModel.TypeSpecificFields?.DayOfWeek == 0
                                                            ? 1
                                                            : areaRulePlanningModel.TypeSpecificFields.DayOfWeek;
                                                    await areaRule.AreaRulesPlannings[0]
                                                        .Update(_backendConfigurationPnDbContext);
                                                }
                                                else
                                                {
                                                    if (areaRule.AreaRulesPlannings[0].ItemPlanningId != 0)
                                                    {
                                                        await DeleteItemPlanning(areaRule.AreaRulesPlannings[0].ItemPlanningId);
                                                        areaRule.AreaRulesPlannings[0].ItemPlanningId = 0;
                                                        await areaRule.AreaRulesPlannings[0]
                                                            .Update(_backendConfigurationPnDbContext);
                                                    }
                                                }

                                                if (areaRule.TailBite is true)
                                                {
                                                    // TODO add the deploy eform
                                                    var sites = areaRulePlanningModel.AssignedSites.Select(x => x.SiteId)
                                                        .ToList();
                                                    foreach (var siteId in sites)
                                                    {
                                                        var eformName = $"05. Halebid - {property.Name}";
                                                        var eformId = await sdkDbContext.CheckListTranslations
                                                            .Where(x => x.Text == eformName)
                                                            .Select(x => x.CheckListId)
                                                            .FirstAsync();
                                                        var site = await sdkDbContext.Sites.SingleOrDefaultAsync(x =>
                                                            x.Id == siteId);
                                                        var language =
                                                            await sdkDbContext.Languages.SingleOrDefaultAsync(x =>
                                                                x.Id == site.LanguageId);
                                                        if (!sdkDbContext.CheckListSites.Any(x =>
                                                            x.CheckListId == eformId &&
                                                            x.SiteId == siteId &&
                                                            x.WorkflowState != Constants.WorkflowStates.Removed))
                                                        {
                                                            var mainElement = await core.ReadeForm(eformId, language);

                                                            var folder =
                                                                await sdkDbContext.Folders.SingleAsync(x =>
                                                                    x.Id == areaRule.FolderId);
                                                            var folderMicrotingId = folder.MicrotingUid.ToString();
                                                            mainElement.Label =
                                                                mainElement.Label.Replace($" - {property.Name}", "");
                                                            mainElement.Repeated = -1;
                                                            mainElement.CheckListFolderName = folderMicrotingId;
                                                            mainElement.StartDate = DateTime.Now.ToUniversalTime();
                                                            mainElement.EndDate = DateTime.Now.AddYears(10).ToUniversalTime();
                                                            mainElement.DisplayOrder = 10000000;
                                                            /*var caseId = #1#
                                                            await core.CaseCreate(mainElement, "",
                                               (int)site.MicrotingUid, folder.Id);
                                                        }
                                                    }
                                                    var areaProperty = await _backendConfigurationPnDbContext.AreaProperties
                                                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                                        .Where(x => x.AreaId == areaRule.AreaId)
                                                        .Where(x => x.PropertyId == areaRule.PropertyId)
                                                        .Select(x => new { x.Area, x.GroupMicrotingUuid, x.PropertyId })
                                                        .FirstAsync();
                                                    var entityGroup = await core.EntityGroupRead(areaProperty.GroupMicrotingUuid.ToString());
                                                    var nextItemUid = entityGroup.EntityGroupItemLst.Count;
                                                    var entityItem = await core.EntitySelectItemCreate(entityGroup.Id,
                                                        areaRule.AreaRuleTranslations.First().Name, entityGroup.EntityGroupItemLst.Count,
                                                        nextItemUid.ToString());
                                                    areaRule.GroupItemId = entityItem.Id;
                                                    await areaRule.Update(_backendConfigurationPnDbContext);
                                                }
                                                else
                                                {
                                                    // TODO add the remove check
                                                    var eformName = $"05. Halebid - {property.Name}";
                                                    var eformId = await sdkDbContext.CheckListTranslations
                                                        .Where(x => x.Text == eformName)
                                                        .Select(x => x.CheckListId)
                                                        .FirstAsync();

                                                    var numRules = _backendConfigurationPnDbContext.AreaRules
                                                        .Join(_backendConfigurationPnDbContext.AreaRulePlannings,
                                                            rule => rule.Id,
                                                            rulePlanningModel => rulePlanningModel.AreaRuleId,
                                                            (rule, planning) =>
                                                                new
                                                                {
                                                                    rule.PropertyId,
                                                                    rule.AreaId,
                                                                    planning.Status,
                                                                    planning.WorkflowState,
                                                                    RuleWorkflowState = rule.WorkflowState
                                                                })
                                                        .Where(x => x.PropertyId == property.Id)
                                                        .Where(x => x.AreaId == areaRule.AreaId)
                                                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                                        .Where(x => x.RuleWorkflowState != Constants.WorkflowStates.Removed)
                                                        .Count(x => x.Status == true);
                                                    // .Where(x =>
                                                    // x.TailBite == true && x.PropertyId == areaRule.PropertyId);
                                                    if (numRules == 0)
                                                    {
                                                        foreach (var checkListSite in sdkDbContext.CheckListSites
                                                            .Where(x => x.CheckListId == eformId))
                                                        {
                                                            await core.CaseDelete(checkListSite.MicrotingUid);
                                                        }
                                                    }

                                                    if (areaRule.GroupItemId == 0)
                                                    {
                                                        await core.EntityItemDelete(areaRule.GroupItemId);
                                                        areaRule.GroupItemId = 0;
                                                    }
                                                }
                                                i = areaRule.AreaRulesPlannings.Count;
                                                break;
                                            }*/
                                        case AreaTypesEnum.Type6: // heat pumps
                                        {
                                            if (areaRulePlanningModel.TypeSpecificFields?.HoursAndEnergyEnabled is true)
                                            {
                                                if (areaRulePlanningModel.TypeSpecificFields
                                                        ?.HoursAndEnergyEnabled is true)
                                                {
                                                    areaRule.AreaRulesPlannings[0].HoursAndEnergyEnabled = true;
                                                    areaRule.AreaRulesPlannings[1].HoursAndEnergyEnabled = true;
                                                    areaRule.AreaRulesPlannings[2].HoursAndEnergyEnabled = true;
                                                    areaRule.AreaRulesPlannings[0].DayOfMonth =
                                                        (int)areaRulePlanningModel.TypeSpecificFields?.DayOfMonth == 0
                                                            ? 1
                                                            : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                                                    areaRule.AreaRulesPlannings[0].DayOfWeek =
                                                        (int)areaRulePlanningModel.TypeSpecificFields?.DayOfWeek == 0
                                                            ? 1
                                                            : areaRulePlanningModel.TypeSpecificFields.DayOfWeek;
                                                    areaRule.AreaRulesPlannings[1].DayOfMonth =
                                                        (int)areaRulePlanningModel.TypeSpecificFields?.DayOfMonth == 0
                                                            ? 1
                                                            : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                                                    areaRule.AreaRulesPlannings[1].DayOfWeek =
                                                        (int)areaRulePlanningModel.TypeSpecificFields?.DayOfWeek == 0
                                                            ? 1
                                                            : areaRulePlanningModel.TypeSpecificFields.DayOfWeek;
                                                    areaRule.AreaRulesPlannings[2].DayOfMonth =
                                                        (int)areaRulePlanningModel.TypeSpecificFields?.DayOfMonth == 0
                                                            ? 1
                                                            : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                                                    areaRule.AreaRulesPlannings[2].DayOfWeek =
                                                        (int)areaRulePlanningModel.TypeSpecificFields?.DayOfWeek == 0
                                                            ? 1
                                                            : areaRulePlanningModel.TypeSpecificFields.DayOfWeek;
                                                    await areaRule.Update(_backendConfigurationPnDbContext);
                                                    const string eformName = "10. Varmepumpe timer og energi";
                                                    var eformId = await sdkDbContext.CheckListTranslations
                                                        .Where(x => x.Text == eformName)
                                                        .Select(x => x.CheckListId)
                                                        .FirstAsync();
                                                    var planningForType6HoursAndEnergyEnabled =
                                                        await CreateItemPlanningObject(eformId, eformName,
                                                            areaRule.AreaRulesPlannings[0].FolderId,
                                                            areaRulePlanningModel,
                                                            areaRule);
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
                                                        if (areaRulePlanningModel.TypeSpecificFields.RepeatType != null)
                                                        {
                                                            planningForType6HoursAndEnergyEnabled.RepeatType =
                                                                (RepeatType)areaRulePlanningModel.TypeSpecificFields
                                                                    .RepeatType;
                                                        }

                                                        if (areaRulePlanningModel.TypeSpecificFields?.RepeatEvery !=
                                                            null)
                                                        {
                                                            planningForType6HoursAndEnergyEnabled.RepeatEvery =
                                                                (int)areaRulePlanningModel.TypeSpecificFields
                                                                    .RepeatEvery;
                                                        }

                                                        planningForType6HoursAndEnergyEnabled.DayOfMonth =
                                                            areaRulePlanningModel.TypeSpecificFields.DayOfMonth == 0
                                                                ? 1
                                                                : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                                                        planningForType6HoursAndEnergyEnabled.DayOfWeek =
                                                            (DayOfWeek?)areaRulePlanningModel.TypeSpecificFields
                                                                .DayOfWeek == 0
                                                                ? DayOfWeek.Monday
                                                                : (DayOfWeek?)areaRulePlanningModel.TypeSpecificFields
                                                                    .DayOfWeek;
                                                    }

                                                    await planningForType6HoursAndEnergyEnabled.Create(
                                                        _itemsPlanningPnDbContext);
                                                    areaRule.AreaRulesPlannings[0].ItemPlanningId =
                                                        planningForType6HoursAndEnergyEnabled.Id;
                                                    await areaRule.AreaRulesPlannings[0]
                                                        .Update(_backendConfigurationPnDbContext);
                                                }
                                                else
                                                {
                                                    areaRule.AreaRulesPlannings[0].HoursAndEnergyEnabled = false;
                                                    areaRule.AreaRulesPlannings[1].HoursAndEnergyEnabled = false;
                                                    areaRule.AreaRulesPlannings[2].HoursAndEnergyEnabled = false;
                                                    if (areaRule.AreaRulesPlannings[0].ItemPlanningId != 0)
                                                    {
                                                        await DeleteItemPlanning(areaRule.AreaRulesPlannings[0]
                                                            .ItemPlanningId);
                                                        areaRule.AreaRulesPlannings[0].ItemPlanningId = 0;
                                                        await areaRule.AreaRulesPlannings[0]
                                                            .Update(_backendConfigurationPnDbContext);
                                                    }
                                                }

                                                const string eformNameOne = "10. Varmepumpe serviceaftale";
                                                var eformIdOne = await sdkDbContext.CheckListTranslations
                                                    .Where(x => x.Text == eformNameOne)
                                                    .Select(x => x.CheckListId)
                                                    .FirstAsync();
                                                const string eformNameTwo = "10. Varmepumpe logbog";
                                                var eformIdTwo = await sdkDbContext.CheckListTranslations
                                                    .Where(x => x.Text == eformNameTwo)
                                                    .Select(x => x.CheckListId)
                                                    .FirstAsync();
                                                var planningForType6One = await CreateItemPlanningObject(eformIdOne,
                                                    eformNameOne, areaRule.AreaRulesPlannings[1].FolderId,
                                                    areaRulePlanningModel, areaRule);
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
                                                var planningForType6Two = await CreateItemPlanningObject(eformIdTwo,
                                                    eformNameTwo, areaRule.AreaRulesPlannings[2].FolderId,
                                                    areaRulePlanningModel, areaRule);
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
                                                planningForType6Two.RepeatEvery = 12;
                                                planningForType6Two.RepeatType = RepeatType.Month;
                                                if (areaRulePlanningModel.TypeSpecificFields is not null)
                                                {
                                                    planningForType6One.DayOfMonth =
                                                        areaRulePlanningModel.TypeSpecificFields.DayOfMonth == 0
                                                            ? 1
                                                            : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                                                    planningForType6One.DayOfWeek =
                                                        (DayOfWeek?)areaRulePlanningModel.TypeSpecificFields
                                                            .DayOfWeek == 0
                                                            ? DayOfWeek.Monday
                                                            : (DayOfWeek?)areaRulePlanningModel.TypeSpecificFields
                                                                .DayOfWeek;
                                                    planningForType6One.RepeatUntil =
                                                        areaRulePlanningModel.TypeSpecificFields.EndDate;
                                                    planningForType6Two.DayOfMonth =
                                                        areaRulePlanningModel.TypeSpecificFields.DayOfMonth == 0
                                                            ? 1
                                                            : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                                                    planningForType6Two.DayOfWeek =
                                                        (DayOfWeek?)areaRulePlanningModel.TypeSpecificFields
                                                            .DayOfWeek == 0
                                                            ? DayOfWeek.Monday
                                                            : (DayOfWeek?)areaRulePlanningModel.TypeSpecificFields
                                                                .DayOfWeek;
                                                    planningForType6Two.RepeatUntil =
                                                        areaRulePlanningModel.TypeSpecificFields.EndDate;
                                                }

                                                await planningForType6One.Create(_itemsPlanningPnDbContext);
                                                await planningForType6Two.Create(_itemsPlanningPnDbContext);
                                                await _pairItemWichSiteHelper.Pair(
                                                    rulePlanning.PlanningSites.Select(x => x.SiteId).ToList(),
                                                    eformIdOne,
                                                    planningForType6One.Id,
                                                    areaRule.AreaRulesPlannings[1].FolderId);
                                                await _pairItemWichSiteHelper.Pair(
                                                    rulePlanning.PlanningSites.Select(x => x.SiteId).ToList(),
                                                    eformIdTwo,
                                                    planningForType6Two.Id,
                                                    areaRule.AreaRulesPlannings[2].FolderId);
                                                areaRule.AreaRulesPlannings[1].ItemPlanningId = planningForType6One.Id;
                                                areaRule.AreaRulesPlannings[2].ItemPlanningId = planningForType6Two.Id;
                                                await areaRule.AreaRulesPlannings[1]
                                                    .Update(_backendConfigurationPnDbContext);
                                                await areaRule.AreaRulesPlannings[2]
                                                    .Update(_backendConfigurationPnDbContext);
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
                                                var folderId = await _backendConfigurationPnDbContext.ProperyAreaFolders
                                                    .Include(x => x.AreaProperty)
                                                    .Where(x => x.AreaProperty.PropertyId ==
                                                                areaRulePlanningModel.PropertyId)
                                                    .Where(x => x.AreaProperty.AreaId == areaRule.AreaId)
                                                    .Select(x => x.FolderId)
                                                    .FirstOrDefaultAsync();
                                                if (folderId != 0)
                                                {
                                                    areaRule.FolderId = folderId;
                                                    areaRule.FolderName = await sdkDbContext.FolderTranslations
                                                        .Where(x => x.FolderId == folderId)
                                                        .Where(x => x.LanguageId == 1) // danish
                                                        .Select(x => x.Name)
                                                        .FirstAsync();
                                                    await areaRule.Update(_backendConfigurationPnDbContext);
                                                }
                                            }

                                            if (areaRule.Area.Type == AreaTypesEnum.Type5) // recuring tasks(mon-sun)
                                            {
                                                var folderIds = await _backendConfigurationPnDbContext
                                                    .ProperyAreaFolders
                                                    .Include(x => x.AreaProperty)
                                                    .Where(x => x.AreaProperty.PropertyId ==
                                                                areaRulePlanningModel.PropertyId)
                                                    .Where(x => x.AreaProperty.AreaId == areaRule.AreaId)
                                                    .Select(x => x.FolderId)
                                                    .Skip(1)
                                                    .ToListAsync();
                                                areaRule.FolderId = folderIds[areaRule.DayOfWeek];
                                                areaRule.FolderName = await sdkDbContext.FolderTranslations
                                                    .Where(x => x.FolderId == areaRule.FolderId)
                                                    .Where(x => x.LanguageId == 1) // danish
                                                    .Select(x => x.Name)
                                                    .FirstAsync();
                                                await areaRule.Update(_backendConfigurationPnDbContext);
                                                areaRulePlanningModel.TypeSpecificFields ??=
                                                    new AreaRuleTypePlanningModel(); // if areaRulePlanningModel.TypeSpecificFields == null -> areaRulePlanningModel.TypeSpecificFields = new()
                                                areaRulePlanningModel.TypeSpecificFields.RepeatEvery =
                                                    areaRule.RepeatEvery; // repeat every mast be from area rule
                                            }

                                            var planning = await CreateItemPlanningObject((int)areaRule.EformId,
                                                areaRule.EformName, areaRule.FolderId, areaRulePlanningModel, areaRule);
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
                                                    (DayOfWeek?)areaRulePlanningModel.TypeSpecificFields.DayOfWeek == 0
                                                        ? DayOfWeek.Monday
                                                        : (DayOfWeek?)areaRulePlanningModel.TypeSpecificFields
                                                            .DayOfWeek;
                                                planning.RepeatUntil =
                                                    areaRulePlanningModel.TypeSpecificFields.EndDate;
                                                if (areaRulePlanningModel.TypeSpecificFields.RepeatEvery is not null)
                                                {
                                                    planning.RepeatEvery =
                                                        (int)areaRulePlanningModel.TypeSpecificFields.RepeatEvery;
                                                }

                                                if (areaRulePlanningModel.TypeSpecificFields.RepeatType is not null)
                                                {
                                                    planning.RepeatType =
                                                        (RepeatType)areaRulePlanningModel.TypeSpecificFields
                                                            .RepeatType;
                                                }
                                            }

                                            if (planning.NameTranslations.Any(x => x.Name == "13. APV Medarbejder"))
                                            {
                                                planning.RepeatEvery = 0;
                                                planning.RepeatType = RepeatType.Day;
                                            }

                                            await planning.Create(_itemsPlanningPnDbContext);
                                            await _pairItemWichSiteHelper.Pair(
                                                rulePlanning.PlanningSites
                                                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                                    .Select(x => x.SiteId).ToList(),
                                                (int)areaRule.EformId,
                                                planning.Id,
                                                areaRule.FolderId);
                                            rulePlanning.ItemPlanningId = planning.Id;
                                            await rulePlanning.Update(_backendConfigurationPnDbContext);
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
                                            .ToListAsync();
                                        foreach (var compliance in complianceList)
                                        {
                                            if (compliance != null)
                                            {
                                                await compliance.Delete(_backendConfigurationPnDbContext);
                                            }
                                        }

                                        await DeleteItemPlanning(rulePlanning.ItemPlanningId);

                                        rulePlanning.ItemPlanningId = 0;
                                        rulePlanning.Status = false;
                                        await rulePlanning.Update(_backendConfigurationPnDbContext);
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
                                                    x.Id == areaRule.AreaRulesPlannings[0].ItemPlanningId);
                                        await planningForType6HoursAndEnergyEnabled.Delete(
                                            _itemsPlanningPnDbContext);
                                        areaRule.AreaRulesPlannings[0].ItemPlanningId = 0;
                                        await areaRule.AreaRulesPlannings[0].Update(_backendConfigurationPnDbContext);
                                        continue;
                                    }

                                    if (rulePlanning.ItemPlanningId != 0) // if item planning is create - need to update
                                    {
                                        var planning = await _itemsPlanningPnDbContext.Plannings
                                            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                            .Where(x => x.Id == rulePlanning.ItemPlanningId)
                                            .Include(x => x.PlanningSites)
                                            .FirstAsync();
                                        planning.Enabled = areaRulePlanningModel.Status;
                                        planning.PushMessageOnDeployment = areaRulePlanningModel.SendNotifications;
                                        planning.StartDate = areaRulePlanningModel.StartDate;
                                        planning.DayOfMonth =
                                            (int)areaRulePlanningModel.TypeSpecificFields?.DayOfMonth == 0
                                                ? 1
                                                : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                                        planning.DayOfWeek =
                                            (DayOfWeek)areaRulePlanningModel.TypeSpecificFields?.DayOfWeek == 0
                                                ? DayOfWeek.Friday
                                                : (DayOfWeek)areaRulePlanningModel.TypeSpecificFields.DayOfWeek;
                                        foreach (var planningSite in planning.PlanningSites
                                                     .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed))
                                        {
                                            if (siteIdsForDelete.Contains(planningSite.SiteId))
                                            {
                                                await planningSite.Delete(_itemsPlanningPnDbContext);
                                                var someList = await _itemsPlanningPnDbContext.PlanningCaseSites
                                                    .Where(x => x.PlanningId == planning.Id)
                                                    .Where(x => x.MicrotingSdkSiteId == planningSite.SiteId)
                                                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                                    .ToListAsync();

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
                                                            x.Id == planningCaseSite.MicrotingSdkCaseId);
                                                    if (result is { MicrotingUid: { } })
                                                    {
                                                        await core.CaseDelete((int)result.MicrotingUid);
                                                    }
                                                    else
                                                    {
                                                        var clSites = await sdkDbContext.CheckListSites.SingleAsync(x =>
                                                            x.Id == planningCaseSite.MicrotingCheckListSitId);

                                                        await core.CaseDelete(clSites.MicrotingUid);
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
                                            await planningSite.Create(_itemsPlanningPnDbContext);
                                        }

                                        if (sitesForCreate.Count > 0)
                                        {
                                            await _pairItemWichSiteHelper.Pair(
                                                sitesForCreate.Select(x => x.SiteId).ToList(), planning.RelatedEFormId,
                                                planning.Id,
                                                (int)planning.SdkFolderId);
                                        }

                                        await planning.Update(_itemsPlanningPnDbContext);
                                        if (!_itemsPlanningPnDbContext.PlanningSites.Any(x =>
                                                x.PlanningId == planning.Id &&
                                                x.WorkflowState != Constants.WorkflowStates.Removed) ||
                                            !rulePlanning.ComplianceEnabled)
                                        {
                                            var complianceList = await _backendConfigurationPnDbContext.Compliances
                                                .Where(x => x.PlanningId == planning.Id)
                                                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                                .ToListAsync();
                                            foreach (var compliance in complianceList)
                                            {
                                                await compliance.Delete(_backendConfigurationPnDbContext);
                                                if (_backendConfigurationPnDbContext.Compliances.Any(x =>
                                                        x.PropertyId == property.Id && x.Deadline < DateTime.UtcNow &&
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

                    return new OperationDataResult<AreaRuleModel>(true,
                        _backendConfigurationLocalizationService.GetString("SuccessfullyUpdatePlanning"));
                }

                return await CreatePlanning(areaRulePlanningModel); // create planning
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
                    .FirstOrDefaultAsync();

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
                    .FirstOrDefaultAsync();

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
            var core = await _coreHelper.GetCore();
            var sdkDbContext = core.DbContextHelper.GetDbContext();

            var areaRule = await _backendConfigurationPnDbContext.AreaRules
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.Id == areaRulePlanningModel.RuleId)
                .Include(x => x.AreaRuleTranslations)
                .Include(x => x.Area)
                .Include(x => x.Property)
                .FirstOrDefaultAsync();

            if (areaRule == null)
            {
                return new OperationDataResult<AreaRuleModel>(true,
                    _backendConfigurationLocalizationService.GetString("AreaRuleNotFound"));
            }

            switch (areaRule.Area.Type)
            {
                case AreaTypesEnum.Type2: // tanks
                    {
                        await CreatePlanningType2(areaRule, sdkDbContext, areaRulePlanningModel, core);
                        break;
                    }
                case AreaTypesEnum.Type3: // stables and tail bite
                    {
                        await CreatePlanningType3(areaRule, sdkDbContext, areaRulePlanningModel, core);
                        break;
                    }
                case AreaTypesEnum.Type5: // recuring tasks(mon-sun)
                    {
                        await CreatePlanningType5(areaRule, areaRulePlanningModel);
                        break;
                    }
                case AreaTypesEnum.Type6: // heat pumps
                    {
                        await CreatePlanningType6(areaRule, sdkDbContext, areaRulePlanningModel, core);
                        break;
                    }
                case AreaTypesEnum.Type9: // heat pumps
                    {
                        await CreatePlanningType9(areaRule, sdkDbContext, areaRulePlanningModel, core);
                        break;
                    }
                case AreaTypesEnum.Type10:
                {
                    await CreatePlanningType10(areaRule, sdkDbContext, areaRulePlanningModel, core);
                    break;
                }
                default:
                    {
                        await CreatePlanningDefaultType(areaRule, sdkDbContext, areaRulePlanningModel, core);
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
                    .FirstAsync();
                planning.UpdatedByUserId = _userService.UserId;
                foreach (var planningSite in planning.PlanningSites
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed))
                {
                    planningSite.UpdatedByUserId = _userService.UserId;
                    await planningSite.Delete(_itemsPlanningPnDbContext);
                }

                var core = await _coreHelper.GetCore();
                await using var sdkDbContext = core.DbContextHelper.GetDbContext();
                var planningCases = await _itemsPlanningPnDbContext.PlanningCases
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.PlanningId == planning.Id)
                    .ToListAsync();

                foreach (var planningCase in planningCases)
                {
                    var planningCaseSites = await _itemsPlanningPnDbContext.PlanningCaseSites
                        .Where(x => x.PlanningCaseId == planningCase.Id)
                        .Where(planningCaseSite => planningCaseSite.MicrotingSdkCaseId != 0 || planningCaseSite.MicrotingCheckListSitId != 0)
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .ToListAsync();
                    foreach (var planningCaseSite in planningCaseSites)
                    {
                        var result =
                            await sdkDbContext.Cases.SingleOrDefaultAsync(x => x.Id == planningCaseSite.MicrotingSdkCaseId);
                        if (result is {MicrotingUid: { }})
                        {
                            await core.CaseDelete((int)result.MicrotingUid);
                        }
                        else
                        {
                            var clSites = await sdkDbContext.CheckListSites.SingleAsync(x =>
                                x.Id == planningCaseSite.MicrotingCheckListSitId);

                            await core.CaseDelete(clSites.MicrotingUid);
                        }
                    }
                }

                var nameTranslationsPlanning =
                    planning.NameTranslations
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .ToList();

                foreach (var translation in nameTranslationsPlanning)
                {
                    await translation.Delete(_itemsPlanningPnDbContext);
                }

                await planning.Delete(_itemsPlanningPnDbContext);


                if (!_itemsPlanningPnDbContext.PlanningSites.AsNoTracking().Any(x => x.PlanningId == planning.Id && x.WorkflowState != Constants.WorkflowStates.Removed))
                {
                    var complianceList = await _backendConfigurationPnDbContext.Compliances
                        .Where(x => x.PlanningId == planning.Id).AsNoTracking().ToListAsync();
                    foreach (var compliance in complianceList)
                    {
                        var dbCompliacne =
                            await _backendConfigurationPnDbContext.Compliances.SingleAsync(x => x.Id == compliance.Id);
                        await dbCompliacne.Delete(_backendConfigurationPnDbContext);
                        var property = await _backendConfigurationPnDbContext.Properties.SingleAsync(x => x.Id == compliance.PropertyId);
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

                        await property.Update(_backendConfigurationPnDbContext);
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
                .FirstAsync();
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
                var language = await _userService.GetCurrentUserLanguage();
                var listTaskWorker = new List<TaskWorkerModel>();

                var propertyIds = await _backendConfigurationPnDbContext.PropertyWorkers
                    .Where(x =>
                        x.WorkerId == siteId
                        && x.WorkflowState != Constants.WorkflowStates.Removed).Select(x => x.PropertyId).ToListAsync();

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
                    .ToListAsync();
                // var total = sitePlannings.Count;
                foreach (var sitePlanning in sitePlannings)
                {
                    var areaName = await _backendConfigurationPnDbContext.AreaTranslations
                        .Where(x => x.AreaId == sitePlanning.AreaId)
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Where(x => x.LanguageId == language.Id)
                        .Select(x => x.Name)
                        .FirstOrDefaultAsync();

                    var areaRuleName = await _backendConfigurationPnDbContext.AreaRuleTranslations
                        .Where(x => x.AreaRuleId == sitePlanning.AreaRuleId)
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Where(x => x.LanguageId == language.Id)
                        .Select(x => x.Name)
                        .FirstOrDefaultAsync();

                    var propertyName = await _backendConfigurationPnDbContext.Properties
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Where(x => x.Id == sitePlanning.PropertyId)
                        .Select(x => x.Name)
                        .FirstOrDefaultAsync();

                    var itemPlanningName = await _itemsPlanningPnDbContext.PlanningNameTranslation
                        .Where(x => x.PlanningId == sitePlanning.ItemPlanningId)
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Where(x => x.LanguageId == language.Id)
                        .Select(x => x.Name)
                        .FirstOrDefaultAsync();

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
                    .FirstOrDefaultAsync();

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
            var folderId = await core.FolderCreate(translatesForFolder, areaRule.FolderId);
            var planningForType2TypeTankOpenId = 0;
            if (areaRule.Type == AreaRuleT2TypesEnum.Open)
            {
                const string eformName = "03. Kontrol flydelag";
                var eformId = await sdkDbContext.CheckListTranslations
                    .Where(x => x.Text == eformName)
                    .Select(x => x.CheckListId)
                    .FirstAsync();

                if (areaRulePlanningModel.Status)
                {
                    var planningForType2TypeTankOpen = await CreateItemPlanningObject(eformId, eformName,
                        folderId, areaRulePlanningModel, areaRule);
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

                    await planningForType2TypeTankOpen.Create(_itemsPlanningPnDbContext);
                    await _pairItemWichSiteHelper.Pair(
                        areaRulePlanningModel.AssignedSites.Select(x => x.SiteId).ToList(), eformId,
                        planningForType2TypeTankOpen.Id,
                        folderId);
                    planningForType2TypeTankOpenId = planningForType2TypeTankOpen.Id;
                }
            }

            var areaRulePlanningForType2TypeTankOpen = CreateAreaRulePlanningObject(areaRulePlanningModel,
                areaRule, planningForType2TypeTankOpenId,
                folderId);
            await areaRulePlanningForType2TypeTankOpen.Create(_backendConfigurationPnDbContext);

            var planningForType2AlarmYesId = 0;
            if (areaRule.Type is AreaRuleT2TypesEnum.Open or AreaRuleT2TypesEnum.Closed
                && areaRule.Alarm is AreaRuleT2AlarmsEnum.Yes)
            {
                const string eformName = "03. Kontrol alarmanlæg gyllebeholder";
                var eformId = await sdkDbContext.CheckListTranslations
                    .Where(x => x.Text == eformName)
                    .Select(x => x.CheckListId)
                    .FirstAsync();

                if (areaRulePlanningModel.Status)
                {
                    var planningForType2AlarmYes = await CreateItemPlanningObject(eformId, eformName, folderId,
                        areaRulePlanningModel, areaRule);
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

                    await planningForType2AlarmYes.Create(_itemsPlanningPnDbContext);
                    await _pairItemWichSiteHelper.Pair(
                        areaRulePlanningModel.AssignedSites.Select(x => x.SiteId).ToList(), eformId,
                        planningForType2AlarmYes.Id,
                        folderId);
                    planningForType2AlarmYesId = planningForType2AlarmYes.Id;
                }
            }

            var areaRulePlanningForType2AlarmYes = CreateAreaRulePlanningObject(areaRulePlanningModel, areaRule,
                planningForType2AlarmYesId,
                folderId);
            await areaRulePlanningForType2AlarmYes.Create(_backendConfigurationPnDbContext);

            var planningForType2Id = 0;
            if (areaRulePlanningModel.Status)
            {
                //areaRule.EformName must be "03. Kontrol konstruktion"
                var planningForType2 = await CreateItemPlanningObject((int)areaRule.EformId,
                    areaRule.EformName, folderId, areaRulePlanningModel, areaRule);
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

                await planningForType2.Create(_itemsPlanningPnDbContext);
                await _pairItemWichSiteHelper.Pair(
                    areaRulePlanningModel.AssignedSites.Select(x => x.SiteId).ToList(), (int)areaRule.EformId,
                    planningForType2.Id,
                    folderId);
                planningForType2Id = planningForType2.Id;
            }

            var areaRulePlanningForType2 = CreateAreaRulePlanningObject(areaRulePlanningModel, areaRule,
                planningForType2Id,
                folderId);
            await areaRulePlanningForType2.Create(_backendConfigurationPnDbContext);
        }

        private async Task CreatePlanningType3(AreaRule areaRule, MicrotingDbContext sdkDbContext, AreaRulePlanningModel areaRulePlanningModel, eFormCore.Core core)
        {
            await CreatePlanningDefaultType(areaRule, sdkDbContext, areaRulePlanningModel, core);

            var sites = areaRulePlanningModel.AssignedSites.Select(x => x.SiteId).ToList();
            if (areaRulePlanningModel.Status)
            {
                foreach (var siteId in sites)
                {
                    var site = await sdkDbContext.Sites.SingleOrDefaultAsync(x => x.Id == siteId);
                    var language =
                        await sdkDbContext.Languages.SingleOrDefaultAsync(x => x.Id == site.LanguageId);
                    var entityListUid = await _backendConfigurationPnDbContext.AreaProperties
                        .Where(x => x.PropertyId == areaRule.PropertyId)
                        .Where(x => x.AreaId == areaRule.AreaId)
                        .Select(x => x.GroupMicrotingUuid)
                        .FirstAsync();
                    if (!sdkDbContext.CheckListSites
                            .Any(x =>
                                x.CheckListId == areaRule.EformId &&
                                x.SiteId == siteId &&
                                x.WorkflowState != Constants.WorkflowStates.Removed))
                    {
                        var mainElement = await core.ReadeForm((int)areaRule.EformId, language);
                        // todo add group id to eform
                        var folder = await sdkDbContext.Folders.SingleAsync(x => x.Id == areaRule.FolderId);
                        var folderMicrotingId = folder.MicrotingUid.ToString();
                        mainElement.Repeated = -1;
                        mainElement.CheckListFolderName = folderMicrotingId;
                        mainElement.StartDate = DateTime.Now.ToUniversalTime();
                        mainElement.EndDate = DateTime.Now.AddYears(10).ToUniversalTime();
                        mainElement.DisplayOrder = 10000000;
                        ((EntitySelect)((DataElement)mainElement.ElementList[0]).DataItemList[1]).Source = entityListUid;
                        /*var caseId = */
                        await core.CaseCreate(mainElement, "", (int)site.MicrotingUid, folder.Id);
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
                .ToListAsync();
            var folderId = folderIds[areaRule.DayOfWeek];
            var planningId = 0;
            if (areaRulePlanningModel.Status)
            {
                areaRulePlanningModel.TypeSpecificFields ??=
                    new AreaRuleTypePlanningModel(); // if areaRulePlanningModel.TypeSpecificFields == null -> areaRulePlanningModel.TypeSpecificFields = new()
                areaRulePlanningModel.TypeSpecificFields.RepeatEvery =
                    areaRule.RepeatEvery; // repeat every mast be from area rule
                var planning = await CreateItemPlanningObject((int)areaRule.EformId, areaRule.EformName,
                    folderId, areaRulePlanningModel, areaRule);
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

                await planning.Create(_itemsPlanningPnDbContext);
                await _pairItemWichSiteHelper.Pair(
                    areaRulePlanningModel.AssignedSites.Select(x => x.SiteId).ToList(), (int)areaRule.EformId,
                    planning.Id,
                    folderId);
                planningId = planning.Id;

            }
            var areaRulePlanning = CreateAreaRulePlanningObject(areaRulePlanningModel, areaRule, planningId,
                folderId);
            await areaRulePlanning.Create(_backendConfigurationPnDbContext);
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
            var folderId = await core.FolderCreate(translatesForFolder, areaRule.FolderId);

            var planningForType6HoursAndEnergyEnabledId = 0;
            var planningForType6IdOne = 0;
            var planningForType6IdTwo = 0;
            if (areaRulePlanningModel.TypeSpecificFields?.HoursAndEnergyEnabled is true &&
                areaRulePlanningModel.Status)
            {
                await areaRule.Update(_backendConfigurationPnDbContext);
                const string eformName = "10. Varmepumpe timer og energi";
                var eformId = await sdkDbContext.CheckListTranslations
                    .Where(x => x.Text == eformName)
                    .Select(x => x.CheckListId)
                    .FirstAsync();
                var planningForType6HoursAndEnergyEnabled = await CreateItemPlanningObject(eformId, eformName,
                    folderId, areaRulePlanningModel, areaRule);
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

                await planningForType6HoursAndEnergyEnabled.Create(_itemsPlanningPnDbContext);
                await _pairItemWichSiteHelper.Pair(
                    areaRulePlanningModel.AssignedSites.Select(x => x.SiteId).ToList(), eformId,
                    planningForType6HoursAndEnergyEnabled.Id,
                    folderId);
                planningForType6HoursAndEnergyEnabledId = planningForType6HoursAndEnergyEnabled.Id;
            }

            if (areaRulePlanningModel.Status)
            {
                const string eformNameOne = "10. Varmepumpe serviceaftale";
                var eformIdOne = await sdkDbContext.CheckListTranslations
                    .Where(x => x.Text == eformNameOne)
                    .Select(x => x.CheckListId)
                    .FirstAsync();
                const string eformNameTwo = "10. Varmepumpe driftsstop";
                var eformIdTwo = await sdkDbContext.CheckListTranslations
                    .Where(x => x.Text == eformNameTwo)
                    .Select(x => x.CheckListId)
                    .FirstAsync();
                var planningForType6One = await CreateItemPlanningObject(eformIdOne, eformNameOne, folderId,
                    areaRulePlanningModel, areaRule);
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
                    areaRulePlanningModel, areaRule);
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

                await planningForType6One.Create(_itemsPlanningPnDbContext);
                await _pairItemWichSiteHelper.Pair(
                    areaRulePlanningModel.AssignedSites.Select(x => x.SiteId).ToList(), eformIdOne,
                    planningForType6One.Id,
                    folderId);
                planningForType6IdOne = planningForType6One.Id;
                await planningForType6Two.Create(_itemsPlanningPnDbContext);
                await _pairItemWichSiteHelper.Pair(
                    areaRulePlanningModel.AssignedSites.Select(x => x.SiteId).ToList(), eformIdTwo,
                    planningForType6Two.Id,
                    folderId);
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
                _backendConfigurationPnDbContext);
            await areaRulePlanningForType6One.Create(_backendConfigurationPnDbContext);
            await areaRulePlanningForType6Two.Create(_backendConfigurationPnDbContext);
        }

        private async Task CreatePlanningType9(AreaRule areaRule, MicrotingDbContext sdkDbContext, AreaRulePlanningModel areaRulePlanningModel, Core core)
        {
            // await CreatePlanningDefaultType(areaRule, sdkDbContext, areaRulePlanningModel, core);

            var sites = areaRulePlanningModel.AssignedSites.Select(x => x.SiteId).ToList();
            if (areaRulePlanningModel.Status)
            {
                var siteId = sites.First();
                var site = await sdkDbContext.Sites.SingleOrDefaultAsync(x => x.Id == siteId);
                var language =
                    await sdkDbContext.Languages.SingleOrDefaultAsync(x => x.Id == site.LanguageId);
                var entityListUid = (int)await _backendConfigurationPnDbContext.Properties
                    .Where(x => x.Id == areaRule.PropertyId)
                    .Select(x => x.EntitySearchListChemicals)
                    .FirstAsync().ConfigureAwait(false);
                var entityListUidRegNo = (int)await _backendConfigurationPnDbContext.Properties
                    .Where(x => x.Id == areaRule.PropertyId)
                    .Select(x => x.EntitySearchListChemicalRegNos)
                    .FirstAsync().ConfigureAwait(false);
                // if (!sdkDbContext.CheckListSites
                //         .Any(x =>
                //             x.CheckListId == areaRule.EformId &&
                //             x.SiteId == siteId &&
                //             x.WorkflowState != Constants.WorkflowStates.Removed))
                // {
                var mainElement = await core.ReadeForm((int)areaRule.EformId, language);
                // todo add group id to eform
                var folder = await sdkDbContext.Folders.SingleAsync(x => x.Id == areaRule.FolderId);
                var folderMicrotingId = folder.MicrotingUid.ToString();
                mainElement.Repeated = 0;
                mainElement.CheckListFolderName = folderMicrotingId;
                mainElement.StartDate = DateTime.Now.ToUniversalTime();
                mainElement.EndDate = DateTime.Now.AddYears(10).ToUniversalTime();
                mainElement.DisplayOrder = 10000000;
                ((EntitySearch)((DataElement)mainElement.ElementList[0]).DataItemList[0]).EntityTypeId = entityListUid;
                ((EntitySearch)((DataElement)mainElement.ElementList[0]).DataItemList[1]).EntityTypeId = entityListUidRegNo;
                ((EntitySearch)((DataElement)mainElement.ElementList[0]).DataItemList[2]).EntityTypeId = entityListUid;
                ((EntitySearch)((DataElement)mainElement.ElementList[0]).DataItemList[3]).EntityTypeId = entityListUidRegNo;
                ((EntitySearch)((DataElement)mainElement.ElementList[0]).DataItemList[4]).EntityTypeId = entityListUid;
                ((EntitySearch)((DataElement)mainElement.ElementList[0]).DataItemList[5]).EntityTypeId = entityListUidRegNo;
                ((EntitySearch)((DataElement)mainElement.ElementList[0]).DataItemList[6]).EntityTypeId = entityListUid;
                ((EntitySearch)((DataElement)mainElement.ElementList[0]).DataItemList[7]).EntityTypeId = entityListUidRegNo;
                ((EntitySearch)((DataElement)mainElement.ElementList[0]).DataItemList[8]).EntityTypeId = entityListUid;
                ((EntitySearch)((DataElement)mainElement.ElementList[0]).DataItemList[9]).EntityTypeId = entityListUidRegNo;
                ((EntitySearch)((DataElement)mainElement.ElementList[0]).DataItemList[10]).EntityTypeId = entityListUid;
                ((EntitySearch)((DataElement)mainElement.ElementList[0]).DataItemList[11]).EntityTypeId = entityListUidRegNo;
                ((EntitySearch)((DataElement)mainElement.ElementList[0]).DataItemList[12]).EntityTypeId = entityListUid;
                ((EntitySearch)((DataElement)mainElement.ElementList[0]).DataItemList[13]).EntityTypeId = entityListUidRegNo;
                ((EntitySearch)((DataElement)mainElement.ElementList[0]).DataItemList[14]).EntityTypeId = entityListUid;
                ((EntitySearch)((DataElement)mainElement.ElementList[0]).DataItemList[15]).EntityTypeId = entityListUidRegNo;
                ((EntitySearch)((DataElement)mainElement.ElementList[0]).DataItemList[16]).EntityTypeId = entityListUid;
                ((EntitySearch)((DataElement)mainElement.ElementList[0]).DataItemList[17]).EntityTypeId = entityListUidRegNo;
                ((EntitySearch)((DataElement)mainElement.ElementList[0]).DataItemList[18]).EntityTypeId = entityListUid;
                ((EntitySearch)((DataElement)mainElement.ElementList[0]).DataItemList[19]).EntityTypeId = entityListUidRegNo;
                /*var caseId = */
                var caseId = await core.CaseCreate(mainElement, "", (int)site.MicrotingUid, folder.Id);

                var planning = await CreateItemPlanningObject((int)areaRule.EformId, areaRule.EformName,
                    areaRule.FolderId, areaRulePlanningModel, areaRule);
                planning.RepeatEvery = 0;
                planning.RepeatType = RepeatType.Day;
                planning.StartDate = DateTime.Now.ToUniversalTime();
                var now = DateTime.UtcNow;
                planning.LastExecutedTime = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0);
                await planning.Create(_itemsPlanningPnDbContext);
                var planningCase = new PlanningCase
                {
                    PlanningId = planning.Id,
                    Status = 66,
                    MicrotingSdkeFormId = (int)areaRule.EformId
                };
                await planningCase.Create(_itemsPlanningPnDbContext);
                var planningCaseSite = new PlanningCaseSite
                {
                    MicrotingSdkSiteId = siteId,
                    MicrotingSdkeFormId = (int)areaRule.EformId,
                    Status = 66,
                    PlanningId = planning.Id,
                    PlanningCaseId = planningCase.Id,
                    MicrotingSdkCaseId = (int)caseId
                };

                await planningCaseSite.Create(_itemsPlanningPnDbContext);

                var areaRulePlanning = CreateAreaRulePlanningObject(areaRulePlanningModel, areaRule, planning.Id,
                    areaRule.FolderId);


                await areaRulePlanning.Create(_backendConfigurationPnDbContext);
                // }
            }
        }

        private async Task CreatePlanningType10(AreaRule areaRule, MicrotingDbContext sdkDbContext,
            AreaRulePlanningModel areaRulePlanningModel, Core core)
        {
            //var sites = areaRulePlanningModel.AssignedSites.Select(x => x.SiteId).ToList();
            if (areaRulePlanningModel.Status)
            {
                var parrings = await _backendConfigurationPnDbContext.PoolHours
                    .Where(x => x.AreaRuleId == areaRule.Id)
                    .Where(x => x.IsActive == true)
                    .ToListAsync();

                var property = await _backendConfigurationPnDbContext.Properties.SingleAsync(x => x.Id == areaRule.PropertyId);

                var lookupName = areaRule.AreaRuleTranslations.First().Name;

                var subfolder = await sdkDbContext.Folders
                    .Include(x => x.FolderTranslations)
                    .Where(x=> x.ParentId == areaRule.FolderId)
                    .Where(x => x.FolderTranslations.Any(y => y.Name == lookupName))
                    .FirstOrDefaultAsync();

                Regex regex = new Regex(@"(\d\.\s)");
                DayOfWeek? currentWeekDay= null;
                var clId = sdkDbContext.CheckListTranslations.Where(x => x.Text == $"02. Fækale uheld - {property.Name}").Select(x => x.CheckListId).FirstOrDefault();
                var clTranslations = await sdkDbContext.CheckListTranslations.Where(x => x.CheckListId == clId).ToListAsync();
                foreach (var poolHour in parrings)
                {
                    var innerLookupName = $"{(int)poolHour.DayOfWeek}. {poolHour.DayOfWeek.ToString().Substring(0, 3)}";
                    var poolDayFolder = await sdkDbContext.Folders
                        .Include(x => x.FolderTranslations)
                        .Where(x=> x.ParentId == subfolder.Id)
                        .Where(x => x.FolderTranslations.Any(y => y.Name == innerLookupName))
                        .FirstAsync();
                    if (currentWeekDay == null || currentWeekDay != (DayOfWeek)poolHour.DayOfWeek)
                    {
                        var planningStatic = await CreateItemPlanningObject(clId, $"02. Fækale uheld - {property.Name}",
                            poolDayFolder.Id, areaRulePlanningModel, areaRule);
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
                        await planningStatic.Create(_itemsPlanningPnDbContext);
                        await _pairItemWichSiteHelper.Pair(
                            areaRulePlanningModel.AssignedSites.Select(x => x.SiteId).ToList(), clId, planningStatic.Id,
                            poolDayFolder.Id);
                        var areaRulePlanningStatic = CreateAreaRulePlanningObject(areaRulePlanningModel, areaRule, planningStatic.Id,
                            areaRule.FolderId);
                        areaRulePlanningStatic.ComplianceEnabled = false;
                        areaRulePlanningStatic.RepeatEvery = 0;
                        areaRulePlanningStatic.RepeatType = (int)RepeatType.Day;
                        areaRulePlanningStatic.FolderId = poolDayFolder.Id;

                        await areaRulePlanningStatic.Create(_backendConfigurationPnDbContext);
                    }

                    currentWeekDay = (DayOfWeek)poolHour.DayOfWeek;

                    clId = sdkDbContext.CheckListTranslations.Where(x => x.Text == $"01. Aflæsninger - {property.Name}").Select(x => x.CheckListId).FirstOrDefault();
                    var planning = await CreateItemPlanningObject(clId, $"01. Aflæsninger - {property.Name}",
                        poolDayFolder.Id, areaRulePlanningModel, areaRule);
                    planning.DayOfWeek = (DayOfWeek)poolHour.DayOfWeek;
                    planning.RepeatEvery = 1;
                    planning.RepeatType = RepeatType.Week;
                    planning.SdkFolderName = innerLookupName;
                    planning.PushMessageOnDeployment = false;
                    planning.NameTranslations = areaRule.AreaRuleTranslations.Select(
                        areaRuleAreaRuleTranslation => new PlanningNameTranslation
                        {
                            LanguageId = areaRuleAreaRuleTranslation.LanguageId,
                            Name =
                                $"{poolDayFolder.FolderTranslations.Where(x => x.LanguageId == areaRuleAreaRuleTranslation.LanguageId).Select(x => x.Name).First()} - {poolHour.Name}:00 - {areaRuleAreaRuleTranslation.Name}",
                        }).ToList();
                    foreach (var planningNameTranslation in planning.NameTranslations)
                    {
                        planningNameTranslation.Name = regex.Replace(planningNameTranslation.Name, "");
                        planningNameTranslation.Name = $"{poolHour.Name}. {planningNameTranslation.Name}";
                    }
                    await planning.Create(_itemsPlanningPnDbContext);

                    var areaRulePlanning = CreateAreaRulePlanningObject(areaRulePlanningModel, areaRule, planning.Id,
                        areaRule.FolderId);
                    areaRulePlanning.ComplianceEnabled = false;
                    areaRulePlanning.RepeatEvery = 1;
                    areaRulePlanning.RepeatType = (int)RepeatType.Day;
                    areaRulePlanning.DayOfWeek = (int)(DayOfWeek)poolHour.DayOfWeek;
                    areaRulePlanning.FolderId = poolDayFolder.Id;

                    await areaRulePlanning.Create(_backendConfigurationPnDbContext);
                }
            }
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
                    .FirstOrDefaultAsync();
                if (folderId != 0)
                {
                    areaRule.FolderId = folderId;
                    areaRule.FolderName = await sdkDbContext.FolderTranslations
                        .Where(x => x.FolderId == folderId)
                        .Where(x => x.LanguageId == 1) // danish
                        .Select(x => x.Name)
                        .FirstAsync();
                    await areaRule.Update(_backendConfigurationPnDbContext);
                }
            }

            var planningId = 0;
            if (areaRulePlanningModel.Status)
            {
                var planning = await CreateItemPlanningObject((int)areaRule.EformId, areaRule.EformName,
                    areaRule.FolderId, areaRulePlanningModel, areaRule);
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

                await planning.Create(_itemsPlanningPnDbContext);
                await _pairItemWichSiteHelper.Pair(
                    areaRulePlanningModel.AssignedSites.Select(x => x.SiteId).ToList(), (int)areaRule.EformId,
                    planning.Id,
                    areaRule.FolderId);
                planningId = planning.Id;
            }

            var areaRulePlanning = CreateAreaRulePlanningObject(areaRulePlanningModel, areaRule, planningId,
                areaRule.FolderId);

            await areaRulePlanning.Create(_backendConfigurationPnDbContext);
        }
    }
}