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
    private IClientProxy _proxy = null!;
    private IClientProxy _single = null!;
    private IGroupManager _groups = null!;
    private LiveNotifier _notifier = null!;
    private readonly List<string> _aborted = [];

    [SetUp]
    public void SetUp()
    {
        _clock = new ManualClock { UtcNow = Now };
        _registry = new LiveConnectionRegistry(_clock);
        _hub = Substitute.For<IHubContext<BackendConfigurationHub>>();
        _clients = Substitute.For<IHubClients>();
        _proxy = Substitute.For<IClientProxy>();
        _single = Substitute.For<IClientProxy>();
        _groups = Substitute.For<IGroupManager>();
        _hub.Clients.Returns(_clients);
        _hub.Groups.Returns(_groups);
        _clients.Group(Arg.Any<string>()).Returns(_proxy);
        _clients.Clients(Arg.Any<IReadOnlyList<string>>()).Returns(_single);
        _aborted.Clear();
        _notifier = new LiveNotifier(_hub, _registry, NullLogger<LiveNotifier>.Instance);
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

    private static object? SentPayload(IClientProxy proxy, string method)
    {
        var call = proxy.ReceivedCalls().Single();
        Assert.That(call.GetArguments()[0], Is.EqualTo(method));
        var args = (object?[])call.GetArguments()[1]!;
        Assert.That(args, Has.Length.EqualTo(1));
        return args[0];
    }

    [Test]
    public async Task TaskChanged_FullKey_GoesToThePropertyGroupOnly_WithTheDocumentedPayload()
    {
        Member("c1", 11, LiveHubContract.PropertyGroup(42));
        Member("c2", 12, LiveHubContract.PropertyGroup(43));

        await _notifier.TaskChangedAsync(42, eventId: 7, planDay: new DateOnly(2026, 11, 21), complianceId: 900);

        _clients.Received(1).Group(LiveHubContract.PropertyGroup(42));
        _clients.ReceivedWithAnyArgs(1).Group(default!);
        Assert.That(Json(SentPayload(_proxy, LiveHubContract.ServerMethods.TaskChanged)), Is.EqualTo(
            """{"v":1,"property_id":"42","event_id":"7","plan_day_key":"2026-11-21","compliance_id":900,"kind":"changed"}"""));
    }

    [Test]
    public async Task TaskChanged_PropertyOnly_OmitsTheUnknownKeyFields()
    {
        Member("c1", 11, LiveHubContract.PropertyGroup(42));

        await _notifier.TaskChangedAsync(42, kind: TaskChangeKinds.Property);

        Assert.That(Json(SentPayload(_proxy, LiveHubContract.ServerMethods.TaskChanged)),
            Is.EqualTo("""{"v":1,"property_id":"42","kind":"property"}"""));
    }

    [Test]
    public async Task AssignmentsChanged_GoesToTheSiteGroupOnly()
    {
        Member("c1", 11, LiveHubContract.SiteGroup(11));

        await _notifier.AssignmentsChangedAsync(11);

        _clients.Received(1).Group(LiveHubContract.SiteGroup(11));
        _clients.ReceivedWithAnyArgs(1).Group(default!);
        Assert.That(Json(SentPayload(_proxy, LiveHubContract.ServerMethods.AssignmentsChanged)), Is.EqualTo("""{"v":1}"""));
    }

    [Test]
    public async Task NoMembers_IsANoOp()
    {
        Member("c1", 11, LiveHubContract.PropertyGroup(43));

        await _notifier.TaskChangedAsync(42);
        await _notifier.AssignmentsChangedAsync(11);

        Assert.That(_proxy.ReceivedCalls(), Is.Empty);
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
        await _groups.Received(1).RemoveFromGroupAsync("expired", LiveHubContract.PropertyGroup(42), Arg.Any<CancellationToken>());
        await _groups.Received(1).RemoveFromGroupAsync("expired", LiveHubContract.SiteGroup(11), Arg.Any<CancellationToken>());
        _clients.Received(1).Clients(Arg.Is<IReadOnlyList<string>>(ids => ids.Single() == "expired"));
        Assert.That(Json(SentPayload(_single, LiveHubContract.ServerMethods.Refused)),
            Is.EqualTo("""{"v":1,"code":"not_authenticated"}"""));
        Assert.That(SentPayload(_proxy, LiveHubContract.ServerMethods.TaskChanged), Is.Not.Null, "the valid member still gets the hint");
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
        await _groups.Received(1).RemoveFromGroupAsync("a", LiveHubContract.SiteGroup(11), Arg.Any<CancellationToken>());
        await _groups.Received(1).RemoveFromGroupAsync("a", LiveHubContract.PropertyGroup(42), Arg.Any<CancellationToken>());
        await _groups.Received(1).RemoveFromGroupAsync("b", LiveHubContract.PropertyGroup(43), Arg.Any<CancellationToken>());
        await _groups.ReceivedWithAnyArgs(3).RemoveFromGroupAsync(default!, default!);
        Assert.That(_single.ReceivedCalls().Select(c => Json(((object?[])c.GetArguments()[1]!)[0])),
            Is.All.EqualTo("""{"v":1,"code":"revoked"}"""));
    }

    [Test]
    public async Task HubContextFailures_NeverReachTheCaller()
    {
        Member("c1", 11, LiveHubContract.PropertyGroup(42));
        _registry.Joined("c1", LiveHubContract.SiteGroup(11));
        Member("c2", 13, LiveHubContract.SiteGroup(13));
        _proxy.SendCoreAsync(default!, default!, default).ThrowsAsyncForAnyArgs(new InvalidOperationException("send"));
        _single.SendCoreAsync(default!, default!, default).ThrowsAsyncForAnyArgs(new InvalidOperationException("single"));
        _groups.RemoveFromGroupAsync(default!, default!, default).ThrowsForAnyArgs(new InvalidOperationException("sync"));
        var logger = Substitute.For<ILogger<LiveNotifier>>();
        var notifier = new LiveNotifier(_hub, _registry, logger);

        await Assert.DoesNotThrowAsync(() => notifier.TaskChangedAsync(42, eventId: 7));
        await Assert.DoesNotThrowAsync(() => notifier.AssignmentsChangedAsync(11));
        await Assert.DoesNotThrowAsync(() => notifier.RevokePropertyAccessAsync(11, 42));
        await Assert.DoesNotThrowAsync(() => notifier.RevokeSiteAsync(13));
        Assert.That(_aborted, Is.EqualTo(new[] { "c2" }), "a connection is aborted even when its group removal fails");

        _clients.Group(Arg.Any<string>()).Throws(new InvalidOperationException("clients"));
        await Assert.DoesNotThrowAsync(() => notifier.AssignmentsChangedAsync(11));
        Assert.That(logger.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ILogger.Log)), Is.GreaterThanOrEqualTo(5));
    }

    [Test]
    public async Task RevokePropertyAccess_RemovesOnlyThatSitesConnectionsFromThatGroup()
    {
        Member("mine", 11, LiveHubContract.PropertyGroup(42));
        _registry.Joined("mine", LiveHubContract.PropertyGroup(43));
        Member("other", 12, LiveHubContract.PropertyGroup(42));

        await _notifier.RevokePropertyAccessAsync(11, 42);

        await _groups.Received(1).RemoveFromGroupAsync("mine", LiveHubContract.PropertyGroup(42), Arg.Any<CancellationToken>());
        await _groups.ReceivedWithAnyArgs(1).RemoveFromGroupAsync(default!, default!);
        Assert.Multiple(() =>
        {
            Assert.That(_registry.IsIn("mine", LiveHubContract.PropertyGroup(42)), Is.False);
            Assert.That(_registry.IsIn("mine", LiveHubContract.PropertyGroup(43)), Is.True);
            Assert.That(_registry.IsIn("other", LiveHubContract.PropertyGroup(42)), Is.True);
            Assert.That(_registry.RevocationCount(11, 42), Is.EqualTo(1));
            Assert.That(_aborted, Is.Empty);
        });
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
