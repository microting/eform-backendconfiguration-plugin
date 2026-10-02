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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Enums;
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

    [SetUp]
    public void CreateSdkOperationsSubstitute()
    {
        _sdkOperations = Substitute.For<ILegacyChemicalSdkOperations>();
    }

    /// <summary>
    /// Call before seeding: GetCore() runs Core.StartSqlOnly, which migrates the
    /// SDK database to the current model (e.g. Folders.ChildrenProhibited).
    /// </summary>
    private async Task<LegacyChemicalCleanupService> CreateSut(ILogger<LegacyChemicalCleanupService>? logger = null)
    {
        var core = await GetCore();
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));
        return new LegacyChemicalCleanupService(BackendConfigurationPnDbContext!, ItemsPlanningPnDbContext!,
            coreHelper, _sdkOperations, logger ?? NullLogger<LegacyChemicalCleanupService>.Instance);
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

    private sealed record LegacyPlanning(
        AreaProperty Assignment,
        AreaRule Rule,
        AreaRulePlanning RulePlanning,
        Planning Planning,
        PlanningCaseSite CaseSite);

    /// <summary>
    /// The shape the removed CreatePlanningType9 left behind. Core.CaseCreate with
    /// Repeated = 0 returns a MicrotingUid and creates only a CheckListSite (no
    /// Cases row), and the PlanningCaseSite stores that uid in MicrotingSdkCaseId
    /// and the CheckListSite's Id in MicrotingCheckListSitId.
    /// With <paramref name="checkListSiteUid"/> null the SDK side is missing
    /// altogether (never deployed): MicrotingSdkCaseId points nowhere.
    /// </summary>
    private async Task<LegacyPlanning> SeedLegacyPlanningAsync(int? checkListSiteUid, int? folderId = null)
    {
        var property = new Property { Name = Guid.NewGuid().ToString(), CreatedByUserId = 1, UpdatedByUserId = 1 };
        await property.Create(BackendConfigurationPnDbContext!);
        var chemicalArea = new Area { Type = AreaTypesEnum.Type9, IsDisabled = true, CreatedByUserId = 1, UpdatedByUserId = 1 };
        await chemicalArea.Create(BackendConfigurationPnDbContext!);
        var assignment = new AreaProperty { AreaId = chemicalArea.Id, PropertyId = property.Id, Checked = true, CreatedByUserId = 1, UpdatedByUserId = 1 };
        await assignment.Create(BackendConfigurationPnDbContext!);
        if (folderId != null)
        {
            await new ProperyAreaFolder { FolderId = folderId.Value, ProperyAreaAsignmentId = assignment.Id, CreatedByUserId = 1, UpdatedByUserId = 1 }
                .Create(BackendConfigurationPnDbContext!);
        }

        var areaRule = new AreaRule
        {
            AreaId = chemicalArea.Id, PropertyId = property.Id, EformId = 7, CreatedInGuide = true,
            CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await areaRule.Create(BackendConfigurationPnDbContext!);
        var planning = new Planning
        {
            Enabled = true, RepeatEvery = 0, RepeatType = RepeatType.Day,
            StartDate = DateTime.UtcNow.Date, RelatedEFormId = 7, Description = "Legacy chemical",
            CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await planning.Create(ItemsPlanningPnDbContext!);
        var rulePlanning = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = property.Id, AreaId = chemicalArea.Id,
            ItemPlanningId = planning.Id, StartDate = DateTime.UtcNow.Date, Status = true,
            RepeatType = 1, RepeatEvery = 0, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await rulePlanning.Create(BackendConfigurationPnDbContext!);

        var checkListSiteId = 0;
        var sdkCaseId = int.MaxValue;
        if (checkListSiteUid != null)
        {
            var checkListSite = new CheckListSite { MicrotingUid = checkListSiteUid.Value, FolderId = folderId };
            await checkListSite.Create(MicrotingDbContext!);
            checkListSiteId = checkListSite.Id;
            sdkCaseId = checkListSiteUid.Value;
        }

        var planningCase = new PlanningCase
        {
            PlanningId = planning.Id, Status = 66, MicrotingSdkeFormId = 7, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await planningCase.Create(ItemsPlanningPnDbContext!);
        var caseSite = new PlanningCaseSite
        {
            PlanningId = planning.Id, PlanningCaseId = planningCase.Id, MicrotingSdkSiteId = 0,
            MicrotingSdkeFormId = 7, MicrotingSdkCaseId = sdkCaseId, MicrotingCheckListSitId = checkListSiteId,
            Status = 66, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await caseSite.Create(ItemsPlanningPnDbContext!);

        return new LegacyPlanning(assignment, areaRule, rulePlanning, planning, caseSite);
    }

    private static int NewUid() => Random.Shared.Next(100_000_000, 999_999_999);

    private async Task<T> ReloadAsync<T>(DbContext context, int id) where T : class =>
        await context.Set<T>().AsNoTracking().SingleAsync(x => EF.Property<int>(x, "Id") == id);

    [Test]
    public async Task Cleanup_LegacyPlanningShape_DeletesTheCheckListSiteOnce_AndNeverAnUnrelatedCase()
    {
        var sut = await CreateSut();
        // An unrelated case whose Cases.Id equals the legacy uid: reading the uid
        // as a Cases.Id would delete this case from the device and the cloud.
        var unrelatedUid = NewUid();
        var unrelated = new Case { MicrotingUid = unrelatedUid };
        await unrelated.Create(MicrotingDbContext!);
        var folder = new Folder { Name = "25.01 Registrer produkter", Description = Guid.NewGuid().ToString() };
        await folder.Create(MicrotingDbContext!);
        var legacy = await SeedLegacyPlanningAsync(unrelated.Id, folder.Id);

        var result = await sut.CleanupAsync();

        await _sdkOperations.Received(1).DeleteCaseAsync(unrelated.Id);
        await _sdkOperations.DidNotReceive().DeleteCaseAsync(unrelatedUid);
        Assert.That(result.Failures, Does.Not.Contain($"areaProperty:{legacy.Assignment.Id}"));
        Assert.That((await ReloadAsync<AreaProperty>(BackendConfigurationPnDbContext!, legacy.Assignment.Id)).WorkflowState,
            Is.EqualTo(Constants.WorkflowStates.Removed));
        Assert.That((await ReloadAsync<AreaRulePlanning>(BackendConfigurationPnDbContext!, legacy.RulePlanning.Id)).WorkflowState,
            Is.EqualTo(Constants.WorkflowStates.Removed));
    }

    [Test]
    public async Task Cleanup_PlanningCaseSiteWithoutAnySdkRow_IsSkipped_AndTheAssignmentRemoved()
    {
        var sut = await CreateSut();
        var legacy = await SeedLegacyPlanningAsync(checkListSiteUid: null);

        var result = await sut.CleanupAsync();

        await _sdkOperations.DidNotReceive().DeleteCaseAsync(int.MaxValue);
        Assert.That(result.Failures, Does.Not.Contain($"areaProperty:{legacy.Assignment.Id}"));
        Assert.That((await ReloadAsync<AreaProperty>(BackendConfigurationPnDbContext!, legacy.Assignment.Id)).WorkflowState,
            Is.EqualTo(Constants.WorkflowStates.Removed));
    }

    [Test]
    public async Task Cleanup_FailedPlannedDelete_KeepsTheCaseSiteLink_ForTheNextRun()
    {
        var sut = await CreateSut();
        var uid = NewUid();
        var legacy = await SeedLegacyPlanningAsync(uid);
        _sdkOperations.DeleteCaseAsync(uid).Returns(Task.FromException(new InvalidOperationException("cloud said no")));

        var result = await sut.CleanupAsync();

        Assert.That(result.Failures, Does.Contain($"areaProperty:{legacy.Assignment.Id}"));
        Assert.That((await ReloadAsync<PlanningCaseSite>(ItemsPlanningPnDbContext!, legacy.CaseSite.Id)).WorkflowState,
            Is.Not.EqualTo(Constants.WorkflowStates.Removed), "the link that finds the case again is kept");
    }

    /// <summary>
    /// Makes the SDK seam mark what it "deletes" as removed locally, the way
    /// Core does: Core.CaseDelete marks a Cases row only when exactly one row
    /// carries the uid (SqlController.CaseDelete), and always the CheckListSite
    /// (CaseDeleteReversed); Core.EntityGroupDelete marks the entity group.
    /// </summary>
    private void EmulateSdkDeletes()
    {
        _sdkOperations.When(x => x.DeleteCaseAsync(Arg.Any<int>())).Do(call =>
        {
            var uid = call.Arg<int>();
            if (MicrotingDbContext!.Cases.Count(x => x.MicrotingUid == uid) == 1)
            {
                MicrotingDbContext.Cases.Where(x => x.MicrotingUid == uid)
                    .ExecuteUpdate(x => x.SetProperty(c => c.WorkflowState, Constants.WorkflowStates.Removed));
            }

            MicrotingDbContext.CheckListSites.Where(x => x.MicrotingUid == uid)
                .ExecuteUpdate(x => x.SetProperty(c => c.WorkflowState, Constants.WorkflowStates.Removed));
        });
        _sdkOperations.When(x => x.DeleteEntityGroupAsync(Arg.Any<string>())).Do(call =>
        {
            var uid = call.Arg<string>();
            MicrotingDbContext!.EntityGroups.Where(x => x.MicrotingUid == uid)
                .ExecuteUpdate(x => x.SetProperty(c => c.WorkflowState, Constants.WorkflowStates.Removed));
        });
    }

    /// <summary>Clears what earlier tests in this fixture left, then forgets those calls.</summary>
    private async Task StartFromACleanSlateAsync(LegacyChemicalCleanupService sut)
    {
        EmulateSdkDeletes();
        await sut.CleanupAsync();
        _sdkOperations.ClearReceivedCalls();
    }

    [Test]
    public async Task RunIfNeeded_ReversedEformCompletedTwice_DoesNotRunAgain()
    {
        // The real shape of a legacy deployment: a reversed CheckListSite, plus one
        // completed Cases row per completion with the same uid. Core.CaseDelete
        // leaves those rows live (it marks a Cases row only on a single match).
        var sut = await CreateSut();
        await StartFromACleanSlateAsync(sut);
        var seeded = await SeedLegacyAsync();
        var uid = NewUid();
        await new CheckListSite { MicrotingUid = uid, FolderId = seeded.LegacyFolder.Id }.Create(MicrotingDbContext!);
        foreach (var _ in new[] { 1, 2 })
        {
            await new Case { FolderId = seeded.LegacyFolder.Id, MicrotingUid = uid, Status = 100, DoneAt = DateTime.UtcNow }
                .Create(MicrotingDbContext!);
        }
        await ClearMarkersAsync();

        await sut.RunIfNeededAsync();
        _sdkOperations.ClearReceivedCalls();
        await sut.RunIfNeededAsync();

        await _sdkOperations.DidNotReceiveWithAnyArgs().DeleteCaseAsync(default);
        await _sdkOperations.DidNotReceiveWithAnyArgs().DeleteEntityGroupAsync(default!);
    }

    [Test]
    public async Task CompletedLegacyRecords_SurviveTheCleanupAndTheRepair_Untouched()
    {
        // Owner decision (I2): completed legacy cases stay exactly as they are.
        // Core.CaseDelete(uid) marks the Cases row Removed when it is the only row
        // with that uid, so a reversed site completed once must not be deleted.
        var sut = await CreateSut();
        await StartFromACleanSlateAsync(sut);
        var seeded = await SeedLegacyAsync();

        // A reversed site in the legacy folder, completed once.
        var folderUid = NewUid();
        await new CheckListSite { MicrotingUid = folderUid, FolderId = seeded.LegacyFolder.Id }.Create(MicrotingDbContext!);
        var folderRecord = new Case { FolderId = seeded.LegacyFolder.Id, MicrotingUid = folderUid, Status = 100, DoneAt = DateTime.UtcNow };
        await folderRecord.Create(MicrotingDbContext!);
        // A completed ordinary case in the legacy folder.
        var plainRecord = new Case { FolderId = seeded.LegacyFolder.Id, MicrotingUid = NewUid(), Status = 100, DoneAt = DateTime.UtcNow };
        await plainRecord.Create(MicrotingDbContext!);
        // A legacy 25.01 planning whose site was completed once.
        var plannedUid = NewUid();
        await SeedLegacyPlanningAsync(plannedUid);
        var plannedRecord = new Case { MicrotingUid = plannedUid, Status = 100, DoneAt = DateTime.UtcNow };
        await plannedRecord.Create(MicrotingDbContext!);
        var records = new[] { folderRecord, plainRecord, plannedRecord };
        var before = await MicrotingDbContext!.Cases.AsNoTracking()
            .Where(x => records.Select(r => r.Id).Contains(x.Id))
            .Select(x => new { x.Id, x.WorkflowState, x.Version, x.UpdatedAt })
            .OrderBy(x => x.Id).ToListAsync();

        await ClearMarkersAsync();
        await sut.RunIfNeededAsync();
        // The repair: marker set and another leftover forces a re-run.
        await SeedLegacyPlanningAsync(NewUid());
        await SetMarkerAsync();
        await sut.RunIfNeededAsync();

        foreach (var uid in new[] { folderUid, plainRecord.MicrotingUid!.Value, plannedUid })
        {
            await _sdkOperations.DidNotReceive().DeleteCaseAsync(uid);
        }
        var after = await MicrotingDbContext.Cases.AsNoTracking()
            .Where(x => records.Select(r => r.Id).Contains(x.Id))
            .Select(x => new { x.Id, x.WorkflowState, x.Version, x.UpdatedAt })
            .OrderBy(x => x.Id).ToListAsync();
        Assert.That(after, Is.EqualTo(before));
        Assert.That((await ReloadAsync<AreaProperty>(BackendConfigurationPnDbContext!, seeded.Legacy.Id)).WorkflowState,
            Is.EqualTo(Constants.WorkflowStates.Removed), "the legacy flow itself is still removed");
    }

    [Test]
    public async Task RunIfNeeded_LeftoversThatNeverClear_GiveUpAfterFivePasses_WithOneWarning()
    {
        var logger = new CapturingLogger();
        var sut = await CreateSut(logger);
        await StartFromACleanSlateAsync(sut);
        var stuck = await SeedFailingLegacyPlanningAsync();
        await ClearMarkersAsync();

        for (var start = 0; start < 7; start++)
        {
            await sut.RunIfNeededAsync();
        }

        await _sdkOperations.Received(5).DeleteCaseAsync(stuck.CaseSite.MicrotingSdkCaseId);
        Assert.That(logger.Warnings.Count(x => x.Contains("giving up", StringComparison.OrdinalIgnoreCase)), Is.EqualTo(1),
            string.Join(Environment.NewLine, logger.Warnings));
    }

    private sealed class CapturingLogger : ILogger<LegacyChemicalCleanupService>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }

    [Test]
    public async Task Cleanup_CaseSiteOfAnAlreadyRemovedPlanning_IsStillDeleted()
    {
        // An interrupted #1362 pass soft-deleted the planning before its case sites.
        var sut = await CreateSut();
        var uid = NewUid();
        var legacy = await SeedLegacyPlanningAsync(uid);
        await legacy.Planning.Delete(ItemsPlanningPnDbContext!);

        await sut.CleanupAsync();

        await _sdkOperations.Received(1).DeleteCaseAsync(uid);
        Assert.That((await ReloadAsync<PlanningCaseSite>(ItemsPlanningPnDbContext!, legacy.CaseSite.Id)).WorkflowState,
            Is.EqualTo(Constants.WorkflowStates.Removed));
    }

    [Test]
    public async Task RunIfNeeded_MarkerSet_FolderCaseLeftAfterItsAssignment_IsRetried()
    {
        var sut = await CreateSut();
        await StartFromACleanSlateAsync(sut);
        var seeded = await SeedLegacyAsync();
        await seeded.Legacy.Delete(BackendConfigurationPnDbContext!);
        foreach (var uid in new[] { seeded.BarcodeGroupUid, seeded.PropertyGroupUid })
        {
            await MicrotingDbContext!.EntityGroups.Where(x => x.MicrotingUid == uid)
                .ExecuteUpdateAsync(x => x.SetProperty(c => c.WorkflowState, Constants.WorkflowStates.Removed));
        }
        await SetMarkerAsync();

        await sut.RunIfNeededAsync();

        await _sdkOperations.Received(1).DeleteCaseAsync(seeded.CaseUid);
    }

    [Test]
    public async Task RunIfNeeded_MarkerSet_RetractedFolderCase_IsNotALeftover()
    {
        // Retracted means already deleted from the device (as the planned-case
        // resolver treats it), so it must neither be deleted again nor keep the
        // retry gate open on every start.
        var sut = await CreateSut();
        await StartFromACleanSlateAsync(sut);
        var seeded = await SeedLegacyAsync();
        await seeded.Legacy.Delete(BackendConfigurationPnDbContext!);
        foreach (var uid in new[] { seeded.BarcodeGroupUid, seeded.PropertyGroupUid })
        {
            await MicrotingDbContext!.EntityGroups.Where(x => x.MicrotingUid == uid)
                .ExecuteUpdateAsync(x => x.SetProperty(c => c.WorkflowState, Constants.WorkflowStates.Removed));
        }
        await MicrotingDbContext!.Cases.Where(x => x.MicrotingUid == seeded.CaseUid)
            .ExecuteUpdateAsync(x => x.SetProperty(c => c.WorkflowState, Constants.WorkflowStates.Retracted));
        await SetMarkerAsync();

        await sut.RunIfNeededAsync();

        await _sdkOperations.DidNotReceiveWithAnyArgs().DeleteCaseAsync(default);
    }

    [Test]
    public async Task RunIfNeeded_MarkerSet_LegacyEntityListLeft_IsRetried()
    {
        var sut = await CreateSut();
        await StartFromACleanSlateAsync(sut);
        var groupUid = NewEntityGroupUid();
        await new EntityGroup { Name = "Chemicals - RegNo", MicrotingUid = groupUid, Type = "EntitySearch" }.Create(MicrotingDbContext!);
        await SetMarkerAsync();

        await sut.RunIfNeededAsync();

        await _sdkOperations.Received(1).DeleteEntityGroupAsync(groupUid);
    }

    private async Task ClearMarkersAsync() =>
        await BackendConfigurationPnDbContext!.PluginConfigurationValues
            .Where(x => x.Name == LegacyChemicalCleanupService.MarkerName
                        || x.Name == LegacyChemicalCleanupService.AttemptsName)
            .ExecuteDeleteAsync();

    /// <summary>The state of a tenant where the first pass already ran (279), with no attempts row yet.</summary>
    private async Task SetMarkerAsync()
    {
        await BackendConfigurationPnDbContext!.PluginConfigurationValues
            .Where(x => x.Name == LegacyChemicalCleanupService.AttemptsName).ExecuteDeleteAsync();
        if (!await BackendConfigurationPnDbContext!.PluginConfigurationValues
                .AnyAsync(x => x.Name == LegacyChemicalCleanupService.MarkerName))
        {
            BackendConfigurationPnDbContext.PluginConfigurationValues.Add(
                new Microting.eFormApi.BasePn.Infrastructure.Database.Entities.PluginConfigurationValue
                {
                    Name = LegacyChemicalCleanupService.MarkerName, Value = "true",
                    CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1,
                    WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1
                });
            await BackendConfigurationPnDbContext.SaveChangesAsync();
        }
    }

    [Test]
    public async Task RunIfNeeded_MarkerSet_HalfDeletedAssignment_IsRemovedOnTheNextStart()
    {
        // What the #1362 delete path left on tenants: the planning was soft-deleted,
        // then the case lookup threw, so the rule, its planning and the assignment stayed.
        var sut = await CreateSut();
        var legacy = await SeedLegacyPlanningAsync(NewUid());
        await legacy.Planning.Delete(ItemsPlanningPnDbContext!);
        await legacy.CaseSite.Delete(ItemsPlanningPnDbContext!);
        await SetMarkerAsync();

        await sut.RunIfNeededAsync();

        Assert.That((await ReloadAsync<AreaProperty>(BackendConfigurationPnDbContext!, legacy.Assignment.Id)).WorkflowState,
            Is.EqualTo(Constants.WorkflowStates.Removed));
        Assert.That((await ReloadAsync<AreaRule>(BackendConfigurationPnDbContext!, legacy.Rule.Id)).WorkflowState,
            Is.EqualTo(Constants.WorkflowStates.Removed));
        Assert.That((await ReloadAsync<AreaRulePlanning>(BackendConfigurationPnDbContext!, legacy.RulePlanning.Id)).WorkflowState,
            Is.EqualTo(Constants.WorkflowStates.Removed));
        Assert.That(await BackendConfigurationPnDbContext!.PluginConfigurationValues
            .CountAsync(x => x.Name == LegacyChemicalCleanupService.MarkerName), Is.EqualTo(1));
    }

    [Test]
    public async Task RunIfNeeded_MarkerSet_OrphanRuleAndPlanning_AreRemoved()
    {
        var sut = await CreateSut();
        // A rule left without its assignment, and a rule planning left without its rule.
        var orphanRule = await SeedLegacyPlanningAsync(NewUid());
        await orphanRule.Assignment.Delete(BackendConfigurationPnDbContext!);
        var orphanPlanning = await SeedLegacyPlanningAsync(NewUid());
        await orphanPlanning.Rule.Delete(BackendConfigurationPnDbContext!);
        await orphanPlanning.Assignment.Delete(BackendConfigurationPnDbContext!);
        await SetMarkerAsync();

        await sut.RunIfNeededAsync();

        Assert.That((await ReloadAsync<AreaRule>(BackendConfigurationPnDbContext!, orphanRule.Rule.Id)).WorkflowState,
            Is.EqualTo(Constants.WorkflowStates.Removed));
        Assert.That((await ReloadAsync<AreaRulePlanning>(BackendConfigurationPnDbContext!, orphanRule.RulePlanning.Id)).WorkflowState,
            Is.EqualTo(Constants.WorkflowStates.Removed));
        Assert.That((await ReloadAsync<AreaRulePlanning>(BackendConfigurationPnDbContext!, orphanPlanning.RulePlanning.Id)).WorkflowState,
            Is.EqualTo(Constants.WorkflowStates.Removed));
        await _sdkOperations.Received(1).DeleteCaseAsync(orphanRule.CaseSite.MicrotingSdkCaseId);
        await _sdkOperations.Received(1).DeleteCaseAsync(orphanPlanning.CaseSite.MicrotingSdkCaseId);
    }

    /// <summary>
    /// Plans one case into the legacy assignment's 25.02 folder and one outside
    /// it, each through an items-planning PlanningCaseSite. Returns their MicrotingUids.
    /// </summary>
    private async Task<(int InFolderUid, int OutsideFolderUid)> SeedPlannedCasesAsync(Seeded seeded)
    {
        var areaRule = new AreaRule
        {
            AreaId = seeded.Legacy.AreaId, PropertyId = seeded.Legacy.PropertyId, EformId = 7, CreatedInGuide = true,
            CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await areaRule.Create(BackendConfigurationPnDbContext!);
        var planning = new Planning
        {
            Enabled = true, RepeatEvery = 1, RepeatType = RepeatType.Week,
            StartDate = DateTime.UtcNow.Date, RelatedEFormId = 7, Description = "Legacy chemical",
            CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await planning.Create(ItemsPlanningPnDbContext!);
        await new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = seeded.Legacy.PropertyId, AreaId = seeded.Legacy.AreaId,
            ItemPlanningId = planning.Id, StartDate = DateTime.UtcNow.Date, Status = true,
            RepeatType = 2, RepeatEvery = 1, CreatedByUserId = 1, UpdatedByUserId = 1
        }.Create(BackendConfigurationPnDbContext!);

        var inFolderUid = Random.Shared.Next(100_000_000, 999_999_999);
        var inFolder = new Case { FolderId = seeded.LegacyFolder.Id, MicrotingUid = inFolderUid };
        await inFolder.Create(MicrotingDbContext!);
        var outsideFolderUid = Random.Shared.Next(100_000_000, 999_999_999);
        var outsideFolder = new Case { MicrotingUid = outsideFolderUid };
        await outsideFolder.Create(MicrotingDbContext!);

        foreach (var sdkCase in new[] { inFolder, outsideFolder })
        {
            var planningCase = new PlanningCase
            {
                PlanningId = planning.Id, Status = 66, MicrotingSdkCaseId = sdkCase.Id,
                MicrotingSdkeFormId = 7, CreatedByUserId = 1, UpdatedByUserId = 1
            };
            await planningCase.Create(ItemsPlanningPnDbContext!);
            await new PlanningCaseSite
            {
                PlanningId = planning.Id, PlanningCaseId = planningCase.Id, MicrotingSdkSiteId = 0,
                MicrotingSdkeFormId = 7, MicrotingSdkCaseId = sdkCase.Id, Status = 66,
                CreatedByUserId = 1, UpdatedByUserId = 1
            }.Create(ItemsPlanningPnDbContext!);
        }

        return (inFolderUid, outsideFolderUid);
    }

    [Test]
    public async Task Cleanup_PlannedCases_AreDeletedOnce_ThroughTheSdkSeam()
    {
        var sut = await CreateSut();
        var seeded = await SeedLegacyAsync();
        var (inFolderUid, outsideFolderUid) = await SeedPlannedCasesAsync(seeded);

        var result = await sut.CleanupAsync();

        await _sdkOperations.Received(1).DeleteCaseAsync(inFolderUid);
        await _sdkOperations.Received(1).DeleteCaseAsync(outsideFolderUid);
        await _sdkOperations.Received(1).DeleteCaseAsync(seeded.CaseUid);
        Assert.That(result.Failures, Does.Not.Contain($"areaProperty:{seeded.Legacy.Id}"));
        var legacy = await BackendConfigurationPnDbContext!.AreaProperties.AsNoTracking().SingleAsync(x => x.Id == seeded.Legacy.Id);
        Assert.That(legacy.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
    }

    [Test]
    public async Task Cleanup_PlannedCaseDeleteFailing_IsReportedAsAFailedAssignment()
    {
        var sut = await CreateSut();
        var seeded = await SeedLegacyAsync();
        var (_, outsideFolderUid) = await SeedPlannedCasesAsync(seeded);
        _sdkOperations.DeleteCaseAsync(outsideFolderUid)
            .Returns(Task.FromException(new InvalidOperationException("Core.CaseDelete returned false")));

        var result = await sut.CleanupAsync();

        await _sdkOperations.Received(1).DeleteCaseAsync(outsideFolderUid);
        Assert.That(result.Failures, Does.Contain($"areaProperty:{seeded.Legacy.Id}"));
    }

    /// <summary>A legacy planning whose check-list-site delete the cloud refuses.</summary>
    private async Task<LegacyPlanning> SeedFailingLegacyPlanningAsync()
    {
        var uid = NewUid();
        _sdkOperations.DeleteCaseAsync(uid).Returns(Task.FromException(new InvalidOperationException("cloud said no")));
        return await SeedLegacyPlanningAsync(uid);
    }

    private void FailSdkDeletesFor(Seeded seeded)
    {
        _sdkOperations.DeleteCaseAsync(seeded.CaseUid)
            .Returns(Task.FromException(new InvalidOperationException("cloud said no")));
        _sdkOperations.DeleteEntityGroupAsync(seeded.BarcodeGroupUid)
            .Returns(Task.FromException(new InvalidOperationException("cloud said no")));
    }

    [Test]
    public async Task Cleanup_OneItemFailing_StillCleansTheRestAndReportsTheFailures()
    {
        var sut = await CreateSut();
        var undeletable = (await SeedFailingLegacyPlanningAsync()).Assignment;
        var seeded = await SeedLegacyAsync();
        FailSdkDeletesFor(seeded);

        var result = await sut.CleanupAsync();

        await _sdkOperations.Received(1).DeleteEntityGroupAsync(seeded.PropertyGroupUid);
        var legacy = await BackendConfigurationPnDbContext!.AreaProperties.AsNoTracking().SingleAsync(x => x.Id == seeded.Legacy.Id);
        var folder = await MicrotingDbContext!.Folders.AsNoTracking().SingleAsync(x => x.Id == seeded.LegacyFolder.Id);
        Assert.That(legacy.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
        Assert.That(folder.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
        Assert.That(result.Failures, Does.Contain($"areaProperty:{undeletable.Id}"));
        Assert.That(result.Failures, Does.Contain($"case:{seeded.CaseUid}"));
        Assert.That(result.Failures, Does.Contain($"entityGroup:{seeded.BarcodeGroupUid}"));
    }

    [Test]
    public async Task RunIfNeeded_OneItemFailing_StillWritesTheMarker()
    {
        await ClearMarkersAsync();
        var sut = await CreateSut();
        await SeedFailingLegacyPlanningAsync();
        var seeded = await SeedLegacyAsync();
        FailSdkDeletesFor(seeded);

        await sut.RunIfNeededAsync();

        await _sdkOperations.Received(1).DeleteEntityGroupAsync(seeded.PropertyGroupUid);
        Assert.That(await BackendConfigurationPnDbContext.PluginConfigurationValues
            .CountAsync(x => x.Name == LegacyChemicalCleanupService.MarkerName), Is.EqualTo(1));
    }

    [Test]
    public async Task Cleanup_LegacyNamedEntityGroupWithoutMicrotingUid_IsNotDeleted()
    {
        var sut = await CreateSut();
        await new EntityGroup { Name = "Chemicals - RegNo", MicrotingUid = null, Type = "EntitySearch" }.Create(MicrotingDbContext!);

        await sut.CleanupAsync();

        await _sdkOperations.DidNotReceive().DeleteEntityGroupAsync(Arg.Is<string>(x => x == null));
    }

    [Test]
    public async Task RunIfNeeded_SecondRun_NothingLeft_DoesNothing()
    {
        await ClearMarkersAsync();
        var sut = await CreateSut();
        EmulateSdkDeletes();
        await SeedLegacyAsync();

        await sut.RunIfNeededAsync();
        _sdkOperations.ClearReceivedCalls();
        await sut.RunIfNeededAsync();

        await _sdkOperations.DidNotReceiveWithAnyArgs().DeleteCaseAsync(default);
        await _sdkOperations.DidNotReceiveWithAnyArgs().DeleteEntityGroupAsync(default!);
        Assert.That(await BackendConfigurationPnDbContext.PluginConfigurationValues
            .CountAsync(x => x.Name == LegacyChemicalCleanupService.MarkerName), Is.EqualTo(1));
    }
}
