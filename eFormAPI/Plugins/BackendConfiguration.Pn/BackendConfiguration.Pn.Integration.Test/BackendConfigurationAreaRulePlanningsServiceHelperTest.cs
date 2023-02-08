using BackendConfiguration.Pn.Infrastructure.Helpers;
using BackendConfiguration.Pn.Infrastructure.Models.AreaRules;
using BackendConfiguration.Pn.Services.BackendConfigurationAreaRulePlanningsService;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Data;
using Microting.TimePlanningBase.Infrastructure.Data;
using File = System.IO.File;

namespace BackendConfiguration.Pn.Integration.Test;

public class BackendConfigurationAreaRulePlanningsServiceHelperTest
{

    private readonly MySqlTestcontainer _mySqlTestcontainer = new TestcontainersBuilder<MySqlTestcontainer>()
        // .WithImage("mariadb:10.8")
        // .ConfigureContainer(container =>
        // {
        //     container.Username = "bla";
        //     container.Password = "secretpassword";
        // })
        .WithDatabase(new MySqlTestcontainerConfiguration(image: "mariadb:10.8")
        {
            Database = "myDb",
            Username = "root",
            Password = "secretpassword",
        })
        .WithEnvironment("MYSQL_ROOT_PASSWORD", "secretpassword")
        .Build();
    protected BackendConfigurationPnDbContext BackendConfigurationPnDbContext;
    protected ItemsPlanningPnDbContext ItemsPlanningPnDbContext;
    protected TimePlanningPnDbContext TimePlanningPnDbContext;
    protected string ConnectionString;

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


    [SetUp]
    public async Task Setup()
    {
        Console.WriteLine($"{DateTime.Now} : Starting MariaDb Container...");
        await _mySqlTestcontainer.StartAsync();
        Console.WriteLine($"{DateTime.Now} : Started MariaDb Container");
        ConnectionString = _mySqlTestcontainer.ConnectionString;

        BackendConfigurationPnDbContext = GetBackendDbContext(_mySqlTestcontainer.ConnectionString);

        BackendConfigurationPnDbContext.Database.SetCommandTimeout(300);

        ItemsPlanningPnDbContext = GetItemsPlanningPnDbContext(_mySqlTestcontainer.ConnectionString);

        ItemsPlanningPnDbContext.Database.SetCommandTimeout(300);

        TimePlanningPnDbContext = GetTimePlanningPnDbContext(_mySqlTestcontainer.ConnectionString);

        TimePlanningPnDbContext.Database.SetCommandTimeout(300);

    }

    // Should test the CreateAreaRulePlanningObject method
    [Test]
    public async Task
        BackendConfigurationAreaRulePlanningsServiceHelper_CreateAreaRulePlanningObject_DoesCreateAreaRulePlanningObject()
    {
        // Arrange
        // AreaRulePlanningModel areaRulePlanningModel,
        // AreaRule areaRule, int planningId, int folderId, BackendConfigurationPnDbContext dbContext, int userId
        AreaRulePlanningModel areaRulePlanningModel = new AreaRulePlanningModel();
        AreaRule areaRule = new AreaRule();
        int planningId = 1;
        int folderId = 1;
        int userId = 1;

        // Act
        var result = await BackendConfigurationAreaRulePlanningsServiceHelper.CreateAreaRulePlanningObject(
            areaRulePlanningModel, areaRule, planningId, folderId, BackendConfigurationPnDbContext, userId);

        // Assert
        Assert.NotNull(result);
    }

}