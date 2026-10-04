using System.Text;
using BackendConfiguration.Pn.Infrastructure.Models.Inbox;
using BackendConfiguration.Pn.Services.FileArchive;
using BackendConfiguration.Pn.Services.InboundMail;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;
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

    [SetUp]
    public async Task SetUpService()
    {
        await IdentityTestUtils.ReserveEformUserId1Async(BaseDbContext!);
        await InboxTestData.ClearAsync(BackendConfigurationPnDbContext!);
        BaseDbContext!.Users.RemoveRange(BaseDbContext.Users.Where(u => u.Email!.EndsWith("@example.org")));
        await BaseDbContext.SaveChangesAsync();
        await SetPolicyAsync("hold");
        _storage = Substitute.For<IArchiveStorage>();
        _service = new InboxHubService(BackendConfigurationPnDbContext!, _storage,
            new SenderVerdictResolver(BackendConfigurationPnDbContext!, BaseDbContext!),
            NullLogger<InboxHubService>.Instance);
    }

    private async Task SetPolicyAsync(string value)
    {
        var row = await BackendConfigurationPnDbContext!.PluginConfigurationValues
            .SingleOrDefaultAsync(x => x.Name == SenderVerdictResolver.PolicyName);
        if (row == null)
            BackendConfigurationPnDbContext.PluginConfigurationValues.Add(
                new PluginConfigurationValue { Name = SenderVerdictResolver.PolicyName, Value = value });
        else row.Value = value;
        await BackendConfigurationPnDbContext.SaveChangesAsync();
    }

    private static ArrivedRequest Arrived(string from = "post@example.net", string? id = null) => new(
        id ?? Guid.NewGuid().ToString(), from, "Dokumenter", DateTime.UtcNow, "scan_0012.pdf", 1000,
        "not-checked", "not-checked", null);

    private static MemoryStream Pdf() => new(Encoding.ASCII.GetBytes("%PDF-1.7 x"));

    [Test]
    public async Task Verdict_UnknownSender_HoldPolicy_CreatesSenderPending()
    {
        var res = await _service.ArrivedAsync(Arrived());

        Assert.That(res.SenderVerdict, Is.EqualTo(SenderVerdict.Unknown));
        var doc = await BackendConfigurationPnDbContext!.InboxDocuments.AsNoTracking().SingleAsync();
        Assert.That(doc.Status, Is.EqualTo(InboxDocumentStatus.SenderPending));
    }

    [Test]
    public async Task Verdict_UnknownSender_RefusePolicy_BlockedNoRow()
    {
        await SetPolicyAsync("refuse");

        var res = await _service.ArrivedAsync(Arrived());

        Assert.That(res.SenderVerdict, Is.EqualTo(SenderVerdict.Blocked));
        Assert.That(await BackendConfigurationPnDbContext!.InboxDocuments.CountAsync(), Is.EqualTo(0));
    }

    [Test]
    public async Task Verdict_UserEmail_CaseInsensitive()
    {
        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);
        var created = await userManager.CreateAsync(new EformUser
        {
            UserName = "jane.doe@example.org", Email = "jane.doe@example.org", FirstName = "Jane", LastName = "Doe",
            Locale = "da", EmailConfirmed = true, TimeZone = "Europe/Copenhagen", Formats = "de-DE"
        });
        Assert.That(created.Succeeded, Is.True, string.Join(",", created.Errors.Select(e => e.Description)));

        var res = await _service.ArrivedAsync(Arrived("Jane.Doe@example.org"));

        Assert.That(res.SenderVerdict, Is.EqualTo(SenderVerdict.Allowed));
        Assert.That((await BackendConfigurationPnDbContext!.InboxDocuments.AsNoTracking().SingleAsync()).Status,
            Is.EqualTo(InboxDocumentStatus.Preparing));
    }

    [Test]
    public async Task Verdict_InactiveUserEmail_Unknown()
    {
        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);
        var created = await userManager.CreateAsync(new EformUser
        {
            UserName = "john.roe@example.org", Email = "john.roe@example.org", FirstName = "John", LastName = "Roe",
            Locale = "da", EmailConfirmed = true, TimeZone = "Europe/Copenhagen", Formats = "de-DE", IsActive = false
        });
        Assert.That(created.Succeeded, Is.True, string.Join(",", created.Errors.Select(e => e.Description)));

        var res = await _service.ArrivedAsync(Arrived("john.roe@example.org"));

        Assert.That(res.SenderVerdict, Is.EqualTo(SenderVerdict.Unknown));
        Assert.That((await BackendConfigurationPnDbContext!.InboxDocuments.AsNoTracking().SingleAsync()).Status,
            Is.EqualTo(InboxDocumentStatus.SenderPending));
    }

    [Test]
    public async Task Arrived_Replay_OfHeldDocumentThatFailed_StaysUnknown()
    {
        var req = Arrived();
        await _service.ArrivedAsync(req);
        await _service.FailedAsync(new FailedRequest(req.HubDocumentId, "PDF-filen kunne ikke læses."));

        var replay = await _service.ArrivedAsync(req);

        Assert.That(replay.SenderVerdict, Is.EqualTo(SenderVerdict.Unknown));
        Assert.That(await BackendConfigurationPnDbContext!.InboxDocuments.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task Verdict_DomainAllowRule_Allowed_ButBlockRuleWins()
    {
        await new InboxSenderRule { Pattern = "@example.net", Kind = InboxSenderRuleKind.Allow, CreatedByUserId = 1, UpdatedByUserId = 1 }
            .Create(BackendConfigurationPnDbContext!);
        Assert.That((await _service.ArrivedAsync(Arrived("post@example.net"))).SenderVerdict, Is.EqualTo(SenderVerdict.Allowed));

        await new InboxSenderRule { Pattern = "spam@example.net", Kind = InboxSenderRuleKind.Block, CreatedByUserId = 1, UpdatedByUserId = 1 }
            .Create(BackendConfigurationPnDbContext!);
        Assert.That((await _service.ArrivedAsync(Arrived("spam@example.net"))).SenderVerdict, Is.EqualTo(SenderVerdict.Blocked));
    }

    [Test]
    public async Task Arrived_Twice_SameRow()
    {
        var req = Arrived();
        await _service.ArrivedAsync(req);
        var second = await _service.ArrivedAsync(req);

        Assert.That(second.SenderVerdict, Is.EqualTo(SenderVerdict.Unknown));
        Assert.That(await BackendConfigurationPnDbContext!.InboxDocuments.CountAsync(), Is.EqualTo(1));
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
    }

    private async Task<InboxDocument> ArrivedDocumentAsync(InboxSenderRuleKind? rule)
    {
        var req = Arrived("post@example.net");
        if (rule != null)
        {
            await new InboxSenderRule { Pattern = "post@example.net", Kind = rule.Value, CreatedByUserId = 1, UpdatedByUserId = 1 }
                .Create(BackendConfigurationPnDbContext!);
        }

        await _service.ArrivedAsync(req);
        return await BackendConfigurationPnDbContext!.InboxDocuments.SingleAsync(d => d.HubDocumentId == req.HubDocumentId);
    }

    private Task<InboxDocument> PreparingAsync() => ArrivedDocumentAsync(InboxSenderRuleKind.Allow);

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
    public async Task Deliver_SenderPending_StoresButStaysSenderPending()
    {
        var doc = await ArrivedDocumentAsync(null);
        Assert.That(doc.Status, Is.EqualTo(InboxDocumentStatus.SenderPending));
        var p = await InboxTestData.PropertyAsync(BackendConfigurationPnDbContext!);

        await _service.DeliverAsync(new DeliverMetadata(doc.HubDocumentId, 1, false,
            [new("property", p.Id, "textMatch", 0.5, null, 1, null)]), Pdf(), "a.pdf");

        await BackendConfigurationPnDbContext!.Entry(doc).ReloadAsync();
        Assert.That(doc.Status, Is.EqualTo(InboxDocumentStatus.SenderPending));
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
    public async Task Deliver_UnknownHubDocument_CreatesSenderPendingRow()
    {
        var id = Guid.NewGuid().ToString();

        await _service.DeliverAsync(new DeliverMetadata(id, 1, false, []), Pdf(), "lost.pdf");

        // The sender was never checked, so the document waits for approve-sender.
        var doc = await BackendConfigurationPnDbContext!.InboxDocuments.AsNoTracking().SingleAsync(d => d.HubDocumentId == id);
        Assert.That(doc.Status, Is.EqualTo(InboxDocumentStatus.SenderPending));
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
    public async Task Deliver_AfterFailedForHeldSender_StaysSenderPending()
    {
        var doc = await ArrivedDocumentAsync(null);
        await _service.FailedAsync(new FailedRequest(doc.HubDocumentId, "Dokumentet kunne ikke behandles automatisk."));

        await _service.DeliverAsync(new DeliverMetadata(doc.HubDocumentId, 1, false, []), Pdf(), "a.pdf");

        await BackendConfigurationPnDbContext!.Entry(doc).ReloadAsync();
        Assert.That(doc.Status, Is.EqualTo(InboxDocumentStatus.SenderPending));
        Assert.That(doc.DeliveredAt, Is.Not.Null);
    }

    [Test]
    public async Task Deliver_AfterFailedForAllowedSender_IsReady()
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
}
