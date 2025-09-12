using BackendConfiguration.Pn.Infrastructure.Helpers;
using BackendConfiguration.Pn.Infrastructure.Models.Properties;
using BackendConfiguration.Pn.Infrastructure.Models.PropertyAreas;
using Microsoft.EntityFrameworkCore;

namespace BackendConfiguration.Pn.Integration.Test;

[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class BackendConfigurationPropertyAreasServiceHelperTest : TestBaseSetup
{
    // Should test the Update method and enable "01. Logbøger" and return success
    [Test]
    public async Task BackendConfigurationPropertyAreasServiceHelper_Update_LogBooks_ReturnsSuccess()
    {
        // Arrange
        var propertyCreateModel = new PropertyCreateModel
        {
            Address = Guid.NewGuid().ToString(),
            Chr = Guid.NewGuid().ToString(),
            IndustryCode = Guid.NewGuid().ToString(),
            Cvr = Guid.NewGuid().ToString(),
            IsFarm = true,
            LanguagesIds = [1],
            MainMailAddress = Guid.NewGuid().ToString(),
            Name = Guid.NewGuid().ToString(),
            WorkorderEnable = false
        };

        var core = await GetCore();

        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1, BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, 1, 1);

        var property = await BackendConfigurationPnDbContext!.Properties.FirstAsync(x => x.Name == propertyCreateModel.Name);
        var areaTranslation = await BackendConfigurationPnDbContext!.AreaTranslations.FirstAsync(x => x.Name == "00. Logbøger");

        var propertyAreasUpdateModel = new PropertyAreasUpdateModel
        {
            Areas =
            [
                new()
                {
                    AreaId = areaTranslation.AreaId,
                    Activated = true
                }
            ],
            PropertyId = property.Id
        };

        // Act
        var result = await BackendConfigurationPropertyAreasServiceHelper.Update(propertyAreasUpdateModel, core, BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, 1);

        // Assert
        var areaRules = await BackendConfigurationPnDbContext!.AreaRules.Where(x => x.PropertyId == property.Id).ToListAsync();
        var areaRuleTranslations = await BackendConfigurationPnDbContext!.AreaRuleTranslations
            .Join(BackendConfigurationPnDbContext.AreaRules, atr => atr.AreaRuleId, ar => ar.Id,
                (atr, ar) => new { atr, ar }).Where(x => x.ar.PropertyId == property.Id).ToListAsync();
        var areaProperties = await BackendConfigurationPnDbContext!.AreaProperties.Where(x => x.PropertyId == property.Id).ToListAsync();
        var folderTranslations = await MicrotingDbContext!.FolderTranslations.ToListAsync();

        // Assert result
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.EqualTo(true));

        // Assert areaRules
        Assert.That(areaRules, Is.Not.Null);
        Assert.That(areaRules.Count, Is.EqualTo(0));
        // Assert.That(areaRules[0].PropertyId, Is.EqualTo(property.Id));
        // Assert.That(areaRules[1].PropertyId, Is.EqualTo(property.Id));
        // Assert.That(areaRules[2].PropertyId, Is.EqualTo(property.Id));
        // Assert.That(areaRules[3].PropertyId, Is.EqualTo(property.Id));
        // Assert.That(areaRules[4].PropertyId, Is.EqualTo(property.Id));
        // Assert.That(areaRules[5].PropertyId, Is.EqualTo(property.Id));
        // Assert.That(areaRules[6].PropertyId, Is.EqualTo(property.Id));
        // Assert.That(areaRules[7].PropertyId, Is.EqualTo(property.Id));
        // Assert.That(areaRules[8].PropertyId, Is.EqualTo(property.Id));
        // Assert.That(areaRules[9].PropertyId, Is.EqualTo(property.Id));
        // Assert.That(areaRules[10].PropertyId, Is.EqualTo(property.Id));
        // Assert.That(areaRules[11].PropertyId, Is.EqualTo(property.Id));
        // Assert.That(areaRules[12].PropertyId, Is.EqualTo(property.Id));
        // Assert.That(areaRules[13].PropertyId, Is.EqualTo(property.Id));
        // Assert.That(areaRules[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
        // Assert.That(areaRules[1].AreaId, Is.EqualTo(areaTranslation.AreaId));
        // Assert.That(areaRules[2].AreaId, Is.EqualTo(areaTranslation.AreaId));
        // Assert.That(areaRules[3].AreaId, Is.EqualTo(areaTranslation.AreaId));
        // Assert.That(areaRules[4].AreaId, Is.EqualTo(areaTranslation.AreaId));
        // Assert.That(areaRules[5].AreaId, Is.EqualTo(areaTranslation.AreaId));
        // Assert.That(areaRules[6].AreaId, Is.EqualTo(areaTranslation.AreaId));
        // Assert.That(areaRules[7].AreaId, Is.EqualTo(areaTranslation.AreaId));
        // Assert.That(areaRules[8].AreaId, Is.EqualTo(areaTranslation.AreaId));
        // Assert.That(areaRules[9].AreaId, Is.EqualTo(areaTranslation.AreaId));
        // Assert.That(areaRules[10].AreaId, Is.EqualTo(areaTranslation.AreaId));
        // Assert.That(areaRules[11].AreaId, Is.EqualTo(areaTranslation.AreaId));
        // Assert.That(areaRules[12].AreaId, Is.EqualTo(areaTranslation.AreaId));
        // Assert.That(areaRules[0].EformName, Is.EqualTo("01. Gyllekøling"));
        // Assert.That(areaRules[1].EformName, Is.EqualTo("02. Forsuring"));
        // Assert.That(areaRules[2].EformName, Is.EqualTo("03. Luftrensning"));
        // Assert.That(areaRules[3].EformName, Is.EqualTo("04. Beholderkontrol gennemført"));
        // Assert.That(areaRules[4].EformName, Is.EqualTo("05. Gyllebeholdere"));
        // Assert.That(areaRules[5].EformName, Is.EqualTo("06. Gyllepumper, - miksere, - seperatorer og spredere"));
        // Assert.That(areaRules[6].EformName, Is.EqualTo("07. Forsyningssystemer til vand og foder"));
        // Assert.That(areaRules[7].EformName, Is.EqualTo("08. Varme-, køle- og ventilationssystemer samt temperaturfølere"));
        // Assert.That(areaRules[8].EformName, Is.EqualTo("09. Siloer og transportudstyr"));
        // Assert.That(areaRules[9].EformName, Is.EqualTo("10. Luftrensningssystemer"));
        // Assert.That(areaRules[10].EformName, Is.EqualTo("11. Udstyr til drikkevand"));
        // Assert.That(areaRules[11].EformName, Is.EqualTo("12. Maskiner til udbringning af husdyrgødning samt doseringsmekanisme- eller dyse"));
        // Assert.That(areaRules[12].EformName, Is.EqualTo("13. Miljøledelse"));
        // Assert.That(areaRules[13].EformName, Is.EqualTo("14. Beredskabsplan"));

        // Assert areaRuleTranslations
        Assert.That(areaRuleTranslations, Is.Not.Null);
        Assert.That(areaRuleTranslations.Count, Is.EqualTo(0));
        // Assert.That(areaRuleTranslations[0].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        // Assert.That(areaRuleTranslations[0].atr.LanguageId, Is.EqualTo(1));
        // Assert.That(areaRuleTranslations[0].atr.Name, Is.EqualTo("01. Gyllekøling"));
        // Assert.That(areaRuleTranslations[1].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        // Assert.That(areaRuleTranslations[1].atr.LanguageId, Is.EqualTo(2));
        // Assert.That(areaRuleTranslations[1].atr.Name, Is.EqualTo("01. Slurry cooling"));
        // Assert.That(areaRuleTranslations[2].atr.AreaRuleId, Is.EqualTo(areaRules[0].Id));
        // Assert.That(areaRuleTranslations[2].atr.LanguageId, Is.EqualTo(3));
        // Assert.That(areaRuleTranslations[2].atr.Name, Is.EqualTo("01. Schlammkühlung"));
        // Assert.That(areaRuleTranslations[3].atr.AreaRuleId, Is.EqualTo(areaRules[1].Id));
        // Assert.That(areaRuleTranslations[3].atr.LanguageId, Is.EqualTo(1));
        // Assert.That(areaRuleTranslations[3].atr.Name, Is.EqualTo("02. Forsuring"));
        // Assert.That(areaRuleTranslations[4].atr.AreaRuleId, Is.EqualTo(areaRules[1].Id));
        // Assert.That(areaRuleTranslations[4].atr.LanguageId, Is.EqualTo(2));
        // Assert.That(areaRuleTranslations[4].atr.Name, Is.EqualTo("02. Acidification"));
        // Assert.That(areaRuleTranslations[5].atr.AreaRuleId, Is.EqualTo(areaRules[1].Id));
        // Assert.That(areaRuleTranslations[5].atr.LanguageId, Is.EqualTo(3));
        // Assert.That(areaRuleTranslations[5].atr.Name, Is.EqualTo("02. Ansäuerung"));
        // Assert.That(areaRuleTranslations[6].atr.AreaRuleId, Is.EqualTo(areaRules[2].Id));
        // Assert.That(areaRuleTranslations[6].atr.LanguageId, Is.EqualTo(1));
        // Assert.That(areaRuleTranslations[6].atr.Name, Is.EqualTo("03. Luftrensning"));
        // Assert.That(areaRuleTranslations[7].atr.AreaRuleId, Is.EqualTo(areaRules[2].Id));
        // Assert.That(areaRuleTranslations[7].atr.LanguageId, Is.EqualTo(2));
        // Assert.That(areaRuleTranslations[7].atr.Name, Is.EqualTo("03. Air purification"));
        // Assert.That(areaRuleTranslations[8].atr.AreaRuleId, Is.EqualTo(areaRules[2].Id));
        // Assert.That(areaRuleTranslations[8].atr.LanguageId, Is.EqualTo(3));
        // Assert.That(areaRuleTranslations[8].atr.Name, Is.EqualTo("03. Luftreinigung"));
        // Assert.That(areaRuleTranslations[9].atr.AreaRuleId, Is.EqualTo(areaRules[3].Id));
        // Assert.That(areaRuleTranslations[9].atr.LanguageId, Is.EqualTo(1));
        // Assert.That(areaRuleTranslations[9].atr.Name, Is.EqualTo("04. Beholderkontrol gennemført"));
        // Assert.That(areaRuleTranslations[10].atr.AreaRuleId, Is.EqualTo(areaRules[3].Id));
        // Assert.That(areaRuleTranslations[10].atr.LanguageId, Is.EqualTo(2));
        // Assert.That(areaRuleTranslations[10].atr.Name, Is.EqualTo("04. Container control completed"));
        // Assert.That(areaRuleTranslations[11].atr.AreaRuleId, Is.EqualTo(areaRules[3].Id));
        // Assert.That(areaRuleTranslations[11].atr.LanguageId, Is.EqualTo(3));
        // Assert.That(areaRuleTranslations[11].atr.Name, Is.EqualTo("04. Behälterkontrolle abgeschlossen"));
        // Assert.That(areaRuleTranslations[12].atr.AreaRuleId, Is.EqualTo(areaRules[4].Id));
        // Assert.That(areaRuleTranslations[12].atr.LanguageId, Is.EqualTo(1));
        // Assert.That(areaRuleTranslations[12].atr.Name, Is.EqualTo("05. Gyllebeholdere"));
        // Assert.That(areaRuleTranslations[13].atr.AreaRuleId, Is.EqualTo(areaRules[4].Id));
        // Assert.That(areaRuleTranslations[13].atr.LanguageId, Is.EqualTo(2));
        // Assert.That(areaRuleTranslations[13].atr.Name, Is.EqualTo("05. Slurry containers"));
        // Assert.That(areaRuleTranslations[14].atr.AreaRuleId, Is.EqualTo(areaRules[4].Id));
        // Assert.That(areaRuleTranslations[14].atr.LanguageId, Is.EqualTo(3));
        // Assert.That(areaRuleTranslations[14].atr.Name, Is.EqualTo("05. Güllebehälter"));
        // Assert.That(areaRuleTranslations[15].atr.AreaRuleId, Is.EqualTo(areaRules[5].Id));
        // Assert.That(areaRuleTranslations[15].atr.LanguageId, Is.EqualTo(1));
        // Assert.That(areaRuleTranslations[15].atr.Name, Is.EqualTo("06. Gyllepumper, - miksere, - seperatorer og spredere"));
        // Assert.That(areaRuleTranslations[16].atr.AreaRuleId, Is.EqualTo(areaRules[5].Id));
        // Assert.That(areaRuleTranslations[16].atr.LanguageId, Is.EqualTo(2));
        // Assert.That(areaRuleTranslations[16].atr.Name, Is.EqualTo("06. Slurry pumps, - mixers, - separators and spreaders"));
        // Assert.That(areaRuleTranslations[17].atr.AreaRuleId, Is.EqualTo(areaRules[5].Id));
        // Assert.That(areaRuleTranslations[17].atr.LanguageId, Is.EqualTo(3));
        // Assert.That(areaRuleTranslations[17].atr.Name, Is.EqualTo("06. Schlammpumpen, - Mischer, - Separatoren und Verteiler"));
        // Assert.That(areaRuleTranslations[18].atr.AreaRuleId, Is.EqualTo(areaRules[6].Id));
        // Assert.That(areaRuleTranslations[18].atr.LanguageId, Is.EqualTo(1));
        // Assert.That(areaRuleTranslations[18].atr.Name, Is.EqualTo("07. Forsyningssystemer til vand og foder"));
        // Assert.That(areaRuleTranslations[19].atr.AreaRuleId, Is.EqualTo(areaRules[6].Id));
        // Assert.That(areaRuleTranslations[19].atr.LanguageId, Is.EqualTo(2));
        // Assert.That(areaRuleTranslations[19].atr.Name, Is.EqualTo("07. Supply systems for water and feed"));
        // Assert.That(areaRuleTranslations[20].atr.AreaRuleId, Is.EqualTo(areaRules[6].Id));
        // Assert.That(areaRuleTranslations[20].atr.LanguageId, Is.EqualTo(3));
        // Assert.That(areaRuleTranslations[20].atr.Name, Is.EqualTo("07. Versorgungssysteme für Wasser und Futter"));
        // Assert.That(areaRuleTranslations[21].atr.AreaRuleId, Is.EqualTo(areaRules[7].Id));
        // Assert.That(areaRuleTranslations[21].atr.LanguageId, Is.EqualTo(1));
        // Assert.That(areaRuleTranslations[21].atr.Name, Is.EqualTo("08. Varme-, køle- og ventilationssystemer samt temperaturfølere"));
        // Assert.That(areaRuleTranslations[22].atr.AreaRuleId, Is.EqualTo(areaRules[7].Id));
        // Assert.That(areaRuleTranslations[22].atr.LanguageId, Is.EqualTo(2));
        // Assert.That(areaRuleTranslations[22].atr.Name, Is.EqualTo("08. Heating, cooling and ventilation systems and temperature sensors"));
        // Assert.That(areaRuleTranslations[23].atr.AreaRuleId, Is.EqualTo(areaRules[7].Id));
        // Assert.That(areaRuleTranslations[23].atr.LanguageId, Is.EqualTo(3));
        // Assert.That(areaRuleTranslations[23].atr.Name, Is.EqualTo("08. Heizungs-, Kühl- und Lüftungssysteme und Temperatursensoren"));
        // Assert.That(areaRuleTranslations[24].atr.AreaRuleId, Is.EqualTo(areaRules[8].Id));
        // Assert.That(areaRuleTranslations[24].atr.LanguageId, Is.EqualTo(1));
        // Assert.That(areaRuleTranslations[24].atr.Name, Is.EqualTo("09. Siloer og transportudstyr"));
        // Assert.That(areaRuleTranslations[25].atr.AreaRuleId, Is.EqualTo(areaRules[8].Id));
        // Assert.That(areaRuleTranslations[25].atr.LanguageId, Is.EqualTo(2));
        // Assert.That(areaRuleTranslations[25].atr.Name, Is.EqualTo("09. Silos and transport equipment"));
        // Assert.That(areaRuleTranslations[26].atr.AreaRuleId, Is.EqualTo(areaRules[8].Id));
        // Assert.That(areaRuleTranslations[26].atr.LanguageId, Is.EqualTo(3));
        // Assert.That(areaRuleTranslations[26].atr.Name, Is.EqualTo("09. Silos und Transportgeräte"));
        // Assert.That(areaRuleTranslations[27].atr.AreaRuleId, Is.EqualTo(areaRules[9].Id));
        // Assert.That(areaRuleTranslations[27].atr.LanguageId, Is.EqualTo(1));
        // Assert.That(areaRuleTranslations[27].atr.Name, Is.EqualTo("10. Luftrensningssystemer"));
        // Assert.That(areaRuleTranslations[28].atr.AreaRuleId, Is.EqualTo(areaRules[9].Id));
        // Assert.That(areaRuleTranslations[28].atr.LanguageId, Is.EqualTo(2));
        // Assert.That(areaRuleTranslations[28].atr.Name, Is.EqualTo("10. Air purification systems"));
        // Assert.That(areaRuleTranslations[29].atr.AreaRuleId, Is.EqualTo(areaRules[9].Id));
        // Assert.That(areaRuleTranslations[29].atr.LanguageId, Is.EqualTo(3));
        // Assert.That(areaRuleTranslations[29].atr.Name, Is.EqualTo("10. Luftreinigungssysteme"));
        // Assert.That(areaRuleTranslations[30].atr.AreaRuleId, Is.EqualTo(areaRules[10].Id));
        // Assert.That(areaRuleTranslations[30].atr.LanguageId, Is.EqualTo(1));
        // Assert.That(areaRuleTranslations[30].atr.Name, Is.EqualTo("11. Udstyr til drikkevand"));
        // Assert.That(areaRuleTranslations[31].atr.AreaRuleId, Is.EqualTo(areaRules[10].Id));
        // Assert.That(areaRuleTranslations[31].atr.LanguageId, Is.EqualTo(2));
        // Assert.That(areaRuleTranslations[31].atr.Name, Is.EqualTo("11. Equipment for drinking water"));
        // Assert.That(areaRuleTranslations[32].atr.AreaRuleId, Is.EqualTo(areaRules[10].Id));
        // Assert.That(areaRuleTranslations[32].atr.LanguageId, Is.EqualTo(3));
        // Assert.That(areaRuleTranslations[32].atr.Name, Is.EqualTo("11. Ausrüstung für Trinkwasser"));
        // Assert.That(areaRuleTranslations[33].atr.AreaRuleId, Is.EqualTo(areaRules[11].Id));
        // Assert.That(areaRuleTranslations[33].atr.LanguageId, Is.EqualTo(1));
        // Assert.That(areaRuleTranslations[33].atr.Name, Is.EqualTo("12. Maskiner til udbringning af husdyrgødning samt doseringsmekanisme- eller dyse"));
        // Assert.That(areaRuleTranslations[34].atr.AreaRuleId, Is.EqualTo(areaRules[11].Id));
        // Assert.That(areaRuleTranslations[34].atr.LanguageId, Is.EqualTo(2));
        // Assert.That(areaRuleTranslations[34].atr.Name, Is.EqualTo("12. Machines for spreading livestock manure and dosing mechanisms or nozzles"));
        // Assert.That(areaRuleTranslations[35].atr.AreaRuleId, Is.EqualTo(areaRules[11].Id));
        // Assert.That(areaRuleTranslations[35].atr.LanguageId, Is.EqualTo(3));
        // Assert.That(areaRuleTranslations[35].atr.Name, Is.EqualTo("12. Maschinen zum Ausbringen von Viehmist und Dosiervorrichtungen oder Düsen"));
        // Assert.That(areaRuleTranslations[36].atr.AreaRuleId, Is.EqualTo(areaRules[12].Id));
        // Assert.That(areaRuleTranslations[36].atr.LanguageId, Is.EqualTo(1));
        // Assert.That(areaRuleTranslations[36].atr.Name, Is.EqualTo("13. Miljøledelse gennemgået og revideret"));
        // Assert.That(areaRuleTranslations[37].atr.AreaRuleId, Is.EqualTo(areaRules[12].Id));
        // Assert.That(areaRuleTranslations[37].atr.LanguageId, Is.EqualTo(2));
        // Assert.That(areaRuleTranslations[37].atr.Name, Is.EqualTo("13. Environmental management reviewed and revised"));
        // Assert.That(areaRuleTranslations[38].atr.AreaRuleId, Is.EqualTo(areaRules[12].Id));
        // Assert.That(areaRuleTranslations[38].atr.LanguageId, Is.EqualTo(3));
        // Assert.That(areaRuleTranslations[38].atr.Name, Is.EqualTo("13. Umweltmanagement überprüft und überarbeitet"));
        // Assert.That(areaRuleTranslations[39].atr.AreaRuleId, Is.EqualTo(areaRules[13].Id));
        // Assert.That(areaRuleTranslations[39].atr.LanguageId, Is.EqualTo(1));
        // Assert.That(areaRuleTranslations[39].atr.Name, Is.EqualTo("14. Beredskabsplan gennemgået og revideret"));
        // Assert.That(areaRuleTranslations[40].atr.AreaRuleId, Is.EqualTo(areaRules[13].Id));
        // Assert.That(areaRuleTranslations[40].atr.LanguageId, Is.EqualTo(2));
        // Assert.That(areaRuleTranslations[40].atr.Name, Is.EqualTo("14. Contingency plan reviewed and revised"));
        // Assert.That(areaRuleTranslations[41].atr.AreaRuleId, Is.EqualTo(areaRules[13].Id));
        // Assert.That(areaRuleTranslations[41].atr.LanguageId, Is.EqualTo(3));
        // Assert.That(areaRuleTranslations[41].atr.Name, Is.EqualTo("14. Notfallplan überprüft und überarbeitet"));

        // Assert areaProperties
        Assert.That(areaProperties, Is.Not.Null);
        Assert.That(areaProperties.Count, Is.EqualTo(1));
        Assert.That(areaProperties[0].PropertyId, Is.EqualTo(property.Id));
        Assert.That(areaProperties[0].AreaId, Is.EqualTo(areaTranslation.AreaId));

        // Assert folder translations
        Assert.That(folderTranslations, Is.Not.Null);
        Assert.That(folderTranslations.Count, Is.EqualTo(56));
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
        Assert.That(folderTranslations[31].Name, Is.EqualTo("00. Завдання, що прострочені"));
        Assert.That(folderTranslations[31].LanguageId, Is.EqualTo(4));
        Assert.That(folderTranslations[32].Name, Is.EqualTo("00. Zaległe zadania"));
        Assert.That(folderTranslations[32].LanguageId, Is.EqualTo(5));
        Assert.That(folderTranslations[33].Name, Is.EqualTo("00. Forfalte oppgaver"));
        Assert.That(folderTranslations[33].LanguageId, Is.EqualTo(6));
        Assert.That(folderTranslations[34].Name, Is.EqualTo("00. Försenade uppgifter"));
        Assert.That(folderTranslations[34].LanguageId, Is.EqualTo(7));
        Assert.That(folderTranslations[35].Name, Is.EqualTo("00. Tareas vencidas"));
        Assert.That(folderTranslations[35].LanguageId, Is.EqualTo(8));
        Assert.That(folderTranslations[36].Name, Is.EqualTo("00. Tâches dépassées"));
        Assert.That(folderTranslations[36].LanguageId, Is.EqualTo(9));
        Assert.That(folderTranslations[37].Name, Is.EqualTo("00. Compiti superati"));
        Assert.That(folderTranslations[37].LanguageId, Is.EqualTo(10));
        Assert.That(folderTranslations[38].Name, Is.EqualTo("00. Overschreden taken"));
        Assert.That(folderTranslations[38].LanguageId, Is.EqualTo(11));
        Assert.That(folderTranslations[39].Name, Is.EqualTo("00. Tarefas excedidas"));
        Assert.That(folderTranslations[39].LanguageId, Is.EqualTo(12));
        Assert.That(folderTranslations[40].Name, Is.EqualTo("00. Tarefas excedidas"));
        Assert.That(folderTranslations[40].LanguageId, Is.EqualTo(13));
        Assert.That(folderTranslations[41].Name, Is.EqualTo("00. Ylitetyt tehtävät"));
        Assert.That(folderTranslations[41].LanguageId, Is.EqualTo(14));
        Assert.That(folderTranslations[42].Name, Is.EqualTo("00. Aşılan görevler"));
        Assert.That(folderTranslations[42].LanguageId, Is.EqualTo(15));
        Assert.That(folderTranslations[43].Name, Is.EqualTo("00. Ületatud ülesanded"));
        Assert.That(folderTranslations[43].LanguageId, Is.EqualTo(16));
        Assert.That(folderTranslations[44].Name, Is.EqualTo("00. Pārsniegtie uzdevumi"));
        Assert.That(folderTranslations[44].LanguageId, Is.EqualTo(17));
        Assert.That(folderTranslations[45].Name, Is.EqualTo("00. Viršyti uždaviniai"));
        Assert.That(folderTranslations[45].LanguageId, Is.EqualTo(18));
        Assert.That(folderTranslations[46].Name, Is.EqualTo("00. Sarcini depășite"));
        Assert.That(folderTranslations[46].LanguageId, Is.EqualTo(19));
        Assert.That(folderTranslations[47].Name, Is.EqualTo("00. Превишени задачи"));
        Assert.That(folderTranslations[47].LanguageId, Is.EqualTo(20));
        Assert.That(folderTranslations[48].Name, Is.EqualTo("00. Prekročené úlohy"));
        Assert.That(folderTranslations[48].LanguageId, Is.EqualTo(21));
        Assert.That(folderTranslations[49].Name, Is.EqualTo("00. Presežene naloge"));
        Assert.That(folderTranslations[49].LanguageId, Is.EqualTo(22));
        Assert.That(folderTranslations[50].Name, Is.EqualTo("00. Yfirskredin verkefni"));
        Assert.That(folderTranslations[50].LanguageId, Is.EqualTo(23));
        Assert.That(folderTranslations[51].Name, Is.EqualTo("00. Překročené úkoly"));
        Assert.That(folderTranslations[51].LanguageId, Is.EqualTo(24));
        Assert.That(folderTranslations[52].Name, Is.EqualTo("00. Prekoračeni zad"));
        Assert.That(folderTranslations[52].LanguageId, Is.EqualTo(25));
        Assert.That(folderTranslations[53].Name, Is.EqualTo("00. Logbøger"));
        Assert.That(folderTranslations[53].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[54].Name, Is.EqualTo("01. Log books")); // TODO: This should be "00. Log books"
        Assert.That(folderTranslations[54].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[55].Name, Is.EqualTo("01. Logbücher")); // TODO: This should be "00. Logbücher"
        Assert.That(folderTranslations[55].LanguageId, Is.EqualTo(3));
    }
}