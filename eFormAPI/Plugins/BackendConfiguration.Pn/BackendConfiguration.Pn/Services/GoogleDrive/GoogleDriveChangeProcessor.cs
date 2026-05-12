namespace BackendConfiguration.Pn.Services.GoogleDrive;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;

/// <summary>
/// PR-7 implementation of <see cref="IGoogleDriveChangeProcessor"/>.
///
/// Single per-file pipeline (<see cref="ProcessFileAsync"/>):
/// <list type="number">
///   <item><description>Load <c>AreaRulePlanningFile</c> row eager-including
///     the owning <c>GoogleOAuthToken</c>. Bail out (NoChange) when the row
///     is already soft-deleted or has no Drive metadata — defends against
///     races where the cron picks up a row the disconnect flow has just
///     removed.</description></item>
///   <item><description>Get an access token via the auth service. Catches
///     <see cref="GoogleDriveTokenRevokedException"/> here — the auth service
///     has already stamped <c>RevokedAt</c>, we just propagate the outcome
///     enum.</description></item>
///   <item><description>Probe Drive metadata (<c>files.get?fields=...</c>).
///     404/403 are mapped to <see cref="DriveChangeOutcome.DriveNotFound"/> /
///     <see cref="DriveChangeOutcome.PermissionDenied"/> and the row is
///     soft-deleted (<c>WorkflowState=Removed</c>). 5xx returns
///     <see cref="DriveChangeOutcome.Error"/> so the next reconcile pass
///     retries.</description></item>
///   <item><description>If <c>modifiedTime &lt;= cached</c>, return
///     <see cref="DriveChangeOutcome.NoChange"/>. Idempotency hinge: webhook
///     storms collapse here.</description></item>
///   <item><description>Otherwise delegate to
///     <see cref="IGoogleDriveFileService.RefreshFileAsync"/> for the actual
///     bytes + S3 upload. The file service handles MIME / size validation
///     and the <c>UploadedData</c> rewrite; we just translate its bool to
///     <see cref="DriveChangeOutcome.Refreshed"/>.</description></item>
/// </list>
///
/// User-level entry point (<see cref="ProcessUserAsync"/>) just iterates and
/// tallies.
/// </summary>
public class GoogleDriveChangeProcessor : IGoogleDriveChangeProcessor
{
    private readonly BackendConfigurationPnDbContext _dbContext;
    private readonly IGoogleDriveAuthService _authService;
    private readonly IGoogleDriveFileService _fileService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GoogleDriveChangeProcessor> _logger;

