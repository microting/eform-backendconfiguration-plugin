using System;
using System.Globalization;
using System.Linq;
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
/// completion inline (mirroring CompliancesGrpcService.UpdateComplianceCase)
/// because the JSON-side ToggleComplete is currently a TODO and the Opgaver
/// flow has no form data — so calling core.CaseUpdate with empty field/checklist
/// lists would be a no-op anyway. Remaining write RPCs (SetComment,
/// UploadPhoto, RemovePhoto, StreamOpgaveChanges) are intentionally not
/// overridden — the generated base returns UNIMPLEMENTED, which is the correct
/// v1 behaviour. Follow-up PRs in the stack will fill them in.
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

        foreach (var task in result.Model)
        {
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
                Comment = string.Empty
                // updated_at: Timestamp default (zero) — no source field in CalendarTaskResponseModel.
                // attachments: empty — populated in a later PR via the Documents/attachments flow.
            });
        }

        return response;
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
