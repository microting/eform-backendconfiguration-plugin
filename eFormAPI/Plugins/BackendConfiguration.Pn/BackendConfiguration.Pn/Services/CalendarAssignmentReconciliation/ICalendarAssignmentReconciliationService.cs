using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;

public interface ICalendarAssignmentReconciliationService
{
    /// <returns>
    /// True when a recipient language keeps the Danish title/description only, because
    /// translation was unavailable (#1384); the caller may show that as a notice.
    /// </returns>
    Task<bool> ReconcileEventAsync(int areaRulePlanningId, CancellationToken ct = default);
    Task ReconcileEventsForWorkerTagsAsync(IReadOnlyCollection<int> tagIds, CancellationToken ct = default);
}
