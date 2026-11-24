#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace BackendConfiguration.Pn.Services.LiveChanges;

/// <summary>
/// Finds the collector a save belongs to: an open <see cref="LiveChangeScope"/>
/// on this async flow, else the request's own (when the context is the
/// request's), else none (the save is then announced on its own after commit).
/// A seam so tests can substitute it.
/// </summary>
public interface ILiveChangeTargets
{
    LiveChangeCollector? For(DbContext context);
}

public sealed class LiveChangeTargets(IHttpContextAccessor? httpContextAccessor = null) : ILiveChangeTargets
{
    public LiveChangeCollector? For(DbContext context)
    {
        if (LiveChangeScope.Current is { } ambient && ambient.Owns(context)) return ambient;
        var http = httpContextAccessor?.HttpContext;
        if (http is null) return null;
        try
        {
            var collector = http.RequestServices?.GetService<LiveChangeCollector>();
            return collector is { IsClosed: false } && collector.Owns(context) ? collector : null;
        }
        catch (ObjectDisposedException)
        {
            return null; // the request has ended under a background save
        }
    }
}

/// <summary>
/// Detects task-list changes at the one place every plugin and items-planning
/// write goes through, SaveChanges. A singleton on pooled contexts, so all
/// per-save state is keyed by context instance (as kanban's interceptor).
/// <list type="bullet">
///   <item>SavingChanges: an in-memory snapshot of the watched rows (no query, no I/O).
///     In a read path (<see cref="ILiveChanges.BeginReadPath"/>) only revocation rows (assignments, properties) are kept.</item>
///   <item>SavedChanges outside a transaction: the changes are committed; they go to the
///     collector (sent when its scope ends) or, with none, are published at once in the background.</item>
///   <item>SavedChanges inside a transaction: held until <see cref="LiveChangeTransactionInterceptor"/>
///     sees the commit; a rollback, or a transaction that is only disposed, drops them.</item>
///   <item>A failed or cancelled save drops its snapshot.</item>
/// </list>
/// Never throws into the save: every failure is swallowed and logged once per kind.
/// </summary>
public sealed class LiveChangeInterceptor(
    ILiveChangeTargets targets,
    LiveChangePublisher publisher,
    LiveChangeWarnings warnings,
    ILogger<LiveChangeInterceptor> logger) : SaveChangesInterceptor
{

    /// <summary>A save in progress: its collector, and its snapshot once EF's own change detection has run.</summary>
    private sealed class Pending(LiveChangeCollector? target, bool revocationRowsOnly)
    {
        public LiveChangeCollector? Target { get; } = target;
        public bool RevocationRowsOnly { get; } = revocationRowsOnly;
        public List<CapturedEntry>? Entries { get; set; }
    }

    private sealed class TransactionBuffer
    {
        public readonly List<(LiveChangeCollector? Target, List<LiveChange> Changes)> Items = [];
    }

    private readonly ConditionalWeakTable<DbContext, Pending> _pending = new();
    private readonly ConditionalWeakTable<DbContext, TransactionBuffer> _transactions = new();

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Capture(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Capture(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Saved(eventData.Context);
        return result;
    }

    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
        CancellationToken cancellationToken = default)
    {
        Saved(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => Forget(eventData.Context);

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Forget(eventData.Context);
        return Task.CompletedTask;
    }

    public override void SaveChangesCanceled(DbContextEventData eventData) => Forget(eventData.Context);

    public override Task SaveChangesCanceledAsync(DbContextEventData eventData, CancellationToken cancellationToken = default)
    {
        Forget(eventData.Context);
        return Task.CompletedTask;
    }

    /// <summary>
    /// A transaction began (anything left from an earlier one that was never
    /// committed is stale) or rolled back (nothing it saved happened): drop
    /// what the context's transaction held.
    /// </summary>
    internal void DropTransaction(DbContext? context)
    {
        if (context is not null) _transactions.Remove(context);
    }

    /// <summary>The transaction committed: its saves are now real.</summary>
    internal void TransactionCommitted(DbContext? context)
    {
        if (context is null || !_transactions.TryGetValue(context, out var buffer)) return;
        _transactions.Remove(context);
        try
        {
            // Every revocation of the transaction first, then the hints: a hint dispatched
            // for an earlier save must not reach a connection a later save revoked.
            var rests = buffer.Items.Select(i => (Collector: i.Target, Remaining: publisher.ApplyRevocations(i.Changes))).ToList();
            foreach (var (target, rest) in rests)
            {
                Deliver(target, rest);
            }
        }
        catch (Exception e)
        {
            warnings.Report(logger, "commit", e);
        }
    }

    /// <summary>
    /// SavingChanges. Revocations are always captured, hints only with an
    /// audience: while no hub connection exists only the revocation rows
    /// (PropertyWorker, Property) are kept (no collector lookup), because a connection may join before
    /// this save commits and must then be revoked; the revoke at commit is a
    /// registry no-op if still nobody is connected. The save is marked, and the
    /// snapshot is taken from EF's OWN change
    /// detection of this save (<see cref="OnDetectedAllChanges"/>), so no extra
    /// detection pass runs. With AutoDetectChanges off, EF does not detect: the
    /// tracked states are read now, without forcing a detection.
    /// </summary>
    private void Capture(DbContext? context)
    {
        if (context is null) return;
        _pending.Remove(context);
        try
        {
            // Revocations are always captured; everything else only while changes are wanted
            // (a hub client is connected, or an install has task reminders on).
            var wanted = publisher.WantsChanges;
            var target = wanted ? targets.For(context) : null;
            var pending = new Pending(target, revocationRowsOnly: !wanted || target?.IsReadPath == true);
            var tracker = context.ChangeTracker;
            if (tracker.AutoDetectChangesEnabled)
            {
                // -= then += never stacks it (same method, same target); a pooled context's
                // events are reset when it is returned, hence per save.
                tracker.DetectedAllChanges -= OnDetectedAllChanges;
                tracker.DetectedAllChanges += OnDetectedAllChanges;
            }
            else
            {
                pending.Entries = LiveChangeCapture.Snapshot(tracker, pending.RevocationRowsOnly);
            }

            _pending.AddOrUpdate(context, pending);
        }
        catch (Exception e)
        {
            _pending.Remove(context);
            warnings.Report(logger, "capture", e);
        }
    }

    /// <summary>EF's own detection pass of a marked save has run: snapshot the now-final states.</summary>
    private void OnDetectedAllChanges(object? sender, DetectedChangesEventArgs args)
    {
        if (sender is not ChangeTracker tracker) return;
        var context = tracker.Context;
        if (!_pending.TryGetValue(context, out var pending) || pending.Entries is not null) return;
        try
        {
            pending.Entries = LiveChangeCapture.Snapshot(tracker, pending.RevocationRowsOnly);
        }
        catch (Exception e)
        {
            _pending.Remove(context);
            warnings.Report(logger, "capture", e);
        }
    }

    private void Forget(DbContext? context)
    {
        if (context is not null) _pending.Remove(context);
    }

    private void Saved(DbContext? context)
    {
        if (context is null || !_pending.TryGetValue(context, out var pending)) return;
        _pending.Remove(context);
        if (pending.Entries is not { Count: > 0 } entries) return;
        try
        {
            var changes = LiveChangeCapture.ToChanges(entries);
            if (changes.Count == 0) return;
            if (context.Database.CurrentTransaction is not null)
            {
                _transactions.GetOrCreateValue(context).Items.Add((pending.Target, changes));
                return;
            }

            Deliver(pending.Target, changes);
        }
        catch (Exception e)
        {
            warnings.Report(logger, "commit", e);
        }
    }

    private void Deliver(LiveChangeCollector? target, IReadOnlyList<LiveChange> changes)
    {
        try
        {
            if (target is { IsClosed: false })
            {
                target.Accept(changes);
                return;
            }

            publisher.Submit(changes);
        }
        catch (Exception e)
        {
            warnings.Report(logger, "deliver", e);
        }
    }
}

/// <summary>Feeds transaction boundaries to <see cref="LiveChangeInterceptor"/>, so a rolled-back save is never announced.</summary>
public sealed class LiveChangeTransactionInterceptor(LiveChangeInterceptor changes) : DbTransactionInterceptor
{
    public override DbTransaction TransactionStarted(DbConnection connection, TransactionEndEventData eventData,
        DbTransaction result)
    {
        changes.DropTransaction(eventData.Context);
        return result;
    }

    public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection,
        TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default)
    {
        changes.DropTransaction(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) =>
        changes.TransactionCommitted(eventData.Context);

    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        changes.TransactionCommitted(eventData.Context);
        return Task.CompletedTask;
    }

    public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) =>
        changes.DropTransaction(eventData.Context);

    public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        changes.DropTransaction(eventData.Context);
        return Task.CompletedTask;
    }
}

