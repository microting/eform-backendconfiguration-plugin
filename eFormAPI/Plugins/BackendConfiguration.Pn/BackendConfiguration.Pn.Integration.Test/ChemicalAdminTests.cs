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

namespace BackendConfiguration.Pn.Integration.Test;

[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class ChemicalAdminTests : ChemicalTestBase
{
    private static readonly ChemicalPermissionFlagsModel AdminOnly = ChemicalPermissionFlagsModel.None with { Admin = true };

    private static readonly ChemicalPermissionFlagsModel EverythingButAdmin = ChemicalPermissionFlagsModel.All with { Admin = false };

    private async Task<(int PropertyId, int WorkerId, ChemicalCaller Caller)> WorkerWith(ChemicalPermissionFlagsModel flags)
    {
        var property = await CreatePropertyAsync();
        var worker = await AddWorkerAsync(property.Id);
        await GrantAsync(property.Id, worker, flags);
        return (property.Id, worker, ChemicalCaller.App(TestUserId, worker));
    }

    [Test]
    public async Task Settings_DefaultToStockOffAndNoRecipients()
    {
        var (propertyId, _, caller) = await WorkerWith(AdminOnly);

        var settings = await CreateInventoryService().GetSettingsAsync(caller, propertyId);

        Assert.That(settings.PropertyId, Is.EqualTo(propertyId));
        Assert.That(settings.StockEnabled, Is.False);
        Assert.That(settings.DigestRecipients, Is.Empty);
    }

    [Test]
    public async Task SetSettings_NormalisesRecipients_AndUpsertsOneRow()
    {
        var (propertyId, _, caller) = await WorkerWith(AdminOnly);
        var sut = CreateInventoryService();

        await sut.SetSettingsAsync(caller, new ChemicalSetSettingsCommand(propertyId, true, [" Anna@Example.com ", "", "anna@example.com", "bo@example.com"]));
        var saved = await sut.SetSettingsAsync(caller, new ChemicalSetSettingsCommand(propertyId, true, ["anna@example.com", "bo@example.com"]));

        Assert.That(saved.StockEnabled, Is.True);
        Assert.That(saved.DigestRecipients, Is.EqualTo(new[] { "anna@example.com", "bo@example.com" }));
        Assert.That(BackendConfigurationPnDbContext!.ChemicalPropertySettings.Count(x => x.PropertyId == propertyId), Is.EqualTo(1));
    }

    [TestCase("not-an-address")]
    [TestCase("Anna <anna@example.com>")]
    [TestCase("a@b.dk,c@d.dk")]
    public async Task SetSettings_RejectsAnythingButPlainAddresses(string recipient)
    {
        var (propertyId, _, caller) = await WorkerWith(AdminOnly);
        Assert.That(async () => await CreateInventoryService().SetSettingsAsync(caller, new ChemicalSetSettingsCommand(propertyId, false, [recipient])),
            Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public async Task SettingsAndPermissions_NeedAdmin()
    {
        var (propertyId, _, caller) = await WorkerWith(EverythingButAdmin);
        var sut = CreateInventoryService();

        Assert.That(async () => await sut.GetSettingsAsync(caller, propertyId), Throws.InstanceOf<ChemicalPermissionDeniedException>());
        Assert.That(async () => await sut.ListWorkerPermissionsAsync(caller, propertyId), Throws.InstanceOf<ChemicalPermissionDeniedException>());
    }

    [Test]
    public async Task ListWorkerPermissions_ListsEveryAssignedWorker_WithStoredFlags()
    {
        var (propertyId, adminWorker, caller) = await WorkerWith(AdminOnly);
        var plainWorker = await AddWorkerAsync(propertyId);

        var list = await CreateInventoryService().ListWorkerPermissionsAsync(caller, propertyId);

        Assert.That(list.Single(w => w.WorkerId == adminWorker).Flags, Is.EqualTo(AdminOnly), "Admin is stored, not expanded");
        Assert.That(list.Single(w => w.WorkerId == plainWorker).Flags, Is.EqualTo(ChemicalPermissionFlagsModel.None));
        Assert.That(list.Single(w => w.WorkerId == plainWorker).WorkerName, Is.EqualTo($"Worker {plainWorker}"));
    }

    [Test]
    public async Task SetWorkerPermissions_Upserts_AndRejectsUnknownOrDuplicateWorkers()
    {
        var (propertyId, _, caller) = await WorkerWith(AdminOnly);
        var target = await AddWorkerAsync(propertyId);
        var sut = CreateInventoryService();
        var stockKeeper = ChemicalPermissionFlagsModel.None with { View = true, Stock = true };

        await sut.SetWorkerPermissionsAsync(caller, propertyId, [new ChemicalSetWorkerPermissionCommand(target, ChemicalPermissionFlagsModel.None with { View = true })]);
        var list = await sut.SetWorkerPermissionsAsync(caller, propertyId, [new ChemicalSetWorkerPermissionCommand(target, stockKeeper)]);

        Assert.That(list.Single(w => w.WorkerId == target).Flags, Is.EqualTo(stockKeeper));
        Assert.That(BackendConfigurationPnDbContext!.ChemicalWorkerPermissions.Count(x => x.PropertyId == propertyId && x.WorkerId == target), Is.EqualTo(1));
        Assert.That(async () => await sut.SetWorkerPermissionsAsync(caller, propertyId, [new ChemicalSetWorkerPermissionCommand(int.MaxValue, stockKeeper)]),
            Throws.InstanceOf<ArgumentException>());
        Assert.That(async () => await sut.SetWorkerPermissionsAsync(caller, propertyId,
                [new ChemicalSetWorkerPermissionCommand(target, stockKeeper), new ChemicalSetWorkerPermissionCommand(target, stockKeeper)]),
            Throws.InstanceOf<ArgumentException>());
    }
}
