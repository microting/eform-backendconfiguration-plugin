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
using Microting.eForm.Infrastructure.Constants;

namespace BackendConfiguration.Pn.Integration.Test;

[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class ChemicalPermissionServiceTests : ChemicalTestBase
{
    private ChemicalPermissionService CreateSut() => new(BackendConfigurationPnDbContext!);

    private static readonly ChemicalPermissionFlagsModel ViewOnly = ChemicalPermissionFlagsModel.None with { View = true };

    [Test]
    public async Task Require_WithTheFlag_Passes_WithoutIt_IsDenied()
    {
        var property = await CreatePropertyAsync();
        var worker = await AddWorkerAsync(property.Id);
        await GrantAsync(property.Id, worker, ViewOnly with { Register = true });
        var caller = ChemicalCaller.App(TestUserId, worker);

        Assert.That(async () => await CreateSut().RequireAsync(caller, property.Id, ChemicalPermission.Register), Throws.Nothing);
        Assert.That(async () => await CreateSut().RequireAsync(caller, property.Id, ChemicalPermission.Remove),
            Throws.InstanceOf<ChemicalPermissionDeniedException>());
    }

    [Test]
    public async Task Admin_ImpliesEveryFlag()
    {
        var property = await CreatePropertyAsync();
        var worker = await AddWorkerAsync(property.Id);
        await GrantAsync(property.Id, worker, ChemicalPermissionFlagsModel.None with { Admin = true });

        foreach (var permission in Enum.GetValues<ChemicalPermission>())
        {
            Assert.That(async () => await CreateSut().RequireAsync(ChemicalCaller.App(TestUserId, worker), property.Id, permission),
                Throws.Nothing, permission.ToString());
        }
    }

    [Test]
    public async Task RemovedPropertyWorker_LosesAccess()
    {
        var property = await CreatePropertyAsync();
        var worker = await AddWorkerAsync(property.Id);
        await GrantAsync(property.Id, worker, ChemicalPermissionFlagsModel.None with { Admin = true });
        var caller = ChemicalCaller.App(TestUserId, worker);

        await RemoveWorkerAsync(property.Id, worker);

        Assert.That(async () => await CreateSut().RequireAsync(caller, property.Id, ChemicalPermission.View),
            Throws.InstanceOf<ChemicalPermissionDeniedException>());
        var visible = await CreateSut().ListVisiblePropertiesAsync(caller);
        Assert.That(visible.Select(v => v.Access.PropertyId), Has.No.Member(property.Id));
    }

    [Test]
    public async Task PermissionRowForAWorkerNotOnTheProperty_GrantsNothing()
    {
        var property = await CreatePropertyAsync();
        var worker = Random.Shared.Next(100_000, 999_999);
        await GrantAsync(property.Id, worker, ChemicalPermissionFlagsModel.All);

        Assert.That(async () => await CreateSut().RequireAsync(ChemicalCaller.App(TestUserId, worker), property.Id, ChemicalPermission.View),
            Throws.InstanceOf<ChemicalPermissionDeniedException>());
    }

    [Test]
    public async Task RemovedProperty_IsNotFound_EvenForWebAdmins()
    {
        var property = await CreatePropertyAsync();
        await property.Delete(BackendConfigurationPnDbContext!);

        Assert.That(async () => await CreateSut().RequireAsync(ChemicalCaller.Web(TestUserId), property.Id, ChemicalPermission.View),
            Throws.InstanceOf<ChemicalNotFoundException>());
    }

    [Test]
    public async Task WebAdmin_PassesEveryCheck_AndSeesEveryActiveProperty()
    {
        var property = await CreatePropertyAsync();
        var caller = ChemicalCaller.Web(TestUserId);

        Assert.That(async () => await CreateSut().RequireAsync(caller, property.Id, ChemicalPermission.Admin), Throws.Nothing);
        var visible = await CreateSut().ListVisiblePropertiesAsync(caller);
        var row = visible.Single(v => v.Access.PropertyId == property.Id);
        Assert.That(row.Access.Permissions, Is.EqualTo(ChemicalPermissionFlagsModel.All));
    }

    [Test]
    public async Task ListVisible_ReturnsOnlyViewOrAdminProperties_WithEffectiveFlagsAndStockSetting()
    {
        var visibleProperty = await CreatePropertyAsync();
        var adminProperty = await CreatePropertyAsync();
        var hiddenProperty = await CreatePropertyAsync();
        var worker = Random.Shared.Next(100_000, 999_999);
        foreach (var property in new[] { visibleProperty, adminProperty, hiddenProperty })
        {
            await AddWorkerAsync(property.Id, worker);
        }

        await GrantAsync(visibleProperty.Id, worker, ViewOnly);
        await GrantAsync(adminProperty.Id, worker, ChemicalPermissionFlagsModel.None with { Admin = true });
        await GrantAsync(hiddenProperty.Id, worker, ChemicalPermissionFlagsModel.None with { Register = true });
        await EnableStockAsync(adminProperty.Id);

        var visible = await CreateSut().ListVisiblePropertiesAsync(ChemicalCaller.App(TestUserId, worker));

        Assert.That(visible.Select(v => v.Access.PropertyId),
            Is.EquivalentTo(new[] { visibleProperty.Id, adminProperty.Id }));
        var admin = visible.Single(v => v.Access.PropertyId == adminProperty.Id).Access;
        Assert.That(admin.Permissions, Is.EqualTo(ChemicalPermissionFlagsModel.All));
        Assert.That(admin.StockEnabled, Is.True);
        Assert.That(visible.Single(v => v.Access.PropertyId == visibleProperty.Id).Access.StockEnabled, Is.False);
    }

    [Test]
    public async Task RequireViewOnAnyProperty_DeniesWorkersWithoutView()
    {
        var property = await CreatePropertyAsync();
        var worker = await AddWorkerAsync(property.Id);
        var caller = ChemicalCaller.App(TestUserId, worker);

        Assert.That(async () => await CreateSut().RequireViewOnAnyPropertyAsync(caller),
            Throws.InstanceOf<ChemicalPermissionDeniedException>());

        await GrantAsync(property.Id, worker, ViewOnly);
        Assert.That(async () => await CreateSut().RequireViewOnAnyPropertyAsync(caller), Throws.Nothing);
    }
}
