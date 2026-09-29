/*
The MIT License (MIT)

Copyright (c) 2007 - 2026 Microting A/S

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.
*/

#nullable enable

namespace BackendConfiguration.Pn.Infrastructure.Helpers;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;

/// <summary>
/// #1323 — a copied task carries the source task's attachments. Shared by the calendar
/// copy dialog and the task-list batch copy, which both create the copy through
/// <c>CreateTask</c>.
///
/// <para>No file is copied: the new task gets new <see cref="AreaRulePlanningFile"/> rows
/// that point at the same content-addressed SDK <c>UploadedData</c>, with the Drive link
/// columns copied as they are. Deleting an attachment only soft-deletes its own row, so
/// the copy and the source stay independent.</para>
///
/// <para>Validation runs BEFORE the copy is created (<see cref="ResolveAsync"/>) and the
/// rows are written after (<see cref="CopyAsync"/>), so a bad request never leaves a
/// half-copied task.</para>
/// </summary>
public static class TaskAttachmentCopyHelper
{
    /// <summary>Why <see cref="ResolveAsync"/> refused; each value is a localization key.</summary>
    public const string SourceNotFound = "AreaRulePlanningNotFound";
    public const string AttachmentNotFound = "FileNotFound";
    public const string LimitReached = "AttachmentLimitReached";

    /// <summary>
    /// The source task's live attachments to carry over: all of them when
    /// <paramref name="attachmentIds"/> is null, otherwise exactly the listed ones (the
    /// chips the user kept). The source must be a live task on a live property — the
    /// web has no per-user property scoping, so a live property is one the user can
    /// access. Inherited files count toward <paramref name="maxAttachments"/>.
    /// </summary>
    public static async Task<(List<AreaRulePlanningFile> Files, string? Error)> ResolveAsync(
        BackendConfigurationPnDbContext dbContext,
        int sourceTaskId,
        IReadOnlyCollection<int>? attachmentIds,
        int maxAttachments)
    {
        var sourceIsAccessible = await dbContext.AreaRulePlannings
            .AsNoTracking()
            .AnyAsync(arp => arp.Id == sourceTaskId
                             && arp.WorkflowState != Constants.WorkflowStates.Removed
                             && dbContext.Properties.Any(p =>
                                 p.Id == arp.PropertyId
                                 && p.WorkflowState != Constants.WorkflowStates.Removed))
            .ConfigureAwait(false);
        if (!sourceIsAccessible)
        {
            return ([], SourceNotFound);
        }

        var files = await dbContext.AreaRulePlanningFiles
            .AsNoTracking()
            .Where(f => f.AreaRulePlanningId == sourceTaskId
                        && f.WorkflowState != Constants.WorkflowStates.Removed)
            .OrderBy(f => f.Id)
            .ToListAsync()
            .ConfigureAwait(false);

        if (attachmentIds != null)
        {
            var wanted = attachmentIds.ToHashSet();
            files = files.Where(f => wanted.Contains(f.Id)).ToList();
            if (files.Count != wanted.Count)
            {
                return ([], AttachmentNotFound);
            }
        }

        return files.Count > maxAttachments ? ([], LimitReached) : (files, null);
    }

    /// <summary>Writes one new attachment row per source row onto <paramref name="targetTaskId"/>.</summary>
    public static async Task CopyAsync(
        BackendConfigurationPnDbContext dbContext,
        IEnumerable<AreaRulePlanningFile> sourceFiles,
        int targetTaskId,
        int userId)
    {
        foreach (var source in sourceFiles)
        {
            await new AreaRulePlanningFile
            {
                AreaRulePlanningId = targetTaskId,
                UploadedDataId = source.UploadedDataId,
                OriginalFileName = source.OriginalFileName,
                MimeType = source.MimeType,
                SizeBytes = source.SizeBytes,
                DriveFileId = source.DriveFileId,
                DriveModifiedTime = source.DriveModifiedTime,
                GoogleOAuthTokenId = source.GoogleOAuthTokenId,
                CreatedByUserId = userId,
                UpdatedByUserId = userId
            }.Create(dbContext).ConfigureAwait(false);
        }
    }
}
