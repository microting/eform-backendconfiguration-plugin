#nullable enable
using System.IO.Compression;
using System.Text;
using BackendConfiguration.Pn.Controllers;
using BackendConfiguration.Pn.Infrastructure.Models.Files;
using BackendConfiguration.Pn.Services.ArchiveAccess;
using BackendConfiguration.Pn.Services.BackendConfigurationFileTagsService;
using BackendConfiguration.Pn.Services.BackendConfigurationFilesService;
using BackendConfiguration.Pn.Services.FileArchive;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using NSubstitute;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// The files endpoints under read access (spec section 4): list filter and fields, 404 for unreadable on fetch,
/// download, zip and writes, and uploads into a folder with a document date.
/// Tree: Arkiv (default) · Drift (open) › Fakturaer (G1).
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class FilesArchiveAccessTests : TestBaseSetup
{
    private const int G1 = 801;
    private FixedArchiveAccess _access = null!;
    private BackendConfigurationFilesService _service = null!;
    private IArchiveStorage _storage = null!;
    private int _arkiv, _drift, _fakturaer, _open, _restricted, _suggested;

    [SetUp]
    public async Task SetUpService()
    {
        var db = BackendConfigurationPnDbContext!;
        await ArchiveTestData.ClearAsync(db);
        _arkiv = await ArchiveTestData.DefaultFolderIdAsync(db);
        _drift = (await ArchiveTestData.FolderAsync(db, "Drift")).Id;
        _fakturaer = (await ArchiveTestData.FolderAsync(db, "Fakturaer", _drift, G1)).Id;
        _open = (await ArchiveTestData.FileAsync(db, _drift, "Serviceplan", documentDate: new DateTime(2026, 9, 1),
            dateState: DocumentDateState.Approved)).Id;
        _restricted = (await ArchiveTestData.FileAsync(db, _fakturaer, "Faktura 1002")).Id;
        _suggested = (await ArchiveTestData.FileAsync(db, _drift, "Tilbud", documentDate: new DateTime(2026, 8, 15),
            dateState: DocumentDateState.Suggested)).Id;

        _access = new FixedArchiveAccess(ArchiveTestData.User(), db, G1);
        _storage = Substitute.For<IArchiveStorage>();
        _storage.GetAsync(Arg.Any<string>())
            .Returns(_ => Task.FromResult<Stream?>(new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.7 x"))));
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(5);
        _service = new BackendConfigurationFilesService(NullLogger<BackendConfigurationTagsService>.Instance,
            new BackendConfigurationLocalizationService(), db, userService, new FileArchiver(db, _storage),
            BaseDbContext, _access);
    }

    private async Task<List<BackendConfigurationFilesModel>> List(BackendConfigurationFileRequestModel? request = null)
    {
        var result = await _service.Index(request ?? new BackendConfigurationFileRequestModel());
        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Model.Total, Is.EqualTo(result.Model.Entities.Count));
        return result.Model.Entities;
    }

    private FilesController Controller() =>
        new(_service, new BackendConfigurationLocalizationService(), NullLogger<FilesController>.Instance, _storage);

    [Test]
    public async Task Index_NonAdmin_ListsAndCountsOnlyReadableDocuments()
    {
        Assert.That((await List()).Select(f => f.Id), Is.EquivalentTo(new[] { _open, _suggested }));

        _access.Context = ArchiveTestData.User(G1);
        Assert.That((await List()).Select(f => f.Id), Is.EquivalentTo(new[] { _open, _restricted, _suggested }));
    }

    [Test]
    public async Task Index_FolderFilter_ListsDocumentsDirectlyInTheFolderOnly()
    {
        _access.Context = ArchiveTestData.Admin;

        var inDrift = await List(new BackendConfigurationFileRequestModel { FolderId = _drift });

        Assert.That(inDrift.Select(f => f.Id), Is.EquivalentTo(new[] { _open, _suggested }),
            "a top folder does not include its subfolders' documents");
    }

    [Test]
    public async Task Index_DateStateFilter_SelectsDatoSkalTjekkes()
    {
        var suggested = await List(new BackendConfigurationFileRequestModel { DateState = DocumentDateState.Suggested });

        Assert.That(suggested.Select(f => f.Id), Is.EqualTo(new[] { _suggested }));
    }

    [Test]
    public async Task Index_RowsCarryFolderPathDateAndEffectiveGroups()
    {
        _access.Context = ArchiveTestData.Admin;

        var rows = await List();

        var restricted = rows.Single(f => f.Id == _restricted);
        Assert.That(restricted.FolderId, Is.EqualTo(_fakturaer));
        Assert.That(restricted.FolderPath, Is.EqualTo(new[] { "Drift", "Fakturaer" }));
        Assert.That(restricted.ReadGroups.Select(g => (g.Id, g.Name)), Is.EqualTo(new[] { (G1, $"Gruppe {G1}") }));
        Assert.That(restricted.OwnAccess, Is.False);
        var open = rows.Single(f => f.Id == _open);
        Assert.That(open.DocumentDate, Is.EqualTo(new DateTime(2026, 9, 1)));
        Assert.That(open.DocumentDateState, Is.EqualTo(DocumentDateState.Approved));
        Assert.That(open.ReadGroups, Is.Empty);
    }

    [Test]
    public async Task Index_NullFolderRow_ListsUnderTheDefaultFolderWithItsPath()
    {
        var nullRow = (await ArchiveTestData.FileAsync(BackendConfigurationPnDbContext!, null, "Uden mappe")).Id;

        var inArkiv = await List(new BackendConfigurationFileRequestModel { FolderId = _arkiv });

        var row = inArkiv.Single();
        Assert.That(row.Id, Is.EqualTo(nullRow));
        Assert.That(row.FolderId, Is.EqualTo(_arkiv));
        Assert.That(row.FolderPath, Is.EqualTo(new[] { "Arkiv" }));
        var detail = await _service.GetById(nullRow);
        Assert.That(detail.Success, Is.True, detail.Message);
        Assert.That(detail.Model!.FolderId, Is.EqualTo(_arkiv));
        Assert.That((await List(new BackendConfigurationFileRequestModel { FolderId = _drift })).Select(f => f.Id),
            Does.Not.Contain(nullRow));
    }

    [Test]
    public async Task Index_DisabledCaller_Throws()
    {
        _access.Context = ArchiveTestData.Disabled;

        await Assert.ThrowsAsync<ArchiveAccountDisabledException>(() =>
            _service.Index(new BackendConfigurationFileRequestModel()));
    }

    [Test]
    public async Task GetById_Readable_CarriesTheNewFields()
    {
        var result = await _service.GetById(_open);

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Model.FolderPath, Is.EqualTo(new[] { "Drift" }));
        Assert.That(result.Model.DocumentDateState, Is.EqualTo(DocumentDateState.Approved));
    }

    [Test]
    public async Task GetById_UnreadableOrMissing_IsNotFound()
    {
        await Assert.ThrowsAsync<ArchiveNotFoundException>(() => _service.GetById(_restricted));
        await Assert.ThrowsAsync<ArchiveNotFoundException>(() => _service.GetById(999999));
    }

    [Test]
    public async Task GetFile_Unreadable_Is404_Readable_IsThePdf()
    {
        var controller = Controller();

        Assert.That(await controller.GetLoginPageImage(_restricted), Is.InstanceOf<NotFoundResult>());
        Assert.That(await controller.GetLoginPageImage(999999), Is.InstanceOf<NotFoundResult>());
        Assert.That(await controller.GetLoginPageImage(_open), Is.InstanceOf<FileStreamResult>());
    }

    [Test]
    public async Task GetFiles_OneUnreadableId_Is404ForTheWholeZip()
    {
        var result = await Controller().GetArchiveFiles(new BackendConfigurationArchiveFile
        {
            FileIds = [_open, _restricted], ArchiveName = "arkiv.zip"
        });

        Assert.That(result, Is.InstanceOf<NotFoundResult>());
    }

    [Test]
    public async Task GetFiles_AllReadable_ZipsEveryDocumentUnderItsCurrentName()
    {
        await _service.UpdateName(new BackendConfigurationFileUpdateFilenameModel
        {
            Id = _open, NewName = "Serviceplan 2026", PropertyIds = []
        });

        var result = await Controller().GetArchiveFiles(new BackendConfigurationArchiveFile
        {
            FileIds = [_open, _suggested], ArchiveName = "arkiv.zip"
        });

        var zip = (FileContentResult)result;
        using var archive = new ZipArchive(new MemoryStream(zip.FileContents), ZipArchiveMode.Read);
        Assert.That(archive.Entries.Select(e => e.FullName), Is.EqualTo(new[] { "Serviceplan 2026.pdf", "Tilbud.pdf" }));
    }

    [Test]
    public async Task Writes_OnAnUnreadableDocument_AreNotFound()
    {
        await Assert.ThrowsAsync<ArchiveNotFoundException>(() => _service.UpdateName(
            new BackendConfigurationFileUpdateFilenameModel { Id = _restricted, NewName = "x" }));
        await Assert.ThrowsAsync<ArchiveNotFoundException>(() => _service.UpdateTags(
            new BackendConfigurationFileUpdateFileTags { Id = _restricted }));
        await Assert.ThrowsAsync<ArchiveNotFoundException>(() => _service.UpdateProperties(
            new BackendConfigurationFileUpdateProperties { Id = _restricted }));
        await Assert.ThrowsAsync<ArchiveNotFoundException>(() => _service.Delete(_restricted));
    }

    [Test]
    public async Task Delete_ReadableDocument_StillWorks()
    {
        var result = await _service.Delete(_open);

        Assert.That(result.Success, Is.True, result.Message);
        var file = await BackendConfigurationPnDbContext!.Files.AsNoTracking().SingleAsync(f => f.Id == _open);
        Assert.That(file.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
    }

    private static BackendConfigurationFileCreate Upload(string fileName, int? folderId = null, DateTime? date = null) =>
        ArchiveTestData.Upload(fileName, folderId, date);

    [Test]
    public async Task Create_FolderIdZeroOrMissing_LandsInDefaultFolderWithStateNone()
    {
        var result = await _service.Create([Upload("Gammel app.pdf"), Upload("Nul.pdf", 0)]);

        Assert.That(result.Success, Is.True, result.Message);
        var files = await BackendConfigurationPnDbContext!.Files.AsNoTracking()
            .Where(f => f.FileName == "Gammel app" || f.FileName == "Nul").ToListAsync();
        Assert.That(files, Has.Count.EqualTo(2));
        Assert.That(files.Select(f => f.FolderId), Is.All.EqualTo(_arkiv));
        Assert.That(files.Select(f => f.DocumentDateState), Is.All.EqualTo(DocumentDateState.None));
    }

    [Test]
    public async Task Create_WithFolderAndDate_StoresBothAndApprovesTheDate()
    {
        var result = await _service.Create([Upload("Kontrakt.pdf", _drift, new DateTime(2026, 10, 2, 9, 30, 0))]);

        Assert.That(result.Success, Is.True, result.Message);
        var file = await BackendConfigurationPnDbContext!.Files.AsNoTracking().SingleAsync(f => f.FileName == "Kontrakt");
        Assert.That(file.FolderId, Is.EqualTo(_drift));
        Assert.That(file.DocumentDate, Is.EqualTo(new DateTime(2026, 10, 2)));
        Assert.That(file.DocumentDateState, Is.EqualTo(DocumentDateState.Approved));
        Assert.That(file.CreatedByUserId, Is.EqualTo(5));
    }

    [Test]
    public async Task Create_NegativeFolderId_IsRefused()
    {
        var result = await _service.Create([Upload("Negativ.pdf", -1)]);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo("ArchiveFolderNotFound"));
        Assert.That(await BackendConfigurationPnDbContext!.Files.AnyAsync(f => f.FileName == "Negativ"), Is.False,
            "only a missing FolderId or 0 means the default folder");
        await _storage.DidNotReceive().PutAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    [Test]
    public async Task Create_IntoAFolderTheCallerCannotRead_IsRefusedAndStoresNothing()
    {
        var result = await _service.Create([Upload("Hemmelig.pdf", _fakturaer)]);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo("ArchiveFolderNotFound"));
        Assert.That(await BackendConfigurationPnDbContext!.Files.AnyAsync(f => f.FileName == "Hemmelig"), Is.False);
        await _storage.DidNotReceive().PutAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    [Test]
    public async Task Create_Leder_IntoARestrictedFolder_StoresIt()
    {
        // A Leder may file into a folder none of their groups can read.
        _access.Context = ArchiveTestData.Leder;

        var result = await _service.Create([Upload("Ledermøde.pdf", _fakturaer)]);

        Assert.That(result.Success, Is.True, result.Message);
        var file = await BackendConfigurationPnDbContext!.Files.AsNoTracking().SingleAsync(f => f.FileName == "Ledermøde");
        Assert.That(file.FolderId, Is.EqualTo(_fakturaer));
    }

    [Test]
    public async Task Index_Leder_ListsEveryDocument()
    {
        _access.Context = ArchiveTestData.Leder;

        Assert.That((await List()).Select(f => f.Id), Is.EquivalentTo(new[] { _open, _restricted, _suggested }));
    }
}
