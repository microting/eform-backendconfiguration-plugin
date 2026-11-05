#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using static Microting.eForm.Infrastructure.Constants.Constants.WorkflowStates;

namespace BackendConfiguration.Pn.Services.Leder;

/// <summary>
/// The one owner of the Leder flag (D-L15): a LederAssignments row per SDK site (the Sites.Id of a Medarbejdere row),
/// for all time. The unique index on SiteId has no WorkflowState filter, so grant and revoke flip that one row between
/// created and removed; the version table (written by PnBase) is the audit trail. The flag alone does not make a
/// Leder: ILederService also requires an active login whose live sites include the flagged one (D-L18). Rows keyed by
/// EformUserId (D-L13) are legacy; LederLegacyConversionService moves them to sites at startup (D-L19).
/// </summary>
public static class LederAssignmentStore
{
    /// <summary>Upserts the row of <paramref name="siteId"/> to created. An already live row is left as it is.</summary>
    public static async Task GrantSiteAsync(BackendConfigurationPnDbContext db, int siteId, int actorUserId)
    {
        var row = await FindOrCreateSiteRowAsync(db, siteId, actorUserId);
        if (row == null || row.WorkflowState != Removed)
        {
            // Null: we just created it live. Otherwise an already live row is left as it is.
            return;
        }

        row.WorkflowState = Created;
        row.UpdatedByUserId = actorUserId;
        await row.Update(db);
    }

    /// <summary>
    /// The site's existing row (live or removed; the context has no query filters), or null when this call created it.
    /// A parallel grant that wins the unique SiteId race is returned as the existing row.
    /// </summary>
    private static async Task<LederAssignment?> FindOrCreateSiteRowAsync(BackendConfigurationPnDbContext db,
        int siteId, int actorUserId)
    {
        var row = await db.LederAssignments.FirstOrDefaultAsync(x => x.SiteId == siteId);
        if (row != null)
        {
            return row;
        }

        var created = new LederAssignment { SiteId = siteId, CreatedByUserId = actorUserId, UpdatedByUserId = actorUserId };
        try
        {
            await created.Create(db);
            return null;
        }
        catch (DbUpdateException e) when (e.InnerException is MySqlException
                                          {
                                              ErrorCode: MySqlErrorCode.DuplicateKeyEntry
                                          })
        {
            // A failed save leaves our row Added, and the next save would replay it: detach, continue with theirs.
            db.Entry(created).State = EntityState.Detached;
            return await db.LederAssignments.FirstAsync(x => x.SiteId == siteId);
        }
    }

    /// <summary>The live row of <paramref name="siteId"/> → removed. No row, or already removed → no-op.</summary>
    public static async Task RevokeSiteAsync(BackendConfigurationPnDbContext db, int siteId, int actorUserId)
    {
        var row = await Live(db).FirstOrDefaultAsync(x => x.SiteId == siteId);
        if (row == null)
        {
            return;
        }

        row.UpdatedByUserId = actorUserId;
        await row.Delete(db);
    }

    /// <summary>The stored flag of one site (no liveness check).</summary>
    public static Task<bool> IsSiteLederAsync(BackendConfigurationPnDbContext db, int siteId) =>
        Live(db).AsNoTracking().AnyAsync(x => x.SiteId == siteId);

    /// <summary>True when any site of the tenant has a live flag: lets a tenant without Leders skip the SDK lookup.</summary>
    public static Task<bool> AnySiteLederAsync(BackendConfigurationPnDbContext db) =>
        Live(db).AsNoTracking().AnyAsync(x => x.SiteId != null);

    /// <summary>True when any of <paramref name="siteIds"/> has a live row: one indexed query (D-L18).</summary>
    public static Task<bool> AnyLiveSiteAsync(BackendConfigurationPnDbContext db, IReadOnlyCollection<int> siteIds)
    {
        if (siteIds.Count == 0)
        {
            return Task.FromResult(false);
        }

        return LiveForSites(db, siteIds).AnyAsync();
    }

    /// <summary>The flagged ones of <paramref name="siteIds"/>: one query for a whole Medarbejdere list.</summary>
    public static async Task<HashSet<int>> LiveSiteIdsAsync(BackendConfigurationPnDbContext db,
        IReadOnlyCollection<int> siteIds)
    {
        if (siteIds.Count == 0)
        {
            return [];
        }

        return (await LiveForSites(db, siteIds).Select(x => x.SiteId!.Value).ToListAsync()).ToHashSet();
    }

    /// <summary>Live rows still keyed by login (SiteId null), oldest first, tracked (the conversion reads their ids, then locks and re-reads each).</summary>
    public static Task<List<LederAssignment>> LiveLegacyRowsAsync(BackendConfigurationPnDbContext db) =>
        Live(db).Where(x => x.SiteId == null).OrderBy(x => x.Id).ToListAsync();

    /// <summary>"Live" is defined here once: every row that is not removed.</summary>
    private static IQueryable<LederAssignment> Live(BackendConfigurationPnDbContext db) =>
        db.LederAssignments.Where(x => x.WorkflowState != Removed);

    /// <summary>Live rows of <paramref name="siteIds"/>, untracked.</summary>
    private static IQueryable<LederAssignment> LiveForSites(BackendConfigurationPnDbContext db,
        IReadOnlyCollection<int> siteIds)
    {
        var ids = siteIds.ToList();
        return Live(db).AsNoTracking().Where(x => x.SiteId != null && ids.Contains(x.SiteId.Value));
    }
}
