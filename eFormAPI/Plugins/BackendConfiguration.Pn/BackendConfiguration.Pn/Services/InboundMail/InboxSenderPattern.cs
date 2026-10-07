#nullable enable
using System.Linq;
using System.Text.RegularExpressions;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;

namespace BackendConfiguration.Pn.Services.InboundMail;

/// <summary>
/// A Block rule's pattern: an exact address ("jane.doe@example.org") or a domain ("@example.org"),
/// stored lower-cased. These are the shapes <see cref="SenderVerdictResolver"/> matches.
/// </summary>
public static partial class InboxSenderPattern
{
    [GeneratedRegex(@"^(@[a-z0-9.-]+\.[a-z]{2,}|[^@\s]+@[a-z0-9.-]+\.[a-z]{2,})$")]
    private static partial Regex PatternRegex();

    public static string Normalize(string? pattern) => (pattern ?? "").Trim().ToLowerInvariant();

    /// <summary>For an already normalized pattern.</summary>
    public static bool IsValid(string pattern) => pattern.Length <= 254 && PatternRegex().IsMatch(pattern);
}

/// <summary>
/// Block is the only rule kind the plugin reads or writes; legacy Allow rows are ignored everywhere.
/// </summary>
public static class InboxBlockRules
{
    public static IQueryable<InboxSenderRule> LiveBlockRules(this IQueryable<InboxSenderRule> rules) =>
        rules.Where(r => r.Kind == InboxSenderRuleKind.Block && r.WorkflowState != Constants.WorkflowStates.Removed);

    /// <param name="pattern">Already normalized and valid (<see cref="InboxSenderPattern"/>).</param>
    public static InboxSenderRule New(string pattern, int userId) => new()
    {
        Pattern = pattern, Kind = InboxSenderRuleKind.Block, CreatedByUserId = userId, UpdatedByUserId = userId
    };
}

/// <summary>
/// SenderPending rows were held under the removed unknown-sender policy and nothing produces them any more.
/// The few left are treated like Preparing: they can fail, be delivered (then Ready) or be rejected.
/// Delete this, and its uses, once no SenderPending rows remain.
/// </summary>
public static class InboxLegacy
{
    public const InboxDocumentStatus SenderPending = InboxDocumentStatus.SenderPending;
}
