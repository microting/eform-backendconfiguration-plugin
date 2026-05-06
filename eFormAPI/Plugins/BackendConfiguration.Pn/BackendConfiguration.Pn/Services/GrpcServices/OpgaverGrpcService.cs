using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Grpc.Documents;
using BackendConfiguration.Pn.Grpc.Opgaver;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.BackendConfigurationPropertiesService;
using BackendConfiguration.Pn.Services.UserPropertyAccess;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Models;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.ItemsPlanningBase.Infrastructure.Data;
using SdkUploadedData = Microting.eForm.Infrastructure.Data.Entities.UploadedData;
using SdkDataItem = Microting.eForm.Infrastructure.Models.DataItem;
using SdkElement = Microting.eForm.Infrastructure.Models.Element;

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
/// UploadPhoto / RemovePhoto extend the same <c>Custom</c> envelope with an
/// <c>opgaver_photos</c> array carrying <c>{slot, uploaded_data_id, ts_unix,
/// content_type}</c> per slot. Bytes are written to S3 via the eFormCore SDK's
/// <c>core.PutFileToS3Storage</c> (same path as
/// <c>BackendConfigurationTaskManagementService.CreateTask</c>); a row in the
/// SDK <c>uploaded_data</c> table tracks file metadata. RemovePhoto soft-
/// deletes the <c>UploadedData</c> row (<c>WorkflowState=Removed</c>) and
/// drops the slot entry from the envelope. ListOpgaver replays the envelope
/// and surfaces photos as <see cref="Attachment"/> messages. No new EF
/// entity / migration is introduced; the photo-upload pipeline reuses the
/// existing TaskManagement S3 / UploadedData pattern.
/// StreamOpgaveChanges is a poll-based server stream: the server emits a
/// snapshot at subscribe time, then re-queries every ~5s and diffs against
/// the previous result by JSON state-hash to emit <c>upserted</c> for
/// new/changed tasks and <c>removed_id</c> for tasks that fell out of the
/// watch window. v2 will likely replace this with an event-bus push model
/// once the JSON write paths emit change notifications. See
/// <see cref="StreamOpgaveChanges"/> for poll-window semantics.
///
/// Known divergences from the canonical
/// <c>BackendConfigurationCompliancesService.Update</c> JSON path.
/// CompleteOpgave NOW performs (added in this PR):
/// <list type="bullet">
///   <item><description><c>PlanningCaseSite</c> row update (Status=100,
///     MicrotingSdkCaseId, MicrotingSdkCaseDoneAt, DoneByUserId,
///     DoneByUserName) — mirrors
///     <c>BackendConfigurationCompliancesService.cs:307-318</c>.</description></item>
///   <item><description><c>PlanningCase</c> row update (Status=100,
///     WorkflowState=Processed, MicrotingSdkCaseDoneAt, DoneByUserId,
///     DoneByUserName) — mirrors lines 320-335.</description></item>
/// </list>
/// Still omitted (deferred; closing the full gap requires factoring a shared
/// completion helper — out of scope for this PR):
/// <list type="bullet">
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
/// </summary>
public class OpgaverGrpcService(
    IBackendConfigurationCalendarService calendarService,
    IBackendConfigurationPropertiesService propertiesService,
    IBackendConfigurationUserPropertyAccess userPropertyAccess,
    IGrpcSiteResolver siteResolver,
    IEFormCoreService coreHelper,
    BackendConfigurationPnDbContext dbContext,
    ItemsPlanningPnDbContext itemsPlanningPnDbContext,
    ILogger<OpgaverGrpcService> logger)
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
        // worker-supplied comment (written by SetComment) and photo metadata
        // (written by UploadPhoto / RemovePhoto, both stored as JSON in the
        // same Custom envelope) round-trip on subsequent reads. Recurrence-
        // only tasks have no SdkCaseId and therefore no Custom slot to read.
        var envelopeByTaskId = await LoadEnvelopeByTaskIdAsync(result.Model)
            .ConfigureAwait(false);

        // Pull eForm template field structure + current values per backing
        // SDK Case so the Flutter list view can render fields inline.
        // Recurrence-only tasks (no SdkCaseId yet) get no fields.
        var fieldsByTaskId = await LoadFieldsByTaskIdAsync(result.Model)
            .ConfigureAwait(false);

        foreach (var task in result.Model)
        {
            envelopeByTaskId.TryGetValue(task.Id, out var envelope);

            var comment = envelope?.OpgaverComment?.Text ?? string.Empty;

            var opgave = new Opgave
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
                Comment = comment,
                EformId = task.EformId ?? 0
                // updated_at: Timestamp default (zero) — no source field in CalendarTaskResponseModel.
            };

            PopulateAttachments(opgave, envelope);
            if (fieldsByTaskId.TryGetValue(task.Id, out var fields))
            {
                opgave.Fields.AddRange(fields);
            }
            response.Opgaver.Add(opgave);
        }

        return response;
    }

    /// <summary>
    /// Translates the <c>opgaver_photos</c> entries from the Case.Custom
    /// envelope into <see cref="Attachment"/> wire messages on the response
    /// Opgave. Internal storage is signalled with
    /// <see cref="AttachmentSource.Unspecified"/>; the <c>name</c> field
    /// carries the SDK <c>UploadedData.Id</c> as a string so clients can
    /// later request the bytes via Documents.GetAttachment (whose contract
    /// is opaque about what <c>name</c> is — internal id vs cloud-storage
    /// path — both are valid). Photos are emitted in slot order so clients
    /// can index them stably; entries with missing or invalid metadata
    /// are skipped silently.
    /// </summary>
    private static void PopulateAttachments(Opgave opgave, OpgaverCustomEnvelope? envelope)
    {
        if (envelope?.OpgaverPhotos == null)
        {
            return;
        }

        foreach (var photo in envelope.OpgaverPhotos.OrderBy(p => p.Slot))
        {
            if (photo.UploadedDataId <= 0)
            {
                continue;
            }

            opgave.Attachments.Add(new Attachment
            {
                Source = AttachmentSource.Unspecified,
                Name = photo.UploadedDataId.ToString(CultureInfo.InvariantCulture)
            });
        }
    }

    /// <summary>
    /// Reads the parsed Case.Custom envelope back for the given calendar
    /// tasks. Compliance-derived tasks carry the SDK Case id directly on
    /// the response model (see <c>BackendConfigurationCalendarService.cs</c>
    /// line 484), so we can batch-fetch the Cases in one query and parse
    /// their <c>Custom</c> column. Recurrence-only tasks (no Case yet) are
    /// skipped — there is no envelope storage slot for them in this PR.
    /// Non-envelope or malformed Custom values degrade silently to a null
    /// entry (the caller treats that as "no comment, no photos").
    /// </summary>
    private async Task<Dictionary<int, OpgaverCustomEnvelope?>> LoadEnvelopeByTaskIdAsync(
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
            return new Dictionary<int, OpgaverCustomEnvelope?>();
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

        var result = new Dictionary<int, OpgaverCustomEnvelope?>(taskIdToCaseId.Count);
        foreach (var (taskId, caseId) in taskIdToCaseId)
        {
            if (!customByCaseId.TryGetValue(caseId, out var customJson))
            {
                continue;
            }

            result[taskId] = TryParseEnvelope(customJson);
        }

        return result;
    }

    /// <summary>
    /// Reads the eForm template field structure + current values for each
    /// task that has a backing SDK Case. Uses
    /// <c>core.CaseRead(caseId, language)</c> — the same path
    /// <c>CompliancesGrpcService.ReadComplianceCase</c> takes — which
    /// returns a <see cref="ReplyElement"/> with <c>ElementList</c> already
    /// populated with each <see cref="SdkDataItem"/>'s definition AND its
    /// current worker-supplied value (where one exists), so a single call
    /// per Case yields everything we need (no separate template + values
    /// dance, no N templates lookup).
    ///
    /// Recurrence-only tasks (<c>SdkCaseId &lt;= 0</c>) are skipped — there
    /// is no Case row, so no fields can be reported. Per-task SDK lookup
    /// failures (Case missing, decode error) log a warning and produce an
    /// empty list for that task; the rest of the result set is unaffected.
    ///
    /// Field flattening: <see cref="GroupElement"/>s are walked recursively
    /// so nested fields surface alongside top-level fields in a single
    /// list — Flutter's inline renderer doesn't currently distinguish
    /// nesting depth.
    /// </summary>
    private async Task<Dictionary<int, List<FormField>>> LoadFieldsByTaskIdAsync(
        IReadOnlyCollection<CalendarTaskResponseModel> tasks)
    {
        var taskIdToCaseId = tasks
            .Where(t => t.SdkCaseId is > 0)
            .GroupBy(t => t.Id)
            .ToDictionary(g => g.Key, g => g.First().SdkCaseId!.Value);

        var result = new Dictionary<int, List<FormField>>(taskIdToCaseId.Count);
        if (taskIdToCaseId.Count == 0)
        {
            return result;
        }

        var core = await coreHelper.GetCore().ConfigureAwait(false);
        var sdkDbContext = core.DbContextHelper.GetDbContext();
        var language = await sdkDbContext.Languages.FirstAsync().ConfigureAwait(false);

        foreach (var (taskId, caseId) in taskIdToCaseId)
        {
            try
            {
                var theCase = await core.CaseRead(caseId, language).ConfigureAwait(false);
                if (theCase?.ElementList == null)
                {
                    continue;
                }

                var fields = new List<FormField>();
                foreach (var element in theCase.ElementList)
                {
                    CollectFieldsFromElement(element, fields);
                }
                result[taskId] = fields;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "OpgaverGrpcService.LoadFieldsByTaskIdAsync: failed to load FormField for task {TaskId} (SdkCaseId={CaseId})",
                    taskId, caseId);
            }
        }

        return result;
    }

    /// <summary>
    /// Walks a <see cref="SdkElement"/> tree and appends every
    /// <see cref="SdkDataItem"/> it encounters to <paramref name="target"/>
    /// as a <see cref="FormField"/>. <see cref="GroupElement"/>s recurse;
    /// <see cref="DataElement"/> / <see cref="CheckListValue"/> emit their
    /// own <c>DataItemList</c> as well as fields nested under
    /// <see cref="DataItemGroup"/>s.
    /// </summary>
    private static void CollectFieldsFromElement(SdkElement element, List<FormField> target)
    {
        switch (element)
        {
            case CheckListValue clv:
                AppendDataItems(clv.DataItemList, target);
                AppendDataItemGroups(clv.DataItemGroupList, target);
                break;
            case DataElement de:
                AppendDataItems(de.DataItemList, target);
                AppendDataItemGroups(de.DataItemGroupList, target);
                break;
            case GroupElement ge:
                if (ge.ElementList != null)
                {
                    foreach (var child in ge.ElementList)
                    {
                        CollectFieldsFromElement(child, target);
                    }
                }
                break;
        }
    }

    private static void AppendDataItems(List<SdkDataItem>? source, List<FormField> target)
    {
        if (source == null) return;
        foreach (var di in source)
        {
            target.Add(MapToFormField(di));
        }
    }

    private static void AppendDataItemGroups(List<DataItemGroup>? source, List<FormField> target)
    {
        if (source == null) return;
        foreach (var group in source)
        {
            AppendDataItems(group.DataItemList, target);
        }
    }

    /// <summary>
    /// Per-type extraction of the worker-facing value + selectable options
    /// for a <see cref="SdkDataItem"/>. Mirrors the dispatch in
    /// <c>CompliancesGrpcService.MapDataItems</c>; types not matched here
    /// fall through to an empty value (and field_type still carries the
    /// SDK class name so the client renders an "Unknown" placeholder).
    /// </summary>
    private static FormField MapToFormField(SdkDataItem di)
    {
        var field = new FormField
        {
            Id = di.Id,
            Label = di.Label ?? string.Empty,
            Description = di.Description?.InderValue ?? string.Empty,
            FieldType = di is Field sdkField ? sdkField.FieldType : di.GetType().Name,
            Required = di.Mandatory
        };

        switch (di)
        {
            case Date d:
                field.Value = d.DefaultValue ?? string.Empty;
                break;
            case Number n:
                field.Value = n.DefaultValue.ToString(CultureInfo.InvariantCulture);
                break;
            case NumberStepper ns:
                field.Value = ns.DefaultValue.ToString(CultureInfo.InvariantCulture);
                break;
            case Text t:
                field.Value = t.Value ?? string.Empty;
                break;
            case Comment c:
                field.Value = c.Value ?? string.Empty;
                break;
            case CheckBox cb:
                field.Value = cb.Selected ? "1" : "0";
                break;
            case ShowPdf sp:
                field.Value = sp.Value ?? string.Empty;
                break;
            case SaveButton sb:
                field.Value = sb.Value ?? string.Empty;
                break;
            case SingleSelect ss:
                AppendKeyValuePairOptions(ss.KeyValuePairList, field);
                break;
            case MultiSelect ms:
                AppendKeyValuePairOptions(ms.KeyValuePairList, field);
                break;
            case EntitySearch es:
                field.Value = es.DefaultValue.ToString(CultureInfo.InvariantCulture);
                break;
            case EntitySelect el:
                field.Value = el.DefaultValue.ToString(CultureInfo.InvariantCulture);
                break;
            case Field f:
                // SDK runtime wrapper (returned by CaseRead) — carries the actual answer value
                // and the canonical FieldType string (e.g. "Text", "Number", "CheckBox", ...).
                if (f.KeyValuePairList?.Count > 0)
                {
                    AppendKeyValuePairOptions(f.KeyValuePairList, field);
                }
                else
                {
                    field.Value = f.FieldValue ?? string.Empty;
                }
                break;
            default:
                field.Value = string.Empty;
                break;
        }

        return field;
    }

    /// <summary>
    /// Populates <see cref="FormField.Options"/> in display order and sets
    /// <see cref="FormField.Value"/> to the comma-joined values of the
    /// currently-selected entries (mirroring SDK convention for
    /// MultiSelect / SingleSelect).
    /// </summary>
    private static void AppendKeyValuePairOptions(
        List<Microting.eForm.Dto.KeyValuePair>? source, FormField field)
    {
        if (source == null) return;

        var ordered = source
            .OrderBy(kvp => int.TryParse(kvp.DisplayOrder, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var n) ? n : int.MaxValue)
            .ToList();

        var selected = new List<string>();
        foreach (var kvp in ordered)
        {
            field.Options.Add(kvp.Value ?? string.Empty);
            if (kvp.Selected)
            {
                selected.Add(kvp.Value ?? string.Empty);
            }
        }

        if (selected.Count > 0)
        {
            field.Value = string.Join(",", selected);
        }
    }

    /// <summary>
    /// Returns the parsed envelope from a <c>Case.Custom</c> string, or
    /// <c>null</c> if the value is missing, empty, or not in the expected
    /// shape (legacy free-form strings, garbage).
    /// </summary>
    private static OpgaverCustomEnvelope? TryParseEnvelope(string customJson)
    {
        if (string.IsNullOrWhiteSpace(customJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<OpgaverCustomEnvelope>(customJson);
        }
        catch (JsonException)
        {
            // Non-envelope / pre-existing free-form string — treat as empty.
            return null;
        }
    }

    /// <summary>
    /// Watch window for the streaming RPC: tasks scheduled within
    /// (today - <see cref="StreamWatchPastDays"/>) ..
    /// (today + <see cref="StreamWatchFutureDays"/>) are tracked. The bound
    /// keeps the per-subscriber state dictionary small (a few hundred entries
    /// in normal usage), and the past-days lookback ensures recently-completed
    /// tasks still emit a final upserted/removed event before falling out.
    /// </summary>
    private const int StreamWatchPastDays = 7;
    private const int StreamWatchFutureDays = 30;

    /// <summary>
    /// Poll cadence for <see cref="StreamOpgaveChanges"/>. Five seconds is a
    /// trade-off: tight enough that workers see each other's edits before
    /// they're confused by stale UI, loose enough not to hammer the DB
    /// across hundreds of concurrent kiosk subscribers. The value is constant
    /// (no config knob yet) — v2 with event-bus push will retire this.
    /// </summary>
    private static readonly TimeSpan StreamPollInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Server-streaming RPC. Initial behaviour: emit one
    /// <c>OpgaveChange{upserted}</c> per opgave currently in the watch window
    /// so the client gets a baseline; then poll the same window every
    /// <see cref="StreamPollInterval"/> and emit deltas.
    ///
    /// Delta detection is a JSON-hash diff over the proto Opgave's serialised
    /// form: any observable field change (comment, completion status, photo
    /// list, color, ...) flips the hash and produces a fresh
    /// <c>upserted</c> event. Tasks that disappear from the result set
    /// between polls produce <c>removed_id</c> events. The per-subscriber
    /// <c>seen</c> dictionary is bounded by the watch-window size — a few
    /// hundred entries in normal usage.
    ///
    /// Cancellation: <see cref="ServerCallContext.CancellationToken"/> is
    /// threaded through every <c>Task.Delay</c>, every DB query (via
    /// <c>GetTasksForWeek</c>'s underlying EF queries — they don't expose a
    /// CT, but the per-poll wait is bounded), and every
    /// <c>responseStream.WriteAsync</c>. Client disconnect / deadline exits
    /// the loop cleanly.
    ///
    /// Per-poll error isolation: a single DB hiccup logs and continues the
    /// loop instead of killing the stream. The two terminal exceptions are
    /// <see cref="OperationCanceledException"/> (cancellation token tripped)
    /// and any <see cref="RpcException"/> with a permission-denied / hard-
    /// state status — those propagate to the client.
    ///
    /// Concurrent subscribers: each call has its own state dictionary; the
    /// server holds no shared subscription registry. v2 with event-bus push
    /// will introduce one.
    /// </summary>
    public override async Task StreamOpgaveChanges(
        StreamOpgaveChangesRequest request,
        IServerStreamWriter<OpgaveChange> responseStream,
        ServerCallContext context)
    {
        var propertyId = ParsePropertyId(request.EjendomId);

        var sdkSiteId = await siteResolver.GetSdkSiteIdAsync().ConfigureAwait(false);
        if (!await userPropertyAccess.HasAccessAsync(sdkSiteId, propertyId)
                .ConfigureAwait(false))
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied,
                "Caller has no PropertyWorker access to the requested property."));
        }

        var boardFilter = TryParseBoardIds(request.TavleId);
        var ct = context.CancellationToken;

        // Watch window is recomputed on every poll so the day-roll-over case
        // (a kiosk left subscribed across midnight) gradually shifts its
        // window forward without dropping the subscription.
        // ComputeWatchWindow uses today-N..today+M relative to UTC.

        // seen: opgave_id (numeric) → state-hash. Tracks every Opgave we have
        // already emitted, so subsequent polls only re-emit on real changes.
        var seen = new Dictionary<int, string>();

        // 1. Initial snapshot.
        try
        {
            var (initialStart, initialEnd) = ComputeWatchWindow();
            var initial = await LoadOpgaverAsync(
                propertyId, boardFilter, initialStart, initialEnd).ConfigureAwait(false);
            foreach (var op in initial)
            {
                ct.ThrowIfCancellationRequested();
                await responseStream.WriteAsync(new OpgaveChange { Upserted = op }, ct)
                    .ConfigureAwait(false);
                if (int.TryParse(op.Id, NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out var opgaveId))
                {
                    seen[opgaveId] = ComputeStateHash(op);
                }
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // 2. Poll loop.
        // lastErrorType: tracks the exception class of the most recent
        // consecutive poll failure so we only emit a full stack trace on
        // the first occurrence (or whenever the failure class changes).
        // Reset to null on every successful poll.
        Type? lastErrorType = null;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(StreamPollInterval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                var (windowStart, windowEnd) = ComputeWatchWindow();
                var current = await LoadOpgaverAsync(
                    propertyId, boardFilter, windowStart, windowEnd).ConfigureAwait(false);

                var currentIds = new HashSet<int>();

                foreach (var op in current)
                {
                    if (!int.TryParse(op.Id, NumberStyles.Integer, CultureInfo.InvariantCulture,
                            out var opgaveId))
                    {
                        continue;
                    }
                    currentIds.Add(opgaveId);

                    var hash = ComputeStateHash(op);
                    if (!seen.TryGetValue(opgaveId, out var prevHash) || prevHash != hash)
                    {
                        ct.ThrowIfCancellationRequested();
                        await responseStream.WriteAsync(new OpgaveChange { Upserted = op }, ct)
                            .ConfigureAwait(false);
                        seen[opgaveId] = hash;
                    }
                }

                // Detect removals: anything in `seen` but not in `currentIds`.
                var removed = seen.Keys.Where(id => !currentIds.Contains(id)).ToList();
                foreach (var id in removed)
                {
                    ct.ThrowIfCancellationRequested();
                    await responseStream.WriteAsync(new OpgaveChange
                        {
                            RemovedId = id.ToString(CultureInfo.InvariantCulture)
                        }, ct).ConfigureAwait(false);
                    seen.Remove(id);
                }

                // Successful poll — clear the consecutive-error tracker so the
                // next failure class (if any) gets a fresh full stack trace.
                lastErrorType = null;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // Per-poll failure (DB hiccup, transient I/O) — keep the
                // stream alive; clients can decide to reconnect on
                // noticeable staleness. To avoid log-spam during sustained
                // outages (12 stack traces per minute per subscriber), we
                // only emit a full stack trace on the first occurrence per
                // consecutive-error streak, and on every change of error
                // class. Subsequent identical failures get a single-line
                // warning with type + message.
                if (lastErrorType != ex.GetType())
                {
                    logger.LogError(ex,
                        "OpgaverGrpcService.StreamOpgaveChanges poll failed for sdkSiteId={SdkSiteId} property={PropertyId}; suppressing further full stack traces until error class changes",
                        sdkSiteId, propertyId);
                    lastErrorType = ex.GetType();
                }
                else
                {
                    logger.LogWarning(
                        "OpgaverGrpcService.StreamOpgaveChanges poll failed (repeating): {ExType}: {ExMessage}",
                        ex.GetType().Name, ex.Message);
                }
            }
        }
    }

    /// <summary>
    /// Today (UTC) - <see cref="StreamWatchPastDays"/> ..
    /// today + <see cref="StreamWatchFutureDays"/>. Recomputed every poll
    /// so the window shifts naturally across midnight rolls.
    /// </summary>
    private static (DateTime start, DateTime end) ComputeWatchWindow()
    {
        var todayUtc = DateTime.UtcNow.Date;
        return (todayUtc.AddDays(-StreamWatchPastDays),
                todayUtc.AddDays(StreamWatchFutureDays));
    }

    /// <summary>
    /// Loads the current Opgave set within the given window through the
    /// existing calendar service path. Reuses the
    /// <see cref="LoadEnvelopeByTaskIdAsync"/> helper from
    /// <see cref="ListOpgaver"/> so streamed Opgave messages carry the same
    /// comment + attachments shape as a one-shot list.
    ///
    /// Despite its name, <c>GetTasksForWeek</c> accepts arbitrary
    /// <c>WeekStart</c>/<c>WeekEnd</c> date strings (see
    /// <c>BackendConfigurationCalendarService.cs:40-43</c>) — the date range
    /// is forwarded to the EF compliance + occurrence queries verbatim, so a
    /// month-wide window is supported in a single call.
    /// </summary>
    private async Task<List<Opgave>> LoadOpgaverAsync(
        int propertyId,
        List<int> boardFilter,
        DateTime windowStart,
        DateTime windowEnd)
    {
        var model = new CalendarTaskRequestModel
        {
            PropertyId = propertyId,
            WeekStart = windowStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            WeekEnd = windowEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            BoardIds = boardFilter,
            TagNames = [],
            SiteIds = []
        };

        var result = await calendarService.GetTasksForWeek(model).ConfigureAwait(false);
        var output = new List<Opgave>();

        if (!result.Success || result.Model == null)
        {
            return output;
        }

        var envelopeByTaskId = await LoadEnvelopeByTaskIdAsync(result.Model)
            .ConfigureAwait(false);

        var fieldsByTaskId = await LoadFieldsByTaskIdAsync(result.Model)
            .ConfigureAwait(false);

        foreach (var task in result.Model)
        {
            envelopeByTaskId.TryGetValue(task.Id, out var envelope);
            var comment = envelope?.OpgaverComment?.Text ?? string.Empty;

            var opgave = new Opgave
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
                Comment = comment,
                EformId = task.EformId ?? 0
            };

            PopulateAttachments(opgave, envelope);
            if (fieldsByTaskId.TryGetValue(task.Id, out var fields))
            {
                opgave.Fields.AddRange(fields);
            }
            output.Add(opgave);
        }

        return output;
    }

    /// <summary>
    /// Stable SHA256 hash over the canonical protobuf wire-format bytes of
    /// the Opgave. <c>ToByteArray</c> emits a deterministic byte sequence
    /// for all current scalar/message field types, including future-added
    /// fields, so this is robust against schema evolution. (JSON-based
    /// hashing was sufficient for the present Opgave shape but would
    /// silently break the moment a <c>map&lt;...&gt;</c> field is added,
    /// since proto map-field serialisation is not order-stable.) We hash
    /// the bytes (instead of storing them directly) to keep the per-entry
    /// memory footprint small (~44 base64 chars) — the dictionary may hold
    /// a few hundred entries per subscriber.
    /// </summary>
    private static string ComputeStateHash(Opgave op)
    {
        var bytes = SHA256.HashData(op.ToByteArray());
        return Convert.ToBase64String(bytes);
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
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed || x.WorkflowState == null)
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
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed || x.WorkflowState == null)
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

            // Mirror the post-update sequence from
            // BackendConfigurationCompliancesService.Update (lines 307-335):
            // set Status=100 on PlanningCaseSite + parent PlanningCase so the
            // admin "filled cases" view (queries PlanningCases WHERE Status=100
            // AND MicrotingSdkCaseDoneAt >= fromDate) picks up device completions.
            var siteName = (await sdkDbContext.Sites
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed || x.WorkflowState == null)
                .FirstOrDefaultAsync(x => x.Id == sdkSiteId)
                .ConfigureAwait(false))?.Name ?? string.Empty;

            var planningCaseSite = await itemsPlanningPnDbContext.PlanningCaseSites
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed || x.WorkflowState == null)
                .FirstOrDefaultAsync(x =>
                    x.MicrotingSdkCaseId == foundCase.Id)
                .ConfigureAwait(false);

            if (planningCaseSite != null)
            {
                planningCaseSite.Status = 100;
                planningCaseSite.MicrotingSdkCaseId = foundCase.Id;
                planningCaseSite.MicrotingSdkCaseDoneAt = foundCase.DoneAt;
                planningCaseSite.DoneByUserId = (int)sdkSiteId;
                planningCaseSite.DoneByUserName = siteName;
                await planningCaseSite.Update(itemsPlanningPnDbContext).ConfigureAwait(false);

                var planningCase = await itemsPlanningPnDbContext.PlanningCases
                    .SingleAsync(x => x.Id == planningCaseSite.PlanningCaseId)
                    .ConfigureAwait(false);

                if (planningCase.Status != 100)
                {
                    planningCase.Status = 100;
                    planningCase.MicrotingSdkCaseDoneAt = foundCase.DoneAt;
                    planningCase.MicrotingSdkCaseId = foundCase.Id;
                    planningCase.DoneByUserId = (int)sdkSiteId;
                    planningCase.DoneByUserName = siteName;
                    planningCase.WorkflowState = Constants.WorkflowStates.Processed;
                    await planningCase.Update(itemsPlanningPnDbContext).ConfigureAwait(false);
                }

                planningCaseSite.PlanningCaseId = planningCase.Id;
                await planningCaseSite.Update(itemsPlanningPnDbContext).ConfigureAwait(false);
            }
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
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed || x.WorkflowState == null)
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

        // Write — preserve any existing photo metadata in the envelope so a
        // SetComment call doesn't accidentally drop attachments. An empty
        // comment with no photos collapses the envelope back to "" so the
        // legacy CompliancesGrpcService.ReadComplianceCase passthrough sees
        // an empty string instead of "{...}".
        var existingEnvelope = TryParseEnvelope(foundCase.Custom);
        var nextEnvelope = existingEnvelope ?? new OpgaverCustomEnvelope();
        nextEnvelope.OpgaverComment = string.IsNullOrEmpty(trimmed)
            ? null
            : new OpgaverCommentBody
            {
                Text = trimmed,
                TsUnix = ToUnixSeconds(commentAtUtc)
            };
        foundCase.Custom = SerializeEnvelopeOrEmpty(nextEnvelope);
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

    /// <summary>
    /// Maximum bytes accepted per photo. Mirrors a typical phone-camera
    /// JPEG cap; bigger uploads cause the stream to be aborted with
    /// <c>InvalidArgument</c> mid-flight (we don't know the size up front
    /// because the client is streaming).
    /// </summary>
    private const int MaxPhotoBytes = 20 * 1024 * 1024;

    /// <summary>
    /// Maximum number of photo slots per opgave. Slots are addressed
    /// 0..MaxPhotoSlots-1. The bound is enforced both by the proto's
    /// <c>slot</c> validation in UploadPhoto and by the clients' UI.
    /// </summary>
    private const int MaxPhotoSlots = 10;

    /// <summary>
    /// Receives an uploaded photo as a stream of <c>UploadPhotoChunk</c>
    /// messages. Wire protocol: the very first chunk MUST be the
    /// <c>meta</c> oneof variant, all subsequent chunks MUST be <c>chunk</c>
    /// (raw bytes). Anything else is rejected with <c>InvalidArgument</c>.
    ///
    /// Storage: the assembled bytes go to S3 via
    /// <c>core.PutFileToS3Storage(stream, fileName)</c> — the same path
    /// <c>BackendConfigurationTaskManagementService.CreateTask</c> uses for
    /// work-order task images. A row is created in the SDK
    /// <c>uploaded_data</c> table to track checksum / extension / filename;
    /// no resizing or thumbnail variants are produced (the existing
    /// TaskManagement code creates 300px / 700px variants via ImageMagick;
    /// for the Opgaver flow the mobile clients work with the original
    /// image, so we keep this v1 storage path simple). Filename mirrors
    /// the TaskManagement convention: <c>{uploaded_data_id}_{checksum}{ext}</c>.
    ///
    /// Slot tracking: the (slot, uploaded_data_id, ts_unix, content_type)
    /// tuple is appended to the <c>opgaver_photos</c> array in the
    /// <c>Case.Custom</c> JSON envelope (the same envelope SetComment uses
    /// for <c>opgaver_comment</c>). Re-uploading to a slot that already
    /// contains a photo soft-deletes the previous <c>UploadedData</c> row
    /// and replaces the entry, so the slot acts as a stable per-opgave
    /// identifier.
    ///
    /// Validation: <c>content_type</c> must be image/jpeg or image/png,
    /// <c>slot</c> must be in 0..<see cref="MaxPhotoSlots"/>-1, total bytes
    /// capped at <see cref="MaxPhotoBytes"/>. Empty payloads are rejected
    /// to prevent zero-byte files from polluting storage.
    ///
    /// Recurrence-only opgaver (no compliance row, no SDK Case) cannot
    /// store photos — there is no Case.Custom slot to write to. Returns
    /// <c>FailedPrecondition</c> in that branch (vs. SetComment's "echo
    /// back the synthesised opgave" approach: a comment is metadata the
    /// client can replay later, but uploaded bytes have nowhere to go and
    /// silently discarding them would lose user content).
    /// </summary>
    public override async Task<UploadPhotoResponse> UploadPhoto(
        IAsyncStreamReader<UploadPhotoChunk> requestStream,
        ServerCallContext context)
    {
        // 1. Read first chunk — must be `meta`.
        if (!await requestStream.MoveNext(context.CancellationToken).ConfigureAwait(false))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                "UploadPhoto stream is empty — at least a meta chunk is required."));
        }

        var firstChunk = requestStream.Current;
        if (firstChunk.KindCase != UploadPhotoChunk.KindOneofCase.Meta)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                "First UploadPhotoChunk must carry the meta oneof variant."));
        }

        var meta = firstChunk.Meta;

        // 2. Validate metadata up front so we don't waste the upload.
        var opgaveId = ParseOpgaveId(meta.OpgaveId);
        if (meta.Slot < 0 || meta.Slot >= MaxPhotoSlots)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                $"slot must be in 0..{MaxPhotoSlots - 1}."));
        }

        var contentType = meta.ContentType?.Trim() ?? string.Empty;
        var extension = contentType switch
        {
            "image/jpeg" or "image/jpg" => ".jpg",
            "image/png" => ".png",
            _ => throw new RpcException(new Status(StatusCode.InvalidArgument,
                "content_type must be image/jpeg, image/jpg, or image/png."))
        };

        // 3. Auth + property access. Mirrors CompleteOpgave / SetComment.
        var arp = await dbContext.AreaRulePlannings
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed || x.WorkflowState == null)
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

        // SetComment includes Removed compliance rows so post-completion
        // edits are possible; do the same here so a worker can attach a
        // photo to a just-completed opgave.
        var compliance = await dbContext.Compliances
            .Where(x => x.PlanningId == arp.ItemPlanningId)
            .OrderBy(x => x.Deadline)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);

        if (compliance == null)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition,
                $"Opgave {opgaveId}: no SDK case to attach a photo to (recurrence-only opgaver are not supported in v1)."));
        }

        var core = await coreHelper.GetCore().ConfigureAwait(false);
        var sdkDbContext = core.DbContextHelper.GetDbContext();

        var foundCase = await sdkDbContext.Cases
            .FirstOrDefaultAsync(x => x.Id == compliance.MicrotingSdkCaseId)
            .ConfigureAwait(false);

        if (foundCase == null)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition,
                $"Opgave {opgaveId}: compliance {compliance.Id} references missing SDK case {compliance.MicrotingSdkCaseId}."));
        }

        // 4. Reassemble bytes from subsequent chunks. We allocate to
        // MemoryStream rather than a File or pooled buffer; with 20MB cap
        // and image-upload latency, this is well under typical request
        // memory budgets.
        var ms = new MemoryStream();
        try
        {
            while (await requestStream.MoveNext(context.CancellationToken).ConfigureAwait(false))
            {
                var chunk = requestStream.Current;
                if (chunk.KindCase != UploadPhotoChunk.KindOneofCase.Chunk)
                {
                    throw new RpcException(new Status(StatusCode.InvalidArgument,
                        "Only the first UploadPhotoChunk may carry meta; all subsequent chunks must be `chunk` bytes."));
                }

                if (chunk.Chunk.Length == 0)
                {
                    continue;
                }

                if (ms.Length + chunk.Chunk.Length > MaxPhotoBytes)
                {
                    throw new RpcException(new Status(StatusCode.InvalidArgument,
                        $"Photo exceeds {MaxPhotoBytes / (1024 * 1024)} MB limit."));
                }

                chunk.Chunk.WriteTo(ms);
            }

            if (ms.Length == 0)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument,
                    "UploadPhoto stream contained no image bytes."));
            }

            ms.Position = 0;

            // 5. Compute checksum + create UploadedData row + write to S3.
            // Order matches TaskManagementService.CreateTask: create
            // UploadedData first to get its Id, then build the filename,
            // then upload. The Update() at the end commits the FileName
            // back so reads can locate the bytes.
            string checksum;
            using (var md5 = MD5.Create())
            {
                checksum = BitConverter.ToString(await md5.ComputeHashAsync(ms).ConfigureAwait(false))
                    .Replace("-", string.Empty)
                    .ToLowerInvariant();
            }
            ms.Position = 0;

            var uploadedData = new SdkUploadedData
            {
                Checksum = checksum,
                FileName = string.Empty,
                FileLocation = string.Empty,
                Extension = extension
            };
            await uploadedData.Create(sdkDbContext).ConfigureAwait(false);

            var fileName = $"{uploadedData.Id}_{checksum}{extension}";
            uploadedData.FileName = fileName;
            await uploadedData.Update(sdkDbContext).ConfigureAwait(false);

            await core.PutFileToS3Storage(ms, fileName).ConfigureAwait(false);

            // 6. Update Case.Custom envelope: replace existing entry at
            // slot if present (soft-deleting its UploadedData row), then
            // append the new tuple.
            var commentAtUtc = meta.ClientTsUnix > 0
                ? DateTimeOffset.FromUnixTimeSeconds(meta.ClientTsUnix).UtcDateTime
                : DateTime.UtcNow;

            var envelope = TryParseEnvelope(foundCase.Custom) ?? new OpgaverCustomEnvelope();
            envelope.OpgaverPhotos ??= new List<OpgaverPhotoBody>();

            var existing = envelope.OpgaverPhotos.FirstOrDefault(p => p.Slot == meta.Slot);
            if (existing != null)
            {
                if (existing.UploadedDataId > 0)
                {
                    var stale = await sdkDbContext.UploadedDatas
                        .FirstOrDefaultAsync(u => u.Id == existing.UploadedDataId)
                        .ConfigureAwait(false);
                    if (stale != null)
                    {
                        await stale.Delete(sdkDbContext).ConfigureAwait(false);
                    }
                }
                envelope.OpgaverPhotos.Remove(existing);
            }

            envelope.OpgaverPhotos.Add(new OpgaverPhotoBody
            {
                Slot = meta.Slot,
                UploadedDataId = uploadedData.Id,
                TsUnix = ToUnixSeconds(commentAtUtc),
                ContentType = contentType
            });

            foundCase.Custom = SerializeEnvelopeOrEmpty(envelope);
            await foundCase.Update(sdkDbContext).ConfigureAwait(false);

            // 7. Echo the new UploadedData id as the storage_id so the
            // client can address subsequent reads / removes.
            return new UploadPhotoResponse
            {
                StorageId = uploadedData.Id.ToString(CultureInfo.InvariantCulture)
            };
        }
        finally
        {
            await ms.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Soft-deletes the photo at the requested slot for an opgave.
    /// "Soft" = the SDK <c>UploadedData</c> row is marked
    /// <c>WorkflowState=Removed</c> via <c>UploadedData.Delete()</c>; the
    /// S3 object is intentionally left in place because the eFormCore SDK
    /// has no public delete-from-S3 helper, and orphaned objects are
    /// already produced elsewhere in the pipeline (e.g.
    /// <c>BackendConfigurationFilesService</c> only soft-deletes the row).
    /// The slot entry is removed from the envelope so subsequent
    /// ListOpgaver reads won't surface it.
    ///
    /// Idempotent: removing a slot that doesn't currently hold a photo
    /// returns OK with no error — the client may retry after a partial
    /// failure without needing to know what state the server holds.
    /// </summary>
    public override async Task<RemovePhotoResponse> RemovePhoto(
        RemovePhotoRequest request,
        ServerCallContext context)
    {
        var opgaveId = ParseOpgaveId(request.OpgaveId);
        if (request.Slot < 0 || request.Slot >= MaxPhotoSlots)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                $"slot must be in 0..{MaxPhotoSlots - 1}."));
        }

        var arp = await dbContext.AreaRulePlannings
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed || x.WorkflowState == null)
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

        var compliance = await dbContext.Compliances
            .Where(x => x.PlanningId == arp.ItemPlanningId)
            .OrderBy(x => x.Deadline)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);

        if (compliance == null)
        {
            // No Case → nothing to remove. Treat as success (idempotent).
            return new RemovePhotoResponse();
        }

        var core = await coreHelper.GetCore().ConfigureAwait(false);
        var sdkDbContext = core.DbContextHelper.GetDbContext();

        var foundCase = await sdkDbContext.Cases
            .FirstOrDefaultAsync(x => x.Id == compliance.MicrotingSdkCaseId)
            .ConfigureAwait(false);

        if (foundCase == null)
        {
            // SDK Case missing → no envelope to clean up. Idempotent.
            return new RemovePhotoResponse();
        }

        var envelope = TryParseEnvelope(foundCase.Custom);
        if (envelope?.OpgaverPhotos == null || envelope.OpgaverPhotos.Count == 0)
        {
            return new RemovePhotoResponse();
        }

        var photo = envelope.OpgaverPhotos.FirstOrDefault(p => p.Slot == request.Slot);
        if (photo == null)
        {
            return new RemovePhotoResponse();
        }

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

        envelope.OpgaverPhotos.Remove(photo);
        foundCase.Custom = SerializeEnvelopeOrEmpty(envelope);
        await foundCase.Update(sdkDbContext).ConfigureAwait(false);

        return new RemovePhotoResponse();
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

    /// <summary>
    /// Serialises the envelope, returning the empty string when nothing
    /// substantive remains (no comment, no photos). The empty-string
    /// collapse is important so that the legacy
    /// <c>CompliancesGrpcService.ReadComplianceCase</c> passthrough — which
    /// echoes <c>Case.Custom</c> as a free-form string — sees a clean empty
    /// value instead of <c>{}</c> after the worker clears the comment and
    /// removes the last photo.
    /// </summary>
    private static string SerializeEnvelopeOrEmpty(OpgaverCustomEnvelope envelope)
    {
        var hasComment = envelope.OpgaverComment != null
            && !string.IsNullOrEmpty(envelope.OpgaverComment.Text);
        var hasPhotos = envelope.OpgaverPhotos is { Count: > 0 };
        if (!hasComment && !hasPhotos)
        {
            return string.Empty;
        }
        return JsonSerializer.Serialize(envelope);
    }

    private static long ToUnixSeconds(DateTime utc)
    {
        return new DateTimeOffset(utc, TimeSpan.Zero).ToUnixTimeSeconds();
    }

    private sealed class OpgaverCustomEnvelope
    {
        // Nullable: SetComment with empty text clears the comment slot; a
        // null entry survives serialisation because of the null-handling
        // option below in the writer (System.Text.Json defaults omit nulls
        // only when explicitly opted in, but we accept the trailing null
        // here since the absence on read is what matters).
        [JsonPropertyName("opgaver_comment")]
        public OpgaverCommentBody? OpgaverComment { get; set; }

        [JsonPropertyName("opgaver_photos")]
        public List<OpgaverPhotoBody>? OpgaverPhotos { get; set; }
    }

    private sealed class OpgaverCommentBody
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;

        [JsonPropertyName("ts_unix")]
        public long TsUnix { get; set; }
    }

    private sealed class OpgaverPhotoBody
    {
        [JsonPropertyName("slot")]
        public int Slot { get; set; }

        [JsonPropertyName("uploaded_data_id")]
        public int UploadedDataId { get; set; }

        [JsonPropertyName("ts_unix")]
        public long TsUnix { get; set; }

        [JsonPropertyName("content_type")]
        public string ContentType { get; set; } = string.Empty;
    }

    /// <summary>
    /// Updates a single eForm field value on the backing SDK Case and returns
    /// the refreshed Opgave so the Flutter client can reconcile in one round
    /// trip.
    ///
    /// Field-value wire format: the SDK <c>core.CaseUpdate</c> method accepts a
    /// list of <c>"fieldId|value"</c> strings (same convention used by
    /// <c>CaseUpdateHelper.GetFieldList</c> in every existing update path). We
    /// construct a single-element list from the caller-supplied
    /// <c>field_id</c> and <c>value</c>, which lets the SDK validate field
    /// ownership and persist the answer atomically.
    ///
    /// After <c>CaseUpdate</c>, we call <c>CaseUpdateFieldValues</c> to sync
    /// the SDK's FieldValues table — same two-call pattern as
    /// <c>BackendConfigurationCaseService.Update</c> and
    /// <c>CompliancesGrpcService.UpdateComplianceCase</c>.
    ///
    /// The response Opgave is assembled by re-reading the calendar task and
    /// reloading fields via <c>LoadFieldsByTaskIdAsync</c>, identical to
    /// the pattern used in <c>SetComment</c>. An envelope read is included so
    /// any comment / photos already written remain visible in the response.
    ///
    /// Authorization mirrors <c>SetComment</c>: caller must have a
    /// <c>PropertyWorker</c> relationship to the task's property. The compliance
    /// must exist (and may be removed/completed — same rationale as SetComment:
    /// a worker might want to fill in a field after marking the task done).
    /// </summary>
    public override async Task<SetFieldValueResponse> SetFieldValue(
        SetFieldValueRequest request,
        ServerCallContext context)
    {
        if (request.FieldId <= 0)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                "field_id must be a positive integer."));
        }

        var opgaveId = ParseOpgaveId(request.OpgaveId);

        var arp = await dbContext.AreaRulePlannings
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed || x.WorkflowState == null)
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

        // Accept the compliance row regardless of WorkflowState — same rationale
        // as SetComment: a worker can write field values on a just-completed task.
        var compliance = await dbContext.Compliances
            .Where(x => x.PlanningId == arp.ItemPlanningId)
            .OrderBy(x => x.Deadline)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);

        if (compliance == null)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition,
                $"Opgave {opgaveId} has no backing Case — field values cannot be persisted yet."));
        }

        var caseId = compliance.MicrotingSdkCaseId;

        var core = await coreHelper.GetCore().ConfigureAwait(false);
        var sdkDbContext = core.DbContextHelper.GetDbContext();
        var language = await sdkDbContext.Languages.FirstAsync().ConfigureAwait(false);

        // Resolve the FieldValue row PK from the (caseId, eFormFieldId) pair.
        // Core.CaseUpdate's wire format is "[fieldValueId]|[value]" where fieldValueId
        // is the FieldValues table PK, not the eForm template field.Id. Without this
        // lookup the SDK silently fails (or updates a random unrelated FieldValue row
        // that happens to have Id == request.FieldId).
        var fieldValueRowId = await sdkDbContext.FieldValues
            .Where(fv => fv.CaseId == caseId && fv.FieldId == request.FieldId)
            .Select(fv => fv.Id)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);

        if (fieldValueRowId == 0)
        {
            throw new RpcException(new Status(StatusCode.NotFound,
                $"No FieldValue row exists for case {caseId} field {request.FieldId}."));
        }

        var fieldValueList = new List<string>
        {
            $"{fieldValueRowId}|{request.Value ?? string.Empty}"
        };

        try
        {
            await core.CaseUpdate(caseId, fieldValueList, []).ConfigureAwait(false);
            await core.CaseUpdateFieldValues(caseId, language).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "OpgaverGrpcService.SetFieldValue: CaseUpdate failed for opgave {OpgaveId} field {FieldId}",
                opgaveId, request.FieldId);
            throw new RpcException(new Status(StatusCode.Internal,
                $"Field value update failed: {ex.Message}"));
        }

        // Reload the opgave for the response — preserve comment + photos from
        // the envelope exactly as SetComment does.
        var dayKey = (compliance.Deadline != default ? compliance.Deadline : DateTime.UtcNow)
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

        Opgave opgave;
        if (refreshedTask != null)
        {
            opgave = new Opgave
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
                Comment = string.Empty,
                EformId = refreshedTask.EformId ?? 0
            };

            // Reload envelope for comment + photos.
            var envelopeByTaskId = await LoadEnvelopeByTaskIdAsync(refreshed.Model!).ConfigureAwait(false);
            envelopeByTaskId.TryGetValue(refreshedTask.Id, out var envelope);
            opgave.Comment = envelope?.OpgaverComment?.Text ?? string.Empty;
            PopulateAttachments(opgave, envelope);

            // Reload fields — CaseUpdateFieldValues has committed by now, so
            // this read returns the just-written value.
            var fieldsByTaskId = await LoadFieldsByTaskIdAsync(refreshed.Model!).ConfigureAwait(false);
            if (fieldsByTaskId.TryGetValue(refreshedTask.Id, out var fields))
            {
                opgave.Fields.AddRange(fields);
            }
        }
        else
        {
            // Task fell out of the window after update — synthesise minimal Opgave.
            opgave = new Opgave
            {
                Id = opgaveId.ToString(CultureInfo.InvariantCulture),
                EjendomId = arp.PropertyId.ToString(CultureInfo.InvariantCulture),
                TavleId = string.Empty,
                PlanDayKey = dayKey,
                PlannedAt = string.Empty,
                TaskText = string.Empty,
                CalendarColor = string.Empty,
                Completed = false,
                CompletedBy = string.Empty,
                DescriptionHtml = string.Empty,
                Comment = string.Empty
            };
        }

        return new SetFieldValueResponse { Opgave = opgave };
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
