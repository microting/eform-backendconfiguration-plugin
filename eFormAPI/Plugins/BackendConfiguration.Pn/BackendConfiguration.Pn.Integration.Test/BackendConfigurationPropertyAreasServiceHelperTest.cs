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
        var folderTranslations = await _microtingDbContext!.FolderTranslations.ToListAsync();

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
        var folderTranslations = await _microtingDbContext!.FolderTranslations.ToListAsync();

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
        Assert.That(folderTranslations[28].Name, Is.EqualTo("03. Gyllebeholdere"));
        Assert.That(folderTranslations[28].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[29].Name, Is.EqualTo("03. Slurry tanks"));
        Assert.That(folderTranslations[29].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[30].Name, Is.EqualTo("03. Gülletanks"));
        Assert.That(folderTranslations[30].LanguageId, Is.EqualTo(3));
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
        var folderTranslations = await _microtingDbContext!.FolderTranslations.ToListAsync();

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
        Assert.That(folderTranslations[28].Name, Is.EqualTo("04. Foderindlægssedler"));
        Assert.That(folderTranslations[28].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[29].Name, Is.EqualTo("04. Feeding documentation (kun IE-livestock only)")); // TODO fix translation
        Assert.That(folderTranslations[29].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[30].Name, Is.EqualTo("04. Fütterungsdokumentation (nur IE Vieh)"));
        Assert.That(folderTranslations[30].LanguageId, Is.EqualTo(3));
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
        var folderTranslations = await _microtingDbContext!.FolderTranslations.ToListAsync();

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
        Assert.That(folderTranslations[28].Name, Is.EqualTo("05. Stalde: Halebid og klargøring"));
        Assert.That(folderTranslations[28].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[29].Name, Is.EqualTo("05. Stables: Tail biting and preparation"));
        Assert.That(folderTranslations[29].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[30].Name, Is.EqualTo("05. Ställe: Schwanzbeißen und Vorbereitung"));
        Assert.That(folderTranslations[30].LanguageId, Is.EqualTo(3));
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
        var folderTranslations = await _microtingDbContext!.FolderTranslations.ToListAsync();

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
        Assert.That(folderTranslations[28].Name, Is.EqualTo("06. Fodersiloer"));
        Assert.That(folderTranslations[28].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[29].Name, Is.EqualTo("06. Silos"));
        Assert.That(folderTranslations[29].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[30].Name, Is.EqualTo("06. Silos"));
        Assert.That(folderTranslations[30].LanguageId, Is.EqualTo(3));
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
        var folderTranslations = await _microtingDbContext!.FolderTranslations.ToListAsync();

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
        Assert.That(folderTranslations[28].Name, Is.EqualTo("07. Skadedyrsbekæmpelse"));
        Assert.That(folderTranslations[28].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[29].Name, Is.EqualTo("07. Pest control"));
        Assert.That(folderTranslations[29].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[30].Name, Is.EqualTo("07. Schädlingsbekämpfung"));
        Assert.That(folderTranslations[30].LanguageId, Is.EqualTo(3));
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
        var folderTranslations = await _microtingDbContext!.FolderTranslations.ToListAsync();

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
        Assert.That(folderTranslations[28].Name, Is.EqualTo("08. Luftrensning"));
        Assert.That(folderTranslations[28].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[29].Name, Is.EqualTo("08. Aircleaning"));
        Assert.That(folderTranslations[29].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[30].Name, Is.EqualTo("08. Luftreinigung"));
        Assert.That(folderTranslations[30].LanguageId, Is.EqualTo(3));
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
        var folderTranslations = await _microtingDbContext!.FolderTranslations.ToListAsync();

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
        Assert.That(folderTranslations[28].Name, Is.EqualTo("09. Forsuring"));
        Assert.That(folderTranslations[28].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[29].Name, Is.EqualTo("09. Acidification"));
        Assert.That(folderTranslations[29].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[30].Name, Is.EqualTo("09. Ansäuerung"));
        Assert.That(folderTranslations[30].LanguageId, Is.EqualTo(3));
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
        var folderTranslations = await _microtingDbContext!.FolderTranslations.ToListAsync();

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
        Assert.That(folderTranslations[28].Name, Is.EqualTo("10. Varmepumper"));
        Assert.That(folderTranslations[28].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[29].Name, Is.EqualTo("10. Heat pumps"));
        Assert.That(folderTranslations[29].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[30].Name, Is.EqualTo("10. Wärmepumpen"));
        Assert.That(folderTranslations[30].LanguageId, Is.EqualTo(3));
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
        var folderTranslations = await _microtingDbContext!.FolderTranslations.ToListAsync();

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
        Assert.That(folderTranslations[28].Name, Is.EqualTo("11. Varmekilder"));
        Assert.That(folderTranslations[28].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[29].Name, Is.EqualTo("11. Heat sources"));
        Assert.That(folderTranslations[29].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[30].Name, Is.EqualTo("11. Wärmequellen"));
        Assert.That(folderTranslations[30].LanguageId, Is.EqualTo(3));
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
        var folderTranslations = await _microtingDbContext!.FolderTranslations.ToListAsync();

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
        Assert.That(folderTranslations[28].Name, Is.EqualTo("12. Miljøfarlige stoffer"));
        Assert.That(folderTranslations[28].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[29].Name, Is.EqualTo("12. Environmentally hazardous substances"));
        Assert.That(folderTranslations[29].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[30].Name, Is.EqualTo("12. Umweltgefährdende Stoffe"));
        Assert.That(folderTranslations[30].LanguageId, Is.EqualTo(3));
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
        var folderTranslations = await _microtingDbContext!.FolderTranslations.ToListAsync();

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
        Assert.That(folderTranslations[28].Name, Is.EqualTo("13. APV Landbrug"));
        Assert.That(folderTranslations[28].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[29].Name, Is.EqualTo("13. APV Agriculture"));
        Assert.That(folderTranslations[29].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[30].Name, Is.EqualTo("13. APV Landwirtschaft"));
        Assert.That(folderTranslations[30].LanguageId, Is.EqualTo(3));
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
        var folderTranslations = await _microtingDbContext!.FolderTranslations.ToListAsync();

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
        Assert.That(folderTranslations[28].Name, Is.EqualTo("14. Maskiner"));
        Assert.That(folderTranslations[28].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[29].Name, Is.EqualTo("14. Machines"));
        Assert.That(folderTranslations[29].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[30].Name, Is.EqualTo("14. Machinen"));
        Assert.That(folderTranslations[30].LanguageId, Is.EqualTo(3));
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
        var folderTranslations = await _microtingDbContext!.FolderTranslations.ToListAsync();

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
        Assert.That(folderTranslations[28].Name, Is.EqualTo("15. Elværktøj"));
        Assert.That(folderTranslations[28].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[29].Name, Is.EqualTo("15. Inspection of power tools"));
        Assert.That(folderTranslations[29].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[30].Name, Is.EqualTo("15. Inspektion von Elektrowerkzeugen"));
        Assert.That(folderTranslations[30].LanguageId, Is.EqualTo(3));
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
        var folderTranslations = await _microtingDbContext!.FolderTranslations.ToListAsync();

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
        Assert.That(folderTranslations[28].Name, Is.EqualTo("16. Stiger"));
        Assert.That(folderTranslations[28].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[29].Name, Is.EqualTo("16. Ladders"));
        Assert.That(folderTranslations[29].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[30].Name, Is.EqualTo("16. Leitern"));
        Assert.That(folderTranslations[30].LanguageId, Is.EqualTo(3));
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
        var folderTranslations = await _microtingDbContext!.FolderTranslations.ToListAsync();

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
        Assert.That(folderTranslations[28].Name, Is.EqualTo("17. Brandslukkere"));
        Assert.That(folderTranslations[28].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[29].Name, Is.EqualTo("17. Fire extinguishers"));
        Assert.That(folderTranslations[29].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[30].Name, Is.EqualTo("17. Feuerlöscher"));
        Assert.That(folderTranslations[30].LanguageId, Is.EqualTo(3));
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
        var folderTranslations = await _microtingDbContext!.FolderTranslations.ToListAsync();

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
        Assert.That(folderTranslations[28].Name, Is.EqualTo("18. Alarm"));
        Assert.That(folderTranslations[28].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[29].Name, Is.EqualTo("18. Alarm"));
        Assert.That(folderTranslations[29].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[30].Name, Is.EqualTo("18. Alarm"));
        Assert.That(folderTranslations[30].LanguageId, Is.EqualTo(3));
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
        var folderTranslations = await _microtingDbContext!.FolderTranslations.ToListAsync();

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
        Assert.That(folderTranslations[28].Name, Is.EqualTo("19. Ventilation"));
        Assert.That(folderTranslations[28].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[29].Name, Is.EqualTo("19. Ventilation"));
        Assert.That(folderTranslations[29].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[30].Name, Is.EqualTo("19. Belüftung"));
        Assert.That(folderTranslations[30].LanguageId, Is.EqualTo(3));
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
        var folderTranslations = await _microtingDbContext!.FolderTranslations.ToListAsync();

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

        // Assert folder translations
        Assert.That(folderTranslations, Is.Not.Null);
        Assert.That(folderTranslations.Count, Is.EqualTo(52));
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
        Assert.That(folderTranslations[28].Name, Is.EqualTo("20. Ugentlige rutineopgaver"));
        Assert.That(folderTranslations[28].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[29].Name, Is.EqualTo("20. Weekly routine tasks"));
        Assert.That(folderTranslations[29].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[30].Name, Is.EqualTo("20. Wöchentliche Routineaufgaben"));
        Assert.That(folderTranslations[30].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[31].Name, Is.EqualTo("20.07 Søndag"));
        Assert.That(folderTranslations[31].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[32].Name, Is.EqualTo("20.07 Sunday"));
        Assert.That(folderTranslations[32].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[33].Name, Is.EqualTo("20.07 Sonntag"));
        Assert.That(folderTranslations[33].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[34].Name, Is.EqualTo("20.01 Mandag"));
        Assert.That(folderTranslations[34].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[35].Name, Is.EqualTo("20.01 Monday"));
        Assert.That(folderTranslations[35].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[36].Name, Is.EqualTo("20.01 Montag"));
        Assert.That(folderTranslations[36].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[37].Name, Is.EqualTo("20.02 Tirsdag"));
        Assert.That(folderTranslations[37].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[38].Name, Is.EqualTo("20.02 Tuesday"));
        Assert.That(folderTranslations[38].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[39].Name, Is.EqualTo("20.02 Dienstag"));
        Assert.That(folderTranslations[39].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[40].Name, Is.EqualTo("20.03 Onsdag"));
        Assert.That(folderTranslations[40].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[41].Name, Is.EqualTo("20.03 Wednesday"));
        Assert.That(folderTranslations[41].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[42].Name, Is.EqualTo("20.03 Mittwoch"));
        Assert.That(folderTranslations[42].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[43].Name, Is.EqualTo("20.04 Torsdag"));
        Assert.That(folderTranslations[43].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[44].Name, Is.EqualTo("20.04 Thursday"));
        Assert.That(folderTranslations[44].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[45].Name, Is.EqualTo("20.04 Donnerstag"));
        Assert.That(folderTranslations[45].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[46].Name, Is.EqualTo("20.05 Fredag"));
        Assert.That(folderTranslations[46].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[47].Name, Is.EqualTo("20.05 Friday"));
        Assert.That(folderTranslations[47].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[48].Name, Is.EqualTo("20.05 Freitag"));
        Assert.That(folderTranslations[48].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[49].Name, Is.EqualTo("20.06 Lørdag"));
        Assert.That(folderTranslations[49].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[50].Name, Is.EqualTo("20.06 Saturday"));
        Assert.That(folderTranslations[50].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[51].Name, Is.EqualTo("20.06 Samstag"));
        Assert.That(folderTranslations[51].LanguageId, Is.EqualTo(3));
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
        var folderTranslations = await _microtingDbContext!.FolderTranslations.ToListAsync();

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
        Assert.That(folderTranslations[28].Name, Is.EqualTo("21. DANISH Standard"));
        Assert.That(folderTranslations[28].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[29].Name, Is.EqualTo("21. DANISH Standard"));
        Assert.That(folderTranslations[29].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[30].Name, Is.EqualTo("21. DANISH Standard"));
        Assert.That(folderTranslations[30].LanguageId, Is.EqualTo(3));
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
        var folderTranslations = await _microtingDbContext!.FolderTranslations.ToListAsync();

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
        Assert.That(folderTranslations[28].Name, Is.EqualTo("22. Sigtetest"));
        Assert.That(folderTranslations[28].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[29].Name, Is.EqualTo("22. Sieve test"));
        Assert.That(folderTranslations[29].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[30].Name, Is.EqualTo("22. Testen mit Sieb"));
        Assert.That(folderTranslations[30].LanguageId, Is.EqualTo(3));
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
        var folderTranslations = await _microtingDbContext!.FolderTranslations.ToListAsync();

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

        // Assert folder translations
        Assert.That(folderTranslations, Is.Not.Null);
        Assert.That(folderTranslations.Count, Is.EqualTo(106));
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
        Assert.That(folderTranslations[28].Name, Is.EqualTo("24. IE-indberetning"));
        Assert.That(folderTranslations[28].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[29].Name, Is.EqualTo("24. IE Reporting"));
        Assert.That(folderTranslations[29].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[30].Name, Is.EqualTo("24. IE-Berichterstattung"));
        Assert.That(folderTranslations[30].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[31].Name, Is.EqualTo("24.00 Aflæsninger"));
        Assert.That(folderTranslations[31].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[32].Name, Is.EqualTo("24.00 Readings environmental management")); // TODO: This should be "24.00 Readings"
        Assert.That(folderTranslations[32].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[33].Name, Is.EqualTo("24.00 Messungen Umweltmanagement")); // TODO: This should be "24.00 Messungen"
        Assert.That(folderTranslations[33].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[34].Name, Is.EqualTo("24.01 Logbøger og bilag"));
        Assert.That(folderTranslations[34].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[35].Name, Is.EqualTo("24.01 Logbooks and appendices"));
        Assert.That(folderTranslations[35].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[36].Name, Is.EqualTo("24.01 Logbücher und Anhänge"));
        Assert.That(folderTranslations[36].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[37].Name, Is.EqualTo("24.01.01 Gyllebeholdere"));
        Assert.That(folderTranslations[37].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[38].Name, Is.EqualTo("24.01.01 Manure containers"));
        Assert.That(folderTranslations[38].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[39].Name, Is.EqualTo("24.01.01 Güllebehälter"));
        Assert.That(folderTranslations[39].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[40].Name, Is.EqualTo("24.01.02 Gyllekøling"));
        Assert.That(folderTranslations[40].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[41].Name, Is.EqualTo("24.01.02 Slurry cooling"));
        Assert.That(folderTranslations[41].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[42].Name, Is.EqualTo("24.01.02 Schlammkühlung"));
        Assert.That(folderTranslations[42].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[43].Name, Is.EqualTo("24.01.03 Forsuring"));
        Assert.That(folderTranslations[43].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[44].Name, Is.EqualTo("24.01.03 Acidification"));
        Assert.That(folderTranslations[44].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[45].Name, Is.EqualTo("24.01.03 Versauerung"));
        Assert.That(folderTranslations[45].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[46].Name, Is.EqualTo("24.01.04 Ugentlig udslusning af gylle"));
        Assert.That(folderTranslations[46].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[47].Name, Is.EqualTo("24.01.04 Weekly slurry disposal"));
        Assert.That(folderTranslations[47].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[48].Name, Is.EqualTo("24.01.04 Wöchentliche Gülleentsorgung"));
        Assert.That(folderTranslations[48].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[49].Name, Is.EqualTo("24.01.05 Punktudsugning i slagtesvinestalde"));
        Assert.That(folderTranslations[49].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[50].Name, Is.EqualTo("24.01.05 Point extraction in fattening pig stables"));
        Assert.That(folderTranslations[50].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[51].Name, Is.EqualTo("24.01.05 Punktabsaugung in Mastschweineställen"));
        Assert.That(folderTranslations[51].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[52].Name, Is.EqualTo("24.01.06 Varmevekslere til traditionelle slagtekyllingestalde"));
        Assert.That(folderTranslations[52].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[53].Name, Is.EqualTo("24.01.06 Heat exchangers for traditional broiler houses"));
        Assert.That(folderTranslations[53].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[54].Name, Is.EqualTo("24.01.06 Wärmetauscher für traditionelle Masthähnchenställe"));
        Assert.That(folderTranslations[54].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[55].Name, Is.EqualTo("24.01.07 Gødningsbånd til æglæggende høns"));
        Assert.That(folderTranslations[55].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[56].Name, Is.EqualTo("24.01.07 Manure belt for laying hens"));
        Assert.That(folderTranslations[56].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[57].Name, Is.EqualTo("24.01.07 Kotband für Legehennen"));
        Assert.That(folderTranslations[57].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[58].Name, Is.EqualTo("24.01.08 Biologisk luftrensning"));
        Assert.That(folderTranslations[58].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[59].Name, Is.EqualTo("24.01.08 Biological air purification"));
        Assert.That(folderTranslations[59].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[60].Name, Is.EqualTo("24.01.08 Biologische Luftreinigung"));
        Assert.That(folderTranslations[60].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[61].Name, Is.EqualTo("24.01.09 Kemisk luftrensning"));
        Assert.That(folderTranslations[61].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[62].Name, Is.EqualTo("24.01.09 Chemical air purification"));
        Assert.That(folderTranslations[62].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[63].Name, Is.EqualTo("24.01.09 Chemische Luftreinigung"));
        Assert.That(folderTranslations[63].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[64].Name, Is.EqualTo("24.02 Kontroller og bilag"));
        Assert.That(folderTranslations[64].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[65].Name, Is.EqualTo("24.02 Checks and attachments"));
        Assert.That(folderTranslations[65].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[66].Name, Is.EqualTo("24.02 Schecks und Anhänge"));
        Assert.That(folderTranslations[66].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[67].Name, Is.EqualTo("24.02.01 Visuel kontrol af tom gyllebeholdere"));
        Assert.That(folderTranslations[67].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[68].Name, Is.EqualTo("24.02.01 Visual inspection of empty slurry tankers"));
        Assert.That(folderTranslations[68].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[69].Name, Is.EqualTo("24.02.01 Sichtprüfung von leeren Güllefässern"));
        Assert.That(folderTranslations[69].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[70].Name, Is.EqualTo("24.02.02 Gyllepumper"));
        Assert.That(folderTranslations[70].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[71].Name, Is.EqualTo("24.02.02 Slurry pumps"));
        Assert.That(folderTranslations[71].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[72].Name, Is.EqualTo("24.02.02 Schlammpumpen"));
        Assert.That(folderTranslations[72].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[73].Name, Is.EqualTo("24.02.03 Forsyningssystemer til vand og foder"));
        Assert.That(folderTranslations[73].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[74].Name, Is.EqualTo("24.02.03 Water and feed supply systems"));
        Assert.That(folderTranslations[74].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[75].Name, Is.EqualTo("24.02.03 Wasser- und Futterversorgungssysteme"));
        Assert.That(folderTranslations[75].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[76].Name, Is.EqualTo("24.02.04 Varme-, køle- og ventilationssystemer"));
        Assert.That(folderTranslations[76].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[77].Name, Is.EqualTo("24.02.04 Heating, cooling and ventilation systems"));
        Assert.That(folderTranslations[77].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[78].Name, Is.EqualTo("24.02.04 Heiz-, Kühl- und Lüftungssysteme"));
        Assert.That(folderTranslations[78].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[79].Name, Is.EqualTo("24.02.05 Siloer og materiel i transportudstyr i forbindelse med foderanlæg - rør, snegle mv."));
        Assert.That(folderTranslations[79].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[80].Name, Is.EqualTo("24.02.05 Silos and equipment in transport equipment in connection with feed systems - pipes, augers, etc."));
        Assert.That(folderTranslations[80].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[81].Name, Is.EqualTo("24.02.05 Silos und Einrichtungen in Transporteinrichtungen in Verbindung mit Beschickungssystemen - Rohre, Schnecken usw."));
        Assert.That(folderTranslations[81].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[82].Name, Is.EqualTo("24.02.06 Luftrensningssystemer"));
        Assert.That(folderTranslations[82].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[83].Name, Is.EqualTo("24.02.06 Air purification systems"));
        Assert.That(folderTranslations[83].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[84].Name, Is.EqualTo("24.02.06 Luftreinigungssysteme"));
        Assert.That(folderTranslations[84].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[85].Name, Is.EqualTo("24.02.07 Udstyr til drikkevand"));
        Assert.That(folderTranslations[85].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[86].Name, Is.EqualTo("24.02.07 Equipment for drinking water"));
        Assert.That(folderTranslations[86].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[87].Name, Is.EqualTo("24.02.07 Ausrüstung für Trinkwasser"));
        Assert.That(folderTranslations[87].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[88].Name, Is.EqualTo("24.02.08 Maskiner til udbringning af husdyrgødning samt doseringsmekanisme"));
        Assert.That(folderTranslations[88].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[89].Name, Is.EqualTo("24.02.08 Machines for application of livestock manure and dosing mechanism"));
        Assert.That(folderTranslations[89].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[90].Name, Is.EqualTo("24.02.08 Maschinen zum Ausbringen von Viehmist und Dosiermechanismus"));
        Assert.That(folderTranslations[90].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[91].Name, Is.EqualTo("24.03 Miljøledelse"));
        Assert.That(folderTranslations[91].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[92].Name, Is.EqualTo("24.03 Environmental management"));
        Assert.That(folderTranslations[92].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[93].Name, Is.EqualTo("24.03 Umweltmanagement"));
        Assert.That(folderTranslations[93].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[94].Name, Is.EqualTo("24.04 Fodringskrav"));
        Assert.That(folderTranslations[94].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[95].Name, Is.EqualTo("24.04 Feeding requirements"));
        Assert.That(folderTranslations[95].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[96].Name, Is.EqualTo("24.04 Fütterungsanforderungen"));
        Assert.That(folderTranslations[96].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[97].Name, Is.EqualTo("24.04.01 Fasefodring"));
        Assert.That(folderTranslations[97].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[98].Name, Is.EqualTo("24.04.01 Phase feeding"));
        Assert.That(folderTranslations[98].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[99].Name, Is.EqualTo("24.04.01 Phasenfütterung"));
        Assert.That(folderTranslations[99].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[100].Name, Is.EqualTo("24.04.02 Reduceret indhold af råprotein"));
        Assert.That(folderTranslations[100].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[101].Name, Is.EqualTo("24.04.02 Reduced content of crude protein"));
        Assert.That(folderTranslations[101].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[102].Name, Is.EqualTo("24.04.02 Reduzierter Gehalt an Rohprotein"));
        Assert.That(folderTranslations[102].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[103].Name, Is.EqualTo("24.04.03 Tilsætningsstoffer i foder - fytase eller andet"));
        Assert.That(folderTranslations[103].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[104].Name, Is.EqualTo("24.04.03 Additives in feed - phytase or other"));
        Assert.That(folderTranslations[104].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[105].Name, Is.EqualTo("24.04.03 Zusatzstoffe in Futtermitteln – Phytase oder andere"));
        Assert.That(folderTranslations[105].LanguageId, Is.EqualTo(3));
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
        var folderTranslations = await _microtingDbContext!.FolderTranslations.ToListAsync();

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

        // Assert folder translations
        Assert.That(folderTranslations, Is.Not.Null);
        Assert.That(folderTranslations.Count, Is.EqualTo(52));
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
        Assert.That(folderTranslations[28].Name, Is.EqualTo("25. KemiKontrol"));
        Assert.That(folderTranslations[28].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[29].Name, Is.EqualTo("25. Chemistry Control"));
        Assert.That(folderTranslations[29].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[30].Name, Is.EqualTo("25. Chemiekontrolle"));
        Assert.That(folderTranslations[30].LanguageId, Is.EqualTo(3));
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
        var folderTranslations = await _microtingDbContext!.FolderTranslations.ToListAsync();

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
        Assert.That(folderTranslations[28].Name, Is.EqualTo("26. Kornlager"));
        Assert.That(folderTranslations[28].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[29].Name, Is.EqualTo("26. Grain store")); // TODO: This should be "26. Grain storage"
        Assert.That(folderTranslations[29].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[30].Name, Is.EqualTo("26. Getreidelagerung"));
        Assert.That(folderTranslations[30].LanguageId, Is.EqualTo(3));
    }
}