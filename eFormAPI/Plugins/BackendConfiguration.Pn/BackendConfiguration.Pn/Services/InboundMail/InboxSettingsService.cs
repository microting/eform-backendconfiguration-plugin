#nullable enable
using System;
using System.Data;
using System.Linq;
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
}

/// <summary>
/// Inbox settings: the archive address and the blocked senders. Every sender is accepted unless a Block
/// rule matches, so Block is the only rule kind this service reads or writes; legacy Allow rows are left
/// alone and ignored. Every hub call happens before anything is written, so a hub that says no (or is
/// down) leaves the tenant exactly as it was.
/// </summary>
public class InboxSettingsService(BackendConfigurationPnDbContext dbContext, IInboundMailHubClient hub,
    ICustomerNoProvider customerNo, IOptions<InboundMailHubOptions> options,
    IBackendConfigurationLocalizationService localization, ILogger<InboxSettingsService> logger) : IInboxSettingsService
{
    /// <summary>MySQL named lock: address creation and rotation never interleave (there is no unique index to lean on).</summary>
    private const string AddressLockSql = "SELECT GET_LOCK(CONCAT('inbox-address-create:', DATABASE()), 15)";
    private const string AddressUnlockSql = "SELECT RELEASE_LOCK(CONCAT('inbox-address-create:', DATABASE()))";
    private static readonly TimeSpan Grace = TimeSpan.FromDays(7);

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
            {
                // The address still shows, but without hub settings nothing reaches it: say so.
                return hub.IsConfigured
                    ? new OperationDataResult<InboxSettingsModel>(true, await ModelAsync(address.Address))
                    : await FailureAsync("InboxNotConfigured", address.Address);
            }

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

    /// <summary>
    /// Full replace of the blocked senders. Any other rule kind is refused rather than dropped, so a client
    /// still sending Allow rules learns that they no longer exist.
    /// </summary>
    public async Task<OperationResult> UpdateAsync(InboxSettingsModel model, int userId)
    {
        var rules = model?.SenderRules ?? [];
        // Only Block rules exist now; a null entry (malformed request body) is refused like any invalid rule.
        if (model == null || rules.Any(r => r == null || r.Kind != (int)InboxSenderRuleKind.Block))
            return new OperationResult(false, localization.GetString("InboxInvalidSenderRule"));
        var wanted = rules.Select(r => InboxSenderPattern.Normalize(r.Pattern)).Distinct().ToList();
        if (!wanted.All(InboxSenderPattern.IsValid))
            return new OperationResult(false, localization.GetString("InboxInvalidSenderRule"));

        try
        {
            await InTransactionAsync(async () =>
            {
                var existing = await dbContext.InboxSenderRules.LiveBlockRules().ToListAsync();
                foreach (var rule in existing.Where(r => !wanted.Contains(InboxSenderPattern.Normalize(r.Pattern))))
                {
                    rule.UpdatedByUserId = userId;
                    await rule.Delete(dbContext);
                }

                foreach (var pattern in wanted.Where(w =>
                             !existing.Any(r => InboxSenderPattern.Normalize(r.Pattern) == w)))
                {
                    await InboxBlockRules.New(pattern, userId).Create(dbContext);
                }
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

    private Task<InboxAddress?> ActiveAddressAsync() =>
        dbContext.InboxAddresses.AsNoTracking().OrderByDescending(a => a.Id)
            .FirstOrDefaultAsync(a => a.Active && a.WorkflowState != Constants.WorkflowStates.Removed);

    private static InboxAddress NewAddress(string address, string tokenHash, int userId) => new()
    {
        Address = address, TokenHash = tokenHash, Active = true, CreatedByUserId = userId, UpdatedByUserId = userId
    };

    private async Task<InboxSettingsModel> ModelAsync(string? address) => new()
    {
        Address = address,
        SenderRules = await dbContext.InboxSenderRules.LiveBlockRules().OrderBy(r => r.Pattern)
            .Select(r => new InboxSenderRuleModel { Id = r.Id, Pattern = r.Pattern, Kind = (int)r.Kind })
            .ToListAsync()
    };
}
