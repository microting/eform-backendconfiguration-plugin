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
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.eFormCaseTemplateBase.Infrastructure.Data;
using Microting.ItemsPlanningBase.Infrastructure.Data;
using Microting.TimePlanningBase.Infrastructure.Data;
using Rebus.Bus;
using File = System.IO.File;

namespace BackendConfiguration.Pn.Integration.Test;

[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class BackendConfigurationAreaRulePlanningsServiceHelperTest
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

    // Should test the CreateAreaRulePlanningObject method
    [Test]
    public async Task
        BackendConfigurationAreaRulePlanningsServiceHelper_CreateAreaRulePlanningObject_DoesCreateAreaRulePlanningObject()
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

        // should create a device user model and assign it to the property
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

        var result2 = await BackendConfigurationAssignmentWorkerServiceHelper.Create(propertyAssignWorkersModel, core, 1,
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

        // should create AreaRulePlanningModel for areaId
        var areaRulePlanningModel = new AreaRulePlanningModel()
        {
            AssignedSites = new List<AreaRuleAssignedSitesModel>()
            {
                new AreaRuleAssignedSitesModel()
                {
                    Checked = true,
                    SiteId = currentSite.Id

                }
            }
        };

        //var result3 = await UpdatePlanning(areaRulePlanningModel);

    }

}