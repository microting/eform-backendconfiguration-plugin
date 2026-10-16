#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.ArchiveAccess;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using File = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.File;

namespace BackendConfiguration.Pn.Services.FileArchive;

public interface IFileArchiver
{
    /// <summary>
    /// Stores the content, then creates File + PropertyFile + FileTags + UploadedData in one
    /// transaction. Returns the new File.Id; throws on failure leaving no rows behind.
    /// The file is placed in <paramref name="folderId"/> (must be a live archive folder). <paramref name="documentDate"/>
    /// is stored as a date only; without a date the state is always <see cref="DocumentDateState.None"/>.
    /// <paramref name="inTransaction"/> runs inside that transaction after the rows are saved and
    /// before commit (receives the File.Id), so the caller can commit its own changes atomically
    /// with the filing. It may run more than once if the execution
    /// strategy retries the transaction (the change tracker is cleared before each retry), so it must
    /// (re)load whatever it modifies inside the callback, never use entities loaded before the call.
    /// <para>
    /// The transaction first locks <paramref name="folderId"/> shared (ArchiveFolderLock) and, before commit, re-checks
    /// on the live tree that it is a live folder and that <paramref name="folderAllowed"/> (the caller's D17 rule, e.g.
    /// ArchiveAccessRules.IsWritableFolder) still holds; otherwise it throws
    /// <see cref="ArchiveFolderUnavailableException"/> and nothing is committed. So a document never lands in a folder
    /// that was deleted, or made unreadable to the caller, after the caller's early check.
    /// </para>
    /// </summary>
    Task<int> ArchiveAsync(Stream content, string name, string extension,
        IReadOnlyCollection<int> propertyIds, IReadOnlyCollection<int> tagIds, int folderId,
        DateTime? documentDate, DocumentDateState documentDateState, int userId,
        Func<int, Task>? inTransaction = null, Func<ArchiveFolderSnapshot, bool>? folderAllowed = null);
}

/// <summary>
/// Puts one file into the PDF archive. Upload first, then the DB rows in one transaction, so a
/// storage failure leaves nothing behind and a DB failure leaves only an orphan object (same
/// md5 name next time).
/// </summary>
public class FileArchiver(BackendConfigurationPnDbContext dbContext, IArchiveStorage storage) : IFileArchiver
{
    public static string ObjectName(string md5, string extension) => $"{md5}.{extension}";

    public async Task<int> ArchiveAsync(Stream content, string name, string extension,
        IReadOnlyCollection<int> propertyIds, IReadOnlyCollection<int> tagIds, int folderId,
        DateTime? documentDate, DocumentDateState documentDateState, int userId,
        Func<int, Task>? inTransaction = null, Func<ArchiveFolderSnapshot, bool>? folderAllowed = null)
    {
        // Letters and digits only: the extension ends up in storage keys, content types and zip entry names.
        extension = new string(extension.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        var date = documentDate?.Date;
        var dateState = date == null ? DocumentDateState.None : documentDateState;
        var staged = await StagedUpload.StageAsync(content, extension);

        await storage.PutAsync(staged.TempPath, ObjectName(staged.Md5, extension));

        return await ArchiveTransaction.RunAsync(dbContext, async () =>
        {
            // Lock first; re-checked below, after the writes.
            await ArchiveFolderLock.SharedAsync(dbContext, [folderId]);
            var file = new File
            {
                FileName = name, FolderId = folderId, DocumentDate = date, DocumentDateState = dateState,
                CreatedByUserId = userId, UpdatedByUserId = userId
            };
            await file.Create(dbContext);
            foreach (var propertyId in propertyIds)
            {
                await new PropertyFile
                {
                    FileId = file.Id, PropertyId = propertyId, CreatedByUserId = userId, UpdatedByUserId = userId
                }.Create(dbContext);
            }

            foreach (var tagId in tagIds)
            {
                await new FileTags
                {
                    FileId = file.Id, FileTagId = tagId, CreatedByUserId = userId, UpdatedByUserId = userId
                }.Create(dbContext);
            }

            await new UploadedData
            {
                FileId = file.Id, Extension = extension, FileName = staged.TempName, Checksum = staged.Md5,
                FileLocation = staged.TempPath, CreatedByUserId = userId, UpdatedByUserId = userId
            }.Create(dbContext);
            if (inTransaction != null)
            {
                await inTransaction(file.Id);
            }

            // Re-checked after the writes, not before: the first plain read fixes the transaction's snapshot, and
            // the caller's claim in inTransaction (an UPDATE ... WHERE still-Ready) must run without one. The shared
            // lock already holds the folder as it is now; a refusal rolls every row of this attempt back.
            var folders = await ArchiveFolderSnapshot.LoadAsync(dbContext);
            if (folders.Find(folderId) == null || folderAllowed?.Invoke(folders) == false)
            {
                dbContext.ChangeTracker.Clear();
                throw new ArchiveFolderUnavailableException();
            }

            return file.Id;
        });
    }
}
