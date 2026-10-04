#nullable enable
using System.Text;
using BackendConfiguration.Pn.Services.FileArchive;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace BackendConfiguration.Pn.Integration.Test;

[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class FileArchiverTests : TestBaseSetup
{
    private IArchiveStorage _storage = null!;
    private FileArchiver _archiver = null!;

    [SetUp]
    public async Task SetUpArchiver()
    {
        BackendConfigurationPnDbContext!.FilesTags.RemoveRange(BackendConfigurationPnDbContext.FilesTags);
        BackendConfigurationPnDbContext.PropertyFiles.RemoveRange(BackendConfigurationPnDbContext.PropertyFiles);
        BackendConfigurationPnDbContext.UploadedDatas.RemoveRange(BackendConfigurationPnDbContext.UploadedDatas);
        BackendConfigurationPnDbContext.Files.RemoveRange(BackendConfigurationPnDbContext.Files);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        _storage = Substitute.For<IArchiveStorage>();
        _archiver = new FileArchiver(BackendConfigurationPnDbContext, _storage);
    }

    private static MemoryStream Pdf() => new(Encoding.ASCII.GetBytes("%PDF-1.7 test"));

    [Test]
    public async Task Archive_CreatesFilePropertiesTagsAndUploadedData()
    {
        var property = new Property { Name = "Nordvej 12", ItemPlanningTagId = 0, CreatedByUserId = 1, UpdatedByUserId = 1 };
        await property.Create(BackendConfigurationPnDbContext!);
        var tag = new FileTag { Name = "Ventilation", CreatedByUserId = 1, UpdatedByUserId = 1 };
        await tag.Create(BackendConfigurationPnDbContext!);

        var fileId = await _archiver.ArchiveAsync(Pdf(), "Servicerapport VA-02", "pdf", [property.Id], [tag.Id], 7);

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
        var fileId = await _archiver.ArchiveAsync(Pdf(), "x", "A\\..\\..\\Evil", [property.Id], [], 7);

        var uploaded = await BackendConfigurationPnDbContext!.UploadedDatas.AsNoTracking().SingleAsync(x => x.FileId == fileId);
        Assert.That(uploaded.Extension, Is.EqualTo("aevil"));
        await _storage.Received(1).PutAsync(Arg.Any<string>(), FileArchiver.ObjectName(uploaded.Checksum, "aevil"));
    }

    [Test]
    public async Task Archive_UploadFails_CreatesNoRows()
    {
        _storage.PutAsync(Arg.Any<string>(), Arg.Any<string>()).ThrowsAsync(new IOException("storage down"));

        await Assert.ThrowsAsync<IOException>(() => _archiver.ArchiveAsync(Pdf(), "x", "pdf", [], [], 1));

        Assert.That(BackendConfigurationPnDbContext!.Files.Count(f => f.WorkflowState != Constants.WorkflowStates.Removed), Is.EqualTo(0));
        Assert.That(BackendConfigurationPnDbContext.UploadedDatas.Count(), Is.EqualTo(0));
    }

    [Test]
    public async Task Archive_RunsCallerCallbackInsideTheTransaction()
    {
        int? seenFileId = null;
        var inTransaction = false;
        var fileVisible = false;

        var fileId = await _archiver.ArchiveAsync(Pdf(), "Callback ok", "pdf", [], [], 1, async id =>
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

        await Assert.ThrowsAsync<InvalidOperationException>(() => _archiver.ArchiveAsync(Pdf(), "Callback fails", "pdf",
            [property.Id], [tag.Id], 1, _ => throw new InvalidOperationException("caller change failed")));

        BackendConfigurationPnDbContext!.ChangeTracker.Clear();
        Assert.That(BackendConfigurationPnDbContext.Files.Count(), Is.EqualTo(0));
        Assert.That(BackendConfigurationPnDbContext.PropertyFiles.Count(), Is.EqualTo(0));
        Assert.That(BackendConfigurationPnDbContext.FilesTags.Count(), Is.EqualTo(0));
        Assert.That(BackendConfigurationPnDbContext.UploadedDatas.Count(), Is.EqualTo(0));
    }
}
