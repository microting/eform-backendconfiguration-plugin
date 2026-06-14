using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Infrastructure.Models.TaskWizard;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService;
using BackendConfiguration.Pn.Services.EventDeployService;
using BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;
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
            NullLogger<BackendConfigurationCalendarService>.Instance
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
        DayOfWeek? planningDayOfWeek = null)
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
            AreaId = area.Id, PropertyId = property.Id, EformId = 0,
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
            Enabled = true, RepeatEvery = 1, RepeatType = planningRepeatType, StartDate = startDate,
            DayOfWeek = planningDayOfWeek, RelatedEFormId = 0, Description = "Original description",
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = property.Id, AreaId = area.Id,
            ItemPlanningId = planning.Id, StartDate = startDate, Status = true,
            RepeatType = arpRepeatType, RepeatEvery = 1,
            DayOfWeek = dayOfWeek, DayOfMonth = dayOfMonth, RepeatOrdinalWeek = repeatOrdinalWeek,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(arp);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

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
}
