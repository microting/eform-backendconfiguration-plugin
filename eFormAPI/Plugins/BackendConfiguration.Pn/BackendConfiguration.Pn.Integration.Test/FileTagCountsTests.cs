#nullable enable
using BackendConfiguration.Pn.Services.ArchiveAccess;
using BackendConfiguration.Pn.Services.BackendConfigurationFileTagsService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>S2: a tag's count says how many documents the caller can read with it, never how many exist.</summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class FileTagCountsTests : TestBaseSetup
{
    private const int G1 = 701;
    private FixedArchiveAccess _access = null!;
    private BackendConfigurationTagsService _service = null!;
    private int _tagId;

    [SetUp]
    public async Task Seed()
    {
        var db = BackendConfigurationPnDbContext!;
        await ArchiveTestData.ClearAsync(db);
        var arkiv = await ArchiveTestData.DefaultFolderIdAsync(db);
        var ledelse = (await ArchiveTestData.FolderAsync(db, "Ledelse", null, G1)).Id;
        var tag = await InboxTestData.TagAsync(db, "Kontrakt");
        _tagId = tag.Id;
        var open = await ArchiveTestData.FileAsync(db, arkiv, "Åben");
        var restricted = await ArchiveTestData.FileAsync(db, ledelse, "Lukket");
        var removed = await ArchiveTestData.FileAsync(db, arkiv, "Slettet");
        foreach (var file in new[] { open, restricted, removed })
        {
            await new FileTags { FileId = file.Id, FileTagId = tag.Id, CreatedByUserId = 1, UpdatedByUserId = 1 }.Create(db);
        }

        await removed.Delete(db);

        // A live, readable document whose tag link was removed: the link must not count.
        var unlinked = await ArchiveTestData.FileAsync(db, arkiv, "Afmærket");
        var removedLink = new FileTags { FileId = unlinked.Id, FileTagId = tag.Id, CreatedByUserId = 1, UpdatedByUserId = 1 };
        await removedLink.Create(db);
        await removedLink.Delete(db);
        _access = new FixedArchiveAccess(ArchiveTestData.User(), db);
        _service = new BackendConfigurationTagsService(new BackendConfigurationLocalizationService(),
            NullLogger<BackendConfigurationTagsService>.Instance, db, LederTestData.Caller(5), _access);
    }

    private async Task<int> CountInList() =>
        (await _service.GetTags()).Model.Single(t => t.Id == _tagId).TagCount;

    private async Task<int> CountById() => (await _service.GetById(_tagId)).Model.TagCount;

    [Test]
    public async Task NoGroups_CountsOnlyTheOpenLiveDocument()
    {
        Assert.That(await CountInList(), Is.EqualTo(1));
        Assert.That(await CountById(), Is.EqualTo(1));
    }

    [Test]
    public async Task GroupMember_CountsTheRestrictedDocumentToo()
    {
        _access.Context = ArchiveTestData.User(G1);
        Assert.That(await CountInList(), Is.EqualTo(2));
        Assert.That(await CountById(), Is.EqualTo(2));
    }

    [Test]
    public async Task Leder_CountsEveryLiveDocument_NeverARemovedOne()
    {
        _access.Context = ArchiveTestData.Leder;
        Assert.That(await CountInList(), Is.EqualTo(2));
        Assert.That(await CountById(), Is.EqualTo(2));
    }

    [Test]
    public async Task RemovedTagLink_OnAReadableDocument_IsNotCounted()
    {
        _access.Context = ArchiveTestData.Leder;
        var tag = (await _service.GetById(_tagId)).Model;
        Assert.That(tag.TagCount, Is.EqualTo(2), "open + restricted; the unlinked document adds nothing");
    }

    [Test]
    public async Task TagNames_StayListed_EvenWithZeroReadableDocuments()
    {
        // S2: the tag-name list is unchanged; names are low-sensitivity.
        _access.Context = ArchiveTestData.User(9999);
        var tags = (await _service.GetTags()).Model;
        Assert.That(tags.Select(t => t.Id), Does.Contain(_tagId));
    }

    [Test]
    public async Task Disabled_IsTheD7Refusal_NotAGenericFailure()
    {
        _access.Context = ArchiveTestData.Disabled;
        await Assert.ThrowsAsync<ArchiveAccountDisabledException>(() => _service.GetTags());
        await Assert.ThrowsAsync<ArchiveAccountDisabledException>(() => _service.GetById(_tagId));
    }
}
