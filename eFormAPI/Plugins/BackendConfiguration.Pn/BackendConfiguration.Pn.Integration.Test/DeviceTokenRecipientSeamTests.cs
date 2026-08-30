using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// Pins the register -> send seam: a DeviceToken registered under an SDK site
/// id must be selected by the same recipient query the adhoc reminder sender
/// uses. SettingsGrpcServicePushTokenTests covers the register half and the
/// bc-service suite covers the send half, but nothing asserts that what one
/// writes the other reads.
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

    /// Mirrors AdhocReminderJob.SendReminderForTask's token query.
    private Task<List<DeviceToken>> SelectRecipientsAsync(List<int> workerIds) =>
        BackendConfigurationPnDbContext!.DeviceTokens
            .AsNoTracking()
            .Where(x => x.WorkflowState == Constants.WorkflowStates.Created)
            .Where(x => workerIds.Contains(x.WorkerId))
            .ToListAsync();

    [Test]
    public async Task RegisteredToken_IsSelectedByAssignmentRecipientQuery()
    {
        const int siteId = 4711;

        await new DeviceToken
        {
            WorkerId = siteId,
            FcmToken = "seam-token-1",
            Platform = "android",
        }.Create(BackendConfigurationPnDbContext!);

        var selected = await SelectRecipientsAsync([siteId]);

        Assert.That(selected.Select(x => x.FcmToken), Is.EquivalentTo(new[] { "seam-token-1" }));
    }

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

        // Logout path: PnBase.Delete() only soft-deletes, so the row stays in
        // the table. The sender must not target it.
        await token.Delete(BackendConfigurationPnDbContext!);

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
