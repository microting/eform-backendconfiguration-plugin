using System;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.TailBite;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Integration.Test.TailBite;

[TestFixture]
public class TailBiteSetupServiceTests : TailBiteTestBase
{
    private const int ManagerSite = 7;
    private const int WorkerSite = 8;

    private TailBiteSetupService Sut() => new(Db, new TailBitePropertyLock(Db), NewAccess(), new TailBiteSnapshotLoader(Db), Clock);

    private async Task SeedManagerAsync()
    {
        await SeedTreeAsync();
        await SeedWorkerAsync(ManagerSite, manager: true);
    }

    private async Task OpenOutbreakAsync(int locationId)
        => await new TailBiteOutbreak
            { PropertyId = PropertyId, LocationId = locationId, RuleId = RuleId, RuleVersion = 1,
              OpenedAt = Clock.GetUtcNow().UtcDateTime, OpenedByRegistrationId = 1 }.Create(Db);

    // `count` registrations of 2 bitten pigs in Sti 309 on consecutive days from `startDay` (relative to the clock).
    private async Task SeedBurstAsync(int startDay, int count)
    {
        var now = Clock.GetUtcNow().UtcDateTime;
        for (var i = 0; i < count; i++)
            await SeedRegistrationAsync(now.AddDays(startDay + i), false, (Pen309Id, 2, 0));
    }

    // ---------- enabling and listing ----------

    [Test]
    public async Task Enable_CreatesRootDefaultRuleAndActionTypes_Idempotent()
    {
        var sut = Sut();
        await sut.EnableAsync(0, PropertyId);
        await sut.EnableAsync(0, PropertyId);
        Assert.That(await Db.TailBiteLocations.CountAsync(l => l.PropertyId == PropertyId && l.ParentId == null), Is.EqualTo(1));
        var rule = await Db.TailBiteRules.SingleAsync();
        Assert.That((rule.MinBittenPigs, rule.MinSevere, rule.WindowDays, rule.CountDepth), Is.EqualTo(((int?)5, (int?)1, 7, 1)));
        Assert.That(await Db.TailBiteActionTypes.CountAsync(a => a.PropertyId == PropertyId), Is.EqualTo(TailBiteDefaults.ActionTypes.Length));
        Assert.That((await Db.TailBiteProperties.SingleAsync(p => p.PropertyId == PropertyId)).Enabled, Is.True);
    }

    [Test]
    public async Task ListEnabledProperties_ReturnsOnlyEnabledWithManagerFlag()
    {
        await SeedManagerAsync();
        var disabled = new Property { Name = "Ejendom Slukket" };
        await disabled.Create(Db);
        await new TailBiteProperty { PropertyId = disabled.Id, Enabled = false }.Create(Db);
        await new PropertyWorker { PropertyId = disabled.Id, WorkerId = ManagerSite, TailBiteManager = true }.Create(Db);
        var notSetUp = new Property { Name = "Ejendom Uden Halebid" };
        await notSetUp.Create(Db);
        await new PropertyWorker { PropertyId = notSetUp.Id, WorkerId = ManagerSite }.Create(Db);
        var sut = Sut();

        Assert.That(await sut.ListEnabledPropertiesAsync(ManagerSite), Is.EqualTo(new[] { (PropertyId, "Ejendom Test", true) }));
        await SeedWorkerAsync(WorkerSite);
        Assert.That(await sut.ListEnabledPropertiesAsync(WorkerSite), Is.EqualTo(new[] { (PropertyId, "Ejendom Test", false) }));
        Assert.That(await sut.ListEnabledPropertiesAsync(9), Is.Empty);
    }

    // ---------- tree ----------

    [Test]
    public async Task GetTree_WorkerAllowed_ReturnsDepthsAndActionTypes()
    {
        await SeedTreeAsync(); await SeedWorkerAsync(WorkerSite);
        await new TailBiteActionType { PropertyId = PropertyId, Code = "HALM", Name = "Halm" }.Create(Db);
        var sut = Sut();
        var tree = await sut.GetTreeAsync(WorkerSite, PropertyId);
        Assert.That(tree.Locations.Single(l => l.Id == Pen309Id).Depth, Is.EqualTo(3));
        Assert.That(tree.ActionTypes.Single().Code, Is.EqualTo("HALM"));
        Assert.That(tree.TreeVersion, Is.GreaterThan(0));
        await Assert.ThrowsAsync<TailBiteForbiddenException>(() => sut.GetTreeAsync(9, PropertyId));
    }

