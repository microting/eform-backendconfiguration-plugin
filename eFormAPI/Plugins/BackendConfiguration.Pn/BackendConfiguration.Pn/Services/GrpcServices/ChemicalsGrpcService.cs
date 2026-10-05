/*
The MIT License (MIT)
Copyright (c) 2007 - 2026 Microting A/S
Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:
The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.
THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/


using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Grpc.Chemicals;
using BackendConfiguration.Pn.Infrastructure.Models.Chemicals;
using BackendConfiguration.Pn.Services.ChemicalInventoryService;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using static BackendConfiguration.Pn.Services.GrpcServices.ChemicalsProtoMapper;

namespace BackendConfiguration.Pn.Services.GrpcServices;

/// <summary>
/// gRPC adapter for flutter-chemistry (chemicals.proto). Resolves the caller
/// like AdhocGrpcService (SDK site id via IGrpcSiteResolver; Unauthenticated
/// when 0) and forwards to IChemicalInventoryService, which owns every rule and
/// permission check. Typed failures map to status codes in RunAsync.
/// SuggestBarcode is deliberately not overridden (D3): the generated base
/// answers UNIMPLEMENTED.
/// </summary>
public class ChemicalsGrpcService(
    IChemicalInventoryService inventory,
    IGrpcSiteResolver siteResolver,
    IUserService userService,
    ILogger<ChemicalsGrpcService> logger) : ChemicalsGrpc.ChemicalsGrpcBase
{
    /// <summary>Download chunk size (spec §6: 64–256 KB).</summary>
    internal const int FileChunkSize = 128 * 1024;

    public override async Task<ChemicalInventoryResponse> GetMyInventory(ChemicalInventoryRequest request, ServerCallContext context) =>
        ToProto(await RunAsync(caller => inventory.GetInventoryAsync(caller, request.Since)).ConfigureAwait(false));

    public override async Task GetSdsPdf(ChemicalSdsPdfRequest request, IServerStreamWriter<ChemicalFileChunk> responseStream,
        ServerCallContext context)
    {
        var content = await RunAsync(caller => inventory.GetSdsPdfAsync(caller, request.FileName)).ConfigureAwait(false);
        await WriteFileAsync(responseStream, content, "application/pdf", request.Offset, context.CancellationToken).ConfigureAwait(false);
    }

    public override async Task GetLocationPhoto(ChemicalLocationPhotoRequest request, IServerStreamWriter<ChemicalFileChunk> responseStream,
        ServerCallContext context)
    {
        var (content, contentType) = await RunAsync(caller => inventory.GetLocationPhotoAsync(caller, request.LocationId)).ConfigureAwait(false);
        await WriteFileAsync(responseStream, content, contentType, request.Offset, context.CancellationToken).ConfigureAwait(false);
    }

    public override async Task<ChemicalRegisterSearchResponse> LookupBarcode(ChemicalLookupBarcodeRequest request, ServerCallContext context)
    {
        // GS1 Digital Link / element string / GTIN-14 → GTIN; a junk scan is INVALID_ARGUMENT (inside RunAsync).
        var entries = await RunAsync(caller => inventory.LookupBarcodeAsync(caller, ChemicalBarcode.Normalize(request.Barcode)))
            .ConfigureAwait(false);
        return ToProto(new ChemicalRegisterPageModel(entries, entries.Count));
    }

    public override async Task<ChemicalRegisterSearchResponse> SearchRegister(ChemicalSearchRegisterRequest request, ServerCallContext context) =>
        ToProto(await RunAsync(caller => inventory.SearchRegisterAsync(caller, ChemicalBarcode.NormalizeSearchQuery(request.Query),
            request.Page, request.PageSize)).ConfigureAwait(false));

    // Commands are built inside RunAsync: a malformed timestamp or amount is an
    // ArgumentException there, and so INVALID_ARGUMENT rather than UNKNOWN.

    public override async Task<ChemicalPlacementChangeResponse> RegisterPlacement(ChemicalRegisterPlacementRequest request, ServerCallContext context) =>
        ToProto(await RunAsync(caller => inventory.RegisterPlacementAsync(caller, new ChemicalRegisterPlacementCommand(
            request.LocationId, request.ChemicalId, OptionalId(request.ProductId), request.PlacementNote,
            FromProto(request.InitialStock)))).ConfigureAwait(false));

    public override async Task<ChemicalPlacementChangeResponse> MovePlacement(ChemicalMovePlacementRequest request, ServerCallContext context) =>
        ToProto(await RunAsync(caller => inventory.MovePlacementAsync(caller, new ChemicalMovePlacementCommand(
            request.PlacementId, request.TargetLocationId, request.TargetPlacementNote,
            request.AmountMilli == 0 ? null : ChemicalQuantity.FromMilli(request.AmountMilli)))).ConfigureAwait(false));

    public override async Task<ChemicalPlacementChangeResponse> RemovePlacement(ChemicalRemovePlacementRequest request, ServerCallContext context) =>
        ToProto(await RunAsync(caller => inventory.RemovePlacementAsync(caller, new ChemicalRemovePlacementCommand(
            request.PlacementId, FromProto(request.Reason), FromProto(request.RemovedAt), request.Note))).ConfigureAwait(false));

    public override async Task<ChemicalPlacementChangeResponse> UpdatePlacementNote(ChemicalUpdatePlacementNoteRequest request,
        ServerCallContext context) =>
        ToProto(await RunAsync(caller => inventory.UpdatePlacementNoteAsync(caller, request.PlacementId, request.PlacementNote))
            .ConfigureAwait(false));

    public override async Task<ChemicalPlacementChangeResponse> AddStockEntry(ChemicalAddStockEntryRequest request, ServerCallContext context) =>
        ToProto(await RunAsync(caller => inventory.AddStockEntryAsync(caller, new ChemicalAddStockEntryCommand(
            request.PlacementId, FromProto(request.Kind), FromProto(request.Amount)))).ConfigureAwait(false));

    public override async Task<ChemicalLocationResponse> CreateLocation(ChemicalCreateLocationRequest request, ServerCallContext context) =>
        ToLocationResponse(await RunAsync(caller => inventory.CreateLocationAsync(caller, new ChemicalCreateLocationCommand(
            request.PropertyId, request.Name, request.Description, OptionalId(request.SortOrder)))).ConfigureAwait(false));

    public override async Task<ChemicalLocationResponse> UpdateLocation(ChemicalUpdateLocationRequest request, ServerCallContext context) =>
        ToLocationResponse(await RunAsync(caller => inventory.UpdateLocationAsync(caller, new ChemicalUpdateLocationCommand(
            request.LocationId, request.Name, request.Description, OptionalId(request.SortOrder)))).ConfigureAwait(false));

    public override async Task<ChemicalLocationResponse> ArchiveLocation(ChemicalArchiveLocationRequest request, ServerCallContext context) =>
        ToLocationResponse(await RunAsync(caller => inventory.ArchiveLocationAsync(caller, request.LocationId)).ConfigureAwait(false));

    public override async Task<ChemicalReorderLocationsResponse> ReorderLocations(ChemicalReorderLocationsRequest request,
        ServerCallContext context)
    {
        var locations = await RunAsync(caller => inventory.ReorderLocationsAsync(caller, request.PropertyId, request.LocationIds.ToList()))
            .ConfigureAwait(false);
        var response = new ChemicalReorderLocationsResponse();
        response.Locations.AddRange(locations.Select(ToProto));
        return response;
    }

    public override async Task<ChemicalLocationResponse> UploadLocationPhoto(IAsyncStreamReader<ChemicalLocationPhotoUploadChunk> requestStream,
        ServerCallContext context) =>
        ToLocationResponse(await RunAsync(async caller =>
        {
            var meta = await ReadMetaAsync(requestStream,
                c => c.KindCase == ChemicalLocationPhotoUploadChunk.KindOneofCase.Meta ? c.Meta : null,
                context.CancellationToken).ConfigureAwait(false);
            // Refuse before buffering the photo; SaveLocationPhotoAsync checks again.
            await inventory.RequireCanManageLocationAsync(caller, meta.LocationId).ConfigureAwait(false);
            var content = await ReadBytesAsync(requestStream,
                c => c.KindCase == ChemicalLocationPhotoUploadChunk.KindOneofCase.Chunk ? c.Chunk : null,
                context.CancellationToken).ConfigureAwait(false);
            return await inventory.SaveLocationPhotoAsync(caller, meta.LocationId, content, meta.ContentType).ConfigureAwait(false);
        }).ConfigureAwait(false));

    public override async Task<ChemicalListWorkerPermissionsResponse> ListWorkerPermissions(ChemicalListWorkerPermissionsRequest request,
        ServerCallContext context)
    {
        var workers = await RunAsync(caller => inventory.ListWorkerPermissionsAsync(caller, request.PropertyId)).ConfigureAwait(false);
        var response = new ChemicalListWorkerPermissionsResponse();
        response.Workers.AddRange(workers.Select(ToProto));
        return response;
    }

    public override async Task<ChemicalWorkerPermissionEntry> SetWorkerPermission(ChemicalSetWorkerPermissionRequest request,
        ServerCallContext context)
    {
        var workers = await RunAsync(caller => inventory.SetWorkerPermissionsAsync(caller, request.PropertyId,
            [new ChemicalSetWorkerPermissionCommand(request.WorkerId, FromProto(request.Flags))])).ConfigureAwait(false);
        return ToProto(workers.Single(w => w.WorkerId == request.WorkerId));
    }

    public override async Task<ChemicalSettings> GetPropertySettings(ChemicalPropertySettingsRequest request, ServerCallContext context) =>
        ToProto(await RunAsync(caller => inventory.GetSettingsAsync(caller, request.PropertyId)).ConfigureAwait(false));

    public override async Task<ChemicalSettings> SetPropertySettings(ChemicalSetPropertySettingsRequest request, ServerCallContext context) =>
        ToProto(await RunAsync(caller => inventory.SetSettingsAsync(caller,
            new ChemicalSetSettingsCommand(request.PropertyId, request.StockEnabled, request.DigestRecipients.ToList()))).ConfigureAwait(false));

    // ---------------------------------------------------------------- helpers

    private async Task<ChemicalCaller> ResolveCallerAsync()
    {
        var workerId = await siteResolver.GetSdkSiteIdAsync().ConfigureAwait(false);
        if (workerId == 0)
        {
            logger.LogWarning("ChemicalsGrpcService: no resolvable SDK worker/site identity for the caller.");
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Caller has no resolvable SDK worker/site identity."));
        }

        return ChemicalCaller.App(userService.UserId, workerId);
    }

    /// <summary>
    /// Resolves the caller first (UNAUTHENTICATED passes straight through), then
    /// runs <paramref name="action"/> and maps typed failures to status codes.
    /// </summary>
    private async Task<T> RunAsync<T>(Func<ChemicalCaller, Task<T>> action)
    {
        var caller = await ResolveCallerAsync().ConfigureAwait(false);
        try
        {
            return await action(caller).ConfigureAwait(false);
        }
        catch (ChemicalNotFoundException e)
        {
            throw new RpcException(new Status(StatusCode.NotFound, e.Message));
        }
        catch (ChemicalPermissionDeniedException e)
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied, e.Message));
        }
        catch (ChemicalPreconditionException e)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, e.Message));
        }
        catch (ChemicalConflictException e)
        {
            throw new RpcException(new Status(StatusCode.AlreadyExists, e.Message));
        }
        catch (ChemicalUnavailableException e)
        {
            throw new RpcException(new Status(StatusCode.Unavailable, e.Message));
        }
        catch (ArgumentException e)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, e.Message));
        }
    }

    /// <summary>One meta message, FileChunkSize chunks from <paramref name="offset"/>, one trailer with the whole file's md5.</summary>
    private static async Task WriteFileAsync(IServerStreamWriter<ChemicalFileChunk> stream, byte[] content, string contentType,
        long offset, CancellationToken cancellationToken)
    {
        if (offset < 0 || offset > content.Length)
        {
            throw new RpcException(new Status(StatusCode.OutOfRange, $"offset {offset} is outside the file (0..{content.Length})."));
        }

        await stream.WriteAsync(new ChemicalFileChunk
        {
            Meta = new ChemicalFileMeta { ContentType = contentType, TotalBytes = content.Length, Offset = offset },
        }, cancellationToken).ConfigureAwait(false);

        for (var position = (int)offset; position < content.Length; position += FileChunkSize)
        {
            var length = Math.Min(FileChunkSize, content.Length - position);
            await stream.WriteAsync(new ChemicalFileChunk { Chunk = ByteString.CopyFrom(content, position, length) }, cancellationToken)
                .ConfigureAwait(false);
        }

        await stream.WriteAsync(new ChemicalFileChunk
        {
            Trailer = new ChemicalFileTrailer { Md5 = Convert.ToHexStringLower(MD5.HashData(content)) },
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Client-stream uploads: the first message must carry meta.</summary>
    private static async Task<TMeta> ReadMetaAsync<TChunk, TMeta>(IAsyncStreamReader<TChunk> requestStream,
        Func<TChunk, TMeta> metaOf, CancellationToken cancellationToken)
        where TMeta : class
    {
        if (!await requestStream.MoveNext(cancellationToken).ConfigureAwait(false)
            || metaOf(requestStream.Current) is not { } meta)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "The first message must carry meta."));
        }

        return meta;
    }

    /// <summary>The rest of a client-stream upload: byte chunks only, within the photo size limit.</summary>
    private static async Task<byte[]> ReadBytesAsync<TChunk>(IAsyncStreamReader<TChunk> requestStream,
        Func<TChunk, ByteString> bytesOf, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        while (await requestStream.MoveNext(cancellationToken).ConfigureAwait(false))
        {
            var bytes = bytesOf(requestStream.Current)
                        ?? throw new RpcException(new Status(StatusCode.InvalidArgument, "Only the first message may carry meta."));
            // ArgumentException: RunAsync maps it to INVALID_ARGUMENT.
            Services.ChemicalInventoryService.ChemicalInventoryService.EnsurePhotoWithinLimit(buffer.Length + bytes.Length);
            bytes.WriteTo(buffer);
        }

        return buffer.ToArray();
    }
}
