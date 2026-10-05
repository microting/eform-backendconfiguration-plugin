#nullable enable
using System.Collections.Generic;
using System.Linq;
using BackendConfiguration.Pn.Services.TailBite;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Test.Services;

/// <summary>
/// Runs the shipped TargetTokenQuery over in-memory rows, as AdhocReminderRecipientTests does in the service plugin.
/// LINQ-to-Objects compares strings case-sensitively while MariaDB does not; the AppId values here are all lower case.
/// </summary>
public class TailBitePushSenderTests
{
    private static DeviceToken Device(string appId, int sdkSiteId, string fcm, string? workflowState = null) => new()
    {
        AppId = appId, InstallationId = $"install-{fcm}", SdkSiteId = sdkSiteId, FcmToken = fcm,
        Platform = "android", WorkflowState = workflowState ?? Constants.WorkflowStates.Created
    };

    [Test]
    public void TargetTokens_SelectOnlyHalebidTokens()
    {
        var devices = new List<DeviceToken>
        {
            Device("halebid", 7, "hb-7"),
            Device("eform", 7, "ef-7"),
            Device("adhoc", 7, "ad-7"),
            Device("halebid", 8, "hb-8"),
            Device("halebid", 7, "hb-7-removed", Constants.WorkflowStates.Removed)
        };

        var selected = TailBitePushSender.TargetTokenQuery(devices.AsQueryable(), 7).Select(d => d.FcmToken).ToList();

        Assert.That(selected, Is.EqualTo(new[] { "hb-7" }));
    }
}
