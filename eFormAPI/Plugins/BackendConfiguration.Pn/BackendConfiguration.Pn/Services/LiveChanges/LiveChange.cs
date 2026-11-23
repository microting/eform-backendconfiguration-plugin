#nullable enable
using System;
using BackendConfiguration.Pn.Hubs;

namespace BackendConfiguration.Pn.Services.LiveChanges;

/// <summary>What a <see cref="LiveChange"/> asks the publisher to do.</summary>
public enum LiveChangeKind
{
    /// <summary>A <c>TaskChanged</c> hint to a property group (see <see cref="LiveChange"/> for the ids).</summary>
    Task,

    /// <summary>An <c>AssignmentsChanged</c> hint to a worker's site group.</summary>
    Assignments,

    /// <summary>Take the site's connections out of the property group (in memory, done at once).</summary>
    RevokeProperty,

    /// <summary>End every connection of the site.</summary>
    RevokeSite,

    /// <summary>Take every connection (workers and web users) out of the property group: the property was removed.</summary>
    RevokePropertyForEveryone,

    /// <summary>End every connection of a host user (<c>EformUser.Id</c>), whatever its site.</summary>
    RevokeUser,
}

/// <summary>
/// One committed change, as cheap ids only. The capture side fills in what is
/// on the row; the publisher resolves the rest after the commit, in one
/// batched query per flush:
/// <list type="bullet">
///   <item><see cref="EventId"/> without <see cref="PropertyId"/>: the property of that AreaRulePlanning.</item>
///   <item><see cref="ItemPlanningId"/> without <see cref="EventId"/>: every AreaRulePlanning on that planning
///     (and, when <see cref="PropertyId"/> is set, only those of that property).</item>
/// </list>
/// A <see cref="LiveChangeKind.Task"/> change with only <see cref="PropertyId"/> asks for a
/// property-wide hint (<see cref="TaskChangeKinds.Property"/>).
/// </summary>
public readonly record struct LiveChange(
    LiveChangeKind Kind,
    int? PropertyId = null,
    int? EventId = null,
    int? ItemPlanningId = null,
    DateOnly? PlanDay = null,
    long? ComplianceId = null,
    string TaskKind = TaskChangeKinds.Changed,
    int? SdkSiteId = null,
    int? UserId = null)
{
    public static LiveChange ForTask(int propertyId, int? eventId = null, DateOnly? planDay = null,
        long? complianceId = null, string kind = TaskChangeKinds.Changed) =>
        new(LiveChangeKind.Task, PropertyId: propertyId, EventId: eventId, PlanDay: planDay,
            ComplianceId: complianceId, TaskKind: kind);

    /// <summary>A task row: it also tells the flush which event its planning belongs to (no lookup for that planning).</summary>
    public static LiveChange ForAreaRulePlanning(int eventId, int itemPlanningId, int propertyId,
        string kind = TaskChangeKinds.Changed) =>
        new(LiveChangeKind.Task, PropertyId: propertyId, EventId: eventId, ItemPlanningId: itemPlanningId, TaskKind: kind);

    public static LiveChange ForEvent(int eventId, string kind = TaskChangeKinds.Changed) =>
        new(LiveChangeKind.Task, EventId: eventId, TaskKind: kind);

    public static LiveChange ForPlanning(int itemPlanningId, int? propertyId = null, long? complianceId = null,
        string kind = TaskChangeKinds.Changed) =>
        new(LiveChangeKind.Task, PropertyId: propertyId, ItemPlanningId: itemPlanningId,
            ComplianceId: complianceId, TaskKind: kind);

    public static LiveChange ForAssignments(int sdkSiteId) => new(LiveChangeKind.Assignments, SdkSiteId: sdkSiteId);

    public static LiveChange ForRevokeProperty(int sdkSiteId, int propertyId) =>
        new(LiveChangeKind.RevokeProperty, PropertyId: propertyId, SdkSiteId: sdkSiteId);

    public static LiveChange ForRevokeSite(int sdkSiteId) => new(LiveChangeKind.RevokeSite, SdkSiteId: sdkSiteId);

    public static LiveChange ForRevokePropertyForEveryone(int propertyId) =>
        new(LiveChangeKind.RevokePropertyForEveryone, PropertyId: propertyId);

    public static LiveChange ForRevokeUser(int userId) => new(LiveChangeKind.RevokeUser, UserId: userId);
}

/// <summary>Tuning of the coalescing in <see cref="LiveChangePublisher"/>.</summary>
public static class LiveChangeLimits
{
    /// <summary>
    /// More distinct events than this for one property in one flush become a
    /// single property-wide hint (<see cref="TaskChangeKinds.Property"/>): a
    /// bulk edit then costs every client one <c>ListChanges</c>, not one per task.
    /// </summary>
    public const int PropertyWideThreshold = 5;
}
