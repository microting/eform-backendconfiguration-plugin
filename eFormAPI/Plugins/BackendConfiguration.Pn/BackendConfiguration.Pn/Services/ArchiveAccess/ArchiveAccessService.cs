#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.Leder;
using BackendConfiguration.Pn.Services.WorkerTagMembership;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using static Microting.eForm.Infrastructure.Constants.Constants.WorkflowStates;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using File = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.File;

namespace BackendConfiguration.Pn.Services.ArchiveAccess;

/// <summary>
/// The one owner of archive read access (spec section 3). Scoped: the caller context is resolved once per request,
/// so list, counts, single fetch, download and writes cannot disagree about who is calling.
/// </summary>
public interface IArchiveAccessService
{
    /// <summary>The caller's context; never throws for a disabled account (the filter reads it).</summary>
    Task<ArchiveAccessContext> GetContextAsync();

    /// <summary>The caller's context, or <see cref="ArchiveAccountDisabledException"/> when disabled (D7).</summary>
    Task<ArchiveAccessContext> GetEnabledContextAsync();

    /// <summary>The documents of <paramref name="files"/> the caller can read, as SQL.</summary>
    IQueryable<File> Readable(IQueryable<File> files, ArchiveAccessContext ctx);

    Task<List<int>> EffectiveGroupsAsync(File file);

    Task<List<int>> EffectiveGroupsAsync(ArchiveFolder folder);

    /// <summary>Folder visibility for one folder (see <see cref="VisibleFolderIdsAsync"/>).</summary>
    Task<bool> CanReadFolderOrContentsAsync(int folderId, ArchiveAccessContext ctx);

    /// <summary>
    /// The folders the caller sees: all for an admin or Leder; for others a folder they can read, or one holding a readable
    /// document or a visible subfolder — so no readable document is ever unreachable in the tree.
    /// </summary>
    Task<HashSet<int>> VisibleFolderIdsAsync(ArchiveAccessContext ctx, ArchiveFolderSnapshot folders);

    /// <summary>SDK tag names by id; ids without a tag are left out.</summary>
    Task<Dictionary<int, string>> GetGroupNamesAsync(IReadOnlyCollection<int> tagIds);

    /// <summary>The medarbejdergrupper with at least one live member (#1213).</summary>
    Task<HashSet<int>> GetLiveGroupIdsAsync();

    /// <summary>
    /// True when every group in <paramref name="requested"/> that is not already in <paramref name="current"/> is a
    /// live medarbejdergruppe. A group that lost its last member stays assignable where it already is.
    /// </summary>
    Task<bool> AreAssignableGroupsAsync(IReadOnlyCollection<int> requested, IReadOnlyCollection<int> current);
}

