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
using BackendConfiguration.Pn.Services.WorkerTagMembership;
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
/// <b>Liveness is not defined here.</b> It comes from
/// <see cref="BackendConfiguration.Pn.Services.WorkerTagMembership.IWorkerTagMembershipService"/>,
/// the single owner of the rule, which this list, the deploy resolver
/// (<see cref="BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation.CalendarAssignmentResolver"/>)
/// and the calendar's assignee filter all now share. The predicate moved there
/// UNCHANGED — this service's behaviour is identical before and after. What changed is
/// that the resolver, which lacked the <c>Site.WorkflowState</c> clause this list
/// already had, now shares it. The asymmetry documented here is therefore REDUCED, not
/// removed: exactly one clause of it survives, and it is this list's own
/// <c>Tag.WorkflowState == Created</c> filter (below), which
/// <c>GetTagIdsWithLiveMembersAsync</c> does not apply at all. A soft-removed <c>Tag</c>
/// with live members is consequently absent from this list while still resolving to
/// recipients through the resolver — see
/// <c>WorkerTagsListTests.GetWorkerTags_ExcludesRemovedTagEvenWithLiveMembers</c>. So
/// "appears in the Teams list" implies "would resolve to at least one recipient", but not
/// the reverse.
/// </para>
/// </summary>
public class BackendConfigurationWorkerTagsService(
    IEFormCoreService coreHelper,
    IWorkerTagMembershipService workerTagMembershipService,
    ILogger<BackendConfigurationWorkerTagsService> logger)
    : IBackendConfigurationWorkerTagsService
{
    public async Task<OperationDataResult<List<CommonDictionaryModel>>> GetWorkerTags()
    {
        try
        {
            var core = await coreHelper.GetCore().ConfigureAwait(false);
            await using var sdkDbContext = core.DbContextHelper.GetDbContext();

            // Server-side filter: no "fetch every tag and discard in the browser".
            // Which tags have at least one live member — the whole of the worker-group
            // rule, owned by IWorkerTagMembershipService and shared with the deploy
            // resolver and the calendar's assignee filter. One query, server-side; the
            // id set then filters Tags with a plain IN (...).
            var liveTagIds = await workerTagMembershipService
                .GetTagIdsWithLiveMembersAsync().ConfigureAwait(false);

            if (liveTagIds.Count == 0)
            {
                return new OperationDataResult<List<CommonDictionaryModel>>(
                    true, new List<CommonDictionaryModel>());
            }

            var liveTagIdList = liveTagIds.ToList();

            var tags = await sdkDbContext.Tags
                .AsNoTracking()
                // Same tag liveness the core list uses (SqlController.GetAllTags(false)
                // filters on == Created, not != Removed), so this list is always a strict
                // SUBSET of what the calendar received before — never a superset.
                .Where(t => t.WorkflowState == Constants.WorkflowStates.Created)
                .Where(t => liveTagIdList.Contains(t.Id))
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
            // all three Angular callers (the calendar's loadTeams, the calendar task
            // list's and the task list's) read `success` and drop the message. Adding a
            // 26-locale entry for text nobody sees is not worth it.
            return new OperationDataResult<List<CommonDictionaryModel>>(false, e.Message);
        }
    }
}