    [Test]
    public async Task PenRange_CreatesNamedPens_WithUniqueQrCodes()
    {
        await SeedManagerAsync();
        var ids = await Sut().CreatePenRangeAsync(ManagerSite, StableBId, "Sti", 601, 612);
        var pens = await Db.TailBiteLocations.Where(l => ids.Contains(l.Id)).ToListAsync();
        Assert.That(pens.Select(p => p.Name), Does.Contain("Sti 601").And.Contain("Sti 612"));
        Assert.That(pens.Select(p => p.QrCode).Distinct().Count(), Is.EqualTo(12));
        Assert.That(pens.All(p => p.QrCode.Length == 22), Is.True);
    }

    [Test]
    public async Task PenRange_NoZeroPadding_RejectsReversedOversizedAndExistingNames()
    {
        await SeedManagerAsync();
        var sut = Sut();
        var ids = await sut.CreatePenRangeAsync(ManagerSite, StableBId, "Sti", 8, 10);
        Assert.That(Db.TailBiteLocations.Where(l => ids.Contains(l.Id)).Select(l => l.Name).ToList(),
            Is.EquivalentTo(new[] { "Sti 8", "Sti 9", "Sti 10" }));
        await Assert.ThrowsAsync<TailBiteValidationException>(() => sut.CreatePenRangeAsync(ManagerSite, SectionId, "Sti", 312, 301));
        await Assert.ThrowsAsync<TailBiteValidationException>(() => sut.CreatePenRangeAsync(ManagerSite, SectionId, "Sti", 1, 501));
        await Assert.ThrowsAsync<TailBiteValidationException>(() => sut.CreatePenRangeAsync(ManagerSite, SectionId, "Sti", 305, 310)); // Sti 309 and 310 exist
    }

    [Test]
    public async Task CreateAndRename_RejectDuplicateSiblingName()
    {
        await SeedManagerAsync();
        var sut = Sut();
        var id = await sut.CreateLocationAsync(ManagerSite, SectionId, "Sti 311");
        Assert.That(Db.TailBiteLocations.Single(l => l.Id == id).ParentId, Is.EqualTo(SectionId));
        await Assert.ThrowsAsync<TailBiteValidationException>(() => sut.CreateLocationAsync(ManagerSite, SectionId, " Sti 309 "));
        await Assert.ThrowsAsync<TailBiteValidationException>(() => sut.RenameLocationAsync(ManagerSite, id, "Sti 310"));
    }

    [Test]
    public async Task NonManager_CannotEditTree()
    {
        await SeedTreeAsync(); await SeedWorkerAsync(WorkerSite);
        await Assert.ThrowsAsync<TailBiteForbiddenException>(() => Sut().CreateLocationAsync(WorkerSite, SectionId, "Sti 999"));
    }

    // ---------- guards under open outbreaks (§5, §6.4) ----------

    [Test]
    public async Task Move_UnderOpenOutbreak_Refused_Delete_OnlyOpenNodeOrAncestorRefused_RenameAllowed()
    {
        await SeedManagerAsync(); await OpenOutbreakAsync(StableAId);
        var sut = Sut();
        await Assert.ThrowsAsync<TailBiteConflictException>(() => sut.MoveLocationAsync(ManagerSite, Pen309Id, StableBId));
        await Assert.DoesNotThrowAsync(() => sut.RenameLocationAsync(ManagerSite, Pen309Id, "Sti 309A"));
        await Assert.ThrowsAsync<TailBiteConflictException>(() => sut.DeleteLocationAsync(ManagerSite, StableAId)); // the open node
        await Assert.ThrowsAsync<TailBiteConflictException>(() => sut.DeleteLocationAsync(ManagerSite, RootId));    // its ancestor
        await Assert.DoesNotThrowAsync(() => sut.DeleteLocationAsync(ManagerSite, SectionId));                      // inside the open subtree: allowed
        Db.ChangeTracker.Clear();
        Assert.That(Db.TailBiteLocations.Single(l => l.Id == Pen309Id).Name, Is.EqualTo("Sti 309A"));
        var removed = new[] { SectionId, Pen309Id, Pen310Id };
        Assert.That(Db.TailBiteLocations.Where(l => removed.Contains(l.Id)).All(l => l.WorkflowState == Constants.WorkflowStates.Removed), Is.True);
    }

