using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Grpc.Adhoc;
using BackendConfiguration.Pn.Infrastructure.Models.Adhoc;
using BackendConfiguration.Pn.Services.BackendConfigurationAdhocService;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace BackendConfiguration.Pn.Services.GrpcServices;

/// <summary>
/// gRPC adapter for the mobile "Adhoc task" feature. Every RPC resolves the
/// caller's SDK site id via <see cref="IGrpcSiteResolver"/> (mirroring
/// <c>EventsGrpcService</c>'s convention: <c>RpcException(Unauthenticated)</c>
/// when the resolver returns 0) and forwards to
/// <see cref="IBackendConfigurationAdhocService"/> — the shared task CRUD +
/// visibility service also consumed by <c>AdhocController</c> (B6, dashboard
/// REST). Mobile callers always pass <c>isAdmin=false</c> (the service's
/// default) — visibility here is entirely governed by that service's
/// TaskVisibility-mirroring predicates.
///
/// Typed service exceptions map to RPC status codes:
/// <see cref="AdhocTaskNotFoundException"/>/<see cref="AdhocTagNotFoundException"/>/
/// <see cref="AdhocTaskPhotoNotFoundException"/> -&gt; <c>NotFound</c>;
/// <see cref="AdhocTaskUnauthorizedException"/> -&gt; <c>PermissionDenied</c>;
/// any other <see cref="ArgumentException"/> (bad input, e.g. an empty tag
/// name) -&gt; <c>InvalidArgument</c>.
///
/// <c>UploadPhoto</c>/<c>GetPhoto</c> follow the same meta-first streaming
/// shape as <c>EventsGrpcService.UploadPhoto</c>/a 64KB-chunked download, but
/// delegate the actual S3 round-trip to
/// <see cref="IBackendConfigurationAdhocService.SavePhoto"/>/<c>GetPhoto</c>
/// (backed by <c>IAdhocPhotoStorage</c>, B4) rather than touching the SDK
/// core directly.
/// </summary>
public class AdhocGrpcService(
    IBackendConfigurationAdhocService adhocService,
    IGrpcSiteResolver siteResolver,
    ILogger<AdhocGrpcService> logger)
    : AdhocGrpc.AdhocGrpcBase
{
    /// <summary>Mirrors EventsGrpcService.UploadPhoto's cap — no size hint on the wire, so we bound eagerly.</summary>
    private const int MaxPhotoBytes = 20 * 1024 * 1024;

    private const int PhotoChunkSize = 64 * 1024;

    public override async Task<CurrentWorker> GetCurrentWorker(
        GetCurrentWorkerRequest request,
        ServerCallContext context)
    {
        var workerId = await ResolveWorkerIdAsync().ConfigureAwait(false);

        // display_name has no home in IBackendConfigurationAdhocService (it's a
        // gRPC-facade-only concept — REST/dashboard callers have no single SDK
        // worker identity), so resolve it via the site resolver, mirroring
        // BackendConfigurationAdhocService.ListWorkers' own Site.Name lookup.
        var displayName = await siteResolver.GetDisplayNameAsync(workerId).ConfigureAwait(false);
        var properties = await adhocService.ListProperties(workerId).ConfigureAwait(false);

        var response = new CurrentWorker
        {
            WorkerId = workerId.ToString(CultureInfo.InvariantCulture),
            DisplayName = displayName
        };
        response.PropertyIds.AddRange(properties.Select(p => p.Id.ToString(CultureInfo.InvariantCulture)));
        return response;
    }

    public override async Task<ListTasksResponse> ListTasks(ListTasksRequest request, ServerCallContext context)
    {
        var workerId = await ResolveWorkerIdAsync().ConfigureAwait(false);
        var scope = MapScope(request.Scope);
        var propertyId = ParseOptionalId(request.PropertyId);

        var tasks = await adhocService.ListTasks(workerId, scope, propertyId).ConfigureAwait(false);

        var response = new ListTasksResponse();
        response.Tasks.AddRange(tasks.Select(MapTask));
        return response;
    }

    public override async Task<GetTaskResponse> GetTask(GetTaskRequest request, ServerCallContext context)
    {
        var workerId = await ResolveWorkerIdAsync().ConfigureAwait(false);
        var taskId = ParseRequiredId(request.TaskId, "task_id");

        var task = await RunAsync(() => adhocService.GetTask(workerId, taskId)).ConfigureAwait(false);
        return new GetTaskResponse { Task = MapTask(task) };
    }

    public override async Task<TaskResponse> CreateTask(CreateTaskRequest request, ServerCallContext context)
    {
        var workerId = await ResolveWorkerIdAsync().ConfigureAwait(false);
        var model = MapCreateModel(request);

        var task = await RunAsync(() => adhocService.CreateTask(workerId, model)).ConfigureAwait(false);
        return new TaskResponse { Task = MapTask(task) };
    }

    public override async Task<TaskResponse> UpdateTask(UpdateTaskRequest request, ServerCallContext context)
    {
        var workerId = await ResolveWorkerIdAsync().ConfigureAwait(false);
        var taskId = ParseRequiredId(request.Id, "id");
        var model = MapCreateModel(request);

        var task = await RunAsync(() => adhocService.UpdateTask(workerId, taskId, model)).ConfigureAwait(false);
        return new TaskResponse { Task = MapTask(task) };
    }

    public override async Task<TaskResponse> SetCompleted(SetCompletedRequest request, ServerCallContext context)
    {
        var workerId = await ResolveWorkerIdAsync().ConfigureAwait(false);
        var taskId = ParseRequiredId(request.TaskId, "task_id");

        var task = await RunAsync(() => adhocService.SetCompleted(workerId, taskId, request.Completed))
            .ConfigureAwait(false);
        return new TaskResponse { Task = MapTask(task) };
    }

    public override async Task<TaskResponse> Archive(ArchiveRequest request, ServerCallContext context)
    {
        var workerId = await ResolveWorkerIdAsync().ConfigureAwait(false);
        var taskId = ParseRequiredId(request.TaskId, "task_id");

        var task = await RunAsync(() => adhocService.Archive(workerId, taskId)).ConfigureAwait(false);
        return new TaskResponse { Task = MapTask(task) };
    }

    public override async Task<TaskResponse> Reopen(ReopenRequest request, ServerCallContext context)
    {
        var workerId = await ResolveWorkerIdAsync().ConfigureAwait(false);
        var taskId = ParseRequiredId(request.TaskId, "task_id");

        var task = await RunAsync(() => adhocService.Reopen(workerId, taskId)).ConfigureAwait(false);
        return new TaskResponse { Task = MapTask(task) };
    }

    public override async Task<DeleteResponse> Delete(DeleteRequest request, ServerCallContext context)
    {
        var workerId = await ResolveWorkerIdAsync().ConfigureAwait(false);
        var taskId = ParseRequiredId(request.TaskId, "task_id");

        await RunVoidAsync(() => adhocService.Delete(workerId, taskId)).ConfigureAwait(false);
        return new DeleteResponse();
    }

    public override async Task<TaskResponse> AddComment(AddCommentRequest request, ServerCallContext context)
    {
        var workerId = await ResolveWorkerIdAsync().ConfigureAwait(false);
        var taskId = ParseRequiredId(request.TaskId, "task_id");

        var task = await RunAsync(() => adhocService.AddComment(workerId, taskId, request.Text ?? string.Empty))
            .ConfigureAwait(false);
        return new TaskResponse { Task = MapTask(task) };
    }

    public override async Task<ListAdhocPropertiesResponse> ListProperties(
        ListAdhocPropertiesRequest request,
        ServerCallContext context)
    {
        var workerId = await ResolveWorkerIdAsync().ConfigureAwait(false);
        var properties = await adhocService.ListProperties(workerId).ConfigureAwait(false);

        var response = new ListAdhocPropertiesResponse();
        response.Properties.AddRange(properties.Select(p => new PropertyRef
        {
            Id = p.Id.ToString(CultureInfo.InvariantCulture),
            Name = p.Name
        }));
        return response;
    }

    public override async Task<ListAreasResponse> ListAreas(ListAreasRequest request, ServerCallContext context)
    {
        var workerId = await ResolveWorkerIdAsync().ConfigureAwait(false);
        var propertyId = ParseRequiredId(request.PropertyId, "property_id");

        var areas = await RunAsync(() => adhocService.ListAreas(workerId, propertyId)).ConfigureAwait(false);

        var response = new ListAreasResponse();
        response.Areas.AddRange(areas.Select(a => new AreaRef
        {
            Id = a.Id.ToString(CultureInfo.InvariantCulture),
            PropertyId = a.PropertyId.ToString(CultureInfo.InvariantCulture),
            Name = a.Name
        }));
        return response;
    }

    public override async Task<ListWorkersResponse> ListWorkers(ListWorkersRequest request, ServerCallContext context)
    {
        var workerId = await ResolveWorkerIdAsync().ConfigureAwait(false);
        var propertyId = ParseRequiredId(request.PropertyId, "property_id");

        var workers = await RunAsync(() => adhocService.ListWorkers(workerId, propertyId)).ConfigureAwait(false);

        var response = new ListWorkersResponse();
        response.Workers.AddRange(workers.Select(w => new WorkerRef
        {
            Id = w.WorkerId.ToString(CultureInfo.InvariantCulture),
            DisplayName = w.DisplayName
        }));
        return response;
    }

    public override async Task<ListTagsResponse> ListTags(ListTagsRequest request, ServerCallContext context)
    {
        var workerId = await ResolveWorkerIdAsync().ConfigureAwait(false);
        var tags = await adhocService.ListTags(workerId).ConfigureAwait(false);

        var response = new ListTagsResponse();
        response.Tags.AddRange(tags.Select(MapTag));
        return response;
    }

    public override async Task<TagResponse> CreateTag(CreateTagRequest request, ServerCallContext context)
    {
        var workerId = await ResolveWorkerIdAsync().ConfigureAwait(false);
        var tag = await RunAsync(() => adhocService.CreateTag(workerId, request.Name ?? string.Empty))
            .ConfigureAwait(false);
        return new TagResponse { Tag = MapTag(tag) };
    }

    public override async Task<TagResponse> RenameTag(RenameTagRequest request, ServerCallContext context)
    {
        var workerId = await ResolveWorkerIdAsync().ConfigureAwait(false);
        var tagId = ParseRequiredId(request.Id, "id");

        var tag = await RunAsync(() => adhocService.RenameTag(workerId, tagId, request.Name ?? string.Empty))
            .ConfigureAwait(false);
        return new TagResponse { Tag = MapTag(tag) };
    }

    public override async Task<DeleteTagResponse> DeleteTag(DeleteTagRequest request, ServerCallContext context)
    {
        var workerId = await ResolveWorkerIdAsync().ConfigureAwait(false);
        var tagId = ParseRequiredId(request.Id, "id");

        await RunVoidAsync(() => adhocService.DeleteTag(workerId, tagId)).ConfigureAwait(false);
        return new DeleteTagResponse();
    }

    /// <summary>
    /// Receives an uploaded photo as a stream of <c>UploadPhotoChunk</c>
    /// messages: the first message MUST carry the <c>meta</c> oneof variant
    /// (task_id + content_type), every subsequent message MUST carry
    /// <c>chunk</c> bytes. Assembled bytes + content_type are handed to
    /// <see cref="IBackendConfigurationAdhocService.SavePhoto"/>, which owns
    /// the actual S3/UploadedData persistence (B4).
    /// </summary>
    public override async Task<UploadPhotoResponse> UploadPhoto(
        IAsyncStreamReader<UploadPhotoChunk> requestStream,
        ServerCallContext context)
    {
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
        var taskId = ParseRequiredId(meta.TaskId, "task_id");
        var contentType = meta.ContentType?.Trim() ?? string.Empty;

        var workerId = await ResolveWorkerIdAsync().ConfigureAwait(false);

        using var ms = new MemoryStream();
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

        var photoId = await RunAsync(() => adhocService.SavePhoto(workerId, taskId, ms.ToArray(), contentType))
            .ConfigureAwait(false);

        return new UploadPhotoResponse { PhotoId = photoId.ToString(CultureInfo.InvariantCulture) };
    }

    /// <summary>
    /// Streams a photo back as a <c>meta</c> message (content_type) followed
    /// by <see cref="PhotoChunkSize"/>-sized <c>chunk</c> messages, reading
    /// from the <see cref="Stream"/> handed back by
    /// <see cref="IBackendConfigurationAdhocService.GetPhoto"/>.
    /// </summary>
    public override async Task GetPhoto(
        GetPhotoRequest request,
        IServerStreamWriter<PhotoChunk> responseStream,
        ServerCallContext context)
    {
        var workerId = await ResolveWorkerIdAsync().ConfigureAwait(false);
        var photoId = ParseRequiredId(request.PhotoId, "photo_id");

        var (content, contentType) = await RunAsync(() => adhocService.GetPhoto(workerId, photoId))
            .ConfigureAwait(false);

        await using (content.ConfigureAwait(false))
        {
            await responseStream.WriteAsync(new PhotoChunk
            {
                Meta = new PhotoMeta { ContentType = contentType ?? string.Empty }
            }, context.CancellationToken).ConfigureAwait(false);

            var buffer = new byte[PhotoChunkSize];
            int bytesRead;
            while ((bytesRead = await content
                       .ReadAsync(buffer, 0, buffer.Length, context.CancellationToken)
                       .ConfigureAwait(false)) > 0)
            {
                await responseStream.WriteAsync(new PhotoChunk
                {
                    Chunk = ByteString.CopyFrom(buffer, 0, bytesRead)
                }, context.CancellationToken).ConfigureAwait(false);
            }
        }
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private async Task<int> ResolveWorkerIdAsync()
    {
        var workerId = await siteResolver.GetSdkSiteIdAsync().ConfigureAwait(false);
        if (workerId == 0)
        {
            logger.LogWarning("AdhocGrpcService: no resolvable SDK worker/site identity for the caller.");
            throw new RpcException(new Status(StatusCode.Unauthenticated,
                "Caller has no resolvable SDK worker/site identity."));
        }
        return workerId;
    }

    /// <summary>Runs a service call, mapping the typed Adhoc exceptions to RPC status codes.</summary>
    private static async Task<T> RunAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (AdhocTaskNotFoundException ex)
        {
            throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
        }
        catch (AdhocTagNotFoundException ex)
        {
            throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
        }
        catch (AdhocTaskPhotoNotFoundException ex)
        {
            throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
        }
        catch (AdhocTaskUnauthorizedException ex)
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied, ex.Message));
        }
        catch (ArgumentException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
    }

    private static async Task RunVoidAsync(Func<Task> action)
    {
        await RunAsync(async () =>
        {
            await action().ConfigureAwait(false);
            return true;
        }).ConfigureAwait(false);
    }

    private static int ParseRequiredId(string raw, string fieldName)
    {
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
        {
            return id;
        }
        throw new RpcException(new Status(StatusCode.InvalidArgument, $"{fieldName} must be a numeric id."));
    }

    private static int? ParseOptionalId(string raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : null;
    }

    private static TaskScopeFilter MapScope(TaskScope scope) => scope switch
    {
        TaskScope.Mine => TaskScopeFilter.Mine,
        TaskScope.Everyone => TaskScopeFilter.Everyone,
        TaskScope.Completed => TaskScopeFilter.Completed,
        TaskScope.CreatedByMe => TaskScopeFilter.CreatedByMe,
        _ => TaskScopeFilter.All
    };

    private static List<int> ParseIdList(IEnumerable<string> raws) =>
        raws
            .Select(r => int.TryParse(r, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
                ? id
                : (int?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToList();

    private static DateTime? FromTimestamp(Timestamp timestamp) =>
        timestamp is null ? null : timestamp.ToDateTime();

    private static Timestamp ToTimestamp(DateTime? value) =>
        value.HasValue ? Timestamp.FromDateTime(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)) : null;

    /// <summary>
    /// Shared by CreateTask/UpdateTask: both requests carry the identical
    /// AdhocTaskDraft-shaped field set (UpdateTaskRequest additionally has
    /// `id`, handled by the caller).
    /// </summary>
    private static AdhocTaskCreateModel MapCreateModel(CreateTaskRequest request) => new()
    {
        Title = request.Title ?? string.Empty,
        Description = request.Description ?? string.Empty,
        Urgent = request.Urgent,
        PropertyId = ParseRequiredId(request.PropertyId, "property_id"),
        AreaId = ParseOptionalId(request.AreaId),
        TagIds = ParseIdList(request.TagIds),
        PhotoIds = ParseIdList(request.PhotoIds),
        VisibleFrom = FromTimestamp(request.VisibleFrom),
        Deadline = FromTimestamp(request.Deadline),
        VisibleReminder = request.VisibleReminder,
        DeadlineReminder = request.DeadlineReminder,
        DeadlineReminderRepeat = (int)request.DeadlineReminderRepeat,
        VisibleReminderTimeMinutes = request.VisibleReminderMinutes,
        DeadlineReminderTimeMinutes = request.DeadlineReminderMinutes,
        ExecutionRule = (int)request.ExecutionRule,
        AssignedWorkerIds = ParseIdList(request.AssignedWorkerIds)
    };

    private static AdhocTaskCreateModel MapCreateModel(UpdateTaskRequest request) => new()
    {
        Title = request.Title ?? string.Empty,
        Description = request.Description ?? string.Empty,
        Urgent = request.Urgent,
        PropertyId = ParseRequiredId(request.PropertyId, "property_id"),
        AreaId = ParseOptionalId(request.AreaId),
        TagIds = ParseIdList(request.TagIds),
        PhotoIds = ParseIdList(request.PhotoIds),
        VisibleFrom = FromTimestamp(request.VisibleFrom),
        Deadline = FromTimestamp(request.Deadline),
        VisibleReminder = request.VisibleReminder,
        DeadlineReminder = request.DeadlineReminder,
        DeadlineReminderRepeat = (int)request.DeadlineReminderRepeat,
        VisibleReminderTimeMinutes = request.VisibleReminderMinutes,
        DeadlineReminderTimeMinutes = request.DeadlineReminderMinutes,
        ExecutionRule = (int)request.ExecutionRule,
        AssignedWorkerIds = ParseIdList(request.AssignedWorkerIds)
    };

    private static AdhocTask MapTask(AdhocTaskModel model)
    {
        var task = new AdhocTask
        {
            Id = model.Id.ToString(CultureInfo.InvariantCulture),
            CreatedBy = model.CreatedByWorkerId.ToString(CultureInfo.InvariantCulture),
            CreatedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(model.CreatedAt, DateTimeKind.Utc)),
            Title = model.Title,
            Description = model.Description,
            Urgent = model.Urgent,
            PropertyId = model.PropertyId.ToString(CultureInfo.InvariantCulture),
            AreaId = model.AreaId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            VisibleReminder = model.VisibleReminder,
            DeadlineReminder = model.DeadlineReminder,
            DeadlineReminderRepeat = (ReminderRepeat)model.DeadlineReminderRepeat,
            VisibleReminderMinutes = model.VisibleReminderTimeMinutes,
            DeadlineReminderMinutes = model.DeadlineReminderTimeMinutes,
            ExecutionRule = (ExecutionRule)model.ExecutionRule,
            Completed = model.Completed,
            CompletedBy = model.CompletedByWorkerId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            Archived = model.Archived
        };

        var visibleFrom = ToTimestamp(model.VisibleFrom);
        if (visibleFrom != null) task.VisibleFrom = visibleFrom;

        var deadline = ToTimestamp(model.Deadline);
        if (deadline != null) task.Deadline = deadline;

        var completedAt = ToTimestamp(model.CompletedAt);
        if (completedAt != null) task.CompletedAt = completedAt;

        var archivedAt = ToTimestamp(model.ArchivedAt);
        if (archivedAt != null) task.ArchivedAt = archivedAt;

        task.TagIds.AddRange(model.TagIds.Select(id => id.ToString(CultureInfo.InvariantCulture)));
        task.Photos.AddRange(model.Photos.Select(p => new AdhocPhoto
        {
            Id = p.Id.ToString(CultureInfo.InvariantCulture),
            ContentType = p.ContentType
        }));
        task.AssignedWorkerIds.AddRange(model.AssignedWorkerIds.Select(id => id.ToString(CultureInfo.InvariantCulture)));
        task.AssignmentLog.AddRange(model.AssignmentLog.Select(l =>
        {
            var entry = new AssignmentLogEntry
            {
                ChangedBy = l.ChangedByWorkerId.ToString(CultureInfo.InvariantCulture),
                ChangedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(l.ChangedAt, DateTimeKind.Utc))
            };
            entry.FromWorkerIds.AddRange(l.FromWorkerIds.Select(id => id.ToString(CultureInfo.InvariantCulture)));
            entry.ToWorkerIds.AddRange(l.ToWorkerIds.Select(id => id.ToString(CultureInfo.InvariantCulture)));
            return entry;
        }));
        task.Comments.AddRange(model.Comments.Select(c => new AdhocComment
        {
            AuthorId = c.AuthorWorkerId.ToString(CultureInfo.InvariantCulture),
            CreatedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(c.CreatedAt, DateTimeKind.Utc)),
            Text = c.Text
        }));

        return task;
    }

    private static AdhocTag MapTag(AdhocTagModel model) => new()
    {
        Id = model.Id.ToString(CultureInfo.InvariantCulture),
        Name = model.Name,
        IsUserTag = model.IsUserTag
    };
}
