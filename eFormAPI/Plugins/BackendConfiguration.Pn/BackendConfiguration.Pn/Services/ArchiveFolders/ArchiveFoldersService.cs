#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.ArchiveFolders;
using BackendConfiguration.Pn.Services.ArchiveAccess;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using Microsoft.EntityFrameworkCore;
using static Microting.eForm.Infrastructure.Constants.Constants.WorkflowStates;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.eFormApi.BasePn.Infrastructure.Models.Common;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;

namespace BackendConfiguration.Pn.Services.ArchiveFolders;

public interface IArchiveFoldersService
{
    Task<OperationDataResult<List<ArchiveFolderModel>>> IndexAsync(List<int> propertyIds);

    Task<OperationDataResult<ArchiveFolderModel>> CreateAsync(ArchiveFolderCreateModel model);

    /// <summary>Rename and/or move (D2, D12). Returns how many documents' effective access changed (D11).</summary>
    Task<OperationDataResult<AccessChangeModel>> UpdateAsync(int id, ArchiveFolderUpdateModel model);

    /// <summary>Soft delete; only an empty, non-default folder (D12).</summary>
    Task<OperationResult> DeleteAsync(int id);

    /// <summary>Sets the folder's own read groups; empty = open. Not on the default folder (D12).</summary>
    Task<OperationDataResult<AccessChangeModel>> UpdateAccessAsync(int id, ArchiveFolderAccessModel model);
}

