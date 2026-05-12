namespace BackendConfiguration.Pn.Services.GoogleDrive;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Routes Drive change notifications to the file service that actually
/// re-downloads the bytes. Two callers:
///
/// <list type="bullet">
///   <item><description><see cref="Controllers.GoogleDriveController.Notify"/>
///     (PR-5) calls <see cref="ProcessUserAsync"/> when Drive fans out a
///     webhook for the user's channel — fire-and-forget so the webhook
///     responds within Drive's tight timeout budget.</description></item>
///   <item><description>The PR-6 reconcile cron (in
///     <c>eform-service-backendconfiguration-plugin</c>) calls
///     <see cref="ProcessUserAsync"/> as the daily safety net for missed
///     deliveries.</description></item>
/// </list>
///
/// The processor is idempotent — repeated runs converge because each file's
/// <c>DriveModifiedTime</c> short-circuits a no-op on the second hit. No
/// locking needed: each <see cref="ProcessFileAsync"/> call is independent.
/// </summary>
public interface IGoogleDriveChangeProcessor
{
    /// <summary>
    /// Process all Drive-sourced files for a user. Called by:
    /// PR-5 /notify (when a webhook fires for the user's channel),
    /// PR-6 reconcile cron (daily safety net for missed webhooks).
    /// </summary>
    Task<DriveChangeProcessingResult> ProcessUserAsync(int userId, CancellationToken ct = default);

    /// <summary>
    /// Process a single Drive file by id, on demand. Returns the
    /// outcome enum for callers that want to surface UI feedback.
    /// </summary>
    Task<DriveChangeOutcome> ProcessFileAsync(int areaRulePlanningFileId, CancellationToken ct = default);
}

/// <summary>
/// Per-file outcome from <see cref="IGoogleDriveChangeProcessor.ProcessFileAsync"/>.
/// Aggregated into <see cref="DriveChangeProcessingResult"/> by the user-level
/// entry point.
/// </summary>
public enum DriveChangeOutcome
{
    /// <summary>Drive's <c>modifiedTime</c> &lt;= cached value; no work done.</summary>
    NoChange,

    /// <summary>File content was re-fetched from Drive and the cached blob updated.</summary>
    Refreshed,

    /// <summary>Drive returned 404 — file deleted by user; row WorkflowState=Removed.</summary>
    DriveNotFound,

    /// <summary>Drive returned 403 — token can no longer access the file; row WorkflowState=Removed.</summary>
    PermissionDenied,

    /// <summary>Proxy /refresh returned invalid_grant; the entire token is dead.</summary>
    TokenRevoked,

    /// <summary>Other transient error — row left untouched, will be retried next reconcile pass.</summary>
    Error
}

/// <summary>
/// Tally of <see cref="DriveChangeOutcome"/>s from a user-level pass. Errors
/// and TokenRevoked are folded into <see cref="Errors"/> for the cron's
/// summary log; per-file callers get the precise enum back from
/// <see cref="IGoogleDriveChangeProcessor.ProcessFileAsync"/>.
/// </summary>
public sealed record DriveChangeProcessingResult(int Refreshed, int Removed, int NoChange, int Errors);
