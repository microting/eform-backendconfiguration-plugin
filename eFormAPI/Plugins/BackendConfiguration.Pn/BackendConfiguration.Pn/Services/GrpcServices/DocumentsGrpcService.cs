using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Grpc.Documents;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.UserPropertyAccess;
using Grpc.Core;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.eForm.Infrastructure.Constants;

namespace BackendConfiguration.Pn.Services.GrpcServices;

public class DocumentsGrpcService(
    IBackendConfigurationCalendarService calendarService,
    IBackendConfigurationUserPropertyAccess userPropertyAccess,
    IGrpcSiteResolver siteResolver,
    BackendConfigurationPnDbContext dbContext)
    : Documents.DocumentsBase
{
    public override async Task<GetAttachmentResponse> GetAttachment(
        GetAttachmentRequest request,
        ServerCallContext context)
    {
        return request.Source switch
        {
            AttachmentSource.CalendarFile => await GetAttachmentCalendarFile(request),
            _ => throw new RpcException(new Status(StatusCode.InvalidArgument,
                $"Unsupported attachment source: {request.Source}"))
        };
    }

    private async Task<GetAttachmentResponse> GetAttachmentCalendarFile(
        GetAttachmentRequest request)
    {
        // Parse EventId as int — this is the AreaRulePlanning.Id per proto contract
        if (!int.TryParse(request.EventId, out var planningId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                "EventId must be a valid integer for CALENDAR_FILE attachments"));
        }

        // Get SDK site ID for permission check (same pattern as CalendarGrpcService)
        var sdkSiteId = await siteResolver.GetSdkSiteIdAsync();

        // Permission check: verify user has access to the planning's property
        // (mirrors CalendarGrpcService.GetTasksForWeek permission pattern)
        var planning = await dbContext.AreaRulePlannings
            .Where(x => x.Id == planningId)
            .Select(x => new { x.PropertyId })
            .FirstOrDefaultAsync();

        if (planning == null)
        {
            throw new RpcException(new Status(StatusCode.NotFound,
                "planning not found"));
        }

        if (!await userPropertyAccess.HasAccessAsync(sdkSiteId, planning.PropertyId))
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied,
                "Caller has no PropertyWorker access to this attachment's property."));
        }

        // Get the index-th AreaRulePlanningFile for this planning
        // (soft-deleted are filtered, ordered by Id)
        var files = await dbContext.AreaRulePlanningFiles
            .Where(x => x.AreaRulePlanningId == planningId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .OrderBy(x => x.Id)
            .ToListAsync();

        if (request.Index < 0 || request.Index >= files.Count)
        {
            throw new RpcException(new Status(StatusCode.NotFound,
                "attachment index out of range"));
        }

        var planningFile = files[request.Index];

        // Delegate to BackendConfigurationCalendarService.DownloadFile to get bytes
        var download = await calendarService.DownloadFile(planningId, planningFile.Id);
        if (download == null)
        {
            throw new RpcException(new Status(StatusCode.NotFound,
                "Failed to download attachment"));
        }

        // Read the stream into bytes for gRPC response
        using var ms = new MemoryStream();
        await download.Content.CopyToAsync(ms);
        var bytes = ms.ToArray();

        return new GetAttachmentResponse
        {
            ContentType = download.MimeType,
            Filename = download.FileName,
            Data = ByteString.CopyFrom(bytes)
        };
    }
}
