using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.PushNotificationService;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// The flutter-eform push sender. Every invariant pinned here is one that
/// fails invisibly in production: a wrong Firebase app silently pushes through
/// another co-hosted plugin's project, a missing AppId predicate silently
/// table-scans DeviceTokens, and a wrong credential silently soft-deletes a
/// tenant's entire token set.
///
/// Deliberately NOT [Parallelizable]: this fixture creates and deletes an app
/// in the process-wide FirebaseApp registry, so a fixture running beside it
/// that touched the same registry would have its app deleted mid-test. NUnit
/// runs non-parallel work items in a shift of their own, which is what keeps
/// that from happening.
/// </summary>
[TestFixture]
public class PushNotificationServiceTests : TestBaseSetup
{
    /// <summary>
    /// Pinned as a literal, never read back from the production constant: this
    /// must fail on a rename, including one that re-points this plugin at a
    /// co-hosted sender's app. The name is a process-wide key - a wire value.
    /// </summary>
    private const string ExpectedFirebaseAppName = "microting-eform";

    private const string ServiceAccountKeyName =
        "BackendConfigurationSettings:EformFirebaseServiceAccountJson";

    /// <summary>
    /// FirebaseApp instances live in a process-wide registry that outlives the
    /// fixture, so every test starts and ends with this sender's app absent.
    /// Only the named app is deleted: DefaultInstance is shared with the whole
    /// host and nothing here ever creates it, so deleting it could only ever
    /// destroy someone else's.
    /// </summary>
    [SetUp]
    [TearDown]
    public void DeleteOwnFirebaseApp() =>
        FirebaseApp.GetInstance(ExpectedFirebaseAppName)?.Delete();

    // ---- disabled by absence ----------------------------------------------
    //
    // No credential is the normal state of every deployment that has not opted
    // in, so it must be a quiet no-op rather than a startup or request failure.

    [Test]
    public async Task Constructor_WithoutServiceAccountConfigured_DoesNotThrow()
    {
        await ClearServiceAccount();

        Assert.DoesNotThrow(() => CreateService());
    }

    [Test]
    public async Task SendToSiteAsync_WhenNotConfigured_IsNoOpAndKeepsTokens()
    {
        await ClearServiceAccount();
        var token = await SeedToken("disabled-noop", sdkSiteId: 600);

        await CreateService().SendToSiteAsync(600, "Title", "Body");

        Assert.That(await ReadWorkflowState(token.Id), Is.EqualTo(Constants.WorkflowStates.Created),
            "a disabled sender must not touch the tokens it never sent to");
    }

    // ---- recipient selection ----------------------------------------------

