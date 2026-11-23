#nullable enable
using System;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace BackendConfiguration.Pn.Services.LiveChanges;

/// <summary>
/// Batches the live changes of background work that has no HTTP request
/// (startup repairs, jobs): inside <c>using (LiveChangeScope.Begin(scope.ServiceProvider))</c>
/// the scope's saves are collected like a request's and sent once, when the
/// handle is disposed (or, at the latest, when the DI scope ends).
/// <para>
/// Without it, a background save is still announced, right after it commits,
/// one flush per SaveChanges (see <see cref="LiveChangeInterceptor"/>).
/// </para>
/// </summary>
public static class LiveChangeScope
{
    private static readonly AsyncLocal<LiveChangeCollector?> Ambient = new();

    /// <summary>The innermost open background batch on this async flow, if any.</summary>
    internal static LiveChangeCollector? Current => Ambient.Value is { IsClosed: false } collector ? collector : null;

    /// <summary>Starts a batch over <paramref name="scopeServices"/> (a DI scope's provider). Never throws.</summary>
    public static IDisposable Begin(IServiceProvider scopeServices)
    {
        LiveChangeCollector? collector;
        try
        {
            collector = scopeServices.GetService<LiveChangeCollector>();
        }
        catch (Exception)
        {
            collector = null;
        }

        if (collector is null) return NoOp.Instance;
        var previous = Ambient.Value;
        Ambient.Value = collector;
        return new Handle(collector, previous);
    }

    private sealed class Handle(LiveChangeCollector collector, LiveChangeCollector? previous) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
            Ambient.Value = previous;
            collector.Flush();
        }
    }

    private sealed class NoOp : IDisposable
    {
        public static readonly NoOp Instance = new();

        public void Dispose()
        {
        }
    }
}
