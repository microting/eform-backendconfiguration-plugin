using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;

public interface ICalendarAssignmentReconciliationService
{
    Task ReconcileEventAsync(int areaRulePlanningId, CancellationToken ct = default);
    Task ReconcileEventsForWorkerTagsAsync(IReadOnlyCollection<int> tagIds, CancellationToken ct = default);
}
