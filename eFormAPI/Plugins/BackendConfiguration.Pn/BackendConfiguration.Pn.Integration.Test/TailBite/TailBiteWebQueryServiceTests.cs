using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.GrpcServices;
using BackendConfiguration.Pn.Services.TailBite;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microting.EformBackendConfigurationBase.Infrastructure.Const;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using NSubstitute;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Integration.Test.TailBite;

[TestFixture]
public class TailBiteWebQueryServiceTests : TailBiteTestBase
{
    private const int ManagerSite = 7;
    private const int WorkerSite = 8;
    private const int ForeignSite = 9;

    // Display names come from IGrpcSiteResolver (the lookup the app's gRPC service uses); the fake knows 7 and 8 only.
    private static IGrpcSiteResolver Resolver()
    {
        var resolver = Substitute.For<IGrpcSiteResolver>();
        resolver.GetDisplayNameAsync(Arg.Any<int>()).Returns(call => call.Arg<int>() switch
        {
            ManagerSite => "Jane Doe",
            WorkerSite => "John Doe",
            _ => ""
        });
        return resolver;
    }

    private TailBiteWebQueryService Sut() => new(Db, NewAccess(), Resolver(), Clock);

    // ---------- properties and workers ----------

    [Test]
    public async Task ListProperties_FlagsEnabled_SkipsRemoved()
    {
        await SeedTreeAsync(enabled: true);
        var disabled = new Property { Name = "Ejendom Uden Halebid" };
        await disabled.Create(Db);
        var removed = new Property { Name = "Ejendom Solgt" };
        await removed.Create(Db);
        await removed.Delete(Db);

        var list = await Sut().ListPropertiesAsync();

        Assert.That(list, Is.EqualTo(new[]
        {
            new TailBitePropertyStatus(PropertyId, "Ejendom Test", true),
            new TailBitePropertyStatus(disabled.Id, "Ejendom Uden Halebid", false),
        }));
    }

    [Test]
    public async Task ListWorkers_GroupsRowsPerSite_ManagerIfAnyRow_NamesFromSdk()
    {
        await SeedTreeAsync();
        var first = await SeedWorkerAsync(ManagerSite);
        var second = await SeedWorkerAsync(ManagerSite, manager: true);
        await SeedWorkerAsync(WorkerSite);
        await SeedWorkerAsync(ForeignSite);
        var left = await SeedWorkerAsync(42);
        await left.Delete(Db);

        var workers = await Sut().ListWorkersAsync(PropertyId);

        Assert.That(workers.Select(w => (w.SiteId, w.Name, w.IsManager)), Is.EqualTo(new[]
        {
            (ForeignSite, "#9", false), (ManagerSite, "Jane Doe", true), (WorkerSite, "John Doe", false),
        }));
        Assert.That(workers.Single(w => w.SiteId == ManagerSite).PropertyWorkerIds, Is.EqualTo(new[] { first.Id, second.Id }));
    }

    // ---------- rules ----------

