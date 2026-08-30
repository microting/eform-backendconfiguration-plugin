using System.Collections.Generic;
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
/// upsert semantics against the real unique (AppId, InstallationId) index.
/// <para>
/// Identity is the app install, not the token: <c>FcmToken</c> rotates and
/// <c>SdkSiteId</c> is reassigned when another user logs in on the same
/// device, so both are mutable fields of a row identified by
/// (AppId, InstallationId).
/// </para>
/// <para>
/// Two cases carry the weight here.
/// <see cref="RegisterPushToken_AfterSoftDelete_RevivesExistingRow"/>: the
/// index has no WorkflowState filter and PnBase.Delete() only soft-deletes, so
/// re-registering after logout MUST Update() the removed row back to Created
/// instead of Create()ing a duplicate (which would violate the unique index).
/// <see cref="RegisterPushToken_AdoptsLegacyRowMatchingOnToken"/>: the
/// DeviceTokenIdentityModel migration backfills pre-existing rows with a
/// synthetic <c>legacy:&lt;Id&gt;</c> InstallationId, so the first real
/// register from an already-installed device must claim that row rather than
/// insert beside it.
/// </para>
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

    private SettingsGrpcService CreateSut(int resolvedSdkSiteId)
    {
        var resolver = Substitute.For<IGrpcSiteResolver>();
        resolver.GetSdkSiteIdAsync().Returns(resolvedSdkSiteId);
        return new SettingsGrpcService(
            BackendConfigurationPnDbContext!,
            resolver,
            NullLogger<SettingsGrpcService>.Instance);
    }

    private static RegisterPushTokenRequest MakeRequest(
        string token,
        string installationId,
        string platform = "android",
        string appId = "adhoc") =>
        new()
        {
            FcmToken = token,
            Platform = platform,
            AppId = appId,
            InstallationId = installationId,
        };

    private static ServerCallContext Context() => Substitute.For<ServerCallContext>();

    private Task<List<DeviceToken>> AllRowsAsync() =>
        BackendConfigurationPnDbContext!.DeviceTokens.AsNoTracking().OrderBy(t => t.Id).ToListAsync();

    [Test]
    public async Task RegisterPushToken_NewToken_CreatesRow()
    {
        var sut = CreateSut(7);

        await sut.RegisterPushToken(MakeRequest("token-a", "inst-a"), Context());

        var rows = await AllRowsAsync();
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].SdkSiteId, Is.EqualTo(7));
        Assert.That(rows[0].AppId, Is.EqualTo("adhoc"));
        Assert.That(rows[0].InstallationId, Is.EqualTo("inst-a"));
        Assert.That(rows[0].FcmToken, Is.EqualTo("token-a"));
        Assert.That(rows[0].Platform, Is.EqualTo("android"));
        Assert.That(rows[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
        Assert.That(rows[0].Version, Is.EqualTo(1));
    }

    [Test]
    public async Task RegisterPushToken_ExistingInstall_UpdatesRowInsteadOfCreating()
    {
        var sut = CreateSut(7);
        await sut.RegisterPushToken(MakeRequest("token-a", "inst-a", "android"), Context());

        await sut.RegisterPushToken(MakeRequest("token-a", "inst-a", "ios"), Context());

        var rows = await AllRowsAsync();
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].SdkSiteId, Is.EqualTo(7));
        Assert.That(rows[0].FcmToken, Is.EqualTo("token-a"));
        Assert.That(rows[0].Platform, Is.EqualTo("ios"));
        Assert.That(rows[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
        Assert.That(rows[0].Version, Is.EqualTo(2));
    }

    /// <summary>
    /// FCM rotates a token without the install changing. The row is keyed on
    /// the install, so the new token must land on the existing row — an insert
    /// here would leave the dead token behind and double every push until FCM
    /// happened to report the old one Unregistered.
    /// </summary>
    [Test]
    public async Task RegisterPushToken_SameInstall_RotatedToken_UpdatesInPlace()
    {
        var sut = CreateSut(500);
        await sut.RegisterPushToken(MakeRequest("rot-old", "inst-rot"), Context());

        await sut.RegisterPushToken(MakeRequest("rot-new", "inst-rot"), Context());

        var rows = await AllRowsAsync();
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].FcmToken, Is.EqualTo("rot-new"));
        Assert.That(rows[0].InstallationId, Is.EqualTo("inst-rot"));
        Assert.That(rows[0].SdkSiteId, Is.EqualTo(500));
    }

    /// <summary>
    /// A different user logs in on the same device: the owner moves, the row
    /// does not. Leaving the old owner in place would keep pushing the previous
    /// user's reminders to a device they no longer hold.
    /// </summary>
    [Test]
    public async Task RegisterPushToken_SameInstall_DifferentUser_ReassignsOwner()
    {
        await CreateSut(600).RegisterPushToken(MakeRequest("own-1", "inst-own"), Context());

        await CreateSut(601).RegisterPushToken(MakeRequest("own-1", "inst-own"), Context());

        var rows = await AllRowsAsync();
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].SdkSiteId, Is.EqualTo(601));
        Assert.That(rows[0].InstallationId, Is.EqualTo("inst-own"));
    }

    /// <summary>
    /// One worker, two phones: two installs, two tokens, two live rows. Both
    /// must be selected by the sender, so neither register may displace the
    /// other.
    /// </summary>
    [Test]
    public async Task RegisterPushToken_SecondDevice_CreatesSecondRow()
    {
        var sut = CreateSut(42);
        await sut.RegisterPushToken(MakeRequest("dev-tok-1", "inst-dev-1"), Context());

        await sut.RegisterPushToken(MakeRequest("dev-tok-2", "inst-dev-2"), Context());

        var rows = await AllRowsAsync();
        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows.Select(r => r.InstallationId),
            Is.EquivalentTo(new[] { "inst-dev-1", "inst-dev-2" }));
        Assert.That(rows.Select(r => r.SdkSiteId), Is.All.EqualTo(42));
    }

    [Test]
    public async Task RegisterPushToken_AfterSoftDelete_RevivesExistingRow()
    {
        var sut = CreateSut(7);
        await sut.RegisterPushToken(MakeRequest("token-a", "inst-a"), Context());

        // Logout path: PnBase.Delete() soft-deletes (WorkflowState=Removed,
        // row stays and still occupies the unique (AppId, InstallationId) slot).
        var stored = await BackendConfigurationPnDbContext!.DeviceTokens.SingleAsync();
        await stored.Delete(BackendConfigurationPnDbContext);
        Assert.That(
            (await BackendConfigurationPnDbContext.DeviceTokens.AsNoTracking().SingleAsync()).WorkflowState,
            Is.EqualTo(Constants.WorkflowStates.Removed));

        // Re-register after login: must revive, not Create() a duplicate.
        await sut.RegisterPushToken(MakeRequest("token-a", "inst-a"), Context());

        var rows = await AllRowsAsync();
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].Id, Is.EqualTo(stored.Id));
        Assert.That(rows[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
        Assert.That(rows[0].Platform, Is.EqualTo("android"));
    }

    /// <summary>
    /// The migration's legacy rows. DeviceTokenIdentityModel backfills every
    /// pre-existing row with <c>InstallationId = CONCAT('legacy:', Id)</c> and
    /// leaves it WorkflowState=created, so the first register from an
    /// already-installed device carries an InstallationId nothing in the table
    /// matches. Inserting there would give that device two live rows holding
    /// one valid token — doubled pushes indefinitely, since FCM never prunes a
    /// token that still works. The (AppId, FcmToken) fallback claims the legacy
    /// row instead.
    /// </summary>
    [Test]
    public async Task RegisterPushToken_AdoptsLegacyRowMatchingOnToken()
    {
        var legacy = new DeviceToken
        {
            AppId = "adhoc",
            InstallationId = "legacy:123",
            FcmToken = "carried-over-token",
            SdkSiteId = 900,
            Platform = "android",
        };
        await legacy.Create(BackendConfigurationPnDbContext!);

        await CreateSut(900).RegisterPushToken(
            MakeRequest("carried-over-token", "real-install-uuid"), Context());

        var rows = await AllRowsAsync();
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].Id, Is.EqualTo(legacy.Id));
        Assert.That(rows[0].InstallationId, Is.EqualTo("real-install-uuid"));
        Assert.That(rows[0].FcmToken, Is.EqualTo("carried-over-token"));
        Assert.That(rows[0].SdkSiteId, Is.EqualTo(900));
        Assert.That(rows[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
    }

    /// <summary>
    /// Adoption is scoped to the app. An FcmToken is only meaningful within the
    /// Firebase project that minted it, so a row belonging to another app must
    /// never be claimed — that would hand one app's install row to the other
    /// and silently stop push for the app that lost it.
    /// </summary>
    [Test]
    public async Task RegisterPushToken_DoesNotAdoptAcrossApps()
    {
        await new DeviceToken
        {
            AppId = "adhoc",
            InstallationId = "legacy:456",
            FcmToken = "shared-looking-token",
            SdkSiteId = 901,
            Platform = "android",
        }.Create(BackendConfigurationPnDbContext!);

        await CreateSut(901).RegisterPushToken(
            MakeRequest("shared-looking-token", "eform-install", appId: "eform"), Context());

        var rows = await AllRowsAsync();
        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows.Select(r => r.AppId), Is.EquivalentTo(new[] { "adhoc", "eform" }));
        Assert.That(rows.Select(r => r.InstallationId),
            Is.EquivalentTo(new[] { "legacy:456", "eform-install" }));
    }

    /// <summary>
    /// Two legacy rows can share an FcmToken: the outgoing key was
    /// (WorkerId, FcmToken), so two workers signing in on one shared device
    /// each got a row. Adoption takes the lowest Id so that a retried or
    /// repeated register always lands on the same row; the sibling is left live
    /// for its own user's next register to adopt, or for FCM to prune.
    /// Soft-deleting it here would silently stop push for a user who never
    /// reopens the app.
    /// </summary>
    [Test]
    public async Task RegisterPushToken_TwoLegacyRowsSharingToken_AdoptsLowestIdDeterministically()
    {
        foreach (var siteId in new[] { 910, 911 })
        {
            await new DeviceToken
            {
                AppId = "adhoc",
                InstallationId = "legacy:site-" + siteId,
                FcmToken = "shared-device-token",
                SdkSiteId = siteId,
                Platform = "android",
            }.Create(BackendConfigurationPnDbContext!);
        }

        var lowestId = (await AllRowsAsync())[0].Id;

        await CreateSut(910).RegisterPushToken(
            MakeRequest("shared-device-token", "real-shared-install"), Context());

        var rows = await AllRowsAsync();
        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows[0].Id, Is.EqualTo(lowestId));
        Assert.That(rows[0].InstallationId, Is.EqualTo("real-shared-install"));
        Assert.That(rows[1].InstallationId, Is.EqualTo("legacy:site-911"));
    }

    [Test]
    public void RegisterPushToken_NoResolvableWorker_ThrowsUnauthenticated()
    {
        var sut = CreateSut(0);

        var ex = Assert.ThrowsAsync<RpcException>(async () =>
            await sut.RegisterPushToken(MakeRequest("token-a", "inst-a"), Context()));
        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.Unauthenticated));
        Assert.That(BackendConfigurationPnDbContext!.DeviceTokens.AsNoTracking().Count(), Is.EqualTo(0));
    }

    [Test]
    public void RegisterPushToken_EmptyToken_ThrowsInvalidArgument()
    {
        var sut = CreateSut(7);

        var ex = Assert.ThrowsAsync<RpcException>(async () =>
            await sut.RegisterPushToken(MakeRequest("   ", "inst-a"), Context()));
        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.InvalidArgument));
        Assert.That(BackendConfigurationPnDbContext!.DeviceTokens.AsNoTracking().Count(), Is.EqualTo(0));
    }

    [Test]
    public void RegisterPushToken_MissingAppId_ThrowsInvalidArgument()
    {
        var sut = CreateSut(7);

        var ex = Assert.ThrowsAsync<RpcException>(async () =>
            await sut.RegisterPushToken(MakeRequest("token-a", "inst-a", appId: "  "), Context()));
        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.InvalidArgument));
        Assert.That(BackendConfigurationPnDbContext!.DeviceTokens.AsNoTracking().Count(), Is.EqualTo(0));
    }

    [Test]
    public void RegisterPushToken_MissingInstallationId_ThrowsInvalidArgument()
    {
        var sut = CreateSut(7);

        var ex = Assert.ThrowsAsync<RpcException>(async () =>
            await sut.RegisterPushToken(MakeRequest("token-a", "  "), Context()));
        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.InvalidArgument));
        Assert.That(BackendConfigurationPnDbContext!.DeviceTokens.AsNoTracking().Count(), Is.EqualTo(0));
    }
}
