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

namespace BackendConfiguration.Pn.Services.BackendConfigurationFileTagsService;

using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;

/// <summary>
/// One live file tag per name. Callers hold <c>InboxNamedLock.TagCreate</c>, so two requests cannot both find
/// no tag and insert one each. Name matching follows the database collation.
/// </summary>
public static class FileTagStore
{
    /// <summary>
    /// The live tag with <paramref name="name"/>; else the newest removed one, restored; else a new tag.
    /// <c>Created</c> is true only for a new row.
    /// </summary>
    public static async Task<(FileTag Tag, bool Created)> FindOrCreateAsync(BackendConfigurationPnDbContext db,
        string name, int userId)
    {
        // A live tag wins, so a removed duplicate is never restored beside it.
        var live = await db.FileTags
            .Where(t => t.Name == name && t.WorkflowState != Constants.WorkflowStates.Removed)
            .OrderBy(t => t.Id)
            .FirstOrDefaultAsync();
        if (live != null)
        {
            return (live, false);
        }

        var removed = await db.FileTags
            .Where(t => t.Name == name && t.WorkflowState == Constants.WorkflowStates.Removed)
            .OrderByDescending(t => t.Id)
            .FirstOrDefaultAsync();
        if (removed != null)
        {
            removed.WorkflowState = Constants.WorkflowStates.Created;
            removed.UpdatedByUserId = userId;
            await removed.Update(db);
            return (removed, false);
        }

        var tag = new FileTag { Name = name, CreatedByUserId = userId, UpdatedByUserId = userId };
        await tag.Create(db);
        return (tag, true);
    }
}
