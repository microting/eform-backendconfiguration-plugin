#nullable enable
using System;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Inbox;
using BackendConfiguration.Pn.Infrastructure.Models.Settings;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;

namespace BackendConfiguration.Pn.Services.InboundMail;

public interface IInboxSettingsService
{
    Task<OperationDataResult<InboxSettingsModel>> GetAsync(int userId);
    Task<OperationResult> UpdateAsync(InboxSettingsModel model, int userId);
    Task<OperationDataResult<InboxSettingsModel>> RotateAddressAsync(int userId);
    Task<OperationResult> ApproveSenderAsync(int inboxDocumentId, int userId);
    Task<OperationResult> RejectSenderAsync(int inboxDocumentId, bool block, int userId);
}

/// <summary>
/// Inbox settings (address, sender rules, unknown-sender policy) and the manager's decision on a
/// SenderPending document. Every hub call happens before anything is written, so a hub that says no
/// (or is down) leaves the tenant exactly as it was.
/// </summary>
public partial class InboxSettingsService(BackendConfigurationPnDbContext dbContext, IInboundMailHubClient hub,
    ICustomerNoProvider customerNo, IOptions<InboundMailHubOptions> options,
    IBackendConfigurationLocalizationService localization, ILogger<InboxSettingsService> logger) : IInboxSettingsService
{
    private const string PolicyHold = "hold";
    private const string PolicyRefuse = "refuse";
    /// <summary>MySQL named lock: address creation and rotation never interleave (there is no unique index to lean on).</summary>
    private const string AddressLockSql = "SELECT GET_LOCK('inbox-address-create', 15)";
    private const string AddressUnlockSql = "SELECT RELEASE_LOCK('inbox-address-create')";
    private static readonly TimeSpan Grace = TimeSpan.FromDays(7);

    /// <summary>An exact address or "@domain" (the shapes SenderVerdictResolver matches), lower-cased.</summary>
    [GeneratedRegex(@"^(@[a-z0-9.-]+\.[a-z]{2,}|[^@\s]+@[a-z0-9.-]+\.[a-z]{2,})$")]
    private static partial Regex PatternRegex();

    /// <summary>The row is no longer in the state the operation needs (a concurrent request won).</summary>
    private sealed class InboxStateException(string messageKey) : Exception(messageKey)
    {
        public string MessageKey { get; } = messageKey;
    }

    public async Task<OperationDataResult<InboxSettingsModel>> GetAsync(int userId)
    {
        try
        {
            var address = await ActiveAddressAsync();
            if (address != null)
                return new OperationDataResult<InboxSettingsModel>(true, await ModelAsync(address.Address));

            var n = await customerNo.GetAsync();
            if (n == 0)
                return await FailureAsync("InboxNotConfigured", null);
            return await WithAddressLockAsync(() => CreateFirstAddressAsync(n, userId),
                () => FailureAsync("InboxTryAgainShortly", null));
        }
        catch (Exception e)
        {
            var message = UnexpectedMessage(e, "get settings");
            InboxSettingsModel? model = null;
            try
            {
                model = await ModelAsync(null); // the UI always gets a shape when it can
            }
            catch (Exception inner)
            {
                logger.LogError(inner, "Inbox get settings: could not build the settings model either");
            }

            return model == null
                ? new OperationDataResult<InboxSettingsModel>(false, message)
                : new OperationDataResult<InboxSettingsModel>(false, message, model);
        }
    }

    /// <summary>
    /// First use, inside the address lock. The row is written only once the hub knows the hash, so an
    /// outage never leaves an active address that mail cannot reach.
    /// </summary>
    private async Task<OperationDataResult<InboxSettingsModel>> CreateFirstAddressAsync(int n, int userId)
    {
        var existing = await ActiveAddressAsync(); // another request may have created it while we waited
        if (existing != null)
            return new OperationDataResult<InboxSettingsModel>(true, await ModelAsync(existing.Address));

        var (value, hash) = InboxAddressGenerator.New(n, options.Value.MailDomain);
        try
        {
            await hub.RegisterAddressAsync(hash, null, null);
        }
        catch (InboundMailHubException e)
        {
            return await FailureAsync(HubFailureKey(e, "register address"), null);
        }

        await NewAddress(value, hash, userId).Create(dbContext);
        return new OperationDataResult<InboxSettingsModel>(true, await ModelAsync(value));
    }

    public async Task<OperationResult> UpdateAsync(InboxSettingsModel model, int userId)
    {
        if (model.UnknownSenderPolicy is not (PolicyHold or PolicyRefuse))
            return new OperationResult(false, localization.GetString("InboxInvalidUnknownSenderPolicy"));

        var wanted = (model.SenderRules ?? [])
            .Select(r => (Pattern: Normalize(r.Pattern), r.Kind))
            .Distinct()
            .ToList();
        var invalid = wanted.Any(r => !IsValidPattern(r.Pattern) || !Enum.IsDefined(typeof(InboxSenderRuleKind), r.Kind))
                      || wanted.GroupBy(r => r.Pattern).Any(g => g.Count() > 1); // both Allow and Block
        if (invalid)
            return new OperationResult(false, localization.GetString("InboxInvalidSenderRule"));

        try
        {
            await InTransactionAsync(async () =>
            {
                var existing = await LiveRules().ToListAsync();
                foreach (var rule in existing.Where(r => !wanted.Contains((Normalize(r.Pattern), (int)r.Kind))))
                {
                    rule.UpdatedByUserId = userId;
                    await rule.Delete(dbContext);
                }

                foreach (var (pattern, kind) in wanted.Where(w =>
                             !existing.Any(r => Normalize(r.Pattern) == w.Pattern && (int)r.Kind == w.Kind)))
                {
                    await NewRule(pattern, (InboxSenderRuleKind)kind, userId).Create(dbContext);
                }

                await UpsertPolicyAsync(model.UnknownSenderPolicy, userId);
            });
        }
        catch (Exception e)
        {
            return new OperationResult(false, UnexpectedMessage(e, "update settings"));
        }

        return new OperationResult(true);
    }

    public async Task<OperationDataResult<InboxSettingsModel>> RotateAddressAsync(int userId)
    {
        try
        {
            var n = await customerNo.GetAsync();
            if (n == 0)
                return await FailureAsync("InboxNotConfigured", (await ActiveAddressAsync())?.Address);
            return await WithAddressLockAsync(() => RotateLockedAsync(n, userId),
                async () => await FailureAsync("InboxTryAgainShortly", (await ActiveAddressAsync())?.Address));
        }
        catch (InboxStateException e)
        {
            logger.LogWarning("Inbox rotate address: a concurrent rotation won; the hub may hold this attempt's hash");
            return new OperationDataResult<InboxSettingsModel>(false, StateMessage(e));
        }
        catch (Exception e)
        {
            return new OperationDataResult<InboxSettingsModel>(false, UnexpectedMessage(e, "rotate address"));
        }
    }

    private async Task<OperationDataResult<InboxSettingsModel>> RotateLockedAsync(int n, int userId)
    {
        var old = await ActiveAddressAsync();
        var (value, hash) = InboxAddressGenerator.New(n, options.Value.MailDomain);
        // Whole seconds, so what the hub is told and what the row stores are the same instant.
        var now = DateTime.UtcNow;
        DateTime? graceUntil = old == null ? null : now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond)) + Grace;
        try
        {
            await hub.RegisterAddressAsync(hash, old?.TokenHash, graceUntil);
        }
        catch (InboundMailHubException e)
        {
            return await FailureAsync(HubFailureKey(e, "rotate address"), old?.Address);
        }

        await InTransactionAsync(async () =>
        {
            if (old != null)
            {
                // Conditional claim: belt and braces next to the named lock.
                var claimed = await dbContext.InboxAddresses
                    .Where(a => a.Id == old.Id && a.Active)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.UpdatedAt, DateTime.UtcNow));
                if (claimed == 0) throw new InboxStateException("InboxTryAgainShortly");

                var tracked = await dbContext.InboxAddresses.SingleAsync(a => a.Id == old.Id);
                tracked.Active = false;
                tracked.GraceUntil = graceUntil;
                tracked.UpdatedByUserId = userId;
                await tracked.Update(dbContext);
            }

            await NewAddress(value, hash, userId).Create(dbContext);
        });
        return new OperationDataResult<InboxSettingsModel>(true, await ModelAsync(value));
    }

    public Task<OperationResult> ApproveSenderAsync(int inboxDocumentId, int userId) =>
        DecideSenderAsync(inboxDocumentId, approve: true, block: false, userId);

    public Task<OperationResult> RejectSenderAsync(int inboxDocumentId, bool block, int userId) =>
        DecideSenderAsync(inboxDocumentId, approve: false, block, userId);

    private async Task<OperationResult> DecideSenderAsync(int id, bool approve, bool block, int userId)
    {
        var operation = approve ? "approve sender" : "reject sender";
        try
        {
            var doc = await dbContext.InboxDocuments.AsNoTracking()
                .Where(d => d.Id == id && d.WorkflowState != Constants.WorkflowStates.Removed)
                .Select(d => new { d.Status, d.HubDocumentId }).FirstOrDefaultAsync();
            if (doc is not { Status: InboxDocumentStatus.SenderPending })
                return new OperationResult(false, localization.GetString("InboxSenderAlreadyDecided"));

            // The hub decides first: a 409 or an outage must leave no rule and no status change behind.
            try
            {
                await hub.SenderDecisionAsync(doc.HubDocumentId, approve);
            }
            catch (InboundMailHubException e)
            {
                return new OperationResult(false, localization.GetString(HubFailureKey(e, operation)));
            }

            await InTransactionAsync(async () =>
            {
                // Conditional claim, so two managers deciding at once cannot both win.
                var claimed = await dbContext.InboxDocuments
                    .Where(d => d.Id == id && d.Status == InboxDocumentStatus.SenderPending
                                && d.WorkflowState != Constants.WorkflowStates.Removed)
                    .ExecuteUpdateAsync(s => s.SetProperty(d => d.UpdatedAt, DateTime.UtcNow));
                if (claimed == 0)
                {
                    logger.LogWarning(
                        "Inbox {Operation}: the hub accepted {Decision} for {HubDocumentId}, but a concurrent decision won locally",
                        operation, approve ? "approve" : "reject", doc.HubDocumentId);
                    throw new InboxStateException("InboxSenderAlreadyDecided");
                }

                var tracked = await dbContext.InboxDocuments.SingleAsync(d => d.Id == id);
                tracked.Status = DecidedStatus(approve, tracked.DeliveredAt != null);
                tracked.UpdatedByUserId = userId;
                await tracked.Update(dbContext);

                if (approve || block)
                    await AddRuleIfMissingAsync(tracked.FromAddress,
                        approve ? InboxSenderRuleKind.Allow : InboxSenderRuleKind.Block, userId);
            });
        }
        catch (InboxStateException e)
        {
            return new OperationResult(false, StateMessage(e));
        }
        catch (Exception e)
        {
            return new OperationResult(false, UnexpectedMessage(e, operation));
        }

        return new OperationResult(true,
            localization.GetString(approve ? "InboxSenderApproved" : "InboxDocumentRejected"));
    }

    /// <summary>Approved: Ready when the PDF already arrived, otherwise it is still being prepared.</summary>
    private static InboxDocumentStatus DecidedStatus(bool approve, bool delivered)
    {
        if (!approve) return InboxDocumentStatus.Rejected;
        return delivered ? InboxDocumentStatus.Ready : InboxDocumentStatus.Preparing;
    }

    /// <summary>
    /// No rule for the unknown-sender placeholder or anything else a rule cannot hold (it would make the
    /// settings page fail its own validation on the next save), and no duplicate of a live rule.
    /// </summary>
    private async Task AddRuleIfMissingAsync(string fromAddress, InboxSenderRuleKind kind, int userId)
    {
        var pattern = Normalize(fromAddress);
        if (fromAddress == InboxHubService.UnknownSender || !IsValidPattern(pattern))
        {
            logger.LogWarning("Inbox: no {Kind} rule added for a sender address that is not a valid rule pattern", kind);
            return;
        }

        if (!await LiveRules().AnyAsync(r => r.Kind == kind && r.Pattern.ToLower() == pattern))
            await NewRule(pattern, kind, userId).Create(dbContext);
    }

    /// <summary>
    /// Insert-if-missing as one statement, then update. A duplicate PluginConfigurationValues name makes
    /// BasePn's configuration provider throw on load (see CalendarConfigurationBackfillService).
    /// Raw insert/ExecuteUpdate and no version row on purpose: the tracked Create/Update pattern could let
    /// concurrent hosts each insert a row, and duplicate PluginConfigurationValues rows must never exist.
    /// </summary>
    private async Task UpsertPolicyAsync(string policy, int userId)
    {
        var now = DateTime.UtcNow;
        await dbContext.Database.ExecuteSqlRawAsync(
            @"INSERT INTO `PluginConfigurationValues`
                  (`Name`, `Value`, `CreatedAt`, `UpdatedAt`, `Version`,
                   `WorkflowState`, `CreatedByUserId`, `UpdatedByUserId`)
              SELECT {0}, {1}, {2}, {2}, 1, {3}, {4}, {4} FROM DUAL
              WHERE NOT EXISTS (
                  SELECT 1 FROM `PluginConfigurationValues` `existing`
                  WHERE `existing`.`Name` = {0})",
            SenderVerdictResolver.PolicyName, policy, now, Constants.WorkflowStates.Created, userId);
        await dbContext.PluginConfigurationValues
            .Where(x => x.Name == SenderVerdictResolver.PolicyName)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Value, policy)
                .SetProperty(x => x.UpdatedAt, now)
                .SetProperty(x => x.UpdatedByUserId, userId));
    }

    /// <summary>One retry-safe transaction: the work may run again on a transient failure.</summary>
    private async Task InTransactionAsync(Func<Task> work)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        var attempt = 0;
        await strategy.ExecuteAsync(async () =>
        {
            if (attempt++ > 0) dbContext.ChangeTracker.Clear();
            await using var tx = await dbContext.Database.BeginTransactionAsync();
            await work();
            await tx.CommitAsync();
        });
    }

    /// <summary>
    /// Runs <paramref name="work"/> holding the address named lock on this context's connection.
    /// The lock is per connection, so the connection stays open until it is released.
    /// </summary>
    private async Task<T> WithAddressLockAsync<T>(Func<Task<T>> work, Func<Task<T>> lockUnavailable)
    {
        var database = dbContext.Database;
        var connection = database.GetDbConnection();
        var opened = connection.State != ConnectionState.Open;
        if (opened) await database.OpenConnectionAsync();
        try
        {
            if (!await ScalarIsOneAsync(connection, AddressLockSql))
            {
                logger.LogWarning("Inbox: the address lock was not granted within 15 seconds");
                return await lockUnavailable();
            }

            try
            {
                return await work();
            }
            finally
            {
                try
                {
                    await ScalarIsOneAsync(connection, AddressUnlockSql);
                }
                catch (Exception e)
                {
                    // MySQL releases a named lock when its connection closes, which happens just below.
                    logger.LogWarning(e, "Inbox: releasing the address lock failed");
                }
            }
        }
        finally
        {
            if (opened) await database.CloseConnectionAsync();
        }
    }

    private static async Task<bool> ScalarIsOneAsync(System.Data.Common.DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var result = await command.ExecuteScalarAsync();
        return result is not (null or DBNull) && Convert.ToInt64(result) == 1;
    }

    private async Task<OperationDataResult<InboxSettingsModel>> FailureAsync(string messageKey, string? address) =>
        new(false, localization.GetString(messageKey), await ModelAsync(address));

    private string HubFailureKey(InboundMailHubException e, string operation)
    {
        switch (e.Failure)
        {
            case InboundMailHubFailure.Conflict:
                logger.LogInformation("Inbox {Operation}: the hub is busy with it (409)", operation);
                return "InboxTryAgainShortly";
            case InboundMailHubFailure.NotConfigured:
                logger.LogWarning("Inbox {Operation}: {Reason}", operation, e.Message);
                return "InboxNotConfigured";
            default:
                logger.LogError(e, "Inbox {Operation}: the hub call failed", operation);
                return "InboxHubUnavailable";
        }
    }

    private string StateMessage(InboxStateException e)
    {
        dbContext.ChangeTracker.Clear();
        return localization.GetString(e.MessageKey);
    }

    private string UnexpectedMessage(Exception e, string operation)
    {
        dbContext.ChangeTracker.Clear();
        logger.LogError(e, "Inbox {Operation} failed", operation);
        return localization.GetString("InboxUnexpectedError");
    }

    private static string Normalize(string? pattern) => (pattern ?? "").Trim().ToLowerInvariant();

    private static bool IsValidPattern(string pattern) => pattern.Length <= 254 && PatternRegex().IsMatch(pattern);

    private IQueryable<InboxSenderRule> LiveRules() =>
        dbContext.InboxSenderRules.Where(r => r.WorkflowState != Constants.WorkflowStates.Removed);

    private Task<InboxAddress?> ActiveAddressAsync() =>
        dbContext.InboxAddresses.AsNoTracking().OrderByDescending(a => a.Id)
            .FirstOrDefaultAsync(a => a.Active && a.WorkflowState != Constants.WorkflowStates.Removed);

    private static InboxAddress NewAddress(string address, string tokenHash, int userId) => new()
    {
        Address = address, TokenHash = tokenHash, Active = true, CreatedByUserId = userId, UpdatedByUserId = userId
    };

    private static InboxSenderRule NewRule(string pattern, InboxSenderRuleKind kind, int userId) => new()
    {
        Pattern = pattern, Kind = kind, CreatedByUserId = userId, UpdatedByUserId = userId
    };

    private async Task<InboxSettingsModel> ModelAsync(string? address) => new()
    {
        Address = address,
        UnknownSenderPolicy = await dbContext.PluginConfigurationValues
            .Where(x => x.Name == SenderVerdictResolver.PolicyName)
            .Select(x => x.Value).FirstOrDefaultAsync() == PolicyRefuse ? PolicyRefuse : PolicyHold,
        SenderRules = await LiveRules().OrderBy(r => r.Pattern)
            .Select(r => new InboxSenderRuleModel { Id = r.Id, Pattern = r.Pattern, Kind = (int)r.Kind })
            .ToListAsync()
    };
}
