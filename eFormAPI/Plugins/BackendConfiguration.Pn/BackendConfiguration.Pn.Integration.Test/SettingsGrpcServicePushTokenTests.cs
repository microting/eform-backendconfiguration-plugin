using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.GrpcServices;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.Settings.V1;
using NSubstitute;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// DB-backed tests for <see cref="SettingsGrpcService.RegisterPushToken"/>'s
/// upsert semantics against the real unique (WorkerId, FcmToken) index.
/// The critical case is <see cref="RegisterPushToken_AfterSoftDelete_RevivesExistingRow"/>:
/// the index has no WorkflowState filter and PnBase.Delete() only soft-deletes,
/// so re-registering after logout MUST Update() the removed row back to
/// Created instead of Create()ing a duplicate (which would throw
/// DbUpdateException).
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class SettingsGrpcServicePushTokenTests : TestBaseSetup
{
    // The DeviceToken tables were added after the raw SQL bootstrap script
    // (SQL/420_eform-backend-configuration-plugin.sql) was last regenerated,
    // so TestBaseSetup.Setup's DROP+CREATE pass never touches them and rows
    // would otherwise accumulate across tests (mirrors
    // AdhocServiceTaskCrudTests.CleanAdhocTables).
    [SetUp]
    public async Task CleanDeviceTokenTables()
    {
        BackendConfigurationPnDbContext!.DeviceTokenVersions.RemoveRange(
            BackendConfigurationPnDbContext.DeviceTokenVersions);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.DeviceTokens.RemoveRange(
            BackendConfigurationPnDbContext.DeviceTokens);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
    }

    private SettingsGrpcService CreateSut(int resolvedWorkerId)
    {
        var resolver = Substitute.For<IGrpcSiteResolver>();
        resolver.GetSdkSiteIdAsync().Returns(resolvedWorkerId);
        return new SettingsGrpcService(
            BackendConfigurationPnDbContext!,
            resolver,
            NullLogger<SettingsGrpcService>.Instance);
    }

    private static RegisterPushTokenRequest MakeRequest(string token, string platform = "android") =>
        new() { FcmToken = token, Platform = platform };

    private static ServerCallContext Context() => Substitute.For<ServerCallContext>();

    [Test]
    public async Task RegisterPushToken_NewToken_CreatesRow()
    {
        var sut = CreateSut(7);

        await sut.RegisterPushToken(MakeRequest("token-a"), Context());

        var rows = await BackendConfigurationPnDbContext!.DeviceTokens.AsNoTracking().ToListAsync();
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].WorkerId, Is.EqualTo(7));
        Assert.That(rows[0].FcmToken, Is.EqualTo("token-a"));
        Assert.That(rows[0].Platform, Is.EqualTo("android"));
        Assert.That(rows[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
        Assert.That(rows[0].Version, Is.EqualTo(1));
    }

    [Test]
    public async Task RegisterPushToken_ExistingToken_UpdatesRowInsteadOfCreating()
    {
        var sut = CreateSut(7);
        await sut.RegisterPushToken(MakeRequest("token-a", "android"), Context());

        await sut.RegisterPushToken(MakeRequest("token-a", "ios"), Context());

        var rows = await BackendConfigurationPnDbContext!.DeviceTokens.AsNoTracking().ToListAsync();
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].WorkerId, Is.EqualTo(7));
        Assert.That(rows[0].FcmToken, Is.EqualTo("token-a"));
        Assert.That(rows[0].Platform, Is.EqualTo("ios"));
        Assert.That(rows[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
        Assert.That(rows[0].Version, Is.EqualTo(2));
    }

    [Test]
    public async Task RegisterPushToken_AfterSoftDelete_RevivesExistingRow()
    {
        var sut = CreateSut(7);
        await sut.RegisterPushToken(MakeRequest("token-a"), Context());

        // Logout path: PnBase.Delete() soft-deletes (WorkflowState=Removed,
        // row stays and still occupies the unique (WorkerId, FcmToken) slot).
        var stored = await BackendConfigurationPnDbContext!.DeviceTokens.SingleAsync();
        await stored.Delete(BackendConfigurationPnDbContext);
        Assert.That(
            (await BackendConfigurationPnDbContext.DeviceTokens.AsNoTracking().SingleAsync()).WorkflowState,
            Is.EqualTo(Constants.WorkflowStates.Removed));

        // Re-register after login: must revive, not Create() a duplicate.
        await sut.RegisterPushToken(MakeRequest("token-a", "android"), Context());

        var rows = await BackendConfigurationPnDbContext.DeviceTokens.AsNoTracking().ToListAsync();
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].Id, Is.EqualTo(stored.Id));
        Assert.That(rows[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
        Assert.That(rows[0].Platform, Is.EqualTo("android"));
    }

    [Test]
    public async Task RegisterPushToken_SameTokenForSecondWorker_CreatesSecondRow()
    {
        await CreateSut(7).RegisterPushToken(MakeRequest("token-a"), Context());

        await CreateSut(8).RegisterPushToken(MakeRequest("token-a"), Context());

        var rows = await BackendConfigurationPnDbContext!.DeviceTokens.AsNoTracking()
            .OrderBy(t => t.WorkerId).ToListAsync();
        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows[0].WorkerId, Is.EqualTo(7));
        Assert.That(rows[1].WorkerId, Is.EqualTo(8));
        Assert.That(rows.Select(r => r.FcmToken), Is.All.EqualTo("token-a"));
    }

    [Test]
    public void RegisterPushToken_NoResolvableWorker_ThrowsUnauthenticated()
    {
        var sut = CreateSut(0);

        var ex = Assert.ThrowsAsync<RpcException>(async () =>
            await sut.RegisterPushToken(MakeRequest("token-a"), Context()));
        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.Unauthenticated));
        Assert.That(BackendConfigurationPnDbContext!.DeviceTokens.AsNoTracking().Count(), Is.EqualTo(0));
    }

    [Test]
    public void RegisterPushToken_EmptyToken_ThrowsInvalidArgument()
    {
        var sut = CreateSut(7);

        var ex = Assert.ThrowsAsync<RpcException>(async () =>
            await sut.RegisterPushToken(MakeRequest("   "), Context()));
        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.InvalidArgument));
        Assert.That(BackendConfigurationPnDbContext!.DeviceTokens.AsNoTracking().Count(), Is.EqualTo(0));
    }
}
