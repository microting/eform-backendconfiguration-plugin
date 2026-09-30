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

using BackendConfiguration.Pn.Services.ChemicalInventoryService;
using Microsoft.EntityFrameworkCore;

namespace BackendConfiguration.Pn.Integration.Test;

[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class ChemicalRegisterReaderTests : ChemicalTestBase
{
    private ChemicalRegisterReader CreateSut() => new(ChemicalsDbContext!);

    [Test]
    public async Task GetByIds_MapsBmdDetailsToDisplayTexts()
    {
        var sales = new DateTime(2027, 1, 31, 0, 0, 0, DateTimeKind.Utc);
        var possession = new DateTime(2027, 7, 31, 0, 0, 0, DateTimeKind.Utc);
        var seeded = await ChemicalRegisterSeed.AddChemicalAsync(ChemicalsDbContext!, "Roundup Flex", "1-234", status: 6,
            salesDeadline: sales, useAndPossessionDeadline: possession, barcode: "5701234567892", sdsFileName: "a1b2c3");

        var entry = (await CreateSut().GetByIdsAsync([seeded.ChemicalId])).Single();

        Assert.That(entry.Name, Is.EqualTo("Roundup Flex"));
        Assert.That(entry.RegistrationNo, Is.EqualTo("1-234"));
        Assert.That(entry.Status, Is.EqualTo(6));
        Assert.That(entry.StatusText, Is.EqualTo("Produkt afmeldt"));
        Assert.That(entry.SalesDeadline, Is.EqualTo(sales));
        Assert.That(entry.UseAndPossessionDeadline, Is.EqualTo(possession));
        Assert.That(entry.HazardPictograms, Is.EqualTo(new[] { "GHS02", "GHS07" }));
        Assert.That(entry.SignalWordText, Is.EqualTo("Fare"));
        Assert.That(entry.HazardStatements.Single().Code, Is.EqualTo("H200"));
        Assert.That(entry.HazardStatements.Single().Text, Is.EqualTo("Ustabilt eksplosiv"));
        Assert.That(entry.ActiveSubstances.Single().Concentration, Is.EqualTo("360 g/l"));
        Assert.That(entry.AuthorisationHolder, Is.EqualTo("Holder A/S"));
        var product = entry.Products.Single();
        Assert.That(product.Barcode, Is.EqualTo("5701234567892"));
        Assert.That(product.SdsFileName, Is.EqualTo("a1b2c3"));
        Assert.That(product.SdsChecksum, Is.EqualTo("a1b2c3"));
    }

    [Test]
    public async Task GetByIds_KeepsChemicalsRemovedFromTheRegister()
    {
        var seeded = await ChemicalRegisterSeed.AddChemicalAsync(ChemicalsDbContext!, "Old product", "9-999");
        var chemical = await ChemicalsDbContext!.Chemicals.SingleAsync(c => c.Id == seeded.ChemicalId);
        chemical.WorkflowState = "removed";
        await ChemicalsDbContext.SaveChangesAsync();

        Assert.That(await CreateSut().GetByIdsAsync([seeded.ChemicalId]), Has.Count.EqualTo(1));
    }

    [TestCase(ChemicalRegisterReader.EmptyContentMd5)]
    [TestCase("")]
    [TestCase("   ")]
    public async Task EmptyContentMd5OrBlank_IsTreatedAsNoSds(string sdsFileName)
    {
        var seeded = await ChemicalRegisterSeed.AddChemicalAsync(ChemicalsDbContext!, "Broken SDS", "2-222",
            sdsFileName: sdsFileName);

        var product = (await CreateSut().GetByIdsAsync([seeded.ChemicalId])).Single().Products.Single();
        var reference = await CreateSut().FindProductAsync(seeded.ChemicalId, seeded.ProductId);

        Assert.That(product.SdsFileName, Is.Empty);
        Assert.That(product.SdsChecksum, Is.Empty);
        Assert.That(reference.ProductFileName, Is.Empty);
        Assert.That(await CreateSut().SdsFileExistsAsync(sdsFileName), Is.False);
    }

    [Test]
    public async Task SdsFileExists_OnlyForAnActiveProductsFile()
    {
        var fileName = Guid.NewGuid().ToString("N");
        var seeded = await ChemicalRegisterSeed.AddChemicalAsync(ChemicalsDbContext!, "With SDS", "2-223", sdsFileName: fileName);

        Assert.That(await CreateSut().SdsFileExistsAsync(fileName), Is.True);
        Assert.That(await CreateSut().SdsFileExistsAsync(Guid.NewGuid().ToString("N")), Is.False);

        await ChemicalsDbContext!.Products.Where(p => p.Id == seeded.ProductId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.WorkflowState, "removed"));

        Assert.That(await CreateSut().SdsFileExistsAsync(fileName), Is.False);
    }

    [Test]
    public async Task LookupBarcode_UpcAAndEan13FormsMatch()
    {
        // Stored as 13-digit EAN-13 ("0" + UPC-A, the central register's form), scanned as 12-digit UPC-A.
        var upcA = ChemicalRegisterSeed.RandomDigits(12);
        var curatedAsEan13 = await ChemicalRegisterSeed.AddChemicalAsync(ChemicalsDbContext!, "Curated EAN-13", "3-333", barcode: "0" + upcA);
        // Stored as 12-digit UPC-A, scanned as its 13-digit EAN-13 form.
        var upcA2 = ChemicalRegisterSeed.RandomDigits(12);
        var curatedAsUpcA = await ChemicalRegisterSeed.AddChemicalAsync(ChemicalsDbContext!, "Curated UPC-A", "3-334", barcode: upcA2);

        var byUpcA = await CreateSut().LookupBarcodeAsync(upcA);
        var byEan13 = await CreateSut().LookupBarcodeAsync("0" + upcA2);
        var byExactEan13 = await CreateSut().LookupBarcodeAsync("0" + upcA);
        var byExactUpcA = await CreateSut().LookupBarcodeAsync(upcA2);

        Assert.That(byUpcA.Select(e => e.ChemicalId), Is.EqualTo(new[] { curatedAsEan13.ChemicalId }));
        Assert.That(byEan13.Select(e => e.ChemicalId), Is.EqualTo(new[] { curatedAsUpcA.ChemicalId }));
        Assert.That(byExactEan13.Select(e => e.ChemicalId), Is.EqualTo(new[] { curatedAsEan13.ChemicalId }));
        Assert.That(byExactUpcA.Select(e => e.ChemicalId), Is.EqualTo(new[] { curatedAsUpcA.ChemicalId }));
    }

    [Test]
    public async Task LookupBarcode_UnknownOrRemoved_IsEmpty_InvalidThrows()
    {
        var barcode = ChemicalRegisterSeed.RandomDigits(13);
        var seeded = await ChemicalRegisterSeed.AddChemicalAsync(ChemicalsDbContext!, "Removed", "4-444", barcode: barcode);
        var chemical = await ChemicalsDbContext!.Chemicals.SingleAsync(c => c.Id == seeded.ChemicalId);
        chemical.WorkflowState = "removed";
        await ChemicalsDbContext.SaveChangesAsync();

        Assert.That(await CreateSut().LookupBarcodeAsync(barcode), Is.Empty);
        Assert.That(await CreateSut().LookupBarcodeAsync(ChemicalRegisterSeed.RandomDigits(13)), Is.Empty);
        Assert.That(async () => await CreateSut().LookupBarcodeAsync("abc"), Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public async Task Search_MatchesNameRegNoAndBarcode_Paged()
    {
        var token = $"Zq{Guid.NewGuid():N}"[..12];
        var barcode = ChemicalRegisterSeed.RandomDigits(13);
        var regNo = $"R{Guid.NewGuid():N}"[..10];
        await ChemicalRegisterSeed.AddChemicalAsync(ChemicalsDbContext!, $"{token} A", "5-001");
        await ChemicalRegisterSeed.AddChemicalAsync(ChemicalsDbContext!, $"{token} B", "5-002");
        await ChemicalRegisterSeed.AddChemicalAsync(ChemicalsDbContext!, $"{token} C", "5-003", barcode: barcode);
        await ChemicalRegisterSeed.AddChemicalAsync(ChemicalsDbContext!, "By reg.nr", regNo);

        var first = await CreateSut().SearchAsync(token, page: 0, pageSize: 2);
        var second = await CreateSut().SearchAsync(token, page: 1, pageSize: 2);
        var byBarcode = await CreateSut().SearchAsync(barcode, page: 0, pageSize: 0);
        var byRegNo = await CreateSut().SearchAsync(regNo, page: 0, pageSize: 0);

        Assert.That(first.Total, Is.EqualTo(3));
        Assert.That(first.Entries.Select(e => e.Name), Is.EqualTo(new[] { $"{token} A", $"{token} B" }));
        Assert.That(second.Entries.Select(e => e.Name), Is.EqualTo(new[] { $"{token} C" }));
        Assert.That(byBarcode.Entries.Single().Name, Is.EqualTo($"{token} C"));
        Assert.That(byRegNo.Entries.Single().Name, Is.EqualTo("By reg.nr"));
        Assert.That(async () => await CreateSut().SearchAsync("x", 0, 25), Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public async Task FindProduct_ValidatesTheChemicalProductPair()
    {
        var a = await ChemicalRegisterSeed.AddChemicalAsync(ChemicalsDbContext!, "Product A", "6-001", sdsFileName: "f00d");
        var b = await ChemicalRegisterSeed.AddChemicalAsync(ChemicalsDbContext!, "Product B", "6-002");

        var found = await CreateSut().FindProductAsync(a.ChemicalId, a.ProductId);

        Assert.That(found.ChemicalRemoteId, Is.EqualTo(a.RemoteId));
        Assert.That(found.ProductFileName, Is.EqualTo("f00d"));
        Assert.That(found.Status, Is.EqualTo(5));
        Assert.That(async () => await CreateSut().FindProductAsync(a.ChemicalId, b.ProductId), Throws.InstanceOf<ArgumentException>());
        Assert.That(async () => await CreateSut().FindProductAsync(int.MaxValue, null), Throws.InstanceOf<ChemicalNotFoundException>());
    }

    [Test]
    public async Task ChangedSince_ReturnsOnlyChemicalsUpdatedAfterTheInstant()
    {
        var unchanged = await ChemicalRegisterSeed.AddChemicalAsync(ChemicalsDbContext!, "Unchanged", "7-001");
        var changed = await ChemicalRegisterSeed.AddChemicalAsync(ChemicalsDbContext!, "Changed", "7-002");
        var productChanged = await ChemicalRegisterSeed.AddChemicalAsync(ChemicalsDbContext!, "Product changed", "7-003");
        int[] seededIds = [unchanged.ChemicalId, changed.ChemicalId, productChanged.ChemicalId];
        var since = DateTime.UtcNow.AddMinutes(-1);
        await ChemicalsDbContext!.Chemicals.Where(c => seededIds.Contains(c.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.UpdatedAt, since.AddHours(-1)));
        await ChemicalsDbContext.Products.Where(p => seededIds.Contains(p.ChemicalId))
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.UpdatedAt, since.AddHours(-1)));
        await ChemicalsDbContext.Chemicals.Where(c => c.Id == changed.ChemicalId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.UpdatedAt, DateTime.UtcNow));
        await ChemicalsDbContext.Products.Where(p => p.Id == productChanged.ProductId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.UpdatedAt, DateTime.UtcNow));

        var ids = await CreateSut().ChangedSinceAsync(seededIds, since);

        Assert.That(ids, Is.EquivalentTo(new[] { changed.ChemicalId, productChanged.ChemicalId }));
    }
}
