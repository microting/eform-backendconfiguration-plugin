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
/// Schema is therefore never hand-maintained, so bumping Microting.TimePlanningBase /
/// .ItemsPlanningBase / .EformBackendConfigurationBase / .eFormCaseTemplateBase can no longer
/// fail the suite with "Unknown column '&lt;NewColumn&gt;'". Keep the seed files data-only -
/// each one's header states the rules for editing it.
/// </para>
/// <para>
/// Files that are still schema+data dumps, and why they are safe:
/// 420_SDK.sql - Core.StartSqlOnly constructs a SqlController, which runs Database.Migrate() and
/// pulls the SDK database forward to the current model on its own (it needs the dump's
/// __EFMigrationsHistory rows to know where to resume, so this one must stay a full dump).
/// 420_Angular.sql - never replayed; GetBaseDbContext only calls EnsureCreated().
/// 420_chemical-base-plugin.sql - not loaded by this fixture at all.
/// </para>
/// <para>
/// EnsureCreated() does not create __EFMigrationsHistory, and the seed files no longer carry
/// its rows, so the four plugin databases have no migration history here. Nothing in the
/// fixture reads one. Production does - EformBackendConfigurationPlugin.ConfigureDbContext
/// calls Migrate() behind an IHistoryRepository.Exists() gate - so a test that ever
/// constructs the plugin would find that gate open against an already-complete schema.
/// </para>
/// </summary>
public abstract class TestBaseSetup
{
    /// <summary>
    /// Fixture queries are far slower than a production request, so every context gets the
    /// same generous command timeout. It is applied after the bootstrap, as it was before
    /// this was hoisted out of Setup(), so the seed replay still runs at the default timeout.
    /// </summary>
    private const int CommandTimeoutSeconds = 300;

    private readonly MariaDbContainer _mariadbTestcontainer = new MariaDbBuilder("mariadb:11.2")
        .WithDatabase(
            "myDb").WithUsername("bla").WithPassword("secretpassword")
        .WithEnvironment("MYSQL_ROOT_PASSWORD", "Qq1234567$")
        .Build();

    protected BackendConfigurationPnDbContext? BackendConfigurationPnDbContext;
    protected ItemsPlanningPnDbContext? ItemsPlanningPnDbContext;
    protected TimePlanningPnDbContext? TimePlanningPnDbContext;
    protected MicrotingDbContext? MicrotingDbContext;
    protected CaseTemplatePnDbContext? CaseTemplatePnDbContext;
    protected BaseDbContext BaseDbContext;
    protected IBus? Bus;

    /// <summary>
    /// Points a context at one database on the shared test container. The container hands out a
    /// single "myDb"/"bla" connection string, so each database is addressed by substituting its
    /// own name and connecting as root.
    /// </summary>
    private static DbContextOptions<TContext> BuildOptions<TContext>(string connectionStr, string databaseName)
        where TContext : DbContext
    {
        var optionsBuilder = new DbContextOptionsBuilder<TContext>();

        optionsBuilder.UseMySql(
            connectionStr.Replace("myDb", databaseName).Replace("bla", "root"),
            new MariaDbServerVersion(
                ServerVersion.AutoDetect(connectionStr)),
            mySqlOptionsAction: builder => {
                builder.EnableRetryOnFailure();
            });

        return optionsBuilder.Options;
    }

    /// <summary>
    /// Creates a context for <paramref name="databaseName"/>. When <paramref name="bootstrapSchema"/>
    /// is set, EnsureCreated() builds the schema from the entity model and SQL/&lt;databaseName&gt;.sql
    /// is replayed on top to seed the rows - each database's seed file is named after it.
    /// </summary>
    private static TContext CreateSeededContext<TContext>(
        string connectionStr,
        string databaseName,
        bool bootstrapSchema,
        Func<DbContextOptions<TContext>, TContext> createContext)
        where TContext : DbContext
    {
        var context = createContext(BuildOptions<TContext>(connectionStr, databaseName));

        if (bootstrapSchema)
        {
            context.Database.EnsureCreated();
            context.Database.ExecuteSqlRaw(File.ReadAllText(Path.Combine("SQL", $"{databaseName}.sql")));
        }

        context.Database.SetCommandTimeout(CommandTimeoutSeconds);

        return context;
    }

    /// <summary>
    /// The Angular base database has no seed file - 420_Angular.sql is never replayed - so
    /// EnsureCreated() is the whole bootstrap.
    /// </summary>
    private static BaseDbContext GetBaseDbContext(string connectionStr, bool bootstrapSchema)
    {
        var baseDbContext = new BaseDbContext(BuildOptions<BaseDbContext>(connectionStr, "420_Angular"));

        if (bootstrapSchema)
        {
            baseDbContext.Database.EnsureCreated();
        }

        baseDbContext.Database.SetCommandTimeout(CommandTimeoutSeconds);

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
    /// Rebuild and reseed the databases before EVERY test instead of once per fixture.
    /// <para>
    /// That bootstrap - EnsureCreated(), the seed replay, and the full DROP/CREATE pass
    /// 420_SDK.sql still carries - is the dominant cost of the whole integration suite.
    /// Bootstrapping once per fixture means tests share accumulated rows and identity
    /// counters no longer restart at 1. Most fixtures already tolerate that: the Calendar*
    /// and Adhoc* tables are in no seed file and have therefore always accumulated, which
    /// is why ~32 fixtures already carry FK-ordered cleanup.
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

        var connectionStr = _mariadbTestcontainer.GetConnectionString();

        BackendConfigurationPnDbContext = CreateSeededContext<BackendConfigurationPnDbContext>(
            connectionStr, "420_eform-backend-configuration-plugin", bootstrapSchema, options => new(options));

        ItemsPlanningPnDbContext = CreateSeededContext<ItemsPlanningPnDbContext>(
            connectionStr, "420_eform-angular-items-planning-plugin", bootstrapSchema, options => new(options));

        TimePlanningPnDbContext = CreateSeededContext<TimePlanningPnDbContext>(
            connectionStr, "420_eform-angular-time-planning-plugin", bootstrapSchema, options => new(options));

        MicrotingDbContext = CreateSeededContext<MicrotingDbContext>(
            connectionStr, "420_SDK", bootstrapSchema, options => new(options));

        CaseTemplatePnDbContext = CreateSeededContext<CaseTemplatePnDbContext>(
            connectionStr, "420_eform-angular-case-template-plugin", bootstrapSchema, options => new(options));

        BaseDbContext = GetBaseDbContext(connectionStr, bootstrapSchema);

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
