#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;

namespace BackendConfiguration.Pn.Services.LiveChanges;

/// <summary>An AreaRulePlanning's ids: the event id, its items-planning planning, and its property.</summary>
public readonly record struct LiveEventRef(int EventId, int ItemPlanningId, int PropertyId);

/// <summary>What one flush needed to look up.</summary>
public sealed record LiveChangeLookups(
    IReadOnlyList<LiveEventRef> Events)
{
    public static readonly LiveChangeLookups Empty = new([]);
}

/// <summary>
/// What one flush must look up. <see cref="PlanningsOnProperty"/> are plannings
/// whose property the flush already knows (from a Compliance row): looked up
/// through the PropertyId index. <see cref="PlanningsAnywhere"/> have no known
/// property: <c>ItemPlanningId</c> has no index in the base package, so that
/// query scans; it runs only when such plannings remain.
/// </summary>
public sealed record LiveChangeLookupRequest(
    IReadOnlyCollection<int> EventIds,
    IReadOnlyCollection<(int PlanningId, int PropertyId)> PlanningsOnProperty,
    IReadOnlyCollection<int> PlanningsAnywhere)
{
    public bool IsEmpty => EventIds.Count == 0 && PlanningsOnProperty.Count == 0 && PlanningsAnywhere.Count == 0;
}

/// <summary>
/// Resolves the ids a capture could not see on the row. Called by the
/// publisher after the commit and off the caller's path: at most three small queries on
/// the plugin database (see <see cref="LiveChangeResolver"/>) per flush.
/// </summary>
public interface ILiveChangeResolver
{
    Task<LiveChangeLookups> LoadAsync(LiveChangeLookupRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// The production resolver: its own DI scope (a pooled context, never the
/// caller's, which may be gone by the time the flush runs). Removed rows are
/// included on purpose, so a deleted task still finds its property. Separate
/// queries, never an OR: events by primary key, plannings of a known property
/// through the PropertyId index, and only the rest by the unindexed ItemPlanningId.
/// </summary>
public class LiveChangeResolver(IServiceScopeFactory scopeFactory) : ILiveChangeResolver
{
    public async Task<LiveChangeLookups> LoadAsync(LiveChangeLookupRequest request, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var events = new List<LiveEventRef>();
        if (request.EventIds.Count > 0 || request.PlanningsOnProperty.Count > 0 || request.PlanningsAnywhere.Count > 0)
        {
            var db = scope.ServiceProvider.GetRequiredService<BackendConfigurationPnDbContext>();
            var arps = db.AreaRulePlannings.AsNoTracking();
            if (request.EventIds.Count > 0)
            {
                // Primary key.
                var ids = request.EventIds;
                events.AddRange(await arps.Where(x => ids.Contains(x.Id))
                    .Select(x => new LiveEventRef(x.Id, x.ItemPlanningId, x.PropertyId))
                    .ToListAsync(cancellationToken).ConfigureAwait(false));
            }

            if (request.PlanningsOnProperty.Count > 0)
            {
                // IX_AreaRulePlannings_PropertyId narrows it; the pairs are matched in memory.
                var properties = request.PlanningsOnProperty.Select(p => p.PropertyId).Distinct().ToList();
                var plannings = request.PlanningsOnProperty.Select(p => p.PlanningId).Distinct().ToList();
                var wanted = request.PlanningsOnProperty.ToHashSet();
                events.AddRange((await arps.Where(x => properties.Contains(x.PropertyId) && plannings.Contains(x.ItemPlanningId))
                        .Select(x => new LiveEventRef(x.Id, x.ItemPlanningId, x.PropertyId))
                        .ToListAsync(cancellationToken).ConfigureAwait(false))
                    .Where(e => wanted.Contains((e.ItemPlanningId, e.PropertyId))));
            }

            if (request.PlanningsAnywhere.Count > 0)
            {
                // ItemPlanningId has no index in the base package: only plannings no row of
                // the flush placed on a property get here (see LiveChangePublisher.LookupRequestFor).
                var plannings = request.PlanningsAnywhere;
                events.AddRange(await arps.Where(x => plannings.Contains(x.ItemPlanningId))
                    .Select(x => new LiveEventRef(x.Id, x.ItemPlanningId, x.PropertyId))
                    .ToListAsync(cancellationToken).ConfigureAwait(false));
            }
        }

        return new LiveChangeLookups(events);
    }
}
