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
/// Sends FCM pushes to flutter-eform devices. Shaped after TimePlanning.Pn's
/// PushNotificationService, which it now sits beside in one eFormAPI.Web
/// process. It is not a copy: this one has no minBuild gate (DeviceToken here
/// carries no AppBuildNumber), stays silent rather than warning when push is
/// simply not configured, and defers every permanent per-token failure to one
/// prune decision taken over the whole send instead of acting at the catch
/// site - so a fault in this sender rather than in the devices is recognised
/// as such, keeps the tokens, and raises one Sentry event rather than one per
/// device.
/// </summary>
public class PushNotificationService : IPushNotificationService
{
    /// <summary>
    /// AppId of the tokens this sender owns. This service holds the credential
    /// for exactly one Firebase project, and a token minted by any other app
    /// returns SENDER_ID_MISMATCH - so foreign tokens are excluded at selection
    /// time rather than discovered at send time.
    ///
    /// The value originates on the client and is stored verbatim:
    /// <c>SettingsGrpcService.RegisterPushToken</c> only passes through
    /// whatever <c>app_id</c> the caller sent, so the counterpart constant
    /// lives in flutter-eform
    /// (packages/microting_mobile/lib/features/settings/data/settings_repository.dart).
    /// Renaming one without the other silently empties this query.
    /// </summary>
    private const string EformAppId = "eform";

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
    /// <see cref="PrunePermanentFailuresAsync"/> correctly reads as a
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
    /// snapshot would mean an operator's INSERT is not seen until the host is
    /// restarted.
    ///
    /// CAVEAT: that only makes TURNING PUSH ON restart-free. The FirebaseApp
    /// this credential builds is cached process-wide under
    /// <see cref="FirebaseAppName"/> and is never rebuilt, so ROTATING or
    /// REPOINTING the key has no effect until the host restarts - the app
    /// created from the first credential keeps being used. Say so to whoever
    /// you tell to fix a credential fault; see
    /// <see cref="PrunePermanentFailuresAsync"/>.
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
        _firebaseApp = ResolveFirebaseApp(dbContext, logger);
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
        BackendConfigurationPnDbContext dbContext,
        ILogger<PushNotificationService> logger)
    {
        try
        {
            // Inside the try on purpose. This is the only I/O the constructor
            // does, and a constructor that throws fails DI resolution in the
            // caller - i.e. it fails the very request that a push must never
            // be able to fail. A database blip here means push is off for this
            // request, nothing more.
            var serviceAccountJson = dbContext.PluginConfigurationValues
                .FirstOrDefault(x => x.Name == ServiceAccountConfigurationKey)?.Value;

            if (string.IsNullOrWhiteSpace(serviceAccountJson))
            {
                return null;
            }

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
    internal static Message BuildMessage(
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
#pragma warning disable CS0618
            Token = token,
#pragma warning restore CS0618
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
    /// The query selecting the live device tokens a push targets: this app's
    /// tokens, same site, still in the Created workflow state.
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
            // GetMessaging(app), never DefaultInstance: see FirebaseAppName.
            // Hoisted - it is the same client for every token, and each call
            // takes FirebaseAdmin's global lock to re-derive it.
            var messaging = FirebaseMessaging.GetMessaging(_firebaseApp);

            await SendAndPruneAsync(
                targetSdkSiteId,
                deviceToken => messaging.SendAsync(
                    BuildMessage(deviceToken.FcmToken, title, body, data)));
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
    /// Sends to every live token of the site through <paramref name="sendOne"/>,
    /// then applies the prune decision for the whole send.
    ///
    /// The FCM call is a parameter rather than a hard-coded
    /// <see cref="FirebaseMessaging.SendAsync(Message)"/> so that the part of
    /// this class that can silently soft-delete a tenant's entire token set -
    /// how per-token outcomes are counted and classified, and the systemic-fault
    /// arithmetic in <see cref="PrunePermanentFailuresAsync"/> that reads them -
    /// can be driven against real rows without a Firebase project. Testing the
    /// prune decision alone cannot catch the counting being wrong at this call
    /// site, which is exactly how a systemic fault reads as N dead devices.
    ///
    /// Unlike <see cref="SendToSiteAsync"/> this has no catch-all: it is called
    /// from inside that boundary, and a test must see a fault rather than a log
    /// line.
    /// </summary>
    internal async Task SendAndPruneAsync(int targetSdkSiteId, Func<DeviceToken, Task> sendOne)
    {
        var tokens = await TargetTokenQuery(targetSdkSiteId).ToListAsync();

        if (tokens.Count == 0)
        {
            _logger.LogInformation(
                "No eform device tokens found for SdkSiteId {SdkSiteId}", targetSdkSiteId);
            return;
        }

        var permanentFailures = new List<(DeviceToken Token, MessagingErrorCode Code)>();

        // Tokens FCM actually answered for - a delivery, or a permanent
        // rejection of that specific token. A send that failed transiently or
        // unrecognisably (a network blip, UNAVAILABLE, a quota) says nothing
        // about its token and must not count: see PrunePermanentFailuresAsync,
        // which divides by this.
        var respondedCount = 0;

        foreach (var deviceToken in tokens)
        {
            try
            {
                await sendOne(deviceToken);
                respondedCount++;
            }
            catch (FirebaseMessagingException fex)
                when (fex.MessagingErrorCode is MessagingErrorCode.SenderIdMismatch
                      or MessagingErrorCode.Unregistered
                      or MessagingErrorCode.InvalidArgument)
            {
                // Collected, not pruned here: the decision needs the whole
                // send's outcome. See PrunePermanentFailuresAsync.
                respondedCount++;
                permanentFailures.Add((deviceToken, fex.MessagingErrorCode!.Value));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to send push notification to token {TokenId} for SdkSiteId {SdkSiteId}",
                    deviceToken.Id, targetSdkSiteId);
            }
        }

        await PrunePermanentFailuresAsync(permanentFailures, respondedCount, targetSdkSiteId);
    }

    /// <summary>
    /// Permanent FCM codes that a fault OUTSIDE the token can produce for
    /// every token of a send at once, and which therefore must not be pruned
    /// on when they account for the whole send:
    ///
    /// - SENDER_ID_MISMATCH: this sender holds the wrong credential
    ///   (<see cref="ServiceAccountConfigurationKey"/> pointing at another
    ///   Firebase project), so every token was minted by "a different app".
    /// - INVALID_ARGUMENT: the message payload is malformed, so FCM rejects
    ///   the send before the token can matter.
    ///
    /// UNREGISTERED is deliberately excluded, and not for want of symmetry.
    /// Every systemic cause has a code of its own - a wrong credential is
    /// SENDER_ID_MISMATCH, a bad payload INVALID_ARGUMENT, a bad APNs key
    /// THIRD_PARTY_AUTH_ERROR - and nothing this server can misconfigure makes
    /// FCM answer UNREGISTERED for a live token: it means that registration is
    /// gone, and only that. Most sites hold one or two tokens, so "every token
    /// unregistered" is the ORDINARY shape of an uninstall; guarding it would
    /// block nearly every legitimate prune and leave dead rows accumulating
    /// forever, in exchange for protection against a cause that does not exist.
    /// </summary>
    private static bool CanBeSystemic(MessagingErrorCode code) =>
        code is MessagingErrorCode.SenderIdMismatch or MessagingErrorCode.InvalidArgument;

    /// <summary>
    /// Applies the prune decision for the tokens of one send that FCM
    /// permanently rejected.
    ///
    /// A permanent rejection is not always about the token. When EVERY token
    /// FCM answered for failed with the SAME code, and that code is one a
    /// server-side fault can produce (see <see cref="CanBeSystemic"/>), the
    /// fault is this sender's, not the devices': a naive prune would silently
    /// soft-delete the tenant's entire token set over a misconfiguration that
    /// is recoverable when the tokens are not, and the next send would then
    /// find no tokens at all. Such a send prunes nothing and alerts instead.
    /// Anything else - a failure alongside tokens that went through, or a mix
    /// of codes that rules out one common cause - is per-token and is pruned.
    ///
    /// <paramref name="respondedCount"/> is the tokens FCM ANSWERED for, not
    /// the tokens targeted. A send that failed transiently returned no verdict
    /// about its token, and counting it would make a wholesale systemic fault
    /// read as partial - one flaky socket then prunes a live token.
    /// </summary>
    internal async Task PrunePermanentFailuresAsync(
        IReadOnlyList<(DeviceToken Token, MessagingErrorCode Code)> permanentFailures,
        int respondedCount,
        int targetSdkSiteId)
    {
        if (permanentFailures.Count == 0)
        {
            return;
        }

        var firstCode = permanentFailures[0].Code;
        if (permanentFailures.Count == respondedCount
            && CanBeSystemic(firstCode)
            && permanentFailures.All(f => f.Code == firstCode))
        {
            _logger.LogWarning(
                "All {Count} eform device tokens FCM answered for on SdkSiteId {SdkSiteId} "
                + "failed with {Error}. That is a fault in this sender, not in the tokens "
                + "- keeping them. SenderIdMismatch means {ConfigurationKey} holds the "
                + "wrong Firebase project's credential; fix it and then RESTART the host, "
                + "because the Firebase app is cached process-wide and is not rebuilt when "
                + "the credential changes. InvalidArgument means the message payload this "
                + "server built is malformed",
                respondedCount, targetSdkSiteId, firstCode, ServiceAccountConfigurationKey);
            SentrySdk.CaptureMessage(
                $"All {respondedCount} eform device tokens answered for on SdkSiteId "
                + $"{targetSdkSiteId} failed with {firstCode} - treated as a fault in this "
                + "sender, no tokens pruned. Check "
                + $"{ServiceAccountConfigurationKey} (then restart the host; the Firebase "
                + "app is cached process-wide) or the message payload",
                SentryLevel.Warning);
            return;
        }

        foreach (var (deviceToken, code) in permanentFailures)
        {
            if (code == MessagingErrorCode.SenderIdMismatch)
            {
                _logger.LogWarning(
                    "Removing foreign device token {TokenId} for SdkSiteId {SdkSiteId}: "
                    + "SenderIdMismatch (minted by a different Firebase project)",
                    deviceToken.Id, targetSdkSiteId);
                SentrySdk.CaptureMessage(
                    $"SenderIdMismatch for DeviceToken {deviceToken.Id} (SdkSiteId {targetSdkSiteId})",
                    SentryLevel.Warning);
            }
            else
            {
                // The token is permanently dead - the app was uninstalled, or
                // FCM rejects the token itself as malformed. Retrying it would
                // fail identically forever. Routine, so no Sentry event.
                _logger.LogInformation(
                    "Removing stale device token {TokenId} for SdkSiteId {SdkSiteId}: {Error}",
                    deviceToken.Id, targetSdkSiteId, code);
            }

            await deviceToken.Delete(_dbContext);
        }
    }
}
