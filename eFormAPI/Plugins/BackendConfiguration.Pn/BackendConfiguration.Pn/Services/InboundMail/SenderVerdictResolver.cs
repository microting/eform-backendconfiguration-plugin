using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Inbox;
using Microsoft.EntityFrameworkCore;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;

namespace BackendConfiguration.Pn.Services.InboundMail;

/// <summary>
/// Decides whether mail from a sender reaches the Indbakke: a Block rule → "blocked", anything else →
/// "allowed". Rules are an exact address ("jane.doe@example.org") or a domain ("@example.org").
/// Legacy Allow rule rows are ignored: they no longer change anything.
/// </summary>
public class SenderVerdictResolver(BackendConfigurationPnDbContext dbContext)
{
    public async Task<string> ResolveAsync(string fromAddress)
    {
        var address = InboxSenderPattern.Normalize(fromAddress);
        var at = address.IndexOf('@');
        var domain = at >= 0 ? address[at..] : null; // no "@": only exact-address rules can match
        var blocked = await dbContext.InboxSenderRules.LiveBlockRules()
            .Select(r => r.Pattern.ToLower())
            .AnyAsync(p => p == address || p == domain);
        return blocked ? SenderVerdict.Blocked : SenderVerdict.Allowed;
    }
}
