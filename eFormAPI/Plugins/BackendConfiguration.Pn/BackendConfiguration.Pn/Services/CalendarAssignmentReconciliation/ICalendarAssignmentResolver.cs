using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;

public interface ICalendarAssignmentResolver
{
    // Effective recipient site ids = explicit PlanningSites ∪ live members of each assigned worker tag
    // that are linked to the event's property (#1295).
    Task<HashSet<int>> ResolveEffectiveSiteIdsAsync(int areaRulePlanningId, CancellationToken ct = default);
}
