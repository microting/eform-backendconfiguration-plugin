#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Grpc.Events;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure;
using Microting.eForm.Infrastructure.Constants;
using Compliance = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.Compliance;
using SdkCase = Microting.eForm.Infrastructure.Data.Entities.Case;

namespace BackendConfiguration.Pn.Services.GrpcServices;

/// <summary>
/// Mobile contract revision 1 ("package 9, contract additions", part A):
/// capability handshake, request ids with natural idempotency, Event
/// version / etag / due information, and <c>GetEvent</c>.
/// <para>
/// Compatibility rule: every branch here is switched on by a field an old
/// app never sends (<c>request_id</c>, <c>expected_storage_id</c>) or lives
/// in a new RPC. Without those fields every existing RPC answers exactly as
/// before; read RPCs only gain the additive Event fields 19-22.
/// </para>
/// <para>
/// Request ids are recorded in the SDK <c>Case.Custom</c> JSON envelope the
/// service already owns (no new table, no migration):
/// <c>completed_request_id</c> (the one completion of the case) and
/// <c>applied_requests</c> (the last <see cref="MaxAppliedRequests"/> other
/// writes, oldest dropped first). Both are omitted from the JSON when unset,
/// so envelopes written without request ids are byte-identical to before.
/// </para>
/// </summary>
public partial class EventsGrpcService
{
    /// <summary>Contract revision answered by <see cref="GetServerInfo"/>.</summary>
    internal const int ContractVersion = 1;

    /// <summary>The optional behaviours this revision adds, verbatim on the wire.</summary>
    internal static readonly IReadOnlyList<string> ContractCapabilities =
    [
        "request_id",
        "remove_photo_expected_storage_id",
        "write_reply_compliance_id",
        "get_event",
        "event_version",
        "event_etag",
        "event_due_local"
    ];

    /// <summary>
    /// How far <see cref="GetEvent"/> widens its calendar window towards an
    /// occurrence's original date; an occurrence moved further is looked up
    /// on its day alone.
    /// </summary>
    private const int MaxGetEventWindowWideningDays = 92;

    /// <summary>Upper bound on a client request id; longer ids are rejected.</summary>
    internal const int MaxRequestIdLength = 128;

    /// <summary>
    /// How many non-completion request ids one case envelope remembers. A
    /// repeat older than this many later writes on the same case is applied
    /// again (the pre-contract behaviour).
    /// </summary>
    internal const int MaxAppliedRequests = 16;

    /// <summary>
    /// How long a request that lost the completion claim to a concurrent
    /// request waits for the winner to finish its case update before
    /// answering <c>ABORTED</c> (retryable). Only request-id callers wait.
    /// Total wait: LostClaimWaitSteps x LostClaimWaitStep = 12 x 250 ms = 3 s.
    /// </summary>
    private static readonly TimeSpan LostClaimWaitStep = TimeSpan.FromMilliseconds(250);
    private const int LostClaimWaitSteps = 12;

    /// <summary>
    /// A compliance soft-deleted this recently with its case still open is
    /// treated as a completion in flight (the claim commits before the case
    /// update) rather than an admin cancellation.
    /// </summary>
    private static readonly TimeSpan InFlightClaimWindow = TimeSpan.FromSeconds(60);

    private const string OpUploadPhoto = "upload_photo";
    private const string OpRemovePhoto = "remove_photo";
    private const string OpSetComment = "set_comment";
    private const string OpSetFieldValue = "set_field_value";

    private const string RemoveResultRemoved = "removed";
    private const string RemoveResultAbsent = "absent";
    private const string RemoveResultOtherPrefix = "other:";

    /// <summary>
    /// Writes a photo object to storage. Production always uses the SDK's S3
    /// upload; the seam exists because the SDK's S3 client is process-static
    /// and cannot be faked per test without leaking into other fixtures.
    /// </summary>
    internal Func<eFormCore.Core, Stream, string, Task> PutFileToS3StorageAsync { get; init; } =
        (core, stream, fileName) => core.PutFileToS3Storage(stream, fileName);

    // ------------------------------------------------------------------
    // New RPCs
    // ------------------------------------------------------------------

    /// <summary>
    /// Capability handshake. Authenticates exactly like the read RPCs
    /// (<c>ListProperties</c>): the site resolver rejects disabled / resigned
    /// accounts with <c>UNAUTHENTICATED</c>; nothing here is per property.
    /// </summary>
    public override async Task<GetServerInfoResponse> GetServerInfo(
        GetServerInfoRequest request,
        ServerCallContext context)
    {
        await siteResolver.GetSdkSiteIdAsync().ConfigureAwait(false);

        var response = new GetServerInfoResponse { ContractVersion = ContractVersion };
        response.Capabilities.AddRange(ContractCapabilities);
        return response;
    }

