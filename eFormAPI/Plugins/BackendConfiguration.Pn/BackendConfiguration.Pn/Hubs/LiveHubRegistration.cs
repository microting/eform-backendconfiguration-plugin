#nullable enable
using System;
using BackendConfiguration.Pn.Services.LiveNotifier;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BackendConfiguration.Pn.Hubs;

/// <summary>
/// Registers and maps the live hub from the plugin, as kanban does: the host
/// never calls AddSignalR or MapHub, and plugin.Configure runs between the
/// host's UseRouting and UseEndpoints. No new package: SignalR is part of the
/// ASP.NET Core shared framework.
/// </summary>
public static class LiveHubRegistration
{
    public static IServiceCollection AddBackendConfigurationLiveHub(this IServiceCollection services)
    {
        // AddSignalR is idempotent (kanban calls it too). The options are this
        // hub's only, so the host's other hubs keep theirs.
        services.AddSignalR().AddHubOptions<BackendConfigurationHub>(o =>
        {
            o.KeepAliveInterval = LiveHubLimits.KeepAliveInterval;
            o.ClientTimeoutInterval = LiveHubLimits.ClientTimeoutInterval;
            o.HandshakeTimeout = LiveHubLimits.HandshakeTimeout;
            o.MaximumReceiveMessageSize = LiveHubLimits.MaximumReceiveMessageSize;
            o.MaximumParallelInvocationsPerClient = LiveHubLimits.MaximumParallelInvocationsPerClient;
            o.EnableDetailedErrors = false;
        });
        services.TryAddSingleton(TimeProvider.System); // the plugin registers the same; whichever runs first wins
        services.TryAddSingleton<LiveConnectionRegistry>();
        services.TryAddSingleton<LiveHubAuthenticator>();
        services.TryAddSingleton<ILiveNotifier, LiveNotifier>();
        services.TryAddTransient<ILiveCallerResolver, LiveCallerResolver>();
        services.TryAddTransient<ILivePropertyAccess, WorkerLivePropertyAccess>();
        return services;
    }

    public static HubEndpointConventionBuilder MapBackendConfigurationLiveHub(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapHub<BackendConfigurationHub>(LiveHubContract.HubPath);
}
