#nullable enable
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BackendConfiguration.Pn.Infrastructure.Models.Inbox;
using BackendConfiguration.Pn.Infrastructure.Models.Settings;
using BackendConfiguration.Pn.Services.InboundMail;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace BackendConfiguration.Pn.Integration.Test;

[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class InboxSettingsServiceTests : TestBaseSetup
{
    private const string HubUrl = "https://hub.example.net";
    private const string SigningKey = "c25608a9e5271a2bafd480022de09ea60a11c7970566d3dca57034c662d7c767";

    private IInboundMailHubClient _hub = null!;
    private ICustomerNoProvider _customerNo = null!;
    private InboxSettingsService _service = null!;

    [SetUp]
    public async Task SetUpService()
    {
        await InboxTestData.ClearAsync(BackendConfigurationPnDbContext!);
        // No seeded policy row: the default (hold) is the missing row.
        await BackendConfigurationPnDbContext!.PluginConfigurationValues
            .Where(x => x.Name == SenderVerdictResolver.PolicyName).ExecuteDeleteAsync();
        _hub = Substitute.For<IInboundMailHubClient>();
        _hub.IsConfigured.Returns(true);
        _customerNo = Substitute.For<ICustomerNoProvider>();
        _customerNo.GetAsync().Returns(4711);
        _service = NewService(_hub);
    }

    private InboxSettingsService NewService(IInboundMailHubClient hub, BackendConfigurationPnDbContext? db = null) =>
        new(db ?? BackendConfigurationPnDbContext!, hub, _customerNo,
            Options.Create(new InboundMailHubOptions { MailDomain = "indbakke.microting.dk" }),
            new BackendConfigurationLocalizationService(), NullLogger<InboxSettingsService>.Instance);

    private InboundMailHubClient RealClient(FakeHubHandler handler) =>
        new(new HttpClient(handler), Options.Create(new InboundMailHubOptions { HubUrl = HubUrl, TenantSigningKey = SigningKey }),
            _customerNo);

    private async Task<InboxDocument> SenderPendingDocumentAsync(string fromAddress = "post@example.net", bool delivered = false)
    {
        var doc = await InboxTestData.ReadyDocumentAsync(BackendConfigurationPnDbContext!);
        doc.Status = InboxDocumentStatus.SenderPending;
        doc.FromAddress = fromAddress;
        if (!delivered)
        {
            doc.DeliveredAt = null;
            doc.Md5 = null;
        }
        await doc.Update(BackendConfigurationPnDbContext!);
        return doc;
    }

    private Task<List<InboxSenderRule>> LiveRulesAsync() =>
        BackendConfigurationPnDbContext!.InboxSenderRules.Where(r => r.WorkflowState != "removed").ToListAsync();

    // ---- Address generator ----

    [Test]
    public void Generator_MakesValidAddress()
    {
        var (address, hash) = InboxAddressGenerator.New(4711, "indbakke.microting.dk");
        var m = Regex.Match(address, "^4711-([a-z2-7]{10})@indbakke\\.microting\\.dk$");
        Assert.That(m.Success, Is.True, address);
        Assert.That(hash, Is.EqualTo(InboxAddressGenerator.HashToken(m.Groups[1].Value)));
        Assert.That(hash, Does.Match("^[0-9a-f]{64}$"));
        Assert.That(InboxAddressGenerator.HashToken(m.Groups[1].Value.ToUpperInvariant()), Is.EqualTo(hash));
    }

    // ---- Settings: address ----

    [Test]
    public async Task Get_FirstTime_CreatesAndRegistersAddress()
    {
        var res = await _service.GetAsync(1);

        Assert.That(res.Success, Is.True);
        Assert.That(res.Model.Address, Does.StartWith("4711-"));
        Assert.That(res.Model.UnknownSenderPolicy, Is.EqualTo("hold"));
        var row = await BackendConfigurationPnDbContext!.InboxAddresses.SingleAsync();
        Assert.That(row.Active, Is.True);
        Assert.That(row.Address, Is.EqualTo(res.Model.Address));
        await _hub.Received(1).RegisterAddressAsync(row.TokenHash, null, null);
    }

    [Test]
    public async Task Get_ExistingAddress_DoesNotRegisterAgain()
    {
        var first = (await _service.GetAsync(1)).Model.Address;
        _hub.ClearReceivedCalls();

        var res = await _service.GetAsync(1);

        Assert.That(res.Model.Address, Is.EqualTo(first));
        await _hub.DidNotReceiveWithAnyArgs().RegisterAddressAsync(default!, default, default);
    }

    [Test]
    public async Task Get_ExistingAddress_HubNotConfigured_ShowsAddressWithNotConfiguredMessage()
    {
        var first = (await _service.GetAsync(1)).Model.Address;
        _hub.IsConfigured.Returns(false);

        var res = await _service.GetAsync(1);

        Assert.That(res.Success, Is.False);
        Assert.That(res.Message, Is.EqualTo("InboxNotConfigured"));
        Assert.That(res.Model.Address, Is.EqualTo(first));
    }

    [Test]
    public async Task Get_FirstTime_HubDown_NoAddressRowAndFailureMessage()
    {
        _hub.RegisterAddressAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<DateTime?>())
            .ThrowsAsync(new InboundMailHubException(InboundMailHubFailure.Unavailable, "down"));

        var res = await _service.GetAsync(1);

        Assert.That(res.Success, Is.False);
        Assert.That(res.Message, Is.EqualTo("InboxHubUnavailable"));
        Assert.That(res.Model, Is.Not.Null);
        Assert.That(res.Model.Address, Is.Null);
        Assert.That(await BackendConfigurationPnDbContext!.InboxAddresses.AnyAsync(), Is.False);
    }

    [Test]
    public async Task Get_UnknownCustomerNo_NotConfigured_NoAddress()
    {
        _customerNo.GetAsync().Returns(0);

        var res = await _service.GetAsync(1);

        Assert.That(res.Success, Is.False);
        Assert.That(res.Message, Is.EqualTo("InboxNotConfigured"));
        Assert.That(await BackendConfigurationPnDbContext!.InboxAddresses.AnyAsync(), Is.False);
    }

    [Test]
    public async Task Get_TwoConcurrentFirstGets_CreateOneAddress()
    {
        // The registration takes a while, so without the named lock both requests would create an address.
        _hub.RegisterAddressAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<DateTime?>())
            .Returns(_ => Task.Delay(500));
        var options = (DbContextOptions<BackendConfigurationPnDbContext>)BackendConfigurationPnDbContext!
            .GetService<IDbContextOptions>();
        await using var db1 = new BackendConfigurationPnDbContext(options);
        await using var db2 = new BackendConfigurationPnDbContext(options);

        var results = await Task.WhenAll(NewService(_hub, db1).GetAsync(1), NewService(_hub, db2).GetAsync(2));

        Assert.That(results.All(r => r.Success), Is.True, string.Join(" | ", results.Select(r => r.Message)));
        Assert.That(results[0].Model.Address, Is.EqualTo(results[1].Model.Address));
        var rows = await BackendConfigurationPnDbContext!.InboxAddresses.AsNoTracking().ToListAsync();
        Assert.That(rows.Count(a => a.Active), Is.EqualTo(1));
        Assert.That(rows, Has.Count.EqualTo(1));
        await _hub.ReceivedWithAnyArgs(1).RegisterAddressAsync(default!, default, default);
    }

    [Test]
    public async Task Rotate_KeepsOldAddressForSevenDays()
    {
        var first = (await _service.GetAsync(1)).Model.Address;

        var res = await _service.RotateAddressAsync(1);

        Assert.That(res.Success, Is.True);
        Assert.That(res.Model.Address, Is.Not.EqualTo(first));
        var rows = await BackendConfigurationPnDbContext!.InboxAddresses.OrderBy(a => a.Id).ToListAsync();
        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows[0].Active, Is.False);
        Assert.That(rows[0].GraceUntil, Is.EqualTo(DateTime.UtcNow.AddDays(7)).Within(TimeSpan.FromMinutes(1)));
        Assert.That(rows[1].Active, Is.True);
        Assert.That(rows[1].Address, Is.EqualTo(res.Model.Address));
        await _hub.Received(1).RegisterAddressAsync(rows[1].TokenHash, rows[0].TokenHash, rows[0].GraceUntil);
    }

    [Test]
    public async Task Rotate_HubDown_OldAddressStaysActive()
    {
        var first = (await _service.GetAsync(1)).Model.Address;
        _hub.RegisterAddressAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<DateTime?>())
            .ThrowsAsync(new InboundMailHubException(InboundMailHubFailure.Unavailable, "down"));

        var res = await _service.RotateAddressAsync(1);

        Assert.That(res.Success, Is.False);
        Assert.That(res.Message, Is.EqualTo("InboxHubUnavailable"));
        var row = await BackendConfigurationPnDbContext!.InboxAddresses.AsNoTracking().SingleAsync();
        Assert.That(row.Address, Is.EqualTo(first));
        Assert.That(row.Active, Is.True);
        Assert.That(row.GraceUntil, Is.Null);
    }

    // ---- Settings: rules and policy ----

    [Test]
    public async Task Update_ReplacesRulesAndPolicy()
    {
        await _service.UpdateAsync(new InboxSettingsModel
        {
            UnknownSenderPolicy = "refuse",
            SenderRules = [new() { Pattern = "@example.org", Kind = 0 }, new() { Pattern = "spam@example.net", Kind = 1 }]
        }, 1);
        var res = await _service.UpdateAsync(new InboxSettingsModel
        {
            UnknownSenderPolicy = "refuse", SenderRules = [new() { Pattern = " @Example.org ", Kind = 0 }]
        }, 1);

        Assert.That(res.Success, Is.True);
        var live = await LiveRulesAsync();
        Assert.That(live.Select(r => r.Pattern), Is.EquivalentTo(new[] { "@example.org" }));
        var policy = await BackendConfigurationPnDbContext!.PluginConfigurationValues.AsNoTracking()
            .SingleAsync(x => x.Name == SenderVerdictResolver.PolicyName);
        Assert.That(policy.Value, Is.EqualTo("refuse"));
    }

    [Test]
    public async Task Update_MissingPolicyRow_IsCreatedOnce_AndReportedByGet()
    {
        await _service.UpdateAsync(new InboxSettingsModel { UnknownSenderPolicy = "refuse" }, 1);
        await _service.UpdateAsync(new InboxSettingsModel { UnknownSenderPolicy = "hold" }, 1);
        await _service.UpdateAsync(new InboxSettingsModel { UnknownSenderPolicy = "refuse" }, 1);

        var rows = await BackendConfigurationPnDbContext!.PluginConfigurationValues.AsNoTracking()
            .Where(x => x.Name == SenderVerdictResolver.PolicyName).ToListAsync();
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].Value, Is.EqualTo("refuse"));
        Assert.That((await _service.GetAsync(1)).Model.UnknownSenderPolicy, Is.EqualTo("refuse"));
    }

    [Test]
    public async Task Update_NullRulesOrBody_DoesNotThrow()
    {
        var ok = await _service.UpdateAsync(new InboxSettingsModel { UnknownSenderPolicy = "hold", SenderRules = null! }, 1);
        Assert.That(ok.Success, Is.True);

        var nullBody = await _service.UpdateAsync(null!, 1);
        Assert.That(nullBody.Success, Is.False);
        Assert.That(nullBody.Message, Is.EqualTo("InboxInvalidUnknownSenderPolicy"));
    }

    [Test]
    public async Task Update_InvalidPattern_IsRejected()
    {
        var res = await _service.UpdateAsync(new InboxSettingsModel { SenderRules = [new() { Pattern = "not an address", Kind = 0 }] }, 1);
        Assert.That(res.Success, Is.False);
        Assert.That(res.Message, Is.EqualTo("InboxInvalidSenderRule"));
        Assert.That(await LiveRulesAsync(), Is.Empty);
    }

    [Test]
    public async Task Update_SamePatternAllowAndBlock_IsRejected()
    {
        var res = await _service.UpdateAsync(new InboxSettingsModel
        {
            SenderRules = [new() { Pattern = "post@example.net", Kind = 0 }, new() { Pattern = "Post@Example.net", Kind = 1 }]
        }, 1);
        Assert.That(res.Success, Is.False);
        Assert.That(res.Message, Is.EqualTo("InboxInvalidSenderRule"));
        Assert.That(await LiveRulesAsync(), Is.Empty);
    }

    [Test]
    public async Task Update_InvalidPolicy_IsRejected()
    {
        var res = await _service.UpdateAsync(new InboxSettingsModel { UnknownSenderPolicy = "drop" }, 1);
        Assert.That(res.Success, Is.False);
        Assert.That(res.Message, Is.EqualTo("InboxInvalidUnknownSenderPolicy"));
        Assert.That(await BackendConfigurationPnDbContext!.PluginConfigurationValues
            .AnyAsync(x => x.Name == SenderVerdictResolver.PolicyName), Is.False);
    }

    // ---- Sender decisions ----

    [Test]
    public async Task ApproveSender_AddsAllowRule_TellsHub_StaysPreparing()
    {
        var doc = await SenderPendingDocumentAsync();

        var res = await _service.ApproveSenderAsync(doc.Id, 1);

        Assert.That(res.Success, Is.True);
        await BackendConfigurationPnDbContext!.Entry(doc).ReloadAsync();
        Assert.That(doc.Status, Is.EqualTo(InboxDocumentStatus.Preparing));
        Assert.That(await BackendConfigurationPnDbContext.InboxSenderRules.AnyAsync(r => r.Pattern == "post@example.net" && r.Kind == InboxSenderRuleKind.Allow), Is.True);
        await _hub.Received(1).SenderDecisionAsync(doc.HubDocumentId, true);
    }

    [Test]
    public async Task ApproveSender_AlreadyDelivered_BecomesReady()
    {
        var doc = await SenderPendingDocumentAsync(delivered: true);

        var res = await _service.ApproveSenderAsync(doc.Id, 1);

        Assert.That(res.Success, Is.True);
        await BackendConfigurationPnDbContext!.Entry(doc).ReloadAsync();
        Assert.That(doc.Status, Is.EqualTo(InboxDocumentStatus.Ready));
    }

    [Test]
    public async Task ApproveSender_ExistingAllowRule_IsNotDuplicated()
    {
        await new InboxSenderRule { Pattern = "Post@Example.net", Kind = InboxSenderRuleKind.Allow, CreatedByUserId = 1, UpdatedByUserId = 1 }
            .Create(BackendConfigurationPnDbContext!);
        var doc = await SenderPendingDocumentAsync();

        await _service.ApproveSenderAsync(doc.Id, 1);

        Assert.That(await LiveRulesAsync(), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task ApproveSender_UnknownSenderPlaceholder_AddsNoRule()
    {
        var doc = await SenderPendingDocumentAsync(InboxHubService.UnknownSender);

        var res = await _service.ApproveSenderAsync(doc.Id, 1);

        Assert.That(res.Success, Is.True);
        Assert.That(await LiveRulesAsync(), Is.Empty);
        await BackendConfigurationPnDbContext!.Entry(doc).ReloadAsync();
        Assert.That(doc.Status, Is.EqualTo(InboxDocumentStatus.Preparing));
    }

    [Test]
    public async Task ApproveSender_NotSenderPending_IsRefused_HubNotCalled()
    {
        var doc = await InboxTestData.ReadyDocumentAsync(BackendConfigurationPnDbContext!);

        var res = await _service.ApproveSenderAsync(doc.Id, 1);

        Assert.That(res.Success, Is.False);
        Assert.That(res.Message, Is.EqualTo("InboxSenderAlreadyDecided"));
        await _hub.DidNotReceiveWithAnyArgs().SenderDecisionAsync(default!, default);
    }

    [Test]
    public async Task RejectSender_WithBlock_AddsBlockRule_Rejected()
    {
        var doc = await SenderPendingDocumentAsync();

        var res = await _service.RejectSenderAsync(doc.Id, true, 1);

        Assert.That(res.Success, Is.True);
        await BackendConfigurationPnDbContext!.Entry(doc).ReloadAsync();
        Assert.That(doc.Status, Is.EqualTo(InboxDocumentStatus.Rejected));
        Assert.That(await BackendConfigurationPnDbContext.InboxSenderRules.AnyAsync(r => r.Pattern == "post@example.net" && r.Kind == InboxSenderRuleKind.Block), Is.True);
        await _hub.Received(1).SenderDecisionAsync(doc.HubDocumentId, false);
    }

    [Test]
    public async Task RejectSender_WithoutBlock_AddsNoRule()
    {
        var doc = await SenderPendingDocumentAsync();

        await _service.RejectSenderAsync(doc.Id, false, 1);

        await BackendConfigurationPnDbContext!.Entry(doc).ReloadAsync();
        Assert.That(doc.Status, Is.EqualTo(InboxDocumentStatus.Rejected));
        Assert.That(await LiveRulesAsync(), Is.Empty);
    }

    [Test]
    public async Task ApproveSender_Hub409_TryAgainShortly_NothingWritten()
    {
        var handler = new FakeHubHandler(_ => new HttpResponseMessage(HttpStatusCode.Conflict));
        var service = NewService(RealClient(handler));
        var doc = await SenderPendingDocumentAsync();

        var res = await service.ApproveSenderAsync(doc.Id, 1);

        Assert.That(res.Success, Is.False);
        Assert.That(res.Message, Is.EqualTo("InboxTryAgainShortly"));
        Assert.That(handler.Requests, Has.Count.EqualTo(1));
        await BackendConfigurationPnDbContext!.Entry(doc).ReloadAsync();
        Assert.That(doc.Status, Is.EqualTo(InboxDocumentStatus.SenderPending));
        Assert.That(await LiveRulesAsync(), Is.Empty);
    }

    [Test]
    public async Task RejectSender_Hub409_TryAgainShortly_NothingWritten()
    {
        var handler = new FakeHubHandler(_ => new HttpResponseMessage(HttpStatusCode.Conflict));
        var service = NewService(RealClient(handler));
        var doc = await SenderPendingDocumentAsync();

        var res = await service.RejectSenderAsync(doc.Id, true, 1);

        Assert.That(res.Success, Is.False);
        Assert.That(res.Message, Is.EqualTo("InboxTryAgainShortly"));
        await BackendConfigurationPnDbContext!.Entry(doc).ReloadAsync();
        Assert.That(doc.Status, Is.EqualTo(InboxDocumentStatus.SenderPending));
        Assert.That(await LiveRulesAsync(), Is.Empty);
    }

    [Test]
    public async Task RejectSender_HubDown_FailureMessage_NothingWritten()
    {
        var handler = new FakeHubHandler(_ => throw new HttpRequestException("connection refused"));
        var service = NewService(RealClient(handler));
        var doc = await SenderPendingDocumentAsync();

        var res = await service.RejectSenderAsync(doc.Id, true, 1);

        Assert.That(res.Success, Is.False);
        Assert.That(res.Message, Is.EqualTo("InboxHubUnavailable"));
        await BackendConfigurationPnDbContext!.Entry(doc).ReloadAsync();
        Assert.That(doc.Status, Is.EqualTo(InboxDocumentStatus.SenderPending));
        Assert.That(await LiveRulesAsync(), Is.Empty);
    }

    // ---- Outbound client ----

    [Test]
    public async Task HubClient_SignsRequestWithTheInboundScheme()
    {
        var handler = new FakeHubHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var graceUntil = new DateTime(2026, 10, 11, 10, 0, 0, DateTimeKind.Utc);

        await RealClient(handler).RegisterAddressAsync("abc", "def", graceUntil);

        var req = handler.Requests.Single();
        Assert.That(req.Method, Is.EqualTo("PUT"));
        Assert.That(req.Uri, Is.EqualTo(new Uri("https://hub.example.net/api/tenants/4711/address")));
        Assert.That(req.Headers["X-Customer-No"], Is.EqualTo("4711"));
        Assert.That(Guid.TryParse(req.Headers["X-Request-Id"], out _), Is.True);
        var date = DateTime.ParseExact(req.Headers["Date"], "R", CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
        Assert.That(date, Is.EqualTo(DateTime.UtcNow).Within(TimeSpan.FromMinutes(2)));
        var canonical = InboundMailSignature.Canonical("PUT", "/api/tenants/4711/address", 4711,
            req.Headers["X-Request-Id"], req.Headers["Date"], InboundMailSignature.BodyHash(req.Body));
        Assert.That(req.Headers["Authorization"],
            Is.EqualTo(InboundMailSignature.Scheme + InboundMailSignature.Sign(SigningKey, canonical)));

        using var json = JsonDocument.Parse(req.Body);
        Assert.That(json.RootElement.GetProperty("tokenHash").GetString(), Is.EqualTo("abc"));
        Assert.That(json.RootElement.GetProperty("previousTokenHash").GetString(), Is.EqualTo("def"));
        Assert.That(json.RootElement.GetProperty("graceUntil").GetDateTime(), Is.EqualTo(graceUntil));
        Assert.That(json.RootElement.GetProperty("tenantName").GetString(), Is.EqualTo("Kunde 4711"));
    }

    [Test]
    public async Task HubClient_SenderDecision_PostsDecision()
    {
        var handler = new FakeHubHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        await RealClient(handler).SenderDecisionAsync("0f8fad5b-d9cb-469f-a165-70867728950e", false);

        var req = handler.Requests.Single();
        Assert.That(req.Method, Is.EqualTo("POST"));
        Assert.That(req.Uri.AbsolutePath,
            Is.EqualTo("/api/tenants/4711/documents/0f8fad5b-d9cb-469f-a165-70867728950e/sender-decision"));
        Assert.That(Encoding.UTF8.GetString(req.Body), Is.EqualTo("{\"decision\":\"reject\"}"));
    }

    [Test]
    public async Task HubClient_NotConfigured_Throws_WithoutCallingHub()
    {
        var handler = new FakeHubHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var client = new InboundMailHubClient(new HttpClient(handler),
            Options.Create(new InboundMailHubOptions { HubUrl = HubUrl }), _customerNo);

        var e = await Assert.ThrowsAsync<InboundMailHubException>(() => client.SenderDecisionAsync("x", true));

        Assert.That(e!.Failure, Is.EqualTo(InboundMailHubFailure.NotConfigured));
        Assert.That(client.IsConfigured, Is.False);
        Assert.That(handler.Requests, Is.Empty);
    }

    private sealed record CapturedRequest(string Method, Uri Uri, Dictionary<string, string> Headers, byte[] Body);

    private sealed class FakeHubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            // HttpClient normalises known header names (X-Request-Id becomes X-Request-ID); HTTP header names are case-insensitive.
            var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
            var body = request.Content == null ? [] : await request.Content.ReadAsByteArrayAsync(ct);
            Requests.Add(new CapturedRequest(request.Method.Method, request.RequestUri!, headers, body));
            return respond(request);
        }
    }
}
