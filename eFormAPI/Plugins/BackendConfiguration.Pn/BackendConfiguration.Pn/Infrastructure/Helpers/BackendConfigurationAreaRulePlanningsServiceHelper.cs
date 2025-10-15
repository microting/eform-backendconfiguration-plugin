using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Enums;
using BackendConfiguration.Pn.Infrastructure.Models.AreaRules;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using eFormCore;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Models;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.ItemsPlanningBase.Infrastructure.Data;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using PlanningSite = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.PlanningSite;

namespace BackendConfiguration.Pn.Infrastructure.Helpers;

public static class BackendConfigurationAreaRulePlanningsServiceHelper
{
    public static async Task<OperationResult> UpdatePlanning(AreaRulePlanningModel areaRulePlanningModel,
        Core core,
        int userId,
        BackendConfigurationPnDbContext backendConfigurationPnDbContext,
        ItemsPlanningPnDbContext itemsPlanningPnDbContext,
        IBackendConfigurationLocalizationService? localizationService)
    {
        try
        {
            //var core = await _coreHelper.GetCore().ConfigureAwait(false);
            var sdkDbContext = core.DbContextHelper.GetDbContext();
            areaRulePlanningModel.AssignedSites =
                areaRulePlanningModel.AssignedSites.Where(x => x.Checked).ToList();

            if (areaRulePlanningModel.TypeSpecificFields != null)
            {
                if (areaRulePlanningModel.TypeSpecificFields.RepeatType == 1 &&
                    areaRulePlanningModel.TypeSpecificFields.RepeatEvery == 1)
                {
                    areaRulePlanningModel.TypeSpecificFields.RepeatEvery = 0;
                }
            }

            if (areaRulePlanningModel.Id.HasValue) // update planning
            {
                var areaRule = await backendConfigurationPnDbContext.AreaRules
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Id == areaRulePlanningModel.RuleId)
                    .Include(x => x.AreaRulesPlannings)
                    .ThenInclude(x => x.PlanningSites)
                    .Include(x => x.Area)
                    .Include(x => x.AreaRuleTranslations)
                    .FirstAsync().ConfigureAwait(false);

                var property = await backendConfigurationPnDbContext.Properties
                    .SingleAsync(x => x.Id == areaRule.PropertyId).ConfigureAwait(false);

                switch (areaRule.Area.Type)
                {
                    // Chemical products
                    case AreaTypesEnum.Type9:
                    {
                        var oldStatus = areaRule.AreaRulesPlannings
                            .Last(x => x.WorkflowState != Constants.WorkflowStates.Removed).Status;
                        if (areaRulePlanningModel.Status && !oldStatus)
                        {
                            foreach (AreaRulePlanning areaRuleAreaRulesPlanning in areaRule.AreaRulesPlannings)
                            {
                                await areaRuleAreaRulesPlanning.Delete(backendConfigurationPnDbContext)
                                    .ConfigureAwait(false);
                            }

                            await CreatePlanningType9(areaRule, sdkDbContext, areaRulePlanningModel, core, userId,
                                backendConfigurationPnDbContext, itemsPlanningPnDbContext).ConfigureAwait(false);
                        }

                        if (!areaRulePlanningModel.Status && oldStatus)
                        {
                            var arps = areaRule.AreaRulesPlannings.Join(backendConfigurationPnDbContext.PlanningSites
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
                                var planning = await itemsPlanningPnDbContext.Plannings
                                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                    .Where(x => x.Id == arp.ItemPlanningId)
                                    .Include(x => x.PlanningSites)
                                    .FirstOrDefaultAsync().ConfigureAwait(false);

                                if (planning != null)
                                {
                                    foreach (var planningSite in planning.PlanningSites
                                                 .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed))
                                    {
                                        await planningSite.Delete(itemsPlanningPnDbContext).ConfigureAwait(false);
                                        var someList = await itemsPlanningPnDbContext.PlanningCaseSites
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

                                    await planning.Delete(itemsPlanningPnDbContext).ConfigureAwait(false);
                                }

                                var areaRulePlanning = backendConfigurationPnDbContext.AreaRulePlannings
                                    .Single(x => x.Id == arp.Id);
                                areaRulePlanning.Status = false;
                                await areaRulePlanning.Update(backendConfigurationPnDbContext).ConfigureAwait(false);
                            }

                            foreach (AreaRulePlanning areaRuleAreaRulesPlanning in areaRule.AreaRulesPlannings)
                            {
                                areaRuleAreaRulesPlanning.ItemPlanningId = 0;
                                areaRuleAreaRulesPlanning.Status = false;
                                await areaRuleAreaRulesPlanning.Update(backendConfigurationPnDbContext)
                                    .ConfigureAwait(false);
                            }
                        }

                        break;
                    }
                    //case AreaTypesEnum.Type10:
                    // {
                    //     var oldStatus = areaRule.AreaRulesPlannings
                    //         .LastOrDefault(x => x.WorkflowState != Constants.WorkflowStates.Removed)?.Status ?? false;
                    //     var currentPlanningSites = await backendConfigurationPnDbContext.PlanningSites
                    //         .Where(x => x.AreaRuleId == areaRulePlanningModel.RuleId)
                    //         .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    //         .Select(x => x.SiteId).Distinct()
                    //         .ToListAsync().ConfigureAwait(false);
                    //     var forDelete = currentPlanningSites
                    //         .Except(areaRulePlanningModel.AssignedSites.Select(x => x.SiteId)).ToList();
                    //     var forAdd = areaRulePlanningModel.AssignedSites.Select(x => x.SiteId)
                    //         .Except(currentPlanningSites).ToList();
                    //     if (areaRulePlanningModel.Status && oldStatus)
                    //     {
                    //         var areaRulePlannings = areaRule.AreaRulesPlannings.Join(
                    //             backendConfigurationPnDbContext
                    //                 .PlanningSites
                    //                 .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed),
                    //             arp => arp.Id, planningSite => planningSite.AreaRulePlanningsId,
                    //             (arp, planningSite) =>
                    //                 new
                    //                 {
                    //                     arp.Id,
                    //                     PlanningSiteId = planningSite.Id,
                    //                     planningSite.SiteId,
                    //                     arp.ItemPlanningId
                    //                 }).ToList();
                    //
                    //         foreach (var i in forDelete)
                    //         {
                    //             var planningSiteId = areaRulePlannings.Single(y => y.SiteId == i && y.ItemPlanningId != 0).PlanningSiteId;
                    //             var itemsPlanningId = areaRulePlannings.Single(x => x.SiteId == i && x.ItemPlanningId != 0).ItemPlanningId;
                    //             var backendPlanningSite = await backendConfigurationPnDbContext.PlanningSites
                    //                 .SingleAsync(x => x.Id == planningSiteId).ConfigureAwait(false);
                    //             await backendPlanningSite.Delete(backendConfigurationPnDbContext).ConfigureAwait(false);
                    //             var planning = await itemsPlanningPnDbContext.Plannings
                    //                 .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    //                 .Where(x => x.Id == itemsPlanningId)
                    //                 .Include(x => x.PlanningSites)
                    //                 .FirstOrDefaultAsync().ConfigureAwait(false);
                    //
                    //             if (planning != null)
                    //             {
                    //                 foreach (var planningSite in planning.PlanningSites
                    //                              .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed))
                    //                 {
                    //                     if (forDelete.Contains(planningSite.SiteId))
                    //                     {
                    //                         await planningSite.Delete(itemsPlanningPnDbContext).ConfigureAwait(false);
                    //                         var someList = await itemsPlanningPnDbContext.PlanningCaseSites
                    //                             .Where(x => x.PlanningId == planning.Id)
                    //                             .Where(x => x.MicrotingSdkSiteId == planningSite.SiteId)
                    //                             .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    //                             .ToListAsync().ConfigureAwait(false);
                    //
                    //                         foreach (var planningCaseSite in someList)
                    //                         {
                    //                             var result =
                    //                                 await sdkDbContext.Cases.SingleOrDefaultAsync(x =>
                    //                                         x.Id == planningCaseSite.MicrotingSdkCaseId)
                    //                                     .ConfigureAwait(false);
                    //                             if (result != null)
                    //                             {
                    //                                 await core.CaseDelete((int)result.MicrotingUid)
                    //                                     .ConfigureAwait(false);
                    //                             }
                    //                         }
                    //                     }
                    //                 }
                    //             }
                    //         }
                    //
                    //         foreach (var areaRulePlanning in areaRule.AreaRulesPlannings.Where(x =>
                    //                      x.WorkflowState != Constants.WorkflowStates.Removed))
                    //         {
                    //             var clId = sdkDbContext.CheckListTranslations
                    //                 .Where(x => x.Text == $"02. Fækale uheld - {property.Name}")
                    //                 .Select(x => x.CheckListId).FirstOrDefault();
                    //
                    //             foreach (int i in forAdd)
                    //             {
                    //                 var siteForCreate = new PlanningSite
                    //                 {
                    //                     AreaRulePlanningsId = areaRulePlanning.Id,
                    //                     SiteId = i,
                    //                     CreatedByUserId = userId,
                    //                     UpdatedByUserId = userId,
                    //                     AreaId = areaRule.AreaId,
                    //                     AreaRuleId = areaRule.Id,
                    //                     Status = 33
                    //                 };
                    //                 await siteForCreate.Create(backendConfigurationPnDbContext).ConfigureAwait(false);
                    //                 var planningSite =
                    //                     new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.
                    //                         PlanningSite
                    //                         {
                    //                             SiteId = i,
                    //                             PlanningId = areaRulePlanning.ItemPlanningId,
                    //                             CreatedByUserId = userId,
                    //                             UpdatedByUserId = userId
                    //                         };
                    //                 await planningSite.Create(itemsPlanningPnDbContext).ConfigureAwait(false);
                    //                 var planning = await itemsPlanningPnDbContext.Plannings
                    //                     .Where(x => x.Id == areaRulePlanning.ItemPlanningId)
                    //                     .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    //                     .Include(x => x.NameTranslations)
                    //                     .Select(x => new
                    //                     {
                    //                         x.Id, x.Type, x.PlanningNumber, x.BuildYear, x.StartDate,
                    //                         x.PushMessageOnDeployment,
                    //                         x.SdkFolderId, x.NameTranslations, x.RepeatEvery, x.RepeatType,
                    //                         x.RelatedEFormId
                    //                     })
                    //                     .FirstAsync().ConfigureAwait(false);
                    //
                    //                 if (planning.RelatedEFormId == clId ||
                    //                     areaRule.SecondaryeFormName == "Morgenrundtur")
                    //                 {
                    //                     var sdkSite = await sdkDbContext.Sites.SingleAsync(x => x.Id == i)
                    //                         .ConfigureAwait(false);
                    //                     var language =
                    //                         await sdkDbContext.Languages.SingleAsync(x => x.Id == sdkSite.LanguageId)
                    //                             .ConfigureAwait(false);
                    //                     var mainElement = await core.ReadeForm(planning.RelatedEFormId, language)
                    //                         .ConfigureAwait(false);
                    //                     var translation = planning.NameTranslations
                    //                         .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    //                         .Where(x => x.LanguageId == language.Id)
                    //                         .Select(x => x.Name)
                    //                         .FirstOrDefault();
                    //                     var planningCase = await itemsPlanningPnDbContext.PlanningCases
                    //                         .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    //                         .Where(x => x.WorkflowState != Constants.WorkflowStates.Retracted)
                    //                         .Where(x => x.PlanningId == planning.Id)
                    //                         .Where(x => x.MicrotingSdkeFormId == planning.RelatedEFormId)
                    //                         .FirstOrDefaultAsync().ConfigureAwait(false);
                    //
                    //                     if (planningCase == null)
                    //                     {
                    //                         planningCase = new PlanningCase
                    //                         {
                    //                             PlanningId = planning.Id,
                    //                             Status = 66,
                    //                             MicrotingSdkeFormId = planning.RelatedEFormId,
                    //                             CreatedByUserId = userId
                    //                         };
                    //                         await planningCase.Create(itemsPlanningPnDbContext).ConfigureAwait(false);
                    //                     }
                    //
                    //                     mainElement.Label = string.IsNullOrEmpty(planning.PlanningNumber)
                    //                         ? ""
                    //                         : planning.PlanningNumber;
                    //                     if (!string.IsNullOrEmpty(translation))
                    //                     {
                    //                         mainElement.Label +=
                    //                             string.IsNullOrEmpty(mainElement.Label)
                    //                                 ? $"{translation}"
                    //                                 : $" - {translation}";
                    //                     }
                    //
                    //                     if (!string.IsNullOrEmpty(planning.BuildYear))
                    //                     {
                    //                         mainElement.Label += string.IsNullOrEmpty(mainElement.Label)
                    //                             ? $"{planning.BuildYear}"
                    //                             : $" - {planning.BuildYear}";
                    //                     }
                    //
                    //                     if (!string.IsNullOrEmpty(planning.Type))
                    //                     {
                    //                         mainElement.Label += string.IsNullOrEmpty(mainElement.Label)
                    //                             ? $"{planning.Type}"
                    //                             : $" - {planning.Type}";
                    //                     }
                    //
                    //                     if (mainElement.ElementList.Count == 1)
                    //                     {
                    //                         mainElement.ElementList[0].Label = mainElement.Label;
                    //                     }
                    //
                    //                     var folder =
                    //                         await sdkDbContext.Folders.SingleAsync(x => x.Id == planning.SdkFolderId)
                    //                             .ConfigureAwait(false);
                    //                     var folderMicrotingId = folder.MicrotingUid.ToString();
                    //
                    //                     mainElement.CheckListFolderName = folderMicrotingId;
                    //                     mainElement.StartDate = DateTime.Now.ToUniversalTime();
                    //                     mainElement.EndDate = DateTime.Now.AddYears(10).ToUniversalTime();
                    //                     var planningCaseSite = new PlanningCaseSite
                    //                     {
                    //                         MicrotingSdkSiteId = i,
                    //                         MicrotingSdkeFormId = planning.RelatedEFormId,
                    //                         Status = 66,
                    //                         PlanningId = planning.Id,
                    //                         PlanningCaseId = planningCase.Id,
                    //                         CreatedByUserId = userId
                    //                     };
                    //
                    //                     await planningCaseSite.Create(itemsPlanningPnDbContext).ConfigureAwait(false);
                    //                     mainElement.Repeated = 1;
                    //                     var caseId = await core.CaseCreate(mainElement, "", (int)sdkSite.MicrotingUid,
                    //                         null).ConfigureAwait(false);
                    //                     if (caseId != null)
                    //                     {
                    //                         if (sdkDbContext.Cases.Any(x => x.MicrotingUid == caseId))
                    //                         {
                    //                             planningCaseSite.MicrotingSdkCaseId =
                    //                                 sdkDbContext.Cases.Single(x => x.MicrotingUid == caseId).Id;
                    //                         }
                    //                         else
                    //                         {
                    //                             planningCaseSite.MicrotingCheckListSitId =
                    //                                 sdkDbContext.CheckListSites.Single(x => x.MicrotingUid == caseId)
                    //                                     .Id;
                    //                         }
                    //
                    //                         await planningCaseSite.Update(itemsPlanningPnDbContext)
                    //                             .ConfigureAwait(false);
                    //                     }
                    //                 }
                    //             }
                    //         }
                    //     }
                    //     else
                    //     {
                    //         if (areaRulePlanningModel.Status && !oldStatus)
                    //         {
                    //             foreach (AreaRulePlanning areaRuleAreaRulesPlanning in areaRule.AreaRulesPlannings)
                    //             {
                    //                 await areaRuleAreaRulesPlanning.Delete(backendConfigurationPnDbContext)
                    //                     .ConfigureAwait(false);
                    //             }
                    //
                    //             await CreatePlanningType10(areaRule, sdkDbContext, areaRulePlanningModel, core, userId,
                    //                 backendConfigurationPnDbContext, itemsPlanningPnDbContext, localizationService).ConfigureAwait(false);
                    //         }
                    //
                    //         if (!areaRulePlanningModel.Status && oldStatus)
                    //         {
                    //
                    //             var arps = areaRule.AreaRulesPlannings.Join(backendConfigurationPnDbContext
                    //                     .PlanningSites
                    //                     .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed),
                    //                 arp => arp.Id, planningSite => planningSite.AreaRulePlanningsId,
                    //                 (arp, planningSite) =>
                    //                     new
                    //                     {
                    //                         arp.Id,
                    //                         PlanningSiteId = planningSite.Id,
                    //                         planningSite.SiteId,
                    //                         arp.ItemPlanningId
                    //                     }).ToList();
                    //             foreach (var arp in arps)
                    //             {
                    //                 var planning = await itemsPlanningPnDbContext.Plannings
                    //                     .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    //                     .Where(x => x.Id == arp.ItemPlanningId)
                    //                     .Include(x => x.PlanningSites)
                    //                     .FirstOrDefaultAsync().ConfigureAwait(false);
                    //
                    //                 if (planning != null)
                    //                 {
                    //                     foreach (var planningSite in planning.PlanningSites
                    //                                  .Where(
                    //                                      x => x.WorkflowState != Constants.WorkflowStates.Removed))
                    //                     {
                    //                         await planningSite.Delete(itemsPlanningPnDbContext).ConfigureAwait(false);
                    //                         var someList = await itemsPlanningPnDbContext.PlanningCaseSites
                    //                             .Where(x => x.PlanningId == planning.Id)
                    //                             .Where(x => x.MicrotingSdkSiteId == planningSite.SiteId)
                    //                             .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    //                             .ToListAsync().ConfigureAwait(false);
                    //
                    //                         foreach (var planningCaseSite in someList)
                    //                         {
                    //                             var result =
                    //                                 await sdkDbContext.Cases.SingleOrDefaultAsync(x =>
                    //                                         x.Id == planningCaseSite.MicrotingSdkCaseId)
                    //                                     .ConfigureAwait(false);
                    //                             if (result is { MicrotingUid: { } })
                    //                             {
                    //                                 await core.CaseDelete((int)result.MicrotingUid)
                    //                                     .ConfigureAwait(false);
                    //                             }
                    //                             else
                    //                             {
                    //                                 var clSites = await sdkDbContext.CheckListSites
                    //                                     .SingleOrDefaultAsync(x =>
                    //                                         x.Id == planningCaseSite.MicrotingCheckListSitId)
                    //                                     .ConfigureAwait(false);
                    //
                    //                                 if (clSites != null)
                    //                                 {
                    //                                     await core.CaseDelete(clSites.MicrotingUid)
                    //                                         .ConfigureAwait(false);
                    //                                 }
                    //                             }
                    //                         }
                    //                     }
                    //
                    //                     await planning.Delete(itemsPlanningPnDbContext).ConfigureAwait(false);
                    //                 }
                    //
                    //                 var areaRulePlanning = backendConfigurationPnDbContext.AreaRulePlannings
                    //                     .Single(x => x.Id == arp.Id);
                    //                 areaRulePlanning.Status = false;
                    //                 await areaRulePlanning.Update(backendConfigurationPnDbContext)
                    //                     .ConfigureAwait(false);
                    //             }
                    //
                    //             foreach (AreaRulePlanning areaRuleAreaRulesPlanning in areaRule.AreaRulesPlannings)
                    //             {
                    //                 areaRuleAreaRulesPlanning.ItemPlanningId = 0;
                    //                 areaRuleAreaRulesPlanning.Status = false;
                    //                 await areaRuleAreaRulesPlanning.Update(backendConfigurationPnDbContext)
                    //                     .ConfigureAwait(false);
                    //             }
                    //         }
                    //     }
                    //
                    //     break;
                    // }
                    default:
                    {
                        if (!areaRule.AreaRulesPlannings.Any())
                        {
                            return new OperationDataResult<AreaRulePlanningModel>(false, "ErrorWhileReadPlanning");
                        }

                        for (var i = 0; i < areaRule.AreaRulesPlannings.Count; i++)
                        {
                            var rulePlanning = areaRule.AreaRulesPlannings[i];
                            var oldStatus = rulePlanning.Status;
                            rulePlanning.UpdatedByUserId = userId;
                            rulePlanning.StartDate = new DateTime(areaRulePlanningModel.StartDate.Year, areaRulePlanningModel.StartDate.Month, areaRulePlanningModel.StartDate.Day, 0, 0, 0);
                            rulePlanning.Status = areaRulePlanningModel.Status;
                            rulePlanning.ComplianceEnabled = areaRulePlanningModel.ComplianceEnabled;
                            rulePlanning.SendNotifications = areaRulePlanningModel.SendNotifications;
                            rulePlanning.UseStartDateAsStartOfPeriod = areaRulePlanningModel.UseStartDateAsStartOfPeriod;
                            if (rulePlanning.RepeatType == 1 && rulePlanning.RepeatEvery == 0 &&
                                areaRule.Area.Type != AreaTypesEnum.Type2)
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
                                    CreatedByUserId = userId,
                                    UpdatedByUserId = userId,
                                    Status = 33
                                })
                                .ToList();

                            foreach (var assignedSite in sitesForCreate)
                            {
                                await assignedSite.Create(backendConfigurationPnDbContext).ConfigureAwait(false);
                            }

                            foreach (var siteId in siteIdsForDelete)
                            {
                                foreach (var planningSite in rulePlanning.PlanningSites
                                             .Where(x => x.SiteId == siteId)
                                             .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed))
                                {
                                    await planningSite.Delete(backendConfigurationPnDbContext).ConfigureAwait(false);
                                }

                                // await rulePlanning.PlanningSites
                                // .First(x => x.SiteId == siteId)
                                // .Delete(_backendConfigurationPnDbContext).ConfigureAwait(false);
                            }

                            await rulePlanning.Update(backendConfigurationPnDbContext).ConfigureAwait(false);

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
                                                var eformId = await sdkDbContext.CheckLists
                                                    .Where(x => x.OriginalId == "142142new1")
                                                    .Select(x => x.Id)
                                                    .FirstAsync().ConfigureAwait(false);

                                                var planningTagSlurryTankEnv = await itemsPlanningPnDbContext.PlanningTags.FirstOrDefaultAsync(
                                                    x => x.Name == "Miljøledelse").ConfigureAwait(false);

                                                if (planningTagSlurryTankEnv == null)
                                                {
                                                    planningTagSlurryTankEnv = new PlanningTag
                                                    {
                                                        Name = "Miljøledelse",
                                                        CreatedByUserId = userId,
                                                        UpdatedByUserId = userId
                                                    };
                                                    await planningTagSlurryTankEnv.Create(itemsPlanningPnDbContext).ConfigureAwait(false);
                                                }

                                                var planningTagSlurryTank = await itemsPlanningPnDbContext.PlanningTags.FirstOrDefaultAsync(
                                                    x => x.Name == "Gyllebeholder").ConfigureAwait(false);


                                                if (planningTagSlurryTank == null)
                                                {
                                                    planningTagSlurryTank = new PlanningTag
                                                    {
                                                        Name = "Gyllebeholder",
                                                        CreatedByUserId = userId,
                                                        UpdatedByUserId = userId
                                                    };
                                                    await planningTagSlurryTank.Create(itemsPlanningPnDbContext).ConfigureAwait(false);
                                                }

                                                var planningTagSlurryTankFloatingLayerTag = await itemsPlanningPnDbContext.PlanningTags.FirstOrDefaultAsync(
                                                    x => x.Name == "Flyderlag").ConfigureAwait(false);

                                                if (planningTagSlurryTankFloatingLayerTag == null)
                                                {
                                                    planningTagSlurryTankFloatingLayerTag = new PlanningTag
                                                    {
                                                        Name = "Flyderlag",
                                                        CreatedByUserId = userId,
                                                        UpdatedByUserId = userId
                                                    };
                                                    await planningTagSlurryTankFloatingLayerTag.Create(itemsPlanningPnDbContext).ConfigureAwait(false);
                                                }

                                                var name = areaRule.AreaRuleTranslations
                                                    .Where(x => x.LanguageId == 1)
                                                    .Select(x => x.Name)
                                                    .FirstOrDefault();

                                                var planningTagSlurryTankNameTag = await itemsPlanningPnDbContext.PlanningTags.FirstOrDefaultAsync(
                                                    x => x.Name == name).ConfigureAwait(false);

                                                if (planningTagSlurryTankNameTag == null)
                                                {
                                                    planningTagSlurryTankNameTag = new PlanningTag
                                                    {
                                                        Name = name,
                                                        CreatedByUserId = userId,
                                                        UpdatedByUserId = userId
                                                    };
                                                    await planningTagSlurryTankNameTag.Create(itemsPlanningPnDbContext).ConfigureAwait(false);
                                                }
                                                var planningForType2TypeTankOpen = await CreateItemPlanningObject(
                                                        eformId,
                                                        eformName, areaRule.AreaRulesPlannings[0].FolderId,
                                                        areaRulePlanningModel, areaRule, userId,
                                                        backendConfigurationPnDbContext, itemsPlanningPnDbContext)
                                                    .ConfigureAwait(false);
                                                planningForType2TypeTankOpen.NameTranslations =
                                                [
                                                    new()
                                                    {
                                                        LanguageId = 1, // da
                                                        Name = areaRule.AreaRuleTranslations
                                                            .Where(x => x.LanguageId == 1)
                                                            .Select(x => x.Name)
                                                            .FirstOrDefault() + ": Flydelag"
                                                    },

                                                    new()
                                                    {
                                                        LanguageId = 2, // en
                                                        Name = areaRule.AreaRuleTranslations
                                                            .Where(x => x.LanguageId == 2)
                                                            .Select(x => x.Name)
                                                            .FirstOrDefault() + ": Floating layer"
                                                    },

                                                    new()
                                                    {
                                                        LanguageId = 3, // ge
                                                        Name = areaRule.AreaRuleTranslations
                                                            .Where(x => x.LanguageId == 2)
                                                            .Select(x => x.Name)
                                                            .FirstOrDefault() + ": Schwimmende Ebene"
                                                    }
                                                ];
                                                planningForType2TypeTankOpen.RepeatEvery = 1;
                                                planningForType2TypeTankOpen.RepeatType =
                                                    (Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType)
                                                    RepeatType.Month;
                                                if (areaRulePlanningModel.TypeSpecificFields is not null)
                                                {
                                                    planningForType2TypeTankOpen.RepeatUntil =
                                                        areaRulePlanningModel.TypeSpecificFields.EndDate;
                                                    planningForType2TypeTankOpen.DayOfWeek =
                                                        (DayOfWeek)areaRulePlanningModel.TypeSpecificFields
                                                            .DayOfWeek ==
                                                        0
                                                            ? DayOfWeek.Monday
                                                            : (DayOfWeek)areaRulePlanningModel.TypeSpecificFields
                                                                .DayOfWeek;
                                                    planningForType2TypeTankOpen.DayOfMonth =
                                                        areaRulePlanningModel.TypeSpecificFields.DayOfMonth == 0
                                                            ? 1
                                                            : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                                                }

                                                planningForType2TypeTankOpen.ReportGroupPlanningTagId =
                                                    planningTagSlurryTankNameTag.Id;

                                                await planningForType2TypeTankOpen.Update(
                                                    itemsPlanningPnDbContext).ConfigureAwait(false);

                                                foreach (var planningSite in areaRule.AreaRulesPlannings[0]
                                                             .PlanningSites)
                                                {
                                                    planningSite.Status = 33;
                                                    await planningSite.Update(backendConfigurationPnDbContext)
                                                        .ConfigureAwait(false);
                                                }

                                                await PairItemWithSiteHelper.Pair(
                                                    rulePlanning.PlanningSites.Select(x => x.SiteId).ToList(),
                                                    eformId,
                                                    planningForType2TypeTankOpen.Id,
                                                    areaRule.AreaRulesPlannings[0].FolderId, core,
                                                    itemsPlanningPnDbContext, rulePlanning.UseStartDateAsStartOfPeriod,
                                                    localizationService).ConfigureAwait(false);
                                                areaRule.AreaRulesPlannings[0].DayOfMonth =
                                                    (int)areaRulePlanningModel.TypeSpecificFields?.DayOfMonth == 0
                                                        ? 1
                                                        : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                                                areaRule.AreaRulesPlannings[0].ItemPlanningId =
                                                    planningForType2TypeTankOpen.Id;
                                                areaRule.AreaRulesPlannings[0].Status = true;
                                                await areaRule.AreaRulesPlannings[0]
                                                    .Update(backendConfigurationPnDbContext).ConfigureAwait(false);
                                            }
                                            else
                                            {
                                                if (areaRule.AreaRulesPlannings[0].ItemPlanningId != 0)
                                                {
                                                    await DeleteItemPlanning(areaRule.AreaRulesPlannings[0]
                                                            .ItemPlanningId, core, userId,
                                                        backendConfigurationPnDbContext,
                                                        itemsPlanningPnDbContext).ConfigureAwait(false);
                                                    areaRule.AreaRulesPlannings[0].ItemPlanningId = 0;
                                                    await areaRule.AreaRulesPlannings[0]
                                                        .Update(backendConfigurationPnDbContext).ConfigureAwait(false);
                                                }
                                            }
                                            break;
                                        }
                                        default:
                                        {
                                            if (areaRule.FolderId == 0)
                                            {
                                                var folderId = await backendConfigurationPnDbContext
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
                                                    await areaRule.Update(backendConfigurationPnDbContext)
                                                        .ConfigureAwait(false);
                                                }
                                            }

