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


using BackendConfiguration.Pn.Grpc.Chemicals;
using BackendConfiguration.Pn.Infrastructure.Models.Chemicals;
using BackendConfiguration.Pn.Services.GrpcServices;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using NSubstitute;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// Spec §15: every RPC is tested with and without the permission it needs,
/// end to end through ChemicalsGrpcService and the real ChemicalInventoryService.
/// "Without" grants every other flag (Admin off); "with" grants only that flag.
/// SuggestBarcode is deliberately unimplemented (D3) and has no row.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class ChemicalsGrpcPermissionTests : ChemicalTestBase
{
    private sealed record Scenario(int PropertyId, int WorkerId, int OtherWorkerId, int LocationId, int EmptyLocationId,
        int PlacementId, int ChemicalId, string SdsFileName, string Barcode);

    private delegate Task Rpc(ChemicalsGrpcService sut, Scenario s);

    private static ServerCallContext Context() => Substitute.For<ServerCallContext>();

    private static readonly Dictionary<string, (ChemicalPermission Required, Rpc Call)> Rpcs = new()
    {
        ["GetSdsPdf"] = (ChemicalPermission.View, (sut, s) =>
            sut.GetSdsPdf(new ChemicalSdsPdfRequest { FileName = s.SdsFileName }, new FakeServerStreamWriter<ChemicalFileChunk>(), Context())),
        ["GetLocationPhoto"] = (ChemicalPermission.View, (sut, s) =>
            sut.GetLocationPhoto(new ChemicalLocationPhotoRequest { LocationId = s.LocationId }, new FakeServerStreamWriter<ChemicalFileChunk>(), Context())),
        ["LookupBarcode"] = (ChemicalPermission.View, (sut, s) =>
            sut.LookupBarcode(new ChemicalLookupBarcodeRequest { Barcode = s.Barcode }, Context())),
        ["SearchRegister"] = (ChemicalPermission.View, (sut, _) =>
            sut.SearchRegister(new ChemicalSearchRegisterRequest { Query = "Matrix" }, Context())),
        ["RegisterPlacement"] = (ChemicalPermission.Register, (sut, s) =>
            sut.RegisterPlacement(new ChemicalRegisterPlacementRequest { LocationId = s.LocationId, ChemicalId = s.ChemicalId }, Context())),
        ["MovePlacement"] = (ChemicalPermission.Register, (sut, s) =>
            sut.MovePlacement(new ChemicalMovePlacementRequest { PlacementId = s.PlacementId, TargetLocationId = s.EmptyLocationId }, Context())),
        ["RemovePlacement"] = (ChemicalPermission.Remove, (sut, s) =>
            sut.RemovePlacement(new ChemicalRemovePlacementRequest { PlacementId = s.PlacementId, Reason = ChemicalRemovalReason.Used }, Context())),
        ["UpdatePlacementNote"] = (ChemicalPermission.Register, (sut, s) =>
            sut.UpdatePlacementNote(new ChemicalUpdatePlacementNoteRequest { PlacementId = s.PlacementId, PlacementNote = "Hylde 3" }, Context())),
        ["AddStockEntry"] = (ChemicalPermission.Stock, (sut, s) =>
            sut.AddStockEntry(new ChemicalAddStockEntryRequest
            {
                PlacementId = s.PlacementId, Kind = ChemicalStockEntryKind.Received,
                Amount = new ChemicalStockAmount { AmountMilli = 1000, Unit = ChemicalStockUnit.L },
            }, Context())),
        ["CreateLocation"] = (ChemicalPermission.ManageLocations, (sut, s) =>
            sut.CreateLocation(new ChemicalCreateLocationRequest { PropertyId = s.PropertyId, Name = Guid.NewGuid().ToString("N") }, Context())),
        ["UpdateLocation"] = (ChemicalPermission.ManageLocations, (sut, s) =>
            sut.UpdateLocation(new ChemicalUpdateLocationRequest { LocationId = s.EmptyLocationId, Name = Guid.NewGuid().ToString("N") }, Context())),
        ["ArchiveLocation"] = (ChemicalPermission.ManageLocations, (sut, s) =>
            sut.ArchiveLocation(new ChemicalArchiveLocationRequest { LocationId = s.EmptyLocationId }, Context())),
        ["ReorderLocations"] = (ChemicalPermission.ManageLocations, (sut, s) =>
            sut.ReorderLocations(new ChemicalReorderLocationsRequest
            {
                PropertyId = s.PropertyId, LocationIds = { s.EmptyLocationId, s.LocationId },
            }, Context())),
        ["UploadLocationPhoto"] = (ChemicalPermission.ManageLocations, (sut, s) =>
            sut.UploadLocationPhoto(new FakeAsyncStreamReader<ChemicalLocationPhotoUploadChunk>([
                new() { Meta = new ChemicalLocationPhotoMeta { LocationId = s.EmptyLocationId, ContentType = "image/png" } },
                new() { Chunk = ByteString.CopyFrom(1, 2, 3) },
            ]), Context())),
        ["ListWorkerPermissions"] = (ChemicalPermission.Admin, (sut, s) =>
            sut.ListWorkerPermissions(new ChemicalListWorkerPermissionsRequest { PropertyId = s.PropertyId }, Context())),
        ["SetWorkerPermission"] = (ChemicalPermission.Admin, (sut, s) =>
            sut.SetWorkerPermission(new ChemicalSetWorkerPermissionRequest
            {
                PropertyId = s.PropertyId, WorkerId = s.OtherWorkerId, Flags = new ChemicalPermissionFlags { View = true },
            }, Context())),
        ["GetPropertySettings"] = (ChemicalPermission.Admin, (sut, s) =>
            sut.GetPropertySettings(new ChemicalPropertySettingsRequest { PropertyId = s.PropertyId }, Context())),
        ["SetPropertySettings"] = (ChemicalPermission.Admin, (sut, s) =>
            sut.SetPropertySettings(new ChemicalSetPropertySettingsRequest { PropertyId = s.PropertyId, StockEnabled = true }, Context())),
    };

    private static IEnumerable<string> RpcNames() => Rpcs.Keys;

    private static ChemicalPermissionFlagsModel Only(ChemicalPermission permission) =>
        With(ChemicalPermissionFlagsModel.None, permission, true);

    private static ChemicalPermissionFlagsModel AllExcept(ChemicalPermission permission) =>
        With(ChemicalPermissionFlagsModel.All, permission, false) with { Admin = false };

    private static ChemicalPermissionFlagsModel With(ChemicalPermissionFlagsModel flags, ChemicalPermission permission, bool value) =>
        permission switch
        {
            ChemicalPermission.View => flags with { View = value },
            ChemicalPermission.Register => flags with { Register = value },
            ChemicalPermission.Remove => flags with { Remove = value },
            ChemicalPermission.Stock => flags with { Stock = value },
            ChemicalPermission.ManageLocations => flags with { ManageLocations = value },
            ChemicalPermission.Admin => flags with { Admin = value },
            _ => throw new ArgumentOutOfRangeException(nameof(permission), permission, "No flag for this permission."),
        };

    private async Task<Scenario> ArrangeAsync(ChemicalPermissionFlagsModel flags)
    {
        var property = await CreatePropertyAsync();
        var worker = await AddWorkerAsync(property.Id);
        var other = await AddWorkerAsync(property.Id);
        await GrantAsync(property.Id, worker, flags);
        await EnableStockAsync(property.Id);
        var location = await CreateLocationAsync(property.Id, sortOrder: 1);
        var empty = await CreateLocationAsync(property.Id, sortOrder: 2);
        var sds = Guid.NewGuid().ToString("N");
        var barcode = ChemicalRegisterSeed.RandomGtin(13);
        var chemical = await ChemicalRegisterSeed.AddChemicalAsync(ChemicalsDbContext!, "Matrix", "8-888", barcode: barcode, sdsFileName: sds);
        var placement = await CreatePlacementAsync(location.Id, chemical.ChemicalId);
        await new ChemicalStockEntry
        {
            PlacementId = placement.Id, Kind = ChemicalStockEntryKindEnum.Received, Unit = ChemicalStockUnitEnum.L, Amount = 2m,
            ByUserId = TestUserId, At = DateTime.UtcNow.AddDays(-1), CreatedByUserId = TestUserId, UpdatedByUserId = TestUserId,
        }.Create(BackendConfigurationPnDbContext!);

        var photo = $"chemical-location-{location.Id}-matrix.jpg";
        await PhotoStorage.PutAsync(photo, new MemoryStream([1, 2, 3]));
        location.PhotoFileName = photo;
        await location.Update(BackendConfigurationPnDbContext!);

        ChemicalBase.DownloadSdsAsync(sds, Arg.Any<CancellationToken>()).Returns(new byte[] { 37, 80, 68, 70 });

        return new Scenario(property.Id, worker, other, location.Id, empty.Id, placement.Id, chemical.ChemicalId, sds, barcode);
    }

    private ChemicalsGrpcService CreateSut(int workerId)
    {
        var resolver = Substitute.For<IGrpcSiteResolver>();
        resolver.GetSdkSiteIdAsync().Returns(workerId);
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(TestUserId);
        return new ChemicalsGrpcService(CreateInventoryService(), resolver, userService, NullLogger<ChemicalsGrpcService>.Instance);
    }

    [TestCaseSource(nameof(RpcNames))]
    public async Task Rpc_WithoutItsFlag_IsPermissionDenied(string rpc)
    {
        var (required, call) = Rpcs[rpc];
        var scenario = await ArrangeAsync(AllExcept(required));

        var ex = await Assert.ThrowsAsync<RpcException>(async () => await call(CreateSut(scenario.WorkerId), scenario));

        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.PermissionDenied));
    }

    [TestCaseSource(nameof(RpcNames))]
    public async Task Rpc_WithOnlyItsFlag_Succeeds(string rpc)
    {
        var (required, call) = Rpcs[rpc];
        var scenario = await ArrangeAsync(Only(required));

        Assert.That(async () => await call(CreateSut(scenario.WorkerId), scenario), Throws.Nothing);
    }

    [Test]
    public async Task GetMyInventory_WithoutView_ReturnsNoProperties_WithViewReturnsTheProperty()
    {
        var hidden = await ArrangeAsync(AllExcept(ChemicalPermission.View));
        var visible = await ArrangeAsync(Only(ChemicalPermission.View));

        var none = await CreateSut(hidden.WorkerId).GetMyInventory(new ChemicalInventoryRequest(), Context());
        var one = await CreateSut(visible.WorkerId).GetMyInventory(new ChemicalInventoryRequest(), Context());

        Assert.That(none.Properties, Is.Empty);
        Assert.That(one.Properties.Select(p => p.PropertyId), Is.EqualTo(new[] { visible.PropertyId }));
        Assert.That(one.Placements.Single().BalanceMilli, Is.EqualTo(2000));
    }

    [Test]
    public async Task ReorderLocations_WithManageLocations_StoresTheOrder_AndKeepsNamesAndDescriptions()
    {
        var s = await ArrangeAsync(Only(ChemicalPermission.ManageLocations));
        var before = BackendConfigurationPnDbContext!.ChemicalLocations.AsNoTracking()
            .Where(l => l.PropertyId == s.PropertyId).ToList()
            .ToDictionary(l => l.Id, l => (l.Name, l.Description ?? "", l.PhotoFileName ?? ""));

        var response = await CreateSut(s.WorkerId).ReorderLocations(new ChemicalReorderLocationsRequest
        {
            PropertyId = s.PropertyId, LocationIds = { s.EmptyLocationId, s.LocationId },
        }, Context());

        Assert.That(response.Locations.Select(l => (l.Id, l.SortOrder)),
            Is.EqualTo(new[] { (s.EmptyLocationId, 1), (s.LocationId, 2) }));
        Assert.That(response.Locations.ToDictionary(l => l.Id, l => (l.Name, l.Description, l.PhotoFileName)), Is.EqualTo(before));
        var stored = BackendConfigurationPnDbContext.ChemicalLocations.AsNoTracking()
            .Where(l => l.PropertyId == s.PropertyId).OrderBy(l => l.SortOrder).Select(l => l.Id).ToList();
        Assert.That(stored, Is.EqualTo(new[] { s.EmptyLocationId, s.LocationId }));
    }

    [Test]
    public async Task ReorderLocations_IncompleteDuplicateForeignOrArchivedIds_AreInvalidArgument_AndChangeNothing()
    {
        var s = await ArrangeAsync(Only(ChemicalPermission.ManageLocations));
        var foreign = await CreateLocationAsync((await CreatePropertyAsync()).Id);
        var archived = await CreateLocationAsync(s.PropertyId, sortOrder: 3);
        await archived.Delete(BackendConfigurationPnDbContext!);
        var sut = CreateSut(s.WorkerId);
        int[][] bad =
        [
            [s.LocationId],
            [s.EmptyLocationId, s.EmptyLocationId],
            [s.EmptyLocationId, foreign.Id],
            [s.EmptyLocationId, s.LocationId, foreign.Id],
            [s.EmptyLocationId, s.LocationId, archived.Id],
            [s.EmptyLocationId, archived.Id],
        ];

        foreach (var ids in bad)
        {
            var request = new ChemicalReorderLocationsRequest { PropertyId = s.PropertyId };
            request.LocationIds.AddRange(ids);
            var ex = await Assert.ThrowsAsync<RpcException>(async () => await sut.ReorderLocations(request, Context()));
            Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.InvalidArgument), string.Join(",", ids));
        }

        var stored = BackendConfigurationPnDbContext!.ChemicalLocations.AsNoTracking()
            .Where(l => l.PropertyId == s.PropertyId).OrderBy(l => l.SortOrder).Select(l => l.Id).ToList();
        Assert.That(stored, Is.EqualTo(new[] { s.LocationId, s.EmptyLocationId, archived.Id }));
    }

    [Test]
    public async Task AllFlagsOnOneProperty_GrantNothingOnAnother()
    {
        var mine = await ArrangeAsync(ChemicalPermissionFlagsModel.All);
        var theirs = await ArrangeAsync(ChemicalPermissionFlagsModel.All);
        var sut = CreateSut(mine.WorkerId);

        Func<Task>[] calls =
        [
            () => sut.RemovePlacement(new ChemicalRemovePlacementRequest { PlacementId = theirs.PlacementId, Reason = ChemicalRemovalReason.Used }, Context()),
            () => sut.ArchiveLocation(new ChemicalArchiveLocationRequest { LocationId = theirs.EmptyLocationId }, Context()),
            () => sut.ReorderLocations(new ChemicalReorderLocationsRequest
            {
                PropertyId = theirs.PropertyId, LocationIds = { theirs.EmptyLocationId, theirs.LocationId },
            }, Context()),
            () => sut.SetPropertySettings(new ChemicalSetPropertySettingsRequest { PropertyId = theirs.PropertyId, StockEnabled = false }, Context()),
        ];

        foreach (var call in calls)
        {
            var ex = await Assert.ThrowsAsync<RpcException>(async () => await call());
            Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.PermissionDenied));
        }
    }
}
