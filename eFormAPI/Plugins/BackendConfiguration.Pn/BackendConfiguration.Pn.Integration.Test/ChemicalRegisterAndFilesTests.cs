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

using BackendConfiguration.Pn.Infrastructure.Models.Chemicals;
using BackendConfiguration.Pn.Services.ChemicalInventoryService;
using NSubstitute;

namespace BackendConfiguration.Pn.Integration.Test;

[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class ChemicalRegisterAndFilesTests : ChemicalTestBase
{
    private async Task<ChemicalCaller> WorkerAsync(bool view)
    {
        var property = await CreatePropertyAsync();
        var worker = await AddWorkerAsync(property.Id);
        await GrantAsync(property.Id, worker, view
            ? ChemicalPermissionFlagsModel.None with { View = true }
            : ChemicalPermissionFlagsModel.None with { Register = true });
        return ChemicalCaller.App(TestUserId, worker);
    }

    [Test]
    public async Task RegisterWideActions_NeedViewOnSomeProperty()
    {
        var blind = await WorkerAsync(view: false);
        var sut = CreateInventoryService();

        Assert.That(async () => await sut.LookupBarcodeAsync(blind, "5701234567892"), Throws.InstanceOf<ChemicalPermissionDeniedException>());
        Assert.That(async () => await sut.SearchRegisterAsync(blind, "Round", 0, 25), Throws.InstanceOf<ChemicalPermissionDeniedException>());
        Assert.That(async () => await sut.GetSdsPdfAsync(blind, "abc"), Throws.InstanceOf<ChemicalPermissionDeniedException>());
    }

    [Test]
    public async Task LookupAndSearch_ReturnRegisterEntries()
    {
        var caller = await WorkerAsync(view: true);
        var barcode = ChemicalRegisterSeed.RandomDigits(13);
        var seeded = await ChemicalRegisterSeed.AddChemicalAsync(ChemicalsDbContext!, $"Lookup {Guid.NewGuid():N}", "4-567", barcode: barcode);
        var sut = CreateInventoryService();

        Assert.That((await sut.LookupBarcodeAsync(caller, barcode)).Single().ChemicalId, Is.EqualTo(seeded.ChemicalId));
        Assert.That((await sut.SearchRegisterAsync(caller, "4-567", 0, 50)).Entries.Select(e => e.ChemicalId), Has.Member(seeded.ChemicalId));
    }

    [Test]
    public async Task Sds_KnownFileIsProxied_UnknownOrMalformedIsRejected()
    {
        var caller = await WorkerAsync(view: true);
        var fileName = Guid.NewGuid().ToString("N");
        await ChemicalRegisterSeed.AddChemicalAsync(ChemicalsDbContext!, "With SDS", "5-678", sdsFileName: fileName);
        ChemicalBase.DownloadSdsAsync(fileName, Arg.Any<CancellationToken>()).Returns(new byte[] { 37, 80, 68, 70 });
        var sut = CreateInventoryService();

        Assert.That(await sut.GetSdsPdfAsync(caller, fileName), Is.EqualTo(new byte[] { 37, 80, 68, 70 }));
        Assert.That(async () => await sut.GetSdsPdfAsync(caller, Guid.NewGuid().ToString("N")), Throws.InstanceOf<ChemicalNotFoundException>());
        Assert.That(async () => await sut.GetSdsPdfAsync(caller, "../../etc/passwd"), Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public async Task Sds_MissingAtChemicalbase_IsNotFound()
    {
        var caller = await WorkerAsync(view: true);
        var fileName = Guid.NewGuid().ToString("N");
        await ChemicalRegisterSeed.AddChemicalAsync(ChemicalsDbContext!, "Lost SDS", "5-679", sdsFileName: fileName);
        ChemicalBase.DownloadSdsAsync(fileName, Arg.Any<CancellationToken>()).Returns((byte[]?)null);

        Assert.That(async () => await CreateInventoryService().GetSdsPdfAsync(caller, fileName), Throws.InstanceOf<ChemicalNotFoundException>());
    }

    [Test]
    public async Task Sds_EmptyBodyOrBlankName_IsNotFound()
    {
        var caller = await WorkerAsync(view: true);
        var fileName = Guid.NewGuid().ToString("N");
        await ChemicalRegisterSeed.AddChemicalAsync(ChemicalsDbContext!, "Empty SDS", "5-680", sdsFileName: fileName);
        ChemicalBase.DownloadSdsAsync(fileName, Arg.Any<CancellationToken>()).Returns(Array.Empty<byte>());
        var sut = CreateInventoryService();

        Assert.That(async () => await sut.GetSdsPdfAsync(caller, fileName), Throws.InstanceOf<ChemicalNotFoundException>());
        Assert.That(async () => await sut.GetSdsPdfAsync(caller, "   "), Throws.InstanceOf<ChemicalNotFoundException>());
        await ChemicalBase.DidNotReceive().DownloadSdsAsync(Arg.Is<string>(n => n != fileName), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Sds_EmptyContentMd5_IsNoSds_InAnyCase()
    {
        var caller = await WorkerAsync(view: true);
        // A product really stores the md5 of an empty upload, so only the md5 guard can refuse it.
        const string upperMd5 = "D41D8CD98F00B204E9800998ECF8427E";
        await ChemicalRegisterSeed.AddChemicalAsync(ChemicalsDbContext!, "Empty upload", "5-681", sdsFileName: upperMd5);
        ChemicalBase.DownloadSdsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new byte[] { 37, 80, 68, 70 });
        var sut = CreateInventoryService();

        Assert.That(async () => await sut.GetSdsPdfAsync(caller, upperMd5), Throws.InstanceOf<ChemicalNotFoundException>());
        Assert.That(async () => await sut.GetSdsPdfAsync(caller, upperMd5.ToLowerInvariant()), Throws.InstanceOf<ChemicalNotFoundException>());
        await ChemicalBase.DidNotReceive().DownloadSdsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Sds_ForwardsTheStoredFileName_NotTheCallersSpelling()
    {
        var caller = await WorkerAsync(view: true);
        var fileName = Guid.NewGuid().ToString("N");
        await ChemicalRegisterSeed.AddChemicalAsync(ChemicalsDbContext!, "Stored SDS", "5-682", sdsFileName: fileName);
        ChemicalBase.DownloadSdsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new byte[] { 37, 80, 68, 70 });

        await CreateInventoryService().GetSdsPdfAsync(caller, $" {fileName.ToUpperInvariant()} ");

        await ChemicalBase.Received(1).DownloadSdsAsync(fileName, Arg.Any<CancellationToken>());
        await ChemicalBase.DidNotReceive().DownloadSdsAsync(Arg.Is<string>(n => n != fileName), Arg.Any<CancellationToken>());
    }
}