                                            var planning = await CreateItemPlanningObject((int)areaRule.EformId,
                                                areaRule.EformName, areaRule.FolderId, areaRulePlanningModel,
                                                areaRule, userId, backendConfigurationPnDbContext,
                                                itemsPlanningPnDbContext).ConfigureAwait(false);
                                            planning.NameTranslations = areaRule.AreaRuleTranslations.Select(
                                                areaRuleAreaRuleTranslation => new PlanningNameTranslation
                                                {
                                                    LanguageId = areaRuleAreaRuleTranslation.LanguageId,
                                                    Name = areaRuleAreaRuleTranslation.Name
                                                }).ToList();
                                            if (planning.NameTranslations.Any(x => x.Name == "01. Registrer halebid"))
                                            {
                                                var itemPlanningTag =
                                                    await itemsPlanningPnDbContext.PlanningTags.FirstOrDefaultAsync(x =>
                                                        x.Name == "Halebid");
                                                if (itemPlanningTag != null)
                                                {
                                                    planning.ReportGroupPlanningTagId = itemPlanningTag.Id;
                                                    await planning.Update(itemsPlanningPnDbContext)
                                                        .ConfigureAwait(false);
                                                }
                                            }
                                            if (areaRulePlanningModel.TypeSpecificFields is not null)
                                            {
                                                planning.DayOfMonth =
                                                    areaRulePlanningModel.TypeSpecificFields.DayOfMonth == 0
                                                        ? 1
                                                        : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                                                planning.DayOfWeek =
                                                    (DayOfWeek?)areaRulePlanningModel.TypeSpecificFields
                                                        .DayOfWeek == 0
                                                        ? DayOfWeek.Monday
                                                        : (DayOfWeek?)areaRulePlanningModel.TypeSpecificFields
                                                            .DayOfWeek;
                                                planning.RepeatUntil =
                                                    areaRulePlanningModel.TypeSpecificFields.EndDate;
                                                if (areaRulePlanningModel.TypeSpecificFields
                                                        .RepeatEvery is not null)
                                                {
                                                    planning.RepeatEvery =
                                                        (int)areaRulePlanningModel.TypeSpecificFields.RepeatEvery;
                                                }

                                                if (areaRulePlanningModel.TypeSpecificFields.RepeatType is not null)
                                                {
                                                    planning.RepeatType =
                                                        (Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType)
                                                        areaRulePlanningModel.TypeSpecificFields
                                                            .RepeatType;
                                                }
                                            }

