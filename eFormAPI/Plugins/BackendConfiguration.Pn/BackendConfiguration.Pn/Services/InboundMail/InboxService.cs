#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Inbox;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.FileArchive;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;

namespace BackendConfiguration.Pn.Services.InboundMail;

public interface IInboxService
{
    Task<OperationDataResult<List<InboxListItem>>> ListAsync(int? status, string? search);
    Task<OperationDataResult<InboxListItem>> GetAsync(int id);
    Task<Stream?> GetPdfAsync(int id);
    Task<OperationResult> FileAsync(int id, FileInboxDocumentRequest req, int userId);
    Task<OperationResult> UndoAsync(int id, int userId);
    Task<OperationResult> RejectAsync(int id, int userId);
}

public class InboxService(BackendConfigurationPnDbContext dbContext, IArchiveStorage storage, IFileArchiver archiver,
    IBackendConfigurationLocalizationService localization, ILogger<InboxService> logger) : IInboxService
{
    private static readonly TimeSpan UndoWindow = TimeSpan.FromMinutes(10);

    /// <summary>The document is no longer in the state the operation needs (a concurrent file/undo won).</summary>
    private sealed class InboxStateException(string messageKey) : Exception(messageKey)
    {
        public string MessageKey { get; } = messageKey;
    }

    private OperationResult StateFailure(InboxStateException e) => new(false, localization.GetString(e.MessageKey));

    private OperationResult UnexpectedFailure(Exception e, string operation)
    {
        dbContext.ChangeTracker.Clear();
        logger.LogError(e, "Inbox {Operation} failed", operation);
        return new OperationResult(false, localization.GetString("InboxUnexpectedError"));
    }

    public async Task<OperationDataResult<List<InboxListItem>>> ListAsync(int? status, string? search)
    {
        var query = dbContext.InboxDocuments.Where(d => d.WorkflowState != Constants.WorkflowStates.Removed);
        if (status is { } s)
        {
            query = query.Where(d => (int)d.Status == s);
        }
        else
        {
            var filedSince = DateTime.UtcNow.AddDays(-7);
            query = query.Where(d => d.Status != InboxDocumentStatus.Rejected
                                     && (d.Status != InboxDocumentStatus.Filed || d.FiledAt > filedSince));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim(); // MySQL collation is case-insensitive
            query = query.Where(d => d.FileName.Contains(term) || d.FromAddress.Contains(term)
                                     || (d.Subject != null && d.Subject.Contains(term)));
        }

        var docs = await query.OrderByDescending(d => d.ReceivedAt).Take(500)
            .Include(d => d.Suggestions).AsNoTracking().ToListAsync();
        return new OperationDataResult<List<InboxListItem>>(true, await MapAsync(docs));
    }

    public async Task<OperationDataResult<InboxListItem>> GetAsync(int id)
    {
        var doc = await dbContext.InboxDocuments.Include(d => d.Suggestions).AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id && d.WorkflowState != Constants.WorkflowStates.Removed);
        return doc == null
            ? new OperationDataResult<InboxListItem>(false, localization.GetString("InboxDocumentNotFound"))
            : new OperationDataResult<InboxListItem>(true, (await MapAsync([doc])).Single());
    }

    public async Task<Stream?> GetPdfAsync(int id)
    {
        var md5 = await dbContext.InboxDocuments
            .Where(d => d.Id == id && d.WorkflowState != Constants.WorkflowStates.Removed)
            .Select(d => d.Md5).FirstOrDefaultAsync();
        return md5 == null ? null : await storage.GetAsync(FileArchiver.ObjectName(md5, "pdf"));
    }

    public async Task<OperationResult> FileAsync(int id, FileInboxDocumentRequest req, int userId)
    {
        var doc = await dbContext.InboxDocuments.AsNoTracking()
            .Where(d => d.Id == id && d.WorkflowState != Constants.WorkflowStates.Removed)
            .Select(d => new { d.Status, d.Md5, d.FileName }).FirstOrDefaultAsync();
        if (doc is not { Status: InboxDocumentStatus.Ready, Md5: { } md5 })
            return new OperationResult(false, localization.GetString("InboxDocumentNotReady"));

        var propertyIds = req.PropertyIds.Distinct().ToList();
        var tagIds = req.TagIds.Distinct().ToList();
        if (propertyIds.Count == 0)
            return new OperationResult(false, localization.GetString("InboxChooseAtLeastOneProperty"));

        var liveProperties = await dbContext.Properties
            .CountAsync(p => propertyIds.Contains(p.Id) && p.WorkflowState != Constants.WorkflowStates.Removed);
        var liveTags = await dbContext.FileTags
            .CountAsync(t => tagIds.Contains(t.Id) && t.WorkflowState != Constants.WorkflowStates.Removed);
        if (liveProperties != propertyIds.Count || liveTags != tagIds.Count)
            return new OperationResult(false, localization.GetString("InboxUnknownPropertyOrTag"));

        await using var pdf = await storage.GetAsync(FileArchiver.ObjectName(md5, "pdf"));
        if (pdf == null) return new OperationResult(false, localization.GetString("InboxDocumentNotReady"));

        var name = string.IsNullOrWhiteSpace(req.Name) ? Path.GetFileNameWithoutExtension(doc.FileName) : req.Name.Trim();
        try
        {
            // The inbox state commits in the archiver's own transaction, so the archive rows and the
            // Filed status can never disagree. The callback may run again on a retry: it reloads what it changes.
            await archiver.ArchiveAsync(pdf, name, "pdf", propertyIds, tagIds, userId, async fileId =>
            {
                // Claims the row (blocks a concurrent filer until commit) only while it is still Ready.
                var claimed = await dbContext.InboxDocuments
                    .Where(d => d.Id == id && d.Status == InboxDocumentStatus.Ready)
                    .ExecuteUpdateAsync(s => s.SetProperty(d => d.UpdatedAt, DateTime.UtcNow));
                if (claimed == 0) throw new InboxStateException("InboxDocumentNotReady");

                var tracked = await dbContext.InboxDocuments.Include(d => d.Suggestions).SingleAsync(d => d.Id == id);
                foreach (var suggestion in tracked.Suggestions.Where(x => x.WorkflowState != Constants.WorkflowStates.Removed))
                {
                    suggestion.Accepted = suggestion.Kind == InboxSuggestionKind.Property
                        ? propertyIds.Contains(suggestion.TargetId)
                        : tagIds.Contains(suggestion.TargetId);
                    suggestion.UpdatedByUserId = userId;
                    await suggestion.Update(dbContext);
                }

                tracked.Status = InboxDocumentStatus.Filed;
                tracked.FiledFileId = fileId;
                tracked.FiledAt = DateTime.UtcNow;
                tracked.FiledByUserId = userId;
                tracked.UpdatedByUserId = userId;
                await tracked.Update(dbContext);
            });
        }
        catch (InboxStateException e)
        {
            dbContext.ChangeTracker.Clear();
            return StateFailure(e);
        }
        catch (Exception e)
        {
            return UnexpectedFailure(e, "file");
        }

        return new OperationResult(true, localization.GetString("InboxDocumentFiled"));
    }

    public async Task<OperationResult> UndoAsync(int id, int userId)
    {
        var cutoff = DateTime.UtcNow - UndoWindow;
        var filed = await dbContext.InboxDocuments.AsNoTracking()
            .Where(d => d.Id == id && d.WorkflowState != Constants.WorkflowStates.Removed)
            .Select(d => new { d.Status, d.FiledFileId, d.FiledAt }).FirstOrDefaultAsync();
        if (filed is not { Status: InboxDocumentStatus.Filed, FiledFileId: { } fileId, FiledAt: { } filedAt }
            || filedAt < cutoff)
            return new OperationResult(false, localization.GetString("InboxUndoNotPossible"));

        var strategy = dbContext.Database.CreateExecutionStrategy();
        var attempt = 0;
        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                if (attempt++ > 0) dbContext.ChangeTracker.Clear();
                await using var tx = await dbContext.Database.BeginTransactionAsync();

                // Claims the row only while it is still the filing we looked at and inside the window.
                var claimed = await dbContext.InboxDocuments
                    .Where(d => d.Id == id && d.Status == InboxDocumentStatus.Filed && d.FiledFileId == fileId
                                && d.WorkflowState != Constants.WorkflowStates.Removed && d.FiledAt >= cutoff)
                    .ExecuteUpdateAsync(s => s.SetProperty(d => d.UpdatedAt, DateTime.UtcNow));
                if (claimed == 0) throw new InboxStateException("InboxUndoNotPossible");

                var removed = Constants.WorkflowStates.Removed;
                foreach (var pf in await dbContext.PropertyFiles
                             .Where(x => x.FileId == fileId && x.WorkflowState != removed).ToListAsync())
                {
                    pf.UpdatedByUserId = userId;
                    await pf.Delete(dbContext);
                }

                foreach (var ft in await dbContext.FilesTags
                             .Where(x => x.FileId == fileId && x.WorkflowState != removed).ToListAsync())
                {
                    ft.UpdatedByUserId = userId;
                    await ft.Delete(dbContext);
                }

                foreach (var ud in await dbContext.UploadedDatas
                             .Where(x => x.FileId == fileId && x.WorkflowState != removed).ToListAsync())
                {
                    ud.UpdatedByUserId = userId;
                    await ud.Delete(dbContext);
                }

                var file = await dbContext.Files.SingleOrDefaultAsync(f => f.Id == fileId);
                if (file != null && file.WorkflowState != removed)
                {
                    file.UpdatedByUserId = userId;
                    await file.Delete(dbContext);
                }

                var doc = await dbContext.InboxDocuments.Include(d => d.Suggestions).SingleAsync(d => d.Id == id);
                foreach (var suggestion in doc.Suggestions)
                {
                    suggestion.Accepted = null;
                    suggestion.UpdatedByUserId = userId;
                    await suggestion.Update(dbContext);
                }

                doc.Status = InboxDocumentStatus.Ready;
                doc.FiledFileId = null;
                doc.FiledAt = null;
                doc.FiledByUserId = null;
                doc.UpdatedByUserId = userId;
                await doc.Update(dbContext);

                await tx.CommitAsync();
            });
        }
        catch (InboxStateException e)
        {
            dbContext.ChangeTracker.Clear();
            return StateFailure(e);
        }
        catch (Exception e)
        {
            return UnexpectedFailure(e, "undo");
        }

        return new OperationResult(true, localization.GetString("InboxDocumentUndone"));
    }

    public async Task<OperationResult> RejectAsync(int id, int userId)
    {
        try
        {
            var strategy = dbContext.Database.CreateExecutionStrategy();
            var attempt = 0;
            await strategy.ExecuteAsync(async () =>
            {
                if (attempt++ > 0) dbContext.ChangeTracker.Clear();
                await using var tx = await dbContext.Database.BeginTransactionAsync();

                // Conditional claim, so a concurrent filing is never turned into Rejected.
                var claimed = await dbContext.InboxDocuments
                    .Where(d => d.Id == id && d.WorkflowState != Constants.WorkflowStates.Removed
                                && (d.Status == InboxDocumentStatus.Ready || d.Status == InboxDocumentStatus.Failed))
                    .ExecuteUpdateAsync(s => s.SetProperty(d => d.UpdatedAt, DateTime.UtcNow));
                if (claimed == 0) throw new InboxStateException("InboxDocumentNotReady");

                var doc = await dbContext.InboxDocuments.SingleAsync(d => d.Id == id);
                doc.Status = InboxDocumentStatus.Rejected;
                doc.UpdatedByUserId = userId;
                await doc.Update(dbContext);
                await tx.CommitAsync();
            });
        }
        catch (InboxStateException e)
        {
            dbContext.ChangeTracker.Clear();
            return StateFailure(e);
        }
        catch (Exception e)
        {
            return UnexpectedFailure(e, "reject");
        }

        return new OperationResult(true, localization.GetString("InboxDocumentRejected"));
    }

    private async Task<List<InboxListItem>> MapAsync(List<InboxDocument> docs)
    {
        var suggestions = docs.SelectMany(d => d.Suggestions)
            .Where(s => s.WorkflowState != Constants.WorkflowStates.Removed).ToList();
        var propertyIds = suggestions.Where(s => s.Kind == InboxSuggestionKind.Property).Select(s => s.TargetId).Distinct().ToList();
        var tagIds = suggestions.Where(s => s.Kind == InboxSuggestionKind.Tag).Select(s => s.TargetId).Distinct().ToList();
        var propertyNames = await dbContext.Properties.Where(p => propertyIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name);
        var tagNames = await dbContext.FileTags.Where(t => tagIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Name);

        return docs.Select(d => new InboxListItem
        {
            Id = d.Id,
            FileName = d.FileName,
            Subject = d.Subject,
            FromAddress = d.FromAddress,
            ReceivedAt = d.ReceivedAt,
            ReadyBy = d.ReadyBy,
            Status = (int)d.Status,
            FailureReason = d.FailureReason,
            ReviewedByMicroting = d.ReviewedByMicroting,
            Suggestions = d.Suggestions.Where(s => s.WorkflowState != Constants.WorkflowStates.Removed)
                .OrderByDescending(s => s.Confidence)
                .Select(s => new InboxSuggestionModel
                {
                    Id = s.Id,
                    Kind = (int)s.Kind,
                    TargetId = s.TargetId,
                    Source = (int)s.Source,
                    Confidence = s.Confidence,
                    Evidence = s.Evidence,
                    Page = s.Page,
                    Reason = s.Reason,
                    TargetName = (s.Kind == InboxSuggestionKind.Property ? propertyNames : tagNames)
                        .GetValueOrDefault(s.TargetId, "")
                }).ToList()
        }).ToList();
    }
}
