#nullable enable
namespace BackendConfiguration.Pn.Services.PushNotificationService;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Sentry;

/// <summary>
/// Sends FCM pushes to flutter-eform devices. Mirrors
/// TimePlanning.Pn's PushNotificationService; the two differ only in which
/// app's tokens they own and which Firebase project they hold.
/// </summary>
public class PushNotificationService : IPushNotificationService
{
    /// <summary>
    /// AppId of the tokens this sender owns, as registered by
    /// <c>SettingsGrpcService.RegisterPushToken</c>. It holds the credential
    /// for one Firebase project; a token minted by any other app returns
    /// SENDER_ID_MISMATCH, so those are filtered out at selection time rather
    /// than discovered at send time.
    /// </summary>
    public const string EformAppId = "eform";

    /// <summary>
    /// Name of the FirebaseApp this sender owns, namespaced vendor-then-sender
    /// to match "microting-time" in eform-angular-timeplanning-plugin and
    /// "microting-adhoc" in eform-service-backendconfiguration-plugin.
    ///
    /// It MUST be named. FirebaseApp.DefaultInstance is process-wide, and
    /// BackendConfiguration.Pn is only one of several plugins loaded into a
    /// single eFormAPI.Web host process - TimePlanning.Pn has a sender of its
    /// own. Each holds the credential for a DIFFERENT Firebase project, so
    /// whichever plugin initialised first would own the default instance and
    /// every other sender would silently push through that first project.
    /// Every token then comes back SENDER_ID_MISMATCH, which
    /// <see cref="PruneSenderIdMismatchesAsync"/> correctly reads as a
    /// credential fault and leaves alone - so the send is retried forever and
    /// never surfaces as an error. A named app keeps the credentials
    /// per-plugin, which is what rules that failure out.
    /// </summary>
    private const string FirebaseAppName = "microting-eform";

    /// <summary>
    /// Read straight off <see cref="BackendConfigurationPnDbContext"/> rather
    /// than through IDbOptions/PluginConfigurationProvider, matching
    /// TimePlanning's sender and the adhoc reminder job. The value is written
    /// out of band by a fleet script; routing it through the bound options
    /// snapshot would mean an operator's UPDATE is not seen until the host is
    /// restarted.
    /// </summary>
    private const string ServiceAccountConfigurationKey =
        "BackendConfigurationSettings:EformFirebaseServiceAccountJson";

    // Serialises the create-if-absent in EnsureFirebaseApp; see there for why
    // that call must happen at most once.
    private static readonly object FirebaseInitLock = new();

    private readonly BackendConfigurationPnDbContext _dbContext;
    private readonly ILogger<PushNotificationService> _logger;
    private readonly FirebaseApp? _firebaseApp;

    public PushNotificationService(
        BackendConfigurationPnDbContext dbContext,
        ILogger<PushNotificationService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;

        var serviceAccountJson = _dbContext.PluginConfigurationValues
            .FirstOrDefault(x => x.Name == ServiceAccountConfigurationKey)?.Value;

        _firebaseApp = ResolveFirebaseApp(serviceAccountJson, logger);
    }