    [Test]
    public async Task MoveIntoOpenSubtree_Refused()
    {
        await SeedManagerAsync(); await OpenOutbreakAsync(StableAId);
        await Assert.ThrowsAsync<TailBiteConflictException>(() => Sut().MoveLocationAsync(ManagerSite, Pen501Id, SectionId));
    }

    [Test]
    public async Task MoveOpenNode_Refused()
    {
        await SeedManagerAsync(); await OpenOutbreakAsync(StableAId);
        await Assert.ThrowsAsync<TailBiteConflictException>(() => Sut().MoveLocationAsync(ManagerSite, StableAId, StableBId));
    }

    [Test]
    public async Task MoveAncestorOfOpenNode_Refused()
    {
        await SeedManagerAsync(); await OpenOutbreakAsync(SectionId);
        await Assert.ThrowsAsync<TailBiteConflictException>(() => Sut().MoveLocationAsync(ManagerSite, StableAId, StableBId));
    }

    [Test]
    public async Task Move_WithoutOpenOutbreak_ChangesParent_UnderItselfRejected()
    {
        await SeedManagerAsync();
        var sut = Sut();
        await sut.MoveLocationAsync(ManagerSite, Pen501Id, SectionId);
        await Assert.ThrowsAsync<TailBiteValidationException>(() => sut.MoveLocationAsync(ManagerSite, StableAId, SectionId));
        Db.ChangeTracker.Clear();
        Assert.That(Db.TailBiteLocations.Single(l => l.Id == Pen501Id).ParentId, Is.EqualTo(SectionId));
    }

    [Test]
    public async Task Move_DeepeningSubtreeBelowRuleCountDepth_Conflict_ShallowEnoughRuleAllowed()
    {
        await SeedManagerAsync();
        var sut = Sut();
        await new TailBiteRule { LocationId = Pen501Id, MinBittenPigs = 3, WindowDays = 7, CountDepth = 2 }.Create(Db);
        // Sti 501 sits at depth 2; under Sektion 4 it would be depth 3, above which its rule (CountDepth 2) must not sum.
        await Assert.ThrowsAsync<TailBiteConflictException>(() => sut.MoveLocationAsync(ManagerSite, Pen501Id, SectionId));
        Db.ChangeTracker.Clear();
        Assert.That(Db.TailBiteLocations.Single(l => l.Id == Pen501Id).ParentId, Is.EqualTo(StableBId));

        var rule = Db.TailBiteRules.Single(r => r.LocationId == Pen501Id);
        rule.CountDepth = 3;
        await rule.Update(Db);
        await Assert.DoesNotThrowAsync(() => sut.MoveLocationAsync(ManagerSite, Pen501Id, SectionId));
        Db.ChangeTracker.Clear();
        Assert.That(Db.TailBiteLocations.Single(l => l.Id == Pen501Id).ParentId, Is.EqualTo(SectionId));
    }

    [Test]
    public async Task AddRuleInsideOpenSubtree_Refused_ElsewhereAllowed()
    {
        await SeedManagerAsync(); await OpenOutbreakAsync(StableAId);
        var sut = Sut();
        await Assert.ThrowsAsync<TailBiteConflictException>(() => sut.CreateRuleAsync(ManagerSite, new RuleInput(SectionId, 3, null, 7, 3)));
        await Assert.DoesNotThrowAsync(() => sut.CreateRuleAsync(ManagerSite, new RuleInput(StableBId, 3, null, 7, 2)));
    }

    [Test]
    public async Task AddRuleAtOpenNode_Refused()
    {
        await SeedManagerAsync(); await OpenOutbreakAsync(StableAId);
        await Assert.ThrowsAsync<TailBiteConflictException>(() => Sut().CreateRuleAsync(ManagerSite, new RuleInput(StableAId, 3, null, 7, 1)));
    }

    [Test]
    public async Task AddRuleBetweenRuleNodeAndOpenNode_Refused()
    {
        await SeedManagerAsync();
        var rule = await Db.TailBiteRules.SingleAsync(r => r.Id == RuleId);
        rule.CountDepth = 2; // the root rule sums per section
        await rule.Update(Db);
        await OpenOutbreakAsync(SectionId);
        await Assert.ThrowsAsync<TailBiteConflictException>(() => Sut().CreateRuleAsync(ManagerSite, new RuleInput(StableAId, 3, null, 7, 2)));
    }

    // ---------- rules ----------

