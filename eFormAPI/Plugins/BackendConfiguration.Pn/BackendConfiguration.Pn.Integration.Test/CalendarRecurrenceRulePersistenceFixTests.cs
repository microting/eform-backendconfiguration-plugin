using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Infrastructure.Models.TaskWizard;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService;
using BackendConfiguration.Pn.Services.EventDeployService;
using BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;
using BackendConfiguration.Pn.Services.CalendarChangeNotification;
using BackendConfiguration.Pn.Services.WorkerTagMembership;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eForm.Infrastructure.Models;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using NSubstitute;
using PluginRepeatType = BackendConfiguration.Pn.Infrastructure.Enums.RepeatType;
using PlanningRepeatType = Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// Locks in the calendar recurrence-rule persistence fixes:
///   #925/#938 — Planning.DayOfMonth derivation from start date per RepeatType
///   #926 — MoveTask (thisAndFollowing + all) re-derives the Nth-weekday rule
///   #927 — UpdateTask thisAndFollowing re-anchors on a changed date but keeps
///          the series start on an unchanged (pure field) edit
///   #929 — UpdateTask scope=all persists the weekly DayOfWeek
/// Mirrors the fixture/seed pattern of <see cref="CalendarUpdateTaskScopeTests"/>;
/// the task wizard is mocked, so these tests assert on the rows the
/// CalendarService itself writes plus the wizard mock's received calls.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class CalendarRecurrenceRulePersistenceFixTests : TestBaseSetup
{
    // ------------------------------------------------------------------
    // Date anchors — computed relative to UtcNow so the fixture never rots.
    //
    // The recurrence assertions below were originally written against a
    // hardcoded June/July 2026 calendar (series start = Thu 2026-06-04, the
    // 1st Thursday of June; next-month occurrence = Thu 2026-07-02, the 1st
    // Thursday of July; drag target = Wed 2026-07-01). That made the suite a
    // time-bomb: once "today" reached the series date, the CalendarService's
    // "can't create/edit a task in the past" guard (CannotCreateTaskInThePast)
    // started rejecting the edits and the tests failed for everyone.
    //
    // We reproduce the SAME calendar shape, anchored a couple of months into
    // the future:
    //   SeriesStart                — 1st Thursday of base month M (a Thursday)
    //   NextMonthFirstThu          — 1st Thursday of month M+1
    //   WedBeforeNextMonthFirstThu — the Wednesday before it (week 1 → ordinal 1)
    //   WeeklyThuPlus4/5/6         — Thursdays 4/5/6 weeks after SeriesStart
    //                                (the weekly-series occurrences)
    // M is chosen so M+1's 1st Thursday is not day 1 of the month, i.e. the
    // Wednesday-before lives in the same month and in week 1 — exactly the
    // 2026-07-01/02 shape the assertions rely on.
    // ------------------------------------------------------------------
    private static readonly DateTime SeriesStart;
    private static readonly DateTime NextMonthFirstThu;
    private static readonly DateTime WedBeforeNextMonthFirstThu;
    private static readonly DateTime WeeklyThuPlus4;
    private static readonly DateTime WeeklyThuPlus5;
    private static readonly DateTime WeeklyThuPlus6;

    static CalendarRecurrenceRulePersistenceFixTests()
    {
        var probeMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc)
            .AddMonths(2);
        while (true)
        {
            var baseThu = FirstThursdayOfMonth(probeMonth);
            var nextThu = FirstThursdayOfMonth(probeMonth.AddMonths(1));
            if (nextThu.Day > 1) // Wednesday-before stays inside the same month (week 1)
            {
                SeriesStart = baseThu;
                NextMonthFirstThu = nextThu;
                WedBeforeNextMonthFirstThu = nextThu.AddDays(-1);
                break;
            }
            probeMonth = probeMonth.AddMonths(1);
        }
        WeeklyThuPlus4 = SeriesStart.AddDays(28);
        WeeklyThuPlus5 = SeriesStart.AddDays(35);
        WeeklyThuPlus6 = SeriesStart.AddDays(42);
    }

    private static DateTime FirstThursdayOfMonth(DateTime anyDayInMonth)
    {
        var first = new DateTime(anyDayInMonth.Year, anyDayInMonth.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var offset = ((int)DayOfWeek.Thursday - (int)first.DayOfWeek + 7) % 7;
        return first.AddDays(offset);
    }

    private IUserService _userService;
    private IBackendConfigurationTaskWizardService _taskWizardService;
    private BackendConfigurationCalendarService _calendarService;

    [SetUp]
    public async Task SetupCalendarService()
    {
        // FK-safe cleanup so each test starts fresh.
        BackendConfigurationPnDbContext!.CalendarOccurrenceExceptionSites.RemoveRange(
            BackendConfigurationPnDbContext.CalendarOccurrenceExceptionSites);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.CalendarOccurrenceExceptions.RemoveRange(
            BackendConfigurationPnDbContext.CalendarOccurrenceExceptions);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.CalendarConfigurations.RemoveRange(
            BackendConfigurationPnDbContext.CalendarConfigurations);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.AreaRulePlannings.RemoveRange(
            BackendConfigurationPnDbContext.AreaRulePlannings);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.AreaRuleTranslations.RemoveRange(
            BackendConfigurationPnDbContext.AreaRuleTranslations);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.AreaRules.RemoveRange(
            BackendConfigurationPnDbContext.AreaRules);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.Areas.RemoveRange(
            BackendConfigurationPnDbContext.Areas);
        BackendConfigurationPnDbContext.Properties.RemoveRange(
            BackendConfigurationPnDbContext.Properties);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        ItemsPlanningPnDbContext!.Plannings.RemoveRange(
            ItemsPlanningPnDbContext.Plannings);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        _userService = Substitute.For<IUserService>();
        _userService.UserId.Returns(1);
        _userService.GetCurrentUserLanguage()
            .Returns(Task.FromResult(new Language { Id = 1, Name = "English", LanguageCode = "en-US" }));

        _taskWizardService = Substitute.For<IBackendConfigurationTaskWizardService>();
        // scope=all / thisAndFollowing delegate field updates to the wizard.
        _taskWizardService.UpdateTask(Arg.Any<TaskWizardCreateModel>())
            .Returns(Task.FromResult(new OperationResult(true)));

        _calendarService = new BackendConfigurationCalendarService(
            new BackendConfigurationLocalizationService(),
            _userService,
            BackendConfigurationPnDbContext!,
            null,
            Substitute.For<IEventDeployService>(),
            ItemsPlanningPnDbContext!,
            _taskWizardService,
            Substitute.For<ICalendarAssignmentReconciliationService>(),
            Substitute.For<ICalendarChangeNotifier>(),
            TestContextLogger<BackendConfigurationCalendarService>.Instance,
            Substitute.For<ICalendarOccurrenceRetractionService>(),
            Substitute.For<ICalendarPastSeriesBackfillService>(),
            // This fixture builds the calendar service without a core, so the shared
            // membership rule gets the same null: it is only reached when a request
            // carries SiteIds, and nothing here filters by site.
            new WorkerTagMembershipService(null)
        );
    }

    /// <summary>
    /// Seeds Area→Property→AreaRule(+translation)→Planning→AreaRulePlanning→
    /// CalendarConfiguration for a recurring series with explicit recurrence
    /// metadata. Returns the ARP Id. The wizard is mocked, so this seed is the
    /// only source of recurrence rows the assertions read back.
    /// </summary>
    private async Task<int> SeedTask(
        DateTime startDate,
        int arpRepeatType,
        int? repeatOrdinalWeek,
        int dayOfWeek,
        int dayOfMonth = 0,
        PlanningRepeatType planningRepeatType = PlanningRepeatType.Month,
        DayOfWeek? planningDayOfWeek = null,
        bool createdInGuide = false,
        int repeatEvery = 1,
        bool withCalendarConfiguration = true)
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
            Name = $"TestProp-{Guid.NewGuid()}", ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.Properties.AddAsync(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var areaRule = new AreaRule
        {
            AreaId = area.Id, PropertyId = property.Id, EformId = 0, CreatedInGuide = createdInGuide,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRules.AddAsync(areaRule);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var areaRuleTranslation = new AreaRuleTranslation
        {
            AreaRuleId = areaRule.Id, LanguageId = 1, Name = "Original Title",
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRuleTranslations.AddAsync(areaRuleTranslation);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var planning = new Planning
        {
            Enabled = true, RepeatEvery = repeatEvery, RepeatType = planningRepeatType, StartDate = startDate,
            DayOfWeek = planningDayOfWeek, RelatedEFormId = 0, Description = "Original description",
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = property.Id, AreaId = area.Id,
            ItemPlanningId = planning.Id, StartDate = startDate, Status = true,
            RepeatType = arpRepeatType, RepeatEvery = repeatEvery,
            DayOfWeek = dayOfWeek, DayOfMonth = dayOfMonth, RepeatOrdinalWeek = repeatOrdinalWeek,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(arp);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        if (!withCalendarConfiguration)
        {
            return arp.Id;
        }

        var calConfig = new CalendarConfiguration
        {
            AreaRulePlanningId = arp.Id, StartHour = 9.0, Duration = 1.0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.CalendarConfigurations.AddAsync(calConfig);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        return arp.Id;
    }

    private static CalendarTaskUpdateRequestModel BuildEdit(
        int arpId, DateTime startDate, string scope, DateTime originalDate,
        int repeatType, int? repeatOrdinalWeek,
        double startHour = 11.0, double duration = 2.0)
    {
        return new CalendarTaskUpdateRequestModel
        {
            Id = arpId,
            Scope = scope,
            OriginalDate = originalDate.ToString("yyyy-MM-dd") + "T00:00:00Z",
            StartDate = DateTime.SpecifyKind(startDate, DateTimeKind.Utc),
            StartHour = startHour,
            Duration = duration,
            Status = 1,
            RepeatType = repeatType,
            RepeatEvery = 1,
            RepeatOrdinalWeek = repeatOrdinalWeek,
            ComplianceEnabled = false,
            PropertyId = 0,
            EformId = 0,
            Sites = [101],
            TagIds = [],
            BoardId = 42,
            Color = "#abcdef",
            DescriptionHtml = "Edited description",
            Translates = [new CommonTranslationsModel { LanguageId = 1, Name = "Edited Title" }]
        };
    }

    // ------------------------------------------------------------------
    // A. DeriveDayOfMonth unit tests (#925/#938) — pure, no DB needed.
    //    These are calendar-math unit tests with no "now" comparison, so the
    //    literal dates are intentional and do not rot.
    // ------------------------------------------------------------------

    [TestCase(PluginRepeatType.Week, 2026, 6, 4, ExpectedResult = 1)]
    [TestCase(PluginRepeatType.Day, 2026, 6, 15, ExpectedResult = 1)]
    [TestCase(PluginRepeatType.Month, 2026, 6, 15, ExpectedResult = 15)]
    [TestCase(PluginRepeatType.Month, 2026, 1, 31, ExpectedResult = 28)] // capped at 28
    [TestCase(PluginRepeatType.Month, 2026, 6, 1, ExpectedResult = 1)]
    [TestCase(PluginRepeatType.Year, 2026, 6, 4, ExpectedResult = 4)]
    [TestCase(PluginRepeatType.Year, 2026, 12, 31, ExpectedResult = 31)] // NOT capped
    public int DeriveDayOfMonth_DerivesPerRepeatType(
        PluginRepeatType repeatType, int year, int month, int day)
    {
        return BackendConfigurationTaskWizardService.DeriveDayOfMonth(
            repeatType, new DateTime(year, month, day));
    }

    // ------------------------------------------------------------------
    // B. MoveTask thisAndFollowing re-derives the Nth-weekday rule (#926).
    // ------------------------------------------------------------------

    [Test]
    public async Task MoveTask_ThisAndFollowing_ReDerivesNthWeekday()
    {
        // Monthly 1st-Thursday rule starting on SeriesStart (1st Thu of month M).
        var arpId = await SeedTask(
            SeriesStart, arpRepeatType: 3, repeatOrdinalWeek: 1, dayOfWeek: 4,
            planningRepeatType: PlanningRepeatType.Month, planningDayOfWeek: DayOfWeek.Thursday);

        // Drag the 1st Thursday of month M+1 to the Wednesday before it.
        var originalDate = NextMonthFirstThu;
        var newDate = WedBeforeNextMonthFirstThu; // Wednesday, week 1
        var moveModel = new CalendarTaskMoveRequestModel
        {
            Id = arpId,
            OriginalDate = originalDate.ToString("yyyy-MM-dd") + "T00:00:00Z",
            NewDate = newDate.ToString("yyyy-MM-dd") + "T00:00:00Z",
            NewStartHour = 10.0,
            Scope = "thisAndFollowing"
        };

        var result = await _calendarService.MoveTask(moveModel);
        Assert.That(result.Success, Is.True, result.Message);

        var arp = await BackendConfigurationPnDbContext!.AreaRulePlannings.SingleAsync(x => x.Id == arpId);
        var planning = await ItemsPlanningPnDbContext!.Plannings.SingleAsync(x => x.Id == arp.ItemPlanningId);
        Assert.Multiple(() =>
        {
            Assert.That(arp.DayOfWeek, Is.EqualTo(3), "Wednesday");
            Assert.That(arp.RepeatOrdinalWeek, Is.EqualTo(1), "(1-1)/7+1 = 1st Wednesday");
            Assert.That(arp.StartDate!.Value.Date, Is.EqualTo(newDate.Date));
            Assert.That(planning.DayOfWeek, Is.EqualTo(DayOfWeek.Wednesday));
        });
    }

    // ------------------------------------------------------------------
    // C. MoveTask scope=all re-derives the Nth-weekday rule (#926).
    // ------------------------------------------------------------------

    [Test]
    public async Task MoveTask_ScopeAll_ReDerivesNthWeekday()
    {
        var arpId = await SeedTask(
            SeriesStart, arpRepeatType: 3, repeatOrdinalWeek: 1, dayOfWeek: 4,
            planningRepeatType: PlanningRepeatType.Month, planningDayOfWeek: DayOfWeek.Thursday);

        var newDate = WedBeforeNextMonthFirstThu; // Wednesday, week 1
        var moveModel = new CalendarTaskMoveRequestModel
        {
            Id = arpId,
            OriginalDate = NextMonthFirstThu.ToString("yyyy-MM-dd") + "T00:00:00Z",
            NewDate = newDate.ToString("yyyy-MM-dd") + "T00:00:00Z",
            NewStartHour = 10.0,
            Scope = "all"
        };

        var result = await _calendarService.MoveTask(moveModel);
        Assert.That(result.Success, Is.True, result.Message);

        var arp = await BackendConfigurationPnDbContext!.AreaRulePlannings.SingleAsync(x => x.Id == arpId);
        var planning = await ItemsPlanningPnDbContext!.Plannings.SingleAsync(x => x.Id == arp.ItemPlanningId);
        Assert.Multiple(() =>
        {
            Assert.That(arp.DayOfWeek, Is.EqualTo(3), "Wednesday");
            Assert.That(arp.RepeatOrdinalWeek, Is.EqualTo(1));
            Assert.That(arp.StartDate!.Value.Date, Is.EqualTo(newDate.Date));
            Assert.That(planning.DayOfWeek, Is.EqualTo(DayOfWeek.Wednesday));
        });
    }

    // ------------------------------------------------------------------
    // D. UpdateTask thisAndFollowing with a CHANGED date re-anchors (#927).
    // ------------------------------------------------------------------

    [Test]
    public async Task UpdateTask_ThisAndFollowing_ChangedDate_ReAnchorsSeries()
    {
        var arpId = await SeedTask(
            SeriesStart, arpRepeatType: 3, repeatOrdinalWeek: 1, dayOfWeek: 4,
            planningRepeatType: PlanningRepeatType.Month, planningDayOfWeek: DayOfWeek.Thursday);

        // OriginalDate = 1st Thursday of month M+1; the edit moves it to the
        // Wednesday before it.
        var model = BuildEdit(
            arpId,
            startDate: WedBeforeNextMonthFirstThu,
            scope: "thisAndFollowing",
            originalDate: NextMonthFirstThu,
            repeatType: 3,
            repeatOrdinalWeek: 1);

        var result = await _calendarService.UpdateTask(model);
        Assert.That(result.Success, Is.True, result.Message);

        var arp = await BackendConfigurationPnDbContext!.AreaRulePlannings.SingleAsync(x => x.Id == arpId);
        var planning = await ItemsPlanningPnDbContext!.Plannings.SingleAsync(x => x.Id == arp.ItemPlanningId);
        Assert.Multiple(() =>
        {
            Assert.That(arp.StartDate!.Value.Date, Is.EqualTo(WedBeforeNextMonthFirstThu.Date), "re-anchored");
            Assert.That(arp.DayOfWeek, Is.EqualTo(3), "Wednesday");
            Assert.That(arp.RepeatOrdinalWeek, Is.EqualTo(1));
            Assert.That(planning.StartDate.Date, Is.EqualTo(WedBeforeNextMonthFirstThu.Date));
        });
        // The wizard must rebuild the series from the re-anchored start date.
        await _taskWizardService.Received().UpdateTask(
            Arg.Is<TaskWizardCreateModel>(m => m.StartDate.HasValue && m.StartDate.Value.Date == WedBeforeNextMonthFirstThu.Date));

        // No backfill anchor may survive at/after the new anchor — it would
        // shadow the re-anchored series' own occurrences with stale values.
        var exceptions = await BackendConfigurationPnDbContext.CalendarOccurrenceExceptions
            .Where(x => x.AreaRulePlanningId == arpId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync();
        Assert.That(exceptions.Any(e => e.OriginalDate.Date >= WedBeforeNextMonthFirstThu.Date), Is.False,
            "no anchor may shadow the re-anchored series");
    }

    // ------------------------------------------------------------------
    // G. UpdateTask thisAndFollowing BACKWARD move to the SAME weekday must
    //    not leave a backfill anchor shadowing the re-anchored series (#927
    //    cutoff). This is the case the weekday-changing test D cannot trigger.
    // ------------------------------------------------------------------

    [Test]
    public async Task UpdateTask_ThisAndFollowing_BackwardSameWeekday_NoShadowAnchor()
    {
        // Weekly Thursday series from SeriesStart.
        var arpId = await SeedTask(
            SeriesStart, arpRepeatType: 2, repeatOrdinalWeek: null, dayOfWeek: 4,
            planningRepeatType: PlanningRepeatType.Week, planningDayOfWeek: DayOfWeek.Thursday);

        // Edit the (SeriesStart + 6 weeks) Thursday back to the earlier
        // (SeriesStart + 5 weeks) Thursday.
        var model = BuildEdit(
            arpId,
            startDate: WeeklyThuPlus5,
            scope: "thisAndFollowing",
            originalDate: WeeklyThuPlus6,
            repeatType: 2,
            repeatOrdinalWeek: null);

        var result = await _calendarService.UpdateTask(model);
        Assert.That(result.Success, Is.True, result.Message);

        var exceptions = await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions
            .Where(x => x.AreaRulePlanningId == arpId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync();
        Assert.Multiple(() =>
        {
            // The re-anchored Thursday (and everything after it) is regenerated
            // by the series, so no anchor may remain at/after it.
            Assert.That(exceptions.Any(e => e.OriginalDate.Date >= WeeklyThuPlus5.Date), Is.False,
                "the +5wk anchor (and later) must be cleared, not left to shadow the series");
            // Earlier occurrences (before the new anchor) stay pinned.
            Assert.That(exceptions.Any(e => e.OriginalDate.Date == WeeklyThuPlus4.Date), Is.True,
                "the +4wk occurrence stays anchored with its old values");
        });
    }

    // ------------------------------------------------------------------
    // H. MoveTask thisAndFollowing BACKWARD move to the SAME weekday — same
    //    cutoff guarantee on the drag path (#927 companion).
    // ------------------------------------------------------------------

    [Test]
    public async Task MoveTask_ThisAndFollowing_BackwardSameWeekday_NoShadowAnchor()
    {
        var arpId = await SeedTask(
            SeriesStart, arpRepeatType: 2, repeatOrdinalWeek: null, dayOfWeek: 4,
            planningRepeatType: PlanningRepeatType.Week, planningDayOfWeek: DayOfWeek.Thursday);

        var moveModel = new CalendarTaskMoveRequestModel
        {
            Id = arpId,
            OriginalDate = WeeklyThuPlus6.ToString("yyyy-MM-dd") + "T00:00:00Z",
            NewDate = WeeklyThuPlus5.ToString("yyyy-MM-dd") + "T00:00:00Z",
            NewStartHour = 10.0,
            Scope = "thisAndFollowing"
        };

        var result = await _calendarService.MoveTask(moveModel);
        Assert.That(result.Success, Is.True, result.Message);

        var exceptions = await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions
            .Where(x => x.AreaRulePlanningId == arpId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync();
        Assert.Multiple(() =>
        {
            Assert.That(exceptions.Any(e => e.OriginalDate.Date >= WeeklyThuPlus5.Date), Is.False,
                "the +5wk anchor (and later) must be cleared, not left to shadow the series");
            Assert.That(exceptions.Any(e => e.OriginalDate.Date == WeeklyThuPlus4.Date), Is.True,
                "the +4wk occurrence stays anchored");
        });
    }

    // ------------------------------------------------------------------
    // E. UpdateTask thisAndFollowing with an UNCHANGED date keeps the series
    //    start (#927 regression guard — pure field edit).
    // ------------------------------------------------------------------

    [Test]
    public async Task UpdateTask_ThisAndFollowing_UnchangedDate_KeepsSeriesStart()
    {
        var arpId = await SeedTask(
            SeriesStart, arpRepeatType: 3, repeatOrdinalWeek: 1, dayOfWeek: 4,
            planningRepeatType: PlanningRepeatType.Month, planningDayOfWeek: DayOfWeek.Thursday);

        // StartDate == OriginalDate → pure field edit, no date move.
        var model = BuildEdit(
            arpId,
            startDate: NextMonthFirstThu,
            scope: "thisAndFollowing",
            originalDate: NextMonthFirstThu,
            repeatType: 3,
            repeatOrdinalWeek: 1);

        var result = await _calendarService.UpdateTask(model);
        Assert.That(result.Success, Is.True, result.Message);

        var arp = await BackendConfigurationPnDbContext!.AreaRulePlannings.SingleAsync(x => x.Id == arpId);
        Assert.That(arp.StartDate!.Value.Date, Is.EqualTo(SeriesStart.Date),
            "series start must NOT be relocated on an unchanged-date edit");
        await _taskWizardService.Received().UpdateTask(
            Arg.Is<TaskWizardCreateModel>(m => m.StartDate.HasValue && m.StartDate.Value.Date == SeriesStart.Date));
    }

    // ------------------------------------------------------------------
    // F. UpdateTask scope=all persists the weekly DayOfWeek (#929).
    // ------------------------------------------------------------------

    [Test]
    public async Task UpdateTask_ScopeAll_PersistsWeeklyDayOfWeek()
    {
        // Weekly rule with a stale Sunday (0) DayOfWeek default that would
        // otherwise mislabel the event. SeriesStart is a Thursday.
        var arpId = await SeedTask(
            SeriesStart, arpRepeatType: 2, repeatOrdinalWeek: null, dayOfWeek: 0,
            planningRepeatType: PlanningRepeatType.Week, planningDayOfWeek: DayOfWeek.Thursday);

        var model = BuildEdit(
            arpId,
            startDate: SeriesStart,
            scope: "all",
            originalDate: SeriesStart,
            repeatType: 2,
            repeatOrdinalWeek: null);

        var result = await _calendarService.UpdateTask(model);
        Assert.That(result.Success, Is.True, result.Message);

        var arp = await BackendConfigurationPnDbContext!.AreaRulePlannings.SingleAsync(x => x.Id == arpId);
        Assert.That(arp.DayOfWeek, Is.EqualTo(4), "Thursday");
    }

    // ------------------------------------------------------------------
    // G. #1294 — an Nth-weekday rule keeps the weekday the dialog picked
    //    ("Månedligt på den første <ugedag>" ships it only in
    //    RepeatWeekdaysCsv), not the weekday of the clicked cell, and the
    //    items-planning Planning (the scheduler's master) mirrors it.
    // ------------------------------------------------------------------

    [Test]
    public async Task CreateTask_NthWeekday_TakesTheDialogWeekday_OnArpAndPlanning()
    {
        // The wizard is mocked, so pre-seed the ARP it would have created; the
        // calendar correlates it by property + CreatedInGuide + eForm.
        var arpId = await SeedTask(
            SeriesStart, arpRepeatType: 3, repeatOrdinalWeek: null, dayOfWeek: 0,
            planningRepeatType: PlanningRepeatType.Month, planningDayOfWeek: DayOfWeek.Sunday,
            createdInGuide: true);
        var seeded = await BackendConfigurationPnDbContext!.AreaRulePlannings.AsNoTracking()
            .SingleAsync(x => x.Id == arpId);
        _taskWizardService.CreateTask(Arg.Any<TaskWizardCreateModel>())
            .Returns(Task.FromResult(new OperationResult(true)));

        // The user clicked the Wednesday cell and picked "1st Monday".
        var result = await _calendarService.CreateTask(new CalendarTaskCreateRequestModel
        {
            PropertyId = seeded.PropertyId,
            FolderId = 1,
            EformId = 0,
            StartDate = DateTime.SpecifyKind(WedBeforeNextMonthFirstThu, DateTimeKind.Utc),
            StartHour = 9.0,
            Duration = 1.0,
            RepeatType = 3,
            RepeatEvery = 1,
            RepeatOrdinalWeek = 1,
            RepeatWeekdaysCsv = "1",
            DayOfMonth = 0,
            Status = 1,
            Sites = [101],
            Translates = [new CommonTranslationsModel { LanguageId = 1, Name = "Created Title" }]
        });
        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Model, Is.EqualTo(arpId));

        var arp = await BackendConfigurationPnDbContext.AreaRulePlannings.AsNoTracking().SingleAsync(x => x.Id == arpId);
        var planning = await ItemsPlanningPnDbContext!.Plannings.AsNoTracking().SingleAsync(x => x.Id == arp.ItemPlanningId);
        Assert.Multiple(() =>
        {
            Assert.That(arp.DayOfWeek, Is.EqualTo(1), "Monday, as picked — not the clicked Wednesday");
            Assert.That(arp.RepeatOrdinalWeek, Is.EqualTo(1));
            Assert.That(planning.DayOfWeek, Is.EqualTo(DayOfWeek.Monday), "the scheduler's weekday mirrors the ARP");
        });
    }

    [Test]
    public async Task UpdateTask_ScopeAll_NthWeekday_UnchangedDate_TakesTheDialogWeekday()
    {
        // "1st Thursday" whose user switches the dialog to "1st Monday" without
        // moving the date.
        var arpId = await SeedTask(
            SeriesStart, arpRepeatType: 3, repeatOrdinalWeek: 1, dayOfWeek: 4,
            planningRepeatType: PlanningRepeatType.Month, planningDayOfWeek: DayOfWeek.Thursday);

        var model = BuildEdit(arpId, startDate: NextMonthFirstThu, scope: "all",
            originalDate: NextMonthFirstThu, repeatType: 3, repeatOrdinalWeek: 1);
        model.RepeatWeekdaysCsv = "1";

        var result = await _calendarService.UpdateTask(model);
        Assert.That(result.Success, Is.True, result.Message);

        var arp = await BackendConfigurationPnDbContext!.AreaRulePlannings.AsNoTracking().SingleAsync(x => x.Id == arpId);
        var planning = await ItemsPlanningPnDbContext!.Plannings.AsNoTracking().SingleAsync(x => x.Id == arp.ItemPlanningId);
        Assert.Multiple(() =>
        {
            Assert.That(arp.DayOfWeek, Is.EqualTo(1), "Monday, as picked in the dialog");
            Assert.That(arp.RepeatWeekdaysCsv, Is.EqualTo("1"));
            Assert.That(planning.DayOfWeek, Is.EqualTo(DayOfWeek.Monday));
        });
    }

    [Test]
    public async Task UpdateTask_ThisAndFollowing_NthWeekday_UnchangedDate_TakesTheDialogWeekday()
    {
        var arpId = await SeedTask(
            SeriesStart, arpRepeatType: 3, repeatOrdinalWeek: 1, dayOfWeek: 4,
            planningRepeatType: PlanningRepeatType.Month, planningDayOfWeek: DayOfWeek.Thursday);

        var model = BuildEdit(arpId, startDate: NextMonthFirstThu, scope: "thisAndFollowing",
            originalDate: NextMonthFirstThu, repeatType: 3, repeatOrdinalWeek: 1);
        model.RepeatWeekdaysCsv = "1";

        var result = await _calendarService.UpdateTask(model);
        Assert.That(result.Success, Is.True, result.Message);

        var arp = await BackendConfigurationPnDbContext!.AreaRulePlannings.AsNoTracking().SingleAsync(x => x.Id == arpId);
        var planning = await ItemsPlanningPnDbContext!.Plannings.AsNoTracking().SingleAsync(x => x.Id == arp.ItemPlanningId);
        Assert.Multiple(() =>
        {
            Assert.That(arp.DayOfWeek, Is.EqualTo(1));
            Assert.That(planning.DayOfWeek, Is.EqualTo(DayOfWeek.Monday));
        });
    }

    /// <summary>
    /// When the anchor MOVES, the new date defines the rule (#1289 re-derives the
    /// ordinal from it) — a stale CSV (the task-list batch copies the stored one
    /// verbatim) must not drag the weekday back, and is rewritten to match.
    /// </summary>
    [Test]
    public async Task UpdateTask_ScopeAll_NthWeekday_DateChanged_TakesTheWeekdayOfTheNewDate()
    {
        var arpId = await SeedTask(
            SeriesStart, arpRepeatType: 3, repeatOrdinalWeek: 1, dayOfWeek: 4,
            planningRepeatType: PlanningRepeatType.Month, planningDayOfWeek: DayOfWeek.Thursday);

        var model = BuildEdit(arpId, startDate: WedBeforeNextMonthFirstThu, scope: "all",
            originalDate: NextMonthFirstThu, repeatType: 3, repeatOrdinalWeek: 1);
        model.RepeatWeekdaysCsv = "4";

        var result = await _calendarService.UpdateTask(model);
        Assert.That(result.Success, Is.True, result.Message);

        var arp = await BackendConfigurationPnDbContext!.AreaRulePlannings.AsNoTracking().SingleAsync(x => x.Id == arpId);
        var planning = await ItemsPlanningPnDbContext!.Plannings.AsNoTracking().SingleAsync(x => x.Id == arp.ItemPlanningId);
        Assert.Multiple(() =>
        {
            Assert.That(arp.DayOfWeek, Is.EqualTo(3), "Wednesday — the new anchor's weekday");
            Assert.That(arp.RepeatWeekdaysCsv, Is.EqualTo("3"), "the stale CSV follows the weekday");
            Assert.That(planning.DayOfWeek, Is.EqualTo(DayOfWeek.Wednesday));
        });
    }

    // ------------------------------------------------------------------
    // H. #1294 — a pattern change without a date move re-patterns open
    //    occurrences (and, for thisAndFollowing, splits the series).
    // ------------------------------------------------------------------

    private static DateTime FirstWeekdayOfMonth(DateTime anyDayInMonth, DayOfWeek dow)
    {
        var first = new DateTime(anyDayInMonth.Year, anyDayInMonth.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        return first.AddDays(((int)dow - (int)first.DayOfWeek + 7) % 7);
    }

    /// <summary>
    /// The relocation (and GetTasksForWeek's compliance loop) consult the SDK, so these
    /// tests need a calendar service wired to a real core.
    /// </summary>
    private async Task<BackendConfigurationCalendarService> BuildCalendarServiceWithCoreAsync()
    {
        var core = await GetCore();
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));
        return new BackendConfigurationCalendarService(
            new BackendConfigurationLocalizationService(), _userService, BackendConfigurationPnDbContext!,
            coreHelper, Substitute.For<IEventDeployService>(), ItemsPlanningPnDbContext!, _taskWizardService,
            Substitute.For<ICalendarAssignmentReconciliationService>(), Substitute.For<ICalendarChangeNotifier>(),
            TestContextLogger<BackendConfigurationCalendarService>.Instance,
            Substitute.For<ICalendarOccurrenceRetractionService>(), Substitute.For<ICalendarPastSeriesBackfillService>(),
            new WorkerTagMembershipService(coreHelper, BackendConfigurationPnDbContext!));
    }

    private async Task<int> SeedSdkCaseAsync(int status)
    {
        var language = await MicrotingDbContext!.Languages.FirstAsync();
        var site = new Site
        {
            Name = "Device B", MicrotingUid = Random.Shared.Next(100_000, 900_000), LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddAsync(site);
        await MicrotingDbContext.SaveChangesAsync();
        var sdkCase = new Case { SiteId = site.Id, Status = status, WorkflowState = Constants.WorkflowStates.Created };
        await MicrotingDbContext.Cases.AddAsync(sdkCase);
        await MicrotingDbContext.SaveChangesAsync();
        return sdkCase.Id;
    }

    private async Task<int> SeedComplianceAsync(int arpId, DateTime deadline, int sdkCaseId,
        string workflowState = Constants.WorkflowStates.Created)
    {
        var arp = await BackendConfigurationPnDbContext!.AreaRulePlannings.AsNoTracking().SingleAsync(x => x.Id == arpId);
        var compliance = new Compliance
        {
            PlanningId = arp.ItemPlanningId, PropertyId = arp.PropertyId, AreaId = arp.AreaId,
            Deadline = deadline, StartDate = deadline.AddDays(-7), MicrotingSdkCaseId = sdkCaseId,
            WorkflowState = workflowState, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.Compliances.AddAsync(compliance);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        return compliance.Id;
    }

    private async Task<DateTime> DeadlineOf(int complianceId)
        => (await BackendConfigurationPnDbContext!.Compliances.AsNoTracking().SingleAsync(x => x.Id == complianceId))
            .Deadline;

    /// <summary>A "1st Thursday" rule edited in the dialog to "1st Monday", date untouched.</summary>
    private CalendarTaskUpdateRequestModel DialogWeekdayEdit(int arpId, string scope)
    {
        var model = BuildEdit(arpId, startDate: NextMonthFirstThu, scope: scope,
            originalDate: NextMonthFirstThu, repeatType: 3, repeatOrdinalWeek: 1);
        model.RepeatWeekdaysCsv = "1";
        return model;
    }

    private Task<int> SeedFirstThursdayRule()
        => SeedTask(SeriesStart, arpRepeatType: 3, repeatOrdinalWeek: 1, dayOfWeek: 4,
            planningRepeatType: PlanningRepeatType.Month, planningDayOfWeek: DayOfWeek.Thursday);

    /// <summary>
    /// A dialog-only weekday change (date untouched) re-patterns the rule, so an open,
    /// deployed occurrence follows it to the new weekday in its own month — exactly what
    /// the date-change path's relocate branch does — never left behind as a second tile.
    /// </summary>
    [Test]
    public async Task UpdateTask_ScopeAll_NthWeekday_DialogWeekdayChange_RelocatesTheOpenCompliance()
    {
        var arpId = await SeedFirstThursdayRule();
        var service = await BuildCalendarServiceWithCoreAsync();
        var complianceId = await SeedComplianceAsync(arpId, NextMonthFirstThu, await SeedSdkCaseAsync(status: 33));

        var result = await service.UpdateTask(DialogWeekdayEdit(arpId, "all"));
        Assert.That(result.Success, Is.True, result.Message);

        Assert.That((await DeadlineOf(complianceId)).Date,
            Is.EqualTo(FirstWeekdayOfMonth(NextMonthFirstThu, DayOfWeek.Monday).Date),
            "the open occurrence follows the rule to the 1st Monday of its month");
    }

    /// <summary>Completed occurrences are immutable (R2) in the new branch too.</summary>
    [Test]
    public async Task UpdateTask_ScopeAll_NthWeekday_DialogWeekdayChange_LeavesACompletedRow()
    {
        var arpId = await SeedFirstThursdayRule();
        var service = await BuildCalendarServiceWithCoreAsync();
        var complianceId = await SeedComplianceAsync(arpId, NextMonthFirstThu, await SeedSdkCaseAsync(status: 100));

        var result = await service.UpdateTask(DialogWeekdayEdit(arpId, "all"));
        Assert.That(result.Success, Is.True, result.Message);

        Assert.That((await DeadlineOf(complianceId)).Date, Is.EqualTo(NextMonthFirstThu.Date));
    }

    /// <summary>
    /// The target is taken on the unique (PlanningId, Deadline) — here by a removed row.
    /// The relocation leaves the row in place instead of throwing after the rule was saved.
    /// </summary>
    [Test]
    public async Task UpdateTask_ScopeAll_Relocation_TargetTaken_LeavesTheRowAndSucceeds()
    {
        var arpId = await SeedFirstThursdayRule();
        var service = await BuildCalendarServiceWithCoreAsync();
        var complianceId = await SeedComplianceAsync(arpId, NextMonthFirstThu, await SeedSdkCaseAsync(status: 33));
        await SeedComplianceAsync(arpId, FirstWeekdayOfMonth(NextMonthFirstThu, DayOfWeek.Monday),
            await SeedSdkCaseAsync(status: 33), Constants.WorkflowStates.Removed);

        var result = await service.UpdateTask(DialogWeekdayEdit(arpId, "all"));

        Assert.That(result.Success, Is.True, result.Message);
        var arp = await BackendConfigurationPnDbContext!.AreaRulePlannings.AsNoTracking().SingleAsync(x => x.Id == arpId);
        await Assert.MultipleAsync(async () =>
        {
            Assert.That(arp.DayOfWeek, Is.EqualTo(1), "the rule edit itself is saved");
            Assert.That((await DeadlineOf(complianceId)).Date, Is.EqualTo(NextMonthFirstThu.Date), "left in place");
        });
    }

    /// <summary>
    /// A title-only edit opened from any tile of an Nth-weekday rule, with a built-in
    /// Month preset (no weekday CSV), must keep the stored weekday — not take the clicked
    /// tile's (here a Sunday, e.g. a legacy off-pattern compliance tile).
    /// </summary>
    [Test]
    public async Task UpdateTask_ScopeAll_NthWeekday_NoCsv_UnchangedDate_KeepsTheStoredWeekday()
    {
        var arpId = await SeedFirstThursdayRule();
        var sundayTile = NextMonthFirstThu.AddDays(3);
        var model = BuildEdit(arpId, startDate: sundayTile, scope: "all", originalDate: sundayTile,
            repeatType: 3, repeatOrdinalWeek: 1);
        model.RepeatWeekdaysCsv = null;

        var result = await _calendarService.UpdateTask(model);
        Assert.That(result.Success, Is.True, result.Message);

        var arp = await BackendConfigurationPnDbContext!.AreaRulePlannings.AsNoTracking().SingleAsync(x => x.Id == arpId);
        var planning = await ItemsPlanningPnDbContext!.Plannings.AsNoTracking().SingleAsync(x => x.Id == arp.ItemPlanningId);
        Assert.Multiple(() =>
        {
            Assert.That(arp.DayOfWeek, Is.EqualTo(4), "still Thursday");
            Assert.That(planning.DayOfWeek, Is.EqualTo(DayOfWeek.Thursday));
        });
    }

    /// <summary>
    /// "thisAndFollowing", date untouched, ordinal changed ("1st" → "2nd Thursday"): the
    /// series is split at the edited occurrence. The earlier month keeps its open row and
    /// renders ONCE (not also on its 2nd Thursday — the #1294 double tile); the row from
    /// the edited occurrence on moves to the new pattern.
    /// </summary>
    [Test]
    public async Task UpdateTask_ThisAndFollowing_OrdinalOnlyChange_SplitsTheSeries_PastMonthRendersOnce()
    {
        var arpId = await SeedFirstThursdayRule();
        var service = await BuildCalendarServiceWithCoreAsync();
        var earlierId = await SeedComplianceAsync(arpId, SeriesStart, await SeedSdkCaseAsync(status: 33));
        var editedId = await SeedComplianceAsync(arpId, NextMonthFirstThu, await SeedSdkCaseAsync(status: 33));

        var model = BuildEdit(arpId, startDate: NextMonthFirstThu, scope: "thisAndFollowing",
            originalDate: NextMonthFirstThu, repeatType: 3, repeatOrdinalWeek: 2);
        var result = await service.UpdateTask(model);
        Assert.That(result.Success, Is.True, result.Message);

        var arp = await BackendConfigurationPnDbContext!.AreaRulePlannings.AsNoTracking().SingleAsync(x => x.Id == arpId);
        await Assert.MultipleAsync(async () =>
        {
            Assert.That(arp.StartDate!.Value.Date, Is.EqualTo(NextMonthFirstThu.AddDays(7).Date),
                "split at the NEW pattern date of the edited occurrence's month (its 2nd Thursday)");
            Assert.That(arp.RepeatOrdinalWeek, Is.EqualTo(2));
            Assert.That((await DeadlineOf(earlierId)).Date, Is.EqualTo(SeriesStart.Date), "the earlier row stays");
            Assert.That((await DeadlineOf(editedId)).Date, Is.EqualTo(NextMonthFirstThu.AddDays(7).Date),
                "the edited occurrence's row moves to the 2nd Thursday");
        });

        // The earlier month: its 1st and 2nd Thursday weeks together hold exactly one tile.
        var tiles = new List<CalendarTaskResponseModel>();
        foreach (var monday in new[] { SeriesStart, SeriesStart.AddDays(7) }
                     .Select(d => d.Date.AddDays(-(((int)d.DayOfWeek + 6) % 7))))
        {
            var week = await service.GetTasksForWeek(new CalendarTaskRequestModel
            {
                PropertyId = arp.PropertyId,
                WeekStart = monday.ToString("yyyy-MM-ddT00:00:00Z"),
                WeekEnd = monday.AddDays(6).ToString("yyyy-MM-ddT23:59:00Z"),
                ActionableOnly = false,
                BoardIds = [], TagNames = [], SiteIds = []
            });
            Assert.That(week.Success, Is.True, week.Message);
            tiles.AddRange(week.Model.Where(t => t.PlanningId == arp.ItemPlanningId));
        }
        Assert.That(tiles.Select(t => t.TaskDate), Is.EquivalentTo(new[] { SeriesStart.ToString("yyyy-MM-dd") }),
            "the past month renders once, on its own open row");
    }

    /// <summary>
    /// The first future Monday for which the month's 1st Wednesday comes AFTER it
    /// (Monday on the 1st–5th) or BEFORE it (Monday on the 8th or later).
    /// </summary>
    private static DateTime FutureMonday(bool firstWednesdayAfterIt)
    {
        var d = DateTime.UtcNow.Date.AddDays(7);
        while (d.DayOfWeek != DayOfWeek.Monday || (firstWednesdayAfterIt ? d.Day > 5 : d.Day < 8))
        {
            d = d.AddDays(1);
        }
        return DateTime.SpecifyKind(d, DateTimeKind.Utc);
    }

    /// <summary>
    /// Playwright CR32 as a backend test: the dialog is opened on a MONDAY cell, custom
    /// month every 12, "Månedligt på den første" + Wednesday — the payload the spec
    /// captures (repeatType 3, repeatEvery 12, repeatOrdinalWeek 1, dayOfMonth 0,
    /// repeatWeekdaysCsv "3"). Since #1294 the rule keeps the picked Wednesday, so the
    /// anchor is not on the rule's weekday; the start week must still paint the task on
    /// its Monday — and nothing else — whether the month's 1st Wednesday comes before
    /// the Monday (#1207 option (b)) or after it (the anchor replaces the start month's
    /// pattern date).
    /// </summary>
    [TestCase(true, TestName = "CreateTask_CR32_FirstWednesdayAfterTheMondayAnchor_StartWeekPaintsTheMonday")]
    [TestCase(false, TestName = "CreateTask_CR32_FirstWednesdayBeforeTheMondayAnchor_StartWeekPaintsTheMonday")]
    public async Task CreateTask_CR32_StartWeekPaintsTheMondayAnchor(bool firstWednesdayAfterIt)
    {
        var monday = FutureMonday(firstWednesdayAfterIt);
        // The ARP/Planning the (mocked) wizard creates from the request.
        var arpId = await SeedTask(monday, arpRepeatType: 3, repeatOrdinalWeek: null, dayOfWeek: 0,
            planningRepeatType: PlanningRepeatType.Month, planningDayOfWeek: DayOfWeek.Monday,
            createdInGuide: true, repeatEvery: 12, withCalendarConfiguration: false);
        var seeded = await BackendConfigurationPnDbContext!.AreaRulePlannings.AsNoTracking()
            .SingleAsync(x => x.Id == arpId);
        _taskWizardService.CreateTask(Arg.Any<TaskWizardCreateModel>())
            .Returns(Task.FromResult(new OperationResult(true)));
        var service = await BuildCalendarServiceWithCoreAsync();

        var created = await service.CreateTask(new CalendarTaskCreateRequestModel
        {
            PropertyId = seeded.PropertyId, FolderId = 1, EformId = 0,
            StartDate = monday, StartHour = 15.0, Duration = 1.0,
            RepeatType = 3, RepeatEvery = 12, RepeatOrdinalWeek = 1, RepeatWeekdaysCsv = "3", DayOfMonth = 0,
            Status = 1, Sites = [101],
            Translates = [new CommonTranslationsModel { LanguageId = 1, Name = "CR32 Title" }]
        });
        Assert.That(created.Success, Is.True, created.Message);

        var arp = await BackendConfigurationPnDbContext.AreaRulePlannings.AsNoTracking().SingleAsync(x => x.Id == arpId);
        Assert.That(arp.DayOfWeek, Is.EqualTo((int)DayOfWeek.Wednesday), "the picked weekday is kept (#1294)");

        var week = await service.GetTasksForWeek(new CalendarTaskRequestModel
        {
            PropertyId = seeded.PropertyId,
            WeekStart = monday.ToString("yyyy-MM-ddT00:00:00Z"),
            WeekEnd = monday.AddDays(6).ToString("yyyy-MM-ddT23:59:00Z"),
            ActionableOnly = false,
            BoardIds = [], TagNames = [], SiteIds = []
        });
        Assert.That(week.Success, Is.True, week.Message);
        Assert.That(week.Model.Where(t => t.Id == arpId).Select(t => t.TaskDate),
            Is.EquivalentTo(new[] { monday.ToString("yyyy-MM-dd") }),
            "the start week paints the Monday anchor, and only it");
    }

    private async Task SetNextExecutionTimeAsync(int arpId, DateTime next)
    {
        var arp = await BackendConfigurationPnDbContext!.AreaRulePlannings.AsNoTracking().SingleAsync(x => x.Id == arpId);
        var planning = await ItemsPlanningPnDbContext!.Plannings.SingleAsync(x => x.Id == arp.ItemPlanningId);
        planning.NextExecutionTime = next;
        await ItemsPlanningPnDbContext.SaveChangesAsync();
    }

    private async Task<Planning> PlanningOf(int arpId)
    {
        var arp = await BackendConfigurationPnDbContext!.AreaRulePlannings.AsNoTracking().SingleAsync(x => x.Id == arpId);
        return await ItemsPlanningPnDbContext!.Plannings.AsNoTracking().SingleAsync(x => x.Id == arp.ItemPlanningId);
    }

    /// <summary>Every tile of the planning in the calendar months [firstMonth, firstMonth + months).</summary>
    private static async Task<List<DateTime>> TileDatesAsync(BackendConfigurationCalendarService service,
        int propertyId, int planningId, DateTime firstMonth, int months)
    {
        var from = new DateTime(firstMonth.Year, firstMonth.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = from.AddMonths(months);
        var monday = from.AddDays(-(((int)from.DayOfWeek + 6) % 7));
        var dates = new List<DateTime>();
        for (; monday < to; monday = monday.AddDays(7))
        {
            var week = await service.GetTasksForWeek(new CalendarTaskRequestModel
            {
                PropertyId = propertyId,
                WeekStart = monday.ToString("yyyy-MM-ddT00:00:00Z"),
                WeekEnd = monday.AddDays(6).ToString("yyyy-MM-ddT23:59:00Z"),
                ActionableOnly = false,
                BoardIds = [], TagNames = [], SiteIds = []
            });
            Assert.That(week.Success, Is.True, week.Message);
            dates.AddRange(week.Model.Where(t => t.PlanningId == planningId)
                .Select(t => DateTime.ParseExact(t.TaskDate, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));
        }
        return dates.Where(d => d >= from.Date && d < to.Date).OrderBy(d => d).ToList();
    }

    /// <summary>
    /// #1294 — "thisAndFollowing", date untouched, "1st Thursday" → "1st Monday" opened on
    /// the clicked Thursday tile. The split anchors on the month's NEW pattern date (its 1st
    /// Monday), so that month and every later one follow Monday: the tile's open row moves
    /// to the 1st Monday, NextExecutionTime follows, the earlier month keeps its own row,
    /// and every month shows exactly one tile. (All dates are months ahead of today.)
    /// </summary>
    [Test]
    public async Task UpdateTask_ThisAndFollowing_WeekdayOnlyChange_SplitsOnTheNewPatternDate()
    {
        var arpId = await SeedFirstThursdayRule();
        var service = await BuildCalendarServiceWithCoreAsync();
        await SetNextExecutionTimeAsync(arpId, NextMonthFirstThu);
        var earlierId = await SeedComplianceAsync(arpId, SeriesStart, await SeedSdkCaseAsync(status: 33));
        var editedId = await SeedComplianceAsync(arpId, NextMonthFirstThu, await SeedSdkCaseAsync(status: 33));
        var firstMondayNext = FirstWeekdayOfMonth(NextMonthFirstThu, DayOfWeek.Monday).Date;
        var firstMondayAfter = FirstWeekdayOfMonth(NextMonthFirstThu.AddMonths(1), DayOfWeek.Monday).Date;

        var result = await service.UpdateTask(DialogWeekdayEdit(arpId, "thisAndFollowing"));
        Assert.That(result.Success, Is.True, result.Message);

        var arp = await BackendConfigurationPnDbContext!.AreaRulePlannings.AsNoTracking().SingleAsync(x => x.Id == arpId);
        var planning = await PlanningOf(arpId);
        await Assert.MultipleAsync(async () =>
        {
            Assert.That(arp.StartDate!.Value.Date, Is.EqualTo(firstMondayNext), "split at the month's 1st Monday");
            Assert.That(arp.DayOfWeek, Is.EqualTo(1));
            Assert.That(planning.NextExecutionTime!.Value.Date, Is.EqualTo(firstMondayNext), "the next deploy follows");
            Assert.That((await DeadlineOf(editedId)).Date, Is.EqualTo(firstMondayNext), "the tile's open row moves");
            Assert.That((await DeadlineOf(earlierId)).Date, Is.EqualTo(SeriesStart.Date), "the earlier month is untouched");
        });

        var tiles = await TileDatesAsync(service, arp.PropertyId, arp.ItemPlanningId, SeriesStart, months: 3);
        Assert.That(tiles, Is.EqualTo(new[] { SeriesStart.Date, firstMondayNext, firstMondayAfter }),
            "exactly one tile per month: the earlier row, then the 1st Mondays");
    }

    /// <summary>#1294 — a weekday-only "all" edit re-snaps NextExecutionTime to the new day in its month.</summary>
    [Test]
    public async Task UpdateTask_ScopeAll_WeekdayOnlyChange_ResnapsNextExecutionTime()
    {
        var arpId = await SeedFirstThursdayRule();
        await SetNextExecutionTimeAsync(arpId, NextMonthFirstThu);

        var result = await _calendarService.UpdateTask(DialogWeekdayEdit(arpId, "all"));
        Assert.That(result.Success, Is.True, result.Message);

        Assert.That((await PlanningOf(arpId)).NextExecutionTime!.Value.Date,
            Is.EqualTo(FirstWeekdayOfMonth(NextMonthFirstThu, DayOfWeek.Monday).Date));
    }

    /// <summary>
    /// Pins the "all" weekday-only side effect: the series keeps its StartDate (a Thursday),
    /// which is now off the rule's weekday and so IS its start month's occurrence — the
    /// start-month row stays on it; a later month's row follows the new 1st Monday.
    /// </summary>
    [Test]
    public async Task UpdateTask_ScopeAll_WeekdayOnlyChange_StartMonthRowStays_LaterRowMoves()
    {
        var arpId = await SeedFirstThursdayRule();
        var service = await BuildCalendarServiceWithCoreAsync();
        var startMonthId = await SeedComplianceAsync(arpId, SeriesStart, await SeedSdkCaseAsync(status: 33));
        var laterId = await SeedComplianceAsync(arpId, NextMonthFirstThu, await SeedSdkCaseAsync(status: 33));

        var result = await service.UpdateTask(DialogWeekdayEdit(arpId, "all"));
        Assert.That(result.Success, Is.True, result.Message);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That((await DeadlineOf(startMonthId)).Date, Is.EqualTo(SeriesStart.Date),
                "the start month's occurrence is the StartDate itself");
            Assert.That((await DeadlineOf(laterId)).Date,
                Is.EqualTo(FirstWeekdayOfMonth(NextMonthFirstThu, DayOfWeek.Monday).Date));
        });
    }

    /// <summary>
    /// The relocation target is occupied by ANOTHER occurrence moved onto it (an exception
    /// whose NewDate is the target, OriginalDate elsewhere): the row is left in place.
    /// </summary>
    [Test]
    public async Task UpdateTask_ScopeAll_Relocation_OccurrenceMovedOntoTheTarget_LeavesTheRow()
    {
        var arpId = await SeedFirstThursdayRule();
        var service = await BuildCalendarServiceWithCoreAsync();
        var complianceId = await SeedComplianceAsync(arpId, NextMonthFirstThu, await SeedSdkCaseAsync(status: 33));
        await new CalendarOccurrenceException
        {
            AreaRulePlanningId = arpId, OriginalDate = NextMonthFirstThu.AddMonths(1),
            NewDate = FirstWeekdayOfMonth(NextMonthFirstThu, DayOfWeek.Monday),
            CreatedByUserId = 1, UpdatedByUserId = 1
        }.Create(BackendConfigurationPnDbContext!);

        var result = await service.UpdateTask(DialogWeekdayEdit(arpId, "all"));

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That((await DeadlineOf(complianceId)).Date, Is.EqualTo(NextMonthFirstThu.Date), "left in place");
    }
}