    /// <summary>
    /// The app this sender pushes through, or null when push is off.
    /// </summary>
    /// <remarks>
    /// An absent or empty credential is the normal state of every deployment
    /// that has not opted into push, so it is not an error and is not logged
    /// here - this service is transient, and a line per request would be pure
    /// noise. <see cref="SendToSiteAsync"/> reports the skip instead, once per
    /// send. A credential that is present but unusable IS an error and is
    /// logged as one, because someone configured it and it does not work.
    /// </remarks>
    private static FirebaseApp? ResolveFirebaseApp(
        string? serviceAccountJson,
        ILogger<PushNotificationService> logger)
    {
        if (string.IsNullOrWhiteSpace(serviceAccountJson))
        {
            return null;
        }

        try
        {
            var app = EnsureFirebaseApp(serviceAccountJson);
            logger.LogInformation(
                "Firebase push notifications initialized on app {FirebaseAppName}",
                FirebaseAppName);
            return app;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to initialize Firebase Admin SDK from {ConfigurationKey}",
                ServiceAccountConfigurationKey);
            return null;
        }
    }

    /// <summary>
    /// Returns this plugin's named FirebaseApp, creating it on first use.
    ///
    /// Double-checked over <see cref="FirebaseApp.GetInstance(string)"/>,
    /// which returns null when the app is absent - unlike
    /// <see cref="FirebaseApp.Create(AppOptions, string)"/>, which throws
    /// ArgumentException ("FirebaseApp named ... already exists") when the name
    /// is taken. Re-checking INSIDE the lock is what makes concurrent first
    /// requests idempotent instead of turning the loser into a swallowed
    /// exception that disables push for that request.
    ///
    /// The lock is private to this assembly, so it cannot serialise a creator
    /// outside it (a second load context, another in-process sender). The
    /// catch makes the postcondition hold regardless of who won: when this
    /// returns, an app of this name exists.
    /// </summary>
    private static FirebaseApp EnsureFirebaseApp(string serviceAccountJson)
    {
        var app = FirebaseApp.GetInstance(FirebaseAppName);
        if (app != null)
        {
            return app;
        }

        lock (FirebaseInitLock)
        {
            var existing = FirebaseApp.GetInstance(FirebaseAppName);
            if (existing != null)
            {
                return existing;
            }

            try
            {
                return FirebaseApp.Create(
                    new AppOptions
                    {
                        // CredentialFactory, not the obsolete
                        // GoogleCredential.FromJson: pinning the generic to
                        // ServiceAccountCredential also fails fast into the
                        // caller's catch when the configured JSON is not a
                        // service-account key.
                        Credential = CredentialFactory
                            .FromJson<ServiceAccountCredential>(serviceAccountJson)
                            .ToGoogleCredential()
                    },
                    FirebaseAppName);
            }
            catch (ArgumentException)
            {
                // Someone outside this lock created it first. That is the
                // postcondition, not a failure - but only if the app is
                // actually there; otherwise the ArgumentException came from
                // somewhere else and must not be swallowed.
                var raced = FirebaseApp.GetInstance(FirebaseAppName);
                if (raced == null)
                {
                    throw;
                }

                return raced;
            }
        }
    }

    /// <summary>
    /// Builds an FCM message. When both <paramref name="title"/> and
    /// <paramref name="body"/> are empty the message is data-only (silent): no
    /// visible <see cref="Notification"/> block is attached and APNs
    /// content-available is set so iOS wakes the app in the background to
    /// process the data payload. Otherwise a normal visible notification is
    /// attached alongside the data.
    /// </summary>
    public static Message BuildMessage(
        string token,
        string title,
        string body,
        Dictionary<string, string>? data)
    {
        var hasNotification = !string.IsNullOrEmpty(title) || !string.IsNullOrEmpty(body);
        var message = new Message
        {
            // CS0618: FirebaseAdmin 3.6.0 marks Token deprecated "use Fid
            // instead". Do NOT take that advice here. Fid addresses a Firebase
            // Installation ID; what DeviceToken.FcmToken holds - and what
            // SettingsGrpcService.RegisterPushToken receives from the client -
            // is an FCM registration token. Putting one in the other's field
            // makes every send fail. TimePlanning.Pn's sender ships the same
            // property against the same package version.
            Token = token,
            Data = data
        };

        if (hasNotification)
        {
            message.Notification = new Notification
            {
                Title = title,
                Body = body
            };
        }
        else
        {
            message.Apns = new ApnsConfig
            {
                Aps = new Aps { ContentAvailable = true }
            };
        }

        return message;
    }

    /// <summary>
    /// The live device tokens targeted by a push: this app's tokens, same
    /// site, still in the Created workflow state.
    ///
    /// INVARIANT: this query must always carry an equality predicate on AppId.
    /// AppId is the leading column of
    /// IX_DeviceTokens_AppId_SdkSiteId_WorkflowState and the old site-only
    /// index was dropped with it, so a query without an AppId predicate has no
    /// usable index and table-scans. That index is defined in
    /// eform-backendconfiguration-base
    /// (BackendConfigurationPnDbContext.OnModelCreating); re-check it there
    /// whenever the base package is bumped.
    /// </summary>
    internal IQueryable<DeviceToken> TargetTokenQuery(int targetSdkSiteId) =>
        _dbContext.DeviceTokens
            .Where(dt => dt.AppId == EformAppId
                         && dt.SdkSiteId == targetSdkSiteId
                         && dt.WorkflowState == Constants.WorkflowStates.Created);

    internal Task<List<DeviceToken>> ResolveTargetTokensAsync(int targetSdkSiteId) =>
        TargetTokenQuery(targetSdkSiteId).ToListAsync();

    public async Task SendToSiteAsync(
        int targetSdkSiteId,
        string title,
        string body,
        Dictionary<string, string>? data = null)
    {
        if (_firebaseApp == null)
        {
            _logger.LogInformation(
                "Push notification skipped (Firebase push disabled, {ConfigurationKey} not set): "
                + "SdkSiteId={SdkSiteId}, Title={Title}",
                ServiceAccountConfigurationKey, targetSdkSiteId, title);
            return;
        }

        try
        {
            var tokens = await ResolveTargetTokensAsync(targetSdkSiteId);

            if (tokens.Count == 0)
            {
                _logger.LogInformation(
                    "No eform device tokens found for SdkSiteId {SdkSiteId}", targetSdkSiteId);
                return;
            }

            var senderIdMismatches = new List<DeviceToken>();

            // GetMessaging(app), never DefaultInstance: see FirebaseAppName.
            // Hoisted - it is the same client for every token, and each call
            // takes FirebaseAdmin's global lock to re-derive it.
            var messaging = FirebaseMessaging.GetMessaging(_firebaseApp);

            foreach (var deviceToken in tokens)
            {
                try
                {
                    await messaging.SendAsync(BuildMessage(deviceToken.FcmToken, title, body, data));
                }
                catch (FirebaseMessagingException fex)
                    when (fex.MessagingErrorCode == MessagingErrorCode.SenderIdMismatch)
                {
                    // Collected, not pruned here: the decision needs the whole
                    // send's outcome. See PruneSenderIdMismatchesAsync.
                    senderIdMismatches.Add(deviceToken);
                }
                catch (FirebaseMessagingException fex)
                    when (fex.MessagingErrorCode is MessagingErrorCode.Unregistered
                          or MessagingErrorCode.InvalidArgument)
                {
                    // The token is permanently dead - the app was uninstalled,
                    // or FCM rejects the token as malformed. Retrying it would
                    // fail identically forever.
                    _logger.LogInformation(
                        "Removing stale device token {TokenId} for SdkSiteId {SdkSiteId}: {Error}",
                        deviceToken.Id, targetSdkSiteId, fex.MessagingErrorCode);
                    await deviceToken.Delete(_dbContext);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Failed to send push notification to token {TokenId} for SdkSiteId {SdkSiteId}",
                        deviceToken.Id, targetSdkSiteId);
                }
            }

            await PruneSenderIdMismatchesAsync(senderIdMismatches, tokens.Count, targetSdkSiteId);
        }
        catch (Exception ex)
        {
            // A push is a courtesy on top of the request that triggered it;
            // this method is the boundary that keeps a Firebase or database
            // fault from failing that request.
            _logger.LogError(ex,
                "Error sending push notifications to SdkSiteId {SdkSiteId}", targetSdkSiteId);
        }
    }

    /// <summary>
    /// Applies the prune decision for the tokens of one send that failed with
    /// SENDER_ID_MISMATCH.
    ///
    /// That error has two causes. Either the token was minted by a different
    /// app's Firebase project - a token fault, and pruning it is right - or
    /// this sender is holding the wrong credential
    /// (<see cref="ServiceAccountConfigurationKey"/> pointing at the wrong
    /// project), in which case EVERY token mismatches and a naive prune
    /// silently soft-deletes the tenant's entire token set. The two are
    /// indistinguishable per token, but not per send: a mismatch alongside
    /// tokens that went through is a token fault, while a wholesale mismatch
    /// is a credential fault and is left alone for an operator to fix.
    /// </summary>
    internal async Task PruneSenderIdMismatchesAsync(
        IReadOnlyList<DeviceToken> senderIdMismatches, int targetedCount, int targetSdkSiteId)
    {
        if (senderIdMismatches.Count == 0)
        {
            return;
        }

        if (senderIdMismatches.Count == targetedCount)
        {
            _logger.LogWarning(
                "All {Count} eform device tokens for SdkSiteId {SdkSiteId} returned "
                + "SenderIdMismatch. This is a Firebase credential fault, not a token "
                + "fault - keeping the tokens. Check {ConfigurationKey}",
                targetedCount, targetSdkSiteId, ServiceAccountConfigurationKey);
            SentrySdk.CaptureMessage(
                $"All {targetedCount} eform device tokens for SdkSiteId {targetSdkSiteId} "
                + $"returned SenderIdMismatch - check {ServiceAccountConfigurationKey}",
                SentryLevel.Warning);
            return;
        }

        foreach (var deviceToken in senderIdMismatches)
        {
            _logger.LogWarning(
                "Removing foreign device token {TokenId} for SdkSiteId {SdkSiteId}: "
                + "SenderIdMismatch (minted by a different Firebase project)",
                deviceToken.Id, targetSdkSiteId);
            SentrySdk.CaptureMessage(
                $"SenderIdMismatch for DeviceToken {deviceToken.Id} (SdkSiteId {targetSdkSiteId})",
                SentryLevel.Warning);
            await deviceToken.Delete(_dbContext);
        }
    }
}