    [Test]
    public async Task ChangeCountDepthOrDelete_RuleWithOpenOutbreak_Refused_ThresholdChangeAllowedAndVersioned()
    {
        await SeedManagerAsync(); await OpenOutbreakAsync(StableAId);
        var sut = Sut();
        await Assert.ThrowsAsync<TailBiteConflictException>(() => sut.UpdateRuleAsync(ManagerSite, RuleId, new RuleInput(RootId, 5, 1, 7, 2)));
        await Assert.ThrowsAsync<TailBiteConflictException>(() => sut.DeleteRuleAsync(ManagerSite, RuleId));
        await sut.UpdateRuleAsync(ManagerSite, RuleId, new RuleInput(RootId, 4, 1, 7, 1));
        Db.ChangeTracker.Clear();
        var rule = Db.TailBiteRules.Single(r => r.Id == RuleId);
        Assert.That((rule.Version, rule.MinBittenPigs), Is.EqualTo((2, (int?)4)));
    }

    [Test]
    public async Task RuleValidation_CountDepthAboveRuleNode_Rejected_NoThreshold_Rejected()
    {
        await SeedManagerAsync();
        var sut = Sut();
        await Assert.ThrowsAsync<TailBiteValidationException>(() => sut.CreateRuleAsync(ManagerSite, new RuleInput(SectionId, 3, null, 7, 1)));
        await Assert.ThrowsAsync<TailBiteValidationException>(() => sut.CreateRuleAsync(ManagerSite, new RuleInput(StableBId, null, null, 7, 1)));
    }

    [Test]
    public async Task SetOccupancy_UpsertsPerValidFrom()
    {
        await SeedManagerAsync();
        var sut = Sut();
        var from = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        await sut.SetOccupancyAsync(ManagerSite, Pen309Id, 30, from);
        await sut.SetOccupancyAsync(ManagerSite, Pen309Id, 28, from);
        await Assert.ThrowsAsync<TailBiteValidationException>(() => sut.SetOccupancyAsync(ManagerSite, Pen309Id, -1, from));
        Db.ChangeTracker.Clear();
        var occ = await Db.TailBiteOccupancies.SingleAsync(o => o.LocationId == Pen309Id);
        Assert.That((occ.PigCount, occ.Source), Is.EqualTo((28, TailBiteOccupancySource.Manual)));
    }

    // ---------- preview (§6.4) ----------

    [Test]
    public async Task Preview_WritesNothing_AndCountsOutbreaks()
    {
        await SeedManagerAsync();
        await SeedBurstAsync(-10, 3); // three bites of 2 pigs in Stald A → with threshold 5 that is one outbreak
        var preview = await Sut().PreviewRuleAsync(ManagerSite, new RuleInput(RootId, 5, null, 7, 1));
        Assert.That(preview.OutbreaksWouldOpen, Is.EqualTo(1));
        Assert.That(preview.PerSummingLocation[StableAId], Is.EqualTo(1));
        Assert.That(await Db.TailBiteOutbreaks.CountAsync(), Is.EqualTo(0));
    }

    [Test]
    public async Task Preview_UsesCandidateNotStoredRule()
    {
        await SeedManagerAsync();
        await SeedBurstAsync(-10, 3); // 6 bitten pigs: the stored rule (5) would open one
        var preview = await Sut().PreviewRuleAsync(ManagerSite, new RuleInput(RootId, 7, null, 7, 1));
        Assert.That(preview.OutbreaksWouldOpen, Is.EqualTo(0));
        Assert.That(preview.PerSummingLocation, Is.Empty);
    }

    [Test]
    public async Task Preview_DeeperRuleKeepsRowsFromCountingAtRootRuleNode()
    {
        await SeedManagerAsync();
        // Stald A has its own rule (per pen, threshold 100): Sti 309's rows belong to it, not to the root candidate.
        await new TailBiteRule { LocationId = StableAId, MinBittenPigs = 100, WindowDays = 7, CountDepth = 3 }.Create(Db);
        await SeedBurstAsync(-10, 3);
        var preview = await Sut().PreviewRuleAsync(ManagerSite, new RuleInput(RootId, 5, null, 7, 1));
        Assert.That(preview.OutbreaksWouldOpen, Is.EqualTo(0));
    }

