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
            TestContextLogger<SettingsGrpcService>.Instance);
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

    /// <summary>
    /// A row exactly as the DeviceTokenIdentityModel migration leaves it: a
    /// live adhoc token whose InstallationId is the synthetic
    /// <c>legacy:&lt;Id&gt;</c> backfill instead of a client-sent uuid.
    /// </summary>
    private async Task<DeviceToken> SeedLegacyAdhocRowAsync(
        string installationId,
        string fcmToken,
        int sdkSiteId)
    {
        var row = new DeviceToken
        {
            AppId = "adhoc",
            InstallationId = installationId,
            FcmToken = fcmToken,
            SdkSiteId = sdkSiteId,
            Platform = "android",
        };
        await row.Create(BackendConfigurationPnDbContext!);
        return row;
    }

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
        var legacy = await SeedLegacyAdhocRowAsync("legacy:123", "carried-over-token", sdkSiteId: 900);

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
        await SeedLegacyAdhocRowAsync("legacy:456", "shared-looking-token", sdkSiteId: 901);

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
    /// each got their own row. Adoption takes the lowest Id, so a retried or
    /// repeated register always lands on the same row, and the sibling is
    /// retired — one token addresses one device, so a second live row holding
    /// it is a second copy of every push.
    /// </summary>
    [Test]
    public async Task RegisterPushToken_TwoLegacyRowsSharingToken_AdoptsLowestIdAndRetiresTheSibling()
    {
        foreach (var siteId in new[] { 910, 911 })
        {
            await SeedLegacyAdhocRowAsync("legacy:site-" + siteId, "shared-device-token", siteId);
        }

        var seeded = await AllRowsAsync();
        Assert.That(seeded.Select(r => r.WorkflowState),
            Is.All.EqualTo(Constants.WorkflowStates.Created), "both legacy rows start live");

        await CreateSut(910).RegisterPushToken(
            MakeRequest("shared-device-token", "real-shared-install"), Context());

        var rows = await AllRowsAsync();
        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows[0].Id, Is.EqualTo(seeded[0].Id), "the lowest Id is the one adopted");
        Assert.That(rows[0].InstallationId, Is.EqualTo("real-shared-install"));
        Assert.That(rows[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
        Assert.That(rows[1].InstallationId, Is.EqualTo("legacy:site-911"));
        Assert.That(rows[1].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
    }

    /// <summary>
    /// The case adoption alone cannot fix, and the reason the sibling is
    /// retired rather than left for "its own user's next register".
    /// InstallationId identifies the install, not the user: both workers on a
    /// shared device register from the SAME install, so the second one hits the
    /// row the first already adopted and never reaches the (AppId, FcmToken)
    /// fallback. If the sibling stayed live it would end up alongside the
    /// adopted row with the same site and the same token — every reminder for
    /// site 911 delivered twice, and site 911's reminders still going to that
    /// device while worker 910 is the one signed in.
    /// </summary>
    [Test]
    public async Task RegisterPushToken_SharedDeviceUserSwitch_LeavesOneLiveRowForTheToken()
    {
        foreach (var siteId in new[] { 920, 921 })
        {
            await SeedLegacyAdhocRowAsync("legacy:shared-" + siteId, "one-device-token", siteId);
        }

        // Worker 920 opens the app first; worker 921 then signs in on the same
        // device, so both registers carry the same installation id.
        await CreateSut(920).RegisterPushToken(
            MakeRequest("one-device-token", "shared-install"), Context());
        await CreateSut(921).RegisterPushToken(
            MakeRequest("one-device-token", "shared-install"), Context());

        var live = (await AllRowsAsync())
            .Where(r => r.WorkflowState == Constants.WorkflowStates.Created)
            .ToList();
        Assert.That(live, Has.Count.EqualTo(1));
        Assert.That(live[0].SdkSiteId, Is.EqualTo(921));
        Assert.That(live[0].InstallationId, Is.EqualTo("shared-install"));
        Assert.That(live[0].FcmToken, Is.EqualTo("one-device-token"));
    }

    [Test]
    public async Task RegisterPushToken_NoResolvableWorker_ThrowsUnauthenticated()
    {
        var sut = CreateSut(0);

        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
            await sut.RegisterPushToken(MakeRequest("token-a", "inst-a"), Context()));
        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.Unauthenticated));
        Assert.That(BackendConfigurationPnDbContext!.DeviceTokens.AsNoTracking().Count(), Is.EqualTo(0));
    }

    [Test]
    public async Task RegisterPushToken_EmptyToken_ThrowsInvalidArgument()
    {
        var sut = CreateSut(7);

        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
            await sut.RegisterPushToken(MakeRequest("   ", "inst-a"), Context()));
        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.InvalidArgument));
        Assert.That(BackendConfigurationPnDbContext!.DeviceTokens.AsNoTracking().Count(), Is.EqualTo(0));
    }

    [Test]
    public async Task RegisterPushToken_MissingAppId_ThrowsInvalidArgument()
    {
        var sut = CreateSut(7);

        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
            await sut.RegisterPushToken(MakeRequest("token-a", "inst-a", appId: "  "), Context()));
        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.InvalidArgument));
        Assert.That(BackendConfigurationPnDbContext!.DeviceTokens.AsNoTracking().Count(), Is.EqualTo(0));
    }

    [Test]
    public async Task RegisterPushToken_MissingInstallationId_ThrowsInvalidArgument()
    {
        var sut = CreateSut(7);

        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
            await sut.RegisterPushToken(MakeRequest("token-a", "  "), Context()));
        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.InvalidArgument));
        Assert.That(BackendConfigurationPnDbContext!.DeviceTokens.AsNoTracking().Count(), Is.EqualTo(0));
    }

    // ---- ReportInstall (#1335) ---------------------------------------------

    private static ReportInstallRequest MakeReport(
        string installationId,
        string appVersion = "1.1.1",
        string appId = "adhoc",
        string platform = "ios") =>
        new()
        {
            AppId = appId,
            InstallationId = installationId,
            Platform = platform,
            AppVersion = appVersion,
            AppBuild = "11101",
            DeviceModel = "iPhone 15",
            DeviceManufacturer = "Apple",
            OsVersion = "18.1",
        };

    [Test]
    public async Task ReportInstall_NewInstall_CreatesRowWithoutAPushToken()
    {
        var sut = CreateSut(7);

        await sut.ReportInstall(MakeReport("inst-r"), Context());

        var rows = await AllRowsAsync();
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(rows[0].SdkSiteId, Is.EqualTo(7));
            Assert.That(rows[0].AppId, Is.EqualTo("adhoc"));
            Assert.That(rows[0].InstallationId, Is.EqualTo("inst-r"));
            Assert.That(rows[0].FcmToken, Is.Null, "no push permission: no token");
            Assert.That(rows[0].Platform, Is.EqualTo("ios"));
            Assert.That(rows[0].AppVersion, Is.EqualTo("1.1.1"));
            Assert.That(rows[0].AppBuild, Is.EqualTo("11101"));
            Assert.That(rows[0].DeviceModel, Is.EqualTo("iPhone 15"));
            Assert.That(rows[0].DeviceManufacturer, Is.EqualTo("Apple"));
            Assert.That(rows[0].OsVersion, Is.EqualTo("18.1"));
            Assert.That(rows[0].LastSeenAt, Is.Not.Null);
            Assert.That(rows[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
        });
    }

    [Test]
    public async Task ReportInstall_OnARegisteredInstall_KeepsTheTokenAndAddsTheReport()
    {
        var sut = CreateSut(7);
        await sut.RegisterPushToken(MakeRequest("token-a", "inst-a", platform: "android"), Context());

        await sut.ReportInstall(MakeReport("inst-a", platform: ""), Context());

        var rows = await AllRowsAsync();
        Assert.That(rows, Has.Count.EqualTo(1), "one install, one row");
        Assert.Multiple(() =>
        {
            Assert.That(rows[0].FcmToken, Is.EqualTo("token-a"));
            Assert.That(rows[0].Platform, Is.EqualTo("android"), "a blank platform keeps the registered one");
            Assert.That(rows[0].AppVersion, Is.EqualTo("1.1.1"));
        });
    }

    [Test]
    public async Task RegisterPushToken_AfterReportInstall_AddsTheTokenToTheSameRow()
    {
        var sut = CreateSut(7);
        await sut.ReportInstall(MakeReport("inst-a"), Context());

        await sut.RegisterPushToken(MakeRequest("token-a", "inst-a"), Context());

        var rows = await AllRowsAsync();
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(rows[0].FcmToken, Is.EqualTo("token-a"));
            Assert.That(rows[0].AppVersion, Is.EqualTo("1.1.1"), "the report survives a token register");
        });
    }

    [Test]
    public async Task ReportInstall_AgainAfterAnUpdate_OverwritesTheVersion_AndMovesTheOwner()
    {
        await CreateSut(7).ReportInstall(MakeReport("inst-a", appVersion: "1.1.0"), Context());

        await CreateSut(8).ReportInstall(MakeReport("inst-a", appVersion: "1.1.1"), Context());

        var rows = await AllRowsAsync();
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(rows[0].AppVersion, Is.EqualTo("1.1.1"));
            Assert.That(rows[0].SdkSiteId, Is.EqualTo(8), "another worker signed in on the device");
        });
    }

    [Test]
    public async Task ReportInstall_AfterLogout_RevivesTheRowWithoutItsOldToken()
    {
        var sut = CreateSut(7);
        await sut.RegisterPushToken(MakeRequest("token-a", "inst-a"), Context());
        var stored = await BackendConfigurationPnDbContext!.DeviceTokens.SingleAsync();
        await stored.Delete(BackendConfigurationPnDbContext);

        await CreateSut(8).ReportInstall(MakeReport("inst-a"), Context());

        var rows = await AllRowsAsync();
        Assert.That(rows, Has.Count.EqualTo(1), "revived, not duplicated");
        Assert.Multiple(() =>
        {
            Assert.That(rows[0].Id, Is.EqualTo(stored.Id));
            Assert.That(rows[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
            Assert.That(rows[0].FcmToken, Is.Null,
                "the old token must not reach the new owner before the app registers again");
            Assert.That(rows[0].SdkSiteId, Is.EqualTo(8));
        });
    }

    [Test]
    public async Task ReportInstall_FromAnotherOwner_ClearsTheToken()
    {
        // The push token must not follow the install to a worker it was not minted for.
        await CreateSut(7).RegisterPushToken(MakeRequest("token-a", "inst-a"), Context());

        await CreateSut(8).ReportInstall(MakeReport("inst-a"), Context());

        var row = (await AllRowsAsync()).Single();
        Assert.Multiple(() =>
        {
            Assert.That(row.SdkSiteId, Is.EqualTo(8));
            Assert.That(row.FcmToken, Is.Null);
        });
    }

    [Test]
    public async Task ReportInstall_SameReportAgainWithinTheHour_WritesNothing()
    {
        // The apps report on every start; an unchanged report must not add a version row each time.
        var sut = CreateSut(7);
        await sut.ReportInstall(MakeReport("inst-a"), Context());
        var versionsBefore = await BackendConfigurationPnDbContext!.DeviceTokenVersions.AsNoTracking().CountAsync();

        await sut.ReportInstall(MakeReport("inst-a"), Context());

        Assert.That(await BackendConfigurationPnDbContext.DeviceTokenVersions.AsNoTracking().CountAsync(),
            Is.EqualTo(versionsBefore));
    }

    [TestCase(true, false, TestName = "ReportInstall_AChangedReport_IsWritten")]
    [TestCase(false, true, TestName = "ReportInstall_TheSameReportAfterAnHour_IsWritten")]
    public async Task ReportInstall_ChangedOrStale_IsWritten(bool changed, bool stale)
    {
        var sut = CreateSut(7);
        await sut.ReportInstall(MakeReport("inst-a", appVersion: "1.1.0"), Context());
        if (stale)
        {
            await BackendConfigurationPnDbContext!.DeviceTokens
                .ExecuteUpdateAsync(u => u.SetProperty(t => t.LastSeenAt, DateTime.UtcNow.AddHours(-2)));
        }
        var versionsBefore = await BackendConfigurationPnDbContext!.DeviceTokenVersions.AsNoTracking().CountAsync();

        BackendConfigurationPnDbContext.ChangeTracker.Clear();
        await sut.ReportInstall(MakeReport("inst-a", appVersion: changed ? "1.1.1" : "1.1.0"), Context());

        var row = (await AllRowsAsync()).Single();
        await Assert.MultipleAsync(async () =>
        {
            Assert.That(await BackendConfigurationPnDbContext.DeviceTokenVersions.AsNoTracking().CountAsync(),
                Is.EqualTo(versionsBefore + 1));
            Assert.That(row.AppVersion, Is.EqualTo(changed ? "1.1.1" : "1.1.0"));
            Assert.That(row.LastSeenAt, Is.GreaterThan(DateTime.UtcNow.AddMinutes(-5)));
        });
    }

    [Test]
    public async Task RegisterPushToken_SameTokenAgain_RestoresItAfterAConcurrentClear()
    {
        // A concurrent ReportInstall clears the token (in SQL) after this call loaded the row;
        // the tracked entity still holds the same token, so EF alone would not write it back.
        var sut = CreateSut(7);
        await sut.RegisterPushToken(MakeRequest("token-a", "inst-a"), Context());
        await BackendConfigurationPnDbContext!.DeviceTokens
            .ExecuteUpdateAsync(u => u.SetProperty(t => t.FcmToken, (string)null));

        await sut.RegisterPushToken(MakeRequest("token-a", "inst-a"), Context());

        Assert.That((await AllRowsAsync()).Single().FcmToken, Is.EqualTo("token-a"));
    }

    [Test]
    public async Task RegisterPushToken_StaleRegisterAfterATokenRotation_KeepsTheNewToken()
    {
        // The device rotated its token to token-b (written in SQL) after this call loaded the
        // row holding token-a; the late register of token-a must not overwrite the newer token.
        var sut = CreateSut(7);
        await sut.RegisterPushToken(MakeRequest("token-a", "inst-a"), Context());
        await BackendConfigurationPnDbContext!.DeviceTokens
            .ExecuteUpdateAsync(u => u.SetProperty(t => t.FcmToken, "token-b"));

        await sut.RegisterPushToken(MakeRequest("token-a", "inst-a"), Context());

        Assert.That((await AllRowsAsync()).Single().FcmToken, Is.EqualTo("token-b"));
    }

    [Test]
    public async Task RegisterPushToken_StaleRegisterAfterAnotherWorkerTookTheDevice_DoesNotHandBackTheToken()
    {
        // Worker 8 signed in on the device meanwhile (ReportInstall moved the row and cleared
        // the token). A register from worker 7 that loaded the row before that must not move it
        // back to 7 with 7's token; worker 8's own register restores the token.
        var sut = CreateSut(7);
        await sut.RegisterPushToken(MakeRequest("token-a", "inst-a"), Context());
        await BackendConfigurationPnDbContext!.DeviceTokens.ExecuteUpdateAsync(u => u
            .SetProperty(t => t.SdkSiteId, 8)
            .SetProperty(t => t.FcmToken, (string)null));

        await sut.RegisterPushToken(MakeRequest("token-a", "inst-a"), Context());

        var row = (await AllRowsAsync()).Single();
        Assert.Multiple(() =>
        {
            Assert.That(row.SdkSiteId, Is.EqualTo(8));
            Assert.That(row.FcmToken, Is.Null);
        });
    }

    [Test]
    public async Task RegisterPushToken_SameRegistrationAgain_WritesNoVersionRow()
    {
        var sut = CreateSut(7);
        await sut.RegisterPushToken(MakeRequest("token-a", "inst-a"), Context());
        var versionsBefore = await BackendConfigurationPnDbContext!.DeviceTokenVersions.AsNoTracking().CountAsync();

        await sut.RegisterPushToken(MakeRequest("token-a", "inst-a"), Context());

        Assert.That(await BackendConfigurationPnDbContext.DeviceTokenVersions.AsNoTracking().CountAsync(),
            Is.EqualTo(versionsBefore));
    }

    [Test]
    public async Task ReportInstall_ValuesAreTrimmed()
    {
        await CreateSut(7).ReportInstall(MakeReport("inst-a", appVersion: "  1.2.3 "), Context());

        Assert.That((await AllRowsAsync()).Single().AppVersion, Is.EqualTo("1.2.3"));
    }

    [Test]
    public async Task ReportInstall_LongValues_AreCutToTheColumnLength()
    {
        var sut = CreateSut(7);
        var request = MakeReport("inst-a", appVersion: new string('9', 40));
        request.DeviceModel = new string('m', 200);

        await sut.ReportInstall(request, Context());

        var row = (await AllRowsAsync()).Single();
        Assert.Multiple(() =>
        {
            Assert.That(row.AppVersion, Has.Length.EqualTo(32));
            Assert.That(row.DeviceModel, Has.Length.EqualTo(128));
        });
    }

    [Test]
    public async Task ReportInstall_NoResolvableWorker_ThrowsUnauthenticated()
    {
        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
            await CreateSut(0).ReportInstall(MakeReport("inst-a"), Context()));
        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.Unauthenticated));
        Assert.That(BackendConfigurationPnDbContext!.DeviceTokens.AsNoTracking().Count(), Is.EqualTo(0));
    }

    [TestCase("  ", "inst-a", TestName = "ReportInstall_MissingAppId_ThrowsInvalidArgument")]
    [TestCase("adhoc", "  ", TestName = "ReportInstall_MissingInstallationId_ThrowsInvalidArgument")]
    public async Task ReportInstall_MissingIdentity_ThrowsInvalidArgument(string appId, string installationId)
    {
        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
            await CreateSut(7).ReportInstall(MakeReport(installationId, appId: appId), Context()));
        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.InvalidArgument));
        Assert.That(BackendConfigurationPnDbContext!.DeviceTokens.AsNoTracking().Count(), Is.EqualTo(0));
    }
}
