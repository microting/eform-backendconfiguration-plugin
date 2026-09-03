using System;
using System.Collections.Generic;
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

            if (requestModel.PropertyId.HasValue)
            {
                complianceQuery = complianceQuery.Where(x => x.PropertyId == requestModel.PropertyId.Value);
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
            if (requestModel.TagIds is { Count: > 0 } tagIds)
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

            if (requestModel.SiteIds is { Count: > 0 } siteIds)
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

            var sdkCore = await coreHelper.GetCore().ConfigureAwait(false);
            // NOTE: this context stays alive for the whole method — phase E reads
            // Sites from it. Disposing it after phase B would blow up only on the
            // code path where a page actually has assigned workers.
            await using var sdkDbContext = sdkCore.DbContextHelper.GetDbContext();

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

            var wantOpen = requestModel.Status is "open" or "all";
            var wantDone = requestModel.Status is "done" or "all";

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
                if (requestModel.BoardIds is { Count: > 0 } boardIds
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

                var isAllDay = CalendarService.ComputeIsAllDay(arp, calConfig);

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
                    StartHour = isAllDay ? 0 : exception?.StartHour ?? calConfig?.StartHour ?? 9.0,
                    Duration = isAllDay ? 0 : exception?.Duration ?? calConfig?.Duration ?? 1.0,
                    DoneAt = done ? sdkCase?.DoneAtUserModifiable ?? sdkCase?.DoneAt : null
                });
            }

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