    public GoogleDriveChangeProcessor(
        BackendConfigurationPnDbContext dbContext,
        IGoogleDriveAuthService authService,
        IGoogleDriveFileService fileService,
        IHttpClientFactory httpClientFactory,
        ILogger<GoogleDriveChangeProcessor> logger)
    {
        _dbContext = dbContext;
        _authService = authService;
        _fileService = fileService;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<DriveChangeProcessingResult> ProcessUserAsync(int userId, CancellationToken ct = default)
    {
        // Resolve the token id for the user. We scope the per-file query
        // to that token id rather than just "any file owned by userId" so
        // a user who reconnects under a new token after a stale row is
        // left over doesn't see the stale row picked up here.
        var tokenId = await _dbContext.GoogleOAuthTokens
            .Where(x => x.UserId == userId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .Where(x => x.RevokedAt == null)
            .OrderByDescending(x => x.Id)
            .Select(x => (int?)x.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (tokenId == null)
        {
            // No active token — nothing to reconcile. Treat as a clean pass.
            return new DriveChangeProcessingResult(0, 0, 0, 0);
        }

        var fileIds = await _dbContext.AreaRulePlanningFiles
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .Where(x => x.DriveFileId != null)
            .Where(x => x.GoogleOAuthTokenId == tokenId)
            .Select(x => x.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        int refreshed = 0, removed = 0, noChange = 0, errors = 0;
        foreach (var id in fileIds)
        {
            ct.ThrowIfCancellationRequested();
            var outcome = await ProcessFileAsync(id, ct).ConfigureAwait(false);
            switch (outcome)
            {
                case DriveChangeOutcome.Refreshed:
                    refreshed++;
                    break;
                case DriveChangeOutcome.DriveNotFound:
                case DriveChangeOutcome.PermissionDenied:
                    removed++;
                    break;
                case DriveChangeOutcome.NoChange:
                    noChange++;
                    break;
                case DriveChangeOutcome.TokenRevoked:
                    // The whole token is gone — no point continuing the
                    // loop; every remaining file would hit the same
                    // revoked-token path. Tally the current file PLUS the
                    // unprocessed remainder as errors so the caller's
                    // summary reflects "we couldn't process N files".
                    var remaining = fileIds.Count - (refreshed + removed + noChange + errors) - 1;
                    errors += 1 + Math.Max(0, remaining);
                    _logger.LogInformation(
                        "ProcessUserAsync({UserId}) aborted — token revoked mid-pass", userId);
                    return new DriveChangeProcessingResult(refreshed, removed, noChange, errors);
                case DriveChangeOutcome.Error:
                    errors++;
                    break;
            }
        }

        return new DriveChangeProcessingResult(refreshed, removed, noChange, errors);
    }

    public async Task<DriveChangeOutcome> ProcessFileAsync(int areaRulePlanningFileId, CancellationToken ct = default)
    {
        var file = await _dbContext.AreaRulePlanningFiles
            .Include(x => x.GoogleOAuthToken)
            .FirstOrDefaultAsync(x => x.Id == areaRulePlanningFileId, ct)
            .ConfigureAwait(false);

        if (file == null
            || file.WorkflowState == Constants.WorkflowStates.Removed
            || string.IsNullOrEmpty(file.DriveFileId)
            || file.GoogleOAuthTokenId == null
            || file.GoogleOAuthToken == null
            || file.GoogleOAuthToken.WorkflowState == Constants.WorkflowStates.Removed
            || file.GoogleOAuthToken.RevokedAt != null)
        {
            return DriveChangeOutcome.NoChange;
        }

        var token = file.GoogleOAuthToken;

        string accessToken;
        try
        {
            accessToken = await _authService.GetAccessTokenAsync(token.UserId).ConfigureAwait(false);
        }
        catch (GoogleDriveTokenRevokedException)
        {
            // Auth service has already stamped RevokedAt — surface the
            // outcome to callers without crashing the loop.
            return DriveChangeOutcome.TokenRevoked;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "ProcessFileAsync({FileId}) failed to obtain access token for user {UserId}",
                areaRulePlanningFileId, token.UserId);
            return DriveChangeOutcome.Error;
        }

        using var http = _httpClientFactory.CreateClient(nameof(GoogleDriveFileService));
        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

        GoogleDriveFileService.DriveMetadataResult? metadata;
        try
        {
            metadata = await GoogleDriveFileService
                .ProbeDriveMetadataAsync(http, file.DriveFileId!)
                .ConfigureAwait(false);
        }
        catch (DriveFileNotFoundException)
        {
            _logger.LogInformation(
                "Drive file {DriveFileId} (row {FileId}) returned 404 — soft-deleting AreaRulePlanningFile",
                file.DriveFileId, file.Id);
            file.WorkflowState = Constants.WorkflowStates.Removed;
            await file.Update(_dbContext).ConfigureAwait(false);
            return DriveChangeOutcome.DriveNotFound;
        }
        catch (DriveFilePermissionDeniedException)
        {
            _logger.LogWarning(
                "Drive file {DriveFileId} (row {FileId}) returned 403 — soft-deleting AreaRulePlanningFile",
                file.DriveFileId, file.Id);
            file.WorkflowState = Constants.WorkflowStates.Removed;
            await file.Update(_dbContext).ConfigureAwait(false);
            return DriveChangeOutcome.PermissionDenied;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "ProbeDriveMetadataAsync({DriveFileId}) threw — treating as transient error",
                file.DriveFileId);
            return DriveChangeOutcome.Error;
        }

        if (metadata == null)
        {
            // 5xx / unexpected response code — bail without mutating the
            // row. The next reconcile pass retries.
            _logger.LogWarning(
                "Drive metadata fetch returned non-success for {DriveFileId} (row {FileId})",
                file.DriveFileId, file.Id);
            return DriveChangeOutcome.Error;
        }

        if (metadata.ModifiedTime.HasValue
            && file.DriveModifiedTime.HasValue
            && metadata.ModifiedTime.Value <= file.DriveModifiedTime.Value)
        {
            return DriveChangeOutcome.NoChange;
        }

        // Capture the old modifiedTime BEFORE the refresh so the audit
        // line below can show the transition (RefreshFileAsync mutates
        // the row in place).
        var oldModifiedTime = file.DriveModifiedTime;
        var oldSizeBytes = file.SizeBytes;

        // Hand off to the file service for the actual byte refresh + S3
        // upload + UploadedData rewrite. Translate its bool back into the
        // outcome enum. RefreshFileAsync also re-checks modifiedTime, so a
        // race where the metadata moves back between our probe and its own
        // probe converges to the same "no change" answer.
        bool changed;
        try
        {
            changed = await _fileService.RefreshFileAsync(file).ConfigureAwait(false);
        }
        catch (DriveFileNotFoundException)
        {
            // Race window: file vanished between our probe and the inner
            // refresh. Same handling as the up-front 404.
            file.WorkflowState = Constants.WorkflowStates.Removed;
            await file.Update(_dbContext).ConfigureAwait(false);
            return DriveChangeOutcome.DriveNotFound;
        }
        catch (DriveFilePermissionDeniedException)
        {
            file.WorkflowState = Constants.WorkflowStates.Removed;
            await file.Update(_dbContext).ConfigureAwait(false);
            return DriveChangeOutcome.PermissionDenied;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "RefreshFileAsync({FileId}) failed unexpectedly", file.Id);
            return DriveChangeOutcome.Error;
        }

        if (changed)
        {
            // Spec PR-7 step 4: audit-log the change so an operator can
            // trace any single refresh after the fact and PR-8's
            // "Last refreshed" tooltip has a structured source.
            _logger.LogInformation(
                "Drive file refreshed: userId={UserId} driveFileId={DriveFileId} fileId={FileId} oldModifiedTime={OldModifiedTime} newModifiedTime={NewModifiedTime} oldSizeBytes={OldSizeBytes} newSizeBytes={NewSizeBytes}",
                token.UserId, file.DriveFileId, file.Id,
                oldModifiedTime, file.DriveModifiedTime, oldSizeBytes, file.SizeBytes);
            return DriveChangeOutcome.Refreshed;
        }

        return DriveChangeOutcome.NoChange;
    }
}
