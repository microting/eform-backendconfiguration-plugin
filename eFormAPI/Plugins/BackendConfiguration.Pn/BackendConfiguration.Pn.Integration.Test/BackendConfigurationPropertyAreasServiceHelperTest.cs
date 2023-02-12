using BackendConfiguration.Pn.Infrastructure.Helpers;
using BackendConfiguration.Pn.Infrastructure.Models.Properties;
using BackendConfiguration.Pn.Infrastructure.Models.PropertyAreas;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using eFormCore;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.ItemsPlanningBase.Infrastructure.Data;
using Microting.TimePlanningBase.Infrastructure.Data;
using File = System.IO.File;

namespace BackendConfiguration.Pn.Integration.Test;

[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class BackendConfigurationPropertyAreasServiceHelperTest
{
#pragma warning disable CS0618
    private readonly MariaDbTestcontainer _mySqlTestcontainer = new ContainerBuilder<MariaDbTestcontainer>()
#pragma warning restore CS0618
        .WithDatabase(new MySqlTestcontainerConfiguration(image: "mariadb:10.8")
        {
            Database = "myDb",
            Username = "root",
            Password = "secretpassword"
        })
        .WithEnvironment("MYSQL_ROOT_PASSWORD", "secretpassword")
        .Build();

    private BackendConfigurationPnDbContext? _backendConfigurationPnDbContext;
    private ItemsPlanningPnDbContext? _itemsPlanningPnDbContext;
    private TimePlanningPnDbContext? _timePlanningPnDbContext;
    private MicrotingDbContext? _microtingDbContext;

    private BackendConfigurationPnDbContext GetBackendDbContext(string connectionStr)
    {

        var optionsBuilder = new DbContextOptionsBuilder<BackendConfigurationPnDbContext>();

        optionsBuilder.UseMySql(connectionStr.Replace("myDb", "420_eform-backend-configuration-plugin"), new MariaDbServerVersion(
            new Version(10, 8)));

        var backendConfigurationPnDbContext = new BackendConfigurationPnDbContext(optionsBuilder.Options);
        string file = Path.Combine("SQL", "420_eform-backend-configuration-plugin.sql");
        string rawSql = File.ReadAllText(file);

        try
        {
            backendConfigurationPnDbContext.Database.EnsureCreated();
        } catch (Exception e)
        {
            Console.WriteLine(e);
        }
        backendConfigurationPnDbContext.Database.ExecuteSqlRaw(rawSql);

        return backendConfigurationPnDbContext;
    }

    private ItemsPlanningPnDbContext GetItemsPlanningPnDbContext(string connectionStr)
    {

        var optionsBuilder = new DbContextOptionsBuilder<ItemsPlanningPnDbContext>();

        optionsBuilder.UseMySql(connectionStr.Replace("myDb", "420_eform-angular-items-planning-plugin"), new MariaDbServerVersion(
            new Version(10, 8)));

        var backendConfigurationPnDbContext = new ItemsPlanningPnDbContext(optionsBuilder.Options);
        string file = Path.Combine("SQL", "420_eform-angular-items-planning-plugin.sql");
        string rawSql = File.ReadAllText(file);

        backendConfigurationPnDbContext.Database.EnsureCreated();
        backendConfigurationPnDbContext.Database.ExecuteSqlRaw(rawSql);

        return backendConfigurationPnDbContext;
    }

    private TimePlanningPnDbContext GetTimePlanningPnDbContext(string connectionStr)
    {

        var optionsBuilder = new DbContextOptionsBuilder<TimePlanningPnDbContext>();

        optionsBuilder.UseMySql(connectionStr.Replace("myDb", "420_eform-angular-items-planning-plugin"), new MariaDbServerVersion(
            new Version(10, 8)));

        var backendConfigurationPnDbContext = new TimePlanningPnDbContext(optionsBuilder.Options);
        string file = Path.Combine("SQL", "420_eform-angular-time-planning-plugin.sql");
        string rawSql = File.ReadAllText(file);

        backendConfigurationPnDbContext.Database.EnsureCreated();
        backendConfigurationPnDbContext.Database.ExecuteSqlRaw(rawSql);

        return backendConfigurationPnDbContext;
    }

    private MicrotingDbContext GetContext(string connectionStr)
    {
        DbContextOptionsBuilder dbContextOptionsBuilder = new DbContextOptionsBuilder();

        dbContextOptionsBuilder.UseMySql(connectionStr.Replace("myDb", "420_SDK"), new MariaDbServerVersion(
            new Version(10, 8)));
        var microtingDbContext =  new MicrotingDbContext(dbContextOptionsBuilder.Options);
        string file = Path.Combine("SQL", "420_SDK.sql");
        string rawSql = File.ReadAllText(file);

        microtingDbContext.Database.EnsureCreated();
        microtingDbContext.Database.ExecuteSqlRaw(rawSql);

        return microtingDbContext;
    }

    private async Task<Core> GetCore()
    {
        var core = new Core();
        await core.StartSqlOnly(_mySqlTestcontainer.ConnectionString.Replace("myDb", "420_SDK"));
        return core;
    }

    [SetUp]
    public async Task Setup()
    {
        Console.WriteLine($"{DateTime.Now} : Starting MariaDb Container...");
        await _mySqlTestcontainer.StartAsync();
        Console.WriteLine($"{DateTime.Now} : Started MariaDb Container");

        _backendConfigurationPnDbContext = GetBackendDbContext(_mySqlTestcontainer.ConnectionString);

        _backendConfigurationPnDbContext!.Database.SetCommandTimeout(300);

        _itemsPlanningPnDbContext = GetItemsPlanningPnDbContext(_mySqlTestcontainer.ConnectionString);

        _itemsPlanningPnDbContext.Database.SetCommandTimeout(300);

        _timePlanningPnDbContext = GetTimePlanningPnDbContext(_mySqlTestcontainer.ConnectionString);

        _timePlanningPnDbContext.Database.SetCommandTimeout(300);

        _microtingDbContext = GetContext(_mySqlTestcontainer.ConnectionString);

        _microtingDbContext.Database.SetCommandTimeout(300);

    }

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
            IsFarm = false,
            LanguagesIds = new List<int>
            {
                1
            },
            MainMailAddress = Guid.NewGuid().ToString(),
            Name = Guid.NewGuid().ToString(),
            WorkorderEnable = false
        };

        var core = await GetCore();

        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1, 1);

        var property = await _backendConfigurationPnDbContext!.Properties.FirstAsync(x => x.Name == propertyCreateModel.Name);
        var areaTranslation = await _backendConfigurationPnDbContext!.AreaTranslations.FirstAsync(x => x.Name == "00. Logbøger");

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
            PropertyId = property.Id
        };

        // Act
        var result = await BackendConfigurationPropertyAreasServiceHelper.Update(propertyAreasUpdateModel, core, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1);

        // Assert
        var areaRules = await _backendConfigurationPnDbContext!.AreaRules.Where(x => x.PropertyId == property.Id).ToListAsync();
        var areaRuleTranslations = await _backendConfigurationPnDbContext!.AreaRuleTranslations
            .Join(_backendConfigurationPnDbContext.AreaRules, atr => atr.AreaRuleId, ar => ar.Id,
                (atr, ar) => new { atr, ar }).Where(x => x.ar.PropertyId == property.Id).ToListAsync();
        var areaProperties = await _backendConfigurationPnDbContext!.AreaProperties.Where(x => x.PropertyId == property.Id).ToListAsync();

        // Assert result
        Assert.NotNull(result);
        Assert.That(result.Success, Is.EqualTo(true));

        // Assert areaRules
        Assert.NotNull(areaRules);
        Assert.That(areaRules.Count, Is.EqualTo(14));
        Assert.That(areaRules[0].PropertyId, Is.EqualTo(property.Id));
        Assert.That(areaRules[1].PropertyId, Is.EqualTo(property.Id));
        Assert.That(areaRules[2].PropertyId, Is.EqualTo(property.Id));
        Assert.That(areaRules[3].PropertyId, Is.EqualTo(property.Id));
        Assert.That(areaRules[4].PropertyId, Is.EqualTo(property.Id));
        Assert.That(areaRules[5].PropertyId, Is.EqualTo(property.Id));
        Assert.That(areaRules[6].PropertyId, Is.EqualTo(property.Id));
        Assert.That(areaRules[7].PropertyId, Is.EqualTo(property.Id));
        Assert.That(areaRules[8].PropertyId, Is.EqualTo(property.Id));
        Assert.That(areaRules[9].PropertyId, Is.EqualTo(property.Id));
        Assert.That(areaRules[10].PropertyId, Is.EqualTo(property.Id));
        Assert.That(areaRules[11].PropertyId, Is.EqualTo(property.Id));
        Assert.That(areaRules[12].PropertyId, Is.EqualTo(property.Id));
        Assert.That(areaRules[13].PropertyId, Is.EqualTo(property.Id));
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
        Assert.That(areaProperties[0].PropertyId, Is.EqualTo(property.Id));
        Assert.That(areaProperties[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
    }

    // Should test the Update method and enable "03. Gyllebeholdere" and return success
    [Test]
    public async Task BackendConfigurationPropertyAreasServiceHelper_Update_SlurryTanks_ReturnsSuccess()
    {
        // Arrange
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

        var core = await GetCore();

        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1, 1);

        var property = await _backendConfigurationPnDbContext!.Properties.FirstAsync(x => x.Name == propertyCreateModel.Name).ConfigureAwait(false);
        var areaTranslation = await _backendConfigurationPnDbContext!.AreaTranslations.FirstAsync(x => x.Name == "03. Gyllebeholdere").ConfigureAwait(false);

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
            PropertyId = property.Id
        };

        // Act
        var result = await BackendConfigurationPropertyAreasServiceHelper.Update(propertyAreasUpdateModel, core, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1);

        // Assert
        var areaRules = await _backendConfigurationPnDbContext!.AreaRules.Where(x => x.PropertyId == property.Id).ToListAsync();
        var areaRuleTranslations = await _backendConfigurationPnDbContext!.AreaRuleTranslations
            .Join(_backendConfigurationPnDbContext.AreaRules, atr => atr.AreaRuleId, ar => ar.Id,
                (atr, ar) => new { atr, ar }).Where(x => x.ar.PropertyId == property.Id).ToListAsync();
        var areaProperties = await _backendConfigurationPnDbContext!.AreaProperties.Where(x => x.PropertyId == property.Id).ToListAsync();

        // Assert result
        Assert.NotNull(result);
        Assert.That(result.Success, Is.EqualTo(true));

        // Assert areaRules
        Assert.NotNull(areaRules);
        Assert.That(areaRules.Count, Is.EqualTo(0));

        // Assert areaRuleTranslations
        Assert.NotNull(areaRuleTranslations);
        Assert.That(areaRuleTranslations.Count, Is.EqualTo(0));

        // Assert areaProperties
        Assert.NotNull(areaProperties);
        Assert.That(areaProperties.Count, Is.EqualTo(1));
    }

    // Should test the Update method and disable "04. Foderindlægssedler" and return success
    [Test]
    public async Task BackendConfigurationPropertyAreasServiceHelper_Update_FeedinDocuments_ReturnsSuccess()
    {
        // Arrange
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

        var core = await GetCore();

        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1, 1);

        var property = await _backendConfigurationPnDbContext!.Properties.FirstAsync(x => x.Name == propertyCreateModel.Name).ConfigureAwait(false);
        var areaTranslation = await _backendConfigurationPnDbContext!.AreaTranslations.FirstAsync(x => x.Name == "04. Foderindlægssedler").ConfigureAwait(false);

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
            PropertyId = property.Id
        };

        // Act
        var result = await BackendConfigurationPropertyAreasServiceHelper.Update(propertyAreasUpdateModel, core, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1);

        // Assert
        var areaRules = await _backendConfigurationPnDbContext!.AreaRules.Where(x => x.PropertyId == property.Id).ToListAsync();
        var areaRuleTranslations = await _backendConfigurationPnDbContext!.AreaRuleTranslations
            .Join(_backendConfigurationPnDbContext.AreaRules, atr => atr.AreaRuleId, ar => ar.Id,
                (atr, ar) => new { atr, ar }).Where(x => x.ar.PropertyId == property.Id).ToListAsync();
        var areaProperties = await _backendConfigurationPnDbContext!.AreaProperties.Where(x => x.PropertyId == property.Id).ToListAsync();

        // Assert result
        Assert.NotNull(result);
        Assert.That(result.Success, Is.EqualTo(true));

        // Assert areaRules
        Assert.NotNull(areaRules);
        Assert.That(areaRules.Count, Is.EqualTo(0));

        // Assert areaRuleTranslations
        Assert.NotNull(areaRuleTranslations);
        Assert.That(areaRuleTranslations.Count, Is.EqualTo(0));

        // Assert areaProperties
        Assert.NotNull(areaProperties);
        Assert.That(areaProperties.Count, Is.EqualTo(1));
    }

    // Should test the Update method and enable "05. Stalde: Halebid og klargøring" and return success
    [Test]
    public async Task BackendConfigurationPropertyAreasServiceHelper_Update_TailBiteAndPreparations_ReturnsSuccess()
    {
        // Arrange
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

        var core = await GetCore();

        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1, 1);

        var property = await _backendConfigurationPnDbContext!.Properties.FirstAsync(x => x.Name == propertyCreateModel.Name).ConfigureAwait(false);
        var areaTranslation = await _backendConfigurationPnDbContext!.AreaTranslations.FirstAsync(x => x.Name == "05. Stalde: Halebid og klargøring").ConfigureAwait(false);

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
            PropertyId = property.Id
        };

        // Act
        var result = await BackendConfigurationPropertyAreasServiceHelper.Update(propertyAreasUpdateModel, core, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1);

        // Assert
        var areaRules = await _backendConfigurationPnDbContext!.AreaRules.Where(x => x.PropertyId == property.Id).ToListAsync();
        var areaRuleTranslations = await _backendConfigurationPnDbContext!.AreaRuleTranslations
            .Join(_backendConfigurationPnDbContext.AreaRules, atr => atr.AreaRuleId, ar => ar.Id,
                (atr, ar) => new { atr, ar }).Where(x => x.ar.PropertyId == property.Id).ToListAsync();
        var areaProperties = await _backendConfigurationPnDbContext!.AreaProperties.Where(x => x.PropertyId == property.Id).ToListAsync();

        // Assert result
        Assert.NotNull(result);
        Assert.That(result.Success, Is.EqualTo(true));

        // Assert areaRules
        Assert.NotNull(areaRules);
        Assert.That(areaRules.Count, Is.EqualTo(1));

        // Assert areaRuleTranslations
        Assert.NotNull(areaRuleTranslations);
        Assert.That(areaRuleTranslations.Count, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[0].atr.Name, Is.EqualTo("01. Registrer halebid"));
        Assert.That(areaRuleTranslations[0].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[1].atr.Name, Is.EqualTo("01. Register tail bite"));
        Assert.That(areaRuleTranslations[1].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[2].atr.Name, Is.EqualTo("01. Schwanzbiss registrieren"));
        Assert.That(areaRuleTranslations[2].atr.LanguageId, Is.EqualTo(3));

        // Assert areaProperties
        Assert.NotNull(areaProperties);
        Assert.That(areaProperties.Count, Is.EqualTo(1));
    }

    // Should test the Update method and enable "06. Fodersiloer" and return success
    [Test]
    public async Task BackendConfigurationPropertyAreasServiceHelper_Update_Silos_ReturnsSuccess()
    {
        // Arrange
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

        var core = await GetCore();

        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1, 1);

        var property = await _backendConfigurationPnDbContext!.Properties.FirstAsync(x => x.Name == propertyCreateModel.Name).ConfigureAwait(false);
        var areaTranslation = await _backendConfigurationPnDbContext!.AreaTranslations.FirstAsync(x => x.Name == "06. Fodersiloer").ConfigureAwait(false);

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
            PropertyId = property.Id
        };

        // Act
        var result = await BackendConfigurationPropertyAreasServiceHelper.Update(propertyAreasUpdateModel, core, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1);

        // Assert
        var areaRules = await _backendConfigurationPnDbContext!.AreaRules.Where(x => x.PropertyId == property.Id).ToListAsync();
        var areaRuleTranslations = await _backendConfigurationPnDbContext!.AreaRuleTranslations
            .Join(_backendConfigurationPnDbContext.AreaRules, atr => atr.AreaRuleId, ar => ar.Id,
                (atr, ar) => new { atr, ar }).Where(x => x.ar.PropertyId == property.Id).ToListAsync();
        var areaProperties = await _backendConfigurationPnDbContext!.AreaProperties.Where(x => x.PropertyId == property.Id).ToListAsync();

        // Assert result
        Assert.NotNull(result);
        Assert.That(result.Success, Is.EqualTo(true));

        // Assert areaRules
        Assert.NotNull(areaRules);
        Assert.That(areaRules.Count, Is.EqualTo(0));

        // Assert areaRuleTranslations
        Assert.NotNull(areaRuleTranslations);
        Assert.That(areaRuleTranslations.Count, Is.EqualTo(0));

        // Assert areaProperties
        Assert.NotNull(areaProperties);
        Assert.That(areaProperties.Count, Is.EqualTo(1));
    }

    // Should test the Update method and enable "07. Skadedyrsbekæmpelse" and return success
    [Test]
    public async Task BackendConfigurationPropertyAreasServiceHelper_Update_PestControl_ReturnsSuccess()
    {
        // Arrange
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

        var core = await GetCore();

        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1, 1);

        var property = await _backendConfigurationPnDbContext!.Properties.FirstAsync(x => x.Name == propertyCreateModel.Name).ConfigureAwait(false);
        var areaTranslation = await _backendConfigurationPnDbContext!.AreaTranslations.FirstAsync(x => x.Name == "07. Skadedyrsbekæmpelse").ConfigureAwait(false);

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
            PropertyId = property.Id
        };

        // Act
        var result = await BackendConfigurationPropertyAreasServiceHelper.Update(propertyAreasUpdateModel, core, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1);

        // Assert
        var areaRules = await _backendConfigurationPnDbContext!.AreaRules.Where(x => x.PropertyId == property.Id).ToListAsync();
        var areaRuleTranslations = await _backendConfigurationPnDbContext!.AreaRuleTranslations
            .Join(_backendConfigurationPnDbContext.AreaRules, atr => atr.AreaRuleId, ar => ar.Id,
                (atr, ar) => new { atr, ar }).Where(x => x.ar.PropertyId == property.Id).ToListAsync();
        var areaProperties = await _backendConfigurationPnDbContext!.AreaProperties.Where(x => x.PropertyId == property.Id).ToListAsync();

        // Assert result
        Assert.NotNull(result);
        Assert.That(result.Success, Is.EqualTo(true));

        // Assert areaRules
        Assert.NotNull(areaRules);
        Assert.That(areaRules.Count, Is.EqualTo(2));

        // Assert areaRuleTranslations
        Assert.NotNull(areaRuleTranslations);
        Assert.That(areaRuleTranslations.Count, Is.EqualTo(6));
        Assert.That(areaRuleTranslations[0].atr.Name, Is.EqualTo("07. Rotter"));
        Assert.That(areaRuleTranslations[0].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[1].atr.Name, Is.EqualTo("07. Rats"));
        Assert.That(areaRuleTranslations[1].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[2].atr.Name, Is.EqualTo("07. Ratten"));
        Assert.That(areaRuleTranslations[2].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[3].atr.Name, Is.EqualTo("07. Fluer"));
        Assert.That(areaRuleTranslations[3].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[4].atr.Name, Is.EqualTo("07. Flies"));
        Assert.That(areaRuleTranslations[4].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[5].atr.Name, Is.EqualTo("07. Fliegen"));
        Assert.That(areaRuleTranslations[5].atr.LanguageId, Is.EqualTo(3));

        // Assert areaProperties
        Assert.NotNull(areaProperties);
        Assert.That(areaProperties.Count, Is.EqualTo(1));
    }

    // Should test the Update method and enable "08. Luftrensning" and return success
    [Test]
    public async Task BackendConfigurationPropertyAreasServiceHelper_Update_AirCleaning_ReturnsSuccess()
    {
        // Arrange
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

        var core = await GetCore();

        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1, 1);

        var property = await _backendConfigurationPnDbContext!.Properties.FirstAsync(x => x.Name == propertyCreateModel.Name).ConfigureAwait(false);
        var areaTranslation = await _backendConfigurationPnDbContext!.AreaTranslations.FirstAsync(x => x.Name == "08. Luftrensning").ConfigureAwait(false);

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
            PropertyId = property.Id
        };

        // Act
        var result = await BackendConfigurationPropertyAreasServiceHelper.Update(propertyAreasUpdateModel, core, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1);

        // Assert
        var areaRules = await _backendConfigurationPnDbContext!.AreaRules.Where(x => x.PropertyId == property.Id).ToListAsync();
        var areaRuleTranslations = await _backendConfigurationPnDbContext!.AreaRuleTranslations
            .Join(_backendConfigurationPnDbContext.AreaRules, atr => atr.AreaRuleId, ar => ar.Id,
                (atr, ar) => new { atr, ar }).Where(x => x.ar.PropertyId == property.Id).ToListAsync();
        var areaProperties = await _backendConfigurationPnDbContext!.AreaProperties.Where(x => x.PropertyId == property.Id).ToListAsync();

        // Assert result
        Assert.NotNull(result);
        Assert.That(result.Success, Is.EqualTo(true));

        // Assert areaRules
        Assert.NotNull(areaRules);
        Assert.That(areaRules.Count, Is.EqualTo(3));

        // Assert areaRuleTranslations
        Assert.NotNull(areaRuleTranslations);
        Assert.That(areaRuleTranslations.Count, Is.EqualTo(9));
        Assert.That(areaRuleTranslations[0].atr.Name, Is.EqualTo("08. Luftrensning timer"));
        Assert.That(areaRuleTranslations[0].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[1].atr.Name, Is.EqualTo("08. Air cleaning timer"));
        Assert.That(areaRuleTranslations[1].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[2].atr.Name, Is.EqualTo("08. Luftreinigungstimer"));
        Assert.That(areaRuleTranslations[2].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[3].atr.Name, Is.EqualTo("08. Luftrensning serviceaftale"));
        Assert.That(areaRuleTranslations[3].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[4].atr.Name, Is.EqualTo("08. Air cleaning service agreement"));
        Assert.That(areaRuleTranslations[4].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[5].atr.Name, Is.EqualTo("08. Luftreinigungsservicevertrag"));
        Assert.That(areaRuleTranslations[5].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[6].atr.Name, Is.EqualTo("08. Luftrensning driftsstop"));
        Assert.That(areaRuleTranslations[6].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[7].atr.Name, Is.EqualTo("08. Air cleaning downtime"));
        Assert.That(areaRuleTranslations[7].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[8].atr.Name, Is.EqualTo("08. Ausfallzeit der Luftreinigung"));
        Assert.That(areaRuleTranslations[8].atr.LanguageId, Is.EqualTo(3));

        // Assert areaProperties
        Assert.NotNull(areaProperties);
        Assert.That(areaProperties.Count, Is.EqualTo(1));
    }

    // Should test the Update method and enable "09. Forsuring" and return success
    [Test]
    public async Task BackendConfigurationPropertyAreasServiceHelper_Update_Acidification_ReturnsSuccess()
    {
        // Arrange
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

        var core = await GetCore();

        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1, 1);

        var property = await _backendConfigurationPnDbContext!.Properties.FirstAsync(x => x.Name == propertyCreateModel.Name).ConfigureAwait(false);
        var areaTranslation = await _backendConfigurationPnDbContext!.AreaTranslations.FirstAsync(x => x.Name == "09. Forsuring").ConfigureAwait(false);

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
            PropertyId = property.Id
        };

        // Act
        var result = await BackendConfigurationPropertyAreasServiceHelper.Update(propertyAreasUpdateModel, core, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1);

        // Assert
        var areaRules = await _backendConfigurationPnDbContext!.AreaRules.Where(x => x.PropertyId == property.Id).ToListAsync();
        var areaRuleTranslations = await _backendConfigurationPnDbContext!.AreaRuleTranslations
            .Join(_backendConfigurationPnDbContext.AreaRules, atr => atr.AreaRuleId, ar => ar.Id,
                (atr, ar) => new { atr, ar }).Where(x => x.ar.PropertyId == property.Id).ToListAsync();
        var areaProperties = await _backendConfigurationPnDbContext!.AreaProperties.Where(x => x.PropertyId == property.Id).ToListAsync();

        // Assert result
        Assert.NotNull(result);
        Assert.That(result.Success, Is.EqualTo(true));

        // Assert areaRules
        Assert.NotNull(areaRules);
        Assert.That(areaRules.Count, Is.EqualTo(3));

        // Assert areaRuleTranslations
        Assert.NotNull(areaRuleTranslations);
        Assert.That(areaRuleTranslations.Count, Is.EqualTo(9));
        Assert.That(areaRuleTranslations[0].atr.Name, Is.EqualTo("09. Forsuring pH værdi"));
        Assert.That(areaRuleTranslations[0].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[1].atr.Name, Is.EqualTo("09. Acidification pH value"));
        Assert.That(areaRuleTranslations[1].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[2].atr.Name, Is.EqualTo("09. Ansäuerung pH-Wert"));
        Assert.That(areaRuleTranslations[2].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[3].atr.Name, Is.EqualTo("09. Forsuring serviceaftale"));
        Assert.That(areaRuleTranslations[3].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[4].atr.Name, Is.EqualTo("09. Acidification service agreement"));
        Assert.That(areaRuleTranslations[4].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[5].atr.Name, Is.EqualTo("09. Säuerungsservicevertrag"));
        Assert.That(areaRuleTranslations[5].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[6].atr.Name, Is.EqualTo("09. Forsuring driftsstop"));
        Assert.That(areaRuleTranslations[6].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[7].atr.Name, Is.EqualTo("09. Acidification downtime"));
        Assert.That(areaRuleTranslations[7].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[8].atr.Name, Is.EqualTo("09. Ausfallzeit der Ansäuerung"));
        Assert.That(areaRuleTranslations[8].atr.LanguageId, Is.EqualTo(3));

        // Assert areaProperties
        Assert.NotNull(areaProperties);
        Assert.That(areaProperties.Count, Is.EqualTo(1));
    }

    // Should test the Update method and enable "10. Varmepumper" and return success
    [Test]
    public async Task BackendConfigurationPropertyAreasServiceHelper_Update_HeatPumps_ReturnsSuccess()
    {
        // Arrange
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

        var core = await GetCore();

        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1, 1);

        var property = await _backendConfigurationPnDbContext!.Properties.FirstAsync(x => x.Name == propertyCreateModel.Name).ConfigureAwait(false);
        var areaTranslation = await _backendConfigurationPnDbContext!.AreaTranslations.FirstAsync(x => x.Name == "10. Varmepumper").ConfigureAwait(false);

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
            PropertyId = property.Id
        };

        // Act
        var result = await BackendConfigurationPropertyAreasServiceHelper.Update(propertyAreasUpdateModel, core, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1);

        // Assert
        var areaRules = await _backendConfigurationPnDbContext!.AreaRules.Where(x => x.PropertyId == property.Id).ToListAsync();
        var areaRuleTranslations = await _backendConfigurationPnDbContext!.AreaRuleTranslations
            .Join(_backendConfigurationPnDbContext.AreaRules, atr => atr.AreaRuleId, ar => ar.Id,
                (atr, ar) => new { atr, ar }).Where(x => x.ar.PropertyId == property.Id).ToListAsync();
        var areaProperties = await _backendConfigurationPnDbContext!.AreaProperties.Where(x => x.PropertyId == property.Id).ToListAsync();

        // Assert result
        Assert.NotNull(result);
        Assert.That(result.Success, Is.EqualTo(true));

        // Assert areaRules
        Assert.NotNull(areaRules);
        Assert.That(areaRules.Count, Is.EqualTo(0));

        // Assert areaRuleTranslations
        Assert.NotNull(areaRuleTranslations);
        Assert.That(areaRuleTranslations.Count, Is.EqualTo(0));

        // Assert areaProperties
        Assert.NotNull(areaProperties);
        Assert.That(areaProperties.Count, Is.EqualTo(1));
    }

    // Should test the Update method and enable "11. Varmekilder" and return success
    [Test]
    public async Task BackendConfigurationPropertyAreasServiceHelper_Update_HeatingSources_ReturnsSuccess()
    {
        // Arrange
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

        var core = await GetCore();

        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1, 1);

        var property = await _backendConfigurationPnDbContext!.Properties.FirstAsync(x => x.Name == propertyCreateModel.Name).ConfigureAwait(false);
        var areaTranslation = await _backendConfigurationPnDbContext!.AreaTranslations.FirstAsync(x => x.Name == "11. Varmekilder").ConfigureAwait(false);

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
            PropertyId = property.Id
        };

        // Act
        var result = await BackendConfigurationPropertyAreasServiceHelper.Update(propertyAreasUpdateModel, core, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1);

        // Assert
        var areaRules = await _backendConfigurationPnDbContext!.AreaRules.Where(x => x.PropertyId == property.Id).ToListAsync();
        var areaRuleTranslations = await _backendConfigurationPnDbContext!.AreaRuleTranslations
            .Join(_backendConfigurationPnDbContext.AreaRules, atr => atr.AreaRuleId, ar => ar.Id,
                (atr, ar) => new { atr, ar }).Where(x => x.ar.PropertyId == property.Id).ToListAsync();
        var areaProperties = await _backendConfigurationPnDbContext!.AreaProperties.Where(x => x.PropertyId == property.Id).ToListAsync();

        // Assert result
        Assert.NotNull(result);
        Assert.That(result.Success, Is.EqualTo(true));

        // Assert areaRules
        Assert.NotNull(areaRules);
        Assert.That(areaRules.Count, Is.EqualTo(0));

        // Assert areaRuleTranslations
        Assert.NotNull(areaRuleTranslations);
        Assert.That(areaRuleTranslations.Count, Is.EqualTo(0));

        // Assert areaProperties
        Assert.NotNull(areaProperties);
        Assert.That(areaProperties.Count, Is.EqualTo(1));
    }

    // Should test the Update method and enable "12. Miljøfarlige stoffer" and return success
    [Test]
    public async Task BackendConfigurationPropertyAreasServiceHelper_Update_EnviromentailHazardousSubstances_ReturnsSuccess()
    {
        // Arrange
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

        var core = await GetCore();

        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1, 1);

        var property = await _backendConfigurationPnDbContext!.Properties.FirstAsync(x => x.Name == propertyCreateModel.Name).ConfigureAwait(false);
        var areaTranslation = await _backendConfigurationPnDbContext!.AreaTranslations.FirstAsync(x => x.Name == "12. Miljøfarlige stoffer").ConfigureAwait(false);

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
            PropertyId = property.Id
        };

        // Act
        var result = await BackendConfigurationPropertyAreasServiceHelper.Update(propertyAreasUpdateModel, core, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1);

        // Assert
        var areaRules = await _backendConfigurationPnDbContext!.AreaRules.Where(x => x.PropertyId == property.Id).ToListAsync();
        var areaRuleTranslations = await _backendConfigurationPnDbContext!.AreaRuleTranslations
            .Join(_backendConfigurationPnDbContext.AreaRules, atr => atr.AreaRuleId, ar => ar.Id,
                (atr, ar) => new { atr, ar }).Where(x => x.ar.PropertyId == property.Id).ToListAsync();
        var areaProperties = await _backendConfigurationPnDbContext!.AreaProperties.Where(x => x.PropertyId == property.Id).ToListAsync();

        // Assert result
        Assert.NotNull(result);
        Assert.That(result.Success, Is.EqualTo(true));

        // Assert areaRules
        Assert.NotNull(areaRules);
        Assert.That(areaRules.Count, Is.EqualTo(4));
        Assert.That(areaRules[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[1].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[2].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[3].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[0].PropertyId, Is.EqualTo(property.Id));
        Assert.That(areaRules[1].PropertyId, Is.EqualTo(property.Id));
        Assert.That(areaRules[2].PropertyId, Is.EqualTo(property.Id));
        Assert.That(areaRules[3].PropertyId, Is.EqualTo(property.Id));
        Assert.That(areaRules[0].EformName, Is.EqualTo("12. Dieseltank"));
        Assert.That(areaRules[1].EformName, Is.EqualTo("12. Motor- og spildolie"));
        Assert.That(areaRules[2].EformName, Is.EqualTo("12. Kemi"));
        Assert.That(areaRules[3].EformName, Is.EqualTo("12. Affald og farligt affald"));


        // Assert areaRuleTranslations
        Assert.NotNull(areaRuleTranslations);
        Assert.That(areaRuleTranslations.Count, Is.EqualTo(12));
        Assert.That(areaRuleTranslations[0].atr.Name, Is.EqualTo("12. Dieseltank"));
        Assert.That(areaRuleTranslations[0].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[1].atr.Name, Is.EqualTo("12. Diesel tank"));
        Assert.That(areaRuleTranslations[1].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[2].atr.Name, Is.EqualTo("12. Dieseltank"));
        Assert.That(areaRuleTranslations[2].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[3].atr.Name, Is.EqualTo("12. Motor- og spildolie"));
        Assert.That(areaRuleTranslations[3].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[4].atr.Name, Is.EqualTo("12. Motor oil and waste oil"));
        Assert.That(areaRuleTranslations[4].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[5].atr.Name, Is.EqualTo("12. Motoröl und Altöl"));
        Assert.That(areaRuleTranslations[5].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[6].atr.Name, Is.EqualTo("12. Kemi"));
        Assert.That(areaRuleTranslations[6].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[7].atr.Name, Is.EqualTo("12. Chemistry"));
        Assert.That(areaRuleTranslations[7].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[8].atr.Name, Is.EqualTo("12. Chemie"));
        Assert.That(areaRuleTranslations[8].atr.LanguageId, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[9].atr.Name, Is.EqualTo("12. Affald og farligt affald"));
        Assert.That(areaRuleTranslations[9].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[10].atr.Name, Is.EqualTo("12. Trash"));
        Assert.That(areaRuleTranslations[10].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[11].atr.Name, Is.EqualTo("12. Müll"));
        Assert.That(areaRuleTranslations[11].atr.LanguageId, Is.EqualTo(3));

        // Assert areaProperties
        Assert.NotNull(areaProperties);
        Assert.That(areaProperties.Count, Is.EqualTo(1));
    }

    // Should test the Update method and enable "13. APV Landbrug" and return success
    [Test]
    public async Task BackendConfigurationPropertyAreasServiceHelper_Update_APVAgricultur_ReturnsSuccess()
    {
        // Arrange
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

        var core = await GetCore();

        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1, 1);

        var property = await _backendConfigurationPnDbContext!.Properties.FirstAsync(x => x.Name == propertyCreateModel.Name).ConfigureAwait(false);
        var areaTranslation = await _backendConfigurationPnDbContext!.AreaTranslations.FirstAsync(x => x.Name == "13. APV Landbrug").ConfigureAwait(false);

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
            PropertyId = property.Id
        };

        // Act
        var result = await BackendConfigurationPropertyAreasServiceHelper.Update(propertyAreasUpdateModel, core, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1);

        // Assert
        var areaRules = await _backendConfigurationPnDbContext!.AreaRules.Where(x => x.PropertyId == property.Id).ToListAsync();
        var areaRuleTranslations = await _backendConfigurationPnDbContext!.AreaRuleTranslations
            .Join(_backendConfigurationPnDbContext.AreaRules, atr => atr.AreaRuleId, ar => ar.Id,
                (atr, ar) => new { atr, ar }).Where(x => x.ar.PropertyId == property.Id).ToListAsync();
        var areaProperties = await _backendConfigurationPnDbContext!.AreaProperties.Where(x => x.PropertyId == property.Id).ToListAsync();

        // Assert result
        Assert.NotNull(result);
        Assert.That(result.Success, Is.EqualTo(true));

        // Assert areaRules
        Assert.NotNull(areaRules);
        Assert.That(areaRules.Count, Is.EqualTo(1));
        Assert.That(areaRules[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[0].PropertyId, Is.EqualTo(property.Id));
        Assert.That(areaRules[0].EformName, Is.EqualTo("13. APV Medarbejder"));


        // Assert areaRuleTranslations
        Assert.NotNull(areaRuleTranslations);
        Assert.That(areaRuleTranslations.Count, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[0].atr.Name, Is.EqualTo("13. APV Medarbejder"));
        Assert.That(areaRuleTranslations[0].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[1].atr.Name, Is.EqualTo("13. WPA Agriculture"));
        Assert.That(areaRuleTranslations[1].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[2].atr.Name, Is.EqualTo("13. Arbeitsplatz Landwirtschaft"));
        Assert.That(areaRuleTranslations[2].atr.LanguageId, Is.EqualTo(3));

        // Assert areaProperties
        Assert.NotNull(areaProperties);
        Assert.That(areaProperties.Count, Is.EqualTo(1));
    }

    // Should test the Update method and enable "14. Maskiner" and return success
    [Test]
    public async Task BackendConfigurationPropertyAreasServiceHelper_Update_Machines_ReturnsSuccess()
    {
        // Arrange
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

        var core = await GetCore();

        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1, 1);

        var property = await _backendConfigurationPnDbContext!.Properties.FirstAsync(x => x.Name == propertyCreateModel.Name).ConfigureAwait(false);
        var areaTranslation = await _backendConfigurationPnDbContext!.AreaTranslations.FirstAsync(x => x.Name == "14. Maskiner").ConfigureAwait(false);

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
            PropertyId = property.Id
        };

        // Act
        var result = await BackendConfigurationPropertyAreasServiceHelper.Update(propertyAreasUpdateModel, core, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1);

        // Assert
        var areaRules = await _backendConfigurationPnDbContext!.AreaRules.Where(x => x.PropertyId == property.Id).ToListAsync();
        var areaRuleTranslations = await _backendConfigurationPnDbContext!.AreaRuleTranslations
            .Join(_backendConfigurationPnDbContext.AreaRules, atr => atr.AreaRuleId, ar => ar.Id,
                (atr, ar) => new { atr, ar }).Where(x => x.ar.PropertyId == property.Id).ToListAsync();
        var areaProperties = await _backendConfigurationPnDbContext!.AreaProperties.Where(x => x.PropertyId == property.Id).ToListAsync();

        // Assert result
        Assert.NotNull(result);
        Assert.That(result.Success, Is.EqualTo(true));

        // Assert areaRules
        Assert.NotNull(areaRules);
        Assert.That(areaRules.Count, Is.EqualTo(0));

        // Assert areaRuleTranslations
        Assert.NotNull(areaRuleTranslations);
        Assert.That(areaRuleTranslations.Count, Is.EqualTo(0));

        // Assert areaProperties
        Assert.NotNull(areaProperties);
        Assert.That(areaProperties.Count, Is.EqualTo(1));
    }

    // Should test the Update method and enable "15. Elværktøj" and return success
    [Test]
    public async Task BackendConfigurationPropertyAreasServiceHelper_Update_ElectricalPowerTools_ReturnsSuccess()
    {
        // Arrange
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

        var core = await GetCore();

        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1, 1);

        var property = await _backendConfigurationPnDbContext!.Properties.FirstAsync(x => x.Name == propertyCreateModel.Name).ConfigureAwait(false);
        var areaTranslation = await _backendConfigurationPnDbContext!.AreaTranslations.FirstAsync(x => x.Name == "15. Elværktøj").ConfigureAwait(false);

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
            PropertyId = property.Id
        };

        // Act
        var result = await BackendConfigurationPropertyAreasServiceHelper.Update(propertyAreasUpdateModel, core, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1);

        // Assert
        var areaRules = await _backendConfigurationPnDbContext!.AreaRules.Where(x => x.PropertyId == property.Id).ToListAsync();
        var areaRuleTranslations = await _backendConfigurationPnDbContext!.AreaRuleTranslations
            .Join(_backendConfigurationPnDbContext.AreaRules, atr => atr.AreaRuleId, ar => ar.Id,
                (atr, ar) => new { atr, ar }).Where(x => x.ar.PropertyId == property.Id).ToListAsync();
        var areaProperties = await _backendConfigurationPnDbContext!.AreaProperties.Where(x => x.PropertyId == property.Id).ToListAsync();

        // Assert result
        Assert.NotNull(result);
        Assert.That(result.Success, Is.EqualTo(true));

        // Assert areaRules
        Assert.NotNull(areaRules);
        Assert.That(areaRules.Count, Is.EqualTo(0));

        // Assert areaRuleTranslations
        Assert.NotNull(areaRuleTranslations);
        Assert.That(areaRuleTranslations.Count, Is.EqualTo(0));

        // Assert areaProperties
        Assert.NotNull(areaProperties);
        Assert.That(areaProperties.Count, Is.EqualTo(1));
    }

    // Should test the Update method and enable "16. Stiger" and return success
    [Test]
    public async Task BackendConfigurationPropertyAreasServiceHelper_Update_Ladders_ReturnsSuccess()
    {
        // Arrange
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

        var core = await GetCore();

        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1, 1);

        var property = await _backendConfigurationPnDbContext!.Properties.FirstAsync(x => x.Name == propertyCreateModel.Name).ConfigureAwait(false);
        var areaTranslation = await _backendConfigurationPnDbContext!.AreaTranslations.FirstAsync(x => x.Name == "16. Stiger").ConfigureAwait(false);

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
            PropertyId = property.Id
        };

        // Act
        var result = await BackendConfigurationPropertyAreasServiceHelper.Update(propertyAreasUpdateModel, core, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1);

        // Assert
        var areaRules = await _backendConfigurationPnDbContext!.AreaRules.Where(x => x.PropertyId == property.Id).ToListAsync();
        var areaRuleTranslations = await _backendConfigurationPnDbContext!.AreaRuleTranslations
            .Join(_backendConfigurationPnDbContext.AreaRules, atr => atr.AreaRuleId, ar => ar.Id,
                (atr, ar) => new { atr, ar }).Where(x => x.ar.PropertyId == property.Id).ToListAsync();
        var areaProperties = await _backendConfigurationPnDbContext!.AreaProperties.Where(x => x.PropertyId == property.Id).ToListAsync();

        // Assert result
        Assert.NotNull(result);
        Assert.That(result.Success, Is.EqualTo(true));

        // Assert areaRules
        Assert.NotNull(areaRules);
        Assert.That(areaRules.Count, Is.EqualTo(0));

        // Assert areaRuleTranslations
        Assert.NotNull(areaRuleTranslations);
        Assert.That(areaRuleTranslations.Count, Is.EqualTo(0));

        // Assert areaProperties
        Assert.NotNull(areaProperties);
        Assert.That(areaProperties.Count, Is.EqualTo(1));
    }

    // Should test the Update method and enable "17. Brandslukkere" and return success
    [Test]
    public async Task BackendConfigurationPropertyAreasServiceHelper_Update_FireExtinguishers_ReturnsSuccess()
    {
        // Arrange
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

        var core = await GetCore();

        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1, 1);

        var property = await _backendConfigurationPnDbContext!.Properties.FirstAsync(x => x.Name == propertyCreateModel.Name).ConfigureAwait(false);
        var areaTranslation = await _backendConfigurationPnDbContext!.AreaTranslations.FirstAsync(x => x.Name == "17. Brandslukkere").ConfigureAwait(false);

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
            PropertyId = property.Id
        };

        // Act
        var result = await BackendConfigurationPropertyAreasServiceHelper.Update(propertyAreasUpdateModel, core, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1);

        // Assert
        var areaRules = await _backendConfigurationPnDbContext!.AreaRules.Where(x => x.PropertyId == property.Id).ToListAsync();
        var areaRuleTranslations = await _backendConfigurationPnDbContext!.AreaRuleTranslations
            .Join(_backendConfigurationPnDbContext.AreaRules, atr => atr.AreaRuleId, ar => ar.Id,
                (atr, ar) => new { atr, ar }).Where(x => x.ar.PropertyId == property.Id).ToListAsync();
        var areaProperties = await _backendConfigurationPnDbContext!.AreaProperties.Where(x => x.PropertyId == property.Id).ToListAsync();

        // Assert result
        Assert.NotNull(result);
        Assert.That(result.Success, Is.EqualTo(true));

        // Assert areaRules
        Assert.NotNull(areaRules);
        Assert.That(areaRules.Count, Is.EqualTo(0));

        // Assert areaRuleTranslations
        Assert.NotNull(areaRuleTranslations);
        Assert.That(areaRuleTranslations.Count, Is.EqualTo(0));

        // Assert areaProperties
        Assert.NotNull(areaProperties);
        Assert.That(areaProperties.Count, Is.EqualTo(1));
    }

    // Should test the Update method and enable "18. Alarm" and return success
    [Test]
    public async Task BackendConfigurationPropertyAreasServiceHelper_Update_Alarm_ReturnsSuccess()
    {
        // Arrange
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

        var core = await GetCore();

        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1, 1);

        var property = await _backendConfigurationPnDbContext!.Properties.FirstAsync(x => x.Name == propertyCreateModel.Name).ConfigureAwait(false);
        var areaTranslation = await _backendConfigurationPnDbContext!.AreaTranslations.FirstAsync(x => x.Name == "18. Alarm").ConfigureAwait(false);

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
            PropertyId = property.Id
        };

        // Act
        var result = await BackendConfigurationPropertyAreasServiceHelper.Update(propertyAreasUpdateModel, core, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1);

        // Assert
        var areaRules = await _backendConfigurationPnDbContext!.AreaRules.Where(x => x.PropertyId == property.Id).ToListAsync();
        var areaRuleTranslations = await _backendConfigurationPnDbContext!.AreaRuleTranslations
            .Join(_backendConfigurationPnDbContext.AreaRules, atr => atr.AreaRuleId, ar => ar.Id,
                (atr, ar) => new { atr, ar }).Where(x => x.ar.PropertyId == property.Id).ToListAsync();
        var areaProperties = await _backendConfigurationPnDbContext!.AreaProperties.Where(x => x.PropertyId == property.Id).ToListAsync();

        // Assert result
        Assert.NotNull(result);
        Assert.That(result.Success, Is.EqualTo(true));

        // Assert areaRules
        Assert.NotNull(areaRules);
        Assert.That(areaRules.Count, Is.EqualTo(0));

        // Assert areaRuleTranslations
        Assert.NotNull(areaRuleTranslations);
        Assert.That(areaRuleTranslations.Count, Is.EqualTo(0));

        // Assert areaProperties
        Assert.NotNull(areaProperties);
        Assert.That(areaProperties.Count, Is.EqualTo(1));
    }

    // Should test the Update method and enable "19. Ventilation" and return success
    [Test]
    public async Task BackendConfigurationPropertyAreasServiceHelper_Update_Ventilation_ReturnsSuccess()
    {
        // Arrange
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

        var core = await GetCore();

        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1, 1);

        var property = await _backendConfigurationPnDbContext!.Properties.FirstAsync(x => x.Name == propertyCreateModel.Name).ConfigureAwait(false);
        var areaTranslation = await _backendConfigurationPnDbContext!.AreaTranslations.FirstAsync(x => x.Name == "19. Ventilation").ConfigureAwait(false);

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
            PropertyId = property.Id
        };

        // Act
        var result = await BackendConfigurationPropertyAreasServiceHelper.Update(propertyAreasUpdateModel, core, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1);

        // Assert
        var areaRules = await _backendConfigurationPnDbContext!.AreaRules.Where(x => x.PropertyId == property.Id).ToListAsync();
        var areaRuleTranslations = await _backendConfigurationPnDbContext!.AreaRuleTranslations
            .Join(_backendConfigurationPnDbContext.AreaRules, atr => atr.AreaRuleId, ar => ar.Id,
                (atr, ar) => new { atr, ar }).Where(x => x.ar.PropertyId == property.Id).ToListAsync();
        var areaProperties = await _backendConfigurationPnDbContext!.AreaProperties.Where(x => x.PropertyId == property.Id).ToListAsync();

        // Assert result
        Assert.NotNull(result);
        Assert.That(result.Success, Is.EqualTo(true));

        // Assert areaRules
        Assert.NotNull(areaRules);
        Assert.That(areaRules.Count, Is.EqualTo(0));

        // Assert areaRuleTranslations
        Assert.NotNull(areaRuleTranslations);
        Assert.That(areaRuleTranslations.Count, Is.EqualTo(0));

        // Assert areaProperties
        Assert.NotNull(areaProperties);
        Assert.That(areaProperties.Count, Is.EqualTo(1));
    }

    // Should test the Update method and enable "20. Ugentlige rutineopgaver" and return success
    [Test]
    public async Task BackendConfigurationPropertyAreasServiceHelper_Update_WeeklyTasks_ReturnsSuccess()
    {
        // Arrange
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

        var core = await GetCore();

        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1, 1);

        var property = await _backendConfigurationPnDbContext!.Properties.FirstAsync(x => x.Name == propertyCreateModel.Name).ConfigureAwait(false);
        var areaTranslation = await _backendConfigurationPnDbContext!.AreaTranslations.FirstAsync(x => x.Name == "20. Ugentlige rutineopgaver").ConfigureAwait(false);

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
            PropertyId = property.Id
        };

        // Act
        var result = await BackendConfigurationPropertyAreasServiceHelper.Update(propertyAreasUpdateModel, core, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1);

        // Assert
        var areaRules = await _backendConfigurationPnDbContext!.AreaRules.Where(x => x.PropertyId == property.Id).ToListAsync();
        var areaRuleTranslations = await _backendConfigurationPnDbContext!.AreaRuleTranslations
            .Join(_backendConfigurationPnDbContext.AreaRules, atr => atr.AreaRuleId, ar => ar.Id,
                (atr, ar) => new { atr, ar }).Where(x => x.ar.PropertyId == property.Id).ToListAsync();
        var areaProperties = await _backendConfigurationPnDbContext!.AreaProperties.Where(x => x.PropertyId == property.Id).ToListAsync();

        // Assert result
        Assert.NotNull(result);
        Assert.That(result.Success, Is.EqualTo(true));

        // Assert areaRules
        Assert.NotNull(areaRules);
        Assert.That(areaRules.Count, Is.EqualTo(0));

        // Assert areaRuleTranslations
        Assert.NotNull(areaRuleTranslations);
        Assert.That(areaRuleTranslations.Count, Is.EqualTo(0));

        // Assert areaProperties
        Assert.NotNull(areaProperties);
        Assert.That(areaProperties.Count, Is.EqualTo(1));
    }

    // Should test the Update method and enable "21. DANISH Standard" and return success
    [Test]
    public async Task BackendConfigurationPropertyAreasServiceHelper_Update_DanishStandard_ReturnsSuccess()
    {
        // Arrange
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

        var core = await GetCore();

        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1, 1);

        var property = await _backendConfigurationPnDbContext!.Properties.FirstAsync(x => x.Name == propertyCreateModel.Name).ConfigureAwait(false);
        var areaTranslation = await _backendConfigurationPnDbContext!.AreaTranslations.FirstAsync(x => x.Name == "21. DANISH Standard").ConfigureAwait(false);

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
            PropertyId = property.Id
        };

        // Act
        var result = await BackendConfigurationPropertyAreasServiceHelper.Update(propertyAreasUpdateModel, core, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1);

        // Assert
        var areaRules = await _backendConfigurationPnDbContext!.AreaRules.Where(x => x.PropertyId == property.Id).ToListAsync();
        var areaRuleTranslations = await _backendConfigurationPnDbContext!.AreaRuleTranslations
            .Join(_backendConfigurationPnDbContext.AreaRules, atr => atr.AreaRuleId, ar => ar.Id,
                (atr, ar) => new { atr, ar }).Where(x => x.ar.PropertyId == property.Id).ToListAsync();
        var areaProperties = await _backendConfigurationPnDbContext!.AreaProperties.Where(x => x.PropertyId == property.Id).ToListAsync();

        // Assert result
        Assert.NotNull(result);
        Assert.That(result.Success, Is.EqualTo(true));

        // Assert areaRules
        Assert.NotNull(areaRules);
        Assert.That(areaRules.Count, Is.EqualTo(1));
        Assert.That(areaRules[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[0].PropertyId, Is.EqualTo(property.Id));
        Assert.That(areaRules[0].EformName, Is.EqualTo("21. DANISH Produktstandard v_1_01"));

        // Assert areaRuleTranslations
        Assert.NotNull(areaRuleTranslations);
        Assert.That(areaRuleTranslations.Count, Is.EqualTo(3));
        Assert.That(areaRuleTranslations[0].atr.Name, Is.EqualTo("21. DANISH Standard v. 1.01"));
        Assert.That(areaRuleTranslations[0].atr.LanguageId, Is.EqualTo(1));
        Assert.That(areaRuleTranslations[1].atr.Name, Is.EqualTo("21. DANISH Standard v. 1.01"));
        Assert.That(areaRuleTranslations[1].atr.LanguageId, Is.EqualTo(2));
        Assert.That(areaRuleTranslations[2].atr.Name, Is.EqualTo("21. DÄNISCHER Standard v. 1.01"));
        Assert.That(areaRuleTranslations[2].atr.LanguageId, Is.EqualTo(3));

        // Assert areaProperties
        Assert.NotNull(areaProperties);
        Assert.That(areaProperties.Count, Is.EqualTo(1));
    }

    // Should test the Update method and enable "22. Sigtetest" and return success
    [Test]
    public async Task BackendConfigurationPropertyAreasServiceHelper_Update_SeveTest_ReturnsSuccess()
    {
        // Arrange
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

        var core = await GetCore();

        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1, 1);

        var property = await _backendConfigurationPnDbContext!.Properties.FirstAsync(x => x.Name == propertyCreateModel.Name).ConfigureAwait(false);
        var areaTranslation = await _backendConfigurationPnDbContext!.AreaTranslations.FirstAsync(x => x.Name == "22. Sigtetest").ConfigureAwait(false);

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
            PropertyId = property.Id
        };

        // Act
        var result = await BackendConfigurationPropertyAreasServiceHelper.Update(propertyAreasUpdateModel, core, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1);

        // Assert
        var areaRules = await _backendConfigurationPnDbContext!.AreaRules.Where(x => x.PropertyId == property.Id).ToListAsync();
        var areaRuleTranslations = await _backendConfigurationPnDbContext!.AreaRuleTranslations
            .Join(_backendConfigurationPnDbContext.AreaRules, atr => atr.AreaRuleId, ar => ar.Id,
                (atr, ar) => new { atr, ar }).Where(x => x.ar.PropertyId == property.Id).ToListAsync();
        var areaProperties = await _backendConfigurationPnDbContext!.AreaProperties.Where(x => x.PropertyId == property.Id).ToListAsync();

        // Assert result
        Assert.NotNull(result);
        Assert.That(result.Success, Is.EqualTo(true));

        // Assert areaRules
        Assert.NotNull(areaRules);
        Assert.That(areaRules.Count, Is.EqualTo(0));

        // Assert areaRuleTranslations
        Assert.NotNull(areaRuleTranslations);
        Assert.That(areaRuleTranslations.Count, Is.EqualTo(0));

        // Assert areaProperties
        Assert.NotNull(areaProperties);
        Assert.That(areaProperties.Count, Is.EqualTo(1));
    }

    // Should test the Update method and enable "24. IE-indberetning" and return success
    [Test]
    public async Task BackendConfigurationPropertyAreasServiceHelper_Update_IEReporting_ReturnsSuccess()
    {
        // Arrange
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

        var core = await GetCore();

        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1, 1);

        var property = await _backendConfigurationPnDbContext!.Properties.FirstAsync(x => x.Name == propertyCreateModel.Name).ConfigureAwait(false);
        var areaTranslation = await _backendConfigurationPnDbContext!.AreaTranslations.FirstAsync(x => x.Name == "24. IE-indberetning").ConfigureAwait(false);

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
            PropertyId = property.Id
        };

        // Act
        var result = await BackendConfigurationPropertyAreasServiceHelper.Update(propertyAreasUpdateModel, core, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1);

        // Assert
        var areaRules = await _backendConfigurationPnDbContext!.AreaRules.Where(x => x.PropertyId == property.Id).ToListAsync();
        var areaRuleTranslations = await _backendConfigurationPnDbContext!.AreaRuleTranslations
            .Join(_backendConfigurationPnDbContext.AreaRules, atr => atr.AreaRuleId, ar => ar.Id,
                (atr, ar) => new { atr, ar }).Where(x => x.ar.PropertyId == property.Id).ToListAsync();
        var areaProperties = await _backendConfigurationPnDbContext!.AreaProperties.Where(x => x.PropertyId == property.Id).ToListAsync();

        // Assert result
        Assert.NotNull(result);
        Assert.That(result.Success, Is.EqualTo(true));

        // Assert areaRules
        Assert.NotNull(areaRules);
        Assert.That(areaRules.Count, Is.EqualTo(0));

        // Assert areaRuleTranslations
        Assert.NotNull(areaRuleTranslations);
        Assert.That(areaRuleTranslations.Count, Is.EqualTo(0));

        // Assert areaProperties
        Assert.NotNull(areaProperties);
        Assert.That(areaProperties.Count, Is.EqualTo(1));
    }

    // Should test the Update method and enable "25. KemiKontrol" and return success
    [Test]
    public async Task BackendConfigurationPropertyAreasServiceHelper_Update_ChemicalControl_ReturnsSuccess()
    {
        // Arrange
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

        var core = await GetCore();

        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1, 1);

        var property = await _backendConfigurationPnDbContext!.Properties.FirstAsync(x => x.Name == propertyCreateModel.Name).ConfigureAwait(false);
        var areaTranslation = await _backendConfigurationPnDbContext!.AreaTranslations.FirstAsync(x => x.Name == "25. KemiKontrol").ConfigureAwait(false);

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
            PropertyId = property.Id
        };

        // Act
        var result = await BackendConfigurationPropertyAreasServiceHelper.Update(propertyAreasUpdateModel, core, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1);

        // Assert
        var areaRules = await _backendConfigurationPnDbContext!.AreaRules.Where(x => x.PropertyId == property.Id).ToListAsync();
        var areaRuleTranslations = await _backendConfigurationPnDbContext!.AreaRuleTranslations
            .Join(_backendConfigurationPnDbContext.AreaRules, atr => atr.AreaRuleId, ar => ar.Id,
                (atr, ar) => new { atr, ar }).Where(x => x.ar.PropertyId == property.Id).ToListAsync();
        var areaProperties = await _backendConfigurationPnDbContext!.AreaProperties.Where(x => x.PropertyId == property.Id).ToListAsync();

        // Assert result
        Assert.NotNull(result);
        Assert.That(result.Success, Is.EqualTo(true));

        // Assert areaRules
        Assert.NotNull(areaRules);
        Assert.That(areaRules.Count, Is.EqualTo(1));
        Assert.That(areaRules[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRules[0].PropertyId, Is.EqualTo(property.Id));
        Assert.That(areaRules[0].EformName, Is.EqualTo("25.01 Registrer produkter"));

        // Assert areaRuleTranslations
        Assert.NotNull(areaRuleTranslations);
        Assert.That(areaRuleTranslations.Count, Is.EqualTo(0));


        // Assert areaProperties
        Assert.NotNull(areaProperties);
        Assert.That(areaProperties.Count, Is.EqualTo(1));
    }

    // Should test the Update method and enable "26. Kornlager" and return success
    [Test]
    public async Task BackendConfigurationPropertyAreasServiceHelper_Update_GrainStorage_ReturnsSuccess()
    {
        // Arrange
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

        var core = await GetCore();

        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1, 1);

        var property = await _backendConfigurationPnDbContext!.Properties.FirstAsync(x => x.Name == propertyCreateModel.Name).ConfigureAwait(false);
        var areaTranslation = await _backendConfigurationPnDbContext!.AreaTranslations.FirstAsync(x => x.Name == "26. Kornlager").ConfigureAwait(false);

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
            PropertyId = property.Id
        };

        // Act
        var result = await BackendConfigurationPropertyAreasServiceHelper.Update(propertyAreasUpdateModel, core, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1);

        // Assert
        var areaRules = await _backendConfigurationPnDbContext!.AreaRules.Where(x => x.PropertyId == property.Id).ToListAsync();
        var areaRuleTranslations = await _backendConfigurationPnDbContext!.AreaRuleTranslations
            .Join(_backendConfigurationPnDbContext.AreaRules, atr => atr.AreaRuleId, ar => ar.Id,
                (atr, ar) => new { atr, ar }).Where(x => x.ar.PropertyId == property.Id).ToListAsync();
        var areaProperties = await _backendConfigurationPnDbContext!.AreaProperties.Where(x => x.PropertyId == property.Id).ToListAsync();

        // Assert result
        Assert.NotNull(result);
        Assert.That(result.Success, Is.EqualTo(true));

        // Assert areaRules
        Assert.NotNull(areaRules);
        Assert.That(areaRules.Count, Is.EqualTo(0));

        // Assert areaRuleTranslations
        Assert.NotNull(areaRuleTranslations);
        Assert.That(areaRuleTranslations.Count, Is.EqualTo(0));

        // Assert areaProperties
        Assert.NotNull(areaProperties);
        Assert.That(areaProperties.Count, Is.EqualTo(1));
    }
}