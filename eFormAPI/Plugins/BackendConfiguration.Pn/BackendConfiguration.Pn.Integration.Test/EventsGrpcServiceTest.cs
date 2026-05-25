/*
The MIT License (MIT)

Copyright (c) 2007 - 2026 Microting A/S

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

namespace BackendConfiguration.Pn.Integration.Test;

using System.Threading;
using BackendConfiguration.Pn.Grpc.Events;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.BackendConfigurationPropertiesService;
using BackendConfiguration.Pn.Services.EventDeployService;
using BackendConfiguration.Pn.Services.GrpcServices;
using BackendConfiguration.Pn.Services.UserPropertyAccess;
// Fully qualify Grpc.Core to avoid ambiguity with the
// generated BackendConfiguration.Pn.Grpc.* namespace which shadows the
// short alias inside this test namespace.
using GrpcCore = global::Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using NSubstitute;

/// <summary>
/// Targeted tests for the projection helpers + parsing branches that back the
/// flutter-eform "planned-time" chip on the event card:
///
/// <list type="bullet">
///   <item><description><see cref="EventsGrpcService.ListEvents"/> formats
///     <c>CalendarTaskResponseModel.StartHour</c> (fractional hour-of-day,
///     e.g. 8.5 → 08:30) into <c>Event.PlannedAt</c> as an HH:mm 24-hour
///     string.</description></item>
///   <item><description><c>FormatStartHour</c> private helper handles edge
///     inputs (zero, fractional, NaN, negative, &gt;24) without throwing.</description></item>
/// </list>
///
/// The read-path tests use NSubstitute mocks for every collaborator and pass
/// a <see cref="CalendarTaskResponseModel"/> with <c>SdkCaseId = null</c>, which
/// short-circuits both <c>LoadEnvelopeByTaskIdAsync</c> and
/// <c>LoadFieldsByTaskIdAsync</c> (early-return on empty dictionary) so no
/// real SDK <c>Core</c> / Case row is required.
///
/// The CompleteEvent date-override parsing branch (full ISO datetime vs
/// date-only fallback) is exercised indirectly by manual smoke tests against
/// the running backend; an integration test would require seeding a full
/// AreaRulePlanning + Compliance + SDK Case + PlanningCase graph, plus the
/// CaseUpdateDelegate broadcast plumbing. Tracked as a follow-up.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class EventsGrpcServiceTest : TestBaseSetup
{
    private const string PropertyIdString = "1";
    private const int PropertyIdInt = 1;
    private const int SdkSiteId = 1;

    /// <summary>
    /// Minimal <see cref="GrpcCore.ServerCallContext"/> subclass — the gRPC
    /// service only reads <c>CancellationToken</c>, every other member is
    /// left at safe defaults. Cheaper than pulling in <c>Grpc.Core.Testing</c>
    /// (not currently referenced) just for a context shim.
    /// </summary>
    private sealed class TestServerCallContext : GrpcCore.ServerCallContext
    {
        protected override string MethodCore => "test";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "test-peer";
        protected override System.DateTime DeadlineCore => System.DateTime.MaxValue;
        protected override GrpcCore.Metadata RequestHeadersCore { get; } = new();
        protected override CancellationToken CancellationTokenCore => CancellationToken.None;
        protected override GrpcCore.Metadata ResponseTrailersCore { get; } = new();
        protected override GrpcCore.Status StatusCore { get; set; }
        protected override GrpcCore.WriteOptions? WriteOptionsCore { get; set; }
        protected override GrpcCore.AuthContext AuthContextCore { get; } =
            new(string.Empty, new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<GrpcCore.AuthProperty>>());

        protected override GrpcCore.ContextPropagationToken CreatePropagationTokenCore(GrpcCore.ContextPropagationOptions? options)
            => throw new System.NotSupportedException();

        protected override System.Threading.Tasks.Task WriteResponseHeadersAsyncCore(GrpcCore.Metadata responseHeaders)
            => System.Threading.Tasks.Task.CompletedTask;
    }

    /// <summary>
    /// Builds the SUT with all collaborators substituted. The two real DB
    /// contexts come from <see cref="TestBaseSetup"/> so the constructor's
    /// EF-bound fields are non-null; the test path itself does not write
    /// to either context.
    /// </summary>
    private EventsGrpcService MakeService(
        IBackendConfigurationCalendarService calendar,
        IBackendConfigurationUserPropertyAccess access,
        IGrpcSiteResolver siteResolver,
        IEventDeployService deploy,
        ILogger<EventsGrpcService>? logger = null)
    {
        var properties = Substitute.For<IBackendConfigurationPropertiesService>();
        var coreHelper = Substitute.For<IEFormCoreService>();
        return new EventsGrpcService(
            calendar,
            properties,
            access,
            siteResolver,
            coreHelper,
            BackendConfigurationPnDbContext!,
            ItemsPlanningPnDbContext!,
            deploy,
            logger ?? NullLogger<EventsGrpcService>.Instance);
    }

    /// <summary>
    /// Wires the calendar/site/access/deploy mocks for the ListEvents
    /// read-path tests and returns the fully-built SUT. The single
    /// <paramref name="task"/> is what <c>GetTasksForWeek</c> returns;
    /// every other collaborator is a no-op substitute.
    /// </summary>
    private EventsGrpcService MakeServiceWithTask(CalendarTaskResponseModel task)
    {
        var calendar = Substitute.For<IBackendConfigurationCalendarService>();
        calendar.GetTasksForWeek(Arg.Any<CalendarTaskRequestModel>())
            .Returns(new OperationDataResult<System.Collections.Generic.List<CalendarTaskResponseModel>>(
                true, new System.Collections.Generic.List<CalendarTaskResponseModel> { task }));

        var siteResolver = Substitute.For<IGrpcSiteResolver>();
        siteResolver.GetSdkSiteIdAsync().Returns(System.Threading.Tasks.Task.FromResult(SdkSiteId));

        var access = Substitute.For<IBackendConfigurationUserPropertyAccess>();
        access.HasAccessAsync(SdkSiteId, PropertyIdInt)
            .Returns(System.Threading.Tasks.Task.FromResult(true));

        var deploy = Substitute.For<IEventDeployService>();
        deploy.EnsureDeployedAsync(
                Arg.Any<string>(), Arg.Any<System.Collections.Generic.IReadOnlyCollection<string>>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(System.Threading.Tasks.Task.CompletedTask);

        return MakeService(calendar, access, siteResolver, deploy);
    }

    /// <summary>
    /// Canonical ListEventsRequest literal used by every read-path test
    /// in this fixture. The window spans the same week as the seeded
    /// task's TaskDate.
    /// </summary>
    private static ListEventsRequest MakeListRequest() =>
        new()
        {
            PropertyId = PropertyIdString,
            FromDateKey = "2026-05-18",
            ToDateKey = "2026-05-24"
        };

    /// <summary>
    /// Pins the read-side projection: a calendar task with
    /// <c>StartHour = 8.5</c> surfaces as <c>Event.PlannedAt = "08:30"</c>
    /// on the gRPC wire. Asserts the formatter wired into ListEvents
    /// (and ListTaskTracker via the shared <c>FormatStartHour</c> helper).
    /// </summary>
    [Test]
    public async Task ListEvents_FormatsStartHourIntoPlannedAt()
    {
        // Arrange — one calendar task with StartHour = 8.5 (i.e. 08:30).
        // SdkCaseId is left null so LoadEnvelopeByTaskIdAsync /
        // LoadFieldsByTaskIdAsync both short-circuit and no real SDK
        // Core is required.
        var task = new CalendarTaskResponseModel
        {
            Id = 42,
            Title = "Planned-time test",
            PropertyId = PropertyIdInt,
            BoardId = 7,
            TaskDate = "2026-05-19",
            StartHour = 8.5,
            Completed = false,
            Color = "#abcdef"
        };

        var service = MakeServiceWithTask(task);

        // Act
        var response = await service.ListEvents(MakeListRequest(), new TestServerCallContext());

        // Assert — exactly one event, planned_at formatted from StartHour.
        Assert.Multiple(() =>
        {
            Assert.That(response.Events, Has.Count.EqualTo(1));
            Assert.That(response.Events[0].PlannedAt, Is.EqualTo("08:30"));
            Assert.That(response.Events[0].Id, Is.EqualTo("42"));
        });
    }

    /// <summary>
    /// Pins the FormatStartHour helper's edge-case contract via the
    /// projection: zero, on-the-hour, half-hour, NaN, negative, and
    /// past-24h fractional inputs all render predictably without
    /// throwing. Each row drives a single ListEvents call so the
    /// assertion failures clearly identify which input regressed.
    /// </summary>
    [TestCase(0.0, "00:00")]
    [TestCase(8.0, "08:00")]
    [TestCase(8.5, "08:30")]
    [TestCase(13.25, "13:15")]
    [TestCase(23.99, "23:59")] // rounds to 23:59 (1439 min ≈ 1439.4 → 1439)
    [TestCase(double.NaN, "")]
    [TestCase(double.PositiveInfinity, "")]
    [TestCase(-1.0, "")]
    [TestCase(25.5, "01:30")] // hours wrap modulo 24
    public async Task ListEvents_FormatStartHour_HandlesEdgeCases(double startHour, string expected)
    {
        var task = new CalendarTaskResponseModel
        {
            Id = 1,
            Title = "edge",
            PropertyId = PropertyIdInt,
            TaskDate = "2026-05-19",
            StartHour = startHour
        };

        var service = MakeServiceWithTask(task);

        var response = await service.ListEvents(MakeListRequest(), new TestServerCallContext());

        Assert.That(response.Events, Has.Count.EqualTo(1));
        Assert.That(response.Events[0].PlannedAt, Is.EqualTo(expected));
    }

    [Test]
    public async Task PopulateCalendarAttachments_LoadsRowsForPlanning()
    {
        // Arrange: seed an AreaRulePlanning + 2 AreaRulePlanningFile rows, one
        // active and one soft-removed. The helper should return only the active
        // row, projected with id/originalFileName/mimeType/sizeBytes set.
        var area = new Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.Area
        {
            Type = Microting.EformBackendConfigurationBase.Infrastructure.Enum.AreaTypesEnum.Type1,
            ItemPlanningTagId = 0,
            WorkflowState = Microting.eForm.Infrastructure.Constants.Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1,
        };
        BackendConfigurationPnDbContext!.Areas.Add(area);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        // AreaRule requires a Property FK reference
        var property = new Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.Property
        {
            Name = "Test Property",
            WorkflowState = Microting.eForm.Infrastructure.Constants.Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1,
        };
        BackendConfigurationPnDbContext.Properties.Add(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var rule = new Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.AreaRule
        {
            AreaId = area.Id,
            PropertyId = property.Id,
            EformName = "x",
            WorkflowState = Microting.eForm.Infrastructure.Constants.Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1,
        };
        BackendConfigurationPnDbContext.AreaRules.Add(rule);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var planning = new Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.AreaRulePlanning
        {
            AreaRuleId = rule.Id,
            WorkflowState = Microting.eForm.Infrastructure.Constants.Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1,
        };
        BackendConfigurationPnDbContext.AreaRulePlannings.Add(planning);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var active = new Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.AreaRulePlanningFile
        {
            AreaRulePlanningId = planning.Id,
            OriginalFileName = "report.pdf",
            MimeType = "application/pdf",
            SizeBytes = 12345,
            WorkflowState = Microting.eForm.Infrastructure.Constants.Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1,
        };
        var removed = new Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.AreaRulePlanningFile
        {
            AreaRulePlanningId = planning.Id,
            OriginalFileName = "old.pdf",
            MimeType = "application/pdf",
            SizeBytes = 99,
            WorkflowState = Microting.eForm.Infrastructure.Constants.Constants.WorkflowStates.Removed,
            CreatedByUserId = 1,
            UpdatedByUserId = 1,
        };
        BackendConfigurationPnDbContext.AreaRulePlanningFiles.AddRange(active, removed);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var proto = new BackendConfiguration.Pn.Grpc.Events.Event();

        // Act
        await EventsGrpcService.PopulateCalendarAttachments(
            proto, planning.Id, BackendConfigurationPnDbContext, CancellationToken.None);

        // Assert
        Assert.That(proto.Attachments, Has.Count.EqualTo(1));
        var a = proto.Attachments[0];
        Assert.That(a.Source, Is.EqualTo(
            BackendConfiguration.Pn.Grpc.Documents.AttachmentSource.CalendarFile));
        Assert.That(a.Id, Is.EqualTo(active.Id));
        Assert.That(a.OriginalFileName, Is.EqualTo("report.pdf"));
        Assert.That(a.MimeType, Is.EqualTo("application/pdf"));
        Assert.That(a.SizeBytes, Is.EqualTo(12345));
        Assert.That(a.Name, Is.EqualTo("report.pdf"));
    }
}
