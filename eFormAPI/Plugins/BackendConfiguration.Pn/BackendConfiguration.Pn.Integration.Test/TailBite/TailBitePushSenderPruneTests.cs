#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.TailBite;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Integration.Test.TailBite;

/// <summary>
/// The halebid push sender's selection and prune decision, driven through the real send loop with the FCM call
/// replaced. These are the PushNotificationServiceTests cases re-pointed at AppId "halebid"; the credential-less sender
/// never reaches Firebase, so no process-wide FirebaseApp is touched.
/// </summary>
[TestFixture]
public class TailBitePushSenderPruneTests : TestBaseSetup
{
    // ---- recipient selection ----------------------------------------------

    [Test]
    public async Task TargetTokenQuery_SelectsOnlyLiveHalebidTokensForTheSite()
    {
        var mine = await SeedToken("halebid-live", sdkSiteId: 610);
        await SeedToken("adhoc-dev", sdkSiteId: 610, appId: "eform");
        await SeedToken("time-dev", sdkSiteId: 610, appId: "time");
        await SeedToken("other-site", sdkSiteId: 611);
        var dead = await SeedToken("halebid-dead", sdkSiteId: 610);
        await dead.Delete(BackendConfigurationPnDbContext!);

        var devices = await TailBitePushSender.TargetTokenQuery(BackendConfigurationPnDbContext!.DeviceTokens, 610).ToListAsync();

        Assert.That(devices.Select(t => t.FcmToken), Is.EquivalentTo(new[] { mine.FcmToken }),
            "the halebid sender holds one project's credential: a device minted by "
            + "another app, belonging to another site, or already dead must never "
            + "be targeted");
    }

    /// <summary>
    /// The AppId predicate is not cosmetic. AppId is the LEADING column of
    /// IX_DeviceTokens_AppId_SdkSiteId_WorkflowState (declared in
    /// eform-backendconfiguration-base's BackendConfigurationPnDbContext) and
    /// the old site-only index was dropped with it, so a query that omits
    /// AppId has no usable index and table-scans DeviceTokens on every send.
    ///
    /// Asserting on the generated SQL is what makes a "harmless" removal of
    /// that clause fail here rather than in production - the rows the query
    /// returns would still be correct in any database holding only eform
    /// devices, which is every developer's.
    ///
    /// The assertion is scoped to the WHERE clause on purpose. AppId is a
    /// mapped column, so it appears in the SELECT projection of an unprojected
    /// IQueryable&lt;DeviceToken&gt; whether or not anything filters on it - a
    /// bare Does.Contain("AppId") over the whole statement passes with the
    /// predicate deleted, which is precisely the regression this exists for.
    /// </summary>
    [Test]
    public async Task TargetTokenQuery_FiltersOnAppId()
    {

        var sql = TailBitePushSender.TargetTokenQuery(BackendConfigurationPnDbContext!.DeviceTokens, 620).ToQueryString();
        var whereClause = sql[sql.IndexOf("WHERE", StringComparison.Ordinal)..];

        Assert.That(whereClause, Does.Contain("AppId"),
            "without an AppId predicate the send-path query cannot use "
            + $"the AppId-leading composite index and table-scans. SQL: {sql}");
    }

    // ---- systemic-fault guard ----------------------------------------------
    //
    // A permanent FCM rejection is not always about the device. A wrong
    // credential makes EVERY device return SENDER_ID_MISMATCH; a malformed
    // message payload makes EVERY device return INVALID_ARGUMENT. Pruning on
    // either would wipe the tenant's whole device set over a misconfiguration
    // that is recoverable when the devices are not, and would do it silently -
    // the devices are gone, the next send finds none, and push is simply dead.
    //
    // These drive the real send loop through the injected FCM call, so they
    // pin the counting at the call site as well as the decision it feeds. A
    // test of the decision alone passes while the loop hands it the wrong
    // denominator.

    [Test]
    public async Task Send_MixedResults_PrunesOnlyTheFailingToken()
    {
        var healthy = await SeedToken("mixed-healthy", sdkSiteId: 630);
        var mismatching = await SeedToken("mixed-mismatch", sdkSiteId: 630);

        await SendWith(630, new Dictionary<string, Exception>
        {
            [mismatching.FcmToken] = Fcm(MessagingErrorCode.SenderIdMismatch)
        });

        await AssertAllSurvive([healthy],
            "a device FCM delivered to must never be pruned");
        await AssertAllPruned([mismatching],
            "a permanent failure alongside a device that went through is a device "
            + "fault and must be pruned");
    }

