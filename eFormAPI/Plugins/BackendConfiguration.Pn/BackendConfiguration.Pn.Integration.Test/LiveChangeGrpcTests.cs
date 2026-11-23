#nullable enable

namespace BackendConfiguration.Pn.Integration.Test;

using System.Globalization;
using System.Threading;
using BackendConfiguration.Pn.Grpc.Events;
using BackendConfiguration.Pn.Hubs;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.BackendConfigurationPropertiesService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService;
using BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;
using BackendConfiguration.Pn.Services.CalendarChangeNotification;
using BackendConfiguration.Pn.Services.EventDeployService;
using BackendConfiguration.Pn.Services.GrpcServices;
using BackendConfiguration.Pn.Services.LiveChanges;
using BackendConfiguration.Pn.Services.UserPropertyAccess;
using BackendConfiguration.Pn.Services.WorkerTagMembership;
using eFormCore;
using Google.Protobuf;
using GrpcCore = global::Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.eFormApi.BasePn.Infrastructure.Models.Common;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.ItemsPlanningBase.Infrastructure.Data;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Enums;
using NSubstitute;
using BcPlanningSite = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.PlanningSite;
using BcProperty = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.Property;
using Compliance = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.Compliance;
using IpPlanningSite = Microting.ItemsPlanningBase.Infrastructure.Data.Entities.PlanningSite;
using SdkCase = Microting.eForm.Infrastructure.Data.Entities.Case;

