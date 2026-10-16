#nullable enable
using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;

namespace BackendConfiguration.Pn.Services.ArchiveAccess;

/// <summary>
/// Runs read-access writes all-or-nothing (as FileArchiver does): a partial write must never widen access, e.g. own
/// access saved with its old groups deleted and the new ones not yet created reads as open. Retried by the execution
/// strategy, so <paramref name="work"/> must (re)load what it writes.
/// </summary>
public static class ArchiveTransaction
{
    public static async Task RunAsync(BackendConfigurationPnDbContext db, Func<Task> work)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        var attempt = 0;
        await strategy.ExecuteAsync(async () =>
        {
            if (attempt++ > 0)
            {
                db.ChangeTracker.Clear();
            }

            await using var tx = await db.Database.BeginTransactionAsync();
            await work();
            await tx.CommitAsync();
        });
    }
}
