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

    private static ChemicalPlacementModel Placement(int id = 7, DateTime? removedAt = null, int? writeOffEntryId = null) => new(
        id, 11, 1, 5, null, "Hylde 2", UserId, "User", new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc),
        removedAt.HasValue ? UserId : null, removedAt.HasValue ? "User" : "", removedAt,
        removedAt.HasValue ? ChemicalRemovalReasonEnum.Used : null, "", null, 2.5m, ChemicalStockUnitEnum.L,
        new DateTime(2026, 9, 2, 8, 0, 0, DateTimeKind.Utc), writeOffEntryId);

    private static ChemicalStockEntryModel Entry(int id, ChemicalStockEntryKindEnum kind, decimal amount, decimal balanceAfter,
        ChemicalStockEntryOriginEnum origin, int? counterpart = null) => new(
        id, 7, kind, null, ChemicalStockUnitEnum.L, amount, null, "", "", UserId, "User",
        new DateTime(2026, 9, 3, 8, 0, 0, DateTimeKind.Utc), balanceAfter, origin, counterpart);

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

    [Test]
    public async Task StockEntries_CarryBalanceAfterOriginAndCounterpart()
    {
        var sut = CreateSut();
        _inventory.GetInventoryAsync(Caller, "").Returns(new ChemicalInventoryModel([], [], [], [
            Entry(30, ChemicalStockEntryKindEnum.Received, 2m, 2m, ChemicalStockEntryOriginEnum.Manual),
            Entry(31, ChemicalStockEntryKindEnum.MovedOut, -0.75m, 1.25m, ChemicalStockEntryOriginEnum.Move, counterpart: 9),
            Entry(32, ChemicalStockEntryKindEnum.Consumed, -1.25m, 0m, ChemicalStockEntryOriginEnum.RemovalWriteOff),
        ], [], "v1:1", true));

        var entries = (await sut.GetMyInventory(new ChemicalInventoryRequest(), Context())).StockEntries;

        Assert.That(entries.Select(e => (e.HasBalanceAfterMilli, e.BalanceAfterMilli)),
            Is.EqualTo(new[] { (true, 2000L), (true, 1250L), (true, 0L) }), "a zero balance is still sent (presence)");
        Assert.That(entries.Select(e => e.Origin), Is.EqualTo(new[]
        {
            ChemicalStockEntryOrigin.Manual, ChemicalStockEntryOrigin.Move, ChemicalStockEntryOrigin.RemovalWriteOff,
        }));
        Assert.That(entries.Select(e => e.CounterpartPlacementId), Is.EqualTo(new[] { 0, 9, 0 }));
    }

    [Test]
    public async Task Placements_CarryTheWriteOffEntryId_ZeroForNone()
    {
        var sut = CreateSut();
        var removedAt = new DateTime(2026, 9, 4, 8, 0, 0, DateTimeKind.Utc);
        _inventory.RemovePlacementAsync(Caller, Arg.Any<ChemicalRemovePlacementCommand>())
            .Returns(Change(Placement(7, removedAt, writeOffEntryId: 32), Placement(8)));

        var response = await sut.RemovePlacement(new ChemicalRemovePlacementRequest { PlacementId = 7, Reason = ChemicalRemovalReason.Used }, Context());

        Assert.That(response.Placements.Select(p => (p.Id, p.WriteOffEntryId)), Is.EqualTo(new[] { (7, 32), (8, 0) }));
    }

    [Test]
    public async Task ReorderLocations_ForwardsPropertyAndOrder_AndReturnsTheLocations()
    {
        var sut = CreateSut();
        var at = new DateTime(2026, 9, 5, 8, 0, 0, DateTimeKind.Utc);
        _inventory.ReorderLocationsAsync(Caller, 1, Arg.Any<IReadOnlyList<int>>()).Returns([
            new ChemicalLocationModel(12, 1, "Lade", "Bag døren", "", 1, false, at),
            new ChemicalLocationModel(11, 1, "Kemirum", "", "p.jpg", 2, false, at),
        ]);

        var response = await sut.ReorderLocations(new ChemicalReorderLocationsRequest { PropertyId = 1, LocationIds = { 12, 11 } }, Context());

        await _inventory.Received(1).ReorderLocationsAsync(Caller, 1, Arg.Is<IReadOnlyList<int>>(ids => ids.SequenceEqual(new[] { 12, 11 })));
        Assert.That(response.Locations.Select(l => (l.Id, l.SortOrder, l.Name, l.Description)),
            Is.EqualTo(new[] { (12, 1, "Lade", "Bag døren"), (11, 2, "Kemirum", "") }));
        Assert.That(response.Locations[1].PhotoFileName, Is.EqualTo("p.jpg"));
    }

    [Test]
    public async Task ReorderLocations_RefusalsMapLikeEveryOtherRpc()
    {
        var sut = CreateSut();
        _inventory.ReorderLocationsAsync(Caller, 1, Arg.Any<IReadOnlyList<int>>())
            .ThrowsAsync(new ArgumentException("The order must list every active location of the property exactly once."));
        _inventory.ReorderLocationsAsync(Caller, 2, Arg.Any<IReadOnlyList<int>>())
            .ThrowsAsync(new ChemicalPermissionDeniedException("no"));

        var invalid = await Assert.ThrowsAsync<RpcException>(async () =>
            await sut.ReorderLocations(new ChemicalReorderLocationsRequest { PropertyId = 1, LocationIds = { 11 } }, Context()));
        var denied = await Assert.ThrowsAsync<RpcException>(async () =>
            await sut.ReorderLocations(new ChemicalReorderLocationsRequest { PropertyId = 2, LocationIds = { 11 } }, Context()));
        var anonymous = await Assert.ThrowsAsync<RpcException>(async () =>
            await CreateSut(workerId: 0).ReorderLocations(new ChemicalReorderLocationsRequest { PropertyId = 1 }, Context()));

        Assert.That(invalid!.StatusCode, Is.EqualTo(StatusCode.InvalidArgument));
        Assert.That(denied!.StatusCode, Is.EqualTo(StatusCode.PermissionDenied));
        Assert.That(anonymous!.StatusCode, Is.EqualTo(StatusCode.Unauthenticated));
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

    // ---- GS1 Sunrise 2027: QR / DataMatrix / GTIN-14 scans find the product stored as EAN-13 ----

    private const string StoredEan13 = "5701234567899";

    private static ChemicalRegisterEntryModel StoredEntry() => new(
        11, "Roundup", "1-111", 1, "", null, null, null, null, null, [], null, "", [], [], "",
        [new ChemicalProductModel(21, "Roundup 1 L", StoredEan13, "", "")],
        new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc));

    private static readonly string[] Gs1Scans =
    [
        "https://id.gs1.org/01/05701234567899/10/LOT1?17=271231",
        "(01)05701234567899(10)LOT1",
        "05701234567899",
    ];

    private static readonly string[] JunkScans = ["https://example.com/promo", "(01)05701234567892"];

    [TestCaseSource(nameof(Gs1Scans))]
    public async Task LookupBarcode_Gs1Scan_FindsTheProductStoredAsEan13(string scanned)
    {
        var sut = CreateSut();
        _inventory.LookupBarcodeAsync(Caller, StoredEan13).Returns([StoredEntry()]);

        var response = await sut.LookupBarcode(new ChemicalLookupBarcodeRequest { Barcode = scanned }, Context());

        Assert.That(response.Entries.Single().ChemicalId, Is.EqualTo(11));
    }

    [TestCaseSource(nameof(Gs1Scans))]
    public async Task SearchRegister_Gs1Scan_FindsTheProductStoredAsEan13(string scanned)
    {
        var sut = CreateSut();
        _inventory.SearchRegisterAsync(Caller, StoredEan13, 0, 25).Returns(new ChemicalRegisterPageModel([StoredEntry()], 1));

        var response = await sut.SearchRegister(new ChemicalSearchRegisterRequest { Query = scanned, Page = 0, PageSize = 25 }, Context());

        Assert.That(response.Entries.Single().ChemicalId, Is.EqualTo(11));
    }

    [TestCaseSource(nameof(JunkScans))]
    public async Task LookupAndSearch_JunkScan_IsInvalidArgument(string scanned)
    {
        var sut = CreateSut();

        var lookup = await Assert.ThrowsAsync<RpcException>(async () =>
            await sut.LookupBarcode(new ChemicalLookupBarcodeRequest { Barcode = scanned }, Context()));
        var search = await Assert.ThrowsAsync<RpcException>(async () =>
            await sut.SearchRegister(new ChemicalSearchRegisterRequest { Query = scanned, PageSize = 25 }, Context()));

        Assert.That(lookup!.StatusCode, Is.EqualTo(StatusCode.InvalidArgument));
        Assert.That(search!.StatusCode, Is.EqualTo(StatusCode.InvalidArgument));
        Assert.That(_inventory.ReceivedCalls(), Is.Empty);
    }

    [Test]
    public async Task LookupBarcode_JunkScan_WithoutAWorker_IsStillUnauthenticated()
    {
        var sut = CreateSut(workerId: 0);
        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
            await sut.LookupBarcode(new ChemicalLookupBarcodeRequest { Barcode = "https://example.com/promo" }, Context()));
        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.Unauthenticated));
    }

    [Test]
    public async Task LookupBarcode_DigitsWithABadCheckDigit_AreForwardedLiterally()
    {
        var sut = CreateSut();
        _inventory.LookupBarcodeAsync(Caller, "5701234567892").Returns([StoredEntry()]);

        var response = await sut.LookupBarcode(new ChemicalLookupBarcodeRequest { Barcode = " 5701234567892 " }, Context());

        Assert.That(response.Entries.Single().ChemicalId, Is.EqualTo(11));
    }
}
