using BackendConfiguration.Pn.Infrastructure.Models.Settings;
using BackendConfiguration.Pn.Services.BackendConfigurationPropertiesService;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eForm.Infrastructure;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;
using Microting.eFormApi.BasePn.Infrastructure.Helpers.PluginDbOptions;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using NSubstitute;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// #1184 — <c>BackendConfigurationPropertiesService.GetLinkedSites</c> feeds every
/// assignee picker in the plugin (calendar create/edit + complete modals, task
/// wizard, task tracker, task-worker assignments). "Resigned" lives on the SDK
/// <c>Worker</c> (reached via <c>SiteWorkers</c>), and resigning never removes the
/// <c>PropertyWorker</c> row, so both overloads must drop resigned workers while
/// keeping active ones. The <c>compliance: true</c> branch that injects the
/// caller's own site must keep working.
/// <para>
/// The database is bootstrapped once per fixture, so SDK <c>Sites</c> accumulate
/// across tests; every assertion is scoped to the ids seeded by that test and
/// never to whole-table counts. Names deliberately avoid the AAA/ZZZ extremes
/// (Danish collation sorts "aa" last).
/// </para>
/// <para>
/// <c>Workers.Resigned</c> / <c>ResignedAtDate</c> only exist after
/// <c>GetCore()</c> migrates the fixture DB — the bootstrap SQL
/// (<c>SQL/420_SDK.sql</c>) creates <c>Workers</c> without them. Every test
/// seeds a <c>Worker</c> before it builds the SUT, so the class-level
/// <c>[SetUp]</c> pre-warms the Core and recreates <c>MicrotingDbContext</c>
/// on the post-migration schema BEFORE any seeding (same pattern as
/// <c>ComplianceReportEformColumnsTests</c> / <c>CalendarAttachmentTests</c>).
/// Seed only after that pre-warm.
/// </para>
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class BackendConfigurationPropertiesServiceGetLinkedSitesTests : TestBaseSetup
{
    private sealed record SeededSite(int SiteId, int WorkerId);

    /// <summary>
    /// Pre-warm the SDK Core so its EF migrations apply. The bootstrap SQL
    /// creates <c>Workers</c> without <c>Resigned</c>/<c>ResignedAtDate</c>,
    /// so an INSERT through the entity would fail with "Unknown column
    /// 'Resigned'" until the migration has run. Runs after the base
    /// <c>[SetUp]</c> (NUnit orders base SetUp first) and before any test body
    /// seeds a row.
    /// </summary>
    [SetUp]
    public async Task PreWarmSdkSchema()
    {
        var sdkConnectionString = MicrotingDbContext!.Database.GetConnectionString()!;

        await GetCore();

        // Refresh the test's MicrotingDbContext so it sees the post-migration
        // schema. EF caches the model on first query, so a context that ran
        // before the migration would still error on the new Workers columns.
        await MicrotingDbContext.DisposeAsync();
        MicrotingDbContext = new MicrotingDbContext(
            new DbContextOptionsBuilder<MicrotingDbContext>()
                .UseMySql(sdkConnectionString,
                    new MariaDbServerVersion(ServerVersion.AutoDetect(sdkConnectionString)),
                    o => o.EnableRetryOnFailure())
                .Options);
    }

    /// <summary>
    /// Seeds an SDK Site + Worker + SiteWorker triple, the same shape the real
    /// device-user creation leaves behind, with the worker's Resigned flag set
    /// as requested. Uses the post-migration <c>MicrotingDbContext</c> built by
    /// <see cref="PreWarmSdkSchema"/> — never call this before that
    /// <c>[SetUp]</c> has run.
    /// </summary>
    private async Task<SeededSite> SeedSdkSiteAsync(string name, bool resigned)
    {
        var language = await MicrotingDbContext!.Languages.FirstAsync();
        var site = new Site
        {
            Name = name,
            LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created,
        };
        await MicrotingDbContext.Sites.AddAsync(site);
        await MicrotingDbContext.SaveChangesAsync();

        var worker = new Worker
        {
            FirstName = name,
            LastName = "Worker",
            Email = $"{Guid.NewGuid():N}@example.test",
            Resigned = resigned,
            ResignedAtDate = resigned ? DateTime.UtcNow.AddDays(-1) : default,
            WorkflowState = Constants.WorkflowStates.Created,
        };
        await MicrotingDbContext.Workers.AddAsync(worker);
        await MicrotingDbContext.SaveChangesAsync();

        var siteWorker = new SiteWorker
        {
            SiteId = site.Id,
            WorkerId = worker.Id,
            WorkflowState = Constants.WorkflowStates.Created,
        };
        await MicrotingDbContext.SiteWorkers.AddAsync(siteWorker);
        await MicrotingDbContext.SaveChangesAsync();

        return new SeededSite(site.Id, worker.Id);
    }

    private async Task<Property> CreatePropertyAsync()
    {
        var property = new Property
        {
            Name = $"Prop {Guid.NewGuid():N}",
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1,
        };
        await BackendConfigurationPnDbContext!.Properties.AddAsync(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        return property;
    }

    private async Task AssignToPropertyAsync(int propertyId, int siteId)
    {
        await BackendConfigurationPnDbContext!.PropertyWorkers.AddAsync(new PropertyWorker
        {
            PropertyId = propertyId,
            WorkerId = siteId,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1,
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();
    }

    /// <summary>
    /// Builds the SUT over a real SDK Core (GetLinkedSites reads SiteWorkers →
    /// Workers on <c>core.DbContextHelper.GetDbContext()</c>) and a stubbed
    /// current user. The <c>compliance: true</c> branch looks up the caller's
    /// SDK site by "FirstName LastName", so the stub's names are what the test
    /// seeds as that site's Name.
    /// </summary>
    private async Task<BackendConfigurationPropertiesService> CreateSutAsync(
        string callerFirstName, string callerLastName)
    {
        var core = await GetCore();
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));

        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        userService.GetCurrentUserAsync().Returns(Task.FromResult(new EformUser
        {
            Id = 1,
            FirstName = callerFirstName,
            LastName = callerLastName,
        }));

        return new BackendConfigurationPropertiesService(
            coreHelper,
            userService,
            BackendConfigurationPnDbContext!,
            new BackendConfigurationLocalizationService(),
            ItemsPlanningPnDbContext!,
            Substitute.For<IPluginDbOptions<BackendConfigurationBaseSettings>>(),
            NullLogger<BackendConfigurationPropertiesService>.Instance);
    }

    // ------------------------------------------------------------------
    // int overload — GET properties/get-linked-sites?propertyId=P&compliance=false
    // ------------------------------------------------------------------

    [Test]
    public async Task GetLinkedSites_IntOverload_ExcludesResignedWorker_KeepsActiveWorker()
    {
        // Arrange: one property with an active and a resigned worker, both still
        // normal, non-removed PropertyWorkers (resigning never removes that row).
        var property = await CreatePropertyAsync();
        var active = await SeedSdkSiteAsync($"Bente {Guid.NewGuid():N}", resigned: false);
        var resigned = await SeedSdkSiteAsync($"Carl {Guid.NewGuid():N}", resigned: true);
        await AssignToPropertyAsync(property.Id, active.SiteId);
        await AssignToPropertyAsync(property.Id, resigned.SiteId);

        var sut = await CreateSutAsync("Caller", Guid.NewGuid().ToString("N"));

        // Act
        var result = await sut.GetLinkedSites(property.Id, compliance: false);

        // Assert
        Assert.That(result.Success, Is.True, result.Message);
        var ids = result.Model.Select(x => x.Id).ToList();
        Assert.That(ids, Does.Contain(active.SiteId),
            "A non-resigned PropertyWorker must still be listed.");
        Assert.That(ids, Does.Not.Contain(resigned.SiteId),
            "A PropertyWorker whose SDK Worker.Resigned is true must be excluded (#1184).");
    }

    [Test]
    public async Task GetLinkedSites_IntOverload_UnresigningWorker_BringsThemBack()
    {
        // The filter is evaluated per request against the SDK Worker row, so
        // clearing Resigned must reinstate the worker with no restart / cache.
        var property = await CreatePropertyAsync();
        var seeded = await SeedSdkSiteAsync($"Dorte {Guid.NewGuid():N}", resigned: true);
        await AssignToPropertyAsync(property.Id, seeded.SiteId);

        var sut = await CreateSutAsync("Caller", Guid.NewGuid().ToString("N"));

        var before = await sut.GetLinkedSites(property.Id, compliance: false);
        Assert.That(before.Success, Is.True, before.Message);
        Assert.That(before.Model.Select(x => x.Id), Does.Not.Contain(seeded.SiteId));

        var worker = await MicrotingDbContext!.Workers.SingleAsync(w => w.Id == seeded.WorkerId);
        worker.Resigned = false;
        await MicrotingDbContext.SaveChangesAsync();

        var after = await sut.GetLinkedSites(property.Id, compliance: false);
        Assert.That(after.Success, Is.True, after.Message);
        Assert.That(after.Model.Select(x => x.Id), Does.Contain(seeded.SiteId),
            "Un-resigning the worker must make them appear again without a backend restart.");
    }

    [Test]
    public async Task GetLinkedSites_IntOverload_ComplianceTrue_StillInjectsCallerSite()
    {
        // Arrange: the caller's own SDK site (named "FirstName LastName") is NOT
        // a PropertyWorker of the property. compliance:true must add it; and the
        // resigned exclusion must still apply to the property's workers.
        var property = await CreatePropertyAsync();
        var active = await SeedSdkSiteAsync($"Erik {Guid.NewGuid():N}", resigned: false);
        var resigned = await SeedSdkSiteAsync($"Finn {Guid.NewGuid():N}", resigned: true);
        await AssignToPropertyAsync(property.Id, active.SiteId);
        await AssignToPropertyAsync(property.Id, resigned.SiteId);

        var callerFirst = "Caller";
        var callerLast = Guid.NewGuid().ToString("N");
        var callerSite = await SeedSdkSiteAsync($"{callerFirst} {callerLast}", resigned: false);

        var sut = await CreateSutAsync(callerFirst, callerLast);

        // Act
        var withCompliance = await sut.GetLinkedSites(property.Id, compliance: true);
        var withoutCompliance = await sut.GetLinkedSites(property.Id, compliance: false);

        // Assert
        Assert.That(withCompliance.Success, Is.True, withCompliance.Message);
        var idsWith = withCompliance.Model.Select(x => x.Id).ToList();
        Assert.That(idsWith, Does.Contain(callerSite.SiteId),
            "compliance:true must inject the current user's own site even though it is not a PropertyWorker.");
        Assert.That(idsWith, Does.Contain(active.SiteId));
        Assert.That(idsWith, Does.Not.Contain(resigned.SiteId),
            "The resigned exclusion must still apply on the compliance path.");

        Assert.That(withoutCompliance.Success, Is.True, withoutCompliance.Message);
        Assert.That(withoutCompliance.Model.Select(x => x.Id), Does.Not.Contain(callerSite.SiteId),
            "compliance:false must not inject the caller's site.");
    }

    // ------------------------------------------------------------------
    // list overload — POST properties/get-linked-sites (task-wizard / task-tracker filters)
    // ------------------------------------------------------------------

    [Test]
    public async Task GetLinkedSites_ListOverload_ExcludesResignedWorker_KeepsActiveWorker()
    {
        // Arrange: two properties; the active worker on one, the resigned worker
        // on the other, plus a resigned worker on both — the union across the
        // requested properties must still drop every resigned site.
        var propertyOne = await CreatePropertyAsync();
        var propertyTwo = await CreatePropertyAsync();
        var active = await SeedSdkSiteAsync($"Gitte {Guid.NewGuid():N}", resigned: false);
        var resignedOnTwo = await SeedSdkSiteAsync($"Hans {Guid.NewGuid():N}", resigned: true);
        var resignedOnBoth = await SeedSdkSiteAsync($"Inge {Guid.NewGuid():N}", resigned: true);
        await AssignToPropertyAsync(propertyOne.Id, active.SiteId);
        await AssignToPropertyAsync(propertyTwo.Id, resignedOnTwo.SiteId);
        await AssignToPropertyAsync(propertyOne.Id, resignedOnBoth.SiteId);
        await AssignToPropertyAsync(propertyTwo.Id, resignedOnBoth.SiteId);

        var sut = await CreateSutAsync("Caller", Guid.NewGuid().ToString("N"));

        // Act
        var result = await sut.GetLinkedSites(new List<int> { propertyOne.Id, propertyTwo.Id });

        // Assert
        Assert.That(result.Success, Is.True, result.Message);
        var ids = result.Model.Select(x => x.Id).ToList();
        Assert.That(ids, Does.Contain(active.SiteId),
            "A non-resigned PropertyWorker must still be listed by the list overload.");
        Assert.That(ids, Does.Not.Contain(resignedOnTwo.SiteId),
            "A resigned PropertyWorker must be excluded by the list overload (#1184).");
        Assert.That(ids, Does.Not.Contain(resignedOnBoth.SiteId),
            "A resigned worker assigned to several requested properties must still be excluded.");
    }

    [Test]
    public async Task GetLinkedSites_ListOverload_EmptyOrNullPropertyIds_ReturnsEmpty()
    {
        // Pre-existing contract of the list overload, pinned so the #1184 filter
        // did not disturb the early return.
        var sut = await CreateSutAsync("Caller", Guid.NewGuid().ToString("N"));

        var empty = await sut.GetLinkedSites(new List<int>());
        var nullIds = await sut.GetLinkedSites(null);

        Assert.That(empty.Success, Is.True);
        Assert.That(empty.Model, Is.Empty);
        Assert.That(nullIds.Success, Is.True);
        Assert.That(nullIds.Model, Is.Empty);
    }
}
