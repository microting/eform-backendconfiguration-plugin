#nullable enable
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using BackendConfiguration.Pn.Hubs;
using BackendConfiguration.Pn.Services.GrpcServices;
using BackendConfiguration.Pn.Services.LiveNotifier;
using BackendConfiguration.Pn.Services.UserPropertyAccess;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.Tokens;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformAngularFrontendBase.Infrastructure.Const;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using NSubstitute;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// End to end on the wire, as the mobile app will talk to the hub: a real
/// Kestrel on a loopback port, authentication configured exactly as the host
/// configures it (eform-angular-frontend eFormAPI.Web/Hosting/Security/
/// AuthServiceCollectionExtensions.cs, identical on origin/stable 7230d38ba,
/// origin/master 44aaef0ef and origin/chore/dotnet-11 7f8358e51: the REAL JwtBearer handler, default
/// scheme, TokenValidationParameters with issuer = audience and an HS256 key,
/// nothing else; UseAuthentication before UseRouting, Startup.cs), tokens
/// minted exactly as the host's AuthService.GenerateToken does (sub, jti,
/// updated_at; issuer = audience; HS256; expires), the plugin's own hub
/// registration and MapHub, and a raw <see cref="ClientWebSocket"/> that
/// speaks the SignalR JSON protocol by hand with NO negotiate request.
/// Every frame the client sees is written to the test output ("WIRE ...")
/// with tokens redacted, for the app team's codec.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class LiveHubWireTests : TestBaseSetup
{
    private const string Issuer = "eform-wire-test";
    private const string SigningKey = "a-wire-test-signing-key-that-is-long-enough-for-hs256-0123456789";
    private const int UserId = 5;
    private const int Site = 9301;
    private const char RecordSeparator = '\u001e';

    private WebApplication _app = null!;
    private FakeTimeProvider _clock = null!;
    private WarningCounter _warnings = null!;
    private int _port;
    private int _property;

    [SetUp]
    public async Task StartServer()
    {
        // The hub's clock starts at the real time (the JWT library validates lifetimes against it).
        _clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        _warnings = new WarningCounter();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders().AddProvider(_warnings).SetMinimumLevel(LogLevel.Debug);

        // The host's AddEFormAuth, minus cookies and policies the hub does not use.
        builder.Services.AddAuthentication(o => o.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(cfg =>
            {
                cfg.RequireHttpsMetadata = false;
                cfg.SaveToken = true;
                cfg.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = Issuer,
                    ValidAudience = Issuer,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey))
                };
            });

        // The plugin's own registration, on the test database; the host's user service and
        // the SDK site lookup are faked at their seams.
        builder.Services.AddSingleton<TimeProvider>(_clock);
        builder.Services.AddBackendConfigurationLiveHub();
        var bc = ConnectionStringFor("420_eform-backend-configuration-plugin");
        builder.Services.AddDbContext<BackendConfigurationPnDbContext>(o => o.UseMySql(bc, ServerVersion.AutoDetect(bc)));
        builder.Services.AddTransient<IBackendConfigurationUserPropertyAccess, BackendConfigurationUserPropertyAccess>();
        var users = Substitute.For<IUserService>();
        users.GetByIdAsync(UserId).Returns(_ => Task.FromResult(new EformUser { Id = UserId, Email = "w@example.test", IsActive = true }));
        builder.Services.AddSingleton(users);
        var sites = Substitute.For<IGrpcSiteResolver>();
        sites.GetSdkSiteIdForUserAsync(Arg.Any<EformUser>()).Returns(Task.FromResult(Site));
        builder.Services.AddSingleton(sites);

        _app = builder.Build();
        // The host's order: UseAuthentication, UseRouting, then the plugin's UseEndpoints.
        _app.UseAuthentication();
        _app.UseRouting();
        _app.MapBackendConfigurationLiveHub();
        await _app.StartAsync();
        var address = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        _port = new Uri(address).Port;

        var property = new Property
        {
            Name = $"wire-{Guid.NewGuid():N}", ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Properties.AddAsync(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        _property = property.Id;
        await BackendConfigurationPnDbContext.PropertyWorkers.AddAsync(new PropertyWorker
        {
            PropertyId = _property, WorkerId = Site,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();
    }

    [TearDown]
    public async Task StopServer()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        _warnings.Dispose();
    }

    /// <summary>The host's AuthService.GenerateToken: sub, jti, updated_at (ms timestamp), issuer = audience, HS256.</summary>
    private static string HostToken(DateTime expiresUtc, int userId = UserId)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(AuthConsts.ClaimLastUpdateKey, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture)),
            // GenerateToken also adds the user's locale and roles (ClaimTypes.Role, written as "role").
            new("locale", "da"),
            new(ClaimTypes.Role, "user"),
        };
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
            SecurityAlgorithms.HmacSha256);
        // notBefore: a token that must already be expired needs a nbf before its exp.
        var token = new JwtSecurityToken(Issuer, Issuer, claims, notBefore: expiresUtc.AddHours(-24),
            expires: expiresUtc, signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>The query name the hub reads the session from.</summary>
    private const string QueryName = "access" + "_token";

    private Uri HubUri(string? token) => new(token is null
        ? $"ws://127.0.0.1:{_port}{LiveHubContract.HubPath}"
        : $"ws://127.0.0.1:{_port}{LiveHubContract.HubPath}?{QueryName}" + "=" + Uri.EscapeDataString(token));

    private static void Wire(string label, string what) =>
        TestContext.Out.WriteLine($"WIRE [{TestContext.CurrentContext.Test.Name}] {label}: {what}");

    private ILiveNotifier Notifier => _app.Services.GetRequiredService<ILiveNotifier>();

    /// <summary>The test property's id as the wire carries it (a decimal string).</summary>
    private string PropertyArg => $"{_property}";

    /// <summary>
    /// After <c>Refused</c>: SignalR's own Close message (type 7, no error, no
    /// allowReconnect), then the WebSocket close; nothing else in between.
    /// </summary>
    private static async Task ExpectCloseAsync(RawHubClient client)
    {
        var close = await client.NextRecordAsync();
        Wire("server", close ?? "(no close message)");
        Assert.That(close, Is.EqualTo("""{"type":7}"""));
        var after = await client.NextRecordAsync();
        Wire("then", after ?? $"websocket close: frame={client.CloseFrameReceived} status={client.CloseStatus?.ToString() ?? "none"} description='{client.CloseDescription}' state={client.State} error={client.ReceiveError?.GetType().Name ?? "none"}");
        // A clean close, not merely "the socket ended": a reset or abort (no close frame,
        // a WebSocketException) must fail here.
        Assert.Multiple(() =>
        {
            Assert.That(after, Is.Null, "the socket closes after the Close message");
            Assert.That(client.ReceiveError, Is.Null, "no abort or reset");
            Assert.That(client.CloseFrameReceived, Is.True, "the server sent a WebSocket close frame");
            Assert.That(client.CloseStatus, Is.EqualTo(WebSocketCloseStatus.NormalClosure), "close code 1000");
        });
    }

    // ------------------------------------------------------------------

    [Test]
    public async Task AHostToken_WithoutNegotiate_Handshakes_Joins_AndReceivesHints()
    {
        var expires = DateTime.UtcNow.AddHours(24);
        await using var client = await RawHubClient.ConnectAsync(HubUri(HostToken(expires)));
        Wire("upgrade", $"HTTP {client.UpgradeStatus}");
        Assert.That(client.UpgradeStatus, Is.EqualTo(101));

        Assert.That(await client.HandshakeAsync(), Is.EqualTo("{}"));

        var joined = await client.InvokeAsync("1", "JoinProperty", PropertyArg);
        Assert.That(joined, Is.EqualTo("""{"type":3,"invocationId":"1","result":{"v":1,"code":"ok"}}"""));
        var refused = await client.InvokeAsync("2", "JoinProperty", "987654321");
        Assert.That(refused, Is.EqualTo("""{"type":3,"invocationId":"2","result":{"v":1,"code":"property_not_available"}}"""));

        await Notifier.TaskChangedAsync(_property, eventId: 7, planDay: new DateOnly(2026, 11, 23), complianceId: 900);
        var taskChanged = await client.NextRecordAsync();
        Wire("server", taskChanged!);
        Assert.That(taskChanged, Is.EqualTo(
            $$$"""{"type":1,"target":"TaskChanged","arguments":[{"v":1,"property_id":"{{{_property}}}","event_id":"7","plan_day_key":"2026-11-23","compliance_id":900,"kind":"changed"}]}"""));

        await Notifier.AssignmentsChangedAsync(Site);
        var assignments = await client.NextRecordAsync();
        Wire("server", assignments!);
        Assert.That(assignments, Is.EqualTo("""{"type":1,"target":"AssignmentsChanged","arguments":[{"v":1}]}"""));

        Assert.That(_warnings.WarningsOf<LiveHubAuthenticator>(), Is.Zero,
            "the real JwtBearer handler's expiry was read (no 'no readable expiry' warning)");
    }

    /// <summary>
    /// The cut-off is the real token's exp + 5 minutes, read through the real
    /// handler: one second before it a hint still arrives; at it, the next
    /// publish ends the connection with Refused, then a close.
    /// </summary>
    [Test]
    public async Task ATokenThatExpiresWhileConnected_IsCutOffAtExpPlusFiveMinutes_WithRefusedThenClose()
    {
        var now = DateTime.UtcNow;
        var expires = new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc).AddMinutes(2);
        await using var client = await RawHubClient.ConnectAsync(HubUri(HostToken(expires)));
        Assert.That(await client.HandshakeAsync(), Is.EqualTo("{}"));
        Assert.That(await client.InvokeAsync("1", "JoinProperty", PropertyArg),
            Does.Contain("\"code\":\"ok\""));
        var cutOff = new DateTimeOffset(expires) + LiveHubAuthenticator.ClockSkew;

        _clock.SetUtcNow(cutOff - TimeSpan.FromSeconds(1));
        await Notifier.TaskChangedAsync(_property);
        Assert.That(await client.NextRecordAsync(), Does.StartWith("""{"type":1,"target":"TaskChanged","""));

        _clock.SetUtcNow(cutOff);
        await Notifier.TaskChangedAsync(_property);
        var refused = await client.NextRecordAsync();
        Wire("server", refused!);
        Assert.That(refused, Is.EqualTo("""{"type":1,"target":"Refused","arguments":[{"v":1,"code":"not_authenticated"}]}"""));
        await ExpectCloseAsync(client);
        Assert.That(_warnings.WarningsOf<LiveHubAuthenticator>(), Is.Zero);
    }

    [TestCase("none")]
    [TestCase("garbage")]
    [TestCase("expired")]
    public async Task ARefusedToken_UpgradesAndHandshakes_ThenGetsRefused_ThenAClose(string kind)
    {
        var token = kind switch
        {
            "none" => null,
            "garbage" => "not-a-jwt",
            // Past exp + the handler's 5-minute skew: the real JwtBearer handler rejects it.
            _ => HostToken(DateTime.UtcNow.AddMinutes(-10))
        };

        await using var client = await RawHubClient.ConnectAsync(HubUri(token));
        Wire("upgrade", $"HTTP {client.UpgradeStatus}");
        Assert.That(client.UpgradeStatus, Is.EqualTo(101), "the upgrade itself is not refused");
        var handshake = await client.HandshakeAsync();
        Assert.That(handshake, Is.EqualTo("{}"));

        var refused = await client.NextRecordAsync();
        Wire("server", refused ?? "(nothing)");
        Assert.That(refused, Is.EqualTo("""{"type":1,"target":"Refused","arguments":[{"v":1,"code":"not_authenticated"}]}"""));
        await ExpectCloseAsync(client);
    }

    /// <summary>The server's keep-alive: the first ping after an idle handshake, and its interval.</summary>
    [Test]
    public async Task TheServerPings_EveryKeepAliveInterval()
    {
        await using var client = await RawHubClient.ConnectAsync(HubUri(HostToken(DateTime.UtcNow.AddHours(1))));
        Assert.That(await client.HandshakeAsync(), Is.EqualTo("{}"));
        var watch = Stopwatch.StartNew();

        var ping = await client.NextRecordAsync(TimeSpan.FromSeconds(25));
        var elapsed = watch.Elapsed;
        Wire("server", $"{ping} after {elapsed.TotalSeconds:F1} s idle");

        Assert.That(ping, Is.EqualTo("""{"type":6}"""));
        Assert.That(elapsed, Is.GreaterThan(LiveHubLimits.KeepAliveInterval - TimeSpan.FromSeconds(2))
            .And.LessThan(LiveHubLimits.KeepAliveInterval + TimeSpan.FromSeconds(5)));
    }

    /// <summary>A SignalR JSON-protocol client over a bare WebSocket: no negotiate, records split on 0x1E.</summary>
    private sealed class RawHubClient : IAsyncDisposable
    {
        private readonly ClientWebSocket _socket;
        private readonly Queue<string> _records = new();

        private RawHubClient(ClientWebSocket socket) => _socket = socket;

        public int UpgradeStatus => (int)_socket.HttpStatusCode;
        public WebSocketCloseStatus? CloseStatus => _socket.CloseStatus;
        public string? CloseDescription => _socket.CloseStatusDescription;
        public WebSocketState State => _socket.State;

        /// <summary>True once a WebSocket close frame arrived (a clean close).</summary>
        public bool CloseFrameReceived { get; private set; }

        /// <summary>The exception that ended receiving, when the socket was reset or aborted instead of closed.</summary>
        public WebSocketException? ReceiveError { get; private set; }

        public static async Task<RawHubClient> ConnectAsync(Uri uri)
        {
            var socket = new ClientWebSocket();
            socket.Options.CollectHttpResponseDetails = true;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await socket.ConnectAsync(uri, timeout.Token);
            return new RawHubClient(socket);
        }

        public async Task SendAsync(string json)
        {
            Wire("client", json);
            var bytes = Encoding.UTF8.GetBytes(json + RecordSeparator);
            await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
        }

        public async Task<string?> HandshakeAsync()
        {
            await SendAsync("""{"protocol":"json","version":1}""");
            var reply = await NextRecordAsync();
            Wire("server handshake", reply ?? "(closed)");
            return reply;
        }

        public async Task<string?> InvokeAsync(string invocationId, string target, params string[] arguments)
        {
            var args = string.Join(",", arguments.Select(a => System.Text.Json.JsonSerializer.Serialize(a)));
            await SendAsync($$"""{"type":1,"invocationId":"{{invocationId}}","target":"{{target}}","arguments":[{{args}}]}""");
            var reply = await NextRecordAsync();
            Wire("server", reply ?? "(closed)");
            return reply;
        }

        /// <summary>The next non-ping record (pings are skipped unless <paramref name="wait"/> is given), or null once closed.</summary>
        public async Task<string?> NextRecordAsync(TimeSpan? wait = null)
        {
            var keepPings = wait is not null;
            using var timeout = new CancellationTokenSource(wait ?? TimeSpan.FromSeconds(10));
            while (true)
            {
                while (_records.Count > 0)
                {
                    var record = _records.Dequeue();
                    if (keepPings || record != """{"type":6}""") return record;
                }

                if (_socket.State != WebSocketState.Open) return null;
                var message = new StringBuilder();
                var buffer = new byte[4096];
                WebSocketReceiveResult result;
                do
                {
                    try
                    {
                        result = await _socket.ReceiveAsync(buffer, timeout.Token);
                    }
                    catch (WebSocketException e)
                    {
                        // The socket was reset or aborted without a close handshake: recorded, so
                        // a test that expects a clean close fails.
                        ReceiveError = e;
                        return null;
                    }

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        CloseFrameReceived = true;
                        return null;
                    }
                    message.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                } while (!result.EndOfMessage);

                foreach (var record in message.ToString().Split(RecordSeparator, StringSplitOptions.RemoveEmptyEntries))
                    _records.Enqueue(record);
            }
        }

        /// <summary>Closes (bounded: a hub that stops answering cannot hang the fixture), then always disposes.</summary>
        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_socket.State == WebSocketState.Open)
                {
                    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, deadline.Token);
                }
            }
            catch (Exception e) when (e is WebSocketException or OperationCanceledException)
            {
                // Already gone, or the close handshake did not finish in time: dispose regardless.
            }
            finally
            {
                _socket.Dispose();
            }
        }
    }
}
