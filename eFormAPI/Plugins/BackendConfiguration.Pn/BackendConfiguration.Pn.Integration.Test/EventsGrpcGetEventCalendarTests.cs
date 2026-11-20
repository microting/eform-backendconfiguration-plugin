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
using GrpcCore = global::Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Enums;
using NSubstitute;
using SdkCase = Microting.eForm.Infrastructure.Data.Entities.Case;
using Compliance = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.Compliance;
using BcProperty = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.Property;
using BcPlanningSite = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.PlanningSite;

/// <summary>
/// <c>GetEvent</c> against the REAL <see cref="BackendConfigurationCalendarService"/>
/// (PR #1516 review round 2): every key <c>ListEvents</c> returns must be
/// found by <c>GetEvent</c>, including an occurrence moved to another day
/// with <c>scope=this</c>, and <c>GetEvent</c> must return the occurrence's
/// own comment and field values when one planning has several occurrences.
/// Also pins the PRE-EXISTING multi-day list behaviour (a later occurrence of
/// the same planning shows the first occurrence's case content) so this PR
/// demonstrably leaves the legacy list unchanged.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class EventsGrpcGetEventCalendarTests : TestBaseSetup
{
    private const string TemplateXml = @"
<?xml version='1.0' encoding='UTF-8'?>
<Main>
    <Id>9080</Id>
    <Repeated>0</Repeated>
    <Label>GetEventCalendarMain</Label>
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
            <Id>9080</Id>
            <Label>GetEventCalendarElement</Label>
            <Description><![CDATA[]]></Description>
            <DisplayOrder>0</DisplayOrder>
            <ReviewEnabled>false</ReviewEnabled>
            <ManualSync>false</ManualSync>
            <ExtraFieldsEnabled>false</ExtraFieldsEnabled>
            <DoneButtonDisabled>false</DoneButtonDisabled>
            <ApprovalEnabled>false</ApprovalEnabled>
            <DataItemList>
                <DataItem type='Text'>
                    <Id>73681</Id>
                    <Label>TextField</Label>
                    <Description><![CDATA[]]></Description>
                    <DisplayOrder>0</DisplayOrder>
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

    private sealed class TestServerCallContext : GrpcCore.ServerCallContext
    {
        protected override string MethodCore => "test";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "test-peer";
        protected override DateTime DeadlineCore => DateTime.MaxValue;
        protected override GrpcCore.Metadata RequestHeadersCore => new();
        protected override CancellationToken CancellationTokenCore => CancellationToken.None;
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

    private sealed record World(
        EventsGrpcService Events,
        BackendConfigurationCalendarService Calendar,
        Core Core,
        Language Language,
        int SiteId,
        int PropertyId,
        int AreaId,
        int TemplateId);

    private sealed record Series(int ArpId, int PlanningId);

    private sealed record Occurrence(int ComplianceId, int CaseId);

    private static string Key(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateTime NextMonday()
    {
        var today = DateTime.UtcNow.Date;
        var days = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        return DateTime.SpecifyKind(today.AddDays(days == 0 ? 7 : days), DateTimeKind.Utc);
    }

    private int _templateId;

    private async Task<World> SeedWorldAsync(string label)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var core = await GetCore();
        var language = await MicrotingDbContext!.Languages.FirstAsync();
        if (_templateId == 0)
        {
            _templateId = await core.TemplateCreate(await core.TemplateFromXml(TemplateXml));
        }

        var site = new Site
        {
            Name = $"{label}-worker-{suffix}",
            MicrotingUid = Random.Shared.Next(500000, 999999),
            LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddAsync(site);
        await MicrotingDbContext.SaveChangesAsync();

        var area = new Area
        {
            Type = AreaTypesEnum.Type1, ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Areas.AddAsync(area);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var property = new BcProperty
        {
            Name = $"{label}-{suffix}", ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.Properties.AddAsync(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        userService.GetCurrentUserLanguage().Returns(Task.FromResult(language));
        var taskWizardService = Substitute.For<IBackendConfigurationTaskWizardService>();
        taskWizardService.DeleteTask(Arg.Any<int>()).Returns(Task.FromResult(new OperationResult(true)));

        var calendar = new BackendConfigurationCalendarService(
            new BackendConfigurationLocalizationService(), userService,
            BackendConfigurationPnDbContext, coreHelper, Substitute.For<IEventDeployService>(),
            ItemsPlanningPnDbContext!, taskWizardService,
            Substitute.For<ICalendarAssignmentReconciliationService>(),
            Substitute.For<ICalendarChangeNotifier>(),
            TestContextLogger<BackendConfigurationCalendarService>.Instance,
            Substitute.For<ICalendarOccurrenceRetractionService>(),
            Substitute.For<ICalendarPastSeriesBackfillService>(),
            new WorkerTagMembershipService(coreHelper, BackendConfigurationPnDbContext));

        var siteResolver = Substitute.For<IGrpcSiteResolver>();
        siteResolver.GetSdkSiteIdAsync().Returns(Task.FromResult(site.Id));
        siteResolver.GetSiteLanguageIdAsync(Arg.Any<int?>()).Returns(Task.FromResult<int?>(language.Id));
        var access = Substitute.For<IBackendConfigurationUserPropertyAccess>();
        access.HasAccessAsync(site.Id, property.Id).Returns(Task.FromResult(true));

        var events = new EventsGrpcService(
            calendar,
            Substitute.For<IBackendConfigurationPropertiesService>(),
            access,
            siteResolver,
            coreHelper,
            BackendConfigurationPnDbContext,
            ItemsPlanningPnDbContext!,
            Substitute.For<IEventDeployService>(),
            new RecordingLogger<EventsGrpcService>());

        return new World(events, calendar, core, language, site.Id, property.Id, area.Id, _templateId);
    }

    /// <summary>A daily (<paramref name="weekdaysCsv"/> null) or weekly series assigned to the world's site.</summary>
    private async Task<Series> SeedSeriesAsync(World w, string title, DateTime start, string? weekdaysCsv)
    {
        var areaRule = new AreaRule
        {
            AreaId = w.AreaId, PropertyId = w.PropertyId, EformId = w.TemplateId,
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
            AreaRuleId = areaRule.Id, PropertyId = w.PropertyId, AreaId = w.AreaId,
            ItemPlanningId = planning.Id, StartDate = start, Status = true,
            RepeatType = weekly ? 2 : 1, RepeatEvery = 1, RepeatWeekdaysCsv = weekdaysCsv,
            DayOfWeek = (int)start.DayOfWeek,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(arp);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        await BackendConfigurationPnDbContext.PlanningSites.AddAsync(new BcPlanningSite
        {
            AreaRulePlanningsId = arp.Id, SiteId = w.SiteId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.CalendarConfigurations.AddAsync(new CalendarConfiguration
        {
            AreaRulePlanningId = arp.Id, StartHour = 9.0, Duration = 1.0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        return new Series(arp.Id, planning.Id);
    }

    /// <summary>
    /// A deployed, open occurrence: an SDK case on the world's site (with
    /// materialised FieldValues, the text field set to <paramref name="textValue"/>
    /// and the event comment <paramref name="comment"/>) and its live compliance.
    /// </summary>
    private async Task<Occurrence> SeedOccurrenceAsync(
        World w, Series series, DateTime deadline, string comment, string textValue)
    {
        var sdkCase = new SdkCase
        {
            SiteId = w.SiteId, CheckListId = w.TemplateId, Status = 66, MicrotingUid = null,
            WorkflowState = Constants.WorkflowStates.Created,
            Custom = comment.Length == 0
                ? string.Empty
                : "{\"event_comment\":{\"text\":\"" + comment + "\",\"ts_unix\":1700000000}}"
        };
        await MicrotingDbContext!.Cases.AddAsync(sdkCase);
        await MicrotingDbContext.SaveChangesAsync();
        await w.Core.CaseRead(sdkCase.Id, w.Language);
        if (textValue.Length > 0)
        {
            await MicrotingDbContext.FieldValues.Where(fv => fv.CaseId == sdkCase.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(fv => fv.Value, textValue));
        }

        var compliance = new Compliance
        {
            PlanningId = series.PlanningId, PropertyId = w.PropertyId, AreaId = w.AreaId,
            Deadline = deadline, StartDate = deadline.AddDays(-7),
            MicrotingSdkCaseId = sdkCase.Id, MicrotingSdkeFormId = w.TemplateId,
            Version = 1, WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Compliances.AddAsync(compliance);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        return new Occurrence(compliance.Id, sdkCase.Id);
    }

    private static async Task MoveThisAsync(World w, Series series, DateTime from, DateTime to)
    {
        var moved = await w.Calendar.MoveTask(new CalendarTaskMoveRequestModel
        {
            Id = series.ArpId,
            OriginalDate = Key(from) + "T00:00:00Z",
            NewDate = Key(to) + "T00:00:00Z",
            NewStartHour = 9.0,
            Scope = "this"
        });
        Assert.That(moved.Success, Is.True, moved.Message);
    }

    private static async Task<List<Event>> ListAsync(World w, DateTime from, DateTime to) =>
        (await w.Events.ListEvents(new ListEventsRequest
        {
            PropertyId = w.PropertyId.ToString(CultureInfo.InvariantCulture),
            FromDateKey = Key(from),
            ToDateKey = Key(to)
        }, new TestServerCallContext())).Events.ToList();

    private static Task<GetEventResponse> GetAsync(World w, Event listed, bool withComplianceId = true)
    {
        var key = new EventKey { EventId = listed.Id, PlanDayKey = listed.PlanDayKey };
        if (withComplianceId)
        {
            key.ComplianceId = listed.ComplianceId;
        }

        return w.Events.GetEvent(new GetEventRequest { Key = key }, new TestServerCallContext());
    }

    private static string TextValue(Event evt) =>
        evt.Fields.Single(f => f.Label == "TextField").Value;

    /// <summary>
    /// Finding 2: a Monday occurrence moved to Tuesday with scope=this is in
    /// a Monday-Tuesday list on Tuesday; GetEvent finds that key (with and
    /// without compliance_id) with the list's etag.
    /// </summary>
    [Test]
    public async Task GetEvent_FindsAnOccurrenceMovedToAnotherDay_WithTheListsEtag()
    {
        var w = await SeedWorldAsync("moved");
        var monday = NextMonday();
        var tuesday = monday.AddDays(1);
        var series = await SeedSeriesAsync(w, "moved-weekly", monday, "1");
        var occurrence = await SeedOccurrenceAsync(w, series, monday, "moved-comment", "moved-value");
        await MoveThisAsync(w, series, monday, tuesday);

        var listed = (await ListAsync(w, monday, tuesday)).Single(e => e.ComplianceId == occurrence.ComplianceId);
        Assert.That(listed.PlanDayKey, Is.EqualTo(Key(tuesday)), "precondition: listed on the new day");

        var withId = await GetAsync(w, listed);
        var withoutId = await GetAsync(w, listed, withComplianceId: false);

        Assert.Multiple(() =>
        {
            Assert.That(withId.Found, Is.True, "found by its full key");
            Assert.That(withId.Event?.Etag, Is.EqualTo(listed.Etag));
            Assert.That(withId.Event, Is.EqualTo(listed));
            Assert.That(withoutId.Found, Is.True, "found by event id + day");
            Assert.That(withoutId.Event?.ComplianceId, Is.EqualTo(occurrence.ComplianceId));
        });
    }

    /// <summary>
    /// Finding 3: one daily planning, occurrence A on Tuesday and occurrence
    /// B (Monday, moved to Wednesday), each with its own comment and field
    /// value. GetEvent answers each occurrence with its OWN content, also when
    /// its lookup window spans the other occurrence.
    /// </summary>
    [Test]
    public async Task GetEvent_ReturnsTheOccurrencesOwnContent_WhenThePlanningHasSeveralOccurrences()
    {
        var w = await SeedWorldAsync("own-content");
        var monday = NextMonday();
        var series = await SeedSeriesAsync(w, "own-content-daily", monday, null);
        // A first, so it is the first case-backed row the calendar emits.
        var a = await SeedOccurrenceAsync(w, series, monday.AddDays(1), "comment-a", "value-a");
        var b = await SeedOccurrenceAsync(w, series, monday, "comment-b", "value-b");
        await MoveThisAsync(w, series, monday, monday.AddDays(2));

        var listed = await ListAsync(w, monday, monday.AddDays(2));
        var listedA = listed.Single(e => e.ComplianceId == a.ComplianceId);
        var listedB = listed.Single(e => e.ComplianceId == b.ComplianceId);
        var gotA = await GetAsync(w, listedA);
        var gotB = await GetAsync(w, listedB);

        Assert.Multiple(() =>
        {
            Assert.That(listedB.PlanDayKey, Is.EqualTo(Key(monday.AddDays(2))), "precondition: B moved");
            Assert.That(gotA.Found, Is.True);
            Assert.That(gotA.Event.Comment, Is.EqualTo("comment-a"));
            Assert.That(TextValue(gotA.Event), Is.EqualTo("value-a"));
            Assert.That(gotB.Found, Is.True);
            Assert.That(gotB.Event.MicrotingSdkCaseId, Is.EqualTo(b.CaseId));
            Assert.That(gotB.Event.Comment, Is.EqualTo("comment-b"), "B's own comment, not A's");
            Assert.That(TextValue(gotB.Event), Is.EqualTo("value-b"), "B's own field value, not A's");
        });
    }

    /// <summary>
    /// Finding 3, PRE-EXISTING list behaviour (identical on stable; this PR
    /// does not change it): in a multi-day ListEvents a later occurrence of
    /// the same planning carries its own ids but the comment and field values
    /// of the first case-backed occurrence. Pinned so a change to the legacy
    /// list is a visible, deliberate decision.
    /// </summary>
    [Test]
    public async Task ListEvents_MultiDay_LaterOccurrenceShowsTheFirstOccurrencesContent_PreExisting()
    {
        var w = await SeedWorldAsync("list-borrow");
        var monday = NextMonday();
        var series = await SeedSeriesAsync(w, "list-borrow-daily", monday, null);
        var first = await SeedOccurrenceAsync(w, series, monday, "comment-monday", "value-monday");
        var second = await SeedOccurrenceAsync(w, series, monday.AddDays(1), "comment-tuesday", "value-tuesday");

        var listed = await ListAsync(w, monday, monday.AddDays(1));
        var listedSecond = listed.Single(e => e.ComplianceId == second.ComplianceId);
        var oneDay = (await ListAsync(w, monday.AddDays(1), monday.AddDays(1)))
            .Single(e => e.ComplianceId == second.ComplianceId);
        var got = await GetAsync(w, listedSecond);

        Assert.Multiple(() =>
        {
            Assert.That(listed.Single(e => e.ComplianceId == first.ComplianceId).Comment,
                Is.EqualTo("comment-monday"));
            Assert.That(listedSecond.MicrotingSdkCaseId, Is.EqualTo(second.CaseId), "own ids");
            Assert.That(listedSecond.Comment, Is.EqualTo("comment-monday"), "pre-existing: first occurrence's comment");
            Assert.That(TextValue(listedSecond), Is.EqualTo("value-monday"), "pre-existing: first occurrence's value");
            Assert.That(oneDay.Comment, Is.EqualTo("comment-tuesday"), "a one-day list shows the own content");
            Assert.That(got.Event.Comment, Is.EqualTo("comment-tuesday"), "GetEvent shows the own content");
            Assert.That(got.Event.Etag, Is.EqualTo(oneDay.Etag), "GetEvent etag = one-day list etag");
            Assert.That(got.Event.Etag, Is.Not.EqualTo(listedSecond.Etag),
                "and therefore differs from the multi-day list etag for this occurrence");
        });
    }

    /// <summary>
    /// Property: for a small seeded calendar (a weekly series moved Monday ->
    /// Tuesday, a weekly Tuesday series, a daily series with an occurrence on
    /// each day, all with identical case content), every Event a
    /// Monday-Tuesday ListEvents returns is found by GetEvent - by its full
    /// key and by event id + day - with the same Event and etag.
    /// </summary>
    [Test]
    public async Task GetEvent_FindsEveryEventTheListReturns_WithTheSameEtag()
    {
        var w = await SeedWorldAsync("property");
        var monday = NextMonday();
        var tuesday = monday.AddDays(1);

        var moved = await SeedSeriesAsync(w, "p-moved", monday, "1");
        await SeedOccurrenceAsync(w, moved, monday, string.Empty, string.Empty);
        await MoveThisAsync(w, moved, monday, tuesday);

        var tuesdaySeries = await SeedSeriesAsync(w, "p-tuesday", tuesday, "2");
        await SeedOccurrenceAsync(w, tuesdaySeries, tuesday, string.Empty, string.Empty);

        var daily = await SeedSeriesAsync(w, "p-daily", monday, null);
        await SeedOccurrenceAsync(w, daily, monday, string.Empty, string.Empty);
        await SeedOccurrenceAsync(w, daily, tuesday, string.Empty, string.Empty);

        var listed = await ListAsync(w, monday, tuesday);
        Assert.That(listed, Has.Count.EqualTo(4), "precondition: four occurrences listed");

        foreach (var evt in listed)
        {
            var byFullKey = await GetAsync(w, evt);
            Assert.That(byFullKey.Found, Is.True, $"{evt.Id}/{evt.PlanDayKey}/{evt.ComplianceId} by full key");
            Assert.That(byFullKey.Event, Is.EqualTo(evt), $"{evt.Id}/{evt.PlanDayKey} same Event");
            Assert.That(byFullKey.Event.Etag, Is.EqualTo(evt.Etag), $"{evt.Id}/{evt.PlanDayKey} same etag");

            var byDay = await GetAsync(w, evt, withComplianceId: false);
            Assert.That(byDay.Found, Is.True, $"{evt.Id}/{evt.PlanDayKey} by event id + day");
        }
    }
}
