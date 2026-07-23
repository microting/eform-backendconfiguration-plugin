using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Grpc.Adhoc;
using BackendConfiguration.Pn.Infrastructure.Models.Adhoc;
using BackendConfiguration.Pn.Services.BackendConfigurationAdhocService;
using BackendConfiguration.Pn.Services.GrpcServices;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Test.GrpcServices;

/// <summary>
/// Pure mapping tests for <see cref="AdhocGrpcService"/>: <see cref="IBackendConfigurationAdhocService"/>
/// and <see cref="IGrpcSiteResolver"/> are both faked (NSubstitute), so these
/// exercise only the wire &lt;-&gt; domain-model translation and the RPC
/// status-code mapping for the service's typed exceptions — no DB, no S3.
/// </summary>
[TestFixture]
public class AdhocGrpcServiceMappingTests
{
    private static AdhocGrpcService CreateSut(
        IBackendConfigurationAdhocService adhocService = null,
        IGrpcSiteResolver resolver = null)
    {
        adhocService ??= Substitute.For<IBackendConfigurationAdhocService>();
        resolver ??= Substitute.For<IGrpcSiteResolver>();
        return new AdhocGrpcService(adhocService, resolver, NullLogger<AdhocGrpcService>.Instance);
    }

    private static AdhocTaskModel SampleTask(int id = 42) => new()
    {
        Id = id,
        CreatedByWorkerId = 7,
        CreatedAt = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc),
        Title = "Fix the leak",
        Description = "Kitchen sink",
        Urgent = true,
        PropertyId = 10,
        AreaId = 3,
        TagIds = [1, 2],
        Photos = [new AdhocTaskPhotoModel { Id = 99, ContentType = "image/jpeg" }],
        VisibleReminder = true,
        DeadlineReminder = false,
        DeadlineReminderRepeat = 1,
        VisibleReminderTimeMinutes = 480,
        DeadlineReminderTimeMinutes = 600,
        ExecutionRule = 1,
        AssignedWorkerIds = [7, 8],
        Completed = false,
        Archived = false,
        Comments = [new AdhocCommentModel { AuthorWorkerId = 7, CreatedAt = new DateTime(2026, 6, 1, 11, 0, 0, DateTimeKind.Utc), Text = "hi" }]
    };

    // ---- worker resolution ----

    [Test]
    public void ListTasks_NoResolvableWorker_ThrowsUnauthenticated()
    {
        var resolver = Substitute.For<IGrpcSiteResolver>();
        resolver.GetSdkSiteIdAsync().Returns(0);
        var sut = CreateSut(resolver: resolver);

        var ex = Assert.ThrowsAsync<RpcException>(async () =>
            await sut.ListTasks(new ListTasksRequest(), Substitute.For<ServerCallContext>()));
        Assert.That(ex.StatusCode, Is.EqualTo(StatusCode.Unauthenticated));
    }

    [Test]
    public async Task GetCurrentWorker_MapsWorkerIdDisplayNameAndPropertyIds()
    {
        var resolver = Substitute.For<IGrpcSiteResolver>();
        resolver.GetSdkSiteIdAsync().Returns(7);
        resolver.GetDisplayNameAsync(7).Returns("Alice's Phone");
        var adhocService = Substitute.For<IBackendConfigurationAdhocService>();
        adhocService.ListProperties(7).Returns([
            new AdhocPropertyModel { Id = 10, Name = "Prop A" },
            new AdhocPropertyModel { Id = 12, Name = "Prop B" }
        ]);

        var sut = CreateSut(adhocService, resolver);
        var response = await sut.GetCurrentWorker(new GetCurrentWorkerRequest(), Substitute.For<ServerCallContext>());

        Assert.That(response.WorkerId, Is.EqualTo("7"));
        Assert.That(response.DisplayName, Is.EqualTo("Alice's Phone"));
        Assert.That(response.PropertyIds, Is.EquivalentTo(new[] { "10", "12" }));
    }

    // ---- ListTasks ----

    [Test]
    public async Task ListTasks_MapsScopeAndPropertyId_ForwardsToService()
    {
        var resolver = Substitute.For<IGrpcSiteResolver>();
        resolver.GetSdkSiteIdAsync().Returns(7);
        var adhocService = Substitute.For<IBackendConfigurationAdhocService>();
        adhocService.ListTasks(7, TaskScopeFilter.Mine, 10).Returns([SampleTask()]);

        var sut = CreateSut(adhocService, resolver);
        var response = await sut.ListTasks(
            new ListTasksRequest { Scope = TaskScope.Mine, PropertyId = "10" },
            Substitute.For<ServerCallContext>());

        Assert.That(response.Tasks, Has.Count.EqualTo(1));
        Assert.That(response.Tasks[0].Id, Is.EqualTo("42"));
    }

    [Test]
    public async Task ListTasks_UnspecifiedScope_MapsToAll_EmptyPropertyId_MapsToNull()
    {
        var resolver = Substitute.For<IGrpcSiteResolver>();
        resolver.GetSdkSiteIdAsync().Returns(7);
        var adhocService = Substitute.For<IBackendConfigurationAdhocService>();
        adhocService.ListTasks(7, TaskScopeFilter.All, null).Returns([]);

        var sut = CreateSut(adhocService, resolver);
        await sut.ListTasks(new ListTasksRequest { PropertyId = "" }, Substitute.For<ServerCallContext>());

        await adhocService.Received(1).ListTasks(7, TaskScopeFilter.All, null);
    }

    [TestCase(TaskScope.Everyone, TaskScopeFilter.Everyone)]
    [TestCase(TaskScope.Completed, TaskScopeFilter.Completed)]
    [TestCase(TaskScope.CreatedByMe, TaskScopeFilter.CreatedByMe)]
    public async Task ListTasks_MapsEveryScopeValue(TaskScope wire, TaskScopeFilter expected)
    {
        var resolver = Substitute.For<IGrpcSiteResolver>();
        resolver.GetSdkSiteIdAsync().Returns(7);
        var adhocService = Substitute.For<IBackendConfigurationAdhocService>();
        adhocService.ListTasks(7, expected, null).Returns([]);

        var sut = CreateSut(adhocService, resolver);
        await sut.ListTasks(new ListTasksRequest { Scope = wire }, Substitute.For<ServerCallContext>());

        await adhocService.Received(1).ListTasks(7, expected, null);
    }

    // ---- GetTask / exception mapping ----

    [Test]
    public async Task GetTask_MapsFullTaskShape_IncludingUtcTimestamps()
    {
        var resolver = Substitute.For<IGrpcSiteResolver>();
        resolver.GetSdkSiteIdAsync().Returns(7);
        var adhocService = Substitute.For<IBackendConfigurationAdhocService>();
        var model = SampleTask();
        model.VisibleFrom = new DateTime(2026, 6, 2, 8, 0, 0, DateTimeKind.Unspecified);
        adhocService.GetTask(7, 42, false).Returns(model);

        var sut = CreateSut(adhocService, resolver);
        var response = await sut.GetTask(new GetTaskRequest { TaskId = "42" }, Substitute.For<ServerCallContext>());

        var task = response.Task;
        Assert.That(task.Id, Is.EqualTo("42"));
        Assert.That(task.CreatedBy, Is.EqualTo("7"));
        Assert.That(task.PropertyId, Is.EqualTo("10"));
        Assert.That(task.AreaId, Is.EqualTo("3"));
        Assert.That(task.TagIds, Is.EquivalentTo(new[] { "1", "2" }));
        Assert.That(task.AssignedWorkerIds, Is.EquivalentTo(new[] { "7", "8" }));
        Assert.That(task.Photos, Has.Count.EqualTo(1));
        Assert.That(task.Photos[0].Id, Is.EqualTo("99"));
        Assert.That(task.Comments, Has.Count.EqualTo(1));
        Assert.That(task.DeadlineReminderRepeat, Is.EqualTo(ReminderRepeat.Weekdays));
        Assert.That(task.ExecutionRule, Is.EqualTo(ExecutionRule.Everyone));
        Assert.That(task.CreatedAt, Is.EqualTo(Timestamp.FromDateTime(model.CreatedAt)));
        Assert.That(task.VisibleFrom, Is.EqualTo(
            Timestamp.FromDateTime(DateTime.SpecifyKind(model.VisibleFrom.Value, DateTimeKind.Utc))));
        // Unset optional timestamps stay null on the wire.
        Assert.That(task.Deadline, Is.Null);
        Assert.That(task.CompletedAt, Is.Null);
        Assert.That(task.ArchivedAt, Is.Null);
    }

    [Test]
    public void GetTask_NotFound_MapsToRpcNotFound()
    {
        var resolver = Substitute.For<IGrpcSiteResolver>();
        resolver.GetSdkSiteIdAsync().Returns(7);
        var adhocService = Substitute.For<IBackendConfigurationAdhocService>();
        adhocService.GetTask(7, 42, false)
            .Returns<AdhocTaskModel>(_ => throw new AdhocTaskNotFoundException(42));

        var sut = CreateSut(adhocService, resolver);
        var ex = Assert.ThrowsAsync<RpcException>(async () =>
            await sut.GetTask(new GetTaskRequest { TaskId = "42" }, Substitute.For<ServerCallContext>()));
        Assert.That(ex.StatusCode, Is.EqualTo(StatusCode.NotFound));
    }

    [Test]
    public void GetTask_Unauthorized_MapsToRpcPermissionDenied()
    {
        var resolver = Substitute.For<IGrpcSiteResolver>();
        resolver.GetSdkSiteIdAsync().Returns(7);
        var adhocService = Substitute.For<IBackendConfigurationAdhocService>();
        adhocService.GetTask(7, 42, false)
            .Returns<AdhocTaskModel>(_ => throw new AdhocTaskUnauthorizedException("no access"));

        var sut = CreateSut(adhocService, resolver);
        var ex = Assert.ThrowsAsync<RpcException>(async () =>
            await sut.GetTask(new GetTaskRequest { TaskId = "42" }, Substitute.For<ServerCallContext>()));
        Assert.That(ex.StatusCode, Is.EqualTo(StatusCode.PermissionDenied));
    }

    [Test]
    public void GetTask_NonNumericTaskId_ThrowsInvalidArgument()
    {
        var resolver = Substitute.For<IGrpcSiteResolver>();
        resolver.GetSdkSiteIdAsync().Returns(7);

        var sut = CreateSut(resolver: resolver);
        var ex = Assert.ThrowsAsync<RpcException>(async () =>
            await sut.GetTask(new GetTaskRequest { TaskId = "not-a-number" }, Substitute.For<ServerCallContext>()));
        Assert.That(ex.StatusCode, Is.EqualTo(StatusCode.InvalidArgument));
    }

    // ---- CreateTask / UpdateTask model mapping ----

    [Test]
    public async Task CreateTask_MapsRequestIntoCreateModel()
    {
        var resolver = Substitute.For<IGrpcSiteResolver>();
        resolver.GetSdkSiteIdAsync().Returns(7);
        var adhocService = Substitute.For<IBackendConfigurationAdhocService>();
        AdhocTaskCreateModel captured = null;
        adhocService.CreateTask(7, Arg.Do<AdhocTaskCreateModel>(m => captured = m)).Returns(SampleTask());

        var request = new CreateTaskRequest
        {
            Title = "Title",
            Description = "Desc",
            Urgent = true,
            PropertyId = "10",
            AreaId = "3",
            ExecutionRule = ExecutionRule.Everyone,
            DeadlineReminderRepeat = ReminderRepeat.Weekdays,
            VisibleReminderMinutes = 100,
            DeadlineReminderMinutes = 200
        };
        request.TagIds.AddRange(["1", "2", "not-a-number"]);
        request.AssignedWorkerIds.AddRange(["7", "8"]);

        var sut = CreateSut(adhocService, resolver);
        await sut.CreateTask(request, Substitute.For<ServerCallContext>());

        Assert.That(captured, Is.Not.Null);
        Assert.That(captured.Title, Is.EqualTo("Title"));
        Assert.That(captured.PropertyId, Is.EqualTo(10));
        Assert.That(captured.AreaId, Is.EqualTo(3));
        // Non-numeric ids in a repeated field are skipped, not all-or-nothing.
        Assert.That(captured.TagIds, Is.EquivalentTo(new[] { 1, 2 }));
        Assert.That(captured.AssignedWorkerIds, Is.EquivalentTo(new[] { 7, 8 }));
        Assert.That(captured.ExecutionRule, Is.EqualTo(1));
        Assert.That(captured.DeadlineReminderRepeat, Is.EqualTo(1));
        Assert.That(captured.VisibleFrom, Is.Null);
        Assert.That(captured.Deadline, Is.Null);
    }

    [Test]
    public async Task UpdateTask_ParsesIdAndForwardsModel()
    {
        var resolver = Substitute.For<IGrpcSiteResolver>();
        resolver.GetSdkSiteIdAsync().Returns(7);
        var adhocService = Substitute.For<IBackendConfigurationAdhocService>();
        adhocService.UpdateTask(7, 42, Arg.Any<AdhocTaskCreateModel>(), false).Returns(SampleTask());

        var sut = CreateSut(adhocService, resolver);
        var response = await sut.UpdateTask(
            new UpdateTaskRequest { Id = "42", PropertyId = "10" },
            Substitute.For<ServerCallContext>());

        Assert.That(response.Task.Id, Is.EqualTo("42"));
        await adhocService.Received(1).UpdateTask(7, 42, Arg.Any<AdhocTaskCreateModel>(), false);
    }

    // ---- lifecycle RPCs ----

    [Test]
    public async Task SetCompleted_ForwardsCompletedFlag()
    {
        var resolver = Substitute.For<IGrpcSiteResolver>();
        resolver.GetSdkSiteIdAsync().Returns(7);
        var adhocService = Substitute.For<IBackendConfigurationAdhocService>();
        adhocService.SetCompleted(7, 42, true, false).Returns(SampleTask());

        var sut = CreateSut(adhocService, resolver);
        await sut.SetCompleted(
            new SetCompletedRequest { TaskId = "42", Completed = true },
            Substitute.For<ServerCallContext>());

        await adhocService.Received(1).SetCompleted(7, 42, true, false);
    }

    [Test]
    public async Task Delete_ReturnsEmptyResponse_AndForwardsToService()
    {
        var resolver = Substitute.For<IGrpcSiteResolver>();
        resolver.GetSdkSiteIdAsync().Returns(7);
        var adhocService = Substitute.For<IBackendConfigurationAdhocService>();

        var sut = CreateSut(adhocService, resolver);
        var response = await sut.Delete(new DeleteRequest { TaskId = "42" }, Substitute.For<ServerCallContext>());

        Assert.That(response, Is.Not.Null);
        await adhocService.Received(1).Delete(7, 42, false);
    }

    [Test]
    public void Delete_NotFound_MapsToRpcNotFound()
    {
        var resolver = Substitute.For<IGrpcSiteResolver>();
        resolver.GetSdkSiteIdAsync().Returns(7);
        var adhocService = Substitute.For<IBackendConfigurationAdhocService>();
        adhocService.Delete(7, 42, false).Returns(_ => throw new AdhocTaskNotFoundException(42));

        var sut = CreateSut(adhocService, resolver);
        var ex = Assert.ThrowsAsync<RpcException>(async () =>
            await sut.Delete(new DeleteRequest { TaskId = "42" }, Substitute.For<ServerCallContext>()));
        Assert.That(ex.StatusCode, Is.EqualTo(StatusCode.NotFound));
    }

    // ---- reference data ----

    [Test]
    public async Task ListProperties_MapsToPropertyRef()
    {
        var resolver = Substitute.For<IGrpcSiteResolver>();
        resolver.GetSdkSiteIdAsync().Returns(7);
        var adhocService = Substitute.For<IBackendConfigurationAdhocService>();
        adhocService.ListProperties(7).Returns([new AdhocPropertyModel { Id = 10, Name = "Prop A" }]);

        var sut = CreateSut(adhocService, resolver);
        var response = await sut.ListProperties(new ListAdhocPropertiesRequest(), Substitute.For<ServerCallContext>());

        Assert.That(response.Properties, Has.Count.EqualTo(1));
        Assert.That(response.Properties[0].Id, Is.EqualTo("10"));
        Assert.That(response.Properties[0].Name, Is.EqualTo("Prop A"));
    }

    [Test]
    public void ListAreas_Unauthorized_MapsToRpcPermissionDenied()
    {
        var resolver = Substitute.For<IGrpcSiteResolver>();
        resolver.GetSdkSiteIdAsync().Returns(7);
        var adhocService = Substitute.For<IBackendConfigurationAdhocService>();
        adhocService.ListAreas(7, 10, false)
            .Returns<List<AdhocAreaModel>>(_ => throw new AdhocTaskUnauthorizedException("no access"));

        var sut = CreateSut(adhocService, resolver);
        var ex = Assert.ThrowsAsync<RpcException>(async () =>
            await sut.ListAreas(new ListAreasRequest { PropertyId = "10" }, Substitute.For<ServerCallContext>()));
        Assert.That(ex.StatusCode, Is.EqualTo(StatusCode.PermissionDenied));
    }

    [Test]
    public async Task ListWorkers_MapsToWorkerRef()
    {
        var resolver = Substitute.For<IGrpcSiteResolver>();
        resolver.GetSdkSiteIdAsync().Returns(7);
        var adhocService = Substitute.For<IBackendConfigurationAdhocService>();
        adhocService.ListWorkers(7, 10, false)
            .Returns([new AdhocWorkerModel { WorkerId = 8, DisplayName = "Bob" }]);

        var sut = CreateSut(adhocService, resolver);
        var response = await sut.ListWorkers(new ListWorkersRequest { PropertyId = "10" }, Substitute.For<ServerCallContext>());

        Assert.That(response.Workers, Has.Count.EqualTo(1));
        Assert.That(response.Workers[0].Id, Is.EqualTo("8"));
        Assert.That(response.Workers[0].DisplayName, Is.EqualTo("Bob"));
    }

    // ---- tags ----

    [Test]
    public async Task ListTags_MapsIsUserTag()
    {
        var resolver = Substitute.For<IGrpcSiteResolver>();
        resolver.GetSdkSiteIdAsync().Returns(7);
        var adhocService = Substitute.For<IBackendConfigurationAdhocService>();
        adhocService.ListTags(7).Returns([
            new AdhocTagModel { Id = 1, Name = "Global", IsUserTag = false },
            new AdhocTagModel { Id = 2, Name = "Mine", IsUserTag = true }
        ]);

        var sut = CreateSut(adhocService, resolver);
        var response = await sut.ListTags(new ListTagsRequest(), Substitute.For<ServerCallContext>());

        Assert.That(response.Tags, Has.Count.EqualTo(2));
        Assert.That(response.Tags[0].IsUserTag, Is.False);
        Assert.That(response.Tags[1].IsUserTag, Is.True);
    }

    [Test]
    public void CreateTag_EmptyName_MapsToRpcInvalidArgument()
    {
        var resolver = Substitute.For<IGrpcSiteResolver>();
        resolver.GetSdkSiteIdAsync().Returns(7);
        var adhocService = Substitute.For<IBackendConfigurationAdhocService>();
        adhocService.CreateTag(7, "")
            .Returns<AdhocTagModel>(_ => throw new ArgumentException("Tag name must not be empty.", "name"));

        var sut = CreateSut(adhocService, resolver);
        var ex = Assert.ThrowsAsync<RpcException>(async () =>
            await sut.CreateTag(new CreateTagRequest { Name = "" }, Substitute.For<ServerCallContext>()));
        Assert.That(ex.StatusCode, Is.EqualTo(StatusCode.InvalidArgument));
    }

    [Test]
    public void RenameTag_NotFound_MapsToRpcNotFound()
    {
        var resolver = Substitute.For<IGrpcSiteResolver>();
        resolver.GetSdkSiteIdAsync().Returns(7);
        var adhocService = Substitute.For<IBackendConfigurationAdhocService>();
        adhocService.RenameTag(7, 99, "New")
            .Returns<AdhocTagModel>(_ => throw new AdhocTagNotFoundException(99));

        var sut = CreateSut(adhocService, resolver);
        var ex = Assert.ThrowsAsync<RpcException>(async () =>
            await sut.RenameTag(new RenameTagRequest { Id = "99", Name = "New" }, Substitute.For<ServerCallContext>()));
        Assert.That(ex.StatusCode, Is.EqualTo(StatusCode.NotFound));
    }

    [Test]
    public async Task DeleteTag_ForwardsToService()
    {
        var resolver = Substitute.For<IGrpcSiteResolver>();
        resolver.GetSdkSiteIdAsync().Returns(7);
        var adhocService = Substitute.For<IBackendConfigurationAdhocService>();

        var sut = CreateSut(adhocService, resolver);
        await sut.DeleteTag(new DeleteTagRequest { Id = "5" }, Substitute.For<ServerCallContext>());

        await adhocService.Received(1).DeleteTag(7, 5);
    }

    // ---- photos ----

    [Test]
    public void UploadPhoto_StreamNotStartingWithMeta_ThrowsInvalidArgument()
    {
        var resolver = Substitute.For<IGrpcSiteResolver>();
        resolver.GetSdkSiteIdAsync().Returns(7);
        var sut = CreateSut(resolver: resolver);

        var reader = new FakeAsyncStreamReader<UploadPhotoChunk>([
            new UploadPhotoChunk { Chunk = ByteString.CopyFrom([1, 2, 3]) }
        ]);

        var ex = Assert.ThrowsAsync<RpcException>(async () =>
            await sut.UploadPhoto(reader, Substitute.For<ServerCallContext>()));
        Assert.That(ex.StatusCode, Is.EqualTo(StatusCode.InvalidArgument));
    }

    [Test]
    public void UploadPhoto_EmptyStream_ThrowsInvalidArgument()
    {
        var resolver = Substitute.For<IGrpcSiteResolver>();
        resolver.GetSdkSiteIdAsync().Returns(7);
        var sut = CreateSut(resolver: resolver);

        var reader = new FakeAsyncStreamReader<UploadPhotoChunk>([]);

        var ex = Assert.ThrowsAsync<RpcException>(async () =>
            await sut.UploadPhoto(reader, Substitute.For<ServerCallContext>()));
        Assert.That(ex.StatusCode, Is.EqualTo(StatusCode.InvalidArgument));
    }

    [Test]
    public async Task UploadPhoto_AssemblesChunksAndForwardsToSavePhoto()
    {
        var resolver = Substitute.For<IGrpcSiteResolver>();
        resolver.GetSdkSiteIdAsync().Returns(7);
        var adhocService = Substitute.For<IBackendConfigurationAdhocService>();
        adhocService.SavePhoto(7, 42, Arg.Any<byte[]>(), "image/jpeg").Returns(101);

        var reader = new FakeAsyncStreamReader<UploadPhotoChunk>([
            new UploadPhotoChunk { Meta = new UploadPhotoMeta { TaskId = "42", ContentType = "image/jpeg" } },
            new UploadPhotoChunk { Chunk = ByteString.CopyFrom([1, 2, 3]) },
            new UploadPhotoChunk { Chunk = ByteString.CopyFrom([4, 5]) }
        ]);

        var sut = CreateSut(adhocService, resolver);
        var response = await sut.UploadPhoto(reader, Substitute.For<ServerCallContext>());

        Assert.That(response.PhotoId, Is.EqualTo("101"));
        await adhocService.Received(1).SavePhoto(7, 42,
            Arg.Is<byte[]>(b => b.Length == 5 && b[0] == 1 && b[4] == 5), "image/jpeg");
    }

    [Test]
    public async Task GetPhoto_WritesMetaThenChunks()
    {
        var resolver = Substitute.For<IGrpcSiteResolver>();
        resolver.GetSdkSiteIdAsync().Returns(7);
        var adhocService = Substitute.For<IBackendConfigurationAdhocService>();
        var bytes = new byte[] { 9, 8, 7 };
        adhocService.GetPhoto(7, 101).Returns((new System.IO.MemoryStream(bytes), "image/png"));

        var sut = CreateSut(adhocService, resolver);
        var writer = new FakeServerStreamWriter<PhotoChunk>();

        await sut.GetPhoto(new GetPhotoRequest { PhotoId = "101" }, writer, Substitute.For<ServerCallContext>());

        Assert.That(writer.Written, Has.Count.EqualTo(2));
        Assert.That(writer.Written[0].KindCase, Is.EqualTo(PhotoChunk.KindOneofCase.Meta));
        Assert.That(writer.Written[0].Meta.ContentType, Is.EqualTo("image/png"));
        Assert.That(writer.Written[1].KindCase, Is.EqualTo(PhotoChunk.KindOneofCase.Chunk));
        Assert.That(writer.Written[1].Chunk.ToByteArray(), Is.EqualTo(bytes));
    }

    [Test]
    public void GetPhoto_NotFound_MapsToRpcNotFound()
    {
        var resolver = Substitute.For<IGrpcSiteResolver>();
        resolver.GetSdkSiteIdAsync().Returns(7);
        var adhocService = Substitute.For<IBackendConfigurationAdhocService>();
        adhocService.GetPhoto(7, 101)
            .Returns<(System.IO.Stream Content, string ContentType)>(_ => throw new AdhocTaskPhotoNotFoundException(101));

        var sut = CreateSut(adhocService, resolver);
        var writer = new FakeServerStreamWriter<PhotoChunk>();

        var ex = Assert.ThrowsAsync<RpcException>(async () =>
            await sut.GetPhoto(new GetPhotoRequest { PhotoId = "101" }, writer, Substitute.For<ServerCallContext>()));
        Assert.That(ex.StatusCode, Is.EqualTo(StatusCode.NotFound));
    }

    // ---- streaming test doubles ----

    private sealed class FakeAsyncStreamReader<T> : IAsyncStreamReader<T> where T : class
    {
        private readonly Queue<T> _items;

        public FakeAsyncStreamReader(IEnumerable<T> items) => _items = new Queue<T>(items);

        public T Current { get; private set; }

        public Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            if (_items.Count == 0)
            {
                Current = null;
                return Task.FromResult(false);
            }

            Current = _items.Dequeue();
            return Task.FromResult(true);
        }
    }

    private sealed class FakeServerStreamWriter<T> : IServerStreamWriter<T>
    {
        public List<T> Written { get; } = [];
        public WriteOptions WriteOptions { get; set; }

        public Task WriteAsync(T message)
        {
            Written.Add(message);
            return Task.CompletedTask;
        }

        public Task WriteAsync(T message, CancellationToken cancellationToken) => WriteAsync(message);
    }
}
