using BackendConfiguration.Pn.Infrastructure.Helpers;
using BackendConfiguration.Pn.Infrastructure.Models;
using BackendConfiguration.Pn.Infrastructure.Models.AreaRules;
using BackendConfiguration.Pn.Infrastructure.Models.AssignmentWorker;
using BackendConfiguration.Pn.Infrastructure.Models.Properties;
using BackendConfiguration.Pn.Infrastructure.Models.PropertyAreas;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Infrastructure.Models.Common;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.ItemsPlanningBase.Infrastructure.Enums;

namespace BackendConfiguration.Pn.Integration.Test;

[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class BackendConfigurationAreaRulePlanningsServiceHelperTestLogBooks : TestBaseSetup
{
    // Should test the UpdatePlanning method for area rule "00. Logbøger" with repeat type "days" and repeat every "2"
    [Test]
    public async Task UpdatePlanning_AreaRuleDays2_ReturnsSuccess()
    {
        // Arrange
        var core = await GetCore();
        var englishLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "en-US");
        var germanLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "de-DE");
        var danishLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "da");
        var danishName = new Guid().ToString();
        var englishName = new Guid().ToString();
        var germanName = new Guid().ToString();
        var propertyCreateModel = new PropertyCreateModel
        {
            Address = Guid.NewGuid().ToString(),
            Chr = Guid.NewGuid().ToString(),
            IndustryCode = Guid.NewGuid().ToString(),
            Cvr = Guid.NewGuid().ToString(),
            IsFarm = false,
            LanguagesIds = new List<int>
            {
                1
            },
            MainMailAddress = Guid.NewGuid().ToString(),
            Name = Guid.NewGuid().ToString(),
            WorkorderEnable = false
        };

        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1, BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, 1, 1);

        var deviceUserModel = new DeviceUserModel
        {
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = false,
            UserFirstName = Guid.NewGuid().ToString(),
            UserLastName = Guid.NewGuid().ToString()
        };

        await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            TimePlanningPnDbContext);

        var properties = await BackendConfigurationPnDbContext!.Properties.ToListAsync();
        var sites = await MicrotingDbContext!.Sites.AsNoTracking().ToListAsync();

        var propertyAssignWorkersModel = new PropertyAssignWorkersModel
        {
            Assignments = new List<PropertyAssignmentWorkerModel>
            {
                new()
                {
                    PropertyId = properties[0].Id,
                    IsChecked = true
                }
            },
            SiteId = sites[2].Id
        };

        await BackendConfigurationAssignmentWorkerServiceHelper.Create(propertyAssignWorkersModel, core, 1,
            BackendConfigurationPnDbContext, CaseTemplatePnDbContext, "location", Bus);

        var areaTranslation = await BackendConfigurationPnDbContext!.AreaTranslations.FirstAsync(x => x.Name == "00. Logbøger");
        var areaId = areaTranslation.AreaId;
        var currentSite = await MicrotingDbContext!.Sites.OrderByDescending(x => x.Id).FirstAsync();

        var propertyAreasUpdateModel = new PropertyAreasUpdateModel
        {
            Areas = new List<PropertyAreaModel>
            {
                new()
                {
                    AreaId = areaTranslation.AreaId,
                    Activated = true
                }
            },
            PropertyId = properties[0].Id
        };

        var result = await BackendConfigurationPropertyAreasServiceHelper.Update(propertyAreasUpdateModel, core, BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, 1);

        var checkListTranslation = await MicrotingDbContext.CheckListTranslations.FirstAsync(x => x.Text == "01. Gyllekøling");

        AreaRulesCreateModel areaRulesCreateModel = new AreaRulesCreateModel
        {
            AreaRules = new List<AreaRuleCreateModel>
            {
                new()
                {
                    TranslatedNames = new List<CommonDictionaryModel>
                    {
                        new()
                        {
                            Name = danishName, Description = "00. Logbøger",
                            Id = danishLanguage.Id
                        },
                        new()
                        {
                            Name = englishName, Description = "00. Logbooks",
                            Id = englishLanguage.Id
                        },
                        new()
                        {
                            Name = germanName, Description = "00. Logbücher",
                            Id = germanLanguage.Id
                        }
                    },
                    TypeSpecificFields = new TypeSpecificFields
                    {
                        Alarm = AreaRuleT2AlarmsEnum.Yes,
                        DayOfWeek = 1,
                        EformId = checkListTranslation.CheckListId,
                        Type = AreaRuleT2TypesEnum.Open
                    }
                }
            },
            PropertyAreaId = properties[0].Id
        };

        await BackendConfigurationAreaRulesServiceHelper.Create(areaRulesCreateModel, core, 1, BackendConfigurationPnDbContext, danishLanguage);

        var areaRules = await BackendConfigurationPnDbContext!.AreaRules.Where(x => x.PropertyId == properties[0].Id).ToListAsync();

        // should create AreaRulePlanningModel for areaId
        var areaRulePlanningModel = new AreaRulePlanningModel
        {
            AssignedSites = new List<AreaRuleAssignedSitesModel>
            {
                new()
                {
                    Checked = true,
                    SiteId = currentSite.Id
                }
            },
            RuleId = areaRules[0].Id,
            ComplianceEnabled = true,
            PropertyId = properties[0].Id,
            Status = true,
            SendNotifications = true,
            StartDate = DateTime.UtcNow,
            TypeSpecificFields = new AreaRuleTypePlanningModel
            {
                Alarm = AreaRuleT2AlarmsEnum.No,
                DayOfMonth = 0,
                DayOfWeek = 0,
                HoursAndEnergyEnabled = false,
                RepeatEvery = 2,
                RepeatType = 1
            }

        };

        // Act
        await BackendConfigurationAreaRulePlanningsServiceHelper.UpdatePlanning(areaRulePlanningModel,
            core, 1, BackendConfigurationPnDbContext, ItemsPlanningPnDbContext);

        // Assert
        var areaRuleTranslations = await BackendConfigurationPnDbContext!.AreaRuleTranslations
            .Join(BackendConfigurationPnDbContext.AreaRules, atr => atr.AreaRuleId, ar => ar.Id,
                (atr, ar) => new { atr, ar }).Where(x => x.ar.PropertyId == properties[0].Id).ToListAsync();
        var areaProperties = await BackendConfigurationPnDbContext!.AreaProperties.Where(x => x.PropertyId == properties[0].Id).ToListAsync();
        var folderTranslations = await MicrotingDbContext!.FolderTranslations.ToListAsync();
        var areaRulePlannings = await BackendConfigurationPnDbContext!.AreaRulePlannings.ToListAsync();
        var planningSites = await BackendConfigurationPnDbContext.PlanningSites.ToListAsync();
        var plannings = await ItemsPlanningPnDbContext!.Plannings.ToListAsync();
        var planningNameTranslations = await ItemsPlanningPnDbContext.PlanningNameTranslation.ToListAsync();
        var itemPlanningSites = await ItemsPlanningPnDbContext!.PlanningSites.ToListAsync();
        var itemPlanningCases = await ItemsPlanningPnDbContext!.PlanningCases.ToListAsync();
        var itemPlanningCaseSites = await ItemsPlanningPnDbContext!.PlanningCaseSites.ToListAsync();
        var compliances = await BackendConfigurationPnDbContext!.Compliances.ToListAsync();
        var checkListSites = await MicrotingDbContext!.CheckListSites.ToListAsync();
        var cases = await MicrotingDbContext!.Cases.ToListAsync();

        // Assert result
        Assert.NotNull(result);
        Assert.That(result.Success, Is.EqualTo(true));

        // Assert areaRules
        Assert.NotNull(areaRules);
        Assert.That(areaRules.Count, Is.EqualTo(1));
        Assert.That(areaRules[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[0].EformName, Is.EqualTo("01. Gyllekøling"));

        // Assert areaRuleTranslations
        Assert.NotNull(areaRuleTranslations);
        Assert.That(areaRuleTranslations.Count, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[0].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[0].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[0].atr.Name, Is.EqualTo(danishName));
        Assert.That(areaRuleTranslations[1].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[1].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[1].atr.Name, Is.EqualTo(englishName));
        Assert.That(areaRuleTranslations[2].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[2].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[2].atr.Name, Is.EqualTo(germanName));

        // Assert areaProperties
        Assert.NotNull(areaProperties);
        Assert.That(areaProperties.Count, Is.EqualTo(1));
        Assert.That(areaProperties[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaProperties[0].AreaId, Is.EqualTo(areaTranslation.AreaId));

        // Assert folder translations
        Assert.That(folderTranslations, Is.Not.Null);
        Assert.That(folderTranslations.Count, Is.EqualTo(31));
        Assert.That(folderTranslations[4].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[4].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[5].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[5].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[6].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[6].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[7].Name, Is.EqualTo("00.00 Opret ny opgave"));
        Assert.That(folderTranslations[7].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[8].Name, Is.EqualTo("00.00 Create a new task"));
        Assert.That(folderTranslations[8].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[9].Name, Is.EqualTo("00.00 Erstellen Sie eine neue Aufgabe"));
        Assert.That(folderTranslations[9].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[10].Name, Is.EqualTo("00.02 Mine øvrige opgaver"));
        Assert.That(folderTranslations[10].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[11].Name, Is.EqualTo("00.02 My other tasks"));
        Assert.That(folderTranslations[11].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[12].Name, Is.EqualTo("00.02 Meine anderen Aufgaben"));
        Assert.That(folderTranslations[12].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[13].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[13].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[14].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[14].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[15].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[15].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[16].Name, Is.EqualTo("00.03 Andres opgaver"));
        Assert.That(folderTranslations[16].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[17].Name, Is.EqualTo("00.03 Others' tasks"));
        Assert.That(folderTranslations[17].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[18].Name, Is.EqualTo("00.03 Aufgaben anderer"));
        Assert.That(folderTranslations[18].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[19].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[19].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[20].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[20].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[21].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[21].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[22].Name, Is.EqualTo("00.01 Mine hasteopgaver"));
        Assert.That(folderTranslations[22].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[23].Name, Is.EqualTo("00.01 My urgent tasks"));
        Assert.That(folderTranslations[23].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[24].Name, Is.EqualTo("00.01 Meine dringenden Aufgaben"));
        Assert.That(folderTranslations[24].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[25].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[25].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[26].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[26].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[27].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[27].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[28].Name, Is.EqualTo("00. Logbøger"));
        Assert.That(folderTranslations[28].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[29].Name, Is.EqualTo("01. Log books")); // TODO: This should be "00. Log books"
        Assert.That(folderTranslations[29].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[30].Name, Is.EqualTo("01. Logbücher")); // TODO: This should be "00. Logbücher"
        Assert.That(folderTranslations[30].LanguageId, Is.EqualTo(3));

        // Assert AreaRulePlannings
        Assert.That(areaRulePlannings, Is.Not.Null);
        Assert.That(areaRulePlannings.Count, Is.EqualTo(1));
        Assert.That(areaRulePlannings[0].AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRulePlannings[0].ItemPlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(areaRulePlannings[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRulePlannings[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRulePlannings[0].ComplianceEnabled, Is.EqualTo(true));
        Assert.That(areaRulePlannings[0].SendNotifications, Is.EqualTo(true));

        // Assert plannings
        Assert.That(plannings, Is.Not.Null);
        Assert.That(plannings.Count, Is.EqualTo(1));
        Assert.That(plannings[0].RelatedEFormId, Is.EqualTo(areaRules[0].EformId));
        Assert.That(plannings[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
        Assert.That(plannings[0].SdkFolderId, Is.EqualTo(areaRules[0].FolderId));
        Assert.That(plannings[0].LastExecutedTime, Is.Not.Null);
        Assert.That(plannings[0].LastExecutedTime, Is.EqualTo(new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 0,0,0)));
        var now = DateTime.UtcNow;
        var diff = (now - new DateTime(now.Year, 1, 1)).TotalDays;
        var multiplier = (int) (diff / 2);
        var nextExecutionTime =
            new DateTime(now.Year, 1, 1).AddDays(multiplier * 2);
        if (nextExecutionTime < now)
        {
            nextExecutionTime = nextExecutionTime.AddDays(2);
        }

        Assert.That(plannings[0].NextExecutionTime, Is.EqualTo(nextExecutionTime));
        Assert.That(plannings[0].RepeatEvery, Is.EqualTo(2));
        Assert.That(plannings[0].RepeatType, Is.EqualTo(RepeatType.Day));

        // Assert planningNameTranslations
        Assert.That(planningNameTranslations, Is.Not.Null);
        Assert.That(planningNameTranslations.Count, Is.EqualTo(3));
        Assert.That(planningNameTranslations[0].Name, Is.EqualTo(danishName));
        Assert.That(planningNameTranslations[0].LanguageId, Is.EqualTo(danishLanguage.Id));
        Assert.That(planningNameTranslations[0].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(planningNameTranslations[1].Name, Is.EqualTo(englishName));
        Assert.That(planningNameTranslations[1].LanguageId, Is.EqualTo(englishLanguage.Id));
        Assert.That(planningNameTranslations[1].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(planningNameTranslations[2].Name, Is.EqualTo(germanName));
        Assert.That(planningNameTranslations[2].LanguageId, Is.EqualTo(germanLanguage.Id));
        Assert.That(planningNameTranslations[2].PlanningId, Is.EqualTo(plannings[0].Id));

        // Assert planningSites
        Assert.That(planningSites, Is.Not.Null);
        Assert.That(planningSites.Count, Is.EqualTo(1));
        Assert.That(planningSites[0].AreaRulePlanningsId, Is.EqualTo(areaRulePlannings[0].Id));
        Assert.That(planningSites[0].AreaId, Is.EqualTo(areaId));
        Assert.That(planningSites[0].SiteId, Is.EqualTo(sites[2].Id));
        Assert.That(planningSites[0].Status, Is.EqualTo(33));
        Assert.That(planningSites[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));

        // Assert itemPlanningSites
        Assert.That(itemPlanningSites, Is.Not.Null);
        Assert.That(itemPlanningSites.Count, Is.EqualTo(1));
        Assert.That(itemPlanningSites[0].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(itemPlanningSites[0].SiteId, Is.EqualTo(sites[2].Id));
        Assert.That(itemPlanningSites[0].LastExecutedTime, Is.Null);

        // Assert itemPlanningCases
        Assert.That(itemPlanningCases, Is.Not.Null);
        Assert.That(itemPlanningCases.Count, Is.EqualTo(1));
        Assert.That(itemPlanningCases[0].PlanningId, Is.EqualTo(plannings[0].Id));

        // Assert itemPlanningCaseSites
        Assert.That(itemPlanningCaseSites, Is.Not.Null);
        Assert.That(itemPlanningCaseSites.Count, Is.EqualTo(1));
        Assert.That(itemPlanningCaseSites[0].PlanningCaseId, Is.EqualTo(itemPlanningCases[0].Id));
        Assert.That(itemPlanningCaseSites[0].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(itemPlanningCaseSites[0].MicrotingSdkSiteId, Is.EqualTo(sites[2].Id));
        Assert.That(itemPlanningCaseSites[0].MicrotingSdkCaseId, Is.EqualTo(cases[0].Id));
        Assert.That(itemPlanningCaseSites[0].Status, Is.EqualTo(66));
        Assert.That(itemPlanningCaseSites[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
        Assert.That(itemPlanningCaseSites[0].MicrotingCheckListSitId, Is.EqualTo(0));

        // Assert compliances
        Assert.That(compliances, Is.Not.Null);
        Assert.That(compliances.Count, Is.EqualTo(0));

        // Assert checkListSites
        Assert.That(checkListSites, Is.Not.Null);
        Assert.That(checkListSites.Count, Is.EqualTo(0));

        // Assert cases
        Assert.That(cases, Is.Not.Null);
        Assert.That(cases.Count, Is.EqualTo(1));
        Assert.That(cases[0].SiteId, Is.EqualTo(sites[2].Id));
        Assert.That(cases[0].CheckListId, Is.EqualTo(areaRules[0].EformId));
        Assert.That(cases[0].FolderId, Is.Null);
        Assert.That(cases[0].Status, Is.EqualTo(66));
        Assert.That(cases[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
    }

    // Should test the UpdatePlanning method for area rule "00. Logbøger" with repeat type "weeks" and repeat every "1"
    [Test]
    public async Task UpdatePlanning_AreaRuleWeeks1Monday_ReturnsSuccess()
    {
        // Arrange
        var core = await GetCore();
        var englishLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "en-US");
        var germanLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "de-DE");
        var danishLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "da");
        var danishName = new Guid().ToString();
        var englishName = new Guid().ToString();
        var germanName = new Guid().ToString();
        var propertyCreateModel = new PropertyCreateModel
        {
            Address = Guid.NewGuid().ToString(),
            Chr = Guid.NewGuid().ToString(),
            IndustryCode = Guid.NewGuid().ToString(),
            Cvr = Guid.NewGuid().ToString(),
            IsFarm = false,
            LanguagesIds = new List<int>
            {
                1
            },
            MainMailAddress = Guid.NewGuid().ToString(),
            Name = Guid.NewGuid().ToString(),
            WorkorderEnable = false
        };

        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1, BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, 1, 1);

        var deviceUserModel = new DeviceUserModel
        {
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = false,
            UserFirstName = Guid.NewGuid().ToString(),
            UserLastName = Guid.NewGuid().ToString()
        };

        await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            TimePlanningPnDbContext);

        var properties = await BackendConfigurationPnDbContext!.Properties.ToListAsync();
        var sites = await MicrotingDbContext!.Sites.AsNoTracking().ToListAsync();

        var propertyAssignWorkersModel = new PropertyAssignWorkersModel
        {
            Assignments = new List<PropertyAssignmentWorkerModel>
            {
                new()
                {
                    PropertyId = properties[0].Id,
                    IsChecked = true
                }
            },
            SiteId = sites[2].Id
        };

        await BackendConfigurationAssignmentWorkerServiceHelper.Create(propertyAssignWorkersModel, core, 1,
            BackendConfigurationPnDbContext, CaseTemplatePnDbContext, "location", Bus);

        var areaTranslation = await BackendConfigurationPnDbContext!.AreaTranslations.FirstAsync(x => x.Name == "00. Logbøger");
        var areaId = areaTranslation.AreaId;
        var currentSite = await MicrotingDbContext!.Sites.OrderByDescending(x => x.Id).FirstAsync();

        var propertyAreasUpdateModel = new PropertyAreasUpdateModel
        {
            Areas = new List<PropertyAreaModel>
            {
                new()
                {
                    AreaId = areaTranslation.AreaId,
                    Activated = true
                }
            },
            PropertyId = properties[0].Id
        };

        var result = await BackendConfigurationPropertyAreasServiceHelper.Update(propertyAreasUpdateModel, core, BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, 1);

        var checkListTranslation = await MicrotingDbContext.CheckListTranslations.FirstAsync(x => x.Text == "01. Gyllekøling");

        AreaRulesCreateModel areaRulesCreateModel = new AreaRulesCreateModel
        {
            AreaRules = new List<AreaRuleCreateModel>
            {
                new()
                {
                    TranslatedNames = new List<CommonDictionaryModel>
                    {
                        new()
                        {
                            Name = danishName, Description = "00. Logbøger",
                            Id = danishLanguage.Id
                        },
                        new()
                        {
                            Name = englishName, Description = "00. Logbooks",
                            Id = englishLanguage.Id
                        },
                        new()
                        {
                            Name = germanName, Description = "00. Logbücher",
                            Id = germanLanguage.Id
                        }
                    },
                    TypeSpecificFields = new TypeSpecificFields
                    {
                        Alarm = AreaRuleT2AlarmsEnum.Yes,
                        DayOfWeek = 1,
                        EformId = checkListTranslation.CheckListId,
                        Type = AreaRuleT2TypesEnum.Open
                    }
                }
            },
            PropertyAreaId = properties[0].Id
        };

        await BackendConfigurationAreaRulesServiceHelper.Create(areaRulesCreateModel, core, 1, BackendConfigurationPnDbContext, danishLanguage);

        var areaRules = await BackendConfigurationPnDbContext!.AreaRules.Where(x => x.PropertyId == properties[0].Id).ToListAsync();

        // should create AreaRulePlanningModel for areaId
        var areaRulePlanningModel = new AreaRulePlanningModel
        {
            AssignedSites = new List<AreaRuleAssignedSitesModel>
            {
                new()
                {
                    Checked = true,
                    SiteId = currentSite.Id
                }
            },
            RuleId = areaRules[0].Id,
            ComplianceEnabled = true,
            PropertyId = properties[0].Id,
            Status = true,
            SendNotifications = true,
            StartDate = DateTime.UtcNow,
            TypeSpecificFields = new AreaRuleTypePlanningModel
            {
                Alarm = AreaRuleT2AlarmsEnum.No,
                DayOfMonth = 0,
                DayOfWeek = 0,
                HoursAndEnergyEnabled = false,
                RepeatEvery = 1,
                RepeatType = 2
            }

        };

        // Act
        await BackendConfigurationAreaRulePlanningsServiceHelper.UpdatePlanning(areaRulePlanningModel,
            core, 1, BackendConfigurationPnDbContext, ItemsPlanningPnDbContext);

        // Assert
        var areaRuleTranslations = await BackendConfigurationPnDbContext!.AreaRuleTranslations
            .Join(BackendConfigurationPnDbContext.AreaRules, atr => atr.AreaRuleId, ar => ar.Id,
                (atr, ar) => new { atr, ar }).Where(x => x.ar.PropertyId == properties[0].Id).ToListAsync();
        var areaProperties = await BackendConfigurationPnDbContext!.AreaProperties.Where(x => x.PropertyId == properties[0].Id).ToListAsync();
        var folderTranslations = await MicrotingDbContext!.FolderTranslations.ToListAsync();
        var areaRulePlannings = await BackendConfigurationPnDbContext!.AreaRulePlannings.ToListAsync();
        var planningSites = await BackendConfigurationPnDbContext.PlanningSites.ToListAsync();
        var plannings = await ItemsPlanningPnDbContext!.Plannings.ToListAsync();
        var planningNameTranslations = await ItemsPlanningPnDbContext.PlanningNameTranslation.ToListAsync();
        var itemPlanningSites = await ItemsPlanningPnDbContext!.PlanningSites.ToListAsync();
        var itemPlanningCases = await ItemsPlanningPnDbContext!.PlanningCases.ToListAsync();
        var itemPlanningCaseSites = await ItemsPlanningPnDbContext!.PlanningCaseSites.ToListAsync();
        var compliances = await BackendConfigurationPnDbContext!.Compliances.ToListAsync();
        var checkListSites = await MicrotingDbContext!.CheckListSites.ToListAsync();
        var cases = await MicrotingDbContext!.Cases.ToListAsync();

        // Assert result
        Assert.NotNull(result);
        Assert.That(result.Success, Is.EqualTo(true));

        // Assert areaRules
        Assert.NotNull(areaRules);
        Assert.That(areaRules.Count, Is.EqualTo(1));
        Assert.That(areaRules[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[0].EformName, Is.EqualTo("01. Gyllekøling"));

        // Assert areaRuleTranslations
        Assert.NotNull(areaRuleTranslations);
        Assert.That(areaRuleTranslations.Count, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[0].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[0].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[0].atr.Name, Is.EqualTo(danishName));
        Assert.That(areaRuleTranslations[1].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[1].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[1].atr.Name, Is.EqualTo(englishName));
        Assert.That(areaRuleTranslations[2].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[2].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[2].atr.Name, Is.EqualTo(germanName));

        // Assert areaProperties
        Assert.NotNull(areaProperties);
        Assert.That(areaProperties.Count, Is.EqualTo(1));
        Assert.That(areaProperties[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaProperties[0].AreaId, Is.EqualTo(areaTranslation.AreaId));

        // Assert folder translations
        Assert.That(folderTranslations, Is.Not.Null);
        Assert.That(folderTranslations.Count, Is.EqualTo(31));
        Assert.That(folderTranslations[4].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[4].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[5].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[5].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[6].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[6].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[7].Name, Is.EqualTo("00.00 Opret ny opgave"));
        Assert.That(folderTranslations[7].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[8].Name, Is.EqualTo("00.00 Create a new task"));
        Assert.That(folderTranslations[8].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[9].Name, Is.EqualTo("00.00 Erstellen Sie eine neue Aufgabe"));
        Assert.That(folderTranslations[9].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[10].Name, Is.EqualTo("00.02 Mine øvrige opgaver"));
        Assert.That(folderTranslations[10].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[11].Name, Is.EqualTo("00.02 My other tasks"));
        Assert.That(folderTranslations[11].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[12].Name, Is.EqualTo("00.02 Meine anderen Aufgaben"));
        Assert.That(folderTranslations[12].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[13].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[13].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[14].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[14].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[15].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[15].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[16].Name, Is.EqualTo("00.03 Andres opgaver"));
        Assert.That(folderTranslations[16].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[17].Name, Is.EqualTo("00.03 Others' tasks"));
        Assert.That(folderTranslations[17].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[18].Name, Is.EqualTo("00.03 Aufgaben anderer"));
        Assert.That(folderTranslations[18].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[19].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[19].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[20].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[20].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[21].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[21].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[22].Name, Is.EqualTo("00.01 Mine hasteopgaver"));
        Assert.That(folderTranslations[22].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[23].Name, Is.EqualTo("00.01 My urgent tasks"));
        Assert.That(folderTranslations[23].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[24].Name, Is.EqualTo("00.01 Meine dringenden Aufgaben"));
        Assert.That(folderTranslations[24].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[25].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[25].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[26].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[26].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[27].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[27].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[28].Name, Is.EqualTo("00. Logbøger"));
        Assert.That(folderTranslations[28].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[29].Name, Is.EqualTo("01. Log books")); // TODO: This should be "00. Log books"
        Assert.That(folderTranslations[29].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[30].Name, Is.EqualTo("01. Logbücher")); // TODO: This should be "00. Logbücher"
        Assert.That(folderTranslations[30].LanguageId, Is.EqualTo(3));

        // Assert AreaRulePlannings
        Assert.That(areaRulePlannings, Is.Not.Null);
        Assert.That(areaRulePlannings.Count, Is.EqualTo(1));
        Assert.That(areaRulePlannings[0].AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRulePlannings[0].ItemPlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(areaRulePlannings[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRulePlannings[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRulePlannings[0].ComplianceEnabled, Is.EqualTo(true));
        Assert.That(areaRulePlannings[0].SendNotifications, Is.EqualTo(true));

        // Assert plannings
        Assert.That(plannings, Is.Not.Null);
        Assert.That(plannings.Count, Is.EqualTo(1));
        Assert.That(plannings[0].RelatedEFormId, Is.EqualTo(areaRules[0].EformId));
        Assert.That(plannings[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
        Assert.That(plannings[0].SdkFolderId, Is.EqualTo(areaRules[0].FolderId));
        Assert.That(plannings[0].LastExecutedTime, Is.Not.Null);
        Assert.That(plannings[0].LastExecutedTime, Is.EqualTo(new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 0,0,0)));
        var now = DateTime.UtcNow;
        var diff = (now - new DateTime(now.Year, 1, 1)).TotalDays;
        var multiplier = (int) (diff / 7);
        var dayOfWeek = 1;
        // if (dayOfWeek == 0)
        // {
        //     dayOfWeek = 7;
        // }

        var startOfWeek =
            new DateTime(now.Year, 1, 1, 0, 0, 0).StartOfWeek(
                (DayOfWeek) dayOfWeek);
        if (startOfWeek.Year != now.Year)
        {
            startOfWeek = startOfWeek.AddDays(7);
        }

        var nextExecutionTime =
            startOfWeek.AddDays(multiplier * 7);
        if (nextExecutionTime < now)
        {
            nextExecutionTime = nextExecutionTime.AddDays(7);
        }

        Assert.That(plannings[0].NextExecutionTime, Is.EqualTo(nextExecutionTime));
        Assert.That(plannings[0].RepeatEvery, Is.EqualTo(1));
        Assert.That(plannings[0].RepeatType, Is.EqualTo(RepeatType.Week));
        Assert.That(plannings[0].DayOfWeek, Is.EqualTo(DayOfWeek.Monday));

        // Assert planningNameTranslations
        Assert.That(planningNameTranslations, Is.Not.Null);
        Assert.That(planningNameTranslations.Count, Is.EqualTo(3));
        Assert.That(planningNameTranslations[0].Name, Is.EqualTo(danishName));
        Assert.That(planningNameTranslations[0].LanguageId, Is.EqualTo(danishLanguage.Id));
        Assert.That(planningNameTranslations[0].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(planningNameTranslations[1].Name, Is.EqualTo(englishName));
        Assert.That(planningNameTranslations[1].LanguageId, Is.EqualTo(englishLanguage.Id));
        Assert.That(planningNameTranslations[1].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(planningNameTranslations[2].Name, Is.EqualTo(germanName));
        Assert.That(planningNameTranslations[2].LanguageId, Is.EqualTo(germanLanguage.Id));
        Assert.That(planningNameTranslations[2].PlanningId, Is.EqualTo(plannings[0].Id));

        // Assert planningSites
        Assert.That(planningSites, Is.Not.Null);
        Assert.That(planningSites.Count, Is.EqualTo(1));
        Assert.That(planningSites[0].AreaRulePlanningsId, Is.EqualTo(areaRulePlannings[0].Id));
        Assert.That(planningSites[0].AreaId, Is.EqualTo(areaId));
        Assert.That(planningSites[0].SiteId, Is.EqualTo(sites[2].Id));
        Assert.That(planningSites[0].Status, Is.EqualTo(33));
        Assert.That(planningSites[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));

        // Assert itemPlanningSites
        Assert.That(itemPlanningSites, Is.Not.Null);
        Assert.That(itemPlanningSites.Count, Is.EqualTo(1));
        Assert.That(itemPlanningSites[0].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(itemPlanningSites[0].SiteId, Is.EqualTo(sites[2].Id));
        Assert.That(itemPlanningSites[0].LastExecutedTime, Is.Null);

        // Assert itemPlanningCases
        Assert.That(itemPlanningCases, Is.Not.Null);
        Assert.That(itemPlanningCases.Count, Is.EqualTo(1));
        Assert.That(itemPlanningCases[0].PlanningId, Is.EqualTo(plannings[0].Id));

        // Assert itemPlanningCaseSites
        Assert.That(itemPlanningCaseSites, Is.Not.Null);
        Assert.That(itemPlanningCaseSites.Count, Is.EqualTo(1));
        Assert.That(itemPlanningCaseSites[0].PlanningCaseId, Is.EqualTo(itemPlanningCases[0].Id));
        Assert.That(itemPlanningCaseSites[0].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(itemPlanningCaseSites[0].MicrotingSdkSiteId, Is.EqualTo(sites[2].Id));
        Assert.That(itemPlanningCaseSites[0].MicrotingSdkCaseId, Is.EqualTo(cases[0].Id));
        Assert.That(itemPlanningCaseSites[0].Status, Is.EqualTo(66));
        Assert.That(itemPlanningCaseSites[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
        Assert.That(itemPlanningCaseSites[0].MicrotingCheckListSitId, Is.EqualTo(0));

        // Assert compliances
        Assert.That(compliances, Is.Not.Null);
        Assert.That(compliances.Count, Is.EqualTo(0));

        // Assert checkListSites
        Assert.That(checkListSites, Is.Not.Null);
        Assert.That(checkListSites.Count, Is.EqualTo(0));

        // Assert cases
        Assert.That(cases, Is.Not.Null);
        Assert.That(cases.Count, Is.EqualTo(1));
        Assert.That(cases[0].SiteId, Is.EqualTo(sites[2].Id));
        Assert.That(cases[0].CheckListId, Is.EqualTo(areaRules[0].EformId));
        Assert.That(cases[0].FolderId, Is.Null);
        Assert.That(cases[0].Status, Is.EqualTo(66));
        Assert.That(cases[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
    }

    // Should test the UpdatePlanning method for area rule "00. Logbøger" with repeat type "weeks" and repeat every "2" Wednesday
    [Test]
    public async Task UpdatePlanning_AreaRuleWeeks2Wednesday_ReturnsSuccess()
    {
        // Arrange
        var core = await GetCore();
        var englishLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "en-US");
        var germanLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "de-DE");
        var danishLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "da");
        var danishName = new Guid().ToString();
        var englishName = new Guid().ToString();
        var germanName = new Guid().ToString();
        var propertyCreateModel = new PropertyCreateModel
        {
            Address = Guid.NewGuid().ToString(),
            Chr = Guid.NewGuid().ToString(),
            IndustryCode = Guid.NewGuid().ToString(),
            Cvr = Guid.NewGuid().ToString(),
            IsFarm = false,
            LanguagesIds = new List<int>
            {
                1
            },
            MainMailAddress = Guid.NewGuid().ToString(),
            Name = Guid.NewGuid().ToString(),
            WorkorderEnable = false
        };

        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1, BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, 1, 1);

        var deviceUserModel = new DeviceUserModel
        {
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = false,
            UserFirstName = Guid.NewGuid().ToString(),
            UserLastName = Guid.NewGuid().ToString()
        };

        await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            TimePlanningPnDbContext);

        var properties = await BackendConfigurationPnDbContext!.Properties.ToListAsync();
        var sites = await MicrotingDbContext!.Sites.AsNoTracking().ToListAsync();

        var propertyAssignWorkersModel = new PropertyAssignWorkersModel
        {
            Assignments = new List<PropertyAssignmentWorkerModel>
            {
                new()
                {
                    PropertyId = properties[0].Id,
                    IsChecked = true
                }
            },
            SiteId = sites[2].Id
        };

        await BackendConfigurationAssignmentWorkerServiceHelper.Create(propertyAssignWorkersModel, core, 1,
            BackendConfigurationPnDbContext, CaseTemplatePnDbContext, "location", Bus);

        var areaTranslation = await BackendConfigurationPnDbContext!.AreaTranslations.FirstAsync(x => x.Name == "00. Logbøger");
        var areaId = areaTranslation.AreaId;
        var currentSite = await MicrotingDbContext!.Sites.OrderByDescending(x => x.Id).FirstAsync();

        var propertyAreasUpdateModel = new PropertyAreasUpdateModel
        {
            Areas = new List<PropertyAreaModel>
            {
                new()
                {
                    AreaId = areaTranslation.AreaId,
                    Activated = true
                }
            },
            PropertyId = properties[0].Id
        };

        var result = await BackendConfigurationPropertyAreasServiceHelper.Update(propertyAreasUpdateModel, core, BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, 1);

        var checkListTranslation = await MicrotingDbContext.CheckListTranslations.FirstAsync(x => x.Text == "01. Gyllekøling");

        AreaRulesCreateModel areaRulesCreateModel = new AreaRulesCreateModel
        {
            AreaRules = new List<AreaRuleCreateModel>
            {
                new()
                {
                    TranslatedNames = new List<CommonDictionaryModel>
                    {
                        new()
                        {
                            Name = danishName, Description = "00. Logbøger",
                            Id = danishLanguage.Id
                        },
                        new()
                        {
                            Name = englishName, Description = "00. Logbooks",
                            Id = englishLanguage.Id
                        },
                        new()
                        {
                            Name = germanName, Description = "00. Logbücher",
                            Id = germanLanguage.Id
                        }
                    },
                    TypeSpecificFields = new TypeSpecificFields
                    {
                        Alarm = AreaRuleT2AlarmsEnum.Yes,
                        DayOfWeek = 1,
                        EformId = checkListTranslation.CheckListId,
                        Type = AreaRuleT2TypesEnum.Open
                    }
                }
            },
            PropertyAreaId = properties[0].Id
        };

        await BackendConfigurationAreaRulesServiceHelper.Create(areaRulesCreateModel, core, 1, BackendConfigurationPnDbContext, danishLanguage);

        var areaRules = await BackendConfigurationPnDbContext!.AreaRules.Where(x => x.PropertyId == properties[0].Id).ToListAsync();

        // should create AreaRulePlanningModel for areaId
        var areaRulePlanningModel = new AreaRulePlanningModel
        {
            AssignedSites = new List<AreaRuleAssignedSitesModel>
            {
                new()
                {
                    Checked = true,
                    SiteId = currentSite.Id
                }
            },
            RuleId = areaRules[0].Id,
            ComplianceEnabled = true,
            PropertyId = properties[0].Id,
            Status = true,
            SendNotifications = true,
            StartDate = DateTime.UtcNow,
            TypeSpecificFields = new AreaRuleTypePlanningModel
            {
                Alarm = AreaRuleT2AlarmsEnum.No,
                DayOfMonth = 0,
                DayOfWeek = 3,
                HoursAndEnergyEnabled = false,
                RepeatEvery = 2,
                RepeatType = 2
            }

        };

        // Act
        await BackendConfigurationAreaRulePlanningsServiceHelper.UpdatePlanning(areaRulePlanningModel,
            core, 1, BackendConfigurationPnDbContext, ItemsPlanningPnDbContext);

        // Assert
        var areaRuleTranslations = await BackendConfigurationPnDbContext!.AreaRuleTranslations
            .Join(BackendConfigurationPnDbContext.AreaRules, atr => atr.AreaRuleId, ar => ar.Id,
                (atr, ar) => new { atr, ar }).Where(x => x.ar.PropertyId == properties[0].Id).ToListAsync();
        var areaProperties = await BackendConfigurationPnDbContext!.AreaProperties.Where(x => x.PropertyId == properties[0].Id).ToListAsync();
        var folderTranslations = await MicrotingDbContext!.FolderTranslations.ToListAsync();
        var areaRulePlannings = await BackendConfigurationPnDbContext!.AreaRulePlannings.ToListAsync();
        var planningSites = await BackendConfigurationPnDbContext.PlanningSites.ToListAsync();
        var plannings = await ItemsPlanningPnDbContext!.Plannings.ToListAsync();
        var planningNameTranslations = await ItemsPlanningPnDbContext.PlanningNameTranslation.ToListAsync();
        var itemPlanningSites = await ItemsPlanningPnDbContext!.PlanningSites.ToListAsync();
        var itemPlanningCases = await ItemsPlanningPnDbContext!.PlanningCases.ToListAsync();
        var itemPlanningCaseSites = await ItemsPlanningPnDbContext!.PlanningCaseSites.ToListAsync();
        var compliances = await BackendConfigurationPnDbContext!.Compliances.ToListAsync();
        var checkListSites = await MicrotingDbContext!.CheckListSites.ToListAsync();
        var cases = await MicrotingDbContext!.Cases.ToListAsync();

        // Assert result
        Assert.NotNull(result);
        Assert.That(result.Success, Is.EqualTo(true));

        // Assert areaRules
        Assert.NotNull(areaRules);
        Assert.That(areaRules.Count, Is.EqualTo(1));
        Assert.That(areaRules[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[0].EformName, Is.EqualTo("01. Gyllekøling"));

        // Assert areaRuleTranslations
        Assert.NotNull(areaRuleTranslations);
        Assert.That(areaRuleTranslations.Count, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[0].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[0].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[0].atr.Name, Is.EqualTo(danishName));
        Assert.That(areaRuleTranslations[1].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[1].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[1].atr.Name, Is.EqualTo(englishName));
        Assert.That(areaRuleTranslations[2].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[2].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[2].atr.Name, Is.EqualTo(germanName));

        // Assert areaProperties
        Assert.NotNull(areaProperties);
        Assert.That(areaProperties.Count, Is.EqualTo(1));
        Assert.That(areaProperties[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaProperties[0].AreaId, Is.EqualTo(areaTranslation.AreaId));

        // Assert folder translations
        Assert.That(folderTranslations, Is.Not.Null);
        Assert.That(folderTranslations.Count, Is.EqualTo(31));
        Assert.That(folderTranslations[4].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[4].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[5].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[5].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[6].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[6].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[7].Name, Is.EqualTo("00.00 Opret ny opgave"));
        Assert.That(folderTranslations[7].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[8].Name, Is.EqualTo("00.00 Create a new task"));
        Assert.That(folderTranslations[8].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[9].Name, Is.EqualTo("00.00 Erstellen Sie eine neue Aufgabe"));
        Assert.That(folderTranslations[9].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[10].Name, Is.EqualTo("00.02 Mine øvrige opgaver"));
        Assert.That(folderTranslations[10].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[11].Name, Is.EqualTo("00.02 My other tasks"));
        Assert.That(folderTranslations[11].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[12].Name, Is.EqualTo("00.02 Meine anderen Aufgaben"));
        Assert.That(folderTranslations[12].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[13].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[13].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[14].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[14].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[15].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[15].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[16].Name, Is.EqualTo("00.03 Andres opgaver"));
        Assert.That(folderTranslations[16].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[17].Name, Is.EqualTo("00.03 Others' tasks"));
        Assert.That(folderTranslations[17].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[18].Name, Is.EqualTo("00.03 Aufgaben anderer"));
        Assert.That(folderTranslations[18].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[19].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[19].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[20].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[20].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[21].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[21].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[22].Name, Is.EqualTo("00.01 Mine hasteopgaver"));
        Assert.That(folderTranslations[22].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[23].Name, Is.EqualTo("00.01 My urgent tasks"));
        Assert.That(folderTranslations[23].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[24].Name, Is.EqualTo("00.01 Meine dringenden Aufgaben"));
        Assert.That(folderTranslations[24].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[25].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[25].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[26].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[26].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[27].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[27].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[28].Name, Is.EqualTo("00. Logbøger"));
        Assert.That(folderTranslations[28].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[29].Name, Is.EqualTo("01. Log books")); // TODO: This should be "00. Log books"
        Assert.That(folderTranslations[29].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[30].Name, Is.EqualTo("01. Logbücher")); // TODO: This should be "00. Logbücher"
        Assert.That(folderTranslations[30].LanguageId, Is.EqualTo(3));

        // Assert AreaRulePlannings
        Assert.That(areaRulePlannings, Is.Not.Null);
        Assert.That(areaRulePlannings.Count, Is.EqualTo(1));
        Assert.That(areaRulePlannings[0].AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRulePlannings[0].ItemPlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(areaRulePlannings[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRulePlannings[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRulePlannings[0].ComplianceEnabled, Is.EqualTo(true));
        Assert.That(areaRulePlannings[0].SendNotifications, Is.EqualTo(true));

        // Assert plannings
        Assert.That(plannings, Is.Not.Null);
        Assert.That(plannings.Count, Is.EqualTo(1));
        Assert.That(plannings[0].RelatedEFormId, Is.EqualTo(areaRules[0].EformId));
        Assert.That(plannings[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
        Assert.That(plannings[0].SdkFolderId, Is.EqualTo(areaRules[0].FolderId));
        Assert.That(plannings[0].LastExecutedTime, Is.Not.Null);
        Assert.That(plannings[0].LastExecutedTime, Is.EqualTo(new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 0,0,0)));
        var now = DateTime.UtcNow;
        var diff = (now - new DateTime(now.Year, 1, 1)).TotalDays;
        var multiplier = (int) (diff / 14);
        var dayOfWeek = 3;
        // if (dayOfWeek == 0)
        // {
        //     dayOfWeek = 7;
        // }

        var startOfWeek =
            new DateTime(now.Year, 1, 1, 0, 0, 0).StartOfWeek(
                (DayOfWeek) dayOfWeek);
        if (startOfWeek.Year != now.Year)
        {
            startOfWeek = startOfWeek.AddDays(7);
        }

        var nextExecutionTime =
            startOfWeek.AddDays(multiplier * 14);
        if (nextExecutionTime < now)
        {
            nextExecutionTime = nextExecutionTime.AddDays(14);
        }

        Assert.That(plannings[0].NextExecutionTime, Is.EqualTo(nextExecutionTime));
        Assert.That(plannings[0].RepeatEvery, Is.EqualTo(2));
        Assert.That(plannings[0].RepeatType, Is.EqualTo(RepeatType.Week));
        Assert.That(plannings[0].DayOfWeek, Is.EqualTo(DayOfWeek.Wednesday));

        // Assert planningNameTranslations
        Assert.That(planningNameTranslations, Is.Not.Null);
        Assert.That(planningNameTranslations.Count, Is.EqualTo(3));
        Assert.That(planningNameTranslations[0].Name, Is.EqualTo(danishName));
        Assert.That(planningNameTranslations[0].LanguageId, Is.EqualTo(danishLanguage.Id));
        Assert.That(planningNameTranslations[0].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(planningNameTranslations[1].Name, Is.EqualTo(englishName));
        Assert.That(planningNameTranslations[1].LanguageId, Is.EqualTo(englishLanguage.Id));
        Assert.That(planningNameTranslations[1].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(planningNameTranslations[2].Name, Is.EqualTo(germanName));
        Assert.That(planningNameTranslations[2].LanguageId, Is.EqualTo(germanLanguage.Id));
        Assert.That(planningNameTranslations[2].PlanningId, Is.EqualTo(plannings[0].Id));

        // Assert planningSites
        Assert.That(planningSites, Is.Not.Null);
        Assert.That(planningSites.Count, Is.EqualTo(1));
        Assert.That(planningSites[0].AreaRulePlanningsId, Is.EqualTo(areaRulePlannings[0].Id));
        Assert.That(planningSites[0].AreaId, Is.EqualTo(areaId));
        Assert.That(planningSites[0].SiteId, Is.EqualTo(sites[2].Id));
        Assert.That(planningSites[0].Status, Is.EqualTo(33));
        Assert.That(planningSites[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));

        // Assert itemPlanningSites
        Assert.That(itemPlanningSites, Is.Not.Null);
        Assert.That(itemPlanningSites.Count, Is.EqualTo(1));
        Assert.That(itemPlanningSites[0].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(itemPlanningSites[0].SiteId, Is.EqualTo(sites[2].Id));
        Assert.That(itemPlanningSites[0].LastExecutedTime, Is.Null);

        // Assert itemPlanningCases
        Assert.That(itemPlanningCases, Is.Not.Null);
        Assert.That(itemPlanningCases.Count, Is.EqualTo(1));
        Assert.That(itemPlanningCases[0].PlanningId, Is.EqualTo(plannings[0].Id));

        // Assert itemPlanningCaseSites
        Assert.That(itemPlanningCaseSites, Is.Not.Null);
        Assert.That(itemPlanningCaseSites.Count, Is.EqualTo(1));
        Assert.That(itemPlanningCaseSites[0].PlanningCaseId, Is.EqualTo(itemPlanningCases[0].Id));
        Assert.That(itemPlanningCaseSites[0].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(itemPlanningCaseSites[0].MicrotingSdkSiteId, Is.EqualTo(sites[2].Id));
        Assert.That(itemPlanningCaseSites[0].MicrotingSdkCaseId, Is.EqualTo(cases[0].Id));
        Assert.That(itemPlanningCaseSites[0].Status, Is.EqualTo(66));
        Assert.That(itemPlanningCaseSites[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
        Assert.That(itemPlanningCaseSites[0].MicrotingCheckListSitId, Is.EqualTo(0));

        // Assert compliances
        Assert.That(compliances, Is.Not.Null);
        Assert.That(compliances.Count, Is.EqualTo(0));

        // Assert checkListSites
        Assert.That(checkListSites, Is.Not.Null);
        Assert.That(checkListSites.Count, Is.EqualTo(0));

        // Assert cases
        Assert.That(cases, Is.Not.Null);
        Assert.That(cases.Count, Is.EqualTo(1));
        Assert.That(cases[0].SiteId, Is.EqualTo(sites[2].Id));
        Assert.That(cases[0].CheckListId, Is.EqualTo(areaRules[0].EformId));
        Assert.That(cases[0].FolderId, Is.Null);
        Assert.That(cases[0].Status, Is.EqualTo(66));
        Assert.That(cases[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
    }

    // Should test the UpdatePlanning method for area rule "00. Logbøger" with repeat type "days" and repeat every "0"
    [Test]
    public async Task UpdatePlanning_AreaRuleDays0_ReturnsSuccess()
    {
        // Arrange
        var core = await GetCore();
        var englishLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "en-US");
        var germanLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "de-DE");
        var danishLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "da");
        var danishName = new Guid().ToString();
        var englishName = new Guid().ToString();
        var germanName = new Guid().ToString();
        var propertyCreateModel = new PropertyCreateModel
        {
            Address = Guid.NewGuid().ToString(),
            Chr = Guid.NewGuid().ToString(),
            IndustryCode = Guid.NewGuid().ToString(),
            Cvr = Guid.NewGuid().ToString(),
            IsFarm = false,
            LanguagesIds = new List<int>
            {
                1
            },
            MainMailAddress = Guid.NewGuid().ToString(),
            Name = Guid.NewGuid().ToString(),
            WorkorderEnable = false
        };

        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1, BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, 1, 1);

        var deviceUserModel = new DeviceUserModel
        {
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = false,
            UserFirstName = Guid.NewGuid().ToString(),
            UserLastName = Guid.NewGuid().ToString()
        };

        await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            TimePlanningPnDbContext);

        var properties = await BackendConfigurationPnDbContext!.Properties.ToListAsync();
        var sites = await MicrotingDbContext!.Sites.AsNoTracking().ToListAsync();

        var propertyAssignWorkersModel = new PropertyAssignWorkersModel
        {
            Assignments = new List<PropertyAssignmentWorkerModel>
            {
                new()
                {
                    PropertyId = properties[0].Id,
                    IsChecked = true
                }
            },
            SiteId = sites[2].Id
        };

        await BackendConfigurationAssignmentWorkerServiceHelper.Create(propertyAssignWorkersModel, core, 1,
            BackendConfigurationPnDbContext, CaseTemplatePnDbContext, "location", Bus);

        var areaTranslation = await BackendConfigurationPnDbContext!.AreaTranslations.FirstAsync(x => x.Name == "00. Logbøger");
        var areaId = areaTranslation.AreaId;
        var currentSite = await MicrotingDbContext!.Sites.OrderByDescending(x => x.Id).FirstAsync();

        var propertyAreasUpdateModel = new PropertyAreasUpdateModel
        {
            Areas = new List<PropertyAreaModel>
            {
                new()
                {
                    AreaId = areaTranslation.AreaId,
                    Activated = true
                }
            },
            PropertyId = properties[0].Id
        };

        var result = await BackendConfigurationPropertyAreasServiceHelper.Update(propertyAreasUpdateModel, core, BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, 1);
        var checkListTranslation = await MicrotingDbContext.CheckListTranslations.FirstAsync(x => x.Text == "01. Gyllekøling");

        AreaRulesCreateModel areaRulesCreateModel = new AreaRulesCreateModel
        {
            AreaRules = new List<AreaRuleCreateModel>
            {
                new()
                {
                    TranslatedNames = new List<CommonDictionaryModel>
                    {
                        new()
                        {
                            Name = danishName, Description = "00. Logbøger",
                            Id = danishLanguage.Id
                        },
                        new()
                        {
                            Name = englishName, Description = "00. Logbooks",
                            Id = englishLanguage.Id
                        },
                        new()
                        {
                            Name = germanName, Description = "00. Logbücher",
                            Id = germanLanguage.Id
                        }
                    },
                    TypeSpecificFields = new TypeSpecificFields
                    {
                        Alarm = AreaRuleT2AlarmsEnum.Yes,
                        DayOfWeek = 1,
                        EformId = checkListTranslation.CheckListId,
                        Type = AreaRuleT2TypesEnum.Open
                    }
                }
            },
            PropertyAreaId = properties[0].Id
        };

        await BackendConfigurationAreaRulesServiceHelper.Create(areaRulesCreateModel, core, 1, BackendConfigurationPnDbContext, danishLanguage);


        var areaRules = await BackendConfigurationPnDbContext!.AreaRules.Where(x => x.PropertyId == properties[0].Id).ToListAsync();

        // should create AreaRulePlanningModel for areaId
        var areaRulePlanningModel = new AreaRulePlanningModel
        {
            AssignedSites = new List<AreaRuleAssignedSitesModel>
            {
                new()
                {
                    Checked = true,
                    SiteId = currentSite.Id
                }
            },
            RuleId = areaRules[0].Id,
            ComplianceEnabled = true,
            PropertyId = properties[0].Id,
            Status = true,
            SendNotifications = true,
            StartDate = DateTime.UtcNow,
            TypeSpecificFields = new AreaRuleTypePlanningModel
            {
                Alarm = AreaRuleT2AlarmsEnum.No,
                DayOfMonth = 1,
                DayOfWeek = 0,
                HoursAndEnergyEnabled = false,
                RepeatEvery = 0,
                RepeatType = 1
            }

        };

        // Act
        await BackendConfigurationAreaRulePlanningsServiceHelper.UpdatePlanning(areaRulePlanningModel,
            core, 1, BackendConfigurationPnDbContext, ItemsPlanningPnDbContext);

        // Assert
        var areaRuleTranslations = await BackendConfigurationPnDbContext!.AreaRuleTranslations
            .Join(BackendConfigurationPnDbContext.AreaRules, atr => atr.AreaRuleId, ar => ar.Id,
                (atr, ar) => new { atr, ar }).Where(x => x.ar.PropertyId == properties[0].Id).ToListAsync();
        var areaProperties = await BackendConfigurationPnDbContext!.AreaProperties.Where(x => x.PropertyId == properties[0].Id).ToListAsync();
        var folderTranslations = await MicrotingDbContext!.FolderTranslations.ToListAsync();
        var areaRulePlannings = await BackendConfigurationPnDbContext!.AreaRulePlannings.ToListAsync();
        var planningSites = await BackendConfigurationPnDbContext.PlanningSites.ToListAsync();
        var plannings = await ItemsPlanningPnDbContext!.Plannings.ToListAsync();
        var planningNameTranslations = await ItemsPlanningPnDbContext.PlanningNameTranslation.ToListAsync();
        var itemPlanningSites = await ItemsPlanningPnDbContext!.PlanningSites.ToListAsync();
        var itemPlanningCases = await ItemsPlanningPnDbContext!.PlanningCases.ToListAsync();
        var itemPlanningCaseSites = await ItemsPlanningPnDbContext!.PlanningCaseSites.ToListAsync();
        var compliances = await BackendConfigurationPnDbContext!.Compliances.ToListAsync();
        var checkListSites = await MicrotingDbContext!.CheckListSites.ToListAsync();
        var cases = await MicrotingDbContext!.Cases.ToListAsync();

        // Assert result
        Assert.NotNull(result);
        Assert.That(result.Success, Is.EqualTo(true));

        // Assert areaRules
        Assert.NotNull(areaRules);
        Assert.That(areaRules.Count, Is.EqualTo(1));
        Assert.That(areaRules[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[0].EformName, Is.EqualTo("01. Gyllekøling"));

        // Assert areaRuleTranslations
        Assert.NotNull(areaRuleTranslations);
        Assert.That(areaRuleTranslations.Count, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[0].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[0].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[0].atr.Name, Is.EqualTo(danishName));
        Assert.That(areaRuleTranslations[1].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[1].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[1].atr.Name, Is.EqualTo(englishName));
        Assert.That(areaRuleTranslations[2].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[2].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[2].atr.Name, Is.EqualTo(germanName));

        // Assert areaProperties
        Assert.NotNull(areaProperties);
        Assert.That(areaProperties.Count, Is.EqualTo(1));
        Assert.That(areaProperties[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaProperties[0].AreaId, Is.EqualTo(areaTranslation.AreaId));

        // Assert folder translations
        Assert.That(folderTranslations, Is.Not.Null);
        Assert.That(folderTranslations.Count, Is.EqualTo(31));
        Assert.That(folderTranslations[4].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[4].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[5].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[5].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[6].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[6].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[7].Name, Is.EqualTo("00.00 Opret ny opgave"));
        Assert.That(folderTranslations[7].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[8].Name, Is.EqualTo("00.00 Create a new task"));
        Assert.That(folderTranslations[8].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[9].Name, Is.EqualTo("00.00 Erstellen Sie eine neue Aufgabe"));
        Assert.That(folderTranslations[9].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[10].Name, Is.EqualTo("00.02 Mine øvrige opgaver"));
        Assert.That(folderTranslations[10].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[11].Name, Is.EqualTo("00.02 My other tasks"));
        Assert.That(folderTranslations[11].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[12].Name, Is.EqualTo("00.02 Meine anderen Aufgaben"));
        Assert.That(folderTranslations[12].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[13].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[13].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[14].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[14].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[15].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[15].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[16].Name, Is.EqualTo("00.03 Andres opgaver"));
        Assert.That(folderTranslations[16].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[17].Name, Is.EqualTo("00.03 Others' tasks"));
        Assert.That(folderTranslations[17].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[18].Name, Is.EqualTo("00.03 Aufgaben anderer"));
        Assert.That(folderTranslations[18].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[19].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[19].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[20].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[20].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[21].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[21].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[22].Name, Is.EqualTo("00.01 Mine hasteopgaver"));
        Assert.That(folderTranslations[22].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[23].Name, Is.EqualTo("00.01 My urgent tasks"));
        Assert.That(folderTranslations[23].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[24].Name, Is.EqualTo("00.01 Meine dringenden Aufgaben"));
        Assert.That(folderTranslations[24].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[25].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[25].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[26].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[26].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[27].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[27].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[28].Name, Is.EqualTo("00. Logbøger"));
        Assert.That(folderTranslations[28].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[29].Name, Is.EqualTo("01. Log books")); // TODO: This should be "00. Log books"
        Assert.That(folderTranslations[29].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[30].Name, Is.EqualTo("01. Logbücher")); // TODO: This should be "00. Logbücher"
        Assert.That(folderTranslations[30].LanguageId, Is.EqualTo(3));

        // Assert AreaRulePlannings
        Assert.That(areaRulePlannings, Is.Not.Null);
        Assert.That(areaRulePlannings.Count, Is.EqualTo(1));
        Assert.That(areaRulePlannings[0].AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRulePlannings[0].ItemPlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(areaRulePlannings[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRulePlannings[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRulePlannings[0].ComplianceEnabled, Is.EqualTo(true));
        Assert.That(areaRulePlannings[0].SendNotifications, Is.EqualTo(true));

        // Assert plannings
        Assert.That(plannings, Is.Not.Null);
        Assert.That(plannings.Count, Is.EqualTo(1));
        Assert.That(plannings[0].RelatedEFormId, Is.EqualTo(areaRules[0].EformId));
        Assert.That(plannings[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
        Assert.That(plannings[0].SdkFolderId, Is.EqualTo(areaRules[0].FolderId));
        Assert.That(plannings[0].LastExecutedTime, Is.Not.Null);
        Assert.That(plannings[0].LastExecutedTime, Is.EqualTo(new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 0,0,0)));

        Assert.That(plannings[0].NextExecutionTime, Is.Null);
        Assert.That(plannings[0].RepeatEvery, Is.EqualTo(0));
        Assert.That(plannings[0].RepeatType, Is.EqualTo(RepeatType.Day));
        Assert.That(plannings[0].DayOfWeek, Is.EqualTo(DayOfWeek.Monday));

        // Assert planningNameTranslations
        Assert.That(planningNameTranslations, Is.Not.Null);
        Assert.That(planningNameTranslations.Count, Is.EqualTo(3));
        Assert.That(planningNameTranslations[0].Name, Is.EqualTo(danishName));
        Assert.That(planningNameTranslations[0].LanguageId, Is.EqualTo(danishLanguage.Id));
        Assert.That(planningNameTranslations[0].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(planningNameTranslations[1].Name, Is.EqualTo(englishName));
        Assert.That(planningNameTranslations[1].LanguageId, Is.EqualTo(englishLanguage.Id));
        Assert.That(planningNameTranslations[1].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(planningNameTranslations[2].Name, Is.EqualTo(germanName));
        Assert.That(planningNameTranslations[2].LanguageId, Is.EqualTo(germanLanguage.Id));
        Assert.That(planningNameTranslations[2].PlanningId, Is.EqualTo(plannings[0].Id));

        // Assert planningSites
        Assert.That(planningSites, Is.Not.Null);
        Assert.That(planningSites.Count, Is.EqualTo(1));
        Assert.That(planningSites[0].AreaRulePlanningsId, Is.EqualTo(areaRulePlannings[0].Id));
        Assert.That(planningSites[0].AreaId, Is.EqualTo(areaId));
        Assert.That(planningSites[0].SiteId, Is.EqualTo(sites[2].Id));
        Assert.That(planningSites[0].Status, Is.EqualTo(33));
        Assert.That(planningSites[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));

        // Assert itemPlanningSites
        Assert.That(itemPlanningSites, Is.Not.Null);
        Assert.That(itemPlanningSites.Count, Is.EqualTo(1));
        Assert.That(itemPlanningSites[0].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(itemPlanningSites[0].SiteId, Is.EqualTo(sites[2].Id));
        Assert.That(itemPlanningSites[0].LastExecutedTime, Is.Null);

        // Assert itemPlanningCases
        Assert.That(itemPlanningCases, Is.Not.Null);
        Assert.That(itemPlanningCases.Count, Is.EqualTo(1));
        Assert.That(itemPlanningCases[0].PlanningId, Is.EqualTo(plannings[0].Id));

        // Assert itemPlanningCaseSites
        Assert.That(itemPlanningCaseSites, Is.Not.Null);
        Assert.That(itemPlanningCaseSites.Count, Is.EqualTo(1));
        Assert.That(itemPlanningCaseSites[0].PlanningCaseId, Is.EqualTo(itemPlanningCases[0].Id));
        Assert.That(itemPlanningCaseSites[0].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(itemPlanningCaseSites[0].MicrotingSdkSiteId, Is.EqualTo(sites[2].Id));
        Assert.That(itemPlanningCaseSites[0].MicrotingSdkCaseId, Is.EqualTo(0));
        Assert.That(itemPlanningCaseSites[0].Status, Is.EqualTo(66));
        Assert.That(itemPlanningCaseSites[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
        Assert.That(itemPlanningCaseSites[0].MicrotingCheckListSitId, Is.EqualTo(checkListSites[0].Id));

        // Assert compliances
        Assert.That(compliances, Is.Not.Null);
        Assert.That(compliances.Count, Is.EqualTo(0));

        // Assert checkListSites
        Assert.That(checkListSites, Is.Not.Null);
        Assert.That(checkListSites.Count, Is.EqualTo(1));
        Assert.That(checkListSites[0].SiteId, Is.EqualTo(sites[2].Id));
        Assert.That(checkListSites[0].CheckListId, Is.EqualTo(areaRules[0].EformId));
        Assert.That(checkListSites[0].FolderId, Is.Null);
        Assert.That(checkListSites[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));

        // Assert cases
        Assert.That(cases, Is.Not.Null);
        Assert.That(cases.Count, Is.EqualTo(0));
    }

    // Should test the UpdatePlanning method for area rule "00. Logbøger" with repeat type "days" and repeat every "0" fill it and add a new site
    [Test]
    public async Task UpdatePlanning_AreaRuleDays0FillAndAdd_ReturnsSuccess()
    {
        // Arrange
        var core = await GetCore();
        var englishLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "en-US");
        var germanLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "de-DE");
        var danishLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "da");
        var danishName = new Guid().ToString();
        var englishName = new Guid().ToString();
        var germanName = new Guid().ToString();
        var propertyCreateModel = new PropertyCreateModel
        {
            Address = Guid.NewGuid().ToString(),
            Chr = Guid.NewGuid().ToString(),
            IndustryCode = Guid.NewGuid().ToString(),
            Cvr = Guid.NewGuid().ToString(),
            IsFarm = false,
            LanguagesIds = new List<int>
            {
                1
            },
            MainMailAddress = Guid.NewGuid().ToString(),
            Name = Guid.NewGuid().ToString(),
            WorkorderEnable = false
        };

        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1, BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, 1, 1);

        var deviceUserModel = new DeviceUserModel
        {
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = false,
            UserFirstName = Guid.NewGuid().ToString(),
            UserLastName = Guid.NewGuid().ToString()
        };

        await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            TimePlanningPnDbContext);

        var properties = await BackendConfigurationPnDbContext!.Properties.ToListAsync();
        var sites = await MicrotingDbContext!.Sites.AsNoTracking().ToListAsync();

        var propertyAssignWorkersModel = new PropertyAssignWorkersModel
        {
            Assignments = new List<PropertyAssignmentWorkerModel>
            {
                new()
                {
                    PropertyId = properties[0].Id,
                    IsChecked = true
                }
            },
            SiteId = sites[2].Id
        };

        await BackendConfigurationAssignmentWorkerServiceHelper.Create(propertyAssignWorkersModel, core, 1,
            BackendConfigurationPnDbContext, CaseTemplatePnDbContext, "location", Bus);

        var areaTranslation = await BackendConfigurationPnDbContext!.AreaTranslations.FirstAsync(x => x.Name == "00. Logbøger");
        var areaId = areaTranslation.AreaId;
        sites = await MicrotingDbContext!.Sites.AsNoTracking().ToListAsync();

        var propertyAreasUpdateModel = new PropertyAreasUpdateModel
        {
            Areas = new List<PropertyAreaModel>
            {
                new()
                {
                    AreaId = areaTranslation.AreaId,
                    Activated = true
                }
            },
            PropertyId = properties[0].Id
        };

        var result = await BackendConfigurationPropertyAreasServiceHelper.Update(propertyAreasUpdateModel, core, BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, 1);
        var checkListTranslation = await MicrotingDbContext.CheckListTranslations.FirstAsync(x => x.Text == "01. Gyllekøling");

        AreaRulesCreateModel areaRulesCreateModel = new AreaRulesCreateModel
        {
            AreaRules = new List<AreaRuleCreateModel>
            {
                new()
                {
                    TranslatedNames = new List<CommonDictionaryModel>
                    {
                        new()
                            {
                                Name = danishName, Description = "00. Logbøger",
                                Id = danishLanguage.Id
                            },
                        new()
                        {
                            Name = englishName, Description = "00. Logbooks",
                            Id = englishLanguage.Id
                        },
                        new()
                        {
                            Name = germanName, Description = "00. Logbücher",
                            Id = germanLanguage.Id
                        }
                    },
                    TypeSpecificFields = new TypeSpecificFields
                    {
                        Alarm = AreaRuleT2AlarmsEnum.Yes,
                        DayOfWeek = 1,
                        EformId = checkListTranslation.CheckListId,
                        Type = AreaRuleT2TypesEnum.Open
                    }
                }
            },
            PropertyAreaId = properties[0].Id
        };

        await BackendConfigurationAreaRulesServiceHelper.Create(areaRulesCreateModel, core, 1, BackendConfigurationPnDbContext, danishLanguage);
        var areaRules = await BackendConfigurationPnDbContext!.AreaRules.Where(x => x.PropertyId == properties[0].Id).ToListAsync();

        // should create AreaRulePlanningModel for areaId
        var areaRulePlanningModel = new AreaRulePlanningModel
        {
            AssignedSites = new List<AreaRuleAssignedSitesModel>
            {
                new()
                {
                    Checked = true,
                    SiteId = sites[2].Id
                }
            },
            RuleId = areaRules[0].Id,
            ComplianceEnabled = true,
            PropertyId = properties[0].Id,
            Status = true,
            SendNotifications = true,
            StartDate = DateTime.UtcNow,
            TypeSpecificFields = new AreaRuleTypePlanningModel
            {
                Alarm = AreaRuleT2AlarmsEnum.No,
                DayOfMonth = 1,
                DayOfWeek = 0,
                HoursAndEnergyEnabled = false,
                RepeatEvery = 0,
                RepeatType = 1
            }

        };

        await BackendConfigurationAreaRulePlanningsServiceHelper.UpdatePlanning(areaRulePlanningModel,
            core, 1, BackendConfigurationPnDbContext, ItemsPlanningPnDbContext);

        var deviceUserModel2 = new DeviceUserModel
        {
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = false,
            UserFirstName = Guid.NewGuid().ToString(),
            UserLastName = Guid.NewGuid().ToString()
        };

        await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel2, core, 1,
            TimePlanningPnDbContext);

        sites = await MicrotingDbContext!.Sites.AsNoTracking().ToListAsync();
        var areaRulePlannings = await BackendConfigurationPnDbContext!.AreaRulePlannings.ToListAsync();
        var itemPlanningCase = await ItemsPlanningPnDbContext!.PlanningCases.FirstAsync();

        itemPlanningCase.Status = 100;
        await itemPlanningCase.Update(ItemsPlanningPnDbContext);

        var areaRulePlanningModel2 = new AreaRulePlanningModel
        {
            Id = areaRulePlannings.First().Id,
            AssignedSites = new List<AreaRuleAssignedSitesModel>
            {
                new()
                {
                    Checked = true,
                    SiteId = sites[2].Id
                },
                new()
                {
                    Checked = true,
                    SiteId = sites[3].Id
                }
            },
            RuleId = areaRules[0].Id,
            ComplianceEnabled = true,
            PropertyId = properties[0].Id,
            Status = true,
            SendNotifications = true,
            StartDate = DateTime.UtcNow,
            TypeSpecificFields = new AreaRuleTypePlanningModel
            {
                Alarm = AreaRuleT2AlarmsEnum.No,
                DayOfMonth = 1,
                DayOfWeek = 0,
                HoursAndEnergyEnabled = false,
                RepeatEvery = 0,
                RepeatType = 1
            }

        };

        // Act
        await BackendConfigurationAreaRulePlanningsServiceHelper.UpdatePlanning(areaRulePlanningModel2,
            core, 1, BackendConfigurationPnDbContext, ItemsPlanningPnDbContext);

        // Assert
        var areaRuleTranslations = await BackendConfigurationPnDbContext!.AreaRuleTranslations
            .Join(BackendConfigurationPnDbContext.AreaRules, atr => atr.AreaRuleId, ar => ar.Id,
                (atr, ar) => new { atr, ar }).Where(x => x.ar.PropertyId == properties[0].Id).ToListAsync();
        var areaProperties = await BackendConfigurationPnDbContext!.AreaProperties.Where(x => x.PropertyId == properties[0].Id).ToListAsync();
        var folderTranslations = await MicrotingDbContext!.FolderTranslations.ToListAsync();
        areaRulePlannings = await BackendConfigurationPnDbContext!.AreaRulePlannings.ToListAsync();
        var planningSites = await BackendConfigurationPnDbContext.PlanningSites.ToListAsync();
        var plannings = await ItemsPlanningPnDbContext!.Plannings.ToListAsync();
        var planningNameTranslations = await ItemsPlanningPnDbContext.PlanningNameTranslation.ToListAsync();
        var itemPlanningSites = await ItemsPlanningPnDbContext!.PlanningSites.ToListAsync();
        var itemPlanningCases = await ItemsPlanningPnDbContext!.PlanningCases.AsNoTracking().ToListAsync();
        var itemPlanningCaseSites = await ItemsPlanningPnDbContext!.PlanningCaseSites.ToListAsync();
        var compliances = await BackendConfigurationPnDbContext!.Compliances.ToListAsync();
        var checkListSites = await MicrotingDbContext!.CheckListSites.ToListAsync();
        var cases = await MicrotingDbContext!.Cases.ToListAsync();

        // Assert result
        Assert.NotNull(result);
        Assert.That(result.Success, Is.EqualTo(true));

        // Assert areaRules
        Assert.NotNull(areaRules);
        Assert.That(areaRules.Count, Is.EqualTo(1));
        Assert.That(areaRules[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[0].EformName, Is.EqualTo("01. Gyllekøling"));

        // Assert areaRuleTranslations
        Assert.NotNull(areaRuleTranslations);
        Assert.That(areaRuleTranslations.Count, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[0].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[0].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[0].atr.Name, Is.EqualTo(danishName));
        Assert.That(areaRuleTranslations[1].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[1].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[1].atr.Name, Is.EqualTo(englishName));
        Assert.That(areaRuleTranslations[2].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[2].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[2].atr.Name, Is.EqualTo(germanName));

        // Assert areaProperties
        Assert.NotNull(areaProperties);
        Assert.That(areaProperties.Count, Is.EqualTo(1));
        Assert.That(areaProperties[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaProperties[0].AreaId, Is.EqualTo(areaTranslation.AreaId));

        // Assert folder translations
        Assert.That(folderTranslations, Is.Not.Null);
        Assert.That(folderTranslations.Count, Is.EqualTo(31));
        Assert.That(folderTranslations[4].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[4].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[5].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[5].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[6].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[6].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[7].Name, Is.EqualTo("00.00 Opret ny opgave"));
        Assert.That(folderTranslations[7].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[8].Name, Is.EqualTo("00.00 Create a new task"));
        Assert.That(folderTranslations[8].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[9].Name, Is.EqualTo("00.00 Erstellen Sie eine neue Aufgabe"));
        Assert.That(folderTranslations[9].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[10].Name, Is.EqualTo("00.02 Mine øvrige opgaver"));
        Assert.That(folderTranslations[10].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[11].Name, Is.EqualTo("00.02 My other tasks"));
        Assert.That(folderTranslations[11].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[12].Name, Is.EqualTo("00.02 Meine anderen Aufgaben"));
        Assert.That(folderTranslations[12].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[13].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[13].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[14].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[14].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[15].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[15].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[16].Name, Is.EqualTo("00.03 Andres opgaver"));
        Assert.That(folderTranslations[16].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[17].Name, Is.EqualTo("00.03 Others' tasks"));
        Assert.That(folderTranslations[17].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[18].Name, Is.EqualTo("00.03 Aufgaben anderer"));
        Assert.That(folderTranslations[18].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[19].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[19].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[20].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[20].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[21].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[21].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[22].Name, Is.EqualTo("00.01 Mine hasteopgaver"));
        Assert.That(folderTranslations[22].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[23].Name, Is.EqualTo("00.01 My urgent tasks"));
        Assert.That(folderTranslations[23].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[24].Name, Is.EqualTo("00.01 Meine dringenden Aufgaben"));
        Assert.That(folderTranslations[24].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[25].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[25].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[26].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[26].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[27].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[27].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[28].Name, Is.EqualTo("00. Logbøger"));
        Assert.That(folderTranslations[28].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[29].Name, Is.EqualTo("01. Log books")); // TODO: This should be "00. Log books"
        Assert.That(folderTranslations[29].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[30].Name, Is.EqualTo("01. Logbücher")); // TODO: This should be "00. Logbücher"
        Assert.That(folderTranslations[30].LanguageId, Is.EqualTo(3));

        // Assert AreaRulePlannings
        Assert.That(areaRulePlannings, Is.Not.Null);
        Assert.That(areaRulePlannings.Count, Is.EqualTo(1));
        Assert.That(areaRulePlannings[0].AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRulePlannings[0].ItemPlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(areaRulePlannings[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRulePlannings[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRulePlannings[0].ComplianceEnabled, Is.EqualTo(false));
        Assert.That(areaRulePlannings[0].SendNotifications, Is.EqualTo(false));

        // Assert plannings
        Assert.That(plannings, Is.Not.Null);
        Assert.That(plannings.Count, Is.EqualTo(1));
        Assert.That(plannings[0].RelatedEFormId, Is.EqualTo(areaRules[0].EformId));
        Assert.That(plannings[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
        Assert.That(plannings[0].SdkFolderId, Is.EqualTo(areaRules[0].FolderId));
        Assert.That(plannings[0].LastExecutedTime, Is.Not.Null);
        Assert.That(plannings[0].LastExecutedTime, Is.EqualTo(new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 0,0,0)));

        Assert.That(plannings[0].NextExecutionTime, Is.Null);
        Assert.That(plannings[0].RepeatEvery, Is.EqualTo(0));
        Assert.That(plannings[0].RepeatType, Is.EqualTo(RepeatType.Day));
        Assert.That(plannings[0].DayOfWeek, Is.EqualTo(DayOfWeek.Friday));

        // Assert planningNameTranslations
        Assert.That(planningNameTranslations, Is.Not.Null);
        Assert.That(planningNameTranslations.Count, Is.EqualTo(3));
        Assert.That(planningNameTranslations[0].Name, Is.EqualTo(danishName));
        Assert.That(planningNameTranslations[0].LanguageId, Is.EqualTo(danishLanguage.Id));
        Assert.That(planningNameTranslations[0].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(planningNameTranslations[1].Name, Is.EqualTo(englishName));
        Assert.That(planningNameTranslations[1].LanguageId, Is.EqualTo(englishLanguage.Id));
        Assert.That(planningNameTranslations[1].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(planningNameTranslations[2].Name, Is.EqualTo(germanName));
        Assert.That(planningNameTranslations[2].LanguageId, Is.EqualTo(germanLanguage.Id));
        Assert.That(planningNameTranslations[2].PlanningId, Is.EqualTo(plannings[0].Id));

        // Assert planningSites
        Assert.That(planningSites, Is.Not.Null);
        Assert.That(planningSites.Count, Is.EqualTo(2));
        Assert.That(planningSites[0].AreaRulePlanningsId, Is.EqualTo(areaRulePlannings[0].Id));
        Assert.That(planningSites[0].AreaId, Is.EqualTo(areaId));
        Assert.That(planningSites[0].SiteId, Is.EqualTo(sites[2].Id));
        Assert.That(planningSites[0].Status, Is.EqualTo(33));
        Assert.That(planningSites[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
        Assert.That(planningSites[1].AreaRulePlanningsId, Is.EqualTo(areaRulePlannings[0].Id));
        Assert.That(planningSites[1].AreaId, Is.EqualTo(areaId));
        Assert.That(planningSites[1].SiteId, Is.EqualTo(sites[3].Id));
        Assert.That(planningSites[1].Status, Is.EqualTo(33));
        Assert.That(planningSites[1].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));

        // Assert itemPlanningSites
        Assert.That(itemPlanningSites, Is.Not.Null);
        Assert.That(itemPlanningSites.Count, Is.EqualTo(2));
        Assert.That(itemPlanningSites[0].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(itemPlanningSites[0].SiteId, Is.EqualTo(sites[2].Id));
        Assert.That(itemPlanningSites[0].LastExecutedTime, Is.Null);
        Assert.That(itemPlanningSites[1].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(itemPlanningSites[1].SiteId, Is.EqualTo(sites[3].Id));
        Assert.That(itemPlanningSites[1].LastExecutedTime, Is.Null);

        // Assert itemPlanningCases
        Assert.That(itemPlanningCases, Is.Not.Null);
        Assert.That(itemPlanningCases.Count, Is.EqualTo(1));
        Assert.That(itemPlanningCases[0].PlanningId, Is.EqualTo(plannings[0].Id));

        // Assert itemPlanningCaseSites
        Assert.That(itemPlanningCaseSites, Is.Not.Null);
        Assert.That(itemPlanningCaseSites.Count, Is.EqualTo(2));
        Assert.That(itemPlanningCaseSites[0].PlanningCaseId, Is.EqualTo(itemPlanningCases[0].Id));
        Assert.That(itemPlanningCaseSites[0].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(itemPlanningCaseSites[0].MicrotingSdkSiteId, Is.EqualTo(sites[2].Id));
        Assert.That(itemPlanningCaseSites[0].MicrotingSdkCaseId, Is.EqualTo(0));
        Assert.That(itemPlanningCaseSites[0].Status, Is.EqualTo(66));
        Assert.That(itemPlanningCaseSites[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
        Assert.That(itemPlanningCaseSites[0].MicrotingCheckListSitId, Is.EqualTo(checkListSites[0].Id));
        Assert.That(itemPlanningCaseSites[1].PlanningCaseId, Is.EqualTo(itemPlanningCases[0].Id));
        Assert.That(itemPlanningCaseSites[1].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(itemPlanningCaseSites[1].MicrotingSdkSiteId, Is.EqualTo(sites[3].Id));
        Assert.That(itemPlanningCaseSites[1].MicrotingSdkCaseId, Is.EqualTo(0));
        Assert.That(itemPlanningCaseSites[1].Status, Is.EqualTo(66));
        Assert.That(itemPlanningCaseSites[1].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
        Assert.That(itemPlanningCaseSites[1].MicrotingCheckListSitId, Is.EqualTo(checkListSites[1].Id));

        // Assert compliances
        Assert.That(compliances, Is.Not.Null);
        Assert.That(compliances.Count, Is.EqualTo(0));

        // Assert checkListSites
        Assert.That(checkListSites, Is.Not.Null);
        Assert.That(checkListSites.Count, Is.EqualTo(2));
        Assert.That(checkListSites[0].SiteId, Is.EqualTo(sites[2].Id));
        Assert.That(checkListSites[0].CheckListId, Is.EqualTo(areaRules[0].EformId));
        Assert.That(checkListSites[0].FolderId, Is.Null);
        Assert.That(checkListSites[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
        Assert.That(checkListSites[1].SiteId, Is.EqualTo(sites[3].Id));
        Assert.That(checkListSites[1].CheckListId, Is.EqualTo(areaRules[0].EformId));
        Assert.That(checkListSites[1].FolderId, Is.Null);
        Assert.That(checkListSites[1].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));

        // Assert cases
        Assert.That(cases, Is.Not.Null);
        Assert.That(cases.Count, Is.EqualTo(0));
    }

    // Should test the UpdatePlanning method for area rule "00. Logbøger" with repeat type "Days" and repeat every 4, disable and reenable compliance and notifications
    [Test]
    public async Task UpdatePlanning_AreaRuleDays4DisableReenable_ReturnsSuccess()
    {
        // Arrange
        var core = await GetCore();
        var englishLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "en-US");
        var germanLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "de-DE");
        var danishLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "da");
        var danishName = new Guid().ToString();
        var englishName = new Guid().ToString();
        var germanName = new Guid().ToString();
        var propertyCreateModel = new PropertyCreateModel
        {
            Address = Guid.NewGuid().ToString(),
            Chr = Guid.NewGuid().ToString(),
            IndustryCode = Guid.NewGuid().ToString(),
            Cvr = Guid.NewGuid().ToString(),
            IsFarm = false,
            LanguagesIds = new List<int>
            {
                1
            },
            MainMailAddress = Guid.NewGuid().ToString(),
            Name = Guid.NewGuid().ToString(),
            WorkorderEnable = false
        };

        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1, BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, 1, 1);

        var deviceUserModel = new DeviceUserModel
        {
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = false,
            UserFirstName = Guid.NewGuid().ToString(),
            UserLastName = Guid.NewGuid().ToString()
        };

        await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            TimePlanningPnDbContext);

        var properties = await BackendConfigurationPnDbContext!.Properties.ToListAsync();
        var sites = await MicrotingDbContext!.Sites.AsNoTracking().ToListAsync();

        var propertyAssignWorkersModel = new PropertyAssignWorkersModel
        {
            Assignments = new List<PropertyAssignmentWorkerModel>
            {
                new()
                {
                    PropertyId = properties[0].Id,
                    IsChecked = true
                }
            },
            SiteId = sites[2].Id
        };

        await BackendConfigurationAssignmentWorkerServiceHelper.Create(propertyAssignWorkersModel, core, 1,
            BackendConfigurationPnDbContext, CaseTemplatePnDbContext, "location", Bus);

        var areaTranslation = await BackendConfigurationPnDbContext!.AreaTranslations.FirstAsync(x => x.Name == "00. Logbøger");
        var areaId = areaTranslation.AreaId;
        var currentSite = await MicrotingDbContext!.Sites.OrderByDescending(x => x.Id).FirstAsync();

        var propertyAreasUpdateModel = new PropertyAreasUpdateModel
        {
            Areas = new List<PropertyAreaModel>
            {
                new()
                {
                    AreaId = areaTranslation.AreaId,
                    Activated = true
                }
            },
            PropertyId = properties[0].Id
        };

        var result = await BackendConfigurationPropertyAreasServiceHelper.Update(propertyAreasUpdateModel, core, BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, 1);

        var checkListTranslation = await MicrotingDbContext.CheckListTranslations.FirstAsync(x => x.Text == "01. Gyllekøling");

        AreaRulesCreateModel areaRulesCreateModel = new AreaRulesCreateModel
        {
            AreaRules = new List<AreaRuleCreateModel>
            {
                new()
                {
                    TranslatedNames = new List<CommonDictionaryModel>
                    {
                        new()
                        {
                            Name = danishName, Description = "00. Logbøger",
                            Id = danishLanguage.Id
                        },
                        new()
                        {
                            Name = englishName, Description = "00. Logbooks",
                            Id = englishLanguage.Id
                        },
                        new()
                        {
                            Name = germanName, Description = "00. Logbücher",
                            Id = germanLanguage.Id
                        }
                    },
                    TypeSpecificFields = new TypeSpecificFields
                    {
                        Alarm = AreaRuleT2AlarmsEnum.Yes,
                        DayOfWeek = 1,
                        EformId = checkListTranslation.CheckListId,
                        Type = AreaRuleT2TypesEnum.Open
                    }
                }
            },
            PropertyAreaId = properties[0].Id
        };

        await BackendConfigurationAreaRulesServiceHelper.Create(areaRulesCreateModel, core, 1, BackendConfigurationPnDbContext, danishLanguage);

        var areaRules = await BackendConfigurationPnDbContext!.AreaRules.Where(x => x.PropertyId == properties[0].Id).ToListAsync();

        // should create AreaRulePlanningModel for areaId
        var areaRulePlanningModel = new AreaRulePlanningModel
        {
            AssignedSites = new List<AreaRuleAssignedSitesModel>
            {
                new()
                {
                    Checked = true,
                    SiteId = currentSite.Id
                }
            },
            RuleId = areaRules[0].Id,
            ComplianceEnabled = true,
            PropertyId = properties[0].Id,
            Status = true,
            SendNotifications = true,
            StartDate = DateTime.UtcNow,
            TypeSpecificFields = new AreaRuleTypePlanningModel
            {
                Alarm = AreaRuleT2AlarmsEnum.No,
                DayOfMonth = 0,
                DayOfWeek = 0,
                HoursAndEnergyEnabled = false,
                RepeatEvery = 4,
                RepeatType = 1
            }

        };

        await BackendConfigurationAreaRulePlanningsServiceHelper.UpdatePlanning(areaRulePlanningModel,
            core, 1, BackendConfigurationPnDbContext, ItemsPlanningPnDbContext);

        var areaRulePlannings = await BackendConfigurationPnDbContext!.AreaRulePlannings.ToListAsync();

        var areaRulePlanningModel2 = new AreaRulePlanningModel
        {
            Id = areaRulePlannings.First().Id,
            AssignedSites = new List<AreaRuleAssignedSitesModel>
            {
                new()
                {
                    Checked = true,
                    SiteId = currentSite.Id
                }
            },
            RuleId = areaRules[0].Id,
            ComplianceEnabled = true,
            PropertyId = properties[0].Id,
            Status = false,
            SendNotifications = true,
            StartDate = DateTime.UtcNow,
            TypeSpecificFields = new AreaRuleTypePlanningModel
            {
                Alarm = AreaRuleT2AlarmsEnum.No,
                DayOfMonth = 0,
                DayOfWeek = 0,
                HoursAndEnergyEnabled = false,
                RepeatEvery = 4,
                RepeatType = 1
            }

        };

        await BackendConfigurationAreaRulePlanningsServiceHelper.UpdatePlanning(areaRulePlanningModel2,
            core, 1, BackendConfigurationPnDbContext, ItemsPlanningPnDbContext);

        var areaRulePlanningModel3 = new AreaRulePlanningModel
        {
            Id = areaRulePlannings.First().Id,
            AssignedSites = new List<AreaRuleAssignedSitesModel>
            {
                new()
                {
                    Checked = true,
                    SiteId = currentSite.Id
                }
            },
            RuleId = areaRules[0].Id,
            ComplianceEnabled = true,
            PropertyId = properties[0].Id,
            Status = true,
            SendNotifications = true,
            StartDate = DateTime.UtcNow,
            TypeSpecificFields = new AreaRuleTypePlanningModel
            {
                Alarm = AreaRuleT2AlarmsEnum.No,
                DayOfMonth = 0,
                DayOfWeek = 0,
                HoursAndEnergyEnabled = false,
                RepeatEvery = 4,
                RepeatType = 1
            }

        };

        await BackendConfigurationAreaRulePlanningsServiceHelper.UpdatePlanning(areaRulePlanningModel3,
            core, 1, BackendConfigurationPnDbContext, ItemsPlanningPnDbContext);

        // Assert
        var areaRuleTranslations = await BackendConfigurationPnDbContext!.AreaRuleTranslations
            .Join(BackendConfigurationPnDbContext.AreaRules, atr => atr.AreaRuleId, ar => ar.Id,
                (atr, ar) => new { atr, ar }).Where(x => x.ar.PropertyId == properties[0].Id).ToListAsync();
        var areaProperties = await BackendConfigurationPnDbContext!.AreaProperties.Where(x => x.PropertyId == properties[0].Id).ToListAsync();
        var folderTranslations = await MicrotingDbContext!.FolderTranslations.ToListAsync();
        areaRulePlannings = await BackendConfigurationPnDbContext!.AreaRulePlannings.ToListAsync();
        var planningSites = await BackendConfigurationPnDbContext.PlanningSites.ToListAsync();
        var plannings = await ItemsPlanningPnDbContext!.Plannings.ToListAsync();
        var planningNameTranslations = await ItemsPlanningPnDbContext.PlanningNameTranslation.ToListAsync();
        var itemPlanningSites = await ItemsPlanningPnDbContext!.PlanningSites.ToListAsync();
        var itemPlanningCases = await ItemsPlanningPnDbContext!.PlanningCases.ToListAsync();
        var itemPlanningCaseSites = await ItemsPlanningPnDbContext!.PlanningCaseSites.ToListAsync();
        var compliances = await BackendConfigurationPnDbContext!.Compliances.ToListAsync();
        var checkListSites = await MicrotingDbContext!.CheckListSites.ToListAsync();
        var cases = await MicrotingDbContext!.Cases.ToListAsync();

        // Assert result
        Assert.NotNull(result);
        Assert.That(result.Success, Is.EqualTo(true));

        // Assert areaRules
        Assert.NotNull(areaRules);
        Assert.That(areaRules.Count, Is.EqualTo(1));
        Assert.That(areaRules[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[0].EformName, Is.EqualTo("01. Gyllekøling"));

        // Assert areaRuleTranslations
        Assert.NotNull(areaRuleTranslations);
        Assert.That(areaRuleTranslations.Count, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[0].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[0].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[0].atr.Name, Is.EqualTo(danishName));
        Assert.That(areaRuleTranslations[1].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[1].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[1].atr.Name, Is.EqualTo(englishName));
        Assert.That(areaRuleTranslations[2].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[2].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[2].atr.Name, Is.EqualTo(germanName));

        // Assert areaProperties
        Assert.NotNull(areaProperties);
        Assert.That(areaProperties.Count, Is.EqualTo(1));
        Assert.That(areaProperties[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaProperties[0].AreaId, Is.EqualTo(areaTranslation.AreaId));

        // Assert folder translations
        Assert.That(folderTranslations, Is.Not.Null);
        Assert.That(folderTranslations.Count, Is.EqualTo(31));
        Assert.That(folderTranslations[4].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[4].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[5].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[5].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[6].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[6].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[7].Name, Is.EqualTo("00.00 Opret ny opgave"));
        Assert.That(folderTranslations[7].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[8].Name, Is.EqualTo("00.00 Create a new task"));
        Assert.That(folderTranslations[8].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[9].Name, Is.EqualTo("00.00 Erstellen Sie eine neue Aufgabe"));
        Assert.That(folderTranslations[9].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[10].Name, Is.EqualTo("00.02 Mine øvrige opgaver"));
        Assert.That(folderTranslations[10].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[11].Name, Is.EqualTo("00.02 My other tasks"));
        Assert.That(folderTranslations[11].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[12].Name, Is.EqualTo("00.02 Meine anderen Aufgaben"));
        Assert.That(folderTranslations[12].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[13].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[13].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[14].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[14].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[15].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[15].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[16].Name, Is.EqualTo("00.03 Andres opgaver"));
        Assert.That(folderTranslations[16].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[17].Name, Is.EqualTo("00.03 Others' tasks"));
        Assert.That(folderTranslations[17].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[18].Name, Is.EqualTo("00.03 Aufgaben anderer"));
        Assert.That(folderTranslations[18].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[19].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[19].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[20].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[20].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[21].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[21].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[22].Name, Is.EqualTo("00.01 Mine hasteopgaver"));
        Assert.That(folderTranslations[22].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[23].Name, Is.EqualTo("00.01 My urgent tasks"));
        Assert.That(folderTranslations[23].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[24].Name, Is.EqualTo("00.01 Meine dringenden Aufgaben"));
        Assert.That(folderTranslations[24].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[25].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[25].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[26].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[26].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[27].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(folderTranslations[27].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[28].Name, Is.EqualTo("00. Logbøger"));
        Assert.That(folderTranslations[28].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[29].Name, Is.EqualTo("01. Log books")); // TODO: This should be "00. Log books"
        Assert.That(folderTranslations[29].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[30].Name, Is.EqualTo("01. Logbücher")); // TODO: This should be "00. Logbücher"
        Assert.That(folderTranslations[30].LanguageId, Is.EqualTo(3));

        // Assert AreaRulePlannings
        Assert.That(areaRulePlannings, Is.Not.Null);
        Assert.That(areaRulePlannings.Count, Is.EqualTo(1));
        Assert.That(areaRulePlannings[0].AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRulePlannings[0].ItemPlanningId, Is.EqualTo(plannings[1].Id));
        Assert.That(areaRulePlannings[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRulePlannings[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRulePlannings[0].ComplianceEnabled, Is.EqualTo(true));
        Assert.That(areaRulePlannings[0].SendNotifications, Is.EqualTo(true));
        Assert.That(areaRulePlannings[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));

        // Assert plannings
        Assert.That(plannings, Is.Not.Null);
        Assert.That(plannings.Count, Is.EqualTo(2));
        Assert.That(plannings[0].RelatedEFormId, Is.EqualTo(areaRules[0].EformId));
        Assert.That(plannings[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
        Assert.That(plannings[0].SdkFolderId, Is.EqualTo(areaRules[0].FolderId));
        Assert.That(plannings[0].LastExecutedTime, Is.Not.Null);
        Assert.That(plannings[0].LastExecutedTime, Is.EqualTo(new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 0,0,0)));
        var now = DateTime.UtcNow;
        var diff = (now - new DateTime(now.Year, 1, 1)).TotalDays;
        var multiplier = (int) (diff / 4);
        var nextExecutionTime =
            new DateTime(now.Year, 1, 1).AddDays(multiplier * 4);
        if (nextExecutionTime < now)
        {
            nextExecutionTime = nextExecutionTime.AddDays(4);
        }

        Assert.That(plannings[0].NextExecutionTime, Is.EqualTo(nextExecutionTime));
        Assert.That(plannings[0].RepeatEvery, Is.EqualTo(4));
        Assert.That(plannings[0].RepeatType, Is.EqualTo(RepeatType.Day));

        Assert.That(plannings[1].RelatedEFormId, Is.EqualTo(areaRules[0].EformId));
        Assert.That(plannings[1].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
        Assert.That(plannings[1].SdkFolderId, Is.EqualTo(areaRules[0].FolderId));
        Assert.That(plannings[1].LastExecutedTime, Is.Not.Null);
        Assert.That(plannings[1].LastExecutedTime, Is.EqualTo(new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 0,0,0)));

        Assert.That(plannings[1].NextExecutionTime, Is.EqualTo(nextExecutionTime));
        Assert.That(plannings[1].RepeatEvery, Is.EqualTo(4));
        Assert.That(plannings[1].RepeatType, Is.EqualTo(RepeatType.Day));

        // Assert planningNameTranslations
        Assert.That(planningNameTranslations, Is.Not.Null);
        Assert.That(planningNameTranslations.Count, Is.EqualTo(6));
        Assert.That(planningNameTranslations[0].Name, Is.EqualTo(danishName));
        Assert.That(planningNameTranslations[0].LanguageId, Is.EqualTo(danishLanguage.Id));
        Assert.That(planningNameTranslations[0].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(planningNameTranslations[1].Name, Is.EqualTo(englishName));
        Assert.That(planningNameTranslations[1].LanguageId, Is.EqualTo(englishLanguage.Id));
        Assert.That(planningNameTranslations[1].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(planningNameTranslations[2].Name, Is.EqualTo(germanName));
        Assert.That(planningNameTranslations[2].LanguageId, Is.EqualTo(germanLanguage.Id));
        Assert.That(planningNameTranslations[2].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(planningNameTranslations[3].Name, Is.EqualTo(danishName));
        Assert.That(planningNameTranslations[3].LanguageId, Is.EqualTo(danishLanguage.Id));
        Assert.That(planningNameTranslations[3].PlanningId, Is.EqualTo(plannings[1].Id));
        Assert.That(planningNameTranslations[4].Name, Is.EqualTo(englishName));
        Assert.That(planningNameTranslations[4].LanguageId, Is.EqualTo(englishLanguage.Id));
        Assert.That(planningNameTranslations[4].PlanningId, Is.EqualTo(plannings[1].Id));
        Assert.That(planningNameTranslations[5].Name, Is.EqualTo(germanName));
        Assert.That(planningNameTranslations[5].LanguageId, Is.EqualTo(germanLanguage.Id));
        Assert.That(planningNameTranslations[5].PlanningId, Is.EqualTo(plannings[1].Id));

        // Assert planningSites
        Assert.That(planningSites, Is.Not.Null);
        Assert.That(planningSites.Count, Is.EqualTo(1));
        Assert.That(planningSites[0].AreaRulePlanningsId, Is.EqualTo(areaRulePlannings[0].Id));
        Assert.That(planningSites[0].AreaId, Is.EqualTo(areaId));
        Assert.That(planningSites[0].SiteId, Is.EqualTo(sites[2].Id));
        Assert.That(planningSites[0].Status, Is.EqualTo(33));
        Assert.That(planningSites[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));

        // Assert itemPlanningSites
        Assert.That(itemPlanningSites, Is.Not.Null);
        Assert.That(itemPlanningSites.Count, Is.EqualTo(2));
        Assert.That(itemPlanningSites[0].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(itemPlanningSites[0].SiteId, Is.EqualTo(sites[2].Id));
        Assert.That(itemPlanningSites[0].LastExecutedTime, Is.Null);
        Assert.That(itemPlanningSites[1].PlanningId, Is.EqualTo(plannings[1].Id));
        Assert.That(itemPlanningSites[1].SiteId, Is.EqualTo(sites[2].Id));
        Assert.That(itemPlanningSites[1].LastExecutedTime, Is.Null);

        // Assert itemPlanningCases
        Assert.That(itemPlanningCases, Is.Not.Null);
        Assert.That(itemPlanningCases.Count, Is.EqualTo(2));
        Assert.That(itemPlanningCases[0].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(itemPlanningCases[1].PlanningId, Is.EqualTo(plannings[1].Id));

        // Assert itemPlanningCaseSites
        Assert.That(itemPlanningCaseSites, Is.Not.Null);
        Assert.That(itemPlanningCaseSites.Count, Is.EqualTo(2));
        Assert.That(itemPlanningCaseSites[0].PlanningCaseId, Is.EqualTo(itemPlanningCases[0].Id));
        Assert.That(itemPlanningCaseSites[0].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(itemPlanningCaseSites[0].MicrotingSdkSiteId, Is.EqualTo(sites[2].Id));
        Assert.That(itemPlanningCaseSites[0].MicrotingSdkCaseId, Is.EqualTo(cases[0].Id));
        Assert.That(itemPlanningCaseSites[0].Status, Is.EqualTo(66));
        Assert.That(itemPlanningCaseSites[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
        Assert.That(itemPlanningCaseSites[0].MicrotingCheckListSitId, Is.EqualTo(0));
        Assert.That(itemPlanningCaseSites[1].PlanningCaseId, Is.EqualTo(itemPlanningCases[1].Id));
        Assert.That(itemPlanningCaseSites[1].PlanningId, Is.EqualTo(plannings[1].Id));
        Assert.That(itemPlanningCaseSites[1].MicrotingSdkSiteId, Is.EqualTo(sites[2].Id));
        Assert.That(itemPlanningCaseSites[1].MicrotingSdkCaseId, Is.EqualTo(cases[1].Id));
        Assert.That(itemPlanningCaseSites[1].Status, Is.EqualTo(66));
        Assert.That(itemPlanningCaseSites[1].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
        Assert.That(itemPlanningCaseSites[1].MicrotingCheckListSitId, Is.EqualTo(0));

        // Assert compliances
        Assert.That(compliances, Is.Not.Null);
        Assert.That(compliances.Count, Is.EqualTo(0));

        // Assert checkListSites
        Assert.That(checkListSites, Is.Not.Null);
        Assert.That(checkListSites.Count, Is.EqualTo(0));

        // Assert cases
        Assert.That(cases, Is.Not.Null);
        Assert.That(cases.Count, Is.EqualTo(2));
        Assert.That(cases[0].SiteId, Is.EqualTo(sites[2].Id));
        Assert.That(cases[0].CheckListId, Is.EqualTo(areaRules[0].EformId));
        Assert.That(cases[0].FolderId, Is.Null);
        Assert.That(cases[0].Status, Is.EqualTo(66));
        Assert.That(cases[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
        Assert.That(cases[1].SiteId, Is.EqualTo(sites[2].Id));
        Assert.That(cases[1].CheckListId, Is.EqualTo(areaRules[0].EformId));
        Assert.That(cases[1].FolderId, Is.Null);
        Assert.That(cases[1].Status, Is.EqualTo(66));
        Assert.That(cases[1].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
    }
}

public static class DateTimeExtensions
{
    public static DateTime StartOfWeek(this DateTime dt, DayOfWeek startOfWeek)
    {
        int diff = (7 + (dt.DayOfWeek - startOfWeek)) % 7;
        return dt.AddDays(-1 * diff).Date;
    }
}