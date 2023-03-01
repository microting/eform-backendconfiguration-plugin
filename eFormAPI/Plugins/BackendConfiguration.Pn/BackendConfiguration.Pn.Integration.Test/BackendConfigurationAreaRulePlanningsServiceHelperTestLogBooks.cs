using BackendConfiguration.Pn.Infrastructure.Helpers;
using BackendConfiguration.Pn.Infrastructure.Models;
using BackendConfiguration.Pn.Infrastructure.Models.AreaRules;
using BackendConfiguration.Pn.Infrastructure.Models.AssignmentWorker;
using BackendConfiguration.Pn.Infrastructure.Models.Properties;
using BackendConfiguration.Pn.Infrastructure.Models.PropertyAreas;
using BackendConfiguration.Pn.Services.RebusService;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using eFormCore;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.eFormCaseTemplateBase.Infrastructure.Data;
using Microting.ItemsPlanningBase.Infrastructure.Data;
using Microting.ItemsPlanningBase.Infrastructure.Enums;
using Microting.TimePlanningBase.Infrastructure.Data;
using Rebus.Bus;
using File = System.IO.File;

namespace BackendConfiguration.Pn.Integration.Test;

[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class BackendConfigurationAreaRulePlanningsServiceHelperTestLogBooks
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
    private CaseTemplatePnDbContext? _caseTemplatePnDbContext;
    private IBus _bus;

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

    private CaseTemplatePnDbContext GetCaseTemplatePnDbContext(string connectionStr)
    {

        var optionsBuilder = new DbContextOptionsBuilder<CaseTemplatePnDbContext>();

        optionsBuilder.UseMySql(connectionStr.Replace("myDb", "420_eform-angular-case-template-plugin"), new MariaDbServerVersion(
            new Version(10, 8)));

        var backendConfigurationPnDbContext = new CaseTemplatePnDbContext(optionsBuilder.Options);
        string file = Path.Combine("SQL", "420_eform-angular-case-template-plugin.sql");
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

        _caseTemplatePnDbContext = GetCaseTemplatePnDbContext(_mySqlTestcontainer.ConnectionString);

        _caseTemplatePnDbContext.Database.SetCommandTimeout(300);

        var rebusService =
            new RebusService(new EFormCoreService(_mySqlTestcontainer.ConnectionString.Replace("myDb", "420_SDK")), new BackendConfigurationLocalizationService());
        rebusService.Start(_mySqlTestcontainer.ConnectionString.Replace("myDb", "420_SDK")).GetAwaiter().GetResult();
        _bus = rebusService.GetBus();

    }

    // Should test the UpdatePlanning method for area rule "00. Logbøger" for areaRule: 0 with repeat type "days" adn repeat every "2"
    [Test]
    public async Task UpdatePlanning_AreaRule0Days2_ReturnsSuccess()
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

        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1, 1);

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
            _timePlanningPnDbContext);

        var properties = await _backendConfigurationPnDbContext!.Properties.ToListAsync();
        var sites = await _microtingDbContext!.Sites.AsNoTracking().ToListAsync();

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
            _backendConfigurationPnDbContext, _caseTemplatePnDbContext, "location", _bus);

        var areaTranslation = await _backendConfigurationPnDbContext!.AreaTranslations.FirstAsync(x => x.Name == "00. Logbøger");
        var areaId = areaTranslation.AreaId;
        var currentSite = await _microtingDbContext!.Sites.OrderByDescending(x => x.Id).FirstAsync();

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

        var result = await BackendConfigurationPropertyAreasServiceHelper.Update(propertyAreasUpdateModel, core, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1);
        var areaRules = await _backendConfigurationPnDbContext!.AreaRules.Where(x => x.PropertyId == properties[0].Id).ToListAsync();

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
            core, 1, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext);

        // Assert
        var areaRuleTranslations = await _backendConfigurationPnDbContext!.AreaRuleTranslations
            .Join(_backendConfigurationPnDbContext.AreaRules, atr => atr.AreaRuleId, ar => ar.Id,
                (atr, ar) => new { atr, ar }).Where(x => x.ar.PropertyId == properties[0].Id).ToListAsync();
        var areaProperties = await _backendConfigurationPnDbContext!.AreaProperties.Where(x => x.PropertyId == properties[0].Id).ToListAsync();
        var folderTranslations = await _microtingDbContext!.FolderTranslations.ToListAsync();
        var areaRulePlannings = await _backendConfigurationPnDbContext!.AreaRulePlannings.ToListAsync();
        var planningSites = await _backendConfigurationPnDbContext.PlanningSites.ToListAsync();
        var plannings = await _itemsPlanningPnDbContext!.Plannings.ToListAsync();
        var planningNameTranslations = await _itemsPlanningPnDbContext.PlanningNameTranslation.ToListAsync();
        var itemPlanningSites = await _itemsPlanningPnDbContext!.PlanningSites.ToListAsync();
        var itemPlanningCases = await _itemsPlanningPnDbContext!.PlanningCases.ToListAsync();
        var itemPlanningCaseSites = await _itemsPlanningPnDbContext!.PlanningCaseSites.ToListAsync();
        var compliances = await _backendConfigurationPnDbContext!.Compliances.ToListAsync();
        var checkListSites = await _microtingDbContext!.CheckListSites.ToListAsync();
        var cases = await _microtingDbContext!.Cases.ToListAsync();

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
        // test last executed time within 1 minute
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
        Assert.That(planningNameTranslations[0].Name, Is.EqualTo(areaRuleTranslations[0].atr.Name));
        Assert.That(planningNameTranslations[0].LanguageId, Is.EqualTo(areaRuleTranslations[0].atr.LanguageId));
        Assert.That(planningNameTranslations[0].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(planningNameTranslations[1].Name, Is.EqualTo(areaRuleTranslations[1].atr.Name));
        Assert.That(planningNameTranslations[1].LanguageId, Is.EqualTo(areaRuleTranslations[1].atr.LanguageId));
        Assert.That(planningNameTranslations[1].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(planningNameTranslations[2].Name, Is.EqualTo(areaRuleTranslations[2].atr.Name));
        Assert.That(planningNameTranslations[2].LanguageId, Is.EqualTo(areaRuleTranslations[2].atr.LanguageId));
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
        Assert.That(itemPlanningSites[0].LastExecutedTime, Is.EqualTo(null));

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
        Assert.That(cases[0].FolderId, Is.EqualTo(null));
        Assert.That(cases[0].Status, Is.EqualTo(66));
        Assert.That(cases[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));

    }

    // Should test the UpdatePlanning method for area rule "00. Logbøger" for areaRule: 1 with repeat type "days" adn repeat every "2"
    [Test]
    public async Task UpdatePlanning_AreaRule1Days2_ReturnsSuccess()
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

        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1, 1);

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
            _timePlanningPnDbContext);

        var properties = await _backendConfigurationPnDbContext!.Properties.ToListAsync();
        var sites = await _microtingDbContext!.Sites.AsNoTracking().ToListAsync();

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
            _backendConfigurationPnDbContext, _caseTemplatePnDbContext, "location", _bus);

        var areaTranslation = await _backendConfigurationPnDbContext!.AreaTranslations.FirstAsync(x => x.Name == "00. Logbøger");
        var areaId = areaTranslation.AreaId;
        var currentSite = await _microtingDbContext!.Sites.OrderByDescending(x => x.Id).FirstAsync();

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

        var result = await BackendConfigurationPropertyAreasServiceHelper.Update(propertyAreasUpdateModel, core, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1);
        var areaRules = await _backendConfigurationPnDbContext!.AreaRules.Where(x => x.PropertyId == properties[0].Id).ToListAsync();

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
            RuleId = areaRules[1].Id,
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
            core, 1, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext);

        // Assert
        var areaRuleTranslations = await _backendConfigurationPnDbContext!.AreaRuleTranslations
            .Join(_backendConfigurationPnDbContext.AreaRules, atr => atr.AreaRuleId, ar => ar.Id,
                (atr, ar) => new { atr, ar }).Where(x => x.ar.PropertyId == properties[0].Id).ToListAsync();
        var areaProperties = await _backendConfigurationPnDbContext!.AreaProperties.Where(x => x.PropertyId == properties[0].Id).ToListAsync();
        var folderTranslations = await _microtingDbContext!.FolderTranslations.ToListAsync();
        var areaRulePlannings = await _backendConfigurationPnDbContext!.AreaRulePlannings.ToListAsync();
        var planningSites = await _backendConfigurationPnDbContext.PlanningSites.ToListAsync();
        var plannings = await _itemsPlanningPnDbContext!.Plannings.ToListAsync();
        var planningNameTranslations = await _itemsPlanningPnDbContext.PlanningNameTranslation.ToListAsync();
        var itemPlanningSites = await _itemsPlanningPnDbContext!.PlanningSites.ToListAsync();
        var itemPlanningCases = await _itemsPlanningPnDbContext!.PlanningCases.ToListAsync();
        var itemPlanningCaseSites = await _itemsPlanningPnDbContext!.PlanningCaseSites.ToListAsync();
        var compliances = await _backendConfigurationPnDbContext!.Compliances.ToListAsync();
        var checkListSites = await _microtingDbContext!.CheckListSites.ToListAsync();
        var cases = await _microtingDbContext!.Cases.ToListAsync();

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
        Assert.That(areaRulePlannings[0].AreaRuleId, Is.EqualTo(areaRules[1].Id));
        Assert.That(areaRulePlannings[0].ItemPlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(areaRulePlannings[0].AreaId, Is.EqualTo(areaTranslation.AreaId));
        Assert.That(areaRulePlannings[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(areaRulePlannings[0].ComplianceEnabled, Is.EqualTo(true));
        Assert.That(areaRulePlannings[0].SendNotifications, Is.EqualTo(true));

        // Assert plannings
        Assert.That(plannings, Is.Not.Null);
        Assert.That(plannings.Count, Is.EqualTo(1));
        Assert.That(plannings[0].RelatedEFormId, Is.EqualTo(areaRules[1].EformId));
        Assert.That(plannings[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
        Assert.That(plannings[0].SdkFolderId, Is.EqualTo(areaRules[1].FolderId));
        Assert.That(plannings[0].LastExecutedTime, Is.Not.Null);
        // test last executed time within 1 minute
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
        Assert.That(planningNameTranslations[0].Name, Is.EqualTo(areaRuleTranslations[3].atr.Name));
        Assert.That(planningNameTranslations[0].LanguageId, Is.EqualTo(areaRuleTranslations[3].atr.LanguageId));
        Assert.That(planningNameTranslations[0].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(planningNameTranslations[1].Name, Is.EqualTo(areaRuleTranslations[4].atr.Name));
        Assert.That(planningNameTranslations[1].LanguageId, Is.EqualTo(areaRuleTranslations[4].atr.LanguageId));
        Assert.That(planningNameTranslations[1].PlanningId, Is.EqualTo(plannings[0].Id));
        Assert.That(planningNameTranslations[2].Name, Is.EqualTo(areaRuleTranslations[5].atr.Name));
        Assert.That(planningNameTranslations[2].LanguageId, Is.EqualTo(areaRuleTranslations[5].atr.LanguageId));
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
        Assert.That(itemPlanningSites[0].LastExecutedTime, Is.EqualTo(null));

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
        Assert.That(cases[0].CheckListId, Is.EqualTo(areaRules[1].EformId));
        Assert.That(cases[0].FolderId, Is.EqualTo(null));
        Assert.That(cases[0].Status, Is.EqualTo(66));
        Assert.That(cases[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));

    }


}