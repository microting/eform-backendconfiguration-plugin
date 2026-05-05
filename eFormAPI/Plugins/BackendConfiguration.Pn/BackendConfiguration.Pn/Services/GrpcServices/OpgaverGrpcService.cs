using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Grpc.Opgaver;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.BackendConfigurationPropertiesService;
using BackendConfiguration.Pn.Services.UserPropertyAccess;
using Grpc.Core;

namespace BackendConfiguration.Pn.Services.GrpcServices;

/// <summary>
/// gRPC adapter for the mobile "Opgaver" feature.
/// Read-only RPCs (ListEjendomme / ListTavler / ListOpgaver) reuse the existing
/// Properties + Calendar service paths and reshape the result into the
/// microting.opgaver wire contract. Write RPCs (CompleteOpgave, SetComment,
/// UploadPhoto, RemovePhoto, StreamOpgaveChanges) are intentionally not
/// overridden — the generated base returns UNIMPLEMENTED, which is the correct
/// v1 behaviour. Follow-up PRs in the stack will fill them in.
/// </summary>
public class OpgaverGrpcService(
    IBackendConfigurationCalendarService calendarService,
    IBackendConfigurationPropertiesService propertiesService,
    IBackendConfigurationUserPropertyAccess userPropertyAccess,
    IGrpcSiteResolver siteResolver)
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
