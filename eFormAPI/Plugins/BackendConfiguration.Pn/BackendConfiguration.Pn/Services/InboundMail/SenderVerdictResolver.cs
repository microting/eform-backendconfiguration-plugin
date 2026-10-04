using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Inbox;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformAngularFrontendBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;

namespace BackendConfiguration.Pn.Services.InboundMail;

/// <summary>
/// Decides whether mail from a sender reaches the Indbakke: a Block rule → "blocked"; a user's login
/// email (active users only) or an Allow rule → "allowed"; anything else → "unknown", or "blocked" under
/// the refuse policy.
/// Rules are an exact address ("jane.doe@example.org") or a domain ("@example.org").
/// </summary>
public class SenderVerdictResolver(BackendConfigurationPnDbContext dbContext, BaseDbContext baseDbContext)
{
    public const string PolicyName = "BackendConfigurationSettings:InboxUnknownSenderPolicy";

    public async Task<string> ResolveAsync(string fromAddress)
    {
        var address = fromAddress.Trim().ToLowerInvariant();
        var at = address.IndexOf('@');
        var domain = at >= 0 ? address[at..] : null; // no "@": only exact-address rules can match
        var rules = await dbContext.InboxSenderRules
            .Where(r => r.WorkflowState != Constants.WorkflowStates.Removed)
            .Select(r => new { Pattern = r.Pattern.ToLower(), r.Kind })
            .ToListAsync();

        bool Matches(InboxSenderRuleKind kind) =>
            rules.Any(r => r.Kind == kind && (r.Pattern == address || r.Pattern == domain));

        if (Matches(InboxSenderRuleKind.Block))
            return SenderVerdict.Blocked;
        if (Matches(InboxSenderRuleKind.Allow)
            || await baseDbContext.Users.AnyAsync(u => u.IsActive && u.Email != null && u.Email.ToLower() == address))
            return SenderVerdict.Allowed;

        // A missing row means the default policy, hold.
        var policy = await dbContext.PluginConfigurationValues
            .Where(x => x.Name == PolicyName)
            .Select(x => x.Value)
            .FirstOrDefaultAsync();
        return policy == "refuse" ? SenderVerdict.Blocked : SenderVerdict.Unknown;
    }
}
