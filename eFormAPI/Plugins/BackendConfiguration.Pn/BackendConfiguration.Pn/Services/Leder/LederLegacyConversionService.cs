#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.ArchiveAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure;
using Microting.EformAngularFrontendBase.Infrastructure.Data;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using static Microting.eForm.Infrastructure.Constants.Constants.WorkflowStates;

namespace BackendConfiguration.Pn.Services.Leder;

/// <summary>
/// D-L19: moves every live LederAssignments row still keyed by login (SiteId null, D-L13) to the sites of that login's
/// workers (actor = the row's UpdatedByUserId), then marks the legacy row removed. A row whose login or workers no
/// longer exist is only marked removed. Runs at plugin startup. Self-narrowing, so no marker row: a converted row is
/// removed, a second run finds nothing, and a run cut short repeats only the rows it did not finish: each row's grants
/// and its retirement commit together (a grant on an already flagged site is a no-op).
/// </summary>
public class LederLegacyConversionService(BackendConfigurationPnDbContext dbContext, BaseDbContext baseDbContext,
    IEFormCoreService coreHelper, ILogger<LederLegacyConversionService> logger)
{
    /// <returns>How many legacy rows were handled.</returns>
    public async Task<int> RunAsync()
    {
        var legacy = await LederAssignmentStore.LiveLegacyRowsAsync(dbContext);
        if (legacy.Count == 0)
        {
            return 0;
        }

        return await ConvertAsync(legacy.Select(x => x.Id).ToList());
    }

    /// <summary>
    /// Converts the legacy rows <paramref name="rowIds"/>, each in its own transaction. Internal so a test can hand it
    /// an id that another pod retired after the list was read.
    /// </summary>
    internal async Task<int> ConvertAsync(IReadOnlyList<int> rowIds)
    {
        var core = await coreHelper.GetCore();
        await using var sdk = core.DbContextHelper.GetDbContext();
        var handled = 0;
        foreach (var rowId in rowIds)
        {
            // One failing row must not block the rest; it stays live and is retried at the next start.
            try
            {
                if (await ConvertRowAsync(sdk, rowId))
                {
                    handled++;
                }
            }
            catch (Exception e)
            {
                dbContext.ChangeTracker.Clear();
                logger.LogWarning(e, "Leder legacy row {RowId} failed; it is retried at the next start", rowId);
            }
        }

        return handled;
    }

    /// <summary>
    /// The grants and the retirement of one legacy row commit together, under a lock on that row: a failure leaves
    /// neither, so the next start cannot replay grants over a revoke made in between, and a second pod starting at the
    /// same time waits for the lock, then finds the row retired and skips it. A retry after an unknown commit outcome
    /// finds the row retired too, so no "committing" guard is needed.
    /// </summary>
    /// <returns>False when the row was no longer live (another pod got there first).</returns>
    private Task<bool> ConvertRowAsync(MicrotingDbContext sdk, int rowId)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(async () =>
        {
            // A retried attempt starts on a clean tracker, so nothing a failed attempt left pending is saved twice.
            dbContext.ChangeTracker.Clear();
            // READ COMMITTED, as TailBitePropertyLock: every statement after the lock sees what other pods committed
            // before it was granted (the re-read below, and the site row GrantSiteAsync re-reads after a duplicate key).
            await using var tx = await dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            // A plain statement, as TailBitePropertyLock: FOR UPDATE must not end up inside an EF derived table.
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT `Id` FROM `LederAssignments` WHERE `Id` = {rowId} FOR UPDATE");
            // Tracked: PnBase.Delete saves only tracked changes.
            var row = await dbContext.LederAssignments.SingleOrDefaultAsync(x => x.Id == rowId);
            if (row == null || row.WorkflowState == Removed || row.SiteId != null)
            {
                logger.LogDebug("Leder legacy row {RowId} is no longer live; skipped", rowId);
                return false;
            }

            var siteIds = await SitesOfLoginAsync(sdk, row.EformUserId);
            foreach (var siteId in siteIds)
            {
                await LederAssignmentStore.GrantSiteAsync(dbContext, siteId, row.UpdatedByUserId);
            }

            await row.Delete(dbContext);
            await tx.CommitAsync();
            // Ids only, never the e-mail.
            logger.LogInformation(
                "Leder legacy row {RowId} of login {LoginId}: {SiteCount} site(s) flagged, legacy row removed",
                row.Id, row.EformUserId, siteIds.Count);
            return true;
        });
    }

    /// <summary>The sites of the login's workers; none for a deleted login or one without an e-mail.</summary>
    private async Task<List<int>> SitesOfLoginAsync(MicrotingDbContext sdk, int? eformUserId)
    {
        var email = await baseDbContext.Users.AsNoTracking()
            .Where(u => u.Id == eformUserId)
            .Select(u => u.Email)
            .FirstOrDefaultAsync();
        return string.IsNullOrWhiteSpace(email) ? [] : await CallerSites.SitesOfWorkersAsync(sdk, email);
    }
}