    // Burst 1: 3 × 2 pigs on days -40, -39, -38 in Sti 309 → opens at day -38 (6 ≥ 5); handled at day -38 + 7 = -31.
    // Burst 2 from day -20 starts after that → a second outbreak. From -36 it is before -31 → it joins the first.
    // From exactly -31 (= HandledAt) its first row no longer joins → the burst opens a second outbreak.
    [TestCase(-20, 2)]
    [TestCase(-36, 1)]
    [TestCase(-31, 2)]
    public async Task Preview_OutbreakHandledAfterWindow_SecondBurstOpensSecond(int secondBurstFirstDay, int expectedOutbreaks)
    {
        await SeedManagerAsync();
        await SeedBurstAsync(-40, 3);
        await SeedBurstAsync(secondBurstFirstDay, 3);

        var preview = await Sut().PreviewRuleAsync(ManagerSite, new RuleInput(RootId, 5, null, 7, 1));

        Assert.That(preview.OutbreaksWouldOpen, Is.EqualTo(expectedOutbreaks));
        Assert.That(preview.PerSummingLocation[StableAId], Is.EqualTo(expectedOutbreaks));
    }

    // ---------- managers ----------

    [Test]
    public async Task SetManager_TogglesFlag()
    {
        await SeedTreeAsync(); var pw = await SeedWorkerAsync(WorkerSite);
        await Sut().SetManagerAsync(pw.Id, true);
        Db.ChangeTracker.Clear();
        Assert.That(Db.PropertyWorkers.Single(x => x.Id == pw.Id).TailBiteManager, Is.True);
    }

    [Test]
    public async Task SetManager_PropertyNotEnabled_NotFound()
    {
        await SeedTreeAsync(enabled: false); var pw = await SeedWorkerAsync(WorkerSite);
        await Assert.ThrowsAsync<TailBiteNotFoundException>(() => Sut().SetManagerAsync(pw.Id, true));
    }

    // ---------- farm-editable action types (§5, §7.3) ----------

