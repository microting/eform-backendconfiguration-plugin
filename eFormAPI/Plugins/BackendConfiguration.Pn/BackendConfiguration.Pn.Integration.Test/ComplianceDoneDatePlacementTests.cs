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
using eFormCore;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Infrastructure.Models.ComplianceReport;
using BackendConfiguration.Pn.Infrastructure.Models.TaskWizard;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.BackendConfigurationCaseService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService;
using BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;
using BackendConfiguration.Pn.Services.CalendarChangeNotification;
using BackendConfiguration.Pn.Services.EventDeployService;
using BackendConfiguration.Pn.Services.WorkerTagMembership;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.eFormApi.BasePn.Infrastructure.Models.Application.Case.CaseEdit;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Enums;
using NSubstitute;

/// <summary>
/// #1370 / #1373 (option B) — a COMPLETED log is placed on the day it was DONE
/// (<c>DoneAtUserModifiable ?? DoneAt</c>, as a Danish date) in Rapport, Detaljer, Oversigt and
/// the calendar week view; an OPEN occurrence keeps its task date. The done date of an edited
/// log may not lie after today.
///
/// <para>Every date here is fixed and in the past, so nothing depends on the clock except the
/// two validation tests, which pin it through the services' <c>UtcNow</c> seam. Every
/// assertion is scoped to the property the test seeded (the database is shared and not
/// reset). SDK cases carry <c>MicrotingUid = null</c>, so no platform call is attempted.</para>
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class ComplianceDoneDatePlacementTests : TestBaseSetup
{
    private const int CompletedStatus = 100;
    private const int OpenCaseStatus = 33;

    /// <summary>A minimal eForm with one optional Comment, for the CaseService.Update tests.</summary>
    private const string CommentTemplateXml = @"
<?xml version='1.0' encoding='UTF-8'?>
<Main>
    <Id>9070</Id>
    <Repeated>0</Repeated>
    <Label>DoneDateMain</Label>
    <StartDate>2017-07-07</StartDate>
    <EndDate>2027-07-07</EndDate>
    <Language>da</Language>
    <MultiApproval>false</MultiApproval>
    <FastNavigation>false</FastNavigation>
    <Review>false</Review>
    <Summary>false</Summary>
    <DisplayOrder>0</DisplayOrder>
    <ElementList>
        <Element type='DataElement'>
            <Id>9070</Id>
            <Label>DoneDateElement</Label>
            <Description><![CDATA[DoneDateElement]]></Description>
            <DisplayOrder>0</DisplayOrder>
            <ReviewEnabled>false</ReviewEnabled>
            <ManualSync>false</ManualSync>
            <ExtraFieldsEnabled>false</ExtraFieldsEnabled>
            <DoneButtonDisabled>false</DoneButtonDisabled>
            <ApprovalEnabled>false</ApprovalEnabled>
            <DataItemList>
                <DataItem type='Comment'>
                    <Id>73670</Id>
                    <Label>Comment</Label>
                    <Description><![CDATA[Comment]]></Description>
                    <DisplayOrder>0</DisplayOrder>
                    <Multi>1</Multi>
                    <GeolocationEnabled>false</GeolocationEnabled>
                    <Split>false</Split>
                    <Value />
                    <ReadOnly>false</ReadOnly>
                    <Mandatory>false</Mandatory>
                    <Color>e8eaf6</Color>
                </DataItem>
            </DataItemList>
        </Element>
    </ElementList>
</Main>";

    // GetCore() is expensive; the database is not reset per test, so one Core serves
    // the whole fixture.
    private Core? _core;
    private int _uidCounter = 970_000;

    private async Task<Core> SharedCore() => _core ??= await GetCore();

    // ------------------------------------------------------------------
    // Services
    // ------------------------------------------------------------------

    private static IEFormCoreService CoreHelper(Core core)
    {
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));
        return coreHelper;
    }

    private async Task<IUserService> UserService()
    {
        var language = await MicrotingDbContext!.Languages.FirstAsync();
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        userService.GetCurrentUserLanguage().Returns(Task.FromResult(language));
        userService.GetCurrentUserLocale().Returns(Task.FromResult("en-US"));
        userService.GetCurrentUserAsync().Returns(Task.FromResult(new EformUser { Id = 1 }));
        return userService;
    }

    private async Task<BackendConfigurationComplianceReportService> ReportService(Core core)
    {
        var coreHelper = CoreHelper(core);
        return new BackendConfigurationComplianceReportService(
            new BackendConfigurationLocalizationService(), await UserService(),
            BackendConfigurationPnDbContext!, coreHelper, ItemsPlanningPnDbContext!,
            TestContextLogger<BackendConfigurationComplianceReportService>.Instance,
            new WorkerTagMembershipService(coreHelper, BackendConfigurationPnDbContext));
    }

    private async Task<BackendConfigurationCalendarService> CalendarService(Core core)
    {
        var coreHelper = CoreHelper(core);
        var taskWizardService = Substitute.For<IBackendConfigurationTaskWizardService>();
        taskWizardService.UpdateTask(Arg.Any<TaskWizardCreateModel>())
            .Returns(Task.FromResult(new OperationResult(true)));
        return new BackendConfigurationCalendarService(
            new BackendConfigurationLocalizationService(), await UserService(),
            BackendConfigurationPnDbContext!, coreHelper, Substitute.For<IEventDeployService>(),
            ItemsPlanningPnDbContext!, taskWizardService,
            Substitute.For<ICalendarAssignmentReconciliationService>(),
            Substitute.For<ICalendarChangeNotifier>(),
            TestContextLogger<BackendConfigurationCalendarService>.Instance,
            Substitute.For<ICalendarOccurrenceRetractionService>(),
            Substitute.For<ICalendarPastSeriesBackfillService>(),
            new WorkerTagMembershipService(coreHelper, BackendConfigurationPnDbContext));
    }

    private async Task<BackendConfigurationCaseService> CaseService(Core core, DateTime utcNow)
        => new(
            ItemsPlanningPnDbContext!,
            TestContextLogger<BackendConfigurationCaseService>.Instance,
            CoreHelper(core),
            // Test-project stub that echoes the resource key.
            new BackendConfigurationLocalizationService(),
            await UserService())
        {
            UtcNow = () => utcNow
        };

    // ------------------------------------------------------------------
    // Seeding
    // ------------------------------------------------------------------

    private sealed class Series
    {
        public int PropertyId;
        public int AreaId;
        public int ArpId;
        public int PlanningId;
    }

    private sealed class Occurrence
    {
        public int SdkCaseId;
        public int ComplianceId;
        public int PlanningCaseId;
    }

    /// <summary>
    /// Area → Property → AreaRule(+translation) → a weekly Monday Planning from
    /// 2024-09-02 → AreaRulePlanning with a report headline (Rapport excludes
    /// headline-less tasks, #1301).
    /// </summary>
    private async Task<Series> SeedSeries(string title, int everyMonths = 0)
    {
        var area = new Area
        {
            Type = AreaTypesEnum.Type1, ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Areas.AddAsync(area);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var property = new Property
        {
            Name = $"DoneDate-{Guid.NewGuid()}", ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.Properties.AddAsync(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var areaRule = new AreaRule
        {
            AreaId = area.Id, PropertyId = property.Id, EformId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRules.AddAsync(areaRule);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        await BackendConfigurationPnDbContext.AreaRuleTranslations.AddAsync(new AreaRuleTranslation
        {
            AreaRuleId = areaRule.Id, LanguageId = 1, Name = title,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var startDate = new DateTime(2024, 9, 2, 0, 0, 0, DateTimeKind.Utc); // a Monday
        var monthly = everyMonths > 0;
        var planning = new Planning
        {
            Enabled = true,
            RepeatEvery = monthly ? everyMonths : 1,
            RepeatType = monthly ? RepeatType.Month : RepeatType.Week,
            DayOfMonth = monthly ? 2 : null,
            StartDate = startDate, DayOfWeek = DayOfWeek.Monday, RelatedEFormId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var headline = new PlanningTag
        {
            Name = $"DoneDate headline {Guid.NewGuid()}",
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext.PlanningTags.AddAsync(headline);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = property.Id, AreaId = area.Id,
            ItemPlanningId = planning.Id, StartDate = startDate, Status = true,
            ItemPlanningTagId = headline.Id,
            // Missed occurrences are reported, so an open past row shows in Detaljer.
            ComplianceEnabled = true,
            RepeatType = monthly ? 3 : 2, RepeatEvery = monthly ? everyMonths : 1,
            RepeatWeekdaysCsv = monthly ? null : "1", DayOfWeek = 1, DayOfMonth = monthly ? 2 : 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(arp);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        return new Series
        {
            PropertyId = property.Id, AreaId = area.Id, ArpId = arp.Id, PlanningId = planning.Id
        };
    }

    /// <summary>
    /// One occurrence on <paramref name="deadline"/>. A completed one has case
    /// <c>Status = 100</c>, the given done timestamps and a soft-removed Compliance — the
    /// shape every completion path leaves. <paramref name="checkListId"/> defaults to a
    /// bare CheckList (no fields), which is all the report needs.
    /// </summary>
    private async Task<Occurrence> SeedOccurrence(
        Series series, DateTime deadline, DateTime? doneAt, DateTime? doneAtUserModifiable = null,
        int? checkListId = null, int? siteId = null)
    {
        var completed = doneAt.HasValue;
        siteId ??= await SeedSdkSite();
        checkListId ??= await SeedCheckList();

        var sdkCase = new Case
        {
            SiteId = siteId,
            Status = completed ? CompletedStatus : OpenCaseStatus,
            DoneAt = doneAt,
            DoneAtUserModifiable = doneAtUserModifiable ?? doneAt,
            CheckListId = checkListId,
            MicrotingUid = null,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext!.Cases.AddAsync(sdkCase);
        await MicrotingDbContext.SaveChangesAsync();

        var planningCase = new PlanningCase
        {
            PlanningId = series.PlanningId,
            MicrotingSdkeFormId = checkListId.Value,
            MicrotingSdkCaseId = sdkCase.Id,
            Status = completed ? CompletedStatus : 66,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.PlanningCases.AddAsync(planningCase);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        await ItemsPlanningPnDbContext.PlanningCaseSites.AddAsync(new PlanningCaseSite
        {
            PlanningId = series.PlanningId,
            PlanningCaseId = planningCase.Id,
            MicrotingSdkSiteId = siteId.Value,
            MicrotingSdkeFormId = checkListId.Value,
            MicrotingSdkCaseId = sdkCase.Id,
            Status = completed ? CompletedStatus : 66,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var compliance = new Compliance
        {
            ItemName = "Done date item",
            PlanningId = series.PlanningId,
            PropertyId = series.PropertyId,
            AreaId = series.AreaId,
            Deadline = DateTime.SpecifyKind(deadline.Date, DateTimeKind.Utc),
            StartDate = DateTime.SpecifyKind(deadline.Date.AddDays(-7), DateTimeKind.Utc),
            MicrotingSdkCaseId = sdkCase.Id,
            MicrotingSdkeFormId = checkListId.Value,
            WorkflowState = completed ? Constants.WorkflowStates.Removed : Constants.WorkflowStates.Created,
            CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Compliances.AddAsync(compliance);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        return new Occurrence
        {
            SdkCaseId = sdkCase.Id, ComplianceId = compliance.Id, PlanningCaseId = planningCase.Id
        };
    }

    private async Task<int> SeedSdkSite()
    {
        var uid = ++_uidCounter;
        var language = await MicrotingDbContext!.Languages.FirstAsync();
        var site = new Site
        {
            Name = $"done-date-site-{uid}",
            MicrotingUid = uid,
            LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddAsync(site);
        await MicrotingDbContext.SaveChangesAsync();
        return site.Id;
    }

    private async Task<int> SeedCheckList()
    {
        var checkList = new CheckList
        {
            Label = $"done-date-{Guid.NewGuid()}",
            ParentId = null,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext!.CheckLists.AddAsync(checkList);
        await MicrotingDbContext.SaveChangesAsync();
        return checkList.Id;
    }

    private static DateTime Utc(int y, int m, int d, int h = 0, int min = 0)
        => new(y, m, d, h, min, 0, DateTimeKind.Utc);

    // ------------------------------------------------------------------
    // Reading the views
    // ------------------------------------------------------------------

    /// <summary>Detaljer: (complianceId → TaskDate) for the property and window, status "all".</summary>
    private async Task<Dictionary<int, string>> Detaljer(Core core, int propertyId, DateTime from, DateTime to)
    {
        var result = await (await ReportService(core)).Index(new ComplianceReportRequestModel
        {
            DateFrom = from, DateTo = to, Status = "all", PropertyId = propertyId,
            BoardIds = [], TagIds = [], SiteIds = [], PageIndex = 0, PageSize = 0
        });
        Assert.That(result.Success, Is.True, result.Message);
        return result.Model.Entities.ToDictionary(r => r.ComplianceId, r => r.TaskDate);
    }

    /// <summary>Rapport: (complianceId → TaskDate) for the property and window.</summary>
    private async Task<Dictionary<int, string>> Rapport(Core core, int propertyId, DateTime from, DateTime to)
    {
        var result = await (await ReportService(core)).EformColumns(new ComplianceReportRequestModel
        {
            DateFrom = from, DateTo = to, Status = "all", PropertyId = propertyId,
            BoardIds = [], TagIds = [], SiteIds = [], PageSize = 0
        });
        Assert.That(result.Success, Is.True, result.Message);
        return result.Model
            .SelectMany(g => g.Templates)
            .SelectMany(t => t.Cases)
            .ToDictionary(c => c.ComplianceId, c => c.TaskDate);
    }

    /// <summary>Oversigt: (Total, Done) of the property's row.</summary>
    private async Task<(int Total, int Done)> Oversigt(Core core, int propertyId, DateTime from, DateTime to)
    {
        var result = await (await ReportService(core)).Overview(new ComplianceReportOverviewRequestModel
        {
            DateFrom = from, DateTo = to, PropertyId = propertyId,
            BoardIds = [], TagIds = [], SiteIds = []
        });
        Assert.That(result.Success, Is.True, result.Message);
        var row = result.Model.Rows.SingleOrDefault(r => r.PropertyId == propertyId);
        return row == null ? (0, 0) : (row.Total, row.Done);
    }

    private static string IsoUtc(DateTime d) =>
        DateTime.SpecifyKind(d, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    /// <summary>The week view's compliance tiles for the property, Monday..Sunday.</summary>
    private async Task<List<CalendarTaskResponseModel>> Week(Core core, int propertyId, DateTime monday)
    {
        var result = await (await CalendarService(core)).GetTasksForWeek(new CalendarTaskRequestModel
        {
            PropertyId = propertyId,
            WeekStart = IsoUtc(monday),
            WeekEnd = IsoUtc(monday.AddDays(6).AddHours(23).AddMinutes(59)),
            ActionableOnly = false,
            BoardIds = [], TagNames = [], SiteIds = []
        });
        Assert.That(result.Success, Is.True, result.Message);
        return result.Model;
    }

    private void ClearTrackers()
    {
        BackendConfigurationPnDbContext!.ChangeTracker.Clear();
        ItemsPlanningPnDbContext!.ChangeTracker.Clear();
        MicrotingDbContext!.ChangeTracker.Clear();
    }

    // ==================================================================
    // Rapport, Detaljer, Oversigt — one builder, one rule
    // ==================================================================

    /// <summary>
    /// The customer's case: a log completed early in December carries next January's
    /// deadline. "År til dato" in the new year must not list it; the old year does, on its
    /// done date, in all three views.
    /// </summary>
    [Test]
    public async Task CompletedEarly_DeadlineNextYear_BelongsToTheYearItWasDone()
    {
        var core = await SharedCore();
        var series = await SeedSeries("Done early");
        var log = await SeedOccurrence(series, deadline: Utc(2025, 1, 13), doneAt: Utc(2024, 12, 20, 10));

        var (from2025, to2025) = (Utc(2025, 1, 1), Utc(2025, 3, 31));
        var (from2024, to2024) = (Utc(2024, 12, 1), Utc(2024, 12, 31));

        var detaljer2025 = await Detaljer(core, series.PropertyId, from2025, to2025);
        var rapport2025 = await Rapport(core, series.PropertyId, from2025, to2025);
        var oversigt2025 = await Oversigt(core, series.PropertyId, from2025, to2025);
        var detaljer2024 = await Detaljer(core, series.PropertyId, from2024, to2024);
        var rapport2024 = await Rapport(core, series.PropertyId, from2024, to2024);
        var oversigt2024 = await Oversigt(core, series.PropertyId, from2024, to2024);

        Assert.Multiple(() =>
        {
            Assert.That(detaljer2025, Does.Not.ContainKey(log.ComplianceId), "Detaljer, the deadline's year");
            Assert.That(rapport2025, Does.Not.ContainKey(log.ComplianceId), "Rapport, the deadline's year");
            Assert.That(oversigt2025, Is.EqualTo((0, 0)), "Oversigt, the deadline's year");

            Assert.That(detaljer2024.GetValueOrDefault(log.ComplianceId), Is.EqualTo("2024-12-20"),
                "Detaljer, the done date's year");
            Assert.That(rapport2024.GetValueOrDefault(log.ComplianceId), Is.EqualTo("2024-12-20"),
                "Rapport, the done date's year");
            Assert.That(oversigt2024, Is.EqualTo((1, 1)), "Oversigt, the done date's year");
        });
    }

    /// <summary>
    /// A task repeating every 24 months, done 20 months before its deadline, is found in the
    /// period it was done in — beyond the default reach, through the long-interval reach — in
    /// the report and in the calendar.
    /// </summary>
    [Test]
    public async Task LongIntervalTask_DoneMonthsEarly_IsFoundInItsDonePeriod()
    {
        var core = await SharedCore();
        var series = await SeedSeries("Every 24 months", everyMonths: 24);
        var log = await SeedOccurrence(series, deadline: Utc(2026, 9, 2), doneAt: Utc(2025, 1, 8, 10));

        var detaljer = await Detaljer(core, series.PropertyId, Utc(2025, 1, 1), Utc(2025, 1, 31));
        var rapport = await Rapport(core, series.PropertyId, Utc(2025, 1, 1), Utc(2025, 1, 31));
        var week = await Week(core, series.PropertyId, Utc(2025, 1, 6));

        Assert.Multiple(() =>
        {
            Assert.That(detaljer.GetValueOrDefault(log.ComplianceId), Is.EqualTo("2025-01-08"), "Detaljer");
            Assert.That(rapport.GetValueOrDefault(log.ComplianceId), Is.EqualTo("2025-01-08"), "Rapport");
            Assert.That(week.SingleOrDefault(t => t.ComplianceId == log.ComplianceId)?.TaskDate,
                Is.EqualTo("2025-01-08"), "Kalender");
        });
    }

    /// <summary>A late completion moves FORWARD: done in January, dated November.</summary>
    [Test]
    public async Task CompletedLate_BelongsToThePeriodItWasDone()
    {
        var core = await SharedCore();
        var series = await SeedSeries("Done late");
        var log = await SeedOccurrence(series, deadline: Utc(2024, 11, 25), doneAt: Utc(2025, 1, 6, 9));

        var november = await Detaljer(core, series.PropertyId, Utc(2024, 11, 1), Utc(2024, 11, 30));
        var january = await Detaljer(core, series.PropertyId, Utc(2025, 1, 1), Utc(2025, 1, 31));

        Assert.Multiple(() =>
        {
            Assert.That(november, Does.Not.ContainKey(log.ComplianceId), "the deadline's month");
            Assert.That(january.GetValueOrDefault(log.ComplianceId), Is.EqualTo("2025-01-06"), "the done date's month");
        });
    }

    /// <summary>
    /// An OPEN occurrence is untouched by the rule: it stays on its deadline, in the same
    /// window as the completed log it sits beside.
    /// </summary>
    [Test]
    public async Task OpenOccurrence_KeepsItsDeadline()
    {
        var core = await SharedCore();
        var series = await SeedSeries("Open stays");
        var open = await SeedOccurrence(series, deadline: Utc(2025, 1, 20), doneAt: null);
        var done = await SeedOccurrence(series, deadline: Utc(2025, 1, 27), doneAt: Utc(2025, 1, 21, 8));

        var rows = await Detaljer(core, series.PropertyId, Utc(2025, 1, 1), Utc(2025, 1, 31));

        Assert.Multiple(() =>
        {
            Assert.That(rows.GetValueOrDefault(open.ComplianceId), Is.EqualTo("2025-01-20"), "open → deadline");
            Assert.That(rows.GetValueOrDefault(done.ComplianceId), Is.EqualTo("2025-01-21"), "done → done date");
        });
    }

    /// <summary>
    /// The done date is the DANISH date the user sees: 23:30 UTC on New Year's Eve is
    /// 00:30 on 1 January in Copenhagen.
    /// </summary>
    [Test]
    public async Task DoneDate_IsTheCopenhagenDate()
    {
        var core = await SharedCore();
        var series = await SeedSeries("Copenhagen midnight");
        var log = await SeedOccurrence(series, deadline: Utc(2024, 12, 30), doneAt: Utc(2024, 12, 31, 23, 30));

        var december = await Detaljer(core, series.PropertyId, Utc(2024, 12, 1), Utc(2024, 12, 31));
        var january = await Detaljer(core, series.PropertyId, Utc(2025, 1, 1), Utc(2025, 1, 31));

        Assert.Multiple(() =>
        {
            Assert.That(december, Does.Not.ContainKey(log.ComplianceId), "not on the UTC date");
            Assert.That(january.GetValueOrDefault(log.ComplianceId), Is.EqualTo("2025-01-01"), "on the Danish date");
        });
    }

    /// <summary>The user-edited done date wins over the device's DoneAt.</summary>
    [Test]
    public async Task DoneAtUserModifiable_WinsOverDoneAt()
    {
        var core = await SharedCore();
        var series = await SeedSeries("User edited");
        var log = await SeedOccurrence(series, deadline: Utc(2025, 2, 10),
            doneAt: Utc(2025, 2, 10, 9), doneAtUserModifiable: Utc(2025, 2, 4, 9));

        var rows = await Rapport(core, series.PropertyId, Utc(2025, 2, 1), Utc(2025, 2, 28));

        Assert.That(rows.GetValueOrDefault(log.ComplianceId), Is.EqualTo("2025-02-04"));
    }

    /// <summary>
    /// A deleted log (#1290's IsDeleted marker on the DEADLINE date) stays hidden in the
    /// period of its done date too: the marker identifies the occurrence, not its placement.
    /// </summary>
    [Test]
    public async Task DeletedLog_StaysHiddenInTheDoneDatesPeriod()
    {
        var core = await SharedCore();
        var series = await SeedSeries("Deleted");
        var log = await SeedOccurrence(series, deadline: Utc(2025, 1, 13), doneAt: Utc(2024, 12, 20, 10));
        await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions.AddAsync(new CalendarOccurrenceException
        {
            AreaRulePlanningId = series.ArpId,
            OriginalDate = Utc(2025, 1, 13),
            IsDeleted = true,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        Assert.That(await Detaljer(core, series.PropertyId, Utc(2024, 12, 1), Utc(2024, 12, 31)),
            Does.Not.ContainKey(log.ComplianceId));
    }

    // ==================================================================
    // Calendar week view
    // ==================================================================

    /// <summary>
    /// A log due Monday 13 January and done Wednesday 8 January is shown in the week of
    /// 6 January, on the 8th, completed. The week of the 13th shows neither the log nor an
    /// open tile for the occurrence it completed: recurrence suppression stays keyed by the
    /// deadline, so nothing is duplicated.
    /// </summary>
    [Test]
    public async Task Calendar_CompletedLog_IsOnItsDoneDate_AndItsOccurrenceIsNotReopened()
    {
        var core = await SharedCore();
        var series = await SeedSeries("Calendar early");
        var log = await SeedOccurrence(series, deadline: Utc(2025, 1, 13), doneAt: Utc(2025, 1, 8, 10));

        var doneWeek = await Week(core, series.PropertyId, Utc(2025, 1, 6));
        var deadlineWeek = await Week(core, series.PropertyId, Utc(2025, 1, 13));

        var tile = doneWeek.SingleOrDefault(t => t.ComplianceId == log.ComplianceId);
        Assert.Multiple(() =>
        {
            Assert.That(tile, Is.Not.Null, "the done date's week renders the log");
            Assert.That(tile?.TaskDate, Is.EqualTo("2025-01-08"));
            Assert.That(tile?.Completed, Is.True);

            Assert.That(deadlineWeek.Where(t => t.ComplianceId == log.ComplianceId), Is.Empty,
                "the deadline's week does not render the log");
            Assert.That(deadlineWeek.Where(t => t.TaskDate == "2025-01-13" && t.PlanningId == series.PlanningId),
                Is.Empty, "no open tile re-appears on the completed occurrence's date");
        });
    }

    /// <summary>Done in the deadline's own week, on another day: shown on the done day.</summary>
    [Test]
    public async Task Calendar_CompletedLog_SameWeek_IsOnTheDoneDay()
    {
        var core = await SharedCore();
        var series = await SeedSeries("Calendar same week");
        var log = await SeedOccurrence(series, deadline: Utc(2025, 2, 3), doneAt: Utc(2025, 2, 6, 12));

        var week = await Week(core, series.PropertyId, Utc(2025, 2, 3));

        Assert.That(week.Single(t => t.ComplianceId == log.ComplianceId).TaskDate, Is.EqualTo("2025-02-06"));
    }

    /// <summary>
    /// Moved ("this" scope) from Monday 3 March to Wednesday 12 March, then completed on
    /// Tuesday 11 March: the week of 10 March shows the occurrence ONCE — the completed log on
    /// its done date — not also an open tile on the moved-to Wednesday.
    /// </summary>
    [Test]
    public async Task Calendar_MovedThenCompleted_IsOneTile()
    {
        var core = await SharedCore();
        var series = await SeedSeries("Moved then completed");
        var move = new CalendarOccurrenceException
        {
            AreaRulePlanningId = series.ArpId,
            OriginalDate = Utc(2025, 3, 3),
            NewDate = Utc(2025, 3, 12),
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions.AddAsync(move);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        var log = await SeedOccurrence(series, deadline: Utc(2025, 3, 3), doneAt: Utc(2025, 3, 11, 10));

        var week = await Week(core, series.PropertyId, Utc(2025, 3, 10));

        var tiles = week.Where(t => t.ExceptionId == move.Id || t.ComplianceId == log.ComplianceId).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(tiles, Has.Count.EqualTo(1), "one tile for the moved, completed occurrence");
            Assert.That(tiles.FirstOrDefault()?.ComplianceId, Is.EqualTo(log.ComplianceId));
            Assert.That(tiles.FirstOrDefault()?.TaskDate, Is.EqualTo("2025-03-11"));
            Assert.That(tiles.FirstOrDefault()?.Completed, Is.True);
            Assert.That(week.Where(t => t.TaskDate == "2025-03-12"), Is.Empty, "no open tile on the moved-to day");
        });
    }

    // ==================================================================
    // BackendConfigurationCaseService.Update — the edit path of Rapport
    // ==================================================================

    /// <summary>
    /// A done date after today (Copenhagen) is refused and nothing is written. Pinned at
    /// 22:30 UTC on 10 June, which is already 11 June in Copenhagen: the 12th is refused.
    /// </summary>
    [Test]
    public async Task CaseUpdate_RefusesADoneDateAfterToday()
    {
        var core = await SharedCore();
        var scenario = await SeedEditableLog("Refused");
        var service = await CaseService(core, utcNow: Utc(2026, 6, 10, 22, 30));

        var result = await service.Update(new ReplyRequest
        {
            Id = scenario.Log.SdkCaseId,
            ElementList = new List<CaseEditRequest>(),
            DoneAt = new DateTime(2026, 6, 12, 0, 0, 0, DateTimeKind.Utc),
            SiteId = scenario.SiteId
        });

        ClearTrackers();
        var reloaded = await MicrotingDbContext!.Cases.AsNoTracking().FirstAsync(c => c.Id == scenario.Log.SdkCaseId);
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Is.EqualTo("DoneDateCannotBeInTheFuture"));
            Assert.That(reloaded.DoneAtUserModifiable, Is.EqualTo(scenario.DoneAt), "nothing was written");
        });
    }

    /// <summary>
    /// Today is accepted — the Danish today, which at 22:30 UTC on 10 June is the 11th — and
    /// the log STAYS on the picked day although it was done at 22:30 UTC (00:30 Danish): the
    /// time of day is grafted on in Danish time, so the stored instant is 22:30 UTC on the
    /// 10th, the 11th in Copenhagen (a UTC graft would have stored the 12th, tomorrow). The
    /// edited date moves the log in Detaljer and in the calendar (option B: no occurrence
    /// exception is written, the placement follows the done date).
    /// </summary>
    [Test]
    public async Task CaseUpdate_AcceptsTodayInCopenhagen_AndTheLogMovesEverywhere()
    {
        var core = await SharedCore();
        var scenario = await SeedEditableLog("Accepted");
        var service = await CaseService(core, utcNow: Utc(2026, 6, 10, 22, 30));

        // The edit dialog sends a picked day as that day's UTC midnight.
        var result = await service.Update(new ReplyRequest
        {
            Id = scenario.Log.SdkCaseId,
            ElementList = new List<CaseEditRequest>(),
            DoneAt = new DateTime(2026, 6, 11, 0, 0, 0, DateTimeKind.Utc),
            SiteId = scenario.SiteId
        });
        Assert.That(result.Success, Is.True, result.Message);

        ClearTrackers();
        var stored = (await MicrotingDbContext!.Cases.AsNoTracking()
            .FirstAsync(c => c.Id == scenario.Log.SdkCaseId)).DoneAtUserModifiable;
        var detaljer = await Detaljer(core, scenario.Series.PropertyId, Utc(2026, 6, 1), Utc(2026, 6, 30));
        var week = await Week(core, scenario.Series.PropertyId, Utc(2026, 6, 8));
        var exceptions = await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions
            .AsNoTracking().CountAsync(x => x.AreaRulePlanningId == scenario.Series.ArpId);

        Assert.Multiple(() =>
        {
            Assert.That(stored, Is.EqualTo(new DateTime(2026, 6, 10, 22, 30, 0)), "00:30 Danish on the 11th");
            Assert.That(detaljer.GetValueOrDefault(scenario.Log.ComplianceId), Is.EqualTo("2026-06-11"), "Detaljer");
            Assert.That(week.SingleOrDefault(t => t.ComplianceId == scenario.Log.ComplianceId)?.TaskDate,
                Is.EqualTo("2026-06-11"), "Kalender");
            Assert.That(exceptions, Is.EqualTo(0), "option A (an occurrence exception) is not used");
        });
    }

    private sealed class EditableLog
    {
        public Series Series = null!;
        public Occurrence Log = null!;
        public int SiteId;
        public DateTime DoneAt;
    }

    /// <summary>
    /// A completed log on a real eForm (CaseService.Update runs core.CaseUpdate), due Monday
    /// 8 June 2026 and done at 22:30 UTC on Friday 5 June — 00:30 on the 6th in Copenhagen.
    /// </summary>
    private async Task<EditableLog> SeedEditableLog(string title)
    {
        var core = await SharedCore();
        var template = await core.TemplateFromXml(CommentTemplateXml);
        var templateId = await core.TemplateCreate(template);
        var siteId = await SeedSdkSite();
        var series = await SeedSeries(title);
        var doneAt = Utc(2026, 6, 5, 22, 30);
        var log = await SeedOccurrence(series, deadline: Utc(2026, 6, 8), doneAt: doneAt,
            checkListId: templateId, siteId: siteId);
        return new EditableLog { Series = series, Log = log, SiteId = siteId, DoneAt = doneAt };
    }
}
