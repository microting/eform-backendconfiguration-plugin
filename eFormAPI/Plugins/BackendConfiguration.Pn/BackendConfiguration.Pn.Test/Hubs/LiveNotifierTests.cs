#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Hubs;
using BackendConfiguration.Pn.Services.LiveNotifier;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Test.Hubs;

/// <summary>
/// The publisher side of the live hub contract (docs/live-updates-hub-contract.md):
/// method names, payload JSON, target group, and that it never throws.
/// </summary>
[TestFixture]
public class LiveNotifierTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    private ManualClock _clock = null!;
    private LiveConnectionRegistry _registry = null!;
    private IHubContext<BackendConfigurationHub> _hub = null!;
    private IHubClients _clients = null!;
    private IClientProxy _groupBroadcast = null!;
    private LiveNotifier _notifier = null!;
    private readonly List<string> _aborted = [];
    private readonly List<Sent> _sent = [];
    private Exception? _sendFailure;

    /// <summary>When set, a <c>Refused</c> send (the end of a connection) waits for it: a paused tear-down.</summary>
    private TaskCompletionSource? _refusedGate;

    /// <summary>One send through <c>Clients.Clients(ids)</c>: who, what, and the single payload.</summary>
    private sealed record Sent(IReadOnlyList<string> Ids, string Method, object? Payload);

    [SetUp]
    public void SetUp()
    {
        _clock = new ManualClock { UtcNow = Now };
        _registry = new LiveConnectionRegistry(_clock);
        _hub = Substitute.For<IHubContext<BackendConfigurationHub>>();
        _clients = Substitute.For<IHubClients>();
        _groupBroadcast = Substitute.For<IClientProxy>();
        _hub.Clients.Returns(_clients);
        _clients.Group(Arg.Any<string>()).Returns(_groupBroadcast);
        _clients.Clients(Arg.Any<IReadOnlyList<string>>()).Returns(ci => Recorder(ci.Arg<IReadOnlyList<string>>().ToList()));
        _aborted.Clear();
        _sent.Clear();
        _sendFailure = null;
        _refusedGate = null;
        _notifier = new LiveNotifier(_hub, _registry, NullLogger<LiveNotifier>.Instance);
    }

    private IClientProxy Recorder(IReadOnlyList<string> ids)
    {
        var proxy = Substitute.For<IClientProxy>();
        proxy.SendCoreAsync(Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>()).Returns(ci =>
        {
            if (_sendFailure is not null) throw _sendFailure;
            var method = ci.ArgAt<string>(0);
            lock (_sent) _sent.Add(new Sent(ids, method, ci.ArgAt<object?[]>(1).Single()));
            return method == LiveHubContract.ServerMethods.Refused && _refusedGate is not null
                ? _refusedGate.Task
                : Task.CompletedTask;
        });
        return proxy;
    }

    private void Member(string connectionId, int siteId, string group, DateTimeOffset? expiresAt = null, int? userId = null)
    {
        Assert.That(_registry.TryConnect(connectionId, userId ?? siteId, expiresAt ?? Now.AddHours(1),
            () => _aborted.Add(connectionId), LiveHubLimits.MaxConnectionsPerUser), Is.True);
        _registry.SetSite(connectionId, siteId);
        _registry.Joined(connectionId, group);
    }

    /// <summary>The payload as SignalR's JSON protocol writes it (System.Text.Json, web defaults).</summary>
    private static string Json(object? payload) =>
        JsonSerializer.Serialize(payload, payload!.GetType(), new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private List<Sent> SentOf(string method)
    {
        lock (_sent) return _sent.Where(x => x.Method == method).ToList();
    }

    /// <summary>Every connection id a hint of <paramref name="method"/> was sent to.</summary>
    private List<string> RecipientsOf(string method) => SentOf(method).SelectMany(x => x.Ids).ToList();

    [Test]
    public async Task TaskChanged_FullKey_GoesToThePropertyGroupsMembersOnly_WithTheDocumentedPayload()
    {
        Member("c1", 11, LiveHubContract.PropertyGroup(42));
        Member("c2", 12, LiveHubContract.PropertyGroup(43));

        await _notifier.TaskChangedAsync(42, eventId: 7, planDay: new DateOnly(2026, 11, 21), complianceId: 900);

        var sent = SentOf(LiveHubContract.ServerMethods.TaskChanged);
        Assert.That(sent, Has.Count.EqualTo(1));
        Assert.That(sent[0].Ids, Is.EqualTo(new[] { "c1" }));
        Assert.That(Json(sent[0].Payload), Is.EqualTo(
            """{"v":1,"property_id":"42","event_id":"7","plan_day_key":"2026-11-21","compliance_id":900,"kind":"changed"}"""));
        Assert.That(_groupBroadcast.ReceivedCalls(), Is.Empty, "hints go to the registry's members, never a group broadcast");
    }

    [Test]
    public async Task TaskChanged_PropertyOnly_OmitsTheUnknownKeyFields()
    {
        Member("c1", 11, LiveHubContract.PropertyGroup(42));

        await _notifier.TaskChangedAsync(42, kind: TaskChangeKinds.Property);

        Assert.That(Json(SentOf(LiveHubContract.ServerMethods.TaskChanged).Single().Payload),
            Is.EqualTo("""{"v":1,"property_id":"42","kind":"property"}"""));
    }

    [Test]
    public async Task AssignmentsChanged_GoesToTheSiteGroupsMembersOnly()
    {
        Member("c1", 11, LiveHubContract.SiteGroup(11));
        Member("c2", 12, LiveHubContract.SiteGroup(12));

        await _notifier.AssignmentsChangedAsync(11);

        var sent = SentOf(LiveHubContract.ServerMethods.AssignmentsChanged).Single();
        Assert.That(sent.Ids, Is.EqualTo(new[] { "c1" }));
        Assert.That(Json(sent.Payload), Is.EqualTo("""{"v":1}"""));
    }

    [Test]
    public async Task NoMembers_IsANoOp()
    {
        Member("c1", 11, LiveHubContract.PropertyGroup(43));

        await _notifier.TaskChangedAsync(42);
        await _notifier.AssignmentsChangedAsync(11);

        Assert.That(_sent, Is.Empty);
        Assert.That(_groupBroadcast.ReceivedCalls(), Is.Empty);
    }

    [Test]
    public async Task Publish_FirstEndsConnectionsWhoseTokenExpired()
    {
        Member("expired", 11, LiveHubContract.PropertyGroup(42), expiresAt: Now.AddMinutes(30));
        _registry.Joined("expired", LiveHubContract.SiteGroup(11));
        Member("valid", 12, LiveHubContract.PropertyGroup(42), expiresAt: Now.AddHours(2));
        _clock.UtcNow = Now.AddMinutes(30); // expiry is inclusive

        await _notifier.TaskChangedAsync(42);

        Assert.Multiple(() =>
        {
            Assert.That(_aborted, Is.EqualTo(new[] { "expired" }));
            Assert.That(_registry.SiteOf("expired"), Is.Null);
            Assert.That(_registry.SiteOf("valid"), Is.EqualTo(12));
        });
        var refused = SentOf(LiveHubContract.ServerMethods.Refused).Single();
        Assert.That(refused.Ids, Is.EqualTo(new[] { "expired" }));
        Assert.That(Json(refused.Payload), Is.EqualTo("""{"v":1,"code":"not_authenticated"}"""));
        Assert.That(RecipientsOf(LiveHubContract.ServerMethods.TaskChanged), Is.EqualTo(new[] { "valid" }));
    }

    /// <summary>
    /// PR #1523 review: publish A takes the expired connection out of the
    /// registry and is still ending it (its Refused send is paused) when
    /// publish B runs. B must not reach the expired connection.
    /// </summary>
    [Test]
    public async Task APublishDuringAnotherPublishsCleanUp_DoesNotReachTheExpiredConnection()
    {
        Member("expired", 11, LiveHubContract.PropertyGroup(42), expiresAt: Now.AddMinutes(30));
        Member("valid", 12, LiveHubContract.PropertyGroup(42), expiresAt: Now.AddHours(2));
        _clock.UtcNow = Now.AddMinutes(30);
        var cleanUp = new TaskCompletionSource();
        _refusedGate = cleanUp;

        var publishA = _notifier.TaskChangedAsync(42, eventId: 1);
        Assert.That(publishA.IsCompleted, Is.False, "A is paused while ending the expired connection");
        await _notifier.TaskChangedAsync(42, eventId: 2);

        Assert.That(RecipientsOf(LiveHubContract.ServerMethods.TaskChanged), Is.EqualTo(new[] { "valid" }));
        cleanUp.SetResult();
        await publishA;
        Assert.That(RecipientsOf(LiveHubContract.ServerMethods.TaskChanged), Is.EqualTo(new[] { "valid", "valid" }));
        Assert.That(_groupBroadcast.ReceivedCalls(), Is.Empty);
    }

    /// <summary>The same window for a site revoke: a publish while it is still ending the connection does not reach it.</summary>
    [Test]
    public async Task APublishDuringASiteRevokesCleanUp_DoesNotReachTheRevokedConnection()
    {
        Member("revoked", 11, LiveHubContract.PropertyGroup(42));
        Member("valid", 12, LiveHubContract.PropertyGroup(42));
        var cleanUp = new TaskCompletionSource();
        _refusedGate = cleanUp;

        var revoke = _notifier.RevokeSiteAsync(11);
        Assert.That(revoke.IsCompleted, Is.False, "the revoke is paused while ending the connection");
        await _notifier.TaskChangedAsync(42);

        Assert.That(RecipientsOf(LiveHubContract.ServerMethods.TaskChanged), Is.EqualTo(new[] { "valid" }));
        cleanUp.SetResult();
        await revoke;
    }

    [Test]
    public async Task RevokeSite_EndsEveryConnectionOfThatSiteOnly()
    {
        Member("a", 11, LiveHubContract.SiteGroup(11), userId: 5);
        _registry.Joined("a", LiveHubContract.PropertyGroup(42));
        Member("b", 11, LiveHubContract.PropertyGroup(43), userId: 5);
        Member("other", 12, LiveHubContract.PropertyGroup(42));

        await _notifier.RevokeSiteAsync(11);

        Assert.Multiple(() =>
        {
            Assert.That(_aborted, Is.EquivalentTo(new[] { "a", "b" }));
            Assert.That(_registry.ConnectionsOf(5), Is.Zero);
            Assert.That(_registry.IsIn("other", LiveHubContract.PropertyGroup(42)), Is.True);
        });
        Assert.That(SentOf(LiveHubContract.ServerMethods.Refused).Select(x => Json(x.Payload)),
            Is.All.EqualTo("""{"v":1,"code":"revoked"}"""));

        await _notifier.TaskChangedAsync(42);
        await _notifier.AssignmentsChangedAsync(11);
        Assert.That(RecipientsOf(LiveHubContract.ServerMethods.TaskChanged), Is.EqualTo(new[] { "other" }));
        Assert.That(RecipientsOf(LiveHubContract.ServerMethods.AssignmentsChanged), Is.Empty);
    }

    [Test]
    public async Task HubContextFailures_NeverReachTheCaller()
    {
        Member("c1", 11, LiveHubContract.PropertyGroup(42));
        _registry.Joined("c1", LiveHubContract.SiteGroup(11));
        Member("c2", 13, LiveHubContract.SiteGroup(13));
        _sendFailure = new InvalidOperationException("send");
        var logger = Substitute.For<ILogger<LiveNotifier>>();
        var notifier = new LiveNotifier(_hub, _registry, logger);

        await Assert.DoesNotThrowAsync(() => notifier.TaskChangedAsync(42, eventId: 7));
        await Assert.DoesNotThrowAsync(() => notifier.AssignmentsChangedAsync(11));
        await Assert.DoesNotThrowAsync(() => notifier.RevokeSiteAsync(13));
        Assert.That(_aborted, Is.EqualTo(new[] { "c2" }), "a connection is aborted even when its Refused send fails");

        _clients.Clients(Arg.Any<IReadOnlyList<string>>()).Throws(new InvalidOperationException("clients"));
        await Assert.DoesNotThrowAsync(() => notifier.AssignmentsChangedAsync(11));
        await Assert.DoesNotThrowAsync(() => notifier.RevokePropertyAccessAsync(11, 42));
        Assert.That(logger.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ILogger.Log)), Is.GreaterThanOrEqualTo(5));
    }

    [Test]
    public async Task RevokePropertyAccess_RemovesOnlyThatSitesConnectionsFromThatGroup()
    {
        Member("mine", 11, LiveHubContract.PropertyGroup(42));
        _registry.Joined("mine", LiveHubContract.PropertyGroup(43));
        Member("other", 12, LiveHubContract.PropertyGroup(42));

        await _notifier.RevokePropertyAccessAsync(11, 42);

        Assert.Multiple(() =>
        {
            Assert.That(_registry.IsIn("mine", LiveHubContract.PropertyGroup(42)), Is.False);
            Assert.That(_registry.IsIn("mine", LiveHubContract.PropertyGroup(43)), Is.True);
            Assert.That(_registry.IsIn("other", LiveHubContract.PropertyGroup(42)), Is.True);
            Assert.That(_registry.RevocationCount(11, 42), Is.EqualTo(1));
            Assert.That(_aborted, Is.Empty, "a property revoke does not end the connection");
        });

        await _notifier.TaskChangedAsync(42);
        await _notifier.TaskChangedAsync(43);
        Assert.That(RecipientsOf(LiveHubContract.ServerMethods.TaskChanged), Is.EqualTo(new[] { "other", "mine" }));
    }

    [Test]
    public async Task Registry_PerUserCap_HoldsUnderConcurrentConnects_AndFreesOnDisconnect()
    {
        const int attempts = 200;
        var start = new ManualResetEventSlim(false);
        var tasks = Enumerable.Range(0, attempts).Select(i => Task.Run(() =>
        {
            start.Wait();
            return _registry.TryConnect($"c{i}", 5, Now.AddHours(1), () => { }, LiveHubLimits.MaxConnectionsPerUser);
        })).ToList();
        start.Set();
        var results = await Task.WhenAll(tasks);

        Assert.That(results.Count(r => r), Is.EqualTo(LiveHubLimits.MaxConnectionsPerUser));
        Assert.That(_registry.ConnectionsOf(5), Is.EqualTo(LiveHubLimits.MaxConnectionsPerUser));
        Assert.That(_registry.TryConnect("other-user", 6, Now.AddHours(1), () => { }, LiveHubLimits.MaxConnectionsPerUser), Is.True);

        await Task.WhenAll(Enumerable.Range(0, attempts).Select(i => Task.Run(() => _registry.Disconnected($"c{i}"))));

        Assert.That(_registry.ConnectionsOf(5), Is.Zero);
        Assert.That(_registry.TryConnect("again", 5, Now.AddHours(1), () => { }, LiveHubLimits.MaxConnectionsPerUser), Is.True);
    }

    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; }
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    [Test]
    public void Registration_MapsTheHubOnItsPath_WithTheNamedLimits_AndResolvesTheNotifier()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddBackendConfigurationLiveHub();
        builder.Services.AddBackendConfigurationLiveHub(); // idempotent
        using var app = builder.Build();
        app.MapBackendConfigurationLiveHub();

        var patterns = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(d => d.Endpoints).OfType<RouteEndpoint>()
            .Select(e => e.RoutePattern.RawText).ToList();
        var options = app.Services.GetRequiredService<IOptions<HubOptions<BackendConfigurationHub>>>().Value;

        Assert.Multiple(() =>
        {
            Assert.That(patterns, Does.Contain("/hub/backend-configuration"));
            Assert.That(patterns, Does.Contain("/hub/backend-configuration/negotiate"));
            // Outside every prefix the gRPC ingress claims (helm ingress-grpc.yaml).
            Assert.That(LiveHubContract.HubPath,
                Does.Not.StartWith("/eform.").And.Not.StartWith("/timeplanning.").And.Not.StartWith("/backend_configuration."));
            Assert.That(options.KeepAliveInterval, Is.EqualTo(LiveHubLimits.KeepAliveInterval));
            Assert.That(options.ClientTimeoutInterval, Is.EqualTo(LiveHubLimits.ClientTimeoutInterval));
            Assert.That(options.MaximumReceiveMessageSize, Is.EqualTo(LiveHubLimits.MaximumReceiveMessageSize));
            Assert.That(options.EnableDetailedErrors, Is.False);
            var notifier = app.Services.GetRequiredService<ILiveNotifier>();
            Assert.That(notifier, Is.InstanceOf<LiveNotifier>());
            Assert.That(app.Services.GetRequiredService<ILiveNotifier>(), Is.SameAs(notifier), "singleton");
        });
    }
}
