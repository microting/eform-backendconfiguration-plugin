#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using File = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.File;

namespace BackendConfiguration.Pn.Services.ArchiveAccess;

/// <summary>
/// InnoDB row locks on archive folders and documents, taken inside <see cref="ArchiveTransaction.RunAsync{T}"/> and
/// held until it commits. The one protocol every archive write follows:
/// <list type="bullet">
/// <item>Tree and access changes of a folder (create under it, rename, move, delete, set read groups) lock the folders
/// they change exclusively; adding content to a folder (upload, inbox filing, document move) locks it shared. So a
/// folder cannot be deleted, moved or restricted while a document is being put into it, and two tree changes on the
/// same folders run one after the other.</item>
/// <item>Lock order: all folder locks of a transaction in ONE statement, ascending Id (InnoDB takes them in index
/// order), then document locks, ascending Id. Two archive transactions therefore never wait on each other in a
/// cycle.</item>
/// <item>The locks are the first statements of the transaction. InnoDB fixes a REPEATABLE READ snapshot at the
/// first plain read, so every re-validation read after the locks sees what the previous holder committed.</item>
/// </list>
/// Production is a Galera cluster behind one endpoint: these locks are local to the node that serves the writes,
/// which is the node every archive write goes to.
/// </summary>
public static class ArchiveFolderLock
{
    /// <summary>For changes to the folders themselves: tree mutations, deletion, read groups.</summary>
    public static Task ExclusiveAsync(BackendConfigurationPnDbContext db, IEnumerable<int?> folderIds) =>
        LockAsync<ArchiveFolder>(db, folderIds, "FOR UPDATE");

    /// <summary>For adding content to folders: the folders stay live and keep their access until commit.</summary>
    public static Task SharedAsync(BackendConfigurationPnDbContext db, IEnumerable<int?> folderIds) =>
        LockAsync<ArchiveFolder>(db, folderIds, "LOCK IN SHARE MODE");

    /// <summary>For changes to documents' folder or access; after the folder locks.</summary>
    public static Task DocumentsExclusiveAsync(BackendConfigurationPnDbContext db, IEnumerable<int> fileIds) =>
        LockAsync<File>(db, fileIds.Select(id => (int?)id), "FOR UPDATE");

    private static async Task LockAsync<TEntity>(BackendConfigurationPnDbContext db, IEnumerable<int?> ids, string mode)
    {
        if (db.Database.CurrentTransaction == null)
        {
            // A lock outside a transaction is released at once and protects nothing.
            throw new InvalidOperationException("Archive row locks are taken inside ArchiveTransaction.RunAsync only.");
        }

        var sorted = ids.OfType<int>().Where(id => id > 0).Distinct().Order().ToList();
        if (sorted.Count == 0)
        {
            return;
        }

        var table = db.Model.FindEntityType(typeof(TEntity))!.GetTableName();
        var placeholders = string.Join(", ", sorted.Select((_, i) => $"{{{i}}}"));
        // Ids are bound as parameters; only the EF table name and a lock-clause literal are spliced in.
        var sql = $"SELECT Id FROM `{table}` WHERE Id IN ({placeholders}) ORDER BY Id {mode}";
        await db.Database.ExecuteSqlRawAsync(sql, sorted.Cast<object>());
    }
}
