// using BackendConfiguration.Pn.Services.RebusService;
using eFormCore;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure;
using Microting.EformAngularFrontendBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.eFormCaseTemplateBase.Infrastructure.Data;
using Microting.ItemsPlanningBase.Infrastructure.Data;
using Microting.TimePlanningBase.Infrastructure.Data;
using Rebus.Bus;
using Testcontainers.MariaDb;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// Bootstraps the plugin databases: EnsureCreated() builds the schema from the entity model,
/// then the matching file in SQL/ seeds the rows.
/// <para>
/// Schema is therefore never hand-maintained. A column a base package adds is created by
/// EnsureCreated() from the model that ships with the package, so bumping
/// Microting.TimePlanningBase / .ItemsPlanningBase / .EformBackendConfigurationBase /
/// .eFormCaseTemplateBase can no longer fail the suite with "Unknown column '&lt;NewColumn&gt;'".
/// A seed file holds INSERTs only - no CREATE TABLE to go stale, and every INSERT names its
/// columns, so a new column cannot break the arity of an existing VALUES row either.
/// </para>
/// <para>
/// Files that are still schema+data dumps, and why they are safe:
/// 420_SDK.sql - Core.StartSqlOnly constructs a SqlController, which runs Database.Migrate() and
/// pulls the SDK database forward to the current model on its own (it needs the dump's
/// __EFMigrationsHistory rows to know where to resume, so this one must stay a full dump).
/// 420_Angular.sql - never replayed; GetBaseDbContext only calls EnsureCreated().
/// 420_chemical-base-plugin.sql - not loaded by this fixture at all.
/// </para>
/// </summary>
public abstract class TestBaseSetup
{
    private readonly MariaDbContainer _mariadbTestcontainer = new MariaDbBuilder("mariadb:11.2")
        .WithDatabase(
            "myDb").WithUsername("bla").WithPassword("secretpassword")
        .WithEnvironment("MYSQL_ROOT_PASSWORD", "Qq1234567$")
        .Build();

    protected MicrotingDbContext? DbContext;

    protected BackendConfigurationPnDbContext? BackendConfigurationPnDbContext;
    protected ItemsPlanningPnDbContext? ItemsPlanningPnDbContext;
    protected TimePlanningPnDbContext? TimePlanningPnDbContext;
    protected MicrotingDbContext? MicrotingDbContext;
    protected CaseTemplatePnDbContext? CaseTemplatePnDbContext;
    protected BaseDbContext BaseDbContext;
    protected IBus? Bus;

    private BackendConfigurationPnDbContext GetBackendDbContext(string connectionStr, bool bootstrapSchema)
    {
        var optionsBuilder = new DbContextOptionsBuilder<BackendConfigurationPnDbContext>();

        optionsBuilder.UseMySql(
            connectionStr.Replace("myDb", "420_eform-backend-configuration-plugin").Replace("bla", "root"),
            new MariaDbServerVersion(
                ServerVersion.AutoDetect(connectionStr)),
            mySqlOptionsAction: builder => {
                builder.EnableRetryOnFailure();
            });

        var backendConfigurationPnDbContext = new BackendConfigurationPnDbContext(optionsBuilder.Options);
        var file = Path.Combine("SQL", "420_eform-backend-configuration-plugin.sql");
        var rawSql = File.ReadAllText(file);

        try
        {
            if (bootstrapSchema) backendConfigurationPnDbContext.Database.EnsureCreated();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }

        if (bootstrapSchema) backendConfigurationPnDbContext.Database.ExecuteSqlRaw(rawSql);

        return backendConfigurationPnDbContext;
    }

    private ItemsPlanningPnDbContext GetItemsPlanningPnDbContext(string connectionStr, bool bootstrapSchema)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ItemsPlanningPnDbContext>();

        optionsBuilder.UseMySql(
            connectionStr.Replace("myDb", "420_eform-angular-items-planning-plugin").Replace("bla", "root"),
            new MariaDbServerVersion(
                ServerVersion.AutoDetect(connectionStr)),
            mySqlOptionsAction: builder => {
                builder.EnableRetryOnFailure();
            });

        var itemsPlanningPnDbContext = new ItemsPlanningPnDbContext(optionsBuilder.Options);
        var file = Path.Combine("SQL", "420_eform-angular-items-planning-plugin.sql");
        var rawSql = File.ReadAllText(file);

