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

    public async Task<OperationDataResult<ArchiveFolderModel>> CreateAsync(ArchiveFolderCreateModel model)
    {
        await access.GetEnabledContextAsync();
        var folders = await ArchiveFolderSnapshot.LoadAsync(dbContext);
        var name = (model.Name ?? "").Trim();
        var refusal = ValidateParent(model.ParentId, null, folders) ?? ValidateName(name, model.ParentId, null, folders);
        if (refusal != null)
        {
            return new OperationDataResult<ArchiveFolderModel>(false, refusal);
        }

        var folder = new ArchiveFolder
        {
            Name = name, ParentId = model.ParentId, CreatedByUserId = userService.UserId,
            UpdatedByUserId = userService.UserId
        };
        await folder.Create(dbContext);
        var node = new ArchiveFolderNode(folder.Id, folder.Name, folder.ParentId, false, []);
        var created = new ArchiveFolderSnapshot(folders.All.Append(node));
        var names = await access.GetGroupNamesAsync(created.EffectiveGroups(node.Id).ToList());
        return new OperationDataResult<ArchiveFolderModel>(true, Model(node, 0, created, names));
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
        if (parent == null || id == moving?.Id)
        {
            return localization.GetString("ArchiveFolderNotFound");
        }

        return parent.ParentId != null || (moving != null && folders.ChildrenOf(moving.Id).Any())
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