    [Test]
    public async Task ListRules_WorkerSeesLiveRulesOfTheProperty_ForeignCallerRefused()
    {
        await SeedTreeAsync();
        await SeedWorkerAsync(WorkerSite);
        var stable = new TailBiteRule { LocationId = StableAId, MinBittenPigs = 3, WindowDays = 7, CountDepth = 2 };
        await stable.Create(Db);
        var gone = new TailBiteRule { LocationId = StableBId, MinSevere = 1, WindowDays = 7, CountDepth = 1 };
        await gone.Create(Db);
        await gone.Delete(Db);

        var rules = await Sut().ListRulesAsync(WorkerSite, PropertyId);

        Assert.That(rules.Select(r => r.Id), Is.EqualTo(new[] { RuleId, stable.Id }));
        Assert.That((rules[1].LocationId, rules[1].MinBittenPigs, rules[1].MinSevere, rules[1].CountDepth, rules[1].Version),
            Is.EqualTo((StableAId, (int?)3, (int?)null, 2, 1)));
        Assert.That(rules[1].UpdatedAt!.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
        await Assert.ThrowsAsync<TailBiteForbiddenException>(() => Sut().ListRulesAsync(ForeignSite, PropertyId));
    }

    [Test]
    public async Task RuleHistory_NewestFirst_WithoutTheDeleteRow_EvenForADeletedRule()
    {
        await SeedTreeAsync();
        await SeedWorkerAsync(WorkerSite);
        var rule = new TailBiteRule { LocationId = StableAId, MinBittenPigs = 8, WindowDays = 14, CountDepth = 1 };
        await rule.Create(Db);
        rule.MinBittenPigs = 5;
        rule.MinSevere = 1;
        rule.WindowDays = 7;
        await rule.Update(Db);
        await rule.Delete(Db);

        var history = await Sut().RuleHistoryAsync(WorkerSite, rule.Id);

        Assert.That(history.Select(v => (v.Version, v.MinBittenPigs, v.MinSevere, v.WindowDays)), Is.EqualTo(new[]
        {
            (2, (int?)5, (int?)1, 7), (1, (int?)8, (int?)null, 14),
        }));
    }

    [Test]
    public async Task RuleHistory_MissingAndForeignIdsAreRefusedAlike()
    {
        await SeedTreeAsync();
        await SeedWorkerAsync(WorkerSite);
        await AssertRefusedAlike(
            () => Sut().RuleHistoryAsync(WorkerSite, int.MaxValue),
            () => Sut().RuleHistoryAsync(ForeignSite, RuleId));
    }

    // ---------- occupancy ----------

    [Test]
    public async Task CurrentOccupancy_NewestValidFromNotInTheFuture_PerLocation()
    {
        await SeedTreeAsync();
        await SeedWorkerAsync(WorkerSite);
        var now = Clock.GetUtcNow().UtcDateTime;
        async Task Count(int locationId, int pigs, DateTime validFrom, bool removed = false)
        {
            var o = new TailBiteOccupancy { LocationId = locationId, PigCount = pigs, Source = TailBiteOccupancySource.Manual, ValidFrom = validFrom };
            await o.Create(Db);
            if (removed) await o.Delete(Db);
        }
        await Count(SectionId, 300, now.AddDays(-30));
        await Count(SectionId, 360, now.AddDays(-2));
        await Count(SectionId, 999, now.AddDays(3));
        await Count(Pen309Id, 30, now.AddDays(-1), removed: true);

        var occupancy = await Sut().CurrentOccupancyAsync(WorkerSite, PropertyId);

        Assert.That(occupancy.Select(o => (o.LocationId, o.PigCount)), Is.EqualTo(new[] { (SectionId, 360) }));
        Assert.That(occupancy[0].ValidFrom.Kind, Is.EqualTo(DateTimeKind.Utc));
        await Assert.ThrowsAsync<TailBiteForbiddenException>(() => Sut().CurrentOccupancyAsync(ForeignSite, PropertyId));
    }

    // ---------- outbreak registrations ----------

    private async Task<TailBiteOutbreak> OutbreakWithLinksAsync(int locationId, params int[] rowIds)
    {
        var outbreak = new TailBiteOutbreak
        {
            PropertyId = PropertyId, LocationId = locationId, RuleId = RuleId, RuleVersion = 1,
            OpenedAt = Clock.GetUtcNow().UtcDateTime, OpenedByRegistrationId = 1
        };
        await outbreak.Create(Db);
        foreach (var rowId in rowIds)
            await new TailBiteOutbreakLink { OutbreakId = outbreak.Id, RegistrationLocationId = rowId }.Create(Db);
        return outbreak;
    }

    private async Task PhotoAsync(TailBiteRegistration reg, int uploadedBySiteId, int sdkUploadedDataId, bool removed = false)
    {
        var photo = new TailBiteRegistrationPhoto
        {
            PhotoUuid = Guid.NewGuid(), PropertyId = PropertyId, UploadedBySiteId = uploadedBySiteId,
            RegistrationClientUuid = reg.ClientUuid, SdkUploadedDataId = sdkUploadedDataId
        };
        await photo.Create(Db);
        if (removed) await photo.Delete(Db);
    }

    [Test]
    public async Task OutbreakRegistrations_LinkedRowsInTimeOrder_WithActionsPhotosNamesAndCancellation()
    {
        await SeedTreeAsync();
        await SeedWorkerAsync(ManagerSite, manager: true);
        var now = Clock.GetUtcNow().UtcDateTime;
        var (late, lateRows) = await SeedRegistrationAsync(now.AddHours(-1), false, (Pen310Id, 1, 0));
        var (early, earlyRows) = await SeedRegistrationAsync(now.AddDays(-2), true, (Pen309Id, 2, 1), (Pen501Id, 4, 0));
        var halm = new TailBiteActionType { PropertyId = PropertyId, Code = "HALM", Name = "Halm" };
        await halm.Create(Db);
        await new TailBiteRegistrationAction { RegistrationId = late.Id, ActionTypeId = halm.Id }.Create(Db);
        await PhotoAsync(late, uploadedBySiteId: 7, sdkUploadedDataId: 55);
        await PhotoAsync(late, uploadedBySiteId: 7, sdkUploadedDataId: 0);   // a reservation, bytes not stored
        await PhotoAsync(late, uploadedBySiteId: 8, sdkUploadedDataId: 56);  // someone else's upload with the same uuid
        await PhotoAsync(late, uploadedBySiteId: 7, sdkUploadedDataId: 57, removed: true);
        // Sti 501 belongs to Stald B: only the Stald A rows are linked to this outbreak.
        var outbreak = await OutbreakWithLinksAsync(StableAId, lateRows[0], earlyRows[0]);

        var result = await Sut().OutbreakRegistrationsAsync(ManagerSite, outbreak.Id);

        Assert.That(result.PropertyId, Is.EqualTo(PropertyId));
        Assert.That(result.Rows.Select(r => (r.RegistrationId, r.LocationId, r.Minor, r.Severe, r.Cancelled)), Is.EqualTo(new[]
        {
            (early.Id, Pen309Id, 2, 1, true), (late.Id, Pen310Id, 1, 0, false),
        }));
        var lateRow = result.Rows[1];
        Assert.That(lateRow.ActionTypeIds, Is.EqualTo(new[] { halm.Id }));
        Assert.That(lateRow.PhotoCount, Is.EqualTo(1));
        Assert.That(lateRow.SiteName, Is.EqualTo("Jane Doe"));
        Assert.That(lateRow.EffectiveAt.Kind, Is.EqualTo(DateTimeKind.Utc));
        Assert.That(result.Rows[0].CancelReason, Is.EqualTo("Registreret på forkert sti"));
    }

    [Test]
    public async Task OutbreakRegistrations_NeedsAManager_MissingAndForeignIdsRefusedAlike()
    {
        await SeedTreeAsync();
        await SeedWorkerAsync(WorkerSite);
        var outbreak = await OutbreakWithLinksAsync(StableAId);
        await AssertRefusedAlike(
            () => Sut().OutbreakRegistrationsAsync(WorkerSite, int.MaxValue),
            () => Sut().OutbreakRegistrationsAsync(WorkerSite, outbreak.Id));
    }

    // ListProperties / ListWorkers have no caller check in the service: any route that exposes them must be plugin-admin only.
    [Test]
    public void RoutesExposingPropertyAndWorkerLists_CarryThePluginAdminPolicy()
    {
        var exposing = typeof(TailBiteWebQueryService).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t))
            .Where(t => t.GetConstructors().Any(c => c.GetParameters().Any(p => p.ParameterType == typeof(ITailBiteWebQueryService))))
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance
                                          | BindingFlags.DeclaredOnly))
            .Where(m => m.Name.Contains("Properties") || m.Name.Contains("Workers"))
            .ToList();
        Assert.That(exposing, Is.Not.Empty, "no controller action exposing the property or worker lists was found; the test would pass vacuously");
        foreach (var action in exposing)
        {
            var policies = action.GetCustomAttributes(typeof(AuthorizeAttribute), true)
                .Cast<AuthorizeAttribute>().Select(a => a.Policy);
            Assert.That(policies, Does.Contain(BackendConfigurationClaims.AccessBackendConfigurationPlugin),
                $"{action.DeclaringType!.Name}.{action.Name} must be plugin-admin only");
        }
    }
}
