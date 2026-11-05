#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.ArchiveAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure;
using Microting.EformAngularFrontendBase.Infrastructure.Data;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;

namespace BackendConfiguration.Pn.Services.Leder;

/// <summary>
/// D-L19: moves every live LederAssignments row still keyed by login (SiteId null, D-L13) to the sites of that login's
/// workers (actor = the row's UpdatedByUserId), then marks the legacy row removed. A row whose login or workers no
/// longer exist is only marked removed. Runs at plugin startup. Self-narrowing, so no marker row: a converted row is
/// removed, a second run finds nothing, and a run cut short repeats only what it did not finish (a grant on an already
/// flagged site is a no-op).
/// </summary>
public class LederLegacyConversionService(BackendConfigurationPnDbContext dbContext, BaseDbContext baseDbContext,
    IEFormCoreService coreHelper, ILogger<LederLegacyConversionService> logger)
{
    /// <returns>How many legacy rows were handled.</returns>
    public async Task<int> RunAsync()
    {
        var legacy = await LederAssignmentStore.LiveLegacyRowsAsync(dbContext);
        if (legacy.Count == 0)
        {
            return 0;
        }

        var core = await coreHelper.GetCore();
        await using var sdk = core.DbContextHelper.GetDbContext();
        var handled = 0;
        // By id, each row re-read tracked: a failure clears the change tracker, and PnBase.Delete saves only tracked
        // changes, so a row loaded before the clear would silently stay live.
        foreach (var rowId in legacy.Select(x => x.Id).ToList())
        {
            // One failing row must not block the rest; it stays live and is retried at the next start.
            try
            {
                var row = await dbContext.LederAssignments.SingleAsync(x => x.Id == rowId);
                var siteIds = await SitesOfLoginAsync(sdk, row.EformUserId);
                foreach (var siteId in siteIds)
                {
                    await LederAssignmentStore.GrantSiteAsync(dbContext, siteId, row.UpdatedByUserId);
                }

                await row.Delete(dbContext);
                handled++;
                // Ids only, never the e-mail.
                logger.LogInformation(
                    "Leder legacy row {RowId} of login {LoginId}: {SiteCount} site(s) flagged, legacy row removed",
                    row.Id, row.EformUserId, siteIds.Count);
            }
            catch (Exception e)
            {
                dbContext.ChangeTracker.Clear();
                logger.LogWarning(e, "Leder legacy row {RowId} failed; it is retried at the next start", rowId);
            }
        }

        return handled;
    }

    /// <summary>The sites of the login's workers; none for a deleted login or one without an e-mail.</summary>
    private async Task<List<int>> SitesOfLoginAsync(MicrotingDbContext sdk, int? eformUserId)
    {
        var email = await baseDbContext.Users.AsNoTracking()
            .Where(u => u.Id == eformUserId)
            .Select(u => u.Email)
            .FirstOrDefaultAsync();
        return string.IsNullOrWhiteSpace(email) ? [] : await CallerSites.SitesOfWorkersAsync(sdk, email);
    }
}
