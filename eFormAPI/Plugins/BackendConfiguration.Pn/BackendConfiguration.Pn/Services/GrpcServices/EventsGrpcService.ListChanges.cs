#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Grpc.Documents;
using BackendConfiguration.Pn.Grpc.Events;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Models;
using SdkLanguage = Microting.eForm.Infrastructure.Data.Entities.Language;

namespace BackendConfiguration.Pn.Services.GrpcServices;

/// <summary>
/// Contract revision 2: <c>ListChanges</c>. New code only; ListEvents,
/// ListTaskTracker, the stream and the writes are untouched, and the stream
/// keeps its own <c>EventChange</c>.
/// </summary>
public partial class EventsGrpcService
{
    /// <summary>Window when the request names none: today .. today+2, the app's list window.</summary>
    private const int DefaultListChangesWindowDays = 3;

    /// <summary>
    /// The legacy list's deploy range (the app asks for today..today+2);
    /// ListChanges runs exactly that deploy when its window touches it.
    /// </summary>
    private const int ListDeployFutureDays = 2;

    /// <summary>The calendar expands recurrences correctly for at most one 7-day window per read.</summary>
    private const int ListChangesChunkDays = 7;

    /// <summary>
    /// A stored occurrence is only trusted as unchanged without a form read
    /// when its version stamp is older than this. SDK stamps behave as whole
    /// seconds and come from each app server's clock, so a write in the same
    /// second as the previous read, or on an instance whose clock is a few
    /// seconds behind, can leave the version equal; 10 s covers the second
    /// plus a few seconds of clock skew and commit delay. Such entries are read.
    /// </summary>
    internal static readonly TimeSpan UnchangedTrustAge = TimeSpan.FromSeconds(10);

    private const string DateKeyFormat = ListChangesCursor.DateKeyFormat;

    /// <summary>
    /// Reads one case's form. Production uses the SDK's <c>CaseRead</c>; the
    /// seam lets tests count and fail reads.
    /// </summary>
    internal Func<eFormCore.Core, int, SdkLanguage, Task<ReplyElement?>> ReadCaseFormAsync { get; init; } =
        async (core, caseId, language) => await core.CaseRead(caseId, language).ConfigureAwait(false);

