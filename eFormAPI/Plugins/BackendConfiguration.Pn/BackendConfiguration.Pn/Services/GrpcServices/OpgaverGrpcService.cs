using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Grpc.Opgaver;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.BackendConfigurationPropertiesService;
using BackendConfiguration.Pn.Services.UserPropertyAccess;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;

namespace BackendConfiguration.Pn.Services.GrpcServices;

/// <summary>
/// gRPC adapter for the mobile "Opgaver" feature.
/// Read-only RPCs (ListEjendomme / ListTavler / ListOpgaver) reuse the existing
/// Properties + Calendar service paths and reshape the result into the
/// microting.opgaver wire contract. CompleteOpgave performs the SDK-case
/// completion inline (mirroring CompliancesGrpcService.UpdateComplianceCase,
/// lines 159-174) for two reasons:
/// (1) the Opgaver flow has no form data, so <c>core.CaseUpdate</c> with empty
///     field/checklist lists would be a no-op anyway; and
/// (2) the JSON-side parity, <c>BackendConfigurationCalendarService.ToggleComplete</c>
///     (line 1272), is a TODO stub returning <c>OperationResult(true)</c> — there
///     is no real implementation to delegate to.
/// SetComment stores worker-supplied comment text on the SDK case row's
/// existing free-form <c>Custom</c> column, JSON-encoded under an
/// <c>opgaver_comment</c> envelope (see <see cref="SetComment"/> docs for the
/// rationale and collision analysis). No new EF entity / migration is
/// introduced.
/// Remaining write RPCs (UploadPhoto, RemovePhoto, StreamOpgaveChanges) are
/// intentionally not overridden — the generated base returns UNIMPLEMENTED,
/// which is the correct v1 behaviour. Follow-up PRs in the stack will fill
/// them in.
///
/// Known divergences from the canonical
/// <c>BackendConfigurationCompliancesService.Update</c> JSON path that
/// CompleteOpgave does NOT perform (parity with
/// <c>CompliancesGrpcService.UpdateComplianceCase</c>, which has the same
/// gaps — this PR introduces no new divergence):
/// <list type="bullet">
///   <item><description><c>PlanningCaseSite</c> row update (Status=100,
///     MicrotingSdkCaseId, MicrotingSdkCaseDoneAt, DoneByUserId,
///     DoneByUserName) — see
///     <c>BackendConfigurationCompliancesService.cs:307-318</c>.</description></item>
///   <item><description><c>PlanningCase</c> row update (Status=100,
///     WorkflowState=Processed) — lines 320-335.</description></item>
///   <item><description><c>Property.ComplianceStatus</c> /
///     <c>ComplianceStatusThirty</c> recomputation — lines 344-371. Without
///     this, the property compliance "dot" UI elsewhere in the system will be
///     stale.</description></item>
///   <item><description><c>CaseUpdateDelegate</c> invocation — lines 262-270 of
///     <c>BackendConfigurationCompliancesService.Update</c>. Downstream
///     subscribers won't be notified.</description></item>
///   <item><description><c>core.CaseDelete</c> of the underlying microting
///     case — lines 373-389. The device-side case won't be deleted.</description></item>
/// </list>
/// Known limitation; closing the gap likely requires factoring a shared
/// completion helper called by both <c>Update</c> and the gRPC paths — out of
/// scope for this PR.
/// </summary>
public class OpgaverGrpcService(
    IBackendConfigurationCalendarService calendarService,
    IBackendConfigurationPropertiesService propertiesService,
    IBackendConfigurationUserPropertyAccess userPropertyAccess,
    IGrpcSiteResolver siteResolver,
    IEFormCoreService coreHelper,
    BackendConfigurationPnDbContext dbContext)
    : Opgaver.OpgaverBase
{
    public override async Task<ListEjendommeResponse> ListEjendomme(
        ListEjendommeRequest request,
        ServerCallContext context)
    {
        var sdkSiteId = await siteResolver.GetSdkSiteIdAsync();
        var accessibleIds = (await userPropertyAccess
            .GetAccessiblePropertyIdsAsync(sdkSiteId)).ToHashSet();

        // fullNames: false matches the historical mobile call (short name only).
        var result = await propertiesService.GetCommonDictionary(false);

        var response = new ListEjendommeResponse();

        if (!result.Success || result.Model == null)
        {
            return response;
        }

        foreach (var item in result.Model)
        {
            if (item.Id is null || !accessibleIds.Contains(item.Id.Value))
            {
                continue;
            }

            response.Ejendomme.Add(new Ejendom
            {
                Id = item.Id.Value.ToString(CultureInfo.InvariantCulture),
                Name = item.Name ?? string.Empty
            });
        }

        return response;
    }

    public override async Task<ListTavlerResponse> ListTavler(
        ListTavlerRequest request,
        ServerCallContext context)
    {
        var propertyId = ParsePropertyId(request.EjendomId);

        var sdkSiteId = await siteResolver.GetSdkSiteIdAsync();
        if (!await userPropertyAccess.HasAccessAsync(sdkSiteId, propertyId))
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied,
                "Caller has no PropertyWorker access to the requested property."));
        }

        var result = await calendarService.GetBoards(propertyId);

        var response = new ListTavlerResponse();

        if (!result.Success || result.Model == null)
        {
            return response;
        }

        foreach (var board in result.Model)
        {
            response.Tavler.Add(new Tavle
            {
                Id = board.Id.ToString(CultureInfo.InvariantCulture),
                EjendomId = board.PropertyId.ToString(CultureInfo.InvariantCulture),
                Name = board.Name ?? string.Empty,
                ColorHex = board.Color ?? string.Empty
            });
        }

        return response;
    }

    public override async Task<ListOpgaverResponse> ListOpgaver(
        ListOpgaverRequest request,
        ServerCallContext context)
    {
        var propertyId = ParsePropertyId(request.EjendomId);

        var sdkSiteId = await siteResolver.GetSdkSiteIdAsync();
        if (!await userPropertyAccess.HasAccessAsync(sdkSiteId, propertyId))
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied,
                "Caller has no PropertyWorker access to the requested property."));
        }

        var model = new CalendarTaskRequestModel
        {
            PropertyId = propertyId,
            WeekStart = request.FromDateKey ?? string.Empty,
            WeekEnd = request.ToDateKey ?? string.Empty,
            BoardIds = TryParseBoardIds(request.TavleId),
            TagNames = [],
            SiteIds = []
        };

        var result = await calendarService.GetTasksForWeek(model);

        var response = new ListOpgaverResponse();

        if (!result.Success || result.Model == null)
        {
            return response;
        }

        // Batch-fetch Case.Custom for every compliance-derived task so the
        // worker-supplied comment (written by SetComment into Case.Custom as
        // a JSON envelope) round-trips on subsequent reads. Recurrence-only
        // tasks have no SdkCaseId and therefore no Custom slot to read.
        var commentByTaskId = await LoadCommentsByTaskIdAsync(result.Model)
            .ConfigureAwait(false);

        foreach (var task in result.Model)
        {
            var comment = commentByTaskId.TryGetValue(task.Id, out var parsed)
                ? parsed
                : string.Empty;

            response.Opgaver.Add(new Opgave
            {
                Id = task.Id.ToString(CultureInfo.InvariantCulture),
                EjendomId = task.PropertyId.ToString(CultureInfo.InvariantCulture),
                TavleId = task.BoardId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                PlanDayKey = task.TaskDate ?? string.Empty,
                PlannedAt = string.Empty,
                TaskText = task.Title ?? string.Empty,
                CalendarColor = task.Color ?? string.Empty,
                Completed = task.Completed,
                CompletedBy = string.Empty,
                DescriptionHtml = task.DescriptionHtml ?? string.Empty,
                Comment = comment
                // updated_at: Timestamp default (zero) — no source field in CalendarTaskResponseModel.
                // attachments: empty — populated in a later PR via the Documents/attachments flow.
            });
        }

        return response;
    }

    /// <summary>
    /// Reads worker-authored comments back for the given calendar tasks.
    /// Compliance-derived tasks carry the SDK Case id directly on the
    /// response model (see <c>BackendConfigurationCalendarService.cs</c>
    /// line 484), so we can batch-fetch the Cases in one query and parse
    /// their <c>Custom</c> column. Recurrence-only tasks (no Case yet) are
    /// skipped — there is no comment-storage slot for them in this PR.
    /// Non-envelope or malformed Custom values degrade silently to "".
    /// </summary>
    private async Task<Dictionary<int, string>> LoadCommentsByTaskIdAsync(
        IReadOnlyCollection<CalendarTaskResponseModel> tasks)
    {
        // Distinct (task.Id → SdkCaseId). Tasks with no SdkCaseId are
        // recurrence-only and have no Case row to read from.
        var taskIdToCaseId = tasks
            .Where(t => t.SdkCaseId is > 0)
            .GroupBy(t => t.Id)
            .ToDictionary(g => g.Key, g => g.First().SdkCaseId!.Value);

        if (taskIdToCaseId.Count == 0)
        {
            return new Dictionary<int, string>();
        }

        var caseIds = taskIdToCaseId.Values.Distinct().ToList();

        var core = await coreHelper.GetCore().ConfigureAwait(false);
        var sdkDbContext = core.DbContextHelper.GetDbContext();

        // Single query for every relevant Case.Custom value.
        var customByCaseId = await sdkDbContext.Cases
            .Where(c => caseIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Custom })
            .ToDictionaryAsync(c => c.Id, c => c.Custom)
            .ConfigureAwait(false);

        var result = new Dictionary<int, string>(taskIdToCaseId.Count);
        foreach (var (taskId, caseId) in taskIdToCaseId)
        {
            if (!customByCaseId.TryGetValue(caseId, out var customJson))
            {
                continue;
            }

            var parsed = TryParseComment(customJson);
            if (!string.IsNullOrEmpty(parsed))
            {
                result[taskId] = parsed;
            }
        }

        return result;
    }

    /// <summary>
    /// Inverse of <see cref="SerializeOpgaverComment"/>. Returns the worker
    /// comment text from a <c>Case.Custom</c> JSON envelope, or
    /// <see cref="string.Empty"/> if the value is missing, empty, or not in
    /// the expected shape (legacy free-form strings, garbage).
    /// </summary>
    private static string TryParseComment(string customJson)
    {
        if (string.IsNullOrWhiteSpace(customJson))
        {
            return string.Empty;
        }

        try
        {
            var envelope = JsonSerializer.Deserialize<OpgaverCustomEnvelope>(customJson);
            return envelope?.OpgaverComment?.Text ?? string.Empty;
        }
        catch (JsonException)
        {
            // Non-envelope / pre-existing free-form string — treat as no comment.
            return string.Empty;
        }
    }

    public override async Task<CompleteOpgaveResponse> CompleteOpgave(
        CompleteOpgaveRequest request,
        ServerCallContext context)
    {
        // Only "complete" semantics are supported in v1. The JSON-side
        // ToggleComplete is itself a TODO; an explicit un-complete flow will
        // require new entity work (re-creating the compliance row), which is
        // out of scope for this PR.
        if (!request.Completed)
        {
            throw new RpcException(new Status(StatusCode.Unimplemented,
                "Un-completing an opgave is not supported yet."));
        }

        var opgaveId = ParseOpgaveId(request.OpgaveId);

        // Look up the AreaRulePlanning to learn its property + ItemPlanningId.
        // ItemPlanningId is the join key into Compliances.PlanningId.
        var arp = await dbContext.AreaRulePlannings
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .FirstOrDefaultAsync(x => x.Id == opgaveId)
            .ConfigureAwait(false);

        if (arp == null)
        {
            throw new RpcException(new Status(StatusCode.NotFound,
                $"Opgave {opgaveId} not found."));
        }

        var sdkSiteId = await siteResolver.GetSdkSiteIdAsync().ConfigureAwait(false);
        if (!await userPropertyAccess.HasAccessAsync(sdkSiteId, arp.PropertyId)
                .ConfigureAwait(false))
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied,
                "Caller has no PropertyWorker access to the opgave's property."));
        }

        // Resolve the Compliance row that links this ARP to a real SDK Case.
        // GetTasksForWeek treats compliance-derived rows as "completable" and
        // anything else as a future occurrence with no Case to update — so the
        // absence of a compliance row is a hard error here.
        var compliance = await dbContext.Compliances
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .Where(x => x.PlanningId == arp.ItemPlanningId)
            .OrderBy(x => x.Deadline)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);

        if (compliance == null)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition,
                $"Opgave {opgaveId} has no pending compliance — there is no SDK case to complete."));
        }

        // Convert client_ts_unix (seconds) → UTC DateTime. Fall back to
        // server-side now if the client didn't send a usable timestamp.
        DateTime doneAtUtc = request.ClientTsUnix > 0
            ? DateTimeOffset.FromUnixTimeSeconds(request.ClientTsUnix).UtcDateTime
            : DateTime.UtcNow;
        // BackendConfigurationCompliancesService.Update truncates DoneAt to
        // midnight UTC of that calendar date — keep parity.
        var dayDoneAt = new DateTime(doneAtUtc.Year, doneAtUtc.Month, doneAtUtc.Day,
            0, 0, 0, DateTimeKind.Utc);

        var caseId = compliance.MicrotingSdkCaseId;

        // Soft-delete the compliance row so it disappears from outstanding
        // lists, then mark the SDK Case row as completed. This mirrors the
        // shape of CompliancesGrpcService.UpdateComplianceCase but skips the
        // core.CaseUpdate / CaseUpdateFieldValues calls — the Opgaver flow has
        // no form fields, so those calls would just iterate empty lists.
        var core = await coreHelper.GetCore().ConfigureAwait(false);
        var sdkDbContext = core.DbContextHelper.GetDbContext();

        await compliance.Delete(dbContext).ConfigureAwait(false);

        var foundCase = await sdkDbContext.Cases
            .FirstOrDefaultAsync(x => x.Id == caseId)
            .ConfigureAwait(false);

        if (foundCase != null)
        {
            foundCase.DoneAtUserModifiable = dayDoneAt;
            foundCase.DoneAt = dayDoneAt;
            foundCase.SiteId = sdkSiteId;
            foundCase.Status = 100;
            foundCase.WorkflowState = Constants.WorkflowStates.Created;
            await foundCase.Update(sdkDbContext).ConfigureAwait(false);
        }

        // Re-read the calendar tasks for the day in question so we can return
        // the freshly-completed Opgave in its new shape (now compliance-less,
        // so it will fall out of the recurrence/compliance branches — we look
        // it up by ARP id and synthesize a minimal mapping if it's gone).
        var dayKey = dayDoneAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var refreshed = await calendarService.GetTasksForWeek(new CalendarTaskRequestModel
        {
            PropertyId = arp.PropertyId,
            WeekStart = dayKey,
            WeekEnd = dayKey,
            BoardIds = [],
            TagNames = [],
            SiteIds = []
        }).ConfigureAwait(false);

        var refreshedTask = refreshed.Success && refreshed.Model != null
            ? refreshed.Model.FirstOrDefault(t => t.Id == opgaveId)
            : null;

        var opgave = refreshedTask != null
            ? new Opgave
            {
                Id = refreshedTask.Id.ToString(CultureInfo.InvariantCulture),
                EjendomId = refreshedTask.PropertyId.ToString(CultureInfo.InvariantCulture),
                TavleId = refreshedTask.BoardId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                PlanDayKey = refreshedTask.TaskDate ?? string.Empty,
                PlannedAt = string.Empty,
                TaskText = refreshedTask.Title ?? string.Empty,
                CalendarColor = refreshedTask.Color ?? string.Empty,
                Completed = true,
                CompletedBy = request.CompletedBy ?? string.Empty,
                DescriptionHtml = refreshedTask.DescriptionHtml ?? string.Empty,
                Comment = string.Empty
            }
            : new Opgave
            {
                // Compliance row is gone and no recurrence covered today —
                // synthesize a minimal "completed" Opgave so the client can
                // reconcile local state against the new server truth.
                Id = opgaveId.ToString(CultureInfo.InvariantCulture),
                EjendomId = arp.PropertyId.ToString(CultureInfo.InvariantCulture),
                TavleId = string.Empty,
                PlanDayKey = dayKey,
                PlannedAt = string.Empty,
                TaskText = string.Empty,
                CalendarColor = string.Empty,
                Completed = true,
                CompletedBy = request.CompletedBy ?? string.Empty,
                DescriptionHtml = string.Empty,
                Comment = string.Empty
            };

        return new CompleteOpgaveResponse { Opgave = opgave };
    }

    /// <summary>
    /// Stores the worker's comment for an opgave on the underlying SDK Case row.
    ///
    /// Storage path: SDK <c>Case.Custom</c> (free-form string column on the
    /// SDK eFormCore Cases table), JSON-encoded as
    /// <c>{"opgaver_comment":{"text":...,"ts_unix":...}}</c>.
    ///
    /// Rationale (Path A from the design exploration; see PR description):
    /// <list type="bullet">
    ///   <item><description>No new EF entities or migrations — hard
    ///     constraint from the user.</description></item>
    ///   <item><description>The plugin always passes <c>""</c> as the
    ///     <c>custom</c> arg to <c>core.CaseCreate</c> (verified at all
    ///     call sites in <c>BackendConfigurationAreaRulePlanningsServiceHelper</c>,
    ///     <c>BackendConfigurationTaskManagementHelper</c>,
    ///     <c>WorkOrderHelper</c>, <c>DocumentHelper</c>,
    ///     <c>PairItemWithSiteHelper</c>) — so for ARP-derived cases the
    ///     <c>Custom</c> slot is reliably empty and we are not overwriting
    ///     pre-existing data.</description></item>
    ///   <item><description>The only plugin reader of <c>Case.Custom</c> is
    ///     <c>CompliancesGrpcService.ReadComplianceCase</c>, which echoes the
    ///     value through the wire as a free-form passthrough — adding JSON
    ///     does not break that callsite, but clients that surface the raw
    ///     <c>ComplianceCase.Custom</c> field will see the JSON envelope.
    ///     This is acceptable because <c>Custom</c> is documented as
    ///     unstructured.</description></item>
    ///   <item><description><c>SqlController.CaseFindCustomMatchs</c> does an
    ///     equality match on <c>Custom</c>, but it has no callers anywhere
    ///     in the workspace, so the equality-match collision is theoretical
    ///     only.</description></item>
    ///   <item><description>Path B (writing the comment to a designated
    ///     "Comment" field of the eForm template via
    ///     <c>core.CaseUpdate(caseId, fieldValueList, ...)</c>) was rejected
    ///     because there is no guarantee every ARP-bound template carries a
    ///     comment-typed field — would not work universally.</description></item>
    /// </list>
    ///
    /// Edge cases:
    /// <list type="bullet">
    ///   <item><description>Empty <c>text</c> (after trim): clears the
    ///     comment by writing back <c>""</c> to <c>Case.Custom</c>.
    ///     A future "comment history" feature would need to extend the
    ///     envelope or migrate to a dedicated table.</description></item>
    ///   <item><description><c>text.Length &gt; 10_000</c>: rejected with
    ///     <c>InvalidArgument</c> — the comment is intended for short
    ///     worker remarks, not free-form essay storage.</description></item>
    ///   <item><description><c>client_ts_unix == 0</c>: server-side
    ///     <c>DateTime.UtcNow</c> is recorded instead (same fallback as
    ///     CompleteOpgave).</description></item>
    /// </list>
    ///
    /// Compliance lookup deliberately includes <c>WorkflowState=Removed</c>
    /// rows so that workers can still attach a comment after CompleteOpgave
    /// has soft-deleted the compliance (e.g. "noticed leak, scheduled
    /// repair"). The Case row and its <c>Custom</c> slot survive
    /// completion. CompleteOpgave keeps its own not-removed filter — that
    /// path doesn't make sense to re-run.
    ///
    /// Pure-recurrence opgaver without a backing Case (no compliance row at
    /// all) cannot be persisted today; the response echoes the comment back
    /// in a synthesised minimal Opgave so the client's optimistic write is
    /// preserved, but no SDK write occurs. Materialising the case early or
    /// adopting Path B/C is out of scope here.
    /// </summary>
    public override async Task<SetCommentResponse> SetComment(
        SetCommentRequest request,
        ServerCallContext context)
    {
        const int MaxCommentLength = 10_000;

        var text = request.Text ?? string.Empty;
        if (text.Length > MaxCommentLength)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                $"Comment text exceeds {MaxCommentLength}-character limit."));
        }

        var opgaveId = ParseOpgaveId(request.OpgaveId);

        var arp = await dbContext.AreaRulePlannings
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .FirstOrDefaultAsync(x => x.Id == opgaveId)
            .ConfigureAwait(false);

        if (arp == null)
        {
            throw new RpcException(new Status(StatusCode.NotFound,
                $"Opgave {opgaveId} not found."));
        }

        var sdkSiteId = await siteResolver.GetSdkSiteIdAsync().ConfigureAwait(false);
        if (!await userPropertyAccess.HasAccessAsync(sdkSiteId, arp.PropertyId)
                .ConfigureAwait(false))
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied,
                "Caller has no PropertyWorker access to the opgave's property."));
        }

        // Trim only the right end so leading whitespace in legitimate worker
        // input isn't destroyed; whitespace-only is treated as "clear"
        // (TrimEnd of an all-whitespace string is the empty string). Done
        // up-front so all branches below (write, synthesise) echo the same
        // text on the wire.
        var trimmed = text.TrimEnd();

        // client_ts_unix → UTC; fall back to server-side now.
        var commentAtUtc = request.ClientTsUnix > 0
            ? DateTimeOffset.FromUnixTimeSeconds(request.ClientTsUnix).UtcDateTime
            : DateTime.UtcNow;

        // Find the Compliance row → SDK Case. Comments must be writable
        // regardless of completion status, so we deliberately do NOT filter
        // out WorkflowState=Removed rows: CompleteOpgave soft-deletes the
        // compliance, but the Case row (and its Custom slot) survives, so a
        // worker can still attach a post-completion remark like
        // "noticed leak, scheduled repair". CompleteOpgave keeps the
        // not-removed filter on its own lookup — re-completing an already
        // completed task makes no sense there.
        var compliance = await dbContext.Compliances
            .Where(x => x.PlanningId == arp.ItemPlanningId)
            .OrderBy(x => x.Deadline)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);

        // Truly orphaned task (no compliance row at all → no SDK Case ever
        // existed). Echo the comment back in a synthesised minimal Opgave so
        // the client can keep its local optimistic write, but skip the SDK
        // write because there is nowhere to store it. This mirrors the
        // synthesise-on-miss fallback used in the success branch below and
        // matches CompleteOpgave's behaviour for the same edge case.
        if (compliance == null)
        {
            return new SetCommentResponse
            {
                Opgave = SynthesiseMinimalOpgave(opgaveId, arp.PropertyId, commentAtUtc, trimmed)
            };
        }

        var core = await coreHelper.GetCore().ConfigureAwait(false);
        var sdkDbContext = core.DbContextHelper.GetDbContext();

        var foundCase = await sdkDbContext.Cases
            .FirstOrDefaultAsync(x => x.Id == compliance.MicrotingSdkCaseId)
            .ConfigureAwait(false);

        if (foundCase == null)
        {
            // Compliance points at an SDK Case that no longer exists —
            // genuinely broken state, not a "soft" miss. Fail loudly.
            throw new RpcException(new Status(StatusCode.FailedPrecondition,
                $"Opgave {opgaveId}: compliance {compliance.Id} references missing SDK case {compliance.MicrotingSdkCaseId}."));
        }

        // Write — empty text clears the slot; non-empty wraps in the envelope.
        foundCase.Custom = string.IsNullOrEmpty(trimmed)
            ? string.Empty
            : SerializeOpgaverComment(trimmed, commentAtUtc);
        await foundCase.Update(sdkDbContext).ConfigureAwait(false);

        // Refresh the opgave for the day in question so the response shape
        // matches CompleteOpgave (same synthesise-on-miss fallback).
        var dayKey = (compliance.Deadline != default ? compliance.Deadline : commentAtUtc)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var refreshed = await calendarService.GetTasksForWeek(new CalendarTaskRequestModel
        {
            PropertyId = arp.PropertyId,
            WeekStart = dayKey,
            WeekEnd = dayKey,
            BoardIds = [],
            TagNames = [],
            SiteIds = []
        }).ConfigureAwait(false);

        var refreshedTask = refreshed.Success && refreshed.Model != null
            ? refreshed.Model.FirstOrDefault(t => t.Id == opgaveId)
            : null;

        // Echo the just-written text on the way out so the client doesn't
        // need a follow-up read. GetTasksForWeek does not currently surface
        // Case.Custom, so populating opgave.comment here is the only path.
        var opgave = refreshedTask != null
            ? new Opgave
            {
                Id = refreshedTask.Id.ToString(CultureInfo.InvariantCulture),
                EjendomId = refreshedTask.PropertyId.ToString(CultureInfo.InvariantCulture),
                TavleId = refreshedTask.BoardId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                PlanDayKey = refreshedTask.TaskDate ?? string.Empty,
                PlannedAt = string.Empty,
                TaskText = refreshedTask.Title ?? string.Empty,
                CalendarColor = refreshedTask.Color ?? string.Empty,
                Completed = refreshedTask.Completed,
                CompletedBy = string.Empty,
                DescriptionHtml = refreshedTask.DescriptionHtml ?? string.Empty,
                Comment = trimmed
            }
            : SynthesiseMinimalOpgave(opgaveId, arp.PropertyId, commentAtUtc, trimmed);

        return new SetCommentResponse { Opgave = opgave };
    }

    private static Opgave SynthesiseMinimalOpgave(
        int opgaveId, int propertyId, DateTime commentAtUtc, string trimmed)
    {
        return new Opgave
        {
            Id = opgaveId.ToString(CultureInfo.InvariantCulture),
            EjendomId = propertyId.ToString(CultureInfo.InvariantCulture),
            TavleId = string.Empty,
            PlanDayKey = commentAtUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            PlannedAt = string.Empty,
            TaskText = string.Empty,
            CalendarColor = string.Empty,
            Completed = false,
            CompletedBy = string.Empty,
            DescriptionHtml = string.Empty,
            Comment = trimmed
        };
    }

    private static string SerializeOpgaverComment(string text, DateTime tsUtc)
    {
        var envelope = new OpgaverCustomEnvelope
        {
            OpgaverComment = new OpgaverCommentBody
            {
                Text = text,
                TsUnix = new DateTimeOffset(tsUtc, TimeSpan.Zero).ToUnixTimeSeconds()
            }
        };
        return JsonSerializer.Serialize(envelope);
    }

    private sealed class OpgaverCustomEnvelope
    {
        [JsonPropertyName("opgaver_comment")]
        public OpgaverCommentBody OpgaverComment { get; set; } = new();
    }

    private sealed class OpgaverCommentBody
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;

        [JsonPropertyName("ts_unix")]
        public long TsUnix { get; set; }
    }

    private static int ParseOpgaveId(string raw)
    {
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
        {
            return id;
        }
        throw new RpcException(new Status(StatusCode.InvalidArgument,
            "opgave_id must be a numeric AreaRulePlanning id."));
    }

    private static int ParsePropertyId(string raw)
    {
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
        {
            return id;
        }
        throw new RpcException(new Status(StatusCode.InvalidArgument,
            "ejendom_id must be a numeric property id."));
    }

    private static System.Collections.Generic.List<int> TryParseBoardIds(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
        {
            return [id];
        }

        // Non-numeric tavle_id is treated as "no board filter" rather than a hard failure.
        return [];
    }
}