        if (bootstrapSchema) itemsPlanningPnDbContext.Database.EnsureCreated();
        if (bootstrapSchema) itemsPlanningPnDbContext.Database.ExecuteSqlRaw(rawSql);

        return itemsPlanningPnDbContext;
    }

    private TimePlanningPnDbContext GetTimePlanningPnDbContext(string connectionStr, bool bootstrapSchema)
    {
        var optionsBuilder = new DbContextOptionsBuilder<TimePlanningPnDbContext>();

        optionsBuilder.UseMySql(
            connectionStr.Replace("myDb", "420_eform-angular-time-planning-plugin").Replace("bla", "root"),
            new MariaDbServerVersion(
                ServerVersion.AutoDetect(connectionStr)),
            mySqlOptionsAction: builder => {
                builder.EnableRetryOnFailure();
            });

        var timePlanningPnDbContext = new TimePlanningPnDbContext(optionsBuilder.Options);
        var file = Path.Combine("SQL", "420_eform-angular-time-planning-plugin.sql");
        var rawSql = File.ReadAllText(file);

        if (bootstrapSchema) timePlanningPnDbContext.Database.EnsureCreated();
        if (bootstrapSchema) timePlanningPnDbContext.Database.ExecuteSqlRaw(rawSql);

        return timePlanningPnDbContext;
    }

    private CaseTemplatePnDbContext GetCaseTemplatePnDbContext(string connectionStr, bool bootstrapSchema)
    {
        var optionsBuilder = new DbContextOptionsBuilder<CaseTemplatePnDbContext>();

        optionsBuilder.UseMySql(
            connectionStr.Replace("myDb", "420_eform-angular-case-template-plugin").Replace("bla", "root"),
            new MariaDbServerVersion(
                ServerVersion.AutoDetect(connectionStr)),
            mySqlOptionsAction: builder => {
                builder.EnableRetryOnFailure();
            });

        var caseTemplatePnDbContext = new CaseTemplatePnDbContext(optionsBuilder.Options);
        var file = Path.Combine("SQL", "420_eform-angular-case-template-plugin.sql");
        var rawSql = File.ReadAllText(file);

        if (bootstrapSchema) caseTemplatePnDbContext.Database.EnsureCreated();
        if (bootstrapSchema) caseTemplatePnDbContext.Database.ExecuteSqlRaw(rawSql);

        return caseTemplatePnDbContext;
    }

    private MicrotingDbContext GetContext(string connectionStr, bool bootstrapSchema)
    {
        var dbContextOptionsBuilder = new DbContextOptionsBuilder();

        dbContextOptionsBuilder.UseMySql(connectionStr.Replace("myDb", "420_SDK").Replace("bla", "root")
            , new MariaDbServerVersion(
                ServerVersion.AutoDetect(connectionStr)),
            mySqlOptionsAction: builder => {
                builder.EnableRetryOnFailure();
            });
        var microtingDbContext = new MicrotingDbContext(dbContextOptionsBuilder.Options);
        var file = Path.Combine("SQL", "420_SDK.sql");
        var rawSql = File.ReadAllText(file);

        if (bootstrapSchema) microtingDbContext.Database.EnsureCreated();
        if (bootstrapSchema) microtingDbContext.Database.ExecuteSqlRaw(rawSql);

        return microtingDbContext;
    }

    private BaseDbContext GetBaseDbContext(string connectionStr, bool bootstrapSchema)
    {
        var optionsBuilder = new DbContextOptionsBuilder<BaseDbContext>();

        optionsBuilder.UseMySql(connectionStr.Replace("myDb", "420_Angular").Replace("bla", "root")
            , new MariaDbServerVersion(
                ServerVersion.AutoDetect(connectionStr)),
            mySqlOptionsAction: builder => {
                builder.EnableRetryOnFailure();
            });
        var baseDbContext = new BaseDbContext(optionsBuilder.Options);

        if (bootstrapSchema) baseDbContext.Database.EnsureCreated();

        return baseDbContext;
    }

    protected async Task<Core> GetCore()
    {
        var core = new Core();
        await core.StartSqlOnly(_mariadbTestcontainer.GetConnectionString().Replace("myDb", "420_SDK")
            .Replace("bla", "root"));

        // Tests have no Microting cloud credentials, so Core.SendXml would block
        // on a doomed PostXml for every cloud CaseCreate (the PairItemWithSiteHelper
        // and TaskManagementHelper paths). skipCloudDeploy makes SendXml hand back a
        // synthetic MicrotingUid instead - see eform-sdk Core.cs:5489-5496.
        // CaseCreateLocalOnly never reached the cloud, so those paths are unaffected.
        await core.SetSdkSetting(Microting.eForm.Dto.Settings.skipCloudDeploy, "true");

        return core;
    }

    private bool _schemaBootstrapped;

    /// <summary>
    /// Replay the six SQL dumps before EVERY test instead of once per fixture.
    /// <para>
    /// The replay is ~586 DROP/CREATE TABLE statements costing ~34 seconds per
    /// test - the dominant cost of the whole integration suite. Replaying once
    /// per fixture means tests share accumulated rows and identity counters no
    /// longer restart at 1. Most fixtures already tolerate that: the Calendar*
    /// and Adhoc* tables were never in the dumps and have therefore always
    /// accumulated, which is why ~32 fixtures already carry FK-ordered cleanup.
    /// </para>
    /// <para>
    /// Override to <c>true</c> only where assertions are absolute whole-table
    /// counts or positional indexes into unfiltered lists - there, scoping every
    /// assertion is a rewrite rather than an edit.
    /// </para>
    /// </summary>
    protected virtual bool ResetDatabasePerTest => false;

    [SetUp]
    public async Task Setup()
    {
        Console.WriteLine($"{DateTime.Now} : Starting MariaDb Container...");
        await _mariadbTestcontainer.StartAsync();
        Console.WriteLine($"{DateTime.Now} : Started MariaDb Container");

        // DbContexts stay per-test, so [TearDown] and change-tracker semantics
        // are unchanged; only the expensive schema replay is skipped.
        var bootstrapSchema = !_schemaBootstrapped || ResetDatabasePerTest;
        _schemaBootstrapped = true;

        BackendConfigurationPnDbContext = GetBackendDbContext(_mariadbTestcontainer.GetConnectionString(), bootstrapSchema);

        BackendConfigurationPnDbContext!.Database.SetCommandTimeout(300);

        ItemsPlanningPnDbContext = GetItemsPlanningPnDbContext(_mariadbTestcontainer.GetConnectionString(), bootstrapSchema);

        ItemsPlanningPnDbContext.Database.SetCommandTimeout(300);

        TimePlanningPnDbContext = GetTimePlanningPnDbContext(_mariadbTestcontainer.GetConnectionString(), bootstrapSchema);

        TimePlanningPnDbContext.Database.SetCommandTimeout(300);

        MicrotingDbContext = GetContext(_mariadbTestcontainer.GetConnectionString(), bootstrapSchema);

        MicrotingDbContext.Database.SetCommandTimeout(300);

        CaseTemplatePnDbContext = GetCaseTemplatePnDbContext(_mariadbTestcontainer.GetConnectionString(), bootstrapSchema);

        CaseTemplatePnDbContext.Database.SetCommandTimeout(300);

        BaseDbContext = GetBaseDbContext(_mariadbTestcontainer.GetConnectionString(), bootstrapSchema);
        BaseDbContext.Database.SetCommandTimeout(300);

        // var rebusService =
            // new RebusService(
                // new EFormCoreService(_mariadbTestcontainer.GetConnectionString().Replace("myDb", "420_SDK")
                    // .Replace("bla", "root")), new BackendConfigurationLocalizationService());
        // rebusService
            // .Start(_mariadbTestcontainer.GetConnectionString().Replace("myDb", "420_SDK").Replace("bla", "root"))
            // .GetAwaiter().GetResult();
        // Bus = rebusService.GetBus();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        Console.WriteLine($"{DateTime.Now} : Stopping MariaDb Container...");
        await _mariadbTestcontainer.StopAsync();
        await _mariadbTestcontainer.DisposeAsync();
        Console.WriteLine($"{DateTime.Now} : Stopped MariaDb Container");
    }

    [TearDown]
    public async Task TearDown()
    {
        await BackendConfigurationPnDbContext!.DisposeAsync();
        await ItemsPlanningPnDbContext!.DisposeAsync();
        await TimePlanningPnDbContext!.DisposeAsync();
        await MicrotingDbContext!.DisposeAsync();
        await CaseTemplatePnDbContext!.DisposeAsync();
        await BaseDbContext.DisposeAsync();
        if (Bus != null) Bus.Dispose();
    }
}