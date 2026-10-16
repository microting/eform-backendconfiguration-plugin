#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using static Microting.eForm.Infrastructure.Constants.Constants.WorkflowStates;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;

namespace BackendConfiguration.Pn.Services.ArchiveAccess;

/// <summary>Makes a folder's or document's live read-tag rows equal a set: soft-deletes the rest, creates the missing.</summary>
public static class ArchiveReadTagSync
{
    public static async Task SyncFolderAsync(BackendConfigurationPnDbContext db, int folderId,
        IReadOnlyCollection<int> tagIds, int userId)
    {
        var missing = await RemoveOthersAsync(db, db.ArchiveFolderReadTags.Where(t => t.ArchiveFolderId == folderId),
            t => t.SdkTagId, tagIds, userId);
        foreach (var tagId in missing)
        {
            await new ArchiveFolderReadTag
            {
                ArchiveFolderId = folderId, SdkTagId = tagId, CreatedByUserId = userId, UpdatedByUserId = userId
            }.Create(db);
        }
    }

    public static async Task SyncFileAsync(BackendConfigurationPnDbContext db, int fileId,
        IReadOnlyCollection<int> tagIds, int userId)
    {
        var missing = await RemoveOthersAsync(db, db.FileReadTags.Where(t => t.FileId == fileId),
            t => t.SdkTagId, tagIds, userId);
        foreach (var tagId in missing)
        {
            await new FileReadTag { FileId = fileId, SdkTagId = tagId, CreatedByUserId = userId, UpdatedByUserId = userId }
                .Create(db);
        }
    }

    /// <summary>Soft-deletes the live rows whose tag is not in <paramref name="tagIds"/>; returns the tag ids to create.</summary>
    private static async Task<List<int>> RemoveOthersAsync<T>(BackendConfigurationPnDbContext db, IQueryable<T> rows,
        Func<T, int> tagOf, IReadOnlyCollection<int> tagIds, int userId) where T : PnBase
    {
        var live = await rows.Where(t => t.WorkflowState != Removed).ToListAsync();
        foreach (var row in live.Where(t => !tagIds.Contains(tagOf(t))))
        {
            row.UpdatedByUserId = userId;
            await row.Delete(db);
        }

        return tagIds.Distinct().Except(live.Select(tagOf)).ToList();
    }
}