    // n=1 is the boundary and not a separate rule: a lone device that
    // mismatches on its own send is indistinguishable from a credential fault,
    // so it is kept too.
    [TestCase(1)]
    [TestCase(2)]
    public async Task Send_EveryAnsweredTokenReturnedSenderIdMismatch_PrunesNothing(int deviceCount)
    {
        var site = 640 + deviceCount;
        var devices = await SeedTokens(site, deviceCount, $"cred-{deviceCount}");

        await SendWithAllFailing(site, devices, MessagingErrorCode.SenderIdMismatch);

        await AssertAllSurvive(devices,
            "a wholesale mismatch is a credential fault; the devices must survive it");
    }

    /// <summary>
    /// The gap the SenderIdMismatch guard left open. A malformed message
    /// payload is rejected identically for every device in the send, so an
    /// INVALID_ARGUMENT sweep is a fault in what this server sent, not N dead
    /// devices - and pruning on it destroys the device set of every site the
    /// bad payload is sent to.
    /// </summary>
    [TestCase(1)]
    [TestCase(2)]
    public async Task Send_EveryAnsweredTokenReturnedInvalidArgument_PrunesNothing(int deviceCount)
    {
        var site = 650 + deviceCount;
        var devices = await SeedTokens(site, deviceCount, $"payload-{deviceCount}");

        await SendWithAllFailing(site, devices, MessagingErrorCode.InvalidArgument);

        await AssertAllSurvive(devices,
            "every device rejected with the same permanent code is a systemic fault - "
            + "here a malformed payload - and must prune nothing");
    }

    /// <summary>
    /// UNREGISTERED is deliberately NOT covered by the guard, and this pins
    /// that judgement rather than inheriting it from symmetry.
    ///
    /// The systemic causes are server-side and each has its own code: a wrong
    /// credential is SENDER_ID_MISMATCH, a bad payload is INVALID_ARGUMENT, a
    /// bad APNs key is THIRD_PARTY_AUTH_ERROR. Nothing this server can
    /// misconfigure makes FCM answer UNREGISTERED for a live device - it means
    /// that registration is gone, and only that. Meanwhile most sites have one
    /// or two devices, so "every device unregistered" is the ORDINARY shape of
    /// an uninstall. Guarding it would block nearly every legitimate prune and
    /// leave dead rows accumulating forever, buying nothing back.
    /// </summary>
    [TestCase(1)]
    [TestCase(2)]
    public async Task Send_EveryAnsweredTokenReturnedUnregistered_PrunesThemAll(int deviceCount)
    {
        var site = 660 + deviceCount;
        var devices = await SeedTokens(site, deviceCount, $"gone-{deviceCount}");

        await SendWithAllFailing(site, devices, MessagingErrorCode.Unregistered);

        await AssertAllPruned(devices,
            "Unregistered is only ever about the registration; a site whose every "
            + "device uninstalled must still have its rows pruned");
    }

    /// <summary>
    /// Two different permanent codes cannot come from one systemic cause: a
    /// malformed payload would have failed both devices with INVALID_ARGUMENT.
    /// A mix is therefore per-device, and both are pruned.
    ///
    /// Both seeding orders, because the guard reads the FIRST failure's code to
    /// decide whether the code is even systemic-capable. Order the Unregistered
    /// device first and that check short-circuits, so the "all the same code"
    /// clause this exists to pin is never reached and the test passes with that
    /// clause deleted. TargetTokenQuery is unordered, so the order cannot be
    /// assumed either way - covering both is what makes the pin hold.
    /// </summary>
    [TestCase(MessagingErrorCode.InvalidArgument, MessagingErrorCode.Unregistered)]
    [TestCase(MessagingErrorCode.Unregistered, MessagingErrorCode.InvalidArgument)]
    public async Task Send_AnsweredTokensFailedWithDifferentPermanentCodes_PrunesThemAll(
        MessagingErrorCode firstSeeded, MessagingErrorCode secondSeeded)
    {
        var site = firstSeeded == MessagingErrorCode.InvalidArgument ? 671 : 672;
        var first = await SeedToken($"mixedcode-{site}-a", sdkSiteId: site);
        var second = await SeedToken($"mixedcode-{site}-b", sdkSiteId: site);

        await SendWith(site, new Dictionary<string, Exception>
        {
            [first.FcmToken] = Fcm(firstSeeded),
            [second.FcmToken] = Fcm(secondSeeded)
        });

        await AssertAllPruned([first, second],
            "differing permanent codes rule out a single systemic cause, so each "
            + "failure is about its own device");
    }

    /// <summary>
    /// The guard divides by the devices FCM ANSWERED for, not the devices
    /// targeted. A send that failed transiently returned no verdict about its
    /// device, so counting it makes a wholesale credential fault look partial -
    /// and one flaky socket is then enough to prune a live device that the guard
    /// exists to protect.
    /// </summary>
    [Test]
    public async Task Send_TokensFcmNeverAnsweredFor_DoNotDiluteTheGuard()
    {
        var mismatching = await SeedToken("diluted-mismatch", sdkSiteId: 680);
        var unanswered = await SeedToken("diluted-transient", sdkSiteId: 680);

        await SendWith(680, new Dictionary<string, Exception>
        {
            [mismatching.FcmToken] = Fcm(MessagingErrorCode.SenderIdMismatch),
            [unanswered.FcmToken] = new HttpRequestException("connection reset")
        });

        await AssertAllSurvive([mismatching, unanswered],
            "every device FCM answered for mismatched, so this is a credential fault "
            + "however many sends never reached FCM at all");
    }

