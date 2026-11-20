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
*/

namespace BackendConfiguration.Pn.Integration.Test;

using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using BackendConfiguration.Pn.Grpc.Events;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.BackendConfigurationPropertiesService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService;
using BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;
using BackendConfiguration.Pn.Services.CalendarChangeNotification;
using BackendConfiguration.Pn.Services.EventDeployService;
using BackendConfiguration.Pn.Services.GrpcServices;
using BackendConfiguration.Pn.Services.UserPropertyAccess;
using BackendConfiguration.Pn.Services.WorkerTagMembership;
using eFormCore;
using Google.Protobuf;
using GrpcCore = global::Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
using SdkCase = Microting.eForm.Infrastructure.Data.Entities.Case;
using Compliance = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.Compliance;
using BcProperty = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.Property;
using BcPlanningSite = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.PlanningSite;

/// <summary>
/// <c>ListChanges</c> (contract_version 2) against the REAL
/// <see cref="BackendConfigurationCalendarService"/> and the real
/// <see cref="BackendConfigurationUserPropertyAccess"/>: one worker assigned
/// to two properties (P1, P2), a third property (P3) they are not assigned to
/// although one of its tasks names their site, and a second worker (B) on P1.
/// Window Monday..Wednesday of next week.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class EventsGrpcListChangesTests : TestBaseSetup
{
    private const string TemplateXml = @"
<?xml version='1.0' encoding='UTF-8'?>
<Main>
    <Id>9095</Id>
    <Repeated>0</Repeated>
    <Label>ListChangesMain</Label>
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
            <Id>9095</Id>
            <Label>ListChangesElement</Label>
            <Description><![CDATA[]]></Description>
            <DisplayOrder>0</DisplayOrder>
            <ReviewEnabled>false</ReviewEnabled>
            <ManualSync>false</ManualSync>
            <ExtraFieldsEnabled>false</ExtraFieldsEnabled>
            <DoneButtonDisabled>false</DoneButtonDisabled>
            <ApprovalEnabled>false</ApprovalEnabled>
            <DataItemList>
                <DataItem type='Picture'>
                    <Id>73690</Id>
                    <Label>PictureField</Label>
                    <Description><![CDATA[]]></Description>
                    <DisplayOrder>0</DisplayOrder>
                    <Mandatory>false</Mandatory>
                    <Color>e8eaf6</Color>
                </DataItem>
                <DataItem type='Text'>
                    <Id>73691</Id>
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

    /// <summary>A valid 1x1 PNG.</summary>
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

    private sealed class ListStreamReader<T>(IEnumerable<T> items) : GrpcCore.IAsyncStreamReader<T>
        where T : class
    {
        private readonly Queue<T> _items = new(items);
        public T Current { get; private set; } = null!;

        public Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            if (_items.Count == 0)
            {
                return Task.FromResult(false);
            }

            Current = _items.Dequeue();
            return Task.FromResult(true);
        }
    }

    private sealed class RecordingStreamWriter<T> : GrpcCore.IServerStreamWriter<T>
    {
        private readonly object _gate = new();
        private readonly List<T> _written = [];

        public List<T> Snapshot()
        {
            lock (_gate)
            {
                return _written.ToList();
            }
        }

        public GrpcCore.WriteOptions? WriteOptions { get; set; }

        public Task WriteAsync(T message)
        {
            lock (_gate)
            {
                _written.Add(message);
            }

            return Task.CompletedTask;
        }

        public Task WriteAsync(T message, CancellationToken cancellationToken) => WriteAsync(message);
    }

    /// <summary>Counts every SQL command a context runs.</summary>
    private sealed class CommandCounter : DbCommandInterceptor
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);
        public void Reset() => Interlocked.Exchange(ref _count, 0);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Interlocked.Increment(ref _count);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed record World(
        Core Core,
        Language Language,
        int SiteA,
        int SiteB,
        int AreaId,
        int TemplateId,
        int P1,
        int P2,
        int P3,
        IEventDeployService Deploy,
        IBackendConfigurationPropertiesService Properties);

    private sealed record Series(int ArpId, int PlanningId, int PropertyId);

    private sealed record Occurrence(Series Series, int ComplianceId, int CaseId, DateTime Day)
    {
        public OccurrenceKey Key => new(Series.ArpId, Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ComplianceId);
    }

    /// <summary>The seeded calendar: P1 weekly Mon + daily Mon..Wed, P2 weekly Tue + weekly Wed.</summary>
    private sealed record Calendar(
        World W,
        DateTime Monday,
        Occurrence P1Weekly,
        Occurrence[] P1Daily,
        Occurrence P2Tuesday,
        Occurrence P2Wednesday,
        Occurrence P3ForSiteA,
        Occurrence P1ForSiteB)
    {
        public string From => Key(Monday);
        public string To => Key(Monday.AddDays(2));
        public IEnumerable<Occurrence> Visible => new[] { P1Weekly }.Concat(P1Daily).Append(P2Tuesday).Append(P2Wednesday);
    }

    private static string Key(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateTime NextMonday()
    {
        var today = DateTime.UtcNow.Date;
        var days = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        return DateTime.SpecifyKind(today.AddDays(days == 0 ? 7 : days), DateTimeKind.Utc);
    }

    private int _templateId;

    // ------------------------------------------------------------------
    // Seeding
    // ------------------------------------------------------------------

    private async Task<World> SeedWorldAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var core = await GetCore();
        var language = await MicrotingDbContext!.Languages.FirstAsync();
        if (_templateId == 0)
        {
            _templateId = await core.TemplateCreate(await core.TemplateFromXml(TemplateXml));
        }

        async Task<int> SiteAsync(string name)
        {
            var site = new Site
            {
                Name = $"{name}-{suffix}", MicrotingUid = Random.Shared.Next(500000, 999999),
                LanguageId = language.Id, WorkflowState = Constants.WorkflowStates.Created
            };
            await MicrotingDbContext.Sites.AddAsync(site);
            await MicrotingDbContext.SaveChangesAsync();
            return site.Id;
        }

        async Task<int> PropertyAsync(string name)
        {
            var property = new BcProperty
            {
                Name = $"{name}-{suffix}", ItemPlanningTagId = 0,
                WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
            };
            await BackendConfigurationPnDbContext!.Properties.AddAsync(property);
            await BackendConfigurationPnDbContext.SaveChangesAsync();
            return property.Id;
        }

        var siteA = await SiteAsync("worker-a");
        var siteB = await SiteAsync("worker-b");

        var area = new Area
        {
            Type = AreaTypesEnum.Type1, ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Areas.AddAsync(area);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var p1 = await PropertyAsync("p1");
        var p2 = await PropertyAsync("p2");
        var p3 = await PropertyAsync("p3");
        await AssignAsync(siteA, p1);
        await AssignAsync(siteA, p2);
        await AssignAsync(siteB, p1);

        var properties = Substitute.For<IBackendConfigurationPropertiesService>();
        properties.GetCommonDictionary(false).Returns(new OperationDataResult<List<CommonDictionaryModel>>(true,
        [
            new CommonDictionaryModel { Id = p1, Name = "P1" },
            new CommonDictionaryModel { Id = p2, Name = "P2" },
            new CommonDictionaryModel { Id = p3, Name = "P3" }
        ]));

        return new World(core, language, siteA, siteB, area.Id, _templateId, p1, p2, p3,
            Substitute.For<IEventDeployService>(), properties);
    }

    private async Task AssignAsync(int siteId, int propertyId)
    {
        await BackendConfigurationPnDbContext!.PropertyWorkers.AddAsync(new PropertyWorker
        {
            PropertyId = propertyId, WorkerId = siteId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();
    }

    private BackendConfigurationCalendarService NewCalendar(
        World w, BackendConfigurationPnDbContext bc, ItemsPlanningPnDbContext ip)
    {
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(w.Core));
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        userService.GetCurrentUserLanguage().Returns(Task.FromResult(w.Language));
        var taskWizardService = Substitute.For<IBackendConfigurationTaskWizardService>();
        taskWizardService.DeleteTask(Arg.Any<int>()).Returns(Task.FromResult(new OperationResult(true)));
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

    /// <summary>The service as worker <paramref name="siteId"/> sees it, optionally on other contexts.</summary>
    /// <summary>Records every form read ListChanges makes; can fail chosen cases.</summary>
    private sealed class FormReads
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<int> _caseIds = new();
        public Func<int, bool> Fail { get; set; } = _ => false;
        public List<int> CaseIds => _caseIds.ToList();
        public void Clear() => _caseIds.Clear();

        public async Task<Microting.eForm.Infrastructure.Models.ReplyElement?> ReadAsync(
            Core core, int caseId, Language language)
        {
            _caseIds.Enqueue(caseId);
            if (Fail(caseId))
            {
                throw new InvalidOperationException($"simulated SDK failure for case {caseId}");
            }

            return await core.CaseRead(caseId, language);
        }
    }

    private EventsGrpcService ServiceFor(
        World w, int siteId,
        BackendConfigurationPnDbContext? bc = null,
        ItemsPlanningPnDbContext? ip = null,
        IBackendConfigurationCalendarService? calendar = null,
        FormReads? forms = null,
        int? languageId = null)
    {
        bc ??= BackendConfigurationPnDbContext!;
        ip ??= ItemsPlanningPnDbContext!;
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(w.Core));
        var siteResolver = Substitute.For<IGrpcSiteResolver>();
        siteResolver.GetSdkSiteIdAsync().Returns(Task.FromResult(siteId));
        siteResolver.GetSiteLanguageIdAsync(Arg.Any<int?>()).Returns(Task.FromResult<int?>(languageId ?? w.Language.Id));
        forms ??= new FormReads();
        return new EventsGrpcService(
            calendar ?? NewCalendar(w, bc, ip),
            w.Properties,
            new BackendConfigurationUserPropertyAccess(bc),
            siteResolver,
            coreHelper,
            bc,
            ip,
            w.Deploy,
            new RecordingLogger<EventsGrpcService>())
        {
            PutFileToS3StorageAsync = (_, _, _) => Task.CompletedTask,
            ReadCaseFormAsync = forms.ReadAsync
        };
    }

    /// <summary>
    /// Moves every stamp a version is built from an hour into the past, so
    /// stored occurrences are older than <see cref="EventsGrpcService.UnchangedTrustAge"/>.
    /// </summary>
    private async Task AgeStampsAsync()
    {
        var old = DateTime.UtcNow.AddHours(-1);
        await BackendConfigurationPnDbContext!.AreaRulePlannings.ExecuteUpdateAsync(u => u.SetProperty(x => x.UpdatedAt, old));
        await BackendConfigurationPnDbContext.CalendarConfigurations.ExecuteUpdateAsync(u => u.SetProperty(x => x.UpdatedAt, old));
        await BackendConfigurationPnDbContext.CalendarOccurrenceExceptions.ExecuteUpdateAsync(u => u.SetProperty(x => x.UpdatedAt, old));
        await BackendConfigurationPnDbContext.AreaRulePlanningFiles.ExecuteUpdateAsync(u => u.SetProperty(x => x.UpdatedAt, old));
        await BackendConfigurationPnDbContext.Compliances.ExecuteUpdateAsync(u => u.SetProperty(x => x.UpdatedAt, old));
        await ItemsPlanningPnDbContext!.Plannings.ExecuteUpdateAsync(u => u.SetProperty(x => x.UpdatedAt, old));
        await MicrotingDbContext!.Cases.ExecuteUpdateAsync(u => u.SetProperty(x => x.UpdatedAt, old));
        await MicrotingDbContext.FieldValues.ExecuteUpdateAsync(u => u.SetProperty(x => x.UpdatedAt, old));
    }

    private async Task<Series> SeedSeriesAsync(
        World w, int propertyId, string title, DateTime start, string? weekdaysCsv, int siteId)
    {
        var areaRule = new AreaRule
        {
            AreaId = w.AreaId, PropertyId = propertyId, EformId = w.TemplateId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.AreaRules.AddAsync(areaRule);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        await BackendConfigurationPnDbContext.AreaRuleTranslations.AddAsync(new AreaRuleTranslation
        {
            AreaRuleId = areaRule.Id, LanguageId = w.Language.Id, Name = title,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var weekly = weekdaysCsv != null;
        var planning = new Planning
        {
            Enabled = true, RepeatEvery = 1,
            RepeatType = weekly ? RepeatType.Week : RepeatType.Day,
            StartDate = start, DayOfWeek = start.DayOfWeek, RelatedEFormId = w.TemplateId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = propertyId, AreaId = w.AreaId,
            ItemPlanningId = planning.Id, StartDate = start, Status = true,
            RepeatType = weekly ? 2 : 1, RepeatEvery = 1, RepeatWeekdaysCsv = weekdaysCsv,
            DayOfWeek = (int)start.DayOfWeek,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(arp);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        await BackendConfigurationPnDbContext.PlanningSites.AddAsync(new BcPlanningSite
        {
            AreaRulePlanningsId = arp.Id, SiteId = siteId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.CalendarConfigurations.AddAsync(new CalendarConfiguration
        {
            AreaRulePlanningId = arp.Id, StartHour = 9.0, Duration = 1.0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        return new Series(arp.Id, planning.Id, propertyId);
    }

    /// <summary>A deployed, open occurrence with its own comment and text value.</summary>
    private async Task<Occurrence> SeedOccurrenceAsync(
        World w, Series series, DateTime day, string comment, string textValue, int siteId)
    {
        var sdkCase = new SdkCase
        {
            SiteId = siteId, CheckListId = w.TemplateId, Status = 66, MicrotingUid = null,
            WorkflowState = Constants.WorkflowStates.Created,
            Custom = "{\"event_comment\":{\"text\":\"" + comment + "\",\"ts_unix\":1700000000}}"
        };
        await MicrotingDbContext!.Cases.AddAsync(sdkCase);
        await MicrotingDbContext.SaveChangesAsync();
        await w.Core.CaseRead(sdkCase.Id, w.Language);
        await MicrotingDbContext.FieldValues
            .Where(fv => fv.CaseId == sdkCase.Id && fv.Field!.FieldTypeId != null
                         && fv.Field.FieldType!.Type == "Text")
            .ExecuteUpdateAsync(u => u.SetProperty(fv => fv.Value, textValue));

        var compliance = new Compliance
        {
            PlanningId = series.PlanningId, PropertyId = series.PropertyId, AreaId = w.AreaId,
            Deadline = day, StartDate = day.AddDays(-7),
            MicrotingSdkCaseId = sdkCase.Id, MicrotingSdkeFormId = w.TemplateId,
            Version = 1, WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Compliances.AddAsync(compliance);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        return new Occurrence(series, compliance.Id, sdkCase.Id, day);
    }

    private async Task<Calendar> SeedCalendarAsync()
    {
        var w = await SeedWorldAsync();
        var monday = NextMonday();

        var p1Weekly = await SeedSeriesAsync(w, w.P1, "p1-weekly-mon", monday, "1", w.SiteA);
        var p1Daily = await SeedSeriesAsync(w, w.P1, "p1-daily", monday, null, w.SiteA);
        var p2Tue = await SeedSeriesAsync(w, w.P2, "p2-weekly-tue", monday.AddDays(1), "2", w.SiteA);
        var p2Wed = await SeedSeriesAsync(w, w.P2, "p2-weekly-wed", monday.AddDays(2), "3", w.SiteA);
        // P3 names site A, but A is not assigned to P3.
        var p3 = await SeedSeriesAsync(w, w.P3, "p3-not-assigned", monday, "1", w.SiteA);
        var p1B = await SeedSeriesAsync(w, w.P1, "p1-for-worker-b", monday, "1", w.SiteB);

        return new Calendar(
            w, monday,
            await SeedOccurrenceAsync(w, p1Weekly, monday, "weekly-comment", "weekly-value", w.SiteA),
            [
                await SeedOccurrenceAsync(w, p1Daily, monday, "daily-mon", "value-mon", w.SiteA),
                await SeedOccurrenceAsync(w, p1Daily, monday.AddDays(1), "daily-tue", "value-tue", w.SiteA),
                await SeedOccurrenceAsync(w, p1Daily, monday.AddDays(2), "daily-wed", "value-wed", w.SiteA)
            ],
            await SeedOccurrenceAsync(w, p2Tue, monday.AddDays(1), "p2-tue", "p2-tue-value", w.SiteA),
            await SeedOccurrenceAsync(w, p2Wed, monday.AddDays(2), "p2-wed", "p2-wed-value", w.SiteA),
            await SeedOccurrenceAsync(w, p3, monday, "p3", "p3-value", w.SiteA),
            await SeedOccurrenceAsync(w, p1B, monday, "b", "b-value", w.SiteB));
    }

    // ------------------------------------------------------------------
    // Calls
    // ------------------------------------------------------------------

    private static Task<ListChangesResponse> ChangesAsync(
        EventsGrpcService service, string from, string to, ByteString? cursor = null) =>
        service.ListChanges(new ListChangesRequest
        {
            FromDateKey = from, ToDateKey = to, Cursor = cursor ?? ByteString.Empty
        }, new TestServerCallContext());

    private Task<ListChangesResponse> ChangesAsync(Calendar c, ByteString? cursor = null) =>
        ChangesAsync(ServiceFor(c.W, c.W.SiteA), c.From, c.To, cursor);

    private static async Task<List<Event>> ListEventsAsync(EventsGrpcService service, int propertyId, string from, string to) =>
        (await service.ListEvents(new ListEventsRequest
        {
            PropertyId = propertyId.ToString(CultureInfo.InvariantCulture), FromDateKey = from, ToDateKey = to
        }, new TestServerCallContext())).Events.ToList();

    private static async Task<Event?> GetEventAsync(EventsGrpcService service, OccurrenceKey key)
    {
        var got = await service.GetEvent(new GetEventRequest
        {
            Key = new EventKey
            {
                EventId = key.EventId.ToString(CultureInfo.InvariantCulture),
                PlanDayKey = key.PlanDayKey,
                ComplianceId = key.ComplianceId
            }
        }, new TestServerCallContext());
        return got.Found ? got.Event : null;
    }

    private static OccurrenceKey KeyOf(Event e) =>
        new(int.Parse(e.Id, CultureInfo.InvariantCulture), e.PlanDayKey, e.ComplianceId);

    private static OccurrenceKey KeyOf(EventKey k) =>
        new(int.Parse(k.EventId, CultureInfo.InvariantCulture), k.PlanDayKey, k.ComplianceId);

    private static List<Event> Upserts(ListChangesResponse r) =>
        r.Changes.Where(c => c.KindCase == ListChange.KindOneofCase.Upserted).Select(c => c.Upserted).ToList();

    private static List<OccurrenceKey> Removals(ListChangesResponse r) =>
        r.Changes.Where(c => c.KindCase == ListChange.KindOneofCase.Removed).Select(c => KeyOf(c.Removed)).ToList();

    private static string TextValue(Event evt) => evt.Fields.Single(f => f.Label == "TextField").Value;

    // ------------------------------------------------------------------
    // Snapshot
    // ------------------------------------------------------------------

    /// <summary>
    /// The snapshot holds exactly the keys of ListEvents(P1) + ListEvents(P2)
    /// for the same window; single-occurrence tasks are the same Event; each
    /// occurrence of the multi-day task carries its OWN content (where the
    /// legacy list borrows the first occurrence's) and equals GetEvent.
    /// </summary>
    [Test]
    public async Task Snapshot_IsTheUnionOfListEventsPerProperty_WithEachOccurrencesOwnContent()
    {
        var c = await SeedCalendarAsync();
        var service = ServiceFor(c.W, c.W.SiteA);

        var snapshot = await ChangesAsync(service, c.From, c.To);
        var union = (await ListEventsAsync(service, c.W.P1, c.From, c.To))
            .Concat(await ListEventsAsync(service, c.W.P2, c.From, c.To)).ToList();
        var upserts = Upserts(snapshot);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Reset, Is.True, "no cursor: a full snapshot");
            Assert.That(Removals(snapshot), Is.Empty);
            Assert.That(snapshot.FromDateKey, Is.EqualTo(c.From));
            Assert.That(snapshot.ToDateKey, Is.EqualTo(c.To));
            Assert.That(upserts.Select(KeyOf), Is.EquivalentTo(union.Select(KeyOf)), "same keys as the per-property lists");
            Assert.That(upserts.Select(KeyOf), Is.EquivalentTo(c.Visible.Select(o => o.Key)), "and those are the seeded ones");
            Assert.That(snapshot.Properties.Select(p => (p.Id, p.Name)), Is.EquivalentTo(new[]
            {
                (c.W.P1.ToString(CultureInfo.InvariantCulture), "P1"),
                (c.W.P2.ToString(CultureInfo.InvariantCulture), "P2")
            }));
            Assert.That(snapshot.Boards.Select(b => b.PropertyId).Distinct(), Is.EquivalentTo(new[]
            {
                c.W.P1.ToString(CultureInfo.InvariantCulture), c.W.P2.ToString(CultureInfo.InvariantCulture)
            }), "the boards of both properties, none of P3");
            Assert.That(snapshot.NextCursor.IsEmpty, Is.False);
        });

        var singleOccurrenceTasks = union.GroupBy(e => e.Id).Where(g => g.Count() == 1).Select(g => g.Key).ToHashSet();
        Assert.That(singleOccurrenceTasks, Has.Count.EqualTo(3), "precondition: three single-occurrence tasks");
        foreach (var listed in union.Where(e => singleOccurrenceTasks.Contains(e.Id)))
        {
            Assert.That(upserts.Single(u => KeyOf(u) == KeyOf(listed)), Is.EqualTo(listed),
                $"{listed.Id}/{listed.PlanDayKey}: the same Event as ListEvents");
        }

        foreach (var (occurrence, comment, value) in c.P1Daily.Zip(
                     new[] { "daily-mon", "daily-tue", "daily-wed" },
                     new[] { "value-mon", "value-tue", "value-wed" }))
        {
            var changed = upserts.Single(u => KeyOf(u) == occurrence.Key);
            Assert.Multiple(() =>
            {
                Assert.That(changed.Comment, Is.EqualTo(comment), $"{occurrence.Day:ddd}: own comment");
                Assert.That(TextValue(changed), Is.EqualTo(value), $"{occurrence.Day:ddd}: own field value");
            });
        }

        Assert.That(union.Single(e => KeyOf(e) == c.P1Daily[2].Key).Comment, Is.EqualTo("daily-mon"),
            "precondition: the legacy multi-day list still borrows the first occurrence's comment");

        foreach (var changed in upserts)
        {
            var got = await GetEventAsync(service, KeyOf(changed));
            Assert.That(got, Is.EqualTo(changed), $"{changed.Id}/{changed.PlanDayKey}: equals GetEvent, etag included");
        }

        // Same eager deploy as ListEvents, once per assigned property, for the window.
        // Deploy scope: Deploy_CoversOnlyTheListsDays_NeverTheFarWindow.
        await c.W.Deploy.DidNotReceive().EnsureDeployedAsync(c.W.P3.ToString(CultureInfo.InvariantCulture),
            Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TasksOfAnUnassignedProperty_NeverAppear_EvenWhenTheyNameTheCallersSite()
    {
        var c = await SeedCalendarAsync();

        var snapshot = await ChangesAsync(c);
        var keys = Upserts(snapshot).Select(KeyOf).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(keys, Does.Not.Contain(c.P3ForSiteA.Key), "P3 is not assigned to A");
            Assert.That(keys, Does.Not.Contain(c.P1ForSiteB.Key), "a P1 task of worker B only");
            Assert.That(Upserts(snapshot).Select(e => e.PropertyId).Distinct(),
                Is.SubsetOf(new[] { c.W.P1.ToString(CultureInfo.InvariantCulture), c.W.P2.ToString(CultureInfo.InvariantCulture) }));
            Assert.That(snapshot.Properties.Select(p => p.Id), Does.Not.Contain(c.W.P3.ToString(CultureInfo.InvariantCulture)));
        });
    }

    [Test]
    public async Task UnchangedFollowUp_HasNoChanges_AndTheSameCursor()
    {
        var c = await SeedCalendarAsync();
        var snapshot = await ChangesAsync(c);

        var followUp = await ChangesAsync(c, snapshot.NextCursor);
        var again = await ChangesAsync(c, followUp.NextCursor);

        Assert.Multiple(() =>
        {
            Assert.That(followUp.Reset, Is.False);
            Assert.That(followUp.Changes, Is.Empty);
            Assert.That(followUp.RemovedPropertyIds, Is.Empty);
            Assert.That(followUp.NextCursor, Is.EqualTo(snapshot.NextCursor), "the same cursor bytes");
            Assert.That(followUp.Properties, Is.EqualTo(snapshot.Properties), "properties on every call");
            Assert.That(followUp.Boards, Is.EqualTo(snapshot.Boards), "boards on every call");
            Assert.That(again.Changes, Is.Empty);
        });
    }

    // ------------------------------------------------------------------
    // Changes after writes
    // ------------------------------------------------------------------

    /// <summary>
    /// A comment and a field value on one occurrence of the multi-day task
    /// each produce exactly that key, with the etag GetEvent returns; the
    /// sibling occurrences of the same task are not touched.
    /// </summary>
    [Test]
    public async Task CommentAndFieldValueWrites_UpsertExactlyThatKey_WithTheGetEventEtag()
    {
        var c = await SeedCalendarAsync();
        var service = ServiceFor(c.W, c.W.SiteA);
        var snapshot = await ChangesAsync(c);
        var target = c.P1Daily[1];

        await service.SetComment(new SetCommentRequest
        {
            EventId = target.Series.ArpId.ToString(CultureInfo.InvariantCulture),
            Text = "tuesday-changed",
            ClientTsUnix = 1700000100,
            ComplianceId = target.ComplianceId,
            MicrotingSdkCaseId = target.CaseId
        }, new TestServerCallContext());
        var afterComment = await ChangesAsync(c, snapshot.NextCursor);

        var textFieldId = Upserts(snapshot).Single(e => KeyOf(e) == c.P1Weekly.Key)
            .Fields.Single(f => f.Label == "TextField").Id;
        await service.SetFieldValue(new SetFieldValueRequest
        {
            EventId = c.P1Weekly.Series.ArpId.ToString(CultureInfo.InvariantCulture),
            FieldId = textFieldId,
            Value = "weekly-changed",
            ClientTsUnix = 1700000200,
            ComplianceId = c.P1Weekly.ComplianceId,
            MicrotingSdkCaseId = c.P1Weekly.CaseId
        }, new TestServerCallContext());
        var afterField = await ChangesAsync(c, afterComment.NextCursor);

        var commentGet = await GetEventAsync(service, target.Key);
        var fieldGet = await GetEventAsync(service, c.P1Weekly.Key);
        Assert.Multiple(() =>
        {
            Assert.That(afterComment.Reset, Is.False);
            Assert.That(afterComment.Changes, Has.Count.EqualTo(1));
            Assert.That(Upserts(afterComment).Single().Comment, Is.EqualTo("tuesday-changed"));
            Assert.That(KeyOf(Upserts(afterComment).Single()), Is.EqualTo(target.Key));
            Assert.That(Upserts(afterComment).Single().Etag, Is.EqualTo(commentGet!.Etag), "the GetEvent etag");
            Assert.That(afterField.Changes, Has.Count.EqualTo(1));
            Assert.That(KeyOf(Upserts(afterField).Single()), Is.EqualTo(c.P1Weekly.Key));
            Assert.That(TextValue(Upserts(afterField).Single()), Is.EqualTo("weekly-changed"));
            Assert.That(Upserts(afterField).Single().Etag, Is.EqualTo(fieldGet!.Etag));
        });
    }

    /// <summary>
    /// A photo upload changes the case's stored photos and FieldValues but
    /// not necessarily the Event content. ListChanges sends the key exactly
    /// when its content (GetEvent etag) changed, and nothing else.
    /// </summary>
    [Test]
    public async Task PhotoUpload_ChangesExactlyTheKeysWhoseEtagChanged()
    {
        var c = await SeedCalendarAsync();
        var service = ServiceFor(c.W, c.W.SiteA);
        var snapshot = await ChangesAsync(c);
        var before = await GetEventAsync(service, c.P2Tuesday.Key);
        var pictureFieldId = before!.Fields.Single(f => f.Label == "PictureField").Id;

        await service.UploadPhoto(new ListStreamReader<UploadPhotoChunk>(
        [
            new UploadPhotoChunk
            {
                Meta = new UploadPhotoMeta
                {
                    EventId = c.P2Tuesday.Series.ArpId.ToString(CultureInfo.InvariantCulture),
                    Slot = 0, ContentType = "image/png", ClientTsUnix = 1700000300,
                    ComplianceId = c.P2Tuesday.ComplianceId, MicrotingSdkCaseId = c.P2Tuesday.CaseId,
                    FieldId = pictureFieldId
                }
            },
            new UploadPhotoChunk { Chunk = ByteString.CopyFrom(OnePixelPng) }
        ]), new TestServerCallContext());

        var after = await GetEventAsync(service, c.P2Tuesday.Key);
        var changes = await ChangesAsync(c, snapshot.NextCursor);
        TestContext.Out.WriteLine($"photo upload: GetEvent etag changed={before.Etag != after!.Etag}, ListChanges changes={changes.Changes.Count}");

        if (before.Etag == after.Etag)
        {
            Assert.That(changes.Changes, Is.Empty, "no content change, no upsert (as the stream)");
        }
        else
        {
            Assert.That(changes.Changes.Select(ch => KeyOf(ch.Upserted)), Is.EqualTo(new[] { c.P2Tuesday.Key }));
            Assert.That(changes.Changes.Single().Upserted.Etag, Is.EqualTo(after.Etag));
        }
    }

    /// <summary>The window is actionable-only: a completed occurrence is removed by its full key.</summary>
    [Test]
    public async Task Completion_RemovesThatKey()
    {
        var c = await SeedCalendarAsync();
        var service = ServiceFor(c.W, c.W.SiteA);
        var snapshot = await ChangesAsync(c);
        var target = c.P1Daily[2];

        await service.CompleteEvent(new CompleteEventRequest
        {
            EventId = target.Series.ArpId.ToString(CultureInfo.InvariantCulture),
            Completed = true,
            CompletedBy = "list-changes-worker",
            ClientTsUnix = 1700000400,
            ComplianceId = target.ComplianceId,
            MicrotingSdkCaseId = target.CaseId,
            Comment = string.Empty,
            DoneAtUserModifiable = string.Empty
        }, new TestServerCallContext());

        var changes = await ChangesAsync(c, snapshot.NextCursor);
        var listed = (await ListEventsAsync(service, c.W.P1, c.From, c.To)).Select(KeyOf).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(listed, Does.Not.Contain(target.Key), "precondition: ListEvents drops it too");
            Assert.That(Removals(changes), Does.Contain(target.Key));
            Assert.That(changes.Changes.Single(ch => ch.KindCase == ListChange.KindOneofCase.Removed).Removed.HasComplianceId,
                Is.True, "the full 3-part key");
            Assert.That(Removals(changes), Has.Count.EqualTo(1));
            Assert.That(Upserts(changes).Select(KeyOf), Is.SubsetOf(listed), "anything else sent is still listed");
        });
    }

    /// <summary>
    /// An occurrence moved out of the window (scope=this, Wednesday -> next
    /// week) is removed by its key; afterwards the keys still equal the
    /// per-property lists.
    /// </summary>
    [Test]
    public async Task OccurrenceMovedOutOfTheWindow_IsRemoved_AndAnOccurrenceEnteringIsUpserted()
    {
        var c = await SeedCalendarAsync();
        var service = ServiceFor(c.W, c.W.SiteA);
        var calendar = NewCalendar(c.W, BackendConfigurationPnDbContext!, ItemsPlanningPnDbContext!);
        var snapshot = await ChangesAsync(c);

        var moved = await calendar.MoveTask(new CalendarTaskMoveRequestModel
        {
            Id = c.P2Wednesday.Series.ArpId,
            OriginalDate = Key(c.P2Wednesday.Day) + "T00:00:00Z",
            NewDate = Key(c.Monday.AddDays(9)) + "T00:00:00Z",
            NewStartHour = 9.0,
            Scope = "this"
        });
        Assert.That(moved.Success, Is.True, moved.Message);

        var newSeries = await SeedSeriesAsync(c.W, c.W.P1, "p1-entering", c.Monday.AddDays(1), "2", c.W.SiteA);
        var entering = await SeedOccurrenceAsync(c.W, newSeries, c.Monday.AddDays(1), "new", "new-value", c.W.SiteA);

        var changes = await ChangesAsync(c, snapshot.NextCursor);
        var union = (await ListEventsAsync(service, c.W.P1, c.From, c.To))
            .Concat(await ListEventsAsync(service, c.W.P2, c.From, c.To)).Select(KeyOf).ToList();
        var known = snapshot.Changes.Select(ch => KeyOf(ch.Upserted))
            .Except(Removals(changes)).Union(Upserts(changes).Select(KeyOf)).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(Removals(changes), Is.EqualTo(new[] { c.P2Wednesday.Key }), "removed by its key");
            Assert.That(Upserts(changes).Select(KeyOf), Is.EqualTo(new[] { entering.Key }), "only the entering one");
            Assert.That(known, Is.EquivalentTo(union), "client state = the per-property lists");
        });
    }

    // ------------------------------------------------------------------
    // Property assignments
    // ------------------------------------------------------------------

    [Test]
    public async Task AssignmentRemoved_RemovesItsTasks_AndAssignmentAdded_UpsertsThem()
    {
        var c = await SeedCalendarAsync();
        var snapshot = await ChangesAsync(c);

        var p2Worker = await BackendConfigurationPnDbContext!.PropertyWorkers
            .SingleAsync(x => x.WorkerId == c.W.SiteA && x.PropertyId == c.W.P2);
        await p2Worker.Delete(BackendConfigurationPnDbContext);
        var afterRemoval = await ChangesAsync(c, snapshot.NextCursor);

        await AssignAsync(c.W.SiteA, c.W.P3);
        var afterAdding = await ChangesAsync(c, afterRemoval.NextCursor);

        var p2 = c.W.P2.ToString(CultureInfo.InvariantCulture);
        var p3 = c.W.P3.ToString(CultureInfo.InvariantCulture);
        Assert.Multiple(() =>
        {
            Assert.That(afterRemoval.Reset, Is.False);
            Assert.That(Removals(afterRemoval), Is.EquivalentTo(new[] { c.P2Tuesday.Key, c.P2Wednesday.Key }));
            Assert.That(Upserts(afterRemoval), Is.Empty);
            Assert.That(afterRemoval.RemovedPropertyIds, Is.EqualTo(new[] { p2 }));
            Assert.That(afterRemoval.Properties.Select(p => p.Id), Does.Not.Contain(p2));
            Assert.That(afterRemoval.Boards.Select(b => b.PropertyId), Does.Not.Contain(p2));

            Assert.That(Upserts(afterAdding).Select(KeyOf), Is.EqualTo(new[] { c.P3ForSiteA.Key }));
            Assert.That(Removals(afterAdding), Is.Empty);
            Assert.That(afterAdding.RemovedPropertyIds, Is.Empty);
            Assert.That(afterAdding.Properties.Select(p => p.Id), Does.Contain(p3));
        });
    }

    // ------------------------------------------------------------------
    // Cursor robustness
    // ------------------------------------------------------------------

    [Test]
    public async Task UnusableCursors_AnswerAFullResetSnapshot_NeverAnError()
    {
        var c = await SeedCalendarAsync();
        var snapshot = await ChangesAsync(c);
        var expected = Upserts(snapshot).Select(KeyOf).ToList();
        var cursor = snapshot.NextCursor.ToByteArray();
        var garbage = new byte[64];
        new Random(3).NextBytes(garbage);

        var cases = new Dictionary<string, ByteString>
        {
            ["garbage"] = ByteString.CopyFrom(garbage),
            ["truncated"] = ByteString.CopyFrom(cursor[..(cursor.Length / 2)]),
            ["one byte"] = ByteString.CopyFrom(cursor[..1]),
            ["bit flip"] = ByteString.CopyFrom(cursor.Select((b, i) => i == 10 ? (byte)(b ^ 4) : b).ToArray())
        };

        foreach (var (name, bad) in cases)
        {
            var response = await ChangesAsync(c, bad);
            Assert.Multiple(() =>
            {
                Assert.That(response.Reset, Is.True, name);
                Assert.That(Removals(response), Is.Empty, name);
                Assert.That(Upserts(response).Select(KeyOf), Is.EquivalentTo(expected), name);
                Assert.That(response.NextCursor, Is.EqualTo(snapshot.NextCursor), $"{name}: a fresh, valid cursor");
            });
        }

        // Another window: reset, with the new window's content.
        var service = ServiceFor(c.W, c.W.SiteA);
        var otherWindow = await ChangesAsync(service, c.From, c.From, snapshot.NextCursor);
        Assert.Multiple(() =>
        {
            Assert.That(otherWindow.Reset, Is.True, "another window");
            Assert.That(Removals(otherWindow), Is.Empty);
            Assert.That(Upserts(otherWindow).Select(e => e.PlanDayKey).Distinct(), Is.EqualTo(new[] { c.From }));
        });
    }

    /// <summary>
    /// Worker B presenting worker A's cursor gets B's own snapshot: reset,
    /// no removals (which would echo A's keys), nothing of A's.
    /// </summary>
    [Test]
    public async Task AnotherWorkersCursor_IsAReset_AndLeaksNothing()
    {
        var c = await SeedCalendarAsync();
        var cursorOfA = (await ChangesAsync(c)).NextCursor;

        var asB = await ChangesAsync(ServiceFor(c.W, c.W.SiteB), c.From, c.To, cursorOfA);

        Assert.Multiple(() =>
        {
            Assert.That(asB.Reset, Is.True);
            Assert.That(Removals(asB), Is.Empty);
            Assert.That(asB.RemovedPropertyIds, Is.Empty);
            Assert.That(Upserts(asB).Select(KeyOf), Is.EqualTo(new[] { c.P1ForSiteB.Key }), "B's own task only");
            Assert.That(asB.Properties.Select(p => p.Id), Is.EqualTo(new[] { c.W.P1.ToString(CultureInfo.InvariantCulture) }));
            Assert.That(asB.NextCursor, Is.Not.EqualTo(cursorOfA));
        });
    }

    /// <summary>
    /// Both keys empty = today..today+2; a window is clamped to the stream's
    /// window (today-7..today+30) and the served window is stated; a window
    /// entirely outside it, or a malformed one, is INVALID_ARGUMENT.
    /// </summary>
    [Test]
    public async Task Window_DefaultsToTheListWindow_IsClampedToTheStreamsWindow_AndRejectsNonsense()
    {
        var c = await SeedCalendarAsync();
        var service = ServiceFor(c.W, c.W.SiteA);
        var today = DateTime.UtcNow.Date;

        var defaulted = await ChangesAsync(service, string.Empty, string.Empty);
        var clamped = await ChangesAsync(service, Key(today.AddDays(-400)), Key(today.AddDays(400)));

        Assert.Multiple(() =>
        {
            Assert.That(defaulted.FromDateKey, Is.EqualTo(Key(today)));
            Assert.That(defaulted.ToDateKey, Is.EqualTo(Key(today.AddDays(2))));
            Assert.That(clamped.FromDateKey, Is.EqualTo(Key(today.AddDays(-7))));
            Assert.That(clamped.ToDateKey, Is.EqualTo(Key(today.AddDays(30))));
        });

        foreach (var (from, to) in new[]
                 {
                     (c.From, string.Empty), ("2026-13-01", c.To), (c.To, c.From), ("9999-12-31", "9999-12-31"),
                     ("0001-01-01", "0001-01-01"), (Key(today.AddDays(31)), Key(today.AddDays(200))),
                     (Key(today.AddDays(-200)), Key(today.AddDays(-8))), ("garbage", "garbage")
                 })
        {
            var ex = await Assert.ThrowsAsync<GrpcCore.RpcException>(() => ChangesAsync(service, from, to));
            Assert.That(ex!.StatusCode, Is.EqualTo(GrpcCore.StatusCode.InvalidArgument), $"{from}..{to}");
        }
    }

    /// <summary>
    /// The eager deploy covers only what the legacy list would deploy
    /// (today..today+2 inside the window): a future window deploys nothing,
    /// a stream-sized window deploys today..today+2 per assigned property.
    /// </summary>
    [Test]
    public async Task Deploy_CoversOnlyTheListsDays_NeverTheFarWindow()
    {
        var c = await SeedCalendarAsync();
        var service = ServiceFor(c.W, c.W.SiteA);
        var today = DateTime.UtcNow.Date;

        var future = await ChangesAsync(service, Key(today.AddDays(20)), Key(today.AddDays(300)));
        var futureCalls = c.W.Deploy.ReceivedCalls().Count();
        await ChangesAsync(service, Key(today.AddDays(-7)), Key(today.AddDays(30)));

        var deploys = c.W.Deploy.ReceivedCalls()
            .Select(call => call.GetArguments())
            .Select(a => ((string)a[0]!, (string)a[2]!, (string)a[3]!, (int)a[4]!))
            .ToList();
        Assert.Multiple(() =>
        {
            Assert.That(future.FromDateKey, Is.EqualTo(Key(today.AddDays(20))));
            Assert.That(future.ToDateKey, Is.EqualTo(Key(today.AddDays(30))), "served window clamped");
            Assert.That(futureCalls, Is.Zero, "a window after today+2 deploys nothing");
            Assert.That(deploys, Is.EquivalentTo(new[] { c.W.P1, c.W.P2 }.Select(p =>
                (p.ToString(CultureInfo.InvariantCulture), Key(today), Key(today.AddDays(2)), c.W.SiteA))));
        });
    }

    // ------------------------------------------------------------------
    // Form reads (cost)
    // ------------------------------------------------------------------

    /// <summary>
    /// With stamps older than the trust age, an unchanged follow-up reads no
    /// form at all; a field-value write is detected (its case is read and
    /// upserted) while the other cases are still not read.
    /// </summary>
    [Test]
    public async Task UnchangedFollowUp_ReadsNoForm_AndAChangedCaseIsStillDetected()
    {
        var c = await SeedCalendarAsync();
        await AgeStampsAsync();
        var forms = new FormReads();
        var service = ServiceFor(c.W, c.W.SiteA, forms: forms);

        var snapshot = await ChangesAsync(service, c.From, c.To);
        var snapshotReads = forms.CaseIds;
        forms.Clear();
        var followUp = await ChangesAsync(service, c.From, c.To, snapshot.NextCursor);
        var followUpReads = forms.CaseIds;

        var textFieldId = Upserts(snapshot).Single(e => KeyOf(e) == c.P2Tuesday.Key)
            .Fields.Single(f => f.Label == "TextField").Id;
        await service.SetFieldValue(new SetFieldValueRequest
        {
            EventId = c.P2Tuesday.Series.ArpId.ToString(CultureInfo.InvariantCulture),
            FieldId = textFieldId,
            Value = "changed-after-aging",
            ClientTsUnix = 1700000500,
            ComplianceId = c.P2Tuesday.ComplianceId,
            MicrotingSdkCaseId = c.P2Tuesday.CaseId
        }, new TestServerCallContext());
        forms.Clear();
        var afterWrite = await ChangesAsync(service, c.From, c.To, followUp.NextCursor);

        Assert.Multiple(() =>
        {
            Assert.That(snapshotReads, Is.EquivalentTo(c.Visible.Select(o => o.CaseId)), "a snapshot reads every form once");
            Assert.That(followUp.Changes, Is.Empty);
            Assert.That(followUpReads, Is.Empty, "an unchanged follow-up reads no form");
            Assert.That(forms.CaseIds, Is.EqualTo(new[] { c.P2Tuesday.CaseId }), "only the written case is read");
            Assert.That(Upserts(afterWrite).Select(KeyOf), Is.EqualTo(new[] { c.P2Tuesday.Key }));
            Assert.That(TextValue(Upserts(afterWrite).Single()), Is.EqualTo("changed-after-aging"));
        });
    }

    /// <summary>
    /// An occurrence whose newest stamp is younger than the trust age is
    /// read again even when its version equals the cursor's: a same-second
    /// write would not have moved the version.
    /// </summary>
    [Test]
    public async Task RecentVersionStamp_IsNotTrusted_TheFormIsRead()
    {
        var c = await SeedCalendarAsync();
        await AgeStampsAsync();
        var recent = DateTime.UtcNow.AddSeconds(-2);
        await MicrotingDbContext!.Cases.Where(x => x.Id == c.P1Weekly.CaseId)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.UpdatedAt, recent));
        var forms = new FormReads();
        var service = ServiceFor(c.W, c.W.SiteA, forms: forms);

        var snapshot = await ChangesAsync(service, c.From, c.To);
        forms.Clear();
        var followUp = await ChangesAsync(service, c.From, c.To, snapshot.NextCursor);

        Assert.Multiple(() =>
        {
            Assert.That(followUp.Changes, Is.Empty);
            Assert.That(forms.CaseIds, Is.EqualTo(new[] { c.P1Weekly.CaseId }), "only the recently stamped case is read");
        });
    }

    /// <summary>
    /// A form read while its newest stamp was still recent must not let a
    /// later call trust that stamp: a second write that does not raise the
    /// stamp (same second, clock skew, late commit) is found by the next call
    /// even when that call comes after the trust age.
    /// </summary>
    [Test]
    public async Task FormReadWhileTheStampWasRecent_IsReadAgainLater_AndAStampNeutralWriteIsSent()
    {
        var c = await SeedCalendarAsync();
        await AgeStampsAsync();
        var stamp = DateTime.UtcNow.AddSeconds(-2);
        await MicrotingDbContext!.Cases.Where(x => x.Id == c.P1Weekly.CaseId)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.UpdatedAt, stamp));
        var forms = new FormReads();
        var service = ServiceFor(c.W, c.W.SiteA, forms: forms);
        var snapshot = await ChangesAsync(service, c.From, c.To);

        // A second write that leaves every stamp as it was (its FieldValues stamp stays an hour old).
        await MicrotingDbContext.FieldValues
            .Where(fv => fv.CaseId == c.P1Weekly.CaseId && fv.Value == "weekly-value")
            .ExecuteUpdateAsync(u => u.SetProperty(fv => fv.Value, "stamp-neutral"));

        // Past the trust age of the stamp.
        var wait = stamp + EventsGrpcService.UnchangedTrustAge + TimeSpan.FromSeconds(1) - DateTime.UtcNow;
        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait);
        }

        forms.Clear();
        var later = await ChangesAsync(service, c.From, c.To, snapshot.NextCursor);

        Assert.Multiple(() =>
        {
            Assert.That(forms.CaseIds, Does.Contain(c.P1Weekly.CaseId), "the form is read again");
            Assert.That(Upserts(later).Select(KeyOf), Is.EqualTo(new[] { c.P1Weekly.Key }));
            Assert.That(TextValue(Upserts(later).Single()), Is.EqualTo("stamp-neutral"));
        });
    }

    /// <summary>
    /// A reset snapshot while one form cannot be read is UNAVAILABLE (a
    /// reset would make the client drop it). In a diff the unreadable
    /// occurrence is neither removed nor forgotten: no change now, and once
    /// it reads again with unchanged content, still no change.
    /// </summary>
    [Test]
    public async Task UnreadableForm_ResetIsUnavailable_DiffKeepsTheOccurrence()
    {
        var c = await SeedCalendarAsync();
        var forms = new FormReads();
        var service = ServiceFor(c.W, c.W.SiteA, forms: forms);
        var snapshot = await ChangesAsync(service, c.From, c.To);

        forms.Fail = id => id == c.P1Daily[1].CaseId;
        var reset = await Assert.ThrowsAsync<GrpcCore.RpcException>(() => ChangesAsync(service, c.From, c.To));
        var garbageReset = await Assert.ThrowsAsync<GrpcCore.RpcException>(() =>
            ChangesAsync(service, c.From, c.To, ByteString.CopyFromUtf8("garbage-cursor")));
        var whileFailing = await ChangesAsync(service, c.From, c.To, snapshot.NextCursor);

        forms.Fail = _ => false;
        var recovered = await ChangesAsync(service, c.From, c.To, whileFailing.NextCursor);

        Assert.Multiple(() =>
        {
            Assert.That(reset!.StatusCode, Is.EqualTo(GrpcCore.StatusCode.Unavailable), "no reset with a hole");
            Assert.That(garbageReset!.StatusCode, Is.EqualTo(GrpcCore.StatusCode.Unavailable));
            Assert.That(whileFailing.Reset, Is.False);
            Assert.That(whileFailing.Changes, Is.Empty, "not removed");
            Assert.That(recovered.Changes, Is.Empty, "not forgotten (would come back as an upsert)");
            Assert.That(recovered.NextCursor, Is.EqualTo(snapshot.NextCursor));
        });
    }

    /// <summary>The cursor is bound to the site language the forms were read in.</summary>
    [Test]
    public async Task AnotherSiteLanguage_IsAReset()
    {
        var c = await SeedCalendarAsync();
        var other = await MicrotingDbContext!.Languages.Where(l => l.Id != c.W.Language.Id).Select(l => l.Id).FirstAsync();
        var cursor = (await ChangesAsync(c)).NextCursor;

        var inOtherLanguage = await ChangesAsync(ServiceFor(c.W, c.W.SiteA, languageId: other), c.From, c.To, cursor);

        Assert.That(inOtherLanguage.Reset, Is.True);
    }

    // ------------------------------------------------------------------
    // Failures
    // ------------------------------------------------------------------

    [Test]
    public async Task FailedCalendarRead_IsUnavailable_NeverAnEmptySnapshot()
    {
        var c = await SeedCalendarAsync();
        var real = NewCalendar(c.W, BackendConfigurationPnDbContext!, ItemsPlanningPnDbContext!);
        var failing = Substitute.For<IBackendConfigurationCalendarService>();
        failing.GetBoards(Arg.Any<int>()).Returns(ci => real.GetBoards(ci.Arg<int>()));
        failing.GetTasksForWeek(Arg.Any<CalendarTaskRequestModel>())
            .Returns(new OperationDataResult<List<CalendarTaskResponseModel>>(false, "db down"));
        var service = ServiceFor(c.W, c.W.SiteA, calendar: failing);

        var ex = await Assert.ThrowsAsync<GrpcCore.RpcException>(() => ChangesAsync(service, c.From, c.To));
        Assert.That(ex!.StatusCode, Is.EqualTo(GrpcCore.StatusCode.Unavailable));
    }

    [Test]
    public async Task FailedPropertyRead_IsUnavailable()
    {
        var c = await SeedCalendarAsync();
        c.W.Properties.GetCommonDictionary(false)
            .Returns(new OperationDataResult<List<CommonDictionaryModel>>(false, "down"));

        var ex = await Assert.ThrowsAsync<GrpcCore.RpcException>(() => ChangesAsync(c));
        Assert.That(ex!.StatusCode, Is.EqualTo(GrpcCore.StatusCode.Unavailable));
    }

    // ------------------------------------------------------------------
    // Legacy untouched
    // ------------------------------------------------------------------

    /// <summary>
    /// The stream's EventChange keeps exactly its three cases (ListChanges has
    /// its own ListChange), and a stream on P1 sends only those.
    /// </summary>
    [Test]
    public async Task Stream_StillSendsOnlyTheThreeOriginalEventChangeCases()
    {
        var c = await SeedCalendarAsync();
        var oneof = EventChange.Descriptor.Oneofs.Single();
        Assert.That(oneof.Fields.Select(f => (f.FieldNumber, f.Name)), Is.EqualTo(new[]
        {
            (1, "upserted"), (2, "removed_id"), (3, "removed_occurrence")
        }));

        var writer = new RecordingStreamWriter<EventChange>();
        using var cts = new CancellationTokenSource();
        var streaming = ServiceFor(c.W, c.W.SiteA).StreamEventChanges(
            new StreamEventChangesRequest { PropertyId = c.W.P1.ToString(CultureInfo.InvariantCulture) },
            writer, new TestServerCallContext(cts.Token));
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (writer.Snapshot().Count < 4 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(100);
            }
        }
        finally
        {
            await cts.CancelAsync();
            await streaming;
        }

        var sent = writer.Snapshot();
        Assert.That(sent, Has.Count.GreaterThanOrEqualTo(4), "precondition: the initial snapshot arrived");
        Assert.That(sent.Select(m => m.KindCase), Is.All.AnyOf(
            EventChange.KindOneofCase.Upserted, EventChange.KindOneofCase.RemovedId,
            EventChange.KindOneofCase.RemovedOccurrence));
        Assert.That(sent.Select(m => EventChange.Parser.ParseFrom(m.ToByteArray())), Is.EqualTo(sent),
            "nothing unknown on the wire");
    }

    // ------------------------------------------------------------------
    // Cost
    // ------------------------------------------------------------------

    private async Task<long> GlobalQuestionsAsync()
    {
        var connection = BackendConfigurationPnDbContext!.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SHOW GLOBAL STATUS LIKE 'Questions'";
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return long.Parse(reader.GetString(1), CultureInfo.InvariantCulture);
    }

    private BackendConfigurationPnDbContext NewBackendContext(CommandCounter counter)
    {
        var cs = BackendConfigurationPnDbContext!.Database.GetConnectionString()!;
        var ob = new DbContextOptionsBuilder<BackendConfigurationPnDbContext>();
        ob.UseMySql(cs, ServerVersion.AutoDetect(cs), b => b.EnableRetryOnFailure());
        ob.AddInterceptors(counter);
        return new BackendConfigurationPnDbContext(ob.Options);
    }

    private ItemsPlanningPnDbContext NewItemsPlanningContext(CommandCounter counter)
    {
        var cs = ItemsPlanningPnDbContext!.Database.GetConnectionString()!;
        var ob = new DbContextOptionsBuilder<ItemsPlanningPnDbContext>();
        ob.UseMySql(cs, ServerVersion.AutoDetect(cs), b => b.EnableRetryOnFailure());
        ob.AddInterceptors(counter);
        return new ItemsPlanningPnDbContext(ob.Options);
    }

    /// <summary>
    /// Queries per call for 2 properties with 6 occurrences (4 tasks):
    /// plugin + ItemsPlanning commands by interceptor, every statement of
    /// every database (SDK included) by MariaDB's global Questions counter
    /// (each probe itself counts 1). Compared with what the two legacy
    /// ListEvents calls for the same window cost. The unchanged follow-up
    /// must cost less than the snapshot (no version stamping, nothing sent).
    /// </summary>
    [Test]
    public async Task QueryCount_SnapshotAndUnchangedFollowUp()
    {
        var c = await SeedCalendarAsync();
        // Stored data is normally older than the trust age; fresh seed data is not.
        await AgeStampsAsync();
        var plugin = new CommandCounter();
        var planning = new CommandCounter();
        await using var bc = NewBackendContext(plugin);
        await using var ip = NewItemsPlanningContext(planning);
        var forms = new FormReads();
        var service = ServiceFor(c.W, c.W.SiteA, bc, ip, forms: forms);

        // Warm-up (EF model, Default boards auto-created by GetBoards).
        var warm = await ChangesAsync(service, c.From, c.To);

        async Task<(int Plugin, int Planning, long Total, long Ms, T Result, int FormReads)> MeasureAsync<T>(Func<Task<T>> call)
        {
            plugin.Reset();
            planning.Reset();
            forms.Clear();
            var before = await GlobalQuestionsAsync();
            var watch = Stopwatch.StartNew();
            var result = await call();
            watch.Stop();
            var after = await GlobalQuestionsAsync();
            return (plugin.Count, planning.Count, after - before - 1, watch.ElapsedMilliseconds, result, forms.CaseIds.Count);
        }

        var snapshot = await MeasureAsync(() => ChangesAsync(service, c.From, c.To));
        var followUp = await MeasureAsync(() => ChangesAsync(service, c.From, c.To, snapshot.Result.NextCursor));
        var legacy = await MeasureAsync(async () =>
        {
            var p1 = await ListEventsAsync(service, c.W.P1, c.From, c.To);
            var p2 = await ListEventsAsync(service, c.W.P2, c.From, c.To);
            return p1.Count + p2.Count;
        });

        // The stream's window: one stream poll per property re-reads this
        // (ListEvents over it is the same read; deploy is stubbed here).
        var streamFrom = Key(DateTime.UtcNow.Date.AddDays(-7));
        var streamTo = Key(DateTime.UtcNow.Date.AddDays(30));
        var wideSnapshot = await MeasureAsync(() => ChangesAsync(service, streamFrom, streamTo));
        var wideFollowUp = await MeasureAsync(() => ChangesAsync(service, streamFrom, streamTo, wideSnapshot.Result.NextCursor));
        var streamPolls = await MeasureAsync(async () =>
            (await ListEventsAsync(service, c.W.P1, streamFrom, streamTo)).Count
            + (await ListEventsAsync(service, c.W.P2, streamFrom, streamTo)).Count);

        var snapshotBytes = snapshot.Result.CalculateSize();
        var perEvent = Upserts(snapshot.Result).Average(e => e.CalculateSize());
        TestContext.Out.WriteLine(
            $"ListChanges snapshot: plugin={snapshot.Plugin} itemsPlanning={snapshot.Planning} allStatements={snapshot.Total} ms={snapshot.Ms} " +
            $"events={Upserts(snapshot.Result).Count} responseBytes={snapshotBytes} avgEventBytes={perEvent:F0} cursorBytes={snapshot.Result.NextCursor.Length}");
        TestContext.Out.WriteLine(
            $"ListChanges unchanged follow-up: plugin={followUp.Plugin} itemsPlanning={followUp.Planning} allStatements={followUp.Total} ms={followUp.Ms} " +
            $"responseBytes={followUp.Result.CalculateSize()} formReads={followUp.FormReads} (snapshot formReads={snapshot.FormReads})");
        TestContext.Out.WriteLine(
            $"Stream window: ListChanges snapshot allStatements={wideSnapshot.Total} formReads={wideSnapshot.FormReads}; " +
            $"unchanged follow-up allStatements={wideFollowUp.Total} formReads={wideFollowUp.FormReads}; " +
            $"two stream-poll-equivalent reads (ListEvents P1+P2) allStatements={streamPolls.Total} events={streamPolls.Result}");
        TestContext.Out.WriteLine(
            $"Legacy ListEvents P1+P2: plugin={legacy.Plugin} itemsPlanning={legacy.Planning} allStatements={legacy.Total} ms={legacy.Ms} events={legacy.Result}");

        Assert.Multiple(() =>
        {
            Assert.That(warm.Changes, Has.Count.EqualTo(6));
            Assert.That(snapshot.Result.Changes, Has.Count.EqualTo(6));
            Assert.That(followUp.Result.Changes, Is.Empty);
            Assert.That(followUp.FormReads, Is.Zero, "an unchanged follow-up reads no form");
            Assert.That(wideFollowUp.FormReads, Is.Zero);
            Assert.That(followUp.Total, Is.LessThan(snapshot.Total / 2), "an unchanged follow-up costs far less");
        });
    }
}
