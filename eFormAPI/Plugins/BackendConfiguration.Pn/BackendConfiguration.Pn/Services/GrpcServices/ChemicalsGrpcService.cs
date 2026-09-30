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

    /// <summary>Upload ceiling: the inventory service's own photo limit, enforced while the stream is read.</summary>
    private const int MaxUploadBytes = Services.ChemicalInventoryService.ChemicalInventoryService.MaxPhotoBytes;

    public override async Task<ChemicalInventoryResponse> GetMyInventory(ChemicalInventoryRequest request, ServerCallContext context)
    {
        var caller = await ResolveCallerAsync().ConfigureAwait(false);
        return ToProto(await RunAsync(() => inventory.GetInventoryAsync(caller, request.Since)).ConfigureAwait(false));
    }

    public override async Task GetSdsPdf(ChemicalSdsPdfRequest request, IServerStreamWriter<ChemicalFileChunk> responseStream,
        ServerCallContext context)
    {
        var caller = await ResolveCallerAsync().ConfigureAwait(false);
        var content = await RunAsync(() => inventory.GetSdsPdfAsync(caller, request.FileName)).ConfigureAwait(false);
        await WriteFileAsync(responseStream, content, "application/pdf", request.Offset, context.CancellationToken).ConfigureAwait(false);
    }

    public override async Task GetLocationPhoto(ChemicalLocationPhotoRequest request, IServerStreamWriter<ChemicalFileChunk> responseStream,
        ServerCallContext context)
    {
        var caller = await ResolveCallerAsync().ConfigureAwait(false);
        var (content, contentType) = await RunAsync(() => inventory.GetLocationPhotoAsync(caller, request.LocationId)).ConfigureAwait(false);
        await WriteFileAsync(responseStream, content, contentType, request.Offset, context.CancellationToken).ConfigureAwait(false);
    }

    public override async Task<ChemicalRegisterSearchResponse> LookupBarcode(ChemicalLookupBarcodeRequest request, ServerCallContext context)
    {
        var caller = await ResolveCallerAsync().ConfigureAwait(false);
        var entries = await RunAsync(() => inventory.LookupBarcodeAsync(caller, request.Barcode)).ConfigureAwait(false);
        return ToProto(new ChemicalRegisterPageModel(entries, entries.Count));
    }

    public override async Task<ChemicalRegisterSearchResponse> SearchRegister(ChemicalSearchRegisterRequest request, ServerCallContext context)
    {
        var caller = await ResolveCallerAsync().ConfigureAwait(false);
        return ToProto(await RunAsync(() => inventory.SearchRegisterAsync(caller, request.Query, request.Page, request.PageSize))
            .ConfigureAwait(false));
    }

    public override async Task<ChemicalPlacementChangeResponse> RegisterPlacement(ChemicalRegisterPlacementRequest request, ServerCallContext context)
    {
        var caller = await ResolveCallerAsync().ConfigureAwait(false);
        var command = new ChemicalRegisterPlacementCommand(request.LocationId, request.ChemicalId, OptionalId(request.ProductId),
            request.PlacementNote, FromProto(request.InitialStock));
        return ToProto(await RunAsync(() => inventory.RegisterPlacementAsync(caller, command)).ConfigureAwait(false));
    }

    public override async Task<ChemicalPlacementChangeResponse> MovePlacement(ChemicalMovePlacementRequest request, ServerCallContext context)
    {
        var caller = await ResolveCallerAsync().ConfigureAwait(false);
        var command = new ChemicalMovePlacementCommand(request.PlacementId, request.TargetLocationId, request.TargetPlacementNote,
            request.AmountMilli == 0 ? null : ChemicalQuantity.FromMilli(request.AmountMilli));
        return ToProto(await RunAsync(() => inventory.MovePlacementAsync(caller, command)).ConfigureAwait(false));
    }

    public override async Task<ChemicalPlacementChangeResponse> RemovePlacement(ChemicalRemovePlacementRequest request, ServerCallContext context)
    {
        var caller = await ResolveCallerAsync().ConfigureAwait(false);
        var command = new ChemicalRemovePlacementCommand(request.PlacementId, (ChemicalRemovalReasonEnum)(int)request.Reason,
            FromProto(request.RemovedAt), request.Note);
        return ToProto(await RunAsync(() => inventory.RemovePlacementAsync(caller, command)).ConfigureAwait(false));
    }

    public override async Task<ChemicalPlacementChangeResponse> UpdatePlacementNote(ChemicalUpdatePlacementNoteRequest request,
        ServerCallContext context)
    {
        var caller = await ResolveCallerAsync().ConfigureAwait(false);
        return ToProto(await RunAsync(() => inventory.UpdatePlacementNoteAsync(caller, request.PlacementId, request.PlacementNote))
            .ConfigureAwait(false));
    }

    public override async Task<ChemicalPlacementChangeResponse> AddStockEntry(ChemicalAddStockEntryRequest request, ServerCallContext context)
    {
        var caller = await ResolveCallerAsync().ConfigureAwait(false);
        var command = new ChemicalAddStockEntryCommand(request.PlacementId, (ChemicalStockEntryKindEnum)(int)request.Kind,
            FromProto(request.Amount));
        return ToProto(await RunAsync(() => inventory.AddStockEntryAsync(caller, command)).ConfigureAwait(false));
    }

    public override async Task<ChemicalLocationResponse> CreateLocation(ChemicalCreateLocationRequest request, ServerCallContext context)
    {
        var caller = await ResolveCallerAsync().ConfigureAwait(false);
        var command = new ChemicalCreateLocationCommand(request.PropertyId, request.Name, request.Description, OptionalId(request.SortOrder));
        return new ChemicalLocationResponse
        {
            Location = ToProto(await RunAsync(() => inventory.CreateLocationAsync(caller, command)).ConfigureAwait(false)),
        };
    }

    public override async Task<ChemicalLocationResponse> UpdateLocation(ChemicalUpdateLocationRequest request, ServerCallContext context)
    {
        var caller = await ResolveCallerAsync().ConfigureAwait(false);
        var command = new ChemicalUpdateLocationCommand(request.LocationId, request.Name, request.Description, OptionalId(request.SortOrder));
        return new ChemicalLocationResponse
        {
            Location = ToProto(await RunAsync(() => inventory.UpdateLocationAsync(caller, command)).ConfigureAwait(false)),
        };
    }

    public override async Task<ChemicalLocationResponse> ArchiveLocation(ChemicalArchiveLocationRequest request, ServerCallContext context)
    {
        var caller = await ResolveCallerAsync().ConfigureAwait(false);
        return new ChemicalLocationResponse
        {
            Location = ToProto(await RunAsync(() => inventory.ArchiveLocationAsync(caller, request.LocationId)).ConfigureAwait(false)),
        };
    }

    public override async Task<ChemicalLocationResponse> UploadLocationPhoto(IAsyncStreamReader<ChemicalLocationPhotoUploadChunk> requestStream,
        ServerCallContext context)
    {
        var caller = await ResolveCallerAsync().ConfigureAwait(false);
        var (meta, content) = await ReadMetaThenBytesAsync(requestStream,
            c => c.KindCase == ChemicalLocationPhotoUploadChunk.KindOneofCase.Meta ? c.Meta : null,
            c => c.KindCase == ChemicalLocationPhotoUploadChunk.KindOneofCase.Chunk ? c.Chunk : null,
            context.CancellationToken).ConfigureAwait(false);
        return new ChemicalLocationResponse
        {
            Location = ToProto(await RunAsync(() => inventory.SaveLocationPhotoAsync(caller, meta.LocationId, content, meta.ContentType))
                .ConfigureAwait(false)),
        };
    }

    public override async Task<ChemicalListWorkerPermissionsResponse> ListWorkerPermissions(ChemicalListWorkerPermissionsRequest request,
        ServerCallContext context)
    {
        var caller = await ResolveCallerAsync().ConfigureAwait(false);
        var workers = await RunAsync(() => inventory.ListWorkerPermissionsAsync(caller, request.PropertyId)).ConfigureAwait(false);
        var response = new ChemicalListWorkerPermissionsResponse();
        response.Workers.AddRange(workers.Select(ToProto));
        return response;
    }

    public override async Task<ChemicalWorkerPermissionEntry> SetWorkerPermission(ChemicalSetWorkerPermissionRequest request,
        ServerCallContext context)
    {
        var caller = await ResolveCallerAsync().ConfigureAwait(false);
        var workers = await RunAsync(() => inventory.SetWorkerPermissionsAsync(caller, request.PropertyId,
            [new ChemicalSetWorkerPermissionCommand(request.WorkerId, FromProto(request.Flags))])).ConfigureAwait(false);
        return ToProto(workers.Single(w => w.WorkerId == request.WorkerId));
    }

    public override async Task<ChemicalSettings> GetPropertySettings(ChemicalPropertySettingsRequest request, ServerCallContext context)
    {
        var caller = await ResolveCallerAsync().ConfigureAwait(false);
        return ToProto(await RunAsync(() => inventory.GetSettingsAsync(caller, request.PropertyId)).ConfigureAwait(false));
    }

    public override async Task<ChemicalSettings> SetPropertySettings(ChemicalSetPropertySettingsRequest request, ServerCallContext context)
    {
        var caller = await ResolveCallerAsync().ConfigureAwait(false);
        var command = new ChemicalSetSettingsCommand(request.PropertyId, request.StockEnabled, request.DigestRecipients.ToList());
        return ToProto(await RunAsync(() => inventory.SetSettingsAsync(caller, command)).ConfigureAwait(false));
    }

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

    private static async Task<T> RunAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action().ConfigureAwait(false);
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

    /// <summary>One meta message, 128 KB chunks from <paramref name="offset"/>, one trailer with the whole file's md5.</summary>
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

    /// <summary>Client-stream uploads: exactly one meta message first, then byte chunks only.</summary>
    private static async Task<(TMeta Meta, byte[] Content)> ReadMetaThenBytesAsync<TChunk, TMeta>(
        IAsyncStreamReader<TChunk> requestStream, Func<TChunk, TMeta> metaOf, Func<TChunk, ByteString> bytesOf,
        CancellationToken cancellationToken)
        where TMeta : class
    {
        if (!await requestStream.MoveNext(cancellationToken).ConfigureAwait(false)
            || metaOf(requestStream.Current) is not { } meta)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "The first message must carry meta."));
        }

        using var buffer = new MemoryStream();
        while (await requestStream.MoveNext(cancellationToken).ConfigureAwait(false))
        {
            var bytes = bytesOf(requestStream.Current)
                        ?? throw new RpcException(new Status(StatusCode.InvalidArgument, "Only the first message may carry meta."));
            if (buffer.Length + bytes.Length > MaxUploadBytes)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, $"The upload exceeds {MaxUploadBytes / (1024 * 1024)} MB."));
            }

            bytes.WriteTo(buffer);
        }

        return (meta, buffer.ToArray());
    }
}
