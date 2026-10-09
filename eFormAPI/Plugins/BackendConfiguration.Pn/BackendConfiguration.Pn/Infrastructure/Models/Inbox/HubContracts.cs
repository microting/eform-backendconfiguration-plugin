#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace BackendConfiguration.Pn.Infrastructure.Models.Inbox;

// Wire contract with the central inbound mail service (see the tenant-side spec). JSON is camelCase,
// dates are UTC, and enum values travel as camelCase strings that the service maps case-insensitively.

public record ArrivedRequest(string HubDocumentId, string FromAddress, string? Subject, DateTime ReceivedAt,
    string FileName, long SizeBytes, string SpfResult, string DkimResult, DateTime? ReadyBy);

/// <summary><c>SenderVerdict</c> is one of the <see cref="Inbox.SenderVerdict"/> values.</summary>
public record ArrivedResponse(string SenderVerdict);

/// <summary>
/// The sender verdict values on the wire. The protocol also knows "unknown" (the removed hold for unknown
/// senders); this tenant no longer answers it: every sender is allowed unless a Block rule matches.
/// </summary>
public static class SenderVerdict
{
    public const string Allowed = "allowed";
    public const string Blocked = "blocked";
}

public record CatalogProperty(int Id, string Name, string? Address);

public record CatalogTag(int Id, string Name);

/// <summary><c>CanCreateTags</c>: this tenant answers <c>POST …/inbox/hub/tags</c>. Older tenants leave it out.</summary>
public record CatalogResponse(List<CatalogProperty> Properties, List<CatalogTag> Tags, bool CanCreateTags);

/// <summary>The tag name as staff typed it; the tenant trims it and removes a leading '#'.</summary>
public record CreateTagRequest(string? Name);

/// <summary><c>Created</c> is false when an existing tag was returned or a removed one restored.</summary>
public record CreateTagResponse(int Id, string Name, bool Created);

/// <summary><c>Kind</c> is "property" or "tag"; <c>Source</c> is "textMatch", "ai" or "reviewer".</summary>
public record DeliverSuggestion(string Kind, int TargetId, string Source, double Confidence, string? Evidence,
    int? Page, string? Reason);

public record DeliverMetadata(string HubDocumentId, int? PageCount, bool ReviewedByMicroting,
    List<DeliverSuggestion> Suggestions);

public record FailedRequest(string HubDocumentId, string Reason);

public static class HubJson
{
    /// <summary>camelCase out, case-insensitive in.</summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
