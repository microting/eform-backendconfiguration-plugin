#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;

namespace BackendConfiguration.Pn.Services.ArchiveFolders;

/// <summary>
/// The default archive folder: documents without a chosen folder land here. The AddArchiveFolders migration creates it;
/// GetOrCreateIdAsync creates it only on databases built with EnsureCreated().
/// </summary>
public static class ArchiveDefaultFolder
{
    public const string Name = "Arkiv";

    /// <summary>The id of the default folder, or null when none exists.</summary>
    public static Task<int?> FindIdAsync(BackendConfigurationPnDbContext db) =>
        db.ArchiveFolders
            .Where(f => f.IsDefault && f.WorkflowState != Constants.WorkflowStates.Removed)
            .OrderBy(f => f.Id)
            .Select(f => (int?)f.Id)
            .FirstOrDefaultAsync();

    public static async Task<int> GetOrCreateIdAsync(BackendConfigurationPnDbContext db, int userId)
    {
        if (await FindIdAsync(db) is { } id)
        {
            return id;
        }

        var folder = new ArchiveFolder { Name = Name, IsDefault = true, CreatedByUserId = userId, UpdatedByUserId = userId };
        await folder.Create(db);
        return folder.Id;
    }
}