    [Test]
    public async Task Send_WhenNoSendReachedFcm_PrunesNothing()
    {
        var device = await SeedToken("all-transient", sdkSiteId: 690);

        await SendWith(690, new Dictionary<string, Exception>
        {
            [device.FcmToken] = new HttpRequestException("connection reset")
        });

        await AssertAllSurvive([device],
            "a send that never got a verdict says nothing about the device");
    }

    // ---- fixture plumbing ----

    private TailBitePushSender CreateService() => new(BackendConfigurationPnDbContext!, NullLogger<TailBitePushSender>.Instance);

    /// <summary>
    /// Runs the real send loop against the site's real rows with the FCM call
    /// replaced: a device listed in <paramref name="failures"/> fails that way,
    /// anything else is delivered.
    /// </summary>
    private Task SendWith(int sdkSiteId, Dictionary<string, Exception> failures) =>
        CreateService().SendAndPruneAsync(sdkSiteId, deviceToken =>
            failures.TryGetValue(deviceToken.FcmToken, out var failure)
                ? Task.FromException(failure)
                : Task.CompletedTask);

    /// <summary>
    /// The shape of a systemic fault: every device of the site fails with the
    /// same <paramref name="code"/>.
    /// </summary>
    private Task SendWithAllFailing(
        int sdkSiteId, IReadOnlyList<DeviceToken> devices, MessagingErrorCode code) =>
        SendWith(sdkSiteId, devices.ToDictionary(t => t.FcmToken, _ => (Exception)Fcm(code)));

    /// <summary>
    /// A FirebaseMessagingException carrying the per-device verdict the send
    /// loop classifies on. Only MessagingErrorCode is read; the transport-level
    /// ErrorCode is incidental.
    ///
    /// Built by reflection because FirebaseAdmin 3.6.0 exposes no public
    /// constructor - the type is sealed with a single internal ctor, so the
    /// exception the production catch filters on cannot otherwise be produced
    /// outside the SDK. Single() rather than a lookup by signature: it fails
    /// loudly on a package bump that changes the shape, instead of silently
    /// picking a different overload.
    /// </summary>
    private static readonly ConstructorInfo FirebaseMessagingExceptionCtor =
        typeof(FirebaseMessagingException)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single();

    private static FirebaseMessagingException Fcm(MessagingErrorCode code) =>
        (FirebaseMessagingException)FirebaseMessagingExceptionCtor.Invoke(
            [ErrorCode.InvalidArgument, code.ToString(), (MessagingErrorCode?)code, null, null]);

    private async Task<List<DeviceToken>> SeedTokens(int sdkSiteId, int count, string prefix)
    {
        var devices = new List<DeviceToken>();
        for (var i = 0; i < count; i++)
        {
            devices.Add(await SeedToken($"{prefix}-{i}", sdkSiteId));
        }

        return devices;
    }

    private async Task AssertAllSurvive(IReadOnlyList<DeviceToken> devices, string because)
    {
        var states = await ReadWorkflowStates(devices);
        Assert.That(states, Is.All.EqualTo(Constants.WorkflowStates.Created), because);
    }

    private async Task AssertAllPruned(IReadOnlyList<DeviceToken> devices, string because)
    {
        var states = await ReadWorkflowStates(devices);
        Assert.That(states, Is.All.EqualTo(Constants.WorkflowStates.Removed), because);
    }

    private async Task<List<string>> ReadWorkflowStates(IReadOnlyList<DeviceToken> devices)
    {
        var states = new List<string>();
        foreach (var device in devices)
        {
            states.Add(await ReadWorkflowState(device.Id));
        }

        return states;
    }

    private async Task<DeviceToken> SeedToken(string device, int sdkSiteId, string appId = "halebid")
    {
        var deviceToken = new DeviceToken
        {
            AppId = appId,
            InstallationId = $"inst-{appId}-{device}",
            FcmToken = device,
            SdkSiteId = sdkSiteId,
            Platform = "android"
        };
        await deviceToken.Create(BackendConfigurationPnDbContext!);
        return deviceToken;
    }

    private async Task<string> ReadWorkflowState(int deviceTokenId) =>
        (await BackendConfigurationPnDbContext!.DeviceTokens.AsNoTracking()
            .SingleAsync(t => t.Id == deviceTokenId)).WorkflowState;
}
