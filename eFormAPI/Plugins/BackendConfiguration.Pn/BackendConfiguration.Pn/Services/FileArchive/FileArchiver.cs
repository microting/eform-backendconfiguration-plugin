#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using File = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.File;

namespace BackendConfiguration.Pn.Services.FileArchive;

public interface IFileArchiver
{
    /// <summary>
    /// Stores the content, then creates File + PropertyFile + FileTags + UploadedData in one
    /// transaction. Returns the new File.Id; throws on failure leaving no rows behind.
    /// <paramref name="inTransaction"/> runs inside that transaction after the rows are saved and
    /// before commit (receives the File.Id), so the caller can commit its own changes atomically
    /// with the filing. It may run more than once if the execution
    /// strategy retries the transaction (the change tracker is cleared before each retry), so it must
    /// (re)load whatever it modifies inside the callback, never use entities loaded before the call.
    /// </summary>
    Task<int> ArchiveAsync(Stream content, string name, string extension,
        IReadOnlyCollection<int> propertyIds, IReadOnlyCollection<int> tagIds, int userId,
        Func<int, Task>? inTransaction = null);
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
        IReadOnlyCollection<int> propertyIds, IReadOnlyCollection<int> tagIds, int userId,
        Func<int, Task>? inTransaction = null)
    {
        // Letters and digits only: the extension ends up in storage keys, content types and zip entry names.
        extension = new string(extension.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        var staged = await StagedUpload.StageAsync(content, extension);

        await storage.PutAsync(staged.TempPath, ObjectName(staged.Md5, extension));

        var strategy = dbContext.Database.CreateExecutionStrategy();
        var attempt = 0;
        return await strategy.ExecuteAsync(async () =>
        {
            if (attempt++ > 0)
            {
                dbContext.ChangeTracker.Clear();
            }

            await using var tx = await dbContext.Database.BeginTransactionAsync();
            var file = new File { FileName = name, CreatedByUserId = userId, UpdatedByUserId = userId };
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

            await tx.CommitAsync();
            return file.Id;
        });
    }
}
