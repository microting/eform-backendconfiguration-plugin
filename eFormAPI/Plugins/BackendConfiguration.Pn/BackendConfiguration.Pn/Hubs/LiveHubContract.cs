#nullable enable
using System;
using System.Text.Json.Serialization;

namespace BackendConfiguration.Pn.Hubs;

/// <summary>
/// The live-update hub's whole wire contract, version <see cref="Version"/>
/// (docs/live-updates-hub-contract.md). Server to client only, and only HINTS:
/// every message carries ids, never task data, and a client reacts by calling
/// the existing gRPC <c>ListChanges</c> (or <c>GetEvent</c> for one full key).
/// A lost, duplicated or reordered message is therefore harmless; after any
/// gap (connect, reconnect, app resume) the client resyncs with ListChanges.
/// <para>
/// Evolution: fields and message kinds are added, never changed or removed.
/// A client ignores fields it does not know and treats an unknown
/// <see cref="TaskChangedMessage.Kind"/> as <see cref="TaskChangeKinds.Changed"/>.
/// A breaking change gets a new method name and a new capability string.
/// </para>
/// </summary>
public static class LiveHubContract
{
    /// <summary>Payload version, sent as <c>v</c> in every message.</summary>
    public const int Version = 1;

    /// <summary>
    /// The hub's path on the web host (REST) origin, e.g.
    /// <c>https://&lt;customerNo&gt;.microting.com/hub/backend-configuration</c>.
    /// Kanban's pattern (<c>/hub/kanban</c>). Deliberately outside the gRPC
    /// ingress prefixes (<c>/eform.</c>, <c>/timeplanning.</c>,
    /// <c>/backend_configuration.</c>), so the default ingress routes it, with
    /// WebSockets, to the web port.
    /// </summary>
    public const string HubPath = "/hub/backend-configuration";

    /// <summary>Listed by <c>GetServerInfo</c> when this hub is served.</summary>
    public const string Capability = "live_hub";

    /// <summary>The success code of a <see cref="JoinPropertyResult"/>; a refusal carries one of <see cref="Errors"/>.</summary>
    public static class Results
    {
        public const string Ok = "ok";
    }

    /// <summary>Client to server: the hub methods a client may invoke.</summary>
    public static class ClientMethods
    {
        /// <summary><c>JoinProperty(string propertyId)</c> returns <see cref="JoinPropertyResult"/>; propertyId as on the gRPC wire (decimal string).</summary>
        public const string JoinProperty = "JoinProperty";

        /// <summary><c>LeaveProperty(string propertyId)</c>; idempotent, never fails.</summary>
        public const string LeaveProperty = "LeaveProperty";
    }

    /// <summary>Server to client: the methods a client subscribes to (<c>connection.on(...)</c>).</summary>
    public static class ServerMethods
    {
        /// <summary>One <see cref="TaskChangedMessage"/>, sent to a property group.</summary>
        public const string TaskChanged = "TaskChanged";

        /// <summary>One <see cref="AssignmentsChangedMessage"/>, sent to the caller's own site group.</summary>
        public const string AssignmentsChanged = "AssignmentsChanged";

        /// <summary>
        /// One <see cref="RefusedMessage"/>, sent to a connection the server is
        /// about to close (refused connect, expired token). Best effort: the
        /// close may arrive without it.
        /// </summary>
        public const string Refused = "Refused";
    }

    /// <summary>
    /// The refusal codes a client can see: the <c>code</c> of a
    /// <see cref="RefusedMessage"/> (then the connection is closed) or of a
    /// <see cref="JoinPropertyResult"/>. Stable; clients match on them.
    /// </summary>
    public static class Errors
    {
        /// <summary>No valid token (header or <c>access_token</c> query), the token expired, or its user no longer exists. Connection refused or closed.</summary>
        public const string NotAuthenticated = "not_authenticated";

        /// <summary>The account is disabled or the worker resigned (same detail as the gRPC refusal). Connection refused.</summary>
        public const string AccountDisabled = "account_disabled";

        /// <summary>
        /// JoinProperty refused: the property does not exist, is not the
        /// caller's, the assignment was removed, the id is malformed, or the
        /// caller is not a worker. One answer for all, so ids cannot be probed.
        /// </summary>
        public const string PropertyNotAvailable = "property_not_available";

        /// <summary>Connection refused: the user already has <see cref="LiveHubLimits.MaxConnectionsPerUser"/> live connections.</summary>
        public const string TooManyConnections = "too_many_connections";

        /// <summary>The connection was closed by the server: the worker was removed or resigned, or its site removed.</summary>
        public const string Revoked = "revoked";

        /// <summary>JoinProperty refused: the connection already holds <see cref="LiveHubLimits.MaxPropertyGroupsPerConnection"/> property groups.</summary>
        public const string TooManyProperties = "too_many_properties";
    }

    /// <summary>What every property group name starts with.</summary>
    public const string PropertyGroupPrefix = "property-";

