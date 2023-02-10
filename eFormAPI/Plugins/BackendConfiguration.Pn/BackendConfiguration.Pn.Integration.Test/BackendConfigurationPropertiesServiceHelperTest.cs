using BackendConfiguration.Pn.Infrastructure.Helpers;
using BackendConfiguration.Pn.Infrastructure.Models.Properties;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using ClosedXML.Excel;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using eFormCore;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Localization;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Data;
using Microting.TimePlanningBase.Infrastructure.Data;
using File = System.IO.File;


namespace BackendConfiguration.Pn.Integration.Test;

public class BackendConfigurationPropertiesServiceHelperTest
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
    protected MicrotingDbContext MicrotingDbContext;
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
        ConnectionString = _mySqlTestcontainer.ConnectionString;

        BackendConfigurationPnDbContext = GetBackendDbContext(_mySqlTestcontainer.ConnectionString);

        BackendConfigurationPnDbContext.Database.SetCommandTimeout(300);

        ItemsPlanningPnDbContext = GetItemsPlanningPnDbContext(_mySqlTestcontainer.ConnectionString);

        ItemsPlanningPnDbContext.Database.SetCommandTimeout(300);

        TimePlanningPnDbContext = GetTimePlanningPnDbContext(_mySqlTestcontainer.ConnectionString);

        TimePlanningPnDbContext.Database.SetCommandTimeout(300);

        MicrotingDbContext = GetContext(_mySqlTestcontainer.ConnectionString);

        MicrotingDbContext.Database.SetCommandTimeout(300);
    }

    // Should test the Create method with correct information
    [Test]
    public async Task BackendConfigurationPropertiesServiceHelper_Create_DoesCreate()
    {
        // Arrange
        var propertyCreateModel = new PropertyCreateModel
        {
            Address = Guid.NewGuid().ToString(),
            Chr = Guid.NewGuid().ToString(),
            IndustryCode = Guid.NewGuid().ToString(),
            Cvr = Guid.NewGuid().ToString(),
            IsFarm = false,
            LanguagesIds = new List<int>()
            {
                1
            },
            MainMailAddress = Guid.NewGuid().ToString(),
            Name = Guid.NewGuid().ToString(),
            WorkorderEnable = false
        };

        var core = await GetCore();

        // Act
        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1, BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, 1, 1);

        var properties = await BackendConfigurationPnDbContext.Properties.ToListAsync();
        var entityGroups = await MicrotingDbContext.EntityGroups.ToListAsync();
        var folderTranslations = await MicrotingDbContext.FolderTranslations.ToListAsync();
        var propertySelectedLanguages = await BackendConfigurationPnDbContext.PropertySelectedLanguages.ToListAsync();
        var planningTags = await ItemsPlanningPnDbContext.PlanningTags.ToListAsync();

        // Assert properties
        Assert.NotNull(properties);
        Assert.That(properties.Count, Is.EqualTo(1));
        Assert.That(properties[0].Name, Is.EqualTo(propertyCreateModel.Name));
        Assert.That(properties[0].Address, Is.EqualTo(propertyCreateModel.Address));
        Assert.That(properties[0].CHR, Is.EqualTo(propertyCreateModel.Chr));
        Assert.That(properties[0].IndustryCode, Is.EqualTo(propertyCreateModel.IndustryCode));
        Assert.That(properties[0].CVR, Is.EqualTo(propertyCreateModel.Cvr));
        Assert.That(properties[0].IsFarm, Is.EqualTo(propertyCreateModel.IsFarm));
        Assert.That(properties[0].MainMailAddress, Is.EqualTo(propertyCreateModel.MainMailAddress));
        Assert.That(properties[0].WorkorderEnable, Is.EqualTo(propertyCreateModel.WorkorderEnable));
        Assert.That(properties[0].EntitySelectListAreas, Is.EqualTo(entityGroups[2].Id));
        Assert.That(properties[0].EntitySelectListDeviceUsers, Is.EqualTo(entityGroups[3].Id));
        Assert.That(properties[0].FolderId, Is.EqualTo(folderTranslations[4].FolderId));
        Assert.That(properties[0].FolderIdForNewTasks, Is.EqualTo(folderTranslations[7].FolderId));
        Assert.That(properties[0].FolderIdForTasks, Is.EqualTo(folderTranslations[25].FolderId));
        Assert.That(properties[0].FolderIdForOngoingTasks, Is.EqualTo(folderTranslations[14].FolderId));
        Assert.That(properties[0].FolderIdForCompletedTasks, Is.EqualTo(folderTranslations[19].FolderId));
        Assert.That(properties[0].ItemPlanningTagId, Is.EqualTo(planningTags[28].Id));

        // Assert property selected languages
        Assert.NotNull(propertySelectedLanguages);
        Assert.That(propertySelectedLanguages.Count, Is.EqualTo(1));
        Assert.That(propertySelectedLanguages[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(propertySelectedLanguages[0].LanguageId, Is.EqualTo(1));

        // Assert planning tags
        Assert.NotNull(planningTags);
        Assert.That(planningTags.Count, Is.EqualTo(29));
        Assert.That(planningTags[0].Name, Is.EqualTo("01. Logbøger Miljøledelse"));
        Assert.That(planningTags[1].Name, Is.EqualTo("02. Beredskab"));
        Assert.That(planningTags[2].Name, Is.EqualTo("03. Gyllebeholdere"));
        Assert.That(planningTags[3].Name, Is.EqualTo("04. Foderindlægssedler"));
        Assert.That(planningTags[4].Name, Is.EqualTo("05. Stalde: Halebid og klargøring"));
        Assert.That(planningTags[5].Name, Is.EqualTo("06. Fodersiloer"));
        Assert.That(planningTags[6].Name, Is.EqualTo("07. Skadedyrsbekæmpelse"));
        Assert.That(planningTags[7].Name, Is.EqualTo("08. Luftrensning"));
        Assert.That(planningTags[8].Name, Is.EqualTo("09. Forsuring"));
        Assert.That(planningTags[9].Name, Is.EqualTo("10. Varmepumper"));
        Assert.That(planningTags[10].Name, Is.EqualTo("11. Varmekilder"));
        Assert.That(planningTags[11].Name, Is.EqualTo("12. Miljøfarlige stoffer"));
        Assert.That(planningTags[12].Name, Is.EqualTo("13. APV Landbrug"));
        Assert.That(planningTags[13].Name, Is.EqualTo("14. Maskiner"));
        Assert.That(planningTags[14].Name, Is.EqualTo("15. Elværktøj"));
        Assert.That(planningTags[15].Name, Is.EqualTo("16. Stiger"));
        Assert.That(planningTags[16].Name, Is.EqualTo("17. Brandslukkere"));
        Assert.That(planningTags[17].Name, Is.EqualTo("18. Alarm"));
        Assert.That(planningTags[18].Name, Is.EqualTo("19. Ventilation"));
        Assert.That(planningTags[19].Name, Is.EqualTo("20. Ugentlige rutineopgaver"));
        Assert.That(planningTags[20].Name, Is.EqualTo("21. DANISH Standard"));
        Assert.That(planningTags[21].Name, Is.EqualTo("22. Sigtetest"));
        Assert.That(planningTags[22].Name, Is.EqualTo("99. Diverse"));
        Assert.That(planningTags[23].Name, Is.EqualTo("24. IE-indberetning"));
        Assert.That(planningTags[24].Name, Is.EqualTo("25. KemiKontrol"));
        Assert.That(planningTags[25].Name, Is.EqualTo("00. Aflæsninger, målinger, forbrug og fækale uheld"));
        Assert.That(planningTags[26].Name, Is.EqualTo("26. Kornlager"));
        Assert.That(planningTags[27].Name, Is.EqualTo("00. Logbøger"));
        Assert.That(planningTags[28].Name, Is.EqualTo($"00. {propertyCreateModel.Name} - {propertyCreateModel.Address}"));

        // Assert folder translations
        Assert.NotNull(folderTranslations);
        Assert.That(folderTranslations.Count, Is.EqualTo(28));
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

        // Assert entity groups
        Assert.That(entityGroups.Count, Is.EqualTo(4));
        Assert.That(entityGroups[2].Name, Is.EqualTo($"{propertyCreateModel.Name} - Areas"));
        Assert.That(entityGroups[2].Type, Is.EqualTo("EntitySelect"));
        Assert.That(entityGroups[3].Name, Is.EqualTo($"{propertyCreateModel.Name} - Device Users"));
        Assert.That(entityGroups[3].Type, Is.EqualTo("EntitySelect"));
    }

    // Should test the Create method with correct information for 2 properties and give a success message
    [Test]
    public async Task CreateToProperties_WithCorrectInformation_ShouldReturnSuccess()
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
        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1,
            BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, 1,1);

        var propertyCreateModel2 = new PropertyCreateModel
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

        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel2, core, 1,
            BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, 2,2);

        var properties = await BackendConfigurationPnDbContext.Properties.ToListAsync();

        // Assert
        Assert.NotNull(properties);
        Assert.That(properties.Count, Is.EqualTo(2));
    }

    // Should test Create method for a property with a CVR and CHR that already exists and give an error message
    [Test]
    public async Task CreateProperty_WithExistingCVRandCHR_ShouldReturnError()
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
        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1,
            BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, 1, 1);

        var newPropertyCreateModel = new PropertyCreateModel
        {
            Address = Guid.NewGuid().ToString(),
            Chr = propertyCreateModel.Chr,
            IndustryCode = Guid.NewGuid().ToString(),
            Cvr = propertyCreateModel.Cvr,
            IsFarm = false,
            LanguagesIds = new List<int>
            {
                1
            },
            MainMailAddress = Guid.NewGuid().ToString(),
            Name = propertyCreateModel.Name,
            WorkorderEnable = false
        };

        // Act
        var result = await BackendConfigurationPropertiesServiceHelper.Create(newPropertyCreateModel, core, 1,
            BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, 2, 2);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Success);
        Assert.False(result.Success);
        Assert.NotNull(result.Message);
        Assert.That(result.Message, Is.EqualTo("PropertyAlreadyExists"));
    }

    // Should test Create method for a property with a CVR that already exists and give an return success
    [Test]
    public async Task CreateProperty_WithExistingCVR_ShouldReturnSuccess()
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
        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1,
            BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, 1, 1);

        var newPropertyCreateModel = new PropertyCreateModel
        {
            Address = Guid.NewGuid().ToString(),
            Chr = Guid.NewGuid().ToString(),
            IndustryCode = Guid.NewGuid().ToString(),
            Cvr = propertyCreateModel.Cvr,
            IsFarm = false,
            LanguagesIds = new List<int>
            {
                1
            },
            MainMailAddress = Guid.NewGuid().ToString(),
            Name = propertyCreateModel.Name,
            WorkorderEnable = false
        };

        // Act
        var result = await BackendConfigurationPropertiesServiceHelper.Create(newPropertyCreateModel, core, 1,
            BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, 2, 2);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Success);
        Assert.True(result.Success);
        Assert.NotNull(result.Message);
    }

    // Should test Create method for a property with a CHR that already exists and succeed
    [Test]
    public async Task CreateProperty_WithExistingCHR_ShouldReturnSuccess()
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
        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1,
            BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, 1, 1);

        var newPropertyCreateModel = new PropertyCreateModel
        {
            Address = Guid.NewGuid().ToString(),
            Chr = propertyCreateModel.Chr,
            IndustryCode = Guid.NewGuid().ToString(),
            Cvr = Guid.NewGuid().ToString(),
            IsFarm = false,
            LanguagesIds = new List<int>
            {
                1
            },
            MainMailAddress = Guid.NewGuid().ToString(),
            Name = propertyCreateModel.Name,
            WorkorderEnable = false
        };

        // Act
        var result = await BackendConfigurationPropertiesServiceHelper.Create(newPropertyCreateModel, core, 1,
            BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, 2, 2);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Success);
        Assert.True(result.Success);
        Assert.NotNull(result.Message);
    }

    // Should test Create method for a property where CVR limit is reached and give an error message
    [Test]
    public async Task CreateProperty_WithCvrLimitReached_ShouldReturnError()
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
        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1,
            BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, 1, 1);

        var newPropertyCreateModel = new PropertyCreateModel
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

        // Act
        var result = await BackendConfigurationPropertiesServiceHelper.Create(newPropertyCreateModel, core, 1,
            BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, 2, 1);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Success);
        Assert.False(result.Success);
        Assert.NotNull(result.Message);
        Assert.That(result.Message, Is.EqualTo("MaxCvrNumbersReached"));
    }

    // Should test Create method for a property where CHR limit is reached and give an error message
    [Test]
    public async Task CreateProperty_WithChrLimitReached_ShouldReturnError()
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
        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1,
            BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, 1, 1);

        var newPropertyCreateModel = new PropertyCreateModel
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

        // Act
        var result = await BackendConfigurationPropertiesServiceHelper.Create(newPropertyCreateModel, core, 1,
            BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, 1, 2);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Success);
        Assert.False(result.Success);
        Assert.NotNull(result.Message);
        Assert.That(result.Message, Is.EqualTo("MaxChrNumbersReached"));
    }

    // Should test Update method for a property and succeed with a valid model
    [Test]
    public async Task UpdateProperty_WithValidModel_ShouldReturnSuccess()
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
        var createResult = await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1,
            BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, 1, 1);

        var properties = await BackendConfigurationPnDbContext.Properties.AsNoTracking().ToListAsync();

        var propertyUpdateModel = new PropertiesUpdateModel
        {
            Id = properties.First().Id,
            Address = Guid.NewGuid().ToString(),
            Chr = Guid.NewGuid().ToString(),
            Cvr = Guid.NewGuid().ToString(),
            LanguagesIds = new List<int>
            {
                1
            },
            MainMailAddress = Guid.NewGuid().ToString(),
            Name = Guid.NewGuid().ToString(),
            WorkorderEnable = false
        };

        // Act
        var result = await BackendConfigurationPropertiesServiceHelper.Update(propertyUpdateModel, core, 1,
            BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, "Location");

        properties = await BackendConfigurationPnDbContext.Properties.AsNoTracking().ToListAsync();
        var entityGroups = await MicrotingDbContext.EntityGroups.ToListAsync();
        var folderTranslations = await MicrotingDbContext.FolderTranslations.ToListAsync();
        var propertySelectedLanguages = await BackendConfigurationPnDbContext.PropertySelectedLanguages.ToListAsync();
        var planningTags = await ItemsPlanningPnDbContext.PlanningTags.ToListAsync();

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Success);
        Assert.True(result.Success);
        Assert.NotNull(result.Message);


        // Assert properties
        Assert.NotNull(properties);
        Assert.That(properties.Count, Is.EqualTo(1));
        Assert.That(properties[0].Name, Is.EqualTo(propertyUpdateModel.Name));
        Assert.That(properties[0].Address, Is.EqualTo(propertyUpdateModel.Address));
        Assert.That(properties[0].CHR, Is.EqualTo(propertyUpdateModel.Chr));
        Assert.That(properties[0].IndustryCode, Is.EqualTo(propertyCreateModel.IndustryCode));
        Assert.That(properties[0].CVR, Is.EqualTo(propertyUpdateModel.Cvr));
        Assert.That(properties[0].IsFarm, Is.EqualTo(propertyCreateModel.IsFarm));
        Assert.That(properties[0].MainMailAddress, Is.EqualTo(propertyUpdateModel.MainMailAddress));
        Assert.That(properties[0].WorkorderEnable, Is.EqualTo(propertyUpdateModel.WorkorderEnable));
        Assert.That(properties[0].EntitySelectListAreas, Is.EqualTo(entityGroups[2].Id));
        Assert.That(properties[0].EntitySelectListDeviceUsers, Is.EqualTo(entityGroups[3].Id));
        Assert.That(properties[0].FolderId, Is.EqualTo(folderTranslations[4].FolderId));
        Assert.That(properties[0].FolderIdForNewTasks, Is.EqualTo(folderTranslations[7].FolderId));
        Assert.That(properties[0].FolderIdForTasks, Is.EqualTo(folderTranslations[25].FolderId));
        Assert.That(properties[0].FolderIdForOngoingTasks, Is.EqualTo(folderTranslations[14].FolderId));
        Assert.That(properties[0].FolderIdForCompletedTasks, Is.EqualTo(folderTranslations[19].FolderId));
        Assert.That(properties[0].ItemPlanningTagId, Is.EqualTo(planningTags[28].Id));

        // Assert property selected languages
        Assert.NotNull(propertySelectedLanguages);
        Assert.That(propertySelectedLanguages.Count, Is.EqualTo(1));
        Assert.That(propertySelectedLanguages[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(propertySelectedLanguages[0].LanguageId, Is.EqualTo(1));

        // Assert planning tags
        Assert.NotNull(planningTags);
        Assert.That(planningTags.Count, Is.EqualTo(29));
        Assert.That(planningTags[0].Name, Is.EqualTo("01. Logbøger Miljøledelse"));
        Assert.That(planningTags[1].Name, Is.EqualTo("02. Beredskab"));
        Assert.That(planningTags[2].Name, Is.EqualTo("03. Gyllebeholdere"));
        Assert.That(planningTags[3].Name, Is.EqualTo("04. Foderindlægssedler"));
        Assert.That(planningTags[4].Name, Is.EqualTo("05. Stalde: Halebid og klargøring"));
        Assert.That(planningTags[5].Name, Is.EqualTo("06. Fodersiloer"));
        Assert.That(planningTags[6].Name, Is.EqualTo("07. Skadedyrsbekæmpelse"));
        Assert.That(planningTags[7].Name, Is.EqualTo("08. Luftrensning"));
        Assert.That(planningTags[8].Name, Is.EqualTo("09. Forsuring"));
        Assert.That(planningTags[9].Name, Is.EqualTo("10. Varmepumper"));
        Assert.That(planningTags[10].Name, Is.EqualTo("11. Varmekilder"));
        Assert.That(planningTags[11].Name, Is.EqualTo("12. Miljøfarlige stoffer"));
        Assert.That(planningTags[12].Name, Is.EqualTo("13. APV Landbrug"));
        Assert.That(planningTags[13].Name, Is.EqualTo("14. Maskiner"));
        Assert.That(planningTags[14].Name, Is.EqualTo("15. Elværktøj"));
        Assert.That(planningTags[15].Name, Is.EqualTo("16. Stiger"));
        Assert.That(planningTags[16].Name, Is.EqualTo("17. Brandslukkere"));
        Assert.That(planningTags[17].Name, Is.EqualTo("18. Alarm"));
        Assert.That(planningTags[18].Name, Is.EqualTo("19. Ventilation"));
        Assert.That(planningTags[19].Name, Is.EqualTo("20. Ugentlige rutineopgaver"));
        Assert.That(planningTags[20].Name, Is.EqualTo("21. DANISH Standard"));
        Assert.That(planningTags[21].Name, Is.EqualTo("22. Sigtetest"));
        Assert.That(planningTags[22].Name, Is.EqualTo("99. Diverse"));
        Assert.That(planningTags[23].Name, Is.EqualTo("24. IE-indberetning"));
        Assert.That(planningTags[24].Name, Is.EqualTo("25. KemiKontrol"));
        Assert.That(planningTags[25].Name, Is.EqualTo("00. Aflæsninger, målinger, forbrug og fækale uheld"));
        Assert.That(planningTags[26].Name, Is.EqualTo("26. Kornlager"));
        Assert.That(planningTags[27].Name, Is.EqualTo("00. Logbøger"));
        Assert.That(planningTags[28].Name, Is.EqualTo($"00. {propertyUpdateModel.Name} - {propertyUpdateModel.Address}"));

        // Assert folder translations
        Assert.NotNull(folderTranslations);
        Assert.That(folderTranslations.Count, Is.EqualTo(28));
        Assert.That(folderTranslations[4].Name, Is.EqualTo(propertyUpdateModel.Name));
        Assert.That(folderTranslations[4].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[5].Name, Is.EqualTo(propertyUpdateModel.Name));
        Assert.That(folderTranslations[5].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[6].Name, Is.EqualTo(propertyUpdateModel.Name));
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
        Assert.That(folderTranslations[13].Name, Is.EqualTo(propertyUpdateModel.Name));
        Assert.That(folderTranslations[13].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[14].Name, Is.EqualTo(propertyUpdateModel.Name));
        Assert.That(folderTranslations[14].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[15].Name, Is.EqualTo(propertyUpdateModel.Name));
        Assert.That(folderTranslations[15].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[16].Name, Is.EqualTo("00.03 Andres opgaver"));
        Assert.That(folderTranslations[16].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[17].Name, Is.EqualTo("00.03 Others' tasks"));
        Assert.That(folderTranslations[17].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[18].Name, Is.EqualTo("00.03 Aufgaben anderer"));
        Assert.That(folderTranslations[18].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[19].Name, Is.EqualTo(propertyUpdateModel.Name));
        Assert.That(folderTranslations[19].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[20].Name, Is.EqualTo(propertyUpdateModel.Name));
        Assert.That(folderTranslations[20].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[21].Name, Is.EqualTo(propertyUpdateModel.Name));
        Assert.That(folderTranslations[21].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[22].Name, Is.EqualTo("00.01 Mine hasteopgaver"));
        Assert.That(folderTranslations[22].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[23].Name, Is.EqualTo("00.01 My urgent tasks"));
        Assert.That(folderTranslations[23].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[24].Name, Is.EqualTo("00.01 Meine dringenden Aufgaben"));
        Assert.That(folderTranslations[24].LanguageId, Is.EqualTo(3));
        Assert.That(folderTranslations[25].Name, Is.EqualTo(propertyUpdateModel.Name));
        Assert.That(folderTranslations[25].LanguageId, Is.EqualTo(1));
        Assert.That(folderTranslations[26].Name, Is.EqualTo(propertyUpdateModel.Name));
        Assert.That(folderTranslations[26].LanguageId, Is.EqualTo(2));
        Assert.That(folderTranslations[27].Name, Is.EqualTo(propertyUpdateModel.Name));
        Assert.That(folderTranslations[27].LanguageId, Is.EqualTo(3));

        // Assert entity groups
        Assert.That(entityGroups.Count, Is.EqualTo(4));
        Assert.That(entityGroups[2].Name, Is.EqualTo($"{propertyUpdateModel.Name} - Areas"));
        Assert.That(entityGroups[2].Type, Is.EqualTo("EntitySelect"));
        Assert.That(entityGroups[3].Name, Is.EqualTo($"{propertyUpdateModel.Name} - Device Users"));
        Assert.That(entityGroups[3].Type, Is.EqualTo("EntitySelect"));
    }

}