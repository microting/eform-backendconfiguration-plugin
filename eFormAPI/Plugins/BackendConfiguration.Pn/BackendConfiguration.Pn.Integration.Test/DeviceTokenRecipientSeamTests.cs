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
/// Pins the register -> send seam: a DeviceToken written by the real
/// <see cref="SettingsGrpcService.RegisterPushToken"/> must be selected by the
/// same recipient query the adhoc reminder sender uses.
/// <see cref="SettingsGrpcServicePushTokenTests"/> covers the register half and
/// the bc-service suite covers the send half, but nothing asserts that what one
/// writes the other reads.
/// <para>
/// The write side therefore goes through the production service, not a
/// hand-built row: if it were hand-built, both ends of the "seam" would be a
/// restatement of production behaviour and would keep passing after
/// RegisterPushToken is rewritten for a new schema, whether or not register
/// and send still agree.
/// </para>
/// <para>
/// The query below is a copy of AdhocReminderJob.SendReminderForTask's token
/// query (eform-service-backendconfiguration-plugin). That job lives in
/// another repo and cannot be referenced from here, so this is a deliberate
/// duplicate: it fails if the shared DeviceToken schema stops satisfying the
/// sender's assumptions, which is exactly the regression the device-token
/// identity model change could introduce.
/// </para>
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class DeviceTokenRecipientSeamTests : TestBaseSetup
{
    // The DeviceToken tables were added after the raw SQL bootstrap script
    // (SQL/420_eform-backend-configuration-plugin.sql) was last regenerated,
    // so TestBaseSetup.Setup's DROP+CREATE pass never touches them and rows
    // would otherwise accumulate across tests (mirrors
    // SettingsGrpcServicePushTokenTests.CleanDeviceTokenTables).
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

    // Same construction as SettingsGrpcServicePushTokenTests.CreateSut/Context:
    // the resolver stands in for the authenticated caller's SDK site id.
    private SettingsGrpcService CreateSut(int resolvedWorkerId)
    {
        var resolver = Substitute.For<IGrpcSiteResolver>();
        resolver.GetSdkSiteIdAsync().Returns(resolvedWorkerId);
        return new SettingsGrpcService(
            BackendConfigurationPnDbContext!,
            resolver,
            NullLogger<SettingsGrpcService>.Instance);
    }

    private static ServerCallContext Context() => Substitute.For<ServerCallContext>();

    /// <summary>
    /// Mirrors <c>AdhocReminderJob.SendReminderForTask</c>'s token query.
    /// </summary>
    private Task<List<DeviceToken>> SelectRecipientsAsync(List<int> workerIds) =>
        BackendConfigurationPnDbContext!.DeviceTokens
            .AsNoTracking()
            .Where(x => x.WorkflowState == Constants.WorkflowStates.Created)
            .Where(x => workerIds.Contains(x.WorkerId))
            .ToListAsync();

    [Test]
    public async Task RegisteredToken_IsSelectedByRecipientQuery()
    {
        const int siteId = 4711;

        // The seam itself: production's write path, then production's read path.
        await CreateSut(siteId).RegisterPushToken(
            new RegisterPushTokenRequest { FcmToken = "seam-token-1", Platform = "android" },
            Context());

        var selected = await SelectRecipientsAsync([siteId]);

        Assert.That(selected.Select(x => x.FcmToken), Is.EquivalentTo(new[] { "seam-token-1" }));
    }

    // The two tests below hand-build their rows on purpose: they assert what the
    // sender's query does with row states (soft-deleted, several per site), so
    // going through RegisterPushToken would only add its upsert semantics —
    // already covered by the sibling fixture — between the setup and the
    // assertion. RegisteredToken_IsSelectedByRecipientQuery above is the one
    // that must prove the real write path lands where the read path looks.

    [Test]
    public async Task SoftDeletedToken_IsNotSelected()
    {
        const int siteId = 4712;

        var token = new DeviceToken
        {
            WorkerId = siteId,
            FcmToken = "seam-token-2",
            Platform = "android",
        };
        await token.Create(BackendConfigurationPnDbContext!);

        // Positive control: without this, Is.Empty below would also pass if
        // Create() had done nothing or the query never matched anything.
        Assert.That(
            (await SelectRecipientsAsync([siteId])).Select(x => x.FcmToken),
            Is.EquivalentTo(new[] { "seam-token-2" }));

        // Logout path: PnBase.Delete() only soft-deletes, so the row stays in
        // the table. The sender must not target it.
        await token.Delete(BackendConfigurationPnDbContext!);
        Assert.That(
            (await BackendConfigurationPnDbContext!.DeviceTokens.AsNoTracking().SingleAsync()).WorkflowState,
            Is.EqualTo(Constants.WorkflowStates.Removed));

        var selected = await SelectRecipientsAsync([siteId]);

        Assert.That(selected, Is.Empty);
    }

    [Test]
    public async Task TwoDevicesForOneSite_BothSelected()
    {
        const int siteId = 4713;

        foreach (var fcmToken in new[] { "seam-multi-a", "seam-multi-b" })
        {
            await new DeviceToken
            {
                WorkerId = siteId,
                FcmToken = fcmToken,
                Platform = "android",
            }.Create(BackendConfigurationPnDbContext!);
        }

        var selected = await SelectRecipientsAsync([siteId]);

        Assert.That(
            selected.Select(x => x.FcmToken),
            Is.EquivalentTo(new[] { "seam-multi-a", "seam-multi-b" }));
    }
}
