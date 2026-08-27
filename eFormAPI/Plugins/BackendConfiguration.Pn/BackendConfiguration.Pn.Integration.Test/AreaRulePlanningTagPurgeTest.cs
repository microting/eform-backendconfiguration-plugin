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

using BackendConfiguration.Pn.Services.AreaRulePlanningTagPurgeService;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Enums;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// Covers <c>AreaRulePlanningTagPurgeService</c>, which cleans up the join rows
/// nothing else can reach.
///
/// WHY the defect exists at all: <c>AreaRulePlanningTag.ItemPlanningTagId</c> is a
/// bare <c>int</c> with no <c>[ForeignKey]</c> and no navigation property, because
/// the <c>PlanningTag</c> it names lives in the items-planning DATABASE, not in the
/// backend-configuration one. <c>ItemsPlanningTagsService.DeleteItemsPlanningTag</c>
/// soft-deletes the <c>PlanningTag</c> and its <c>PlanningsTags</c> rows, but it has
/// no <c>BackendConfigurationPnDbContext</c> and so cannot touch this join — the
/// rows survive the delete still marked <c>Created</c>, pointing at a dead tag id.
/// The cross-database split is also why this cannot be one <c>UPDATE ... JOIN</c>:
/// the live tag ids must be read from the other context first.
///
/// The fixture asserts on real DB rows (not on a substituted service) because the
/// whole point of the purge is the row state it leaves behind: soft-deleted
/// (<c>WorkflowState = Removed</c> via the entity's own <c>.Delete()</c> helper),
/// never hard-deleted, and never touched twice.
///
/// Explicitly NOT covered, because it is a separate known defect in
/// <c>BackendConfigurationTaskWizardService.UpdateTags</c>: duplicate
/// <c>AreaRulePlanningTag</c> rows for the same (ARP, tag) pair. This service
/// purges orphans only.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class AreaRulePlanningTagPurgeTest : TestBaseSetup
{
    private AreaRulePlanningTagPurgeService _sut = null!;

    [SetUp]
    public async Task SetUpPurgeService()
    {
        // FK-safe cleanup, children before parents — same ordering as
        // TaskListBatchEformTagsTest / CalendarConfigurationBackfillTest.
        BackendConfigurationPnDbContext!.AreaRulePlanningTags.RemoveRange(
            BackendConfigurationPnDbContext.AreaRulePlanningTags);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.AreaRulePlannings.RemoveRange(
            BackendConfigurationPnDbContext.AreaRulePlannings);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.AreaRules.RemoveRange(
            BackendConfigurationPnDbContext.AreaRules);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.Areas.RemoveRange(
            BackendConfigurationPnDbContext.Areas);
        BackendConfigurationPnDbContext.Properties.RemoveRange(
            BackendConfigurationPnDbContext.Properties);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        // The run-once startup gate records a marker here; drop it so each test
        // starts from an un-purged database.
        BackendConfigurationPnDbContext.PluginConfigurationValues.RemoveRange(
            BackendConfigurationPnDbContext.PluginConfigurationValues
                .Where(x => x.Name == AreaRulePlanningTagPurgeService.BacklogPurgeMarkerName));
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        ItemsPlanningPnDbContext!.PlanningTags.RemoveRange(
            ItemsPlanningPnDbContext.PlanningTags);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        ItemsPlanningPnDbContext.Plannings.RemoveRange(
            ItemsPlanningPnDbContext.Plannings);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        _sut = new AreaRulePlanningTagPurgeService(
            BackendConfigurationPnDbContext!,
            ItemsPlanningPnDbContext!,
            NullLogger<AreaRulePlanningTagPurgeService>.Instance);
    }

    // ------------------------------------------------------------------
    // Seeding
    // ------------------------------------------------------------------

    /// <summary>
    /// Seeds Area → Property → AreaRule → Planning → AreaRulePlanning and returns
    /// the ARP id. AreaRulePlanningTag carries a real FK on
    /// <c>AreaRulePlanningId</c>, so the whole chain has to exist before a join row
    /// can be inserted.
    /// </summary>
    private async Task<int> SeedAreaRulePlanning()
    {
        var area = new Area
        {
            Type = AreaTypesEnum.Type1, ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await area.Create(BackendConfigurationPnDbContext!);

        var property = new Property
        {
            Name = $"TagPurgeProp-{Guid.NewGuid()}", ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await property.Create(BackendConfigurationPnDbContext!);

        var areaRule = new AreaRule
        {
            AreaId = area.Id, PropertyId = property.Id, EformId = 7, CreatedInGuide = true,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await areaRule.Create(BackendConfigurationPnDbContext!);

        var planning = new Planning
        {
            Enabled = true, RepeatEvery = 1, RepeatType = RepeatType.Week,
            StartDate = DateTime.UtcNow.Date, RelatedEFormId = 7, Description = "Task",
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await planning.Create(ItemsPlanningPnDbContext!);

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = property.Id, AreaId = area.Id,
            ItemPlanningId = planning.Id, StartDate = DateTime.UtcNow.Date, Status = true,
            RepeatType = 2, RepeatEvery = 1,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await arp.Create(BackendConfigurationPnDbContext!);

        return arp.Id;
    }

    /// <summary>
    /// Creates a real <c>PlanningTag</c> in the items-planning database and returns
    /// its id. When <paramref name="removed"/> is true it is then soft-deleted
    /// through the entity's own <c>.Delete()</c> — exactly what
    /// <c>ItemsPlanningTagsService.DeleteItemsPlanningTag</c> does, and exactly what
    /// leaves the backend-configuration join row orphaned.
    /// </summary>
    private async Task<int> SeedTag(bool removed)
    {
        var tag = new PlanningTag
        {
            Name = $"Tag-{Guid.NewGuid()}",
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await tag.Create(ItemsPlanningPnDbContext!);

        if (removed)
        {
            await tag.Delete(ItemsPlanningPnDbContext!);
        }

        return tag.Id;
    }

    /// <summary>
    /// Inserts one join row. <paramref name="alreadyRemoved"/> seeds the
    /// already-soft-deleted shape — the row a previous purge (or an old
    /// RemoveTags call) has already dealt with, which must not be touched again.
    /// </summary>
    private async Task<AreaRulePlanningTag> SeedJoinRow(int arpId, int tagId, bool alreadyRemoved = false)
    {
        var row = new AreaRulePlanningTag
        {
            AreaRulePlanningId = arpId, ItemPlanningTagId = tagId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await row.Create(BackendConfigurationPnDbContext!);

        if (alreadyRemoved)
        {
            await row.Delete(BackendConfigurationPnDbContext!);
        }

        return row;
    }

    // AsNoTracking so assertions read the DB row, not the instance this fixture
    // (and the SUT, which shares the context) still has in the change tracker.
    private async Task<AreaRulePlanningTag> ReadRow(int id) =>
        await BackendConfigurationPnDbContext!.AreaRulePlanningTags
            .AsNoTracking()
            .SingleAsync(x => x.Id == id);

    // ------------------------------------------------------------------
    // PurgeOrphanedAreaRulePlanningTagsAsync
    // ------------------------------------------------------------------

    [Test]
    public async Task Purge_RowPointingAtRemovedTag_IsSoftDeleted()
    {
        var arpId = await SeedAreaRulePlanning();
        var removedTagId = await SeedTag(removed: true);
        var row = await SeedJoinRow(arpId, removedTagId);

        var purged = await _sut.PurgeOrphanedAreaRulePlanningTagsAsync();

        var stored = await ReadRow(row.Id);
        Assert.Multiple(() =>
        {
            Assert.That(purged, Is.EqualTo(1));
            Assert.That(stored.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed),
                "the orphaned join row must be soft-deleted");
            // Soft, never hard: the row is still there (SingleAsync above would have
            // thrown otherwise) and its audit fields advanced through .Delete().
            Assert.That(stored.Version, Is.EqualTo(2),
                ".Delete() must have bumped Version rather than the row being erased");
        });
    }

    [Test]
    public async Task Purge_RowPointingAtLiveTag_IsUntouched()
    {
        var arpId = await SeedAreaRulePlanning();
        var liveTagId = await SeedTag(removed: false);
        var row = await SeedJoinRow(arpId, liveTagId);

        var purged = await _sut.PurgeOrphanedAreaRulePlanningTagsAsync();

        var stored = await ReadRow(row.Id);
        Assert.Multiple(() =>
        {
            Assert.That(purged, Is.EqualTo(0));
            Assert.That(stored.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created),
                "a join row naming a live tag is not an orphan");
            Assert.That(stored.Version, Is.EqualTo(1), "nothing may have been written to it");
        });
    }

    [Test]
    public async Task Purge_RowPointingAtTagIdThatNeverExisted_IsSoftDeleted()
    {
        var arpId = await SeedAreaRulePlanning();
        // No PlanningTag row at all — the hard-deleted / restored-from-another-
        // database case. There is no FK to stop this id from being stored.
        const int danglingTagId = 999_999;
        var row = await SeedJoinRow(arpId, danglingTagId);

        var purged = await _sut.PurgeOrphanedAreaRulePlanningTagsAsync();

        var stored = await ReadRow(row.Id);
        Assert.Multiple(() =>
        {
            Assert.That(purged, Is.EqualTo(1));
            Assert.That(stored.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed),
                "a join row naming no tag at all is just as orphaned as one naming a removed tag");
        });
    }

    [Test]
    public async Task Purge_AlreadyRemovedRow_IsNotDeletedTwice()
    {
        var arpId = await SeedAreaRulePlanning();
        var removedTagId = await SeedTag(removed: true);
        var row = await SeedJoinRow(arpId, removedTagId, alreadyRemoved: true);

        var versionBefore = (await ReadRow(row.Id)).Version;

        var purged = await _sut.PurgeOrphanedAreaRulePlanningTagsAsync();

        var stored = await ReadRow(row.Id);
        Assert.Multiple(() =>
        {
            Assert.That(purged, Is.EqualTo(0),
                "an already-removed row is not part of the backlog and must not be counted");
            Assert.That(stored.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
            Assert.That(stored.Version, Is.EqualTo(versionBefore),
                "re-deleting would bump Version and write a second Versions audit row");
        });
    }

    [Test]
    public async Task Purge_MixedRows_PurgesOnlyOrphansAndReturnsExactCount()
    {
        var arpId = await SeedAreaRulePlanning();
        var liveTagId = await SeedTag(removed: false);
        var removedTagId = await SeedTag(removed: true);
        var secondRemovedTagId = await SeedTag(removed: true);

        var liveRow = await SeedJoinRow(arpId, liveTagId);
        var removedTagRow = await SeedJoinRow(arpId, removedTagId);
        var secondRemovedTagRow = await SeedJoinRow(arpId, secondRemovedTagId);
        var danglingRow = await SeedJoinRow(arpId, 888_888);
        var alreadyRemovedRow = await SeedJoinRow(arpId, removedTagId, alreadyRemoved: true);

        var purged = await _sut.PurgeOrphanedAreaRulePlanningTagsAsync();

        // Read every row up front: Assert.Multiple takes a synchronous delegate,
        // so awaiting inside it would silently become async void.
        var storedLive = await ReadRow(liveRow.Id);
        var storedRemovedTag = await ReadRow(removedTagRow.Id);
        var storedSecondRemovedTag = await ReadRow(secondRemovedTagRow.Id);
        var storedDangling = await ReadRow(danglingRow.Id);
        var storedAlreadyRemoved = await ReadRow(alreadyRemovedRow.Id);

        Assert.Multiple(() =>
        {
            // Three: two removed-tag rows plus the dangling one. The live row and
            // the already-removed row are both excluded.
            Assert.That(purged, Is.EqualTo(3));
            Assert.That(storedLive.WorkflowState,
                Is.EqualTo(Constants.WorkflowStates.Created));
            Assert.That(storedRemovedTag.WorkflowState,
                Is.EqualTo(Constants.WorkflowStates.Removed));
            Assert.That(storedSecondRemovedTag.WorkflowState,
                Is.EqualTo(Constants.WorkflowStates.Removed));
            Assert.That(storedDangling.WorkflowState,
                Is.EqualTo(Constants.WorkflowStates.Removed));
            Assert.That(storedAlreadyRemoved.Version, Is.EqualTo(2),
                "the already-removed row keeps the Version its original delete gave it");
        });
    }

    [Test]
    public async Task Purge_RunTwice_SecondRunFindsNothing()
    {
        var arpId = await SeedAreaRulePlanning();
        var removedTagId = await SeedTag(removed: true);
        await SeedJoinRow(arpId, removedTagId);

        var firstRun = await _sut.PurgeOrphanedAreaRulePlanningTagsAsync();
        var secondRun = await _sut.PurgeOrphanedAreaRulePlanningTagsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(firstRun, Is.EqualTo(1));
            // The endpoint fires after EVERY tag create/rename/delete, so the
            // no-op case is the common one and must stay a no-op.
            Assert.That(secondRun, Is.EqualTo(0));
        });
    }

    // ------------------------------------------------------------------
    // RunIfNeededAsync — the startup gate
    // ------------------------------------------------------------------

    [Test]
    public async Task RunIfNeeded_PurgesBacklogAndRecordsMarker()
    {
        var arpId = await SeedAreaRulePlanning();
        var removedTagId = await SeedTag(removed: true);
        var row = await SeedJoinRow(arpId, removedTagId);

        await _sut.RunIfNeededAsync();

        var stored = await ReadRow(row.Id);
        var markers = await BackendConfigurationPnDbContext!.PluginConfigurationValues
            .AsNoTracking()
            .Where(x => x.Name == AreaRulePlanningTagPurgeService.BacklogPurgeMarkerName)
            .ToListAsync();

        Assert.Multiple(() =>
        {
            Assert.That(stored.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
            // Exactly one: a duplicate Name would make BasePn's
            // PluginConfigurationProvider.Load (ToDictionary on Name) throw on every
            // subsequent start, so the insert is written as a conditional statement.
            Assert.That(markers, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task RunIfNeeded_MarkerAlreadyPresent_SkipsTheScan()
    {
        var arpId = await SeedAreaRulePlanning();
        var removedTagId = await SeedTag(removed: true);

        // First boot: nothing to purge, but the marker is written anyway so later
        // boots cost one small SELECT instead of scanning the join table.
        await _sut.RunIfNeededAsync();

        // Orphan appears afterwards — the endpoint, not a later boot, is what
        // covers this case, so the gate must still short-circuit.
        var row = await SeedJoinRow(arpId, removedTagId);

        await _sut.RunIfNeededAsync();

        var stored = await ReadRow(row.Id);
        var markers = await BackendConfigurationPnDbContext!.PluginConfigurationValues
            .AsNoTracking()
            .Where(x => x.Name == AreaRulePlanningTagPurgeService.BacklogPurgeMarkerName)
            .ToListAsync();

        Assert.Multiple(() =>
        {
            Assert.That(stored.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created),
                "the gate must short-circuit before the scan on every boot after the first");
            Assert.That(markers, Has.Count.EqualTo(1),
                "the second run must not insert a second marker row");
        });
    }

    [Test]
    public async Task RunIfNeeded_NothingToPurge_StillWritesMarker()
    {
        var arpId = await SeedAreaRulePlanning();
        var liveTagId = await SeedTag(removed: false);
        var row = await SeedJoinRow(arpId, liveTagId);

        await _sut.RunIfNeededAsync();

        var stored = await ReadRow(row.Id);
        var markerExists = await BackendConfigurationPnDbContext!.PluginConfigurationValues
            .AsNoTracking()
            .AnyAsync(x => x.Name == AreaRulePlanningTagPurgeService.BacklogPurgeMarkerName);

        Assert.Multiple(() =>
        {
            Assert.That(stored.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
            Assert.That(markerExists, Is.True,
                "a clean database must still be marked, or every boot re-scans for nothing");
        });
    }
}
