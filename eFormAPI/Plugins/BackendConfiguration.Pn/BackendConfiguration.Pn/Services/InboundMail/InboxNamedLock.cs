#nullable enable
using System;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BackendConfiguration.Pn.Services.InboundMail;

/// <summary>
/// MySQL named locks (GET_LOCK, per tenant database) for Indbakke writes that have no unique index to lean
/// on. Every service that writes the same rows must use the same lock name.
/// </summary>
public static class InboxNamedLock
{
    /// <summary>Address creation and rotation never interleave.</summary>
    public const string Address = "inbox-address-create";

    /// <summary>Block-rule writes (settings replace, reject with block) never interleave, so no pattern is inserted twice.</summary>
    public const string SenderRules = "inbox-sender-rules";

    private const int TimeoutSeconds = 15;

    /// <summary>
    /// Runs <paramref name="work"/> holding the lock <paramref name="name"/> (one of the constants above, bound as a parameter) on
    /// <paramref name="db"/>'s connection. The lock is per connection, so the connection stays open until it
    /// is released; a transaction inside <paramref name="work"/> runs on that connection and commits before
    /// the next holder starts. <paramref name="lockUnavailable"/> answers when the lock is not granted in time.
    /// </summary>
    public static async Task<T> RunAsync<T>(DbContext db, string name, Func<Task<T>> work,
        Func<Task<T>> lockUnavailable, ILogger logger)
    {
        var database = db.Database;
        var connection = database.GetDbConnection();
        var opened = connection.State != ConnectionState.Open;
        if (opened) await database.OpenConnectionAsync();
        try
        {
            if (!await ScalarIsOneAsync(connection, $"SELECT GET_LOCK(CONCAT(@name, ':', DATABASE()), {TimeoutSeconds})", name))
            {
                logger.LogWarning("Inbox: the {Lock} lock was not granted within {Seconds} seconds", name, TimeoutSeconds);
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
                    await ScalarIsOneAsync(connection, "SELECT RELEASE_LOCK(CONCAT(@name, ':', DATABASE()))", name);
                }
                catch (Exception e)
                {
                    // MySQL releases a named lock when its connection closes, which happens just below.
                    logger.LogWarning(e, "Inbox: releasing the {Lock} lock failed", name);
                }
            }
        }
        finally
        {
            if (opened) await database.CloseConnectionAsync();
        }
    }

    private static async Task<bool> ScalarIsOneAsync(DbConnection connection, string sql, string name)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        // The lock name is bound, never spliced into the SQL.
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@name";
        parameter.Value = name;
        command.Parameters.Add(parameter);
        var result = await command.ExecuteScalarAsync();
        return result is not (null or DBNull) && Convert.ToInt64(result) == 1;
    }
}
