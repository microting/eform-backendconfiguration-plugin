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
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Grpc.Chemicals;
using BackendConfiguration.Pn.Infrastructure.Models.Chemicals;
using BackendConfiguration.Pn.Services.ChemicalInventoryService;
using BackendConfiguration.Pn.Services.GrpcServices;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Test.GrpcServices;

/// <summary>
/// Wire ⇄ model mapping and status-code mapping of ChemicalsGrpcService with
/// IChemicalInventoryService faked. Permission rules themselves are pinned by
/// the integration matrix (ChemicalsGrpcPermissionTests).
/// </summary>
[TestFixture]
public class ChemicalsGrpcServiceMappingTests
{
    private const int UserId = 3;
    private const int WorkerId = 42;

    private IChemicalInventoryService _inventory;

    private ChemicalsGrpcService CreateSut(int workerId = WorkerId)
    {
        _inventory = Substitute.For<IChemicalInventoryService>();
        var resolver = Substitute.For<IGrpcSiteResolver>();
        resolver.GetSdkSiteIdAsync().Returns(workerId);
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(UserId);
        return new ChemicalsGrpcService(_inventory, resolver, userService, NullLogger<ChemicalsGrpcService>.Instance);
    }

    private static ServerCallContext Context() => Substitute.For<ServerCallContext>();

    private static ChemicalCaller Caller => ChemicalCaller.App(UserId, WorkerId);

