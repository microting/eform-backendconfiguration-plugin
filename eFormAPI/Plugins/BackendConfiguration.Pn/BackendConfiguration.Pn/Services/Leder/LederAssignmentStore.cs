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
/// The one owner of the Leder flag (D-L13): a LederAssignments row per EformUser id, for all time. The unique index on
/// EformUserId has no WorkflowState filter, so grant and revoke flip that one row between created and removed; the
/// version table (written by PnBase) is the audit trail. The flag alone does not make a Leder: ILederService also
/// requires the login to be live.
/// </summary>
public static class LederAssignmentStore
{
    /// <summary>Upserts the row of <paramref name="eformUserId"/> to created. An already live row is left as it is.</summary>
    public static async Task GrantAsync(BackendConfigurationPnDbContext db, int eformUserId, int actorUserId)
    {
        var row = await FindOrCreateAsync(db, eformUserId, actorUserId);
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
    /// The existing row (live or removed; the context has no query filters), or null when this call created it.
    /// A parallel grant that wins the unique EformUserId race is returned as the existing row.
    /// </summary>
    private static async Task<LederAssignment?> FindOrCreateAsync(BackendConfigurationPnDbContext db,
        int eformUserId, int actorUserId)
    {
        var row = await db.LederAssignments.FirstOrDefaultAsync(x => x.EformUserId == eformUserId);
        if (row != null)
        {
            return row;
        }

        var created = new LederAssignment
        {
            EformUserId = eformUserId, CreatedByUserId = actorUserId, UpdatedByUserId = actorUserId
        };
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
            return await db.LederAssignments.FirstAsync(x => x.EformUserId == eformUserId);
        }
    }

    /// <summary>The live row of <paramref name="eformUserId"/> → removed. No row, or already removed → no-op.</summary>
    public static async Task RevokeAsync(BackendConfigurationPnDbContext db, int eformUserId, int actorUserId)
    {
        var row = await Live(db).FirstOrDefaultAsync(x => x.EformUserId == eformUserId);
        if (row == null)
        {
            return;
        }

        row.UpdatedByUserId = actorUserId;
        await row.Delete(db);
    }

    /// <summary>The stored flag only (no liveness check).</summary>
    public static Task<bool> HasLiveRowAsync(BackendConfigurationPnDbContext db, int eformUserId) =>
        Live(db).AsNoTracking().AnyAsync(x => x.EformUserId == eformUserId);

    /// <summary>Every EformUserId with a live row: one query for a whole Medarbejdere list.</summary>
    public static async Task<HashSet<int>> LiveEformUserIdsAsync(BackendConfigurationPnDbContext db) =>
        (await Live(db).AsNoTracking()
            .Select(x => x.EformUserId)
            .ToListAsync()).ToHashSet();

    /// <summary>"Live" is defined here once: every row that is not removed.</summary>
    private static IQueryable<LederAssignment> Live(BackendConfigurationPnDbContext db) =>
        db.LederAssignments.Where(x => x.WorkflowState != Removed);
}
