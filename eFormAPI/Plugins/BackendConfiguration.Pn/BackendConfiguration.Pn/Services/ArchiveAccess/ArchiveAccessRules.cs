#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.ArchiveFolders;
using Microsoft.EntityFrameworkCore;
using static Microting.eForm.Infrastructure.Constants.Constants.WorkflowStates;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;

namespace BackendConfiguration.Pn.Services.ArchiveAccess;

/// <summary>A document's access inputs: its folder, whether it has own access, and its own live groups.</summary>
public sealed record ArchiveFileAccessRow(int FileId, int FolderId, bool OwnAccess, IReadOnlyList<int> OwnGroupIds)
{
    /// <summary>effectiveGroups(file) where it lies now.</summary>
    public IReadOnlyList<int> Effective(ArchiveFolderSnapshot folders) => EffectiveIn(FolderId, folders);

    /// <summary>effectiveGroups(file) if it lay in <paramref name="folderId"/> (D11: own access travels, folder access does not).</summary>
    public IReadOnlyList<int> EffectiveIn(int folderId, ArchiveFolderSnapshot folders) =>
        OwnAccess ? OwnGroupIds : folders.EffectiveGroups(folderId);
}

/// <summary>The in-memory half of the read rule (spec section 3).</summary>
public static class ArchiveAccessRules
{
    /// <summary>canRead: disabled → no; admin → yes; open → yes; else any shared group.</summary>
    public static bool CanRead(ArchiveAccessContext ctx, IReadOnlyCollection<int> effectiveGroups) =>
        !ctx.IsDisabled
        && (ctx.IsAdmin || effectiveGroups.Count == 0 || effectiveGroups.Any(g => ctx.UserGroupIds.Contains(g)));

    public static bool SameGroups(IEnumerable<int> a, IEnumerable<int> b) => new HashSet<int>(a).SetEquals(b);

    /// <summary>Live read-tag ids as stored on a node or row: distinct, ascending.</summary>
    internal static List<int> GroupIds(IEnumerable<int> liveTagIds) => liveTagIds.Distinct().Order().ToList();

    /// <summary>
    /// The live documents among <paramref name="fileIds"/> with their access inputs, in one query. A NULL FolderId is
    /// resolved to the default folder (one extra query, only while such rows exist).
    /// </summary>
    public static async Task<List<ArchiveFileAccessRow>> LoadFileRowsAsync(BackendConfigurationPnDbContext db,
        IReadOnlyCollection<int> fileIds)
    {
        if (fileIds.Count == 0)
        {
            return [];
        }

        var ids = fileIds.Distinct().ToList();
        var rows = await db.Files.AsNoTracking()
            .Where(f => ids.Contains(f.Id) && f.WorkflowState != Removed)
            .Select(f => new
            {
                f.Id,
                f.FolderId,
                f.OwnAccess,
                Groups = f.ReadTags
                    .Where(t => t.WorkflowState != Removed)
                    .Select(t => t.SdkTagId)
                    .ToList()
            })
            .ToListAsync();
        var defaultId = rows.Any(r => r.FolderId == null) ? (await ArchiveDefaultFolder.FindIdAsync(db) ?? 0) : 0;
        return rows.Select(r => new ArchiveFileAccessRow(r.Id, r.FolderId ?? defaultId, r.OwnAccess,
                GroupIds(r.Groups)))
            .ToList();
    }
}
