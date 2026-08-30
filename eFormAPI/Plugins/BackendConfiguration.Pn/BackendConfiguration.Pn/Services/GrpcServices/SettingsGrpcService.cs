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
/// unique index.
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

        var fcmToken = request.FcmToken?.Trim() ?? string.Empty;
        if (fcmToken.Length == 0)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "fcm_token must be non-empty."));
        }

        var appId = request.AppId?.Trim() ?? string.Empty;
        if (appId.Length == 0)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "app_id must be non-empty."));
        }

        var installationId = request.InstallationId?.Trim() ?? string.Empty;
        if (installationId.Length == 0)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "installation_id must be non-empty."));
        }

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
        // (WorkerId, FcmToken), so two workers sharing one device could each
        // hold a row with the same token; taking the lowest Id makes a
        // repeated or retried register always land on the same row. The
        // sibling is left live — soft-deleting it would silently stop push for
        // a user who never reopens the app — and is adopted by its own user's
        // next register or pruned by FCM once the token dies.
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
        }

        return new RegisterPushTokenResponse();
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
