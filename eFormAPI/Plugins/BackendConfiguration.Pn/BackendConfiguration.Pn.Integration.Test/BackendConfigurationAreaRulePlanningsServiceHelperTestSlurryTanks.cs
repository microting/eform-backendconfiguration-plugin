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
// public class BackendConfigurationAreaRulePlanningsServiceHelperSlurryTanks : TestBaseSetup
// {
//     // Should test the UpdatePlanning method for area rule "03. Gyllebeholdere" for areaRule: 0 with construction only enabled
//     [Test]
//     public async Task UpdatePlanning_AreaRule0COnstructionOnly_ReturnsSuccess()
//     {
//         // Arrange
//         var core = await GetCore();
//         var propertyCreateModel = new PropertyCreateModel
//         {
//             Address = Guid.NewGuid().ToString(),
//             Chr = Guid.NewGuid().ToString(),
//             IndustryCode = Guid.NewGuid().ToString(),
//             Cvr = Guid.NewGuid().ToString(),
//             IsFarm = true,
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
//         var areaTranslation = await BackendConfigurationPnDbContext!.AreaTranslations.FirstAsync(x => x.Name == "03. Gyllebeholdere");
//         var area = await BackendConfigurationPnDbContext.Areas.FirstAsync(x => x.Id == areaTranslation.AreaId);
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
//         var propertyArea = await BackendConfigurationPnDbContext!.AreaProperties.FirstAsync(x => x.PropertyId == properties[0].Id && x.AreaId == area.Id);
//         var danishLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.Name == "Dansk");
//         var areaInitialFields = await BackendConfigurationPnDbContext.AreaInitialFields.Where(x => x.AreaId == area.Id)
//             .ToListAsync();
//         var checkListTranslation = await MicrotingDbContext.CheckListTranslations.FirstAsync(x => x.Text == areaInitialFields.First().EformName);
//
//         AreaRulesCreateModel areaRulesCreateModel = new AreaRulesCreateModel
//         {
//             AreaRules =
//             [
//                 new()
//                 {
//                     TranslatedNames =
//                     [
//                         new()
//                             { Name = "Beholeder 1", Description = "00. Logbøger", Id = danishLanguage.Id }
//                     ],
//                     TypeSpecificFields = new TypeSpecificFields
//                     {
//                         Alarm = AreaRuleT2AlarmsEnum.No,
//                         DayOfWeek = 1,
//                         EformId = checkListTranslation.CheckListId,
//                         Type = AreaRuleT2TypesEnum.Open
//                     }
//                 }
//             ],
//             PropertyAreaId = propertyArea.Id
//         };
//
//         await BackendConfigurationAreaRulesServiceHelper.Create(areaRulesCreateModel, core, 1, BackendConfigurationPnDbContext, danishLanguage);
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
//                 RepeatEvery = 0,
//                 RepeatType = 1,
//                 Type = AreaRuleT2TypesEnum.Open
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
//         var englishLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "en-US");
//         var germanLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "de-DE");
//         var floatingLayerEformId = await MicrotingDbContext.CheckListTranslations.Where(x => x.Text == "03. Kontrol flydelag").Select(x => x.CheckListId).FirstAsync();
//
//         // Assert result
//         Assert.That(result, Is.Not.Null);
//         Assert.That(result.Success, Is.EqualTo(true));
//
//         // Assert areaRules
//         Assert.That(areaRules, Is.Not.Null);
//         Assert.That(areaRules.Count, Is.EqualTo(1));
//         Assert.That(areaRules[0].PropertyId, Is.EqualTo(properties[0].Id));
//         Assert.That(areaRules[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
//         Assert.That(areaRules[0].EformName, Is.EqualTo("03. Kontrol konstruktion"));
//
//         // Assert areaRuleTranslations
//         Assert.That(areaRuleTranslations, Is.Not.Null);
//         Assert.That(areaRuleTranslations.Count, Is.EqualTo(1));
//         Assert.That(areaRuleTranslations[0].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
//         Assert.That(areaRuleTranslations[0].atr.LanguageId, Is.EqualTo(1));
//         Assert.That(areaRuleTranslations[0].atr.Name, Is.EqualTo("Beholeder 1"));
//
//         // Assert areaProperties
//         Assert.That(areaProperties, Is.Not.Null);
//         Assert.That(areaProperties.Count, Is.EqualTo(1));
//         Assert.That(areaProperties[0].PropertyId, Is.EqualTo(properties[0].Id));
//         Assert.That(areaProperties[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
//
//         // Assert folder translations
//         Assert.That(folderTranslations, Is.Not.Null);
//         Assert.That(folderTranslations.Count, Is.EqualTo(32));
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
//         Assert.That(folderTranslations[28].Name, Is.EqualTo("03. Gyllebeholdere"));
//         Assert.That(folderTranslations[28].LanguageId, Is.EqualTo(1));
//         Assert.That(folderTranslations[29].Name, Is.EqualTo("03. Slurry tanks"));
//         Assert.That(folderTranslations[29].LanguageId, Is.EqualTo(2));
//         Assert.That(folderTranslations[30].Name, Is.EqualTo("03. Gülletanks"));
//         Assert.That(folderTranslations[30].LanguageId, Is.EqualTo(3));
//         Assert.That(folderTranslations[31].Name, Is.EqualTo("Beholeder 1"));
//         Assert.That(folderTranslations[31].LanguageId, Is.EqualTo(1));
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
//         Assert.That(plannings[0].RelatedEFormId, Is.EqualTo(floatingLayerEformId));
//         Assert.That(plannings[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         Assert.That(plannings[0].SdkFolderId, Is.EqualTo(folderTranslations[31].FolderId));
//         Assert.That(plannings[0].LastExecutedTime, Is.Not.Null);
//         Assert.That(plannings[0].LastExecutedTime, Is.EqualTo(new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 0,0,0)));
//         var now = DateTime.UtcNow;
//         var nextExecutionTime =new DateTime(now.Year, now.Month, (int)plannings[0].DayOfMonth!, 0, 0, 0).AddMonths(1);
//
//         Assert.That(plannings[0].NextExecutionTime, Is.EqualTo(nextExecutionTime));
//         Assert.That(plannings[0].RepeatEvery, Is.EqualTo(1));
//         Assert.That(plannings[0].RepeatType, Is.EqualTo(RepeatType.Month));
//
//         // Assert planningNameTranslations
//         Assert.That(planningNameTranslations, Is.Not.Null);
//         Assert.That(planningNameTranslations.Count, Is.EqualTo(3));
//         Assert.That(planningNameTranslations[0].Name, Is.EqualTo("Beholeder 1: Flydelag"));
//         Assert.That(planningNameTranslations[0].LanguageId, Is.EqualTo(areaRuleTranslations[0].atr.LanguageId));
//         Assert.That(planningNameTranslations[0].PlanningId, Is.EqualTo(plannings[0].Id));
//         Assert.That(planningNameTranslations[1].Name, Is.EqualTo(": Floating layer"));
//         Assert.That(planningNameTranslations[1].LanguageId, Is.EqualTo(englishLanguage.Id));
//         Assert.That(planningNameTranslations[1].PlanningId, Is.EqualTo(plannings[0].Id));
//         Assert.That(planningNameTranslations[2].Name, Is.EqualTo(": Schwimmende Ebene"));
//         Assert.That(planningNameTranslations[2].LanguageId, Is.EqualTo(germanLanguage.Id));
//         Assert.That(planningNameTranslations[2].PlanningId, Is.EqualTo(plannings[0].Id));
//
//         // Assert planningSites
//         Assert.That(planningSites, Is.Not.Null);
//         Assert.That(planningSites.Count, Is.EqualTo(1));
//         Assert.That(planningSites[0].AreaRulePlanningsId, Is.EqualTo(areaRulePlannings[0].Id));
//         Assert.That(planningSites[0].AreaId, Is.EqualTo(area.Id));
//         Assert.That(planningSites[0].SiteId, Is.EqualTo(sites[2].Id));
//         Assert.That(planningSites[0].Status, Is.EqualTo(33));
//         Assert.That(planningSites[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(planningSites[1].AreaRulePlanningsId, Is.EqualTo(areaRulePlannings[1].Id));
//         // Assert.That(planningSites[1].AreaId, Is.EqualTo(area.Id));
//         // Assert.That(planningSites[1].SiteId, Is.EqualTo(sites[2].Id));
//         // Assert.That(planningSites[1].Status, Is.EqualTo(33));
//         // Assert.That(planningSites[1].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(planningSites[2].AreaRulePlanningsId, Is.EqualTo(areaRulePlannings[2].Id));
//         // Assert.That(planningSites[2].AreaId, Is.EqualTo(area.Id));
//         // Assert.That(planningSites[2].SiteId, Is.EqualTo(sites[2].Id));
//         // Assert.That(planningSites[2].Status, Is.EqualTo(33));
//         // Assert.That(planningSites[2].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
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
//         Assert.That(cases[0].CheckListId, Is.EqualTo(floatingLayerEformId));
//         Assert.That(cases[0].FolderId, Is.Null);
//         Assert.That(cases[0].Status, Is.EqualTo(33));
//         Assert.That(cases[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//
//     }
//
//     // Should test the UpdatePlanning method for area rule "03. Gyllebeholdere" for areaRule: 0 with alarm and construction
//     [Test]
//     public async Task UpdatePlanning_AreaRule0AlamAndConstruction_ReturnsSuccess()
//     {
//         // Arrange
//         var core = await GetCore();
//         var propertyCreateModel = new PropertyCreateModel
//         {
//             Address = Guid.NewGuid().ToString(),
//             Chr = Guid.NewGuid().ToString(),
//             IndustryCode = Guid.NewGuid().ToString(),
//             Cvr = Guid.NewGuid().ToString(),
//             IsFarm = true,
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
//         var areaTranslation = await BackendConfigurationPnDbContext!.AreaTranslations.FirstAsync(x => x.Name == "03. Gyllebeholdere");
//         var area = await BackendConfigurationPnDbContext.Areas.FirstAsync(x => x.Id == areaTranslation.AreaId);
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
//         var propertyArea = await BackendConfigurationPnDbContext!.AreaProperties.FirstAsync(x => x.PropertyId == properties[0].Id && x.AreaId == area.Id);
//         var danishLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.Name == "Dansk");
//         var areaInitialFields = await BackendConfigurationPnDbContext.AreaInitialFields.Where(x => x.AreaId == area.Id)
//             .ToListAsync();
//         var checkListTranslation = await MicrotingDbContext.CheckListTranslations.FirstAsync(x => x.Text == areaInitialFields.First().EformName);
//
//         AreaRulesCreateModel areaRulesCreateModel = new AreaRulesCreateModel
//         {
//             AreaRules =
//             [
//                 new()
//                 {
//                     TranslatedNames =
//                     [
//                         new()
//                             { Name = "Beholeder 1", Description = "00. Logbøger", Id = danishLanguage.Id }
//                     ],
//                     TypeSpecificFields = new TypeSpecificFields
//                     {
//                         Alarm = AreaRuleT2AlarmsEnum.Yes,
//                         DayOfWeek = 1,
//                         EformId = checkListTranslation.CheckListId,
//                         Type = AreaRuleT2TypesEnum.Closed
//                     }
//                 }
//             ],
//             PropertyAreaId = propertyArea.Id
//         };
//
//         await BackendConfigurationAreaRulesServiceHelper.Create(areaRulesCreateModel, core, 1, BackendConfigurationPnDbContext, danishLanguage);
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
//                 Alarm = AreaRuleT2AlarmsEnum.Yes,
//                 DayOfMonth = 0,
//                 DayOfWeek = 0,
//                 HoursAndEnergyEnabled = false,
//                 RepeatEvery = 0,
//                 RepeatType = 1,
//                 Type = AreaRuleT2TypesEnum.Closed
//             }
//
//         };
//
//         // Act
//         await BackendConfigurationAreaRulePlanningsServiceHelper.UpdatePlanning(areaRulePlanningModel,
//             core, 1, BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, null);
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
//         var englishLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "en-US");
//         var germanLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "de-DE");
//         var alarmeFormid = await MicrotingDbContext.CheckListTranslations.Where(x => x.Text == "03. Kontrol flydelag").Select(x => x.CheckListId).FirstAsync();
//
//         // Assert result
//         Assert.That(result, Is.Not.Null);
//         Assert.That(result.Success, Is.EqualTo(true));
//
//         // Assert areaRules
//         Assert.That(areaRules, Is.Not.Null);
//         Assert.That(areaRules.Count, Is.EqualTo(1));
//         Assert.That(areaRules[0].PropertyId, Is.EqualTo(properties[0].Id));
//         Assert.That(areaRules[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
//         Assert.That(areaRules[0].EformName, Is.EqualTo("03. Kontrol konstruktion"));
//
//         // Assert areaRuleTranslations
//         Assert.That(areaRuleTranslations, Is.Not.Null);
//         Assert.That(areaRuleTranslations.Count, Is.EqualTo(1));
//         Assert.That(areaRuleTranslations[0].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
//         Assert.That(areaRuleTranslations[0].atr.LanguageId, Is.EqualTo(1));
//         Assert.That(areaRuleTranslations[0].atr.Name, Is.EqualTo("Beholeder 1"));
//
//         // Assert areaProperties
//         Assert.That(areaProperties, Is.Not.Null);
//         Assert.That(areaProperties.Count, Is.EqualTo(1));
//         Assert.That(areaProperties[0].PropertyId, Is.EqualTo(properties[0].Id));
//         Assert.That(areaProperties[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
//
//         // Assert folder translations
//         Assert.That(folderTranslations, Is.Not.Null);
//         Assert.That(folderTranslations.Count, Is.EqualTo(32));
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
//         Assert.That(folderTranslations[28].Name, Is.EqualTo("03. Gyllebeholdere"));
//         Assert.That(folderTranslations[28].LanguageId, Is.EqualTo(1));
//         Assert.That(folderTranslations[29].Name, Is.EqualTo("03. Slurry tanks"));
//         Assert.That(folderTranslations[29].LanguageId, Is.EqualTo(2));
//         Assert.That(folderTranslations[30].Name, Is.EqualTo("03. Gülletanks"));
//         Assert.That(folderTranslations[30].LanguageId, Is.EqualTo(3));
//         Assert.That(folderTranslations[31].Name, Is.EqualTo("Beholeder 1"));
//         Assert.That(folderTranslations[31].LanguageId, Is.EqualTo(1));
//
//         // Assert AreaRulePlannings
//         Assert.That(areaRulePlannings, Is.Not.Null);
//         Assert.That(areaRulePlannings.Count, Is.EqualTo(1));
//         Assert.That(areaRulePlannings[0].AreaRuleId, Is.EqualTo(areaRules[0].Id));
//         Assert.That(areaRulePlannings[0].ItemPlanningId, Is.EqualTo(plannings[0].Id));
//         // Assert.That(areaRulePlannings[1].ItemPlanningId, Is.EqualTo(0));
//         // Assert.That(areaRulePlannings[2].ItemPlanningId, Is.EqualTo(plannings[1].Id));
//         Assert.That(areaRulePlannings[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
//         // Assert.That(areaRulePlannings[1].AreaId, Is.EqualTo(areaTranslation.AreaId));
//         // Assert.That(areaRulePlannings[2].AreaId, Is.EqualTo(areaTranslation.AreaId));
//         Assert.That(areaRulePlannings[0].PropertyId, Is.EqualTo(properties[0].Id));
//         // Assert.That(areaRulePlannings[1].PropertyId, Is.EqualTo(properties[0].Id));
//         // Assert.That(areaRulePlannings[2].PropertyId, Is.EqualTo(properties[0].Id));
//         Assert.That(areaRulePlannings[0].ComplianceEnabled, Is.EqualTo(true));
//         // Assert.That(areaRulePlannings[1].ComplianceEnabled, Is.EqualTo(true));
//         // Assert.That(areaRulePlannings[2].ComplianceEnabled, Is.EqualTo(true));
//         Assert.That(areaRulePlannings[0].SendNotifications, Is.EqualTo(true));
//         // Assert.That(areaRulePlannings[1].SendNotifications, Is.EqualTo(true));
//         // Assert.That(areaRulePlannings[2].SendNotifications, Is.EqualTo(true));
//
//         // Assert plannings
//         Assert.That(plannings, Is.Not.Null);
//         Assert.That(plannings.Count, Is.EqualTo(1));
//         const string eformName = "03. Kontrol flydelag";
//         var eformId = await MicrotingDbContext.CheckListTranslations
//             .Where(x => x.Text == eformName)
//             .Select(x => x.CheckListId)
//             .FirstAsync().ConfigureAwait(false);
//         Assert.That(plannings[0].RelatedEFormId, Is.EqualTo(eformId));
//         Assert.That(plannings[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         Assert.That(plannings[0].SdkFolderId, Is.EqualTo(folderTranslations[31].FolderId));
//         Assert.That(plannings[0].LastExecutedTime, Is.Not.Null);
//         Assert.That(plannings[0].LastExecutedTime, Is.EqualTo(new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 0,0,0)));
//         var now = DateTime.UtcNow;
//         var nextExecutionTime = new DateTime(now.Year, now.Month, (int)plannings[0].DayOfMonth!, 0, 0, 0).AddMonths(1);
//
//         Assert.That(plannings[0].NextExecutionTime, Is.EqualTo(nextExecutionTime));
//         Assert.That(plannings[0].RepeatEvery, Is.EqualTo(1));
//         Assert.That(plannings[0].RepeatType, Is.EqualTo(RepeatType.Month));
//
//         // Assert.That(plannings[1].RelatedEFormId, Is.EqualTo(areaRules[0].EformId));
//         // Assert.That(plannings[1].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(plannings[1].SdkFolderId, Is.EqualTo(folderTranslations[31].FolderId));
//         // Assert.That(plannings[1].LastExecutedTime, Is.Not.Null);
//         // Assert.That(plannings[1].LastExecutedTime, Is.EqualTo(new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 0,0,0)));
//         // nextExecutionTime =new DateTime(now.Year + 1, 1, (int)plannings[0].DayOfMonth!, 0, 0, 0);
//         //
//         // Assert.That(plannings[1].NextExecutionTime, Is.EqualTo(nextExecutionTime));
//         // Assert.That(plannings[1].RepeatEvery, Is.EqualTo(12));
//         // Assert.That(plannings[1].RepeatType, Is.EqualTo(RepeatType.Month));
//
//         // Assert planningNameTranslations
//         Assert.That(planningNameTranslations, Is.Not.Null);
//         Assert.That(planningNameTranslations.Count, Is.EqualTo(3));
//         Assert.That(planningNameTranslations[0].Name, Is.EqualTo("Beholeder 1: Flydelag"));
//         Assert.That(planningNameTranslations[0].LanguageId, Is.EqualTo(areaRuleTranslations[0].atr.LanguageId));
//         Assert.That(planningNameTranslations[0].PlanningId, Is.EqualTo(plannings[0].Id));
//         Assert.That(planningNameTranslations[1].Name, Is.EqualTo(": Floating layer"));
//         Assert.That(planningNameTranslations[1].LanguageId, Is.EqualTo(englishLanguage.Id));
//         Assert.That(planningNameTranslations[1].PlanningId, Is.EqualTo(plannings[0].Id));
//         Assert.That(planningNameTranslations[2].Name, Is.EqualTo(": Schwimmende Ebene"));
//         Assert.That(planningNameTranslations[2].LanguageId, Is.EqualTo(germanLanguage.Id));
//         Assert.That(planningNameTranslations[2].PlanningId, Is.EqualTo(plannings[0].Id));
//         // Assert.That(planningNameTranslations[3].Name, Is.EqualTo("Beholeder 1: Konstruktion"));
//         // Assert.That(planningNameTranslations[3].LanguageId, Is.EqualTo(areaRuleTranslations[0].atr.LanguageId));
//         // Assert.That(planningNameTranslations[3].PlanningId, Is.EqualTo(plannings[1].Id));
//         // Assert.That(planningNameTranslations[4].Name, Is.EqualTo(": Construction"));
//         // Assert.That(planningNameTranslations[4].LanguageId, Is.EqualTo(englishLanguage.Id));
//         // Assert.That(planningNameTranslations[4].PlanningId, Is.EqualTo(plannings[1].Id));
//         // Assert.That(planningNameTranslations[5].Name, Is.EqualTo(": Konstruktion"));
//         // Assert.That(planningNameTranslations[5].LanguageId, Is.EqualTo(germanLanguage.Id));
//         // Assert.That(planningNameTranslations[5].PlanningId, Is.EqualTo(plannings[1].Id));
//
//         // Assert planningSites
//         Assert.That(planningSites, Is.Not.Null);
//         Assert.That(planningSites.Count, Is.EqualTo(1));
//         Assert.That(planningSites[0].AreaRulePlanningsId, Is.EqualTo(areaRulePlannings[0].Id));
//         Assert.That(planningSites[0].AreaId, Is.EqualTo(area.Id));
//         Assert.That(planningSites[0].SiteId, Is.EqualTo(sites[2].Id));
//         Assert.That(planningSites[0].Status, Is.EqualTo(33));
//         Assert.That(planningSites[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(planningSites[1].AreaRulePlanningsId, Is.EqualTo(areaRulePlannings[1].Id));
//         // Assert.That(planningSites[1].AreaId, Is.EqualTo(area.Id));
//         // Assert.That(planningSites[1].SiteId, Is.EqualTo(sites[2].Id));
//         // Assert.That(planningSites[1].Status, Is.EqualTo(33));
//         // Assert.That(planningSites[1].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(planningSites[2].AreaRulePlanningsId, Is.EqualTo(areaRulePlannings[2].Id));
//         // Assert.That(planningSites[2].AreaId, Is.EqualTo(area.Id));
//         // Assert.That(planningSites[2].SiteId, Is.EqualTo(sites[2].Id));
//         // Assert.That(planningSites[2].Status, Is.EqualTo(33));
//         // Assert.That(planningSites[2].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//
//         // Assert itemPlanningSites
//         Assert.That(itemPlanningSites, Is.Not.Null);
//         Assert.That(itemPlanningSites.Count, Is.EqualTo(1));
//         Assert.That(itemPlanningSites[0].PlanningId, Is.EqualTo(plannings[0].Id));
//         Assert.That(itemPlanningSites[0].SiteId, Is.EqualTo(sites[2].Id));
//         Assert.That(itemPlanningSites[0].LastExecutedTime, Is.Null);
//         // Assert.That(itemPlanningSites[1].PlanningId, Is.EqualTo(plannings[1].Id));
//         // Assert.That(itemPlanningSites[1].SiteId, Is.EqualTo(sites[2].Id));
//         // Assert.That(itemPlanningSites[1].LastExecutedTime, Is.Null);
//
//         // Assert itemPlanningCases
//         Assert.That(itemPlanningCases, Is.Not.Null);
//         Assert.That(itemPlanningCases.Count, Is.EqualTo(1));
//         Assert.That(itemPlanningCases[0].PlanningId, Is.EqualTo(plannings[0].Id));
//         //Assert.That(itemPlanningCases[1].PlanningId, Is.EqualTo(plannings[1].Id));
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
//         // Assert.That(itemPlanningCaseSites[1].PlanningCaseId, Is.EqualTo(itemPlanningCases[1].Id));
//         // Assert.That(itemPlanningCaseSites[1].PlanningId, Is.EqualTo(plannings[1].Id));
//         // Assert.That(itemPlanningCaseSites[1].MicrotingSdkSiteId, Is.EqualTo(sites[2].Id));
//         // Assert.That(itemPlanningCaseSites[1].MicrotingSdkCaseId, Is.EqualTo(cases[1].Id));
//         // Assert.That(itemPlanningCaseSites[1].Status, Is.EqualTo(66));
//         // Assert.That(itemPlanningCaseSites[1].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(itemPlanningCaseSites[1].MicrotingCheckListSitId, Is.EqualTo(0));
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
//         Assert.That(cases[0].CheckListId, Is.EqualTo(alarmeFormid));
//         Assert.That(cases[0].FolderId, Is.Null);
//         Assert.That(cases[0].Status, Is.EqualTo(33));
//         Assert.That(cases[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(cases[1].SiteId, Is.EqualTo(sites[2].Id));
//         // Assert.That(cases[1].CheckListId, Is.EqualTo(areaRules[0].EformId));
//         // Assert.That(cases[1].FolderId, Is.Null);
//         // Assert.That(cases[1].Status, Is.EqualTo(66));
//         // Assert.That(cases[1].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//     }
//
//     // Should test the UpdatePlanning method for area rule "03. Gyllebeholdere" for areaRule: 0 with construction, alarm and open container
//     [Test]
//     public async Task UpdatePlanning_AreaRule0AlamAndOpenContainerConstruction_ReturnsSuccess()
//     {
//         // Arrange
//         var core = await GetCore();
//         var propertyCreateModel = new PropertyCreateModel
//         {
//             Address = Guid.NewGuid().ToString(),
//             Chr = Guid.NewGuid().ToString(),
//             IndustryCode = Guid.NewGuid().ToString(),
//             Cvr = Guid.NewGuid().ToString(),
//             IsFarm = true,
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
//         var areaTranslation = await BackendConfigurationPnDbContext!.AreaTranslations.FirstAsync(x => x.Name == "03. Gyllebeholdere");
//         var area = await BackendConfigurationPnDbContext.Areas.FirstAsync(x => x.Id == areaTranslation.AreaId);
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
//         var propertyArea = await BackendConfigurationPnDbContext!.AreaProperties.FirstAsync(x => x.PropertyId == properties[0].Id && x.AreaId == area.Id);
//         var danishLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.Name == "Dansk");
//         var areaInitialFields = await BackendConfigurationPnDbContext.AreaInitialFields.Where(x => x.AreaId == area.Id)
//             .ToListAsync();
//         var checkListTranslation = await MicrotingDbContext.CheckListTranslations.FirstAsync(x => x.Text == areaInitialFields.First().EformName);
//
//         AreaRulesCreateModel areaRulesCreateModel = new AreaRulesCreateModel
//         {
//             AreaRules =
//             [
//                 new()
//                 {
//                     TranslatedNames =
//                     [
//                         new()
//                             { Name = "Beholeder 1", Description = "00. Logbøger", Id = danishLanguage.Id }
//                     ],
//                     TypeSpecificFields = new TypeSpecificFields
//                     {
//                         Alarm = AreaRuleT2AlarmsEnum.Yes,
//                         DayOfWeek = 1,
//                         EformId = checkListTranslation.CheckListId,
//                         Type = AreaRuleT2TypesEnum.Open
//                     }
//                 }
//             ],
//             PropertyAreaId = propertyArea.Id
//         };
//
//         await BackendConfigurationAreaRulesServiceHelper.Create(areaRulesCreateModel, core, 1, BackendConfigurationPnDbContext, danishLanguage);
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
//                 Alarm = AreaRuleT2AlarmsEnum.Yes,
//                 DayOfMonth = 0,
//                 DayOfWeek = 0,
//                 HoursAndEnergyEnabled = false,
//                 RepeatEvery = 0,
//                 RepeatType = 1,
//                 Type = AreaRuleT2TypesEnum.Open
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
//         var englishLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "en-US");
//         var germanLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "de-DE");
//         /*var alarmeFormid = */await MicrotingDbContext.CheckListTranslations.Where(x => x.Text == "03. Kontrol alarmanlæg gyllebeholder").Select(x => x.CheckListId).FirstAsync();
//         var floatingLayerEformId = await MicrotingDbContext.CheckListTranslations.Where(x => x.Text == "03. Kontrol flydelag").Select(x => x.CheckListId).FirstAsync();
//
//         Assert.That(result, Is.Not.Null);
//         Assert.That(result.Success, Is.EqualTo(true));
//
//         // Assert areaRules
//         Assert.That(areaRules, Is.Not.Null);
//         Assert.That(areaRules.Count, Is.EqualTo(1));
//         Assert.That(areaRules[0].PropertyId, Is.EqualTo(properties[0].Id));
//         Assert.That(areaRules[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
//         Assert.That(areaRules[0].EformName, Is.EqualTo("03. Kontrol konstruktion"));
//
//         // Assert areaRuleTranslations
//         Assert.That(areaRuleTranslations, Is.Not.Null);
//         Assert.That(areaRuleTranslations.Count, Is.EqualTo(1));
//         Assert.That(areaRuleTranslations[0].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
//         Assert.That(areaRuleTranslations[0].atr.LanguageId, Is.EqualTo(1));
//         Assert.That(areaRuleTranslations[0].atr.Name, Is.EqualTo("Beholeder 1"));
//
//         // Assert areaProperties
//         Assert.That(areaProperties, Is.Not.Null);
//         Assert.That(areaProperties.Count, Is.EqualTo(1));
//         Assert.That(areaProperties[0].PropertyId, Is.EqualTo(properties[0].Id));
//         Assert.That(areaProperties[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
//
//         // Assert folder translations
//         Assert.That(folderTranslations, Is.Not.Null);
//         Assert.That(folderTranslations.Count, Is.EqualTo(32));
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
//         Assert.That(folderTranslations[28].Name, Is.EqualTo("03. Gyllebeholdere"));
//         Assert.That(folderTranslations[28].LanguageId, Is.EqualTo(1));
//         Assert.That(folderTranslations[29].Name, Is.EqualTo("03. Slurry tanks"));
//         Assert.That(folderTranslations[29].LanguageId, Is.EqualTo(2));
//         Assert.That(folderTranslations[30].Name, Is.EqualTo("03. Gülletanks"));
//         Assert.That(folderTranslations[30].LanguageId, Is.EqualTo(3));
//         Assert.That(folderTranslations[31].Name, Is.EqualTo("Beholeder 1"));
//         Assert.That(folderTranslations[31].LanguageId, Is.EqualTo(1));
//
//         // Assert AreaRulePlannings
//         Assert.That(areaRulePlannings, Is.Not.Null);
//         Assert.That(areaRulePlannings.Count, Is.EqualTo(1));
//         Assert.That(areaRulePlannings[0].AreaRuleId, Is.EqualTo(areaRules[0].Id));
//         Assert.That(areaRulePlannings[0].ItemPlanningId, Is.EqualTo(plannings[0].Id));
//         // Assert.That(areaRulePlannings[1].ItemPlanningId, Is.EqualTo(plannings[1].Id));
//         // Assert.That(areaRulePlannings[2].ItemPlanningId, Is.EqualTo(plannings[2].Id));
//         Assert.That(areaRulePlannings[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
//         // Assert.That(areaRulePlannings[1].AreaId, Is.EqualTo(areaTranslation.AreaId));
//         // Assert.That(areaRulePlannings[2].AreaId, Is.EqualTo(areaTranslation.AreaId));
//         Assert.That(areaRulePlannings[0].PropertyId, Is.EqualTo(properties[0].Id));
//         // Assert.That(areaRulePlannings[1].PropertyId, Is.EqualTo(properties[0].Id));
//         // Assert.That(areaRulePlannings[2].PropertyId, Is.EqualTo(properties[0].Id));
//         Assert.That(areaRulePlannings[0].ComplianceEnabled, Is.EqualTo(true));
//         // Assert.That(areaRulePlannings[1].ComplianceEnabled, Is.EqualTo(true));
//         // Assert.That(areaRulePlannings[2].ComplianceEnabled, Is.EqualTo(true));
//         Assert.That(areaRulePlannings[0].SendNotifications, Is.EqualTo(true));
//         // Assert.That(areaRulePlannings[1].SendNotifications, Is.EqualTo(true));
//         // Assert.That(areaRulePlannings[2].SendNotifications, Is.EqualTo(true));
//
//         // Assert plannings
//         Assert.That(plannings, Is.Not.Null);
//         Assert.That(plannings.Count, Is.EqualTo(1));
//         Assert.That(plannings[0].RelatedEFormId, Is.EqualTo(floatingLayerEformId));
//         Assert.That(plannings[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         Assert.That(plannings[0].SdkFolderId, Is.EqualTo(folderTranslations[31].FolderId));
//         Assert.That(plannings[0].LastExecutedTime, Is.Not.Null);
//         Assert.That(plannings[0].LastExecutedTime, Is.EqualTo(new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 0,0,0)));
//         var now = DateTime.UtcNow;
//         var nextExecutionTime = new DateTime(now.Year, now.Month, (int)plannings[0].DayOfMonth!, 0, 0, 0).AddMonths(1);
//
//         Assert.That(plannings[0].NextExecutionTime, Is.EqualTo(nextExecutionTime));
//         Assert.That(plannings[0].RepeatEvery, Is.EqualTo(1));
//         Assert.That(plannings[0].RepeatType, Is.EqualTo(RepeatType.Month));
//
//         // Assert.That(plannings[1].RelatedEFormId, Is.EqualTo(alarmeFormid));
//         // Assert.That(plannings[1].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(plannings[1].SdkFolderId, Is.EqualTo(folderTranslations[31].FolderId));
//         // Assert.That(plannings[1].LastExecutedTime, Is.Not.Null);
//         // Assert.That(plannings[1].LastExecutedTime, Is.EqualTo(new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 0,0,0)));
//         // nextExecutionTime =new DateTime(now.Year, now.Month, (int)plannings[0].DayOfMonth!, 0, 0, 0).AddMonths(1);
//         //
//         // Assert.That(plannings[1].NextExecutionTime, Is.EqualTo(nextExecutionTime));
//         // Assert.That(plannings[1].RepeatEvery, Is.EqualTo(1));
//         // Assert.That(plannings[1].RepeatType, Is.EqualTo(RepeatType.Month));
//         //
//         //
//         // Assert.That(plannings[2].RelatedEFormId, Is.EqualTo(areaRules[0].EformId));
//         // Assert.That(plannings[2].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(plannings[2].SdkFolderId, Is.EqualTo(folderTranslations[31].FolderId));
//         // Assert.That(plannings[2].LastExecutedTime, Is.Not.Null);
//         // Assert.That(plannings[2].LastExecutedTime, Is.EqualTo(new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 0,0,0)));
//         // nextExecutionTime =new DateTime(now.Year + 1, 1, (int)plannings[0].DayOfMonth!, 0, 0, 0);
//         //
//         // Assert.That(plannings[2].NextExecutionTime, Is.EqualTo(nextExecutionTime));
//         // Assert.That(plannings[2].RepeatEvery, Is.EqualTo(12));
//         // Assert.That(plannings[2].RepeatType, Is.EqualTo(RepeatType.Month));
//
//         // Assert planningNameTranslations
//         Assert.That(planningNameTranslations, Is.Not.Null);
//         Assert.That(planningNameTranslations.Count, Is.EqualTo(3));
//         Assert.That(planningNameTranslations[0].Name, Is.EqualTo("Beholeder 1: Flydelag"));
//         Assert.That(planningNameTranslations[0].LanguageId, Is.EqualTo(areaRuleTranslations[0].atr.LanguageId));
//         Assert.That(planningNameTranslations[0].PlanningId, Is.EqualTo(plannings[0].Id));
//         Assert.That(planningNameTranslations[1].Name, Is.EqualTo(": Floating layer"));
//         Assert.That(planningNameTranslations[1].LanguageId, Is.EqualTo(englishLanguage.Id));
//         Assert.That(planningNameTranslations[1].PlanningId, Is.EqualTo(plannings[0].Id));
//         Assert.That(planningNameTranslations[2].Name, Is.EqualTo(": Schwimmende Ebene"));
//         Assert.That(planningNameTranslations[2].LanguageId, Is.EqualTo(germanLanguage.Id));
//         Assert.That(planningNameTranslations[2].PlanningId, Is.EqualTo(plannings[0].Id));
//         // Assert.That(planningNameTranslations[3].Name, Is.EqualTo("Beholeder 1: Alarm"));
//         // Assert.That(planningNameTranslations[3].LanguageId, Is.EqualTo(areaRuleTranslations[0].atr.LanguageId));
//         // Assert.That(planningNameTranslations[3].PlanningId, Is.EqualTo(plannings[1].Id));
//         // Assert.That(planningNameTranslations[4].Name, Is.EqualTo(": Alarm"));
//         // Assert.That(planningNameTranslations[4].LanguageId, Is.EqualTo(englishLanguage.Id));
//         // Assert.That(planningNameTranslations[4].PlanningId, Is.EqualTo(plannings[1].Id));
//         // Assert.That(planningNameTranslations[5].Name, Is.EqualTo(": Alarm"));
//         // Assert.That(planningNameTranslations[5].LanguageId, Is.EqualTo(germanLanguage.Id));
//         // Assert.That(planningNameTranslations[5].PlanningId, Is.EqualTo(plannings[1].Id));
//         // Assert.That(planningNameTranslations[6].Name, Is.EqualTo("Beholeder 1: Konstruktion"));
//         // Assert.That(planningNameTranslations[6].LanguageId, Is.EqualTo(areaRuleTranslations[0].atr.LanguageId));
//         // Assert.That(planningNameTranslations[6].PlanningId, Is.EqualTo(plannings[2].Id));
//         // Assert.That(planningNameTranslations[7].Name, Is.EqualTo(": Construction"));
//         // Assert.That(planningNameTranslations[7].LanguageId, Is.EqualTo(englishLanguage.Id));
//         // Assert.That(planningNameTranslations[7].PlanningId, Is.EqualTo(plannings[2].Id));
//         // Assert.That(planningNameTranslations[8].Name, Is.EqualTo(": Konstruktion"));
//         // Assert.That(planningNameTranslations[8].LanguageId, Is.EqualTo(germanLanguage.Id));
//         // Assert.That(planningNameTranslations[8].PlanningId, Is.EqualTo(plannings[2].Id));
//
//         // Assert planningSites
//         Assert.That(planningSites, Is.Not.Null);
//         Assert.That(planningSites.Count, Is.EqualTo(1));
//         Assert.That(planningSites[0].AreaRulePlanningsId, Is.EqualTo(areaRulePlannings[0].Id));
//         Assert.That(planningSites[0].AreaId, Is.EqualTo(area.Id));
//         Assert.That(planningSites[0].SiteId, Is.EqualTo(sites[2].Id));
//         Assert.That(planningSites[0].Status, Is.EqualTo(33));
//         Assert.That(planningSites[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(planningSites[1].AreaRulePlanningsId, Is.EqualTo(areaRulePlannings[1].Id));
//         // Assert.That(planningSites[1].AreaId, Is.EqualTo(area.Id));
//         // Assert.That(planningSites[1].SiteId, Is.EqualTo(sites[2].Id));
//         // Assert.That(planningSites[1].Status, Is.EqualTo(33));
//         // Assert.That(planningSites[1].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(planningSites[2].AreaRulePlanningsId, Is.EqualTo(areaRulePlannings[2].Id));
//         // Assert.That(planningSites[2].AreaId, Is.EqualTo(area.Id));
//         // Assert.That(planningSites[2].SiteId, Is.EqualTo(sites[2].Id));
//         // Assert.That(planningSites[2].Status, Is.EqualTo(33));
//         // Assert.That(planningSites[2].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//
//         // Assert itemPlanningSites
//         Assert.That(itemPlanningSites, Is.Not.Null);
//         Assert.That(itemPlanningSites.Count, Is.EqualTo(1));
//         Assert.That(itemPlanningSites[0].PlanningId, Is.EqualTo(plannings[0].Id));
//         Assert.That(itemPlanningSites[0].SiteId, Is.EqualTo(sites[2].Id));
//         Assert.That(itemPlanningSites[0].LastExecutedTime, Is.Null);
//         // Assert.That(itemPlanningSites[1].PlanningId, Is.EqualTo(plannings[1].Id));
//         // Assert.That(itemPlanningSites[1].SiteId, Is.EqualTo(sites[2].Id));
//         // Assert.That(itemPlanningSites[1].LastExecutedTime, Is.Null);
//         // Assert.That(itemPlanningSites[2].PlanningId, Is.EqualTo(plannings[2].Id));
//         // Assert.That(itemPlanningSites[2].SiteId, Is.EqualTo(sites[2].Id));
//         // Assert.That(itemPlanningSites[2].LastExecutedTime, Is.Null);
//
//         // Assert itemPlanningCases
//         Assert.That(itemPlanningCases, Is.Not.Null);
//         Assert.That(itemPlanningCases.Count, Is.EqualTo(1));
//         Assert.That(itemPlanningCases[0].PlanningId, Is.EqualTo(plannings[0].Id));
//         // Assert.That(itemPlanningCases[1].PlanningId, Is.EqualTo(plannings[1].Id));
//         // Assert.That(itemPlanningCases[2].PlanningId, Is.EqualTo(plannings[2].Id));
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
//         // Assert.That(itemPlanningCaseSites[1].PlanningCaseId, Is.EqualTo(itemPlanningCases[1].Id));
//         // Assert.That(itemPlanningCaseSites[1].PlanningId, Is.EqualTo(plannings[1].Id));
//         // Assert.That(itemPlanningCaseSites[1].MicrotingSdkSiteId, Is.EqualTo(sites[2].Id));
//         // Assert.That(itemPlanningCaseSites[1].MicrotingSdkCaseId, Is.EqualTo(cases[1].Id));
//         // Assert.That(itemPlanningCaseSites[1].Status, Is.EqualTo(66));
//         // Assert.That(itemPlanningCaseSites[1].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(itemPlanningCaseSites[1].MicrotingCheckListSitId, Is.EqualTo(0));
//         // Assert.That(itemPlanningCaseSites[2].PlanningCaseId, Is.EqualTo(itemPlanningCases[2].Id));
//         // Assert.That(itemPlanningCaseSites[2].PlanningId, Is.EqualTo(plannings[2].Id));
//         // Assert.That(itemPlanningCaseSites[2].MicrotingSdkSiteId, Is.EqualTo(sites[2].Id));
//         // Assert.That(itemPlanningCaseSites[2].MicrotingSdkCaseId, Is.EqualTo(cases[2].Id));
//         // Assert.That(itemPlanningCaseSites[2].Status, Is.EqualTo(66));
//         // Assert.That(itemPlanningCaseSites[2].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(itemPlanningCaseSites[2].MicrotingCheckListSitId, Is.EqualTo(0));
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
//         Assert.That(cases[0].CheckListId, Is.EqualTo(floatingLayerEformId));
//         Assert.That(cases[0].FolderId, Is.Null);
//         Assert.That(cases[0].Status, Is.EqualTo(33));
//         Assert.That(cases[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(cases[1].SiteId, Is.EqualTo(sites[2].Id));
//         // Assert.That(cases[1].CheckListId, Is.EqualTo(alarmeFormid));
//         // Assert.That(cases[1].FolderId, Is.Null);
//         // Assert.That(cases[1].Status, Is.EqualTo(66));
//         // Assert.That(cases[1].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(cases[2].SiteId, Is.EqualTo(sites[2].Id));
//         // Assert.That(cases[2].CheckListId, Is.EqualTo(areaRules[0].EformId));
//         // Assert.That(cases[2].FolderId, Is.Null);
//         // Assert.That(cases[2].Status, Is.EqualTo(66));
//         // Assert.That(cases[2].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//     }
//
//     // Should test the UpdatePlanning method for area rule "03. Gyllebeholdere" for areaRule: 0 with construction, alarm and open container
//     // Adding a nw worker to the list of assigned sites
//     [Test]
//     public async Task UpdatePlanning_AreaRule0AlamAndOpenContainerConstructionAddWorker_ReturnsSuccess()
//     {
//         // Arrange
//         var core = await GetCore();
//         var propertyCreateModel = new PropertyCreateModel
//         {
//             Address = Guid.NewGuid().ToString(),
//             Chr = Guid.NewGuid().ToString(),
//             IndustryCode = Guid.NewGuid().ToString(),
//             Cvr = Guid.NewGuid().ToString(),
//             IsFarm = true,
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
//         var deviceUserModel2 = new DeviceUserModel
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
//         await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel2, core, 1,
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
//         var propertyAssignWorkersModel2 = new PropertyAssignWorkersModel
//         {
//             Assignments =
//             [
//                 new()
//                 {
//                     PropertyId = properties[0].Id,
//                     IsChecked = true
//                 }
//             ],
//             SiteId = sites[3].Id
//         };
//
//         // var userService = Substitute.For<IUserService>();
//         // userService.UserId.Returns(1);
//         await BackendConfigurationAssignmentWorkerServiceHelper.Create(propertyAssignWorkersModel2, core, userService,
//             BackendConfigurationPnDbContext, CaseTemplatePnDbContext, null, Bus);
//
//         var areaTranslation = await BackendConfigurationPnDbContext!.AreaTranslations.FirstAsync(x => x.Name == "03. Gyllebeholdere");
//         var area = await BackendConfigurationPnDbContext.Areas.FirstAsync(x => x.Id == areaTranslation.AreaId);
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
//         var propertyArea = await BackendConfigurationPnDbContext!.AreaProperties.FirstAsync(x => x.PropertyId == properties[0].Id && x.AreaId == area.Id);
//         var danishLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.Name == "Dansk");
//         var areaInitialFields = await BackendConfigurationPnDbContext.AreaInitialFields.Where(x => x.AreaId == area.Id)
//             .ToListAsync();
//         var checkListTranslation = await MicrotingDbContext.CheckListTranslations.FirstAsync(x => x.Text == areaInitialFields.First().EformName);
//
//         AreaRulesCreateModel areaRulesCreateModel = new AreaRulesCreateModel
//         {
//             AreaRules =
//             [
//                 new()
//                 {
//                     TranslatedNames =
//                     [
//                         new()
//                             { Name = "Beholeder 1", Description = "00. Logbøger", Id = danishLanguage.Id }
//                     ],
//                     TypeSpecificFields = new TypeSpecificFields
//                     {
//                         Alarm = AreaRuleT2AlarmsEnum.Yes,
//                         DayOfWeek = 1,
//                         EformId = checkListTranslation.CheckListId,
//                         Type = AreaRuleT2TypesEnum.Open
//                     }
//                 }
//             ],
//             PropertyAreaId = propertyArea.Id
//         };
//
//         await BackendConfigurationAreaRulesServiceHelper.Create(areaRulesCreateModel, core, 1, BackendConfigurationPnDbContext, danishLanguage);
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
//                     SiteId = sites[2].Id
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
//                 Alarm = AreaRuleT2AlarmsEnum.Yes,
//                 DayOfMonth = 0,
//                 DayOfWeek = 0,
//                 HoursAndEnergyEnabled = false,
//                 RepeatEvery = 0,
//                 RepeatType = 1,
//                 Type = AreaRuleT2TypesEnum.Open
//             }
//
//         };
//         await BackendConfigurationAreaRulePlanningsServiceHelper.UpdatePlanning(areaRulePlanningModel,
//             core, 1, BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, null);
//
//         var areaRulePlannings = await BackendConfigurationPnDbContext!.AreaRulePlannings.ToListAsync();
//
//         // Act
//         var areaRulePlanningModel2 = new AreaRulePlanningModel
//         {
//             Id = areaRulePlannings.First().Id,
//             AssignedSites =
//             [
//                 new()
//                 {
//                     Checked = true,
//                     SiteId = sites[2].Id
//                 },
//
//                 new()
//                 {
//                     Checked = true,
//                     SiteId = sites[3].Id
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
//                 Alarm = AreaRuleT2AlarmsEnum.Yes,
//                 DayOfMonth = 0,
//                 DayOfWeek = 0,
//                 HoursAndEnergyEnabled = false,
//                 RepeatEvery = 0,
//                 RepeatType = 1,
//                 Type = AreaRuleT2TypesEnum.Open
//             }
//
//         };
//         await BackendConfigurationAreaRulePlanningsServiceHelper.UpdatePlanning(areaRulePlanningModel2,
//             core, 1, BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, null);
//
//         // Assert
//         var areaRuleTranslations = await BackendConfigurationPnDbContext!.AreaRuleTranslations
//             .Join(BackendConfigurationPnDbContext.AreaRules, atr => atr.AreaRuleId, ar => ar.Id,
//                 (atr, ar) => new { atr, ar }).Where(x => x.ar.PropertyId == properties[0].Id).ToListAsync();
//         var areaProperties = await BackendConfigurationPnDbContext!.AreaProperties.Where(x => x.PropertyId == properties[0].Id).ToListAsync();
//         var folderTranslations = await MicrotingDbContext!.FolderTranslations.ToListAsync();
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
//         var englishLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "en-US");
//         var germanLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "de-DE");
//         /*var alarmeFormid = */await MicrotingDbContext.CheckListTranslations.Where(x => x.Text == "03. Kontrol alarmanlæg gyllebeholder").Select(x => x.CheckListId).FirstAsync();
//         var floatingLayerEformId = await MicrotingDbContext.CheckListTranslations.Where(x => x.Text == "03. Kontrol flydelag").Select(x => x.CheckListId).FirstAsync();
//
//         Assert.That(result, Is.Not.Null);
//         Assert.That(result.Success, Is.EqualTo(true));
//
//         // Assert areaRules
//         Assert.That(areaRules, Is.Not.Null);
//         Assert.That(areaRules.Count, Is.EqualTo(1));
//         Assert.That(areaRules[0].PropertyId, Is.EqualTo(properties[0].Id));
//         Assert.That(areaRules[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
//         Assert.That(areaRules[0].EformName, Is.EqualTo("03. Kontrol konstruktion"));
//
//         // Assert areaRuleTranslations
//         Assert.That(areaRuleTranslations, Is.Not.Null);
//         Assert.That(areaRuleTranslations.Count, Is.EqualTo(1));
//         Assert.That(areaRuleTranslations[0].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
//         Assert.That(areaRuleTranslations[0].atr.LanguageId, Is.EqualTo(1));
//         Assert.That(areaRuleTranslations[0].atr.Name, Is.EqualTo("Beholeder 1"));
//
//         // Assert areaProperties
//         Assert.That(areaProperties, Is.Not.Null);
//         Assert.That(areaProperties.Count, Is.EqualTo(1));
//         Assert.That(areaProperties[0].PropertyId, Is.EqualTo(properties[0].Id));
//         Assert.That(areaProperties[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
//
//         // Assert folder translations
//         Assert.That(folderTranslations, Is.Not.Null);
//         Assert.That(folderTranslations.Count, Is.EqualTo(32));
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
//         Assert.That(folderTranslations[28].Name, Is.EqualTo("03. Gyllebeholdere"));
//         Assert.That(folderTranslations[28].LanguageId, Is.EqualTo(1));
//         Assert.That(folderTranslations[29].Name, Is.EqualTo("03. Slurry tanks"));
//         Assert.That(folderTranslations[29].LanguageId, Is.EqualTo(2));
//         Assert.That(folderTranslations[30].Name, Is.EqualTo("03. Gülletanks"));
//         Assert.That(folderTranslations[30].LanguageId, Is.EqualTo(3));
//         Assert.That(folderTranslations[31].Name, Is.EqualTo("Beholeder 1"));
//         Assert.That(folderTranslations[31].LanguageId, Is.EqualTo(1));
//
//         // Assert AreaRulePlannings
//         Assert.That(areaRulePlannings, Is.Not.Null);
//         Assert.That(areaRulePlannings.Count, Is.EqualTo(1));
//         Assert.That(areaRulePlannings[0].AreaRuleId, Is.EqualTo(areaRules[0].Id));
//         Assert.That(areaRulePlannings[0].ItemPlanningId, Is.EqualTo(plannings[0].Id));
//         // Assert.That(areaRulePlannings[1].ItemPlanningId, Is.EqualTo(plannings[1].Id));
//         // Assert.That(areaRulePlannings[2].ItemPlanningId, Is.EqualTo(plannings[2].Id));
//         Assert.That(areaRulePlannings[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
//         // Assert.That(areaRulePlannings[1].AreaId, Is.EqualTo(areaTranslation.AreaId));
//         // Assert.That(areaRulePlannings[2].AreaId, Is.EqualTo(areaTranslation.AreaId));
//         Assert.That(areaRulePlannings[0].PropertyId, Is.EqualTo(properties[0].Id));
//         // Assert.That(areaRulePlannings[1].PropertyId, Is.EqualTo(properties[0].Id));
//         // Assert.That(areaRulePlannings[2].PropertyId, Is.EqualTo(properties[0].Id));
//         Assert.That(areaRulePlannings[0].ComplianceEnabled, Is.EqualTo(true));
//         // Assert.That(areaRulePlannings[1].ComplianceEnabled, Is.EqualTo(true));
//         // Assert.That(areaRulePlannings[2].ComplianceEnabled, Is.EqualTo(true));
//         Assert.That(areaRulePlannings[0].SendNotifications, Is.EqualTo(true));
//         // Assert.That(areaRulePlannings[1].SendNotifications, Is.EqualTo(true));
//         // Assert.That(areaRulePlannings[2].SendNotifications, Is.EqualTo(true));
//
//         // Assert plannings
//         Assert.That(plannings, Is.Not.Null);
//         Assert.That(plannings.Count, Is.EqualTo(1));
//         Assert.That(plannings[0].RelatedEFormId, Is.EqualTo(floatingLayerEformId));
//         Assert.That(plannings[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         Assert.That(plannings[0].SdkFolderId, Is.EqualTo(folderTranslations[31].FolderId));
//         Assert.That(plannings[0].LastExecutedTime, Is.Not.Null);
//         Assert.That(plannings[0].LastExecutedTime, Is.EqualTo(new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 0,0,0)));
//         var now = DateTime.UtcNow;
//         var nextExecutionTime = new DateTime(now.Year, now.Month, (int)plannings[0].DayOfMonth!, 0, 0, 0).AddMonths(1);
//
//         Assert.That(plannings[0].NextExecutionTime, Is.EqualTo(nextExecutionTime));
//         Assert.That(plannings[0].RepeatEvery, Is.EqualTo(1));
//         Assert.That(plannings[0].RepeatType, Is.EqualTo(RepeatType.Month));
//
//         // Assert.That(plannings[1].RelatedEFormId, Is.EqualTo(alarmeFormid));
//         // Assert.That(plannings[1].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(plannings[1].SdkFolderId, Is.EqualTo(folderTranslations[31].FolderId));
//         // Assert.That(plannings[1].LastExecutedTime, Is.Not.Null);
//         // Assert.That(plannings[1].LastExecutedTime, Is.EqualTo(new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 0,0,0)));
//         // nextExecutionTime =new DateTime(now.Year, now.Month, (int)plannings[0].DayOfMonth!, 0, 0, 0).AddMonths(1);
//         //
//         // Assert.That(plannings[1].NextExecutionTime, Is.EqualTo(nextExecutionTime));
//         // Assert.That(plannings[1].RepeatEvery, Is.EqualTo(1));
//         // Assert.That(plannings[1].RepeatType, Is.EqualTo(RepeatType.Month));
//         //
//         //
//         // Assert.That(plannings[2].RelatedEFormId, Is.EqualTo(areaRules[0].EformId));
//         // Assert.That(plannings[2].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(plannings[2].SdkFolderId, Is.EqualTo(folderTranslations[31].FolderId));
//         // Assert.That(plannings[2].LastExecutedTime, Is.Not.Null);
//         // Assert.That(plannings[2].LastExecutedTime, Is.EqualTo(new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 0,0,0)));
//         // nextExecutionTime =new DateTime(now.Year + 1, 1, (int)plannings[0].DayOfMonth!, 0, 0, 0);
//         //
//         // Assert.That(plannings[2].NextExecutionTime, Is.EqualTo(nextExecutionTime));
//         // Assert.That(plannings[2].RepeatEvery, Is.EqualTo(12));
//         // Assert.That(plannings[2].RepeatType, Is.EqualTo(RepeatType.Month));
//
//         // Assert planningNameTranslations
//         Assert.That(planningNameTranslations, Is.Not.Null);
//         Assert.That(planningNameTranslations.Count, Is.EqualTo(3));
//         Assert.That(planningNameTranslations[0].Name, Is.EqualTo("Beholeder 1: Flydelag"));
//         Assert.That(planningNameTranslations[0].LanguageId, Is.EqualTo(areaRuleTranslations[0].atr.LanguageId));
//         Assert.That(planningNameTranslations[0].PlanningId, Is.EqualTo(plannings[0].Id));
//         Assert.That(planningNameTranslations[1].Name, Is.EqualTo(": Floating layer"));
//         Assert.That(planningNameTranslations[1].LanguageId, Is.EqualTo(englishLanguage.Id));
//         Assert.That(planningNameTranslations[1].PlanningId, Is.EqualTo(plannings[0].Id));
//         Assert.That(planningNameTranslations[2].Name, Is.EqualTo(": Schwimmende Ebene"));
//         Assert.That(planningNameTranslations[2].LanguageId, Is.EqualTo(germanLanguage.Id));
//         Assert.That(planningNameTranslations[2].PlanningId, Is.EqualTo(plannings[0].Id));
//         // Assert.That(planningNameTranslations[3].Name, Is.EqualTo("Beholeder 1: Alarm"));
//         // Assert.That(planningNameTranslations[3].LanguageId, Is.EqualTo(areaRuleTranslations[0].atr.LanguageId));
//         // Assert.That(planningNameTranslations[3].PlanningId, Is.EqualTo(plannings[1].Id));
//         // Assert.That(planningNameTranslations[4].Name, Is.EqualTo(": Alarm"));
//         // Assert.That(planningNameTranslations[4].LanguageId, Is.EqualTo(englishLanguage.Id));
//         // Assert.That(planningNameTranslations[4].PlanningId, Is.EqualTo(plannings[1].Id));
//         // Assert.That(planningNameTranslations[5].Name, Is.EqualTo(": Alarm"));
//         // Assert.That(planningNameTranslations[5].LanguageId, Is.EqualTo(germanLanguage.Id));
//         // Assert.That(planningNameTranslations[5].PlanningId, Is.EqualTo(plannings[1].Id));
//         // Assert.That(planningNameTranslations[6].Name, Is.EqualTo("Beholeder 1: Konstruktion"));
//         // Assert.That(planningNameTranslations[6].LanguageId, Is.EqualTo(areaRuleTranslations[0].atr.LanguageId));
//         // Assert.That(planningNameTranslations[6].PlanningId, Is.EqualTo(plannings[2].Id));
//         // Assert.That(planningNameTranslations[7].Name, Is.EqualTo(": Construction"));
//         // Assert.That(planningNameTranslations[7].LanguageId, Is.EqualTo(englishLanguage.Id));
//         // Assert.That(planningNameTranslations[7].PlanningId, Is.EqualTo(plannings[2].Id));
//         // Assert.That(planningNameTranslations[8].Name, Is.EqualTo(": Konstruktion"));
//         // Assert.That(planningNameTranslations[8].LanguageId, Is.EqualTo(germanLanguage.Id));
//         // Assert.That(planningNameTranslations[8].PlanningId, Is.EqualTo(plannings[2].Id));
//
//         // Assert planningSites
//         Assert.That(planningSites, Is.Not.Null);
//         Assert.That(planningSites.Count, Is.EqualTo(2));
//         Assert.That(planningSites[0].AreaRulePlanningsId, Is.EqualTo(areaRulePlannings[0].Id));
//         Assert.That(planningSites[0].AreaId, Is.EqualTo(area.Id));
//         Assert.That(planningSites[0].SiteId, Is.EqualTo(sites[2].Id));
//         Assert.That(planningSites[0].Status, Is.EqualTo(33));
//         Assert.That(planningSites[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         Assert.That(planningSites[1].AreaRulePlanningsId, Is.EqualTo(areaRulePlannings[0].Id));
//         Assert.That(planningSites[1].AreaId, Is.EqualTo(area.Id));
//         Assert.That(planningSites[1].SiteId, Is.EqualTo(sites[3].Id));
//         Assert.That(planningSites[1].Status, Is.EqualTo(33));
//         Assert.That(planningSites[1].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(planningSites[2].AreaRulePlanningsId, Is.EqualTo(areaRulePlannings[2].Id));
//         // Assert.That(planningSites[2].AreaId, Is.EqualTo(area.Id));
//         // Assert.That(planningSites[2].SiteId, Is.EqualTo(sites[2].Id));
//         // Assert.That(planningSites[2].Status, Is.EqualTo(33));
//         // Assert.That(planningSites[2].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(planningSites[3].AreaRulePlanningsId, Is.EqualTo(areaRulePlannings[0].Id));
//         // Assert.That(planningSites[3].AreaId, Is.EqualTo(area.Id));
//         // Assert.That(planningSites[3].SiteId, Is.EqualTo(sites[3].Id));
//         // Assert.That(planningSites[3].Status, Is.EqualTo(33));
//         // Assert.That(planningSites[3].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(planningSites[4].AreaRulePlanningsId, Is.EqualTo(areaRulePlannings[1].Id));
//         // Assert.That(planningSites[4].AreaId, Is.EqualTo(area.Id));
//         // Assert.That(planningSites[4].SiteId, Is.EqualTo(sites[3].Id));
//         // Assert.That(planningSites[4].Status, Is.EqualTo(33));
//         // Assert.That(planningSites[4].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(planningSites[5].AreaRulePlanningsId, Is.EqualTo(areaRulePlannings[2].Id));
//         // Assert.That(planningSites[5].AreaId, Is.EqualTo(area.Id));
//         // Assert.That(planningSites[5].SiteId, Is.EqualTo(sites[3].Id));
//         // Assert.That(planningSites[5].Status, Is.EqualTo(33));
//         // Assert.That(planningSites[5].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//
//         // Assert itemPlanningSites
//         Assert.That(itemPlanningSites, Is.Not.Null);
//         Assert.That(itemPlanningSites.Count, Is.EqualTo(2));
//         Assert.That(itemPlanningSites[0].PlanningId, Is.EqualTo(plannings[0].Id));
//         Assert.That(itemPlanningSites[0].SiteId, Is.EqualTo(sites[2].Id));
//         Assert.That(itemPlanningSites[0].LastExecutedTime, Is.Null);
//         Assert.That(itemPlanningSites[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         Assert.That(itemPlanningSites[1].PlanningId, Is.EqualTo(plannings[0].Id));
//         Assert.That(itemPlanningSites[1].SiteId, Is.EqualTo(sites[3].Id));
//         Assert.That(itemPlanningSites[1].LastExecutedTime, Is.Null);
//         Assert.That(itemPlanningSites[1].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(itemPlanningSites[2].PlanningId, Is.EqualTo(plannings[2].Id));
//         // Assert.That(itemPlanningSites[2].SiteId, Is.EqualTo(sites[2].Id));
//         // Assert.That(itemPlanningSites[2].LastExecutedTime, Is.Null);
//         // Assert.That(itemPlanningSites[2].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(itemPlanningSites[3].PlanningId, Is.EqualTo(plannings[0].Id));
//         // Assert.That(itemPlanningSites[3].SiteId, Is.EqualTo(sites[3].Id));
//         // Assert.That(itemPlanningSites[3].LastExecutedTime, Is.Null);
//         // Assert.That(itemPlanningSites[3].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(itemPlanningSites[4].PlanningId, Is.EqualTo(plannings[1].Id));
//         // Assert.That(itemPlanningSites[4].SiteId, Is.EqualTo(sites[3].Id));
//         // Assert.That(itemPlanningSites[4].LastExecutedTime, Is.Null);
//         // Assert.That(itemPlanningSites[4].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(itemPlanningSites[5].PlanningId, Is.EqualTo(plannings[2].Id));
//         // Assert.That(itemPlanningSites[5].SiteId, Is.EqualTo(sites[3].Id));
//         // Assert.That(itemPlanningSites[5].LastExecutedTime, Is.Null);
//         // Assert.That(itemPlanningSites[5].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//
//         // Assert itemPlanningCases
//         Assert.That(itemPlanningCases, Is.Not.Null);
//         Assert.That(itemPlanningCases.Count, Is.EqualTo(1));
//         Assert.That(itemPlanningCases[0].PlanningId, Is.EqualTo(plannings[0].Id));
//         // Assert.That(itemPlanningCases[1].PlanningId, Is.EqualTo(plannings[1].Id));
//         // Assert.That(itemPlanningCases[2].PlanningId, Is.EqualTo(plannings[2].Id));
//
//         // Assert itemPlanningCaseSites
//         Assert.That(itemPlanningCaseSites, Is.Not.Null);
//         Assert.That(itemPlanningCaseSites.Count, Is.EqualTo(2));
//         Assert.That(itemPlanningCaseSites[0].PlanningCaseId, Is.EqualTo(itemPlanningCases[0].Id));
//         Assert.That(itemPlanningCaseSites[0].PlanningId, Is.EqualTo(plannings[0].Id));
//         Assert.That(itemPlanningCaseSites[0].MicrotingSdkSiteId, Is.EqualTo(sites[2].Id));
//         Assert.That(itemPlanningCaseSites[0].MicrotingSdkCaseId, Is.EqualTo(cases[0].Id));
//         Assert.That(itemPlanningCaseSites[0].Status, Is.EqualTo(66));
//         Assert.That(itemPlanningCaseSites[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         Assert.That(itemPlanningCaseSites[0].MicrotingCheckListSitId, Is.EqualTo(0));
//         Assert.That(itemPlanningCaseSites[1].PlanningCaseId, Is.EqualTo(itemPlanningCases[0].Id));
//         Assert.That(itemPlanningCaseSites[1].PlanningId, Is.EqualTo(plannings[0].Id));
//         Assert.That(itemPlanningCaseSites[1].MicrotingSdkSiteId, Is.EqualTo(sites[3].Id));
//         Assert.That(itemPlanningCaseSites[1].MicrotingSdkCaseId, Is.EqualTo(cases[1].Id));
//         Assert.That(itemPlanningCaseSites[1].Status, Is.EqualTo(66));
//         Assert.That(itemPlanningCaseSites[1].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         Assert.That(itemPlanningCaseSites[1].MicrotingCheckListSitId, Is.EqualTo(0));
//         // Assert.That(itemPlanningCaseSites[2].PlanningCaseId, Is.EqualTo(itemPlanningCases[2].Id));
//         // Assert.That(itemPlanningCaseSites[2].PlanningId, Is.EqualTo(plannings[2].Id));
//         // Assert.That(itemPlanningCaseSites[2].MicrotingSdkSiteId, Is.EqualTo(sites[2].Id));
//         // Assert.That(itemPlanningCaseSites[2].MicrotingSdkCaseId, Is.EqualTo(cases[2].Id));
//         // Assert.That(itemPlanningCaseSites[2].Status, Is.EqualTo(66));
//         // Assert.That(itemPlanningCaseSites[2].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(itemPlanningCaseSites[2].MicrotingCheckListSitId, Is.EqualTo(0));
//         // Assert.That(itemPlanningCaseSites[3].PlanningCaseId, Is.EqualTo(itemPlanningCases[0].Id));
//         // Assert.That(itemPlanningCaseSites[3].PlanningId, Is.EqualTo(plannings[0].Id));
//         // Assert.That(itemPlanningCaseSites[3].MicrotingSdkSiteId, Is.EqualTo(sites[3].Id));
//         // Assert.That(itemPlanningCaseSites[3].MicrotingSdkCaseId, Is.EqualTo(cases[3].Id));
//         // Assert.That(itemPlanningCaseSites[3].Status, Is.EqualTo(66));
//         // Assert.That(itemPlanningCaseSites[3].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(itemPlanningCaseSites[3].MicrotingCheckListSitId, Is.EqualTo(0));
//         // Assert.That(itemPlanningCaseSites[4].PlanningCaseId, Is.EqualTo(itemPlanningCases[1].Id));
//         // Assert.That(itemPlanningCaseSites[4].PlanningId, Is.EqualTo(plannings[1].Id));
//         // Assert.That(itemPlanningCaseSites[4].MicrotingSdkSiteId, Is.EqualTo(sites[3].Id));
//         // Assert.That(itemPlanningCaseSites[4].MicrotingSdkCaseId, Is.EqualTo(cases[4].Id));
//         // Assert.That(itemPlanningCaseSites[4].Status, Is.EqualTo(66));
//         // Assert.That(itemPlanningCaseSites[4].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(itemPlanningCaseSites[4].MicrotingCheckListSitId, Is.EqualTo(0));
//         // Assert.That(itemPlanningCaseSites[5].PlanningCaseId, Is.EqualTo(itemPlanningCases[2].Id));
//         // Assert.That(itemPlanningCaseSites[5].PlanningId, Is.EqualTo(plannings[2].Id));
//         // Assert.That(itemPlanningCaseSites[5].MicrotingSdkSiteId, Is.EqualTo(sites[3].Id));
//         // Assert.That(itemPlanningCaseSites[5].MicrotingSdkCaseId, Is.EqualTo(cases[5].Id));
//         // Assert.That(itemPlanningCaseSites[5].Status, Is.EqualTo(66));
//         // Assert.That(itemPlanningCaseSites[5].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(itemPlanningCaseSites[5].MicrotingCheckListSitId, Is.EqualTo(0));
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
//         Assert.That(cases.Count, Is.EqualTo(2));
//         Assert.That(cases[0].SiteId, Is.EqualTo(sites[2].Id));
//         Assert.That(cases[0].CheckListId, Is.EqualTo(floatingLayerEformId));
//         Assert.That(cases[0].FolderId, Is.Null);
//         Assert.That(cases[0].Status, Is.EqualTo(33));
//         Assert.That(cases[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         Assert.That(cases[1].SiteId, Is.EqualTo(sites[3].Id));
//         Assert.That(cases[1].CheckListId, Is.EqualTo(floatingLayerEformId));
//         Assert.That(cases[1].FolderId, Is.Null);
//         Assert.That(cases[1].Status, Is.EqualTo(66));
//         Assert.That(cases[1].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(cases[2].SiteId, Is.EqualTo(sites[2].Id));
//         // Assert.That(cases[2].CheckListId, Is.EqualTo(areaRules[0].EformId));
//         // Assert.That(cases[2].FolderId, Is.Null);
//         // Assert.That(cases[2].Status, Is.EqualTo(66));
//         // Assert.That(cases[2].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(cases[3].SiteId, Is.EqualTo(sites[3].Id));
//         // Assert.That(cases[3].CheckListId, Is.EqualTo(floatingLayerEformId));
//         // Assert.That(cases[3].FolderId, Is.Null);
//         // Assert.That(cases[3].Status, Is.EqualTo(66));
//         // Assert.That(cases[3].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(cases[4].SiteId, Is.EqualTo(sites[3].Id));
//         // Assert.That(cases[4].CheckListId, Is.EqualTo(alarmeFormid));
//         // Assert.That(cases[4].FolderId, Is.Null);
//         // Assert.That(cases[4].Status, Is.EqualTo(66));
//         // Assert.That(cases[4].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(cases[5].SiteId, Is.EqualTo(sites[3].Id));
//         // Assert.That(cases[5].CheckListId, Is.EqualTo(areaRules[0].EformId));
//         // Assert.That(cases[5].FolderId, Is.Null);
//         // Assert.That(cases[5].Status, Is.EqualTo(66));
//         // Assert.That(cases[5].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//     }
//
//     // Should test the UpdatePlanning method for area rule "03. Gyllebeholdere" for areaRule: 0 with construction, alarm and open container
//     // Adding a nw worker to the list of assigned sites and deactivate it
//     [Test]
//     public async Task UpdatePlanning_AreaRule0AlamAndOpenContainerConstructionAddWorkerAndDeactivate_ReturnsSuccess()
//     {
//         // Arrange
//         var core = await GetCore();
//         var propertyCreateModel = new PropertyCreateModel
//         {
//             Address = Guid.NewGuid().ToString(),
//             Chr = Guid.NewGuid().ToString(),
//             IndustryCode = Guid.NewGuid().ToString(),
//             Cvr = Guid.NewGuid().ToString(),
//             IsFarm = true,
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
//         var deviceUserModel2 = new DeviceUserModel
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
//         await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel2, core, 1,
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
//         var propertyAssignWorkersModel2 = new PropertyAssignWorkersModel
//         {
//             Assignments =
//             [
//                 new()
//                 {
//                     PropertyId = properties[0].Id,
//                     IsChecked = true
//                 }
//             ],
//             SiteId = sites[3].Id
//         };
//
//         // var userService = Substitute.For<IUserService>();
//         // userService.UserId.Returns(1);
//         await BackendConfigurationAssignmentWorkerServiceHelper.Create(propertyAssignWorkersModel2, core, userService,
//             BackendConfigurationPnDbContext, CaseTemplatePnDbContext, null, Bus);
//
//         var areaTranslation = await BackendConfigurationPnDbContext!.AreaTranslations.FirstAsync(x => x.Name == "03. Gyllebeholdere");
//         var area = await BackendConfigurationPnDbContext.Areas.FirstAsync(x => x.Id == areaTranslation.AreaId);
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
//         var propertyArea = await BackendConfigurationPnDbContext!.AreaProperties.FirstAsync(x => x.PropertyId == properties[0].Id && x.AreaId == area.Id);
//         var danishLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.Name == "Dansk");
//         var areaInitialFields = await BackendConfigurationPnDbContext.AreaInitialFields.Where(x => x.AreaId == area.Id)
//             .ToListAsync();
//         var checkListTranslation = await MicrotingDbContext.CheckListTranslations.FirstAsync(x => x.Text == areaInitialFields.First().EformName);
//
//         AreaRulesCreateModel areaRulesCreateModel = new AreaRulesCreateModel
//         {
//             AreaRules =
//             [
//                 new()
//                 {
//                     TranslatedNames =
//                     [
//                         new()
//                             { Name = "Beholeder 1", Description = "00. Logbøger", Id = danishLanguage.Id }
//                     ],
//                     TypeSpecificFields = new TypeSpecificFields
//                     {
//                         Alarm = AreaRuleT2AlarmsEnum.Yes,
//                         DayOfWeek = 1,
//                         EformId = checkListTranslation.CheckListId,
//                         Type = AreaRuleT2TypesEnum.Open
//                     }
//                 }
//             ],
//             PropertyAreaId = propertyArea.Id
//         };
//
//         await BackendConfigurationAreaRulesServiceHelper.Create(areaRulesCreateModel, core, 1, BackendConfigurationPnDbContext, danishLanguage);
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
//                     SiteId = sites[2].Id
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
//                 Alarm = AreaRuleT2AlarmsEnum.Yes,
//                 DayOfMonth = 0,
//                 DayOfWeek = 0,
//                 HoursAndEnergyEnabled = false,
//                 RepeatEvery = 0,
//                 RepeatType = 1,
//                 Type = AreaRuleT2TypesEnum.Open
//             }
//
//         };
//         await BackendConfigurationAreaRulePlanningsServiceHelper.UpdatePlanning(areaRulePlanningModel,
//             core, 1, BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, null);
//
//         var areaRulePlannings = await BackendConfigurationPnDbContext!.AreaRulePlannings.ToListAsync();
//         // Act
//
//         var areaRulePlanningModel2 = new AreaRulePlanningModel
//         {
//             Id = areaRulePlannings.First().Id,
//             AssignedSites =
//             [
//                 new()
//                 {
//                     Checked = true,
//                     SiteId = sites[2].Id
//                 },
//
//                 new()
//                 {
//                     Checked = true,
//                     SiteId = sites[3].Id
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
//                 Alarm = AreaRuleT2AlarmsEnum.Yes,
//                 DayOfMonth = 0,
//                 DayOfWeek = 0,
//                 HoursAndEnergyEnabled = false,
//                 RepeatEvery = 0,
//                 RepeatType = 1,
//                 Type = AreaRuleT2TypesEnum.Open
//             }
//
//         };
//         await BackendConfigurationAreaRulePlanningsServiceHelper.UpdatePlanning(areaRulePlanningModel2,
//             core, 1, BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, null);
//
//         var areaRulePlanningModel3 = new AreaRulePlanningModel
//         {
//             Id = areaRulePlannings.First().Id,
//             AssignedSites =
//             [
//                 new()
//                 {
//                     Checked = true,
//                     SiteId = sites[2].Id
//                 },
//
//                 new()
//                 {
//                     Checked = true,
//                     SiteId = sites[3].Id
//                 }
//             ],
//             RuleId = areaRules[0].Id,
//             ComplianceEnabled = true,
//             PropertyId = properties[0].Id,
//             Status = false,
//             SendNotifications = true,
//             StartDate = DateTime.UtcNow,
//             TypeSpecificFields = new AreaRuleTypePlanningModel
//             {
//                 Alarm = AreaRuleT2AlarmsEnum.Yes,
//                 DayOfMonth = 0,
//                 DayOfWeek = 0,
//                 HoursAndEnergyEnabled = false,
//                 RepeatEvery = 0,
//                 RepeatType = 1,
//                 Type = AreaRuleT2TypesEnum.Open
//             }
//
//         };
//         await BackendConfigurationAreaRulePlanningsServiceHelper.UpdatePlanning(areaRulePlanningModel3,
//             core, 1, BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, null);
//
//         // Assert
//         var areaRuleTranslations = await BackendConfigurationPnDbContext!.AreaRuleTranslations
//             .Join(BackendConfigurationPnDbContext.AreaRules, atr => atr.AreaRuleId, ar => ar.Id,
//                 (atr, ar) => new { atr, ar }).Where(x => x.ar.PropertyId == properties[0].Id).ToListAsync();
//         var areaProperties = await BackendConfigurationPnDbContext!.AreaProperties.Where(x => x.PropertyId == properties[0].Id).ToListAsync();
//         var folderTranslations = await MicrotingDbContext!.FolderTranslations.ToListAsync();
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
//         var englishLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "en-US");
//         var germanLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "de-DE");
//         /*var alarmeFormid = */await MicrotingDbContext.CheckListTranslations.Where(x => x.Text == "03. Kontrol alarmanlæg gyllebeholder").Select(x => x.CheckListId).FirstAsync();
//         var floatingLayerEformId = await MicrotingDbContext.CheckListTranslations.Where(x => x.Text == "03. Kontrol flydelag").Select(x => x.CheckListId).FirstAsync();
//
//         Assert.That(result, Is.Not.Null);
//         Assert.That(result.Success, Is.EqualTo(true));
//
//         // Assert areaRules
//         Assert.That(areaRules, Is.Not.Null);
//         Assert.That(areaRules.Count, Is.EqualTo(1));
//         Assert.That(areaRules[0].PropertyId, Is.EqualTo(properties[0].Id));
//         Assert.That(areaRules[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
//         Assert.That(areaRules[0].EformName, Is.EqualTo("03. Kontrol konstruktion"));
//
//         // Assert areaRuleTranslations
//         Assert.That(areaRuleTranslations, Is.Not.Null);
//         Assert.That(areaRuleTranslations.Count, Is.EqualTo(1));
//         Assert.That(areaRuleTranslations[0].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
//         Assert.That(areaRuleTranslations[0].atr.LanguageId, Is.EqualTo(1));
//         Assert.That(areaRuleTranslations[0].atr.Name, Is.EqualTo("Beholeder 1"));
//
//         // Assert areaProperties
//         Assert.That(areaProperties, Is.Not.Null);
//         Assert.That(areaProperties.Count, Is.EqualTo(1));
//         Assert.That(areaProperties[0].PropertyId, Is.EqualTo(properties[0].Id));
//         Assert.That(areaProperties[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
//
//         // Assert folder translations
//         Assert.That(folderTranslations, Is.Not.Null);
//         Assert.That(folderTranslations.Count, Is.EqualTo(32));
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
//         Assert.That(folderTranslations[28].Name, Is.EqualTo("03. Gyllebeholdere"));
//         Assert.That(folderTranslations[28].LanguageId, Is.EqualTo(1));
//         Assert.That(folderTranslations[29].Name, Is.EqualTo("03. Slurry tanks"));
//         Assert.That(folderTranslations[29].LanguageId, Is.EqualTo(2));
//         Assert.That(folderTranslations[30].Name, Is.EqualTo("03. Gülletanks"));
//         Assert.That(folderTranslations[30].LanguageId, Is.EqualTo(3));
//         Assert.That(folderTranslations[31].Name, Is.EqualTo("Beholeder 1"));
//         Assert.That(folderTranslations[31].LanguageId, Is.EqualTo(1));
//
//         // Assert AreaRulePlannings
//         Assert.That(areaRulePlannings, Is.Not.Null);
//         Assert.That(areaRulePlannings.Count, Is.EqualTo(1));
//         Assert.That(areaRulePlannings[0].AreaRuleId, Is.EqualTo(areaRules[0].Id));
//         Assert.That(areaRulePlannings[0].ItemPlanningId, Is.EqualTo(0));
//         // Assert.That(areaRulePlannings[1].ItemPlanningId, Is.EqualTo(0));
//         // Assert.That(areaRulePlannings[2].ItemPlanningId, Is.EqualTo(0));
//         Assert.That(areaRulePlannings[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
//         // Assert.That(areaRulePlannings[1].AreaId, Is.EqualTo(areaTranslation.AreaId));
//         // Assert.That(areaRulePlannings[2].AreaId, Is.EqualTo(areaTranslation.AreaId));
//         Assert.That(areaRulePlannings[0].PropertyId, Is.EqualTo(properties[0].Id));
//         // Assert.That(areaRulePlannings[1].PropertyId, Is.EqualTo(properties[0].Id));
//         // Assert.That(areaRulePlannings[2].PropertyId, Is.EqualTo(properties[0].Id));
//         Assert.That(areaRulePlannings[0].ComplianceEnabled, Is.EqualTo(true));
//         // Assert.That(areaRulePlannings[1].ComplianceEnabled, Is.EqualTo(true));
//         // Assert.That(areaRulePlannings[2].ComplianceEnabled, Is.EqualTo(true));
//         Assert.That(areaRulePlannings[0].SendNotifications, Is.EqualTo(true));
//         // Assert.That(areaRulePlannings[1].SendNotifications, Is.EqualTo(true));
//         // Assert.That(areaRulePlannings[2].SendNotifications, Is.EqualTo(true));
//
//         // Assert plannings
//         Assert.That(plannings, Is.Not.Null);
//         Assert.That(plannings.Count, Is.EqualTo(1));
//         Assert.That(plannings[0].RelatedEFormId, Is.EqualTo(floatingLayerEformId));
//         Assert.That(plannings[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
//         Assert.That(plannings[0].SdkFolderId, Is.EqualTo(folderTranslations[31].FolderId));
//         Assert.That(plannings[0].LastExecutedTime, Is.Not.Null);
//         Assert.That(plannings[0].LastExecutedTime, Is.EqualTo(new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 0,0,0)));
//         var now = DateTime.UtcNow;
//         var nextExecutionTime = new DateTime(now.Year, now.Month, (int)plannings[0].DayOfMonth!, 0, 0, 0).AddMonths(1);
//
//         Assert.That(plannings[0].NextExecutionTime, Is.EqualTo(nextExecutionTime));
//         Assert.That(plannings[0].RepeatEvery, Is.EqualTo(1));
//         Assert.That(plannings[0].RepeatType, Is.EqualTo(RepeatType.Month));
//
//         // Assert.That(plannings[1].RelatedEFormId, Is.EqualTo(alarmeFormid));
//         // Assert.That(plannings[1].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
//         // Assert.That(plannings[1].SdkFolderId, Is.EqualTo(folderTranslations[31].FolderId));
//         // Assert.That(plannings[1].LastExecutedTime, Is.Not.Null);
//         // Assert.That(plannings[1].LastExecutedTime, Is.EqualTo(new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 0,0,0)));
//         // nextExecutionTime =new DateTime(now.Year, now.Month, (int)plannings[0].DayOfMonth!, 0, 0, 0).AddMonths(1);
//         //
//         // Assert.That(plannings[1].NextExecutionTime, Is.EqualTo(nextExecutionTime));
//         // Assert.That(plannings[1].RepeatEvery, Is.EqualTo(1));
//         // Assert.That(plannings[1].RepeatType, Is.EqualTo(RepeatType.Month));
//         //
//         //
//         // Assert.That(plannings[2].RelatedEFormId, Is.EqualTo(areaRules[0].EformId));
//         // Assert.That(plannings[2].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
//         // Assert.That(plannings[2].SdkFolderId, Is.EqualTo(folderTranslations[31].FolderId));
//         // Assert.That(plannings[2].LastExecutedTime, Is.Not.Null);
//         // Assert.That(plannings[2].LastExecutedTime, Is.EqualTo(new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 0,0,0)));
//         // nextExecutionTime =new DateTime(now.Year + 1, 1, (int)plannings[0].DayOfMonth!, 0, 0, 0);
//         //
//         // Assert.That(plannings[2].NextExecutionTime, Is.EqualTo(nextExecutionTime));
//         // Assert.That(plannings[2].RepeatEvery, Is.EqualTo(12));
//         // Assert.That(plannings[2].RepeatType, Is.EqualTo(RepeatType.Month));
//
//         // Assert planningNameTranslations
//         Assert.That(planningNameTranslations, Is.Not.Null);
//         Assert.That(planningNameTranslations.Count, Is.EqualTo(3));
//         Assert.That(planningNameTranslations[0].Name, Is.EqualTo("Beholeder 1: Flydelag"));
//         Assert.That(planningNameTranslations[0].LanguageId, Is.EqualTo(areaRuleTranslations[0].atr.LanguageId));
//         Assert.That(planningNameTranslations[0].PlanningId, Is.EqualTo(plannings[0].Id));
//         Assert.That(planningNameTranslations[1].Name, Is.EqualTo(": Floating layer"));
//         Assert.That(planningNameTranslations[1].LanguageId, Is.EqualTo(englishLanguage.Id));
//         Assert.That(planningNameTranslations[1].PlanningId, Is.EqualTo(plannings[0].Id));
//         Assert.That(planningNameTranslations[2].Name, Is.EqualTo(": Schwimmende Ebene"));
//         Assert.That(planningNameTranslations[2].LanguageId, Is.EqualTo(germanLanguage.Id));
//         Assert.That(planningNameTranslations[2].PlanningId, Is.EqualTo(plannings[0].Id));
//         // Assert.That(planningNameTranslations[3].Name, Is.EqualTo("Beholeder 1: Alarm"));
//         // Assert.That(planningNameTranslations[3].LanguageId, Is.EqualTo(areaRuleTranslations[0].atr.LanguageId));
//         // Assert.That(planningNameTranslations[3].PlanningId, Is.EqualTo(plannings[1].Id));
//         // Assert.That(planningNameTranslations[4].Name, Is.EqualTo(": Alarm"));
//         // Assert.That(planningNameTranslations[4].LanguageId, Is.EqualTo(englishLanguage.Id));
//         // Assert.That(planningNameTranslations[4].PlanningId, Is.EqualTo(plannings[1].Id));
//         // Assert.That(planningNameTranslations[5].Name, Is.EqualTo(": Alarm"));
//         // Assert.That(planningNameTranslations[5].LanguageId, Is.EqualTo(germanLanguage.Id));
//         // Assert.That(planningNameTranslations[5].PlanningId, Is.EqualTo(plannings[1].Id));
//         // Assert.That(planningNameTranslations[6].Name, Is.EqualTo("Beholeder 1: Konstruktion"));
//         // Assert.That(planningNameTranslations[6].LanguageId, Is.EqualTo(areaRuleTranslations[0].atr.LanguageId));
//         // Assert.That(planningNameTranslations[6].PlanningId, Is.EqualTo(plannings[2].Id));
//         // Assert.That(planningNameTranslations[7].Name, Is.EqualTo(": Construction"));
//         // Assert.That(planningNameTranslations[7].LanguageId, Is.EqualTo(englishLanguage.Id));
//         // Assert.That(planningNameTranslations[7].PlanningId, Is.EqualTo(plannings[2].Id));
//         // Assert.That(planningNameTranslations[8].Name, Is.EqualTo(": Konstruktion"));
//         // Assert.That(planningNameTranslations[8].LanguageId, Is.EqualTo(germanLanguage.Id));
//         // Assert.That(planningNameTranslations[8].PlanningId, Is.EqualTo(plannings[2].Id));
//
//         // Assert planningSites
//         Assert.That(planningSites, Is.Not.Null);
//         Assert.That(planningSites.Count, Is.EqualTo(2));
//         Assert.That(planningSites[0].AreaRulePlanningsId, Is.EqualTo(areaRulePlannings[0].Id));
//         Assert.That(planningSites[0].AreaId, Is.EqualTo(area.Id));
//         Assert.That(planningSites[0].SiteId, Is.EqualTo(sites[2].Id));
//         Assert.That(planningSites[0].Status, Is.EqualTo(0));
//         Assert.That(planningSites[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         Assert.That(planningSites[1].AreaRulePlanningsId, Is.EqualTo(areaRulePlannings[0].Id));
//         Assert.That(planningSites[1].AreaId, Is.EqualTo(area.Id));
//         Assert.That(planningSites[1].SiteId, Is.EqualTo(sites[3].Id));
//         Assert.That(planningSites[1].Status, Is.EqualTo(0));
//         Assert.That(planningSites[1].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(planningSites[2].AreaRulePlanningsId, Is.EqualTo(areaRulePlannings[2].Id));
//         // Assert.That(planningSites[2].AreaId, Is.EqualTo(area.Id));
//         // Assert.That(planningSites[2].SiteId, Is.EqualTo(sites[2].Id));
//         // Assert.That(planningSites[2].Status, Is.EqualTo(0));
//         // Assert.That(planningSites[2].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(planningSites[3].AreaRulePlanningsId, Is.EqualTo(areaRulePlannings[0].Id));
//         // Assert.That(planningSites[3].AreaId, Is.EqualTo(area.Id));
//         // Assert.That(planningSites[3].SiteId, Is.EqualTo(sites[3].Id));
//         // Assert.That(planningSites[3].Status, Is.EqualTo(0));
//         // Assert.That(planningSites[3].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(planningSites[4].AreaRulePlanningsId, Is.EqualTo(areaRulePlannings[1].Id));
//         // Assert.That(planningSites[4].AreaId, Is.EqualTo(area.Id));
//         // Assert.That(planningSites[4].SiteId, Is.EqualTo(sites[3].Id));
//         // Assert.That(planningSites[4].Status, Is.EqualTo(0));
//         // Assert.That(planningSites[4].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(planningSites[5].AreaRulePlanningsId, Is.EqualTo(areaRulePlannings[2].Id));
//         // Assert.That(planningSites[5].AreaId, Is.EqualTo(area.Id));
//         // Assert.That(planningSites[5].SiteId, Is.EqualTo(sites[3].Id));
//         // Assert.That(planningSites[5].Status, Is.EqualTo(0));
//         // Assert.That(planningSites[5].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//
//         // Assert itemPlanningSites
//         Assert.That(itemPlanningSites, Is.Not.Null);
//         Assert.That(itemPlanningSites.Count, Is.EqualTo(2));
//         Assert.That(itemPlanningSites[0].PlanningId, Is.EqualTo(plannings[0].Id));
//         Assert.That(itemPlanningSites[0].SiteId, Is.EqualTo(sites[2].Id));
//         Assert.That(itemPlanningSites[0].LastExecutedTime, Is.Null);
//         Assert.That(itemPlanningSites[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
//         Assert.That(itemPlanningSites[1].PlanningId, Is.EqualTo(plannings[0].Id));
//         Assert.That(itemPlanningSites[1].SiteId, Is.EqualTo(sites[3].Id));
//         Assert.That(itemPlanningSites[1].LastExecutedTime, Is.Null);
//         Assert.That(itemPlanningSites[1].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
//         // Assert.That(itemPlanningSites[2].PlanningId, Is.EqualTo(plannings[2].Id));
//         // Assert.That(itemPlanningSites[2].SiteId, Is.EqualTo(sites[2].Id));
//         // Assert.That(itemPlanningSites[2].LastExecutedTime, Is.Null);
//         // Assert.That(itemPlanningSites[2].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
//         // Assert.That(itemPlanningSites[3].PlanningId, Is.EqualTo(plannings[0].Id));
//         // Assert.That(itemPlanningSites[3].SiteId, Is.EqualTo(sites[3].Id));
//         // Assert.That(itemPlanningSites[3].LastExecutedTime, Is.Null);
//         // Assert.That(itemPlanningSites[3].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
//         // Assert.That(itemPlanningSites[4].PlanningId, Is.EqualTo(plannings[1].Id));
//         // Assert.That(itemPlanningSites[4].SiteId, Is.EqualTo(sites[3].Id));
//         // Assert.That(itemPlanningSites[4].LastExecutedTime, Is.Null);
//         // Assert.That(itemPlanningSites[4].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
//         // Assert.That(itemPlanningSites[5].PlanningId, Is.EqualTo(plannings[2].Id));
//         // Assert.That(itemPlanningSites[5].SiteId, Is.EqualTo(sites[3].Id));
//         // Assert.That(itemPlanningSites[5].LastExecutedTime, Is.Null);
//         // Assert.That(itemPlanningSites[5].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
//
//         // Assert itemPlanningCases
//         Assert.That(itemPlanningCases, Is.Not.Null);
//         Assert.That(itemPlanningCases.Count, Is.EqualTo(1));
//         Assert.That(itemPlanningCases[0].PlanningId, Is.EqualTo(plannings[0].Id));
//         // Assert.That(itemPlanningCases[1].PlanningId, Is.EqualTo(plannings[1].Id));
//         // Assert.That(itemPlanningCases[2].PlanningId, Is.EqualTo(plannings[2].Id));
//
//         // Assert itemPlanningCaseSites
//         Assert.That(itemPlanningCaseSites, Is.Not.Null);
//         Assert.That(itemPlanningCaseSites.Count, Is.EqualTo(2));
//         Assert.That(itemPlanningCaseSites[0].PlanningCaseId, Is.EqualTo(itemPlanningCases[0].Id));
//         Assert.That(itemPlanningCaseSites[0].PlanningId, Is.EqualTo(plannings[0].Id));
//         Assert.That(itemPlanningCaseSites[0].MicrotingSdkSiteId, Is.EqualTo(sites[2].Id));
//         Assert.That(itemPlanningCaseSites[0].MicrotingSdkCaseId, Is.EqualTo(cases[0].Id));
//         Assert.That(itemPlanningCaseSites[0].Status, Is.EqualTo(66));
//         Assert.That(itemPlanningCaseSites[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         Assert.That(itemPlanningCaseSites[0].MicrotingCheckListSitId, Is.EqualTo(0));
//         Assert.That(itemPlanningCaseSites[1].PlanningCaseId, Is.EqualTo(itemPlanningCases[0].Id));
//         Assert.That(itemPlanningCaseSites[1].PlanningId, Is.EqualTo(plannings[0].Id));
//         Assert.That(itemPlanningCaseSites[1].MicrotingSdkSiteId, Is.EqualTo(sites[3].Id));
//         Assert.That(itemPlanningCaseSites[1].MicrotingSdkCaseId, Is.EqualTo(cases[1].Id));
//         Assert.That(itemPlanningCaseSites[1].Status, Is.EqualTo(66));
//         Assert.That(itemPlanningCaseSites[1].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         Assert.That(itemPlanningCaseSites[1].MicrotingCheckListSitId, Is.EqualTo(0));
//         // Assert.That(itemPlanningCaseSites[2].PlanningCaseId, Is.EqualTo(itemPlanningCases[2].Id));
//         // Assert.That(itemPlanningCaseSites[2].PlanningId, Is.EqualTo(plannings[2].Id));
//         // Assert.That(itemPlanningCaseSites[2].MicrotingSdkSiteId, Is.EqualTo(sites[2].Id));
//         // Assert.That(itemPlanningCaseSites[2].MicrotingSdkCaseId, Is.EqualTo(cases[2].Id));
//         // Assert.That(itemPlanningCaseSites[2].Status, Is.EqualTo(66));
//         // Assert.That(itemPlanningCaseSites[2].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(itemPlanningCaseSites[2].MicrotingCheckListSitId, Is.EqualTo(0));
//         // Assert.That(itemPlanningCaseSites[3].PlanningCaseId, Is.EqualTo(itemPlanningCases[0].Id));
//         // Assert.That(itemPlanningCaseSites[3].PlanningId, Is.EqualTo(plannings[0].Id));
//         // Assert.That(itemPlanningCaseSites[3].MicrotingSdkSiteId, Is.EqualTo(sites[3].Id));
//         // Assert.That(itemPlanningCaseSites[3].MicrotingSdkCaseId, Is.EqualTo(cases[3].Id));
//         // Assert.That(itemPlanningCaseSites[3].Status, Is.EqualTo(66));
//         // Assert.That(itemPlanningCaseSites[3].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(itemPlanningCaseSites[3].MicrotingCheckListSitId, Is.EqualTo(0));
//         // Assert.That(itemPlanningCaseSites[4].PlanningCaseId, Is.EqualTo(itemPlanningCases[1].Id));
//         // Assert.That(itemPlanningCaseSites[4].PlanningId, Is.EqualTo(plannings[1].Id));
//         // Assert.That(itemPlanningCaseSites[4].MicrotingSdkSiteId, Is.EqualTo(sites[3].Id));
//         // Assert.That(itemPlanningCaseSites[4].MicrotingSdkCaseId, Is.EqualTo(cases[4].Id));
//         // Assert.That(itemPlanningCaseSites[4].Status, Is.EqualTo(66));
//         // Assert.That(itemPlanningCaseSites[4].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(itemPlanningCaseSites[4].MicrotingCheckListSitId, Is.EqualTo(0));
//         // Assert.That(itemPlanningCaseSites[5].PlanningCaseId, Is.EqualTo(itemPlanningCases[2].Id));
//         // Assert.That(itemPlanningCaseSites[5].PlanningId, Is.EqualTo(plannings[2].Id));
//         // Assert.That(itemPlanningCaseSites[5].MicrotingSdkSiteId, Is.EqualTo(sites[3].Id));
//         // Assert.That(itemPlanningCaseSites[5].MicrotingSdkCaseId, Is.EqualTo(cases[5].Id));
//         // Assert.That(itemPlanningCaseSites[5].Status, Is.EqualTo(66));
//         // Assert.That(itemPlanningCaseSites[5].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(itemPlanningCaseSites[5].MicrotingCheckListSitId, Is.EqualTo(0));
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
//         Assert.That(cases.Count, Is.EqualTo(2));
//         Assert.That(cases[0].SiteId, Is.EqualTo(sites[2].Id));
//         Assert.That(cases[0].CheckListId, Is.EqualTo(floatingLayerEformId));
//         Assert.That(cases[0].FolderId, Is.Null);
//         Assert.That(cases[0].Status, Is.EqualTo(33));
//         Assert.That(cases[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
//         Assert.That(cases[1].SiteId, Is.EqualTo(sites[3].Id));
//         Assert.That(cases[1].CheckListId, Is.EqualTo(floatingLayerEformId));
//         Assert.That(cases[1].FolderId, Is.Null);
//         Assert.That(cases[1].Status, Is.EqualTo(66));
//         Assert.That(cases[1].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
//         // Assert.That(cases[2].SiteId, Is.EqualTo(sites[2].Id));
//         // Assert.That(cases[2].CheckListId, Is.EqualTo(areaRules[0].EformId));
//         // Assert.That(cases[2].FolderId, Is.Null);
//         // Assert.That(cases[2].Status, Is.EqualTo(66));
//         // Assert.That(cases[2].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
//         // Assert.That(cases[3].SiteId, Is.EqualTo(sites[3].Id));
//         // Assert.That(cases[3].CheckListId, Is.EqualTo(floatingLayerEformId));
//         // Assert.That(cases[3].FolderId, Is.Null);
//         // Assert.That(cases[3].Status, Is.EqualTo(66));
//         // Assert.That(cases[3].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
//         // Assert.That(cases[4].SiteId, Is.EqualTo(sites[3].Id));
//         // Assert.That(cases[4].CheckListId, Is.EqualTo(alarmeFormid));
//         // Assert.That(cases[4].FolderId, Is.Null);
//         // Assert.That(cases[4].Status, Is.EqualTo(66));
//         // Assert.That(cases[4].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
//         // Assert.That(cases[5].SiteId, Is.EqualTo(sites[3].Id));
//         // Assert.That(cases[5].CheckListId, Is.EqualTo(areaRules[0].EformId));
//         // Assert.That(cases[5].FolderId, Is.Null);
//         // Assert.That(cases[5].Status, Is.EqualTo(66));
//         // Assert.That(cases[5].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
//     }
//
//     // Should test the UpdatePlanning method for area rule "03. Gyllebeholdere" for areaRule: 0 with construction, alarm and open container
//     // Adding a nw worker to the list of assigned sites after it's deactivated
//     [Test]
//     public async Task UpdatePlanning_AreaRule0AlamAndOpenContainerConstructionAddWorkerAfterDeactivate_ReturnsSuccess()
//     {
//         // Arrange
//         var core = await GetCore();
//         var propertyCreateModel = new PropertyCreateModel
//         {
//             Address = Guid.NewGuid().ToString(),
//             Chr = Guid.NewGuid().ToString(),
//             IndustryCode = Guid.NewGuid().ToString(),
//             Cvr = Guid.NewGuid().ToString(),
//             IsFarm = true,
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
//         var deviceUserModel2 = new DeviceUserModel
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
//         await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel2, core, 1,
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
//         var propertyAssignWorkersModel2 = new PropertyAssignWorkersModel
//         {
//             Assignments =
//             [
//                 new()
//                 {
//                     PropertyId = properties[0].Id,
//                     IsChecked = true
//                 }
//             ],
//             SiteId = sites[3].Id
//         };
//
//         await BackendConfigurationAssignmentWorkerServiceHelper.Create(propertyAssignWorkersModel2, core, userService,
//             BackendConfigurationPnDbContext, CaseTemplatePnDbContext, null, Bus);
//
//         var areaTranslation = await BackendConfigurationPnDbContext!.AreaTranslations.FirstAsync(x => x.Name == "03. Gyllebeholdere");
//         var area = await BackendConfigurationPnDbContext.Areas.FirstAsync(x => x.Id == areaTranslation.AreaId);
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
//         var propertyArea = await BackendConfigurationPnDbContext!.AreaProperties.FirstAsync(x => x.PropertyId == properties[0].Id && x.AreaId == area.Id);
//         var danishLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.Name == "Dansk");
//         var areaInitialFields = await BackendConfigurationPnDbContext.AreaInitialFields.Where(x => x.AreaId == area.Id)
//             .ToListAsync();
//         var checkListTranslation = await MicrotingDbContext.CheckListTranslations.FirstAsync(x => x.Text == areaInitialFields.First().EformName);
//
//         AreaRulesCreateModel areaRulesCreateModel = new AreaRulesCreateModel
//         {
//             AreaRules =
//             [
//                 new()
//                 {
//                     TranslatedNames =
//                     [
//                         new()
//                             { Name = "Beholeder 1", Description = "00. Logbøger", Id = danishLanguage.Id }
//                     ],
//                     TypeSpecificFields = new TypeSpecificFields
//                     {
//                         Alarm = AreaRuleT2AlarmsEnum.Yes,
//                         DayOfWeek = 1,
//                         EformId = checkListTranslation.CheckListId,
//                         Type = AreaRuleT2TypesEnum.Open
//                     }
//                 }
//             ],
//             PropertyAreaId = propertyArea.Id
//         };
//
//         await BackendConfigurationAreaRulesServiceHelper.Create(areaRulesCreateModel, core, 1, BackendConfigurationPnDbContext, danishLanguage);
//         var areaRules = await BackendConfigurationPnDbContext!.AreaRules.Where(x => x.PropertyId == properties[0].Id).ToListAsync();
//
//
//         // should create AreaRulePlanningModel for areaId
//         var areaRulePlanningModel = new AreaRulePlanningModel
//         {
//             AssignedSites =
//             [
//                 new()
//                 {
//                     Checked = true,
//                     SiteId = sites[2].Id
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
//                 Alarm = AreaRuleT2AlarmsEnum.Yes,
//                 DayOfMonth = 0,
//                 DayOfWeek = 0,
//                 HoursAndEnergyEnabled = false,
//                 RepeatEvery = 0,
//                 RepeatType = 1,
//                 Type = AreaRuleT2TypesEnum.Open
//             }
//
//         };
//         await BackendConfigurationAreaRulePlanningsServiceHelper.UpdatePlanning(areaRulePlanningModel,
//             core, 1, BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, null);
//
//         var areaRulePlannings = await BackendConfigurationPnDbContext!.AreaRulePlannings.ToListAsync();
//
//         var areaRulePlanningModel2 = new AreaRulePlanningModel
//         {
//             Id = areaRulePlannings.First().Id,
//             AssignedSites =
//             [
//                 new()
//                 {
//                     Checked = true,
//                     SiteId = sites[2].Id
//                 },
//
//                 new()
//                 {
//                     Checked = true,
//                     SiteId = sites[3].Id
//                 }
//             ],
//             RuleId = areaRules[0].Id,
//             ComplianceEnabled = true,
//             PropertyId = properties[0].Id,
//             Status = false,
//             SendNotifications = true,
//             StartDate = DateTime.UtcNow,
//             TypeSpecificFields = new AreaRuleTypePlanningModel
//             {
//                 Alarm = AreaRuleT2AlarmsEnum.Yes,
//                 DayOfMonth = 0,
//                 DayOfWeek = 0,
//                 HoursAndEnergyEnabled = false,
//                 RepeatEvery = 0,
//                 RepeatType = 1,
//                 Type = AreaRuleT2TypesEnum.Open
//             }
//
//         };
//         await BackendConfigurationAreaRulePlanningsServiceHelper.UpdatePlanning(areaRulePlanningModel2,
//             core, 1, BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, null);
//
//         // Act
//         var areaRulePlanningModel3 = new AreaRulePlanningModel
//         {
//             Id = areaRulePlannings.First().Id,
//             AssignedSites =
//             [
//                 new()
//                 {
//                     Checked = true,
//                     SiteId = sites[2].Id
//                 },
//
//                 new()
//                 {
//                     Checked = true,
//                     SiteId = sites[3].Id
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
//                 Alarm = AreaRuleT2AlarmsEnum.Yes,
//                 DayOfMonth = 0,
//                 DayOfWeek = 0,
//                 HoursAndEnergyEnabled = false,
//                 RepeatEvery = 0,
//                 RepeatType = 1,
//                 Type = AreaRuleT2TypesEnum.Open
//             }
//
//         };
//         await BackendConfigurationAreaRulePlanningsServiceHelper.UpdatePlanning(areaRulePlanningModel3,
//             core, 1, BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, null);
//
//         // Assert
//         var areaRuleTranslations = await BackendConfigurationPnDbContext!.AreaRuleTranslations
//             .Join(BackendConfigurationPnDbContext.AreaRules, atr => atr.AreaRuleId, ar => ar.Id,
//                 (atr, ar) => new { atr, ar }).Where(x => x.ar.PropertyId == properties[0].Id).ToListAsync();
//         var areaProperties = await BackendConfigurationPnDbContext!.AreaProperties.Where(x => x.PropertyId == properties[0].Id).ToListAsync();
//         var folderTranslations = await MicrotingDbContext!.FolderTranslations.ToListAsync();
//         var planningSites = await BackendConfigurationPnDbContext.PlanningSites.AsNoTracking().ToListAsync();
//         var plannings = await ItemsPlanningPnDbContext!.Plannings.AsNoTracking().ToListAsync();
//         var planningNameTranslations = await ItemsPlanningPnDbContext.PlanningNameTranslation.ToListAsync();
//         var itemPlanningSites = await ItemsPlanningPnDbContext!.PlanningSites.AsNoTracking().ToListAsync();
//         var itemPlanningCases = await ItemsPlanningPnDbContext!.PlanningCases.ToListAsync();
//         var itemPlanningCaseSites = await ItemsPlanningPnDbContext!.PlanningCaseSites.ToListAsync();
//         var compliances = await BackendConfigurationPnDbContext!.Compliances.ToListAsync();
//         var checkListSites = await MicrotingDbContext!.CheckListSites.ToListAsync();
//         var cases = await MicrotingDbContext!.Cases.ToListAsync();
//
//         var englishLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "en-US");
//         var germanLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "de-DE");
//         /*var alarmeFormid = */await MicrotingDbContext.CheckListTranslations.Where(x => x.Text == "03. Kontrol alarmanlæg gyllebeholder").Select(x => x.CheckListId).FirstAsync();
//         var floatingLayerEformId = await MicrotingDbContext.CheckListTranslations.Where(x => x.Text == "03. Kontrol flydelag").Select(x => x.CheckListId).FirstAsync();
//
//         Assert.That(result, Is.Not.Null);
//         Assert.That(result.Success, Is.EqualTo(true));
//
//         // Assert areaRules
//         Assert.That(areaRules, Is.Not.Null);
//         Assert.That(areaRules.Count, Is.EqualTo(1));
//         Assert.That(areaRules[0].PropertyId, Is.EqualTo(properties[0].Id));
//         Assert.That(areaRules[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
//         Assert.That(areaRules[0].EformName, Is.EqualTo("03. Kontrol konstruktion"));
//
//         // Assert areaRuleTranslations
//         Assert.That(areaRuleTranslations, Is.Not.Null);
//         Assert.That(areaRuleTranslations.Count, Is.EqualTo(1));
//         Assert.That(areaRuleTranslations[0].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
//         Assert.That(areaRuleTranslations[0].atr.LanguageId, Is.EqualTo(1));
//         Assert.That(areaRuleTranslations[0].atr.Name, Is.EqualTo("Beholeder 1"));
//
//
//         // Assert areaProperties
//         Assert.That(areaProperties, Is.Not.Null);
//         Assert.That(areaProperties.Count, Is.EqualTo(1));
//         Assert.That(areaProperties[0].PropertyId, Is.EqualTo(properties[0].Id));
//         Assert.That(areaProperties[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
//
//         // Assert folder translations
//         Assert.That(folderTranslations, Is.Not.Null);
//         Assert.That(folderTranslations.Count, Is.EqualTo(32));
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
//         Assert.That(folderTranslations[28].Name, Is.EqualTo("03. Gyllebeholdere"));
//         Assert.That(folderTranslations[28].LanguageId, Is.EqualTo(1));
//         Assert.That(folderTranslations[29].Name, Is.EqualTo("03. Slurry tanks"));
//         Assert.That(folderTranslations[29].LanguageId, Is.EqualTo(2));
//         Assert.That(folderTranslations[30].Name, Is.EqualTo("03. Gülletanks"));
//         Assert.That(folderTranslations[30].LanguageId, Is.EqualTo(3));
//         Assert.That(folderTranslations[31].Name, Is.EqualTo("Beholeder 1"));
//         Assert.That(folderTranslations[31].LanguageId, Is.EqualTo(1));
//
//         // Assert AreaRulePlannings
//         Assert.That(areaRulePlannings, Is.Not.Null);
//         Assert.That(areaRulePlannings.Count, Is.EqualTo(1));
//         Assert.That(areaRulePlannings[0].AreaRuleId, Is.EqualTo(areaRules[0].Id));
//         Assert.That(areaRulePlannings[0].ItemPlanningId, Is.EqualTo(plannings[1].Id));
//         // Assert.That(areaRulePlannings[1].ItemPlanningId, Is.EqualTo(plannings[4].Id));
//         // Assert.That(areaRulePlannings[2].ItemPlanningId, Is.EqualTo(plannings[5].Id));
//         Assert.That(areaRulePlannings[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
//         // Assert.That(areaRulePlannings[1].AreaId, Is.EqualTo(areaTranslation.AreaId));
//         // Assert.That(areaRulePlannings[2].AreaId, Is.EqualTo(areaTranslation.AreaId));
//         Assert.That(areaRulePlannings[0].PropertyId, Is.EqualTo(properties[0].Id));
//         // Assert.That(areaRulePlannings[1].PropertyId, Is.EqualTo(properties[0].Id));
//         // Assert.That(areaRulePlannings[2].PropertyId, Is.EqualTo(properties[0].Id));
//         Assert.That(areaRulePlannings[0].ComplianceEnabled, Is.EqualTo(true));
//         // Assert.That(areaRulePlannings[1].ComplianceEnabled, Is.EqualTo(true));
//         // Assert.That(areaRulePlannings[2].ComplianceEnabled, Is.EqualTo(true));
//         Assert.That(areaRulePlannings[0].SendNotifications, Is.EqualTo(true));
//         // Assert.That(areaRulePlannings[1].SendNotifications, Is.EqualTo(true));
//         // Assert.That(areaRulePlannings[2].SendNotifications, Is.EqualTo(true));
//
//         // Assert plannings
//         Assert.That(plannings, Is.Not.Null);
//         Assert.That(plannings.Count, Is.EqualTo(2));
//         Assert.That(plannings[0].RelatedEFormId, Is.EqualTo(floatingLayerEformId));
//         Assert.That(plannings[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
//         Assert.That(plannings[0].SdkFolderId, Is.EqualTo(folderTranslations[31].FolderId));
//         Assert.That(plannings[0].LastExecutedTime, Is.Not.Null);
//         Assert.That(plannings[0].LastExecutedTime, Is.EqualTo(new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 0,0,0)));
//         var now = DateTime.UtcNow;
//         var nextExecutionTime = new DateTime(now.Year, now.Month, (int)plannings[0].DayOfMonth!, 0, 0, 0).AddMonths(1);
//
//         Assert.That(plannings[0].NextExecutionTime, Is.EqualTo(nextExecutionTime));
//         Assert.That(plannings[0].RepeatEvery, Is.EqualTo(1));
//         Assert.That(plannings[0].RepeatType, Is.EqualTo(RepeatType.Month));
//
//         Assert.That(plannings[1].RelatedEFormId, Is.EqualTo(floatingLayerEformId));
//         Assert.That(plannings[1].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         Assert.That(plannings[1].SdkFolderId, Is.EqualTo(folderTranslations[31].FolderId));
//         Assert.That(plannings[1].LastExecutedTime, Is.Not.Null);
//         Assert.That(plannings[1].LastExecutedTime, Is.EqualTo(new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 0,0,0)));
//         nextExecutionTime =new DateTime(now.Year, now.Month, (int)plannings[0].DayOfMonth!, 0, 0, 0).AddMonths(1);
//
//         Assert.That(plannings[1].NextExecutionTime, Is.EqualTo(nextExecutionTime));
//         Assert.That(plannings[1].RepeatEvery, Is.EqualTo(1));
//         Assert.That(plannings[1].RepeatType, Is.EqualTo(RepeatType.Month));
//
//
//         // Assert.That(plannings[2].RelatedEFormId, Is.EqualTo(areaRules[0].EformId));
//         // Assert.That(plannings[2].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
//         // Assert.That(plannings[2].SdkFolderId, Is.EqualTo(folderTranslations[31].FolderId));
//         // Assert.That(plannings[2].LastExecutedTime, Is.Not.Null);
//         // // test last executed time within 1 minute
//         // Assert.That(plannings[2].LastExecutedTime, Is.EqualTo(new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 0,0,0)));
//         // nextExecutionTime =new DateTime(now.Year + 1, 1, (int)plannings[0].DayOfMonth!, 0, 0, 0);
//         //
//         // Assert.That(plannings[2].NextExecutionTime, Is.EqualTo(nextExecutionTime));
//         // Assert.That(plannings[2].RepeatEvery, Is.EqualTo(12));
//         // Assert.That(plannings[2].RepeatType, Is.EqualTo(RepeatType.Month));
//         // Assert.That(plannings[3].RelatedEFormId, Is.EqualTo(floatingLayerEformId));
//         // Assert.That(plannings[3].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(plannings[3].SdkFolderId, Is.EqualTo(folderTranslations[31].FolderId));
//         // Assert.That(plannings[3].LastExecutedTime, Is.Not.Null);
//         // // test last executed time within 1 minute
//         // Assert.That(plannings[3].LastExecutedTime, Is.EqualTo(new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 0,0,0)));
//         // now = DateTime.UtcNow;
//         // nextExecutionTime = new DateTime(now.Year, now.Month, (int)plannings[0].DayOfMonth!, 0, 0, 0).AddMonths(1);
//         //
//         // Assert.That(plannings[3].NextExecutionTime, Is.EqualTo(nextExecutionTime));
//         // Assert.That(plannings[3].RepeatEvery, Is.EqualTo(1));
//         // Assert.That(plannings[3].RepeatType, Is.EqualTo(RepeatType.Month));
//         //
//         // Assert.That(plannings[4].RelatedEFormId, Is.EqualTo(alarmeFormid));
//         // Assert.That(plannings[4].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(plannings[4].SdkFolderId, Is.EqualTo(folderTranslations[31].FolderId));
//         // Assert.That(plannings[4].LastExecutedTime, Is.Not.Null);
//         // // test last executed time within 1 minute
//         // Assert.That(plannings[4].LastExecutedTime, Is.EqualTo(new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 0,0,0)));
//         // nextExecutionTime =new DateTime(now.Year, now.Month, (int)plannings[0].DayOfMonth!, 0, 0, 0).AddMonths(1);
//         //
//         // Assert.That(plannings[4].NextExecutionTime, Is.EqualTo(nextExecutionTime));
//         // Assert.That(plannings[4].RepeatEvery, Is.EqualTo(1));
//         // Assert.That(plannings[4].RepeatType, Is.EqualTo(RepeatType.Month));
//         //
//         //
//         // Assert.That(plannings[5].RelatedEFormId, Is.EqualTo(areaRules[0].EformId));
//         // Assert.That(plannings[5].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(plannings[5].SdkFolderId, Is.EqualTo(folderTranslations[31].FolderId));
//         // Assert.That(plannings[5].LastExecutedTime, Is.Not.Null);
//         // // test last executed time within 1 minute
//         // Assert.That(plannings[5].LastExecutedTime, Is.EqualTo(new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 0,0,0)));
//         // nextExecutionTime =new DateTime(now.Year + 1, 1, (int)plannings[0].DayOfMonth!, 0, 0, 0);
//         //
//         // Assert.That(plannings[5].NextExecutionTime, Is.EqualTo(nextExecutionTime));
//         // Assert.That(plannings[5].RepeatEvery, Is.EqualTo(12));
//         // Assert.That(plannings[5].RepeatType, Is.EqualTo(RepeatType.Month));
//
//         // Assert planningNameTranslations
//         Assert.That(planningNameTranslations, Is.Not.Null);
//         Assert.That(planningNameTranslations.Count, Is.EqualTo(6));
//         Assert.That(planningNameTranslations[0].Name, Is.EqualTo("Beholeder 1: Flydelag"));
//         Assert.That(planningNameTranslations[0].LanguageId, Is.EqualTo(areaRuleTranslations[0].atr.LanguageId));
//         Assert.That(planningNameTranslations[0].PlanningId, Is.EqualTo(plannings[0].Id));
//         Assert.That(planningNameTranslations[1].Name, Is.EqualTo(": Floating layer"));
//         Assert.That(planningNameTranslations[1].LanguageId, Is.EqualTo(englishLanguage.Id));
//         Assert.That(planningNameTranslations[1].PlanningId, Is.EqualTo(plannings[0].Id));
//         Assert.That(planningNameTranslations[2].Name, Is.EqualTo(": Schwimmende Ebene"));
//         Assert.That(planningNameTranslations[2].LanguageId, Is.EqualTo(germanLanguage.Id));
//         Assert.That(planningNameTranslations[2].PlanningId, Is.EqualTo(plannings[0].Id));
//         Assert.That(planningNameTranslations[3].Name, Is.EqualTo("Beholeder 1: Flydelag"));
//         Assert.That(planningNameTranslations[3].LanguageId, Is.EqualTo(areaRuleTranslations[0].atr.LanguageId));
//         Assert.That(planningNameTranslations[3].PlanningId, Is.EqualTo(plannings[1].Id));
//         Assert.That(planningNameTranslations[4].Name, Is.EqualTo(": Floating layer"));
//         Assert.That(planningNameTranslations[4].LanguageId, Is.EqualTo(englishLanguage.Id));
//         Assert.That(planningNameTranslations[4].PlanningId, Is.EqualTo(plannings[1].Id));
//         Assert.That(planningNameTranslations[5].Name, Is.EqualTo(": Schwimmende Ebene"));
//         Assert.That(planningNameTranslations[5].LanguageId, Is.EqualTo(germanLanguage.Id));
//         Assert.That(planningNameTranslations[5].PlanningId, Is.EqualTo(plannings[1].Id));
//         // Assert.That(planningNameTranslations[3].Name, Is.EqualTo("Beholeer 1: Alarm"));
//         // Assert.That(planningNameTranslations[3].LanguageId, Is.EqualTo(areaRuleTranslations[0].atr.LanguageId));
//         // Assert.That(planningNameTranslations[3].PlanningId, Is.EqualTo(plannings[1].Id));
//         // Assert.That(planningNameTranslations[4].Name, Is.EqualTo(": Alarm"));
//         // Assert.That(planningNameTranslations[4].LanguageId, Is.EqualTo(englishLanguage.Id));
//         // Assert.That(planningNameTranslations[4].PlanningId, Is.EqualTo(plannings[1].Id));
//         // Assert.That(planningNameTranslations[5].Name, Is.EqualTo(": Alarm"));
//         // Assert.That(planningNameTranslations[5].LanguageId, Is.EqualTo(germanLanguage.Id));
//         // Assert.That(planningNameTranslations[5].PlanningId, Is.EqualTo(plannings[1].Id));
//         // Assert.That(planningNameTranslations[6].Name, Is.EqualTo("Beholeder 1: Konstruktion"));
//         // Assert.That(planningNameTranslations[6].LanguageId, Is.EqualTo(areaRuleTranslations[0].atr.LanguageId));
//         // Assert.That(planningNameTranslations[6].PlanningId, Is.EqualTo(plannings[2].Id));
//         // Assert.That(planningNameTranslations[7].Name, Is.EqualTo(": Construction"));
//         // Assert.That(planningNameTranslations[7].LanguageId, Is.EqualTo(englishLanguage.Id));
//         // Assert.That(planningNameTranslations[7].PlanningId, Is.EqualTo(plannings[2].Id));
//         // Assert.That(planningNameTranslations[8].Name, Is.EqualTo(": Konstruktion"));
//         // Assert.That(planningNameTranslations[8].LanguageId, Is.EqualTo(germanLanguage.Id));
//         // Assert.That(planningNameTranslations[8].PlanningId, Is.EqualTo(plannings[2].Id));
//         // Assert.That(planningNameTranslations[9].Name, Is.EqualTo("Beholeder 1: Flydelag"));
//         // Assert.That(planningNameTranslations[9].LanguageId, Is.EqualTo(areaRuleTranslations[0].atr.LanguageId));
//         // Assert.That(planningNameTranslations[9].PlanningId, Is.EqualTo(plannings[3].Id));
//         // Assert.That(planningNameTranslations[10].Name, Is.EqualTo(": Floating layer"));
//         // Assert.That(planningNameTranslations[10].LanguageId, Is.EqualTo(englishLanguage.Id));
//         // Assert.That(planningNameTranslations[10].PlanningId, Is.EqualTo(plannings[3].Id));
//         // Assert.That(planningNameTranslations[11].Name, Is.EqualTo(": Schwimmende Ebene"));
//         // Assert.That(planningNameTranslations[11].LanguageId, Is.EqualTo(germanLanguage.Id));
//         // Assert.That(planningNameTranslations[11].PlanningId, Is.EqualTo(plannings[3].Id));
//         // Assert.That(planningNameTranslations[12].Name, Is.EqualTo("Beholeder 1: Alarm"));
//         // Assert.That(planningNameTranslations[12].LanguageId, Is.EqualTo(areaRuleTranslations[0].atr.LanguageId));
//         // Assert.That(planningNameTranslations[12].PlanningId, Is.EqualTo(plannings[4].Id));
//         // Assert.That(planningNameTranslations[13].Name, Is.EqualTo(": Alarm"));
//         // Assert.That(planningNameTranslations[13].LanguageId, Is.EqualTo(englishLanguage.Id));
//         // Assert.That(planningNameTranslations[13].PlanningId, Is.EqualTo(plannings[4].Id));
//         // Assert.That(planningNameTranslations[14].Name, Is.EqualTo(": Alarm"));
//         // Assert.That(planningNameTranslations[14].LanguageId, Is.EqualTo(germanLanguage.Id));
//         // Assert.That(planningNameTranslations[14].PlanningId, Is.EqualTo(plannings[4].Id));
//         // Assert.That(planningNameTranslations[15].Name, Is.EqualTo("Beholeder 1: Konstruktion"));
//         // Assert.That(planningNameTranslations[15].LanguageId, Is.EqualTo(areaRuleTranslations[0].atr.LanguageId));
//         // Assert.That(planningNameTranslations[15].PlanningId, Is.EqualTo(plannings[5].Id));
//         // Assert.That(planningNameTranslations[16].Name, Is.EqualTo(": Construction"));
//         // Assert.That(planningNameTranslations[16].LanguageId, Is.EqualTo(englishLanguage.Id));
//         // Assert.That(planningNameTranslations[16].PlanningId, Is.EqualTo(plannings[5].Id));
//         // Assert.That(planningNameTranslations[17].Name, Is.EqualTo(": Konstruktion"));
//         // Assert.That(planningNameTranslations[17].LanguageId, Is.EqualTo(germanLanguage.Id));
//         // Assert.That(planningNameTranslations[17].PlanningId, Is.EqualTo(plannings[5].Id));
//
//         // Assert planningSites
//         Assert.That(planningSites, Is.Not.Null);
//         Assert.That(planningSites.Count, Is.EqualTo(2));
//         Assert.That(planningSites[0].AreaRulePlanningsId, Is.EqualTo(areaRulePlannings[0].Id));
//         Assert.That(planningSites[0].AreaId, Is.EqualTo(area.Id));
//         Assert.That(planningSites[0].SiteId, Is.EqualTo(sites[2].Id));
//         Assert.That(planningSites[0].Status, Is.EqualTo(33));
//         Assert.That(planningSites[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         Assert.That(planningSites[1].AreaRulePlanningsId, Is.EqualTo(areaRulePlannings[0].Id));
//         Assert.That(planningSites[1].AreaId, Is.EqualTo(area.Id));
//         Assert.That(planningSites[1].SiteId, Is.EqualTo(sites[3].Id));
//         Assert.That(planningSites[1].Status, Is.EqualTo(33));
//         Assert.That(planningSites[1].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(planningSites[2].AreaRulePlanningsId, Is.EqualTo(areaRulePlannings[2].Id));
//         // Assert.That(planningSites[2].AreaId, Is.EqualTo(area.Id));
//         // Assert.That(planningSites[2].SiteId, Is.EqualTo(sites[2].Id));
//         // Assert.That(planningSites[2].Status, Is.EqualTo(33));
//         // Assert.That(planningSites[2].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(planningSites[3].AreaRulePlanningsId, Is.EqualTo(areaRulePlannings[0].Id));
//         // Assert.That(planningSites[3].AreaId, Is.EqualTo(area.Id));
//         // Assert.That(planningSites[3].SiteId, Is.EqualTo(sites[3].Id));
//         // Assert.That(planningSites[3].Status, Is.EqualTo(33));
//         // Assert.That(planningSites[3].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(planningSites[4].AreaRulePlanningsId, Is.EqualTo(areaRulePlannings[1].Id));
//         // Assert.That(planningSites[4].AreaId, Is.EqualTo(area.Id));
//         // Assert.That(planningSites[4].SiteId, Is.EqualTo(sites[3].Id));
//         // Assert.That(planningSites[4].Status, Is.EqualTo(33));
//         // Assert.That(planningSites[4].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(planningSites[5].AreaRulePlanningsId, Is.EqualTo(areaRulePlannings[2].Id));
//         // Assert.That(planningSites[5].AreaId, Is.EqualTo(area.Id));
//         // Assert.That(planningSites[5].SiteId, Is.EqualTo(sites[3].Id));
//         // Assert.That(planningSites[5].Status, Is.EqualTo(33));
//         // Assert.That(planningSites[5].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//
//         // Assert itemPlanningSites
//         Assert.That(itemPlanningSites, Is.Not.Null);
//         Assert.That(itemPlanningSites.Count, Is.EqualTo(3));
//         Assert.That(itemPlanningSites[0].PlanningId, Is.EqualTo(plannings[0].Id));
//         Assert.That(itemPlanningSites[0].SiteId, Is.EqualTo(sites[2].Id));
//         Assert.That(itemPlanningSites[0].LastExecutedTime, Is.Null);
//         Assert.That(itemPlanningSites[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
//         Assert.That(itemPlanningSites[1].PlanningId, Is.EqualTo(plannings[1].Id));
//         Assert.That(itemPlanningSites[1].SiteId, Is.EqualTo(sites[2].Id));
//         Assert.That(itemPlanningSites[1].LastExecutedTime, Is.Null);
//         Assert.That(itemPlanningSites[1].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         Assert.That(itemPlanningSites[2].PlanningId, Is.EqualTo(plannings[1].Id));
//         Assert.That(itemPlanningSites[2].SiteId, Is.EqualTo(sites[3].Id));
//         Assert.That(itemPlanningSites[2].LastExecutedTime, Is.Null);
//         Assert.That(itemPlanningSites[2].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(itemPlanningSites[3].PlanningId, Is.EqualTo(plannings[3].Id));
//         // Assert.That(itemPlanningSites[3].SiteId, Is.EqualTo(sites[2].Id));
//         // Assert.That(itemPlanningSites[3].LastExecutedTime, Is.Null);
//         // Assert.That(itemPlanningSites[3].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(itemPlanningSites[4].PlanningId, Is.EqualTo(plannings[3].Id));
//         // Assert.That(itemPlanningSites[4].SiteId, Is.EqualTo(sites[3].Id));
//         // Assert.That(itemPlanningSites[4].LastExecutedTime, Is.Null);
//         // Assert.That(itemPlanningSites[4].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(itemPlanningSites[5].PlanningId, Is.EqualTo(plannings[4].Id));
//         // Assert.That(itemPlanningSites[5].SiteId, Is.EqualTo(sites[2].Id));
//         // Assert.That(itemPlanningSites[5].LastExecutedTime, Is.Null);
//         // Assert.That(itemPlanningSites[5].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(itemPlanningSites[6].PlanningId, Is.EqualTo(plannings[4].Id));
//         // Assert.That(itemPlanningSites[6].SiteId, Is.EqualTo(sites[3].Id));
//         // Assert.That(itemPlanningSites[6].LastExecutedTime, Is.Null);
//         // Assert.That(itemPlanningSites[6].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(itemPlanningSites[7].PlanningId, Is.EqualTo(plannings[5].Id));
//         // Assert.That(itemPlanningSites[7].SiteId, Is.EqualTo(sites[2].Id));
//         // Assert.That(itemPlanningSites[7].LastExecutedTime, Is.Null);
//         // Assert.That(itemPlanningSites[7].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(itemPlanningSites[8].PlanningId, Is.EqualTo(plannings[5].Id));
//         // Assert.That(itemPlanningSites[8].SiteId, Is.EqualTo(sites[3].Id));
//         // Assert.That(itemPlanningSites[8].LastExecutedTime, Is.Null);
//         // Assert.That(itemPlanningSites[8].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//
//         // Assert itemPlanningCases
//         Assert.That(itemPlanningCases, Is.Not.Null);
//         Assert.That(itemPlanningCases.Count, Is.EqualTo(2));
//         Assert.That(itemPlanningCases[0].PlanningId, Is.EqualTo(plannings[0].Id));
//         Assert.That(itemPlanningCases[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         Assert.That(itemPlanningCases[0].Status, Is.EqualTo(66));
//         Assert.That(itemPlanningCases[1].PlanningId, Is.EqualTo(plannings[1].Id));
//         Assert.That(itemPlanningCases[1].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         Assert.That(itemPlanningCases[1].Status, Is.EqualTo(66));
//         // Assert.That(itemPlanningCases[2].PlanningId, Is.EqualTo(plannings[1].Id));
//         // Assert.That(itemPlanningCases[2].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(itemPlanningCases[2].Status, Is.EqualTo(66));
//         // Assert.That(itemPlanningCases[3].PlanningId, Is.EqualTo(plannings[3].Id));
//         // Assert.That(itemPlanningCases[3].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(itemPlanningCases[3].Status, Is.EqualTo(66));
//         // Assert.That(itemPlanningCases[4].PlanningId, Is.EqualTo(plannings[4].Id));
//         // Assert.That(itemPlanningCases[4].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(itemPlanningCases[4].Status, Is.EqualTo(66));
//         // Assert.That(itemPlanningCases[5].PlanningId, Is.EqualTo(plannings[5].Id));
//         // Assert.That(itemPlanningCases[5].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(itemPlanningCases[5].Status, Is.EqualTo(66));
//
//         // Assert itemPlanningCaseSites
//         Assert.That(itemPlanningCaseSites, Is.Not.Null);
//         Assert.That(itemPlanningCaseSites.Count, Is.EqualTo(3));
//         Assert.That(itemPlanningCaseSites[0].PlanningCaseId, Is.EqualTo(itemPlanningCases[0].Id));
//         Assert.That(itemPlanningCaseSites[0].PlanningId, Is.EqualTo(plannings[0].Id));
//         Assert.That(itemPlanningCaseSites[0].MicrotingSdkSiteId, Is.EqualTo(sites[2].Id));
//         Assert.That(itemPlanningCaseSites[0].MicrotingSdkCaseId, Is.EqualTo(cases[0].Id));
//         Assert.That(itemPlanningCaseSites[0].Status, Is.EqualTo(66));
//         Assert.That(itemPlanningCaseSites[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         Assert.That(itemPlanningCaseSites[0].MicrotingCheckListSitId, Is.EqualTo(0));
//         Assert.That(itemPlanningCaseSites[1].PlanningCaseId, Is.EqualTo(itemPlanningCases[1].Id));
//         Assert.That(itemPlanningCaseSites[1].PlanningId, Is.EqualTo(plannings[1].Id));
//         Assert.That(itemPlanningCaseSites[1].MicrotingSdkSiteId, Is.EqualTo(sites[2].Id));
//         Assert.That(itemPlanningCaseSites[1].MicrotingSdkCaseId, Is.EqualTo(cases[1].Id));
//         Assert.That(itemPlanningCaseSites[1].Status, Is.EqualTo(66));
//         Assert.That(itemPlanningCaseSites[1].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         Assert.That(itemPlanningCaseSites[1].MicrotingCheckListSitId, Is.EqualTo(0));
//         Assert.That(itemPlanningCaseSites[2].PlanningCaseId, Is.EqualTo(itemPlanningCases[1].Id));
//         Assert.That(itemPlanningCaseSites[2].PlanningId, Is.EqualTo(plannings[1].Id));
//         Assert.That(itemPlanningCaseSites[2].MicrotingSdkSiteId, Is.EqualTo(sites[3].Id));
//         Assert.That(itemPlanningCaseSites[2].MicrotingSdkCaseId, Is.EqualTo(cases[2].Id));
//         Assert.That(itemPlanningCaseSites[2].Status, Is.EqualTo(66));
//         Assert.That(itemPlanningCaseSites[2].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         Assert.That(itemPlanningCaseSites[2].MicrotingCheckListSitId, Is.EqualTo(0));
//         // Assert.That(itemPlanningCaseSites[3].PlanningCaseId, Is.EqualTo(itemPlanningCases[3].Id));
//         // Assert.That(itemPlanningCaseSites[3].PlanningId, Is.EqualTo(plannings[3].Id));
//         // Assert.That(itemPlanningCaseSites[3].MicrotingSdkSiteId, Is.EqualTo(sites[2].Id));
//         // Assert.That(itemPlanningCaseSites[3].MicrotingSdkCaseId, Is.EqualTo(cases[3].Id));
//         // Assert.That(itemPlanningCaseSites[3].Status, Is.EqualTo(66));
//         // Assert.That(itemPlanningCaseSites[3].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(itemPlanningCaseSites[3].MicrotingCheckListSitId, Is.EqualTo(0));
//         // Assert.That(itemPlanningCaseSites[4].PlanningCaseId, Is.EqualTo(itemPlanningCases[3].Id));
//         // Assert.That(itemPlanningCaseSites[4].PlanningId, Is.EqualTo(plannings[3].Id));
//         // Assert.That(itemPlanningCaseSites[4].MicrotingSdkSiteId, Is.EqualTo(sites[3].Id));
//         // Assert.That(itemPlanningCaseSites[4].MicrotingSdkCaseId, Is.EqualTo(cases[4].Id));
//         // Assert.That(itemPlanningCaseSites[4].Status, Is.EqualTo(66));
//         // Assert.That(itemPlanningCaseSites[4].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(itemPlanningCaseSites[4].MicrotingCheckListSitId, Is.EqualTo(0));
//         // Assert.That(itemPlanningCaseSites[5].PlanningCaseId, Is.EqualTo(itemPlanningCases[4].Id));
//         // Assert.That(itemPlanningCaseSites[5].PlanningId, Is.EqualTo(plannings[4].Id));
//         // Assert.That(itemPlanningCaseSites[5].MicrotingSdkSiteId, Is.EqualTo(sites[2].Id));
//         // Assert.That(itemPlanningCaseSites[5].MicrotingSdkCaseId, Is.EqualTo(cases[5].Id));
//         // Assert.That(itemPlanningCaseSites[5].Status, Is.EqualTo(66));
//         // Assert.That(itemPlanningCaseSites[5].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(itemPlanningCaseSites[5].MicrotingCheckListSitId, Is.EqualTo(0));
//         // Assert.That(itemPlanningCaseSites[6].PlanningCaseId, Is.EqualTo(itemPlanningCases[4].Id));
//         // Assert.That(itemPlanningCaseSites[6].PlanningId, Is.EqualTo(plannings[4].Id));
//         // Assert.That(itemPlanningCaseSites[6].MicrotingSdkSiteId, Is.EqualTo(sites[3].Id));
//         // Assert.That(itemPlanningCaseSites[6].MicrotingSdkCaseId, Is.EqualTo(cases[6].Id));
//         // Assert.That(itemPlanningCaseSites[6].Status, Is.EqualTo(66));
//         // Assert.That(itemPlanningCaseSites[6].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(itemPlanningCaseSites[6].MicrotingCheckListSitId, Is.EqualTo(0));
//         // Assert.That(itemPlanningCaseSites[7].PlanningCaseId, Is.EqualTo(itemPlanningCases[5].Id));
//         // Assert.That(itemPlanningCaseSites[7].PlanningId, Is.EqualTo(plannings[5].Id));
//         // Assert.That(itemPlanningCaseSites[7].MicrotingSdkSiteId, Is.EqualTo(sites[2].Id));
//         // Assert.That(itemPlanningCaseSites[7].MicrotingSdkCaseId, Is.EqualTo(cases[7].Id));
//         // Assert.That(itemPlanningCaseSites[7].Status, Is.EqualTo(66));
//         // Assert.That(itemPlanningCaseSites[7].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(itemPlanningCaseSites[7].MicrotingCheckListSitId, Is.EqualTo(0));
//         // Assert.That(itemPlanningCaseSites[8].PlanningCaseId, Is.EqualTo(itemPlanningCases[5].Id));
//         // Assert.That(itemPlanningCaseSites[8].PlanningId, Is.EqualTo(plannings[5].Id));
//         // Assert.That(itemPlanningCaseSites[8].MicrotingSdkSiteId, Is.EqualTo(sites[3].Id));
//         // Assert.That(itemPlanningCaseSites[8].MicrotingSdkCaseId, Is.EqualTo(cases[8].Id));
//         // Assert.That(itemPlanningCaseSites[8].Status, Is.EqualTo(66));
//         // Assert.That(itemPlanningCaseSites[8].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(itemPlanningCaseSites[8].MicrotingCheckListSitId, Is.EqualTo(0));
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
//         Assert.That(cases.Count, Is.EqualTo(3));
//         Assert.That(cases[0].SiteId, Is.EqualTo(sites[2].Id));
//         Assert.That(cases[0].CheckListId, Is.EqualTo(floatingLayerEformId));
//         Assert.That(cases[0].FolderId, Is.Null);
//         Assert.That(cases[0].Status, Is.EqualTo(33));
//         Assert.That(cases[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
//         Assert.That(cases[1].SiteId, Is.EqualTo(sites[2].Id));
//         Assert.That(cases[1].CheckListId, Is.EqualTo(floatingLayerEformId));
//         Assert.That(cases[1].FolderId, Is.Null);
//         Assert.That(cases[1].Status, Is.EqualTo(66));
//         Assert.That(cases[1].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         Assert.That(cases[2].SiteId, Is.EqualTo(sites[3].Id));
//         Assert.That(cases[2].CheckListId, Is.EqualTo(floatingLayerEformId));
//         Assert.That(cases[2].FolderId, Is.Null);
//         Assert.That(cases[2].Status, Is.EqualTo(66));
//         Assert.That(cases[2].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(cases[3].SiteId, Is.EqualTo(sites[2].Id));
//         // Assert.That(cases[3].CheckListId, Is.EqualTo(floatingLayerEformId));
//         // Assert.That(cases[3].FolderId, Is.Null);
//         // Assert.That(cases[3].Status, Is.EqualTo(66));
//         // Assert.That(cases[3].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(cases[4].SiteId, Is.EqualTo(sites[3].Id));
//         // Assert.That(cases[4].CheckListId, Is.EqualTo(floatingLayerEformId));
//         // Assert.That(cases[4].FolderId, Is.Null);
//         // Assert.That(cases[4].Status, Is.EqualTo(66));
//         // Assert.That(cases[4].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(cases[5].SiteId, Is.EqualTo(sites[2].Id));
//         // Assert.That(cases[5].CheckListId, Is.EqualTo(alarmeFormid));
//         // Assert.That(cases[5].FolderId, Is.Null);
//         // Assert.That(cases[5].Status, Is.EqualTo(66));
//         // Assert.That(cases[5].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(cases[6].SiteId, Is.EqualTo(sites[3].Id));
//         // Assert.That(cases[6].CheckListId, Is.EqualTo(alarmeFormid));
//         // Assert.That(cases[6].FolderId, Is.Null);
//         // Assert.That(cases[6].Status, Is.EqualTo(66));
//         // Assert.That(cases[6].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(cases[7].SiteId, Is.EqualTo(sites[2].Id));
//         // Assert.That(cases[7].CheckListId, Is.EqualTo(areaRules[0].EformId));
//         // Assert.That(cases[7].FolderId, Is.Null);
//         // Assert.That(cases[7].Status, Is.EqualTo(66));
//         // Assert.That(cases[7].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//         // Assert.That(cases[8].SiteId, Is.EqualTo(sites[3].Id));
//         // Assert.That(cases[8].CheckListId, Is.EqualTo(areaRules[0].EformId));
//         // Assert.That(cases[8].FolderId, Is.Null);
//         // Assert.That(cases[8].Status, Is.EqualTo(66));
//         // Assert.That(cases[8].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
//     }
// }