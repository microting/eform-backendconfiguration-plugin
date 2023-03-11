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

namespace BackendConfiguration.Pn.Integration.Test;

public class TestBaseSetup
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

    public BackendConfigurationPnDbContext? BackendConfigurationPnDbContext;
    public ItemsPlanningPnDbContext? ItemsPlanningPnDbContext;
    public TimePlanningPnDbContext? TimePlanningPnDbContext;
    public MicrotingDbContext? MicrotingDbContext;
    public CaseTemplatePnDbContext? CaseTemplatePnDbContext;
    public IBus? Bus;

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

    protected async Task<Core> GetCore()
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

        BackendConfigurationPnDbContext = GetBackendDbContext(_mySqlTestcontainer.ConnectionString);

        BackendConfigurationPnDbContext!.Database.SetCommandTimeout(300);

        ItemsPlanningPnDbContext = GetItemsPlanningPnDbContext(_mySqlTestcontainer.ConnectionString);

        ItemsPlanningPnDbContext.Database.SetCommandTimeout(300);

        TimePlanningPnDbContext = GetTimePlanningPnDbContext(_mySqlTestcontainer.ConnectionString);

        TimePlanningPnDbContext.Database.SetCommandTimeout(300);

        MicrotingDbContext = GetContext(_mySqlTestcontainer.ConnectionString);

        MicrotingDbContext.Database.SetCommandTimeout(300);

        CaseTemplatePnDbContext = GetCaseTemplatePnDbContext(_mySqlTestcontainer.ConnectionString);

        CaseTemplatePnDbContext.Database.SetCommandTimeout(300);

        var rebusService =
            new RebusService(new EFormCoreService(_mySqlTestcontainer.ConnectionString.Replace("myDb", "420_SDK")), new BackendConfigurationLocalizationService());
        rebusService.Start(_mySqlTestcontainer.ConnectionString.Replace("myDb", "420_SDK")).GetAwaiter().GetResult();
        Bus = rebusService.GetBus();
    }
}