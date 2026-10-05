/*
The MIT License (MIT)

Copyright (c) 2007 - 2026 Microting A/S

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

#nullable enable

namespace BackendConfiguration.Pn.Services.TailBite;

using System;
using System.Data;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;

/// <remarks>
/// Entities loaded before RunLockedAsync are detached when it starts (the change tracker is cleared).
/// Inside <c>work</c>, re-query every entity you check or mutate; do not Reload or mutate a pre-lock
/// instance, because PnBase.Update on a detached entity saves nothing.
/// </remarks>
public interface ITailBitePropertyLock
{
    Task<T> RunLockedAsync<T>(int propertyId, Func<Task<T>> work);  // throws TailBiteNotFoundException if no TailBiteProperty row
    Task RunLockedAsync(int propertyId, Func<Task> work);
}

public class TailBitePropertyLock(BackendConfigurationPnDbContext db) : ITailBitePropertyLock
{
    public Task RunLockedAsync(int propertyId, Func<Task> work)
        => RunLockedAsync(propertyId, async () => { await work(); return true; });

    public Task<T> RunLockedAsync<T>(int propertyId, Func<Task<T>> work)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(async () =>
        {
            // Detach anything a failed earlier attempt left pending, so a retry cannot insert it twice.
            // A CommitAsync with unknown outcome re-runs `work`; the ClientUuid/OpenKey unique indexes are the backstop.
            db.ChangeTracker.Clear();
            // READ COMMITTED: every statement after the lock sees all rows committed before the lock was granted.
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            try
            {
                // First statement of the transaction (§6.2). A plain statement rather than SqlQuery/FromSql, as in
                // EventDeployService's Compliances claim: EF composes those into a derived table, and the lock must
                // not depend on how the server treats FOR UPDATE inside one.
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT `Id` FROM `TailBiteProperties` WHERE `PropertyId` = {propertyId} FOR UPDATE");
                if (!await db.TailBiteProperties.AnyAsync(p => p.PropertyId == propertyId))
                    throw new TailBiteNotFoundException($"Tail bite is not set up for property {propertyId}.");
                var result = await work();
                await tx.CommitAsync();
                return result;
            }
            catch
            {
                db.ChangeTracker.Clear();
                try { await tx.RollbackAsync(); }
                catch { /* the original exception is the one that matters; a dead connection rolls back on its own */ }
                throw;
            }
        });
    }
}
