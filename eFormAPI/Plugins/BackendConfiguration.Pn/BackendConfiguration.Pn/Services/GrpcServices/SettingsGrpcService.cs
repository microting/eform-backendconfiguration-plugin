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

        return new RegisterPushTokenResponse();
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
