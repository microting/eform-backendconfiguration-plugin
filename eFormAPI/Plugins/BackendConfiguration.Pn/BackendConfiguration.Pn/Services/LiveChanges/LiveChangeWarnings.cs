#nullable enable
using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace BackendConfiguration.Pn.Services.LiveChanges;

/// <summary>
/// The live-change pipeline swallows every failure (a hint must never break a
/// write). Each kind of failure is logged once per process at Warning, then at
/// Debug, so a broken hub or resolver cannot flood the log on every save.
/// </summary>
public sealed class LiveChangeWarnings
{
    private readonly ConcurrentDictionary<string, byte> _logged = new();

    public void Report(ILogger logger, string kind, Exception exception)
    {
        try
        {
            if (_logged.TryAdd(kind, 0))
                logger.LogWarning(exception, "Live updates: {Kind} failed; the write is unaffected (logged once per kind)", kind);
            else
                logger.LogDebug(exception, "Live updates: {Kind} failed again", kind);
        }
        catch
        {
            // A logger that throws must not reach the caller either.
        }
    }
}