    private static ChemicalPlacementModel Placement(int id = 7, DateTime? removedAt = null) => new(
        id, 11, 1, 5, null, "Hylde 2", UserId, "User", new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc),
        removedAt.HasValue ? UserId : null, removedAt.HasValue ? "User" : "", removedAt,
        removedAt.HasValue ? ChemicalRemovalReasonEnum.Used : null, "", null, 2.5m, ChemicalStockUnitEnum.L,
        new DateTime(2026, 9, 2, 8, 0, 0, DateTimeKind.Utc));

    private static ChemicalPlacementChangeModel Change(params ChemicalPlacementModel[] placements) => new(placements, [], []);

    [Test]
    public async Task NoResolvableWorker_IsUnauthenticated()
    {
        var sut = CreateSut(workerId: 0);
        var ex = await Assert.ThrowsAsync<RpcException>(async () => await sut.GetMyInventory(new ChemicalInventoryRequest(), Context()));
        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.Unauthenticated));
    }

    [Test]
    public async Task GetMyInventory_ForwardsTheTokenAndMapsRows()
    {
        var sut = CreateSut();
        _inventory.GetInventoryAsync(Caller, "v1:123").Returns(new ChemicalInventoryModel(
            [new ChemicalPropertyAccessModel(1, "Gården", ChemicalPermissionFlagsModel.All, true)],
            [], [Placement()], [], [], "v1:456", false));

        var response = await sut.GetMyInventory(new ChemicalInventoryRequest { Since = "v1:123" }, Context());

        Assert.That(response.SyncToken, Is.EqualTo("v1:456"));
        Assert.That(response.Full, Is.False);
        Assert.That(response.Properties.Single().Permissions.Admin, Is.True);
        var placement = response.Placements.Single();
        Assert.That(placement.BalanceMilli, Is.EqualTo(2500));
        Assert.That(placement.Unit, Is.EqualTo(ChemicalStockUnit.L));
        Assert.That(placement.ProductId, Is.EqualTo(0));
        Assert.That(placement.RemovedAt, Is.Null);
        Assert.That(placement.RemovalReason, Is.EqualTo(ChemicalRemovalReason.Unspecified));
    }

    private static IEnumerable<TestCaseData> Failures()
    {
        yield return new TestCaseData(new ChemicalNotFoundException("boom"), StatusCode.NotFound);
        yield return new TestCaseData(new ChemicalPermissionDeniedException("boom"), StatusCode.PermissionDenied);
        yield return new TestCaseData(new ChemicalPreconditionException("boom"), StatusCode.FailedPrecondition);
        yield return new TestCaseData(new ChemicalConflictException("boom"), StatusCode.AlreadyExists);
        yield return new TestCaseData(new ChemicalUnavailableException("boom"), StatusCode.Unavailable);
        yield return new TestCaseData(new ArgumentException("boom"), StatusCode.InvalidArgument);
    }

    [TestCaseSource(nameof(Failures))]
    public async Task TypedFailures_MapToStatusCodes(Exception exception, StatusCode expected)
    {
        var sut = CreateSut();
        _inventory.ArchiveLocationAsync(Caller, 9).ThrowsAsync(exception);

        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
            await sut.ArchiveLocation(new ChemicalArchiveLocationRequest { LocationId = 9 }, Context()));
        Assert.That(ex!.StatusCode, Is.EqualTo(expected));
    }

    [Test]
    public async Task GetSdsPdf_StreamsMetaChunksAndAWholeFileMd5()
    {
        var sut = CreateSut();
        var content = Enumerable.Range(0, 300_000).Select(i => (byte)(i % 251)).ToArray();
        _inventory.GetSdsPdfAsync(Caller, "abc").Returns(content);
        var writer = new FakeServerStreamWriter<ChemicalFileChunk>();

        await sut.GetSdsPdf(new ChemicalSdsPdfRequest { FileName = "abc", Offset = 0 }, writer, Context());

        Assert.That(writer.Written[0].Meta.TotalBytes, Is.EqualTo(300_000));
        Assert.That(writer.Written[0].Meta.ContentType, Is.EqualTo("application/pdf"));
        var chunks = writer.Written.Where(c => c.KindCase == ChemicalFileChunk.KindOneofCase.Chunk).Select(c => c.Chunk.Length).ToList();
        Assert.That(chunks, Is.EqualTo(new[] { 131_072, 131_072, 37_856 }));
        Assert.That(writer.Written[^1].Trailer.Md5, Is.EqualTo(Convert.ToHexStringLower(MD5.HashData(content))));
    }

    [Test]
    public async Task GetSdsPdf_ResumesFromOffset_EndOffsetSendsNoBytes_PastTheEndIsOutOfRange()
    {
        var sut = CreateSut();
        var content = Enumerable.Range(0, 1000).Select(i => (byte)(i % 251)).ToArray();
        _inventory.GetSdsPdfAsync(Caller, "abc").Returns(content);

        var resumed = new FakeServerStreamWriter<ChemicalFileChunk>();
        await sut.GetSdsPdf(new ChemicalSdsPdfRequest { FileName = "abc", Offset = 600 }, resumed, Context());
        Assert.That(resumed.Written[0].Meta.Offset, Is.EqualTo(600));
        Assert.That(resumed.Written[0].Meta.TotalBytes, Is.EqualTo(1000));
        var resumedBytes = resumed.Written.Where(c => c.KindCase == ChemicalFileChunk.KindOneofCase.Chunk)
            .SelectMany(c => c.Chunk.ToByteArray()).ToArray();
        Assert.That(resumedBytes, Is.EqualTo(content[600..]));
        Assert.That(resumed.Written[^1].Trailer.Md5, Is.EqualTo(Convert.ToHexStringLower(MD5.HashData(content))),
            "the trailer md5 covers the whole file, not the resumed tail");

        var atEnd = new FakeServerStreamWriter<ChemicalFileChunk>();
        await sut.GetSdsPdf(new ChemicalSdsPdfRequest { FileName = "abc", Offset = 1000 }, atEnd, Context());
        Assert.That(atEnd.Written.Select(c => c.KindCase),
            Is.EqualTo(new[] { ChemicalFileChunk.KindOneofCase.Meta, ChemicalFileChunk.KindOneofCase.Trailer }));

        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
            await sut.GetSdsPdf(new ChemicalSdsPdfRequest { FileName = "abc", Offset = 1001 }, new FakeServerStreamWriter<ChemicalFileChunk>(), Context()));
        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.OutOfRange));
    }

    [Test]
    public async Task UploadLocationPhoto_ConcatenatesChunksAfterMeta()
    {
        var sut = CreateSut();
        _inventory.SaveLocationPhotoAsync(Caller, 11, Arg.Any<byte[]>(), "image/jpeg")
            .Returns(new ChemicalLocationModel(11, 1, "Kemirum", "", "x.jpg", 1, false, DateTime.UtcNow));
        var reader = new FakeAsyncStreamReader<ChemicalLocationPhotoUploadChunk>([
            new() { Meta = new ChemicalLocationPhotoMeta { LocationId = 11, ContentType = "image/jpeg" } },
            new() { Chunk = ByteString.CopyFrom(1, 2) },
            new() { Chunk = ByteString.CopyFrom(3) },
        ]);

        var response = await sut.UploadLocationPhoto(reader, Context());

        Assert.That(response.Location.PhotoFileName, Is.EqualTo("x.jpg"));
        await _inventory.Received(1).SaveLocationPhotoAsync(Caller, 11,
            Arg.Is<byte[]>(b => b.SequenceEqual(new byte[] { 1, 2, 3 })), "image/jpeg");
    }

    [Test]
    public async Task UploadLocationPhoto_WithoutMetaFirst_IsInvalidArgument()
    {
        var sut = CreateSut();
        var reader = new FakeAsyncStreamReader<ChemicalLocationPhotoUploadChunk>([new() { Chunk = ByteString.CopyFrom(1) }]);

        var ex = await Assert.ThrowsAsync<RpcException>(async () => await sut.UploadLocationPhoto(reader, Context()));
        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.InvalidArgument));
    }

    [Test]
    public async Task UploadLocationPhoto_OverThePhotoLimit_IsInvalidArgument_BeforeTheInventorySeesIt()
    {
        var sut = CreateSut();
        var reader = new FakeAsyncStreamReader<ChemicalLocationPhotoUploadChunk>([
            new() { Meta = new ChemicalLocationPhotoMeta { LocationId = 11, ContentType = "image/jpeg" } },
            new() { Chunk = ByteString.CopyFrom(new byte[ChemicalInventoryService.MaxPhotoBytes]) },
            new() { Chunk = ByteString.CopyFrom(1) },
        ]);

        var ex = await Assert.ThrowsAsync<RpcException>(async () => await sut.UploadLocationPhoto(reader, Context()));

        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.InvalidArgument));
        await _inventory.DidNotReceiveWithAnyArgs().SaveLocationPhotoAsync(default!, default, default!, default!);
    }

    [Test]
    public async Task UploadLocationPhoto_WithoutManageLocations_IsDenied_BeforeTheBytesAreRead()
    {
        var sut = CreateSut();
        _inventory.RequireCanManageLocationAsync(Caller, 11).ThrowsAsync(new ChemicalPermissionDeniedException("no"));
        var reader = new FakeAsyncStreamReader<ChemicalLocationPhotoUploadChunk>([
            new() { Meta = new ChemicalLocationPhotoMeta { LocationId = 11, ContentType = "image/jpeg" } },
            new() { Chunk = ByteString.CopyFrom(1, 2) },
            new() { Chunk = ByteString.CopyFrom(3) },
        ]);

        var ex = await Assert.ThrowsAsync<RpcException>(async () => await sut.UploadLocationPhoto(reader, Context()));

        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.PermissionDenied));
        Assert.That(reader.Remaining, Is.EqualTo(2), "only the meta message may be read before the permission check");
        await _inventory.DidNotReceiveWithAnyArgs().SaveLocationPhotoAsync(default!, default, default!, default!);
    }

    [Test]
    public async Task SuggestBarcode_IsUnimplemented_AndNeverTouchesTheInventory()
    {
        var sut = CreateSut();
        var reader = new FakeAsyncStreamReader<ChemicalSuggestBarcodeChunk>([
            new() { Meta = new ChemicalSuggestBarcodeMeta { Barcode = "5701234567892" } },
        ]);

        var ex = await Assert.ThrowsAsync<RpcException>(async () => await sut.SuggestBarcode(reader, Context()));

        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.Unimplemented));
        Assert.That(_inventory.ReceivedCalls(), Is.Empty);
    }

    [Test]
    public async Task AddStockEntry_MapsPresenceOfAmountAndUnits()
    {
        var sut = CreateSut();
        _inventory.AddStockEntryAsync(Caller, Arg.Any<ChemicalAddStockEntryCommand>()).Returns(Change(Placement()));

        await sut.AddStockEntry(new ChemicalAddStockEntryRequest
        {
            PlacementId = 7, Kind = ChemicalStockEntryKind.Adjusted,
            Amount = new ChemicalStockAmount { AmountMilli = 0, Unit = ChemicalStockUnit.Kg },
        }, Context());
        await sut.AddStockEntry(new ChemicalAddStockEntryRequest
        {
            PlacementId = 7, Kind = ChemicalStockEntryKind.Received,
            Amount = new ChemicalStockAmount { ContainerSizeMilli = 5000, ContainerCount = 4, Unit = ChemicalStockUnit.L },
        }, Context());

        await _inventory.Received(1).AddStockEntryAsync(Caller, Arg.Is<ChemicalAddStockEntryCommand>(c =>
            c.Kind == ChemicalStockEntryKindEnum.Adjusted && c.Amount.Amount == 0m && c.Amount.Unit == ChemicalStockUnitEnum.Kg));
        await _inventory.Received(1).AddStockEntryAsync(Caller, Arg.Is<ChemicalAddStockEntryCommand>(c =>
            c.Kind == ChemicalStockEntryKindEnum.Received && c.Amount.Amount == null
            && c.Amount.ContainerSize == 5m && c.Amount.ContainerCount == 4));
    }

    [Test]
    public async Task MovePlacement_ZeroAmountMeansEverything()
    {
        var sut = CreateSut();
        _inventory.MovePlacementAsync(Caller, Arg.Any<ChemicalMovePlacementCommand>()).Returns(Change(Placement()));

        await sut.MovePlacement(new ChemicalMovePlacementRequest { PlacementId = 7, TargetLocationId = 12 }, Context());
        await sut.MovePlacement(new ChemicalMovePlacementRequest { PlacementId = 7, TargetLocationId = 12, AmountMilli = 500 }, Context());

        await _inventory.Received(1).MovePlacementAsync(Caller, new ChemicalMovePlacementCommand(7, 12, "", null));
        await _inventory.Received(1).MovePlacementAsync(Caller, new ChemicalMovePlacementCommand(7, 12, "", 0.5m));
    }

    [Test]
    public async Task SetWorkerPermission_ReturnsTheChangedWorker()
    {
        var sut = CreateSut();
        var flags = ChemicalPermissionFlagsModel.None with { View = true };
        _inventory.SetWorkerPermissionsAsync(Caller, 1, Arg.Any<IReadOnlyList<ChemicalSetWorkerPermissionCommand>>())
            .Returns([new ChemicalWorkerPermissionModel(8, "Anna", flags), new ChemicalWorkerPermissionModel(9, "Bo", ChemicalPermissionFlagsModel.None)]);

        var entry = await sut.SetWorkerPermission(new ChemicalSetWorkerPermissionRequest
        {
            PropertyId = 1, WorkerId = 8, Flags = new ChemicalPermissionFlags { View = true },
        }, Context());

        Assert.That(entry.WorkerId, Is.EqualTo(8));
        Assert.That(entry.WorkerName, Is.EqualTo("Anna"));
        Assert.That(entry.Flags.View, Is.True);
    }

    [Test]
    public async Task AddStockEntry_NegativeContainerSizeAndCount_ReachTheInventoryToBeRejected()
    {
        var sut = CreateSut();
        _inventory.AddStockEntryAsync(Caller, Arg.Any<ChemicalAddStockEntryCommand>()).Returns(Change(Placement()));

        await sut.AddStockEntry(new ChemicalAddStockEntryRequest
        {
            PlacementId = 7, Kind = ChemicalStockEntryKind.Received,
            Amount = new ChemicalStockAmount { ContainerSizeMilli = -5000, ContainerCount = -2, Unit = ChemicalStockUnit.L },
        }, Context());

        await _inventory.Received(1).AddStockEntryAsync(Caller, Arg.Is<ChemicalAddStockEntryCommand>(c =>
            c.Amount.ContainerSize == -5m && c.Amount.ContainerCount == -2));
    }

    [Test]
    public async Task MalformedOrOutOfRangeTimestamps_AreInvalidArgument()
    {
        var sut = CreateSut();
        Timestamp[] bad = [new() { Seconds = long.MaxValue }, new() { Seconds = 0, Nanos = -1 }];

        foreach (var timestamp in bad)
        {
            var removed = await Assert.ThrowsAsync<RpcException>(async () => await sut.RemovePlacement(new ChemicalRemovePlacementRequest
            {
                PlacementId = 7, Reason = ChemicalRemovalReason.Used, RemovedAt = timestamp,
            }, Context()));
            var stocked = await Assert.ThrowsAsync<RpcException>(async () => await sut.AddStockEntry(new ChemicalAddStockEntryRequest
            {
                PlacementId = 7, Kind = ChemicalStockEntryKind.Received,
                Amount = new ChemicalStockAmount { AmountMilli = 1000, Unit = ChemicalStockUnit.L, At = timestamp },
            }, Context()));

            Assert.That(removed!.StatusCode, Is.EqualTo(StatusCode.InvalidArgument), $"RemovedAt {timestamp.Seconds}s {timestamp.Nanos}ns");
            Assert.That(stocked!.StatusCode, Is.EqualTo(StatusCode.InvalidArgument), $"At {timestamp.Seconds}s {timestamp.Nanos}ns");
        }

        Assert.That(_inventory.ReceivedCalls(), Is.Empty);
    }
}
