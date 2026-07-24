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
/// UPSERT contract (documented on the unique (WorkerId, FcmToken) index in
/// <c>BackendConfigurationPnDbContext</c> and on <see cref="DeviceToken"/>):
/// the index has NO WorkflowState filter and <c>PnBase.Delete()</c> only
/// soft-deletes, so the lookup MUST include soft-deleted rows and
/// <c>Update()</c> an existing row (flipping WorkflowState back to Created)
/// — <c>Create()</c> is only legal when no row exists at all, otherwise a
/// re-register after logout would violate the unique index.
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
        var workerId = await ResolveWorkerIdAsync().ConfigureAwait(false);

        var fcmToken = request.FcmToken?.Trim() ?? string.Empty;
        if (fcmToken.Length == 0)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "fcm_token must be non-empty."));
        }

        // Platform is stored exactly as sent ("android"/"ios"/"unknown").
        var platform = request.Platform ?? string.Empty;

        // No WorkflowState filter here — soft-deleted rows MUST be found and
        // revived via Update(); see the class doc / the base repo's index note.
        var existing = await dbContext.DeviceTokens
            .FirstOrDefaultAsync(t => t.WorkerId == workerId && t.FcmToken == fcmToken)
            .ConfigureAwait(false);

        if (existing != null)
        {
            existing.Platform = platform;
            existing.WorkflowState = Constants.WorkflowStates.Created;
            await existing.Update(dbContext).ConfigureAwait(false);
        }
        else
        {
            var deviceToken = new DeviceToken
            {
                WorkerId = workerId,
                FcmToken = fcmToken,
                Platform = platform
            };
            await deviceToken.Create(dbContext).ConfigureAwait(false);
        }

        return new RegisterPushTokenResponse();
    }

    private async Task<int> ResolveWorkerIdAsync()
    {
        var workerId = await siteResolver.GetSdkSiteIdAsync().ConfigureAwait(false);
        if (workerId == 0)
        {
            logger.LogWarning("SettingsGrpcService: no resolvable SDK worker/site identity for the caller.");
            throw new RpcException(new Status(StatusCode.Unauthenticated,
                "Caller has no resolvable SDK worker/site identity."));
        }
        return workerId;
    }
}
