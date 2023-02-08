using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Enums;
using BackendConfiguration.Pn.Infrastructure.Models.AreaRules;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.ItemsPlanningBase.Infrastructure.Data;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using PlanningSite = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.PlanningSite;

namespace BackendConfiguration.Pn.Infrastructure.Helpers;

public static class BackendConfigurationAreaRulePlanningsServiceHelper
{
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

}