/// <summary>
/// The tenant-wide archive folder tree (D1, D2, D5, D12, D16). At most two levels, enforced here. A disabled caller
/// surfaces as <see cref="ArchiveAccountDisabledException"/>, which ArchiveAccessFilterAttribute turns into 403:
/// nothing here catches it.
/// </summary>
public class ArchiveFoldersService(
    BackendConfigurationPnDbContext dbContext,
    IArchiveAccessService access,
    IBackendConfigurationLocalizationService localization,
    IUserService userService) : IArchiveFoldersService
{
    public const int MaxNameLength = 250;

    public async Task<OperationDataResult<List<ArchiveFolderModel>>> IndexAsync(List<int> propertyIds)
    {
        var ctx = await access.GetEnabledContextAsync();
        var folders = await ArchiveFolderSnapshot.LoadAsync(dbContext);
        var ids = propertyIds ?? [];
        var counted = access.Readable(dbContext.Files.Where(f => f.WorkflowState != Removed), ctx);
        if (ids.Count > 0)
        {
            counted = counted.Where(f => f.PropertyFiles.Any(p => p.WorkflowState != Removed && ids.Contains(p.PropertyId)));
        }

        // One grouped query for every count (spec section 11). A NULL FolderId lies in the default folder (10.0.59).
        var defaultId = folders.DefaultFolderId ?? 0;
        var direct = await counted.GroupBy(f => f.FolderId ?? defaultId)
            .Select(g => new { FolderId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.FolderId, x => x.Count);
        var visible = await access.VisibleFolderIdsAsync(ctx, folders);
        var names = await access.GetGroupNamesAsync(folders.All.SelectMany(f => f.OwnGroupIds).Distinct().ToList());

        var result = new List<ArchiveFolderModel>();
        foreach (var top in folders.All.Where(f => f.ParentId == null && visible.Contains(f.Id))
                     .OrderByDescending(f => f.IsDefault).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            var children = folders.ChildrenOf(top.Id).ToList();
            var count = direct.GetValueOrDefault(top.Id) + children.Sum(c => direct.GetValueOrDefault(c.Id));
            result.Add(Model(top, count, folders, names));
            result.AddRange(children.Where(c => visible.Contains(c.Id))
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .Select(c => Model(c, direct.GetValueOrDefault(c.Id), folders, names)));
        }

        return new OperationDataResult<List<ArchiveFolderModel>>(true, result);
    }

    // Every tree or access change below runs in ArchiveTransaction and starts by locking the folders it changes
    // (ArchiveFolderLock: exclusive, one statement, ascending Id), then reloads the tree and validates on it. The
    // validation is inside, not before, so two concurrent requests can never both pass on the same old tree (e.g. A
    // under B and B under A, which would save a cycle) and a retry never commits a stale decision.

    public async Task<OperationDataResult<ArchiveFolderModel>> CreateAsync(ArchiveFolderCreateModel model)
    {
        await access.GetEnabledContextAsync();
        var name = (model.Name ?? "").Trim();
        return await InTransactionAsync(async () =>
        {
            // Exclusive, not shared, on the parent: the two-level rule also needs it not to move under another folder
            // meanwhile. A top-level folder has no parent row to lock, so two top-level folders created with the
            // same name at the same instant are not serialised (a duplicate name, never a broken tree).
            await ArchiveFolderLock.ExclusiveAsync(dbContext, [model.ParentId]);
            var folders = await ArchiveFolderSnapshot.LoadAsync(dbContext);
            Refuse(ValidateParent(model.ParentId, null, folders) ?? ValidateName(name, model.ParentId, null, folders));

            var folder = new ArchiveFolder
            {
                Name = name, ParentId = model.ParentId, CreatedByUserId = userService.UserId,
                UpdatedByUserId = userService.UserId
            };
            await folder.Create(dbContext);
            var node = new ArchiveFolderNode(folder.Id, folder.Name, folder.ParentId, false, []);
            var created = new ArchiveFolderSnapshot(folders.All.Append(node));
            var names = await access.GetGroupNamesAsync(created.EffectiveGroups(node.Id).ToList());
            return Model(node, 0, created, names);
        });
    }

    public async Task<OperationDataResult<AccessChangeModel>> UpdateAsync(int id, ArchiveFolderUpdateModel model)
    {
        await access.GetEnabledContextAsync();
        // Read now only to know which parent to lock; everything is decided on the tree reloaded under the locks.
        var (_, current) = await LoadExistingAsync(id);
        return await InTransactionAsync(async () =>
        {
            // The folder, its old parent and its new parent.
            await ArchiveFolderLock.ExclusiveAsync(dbContext,
                [id, current.ParentId, model.MoveToTop ? null : model.ParentId]);
            var (before, node) = await LoadExistingAsync(id);
            var parentId = model.MoveToTop ? null : model.ParentId ?? node.ParentId;
            var name = model.Name?.Trim() ?? node.Name;
            var moving = parentId != node.ParentId;
            if (!moving && name == node.Name)
            {
                return new AccessChangeModel();
            }

            if (moving && node.IsDefault)
            {
                RefuseKey("ArchiveDefaultFolderCannotMove");
            }

            Refuse((moving ? ValidateParent(parentId, node, before) : null)
                   ?? ValidateName(name, parentId, node.Id, before));

            var documents = await DocumentsUnderAsync(id);
            var folder = await dbContext.ArchiveFolders.SingleAsync(f => f.Id == id);
            folder.Name = name;
            folder.ParentId = parentId;
            folder.UpdatedByUserId = userService.UserId;
            await folder.Update(dbContext);
            return await AccessChangedAsync(documents, before);
        });
    }

    public async Task<OperationResult> DeleteAsync(int id)
    {
        await access.GetEnabledContextAsync();
        // One transaction: a folder whose read tags are gone but which is still live would read as open. The lock
        // keeps uploads, filings and moves into it (shared locks) and new subfolders out until it is gone.
        var deleted = await InTransactionAsync(async () =>
        {
            await ArchiveFolderLock.ExclusiveAsync(dbContext, [id]);
            var folder = await dbContext.ArchiveFolders.FirstOrDefaultAsync(f => f.Id == id && f.WorkflowState != Removed)
                         ?? throw new ArchiveNotFoundException();
            if (folder.IsDefault)
            {
                RefuseKey("ArchiveDefaultFolderCannotDelete");
            }

            // Restricted documents block too; NULL-folder rows are default-folder rows, refused above.
            var documents = await dbContext.Files.CountAsync(f => f.FolderId == id && f.WorkflowState != Removed);
            var subfolders = await dbContext.ArchiveFolders.CountAsync(f => f.ParentId == id && f.WorkflowState != Removed);
            if (documents + subfolders > 0)
            {
                RefuseKey("ArchiveFolderNotEmpty", documents, subfolders);
            }

            await ArchiveReadTagSync.SyncFolderAsync(dbContext, id, [], userService.UserId);
            folder.UpdatedByUserId = userService.UserId;
            await folder.Delete(dbContext);
            return true;
        });
        // A plain OperationResult, as before: no model in the response.
        return new OperationResult(deleted.Success, deleted.Message);
    }

    public async Task<OperationDataResult<AccessChangeModel>> UpdateAccessAsync(int id, ArchiveFolderAccessModel model)
    {
        await access.GetEnabledContextAsync();
        var tagIds = (model.TagIds ?? []).Distinct().ToList();
        // One transaction: the sync deletes the old groups before creating the new ones, and a folder left with none
        // in between would read as open. A partial write must never widen access. Under the lock the sync starts from
        // the folder's live groups, so two concurrent changes apply one after the other instead of adding up.
        return await InTransactionAsync(async () =>
        {
            await ArchiveFolderLock.ExclusiveAsync(dbContext, [id]);
            var (before, node) = await LoadExistingAsync(id);
            if (node.IsDefault && tagIds.Count > 0)
            {
                RefuseKey("ArchiveDefaultFolderCannotRestrict");
            }

            // Only newly added groups need live members; a kept group that lost its last member stays.
            if (!await access.AreAssignableGroupsAsync(tagIds, node.OwnGroupIds))
            {
                RefuseKey("ArchiveUnknownGroup");
            }

            var documents = await DocumentsUnderAsync(id);
            await ArchiveReadTagSync.SyncFolderAsync(dbContext, id, tagIds, userService.UserId);
            return await AccessChangedAsync(documents, before);
        });
    }

    /// <summary>A refusal found inside a transaction: thrown before any write, so the transaction commits nothing.</summary>
    private sealed class FolderRefusedException(string message) : Exception(message);

    private static void Refuse(string? localizedMessage)
    {
        if (localizedMessage != null)
        {
            throw new FolderRefusedException(localizedMessage);
        }
    }

    private void RefuseKey(string key, params object[] args) =>
        throw new FolderRefusedException(args.Length == 0 ? localization.GetString(key) : localization.GetString(key, args));

    /// <summary>Runs <paramref name="work"/> in ArchiveTransaction; a <see cref="Refuse"/> inside becomes success=false.</summary>
    private async Task<OperationDataResult<T>> InTransactionAsync<T>(Func<Task<T>> work)
    {
        try
        {
            return new OperationDataResult<T>(true, await ArchiveTransaction.RunAsync(dbContext, work));
        }
        catch (FolderRefusedException e)
        {
            return new OperationDataResult<T>(false, e.Message);
        }
    }

    /// <summary>The live folder tree and the live folder <paramref name="id"/> in it; unknown → 404.</summary>
    private async Task<(ArchiveFolderSnapshot Folders, ArchiveFolderNode Node)> LoadExistingAsync(int id)
    {
        var folders = await ArchiveFolderSnapshot.LoadAsync(dbContext);
        return (folders, folders.Find(id) ?? throw new ArchiveNotFoundException());
    }

    /// <summary>
    /// The live documents in the folder and in its subfolders, with their access inputs. `FolderId == folderId` misses
    /// NULL (default-folder) rows; that is safe because the default folder can never be restricted (D12), so its
    /// documents' effective access is open before and after any allowed change.
    /// </summary>
    private async Task<List<ArchiveFileAccessRow>> DocumentsUnderAsync(int folderId)
    {
        var ids = await dbContext.Files
            .Where(f => f.WorkflowState != Removed && (f.FolderId == folderId || f.Folder.ParentId == folderId))
            .Select(f => f.Id)
            .ToListAsync();
        return await ArchiveAccessRules.LoadFileRowsAsync(dbContext, ids);
    }

    /// <summary>D11: how many documents' effective groups differ between <paramref name="before"/> and the saved tree.</summary>
    private async Task<AccessChangeModel> AccessChangedAsync(List<ArchiveFileAccessRow> documents,
        ArchiveFolderSnapshot before)
    {
        var after = await ArchiveFolderSnapshot.LoadAsync(dbContext);
        var changed = ArchiveAccessRules.CountAccessChanged(documents, d => d.Effective(before), d => d.Effective(after));
        return new AccessChangeModel { AccessChanged = changed };
    }

    private static ArchiveFolderModel Model(ArchiveFolderNode folder, int count, ArchiveFolderSnapshot folders,
        IReadOnlyDictionary<int, string> names)
    {
        var effective = folders.EffectiveGroups(folder.Id);
        return new ArchiveFolderModel
        {
            Id = folder.Id,
            Name = folder.Name,
            ParentId = folder.ParentId,
            IsDefault = folder.IsDefault,
            Count = count,
            ReadGroups = effective
                .Select(id => new CommonTagModel { Id = id, Name = names.GetValueOrDefault(id, "") }).ToList(),
            OwnGroups = folder.OwnGroupIds.Count > 0,
            Restricted = effective.Count > 0
        };
    }

    /// <summary>
    /// The parent must be a live top folder (2 levels, D2), not the folder itself, and a folder with subfolders
    /// cannot get a parent.
    /// </summary>
    private string? ValidateParent(int? parentId, ArchiveFolderNode? moving, ArchiveFolderSnapshot folders)
    {
        if (parentId is not { } id)
        {
            return null;
        }

        var parent = folders.Find(id);
        if (parent == null)
        {
            return localization.GetString("ArchiveFolderNotFound");
        }

        // Under itself would nest a folder in its own level: refused as a level violation, not "not found".
        return id == moving?.Id || parent.ParentId != null || (moving != null && folders.ChildrenOf(moving.Id).Any())
            ? localization.GetString("ArchiveMaxTwoLevels")
            : null;
    }

    /// <summary>1–250 characters after trimming; unique among live siblings, ignoring case.</summary>
    private string? ValidateName(string name, int? parentId, int? selfId, ArchiveFolderSnapshot folders)
    {
        if (name.Length is 0 or > MaxNameLength)
        {
            return localization.GetString("ArchiveFolderNameInvalid");
        }

        var taken = folders.All.Any(f => f.Id != selfId && f.ParentId == parentId
                                         && string.Equals(f.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));
        if (!taken)
        {
            return null;
        }

        return parentId is { } p
            ? localization.GetString("ArchiveFolderNameExistsIn", folders.Find(p)?.Name ?? "")
            : localization.GetString("ArchiveFolderNameExistsTopLevel");
    }
}
