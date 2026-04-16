using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Grpc;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.GrpcServices;
using BackendConfiguration.Pn.Services.UserPropertyAccess;
using Grpc.Core;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using NSubstitute;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Test.GrpcServices;

[TestFixture]
public class CalendarGrpcServiceTests
{
    private static CalendarGrpcService CreateSut(
        IBackendConfigurationCalendarService calendarService = null,
        IBackendConfigurationUserPropertyAccess access = null,
        IGrpcSiteResolver resolver = null)
    {
        calendarService ??= Substitute.For<IBackendConfigurationCalendarService>();
        access ??= Substitute.For<IBackendConfigurationUserPropertyAccess>();
        resolver ??= Substitute.For<IGrpcSiteResolver>();
        return new CalendarGrpcService(calendarService, access, resolver);
    }

    [Test]
    public void GetTasksForWeek_AccessDenied_ThrowsPermissionDenied()
    {
        var resolver = Substitute.For<IGrpcSiteResolver>();
        resolver.GetSdkSiteIdAsync().Returns(7);
        var access = Substitute.For<IBackendConfigurationUserPropertyAccess>();
        access.HasAccessAsync(7, 10).Returns(false);

        var sut = CreateSut(access: access, resolver: resolver);
        var request = new GetTasksForWeekRequest { PropertyId = 10 };

        var ex = Assert.ThrowsAsync<RpcException>(async () =>
            await sut.GetTasksForWeek(request, Substitute.For<ServerCallContext>()));
        Assert.That(ex.StatusCode, Is.EqualTo(StatusCode.PermissionDenied));
    }

    [Test]
    public async Task GetTasksForWeek_ServiceFails_ReturnsFailureWithEmptyTasks()
    {
        var resolver = Substitute.For<IGrpcSiteResolver>();
        resolver.GetSdkSiteIdAsync().Returns(7);
        var access = Substitute.For<IBackendConfigurationUserPropertyAccess>();
        access.HasAccessAsync(7, 10).Returns(true);
        var calSvc = Substitute.For<IBackendConfigurationCalendarService>();
        calSvc.GetTasksForWeek(Arg.Any<CalendarTaskRequestModel>())
            .Returns(new OperationDataResult<List<CalendarTaskResponseModel>>(false, "some error"));

        var sut = CreateSut(calendarService: calSvc, access: access, resolver: resolver);
        var response = await sut.GetTasksForWeek(
            new GetTasksForWeekRequest { PropertyId = 10 },
            Substitute.For<ServerCallContext>());

        Assert.That(response.Success, Is.False);
        Assert.That(response.Tasks, Is.Empty);
    }

    [Test]
    public async Task GetTasksForWeek_HappyPath_MapsAllFields()
    {
        var resolver = Substitute.For<IGrpcSiteResolver>();
        resolver.GetSdkSiteIdAsync().Returns(7);
        var access = Substitute.For<IBackendConfigurationUserPropertyAccess>();
        access.HasAccessAsync(7, 10).Returns(true);
        var calSvc = Substitute.For<IBackendConfigurationCalendarService>();

        var task1 = new CalendarTaskResponseModel
        {
            Id = 1, Title = "Task 1", StartHour = 9.5, Duration = 1.5,
            TaskDate = "2026-04-16", Tags = ["urgent"], AssigneeIds = [5, 6],
            BoardId = 3, Color = "#ff0000", RepeatType = 1, RepeatEvery = 2,
            Completed = false, PropertyId = 10, ComplianceId = null,
            IsFromCompliance = true, Deadline = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            NextExecutionTime = null, PlanningId = 42, IsAllDay = false, ExceptionId = null
        };

        calSvc.GetTasksForWeek(Arg.Any<CalendarTaskRequestModel>())
            .Returns(new OperationDataResult<List<CalendarTaskResponseModel>>(true, [task1]));

        var sut = CreateSut(calendarService: calSvc, access: access, resolver: resolver);
        var response = await sut.GetTasksForWeek(
            new GetTasksForWeekRequest { PropertyId = 10 },
            Substitute.For<ServerCallContext>());

        Assert.That(response.Success, Is.True);
        Assert.That(response.Tasks, Has.Count.EqualTo(1));
        var t = response.Tasks[0];
        Assert.That(t.Id, Is.EqualTo(1));
        Assert.That(t.Title, Is.EqualTo("Task 1"));
        Assert.That(t.StartHour, Is.EqualTo(9.5));
        Assert.That(t.Duration, Is.EqualTo(1.5));
        Assert.That(t.BoardId, Is.EqualTo(3));
        Assert.That(t.ComplianceId, Is.EqualTo(0));
        Assert.That(t.PlanningId, Is.EqualTo(42));
        Assert.That(t.ExceptionId, Is.EqualTo(0));
        Assert.That(t.Deadline, Is.Not.Empty);
        Assert.That(t.NextExecutionTime, Is.Empty);
        Assert.That(t.Tags, Is.EquivalentTo(new[] { "urgent" }));
        Assert.That(t.AssigneeIds, Is.EquivalentTo(new[] { 5, 6 }));
    }

