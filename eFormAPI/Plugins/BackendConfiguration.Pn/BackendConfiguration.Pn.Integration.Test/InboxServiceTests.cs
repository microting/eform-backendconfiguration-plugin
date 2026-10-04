#nullable enable
using System.Security.Cryptography;
using System.Text;
using BackendConfiguration.Pn.Infrastructure.Models.Inbox;
using BackendConfiguration.Pn.Services.FileArchive;
using BackendConfiguration.Pn.Services.InboundMail;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace BackendConfiguration.Pn.Integration.Test;

[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class InboxServiceTests : TestBaseSetup
{
    private IArchiveStorage _storage = null!;
    private InboxService _service = null!;

    [SetUp]
    public async Task SetUpService()
    {
        await InboxTestData.ClearAsync(BackendConfigurationPnDbContext!);
        _storage = Substitute.For<IArchiveStorage>();
        _storage.GetAsync(Arg.Any<string>())
            .Returns(_ => Task.FromResult<Stream?>(new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.7 x"))));
        _service = new InboxService(BackendConfigurationPnDbContext!, _storage,
            new FileArchiver(BackendConfigurationPnDbContext!, _storage), new BackendConfigurationLocalizationService(),
            NullLogger<InboxService>.Instance);
    }

    private static InboxSuggestion Suggestion(int docId, InboxSuggestionKind kind, int targetId, double confidence) => new()
    {
        InboxDocumentId = docId, Kind = kind, TargetId = targetId, Confidence = confidence,
        CreatedByUserId = 0, UpdatedByUserId = 0
    };

    [Test]
    public async Task List_ShowsLiveDocumentsWithSuggestionNames()
    {
        var doc = await InboxTestData.ReadyDocumentAsync(BackendConfigurationPnDbContext!);
        var p = await InboxTestData.PropertyAsync(BackendConfigurationPnDbContext!);
        await Suggestion(doc.Id, InboxSuggestionKind.Property, p.Id, 0.5).Create(BackendConfigurationPnDbContext!);

        var res = await _service.ListAsync(null, null);

        Assert.That(res.Success, Is.True);
        var item = res.Model.Single();
        Assert.That(item.Status, Is.EqualTo((int)InboxDocumentStatus.Ready));
        Assert.That(item.Suggestions.Single().TargetName, Is.EqualTo("Nordvej 12"));
    }

    [Test]
    public async Task List_Search_MatchesFileNameOrSender()
    {
        await InboxTestData.ReadyDocumentAsync(BackendConfigurationPnDbContext!);
        Assert.That((await _service.ListAsync(null, "servicerapport")).Model, Has.Count.EqualTo(1));
        Assert.That((await _service.ListAsync(null, "example.org")).Model, Has.Count.EqualTo(1));
        Assert.That((await _service.ListAsync(null, "faktura")).Model, Is.Empty);
    }

    [Test]
    public async Task File_CreatesArchiveFile_MarksFiledAndAccepted()
    {
        var doc = await InboxTestData.ReadyDocumentAsync(BackendConfigurationPnDbContext!);
        var p = await InboxTestData.PropertyAsync(BackendConfigurationPnDbContext!);
        var t = await InboxTestData.TagAsync(BackendConfigurationPnDbContext!);
        var other = await InboxTestData.TagAsync(BackendConfigurationPnDbContext!, "Brand");
        await Suggestion(doc.Id, InboxSuggestionKind.Tag, t.Id, 0.5).Create(BackendConfigurationPnDbContext!);
        await Suggestion(doc.Id, InboxSuggestionKind.Tag, other.Id, 0.3).Create(BackendConfigurationPnDbContext!);

        var res = await _service.FileAsync(doc.Id,
            new FileInboxDocumentRequest { Name = "Servicerapport VA-02", PropertyIds = [p.Id], TagIds = [t.Id] }, 7);

        Assert.That(res.Success, Is.True);
        await BackendConfigurationPnDbContext!.Entry(doc).ReloadAsync();
        Assert.That(doc.Status, Is.EqualTo(InboxDocumentStatus.Filed));
        Assert.That(doc.FiledByUserId, Is.EqualTo(7));
        var file = await BackendConfigurationPnDbContext.Files.AsNoTracking().SingleAsync(f => f.Id == doc.FiledFileId);
        Assert.That(file.FileName, Is.EqualTo("Servicerapport VA-02"));
        var accepted = await BackendConfigurationPnDbContext.InboxSuggestions.AsNoTracking()
            .ToDictionaryAsync(s => s.TargetId, s => s.Accepted);
        Assert.That(accepted[t.Id], Is.True);
        Assert.That(accepted[other.Id], Is.False);
    }

    [Test]
    public async Task File_WithLocalStorage_ArchivesUnderTheSameObjectName()
    {
        // Installs without S3: the inbox PDF and the archive file share one content-derived object name.
        var root = Path.Combine(Path.GetTempPath(), "inbox-file-test-" + Guid.NewGuid().ToString("N"));
        var staged = Path.Combine(Path.GetTempPath(), $"inbox-file-test-{Guid.NewGuid():N}.pdf");
        try
        {
            var storage = new LocalArchiveStorage(root);
            var bytes = Encoding.ASCII.GetBytes("%PDF-1.7 local storage");
            var md5 = Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant();
            await System.IO.File.WriteAllBytesAsync(staged, bytes);
            await storage.PutAsync(staged, FileArchiver.ObjectName(md5, "pdf"));
            var doc = await InboxTestData.ReadyDocumentAsync(BackendConfigurationPnDbContext!, md5);
            var p = await InboxTestData.PropertyAsync(BackendConfigurationPnDbContext!);
            var service = new InboxService(BackendConfigurationPnDbContext!, storage,
                new FileArchiver(BackendConfigurationPnDbContext!, storage), new BackendConfigurationLocalizationService(),
                NullLogger<InboxService>.Instance);

            var res = await service.FileAsync(doc.Id,
                new FileInboxDocumentRequest { Name = "Lokal", PropertyIds = [p.Id], TagIds = [] }, 7);

            Assert.That(res.Success, Is.True, res.Message);
            await using var archived = await storage.GetAsync(FileArchiver.ObjectName(md5, "pdf"));
            Assert.That(archived, Is.Not.Null);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
            if (System.IO.File.Exists(staged)) System.IO.File.Delete(staged);
        }
    }

    [Test]
    public async Task File_Twice_SecondIsRejectedAndLeavesOneFile()
    {
        var doc = await InboxTestData.ReadyDocumentAsync(BackendConfigurationPnDbContext!);
        var p = await InboxTestData.PropertyAsync(BackendConfigurationPnDbContext!);
        var req = new FileInboxDocumentRequest { Name = "x", PropertyIds = [p.Id], TagIds = [] };
        Assert.That((await _service.FileAsync(doc.Id, req, 7)).Success, Is.True);

        Assert.That((await _service.FileAsync(doc.Id, req, 7)).Success, Is.False);

        Assert.That(await BackendConfigurationPnDbContext!.Files.CountAsync(), Is.EqualTo(1));
    }

    /// <summary>Flips the document to Filed just before the real archiver runs, as a concurrent filer would.</summary>
    private sealed class RacingArchiver(IFileArchiver inner, BackendConfiguration.Pn.Services.FileArchive.IArchiveStorage _,
        Microting.EformBackendConfigurationBase.Infrastructure.Data.BackendConfigurationPnDbContext db, int docId) : IFileArchiver
    {
        public async Task<int> ArchiveAsync(Stream content, string name, string extension,
            IReadOnlyCollection<int> propertyIds, IReadOnlyCollection<int> tagIds, int userId,
            Func<int, Task>? inTransaction = null)
        {
            await db.InboxDocuments.Where(d => d.Id == docId)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, InboxDocumentStatus.Filed));
            return await inner.ArchiveAsync(content, name, extension, propertyIds, tagIds, userId, inTransaction);
        }
    }

    [Test]
    public async Task File_ConcurrentlyFiled_ClaimFailsAndNoFileRowRemains()
    {
        var doc = await InboxTestData.ReadyDocumentAsync(BackendConfigurationPnDbContext!);
        var p = await InboxTestData.PropertyAsync(BackendConfigurationPnDbContext!);
        var racing = new RacingArchiver(new FileArchiver(BackendConfigurationPnDbContext!, _storage), _storage,
            BackendConfigurationPnDbContext!, doc.Id);
        var service = new InboxService(BackendConfigurationPnDbContext!, _storage, racing,
            new BackendConfigurationLocalizationService(), NullLogger<InboxService>.Instance);

        var res = await service.FileAsync(doc.Id, new FileInboxDocumentRequest { Name = "x", PropertyIds = [p.Id], TagIds = [] }, 7);

        Assert.That(res.Success, Is.False);
        Assert.That(await BackendConfigurationPnDbContext!.Files.CountAsync(), Is.EqualTo(0));
    }

    [Test]
    public async Task File_NoProperty_IsRejected()
    {
        var doc = await InboxTestData.ReadyDocumentAsync(BackendConfigurationPnDbContext!);

        var res = await _service.FileAsync(doc.Id, new FileInboxDocumentRequest { Name = "x", PropertyIds = [], TagIds = [] }, 7);

        Assert.That(res.Success, Is.False);
        await BackendConfigurationPnDbContext!.Entry(doc).ReloadAsync();
        Assert.That(doc.Status, Is.EqualTo(InboxDocumentStatus.Ready));
    }

    [Test]
    public async Task File_UnknownPropertyId_IsRejected()
    {
        var doc = await InboxTestData.ReadyDocumentAsync(BackendConfigurationPnDbContext!);
        var res = await _service.FileAsync(doc.Id, new FileInboxDocumentRequest { Name = "x", PropertyIds = [999999], TagIds = [] }, 7);
        Assert.That(res.Success, Is.False);
        Assert.That(await BackendConfigurationPnDbContext!.Files.CountAsync(), Is.EqualTo(0));
    }

    [Test]
    public async Task File_NotReady_IsRejected()
    {
        var doc = await InboxTestData.ReadyDocumentAsync(BackendConfigurationPnDbContext!);
        doc.Status = InboxDocumentStatus.SenderPending;
        await doc.Update(BackendConfigurationPnDbContext!);
        var p = await InboxTestData.PropertyAsync(BackendConfigurationPnDbContext!);

        var res = await _service.FileAsync(doc.Id, new FileInboxDocumentRequest { Name = "x", PropertyIds = [p.Id], TagIds = [] }, 7);

        Assert.That(res.Success, Is.False);
    }

    [Test]
    public async Task Undo_WithinTenMinutes_RemovesFileAndReopens()
    {
        var doc = await InboxTestData.ReadyDocumentAsync(BackendConfigurationPnDbContext!);
        var p = await InboxTestData.PropertyAsync(BackendConfigurationPnDbContext!);
        var t = await InboxTestData.TagAsync(BackendConfigurationPnDbContext!);
        await Suggestion(doc.Id, InboxSuggestionKind.Tag, t.Id, 0.5).Create(BackendConfigurationPnDbContext!);
        await _service.FileAsync(doc.Id, new FileInboxDocumentRequest { Name = "x", PropertyIds = [p.Id], TagIds = [t.Id] }, 7);

        var res = await _service.UndoAsync(doc.Id, 7);

        Assert.That(res.Success, Is.True);
        await BackendConfigurationPnDbContext!.Entry(doc).ReloadAsync();
        Assert.That(doc.Status, Is.EqualTo(InboxDocumentStatus.Ready));
        Assert.That(doc.FiledFileId, Is.Null);
        Assert.That(doc.FiledAt, Is.Null);
        Assert.That(await BackendConfigurationPnDbContext.Files
            .CountAsync(f => f.WorkflowState != Constants.WorkflowStates.Removed), Is.EqualTo(0));
        Assert.That(await BackendConfigurationPnDbContext.PropertyFiles
            .CountAsync(f => f.WorkflowState != Constants.WorkflowStates.Removed), Is.EqualTo(0));
        Assert.That(await BackendConfigurationPnDbContext.FilesTags
            .CountAsync(f => f.WorkflowState != Constants.WorkflowStates.Removed), Is.EqualTo(0));
        Assert.That(await BackendConfigurationPnDbContext.InboxSuggestions.AsNoTracking()
            .Select(s => s.Accepted).SingleAsync(), Is.Null);
    }

    [Test]
    public async Task Undo_AfterTenMinutes_IsRejected()
    {
        var doc = await InboxTestData.ReadyDocumentAsync(BackendConfigurationPnDbContext!);
        var p = await InboxTestData.PropertyAsync(BackendConfigurationPnDbContext!);
        await _service.FileAsync(doc.Id, new FileInboxDocumentRequest { Name = "x", PropertyIds = [p.Id], TagIds = [] }, 7);
        await BackendConfigurationPnDbContext!.Entry(doc).ReloadAsync();
        doc.FiledAt = DateTime.UtcNow.AddMinutes(-11);
        await doc.Update(BackendConfigurationPnDbContext);

        Assert.That((await _service.UndoAsync(doc.Id, 7)).Success, Is.False);
    }

    [Test]
    public async Task Reject_SetsRejected()
    {
        var doc = await InboxTestData.ReadyDocumentAsync(BackendConfigurationPnDbContext!);
        Assert.That((await _service.RejectAsync(doc.Id, 7)).Success, Is.True);
        await BackendConfigurationPnDbContext!.Entry(doc).ReloadAsync();
        Assert.That(doc.Status, Is.EqualTo(InboxDocumentStatus.Rejected));
        Assert.That(await BackendConfigurationPnDbContext.InboxDocumentVersions.AsNoTracking()
            .AnyAsync(v => v.InboxDocumentId == doc.Id && v.Status == InboxDocumentStatus.Rejected), Is.True);
    }

    [Test]
    public async Task Reject_Preparing_SetsRejected()
    {
        // A document stuck in Preparing (the hub never delivers) must still be rejectable.
        var doc = await InboxTestData.ReadyDocumentAsync(BackendConfigurationPnDbContext!);
        doc.Status = InboxDocumentStatus.Preparing;
        await doc.Update(BackendConfigurationPnDbContext!);

        Assert.That((await _service.RejectAsync(doc.Id, 7)).Success, Is.True);
        await BackendConfigurationPnDbContext!.Entry(doc).ReloadAsync();
        Assert.That(doc.Status, Is.EqualTo(InboxDocumentStatus.Rejected));
    }
}
