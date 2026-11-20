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
    /// The days the legacy list eager-deploys (the app asks for today..today+2);
    /// ListChanges deploys only the part of its window inside them.
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
        var deployFrom = from > today ? from : today;
        var deployTo = to < today.AddDays(ListDeployFutureDays) ? to : today.AddDays(ListDeployFutureDays);

        var rows = new List<CalendarTaskResponseModel>();
        foreach (var propertyId in propertyIds)
        {
            ct.ThrowIfCancellationRequested();
            if (deployFrom <= deployTo)
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
            var propertyRows = new List<CalendarTaskResponseModel>();
            for (var chunkStart = from; chunkStart <= to; chunkStart = chunkStart.AddDays(ListChangesChunkDays))
            {
                var chunkEnd = chunkStart.AddDays(ListChangesChunkDays - 1);
                if (chunkEnd > to)
                {
                    chunkEnd = to;
                }

                var chunkRows = await ReadCalendarAsync(propertyId, chunkStart, chunkEnd, sdkSiteId, languageId)
                    .ConfigureAwait(false);
                // Per chunk: a move from another chunk of the window is "moved in" for this one.
                propertyRows.AddRange(await ResolveMovedInRowsAsync(
                    chunkRows, propertyId, chunkStart, chunkEnd, sdkSiteId, languageId, ct).ConfigureAwait(false));
            }
            rows.AddRange(propertyRows
                .Where(r => r.Status && IsInWindow(r.TaskDate, from, to))
                .DistinctBy(r => (r.Id, r.TaskDate, r.ComplianceId ?? 0)));
            if (rows.Count > ListChangesCursor.MaxEntries)
            {
                logger.LogWarning(
                    "EventsGrpcService.ListChanges: more than {Max} occurrences for sdkSiteId={SdkSiteId} in {From}..{To}",
                    ListChangesCursor.MaxEntries, sdkSiteId, fromKey, toKey);
                throw new RpcException(new Status(StatusCode.ResourceExhausted,
                    $"The window holds more than {ListChangesCursor.MaxEntries} occurrences; request a narrower window."));
            }
        }

        return rows;
    }

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
    /// The calendar renders an occurrence moved INTO a read range from outside
    /// it without its compliance and case, and the read holding the original
    /// date omits it. For an eForm occurrence of one chunk whose original date
    /// is outside that chunk (another chunk of the window, or outside the
    /// window), the deployed row is read the way GetEvent reads it (its day
    /// widened to the original date, at most <see cref="MaxGetEventWindowWideningDays"/>),
    /// so it is served once, with its own ids, content and GetEvent etag. A
    /// move within the chunk needs nothing: that read carries the deployed row.
    /// </summary>
    private async Task<List<CalendarTaskResponseModel>> ResolveMovedInRowsAsync(
        List<CalendarTaskResponseModel> rows, int propertyId, DateOnly chunkStart, DateOnly chunkEnd,
        int sdkSiteId, int? languageId, CancellationToken ct)
    {
        var movedIn = rows
            .Where(r => r.ExceptionId is > 0 && r.ComplianceId is not > 0 && r.EformId is > 0
                        && IsInWindow(r.TaskDate, chunkStart, chunkEnd))
            .ToList();
        if (movedIn.Count == 0)
        {
            return rows;
        }

        var exceptionIds = movedIn.Select(r => r.ExceptionId!.Value).Distinct().ToList();
        var originalDates = await dbContext.CalendarOccurrenceExceptions.AsNoTracking()
            .Where(x => exceptionIds.Contains(x.Id))
            .Select(x => new { x.Id, x.OriginalDate })
            .ToDictionaryAsync(x => x.Id, x => DateOnly.FromDateTime(x.OriginalDate), ct)
            .ConfigureAwait(false);

        var resolved = rows.Except(movedIn).ToList();
        foreach (var row in movedIn)
        {
            if (!originalDates.TryGetValue(row.ExceptionId!.Value, out var original)
                || (original >= chunkStart && original <= chunkEnd))
            {
                continue;
            }

            var day = DateTime.ParseExact(row.TaskDate!, DateKeyFormat, CultureInfo.InvariantCulture);
            var (start, end) = await ResolveGetEventWindowAsync(row.Id, row.PlanningId ?? 0, day,
                new EventKey { EventId = row.Id.ToString(CultureInfo.InvariantCulture), PlanDayKey = row.TaskDate },
                ct).ConfigureAwait(false);
            var read = await ReadCalendarAsync(propertyId, DateOnly.FromDateTime(start), DateOnly.FromDateTime(end),
                sdkSiteId, languageId).ConfigureAwait(false);
            resolved.AddRange(read.Where(t => t.Id == row.Id && t.TaskDate == row.TaskDate && t.ComplianceId is > 0));
        }

        return resolved;
    }

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
