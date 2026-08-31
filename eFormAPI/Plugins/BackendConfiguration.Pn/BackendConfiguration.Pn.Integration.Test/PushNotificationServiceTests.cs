using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.PushNotificationService;
using FirebaseAdmin;
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

    // ---- credential-fault guard -------------------------------------------
    //
    // SENDER_ID_MISMATCH has two causes and only one is about tokens. A single
    // mismatch among healthy sends is a foreign token and is pruned. EVERY
    // targeted token mismatching instead means this sender holds the wrong
    // credential, where pruning would wipe the tenant's whole token set over a
    // misconfiguration that is recoverable and the tokens are not.

    [Test]
    public async Task PruneSenderIdMismatches_MixedResults_PrunesOnlyTheMismatchingToken()
    {
        await ClearServiceAccount();
        var healthy = await SeedToken("healthy", sdkSiteId: 630);
        var mismatching = await SeedToken("mismatching", sdkSiteId: 630);

        await CreateService().PruneSenderIdMismatchesAsync(
            [mismatching], targetedCount: 2, targetSdkSiteId: 630);

        var healthyState = await ReadWorkflowState(healthy.Id);
        var mismatchingState = await ReadWorkflowState(mismatching.Id);
        Assert.Multiple(() =>
        {
            Assert.That(healthyState, Is.EqualTo(Constants.WorkflowStates.Created));
            Assert.That(mismatchingState, Is.EqualTo(Constants.WorkflowStates.Removed),
                "a mismatch alongside a token that went through is a foreign token "
                + "and must be pruned");
        });
    }

    // n=1 is the boundary and not a separate rule: a lone device that
    // mismatches on its own send is indistinguishable from a credential fault,
    // so it is kept too.
    [TestCase(1)]
    [TestCase(2)]
    public async Task PruneSenderIdMismatches_EveryTargetedTokenMismatched_PrunesNothing(
        int tokenCount)
    {
        await ClearServiceAccount();
        var site = 640 + tokenCount;
        var tokens = new List<DeviceToken>();
        for (var i = 0; i < tokenCount; i++)
        {
            tokens.Add(await SeedToken($"cred-{tokenCount}-{i}", sdkSiteId: site));
        }

        await CreateService().PruneSenderIdMismatchesAsync(
            tokens, targetedCount: tokenCount, targetSdkSiteId: site);

        var states = new List<string>();
        foreach (var token in tokens)
        {
            states.Add(await ReadWorkflowState(token.Id));
        }

        Assert.That(states, Is.All.EqualTo(Constants.WorkflowStates.Created),
            "a wholesale mismatch is a credential fault; the tokens must survive it");
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
