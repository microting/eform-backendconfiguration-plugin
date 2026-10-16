#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using static Microting.eForm.Infrastructure.Constants.Constants.WorkflowStates;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;

namespace BackendConfiguration.Pn.Services.ArchiveAccess;

/// <summary>A live archive folder with its OWN live read groups (not inherited), ascending.</summary>
public sealed record ArchiveFolderNode(int Id, string Name, int? ParentId, bool IsDefault, IReadOnlyList<int> OwnGroupIds);

/// <summary>
/// Every live archive folder, read in one query (a tenant has tens of folders). The in-memory half of the access
/// rule (spec section 3) lives here and in <see cref="ArchiveAccessRules"/>.
/// </summary>
public sealed class ArchiveFolderSnapshot(IEnumerable<ArchiveFolderNode> folders)
{
    public const string PathSeparator = " › ";

    private readonly Dictionary<int, ArchiveFolderNode> _byId = folders.ToDictionary(f => f.Id);

    public IReadOnlyCollection<ArchiveFolderNode> All => _byId.Values;

    /// <summary>
    /// The default folder: the lowest-id live IsDefault folder (as ArchiveDefaultFolder.FindIdAsync), or null when
    /// none exists. A document whose FolderId is NULL lies here.
    /// </summary>
    public int? DefaultFolderId => _byId.Values.Where(f => f.IsDefault).Select(f => (int?)f.Id).Min();

    public static async Task<ArchiveFolderSnapshot> LoadAsync(BackendConfigurationPnDbContext db)
    {
        var rows = await db.ArchiveFolders.AsNoTracking()
            .Where(f => f.WorkflowState != Removed)
            .Select(f => new
            {
                f.Id,
                f.Name,
                f.ParentId,
                f.IsDefault,
                Groups = f.ReadTags
                    .Where(t => t.WorkflowState != Removed)
                    .Select(t => t.SdkTagId)
                    .ToList()
            })
            .ToListAsync();
        return new ArchiveFolderSnapshot(rows.Select(r =>
            new ArchiveFolderNode(r.Id, r.Name, r.ParentId, r.IsDefault, ArchiveAccessRules.GroupIds(r.Groups))));
    }

    public ArchiveFolderNode? Find(int id) => _byId.GetValueOrDefault(id);

    public IEnumerable<ArchiveFolderNode> ChildrenOf(int id) => _byId.Values.Where(f => f.ParentId == id);

    /// <summary>
    /// effectiveGroups(folder): its own groups, else its parent's own groups (a top folder has no parent), else none
    /// (open). Unknown ids are open: a live document never lies in a removed folder, because a folder that holds
    /// documents or subfolders cannot be deleted.
    /// </summary>
    public IReadOnlyList<int> EffectiveGroups(int folderId)
    {
        if (!_byId.TryGetValue(folderId, out var folder))
        {
            return [];
        }

        if (folder.OwnGroupIds.Count > 0)
        {
            return folder.OwnGroupIds;
        }

        return folder.ParentId is { } parentId && _byId.TryGetValue(parentId, out var parent) ? parent.OwnGroupIds : [];
    }

    /// <summary>["Drift", "Fakturaer"]; empty for an unknown id.</summary>
    public IReadOnlyList<string> Path(int folderId)
    {
        if (!_byId.TryGetValue(folderId, out var folder))
        {
            return [];
        }

        return folder.ParentId is { } parentId && _byId.TryGetValue(parentId, out var parent)
            ? [parent.Name, folder.Name]
            : [folder.Name];
    }
}