public class ArchiveAccessService(
    BackendConfigurationPnDbContext dbContext,
    IUserService userService,
    IEFormCoreService coreService,
    IWorkerTagMembershipService membership,
    ILederService leder,
    ILogger<ArchiveAccessService> logger) : IArchiveAccessService
{
    private Task<ArchiveAccessContext>? _context;

    public virtual Task<ArchiveAccessContext> GetContextAsync() => _context ??= ResolveContextAsync();

    public async Task<ArchiveAccessContext> GetEnabledContextAsync()
    {
        var ctx = await GetContextAsync();
        return ctx.IsDisabled ? throw new ArchiveAccountDisabledException() : ctx;
    }

    /// <summary>
    /// D7. Disabled = no current user, <c>EformUser.IsActive == false</c>, or every non-removed SDK worker with the
    /// caller's email is resigned — the same choice as GrpcSiteResolver (#1326): workers ordered by Resigned, the
    /// best one decides, so a re-hired worker whose old resigned row remains is NOT disabled.
    /// userGroups = live tags of the sites CallerSites resolves, via IWorkerTagMembershipService.
    /// </summary>
    private async Task<ArchiveAccessContext> ResolveContextAsync()
    {
        var user = await userService.GetCurrentUserAsync();
        if (user == null)
        {
            return Disabled(0, "no current user");
        }

        if (!user.IsActive)
        {
            return Disabled(user.Id, "EformUser.IsActive is false");
        }

        var isAdmin = userService.IsAdmin();
        if (string.IsNullOrWhiteSpace(user.Email))
        {
            // No address means no live site: only an admin can manage.
            return new ArchiveAccessContext(false, isAdmin, [], await leder.IsLederOrAdminAsync(knownLive: false));
        }

        var core = await coreService.GetCore();
        await using var sdk = core.DbContextHelper.GetDbContext();
        var sites = await CallerSites.ResolveAsync(sdk, user.Email);
        if (sites.AllWorkersResigned)
        {
            return Disabled(user.Id, "SDK worker is resigned");
        }

        // Asked only for enabled callers; the live-site count we already have is the Leder check's liveness.
        var canManage = await leder.IsLederOrAdminAsync(knownLive: sites.LiveSiteIds.Count > 0);
        var groups = await membership.GetTagIdsForSitesAsync(sites.LiveSiteIds);
        return new ArchiveAccessContext(false, isAdmin, groups.ToList(), canManage);
    }

    private ArchiveAccessContext Disabled(int userId, string reason)
    {
        // User id only — never the email.
        logger.LogInformation("Archive access refused for user {UserId}: {Reason}", userId, reason);
        return ArchiveAccessContext.Disabled;
    }

    public IQueryable<File> Readable(IQueryable<File> files, ArchiveAccessContext ctx)
    {
        if (ctx.IsDisabled)
        {
            return files.Where(_ => false);
        }

        if (ctx.CanManage)
        {
            return files;
        }

        var groups = ctx.UserGroupIds.ToList();
        // SQL twin of ArchiveAccessRules.CanRead over ArchiveFolderSnapshot.EffectiveGroups, pinned by ArchiveReadableTests.
        // A NULL FolderId without own access reads as an open top folder: the default folder, never restricted (D12).
        // A folder with contents cannot be deleted (D12), so reading a soft-deleted folder's tags matters only on
        // drifted data, where SQL is the stricter side.
        return files.Where(f =>
            (f.OwnAccess
             && (!f.ReadTags.Any(t => t.WorkflowState != Removed)
                 || f.ReadTags.Any(t => t.WorkflowState != Removed && groups.Contains(t.SdkTagId))))
            || (!f.OwnAccess
                && (f.Folder.ReadTags.Any(t => t.WorkflowState != Removed && groups.Contains(t.SdkTagId))
                    || (!f.Folder.ReadTags.Any(t => t.WorkflowState != Removed)
                        && (f.Folder.ParentId == null
                            || !f.Folder.Parent.ReadTags.Any(t => t.WorkflowState != Removed)
                            || f.Folder.Parent.ReadTags.Any(t =>
                                t.WorkflowState != Removed && groups.Contains(t.SdkTagId)))))));
    }

    public async Task<List<int>> EffectiveGroupsAsync(File file)
    {
        var folders = await ArchiveFolderSnapshot.LoadAsync(dbContext);
        var row = (await ArchiveAccessRules.LoadFileRowsAsync(dbContext, [file.Id])).SingleOrDefault();
        return row == null ? [] : row.Effective(folders).ToList();
    }

    public async Task<List<int>> EffectiveGroupsAsync(ArchiveFolder folder) =>
        (await ArchiveFolderSnapshot.LoadAsync(dbContext)).EffectiveGroups(folder.Id).ToList();

    public async Task<bool> CanReadFolderOrContentsAsync(int folderId, ArchiveAccessContext ctx) =>
        (await VisibleFolderIdsAsync(ctx, await ArchiveFolderSnapshot.LoadAsync(dbContext))).Contains(folderId);

    public async Task<HashSet<int>> VisibleFolderIdsAsync(ArchiveAccessContext ctx, ArchiveFolderSnapshot folders)
    {
        if (ctx.IsDisabled)
        {
            return [];
        }

        if (ctx.CanManage)
        {
            return folders.All.Select(f => f.Id).ToHashSet();
        }

        var defaultId = folders.DefaultFolderId ?? 0;
        var withReadableDocuments = (await Readable(dbContext.Files.Where(f => f.WorkflowState != Removed), ctx)
            .Select(f => f.FolderId ?? defaultId).Distinct().ToListAsync()).ToHashSet();
        var visible = folders.All
            .Where(f => withReadableDocuments.Contains(f.Id) || ArchiveAccessRules.CanRead(ctx, folders.EffectiveGroups(f.Id)))
            .Select(f => f.Id)
            .ToHashSet();
        foreach (var child in folders.All.Where(f => f.ParentId != null && visible.Contains(f.Id)).ToList())
        {
            visible.Add(child.ParentId!.Value);
        }

        return visible;
    }

    public virtual async Task<Dictionary<int, string>> GetGroupNamesAsync(IReadOnlyCollection<int> tagIds)
    {
        if (tagIds.Count == 0)
        {
            return new Dictionary<int, string>();
        }

        var ids = tagIds.Distinct().ToList();
        var core = await coreService.GetCore();
        var sdk = core.DbContextHelper.GetDbContext();
        return await sdk.Tags.AsNoTracking().Where(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name);
    }

    public virtual Task<HashSet<int>> GetLiveGroupIdsAsync() => membership.GetTagIdsWithLiveMembersAsync();

    public async Task<bool> AreAssignableGroupsAsync(IReadOnlyCollection<int> requested, IReadOnlyCollection<int> current)
    {
        var added = requested.Except(current).ToList();
        if (added.Count == 0)
        {
            return true;
        }

        var live = await GetLiveGroupIdsAsync();
        return added.All(live.Contains);
    }
}
