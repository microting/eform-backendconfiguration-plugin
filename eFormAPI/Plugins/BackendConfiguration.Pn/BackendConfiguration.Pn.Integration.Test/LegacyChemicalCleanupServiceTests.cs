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

using BackendConfiguration.Pn.Services.LegacyChemicalCleanupService;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using NSubstitute;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// Covers <c>LegacyChemicalCleanupService</c>. The fixture database is shared
/// by every test in this fixture, and the SDK seam is a substitute (so entity
/// groups it "deletes" stay in the database); every assertion is therefore
/// scoped to the ids a test seeded itself, never to whole-table counts.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class LegacyChemicalCleanupServiceTests : TestBaseSetup
{
    private ILegacyChemicalSdkOperations _sdkOperations = null!;

    private sealed record Seeded(
        AreaProperty Legacy,
        AreaProperty Other,
        Folder LegacyFolder,
        int CaseUid,
        string BarcodeGroupUid,
        string PropertyGroupUid,
        string UnrelatedGroupUid);

    /// <summary>
    /// Call before seeding: GetCore() runs Core.StartSqlOnly, which migrates the
    /// SDK database to the current model (e.g. Folders.ChildrenProhibited).
    /// </summary>
    private async Task<LegacyChemicalCleanupService> CreateSut()
    {
        var core = await GetCore();
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));
        _sdkOperations = Substitute.For<ILegacyChemicalSdkOperations>();
        return new LegacyChemicalCleanupService(BackendConfigurationPnDbContext!, ItemsPlanningPnDbContext!,
            coreHelper, _sdkOperations, NullLogger<LegacyChemicalCleanupService>.Instance);
    }

    private static string NewEntityGroupUid() => Guid.NewGuid().ToString("N");

    private async Task<Seeded> SeedLegacyAsync()
    {
        var property = new Property { Name = Guid.NewGuid().ToString(), CreatedByUserId = 1, UpdatedByUserId = 1 };
        await property.Create(BackendConfigurationPnDbContext!);
        var chemicalArea = new Area { Type = AreaTypesEnum.Type9, IsDisabled = true, CreatedByUserId = 1, UpdatedByUserId = 1 };
        await chemicalArea.Create(BackendConfigurationPnDbContext!);
        var otherArea = new Area { Type = AreaTypesEnum.Type1, CreatedByUserId = 1, UpdatedByUserId = 1 };
        await otherArea.Create(BackendConfigurationPnDbContext!);

        var legacy = new AreaProperty { AreaId = chemicalArea.Id, PropertyId = property.Id, Checked = true, CreatedByUserId = 1, UpdatedByUserId = 1 };
        await legacy.Create(BackendConfigurationPnDbContext!);
        var other = new AreaProperty { AreaId = otherArea.Id, PropertyId = property.Id, Checked = true, CreatedByUserId = 1, UpdatedByUserId = 1 };
        await other.Create(BackendConfigurationPnDbContext!);

        var folder = new Folder { Name = "25.02 Udløber i dag eller er udløbet", Description = property.Name };
        await folder.Create(MicrotingDbContext!);
        await new ProperyAreaFolder { FolderId = folder.Id, ProperyAreaAsignmentId = legacy.Id, CreatedByUserId = 1, UpdatedByUserId = 1 }
            .Create(BackendConfigurationPnDbContext!);

        var caseUid = Random.Shared.Next(100_000_000, 999_999_999);
        await new Case { FolderId = folder.Id, MicrotingUid = caseUid }.Create(MicrotingDbContext!);

        var barcodeGroupUid = NewEntityGroupUid();
        var propertyGroupUid = NewEntityGroupUid();
        var unrelatedGroupUid = NewEntityGroupUid();
        await new EntityGroup { Name = "Chemicals - Barcode", MicrotingUid = barcodeGroupUid, Type = "EntitySearch" }.Create(MicrotingDbContext!);
        await new EntityGroup { Name = $"Chemicals - Areas - {property.Name}", MicrotingUid = propertyGroupUid, Type = "EntitySelect" }.Create(MicrotingDbContext!);
        await new EntityGroup { Name = "Unrelated list", MicrotingUid = unrelatedGroupUid, Type = "EntitySelect" }.Create(MicrotingDbContext!);

        return new Seeded(legacy, other, folder, caseUid, barcodeGroupUid, propertyGroupUid, unrelatedGroupUid);
    }

    [Test]
    public async Task Cleanup_RemovesLegacyAssignmentFoldersCasesAndEntityLists_OnlyThose()
    {
        var sut = await CreateSut();
        var seeded = await SeedLegacyAsync();

        var result = await sut.CleanupAsync();

        await _sdkOperations.Received(1).DeleteCaseAsync(seeded.CaseUid);
        await _sdkOperations.Received(1).DeleteEntityGroupAsync(seeded.BarcodeGroupUid);
        await _sdkOperations.Received(1).DeleteEntityGroupAsync(seeded.PropertyGroupUid);
        await _sdkOperations.DidNotReceive().DeleteEntityGroupAsync(seeded.UnrelatedGroupUid);

        var legacy = await BackendConfigurationPnDbContext!.AreaProperties.AsNoTracking().SingleAsync(x => x.Id == seeded.Legacy.Id);
        var other = await BackendConfigurationPnDbContext.AreaProperties.AsNoTracking().SingleAsync(x => x.Id == seeded.Other.Id);
        var folder = await MicrotingDbContext!.Folders.AsNoTracking().SingleAsync(x => x.Id == seeded.LegacyFolder.Id);

        Assert.That(legacy.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
        Assert.That(other.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
        Assert.That(folder.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
        Assert.That(result.AreaProperties, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public async Task Cleanup_ToleratesLegacyFolderAlreadyRemovedByHand()
    {
        var sut = await CreateSut();
        var seeded = await SeedLegacyAsync();
        var removedFolder = new Folder { Name = "25.03 Udløber om en uge", Description = "already removed" };
        await removedFolder.Create(MicrotingDbContext!);
        await removedFolder.Delete(MicrotingDbContext!);
        await new ProperyAreaFolder { FolderId = removedFolder.Id, ProperyAreaAsignmentId = seeded.Legacy.Id, CreatedByUserId = 1, UpdatedByUserId = 1 }
            .Create(BackendConfigurationPnDbContext!);

        await sut.CleanupAsync();

        var legacy = await BackendConfigurationPnDbContext!.AreaProperties.AsNoTracking().SingleAsync(x => x.Id == seeded.Legacy.Id);
        var folder = await MicrotingDbContext!.Folders.AsNoTracking().SingleAsync(x => x.Id == seeded.LegacyFolder.Id);
        Assert.That(legacy.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
        Assert.That(folder.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
    }

    [Test]
    public async Task RunIfNeeded_SecondRun_DoesNothing()
    {
        await BackendConfigurationPnDbContext!.PluginConfigurationValues
            .Where(x => x.Name == LegacyChemicalCleanupService.MarkerName).ExecuteDeleteAsync();
        var sut = await CreateSut();
        await SeedLegacyAsync();

        await sut.RunIfNeededAsync();
        _sdkOperations.ClearReceivedCalls();
        await SeedLegacyAsync();
        await sut.RunIfNeededAsync();

        await _sdkOperations.DidNotReceiveWithAnyArgs().DeleteCaseAsync(default);
        await _sdkOperations.DidNotReceiveWithAnyArgs().DeleteEntityGroupAsync(default!);
        Assert.That(await BackendConfigurationPnDbContext.PluginConfigurationValues
            .CountAsync(x => x.Name == LegacyChemicalCleanupService.MarkerName), Is.EqualTo(1));
    }
}
