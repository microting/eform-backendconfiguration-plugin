#nullable enable
using System.Text;
using BackendConfiguration.Pn.Services.ArchiveFolders;
using BackendConfiguration.Pn.Services.FileArchive;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace BackendConfiguration.Pn.Integration.Test;

[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class FileArchiverTests : TestBaseSetup
{
    private IArchiveStorage _storage = null!;
    private FileArchiver _archiver = null!;
    private int _folderId;

    [SetUp]
    public async Task SetUpArchiver()
    {
        await ArchiveTestData.ClearAsync(BackendConfigurationPnDbContext!);
        _folderId = await ArchiveTestData.DefaultFolderIdAsync(BackendConfigurationPnDbContext!);
        _storage = Substitute.For<IArchiveStorage>();
        _archiver = new FileArchiver(BackendConfigurationPnDbContext, _storage);
    }

    private Task<int> Archive(Stream content, string name, string extension, int[] propertyIds, int[] tagIds,
        int userId, Func<int, Task>? inTransaction = null) =>
        _archiver.ArchiveAsync(content, name, extension, propertyIds, tagIds, _folderId, null,
            DocumentDateState.None, userId, inTransaction);

    private static MemoryStream Pdf() => new(Encoding.ASCII.GetBytes("%PDF-1.7 test"));

    [Test]
    public async Task Archive_CreatesFilePropertiesTagsAndUploadedData()
    {
        var property = new Property { Name = "Nordvej 12", ItemPlanningTagId = 0, CreatedByUserId = 1, UpdatedByUserId = 1 };
        await property.Create(BackendConfigurationPnDbContext!);
        var tag = new FileTag { Name = "Ventilation", CreatedByUserId = 1, UpdatedByUserId = 1 };
        await tag.Create(BackendConfigurationPnDbContext!);

        var fileId = await Archive(Pdf(), "Servicerapport VA-02", "pdf", [property.Id], [tag.Id], 7);

        var file = await BackendConfigurationPnDbContext!.Files.AsNoTracking().SingleAsync(f => f.Id == fileId);
        Assert.That(file.FileName, Is.EqualTo("Servicerapport VA-02"));
        Assert.That(file.CreatedByUserId, Is.EqualTo(7));
        Assert.That(await BackendConfigurationPnDbContext.PropertyFiles.CountAsync(x => x.FileId == fileId && x.PropertyId == property.Id), Is.EqualTo(1));
        Assert.That(await BackendConfigurationPnDbContext.FilesTags.CountAsync(x => x.FileId == fileId && x.FileTagId == tag.Id), Is.EqualTo(1));
        var uploaded = await BackendConfigurationPnDbContext.UploadedDatas.AsNoTracking().SingleAsync(x => x.FileId == fileId);
        Assert.That(uploaded.Extension, Is.EqualTo("pdf"));
        Assert.That(uploaded.Checksum, Has.Length.EqualTo(32));
        await _storage.Received(1).PutAsync(Arg.Any<string>(), FileArchiver.ObjectName(uploaded.Checksum, "pdf"));
    }

    [Test]
    public async Task Archive_KeepsOnlyLettersAndDigitsInTheExtension()
    {
        var property = new Property { Name = "Nordvej 12", ItemPlanningTagId = 0, CreatedByUserId = 1, UpdatedByUserId = 1 };
        await property.Create(BackendConfigurationPnDbContext!);

        // On Linux a client file name "x.a\\..\\..\\evil" yields this extension from Path.GetExtension.
        var fileId = await Archive(Pdf(), "x", "A\\..\\..\\Evil", [property.Id], [], 7);

        var uploaded = await BackendConfigurationPnDbContext!.UploadedDatas.AsNoTracking().SingleAsync(x => x.FileId == fileId);
        Assert.That(uploaded.Extension, Is.EqualTo("aevil"));
        await _storage.Received(1).PutAsync(Arg.Any<string>(), FileArchiver.ObjectName(uploaded.Checksum, "aevil"));
    }

    [Test]
    public async Task Archive_UploadFails_CreatesNoRows()
    {
        _storage.PutAsync(Arg.Any<string>(), Arg.Any<string>()).ThrowsAsync(new IOException("storage down"));

        await Assert.ThrowsAsync<IOException>(() => Archive(Pdf(), "x", "pdf", [], [], 1));

        Assert.That(BackendConfigurationPnDbContext!.Files.Count(f => f.WorkflowState != Constants.WorkflowStates.Removed), Is.EqualTo(0));
        Assert.That(BackendConfigurationPnDbContext.UploadedDatas.Count(), Is.EqualTo(0));
    }

    [Test]
    public async Task Archive_RunsCallerCallbackInsideTheTransaction()
    {
        int? seenFileId = null;
        var inTransaction = false;
        var fileVisible = false;

        var fileId = await Archive(Pdf(), "Callback ok", "pdf", [], [], 1, async id =>
        {
            seenFileId = id;
            inTransaction = BackendConfigurationPnDbContext!.Database.CurrentTransaction != null;
            fileVisible = await BackendConfigurationPnDbContext.Files.AnyAsync(f => f.Id == id);
        });

        Assert.That(seenFileId, Is.EqualTo(fileId));
        Assert.That(inTransaction, Is.True);
        Assert.That(fileVisible, Is.True);
    }

    [Test]
    public async Task Archive_CallbackFails_RollsBackAllRows()
    {
        var property = new Property { Name = "Sydvej 3", ItemPlanningTagId = 0, CreatedByUserId = 1, UpdatedByUserId = 1 };
        await property.Create(BackendConfigurationPnDbContext!);
        var tag = new FileTag { Name = "Brand", CreatedByUserId = 1, UpdatedByUserId = 1 };
        await tag.Create(BackendConfigurationPnDbContext);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Archive(Pdf(), "Callback fails", "pdf",
            [property.Id], [tag.Id], 1,
            _ => throw new InvalidOperationException("caller change failed")));

        BackendConfigurationPnDbContext!.ChangeTracker.Clear();
        Assert.That(BackendConfigurationPnDbContext.Files.Count(), Is.EqualTo(0));
        Assert.That(BackendConfigurationPnDbContext.PropertyFiles.Count(), Is.EqualTo(0));
        Assert.That(BackendConfigurationPnDbContext.FilesTags.Count(), Is.EqualTo(0));
        Assert.That(BackendConfigurationPnDbContext.UploadedDatas.Count(), Is.EqualTo(0));
    }

    [Test]
    public async Task Archive_StoresFolderDocumentDateOnlyAndState()
    {
        var drift = await ArchiveTestData.FolderAsync(BackendConfigurationPnDbContext!, "Drift");

        var fileId = await _archiver.ArchiveAsync(Pdf(), "Faktura 1001", "pdf", [], [], drift.Id,
            new DateTime(2026, 9, 30, 13, 45, 0), DocumentDateState.Approved, 7);

        var file = await BackendConfigurationPnDbContext!.Files.AsNoTracking().SingleAsync(f => f.Id == fileId);
        Assert.That(file.FolderId, Is.EqualTo(drift.Id));
        Assert.That(file.DocumentDate, Is.EqualTo(new DateTime(2026, 9, 30)));
        Assert.That(file.DocumentDateState, Is.EqualTo(DocumentDateState.Approved));
        Assert.That(file.OwnAccess, Is.False);
        var version = await BackendConfigurationPnDbContext.FileVersions.AsNoTracking().SingleAsync(v => v.FileId == fileId);
        Assert.That(version.FolderId, Is.EqualTo(drift.Id));
    }

    [Test]
    public async Task Archive_StateWithoutDate_IsStoredAsNone()
    {
        var fileId = await _archiver.ArchiveAsync(Pdf(), "Uden dato", "pdf", [], [], _folderId,
            null, DocumentDateState.Suggested, 7);

        var file = await BackendConfigurationPnDbContext!.Files.AsNoTracking().SingleAsync(f => f.Id == fileId);
        Assert.That(file.DocumentDateState, Is.EqualTo(DocumentDateState.None));
        Assert.That(file.DocumentDate, Is.Null);
    }

    [Test]
    public async Task DefaultFolder_IsCreatedOnceAsArkiv()
    {
        var first = await ArchiveTestData.DefaultFolderIdAsync(BackendConfigurationPnDbContext!);
        var second = await ArchiveTestData.DefaultFolderIdAsync(BackendConfigurationPnDbContext!);

        Assert.That(second, Is.EqualTo(first));
        Assert.That(await ArchiveDefaultFolder.FindIdAsync(BackendConfigurationPnDbContext!), Is.EqualTo(first));
        var defaultFolder = await BackendConfigurationPnDbContext!.ArchiveFolders.AsNoTracking()
            .SingleAsync(f => f.IsDefault);
        Assert.That(defaultFolder.Name, Is.EqualTo("Arkiv"));
        Assert.That(defaultFolder.ParentId, Is.Null);
    }
}
