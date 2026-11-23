#nullable enable

namespace BackendConfiguration.Pn.Integration.Test;

using System.Collections.Concurrent;
using BackendConfiguration.Pn.Hubs;
using BackendConfiguration.Pn.Services.LiveChanges;
using BackendConfiguration.Pn.Services.LiveNotifier;
using eFormCore;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.ItemsPlanningBase.Infrastructure.Data;
using NSubstitute;

/// <summary>Records every <see cref="ILiveNotifier"/> call, in order; can be told to throw.</summary>
public sealed class RecordingLiveNotifier : ILiveNotifier
{
    public sealed record Call(string Method, int? PropertyId = null, int? EventId = null, DateOnly? PlanDay = null,
        long? ComplianceId = null, string? Kind = null, int? SdkSiteId = null);

    private readonly List<Call> _calls = [];

    public Exception? Throw { get; set; }

    public List<Call> Calls
    {
        get
        {
            lock (_calls) return _calls.ToList();
        }
    }

    public void Clear()
    {
        lock (_calls) _calls.Clear();
    }

    private Task Record(Call call)
    {
        lock (_calls) _calls.Add(call);
        if (Throw is { } e) throw e;
        return Task.CompletedTask;
    }

    public Task TaskChangedAsync(int propertyId, int? eventId = null, DateOnly? planDay = null, long? complianceId = null,
        string kind = TaskChangeKinds.Changed) =>
        Record(new Call("TaskChanged", propertyId, eventId, planDay, complianceId, kind));

    public Task AssignmentsChangedAsync(int sdkSiteId) => Record(new Call("AssignmentsChanged", SdkSiteId: sdkSiteId));

    public Task RevokePropertyAccessAsync(int sdkSiteId, int propertyId) =>
        Record(new Call("RevokePropertyAccess", PropertyId: propertyId, SdkSiteId: sdkSiteId));

    public Task RevokeSiteAsync(int sdkSiteId) => Record(new Call("RevokeSite", SdkSiteId: sdkSiteId));
}

/// <summary>Counts Warning-level entries per category, for "logged once" assertions.</summary>
internal sealed class WarningCounter : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, int> _warnings = new();

    public int WarningsOf<T>() => _warnings.GetValueOrDefault(typeof(T).FullName!);

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class Logger(WarningCounter owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning) owner._warnings.AddOrUpdate(category, 1, (_, n) => n + 1);
        }
    }
}

/// <summary>
/// The production live-change wiring on the test databases: the plugin and
/// items-planning contexts POOLED with the interceptors exactly as
/// <c>EformBackendConfigurationPlugin.ConfigureDbContext</c> registers them, the
/// real collector/publisher/resolver, a recording notifier, and an
/// <see cref="IHttpContextAccessor"/> a test can point at a fake request.
/// </summary>
internal sealed class LiveChangeTestHost : IDisposable
{
    public RecordingLiveNotifier Notifier { get; } = new();
    public HttpContextAccessor Http { get; } = new();
    public WarningCounter Warnings { get; } = new();
    public ServiceProvider Provider { get; }
    public LiveChangePublisher Publisher => Provider.GetRequiredService<LiveChangePublisher>();

    public LiveConnectionRegistry Registry { get; } = new(TimeProvider.System);

    /// <param name="connected">One live hub connection is registered (the pipeline does nothing without one).</param>
    /// <param name="extraInterceptors">Added after the live interceptors to both contexts.</param>
    public LiveChangeTestHost(string bcConnectionString, string ipConnectionString, Core? core,
        Action<IServiceCollection>? configure = null, bool connected = true,
        Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[]? extraInterceptors = null)
    {
        if (connected) Registry.TryConnect("test-connection", 1, DateTimeOffset.MaxValue, () => { }, 10);
        var extra = extraInterceptors ?? [];
        var services = new ServiceCollection();
        services.AddSingleton(Registry);
        services.AddLogging(b => b.AddProvider(Warnings).SetMinimumLevel(LogLevel.Debug));
        services.AddSingleton<IHttpContextAccessor>(Http);
        services.AddBackendConfigurationLiveChanges();
        services.AddSingleton<ILiveNotifier>(Notifier);
        var coreHelper = Substitute.For<IEFormCoreService>();
        if (core != null) coreHelper.GetCore().Returns(Task.FromResult(core));
        services.AddSingleton(coreHelper);
        var bcVersion = ServerVersion.AutoDetect(bcConnectionString);
        var ipVersion = ServerVersion.AutoDetect(ipConnectionString);
        services.AddDbContextPool<BackendConfigurationPnDbContext>((sp, o) =>
            o.UseMySql(bcConnectionString, bcVersion, b => b.EnableRetryOnFailure()).AddLiveChangeInterceptors(sp)
                .AddInterceptors(extra));
        services.AddDbContextPool<ItemsPlanningPnDbContext>((sp, o) =>
            o.UseMySql(ipConnectionString, ipVersion, b => b.EnableRetryOnFailure()).AddLiveChangeInterceptors(sp)
                .AddInterceptors(extra));
        configure?.Invoke(services);
        Provider = services.BuildServiceProvider();
    }

    /// <summary>
    /// Runs <paramref name="work"/> as one HTTP request (its own DI scope,
    /// HttpContext.RequestServices = that scope) and returns the hints sent
    /// once the request has ended.
    /// </summary>
    public async Task<List<RecordingLiveNotifier.Call>> InRequestAsync(Func<IServiceProvider, Task> work)
    {
        Notifier.Clear();
        using (var scope = Provider.CreateScope())
        {
            Http.HttpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            try
            {
                await work(scope.ServiceProvider);
                await Publisher.WhenIdleAsync();
                Assert.That(Notifier.Calls.Where(c => c.Method is "TaskChanged" or "AssignmentsChanged"), Is.Empty,
                    "no hint before the request has ended");
            }
            finally
            {
                Http.HttpContext = null;
            }
        }

        await Publisher.WhenIdleAsync();
        return Notifier.Calls;
    }

    /// <summary>Runs <paramref name="work"/> in a DI scope with no request, then returns the hints sent.</summary>
    public async Task<List<RecordingLiveNotifier.Call>> InBackgroundAsync(Func<IServiceProvider, Task> work)
    {
        Notifier.Clear();
        using (var scope = Provider.CreateScope())
        {
            await work(scope.ServiceProvider);
        }

        await Publisher.WhenIdleAsync();
        return Notifier.Calls;
    }

    public void Dispose() => Provider.Dispose();
}