    /// <summary>
    /// See the proto. Every property the caller is assigned to (the same
    /// PropertyWorkers rows ListEvents checks), read like ListEvents and
    /// hydrated per occurrence, diffed against the cursor.
    /// </summary>
    public override async Task<ListChangesResponse> ListChanges(
        ListChangesRequest request,
        ServerCallContext context)
    {
        try
        {
            return await ListChangesCoreAsync(request, context.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not RpcException and not OperationCanceledException)
        {
            // Read-failure boundary: every status the handler chose itself
            // (INVALID_ARGUMENT, PERMISSION_DENIED, RESOURCE_EXHAUSTED, the
            // resolver's UNAUTHENTICATED, ...) and cancellation pass through;
            // anything else is a failed read the client may retry.
            logger.LogError(ex, "EventsGrpcService.ListChanges: read failed");
            throw new RpcException(new Status(StatusCode.Unavailable,
                "The events could not be read right now; retry."));
        }
    }

    private async Task<ListChangesResponse> ListChangesCoreAsync(ListChangesRequest request, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var (from, to) = ResolveListChangesWindow(request.FromDateKey, request.ToDateKey, today);

        var sdkSiteId = await siteResolver.GetSdkSiteIdAsync().ConfigureAwait(false);
        if (sdkSiteId <= 0)
        {
            // ListEvents' answer for a caller without a worker site; never an empty snapshot.
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Caller has no worker site."));
        }

        var propertyIds = (await userPropertyAccess.GetAccessiblePropertyIdsAsync(sdkSiteId).ConfigureAwait(false))
            .Distinct().Order().ToList();
        if (propertyIds.Count > ListChangesCursor.MaxProperties)
        {
            // A cursor names at most MaxProperties; a bigger set would reset
            // every call, so it is refused before anything is read.
            logger.LogWarning(
                "EventsGrpcService.ListChanges: sdkSiteId={SdkSiteId} is assigned to {Count} properties (max {Max})",
                sdkSiteId, propertyIds.Count, ListChangesCursor.MaxProperties);
            throw new RpcException(new Status(StatusCode.ResourceExhausted,
                $"The worker is assigned to more than {ListChangesCursor.MaxProperties} properties."));
        }
        var languageId = await siteResolver.GetSiteLanguageIdAsync(sdkSiteId).ConfigureAwait(false);
        var prior = ResolveCursor(request.Cursor, sdkSiteId, languageId ?? 0, from, to, today);

        var response = new ListChangesResponse
        {
            Reset = prior == null,
            FromDateKey = from.ToString(DateKeyFormat, CultureInfo.InvariantCulture),
            ToDateKey = to.ToString(DateKeyFormat, CultureInfo.InvariantCulture)
        };
        await AddPropertiesAndBoardsAsync(response, propertyIds).ConfigureAwait(false);
        if (prior != null)
        {
            response.RemovedPropertyIds.AddRange(prior.PropertyIds.Except(propertyIds)
                .Select(id => id.ToString(CultureInfo.InvariantCulture)));
        }

        var rows = await ReadWindowRowsAsync(propertyIds, from, to, today, sdkSiteId, languageId, ct)
            .ConfigureAwait(false);
        var occurrences = await HydrateOccurrencesAsync(rows, prior, languageId, ct).ConfigureAwait(false);

        if (prior == null && occurrences.Any(o => o.Unreadable))
        {
            // A reset tells the client to drop what is not in it; a transient
            // form-read failure must not make tasks vanish.
            throw new RpcException(new Status(StatusCode.Unavailable,
                "Some events could not be read right now; retry."));
        }

        var current = DiffAgainstCursor(response, occurrences, prior);

        response.NextCursor = ByteString.CopyFrom(ListChangesCursor.Encode(
            new ListChangesCursorState(sdkSiteId, languageId ?? 0, from, to, today, propertyIds, current)));

        logger.LogDebug(
            "EventsGrpcService.ListChanges: sdkSiteId={SdkSiteId} window={From}..{To} reset={Reset} occurrences={Count} formReads={Reads} changes={Changes}",
            sdkSiteId, response.FromDateKey, response.ToDateKey, response.Reset, current.Count,
            occurrences.Count(o => o.FormRead), response.Changes.Count);
        return response;
    }

    // ------------------------------------------------------------------
    // Steps
    // ------------------------------------------------------------------

    /// <summary>
    /// Both keys empty: today .. today+2. Otherwise both yyyy-MM-dd with
    /// from &lt;= to, clamped to the stream's window (today-7 .. today+30);
    /// a window entirely outside it is INVALID_ARGUMENT.
    /// </summary>
    private static (DateOnly From, DateOnly To) ResolveListChangesWindow(string? fromKey, string? toKey, DateOnly today)
    {
        if (string.IsNullOrEmpty(fromKey) && string.IsNullOrEmpty(toKey))
        {
            return (today, today.AddDays(DefaultListChangesWindowDays - 1));
        }

        if (!DateOnly.TryParseExact(fromKey, DateKeyFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var from)
            || !DateOnly.TryParseExact(toKey, DateKeyFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var to)
            || to < from)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                "from_date_key and to_date_key must both be yyyy-MM-dd with from <= to, or both empty."));
        }

        var earliest = today.AddDays(-StreamWatchPastDays);
        var latest = today.AddDays(StreamWatchFutureDays);
        if (to < earliest || from > latest)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                "The window must overlap the stream's window (today-7 .. today+30)."));
        }

        return (from < earliest ? earliest : from, to > latest ? latest : to);
    }

    /// <summary>The cursor's state, or null (reset) when absent or unusable for this caller and window.</summary>
    private ListChangesCursorState? ResolveCursor(
        ByteString cursor, int sdkSiteId, int languageId, DateOnly from, DateOnly to, DateOnly today)
    {
        var prior = ListChangesCursor.TryDecode(cursor.Span, out var reason);
        if (prior != null)
        {
            reason = BindingMismatch(prior, sdkSiteId, languageId, from, to, today);
            if (reason.Length > 0)
            {
                prior = null;
            }
        }

        if (prior == null && !cursor.IsEmpty)
        {
            logger.LogInformation(
                "EventsGrpcService.ListChanges: cursor not usable ({Reason}) for sdkSiteId={SdkSiteId}; answering a full snapshot",
                reason, sdkSiteId);
        }

        return prior;
    }

    /// <summary>
    /// Why a decoded cursor belongs to another caller, language, window or
    /// UTC day; empty when it fits. The day binding makes every client take a
    /// reset snapshot at its first call of a day, so changes that move no
    /// stamp (template, translation, entity options) are never older than that.
    /// </summary>
    private static string BindingMismatch(
        ListChangesCursorState prior, int sdkSiteId, int languageId, DateOnly from, DateOnly to, DateOnly today)
    {
        if (prior.IssuedDay != today)
        {
            return "day";
        }

        if (prior.SdkSiteId != sdkSiteId)
        {
            return "caller";
        }

        if (prior.LanguageId != languageId)
        {
            return "language";
        }

        if (prior.From != from || prior.To != to)
        {
            return "window";
        }

        return string.Empty;
    }

    /// <summary>
    /// The caller's properties (named as ListProperties names them) and their
    /// boards (as the app's GetBoards call). A failed read is UNAVAILABLE.
    /// </summary>
    private async Task AddPropertiesAndBoardsAsync(ListChangesResponse response, IReadOnlyList<int> propertyIds)
    {
        if (propertyIds.Count == 0)
        {
            return;
        }

        var dictionary = await propertiesService.GetCommonDictionary(false).ConfigureAwait(false);
        if (!dictionary.Success || dictionary.Model == null)
        {
            logger.LogWarning("EventsGrpcService.ListChanges: property read failed: {Message}", dictionary.Message);
            throw new RpcException(new Status(StatusCode.Unavailable,
                "The properties could not be read right now; retry."));
        }

        var accessible = propertyIds.ToHashSet();
        foreach (var item in dictionary.Model)
        {
            if (item.Id is { } id && accessible.Contains(id))
            {
                response.Properties.Add(new BackendConfiguration.Pn.Grpc.Events.Property
                {
                    Id = id.ToString(CultureInfo.InvariantCulture),
                    Name = item.Name ?? string.Empty
                });
            }
        }

        foreach (var propertyId in propertyIds)
        {
            var boards = await calendarService.GetBoards(propertyId).ConfigureAwait(false);
            if (!boards.Success || boards.Model == null)
            {
                logger.LogWarning(
                    "EventsGrpcService.ListChanges: board read failed for property {PropertyId}: {Message}",
                    propertyId, boards.Message);
                throw new RpcException(new Status(StatusCode.Unavailable,
                    "The boards could not be read right now; retry."));
            }

            response.Boards.AddRange(boards.Model.Select(board => new Board
            {
                Id = board.Id.ToString(CultureInfo.InvariantCulture),
                PropertyId = board.PropertyId.ToString(CultureInfo.InvariantCulture),
                Name = board.Name ?? string.Empty,
                ColorHex = board.Color ?? string.Empty
            }));
        }
    }

    /// <summary>
    /// Per property: the eager deploy the legacy list would run (only the
    /// window's part inside today..today+2; a failure is logged and the read
    /// goes on, as on the stream), then ListEvents' calendar read for the
    /// whole window. A failed read is UNAVAILABLE; too many rows is
    /// RESOURCE_EXHAUSTED.
    /// </summary>
    private async Task<List<CalendarTaskResponseModel>> ReadWindowRowsAsync(
        IReadOnlyList<int> propertyIds, DateOnly from, DateOnly to, DateOnly today,
        int sdkSiteId, int? languageId, CancellationToken ct)
    {
        var fromKey = from.ToString(DateKeyFormat, CultureInfo.InvariantCulture);
        var toKey = to.ToString(DateKeyFormat, CultureInfo.InvariantCulture);
        // Exactly the deploy the legacy list runs for the app's window
        // (today..today+2, all boards): switching an app from ListEvents to
        // ListChanges must not create a case the list would not. Skipped when
        // the served window does not touch those days.
        var deployFrom = today;
        var deployTo = today.AddDays(ListDeployFutureDays);
        var deploy = from <= deployTo && to >= deployFrom;

        var rows = new List<CalendarTaskResponseModel>();
        foreach (var propertyId in propertyIds)
        {
            ct.ThrowIfCancellationRequested();
            if (deploy)
            {
                try
                {
                    await eventDeployService.EnsureDeployedAsync(
                        propertyId.ToString(CultureInfo.InvariantCulture), [],
                        deployFrom.ToString(DateKeyFormat, CultureInfo.InvariantCulture),
                        deployTo.ToString(DateKeyFormat, CultureInfo.InvariantCulture),
                        sdkSiteId, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex,
                        "EventsGrpcService.ListChanges: eager deploy failed for sdkSiteId={SdkSiteId} property={PropertyId}; serving what is deployed",
                        sdkSiteId, propertyId);
                }
            }

            // GetTasksForWeek expands weekly weekday rules (and caps month/year
            // candidates) for one 7-day window only, so the window is read in
            // 7-day chunks. A row is served when its effective date is inside
            // the window (a scope=this move can carry it outside), once per key.
            var chunks = new List<CalendarChunk>();
            for (var chunkStart = from; chunkStart <= to; chunkStart = chunkStart.AddDays(ListChangesChunkDays))
            {
                var chunkEnd = chunkStart.AddDays(ListChangesChunkDays - 1);
                if (chunkEnd > to)
                {
                    chunkEnd = to;
                }

                chunks.Add(new CalendarChunk(
                    await ReadCalendarAsync(propertyId, chunkStart, chunkEnd, sdkSiteId, languageId).ConfigureAwait(false),
                    chunkStart, chunkEnd));
            }

            // The cap applies before the moved-in read below.
            var inWindow = Served(chunks.SelectMany(chunk => chunk.Rows), from, to).Count;
            if (rows.Count + inWindow > ListChangesCursor.MaxEntries)
            {
                throw TooManyOccurrences(sdkSiteId, fromKey, toKey);
            }

            var propertyRows = await ResolveMovedInRowsAsync(chunks, propertyId, sdkSiteId, languageId, ct)
                .ConfigureAwait(false);
            rows.AddRange(Served(propertyRows, from, to));
            if (rows.Count > ListChangesCursor.MaxEntries)
            {
                throw TooManyOccurrences(sdkSiteId, fromKey, toKey);
            }
        }

        return rows;
    }

    private RpcException TooManyOccurrences(int sdkSiteId, string fromKey, string toKey)
    {
        logger.LogWarning(
            "EventsGrpcService.ListChanges: more than {Max} occurrences for sdkSiteId={SdkSiteId} in {From}..{To}",
            ListChangesCursor.MaxEntries, sdkSiteId, fromKey, toKey);
        return new RpcException(new Status(StatusCode.ResourceExhausted,
            $"The window holds more than {ListChangesCursor.MaxEntries} occurrences; request a narrower window."));
    }

    /// <summary>One 7-day calendar read of the window.</summary>
    private sealed record CalendarChunk(List<CalendarTaskResponseModel> Rows, DateOnly Start, DateOnly End);

    /// <summary>Rows that are served: active, dated inside the window, once per wire key.</summary>
    private static List<CalendarTaskResponseModel> Served(
        IEnumerable<CalendarTaskResponseModel> rows, DateOnly from, DateOnly to) =>
        rows.Where(r => r.Status && IsInWindow(r.TaskDate, from, to))
            .DistinctBy(r => (r.Id, r.TaskDate, r.ComplianceId ?? 0))
            .ToList();

    private static bool IsInWindow(string? dateKey, DateOnly from, DateOnly to) =>
        DateOnly.TryParseExact(dateKey, DateKeyFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
        && day >= from && day <= to;

    /// <summary>ListEvents' calendar read for one property and range; a failed read is UNAVAILABLE.</summary>
    private async Task<List<CalendarTaskResponseModel>> ReadCalendarAsync(
        int propertyId, DateOnly start, DateOnly end, int sdkSiteId, int? languageId)
    {
        var result = await calendarService.GetTasksForWeek(new CalendarTaskRequestModel
        {
            PropertyId = propertyId,
            WeekStart = start.ToString(DateKeyFormat, CultureInfo.InvariantCulture),
            WeekEnd = end.ToString(DateKeyFormat, CultureInfo.InvariantCulture),
            BoardIds = [],
            TagNames = [],
            // #931: only events assigned to the caller's own site.
            SiteIds = [sdkSiteId],
            ActionableOnly = true,
            LanguageId = languageId
        }).ConfigureAwait(false);

        if (!result.Success || result.Model == null)
        {
            logger.LogWarning(
                "EventsGrpcService.ListChanges: calendar read failed for sdkSiteId={SdkSiteId} property={PropertyId}: {Message}",
                sdkSiteId, propertyId, result.Message);
            throw new RpcException(new Status(StatusCode.Unavailable,
                "The events could not be read right now; retry."));
        }

        return result.Model;
    }

    /// <summary>
    /// A moved-in eForm row whose original date is outside its chunk comes
    /// without ids; it is replaced by the rows GetEvent finds for its key. One
    /// shared read binds the open occurrences; each match is kept only when
    /// its own key's GetEvent range (the same <see cref="GetEventWindowInputs"/>)
    /// holds its compliance's deadline. Retracted ones: <see cref="ReadRetractedMovedInAsync"/>.
    /// </summary>
    private async Task<List<CalendarTaskResponseModel>> ResolveMovedInRowsAsync(
        IReadOnlyList<CalendarChunk> chunks, int propertyId, int sdkSiteId, int? languageId, CancellationToken ct)
    {
        var kept = new List<CalendarTaskResponseModel>();
        var movedIn = new List<(CalendarTaskResponseModel Row, CalendarChunk Chunk)>();
        foreach (var chunk in chunks)
        {
            foreach (var row in chunk.Rows)
            {
                if (row.ExceptionId is > 0 && row.ComplianceId is not > 0 && row.EformId is > 0
                    && IsInWindow(row.TaskDate, chunk.Start, chunk.End))
                {
                    movedIn.Add((row, chunk));
                }
                else
                {
                    kept.Add(row);
                }
            }
        }

        if (movedIn.Count == 0)
        {
            return kept;
        }

        var windows = await LoadGetEventWindowInputsAsync(
                movedIn.Select(m => (m.Row.Id, ToUtcDateTime(ParseDayKey(m.Row.TaskDate!)))).Distinct().ToList(), [], ct)
            .ConfigureAwait(false);
        var needed = new List<MovedInOccurrence>();
        foreach (var (row, chunk) in movedIn)
        {
            if (windows.OriginalOf(row.ExceptionId!.Value) is not { } originalDate || row.PlanningId is not { } planningId)
            {
                continue;
            }

            var original = DateOnly.FromDateTime(originalDate);
            if (original >= chunk.Start && original <= chunk.End)
            {
                continue; // the chunk's own read carries the deployed row
            }

            var day = ParseDayKey(row.TaskDate!);
            if (Math.Abs(original.DayNumber - day.DayNumber) > MaxGetEventWindowWideningDays)
            {
                continue; // beyond GetEvent's widening: GetEvent cannot find it either
            }

            // GetEvent's range for (task, day); a key naming the compliance on
            // the original date has the same range (that deadline is in it).
            var (readFrom, readTo) = windows.WindowFor(row.Id, planningId, ToUtcDateTime(day), 0);
            needed.Add(new MovedInOccurrence(row, planningId, original,
                DateOnly.FromDateTime(readFrom), DateOnly.FromDateTime(readTo)));
        }

        if (needed.Count == 0)
        {
            return kept;
        }

        var sharedFrom = needed.Min(n => n.ReadFrom);
        var sharedTo = needed.Max(n => n.ReadTo);
        var shared = await ReadCalendarAsync(propertyId, sharedFrom, sharedTo, sdkSiteId, languageId)
            .ConfigureAwait(false);
        await AddRowsGetEventFindsAsync(kept, needed, shared, sharedFrom, sharedTo, windows, ct).ConfigureAwait(false);
        await ReadRetractedMovedInAsync(kept, needed, propertyId, sdkSiteId, languageId, windows, ct).ConfigureAwait(false);
        return kept;
    }

    /// <summary>
    /// A retracted occurrence (case pulled, Compliance kept) gets its ids only
    /// from the recurrence expansion of its original date, which a weekday rule
    /// does for the first seven days of a read only, so the shared read can
    /// miss it. Each occurrence whose original-date Compliance is still unbound
    /// is read with its own GetEvent range, one read per distinct range.
    /// </summary>
    private async Task ReadRetractedMovedInAsync(
        List<CalendarTaskResponseModel> kept, IReadOnlyList<MovedInOccurrence> needed, int propertyId,
        int sdkSiteId, int? languageId, GetEventWindowInputs windows, CancellationToken ct)
    {
        var planningIds = needed.Select(n => n.PlanningId).Distinct().ToList();
        var firstOriginal = ToUtcDateTime(needed.Min(n => n.Original));
        var afterLastOriginal = ToUtcDateTime(needed.Max(n => n.Original).AddDays(1));
        var compliancesOnOriginal = (await dbContext.Compliances.AsNoTracking()
                .Where(x => x.PropertyId == propertyId && planningIds.Contains(x.PlanningId))
                .Where(x => x.Deadline >= firstOriginal && x.Deadline < afterLastOriginal)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed
                            || x.WorkflowState == null
                            || x.MicrotingSdkCaseId > 0)
                .Select(x => new { x.Id, x.PlanningId, x.Deadline })
                .ToListAsync(ct)
                .ConfigureAwait(false))
            .ToLookup(x => (x.PlanningId, DateOnly.FromDateTime(x.Deadline)), x => x.Id);
        var boundIds = kept.Where(t => t.ComplianceId is > 0)
            .ToLookup(t => (t.Id, t.TaskDate), t => t.ComplianceId!.Value);
        var unbound = needed
            .Where(n => compliancesOnOriginal[(n.PlanningId, n.Original)]
                .Except(boundIds[(n.Row.Id, n.Row.TaskDate)])
                .Any())
            .ToList();
        foreach (var range in unbound.GroupBy(n => (n.ReadFrom, n.ReadTo)))
        {
            var read = await ReadCalendarAsync(propertyId, range.Key.ReadFrom, range.Key.ReadTo, sdkSiteId, languageId)
                .ConfigureAwait(false);
            await AddRowsGetEventFindsAsync(kept, range.ToList(), read, range.Key.ReadFrom, range.Key.ReadTo, windows, ct)
                .ConfigureAwait(false);
        }
    }

    /// <summary>A moved-in occurrence and the range GetEvent reads for its (task, day).</summary>
    private sealed record MovedInOccurrence(
        CalendarTaskResponseModel Row, int PlanningId, DateOnly Original, DateOnly ReadFrom, DateOnly ReadTo);

    /// <summary>
    /// Adds the compliance-backed rows of a read at each occurrence's (task,
    /// day), indexed once (linear, in read order), keeping a row only when
    /// GetEvent would find it: its key's GetEvent range holds the compliance's
    /// deadline (the original date, also for a recurrence-backed row) and lies
    /// inside the read.
    /// </summary>
    private async Task AddRowsGetEventFindsAsync(
        List<CalendarTaskResponseModel> kept, IReadOnlyList<MovedInOccurrence> occurrences,
        IEnumerable<CalendarTaskResponseModel> read, DateOnly readFrom, DateOnly readTo,
        GetEventWindowInputs windows, CancellationToken ct)
    {
        var bound = read.Where(t => t.ComplianceId is > 0).ToLookup(t => (t.Id, t.TaskDate));
        var matches = occurrences
            .DistinctBy(n => (n.Row.Id, n.Row.TaskDate))
            .SelectMany(n => bound[(n.Row.Id, n.Row.TaskDate)].Select(row => (Occurrence: n, Row: row)))
            .ToList();
        await windows.LoadMissingDeadlinesAsync(this, matches.Select(m => m.Row.ComplianceId!.Value), ct)
            .ConfigureAwait(false);

        foreach (var (occurrence, row) in matches)
        {
            var complianceId = row.ComplianceId!.Value;
            if (windows.DeadlineOf(complianceId, occurrence.PlanningId) is not { } deadline)
            {
                continue;
            }

            var day = ToUtcDateTime(ParseDayKey(row.TaskDate!));
            var (from, to) = windows.WindowFor(row.Id, occurrence.PlanningId, day, complianceId);
            if (deadline >= from && deadline <= to
                && DateOnly.FromDateTime(from) >= readFrom && DateOnly.FromDateTime(to) <= readTo)
            {
                kept.Add(row);
            }
        }
    }

    private static DateOnly ParseDayKey(string dateKey) =>
        DateOnly.ParseExact(dateKey, DateKeyFormat, CultureInfo.InvariantCulture);

    private static DateTime ToUtcDateTime(DateOnly day) => day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    /// <summary>One occurrence after hydration.</summary>
    private sealed record HydratedOccurrence(
        OccurrenceKey Key, Event? Evt, CursorEntry Entry, bool Unreadable, bool FormRead);

    /// <summary>
    /// Builds every occurrence with its OWN case's comment and fields, like a
    /// one-row GetEvent, so its etag equals GetEvent's. Everything but the
    /// form is batched (envelopes, attachments, due fields, version stamps).
    /// The form needs one SDK CaseRead per case, skipped when the cursor
    /// proves the occurrence unchanged: same row hash (the Event without its
    /// fields) and same version stamp, older than <see cref="UnchangedTrustAge"/>.
    /// An occurrence with an eForm but no case, or no form yet, is not
    /// delivered; a failed form read marks it unreadable.
    /// </summary>
    private async Task<List<HydratedOccurrence>> HydrateOccurrencesAsync(
        IReadOnlyList<CalendarTaskResponseModel> rows, ListChangesCursorState? prior, int? languageId,
        CancellationToken ct)
    {
        var result = new List<HydratedOccurrence>(rows.Count);
        if (rows.Count == 0)
        {
            return result;
        }

        var core = await coreHelper.GetCore().ConfigureAwait(false);
        var sdkDbContext = core.DbContextHelper.GetDbContext();

        var caseIds = rows.Where(r => r.SdkCaseId is > 0).Select(r => r.SdkCaseId!.Value).Distinct().ToList();
        var customByCaseId = caseIds.Count == 0
            ? new Dictionary<int, string?>()
            : await sdkDbContext.Cases.AsNoTracking()
                .Where(c => caseIds.Contains(c.Id))
                .Select(c => new { c.Id, c.Custom })
                .ToDictionaryAsync(c => c.Id, c => (string?)c.Custom, ct)
                .ConfigureAwait(false);

        var arpIds = rows.Select(r => r.Id).Distinct().ToList();
        var attachmentsByArpId = (await dbContext.AreaRulePlanningFiles
                .Where(f => arpIds.Contains(f.AreaRulePlanningId)
                            && f.WorkflowState != Constants.WorkflowStates.Removed)
                .OrderBy(f => f.Id)
                .Select(f => new { f.AreaRulePlanningId, f.Id, f.OriginalFileName, f.MimeType, f.SizeBytes })
                .ToListAsync(ct)
                .ConfigureAwait(false))
            .ToLookup(f => f.AreaRulePlanningId);

        // The Event without its form fields, for every row.
        var built = new List<(Event Evt, CalendarTaskResponseModel Task)>(rows.Count);
        foreach (var task in rows)
        {
            var caseId = task.SdkCaseId ?? 0;
            if (task.EformId is > 0 && caseId <= 0)
            {
                continue; // eForm, but not deployed yet: nothing to show (GetEvent's rule).
            }

            var custom = customByCaseId.GetValueOrDefault(caseId);
            var evt = new Event
            {
                Id = task.Id.ToString(CultureInfo.InvariantCulture),
                PropertyId = task.PropertyId.ToString(CultureInfo.InvariantCulture),
                BoardId = task.BoardId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                PlanDayKey = task.TaskDate ?? string.Empty,
                PlannedAt = FormatStartHour(task.StartHour),
                TaskText = task.Title ?? string.Empty,
                CalendarColor = task.Color ?? string.Empty,
                Completed = task.Completed,
                CompletedBy = string.Empty,
                DescriptionHtml = task.DescriptionHtml ?? string.Empty,
                Comment = (custom == null ? null : TryParseEnvelope(custom))?.EventComment?.Text ?? string.Empty,
                EformId = task.EformId ?? 0,
                ComplianceId = task.ComplianceId ?? 0,
                MicrotingSdkCaseId = caseId,
                TaskIsExpired = task.TaskIsExpired
            };
            // Same projection as PopulateCalendarAttachments.
            evt.Attachments.AddRange(attachmentsByArpId[task.Id].Select(f => new Attachment
            {
                Source = AttachmentSource.CalendarFile,
                Id = f.Id,
                OriginalFileName = f.OriginalFileName ?? string.Empty,
                MimeType = f.MimeType ?? string.Empty,
                SizeBytes = f.SizeBytes,
                Name = f.OriginalFileName ?? f.Id.ToString(CultureInfo.InvariantCulture)
            }));
            built.Add((evt, task));
        }

        await ApplyDueFieldsAsync(built).ConfigureAwait(false);
        await StampVersionsAsync(built.Select(b => (b.Evt, b.Task.ComplianceId ?? 0, b.Task.SdkCaseId ?? 0)).ToList())
            .ConfigureAwait(false);

        // Decide which forms must be read.
        var trustBefore = ToUnixMicroseconds(DateTime.UtcNow - UnchangedTrustAge);
        var pending = new List<(Event Evt, CalendarTaskResponseModel Task, OccurrenceKey Key, ulong RowHash)>();
        foreach (var (evt, task) in built)
        {
            var key = new OccurrenceKey(task.Id, evt.PlanDayKey, evt.ComplianceId);
            var rowHash = ListChangesCursor.HashFromEtag(ComputeEtag(evt));
            long? version = evt.HasVersion ? evt.Version : null;
            if (evt.MicrotingSdkCaseId <= 0)
            {
                result.Add(new HydratedOccurrence(key, evt, new CursorEntry(rowHash, rowHash, version), false, false));
            }
            else if (prior != null && prior.Entries.TryGetValue(key, out var known)
                     && known.RowHash == rowHash && version is { } v && known.Version == v && v < trustBefore)
            {
                result.Add(new HydratedOccurrence(key, null, known, false, false));
            }
            else
            {
                pending.Add((evt, task, key, rowHash));
            }
        }

        var forms = await ReadFormsAsync(core, sdkDbContext, languageId,
            pending.Select(p => (int)p.Evt.MicrotingSdkCaseId).Distinct().ToList(),
            customByCaseId.Keys.ToHashSet(), ct).ConfigureAwait(false);

        foreach (var (evt, task, key, rowHash) in pending)
        {
            // A stamp still younger than the trust age when the form was read
            // is not remembered: a later write that leaves it unchanged would
            // otherwise be skipped by every following call. Without it the
            // next call reads this form again (trustBefore predates the read).
            long? version = evt.HasVersion && evt.Version < trustBefore ? evt.Version : null;
            switch (forms.GetValueOrDefault((int)evt.MicrotingSdkCaseId))
            {
                case { Failed: true }:
                    result.Add(new HydratedOccurrence(key, null, default, true, true));
                    break;
                case { Fields: { } fields }:
                    evt.Fields.AddRange(fields.Select(f => f.Clone()));
                    result.Add(new HydratedOccurrence(key, evt,
                        new CursorEntry(ListChangesCursor.HashFromEtag(ComputeEtag(evt)), rowHash, version), false, true));
                    break;
                default:
                    // No form yet: an eForm occurrence is held back (not delivered);
                    // one without an eForm is delivered without fields, as the list does.
                    if (task.EformId is not > 0)
                    {
                        result.Add(new HydratedOccurrence(key, evt, new CursorEntry(rowHash, rowHash, version), false, true));
                    }

                    break;
            }
        }

        return result;
    }

    private sealed record FormRead(List<FormField>? Fields, bool Failed);

    /// <summary>
    /// One <see cref="ReadCaseFormAsync"/> per case (the SDK has no batch
    /// read). A null form for an existing case row, or an exception, is a
    /// failed read; a null form without a case row is "not yet".
    /// </summary>
    private async Task<Dictionary<int, FormRead>> ReadFormsAsync(
        eFormCore.Core core, Microting.eForm.Infrastructure.MicrotingDbContext sdkDbContext, int? languageId,
        IReadOnlyList<int> caseIds, IReadOnlySet<int> existingCaseIds, CancellationToken ct)
    {
        var forms = new Dictionary<int, FormRead>(caseIds.Count);
        if (caseIds.Count == 0)
        {
            return forms;
        }

        // Same language choice as LoadFieldsByTaskIdAsync, from the id resolved once.
        var language = (languageId.HasValue
                           ? await sdkDbContext.Languages.FirstOrDefaultAsync(l => l.Id == languageId.Value, ct)
                               .ConfigureAwait(false)
                           : null)
                       ?? await sdkDbContext.Languages.FirstAsync(ct).ConfigureAwait(false);

        foreach (var caseId in caseIds)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var form = await ReadCaseFormAsync(core, caseId, language).ConfigureAwait(false);
                if (form?.ElementList == null)
                {
                    forms[caseId] = new FormRead(null, existingCaseIds.Contains(caseId));
                    continue;
                }

                var fields = new List<FormField>();
                foreach (var element in form.ElementList)
                {
                    CollectFieldsFromElement(element, fields);
                }

                forms[caseId] = new FormRead(fields, false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex,
                    "EventsGrpcService.ListChanges: failed to read the form of SdkCaseId={CaseId}", caseId);
                forms[caseId] = new FormRead(null, true);
            }
        }

        return forms;
    }

    /// <summary>
    /// Adds removals (cursor keys no longer present) and upserts (new or
    /// changed content, with etag; version is already stamped) to the
    /// response and returns the next cursor's entries. Unreadable occurrences
    /// keep their cursor entry, so they are neither removed nor forgotten.
    /// </summary>
    private static Dictionary<OccurrenceKey, CursorEntry> DiffAgainstCursor(
        ListChangesResponse response, IReadOnlyList<HydratedOccurrence> occurrences, ListChangesCursorState? prior)
    {
        var current = new Dictionary<OccurrenceKey, CursorEntry>(occurrences.Count);
        var upserts = new List<Event>();
        foreach (var o in occurrences)
        {
            if (o.Unreadable)
            {
                if (prior != null && prior.Entries.TryGetValue(o.Key, out var known))
                {
                    current.TryAdd(o.Key, known);
                }

                continue;
            }

            // Two calendar rows with one wire key: the client's primary key holds one; keep the first.
            if (!current.TryAdd(o.Key, o.Entry) || o.Evt == null)
            {
                continue;
            }

            if (prior == null || !prior.Entries.TryGetValue(o.Key, out var before)
                              || before.ContentHash != o.Entry.ContentHash)
            {
                o.Evt.Etag = ComputeEtag(o.Evt);
                upserts.Add(o.Evt);
            }
        }

        if (prior != null)
        {
            foreach (var key in prior.Entries.Keys.Where(k => !current.ContainsKey(k)))
            {
                response.Changes.Add(new ListChange
                {
                    Removed = new EventKey
                    {
                        EventId = key.EventId.ToString(CultureInfo.InvariantCulture),
                        PlanDayKey = key.PlanDayKey,
                        ComplianceId = key.ComplianceId
                    }
                });
            }
        }

        response.Changes.AddRange(upserts.Select(e => new ListChange { Upserted = e }));
        return current;
    }
}
