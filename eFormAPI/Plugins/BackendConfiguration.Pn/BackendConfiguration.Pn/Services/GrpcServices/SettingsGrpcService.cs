using System;
using System.Linq;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.Settings.V1;

namespace BackendConfiguration.Pn.Services.GrpcServices;

/// <summary>
/// gRPC adapter for the mobile "Settings" contract
/// (<c>/microting.settings.v1.Settings/*</c>, Protos/settings.proto — kept
/// byte-for-byte identical to the mobile client's copy, hence the
/// package-derived <c>Microting.Settings.V1</c> codegen namespace instead of
/// this repo's usual <c>BackendConfiguration.Pn.Grpc.*</c> option).
///
/// <c>RegisterPushToken</c> resolves the caller's SDK site id via
/// <see cref="IGrpcSiteResolver"/> (mirroring <c>AdhocGrpcService</c>'s
/// convention: <c>RpcException(Unauthenticated)</c> when the resolver returns
/// 0) and upserts a <see cref="DeviceToken"/> row for reminder push delivery.
///
/// UPSERT contract (documented on the unique (AppId, InstallationId) index in
/// <c>BackendConfigurationPnDbContext</c> and on <see cref="DeviceToken"/>):
/// a row is identified by the app install, not by the token. <c>FcmToken</c>
/// rotates and <c>SdkSiteId</c> moves when another user logs in on the same
/// device, so both are updated on the existing row. The index has NO
/// WorkflowState filter and <c>PnBase.Delete()</c> only soft-deletes, so the
/// lookup MUST include soft-deleted rows and <c>Update()</c> them (flipping
/// WorkflowState back to Created) — <c>Create()</c> is only legal when no row
/// matches at all, otherwise a re-register after logout would violate the
/// unique index. Any other live row holding the same (AppId, FcmToken) is then
/// retired: one FCM token addresses one install, so a second live row carrying
/// it would duplicate every push to that device.
///
/// <c>ReportInstall</c> (#1335) records which app version runs on the install,
/// on every start-up or login, with or without push permission. It upserts the
/// same (AppId, InstallationId) row and never sets <c>FcmToken</c>: a row it
/// creates has none, and a soft-deleted row it revives loses its old one, so
/// push senders (which skip token-less rows) cannot reach a stale token or the
/// previous owner until the app registers again.
///
/// <c>SetUserPrefs</c> is intentionally not overridden yet — the base class
/// answers <c>Unimplemented</c>, which the mobile client tolerates.
/// </summary>
public class SettingsGrpcService(
    BackendConfigurationPnDbContext dbContext,
    IGrpcSiteResolver siteResolver,
    ILogger<SettingsGrpcService> logger)
    : Settings.SettingsBase
{
    public override async Task<RegisterPushTokenResponse> RegisterPushToken(
        RegisterPushTokenRequest request,
        ServerCallContext context)
    {
        var sdkSiteId = await ResolveSdkSiteIdAsync().ConfigureAwait(false);

        var fcmToken = RequireNonEmpty(request.FcmToken, "fcm_token");
        var appId = RequireNonEmpty(request.AppId, "app_id");
        var installationId = RequireNonEmpty(request.InstallationId, "installation_id");

        // Platform is stored exactly as sent ("android"/"ios"/"unknown").
        var platform = request.Platform ?? string.Empty;

        await UpsertRetryingOnceAsync(
            () => UpsertPushTokenAsync(appId, installationId, fcmToken, sdkSiteId, platform)).ConfigureAwait(false);

        return new RegisterPushTokenResponse();
    }

    /// <summary>
    /// RegisterPushToken and ReportInstall run at the same app start, so both can make the
    /// first insert for one install. The loser of the unique (AppId, InstallationId) slot
    /// retries once and updates the winner's row.
    /// </summary>
    private async Task UpsertRetryingOnceAsync(Func<Task> upsert)
    {
        try
        {
            await upsert().ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();
            await upsert().ConfigureAwait(false);
        }
    }

    private async Task UpsertPushTokenAsync(string appId, string installationId, string fcmToken, int sdkSiteId,
        string platform)
    {
        // No WorkflowState filter here — soft-deleted rows MUST be found and
        // revived via Update(); see the class doc / the base repo's index note.
        var existing = await dbContext.DeviceTokens
            .FirstOrDefaultAsync(t => t.AppId == appId && t.InstallationId == installationId)
            .ConfigureAwait(false);

        // Legacy-row adoption. The DeviceTokenIdentityModel migration in
        // Microting.EformBackendConfigurationBase backfills every pre-existing
        // row with InstallationId = CONCAT('legacy:', Id) — a synthetic value
        // no client will ever send — and leaves it WorkflowState=created
        // holding a live FCM token. Without this fallback the first register
        // from an already-installed device would find no match on its real
        // installation id and insert a SECOND row for the same physical
        // device, and both rows would be selected by the send path forever:
        // FCM never reports a working token as Unregistered, so the duplicate
        // is never pruned and every legacy device gets doubled pushes.
        //
        // Scoped to the app on purpose: an FcmToken is only meaningful inside
        // the Firebase project that minted it, so a row belonging to a
        // different AppId must never be claimed.
        //
        // OrderBy(Id) is load-bearing. The outgoing key was
        // (WorkerId, FcmToken), so two workers signing in on one shared device
        // each got their own row with the same token; taking the lowest Id
        // makes a repeated or retried register always land on the same one.
        // The rows this does not pick are retired below.
        existing ??= await dbContext.DeviceTokens
            .Where(t => t.AppId == appId && t.FcmToken == fcmToken)
            .OrderBy(t => t.Id)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);

        if (existing != null)
        {
            existing.InstallationId = installationId;
            existing.FcmToken = fcmToken;
            existing.SdkSiteId = sdkSiteId;
            existing.Platform = platform;
            existing.WorkflowState = Constants.WorkflowStates.Created;
            await existing.Update(dbContext).ConfigureAwait(false);

            // A device usually re-registers the same token, and EF leaves an unchanged
            // column out of the UPDATE. If a concurrent ReportInstall cleared the token after
            // the row was loaded here (a revive by this same owner), the row would stay
            // token-less: restore the token - only while the row is still live and owned by
            // this caller, so a row another worker has just taken over is never handed this
            // caller's token. Not versioned; the normal case writes nothing more.
            var rowId = existing.Id;
            await dbContext.DeviceTokens
                .Where(t => t.Id == rowId
                            && t.SdkSiteId == sdkSiteId
                            && t.WorkflowState == Constants.WorkflowStates.Created
                            && (t.FcmToken == null || t.FcmToken != fcmToken))
                .ExecuteUpdateAsync(u => u.SetProperty(t => t.FcmToken, fcmToken))
                .ConfigureAwait(false);
        }
        else
        {
            var deviceToken = new DeviceToken
            {
                AppId = appId,
                InstallationId = installationId,
                FcmToken = fcmToken,
                SdkSiteId = sdkSiteId,
                Platform = platform
            };
            await deviceToken.Create(dbContext).ConfigureAwait(false);
            existing = deviceToken;
        }

        await RetireStaleRowsForTokenAsync(appId, fcmToken, existing.Id).ConfigureAwait(false);
    }

    public override async Task<ReportInstallResponse> ReportInstall(
        ReportInstallRequest request,
        ServerCallContext context)
    {
        var sdkSiteId = await ResolveSdkSiteIdAsync().ConfigureAwait(false);

        var appId = RequireNonEmpty(request.AppId, "app_id");
        var installationId = RequireNonEmpty(request.InstallationId, "installation_id");

        await UpsertRetryingOnceAsync(
            () => UpsertInstallReportAsync(appId, installationId, sdkSiteId, request)).ConfigureAwait(false);

        return new ReportInstallResponse();
    }

    /// <summary>
    /// A report that changes nothing and comes within this interval of the last one is not
    /// written: every write adds a DeviceTokenVersions row, and the apps report on every start.
    /// </summary>
    private static readonly TimeSpan LastSeenResolution = TimeSpan.FromHours(1);

    private async Task UpsertInstallReportAsync(string appId, string installationId, int sdkSiteId,
        ReportInstallRequest request)
    {
        var platform = ToColumn(request.Platform, 50);
        var appVersion = ToColumn(request.AppVersion, 32);
        var appBuild = ToColumn(request.AppBuild, 32);
        var deviceModel = ToColumn(request.DeviceModel, 128);
        var deviceManufacturer = ToColumn(request.DeviceManufacturer, 128);
        var osVersion = ToColumn(request.OsVersion, 64);
        var now = DateTime.UtcNow;

        // No WorkflowState filter: a soft-deleted install row is revived, never duplicated.
        var row = await dbContext.DeviceTokens
            .FirstOrDefaultAsync(t => t.AppId == appId && t.InstallationId == installationId)
            .ConfigureAwait(false);

        if (row == null)
        {
            await new DeviceToken
            {
                AppId = appId, InstallationId = installationId, SdkSiteId = sdkSiteId, Platform = platform,
                AppVersion = appVersion, AppBuild = appBuild, DeviceModel = deviceModel,
                DeviceManufacturer = deviceManufacturer, OsVersion = osVersion, LastSeenAt = now
            }.Create(dbContext).ConfigureAwait(false);
            return;
        }

        var revivedOrMoved = row.WorkflowState == Constants.WorkflowStates.Removed || row.SdkSiteId != sdkSiteId;
        var unchanged = !revivedOrMoved
                        && (platform == null || row.Platform == platform)
                        && row.AppVersion == appVersion && row.AppBuild == appBuild
                        && row.DeviceModel == deviceModel && row.DeviceManufacturer == deviceManufacturer
                        && row.OsVersion == osVersion
                        && row.LastSeenAt > now - LastSeenResolution;
        if (unchanged)
        {
            return;
        }

        if (revivedOrMoved)
        {
            // The token must not come back with a revived row, nor follow the install to
            // another owner. Cleared in SQL only while that is still true, so a token a
            // concurrent RegisterPushToken just committed for this owner is never overwritten;
            // then re-read the row so the save below does not write the stale value back.
            await dbContext.DeviceTokens
                .Where(t => t.Id == row.Id
                            && (t.WorkflowState == Constants.WorkflowStates.Removed || t.SdkSiteId != sdkSiteId))
                .ExecuteUpdateAsync(u => u.SetProperty(t => t.FcmToken, (string)null))
                .ConfigureAwait(false);
            await dbContext.Entry(row).ReloadAsync().ConfigureAwait(false);
        }

        row.SdkSiteId = sdkSiteId;
        // A blank platform keeps what RegisterPushToken stored.
        row.Platform = platform ?? row.Platform;
        row.AppVersion = appVersion;
        row.AppBuild = appBuild;
        row.DeviceModel = deviceModel;
        row.DeviceManufacturer = deviceManufacturer;
        row.OsVersion = osVersion;
        row.LastSeenAt = now;
        row.WorkflowState = Constants.WorkflowStates.Created;
        await row.Update(dbContext).ConfigureAwait(false);
    }

    /// <summary>Trimmed, empty as null, cut to the column length.</summary>
    private static string ToColumn(string raw, int maxLength)
    {
        var value = raw?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    /// <summary>
    /// Soft-deletes every other live row holding the same (AppId, FcmToken).
    /// </summary>
    /// <remarks>
    /// An FCM token addresses exactly one app install, so once the row above
    /// has been claimed for that install any other live row carrying the same
    /// token is stale by construction and would be a second copy of every push
    /// to the same device.
    ///
    /// This is what actually finishes the legacy-row cleanup. Adoption alone
    /// cannot: it only runs when the (AppId, InstallationId) lookup misses,
    /// and InstallationId identifies the INSTALL, not the user. The migration
    /// can leave two legacy rows for one shared device (one per worker, from
    /// the outgoing (WorkerId, FcmToken) key), and both users register from
    /// that one install — so the second user's register hits the row the first
    /// already adopted and never reaches the fallback. Without this sweep the
    /// unadopted sibling stays live forever, doubling pushes and delivering
    /// the previous user's reminders to a device they no longer hold.
    ///
    /// Retiring it does not silently cut anyone off: the sweep only fires
    /// because that very device just registered, so its owner is whoever the
    /// surviving row now names.
    /// </remarks>
    private async Task RetireStaleRowsForTokenAsync(string appId, string fcmToken, int keptId)
    {
        var stale = await dbContext.DeviceTokens
            .Where(t => t.AppId == appId
                        && t.FcmToken == fcmToken
                        && t.Id != keptId
                        && t.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync()
            .ConfigureAwait(false);

        foreach (var row in stale)
        {
            await row.Delete(dbContext).ConfigureAwait(false);
        }
    }

    private static string RequireNonEmpty(string raw, string fieldName)
    {
        var value = raw?.Trim() ?? string.Empty;
        if (value.Length == 0)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"{fieldName} must be non-empty."));
        }
        return value;
    }

    private async Task<int> ResolveSdkSiteIdAsync()
    {
        var sdkSiteId = await siteResolver.GetSdkSiteIdAsync().ConfigureAwait(false);
        if (sdkSiteId == 0)
        {
            logger.LogWarning("SettingsGrpcService: no resolvable SDK worker/site identity for the caller.");
            throw new RpcException(new Status(StatusCode.Unauthenticated,
                "Caller has no resolvable SDK worker/site identity."));
        }
        return sdkSiteId;
    }
}
