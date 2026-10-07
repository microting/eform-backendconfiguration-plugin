#nullable enable
using System;
using System.Collections.Generic;

namespace BackendConfiguration.Pn.Infrastructure.Models.Inbox;

public class InboxSuggestionModel
{
    public int Id { get; set; }
    public int Kind { get; set; }
    public int TargetId { get; set; }
    public string TargetName { get; set; } = "";
    public int Source { get; set; }
    public double Confidence { get; set; }
    public string? Evidence { get; set; }
    public int? Page { get; set; }
    public string? Reason { get; set; }
}

public class InboxListItem
{
    public int Id { get; set; }
    public string FileName { get; set; } = "";
    public string? Subject { get; set; }
    public string FromAddress { get; set; } = "";
    public DateTime ReceivedAt { get; set; }
    public DateTime? ReadyBy { get; set; }
    public int Status { get; set; }
    public string? FailureReason { get; set; }
    public bool ReviewedByMicroting { get; set; }
    public List<InboxSuggestionModel> Suggestions { get; set; } = new();
}

public class FileInboxDocumentRequest
{
    public string Name { get; set; } = "";
    public List<int> PropertyIds { get; set; } = new();
    public List<int> TagIds { get; set; } = new();
}

public class InboxSenderRuleModel
{
    public int? Id { get; set; }
    public string Pattern { get; set; } = "";
    /// <summary>InboxSenderRuleKind: always 1 = Block. Allow (0) rules no longer exist; saving one is refused.</summary>
    public int Kind { get; set; }
}

public class InboxSettingsModel
{
    public string? Address { get; set; }
    /// <summary>The blocked senders. Every other sender is accepted.</summary>
    public List<InboxSenderRuleModel> SenderRules { get; set; } = new();
}