/// <summary>DI and DbContext wiring of the live-change pipeline.</summary>
public static class LiveChangeRegistration
{
    public static IServiceCollection AddBackendConfigurationLiveChanges(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton<LiveChangeWarnings>();
        services.AddSingleton<ILiveChangeResolver, LiveChangeResolver>();
        services.TryAddSingleton<TaskReminders.TaskReminderSignals>(); // the publisher's reminder gate (package 12)
        services.AddSingleton<LiveChangePublisher>();
        services.AddSingleton<ILiveChangeTargets, LiveChangeTargets>();
        services.AddSingleton<LiveChangeInterceptor>();
        services.AddSingleton<LiveChangeTransactionInterceptor>();
        services.AddScoped<LiveChangeCollector>();
        services.AddScoped<ILiveChanges>(sp => sp.GetRequiredService<LiveChangeCollector>());
        return services;
    }

    /// <summary>
    /// Adds the two interceptors to a context's options. They are singletons,
    /// so this is safe on the pooled contexts, whose options are built once.
    /// </summary>
    public static DbContextOptionsBuilder AddLiveChangeInterceptors(this DbContextOptionsBuilder options,
        IServiceProvider services) =>
        options.AddInterceptors(
            services.GetRequiredService<LiveChangeInterceptor>(),
            services.GetRequiredService<LiveChangeTransactionInterceptor>());
}