                                            if (planning.NameTranslations.Any(x => x.Name == "13. APV Medarbejder"))
                                            {
                                                planning.RepeatEvery = 0;
                                                planning.RepeatType =
                                                    (Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType)
                                                    RepeatType.Day;
                                            }

                                            foreach (var planningSite in rulePlanning.PlanningSites)
                                            {
                                                if (planningSite.Status == 0)
                                                {
                                                    planningSite.Status = 33;
                                                    await planningSite.Update(backendConfigurationPnDbContext)
                                                        .ConfigureAwait(false);
                                                }
                                            }

                                            await planning.Update(itemsPlanningPnDbContext).ConfigureAwait(false);
                                            await PairItemWithSiteHelper.Pair(
                                                    rulePlanning.PlanningSites
                                                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                                        .Select(x => x.SiteId).ToList(),
                                                    (int)areaRule.EformId,
                                                    planning.Id,
                                                    areaRule.FolderId, core, itemsPlanningPnDbContext, rulePlanning.UseStartDateAsStartOfPeriod, localizationService)
                                                .ConfigureAwait(false);
                                            rulePlanning.ItemPlanningId = planning.Id;
                                            await rulePlanning.Update(backendConfigurationPnDbContext)
                                                .ConfigureAwait(false);
                                            break;
                                        }
                                    }

                                    break;
                                // delete item planning
                                case true when !areaRulePlanningModel.Status:
                                    if (rulePlanning.ItemPlanningId != 0)
                                    {
                                        var complianceList = await backendConfigurationPnDbContext.Compliances
                                            .Where(x => x.PlanningId == rulePlanning.ItemPlanningId
                                                        && x.WorkflowState != Constants.WorkflowStates.Removed)
                                            .ToListAsync().ConfigureAwait(false);
                                        foreach (var compliance in complianceList)
                                        {
                                            if (compliance != null)
                                            {
                                                await compliance.Delete(backendConfigurationPnDbContext)
                                                    .ConfigureAwait(false);
                                            }
                                        }

                                        await DeleteItemPlanning(rulePlanning.ItemPlanningId, core, userId,
                                                backendConfigurationPnDbContext, itemsPlanningPnDbContext)
                                            .ConfigureAwait(false);

                                        rulePlanning.ItemPlanningId = 0;
                                        rulePlanning.Status = false;
                                        await rulePlanning.Update(backendConfigurationPnDbContext)
                                            .ConfigureAwait(false);

                                        var planningSites = await backendConfigurationPnDbContext.PlanningSites
                                            .Where(x => x.AreaRulePlanningsId == rulePlanning.Id)
                                            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                            .ToListAsync().ConfigureAwait(false);

                                        foreach (var planningSite in planningSites) // delete all planning sites
                                        {
                                            planningSite.Status = 0;
                                            await planningSite.Update(backendConfigurationPnDbContext)
                                                .ConfigureAwait(false);
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
                                            await itemsPlanningPnDbContext.Plannings
                                                .FirstAsync(x =>
                                                    x.Id == areaRule.AreaRulesPlannings[0].ItemPlanningId)
                                                .ConfigureAwait(false);
                                        await planningForType6HoursAndEnergyEnabled.Delete(
                                            itemsPlanningPnDbContext).ConfigureAwait(false);
                                        areaRule.AreaRulesPlannings[0].ItemPlanningId = 0;
                                        await areaRule.AreaRulesPlannings[0]
                                            .Update(backendConfigurationPnDbContext).ConfigureAwait(false);
                                        continue;
                                    }

                                    // TODO, this is not possible to do, since the web interface does not allow to update active plannings
                                    if (rulePlanning.ItemPlanningId !=
                                        0) // Since ItemPlanningId is not 0, we already have a planning and therefore just update it
                                    {
                                        var planning = await itemsPlanningPnDbContext.Plannings
                                            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                            .Where(x => x.Id == rulePlanning.ItemPlanningId)
                                            .Include(x => x.PlanningSites)
                                            .FirstAsync().ConfigureAwait(false);
                                        planning.Enabled = areaRulePlanningModel.Status;
                                        planning.PushMessageOnDeployment = areaRulePlanningModel.SendNotifications;
                                        planning.StartDate = new DateTime(areaRulePlanningModel.StartDate.Year, areaRulePlanningModel.StartDate.Month, areaRulePlanningModel.StartDate.Day, 0, 0, 0);
                                        planning.DayOfMonth =
                                            (int)areaRulePlanningModel.TypeSpecificFields?.DayOfMonth == 0
                                                ? 1
                                                : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                                        planning.DayOfWeek =
                                            (DayOfWeek)areaRulePlanningModel.TypeSpecificFields?.DayOfWeek == 0
                                                ? DayOfWeek.Friday
                                                : (DayOfWeek)areaRulePlanningModel.TypeSpecificFields.DayOfWeek;
                                        foreach (var planningSite in planning.PlanningSites
                                                     .Where(
                                                         x => x.WorkflowState != Constants.WorkflowStates.Removed))
                                        {
                                            if (siteIdsForDelete.Contains(planningSite.SiteId))
                                            {
                                                await planningSite.Delete(itemsPlanningPnDbContext)
                                                    .ConfigureAwait(false);
                                                var someList = await itemsPlanningPnDbContext.PlanningCaseSites
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
                                                                x.Id == planningCaseSite.MicrotingSdkCaseId)
                                                            .ConfigureAwait(false);
                                                    if (result is { MicrotingUid: { } })
                                                    {
                                                        await core.CaseDelete((int)result.MicrotingUid)
                                                            .ConfigureAwait(false);
                                                    }
                                                    else
                                                    {
                                                        var clSites = await sdkDbContext.CheckListSites
                                                            .SingleOrDefaultAsync(
                                                                x =>
                                                                    x.Id == planningCaseSite.MicrotingCheckListSitId)
                                                            .ConfigureAwait(false);

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
                                                        CreatedByUserId = userId,
                                                        UpdatedByUserId = userId
                                                    };
                                            await planningSite.Create(itemsPlanningPnDbContext).ConfigureAwait(false);
                                        }

                                        if (sitesForCreate.Count > 0)
                                        {
                                            await PairItemWithSiteHelper.Pair(
                                                    sitesForCreate.Select(x => x.SiteId).ToList(),
                                                    planning.RelatedEFormId,
                                                    planning.Id,
                                                    (int)planning.SdkFolderId, core, itemsPlanningPnDbContext, rulePlanning.UseStartDateAsStartOfPeriod, localizationService)
                                                .ConfigureAwait(false);
                                        }

                                        if (areaRule.Area.Type == AreaTypesEnum.Type3)
                                        {
                                            var tailBiteTag = await itemsPlanningPnDbContext.PlanningTags
                                                .FirstOrDefaultAsync(x => x.Name == "Halebid")
                                                .ConfigureAwait(false);

                                            if (tailBiteTag != null)
                                            {
                                                planning.ReportGroupPlanningTagId = tailBiteTag.Id;
                                                if (tailBiteTag.WorkflowState == Constants.WorkflowStates.Removed)
                                                {
                                                    tailBiteTag.WorkflowState = Constants.WorkflowStates.Created;
                                                    await tailBiteTag.Update(itemsPlanningPnDbContext)
                                                        .ConfigureAwait(false);
                                                }
                                                if (!tailBiteTag.IsLocked) // if tag is not locked, we lock it
                                                {
                                                    tailBiteTag.IsLocked = true;
                                                    await tailBiteTag.Update(itemsPlanningPnDbContext)
                                                        .ConfigureAwait(false);
                                                }
                                            }
                                            else // create tag
                                            {
                                                var newTag = new PlanningTag
                                                {
                                                    Name = "Halebid",
                                                    CreatedByUserId = userId,
                                                    UpdatedByUserId = userId,
                                                    IsLocked = true
                                                };
                                                await newTag.Create(itemsPlanningPnDbContext).ConfigureAwait(false);
                                                planning.ReportGroupPlanningTagId = newTag.Id;
                                            }
                                        }

                                        await planning.Update(itemsPlanningPnDbContext).ConfigureAwait(false);
                                        if (!itemsPlanningPnDbContext.PlanningSites.Any(x =>
                                                x.PlanningId == planning.Id &&
                                                x.WorkflowState != Constants.WorkflowStates.Removed) ||
                                            !rulePlanning.ComplianceEnabled)
                                        {
                                            var complianceList = await backendConfigurationPnDbContext.Compliances
                                                .Where(x => x.PlanningId == planning.Id)
                                                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                                .ToListAsync().ConfigureAwait(false);
                                            foreach (var compliance in complianceList)
                                            {
                                                await compliance.Delete(backendConfigurationPnDbContext)
                                                    .ConfigureAwait(false);
                                                if (backendConfigurationPnDbContext.Compliances.Any(x =>
                                                        x.PropertyId == property.Id &&
                                                        x.Deadline < DateTime.UtcNow &&
                                                        x.WorkflowState != Constants.WorkflowStates.Removed))
                                                {
                                                    property.ComplianceStatusThirty = 2;
                                                    property.ComplianceStatus = 2;
                                                }
                                                else
                                                {
                                                    if (!backendConfigurationPnDbContext.Compliances.Any(x =>
                                                            x.PropertyId == property.Id && x.WorkflowState !=
                                                            Constants.WorkflowStates.Removed))
                                                    {
                                                        property.ComplianceStatusThirty = 0;
                                                        property.ComplianceStatus = 0;
                                                    }
                                                }

                                                property.Update(backendConfigurationPnDbContext).GetAwaiter()
                                                    .GetResult();
                                            }
                                        }
                                    }
                                    else
                                    {
                                        Console.WriteLine("Create planning");
                                    }

                                    break;
                                // nothing to do
                                case false when !areaRulePlanningModel.Status:
                                    break;
                            }
                        }

                        break;
                    }
                }

                return new OperationDataResult<AreaRuleModel>(true, "SuccessfullyUpdatePlanning");
            }

            return await CreatePlanning(areaRulePlanningModel, core, userId, backendConfigurationPnDbContext,
                itemsPlanningPnDbContext, localizationService).ConfigureAwait(false); // create planning
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
            Console.WriteLine(e.StackTrace);
            return new OperationDataResult<AreaRuleModel>(false, "ErrorWhileUpdatePlanning");
        }
    }

    private static async Task<OperationDataResult<AreaRuleModel>> CreatePlanning(
            AreaRulePlanningModel areaRulePlanningModel, Core core, int userId, BackendConfigurationPnDbContext backendConfigurationPnDbContext, ItemsPlanningPnDbContext itemsPlanningPnDbContext,
            IBackendConfigurationLocalizationService? localizationService)
        {
            var sdkDbContext = core.DbContextHelper.GetDbContext();

            var areaRule = await backendConfigurationPnDbContext.AreaRules
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.Id == areaRulePlanningModel.RuleId)
                .Include(x => x.AreaRuleTranslations)
                .Include(x => x.Area)
                .Include(x => x.Property)
                .FirstOrDefaultAsync().ConfigureAwait(false);

            if (areaRule == null)
            {
                return new OperationDataResult<AreaRuleModel>(true,"AreaRuleNotFound");
            }

            switch (areaRule.Area.Type)
            {
                case AreaTypesEnum.Type2: // tanks
                    {
                        await CreatePlanningType2(areaRule, sdkDbContext, areaRulePlanningModel, core, userId, backendConfigurationPnDbContext, itemsPlanningPnDbContext, localizationService).ConfigureAwait(false);
                        break;
                    }
                case AreaTypesEnum.Type3: // stables and tail bite
                    {
                        await CreatePlanningType3(areaRule, sdkDbContext, areaRulePlanningModel, core, userId, backendConfigurationPnDbContext, itemsPlanningPnDbContext, localizationService).ConfigureAwait(false);
                        break;
                    }
                case AreaTypesEnum.Type5: // recuring tasks(mon-sun)
                    {
                        await CreatePlanningType5(areaRule, areaRulePlanningModel, core, userId, backendConfigurationPnDbContext, itemsPlanningPnDbContext, localizationService).ConfigureAwait(false);
                        break;
                    }
                case AreaTypesEnum.Type6: // heat pumps
                    {
                        await CreatePlanningType6(areaRule, sdkDbContext, areaRulePlanningModel, core, userId, backendConfigurationPnDbContext, itemsPlanningPnDbContext, localizationService).ConfigureAwait(false);
                        break;
                    }
                case AreaTypesEnum.Type9: // chemical APV
                    {
                        await CreatePlanningType9(areaRule, sdkDbContext, areaRulePlanningModel, core, userId, backendConfigurationPnDbContext, itemsPlanningPnDbContext).ConfigureAwait(false);
                        break;
                    }
                case AreaTypesEnum.Type10:
                {
                    await CreatePlanningType10(areaRule, sdkDbContext, areaRulePlanningModel, core, userId, backendConfigurationPnDbContext, itemsPlanningPnDbContext, localizationService).ConfigureAwait(false);
                    break;
                }
                default:
                    {
                        await CreatePlanningDefaultType(areaRule, sdkDbContext, areaRulePlanningModel, core, backendConfigurationPnDbContext, itemsPlanningPnDbContext, userId, localizationService).ConfigureAwait(false);
                        break;
                    }
            }

            return new OperationDataResult<AreaRuleModel>(true,"SuccessfullyCreatedPlanning");
        }


    private static async Task<AreaRulePlanning> CreateAreaRulePlanningObject(
        AreaRulePlanningModel areaRulePlanningModel,
        AreaRule areaRule, int planningId, int folderId, BackendConfigurationPnDbContext dbContext, int userId)
    {
        var areaRulePlanning = new AreaRulePlanning
        {
            AreaId = areaRule.AreaId,
            CreatedByUserId = userId,
            UpdatedByUserId = userId,
            StartDate = new DateTime(areaRulePlanningModel.StartDate.Year, areaRulePlanningModel.StartDate.Month, areaRulePlanningModel.StartDate.Day, 0, 0, 0),
            Status = areaRulePlanningModel.Status,
            SendNotifications = areaRulePlanningModel.SendNotifications,
            AreaRuleId = areaRulePlanningModel.RuleId,
            ItemPlanningId = planningId,
            FolderId = folderId,
            PropertyId = areaRulePlanningModel.PropertyId,
            ComplianceEnabled = areaRulePlanningModel.ComplianceEnabled
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
            areaRulePlanning.UseStartDateAsStartOfPeriod = areaRulePlanningModel.UseStartDateAsStartOfPeriod;
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
        AreaRulePlanningModel areaRulePlanningModel, AreaRule areaRule, int userId,
        BackendConfigurationPnDbContext backendConfigurationPnDbContext, ItemsPlanningPnDbContext itemsPlanningPnDbContext)
    {
        var propertyItemPlanningTagId = await backendConfigurationPnDbContext.Properties
            .Where(x => x.Id == areaRule.PropertyId)
            .Select(x => x.ItemPlanningTagId)
            .FirstAsync().ConfigureAwait(false);
        var planning = new Planning
        {
            CreatedByUserId = userId,
            Enabled = areaRulePlanningModel.Status,
            RelatedEFormId = eformId,
            RelatedEFormName = eformName,
            SdkFolderId = folderId,
            DaysBeforeRedeploymentPushMessageRepeat = false,
            DaysBeforeRedeploymentPushMessage = 5,
            PushMessageOnDeployment = areaRulePlanningModel.SendNotifications,
            StartDate = new DateTime(areaRulePlanningModel.StartDate.Year, areaRulePlanningModel.StartDate.Month, areaRulePlanningModel.StartDate.Day, 0, 0, 0),
            IsLocked = true,
            IsEditable = false,
            IsHidden = true
        };

        await planning.Create(itemsPlanningPnDbContext).ConfigureAwait(false);

        foreach (var assignedSite in areaRulePlanningModel.AssignedSites)
        {
            var planningSite = new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.PlanningSite
            {
                SiteId = assignedSite.SiteId,
                PlanningId = planning.Id,
                CreatedByUserId = userId,
                UpdatedByUserId = userId
            };
            await planningSite.Create(itemsPlanningPnDbContext).ConfigureAwait(false);
        }

        if (areaRule.Area.ItemPlanningTagId != 0)
        {
            var planningsTags = new PlanningsTags
            {
                PlanningId = planning.Id,
                PlanningTagId = areaRule.Area.ItemPlanningTagId,
                CreatedByUserId = userId,
                UpdatedByUserId = userId
            };
            await planningsTags.Create(itemsPlanningPnDbContext).ConfigureAwait(false);
        }

        var planningsTags2 = new PlanningsTags
        {
            PlanningId = planning.Id,
            PlanningTagId = propertyItemPlanningTagId,
            CreatedByUserId = userId,
            UpdatedByUserId = userId
        };
        await planningsTags2.Create(itemsPlanningPnDbContext).ConfigureAwait(false);

        // PlanningSites = areaRulePlanningModel.AssignedSites
        //     .Select(x =>
        //         new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.PlanningSite
        //         {
        //             SiteId = x.SiteId
        //         })
        //     .ToList(),
        // PlanningsTags = new List<PlanningsTags>
        // {
        //     new() { PlanningTagId = areaRule.Area.ItemPlanningTagId },
        //     new() { PlanningTagId = propertyItemPlanningTagId }
        // }

        return planning;
    }

    public static async Task CreatePlanningDefaultType(AreaRule areaRule, MicrotingDbContext sdkDbContext,
        AreaRulePlanningModel areaRulePlanningModel, Core core,
        BackendConfigurationPnDbContext backendConfigurationPnDbContext,
        ItemsPlanningPnDbContext itemsPlanningPnDbContext, int userId,
        IBackendConfigurationLocalizationService? localizationService)
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
            var planning = await CreateItemPlanningObject((int)areaRule.EformId!, areaRule.EformName,
                    areaRule.FolderId, areaRulePlanningModel, areaRule, userId, backendConfigurationPnDbContext, itemsPlanningPnDbContext)
                .ConfigureAwait(false);
            planning.NameTranslations = areaRule.AreaRuleTranslations.Select(
                areaRuleAreaRuleTranslation => new PlanningNameTranslation
                {
                    LanguageId = areaRuleAreaRuleTranslation.LanguageId,
                    Name = areaRuleAreaRuleTranslation.Name
                }).ToList();
            if (areaRulePlanningModel.TypeSpecificFields != null)
            {
                planning.DayOfMonth = (int)areaRulePlanningModel.TypeSpecificFields?.DayOfMonth! == 0
                    ? 1
                    : (int)areaRulePlanningModel.TypeSpecificFields?.DayOfMonth!;
                planning.RepeatUntil = areaRulePlanningModel.TypeSpecificFields?.EndDate;
                planning.DayOfWeek =
                    (DayOfWeek?)areaRulePlanningModel.TypeSpecificFields?.DayOfWeek == 0
                        ? DayOfWeek.Monday
                        : (DayOfWeek?)areaRulePlanningModel.TypeSpecificFields?.DayOfWeek;
                if (areaRulePlanningModel.TypeSpecificFields!.RepeatEvery is not null)
                {
                    planning.RepeatEvery =
                        (int)areaRulePlanningModel.TypeSpecificFields.RepeatEvery;
                }

                if (areaRulePlanningModel.TypeSpecificFields.RepeatType is not null)
                {
                    planning.RepeatType =
                        (Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType)areaRulePlanningModel
                            .TypeSpecificFields.RepeatType;
                }
            }

            if (planning.NameTranslations.Any(x => x.Name == "13. APV Medarbejder"))
            {
                planning.RepeatEvery = 0;
                planning.RepeatType = (Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType)RepeatType.Day;
            }

            await planning.Update(itemsPlanningPnDbContext).ConfigureAwait(false);
            await PairItemWithSiteHelper.Pair(
                areaRulePlanningModel.AssignedSites.Select(x => x.SiteId).ToList(), (int)areaRule.EformId,
                planning.Id,
                areaRule.FolderId, core, itemsPlanningPnDbContext, areaRulePlanningModel.UseStartDateAsStartOfPeriod, localizationService).ConfigureAwait(false);
            planningId = planning.Id;
        }

        await CreateAreaRulePlanningObject(areaRulePlanningModel, areaRule, planningId,
            areaRule.FolderId, backendConfigurationPnDbContext, userId).ConfigureAwait(false);

        // await areaRulePlanning.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);
    }

    public static async Task CreatePlanningType2(AreaRule areaRule, MicrotingDbContext sdkDbContext,
        AreaRulePlanningModel areaRulePlanningModel, Core core, int userId,
        BackendConfigurationPnDbContext backendConfigurationPnDbContext,
        ItemsPlanningPnDbContext itemsPlanningPnDbContext,
        IBackendConfigurationLocalizationService? localizationService)
    {
        var translatesForFolder = areaRule.AreaRuleTranslations
            .Select(x => new CommonTranslationsModel
            {
                LanguageId = x.LanguageId,
                Description = "",
                Name = x.Name
            }).ToList();
        var danishLanguage = await sdkDbContext.Languages.FirstAsync(x => x.LanguageCode == "da");
        var englishLanguage = await sdkDbContext.Languages.FirstAsync(x => x.LanguageCode == "en-US");
        var germanLanguage = await sdkDbContext.Languages.FirstAsync(x => x.LanguageCode == "de-DE");
        // create folder with name tank
        var folderId = await core.FolderCreate(translatesForFolder, areaRule.FolderId).ConfigureAwait(false);
        var planningForType2TypeTankOpenId = 0;
        var propertyItemPlanningTagId = await backendConfigurationPnDbContext.Properties
            .Where(x => x.Id == areaRule.PropertyId)
            .Select(x => x.ItemPlanningTagId)
            .FirstAsync().ConfigureAwait(false);
        // if (areaRule.Type == AreaRuleT2TypesEnum.Open)
        // {
            const string eformName = "Kontrol flydelag";
            var eformId = await sdkDbContext.CheckLists
                .Where(x => x.OriginalId == "142142new1")
                .Select(x => x.Id)
                .FirstAsync().ConfigureAwait(false);

            var planningTagSlurryTankEnv = await itemsPlanningPnDbContext.PlanningTags.FirstOrDefaultAsync(
                x => x.Name == "Miljøledelse").ConfigureAwait(false);

            if (planningTagSlurryTankEnv == null)
            {
                planningTagSlurryTankEnv = new PlanningTag
                {
                    Name = "Miljøledelse",
                    CreatedByUserId = userId,
                    UpdatedByUserId = userId
                };
                await planningTagSlurryTankEnv.Create(itemsPlanningPnDbContext).ConfigureAwait(false);
            }

            var planningTagSlurryTank = await itemsPlanningPnDbContext.PlanningTags.FirstOrDefaultAsync(
                x => x.Name == "Gyllebeholder").ConfigureAwait(false);


            if (planningTagSlurryTank == null)
            {
                planningTagSlurryTank = new PlanningTag
                {
                    Name = "Gyllebeholder",
                    CreatedByUserId = userId,
                    UpdatedByUserId = userId
                };
                await planningTagSlurryTank.Create(itemsPlanningPnDbContext).ConfigureAwait(false);
            }

            var planningTagSlurryTankFloatingLayerTag = await itemsPlanningPnDbContext.PlanningTags.FirstOrDefaultAsync(
                x => x.Name == "Flyderlag").ConfigureAwait(false);

            if (planningTagSlurryTankFloatingLayerTag == null)
            {
                planningTagSlurryTankFloatingLayerTag = new PlanningTag
                {
                    Name = "Flyderlag",
                    CreatedByUserId = userId,
                    UpdatedByUserId = userId
                };
                await planningTagSlurryTankFloatingLayerTag.Create(itemsPlanningPnDbContext).ConfigureAwait(false);
            }

            var name = areaRule.AreaRuleTranslations
                .Where(x => x.LanguageId == 1)
                .Select(x => x.Name)
                .FirstOrDefault();

            var planningTagSlurryTankNameTag = await itemsPlanningPnDbContext.PlanningTags.FirstOrDefaultAsync(
                x => x.Name == name).ConfigureAwait(false);

            if (planningTagSlurryTankNameTag == null)
            {
                planningTagSlurryTankNameTag = new PlanningTag
                {
                    Name = name,
                    CreatedByUserId = userId,
                    UpdatedByUserId = userId
                };
                await planningTagSlurryTankNameTag.Create(itemsPlanningPnDbContext).ConfigureAwait(false);
            }

            if (areaRulePlanningModel.Status)
            {
                var planningForType2TypeTankOpen = await CreateItemPlanningObject(eformId, eformName,
                        folderId, areaRulePlanningModel, areaRule, userId, backendConfigurationPnDbContext,
                        itemsPlanningPnDbContext)
                    .ConfigureAwait(false);
                planningForType2TypeTankOpen.NameTranslations =
                [
                    new()
                    {
                        LanguageId = danishLanguage.Id, // da
                        Name = areaRule.AreaRuleTranslations
                            .Where(x => x.LanguageId == 1)
                            .Select(x => x.Name)
                            .FirstOrDefault() + ": Flydelag"
                    },

                    new()
                    {
                        LanguageId = englishLanguage.Id, // en
                        Name = areaRule.AreaRuleTranslations
                            .Where(x => x.LanguageId == 2)
                            .Select(x => x.Name)
                            .FirstOrDefault() + ": Floating layer"
                    },

                    new()
                    {
                        LanguageId = germanLanguage.Id, // ge
                        Name = areaRule.AreaRuleTranslations
                            .Where(x => x.LanguageId == 3)
                            .Select(x => x.Name)
                            .FirstOrDefault() + ": Schwimmende Ebene"
                    }
                ];
                planningForType2TypeTankOpen.RepeatEvery = 1;
                planningForType2TypeTankOpen.RepeatType =
                    (Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType)RepeatType.Month;
                if (areaRulePlanningModel.TypeSpecificFields is not null)
                {
                    planningForType2TypeTankOpen.RepeatUntil =
                        areaRulePlanningModel.TypeSpecificFields.EndDate;
                    planningForType2TypeTankOpen.DayOfWeek =
                        (DayOfWeek)areaRulePlanningModel.TypeSpecificFields.DayOfWeek == 0
                            ? DayOfWeek.Monday
                            : (DayOfWeek)areaRulePlanningModel.TypeSpecificFields.DayOfWeek;
                    planningForType2TypeTankOpen.DayOfMonth =
                        areaRulePlanningModel.TypeSpecificFields.DayOfMonth == 0
                            ? 1
                            : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                }

                planningForType2TypeTankOpen.ReportGroupPlanningTagId = planningTagSlurryTankNameTag.Id;


                var planningsTag = new PlanningsTags()
                {
                    PlanningId = planningForType2TypeTankOpen.Id,
                    PlanningTagId = planningTagSlurryTank.Id
                };

                await planningsTag.Create(itemsPlanningPnDbContext)
                    .ConfigureAwait(false);

                await planningForType2TypeTankOpen.Update(itemsPlanningPnDbContext).ConfigureAwait(false);

                planningForType2TypeTankOpenId = planningForType2TypeTankOpen.Id;

                var areaRulePlanning = await CreateAreaRulePlanningObject(areaRulePlanningModel,
                    areaRule, planningForType2TypeTankOpenId,
                    folderId, backendConfigurationPnDbContext, userId).ConfigureAwait(false);

                var areaRulePlanningTag = new AreaRulePlanningTag
                {
                    ItemPlanningTagId = planningTagSlurryTankEnv.Id,
                    AreaRulePlanningId = areaRulePlanning.Id
                };

                await areaRulePlanningTag.Create(backendConfigurationPnDbContext)
                    .ConfigureAwait(false);

                areaRulePlanningTag = new AreaRulePlanningTag
                {
                    ItemPlanningTagId = planningTagSlurryTank.Id,
                    AreaRulePlanningId = areaRulePlanning.Id
                };

                await areaRulePlanningTag.Create(backendConfigurationPnDbContext)
                    .ConfigureAwait(false);

                areaRulePlanningTag = new AreaRulePlanningTag
                {
                    ItemPlanningTagId = planningTagSlurryTankFloatingLayerTag.Id,
                    AreaRulePlanningId = areaRulePlanning.Id
                };

                await areaRulePlanningTag.Create(backendConfigurationPnDbContext)
                    .ConfigureAwait(false);

                areaRulePlanningTag = new AreaRulePlanningTag
                {
                    ItemPlanningTagId = planningTagSlurryTankNameTag.Id,
                    AreaRulePlanningId = areaRulePlanning.Id
                };

                await areaRulePlanningTag.Create(backendConfigurationPnDbContext)
                    .ConfigureAwait(false);

                areaRulePlanningTag = new AreaRulePlanningTag
                {
                    ItemPlanningTagId = propertyItemPlanningTagId,
                    AreaRulePlanningId = areaRulePlanning.Id
                };

                await areaRulePlanningTag.Create(backendConfigurationPnDbContext)
                    .ConfigureAwait(false);

                areaRulePlanning.ItemPlanningTagId = planningTagSlurryTankNameTag.Id;
                await areaRulePlanning.Update(backendConfigurationPnDbContext)
                    .ConfigureAwait(false);

                await PairItemWithSiteHelper.Pair(
                    areaRulePlanningModel.AssignedSites.Select(x => x.SiteId).ToList(), eformId,
                    planningForType2TypeTankOpen.Id,
                    folderId, core, itemsPlanningPnDbContext, areaRulePlanningModel.UseStartDateAsStartOfPeriod,
                    localizationService).ConfigureAwait(false);
            }
            //}


        // var planningForType2AlarmYesId = 0;
        // if (areaRule.Type is AreaRuleT2TypesEnum.Open or AreaRuleT2TypesEnum.Closed
        //     && areaRule.Alarm is AreaRuleT2AlarmsEnum.Yes)
        // {
        //     const string eformName = "03. Kontrol alarmanlæg gyllebeholder";
        //     var eformId = await sdkDbContext.CheckListTranslations
        //         .Where(x => x.Text == eformName)
        //         .Select(x => x.CheckListId)
        //         .FirstAsync().ConfigureAwait(false);
        //
        //     if (areaRulePlanningModel.Status)
        //     {
        //         var planningForType2AlarmYes = await CreateItemPlanningObject(eformId, eformName, folderId,
        //             areaRulePlanningModel, areaRule, userId, backendConfigurationPnDbContext, itemsPlanningPnDbContext).ConfigureAwait(false);
        //         planningForType2AlarmYes.NameTranslations =
        //             new List<PlanningNameTranslation>
        //             {
        //                 new()
        //                 {
        //                     LanguageId = danishLanguage.Id, // da
        //                     Name = areaRule.AreaRuleTranslations
        //                         .Where(x => x.LanguageId == 1)
        //                         .Select(x => x.Name)
        //                         .FirstOrDefault() + ": Alarm"
        //                 },
        //                 new()
        //                 {
        //                     LanguageId = englishLanguage.Id, // en
        //                     Name = areaRule.AreaRuleTranslations
        //                         .Where(x => x.LanguageId == 2)
        //                         .Select(x => x.Name)
        //                         .FirstOrDefault() + ": Alarm"
        //                 },
        //                 new()
        //                 {
        //                     LanguageId = germanLanguage.Id, // ge
        //                     Name = areaRule.AreaRuleTranslations
        //                         .Where(x => x.LanguageId == 3)
        //                         .Select(x => x.Name)
        //                         .FirstOrDefault() + ": Alarm"
        //                 }
        //                 // new ()
        //                 // {
        //                 //     LanguageId = 4,// uk-ua
        //                 //     Name = areaRule.AreaRuleTranslations
        //                 //        .Where(x => x.LanguageId == 4)
        //                 //        .Select(x => x.Name)
        //                 //        .FirstOrDefault() + "Перевірте сигналізацію",
        //                 // },
        //             };
        //         planningForType2AlarmYes.RepeatEvery = 1;
        //         planningForType2AlarmYes.RepeatType =
        //             (Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType)RepeatType.Month;
        //         if (areaRulePlanningModel.TypeSpecificFields != null)
        //         {
        //             planningForType2AlarmYes.RepeatUntil =
        //                 areaRulePlanningModel.TypeSpecificFields.EndDate;
        //             planningForType2AlarmYes.DayOfWeek =
        //                 (DayOfWeek)areaRulePlanningModel.TypeSpecificFields.DayOfWeek == 0
        //                     ? DayOfWeek.Monday
        //                     : (DayOfWeek)areaRulePlanningModel.TypeSpecificFields.DayOfWeek;
        //             planningForType2AlarmYes.DayOfMonth =
        //                 areaRulePlanningModel.TypeSpecificFields.DayOfMonth == 0
        //                     ? 1
        //                     : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
        //         }
        //
        //         await planningForType2AlarmYes.Update(itemsPlanningPnDbContext).ConfigureAwait(false);
        //         await PairItemWithSiteHelper.Pair(
        //             areaRulePlanningModel.AssignedSites.Select(x => x.SiteId).ToList(), eformId,
        //             planningForType2AlarmYes.Id,
        //             folderId, core, itemsPlanningPnDbContext, areaRulePlanningModel.UseStartDateAsStartOfPeriod).ConfigureAwait(false);
        //         planningForType2AlarmYesId = planningForType2AlarmYes.Id;
        //     }
        // }
        //
        // await CreateAreaRulePlanningObject(areaRulePlanningModel, areaRule,
        //     planningForType2AlarmYesId,
        //     folderId, backendConfigurationPnDbContext, userId).ConfigureAwait(false);
        // await areaRulePlanningForType2AlarmYes.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);

        //var planningForType2Id = 0;
        // if (areaRulePlanningModel.Status)
        // {
        //     //areaRule.EformName must be "03. Kontrol konstruktion"
        //     var planningForType2 = await CreateItemPlanningObject((int)areaRule.EformId!,
        //             areaRule.EformName, folderId, areaRulePlanningModel, areaRule, userId,
        //             backendConfigurationPnDbContext, itemsPlanningPnDbContext)
        //         .ConfigureAwait(false);
        //     planningForType2.NameTranslations = new List<PlanningNameTranslation>
        //     {
        //         new()
        //         {
        //             LanguageId = danishLanguage.Id, // da
        //             Name = areaRule.AreaRuleTranslations
        //                 .Where(x => x.LanguageId == 1)
        //                 .Select(x => x.Name)
        //                 .FirstOrDefault() + ": Konstruktion"
        //         },
        //         new()
        //         {
        //             LanguageId = englishLanguage.Id, // en
        //             Name = areaRule.AreaRuleTranslations
        //                 .Where(x => x.LanguageId == 2)
        //                 .Select(x => x.Name)
        //                 .FirstOrDefault() + ": Construction"
        //         },
        //         new()
        //         {
        //             LanguageId = germanLanguage.Id, // ge
        //             Name = areaRule.AreaRuleTranslations
        //                 .Where(x => x.LanguageId == 3)
        //                 .Select(x => x.Name)
        //                 .FirstOrDefault() + ": Konstruktion"
        //         }
        //         // new PlanningNameTranslation
        //         // {
        //         //     LanguageId = 4,// uk-ua
        //         //     Name = areaRule.AreaRuleTranslations
        //         //      .Where(x => x.LanguageId == 4)
        //         //      .Select(x => x.Name)
        //         //      .FirstOrDefault() + "Перевірте конструкцію",
        //         // },
        //     };
        //     planningForType2.RepeatEvery = 12;
        //     planningForType2.RepeatType = (Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType)RepeatType.Month;
        //     if (areaRulePlanningModel.TypeSpecificFields != null)
        //     {
        //         planningForType2.RepeatUntil =
        //             areaRulePlanningModel.TypeSpecificFields.EndDate;
        //         planningForType2.DayOfWeek =
        //             (DayOfWeek)areaRulePlanningModel.TypeSpecificFields.DayOfWeek == 0
        //                 ? DayOfWeek.Monday
        //                 : (DayOfWeek)areaRulePlanningModel.TypeSpecificFields.DayOfWeek;
        //         planningForType2.DayOfMonth =
        //             areaRulePlanningModel.TypeSpecificFields.DayOfMonth == 0
        //                 ? 1
        //                 : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
        //     }
        //
        //     await planningForType2.Update(itemsPlanningPnDbContext).ConfigureAwait(false);
        //     await PairItemWithSiteHelper.Pair(
        //         areaRulePlanningModel.AssignedSites.Select(x => x.SiteId).ToList(), (int)areaRule.EformId,
        //         planningForType2.Id,
        //         folderId, core, itemsPlanningPnDbContext, areaRulePlanningModel.UseStartDateAsStartOfPeriod).ConfigureAwait(false);
        //     planningForType2Id = planningForType2.Id;
        // }
        //
        // await CreateAreaRulePlanningObject(areaRulePlanningModel, areaRule,
        //     planningForType2Id,
        //     folderId, backendConfigurationPnDbContext, userId).ConfigureAwait(false);
    }

    public static async Task CreatePlanningType3(AreaRule areaRule, MicrotingDbContext sdkDbContext,
        AreaRulePlanningModel areaRulePlanningModel, Core core, int userId,
        BackendConfigurationPnDbContext backendConfigurationPnDbContext,
        ItemsPlanningPnDbContext itemsPlanningPnDbContext,
        IBackendConfigurationLocalizationService? localizationService)
    {
        await CreatePlanningDefaultType(areaRule, sdkDbContext, areaRulePlanningModel, core,
            backendConfigurationPnDbContext, itemsPlanningPnDbContext, userId, localizationService).ConfigureAwait(false);

        var sites = areaRulePlanningModel.AssignedSites.Select(x => x.SiteId).ToList();
        if (areaRulePlanningModel.Status)
        {
            foreach (var siteId in sites)
            {
                var site = await sdkDbContext.Sites.SingleOrDefaultAsync(x => x.Id == siteId).ConfigureAwait(false);
                var language =
                    await sdkDbContext.Languages.SingleOrDefaultAsync(x => x.Id == site.LanguageId)
                        .ConfigureAwait(false);
                var entityListUid = await backendConfigurationPnDbContext.AreaProperties
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
                    var mainElement = await core.ReadeForm((int)areaRule.EformId!, language).ConfigureAwait(false);
                    // todo add group id to eform
                    var folder = await sdkDbContext.Folders.SingleAsync(x => x.Id == areaRule.FolderId)
                        .ConfigureAwait(false);
                    var folderMicrotingId = folder.MicrotingUid.ToString();
                    mainElement.Repeated = -1;
                    mainElement.CheckListFolderName = folderMicrotingId;
                    mainElement.StartDate = DateTime.Now.ToUniversalTime();
                    mainElement.EndDate = DateTime.Now.AddYears(10).ToUniversalTime();
                    mainElement.DisplayOrder = 10000000;
                    ((EntitySelect)((DataElement)mainElement.ElementList[0]).DataItemList[1]).Source = entityListUid;
                    /*var caseId = */
                    await core.CaseCreate(mainElement, "", (int)site.MicrotingUid!, folder.Id).ConfigureAwait(false);
                }
            }
        }
    }

    public static async Task CreatePlanningType5(AreaRule areaRule, AreaRulePlanningModel areaRulePlanningModel,
        Core core, int userId, BackendConfigurationPnDbContext backendConfigurationPnDbContext,
        ItemsPlanningPnDbContext itemsPlanningPnDbContext,
        IBackendConfigurationLocalizationService? localizationService)
    {

        var folderIds = await backendConfigurationPnDbContext.ProperyAreaFolders
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
            var planning = await CreateItemPlanningObject((int)areaRule.EformId!, areaRule.EformName,
                    folderId, areaRulePlanningModel, areaRule, userId, backendConfigurationPnDbContext, itemsPlanningPnDbContext)
                .ConfigureAwait(false);
            planning.NameTranslations = areaRule.AreaRuleTranslations.Select(
                areaRuleAreaRuleTranslation => new PlanningNameTranslation
                {
                    LanguageId = areaRuleAreaRuleTranslation.LanguageId,
                    Name = areaRuleAreaRuleTranslation.Name
                }).ToList();

            if (areaRulePlanningModel.TypeSpecificFields != null) // it not need
            {
                if (areaRulePlanningModel.TypeSpecificFields.RepeatType != null)
                {
                    planning.RepeatType =
                        (Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType)areaRulePlanningModel
                            .TypeSpecificFields.RepeatType;
                }

                if (areaRulePlanningModel.TypeSpecificFields.RepeatEvery != null)
                {
                    planning.RepeatEvery =
                        (int)areaRulePlanningModel.TypeSpecificFields.RepeatEvery;
                }

                planning.RepeatUntil =
                    areaRulePlanningModel.TypeSpecificFields.EndDate;
                planning.DayOfWeek =
                    (DayOfWeek)areaRulePlanningModel.TypeSpecificFields.DayOfWeek == 0
                        ? DayOfWeek.Sunday
                        : (DayOfWeek)areaRulePlanningModel.TypeSpecificFields.DayOfWeek;
                planning.DayOfMonth =
                    areaRulePlanningModel.TypeSpecificFields.DayOfMonth == 0
                        ? 1
                        : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
            }

            await planning.Update(itemsPlanningPnDbContext).ConfigureAwait(false);
            await PairItemWithSiteHelper.Pair(
                areaRulePlanningModel.AssignedSites.Select(x => x.SiteId).ToList(), (int)areaRule.EformId,
                planning.Id,
                folderId, core, itemsPlanningPnDbContext, areaRulePlanningModel.UseStartDateAsStartOfPeriod, localizationService).ConfigureAwait(false);
            planningId = planning.Id;

        }

        await CreateAreaRulePlanningObject(areaRulePlanningModel, areaRule, planningId,
            folderId, backendConfigurationPnDbContext, userId).ConfigureAwait(false);
    }

    public static async Task CreatePlanningType6(AreaRule areaRule, MicrotingDbContext sdkDbContext,
        AreaRulePlanningModel areaRulePlanningModel, Core core, int userId,
        BackendConfigurationPnDbContext backendConfigurationPnDbContext,
        ItemsPlanningPnDbContext itemsPlanningPnDbContext,
        IBackendConfigurationLocalizationService? localizationService)
    {
        // create folder with name heat pump
        var translatesForFolder = areaRule.AreaRuleTranslations
            .Select(x => new CommonTranslationsModel
            {
                LanguageId = x.LanguageId,
                Description = "",
                Name = x.Name
            }).ToList();
        var danishLanguage = await sdkDbContext.Languages.FirstAsync(x => x.LanguageCode == "da");
        var englishLanguage = await sdkDbContext.Languages.FirstAsync(x => x.LanguageCode == "en-US");
        var germanLanguage = await sdkDbContext.Languages.FirstAsync(x => x.LanguageCode == "de-DE");
        var folderId = await core.FolderCreate(translatesForFolder, areaRule.FolderId).ConfigureAwait(false);

        var planningForType6HoursAndEnergyEnabledId = 0;
        var planningForType6IdOne = 0;
        var planningForType6IdTwo = 0;
        if (areaRulePlanningModel.TypeSpecificFields?.HoursAndEnergyEnabled is true &&
            areaRulePlanningModel.Status)
        {
            await areaRule.Update(backendConfigurationPnDbContext).ConfigureAwait(false);
            const string eformName = "10. Varmepumpe timer og energi";
            var eformId = await sdkDbContext.CheckListTranslations
                .Where(x => x.Text == eformName)
                .Select(x => x.CheckListId)
                .FirstAsync().ConfigureAwait(false);
            var planningForType6HoursAndEnergyEnabled = await CreateItemPlanningObject(eformId, eformName,
                    folderId, areaRulePlanningModel, areaRule, userId, backendConfigurationPnDbContext, itemsPlanningPnDbContext)
                .ConfigureAwait(false);
            planningForType6HoursAndEnergyEnabled.NameTranslations =
            [
                new()
                {
                    LanguageId = danishLanguage.Id, // da
                    Name = areaRule.AreaRuleTranslations
                        .Where(x => x.LanguageId == 1)
                        .Select(x => x.Name)
                        .FirstOrDefault() + ": Timer og energi"
                },

                new()
                {
                    LanguageId = englishLanguage.Id, // en
                    Name = areaRule.AreaRuleTranslations
                        .Where(x => x.LanguageId == 2)
                        .Select(x => x.Name)
                        .FirstOrDefault() + ": Hours and energy"
                },

                new()
                {
                    LanguageId = germanLanguage.Id, // ge
                    Name = areaRule.AreaRuleTranslations
                        .Where(x => x.LanguageId == 3)
                        .Select(x => x.Name)
                        .FirstOrDefault() + ": Stunden und Energie"
                }
            ];
            planningForType6HoursAndEnergyEnabled.RepeatEvery = 12;
            planningForType6HoursAndEnergyEnabled.RepeatType =
                (Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType)RepeatType.Month;
            if (areaRulePlanningModel.TypeSpecificFields != null)
            {
                planningForType6HoursAndEnergyEnabled.DayOfWeek =
                    (DayOfWeek)areaRulePlanningModel.TypeSpecificFields.DayOfWeek == 0
                        ? DayOfWeek.Monday
                        : (DayOfWeek)areaRulePlanningModel.TypeSpecificFields.DayOfWeek;
                planningForType6HoursAndEnergyEnabled.DayOfMonth =
                    areaRulePlanningModel.TypeSpecificFields.DayOfMonth == 0
                        ? 1
                        : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
            }

            await planningForType6HoursAndEnergyEnabled.Update(itemsPlanningPnDbContext).ConfigureAwait(false);
            await PairItemWithSiteHelper.Pair(
                areaRulePlanningModel.AssignedSites.Select(x => x.SiteId).ToList(), eformId,
                planningForType6HoursAndEnergyEnabled.Id,
                folderId, core, itemsPlanningPnDbContext, areaRulePlanningModel.UseStartDateAsStartOfPeriod, localizationService).ConfigureAwait(false);
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
                areaRulePlanningModel, areaRule, userId, backendConfigurationPnDbContext, itemsPlanningPnDbContext).ConfigureAwait(false);
            planningForType6One.NameTranslations =
            [
                new()
                {
                    LanguageId = danishLanguage.Id, // da
                    Name = areaRule.AreaRuleTranslations
                        .Where(x => x.LanguageId == 1)
                        .Select(x => x.Name)
                        .FirstOrDefault() + ": Service"
                },

                new()
                {
                    LanguageId = englishLanguage.Id, // en
                    Name = areaRule.AreaRuleTranslations
                        .Where(x => x.LanguageId == 2)
                        .Select(x => x.Name)
                        .FirstOrDefault() + ": Service"
                },

                new()
                {
                    LanguageId = germanLanguage.Id, // ge
                    Name = areaRule.AreaRuleTranslations
                        .Where(x => x.LanguageId == 3)
                        .Select(x => x.Name)
                        .FirstOrDefault() + ": Service"
                }
            ];
            planningForType6One.RepeatEvery = 12;
            planningForType6One.RepeatType =
                (Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType)RepeatType.Month;
            var planningForType6Two = await CreateItemPlanningObject(eformIdTwo, eformNameTwo, folderId,
                areaRulePlanningModel, areaRule, userId, backendConfigurationPnDbContext, itemsPlanningPnDbContext).ConfigureAwait(false);
            planningForType6Two.NameTranslations =
            [
                new()
                {
                    LanguageId = danishLanguage.Id, // da
                    Name = areaRule.AreaRuleTranslations
                        .Where(x => x.LanguageId == 1)
                        .Select(x => x.Name)
                        .FirstOrDefault() + ": Logbog"
                },

                new()
                {
                    LanguageId = englishLanguage.Id, // en
                    Name = areaRule.AreaRuleTranslations
                        .Where(x => x.LanguageId == 2)
                        .Select(x => x.Name)
                        .FirstOrDefault() + ": Logbook"
                },

                new()
                {
                    LanguageId = germanLanguage.Id, // ge
                    Name = areaRule.AreaRuleTranslations
                        .Where(x => x.LanguageId == 3)
                        .Select(x => x.Name)
                        .FirstOrDefault() + ": Logbook"
                }
            ];
            planningForType6Two.RepeatEvery = 0;
            planningForType6Two.RepeatType =
                (Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType)RepeatType.Day;
            if (areaRulePlanningModel.TypeSpecificFields is not null)
            {
                planningForType6One.DayOfMonth =
                    areaRulePlanningModel.TypeSpecificFields.DayOfMonth == 0
                        ? 1
                        : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                planningForType6One.DayOfWeek =
                    (DayOfWeek?)areaRulePlanningModel.TypeSpecificFields.DayOfWeek == 0
                        ? DayOfWeek.Monday
                        : (DayOfWeek?)areaRulePlanningModel.TypeSpecificFields.DayOfWeek;
                planningForType6One.RepeatUntil =
                    areaRulePlanningModel.TypeSpecificFields.EndDate;

                planningForType6Two.DayOfMonth =
                    areaRulePlanningModel.TypeSpecificFields.DayOfMonth == 0
                        ? 1
                        : areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                planningForType6Two.DayOfWeek =
                    (DayOfWeek?)areaRulePlanningModel.TypeSpecificFields.DayOfWeek == 0
                        ? DayOfWeek.Monday
                        : (DayOfWeek?)areaRulePlanningModel.TypeSpecificFields.DayOfWeek;
                planningForType6Two.RepeatUntil =
                    areaRulePlanningModel.TypeSpecificFields.EndDate;
            }

            await planningForType6One.Update(itemsPlanningPnDbContext).ConfigureAwait(false);
            await PairItemWithSiteHelper.Pair(
                areaRulePlanningModel.AssignedSites.Select(x => x.SiteId).ToList(), eformIdOne,
                planningForType6One.Id,
                folderId, core, itemsPlanningPnDbContext, areaRulePlanningModel.UseStartDateAsStartOfPeriod, localizationService).ConfigureAwait(false);
            planningForType6IdOne = planningForType6One.Id;
            await planningForType6Two.Update(itemsPlanningPnDbContext).ConfigureAwait(false);
            await PairItemWithSiteHelper.Pair(
                areaRulePlanningModel.AssignedSites.Select(x => x.SiteId).ToList(), eformIdTwo,
                planningForType6Two.Id,
                folderId, core, itemsPlanningPnDbContext, areaRulePlanningModel.UseStartDateAsStartOfPeriod, localizationService).ConfigureAwait(false);
            planningForType6IdTwo = planningForType6Two.Id;
        }

        await CreateAreaRulePlanningObject(
            areaRulePlanningModel, areaRule, planningForType6HoursAndEnergyEnabledId,
            folderId, backendConfigurationPnDbContext, userId).ConfigureAwait(false);
        await CreateAreaRulePlanningObject(areaRulePlanningModel, areaRule,
            planningForType6IdOne,
            folderId, backendConfigurationPnDbContext, userId).ConfigureAwait(false);
        await CreateAreaRulePlanningObject(areaRulePlanningModel, areaRule,
            planningForType6IdTwo,
            folderId, backendConfigurationPnDbContext, userId).ConfigureAwait(false);
    }

    public static async Task CreatePlanningType9(AreaRule areaRule, MicrotingDbContext sdkDbContext,
        AreaRulePlanningModel areaRulePlanningModel, Core core, int userId,
        BackendConfigurationPnDbContext backendConfigurationPnDbContext,
        ItemsPlanningPnDbContext itemsPlanningPnDbContext)
    {
        var sites = areaRulePlanningModel.AssignedSites.Select(x => x.SiteId).ToList();
        if (areaRulePlanningModel.Status)
        {
            var siteId = sites.First();
            var site = await sdkDbContext.Sites.SingleOrDefaultAsync(x => x.Id == siteId).ConfigureAwait(false);
            var language =
                await sdkDbContext.Languages.SingleOrDefaultAsync(x => x.Id == site.LanguageId).ConfigureAwait(false);
            var property = await backendConfigurationPnDbContext.Properties
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
            ((EntitySelect)((DataElement)mainElement.ElementList[0]).DataItemList[0]).Source = entityListUidAreas;
            ((EntitySelect)((DataElement)mainElement.ElementList[0]).DataItemList[0]).Label = "Vælg rum for produkt";
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
            ((EntitySearch)((DataElement)mainElement.ElementList[0]).DataItemList[10]).EntityTypeId =
                entityListUidRegNo;
            // ((EntitySearch) ((DataElement) mainElement.ElementList[0]).DataItemList[9]).DisplayOrder = 11;
            ((EntitySearch)((DataElement)mainElement.ElementList[0]).DataItemList[11]).EntityTypeId = entityListUid;
            // ((EntitySearch) ((DataElement) mainElement.ElementList[0]).DataItemList[10]).DisplayOrder = 12;
            ((EntitySearch)((DataElement)mainElement.ElementList[0]).DataItemList[12]).EntityTypeId =
                entityListUidRegNo;
            // ((EntitySearch) ((DataElement) mainElement.ElementList[0]).DataItemList[11]).DisplayOrder = 13;
            ((EntitySearch)((DataElement)mainElement.ElementList[0]).DataItemList[13]).EntityTypeId = entityListUid;
            // ((EntitySearch) ((DataElement) mainElement.ElementList[0]).DataItemList[12]).DisplayOrder = 14;
            ((EntitySearch)((DataElement)mainElement.ElementList[0]).DataItemList[14]).EntityTypeId =
                entityListUidRegNo;
            // ((EntitySearch) ((DataElement) mainElement.ElementList[0]).DataItemList[13]).DisplayOrder = 15;
            ((EntitySearch)((DataElement)mainElement.ElementList[0]).DataItemList[15]).EntityTypeId = entityListUid;
            // ((EntitySearch) ((DataElement) mainElement.ElementList[0]).DataItemList[14]).DisplayOrder = 16;
            ((EntitySearch)((DataElement)mainElement.ElementList[0]).DataItemList[16]).EntityTypeId =
                entityListUidRegNo;
            // ((EntitySearch) ((DataElement) mainElement.ElementList[0]).DataItemList[15]).DisplayOrder = 17;
            ((EntitySearch)((DataElement)mainElement.ElementList[0]).DataItemList[17]).EntityTypeId = entityListUid;
            // ((EntitySearch) ((DataElement) mainElement.ElementList[0]).DataItemList[16]).DisplayOrder = 18;
            ((EntitySearch)((DataElement)mainElement.ElementList[0]).DataItemList[18]).EntityTypeId =
                entityListUidRegNo;
            // ((EntitySearch) ((DataElement) mainElement.ElementList[0]).DataItemList[17]).DisplayOrder = 19;
            ((EntitySearch)((DataElement)mainElement.ElementList[0]).DataItemList[19]).EntityTypeId = entityListUid;
            // ((EntitySearch) ((DataElement) mainElement.ElementList[0]).DataItemList[18]).DisplayOrder = 20;
            ((EntitySearch)((DataElement)mainElement.ElementList[0]).DataItemList[20]).EntityTypeId =
                entityListUidRegNo;
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
            ((DataElement)mainElement.ElementList[0]).DataItemGroupList.Add(new FieldGroup("0",
                "Hvordan opretter jeg produkter",
                "", Constants.FieldColors.Yellow, -1, "", []));

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
            ((FieldContainer)((DataElement)mainElement.ElementList[0]).DataItemGroupList[0]).DataItemList.Add(none);
            var caseId = await core.CaseCreate(mainElement, "", (int)site!.MicrotingUid!, folderTranslation.Id)
                .ConfigureAwait(false);

            var planning = await CreateItemPlanningObject((int)areaRule.EformId, areaRule.EformName,
                    areaRule.FolderId, areaRulePlanningModel, areaRule, userId, backendConfigurationPnDbContext, itemsPlanningPnDbContext)
                .ConfigureAwait(false);
            planning.RepeatEvery = 0;
            planning.RepeatType = (Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType)RepeatType.Day;
            planning.StartDate = DateTime.Now.ToUniversalTime();
            planning.SdkFolderId = folderTranslation.Id;
            var now = DateTime.UtcNow;
            planning.LastExecutedTime = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0);
            await planning.Update(itemsPlanningPnDbContext).ConfigureAwait(false);
            var planningCase = new PlanningCase
            {
                PlanningId = planning.Id,
                Status = 66,
                MicrotingSdkeFormId = (int)areaRule.EformId,
                CreatedByUserId = userId
            };
            await planningCase.Create(itemsPlanningPnDbContext).ConfigureAwait(false);
            var checkListSite = await sdkDbContext.CheckListSites.SingleAsync(x => x.MicrotingUid == caseId)
                .ConfigureAwait(false);
            var planningCaseSite = new PlanningCaseSite
            {
                MicrotingSdkSiteId = siteId,
                MicrotingSdkeFormId = (int)areaRule.EformId,
                Status = 66,
                PlanningId = planning.Id,
                PlanningCaseId = planningCase.Id,
                MicrotingSdkCaseId = (int)caseId!,
                MicrotingCheckListSitId = checkListSite.Id,
                CreatedByUserId = userId
            };

            await planningCaseSite.Create(itemsPlanningPnDbContext).ConfigureAwait(false);

            await CreateAreaRulePlanningObject(areaRulePlanningModel, areaRule, planning.Id,
                areaRule.FolderId, backendConfigurationPnDbContext, userId).ConfigureAwait(false);
        }
    }

    public static async Task CreatePlanningType10(AreaRule areaRule, MicrotingDbContext sdkDbContext,
        AreaRulePlanningModel areaRulePlanningModel, Core core, int userId,
        BackendConfigurationPnDbContext backendConfigurationPnDbContext,
        ItemsPlanningPnDbContext itemsPlanningPnDbContext,
        IBackendConfigurationLocalizationService? localizationService)
    {
        if (areaRulePlanningModel.Status)
        {
            var poolHours = await backendConfigurationPnDbContext.PoolHours
                .Where(x => x.AreaRuleId == areaRule.Id)
                .ToListAsync().ConfigureAwait(false);

            var property = await backendConfigurationPnDbContext.Properties
                .SingleAsync(x => x.Id == areaRule.PropertyId).ConfigureAwait(false);

            var lookupName = areaRule.AreaRuleTranslations.First().Name;

            if (lookupName == "Morgenrundtur" || lookupName == "Morning tour")
            {
                var globalPlanningTag = await itemsPlanningPnDbContext.PlanningTags
                    .OrderBy(x=> x.CreatedAt)
                    .LastOrDefaultAsync(x =>
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

                    await globalPlanningTag.Create(itemsPlanningPnDbContext).ConfigureAwait(false);
                }

                var planning = await CreateItemPlanningObject((int)areaRule.EformId!, areaRule.EformName,
                        areaRule.FolderId, areaRulePlanningModel, areaRule, userId, backendConfigurationPnDbContext, itemsPlanningPnDbContext)
                    .ConfigureAwait(false);
                planning.NameTranslations = areaRule.AreaRuleTranslations.Select(
                    areaRuleAreaRuleTranslation => new PlanningNameTranslation
                    {
                        LanguageId = areaRuleAreaRuleTranslation.LanguageId,
                        Name = areaRuleAreaRuleTranslation.Name
                    }).ToList();

                planning.RepeatEvery = 0;
                planning.RepeatType = (Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType)RepeatType.Day;
                planning.PlanningsTags.Add(new() { PlanningTagId = globalPlanningTag.Id });

                await planning.Update(itemsPlanningPnDbContext).ConfigureAwait(false);
                await PairItemWithSiteHelper.Pair(
                    areaRulePlanningModel.AssignedSites.Select(x => x.SiteId).ToList(), (int)areaRule.EformId,
                    planning.Id,
                    areaRule.FolderId, core, itemsPlanningPnDbContext, areaRulePlanningModel.UseStartDateAsStartOfPeriod, localizationService).ConfigureAwait(false);

                await CreateAreaRulePlanningObject(areaRulePlanningModel, areaRule, planning.Id,
                    areaRule.FolderId, backendConfigurationPnDbContext, userId).ConfigureAwait(false);

                return;
            }

            var subfolder = await sdkDbContext.Folders
                .Include(x => x.FolderTranslations)
                .Where(x => x.ParentId == areaRule.FolderId)
                .Where(x => x.FolderTranslations.Any(y => y.Name == lookupName))
                .FirstOrDefaultAsync().ConfigureAwait(false);

            Regex regex = new Regex(@"(\d\.\s)");
            DayOfWeek? currentWeekDay = null;

            var planningTag = await itemsPlanningPnDbContext.PlanningTags.SingleOrDefaultAsync(x =>
                x.Name == $"{property.Name} - Aflæsninger-Prøver").ConfigureAwait(false);

            if (planningTag == null)
            {
                planningTag = new PlanningTag
                {
                    Name = $"{property.Name} - Aflæsninger-Prøver",
                    CreatedByUserId = userId,
                    UpdatedByUserId = userId
                };

                await planningTag.Create(itemsPlanningPnDbContext).ConfigureAwait(false);
            }


            var globalPlanningTag1 = await itemsPlanningPnDbContext.PlanningTags.SingleOrDefaultAsync(x =>
                x.Name == $"{property.Name} - {areaRule.AreaRuleTranslations.First().Name}").ConfigureAwait(false);

            if (globalPlanningTag1 == null)
            {
                globalPlanningTag1 = new PlanningTag
                {
                    Name = $"{property.Name} - {areaRule.AreaRuleTranslations.First().Name}",
                    CreatedByUserId = userId,
                    UpdatedByUserId = userId
                };

                await globalPlanningTag1.Create(itemsPlanningPnDbContext).ConfigureAwait(false);
            }

            foreach (var poolHour in poolHours)
            {
                var clId = sdkDbContext.CheckListTranslations
                    .Where(x => x.Text == $"02. Fækale uheld - {property.Name}").Select(x => x.CheckListId)
                    .FirstOrDefault();
                var innerLookupName = $"{(int)poolHour.DayOfWeek}. {poolHour.DayOfWeek.ToString().Substring(0, 3)}";
                var poolDayFolder = await sdkDbContext.Folders
                    .Include(x => x.FolderTranslations)
                    .Where(x => x.ParentId == subfolder.Id)
                    .Where(x => x.FolderTranslations.Any(y => y.Name == innerLookupName))
                    .FirstAsync().ConfigureAwait(false);


                if (currentWeekDay == null || currentWeekDay != (DayOfWeek)poolHour.DayOfWeek)
                {
                    var planningStatic = await CreateItemPlanningObject(clId, $"02. Fækale uheld - {property.Name}",
                            poolDayFolder.Id, areaRulePlanningModel, areaRule, userId, backendConfigurationPnDbContext, itemsPlanningPnDbContext)
                        .ConfigureAwait(false);
                    planningStatic.RepeatEvery = 0;
                    planningStatic.RepeatType =
                        (Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType)RepeatType.Day;
                    planningStatic.SdkFolderName = innerLookupName;
                    planningStatic.PushMessageOnDeployment = false;
                    planningStatic.NameTranslations = areaRule.AreaRuleTranslations.Select(
                        areaRuleAreaRuleTranslation => new PlanningNameTranslation
                        {
                            LanguageId = areaRuleAreaRuleTranslation.LanguageId,
                            Name =
                                $"24. Fækale uheld - {areaRuleAreaRuleTranslation.Name}"
                        }).ToList();

                    var planningTagStatic = await itemsPlanningPnDbContext.PlanningTags.SingleOrDefaultAsync(x =>
                        x.Name == $"{property.Name} - Fækale uheld").ConfigureAwait(false);

                    if (planningTagStatic == null)
                    {
                        planningTagStatic = new PlanningTag
                        {
                            Name = $"{property.Name} - Fækale uheld",
                            CreatedByUserId = userId,
                            UpdatedByUserId = userId
                        };

                        await planningTagStatic.Create(itemsPlanningPnDbContext).ConfigureAwait(false);
                    }

                    planningStatic.PlanningsTags.Add(new() { PlanningTagId = planningTagStatic.Id });
                    planningStatic.PlanningsTags.Add(new() { PlanningTagId = globalPlanningTag1.Id });

                    await planningStatic.Update(itemsPlanningPnDbContext).ConfigureAwait(false);
                    await PairItemWithSiteHelper.Pair(
                        areaRulePlanningModel.AssignedSites.Select(x => x.SiteId).ToList(), clId, planningStatic.Id,
                        poolDayFolder.Id, core, itemsPlanningPnDbContext, areaRulePlanningModel.UseStartDateAsStartOfPeriod, localizationService).ConfigureAwait(false);
                    var areaRulePlanningStatic = await CreateAreaRulePlanningObject(areaRulePlanningModel, areaRule,
                        planningStatic.Id,
                        areaRule.FolderId, backendConfigurationPnDbContext, userId).ConfigureAwait(false);
                    areaRulePlanningStatic.ComplianceEnabled = false;
                    areaRulePlanningStatic.RepeatEvery = 0;
                    areaRulePlanningStatic.RepeatType = (int)RepeatType.Day;
                    areaRulePlanningStatic.FolderId = poolDayFolder.Id;

                    await areaRulePlanningStatic.Update(backendConfigurationPnDbContext).ConfigureAwait(false);
                }

                currentWeekDay = (DayOfWeek)poolHour.DayOfWeek;

                if (poolHour.IsActive)
                {
                    clId = sdkDbContext.CheckListTranslations
                        .Where(x => x.Text == $"01. Aflæsninger - {property.Name}").Select(x => x.CheckListId)
                        .FirstOrDefault();
                    var planning = await CreateItemPlanningObject(clId, $"01. Aflæsninger - {property.Name}",
                            poolDayFolder.Id, areaRulePlanningModel, areaRule, userId, backendConfigurationPnDbContext, itemsPlanningPnDbContext)
                        .ConfigureAwait(false);
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
                                $"{poolDayFolder.FolderTranslations.Where(x => x.LanguageId == areaRuleAreaRuleTranslation.LanguageId).Select(x => x.Name).First()} - {areaRuleAreaRuleTranslation.Name}"
                        }).ToList();
                    foreach (var planningNameTranslation in planning.NameTranslations)
                    {
                        planningNameTranslation.Name = regex.Replace(planningNameTranslation.Name, "");
                        planningNameTranslation.Name = $"{poolHour.Name}:00. {planningNameTranslation.Name}";
                    }

                    planning.PlanningsTags.Add(new() { PlanningTagId = planningTag.Id });
                    planning.PlanningsTags.Add(new() { PlanningTagId = globalPlanningTag1.Id });
                    planning.DaysBeforeRedeploymentPushMessageRepeat = false;

                    await planning.Update(itemsPlanningPnDbContext).ConfigureAwait(false);

                    poolHour.ItemsPlanningId = planning.Id;
                    await poolHour.Update(backendConfigurationPnDbContext).ConfigureAwait(false);

                    var areaRulePlanning = await CreateAreaRulePlanningObject(areaRulePlanningModel, areaRule,
                        planning.Id,
                        poolDayFolder.Id, backendConfigurationPnDbContext, userId).ConfigureAwait(false);
                    areaRulePlanning.ComplianceEnabled = false;
                    areaRulePlanning.RepeatEvery = 0;
                    areaRulePlanning.RepeatType = (int)RepeatType.Day;
                    areaRulePlanning.DayOfWeek = (int)(DayOfWeek)poolHour.DayOfWeek;
                    areaRulePlanning.FolderId = poolDayFolder.Id;

                    await areaRulePlanning.Update(backendConfigurationPnDbContext).ConfigureAwait(false);
                }
            }
        }
    }

    public static async Task DeleteItemPlanning(int itemPlanningId, Core core, int userId,
        BackendConfigurationPnDbContext backendConfigurationPnDbContext,
        ItemsPlanningPnDbContext itemsPlanningPnDbContext)
    {
        if (itemPlanningId != 0)
        {
            var planning = await itemsPlanningPnDbContext.Plannings
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.Id == itemPlanningId)
                .Include(x => x.PlanningSites)
                .Include(x => x.NameTranslations)
                .FirstOrDefaultAsync().ConfigureAwait(false);
            if (planning != null)
            {
                planning.UpdatedByUserId = userId;
                foreach (var planningSite in planning.PlanningSites
                             .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed))
                {
                    planningSite.UpdatedByUserId = userId;
                    await planningSite.Delete(itemsPlanningPnDbContext).ConfigureAwait(false);
                }

                var sdkDbContext = core.DbContextHelper.GetDbContext();
                var planningCases = await itemsPlanningPnDbContext.PlanningCases
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.PlanningId == planning.Id)
                    .ToListAsync().ConfigureAwait(false);

                foreach (var planningCase in planningCases)
                {
                    var planningCaseSites = await itemsPlanningPnDbContext.PlanningCaseSites
                        .Where(x => x.PlanningCaseId == planningCase.Id)
                        .Where(planningCaseSite => planningCaseSite.MicrotingSdkCaseId != 0 ||
                                                   planningCaseSite.MicrotingCheckListSitId != 0)
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .ToListAsync().ConfigureAwait(false);
                    foreach (var planningCaseSite in planningCaseSites)
                    {
                        var result =
                            await sdkDbContext.Cases
                                .SingleOrDefaultAsync(x => x.Id == planningCaseSite.MicrotingSdkCaseId)
                                .ConfigureAwait(false);
                        if (result is { MicrotingUid: { } })
                        {
                            await core.CaseDelete((int)result.MicrotingUid).ConfigureAwait(false);
                        }
                        else
                        {
                            var clSites = await sdkDbContext.CheckListSites.FirstOrDefaultAsync(x =>
                                x.Id == planningCaseSite.MicrotingCheckListSitId).ConfigureAwait(false);
                            if (clSites is not null)
                            {
                                await core.CaseDelete(clSites.MicrotingUid).ConfigureAwait(false);
                            }
                        }
                    }
                }

                var nameTranslationsPlanning =
                    planning.NameTranslations
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .ToList();

                foreach (var translation in nameTranslationsPlanning)
                {
                    await translation.Delete(itemsPlanningPnDbContext).ConfigureAwait(false);
                }

                await planning.Delete(itemsPlanningPnDbContext).ConfigureAwait(false);


                if (!itemsPlanningPnDbContext.PlanningSites.AsNoTracking().Any(x =>
                        x.PlanningId == planning.Id && x.WorkflowState != Constants.WorkflowStates.Removed))
                {
                    var complianceList = await backendConfigurationPnDbContext.Compliances
                        .Where(x => x.PlanningId == planning.Id).AsNoTracking().ToListAsync().ConfigureAwait(false);
                    foreach (var compliance in complianceList)
                    {
                        var dbCompliacne =
                            await backendConfigurationPnDbContext.Compliances.SingleAsync(x => x.Id == compliance.Id)
                                .ConfigureAwait(false);
                        await dbCompliacne.Delete(backendConfigurationPnDbContext).ConfigureAwait(false);
                        var property = await backendConfigurationPnDbContext.Properties
                            .SingleAsync(x => x.Id == compliance.PropertyId).ConfigureAwait(false);
                        if (backendConfigurationPnDbContext.Compliances.Any(x =>
                                x.PropertyId == property.Id && x.Deadline < DateTime.UtcNow &&
                                x.WorkflowState != Constants.WorkflowStates.Removed))
                        {
                            property.ComplianceStatusThirty = 2;
                            property.ComplianceStatus = 2;
                        }
                        else
                        {
                            if (!backendConfigurationPnDbContext.Compliances.Any(x =>
                                    x.PropertyId == property.Id && x.WorkflowState != Constants.WorkflowStates.Removed))
                            {
                                property.ComplianceStatusThirty = 0;
                                property.ComplianceStatus = 0;
                            }
                        }

                        await property.Update(backendConfigurationPnDbContext).ConfigureAwait(false);
                    }
                }
            }
        }
    }

    private static DateTime GetNextWeekday(DateTime start, DayOfWeek day)
    {
        // The (... + 7) % 7 ensures we end up with a value in the range [0, 6]
        int daysToAdd = ((int)day - (int)start.DayOfWeek + 7) % 7;
        return start.AddDays(daysToAdd);
    }
}