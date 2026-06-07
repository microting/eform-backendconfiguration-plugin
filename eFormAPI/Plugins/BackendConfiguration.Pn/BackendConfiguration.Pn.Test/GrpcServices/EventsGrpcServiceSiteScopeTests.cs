using System.Collections.Generic;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Grpc.Events;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.BackendConfigurationPropertiesService;
using BackendConfiguration.Pn.Services.EventDeployService;
using BackendConfiguration.Pn.Services.GrpcServices;
using BackendConfiguration.Pn.Services.UserPropertyAccess;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using NSubstitute;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Test.GrpcServices;

/// <summary>
/// #931 — the mobile event stream (EventsGrpcService) must only surface events
/// assigned to the calling worker's own SDK site, never the whole property.
/// These are pure NSubstitute unit tests: GetTasksForWeek is mocked to return an
/// empty result, so the envelope/field loaders short-circuit before touching any
/// DbContext or SDK core (the null contexts below are therefore never
/// dereferenced). The assertion is on the request model the service forwards to
/// the calendar service — proving the per-site scoping the read relies on.
/// </summary>
[TestFixture]
public class EventsGrpcServiceSiteScopeTests
{
    private static EventsGrpcService CreateSut(
        IBackendConfigurationCalendarService calendarService,
        IGrpcSiteResolver resolver,
        IBackendConfigurationUserPropertyAccess access)
    {
        return new EventsGrpcService(
            calendarService,
            Substitute.For<IBackendConfigurationPropertiesService>(),
            access,
            resolver,
            Substitute.For<IEFormCoreService>(),
            null, // BackendConfigurationPnDbContext — untouched on the empty-result path
            null, // ItemsPlanningPnDbContext — untouched on the empty-result path
            Substitute.For<IEventDeployService>(),
            NullLogger<EventsGrpcService>.Instance);
    }

    [Test]
    public async Task ListEvents_ScopesReadToCallerSite()
    {
        var resolver = Substitute.For<IGrpcSiteResolver>();
        resolver.GetSdkSiteIdAsync().Returns(7);
        var access = Substitute.For<IBackendConfigurationUserPropertyAccess>();
        access.HasAccessAsync(7, 10).Returns(true);

        var calSvc = Substitute.For<IBackendConfigurationCalendarService>();
        CalendarTaskRequestModel captured = null;
        calSvc.GetTasksForWeek(Arg.Do<CalendarTaskRequestModel>(m => captured = m))
            .Returns(new OperationDataResult<List<CalendarTaskResponseModel>>(true, []));

        var sut = CreateSut(calSvc, resolver, access);

        var request = new ListEventsRequest
        {
            PropertyId = "10",
            FromDateKey = "2026-06-04",
            ToDateKey = "2026-06-10"
        };
        await sut.ListEvents(request, Substitute.For<ServerCallContext>());

        Assert.That(captured, Is.Not.Null);
        Assert.That(captured.SiteIds, Is.EquivalentTo(new[] { 7 }),
            "#931: the mobile read must be scoped to the caller's own site (7), "
            + "not left empty (which returns every site's tasks on the property).");
    }
}
