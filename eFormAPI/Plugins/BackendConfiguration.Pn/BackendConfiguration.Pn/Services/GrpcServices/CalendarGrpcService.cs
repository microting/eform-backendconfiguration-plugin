using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Grpc;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.UserPropertyAccess;
using Grpc.Core;

namespace BackendConfiguration.Pn.Services.GrpcServices;

public class CalendarGrpcService(
    IBackendConfigurationCalendarService calendarService,
    IBackendConfigurationUserPropertyAccess userPropertyAccess,
    IGrpcSiteResolver siteResolver)
    : BackendConfigurationCalendarGrpc.BackendConfigurationCalendarGrpcBase
{
    private const string IsoDateTime = "yyyy-MM-ddTHH:mm:ss.fffZ";

    public override async Task<GetTasksForWeekResponse> GetTasksForWeek(
        GetTasksForWeekRequest request,
        ServerCallContext context)
    {
        var sdkSiteId = await siteResolver.GetSdkSiteIdAsync();
        if (!await userPropertyAccess.HasAccessAsync(sdkSiteId, request.PropertyId))
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied,
                "Caller has no PropertyWorker access to the requested property."));
        }

        var model = new CalendarTaskRequestModel
        {
            PropertyId = request.PropertyId,
            WeekStart = request.WeekStart,
            WeekEnd = request.WeekEnd,
            BoardIds = request.BoardIds.ToList(),
            TagNames = request.TagNames.ToList(),
            SiteIds = request.SiteIds.ToList()
        };

        var result = await calendarService.GetTasksForWeek(model);

        var response = new GetTasksForWeekResponse
        {
            Success = result.Success,
            Message = result.Message ?? string.Empty
        };

        if (!result.Success || result.Model == null)
        {
            return response;
        }

        foreach (var task in result.Model)
        {
            response.Tasks.Add(new CalendarTaskItem
            {
                Id = task.Id,
                Title = task.Title ?? string.Empty,
                StartHour = task.StartHour,
                Duration = task.Duration,
                TaskDate = task.TaskDate ?? string.Empty,
                Tags = { task.Tags ?? [] },
                AssigneeIds = { task.AssigneeIds ?? [] },
                BoardId = task.BoardId ?? 0,
                Color = task.Color ?? string.Empty,
                RepeatType = task.RepeatType,
                RepeatEvery = task.RepeatEvery,
                Completed = task.Completed,
                PropertyId = task.PropertyId,
                ComplianceId = task.ComplianceId ?? 0,
                IsFromCompliance = task.IsFromCompliance,
                Deadline = task.Deadline?.ToString(IsoDateTime, CultureInfo.InvariantCulture) ?? string.Empty,
                NextExecutionTime = task.NextExecutionTime?.ToString(IsoDateTime, CultureInfo.InvariantCulture) ?? string.Empty,
                PlanningId = task.PlanningId ?? 0,
                IsAllDay = task.IsAllDay,
                ExceptionId = task.ExceptionId ?? 0
            });
        }

        return response;
    }

    public override async Task<GetBoardsResponse> GetBoards(
        GetBoardsRequest request,
        ServerCallContext context)
    {
        var sdkSiteId = await siteResolver.GetSdkSiteIdAsync();
        if (!await userPropertyAccess.HasAccessAsync(sdkSiteId, request.PropertyId))
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied,
                "Caller has no PropertyWorker access to the requested property."));
        }

        var result = await calendarService.GetBoards(request.PropertyId);

        var response = new GetBoardsResponse
        {
            Success = result.Success,
            Message = result.Message ?? string.Empty
        };

        if (!result.Success || result.Model == null)
        {
            return response;
        }

        foreach (var board in result.Model)
        {
            response.Boards.Add(new CalendarBoardItem
            {
                Id = board.Id,
                Name = board.Name ?? string.Empty,
                Color = board.Color ?? string.Empty,
                PropertyId = board.PropertyId
            });
        }

        return response;
    }
}
