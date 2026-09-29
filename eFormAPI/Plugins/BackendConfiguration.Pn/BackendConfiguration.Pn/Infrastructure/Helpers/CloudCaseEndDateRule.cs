namespace BackendConfiguration.Pn.Infrastructure.Helpers;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// #1294 — an SDK case the items-planning scheduler deployed through the Microting cloud
/// (a real <c>CaseCreate</c>) carries an EndDate in the cloud — the NextExecutionTime of
/// that deploy, i.e. the occurrence's original date — which the SDK Core API cannot change.
/// Moving such an occurrence to a LATER date would let the case expire on the device before
/// its new deadline. Local-only cases (<c>CaseCreateLocalOnly</c>) persist no date at all and
/// can be re-dated freely.
///
/// Shared by the calendar's occurrence relocation and the monthly re-anchor repair, so both
/// apply the same rule.
/// </summary>
public static class CloudCaseEndDateRule
{
    /// <summary>
    /// The SDK's <c>NextSyntheticMicrotingUidAsync</c> hands local-only cases uids from
    /// 2,000,000,000 up; anything below was a real cloud CaseCreate.
    /// </summary>
    public const int LocalOnlyMicrotingUidFloor = 2_000_000_000;

    public static bool IsCloudDeployed(int? microtingUid)
        => microtingUid is > 0 and < LocalOnlyMicrotingUidFloor;

    /// <summary>
    /// True when moving an occurrence from <paramref name="oldDate"/> to
    /// <paramref name="targetDate"/> would outlive the cloud EndDate of any of its live cases.
    /// </summary>
    public static bool MoveWouldOutliveCloudCase(DateTime oldDate, DateTime targetDate,
        IEnumerable<int?> liveCaseMicrotingUids)
        => targetDate.Date > oldDate.Date && liveCaseMicrotingUids.Any(IsCloudDeployed);
}
