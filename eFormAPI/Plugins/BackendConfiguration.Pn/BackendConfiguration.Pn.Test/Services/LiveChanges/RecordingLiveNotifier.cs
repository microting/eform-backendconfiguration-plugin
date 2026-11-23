#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Hubs;
using BackendConfiguration.Pn.Services.LiveNotifier;

namespace BackendConfiguration.Pn.Test.Services.LiveChanges;

/// <summary>Records every <see cref="ILiveNotifier"/> call, in order; can be told to throw.</summary>
internal sealed class RecordingLiveNotifier : ILiveNotifier
{
    public sealed record Call(string Method, int? PropertyId = null, int? EventId = null, DateOnly? PlanDay = null,
        long? ComplianceId = null, string? Kind = null, int? SdkSiteId = null, int? UserId = null);

    private readonly List<Call> _calls = [];

    public Exception? Throw { get; set; }

    public List<Call> Calls
    {
        get
        {
            lock (_calls) return _calls.ToList();
        }
    }

    private Task Record(Call call)
    {
        lock (_calls) _calls.Add(call);
        if (Throw is { } e) throw e;
        return Task.CompletedTask;
    }

    public Task TaskChangedAsync(int propertyId, int? eventId = null, DateOnly? planDay = null, long? complianceId = null,
        string kind = TaskChangeKinds.Changed) =>
        Record(new Call("TaskChanged", propertyId, eventId, planDay, complianceId, kind));

    public Task AssignmentsChangedAsync(int sdkSiteId) => Record(new Call("AssignmentsChanged", SdkSiteId: sdkSiteId));

    public Task RevokePropertyAccessAsync(int sdkSiteId, int propertyId) =>
        Record(new Call("RevokePropertyAccess", PropertyId: propertyId, SdkSiteId: sdkSiteId));

    public Task RevokeSiteAsync(int sdkSiteId) => Record(new Call("RevokeSite", SdkSiteId: sdkSiteId));

    public Task RevokePropertyForEveryoneAsync(int propertyId) =>
        Record(new Call("RevokePropertyForEveryone", PropertyId: propertyId));

    public Task RevokeUserAsync(int userId) => Record(new Call("RevokeUser", UserId: userId));
}
