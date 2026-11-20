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
    /// <c>StreamEventChanges</c> (<see cref="LoadEventsAsync"/>: the
    /// one-day calendar read scoped to the caller's site with
    /// <c>ActionableOnly</c>, plus envelope, fields, attachments, due, version
    /// and etag), so its etag equals the list's etag for unchanged content.
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
            .Select(x => new { x.PropertyId })
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

        var events = await LoadEventsAsync(arp.PropertyId, [], day, day, sdkSiteId, context.CancellationToken)
            .ConfigureAwait(false);
        var wantedId = eventId.ToString(CultureInfo.InvariantCulture);
        var match = events.FirstOrDefault(e =>
            e.Id == wantedId
            && e.PlanDayKey == key.PlanDayKey
            && (!key.HasComplianceId || e.ComplianceId == key.ComplianceId));

        return match == null ? notFound : new GetEventResponse { Event = match, Found = true };
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
        return parsed != null;
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
    private async Task<T> WithOccurrenceLockAsync<T>(int complianceId, Func<Task<T>> body)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await dbContext.Database.BeginTransactionAsync().ConfigureAwait(false);
            await dbContext.Database
                .ExecuteSqlAsync($"SELECT `Id` FROM `Compliances` WHERE `Id` = {complianceId} FOR UPDATE")
                .ConfigureAwait(false);
            var result = await body().ConfigureAwait(false);
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
    /// Stamps the request id on the case in the same <c>Update</c> that sets
    /// <c>Status = 100</c>, so any reader that sees the completion also sees
    /// who applied it. Skipped for non-envelope legacy <c>Custom</c>.
    /// </summary>
    private static void StampCompletionRequestId(SdkCase foundCase, string requestId)
    {
        if (!IsEnvelopeWritable(foundCase.Custom, out var envelope))
        {
            return;
        }

        envelope.CompletedRequestId = requestId;
        foundCase.Custom = SerializeEnvelopeOrEmpty(envelope);
    }

    /// <summary>
    /// The occurrence is no longer open. If this very request id completed
    /// it (recorded on one of the cases the request names), answer success
    /// with the current Event and <c>replayed=true</c> instead of the false
    /// "already completed by you, answers discarded" answer. Null otherwise.
    /// <para>
    /// Candidate cases are those of compliance rows on this planning that the
    /// request names by <c>compliance_id</c> or <c>microting_sdk_case_id</c>
    /// (any workflow state, so the stale-id redirect target is covered too).
    /// A request with neither id cannot be replayed.
    /// </para>
    /// </summary>
    private async Task<CompleteEventResponse?> TryReplayCompletionAsync(
        CompleteEventRequest request,
        string requestId,
        int itemPlanningId,
        int propertyId,
        int eventId,
        int sdkSiteId,
        MicrotingDbContext sdkDbContext,
        CancellationToken ct)
    {
        var candidates = await FindNamedCompliancesAsync(
                request.ComplianceId, request.MicrotingSdkCaseId, itemPlanningId, ct)
            .ConfigureAwait(false);

        foreach (var candidate in candidates)
        {
            var envelope = await ReadEnvelopeFreshAsync(sdkDbContext, candidate.MicrotingSdkCaseId)
                .ConfigureAwait(false);
            if (envelope?.CompletedRequestId != requestId)
            {
                continue;
            }

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

        return null;
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
            var replay = await TryReplayCompletionAsync(
                    request, requestId, itemPlanningId, propertyId, eventId, sdkSiteId, sdkDbContext, ct)
                .ConfigureAwait(false);
            if (replay != null)
            {
                return replay;
            }

            var settledCase = settled == null
                ? null
                : await sdkDbContext.Cases
                    .AsNoTracking()
                    .Where(x => x.Id == settled.MicrotingSdkCaseId)
                    .Select(x => new { x.Status, x.DoneAt, x.Custom })
                    .FirstOrDefaultAsync(ct)
                    .ConfigureAwait(false);

            if (settledCase != null && TryParseEnvelope(settledCase.Custom)?.CompletedRequestId == requestId)
            {
                // The winner's update landed between the two reads; the next
                // replay attempt sees it.
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
            var writable = IsEnvelopeWritable(foundCase.Custom, out var envelope);

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

            if (writable && requestId.Length > 0)
            {
                AddAppliedRequest(envelope, requestId, OpRemovePhoto, recordedResult);
                changed = true;
            }

            if (changed && writable)
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

        ApplyDueFields(rows);

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
            ApplyDueFields(evt, task);
        }

        await StampVersionsAsync([(evt, complianceId, caseId)]).ConfigureAwait(false);
        evt.Etag = ComputeEtag(evt);
    }

    private static void ApplyDueFields(IEnumerable<(Event Evt, CalendarTaskResponseModel Task)> rows)
    {
        foreach (var (evt, task) in rows)
        {
            ApplyDueFields(evt, task);
        }
    }

    private static void ApplyDueFields(Event evt, CalendarTaskResponseModel task)
    {
        evt.DueDateKey = task.Deadline?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
        evt.DueTimeLocal = task.IsAllDay ? string.Empty : FormatStartHour(task.StartHour);
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
