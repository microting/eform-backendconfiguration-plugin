#nullable enable
using System.Text;
using BackendConfiguration.Pn.Controllers;
using BackendConfiguration.Pn.Infrastructure.Models.Files;
using BackendConfiguration.Pn.Services.ArchiveAccess;
using BackendConfiguration.Pn.Services.ArchiveFolders;
using Microsoft.AspNetCore.Http;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using File = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.File;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// Archive folder fixture data. Fictional names only. TestBaseSetup builds the schema with EnsureCreated(), so no
/// migration runs and the default "Arkiv" folder exists only after a test (or the code under test) creates it.
/// </summary>
public static class ArchiveTestData
{
    /// <summary>Alias of <see cref="InboxTestData.ClearAsync"/> for archive tests.</summary>
    public static Task ClearAsync(BackendConfigurationPnDbContext db) => InboxTestData.ClearAsync(db);

    public static Task<int> DefaultFolderIdAsync(BackendConfigurationPnDbContext db) =>
        ArchiveDefaultFolder.GetOrCreateIdAsync(db, 1);

    public static async Task<ArchiveFolder> FolderAsync(BackendConfigurationPnDbContext db, string name,
        int? parentId = null, params int[] groups)
    {
        var folder = new ArchiveFolder { Name = name, ParentId = parentId, CreatedByUserId = 1, UpdatedByUserId = 1 };
        await folder.Create(db);
        foreach (var group in groups)
        {
            await new ArchiveFolderReadTag
            {
                ArchiveFolderId = folder.Id, SdkTagId = group, CreatedByUserId = 1, UpdatedByUserId = 1
            }.Create(db);
        }

        return folder;
    }

    /// <summary>Creates a file; a null <paramref name="folderId"/> means the default folder (as in 10.0.59).</summary>
    public static async Task<File> FileAsync(BackendConfigurationPnDbContext db, int? folderId,
        string name = "Faktura 1001", bool ownAccess = false, int[]? ownGroups = null, int[]? propertyIds = null,
        DateTime? documentDate = null, DocumentDateState dateState = DocumentDateState.None)
    {
        var file = new File
        {
            FileName = name, FolderId = folderId, OwnAccess = ownAccess, DocumentDate = documentDate,
            DocumentDateState = dateState, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await file.Create(db);
        foreach (var group in ownGroups ?? [])
        {
            await new FileReadTag { FileId = file.Id, SdkTagId = group, CreatedByUserId = 1, UpdatedByUserId = 1 }
                .Create(db);
        }

        foreach (var propertyId in propertyIds ?? [])
        {
            await new PropertyFile { FileId = file.Id, PropertyId = propertyId, CreatedByUserId = 1, UpdatedByUserId = 1 }
                .Create(db);
        }

        await new UploadedData
        {
            FileId = file.Id, Extension = "pdf", FileName = name, Checksum = Guid.NewGuid().ToString("N"),
            FileLocation = "", CreatedByUserId = 1, UpdatedByUserId = 1
        }.Create(db);
        return file;
    }

    /// <summary>A one-file upload request; a null or 0 <paramref name="folderId"/> means the default folder.</summary>
    public static BackendConfigurationFileCreate Upload(string fileName, int? folderId = null, DateTime? date = null)
    {
        var bytes = Encoding.ASCII.GetBytes("%PDF-1.7 upload");
        return new BackendConfigurationFileCreate
        {
            File = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", fileName),
            FolderId = folderId,
            DocumentDate = date
        };
    }

    /// <summary>Every controller that serves archive data; the filter and authorization tests share it.</summary>
    public static IEnumerable<Type> ArchiveControllers()
    {
        yield return typeof(FilesController);
        yield return typeof(ArchiveFoldersController);
        yield return typeof(InboxController);
        yield return typeof(FileTagsController);
    }

    public static ArchiveAccessContext Admin =>
        new(IsDisabled: false, IsAdmin: true, UserGroupIds: [], CanManage: true);

    /// <summary>A Leder who is not an admin: can manage and read everything, belongs to no group.</summary>
    public static ArchiveAccessContext Leder =>
        new(IsDisabled: false, IsAdmin: false, UserGroupIds: [], CanManage: true);

    public static ArchiveAccessContext User(params int[] groups) =>
        new(IsDisabled: false, IsAdmin: false, UserGroupIds: groups, CanManage: false);

    public static ArchiveAccessContext Disabled => ArchiveAccessContext.Disabled;
}
