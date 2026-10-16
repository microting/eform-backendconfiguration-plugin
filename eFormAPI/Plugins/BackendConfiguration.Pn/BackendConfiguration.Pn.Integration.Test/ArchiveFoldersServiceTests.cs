#nullable enable
using BackendConfiguration.Pn.Infrastructure.Models.ArchiveFolders;
using BackendConfiguration.Pn.Services.ArchiveAccess;
using BackendConfiguration.Pn.Services.ArchiveFolders;
using Microsoft.EntityFrameworkCore;
using Microting.eFormApi.BasePn.Abstractions;
using NSubstitute;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>The archive folder tree: counts, visibility, the 2-level rule, names (spec D1–D17, section 4).</summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class ArchiveFoldersServiceTests : TestBaseSetup
{
    private const int G1 = 701;
    private const int G2 = 702;
    private FixedArchiveAccess _access = null!;
    private ArchiveFoldersService _service = null!;
    private int _arkiv;

    [SetUp]
    public async Task SetUpService()
    {
        await ArchiveTestData.ClearAsync(BackendConfigurationPnDbContext!);
        _arkiv = await ArchiveTestData.DefaultFolderIdAsync(BackendConfigurationPnDbContext!);
        _access = new FixedArchiveAccess(ArchiveTestData.Admin, BackendConfigurationPnDbContext!, G1, G2);
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(3);
        _service = new ArchiveFoldersService(BackendConfigurationPnDbContext!, _access,
            new BackendConfigurationLocalizationService(), userService);
    }

    private static ArchiveFolderModel Folder(List<ArchiveFolderModel> tree, string name) =>
        tree.Single(f => f.Name == name);

    private async Task<List<ArchiveFolderModel>> Index(params int[] propertyIds)
    {
        var result = await _service.IndexAsync(propertyIds.ToList());
        Assert.That(result.Success, Is.True, result.Message);
        return result.Model;
    }

    [Test]
    public async Task Index_Admin_CountsTopFolderWithItsSubfolders_AndKeepsEmptyFolders()
    {
        var db = BackendConfigurationPnDbContext!;
        var drift = await ArchiveTestData.FolderAsync(db, "Drift");
        var fakturaer = await ArchiveTestData.FolderAsync(db, "Fakturaer", drift.Id);
        await ArchiveTestData.FolderAsync(db, "Tom");
        await ArchiveTestData.FileAsync(db, drift.Id, "Plan");
        await ArchiveTestData.FileAsync(db, fakturaer.Id, "Faktura 1");
        await ArchiveTestData.FileAsync(db, fakturaer.Id, "Faktura 2");

        var tree = await Index();

        Assert.That(tree.Select(f => f.Name), Is.EqualTo(new[] { "Arkiv", "Drift", "Fakturaer", "Tom" }));
        Assert.That(Folder(tree, "Drift").Count, Is.EqualTo(3));
        Assert.That(Folder(tree, "Fakturaer").Count, Is.EqualTo(2));
        Assert.That(Folder(tree, "Fakturaer").ParentId, Is.EqualTo(drift.Id));
        Assert.That(Folder(tree, "Tom").Count, Is.EqualTo(0));
        Assert.That(Folder(tree, "Arkiv").IsDefault, Is.True);
    }

    [Test]
    public async Task Index_PropertyFilter_CountsOnlyLinkedDocuments()
    {
        var db = BackendConfigurationPnDbContext!;
        var nord = await InboxTestData.PropertyAsync(db, "Nordvej 12");
        var syd = await InboxTestData.PropertyAsync(db, "Sydvej 3", "Sydvej 3, 8000 Aarhus C");
        var drift = await ArchiveTestData.FolderAsync(db, "Drift");
        await ArchiveTestData.FileAsync(db, drift.Id, "Nord", propertyIds: [nord.Id]);
        await ArchiveTestData.FileAsync(db, drift.Id, "Begge", propertyIds: [nord.Id, syd.Id]);
        await ArchiveTestData.FileAsync(db, drift.Id, "Syd", propertyIds: [syd.Id]);

        Assert.That(Folder(await Index(nord.Id), "Drift").Count, Is.EqualTo(2));
        Assert.That(Folder(await Index(nord.Id, syd.Id), "Drift").Count, Is.EqualTo(3));
        Assert.That(Folder(await Index(), "Drift").Count, Is.EqualTo(3));
    }

    [Test]
    public async Task Index_NonAdmin_FolderVisibilityAndReadableCounts()
    {
        var db = BackendConfigurationPnDbContext!;
        var ledelse = await ArchiveTestData.FolderAsync(db, "Ledelse", null, G1);
        var loen = await ArchiveTestData.FolderAsync(db, "Løn", ledelse.Id, G2);
        await ArchiveTestData.FolderAsync(db, "Hemmelig", null, G1);
        var drift = await ArchiveTestData.FolderAsync(db, "Drift");
        await ArchiveTestData.FileAsync(db, ledelse.Id, "Referat");
        await ArchiveTestData.FileAsync(db, loen.Id, "Lønseddel");
        await ArchiveTestData.FileAsync(db, drift.Id, "Åben");
        await ArchiveTestData.FileAsync(db, drift.Id, "Kun G1", ownAccess: true, ownGroups: [G1]);
        _access.Context = ArchiveTestData.User(G2);

        var tree = await Index();

        Assert.That(tree.Select(f => f.Name), Is.EquivalentTo(new[] { "Arkiv", "Ledelse", "Løn", "Drift" }));
        var ledelseRow = Folder(tree, "Ledelse");
        Assert.That(ledelseRow.Restricted, Is.True);
        Assert.That(ledelseRow.ReadGroups.Select(g => (g.Id, g.Name)), Is.EqualTo(new[] { (G1, $"Gruppe {G1}") }));
        Assert.That(ledelseRow.OwnGroups, Is.True);
        Assert.That(ledelseRow.Count, Is.EqualTo(1), "only Løn's document is readable for G2");
        Assert.That(Folder(tree, "Drift").Count, Is.EqualTo(1));
        Assert.That(Folder(tree, "Drift").Restricted, Is.False);
    }

    [Test]
    public async Task Index_NonAdmin_HiddenSubfolder_NotListedAndNotCounted()
    {
        var db = BackendConfigurationPnDbContext!;
        var drift = await ArchiveTestData.FolderAsync(db, "Drift");
        var hemmelig = await ArchiveTestData.FolderAsync(db, "Hemmelig", drift.Id, G1);
        await ArchiveTestData.FileAsync(db, drift.Id, "Plan");
        await ArchiveTestData.FileAsync(db, hemmelig.Id, "Skjult 1");
        await ArchiveTestData.FileAsync(db, hemmelig.Id, "Skjult 2");

        var adminTree = await Index();
        _access.Context = ArchiveTestData.User(G2);
        var tree = await Index();

        Assert.That(Folder(adminTree, "Hemmelig").Count, Is.EqualTo(2));
        Assert.That(Folder(adminTree, "Drift").Count, Is.EqualTo(3));
        Assert.That(tree.Select(f => f.Name), Has.No.Member("Hemmelig"));
        Assert.That(Folder(tree, "Drift").Count, Is.EqualTo(1), "the hidden subfolder's documents are not counted");
    }

    [Test]
    public async Task Index_SubfolderThatInherits_ShowsTheParentsGroupsAsEffective()
    {
        var db = BackendConfigurationPnDbContext!;
        var ledelse = await ArchiveTestData.FolderAsync(db, "Ledelse", null, G1);
        await ArchiveTestData.FolderAsync(db, "Referater", ledelse.Id);

        var referater = Folder(await Index(), "Referater");

        Assert.That(referater.ReadGroups.Select(g => g.Id), Is.EqualTo(new[] { G1 }));
        Assert.That(referater.OwnGroups, Is.False);
        Assert.That(referater.Restricted, Is.True);
    }

    [Test]
    public async Task Index_DisabledCaller_Throws()
    {
        _access.Context = ArchiveTestData.Disabled;

        await Assert.ThrowsAsync<ArchiveAccountDisabledException>(() => _service.IndexAsync([]));
    }

    [Test]
    public async Task Create_DisabledCaller_Throws_AndCreatesNothing()
    {
        _access.Context = ArchiveTestData.Disabled;

        var db = BackendConfigurationPnDbContext!;

        await Assert.ThrowsAsync<ArchiveAccountDisabledException>(() =>
            _service.CreateAsync(new ArchiveFolderCreateModel { Name = "Drift" }));
        Assert.That(await db.ArchiveFolders.CountAsync(f => !f.IsDefault), Is.EqualTo(0));
    }

    [Test]
    public async Task Index_NullFolderRow_CountsUnderTheDefaultFolder()
    {
        var db = BackendConfigurationPnDbContext!;
        var drift = await ArchiveTestData.FolderAsync(db, "Drift");
        await ArchiveTestData.FileAsync(db, null, "Uden mappe");
        await ArchiveTestData.FileAsync(db, drift.Id, "Plan");

        var tree = await Index();

        Assert.That(tree.Single(f => f.IsDefault).Count, Is.EqualTo(1));
        Assert.That(Folder(tree, "Drift").Count, Is.EqualTo(1));
    }

    [Test]
    public async Task Create_TopFolderAndSubfolder()
    {
        var top = await _service.CreateAsync(new ArchiveFolderCreateModel { Name = "  Drift  " });
        var sub = await _service.CreateAsync(new ArchiveFolderCreateModel { Name = "Fakturaer", ParentId = top.Model.Id });

        Assert.That(top.Success, Is.True, top.Message);
        Assert.That(top.Model.Name, Is.EqualTo("Drift"));
        Assert.That(sub.Success, Is.True, sub.Message);
        Assert.That(sub.Model.ParentId, Is.EqualTo(top.Model.Id));
        var db = BackendConfigurationPnDbContext!;
        var stored = await db.ArchiveFolders.AsNoTracking().SingleAsync(f => f.Id == sub.Model.Id);
        Assert.That(stored.CreatedByUserId, Is.EqualTo(3));
        Assert.That(stored.IsDefault, Is.False);
    }

    [Test]
    public async Task Create_UnderASubfolder_IsRefused_MaxTwoLevels()
    {
        var db = BackendConfigurationPnDbContext!;
        var drift = await ArchiveTestData.FolderAsync(db, "Drift");
        var fakturaer = await ArchiveTestData.FolderAsync(db, "Fakturaer", drift.Id);

        var result = await _service.CreateAsync(new ArchiveFolderCreateModel { Name = "2026", ParentId = fakturaer.Id });

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo("ArchiveMaxTwoLevels"));
    }

    [Test]
    public async Task Create_DuplicateName_IgnoringCaseAndSpaces_IsRefused()
    {
        var db = BackendConfigurationPnDbContext!;
        var drift = await ArchiveTestData.FolderAsync(db, "Drift");
        await ArchiveTestData.FolderAsync(db, "Fakturaer", drift.Id);

        var top = await _service.CreateAsync(new ArchiveFolderCreateModel { Name = " drift" });
        var sub = await _service.CreateAsync(new ArchiveFolderCreateModel { Name = "FAKTURAER", ParentId = drift.Id });
        var elsewhere = await _service.CreateAsync(new ArchiveFolderCreateModel { Name = "Fakturaer" });

        Assert.That(top.Message, Is.EqualTo("ArchiveFolderNameExistsTopLevel"));
        Assert.That(sub.Message, Is.EqualTo("ArchiveFolderNameExistsIn"));
        Assert.That(elsewhere.Success, Is.True, "the same name under another parent is allowed");
    }

    private static IEnumerable<TestCaseData> InvalidNames()
    {
        yield return new TestCaseData("   ").SetName("Create_BlankName_IsRefused");
        yield return new TestCaseData(new string('x', ArchiveFoldersService.MaxNameLength + 1))
            .SetName("Create_TooLongName_IsRefused");
    }

    [TestCaseSource(nameof(InvalidNames))]
    public async Task Create_InvalidName_IsRefused(string name)
    {
        var result = await _service.CreateAsync(new ArchiveFolderCreateModel { Name = name });

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo("ArchiveFolderNameInvalid"));
    }

    [Test]
    public async Task Create_UnknownParent_IsRefused()
    {
        var result = await _service.CreateAsync(new ArchiveFolderCreateModel { Name = "X", ParentId = 999999 });

        Assert.That(result.Message, Is.EqualTo("ArchiveFolderNotFound"));
    }

    [Test]
    public async Task Update_Rename_AndRenameToASiblingsName()
    {
        var db = BackendConfigurationPnDbContext!;
        var drift = await ArchiveTestData.FolderAsync(db, "Drift");
        await ArchiveTestData.FolderAsync(db, "Kontrakter");

        var renamed = await _service.UpdateAsync(drift.Id, new ArchiveFolderUpdateModel { Name = "Driften" });
        var clash = await _service.UpdateAsync(drift.Id, new ArchiveFolderUpdateModel { Name = "kontrakter" });

        Assert.That(renamed.Success, Is.True, renamed.Message);
        Assert.That(clash.Message, Is.EqualTo("ArchiveFolderNameExistsTopLevel"));
        var stored = await db.ArchiveFolders.AsNoTracking().SingleAsync(f => f.Id == drift.Id);
        Assert.That(stored.Name, Is.EqualTo("Driften"));
    }

    [Test]
    public async Task Update_DefaultFolder_CanBeRenamedButNotMoved()
    {
        var db = BackendConfigurationPnDbContext!;
        var drift = await ArchiveTestData.FolderAsync(db, "Drift");

        var renamed = await _service.UpdateAsync(_arkiv, new ArchiveFolderUpdateModel { Name = "Indbakke-arkiv" });
        var moved = await _service.UpdateAsync(_arkiv, new ArchiveFolderUpdateModel { ParentId = drift.Id });

        Assert.That(renamed.Success, Is.True, renamed.Message);
        Assert.That(moved.Message, Is.EqualTo("ArchiveDefaultFolderCannotMove"));
    }

    [Test]
    public async Task Update_MoveThatWouldMakeThreeLevels_IsRefused()
    {
        var db = BackendConfigurationPnDbContext!;
        var drift = await ArchiveTestData.FolderAsync(db, "Drift");
        var fakturaer = await ArchiveTestData.FolderAsync(db, "Fakturaer", drift.Id);
        var kontrakter = await ArchiveTestData.FolderAsync(db, "Kontrakter");

        var underSubfolder = await _service.UpdateAsync(kontrakter.Id, new ArchiveFolderUpdateModel { ParentId = fakturaer.Id });
        var parentWithChildren = await _service.UpdateAsync(drift.Id, new ArchiveFolderUpdateModel { ParentId = kontrakter.Id });

        Assert.That(underSubfolder.Message, Is.EqualTo("ArchiveMaxTwoLevels"));
        Assert.That(parentWithChildren.Message, Is.EqualTo("ArchiveMaxTwoLevels"));
    }

    [Test]
    public async Task Update_MoveUnderRestrictedParent_ReportsDocumentsWhoseAccessChanged_ThenMoveToTop()
    {
        var db = BackendConfigurationPnDbContext!;
        var ledelse = await ArchiveTestData.FolderAsync(db, "Ledelse", null, G1);
        var kontrakter = await ArchiveTestData.FolderAsync(db, "Kontrakter");
        await ArchiveTestData.FileAsync(db, kontrakter.Id, "Lejekontrakt");
        await ArchiveTestData.FileAsync(db, kontrakter.Id, "Egen adgang", ownAccess: true, ownGroups: [G2]);

        var moved = await _service.UpdateAsync(kontrakter.Id, new ArchiveFolderUpdateModel { ParentId = ledelse.Id });
        var back = await _service.UpdateAsync(kontrakter.Id, new ArchiveFolderUpdateModel { MoveToTop = true });

        Assert.That(moved.Success, Is.True, moved.Message);
        Assert.That(moved.Model.AccessChanged, Is.EqualTo(1), "own access travels with the document (D11)");
        Assert.That(back.Model.AccessChanged, Is.EqualTo(1));
        var stored = await db.ArchiveFolders.AsNoTracking().SingleAsync(f => f.Id == kontrakter.Id);
        Assert.That(stored.ParentId, Is.Null);
    }

    [Test]
    public async Task Update_UnknownFolder_IsNotFound() =>
        await Assert.ThrowsAsync<ArchiveNotFoundException>(() =>
            _service.UpdateAsync(999999, new ArchiveFolderUpdateModel { Name = "X" }));

    [Test]
    public async Task Delete_FolderWithDocumentsOrSubfolders_IsRefused()
    {
        var db = BackendConfigurationPnDbContext!;
        var drift = await ArchiveTestData.FolderAsync(db, "Drift");
        await ArchiveTestData.FolderAsync(db, "Fakturaer", drift.Id);
        var kontrakter = await ArchiveTestData.FolderAsync(db, "Kontrakter");
        await ArchiveTestData.FileAsync(db, kontrakter.Id);

        Assert.That((await _service.DeleteAsync(drift.Id)).Message, Is.EqualTo("ArchiveFolderNotEmpty"));
        Assert.That((await _service.DeleteAsync(kontrakter.Id)).Message, Is.EqualTo("ArchiveFolderNotEmpty"));
    }

    [Test]
    public async Task Delete_DefaultFolder_IsRefusedEvenWhenEmpty()
    {
        Assert.That((await _service.DeleteAsync(_arkiv)).Message, Is.EqualTo("ArchiveDefaultFolderCannotDelete"));
    }

    [Test]
    public async Task Delete_EmptyFolder_IsSoftDeletedWithItsReadTags()
    {
        var db = BackendConfigurationPnDbContext!;
        var tom = await ArchiveTestData.FolderAsync(db, "Tom", null, G1);

        var result = await _service.DeleteAsync(tom.Id);

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That((await Index()).Select(f => f.Id), Does.Not.Contain(tom.Id));
        Assert.That(await db.ArchiveFolderReadTags
            .AnyAsync(t => t.ArchiveFolderId == tom.Id && t.WorkflowState != "removed"), Is.False);
    }

    [Test]
    public async Task UpdateAccess_DefaultFolder_CannotBeRestricted()
    {
        var result = await _service.UpdateAccessAsync(_arkiv, new ArchiveFolderAccessModel { TagIds = [G1] });

        Assert.That(result.Message, Is.EqualTo("ArchiveDefaultFolderCannotRestrict"));
    }

    [Test]
    public async Task UpdateAccess_GroupWithoutLiveMembers_IsRefused()
    {
        var db = BackendConfigurationPnDbContext!;
        var drift = await ArchiveTestData.FolderAsync(db, "Drift");

        var result = await _service.UpdateAccessAsync(drift.Id, new ArchiveFolderAccessModel { TagIds = [999] });

        Assert.That(result.Message, Is.EqualTo("ArchiveUnknownGroup"));
    }

    [Test]
    public async Task UpdateAccess_OnTopFolder_CountsItsAndInheritingSubfoldersDocuments()
    {
        var db = BackendConfigurationPnDbContext!;
        var drift = await ArchiveTestData.FolderAsync(db, "Drift");
        var fakturaer = await ArchiveTestData.FolderAsync(db, "Fakturaer", drift.Id);
        var loen = await ArchiveTestData.FolderAsync(db, "Løn", drift.Id, G2);
        await ArchiveTestData.FileAsync(db, drift.Id, "Plan");
        await ArchiveTestData.FileAsync(db, fakturaer.Id, "Faktura");
        await ArchiveTestData.FileAsync(db, loen.Id, "Lønseddel");
        await ArchiveTestData.FileAsync(db, drift.Id, "Egen", ownAccess: true);

        var result = await _service.UpdateAccessAsync(drift.Id, new ArchiveFolderAccessModel { TagIds = [G1] });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Model.AccessChanged, Is.EqualTo(2), "Plan and Faktura; Løn has its own groups, Egen own access");
        Assert.That((await Index()).Single(f => f.Id == drift.Id).ReadGroups.Select(g => g.Id), Is.EqualTo(new[] { G1 }));
    }

    [Test]
    public async Task UpdateAccess_ClearAgain_FolderIsOpenForNonAdmin()
    {
        var db = BackendConfigurationPnDbContext!;
        var drift = await ArchiveTestData.FolderAsync(db, "Drift", null, G1);
        await ArchiveTestData.FileAsync(db, drift.Id, "Plan");

        var cleared = await _service.UpdateAccessAsync(drift.Id, new ArchiveFolderAccessModel { TagIds = [] });
        _access.Context = ArchiveTestData.User();

        Assert.That(cleared.Model.AccessChanged, Is.EqualTo(1));
        var row = (await Index()).Single(f => f.Id == drift.Id);
        Assert.That(row.Restricted, Is.False);
        Assert.That(row.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task UpdateAccess_KeepsAGroupThatLostItsMembers()
    {
        const int deadGroup = 777;
        var db = BackendConfigurationPnDbContext!;
        var drift = await ArchiveTestData.FolderAsync(db, "Drift", null, deadGroup);

        var result = await _service.UpdateAccessAsync(drift.Id, new ArchiveFolderAccessModel { TagIds = [deadGroup, G1] });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That((await Index()).Single(f => f.Id == drift.Id).ReadGroups.Select(g => g.Id),
            Is.EquivalentTo(new[] { deadGroup, G1 }));
    }

    [Test]
    public async Task Update_MoveUnderItself_IsRefused()
    {
        var db = BackendConfigurationPnDbContext!;
        var drift = await ArchiveTestData.FolderAsync(db, "Drift");

        var result = await _service.UpdateAsync(drift.Id, new ArchiveFolderUpdateModel { ParentId = drift.Id });

        Assert.That(result.Message, Is.EqualTo("ArchiveMaxTwoLevels"));
        var stored = await db.ArchiveFolders.AsNoTracking().SingleAsync(f => f.Id == drift.Id);
        Assert.That(stored.ParentId, Is.Null);
    }

    [Test]
    public async Task Update_NothingChanged_SavesNoNewVersion()
    {
        var db = BackendConfigurationPnDbContext!;
        var drift = await ArchiveTestData.FolderAsync(db, "Drift");

        var result = await _service.UpdateAsync(drift.Id, new ArchiveFolderUpdateModel { Name = " Drift " });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Model.AccessChanged, Is.EqualTo(0));
        var stored = await db.ArchiveFolders.AsNoTracking().SingleAsync(f => f.Id == drift.Id);
        Assert.That(stored.Version, Is.EqualTo(drift.Version));
    }

    [Test]
    public async Task UpdateAccess_ClearThenAddTheSameGroup_RestrictsAgainWithANewRow()
    {
        var db = BackendConfigurationPnDbContext!;
        var drift = await ArchiveTestData.FolderAsync(db, "Drift", null, G1);
        await ArchiveTestData.FileAsync(db, drift.Id, "Plan");

        await _service.UpdateAccessAsync(drift.Id, new ArchiveFolderAccessModel { TagIds = [] });
        var again = await _service.UpdateAccessAsync(drift.Id, new ArchiveFolderAccessModel { TagIds = [G1] });
        _access.Context = ArchiveTestData.User(G2);

        Assert.That(again.Success, Is.True, again.Message);
        Assert.That(again.Model.AccessChanged, Is.EqualTo(1));
        Assert.That((await Index()).Select(f => f.Id), Does.Not.Contain(drift.Id), "restricted again for a non-member");
        var rows = await db.ArchiveFolderReadTags.AsNoTracking()
            .Where(t => t.ArchiveFolderId == drift.Id && t.SdkTagId == G1).ToListAsync();
        Assert.That(rows.Select(t => t.WorkflowState), Is.EquivalentTo(new[] { "removed", "created" }));
    }
}