    [Test]
    public async Task GetTasksForWeek_NullCollections_NoException()
    {
        var resolver = Substitute.For<IGrpcSiteResolver>();
        resolver.GetSdkSiteIdAsync().Returns(7);
        var access = Substitute.For<IBackendConfigurationUserPropertyAccess>();
        access.HasAccessAsync(7, 10).Returns(true);
        var calSvc = Substitute.For<IBackendConfigurationCalendarService>();

        var task1 = new CalendarTaskResponseModel
        {
            Id = 1, Title = "T", Tags = null, AssigneeIds = null, PropertyId = 10
        };

        calSvc.GetTasksForWeek(Arg.Any<CalendarTaskRequestModel>())
            .Returns(new OperationDataResult<List<CalendarTaskResponseModel>>(true, [task1]));

        var sut = CreateSut(calendarService: calSvc, access: access, resolver: resolver);
        var response = await sut.GetTasksForWeek(
            new GetTasksForWeekRequest { PropertyId = 10 },
            Substitute.For<ServerCallContext>());

        Assert.That(response.Tasks[0].Tags, Is.Empty);
        Assert.That(response.Tasks[0].AssigneeIds, Is.Empty);
    }

    [Test]
    public void GetBoards_AccessDenied_ThrowsPermissionDenied()
    {
        var resolver = Substitute.For<IGrpcSiteResolver>();
        resolver.GetSdkSiteIdAsync().Returns(7);
        var access = Substitute.For<IBackendConfigurationUserPropertyAccess>();
        access.HasAccessAsync(7, 10).Returns(false);

        var sut = CreateSut(access: access, resolver: resolver);

        var ex = Assert.ThrowsAsync<RpcException>(async () =>
            await sut.GetBoards(new GetBoardsRequest { PropertyId = 10 }, Substitute.For<ServerCallContext>()));
        Assert.That(ex.StatusCode, Is.EqualTo(StatusCode.PermissionDenied));
    }

    [Test]
    public async Task GetBoards_HappyPath_MapsBoardFields()
    {
        var resolver = Substitute.For<IGrpcSiteResolver>();
        resolver.GetSdkSiteIdAsync().Returns(7);
        var access = Substitute.For<IBackendConfigurationUserPropertyAccess>();
        access.HasAccessAsync(7, 10).Returns(true);
        var calSvc = Substitute.For<IBackendConfigurationCalendarService>();

        calSvc.GetBoards(10).Returns(new OperationDataResult<List<CalendarBoardModel>>(true,
            [new CalendarBoardModel { Id = 1, Name = "Default", Color = "#4caf50", PropertyId = 10 }]));

        var sut = CreateSut(calendarService: calSvc, access: access, resolver: resolver);
        var response = await sut.GetBoards(
            new GetBoardsRequest { PropertyId = 10 }, Substitute.For<ServerCallContext>());

        Assert.That(response.Success, Is.True);
        Assert.That(response.Boards, Has.Count.EqualTo(1));
        Assert.That(response.Boards[0].Name, Is.EqualTo("Default"));
        Assert.That(response.Boards[0].Color, Is.EqualTo("#4caf50"));
    }
}
