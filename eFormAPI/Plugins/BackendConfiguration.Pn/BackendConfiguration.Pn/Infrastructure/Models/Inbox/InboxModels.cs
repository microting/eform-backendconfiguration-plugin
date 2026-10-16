#nullable enable
using System;
using System.Collections.Generic;

namespace BackendConfiguration.Pn.Infrastructure.Models.Inbox;

public class InboxSuggestionModel
{
    public int Id { get; set; }
    /// <summary>InboxSuggestionKind as an int: 0 Property, 1 Tag, 2 Folder, 3 DocumentDate.</summary>
    public int Kind { get; set; }
    /// <summary>Property, tag or folder id; 0 for DocumentDate.</summary>
    public int TargetId { get; set; }
    /// <summary>Property/tag name; a folder's path "Drift › Fakturaer"; empty for DocumentDate.</summary>
    public string TargetName { get; set; } = "";
    /// <summary>The suggested document date (DocumentDate only).</summary>
    public DateTime? TargetDate { get; set; }
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
    /// <summary>null = default folder (old apps, pre-folder web); 0 = refused (ArchiveChooseFolder); unknown or unreadable =
    /// ArchiveFolderNotFound. The new web and app UIs still require a folder before Arkivér (D9).</summary>
    public int? FolderId { get; set; }
    public DateTime? DocumentDate { get; set; }
    /// <summary>True when the reviewer pressed Godkend (or typed the date); false keeps it as a suggestion to check.</summary>
    public bool DocumentDateApproved { get; set; }
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
