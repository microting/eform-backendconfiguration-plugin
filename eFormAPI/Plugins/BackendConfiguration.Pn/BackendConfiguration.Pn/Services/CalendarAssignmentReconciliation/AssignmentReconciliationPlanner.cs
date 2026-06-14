using System.Collections.Generic;
using System.Linq;

namespace BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;

public sealed record OccurrencePlan(IReadOnlyList<int> ToAdd, IReadOnlyList<int> ToRemove);

public static class AssignmentReconciliationPlanner
{
    /// Desired = effective recipients. actualNonCompleted = sites with a live (non-completed)
    /// case for this occurrence. completed = sites whose case is completed (immutable).
    public static OccurrencePlan Plan(
        ISet<int> desired, ISet<int> actualNonCompleted, ISet<int> completed)
    {
        var toAdd = desired
            .Where(s => !actualNonCompleted.Contains(s) && !completed.Contains(s))
            .ToList();
        var toRemove = actualNonCompleted
            .Where(s => !desired.Contains(s))
            .Where(s => !completed.Contains(s))
            .ToList();
        return new OccurrencePlan(toAdd, toRemove);
    }
}
