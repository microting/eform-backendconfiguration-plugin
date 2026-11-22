using System.Security.Claims;
using System.Text.Encodings.Web;
using BackendConfiguration.Pn.Hubs;
using BackendConfiguration.Pn.Services.GrpcServices;
using BackendConfiguration.Pn.Services.UserPropertyAccess;
using Grpc.Core;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
/// The live hub, called directly with a fake caller context (kanban's
/// KanbanHubTests pattern; no in-memory server). The property-access rule is
/// the real one the gRPC Events calls use, against the MariaDB container;
/// the token and the user/site lookup are faked at their seams.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class LiveHubTests : TestBaseSetup
{
    private const int UserId = 5;
    private const int WorkerSite = 9101;

    /// <summary>The fake clock's start; the fake tokens' expiries are fixed relative to it.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset GoodTokenExpiry = Now.AddHours(1);

    /// <summary>The hub's ClockSkew (JwtBearer's default, which the host does not override).</summary>
    private static readonly TimeSpan TestSkew = LiveHubAuthenticator.ClockSkew;

    private FakeTimeProvider _clock = null!;
    private LiveHubAuthenticator _authenticator = null!;
    private LiveConnectionRegistry _registry = null!;
    private IClientProxy _caller = null!;
    private FakeCallerContext _context = null!;
    private IUserService _users = null!;
    private IGrpcSiteResolver _sites = null!;
    private int _siteOfUser;

    [SetUp]
    public void HubSetup()
    {
        _clock = new FakeTimeProvider(Now);
        _registry = new LiveConnectionRegistry(_clock);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IClaimsTransformation, ThrowingClaimsTransformer>();
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, FakeBearerHandler>(LiveHubAuthenticator.BearerScheme, _ => { });
        _authenticator = new LiveHubAuthenticator(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            _clock, Microsoft.Extensions.Logging.Abstractions.NullLogger<LiveHubAuthenticator>.Instance);

        _siteOfUser = WorkerSite;
        _users = Substitute.For<IUserService>();
        _users.GetByIdAsync(UserId).Returns(_ => Task.FromResult(new EformUser { Id = UserId, Email = "w@example.test", IsActive = true }));
        _sites = Substitute.For<IGrpcSiteResolver>();
        _sites.GetSdkSiteIdForUserAsync(Arg.Any<EformUser>()).Returns(_ => Task.FromResult(_siteOfUser));
    }

    /// <summary>A hub for one connection; its caller proxy and context are left in <see cref="_caller"/> / <see cref="_context"/>.</summary>
    private (BackendConfigurationHub Hub, IGroupManager Groups) Hub(string? queryToken = "good-token", string connectionId = "c1",
        ClaimsPrincipal? headerUser = null, ILivePropertyAccess? access = null)
    {
        var http = StartedHubRequest();
        if (queryToken is not null) http.Request.QueryString = new QueryString("?access_token=" + queryToken);
        var groups = Substitute.For<IGroupManager>();
        var clients = Substitute.For<IHubCallerClients>();
        _caller = Substitute.For<IClientProxy>();
        ((IHubCallerClients<IClientProxy>)clients).Caller.Returns(_caller);
        _context = new FakeCallerContext(headerUser ?? new ClaimsPrincipal(new ClaimsIdentity()), http, connectionId);
        access ??= new WorkerLivePropertyAccess(new BackendConfigurationUserPropertyAccess(BackendConfigurationPnDbContext!));
        var hub = new BackendConfigurationHub(_authenticator, new LiveCallerResolver(_users, _sites), access, _registry,
            Substitute.For<ILogger<BackendConfigurationHub>>())
        {
            Context = _context,
            Groups = groups,
            Clients = clients,
        };
        return (hub, groups);
    }

    /// <summary>
    /// A refused connect does not throw (no Error log from SignalR): the
    /// caller gets <c>Refused {code}</c>, the connection is aborted, and it
    /// holds no registry slot.
    /// </summary>
    private async Task AssertConnectRefusedAsync(BackendConfigurationHub hub, string code)
    {
        await hub.OnConnectedAsync();

        var refused = _caller.ReceivedCalls().Single();
        Assert.Multiple(() =>
        {
            Assert.That(refused.GetArguments()[0], Is.EqualTo(LiveHubContract.ServerMethods.Refused));
            Assert.That(((object?[])refused.GetArguments()[1]!).Single(), Is.EqualTo(new RefusedMessage { Code = code }));
            Assert.That(_context.Aborted, Is.True);
            Assert.That(_context.Items.ContainsKey(BackendConfigurationHub.UserIdKey), Is.False);
            Assert.That(_registry.SiteOf(_context.ConnectionId), Is.Null);
        });
    }

    private async Task<int> PropertyAsync()
    {
        var property = new Property
        {
            Name = $"live-{Guid.NewGuid():N}", ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Properties.AddAsync(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        return property.Id;
    }

    private async Task AssignAsync(int siteId, int propertyId, string workflowState = Constants.WorkflowStates.Created)
    {
        await BackendConfigurationPnDbContext!.PropertyWorkers.AddAsync(new PropertyWorker
        {
            PropertyId = propertyId, WorkerId = siteId,
            WorkflowState = workflowState, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();
    }

    /// <summary>JoinProperty answers with a result code; it never throws for a refusal.</summary>
    private static async Task<string> CodeOf(Task<JoinPropertyResult> join)
    {
        var result = await join;
        Assert.That(result.V, Is.EqualTo(LiveHubContract.Version));
        return result.Code;
    }

    private static string Id(int id) => id.ToString(System.Globalization.CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------
    // Connect
    // ------------------------------------------------------------------

    [TestCase("")]
    [TestCase("bad-token")]
    [TestCase("no-timestamp-token")]
    [TestCase("non-numeric-user-id-token")]
    [TestCase("expired-token")]
    public async Task Connect_WithoutAValidToken_IsRefused_AndJoinsNothing(string token)
    {
        var (hub, groups) = Hub(queryToken: token);

        await AssertConnectRefusedAsync(hub, LiveHubContract.Errors.NotAuthenticated);
        await groups.DidNotReceiveWithAnyArgs().AddToGroupAsync(default!, default!);
        await _users.DidNotReceiveWithAnyArgs().GetByIdAsync(default);
    }

    /// <summary>
    /// A correctly signed token is refused once its expiry plus the bearer
    /// scheme's ClockSkew has passed (exactly the host's own rule).
    /// </summary>
    [Test]
    public async Task Connect_ExpiredButCorrectlySignedToken_IsRefusedPastTheClockSkew()
    {
        _clock.SetUtcNow(GoodTokenExpiry + TestSkew);
        var (hub, groups) = Hub(queryToken: "good-token");

        await AssertConnectRefusedAsync(hub, LiveHubContract.Errors.NotAuthenticated);
        await groups.DidNotReceiveWithAnyArgs().AddToGroupAsync(default!, default!);
    }

    /// <summary>
    /// A token that expired less than ClockSkew ago is still accepted by the
    /// host's gRPC and REST calls, so the hub accepts it too.
    /// </summary>
    [Test]
    public async Task Connect_TokenExpiredWithinTheClockSkew_IsAccepted()
    {
        _clock.SetUtcNow(GoodTokenExpiry + TestSkew - TimeSpan.FromSeconds(1));
        var (hub, groups) = Hub(queryToken: "good-token");

        await hub.OnConnectedAsync();

        Assert.That(_context.Aborted, Is.False);
        await groups.Received(1).AddToGroupAsync("c1", LiveHubContract.SiteGroup(WorkerSite), Arg.Any<CancellationToken>());
    }

    /// <summary>The publish sweep ends a connection at the same instant: expiry plus ClockSkew.</summary>
    [Test]
    public async Task Sweep_EndsTheConnectionAtExpiryPlusTheClockSkew()
    {
        var (hub, _) = Hub();
        await hub.OnConnectedAsync();

        _clock.SetUtcNow(GoodTokenExpiry + TestSkew - TimeSpan.FromSeconds(1));
        Assert.That(_registry.TakeExpired(), Is.Empty);
        _clock.SetUtcNow(GoodTokenExpiry + TestSkew);
        Assert.That(_registry.TakeExpired().Select(t => t.ConnectionId), Is.EqualTo(new[] { "c1" }));
    }

    [Test]
    public async Task Connect_BeyondTheConnectionsPerUser_IsRefusedBeforeAnyDatabaseLookup_AndADisconnectFreesASlot()
    {
        for (var i = 0; i < LiveHubLimits.MaxConnectionsPerUser; i++)
            await Hub(connectionId: $"c{i}").Hub.OnConnectedAsync();
        _users.ClearReceivedCalls();

        var (extra, _) = Hub(connectionId: "extra");
        await AssertConnectRefusedAsync(extra, LiveHubContract.Errors.TooManyConnections);
        await _users.DidNotReceiveWithAnyArgs().GetByIdAsync(default);
        Assert.That(_registry.ConnectionsOf(UserId), Is.EqualTo(LiveHubLimits.MaxConnectionsPerUser));

        await Hub(connectionId: "c0").Hub.OnDisconnectedAsync(null);
        var (again, groups) = Hub(connectionId: "again");
        await again.OnConnectedAsync();
        await groups.Received(1).AddToGroupAsync("again", LiveHubContract.SiteGroup(WorkerSite), Arg.Any<CancellationToken>());
        Assert.That(_context.Aborted, Is.False);
    }

    [Test]
    public async Task Connect_TokenOfAUserThatNoLongerExists_IsRefused()
    {
        _users.GetByIdAsync(UserId).Returns(Task.FromResult<EformUser>(null!));
        var (hub, _) = Hub();

        await AssertConnectRefusedAsync(hub, LiveHubContract.Errors.NotAuthenticated);
        Assert.That(_registry.ConnectionsOf(UserId), Is.Zero, "a refused connect frees its slot");
    }

    [Test]
    public async Task Connect_DisabledOrResignedAccount_IsRefusedLikeTheGrpcCalls()
    {
        _sites.GetSdkSiteIdForUserAsync(Arg.Any<EformUser>())
            .ThrowsAsync(new RpcException(new Status(StatusCode.Unauthenticated, GrpcSiteResolver.AccountDisabledDetail)));
        var (hub, groups) = Hub();

        await AssertConnectRefusedAsync(hub, LiveHubContract.Errors.AccountDisabled);
        await groups.DidNotReceiveWithAnyArgs().AddToGroupAsync(default!, default!);
        Assert.That(_registry.ConnectionsOf(UserId), Is.Zero);
    }

    [Test]
    public async Task Connect_AuthenticatedWorker_IsAutoJoinedToItsOwnSiteGroupOnly()
    {
        var (hub, groups) = Hub();

        await hub.OnConnectedAsync();

        await groups.Received(1).AddToGroupAsync("c1", LiveHubContract.SiteGroup(WorkerSite), Arg.Any<CancellationToken>());
        await groups.ReceivedWithAnyArgs(1).AddToGroupAsync(default!, default!);
        await _users.Received(1).GetByIdAsync(UserId);
        Assert.That(_registry.SiteOf("c1"), Is.EqualTo(WorkerSite));
        Assert.That(_caller.ReceivedCalls(), Is.Empty);
        Assert.That(_context.Aborted, Is.False);
    }

    [Test]
    public async Task Connect_HeaderAuthenticatedUser_IsAccepted()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "5"), new Claim("updated_at", "638000000000000000"), Exp(GoodTokenExpiry)], "Bearer"));
        var (hub, groups) = Hub(queryToken: null, headerUser: user);

        await hub.OnConnectedAsync();

        await groups.Received(1).AddToGroupAsync("c1", LiveHubContract.SiteGroup(WorkerSite), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Connect_NonWorker_IsConnectedWithoutASiteGroup_AndRefusedEveryProperty()
    {
        _siteOfUser = 0;
        var property = await PropertyAsync();
        await AssignAsync(0, property); // even a stray row for "site 0" grants nothing
        var (hub, groups) = Hub();

        await hub.OnConnectedAsync();

        await groups.DidNotReceiveWithAnyArgs().AddToGroupAsync(default!, default!);
        Assert.That(await CodeOf(hub.JoinProperty(Id(property))),
            Is.EqualTo(LiveHubContract.Errors.PropertyNotAvailable));
    }

    [Test]
    public async Task Disconnect_ForgetsTheConnection()
    {
        var (hub, _) = Hub();
        await hub.OnConnectedAsync();

        await hub.OnDisconnectedAsync(null);

        Assert.That(_registry.SiteOf("c1"), Is.Null);
        Assert.That(_registry.HasMembers(LiveHubContract.SiteGroup(WorkerSite)), Is.False);
    }

    // ------------------------------------------------------------------
    // JoinProperty / LeaveProperty
    // ------------------------------------------------------------------

    [Test]
    public async Task JoinProperty_Assigned_JoinsThePropertyGroup()
    {
        var property = await PropertyAsync();
        await AssignAsync(WorkerSite, property);
        var (hub, groups) = Hub();
        await hub.OnConnectedAsync();

        Assert.That(await CodeOf(hub.JoinProperty(Id(property))), Is.EqualTo(LiveHubContract.Results.Ok));

        await groups.Received(1).AddToGroupAsync("c1", LiveHubContract.PropertyGroup(property), Arg.Any<CancellationToken>());
        Assert.That(_registry.IsIn("c1", LiveHubContract.PropertyGroup(property)), Is.True);
    }

    [Test]
    public async Task JoinProperty_UnassignedNonExistentMalformedAndSoftDeleted_GetTheSameRefusal()
    {
        var unassigned = await PropertyAsync();
        await AssignAsync(WorkerSite + 1, unassigned); // another worker's property
        var softDeleted = await PropertyAsync();
        await AssignAsync(WorkerSite, softDeleted, Constants.WorkflowStates.Removed);
        var (hub, groups) = Hub();
        await hub.OnConnectedAsync();
        groups.ClearReceivedCalls();

        var refusals = new List<string?>();
        foreach (var id in new[] { Id(unassigned), "987654321", Id(softDeleted), "abc", "-1", "0", "", " 1" })
            refusals.Add(await CodeOf(hub.JoinProperty(id)));

        Assert.That(refusals, Is.All.EqualTo(LiveHubContract.Errors.PropertyNotAvailable));
        await groups.DidNotReceiveWithAnyArgs().AddToGroupAsync(default!, default!);
    }

    [Test]
    public async Task JoinProperty_WithoutAConnectedCaller_IsNotAuthenticated()
    {
        var (hub, _) = Hub();

        Assert.That(await CodeOf(hub.JoinProperty("1")), Is.EqualTo(LiveHubContract.Errors.NotAuthenticated));
    }

    [Test]
    public async Task JoinProperty_CallerResignedSinceConnect_IsRefused()
    {
        var property = await PropertyAsync();
        await AssignAsync(WorkerSite, property);
        var (hub, groups) = Hub();
        await hub.OnConnectedAsync();
        groups.ClearReceivedCalls();
        _sites.GetSdkSiteIdForUserAsync(Arg.Any<EformUser>())
            .ThrowsAsync(new RpcException(new Status(StatusCode.Unauthenticated, GrpcSiteResolver.AccountDisabledDetail)));

        Assert.That(await CodeOf(hub.JoinProperty(Id(property))), Is.EqualTo(LiveHubContract.Errors.AccountDisabled));
        await groups.DidNotReceiveWithAnyArgs().AddToGroupAsync(default!, default!);
    }

    [Test]
    public async Task JoinProperty_RevokedWhileJoining_IsUndone()
    {
        var property = await PropertyAsync();
        await AssignAsync(WorkerSite, property);
        var access = Substitute.For<ILivePropertyAccess>();
        // The assignment is removed (and revoked) after the check said yes.
        access.CanJoinPropertyAsync(Arg.Any<LiveCaller>(), property).Returns(_ =>
        {
            _registry.Revoke(WorkerSite, property, LiveHubContract.PropertyGroup(property));
            return Task.FromResult(true);
        });
        var (hub, groups) = Hub(access: access);
        await hub.OnConnectedAsync();

        Assert.That(await CodeOf(hub.JoinProperty(Id(property))), Is.EqualTo(LiveHubContract.Errors.PropertyNotAvailable));
        await groups.Received(1).RemoveFromGroupAsync("c1", LiveHubContract.PropertyGroup(property), Arg.Any<CancellationToken>());
        Assert.That(_registry.IsIn("c1", LiveHubContract.PropertyGroup(property)), Is.False);
    }

    [Test]
    public async Task JoinProperty_BeyondTheLimit_IsRefused_ButRejoiningAJoinedPropertyIsNot()
    {
        var property = await PropertyAsync();
        await AssignAsync(WorkerSite, property);
        var (hub, _) = Hub();
        await hub.OnConnectedAsync();
        Assert.That(await CodeOf(hub.JoinProperty(Id(property))), Is.EqualTo(LiveHubContract.Results.Ok));
        for (var i = 0; _registry.CountGroups("c1", LiveHubContract.PropertyGroupPrefix) < LiveHubLimits.MaxPropertyGroupsPerConnection; i++)
            _registry.Joined("c1", LiveHubContract.PropertyGroup(1_000_000 + i));

        Assert.That(await CodeOf(hub.JoinProperty(Id(property))), Is.EqualTo(LiveHubContract.Results.Ok)); // already joined: fine

        var other = await PropertyAsync();
        await AssignAsync(WorkerSite, other);
        Assert.That(await CodeOf(hub.JoinProperty(Id(other))), Is.EqualTo(LiveHubContract.Errors.TooManyProperties));
    }

    [Test]
    public async Task JoinProperty_AfterTheTokenExpired_IsRefused_AndTheConnectionAborted()
    {
        var property = await PropertyAsync();
        await AssignAsync(WorkerSite, property);
        var (hub, groups) = Hub();
        await hub.OnConnectedAsync();
        groups.ClearReceivedCalls();
        _clock.SetUtcNow(GoodTokenExpiry + TestSkew - TimeSpan.FromSeconds(1));
        Assert.That(await CodeOf(hub.JoinProperty(Id(property))), Is.EqualTo(LiveHubContract.Results.Ok), "within the skew");
        Assert.That(_context.Aborted, Is.False);
        await hub.LeaveProperty(Id(property));
        groups.ClearReceivedCalls();
        _clock.SetUtcNow(GoodTokenExpiry + TestSkew);

        Assert.That(await CodeOf(hub.JoinProperty(Id(property))), Is.EqualTo(LiveHubContract.Errors.NotAuthenticated));
        Assert.That(_context.Aborted, Is.True);
        await groups.DidNotReceiveWithAnyArgs().AddToGroupAsync(default!, default!);
    }

    [Test]
    public async Task LeaveProperty_IsIdempotent_AndNeverThrows()
    {
        var property = await PropertyAsync();
        await AssignAsync(WorkerSite, property);
        var (hub, groups) = Hub();
        await hub.OnConnectedAsync();
        Assert.That(await CodeOf(hub.JoinProperty(Id(property))), Is.EqualTo(LiveHubContract.Results.Ok));

        await hub.LeaveProperty(Id(property));
        await hub.LeaveProperty(Id(property));
        await Assert.DoesNotThrowAsync(() => hub.LeaveProperty("abc"));
        await Assert.DoesNotThrowAsync(() => hub.LeaveProperty("987654321"));

        await groups.Received(2).RemoveFromGroupAsync("c1", LiveHubContract.PropertyGroup(property), Arg.Any<CancellationToken>());
        Assert.That(_registry.IsIn("c1", LiveHubContract.PropertyGroup(property)), Is.False);
    }

    // ------------------------------------------------------------------
    // Fakes (kanban's KanbanHubTests)
    // ------------------------------------------------------------------

    private static Claim Exp(DateTimeOffset at) =>
        new("exp", at.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>A WebSocket/SSE request: its response has started, so its headers are read-only.</summary>
    private static DefaultHttpContext StartedHubRequest()
    {
        var http = new DefaultHttpContext();
        var response = new StartedResponseFeature();
        response.Headers = new HeaderDictionary { IsReadOnly = true };
        http.Features.Set<IHttpResponseFeature>(response);
        return http;
    }

    private sealed class FakeCallerContext : HubCallerContext
    {
        private readonly ClaimsPrincipal _user;
        private readonly FeatureCollection _features = new();
        private readonly string _connectionId;

        public FakeCallerContext(ClaimsPrincipal user, HttpContext http, string connectionId)
        {
            _user = user;
            _connectionId = connectionId;
            _features.Set<IHttpContextFeature>(new HttpContextFeature { HttpContext = http });
        }

        public override string ConnectionId => _connectionId;
        public override string? UserIdentifier => null;
        public override ClaimsPrincipal? User => _user;
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

    /// <summary>Stands in for the host's JwtBearer: "good-token" signs in user 5 with the host's claims.</summary>
    private sealed class FakeBearerHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        private static readonly Dictionary<string, Claim[]> Tokens = new()
        {
            ["good-token"] = [new Claim(ClaimTypes.NameIdentifier, "5"), new Claim("updated_at", "638000000000000000"), Exp(GoodTokenExpiry)],
            ["non-numeric-user-id-token"] = [new Claim(ClaimTypes.NameIdentifier, "five"), new Claim("updated_at", "638000000000000000"), Exp(GoodTokenExpiry)],
            ["no-timestamp-token"] = [new Claim(ClaimTypes.NameIdentifier, "5"), Exp(GoodTokenExpiry)],
            // Signed, but expired more than the skew before the test clock's start.
            ["expired-token"] = [new Claim(ClaimTypes.NameIdentifier, "5"), new Claim("updated_at", "638000000000000000"), Exp(Now - TestSkew - TimeSpan.FromSeconds(1))],
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

    /// <summary>The host's transformer writes to the started hub response; the authenticator must not run it.</summary>
    private sealed class ThrowingClaimsTransformer : IClaimsTransformation
    {
        public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal) =>
            throw new InvalidOperationException("The claims transformation must not run on a hub connection.");
    }
}
