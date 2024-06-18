// using BackendConfiguration.Pn.Infrastructure.Helpers;
// using BackendConfiguration.Pn.Infrastructure.Models;
// using BackendConfiguration.Pn.Infrastructure.Models.AreaRules;
// using BackendConfiguration.Pn.Infrastructure.Models.AssignmentWorker;
// using BackendConfiguration.Pn.Infrastructure.Models.Properties;
// using BackendConfiguration.Pn.Infrastructure.Models.PropertyAreas;
// using Microsoft.EntityFrameworkCore;
// using Microting.eForm.Infrastructure.Constants;
// using Microting.eFormApi.BasePn.Abstractions;
// using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
// using Microting.ItemsPlanningBase.Infrastructure.Enums;
// using NSubstitute;
//
// namespace BackendConfiguration.Pn.Integration.Test;
//
// [Parallelizable(ParallelScope.Fixtures)]
// [TestFixture]
// public class BackendConfigurationAreaRulePlanningsServiceHelperTestAcidification : TestBaseSetup
// {
//     // Should test the UpdatePlanning method for area rule "00. Logbøger" for areaRule: 0 with repeat type "days" adn repeat every "2"
//     [Test]
//     public async Task UpdatePlanning_AreaRule0Days2_ReturnsSuccess()
//     {
//         // Arrange
//         var core = await GetCore();
//         var propertyCreateModel = new PropertyCreateModel
//         {
//             Address = Guid.NewGuid().ToString(),
//             Chr = Guid.NewGuid().ToString(),
//             IndustryCode = Guid.NewGuid().ToString(),
//             Cvr = Guid.NewGuid().ToString(),
//             IsFarm = false,
//             LanguagesIds = [1],
//             MainMailAddress = Guid.NewGuid().ToString(),
//             Name = Guid.NewGuid().ToString(),
//             WorkorderEnable = false
//         };
//
//         await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1, BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, 1, 1);
//
//         var deviceUserModel = new DeviceUserModel
//         {
//             CustomerNo = 0,
//             HasWorkOrdersAssigned = false,
//             IsBackendUser = false,
//             IsLocked = false,
//             LanguageCode = "da",
//             TimeRegistrationEnabled = false,
//             UserFirstName = Guid.NewGuid().ToString(),
//             UserLastName = Guid.NewGuid().ToString()
//         };
//
//         await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
//             TimePlanningPnDbContext);
//
//         var properties = await BackendConfigurationPnDbContext!.Properties.ToListAsync();
//         var sites = await MicrotingDbContext!.Sites.AsNoTracking().ToListAsync();
//
//         var propertyAssignWorkersModel = new PropertyAssignWorkersModel
//         {
//             Assignments =
//             [
//                 new()
//                 {
//                     PropertyId = properties[0].Id,
//                     IsChecked = true
//                 }
//             ],
//             SiteId = sites[2].Id
//         };
//
//         var userService = Substitute.For<IUserService>();
//         userService.UserId.Returns(1);
//         await BackendConfigurationAssignmentWorkerServiceHelper.Create(propertyAssignWorkersModel, core, userService,
//             BackendConfigurationPnDbContext, CaseTemplatePnDbContext, null, Bus);
//
//         var areaTranslation = await BackendConfigurationPnDbContext!.AreaTranslations.FirstAsync(x => x.Name == "09. Forsuring");
//         var areaId = areaTranslation.AreaId;
//         var currentSite = await MicrotingDbContext!.Sites.OrderByDescending(x => x.Id).FirstAsync();
//
//         var propertyAreasUpdateModel = new PropertyAreasUpdateModel
//         {
//             Areas =
//             [
//                 new()
//                 {
//                     AreaId = areaTranslation.AreaId,
//                     Activated = true
//                 }
//             ],
//             PropertyId = properties[0].Id
//         };
//
//         var result = await BackendConfigurationPropertyAreasServiceHelper.Update(propertyAreasUpdateModel, core, BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, 1);
//         var areaRules = await BackendConfigurationPnDbContext!.AreaRules.Where(x => x.PropertyId == properties[0].Id).ToListAsync();
//
//         // should create AreaRulePlanningModel for areaId
//         var areaRulePlanningModel = new AreaRulePlanningModel
//         {
//             AssignedSites =
//             [
//                 new()
//                 {
//                     Checked = true,
//                     SiteId = currentSite.Id
//
//                 }
//             ],
//             RuleId = areaRules[0].Id,
//             ComplianceEnabled = true,
//             PropertyId = properties[0].Id,
//             Status = true,
//             SendNotifications = true,
//             StartDate = DateTime.UtcNow,
//             TypeSpecificFields = new AreaRuleTypePlanningModel
//             {
//                 Alarm = AreaRuleT2AlarmsEnum.No,
//                 DayOfMonth = 0,
//                 DayOfWeek = 0,
//                 HoursAndEnergyEnabled = false,
//                 RepeatEvery = 2,
//                 RepeatType = 1
//             }
//
//         };
//
//         // Act
//         await BackendConfigurationAreaRulePlanningsServiceHelper.UpdatePlanning(areaRulePlanningModel,
//             core, 1, BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, null);
//
//         // Assert
//         var areaRuleTranslations = await BackendConfigurationPnDbContext!.AreaRuleTranslations
//             .Join(BackendConfigurationPnDbContext.AreaRules, atr => atr.AreaRuleId, ar => ar.Id,
//                 (atr, ar) => new { atr, ar }).Where(x => x.ar.PropertyId == properties[0].Id).ToListAsync();
//         var areaProperties = await BackendConfigurationPnDbContext!.AreaProperties.Where(x => x.PropertyId == properties[0].Id).ToListAsync();
//         var folderTranslations = await MicrotingDbContext!.FolderTranslations.ToListAsync();
//         var areaRulePlannings = await BackendConfigurationPnDbContext!.AreaRulePlannings.ToListAsync();
//         var planningSites = await BackendConfigurationPnDbContext.PlanningSites.ToListAsync();
//         var plannings = await ItemsPlanningPnDbContext!.Plannings.ToListAsync();
//         var planningNameTranslations = await ItemsPlanningPnDbContext.PlanningNameTranslation.ToListAsync();
//         var itemPlanningSites = await ItemsPlanningPnDbContext!.PlanningSites.ToListAsync();
//         var itemPlanningCases = await ItemsPlanningPnDbContext!.PlanningCases.ToListAsync();
//         var itemPlanningCaseSites = await ItemsPlanningPnDbContext!.PlanningCaseSites.ToListAsync();
//         var compliances = await BackendConfigurationPnDbContext!.Compliances.ToListAsync();
//         var checkListSites = await MicrotingDbContext!.CheckListSites.ToListAsync();
//         var cases = await MicrotingDbContext!.Cases.ToListAsync();
//
//         // Assert result
//         Assert.NotNull(result);
//         Assert.That(result.Success, Is.EqualTo(true));
//
//         // Assert areaRules
//         Assert.NotNull(areaRules);
//         Assert.That(areaRules.Count, Is.EqualTo(3));
//         Assert.That(areaRules[0].PropertyId, Is.EqualTo(properties[0].Id));
//         Assert.That(areaRules[1].PropertyId, Is.EqualTo(properties[0].Id));
//         Assert.That(areaRules[2].PropertyId, Is.EqualTo(properties[0].Id));
//         Assert.That(areaRules[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
//         Assert.That(areaRules[1].AreaId, Is.EqualTo(areaTranslation.AreaId));
//         Assert.That(areaRules[2].AreaId, Is.EqualTo(areaTranslation.AreaId));
//         Assert.That(areaRules[0].EformName, Is.EqualTo("09. Forsuring pH værdi"));
//         Assert.That(areaRules[1].EformName, Is.EqualTo("09. Forsuring serviceaftale"));
//         Assert.That(areaRules[2].EformName, Is.EqualTo("09. Forsuring driftsstop"));
//
//         // Assert areaRuleTranslations
//         Assert.NotNull(areaRuleTranslations);
//         Assert.That(areaRuleTranslations.Count, Is.EqualTo(9));
//         Assert.That(areaRuleTranslations[0].atr.Name, Is.EqualTo("09. Forsuring pH værdi"));
//         Assert.That(areaRuleTranslations[0].atr.LanguageId, Is.EqualTo(1));
//         Assert.That(areaRuleTranslations[1].atr.Name, Is.EqualTo("09. Acidification pH value"));
//         Assert.That(areaRuleTranslations[1].atr.LanguageId, Is.EqualTo(2));
//         Assert.That(areaRuleTranslations[2].atr.Name, Is.EqualTo("09. Ansäuerung pH-Wert"));
//         Assert.That(areaRuleTranslations[2].atr.LanguageId, Is.EqualTo(3));
//         Assert.That(areaRuleTranslations[3].atr.Name, Is.EqualTo("09. Forsuring serviceaftale"));
//         Assert.That(areaRuleTranslations[3].atr.LanguageId, Is.EqualTo(1));
//         Assert.That(areaRuleTranslations[4].atr.Name, Is.EqualTo("09. Acidification service agreement"));
//         Assert.That(areaRuleTranslations[4].atr.LanguageId, Is.EqualTo(2));
//         Assert.That(areaRuleTranslations[5].atr.Name, Is.EqualTo("09. Säuerungsservicevertrag"));
//         Assert.That(areaRuleTranslations[5].atr.LanguageId, Is.EqualTo(3));
//         Assert.That(areaRuleTranslations[6].atr.Name, Is.EqualTo("09. Forsuring driftsstop"));
//         Assert.That(areaRuleTranslations[6].atr.LanguageId, Is.EqualTo(1));
//         Assert.That(areaRuleTranslations[7].atr.Name, Is.EqualTo("09. Acidification downtime"));
//         Assert.That(areaRuleTranslations[7].atr.LanguageId, Is.EqualTo(2));
//         Assert.That(areaRuleTranslations[8].atr.Name, Is.EqualTo("09. Ausfallzeit der Ansäuerung"));
//         Assert.That(areaRuleTranslations[8].atr.LanguageId, Is.EqualTo(3));
//
//         // Assert areaProperties
//         Assert.NotNull(areaProperties);
//         Assert.That(areaProperties.Count, Is.EqualTo(1));
//         Assert.That(areaProperties[0].PropertyId, Is.EqualTo(properties[0].Id));
//         Assert.That(areaProperties[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
//
//         // Assert folder translations
//         Assert.That(folderTranslations, Is.Not.Null);
//         Assert.That(folderTranslations.Count, Is.EqualTo(31));
//         Assert.That(folderTranslations[4].Name, Is.EqualTo(propertyCreateModel.Name));
//         Assert.That(folderTranslations[4].LanguageId, Is.EqualTo(1));
//         Assert.That(folderTranslations[5].Name, Is.EqualTo(propertyCreateModel.Name));
//         Assert.That(folderTranslations[5].LanguageId, Is.EqualTo(2));
//         Assert.That(folderTranslations[6].Name, Is.EqualTo(propertyCreateModel.Name));
//         Assert.That(folderTranslations[6].LanguageId, Is.EqualTo(3));
//         Assert.That(folderTranslations[7].Name, Is.EqualTo("00.00 Opret ny opgave"));
//         Assert.That(folderTranslations[7].LanguageId, Is.EqualTo(1));
//         Assert.That(folderTranslations[8].Name, Is.EqualTo("00.00 Create a new task"));
//         Assert.That(folderTranslations[8].LanguageId, Is.EqualTo(2));
//         Assert.That(folderTranslations[9].Name, Is.EqualTo("00.00 Erstellen Sie eine neue Aufgabe"));
//         Assert.That(folderTranslations[9].LanguageId, Is.EqualTo(3));
//         Assert.That(folderTranslations[10].Name, Is.EqualTo("00.02 Mine øvrige opgaver"));
//         Assert.That(folderTranslations[10].LanguageId, Is.EqualTo(1));
//         Assert.That(folderTranslations[11].Name, Is.EqualTo("00.02 My other tasks"));
//         Assert.That(folderTranslations[11].LanguageId, Is.EqualTo(2));
//         Assert.That(folderTranslations[12].Name, Is.EqualTo("00.02 Meine anderen Aufgaben"));
//         Assert.That(folderTranslations[12].LanguageId, Is.EqualTo(3));
//         Assert.That(folderTranslations[13].Name, Is.EqualTo(propertyCreateModel.Name));
//         Assert.That(folderTranslations[13].LanguageId, Is.EqualTo(1));
//         Assert.That(folderTranslations[14].Name, Is.EqualTo(propertyCreateModel.Name));
//         Assert.That(folderTranslations[14].LanguageId, Is.EqualTo(2));
//         Assert.That(folderTranslations[15].Name, Is.EqualTo(propertyCreateModel.Name));
//         Assert.That(folderTranslations[15].LanguageId, Is.EqualTo(3));
//         Assert.That(folderTranslations[16].Name, Is.EqualTo("00.03 Andres opgaver"));
//         Assert.That(folderTranslations[16].LanguageId, Is.EqualTo(1));
//         Assert.That(folderTranslations[17].Name, Is.EqualTo("00.03 Others' tasks"));
//         Assert.That(folderTranslations[17].LanguageId, Is.EqualTo(2));
//         Assert.That(folderTranslations[18].Name, Is.EqualTo("00.03 Aufgaben anderer"));
//         Assert.That(folderTranslations[18].LanguageId, Is.EqualTo(3));
//         Assert.That(folderTranslations[19].Name, Is.EqualTo(propertyCreateModel.Name));
//         Assert.That(folderTranslations[19].LanguageId, Is.EqualTo(1));
//         Assert.That(folderTranslations[20].Name, Is.EqualTo(propertyCreateModel.Name));
//         Assert.That(folderTranslations[20].LanguageId, Is.EqualTo(2));
//         Assert.That(folderTranslations[21].Name, Is.EqualTo(propertyCreateModel.Name));
//         Assert.That(folderTranslations[21].LanguageId, Is.EqualTo(3));
//         Assert.That(folderTranslations[22].Name, Is.EqualTo("00.01 Mine hasteopgaver"));
//         Assert.That(folderTranslations[22].LanguageId, Is.EqualTo(1));
//         Assert.That(folderTranslations[23].Name, Is.EqualTo("00.01 My urgent tasks"));
//         Assert.That(folderTranslations[23].LanguageId, Is.EqualTo(2));
//         Assert.That(folderTranslations[24].Name, Is.EqualTo("00.01 Meine dringenden Aufgaben"));
//         Assert.That(folderTranslations[24].LanguageId, Is.EqualTo(3));
//         Assert.That(folderTranslations[25].Name, Is.EqualTo(propertyCreateModel.Name));
//         Assert.That(folderTranslations[25].LanguageId, Is.EqualTo(1));
//         Assert.That(folderTranslations[26].Name, Is.EqualTo(propertyCreateModel.Name));
//         Assert.That(folderTranslations[26].LanguageId, Is.EqualTo(2));
//         Assert.That(folderTranslations[27].Name, Is.EqualTo(propertyCreateModel.Name));
//         Assert.That(folderTranslations[27].LanguageId, Is.EqualTo(3));
//         Assert.That(folderTranslations[28].Name, Is.EqualTo("09. Forsuring"));
//         Assert.That(folderTranslations[28].LanguageId, Is.EqualTo(1));
//         Assert.That(folderTranslations[29].Name, Is.EqualTo("09. Acidification"));
//         Assert.That(folderTranslations[29].LanguageId, Is.EqualTo(2));
//         Assert.That(folderTranslations[30].Name, Is.EqualTo("09. Ansäuerung"));
//         Assert.That(folderTranslations[30].LanguageId, Is.EqualTo(3));
//
//         // Assert AreaRulePlannings
//         Assert.That(areaRulePlannings, Is.Not.Null);
//         Assert.That(areaRulePlannings.Count, Is.EqualTo(1));
//         Assert.That(areaRulePlannings[0].AreaRuleId, Is.EqualTo(areaRules[0].Id));
//         Assert.That(areaRulePlannings[0].ItemPlanningId, Is.EqualTo(plannings[0].Id));
//         Assert.That(areaRulePlannings[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
//         Assert.That(areaRulePlannings[0].PropertyId, Is.EqualTo(properties[0].Id));
//         Assert.That(areaRulePlannings[0].ComplianceEnabled, Is.EqualTo(true));
//         Assert.That(areaRulePlannings[0].SendNotifications, Is.EqualTo(true));
//
//         // Assert plannings
//         Assert.That(plannings, Is.Not.Null);
//         Assert.That(plannings.Count, Is.EqualTo(1));
//         Assert.That(plannings[0].RelatedEFormId, Is.EqualTo(areaRules[0].EformId));
//         Assert.That(plannings[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         Assert.That(plannings[0].SdkFolderId, Is.EqualTo(areaRules[0].FolderId));
//         Assert.That(plannings[0].LastExecutedTime, Is.Not.Null);
//         Assert.That(plannings[0].LastExecutedTime, Is.EqualTo(new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 0,0,0)));
//         var now = DateTime.UtcNow;
//
//         var nextExecutionTime =
//             new DateTime(now.Year, now.Month, now.Day).AddDays(2);
//         while (nextExecutionTime < now)
//         {
//             nextExecutionTime = nextExecutionTime.AddDays(2);
//         }
//
//         Assert.That(plannings[0].NextExecutionTime, Is.EqualTo(nextExecutionTime));
//         Assert.That(plannings[0].RepeatEvery, Is.EqualTo(2));
//         Assert.That(plannings[0].RepeatType, Is.EqualTo(RepeatType.Day));
//
//         // Assert planningNameTranslations
//         Assert.That(planningNameTranslations, Is.Not.Null);
//         Assert.That(planningNameTranslations.Count, Is.EqualTo(3));
//         Assert.That(planningNameTranslations[0].Name, Is.EqualTo(areaRuleTranslations[0].atr.Name));
//         Assert.That(planningNameTranslations[0].LanguageId, Is.EqualTo(areaRuleTranslations[0].atr.LanguageId));
//         Assert.That(planningNameTranslations[0].PlanningId, Is.EqualTo(plannings[0].Id));
//         Assert.That(planningNameTranslations[1].Name, Is.EqualTo(areaRuleTranslations[1].atr.Name));
//         Assert.That(planningNameTranslations[1].LanguageId, Is.EqualTo(areaRuleTranslations[1].atr.LanguageId));
//         Assert.That(planningNameTranslations[1].PlanningId, Is.EqualTo(plannings[0].Id));
//         Assert.That(planningNameTranslations[2].Name, Is.EqualTo(areaRuleTranslations[2].atr.Name));
//         Assert.That(planningNameTranslations[2].LanguageId, Is.EqualTo(areaRuleTranslations[2].atr.LanguageId));
//         Assert.That(planningNameTranslations[2].PlanningId, Is.EqualTo(plannings[0].Id));
//
//         // Assert planningSites
//         Assert.That(planningSites, Is.Not.Null);
//         Assert.That(planningSites.Count, Is.EqualTo(1));
//         Assert.That(planningSites[0].AreaRulePlanningsId, Is.EqualTo(areaRulePlannings[0].Id));
//         Assert.That(planningSites[0].AreaId, Is.EqualTo(areaId));
//         Assert.That(planningSites[0].SiteId, Is.EqualTo(sites[2].Id));
//         Assert.That(planningSites[0].Status, Is.EqualTo(33));
//         Assert.That(planningSites[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//
//         // Assert itemPlanningSites
//         Assert.That(itemPlanningSites, Is.Not.Null);
//         Assert.That(itemPlanningSites.Count, Is.EqualTo(1));
//         Assert.That(itemPlanningSites[0].PlanningId, Is.EqualTo(plannings[0].Id));
//         Assert.That(itemPlanningSites[0].SiteId, Is.EqualTo(sites[2].Id));
//         Assert.That(itemPlanningSites[0].LastExecutedTime, Is.Null);
//
//         // Assert itemPlanningCases
//         Assert.That(itemPlanningCases, Is.Not.Null);
//         Assert.That(itemPlanningCases.Count, Is.EqualTo(1));
//         Assert.That(itemPlanningCases[0].PlanningId, Is.EqualTo(plannings[0].Id));
//
//         // Assert itemPlanningCaseSites
//         Assert.That(itemPlanningCaseSites, Is.Not.Null);
//         Assert.That(itemPlanningCaseSites.Count, Is.EqualTo(1));
//         Assert.That(itemPlanningCaseSites[0].PlanningCaseId, Is.EqualTo(itemPlanningCases[0].Id));
//         Assert.That(itemPlanningCaseSites[0].PlanningId, Is.EqualTo(plannings[0].Id));
//         Assert.That(itemPlanningCaseSites[0].MicrotingSdkSiteId, Is.EqualTo(sites[2].Id));
//         Assert.That(itemPlanningCaseSites[0].MicrotingSdkCaseId, Is.EqualTo(cases[0].Id));
//         Assert.That(itemPlanningCaseSites[0].Status, Is.EqualTo(66));
//         Assert.That(itemPlanningCaseSites[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         Assert.That(itemPlanningCaseSites[0].MicrotingCheckListSitId, Is.EqualTo(0));
//
//         // Assert compliances
//         Assert.That(compliances, Is.Not.Null);
//         Assert.That(compliances.Count, Is.EqualTo(0));
//
//         // Assert checkListSites
//         Assert.That(checkListSites, Is.Not.Null);
//         Assert.That(checkListSites.Count, Is.EqualTo(0));
//
//         // Assert cases
//         Assert.That(cases, Is.Not.Null);
//         Assert.That(cases.Count, Is.EqualTo(1));
//         Assert.That(cases[0].SiteId, Is.EqualTo(sites[2].Id));
//         Assert.That(cases[0].CheckListId, Is.EqualTo(areaRules[0].EformId));
//         Assert.That(cases[0].FolderId, Is.Null);
//         Assert.That(cases[0].Status, Is.EqualTo(66));
//         Assert.That(cases[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//     }
// }