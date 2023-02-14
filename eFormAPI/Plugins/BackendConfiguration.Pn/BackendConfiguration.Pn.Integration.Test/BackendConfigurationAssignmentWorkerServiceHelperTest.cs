using BackendConfiguration.Pn.Infrastructure.Helpers;
using BackendConfiguration.Pn.Infrastructure.Models;
using BackendConfiguration.Pn.Infrastructure.Models.AssignmentWorker;
using BackendConfiguration.Pn.Infrastructure.Models.Properties;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.RebusService;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using eFormCore;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Services;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.eFormCaseTemplateBase.Infrastructure.Data;
using Microting.ItemsPlanningBase.Infrastructure.Data;
using Microting.TimePlanningBase.Infrastructure.Data;
using Rebus.Bus;
using File = System.IO.File;

namespace BackendConfiguration.Pn.Integration.Test;

[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class BackendConfigurationAssignmentWorkerServiceHelperTest
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

    // Should test the CreateDeviceUser method and return success
    [Test]
    public async Task BackendConfigurationAssignmentWorkerServiceHelper_CreateDeviceUser_ReturnsSuccess()
    {
        // Arrange
        var core = await GetCore();

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

        // Act
        var result = await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            _timePlanningPnDbContext);

        // Assert
        var sites = await _microtingDbContext!.Sites.AsNoTracking().ToListAsync();
        var workers = await _microtingDbContext.Workers.AsNoTracking().ToListAsync();
        var siteWorkers = await _microtingDbContext.SiteWorkers.AsNoTracking().ToListAsync();
        var units = await _microtingDbContext.Units.AsNoTracking().ToListAsync();

        Assert.NotNull(result);
        Assert.That(sites.Count, Is.EqualTo(3));
        Assert.That(workers.Count, Is.EqualTo(3));
        Assert.That(units.Count, Is.EqualTo(3));

        // Assert site
        Assert.That(sites[2].Name, Is.EqualTo(deviceUserModel.UserFirstName + " " + deviceUserModel.UserLastName));

        // Assert worker
        Assert.That(workers[2].FirstName, Is.EqualTo(deviceUserModel.UserFirstName));
        Assert.That(workers[2].LastName, Is.EqualTo(deviceUserModel.UserLastName));

        // Assert siteWorker
        Assert.That(siteWorkers[2].WorkerId, Is.EqualTo(workers[2].Id));
        Assert.That(siteWorkers[2].SiteId, Is.EqualTo(sites[2].Id));

        // Assert unit
        Assert.That(units[2].SiteId, Is.EqualTo(sites[2].Id));
    }


    // Should test the CreateDeviceUser method with TimeRegistrationEnabled set to true and return success
    [Test]
    public async Task BackendConfigurationAssignmentWorkerServiceHelper_CreateDeviceUser_TimeRegistrationEnabled_ReturnsSuccess()
    {
        // Arrange
        var core = await GetCore();

        var deviceUserModel = new DeviceUserModel
        {
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = true,
            UserFirstName = Guid.NewGuid().ToString(),
            UserLastName = Guid.NewGuid().ToString()
        };

        // Act
        var result = await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            _timePlanningPnDbContext);

        // Assert
        var sites = await _microtingDbContext!.Sites.ToListAsync();
        var workers = await _microtingDbContext.Workers.ToListAsync();
        var siteWorkers = await _microtingDbContext.SiteWorkers.AsNoTracking().ToListAsync();
        var units = await _microtingDbContext.Units.ToListAsync();
        var timeregistrationSiteAssignments = await _timePlanningPnDbContext!.AssignedSites.ToListAsync();

        Assert.NotNull(result);
        Assert.That(sites.Count, Is.EqualTo(3));
        Assert.That(workers.Count, Is.EqualTo(3));
        Assert.That(units.Count, Is.EqualTo(3));

        // Assert site
        Assert.That(sites[2].Name, Is.EqualTo(deviceUserModel.UserFirstName + " " + deviceUserModel.UserLastName));

        // Assert worker
        Assert.That(workers[2].FirstName, Is.EqualTo(deviceUserModel.UserFirstName));
        Assert.That(workers[2].LastName, Is.EqualTo(deviceUserModel.UserLastName));

        // Assert siteWorker
        Assert.That(siteWorkers[2].WorkerId, Is.EqualTo(workers[2].Id));
        Assert.That(siteWorkers[2].SiteId, Is.EqualTo(sites[2].Id));

        // Assert unit
        Assert.That(units[2].SiteId, Is.EqualTo(sites[2].Id));

        // Assert timeregistrationSiteAssignments
        Assert.That(timeregistrationSiteAssignments.Count, Is.EqualTo(1));
        Assert.That(timeregistrationSiteAssignments[0].SiteId, Is.EqualTo(sites[2].MicrotingUid));
    }

    // Should test the UpdateDeviceUser method and return success
    [Test]
    public async Task BackendConfigurationAssignmentWorkerServiceHelper_UpdateDeviceUser_ReturnsSuccess()
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

        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1,
            _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1,1);

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

        var currentSite = await _microtingDbContext!.Sites.OrderByDescending(x => x.Id).FirstAsync();

        var newDeviceUserModel = new DeviceUserModel
        {
            SiteMicrotingUid = (int)currentSite.MicrotingUid!,
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = false,
            UserFirstName = Guid.NewGuid().ToString(),
            UserLastName = Guid.NewGuid().ToString()
        };

        // Act
        var result = await BackendConfigurationAssignmentWorkerServiceHelper.UpdateDeviceUser(newDeviceUserModel, core, 1,
            _backendConfigurationPnDbContext,
            _timePlanningPnDbContext);

        // Assert
        var sites = await _microtingDbContext!.Sites.AsNoTracking().ToListAsync();
        var workers = await _microtingDbContext.Workers.AsNoTracking().ToListAsync();
        var siteWorkers = await _microtingDbContext.SiteWorkers.AsNoTracking().ToListAsync();
        var units = await _microtingDbContext.Units.AsNoTracking().ToListAsync();
        var timeregistrationSiteAssignments = await _timePlanningPnDbContext!.AssignedSites.ToListAsync();

        Assert.NotNull(result);
        Assert.That(sites.Count, Is.EqualTo(3));
        Assert.That(workers.Count, Is.EqualTo(3));
        Assert.That(units.Count, Is.EqualTo(3));

        // Assert site
        Assert.That(sites[2].Name, Is.EqualTo(newDeviceUserModel.UserFirstName + " " + newDeviceUserModel.UserLastName));

        // Assert worker
        Assert.That(workers[2].FirstName, Is.EqualTo(newDeviceUserModel.UserFirstName));
        Assert.That(workers[2].LastName, Is.EqualTo(newDeviceUserModel.UserLastName));

        // Assert siteWorker
        Assert.That(siteWorkers[2].WorkerId, Is.EqualTo(workers[2].Id));
        Assert.That(siteWorkers[2].SiteId, Is.EqualTo(sites[2].Id));

        // Assert unit
        Assert.That(units[2].SiteId, Is.EqualTo(sites[2].Id));

        // Assert timeregistrationSiteAssignments
        Assert.That(timeregistrationSiteAssignments.Count, Is.EqualTo(0));
    }

    // Should test the UpdateDeviceUser method with timeRegistration set to true and return success
    [Test]
    public async Task
        BackendConfigurationAssignmentWorkerServiceHelper_UpdateDeviceUser_TimeRegistrationEnabled_ReturnsSuccess()
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

        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1,
            _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1, 1);

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

        var currentSite = await _microtingDbContext!.Sites.OrderByDescending(x => x.Id).FirstAsync();

        var newDeviceUserModel = new DeviceUserModel
        {
            SiteMicrotingUid = (int)currentSite.MicrotingUid!,
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = true,
            UserFirstName = Guid.NewGuid().ToString(),
            UserLastName = Guid.NewGuid().ToString()
        };

        // Act
        var result = await BackendConfigurationAssignmentWorkerServiceHelper.UpdateDeviceUser(newDeviceUserModel, core,
            1,
            _backendConfigurationPnDbContext,
            _timePlanningPnDbContext);

        // Assert
        var sites = await _microtingDbContext!.Sites.AsNoTracking().ToListAsync();
        var workers = await _microtingDbContext.Workers.AsNoTracking().ToListAsync();
        var siteWorkers = await _microtingDbContext.SiteWorkers.AsNoTracking().ToListAsync();
        var units = await _microtingDbContext.Units.AsNoTracking().ToListAsync();
        var timeregistrationSiteAssignments = await _timePlanningPnDbContext!.AssignedSites.AsNoTracking().ToListAsync();

        Assert.NotNull(result);
        Assert.That(sites.Count, Is.EqualTo(3));
        Assert.That(workers.Count, Is.EqualTo(3));
        Assert.That(units.Count, Is.EqualTo(3));

        // Assert site
        Assert.That(sites[2].Name, Is.EqualTo(newDeviceUserModel.UserFirstName + " " + newDeviceUserModel.UserLastName));

        // Assert worker
        Assert.That(workers[2].FirstName, Is.EqualTo(newDeviceUserModel.UserFirstName));
        Assert.That(workers[2].LastName, Is.EqualTo(newDeviceUserModel.UserLastName));

        // Assert siteWorker
        Assert.That(siteWorkers[2].WorkerId, Is.EqualTo(workers[2].Id));
        Assert.That(siteWorkers[2].SiteId, Is.EqualTo(sites[2].Id));

        // Assert unit
        Assert.That(units[2].SiteId, Is.EqualTo(sites[2].Id));

        // Assert timeregistrationSiteAssignments
        Assert.That(timeregistrationSiteAssignments.Count, Is.EqualTo(1));
        Assert.That(timeregistrationSiteAssignments[0].SiteId, Is.EqualTo(sites[2].MicrotingUid));
    }

    // Should test the UpdateDeviceUser method with timeRegistration set to false and return success
    [Test]
    public async Task
        BackendConfigurationAssignmentWorkerServiceHelper_UpdateDeviceUser_TimeRegistrationDisabled_ReturnsSuccess()
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

        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1,
            _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1, 1);

        var deviceUserModel = new DeviceUserModel
        {
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = true,
            UserFirstName = Guid.NewGuid().ToString(),
            UserLastName = Guid.NewGuid().ToString()
        };

        await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            _timePlanningPnDbContext);

        var currentSite = await _microtingDbContext!.Sites.OrderByDescending(x => x.Id).FirstAsync();

        var newDeviceUserModel = new DeviceUserModel
        {
            SiteMicrotingUid = (int)currentSite.MicrotingUid!,
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = false,
            UserFirstName = Guid.NewGuid().ToString(),
            UserLastName = Guid.NewGuid().ToString()
        };

        // Act
        var result = await BackendConfigurationAssignmentWorkerServiceHelper.UpdateDeviceUser(newDeviceUserModel, core,
            1,
            _backendConfigurationPnDbContext,
            _timePlanningPnDbContext);

        // Assert
        var sites = await _microtingDbContext!.Sites.AsNoTracking().ToListAsync();
        var workers = await _microtingDbContext.Workers.AsNoTracking().ToListAsync();
        var siteWorkers = await _microtingDbContext.SiteWorkers.AsNoTracking().ToListAsync();
        var units = await _microtingDbContext.Units.AsNoTracking().ToListAsync();
        var timeregistrationSiteAssignments = await _timePlanningPnDbContext!.AssignedSites.AsNoTracking().ToListAsync();
        var propertyWorkers = await _backendConfigurationPnDbContext!.PropertyWorkers.AsNoTracking().ToListAsync();

        Assert.NotNull(result);
        Assert.That(sites.Count, Is.EqualTo(3));
        Assert.That(workers.Count, Is.EqualTo(3));
        Assert.That(units.Count, Is.EqualTo(3));

        // Assert site
        Assert.That(sites[2].Name, Is.EqualTo(newDeviceUserModel.UserFirstName + " " + newDeviceUserModel.UserLastName));

        // Assert worker
        Assert.That(workers[2].FirstName, Is.EqualTo(newDeviceUserModel.UserFirstName));
        Assert.That(workers[2].LastName, Is.EqualTo(newDeviceUserModel.UserLastName));

        // Assert siteWorker
        Assert.That(siteWorkers[2].WorkerId, Is.EqualTo(workers[2].Id));
        Assert.That(siteWorkers[2].SiteId, Is.EqualTo(sites[2].Id));

        // Assert unit
        Assert.That(units[2].SiteId, Is.EqualTo(sites[2].Id));

        // Assert timeregistrationSiteAssignments
        Assert.That(timeregistrationSiteAssignments.Count, Is.EqualTo(1));
        Assert.That(timeregistrationSiteAssignments[0].SiteId, Is.EqualTo(sites[2].MicrotingUid));
        Assert.That(timeregistrationSiteAssignments[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));

        // Assert propertyWorkers
        Assert.That(propertyWorkers.Count, Is.EqualTo(0));
    }

    // Should test the Create method and return success
    [Test]
    public async Task BackendConfigurationAssignmentWorkerServiceHelper_Create_ReturnsSuccess()
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

        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1,
            _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1, 1);

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

        var result = await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
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

        // Act
        var result2 = await BackendConfigurationAssignmentWorkerServiceHelper.Create(propertyAssignWorkersModel, core, 1,
             _backendConfigurationPnDbContext, _caseTemplatePnDbContext, "location", _bus);

        // Assert
        var workers = await _microtingDbContext.Workers.AsNoTracking().ToListAsync();
        var siteWorkers = await _microtingDbContext.SiteWorkers.AsNoTracking().ToListAsync();
        var units = await _microtingDbContext.Units.AsNoTracking().ToListAsync();
        var timeregistrationSiteAssignments =
            await _timePlanningPnDbContext!.AssignedSites.AsNoTracking().ToListAsync();
        var propertyWorkers = await _backendConfigurationPnDbContext!.PropertyWorkers.AsNoTracking().ToListAsync();

        Assert.NotNull(result2);
        Assert.That(result2.Success, Is.True);
        Assert.That(sites.Count, Is.EqualTo(3));
        Assert.That(workers.Count, Is.EqualTo(3));
        Assert.That(units.Count, Is.EqualTo(3));

        // Assert site
        Assert.That(sites[2].Name, Is.EqualTo(deviceUserModel.UserFirstName + " " + deviceUserModel.UserLastName));

        // Assert worker
        Assert.That(workers[2].FirstName, Is.EqualTo(deviceUserModel.UserFirstName));

        // Assert siteWorker
        Assert.That(siteWorkers[2].WorkerId, Is.EqualTo(workers[2].Id));

        // Assert unit
        Assert.That(units[2].SiteId, Is.EqualTo(sites[2].Id));

        // Assert timeregistrationSiteAssignments
        Assert.That(timeregistrationSiteAssignments.Count, Is.EqualTo(0));

        // Assert propertyWorkers
        Assert.That(propertyWorkers.Count, Is.EqualTo(1));
        Assert.That(propertyWorkers[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(propertyWorkers[0].WorkerId, Is.EqualTo(workers[2].Id));
    }

    // Should test the Update method and return success
    [Test]
    public async Task BackendConfigurationAssignmentWorkerServiceHelper_Update_ReturnsSuccess()
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

        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1,
            _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1, 1);

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

        var result = await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
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

        var propertyAssignWorkersModel2 = new PropertyAssignWorkersModel
        {
            Assignments = new List<PropertyAssignmentWorkerModel>
            {
                new()
                {
                    PropertyId = properties[0].Id,
                    IsChecked = false
                }
            },
            TaskManagementEnabled = false,
            SiteId = sites[2].Id
        };

        // Act

        var result2 = await BackendConfigurationAssignmentWorkerServiceHelper.Update(propertyAssignWorkersModel2, core, 1,
            _backendConfigurationPnDbContext, _caseTemplatePnDbContext, "location", _bus, _itemsPlanningPnDbContext);

        // Assert
        var workers = await _microtingDbContext.Workers.AsNoTracking().ToListAsync();
        var siteWorkers = await _microtingDbContext.SiteWorkers.AsNoTracking().ToListAsync();
        var units = await _microtingDbContext.Units.AsNoTracking().ToListAsync();
        var timeregistrationSiteAssignments =
            await _timePlanningPnDbContext!.AssignedSites.AsNoTracking().ToListAsync();
        var propertyWorkers = await _backendConfigurationPnDbContext!.PropertyWorkers.AsNoTracking().ToListAsync();

        Assert.NotNull(result2);
        Assert.That(result2.Success, Is.True);
        Assert.That(sites.Count, Is.EqualTo(3));
        Assert.That(workers.Count, Is.EqualTo(3));
        Assert.That(units.Count, Is.EqualTo(3));

        // Assert site
        Assert.That(sites[2].Name, Is.EqualTo(deviceUserModel.UserFirstName + " " + deviceUserModel.UserLastName));

        // Assert worker
        Assert.That(workers[2].FirstName, Is.EqualTo(deviceUserModel.UserFirstName));

        // Assert siteWorker
        Assert.That(siteWorkers[2].WorkerId, Is.EqualTo(workers[2].Id));

        // Assert unit
        Assert.That(units[2].SiteId, Is.EqualTo(sites[2].Id));

        // Assert timeregistrationSiteAssignments
        Assert.That(timeregistrationSiteAssignments.Count, Is.EqualTo(0));

        // Assert propertyWorkers
        Assert.That(propertyWorkers.Count, Is.EqualTo(1));
        Assert.That(propertyWorkers[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(propertyWorkers[0].WorkerId, Is.EqualTo(workers[2].Id));
    }

    // Should test the Update method with TaskManagementEnabled set to true  and return success
    [Test]
    public async Task BackendConfigurationAssignmentWorkerServiceHelper_Update_TaskManagementEnabled_ReturnsSuccess()
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
            WorkorderEnable = true
        };

        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1,
            _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, 1, 1);

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

        var result = await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
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
            TaskManagementEnabled = false,
            TimeRegistrationEnabled = false,
            SiteId = sites[2].Id
        };

        await BackendConfigurationAssignmentWorkerServiceHelper.Create(propertyAssignWorkersModel, core, 1,
            _backendConfigurationPnDbContext, _caseTemplatePnDbContext, "location", _bus);

        var propertyAssignWorkersModel2 = new PropertyAssignWorkersModel
        {
            Assignments = new List<PropertyAssignmentWorkerModel>
            {
                new()
                {
                    PropertyId = properties[0].Id,
                    IsChecked = true
                }
            },
            TaskManagementEnabled = true,
            TimeRegistrationEnabled = false,
            SiteId = sites[2].Id
        };


        // Act
        var result2 = await BackendConfigurationAssignmentWorkerServiceHelper.Update(propertyAssignWorkersModel2, core, 1,
            _backendConfigurationPnDbContext, _caseTemplatePnDbContext, "location", _bus, _itemsPlanningPnDbContext);

        // Assert
        var workers = await _microtingDbContext.Workers.AsNoTracking().ToListAsync();
        var siteWorkers = await _microtingDbContext.SiteWorkers.AsNoTracking().ToListAsync();
        var units = await _microtingDbContext.Units.AsNoTracking().ToListAsync();
        var timeregistrationSiteAssignments =
            await _timePlanningPnDbContext!.AssignedSites.AsNoTracking().ToListAsync();
        var propertyWorkers = await _backendConfigurationPnDbContext!.PropertyWorkers.AsNoTracking().ToListAsync();
        var workOrders = await _backendConfigurationPnDbContext!.WorkorderCases.AsNoTracking().ToListAsync();
        var sdkCases = await _microtingDbContext!.Cases.AsNoTracking().ToListAsync();
        var checkListSites = await _microtingDbContext!.CheckListSites.AsNoTracking().ToListAsync();

        Assert.NotNull(result2);
        Assert.That(result2.Success, Is.True);
        Assert.That(sites.Count, Is.EqualTo(3));
        Assert.That(workers.Count, Is.EqualTo(3));
        Assert.That(units.Count, Is.EqualTo(3));

        // Assert site
        Assert.That(sites[2].Name, Is.EqualTo(deviceUserModel.UserFirstName + " " + deviceUserModel.UserLastName));

        // Assert worker
        Assert.That(workers[2].FirstName, Is.EqualTo(deviceUserModel.UserFirstName));

        // Assert siteWorker
        Assert.That(siteWorkers[2].WorkerId, Is.EqualTo(workers[2].Id));

        // Assert unit
        Assert.That(units[2].SiteId, Is.EqualTo(sites[2].Id));

        // Assert timeregistrationSiteAssignments
        Assert.That(timeregistrationSiteAssignments.Count, Is.EqualTo(0));

        // Assert propertyWorkers
        Assert.That(propertyWorkers.Count, Is.EqualTo(1));
        Assert.That(propertyWorkers[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(propertyWorkers[0].WorkerId, Is.EqualTo(workers[2].Id));

        // Assert workOrders
        Assert.That(workOrders.Count, Is.EqualTo(1));
        Assert.That(workOrders[0].PropertyWorkerId, Is.EqualTo(propertyWorkers[0].Id));
        Assert.That(workOrders[0].LeadingCase, Is.EqualTo(false));

        // Assert sdkCases
        Assert.That(sdkCases.Count, Is.EqualTo(0));

        // Assert checkListSites
        Assert.That(checkListSites.Count, Is.EqualTo(1));
        Assert.That(checkListSites[0].SiteId, Is.EqualTo(sites[2].Id));
        Assert.That(checkListSites[0].MicrotingUid, Is.EqualTo(workOrders[0].CaseId));
    }
}

public class EFormCoreService : IEFormCoreService
{

    private readonly string _connectionString;
    public EFormCoreService(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<Core> GetCore()
    {
        var core = new Core();
        await core.StartSqlOnly(_connectionString);
        return core;
    }

    public void LogEvent(string appendText)
    {
        Console.WriteLine(appendText);
    }

    public void LogException(string appendText)
    {
        Console.WriteLine(appendText);
    }
}

public class BackendConfigurationLocalizationService : IBackendConfigurationLocalizationService
{
    public string GetString(string key)
    {
        return key;
    }

    public string GetString(string format, params object[] args)
    {
        return format;
    }

    public string GetStringWithFormat(string format, params object[] args)
    {
        return format;
    }
}