    /// <summary>
    /// One occurrence, built by the same path as <c>ListEvents</c> /
    /// <c>StreamEventChanges</c>: the calendar read scoped to the caller's
    /// site with <c>ActionableOnly</c>, then envelope, fields, attachments,
    /// due, version and etag (<see cref="BuildEventsFromCalendarRowsAsync"/>).
    /// <list type="bullet">
    /// <item>The read window is the key's day widened to the occurrence's
    /// ORIGINAL date (its compliance deadline, or the original date of a
    /// <c>scope=this</c> move onto the day), so an occurrence moved between
    /// days is found exactly as a list spanning both days shows it.</item>
    /// <item>Rows are selected by the key BEFORE hydration, and each candidate
    /// is hydrated on its own: nothing else on the day is read, and the
    /// occurrence gets its OWN case's comment and fields (a multi-row
    /// hydration groups by event id, see <c>LoadEnvelopeByTaskIdAsync</c>).</item>
    /// <item>A failed calendar read, or a failed SDK read of the matched
    /// occurrence's form (<c>CaseRead</c> throwing), is <c>UNAVAILABLE</c>
    /// (retry), never <c>found=false</c>; lists and the stream keep their
    /// empty answer and their skip-the-task behaviour. A template that is
    /// simply not materialised yet still answers <c>found=false</c>.</item>
    /// </list>
    /// Unknown, removed, other-property and not-assigned-to-caller tasks all
    /// answer <c>found=false</c>. Read-only: unlike ListEvents it does not
    /// run the eager deploy.
    /// </summary>
    public override async Task<GetEventResponse> GetEvent(
        GetEventRequest request,
        ServerCallContext context)
    {
        var key = request.Key
                  ?? throw new RpcException(new Status(StatusCode.InvalidArgument, "key is required."));
        var eventId = ParseEventId(key.EventId);
        if (!DateTime.TryParseExact(key.PlanDayKey, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var day))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                "key.plan_day_key must be yyyy-MM-dd."));
        }

        var notFound = new GetEventResponse { Found = false };

        // Resolve the caller first (as StreamEventChanges does), so a
        // disabled account gets UNAUTHENTICATED for every key, never a
        // found=false that depends on whether the task exists.
        var sdkSiteId = await siteResolver.GetSdkSiteIdAsync().ConfigureAwait(false);

        var arp = await dbContext.AreaRulePlannings
            .AsNoTracking()
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed || x.WorkflowState == null)
            .Where(x => x.Id == eventId)
            .Select(x => new { x.PropertyId, x.ItemPlanningId })
            .FirstOrDefaultAsync(context.CancellationToken)
            .ConfigureAwait(false);
        if (arp == null)
        {
            return notFound;
        }

        if (!await userPropertyAccess.HasAccessAsync(sdkSiteId, arp.PropertyId).ConfigureAwait(false))
        {
            // Same answer as "no such task": a worker must not be able to
            // learn that a task exists on a property they are not assigned to.
            return notFound;
        }

        var (windowStart, windowEnd) = await ResolveGetEventWindowAsync(
                eventId, arp.ItemPlanningId, day, key, context.CancellationToken)
            .ConfigureAwait(false);

        var result = await calendarService.GetTasksForWeek(new CalendarTaskRequestModel
        {
            PropertyId = arp.PropertyId,
            WeekStart = windowStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            WeekEnd = windowEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            BoardIds = [],
            TagNames = [],
            // #931: only events assigned to the caller's own site.
            SiteIds = [sdkSiteId],
            ActionableOnly = true,
            LanguageId = await siteResolver.GetSiteLanguageIdAsync(sdkSiteId).ConfigureAwait(false)
        }).ConfigureAwait(false);

        if (!result.Success || result.Model == null)
        {
            // The calendar service turns read exceptions into a failed
            // result. UNAVAILABLE (as ChemicalsGrpcService uses for a failing
            // dependency): GetEvent is a read, so a retry is always safe,
            // and the client must not conclude that the task is gone.
            logger.LogWarning(
                "EventsGrpcService.GetEvent: calendar read failed for event {EventId} on {PlanDayKey}: {Message}",
                eventId, key.PlanDayKey, result.Message);
            throw new RpcException(new Status(StatusCode.Unavailable,
                "The event could not be read right now; retry."));
        }

        var candidates = result.Model
            .Where(t => t.Id == eventId
                        && t.TaskDate == key.PlanDayKey
                        && (!key.HasComplianceId || (t.ComplianceId ?? 0) == key.ComplianceId))
            .ToList();
        foreach (var candidate in candidates)
        {
            var built = await BuildEventsFromCalendarRowsAsync([candidate], context.CancellationToken,
                    stampVersions: true, failOnReadError: true)
                .ConfigureAwait(false);
            if (built.Count > 0)
            {
                return new GetEventResponse { Event = built[0], Found = true };
            }
        }

        return notFound;
    }

    /// <summary>
    /// GetEvent's field read only: true when the SDK case row exists (so a
    /// null <c>CaseRead</c> is a read failure); a failing existence check is
    /// itself <c>UNAVAILABLE</c>.
    /// </summary>
    private static async Task<bool> CaseRowExistsOrUnavailableAsync(MicrotingDbContext sdkDbContext, int caseId)
    {
        try
        {
            return await sdkDbContext.Cases.AsNoTracking().AnyAsync(c => c.Id == caseId).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new RpcException(new Status(StatusCode.Unavailable,
                "The event's form could not be read right now; retry."));
        }
    }

    /// <summary>
    /// The calendar window <see cref="GetEvent"/> reads: the key's day,
    /// widened to the occurrence's original date. The calendar places a
    /// compliance-backed occurrence by its <c>Deadline</c> and applies a
    /// <c>scope=this</c> move only when that original date is inside the
    /// window; a window holding just the new day renders the moved row
    /// without its case, and the completeness filter drops it. Sources: the
    /// key's compliance (any state, on this planning) and the move
    /// exceptions of this event onto the key's day.
    /// </summary>
    private async Task<(DateTime Start, DateTime End)> ResolveGetEventWindowAsync(
        int eventId, int itemPlanningId, DateTime day, EventKey key, CancellationToken ct)
    {
        var dates = new List<DateTime> { day.Date };

        if (key.HasComplianceId && key.ComplianceId > 0)
        {
            var complianceId = (int)key.ComplianceId;
            var deadline = await dbContext.Compliances
                .AsNoTracking()
                .Where(x => x.Id == complianceId && x.PlanningId == itemPlanningId)
                .Select(x => (DateTime?)x.Deadline)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
            if (deadline is { } d && d != default)
            {
                dates.Add(d.Date);
            }
        }

        var nextDay = day.Date.AddDays(1);
        var movedFrom = await dbContext.CalendarOccurrenceExceptions
            .AsNoTracking()
            .Where(x => x.AreaRulePlanningId == eventId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .Where(x => !x.IsDeleted)
            .Where(x => x.NewDate.HasValue && x.NewDate.Value >= day.Date && x.NewDate.Value < nextDay)
            .Select(x => x.OriginalDate)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        dates.AddRange(movedFrom.Select(x => x.Date));

        // Bound the read: a key can name any compliance of the planning.
        var reachable = dates.Where(d => Math.Abs((d - day.Date).TotalDays) <= MaxGetEventWindowWideningDays).ToList();
        return (reachable.Min(), reachable.Max());
    }

    // ------------------------------------------------------------------
    // Request ids
    // ------------------------------------------------------------------

    /// <summary>
    /// Returns the trimmed request id ("" = legacy request) or throws
    /// <c>INVALID_ARGUMENT</c> when it is longer than
    /// <see cref="MaxRequestIdLength"/>. Only callers that send the new field
    /// can reach the throw.
    /// </summary>
    private static string NormalizeRequestId(string? raw)
    {
        var trimmed = raw?.Trim() ?? string.Empty;
        if (trimmed.Length > MaxRequestIdLength)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                $"request_id must be at most {MaxRequestIdLength} characters."));
        }

        return trimmed;
    }

    /// <summary>
    /// True when <paramref name="custom"/> can hold an envelope without
    /// destroying anything: empty, or already a JSON object the envelope
    /// parser accepts. Non-JSON legacy content is left alone, so request-id
    /// bookkeeping silently degrades to the pre-contract behaviour there.
    /// </summary>
    private static bool IsEnvelopeWritable(string? custom, out EventCustomEnvelope envelope)
    {
        if (string.IsNullOrWhiteSpace(custom))
        {
            envelope = new EventCustomEnvelope();
            return true;
        }

        var parsed = TryParseEnvelope(custom);
        envelope = parsed ?? new EventCustomEnvelope();
        return parsed != null && HasOnlyKnownEnvelopeKeys(custom);
    }

    /// <summary>The top-level keys the envelope owns; anything else belongs to someone else.</summary>
    private static readonly HashSet<string> KnownEnvelopeKeys =
    [
        "event_comment", "event_photos", "completed_request_id", "completing_request_id",
        "completing_ts_unix", "applied_requests"
    ];

    /// <summary>
    /// False when the JSON object carries a key the envelope does not model
    /// (e.g. <c>{"external_reference":"123"}</c>): a round-trip through
    /// <see cref="EventCustomEnvelope"/> would drop it, so request-id
    /// bookkeeping must not write such a case (it degrades to legacy
    /// behaviour). Content writes keep their pre-existing semantics.
    /// </summary>
    private static bool HasOnlyKnownEnvelopeKeys(string custom)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(custom);
            return document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                   && document.RootElement.EnumerateObject().All(p => KnownEnvelopeKeys.Contains(p.Name));
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    private static AppliedRequestRecord? FindAppliedRequest(
        EventCustomEnvelope? envelope, string requestId, string operation) =>
        envelope?.AppliedRequests?.LastOrDefault(r => r.Id == requestId && r.Operation == operation);

    /// <summary>Appends a record, dropping the oldest beyond <see cref="MaxAppliedRequests"/>.</summary>
    private static void AddAppliedRequest(
        EventCustomEnvelope envelope, string requestId, string operation, string result)
    {
        envelope.AppliedRequests ??= [];
        envelope.AppliedRequests.RemoveAll(r => r.Id == requestId && r.Operation == operation);
        envelope.AppliedRequests.Add(new AppliedRequestRecord
        {
            Id = requestId,
            Operation = operation,
            Result = result,
            TsUnix = ToUnixSeconds(DateTime.UtcNow)
        });
        var excess = envelope.AppliedRequests.Count - MaxAppliedRequests;
        if (excess > 0)
        {
            envelope.AppliedRequests.RemoveRange(0, excess);
        }
    }

    /// <summary>
    /// Per-occurrence mutex for request-id writes: a short plugin-DB
    /// transaction holding <c>SELECT ... FROM Compliances WHERE Id = x FOR
    /// UPDATE</c> (the same row the completion claim locks) around "check the
    /// envelope, apply, record". Two identical requests in flight therefore
    /// serialise, and the second sees the first's record. The lock is on the
    /// plugin database while the guarded work writes the SDK database through
    /// other connections, so no SDK call can deadlock against it. Requests
    /// without a request id never take it (unchanged behaviour).
    /// </summary>
    private Task<T> WithOccurrenceLockAsync<T>(int complianceId, Func<Task<T>> body) =>
        WithOccurrenceLockAsync(complianceId, _ => body());

    /// <summary>
    /// As above, handing the body the Compliance <c>WorkflowState</c> read BY
    /// the locking statement itself (current committed state, not a
    /// snapshot), so a body can refuse an occurrence that was closed between
    /// the caller's earlier lookup and the lock.
    /// </summary>
    private async Task<T> WithOccurrenceLockAsync<T>(int complianceId, Func<string?, Task<T>> body)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await dbContext.Database.BeginTransactionAsync().ConfigureAwait(false);
            var lockedState = (await dbContext.Database
                    .SqlQuery<string>(
                        $"SELECT `WorkflowState` AS `Value` FROM `Compliances` WHERE `Id` = {complianceId} FOR UPDATE")
                    .ToListAsync()
                    .ConfigureAwait(false))
                .FirstOrDefault();
            var result = await body(lockedState).ConfigureAwait(false);
            await tx.CommitAsync().ConfigureAwait(false);
            return result;
        }).ConfigureAwait(false);
    }

    /// <summary>Reads the current envelope of a case, bypassing the change tracker.</summary>
    private static async Task<EventCustomEnvelope?> ReadEnvelopeFreshAsync(
        MicrotingDbContext sdkDbContext, int caseId)
    {
        var custom = await sdkDbContext.Cases
            .AsNoTracking()
            .Where(c => c.Id == caseId)
            .Select(c => c.Custom)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);
        return custom == null ? null : TryParseEnvelope(custom);
    }

    /// <summary>
    /// Records a non-completion request id on the case (fresh load, so it
    /// never overwrites what another writer just stored). No-op when
    /// <c>Custom</c> holds non-envelope legacy content.
    /// </summary>
    private static async Task RecordAppliedRequestAsync(
        MicrotingDbContext sdkDbContext, int caseId, string requestId, string operation, string result)
    {
        var theCase = await sdkDbContext.Cases.FirstOrDefaultAsync(c => c.Id == caseId).ConfigureAwait(false);
        if (theCase == null)
        {
            return;
        }

        await sdkDbContext.Entry(theCase).ReloadAsync().ConfigureAwait(false);
        if (!IsEnvelopeWritable(theCase.Custom, out var envelope))
        {
            return;
        }

        AddAppliedRequest(envelope, requestId, operation, result);
        theCase.Custom = SerializeEnvelopeOrEmpty(envelope);
        await theCase.Update(sdkDbContext).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------
    // CompleteEvent
    // ------------------------------------------------------------------

    /// <summary>
    /// Compliance rows of <paramref name="itemPlanningId"/> (any workflow
    /// state) that a request names by <c>compliance_id</c> or
    /// <c>microting_sdk_case_id</c>; empty when it names neither. These are
    /// the cases a request id can have been recorded on, including the
    /// stale-id redirect target.
    /// </summary>
    private async Task<List<Compliance>> FindNamedCompliancesAsync(
        long requestedComplianceId, long requestedCaseId, int itemPlanningId, CancellationToken ct)
    {
        var complianceId = (int)requestedComplianceId;
        var caseId = (int)requestedCaseId;
        if (complianceId <= 0 && caseId <= 0)
        {
            return [];
        }

        return await dbContext.Compliances
            .AsNoTracking()
            .Where(x => x.PlanningId == itemPlanningId)
            .Where(x => (complianceId > 0 && x.Id == complianceId)
                        || (caseId > 0 && x.MicrotingSdkCaseId == caseId))
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// A request-id write (CompleteEvent, SetComment, SetFieldValue,
    /// UploadPhoto, RemovePhoto, and the completed=false re-tap) that names
    /// its SDK case but no compliance id. Such a request resolves against that
    /// case only (<see cref="FindComplianceForNamedCaseAsync"/>), on first
    /// delivery and on retry; without a request id, or naming neither id, the
    /// legacy "earliest occurrence" lookup stays.
    /// </summary>
    private static bool NamesCaseOnly(string requestId, long complianceId, long caseId) =>
        requestId.Length > 0 && complianceId <= 0 && caseId > 0;

    /// <summary>
    /// The compliance of <paramref name="itemPlanningId"/> backed by SDK case
    /// <paramref name="requestedCaseId"/>, provided that case is homed on the
    /// caller's site; null otherwise. <paramref name="liveOnly"/>: only a
    /// non-removed row (the occurrence to complete); otherwise any state,
    /// newest first (the "already completed" lookup).
    /// </summary>
    private async Task<Compliance?> FindComplianceForNamedCaseAsync(
        long requestedCaseId,
        int itemPlanningId,
        int sdkSiteId,
        bool liveOnly,
        MicrotingDbContext sdkDbContext,
        CancellationToken ct)
    {
        var caseId = (int)requestedCaseId;
        var onCallersSite = await sdkDbContext.Cases
            .AsNoTracking()
            .AnyAsync(c => c.Id == caseId && c.SiteId == sdkSiteId, ct)
            .ConfigureAwait(false);
        if (!onCallersSite)
        {
            return null;
        }

        var query = dbContext.Compliances
            .Where(x => x.PlanningId == itemPlanningId)
            .Where(x => x.MicrotingSdkCaseId == caseId);
        return liveOnly
            ? await query
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed || x.WorkflowState == null)
                .OrderBy(x => x.Deadline)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false)
            : await query
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
    }

    /// <summary>
    /// Phase 1 of the completion record, set in the same <c>Update</c> that
    /// sets <c>Status = 100</c>: "this request id is completing the case".
    /// A concurrent repeat that sees it answers <c>ABORTED</c> (retry), never
    /// <c>replayed</c>, because the bundled answers may still be pending.
    /// Skipped for non-envelope or foreign <c>Custom</c> content.
    /// </summary>
    private static void StampCompletionInFlight(SdkCase foundCase, string requestId)
    {
        if (!IsEnvelopeWritable(foundCase.Custom, out var envelope))
        {
            return;
        }

        envelope.CompletingRequestId = requestId;
        envelope.CompletingTsUnix = ToUnixSeconds(DateTime.UtcNow);
        foundCase.Custom = SerializeEnvelopeOrEmpty(envelope);
    }

    /// <summary>
    /// Phase 2, only after the case update AND the bundled answers succeeded:
    /// publish <c>completed_request_id</c> (the replay marker) and clear the
    /// in-flight marker. Built from a FRESH read of <c>Custom</c>, so it never
    /// writes back a stale copy. If the handler fails before this point the
    /// marker stays "completing"; once it is older than
    /// <see cref="InFlightClaimWindow"/> a repeat gets today's "already
    /// completed" answer (answers flagged as discarded), never a false replay.
    /// </summary>
    private static async Task PublishCompletionAppliedAsync(
        MicrotingDbContext sdkDbContext, SdkCase foundCase, string requestId)
    {
        var freshCustom = await sdkDbContext.Cases
            .AsNoTracking()
            .Where(c => c.Id == foundCase.Id)
            .Select(c => c.Custom)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);
        if (!IsEnvelopeWritable(freshCustom, out var envelope))
        {
            return;
        }

        envelope.CompletedRequestId = requestId;
        envelope.CompletingRequestId = null;
        envelope.CompletingTsUnix = null;
        foundCase.Custom = SerializeEnvelopeOrEmpty(envelope);
        await foundCase.Update(sdkDbContext).ConfigureAwait(false);
    }

    private static bool IsOwnCompletionInFlight(EventCustomEnvelope? envelope, string requestId) =>
        envelope?.CompletingRequestId == requestId
        && envelope.CompletingTsUnix is { } startedUnix
        && DateTimeOffset.FromUnixTimeSeconds(startedUnix).UtcDateTime >= DateTime.UtcNow - InFlightClaimWindow;

    /// <summary>
    /// Looks at every case the request names (scoped to the caller's
    /// planning): a published record of this request id = the replay
    /// response; this request id still completing (recently) = in flight.
    /// </summary>
    private async Task<(CompleteEventResponse? Replay, bool InFlight)> ProbeOwnCompletionAsync(
        CompleteEventRequest request,
        string requestId,
        int itemPlanningId,
        int propertyId,
        int eventId,
        int sdkSiteId,
        MicrotingDbContext sdkDbContext,
        CancellationToken ct)
    {
        var inFlight = false;
        var candidates = await FindNamedCompliancesAsync(
                request.ComplianceId, request.MicrotingSdkCaseId, itemPlanningId, ct)
            .ConfigureAwait(false);
        foreach (var candidate in candidates)
        {
            var envelope = await ReadEnvelopeFreshAsync(sdkDbContext, candidate.MicrotingSdkCaseId)
                .ConfigureAwait(false);
            if (envelope?.CompletedRequestId == requestId)
            {
                return (await BuildCompletionReplayAsync(
                        candidate, request, requestId, propertyId, eventId, sdkSiteId, ct)
                    .ConfigureAwait(false), false);
            }

            inFlight |= IsOwnCompletionInFlight(envelope, requestId);
        }

        return (null, inFlight);
    }

    /// <summary>
    /// The replay answer for a completion this request id already applied:
    /// success with the current Event (same echo as the success path),
    /// <c>replayed=true</c>, <c>answers_discarded=false</c>, no Error log.
    /// </summary>
    private async Task<CompleteEventResponse> BuildCompletionReplayAsync(
        Compliance candidate,
        CompleteEventRequest request,
        string requestId,
        int propertyId,
        int eventId,
        int sdkSiteId,
        CancellationToken ct)
    {
        logger.LogInformation(
            "CompleteEvent replay: requestId={RequestId} eventId={EventId} complianceId={ComplianceId} "
            + "microtingSdkCaseId={SdkCaseId} was already applied; answering with the current state",
            requestId, eventId, candidate.Id, candidate.MicrotingSdkCaseId);

        var deadline = candidate.Deadline != default ? candidate.Deadline : DateTime.UtcNow;
        var dayKey = deadline.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var (evt, task) = await BuildCompletionEchoAsync(
                propertyId, eventId, dayKey, sdkSiteId, request.CompletedBy, ct)
            .ConfigureAwait(false);
        await StampWriteReplyAsync(evt, candidate.Id, candidate.MicrotingSdkCaseId, task).ConfigureAwait(false);
        return new CompleteEventResponse { Event = evt, Replayed = true };
    }

    /// <summary>
    /// A request-id caller found the occurrence already claimed: either it
    /// lost the claim to a concurrent request (<paramref name="waitForWinner"/>
    /// true) or its lookup resolved no live compliance. The winner commits the
    /// claim BEFORE it updates the case, so the decision is taken from ONE
    /// read of the case row - Status, DoneAt and Custom are written by the
    /// same update, so they are seen together or not at all:
    /// our own id there = replay; a completed case without it = the normal
    /// "already completed" answer (a different request); still open = wait
    /// (bounded) when <paramref name="waitForWinner"/>, then <c>ABORTED</c>
    /// (retryable in the app) instead of the permanent
    /// <c>FAILED_PRECONDITION</c>; without waiting, today's answer.
    /// </summary>
    private async Task<CompleteEventResponse> ResolveClaimedOccurrenceForRequestIdAsync(
        Compliance? settled,
        bool waitForWinner,
        CompleteEventRequest request,
        string requestId,
        int itemPlanningId,
        int propertyId,
        int eventId,
        int sdkSiteId,
        MicrotingDbContext sdkDbContext,
        CancellationToken ct)
    {
        for (var step = 0; step < LostClaimWaitSteps; step++)
        {
            var (replay, ownInFlight) = await ProbeOwnCompletionAsync(
                    request, requestId, itemPlanningId, propertyId, eventId, sdkSiteId, sdkDbContext, ct)
                .ConfigureAwait(false);
            if (replay != null)
            {
                return replay;
            }

            if (ownInFlight)
            {
                // This very request is still completing (its answers may not
                // be saved yet): never answer replayed or discarded; wait.
                await Task.Delay(LostClaimWaitStep, ct).ConfigureAwait(false);
                continue;
            }

            var settledCase = settled == null
                ? null
                : await sdkDbContext.Cases
                    .AsNoTracking()
                    .Where(x => x.Id == settled.MicrotingSdkCaseId)
                    .Select(x => new { x.Status, x.DoneAt, x.Custom })
                    .FirstOrDefaultAsync(ct)
                    .ConfigureAwait(false);

            var settledEnvelope = settledCase == null ? null : TryParseEnvelope(settledCase.Custom);
            if (settledEnvelope?.CompletedRequestId == requestId)
            {
                // The winner's update landed between the two reads; the next
                // replay attempt sees it.
                continue;
            }

            if (IsOwnCompletionInFlight(settledEnvelope, requestId))
            {
                await Task.Delay(LostClaimWaitStep, ct).ConfigureAwait(false);
                continue;
            }

            var done = settledCase is { Status: 100, DoneAt: not null };
            if (done || !waitForWinner || settled == null)
            {
                var response = await AlreadyCompletedWithDiscardTrackingAsync(
                        settled, eventId, sdkDbContext, request, sdkSiteId)
                    .ConfigureAwait(false);
                await StampWriteReplyAsync(response.Event, settled?.Id ?? 0, settled?.MicrotingSdkCaseId ?? 0, null)
                    .ConfigureAwait(false);
                return response;
            }

            await Task.Delay(LostClaimWaitStep, ct).ConfigureAwait(false);
        }

        throw new RpcException(new Status(StatusCode.Aborted,
            $"Event {eventId}: another completion of this occurrence is still being applied; retry."));
    }

    private static bool LooksLikeClaimInFlight(
        Compliance? settled) =>
        settled?.UpdatedAt is { } updatedAt
        && updatedAt >= DateTime.UtcNow - InFlightClaimWindow;

    // ------------------------------------------------------------------
    // SetComment / SetFieldValue
    // ------------------------------------------------------------------

    /// <summary>
    /// SetComment with a request id, under the occurrence lock: a repeat of
    /// an applied id writes nothing and echoes the CURRENT comment (a later
    /// comment is never overwritten by a delayed repeat); otherwise writes
    /// exactly what the legacy path writes and records the id.
    /// </summary>
    private async Task<(bool Replayed, string EchoComment)> ApplyCommentWithRequestIdAsync(
        int complianceId,
        MicrotingDbContext sdkDbContext,
        SdkCase foundCase,
        string trimmed,
        DateTime commentAtUtc,
        string requestId)
    {
        return await WithOccurrenceLockAsync(complianceId, async () =>
        {
            await sdkDbContext.Entry(foundCase).ReloadAsync().ConfigureAwait(false);
            var existingEnvelope = TryParseEnvelope(foundCase.Custom);
            if (FindAppliedRequest(existingEnvelope, requestId, OpSetComment) != null)
            {
                return (true, existingEnvelope?.EventComment?.Text ?? string.Empty);
            }

            var nextEnvelope = existingEnvelope ?? new EventCustomEnvelope();
            nextEnvelope.EventComment = string.IsNullOrEmpty(trimmed)
                ? null
                : new EventCommentBody { Text = trimmed, TsUnix = ToUnixSeconds(commentAtUtc) };
            AddAppliedRequest(nextEnvelope, requestId, OpSetComment, string.Empty);
            foundCase.Custom = SerializeEnvelopeOrEmpty(nextEnvelope);
            await foundCase.Update(sdkDbContext).ConfigureAwait(false);
            return (false, trimmed);
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// SetFieldValue repeat detection BEFORE the live-compliance lookup, so a
    /// repeat that arrives after the task was completed is answered as a
    /// replay instead of the legacy <c>FAILED_PRECONDITION</c>. Returns the
    /// compliance whose case recorded the id, or null.
    /// </summary>
    private async Task<Compliance?>
        FindComplianceThatAppliedAsync(
            long requestedComplianceId,
            long requestedCaseId,
            int itemPlanningId,
            string requestId,
            string operation,
            MicrotingDbContext sdkDbContext)
    {
        var candidates = await FindNamedCompliancesAsync(
                requestedComplianceId, requestedCaseId, itemPlanningId, CancellationToken.None)
            .ConfigureAwait(false);

        foreach (var candidate in candidates)
        {
            var envelope = await ReadEnvelopeFreshAsync(sdkDbContext, candidate.MicrotingSdkCaseId)
                .ConfigureAwait(false);
            if (FindAppliedRequest(envelope, requestId, operation) != null)
            {
                return candidate;
            }
        }

        return null;
    }

    // ------------------------------------------------------------------
    // RemovePhoto
    // ------------------------------------------------------------------

    private static bool IsContractRemove(RemovePhotoRequest request) =>
        !string.IsNullOrWhiteSpace(request.RequestId) || !string.IsNullOrWhiteSpace(request.ExpectedStorageId);

    /// <summary>The reply when there is no case / envelope / photo: empty for old apps.</summary>
    private static RemovePhotoResponse NothingToRemoveResponse(RemovePhotoRequest request) =>
        IsContractRemove(request)
            ? new RemovePhotoResponse { Outcome = RemovePhotoOutcome.AlreadyAbsent }
            : new RemovePhotoResponse();

    /// <summary>
    /// RemovePhoto with <c>request_id</c> and/or <c>expected_storage_id</c>,
    /// under the occurrence lock. Removes the slot's photo only when it is
    /// the expected one (or no expectation was sent); a slot now holding a
    /// different photo is left alone and answered as
    /// <see cref="RemovePhotoOutcome.SlotHoldsOtherPhoto"/>. A repeat of an
    /// applied request id answers its recorded outcome with
    /// <c>replayed=true</c>, so a pure repeat never looks like a conflict.
    /// </summary>
    private async Task<RemovePhotoResponse> RemovePhotoWithContractAsync(
        RemovePhotoRequest request,
        int complianceId,
        MicrotingDbContext sdkDbContext,
        SdkCase foundCase)
    {
        var requestId = NormalizeRequestId(request.RequestId);
        var expected = request.ExpectedStorageId?.Trim() ?? string.Empty;

        return await WithOccurrenceLockAsync(complianceId, async () =>
        {
            await sdkDbContext.Entry(foundCase).ReloadAsync().ConfigureAwait(false);
            // canRecord: bookkeeping may be written (no foreign keys, see
            // HasOnlyKnownEnvelopeKeys). contentWritable: a photo removal may
            // rewrite the envelope exactly as the legacy RemovePhoto does.
            var canRecord = IsEnvelopeWritable(foundCase.Custom, out var envelope);
            var contentWritable = canRecord || TryParseEnvelope(foundCase.Custom) != null;

            if (requestId.Length > 0)
            {
                var prior = FindAppliedRequest(envelope, requestId, OpRemovePhoto);
                if (prior != null)
                {
                    var replayed = DecodeRemoveResult(prior.Result);
                    replayed.Replayed = true;
                    return replayed;
                }
            }

            var photo = envelope.EventPhotos?.FirstOrDefault(p => p.Slot == request.Slot);
            RemovePhotoResponse response;
            string recordedResult;
            var changed = false;
            if (photo == null)
            {
                response = new RemovePhotoResponse { Outcome = RemovePhotoOutcome.AlreadyAbsent };
                recordedResult = RemoveResultAbsent;
            }
            else if (expected.Length > 0
                     && expected != photo.UploadedDataId.ToString(CultureInfo.InvariantCulture))
            {
                var current = photo.UploadedDataId.ToString(CultureInfo.InvariantCulture);
                response = new RemovePhotoResponse
                {
                    Outcome = RemovePhotoOutcome.SlotHoldsOtherPhoto,
                    CurrentStorageId = current
                };
                recordedResult = RemoveResultOtherPrefix + current;
            }
            else
            {
                if (photo.UploadedDataId > 0)
                {
                    var uploadedData = await sdkDbContext.UploadedDatas
                        .FirstOrDefaultAsync(u => u.Id == photo.UploadedDataId)
                        .ConfigureAwait(false);
                    if (uploadedData != null
                        && uploadedData.WorkflowState != Constants.WorkflowStates.Removed)
                    {
                        await uploadedData.Delete(sdkDbContext).ConfigureAwait(false);
                    }
                }

                envelope.EventPhotos!.Remove(photo);
                changed = true;
                response = new RemovePhotoResponse { Outcome = RemovePhotoOutcome.Removed };
                recordedResult = RemoveResultRemoved;
            }

            if (canRecord && requestId.Length > 0)
            {
                AddAppliedRequest(envelope, requestId, OpRemovePhoto, recordedResult);
                changed = true;
            }

            if (changed && contentWritable)
            {
                foundCase.Custom = SerializeEnvelopeOrEmpty(envelope);
                await foundCase.Update(sdkDbContext).ConfigureAwait(false);
            }

            return response;
        }).ConfigureAwait(false);
    }

    private static RemovePhotoResponse DecodeRemoveResult(string result)
    {
        if (result.StartsWith(RemoveResultOtherPrefix, StringComparison.Ordinal))
        {
            return new RemovePhotoResponse
            {
                Outcome = RemovePhotoOutcome.SlotHoldsOtherPhoto,
                CurrentStorageId = result[RemoveResultOtherPrefix.Length..]
            };
        }

        return new RemovePhotoResponse
        {
            Outcome = result == RemoveResultRemoved
                ? RemovePhotoOutcome.Removed
                : RemovePhotoOutcome.AlreadyAbsent
        };
    }

    // ------------------------------------------------------------------
    // Event version / etag / due
    // ------------------------------------------------------------------

    /// <summary>
    /// Read paths (ListEvents, ListTaskTracker, the stream, GetEvent): due
    /// fields from the calendar row, then version and etag.
    /// </summary>
    private async Task DecorateReadEventsAsync(
        IReadOnlyList<(Event Evt, CalendarTaskResponseModel Task)> rows)
    {
        if (rows.Count == 0)
        {
            return;
        }

        await ApplyDueFieldsAsync(rows).ConfigureAwait(false);

        await StampVersionsAsync(rows
                .Select(r => (r.Evt, r.Task.ComplianceId ?? 0, r.Task.SdkCaseId ?? 0))
                .ToList())
            .ConfigureAwait(false);

        foreach (var (evt, _) in rows)
        {
            evt.Etag = ComputeEtag(evt);
        }
    }

    /// <summary>
    /// Stream path: version + etag for exactly the events about to be sent
    /// (the initial snapshot and the ones whose content hash changed). Due
    /// fields were already set by <see cref="LoadEventsAsync"/>, and the
    /// change hash never depends on version/etag, so unchanged events cost
    /// no version queries on a poll.
    /// </summary>
    private async Task StampEventsToSendAsync(IReadOnlyList<Event> events)
    {
        if (events.Count == 0)
        {
            return;
        }

        await StampVersionsAsync(events
                .Select(e => (e, (int)e.ComplianceId, (int)e.MicrotingSdkCaseId))
                .ToList())
            .ConfigureAwait(false);
        foreach (var evt in events)
        {
            evt.Etag = ComputeEtag(evt);
        }
    }

    /// <summary>
    /// Write replies of request-id requests: due fields when the echo found a
    /// calendar row, then version (from the compliance / case the write
    /// touched) and etag. Never called for legacy requests.
    /// <para>
    /// When the echo found NO calendar row (the actionable-only read omits a
    /// completed occurrence) the synthesised Event says <c>completed=false</c>;
    /// the completion state is then read from the backing SDK case, so a reply
    /// carrying a completed occurrence's ids never reports it as open. It only
    /// ever sets <c>completed</c> to true (a "completed" answer is never
    /// downgraded).
    /// </para>
    /// </summary>
    private async Task StampWriteReplyAsync(
        Event? evt, int complianceId, int caseId, CalendarTaskResponseModel? task)
    {
        if (evt == null)
        {
            return;
        }

        if (task != null)
        {
            await ApplyDueFieldsAsync([(evt, task)]).ConfigureAwait(false);
        }
        else if (!evt.Completed && caseId > 0)
        {
            var core = await coreHelper.GetCore().ConfigureAwait(false);
            var sdkDbContext = core.DbContextHelper.GetDbContext();
            evt.Completed = await sdkDbContext.Cases
                .AsNoTracking()
                .AnyAsync(c => c.Id == caseId && c.Status == 100)
                .ConfigureAwait(false);
        }

        await StampVersionsAsync([(evt, complianceId, caseId)]).ConfigureAwait(false);
        evt.Etag = ComputeEtag(evt);
    }

    /// <summary>
    /// Due fields from the calendar row. Some calendar rows carry a backing
    /// compliance id without its <c>Deadline</c> (the recurrence fallback in
    /// <c>BackendConfigurationCalendarService</c> copies only the ids); those
    /// deadlines are batch-read by compliance id here rather than changing the
    /// calendar model other callers use. <c>TaskDate</c> is never substituted:
    /// a moved occurrence's date differs from its deadline.
    /// </summary>
    private async Task ApplyDueFieldsAsync(IReadOnlyCollection<(Event Evt, CalendarTaskResponseModel Task)> rows)
    {
        var missing = rows
            .Where(r => r.Task.Deadline == null && r.Task.ComplianceId is > 0)
            .Select(r => r.Task.ComplianceId!.Value)
            .Distinct()
            .ToList();
        var deadlineByComplianceId = missing.Count == 0
            ? new Dictionary<int, DateTime>()
            : await dbContext.Compliances.AsNoTracking()
                .Where(x => missing.Contains(x.Id))
                .Select(x => new { x.Id, x.Deadline })
                .ToDictionaryAsync(x => x.Id, x => x.Deadline)
                .ConfigureAwait(false);

        foreach (var (evt, task) in rows)
        {
            DateTime? deadline = task.Deadline;
            if (deadline == null && task.ComplianceId is { } complianceId
                && deadlineByComplianceId.TryGetValue(complianceId, out var stored) && stored != default)
            {
                deadline = stored;
            }

            evt.DueDateKey = deadline?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
            evt.DueTimeLocal = task.IsAllDay ? string.Empty : FormatStartHour(task.StartHour);
        }
    }

    /// <summary>
    /// SHA-256 (lower-case hex) of the Event's protobuf bytes with
    /// <c>version</c> and <c>etag</c> cleared. Protobuf C# serialisation of
    /// this message (no map fields) is deterministic, so equal content gives
    /// an equal etag across calls and servers of the same version. The
    /// stream's change detection uses the same value, so a version that moves
    /// without a content change never causes a resend.
    /// </summary>
    internal static string ComputeEtag(Event evt)
    {
        var content = evt.Clone();
        content.ClearVersion();
        content.Etag = string.Empty;
        return Convert.ToHexStringLower(SHA256.HashData(content.ToByteArray()));
    }

    /// <summary>
    /// <c>version</c> = newest <c>UpdatedAt</c> (UTC microseconds since the
    /// epoch) over: AreaRulePlanning, its CalendarConfiguration(s),
    /// CalendarOccurrenceException(s) and AreaRulePlanningFile(s), the
    /// ItemsPlanning Planning, the Compliance, the SDK Case and that case's
    /// FieldValues. Batched: one query per table for the whole list.
    /// Rows stamped by <c>PnBase</c>/<c>BaseEntity</c> with
    /// <c>DateTime.UtcNow</c>; a value is read as UTC.
    /// </summary>
    private async Task StampVersionsAsync(IReadOnlyList<(Event Evt, int ComplianceId, int CaseId)> rows)
    {
        var arpIdByEvent = new Dictionary<Event, int>(ReferenceEqualityComparer.Instance);
        foreach (var row in rows)
        {
            if (int.TryParse(row.Evt.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var arpId))
            {
                arpIdByEvent[row.Evt] = arpId;
            }
        }

        var arpIds = arpIdByEvent.Values.Distinct().ToList();
        var complianceIds = rows.Select(r => r.ComplianceId).Where(id => id > 0).Distinct().ToList();
        var caseIds = rows.Select(r => r.CaseId).Where(id => id > 0).Distinct().ToList();

        var arps = await dbContext.AreaRulePlannings.AsNoTracking()
            .Where(x => arpIds.Contains(x.Id))
            .Select(x => new { x.Id, x.UpdatedAt, x.ItemPlanningId })
            .ToListAsync().ConfigureAwait(false);
        var configStamps = await dbContext.CalendarConfigurations.AsNoTracking()
            .Where(x => arpIds.Contains(x.AreaRulePlanningId))
            .GroupBy(x => x.AreaRulePlanningId)
            .Select(g => new { ArpId = g.Key, Max = g.Max(x => x.UpdatedAt) })
            .ToDictionaryAsync(x => x.ArpId, x => x.Max).ConfigureAwait(false);
        var exceptionStamps = await dbContext.CalendarOccurrenceExceptions.AsNoTracking()
            .Where(x => arpIds.Contains(x.AreaRulePlanningId))
            .GroupBy(x => x.AreaRulePlanningId)
            .Select(g => new { ArpId = g.Key, Max = g.Max(x => x.UpdatedAt) })
            .ToDictionaryAsync(x => x.ArpId, x => x.Max).ConfigureAwait(false);
        var fileStamps = await dbContext.AreaRulePlanningFiles.AsNoTracking()
            .Where(x => arpIds.Contains(x.AreaRulePlanningId))
            .GroupBy(x => x.AreaRulePlanningId)
            .Select(g => new { ArpId = g.Key, Max = g.Max(x => x.UpdatedAt) })
            .ToDictionaryAsync(x => x.ArpId, x => x.Max).ConfigureAwait(false);
        var complianceStamps = await dbContext.Compliances.AsNoTracking()
            .Where(x => complianceIds.Contains(x.Id))
            .Select(x => new { x.Id, x.UpdatedAt })
            .ToDictionaryAsync(x => x.Id, x => x.UpdatedAt).ConfigureAwait(false);

        var planningIds = arps.Select(a => a.ItemPlanningId).Where(id => id > 0).Distinct().ToList();
        var planningStamps = await itemsPlanningPnDbContext.Plannings.AsNoTracking()
            .Where(x => planningIds.Contains(x.Id))
            .Select(x => new { x.Id, x.UpdatedAt })
            .ToDictionaryAsync(x => x.Id, x => x.UpdatedAt).ConfigureAwait(false);

        var caseStamps = new Dictionary<int, DateTime?>();
        var fieldValueStamps = new Dictionary<int, DateTime?>();
        if (caseIds.Count > 0)
        {
            var core = await coreHelper.GetCore().ConfigureAwait(false);
            var sdkDbContext = core.DbContextHelper.GetDbContext();
            caseStamps = await sdkDbContext.Cases.AsNoTracking()
                .Where(x => caseIds.Contains(x.Id))
                .Select(x => new { x.Id, x.UpdatedAt })
                .ToDictionaryAsync(x => x.Id, x => x.UpdatedAt).ConfigureAwait(false);
            fieldValueStamps = await sdkDbContext.FieldValues.AsNoTracking()
                .Where(x => x.CaseId != null && caseIds.Contains(x.CaseId.Value))
                .GroupBy(x => x.CaseId!.Value)
                .Select(g => new { CaseId = g.Key, Max = g.Max(x => x.UpdatedAt) })
                .ToDictionaryAsync(x => x.CaseId, x => x.Max).ConfigureAwait(false);
        }

        var arpById = arps.ToDictionary(a => a.Id);
        foreach (var row in rows)
        {
            DateTime? newest = null;

            void Consider(DateTime? stamp)
            {
                if (stamp.HasValue && (!newest.HasValue || stamp.Value > newest.Value))
                {
                    newest = stamp;
                }
            }

            if (arpIdByEvent.TryGetValue(row.Evt, out var arpId) && arpById.TryGetValue(arpId, out var arp))
            {
                Consider(arp.UpdatedAt);
                Consider(configStamps.GetValueOrDefault(arpId));
                Consider(exceptionStamps.GetValueOrDefault(arpId));
                Consider(fileStamps.GetValueOrDefault(arpId));
                Consider(planningStamps.GetValueOrDefault(arp.ItemPlanningId));
            }

            Consider(complianceStamps.GetValueOrDefault(row.ComplianceId));
            Consider(caseStamps.GetValueOrDefault(row.CaseId));
            Consider(fieldValueStamps.GetValueOrDefault(row.CaseId));

            if (newest.HasValue)
            {
                row.Evt.Version = ToUnixMicroseconds(newest.Value);
            }
            else
            {
                row.Evt.ClearVersion();
            }
        }
    }

    private static long ToUnixMicroseconds(DateTime stamp)
    {
        var utc = DateTime.SpecifyKind(stamp, DateTimeKind.Utc);
        return (utc - DateTime.UnixEpoch).Ticks / TimeSpan.TicksPerMicrosecond;
    }

    /// <summary>One remembered request id in the <c>Case.Custom</c> envelope.</summary>
    private sealed class AppliedRequestRecord
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("op")]
        public string Operation { get; set; } = string.Empty;

        // Operation-specific recorded result: the storage id for
        // upload_photo, the outcome for remove_photo, empty otherwise.
        [JsonPropertyName("result")]
        public string Result { get; set; } = string.Empty;

        [JsonPropertyName("ts_unix")]
        public long TsUnix { get; set; }
    }
}
