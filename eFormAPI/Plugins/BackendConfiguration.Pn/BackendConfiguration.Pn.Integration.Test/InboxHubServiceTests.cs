using System.Text;
using BackendConfiguration.Pn.Infrastructure.Models.Inbox;
using BackendConfiguration.Pn.Services.BackendConfigurationFileTagsService;
using BackendConfiguration.Pn.Services.FileArchive;
using BackendConfiguration.Pn.Services.InboundMail;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;
using Microting.eFormApi.BasePn.Infrastructure.Models.Common;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using NSubstitute;

namespace BackendConfiguration.Pn.Integration.Test;

[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class InboxHubServiceTests : TestBaseSetup
{
    private IArchiveStorage _storage = null!;
    private InboxHubService _service = null!;

    /// <summary>The removed unknown-sender policy's setting; existing rows stay in tenants but are not read.</summary>
    private const string LegacyPolicyName = "BackendConfigurationSettings:InboxUnknownSenderPolicy";

    [SetUp]
    public async Task SetUpService()
    {
        await InboxTestData.ClearAsync(BackendConfigurationPnDbContext!);
        await BackendConfigurationPnDbContext!.PluginConfigurationValues
            .Where(x => x.Name == LegacyPolicyName).ExecuteDeleteAsync();
        _storage = Substitute.For<IArchiveStorage>();
        _service = new InboxHubService(BackendConfigurationPnDbContext!, _storage,
            new SenderVerdictResolver(BackendConfigurationPnDbContext!), NullLogger<InboxHubService>.Instance);
    }

    private static ArrivedRequest Arrived(string from = "post@example.net", string? id = null) => new(
        id ?? Guid.NewGuid().ToString(), from, "Dokumenter", DateTime.UtcNow, "scan_0012.pdf", 1000,
        "not-checked", "not-checked", null);

    private static MemoryStream Pdf() => new(Encoding.ASCII.GetBytes("%PDF-1.7 x"));

    private Task RuleAsync(string pattern, InboxSenderRuleKind kind) =>
        new InboxSenderRule { Pattern = pattern, Kind = kind, CreatedByUserId = 1, UpdatedByUserId = 1 }
            .Create(BackendConfigurationPnDbContext!);

    // ---- Verdict: block list only ----

    [Test]
    public async Task Verdict_UnknownSender_IsAllowed_CreatesPreparing()
    {
        var res = await _service.ArrivedAsync(Arrived());

        Assert.That(res.SenderVerdict, Is.EqualTo(SenderVerdict.Allowed));
        var doc = await BackendConfigurationPnDbContext!.InboxDocuments.AsNoTracking().SingleAsync();
        Assert.That(doc.Status, Is.EqualTo(InboxDocumentStatus.Preparing));
    }

    [Test]
    public async Task Verdict_LegacyRefusePolicyRow_IsIgnored()
    {
        BackendConfigurationPnDbContext!.PluginConfigurationValues.Add(
            new PluginConfigurationValue { Name = LegacyPolicyName, Value = "refuse" });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var res = await _service.ArrivedAsync(Arrived());

        Assert.That(res.SenderVerdict, Is.EqualTo(SenderVerdict.Allowed));
        Assert.That(await BackendConfigurationPnDbContext!.InboxDocuments.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task Verdict_BlockRule_ExactAddress_CaseInsensitive_BlockedNoRow()
    {
        await RuleAsync("spam@example.net", InboxSenderRuleKind.Block);

        var blocked = await _service.ArrivedAsync(Arrived("Spam@Example.NET"));
        var other = await _service.ArrivedAsync(Arrived("post@example.net"));

        Assert.That(blocked.SenderVerdict, Is.EqualTo(SenderVerdict.Blocked));
        Assert.That(other.SenderVerdict, Is.EqualTo(SenderVerdict.Allowed));
        var rows = await BackendConfigurationPnDbContext!.InboxDocuments.AsNoTracking().ToListAsync();
        Assert.That(rows.Select(d => d.FromAddress), Is.EquivalentTo(new[] { "post@example.net" }));
    }

    [Test]
    public async Task Verdict_BlockRule_Domain_BlocksEveryAddressInIt()
    {
        await RuleAsync("@example.net", InboxSenderRuleKind.Block);

        Assert.That((await _service.ArrivedAsync(Arrived("post@example.net"))).SenderVerdict, Is.EqualTo(SenderVerdict.Blocked));
        Assert.That((await _service.ArrivedAsync(Arrived("faktura@example.net"))).SenderVerdict, Is.EqualTo(SenderVerdict.Blocked));
        Assert.That((await _service.ArrivedAsync(Arrived("post@example.org"))).SenderVerdict, Is.EqualTo(SenderVerdict.Allowed));
    }

    [Test]
    public async Task Verdict_RemovedBlockRule_NoLongerBlocks()
    {
        var rule = new InboxSenderRule
        {
            Pattern = "spam@example.net", Kind = InboxSenderRuleKind.Block, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await rule.Create(BackendConfigurationPnDbContext!);
        await rule.Delete(BackendConfigurationPnDbContext!);

        Assert.That((await _service.ArrivedAsync(Arrived("spam@example.net"))).SenderVerdict, Is.EqualTo(SenderVerdict.Allowed));
    }

    [Test]
    public async Task Verdict_LegacyAllowRules_HaveNoEffect()
    {
        // An Allow row neither overrides a Block rule nor changes anything for its own sender.
        await RuleAsync("spam@example.net", InboxSenderRuleKind.Allow);
        await RuleAsync("@example.net", InboxSenderRuleKind.Block);
        await RuleAsync("@example.org", InboxSenderRuleKind.Allow);

        Assert.That((await _service.ArrivedAsync(Arrived("spam@example.net"))).SenderVerdict, Is.EqualTo(SenderVerdict.Blocked));
        Assert.That((await _service.ArrivedAsync(Arrived("post@example.org"))).SenderVerdict, Is.EqualTo(SenderVerdict.Allowed));
        var doc = await BackendConfigurationPnDbContext!.InboxDocuments.AsNoTracking().SingleAsync();
        Assert.That(doc.Status, Is.EqualTo(InboxDocumentStatus.Preparing));
    }

    // ---- Arrived replays ----

    [Test]
    public async Task Arrived_Twice_SameRow()
    {
        var req = Arrived();
        await _service.ArrivedAsync(req);
        var second = await _service.ArrivedAsync(req);

        Assert.That(second.SenderVerdict, Is.EqualTo(SenderVerdict.Allowed));
        Assert.That(await BackendConfigurationPnDbContext!.InboxDocuments.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task Arrived_Replay_OfDocumentThatFailedBeforeDelivery_AnswersTheCurrentBlockList()
    {
        var req = Arrived();
        await _service.ArrivedAsync(req);
        await _service.FailedAsync(new FailedRequest(req.HubDocumentId, "PDF-filen kunne ikke læses."));
        Assert.That((await _service.ArrivedAsync(req)).SenderVerdict, Is.EqualTo(SenderVerdict.Allowed));

        await RuleAsync("post@example.net", InboxSenderRuleKind.Block);

        Assert.That((await _service.ArrivedAsync(req)).SenderVerdict, Is.EqualTo(SenderVerdict.Blocked));
        Assert.That(await BackendConfigurationPnDbContext!.InboxDocuments.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task Arrived_Replay_OfRejectedDocument_IsAllowedUnlessBlocked()
    {
        var doc = await PreparingAsync();
        doc.Status = InboxDocumentStatus.Rejected;
        await doc.Update(BackendConfigurationPnDbContext!);

        var replay = await _service.ArrivedAsync(Arrived("post@example.net", doc.HubDocumentId));

        Assert.That(replay.SenderVerdict, Is.EqualTo(SenderVerdict.Allowed));
        Assert.That(await BackendConfigurationPnDbContext!.InboxDocuments.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task Arrived_Replay_OfLegacySenderPendingRow_IsAllowed_RowUnchanged()
    {
        var doc = await LegacySenderPendingAsync();

        var replay = await _service.ArrivedAsync(Arrived("post@example.net", doc.HubDocumentId));

        Assert.That(replay.SenderVerdict, Is.EqualTo(SenderVerdict.Allowed));
        await BackendConfigurationPnDbContext!.Entry(doc).ReloadAsync();
        Assert.That(doc.Status, Is.EqualTo(InboxDocumentStatus.SenderPending));
        Assert.That(await BackendConfigurationPnDbContext.InboxDocuments.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task Catalog_ListsLivePropertiesAndTags()
    {
        var p = await InboxTestData.PropertyAsync(BackendConfigurationPnDbContext!);
        var removed = await InboxTestData.PropertyAsync(BackendConfigurationPnDbContext!, "Gammel", "Gammelvej 1");
        await removed.Delete(BackendConfigurationPnDbContext!);
        var t = await InboxTestData.TagAsync(BackendConfigurationPnDbContext!);
        var removedTag = await InboxTestData.TagAsync(BackendConfigurationPnDbContext!, "Udgået");
        await removedTag.Delete(BackendConfigurationPnDbContext!);

        var catalog = await _service.CatalogAsync();

        // Other tests leave properties behind, so assert membership rather than an exact set.
        var ids = catalog.Properties.Select(x => x.Id).ToList();
        Assert.That(ids, Does.Contain(p.Id));
        Assert.That(ids, Does.Not.Contain(removed.Id));
        Assert.That(catalog.Properties.Single(x => x.Id == p.Id).Address, Is.EqualTo("Nordvej 12, 8000 Aarhus C"));
        Assert.That(catalog.Tags.Select(x => x.Id), Is.EquivalentTo(new[] { t.Id }));
        Assert.That(catalog.CanCreateTags, Is.True);
    }

    private async Task<InboxDocument> PreparingAsync()
    {
        var req = Arrived("post@example.net");
        await _service.ArrivedAsync(req);
        return await BackendConfigurationPnDbContext!.InboxDocuments.SingleAsync(d => d.HubDocumentId == req.HubDocumentId);
    }

    /// <summary>A row held under the removed unknown-sender policy: nothing produces SenderPending any more.</summary>
    private async Task<InboxDocument> LegacySenderPendingAsync()
    {
        var doc = await PreparingAsync();
        doc.Status = InboxDocumentStatus.SenderPending;
        await doc.Update(BackendConfigurationPnDbContext!);
        return doc;
    }

    [Test]
    public async Task Deliver_StoresPdfAndSuggestions_Ready()
    {
        var doc = await PreparingAsync();
        var p = await InboxTestData.PropertyAsync(BackendConfigurationPnDbContext!);
        var t = await InboxTestData.TagAsync(BackendConfigurationPnDbContext!);
        var meta = new DeliverMetadata(doc.HubDocumentId, 2, true,
        [
            new("property", p.Id, "textMatch", 0.5, "Adresse: Nordvej 12", 1, "Adressen står i dokumentet."),
            new("Tag", t.Id, "AI", 0.5, "ventilation", 1, "“Ventilation” står i dokumentet.")
        ]);

        await _service.DeliverAsync(meta, Pdf(), "scan_0012.pdf");

        await BackendConfigurationPnDbContext!.Entry(doc).ReloadAsync();
        Assert.That(doc.Status, Is.EqualTo(InboxDocumentStatus.Ready));
        Assert.That(doc.ReviewedByMicroting, Is.True);
        Assert.That(doc.PageCount, Is.EqualTo(2));
        Assert.That(doc.DeliveredAt, Is.Not.Null);
        Assert.That(doc.Md5, Has.Length.EqualTo(32));
        var suggestions = await BackendConfigurationPnDbContext.InboxSuggestions.AsNoTracking()
            .Where(s => s.InboxDocumentId == doc.Id).ToListAsync();
        Assert.That(suggestions.Select(s => (s.Kind, s.TargetId, s.Source)), Is.EquivalentTo(new[]
        {
            (InboxSuggestionKind.Property, p.Id, InboxSuggestionSource.TextMatch),
            (InboxSuggestionKind.Tag, t.Id, InboxSuggestionSource.Ai)
        }));
        await _storage.Received(1).PutAsync(Arg.Any<string>(), $"{doc.Md5}.pdf");
    }

    [Test]
    public async Task Deliver_Twice_IsIdempotent()
    {
        var doc = await PreparingAsync();
        var p = await InboxTestData.PropertyAsync(BackendConfigurationPnDbContext!);
        var t = await InboxTestData.TagAsync(BackendConfigurationPnDbContext!);
        var meta = new DeliverMetadata(doc.HubDocumentId, 1, false,
            [new("property", p.Id, "textMatch", 0.5, null, 1, null), new("tag", t.Id, "textMatch", 0.5, null, 1, null)]);
        await _service.DeliverAsync(meta, Pdf(), "a.pdf");

        await _service.DeliverAsync(meta, Pdf(), "a.pdf");

        await _storage.Received(1).PutAsync(Arg.Any<string>(), Arg.Any<string>());
        Assert.That(await BackendConfigurationPnDbContext!.InboxSuggestions.CountAsync(s => s.InboxDocumentId == doc.Id),
            Is.EqualTo(2));
    }

    [Test]
    public async Task Deliver_LegacySenderPendingRow_StoresAndBecomesReady()
    {
        var doc = await LegacySenderPendingAsync();
        var p = await InboxTestData.PropertyAsync(BackendConfigurationPnDbContext!);

        await _service.DeliverAsync(new DeliverMetadata(doc.HubDocumentId, 1, false,
            [new("property", p.Id, "textMatch", 0.5, null, 1, null)]), Pdf(), "a.pdf");

        await BackendConfigurationPnDbContext!.Entry(doc).ReloadAsync();
        Assert.That(doc.Status, Is.EqualTo(InboxDocumentStatus.Ready));
        Assert.That(doc.Md5, Has.Length.EqualTo(32));
        Assert.That(doc.DeliveredAt, Is.Not.Null);
        Assert.That(await BackendConfigurationPnDbContext.InboxSuggestions.CountAsync(s => s.InboxDocumentId == doc.Id),
            Is.EqualTo(1));
        await _storage.Received(1).PutAsync(Arg.Any<string>(), $"{doc.Md5}.pdf");
    }

    [Test]
    public async Task Deliver_DropsSuggestionsForDeletedTargets()
    {
        var doc = await PreparingAsync();
        var tag = await InboxTestData.TagAsync(BackendConfigurationPnDbContext!);
        await tag.Delete(BackendConfigurationPnDbContext!);

        await _service.DeliverAsync(new DeliverMetadata(doc.HubDocumentId, 1, false,
            [new("tag", tag.Id, "textMatch", 0.5, null, 1, null), new("property", 999999, "textMatch", 0.5, null, 1, null)]),
            Pdf(), "a.pdf");

        Assert.That(await BackendConfigurationPnDbContext!.InboxSuggestions.CountAsync(), Is.EqualTo(0));
    }

    [Test]
    public async Task Deliver_DropsSuggestionsWithUnknownKindOrSource()
    {
        var doc = await PreparingAsync();
        var p = await InboxTestData.PropertyAsync(BackendConfigurationPnDbContext!);

        await _service.DeliverAsync(new DeliverMetadata(doc.HubDocumentId, 1, false,
            [new("building", p.Id, "textMatch", 0.5, null, 1, null), new("property", p.Id, "guess", 0.5, null, 1, null)]),
            Pdf(), "a.pdf");

        Assert.That(await BackendConfigurationPnDbContext!.InboxSuggestions.CountAsync(), Is.EqualTo(0));
        await BackendConfigurationPnDbContext.Entry(doc).ReloadAsync();
        Assert.That(doc.Status, Is.EqualTo(InboxDocumentStatus.Ready));
    }

    [Test]
    public async Task Deliver_UnknownHubDocument_CreatesReadyRow()
    {
        var id = Guid.NewGuid().ToString();

        await _service.DeliverAsync(new DeliverMetadata(id, 1, false, []), Pdf(), "lost.pdf");

        // The sender is unknown, so no Block rule can match it: nothing holds the document back.
        var doc = await BackendConfigurationPnDbContext!.InboxDocuments.AsNoTracking().SingleAsync(d => d.HubDocumentId == id);
        Assert.That(doc.Status, Is.EqualTo(InboxDocumentStatus.Ready));
        Assert.That(doc.FromAddress, Is.EqualTo(InboxHubService.UnknownSender));
        Assert.That(doc.DeliveredAt, Is.Not.Null);
        Assert.That(doc.FileName, Is.EqualTo("lost.pdf"));
        Assert.That(doc.Md5, Has.Length.EqualTo(32));
    }

    [Test]
    public async Task Failed_SetsFailedWithReason()
    {
        var doc = await PreparingAsync();

        await _service.FailedAsync(new FailedRequest(doc.HubDocumentId, "PDF-filen er beskyttet med adgangskode."));

        await BackendConfigurationPnDbContext!.Entry(doc).ReloadAsync();
        Assert.That(doc.Status, Is.EqualTo(InboxDocumentStatus.Failed));
        Assert.That(doc.FailureReason, Is.EqualTo("PDF-filen er beskyttet med adgangskode."));
    }

    [Test]
    public async Task Deliver_LegacySenderPendingRowThatFailed_IsReady()
    {
        var doc = await LegacySenderPendingAsync();
        await _service.FailedAsync(new FailedRequest(doc.HubDocumentId, "Dokumentet kunne ikke behandles automatisk."));
        await BackendConfigurationPnDbContext!.Entry(doc).ReloadAsync();
        Assert.That(doc.Status, Is.EqualTo(InboxDocumentStatus.Failed));

        await _service.DeliverAsync(new DeliverMetadata(doc.HubDocumentId, 1, false, []), Pdf(), "a.pdf");

        await BackendConfigurationPnDbContext.Entry(doc).ReloadAsync();
        Assert.That(doc.Status, Is.EqualTo(InboxDocumentStatus.Ready));
        Assert.That(doc.DeliveredAt, Is.Not.Null);
    }

    [Test]
    public async Task Deliver_AfterFailed_IsReady()
    {
        var doc = await PreparingAsync();
        await _service.FailedAsync(new FailedRequest(doc.HubDocumentId, "Dokumentet kunne ikke behandles automatisk."));

        await _service.DeliverAsync(new DeliverMetadata(doc.HubDocumentId, 1, false, []), Pdf(), "a.pdf");

        await BackendConfigurationPnDbContext!.Entry(doc).ReloadAsync();
        Assert.That(doc.Status, Is.EqualTo(InboxDocumentStatus.Ready));
    }

    [Test]
    public async Task Failed_AfterDeliver_ChangesNothing()
    {
        var doc = await PreparingAsync();
        await _service.DeliverAsync(new DeliverMetadata(doc.HubDocumentId, 1, false, []), Pdf(), "a.pdf");

        await _service.FailedAsync(new FailedRequest(doc.HubDocumentId, "For sent."));

        await BackendConfigurationPnDbContext!.Entry(doc).ReloadAsync();
        Assert.That(doc.Status, Is.EqualTo(InboxDocumentStatus.Ready));
        Assert.That(doc.FailureReason, Is.Null);
    }

    // ---- Tag creation ----

    private Task<List<FileTag>> TagRowsAsync(string name) =>
        BackendConfigurationPnDbContext!.FileTags.AsNoTracking().Where(t => t.Name == name).OrderBy(t => t.Id).ToListAsync();

    [Test]
    public async Task CreateTagAsync_NewName_CreatesBySystemUser()
    {
        var res = await _service.CreateTagAsync("Skadedyr");

        var row = (await TagRowsAsync("Skadedyr")).Single();
        Assert.That(res, Is.EqualTo(new CreateTagResponse(row.Id, "Skadedyr", true)));
        Assert.That(row.CreatedByUserId, Is.EqualTo(0));
        Assert.That(row.WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Removed));
    }

    [Test]
    public async Task CreateTagAsync_SameNameTwice_SecondAnswersExisting()
    {
        var first = await _service.CreateTagAsync("Skadedyr");
        var second = await _service.CreateTagAsync("Skadedyr");

        Assert.That(first, Is.Not.Null);
        Assert.That(second, Is.EqualTo(first! with { Created = false }));
        Assert.That(await TagRowsAsync("Skadedyr"), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task CreateTagAsync_OtherCase_ReturnsExistingWithStoredName()
    {
        var existing = await InboxTestData.TagAsync(BackendConfigurationPnDbContext!, "Skadedyr");

        var res = await _service.CreateTagAsync("skadedyr");

        Assert.That(res, Is.EqualTo(new CreateTagResponse(existing.Id, "Skadedyr", false)));
        Assert.That(await BackendConfigurationPnDbContext!.FileTags.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task CreateTagAsync_OnlyRemovedTag_RestoresIt_NotCreated()
    {
        var removed = await InboxTestData.TagAsync(BackendConfigurationPnDbContext!, "Skadedyr");
        await removed.Delete(BackendConfigurationPnDbContext!);

        var res = await _service.CreateTagAsync("Skadedyr");

        Assert.That(res, Is.EqualTo(new CreateTagResponse(removed.Id, "Skadedyr", false)));
        var row = (await TagRowsAsync("Skadedyr")).Single();
        Assert.That(row.WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Removed));
        Assert.That(row.UpdatedByUserId, Is.EqualTo(0));
        Assert.That((await _service.CatalogAsync()).Tags.Select(t => t.Id), Does.Contain(removed.Id));
    }

    [Test]
    public async Task CreateTagAsync_LiveAndRemovedDuplicate_ReturnsLive_RestoresNothing()
    {
        var removed = await InboxTestData.TagAsync(BackendConfigurationPnDbContext!, "Skadedyr");
        await removed.Delete(BackendConfigurationPnDbContext!);
        var live = await InboxTestData.TagAsync(BackendConfigurationPnDbContext!, "Skadedyr");

        var res = await _service.CreateTagAsync("Skadedyr");

        Assert.That(res, Is.EqualTo(new CreateTagResponse(live.Id, "Skadedyr", false)));
        var rows = await TagRowsAsync("Skadedyr");
        Assert.That(rows.Single(t => t.Id == removed.Id).WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
    }

    [Test]
    public async Task CreateTagAsync_TwoConcurrentCalls_SameName_OneTag()
    {
        await using var db1 = CreateFreshBackendConfigurationDbContext();
        await using var db2 = CreateFreshBackendConfigurationDbContext();
        InboxHubService NewService(BackendConfigurationPnDbContext db) => new(db, _storage,
            new SenderVerdictResolver(db), NullLogger<InboxHubService>.Instance);

        var results = await Task.WhenAll(NewService(db1).CreateTagAsync("Skadedyr"),
            NewService(db2).CreateTagAsync("Skadedyr"));

        Assert.That(results.Select(r => r!.Id).Distinct().Count(), Is.EqualTo(1));
        Assert.That(results.Count(r => r!.Created), Is.EqualTo(1));
        Assert.That(await TagRowsAsync("Skadedyr"), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task HubAndArchiveConcurrent_SameName_OneTag()
    {
        await using var db1 = CreateFreshBackendConfigurationDbContext();
        await using var db2 = CreateFreshBackendConfigurationDbContext();
        var user = Substitute.For<IUserService>();
        user.UserId.Returns(5);
        var archive = new BackendConfigurationTagsService(
            new BackendConfigurationLocalizationService(),
            NullLogger<BackendConfigurationTagsService>.Instance,
            db2, user);

        var hubTask = new InboxHubService(db1, _storage, new SenderVerdictResolver(db1), NullLogger<InboxHubService>.Instance)
            .CreateTagAsync("Skadedyr");
        var archiveTask = archive.CreateTag(new CommonTagModel { Name = "Skadedyr" });
        await Task.WhenAll(hubTask, archiveTask);

        var hub = await hubTask;
        var archiveResult = await archiveTask;
        Assert.That(archiveResult.Success, Is.True, archiveResult.Message);
        var row = (await TagRowsAsync("Skadedyr")).Single();
        Assert.That(hub!.Id, Is.EqualTo(row.Id));
    }

    [TestCase("Skadedyr", "Skadedyr")]
    [TestCase("  #Skadedyr  ", "Skadedyr")]
    [TestCase("# Skadedyr", "Skadedyr")]
    [TestCase("##x", "#x")]
    [TestCase("Æbletræ øst", "Æbletræ øst")]
    [TestCase("Skadedyr\n", "Skadedyr")]
    [TestCase("", null)]
    [TestCase("   ", null)]
    [TestCase("#", null)]
    [TestCase(" # ", null)]
    [TestCase("a\tb", null)]
    [TestCase("a\u0007b", null)]
    [TestCase(null, null)]
    public void NormalizeTagName_Cases(string? raw, string? expected) =>
        Assert.That(InboxHubService.NormalizeTagName(raw), Is.EqualTo(expected));

    [Test]
    public void NormalizeTagName_LengthLimit()
    {
        Assert.That(InboxHubService.NormalizeTagName(new string('a', 100)), Is.EqualTo(new string('a', 100)));
        Assert.That(InboxHubService.NormalizeTagName("#" + new string('a', 100)), Is.EqualTo(new string('a', 100)));
        Assert.That(InboxHubService.NormalizeTagName(new string('a', 101)), Is.Null);
    }
}
