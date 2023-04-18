using BackendConfiguration.Pn.Infrastructure.Helpers;
using BackendConfiguration.Pn.Infrastructure.Models;
using BackendConfiguration.Pn.Infrastructure.Models.AreaRules;
using BackendConfiguration.Pn.Infrastructure.Models.AssignmentWorker;
using BackendConfiguration.Pn.Infrastructure.Models.Properties;
using BackendConfiguration.Pn.Infrastructure.Models.PropertyAreas;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.ItemsPlanningBase.Infrastructure.Enums;

namespace BackendConfiguration.Pn.Integration.Test;

[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class BackendConfigurationAreaRulePlanningsServiceHelperTestLogBooksMonthsCustomDate : TestBaseSetup
{
    // Should test the UpdatePlanning method for area rule "00. Logbøger" with repeat type "months" and repeat every "1"
    [Test]
    [TestCase(0, "01. Gyllekøling", "01. Slurry cooling", "01. Schlammkühlung")]
    [TestCase(1, "02. Forsuring", "02. Acidification", "02. Ansäuerung")]
    [TestCase(2, "03. Luftrensning", "03. Air purification", "03. Luftreinigung")]
    [TestCase(3, "04. Beholderkontrol gennemført", "04. Container control completed", "04. Behälterkontrolle abgeschlossen")]
    [TestCase(4, "05. Gyllebeholdere", "05. Slurry containers", "05. Güllebehälter")]
    [TestCase(5, "06. Gyllepumper, - miksere, - seperatorer og spredere", "06. Slurry pumps, - mixers, - separators and spreaders", "06. Schlammpumpen, - Mischer, - Separatoren und Verteiler")]
    [TestCase(6, "07. Forsyningssystemer til vand og foder", "07. Supply systems for water and feed", "07. Versorgungssysteme für Wasser und Futter")]
    [TestCase(7, "08. Varme-, køle- og ventilationssystemer samt temperaturfølere", "08. Heating, cooling and ventilation systems and temperature sensors", "08. Heizungs-, Kühl- und Lüftungssysteme und Temperatursensoren")]
    [TestCase(8, "09. Siloer og transportudstyr", "09. Silos and transport equipment", "09. Silos und Transportgeräte")]
    [TestCase(9, "10. Luftrensningssystemer", "10. Air purification systems", "10. Luftreinigungssysteme")]
    [TestCase(10, "11. Udstyr til drikkevand", "11. Equipment for drinking water", "11. Ausrüstung für Trinkwasser")]
    [TestCase(11, "12. Maskiner til udbringning af husdyrgødning samt doseringsmekanisme- eller dyse", "12. Machines for spreading livestock manure and dosing mechanisms or nozzles", "12. Maschinen zum Ausbringen von Viehmist und Dosiervorrichtungen oder Düsen")]
    [TestCase(12, "13. Miljøledelse gennemgået og revideret", "13. Environmental management reviewed and revised", "13. Umweltmanagement überprüft und überarbeitet")]
    [TestCase(13, "14. Beredskabsplan gennemgået og revideret", "14. Contingency plan reviewed and revised", "14. Notfallplan überprüft und überarbeitet")]
    public async Task UpdatePlanning_AreaRuleMonths1_ReturnsSuccess(int areaRuleNo, string danishTranslation, string englishTranslation, string germanTranslation)
    {
        // Arrange
        var core = await GetCore();
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
            RuleId = areaRules[areaRuleNo].Id,
            ComplianceEnabled = true,
            PropertyId = properties[0].Id,
            Status = true,
            SendNotifications = true,
            StartDate = new DateTime(2023, 4, 12, 0, 0,0),
            UseStartDateAsStartOfPeriod = true,
            TypeSpecificFields = new AreaRuleTypePlanningModel
            {
                Alarm = AreaRuleT2AlarmsEnum.No,
                DayOfMonth = 1,
                DayOfWeek = 0,
                HoursAndEnergyEnabled = false,
                RepeatEvery = 1,
                RepeatType = 3
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
        var englishLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "en-US");
        var germanLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "de-DE");
        var danishLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "da");

        // Assert result
        Assert.NotNull(result);
        Assert.That(result.Success, Is.EqualTo(true));

        // Assert areaRules
        Assert.NotNull(areaRules);
        Assert.That(areaRules.Count, Is.EqualTo(14));
        Assert.That(areaRules[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[1].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[2].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[3].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[4].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[5].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[6].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[7].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[8].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[9].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[10].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[11].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[12].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[13].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[1].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[2].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[3].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[4].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[5].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[6].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[7].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[8].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[9].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[10].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[11].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[12].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[0].EformName, Is.EqualTo("01. Gyllekøling"));
        Assert.That(areaRules[1].EformName, Is.EqualTo("02. Forsuring"));
        Assert.That(areaRules[2].EformName, Is.EqualTo("03. Luftrensning"));
        Assert.That(areaRules[3].EformName, Is.EqualTo("04. Beholderkontrol gennemført"));
        Assert.That(areaRules[4].EformName, Is.EqualTo("05. Gyllebeholdere"));
        Assert.That(areaRules[5].EformName, Is.EqualTo("06. Gyllepumper, - miksere, - seperatorer og spredere"));
        Assert.That(areaRules[6].EformName, Is.EqualTo("07. Forsyningssystemer til vand og foder"));
        Assert.That(areaRules[7].EformName, Is.EqualTo("08. Varme-, køle- og ventilationssystemer samt temperaturfølere"));
        Assert.That(areaRules[8].EformName, Is.EqualTo("09. Siloer og transportudstyr"));
        Assert.That(areaRules[9].EformName, Is.EqualTo("10. Luftrensningssystemer"));
        Assert.That(areaRules[10].EformName, Is.EqualTo("11. Udstyr til drikkevand"));
        Assert.That(areaRules[11].EformName, Is.EqualTo("12. Maskiner til udbringning af husdyrgødning samt doseringsmekanisme- eller dyse"));
        Assert.That(areaRules[12].EformName, Is.EqualTo("13. Miljøledelse"));
        Assert.That(areaRules[13].EformName, Is.EqualTo("14. Beredskabsplan"));

        // Assert areaRuleTranslations
        Assert.NotNull(areaRuleTranslations);
        Assert.That(areaRuleTranslations.Count, Is.EqualTo(42));
        Assert.That(areaRuleTranslations[0].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[0].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[0].atr.Name, Is.EqualTo("01. Gyllekøling"));
        Assert.That(areaRuleTranslations[1].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[1].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[1].atr.Name, Is.EqualTo("01. Slurry cooling"));
        Assert.That(areaRuleTranslations[2].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[2].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[2].atr.Name, Is.EqualTo("01. Schlammkühlung"));
        Assert.That(areaRuleTranslations[3].atr.AreaRuleId, Is.EqualTo(areaRules[1].Id));
        Assert.That(areaRuleTranslations[3].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[3].atr.Name, Is.EqualTo("02. Forsuring"));
        Assert.That(areaRuleTranslations[4].atr.AreaRuleId, Is.EqualTo(areaRules[1].Id));
        Assert.That(areaRuleTranslations[4].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[4].atr.Name, Is.EqualTo("02. Acidification"));
        Assert.That(areaRuleTranslations[5].atr.AreaRuleId, Is.EqualTo(areaRules[1].Id));
        Assert.That(areaRuleTranslations[5].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[5].atr.Name, Is.EqualTo("02. Ansäuerung"));
        Assert.That(areaRuleTranslations[6].atr.AreaRuleId, Is.EqualTo(areaRules[2].Id));
        Assert.That(areaRuleTranslations[6].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[6].atr.Name, Is.EqualTo("03. Luftrensning"));
        Assert.That(areaRuleTranslations[7].atr.AreaRuleId, Is.EqualTo(areaRules[2].Id));
        Assert.That(areaRuleTranslations[7].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[7].atr.Name, Is.EqualTo("03. Air purification"));
        Assert.That(areaRuleTranslations[8].atr.AreaRuleId, Is.EqualTo(areaRules[2].Id));
        Assert.That(areaRuleTranslations[8].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[8].atr.Name, Is.EqualTo("03. Luftreinigung"));
        Assert.That(areaRuleTranslations[9].atr.AreaRuleId, Is.EqualTo(areaRules[3].Id));
        Assert.That(areaRuleTranslations[9].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[9].atr.Name, Is.EqualTo("04. Beholderkontrol gennemført"));
        Assert.That(areaRuleTranslations[10].atr.AreaRuleId, Is.EqualTo(areaRules[3].Id));
        Assert.That(areaRuleTranslations[10].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[10].atr.Name, Is.EqualTo("04. Container control completed"));
        Assert.That(areaRuleTranslations[11].atr.AreaRuleId, Is.EqualTo(areaRules[3].Id));
        Assert.That(areaRuleTranslations[11].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[11].atr.Name, Is.EqualTo("04. Behälterkontrolle abgeschlossen"));
        Assert.That(areaRuleTranslations[12].atr.AreaRuleId, Is.EqualTo(areaRules[4].Id));
        Assert.That(areaRuleTranslations[12].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[12].atr.Name, Is.EqualTo("05. Gyllebeholdere"));
        Assert.That(areaRuleTranslations[13].atr.AreaRuleId, Is.EqualTo(areaRules[4].Id));
        Assert.That(areaRuleTranslations[13].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[13].atr.Name, Is.EqualTo("05. Slurry containers"));
        Assert.That(areaRuleTranslations[14].atr.AreaRuleId, Is.EqualTo(areaRules[4].Id));
        Assert.That(areaRuleTranslations[14].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[14].atr.Name, Is.EqualTo("05. Güllebehälter"));
        Assert.That(areaRuleTranslations[15].atr.AreaRuleId, Is.EqualTo(areaRules[5].Id));
        Assert.That(areaRuleTranslations[15].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[15].atr.Name, Is.EqualTo("06. Gyllepumper, - miksere, - seperatorer og spredere"));
        Assert.That(areaRuleTranslations[16].atr.AreaRuleId, Is.EqualTo(areaRules[5].Id));
        Assert.That(areaRuleTranslations[16].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[16].atr.Name, Is.EqualTo("06. Slurry pumps, - mixers, - separators and spreaders"));
        Assert.That(areaRuleTranslations[17].atr.AreaRuleId, Is.EqualTo(areaRules[5].Id));
        Assert.That(areaRuleTranslations[17].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[17].atr.Name, Is.EqualTo("06. Schlammpumpen, - Mischer, - Separatoren und Verteiler"));
        Assert.That(areaRuleTranslations[18].atr.AreaRuleId, Is.EqualTo(areaRules[6].Id));
        Assert.That(areaRuleTranslations[18].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[18].atr.Name, Is.EqualTo("07. Forsyningssystemer til vand og foder"));
        Assert.That(areaRuleTranslations[19].atr.AreaRuleId, Is.EqualTo(areaRules[6].Id));
        Assert.That(areaRuleTranslations[19].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[19].atr.Name, Is.EqualTo("07. Supply systems for water and feed"));
        Assert.That(areaRuleTranslations[20].atr.AreaRuleId, Is.EqualTo(areaRules[6].Id));
        Assert.That(areaRuleTranslations[20].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[20].atr.Name, Is.EqualTo("07. Versorgungssysteme für Wasser und Futter"));
        Assert.That(areaRuleTranslations[21].atr.AreaRuleId, Is.EqualTo(areaRules[7].Id));
        Assert.That(areaRuleTranslations[21].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[21].atr.Name, Is.EqualTo("08. Varme-, køle- og ventilationssystemer samt temperaturfølere"));
        Assert.That(areaRuleTranslations[22].atr.AreaRuleId, Is.EqualTo(areaRules[7].Id));
        Assert.That(areaRuleTranslations[22].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[22].atr.Name, Is.EqualTo("08. Heating, cooling and ventilation systems and temperature sensors"));
        Assert.That(areaRuleTranslations[23].atr.AreaRuleId, Is.EqualTo(areaRules[7].Id));
        Assert.That(areaRuleTranslations[23].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[23].atr.Name, Is.EqualTo("08. Heizungs-, Kühl- und Lüftungssysteme und Temperatursensoren"));
        Assert.That(areaRuleTranslations[24].atr.AreaRuleId, Is.EqualTo(areaRules[8].Id));
        Assert.That(areaRuleTranslations[24].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[24].atr.Name, Is.EqualTo("09. Siloer og transportudstyr"));
        Assert.That(areaRuleTranslations[25].atr.AreaRuleId, Is.EqualTo(areaRules[8].Id));
        Assert.That(areaRuleTranslations[25].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[25].atr.Name, Is.EqualTo("09. Silos and transport equipment"));
        Assert.That(areaRuleTranslations[26].atr.AreaRuleId, Is.EqualTo(areaRules[8].Id));
        Assert.That(areaRuleTranslations[26].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[26].atr.Name, Is.EqualTo("09. Silos und Transportgeräte"));
        Assert.That(areaRuleTranslations[27].atr.AreaRuleId, Is.EqualTo(areaRules[9].Id));
        Assert.That(areaRuleTranslations[27].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[27].atr.Name, Is.EqualTo("10. Luftrensningssystemer"));
        Assert.That(areaRuleTranslations[28].atr.AreaRuleId, Is.EqualTo(areaRules[9].Id));
        Assert.That(areaRuleTranslations[28].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[28].atr.Name, Is.EqualTo("10. Air purification systems"));
        Assert.That(areaRuleTranslations[29].atr.AreaRuleId, Is.EqualTo(areaRules[9].Id));
        Assert.That(areaRuleTranslations[29].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[29].atr.Name, Is.EqualTo("10. Luftreinigungssysteme"));
        Assert.That(areaRuleTranslations[30].atr.AreaRuleId, Is.EqualTo(areaRules[10].Id));
        Assert.That(areaRuleTranslations[30].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[30].atr.Name, Is.EqualTo("11. Udstyr til drikkevand"));
        Assert.That(areaRuleTranslations[31].atr.AreaRuleId, Is.EqualTo(areaRules[10].Id));
        Assert.That(areaRuleTranslations[31].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[31].atr.Name, Is.EqualTo("11. Equipment for drinking water"));
        Assert.That(areaRuleTranslations[32].atr.AreaRuleId, Is.EqualTo(areaRules[10].Id));
        Assert.That(areaRuleTranslations[32].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[32].atr.Name, Is.EqualTo("11. Ausrüstung für Trinkwasser"));
        Assert.That(areaRuleTranslations[33].atr.AreaRuleId, Is.EqualTo(areaRules[11].Id));
        Assert.That(areaRuleTranslations[33].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[33].atr.Name, Is.EqualTo("12. Maskiner til udbringning af husdyrgødning samt doseringsmekanisme- eller dyse"));
        Assert.That(areaRuleTranslations[34].atr.AreaRuleId, Is.EqualTo(areaRules[11].Id));
        Assert.That(areaRuleTranslations[34].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[34].atr.Name, Is.EqualTo("12. Machines for spreading livestock manure and dosing mechanisms or nozzles"));
        Assert.That(areaRuleTranslations[35].atr.AreaRuleId, Is.EqualTo(areaRules[11].Id));
        Assert.That(areaRuleTranslations[35].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[35].atr.Name, Is.EqualTo("12. Maschinen zum Ausbringen von Viehmist und Dosiervorrichtungen oder Düsen"));
        Assert.That(areaRuleTranslations[36].atr.AreaRuleId, Is.EqualTo(areaRules[12].Id));
        Assert.That(areaRuleTranslations[36].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[36].atr.Name, Is.EqualTo("13. Miljøledelse gennemgået og revideret"));
        Assert.That(areaRuleTranslations[37].atr.AreaRuleId, Is.EqualTo(areaRules[12].Id));
        Assert.That(areaRuleTranslations[37].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[37].atr.Name, Is.EqualTo("13. Environmental management reviewed and revised"));
        Assert.That(areaRuleTranslations[38].atr.AreaRuleId, Is.EqualTo(areaRules[12].Id));
        Assert.That(areaRuleTranslations[38].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[38].atr.Name, Is.EqualTo("13. Umweltmanagement überprüft und überarbeitet"));
        Assert.That(areaRuleTranslations[39].atr.AreaRuleId, Is.EqualTo(areaRules[13].Id));
        Assert.That(areaRuleTranslations[39].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[39].atr.Name, Is.EqualTo("14. Beredskabsplan gennemgået og revideret"));
        Assert.That(areaRuleTranslations[40].atr.AreaRuleId, Is.EqualTo(areaRules[13].Id));
        Assert.That(areaRuleTranslations[40].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[40].atr.Name, Is.EqualTo("14. Contingency plan reviewed and revised"));
        Assert.That(areaRuleTranslations[41].atr.AreaRuleId, Is.EqualTo(areaRules[13].Id));
        Assert.That(areaRuleTranslations[41].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[41].atr.Name, Is.EqualTo("14. Notfallplan überprüft und überarbeitet"));

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
        Assert.That(areaRulePlannings[0].AreaRuleId, Is.EqualTo(areaRules[areaRuleNo].Id));
        Assert.That(areaRulePlannings[0].ItemPlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(areaRulePlannings[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRulePlannings[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRulePlannings[0].ComplianceEnabled, Is.EqualTo(true));
        Assert.That(areaRulePlannings[0].SendNotifications, Is.EqualTo(true));

        // Assert plannings
        Assert.That(plannings, Is.Not.Null);
        Assert.That(plannings.Count, Is.EqualTo(1));
        Assert.That(plannings[0].RelatedEFormId, Is.EqualTo(areaRules[areaRuleNo].EformId));
        Assert.That(plannings[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
        Assert.That(plannings[0].SdkFolderId, Is.EqualTo(areaRules[areaRuleNo].FolderId));
        Assert.That(plannings[0].LastExecutedTime, Is.Not.Null);
        Assert.That(plannings[0].LastExecutedTime, Is.EqualTo(new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 0,0,0)));
        var now = DateTime.UtcNow;
        var nextExecutionTime =
            new DateTime(now.Year, now.Month + 1, 12);
        if (nextExecutionTime < now)
        {
            nextExecutionTime = nextExecutionTime.AddMonths(1);
        }

        Assert.That(plannings[0].NextExecutionTime, Is.EqualTo(nextExecutionTime));
        Assert.That(plannings[0].RepeatEvery, Is.EqualTo(1));
        Assert.That(plannings[0].RepeatType, Is.EqualTo(RepeatType.Month));

        // Assert planningNameTranslations
        Assert.That(planningNameTranslations, Is.Not.Null);
        Assert.That(planningNameTranslations.Count, Is.EqualTo(3));
        Assert.That(planningNameTranslations[0].Name, Is.EqualTo(danishTranslation));
        Assert.That(planningNameTranslations[0].LanguageId, Is.EqualTo(danishLanguage.Id));
        Assert.That(planningNameTranslations[0].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(planningNameTranslations[1].Name, Is.EqualTo(englishTranslation));
        Assert.That(planningNameTranslations[1].LanguageId, Is.EqualTo(englishLanguage.Id));
        Assert.That(planningNameTranslations[1].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(planningNameTranslations[2].Name, Is.EqualTo(germanTranslation));
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
        Assert.That(cases[0].CheckListId, Is.EqualTo(areaRules[areaRuleNo].EformId));
        Assert.That(cases[0].FolderId, Is.Null);
        Assert.That(cases[0].Status, Is.EqualTo(66));
        Assert.That(cases[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
    }

    // Should test the UpdatePlanning method for area rule "00. Logbøger" with repeat type "months" and repeat every "2"
    [Test]
    [TestCase(0, "01. Gyllekøling", "01. Slurry cooling", "01. Schlammkühlung")]
    [TestCase(1, "02. Forsuring", "02. Acidification", "02. Ansäuerung")]
    [TestCase(2, "03. Luftrensning", "03. Air purification", "03. Luftreinigung")]
    [TestCase(3, "04. Beholderkontrol gennemført", "04. Container control completed", "04. Behälterkontrolle abgeschlossen")]
    [TestCase(4, "05. Gyllebeholdere", "05. Slurry containers", "05. Güllebehälter")]
    [TestCase(5, "06. Gyllepumper, - miksere, - seperatorer og spredere", "06. Slurry pumps, - mixers, - separators and spreaders", "06. Schlammpumpen, - Mischer, - Separatoren und Verteiler")]
    [TestCase(6, "07. Forsyningssystemer til vand og foder", "07. Supply systems for water and feed", "07. Versorgungssysteme für Wasser und Futter")]
    [TestCase(7, "08. Varme-, køle- og ventilationssystemer samt temperaturfølere", "08. Heating, cooling and ventilation systems and temperature sensors", "08. Heizungs-, Kühl- und Lüftungssysteme und Temperatursensoren")]
    [TestCase(8, "09. Siloer og transportudstyr", "09. Silos and transport equipment", "09. Silos und Transportgeräte")]
    [TestCase(9, "10. Luftrensningssystemer", "10. Air purification systems", "10. Luftreinigungssysteme")]
    [TestCase(10, "11. Udstyr til drikkevand", "11. Equipment for drinking water", "11. Ausrüstung für Trinkwasser")]
    [TestCase(11, "12. Maskiner til udbringning af husdyrgødning samt doseringsmekanisme- eller dyse", "12. Machines for spreading livestock manure and dosing mechanisms or nozzles", "12. Maschinen zum Ausbringen von Viehmist und Dosiervorrichtungen oder Düsen")]
    [TestCase(12, "13. Miljøledelse gennemgået og revideret", "13. Environmental management reviewed and revised", "13. Umweltmanagement überprüft und überarbeitet")]
    [TestCase(13, "14. Beredskabsplan gennemgået og revideret", "14. Contingency plan reviewed and revised", "14. Notfallplan überprüft und überarbeitet")]
    public async Task UpdatePlanning_AreaRuleMonths2_ReturnsSuccess(int areaRuleNo, string danishTranslation, string englishTranslation, string germanTranslation)
    {
        // Arrange
        var core = await GetCore();
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
            RuleId = areaRules[areaRuleNo].Id,
            ComplianceEnabled = true,
            PropertyId = properties[0].Id,
            Status = true,
            SendNotifications = true,
            StartDate = new DateTime(2023, 4, 12, 0, 0,0),
            UseStartDateAsStartOfPeriod = true,
            TypeSpecificFields = new AreaRuleTypePlanningModel
            {
                Alarm = AreaRuleT2AlarmsEnum.No,
                DayOfMonth = 1,
                DayOfWeek = 0,
                HoursAndEnergyEnabled = false,
                RepeatEvery = 2,
                RepeatType = 3
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
        var englishLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "en-US");
        var germanLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "de-DE");
        var danishLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "da");

        // Assert result
        Assert.NotNull(result);
        Assert.That(result.Success, Is.EqualTo(true));

        // Assert areaRules
        Assert.NotNull(areaRules);
        Assert.That(areaRules.Count, Is.EqualTo(14));
        Assert.That(areaRules[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[1].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[2].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[3].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[4].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[5].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[6].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[7].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[8].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[9].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[10].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[11].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[12].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[13].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[1].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[2].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[3].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[4].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[5].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[6].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[7].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[8].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[9].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[10].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[11].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[12].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[0].EformName, Is.EqualTo("01. Gyllekøling"));
        Assert.That(areaRules[1].EformName, Is.EqualTo("02. Forsuring"));
        Assert.That(areaRules[2].EformName, Is.EqualTo("03. Luftrensning"));
        Assert.That(areaRules[3].EformName, Is.EqualTo("04. Beholderkontrol gennemført"));
        Assert.That(areaRules[4].EformName, Is.EqualTo("05. Gyllebeholdere"));
        Assert.That(areaRules[5].EformName, Is.EqualTo("06. Gyllepumper, - miksere, - seperatorer og spredere"));
        Assert.That(areaRules[6].EformName, Is.EqualTo("07. Forsyningssystemer til vand og foder"));
        Assert.That(areaRules[7].EformName, Is.EqualTo("08. Varme-, køle- og ventilationssystemer samt temperaturfølere"));
        Assert.That(areaRules[8].EformName, Is.EqualTo("09. Siloer og transportudstyr"));
        Assert.That(areaRules[9].EformName, Is.EqualTo("10. Luftrensningssystemer"));
        Assert.That(areaRules[10].EformName, Is.EqualTo("11. Udstyr til drikkevand"));
        Assert.That(areaRules[11].EformName, Is.EqualTo("12. Maskiner til udbringning af husdyrgødning samt doseringsmekanisme- eller dyse"));
        Assert.That(areaRules[12].EformName, Is.EqualTo("13. Miljøledelse"));
        Assert.That(areaRules[13].EformName, Is.EqualTo("14. Beredskabsplan"));

        // Assert areaRuleTranslations
        Assert.NotNull(areaRuleTranslations);
        Assert.That(areaRuleTranslations.Count, Is.EqualTo(42));
        Assert.That(areaRuleTranslations[0].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[0].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[0].atr.Name, Is.EqualTo("01. Gyllekøling"));
        Assert.That(areaRuleTranslations[1].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[1].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[1].atr.Name, Is.EqualTo("01. Slurry cooling"));
        Assert.That(areaRuleTranslations[2].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[2].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[2].atr.Name, Is.EqualTo("01. Schlammkühlung"));
        Assert.That(areaRuleTranslations[3].atr.AreaRuleId, Is.EqualTo(areaRules[1].Id));
        Assert.That(areaRuleTranslations[3].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[3].atr.Name, Is.EqualTo("02. Forsuring"));
        Assert.That(areaRuleTranslations[4].atr.AreaRuleId, Is.EqualTo(areaRules[1].Id));
        Assert.That(areaRuleTranslations[4].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[4].atr.Name, Is.EqualTo("02. Acidification"));
        Assert.That(areaRuleTranslations[5].atr.AreaRuleId, Is.EqualTo(areaRules[1].Id));
        Assert.That(areaRuleTranslations[5].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[5].atr.Name, Is.EqualTo("02. Ansäuerung"));
        Assert.That(areaRuleTranslations[6].atr.AreaRuleId, Is.EqualTo(areaRules[2].Id));
        Assert.That(areaRuleTranslations[6].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[6].atr.Name, Is.EqualTo("03. Luftrensning"));
        Assert.That(areaRuleTranslations[7].atr.AreaRuleId, Is.EqualTo(areaRules[2].Id));
        Assert.That(areaRuleTranslations[7].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[7].atr.Name, Is.EqualTo("03. Air purification"));
        Assert.That(areaRuleTranslations[8].atr.AreaRuleId, Is.EqualTo(areaRules[2].Id));
        Assert.That(areaRuleTranslations[8].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[8].atr.Name, Is.EqualTo("03. Luftreinigung"));
        Assert.That(areaRuleTranslations[9].atr.AreaRuleId, Is.EqualTo(areaRules[3].Id));
        Assert.That(areaRuleTranslations[9].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[9].atr.Name, Is.EqualTo("04. Beholderkontrol gennemført"));
        Assert.That(areaRuleTranslations[10].atr.AreaRuleId, Is.EqualTo(areaRules[3].Id));
        Assert.That(areaRuleTranslations[10].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[10].atr.Name, Is.EqualTo("04. Container control completed"));
        Assert.That(areaRuleTranslations[11].atr.AreaRuleId, Is.EqualTo(areaRules[3].Id));
        Assert.That(areaRuleTranslations[11].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[11].atr.Name, Is.EqualTo("04. Behälterkontrolle abgeschlossen"));
        Assert.That(areaRuleTranslations[12].atr.AreaRuleId, Is.EqualTo(areaRules[4].Id));
        Assert.That(areaRuleTranslations[12].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[12].atr.Name, Is.EqualTo("05. Gyllebeholdere"));
        Assert.That(areaRuleTranslations[13].atr.AreaRuleId, Is.EqualTo(areaRules[4].Id));
        Assert.That(areaRuleTranslations[13].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[13].atr.Name, Is.EqualTo("05. Slurry containers"));
        Assert.That(areaRuleTranslations[14].atr.AreaRuleId, Is.EqualTo(areaRules[4].Id));
        Assert.That(areaRuleTranslations[14].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[14].atr.Name, Is.EqualTo("05. Güllebehälter"));
        Assert.That(areaRuleTranslations[15].atr.AreaRuleId, Is.EqualTo(areaRules[5].Id));
        Assert.That(areaRuleTranslations[15].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[15].atr.Name, Is.EqualTo("06. Gyllepumper, - miksere, - seperatorer og spredere"));
        Assert.That(areaRuleTranslations[16].atr.AreaRuleId, Is.EqualTo(areaRules[5].Id));
        Assert.That(areaRuleTranslations[16].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[16].atr.Name, Is.EqualTo("06. Slurry pumps, - mixers, - separators and spreaders"));
        Assert.That(areaRuleTranslations[17].atr.AreaRuleId, Is.EqualTo(areaRules[5].Id));
        Assert.That(areaRuleTranslations[17].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[17].atr.Name, Is.EqualTo("06. Schlammpumpen, - Mischer, - Separatoren und Verteiler"));
        Assert.That(areaRuleTranslations[18].atr.AreaRuleId, Is.EqualTo(areaRules[6].Id));
        Assert.That(areaRuleTranslations[18].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[18].atr.Name, Is.EqualTo("07. Forsyningssystemer til vand og foder"));
        Assert.That(areaRuleTranslations[19].atr.AreaRuleId, Is.EqualTo(areaRules[6].Id));
        Assert.That(areaRuleTranslations[19].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[19].atr.Name, Is.EqualTo("07. Supply systems for water and feed"));
        Assert.That(areaRuleTranslations[20].atr.AreaRuleId, Is.EqualTo(areaRules[6].Id));
        Assert.That(areaRuleTranslations[20].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[20].atr.Name, Is.EqualTo("07. Versorgungssysteme für Wasser und Futter"));
        Assert.That(areaRuleTranslations[21].atr.AreaRuleId, Is.EqualTo(areaRules[7].Id));
        Assert.That(areaRuleTranslations[21].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[21].atr.Name, Is.EqualTo("08. Varme-, køle- og ventilationssystemer samt temperaturfølere"));
        Assert.That(areaRuleTranslations[22].atr.AreaRuleId, Is.EqualTo(areaRules[7].Id));
        Assert.That(areaRuleTranslations[22].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[22].atr.Name, Is.EqualTo("08. Heating, cooling and ventilation systems and temperature sensors"));
        Assert.That(areaRuleTranslations[23].atr.AreaRuleId, Is.EqualTo(areaRules[7].Id));
        Assert.That(areaRuleTranslations[23].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[23].atr.Name, Is.EqualTo("08. Heizungs-, Kühl- und Lüftungssysteme und Temperatursensoren"));
        Assert.That(areaRuleTranslations[24].atr.AreaRuleId, Is.EqualTo(areaRules[8].Id));
        Assert.That(areaRuleTranslations[24].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[24].atr.Name, Is.EqualTo("09. Siloer og transportudstyr"));
        Assert.That(areaRuleTranslations[25].atr.AreaRuleId, Is.EqualTo(areaRules[8].Id));
        Assert.That(areaRuleTranslations[25].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[25].atr.Name, Is.EqualTo("09. Silos and transport equipment"));
        Assert.That(areaRuleTranslations[26].atr.AreaRuleId, Is.EqualTo(areaRules[8].Id));
        Assert.That(areaRuleTranslations[26].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[26].atr.Name, Is.EqualTo("09. Silos und Transportgeräte"));
        Assert.That(areaRuleTranslations[27].atr.AreaRuleId, Is.EqualTo(areaRules[9].Id));
        Assert.That(areaRuleTranslations[27].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[27].atr.Name, Is.EqualTo("10. Luftrensningssystemer"));
        Assert.That(areaRuleTranslations[28].atr.AreaRuleId, Is.EqualTo(areaRules[9].Id));
        Assert.That(areaRuleTranslations[28].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[28].atr.Name, Is.EqualTo("10. Air purification systems"));
        Assert.That(areaRuleTranslations[29].atr.AreaRuleId, Is.EqualTo(areaRules[9].Id));
        Assert.That(areaRuleTranslations[29].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[29].atr.Name, Is.EqualTo("10. Luftreinigungssysteme"));
        Assert.That(areaRuleTranslations[30].atr.AreaRuleId, Is.EqualTo(areaRules[10].Id));
        Assert.That(areaRuleTranslations[30].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[30].atr.Name, Is.EqualTo("11. Udstyr til drikkevand"));
        Assert.That(areaRuleTranslations[31].atr.AreaRuleId, Is.EqualTo(areaRules[10].Id));
        Assert.That(areaRuleTranslations[31].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[31].atr.Name, Is.EqualTo("11. Equipment for drinking water"));
        Assert.That(areaRuleTranslations[32].atr.AreaRuleId, Is.EqualTo(areaRules[10].Id));
        Assert.That(areaRuleTranslations[32].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[32].atr.Name, Is.EqualTo("11. Ausrüstung für Trinkwasser"));
        Assert.That(areaRuleTranslations[33].atr.AreaRuleId, Is.EqualTo(areaRules[11].Id));
        Assert.That(areaRuleTranslations[33].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[33].atr.Name, Is.EqualTo("12. Maskiner til udbringning af husdyrgødning samt doseringsmekanisme- eller dyse"));
        Assert.That(areaRuleTranslations[34].atr.AreaRuleId, Is.EqualTo(areaRules[11].Id));
        Assert.That(areaRuleTranslations[34].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[34].atr.Name, Is.EqualTo("12. Machines for spreading livestock manure and dosing mechanisms or nozzles"));
        Assert.That(areaRuleTranslations[35].atr.AreaRuleId, Is.EqualTo(areaRules[11].Id));
        Assert.That(areaRuleTranslations[35].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[35].atr.Name, Is.EqualTo("12. Maschinen zum Ausbringen von Viehmist und Dosiervorrichtungen oder Düsen"));
        Assert.That(areaRuleTranslations[36].atr.AreaRuleId, Is.EqualTo(areaRules[12].Id));
        Assert.That(areaRuleTranslations[36].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[36].atr.Name, Is.EqualTo("13. Miljøledelse gennemgået og revideret"));
        Assert.That(areaRuleTranslations[37].atr.AreaRuleId, Is.EqualTo(areaRules[12].Id));
        Assert.That(areaRuleTranslations[37].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[37].atr.Name, Is.EqualTo("13. Environmental management reviewed and revised"));
        Assert.That(areaRuleTranslations[38].atr.AreaRuleId, Is.EqualTo(areaRules[12].Id));
        Assert.That(areaRuleTranslations[38].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[38].atr.Name, Is.EqualTo("13. Umweltmanagement überprüft und überarbeitet"));
        Assert.That(areaRuleTranslations[39].atr.AreaRuleId, Is.EqualTo(areaRules[13].Id));
        Assert.That(areaRuleTranslations[39].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[39].atr.Name, Is.EqualTo("14. Beredskabsplan gennemgået og revideret"));
        Assert.That(areaRuleTranslations[40].atr.AreaRuleId, Is.EqualTo(areaRules[13].Id));
        Assert.That(areaRuleTranslations[40].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[40].atr.Name, Is.EqualTo("14. Contingency plan reviewed and revised"));
        Assert.That(areaRuleTranslations[41].atr.AreaRuleId, Is.EqualTo(areaRules[13].Id));
        Assert.That(areaRuleTranslations[41].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[41].atr.Name, Is.EqualTo("14. Notfallplan überprüft und überarbeitet"));

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
        Assert.That(areaRulePlannings[0].AreaRuleId, Is.EqualTo(areaRules[areaRuleNo].Id));
        Assert.That(areaRulePlannings[0].ItemPlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(areaRulePlannings[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRulePlannings[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRulePlannings[0].ComplianceEnabled, Is.EqualTo(true));
        Assert.That(areaRulePlannings[0].SendNotifications, Is.EqualTo(true));

        // Assert plannings
        Assert.That(plannings, Is.Not.Null);
        Assert.That(plannings.Count, Is.EqualTo(1));
        Assert.That(plannings[0].RelatedEFormId, Is.EqualTo(areaRules[areaRuleNo].EformId));
        Assert.That(plannings[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
        Assert.That(plannings[0].SdkFolderId, Is.EqualTo(areaRules[areaRuleNo].FolderId));
        Assert.That(plannings[0].LastExecutedTime, Is.Not.Null);
        Assert.That(plannings[0].LastExecutedTime, Is.EqualTo(new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 0,0,0)));
        var now = DateTime.UtcNow;
        var nextExecutionTime =
            new DateTime(now.Year, 4, 12);
        // var months = new[] {1, 3, 5, 7, 9, 11};
        //
        // if (months.Contains(now.Month))
        // {
        //     nextExecutionTime =
        //         new DateTime(now.Year, now.Month + 2, 1,
        //             0, 0, 0);
        // }
        // else
        // {
        //     nextExecutionTime =
        //         new DateTime(now.Year, now.Month + 1, 1,
        //             0, 0, 0);
        // }
        // if (nextExecutionTime < now)
        // {
        nextExecutionTime = nextExecutionTime.AddMonths(2);
        // }

        Assert.That(plannings[0].NextExecutionTime, Is.EqualTo(nextExecutionTime));
        Assert.That(plannings[0].RepeatEvery, Is.EqualTo(2));
        Assert.That(plannings[0].RepeatType, Is.EqualTo(RepeatType.Month));

        // Assert planningNameTranslations
        Assert.That(planningNameTranslations, Is.Not.Null);
        Assert.That(planningNameTranslations.Count, Is.EqualTo(3));
        Assert.That(planningNameTranslations[0].Name, Is.EqualTo(danishTranslation));
        Assert.That(planningNameTranslations[0].LanguageId, Is.EqualTo(danishLanguage.Id));
        Assert.That(planningNameTranslations[0].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(planningNameTranslations[1].Name, Is.EqualTo(englishTranslation));
        Assert.That(planningNameTranslations[1].LanguageId, Is.EqualTo(englishLanguage.Id));
        Assert.That(planningNameTranslations[1].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(planningNameTranslations[2].Name, Is.EqualTo(germanTranslation));
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
        Assert.That(cases[0].CheckListId, Is.EqualTo(areaRules[areaRuleNo].EformId));
        Assert.That(cases[0].FolderId, Is.Null);
        Assert.That(cases[0].Status, Is.EqualTo(66));
        Assert.That(cases[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
    }

    // Should test the UpdatePlanning method for area rule "00. Logbøger" with repeat type "months" and repeat every "2"
    [Test]
    [TestCase(0, "01. Gyllekøling", "01. Slurry cooling", "01. Schlammkühlung")]
    [TestCase(1, "02. Forsuring", "02. Acidification", "02. Ansäuerung")]
    [TestCase(2, "03. Luftrensning", "03. Air purification", "03. Luftreinigung")]
    [TestCase(3, "04. Beholderkontrol gennemført", "04. Container control completed", "04. Behälterkontrolle abgeschlossen")]
    [TestCase(4, "05. Gyllebeholdere", "05. Slurry containers", "05. Güllebehälter")]
    [TestCase(5, "06. Gyllepumper, - miksere, - seperatorer og spredere", "06. Slurry pumps, - mixers, - separators and spreaders", "06. Schlammpumpen, - Mischer, - Separatoren und Verteiler")]
    [TestCase(6, "07. Forsyningssystemer til vand og foder", "07. Supply systems for water and feed", "07. Versorgungssysteme für Wasser und Futter")]
    [TestCase(7, "08. Varme-, køle- og ventilationssystemer samt temperaturfølere", "08. Heating, cooling and ventilation systems and temperature sensors", "08. Heizungs-, Kühl- und Lüftungssysteme und Temperatursensoren")]
    [TestCase(8, "09. Siloer og transportudstyr", "09. Silos and transport equipment", "09. Silos und Transportgeräte")]
    [TestCase(9, "10. Luftrensningssystemer", "10. Air purification systems", "10. Luftreinigungssysteme")]
    [TestCase(10, "11. Udstyr til drikkevand", "11. Equipment for drinking water", "11. Ausrüstung für Trinkwasser")]
    [TestCase(11, "12. Maskiner til udbringning af husdyrgødning samt doseringsmekanisme- eller dyse", "12. Machines for spreading livestock manure and dosing mechanisms or nozzles", "12. Maschinen zum Ausbringen von Viehmist und Dosiervorrichtungen oder Düsen")]
    [TestCase(12, "13. Miljøledelse gennemgået og revideret", "13. Environmental management reviewed and revised", "13. Umweltmanagement überprüft und überarbeitet")]
    [TestCase(13, "14. Beredskabsplan gennemgået og revideret", "14. Contingency plan reviewed and revised", "14. Notfallplan überprüft und überarbeitet")]
    public async Task UpdatePlanning_AreaRuleMonths3_ReturnsSuccess(int areaRuleNo, string danishTranslation, string englishTranslation, string germanTranslation)
    {
        // Arrange
        var core = await GetCore();
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
            RuleId = areaRules[areaRuleNo].Id,
            ComplianceEnabled = true,
            PropertyId = properties[0].Id,
            Status = true,
            SendNotifications = true,
            StartDate = new DateTime(2023, 4, 12, 0, 0,0),
            UseStartDateAsStartOfPeriod = true,
            TypeSpecificFields = new AreaRuleTypePlanningModel
            {
                Alarm = AreaRuleT2AlarmsEnum.No,
                DayOfMonth = 1,
                DayOfWeek = 0,
                HoursAndEnergyEnabled = false,
                RepeatEvery = 3,
                RepeatType = 3
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
        var englishLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "en-US");
        var germanLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "de-DE");
        var danishLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "da");

        // Assert result
        Assert.NotNull(result);
        Assert.That(result.Success, Is.EqualTo(true));

        // Assert areaRules
        Assert.NotNull(areaRules);
        Assert.That(areaRules.Count, Is.EqualTo(14));
        Assert.That(areaRules[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[1].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[2].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[3].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[4].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[5].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[6].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[7].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[8].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[9].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[10].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[11].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[12].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[13].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[1].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[2].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[3].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[4].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[5].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[6].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[7].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[8].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[9].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[10].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[11].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[12].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[0].EformName, Is.EqualTo("01. Gyllekøling"));
        Assert.That(areaRules[1].EformName, Is.EqualTo("02. Forsuring"));
        Assert.That(areaRules[2].EformName, Is.EqualTo("03. Luftrensning"));
        Assert.That(areaRules[3].EformName, Is.EqualTo("04. Beholderkontrol gennemført"));
        Assert.That(areaRules[4].EformName, Is.EqualTo("05. Gyllebeholdere"));
        Assert.That(areaRules[5].EformName, Is.EqualTo("06. Gyllepumper, - miksere, - seperatorer og spredere"));
        Assert.That(areaRules[6].EformName, Is.EqualTo("07. Forsyningssystemer til vand og foder"));
        Assert.That(areaRules[7].EformName, Is.EqualTo("08. Varme-, køle- og ventilationssystemer samt temperaturfølere"));
        Assert.That(areaRules[8].EformName, Is.EqualTo("09. Siloer og transportudstyr"));
        Assert.That(areaRules[9].EformName, Is.EqualTo("10. Luftrensningssystemer"));
        Assert.That(areaRules[10].EformName, Is.EqualTo("11. Udstyr til drikkevand"));
        Assert.That(areaRules[11].EformName, Is.EqualTo("12. Maskiner til udbringning af husdyrgødning samt doseringsmekanisme- eller dyse"));
        Assert.That(areaRules[12].EformName, Is.EqualTo("13. Miljøledelse"));
        Assert.That(areaRules[13].EformName, Is.EqualTo("14. Beredskabsplan"));

        // Assert areaRuleTranslations
        Assert.NotNull(areaRuleTranslations);
        Assert.That(areaRuleTranslations.Count, Is.EqualTo(42));
        Assert.That(areaRuleTranslations[0].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[0].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[0].atr.Name, Is.EqualTo("01. Gyllekøling"));
        Assert.That(areaRuleTranslations[1].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[1].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[1].atr.Name, Is.EqualTo("01. Slurry cooling"));
        Assert.That(areaRuleTranslations[2].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[2].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[2].atr.Name, Is.EqualTo("01. Schlammkühlung"));
        Assert.That(areaRuleTranslations[3].atr.AreaRuleId, Is.EqualTo(areaRules[1].Id));
        Assert.That(areaRuleTranslations[3].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[3].atr.Name, Is.EqualTo("02. Forsuring"));
        Assert.That(areaRuleTranslations[4].atr.AreaRuleId, Is.EqualTo(areaRules[1].Id));
        Assert.That(areaRuleTranslations[4].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[4].atr.Name, Is.EqualTo("02. Acidification"));
        Assert.That(areaRuleTranslations[5].atr.AreaRuleId, Is.EqualTo(areaRules[1].Id));
        Assert.That(areaRuleTranslations[5].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[5].atr.Name, Is.EqualTo("02. Ansäuerung"));
        Assert.That(areaRuleTranslations[6].atr.AreaRuleId, Is.EqualTo(areaRules[2].Id));
        Assert.That(areaRuleTranslations[6].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[6].atr.Name, Is.EqualTo("03. Luftrensning"));
        Assert.That(areaRuleTranslations[7].atr.AreaRuleId, Is.EqualTo(areaRules[2].Id));
        Assert.That(areaRuleTranslations[7].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[7].atr.Name, Is.EqualTo("03. Air purification"));
        Assert.That(areaRuleTranslations[8].atr.AreaRuleId, Is.EqualTo(areaRules[2].Id));
        Assert.That(areaRuleTranslations[8].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[8].atr.Name, Is.EqualTo("03. Luftreinigung"));
        Assert.That(areaRuleTranslations[9].atr.AreaRuleId, Is.EqualTo(areaRules[3].Id));
        Assert.That(areaRuleTranslations[9].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[9].atr.Name, Is.EqualTo("04. Beholderkontrol gennemført"));
        Assert.That(areaRuleTranslations[10].atr.AreaRuleId, Is.EqualTo(areaRules[3].Id));
        Assert.That(areaRuleTranslations[10].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[10].atr.Name, Is.EqualTo("04. Container control completed"));
        Assert.That(areaRuleTranslations[11].atr.AreaRuleId, Is.EqualTo(areaRules[3].Id));
        Assert.That(areaRuleTranslations[11].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[11].atr.Name, Is.EqualTo("04. Behälterkontrolle abgeschlossen"));
        Assert.That(areaRuleTranslations[12].atr.AreaRuleId, Is.EqualTo(areaRules[4].Id));
        Assert.That(areaRuleTranslations[12].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[12].atr.Name, Is.EqualTo("05. Gyllebeholdere"));
        Assert.That(areaRuleTranslations[13].atr.AreaRuleId, Is.EqualTo(areaRules[4].Id));
        Assert.That(areaRuleTranslations[13].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[13].atr.Name, Is.EqualTo("05. Slurry containers"));
        Assert.That(areaRuleTranslations[14].atr.AreaRuleId, Is.EqualTo(areaRules[4].Id));
        Assert.That(areaRuleTranslations[14].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[14].atr.Name, Is.EqualTo("05. Güllebehälter"));
        Assert.That(areaRuleTranslations[15].atr.AreaRuleId, Is.EqualTo(areaRules[5].Id));
        Assert.That(areaRuleTranslations[15].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[15].atr.Name, Is.EqualTo("06. Gyllepumper, - miksere, - seperatorer og spredere"));
        Assert.That(areaRuleTranslations[16].atr.AreaRuleId, Is.EqualTo(areaRules[5].Id));
        Assert.That(areaRuleTranslations[16].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[16].atr.Name, Is.EqualTo("06. Slurry pumps, - mixers, - separators and spreaders"));
        Assert.That(areaRuleTranslations[17].atr.AreaRuleId, Is.EqualTo(areaRules[5].Id));
        Assert.That(areaRuleTranslations[17].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[17].atr.Name, Is.EqualTo("06. Schlammpumpen, - Mischer, - Separatoren und Verteiler"));
        Assert.That(areaRuleTranslations[18].atr.AreaRuleId, Is.EqualTo(areaRules[6].Id));
        Assert.That(areaRuleTranslations[18].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[18].atr.Name, Is.EqualTo("07. Forsyningssystemer til vand og foder"));
        Assert.That(areaRuleTranslations[19].atr.AreaRuleId, Is.EqualTo(areaRules[6].Id));
        Assert.That(areaRuleTranslations[19].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[19].atr.Name, Is.EqualTo("07. Supply systems for water and feed"));
        Assert.That(areaRuleTranslations[20].atr.AreaRuleId, Is.EqualTo(areaRules[6].Id));
        Assert.That(areaRuleTranslations[20].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[20].atr.Name, Is.EqualTo("07. Versorgungssysteme für Wasser und Futter"));
        Assert.That(areaRuleTranslations[21].atr.AreaRuleId, Is.EqualTo(areaRules[7].Id));
        Assert.That(areaRuleTranslations[21].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[21].atr.Name, Is.EqualTo("08. Varme-, køle- og ventilationssystemer samt temperaturfølere"));
        Assert.That(areaRuleTranslations[22].atr.AreaRuleId, Is.EqualTo(areaRules[7].Id));
        Assert.That(areaRuleTranslations[22].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[22].atr.Name, Is.EqualTo("08. Heating, cooling and ventilation systems and temperature sensors"));
        Assert.That(areaRuleTranslations[23].atr.AreaRuleId, Is.EqualTo(areaRules[7].Id));
        Assert.That(areaRuleTranslations[23].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[23].atr.Name, Is.EqualTo("08. Heizungs-, Kühl- und Lüftungssysteme und Temperatursensoren"));
        Assert.That(areaRuleTranslations[24].atr.AreaRuleId, Is.EqualTo(areaRules[8].Id));
        Assert.That(areaRuleTranslations[24].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[24].atr.Name, Is.EqualTo("09. Siloer og transportudstyr"));
        Assert.That(areaRuleTranslations[25].atr.AreaRuleId, Is.EqualTo(areaRules[8].Id));
        Assert.That(areaRuleTranslations[25].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[25].atr.Name, Is.EqualTo("09. Silos and transport equipment"));
        Assert.That(areaRuleTranslations[26].atr.AreaRuleId, Is.EqualTo(areaRules[8].Id));
        Assert.That(areaRuleTranslations[26].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[26].atr.Name, Is.EqualTo("09. Silos und Transportgeräte"));
        Assert.That(areaRuleTranslations[27].atr.AreaRuleId, Is.EqualTo(areaRules[9].Id));
        Assert.That(areaRuleTranslations[27].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[27].atr.Name, Is.EqualTo("10. Luftrensningssystemer"));
        Assert.That(areaRuleTranslations[28].atr.AreaRuleId, Is.EqualTo(areaRules[9].Id));
        Assert.That(areaRuleTranslations[28].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[28].atr.Name, Is.EqualTo("10. Air purification systems"));
        Assert.That(areaRuleTranslations[29].atr.AreaRuleId, Is.EqualTo(areaRules[9].Id));
        Assert.That(areaRuleTranslations[29].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[29].atr.Name, Is.EqualTo("10. Luftreinigungssysteme"));
        Assert.That(areaRuleTranslations[30].atr.AreaRuleId, Is.EqualTo(areaRules[10].Id));
        Assert.That(areaRuleTranslations[30].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[30].atr.Name, Is.EqualTo("11. Udstyr til drikkevand"));
        Assert.That(areaRuleTranslations[31].atr.AreaRuleId, Is.EqualTo(areaRules[10].Id));
        Assert.That(areaRuleTranslations[31].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[31].atr.Name, Is.EqualTo("11. Equipment for drinking water"));
        Assert.That(areaRuleTranslations[32].atr.AreaRuleId, Is.EqualTo(areaRules[10].Id));
        Assert.That(areaRuleTranslations[32].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[32].atr.Name, Is.EqualTo("11. Ausrüstung für Trinkwasser"));
        Assert.That(areaRuleTranslations[33].atr.AreaRuleId, Is.EqualTo(areaRules[11].Id));
        Assert.That(areaRuleTranslations[33].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[33].atr.Name, Is.EqualTo("12. Maskiner til udbringning af husdyrgødning samt doseringsmekanisme- eller dyse"));
        Assert.That(areaRuleTranslations[34].atr.AreaRuleId, Is.EqualTo(areaRules[11].Id));
        Assert.That(areaRuleTranslations[34].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[34].atr.Name, Is.EqualTo("12. Machines for spreading livestock manure and dosing mechanisms or nozzles"));
        Assert.That(areaRuleTranslations[35].atr.AreaRuleId, Is.EqualTo(areaRules[11].Id));
        Assert.That(areaRuleTranslations[35].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[35].atr.Name, Is.EqualTo("12. Maschinen zum Ausbringen von Viehmist und Dosiervorrichtungen oder Düsen"));
        Assert.That(areaRuleTranslations[36].atr.AreaRuleId, Is.EqualTo(areaRules[12].Id));
        Assert.That(areaRuleTranslations[36].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[36].atr.Name, Is.EqualTo("13. Miljøledelse gennemgået og revideret"));
        Assert.That(areaRuleTranslations[37].atr.AreaRuleId, Is.EqualTo(areaRules[12].Id));
        Assert.That(areaRuleTranslations[37].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[37].atr.Name, Is.EqualTo("13. Environmental management reviewed and revised"));
        Assert.That(areaRuleTranslations[38].atr.AreaRuleId, Is.EqualTo(areaRules[12].Id));
        Assert.That(areaRuleTranslations[38].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[38].atr.Name, Is.EqualTo("13. Umweltmanagement überprüft und überarbeitet"));
        Assert.That(areaRuleTranslations[39].atr.AreaRuleId, Is.EqualTo(areaRules[13].Id));
        Assert.That(areaRuleTranslations[39].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[39].atr.Name, Is.EqualTo("14. Beredskabsplan gennemgået og revideret"));
        Assert.That(areaRuleTranslations[40].atr.AreaRuleId, Is.EqualTo(areaRules[13].Id));
        Assert.That(areaRuleTranslations[40].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[40].atr.Name, Is.EqualTo("14. Contingency plan reviewed and revised"));
        Assert.That(areaRuleTranslations[41].atr.AreaRuleId, Is.EqualTo(areaRules[13].Id));
        Assert.That(areaRuleTranslations[41].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[41].atr.Name, Is.EqualTo("14. Notfallplan überprüft und überarbeitet"));

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
        Assert.That(areaRulePlannings[0].AreaRuleId, Is.EqualTo(areaRules[areaRuleNo].Id));
        Assert.That(areaRulePlannings[0].ItemPlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(areaRulePlannings[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRulePlannings[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRulePlannings[0].ComplianceEnabled, Is.EqualTo(true));
        Assert.That(areaRulePlannings[0].SendNotifications, Is.EqualTo(true));

        // Assert plannings
        Assert.That(plannings, Is.Not.Null);
        Assert.That(plannings.Count, Is.EqualTo(1));
        Assert.That(plannings[0].RelatedEFormId, Is.EqualTo(areaRules[areaRuleNo].EformId));
        Assert.That(plannings[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
        Assert.That(plannings[0].SdkFolderId, Is.EqualTo(areaRules[areaRuleNo].FolderId));
        Assert.That(plannings[0].LastExecutedTime, Is.Not.Null);
        Assert.That(plannings[0].LastExecutedTime, Is.EqualTo(new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 0,0,0)));
        var now = DateTime.UtcNow;
        var nextExecutionTime =
            new DateTime(now.Year, 4, 12);
        // var months = new[] {1, 4, 7, 10};
        //
        // if (months.Contains(now.Month))
        // {
        //     nextExecutionTime =
        //         new DateTime(now.Year, now.Month + 3, 1,
        //             0, 0, 0);
        // }
        // else
        // {
        //     months = new[] {2, 5, 8, 11};
        //     if (months.Contains(now.Month))
        //     {
        //         nextExecutionTime =
        //             new DateTime(now.Year, now.Month + 2, 1,
        //                 0, 0, 0);
        //     }
        //     else
        //     {
        //         nextExecutionTime =
        //             new DateTime(now.Year, now.Month + 1, 1,
        //                 0, 0, 0);
        //     }
        // }
        // if (nextExecutionTime < now)
        // {
            nextExecutionTime = nextExecutionTime.AddMonths(3);
        //}

        Assert.That(plannings[0].NextExecutionTime, Is.EqualTo(nextExecutionTime));
        Assert.That(plannings[0].RepeatEvery, Is.EqualTo(3));
        Assert.That(plannings[0].RepeatType, Is.EqualTo(RepeatType.Month));

        // Assert planningNameTranslations
        Assert.That(planningNameTranslations, Is.Not.Null);
        Assert.That(planningNameTranslations.Count, Is.EqualTo(3));
        Assert.That(planningNameTranslations[0].Name, Is.EqualTo(danishTranslation));
        Assert.That(planningNameTranslations[0].LanguageId, Is.EqualTo(danishLanguage.Id));
        Assert.That(planningNameTranslations[0].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(planningNameTranslations[1].Name, Is.EqualTo(englishTranslation));
        Assert.That(planningNameTranslations[1].LanguageId, Is.EqualTo(englishLanguage.Id));
        Assert.That(planningNameTranslations[1].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(planningNameTranslations[2].Name, Is.EqualTo(germanTranslation));
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
        Assert.That(cases[0].CheckListId, Is.EqualTo(areaRules[areaRuleNo].EformId));
        Assert.That(cases[0].FolderId, Is.Null);
        Assert.That(cases[0].Status, Is.EqualTo(66));
        Assert.That(cases[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
    }

    // Should test the UpdatePlanning method for area rule "00. Logbøger" with repeat type "months" and repeat every "2"
    [Test]
    [TestCase(0, "01. Gyllekøling", "01. Slurry cooling", "01. Schlammkühlung")]
    [TestCase(1, "02. Forsuring", "02. Acidification", "02. Ansäuerung")]
    [TestCase(2, "03. Luftrensning", "03. Air purification", "03. Luftreinigung")]
    [TestCase(3, "04. Beholderkontrol gennemført", "04. Container control completed", "04. Behälterkontrolle abgeschlossen")]
    [TestCase(4, "05. Gyllebeholdere", "05. Slurry containers", "05. Güllebehälter")]
    [TestCase(5, "06. Gyllepumper, - miksere, - seperatorer og spredere", "06. Slurry pumps, - mixers, - separators and spreaders", "06. Schlammpumpen, - Mischer, - Separatoren und Verteiler")]
    [TestCase(6, "07. Forsyningssystemer til vand og foder", "07. Supply systems for water and feed", "07. Versorgungssysteme für Wasser und Futter")]
    [TestCase(7, "08. Varme-, køle- og ventilationssystemer samt temperaturfølere", "08. Heating, cooling and ventilation systems and temperature sensors", "08. Heizungs-, Kühl- und Lüftungssysteme und Temperatursensoren")]
    [TestCase(8, "09. Siloer og transportudstyr", "09. Silos and transport equipment", "09. Silos und Transportgeräte")]
    [TestCase(9, "10. Luftrensningssystemer", "10. Air purification systems", "10. Luftreinigungssysteme")]
    [TestCase(10, "11. Udstyr til drikkevand", "11. Equipment for drinking water", "11. Ausrüstung für Trinkwasser")]
    [TestCase(11, "12. Maskiner til udbringning af husdyrgødning samt doseringsmekanisme- eller dyse", "12. Machines for spreading livestock manure and dosing mechanisms or nozzles", "12. Maschinen zum Ausbringen von Viehmist und Dosiervorrichtungen oder Düsen")]
    [TestCase(12, "13. Miljøledelse gennemgået og revideret", "13. Environmental management reviewed and revised", "13. Umweltmanagement überprüft und überarbeitet")]
    [TestCase(13, "14. Beredskabsplan gennemgået og revideret", "14. Contingency plan reviewed and revised", "14. Notfallplan überprüft und überarbeitet")]
    public async Task UpdatePlanning_AreaRuleMonths6_ReturnsSuccess(int areaRuleNo, string danishTranslation, string englishTranslation, string germanTranslation)
    {
        // Arrange
        var core = await GetCore();
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
            RuleId = areaRules[areaRuleNo].Id,
            ComplianceEnabled = true,
            PropertyId = properties[0].Id,
            Status = true,
            SendNotifications = true,
            StartDate = new DateTime(2023, 4, 12, 0, 0,0),
            UseStartDateAsStartOfPeriod = true,
            TypeSpecificFields = new AreaRuleTypePlanningModel
            {
                Alarm = AreaRuleT2AlarmsEnum.No,
                DayOfMonth = 1,
                DayOfWeek = 0,
                HoursAndEnergyEnabled = false,
                RepeatEvery = 6,
                RepeatType = 3
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
        var englishLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "en-US");
        var germanLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "de-DE");
        var danishLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "da");

        // Assert result
        Assert.NotNull(result);
        Assert.That(result.Success, Is.EqualTo(true));

        // Assert areaRules
        Assert.NotNull(areaRules);
        Assert.That(areaRules.Count, Is.EqualTo(14));
        Assert.That(areaRules[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[1].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[2].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[3].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[4].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[5].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[6].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[7].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[8].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[9].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[10].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[11].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[12].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[13].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[1].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[2].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[3].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[4].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[5].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[6].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[7].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[8].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[9].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[10].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[11].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[12].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[0].EformName, Is.EqualTo("01. Gyllekøling"));
        Assert.That(areaRules[1].EformName, Is.EqualTo("02. Forsuring"));
        Assert.That(areaRules[2].EformName, Is.EqualTo("03. Luftrensning"));
        Assert.That(areaRules[3].EformName, Is.EqualTo("04. Beholderkontrol gennemført"));
        Assert.That(areaRules[4].EformName, Is.EqualTo("05. Gyllebeholdere"));
        Assert.That(areaRules[5].EformName, Is.EqualTo("06. Gyllepumper, - miksere, - seperatorer og spredere"));
        Assert.That(areaRules[6].EformName, Is.EqualTo("07. Forsyningssystemer til vand og foder"));
        Assert.That(areaRules[7].EformName, Is.EqualTo("08. Varme-, køle- og ventilationssystemer samt temperaturfølere"));
        Assert.That(areaRules[8].EformName, Is.EqualTo("09. Siloer og transportudstyr"));
        Assert.That(areaRules[9].EformName, Is.EqualTo("10. Luftrensningssystemer"));
        Assert.That(areaRules[10].EformName, Is.EqualTo("11. Udstyr til drikkevand"));
        Assert.That(areaRules[11].EformName, Is.EqualTo("12. Maskiner til udbringning af husdyrgødning samt doseringsmekanisme- eller dyse"));
        Assert.That(areaRules[12].EformName, Is.EqualTo("13. Miljøledelse"));
        Assert.That(areaRules[13].EformName, Is.EqualTo("14. Beredskabsplan"));

        // Assert areaRuleTranslations
        Assert.NotNull(areaRuleTranslations);
        Assert.That(areaRuleTranslations.Count, Is.EqualTo(42));
        Assert.That(areaRuleTranslations[0].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[0].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[0].atr.Name, Is.EqualTo("01. Gyllekøling"));
        Assert.That(areaRuleTranslations[1].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[1].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[1].atr.Name, Is.EqualTo("01. Slurry cooling"));
        Assert.That(areaRuleTranslations[2].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[2].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[2].atr.Name, Is.EqualTo("01. Schlammkühlung"));
        Assert.That(areaRuleTranslations[3].atr.AreaRuleId, Is.EqualTo(areaRules[1].Id));
        Assert.That(areaRuleTranslations[3].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[3].atr.Name, Is.EqualTo("02. Forsuring"));
        Assert.That(areaRuleTranslations[4].atr.AreaRuleId, Is.EqualTo(areaRules[1].Id));
        Assert.That(areaRuleTranslations[4].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[4].atr.Name, Is.EqualTo("02. Acidification"));
        Assert.That(areaRuleTranslations[5].atr.AreaRuleId, Is.EqualTo(areaRules[1].Id));
        Assert.That(areaRuleTranslations[5].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[5].atr.Name, Is.EqualTo("02. Ansäuerung"));
        Assert.That(areaRuleTranslations[6].atr.AreaRuleId, Is.EqualTo(areaRules[2].Id));
        Assert.That(areaRuleTranslations[6].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[6].atr.Name, Is.EqualTo("03. Luftrensning"));
        Assert.That(areaRuleTranslations[7].atr.AreaRuleId, Is.EqualTo(areaRules[2].Id));
        Assert.That(areaRuleTranslations[7].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[7].atr.Name, Is.EqualTo("03. Air purification"));
        Assert.That(areaRuleTranslations[8].atr.AreaRuleId, Is.EqualTo(areaRules[2].Id));
        Assert.That(areaRuleTranslations[8].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[8].atr.Name, Is.EqualTo("03. Luftreinigung"));
        Assert.That(areaRuleTranslations[9].atr.AreaRuleId, Is.EqualTo(areaRules[3].Id));
        Assert.That(areaRuleTranslations[9].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[9].atr.Name, Is.EqualTo("04. Beholderkontrol gennemført"));
        Assert.That(areaRuleTranslations[10].atr.AreaRuleId, Is.EqualTo(areaRules[3].Id));
        Assert.That(areaRuleTranslations[10].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[10].atr.Name, Is.EqualTo("04. Container control completed"));
        Assert.That(areaRuleTranslations[11].atr.AreaRuleId, Is.EqualTo(areaRules[3].Id));
        Assert.That(areaRuleTranslations[11].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[11].atr.Name, Is.EqualTo("04. Behälterkontrolle abgeschlossen"));
        Assert.That(areaRuleTranslations[12].atr.AreaRuleId, Is.EqualTo(areaRules[4].Id));
        Assert.That(areaRuleTranslations[12].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[12].atr.Name, Is.EqualTo("05. Gyllebeholdere"));
        Assert.That(areaRuleTranslations[13].atr.AreaRuleId, Is.EqualTo(areaRules[4].Id));
        Assert.That(areaRuleTranslations[13].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[13].atr.Name, Is.EqualTo("05. Slurry containers"));
        Assert.That(areaRuleTranslations[14].atr.AreaRuleId, Is.EqualTo(areaRules[4].Id));
        Assert.That(areaRuleTranslations[14].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[14].atr.Name, Is.EqualTo("05. Güllebehälter"));
        Assert.That(areaRuleTranslations[15].atr.AreaRuleId, Is.EqualTo(areaRules[5].Id));
        Assert.That(areaRuleTranslations[15].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[15].atr.Name, Is.EqualTo("06. Gyllepumper, - miksere, - seperatorer og spredere"));
        Assert.That(areaRuleTranslations[16].atr.AreaRuleId, Is.EqualTo(areaRules[5].Id));
        Assert.That(areaRuleTranslations[16].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[16].atr.Name, Is.EqualTo("06. Slurry pumps, - mixers, - separators and spreaders"));
        Assert.That(areaRuleTranslations[17].atr.AreaRuleId, Is.EqualTo(areaRules[5].Id));
        Assert.That(areaRuleTranslations[17].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[17].atr.Name, Is.EqualTo("06. Schlammpumpen, - Mischer, - Separatoren und Verteiler"));
        Assert.That(areaRuleTranslations[18].atr.AreaRuleId, Is.EqualTo(areaRules[6].Id));
        Assert.That(areaRuleTranslations[18].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[18].atr.Name, Is.EqualTo("07. Forsyningssystemer til vand og foder"));
        Assert.That(areaRuleTranslations[19].atr.AreaRuleId, Is.EqualTo(areaRules[6].Id));
        Assert.That(areaRuleTranslations[19].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[19].atr.Name, Is.EqualTo("07. Supply systems for water and feed"));
        Assert.That(areaRuleTranslations[20].atr.AreaRuleId, Is.EqualTo(areaRules[6].Id));
        Assert.That(areaRuleTranslations[20].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[20].atr.Name, Is.EqualTo("07. Versorgungssysteme für Wasser und Futter"));
        Assert.That(areaRuleTranslations[21].atr.AreaRuleId, Is.EqualTo(areaRules[7].Id));
        Assert.That(areaRuleTranslations[21].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[21].atr.Name, Is.EqualTo("08. Varme-, køle- og ventilationssystemer samt temperaturfølere"));
        Assert.That(areaRuleTranslations[22].atr.AreaRuleId, Is.EqualTo(areaRules[7].Id));
        Assert.That(areaRuleTranslations[22].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[22].atr.Name, Is.EqualTo("08. Heating, cooling and ventilation systems and temperature sensors"));
        Assert.That(areaRuleTranslations[23].atr.AreaRuleId, Is.EqualTo(areaRules[7].Id));
        Assert.That(areaRuleTranslations[23].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[23].atr.Name, Is.EqualTo("08. Heizungs-, Kühl- und Lüftungssysteme und Temperatursensoren"));
        Assert.That(areaRuleTranslations[24].atr.AreaRuleId, Is.EqualTo(areaRules[8].Id));
        Assert.That(areaRuleTranslations[24].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[24].atr.Name, Is.EqualTo("09. Siloer og transportudstyr"));
        Assert.That(areaRuleTranslations[25].atr.AreaRuleId, Is.EqualTo(areaRules[8].Id));
        Assert.That(areaRuleTranslations[25].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[25].atr.Name, Is.EqualTo("09. Silos and transport equipment"));
        Assert.That(areaRuleTranslations[26].atr.AreaRuleId, Is.EqualTo(areaRules[8].Id));
        Assert.That(areaRuleTranslations[26].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[26].atr.Name, Is.EqualTo("09. Silos und Transportgeräte"));
        Assert.That(areaRuleTranslations[27].atr.AreaRuleId, Is.EqualTo(areaRules[9].Id));
        Assert.That(areaRuleTranslations[27].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[27].atr.Name, Is.EqualTo("10. Luftrensningssystemer"));
        Assert.That(areaRuleTranslations[28].atr.AreaRuleId, Is.EqualTo(areaRules[9].Id));
        Assert.That(areaRuleTranslations[28].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[28].atr.Name, Is.EqualTo("10. Air purification systems"));
        Assert.That(areaRuleTranslations[29].atr.AreaRuleId, Is.EqualTo(areaRules[9].Id));
        Assert.That(areaRuleTranslations[29].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[29].atr.Name, Is.EqualTo("10. Luftreinigungssysteme"));
        Assert.That(areaRuleTranslations[30].atr.AreaRuleId, Is.EqualTo(areaRules[10].Id));
        Assert.That(areaRuleTranslations[30].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[30].atr.Name, Is.EqualTo("11. Udstyr til drikkevand"));
        Assert.That(areaRuleTranslations[31].atr.AreaRuleId, Is.EqualTo(areaRules[10].Id));
        Assert.That(areaRuleTranslations[31].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[31].atr.Name, Is.EqualTo("11. Equipment for drinking water"));
        Assert.That(areaRuleTranslations[32].atr.AreaRuleId, Is.EqualTo(areaRules[10].Id));
        Assert.That(areaRuleTranslations[32].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[32].atr.Name, Is.EqualTo("11. Ausrüstung für Trinkwasser"));
        Assert.That(areaRuleTranslations[33].atr.AreaRuleId, Is.EqualTo(areaRules[11].Id));
        Assert.That(areaRuleTranslations[33].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[33].atr.Name, Is.EqualTo("12. Maskiner til udbringning af husdyrgødning samt doseringsmekanisme- eller dyse"));
        Assert.That(areaRuleTranslations[34].atr.AreaRuleId, Is.EqualTo(areaRules[11].Id));
        Assert.That(areaRuleTranslations[34].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[34].atr.Name, Is.EqualTo("12. Machines for spreading livestock manure and dosing mechanisms or nozzles"));
        Assert.That(areaRuleTranslations[35].atr.AreaRuleId, Is.EqualTo(areaRules[11].Id));
        Assert.That(areaRuleTranslations[35].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[35].atr.Name, Is.EqualTo("12. Maschinen zum Ausbringen von Viehmist und Dosiervorrichtungen oder Düsen"));
        Assert.That(areaRuleTranslations[36].atr.AreaRuleId, Is.EqualTo(areaRules[12].Id));
        Assert.That(areaRuleTranslations[36].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[36].atr.Name, Is.EqualTo("13. Miljøledelse gennemgået og revideret"));
        Assert.That(areaRuleTranslations[37].atr.AreaRuleId, Is.EqualTo(areaRules[12].Id));
        Assert.That(areaRuleTranslations[37].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[37].atr.Name, Is.EqualTo("13. Environmental management reviewed and revised"));
        Assert.That(areaRuleTranslations[38].atr.AreaRuleId, Is.EqualTo(areaRules[12].Id));
        Assert.That(areaRuleTranslations[38].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[38].atr.Name, Is.EqualTo("13. Umweltmanagement überprüft und überarbeitet"));
        Assert.That(areaRuleTranslations[39].atr.AreaRuleId, Is.EqualTo(areaRules[13].Id));
        Assert.That(areaRuleTranslations[39].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[39].atr.Name, Is.EqualTo("14. Beredskabsplan gennemgået og revideret"));
        Assert.That(areaRuleTranslations[40].atr.AreaRuleId, Is.EqualTo(areaRules[13].Id));
        Assert.That(areaRuleTranslations[40].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[40].atr.Name, Is.EqualTo("14. Contingency plan reviewed and revised"));
        Assert.That(areaRuleTranslations[41].atr.AreaRuleId, Is.EqualTo(areaRules[13].Id));
        Assert.That(areaRuleTranslations[41].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[41].atr.Name, Is.EqualTo("14. Notfallplan überprüft und überarbeitet"));

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
        Assert.That(areaRulePlannings[0].AreaRuleId, Is.EqualTo(areaRules[areaRuleNo].Id));
        Assert.That(areaRulePlannings[0].ItemPlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(areaRulePlannings[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRulePlannings[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRulePlannings[0].ComplianceEnabled, Is.EqualTo(true));
        Assert.That(areaRulePlannings[0].SendNotifications, Is.EqualTo(true));

        // Assert plannings
        Assert.That(plannings, Is.Not.Null);
        Assert.That(plannings.Count, Is.EqualTo(1));
        Assert.That(plannings[0].RelatedEFormId, Is.EqualTo(areaRules[areaRuleNo].EformId));
        Assert.That(plannings[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
        Assert.That(plannings[0].SdkFolderId, Is.EqualTo(areaRules[areaRuleNo].FolderId));
        Assert.That(plannings[0].LastExecutedTime, Is.Not.Null);
        Assert.That(plannings[0].LastExecutedTime, Is.EqualTo(new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 0,0,0)));
        var now = DateTime.UtcNow;
        var nextExecutionTime =
            new DateTime(now.Year, 4, 12);

        //nextExecutionTime = now.Month < 6 ? nextExecutionTime.AddMonths(6) : nextExecutionTime.AddYears(1);
        //
        // if (nextExecutionTime < now)
        // {
        //     nextExecutionTime = nextExecutionTime.AddMonths(2);
        // }
        nextExecutionTime = nextExecutionTime.AddMonths(6);

        Assert.That(plannings[0].NextExecutionTime, Is.EqualTo(nextExecutionTime));
        Assert.That(plannings[0].RepeatEvery, Is.EqualTo(6));
        Assert.That(plannings[0].RepeatType, Is.EqualTo(RepeatType.Month));

        // Assert planningNameTranslations
        Assert.That(planningNameTranslations, Is.Not.Null);
        Assert.That(planningNameTranslations.Count, Is.EqualTo(3));
        Assert.That(planningNameTranslations[0].Name, Is.EqualTo(danishTranslation));
        Assert.That(planningNameTranslations[0].LanguageId, Is.EqualTo(danishLanguage.Id));
        Assert.That(planningNameTranslations[0].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(planningNameTranslations[1].Name, Is.EqualTo(englishTranslation));
        Assert.That(planningNameTranslations[1].LanguageId, Is.EqualTo(englishLanguage.Id));
        Assert.That(planningNameTranslations[1].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(planningNameTranslations[2].Name, Is.EqualTo(germanTranslation));
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
        Assert.That(cases[0].CheckListId, Is.EqualTo(areaRules[areaRuleNo].EformId));
        Assert.That(cases[0].FolderId, Is.Null);
        Assert.That(cases[0].Status, Is.EqualTo(66));
        Assert.That(cases[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
    }

    // Should test the UpdatePlanning method for area rule "00. Logbøger" with repeat type "months" and repeat every "2"
    [Test]
    [TestCase(0, "01. Gyllekøling", "01. Slurry cooling", "01. Schlammkühlung")]
    [TestCase(1, "02. Forsuring", "02. Acidification", "02. Ansäuerung")]
    [TestCase(2, "03. Luftrensning", "03. Air purification", "03. Luftreinigung")]
    [TestCase(3, "04. Beholderkontrol gennemført", "04. Container control completed", "04. Behälterkontrolle abgeschlossen")]
    [TestCase(4, "05. Gyllebeholdere", "05. Slurry containers", "05. Güllebehälter")]
    [TestCase(5, "06. Gyllepumper, - miksere, - seperatorer og spredere", "06. Slurry pumps, - mixers, - separators and spreaders", "06. Schlammpumpen, - Mischer, - Separatoren und Verteiler")]
    [TestCase(6, "07. Forsyningssystemer til vand og foder", "07. Supply systems for water and feed", "07. Versorgungssysteme für Wasser und Futter")]
    [TestCase(7, "08. Varme-, køle- og ventilationssystemer samt temperaturfølere", "08. Heating, cooling and ventilation systems and temperature sensors", "08. Heizungs-, Kühl- und Lüftungssysteme und Temperatursensoren")]
    [TestCase(8, "09. Siloer og transportudstyr", "09. Silos and transport equipment", "09. Silos und Transportgeräte")]
    [TestCase(9, "10. Luftrensningssystemer", "10. Air purification systems", "10. Luftreinigungssysteme")]
    [TestCase(10, "11. Udstyr til drikkevand", "11. Equipment for drinking water", "11. Ausrüstung für Trinkwasser")]
    [TestCase(11, "12. Maskiner til udbringning af husdyrgødning samt doseringsmekanisme- eller dyse", "12. Machines for spreading livestock manure and dosing mechanisms or nozzles", "12. Maschinen zum Ausbringen von Viehmist und Dosiervorrichtungen oder Düsen")]
    [TestCase(12, "13. Miljøledelse gennemgået og revideret", "13. Environmental management reviewed and revised", "13. Umweltmanagement überprüft und überarbeitet")]
    [TestCase(13, "14. Beredskabsplan gennemgået og revideret", "14. Contingency plan reviewed and revised", "14. Notfallplan überprüft und überarbeitet")]
    public async Task UpdatePlanning_AreaRuleMonths12_ReturnsSuccess(int areaRuleNo, string danishTranslation, string englishTranslation, string germanTranslation)
    {
        // Arrange
        var core = await GetCore();
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
            RuleId = areaRules[areaRuleNo].Id,
            ComplianceEnabled = true,
            PropertyId = properties[0].Id,
            Status = true,
            SendNotifications = true,
            StartDate = new DateTime(2023, 4, 12, 0, 0,0),
            UseStartDateAsStartOfPeriod = true,
            TypeSpecificFields = new AreaRuleTypePlanningModel
            {
                Alarm = AreaRuleT2AlarmsEnum.No,
                DayOfMonth = 1,
                DayOfWeek = 0,
                HoursAndEnergyEnabled = false,
                RepeatEvery = 12,
                RepeatType = 3
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
        var englishLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "en-US");
        var germanLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "de-DE");
        var danishLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "da");

        // Assert result
        Assert.NotNull(result);
        Assert.That(result.Success, Is.EqualTo(true));

        // Assert areaRules
        Assert.NotNull(areaRules);
        Assert.That(areaRules.Count, Is.EqualTo(14));
        Assert.That(areaRules[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[1].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[2].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[3].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[4].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[5].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[6].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[7].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[8].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[9].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[10].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[11].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[12].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[13].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[1].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[2].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[3].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[4].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[5].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[6].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[7].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[8].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[9].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[10].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[11].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[12].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[0].EformName, Is.EqualTo("01. Gyllekøling"));
        Assert.That(areaRules[1].EformName, Is.EqualTo("02. Forsuring"));
        Assert.That(areaRules[2].EformName, Is.EqualTo("03. Luftrensning"));
        Assert.That(areaRules[3].EformName, Is.EqualTo("04. Beholderkontrol gennemført"));
        Assert.That(areaRules[4].EformName, Is.EqualTo("05. Gyllebeholdere"));
        Assert.That(areaRules[5].EformName, Is.EqualTo("06. Gyllepumper, - miksere, - seperatorer og spredere"));
        Assert.That(areaRules[6].EformName, Is.EqualTo("07. Forsyningssystemer til vand og foder"));
        Assert.That(areaRules[7].EformName, Is.EqualTo("08. Varme-, køle- og ventilationssystemer samt temperaturfølere"));
        Assert.That(areaRules[8].EformName, Is.EqualTo("09. Siloer og transportudstyr"));
        Assert.That(areaRules[9].EformName, Is.EqualTo("10. Luftrensningssystemer"));
        Assert.That(areaRules[10].EformName, Is.EqualTo("11. Udstyr til drikkevand"));
        Assert.That(areaRules[11].EformName, Is.EqualTo("12. Maskiner til udbringning af husdyrgødning samt doseringsmekanisme- eller dyse"));
        Assert.That(areaRules[12].EformName, Is.EqualTo("13. Miljøledelse"));
        Assert.That(areaRules[13].EformName, Is.EqualTo("14. Beredskabsplan"));

        // Assert areaRuleTranslations
        Assert.NotNull(areaRuleTranslations);
        Assert.That(areaRuleTranslations.Count, Is.EqualTo(42));
        Assert.That(areaRuleTranslations[0].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[0].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[0].atr.Name, Is.EqualTo("01. Gyllekøling"));
        Assert.That(areaRuleTranslations[1].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[1].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[1].atr.Name, Is.EqualTo("01. Slurry cooling"));
        Assert.That(areaRuleTranslations[2].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[2].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[2].atr.Name, Is.EqualTo("01. Schlammkühlung"));
        Assert.That(areaRuleTranslations[3].atr.AreaRuleId, Is.EqualTo(areaRules[1].Id));
        Assert.That(areaRuleTranslations[3].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[3].atr.Name, Is.EqualTo("02. Forsuring"));
        Assert.That(areaRuleTranslations[4].atr.AreaRuleId, Is.EqualTo(areaRules[1].Id));
        Assert.That(areaRuleTranslations[4].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[4].atr.Name, Is.EqualTo("02. Acidification"));
        Assert.That(areaRuleTranslations[5].atr.AreaRuleId, Is.EqualTo(areaRules[1].Id));
        Assert.That(areaRuleTranslations[5].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[5].atr.Name, Is.EqualTo("02. Ansäuerung"));
        Assert.That(areaRuleTranslations[6].atr.AreaRuleId, Is.EqualTo(areaRules[2].Id));
        Assert.That(areaRuleTranslations[6].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[6].atr.Name, Is.EqualTo("03. Luftrensning"));
        Assert.That(areaRuleTranslations[7].atr.AreaRuleId, Is.EqualTo(areaRules[2].Id));
        Assert.That(areaRuleTranslations[7].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[7].atr.Name, Is.EqualTo("03. Air purification"));
        Assert.That(areaRuleTranslations[8].atr.AreaRuleId, Is.EqualTo(areaRules[2].Id));
        Assert.That(areaRuleTranslations[8].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[8].atr.Name, Is.EqualTo("03. Luftreinigung"));
        Assert.That(areaRuleTranslations[9].atr.AreaRuleId, Is.EqualTo(areaRules[3].Id));
        Assert.That(areaRuleTranslations[9].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[9].atr.Name, Is.EqualTo("04. Beholderkontrol gennemført"));
        Assert.That(areaRuleTranslations[10].atr.AreaRuleId, Is.EqualTo(areaRules[3].Id));
        Assert.That(areaRuleTranslations[10].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[10].atr.Name, Is.EqualTo("04. Container control completed"));
        Assert.That(areaRuleTranslations[11].atr.AreaRuleId, Is.EqualTo(areaRules[3].Id));
        Assert.That(areaRuleTranslations[11].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[11].atr.Name, Is.EqualTo("04. Behälterkontrolle abgeschlossen"));
        Assert.That(areaRuleTranslations[12].atr.AreaRuleId, Is.EqualTo(areaRules[4].Id));
        Assert.That(areaRuleTranslations[12].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[12].atr.Name, Is.EqualTo("05. Gyllebeholdere"));
        Assert.That(areaRuleTranslations[13].atr.AreaRuleId, Is.EqualTo(areaRules[4].Id));
        Assert.That(areaRuleTranslations[13].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[13].atr.Name, Is.EqualTo("05. Slurry containers"));
        Assert.That(areaRuleTranslations[14].atr.AreaRuleId, Is.EqualTo(areaRules[4].Id));
        Assert.That(areaRuleTranslations[14].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[14].atr.Name, Is.EqualTo("05. Güllebehälter"));
        Assert.That(areaRuleTranslations[15].atr.AreaRuleId, Is.EqualTo(areaRules[5].Id));
        Assert.That(areaRuleTranslations[15].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[15].atr.Name, Is.EqualTo("06. Gyllepumper, - miksere, - seperatorer og spredere"));
        Assert.That(areaRuleTranslations[16].atr.AreaRuleId, Is.EqualTo(areaRules[5].Id));
        Assert.That(areaRuleTranslations[16].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[16].atr.Name, Is.EqualTo("06. Slurry pumps, - mixers, - separators and spreaders"));
        Assert.That(areaRuleTranslations[17].atr.AreaRuleId, Is.EqualTo(areaRules[5].Id));
        Assert.That(areaRuleTranslations[17].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[17].atr.Name, Is.EqualTo("06. Schlammpumpen, - Mischer, - Separatoren und Verteiler"));
        Assert.That(areaRuleTranslations[18].atr.AreaRuleId, Is.EqualTo(areaRules[6].Id));
        Assert.That(areaRuleTranslations[18].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[18].atr.Name, Is.EqualTo("07. Forsyningssystemer til vand og foder"));
        Assert.That(areaRuleTranslations[19].atr.AreaRuleId, Is.EqualTo(areaRules[6].Id));
        Assert.That(areaRuleTranslations[19].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[19].atr.Name, Is.EqualTo("07. Supply systems for water and feed"));
        Assert.That(areaRuleTranslations[20].atr.AreaRuleId, Is.EqualTo(areaRules[6].Id));
        Assert.That(areaRuleTranslations[20].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[20].atr.Name, Is.EqualTo("07. Versorgungssysteme für Wasser und Futter"));
        Assert.That(areaRuleTranslations[21].atr.AreaRuleId, Is.EqualTo(areaRules[7].Id));
        Assert.That(areaRuleTranslations[21].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[21].atr.Name, Is.EqualTo("08. Varme-, køle- og ventilationssystemer samt temperaturfølere"));
        Assert.That(areaRuleTranslations[22].atr.AreaRuleId, Is.EqualTo(areaRules[7].Id));
        Assert.That(areaRuleTranslations[22].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[22].atr.Name, Is.EqualTo("08. Heating, cooling and ventilation systems and temperature sensors"));
        Assert.That(areaRuleTranslations[23].atr.AreaRuleId, Is.EqualTo(areaRules[7].Id));
        Assert.That(areaRuleTranslations[23].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[23].atr.Name, Is.EqualTo("08. Heizungs-, Kühl- und Lüftungssysteme und Temperatursensoren"));
        Assert.That(areaRuleTranslations[24].atr.AreaRuleId, Is.EqualTo(areaRules[8].Id));
        Assert.That(areaRuleTranslations[24].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[24].atr.Name, Is.EqualTo("09. Siloer og transportudstyr"));
        Assert.That(areaRuleTranslations[25].atr.AreaRuleId, Is.EqualTo(areaRules[8].Id));
        Assert.That(areaRuleTranslations[25].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[25].atr.Name, Is.EqualTo("09. Silos and transport equipment"));
        Assert.That(areaRuleTranslations[26].atr.AreaRuleId, Is.EqualTo(areaRules[8].Id));
        Assert.That(areaRuleTranslations[26].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[26].atr.Name, Is.EqualTo("09. Silos und Transportgeräte"));
        Assert.That(areaRuleTranslations[27].atr.AreaRuleId, Is.EqualTo(areaRules[9].Id));
        Assert.That(areaRuleTranslations[27].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[27].atr.Name, Is.EqualTo("10. Luftrensningssystemer"));
        Assert.That(areaRuleTranslations[28].atr.AreaRuleId, Is.EqualTo(areaRules[9].Id));
        Assert.That(areaRuleTranslations[28].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[28].atr.Name, Is.EqualTo("10. Air purification systems"));
        Assert.That(areaRuleTranslations[29].atr.AreaRuleId, Is.EqualTo(areaRules[9].Id));
        Assert.That(areaRuleTranslations[29].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[29].atr.Name, Is.EqualTo("10. Luftreinigungssysteme"));
        Assert.That(areaRuleTranslations[30].atr.AreaRuleId, Is.EqualTo(areaRules[10].Id));
        Assert.That(areaRuleTranslations[30].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[30].atr.Name, Is.EqualTo("11. Udstyr til drikkevand"));
        Assert.That(areaRuleTranslations[31].atr.AreaRuleId, Is.EqualTo(areaRules[10].Id));
        Assert.That(areaRuleTranslations[31].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[31].atr.Name, Is.EqualTo("11. Equipment for drinking water"));
        Assert.That(areaRuleTranslations[32].atr.AreaRuleId, Is.EqualTo(areaRules[10].Id));
        Assert.That(areaRuleTranslations[32].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[32].atr.Name, Is.EqualTo("11. Ausrüstung für Trinkwasser"));
        Assert.That(areaRuleTranslations[33].atr.AreaRuleId, Is.EqualTo(areaRules[11].Id));
        Assert.That(areaRuleTranslations[33].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[33].atr.Name, Is.EqualTo("12. Maskiner til udbringning af husdyrgødning samt doseringsmekanisme- eller dyse"));
        Assert.That(areaRuleTranslations[34].atr.AreaRuleId, Is.EqualTo(areaRules[11].Id));
        Assert.That(areaRuleTranslations[34].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[34].atr.Name, Is.EqualTo("12. Machines for spreading livestock manure and dosing mechanisms or nozzles"));
        Assert.That(areaRuleTranslations[35].atr.AreaRuleId, Is.EqualTo(areaRules[11].Id));
        Assert.That(areaRuleTranslations[35].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[35].atr.Name, Is.EqualTo("12. Maschinen zum Ausbringen von Viehmist und Dosiervorrichtungen oder Düsen"));
        Assert.That(areaRuleTranslations[36].atr.AreaRuleId, Is.EqualTo(areaRules[12].Id));
        Assert.That(areaRuleTranslations[36].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[36].atr.Name, Is.EqualTo("13. Miljøledelse gennemgået og revideret"));
        Assert.That(areaRuleTranslations[37].atr.AreaRuleId, Is.EqualTo(areaRules[12].Id));
        Assert.That(areaRuleTranslations[37].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[37].atr.Name, Is.EqualTo("13. Environmental management reviewed and revised"));
        Assert.That(areaRuleTranslations[38].atr.AreaRuleId, Is.EqualTo(areaRules[12].Id));
        Assert.That(areaRuleTranslations[38].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[38].atr.Name, Is.EqualTo("13. Umweltmanagement überprüft und überarbeitet"));
        Assert.That(areaRuleTranslations[39].atr.AreaRuleId, Is.EqualTo(areaRules[13].Id));
        Assert.That(areaRuleTranslations[39].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[39].atr.Name, Is.EqualTo("14. Beredskabsplan gennemgået og revideret"));
        Assert.That(areaRuleTranslations[40].atr.AreaRuleId, Is.EqualTo(areaRules[13].Id));
        Assert.That(areaRuleTranslations[40].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[40].atr.Name, Is.EqualTo("14. Contingency plan reviewed and revised"));
        Assert.That(areaRuleTranslations[41].atr.AreaRuleId, Is.EqualTo(areaRules[13].Id));
        Assert.That(areaRuleTranslations[41].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[41].atr.Name, Is.EqualTo("14. Notfallplan überprüft und überarbeitet"));

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
        Assert.That(areaRulePlannings[0].AreaRuleId, Is.EqualTo(areaRules[areaRuleNo].Id));
        Assert.That(areaRulePlannings[0].ItemPlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(areaRulePlannings[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRulePlannings[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRulePlannings[0].ComplianceEnabled, Is.EqualTo(true));
        Assert.That(areaRulePlannings[0].SendNotifications, Is.EqualTo(true));

        // Assert plannings
        Assert.That(plannings, Is.Not.Null);
        Assert.That(plannings.Count, Is.EqualTo(1));
        Assert.That(plannings[0].RelatedEFormId, Is.EqualTo(areaRules[areaRuleNo].EformId));
        Assert.That(plannings[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
        Assert.That(plannings[0].SdkFolderId, Is.EqualTo(areaRules[areaRuleNo].FolderId));
        Assert.That(plannings[0].LastExecutedTime, Is.Not.Null);
        Assert.That(plannings[0].LastExecutedTime, Is.EqualTo(new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 0,0,0)));
        var now = DateTime.UtcNow;
        var nextExecutionTime =
            new DateTime(now.Year, 4, 12).AddYears(1);

        Assert.That(plannings[0].NextExecutionTime, Is.EqualTo(nextExecutionTime));
        Assert.That(plannings[0].RepeatEvery, Is.EqualTo(12));
        Assert.That(plannings[0].RepeatType, Is.EqualTo(RepeatType.Month));

        // Assert planningNameTranslations
        Assert.That(planningNameTranslations, Is.Not.Null);
        Assert.That(planningNameTranslations.Count, Is.EqualTo(3));
        Assert.That(planningNameTranslations[0].Name, Is.EqualTo(danishTranslation));
        Assert.That(planningNameTranslations[0].LanguageId, Is.EqualTo(danishLanguage.Id));
        Assert.That(planningNameTranslations[0].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(planningNameTranslations[1].Name, Is.EqualTo(englishTranslation));
        Assert.That(planningNameTranslations[1].LanguageId, Is.EqualTo(englishLanguage.Id));
        Assert.That(planningNameTranslations[1].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(planningNameTranslations[2].Name, Is.EqualTo(germanTranslation));
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
        Assert.That(cases[0].CheckListId, Is.EqualTo(areaRules[areaRuleNo].EformId));
        Assert.That(cases[0].FolderId, Is.Null);
        Assert.That(cases[0].Status, Is.EqualTo(66));
        Assert.That(cases[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
    }

    // Should test the UpdatePlanning method for area rule "00. Logbøger" with repeat type "months" and repeat every "2"
    [Test]
    [TestCase(0, "01. Gyllekøling", "01. Slurry cooling", "01. Schlammkühlung")]
    [TestCase(1, "02. Forsuring", "02. Acidification", "02. Ansäuerung")]
    [TestCase(2, "03. Luftrensning", "03. Air purification", "03. Luftreinigung")]
    [TestCase(3, "04. Beholderkontrol gennemført", "04. Container control completed", "04. Behälterkontrolle abgeschlossen")]
    [TestCase(4, "05. Gyllebeholdere", "05. Slurry containers", "05. Güllebehälter")]
    [TestCase(5, "06. Gyllepumper, - miksere, - seperatorer og spredere", "06. Slurry pumps, - mixers, - separators and spreaders", "06. Schlammpumpen, - Mischer, - Separatoren und Verteiler")]
    [TestCase(6, "07. Forsyningssystemer til vand og foder", "07. Supply systems for water and feed", "07. Versorgungssysteme für Wasser und Futter")]
    [TestCase(7, "08. Varme-, køle- og ventilationssystemer samt temperaturfølere", "08. Heating, cooling and ventilation systems and temperature sensors", "08. Heizungs-, Kühl- und Lüftungssysteme und Temperatursensoren")]
    [TestCase(8, "09. Siloer og transportudstyr", "09. Silos and transport equipment", "09. Silos und Transportgeräte")]
    [TestCase(9, "10. Luftrensningssystemer", "10. Air purification systems", "10. Luftreinigungssysteme")]
    [TestCase(10, "11. Udstyr til drikkevand", "11. Equipment for drinking water", "11. Ausrüstung für Trinkwasser")]
    [TestCase(11, "12. Maskiner til udbringning af husdyrgødning samt doseringsmekanisme- eller dyse", "12. Machines for spreading livestock manure and dosing mechanisms or nozzles", "12. Maschinen zum Ausbringen von Viehmist und Dosiervorrichtungen oder Düsen")]
    [TestCase(12, "13. Miljøledelse gennemgået og revideret", "13. Environmental management reviewed and revised", "13. Umweltmanagement überprüft und überarbeitet")]
    [TestCase(13, "14. Beredskabsplan gennemgået og revideret", "14. Contingency plan reviewed and revised", "14. Notfallplan überprüft und überarbeitet")]
    public async Task UpdatePlanning_AreaRuleMonths24_ReturnsSuccess(int areaRuleNo, string danishTranslation, string englishTranslation, string germanTranslation)
    {
        // Arrange
        var core = await GetCore();
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
            RuleId = areaRules[areaRuleNo].Id,
            ComplianceEnabled = true,
            PropertyId = properties[0].Id,
            Status = true,
            SendNotifications = true,
            StartDate = new DateTime(2023, 4, 12, 0, 0,0),
            UseStartDateAsStartOfPeriod = true,
            TypeSpecificFields = new AreaRuleTypePlanningModel
            {
                Alarm = AreaRuleT2AlarmsEnum.No,
                DayOfMonth = 1,
                DayOfWeek = 0,
                HoursAndEnergyEnabled = false,
                RepeatEvery = 24,
                RepeatType = 3
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
        var englishLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "en-US");
        var germanLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "de-DE");
        var danishLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "da");

        // Assert result
        Assert.NotNull(result);
        Assert.That(result.Success, Is.EqualTo(true));

        // Assert areaRules
        Assert.NotNull(areaRules);
        Assert.That(areaRules.Count, Is.EqualTo(14));
        Assert.That(areaRules[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[1].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[2].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[3].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[4].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[5].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[6].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[7].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[8].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[9].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[10].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[11].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[12].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[13].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[1].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[2].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[3].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[4].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[5].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[6].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[7].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[8].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[9].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[10].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[11].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[12].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[0].EformName, Is.EqualTo("01. Gyllekøling"));
        Assert.That(areaRules[1].EformName, Is.EqualTo("02. Forsuring"));
        Assert.That(areaRules[2].EformName, Is.EqualTo("03. Luftrensning"));
        Assert.That(areaRules[3].EformName, Is.EqualTo("04. Beholderkontrol gennemført"));
        Assert.That(areaRules[4].EformName, Is.EqualTo("05. Gyllebeholdere"));
        Assert.That(areaRules[5].EformName, Is.EqualTo("06. Gyllepumper, - miksere, - seperatorer og spredere"));
        Assert.That(areaRules[6].EformName, Is.EqualTo("07. Forsyningssystemer til vand og foder"));
        Assert.That(areaRules[7].EformName, Is.EqualTo("08. Varme-, køle- og ventilationssystemer samt temperaturfølere"));
        Assert.That(areaRules[8].EformName, Is.EqualTo("09. Siloer og transportudstyr"));
        Assert.That(areaRules[9].EformName, Is.EqualTo("10. Luftrensningssystemer"));
        Assert.That(areaRules[10].EformName, Is.EqualTo("11. Udstyr til drikkevand"));
        Assert.That(areaRules[11].EformName, Is.EqualTo("12. Maskiner til udbringning af husdyrgødning samt doseringsmekanisme- eller dyse"));
        Assert.That(areaRules[12].EformName, Is.EqualTo("13. Miljøledelse"));
        Assert.That(areaRules[13].EformName, Is.EqualTo("14. Beredskabsplan"));

        // Assert areaRuleTranslations
        Assert.NotNull(areaRuleTranslations);
        Assert.That(areaRuleTranslations.Count, Is.EqualTo(42));
        Assert.That(areaRuleTranslations[0].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[0].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[0].atr.Name, Is.EqualTo("01. Gyllekøling"));
        Assert.That(areaRuleTranslations[1].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[1].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[1].atr.Name, Is.EqualTo("01. Slurry cooling"));
        Assert.That(areaRuleTranslations[2].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[2].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[2].atr.Name, Is.EqualTo("01. Schlammkühlung"));
        Assert.That(areaRuleTranslations[3].atr.AreaRuleId, Is.EqualTo(areaRules[1].Id));
        Assert.That(areaRuleTranslations[3].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[3].atr.Name, Is.EqualTo("02. Forsuring"));
        Assert.That(areaRuleTranslations[4].atr.AreaRuleId, Is.EqualTo(areaRules[1].Id));
        Assert.That(areaRuleTranslations[4].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[4].atr.Name, Is.EqualTo("02. Acidification"));
        Assert.That(areaRuleTranslations[5].atr.AreaRuleId, Is.EqualTo(areaRules[1].Id));
        Assert.That(areaRuleTranslations[5].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[5].atr.Name, Is.EqualTo("02. Ansäuerung"));
        Assert.That(areaRuleTranslations[6].atr.AreaRuleId, Is.EqualTo(areaRules[2].Id));
        Assert.That(areaRuleTranslations[6].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[6].atr.Name, Is.EqualTo("03. Luftrensning"));
        Assert.That(areaRuleTranslations[7].atr.AreaRuleId, Is.EqualTo(areaRules[2].Id));
        Assert.That(areaRuleTranslations[7].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[7].atr.Name, Is.EqualTo("03. Air purification"));
        Assert.That(areaRuleTranslations[8].atr.AreaRuleId, Is.EqualTo(areaRules[2].Id));
        Assert.That(areaRuleTranslations[8].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[8].atr.Name, Is.EqualTo("03. Luftreinigung"));
        Assert.That(areaRuleTranslations[9].atr.AreaRuleId, Is.EqualTo(areaRules[3].Id));
        Assert.That(areaRuleTranslations[9].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[9].atr.Name, Is.EqualTo("04. Beholderkontrol gennemført"));
        Assert.That(areaRuleTranslations[10].atr.AreaRuleId, Is.EqualTo(areaRules[3].Id));
        Assert.That(areaRuleTranslations[10].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[10].atr.Name, Is.EqualTo("04. Container control completed"));
        Assert.That(areaRuleTranslations[11].atr.AreaRuleId, Is.EqualTo(areaRules[3].Id));
        Assert.That(areaRuleTranslations[11].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[11].atr.Name, Is.EqualTo("04. Behälterkontrolle abgeschlossen"));
        Assert.That(areaRuleTranslations[12].atr.AreaRuleId, Is.EqualTo(areaRules[4].Id));
        Assert.That(areaRuleTranslations[12].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[12].atr.Name, Is.EqualTo("05. Gyllebeholdere"));
        Assert.That(areaRuleTranslations[13].atr.AreaRuleId, Is.EqualTo(areaRules[4].Id));
        Assert.That(areaRuleTranslations[13].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[13].atr.Name, Is.EqualTo("05. Slurry containers"));
        Assert.That(areaRuleTranslations[14].atr.AreaRuleId, Is.EqualTo(areaRules[4].Id));
        Assert.That(areaRuleTranslations[14].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[14].atr.Name, Is.EqualTo("05. Güllebehälter"));
        Assert.That(areaRuleTranslations[15].atr.AreaRuleId, Is.EqualTo(areaRules[5].Id));
        Assert.That(areaRuleTranslations[15].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[15].atr.Name, Is.EqualTo("06. Gyllepumper, - miksere, - seperatorer og spredere"));
        Assert.That(areaRuleTranslations[16].atr.AreaRuleId, Is.EqualTo(areaRules[5].Id));
        Assert.That(areaRuleTranslations[16].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[16].atr.Name, Is.EqualTo("06. Slurry pumps, - mixers, - separators and spreaders"));
        Assert.That(areaRuleTranslations[17].atr.AreaRuleId, Is.EqualTo(areaRules[5].Id));
        Assert.That(areaRuleTranslations[17].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[17].atr.Name, Is.EqualTo("06. Schlammpumpen, - Mischer, - Separatoren und Verteiler"));
        Assert.That(areaRuleTranslations[18].atr.AreaRuleId, Is.EqualTo(areaRules[6].Id));
        Assert.That(areaRuleTranslations[18].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[18].atr.Name, Is.EqualTo("07. Forsyningssystemer til vand og foder"));
        Assert.That(areaRuleTranslations[19].atr.AreaRuleId, Is.EqualTo(areaRules[6].Id));
        Assert.That(areaRuleTranslations[19].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[19].atr.Name, Is.EqualTo("07. Supply systems for water and feed"));
        Assert.That(areaRuleTranslations[20].atr.AreaRuleId, Is.EqualTo(areaRules[6].Id));
        Assert.That(areaRuleTranslations[20].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[20].atr.Name, Is.EqualTo("07. Versorgungssysteme für Wasser und Futter"));
        Assert.That(areaRuleTranslations[21].atr.AreaRuleId, Is.EqualTo(areaRules[7].Id));
        Assert.That(areaRuleTranslations[21].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[21].atr.Name, Is.EqualTo("08. Varme-, køle- og ventilationssystemer samt temperaturfølere"));
        Assert.That(areaRuleTranslations[22].atr.AreaRuleId, Is.EqualTo(areaRules[7].Id));
        Assert.That(areaRuleTranslations[22].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[22].atr.Name, Is.EqualTo("08. Heating, cooling and ventilation systems and temperature sensors"));
        Assert.That(areaRuleTranslations[23].atr.AreaRuleId, Is.EqualTo(areaRules[7].Id));
        Assert.That(areaRuleTranslations[23].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[23].atr.Name, Is.EqualTo("08. Heizungs-, Kühl- und Lüftungssysteme und Temperatursensoren"));
        Assert.That(areaRuleTranslations[24].atr.AreaRuleId, Is.EqualTo(areaRules[8].Id));
        Assert.That(areaRuleTranslations[24].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[24].atr.Name, Is.EqualTo("09. Siloer og transportudstyr"));
        Assert.That(areaRuleTranslations[25].atr.AreaRuleId, Is.EqualTo(areaRules[8].Id));
        Assert.That(areaRuleTranslations[25].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[25].atr.Name, Is.EqualTo("09. Silos and transport equipment"));
        Assert.That(areaRuleTranslations[26].atr.AreaRuleId, Is.EqualTo(areaRules[8].Id));
        Assert.That(areaRuleTranslations[26].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[26].atr.Name, Is.EqualTo("09. Silos und Transportgeräte"));
        Assert.That(areaRuleTranslations[27].atr.AreaRuleId, Is.EqualTo(areaRules[9].Id));
        Assert.That(areaRuleTranslations[27].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[27].atr.Name, Is.EqualTo("10. Luftrensningssystemer"));
        Assert.That(areaRuleTranslations[28].atr.AreaRuleId, Is.EqualTo(areaRules[9].Id));
        Assert.That(areaRuleTranslations[28].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[28].atr.Name, Is.EqualTo("10. Air purification systems"));
        Assert.That(areaRuleTranslations[29].atr.AreaRuleId, Is.EqualTo(areaRules[9].Id));
        Assert.That(areaRuleTranslations[29].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[29].atr.Name, Is.EqualTo("10. Luftreinigungssysteme"));
        Assert.That(areaRuleTranslations[30].atr.AreaRuleId, Is.EqualTo(areaRules[10].Id));
        Assert.That(areaRuleTranslations[30].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[30].atr.Name, Is.EqualTo("11. Udstyr til drikkevand"));
        Assert.That(areaRuleTranslations[31].atr.AreaRuleId, Is.EqualTo(areaRules[10].Id));
        Assert.That(areaRuleTranslations[31].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[31].atr.Name, Is.EqualTo("11. Equipment for drinking water"));
        Assert.That(areaRuleTranslations[32].atr.AreaRuleId, Is.EqualTo(areaRules[10].Id));
        Assert.That(areaRuleTranslations[32].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[32].atr.Name, Is.EqualTo("11. Ausrüstung für Trinkwasser"));
        Assert.That(areaRuleTranslations[33].atr.AreaRuleId, Is.EqualTo(areaRules[11].Id));
        Assert.That(areaRuleTranslations[33].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[33].atr.Name, Is.EqualTo("12. Maskiner til udbringning af husdyrgødning samt doseringsmekanisme- eller dyse"));
        Assert.That(areaRuleTranslations[34].atr.AreaRuleId, Is.EqualTo(areaRules[11].Id));
        Assert.That(areaRuleTranslations[34].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[34].atr.Name, Is.EqualTo("12. Machines for spreading livestock manure and dosing mechanisms or nozzles"));
        Assert.That(areaRuleTranslations[35].atr.AreaRuleId, Is.EqualTo(areaRules[11].Id));
        Assert.That(areaRuleTranslations[35].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[35].atr.Name, Is.EqualTo("12. Maschinen zum Ausbringen von Viehmist und Dosiervorrichtungen oder Düsen"));
        Assert.That(areaRuleTranslations[36].atr.AreaRuleId, Is.EqualTo(areaRules[12].Id));
        Assert.That(areaRuleTranslations[36].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[36].atr.Name, Is.EqualTo("13. Miljøledelse gennemgået og revideret"));
        Assert.That(areaRuleTranslations[37].atr.AreaRuleId, Is.EqualTo(areaRules[12].Id));
        Assert.That(areaRuleTranslations[37].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[37].atr.Name, Is.EqualTo("13. Environmental management reviewed and revised"));
        Assert.That(areaRuleTranslations[38].atr.AreaRuleId, Is.EqualTo(areaRules[12].Id));
        Assert.That(areaRuleTranslations[38].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[38].atr.Name, Is.EqualTo("13. Umweltmanagement überprüft und überarbeitet"));
        Assert.That(areaRuleTranslations[39].atr.AreaRuleId, Is.EqualTo(areaRules[13].Id));
        Assert.That(areaRuleTranslations[39].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[39].atr.Name, Is.EqualTo("14. Beredskabsplan gennemgået og revideret"));
        Assert.That(areaRuleTranslations[40].atr.AreaRuleId, Is.EqualTo(areaRules[13].Id));
        Assert.That(areaRuleTranslations[40].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[40].atr.Name, Is.EqualTo("14. Contingency plan reviewed and revised"));
        Assert.That(areaRuleTranslations[41].atr.AreaRuleId, Is.EqualTo(areaRules[13].Id));
        Assert.That(areaRuleTranslations[41].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[41].atr.Name, Is.EqualTo("14. Notfallplan überprüft und überarbeitet"));

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
        Assert.That(areaRulePlannings[0].AreaRuleId, Is.EqualTo(areaRules[areaRuleNo].Id));
        Assert.That(areaRulePlannings[0].ItemPlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(areaRulePlannings[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRulePlannings[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRulePlannings[0].ComplianceEnabled, Is.EqualTo(true));
        Assert.That(areaRulePlannings[0].SendNotifications, Is.EqualTo(true));

        // Assert plannings
        Assert.That(plannings, Is.Not.Null);
        Assert.That(plannings.Count, Is.EqualTo(1));
        Assert.That(plannings[0].RelatedEFormId, Is.EqualTo(areaRules[areaRuleNo].EformId));
        Assert.That(plannings[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
        Assert.That(plannings[0].SdkFolderId, Is.EqualTo(areaRules[areaRuleNo].FolderId));
        Assert.That(plannings[0].LastExecutedTime, Is.Not.Null);
        Assert.That(plannings[0].LastExecutedTime, Is.EqualTo(new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 0,0,0)));
        var now = DateTime.UtcNow;
        var nextExecutionTime =
            new DateTime(now.Year, 4, 12).AddYears(2);

        Assert.That(plannings[0].NextExecutionTime, Is.EqualTo(nextExecutionTime));
        Assert.That(plannings[0].RepeatEvery, Is.EqualTo(24));
        Assert.That(plannings[0].RepeatType, Is.EqualTo(RepeatType.Month));

        // Assert planningNameTranslations
        Assert.That(planningNameTranslations, Is.Not.Null);
        Assert.That(planningNameTranslations.Count, Is.EqualTo(3));
        Assert.That(planningNameTranslations[0].Name, Is.EqualTo(danishTranslation));
        Assert.That(planningNameTranslations[0].LanguageId, Is.EqualTo(danishLanguage.Id));
        Assert.That(planningNameTranslations[0].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(planningNameTranslations[1].Name, Is.EqualTo(englishTranslation));
        Assert.That(planningNameTranslations[1].LanguageId, Is.EqualTo(englishLanguage.Id));
        Assert.That(planningNameTranslations[1].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(planningNameTranslations[2].Name, Is.EqualTo(germanTranslation));
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
        Assert.That(cases[0].CheckListId, Is.EqualTo(areaRules[areaRuleNo].EformId));
        Assert.That(cases[0].FolderId, Is.Null);
        Assert.That(cases[0].Status, Is.EqualTo(66));
        Assert.That(cases[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
    }

    // Should test the UpdatePlanning method for area rule "00. Logbøger" with repeat type "months" and repeat every "2"
    [Test]
    [TestCase(0, "01. Gyllekøling", "01. Slurry cooling", "01. Schlammkühlung")]
    [TestCase(1, "02. Forsuring", "02. Acidification", "02. Ansäuerung")]
    [TestCase(2, "03. Luftrensning", "03. Air purification", "03. Luftreinigung")]
    [TestCase(3, "04. Beholderkontrol gennemført", "04. Container control completed", "04. Behälterkontrolle abgeschlossen")]
    [TestCase(4, "05. Gyllebeholdere", "05. Slurry containers", "05. Güllebehälter")]
    [TestCase(5, "06. Gyllepumper, - miksere, - seperatorer og spredere", "06. Slurry pumps, - mixers, - separators and spreaders", "06. Schlammpumpen, - Mischer, - Separatoren und Verteiler")]
    [TestCase(6, "07. Forsyningssystemer til vand og foder", "07. Supply systems for water and feed", "07. Versorgungssysteme für Wasser und Futter")]
    [TestCase(7, "08. Varme-, køle- og ventilationssystemer samt temperaturfølere", "08. Heating, cooling and ventilation systems and temperature sensors", "08. Heizungs-, Kühl- und Lüftungssysteme und Temperatursensoren")]
    [TestCase(8, "09. Siloer og transportudstyr", "09. Silos and transport equipment", "09. Silos und Transportgeräte")]
    [TestCase(9, "10. Luftrensningssystemer", "10. Air purification systems", "10. Luftreinigungssysteme")]
    [TestCase(10, "11. Udstyr til drikkevand", "11. Equipment for drinking water", "11. Ausrüstung für Trinkwasser")]
    [TestCase(11, "12. Maskiner til udbringning af husdyrgødning samt doseringsmekanisme- eller dyse", "12. Machines for spreading livestock manure and dosing mechanisms or nozzles", "12. Maschinen zum Ausbringen von Viehmist und Dosiervorrichtungen oder Düsen")]
    [TestCase(12, "13. Miljøledelse gennemgået og revideret", "13. Environmental management reviewed and revised", "13. Umweltmanagement überprüft und überarbeitet")]
    [TestCase(13, "14. Beredskabsplan gennemgået og revideret", "14. Contingency plan reviewed and revised", "14. Notfallplan überprüft und überarbeitet")]
    public async Task UpdatePlanning_AreaRuleMonths36_ReturnsSuccess(int areaRuleNo, string danishTranslation, string englishTranslation, string germanTranslation)
    {
        // Arrange
        var core = await GetCore();
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
            RuleId = areaRules[areaRuleNo].Id,
            ComplianceEnabled = true,
            PropertyId = properties[0].Id,
            Status = true,
            SendNotifications = true,
            StartDate = new DateTime(2023, 4, 12, 0, 0,0),
            UseStartDateAsStartOfPeriod = true,
            TypeSpecificFields = new AreaRuleTypePlanningModel
            {
                Alarm = AreaRuleT2AlarmsEnum.No,
                DayOfMonth = 1,
                DayOfWeek = 0,
                HoursAndEnergyEnabled = false,
                RepeatEvery = 36,
                RepeatType = 3
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
        var englishLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "en-US");
        var germanLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "de-DE");
        var danishLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "da");

        // Assert result
        Assert.NotNull(result);
        Assert.That(result.Success, Is.EqualTo(true));

        // Assert areaRules
        Assert.NotNull(areaRules);
        Assert.That(areaRules.Count, Is.EqualTo(14));
        Assert.That(areaRules[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[1].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[2].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[3].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[4].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[5].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[6].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[7].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[8].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[9].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[10].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[11].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[12].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[13].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[1].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[2].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[3].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[4].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[5].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[6].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[7].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[8].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[9].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[10].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[11].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[12].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[0].EformName, Is.EqualTo("01. Gyllekøling"));
        Assert.That(areaRules[1].EformName, Is.EqualTo("02. Forsuring"));
        Assert.That(areaRules[2].EformName, Is.EqualTo("03. Luftrensning"));
        Assert.That(areaRules[3].EformName, Is.EqualTo("04. Beholderkontrol gennemført"));
        Assert.That(areaRules[4].EformName, Is.EqualTo("05. Gyllebeholdere"));
        Assert.That(areaRules[5].EformName, Is.EqualTo("06. Gyllepumper, - miksere, - seperatorer og spredere"));
        Assert.That(areaRules[6].EformName, Is.EqualTo("07. Forsyningssystemer til vand og foder"));
        Assert.That(areaRules[7].EformName, Is.EqualTo("08. Varme-, køle- og ventilationssystemer samt temperaturfølere"));
        Assert.That(areaRules[8].EformName, Is.EqualTo("09. Siloer og transportudstyr"));
        Assert.That(areaRules[9].EformName, Is.EqualTo("10. Luftrensningssystemer"));
        Assert.That(areaRules[10].EformName, Is.EqualTo("11. Udstyr til drikkevand"));
        Assert.That(areaRules[11].EformName, Is.EqualTo("12. Maskiner til udbringning af husdyrgødning samt doseringsmekanisme- eller dyse"));
        Assert.That(areaRules[12].EformName, Is.EqualTo("13. Miljøledelse"));
        Assert.That(areaRules[13].EformName, Is.EqualTo("14. Beredskabsplan"));

        // Assert areaRuleTranslations
        Assert.NotNull(areaRuleTranslations);
        Assert.That(areaRuleTranslations.Count, Is.EqualTo(42));
        Assert.That(areaRuleTranslations[0].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[0].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[0].atr.Name, Is.EqualTo("01. Gyllekøling"));
        Assert.That(areaRuleTranslations[1].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[1].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[1].atr.Name, Is.EqualTo("01. Slurry cooling"));
        Assert.That(areaRuleTranslations[2].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[2].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[2].atr.Name, Is.EqualTo("01. Schlammkühlung"));
        Assert.That(areaRuleTranslations[3].atr.AreaRuleId, Is.EqualTo(areaRules[1].Id));
        Assert.That(areaRuleTranslations[3].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[3].atr.Name, Is.EqualTo("02. Forsuring"));
        Assert.That(areaRuleTranslations[4].atr.AreaRuleId, Is.EqualTo(areaRules[1].Id));
        Assert.That(areaRuleTranslations[4].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[4].atr.Name, Is.EqualTo("02. Acidification"));
        Assert.That(areaRuleTranslations[5].atr.AreaRuleId, Is.EqualTo(areaRules[1].Id));
        Assert.That(areaRuleTranslations[5].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[5].atr.Name, Is.EqualTo("02. Ansäuerung"));
        Assert.That(areaRuleTranslations[6].atr.AreaRuleId, Is.EqualTo(areaRules[2].Id));
        Assert.That(areaRuleTranslations[6].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[6].atr.Name, Is.EqualTo("03. Luftrensning"));
        Assert.That(areaRuleTranslations[7].atr.AreaRuleId, Is.EqualTo(areaRules[2].Id));
        Assert.That(areaRuleTranslations[7].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[7].atr.Name, Is.EqualTo("03. Air purification"));
        Assert.That(areaRuleTranslations[8].atr.AreaRuleId, Is.EqualTo(areaRules[2].Id));
        Assert.That(areaRuleTranslations[8].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[8].atr.Name, Is.EqualTo("03. Luftreinigung"));
        Assert.That(areaRuleTranslations[9].atr.AreaRuleId, Is.EqualTo(areaRules[3].Id));
        Assert.That(areaRuleTranslations[9].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[9].atr.Name, Is.EqualTo("04. Beholderkontrol gennemført"));
        Assert.That(areaRuleTranslations[10].atr.AreaRuleId, Is.EqualTo(areaRules[3].Id));
        Assert.That(areaRuleTranslations[10].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[10].atr.Name, Is.EqualTo("04. Container control completed"));
        Assert.That(areaRuleTranslations[11].atr.AreaRuleId, Is.EqualTo(areaRules[3].Id));
        Assert.That(areaRuleTranslations[11].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[11].atr.Name, Is.EqualTo("04. Behälterkontrolle abgeschlossen"));
        Assert.That(areaRuleTranslations[12].atr.AreaRuleId, Is.EqualTo(areaRules[4].Id));
        Assert.That(areaRuleTranslations[12].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[12].atr.Name, Is.EqualTo("05. Gyllebeholdere"));
        Assert.That(areaRuleTranslations[13].atr.AreaRuleId, Is.EqualTo(areaRules[4].Id));
        Assert.That(areaRuleTranslations[13].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[13].atr.Name, Is.EqualTo("05. Slurry containers"));
        Assert.That(areaRuleTranslations[14].atr.AreaRuleId, Is.EqualTo(areaRules[4].Id));
        Assert.That(areaRuleTranslations[14].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[14].atr.Name, Is.EqualTo("05. Güllebehälter"));
        Assert.That(areaRuleTranslations[15].atr.AreaRuleId, Is.EqualTo(areaRules[5].Id));
        Assert.That(areaRuleTranslations[15].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[15].atr.Name, Is.EqualTo("06. Gyllepumper, - miksere, - seperatorer og spredere"));
        Assert.That(areaRuleTranslations[16].atr.AreaRuleId, Is.EqualTo(areaRules[5].Id));
        Assert.That(areaRuleTranslations[16].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[16].atr.Name, Is.EqualTo("06. Slurry pumps, - mixers, - separators and spreaders"));
        Assert.That(areaRuleTranslations[17].atr.AreaRuleId, Is.EqualTo(areaRules[5].Id));
        Assert.That(areaRuleTranslations[17].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[17].atr.Name, Is.EqualTo("06. Schlammpumpen, - Mischer, - Separatoren und Verteiler"));
        Assert.That(areaRuleTranslations[18].atr.AreaRuleId, Is.EqualTo(areaRules[6].Id));
        Assert.That(areaRuleTranslations[18].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[18].atr.Name, Is.EqualTo("07. Forsyningssystemer til vand og foder"));
        Assert.That(areaRuleTranslations[19].atr.AreaRuleId, Is.EqualTo(areaRules[6].Id));
        Assert.That(areaRuleTranslations[19].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[19].atr.Name, Is.EqualTo("07. Supply systems for water and feed"));
        Assert.That(areaRuleTranslations[20].atr.AreaRuleId, Is.EqualTo(areaRules[6].Id));
        Assert.That(areaRuleTranslations[20].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[20].atr.Name, Is.EqualTo("07. Versorgungssysteme für Wasser und Futter"));
        Assert.That(areaRuleTranslations[21].atr.AreaRuleId, Is.EqualTo(areaRules[7].Id));
        Assert.That(areaRuleTranslations[21].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[21].atr.Name, Is.EqualTo("08. Varme-, køle- og ventilationssystemer samt temperaturfølere"));
        Assert.That(areaRuleTranslations[22].atr.AreaRuleId, Is.EqualTo(areaRules[7].Id));
        Assert.That(areaRuleTranslations[22].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[22].atr.Name, Is.EqualTo("08. Heating, cooling and ventilation systems and temperature sensors"));
        Assert.That(areaRuleTranslations[23].atr.AreaRuleId, Is.EqualTo(areaRules[7].Id));
        Assert.That(areaRuleTranslations[23].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[23].atr.Name, Is.EqualTo("08. Heizungs-, Kühl- und Lüftungssysteme und Temperatursensoren"));
        Assert.That(areaRuleTranslations[24].atr.AreaRuleId, Is.EqualTo(areaRules[8].Id));
        Assert.That(areaRuleTranslations[24].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[24].atr.Name, Is.EqualTo("09. Siloer og transportudstyr"));
        Assert.That(areaRuleTranslations[25].atr.AreaRuleId, Is.EqualTo(areaRules[8].Id));
        Assert.That(areaRuleTranslations[25].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[25].atr.Name, Is.EqualTo("09. Silos and transport equipment"));
        Assert.That(areaRuleTranslations[26].atr.AreaRuleId, Is.EqualTo(areaRules[8].Id));
        Assert.That(areaRuleTranslations[26].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[26].atr.Name, Is.EqualTo("09. Silos und Transportgeräte"));
        Assert.That(areaRuleTranslations[27].atr.AreaRuleId, Is.EqualTo(areaRules[9].Id));
        Assert.That(areaRuleTranslations[27].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[27].atr.Name, Is.EqualTo("10. Luftrensningssystemer"));
        Assert.That(areaRuleTranslations[28].atr.AreaRuleId, Is.EqualTo(areaRules[9].Id));
        Assert.That(areaRuleTranslations[28].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[28].atr.Name, Is.EqualTo("10. Air purification systems"));
        Assert.That(areaRuleTranslations[29].atr.AreaRuleId, Is.EqualTo(areaRules[9].Id));
        Assert.That(areaRuleTranslations[29].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[29].atr.Name, Is.EqualTo("10. Luftreinigungssysteme"));
        Assert.That(areaRuleTranslations[30].atr.AreaRuleId, Is.EqualTo(areaRules[10].Id));
        Assert.That(areaRuleTranslations[30].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[30].atr.Name, Is.EqualTo("11. Udstyr til drikkevand"));
        Assert.That(areaRuleTranslations[31].atr.AreaRuleId, Is.EqualTo(areaRules[10].Id));
        Assert.That(areaRuleTranslations[31].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[31].atr.Name, Is.EqualTo("11. Equipment for drinking water"));
        Assert.That(areaRuleTranslations[32].atr.AreaRuleId, Is.EqualTo(areaRules[10].Id));
        Assert.That(areaRuleTranslations[32].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[32].atr.Name, Is.EqualTo("11. Ausrüstung für Trinkwasser"));
        Assert.That(areaRuleTranslations[33].atr.AreaRuleId, Is.EqualTo(areaRules[11].Id));
        Assert.That(areaRuleTranslations[33].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[33].atr.Name, Is.EqualTo("12. Maskiner til udbringning af husdyrgødning samt doseringsmekanisme- eller dyse"));
        Assert.That(areaRuleTranslations[34].atr.AreaRuleId, Is.EqualTo(areaRules[11].Id));
        Assert.That(areaRuleTranslations[34].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[34].atr.Name, Is.EqualTo("12. Machines for spreading livestock manure and dosing mechanisms or nozzles"));
        Assert.That(areaRuleTranslations[35].atr.AreaRuleId, Is.EqualTo(areaRules[11].Id));
        Assert.That(areaRuleTranslations[35].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[35].atr.Name, Is.EqualTo("12. Maschinen zum Ausbringen von Viehmist und Dosiervorrichtungen oder Düsen"));
        Assert.That(areaRuleTranslations[36].atr.AreaRuleId, Is.EqualTo(areaRules[12].Id));
        Assert.That(areaRuleTranslations[36].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[36].atr.Name, Is.EqualTo("13. Miljøledelse gennemgået og revideret"));
        Assert.That(areaRuleTranslations[37].atr.AreaRuleId, Is.EqualTo(areaRules[12].Id));
        Assert.That(areaRuleTranslations[37].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[37].atr.Name, Is.EqualTo("13. Environmental management reviewed and revised"));
        Assert.That(areaRuleTranslations[38].atr.AreaRuleId, Is.EqualTo(areaRules[12].Id));
        Assert.That(areaRuleTranslations[38].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[38].atr.Name, Is.EqualTo("13. Umweltmanagement überprüft und überarbeitet"));
        Assert.That(areaRuleTranslations[39].atr.AreaRuleId, Is.EqualTo(areaRules[13].Id));
        Assert.That(areaRuleTranslations[39].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[39].atr.Name, Is.EqualTo("14. Beredskabsplan gennemgået og revideret"));
        Assert.That(areaRuleTranslations[40].atr.AreaRuleId, Is.EqualTo(areaRules[13].Id));
        Assert.That(areaRuleTranslations[40].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[40].atr.Name, Is.EqualTo("14. Contingency plan reviewed and revised"));
        Assert.That(areaRuleTranslations[41].atr.AreaRuleId, Is.EqualTo(areaRules[13].Id));
        Assert.That(areaRuleTranslations[41].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[41].atr.Name, Is.EqualTo("14. Notfallplan überprüft und überarbeitet"));

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
        Assert.That(areaRulePlannings[0].AreaRuleId, Is.EqualTo(areaRules[areaRuleNo].Id));
        Assert.That(areaRulePlannings[0].ItemPlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(areaRulePlannings[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRulePlannings[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRulePlannings[0].ComplianceEnabled, Is.EqualTo(true));
        Assert.That(areaRulePlannings[0].SendNotifications, Is.EqualTo(true));

        // Assert plannings
        Assert.That(plannings, Is.Not.Null);
        Assert.That(plannings.Count, Is.EqualTo(1));
        Assert.That(plannings[0].RelatedEFormId, Is.EqualTo(areaRules[areaRuleNo].EformId));
        Assert.That(plannings[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
        Assert.That(plannings[0].SdkFolderId, Is.EqualTo(areaRules[areaRuleNo].FolderId));
        Assert.That(plannings[0].LastExecutedTime, Is.Not.Null);
        Assert.That(plannings[0].LastExecutedTime, Is.EqualTo(new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 0,0,0)));
        var now = DateTime.UtcNow;
        var nextExecutionTime =
            new DateTime(now.Year, 4, 12).AddYears(3);

        Assert.That(plannings[0].NextExecutionTime, Is.EqualTo(nextExecutionTime));
        Assert.That(plannings[0].RepeatEvery, Is.EqualTo(36));
        Assert.That(plannings[0].RepeatType, Is.EqualTo(RepeatType.Month));

        // Assert planningNameTranslations
        Assert.That(planningNameTranslations, Is.Not.Null);
        Assert.That(planningNameTranslations.Count, Is.EqualTo(3));
        Assert.That(planningNameTranslations[0].Name, Is.EqualTo(danishTranslation));
        Assert.That(planningNameTranslations[0].LanguageId, Is.EqualTo(danishLanguage.Id));
        Assert.That(planningNameTranslations[0].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(planningNameTranslations[1].Name, Is.EqualTo(englishTranslation));
        Assert.That(planningNameTranslations[1].LanguageId, Is.EqualTo(englishLanguage.Id));
        Assert.That(planningNameTranslations[1].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(planningNameTranslations[2].Name, Is.EqualTo(germanTranslation));
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
        Assert.That(cases[0].CheckListId, Is.EqualTo(areaRules[areaRuleNo].EformId));
        Assert.That(cases[0].FolderId, Is.Null);
        Assert.That(cases[0].Status, Is.EqualTo(66));
        Assert.That(cases[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
    }

    // Should test the UpdatePlanning method for area rule "00. Logbøger" with repeat type "months" and repeat every "2"
    [Test]
    [TestCase(0, "01. Gyllekøling", "01. Slurry cooling", "01. Schlammkühlung")]
    [TestCase(1, "02. Forsuring", "02. Acidification", "02. Ansäuerung")]
    [TestCase(2, "03. Luftrensning", "03. Air purification", "03. Luftreinigung")]
    [TestCase(3, "04. Beholderkontrol gennemført", "04. Container control completed", "04. Behälterkontrolle abgeschlossen")]
    [TestCase(4, "05. Gyllebeholdere", "05. Slurry containers", "05. Güllebehälter")]
    [TestCase(5, "06. Gyllepumper, - miksere, - seperatorer og spredere", "06. Slurry pumps, - mixers, - separators and spreaders", "06. Schlammpumpen, - Mischer, - Separatoren und Verteiler")]
    [TestCase(6, "07. Forsyningssystemer til vand og foder", "07. Supply systems for water and feed", "07. Versorgungssysteme für Wasser und Futter")]
    [TestCase(7, "08. Varme-, køle- og ventilationssystemer samt temperaturfølere", "08. Heating, cooling and ventilation systems and temperature sensors", "08. Heizungs-, Kühl- und Lüftungssysteme und Temperatursensoren")]
    [TestCase(8, "09. Siloer og transportudstyr", "09. Silos and transport equipment", "09. Silos und Transportgeräte")]
    [TestCase(9, "10. Luftrensningssystemer", "10. Air purification systems", "10. Luftreinigungssysteme")]
    [TestCase(10, "11. Udstyr til drikkevand", "11. Equipment for drinking water", "11. Ausrüstung für Trinkwasser")]
    [TestCase(11, "12. Maskiner til udbringning af husdyrgødning samt doseringsmekanisme- eller dyse", "12. Machines for spreading livestock manure and dosing mechanisms or nozzles", "12. Maschinen zum Ausbringen von Viehmist und Dosiervorrichtungen oder Düsen")]
    [TestCase(12, "13. Miljøledelse gennemgået og revideret", "13. Environmental management reviewed and revised", "13. Umweltmanagement überprüft und überarbeitet")]
    [TestCase(13, "14. Beredskabsplan gennemgået og revideret", "14. Contingency plan reviewed and revised", "14. Notfallplan überprüft und überarbeitet")]
    public async Task UpdatePlanning_AreaRuleMonths48_ReturnsSuccess(int areaRuleNo, string danishTranslation, string englishTranslation, string germanTranslation)
    {
        // Arrange
        var core = await GetCore();
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
            RuleId = areaRules[areaRuleNo].Id,
            ComplianceEnabled = true,
            PropertyId = properties[0].Id,
            Status = true,
            SendNotifications = true,
            StartDate = new DateTime(2023, 4, 12, 0, 0,0),
            UseStartDateAsStartOfPeriod = true,
            TypeSpecificFields = new AreaRuleTypePlanningModel
            {
                Alarm = AreaRuleT2AlarmsEnum.No,
                DayOfMonth = 1,
                DayOfWeek = 0,
                HoursAndEnergyEnabled = false,
                RepeatEvery = 48,
                RepeatType = 3
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
        var englishLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "en-US");
        var germanLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "de-DE");
        var danishLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "da");

        // Assert result
        Assert.NotNull(result);
        Assert.That(result.Success, Is.EqualTo(true));

        // Assert areaRules
        Assert.NotNull(areaRules);
        Assert.That(areaRules.Count, Is.EqualTo(14));
        Assert.That(areaRules[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[1].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[2].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[3].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[4].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[5].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[6].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[7].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[8].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[9].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[10].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[11].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[12].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[13].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[1].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[2].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[3].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[4].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[5].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[6].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[7].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[8].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[9].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[10].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[11].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[12].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[0].EformName, Is.EqualTo("01. Gyllekøling"));
        Assert.That(areaRules[1].EformName, Is.EqualTo("02. Forsuring"));
        Assert.That(areaRules[2].EformName, Is.EqualTo("03. Luftrensning"));
        Assert.That(areaRules[3].EformName, Is.EqualTo("04. Beholderkontrol gennemført"));
        Assert.That(areaRules[4].EformName, Is.EqualTo("05. Gyllebeholdere"));
        Assert.That(areaRules[5].EformName, Is.EqualTo("06. Gyllepumper, - miksere, - seperatorer og spredere"));
        Assert.That(areaRules[6].EformName, Is.EqualTo("07. Forsyningssystemer til vand og foder"));
        Assert.That(areaRules[7].EformName, Is.EqualTo("08. Varme-, køle- og ventilationssystemer samt temperaturfølere"));
        Assert.That(areaRules[8].EformName, Is.EqualTo("09. Siloer og transportudstyr"));
        Assert.That(areaRules[9].EformName, Is.EqualTo("10. Luftrensningssystemer"));
        Assert.That(areaRules[10].EformName, Is.EqualTo("11. Udstyr til drikkevand"));
        Assert.That(areaRules[11].EformName, Is.EqualTo("12. Maskiner til udbringning af husdyrgødning samt doseringsmekanisme- eller dyse"));
        Assert.That(areaRules[12].EformName, Is.EqualTo("13. Miljøledelse"));
        Assert.That(areaRules[13].EformName, Is.EqualTo("14. Beredskabsplan"));

        // Assert areaRuleTranslations
        Assert.NotNull(areaRuleTranslations);
        Assert.That(areaRuleTranslations.Count, Is.EqualTo(42));
        Assert.That(areaRuleTranslations[0].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[0].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[0].atr.Name, Is.EqualTo("01. Gyllekøling"));
        Assert.That(areaRuleTranslations[1].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[1].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[1].atr.Name, Is.EqualTo("01. Slurry cooling"));
        Assert.That(areaRuleTranslations[2].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[2].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[2].atr.Name, Is.EqualTo("01. Schlammkühlung"));
        Assert.That(areaRuleTranslations[3].atr.AreaRuleId, Is.EqualTo(areaRules[1].Id));
        Assert.That(areaRuleTranslations[3].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[3].atr.Name, Is.EqualTo("02. Forsuring"));
        Assert.That(areaRuleTranslations[4].atr.AreaRuleId, Is.EqualTo(areaRules[1].Id));
        Assert.That(areaRuleTranslations[4].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[4].atr.Name, Is.EqualTo("02. Acidification"));
        Assert.That(areaRuleTranslations[5].atr.AreaRuleId, Is.EqualTo(areaRules[1].Id));
        Assert.That(areaRuleTranslations[5].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[5].atr.Name, Is.EqualTo("02. Ansäuerung"));
        Assert.That(areaRuleTranslations[6].atr.AreaRuleId, Is.EqualTo(areaRules[2].Id));
        Assert.That(areaRuleTranslations[6].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[6].atr.Name, Is.EqualTo("03. Luftrensning"));
        Assert.That(areaRuleTranslations[7].atr.AreaRuleId, Is.EqualTo(areaRules[2].Id));
        Assert.That(areaRuleTranslations[7].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[7].atr.Name, Is.EqualTo("03. Air purification"));
        Assert.That(areaRuleTranslations[8].atr.AreaRuleId, Is.EqualTo(areaRules[2].Id));
        Assert.That(areaRuleTranslations[8].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[8].atr.Name, Is.EqualTo("03. Luftreinigung"));
        Assert.That(areaRuleTranslations[9].atr.AreaRuleId, Is.EqualTo(areaRules[3].Id));
        Assert.That(areaRuleTranslations[9].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[9].atr.Name, Is.EqualTo("04. Beholderkontrol gennemført"));
        Assert.That(areaRuleTranslations[10].atr.AreaRuleId, Is.EqualTo(areaRules[3].Id));
        Assert.That(areaRuleTranslations[10].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[10].atr.Name, Is.EqualTo("04. Container control completed"));
        Assert.That(areaRuleTranslations[11].atr.AreaRuleId, Is.EqualTo(areaRules[3].Id));
        Assert.That(areaRuleTranslations[11].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[11].atr.Name, Is.EqualTo("04. Behälterkontrolle abgeschlossen"));
        Assert.That(areaRuleTranslations[12].atr.AreaRuleId, Is.EqualTo(areaRules[4].Id));
        Assert.That(areaRuleTranslations[12].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[12].atr.Name, Is.EqualTo("05. Gyllebeholdere"));
        Assert.That(areaRuleTranslations[13].atr.AreaRuleId, Is.EqualTo(areaRules[4].Id));
        Assert.That(areaRuleTranslations[13].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[13].atr.Name, Is.EqualTo("05. Slurry containers"));
        Assert.That(areaRuleTranslations[14].atr.AreaRuleId, Is.EqualTo(areaRules[4].Id));
        Assert.That(areaRuleTranslations[14].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[14].atr.Name, Is.EqualTo("05. Güllebehälter"));
        Assert.That(areaRuleTranslations[15].atr.AreaRuleId, Is.EqualTo(areaRules[5].Id));
        Assert.That(areaRuleTranslations[15].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[15].atr.Name, Is.EqualTo("06. Gyllepumper, - miksere, - seperatorer og spredere"));
        Assert.That(areaRuleTranslations[16].atr.AreaRuleId, Is.EqualTo(areaRules[5].Id));
        Assert.That(areaRuleTranslations[16].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[16].atr.Name, Is.EqualTo("06. Slurry pumps, - mixers, - separators and spreaders"));
        Assert.That(areaRuleTranslations[17].atr.AreaRuleId, Is.EqualTo(areaRules[5].Id));
        Assert.That(areaRuleTranslations[17].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[17].atr.Name, Is.EqualTo("06. Schlammpumpen, - Mischer, - Separatoren und Verteiler"));
        Assert.That(areaRuleTranslations[18].atr.AreaRuleId, Is.EqualTo(areaRules[6].Id));
        Assert.That(areaRuleTranslations[18].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[18].atr.Name, Is.EqualTo("07. Forsyningssystemer til vand og foder"));
        Assert.That(areaRuleTranslations[19].atr.AreaRuleId, Is.EqualTo(areaRules[6].Id));
        Assert.That(areaRuleTranslations[19].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[19].atr.Name, Is.EqualTo("07. Supply systems for water and feed"));
        Assert.That(areaRuleTranslations[20].atr.AreaRuleId, Is.EqualTo(areaRules[6].Id));
        Assert.That(areaRuleTranslations[20].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[20].atr.Name, Is.EqualTo("07. Versorgungssysteme für Wasser und Futter"));
        Assert.That(areaRuleTranslations[21].atr.AreaRuleId, Is.EqualTo(areaRules[7].Id));
        Assert.That(areaRuleTranslations[21].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[21].atr.Name, Is.EqualTo("08. Varme-, køle- og ventilationssystemer samt temperaturfølere"));
        Assert.That(areaRuleTranslations[22].atr.AreaRuleId, Is.EqualTo(areaRules[7].Id));
        Assert.That(areaRuleTranslations[22].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[22].atr.Name, Is.EqualTo("08. Heating, cooling and ventilation systems and temperature sensors"));
        Assert.That(areaRuleTranslations[23].atr.AreaRuleId, Is.EqualTo(areaRules[7].Id));
        Assert.That(areaRuleTranslations[23].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[23].atr.Name, Is.EqualTo("08. Heizungs-, Kühl- und Lüftungssysteme und Temperatursensoren"));
        Assert.That(areaRuleTranslations[24].atr.AreaRuleId, Is.EqualTo(areaRules[8].Id));
        Assert.That(areaRuleTranslations[24].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[24].atr.Name, Is.EqualTo("09. Siloer og transportudstyr"));
        Assert.That(areaRuleTranslations[25].atr.AreaRuleId, Is.EqualTo(areaRules[8].Id));
        Assert.That(areaRuleTranslations[25].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[25].atr.Name, Is.EqualTo("09. Silos and transport equipment"));
        Assert.That(areaRuleTranslations[26].atr.AreaRuleId, Is.EqualTo(areaRules[8].Id));
        Assert.That(areaRuleTranslations[26].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[26].atr.Name, Is.EqualTo("09. Silos und Transportgeräte"));
        Assert.That(areaRuleTranslations[27].atr.AreaRuleId, Is.EqualTo(areaRules[9].Id));
        Assert.That(areaRuleTranslations[27].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[27].atr.Name, Is.EqualTo("10. Luftrensningssystemer"));
        Assert.That(areaRuleTranslations[28].atr.AreaRuleId, Is.EqualTo(areaRules[9].Id));
        Assert.That(areaRuleTranslations[28].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[28].atr.Name, Is.EqualTo("10. Air purification systems"));
        Assert.That(areaRuleTranslations[29].atr.AreaRuleId, Is.EqualTo(areaRules[9].Id));
        Assert.That(areaRuleTranslations[29].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[29].atr.Name, Is.EqualTo("10. Luftreinigungssysteme"));
        Assert.That(areaRuleTranslations[30].atr.AreaRuleId, Is.EqualTo(areaRules[10].Id));
        Assert.That(areaRuleTranslations[30].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[30].atr.Name, Is.EqualTo("11. Udstyr til drikkevand"));
        Assert.That(areaRuleTranslations[31].atr.AreaRuleId, Is.EqualTo(areaRules[10].Id));
        Assert.That(areaRuleTranslations[31].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[31].atr.Name, Is.EqualTo("11. Equipment for drinking water"));
        Assert.That(areaRuleTranslations[32].atr.AreaRuleId, Is.EqualTo(areaRules[10].Id));
        Assert.That(areaRuleTranslations[32].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[32].atr.Name, Is.EqualTo("11. Ausrüstung für Trinkwasser"));
        Assert.That(areaRuleTranslations[33].atr.AreaRuleId, Is.EqualTo(areaRules[11].Id));
        Assert.That(areaRuleTranslations[33].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[33].atr.Name, Is.EqualTo("12. Maskiner til udbringning af husdyrgødning samt doseringsmekanisme- eller dyse"));
        Assert.That(areaRuleTranslations[34].atr.AreaRuleId, Is.EqualTo(areaRules[11].Id));
        Assert.That(areaRuleTranslations[34].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[34].atr.Name, Is.EqualTo("12. Machines for spreading livestock manure and dosing mechanisms or nozzles"));
        Assert.That(areaRuleTranslations[35].atr.AreaRuleId, Is.EqualTo(areaRules[11].Id));
        Assert.That(areaRuleTranslations[35].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[35].atr.Name, Is.EqualTo("12. Maschinen zum Ausbringen von Viehmist und Dosiervorrichtungen oder Düsen"));
        Assert.That(areaRuleTranslations[36].atr.AreaRuleId, Is.EqualTo(areaRules[12].Id));
        Assert.That(areaRuleTranslations[36].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[36].atr.Name, Is.EqualTo("13. Miljøledelse gennemgået og revideret"));
        Assert.That(areaRuleTranslations[37].atr.AreaRuleId, Is.EqualTo(areaRules[12].Id));
        Assert.That(areaRuleTranslations[37].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[37].atr.Name, Is.EqualTo("13. Environmental management reviewed and revised"));
        Assert.That(areaRuleTranslations[38].atr.AreaRuleId, Is.EqualTo(areaRules[12].Id));
        Assert.That(areaRuleTranslations[38].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[38].atr.Name, Is.EqualTo("13. Umweltmanagement überprüft und überarbeitet"));
        Assert.That(areaRuleTranslations[39].atr.AreaRuleId, Is.EqualTo(areaRules[13].Id));
        Assert.That(areaRuleTranslations[39].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[39].atr.Name, Is.EqualTo("14. Beredskabsplan gennemgået og revideret"));
        Assert.That(areaRuleTranslations[40].atr.AreaRuleId, Is.EqualTo(areaRules[13].Id));
        Assert.That(areaRuleTranslations[40].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[40].atr.Name, Is.EqualTo("14. Contingency plan reviewed and revised"));
        Assert.That(areaRuleTranslations[41].atr.AreaRuleId, Is.EqualTo(areaRules[13].Id));
        Assert.That(areaRuleTranslations[41].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[41].atr.Name, Is.EqualTo("14. Notfallplan überprüft und überarbeitet"));

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
        Assert.That(areaRulePlannings[0].AreaRuleId, Is.EqualTo(areaRules[areaRuleNo].Id));
        Assert.That(areaRulePlannings[0].ItemPlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(areaRulePlannings[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRulePlannings[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRulePlannings[0].ComplianceEnabled, Is.EqualTo(true));
        Assert.That(areaRulePlannings[0].SendNotifications, Is.EqualTo(true));

        // Assert plannings
        Assert.That(plannings, Is.Not.Null);
        Assert.That(plannings.Count, Is.EqualTo(1));
        Assert.That(plannings[0].RelatedEFormId, Is.EqualTo(areaRules[areaRuleNo].EformId));
        Assert.That(plannings[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
        Assert.That(plannings[0].SdkFolderId, Is.EqualTo(areaRules[areaRuleNo].FolderId));
        Assert.That(plannings[0].LastExecutedTime, Is.Not.Null);
        Assert.That(plannings[0].LastExecutedTime, Is.EqualTo(new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 0,0,0)));
        var now = DateTime.UtcNow;
        var nextExecutionTime =
            new DateTime(now.Year, 4, 12).AddYears(4);

        Assert.That(plannings[0].NextExecutionTime, Is.EqualTo(nextExecutionTime));
        Assert.That(plannings[0].RepeatEvery, Is.EqualTo(48));
        Assert.That(plannings[0].RepeatType, Is.EqualTo(RepeatType.Month));

        // Assert planningNameTranslations
        Assert.That(planningNameTranslations, Is.Not.Null);
        Assert.That(planningNameTranslations.Count, Is.EqualTo(3));
        Assert.That(planningNameTranslations[0].Name, Is.EqualTo(danishTranslation));
        Assert.That(planningNameTranslations[0].LanguageId, Is.EqualTo(danishLanguage.Id));
        Assert.That(planningNameTranslations[0].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(planningNameTranslations[1].Name, Is.EqualTo(englishTranslation));
        Assert.That(planningNameTranslations[1].LanguageId, Is.EqualTo(englishLanguage.Id));
        Assert.That(planningNameTranslations[1].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(planningNameTranslations[2].Name, Is.EqualTo(germanTranslation));
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
        Assert.That(cases[0].CheckListId, Is.EqualTo(areaRules[areaRuleNo].EformId));
        Assert.That(cases[0].FolderId, Is.Null);
        Assert.That(cases[0].Status, Is.EqualTo(66));
        Assert.That(cases[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
    }

    // Should test the UpdatePlanning method for area rule "00. Logbøger" with repeat type "months" and repeat every "2"
    [Test]
    [TestCase(0, "01. Gyllekøling", "01. Slurry cooling", "01. Schlammkühlung")]
    [TestCase(1, "02. Forsuring", "02. Acidification", "02. Ansäuerung")]
    [TestCase(2, "03. Luftrensning", "03. Air purification", "03. Luftreinigung")]
    [TestCase(3, "04. Beholderkontrol gennemført", "04. Container control completed", "04. Behälterkontrolle abgeschlossen")]
    [TestCase(4, "05. Gyllebeholdere", "05. Slurry containers", "05. Güllebehälter")]
    [TestCase(5, "06. Gyllepumper, - miksere, - seperatorer og spredere", "06. Slurry pumps, - mixers, - separators and spreaders", "06. Schlammpumpen, - Mischer, - Separatoren und Verteiler")]
    [TestCase(6, "07. Forsyningssystemer til vand og foder", "07. Supply systems for water and feed", "07. Versorgungssysteme für Wasser und Futter")]
    [TestCase(7, "08. Varme-, køle- og ventilationssystemer samt temperaturfølere", "08. Heating, cooling and ventilation systems and temperature sensors", "08. Heizungs-, Kühl- und Lüftungssysteme und Temperatursensoren")]
    [TestCase(8, "09. Siloer og transportudstyr", "09. Silos and transport equipment", "09. Silos und Transportgeräte")]
    [TestCase(9, "10. Luftrensningssystemer", "10. Air purification systems", "10. Luftreinigungssysteme")]
    [TestCase(10, "11. Udstyr til drikkevand", "11. Equipment for drinking water", "11. Ausrüstung für Trinkwasser")]
    [TestCase(11, "12. Maskiner til udbringning af husdyrgødning samt doseringsmekanisme- eller dyse", "12. Machines for spreading livestock manure and dosing mechanisms or nozzles", "12. Maschinen zum Ausbringen von Viehmist und Dosiervorrichtungen oder Düsen")]
    [TestCase(12, "13. Miljøledelse gennemgået og revideret", "13. Environmental management reviewed and revised", "13. Umweltmanagement überprüft und überarbeitet")]
    [TestCase(13, "14. Beredskabsplan gennemgået og revideret", "14. Contingency plan reviewed and revised", "14. Notfallplan überprüft und überarbeitet")]
    public async Task UpdatePlanning_AreaRuleMonths60_ReturnsSuccess(int areaRuleNo, string danishTranslation, string englishTranslation, string germanTranslation)
    {
        // Arrange
        var core = await GetCore();
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
            RuleId = areaRules[areaRuleNo].Id,
            ComplianceEnabled = true,
            PropertyId = properties[0].Id,
            Status = true,
            SendNotifications = true,
            StartDate = new DateTime(2023, 4, 12, 0, 0,0),
            UseStartDateAsStartOfPeriod = true,
            TypeSpecificFields = new AreaRuleTypePlanningModel
            {
                Alarm = AreaRuleT2AlarmsEnum.No,
                DayOfMonth = 1,
                DayOfWeek = 0,
                HoursAndEnergyEnabled = false,
                RepeatEvery = 60,
                RepeatType = 3
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
        var englishLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "en-US");
        var germanLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "de-DE");
        var danishLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "da");

        // Assert result
        Assert.NotNull(result);
        Assert.That(result.Success, Is.EqualTo(true));

        // Assert areaRules
        Assert.NotNull(areaRules);
        Assert.That(areaRules.Count, Is.EqualTo(14));
        Assert.That(areaRules[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[1].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[2].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[3].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[4].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[5].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[6].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[7].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[8].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[9].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[10].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[11].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[12].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[13].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[1].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[2].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[3].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[4].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[5].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[6].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[7].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[8].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[9].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[10].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[11].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[12].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[0].EformName, Is.EqualTo("01. Gyllekøling"));
        Assert.That(areaRules[1].EformName, Is.EqualTo("02. Forsuring"));
        Assert.That(areaRules[2].EformName, Is.EqualTo("03. Luftrensning"));
        Assert.That(areaRules[3].EformName, Is.EqualTo("04. Beholderkontrol gennemført"));
        Assert.That(areaRules[4].EformName, Is.EqualTo("05. Gyllebeholdere"));
        Assert.That(areaRules[5].EformName, Is.EqualTo("06. Gyllepumper, - miksere, - seperatorer og spredere"));
        Assert.That(areaRules[6].EformName, Is.EqualTo("07. Forsyningssystemer til vand og foder"));
        Assert.That(areaRules[7].EformName, Is.EqualTo("08. Varme-, køle- og ventilationssystemer samt temperaturfølere"));
        Assert.That(areaRules[8].EformName, Is.EqualTo("09. Siloer og transportudstyr"));
        Assert.That(areaRules[9].EformName, Is.EqualTo("10. Luftrensningssystemer"));
        Assert.That(areaRules[10].EformName, Is.EqualTo("11. Udstyr til drikkevand"));
        Assert.That(areaRules[11].EformName, Is.EqualTo("12. Maskiner til udbringning af husdyrgødning samt doseringsmekanisme- eller dyse"));
        Assert.That(areaRules[12].EformName, Is.EqualTo("13. Miljøledelse"));
        Assert.That(areaRules[13].EformName, Is.EqualTo("14. Beredskabsplan"));

        // Assert areaRuleTranslations
        Assert.NotNull(areaRuleTranslations);
        Assert.That(areaRuleTranslations.Count, Is.EqualTo(42));
        Assert.That(areaRuleTranslations[0].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[0].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[0].atr.Name, Is.EqualTo("01. Gyllekøling"));
        Assert.That(areaRuleTranslations[1].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[1].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[1].atr.Name, Is.EqualTo("01. Slurry cooling"));
        Assert.That(areaRuleTranslations[2].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[2].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[2].atr.Name, Is.EqualTo("01. Schlammkühlung"));
        Assert.That(areaRuleTranslations[3].atr.AreaRuleId, Is.EqualTo(areaRules[1].Id));
        Assert.That(areaRuleTranslations[3].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[3].atr.Name, Is.EqualTo("02. Forsuring"));
        Assert.That(areaRuleTranslations[4].atr.AreaRuleId, Is.EqualTo(areaRules[1].Id));
        Assert.That(areaRuleTranslations[4].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[4].atr.Name, Is.EqualTo("02. Acidification"));
        Assert.That(areaRuleTranslations[5].atr.AreaRuleId, Is.EqualTo(areaRules[1].Id));
        Assert.That(areaRuleTranslations[5].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[5].atr.Name, Is.EqualTo("02. Ansäuerung"));
        Assert.That(areaRuleTranslations[6].atr.AreaRuleId, Is.EqualTo(areaRules[2].Id));
        Assert.That(areaRuleTranslations[6].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[6].atr.Name, Is.EqualTo("03. Luftrensning"));
        Assert.That(areaRuleTranslations[7].atr.AreaRuleId, Is.EqualTo(areaRules[2].Id));
        Assert.That(areaRuleTranslations[7].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[7].atr.Name, Is.EqualTo("03. Air purification"));
        Assert.That(areaRuleTranslations[8].atr.AreaRuleId, Is.EqualTo(areaRules[2].Id));
        Assert.That(areaRuleTranslations[8].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[8].atr.Name, Is.EqualTo("03. Luftreinigung"));
        Assert.That(areaRuleTranslations[9].atr.AreaRuleId, Is.EqualTo(areaRules[3].Id));
        Assert.That(areaRuleTranslations[9].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[9].atr.Name, Is.EqualTo("04. Beholderkontrol gennemført"));
        Assert.That(areaRuleTranslations[10].atr.AreaRuleId, Is.EqualTo(areaRules[3].Id));
        Assert.That(areaRuleTranslations[10].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[10].atr.Name, Is.EqualTo("04. Container control completed"));
        Assert.That(areaRuleTranslations[11].atr.AreaRuleId, Is.EqualTo(areaRules[3].Id));
        Assert.That(areaRuleTranslations[11].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[11].atr.Name, Is.EqualTo("04. Behälterkontrolle abgeschlossen"));
        Assert.That(areaRuleTranslations[12].atr.AreaRuleId, Is.EqualTo(areaRules[4].Id));
        Assert.That(areaRuleTranslations[12].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[12].atr.Name, Is.EqualTo("05. Gyllebeholdere"));
        Assert.That(areaRuleTranslations[13].atr.AreaRuleId, Is.EqualTo(areaRules[4].Id));
        Assert.That(areaRuleTranslations[13].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[13].atr.Name, Is.EqualTo("05. Slurry containers"));
        Assert.That(areaRuleTranslations[14].atr.AreaRuleId, Is.EqualTo(areaRules[4].Id));
        Assert.That(areaRuleTranslations[14].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[14].atr.Name, Is.EqualTo("05. Güllebehälter"));
        Assert.That(areaRuleTranslations[15].atr.AreaRuleId, Is.EqualTo(areaRules[5].Id));
        Assert.That(areaRuleTranslations[15].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[15].atr.Name, Is.EqualTo("06. Gyllepumper, - miksere, - seperatorer og spredere"));
        Assert.That(areaRuleTranslations[16].atr.AreaRuleId, Is.EqualTo(areaRules[5].Id));
        Assert.That(areaRuleTranslations[16].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[16].atr.Name, Is.EqualTo("06. Slurry pumps, - mixers, - separators and spreaders"));
        Assert.That(areaRuleTranslations[17].atr.AreaRuleId, Is.EqualTo(areaRules[5].Id));
        Assert.That(areaRuleTranslations[17].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[17].atr.Name, Is.EqualTo("06. Schlammpumpen, - Mischer, - Separatoren und Verteiler"));
        Assert.That(areaRuleTranslations[18].atr.AreaRuleId, Is.EqualTo(areaRules[6].Id));
        Assert.That(areaRuleTranslations[18].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[18].atr.Name, Is.EqualTo("07. Forsyningssystemer til vand og foder"));
        Assert.That(areaRuleTranslations[19].atr.AreaRuleId, Is.EqualTo(areaRules[6].Id));
        Assert.That(areaRuleTranslations[19].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[19].atr.Name, Is.EqualTo("07. Supply systems for water and feed"));
        Assert.That(areaRuleTranslations[20].atr.AreaRuleId, Is.EqualTo(areaRules[6].Id));
        Assert.That(areaRuleTranslations[20].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[20].atr.Name, Is.EqualTo("07. Versorgungssysteme für Wasser und Futter"));
        Assert.That(areaRuleTranslations[21].atr.AreaRuleId, Is.EqualTo(areaRules[7].Id));
        Assert.That(areaRuleTranslations[21].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[21].atr.Name, Is.EqualTo("08. Varme-, køle- og ventilationssystemer samt temperaturfølere"));
        Assert.That(areaRuleTranslations[22].atr.AreaRuleId, Is.EqualTo(areaRules[7].Id));
        Assert.That(areaRuleTranslations[22].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[22].atr.Name, Is.EqualTo("08. Heating, cooling and ventilation systems and temperature sensors"));
        Assert.That(areaRuleTranslations[23].atr.AreaRuleId, Is.EqualTo(areaRules[7].Id));
        Assert.That(areaRuleTranslations[23].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[23].atr.Name, Is.EqualTo("08. Heizungs-, Kühl- und Lüftungssysteme und Temperatursensoren"));
        Assert.That(areaRuleTranslations[24].atr.AreaRuleId, Is.EqualTo(areaRules[8].Id));
        Assert.That(areaRuleTranslations[24].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[24].atr.Name, Is.EqualTo("09. Siloer og transportudstyr"));
        Assert.That(areaRuleTranslations[25].atr.AreaRuleId, Is.EqualTo(areaRules[8].Id));
        Assert.That(areaRuleTranslations[25].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[25].atr.Name, Is.EqualTo("09. Silos and transport equipment"));
        Assert.That(areaRuleTranslations[26].atr.AreaRuleId, Is.EqualTo(areaRules[8].Id));
        Assert.That(areaRuleTranslations[26].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[26].atr.Name, Is.EqualTo("09. Silos und Transportgeräte"));
        Assert.That(areaRuleTranslations[27].atr.AreaRuleId, Is.EqualTo(areaRules[9].Id));
        Assert.That(areaRuleTranslations[27].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[27].atr.Name, Is.EqualTo("10. Luftrensningssystemer"));
        Assert.That(areaRuleTranslations[28].atr.AreaRuleId, Is.EqualTo(areaRules[9].Id));
        Assert.That(areaRuleTranslations[28].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[28].atr.Name, Is.EqualTo("10. Air purification systems"));
        Assert.That(areaRuleTranslations[29].atr.AreaRuleId, Is.EqualTo(areaRules[9].Id));
        Assert.That(areaRuleTranslations[29].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[29].atr.Name, Is.EqualTo("10. Luftreinigungssysteme"));
        Assert.That(areaRuleTranslations[30].atr.AreaRuleId, Is.EqualTo(areaRules[10].Id));
        Assert.That(areaRuleTranslations[30].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[30].atr.Name, Is.EqualTo("11. Udstyr til drikkevand"));
        Assert.That(areaRuleTranslations[31].atr.AreaRuleId, Is.EqualTo(areaRules[10].Id));
        Assert.That(areaRuleTranslations[31].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[31].atr.Name, Is.EqualTo("11. Equipment for drinking water"));
        Assert.That(areaRuleTranslations[32].atr.AreaRuleId, Is.EqualTo(areaRules[10].Id));
        Assert.That(areaRuleTranslations[32].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[32].atr.Name, Is.EqualTo("11. Ausrüstung für Trinkwasser"));
        Assert.That(areaRuleTranslations[33].atr.AreaRuleId, Is.EqualTo(areaRules[11].Id));
        Assert.That(areaRuleTranslations[33].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[33].atr.Name, Is.EqualTo("12. Maskiner til udbringning af husdyrgødning samt doseringsmekanisme- eller dyse"));
        Assert.That(areaRuleTranslations[34].atr.AreaRuleId, Is.EqualTo(areaRules[11].Id));
        Assert.That(areaRuleTranslations[34].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[34].atr.Name, Is.EqualTo("12. Machines for spreading livestock manure and dosing mechanisms or nozzles"));
        Assert.That(areaRuleTranslations[35].atr.AreaRuleId, Is.EqualTo(areaRules[11].Id));
        Assert.That(areaRuleTranslations[35].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[35].atr.Name, Is.EqualTo("12. Maschinen zum Ausbringen von Viehmist und Dosiervorrichtungen oder Düsen"));
        Assert.That(areaRuleTranslations[36].atr.AreaRuleId, Is.EqualTo(areaRules[12].Id));
        Assert.That(areaRuleTranslations[36].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[36].atr.Name, Is.EqualTo("13. Miljøledelse gennemgået og revideret"));
        Assert.That(areaRuleTranslations[37].atr.AreaRuleId, Is.EqualTo(areaRules[12].Id));
        Assert.That(areaRuleTranslations[37].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[37].atr.Name, Is.EqualTo("13. Environmental management reviewed and revised"));
        Assert.That(areaRuleTranslations[38].atr.AreaRuleId, Is.EqualTo(areaRules[12].Id));
        Assert.That(areaRuleTranslations[38].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[38].atr.Name, Is.EqualTo("13. Umweltmanagement überprüft und überarbeitet"));
        Assert.That(areaRuleTranslations[39].atr.AreaRuleId, Is.EqualTo(areaRules[13].Id));
        Assert.That(areaRuleTranslations[39].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[39].atr.Name, Is.EqualTo("14. Beredskabsplan gennemgået og revideret"));
        Assert.That(areaRuleTranslations[40].atr.AreaRuleId, Is.EqualTo(areaRules[13].Id));
        Assert.That(areaRuleTranslations[40].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[40].atr.Name, Is.EqualTo("14. Contingency plan reviewed and revised"));
        Assert.That(areaRuleTranslations[41].atr.AreaRuleId, Is.EqualTo(areaRules[13].Id));
        Assert.That(areaRuleTranslations[41].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[41].atr.Name, Is.EqualTo("14. Notfallplan überprüft und überarbeitet"));

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
        Assert.That(areaRulePlannings[0].AreaRuleId, Is.EqualTo(areaRules[areaRuleNo].Id));
        Assert.That(areaRulePlannings[0].ItemPlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(areaRulePlannings[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRulePlannings[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRulePlannings[0].ComplianceEnabled, Is.EqualTo(true));
        Assert.That(areaRulePlannings[0].SendNotifications, Is.EqualTo(true));

        // Assert plannings
        Assert.That(plannings, Is.Not.Null);
        Assert.That(plannings.Count, Is.EqualTo(1));
        Assert.That(plannings[0].RelatedEFormId, Is.EqualTo(areaRules[areaRuleNo].EformId));
        Assert.That(plannings[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
        Assert.That(plannings[0].SdkFolderId, Is.EqualTo(areaRules[areaRuleNo].FolderId));
        Assert.That(plannings[0].LastExecutedTime, Is.Not.Null);
        Assert.That(plannings[0].LastExecutedTime, Is.EqualTo(new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 0,0,0)));
        var now = DateTime.UtcNow;
        var nextExecutionTime =
            new DateTime(now.Year, 4, 12).AddYears(5);

        Assert.That(plannings[0].NextExecutionTime, Is.EqualTo(nextExecutionTime));
        Assert.That(plannings[0].RepeatEvery, Is.EqualTo(60));
        Assert.That(plannings[0].RepeatType, Is.EqualTo(RepeatType.Month));

        // Assert planningNameTranslations
        Assert.That(planningNameTranslations, Is.Not.Null);
        Assert.That(planningNameTranslations.Count, Is.EqualTo(3));
        Assert.That(planningNameTranslations[0].Name, Is.EqualTo(danishTranslation));
        Assert.That(planningNameTranslations[0].LanguageId, Is.EqualTo(danishLanguage.Id));
        Assert.That(planningNameTranslations[0].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(planningNameTranslations[1].Name, Is.EqualTo(englishTranslation));
        Assert.That(planningNameTranslations[1].LanguageId, Is.EqualTo(englishLanguage.Id));
        Assert.That(planningNameTranslations[1].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(planningNameTranslations[2].Name, Is.EqualTo(germanTranslation));
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
        Assert.That(cases[0].CheckListId, Is.EqualTo(areaRules[areaRuleNo].EformId));
        Assert.That(cases[0].FolderId, Is.Null);
        Assert.That(cases[0].Status, Is.EqualTo(66));
        Assert.That(cases[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
    }

    // Should test the UpdatePlanning method for area rule "00. Logbøger" with repeat type "months" and repeat every "2"
    [Test]
    [TestCase(0, "01. Gyllekøling", "01. Slurry cooling", "01. Schlammkühlung")]
    [TestCase(1, "02. Forsuring", "02. Acidification", "02. Ansäuerung")]
    [TestCase(2, "03. Luftrensning", "03. Air purification", "03. Luftreinigung")]
    [TestCase(3, "04. Beholderkontrol gennemført", "04. Container control completed", "04. Behälterkontrolle abgeschlossen")]
    [TestCase(4, "05. Gyllebeholdere", "05. Slurry containers", "05. Güllebehälter")]
    [TestCase(5, "06. Gyllepumper, - miksere, - seperatorer og spredere", "06. Slurry pumps, - mixers, - separators and spreaders", "06. Schlammpumpen, - Mischer, - Separatoren und Verteiler")]
    [TestCase(6, "07. Forsyningssystemer til vand og foder", "07. Supply systems for water and feed", "07. Versorgungssysteme für Wasser und Futter")]
    [TestCase(7, "08. Varme-, køle- og ventilationssystemer samt temperaturfølere", "08. Heating, cooling and ventilation systems and temperature sensors", "08. Heizungs-, Kühl- und Lüftungssysteme und Temperatursensoren")]
    [TestCase(8, "09. Siloer og transportudstyr", "09. Silos and transport equipment", "09. Silos und Transportgeräte")]
    [TestCase(9, "10. Luftrensningssystemer", "10. Air purification systems", "10. Luftreinigungssysteme")]
    [TestCase(10, "11. Udstyr til drikkevand", "11. Equipment for drinking water", "11. Ausrüstung für Trinkwasser")]
    [TestCase(11, "12. Maskiner til udbringning af husdyrgødning samt doseringsmekanisme- eller dyse", "12. Machines for spreading livestock manure and dosing mechanisms or nozzles", "12. Maschinen zum Ausbringen von Viehmist und Dosiervorrichtungen oder Düsen")]
    [TestCase(12, "13. Miljøledelse gennemgået og revideret", "13. Environmental management reviewed and revised", "13. Umweltmanagement überprüft und überarbeitet")]
    [TestCase(13, "14. Beredskabsplan gennemgået og revideret", "14. Contingency plan reviewed and revised", "14. Notfallplan überprüft und überarbeitet")]
    public async Task UpdatePlanning_AreaRuleMonths72_ReturnsSuccess(int areaRuleNo, string danishTranslation, string englishTranslation, string germanTranslation)
    {
        // Arrange
        var core = await GetCore();
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
            RuleId = areaRules[areaRuleNo].Id,
            ComplianceEnabled = true,
            PropertyId = properties[0].Id,
            Status = true,
            SendNotifications = true,
            StartDate = new DateTime(2023, 4, 12, 0, 0,0),
            UseStartDateAsStartOfPeriod = true,
            TypeSpecificFields = new AreaRuleTypePlanningModel
            {
                Alarm = AreaRuleT2AlarmsEnum.No,
                DayOfMonth = 1,
                DayOfWeek = 0,
                HoursAndEnergyEnabled = false,
                RepeatEvery = 72,
                RepeatType = 3
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
        var englishLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "en-US");
        var germanLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "de-DE");
        var danishLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "da");

        // Assert result
        Assert.NotNull(result);
        Assert.That(result.Success, Is.EqualTo(true));

        // Assert areaRules
        Assert.NotNull(areaRules);
        Assert.That(areaRules.Count, Is.EqualTo(14));
        Assert.That(areaRules[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[1].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[2].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[3].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[4].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[5].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[6].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[7].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[8].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[9].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[10].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[11].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[12].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[13].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[1].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[2].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[3].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[4].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[5].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[6].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[7].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[8].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[9].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[10].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[11].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[12].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[0].EformName, Is.EqualTo("01. Gyllekøling"));
        Assert.That(areaRules[1].EformName, Is.EqualTo("02. Forsuring"));
        Assert.That(areaRules[2].EformName, Is.EqualTo("03. Luftrensning"));
        Assert.That(areaRules[3].EformName, Is.EqualTo("04. Beholderkontrol gennemført"));
        Assert.That(areaRules[4].EformName, Is.EqualTo("05. Gyllebeholdere"));
        Assert.That(areaRules[5].EformName, Is.EqualTo("06. Gyllepumper, - miksere, - seperatorer og spredere"));
        Assert.That(areaRules[6].EformName, Is.EqualTo("07. Forsyningssystemer til vand og foder"));
        Assert.That(areaRules[7].EformName, Is.EqualTo("08. Varme-, køle- og ventilationssystemer samt temperaturfølere"));
        Assert.That(areaRules[8].EformName, Is.EqualTo("09. Siloer og transportudstyr"));
        Assert.That(areaRules[9].EformName, Is.EqualTo("10. Luftrensningssystemer"));
        Assert.That(areaRules[10].EformName, Is.EqualTo("11. Udstyr til drikkevand"));
        Assert.That(areaRules[11].EformName, Is.EqualTo("12. Maskiner til udbringning af husdyrgødning samt doseringsmekanisme- eller dyse"));
        Assert.That(areaRules[12].EformName, Is.EqualTo("13. Miljøledelse"));
        Assert.That(areaRules[13].EformName, Is.EqualTo("14. Beredskabsplan"));

        // Assert areaRuleTranslations
        Assert.NotNull(areaRuleTranslations);
        Assert.That(areaRuleTranslations.Count, Is.EqualTo(42));
        Assert.That(areaRuleTranslations[0].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[0].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[0].atr.Name, Is.EqualTo("01. Gyllekøling"));
        Assert.That(areaRuleTranslations[1].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[1].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[1].atr.Name, Is.EqualTo("01. Slurry cooling"));
        Assert.That(areaRuleTranslations[2].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[2].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[2].atr.Name, Is.EqualTo("01. Schlammkühlung"));
        Assert.That(areaRuleTranslations[3].atr.AreaRuleId, Is.EqualTo(areaRules[1].Id));
        Assert.That(areaRuleTranslations[3].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[3].atr.Name, Is.EqualTo("02. Forsuring"));
        Assert.That(areaRuleTranslations[4].atr.AreaRuleId, Is.EqualTo(areaRules[1].Id));
        Assert.That(areaRuleTranslations[4].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[4].atr.Name, Is.EqualTo("02. Acidification"));
        Assert.That(areaRuleTranslations[5].atr.AreaRuleId, Is.EqualTo(areaRules[1].Id));
        Assert.That(areaRuleTranslations[5].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[5].atr.Name, Is.EqualTo("02. Ansäuerung"));
        Assert.That(areaRuleTranslations[6].atr.AreaRuleId, Is.EqualTo(areaRules[2].Id));
        Assert.That(areaRuleTranslations[6].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[6].atr.Name, Is.EqualTo("03. Luftrensning"));
        Assert.That(areaRuleTranslations[7].atr.AreaRuleId, Is.EqualTo(areaRules[2].Id));
        Assert.That(areaRuleTranslations[7].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[7].atr.Name, Is.EqualTo("03. Air purification"));
        Assert.That(areaRuleTranslations[8].atr.AreaRuleId, Is.EqualTo(areaRules[2].Id));
        Assert.That(areaRuleTranslations[8].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[8].atr.Name, Is.EqualTo("03. Luftreinigung"));
        Assert.That(areaRuleTranslations[9].atr.AreaRuleId, Is.EqualTo(areaRules[3].Id));
        Assert.That(areaRuleTranslations[9].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[9].atr.Name, Is.EqualTo("04. Beholderkontrol gennemført"));
        Assert.That(areaRuleTranslations[10].atr.AreaRuleId, Is.EqualTo(areaRules[3].Id));
        Assert.That(areaRuleTranslations[10].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[10].atr.Name, Is.EqualTo("04. Container control completed"));
        Assert.That(areaRuleTranslations[11].atr.AreaRuleId, Is.EqualTo(areaRules[3].Id));
        Assert.That(areaRuleTranslations[11].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[11].atr.Name, Is.EqualTo("04. Behälterkontrolle abgeschlossen"));
        Assert.That(areaRuleTranslations[12].atr.AreaRuleId, Is.EqualTo(areaRules[4].Id));
        Assert.That(areaRuleTranslations[12].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[12].atr.Name, Is.EqualTo("05. Gyllebeholdere"));
        Assert.That(areaRuleTranslations[13].atr.AreaRuleId, Is.EqualTo(areaRules[4].Id));
        Assert.That(areaRuleTranslations[13].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[13].atr.Name, Is.EqualTo("05. Slurry containers"));
        Assert.That(areaRuleTranslations[14].atr.AreaRuleId, Is.EqualTo(areaRules[4].Id));
        Assert.That(areaRuleTranslations[14].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[14].atr.Name, Is.EqualTo("05. Güllebehälter"));
        Assert.That(areaRuleTranslations[15].atr.AreaRuleId, Is.EqualTo(areaRules[5].Id));
        Assert.That(areaRuleTranslations[15].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[15].atr.Name, Is.EqualTo("06. Gyllepumper, - miksere, - seperatorer og spredere"));
        Assert.That(areaRuleTranslations[16].atr.AreaRuleId, Is.EqualTo(areaRules[5].Id));
        Assert.That(areaRuleTranslations[16].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[16].atr.Name, Is.EqualTo("06. Slurry pumps, - mixers, - separators and spreaders"));
        Assert.That(areaRuleTranslations[17].atr.AreaRuleId, Is.EqualTo(areaRules[5].Id));
        Assert.That(areaRuleTranslations[17].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[17].atr.Name, Is.EqualTo("06. Schlammpumpen, - Mischer, - Separatoren und Verteiler"));
        Assert.That(areaRuleTranslations[18].atr.AreaRuleId, Is.EqualTo(areaRules[6].Id));
        Assert.That(areaRuleTranslations[18].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[18].atr.Name, Is.EqualTo("07. Forsyningssystemer til vand og foder"));
        Assert.That(areaRuleTranslations[19].atr.AreaRuleId, Is.EqualTo(areaRules[6].Id));
        Assert.That(areaRuleTranslations[19].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[19].atr.Name, Is.EqualTo("07. Supply systems for water and feed"));
        Assert.That(areaRuleTranslations[20].atr.AreaRuleId, Is.EqualTo(areaRules[6].Id));
        Assert.That(areaRuleTranslations[20].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[20].atr.Name, Is.EqualTo("07. Versorgungssysteme für Wasser und Futter"));
        Assert.That(areaRuleTranslations[21].atr.AreaRuleId, Is.EqualTo(areaRules[7].Id));
        Assert.That(areaRuleTranslations[21].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[21].atr.Name, Is.EqualTo("08. Varme-, køle- og ventilationssystemer samt temperaturfølere"));
        Assert.That(areaRuleTranslations[22].atr.AreaRuleId, Is.EqualTo(areaRules[7].Id));
        Assert.That(areaRuleTranslations[22].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[22].atr.Name, Is.EqualTo("08. Heating, cooling and ventilation systems and temperature sensors"));
        Assert.That(areaRuleTranslations[23].atr.AreaRuleId, Is.EqualTo(areaRules[7].Id));
        Assert.That(areaRuleTranslations[23].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[23].atr.Name, Is.EqualTo("08. Heizungs-, Kühl- und Lüftungssysteme und Temperatursensoren"));
        Assert.That(areaRuleTranslations[24].atr.AreaRuleId, Is.EqualTo(areaRules[8].Id));
        Assert.That(areaRuleTranslations[24].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[24].atr.Name, Is.EqualTo("09. Siloer og transportudstyr"));
        Assert.That(areaRuleTranslations[25].atr.AreaRuleId, Is.EqualTo(areaRules[8].Id));
        Assert.That(areaRuleTranslations[25].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[25].atr.Name, Is.EqualTo("09. Silos and transport equipment"));
        Assert.That(areaRuleTranslations[26].atr.AreaRuleId, Is.EqualTo(areaRules[8].Id));
        Assert.That(areaRuleTranslations[26].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[26].atr.Name, Is.EqualTo("09. Silos und Transportgeräte"));
        Assert.That(areaRuleTranslations[27].atr.AreaRuleId, Is.EqualTo(areaRules[9].Id));
        Assert.That(areaRuleTranslations[27].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[27].atr.Name, Is.EqualTo("10. Luftrensningssystemer"));
        Assert.That(areaRuleTranslations[28].atr.AreaRuleId, Is.EqualTo(areaRules[9].Id));
        Assert.That(areaRuleTranslations[28].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[28].atr.Name, Is.EqualTo("10. Air purification systems"));
        Assert.That(areaRuleTranslations[29].atr.AreaRuleId, Is.EqualTo(areaRules[9].Id));
        Assert.That(areaRuleTranslations[29].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[29].atr.Name, Is.EqualTo("10. Luftreinigungssysteme"));
        Assert.That(areaRuleTranslations[30].atr.AreaRuleId, Is.EqualTo(areaRules[10].Id));
        Assert.That(areaRuleTranslations[30].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[30].atr.Name, Is.EqualTo("11. Udstyr til drikkevand"));
        Assert.That(areaRuleTranslations[31].atr.AreaRuleId, Is.EqualTo(areaRules[10].Id));
        Assert.That(areaRuleTranslations[31].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[31].atr.Name, Is.EqualTo("11. Equipment for drinking water"));
        Assert.That(areaRuleTranslations[32].atr.AreaRuleId, Is.EqualTo(areaRules[10].Id));
        Assert.That(areaRuleTranslations[32].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[32].atr.Name, Is.EqualTo("11. Ausrüstung für Trinkwasser"));
        Assert.That(areaRuleTranslations[33].atr.AreaRuleId, Is.EqualTo(areaRules[11].Id));
        Assert.That(areaRuleTranslations[33].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[33].atr.Name, Is.EqualTo("12. Maskiner til udbringning af husdyrgødning samt doseringsmekanisme- eller dyse"));
        Assert.That(areaRuleTranslations[34].atr.AreaRuleId, Is.EqualTo(areaRules[11].Id));
        Assert.That(areaRuleTranslations[34].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[34].atr.Name, Is.EqualTo("12. Machines for spreading livestock manure and dosing mechanisms or nozzles"));
        Assert.That(areaRuleTranslations[35].atr.AreaRuleId, Is.EqualTo(areaRules[11].Id));
        Assert.That(areaRuleTranslations[35].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[35].atr.Name, Is.EqualTo("12. Maschinen zum Ausbringen von Viehmist und Dosiervorrichtungen oder Düsen"));
        Assert.That(areaRuleTranslations[36].atr.AreaRuleId, Is.EqualTo(areaRules[12].Id));
        Assert.That(areaRuleTranslations[36].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[36].atr.Name, Is.EqualTo("13. Miljøledelse gennemgået og revideret"));
        Assert.That(areaRuleTranslations[37].atr.AreaRuleId, Is.EqualTo(areaRules[12].Id));
        Assert.That(areaRuleTranslations[37].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[37].atr.Name, Is.EqualTo("13. Environmental management reviewed and revised"));
        Assert.That(areaRuleTranslations[38].atr.AreaRuleId, Is.EqualTo(areaRules[12].Id));
        Assert.That(areaRuleTranslations[38].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[38].atr.Name, Is.EqualTo("13. Umweltmanagement überprüft und überarbeitet"));
        Assert.That(areaRuleTranslations[39].atr.AreaRuleId, Is.EqualTo(areaRules[13].Id));
        Assert.That(areaRuleTranslations[39].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[39].atr.Name, Is.EqualTo("14. Beredskabsplan gennemgået og revideret"));
        Assert.That(areaRuleTranslations[40].atr.AreaRuleId, Is.EqualTo(areaRules[13].Id));
        Assert.That(areaRuleTranslations[40].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[40].atr.Name, Is.EqualTo("14. Contingency plan reviewed and revised"));
        Assert.That(areaRuleTranslations[41].atr.AreaRuleId, Is.EqualTo(areaRules[13].Id));
        Assert.That(areaRuleTranslations[41].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[41].atr.Name, Is.EqualTo("14. Notfallplan überprüft und überarbeitet"));

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
        Assert.That(areaRulePlannings[0].AreaRuleId, Is.EqualTo(areaRules[areaRuleNo].Id));
        Assert.That(areaRulePlannings[0].ItemPlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(areaRulePlannings[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRulePlannings[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRulePlannings[0].ComplianceEnabled, Is.EqualTo(true));
        Assert.That(areaRulePlannings[0].SendNotifications, Is.EqualTo(true));

        // Assert plannings
        Assert.That(plannings, Is.Not.Null);
        Assert.That(plannings.Count, Is.EqualTo(1));
        Assert.That(plannings[0].RelatedEFormId, Is.EqualTo(areaRules[areaRuleNo].EformId));
        Assert.That(plannings[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
        Assert.That(plannings[0].SdkFolderId, Is.EqualTo(areaRules[areaRuleNo].FolderId));
        Assert.That(plannings[0].LastExecutedTime, Is.Not.Null);
        Assert.That(plannings[0].LastExecutedTime, Is.EqualTo(new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 0,0,0)));
        var now = DateTime.UtcNow;
        var nextExecutionTime =
            new DateTime(now.Year, 4, 12).AddYears(6);

        Assert.That(plannings[0].NextExecutionTime, Is.EqualTo(nextExecutionTime));
        Assert.That(plannings[0].RepeatEvery, Is.EqualTo(72));
        Assert.That(plannings[0].RepeatType, Is.EqualTo(RepeatType.Month));

        // Assert planningNameTranslations
        Assert.That(planningNameTranslations, Is.Not.Null);
        Assert.That(planningNameTranslations.Count, Is.EqualTo(3));
        Assert.That(planningNameTranslations[0].Name, Is.EqualTo(danishTranslation));
        Assert.That(planningNameTranslations[0].LanguageId, Is.EqualTo(danishLanguage.Id));
        Assert.That(planningNameTranslations[0].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(planningNameTranslations[1].Name, Is.EqualTo(englishTranslation));
        Assert.That(planningNameTranslations[1].LanguageId, Is.EqualTo(englishLanguage.Id));
        Assert.That(planningNameTranslations[1].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(planningNameTranslations[2].Name, Is.EqualTo(germanTranslation));
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
        Assert.That(cases[0].CheckListId, Is.EqualTo(areaRules[areaRuleNo].EformId));
        Assert.That(cases[0].FolderId, Is.Null);
        Assert.That(cases[0].Status, Is.EqualTo(66));
        Assert.That(cases[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
    }

    // Should test the UpdatePlanning method for area rule "00. Logbøger" with repeat type "months" and repeat every "2"
    [Test]
    [TestCase(0, "01. Gyllekøling", "01. Slurry cooling", "01. Schlammkühlung")]
    [TestCase(1, "02. Forsuring", "02. Acidification", "02. Ansäuerung")]
    [TestCase(2, "03. Luftrensning", "03. Air purification", "03. Luftreinigung")]
    [TestCase(3, "04. Beholderkontrol gennemført", "04. Container control completed", "04. Behälterkontrolle abgeschlossen")]
    [TestCase(4, "05. Gyllebeholdere", "05. Slurry containers", "05. Güllebehälter")]
    [TestCase(5, "06. Gyllepumper, - miksere, - seperatorer og spredere", "06. Slurry pumps, - mixers, - separators and spreaders", "06. Schlammpumpen, - Mischer, - Separatoren und Verteiler")]
    [TestCase(6, "07. Forsyningssystemer til vand og foder", "07. Supply systems for water and feed", "07. Versorgungssysteme für Wasser und Futter")]
    [TestCase(7, "08. Varme-, køle- og ventilationssystemer samt temperaturfølere", "08. Heating, cooling and ventilation systems and temperature sensors", "08. Heizungs-, Kühl- und Lüftungssysteme und Temperatursensoren")]
    [TestCase(8, "09. Siloer og transportudstyr", "09. Silos and transport equipment", "09. Silos und Transportgeräte")]
    [TestCase(9, "10. Luftrensningssystemer", "10. Air purification systems", "10. Luftreinigungssysteme")]
    [TestCase(10, "11. Udstyr til drikkevand", "11. Equipment for drinking water", "11. Ausrüstung für Trinkwasser")]
    [TestCase(11, "12. Maskiner til udbringning af husdyrgødning samt doseringsmekanisme- eller dyse", "12. Machines for spreading livestock manure and dosing mechanisms or nozzles", "12. Maschinen zum Ausbringen von Viehmist und Dosiervorrichtungen oder Düsen")]
    [TestCase(12, "13. Miljøledelse gennemgået og revideret", "13. Environmental management reviewed and revised", "13. Umweltmanagement überprüft und überarbeitet")]
    [TestCase(13, "14. Beredskabsplan gennemgået og revideret", "14. Contingency plan reviewed and revised", "14. Notfallplan überprüft und überarbeitet")]
    public async Task UpdatePlanning_AreaRuleMonths84_ReturnsSuccess(int areaRuleNo, string danishTranslation, string englishTranslation, string germanTranslation)
    {
        // Arrange
        var core = await GetCore();
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
            RuleId = areaRules[areaRuleNo].Id,
            ComplianceEnabled = true,
            PropertyId = properties[0].Id,
            Status = true,
            SendNotifications = true,
            StartDate = new DateTime(2023, 4, 12, 0, 0,0),
            UseStartDateAsStartOfPeriod = true,
            TypeSpecificFields = new AreaRuleTypePlanningModel
            {
                Alarm = AreaRuleT2AlarmsEnum.No,
                DayOfMonth = 1,
                DayOfWeek = 0,
                HoursAndEnergyEnabled = false,
                RepeatEvery = 84,
                RepeatType = 3
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
        var englishLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "en-US");
        var germanLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "de-DE");
        var danishLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "da");

        // Assert result
        Assert.NotNull(result);
        Assert.That(result.Success, Is.EqualTo(true));

        // Assert areaRules
        Assert.NotNull(areaRules);
        Assert.That(areaRules.Count, Is.EqualTo(14));
        Assert.That(areaRules[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[1].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[2].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[3].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[4].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[5].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[6].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[7].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[8].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[9].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[10].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[11].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[12].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[13].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[1].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[2].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[3].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[4].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[5].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[6].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[7].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[8].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[9].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[10].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[11].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[12].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[0].EformName, Is.EqualTo("01. Gyllekøling"));
        Assert.That(areaRules[1].EformName, Is.EqualTo("02. Forsuring"));
        Assert.That(areaRules[2].EformName, Is.EqualTo("03. Luftrensning"));
        Assert.That(areaRules[3].EformName, Is.EqualTo("04. Beholderkontrol gennemført"));
        Assert.That(areaRules[4].EformName, Is.EqualTo("05. Gyllebeholdere"));
        Assert.That(areaRules[5].EformName, Is.EqualTo("06. Gyllepumper, - miksere, - seperatorer og spredere"));
        Assert.That(areaRules[6].EformName, Is.EqualTo("07. Forsyningssystemer til vand og foder"));
        Assert.That(areaRules[7].EformName, Is.EqualTo("08. Varme-, køle- og ventilationssystemer samt temperaturfølere"));
        Assert.That(areaRules[8].EformName, Is.EqualTo("09. Siloer og transportudstyr"));
        Assert.That(areaRules[9].EformName, Is.EqualTo("10. Luftrensningssystemer"));
        Assert.That(areaRules[10].EformName, Is.EqualTo("11. Udstyr til drikkevand"));
        Assert.That(areaRules[11].EformName, Is.EqualTo("12. Maskiner til udbringning af husdyrgødning samt doseringsmekanisme- eller dyse"));
        Assert.That(areaRules[12].EformName, Is.EqualTo("13. Miljøledelse"));
        Assert.That(areaRules[13].EformName, Is.EqualTo("14. Beredskabsplan"));

        // Assert areaRuleTranslations
        Assert.NotNull(areaRuleTranslations);
        Assert.That(areaRuleTranslations.Count, Is.EqualTo(42));
        Assert.That(areaRuleTranslations[0].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[0].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[0].atr.Name, Is.EqualTo("01. Gyllekøling"));
        Assert.That(areaRuleTranslations[1].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[1].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[1].atr.Name, Is.EqualTo("01. Slurry cooling"));
        Assert.That(areaRuleTranslations[2].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[2].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[2].atr.Name, Is.EqualTo("01. Schlammkühlung"));
        Assert.That(areaRuleTranslations[3].atr.AreaRuleId, Is.EqualTo(areaRules[1].Id));
        Assert.That(areaRuleTranslations[3].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[3].atr.Name, Is.EqualTo("02. Forsuring"));
        Assert.That(areaRuleTranslations[4].atr.AreaRuleId, Is.EqualTo(areaRules[1].Id));
        Assert.That(areaRuleTranslations[4].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[4].atr.Name, Is.EqualTo("02. Acidification"));
        Assert.That(areaRuleTranslations[5].atr.AreaRuleId, Is.EqualTo(areaRules[1].Id));
        Assert.That(areaRuleTranslations[5].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[5].atr.Name, Is.EqualTo("02. Ansäuerung"));
        Assert.That(areaRuleTranslations[6].atr.AreaRuleId, Is.EqualTo(areaRules[2].Id));
        Assert.That(areaRuleTranslations[6].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[6].atr.Name, Is.EqualTo("03. Luftrensning"));
        Assert.That(areaRuleTranslations[7].atr.AreaRuleId, Is.EqualTo(areaRules[2].Id));
        Assert.That(areaRuleTranslations[7].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[7].atr.Name, Is.EqualTo("03. Air purification"));
        Assert.That(areaRuleTranslations[8].atr.AreaRuleId, Is.EqualTo(areaRules[2].Id));
        Assert.That(areaRuleTranslations[8].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[8].atr.Name, Is.EqualTo("03. Luftreinigung"));
        Assert.That(areaRuleTranslations[9].atr.AreaRuleId, Is.EqualTo(areaRules[3].Id));
        Assert.That(areaRuleTranslations[9].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[9].atr.Name, Is.EqualTo("04. Beholderkontrol gennemført"));
        Assert.That(areaRuleTranslations[10].atr.AreaRuleId, Is.EqualTo(areaRules[3].Id));
        Assert.That(areaRuleTranslations[10].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[10].atr.Name, Is.EqualTo("04. Container control completed"));
        Assert.That(areaRuleTranslations[11].atr.AreaRuleId, Is.EqualTo(areaRules[3].Id));
        Assert.That(areaRuleTranslations[11].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[11].atr.Name, Is.EqualTo("04. Behälterkontrolle abgeschlossen"));
        Assert.That(areaRuleTranslations[12].atr.AreaRuleId, Is.EqualTo(areaRules[4].Id));
        Assert.That(areaRuleTranslations[12].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[12].atr.Name, Is.EqualTo("05. Gyllebeholdere"));
        Assert.That(areaRuleTranslations[13].atr.AreaRuleId, Is.EqualTo(areaRules[4].Id));
        Assert.That(areaRuleTranslations[13].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[13].atr.Name, Is.EqualTo("05. Slurry containers"));
        Assert.That(areaRuleTranslations[14].atr.AreaRuleId, Is.EqualTo(areaRules[4].Id));
        Assert.That(areaRuleTranslations[14].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[14].atr.Name, Is.EqualTo("05. Güllebehälter"));
        Assert.That(areaRuleTranslations[15].atr.AreaRuleId, Is.EqualTo(areaRules[5].Id));
        Assert.That(areaRuleTranslations[15].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[15].atr.Name, Is.EqualTo("06. Gyllepumper, - miksere, - seperatorer og spredere"));
        Assert.That(areaRuleTranslations[16].atr.AreaRuleId, Is.EqualTo(areaRules[5].Id));
        Assert.That(areaRuleTranslations[16].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[16].atr.Name, Is.EqualTo("06. Slurry pumps, - mixers, - separators and spreaders"));
        Assert.That(areaRuleTranslations[17].atr.AreaRuleId, Is.EqualTo(areaRules[5].Id));
        Assert.That(areaRuleTranslations[17].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[17].atr.Name, Is.EqualTo("06. Schlammpumpen, - Mischer, - Separatoren und Verteiler"));
        Assert.That(areaRuleTranslations[18].atr.AreaRuleId, Is.EqualTo(areaRules[6].Id));
        Assert.That(areaRuleTranslations[18].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[18].atr.Name, Is.EqualTo("07. Forsyningssystemer til vand og foder"));
        Assert.That(areaRuleTranslations[19].atr.AreaRuleId, Is.EqualTo(areaRules[6].Id));
        Assert.That(areaRuleTranslations[19].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[19].atr.Name, Is.EqualTo("07. Supply systems for water and feed"));
        Assert.That(areaRuleTranslations[20].atr.AreaRuleId, Is.EqualTo(areaRules[6].Id));
        Assert.That(areaRuleTranslations[20].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[20].atr.Name, Is.EqualTo("07. Versorgungssysteme für Wasser und Futter"));
        Assert.That(areaRuleTranslations[21].atr.AreaRuleId, Is.EqualTo(areaRules[7].Id));
        Assert.That(areaRuleTranslations[21].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[21].atr.Name, Is.EqualTo("08. Varme-, køle- og ventilationssystemer samt temperaturfølere"));
        Assert.That(areaRuleTranslations[22].atr.AreaRuleId, Is.EqualTo(areaRules[7].Id));
        Assert.That(areaRuleTranslations[22].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[22].atr.Name, Is.EqualTo("08. Heating, cooling and ventilation systems and temperature sensors"));
        Assert.That(areaRuleTranslations[23].atr.AreaRuleId, Is.EqualTo(areaRules[7].Id));
        Assert.That(areaRuleTranslations[23].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[23].atr.Name, Is.EqualTo("08. Heizungs-, Kühl- und Lüftungssysteme und Temperatursensoren"));
        Assert.That(areaRuleTranslations[24].atr.AreaRuleId, Is.EqualTo(areaRules[8].Id));
        Assert.That(areaRuleTranslations[24].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[24].atr.Name, Is.EqualTo("09. Siloer og transportudstyr"));
        Assert.That(areaRuleTranslations[25].atr.AreaRuleId, Is.EqualTo(areaRules[8].Id));
        Assert.That(areaRuleTranslations[25].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[25].atr.Name, Is.EqualTo("09. Silos and transport equipment"));
        Assert.That(areaRuleTranslations[26].atr.AreaRuleId, Is.EqualTo(areaRules[8].Id));
        Assert.That(areaRuleTranslations[26].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[26].atr.Name, Is.EqualTo("09. Silos und Transportgeräte"));
        Assert.That(areaRuleTranslations[27].atr.AreaRuleId, Is.EqualTo(areaRules[9].Id));
        Assert.That(areaRuleTranslations[27].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[27].atr.Name, Is.EqualTo("10. Luftrensningssystemer"));
        Assert.That(areaRuleTranslations[28].atr.AreaRuleId, Is.EqualTo(areaRules[9].Id));
        Assert.That(areaRuleTranslations[28].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[28].atr.Name, Is.EqualTo("10. Air purification systems"));
        Assert.That(areaRuleTranslations[29].atr.AreaRuleId, Is.EqualTo(areaRules[9].Id));
        Assert.That(areaRuleTranslations[29].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[29].atr.Name, Is.EqualTo("10. Luftreinigungssysteme"));
        Assert.That(areaRuleTranslations[30].atr.AreaRuleId, Is.EqualTo(areaRules[10].Id));
        Assert.That(areaRuleTranslations[30].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[30].atr.Name, Is.EqualTo("11. Udstyr til drikkevand"));
        Assert.That(areaRuleTranslations[31].atr.AreaRuleId, Is.EqualTo(areaRules[10].Id));
        Assert.That(areaRuleTranslations[31].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[31].atr.Name, Is.EqualTo("11. Equipment for drinking water"));
        Assert.That(areaRuleTranslations[32].atr.AreaRuleId, Is.EqualTo(areaRules[10].Id));
        Assert.That(areaRuleTranslations[32].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[32].atr.Name, Is.EqualTo("11. Ausrüstung für Trinkwasser"));
        Assert.That(areaRuleTranslations[33].atr.AreaRuleId, Is.EqualTo(areaRules[11].Id));
        Assert.That(areaRuleTranslations[33].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[33].atr.Name, Is.EqualTo("12. Maskiner til udbringning af husdyrgødning samt doseringsmekanisme- eller dyse"));
        Assert.That(areaRuleTranslations[34].atr.AreaRuleId, Is.EqualTo(areaRules[11].Id));
        Assert.That(areaRuleTranslations[34].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[34].atr.Name, Is.EqualTo("12. Machines for spreading livestock manure and dosing mechanisms or nozzles"));
        Assert.That(areaRuleTranslations[35].atr.AreaRuleId, Is.EqualTo(areaRules[11].Id));
        Assert.That(areaRuleTranslations[35].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[35].atr.Name, Is.EqualTo("12. Maschinen zum Ausbringen von Viehmist und Dosiervorrichtungen oder Düsen"));
        Assert.That(areaRuleTranslations[36].atr.AreaRuleId, Is.EqualTo(areaRules[12].Id));
        Assert.That(areaRuleTranslations[36].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[36].atr.Name, Is.EqualTo("13. Miljøledelse gennemgået og revideret"));
        Assert.That(areaRuleTranslations[37].atr.AreaRuleId, Is.EqualTo(areaRules[12].Id));
        Assert.That(areaRuleTranslations[37].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[37].atr.Name, Is.EqualTo("13. Environmental management reviewed and revised"));
        Assert.That(areaRuleTranslations[38].atr.AreaRuleId, Is.EqualTo(areaRules[12].Id));
        Assert.That(areaRuleTranslations[38].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[38].atr.Name, Is.EqualTo("13. Umweltmanagement überprüft und überarbeitet"));
        Assert.That(areaRuleTranslations[39].atr.AreaRuleId, Is.EqualTo(areaRules[13].Id));
        Assert.That(areaRuleTranslations[39].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[39].atr.Name, Is.EqualTo("14. Beredskabsplan gennemgået og revideret"));
        Assert.That(areaRuleTranslations[40].atr.AreaRuleId, Is.EqualTo(areaRules[13].Id));
        Assert.That(areaRuleTranslations[40].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[40].atr.Name, Is.EqualTo("14. Contingency plan reviewed and revised"));
        Assert.That(areaRuleTranslations[41].atr.AreaRuleId, Is.EqualTo(areaRules[13].Id));
        Assert.That(areaRuleTranslations[41].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[41].atr.Name, Is.EqualTo("14. Notfallplan überprüft und überarbeitet"));

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
        Assert.That(areaRulePlannings[0].AreaRuleId, Is.EqualTo(areaRules[areaRuleNo].Id));
        Assert.That(areaRulePlannings[0].ItemPlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(areaRulePlannings[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRulePlannings[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRulePlannings[0].ComplianceEnabled, Is.EqualTo(true));
        Assert.That(areaRulePlannings[0].SendNotifications, Is.EqualTo(true));

        // Assert plannings
        Assert.That(plannings, Is.Not.Null);
        Assert.That(plannings.Count, Is.EqualTo(1));
        Assert.That(plannings[0].RelatedEFormId, Is.EqualTo(areaRules[areaRuleNo].EformId));
        Assert.That(plannings[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
        Assert.That(plannings[0].SdkFolderId, Is.EqualTo(areaRules[areaRuleNo].FolderId));
        Assert.That(plannings[0].LastExecutedTime, Is.Not.Null);
        Assert.That(plannings[0].LastExecutedTime, Is.EqualTo(new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 0,0,0)));
        var now = DateTime.UtcNow;
        var nextExecutionTime =
            new DateTime(now.Year, 4, 12).AddYears(7);

        Assert.That(plannings[0].NextExecutionTime, Is.EqualTo(nextExecutionTime));
        Assert.That(plannings[0].RepeatEvery, Is.EqualTo(84));
        Assert.That(plannings[0].RepeatType, Is.EqualTo(RepeatType.Month));

        // Assert planningNameTranslations
        Assert.That(planningNameTranslations, Is.Not.Null);
        Assert.That(planningNameTranslations.Count, Is.EqualTo(3));
        Assert.That(planningNameTranslations[0].Name, Is.EqualTo(danishTranslation));
        Assert.That(planningNameTranslations[0].LanguageId, Is.EqualTo(danishLanguage.Id));
        Assert.That(planningNameTranslations[0].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(planningNameTranslations[1].Name, Is.EqualTo(englishTranslation));
        Assert.That(planningNameTranslations[1].LanguageId, Is.EqualTo(englishLanguage.Id));
        Assert.That(planningNameTranslations[1].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(planningNameTranslations[2].Name, Is.EqualTo(germanTranslation));
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
        Assert.That(cases[0].CheckListId, Is.EqualTo(areaRules[areaRuleNo].EformId));
        Assert.That(cases[0].FolderId, Is.Null);
        Assert.That(cases[0].Status, Is.EqualTo(66));
        Assert.That(cases[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
    }

    // Should test the UpdatePlanning method for area rule "00. Logbøger" with repeat type "months" and repeat every "2"
    [Test]
    [TestCase(0, "01. Gyllekøling", "01. Slurry cooling", "01. Schlammkühlung")]
    [TestCase(1, "02. Forsuring", "02. Acidification", "02. Ansäuerung")]
    [TestCase(2, "03. Luftrensning", "03. Air purification", "03. Luftreinigung")]
    [TestCase(3, "04. Beholderkontrol gennemført", "04. Container control completed", "04. Behälterkontrolle abgeschlossen")]
    [TestCase(4, "05. Gyllebeholdere", "05. Slurry containers", "05. Güllebehälter")]
    [TestCase(5, "06. Gyllepumper, - miksere, - seperatorer og spredere", "06. Slurry pumps, - mixers, - separators and spreaders", "06. Schlammpumpen, - Mischer, - Separatoren und Verteiler")]
    [TestCase(6, "07. Forsyningssystemer til vand og foder", "07. Supply systems for water and feed", "07. Versorgungssysteme für Wasser und Futter")]
    [TestCase(7, "08. Varme-, køle- og ventilationssystemer samt temperaturfølere", "08. Heating, cooling and ventilation systems and temperature sensors", "08. Heizungs-, Kühl- und Lüftungssysteme und Temperatursensoren")]
    [TestCase(8, "09. Siloer og transportudstyr", "09. Silos and transport equipment", "09. Silos und Transportgeräte")]
    [TestCase(9, "10. Luftrensningssystemer", "10. Air purification systems", "10. Luftreinigungssysteme")]
    [TestCase(10, "11. Udstyr til drikkevand", "11. Equipment for drinking water", "11. Ausrüstung für Trinkwasser")]
    [TestCase(11, "12. Maskiner til udbringning af husdyrgødning samt doseringsmekanisme- eller dyse", "12. Machines for spreading livestock manure and dosing mechanisms or nozzles", "12. Maschinen zum Ausbringen von Viehmist und Dosiervorrichtungen oder Düsen")]
    [TestCase(12, "13. Miljøledelse gennemgået og revideret", "13. Environmental management reviewed and revised", "13. Umweltmanagement überprüft und überarbeitet")]
    [TestCase(13, "14. Beredskabsplan gennemgået og revideret", "14. Contingency plan reviewed and revised", "14. Notfallplan überprüft und überarbeitet")]
    public async Task UpdatePlanning_AreaRuleMonths96_ReturnsSuccess(int areaRuleNo, string danishTranslation, string englishTranslation, string germanTranslation)
    {
        // Arrange
        var core = await GetCore();
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
            RuleId = areaRules[areaRuleNo].Id,
            ComplianceEnabled = true,
            PropertyId = properties[0].Id,
            Status = true,
            SendNotifications = true,
            StartDate = new DateTime(2023, 4, 12, 0, 0,0),
            UseStartDateAsStartOfPeriod = true,
            TypeSpecificFields = new AreaRuleTypePlanningModel
            {
                Alarm = AreaRuleT2AlarmsEnum.No,
                DayOfMonth = 1,
                DayOfWeek = 0,
                HoursAndEnergyEnabled = false,
                RepeatEvery = 96,
                RepeatType = 3
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
        var englishLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "en-US");
        var germanLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "de-DE");
        var danishLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "da");

        // Assert result
        Assert.NotNull(result);
        Assert.That(result.Success, Is.EqualTo(true));

        // Assert areaRules
        Assert.NotNull(areaRules);
        Assert.That(areaRules.Count, Is.EqualTo(14));
        Assert.That(areaRules[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[1].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[2].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[3].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[4].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[5].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[6].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[7].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[8].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[9].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[10].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[11].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[12].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[13].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[1].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[2].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[3].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[4].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[5].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[6].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[7].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[8].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[9].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[10].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[11].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[12].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[0].EformName, Is.EqualTo("01. Gyllekøling"));
        Assert.That(areaRules[1].EformName, Is.EqualTo("02. Forsuring"));
        Assert.That(areaRules[2].EformName, Is.EqualTo("03. Luftrensning"));
        Assert.That(areaRules[3].EformName, Is.EqualTo("04. Beholderkontrol gennemført"));
        Assert.That(areaRules[4].EformName, Is.EqualTo("05. Gyllebeholdere"));
        Assert.That(areaRules[5].EformName, Is.EqualTo("06. Gyllepumper, - miksere, - seperatorer og spredere"));
        Assert.That(areaRules[6].EformName, Is.EqualTo("07. Forsyningssystemer til vand og foder"));
        Assert.That(areaRules[7].EformName, Is.EqualTo("08. Varme-, køle- og ventilationssystemer samt temperaturfølere"));
        Assert.That(areaRules[8].EformName, Is.EqualTo("09. Siloer og transportudstyr"));
        Assert.That(areaRules[9].EformName, Is.EqualTo("10. Luftrensningssystemer"));
        Assert.That(areaRules[10].EformName, Is.EqualTo("11. Udstyr til drikkevand"));
        Assert.That(areaRules[11].EformName, Is.EqualTo("12. Maskiner til udbringning af husdyrgødning samt doseringsmekanisme- eller dyse"));
        Assert.That(areaRules[12].EformName, Is.EqualTo("13. Miljøledelse"));
        Assert.That(areaRules[13].EformName, Is.EqualTo("14. Beredskabsplan"));

        // Assert areaRuleTranslations
        Assert.NotNull(areaRuleTranslations);
        Assert.That(areaRuleTranslations.Count, Is.EqualTo(42));
        Assert.That(areaRuleTranslations[0].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[0].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[0].atr.Name, Is.EqualTo("01. Gyllekøling"));
        Assert.That(areaRuleTranslations[1].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[1].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[1].atr.Name, Is.EqualTo("01. Slurry cooling"));
        Assert.That(areaRuleTranslations[2].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[2].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[2].atr.Name, Is.EqualTo("01. Schlammkühlung"));
        Assert.That(areaRuleTranslations[3].atr.AreaRuleId, Is.EqualTo(areaRules[1].Id));
        Assert.That(areaRuleTranslations[3].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[3].atr.Name, Is.EqualTo("02. Forsuring"));
        Assert.That(areaRuleTranslations[4].atr.AreaRuleId, Is.EqualTo(areaRules[1].Id));
        Assert.That(areaRuleTranslations[4].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[4].atr.Name, Is.EqualTo("02. Acidification"));
        Assert.That(areaRuleTranslations[5].atr.AreaRuleId, Is.EqualTo(areaRules[1].Id));
        Assert.That(areaRuleTranslations[5].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[5].atr.Name, Is.EqualTo("02. Ansäuerung"));
        Assert.That(areaRuleTranslations[6].atr.AreaRuleId, Is.EqualTo(areaRules[2].Id));
        Assert.That(areaRuleTranslations[6].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[6].atr.Name, Is.EqualTo("03. Luftrensning"));
        Assert.That(areaRuleTranslations[7].atr.AreaRuleId, Is.EqualTo(areaRules[2].Id));
        Assert.That(areaRuleTranslations[7].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[7].atr.Name, Is.EqualTo("03. Air purification"));
        Assert.That(areaRuleTranslations[8].atr.AreaRuleId, Is.EqualTo(areaRules[2].Id));
        Assert.That(areaRuleTranslations[8].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[8].atr.Name, Is.EqualTo("03. Luftreinigung"));
        Assert.That(areaRuleTranslations[9].atr.AreaRuleId, Is.EqualTo(areaRules[3].Id));
        Assert.That(areaRuleTranslations[9].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[9].atr.Name, Is.EqualTo("04. Beholderkontrol gennemført"));
        Assert.That(areaRuleTranslations[10].atr.AreaRuleId, Is.EqualTo(areaRules[3].Id));
        Assert.That(areaRuleTranslations[10].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[10].atr.Name, Is.EqualTo("04. Container control completed"));
        Assert.That(areaRuleTranslations[11].atr.AreaRuleId, Is.EqualTo(areaRules[3].Id));
        Assert.That(areaRuleTranslations[11].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[11].atr.Name, Is.EqualTo("04. Behälterkontrolle abgeschlossen"));
        Assert.That(areaRuleTranslations[12].atr.AreaRuleId, Is.EqualTo(areaRules[4].Id));
        Assert.That(areaRuleTranslations[12].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[12].atr.Name, Is.EqualTo("05. Gyllebeholdere"));
        Assert.That(areaRuleTranslations[13].atr.AreaRuleId, Is.EqualTo(areaRules[4].Id));
        Assert.That(areaRuleTranslations[13].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[13].atr.Name, Is.EqualTo("05. Slurry containers"));
        Assert.That(areaRuleTranslations[14].atr.AreaRuleId, Is.EqualTo(areaRules[4].Id));
        Assert.That(areaRuleTranslations[14].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[14].atr.Name, Is.EqualTo("05. Güllebehälter"));
        Assert.That(areaRuleTranslations[15].atr.AreaRuleId, Is.EqualTo(areaRules[5].Id));
        Assert.That(areaRuleTranslations[15].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[15].atr.Name, Is.EqualTo("06. Gyllepumper, - miksere, - seperatorer og spredere"));
        Assert.That(areaRuleTranslations[16].atr.AreaRuleId, Is.EqualTo(areaRules[5].Id));
        Assert.That(areaRuleTranslations[16].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[16].atr.Name, Is.EqualTo("06. Slurry pumps, - mixers, - separators and spreaders"));
        Assert.That(areaRuleTranslations[17].atr.AreaRuleId, Is.EqualTo(areaRules[5].Id));
        Assert.That(areaRuleTranslations[17].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[17].atr.Name, Is.EqualTo("06. Schlammpumpen, - Mischer, - Separatoren und Verteiler"));
        Assert.That(areaRuleTranslations[18].atr.AreaRuleId, Is.EqualTo(areaRules[6].Id));
        Assert.That(areaRuleTranslations[18].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[18].atr.Name, Is.EqualTo("07. Forsyningssystemer til vand og foder"));
        Assert.That(areaRuleTranslations[19].atr.AreaRuleId, Is.EqualTo(areaRules[6].Id));
        Assert.That(areaRuleTranslations[19].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[19].atr.Name, Is.EqualTo("07. Supply systems for water and feed"));
        Assert.That(areaRuleTranslations[20].atr.AreaRuleId, Is.EqualTo(areaRules[6].Id));
        Assert.That(areaRuleTranslations[20].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[20].atr.Name, Is.EqualTo("07. Versorgungssysteme für Wasser und Futter"));
        Assert.That(areaRuleTranslations[21].atr.AreaRuleId, Is.EqualTo(areaRules[7].Id));
        Assert.That(areaRuleTranslations[21].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[21].atr.Name, Is.EqualTo("08. Varme-, køle- og ventilationssystemer samt temperaturfølere"));
        Assert.That(areaRuleTranslations[22].atr.AreaRuleId, Is.EqualTo(areaRules[7].Id));
        Assert.That(areaRuleTranslations[22].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[22].atr.Name, Is.EqualTo("08. Heating, cooling and ventilation systems and temperature sensors"));
        Assert.That(areaRuleTranslations[23].atr.AreaRuleId, Is.EqualTo(areaRules[7].Id));
        Assert.That(areaRuleTranslations[23].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[23].atr.Name, Is.EqualTo("08. Heizungs-, Kühl- und Lüftungssysteme und Temperatursensoren"));
        Assert.That(areaRuleTranslations[24].atr.AreaRuleId, Is.EqualTo(areaRules[8].Id));
        Assert.That(areaRuleTranslations[24].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[24].atr.Name, Is.EqualTo("09. Siloer og transportudstyr"));
        Assert.That(areaRuleTranslations[25].atr.AreaRuleId, Is.EqualTo(areaRules[8].Id));
        Assert.That(areaRuleTranslations[25].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[25].atr.Name, Is.EqualTo("09. Silos and transport equipment"));
        Assert.That(areaRuleTranslations[26].atr.AreaRuleId, Is.EqualTo(areaRules[8].Id));
        Assert.That(areaRuleTranslations[26].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[26].atr.Name, Is.EqualTo("09. Silos und Transportgeräte"));
        Assert.That(areaRuleTranslations[27].atr.AreaRuleId, Is.EqualTo(areaRules[9].Id));
        Assert.That(areaRuleTranslations[27].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[27].atr.Name, Is.EqualTo("10. Luftrensningssystemer"));
        Assert.That(areaRuleTranslations[28].atr.AreaRuleId, Is.EqualTo(areaRules[9].Id));
        Assert.That(areaRuleTranslations[28].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[28].atr.Name, Is.EqualTo("10. Air purification systems"));
        Assert.That(areaRuleTranslations[29].atr.AreaRuleId, Is.EqualTo(areaRules[9].Id));
        Assert.That(areaRuleTranslations[29].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[29].atr.Name, Is.EqualTo("10. Luftreinigungssysteme"));
        Assert.That(areaRuleTranslations[30].atr.AreaRuleId, Is.EqualTo(areaRules[10].Id));
        Assert.That(areaRuleTranslations[30].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[30].atr.Name, Is.EqualTo("11. Udstyr til drikkevand"));
        Assert.That(areaRuleTranslations[31].atr.AreaRuleId, Is.EqualTo(areaRules[10].Id));
        Assert.That(areaRuleTranslations[31].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[31].atr.Name, Is.EqualTo("11. Equipment for drinking water"));
        Assert.That(areaRuleTranslations[32].atr.AreaRuleId, Is.EqualTo(areaRules[10].Id));
        Assert.That(areaRuleTranslations[32].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[32].atr.Name, Is.EqualTo("11. Ausrüstung für Trinkwasser"));
        Assert.That(areaRuleTranslations[33].atr.AreaRuleId, Is.EqualTo(areaRules[11].Id));
        Assert.That(areaRuleTranslations[33].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[33].atr.Name, Is.EqualTo("12. Maskiner til udbringning af husdyrgødning samt doseringsmekanisme- eller dyse"));
        Assert.That(areaRuleTranslations[34].atr.AreaRuleId, Is.EqualTo(areaRules[11].Id));
        Assert.That(areaRuleTranslations[34].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[34].atr.Name, Is.EqualTo("12. Machines for spreading livestock manure and dosing mechanisms or nozzles"));
        Assert.That(areaRuleTranslations[35].atr.AreaRuleId, Is.EqualTo(areaRules[11].Id));
        Assert.That(areaRuleTranslations[35].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[35].atr.Name, Is.EqualTo("12. Maschinen zum Ausbringen von Viehmist und Dosiervorrichtungen oder Düsen"));
        Assert.That(areaRuleTranslations[36].atr.AreaRuleId, Is.EqualTo(areaRules[12].Id));
        Assert.That(areaRuleTranslations[36].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[36].atr.Name, Is.EqualTo("13. Miljøledelse gennemgået og revideret"));
        Assert.That(areaRuleTranslations[37].atr.AreaRuleId, Is.EqualTo(areaRules[12].Id));
        Assert.That(areaRuleTranslations[37].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[37].atr.Name, Is.EqualTo("13. Environmental management reviewed and revised"));
        Assert.That(areaRuleTranslations[38].atr.AreaRuleId, Is.EqualTo(areaRules[12].Id));
        Assert.That(areaRuleTranslations[38].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[38].atr.Name, Is.EqualTo("13. Umweltmanagement überprüft und überarbeitet"));
        Assert.That(areaRuleTranslations[39].atr.AreaRuleId, Is.EqualTo(areaRules[13].Id));
        Assert.That(areaRuleTranslations[39].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[39].atr.Name, Is.EqualTo("14. Beredskabsplan gennemgået og revideret"));
        Assert.That(areaRuleTranslations[40].atr.AreaRuleId, Is.EqualTo(areaRules[13].Id));
        Assert.That(areaRuleTranslations[40].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[40].atr.Name, Is.EqualTo("14. Contingency plan reviewed and revised"));
        Assert.That(areaRuleTranslations[41].atr.AreaRuleId, Is.EqualTo(areaRules[13].Id));
        Assert.That(areaRuleTranslations[41].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[41].atr.Name, Is.EqualTo("14. Notfallplan überprüft und überarbeitet"));

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
        Assert.That(areaRulePlannings[0].AreaRuleId, Is.EqualTo(areaRules[areaRuleNo].Id));
        Assert.That(areaRulePlannings[0].ItemPlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(areaRulePlannings[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRulePlannings[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRulePlannings[0].ComplianceEnabled, Is.EqualTo(true));
        Assert.That(areaRulePlannings[0].SendNotifications, Is.EqualTo(true));

        // Assert plannings
        Assert.That(plannings, Is.Not.Null);
        Assert.That(plannings.Count, Is.EqualTo(1));
        Assert.That(plannings[0].RelatedEFormId, Is.EqualTo(areaRules[areaRuleNo].EformId));
        Assert.That(plannings[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
        Assert.That(plannings[0].SdkFolderId, Is.EqualTo(areaRules[areaRuleNo].FolderId));
        Assert.That(plannings[0].LastExecutedTime, Is.Not.Null);
        Assert.That(plannings[0].LastExecutedTime, Is.EqualTo(new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 0,0,0)));
        var now = DateTime.UtcNow;
        var nextExecutionTime =
            new DateTime(now.Year, 4, 12).AddYears(8);

        Assert.That(plannings[0].NextExecutionTime, Is.EqualTo(nextExecutionTime));
        Assert.That(plannings[0].RepeatEvery, Is.EqualTo(96));
        Assert.That(plannings[0].RepeatType, Is.EqualTo(RepeatType.Month));

        // Assert planningNameTranslations
        Assert.That(planningNameTranslations, Is.Not.Null);
        Assert.That(planningNameTranslations.Count, Is.EqualTo(3));
        Assert.That(planningNameTranslations[0].Name, Is.EqualTo(danishTranslation));
        Assert.That(planningNameTranslations[0].LanguageId, Is.EqualTo(danishLanguage.Id));
        Assert.That(planningNameTranslations[0].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(planningNameTranslations[1].Name, Is.EqualTo(englishTranslation));
        Assert.That(planningNameTranslations[1].LanguageId, Is.EqualTo(englishLanguage.Id));
        Assert.That(planningNameTranslations[1].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(planningNameTranslations[2].Name, Is.EqualTo(germanTranslation));
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
        Assert.That(cases[0].CheckListId, Is.EqualTo(areaRules[areaRuleNo].EformId));
        Assert.That(cases[0].FolderId, Is.Null);
        Assert.That(cases[0].Status, Is.EqualTo(66));
        Assert.That(cases[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
    }

    // Should test the UpdatePlanning method for area rule "00. Logbøger" with repeat type "months" and repeat every "2"
    [Test]
    [TestCase(0, "01. Gyllekøling", "01. Slurry cooling", "01. Schlammkühlung")]
    [TestCase(1, "02. Forsuring", "02. Acidification", "02. Ansäuerung")]
    [TestCase(2, "03. Luftrensning", "03. Air purification", "03. Luftreinigung")]
    [TestCase(3, "04. Beholderkontrol gennemført", "04. Container control completed", "04. Behälterkontrolle abgeschlossen")]
    [TestCase(4, "05. Gyllebeholdere", "05. Slurry containers", "05. Güllebehälter")]
    [TestCase(5, "06. Gyllepumper, - miksere, - seperatorer og spredere", "06. Slurry pumps, - mixers, - separators and spreaders", "06. Schlammpumpen, - Mischer, - Separatoren und Verteiler")]
    [TestCase(6, "07. Forsyningssystemer til vand og foder", "07. Supply systems for water and feed", "07. Versorgungssysteme für Wasser und Futter")]
    [TestCase(7, "08. Varme-, køle- og ventilationssystemer samt temperaturfølere", "08. Heating, cooling and ventilation systems and temperature sensors", "08. Heizungs-, Kühl- und Lüftungssysteme und Temperatursensoren")]
    [TestCase(8, "09. Siloer og transportudstyr", "09. Silos and transport equipment", "09. Silos und Transportgeräte")]
    [TestCase(9, "10. Luftrensningssystemer", "10. Air purification systems", "10. Luftreinigungssysteme")]
    [TestCase(10, "11. Udstyr til drikkevand", "11. Equipment for drinking water", "11. Ausrüstung für Trinkwasser")]
    [TestCase(11, "12. Maskiner til udbringning af husdyrgødning samt doseringsmekanisme- eller dyse", "12. Machines for spreading livestock manure and dosing mechanisms or nozzles", "12. Maschinen zum Ausbringen von Viehmist und Dosiervorrichtungen oder Düsen")]
    [TestCase(12, "13. Miljøledelse gennemgået og revideret", "13. Environmental management reviewed and revised", "13. Umweltmanagement überprüft und überarbeitet")]
    [TestCase(13, "14. Beredskabsplan gennemgået og revideret", "14. Contingency plan reviewed and revised", "14. Notfallplan überprüft und überarbeitet")]
    public async Task UpdatePlanning_AreaRuleMonths108_ReturnsSuccess(int areaRuleNo, string danishTranslation, string englishTranslation, string germanTranslation)
    {
        // Arrange
        var core = await GetCore();
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
            RuleId = areaRules[areaRuleNo].Id,
            ComplianceEnabled = true,
            PropertyId = properties[0].Id,
            Status = true,
            SendNotifications = true,
            StartDate = new DateTime(2023, 4, 12, 0, 0,0),
            UseStartDateAsStartOfPeriod = true,
            TypeSpecificFields = new AreaRuleTypePlanningModel
            {
                Alarm = AreaRuleT2AlarmsEnum.No,
                DayOfMonth = 1,
                DayOfWeek = 0,
                HoursAndEnergyEnabled = false,
                RepeatEvery = 108,
                RepeatType = 3
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
        var englishLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "en-US");
        var germanLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "de-DE");
        var danishLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "da");

        // Assert result
        Assert.NotNull(result);
        Assert.That(result.Success, Is.EqualTo(true));

        // Assert areaRules
        Assert.NotNull(areaRules);
        Assert.That(areaRules.Count, Is.EqualTo(14));
        Assert.That(areaRules[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[1].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[2].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[3].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[4].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[5].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[6].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[7].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[8].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[9].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[10].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[11].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[12].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[13].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[1].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[2].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[3].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[4].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[5].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[6].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[7].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[8].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[9].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[10].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[11].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[12].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[0].EformName, Is.EqualTo("01. Gyllekøling"));
        Assert.That(areaRules[1].EformName, Is.EqualTo("02. Forsuring"));
        Assert.That(areaRules[2].EformName, Is.EqualTo("03. Luftrensning"));
        Assert.That(areaRules[3].EformName, Is.EqualTo("04. Beholderkontrol gennemført"));
        Assert.That(areaRules[4].EformName, Is.EqualTo("05. Gyllebeholdere"));
        Assert.That(areaRules[5].EformName, Is.EqualTo("06. Gyllepumper, - miksere, - seperatorer og spredere"));
        Assert.That(areaRules[6].EformName, Is.EqualTo("07. Forsyningssystemer til vand og foder"));
        Assert.That(areaRules[7].EformName, Is.EqualTo("08. Varme-, køle- og ventilationssystemer samt temperaturfølere"));
        Assert.That(areaRules[8].EformName, Is.EqualTo("09. Siloer og transportudstyr"));
        Assert.That(areaRules[9].EformName, Is.EqualTo("10. Luftrensningssystemer"));
        Assert.That(areaRules[10].EformName, Is.EqualTo("11. Udstyr til drikkevand"));
        Assert.That(areaRules[11].EformName, Is.EqualTo("12. Maskiner til udbringning af husdyrgødning samt doseringsmekanisme- eller dyse"));
        Assert.That(areaRules[12].EformName, Is.EqualTo("13. Miljøledelse"));
        Assert.That(areaRules[13].EformName, Is.EqualTo("14. Beredskabsplan"));

        // Assert areaRuleTranslations
        Assert.NotNull(areaRuleTranslations);
        Assert.That(areaRuleTranslations.Count, Is.EqualTo(42));
        Assert.That(areaRuleTranslations[0].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[0].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[0].atr.Name, Is.EqualTo("01. Gyllekøling"));
        Assert.That(areaRuleTranslations[1].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[1].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[1].atr.Name, Is.EqualTo("01. Slurry cooling"));
        Assert.That(areaRuleTranslations[2].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[2].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[2].atr.Name, Is.EqualTo("01. Schlammkühlung"));
        Assert.That(areaRuleTranslations[3].atr.AreaRuleId, Is.EqualTo(areaRules[1].Id));
        Assert.That(areaRuleTranslations[3].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[3].atr.Name, Is.EqualTo("02. Forsuring"));
        Assert.That(areaRuleTranslations[4].atr.AreaRuleId, Is.EqualTo(areaRules[1].Id));
        Assert.That(areaRuleTranslations[4].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[4].atr.Name, Is.EqualTo("02. Acidification"));
        Assert.That(areaRuleTranslations[5].atr.AreaRuleId, Is.EqualTo(areaRules[1].Id));
        Assert.That(areaRuleTranslations[5].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[5].atr.Name, Is.EqualTo("02. Ansäuerung"));
        Assert.That(areaRuleTranslations[6].atr.AreaRuleId, Is.EqualTo(areaRules[2].Id));
        Assert.That(areaRuleTranslations[6].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[6].atr.Name, Is.EqualTo("03. Luftrensning"));
        Assert.That(areaRuleTranslations[7].atr.AreaRuleId, Is.EqualTo(areaRules[2].Id));
        Assert.That(areaRuleTranslations[7].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[7].atr.Name, Is.EqualTo("03. Air purification"));
        Assert.That(areaRuleTranslations[8].atr.AreaRuleId, Is.EqualTo(areaRules[2].Id));
        Assert.That(areaRuleTranslations[8].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[8].atr.Name, Is.EqualTo("03. Luftreinigung"));
        Assert.That(areaRuleTranslations[9].atr.AreaRuleId, Is.EqualTo(areaRules[3].Id));
        Assert.That(areaRuleTranslations[9].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[9].atr.Name, Is.EqualTo("04. Beholderkontrol gennemført"));
        Assert.That(areaRuleTranslations[10].atr.AreaRuleId, Is.EqualTo(areaRules[3].Id));
        Assert.That(areaRuleTranslations[10].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[10].atr.Name, Is.EqualTo("04. Container control completed"));
        Assert.That(areaRuleTranslations[11].atr.AreaRuleId, Is.EqualTo(areaRules[3].Id));
        Assert.That(areaRuleTranslations[11].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[11].atr.Name, Is.EqualTo("04. Behälterkontrolle abgeschlossen"));
        Assert.That(areaRuleTranslations[12].atr.AreaRuleId, Is.EqualTo(areaRules[4].Id));
        Assert.That(areaRuleTranslations[12].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[12].atr.Name, Is.EqualTo("05. Gyllebeholdere"));
        Assert.That(areaRuleTranslations[13].atr.AreaRuleId, Is.EqualTo(areaRules[4].Id));
        Assert.That(areaRuleTranslations[13].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[13].atr.Name, Is.EqualTo("05. Slurry containers"));
        Assert.That(areaRuleTranslations[14].atr.AreaRuleId, Is.EqualTo(areaRules[4].Id));
        Assert.That(areaRuleTranslations[14].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[14].atr.Name, Is.EqualTo("05. Güllebehälter"));
        Assert.That(areaRuleTranslations[15].atr.AreaRuleId, Is.EqualTo(areaRules[5].Id));
        Assert.That(areaRuleTranslations[15].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[15].atr.Name, Is.EqualTo("06. Gyllepumper, - miksere, - seperatorer og spredere"));
        Assert.That(areaRuleTranslations[16].atr.AreaRuleId, Is.EqualTo(areaRules[5].Id));
        Assert.That(areaRuleTranslations[16].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[16].atr.Name, Is.EqualTo("06. Slurry pumps, - mixers, - separators and spreaders"));
        Assert.That(areaRuleTranslations[17].atr.AreaRuleId, Is.EqualTo(areaRules[5].Id));
        Assert.That(areaRuleTranslations[17].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[17].atr.Name, Is.EqualTo("06. Schlammpumpen, - Mischer, - Separatoren und Verteiler"));
        Assert.That(areaRuleTranslations[18].atr.AreaRuleId, Is.EqualTo(areaRules[6].Id));
        Assert.That(areaRuleTranslations[18].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[18].atr.Name, Is.EqualTo("07. Forsyningssystemer til vand og foder"));
        Assert.That(areaRuleTranslations[19].atr.AreaRuleId, Is.EqualTo(areaRules[6].Id));
        Assert.That(areaRuleTranslations[19].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[19].atr.Name, Is.EqualTo("07. Supply systems for water and feed"));
        Assert.That(areaRuleTranslations[20].atr.AreaRuleId, Is.EqualTo(areaRules[6].Id));
        Assert.That(areaRuleTranslations[20].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[20].atr.Name, Is.EqualTo("07. Versorgungssysteme für Wasser und Futter"));
        Assert.That(areaRuleTranslations[21].atr.AreaRuleId, Is.EqualTo(areaRules[7].Id));
        Assert.That(areaRuleTranslations[21].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[21].atr.Name, Is.EqualTo("08. Varme-, køle- og ventilationssystemer samt temperaturfølere"));
        Assert.That(areaRuleTranslations[22].atr.AreaRuleId, Is.EqualTo(areaRules[7].Id));
        Assert.That(areaRuleTranslations[22].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[22].atr.Name, Is.EqualTo("08. Heating, cooling and ventilation systems and temperature sensors"));
        Assert.That(areaRuleTranslations[23].atr.AreaRuleId, Is.EqualTo(areaRules[7].Id));
        Assert.That(areaRuleTranslations[23].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[23].atr.Name, Is.EqualTo("08. Heizungs-, Kühl- und Lüftungssysteme und Temperatursensoren"));
        Assert.That(areaRuleTranslations[24].atr.AreaRuleId, Is.EqualTo(areaRules[8].Id));
        Assert.That(areaRuleTranslations[24].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[24].atr.Name, Is.EqualTo("09. Siloer og transportudstyr"));
        Assert.That(areaRuleTranslations[25].atr.AreaRuleId, Is.EqualTo(areaRules[8].Id));
        Assert.That(areaRuleTranslations[25].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[25].atr.Name, Is.EqualTo("09. Silos and transport equipment"));
        Assert.That(areaRuleTranslations[26].atr.AreaRuleId, Is.EqualTo(areaRules[8].Id));
        Assert.That(areaRuleTranslations[26].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[26].atr.Name, Is.EqualTo("09. Silos und Transportgeräte"));
        Assert.That(areaRuleTranslations[27].atr.AreaRuleId, Is.EqualTo(areaRules[9].Id));
        Assert.That(areaRuleTranslations[27].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[27].atr.Name, Is.EqualTo("10. Luftrensningssystemer"));
        Assert.That(areaRuleTranslations[28].atr.AreaRuleId, Is.EqualTo(areaRules[9].Id));
        Assert.That(areaRuleTranslations[28].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[28].atr.Name, Is.EqualTo("10. Air purification systems"));
        Assert.That(areaRuleTranslations[29].atr.AreaRuleId, Is.EqualTo(areaRules[9].Id));
        Assert.That(areaRuleTranslations[29].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[29].atr.Name, Is.EqualTo("10. Luftreinigungssysteme"));
        Assert.That(areaRuleTranslations[30].atr.AreaRuleId, Is.EqualTo(areaRules[10].Id));
        Assert.That(areaRuleTranslations[30].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[30].atr.Name, Is.EqualTo("11. Udstyr til drikkevand"));
        Assert.That(areaRuleTranslations[31].atr.AreaRuleId, Is.EqualTo(areaRules[10].Id));
        Assert.That(areaRuleTranslations[31].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[31].atr.Name, Is.EqualTo("11. Equipment for drinking water"));
        Assert.That(areaRuleTranslations[32].atr.AreaRuleId, Is.EqualTo(areaRules[10].Id));
        Assert.That(areaRuleTranslations[32].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[32].atr.Name, Is.EqualTo("11. Ausrüstung für Trinkwasser"));
        Assert.That(areaRuleTranslations[33].atr.AreaRuleId, Is.EqualTo(areaRules[11].Id));
        Assert.That(areaRuleTranslations[33].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[33].atr.Name, Is.EqualTo("12. Maskiner til udbringning af husdyrgødning samt doseringsmekanisme- eller dyse"));
        Assert.That(areaRuleTranslations[34].atr.AreaRuleId, Is.EqualTo(areaRules[11].Id));
        Assert.That(areaRuleTranslations[34].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[34].atr.Name, Is.EqualTo("12. Machines for spreading livestock manure and dosing mechanisms or nozzles"));
        Assert.That(areaRuleTranslations[35].atr.AreaRuleId, Is.EqualTo(areaRules[11].Id));
        Assert.That(areaRuleTranslations[35].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[35].atr.Name, Is.EqualTo("12. Maschinen zum Ausbringen von Viehmist und Dosiervorrichtungen oder Düsen"));
        Assert.That(areaRuleTranslations[36].atr.AreaRuleId, Is.EqualTo(areaRules[12].Id));
        Assert.That(areaRuleTranslations[36].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[36].atr.Name, Is.EqualTo("13. Miljøledelse gennemgået og revideret"));
        Assert.That(areaRuleTranslations[37].atr.AreaRuleId, Is.EqualTo(areaRules[12].Id));
        Assert.That(areaRuleTranslations[37].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[37].atr.Name, Is.EqualTo("13. Environmental management reviewed and revised"));
        Assert.That(areaRuleTranslations[38].atr.AreaRuleId, Is.EqualTo(areaRules[12].Id));
        Assert.That(areaRuleTranslations[38].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[38].atr.Name, Is.EqualTo("13. Umweltmanagement überprüft und überarbeitet"));
        Assert.That(areaRuleTranslations[39].atr.AreaRuleId, Is.EqualTo(areaRules[13].Id));
        Assert.That(areaRuleTranslations[39].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[39].atr.Name, Is.EqualTo("14. Beredskabsplan gennemgået og revideret"));
        Assert.That(areaRuleTranslations[40].atr.AreaRuleId, Is.EqualTo(areaRules[13].Id));
        Assert.That(areaRuleTranslations[40].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[40].atr.Name, Is.EqualTo("14. Contingency plan reviewed and revised"));
        Assert.That(areaRuleTranslations[41].atr.AreaRuleId, Is.EqualTo(areaRules[13].Id));
        Assert.That(areaRuleTranslations[41].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[41].atr.Name, Is.EqualTo("14. Notfallplan überprüft und überarbeitet"));

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
        Assert.That(areaRulePlannings[0].AreaRuleId, Is.EqualTo(areaRules[areaRuleNo].Id));
        Assert.That(areaRulePlannings[0].ItemPlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(areaRulePlannings[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRulePlannings[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRulePlannings[0].ComplianceEnabled, Is.EqualTo(true));
        Assert.That(areaRulePlannings[0].SendNotifications, Is.EqualTo(true));

        // Assert plannings
        Assert.That(plannings, Is.Not.Null);
        Assert.That(plannings.Count, Is.EqualTo(1));
        Assert.That(plannings[0].RelatedEFormId, Is.EqualTo(areaRules[areaRuleNo].EformId));
        Assert.That(plannings[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
        Assert.That(plannings[0].SdkFolderId, Is.EqualTo(areaRules[areaRuleNo].FolderId));
        Assert.That(plannings[0].LastExecutedTime, Is.Not.Null);
        Assert.That(plannings[0].LastExecutedTime, Is.EqualTo(new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 0,0,0)));
        var now = DateTime.UtcNow;
        var nextExecutionTime =
            new DateTime(now.Year, 4, 12).AddYears(9);

        Assert.That(plannings[0].NextExecutionTime, Is.EqualTo(nextExecutionTime));
        Assert.That(plannings[0].RepeatEvery, Is.EqualTo(108));
        Assert.That(plannings[0].RepeatType, Is.EqualTo(RepeatType.Month));

        // Assert planningNameTranslations
        Assert.That(planningNameTranslations, Is.Not.Null);
        Assert.That(planningNameTranslations.Count, Is.EqualTo(3));
        Assert.That(planningNameTranslations[0].Name, Is.EqualTo(danishTranslation));
        Assert.That(planningNameTranslations[0].LanguageId, Is.EqualTo(danishLanguage.Id));
        Assert.That(planningNameTranslations[0].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(planningNameTranslations[1].Name, Is.EqualTo(englishTranslation));
        Assert.That(planningNameTranslations[1].LanguageId, Is.EqualTo(englishLanguage.Id));
        Assert.That(planningNameTranslations[1].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(planningNameTranslations[2].Name, Is.EqualTo(germanTranslation));
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
        Assert.That(cases[0].CheckListId, Is.EqualTo(areaRules[areaRuleNo].EformId));
        Assert.That(cases[0].FolderId, Is.Null);
        Assert.That(cases[0].Status, Is.EqualTo(66));
        Assert.That(cases[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
    }

    // Should test the UpdatePlanning method for area rule "00. Logbøger" with repeat type "months" and repeat every "2"
    [Test]
    [TestCase(0, "01. Gyllekøling", "01. Slurry cooling", "01. Schlammkühlung")]
    [TestCase(1, "02. Forsuring", "02. Acidification", "02. Ansäuerung")]
    [TestCase(2, "03. Luftrensning", "03. Air purification", "03. Luftreinigung")]
    [TestCase(3, "04. Beholderkontrol gennemført", "04. Container control completed", "04. Behälterkontrolle abgeschlossen")]
    [TestCase(4, "05. Gyllebeholdere", "05. Slurry containers", "05. Güllebehälter")]
    [TestCase(5, "06. Gyllepumper, - miksere, - seperatorer og spredere", "06. Slurry pumps, - mixers, - separators and spreaders", "06. Schlammpumpen, - Mischer, - Separatoren und Verteiler")]
    [TestCase(6, "07. Forsyningssystemer til vand og foder", "07. Supply systems for water and feed", "07. Versorgungssysteme für Wasser und Futter")]
    [TestCase(7, "08. Varme-, køle- og ventilationssystemer samt temperaturfølere", "08. Heating, cooling and ventilation systems and temperature sensors", "08. Heizungs-, Kühl- und Lüftungssysteme und Temperatursensoren")]
    [TestCase(8, "09. Siloer og transportudstyr", "09. Silos and transport equipment", "09. Silos und Transportgeräte")]
    [TestCase(9, "10. Luftrensningssystemer", "10. Air purification systems", "10. Luftreinigungssysteme")]
    [TestCase(10, "11. Udstyr til drikkevand", "11. Equipment for drinking water", "11. Ausrüstung für Trinkwasser")]
    [TestCase(11, "12. Maskiner til udbringning af husdyrgødning samt doseringsmekanisme- eller dyse", "12. Machines for spreading livestock manure and dosing mechanisms or nozzles", "12. Maschinen zum Ausbringen von Viehmist und Dosiervorrichtungen oder Düsen")]
    [TestCase(12, "13. Miljøledelse gennemgået og revideret", "13. Environmental management reviewed and revised", "13. Umweltmanagement überprüft und überarbeitet")]
    [TestCase(13, "14. Beredskabsplan gennemgået og revideret", "14. Contingency plan reviewed and revised", "14. Notfallplan überprüft und überarbeitet")]
    public async Task UpdatePlanning_AreaRuleMonths120_ReturnsSuccess(int areaRuleNo, string danishTranslation, string englishTranslation, string germanTranslation)
    {
        // Arrange
        var core = await GetCore();
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
            RuleId = areaRules[areaRuleNo].Id,
            ComplianceEnabled = true,
            PropertyId = properties[0].Id,
            Status = true,
            SendNotifications = true,
            StartDate = new DateTime(2023, 4, 12, 0, 0,0),
            UseStartDateAsStartOfPeriod = true,
            TypeSpecificFields = new AreaRuleTypePlanningModel
            {
                Alarm = AreaRuleT2AlarmsEnum.No,
                DayOfMonth = 1,
                DayOfWeek = 0,
                HoursAndEnergyEnabled = false,
                RepeatEvery = 120,
                RepeatType = 3
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
        var englishLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "en-US");
        var germanLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "de-DE");
        var danishLanguage = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "da");

        // Assert result
        Assert.NotNull(result);
        Assert.That(result.Success, Is.EqualTo(true));

        // Assert areaRules
        Assert.NotNull(areaRules);
        Assert.That(areaRules.Count, Is.EqualTo(14));
        Assert.That(areaRules[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[1].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[2].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[3].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[4].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[5].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[6].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[7].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[8].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[9].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[10].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[11].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[12].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[13].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRules[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[1].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[2].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[3].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[4].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[5].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[6].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[7].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[8].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[9].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[10].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[11].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[12].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[0].EformName, Is.EqualTo("01. Gyllekøling"));
        Assert.That(areaRules[1].EformName, Is.EqualTo("02. Forsuring"));
        Assert.That(areaRules[2].EformName, Is.EqualTo("03. Luftrensning"));
        Assert.That(areaRules[3].EformName, Is.EqualTo("04. Beholderkontrol gennemført"));
        Assert.That(areaRules[4].EformName, Is.EqualTo("05. Gyllebeholdere"));
        Assert.That(areaRules[5].EformName, Is.EqualTo("06. Gyllepumper, - miksere, - seperatorer og spredere"));
        Assert.That(areaRules[6].EformName, Is.EqualTo("07. Forsyningssystemer til vand og foder"));
        Assert.That(areaRules[7].EformName, Is.EqualTo("08. Varme-, køle- og ventilationssystemer samt temperaturfølere"));
        Assert.That(areaRules[8].EformName, Is.EqualTo("09. Siloer og transportudstyr"));
        Assert.That(areaRules[9].EformName, Is.EqualTo("10. Luftrensningssystemer"));
        Assert.That(areaRules[10].EformName, Is.EqualTo("11. Udstyr til drikkevand"));
        Assert.That(areaRules[11].EformName, Is.EqualTo("12. Maskiner til udbringning af husdyrgødning samt doseringsmekanisme- eller dyse"));
        Assert.That(areaRules[12].EformName, Is.EqualTo("13. Miljøledelse"));
        Assert.That(areaRules[13].EformName, Is.EqualTo("14. Beredskabsplan"));

        // Assert areaRuleTranslations
        Assert.NotNull(areaRuleTranslations);
        Assert.That(areaRuleTranslations.Count, Is.EqualTo(42));
        Assert.That(areaRuleTranslations[0].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[0].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[0].atr.Name, Is.EqualTo("01. Gyllekøling"));
        Assert.That(areaRuleTranslations[1].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[1].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[1].atr.Name, Is.EqualTo("01. Slurry cooling"));
        Assert.That(areaRuleTranslations[2].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        Assert.That(areaRuleTranslations[2].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[2].atr.Name, Is.EqualTo("01. Schlammkühlung"));
        Assert.That(areaRuleTranslations[3].atr.AreaRuleId, Is.EqualTo(areaRules[1].Id));
        Assert.That(areaRuleTranslations[3].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[3].atr.Name, Is.EqualTo("02. Forsuring"));
        Assert.That(areaRuleTranslations[4].atr.AreaRuleId, Is.EqualTo(areaRules[1].Id));
        Assert.That(areaRuleTranslations[4].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[4].atr.Name, Is.EqualTo("02. Acidification"));
        Assert.That(areaRuleTranslations[5].atr.AreaRuleId, Is.EqualTo(areaRules[1].Id));
        Assert.That(areaRuleTranslations[5].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[5].atr.Name, Is.EqualTo("02. Ansäuerung"));
        Assert.That(areaRuleTranslations[6].atr.AreaRuleId, Is.EqualTo(areaRules[2].Id));
        Assert.That(areaRuleTranslations[6].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[6].atr.Name, Is.EqualTo("03. Luftrensning"));
        Assert.That(areaRuleTranslations[7].atr.AreaRuleId, Is.EqualTo(areaRules[2].Id));
        Assert.That(areaRuleTranslations[7].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[7].atr.Name, Is.EqualTo("03. Air purification"));
        Assert.That(areaRuleTranslations[8].atr.AreaRuleId, Is.EqualTo(areaRules[2].Id));
        Assert.That(areaRuleTranslations[8].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[8].atr.Name, Is.EqualTo("03. Luftreinigung"));
        Assert.That(areaRuleTranslations[9].atr.AreaRuleId, Is.EqualTo(areaRules[3].Id));
        Assert.That(areaRuleTranslations[9].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[9].atr.Name, Is.EqualTo("04. Beholderkontrol gennemført"));
        Assert.That(areaRuleTranslations[10].atr.AreaRuleId, Is.EqualTo(areaRules[3].Id));
        Assert.That(areaRuleTranslations[10].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[10].atr.Name, Is.EqualTo("04. Container control completed"));
        Assert.That(areaRuleTranslations[11].atr.AreaRuleId, Is.EqualTo(areaRules[3].Id));
        Assert.That(areaRuleTranslations[11].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[11].atr.Name, Is.EqualTo("04. Behälterkontrolle abgeschlossen"));
        Assert.That(areaRuleTranslations[12].atr.AreaRuleId, Is.EqualTo(areaRules[4].Id));
        Assert.That(areaRuleTranslations[12].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[12].atr.Name, Is.EqualTo("05. Gyllebeholdere"));
        Assert.That(areaRuleTranslations[13].atr.AreaRuleId, Is.EqualTo(areaRules[4].Id));
        Assert.That(areaRuleTranslations[13].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[13].atr.Name, Is.EqualTo("05. Slurry containers"));
        Assert.That(areaRuleTranslations[14].atr.AreaRuleId, Is.EqualTo(areaRules[4].Id));
        Assert.That(areaRuleTranslations[14].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[14].atr.Name, Is.EqualTo("05. Güllebehälter"));
        Assert.That(areaRuleTranslations[15].atr.AreaRuleId, Is.EqualTo(areaRules[5].Id));
        Assert.That(areaRuleTranslations[15].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[15].atr.Name, Is.EqualTo("06. Gyllepumper, - miksere, - seperatorer og spredere"));
        Assert.That(areaRuleTranslations[16].atr.AreaRuleId, Is.EqualTo(areaRules[5].Id));
        Assert.That(areaRuleTranslations[16].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[16].atr.Name, Is.EqualTo("06. Slurry pumps, - mixers, - separators and spreaders"));
        Assert.That(areaRuleTranslations[17].atr.AreaRuleId, Is.EqualTo(areaRules[5].Id));
        Assert.That(areaRuleTranslations[17].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[17].atr.Name, Is.EqualTo("06. Schlammpumpen, - Mischer, - Separatoren und Verteiler"));
        Assert.That(areaRuleTranslations[18].atr.AreaRuleId, Is.EqualTo(areaRules[6].Id));
        Assert.That(areaRuleTranslations[18].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[18].atr.Name, Is.EqualTo("07. Forsyningssystemer til vand og foder"));
        Assert.That(areaRuleTranslations[19].atr.AreaRuleId, Is.EqualTo(areaRules[6].Id));
        Assert.That(areaRuleTranslations[19].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[19].atr.Name, Is.EqualTo("07. Supply systems for water and feed"));
        Assert.That(areaRuleTranslations[20].atr.AreaRuleId, Is.EqualTo(areaRules[6].Id));
        Assert.That(areaRuleTranslations[20].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[20].atr.Name, Is.EqualTo("07. Versorgungssysteme für Wasser und Futter"));
        Assert.That(areaRuleTranslations[21].atr.AreaRuleId, Is.EqualTo(areaRules[7].Id));
        Assert.That(areaRuleTranslations[21].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[21].atr.Name, Is.EqualTo("08. Varme-, køle- og ventilationssystemer samt temperaturfølere"));
        Assert.That(areaRuleTranslations[22].atr.AreaRuleId, Is.EqualTo(areaRules[7].Id));
        Assert.That(areaRuleTranslations[22].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[22].atr.Name, Is.EqualTo("08. Heating, cooling and ventilation systems and temperature sensors"));
        Assert.That(areaRuleTranslations[23].atr.AreaRuleId, Is.EqualTo(areaRules[7].Id));
        Assert.That(areaRuleTranslations[23].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[23].atr.Name, Is.EqualTo("08. Heizungs-, Kühl- und Lüftungssysteme und Temperatursensoren"));
        Assert.That(areaRuleTranslations[24].atr.AreaRuleId, Is.EqualTo(areaRules[8].Id));
        Assert.That(areaRuleTranslations[24].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[24].atr.Name, Is.EqualTo("09. Siloer og transportudstyr"));
        Assert.That(areaRuleTranslations[25].atr.AreaRuleId, Is.EqualTo(areaRules[8].Id));
        Assert.That(areaRuleTranslations[25].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[25].atr.Name, Is.EqualTo("09. Silos and transport equipment"));
        Assert.That(areaRuleTranslations[26].atr.AreaRuleId, Is.EqualTo(areaRules[8].Id));
        Assert.That(areaRuleTranslations[26].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[26].atr.Name, Is.EqualTo("09. Silos und Transportgeräte"));
        Assert.That(areaRuleTranslations[27].atr.AreaRuleId, Is.EqualTo(areaRules[9].Id));
        Assert.That(areaRuleTranslations[27].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[27].atr.Name, Is.EqualTo("10. Luftrensningssystemer"));
        Assert.That(areaRuleTranslations[28].atr.AreaRuleId, Is.EqualTo(areaRules[9].Id));
        Assert.That(areaRuleTranslations[28].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[28].atr.Name, Is.EqualTo("10. Air purification systems"));
        Assert.That(areaRuleTranslations[29].atr.AreaRuleId, Is.EqualTo(areaRules[9].Id));
        Assert.That(areaRuleTranslations[29].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[29].atr.Name, Is.EqualTo("10. Luftreinigungssysteme"));
        Assert.That(areaRuleTranslations[30].atr.AreaRuleId, Is.EqualTo(areaRules[10].Id));
        Assert.That(areaRuleTranslations[30].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[30].atr.Name, Is.EqualTo("11. Udstyr til drikkevand"));
        Assert.That(areaRuleTranslations[31].atr.AreaRuleId, Is.EqualTo(areaRules[10].Id));
        Assert.That(areaRuleTranslations[31].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[31].atr.Name, Is.EqualTo("11. Equipment for drinking water"));
        Assert.That(areaRuleTranslations[32].atr.AreaRuleId, Is.EqualTo(areaRules[10].Id));
        Assert.That(areaRuleTranslations[32].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[32].atr.Name, Is.EqualTo("11. Ausrüstung für Trinkwasser"));
        Assert.That(areaRuleTranslations[33].atr.AreaRuleId, Is.EqualTo(areaRules[11].Id));
        Assert.That(areaRuleTranslations[33].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[33].atr.Name, Is.EqualTo("12. Maskiner til udbringning af husdyrgødning samt doseringsmekanisme- eller dyse"));
        Assert.That(areaRuleTranslations[34].atr.AreaRuleId, Is.EqualTo(areaRules[11].Id));
        Assert.That(areaRuleTranslations[34].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[34].atr.Name, Is.EqualTo("12. Machines for spreading livestock manure and dosing mechanisms or nozzles"));
        Assert.That(areaRuleTranslations[35].atr.AreaRuleId, Is.EqualTo(areaRules[11].Id));
        Assert.That(areaRuleTranslations[35].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[35].atr.Name, Is.EqualTo("12. Maschinen zum Ausbringen von Viehmist und Dosiervorrichtungen oder Düsen"));
        Assert.That(areaRuleTranslations[36].atr.AreaRuleId, Is.EqualTo(areaRules[12].Id));
        Assert.That(areaRuleTranslations[36].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[36].atr.Name, Is.EqualTo("13. Miljøledelse gennemgået og revideret"));
        Assert.That(areaRuleTranslations[37].atr.AreaRuleId, Is.EqualTo(areaRules[12].Id));
        Assert.That(areaRuleTranslations[37].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[37].atr.Name, Is.EqualTo("13. Environmental management reviewed and revised"));
        Assert.That(areaRuleTranslations[38].atr.AreaRuleId, Is.EqualTo(areaRules[12].Id));
        Assert.That(areaRuleTranslations[38].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[38].atr.Name, Is.EqualTo("13. Umweltmanagement überprüft und überarbeitet"));
        Assert.That(areaRuleTranslations[39].atr.AreaRuleId, Is.EqualTo(areaRules[13].Id));
        Assert.That(areaRuleTranslations[39].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[39].atr.Name, Is.EqualTo("14. Beredskabsplan gennemgået og revideret"));
        Assert.That(areaRuleTranslations[40].atr.AreaRuleId, Is.EqualTo(areaRules[13].Id));
        Assert.That(areaRuleTranslations[40].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[40].atr.Name, Is.EqualTo("14. Contingency plan reviewed and revised"));
        Assert.That(areaRuleTranslations[41].atr.AreaRuleId, Is.EqualTo(areaRules[13].Id));
        Assert.That(areaRuleTranslations[41].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[41].atr.Name, Is.EqualTo("14. Notfallplan überprüft und überarbeitet"));

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
        Assert.That(areaRulePlannings[0].AreaRuleId, Is.EqualTo(areaRules[areaRuleNo].Id));
        Assert.That(areaRulePlannings[0].ItemPlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(areaRulePlannings[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRulePlannings[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRulePlannings[0].ComplianceEnabled, Is.EqualTo(true));
        Assert.That(areaRulePlannings[0].SendNotifications, Is.EqualTo(true));

        // Assert plannings
        Assert.That(plannings, Is.Not.Null);
        Assert.That(plannings.Count, Is.EqualTo(1));
        Assert.That(plannings[0].RelatedEFormId, Is.EqualTo(areaRules[areaRuleNo].EformId));
        Assert.That(plannings[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
        Assert.That(plannings[0].SdkFolderId, Is.EqualTo(areaRules[areaRuleNo].FolderId));
        Assert.That(plannings[0].LastExecutedTime, Is.Not.Null);
        Assert.That(plannings[0].LastExecutedTime, Is.EqualTo(new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 0,0,0)));
        var now = DateTime.UtcNow;
        var nextExecutionTime =
            new DateTime(now.Year, 4, 12).AddYears(10);

        Assert.That(plannings[0].NextExecutionTime, Is.EqualTo(nextExecutionTime));
        Assert.That(plannings[0].RepeatEvery, Is.EqualTo(120));
        Assert.That(plannings[0].RepeatType, Is.EqualTo(RepeatType.Month));

        // Assert planningNameTranslations
        Assert.That(planningNameTranslations, Is.Not.Null);
        Assert.That(planningNameTranslations.Count, Is.EqualTo(3));
        Assert.That(planningNameTranslations[0].Name, Is.EqualTo(danishTranslation));
        Assert.That(planningNameTranslations[0].LanguageId, Is.EqualTo(danishLanguage.Id));
        Assert.That(planningNameTranslations[0].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(planningNameTranslations[1].Name, Is.EqualTo(englishTranslation));
        Assert.That(planningNameTranslations[1].LanguageId, Is.EqualTo(englishLanguage.Id));
        Assert.That(planningNameTranslations[1].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(planningNameTranslations[2].Name, Is.EqualTo(germanTranslation));
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
        Assert.That(cases[0].CheckListId, Is.EqualTo(areaRules[areaRuleNo].EformId));
        Assert.That(cases[0].FolderId, Is.Null);
        Assert.That(cases[0].Status, Is.EqualTo(66));
        Assert.That(cases[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
    }

}