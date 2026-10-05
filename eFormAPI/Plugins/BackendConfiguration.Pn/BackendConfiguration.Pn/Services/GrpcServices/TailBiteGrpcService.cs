#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Grpc.TailBite;
using BackendConfiguration.Pn.Services.TailBite;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.Logging;

using static BackendConfiguration.Pn.Services.GrpcServices.TailBiteGrpcMapper;

namespace BackendConfiguration.Pn.Services.GrpcServices;

/// <summary>
/// gRPC adapter for the tail-bite (halebid) app. Every RPC resolves the caller's SDK site id through
/// <see cref="ITailBiteAccess.RequireCallerSiteAsync"/>, parses the wire ids, calls the tail-bite service that owns the
/// rule and maps the result back. Authorisation (worker / manager of the property) lives in the services, not here.
///
/// <see cref="TailBiteException"/> subclasses map to RPC status codes: Forbidden -&gt; PermissionDenied, NotFound -&gt;
/// NotFound, Validation -&gt; InvalidArgument, Conflict -&gt; FailedPrecondition. An <see cref="RpcException"/> is
/// rethrown unchanged; anything else is logged and surfaces as Internal with a generic message.
///
/// Ids are decimal strings (empty = unset), photo and registration identities are UUIDs, times are UTC Timestamps.
/// <c>UploadPhoto</c>/<c>GetPhoto</c> follow <see cref="AdhocGrpcService"/>: meta first, then only chunks, a 20 MB cap,
/// and a 64 KB chunked download that sends the content type first.
/// </summary>
public class TailBiteGrpcService(
    ITailBiteAccess access,
    ITailBiteRegistrationService registrations,
    ITailBiteSetupService setup,
    ITailBiteOutbreakService outbreaks,
    IGrpcSiteResolver siteResolver,
    ILogger<TailBiteGrpcService> logger)
    : TailBiteGrpc.TailBiteGrpcBase
{
    private const int MaxPhotoBytes = 20 * 1024 * 1024;
    private const int PhotoChunkSize = 64 * 1024;

    // ---------------------------------------------------------------------
    // Worker RPCs
    // ---------------------------------------------------------------------

    public override Task<TbCurrentWorker> GetCurrentWorker(TbGetCurrentWorkerRequest request, ServerCallContext context) => RunAsSiteAsync(async site =>
    {
        var displayName = await siteResolver.GetDisplayNameAsync(site).ConfigureAwait(false);
        var properties = await setup.ListEnabledPropertiesAsync(site).ConfigureAwait(false);
        var response = new TbCurrentWorker { SiteId = S(site), DisplayName = displayName ?? string.Empty };
        response.Properties.AddRange(properties.Select(p => new TbProperty
        {
            PropertyId = S(p.PropertyId), Name = p.Name ?? string.Empty, IsManager = p.IsManager
        }));
        return response;
    });

    public override Task<TbLocationTree> GetLocationTree(TbGetLocationTreeRequest request, ServerCallContext context) => RunAsSiteAsync(async site =>
        MapTree(await setup.GetTreeAsync(site, Id(request.PropertyId, "property_id")).ConfigureAwait(false)));

    public override Task<TbCreateRegistrationResponse> CreateRegistration(TbCreateRegistrationRequest request, ServerCallContext context) => RunAsSiteAsync(async site =>
    {
        var command = new CreateRegistrationCommand(
            Uuid(request.ClientUuid, "client_uuid"),
            Id(request.PropertyId, "property_id"),
            Utc(request.RegisteredAt, "registered_at"),
            request.Locations.Select(l => new RegistrationLocationInput(Id(l.LocationId, "location_id"), l.Minor, l.Severe)).ToList(),
            request.ActionTypeIds.Select(a => Id(a, "action_type_id")).ToList(),
            string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment);
        var result = await registrations.CreateAsync(site, command).ConfigureAwait(false);
        var response = new TbCreateRegistrationResponse { RegistrationId = S(result.RegistrationId) };
        response.Outbreaks.AddRange(result.Outbreaks.Select(o => new TbOutbreakOutcome { OutbreakId = S(o.OutbreakId), Opened = o.Opened }));
        return response;
    });

    /// <summary>
    /// Receives a photo as a stream: the first message MUST be <c>meta</c>, every later one MUST be <c>chunk</c> bytes.
    /// The meta is validated before the caller is resolved, so this RPC does not use <see cref="RunAsSiteAsync{T}"/>.
    /// </summary>
    public override Task<TbUploadPhotoResponse> UploadPhoto(IAsyncStreamReader<TbUploadPhotoChunk> requestStream, ServerCallContext context) => RunAsync(async () =>
    {
        if (!await requestStream.MoveNext(context.CancellationToken).ConfigureAwait(false))
        {
            throw Invalid("UploadPhoto stream is empty — at least a meta chunk is required.");
        }

        var first = requestStream.Current;
        if (first.KindCase != TbUploadPhotoChunk.KindOneofCase.Meta)
        {
            throw Invalid("First TbUploadPhotoChunk must carry the meta oneof variant.");
        }

        var meta = first.Meta;
        var photoUuid = Uuid(meta.PhotoUuid, "photo_uuid");
        var propertyId = Id(meta.PropertyId, "property_id");
        var registrationUuid = Uuid(meta.RegistrationClientUuid, "registration_client_uuid");
        var contentType = meta.ContentType?.Trim() ?? string.Empty;

        var site = await access.RequireCallerSiteAsync().ConfigureAwait(false);

        using var ms = new MemoryStream();
        while (await requestStream.MoveNext(context.CancellationToken).ConfigureAwait(false))
        {
            var chunk = requestStream.Current;
            if (chunk.KindCase != TbUploadPhotoChunk.KindOneofCase.Chunk)
            {
                throw Invalid("Only the first TbUploadPhotoChunk may carry meta; all subsequent chunks must be `chunk` bytes.");
            }

            if (chunk.Chunk.Length == 0)
            {
                continue;
            }

            if (ms.Length + chunk.Chunk.Length > MaxPhotoBytes)
            {
                throw Invalid($"Photo exceeds {MaxPhotoBytes / (1024 * 1024)} MB limit.");
            }

            chunk.Chunk.WriteTo(ms);
        }

        if (ms.Length == 0)
        {
            throw Invalid("UploadPhoto stream contained no image bytes.");
        }

        var saved = await registrations.SavePhotoAsync(site, propertyId, photoUuid, registrationUuid, ms.ToArray(), contentType)
            .ConfigureAwait(false);
        return new TbUploadPhotoResponse { PhotoUuid = saved.ToString() };
    });

    /// <summary>Streams a photo back: the content type first, then <see cref="PhotoChunkSize"/>-sized chunks.</summary>
    public override async Task GetPhoto(TbGetPhotoRequest request, IServerStreamWriter<TbPhotoChunk> responseStream, ServerCallContext context)
    {
        var (content, contentType) = await RunAsSiteAsync(site =>
            registrations.GetPhotoAsync(site, Uuid(request.PhotoUuid, "photo_uuid"))).ConfigureAwait(false);

        await using (content.ConfigureAwait(false))
        {
            await responseStream.WriteAsync(new TbPhotoChunk { ContentType = contentType ?? string.Empty }, context.CancellationToken)
                .ConfigureAwait(false);

            var buffer = new byte[PhotoChunkSize];
            int bytesRead;
            while ((bytesRead = await content.ReadAsync(buffer, 0, buffer.Length, context.CancellationToken).ConfigureAwait(false)) > 0)
            {
                await responseStream.WriteAsync(new TbPhotoChunk { Chunk = ByteString.CopyFrom(buffer, 0, bytesRead) }, context.CancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    public override Task<TbListRecentResponse> ListMyRecentRegistrations(TbListRecentRequest request, ServerCallContext context) => RunAsSiteAsync(async site =>
    {
        var propertyId = Id(request.PropertyId, "property_id");
        // `since` is optional: unset means "everything the service's own recent-limit allows".
        var since = OptionalUtc(request.Since, "since") ?? DateTime.UnixEpoch;
        var rows = await registrations.ListRecentAsync(site, propertyId, since).ConfigureAwait(false);
        var response = new TbListRecentResponse();
        response.Registrations.AddRange(rows.Select(MapRecent));
        return response;
    });

    // ---------------------------------------------------------------------
    // Manager RPCs
    // ---------------------------------------------------------------------

    public override Task<TbListOutbreaksResponse> ListOutbreaks(TbListOutbreaksRequest request, ServerCallContext context) => RunAsSiteAsync(async site =>
    {
        var rows = await outbreaks.ListAsync(site, Id(request.PropertyId, "property_id"), request.OpenOnly).ConfigureAwait(false);
        var response = new TbListOutbreaksResponse();
        response.Outbreaks.AddRange(rows.Select(MapSummary));
        return response;
    });

    public override Task<TbOutbreakDetail> GetOutbreak(TbGetOutbreakRequest request, ServerCallContext context) => RunAsSiteAsync(site =>
        DetailAsync(site, Id(request.OutbreakId, "outbreak_id")));

    public override Task<TbOutbreakDetail> SaveRiskAssessment(TbSaveRiskAssessmentRequest request, ServerCallContext context) => RunAsSiteAsync(async site =>
    {
        var outbreakId = Id(request.OutbreakId, "outbreak_id");
        var wire = request.Answers ?? throw Invalid("answers is required.");
        var answers = new FactorAnswers(wire.Water, wire.Feed, wire.ActivityMaterial, wire.Climate, wire.Health, wire.Management);
        var newActions = request.NewActions.Select(a => new ActionInput(
            Factor(a.Factor),
            a.Description,
            Id(a.ResponsibleSiteId, "responsible_site_id"),
            Utc(a.FollowUpDate, "follow_up_date"))).ToList();
        await outbreaks.SaveAssessmentAsync(site, outbreakId, answers, newActions).ConfigureAwait(false);
        return await DetailAsync(site, outbreakId).ConfigureAwait(false);
    });

    public override Task<TbOutbreakDetail> SetActionDone(TbSetActionDoneRequest request, ServerCallContext context) => RunAsSiteAsync(async site =>
    {
        var actionId = Id(request.ActionId, "action_id");
        await outbreaks.SetActionDoneAsync(site, actionId, request.Done).ConfigureAwait(false);
        return await DetailForActionAsync(site, actionId).ConfigureAwait(false);
    });

    public override Task<TbOutbreakDetail> WithdrawAction(TbWithdrawActionRequest request, ServerCallContext context) => RunAsSiteAsync(async site =>
    {
        var actionId = Id(request.ActionId, "action_id");
        await outbreaks.WithdrawActionAsync(site, actionId, request.Reason).ConfigureAwait(false);
        return await DetailForActionAsync(site, actionId).ConfigureAwait(false);
    });

    public override Task<TbOutbreakDetail> ReassignAction(TbReassignActionRequest request, ServerCallContext context) => RunAsSiteAsync(async site =>
    {
        var actionId = Id(request.ActionId, "action_id");
        var responsibleSiteId = Id(request.ResponsibleSiteId, "responsible_site_id");
        await outbreaks.ReassignActionAsync(site, actionId, responsibleSiteId).ConfigureAwait(false);
        return await DetailForActionAsync(site, actionId).ConfigureAwait(false);
    });

    public override Task<TbOutbreakDetail> CloseOutbreak(TbCloseOutbreakRequest request, ServerCallContext context) => RunAsSiteAsync(async site =>
    {
        var outbreakId = Id(request.OutbreakId, "outbreak_id");
        await outbreaks.CloseAsync(site, outbreakId).ConfigureAwait(false);
        return await DetailAsync(site, outbreakId).ConfigureAwait(false);
    });

    public override Task<TbEmpty> CancelRegistration(TbCancelRegistrationRequest request, ServerCallContext context) => RunAsSiteAsync(async site =>
    {
        await outbreaks.CancelRegistrationAsync(site, Id(request.RegistrationId, "registration_id"), request.Reason).ConfigureAwait(false);
        return new TbEmpty();
    });

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    // Resolves the caller's site, then runs the work with the TailBite exception mapping applied.
    private Task<T> RunAsSiteAsync<T>(Func<int, Task<T>> work) => RunAsync(async () =>
        await work(await access.RequireCallerSiteAsync().ConfigureAwait(false)).ConfigureAwait(false));

    private async Task<TbOutbreakDetail> DetailAsync(int site, int outbreakId)
        => MapDetail(await outbreaks.GetAsync(site, outbreakId).ConfigureAwait(false));

    private async Task<TbOutbreakDetail> DetailForActionAsync(int site, int actionId)
        => MapDetail(await outbreaks.GetForActionAsync(site, actionId).ConfigureAwait(false));

    private async Task<T> RunAsync<T>(Func<Task<T>> work)
    {
        try
        {
            return await work().ConfigureAwait(false);
        }
        catch (RpcException)
        {
            throw;
        }
        catch (TailBiteException e)
        {
            var code = e switch
            {
                TailBiteForbiddenException => StatusCode.PermissionDenied,
                TailBiteNotFoundException => StatusCode.NotFound,
                TailBiteValidationException => StatusCode.InvalidArgument,
                TailBiteConflictException => StatusCode.FailedPrecondition,
                _ => StatusCode.Internal
            };
            throw new RpcException(new Status(code, e.Message));
        }
        catch (Exception e)
        {
            logger.LogError(e, "TailBiteGrpcService: unexpected failure");
            throw new RpcException(new Status(StatusCode.Internal, "Unexpected error."));
        }
    }
}
