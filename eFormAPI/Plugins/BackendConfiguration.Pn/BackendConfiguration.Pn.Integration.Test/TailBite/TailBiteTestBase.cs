using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.GrpcServices;
using BackendConfiguration.Pn.Services.TailBite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Time.Testing;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using NSubstitute;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Integration.Test.TailBite;

public abstract class TailBiteTestBase : TestBaseSetup
{
    protected BackendConfigurationPnDbContext Db => BackendConfigurationPnDbContext!;
    protected int PropertyId;
    protected int RuleId;
    protected int RootId, StableAId, SectionId, Pen309Id, Pen310Id, StableBId, Pen501Id;
    protected readonly FakeTimeProvider Clock = new(new DateTimeOffset(2026, 10, 1, 7, 0, 0, TimeSpan.Zero));
    private eFormCore.Core? _core;

    [SetUp]
    public async Task ResetTailBiteTables()
    {
        var db = BackendConfigurationPnDbContext!;
        foreach (var table in new[] {
            "TailBiteOutbreakLinkVersions","TailBiteOutbreakLinks","TailBiteAssessmentActionVersions","TailBiteAssessmentActions",
            "TailBiteRiskAssessmentVersions","TailBiteRiskAssessments","TailBiteOutbreakVersions","TailBiteOutbreaks",
            "TailBiteRuleVersions","TailBiteRules","TailBiteRegistrationActionVersions","TailBiteRegistrationActions",
            "TailBiteRegistrationPhotoVersions","TailBiteRegistrationPhotos","TailBiteRegistrationLocationVersions","TailBiteRegistrationLocations",
            "TailBiteRegistrationVersions","TailBiteRegistrations","TailBiteActionTypeVersions","TailBiteActionTypes",
            "TailBiteOccupancyVersions","TailBiteOccupancies","TailBiteLocationVersions","TailBiteLocations",
            "TailBitePropertyVersions","TailBiteProperties",
            "PropertyWorkerVersions","PropertyWorkers","PropertieVersions","Properties" })
            await db.Database.ExecuteSqlRawAsync($"DELETE FROM `{table}`");
        db.ChangeTracker.Clear();
        var p = new Property { Name = "Ejendom Test" };
        await p.Create(db);
        PropertyId = p.Id;
    }

    // A manager method refuses a missing id exactly like a foreign one, so it cannot be used to probe which ids exist.
    protected static async Task AssertRefusedAlike(Func<Task> missing, Func<Task> foreign)
    {
        var a = await Assert.ThrowsAsync<TailBiteForbiddenException>(() => missing());
        var b = await Assert.ThrowsAsync<TailBiteForbiddenException>(() => foreign());
        Assert.That(a!.Message, Is.EqualTo(TailBiteForbiddenException.NotFoundOrNoAccess));
        Assert.That(b!.Message, Is.EqualTo(a.Message));
    }

    /// <summary>A plugin context of its own on the fixture database, with the given execution strategy (default: retry on failure) and interceptors.</summary>
    protected BackendConfigurationPnDbContext NewContext(Func<ExecutionStrategyDependencies, IExecutionStrategy>? strategy = null,
        params IInterceptor[] interceptors)
    {
        var connectionString = Db.Database.GetConnectionString()!;
        var options = new DbContextOptionsBuilder<BackendConfigurationPnDbContext>()
            .UseMySql(connectionString, new MariaDbServerVersion(ServerVersion.AutoDetect(connectionString)), builder =>
            {
                if (strategy is null) builder.EnableRetryOnFailure();
                else builder.ExecutionStrategy(strategy);
            })
            .AddInterceptors(interceptors)
            .Options;
        return new BackendConfigurationPnDbContext(options);
    }

    protected TailBiteAccess NewAccess(BackendConfigurationPnDbContext? db = null)
        => new(db ?? BackendConfigurationPnDbContext!, Substitute.For<IGrpcSiteResolver>(), Substitute.For<ITailBiteWorkerEmailCounter>());

    // A core helper over a real SDK Core, created on first use (photo storage writes SDK UploadedData rows).
    protected IEFormCoreService CoreHelper()
    {
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(async _ => _core ??= await GetCore());
        return coreHelper;
    }

    // Builds: root > Stald A > Sektion 4 > Sti 309, Sti 310 ; root > Stald B > Sti 501
    protected async Task SeedTreeAsync(bool enabled = true)
    {
        var db = BackendConfigurationPnDbContext!;
        await new TailBiteProperty { PropertyId = PropertyId, Enabled = enabled, EnabledAt = Clock.GetUtcNow().UtcDateTime }.Create(db);
        async Task<int> Loc(int? parent, string name)
        {
            var l = new TailBiteLocation { PropertyId = PropertyId, ParentId = parent, Name = name, QrCode = TailBiteDefaults.NewQrCode() };
            await l.Create(db);
            return l.Id;
        }
        RootId = await Loc(null, "Ejendom"); StableAId = await Loc(RootId, "Stald A"); SectionId = await Loc(StableAId, "Sektion 4");
        Pen309Id = await Loc(SectionId, "Sti 309"); Pen310Id = await Loc(SectionId, "Sti 310");
        StableBId = await Loc(RootId, "Stald B"); Pen501Id = await Loc(StableBId, "Sti 501");
        var rule = TailBiteDefaults.DefaultRule(RootId);
        await rule.Create(db);
        RuleId = rule.Id;
    }

    protected async Task<PropertyWorker> SeedWorkerAsync(int siteId, bool manager = false)
    {
        var pw = new PropertyWorker { PropertyId = PropertyId, WorkerId = siteId, TailBiteManager = manager };
        await pw.Create(BackendConfigurationPnDbContext!);
        return pw;
    }

    // One registration by site 7 with the given rows; returns it and its row ids in order.
    protected async Task<(TailBiteRegistration Reg, List<int> RowIds)> SeedRegistrationAsync(
        DateTime effectiveAt, bool cancelled, params (int LocationId, int Minor, int Severe)[] rows)
    {
        var db = BackendConfigurationPnDbContext!;
        var reg = new TailBiteRegistration
        {
            PropertyId = PropertyId, SiteId = 7, ClientUuid = Guid.NewGuid(),
            RegisteredAt = effectiveAt, ReceivedAt = effectiveAt, EffectiveAt = effectiveAt,
            CancelledAt = cancelled ? effectiveAt : null, CancelledBySiteId = cancelled ? 7 : null,
            CancelReason = cancelled ? "Registreret på forkert sti" : null
        };
        await reg.Create(db);
        var ids = new List<int>();
        foreach (var (locationId, minor, severe) in rows)
        {
            var row = new TailBiteRegistrationLocation { RegistrationId = reg.Id, LocationId = locationId, MinorCount = minor, SevereCount = severe };
            await row.Create(db);
            ids.Add(row.Id);
        }
        return (reg, ids);
    }
}
