#nullable enable
using BackendConfiguration.Pn.Infrastructure.Models.Files;
using BackendConfiguration.Pn.Services.ArchiveAccess;
using BackendConfiguration.Pn.Services.BackendConfigurationFileTagsService;
using BackendConfiguration.Pn.Services.BackendConfigurationFilesService;
using BackendConfiguration.Pn.Services.FileArchive;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using File = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.File;
using NSubstitute;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// Move (D11, D17), document date (D8) and per-document read access. Tree: Arkiv (default) · Drift (open)
/// › Fakturaer (G1) · Kontrakter (open) · Ledelse (G1) · Løn (G2).
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class FilesMoveDateAccessTests : TestBaseSetup
{
    private const int G1 = 901;
    private const int G2 = 902;
    private FixedArchiveAccess _access = null!;
    private BackendConfigurationFilesService _service = null!;
    private int _drift, _fakturaer, _kontrakter, _ledelse, _loen;

    [SetUp]
    public async Task SetUpService()
    {
        var db = Db;
        await ArchiveTestData.ClearAsync(db);
        await ArchiveTestData.DefaultFolderIdAsync(db);
        _drift = (await ArchiveTestData.FolderAsync(db, "Drift")).Id;
        _fakturaer = (await ArchiveTestData.FolderAsync(db, "Fakturaer", _drift, G1)).Id;
        _kontrakter = (await ArchiveTestData.FolderAsync(db, "Kontrakter")).Id;
        _ledelse = (await ArchiveTestData.FolderAsync(db, "Ledelse", null, G1)).Id;
        _loen = (await ArchiveTestData.FolderAsync(db, "Løn", null, G2)).Id;
        _access = new FixedArchiveAccess(ArchiveTestData.Admin, db, G1, G2);
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(5);
        _service = new BackendConfigurationFilesService(NullLogger<BackendConfigurationTagsService>.Instance,
            new BackendConfigurationLocalizationService(), db, userService,
            new FileArchiver(db, Substitute.For<IArchiveStorage>()), BaseDbContext, _access);
    }

    private BackendConfigurationPnDbContext Db => BackendConfigurationPnDbContext!;

    private async Task<int> NewFile(int folderId, string name, bool ownAccess = false, int[]? ownGroups = null,
        DateTime? documentDate = null, DocumentDateState dateState = DocumentDateState.None) =>
        (await ArchiveTestData.FileAsync(Db, folderId, name, ownAccess, ownGroups, documentDate: documentDate,
            dateState: dateState)).Id;

    private Task<File> StoredFile(int fileId) => Db.Files.AsNoTracking().SingleAsync(f => f.Id == fileId);

    private async Task<int?> FolderOf(int fileId) => (await StoredFile(fileId)).FolderId;

    private async Task<ArchiveFileAccessRow> AccessOf(int fileId) =>
        (await ArchiveAccessRules.LoadFileRowsAsync(Db, [fileId])).Single();

    private void AsUser(params int[] groups) => _access.Context = ArchiveTestData.User(groups);

    [Test]
    public async Task Move_Admin_MovesAndReportsAccessChanged()
    {
        var a = await NewFile(_drift, "a");
        var b = await NewFile(_drift, "b", ownAccess: true);

        var result = await _service.Move(new FilesMoveModel { FileIds = [a, b], FolderId = _fakturaer });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Model.Moved, Is.EqualTo(2));
        Assert.That(result.Model.AccessChanged, Is.EqualTo(1), "b keeps its own (open) access");
        Assert.That(await FolderOf(a), Is.EqualTo(_fakturaer));
        Assert.That(await FolderOf(b), Is.EqualTo(_fakturaer));
    }

    [Test]
    public async Task Move_NonAdmin_BetweenFoldersWithTheSameAccess_IsAllowed()
    {
        var a = await NewFile(_drift, "a");
        AsUser();

        var result = await _service.Move(new FilesMoveModel { FileIds = [a], FolderId = _kontrakter });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Model.AccessChanged, Is.EqualTo(0));
        Assert.That(await FolderOf(a), Is.EqualTo(_kontrakter));
    }

    [Test]
    public async Task Move_Leder_ThatChangesAccess_IsAllowed()
    {
        // Changing who can read a document is an admin's or a Leder's decision.
        var a = await NewFile(_drift, "a");
        _access.Context = ArchiveTestData.Leder;

        var result = await _service.Move(new FilesMoveModel { FileIds = [a], FolderId = _ledelse });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Model.AccessChanged, Is.EqualTo(1), "the move crosses an access boundary");
        Assert.That(await FolderOf(a), Is.EqualTo(_ledelse));
    }

    [Test]
    public async Task Move_NonAdmin_ThatChangesAccess_IsRefusedAndMovesNothing()
    {
        var a = await NewFile(_drift, "a");
        var b = await NewFile(_drift, "b", ownAccess: true);
        AsUser(G1);

        var result = await _service.Move(new FilesMoveModel { FileIds = [a, b], FolderId = _ledelse });

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo("ArchiveMoveChangesAccess"));
        Assert.That(await FolderOf(a), Is.EqualTo(_drift));
        Assert.That(await FolderOf(b), Is.EqualTo(_drift), "one refused document moves nothing");
    }

    [Test]
    public async Task Move_NonAdmin_DocumentWithOwnAccess_KeepsItsAccessSoMayMove()
    {
        var a = await NewFile(_drift, "a", ownAccess: true);
        AsUser(G1);

        var result = await _service.Move(new FilesMoveModel { FileIds = [a], FolderId = _ledelse });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(await FolderOf(a), Is.EqualTo(_ledelse));
        var row = await AccessOf(a);
        Assert.That(row.OwnAccess && row.OwnGroupIds.Count == 0, Is.True, "D11: own access travels unchanged");
    }

    [Test]
    public async Task Move_NonAdmin_IntoAFolderTheyCannotRead_IsNotFound()
    {
        var a = await NewFile(_drift, "a", ownAccess: true);
        AsUser(G1);

        await Assert.ThrowsAsync<ArchiveNotFoundException>(() =>
            _service.Move(new FilesMoveModel { FileIds = [a], FolderId = _loen }));
        Assert.That(await FolderOf(a), Is.EqualTo(_drift));
    }

    [Test]
    public async Task Move_AnUnreadableDocument_IsNotFound()
    {
        var hidden = await NewFile(_loen, "hidden");
        AsUser(G1);

        await Assert.ThrowsAsync<ArchiveNotFoundException>(() =>
            _service.Move(new FilesMoveModel { FileIds = [hidden], FolderId = _ledelse }));
        Assert.That(await FolderOf(hidden), Is.EqualTo(_loen));
    }

    [Test]
    public async Task Move_UnknownFolder_IsNotFound()
    {
        var a = await NewFile(_drift, "a");

        await Assert.ThrowsAsync<ArchiveNotFoundException>(() =>
            _service.Move(new FilesMoveModel { FileIds = [a], FolderId = 999999 }));
    }

    [Test]
    public async Task UpdateDate_SetApprovesClearIsNone()
    {
        var a = await NewFile(_drift, "a",
            documentDate: new DateTime(2026, 9, 1), dateState: DocumentDateState.Suggested);
        AsUser();

        var approved = await _service.UpdateDate(a, new FileDateModel { Date = new DateTime(2026, 9, 2, 8, 0, 0) });
        var stored = await StoredFile(a);
        var cleared = await _service.UpdateDate(a, new FileDateModel { Date = null });
        var clearedRow = await StoredFile(a);

        Assert.That(approved.Success, Is.True, approved.Message);
        Assert.That(stored.DocumentDate, Is.EqualTo(new DateTime(2026, 9, 2)));
        Assert.That(stored.DocumentDateState, Is.EqualTo(DocumentDateState.Approved));
        Assert.That(cleared.Success, Is.True);
        Assert.That(clearedRow.DocumentDate, Is.Null);
        Assert.That(clearedRow.DocumentDateState, Is.EqualTo(DocumentDateState.None));
    }

    [Test]
    public async Task UpdateDate_UnreadableDocument_IsNotFound()
    {
        var hidden = await NewFile(_loen, "hidden");
        AsUser(G1);

        await Assert.ThrowsAsync<ArchiveNotFoundException>(() =>
            _service.UpdateDate(hidden, new FileDateModel { Date = new DateTime(2026, 1, 1) }));
    }

    [Test]
    public async Task UpdateDate_RemovedDocument_IsNotFound()
    {
        var a = await NewFile(_drift, "a");
        var stored = await Db.Files.SingleAsync(f => f.Id == a);
        stored.WorkflowState = Microting.eForm.Infrastructure.Constants.Constants.WorkflowStates.Removed;
        await Db.SaveChangesAsync();
        AsUser();

        await Assert.ThrowsAsync<ArchiveNotFoundException>(() =>
            _service.UpdateDate(a, new FileDateModel { Date = new DateTime(2026, 1, 1) }));
    }

    [Test]
    public async Task UpdateAccess_OwnGroups_ThenBackToTheFolders()
    {
        var a = await NewFile(_fakturaer, "a");
        var b = await NewFile(_drift, "b");

        var own = await _service.UpdateAccess(new FilesAccessModel { FileIds = [a, b], OwnAccess = true, TagIds = [G1] });

        Assert.That(own.Success, Is.True, own.Message);
        Assert.That(own.Model.AccessChanged, Is.EqualTo(1), "a was already G1 through Fakturaer");
        var rows = await ArchiveAccessRules.LoadFileRowsAsync(Db, [a, b]);
        Assert.That(rows.All(r => r.OwnAccess && r.OwnGroupIds.SequenceEqual(new[] { G1 })), Is.True);

        var back = await _service.UpdateAccess(new FilesAccessModel { FileIds = [a, b], OwnAccess = false, TagIds = [G2] });

        Assert.That(back.Success, Is.True, back.Message);
        Assert.That(back.Model.AccessChanged, Is.EqualTo(1), "b is open again in Drift");
        var after = await ArchiveAccessRules.LoadFileRowsAsync(Db, [a, b]);
        Assert.That(after.All(r => !r.OwnAccess && r.OwnGroupIds.Count == 0), Is.True, "ownAccess=false clears own groups");
    }

    [Test]
    public async Task UpdateAccess_OwnAccessWithNoGroups_OpensADocumentInARestrictedFolder()
    {
        var a = await NewFile(_ledelse, "a");

        var result = await _service.UpdateAccess(new FilesAccessModel { FileIds = [a], OwnAccess = true, TagIds = [] });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Model.AccessChanged, Is.EqualTo(1));
        AsUser();
        Assert.That((await _service.GetById(a)).Success, Is.True);
    }

    [Test]
    public async Task UpdateAccess_NewGroupWithoutLiveMembers_IsRefused()
    {
        var a = await NewFile(_drift, "a");

        var result = await _service.UpdateAccess(new FilesAccessModel { FileIds = [a], OwnAccess = true, TagIds = [999] });

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo("ArchiveUnknownGroup"));
        Assert.That((await AccessOf(a)).OwnAccess,
            Is.False, "nothing changed");
    }

    [Test]
    public async Task UpdateAccess_KeptGroupWithoutLiveMembers_IsAllowed_ButNotWhenNewToAnotherDocument()
    {
        var a = await NewFile(_drift, "a", ownAccess: true, ownGroups: [999]);
        var b = await NewFile(_drift, "b");

        var kept = await _service.UpdateAccess(new FilesAccessModel { FileIds = [a], OwnAccess = true, TagIds = [999, G1] });
        var spread = await _service.UpdateAccess(new FilesAccessModel { FileIds = [a, b], OwnAccess = true, TagIds = [999] });

        Assert.That(kept.Success, Is.True, kept.Message);
        Assert.That(spread.Message, Is.EqualTo("ArchiveUnknownGroup"), "999 would be new on b");
    }

    [Test]
    public async Task UpdateAccess_NonAdmin_IsNotFound_AndChangesNothing()
    {
        var a = await NewFile(_drift, "a");
        AsUser(G1);

        await Assert.ThrowsAsync<ArchiveNotFoundException>(() =>
            _service.UpdateAccess(new FilesAccessModel { FileIds = [a], OwnAccess = true, TagIds = [G1] }));
        Assert.That((await AccessOf(a)).OwnAccess, Is.False);
    }

    [Test]
    public async Task UpdateAccess_Leder_SetsOwnGroups()
    {
        var a = await NewFile(_drift, "a");
        _access.Context = ArchiveTestData.Leder;

        var result = await _service.UpdateAccess(new FilesAccessModel { FileIds = [a], OwnAccess = true, TagIds = [G1] });

        Assert.That(result.Success, Is.True, result.Message);
        var row = await AccessOf(a);
        Assert.That(row.OwnAccess, Is.True);
        Assert.That(row.OwnGroupIds, Is.EqualTo(new[] { G1 }));
    }
}