/// <summary>
/// Live hints around the mobile gRPC service, on the real calendar and the
/// real deploy service:
/// <list type="bullet">
///   <item>the read paths (ListEvents, ListChanges, a StreamEventChanges tick, GetEvent) deploy rows
///     through the INTERCEPTED pooled contexts and still announce nothing;</item>
///   <item>the writes that touch SDK tables only (SetComment, UploadPhoto, RemovePhoto, SetFieldValue)
///     announce their key explicitly, and a replay announces nothing.</item>
/// </list>
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class LiveChangeGrpcTests : TestBaseSetup
{
    private const string TemplateXml = @"
<?xml version='1.0' encoding='UTF-8'?>
<Main>
    <Id>9096</Id>
    <Repeated>0</Repeated>
    <Label>LiveMain</Label>
    <StartDate>2017-07-07</StartDate>
    <EndDate>2037-07-07</EndDate>
    <Language>da</Language>
    <MultiApproval>false</MultiApproval>
    <FastNavigation>false</FastNavigation>
    <Review>false</Review>
    <Summary>false</Summary>
    <DisplayOrder>0</DisplayOrder>
    <ElementList>
        <Element type='DataElement'>
            <Id>9096</Id>
            <Label>LiveElement</Label>
            <Description><![CDATA[]]></Description>
            <DisplayOrder>0</DisplayOrder>
            <ReviewEnabled>false</ReviewEnabled>
            <ManualSync>false</ManualSync>
            <ExtraFieldsEnabled>false</ExtraFieldsEnabled>
            <DoneButtonDisabled>false</DoneButtonDisabled>
            <ApprovalEnabled>false</ApprovalEnabled>
            <DataItemList>
                <DataItem type='Picture'>
                    <Id>73790</Id>
                    <Label>PictureField</Label>
                    <Description><![CDATA[]]></Description>
                    <DisplayOrder>0</DisplayOrder>
                    <Mandatory>false</Mandatory>
                    <Color>e8eaf6</Color>
                </DataItem>
                <DataItem type='Text'>
                    <Id>73791</Id>
                    <Label>TextField</Label>
                    <Description><![CDATA[]]></Description>
                    <DisplayOrder>1</DisplayOrder>
                    <Multi>0</Multi>
                    <GeolocationEnabled>false</GeolocationEnabled>
                    <Split>false</Split>
                    <Value/>
                    <ReadOnly>false</ReadOnly>
                    <Mandatory>false</Mandatory>
                    <Color>e8eaf6</Color>
                </DataItem>
            </DataItemList>
        </Element>
    </ElementList>
</Main>";

    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private sealed class TestServerCallContext(CancellationToken token = default) : GrpcCore.ServerCallContext
    {
        protected override string MethodCore => "test";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "test-peer";
        protected override DateTime DeadlineCore => DateTime.MaxValue;
        protected override GrpcCore.Metadata RequestHeadersCore => new();
        protected override CancellationToken CancellationTokenCore => token;
        protected override GrpcCore.Metadata ResponseTrailersCore => new();
        protected override GrpcCore.Status StatusCore { get; set; }
        protected override GrpcCore.WriteOptions? WriteOptionsCore { get; set; }
        protected override GrpcCore.AuthContext AuthContextCore =>
            new(string.Empty, new Dictionary<string, List<GrpcCore.AuthProperty>>());
        protected override GrpcCore.ContextPropagationToken CreatePropagationTokenCore(
            GrpcCore.ContextPropagationOptions? options) => throw new NotSupportedException();
        protected override Task WriteResponseHeadersAsyncCore(GrpcCore.Metadata responseHeaders) =>
            Task.CompletedTask;
    }

    private sealed class ListStreamReader<T>(IEnumerable<T> items) : GrpcCore.IAsyncStreamReader<T> where T : class
    {
        private readonly Queue<T> _items = new(items);
        public T Current { get; private set; } = null!;

        public Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            if (_items.Count == 0) return Task.FromResult(false);
            Current = _items.Dequeue();
            return Task.FromResult(true);
        }
    }

    private sealed class CountingStreamWriter<T> : GrpcCore.IServerStreamWriter<T>
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);
        public GrpcCore.WriteOptions? WriteOptions { get; set; }

        public Task WriteAsync(T message)
        {
            Interlocked.Increment(ref _count);
            return Task.CompletedTask;
        }

        public Task WriteAsync(T message, CancellationToken cancellationToken) => WriteAsync(message);
    }

    private sealed record World(Core Core, Language Language, int SiteId, int AreaId, int TemplateId, int PropertyId);

    private sealed record Series(int ArpId, int PlanningId);

    private int _templateId;

    private static string Key(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private async Task<World> SeedWorldAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var core = await GetCore();
        var language = await MicrotingDbContext!.Languages.FirstAsync();
        if (_templateId == 0) _templateId = await core.TemplateCreate(await core.TemplateFromXml(TemplateXml));

        var site = new Site
        {
            Name = $"live-worker-{suffix}", MicrotingUid = Random.Shared.Next(500000, 999999),
            LanguageId = language.Id, WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddAsync(site);
        await MicrotingDbContext.SaveChangesAsync();

        var area = new Area
        {
            Type = AreaTypesEnum.Type1, ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Areas.AddAsync(area);
        var property = new BcProperty
        {
            Name = $"live-{suffix}", ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.Properties.AddAsync(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        await BackendConfigurationPnDbContext.PropertyWorkers.AddAsync(new PropertyWorker
        {
            PropertyId = property.Id, WorkerId = site.Id,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        return new World(core, language, site.Id, area.Id, _templateId, property.Id);
    }

    /// <summary>A daily eForm series assigned to the world's site (plugin and items-planning PlanningSite).</summary>
    private async Task<Series> SeedDailySeriesAsync(World w, DateTime start)
    {
        var bc = BackendConfigurationPnDbContext!;
        var areaRule = new AreaRule
        {
            AreaId = w.AreaId, PropertyId = w.PropertyId, EformId = w.TemplateId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await bc.AreaRules.AddAsync(areaRule);
        await bc.SaveChangesAsync();
        await bc.AreaRuleTranslations.AddAsync(new AreaRuleTranslation
        {
            AreaRuleId = areaRule.Id, LanguageId = w.Language.Id, Name = "live-daily",
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        var planning = new Planning
        {
            Enabled = true, RepeatEvery = 1, RepeatType = RepeatType.Day, StartDate = start,
            DayOfWeek = start.DayOfWeek, RelatedEFormId = w.TemplateId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();
        await ItemsPlanningPnDbContext.PlanningSites.AddAsync(new IpPlanningSite
        {
            PlanningId = planning.Id, SiteId = w.SiteId, WorkflowState = Constants.WorkflowStates.Created
        });
        await ItemsPlanningPnDbContext.SaveChangesAsync();
        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = w.PropertyId, AreaId = w.AreaId, ItemPlanningId = planning.Id,
            StartDate = start, Status = true, RepeatType = 1, RepeatEvery = 1, DayOfWeek = (int)start.DayOfWeek,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await bc.AreaRulePlannings.AddAsync(arp);
        await bc.SaveChangesAsync();
        await bc.PlanningSites.AddAsync(new BcPlanningSite
        {
            AreaRulePlanningsId = arp.Id, SiteId = w.SiteId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await bc.CalendarConfigurations.AddAsync(new CalendarConfiguration
        {
            AreaRulePlanningId = arp.Id, StartHour = 9.0, Duration = 1.0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await bc.SaveChangesAsync();
        return new Series(arp.Id, planning.Id);
    }

    /// <summary>A deployed, open occurrence (SDK case + compliance) on <paramref name="day"/>.</summary>
    private async Task<(int ComplianceId, int CaseId)> SeedOccurrenceAsync(World w, Series series, DateTime day)
    {
        var sdkCase = new SdkCase
        {
            SiteId = w.SiteId, CheckListId = w.TemplateId, Status = 66, MicrotingUid = null,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext!.Cases.AddAsync(sdkCase);
        await MicrotingDbContext.SaveChangesAsync();
        await w.Core.CaseRead(sdkCase.Id, w.Language);
        var compliance = new Compliance
        {
            PlanningId = series.PlanningId, PropertyId = w.PropertyId, AreaId = w.AreaId,
            Deadline = day, StartDate = day.AddDays(-7),
            MicrotingSdkCaseId = sdkCase.Id, MicrotingSdkeFormId = w.TemplateId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Compliances.AddAsync(compliance);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        return (compliance.Id, sdkCase.Id);
    }

    private static BackendConfigurationCalendarService NewCalendar(
        World w, BackendConfigurationPnDbContext bc, ItemsPlanningPnDbContext ip)
    {
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(w.Core));
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        userService.GetCurrentUserLanguage().Returns(Task.FromResult(w.Language));
        var taskWizardService = Substitute.For<IBackendConfigurationTaskWizardService>();
        return new BackendConfigurationCalendarService(
            new BackendConfigurationLocalizationService(), userService,
            bc, coreHelper, Substitute.For<IEventDeployService>(),
            ip, taskWizardService,
            Substitute.For<ICalendarAssignmentReconciliationService>(),
            Substitute.For<ICalendarChangeNotifier>(),
            TestContextLogger<BackendConfigurationCalendarService>.Instance,
            Substitute.For<ICalendarOccurrenceRetractionService>(),
            Substitute.For<ICalendarPastSeriesBackfillService>(),
            new WorkerTagMembershipService(coreHelper, bc));
    }

    /// <summary>The service as the world's worker sees it, on the given contexts, with the real deploy pass.</summary>
    private static EventsGrpcService ServiceFor(World w, BackendConfigurationPnDbContext bc, ItemsPlanningPnDbContext ip,
        IServiceProvider services, ILiveChanges? liveChanges)
    {
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(w.Core));
        var siteResolver = Substitute.For<IGrpcSiteResolver>();
        siteResolver.GetSdkSiteIdAsync().Returns(Task.FromResult(w.SiteId));
        siteResolver.GetSiteLanguageIdAsync(Arg.Any<int?>()).Returns(Task.FromResult<int?>(w.Language.Id));
        var properties = Substitute.For<IBackendConfigurationPropertiesService>();
        properties.GetCommonDictionary(false).Returns(new OperationDataResult<List<CommonDictionaryModel>>(true,
            [new CommonDictionaryModel { Id = w.PropertyId, Name = "P" }]));
        var deploy = new EventDeployService(bc, ip, coreHelper, services, TestContextLogger<EventDeployService>.Instance);
        return new EventsGrpcService(
            NewCalendar(w, bc, ip), properties, new BackendConfigurationUserPropertyAccess(bc), siteResolver,
            coreHelper, bc, ip, deploy, new RecordingLogger<EventsGrpcService>(), liveChanges: liveChanges)
        {
            PutFileToS3StorageAsync = (_, _, _) => Task.CompletedTask
        };
    }

    /// <summary>The host's DI: pooled intercepted contexts plus the calendar the deploy service resolves.</summary>
    private LiveChangeTestHost NewHost(World w) =>
        new(ConnectionStringFor("420_eform-backend-configuration-plugin"),
            ConnectionStringFor("420_eform-angular-items-planning-plugin"), w.Core,
            s => s.AddScoped<IBackendConfigurationCalendarService>(sp => NewCalendar(w,
                sp.GetRequiredService<BackendConfigurationPnDbContext>(),
                sp.GetRequiredService<ItemsPlanningPnDbContext>())));

    private EventsGrpcService InScope(World w, IServiceProvider sp) =>
        ServiceFor(w, sp.GetRequiredService<BackendConfigurationPnDbContext>(),
            sp.GetRequiredService<ItemsPlanningPnDbContext>(), sp, sp.GetRequiredService<ILiveChanges>());

    private Task<int> CompliancesOfAsync(Series s) =>
        BackendConfigurationPnDbContext!.Compliances.AsNoTracking().CountAsync(x => x.PlanningId == s.PlanningId);

    // ------------------------------------------------------------------
    // Read paths: they deploy, and say nothing.
    // ------------------------------------------------------------------

    [Test]
    public async Task ListEvents_ThatDeploys_SendsNoHint()
    {
        var w = await SeedWorldAsync();
        var today = DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);
        var series = await SeedDailySeriesAsync(w, today.AddDays(-3));
        using var host = NewHost(w);

        var calls = await host.InRequestAsync(async sp => await InScope(w, sp).ListEvents(new ListEventsRequest
        {
            PropertyId = w.PropertyId.ToString(CultureInfo.InvariantCulture),
            FromDateKey = Key(today), ToDateKey = Key(today.AddDays(2))
        }, new TestServerCallContext()));

        Assert.That(await CompliancesOfAsync(series), Is.GreaterThan(0), "the read path did deploy");
        Assert.That(calls, Is.Empty);
    }

    [Test]
    public async Task ListChanges_ThatDeploys_SendsNoHint()
    {
        var w = await SeedWorldAsync();
        var today = DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);
        var series = await SeedDailySeriesAsync(w, today.AddDays(-3));
        using var host = NewHost(w);

        var calls = await host.InRequestAsync(async sp => await InScope(w, sp).ListChanges(new ListChangesRequest
        {
            FromDateKey = Key(today.AddDays(1)), ToDateKey = Key(today.AddDays(3)), Cursor = ByteString.Empty
        }, new TestServerCallContext()));

        Assert.That(await CompliancesOfAsync(series), Is.GreaterThan(0), "the read path did deploy");
        Assert.That(calls, Is.Empty);
    }

    [Test]
    public async Task AStreamTick_ThatDeploys_SendsNoHint()
    {
        var w = await SeedWorldAsync();
        var today = DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);
        var series = await SeedDailySeriesAsync(w, today.AddDays(-3));
        using var host = NewHost(w);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var calls = await host.InRequestAsync(async sp =>
        {
            var stream = InScope(w, sp).StreamEventChanges(new StreamEventChangesRequest
            {
                PropertyId = w.PropertyId.ToString(CultureInfo.InvariantCulture)
            }, new CountingStreamWriter<EventChange>(), new TestServerCallContext(cts.Token));

            // The first poll (5 s after the snapshot) runs the deploy.
            while (await CompliancesOfAsync(series) == 0 && !cts.IsCancellationRequested)
            {
                await Task.Delay(250);
            }

            await host.Publisher.WhenIdleAsync();
            Assert.That(host.Notifier.Calls, Is.Empty, "nothing while the stream runs");
            await cts.CancelAsync();
            try
            {
                await stream;
            }
            catch (OperationCanceledException)
            {
            }
        });

        Assert.That(await CompliancesOfAsync(series), Is.GreaterThan(0), "the tick did deploy");
        Assert.That(calls, Is.Empty);
    }

    [Test]
    public async Task ResizingATaskForAll_WhichWritesOnlyItsCalendarConfiguration_IsAnnounced()
    {
        // PR #1525 review: ResizeTask scope "all" with no occurrence exceptions writes
        // CalendarConfiguration only.
        var w = await SeedWorldAsync();
        var today = DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);
        var series = await SeedDailySeriesAsync(w, today.AddDays(-3));
        using var host = NewHost(w);

        var calls = await host.InRequestAsync(async sp =>
        {
            var calendar = NewCalendar(w, sp.GetRequiredService<BackendConfigurationPnDbContext>(),
                sp.GetRequiredService<ItemsPlanningPnDbContext>());
            var result = await calendar.ResizeTask(new BackendConfiguration.Pn.Infrastructure.Models.Calendar.CalendarTaskResizeRequestModel
            {
                Id = series.ArpId, NewStartHour = 11, NewDuration = 2, Scope = "all"
            });
            Assert.That(result.Success, Is.True, result.Message);
        });

        Assert.That(calls, Is.EqualTo(new[]
        {
            new RecordingLiveNotifier.Call("TaskChanged", w.PropertyId, series.ArpId, null, null, TaskChangeKinds.Changed)
        }));
    }

    [Test]
    public async Task GetEvent_SendsNoHint()
    {
        var w = await SeedWorldAsync();
        var day = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(1), DateTimeKind.Utc);
        var series = await SeedDailySeriesAsync(w, day.AddDays(-3));
        var (complianceId, _) = await SeedOccurrenceAsync(w, series, day);
        using var host = NewHost(w);

        var calls = await host.InRequestAsync(async sp => await InScope(w, sp).GetEvent(new GetEventRequest
        {
            Key = new EventKey
            {
                EventId = series.ArpId.ToString(CultureInfo.InvariantCulture), PlanDayKey = Key(day),
                ComplianceId = complianceId
            }
        }, new TestServerCallContext()));

        Assert.That(calls, Is.Empty);
    }

    [Test]
    public async Task OutsideTheReadPath_TheSameRequestScopeStillAnnounces()
    {
        // Guards the suppression from being wider than the deploy pass: a write in
        // the same request after ListEvents returned is announced.
        var w = await SeedWorldAsync();
        var today = DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);
        var series = await SeedDailySeriesAsync(w, today.AddDays(-3));
        using var host = NewHost(w);

        var calls = await host.InRequestAsync(async sp =>
        {
            await InScope(w, sp).ListEvents(new ListEventsRequest
            {
                PropertyId = w.PropertyId.ToString(CultureInfo.InvariantCulture),
                FromDateKey = Key(today), ToDateKey = Key(today.AddDays(2))
            }, new TestServerCallContext());
            var bc = sp.GetRequiredService<BackendConfigurationPnDbContext>();
            var arp = await bc.AreaRulePlannings.SingleAsync(x => x.Id == series.ArpId);
            arp.SendNotifications = !arp.SendNotifications;
            await arp.Update(bc);
        });

        Assert.That(calls, Is.EqualTo(new[]
        {
            new RecordingLiveNotifier.Call("TaskChanged", w.PropertyId, series.ArpId, null, null, TaskChangeKinds.Changed)
        }));
    }

    [Test]
    public async Task CompleteEvent_IsAnnouncedThroughItsRows_AndTheRetapReplayIsNot()
    {
        // CompleteEvent writes Compliance / PlanningCase / PlanningCaseSite through the
        // request's contexts, so the interceptor covers it; it needs no explicit call.
        var w = await SeedWorldAsync();
        var day = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(1), DateTimeKind.Utc);
        var series = await SeedDailySeriesAsync(w, day.AddDays(-3));
        var (complianceId, caseId) = await SeedOccurrenceAsync(w, series, day);
        using var host = NewHost(w);
        CompleteEventRequest Request(bool completed) => new()
        {
            EventId = series.ArpId.ToString(CultureInfo.InvariantCulture), Completed = completed,
            CompletedBy = "Jane", ClientTsUnix = 1700000600, ComplianceId = complianceId, MicrotingSdkCaseId = caseId
        };

        var completed = await host.InRequestAsync(async sp =>
            await InScope(w, sp).CompleteEvent(Request(true), new TestServerCallContext()));
        var retap = await host.InRequestAsync(async sp =>
            await InScope(w, sp).CompleteEvent(Request(false), new TestServerCallContext()));

        Assert.That(completed, Has.Count.EqualTo(1));
        Assert.That(completed[0], Has.Property("Method").EqualTo("TaskChanged")
            .And.Property("PropertyId").EqualTo(w.PropertyId)
            .And.Property("EventId").EqualTo(series.ArpId));
        Assert.That(retap, Is.Empty, "a re-tap writes no plugin row and announces nothing");
    }

    // ------------------------------------------------------------------
    // Writes to SDK tables only: explicit hints with the reply's key.
    // ------------------------------------------------------------------

    private async Task<(World W, Series S, DateTime Day, int ComplianceId, int CaseId, ILiveChanges Live, EventsGrpcService Service)>
        SeedOpenOccurrenceAsync()
    {
        var w = await SeedWorldAsync();
        var day = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(1), DateTimeKind.Utc);
        var series = await SeedDailySeriesAsync(w, day.AddDays(-3));
        var (complianceId, caseId) = await SeedOccurrenceAsync(w, series, day);
        var live = Substitute.For<ILiveChanges>();
        var service = ServiceFor(w, BackendConfigurationPnDbContext!, ItemsPlanningPnDbContext!,
            new ServiceCollection().BuildServiceProvider(), live);
        return (w, series, day, complianceId, caseId, live, service);
    }

    [Test]
    public async Task SetComment_WithARequestId_AnnouncesOnce_AndItsReplayAnnouncesNothing()
    {
        var o = await SeedOpenOccurrenceAsync();
        var request = new SetCommentRequest
        {
            EventId = o.S.ArpId.ToString(CultureInfo.InvariantCulture), Text = "hello", ClientTsUnix = 1700000100,
            ComplianceId = o.ComplianceId, MicrotingSdkCaseId = o.CaseId, RequestId = Guid.NewGuid().ToString()
        };

        var first = await o.Service.SetComment(request, new TestServerCallContext());
        o.Live.Received(1).TaskChanged(o.W.PropertyId, o.S.ArpId, DateOnly.FromDateTime(o.Day), o.ComplianceId,
            TaskChangeKinds.Changed);
        o.Live.ClearReceivedCalls();
        var replay = await o.Service.SetComment(request, new TestServerCallContext());

        Assert.That(first.Replayed, Is.False);
        Assert.That(replay.Replayed, Is.True);
        o.Live.DidNotReceiveWithAnyArgs().TaskChanged(default);
    }

    [Test]
    public async Task SetComment_Legacy_AnnouncesPropertyEventDayAndCompliance()
    {
        var o = await SeedOpenOccurrenceAsync();

        await o.Service.SetComment(new SetCommentRequest
        {
            EventId = o.S.ArpId.ToString(CultureInfo.InvariantCulture), Text = "hello", ClientTsUnix = 1700000100,
            ComplianceId = o.ComplianceId, MicrotingSdkCaseId = o.CaseId
        }, new TestServerCallContext());

        o.Live.Received(1).TaskChanged(o.W.PropertyId, o.S.ArpId, DateOnly.FromDateTime(o.Day), o.ComplianceId,
            TaskChangeKinds.Changed);
    }

    [Test]
    public async Task SetFieldValue_AnnouncesTheFullKey()
    {
        var o = await SeedOpenOccurrenceAsync();
        var got = await o.Service.GetEvent(new GetEventRequest
        {
            Key = new EventKey
            {
                EventId = o.S.ArpId.ToString(CultureInfo.InvariantCulture), PlanDayKey = Key(o.Day),
                ComplianceId = o.ComplianceId
            }
        }, new TestServerCallContext());
        var textFieldId = got.Event.Fields.Single(f => f.Label == "TextField").Id;
        o.Live.ClearReceivedCalls();

        await o.Service.SetFieldValue(new SetFieldValueRequest
        {
            EventId = o.S.ArpId.ToString(CultureInfo.InvariantCulture), FieldId = textFieldId, Value = "v",
            ClientTsUnix = 1700000200, ComplianceId = o.ComplianceId, MicrotingSdkCaseId = o.CaseId
        }, new TestServerCallContext());

        o.Live.Received(1).TaskChanged(o.W.PropertyId, o.S.ArpId, DateOnly.FromDateTime(o.Day), o.ComplianceId,
            TaskChangeKinds.Changed);
    }

    [Test]
    public async Task UploadAndRemovePhoto_AnnounceEventAndCompliance_AndARemoveOfNothingIsSilent()
    {
        var o = await SeedOpenOccurrenceAsync();
        var eventId = o.S.ArpId.ToString(CultureInfo.InvariantCulture);

        await o.Service.UploadPhoto(new ListStreamReader<UploadPhotoChunk>(
        [
            new UploadPhotoChunk
            {
                Meta = new UploadPhotoMeta
                {
                    EventId = eventId, Slot = 0, ContentType = "image/png", ClientTsUnix = 1700000300,
                    ComplianceId = o.ComplianceId, MicrotingSdkCaseId = o.CaseId
                }
            },
            new UploadPhotoChunk { Chunk = ByteString.CopyFrom(OnePixelPng) }
        ]), new TestServerCallContext());
        o.Live.Received(1).TaskChanged(o.W.PropertyId, o.S.ArpId, null, o.ComplianceId, TaskChangeKinds.Changed);
        o.Live.ClearReceivedCalls();

        await o.Service.RemovePhoto(new RemovePhotoRequest
        {
            EventId = eventId, Slot = 0, ClientTsUnix = 1700000400,
            ComplianceId = o.ComplianceId, MicrotingSdkCaseId = o.CaseId
        }, new TestServerCallContext());
        o.Live.Received(1).TaskChanged(o.W.PropertyId, o.S.ArpId, null, o.ComplianceId, TaskChangeKinds.Changed);
        o.Live.ClearReceivedCalls();

        await o.Service.RemovePhoto(new RemovePhotoRequest
        {
            EventId = eventId, Slot = 0, ClientTsUnix = 1700000500,
            ComplianceId = o.ComplianceId, MicrotingSdkCaseId = o.CaseId
        }, new TestServerCallContext());
        o.Live.DidNotReceiveWithAnyArgs().TaskChanged(default);
    }
}
