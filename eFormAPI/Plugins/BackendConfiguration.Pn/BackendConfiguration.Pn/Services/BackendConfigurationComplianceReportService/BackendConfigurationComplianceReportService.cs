using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.ComplianceReport;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Data;
using Sentry;
using CalendarService =
    BackendConfiguration.Pn.Services.BackendConfigurationCalendarService.BackendConfigurationCalendarService;
using SdkDbContext = Microting.eForm.Infrastructure.MicrotingDbContext;

namespace BackendConfiguration.Pn.Services.BackendConfigurationComplianceReportService;

/// <summary>
/// Read model behind the standalone Compliance page (#1160 / #1161).
///
/// This is the former <c>BackendConfigurationCalendarService.GetComplianceReport</c>,
/// restructured into five phases so that paging, sorting and per-row enrichment
/// have somewhere to live:
///
///   A — one SQL query in the BC context: date window, the soft-removed rule,
///       PropertyId, TagIds and SiteIds. Projected, not materialised as entities.
///   B — one SQL query in the SDK context: the backing cases for the candidates.
///   C — in memory: occurrence-exception delete/move, effective board + BoardIds,
///       and the status filter. <c>Total</c> is the count at the END of this phase.
///   D — sort, then Skip/Take.
///   E — enrich the RETURNED PAGE only: titles, tag names, worker names, board
///       and property names.
///
/// Phase E is the performance point of the exercise: all of that enrichment used
/// to run over the entire match set.
///
/// <see cref="CalendarService.ResolveTaskTitle"/> and
/// <see cref="CalendarService.ComputeIsAllDay"/> are CALLED, never copied — they
/// are shared with GetTasksForWeek and a copy would drift (see #1160).
/// </summary>
public class BackendConfigurationComplianceReportService(
    IBackendConfigurationLocalizationService localizationService,
    IUserService userService,
    BackendConfigurationPnDbContext backendConfigurationPnDbContext,
    IEFormCoreService coreHelper,
    ItemsPlanningPnDbContext itemsPlanningPnDbContext,
    ILogger<BackendConfigurationComplianceReportService> logger)
    : IBackendConfigurationComplianceReportService
{
    /// <summary>
    /// Hard ceiling on the number of rows one call may return. It mainly bites on
    /// the unpaged path (<c>PageSize &lt;= 0</c>), which #1167/#1169 need; the
    /// cap is applied silently and logged, rather than thrown, so an export of a
    /// too-wide filter degrades instead of failing.
    ///
    /// It is skipped entirely when the caller passes <c>enforceRowCap: false</c>
    /// — see the parameter doc on
    /// <see cref="IBackendConfigurationComplianceReportService.Index"/>.
    /// </summary>
    public const int MaxRowsReturned = 5000;

    /// <inheritdoc />
    public async Task<OperationDataResult<ComplianceReportPagedModel>> Index(
        ComplianceReportRequestModel requestModel, bool enforceRowCap = true)
    {
        try
        {
            var userLanguageId = (await userService.GetCurrentUserLanguage()).Id;
            var dateFrom = requestModel.DateFrom.Date;
            var dateTo = requestModel.DateTo.Date.AddDays(1).AddTicks(-1);

            var sdkCore = await coreHelper.GetCore().ConfigureAwait(false);
            // NOTE: this context stays alive for the whole method — phase E reads
            // Sites from it. Disposing it right after BuildCandidateSet (which
            // owns phase B) would blow up only on the code path where a page
            // actually has assigned workers.
            await using var sdkDbContext = sdkCore.DbContextHelper.GetDbContext();

            // ==========================================================
            // Phases A, B and C — SHARED with Overview (#1162).
            // ==========================================================
            var candidateSet = await BuildCandidateSet(
                new CandidateFilter
                {
                    PropertyId = requestModel.PropertyId,
                    BoardIds = requestModel.BoardIds,
                    TagIds = requestModel.TagIds,
                    SiteIds = requestModel.SiteIds,
                    DateFrom = dateFrom,
                    DateTo = dateTo,
                    Status = requestModel.Status,
                    ComputeDisplayFields = true
                },
                sdkDbContext);

            var matched = candidateSet.MatchedRows;
            var boardNamesById = candidateSet.BoardNamesById;

            // Total is the count AFTER the exception delete, the NewDate range
            // re-check, the board filter and the status filter — never a
            // CountAsync() at the end of phase A, which would over-count by every
            // row those four drop.
            var total = matched.Count;

            // ==========================================================
            // Phase D — sort, then page.
            // ==========================================================
            var sortKey = NormaliseSortKey(requestModel.Sort);

            // A sort on a DISPLAY column needs that one column for the whole match
            // set: a set cannot be ordered by a value computed for only one page
            // of it. So the sort resolves exactly the requested column over the
            // matched rows and nothing else — the rest of the enrichment still
            // runs page-only in phase E. taskDate/completed/doneAt need nothing.
            Dictionary<int, AreaRulePlanning> arpDetailsById = null;
            Dictionary<int, string> propertyNamesById = null;

            switch (sortKey)
            {
                case SortKeys.Title:
                    arpDetailsById = await LoadArpDetails(matched);
                    ApplyTitles(matched, arpDetailsById, userLanguageId);
                    break;
                case SortKeys.PropertyName:
                    propertyNamesById = await LoadPropertyNames(matched);
                    ApplyPropertyNames(matched, propertyNamesById);
                    break;
                case SortKeys.BoardName:
                    // Board names are already in memory (one row per board).
                    ApplyBoardNames(matched, boardNamesById);
                    break;
            }

            var sorted = Sort(matched, sortKey, requestModel.IsSortDsc);

            List<MatchedRow> page;
            if (requestModel.PageSize <= 0)
            {
                // Unpaged: #1167 groups the whole filtered set, #1169 exports it.
                // The legacy calendar delegate also lands here, with
                // enforceRowCap: false — its contract is every matching row.
                if (enforceRowCap && sorted.Count > MaxRowsReturned)
                {
                    logger.LogWarning(
                        "BackendConfigurationComplianceReportService.Index: unpaged request matched {Total} rows, "
                        + "truncated to the {Cap}-row cap. Filters: propertyId={PropertyId}, status={Status}, "
                        + "dateFrom={DateFrom:yyyy-MM-dd}, dateTo={DateTo:yyyy-MM-dd}",
                        sorted.Count, MaxRowsReturned, requestModel.PropertyId, requestModel.Status,
                        dateFrom, dateTo);
                    page = sorted.Take(MaxRowsReturned).ToList();
                }
                else
                {
                    page = sorted;
                }
            }
            else
            {
                var take = enforceRowCap
                    ? Math.Min(requestModel.PageSize, MaxRowsReturned)
                    : requestModel.PageSize;
                if (take < requestModel.PageSize)
                {
                    logger.LogWarning(
                        "BackendConfigurationComplianceReportService.Index: PageSize {PageSize} exceeds the "
                        + "{Cap}-row cap and was clamped.", requestModel.PageSize, MaxRowsReturned);
                }

                // Skip is computed from the EFFECTIVE page size (take), not the
                // requested one: when PageSize is clamped to the cap, a skip based
                // on the requested size would leave the rows between the two sizes
                // unreachable from every PageIndex while Total still claims they
                // exist. Paging from `take` keeps the pages contiguous.
                // long arithmetic: PageIndex * page size can overflow int when a
                // caller sends a large page size with a non-zero index.
                var skip = (long)Math.Max(0, requestModel.PageIndex) * take;
                page = skip >= sorted.Count
                    ? new List<MatchedRow>()
                    : sorted.Skip((int)skip).Take(take).ToList();
            }

            // ==========================================================
            // Phase E — enrich the RETURNED PAGE only.
            // ==========================================================
            arpDetailsById ??= await LoadArpDetails(page);
            propertyNamesById ??= await LoadPropertyNames(page);

            var pageArpIds = page
                .Where(r => r.Arp != null)
                .Select(r => r.Arp.Id)
                .Distinct()
                .ToList();

            var arpTags = new List<AreaRulePlanningTag>();
            if (pageArpIds.Count > 0)
            {
                arpTags = await backendConfigurationPnDbContext.AreaRulePlanningTags
                    .Where(x => pageArpIds.Contains(x.AreaRulePlanningId))
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .ToListAsync();
            }
            var tagIdsByArpId = arpTags
                .GroupBy(x => x.AreaRulePlanningId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.ItemPlanningTagId).ToList());

            // The only cross-database hop left, and it is page-sized: tag ids live
            // in the BC database, tag NAMES in the items-planning one.
            var tagItemIds = arpTags.Select(x => x.ItemPlanningTagId).Distinct().ToList();
            var planningTagNames = tagItemIds.Count > 0
                ? await itemsPlanningPnDbContext.PlanningTags
                    .Where(x => tagItemIds.Contains(x.Id))
                    .ToDictionaryAsync(x => x.Id, x => x.Name)
                : new Dictionary<int, string>();

            var siteIdsByArpId = new Dictionary<int, List<int>>();
            foreach (var arpId in pageArpIds)
            {
                if (!arpDetailsById.TryGetValue(arpId, out var detail)) continue;
                siteIdsByArpId[arpId] = (detail.PlanningSites ?? new List<PlanningSite>())
                    .Where(ps => ps.WorkflowState != Constants.WorkflowStates.Removed)
                    .Select(ps => ps.SiteId)
                    .ToList();
            }

            var siteIdsNeeded = siteIdsByArpId.Values.SelectMany(x => x).Distinct().ToList();
            var siteNamesById = siteIdsNeeded.Count > 0
                ? await sdkDbContext.Sites
                    .Where(s => siteIdsNeeded.Contains(s.Id))
                    .ToDictionaryAsync(s => s.Id, s => s.Name)
                : new Dictionary<int, string>();

            ApplyTitles(page, arpDetailsById, userLanguageId);
            ApplyPropertyNames(page, propertyNamesById);
            ApplyBoardNames(page, boardNamesById);

            var entities = new List<ComplianceReportRowModel>(page.Count);
            foreach (var row in page)
            {
                var rowTagIds = row.Arp != null
                    ? tagIdsByArpId.GetValueOrDefault(row.Arp.Id, new List<int>())
                    : new List<int>();
                var rowSiteIds = row.Arp != null
                    ? siteIdsByArpId.GetValueOrDefault(row.Arp.Id, new List<int>())
                    : new List<int>();
                var arpDetail = row.Arp != null ? arpDetailsById.GetValueOrDefault(row.Arp.Id) : null;

                entities.Add(new ComplianceReportRowModel
                {
                    ComplianceId = row.Candidate.ComplianceId,
                    TaskDate = row.EffectiveTaskDate.ToString("yyyy-MM-dd"),
                    StartHour = row.StartHour,
                    Duration = row.Duration,
                    IsAllDay = row.IsAllDay,
                    Title = row.Title,
                    PropertyId = row.Candidate.PropertyId,
                    PropertyName = row.PropertyName ?? string.Empty,
                    BoardId = row.EffectiveBoardId == 0 ? null : row.EffectiveBoardId,
                    BoardName = row.BoardName ?? string.Empty,
                    Tags = rowTagIds
                        .Select(id => planningTagNames.GetValueOrDefault(id))
                        .Where(n => n != null)
                        .ToList(),
                    WorkerNames = rowSiteIds
                        .Select(id => siteNamesById.GetValueOrDefault(id, string.Empty))
                        .Where(n => !string.IsNullOrEmpty(n))
                        .ToList(),
                    Completed = row.Completed,
                    DoneAt = row.DoneAt,
                    SdkCaseId = row.Candidate.MicrotingSdkCaseId,
                    EformId = arpDetail?.AreaRule?.EformId,
                    PlanningId = row.Candidate.PlanningId,
                    AreaRulePlanningId = row.Arp?.Id,
                    // The template ACTUALLY answered, from the SDK case — not
                    // AreaRule.EformId (current configuration) and not
                    // Compliance.MicrotingSdkeFormId. See #1160 finding 1.
                    CheckListId = row.SdkCase?.CheckListId
                });
            }

            return new OperationDataResult<ComplianceReportPagedModel>(true, new ComplianceReportPagedModel
            {
                Total = total,
                Entities = entities
            });
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            logger.LogError(e, "BackendConfigurationComplianceReportService.Index: {Message}", e.Message);
            return new OperationDataResult<ComplianceReportPagedModel>(false,
                $"{localizationService.GetString("ErrorWhileGettingCalendarTasks")}: {e.Message}");
        }
    }

    // ==================================================================
    // Phases A, B and C — the SHARED candidate-set builder (#1162 §5).
    //
    // Everything here used to be inline in Index. It is EXTRACTED, not copied:
    // Index and Overview run byte-identical filtering, so a percentage in
    // Oversigt can never disagree with the row count in Detaljer for the same
    // filters. The only knobs are on CandidateFilter — status behaviour and
    // whether the occurrence display fields are computed.
    // ==================================================================

    /// <summary>
    /// Runs phases A (BC-context SQL), B (SDK-context SQL for the backing cases)
    /// and C (in-memory occurrence-exception delete/move, effective board and the
    /// status filter) and returns the surviving rows.
    /// </summary>
    /// <param name="filter">
    /// The filter set. <c>DateFrom</c> must already be a date and <c>DateTo</c> an
    /// end-of-day boundary — the caller normalises them, exactly as Index always
    /// did.
    /// </param>
    /// <param name="sdkDbContext">
    /// The SDK DbContext, owned by the CALLER: Index's phase E reads Sites from it
    /// long after this method returns, so it must outlive the builder.
    /// </param>
    private async Task<CandidateSet> BuildCandidateSet(
        CandidateFilter filter, SdkDbContext sdkDbContext)
    {
        var dateFrom = filter.DateFrom;
        var dateTo = filter.DateTo;

        // ==========================================================
        // Phase A — one SQL query, BC context.
        // ==========================================================
        var complianceQuery = backendConfigurationPnDbContext.Compliances
            .Where(x => x.Deadline >= dateFrom && x.Deadline <= dateTo)
            // Keep soft-removed rows that ever deployed a case: completed
            // occurrences are soft-removed but retain MicrotingSdkCaseId
            // (same shape as GetTasksForWeek's default branch).
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed
                        || x.MicrotingSdkCaseId > 0);

        if (filter.PropertyId.HasValue)
        {
            complianceQuery = complianceQuery.Where(x => x.PropertyId == filter.PropertyId.Value);
        }

        // TagIds / SiteIds push down as EXISTS, NOT as a join: a row that
        // matches two of the requested tags (or two of the requested sites)
        // must still come back exactly once, and a join would fan it out into
        // one duplicate per match — inflating Total and the page alike.
        // Both AreaRulePlanningTags and PlanningSites live in the SAME
        // DbContext as Compliances, so this is a genuine single-query filter;
        // only the tag NAMES come from the items-planning database, and names
        // are display-only, so that hop moves to page-sized enrichment.
        // Property patterns, so a body posting "tagIds": null (or a null
        // siteIds/boardIds) means "no filtering" exactly as an absent or
        // empty list does, instead of NRE-ing into the catch below.
        if (filter.TagIds is { Count: > 0 } tagIds)
        {
            complianceQuery = complianceQuery.Where(c =>
                backendConfigurationPnDbContext.AreaRulePlannings.Any(arp =>
                    arp.ItemPlanningId == c.PlanningId
                    && arp.WorkflowState != Constants.WorkflowStates.Removed
                    && backendConfigurationPnDbContext.AreaRulePlanningTags.Any(t =>
                        t.AreaRulePlanningId == arp.Id
                        && t.WorkflowState != Constants.WorkflowStates.Removed
                        && tagIds.Contains(t.ItemPlanningTagId))));
        }

        if (filter.SiteIds is { Count: > 0 } siteIds)
        {
            complianceQuery = complianceQuery.Where(c =>
                backendConfigurationPnDbContext.AreaRulePlannings.Any(arp =>
                    arp.ItemPlanningId == c.PlanningId
                    && arp.WorkflowState != Constants.WorkflowStates.Removed
                    && backendConfigurationPnDbContext.PlanningSites.Any(ps =>
                        ps.AreaRulePlanningsId == arp.Id
                        && ps.WorkflowState != Constants.WorkflowStates.Removed
                        && siteIds.Contains(ps.SiteId))));
        }

        // Project rather than materialise entities: nothing downstream writes
        // a Compliance, and the seven columns below are all that is read.
        var candidates = await complianceQuery
            .Select(x => new CandidateRow
            {
                ComplianceId = x.Id,
                ItemName = x.ItemName,
                PlanningId = x.PlanningId,
                PropertyId = x.PropertyId,
                Deadline = x.Deadline,
                MicrotingSdkCaseId = x.MicrotingSdkCaseId,
                WorkflowState = x.WorkflowState
            })
            .ToListAsync();

        // ==========================================================
        // Phase B — one SQL query, SDK context (a DIFFERENT database).
        // ==========================================================
        var caseIds = candidates
            .Select(c => c.MicrotingSdkCaseId)
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        var casesById = caseIds.Count > 0
            ? await sdkDbContext.Cases
                .Where(c => caseIds.Contains(c.Id))
                .Select(c => new SdkCaseInfo
                {
                    Id = c.Id,
                    Status = c.Status,
                    DoneAt = c.DoneAt,
                    DoneAtUserModifiable = c.DoneAtUserModifiable,
                    CheckListId = c.CheckListId
                })
                .ToDictionaryAsync(c => c.Id)
            : new Dictionary<int, SdkCaseInfo>();

        bool IsDone(CandidateRow c) =>
            c.MicrotingSdkCaseId > 0
            && casesById.TryGetValue(c.MicrotingSdkCaseId, out var sdk)
            && sdk.Status == 100;

        // ==========================================================
        // Phase C — in-memory filters. Everything here is either
        // cross-database or a coalesce over rows that must be in memory
        // anyway; see the comments on each block.
        // ==========================================================

        // AreaRulePlannings WITHOUT the display includes: phase C needs only
        // the Id (to key exceptions and calendar configurations) and the two
        // repeat columns ComputeIsAllDay reads. Translations and PlanningSites
        // are loaded page-sized in phase E.
        var planningIds = candidates.Select(x => x.PlanningId).Distinct().ToList();
        var arps = await backendConfigurationPnDbContext.AreaRulePlannings
            .Where(x => planningIds.Contains(x.ItemPlanningId))
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync();
        // Group, do NOT ToDictionary on ItemPlanningId: nothing in the schema
        // makes (ItemPlanningId, non-removed) unique, so two live ARPs on one
        // planning is a data anomaly rather than an impossibility — and this
        // set is now the PRE-status candidate set, so such a planning reaches
        // here even when all of its compliance rows would be filtered out
        // later. Failing the whole report over one anomalous planning is worse
        // than deterministically picking the lowest-Id ARP; the exception
        // grouping below already resolves its duplicates the same way.
        var arpByPlanningId = arps
            .GroupBy(x => x.ItemPlanningId)
            .ToDictionary(g => g.Key, g => g.OrderBy(a => a.Id).First());
        var arpIds = arps.Select(x => x.Id).ToList();

        // Same reasoning as arpByPlanningId above: IX_CalendarConfigurations_
        // AreaRulePlanningId is a plain, non-unique index and the entity carries
        // no uniqueness annotation, so two live configurations on one ARP is a
        // data anomaly rather than an impossibility — and arpIds comes from the
        // PRE-status candidate set, so it is strictly wider than the baseline's.
        // Failing the whole report (both compliance-report/index and the legacy
        // calendar/compliance-report, for the entire date window) over one
        // anomalous ARP is worse than deterministically picking the lowest-Id
        // configuration.
        var calConfigList = await backendConfigurationPnDbContext.CalendarConfigurations
            .Where(x => arpIds.Contains(x.AreaRulePlanningId))
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync();
        var calConfigs = calConfigList
            .GroupBy(x => x.AreaRulePlanningId)
            .ToDictionary(g => g.Key, g => g.OrderBy(c => c.Id).First());

        // "this"-scope occurrence exceptions: hide deleted occurrences, apply
        // date/hour/title/board overrides. These rows CANNOT be pushed into
        // SQL and dropped: they drive the IsDeleted skip and the NewDate move
        // as well as the board, so they are loaded here regardless.
        var exceptions = await backendConfigurationPnDbContext.CalendarOccurrenceExceptions
            .Where(x => arpIds.Contains(x.AreaRulePlanningId))
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync();
        var exceptionsByArpAndDate = exceptions
            .GroupBy(x => x.AreaRulePlanningId)
            .ToDictionary(g => g.Key, g => g
                .GroupBy(x => x.OriginalDate.Date)
                .ToDictionary(gg => gg.Key, gg => gg.First()));

        // Boards: one row per board, not per compliance row. Needed HERE
        // because the BoardIds filter runs in phase C — the effective board is
        // exception.BoardId ?? calConfig.BoardId ?? the property's lowest
        // non-removed board id, and the first arm only exists in memory.
        var propertyIds = candidates.Select(c => c.PropertyId).Distinct().ToList();
        var boardsForProperties = await backendConfigurationPnDbContext.CalendarBoards
            .Where(b => b.WorkflowState != Constants.WorkflowStates.Removed)
            .Where(b => propertyIds.Contains(b.PropertyId))
            .ToListAsync();
        var boardNamesById = boardsForProperties.ToDictionary(b => b.Id, b => b.Name);
        var defaultBoardIdByProperty = boardsForProperties
            .GroupBy(b => b.PropertyId)
            .ToDictionary(g => g.Key, g => g.OrderBy(b => b.Id).First().Id);

        var wantOpen = filter.Status is "open" or "all";
        var wantDone = filter.Status is "done" or "all";

        var matched = new List<MatchedRow>();
        foreach (var candidate in candidates)
        {
            arpByPlanningId.TryGetValue(candidate.PlanningId, out var arp);
            CalendarConfiguration calConfig = null;
            if (arp != null) calConfigs.TryGetValue(arp.Id, out calConfig);

            CalendarOccurrenceException exception = null;
            if (arp != null && exceptionsByArpAndDate.TryGetValue(arp.Id, out var perDate))
            {
                perDate.TryGetValue(candidate.Deadline.Date, out exception);
            }

            // A deleted occurrence is never returned, for ANY status.
            if (exception?.IsDeleted == true) continue;

            var effectiveTaskDate = exception?.NewDate?.Date ?? candidate.Deadline.Date;
            // A moved occurrence can land outside the requested window.
            if (effectiveTaskDate < dateFrom || effectiveTaskDate > dateTo) continue;

            // BOARD FILTER — deliberately NOT pushed into SQL. The effective
            // board is a three-arm coalesce whose first arm lives on the
            // occurrence-exception rows, and those rows have to be in memory
            // anyway (the IsDeleted skip and the NewDate move above). The SQL
            // form would need a LEFT JOIN on DATE(OriginalDate) = DATE(Deadline)
            // — non-sargable, so it defeats every index — plus a correlated
            // MIN(Id) sub-select, in exchange for zero extra narrowing over a
            // set already cut down by date, property, tags and sites.
            var effectiveBoardId = exception?.BoardId
                ?? calConfig?.BoardId
                ?? defaultBoardIdByProperty.GetValueOrDefault(candidate.PropertyId, 0);
            // A property with no non-removed board yields 0, and such rows are
            // excluded whenever a board filter is set. Preserved deliberately.
            if (filter.BoardIds is { Count: > 0 } boardIds
                && (effectiveBoardId == 0 || !boardIds.Contains(effectiveBoardId)))
            {
                continue;
            }

            var done = IsDone(candidate);
            // STATUS FILTER — structurally impossible in SQL. Done-ness is
            // sdkCase.Status == 100 and Cases lives in the SDK database behind
            // a different DbContext; EF cannot join across two contexts.
            // Compliance carries no completion column, so there is no
            // same-database proxy either. This is also why paging cannot be a
            // plain SQL Skip/Take.
            if (done)
            {
                if (!wantDone) continue;
            }
            else
            {
                // Not done + soft-removed = user-deleted occurrence: never shown.
                if (candidate.WorkflowState == Constants.WorkflowStates.Removed) continue;
                if (!wantOpen) continue;
            }

            var sdkCase = candidate.MicrotingSdkCaseId > 0
                ? casesById.GetValueOrDefault(candidate.MicrotingSdkCaseId)
                : null;

            // Occurrence DISPLAY fields. Overview (#1162) reads none of them
            // — it aggregates over PropertyId / EffectiveTaskDate / Completed
            // — so it opts out and the coalesce chain is skipped entirely.
            // Index always opts in, which is why its rows are unchanged.
            var isAllDay = filter.ComputeDisplayFields && CalendarService.ComputeIsAllDay(arp, calConfig);

            matched.Add(new MatchedRow
            {
                Candidate = candidate,
                Arp = arp,
                Exception = exception,
                EffectiveTaskDate = effectiveTaskDate,
                EffectiveBoardId = effectiveBoardId,
                Completed = done,
                SdkCase = sdkCase,
                IsAllDay = isAllDay,
                StartHour = !filter.ComputeDisplayFields || isAllDay
                    ? 0
                    : exception?.StartHour ?? calConfig?.StartHour ?? 9.0,
                Duration = !filter.ComputeDisplayFields || isAllDay
                    ? 0
                    : exception?.Duration ?? calConfig?.Duration ?? 1.0,
                DoneAt = filter.ComputeDisplayFields && done
                    ? sdkCase?.DoneAtUserModifiable ?? sdkCase?.DoneAt
                    : null
            });
        }

        return new CandidateSet
        {
            MatchedRows = matched,
            BoardNamesById = boardNamesById
        };
    }

    /// <summary>
    /// Per-property compliance aggregation for the Oversigt view (#1162).
    ///
    /// <para>
    /// A direct port of the prototype's <c>buildCompanySummaries</c>
    /// (<c>lorem-ipsum/kalender/compliance-overview.js:27-82</c>). One row per
    /// property that has at least one matching compliance row, plus a WEIGHTED
    /// totals row — the summed numerators over the summed denominators, never an
    /// average of the per-property percentages.
    /// </para>
    ///
    /// <para>
    /// <b>"Today" is <c>DateTime.UtcNow.Date</c></b>, evaluated ONCE at the top of
    /// this method and passed down, so that two rows can never be classified
    /// against different "todays" across a midnight boundary. UTC — not local, not
    /// user-local — because the whole compliance/calendar path already compares
    /// against <c>DateTime.UtcNow</c> exclusively (there is not one
    /// <c>DateTime.Now</c> in <c>BackendConfigurationCalendarService</c>), and
    /// deviating would make this the single local-time comparison in the path.
    /// </para>
    ///
    /// <para>
    /// <b>Consequence, accepted deliberately:</b> for a user in UTC+2 between 00:00
    /// and 02:00 local, the server's "today" is still yesterday — so a task dated
    /// today is not yet due, and a task dated yesterday is not yet overdue. Every
    /// threshold below hangs off this one value. If user-local boundaries are ever
    /// wanted, the fix is an explicit offset on the request model; do not guess one.
    /// </para>
    /// </summary>
    public async Task<OperationDataResult<ComplianceReportOverviewModel>> Overview(
        ComplianceReportOverviewRequestModel requestModel)
    {
        try
        {
            // Hoisted: ONE read of the clock per request, before any I/O, so no
            // two rows in one response can be classified against different
            // "todays" across a midnight boundary. Passed down to Aggregate.
            var today = DateTime.UtcNow.Date;

            var dateFrom = requestModel.DateFrom.Date;
            var dateTo = requestModel.DateTo.Date.AddDays(1).AddTicks(-1);

            var sdkCore = await coreHelper.GetCore().ConfigureAwait(false);
            await using var sdkDbContext = sdkCore.DbContextHelper.GetDbContext();

            // The SAME builder Index runs — see BuildCandidateSet. Status is "all"
            // because Oversigt aggregates done AND not-done rows together; that
            // path still drops user-deleted occurrences (soft-removed and not
            // done), which are never a compliance failure.
            var candidateSet = await BuildCandidateSet(
                new CandidateFilter
                {
                    PropertyId = requestModel.PropertyId,
                    BoardIds = requestModel.BoardIds,
                    TagIds = requestModel.TagIds,
                    SiteIds = requestModel.SiteIds,
                    DateFrom = dateFrom,
                    DateTo = dateTo,
                    Status = StatusAll,
                    // No titles, no all-day/StartHour/Duration, no DoneAt, no tag,
                    // worker or board NAMES, no CheckListId. Property names are the
                    // only lookup the aggregation needs.
                    ComputeDisplayFields = false
                },
                sdkDbContext);

            var matched = candidateSet.MatchedRows;
            var propertyNamesById = await LoadPropertyNames(matched);

            var overviewCandidates = matched.Select(r => new OverviewCandidate
            {
                PropertyId = r.Candidate.PropertyId,
                PropertyName = propertyNamesById.GetValueOrDefault(r.Candidate.PropertyId, string.Empty),
                // InvariantCulture deliberately: this format and the TryParseExact
                // in Aggregate use the same culture, so the round-trip holds
                // whatever the server's CurrentCulture is (under a non-Gregorian
                // calendar such as th-TH or ar-SA the year would differ).
                // Index formats the same value with the current culture
                // (ComplianceReportRowModel.TaskDate, :255). That inconsistency
                // predates #1162 and is deliberately not changed here — Index's
                // output is certified unchanged by this work.
                TaskDate = r.EffectiveTaskDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Completed = r.Completed
            });

            return new OperationDataResult<ComplianceReportOverviewModel>(
                true, Aggregate(overviewCandidates, today));
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            logger.LogError(e, "BackendConfigurationComplianceReportService.Overview: {Message}", e.Message);
            return new OperationDataResult<ComplianceReportOverviewModel>(false,
                $"{localizationService.GetString("ErrorWhileGettingCalendarTasks")}: {e.Message}");
        }
    }

    /// <summary>
    /// The pure aggregation half of <see cref="Overview"/>, split out so the
    /// prototype's maths suite can be ported against it directly — including the
    /// unparseable-date case, which the database cannot produce
    /// (<c>Compliance.Deadline</c> is a non-null <c>DateTime</c>) but which the
    /// prototype's NaN semantics still define.
    ///
    /// <para>
    /// <paramref name="today"/> is passed in, never read from the clock here: the
    /// caller hoists <c>DateTime.UtcNow.Date</c> so every row in one response is
    /// classified against one value.
    /// </para>
    /// </summary>
    internal static ComplianceReportOverviewModel Aggregate(
        IEnumerable<OverviewCandidate> candidates, DateTime today)
    {
        var byProperty = new Dictionary<int, ComplianceReportOverviewRowModel>();

        foreach (var candidate in candidates ?? [])
        {
            if (!byProperty.TryGetValue(candidate.PropertyId, out var row))
            {
                // Rows are created LAZILY, on first case — which is what makes
                // "a property with no cases produces no row" true by construction.
                row = new ComplianceReportOverviewRowModel
                {
                    PropertyId = candidate.PropertyId,
                    PropertyName = candidate.PropertyName ?? string.Empty
                };
                byProperty[candidate.PropertyId] = row;
            }

            var parsed = DateTime.TryParseExact(
                candidate.TaskDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var taskDate);
            DateTime? taskDay = parsed ? taskDate.Date : null;

            // NOTE THE NEGATION: !(taskDate > today), not (taskDate <= today).
            // The two differ exactly on an unparseable date — the prototype's NaN
            // (compliance-overview.js:15-20, :50) — where !(NaN > x) is TRUE. A row
            // whose date cannot be read must NOT silently vanish out of the
            // denominator, so it counts as DUE. It is deliberately NOT overdue
            // below (NaN < x is false); keep the asymmetry.
            var isDue = taskDay is null || !(taskDay.Value > today);

            row.Total++;
            if (isDue)
            {
                row.DueTotal++;
                if (candidate.Completed) row.DueDone++;
            }

            if (candidate.Completed)
            {
                row.Done++;
            }
            // STRICTLY before today: a task due TODAY and not done raises DueTotal
            // (so it lowers the percentage) but is not overdue.
            else if (taskDay is not null && taskDay.Value < today)
            {
                row.Overdue++;
            }
        }

        var totals = new ComplianceReportOverviewRowModel
        {
            PropertyId = 0,
            // No "I alt" here — the label is #1164's; the API carries no Danish.
            PropertyName = null
        };

        foreach (var row in byProperty.Values)
        {
            row.CompliancePct = Percent(row.DueDone, row.DueTotal);
            totals.Total += row.Total;
            totals.Done += row.Done;
            totals.Overdue += row.Overdue;
            totals.DueTotal += row.DueTotal;
            totals.DueDone += row.DueDone;
        }

        // WEIGHTED: summed numerators over summed denominators, computed once at
        // the end. Averaging Rows[].CompliancePct reads naturally and is wrong by
        // a factor of 50 in the pinned 1/1 + 0/100 case (which must give 1).
        totals.CompliancePct = Percent(totals.DueDone, totals.DueTotal);

        return new ComplianceReportOverviewModel
        {
            // Documented stable default order — reproducible in CI, not a contract:
            // #1164 sorts client-side. PropertyId breaks ties between same-named
            // properties so the order is total.
            Rows = byProperty.Values
                .OrderBy(r => r.PropertyName ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(r => r.PropertyId)
                .ToList(),
            Totals = totals
        };
    }

    /// <summary>
    /// <c>round(done / total * 100)</c>, or <c>null</c> when <paramref name="total"/>
    /// is 0 (the prototype's <c>percentOf</c>, <c>compliance-overview.js:22-25</c>:
    /// <c>if (!total) return null</c>) — never 0, never NaN.
    ///
    /// <para>
    /// <b><c>MidpointRounding.AwayFromZero</c> is load-bearing.</b> C#'s
    /// <c>Math.Round(double)</c> defaults to BANKER'S rounding (ToEven); JS
    /// <c>Math.round</c> rounds halves up. They disagree on every exact midpoint:
    /// 1 of 8 due is 12.5, which is 12 under ToEven and 13 under AwayFromZero /
    /// JS. Percentages here are never negative, so away-from-zero and half-up
    /// coincide. The prototype's pinned 33/41 case (80.4878…) is NOT a midpoint
    /// and does NOT discriminate between the two modes — which is exactly why
    /// ComplianceReportOverviewTests carries the 1/8 case as well.
    /// </para>
    /// </summary>
    private static int? Percent(int done, int total) =>
        total == 0
            ? null
            : (int)Math.Round(done / (double)total * 100d, MidpointRounding.AwayFromZero);


    /// <summary>
    /// The Rapport view's read model (#1166): the filtered compliance set grouped
    /// by TAG, then by the eForm TEMPLATE that was actually answered, each template
    /// group carrying its own column schema and one keyed cell bag per case.
    ///
    /// <para>
    /// Runs the SAME <see cref="BuildCandidateSet"/> as <see cref="Index"/> and
    /// <see cref="Overview"/>, so a Rapport section can never disagree with the
    /// Detaljer row count or the Oversigt percentage for identical filters.
    /// </para>
    ///
    /// <para>
    /// <b>Unpaged by design.</b> <c>PageIndex</c>, <c>PageSize</c>, <c>Sort</c> and
    /// <c>IsSortDsc</c> on the request are IGNORED here — Rapport groups the whole
    /// filtered set — and the same <see cref="MaxRowsReturned"/> ceiling applies,
    /// silently and logged, so a too-wide filter degrades instead of failing.
    /// </para>
    ///
    /// <para>
    /// <b>Rows with no answered template are dropped.</b> A compliance row with no
    /// backing SDK case (never deployed) or whose case has a null
    /// <c>CheckListId</c> has no answers and therefore no column set to render
    /// against. Rapport is a report of answers, so those rows form no group; the
    /// number dropped is logged rather than silently swallowed. Detaljer (#1165)
    /// still shows them.
    /// </para>
    /// </summary>
    public async Task<OperationDataResult<List<ComplianceReportTagGroupModel>>> EformColumns(
        ComplianceReportRequestModel requestModel)
    {
        try
        {
            // The SDK Language entity, not just its id: Advanced_TemplateFieldReadAll
            // takes one, and the option/checklist translation fallbacks key off it.
            var userLanguage = await userService.GetCurrentUserLanguage();
            var dateFrom = requestModel.DateFrom.Date;
            var dateTo = requestModel.DateTo.Date.AddDays(1).AddTicks(-1);

            var sdkCore = await coreHelper.GetCore().ConfigureAwait(false);
            // Must outlive every enrichment pass below — worker names, the column
            // derivation and all of the answer/image loading read from it.
            await using var sdkDbContext = sdkCore.DbContextHelper.GetDbContext();

            var candidateSet = await BuildCandidateSet(
                new CandidateFilter
                {
                    PropertyId = requestModel.PropertyId,
                    BoardIds = requestModel.BoardIds,
                    TagIds = requestModel.TagIds,
                    SiteIds = requestModel.SiteIds,
                    DateFrom = dateFrom,
                    DateTo = dateTo,
                    Status = requestModel.Status,
                    // DoneAt is the "Udført dato" column, so the display fields are on.
                    ComputeDisplayFields = true
                },
                sdkDbContext);

            // Rows with no answered template are dropped BEFORE the cap, never
            // after. Capping first and filtering second makes the cap mean
            // something unpredictable — 6000 matches of which half never deployed
            // would yield ~2500 groups while the log claimed a 5000-row
            // truncation. Filtering first makes MaxRowsReturned a ceiling on the
            // rows actually RENDERED, which is what the number is for.
            var answerable = candidateSet.MatchedRows
                .Where(r => r.Candidate.MicrotingSdkCaseId > 0 && r.SdkCase?.CheckListId != null)
                .ToList();

            var withoutTemplate = candidateSet.MatchedRows.Count - answerable.Count;
            if (withoutTemplate > 0)
            {
                logger.LogInformation(
                    "BackendConfigurationComplianceReportService.EformColumns: {Dropped} of {Total} matching rows "
                    + "have no answered template (no SDK case, or the case has no CheckListId) and form no group.",
                    withoutTemplate, candidateSet.MatchedRows.Count);
            }

            // One deterministic order for the whole response: cases appear inside
            // every template group in occurrence-date order, oldest first, with the
            // compliance id as the total tiebreak. The cap is applied to that
            // order, so it truncates the tail rather than an arbitrary slice.
            var answered = answerable
                .OrderBy(r => r.EffectiveTaskDate)
                .ThenBy(r => r.Candidate.ComplianceId)
                .ToList();

            if (answered.Count > MaxRowsReturned)
            {
                logger.LogWarning(
                    "BackendConfigurationComplianceReportService.EformColumns: {Total} rows with an answered "
                    + "template, truncated to the {Cap}-row cap. Filters: propertyId={PropertyId}, "
                    + "status={Status}, dateFrom={DateFrom:yyyy-MM-dd}, dateTo={DateTo:yyyy-MM-dd}",
                    answered.Count, MaxRowsReturned, requestModel.PropertyId, requestModel.Status,
                    dateFrom, dateTo);
                answered = answered.Take(MaxRowsReturned).ToList();
            }

            if (answered.Count == 0)
            {
                return new OperationDataResult<List<ComplianceReportTagGroupModel>>(
                    true, new List<ComplianceReportTagGroupModel>());
            }

            // ==========================================================
            // Enrichment — the same helpers Index's phase E uses.
            // ==========================================================
            var arpDetailsById = await LoadArpDetails(answered);
            var propertyNamesById = await LoadPropertyNames(answered);
            ApplyTitles(answered, arpDetailsById, userLanguage.Id);
            ApplyPropertyNames(answered, propertyNamesById);

            var arpIds = answered
                .Where(r => r.Arp != null)
                .Select(r => r.Arp.Id)
                .Distinct()
                .ToList();

            // Tags are read per PLANNING, over EVERY live ARP on it — not off
            // row.Arp alone. BuildCandidateSet pins row.Arp to the LOWEST-Id live
            // ARP (a deliberate, documented choice for the two-live-ARPs-on-one-
            // planning data anomaly), while its tag filter is an EXISTS over ANY
            // live ARP of the planning. Reading tags off row.Arp only would let a
            // row whose tag sits on a higher-Id ARP pass the filter and then fall
            // into the untagged bucket — a "Uden tag" section inside a report the
            // user filtered TO a named tag. One join, no per-row query.
            var planningIdsForTags = answered
                .Select(r => r.Candidate.PlanningId)
                .Distinct()
                .ToList();

            // No emptiness guard needed: answered.Count == 0 already returned above,
            // so planningIdsForTags always holds at least one id.
            var arpTags = await backendConfigurationPnDbContext.AreaRulePlanningTags
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Join(
                    backendConfigurationPnDbContext.AreaRulePlannings
                        .Where(a => a.WorkflowState != Constants.WorkflowStates.Removed)
                        .Where(a => planningIdsForTags.Contains(a.ItemPlanningId)),
                    tag => tag.AreaRulePlanningId,
                    arp => arp.Id,
                    (tag, arp) => new { arp.ItemPlanningId, tag.ItemPlanningTagId })
                .Distinct()
                .ToListAsync();

            var tagIdsByPlanningId = arpTags
                .GroupBy(x => x.ItemPlanningId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.ItemPlanningTagId).Distinct().ToList());

            // Tag ids live in the BC database, tag NAMES in the items-planning one.
            var tagItemIds = arpTags.Select(x => x.ItemPlanningTagId).Distinct().ToList();
            var planningTagNames = tagItemIds.Count > 0
                ? await itemsPlanningPnDbContext.PlanningTags
                    .Where(x => tagItemIds.Contains(x.Id))
                    .ToDictionaryAsync(x => x.Id, x => x.Name)
                : new Dictionary<int, string>();

            var siteIdsByArpId = new Dictionary<int, List<int>>();
            foreach (var arpId in arpIds)
            {
                if (!arpDetailsById.TryGetValue(arpId, out var detail)) continue;
                siteIdsByArpId[arpId] = (detail.PlanningSites ?? new List<PlanningSite>())
                    .Where(ps => ps.WorkflowState != Constants.WorkflowStates.Removed)
                    .Select(ps => ps.SiteId)
                    .ToList();
            }

            var siteIdsNeeded = siteIdsByArpId.Values.SelectMany(x => x).Distinct().ToList();
            var siteNamesById = siteIdsNeeded.Count > 0
                ? await sdkDbContext.Sites
                    .Where(s => siteIdsNeeded.Contains(s.Id))
                    .ToDictionaryAsync(s => s.Id, s => s.Name)
                : new Dictionary<int, string>();

            // ==========================================================
            // Column schemas, answers and images — ONCE per template, never
            // per case, and every bulk query led by FieldId (#1160 finding 2).
            // ==========================================================
            var projector = new ComplianceReportEformProjector(sdkCore, sdkDbContext, userLanguage, logger);

            var caseIdsByCheckList = answered
                .GroupBy(r => r.SdkCase.CheckListId.Value)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(r => r.Candidate.MicrotingSdkCaseId).Distinct().ToList());

            var projections = new Dictionary<int, ComplianceReportEformProjector.TemplateProjection>();
            foreach (var (checkListId, caseIds) in caseIdsByCheckList)
            {
                projections[checkListId] = await projector.ProjectAsync(checkListId, caseIds);
            }

            // ==========================================================
            // Tag -> template grouping (#1160 decision 5).
            // ==========================================================
            // NEVER key a Dictionary on a NULLABLE VALUE TYPE here. Dictionary<TKey,
            // TValue> null-checks its key in both FindValue and TryInsert, and
            // boxing an EMPTY Nullable<int> produces a null reference — so
            // Dictionary<int?, …> throws ArgumentNullException the moment the
            // untagged group is looked up or inserted, which is the NORMAL path,
            // not an edge case. (The compiler would normally warn CS8714, but this
            // csproj sets no <Nullable>, so nothing warns.) Hence: a plain int-keyed
            // dictionary for the named tags plus a dedicated holder for the untagged
            // group.
            var tagGroupsByTagId = new Dictionary<int, ComplianceReportTagGroupModel>();
            ComplianceReportTagGroupModel untaggedGroup = null;
            // templateGroups is keyed on a ValueTuple, which is a struct and is
            // never a null reference when boxed — a null TagId inside it is safe.
            var templateGroups = new Dictionary<(int? TagId, int CheckListId), ComplianceReportTemplateGroupModel>();

            foreach (var row in answered)
            {
                var checkListId = row.SdkCase.CheckListId.Value;
                var projection = projections[checkListId];
                var sdkCaseId = row.Candidate.MicrotingSdkCaseId;

                var rowSiteIds = row.Arp != null
                    ? siteIdsByArpId.GetValueOrDefault(row.Arp.Id, new List<int>())
                    : new List<int>();

                var images = projection.ImagesByCaseId.GetValueOrDefault(sdkCaseId, []);

                // ONE model per compliance row, shared by reference when the row
                // carries several tags — it is never mutated after construction.
                var caseModel = new ComplianceReportCaseModel
                {
                    ComplianceId = row.Candidate.ComplianceId,
                    SdkCaseId = sdkCaseId,
                    PropertyId = row.Candidate.PropertyId,
                    PropertyName = row.PropertyName ?? string.Empty,
                    Title = row.Title,
                    TaskDate = row.EffectiveTaskDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    Completed = row.Completed,
                    // Case METADATA — the prototype's "Udført dato". Never an answer
                    // field (#1160 finding 7).
                    DoneAt = row.DoneAt,
                    WorkerNames = rowSiteIds
                        .Select(id => siteNamesById.GetValueOrDefault(id, string.Empty))
                        .Where(n => !string.IsNullOrEmpty(n))
                        .ToList(),
                    Cells = projection.CellsByCaseId.GetValueOrDefault(sdkCaseId, new Dictionary<string, string>()),
                    ImagesCount = images.Count,
                    Images = images
                };

                // Only a row carrying NO live AreaRulePlanningTag at all lands in
                // the single untagged group, whose label ("Uden tag") is #1167's,
                // not this API's.
                //
                // A tag id is deliberately NOT dropped when its NAME cannot be
                // resolved. Tag ids live in the BC database and names in the
                // items-planning one, with no foreign key between them, so an
                // AreaRulePlanningTag whose ItemPlanningTagId has no PlanningTags
                // row is possible — and filtering those out would empty rowTagIds,
                // trip the null sentinel below, and render a "Uden tag" section
                // inside a report the user had filtered TO a named tag, which is
                // precisely the failure this grouping exists to avoid. The row
                // therefore lands in the NAMED group for the tag it actually
                // carries; that group's TagName is simply null (#1167 renders it
                // without a name — the residual, cosmetic gap).
                //
                // When the request carries a TAG FILTER, only the SELECTED tags form
                // groups. Otherwise a row tagged {A, B} filtered to {A} would render
                // a "B" section too, and the report would look as if the filter had
                // leaked. No row can be lost this way: BuildCandidateSet's EXISTS
                // push-down guarantees every matched row's planning carries at least
                // one of the requested tags on SOME live ARP, and the tag lookup
                // above spans exactly those same ARPs — so a filtered row always
                // finds its tag and can never fall through to the untagged group.
                // #1166 does not settle this either way — it is one predicate to
                // remove if #1167 wants the row's full tag membership instead.
                var rowTagIds = tagIdsByPlanningId
                    .GetValueOrDefault(row.Candidate.PlanningId, [])
                    .Where(id => requestModel.TagIds is not { Count: > 0 } || requestModel.TagIds.Contains(id))
                    .Select(id => (int?)id)
                    .ToList();

                if (rowTagIds.Count == 0) rowTagIds.Add(null);

                foreach (var tagId in rowTagIds)
                {
                    ComplianceReportTagGroupModel tagGroup;
                    if (tagId.HasValue)
                    {
                        if (!tagGroupsByTagId.TryGetValue(tagId.Value, out tagGroup))
                        {
                            tagGroup = new ComplianceReportTagGroupModel
                            {
                                TagId = tagId,
                                TagName = planningTagNames.GetValueOrDefault(tagId.Value)
                            };
                            tagGroupsByTagId[tagId.Value] = tagGroup;
                        }
                    }
                    else
                    {
                        tagGroup = untaggedGroup ??= new ComplianceReportTagGroupModel
                        {
                            TagId = null,
                            TagName = null
                        };
                    }

                    if (!templateGroups.TryGetValue((tagId, checkListId), out var templateGroup))
                    {
                        templateGroup = new ComplianceReportTemplateGroupModel
                        {
                            CheckListId = checkListId,
                            CheckListName = projection.Schema.CheckListName,
                            // Single-valued today: merging structurally-identical
                            // cloned templates is filed, not built (#1166 §8). Two
                            // clones therefore render as two adjacent groups.
                            MergedCheckListIds = [checkListId],
                            Columns = projection.Schema.Columns,
                            // Zero columns because DERIVATION FAILED, not because
                            // the template has no answerable fields — #1167 renders
                            // "columns unavailable" rather than an empty table.
                            SchemaUnavailable = projection.Schema.SchemaUnavailable
                        };
                        templateGroups[(tagId, checkListId)] = templateGroup;
                        tagGroup.Templates.Add(templateGroup);
                    }

                    templateGroup.Cases.Add(caseModel);
                }
            }

            // Stable output order. The untagged group sorts LAST in every locale
            // because it is keyed on the null tag id, not on a translated label.
            // The ordering expression is unchanged by the untagged group living in
            // its own variable rather than in the dictionary: it is simply appended
            // to the same sequence before the sort runs.
            var allTagGroups = tagGroupsByTagId.Values.ToList();
            if (untaggedGroup != null) allTagGroups.Add(untaggedGroup);

            var result = allTagGroups
                .OrderBy(g => g.TagId.HasValue ? 0 : 1)
                .ThenBy(g => g.TagName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(g => g.TagId ?? int.MaxValue)
                .ToList();

            foreach (var tagGroup in result)
            {
                tagGroup.Templates = tagGroup.Templates
                    .OrderBy(t => t.CheckListName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(t => t.CheckListId)
                    .ToList();
            }

            return new OperationDataResult<List<ComplianceReportTagGroupModel>>(true, result);
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            logger.LogError(e, "BackendConfigurationComplianceReportService.EformColumns: {Message}", e.Message);
            return new OperationDataResult<List<ComplianceReportTagGroupModel>>(false,
                $"{localizationService.GetString("ErrorWhileGettingCalendarTasks")}: {e.Message}");
        }
    }

    // ------------------------------------------------------------------
    // Shared-builder input / output shapes
    // ------------------------------------------------------------------

    /// <summary>"all": done AND open. The only status Overview ever asks for.</summary>
    private const string StatusAll = "all";

    /// <summary>
    /// Everything <see cref="BuildCandidateSet"/> filters on. A type rather than a
    /// parameter list so that Index and Overview cannot drift apart by one caller
    /// quietly gaining an argument.
    /// </summary>
    private sealed class CandidateFilter
    {
        public int? PropertyId { get; init; }
        public List<int> BoardIds { get; init; }
        public List<int> TagIds { get; init; }
        public List<int> SiteIds { get; init; }
        /// <summary>Already normalised to a date by the caller.</summary>
        public DateTime DateFrom { get; init; }
        /// <summary>Already normalised to an end-of-day boundary by the caller.</summary>
        public DateTime DateTo { get; init; }
        /// <summary>"open" | "done" | "all"; anything else matches nothing, as before.</summary>
        public string Status { get; init; }

        /// <summary>
        /// Whether to fill the occurrence display fields (IsAllDay, StartHour,
        /// Duration, DoneAt). Index passes <c>true</c> — its rows carry them.
        /// Overview passes <c>false</c>: it aggregates over PropertyId, task date
        /// and completedness only, and must not pay for enrichment it discards.
        /// This flag can change NOTHING that the filtering above reads.
        /// </summary>
        public bool ComputeDisplayFields { get; init; } = true;
    }

    /// <summary>What phases A-C produce.</summary>
    private sealed class CandidateSet
    {
        public List<MatchedRow> MatchedRows { get; init; }
        /// <summary>Board id → name, one entry per board (not per row). Index's phases D/E read it.</summary>
        public Dictionary<int, string> BoardNamesById { get; init; }
    }

    /// <summary>
    /// The four fields the Oversigt aggregation reads. <c>TaskDate</c> is the
    /// yyyy-MM-dd STRING (as on <see cref="ComplianceReportRowModel.TaskDate"/>)
    /// rather than a DateTime, so the prototype's unparseable-date branch is
    /// expressible and testable.
    /// </summary>
    internal sealed class OverviewCandidate
    {
        public int PropertyId { get; init; }
        public string PropertyName { get; init; }
        public string TaskDate { get; init; }
        public bool Completed { get; init; }
    }

    // ------------------------------------------------------------------
    // Enrichment helpers. Each takes the row set it must cover, so the same
    // code serves "the page" (phase E) and "the match set" (a display-column
    // sort in phase D).
    // ------------------------------------------------------------------

    private async Task<Dictionary<int, AreaRulePlanning>> LoadArpDetails(List<MatchedRow> rows)
    {
        var ids = rows.Where(r => r.Arp != null).Select(r => r.Arp.Id).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, AreaRulePlanning>();

        return await backendConfigurationPnDbContext.AreaRulePlannings
            .Where(x => ids.Contains(x.Id))
            .Include(x => x.AreaRule)
            .ThenInclude(x => x.AreaRuleTranslations)
            .Include(x => x.PlanningSites)
            .ToDictionaryAsync(x => x.Id);
    }

    private async Task<Dictionary<int, string>> LoadPropertyNames(List<MatchedRow> rows)
    {
        var ids = rows.Select(r => r.Candidate.PropertyId).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, string>();

        return await backendConfigurationPnDbContext.Properties
            .Where(p => ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name);
    }

    private static void ApplyTitles(
        List<MatchedRow> rows, Dictionary<int, AreaRulePlanning> arpDetailsById, int userLanguageId)
    {
        foreach (var row in rows)
        {
            // An occurrence-level title override wins over the series title.
            if (!string.IsNullOrEmpty(row.Exception?.Title))
            {
                row.Title = row.Exception.Title;
                continue;
            }

            var detail = row.Arp != null ? arpDetailsById.GetValueOrDefault(row.Arp.Id) : null;
            row.Title = CalendarService.ResolveTaskTitle(
                detail?.AreaRule?.AreaRuleTranslations, userLanguageId, row.Candidate.ItemName);
        }
    }

    private static void ApplyPropertyNames(List<MatchedRow> rows, Dictionary<int, string> propertyNamesById)
    {
        foreach (var row in rows)
        {
            row.PropertyName = propertyNamesById.GetValueOrDefault(row.Candidate.PropertyId, string.Empty);
        }
    }

    private static void ApplyBoardNames(List<MatchedRow> rows, Dictionary<int, string> boardNamesById)
    {
        foreach (var row in rows)
        {
            row.BoardName = boardNamesById.GetValueOrDefault(row.EffectiveBoardId, string.Empty);
        }
    }

    // ------------------------------------------------------------------
    // Sorting
    // ------------------------------------------------------------------

    private static class SortKeys
    {
        public const string TaskDate = "taskdate";
        public const string Title = "title";
        public const string PropertyName = "propertyname";
        public const string BoardName = "boardname";
        public const string Completed = "completed";
        public const string DoneAt = "doneat";
    }

    /// <summary>
    /// Maps a caller-supplied sort key onto the allowed set, case-insensitively.
    /// Null, empty and unknown keys all fall back to <c>taskDate</c> — an unknown
    /// key must never throw.
    /// </summary>
    private static string NormaliseSortKey(string sort)
    {
        var key = (sort ?? string.Empty).Trim().ToLowerInvariant();
        return key switch
        {
            SortKeys.Title => SortKeys.Title,
            SortKeys.PropertyName => SortKeys.PropertyName,
            SortKeys.BoardName => SortKeys.BoardName,
            SortKeys.Completed => SortKeys.Completed,
            SortKeys.DoneAt => SortKeys.DoneAt,
            _ => SortKeys.TaskDate
        };
    }

    private static List<MatchedRow> Sort(List<MatchedRow> rows, string sortKey, bool descending)
    {
        // ComplianceId is the final tiebreak everywhere, so paging is stable: two
        // rows that compare equal on the requested key must not swap between page
        // requests.
        IOrderedEnumerable<MatchedRow> ordered = sortKey switch
        {
            SortKeys.Title => descending
                ? rows.OrderByDescending(r => r.Title ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                : rows.OrderBy(r => r.Title ?? string.Empty, StringComparer.OrdinalIgnoreCase),
            SortKeys.PropertyName => descending
                ? rows.OrderByDescending(r => r.PropertyName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                : rows.OrderBy(r => r.PropertyName ?? string.Empty, StringComparer.OrdinalIgnoreCase),
            SortKeys.BoardName => descending
                ? rows.OrderByDescending(r => r.BoardName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                : rows.OrderBy(r => r.BoardName ?? string.Empty, StringComparer.OrdinalIgnoreCase),
            SortKeys.Completed => descending
                ? rows.OrderByDescending(r => r.Completed)
                : rows.OrderBy(r => r.Completed),
            // Nulls last in BOTH directions: an unfinished task has no completion
            // date, and it does not belong at the top of "sorted by completion".
            SortKeys.DoneAt => descending
                ? rows.OrderBy(r => r.DoneAt.HasValue ? 0 : 1).ThenByDescending(r => r.DoneAt)
                : rows.OrderBy(r => r.DoneAt.HasValue ? 0 : 1).ThenBy(r => r.DoneAt),
            // taskDate sorts on the EFFECTIVE DateTime (not the yyyy-MM-dd string
            // the old code sorted on — same order for that format, but no longer
            // dependent on the format), with StartHour ascending as the tiebreak
            // in both directions.
            _ => descending
                ? rows.OrderByDescending(r => r.EffectiveTaskDate).ThenBy(r => r.StartHour)
                : rows.OrderBy(r => r.EffectiveTaskDate).ThenBy(r => r.StartHour)
        };

        return ordered.ThenBy(r => r.Candidate.ComplianceId).ToList();
    }

    // ------------------------------------------------------------------
    // Internal row shapes
    // ------------------------------------------------------------------

    /// <summary>The phase-A projection: the only Compliance columns anything reads.</summary>
    // internal, not private: EF Core materialises this type from a projection,
    // and a compiled expression tree cannot construct a private nested type.
    internal sealed class CandidateRow
    {
        public int ComplianceId { get; set; }
        public string ItemName { get; set; }
        public int PlanningId { get; set; }
        public int PropertyId { get; set; }
        public DateTime Deadline { get; set; }
        public int MicrotingSdkCaseId { get; set; }
        public string WorkflowState { get; set; }
    }

    /// <summary>The phase-B projection: the only SDK Case columns anything reads.</summary>
    internal sealed class SdkCaseInfo
    {
        public int Id { get; set; }
        public int? Status { get; set; }
        public DateTime? DoneAt { get; set; }
        public DateTime? DoneAtUserModifiable { get; set; }
        public int? CheckListId { get; set; }
    }

    /// <summary>A row that survived phase C, carrying what phases D and E need.</summary>
    private sealed class MatchedRow
    {
        public CandidateRow Candidate { get; init; }
        public AreaRulePlanning Arp { get; init; }
        public CalendarOccurrenceException Exception { get; init; }
        public DateTime EffectiveTaskDate { get; init; }
        public int EffectiveBoardId { get; init; }
        public bool Completed { get; init; }
        public SdkCaseInfo SdkCase { get; init; }
        public bool IsAllDay { get; init; }
        public double StartHour { get; init; }
        public double Duration { get; init; }
        public DateTime? DoneAt { get; init; }

        // Display columns, filled by the ApplyX helpers over either the page or
        // (for a display-column sort) the whole match set.
        public string Title { get; set; }
        public string PropertyName { get; set; }
        public string BoardName { get; set; }
    }
}
