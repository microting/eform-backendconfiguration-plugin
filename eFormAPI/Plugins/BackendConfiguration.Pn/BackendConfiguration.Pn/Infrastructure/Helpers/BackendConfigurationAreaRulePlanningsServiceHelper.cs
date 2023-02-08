using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Enums;
using BackendConfiguration.Pn.Infrastructure.Models.AreaRules;
using eFormCore;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Dto;
using Microting.eForm.Infrastructure;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Models;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.ItemsPlanningBase.Infrastructure.Data;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using PlanningSite = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.PlanningSite;

namespace BackendConfiguration.Pn.Infrastructure.Helpers;

public static class BackendConfigurationAreaRulePlanningsServiceHelper
{
    public static async Task<AreaRulePlanning> CreateAreaRulePlanningObject(AreaRulePlanningModel areaRulePlanningModel,
            AreaRule areaRule, int planningId, int folderId, BackendConfigurationPnDbContext dbContext, int userId)
        {
            var areaRulePlanning = new AreaRulePlanning
            {
                AreaId = areaRule.AreaId,
                CreatedByUserId = userId,
                UpdatedByUserId = userId,
                StartDate = areaRulePlanningModel.StartDate,
                Status = areaRulePlanningModel.Status,
                SendNotifications = areaRulePlanningModel.SendNotifications,
                AreaRuleId = areaRulePlanningModel.RuleId,
                ItemPlanningId = planningId,
                FolderId = folderId,
                PropertyId = areaRulePlanningModel.PropertyId,
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

            await areaRulePlanning.Create(dbContext).ConfigureAwait(false);

            foreach (var site in areaRulePlanningModel.AssignedSites)
            {
                var planningSite = new PlanningSite
                {
                    AreaRulePlanningsId = areaRulePlanning.Id,
                    SiteId = site.SiteId,
                    CreatedByUserId = userId,
                    UpdatedByUserId = userId,
                    AreaId = areaRule.AreaId,
                    AreaRuleId = areaRule.Id,
                    Status = 33
                };
                await planningSite.Create(dbContext).ConfigureAwait(false);
            }

            return areaRulePlanning;
        }

    public static async Task<Planning> CreateItemPlanningObject(int eformId, string eformName, int folderId,
        AreaRulePlanningModel areaRulePlanningModel, AreaRule areaRule, int userId, BackendConfigurationPnDbContext _backendConfigurationPnDbContext)
    {
        var propertyItemPlanningTagId = await _backendConfigurationPnDbContext.Properties
            .Where(x => x.Id == areaRule.PropertyId)
            .Select(x => x.ItemPlanningTagId)
            .FirstAsync().ConfigureAwait(false);
        return new Planning
        {
            CreatedByUserId = userId,
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


    public static async Task CreatePlanningDefaultType(AreaRule areaRule, MicrotingDbContext sdkDbContext,
        AreaRulePlanningModel areaRulePlanningModel, eFormCore.Core core,
        BackendConfigurationPnDbContext backendConfigurationPnDbContext,
        ItemsPlanningPnDbContext itemsPlanningPnDbContext, int userId)
        {
            if (areaRule.FolderId == 0)
            {
                var folderId = await backendConfigurationPnDbContext.ProperyAreaFolders
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
                    await areaRule.Update(backendConfigurationPnDbContext).ConfigureAwait(false);
                }
            }

            var planningId = 0;
            if (areaRulePlanningModel.Status)
            {
                var planning = await CreateItemPlanningObject((int)areaRule.EformId, areaRule.EformName,
                    areaRule.FolderId, areaRulePlanningModel, areaRule, userId, backendConfigurationPnDbContext).ConfigureAwait(false);
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
                            (Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType)areaRulePlanningModel.TypeSpecificFields.RepeatType;
                    }
                }

                if (planning.NameTranslations.Any(x => x.Name == "13. APV Medarbejder"))
                {
                    planning.RepeatEvery = 0;
                    planning.RepeatType = (Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType)RepeatType.Day;
                }

                await planning.Create(itemsPlanningPnDbContext).ConfigureAwait(false);
                await PairItemWithSiteHelper.Pair(
                    areaRulePlanningModel.AssignedSites.Select(x => x.SiteId).ToList(), (int)areaRule.EformId,
                    planning.Id,
                    areaRule.FolderId, core, itemsPlanningPnDbContext).ConfigureAwait(false);
                planningId = planning.Id;
            }

            var areaRulePlanning = await BackendConfigurationAreaRulePlanningsServiceHelper.CreateAreaRulePlanningObject(areaRulePlanningModel, areaRule, planningId,
                areaRule.FolderId, backendConfigurationPnDbContext, userId).ConfigureAwait(false);

            // await areaRulePlanning.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);
        }