    /// <summary>The property's group. Joined only through a checked JoinProperty.</summary>
    public static string PropertyGroup(int propertyId) => PropertyGroupPrefix + propertyId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The worker's own SDK site group. Joined automatically on connect.</summary>
    public static string SiteGroup(int sdkSiteId) => $"site-{sdkSiteId}";
}

/// <summary>Values of <see cref="TaskChangedMessage.Kind"/>. Clients treat an unknown value as <see cref="Changed"/>.</summary>
public static class TaskChangeKinds
{
    /// <summary>An occurrence was created or changed (edited, answered, completed, moved in).</summary>
    public const string Changed = "changed";

    /// <summary>An occurrence (or the whole task, when only event_id is set) is gone for at least some callers.</summary>
    public const string Removed = "removed";

    /// <summary>Anything in the property may have changed (no key): resync the property.</summary>
    public const string Property = "property";
}

/// <summary>
/// <c>TaskChanged</c> payload. The key fields mirror gRPC <c>EventKey</c>
/// (same types, same meaning); the ones not known to the sender are omitted.
/// With event_id AND plan_day_key the client may call <c>GetEvent</c> for that
/// key; otherwise (or always, if simpler) it calls <c>ListChanges</c>.
/// </summary>
public sealed record TaskChangedMessage
{
    [JsonPropertyName("v")]
    public int V { get; init; } = LiveHubContract.Version;

    /// <summary>Property.id as on the gRPC wire (decimal string). Always set.</summary>
    [JsonPropertyName("property_id")]
    public required string PropertyId { get; init; }

    /// <summary>Event.id / EventKey.event_id (AreaRulePlanning id, decimal string).</summary>
    [JsonPropertyName("event_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EventId { get; init; }

    /// <summary>EventKey.plan_day_key, yyyy-MM-dd (UTC calendar day of the plan).</summary>
    [JsonPropertyName("plan_day_key")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PlanDayKey { get; init; }

    /// <summary>EventKey.compliance_id.</summary>
    [JsonPropertyName("compliance_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? ComplianceId { get; init; }

    /// <summary>One of <see cref="TaskChangeKinds"/>.</summary>
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }
}

/// <summary>
/// <c>AssignmentsChanged</c> payload: the receiving worker's property
/// assignments (or worker tags) changed. The client calls <c>ListChanges</c>,
/// whose <c>properties</c> is the new complete list, then joins the new
/// property groups and leaves the ones that are gone.
/// </summary>
public sealed record AssignmentsChangedMessage
{
    [JsonPropertyName("v")]
    public int V { get; init; } = LiveHubContract.Version;
}

/// <summary>
/// <c>JoinProperty</c>'s answer: <c>{"v":1,"code":"ok"}</c>, or a refusal code
/// (<c>property_not_available</c>, <c>too_many_properties</c>,
/// <c>account_disabled</c>, <c>not_authenticated</c>). A refusal is not an
/// invocation error; only an unexpected server fault fails the invocation.
/// </summary>
public sealed record JoinPropertyResult
{
    [JsonPropertyName("v")]
    public int V { get; init; } = LiveHubContract.Version;

    [JsonPropertyName("code")]
    public required string Code { get; init; }
}

/// <summary><c>Refused</c> payload: why the server closes this connection (one of <see cref="LiveHubContract.Errors"/>).</summary>
public sealed record RefusedMessage
{
    [JsonPropertyName("v")]
    public int V { get; init; } = LiveHubContract.Version;

    [JsonPropertyName("code")]
    public required string Code { get; init; }
}

/// <summary>
/// Connection settings of this hub only (per-hub options, so kanban's hub in
/// the same host keeps its own). Kanban runs on the SignalR defaults; the
/// keep-alive values below ARE those defaults, named so a mobile client can
/// size its own timers: the client's server timeout must be at least twice
/// <see cref="KeepAliveInterval"/> and its keep-alive at most half of
/// <see cref="ClientTimeoutInterval"/>.
/// </summary>
public static class LiveHubLimits
{
    /// <summary>Server ping interval (SignalR default). Keeps NATs and the ingress from idling the socket out.</summary>
    public static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(15);

    /// <summary>A client silent this long is dropped (SignalR default); it reconnects and resyncs.</summary>
    public static readonly TimeSpan ClientTimeoutInterval = TimeSpan.FromSeconds(30);

    /// <summary>Time a client has to complete the SignalR handshake (SignalR default).</summary>
    public static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Largest client-to-server message. Clients only send Join/LeaveProperty
    /// with one id, so this is far below the 32 KB default.
    /// </summary>
    public const long MaximumReceiveMessageSize = 4 * 1024;

    /// <summary>One invocation at a time per connection (SignalR default); joins are cheap and sequential.</summary>
    public const int MaximumParallelInvocationsPerClient = 1;

    /// <summary>Live connections one user may hold (devices, plus a reconnect overlapping its old socket).</summary>
    public const int MaxConnectionsPerUser = 10;

    /// <summary>Property groups one connection may hold; a worker is assigned to a handful.</summary>
    public const int MaxPropertyGroupsPerConnection = 200;
}