    [Test]
    public async Task TargetTokenQuery_SelectsOnlyLiveEformTokensForTheSite()
    {
        await ClearServiceAccount();
        var mine = await SeedToken("eform-live", sdkSiteId: 610);
        await SeedToken("adhoc-token", sdkSiteId: 610, appId: "adhoc");
        await SeedToken("time-token", sdkSiteId: 610, appId: "time");
        await SeedToken("other-site", sdkSiteId: 611);
        var dead = await SeedToken("eform-dead", sdkSiteId: 610);
        await dead.Delete(BackendConfigurationPnDbContext!);

        var tokens = await CreateService().TargetTokenQuery(610).ToListAsync();

        Assert.That(tokens.Select(t => t.FcmToken), Is.EquivalentTo(new[] { mine.FcmToken }),
            "the eform sender holds one project's credential: a token minted by "
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
    /// tokens, which is every developer's.
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
        await ClearServiceAccount();

        var sql = CreateService().TargetTokenQuery(620).ToQueryString();
        var whereClause = sql[sql.IndexOf("WHERE", StringComparison.Ordinal)..];

        Assert.That(whereClause, Does.Contain("AppId"),
            "without an AppId predicate the send-path query cannot use "
            + $"IX_DeviceTokens_AppId_SdkSiteId_WorkflowState and table-scans. SQL: {sql}");
    }

    // ---- systemic-fault guard ----------------------------------------------
    //
    // A permanent FCM rejection is not always about the token. A wrong
    // credential makes EVERY token return SENDER_ID_MISMATCH; a malformed
    // message payload makes EVERY token return INVALID_ARGUMENT. Pruning on
    // either would wipe the tenant's whole token set over a misconfiguration
    // that is recoverable when the tokens are not, and would do it silently -
    // the tokens are gone, the next send finds none, and push is simply dead.
    //
    // These drive the real send loop through the injected FCM call, so they
    // pin the counting at the call site as well as the decision it feeds. A
    // test of the decision alone passes while the loop hands it the wrong
    // denominator.

    [Test]
    public async Task Send_MixedResults_PrunesOnlyTheFailingToken()
    {
        await ClearServiceAccount();
        var healthy = await SeedToken("mixed-healthy", sdkSiteId: 630);
        var mismatching = await SeedToken("mixed-mismatch", sdkSiteId: 630);

        await SendWith(630, new Dictionary<string, Exception?>
        {
            [mismatching.FcmToken] = Fcm(MessagingErrorCode.SenderIdMismatch)
        });

        await AssertSurvival(
            "a permanent failure alongside a token that went through is a token "
            + "fault and must be pruned",
            (healthy, true),
            (mismatching, false));
    }

    // n=1 is the boundary and not a separate rule: a lone device that
    // mismatches on its own send is indistinguishable from a credential fault,
    // so it is kept too.
    [TestCase(1)]
    [TestCase(2)]
    public async Task Send_EveryAnsweredTokenReturnedSenderIdMismatch_PrunesNothing(int tokenCount)
    {
        await ClearServiceAccount();
        var site = 640 + tokenCount;
        var tokens = await SeedTokens(site, tokenCount, $"cred-{tokenCount}");

        await SendWith(site, tokens.ToDictionary(
            t => t.FcmToken, _ => (Exception?)Fcm(MessagingErrorCode.SenderIdMismatch)));

        await AssertAllSurvive(tokens,
            "a wholesale mismatch is a credential fault; the tokens must survive it");
    }

    /// <summary>
    /// The gap the SenderIdMismatch guard left open. A malformed message
    /// payload is rejected identically for every token in the send, so an
    /// INVALID_ARGUMENT sweep is a fault in what this server sent, not N dead
    /// devices - and pruning on it destroys the token set of every site the
    /// bad payload is sent to.
    /// </summary>
    [TestCase(1)]
    [TestCase(2)]
    public async Task Send_EveryAnsweredTokenReturnedInvalidArgument_PrunesNothing(int tokenCount)
    {
        await ClearServiceAccount();
        var site = 650 + tokenCount;
        var tokens = await SeedTokens(site, tokenCount, $"payload-{tokenCount}");

        await SendWith(site, tokens.ToDictionary(
            t => t.FcmToken, _ => (Exception?)Fcm(MessagingErrorCode.InvalidArgument)));

        await AssertAllSurvive(tokens,
            "every token rejected with the same permanent code is a systemic fault - "
            + "here a malformed payload - and must prune nothing");
    }

    /// <summary>
    /// UNREGISTERED is deliberately NOT covered by the guard, and this pins
    /// that judgement rather than inheriting it from symmetry.
    ///
    /// The systemic causes are server-side and each has its own code: a wrong
    /// credential is SENDER_ID_MISMATCH, a bad payload is INVALID_ARGUMENT, a
    /// bad APNs key is THIRD_PARTY_AUTH_ERROR. Nothing this server can
    /// misconfigure makes FCM answer UNREGISTERED for a live token - it means
    /// that registration is gone, and only that. Meanwhile most sites have one
    /// or two devices, so "every token unregistered" is the ORDINARY shape of
    /// an uninstall. Guarding it would block nearly every legitimate prune and
    /// leave dead rows accumulating forever, buying nothing back.
    /// </summary>
    [TestCase(1)]
    [TestCase(2)]
    public async Task Send_EveryAnsweredTokenReturnedUnregistered_PrunesThemAll(int tokenCount)
    {
        await ClearServiceAccount();
        var site = 660 + tokenCount;
        var tokens = await SeedTokens(site, tokenCount, $"gone-{tokenCount}");

        await SendWith(site, tokens.ToDictionary(
            t => t.FcmToken, _ => (Exception?)Fcm(MessagingErrorCode.Unregistered)));

        var states = await ReadWorkflowStates(tokens);
        Assert.That(states, Is.All.EqualTo(Constants.WorkflowStates.Removed),
            "Unregistered is only ever about the registration; a site whose every "
            + "device uninstalled must still have its rows pruned");
    }

    /// <summary>
    /// Two different permanent codes cannot come from one systemic cause: a
    /// malformed payload would have failed both tokens with INVALID_ARGUMENT.
    /// A mix is therefore per-token, and both are pruned.
    /// </summary>
    [Test]
    public async Task Send_AnsweredTokensFailedWithDifferentPermanentCodes_PrunesThemAll()
    {
        await ClearServiceAccount();
        var gone = await SeedToken("mixedcode-gone", sdkSiteId: 670);
        var malformed = await SeedToken("mixedcode-bad", sdkSiteId: 670);

        await SendWith(670, new Dictionary<string, Exception?>
        {
            [gone.FcmToken] = Fcm(MessagingErrorCode.Unregistered),
            [malformed.FcmToken] = Fcm(MessagingErrorCode.InvalidArgument)
        });

        var states = await ReadWorkflowStates([gone, malformed]);
        Assert.That(states, Is.All.EqualTo(Constants.WorkflowStates.Removed),
            "differing permanent codes rule out a single systemic cause, so each "
            + "failure is about its own token");
    }

    /// <summary>
    /// The guard divides by the tokens FCM ANSWERED for, not the tokens
    /// targeted. A send that failed transiently returned no verdict about its
    /// token, so counting it makes a wholesale credential fault look partial -
    /// and one flaky socket is then enough to prune a live token that the guard
    /// exists to protect.
    /// </summary>
    [Test]
    public async Task Send_TokensFcmNeverAnsweredFor_DoNotDiluteTheGuard()
    {
        await ClearServiceAccount();
        var mismatching = await SeedToken("diluted-mismatch", sdkSiteId: 680);
        var unanswered = await SeedToken("diluted-transient", sdkSiteId: 680);

        await SendWith(680, new Dictionary<string, Exception?>
        {
            [mismatching.FcmToken] = Fcm(MessagingErrorCode.SenderIdMismatch),
            [unanswered.FcmToken] = new HttpRequestException("connection reset")
        });

        await AssertAllSurvive([mismatching, unanswered],
            "every token FCM answered for mismatched, so this is a credential fault "
            + "however many sends never reached FCM at all");
    }

    [Test]
    public async Task Send_WhenNoSendReachedFcm_PrunesNothing()
    {
        await ClearServiceAccount();
        var token = await SeedToken("all-transient", sdkSiteId: 690);

        await SendWith(690, new Dictionary<string, Exception?>
        {
            [token.FcmToken] = new HttpRequestException("connection reset")
        });

        await AssertAllSurvive([token],
            "a send that never got a verdict says nothing about the token");
    }

    // ---- Firebase app ownership -------------------------------------------
    //
    // BackendConfiguration.Pn and TimePlanning.Pn are loaded into ONE
    // eFormAPI.Web process and hold DIFFERENT Firebase projects' credentials.
    // FirebaseApp.DefaultInstance is process-wide, so whichever plugin
    // initialised first would own it and every other sender would push through
    // that one project - returning SENDER_ID_MISMATCH on every token, which the
    // credential-fault guard above then correctly declines to act on. Nothing
    // ever surfaces. A named app is what rules that out.

    [Test]
    public async Task Initialisation_CreatesTheNamedApp_AndNeverTheProcessWideDefault()
    {
        await ConfigureServiceAccount();
        var logger = new RecordingLogger();

        _ = new PushNotificationService(BackendConfigurationPnDbContext!, logger);

        AssertOwnsNamedAppAndNotTheDefault();
        Assert.That(logger.Errors, Is.Empty, "initialisation must not have failed");
    }

    /// <summary>
    /// The loser of the concurrent-first-request race, made deterministic.
    /// FirebaseApp.Create throws a plain ArgumentException when the name is
    /// already taken (FirebaseAdmin 3.6.0 has no
    /// FirebaseAppAlreadyExistsException), and the constructor swallows
    /// initialisation failures into "push disabled" - so without the
    /// re-read-the-registry catch the second scoped request silently sends
    /// nothing.
    /// </summary>
    [Test]
    public async Task Initialisation_WhenTheNamedAppAlreadyExists_ReusesItAndKeepsPushEnabled()
    {
        await ConfigureServiceAccount();
        _ = new PushNotificationService(BackendConfigurationPnDbContext!, new RecordingLogger());
        var firstApp = FirebaseApp.GetInstance(ExpectedFirebaseAppName);

        var secondLogger = new RecordingLogger();
        _ = new PushNotificationService(BackendConfigurationPnDbContext!, secondLogger);

        AssertOwnsNamedAppAndNotTheDefault();
        Assert.Multiple(() =>
        {
            Assert.That(FirebaseApp.GetInstance(ExpectedFirebaseAppName), Is.SameAs(firstApp),
                "the second initialisation must reuse the app, not replace or duplicate it");
            Assert.That(secondLogger.Errors, Is.Empty,
                "a failed re-initialisation is swallowed and disables push for that "
                + "scoped request, which then silently sends nothing");
        });
    }

    [Test]
    public async Task Initialisation_WithMalformedServiceAccount_DisablesPushInsteadOfThrowing()
    {
        await SetServiceAccount("{ \"type\": \"not_a_service_account\" }");
        var logger = new RecordingLogger();

        Assert.DoesNotThrow(() =>
            _ = new PushNotificationService(BackendConfigurationPnDbContext!, logger));
        Assert.Multiple(() =>
        {
            Assert.That(logger.Errors, Is.Not.Empty,
                "a credential this sender cannot use must be reported, not ignored");
            Assert.That(FirebaseApp.GetInstance(ExpectedFirebaseAppName), Is.Null,
                "a bad credential must not leave a half-initialised app behind");
        });
    }

    private static void AssertOwnsNamedAppAndNotTheDefault()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FirebaseApp.GetInstance(ExpectedFirebaseAppName), Is.Not.Null,
                $"this sender must own a Firebase app named '{ExpectedFirebaseAppName}'");
            Assert.That(FirebaseApp.DefaultInstance, Is.Null,
                "FirebaseApp.DefaultInstance is shared with every other plugin in "
                + "eFormAPI.Web; claiming it cross-contaminates Firebase credentials");
        });
    }

    // ---- fixture plumbing --------------------------------------------------

    private PushNotificationService CreateService() =>
        new(BackendConfigurationPnDbContext!, new RecordingLogger());

    /// <summary>
    /// Captures error-level logs so a test can assert that initialisation did
    /// not silently fail, and that a bad credential did not pass unreported.
    /// </summary>
    private sealed class RecordingLogger : ILogger<PushNotificationService>
    {
        private readonly List<string> _errors = new();

        public IReadOnlyCollection<string> Errors => _errors;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error)
            {
                _errors.Add($"{formatter(state, exception)} :: {exception}");
            }
        }
    }

    /// <summary>
    /// A syntactically valid but entirely synthetic service-account key,
    /// generated per run rather than hard-coded so nothing in this file looks
    /// like a leaked credential. Creating a FirebaseApp only parses the
    /// credential, so it never leaves the process.
    /// </summary>
    private static readonly Lazy<string> SyntheticServiceAccountJson = new(() =>
    {
        using var rsa = RSA.Create(2048);
        return JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["type"] = "service_account",
            ["project_id"] = "microting-eform-test",
            ["private_key_id"] = "test-key-id",
            ["private_key"] = rsa.ExportPkcs8PrivateKeyPem(),
            ["client_email"] = "eform-test@microting-eform-test.iam.gserviceaccount.com",
            ["client_id"] = "1234567890",
            ["token_uri"] = "https://oauth2.googleapis.com/token"
        });
    });

    private Task ConfigureServiceAccount() => SetServiceAccount(SyntheticServiceAccountJson.Value);

    private Task ClearServiceAccount() => SetServiceAccount("");

    /// <summary>
    /// Upserts the configuration row. Its existence is not assumed: the
    /// integration SQL dump replays PluginConfigurationValues with only the
    /// keys it was captured with, so a newly seeded key is absent here.
    /// </summary>
    private async Task SetServiceAccount(string value)
    {
        var row = await BackendConfigurationPnDbContext!.PluginConfigurationValues
            .FirstOrDefaultAsync(x => x.Name == ServiceAccountKeyName);

        if (row == null)
        {
            BackendConfigurationPnDbContext.PluginConfigurationValues.Add(new PluginConfigurationValue
            {
                Name = ServiceAccountKeyName,
                Value = value,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Version = 1,
                WorkflowState = Constants.WorkflowStates.Created,
                CreatedByUserId = 1
            });
        }
        else
        {
            row.Value = value;
        }

        await BackendConfigurationPnDbContext.SaveChangesAsync();
    }

    /// <summary>
    /// Runs the real send loop against the site's real rows with the FCM call
    /// replaced: a token mapped to an exception fails that way, anything else
    /// is delivered.
    /// </summary>
    private Task SendWith(int sdkSiteId, Dictionary<string, Exception?> outcomes) =>
        CreateService().SendAndPruneAsync(sdkSiteId, deviceToken =>
            outcomes.TryGetValue(deviceToken.FcmToken, out var failure) && failure != null
                ? Task.FromException(failure)
                : Task.CompletedTask);

    /// <summary>
    /// A FirebaseMessagingException carrying the per-token verdict the send
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
        var tokens = new List<DeviceToken>();
        for (var i = 0; i < count; i++)
        {
            tokens.Add(await SeedToken($"{prefix}-{i}", sdkSiteId));
        }

        return tokens;
    }

    private async Task AssertAllSurvive(IReadOnlyList<DeviceToken> tokens, string because)
    {
        var states = await ReadWorkflowStates(tokens);
        Assert.That(states, Is.All.EqualTo(Constants.WorkflowStates.Created), because);
    }

    private async Task AssertSurvival(
        string because, params (DeviceToken Token, bool Survives)[] expectations)
    {
        var actual = new List<string>();
        foreach (var (token, _) in expectations)
        {
            actual.Add(await ReadWorkflowState(token.Id));
        }

        var expected = expectations
            .Select(e => e.Survives
                ? Constants.WorkflowStates.Created
                : Constants.WorkflowStates.Removed)
            .ToList();
        Assert.That(actual, Is.EqualTo(expected), because);
    }

    private async Task<List<string>> ReadWorkflowStates(IReadOnlyList<DeviceToken> tokens)
    {
        var states = new List<string>();
        foreach (var token in tokens)
        {
            states.Add(await ReadWorkflowState(token.Id));
        }

        return states;
    }

    private async Task<DeviceToken> SeedToken(string token, int sdkSiteId, string appId = "eform")
    {
        var deviceToken = new DeviceToken
        {
            AppId = appId,
            InstallationId = $"inst-{appId}-{token}",
            FcmToken = token,
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