        public static async Task CreatePlanningType2(AreaRule areaRule, MicrotingDbContext sdkDbContext, AreaRulePlanningModel areaRulePlanningModel, eFormCore.Core core, int userId, BackendConfigurationPnDbContext _backendConfigurationPnDbContext, ItemsPlanningPnDbContext _itemsPlanningPnDbContext)
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
                        folderId, areaRulePlanningModel, areaRule, userId, _backendConfigurationPnDbContext).ConfigureAwait(false);
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
                    planningForType2TypeTankOpen.RepeatType = (Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType)RepeatType.Month;
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
                    await PairItemWithSiteHelper.Pair(
                        areaRulePlanningModel.AssignedSites.Select(x => x.SiteId).ToList(), eformId,
                        planningForType2TypeTankOpen.Id,
                        folderId, core, _itemsPlanningPnDbContext).ConfigureAwait(false);
                    planningForType2TypeTankOpenId = planningForType2TypeTankOpen.Id;
                }
            }

            var areaRulePlanningForType2TypeTankOpen = await BackendConfigurationAreaRulePlanningsServiceHelper.CreateAreaRulePlanningObject(areaRulePlanningModel,
                areaRule, planningForType2TypeTankOpenId,
                folderId, _backendConfigurationPnDbContext, userId).ConfigureAwait(false);
            // await areaRulePlanningForType2TypeTankOpen.Create().ConfigureAwait(false);

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
                        areaRulePlanningModel, areaRule, userId, _backendConfigurationPnDbContext).ConfigureAwait(false);
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
                    planningForType2AlarmYes.RepeatType = (Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType)RepeatType.Month;
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
                    await PairItemWithSiteHelper.Pair(
                        areaRulePlanningModel.AssignedSites.Select(x => x.SiteId).ToList(), eformId,
                        planningForType2AlarmYes.Id,
                        folderId, core, _itemsPlanningPnDbContext).ConfigureAwait(false);
                    planningForType2AlarmYesId = planningForType2AlarmYes.Id;
                }
            }

            var areaRulePlanningForType2AlarmYes = await BackendConfigurationAreaRulePlanningsServiceHelper.CreateAreaRulePlanningObject(areaRulePlanningModel, areaRule,
                planningForType2AlarmYesId,
                folderId, _backendConfigurationPnDbContext, userId).ConfigureAwait(false);
            // await areaRulePlanningForType2AlarmYes.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);

            var planningForType2Id = 0;
            if (areaRulePlanningModel.Status)
            {
                //areaRule.EformName must be "03. Kontrol konstruktion"
                var planningForType2 = await CreateItemPlanningObject((int)areaRule.EformId,
                    areaRule.EformName, folderId, areaRulePlanningModel, areaRule, userId, _backendConfigurationPnDbContext).ConfigureAwait(false);
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
                planningForType2.RepeatType = (Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType)RepeatType.Month;
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
                await PairItemWithSiteHelper.Pair(
                    areaRulePlanningModel.AssignedSites.Select(x => x.SiteId).ToList(), (int)areaRule.EformId,
                    planningForType2.Id,
                    folderId, core, _itemsPlanningPnDbContext).ConfigureAwait(false);
                planningForType2Id = planningForType2.Id;
            }

            var areaRulePlanningForType2 = await BackendConfigurationAreaRulePlanningsServiceHelper.CreateAreaRulePlanningObject(areaRulePlanningModel, areaRule,
                planningForType2Id,
                folderId, _backendConfigurationPnDbContext, userId).ConfigureAwait(false);
            // await areaRulePlanningForType2.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);
        }

        public static async Task CreatePlanningType3(AreaRule areaRule, MicrotingDbContext sdkDbContext, AreaRulePlanningModel areaRulePlanningModel, eFormCore.Core core, int userId, BackendConfigurationPnDbContext _backendConfigurationPnDbContext, ItemsPlanningPnDbContext _itemsPlanningPnDbContext)
        {
            await BackendConfigurationAreaRulePlanningsServiceHelper.CreatePlanningDefaultType(areaRule, sdkDbContext, areaRulePlanningModel, core, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, userId).ConfigureAwait(false);

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

        public static async Task CreatePlanningType5(AreaRule areaRule, AreaRulePlanningModel areaRulePlanningModel, Core core, int userId, BackendConfigurationPnDbContext _backendConfigurationPnDbContext, ItemsPlanningPnDbContext _itemsPlanningPnDbContext)
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
                    folderId, areaRulePlanningModel, areaRule, userId, _backendConfigurationPnDbContext).ConfigureAwait(false);
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
                            (Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType)areaRulePlanningModel.TypeSpecificFields.RepeatType;
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
                await PairItemWithSiteHelper.Pair(
                    areaRulePlanningModel.AssignedSites.Select(x => x.SiteId).ToList(), (int)areaRule.EformId,
                    planning.Id,
                    folderId, core, _itemsPlanningPnDbContext).ConfigureAwait(false);
                planningId = planning.Id;

            }
            var areaRulePlanning = await BackendConfigurationAreaRulePlanningsServiceHelper.CreateAreaRulePlanningObject(areaRulePlanningModel, areaRule, planningId,
                folderId, _backendConfigurationPnDbContext, userId).ConfigureAwait(false);
            // await areaRulePlanning.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);
        }

        public static async Task CreatePlanningType6(AreaRule areaRule, MicrotingDbContext sdkDbContext, AreaRulePlanningModel areaRulePlanningModel, Core core, int userId, BackendConfigurationPnDbContext _backendConfigurationPnDbContext, ItemsPlanningPnDbContext _itemsPlanningPnDbContext)
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
                    folderId, areaRulePlanningModel, areaRule, userId, _backendConfigurationPnDbContext).ConfigureAwait(false);
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
                planningForType6HoursAndEnergyEnabled.RepeatType = (Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType)RepeatType.Month;
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
                await PairItemWithSiteHelper.Pair(
                    areaRulePlanningModel.AssignedSites.Select(x => x.SiteId).ToList(), eformId,
                    planningForType6HoursAndEnergyEnabled.Id,
                    folderId, core, _itemsPlanningPnDbContext).ConfigureAwait(false);
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
                    areaRulePlanningModel, areaRule, userId, _backendConfigurationPnDbContext).ConfigureAwait(false);
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
                planningForType6One.RepeatType = (Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType)RepeatType.Month;
                var planningForType6Two = await CreateItemPlanningObject(eformIdTwo, eformNameTwo, folderId,
                    areaRulePlanningModel, areaRule, userId, _backendConfigurationPnDbContext).ConfigureAwait(false);
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
                planningForType6Two.RepeatType = (Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType)RepeatType.Day;
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
                await PairItemWithSiteHelper.Pair(
                    areaRulePlanningModel.AssignedSites.Select(x => x.SiteId).ToList(), eformIdOne,
                    planningForType6One.Id,
                    folderId, core, _itemsPlanningPnDbContext).ConfigureAwait(false);
                planningForType6IdOne = planningForType6One.Id;
                await planningForType6Two.Create(_itemsPlanningPnDbContext).ConfigureAwait(false);
                await PairItemWithSiteHelper.Pair(
                    areaRulePlanningModel.AssignedSites.Select(x => x.SiteId).ToList(), eformIdTwo,
                    planningForType6Two.Id,
                    folderId, core, _itemsPlanningPnDbContext).ConfigureAwait(false);
                planningForType6IdTwo = planningForType6Two.Id;
            }

            var areaRulePlanningForType6HoursAndEnergyEnabled = await BackendConfigurationAreaRulePlanningsServiceHelper.CreateAreaRulePlanningObject(
                areaRulePlanningModel, areaRule, planningForType6HoursAndEnergyEnabledId,
                folderId, _backendConfigurationPnDbContext, userId).ConfigureAwait(false);
            var areaRulePlanningForType6One = await BackendConfigurationAreaRulePlanningsServiceHelper.CreateAreaRulePlanningObject(areaRulePlanningModel, areaRule,
                planningForType6IdOne,
                folderId, _backendConfigurationPnDbContext, userId).ConfigureAwait(false);
            var areaRulePlanningForType6Two = await BackendConfigurationAreaRulePlanningsServiceHelper.CreateAreaRulePlanningObject(areaRulePlanningModel, areaRule,
                planningForType6IdTwo,
                folderId, _backendConfigurationPnDbContext, userId).ConfigureAwait(false);
            // await areaRulePlanningForType6HoursAndEnergyEnabled.Create(
                // _backendConfigurationPnDbContext).ConfigureAwait(false);
            // await areaRulePlanningForType6One.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);
            // await areaRulePlanningForType6Two.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);
        }

        public static async Task CreatePlanningType9(AreaRule areaRule, MicrotingDbContext sdkDbContext, AreaRulePlanningModel areaRulePlanningModel, Core core, int userId, BackendConfigurationPnDbContext _backendConfigurationPnDbContext, ItemsPlanningPnDbContext _itemsPlanningPnDbContext)
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
                    areaRule.FolderId, areaRulePlanningModel, areaRule, userId, _backendConfigurationPnDbContext).ConfigureAwait(false);
                planning.RepeatEvery = 0;
                planning.RepeatType = (Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType)RepeatType.Day;
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

                var areaRulePlanning = await BackendConfigurationAreaRulePlanningsServiceHelper.CreateAreaRulePlanningObject(areaRulePlanningModel, areaRule, planning.Id,
                    areaRule.FolderId, _backendConfigurationPnDbContext, userId).ConfigureAwait(false);


                // await areaRulePlanning.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);
                // }
            }
        }

        public static async Task CreatePlanningType10(AreaRule areaRule, MicrotingDbContext sdkDbContext,
            AreaRulePlanningModel areaRulePlanningModel, Core core, int userId, BackendConfigurationPnDbContext _backendConfigurationPnDbContext, ItemsPlanningPnDbContext _itemsPlanningPnDbContext)
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
                            CreatedByUserId = userId,
                            UpdatedByUserId = userId
                        };

                        await globalPlanningTag.Create(_itemsPlanningPnDbContext).ConfigureAwait(false);
                    }

                    var planning = await CreateItemPlanningObject((int) areaRule.EformId, areaRule.EformName,
                        areaRule.FolderId, areaRulePlanningModel, areaRule, userId, _backendConfigurationPnDbContext).ConfigureAwait(false);
                    planning.NameTranslations = areaRule.AreaRuleTranslations.Select(
                        areaRuleAreaRuleTranslation => new PlanningNameTranslation
                        {
                            LanguageId = areaRuleAreaRuleTranslation.LanguageId,
                            Name = areaRuleAreaRuleTranslation.Name,
                        }).ToList();

                    planning.RepeatEvery = 0;
                    planning.RepeatType = (Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType)RepeatType.Day;
                    planning.PlanningsTags.Add(new() {PlanningTagId = globalPlanningTag.Id});

                    await planning.Create(_itemsPlanningPnDbContext).ConfigureAwait(false);
                    await PairItemWithSiteHelper.Pair(
                        areaRulePlanningModel.AssignedSites.Select(x => x.SiteId).ToList(), (int) areaRule.EformId,
                        planning.Id,
                        areaRule.FolderId, core, _itemsPlanningPnDbContext).ConfigureAwait(false);

                    var areaRulePlanning = BackendConfigurationAreaRulePlanningsServiceHelper.CreateAreaRulePlanningObject(areaRulePlanningModel, areaRule, planning.Id,
                        areaRule.FolderId, _backendConfigurationPnDbContext, userId).ConfigureAwait(false);

                    // await areaRulePlanning.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);

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
                        CreatedByUserId = userId,
                        UpdatedByUserId = userId
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
                        CreatedByUserId = userId,
                        UpdatedByUserId = userId,
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
                            poolDayFolder.Id, areaRulePlanningModel, areaRule, userId, _backendConfigurationPnDbContext).ConfigureAwait(false);
                        planningStatic.RepeatEvery = 0;
                        planningStatic.RepeatType = (Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType)RepeatType.Day;
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
                                CreatedByUserId = userId,
                                UpdatedByUserId = userId
                            };

                            await planningTagStatic.Create(_itemsPlanningPnDbContext).ConfigureAwait(false);
                        }

                        planningStatic.PlanningsTags.Add(new() {PlanningTagId = planningTagStatic.Id});
                        planningStatic.PlanningsTags.Add(new() {PlanningTagId = globalPlanningTag1.Id});

                        await planningStatic.Create(_itemsPlanningPnDbContext).ConfigureAwait(false);
                        await PairItemWithSiteHelper.Pair(
                            areaRulePlanningModel.AssignedSites.Select(x => x.SiteId).ToList(), clId, planningStatic.Id,
                            poolDayFolder.Id, core, _itemsPlanningPnDbContext).ConfigureAwait(false);
                        var areaRulePlanningStatic = await BackendConfigurationAreaRulePlanningsServiceHelper.CreateAreaRulePlanningObject(areaRulePlanningModel, areaRule, planningStatic.Id,
                            areaRule.FolderId, _backendConfigurationPnDbContext, userId).ConfigureAwait(false);
                        areaRulePlanningStatic.ComplianceEnabled = false;
                        areaRulePlanningStatic.RepeatEvery = 0;
                        areaRulePlanningStatic.RepeatType = (int)RepeatType.Day;
                        areaRulePlanningStatic.FolderId = poolDayFolder.Id;

                        await areaRulePlanningStatic.Update(_backendConfigurationPnDbContext).ConfigureAwait(false);
                    }

                    currentWeekDay = (DayOfWeek)poolHour.DayOfWeek;

                    if (poolHour.IsActive)
                    {
                        clId = sdkDbContext.CheckListTranslations
                            .Where(x => x.Text == $"01. Aflæsninger - {property.Name}").Select(x => x.CheckListId)
                            .FirstOrDefault();
                        var planning = await CreateItemPlanningObject(clId, $"01. Aflæsninger - {property.Name}",
                            poolDayFolder.Id, areaRulePlanningModel, areaRule, userId, _backendConfigurationPnDbContext).ConfigureAwait(false);
                        planning.DayOfWeek = (DayOfWeek)poolHour.DayOfWeek;
                        planning.RepeatEvery = 1;
                        planning.RepeatType = (Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType)RepeatType.Week;
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

                        var areaRulePlanning = await BackendConfigurationAreaRulePlanningsServiceHelper.CreateAreaRulePlanningObject(areaRulePlanningModel, areaRule,
                            planning.Id,
                            poolDayFolder.Id, _backendConfigurationPnDbContext, userId).ConfigureAwait(false);
                        areaRulePlanning.ComplianceEnabled = false;
                        areaRulePlanning.RepeatEvery = 0;
                        areaRulePlanning.RepeatType = (int)RepeatType.Day;
                        areaRulePlanning.DayOfWeek = (int)(DayOfWeek)poolHour.DayOfWeek;
                        areaRulePlanning.FolderId = poolDayFolder.Id;

                        await areaRulePlanning.Update(_backendConfigurationPnDbContext).ConfigureAwait(false);
                    }
                }
            }
        }

        public static async Task DeleteItemPlanning(int itemPlanningId, Core core, int userId, BackendConfigurationPnDbContext _backendConfigurationPnDbContext, ItemsPlanningPnDbContext _itemsPlanningPnDbContext)
        {
            if (itemPlanningId != 0)
            {
                var planning = await _itemsPlanningPnDbContext.Plannings
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Id == itemPlanningId)
                    .Include(x => x.PlanningSites)
                    .Include(x => x.NameTranslations)
                    .FirstAsync().ConfigureAwait(false);
                planning.UpdatedByUserId = userId;
                foreach (var planningSite in planning.PlanningSites
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed))
                {
                    planningSite.UpdatedByUserId = userId;
                    await planningSite.Delete(_itemsPlanningPnDbContext).ConfigureAwait(false);
                }

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


        private static DateTime GetNextWeekday(DateTime start, DayOfWeek day)
        {
            // The (... + 7) % 7 ensures we end up with a value in the range [0, 6]
            int daysToAdd = ((int) day - (int) start.DayOfWeek + 7) % 7;
            return start.AddDays(daysToAdd);
        }


}