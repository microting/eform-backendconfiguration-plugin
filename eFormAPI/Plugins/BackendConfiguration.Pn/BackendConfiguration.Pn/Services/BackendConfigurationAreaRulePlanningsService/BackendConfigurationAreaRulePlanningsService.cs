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
    using Microting.eForm.Infrastructure.Constants;
    using Microting.eForm.Infrastructure.Models;
    using Microting.eFormApi.BasePn.Abstractions;
    using Microting.eFormApi.BasePn.Infrastructure.Helpers;
    using Microting.eFormApi.BasePn.Infrastructure.Models.API;
    using Microting.EformBackendConfigurationBase.Infrastructure.Data;
    using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
    using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
    using Microting.ItemsPlanningBase.Infrastructure.Data;
    using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
    using Microting.ItemsPlanningBase.Infrastructure.Enums;
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

                if (areaRulePlanningModel.Id.HasValue) // update planning
                {
                    var areaRule = await _backendConfigurationPnDbContext.AreaRules
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Where(x => x.Id == areaRulePlanningModel.RuleId)
                        .Include(x => x.AreaRulesPlannings)
                        .ThenInclude(x => x.PlanningSites)
                        .Include(x => x.Area)
                        .Include(x => x.AreaRuleTranslations)
                        .FirstOrDefaultAsync();
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
                                .Where(y => y.WorkflowState != Constants.WorkflowStates.Removed).Select(y => y.SiteId)
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
                                            var planningForType2TypeTankOpen = await CreateItemPlanningObject(eformId,
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
                                                    (DayOfWeek) areaRulePlanningModel.TypeSpecificFields.DayOfWeek == 0
                                                        ? DayOfWeek.Monday
                                                        : (DayOfWeek) areaRulePlanningModel.TypeSpecificFields.DayOfWeek;
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
                                                    (DayOfWeek) areaRulePlanningModel.TypeSpecificFields.DayOfWeek == 0
                                                        ? DayOfWeek.Monday
                                                        : (DayOfWeek) areaRulePlanningModel.TypeSpecificFields.DayOfWeek;
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
                                            areaRule.AreaRulesPlannings[1].ItemPlanningId = planningForType2AlarmYes.Id;
                                            await areaRule.AreaRulesPlannings[1]
                                                .Update(_backendConfigurationPnDbContext);
                                        }
                                        else
                                        {
                                            if (areaRule.AreaRulesPlannings[1].ItemPlanningId != 0)
                                            {
                                                await DeleteItemPlanning(areaRule.AreaRulesPlannings[1].ItemPlanningId);
                                                areaRule.AreaRulesPlannings[1].ItemPlanningId = 0;
                                                await areaRule.AreaRulesPlannings[1]
                                                    .Update(_backendConfigurationPnDbContext);
                                            }
                                        }

                                        /*areaRule.EformName must be "03. Kontrol konstruktion"*/
                                        var planningForType2 = await CreateItemPlanningObject((int) areaRule.EformId,
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
                                                (DayOfWeek) areaRulePlanningModel.TypeSpecificFields.DayOfWeek == 0
                                                    ? DayOfWeek.Monday
                                                    : (DayOfWeek) areaRulePlanningModel.TypeSpecificFields.DayOfWeek;
                                            planningForType2.DayOfMonth =
                                                areaRulePlanningModel.TypeSpecificFields.DayOfMonth == 0
                                                    ? 1
                                                    : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                                        }

                                        await planningForType2.Create(_itemsPlanningPnDbContext);
                                        await _pairItemWichSiteHelper.Pair(
                                            rulePlanning.PlanningSites.Select(x => x.SiteId).ToList(),
                                            (int) areaRule.EformId,
                                            planningForType2.Id,
                                            areaRule.AreaRulesPlannings[2].FolderId);
                                        areaRule.AreaRulesPlannings[2].ItemPlanningId = planningForType2.Id;
                                        areaRule.AreaRulesPlannings[2].DayOfMonth =
                                            (int) areaRulePlanningModel.TypeSpecificFields?.DayOfMonth == 0 ? 1 : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                                        await areaRule.AreaRulesPlannings[2].Update(_backendConfigurationPnDbContext);
                                        i = areaRule.AreaRulesPlannings.Count;
                                        break;
                                    }
                                    case AreaTypesEnum.Type3: // stables and tail bite
                                    {
                                        if (areaRule.ChecklistStable is true)
                                        {
                                            var planningForType3ChecklistStable =
                                                await CreateItemPlanningObject((int) areaRule.EformId,
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
                                                        (RepeatType) areaRulePlanningModel.TypeSpecificFields
                                                            .RepeatType;
                                                }

                                                if (areaRulePlanningModel.TypeSpecificFields?.RepeatEvery != null)
                                                {
                                                    planningForType3ChecklistStable.RepeatEvery =
                                                        (int) areaRulePlanningModel.TypeSpecificFields.RepeatEvery;
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
                                                (int) areaRule.EformId,
                                                planningForType3ChecklistStable.Id,
                                                areaRule.FolderId);
                                            areaRule.AreaRulesPlannings[0].ItemPlanningId =
                                                planningForType3ChecklistStable.Id;
                                            areaRule.AreaRulesPlannings[0].DayOfMonth =
                                                (int)areaRulePlanningModel.TypeSpecificFields?.DayOfMonth == 0
                                                    ? 1
                                                    : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                                            areaRule.AreaRulesPlannings[0].DayOfWeek =
                                                (int) areaRulePlanningModel.TypeSpecificFields?.DayOfWeek == 0
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
                                            foreach (int siteId in sites)
                                            {
                                                Property property =
                                                    await _backendConfigurationPnDbContext.Properties
                                                        .SingleOrDefaultAsync(x => x.Id == areaRule.PropertyId);
                                                string eformName = $"05. Halebid - {property.Name}";
                                                var eformId = await sdkDbContext.CheckListTranslations
                                                    .Where(x => x.Text == eformName)
                                                    .Select(x => x.CheckListId)
                                                    .FirstAsync();
                                                Site site = await sdkDbContext.Sites.SingleOrDefaultAsync(x =>
                                                    x.Id == siteId);
                                                Language language =
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
                                                    /*var caseId = */await core.CaseCreate(mainElement, "",
                                                        (int) site.MicrotingUid, folder.Id);
                                                }
                                            }
                                            var areaProperty = await _backendConfigurationPnDbContext.AreaProperties
                                                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                                .Where(x => x.AreaId == areaRule.AreaId)
                                                .Where(x => x.PropertyId == areaRule.PropertyId)
                                                .Select(x => new {x.Area, x.GroupMicrotingUuid, x.PropertyId})
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
                                            Property property =
                                                await _backendConfigurationPnDbContext.Properties
                                                    .SingleOrDefaultAsync(x => x.Id == areaRule.PropertyId);
                                            string eformName = $"05. Halebid - {property.Name}";
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
                                                foreach (CheckListSite checkListSite in sdkDbContext.CheckListSites
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
                                    }
                                    case AreaTypesEnum.Type6: // heat pumps
                                    {
                                        if (areaRulePlanningModel.TypeSpecificFields?.HoursAndEnergyEnabled is true)
                                        {
                                            areaRule.AreaRulesPlannings[0].HoursAndEnergyEnabled = true;
                                            areaRule.AreaRulesPlannings[1].HoursAndEnergyEnabled = true;
                                            areaRule.AreaRulesPlannings[2].HoursAndEnergyEnabled = true;
                                            areaRule.AreaRulesPlannings[0].DayOfMonth =
                                                (int)areaRulePlanningModel.TypeSpecificFields?.DayOfMonth == 0
                                                    ? 1
                                                    : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                                            areaRule.AreaRulesPlannings[0].DayOfWeek =
                                                (int) areaRulePlanningModel.TypeSpecificFields?.DayOfWeek == 0
                                                    ? 1
                                                    : areaRulePlanningModel.TypeSpecificFields.DayOfWeek;
                                            areaRule.AreaRulesPlannings[1].DayOfMonth =
                                                (int)areaRulePlanningModel.TypeSpecificFields?.DayOfMonth == 0
                                                    ? 1
                                                    : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                                            areaRule.AreaRulesPlannings[1].DayOfWeek =
                                                (int) areaRulePlanningModel.TypeSpecificFields?.DayOfWeek == 0
                                                    ? 1
                                                    : areaRulePlanningModel.TypeSpecificFields.DayOfWeek;
                                            areaRule.AreaRulesPlannings[2].DayOfMonth =
                                                (int)areaRulePlanningModel.TypeSpecificFields?.DayOfMonth == 0
                                                    ? 1
                                                    : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                                            areaRule.AreaRulesPlannings[2].DayOfWeek =
                                                (int) areaRulePlanningModel.TypeSpecificFields?.DayOfWeek == 0
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
                                                    areaRule.AreaRulesPlannings[0].FolderId, areaRulePlanningModel,
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
                                                        (RepeatType) areaRulePlanningModel.TypeSpecificFields
                                                            .RepeatType;
                                                }

                                                if (areaRulePlanningModel.TypeSpecificFields?.RepeatEvery != null)
                                                {
                                                    planningForType6HoursAndEnergyEnabled.RepeatEvery =
                                                        (int) areaRulePlanningModel.TypeSpecificFields.RepeatEvery;
                                                }

                                                planningForType6HoursAndEnergyEnabled.DayOfMonth =
                                                    areaRulePlanningModel.TypeSpecificFields.DayOfMonth == 0
                                                        ? 1
                                                        : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                                                planningForType6HoursAndEnergyEnabled.DayOfWeek =
                                                    (DayOfWeek?) areaRulePlanningModel.TypeSpecificFields.DayOfWeek == 0
                                                        ? DayOfWeek.Monday
                                                        : (DayOfWeek?) areaRulePlanningModel.TypeSpecificFields
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
                                                await DeleteItemPlanning(areaRule.AreaRulesPlannings[0].ItemPlanningId);
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
                                                (DayOfWeek?) areaRulePlanningModel.TypeSpecificFields.DayOfWeek == 0
                                                    ? DayOfWeek.Monday
                                                    : (DayOfWeek?) areaRulePlanningModel.TypeSpecificFields.DayOfWeek;
                                            planningForType6One.RepeatUntil =
                                                areaRulePlanningModel.TypeSpecificFields.EndDate;
                                            planningForType6Two.DayOfMonth =
                                                areaRulePlanningModel.TypeSpecificFields.DayOfMonth == 0
                                                    ? 1
                                                    : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                                            planningForType6Two.DayOfWeek =
                                                (DayOfWeek?) areaRulePlanningModel.TypeSpecificFields.DayOfWeek == 0
                                                    ? DayOfWeek.Monday
                                                    : (DayOfWeek?) areaRulePlanningModel.TypeSpecificFields.DayOfWeek;
                                            planningForType6Two.RepeatUntil =
                                                areaRulePlanningModel.TypeSpecificFields.EndDate;
                                        }

                                        await planningForType6One.Create(_itemsPlanningPnDbContext);
                                        await planningForType6Two.Create(_itemsPlanningPnDbContext);
                                        await _pairItemWichSiteHelper.Pair(
                                            rulePlanning.PlanningSites.Select(x => x.SiteId).ToList(), eformIdOne,
                                            planningForType6One.Id,
                                            areaRule.AreaRulesPlannings[1].FolderId);
                                        await _pairItemWichSiteHelper.Pair(
                                            rulePlanning.PlanningSites.Select(x => x.SiteId).ToList(), eformIdTwo,
                                            planningForType6Two.Id,
                                            areaRule.AreaRulesPlannings[2].FolderId);
                                        areaRule.AreaRulesPlannings[1].ItemPlanningId = planningForType6One.Id;
                                        areaRule.AreaRulesPlannings[2].ItemPlanningId = planningForType6Two.Id;
                                        await areaRule.AreaRulesPlannings[1].Update(_backendConfigurationPnDbContext);
                                        await areaRule.AreaRulesPlannings[2].Update(_backendConfigurationPnDbContext);
                                        i = areaRule.AreaRulesPlannings.Count;
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
                                            var folderIds = await _backendConfigurationPnDbContext.ProperyAreaFolders
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

                                        var planning = await CreateItemPlanningObject((int) areaRule.EformId,
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
                                                    : (DayOfWeek?)areaRulePlanningModel.TypeSpecificFields.DayOfWeek;
                                            planning.RepeatUntil =
                                                areaRulePlanningModel.TypeSpecificFields.EndDate;
                                            if (areaRulePlanningModel.TypeSpecificFields.RepeatEvery is not null)
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

                                        await planning.Create(_itemsPlanningPnDbContext);
                                        await _pairItemWichSiteHelper.Pair(
                                            rulePlanning.PlanningSites.Select(x => x.SiteId).ToList(),
                                            (int) areaRule.EformId,
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
                                    await DeleteItemPlanning(rulePlanning.ItemPlanningId);
                                    rulePlanning.ItemPlanningId = 0;
                                    await rulePlanning.Update(_backendConfigurationPnDbContext);
                                }

                                if (areaRule.Area.Type == AreaTypesEnum.Type3)
                                {
                                    Property property =
                                        await _backendConfigurationPnDbContext.Properties
                                            .SingleOrDefaultAsync(x => x.Id == areaRule.PropertyId);
                                    string eformName = $"05. Halebid - {property.Name}";
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
                                        foreach (CheckListSite checkListSite in sdkDbContext.CheckListSites.Where(x =>
                                            x.CheckListId == eformId))
                                        {
                                            await core.CaseDelete(checkListSite.MicrotingUid);
                                        }
                                    }

                                    if (areaRule.GroupItemId != 0)
                                    {
                                        await core.EntityItemDelete(areaRule.GroupItemId);
                                        areaRule.GroupItemId = 0;
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
                                    planning.DayOfMonth = (int)areaRulePlanningModel.TypeSpecificFields?.DayOfMonth == 0
                                        ? 1
                                        : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                                    planning.DayOfWeek =
                                        (DayOfWeek) areaRulePlanningModel.TypeSpecificFields?.DayOfWeek == 0 ? DayOfWeek.Friday : (DayOfWeek) areaRulePlanningModel.TypeSpecificFields.DayOfWeek;
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

                                            foreach (PlanningCaseSite planningCaseSite in someList)
                                            {
                                                var microtingCase = await sdkDbContext.Cases
                                                    .SingleOrDefaultAsync(x =>
                                                        x.Id == planningCaseSite.MicrotingSdkCaseId);
                                                await core.CaseDelete((int)microtingCase.MicrotingUid);
                                            }
                                        }
                                    }

                                    foreach (var siteId in sitesForCreate.Select(x => x.SiteId))
                                    {
                                        var planningSite =
                                            new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.PlanningSite
                                            {
                                                SiteId = siteId,
                                                PlanningId = planning.Id,
                                                CreatedByUserId = _userService.UserId,
                                                UpdatedByUserId = _userService.UserId,
                                            };
                                        await planningSite.Create(_itemsPlanningPnDbContext);
                                    }
                                    if (sitesForCreate.Count > 0) {
                                        await _pairItemWichSiteHelper.Pair(
                                            sitesForCreate.Select(x => x.SiteId).ToList(), planning.RelatedEFormId,
                                            planning.Id,
                                            (int)planning.SdkFolderId);
                                    }

                                    await planning.Update(_itemsPlanningPnDbContext);

                                    if (!_itemsPlanningPnDbContext.PlanningSites.Any(x => x.PlanningId == planning.Id && x.WorkflowState != Constants.WorkflowStates.Removed))
                                    {
                                        var compliance = await _backendConfigurationPnDbContext.Compliances
                                            .SingleOrDefaultAsync(x => x.PlanningId == planning.Id);
                                        if (compliance != null)
                                        {
                                            await compliance.Delete(_backendConfigurationPnDbContext);
                                            var property = await _backendConfigurationPnDbContext.Properties.SingleAsync(x => x.Id == compliance.PropertyId);
                                            if (_backendConfigurationPnDbContext.Compliances.Any(x => x.PropertyId == property.Id && x.Deadline < DateTime.UtcNow && x.WorkflowState != Constants.WorkflowStates.Removed))
                                            {
                                                property.ComplianceStatusThirty = 2;
                                                property.ComplianceStatus = 2;
                                            } else
                                            {
                                                if (_backendConfigurationPnDbContext.Compliances.Any(x => x.PropertyId == property.Id && x.WorkflowState != Constants.WorkflowStates.Removed))
                                                {
                                                    property.ComplianceStatusThirty = _backendConfigurationPnDbContext.Compliances.Any(x =>
                                                        x.Deadline < DateTime.UtcNow.AddDays(30) && x.PropertyId == property.Id &&
                                                        x.WorkflowState != Constants.WorkflowStates.Removed) ? 1 : 0;
                                                    property.ComplianceStatus = 1;
                                                }
                                                else
                                                {
                                                    property.ComplianceStatusThirty = 0;
                                                    property.ComplianceStatus = 0;
                                                }
                                            }
                                            property.Update(_backendConfigurationPnDbContext).GetAwaiter().GetResult();
                                        }
                                    }
                                }

                                break;
                            // nothing to do
                            case false when !areaRulePlanningModel.Status:
                                break;
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

        public async Task<OperationDataResult<AreaRulePlanningModel>> GetPlanning(int ruleId)
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

                var areaRulePlanning = await _backendConfigurationPnDbContext.AreaRulePlannings
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Id == areaRule.AreaRulesPlannings.First().Id)
                    .Include(x => x.PlanningSites)
                    .Select(x => new AreaRulePlanningModel
                    {
                        Id = x.Id,
                        RuleId = ruleId,
                        StartDate = (DateTime) x.StartDate,
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
                            .Select(y => new AreaRuleAssignedSitesModel {SiteId = y.SiteId, Checked = true})
                            .ToList()
                    }).FirstOrDefaultAsync();

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
                                    (DayOfWeek) areaRulePlanningModel.TypeSpecificFields.DayOfWeek == 0 ? DayOfWeek.Monday :
                                        (DayOfWeek) areaRulePlanningModel.TypeSpecificFields.DayOfWeek;
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
                        var planningForType2 = await CreateItemPlanningObject((int) areaRule.EformId,
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
                                (DayOfWeek) areaRulePlanningModel.TypeSpecificFields.DayOfWeek == 0 ? DayOfWeek.Monday :
                                    (DayOfWeek) areaRulePlanningModel.TypeSpecificFields.DayOfWeek;
                            planningForType2.DayOfMonth =
                                areaRulePlanningModel.TypeSpecificFields.DayOfMonth == 0 ? 1 : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                        }

                        await planningForType2.Create(_itemsPlanningPnDbContext);
                        await _pairItemWichSiteHelper.Pair(
                            areaRulePlanningModel.AssignedSites.Select(x => x.SiteId).ToList(), (int) areaRule.EformId,
                            planningForType2.Id,
                            folderId);
                        planningForType2Id = planningForType2.Id;
                    }

                    var areaRulePlanningForType2 = CreateAreaRulePlanningObject(areaRulePlanningModel, areaRule,
                        planningForType2Id,
                        folderId);
                    await areaRulePlanningForType2.Create(_backendConfigurationPnDbContext);
                    break;
                }
                case AreaTypesEnum.Type3: // stables and tail bite
                {
                    var planningForType3ChecklistStableId = 0;
                    if (areaRule.ChecklistStable is true)
                    {
                        if (areaRulePlanningModel.Status)
                        {
                            var planningForType3ChecklistStable = await CreateItemPlanningObject((int) areaRule.EformId,
                                areaRule.EformName, areaRule.FolderId, areaRulePlanningModel, areaRule);
                            planningForType3ChecklistStable.NameTranslations = areaRule.AreaRuleTranslations.Select(
                                areaRuleAreaRuleTranslation => new PlanningNameTranslation
                                {
                                    LanguageId = areaRuleAreaRuleTranslation.LanguageId,
                                    Name = areaRuleAreaRuleTranslation.Name,
                                }).ToList();
                            if (areaRulePlanningModel.TypeSpecificFields != null)
                            {
                                if (areaRulePlanningModel.TypeSpecificFields.RepeatType != null)
                                {
                                    planningForType3ChecklistStable.RepeatType =
                                        (RepeatType) areaRulePlanningModel.TypeSpecificFields.RepeatType;
                                }

                                if (areaRulePlanningModel.TypeSpecificFields.RepeatEvery != null)
                                {
                                    planningForType3ChecklistStable.RepeatEvery =
                                        (int) areaRulePlanningModel.TypeSpecificFields.RepeatEvery;
                                }

                                planningForType3ChecklistStable.RepeatUntil =
                                    areaRulePlanningModel.TypeSpecificFields.EndDate;
                                planningForType3ChecklistStable.DayOfWeek =
                                    (DayOfWeek)areaRulePlanningModel.TypeSpecificFields.DayOfWeek == 0
                                        ? DayOfWeek.Monday
                                        : (DayOfWeek)areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                                planningForType3ChecklistStable.DayOfMonth =
                                    areaRulePlanningModel.TypeSpecificFields.DayOfMonth == 0 ? 1 : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                            }

                            await planningForType3ChecklistStable.Create(_itemsPlanningPnDbContext);
                            await _pairItemWichSiteHelper.Pair(
                                areaRulePlanningModel.AssignedSites.Select(x => x.SiteId).ToList(),
                                (int) areaRule.EformId,
                                planningForType3ChecklistStable.Id,
                                areaRule.FolderId);
                            planningForType3ChecklistStableId = planningForType3ChecklistStable.Id;
                        }
                    }

                    var areaRulePlanningForType3ChecklistStable = CreateAreaRulePlanningObject(areaRulePlanningModel,
                        areaRule, planningForType3ChecklistStableId,
                        areaRule.FolderId);

                    await areaRulePlanningForType3ChecklistStable.Create(_backendConfigurationPnDbContext);

                    // var planningForType3TailBiteId = 0;
                    if (areaRule.TailBite is true)
                    {
                        Property property =
                            await _backendConfigurationPnDbContext.Properties.SingleOrDefaultAsync(x =>
                                x.Id == areaRule.PropertyId);
                        string eformName = $"05. Halebid - {property.Name}";
                        var eformId = await sdkDbContext.CheckListTranslations
                            .Where(x => x.Text == eformName)
                            .Select(x => x.CheckListId)
                            .FirstAsync();

                        var areaProperty = await _backendConfigurationPnDbContext.AreaProperties
                            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                            .Where(x => x.AreaId == areaRule.AreaId)
                            .Where(x => x.PropertyId == areaRule.PropertyId)
                            .Select(x => new {x.Area, x.GroupMicrotingUuid, x.PropertyId})
                            .FirstAsync();
                        var entityGroup = await core.EntityGroupRead(areaProperty.GroupMicrotingUuid.ToString());
                        var nextItemUid = entityGroup.EntityGroupItemLst.Count;
                        var entityItem = await core.EntitySelectItemCreate(entityGroup.Id,
                            areaRule.AreaRuleTranslations.First().Name, entityGroup.EntityGroupItemLst.Count,
                            nextItemUid.ToString());
                        areaRule.GroupItemId = entityItem.Id;
                        await areaRule.Update(_backendConfigurationPnDbContext);

                        var sites = areaRulePlanningModel.AssignedSites.Select(x => x.SiteId).ToList();
                        if (areaRulePlanningModel.Status)
                        {
                            foreach (int siteId in sites)
                            {
                                Site site = await sdkDbContext.Sites.SingleOrDefaultAsync(x => x.Id == siteId);
                                Language language =
                                    await sdkDbContext.Languages.SingleOrDefaultAsync(x => x.Id == site.LanguageId);
                                if (!sdkDbContext.CheckListSites.Any(x =>
                                    x.CheckListId == eformId &&
                                    x.SiteId == siteId &&
                                    x.WorkflowState != Constants.WorkflowStates.Removed))
                                {
                                    var mainElement = await core.ReadeForm(eformId, language);

                                    var folder = await sdkDbContext.Folders.SingleAsync(x => x.Id == areaRule.FolderId);
                                    var folderMicrotingId = folder.MicrotingUid.ToString();
                                    mainElement.Repeated = -1;
                                    mainElement.CheckListFolderName = folderMicrotingId;
                                    mainElement.StartDate = DateTime.Now.ToUniversalTime();
                                    mainElement.EndDate = DateTime.Now.AddYears(10).ToUniversalTime();
                                    mainElement.Label =
                                        mainElement.Label.Replace($" - {property.Name}", "");
                                    mainElement.DisplayOrder = 10000000;
                                    /*var caseId = */await core.CaseCreate(mainElement, "", (int) site.MicrotingUid,
                                        folder.Id);
                                }
                            }
                        }
                    }
                    break;
                }
                case AreaTypesEnum.Type5: // recuring tasks(mon-sun)
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
                        var planning = await CreateItemPlanningObject((int) areaRule.EformId, areaRule.EformName,
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
                                    (RepeatType) areaRulePlanningModel.TypeSpecificFields.RepeatType;
                            }

                            if (areaRulePlanningModel.TypeSpecificFields.RepeatEvery != null)
                            {
                                planning.RepeatEvery =
                                    (int) areaRulePlanningModel.TypeSpecificFields.RepeatEvery;
                            }

                            planning.RepeatUntil =
                                areaRulePlanningModel.TypeSpecificFields.EndDate;
                            planning.DayOfWeek =
                                (DayOfWeek) areaRulePlanningModel.TypeSpecificFields.DayOfWeek == 0 ? DayOfWeek.Monday : (DayOfWeek) areaRulePlanningModel.TypeSpecificFields.DayOfWeek;
                            planning.DayOfMonth =
                                areaRulePlanningModel.TypeSpecificFields.DayOfMonth == 0 ? 1 : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                        }

                        await planning.Create(_itemsPlanningPnDbContext);
                        await _pairItemWichSiteHelper.Pair(
                            areaRulePlanningModel.AssignedSites.Select(x => x.SiteId).ToList(), (int) areaRule.EformId,
                            planning.Id,
                            folderId);
                        planningId = planning.Id;
                    }

                    var areaRulePlanning = CreateAreaRulePlanningObject(areaRulePlanningModel, areaRule, planningId,
                        folderId);
                    await areaRulePlanning.Create(_backendConfigurationPnDbContext);

                    break;
                }
                case AreaTypesEnum.Type6: // heat pumps
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
                                (DayOfWeek?) areaRulePlanningModel.TypeSpecificFields.DayOfWeek == 0 ? DayOfWeek.Monday : (DayOfWeek?)  areaRulePlanningModel.TypeSpecificFields.DayOfWeek;
                            planningForType6One.RepeatUntil =
                                areaRulePlanningModel.TypeSpecificFields.EndDate;

                            planningForType6Two.DayOfMonth =
                                areaRulePlanningModel.TypeSpecificFields.DayOfMonth == 0 ? 1 : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                            planningForType6Two.DayOfWeek =
                                (DayOfWeek?) areaRulePlanningModel.TypeSpecificFields.DayOfWeek == 0 ? DayOfWeek.Monday : (DayOfWeek?)  areaRulePlanningModel.TypeSpecificFields.DayOfWeek;
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
                    break;
                }
                default:
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
                        var planning = await CreateItemPlanningObject((int) areaRule.EformId, areaRule.EformName,
                            areaRule.FolderId, areaRulePlanningModel, areaRule);
                        planning.NameTranslations = areaRule.AreaRuleTranslations.Select(
                            areaRuleAreaRuleTranslation => new PlanningNameTranslation
                            {
                                LanguageId = areaRuleAreaRuleTranslation.LanguageId,
                                Name = areaRuleAreaRuleTranslation.Name,
                            }).ToList();
                        if (areaRulePlanningModel.TypeSpecificFields != null)
                        {
                            planning.DayOfMonth = (int) areaRulePlanningModel.TypeSpecificFields?.DayOfMonth == 0
                                ? 1
                                : (int) areaRulePlanningModel.TypeSpecificFields?.DayOfMonth;
                            planning.RepeatUntil = areaRulePlanningModel.TypeSpecificFields?.EndDate;
                            planning.DayOfWeek =
                                (DayOfWeek?) areaRulePlanningModel.TypeSpecificFields?.DayOfWeek == 0
                                    ? DayOfWeek.Monday
                                    : (DayOfWeek?) areaRulePlanningModel.TypeSpecificFields?.DayOfWeek;
                            if (areaRulePlanningModel.TypeSpecificFields.RepeatEvery is not null)
                            {
                                planning.RepeatEvery =
                                    (int) areaRulePlanningModel.TypeSpecificFields.RepeatEvery;
                            }

                            if (areaRulePlanningModel.TypeSpecificFields.RepeatType is not null)
                            {
                                planning.RepeatType =
                                    (RepeatType) areaRulePlanningModel.TypeSpecificFields.RepeatType;
                            }
                        }

                        await planning.Create(_itemsPlanningPnDbContext);
                        await _pairItemWichSiteHelper.Pair(
                            areaRulePlanningModel.AssignedSites.Select(x => x.SiteId).ToList(), (int) areaRule.EformId,
                            planning.Id,
                            areaRule.FolderId);
                        planningId = planning.Id;
                    }

                    var areaRulePlanning = CreateAreaRulePlanningObject(areaRulePlanningModel, areaRule, planningId,
                        areaRule.FolderId);

                    await areaRulePlanning.Create(_backendConfigurationPnDbContext);
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
                        .Where(planningCaseSite => planningCaseSite.MicrotingSdkCaseId != 0)
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .ToListAsync();
                    foreach (var planningCaseSite in planningCaseSites)
                    {
                        var result =
                            await sdkDbContext.Cases.SingleAsync(x => x.Id == planningCaseSite.MicrotingSdkCaseId);
                        if (result.MicrotingUid != null)
                        {
                            await core.CaseDelete((int) result.MicrotingUid);
                        }
                    }

                    // Delete planning case
                    await planningCase.Delete(_itemsPlanningPnDbContext);
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

                if (!_itemsPlanningPnDbContext.PlanningSites.Any(x => x.PlanningId == planning.Id && x.WorkflowState != Constants.WorkflowStates.Removed))
                {
                    var compliance = await _backendConfigurationPnDbContext.Compliances
                        .SingleOrDefaultAsync(x => x.PlanningId == planning.Id);
                    if (compliance != null)
                    {
                        await compliance.Delete(_backendConfigurationPnDbContext);
                    }

                    var property = await _backendConfigurationPnDbContext.Properties.SingleAsync(x => x.Id == compliance.PropertyId);
                    if (_backendConfigurationPnDbContext.Compliances.Any(x => x.PropertyId == property.Id && x.Deadline < DateTime.UtcNow && x.WorkflowState != Constants.WorkflowStates.Removed))
                    {
                        property.ComplianceStatusThirty = 2;
                        property.ComplianceStatus = 2;
                    } else
                    {
                        if (_backendConfigurationPnDbContext.Compliances.Any(x => x.PropertyId == property.Id && x.WorkflowState != Constants.WorkflowStates.Removed))
                        {
                            property.ComplianceStatusThirty = _backendConfigurationPnDbContext.Compliances.Any(x =>
                                x.Deadline < DateTime.UtcNow.AddDays(30) && x.PropertyId == property.Id &&
                                x.WorkflowState != Constants.WorkflowStates.Removed) ? 1 : 0;
                            property.ComplianceStatus = 1;
                        }
                        else
                        {
                            property.ComplianceStatusThirty = 0;
                            property.ComplianceStatus = 0;
                        }
                    }
                    property.Update(_backendConfigurationPnDbContext).GetAwaiter().GetResult();
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
                }).ToList(),
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
                areaRulePlanning.Type = (AreaRuleT2TypesEnum) areaRule.Type;
            }

            if (areaRule.Alarm != null)
            {
                areaRulePlanning.Alarm = (AreaRuleT2AlarmsEnum) areaRule.Alarm;
            }

            return areaRulePlanning;
        }
    }
}