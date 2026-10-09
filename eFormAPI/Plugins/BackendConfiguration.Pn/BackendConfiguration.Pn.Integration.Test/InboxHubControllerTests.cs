#nullable enable
using System.Globalization;
using System.Text;
using System.Text.Json;
using BackendConfiguration.Pn.Controllers;
using BackendConfiguration.Pn.Infrastructure.Models.Inbox;
using BackendConfiguration.Pn.Infrastructure.Models.Settings;
using BackendConfiguration.Pn.Services.FileArchive;
using BackendConfiguration.Pn.Services.InboundMail;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>The signed calls of the central inbound mail service, through the controller with a real verifier.</summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class InboxHubControllerTests : TestBaseSetup
{
    private const string TenantKey = "c25608a9e5271a2bafd480022de09ea60a11c7970566d3dca57034c662d7c767";
    private const int CustomerNo = 4711;
    private const string HubPath = "/api/backend-configuration-pn/inbox/hub/";

    private InboundMailRequestVerifier _verifier = null!;

    [SetUp]
    public async Task SetUpController()
    {
        await InboxTestData.ClearAsync(BackendConfigurationPnDbContext!);
        _verifier = new InboundMailRequestVerifier(Options.Create(new InboundMailHubOptions { TenantSigningKey = TenantKey }));
    }

    private InboxHubController Controller(string method, string action, byte[] body, string? key = TenantKey,
        string? requestId = null, string? date = null)
    {
        var http = new DefaultHttpContext();
        var path = HubPath + action;
        http.Request.Method = method;
        http.Request.Path = path;
        http.Request.Body = new MemoryStream(body);
        http.Request.ContentLength = body.Length;
        http.Request.ContentType = "application/json";
        if (key != null)
        {
            date ??= DateTime.UtcNow.ToString("R", CultureInfo.InvariantCulture);
            requestId ??= Guid.NewGuid().ToString();
            var canonical = InboundMailSignature.Canonical(method, path, CustomerNo, requestId, date,
                InboundMailSignature.BodyHash(body));
            http.Request.Headers["Authorization"] = InboundMailSignature.Scheme + InboundMailSignature.Sign(key, canonical);
            http.Request.Headers["Date"] = date;
            http.Request.Headers["X-Request-Id"] = requestId;
            http.Request.Headers["X-Customer-No"] = CustomerNo.ToString(CultureInfo.InvariantCulture);
        }

        var customerNo = Substitute.For<ICustomerNoProvider>();
        customerNo.GetAsync().Returns(CustomerNo);
        var service = new InboxHubService(BackendConfigurationPnDbContext!, Substitute.For<IArchiveStorage>(),
            new SenderVerdictResolver(BackendConfigurationPnDbContext!), NullLogger<InboxHubService>.Instance);
        return new InboxHubController(service, _verifier, customerNo)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };
    }

    private static byte[] NameBody(string? name) => JsonSerializer.SerializeToUtf8Bytes(new { name });

    private static JsonElement Json(IActionResult r) => JsonDocument.Parse(((ContentResult)r).Content!).RootElement;

    private static int? Status(IActionResult r) => r switch
    {
        StatusCodeResult s => s.StatusCode,
        ObjectResult o => o.StatusCode,
        ContentResult => 200,
        _ => null
    };

    [Test]
    public async Task Catalog_AnswersCanCreateTags()
    {
        var result = await Controller("GET", "catalog", []).Catalog();

        var json = Json(result);
        Assert.That(json.GetProperty("canCreateTags").GetBoolean(), Is.True);
    }

    [Test]
    public async Task Tags_Signed_CreatesTag_AnswersIdNameCreated()
    {
        var result = await Controller("POST", "tags", NameBody("Skadedyr")).Tags();

        var content = (ContentResult)result;
        Assert.That(content.ContentType, Is.EqualTo("application/json"));
        var json = Json(result);
        var row = await BackendConfigurationPnDbContext!.FileTags.AsNoTracking().SingleAsync();
        Assert.That(json.GetProperty("id").GetInt32(), Is.EqualTo(row.Id));
        Assert.That(json.GetProperty("name").GetString(), Is.EqualTo("Skadedyr"));
        Assert.That(json.GetProperty("created").GetBoolean(), Is.True);
    }

    [Test]
    public async Task Tags_HashAndWhitespace_ReturnExistingTag()
    {
        var existing = await InboxTestData.TagAsync(BackendConfigurationPnDbContext!, "Skadedyr");

        var result = await Controller("POST", "tags", NameBody("  #Skadedyr ")).Tags();

        var json = Json(result);
        Assert.That(json.GetProperty("id").GetInt32(), Is.EqualTo(existing.Id));
        Assert.That(json.GetProperty("created").GetBoolean(), Is.False);
        Assert.That(await BackendConfigurationPnDbContext!.FileTags.CountAsync(), Is.EqualTo(1));
    }

    [TestCase(null)]
    [TestCase("0000000000000000000000000000000000000000000000000000000000000000")]
    public async Task Tags_BadSignature_Is401_AndCreatesNothing(string? key)
    {
        var result = await Controller("POST", "tags", NameBody("Skadedyr"), key).Tags();

        Assert.That(Status(result), Is.EqualTo(401));
        Assert.That(await BackendConfigurationPnDbContext!.FileTags.AnyAsync(), Is.False);
    }

    [Test]
    public async Task Tags_ReplayedRequest_Is401_CreatesOnce()
    {
        var body = NameBody("Skadedyr");
        var requestId = Guid.NewGuid().ToString();
        var date = DateTime.UtcNow.ToString("R", CultureInfo.InvariantCulture);

        var first = await Controller("POST", "tags", body, requestId: requestId, date: date).Tags();
        var second = await Controller("POST", "tags", body, requestId: requestId, date: date).Tags();

        Assert.That(Json(first).GetProperty("created").GetBoolean(), Is.True);
        Assert.That(Status(second), Is.EqualTo(401));
        Assert.That(await BackendConfigurationPnDbContext!.FileTags.CountAsync(), Is.EqualTo(1));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("#")]
    [TestCase("a\tb")]
    [TestCase(null)]
    public async Task Tags_InvalidName_Is400_AndCreatesNothing(string? name)
    {
        var result = await Controller("POST", "tags", NameBody(name)).Tags();

        Assert.That(Status(result), Is.EqualTo(400));
        Assert.That(await BackendConfigurationPnDbContext!.FileTags.AnyAsync(), Is.False);
    }

    [Test]
    public async Task Tags_NameOver100Characters_Is400()
    {
        var result = await Controller("POST", "tags", NameBody(new string('a', 101))).Tags();

        Assert.That(Status(result), Is.EqualTo(400));
        Assert.That(await BackendConfigurationPnDbContext!.FileTags.AnyAsync(), Is.False);
    }

    [TestCase("not json")]
    [TestCase("{\"name\":5}")]
    [TestCase("{}")]
    public async Task Tags_MalformedBody_Is400(string body)
    {
        var result = await Controller("POST", "tags", Encoding.UTF8.GetBytes(body)).Tags();

        Assert.That(Status(result), Is.EqualTo(400));
    }

    [Test]
    public async Task Tags_LockHeldElsewhere_Is503_ThenSucceeds()
    {
        // Another request (e.g. the archive's bulk tag creation) holds the tag lock past InboxNamedLock's 15 s wait.
        await using (var holder = CreateFreshBackendConfigurationDbContext())
        {
            await holder.Database.OpenConnectionAsync();
            await using var cmd = holder.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = $"SELECT GET_LOCK(CONCAT('{InboxNamedLock.TagCreate}', ':', DATABASE()), 5)";
            Assert.That(Convert.ToInt64(await cmd.ExecuteScalarAsync()), Is.EqualTo(1));

            var blocked = await Controller("POST", "tags", NameBody("Skadedyr")).Tags();

            Assert.That(Status(blocked), Is.EqualTo(503));
            Assert.That(await BackendConfigurationPnDbContext!.FileTags.AnyAsync(), Is.False);
        } // disposing the holder closes its connection, which releases the lock

        var retried = await Controller("POST", "tags", NameBody("Skadedyr")).Tags();

        Assert.That(Json(retried).GetProperty("created").GetBoolean(), Is.True);
    }
}
