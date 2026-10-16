#nullable enable
using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;

namespace BackendConfiguration.Pn.Services.ArchiveAccess;

/// <summary>
/// The single place archive writes start a transaction (FileArchiver included). Runs read-access writes
/// all-or-nothing: a partial write must never widen access, e.g. own access saved with its old groups deleted and the
/// new ones not yet created reads as open. Retried by the execution strategy, so <c>work</c> must (re)load what it
/// writes.
/// <para>
/// Concurrency protocol (<see cref="ArchiveFolderLock"/>): <c>work</c> first takes its row locks, then reloads and
/// re-validates everything its decision rests on, then writes. Checks made before the transaction are only fast early
/// refusals; the authoritative check is the one inside, so neither a retry nor a concurrent request can commit a
/// decision made on stale data. A refusal found inside is returned (or thrown) before any write.
/// </para>
/// </summary>
public static class ArchiveTransaction
{
    public static Task RunAsync(BackendConfigurationPnDbContext db, Func<Task> work) =>
        RunAsync(db, async () =>
        {
            await work();
            return true;
        });

    public static async Task<T> RunAsync<T>(BackendConfigurationPnDbContext db, Func<Task<T>> work)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        var attempt = 0;
        return await strategy.ExecuteAsync(async () =>
        {
            if (attempt++ > 0)
            {
                db.ChangeTracker.Clear();
            }

            await using var tx = await db.Database.BeginTransactionAsync();
            var result = await work();
            await tx.CommitAsync();
            return result;
        });
    }
}
