/*
The MIT License (MIT)

Copyright (c) 2007 - 2026 Microting A/S

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

namespace BackendConfiguration.Pn.Services.BackendConfigurationWorkerTagsService;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.eFormApi.BasePn.Infrastructure.Models.Common;

/// <summary>
/// The "teams" (worker groups) list the calendar offers when assigning an event to a
/// group of workers.
///
/// <para>
/// The SDK has ONE <c>Tags</c> table and no discriminator column: it holds both worker
/// groups and eForm/template tags. The core endpoint the calendar used to call
/// (<c>/api/tags/index</c> → <c>TagsService.Index</c> → <c>Core.GetAllTags(false)</c>)
/// returns all of them, so template tags were offered as teams and picking one produced
/// an event that resolved to nobody (#1213).
/// </para>
///
/// <para>
/// <b>The rule.</b> A tag is a worker group iff it has at least one LIVE member, i.e. at
/// least one <c>SiteTags</c> row pointing at a live, non-resigned site. There is no
/// schema change here on purpose — adding a <c>Kind</c> column to the SDK <c>Tag</c>
/// entity is a migration in a repo outside this epic, and the membership rule covers the
/// reported symptom. The known trade: a worker group with no members at all is invisible
/// here until someone is added to it.
/// </para>
///
/// <para>
/// <b>Why the filter is not applied in the core service.</b> <c>/api/tags/index</c> also
/// serves eForm template tagging across the product; narrowing it there would break
/// unrelated features (and would be a change in <c>eform-angular-frontend</c>, which this
/// plugin's CI pins to <c>stable</c>).
/// </para>
///
/// <para>
/// <b>Liveness is deliberately STRICTER than</b> <see cref="BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation.CalendarAssignmentResolver"/>,
/// which decides who actually receives a tag-assigned event. The resigned clause is
/// identical to the resolver's, but this query adds one the resolver does not have:
/// <c>Sites.Any(s =&gt; s.Id == st.SiteId &amp;&amp; s.WorkflowState != Removed)</c>. Stricter is the
/// safe direction — "appears in the Teams list" still implies "would resolve to at least
/// one recipient", because every tag listed here passes a superset of the resolver's
/// conditions. The asymmetry does expose a pre-existing resolver gap in the other
/// direction: a tag whose only member is a soft-deleted Site is (correctly) not listed
/// here, yet the resolver would still return that removed site id as a deploy target.
/// That is tracked separately and is not fixed here. This service also shares the
/// resolver's quirk of treating a site as resigned when ANY of its SiteWorker rows points
/// at a resigned Worker; device users are 1:1 with sites in practice, and diverging on
/// that predicate would be worse than a shared quirk.
/// </para>
/// </summary>
public class BackendConfigurationWorkerTagsService(
    IEFormCoreService coreHelper,
    ILogger<BackendConfigurationWorkerTagsService> logger)
    : IBackendConfigurationWorkerTagsService
{
    public async Task<OperationDataResult<List<CommonDictionaryModel>>> GetWorkerTags()
    {
        try
        {
            var core = await coreHelper.GetCore().ConfigureAwait(false);
            await using var sdkDbContext = core.DbContextHelper.GetDbContext();

            // Server-side filter: one query, no "fetch every tag and discard in the
            // browser". EXISTS over SiteTags is the whole of the worker-group rule.
            var tags = await sdkDbContext.Tags
                .AsNoTracking()
                // Same tag liveness the core list uses (SqlController.GetAllTags(false)
                // filters on == Created, not != Removed), so this list is always a strict
                // SUBSET of what the calendar received before — never a superset.
                .Where(t => t.WorkflowState == Constants.WorkflowStates.Created)
                .Where(t => sdkDbContext.SiteTags.Any(st =>
                    st.TagId == t.Id
                    && st.SiteId != null
                    && st.WorkflowState != Constants.WorkflowStates.Removed
                    // Core.SiteDelete soft-removes the Site (and its Worker) but leaves
                    // the SiteTags rows behind, so a deleted device user would otherwise
                    // keep an empty team alive in this list forever.
                    && sdkDbContext.Sites.Any(s => s.Id == st.SiteId
                                                   && s.WorkflowState != Constants.WorkflowStates.Removed)
                    // Resigned members do not receive new occurrences (#1184), so a tag
                    // whose members have all resigned delivers to nobody and is treated
                    // exactly like an empty tag.
                    && !sdkDbContext.SiteWorkers.Any(sw => sw.SiteId == st.SiteId && sw.Worker.Resigned)))
                // The core list is unordered (PK order in practice); keep that, so this
                // change alters membership only and never the order of what remains.
                .OrderBy(t => t.Id)
                .Select(t => new CommonDictionaryModel
                {
                    Id = t.Id,
                    Name = t.Name
                })
                .ToListAsync().ConfigureAwait(false);

            return new OperationDataResult<List<CommonDictionaryModel>>(true, tags);
        }
        catch (Exception e)
        {
            logger.LogError(e, "BackendConfigurationWorkerTagsService.GetWorkerTags: {Message}", e.Message);
            // No localisation key: Resources/localization.json requires a real
            // translation in all 26 shipped locales for every entry (pinned file-wide by
            // ExportLocalizationCompletenessTests), and this string is never rendered —
            // the only caller (the calendar's loadTeams) reads `success` and drops the
            // message. Adding a 26-locale entry for text nobody sees is not worth it.
            return new OperationDataResult<List<CommonDictionaryModel>>(false, e.Message);
        }
    }
}
