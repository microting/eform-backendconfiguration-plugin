using System.Security.Claims;
using System.Text.Encodings.Web;
using BackendConfiguration.Pn.Hubs;
using BackendConfiguration.Pn.Services.GrpcServices;
using BackendConfiguration.Pn.Services.LiveNotifier;
using BackendConfiguration.Pn.Services.UserPropertyAccess;
using Grpc.Core;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// Package 11b: web calendar users on the live hub. A signed-in, active web
/// user (no SDK site) connects as a "web" connection in no site group and may
/// subscribe, through <c>JoinCalendarProperty</c>, to the hints of any
/// existing property, exactly as the calendar's REST endpoints let it load any
/// property's calendar. Workers keep their rules; <c>JoinProperty</c> is not
/// widened. The hub is called directly with fake caller contexts (as
/// LiveHubTests); the calendar rule runs against the MariaDB container.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class LiveHubCalendarTests : TestBaseSetup
{
    private const int WorkerUserId = 5;
    private const int WebUserId = 6;
    private const int InactiveUserId = 7;
    private const int UnknownUserId = 8;
    private const int WorkerSite = 9201;

    private static readonly DateTimeOffset Now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset TokenExpiry = Now.AddHours(1);

    private FakeTimeProvider _clock = null!;
    private LiveHubAuthenticator _authenticator = null!;
    private LiveConnectionRegistry _registry = null!;
    private IUserService _users = null!;
    private IGrpcSiteResolver _sites = null!;
    private readonly Dictionary<string, FakeCallerContext> _contexts = new();

    [SetUp]
    public void HubSetup()
    {
        _clock = new FakeTimeProvider(Now);
        _registry = new LiveConnectionRegistry(_clock);
        _contexts.Clear();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, FakeBearerHandler>(LiveHubAuthenticator.BearerScheme, _ => { });
        _authenticator = new LiveHubAuthenticator(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            _clock, NullLogger<LiveHubAuthenticator>.Instance);

        _users = Substitute.For<IUserService>();
        foreach (var id in new[] { WorkerUserId, WebUserId, InactiveUserId })
        {
            var userId = id;
            _users.GetByIdAsync(userId).Returns(_ => Task.FromResult(new EformUser
                { Id = userId, Email = $"u{userId}@example.test", IsActive = userId != InactiveUserId }));
        }

        _users.GetByIdAsync(UnknownUserId).Returns(Task.FromResult<EformUser>(null!));
        _sites = Substitute.For<IGrpcSiteResolver>();
        _sites.GetSdkSiteIdForUserAsync(Arg.Is<EformUser>(u => u.Id == WorkerUserId)).Returns(Task.FromResult(WorkerSite));
        _sites.GetSdkSiteIdForUserAsync(Arg.Is<EformUser>(u => u.Id == WebUserId)).Returns(Task.FromResult(0));
        _sites.GetSdkSiteIdForUserAsync(Arg.Is<EformUser>(u => u.Id == InactiveUserId))
            .ThrowsAsync(new RpcException(new Status(StatusCode.Unauthenticated, GrpcSiteResolver.AccountDisabledDetail)));
    }

    private BackendConfigurationHub Hub(string token, string connectionId)
    {
        var http = new DefaultHttpContext();
        var response = new StartedResponseFeature { Headers = new HeaderDictionary { IsReadOnly = true } };
        http.Features.Set<IHttpResponseFeature>(response);
        http.Request.QueryString = new QueryString("?access_token=" + token);
        var clients = Substitute.For<IHubCallerClients>();
        ((IHubCallerClients<IClientProxy>)clients).Caller.Returns(Substitute.For<IClientProxy>());
        var context = new FakeCallerContext(http, connectionId);
        _contexts[connectionId] = context;
        return new BackendConfigurationHub(_authenticator, new LiveCallerResolver(_users, _sites),
            new WorkerLivePropertyAccess(new BackendConfigurationUserPropertyAccess(BackendConfigurationPnDbContext!)),
            _registry, NullLogger<BackendConfigurationHub>.Instance,
            new CalendarLivePropertyAccess(BackendConfigurationPnDbContext!))
        {
            Context = context,
            Clients = clients,
        };
    }

    private async Task<BackendConfigurationHub> ConnectedAsync(string token, string connectionId)
    {
        var hub = Hub(token, connectionId);
        await hub.OnConnectedAsync();
        Assert.That(_contexts[connectionId].Aborted, Is.False, $"{token} is accepted");
        return hub;
    }

    private async Task<int> PropertyAsync(string workflowState = Constants.WorkflowStates.Created)
    {
        var property = new Property
        {
            Name = $"live-cal-{Guid.NewGuid():N}", ItemPlanningTagId = 0,
            WorkflowState = workflowState, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Properties.AddAsync(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        return property.Id;
    }

    private async Task AssignAsync(int siteId, int propertyId)
    {
        await BackendConfigurationPnDbContext!.PropertyWorkers.AddAsync(new PropertyWorker
        {
            PropertyId = propertyId, WorkerId = siteId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();
    }

    private static async Task<string> CodeOf(Task<JoinPropertyResult> join) => (await join).Code;

    private static string Id(int id) => id.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A notifier over this fixture's registry; every send is recorded as (ids, method).</summary>
    private (LiveNotifier Notifier, List<(IReadOnlyList<string> Ids, string Method)> Sent) Notifier()
    {
        var sent = new List<(IReadOnlyList<string>, string)>();
        var hubContext = Substitute.For<IHubContext<BackendConfigurationHub>>();
        var clients = Substitute.For<IHubClients>();
        hubContext.Clients.Returns(clients);
        clients.Clients(Arg.Any<IReadOnlyList<string>>()).Returns(ci =>
        {
            var ids = ci.Arg<IReadOnlyList<string>>().ToList();
            var proxy = Substitute.For<IClientProxy>();
            proxy.SendCoreAsync(Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>()).Returns(c =>
            {
                lock (sent) sent.Add((ids, c.ArgAt<string>(0)));
                return Task.CompletedTask;
            });
            return proxy;
        });
        return (new LiveNotifier(hubContext, _registry, NullLogger<LiveNotifier>.Instance), sent);
    }

    // ------------------------------------------------------------------
    // Connect: the two caller kinds
    // ------------------------------------------------------------------

    [Test]
    public async Task AWorker_ConnectsAsBefore_InItsSiteGroup()
    {
        await ConnectedAsync("worker-token", "w1");

        Assert.Multiple(() =>
        {
            Assert.That(_registry.SiteOf("w1"), Is.EqualTo(WorkerSite));
            Assert.That(_registry.IsIn("w1", LiveHubContract.SiteGroup(WorkerSite)), Is.True);
            Assert.That(_registry.CountGroups("w1", ""), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task AnActiveWebUser_IsAccepted_AsAWebConnection_InNoGroup()
    {
        await ConnectedAsync("web-token", "web1");

        Assert.Multiple(() =>
        {
            Assert.That(_registry.SiteOf("web1"), Is.EqualTo(0), "registered, without a site");
            Assert.That(_registry.CountGroups("web1", ""), Is.Zero);
            Assert.That(_registry.ConnectionsOf(WebUserId), Is.EqualTo(1));
        });
    }

    [TestCase("inactive-token", LiveHubContract.Errors.AccountDisabled)]
    [TestCase("unknown-token", LiveHubContract.Errors.NotAuthenticated)]
    public async Task AnInactiveOrUnknownUser_IsRefused(string token, string code)
    {
        var hub = Hub(token, "x");

        await hub.OnConnectedAsync();

        Assert.Multiple(() =>
        {
            Assert.That(_contexts["x"].Aborted, Is.True);
            Assert.That(_registry.SiteOf("x"), Is.Null);
        });
        var refused = ((IHubCallerClients<IClientProxy>)hub.Clients).Caller.ReceivedCalls().Single();
        Assert.That(((object?[])refused.GetArguments()[1]!).Single(), Is.EqualTo(new RefusedMessage { Code = code }));
    }

    [Test]
    public async Task ThePerUserConnectionCap_CoversWebUsers()
    {
        for (var i = 0; i < LiveHubLimits.MaxConnectionsPerUser; i++)
            await ConnectedAsync("web-token", $"web{i}");

        var extra = Hub("web-token", "extra");
        await extra.OnConnectedAsync();

        Assert.That(_contexts["extra"].Aborted, Is.True);
        Assert.That(_registry.ConnectionsOf(WebUserId), Is.EqualTo(LiveHubLimits.MaxConnectionsPerUser));
    }

    // ------------------------------------------------------------------
    // JoinCalendarProperty
    // ------------------------------------------------------------------

    [TestCase("web-token")]
    [TestCase("worker-token")]
    public async Task JoinCalendarProperty_AnExistingProperty_IsOk_ForBothKinds_WithoutAnAssignment(string token)
    {
        var property = await PropertyAsync();
        var hub = await ConnectedAsync(token, "c1");

        Assert.That(await CodeOf(hub.JoinCalendarProperty(Id(property))), Is.EqualTo(LiveHubContract.Results.Ok));
        Assert.That(_registry.IsIn("c1", LiveHubContract.PropertyGroup(property)), Is.True, "the publishers' own property group");
    }

    [TestCase("web-token")]
    [TestCase("worker-token")]
    public async Task JoinCalendarProperty_RemovedNonExistentAndMalformed_GetTheUniformRefusal(string token)
    {
        var removed = await PropertyAsync(Constants.WorkflowStates.Removed);
        var hub = await ConnectedAsync(token, "c1");

        var codes = new List<string>();
        foreach (var id in new[] { Id(removed), "987654321", "abc", "-1", "0", "", " 1" })
            codes.Add(await CodeOf(hub.JoinCalendarProperty(id)));

        Assert.That(codes, Is.All.EqualTo(LiveHubContract.Errors.PropertyNotAvailable));
        Assert.That(_registry.CountGroups("c1", LiveHubContract.PropertyGroupPrefix), Is.Zero);
    }

    [Test]
    public async Task AWebUsersJoinProperty_IsRefusedAsAnUnassignedCaller()
    {
        var property = await PropertyAsync();
        var hub = await ConnectedAsync("web-token", "web1");

        Assert.That(await CodeOf(hub.JoinProperty(Id(property))), Is.EqualTo(LiveHubContract.Errors.PropertyNotAvailable));
        Assert.That(_registry.CountGroups("web1", ""), Is.Zero);
    }

    [Test]
    public async Task JoinCalendarProperty_RechecksTheUser_AndEndsADeactivatedOnesConnection()
    {
        var property = await PropertyAsync();
        var hub = await ConnectedAsync("web-token", "web1");
        Assert.That(await CodeOf(hub.JoinCalendarProperty(Id(property))), Is.EqualTo(LiveHubContract.Results.Ok));
        _sites.GetSdkSiteIdForUserAsync(Arg.Is<EformUser>(u => u.Id == WebUserId))
            .ThrowsAsync(new RpcException(new Status(StatusCode.Unauthenticated, GrpcSiteResolver.AccountDisabledDetail)));

        Assert.That(await CodeOf(hub.JoinCalendarProperty(Id(property))), Is.EqualTo(LiveHubContract.Errors.AccountDisabled));
        Assert.That(_contexts["web1"].Aborted, Is.True);
        Assert.That(_registry.SiteOf("web1"), Is.Null, "its earlier memberships end with it");
    }

    [Test]
    public async Task JoinCalendarProperty_AfterTheTokenExpired_IsRefused_AndTheConnectionAborted()
    {
        var property = await PropertyAsync();
        var hub = await ConnectedAsync("web-token", "web1");
        _clock.SetUtcNow(TokenExpiry + LiveHubAuthenticator.ClockSkew);

        Assert.That(await CodeOf(hub.JoinCalendarProperty(Id(property))), Is.EqualTo(LiveHubContract.Errors.NotAuthenticated));
        Assert.That(_contexts["web1"].Aborted, Is.True);
    }

    [Test]
    public async Task JoinCalendarProperty_RacingARemovalOfTheProperty_IsUndone()
    {
        var property = await PropertyAsync();
        var hub = await ConnectedAsync("web-token", "web1");
        var calendar = Substitute.For<ILiveCalendarPropertyAccess>();
        calendar.CanJoinCalendarPropertyAsync(Arg.Any<LiveCaller>(), property).Returns(_ =>
        {
            // The property is removed (and revoked for everyone) after the check said yes.
            _registry.RevokePropertyForEveryone(property, LiveHubContract.PropertyGroup(property));
            return Task.FromResult(true);
        });
        var racing = new BackendConfigurationHub(_authenticator, new LiveCallerResolver(_users, _sites),
            new WorkerLivePropertyAccess(new BackendConfigurationUserPropertyAccess(BackendConfigurationPnDbContext!)),
            _registry, NullLogger<BackendConfigurationHub>.Instance, calendar)
        {
            Context = _contexts["web1"],
            Clients = hub.Clients,
        };

        Assert.That(await CodeOf(racing.JoinCalendarProperty(Id(property))), Is.EqualTo(LiveHubContract.Errors.PropertyNotAvailable));
        Assert.That(_registry.IsIn("web1", LiveHubContract.PropertyGroup(property)), Is.False);
    }

    [Test]
    public async Task LeaveCalendarProperty_IsIdempotent_AndNeverThrows()
    {
        var property = await PropertyAsync();
        var hub = await ConnectedAsync("web-token", "web1");
        Assert.That(await CodeOf(hub.JoinCalendarProperty(Id(property))), Is.EqualTo(LiveHubContract.Results.Ok));

        await hub.LeaveCalendarProperty(Id(property));
        await hub.LeaveCalendarProperty(Id(property));
        await Assert.DoesNotThrowAsync(() => hub.LeaveCalendarProperty("abc"));

        Assert.That(_registry.IsIn("web1", LiveHubContract.PropertyGroup(property)), Is.False);
    }

    // ------------------------------------------------------------------
    // Hints and revocation
    // ------------------------------------------------------------------

    [Test]
    public async Task Hints_ReachACalendarMember_AndAssignmentsChangedNeverReachesAWebConnection()
    {
        var property = await PropertyAsync();
        await AssignAsync(WorkerSite, property);
        var web = await ConnectedAsync("web-token", "web1");
        var worker = await ConnectedAsync("worker-token", "w1");
        Assert.That(await CodeOf(web.JoinCalendarProperty(Id(property))), Is.EqualTo(LiveHubContract.Results.Ok));
        Assert.That(await CodeOf(worker.JoinProperty(Id(property))), Is.EqualTo(LiveHubContract.Results.Ok));
        var (notifier, sent) = Notifier();

        await notifier.TaskChangedAsync(property, eventId: 3);
        await notifier.AssignmentsChangedAsync(WorkerSite);
        await notifier.AssignmentsChangedAsync(0);

        Assert.That(sent.Single(s => s.Method == LiveHubContract.ServerMethods.TaskChanged).Ids,
            Is.EquivalentTo(new[] { "web1", "w1" }));
        Assert.That(sent.Where(s => s.Method == LiveHubContract.ServerMethods.AssignmentsChanged).SelectMany(s => s.Ids),
            Is.EqualTo(new[] { "w1" }));
    }

    [Test]
    public async Task RemovingAProperty_RevokesEveryone_WorkersAndWebUsers()
    {
        var property = await PropertyAsync();
        var other = await PropertyAsync();
        await AssignAsync(WorkerSite, property);
        var web = await ConnectedAsync("web-token", "web1");
        var worker = await ConnectedAsync("worker-token", "w1");
        Assert.That(await CodeOf(web.JoinCalendarProperty(Id(property))), Is.EqualTo(LiveHubContract.Results.Ok));
        Assert.That(await CodeOf(web.JoinCalendarProperty(Id(other))), Is.EqualTo(LiveHubContract.Results.Ok));
        Assert.That(await CodeOf(worker.JoinProperty(Id(property))), Is.EqualTo(LiveHubContract.Results.Ok));
        var (notifier, sent) = Notifier();

        await notifier.RevokePropertyForEveryoneAsync(property);
        await notifier.TaskChangedAsync(property);
        await notifier.TaskChangedAsync(other);

        Assert.Multiple(() =>
        {
            Assert.That(_registry.EligibleMembers(LiveHubContract.PropertyGroup(property)), Is.Empty);
            Assert.That(_registry.IsIn("web1", LiveHubContract.PropertyGroup(other)), Is.True, "other properties stay");
            Assert.That(_registry.IsIn("w1", LiveHubContract.SiteGroup(WorkerSite)), Is.True, "connections stay open");
            Assert.That(_contexts["web1"].Aborted || _contexts["w1"].Aborted, Is.False);
            Assert.That(sent.Where(s => s.Method == LiveHubContract.ServerMethods.TaskChanged).SelectMany(s => s.Ids),
                Is.EqualTo(new[] { "web1" }), "only the hint for the other property");
        });
    }

    [Test]
    public async Task AWorkersUnassignment_RevokesItsMembership_EvenOneMadeThroughTheCalendar()
    {
        // One membership per (connection, property group): the unassignment takes it,
        // whichever method joined it; a calendar client may join again.
        var property = await PropertyAsync();
        await AssignAsync(WorkerSite, property);
        var worker = await ConnectedAsync("worker-token", "w1");
        Assert.That(await CodeOf(worker.JoinCalendarProperty(Id(property))), Is.EqualTo(LiveHubContract.Results.Ok));
        var (notifier, _) = Notifier();

        await notifier.RevokePropertyAccessAsync(WorkerSite, property);

        Assert.That(_registry.IsIn("w1", LiveHubContract.PropertyGroup(property)), Is.False);
        Assert.That(await CodeOf(worker.JoinCalendarProperty(Id(property))), Is.EqualTo(LiveHubContract.Results.Ok),
            "the calendar rule lets it join again");
    }

    [Test]
    public async Task RevokingAUser_EndsItsWebConnections_Only()
    {
        var property = await PropertyAsync();
        var web = await ConnectedAsync("web-token", "web1");
        await ConnectedAsync("worker-token", "w1");
        Assert.That(await CodeOf(web.JoinCalendarProperty(Id(property))), Is.EqualTo(LiveHubContract.Results.Ok));
        var (notifier, sent) = Notifier();

        await notifier.RevokeUserAsync(WebUserId);
        await notifier.TaskChangedAsync(property);

        Assert.Multiple(() =>
        {
            Assert.That(_contexts["web1"].Aborted, Is.True);
            Assert.That(_contexts["w1"].Aborted, Is.False);
            Assert.That(_registry.ConnectionsOf(WebUserId), Is.Zero);
            Assert.That(sent.Where(s => s.Method == LiveHubContract.ServerMethods.Refused).SelectMany(s => s.Ids),
                Is.EqualTo(new[] { "web1" }));
            Assert.That(sent.Where(s => s.Method == LiveHubContract.ServerMethods.TaskChanged), Is.Empty);
        });
    }

    [Test]
    public async Task TheExpirySweep_CoversWebConnections()
    {
        var property = await PropertyAsync();
        var web = await ConnectedAsync("web-token", "web1");
        Assert.That(await CodeOf(web.JoinCalendarProperty(Id(property))), Is.EqualTo(LiveHubContract.Results.Ok));
        var (notifier, sent) = Notifier();
        _clock.SetUtcNow(TokenExpiry + LiveHubAuthenticator.ClockSkew);

        await notifier.TaskChangedAsync(property);

        Assert.That(_contexts["web1"].Aborted, Is.True);
        Assert.That(sent.Where(s => s.Method == LiveHubContract.ServerMethods.TaskChanged), Is.Empty);
    }

    // ------------------------------------------------------------------
    // Fakes (as LiveHubTests)
    // ------------------------------------------------------------------

    private static Claim Exp(DateTimeOffset at) =>
        new("exp", at.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));

    private sealed class FakeCallerContext : HubCallerContext
    {
        private readonly FeatureCollection _features = new();
        private readonly string _connectionId;

        public FakeCallerContext(HttpContext http, string connectionId)
        {
            _connectionId = connectionId;
            _features.Set<IHttpContextFeature>(new HttpContextFeature { HttpContext = http });
        }

        public override string ConnectionId => _connectionId;
        public override string? UserIdentifier => null;
        public override ClaimsPrincipal? User { get; } = new(new ClaimsIdentity());
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features => _features;
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public bool Aborted { get; private set; }
        public override void Abort() => Aborted = true;
    }

    private sealed class HttpContextFeature : IHttpContextFeature
    {
        public HttpContext? HttpContext { get; set; }
    }

    private sealed class StartedResponseFeature : HttpResponseFeature
    {
        public override bool HasStarted => true;
    }

    /// <summary>Stands in for the host's JwtBearer: one token per test user, all with the host's claims.</summary>
    private sealed class FakeBearerHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        private static Claim[] For(int userId) =>
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new Claim("updated_at", "638000000000000000"), Exp(TokenExpiry)
        ];

        private static readonly Dictionary<string, Claim[]> Tokens = new()
        {
            ["worker-token"] = For(WorkerUserId),
            ["web-token"] = For(WebUserId),
            ["inactive-token"] = For(InactiveUserId),
            ["unknown-token"] = For(UnknownUserId),
        };

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers.Authorization.ToString();
            if (!header.StartsWith("Bearer ") || !Tokens.TryGetValue(header["Bearer ".Length..], out var claims))
                return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity(claims, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
