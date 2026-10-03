using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;

namespace BackendConfiguration.Pn.Infrastructure.Helpers;

/// <summary>
/// The run marker of a one-time, admin-triggered calendar repair (#1294 monthly, #1375
/// weekly): a <c>PluginConfigurationValues</c> row whose value moves
/// <c>running:{claim id}</c> → <c>done</c> | <c>partial</c>. A <c>partial</c> run, or a
/// <c>running</c> one abandoned for over <see cref="AbandonedRunAfter"/>, may be claimed again.
/// </summary>
internal sealed class CalendarRepairRunMarker(BackendConfigurationPnDbContext dbContext, string name)
{
    public const string Running = "running";
    public const string Done = "done";
    public const string Partial = "partial";

    /// <summary>A <c>running</c> marker older than this belongs to a crashed run and may be re-claimed.</summary>
    public static readonly TimeSpan AbandonedRunAfter = TimeSpan.FromHours(1);

    public sealed record Row(string Value, DateTime? UpdatedAt);

    public Task<Row> ReadAsync(CancellationToken ct)
        => dbContext.PluginConfigurationValues
            .AsNoTracking()
            .Where(x => x.Name == name)
            .Select(x => new Row(x.Value, x.UpdatedAt))
            .FirstOrDefaultAsync(ct);

    /// <summary>A running marker holds the owning run's claim id, as running:{id} (a new Guid per run).</summary>
    public static bool IsRunning(string value)
        => value != null && value.StartsWith(Running, StringComparison.Ordinal);

    /// <summary>The marker's state without the claim token.</summary>
    public static string StateOf(string value) => IsRunning(value) ? Running : value;

    public static bool IsClaimable(Row row)
        => row.Value == Partial
           || (IsRunning(row.Value) && row.UpdatedAt < DateTime.UtcNow - AbandonedRunAfter);

    /// <summary>
    /// Atomic claim, one statement each way, so two concurrent requests can never both
    /// run: INSERT … WHERE NOT EXISTS when there is no marker (same statement shape as
    /// <c>CalendarConfigurationBackfillService.RepairLegacyMidnightConfigurationsAsync</c>,
    /// which explains why a unique index is not an option), or a conditional UPDATE of a
    /// <c>partial</c> / abandoned <c>running</c> marker. Exactly one caller gets a row.
    ///
    /// The marker value becomes running:{id} with an id only this run knows;
    /// the final write (<see cref="FinishAsync"/>) is conditional on it, so a run
    /// whose abandoned claim was re-claimed by another run can never overwrite that run's
    /// marker. Returns the token, or null when another caller won.
    /// </summary>
    public async Task<string> ClaimAsync(bool markerExists)
    {
        var now = DateTime.UtcNow;
        var token = $"{Running}:{Guid.NewGuid():N}";
        var affected = markerExists
            ? await dbContext.Database.ExecuteSqlRawAsync(
                @"UPDATE `PluginConfigurationValues`
                     SET `Value` = {1}, `UpdatedAt` = {2}, `Version` = `Version` + 1
                   WHERE `Name` = {0}
                     AND (`Value` = {3} OR (`Value` LIKE {4} AND `UpdatedAt` < {5}))",
                [name, token, now, Partial, Running + "%", now - AbandonedRunAfter],
                CancellationToken.None).ConfigureAwait(false)
            : await dbContext.Database.ExecuteSqlRawAsync(
                @"INSERT INTO `PluginConfigurationValues`
                      (`Name`, `Value`, `CreatedAt`, `UpdatedAt`, `Version`,
                       `WorkflowState`, `CreatedByUserId`, `UpdatedByUserId`)
                  SELECT {0}, {1}, {2}, {2}, 1, {3}, 1, 0 FROM DUAL
                  WHERE NOT EXISTS (
                      SELECT 1 FROM `PluginConfigurationValues` `existing`
                      WHERE `existing`.`Name` = {0})",
                [name, token, now, Constants.WorkflowStates.Created],
                CancellationToken.None).ConfigureAwait(false);
        return affected == 1 ? token : null;
    }

    /// <summary>
    /// Renews this run's lease (the marker's UpdatedAt) while it still holds the claim, so a
    /// long run is never taken for abandoned. False when the claim was taken over.
    /// </summary>
    public async Task<bool> RenewAsync(string claimToken)
        => await dbContext.Database.ExecuteSqlRawAsync(
            @"UPDATE `PluginConfigurationValues`
                 SET `UpdatedAt` = {2}
               WHERE `Name` = {0} AND `Value` = {1}",
            [name, claimToken, DateTime.UtcNow],
            CancellationToken.None).ConfigureAwait(false) == 1;

    /// <summary>
    /// Ends this run's claim — only while the marker still holds THIS run's token. False
    /// when another run re-claimed it (this run was taken for abandoned).
    /// </summary>
    public async Task<bool> FinishAsync(string claimToken, string state)
        => await dbContext.Database.ExecuteSqlRawAsync(
            @"UPDATE `PluginConfigurationValues`
                 SET `Value` = {1}, `UpdatedAt` = {2}, `Version` = `Version` + 1
               WHERE `Name` = {0} AND `Value` = {3}",
            [name, state, DateTime.UtcNow, claimToken],
            CancellationToken.None).ConfigureAwait(false) == 1;
}