    [Test]
    public async Task ActionTypes_CreateRenameDelete_HappyPath()
    {
        await SeedManagerAsync();
        await new TailBiteActionType { PropertyId = PropertyId, Code = "HALM", Name = "Halm", SortOrder = 4 }.Create(Db);
        var sut = Sut();

        var id = await sut.CreateActionTypeAsync(ManagerSite, PropertyId, "  Vand tjekket ");
        var created = (await sut.ListActionTypesAsync(ManagerSite, PropertyId)).Single(a => a.Id == id);
        Assert.That(created, Is.EqualTo(new ActionTypeDto(id, $"CUSTOM_{id}", "Vand tjekket", 5)));

        await sut.RenameActionTypeAsync(ManagerSite, id, "Vandtryk tjekket");
        Assert.That((await sut.ListActionTypesAsync(ManagerSite, PropertyId)).Single(a => a.Id == id).Name, Is.EqualTo("Vandtryk tjekket"));

        await sut.DeleteActionTypeAsync(ManagerSite, id);
        Assert.That((await sut.ListActionTypesAsync(ManagerSite, PropertyId)).Select(a => a.Id), Does.Not.Contain(id));
        Assert.That((await sut.GetTreeAsync(ManagerSite, PropertyId)).ActionTypes.Select(a => a.Id), Does.Not.Contain(id));
        Db.ChangeTracker.Clear();
        Assert.That(Db.TailBiteActionTypes.Single(a => a.Id == id).WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
    }

    [Test]
    public async Task ActionTypes_DuplicateNameRejected_NameOfDeletedTypeReusable()
    {
        await SeedManagerAsync();
        var sut = Sut();
        var first = await sut.CreateActionTypeAsync(ManagerSite, PropertyId, "Halm");
        var second = await sut.CreateActionTypeAsync(ManagerSite, PropertyId, "Reb");
        await Assert.ThrowsAsync<TailBiteValidationException>(() => sut.CreateActionTypeAsync(ManagerSite, PropertyId, "Halm"));
        await Assert.ThrowsAsync<TailBiteValidationException>(() => sut.RenameActionTypeAsync(ManagerSite, second, "Halm"));
        await sut.DeleteActionTypeAsync(ManagerSite, first);
        await Assert.DoesNotThrowAsync(() => sut.CreateActionTypeAsync(ManagerSite, PropertyId, "Halm"));
    }

    [Test]
    public async Task ActionTypes_NonManagerForbidden_WorkerCanList()
    {
        await SeedManagerAsync(); await SeedWorkerAsync(WorkerSite);
        var sut = Sut();
        var id = await sut.CreateActionTypeAsync(ManagerSite, PropertyId, "Halm");
        await Assert.ThrowsAsync<TailBiteForbiddenException>(() => sut.CreateActionTypeAsync(WorkerSite, PropertyId, "Reb"));
        await Assert.ThrowsAsync<TailBiteForbiddenException>(() => sut.RenameActionTypeAsync(WorkerSite, id, "Reb"));
        await Assert.ThrowsAsync<TailBiteForbiddenException>(() => sut.DeleteActionTypeAsync(WorkerSite, id));
        Assert.That((await sut.ListActionTypesAsync(WorkerSite, PropertyId)).Single().Id, Is.EqualTo(id));
        await Assert.ThrowsAsync<TailBiteForbiddenException>(() => sut.ListActionTypesAsync(9, PropertyId));
    }

    // ---------- id probing ----------

    [Test]
    public async Task MissingIds_RefusedLikeForeignIds()
    {
        await SeedManagerAsync();
        var other = new Property { Name = "Ejendom Anden" };
        await other.Create(Db);
        var foreignLocation = new TailBiteLocation { PropertyId = other.Id, Name = "Stald X", QrCode = TailBiteDefaults.NewQrCode() };
        await foreignLocation.Create(Db);
        var foreignRule = TailBiteDefaults.DefaultRule(foreignLocation.Id);
        await foreignRule.Create(Db);
        var foreignType = new TailBiteActionType { PropertyId = other.Id, Code = "HALM", Name = "Halm" };
        await foreignType.Create(Db);
        var sut = Sut();
        const int missing = int.MaxValue;
        var foreignId = foreignLocation.Id;

        await AssertRefusedAlike(() => sut.CreateLocationAsync(ManagerSite, missing, "Sti 1"), () => sut.CreateLocationAsync(ManagerSite, foreignId, "Sti 1"));
        await AssertRefusedAlike(() => sut.CreatePenRangeAsync(ManagerSite, missing, "Sti", 1, 2), () => sut.CreatePenRangeAsync(ManagerSite, foreignId, "Sti", 1, 2));
        await AssertRefusedAlike(() => sut.RenameLocationAsync(ManagerSite, missing, "Ny"), () => sut.RenameLocationAsync(ManagerSite, foreignId, "Ny"));
        await AssertRefusedAlike(() => sut.MoveLocationAsync(ManagerSite, missing, RootId), () => sut.MoveLocationAsync(ManagerSite, foreignId, RootId));
        await AssertRefusedAlike(() => sut.DeleteLocationAsync(ManagerSite, missing), () => sut.DeleteLocationAsync(ManagerSite, foreignId));
        await AssertRefusedAlike(() => sut.SetOccupancyAsync(ManagerSite, missing, 10, Clock.GetUtcNow().UtcDateTime),
            () => sut.SetOccupancyAsync(ManagerSite, foreignId, 10, Clock.GetUtcNow().UtcDateTime));
        await AssertRefusedAlike(() => sut.CreateRuleAsync(ManagerSite, new RuleInput(missing, 3, null, 7, 1)),
            () => sut.CreateRuleAsync(ManagerSite, new RuleInput(foreignId, 3, null, 7, 1)));
        await AssertRefusedAlike(() => sut.PreviewRuleAsync(ManagerSite, new RuleInput(missing, 3, null, 7, 1)),
            () => sut.PreviewRuleAsync(ManagerSite, new RuleInput(foreignId, 3, null, 7, 1)));
        await AssertRefusedAlike(() => sut.UpdateRuleAsync(ManagerSite, missing, new RuleInput(foreignId, 3, null, 7, 1)),
            () => sut.UpdateRuleAsync(ManagerSite, foreignRule.Id, new RuleInput(foreignId, 3, null, 7, 1)));
        await AssertRefusedAlike(() => sut.DeleteRuleAsync(ManagerSite, missing), () => sut.DeleteRuleAsync(ManagerSite, foreignRule.Id));
        await AssertRefusedAlike(() => sut.RenameActionTypeAsync(ManagerSite, missing, "Reb"), () => sut.RenameActionTypeAsync(ManagerSite, foreignType.Id, "Reb"));
        await AssertRefusedAlike(() => sut.DeleteActionTypeAsync(ManagerSite, missing), () => sut.DeleteActionTypeAsync(ManagerSite, foreignType.Id));
        await AssertRefusedAlike(() => sut.CreateActionTypeAsync(ManagerSite, missing, "Reb"), () => sut.CreateActionTypeAsync(ManagerSite, other.Id, "Reb"));
    }
}
