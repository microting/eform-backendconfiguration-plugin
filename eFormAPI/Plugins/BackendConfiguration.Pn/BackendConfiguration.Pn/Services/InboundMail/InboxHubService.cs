#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Inbox;
using BackendConfiguration.Pn.Services.FileArchive;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;

namespace BackendConfiguration.Pn.Services.InboundMail;

/// <summary>What the central inbound mail service calls; the controller has verified the signature.</summary>
public interface IInboxHubService
{
    /// <summary>Creates the document (Preparing, or SenderPending for an unknown sender) unless the sender is blocked.</summary>
    Task<ArrivedResponse> ArrivedAsync(ArrivedRequest r);

    /// <summary>Live properties and file tags, for the central service's suggestions.</summary>
    Task<CatalogResponse> CatalogAsync();

    /// <summary>Stores the PDF and its suggestions. Idempotent on <see cref="DeliverMetadata.HubDocumentId"/>.</summary>
    Task DeliverAsync(DeliverMetadata meta, Stream pdf, string fileName);

    /// <summary>Marks a not yet delivered document Failed with the (Danish) reason.</summary>
    Task FailedAsync(FailedRequest r);
}

public class InboxHubService(BackendConfigurationPnDbContext dbContext, IArchiveStorage storage,
    SenderVerdictResolver verdicts, ILogger<InboxHubService> logger) : IInboxHubService
{
    private const int SystemUserId = 0;

    /// <summary>FromAddress of a document delivered without a prior "arrived": the sender was never seen.</summary>
    public const string UnknownSender = "ukendt";

    /// <summary>Delivered already, or closed by a manager: a delivery changes nothing.</summary>
    private static readonly Expression<Func<InboxDocument, bool>> DeliveredOrClosed = d =>
        d.DeliveredAt != null || d.Status == InboxDocumentStatus.Filed || d.Status == InboxDocumentStatus.Rejected;

    public async Task<ArrivedResponse> ArrivedAsync(ArrivedRequest r)
    {
        var existing = await dbContext.InboxDocuments.AsNoTracking()
            .FirstOrDefaultAsync(d => d.HubDocumentId == r.HubDocumentId);
        if (existing != null)
        {
            // The central service retried after a timeout: answer what the first call decided.
            return new ArrivedResponse(existing.Status switch
            {
                InboxDocumentStatus.SenderPending => SenderVerdict.Unknown,
                // Failed before delivery: it may have been held, so the status no longer tells. Ask again.
                // Rejected: a manager rejecting a document is not a sender block, so ask again as well.
                InboxDocumentStatus.Failed when existing.DeliveredAt == null => await verdicts.ResolveAsync(existing.FromAddress),
                InboxDocumentStatus.Rejected => await verdicts.ResolveAsync(existing.FromAddress),
                _ => SenderVerdict.Allowed
            });
        }

        var verdict = await verdicts.ResolveAsync(r.FromAddress);
        if (verdict == SenderVerdict.Blocked)
        {
            return new ArrivedResponse(verdict);
        }

        await new InboxDocument
        {
            HubDocumentId = r.HubDocumentId,
            FromAddress = Cut(r.FromAddress, 254),
            Subject = Cut(r.Subject, 500),
            ReceivedAt = r.ReceivedAt,
            ReadyBy = r.ReadyBy,
            FileName = Cut(r.FileName, 250),
            SizeBytes = r.SizeBytes,
            SpfResult = Cut(r.SpfResult, 20),
            DkimResult = Cut(r.DkimResult, 20),
            Status = verdict == SenderVerdict.Unknown ? InboxDocumentStatus.SenderPending : InboxDocumentStatus.Preparing,
            CreatedByUserId = SystemUserId,
            UpdatedByUserId = SystemUserId
        }.Create(dbContext);
        return new ArrivedResponse(verdict);
    }

    public async Task<CatalogResponse> CatalogAsync()
    {
        var properties = await dbContext.Properties
            .Where(p => p.WorkflowState != Constants.WorkflowStates.Removed)
            .OrderBy(p => p.Id)
            .Select(p => new CatalogProperty(p.Id, p.Name, p.Address))
            .ToListAsync();
        var tags = await dbContext.FileTags
            .Where(t => t.WorkflowState != Constants.WorkflowStates.Removed)
            .OrderBy(t => t.Id)
            .Select(t => new CatalogTag(t.Id, t.Name))
            .ToListAsync();
        return new CatalogResponse(properties, tags);
    }

    public async Task DeliverAsync(DeliverMetadata meta, Stream pdf, string fileName)
    {
        // A retry of a delivery that already landed changes nothing and stores nothing.
        if (await dbContext.InboxDocuments.Where(d => d.HubDocumentId == meta.HubDocumentId)
                .AnyAsync(DeliveredOrClosed))
        {
            return;
        }

        var staged = await StagedUpload.StageAsync(pdf, "pdf");
        try
        {
            // Storage first: a DB failure below leaves only an orphan object under the same md5 name.
            await storage.PutAsync(staged.TempPath, FileArchiver.ObjectName(staged.Md5, "pdf"));
            await RecordDeliveryAsync(meta, fileName, staged.Md5);
        }
        finally
        {
            // Unlike a filed upload, no UploadedData row points at this temp copy.
            System.IO.File.Delete(staged.TempPath);
        }
    }

    public async Task FailedAsync(FailedRequest r)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        var attempt = 0;
        await strategy.ExecuteAsync(async () =>
        {
            if (attempt++ > 0)
            {
                dbContext.ChangeTracker.Clear();
            }

            await using var tx = await dbContext.Database.BeginTransactionAsync();

            // Conditional claim, so a delivery that lands concurrently is never turned into Failed.
            var claimed = await dbContext.InboxDocuments
                .Where(d => d.HubDocumentId == r.HubDocumentId && d.DeliveredAt == null
                            && (d.Status == InboxDocumentStatus.Preparing || d.Status == InboxDocumentStatus.SenderPending))
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.UpdatedAt, DateTime.UtcNow));
            if (claimed == 0)
            {
                return;
            }

            var doc = await dbContext.InboxDocuments.SingleAsync(d => d.HubDocumentId == r.HubDocumentId);
            doc.Status = InboxDocumentStatus.Failed;
            doc.FailureReason = Cut(r.Reason, 500);
            doc.UpdatedByUserId = SystemUserId;
            await doc.Update(dbContext);
            await tx.CommitAsync();
        });
    }

    /// <summary>
    /// Suggestions and the document update commit together. The claim on DeliveredAt is a locking
    /// UPDATE, so a concurrent or retried delivery waits for this one and then finds it delivered.
    /// </summary>
    private async Task RecordDeliveryAsync(DeliverMetadata meta, string fileName, string md5)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        var attempt = 0;
        await strategy.ExecuteAsync(async () =>
        {
            if (attempt++ > 0)
            {
                dbContext.ChangeTracker.Clear();
            }

            await using var tx = await dbContext.Database.BeginTransactionAsync();
            var now = DateTime.UtcNow;
            var claimed = await dbContext.InboxDocuments
                .Where(d => d.HubDocumentId == meta.HubDocumentId)
                .Where(Negate(DeliveredOrClosed))
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.DeliveredAt, now));

            InboxDocument doc;
            if (claimed == 0)
            {
                if (await dbContext.InboxDocuments.AnyAsync(d => d.HubDocumentId == meta.HubDocumentId))
                {
                    return; // delivered (or closed) by another request meanwhile
                }

                // "arrived" never reached us, so the sender was never checked: hold it for approval.
                doc = new InboxDocument
                {
                    HubDocumentId = meta.HubDocumentId,
                    FromAddress = UnknownSender,
                    ReceivedAt = now,
                    FileName = Cut(fileName, 250),
                    Status = InboxDocumentStatus.SenderPending,
                    CreatedByUserId = SystemUserId,
                    UpdatedByUserId = SystemUserId
                };
                await doc.Create(dbContext);
            }
            else
            {
                doc = await dbContext.InboxDocuments.SingleAsync(d => d.HubDocumentId == meta.HubDocumentId);
            }

            await AddSuggestionsAsync(doc.Id, meta);

            doc.Md5 = md5;
            doc.PageCount = meta.PageCount;
            doc.ReviewedByMicroting = meta.ReviewedByMicroting;
            doc.DeliveredAt = now;
            doc.FailureReason = null;
            // An unknown sender stays SenderPending: only approve-sender may make the document Ready. A document
            // that failed before delivery may have been held, so its sender is checked again.
            if (doc.Status == InboxDocumentStatus.Failed
                && await verdicts.ResolveAsync(doc.FromAddress) != SenderVerdict.Allowed)
            {
                doc.Status = InboxDocumentStatus.SenderPending;
            }
            else if (doc.Status != InboxDocumentStatus.SenderPending)
            {
                doc.Status = InboxDocumentStatus.Ready;
            }

            await doc.Update(dbContext);
            await tx.CommitAsync();
        });
    }

    private async Task AddSuggestionsAsync(int inboxDocumentId, DeliverMetadata meta)
    {
        if (meta.Suggestions is not { Count: > 0 })
        {
            return;
        }

        var propertyIds = (await dbContext.Properties
            .Where(p => p.WorkflowState != Constants.WorkflowStates.Removed)
            .Select(p => p.Id).ToListAsync()).ToHashSet();
        var tagIds = (await dbContext.FileTags
            .Where(t => t.WorkflowState != Constants.WorkflowStates.Removed)
            .Select(t => t.Id).ToListAsync()).ToHashSet();

        foreach (var s in meta.Suggestions)
        {
            var kind = ParseKind(s.Kind);
            var source = ParseSource(s.Source);
            if (kind == null || source == null)
            {
                logger.LogWarning(
                    "Inbox document {HubDocumentId}: dropped a suggestion with unknown kind '{Kind}' or source '{Source}'",
                    meta.HubDocumentId, s.Kind, s.Source);
                continue;
            }

            // The target may have been removed since the central service fetched the catalog.
            if (!(kind == InboxSuggestionKind.Tag ? tagIds : propertyIds).Contains(s.TargetId))
            {
                continue;
            }

            await new InboxSuggestion
            {
                InboxDocumentId = inboxDocumentId,
                Kind = kind.Value,
                TargetId = s.TargetId,
                Source = source.Value,
                Confidence = Math.Clamp(s.Confidence, 0, 1),
                Evidence = Cut(s.Evidence, 250),
                Page = s.Page,
                Reason = Cut(s.Reason, 500),
                CreatedByUserId = SystemUserId,
                UpdatedByUserId = SystemUserId
            }.Create(dbContext);
        }
    }

    private static InboxSuggestionKind? ParseKind(string? value) => value?.ToLowerInvariant() switch
    {
        "property" => InboxSuggestionKind.Property,
        "tag" => InboxSuggestionKind.Tag,
        _ => null
    };

    private static InboxSuggestionSource? ParseSource(string? value) => value?.ToLowerInvariant() switch
    {
        "textmatch" => InboxSuggestionSource.TextMatch,
        "ai" => InboxSuggestionSource.Ai,
        "reviewer" => InboxSuggestionSource.Reviewer,
        _ => null
    };

    private static Expression<Func<InboxDocument, bool>> Negate(Expression<Func<InboxDocument, bool>> e) =>
        Expression.Lambda<Func<InboxDocument, bool>>(Expression.Not(e.Body), e.Parameters);

    private static string? Cut(string? s, int max) => s != null && s.Length > max ? s[..max] : s;
}
