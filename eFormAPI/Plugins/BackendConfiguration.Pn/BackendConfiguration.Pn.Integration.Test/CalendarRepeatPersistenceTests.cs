using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Enums;
using NSubstitute;

namespace BackendConfiguration.Pn.Integration.Test;

[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class CalendarRepeatPersistenceTests : TestBaseSetup
{
    private IUserService _userService = null!;
    private IBackendConfigurationTaskWizardService _taskWizardService = null!;
    private BackendConfigurationCalendarService _calendarService = null!;

    [SetUp]
    public async Task SetupCalendarService()
    {
        // Mirror CalendarOccurrenceExceptionTests teardown order — clean up
        // FK-safe so each test starts fresh. The base [SetUp] starts the
        // Testcontainers MariaDB container before this runs.
        BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions.RemoveRange(
            BackendConfigurationPnDbContext.CalendarOccurrenceExceptions);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.CalendarConfigurations.RemoveRange(
            BackendConfigurationPnDbContext.CalendarConfigurations);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.AreaRulePlannings.RemoveRange(
            BackendConfigurationPnDbContext.AreaRulePlannings);
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
        var mockLanguage = new Language { Id = 1, Name = "English", LanguageCode = "en-US" };
        _userService.GetCurrentUserLanguage().Returns(Task.FromResult(mockLanguage));

        _taskWizardService = Substitute.For<IBackendConfigurationTaskWizardService>();
        _taskWizardService.DeleteTask(Arg.Any<int>())
            .Returns(Task.FromResult(new OperationResult(true)));

        _calendarService = new BackendConfigurationCalendarService(
            new BackendConfigurationLocalizationService(),
            _userService,
            BackendConfigurationPnDbContext!,
            null,
            ItemsPlanningPnDbContext!,
            _taskWizardService,
            NullLogger<BackendConfigurationCalendarService>.Instance
        );
    }

    /// <summary>
    /// Returns the next future Monday (UTC), guaranteeing a fully-future
    /// startDate so create-task validation does not reject it.
    /// </summary>
    private static DateTime GetNextMonday()
    {
        var today = DateTime.UtcNow.Date;
        var daysUntilMonday = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        if (daysUntilMonday == 0) daysUntilMonday = 7;
        return DateTime.SpecifyKind(today.AddDays(daysUntilMonday), DateTimeKind.Utc);
    }

    private record SeededTask(int ArpId, int PropertyId, DateTime StartDate);

    /// <summary>
    /// Seeds an AreaRulePlanning + Planning + CalendarConfiguration row with
    /// the supplied repeat metadata. Mirrors the persistence shape that
    /// <see cref="BackendConfigurationCalendarService.CreateTask"/> ends up
    /// writing — but bypasses the TaskWizard dependency so the test focuses
    /// purely on the response-mapper round-trip and the iterator behaviour.
    /// </summary>
    private async Task<SeededTask> SeedTask(
        DateTime startDate,
        int arpRepeatType,
        int arpRepeatEvery,
        string? repeatWeekdaysCsv,
        int? repeatEndMode = null,
        int? repeatOccurrences = null,
        DateTime? repeatUntilDate = null,
        int? dayOfWeek = null,
        int? dayOfMonth = null)
    {
        var area = new Area
        {
            Type = AreaTypesEnum.Type1,
            ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Areas.AddAsync(area);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var property = new Property
        {
            Name = $"TestProp-{Guid.NewGuid()}",
            ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.Properties.AddAsync(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var areaRule = new AreaRule
        {
            AreaId = area.Id,
            PropertyId = property.Id,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRules.AddAsync(areaRule);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        // Map the ARP repeatType (1=daily, 2=weekly, 3=monthly) onto the
        // ItemsPlanning RepeatType enum the iterator switches on.
        var planningRepeatType = arpRepeatType switch
        {
            1 => RepeatType.Day,
            2 => RepeatType.Week,
            3 => RepeatType.Month,
            _ => RepeatType.Week
        };

        var planning = new Planning
        {
            Enabled = true,
            RepeatEvery = arpRepeatEvery,
            RepeatType = planningRepeatType,
            StartDate = startDate,
            RelatedEFormId = 0,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id,
            PropertyId = property.Id,
            AreaId = area.Id,
            ItemPlanningId = planning.Id,
            StartDate = startDate,
            Status = true,
            RepeatType = arpRepeatType,
            RepeatEvery = arpRepeatEvery,
            RepeatEndMode = repeatEndMode,
            RepeatOccurrences = repeatOccurrences,
            RepeatUntilDate = repeatUntilDate,
            DayOfWeek = dayOfWeek ?? 0,
            DayOfMonth = dayOfMonth ?? 0,
            RepeatWeekdaysCsv = repeatWeekdaysCsv,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(arp);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var calConfig = new CalendarConfiguration
        {
            AreaRulePlanningId = arp.Id,
            StartHour = 9.0,
            Duration = 1.0,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.CalendarConfigurations.AddAsync(calConfig);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        return new SeededTask(arp.Id, property.Id, startDate);
    }

    private static string IsoUtc(DateTime d) =>
        DateTime.SpecifyKind(d, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ");

    private async Task<List<CalendarTaskResponseModel>> FetchWeek(int propertyId, DateTime weekStart)
    {
        var ws = DateTime.SpecifyKind(weekStart.Date, DateTimeKind.Utc);
        var we = ws.AddDays(7).AddSeconds(-1);
        var result = await _calendarService.GetTasksForWeek(new CalendarTaskRequestModel
        {
            PropertyId = propertyId,
            WeekStart = IsoUtc(ws),
            WeekEnd = IsoUtc(we)
        });
        Assert.That(result.Success, Is.True, result.Message);
        return result.Model;
    }

    [Test]
    public async Task CreatesAndReadsBackMultiDayWeekly()
    {
        // Persist a weeklyMulti rule (every 2 weeks, Mon/Wed/Fri, after 10
        // occurrences) and verify every repeat field round-trips through
        // the response DTO unchanged.
        var monday = GetNextMonday();
        var seeded = await SeedTask(
            startDate: monday,
            arpRepeatType: 2,
            arpRepeatEvery: 2,
            repeatWeekdaysCsv: "1,3,5",
            repeatEndMode: 1,
            repeatOccurrences: 10,
            dayOfWeek: (int)monday.DayOfWeek);

        var tasks = await FetchWeek(seeded.PropertyId, monday);

        Assert.That(tasks, Is.Not.Empty);
        var task = tasks.First();
        Assert.Multiple(() =>
        {
            Assert.That(task.RepeatType, Is.EqualTo(2));
            Assert.That(task.RepeatEvery, Is.EqualTo(2));
            Assert.That(task.RepeatEndMode, Is.EqualTo(1));
            Assert.That(task.RepeatOccurrences, Is.EqualTo(10));
            Assert.That(task.RepeatUntilDate, Is.Null);
            Assert.That(task.RepeatWeekdaysCsv, Is.EqualTo("1,3,5"));
            Assert.That(task.DayOfWeek, Is.EqualTo((int)monday.DayOfWeek));
        });
    }

    [Test]
    public async Task CreatesAndReadsBackSingleDayWeekly()
    {
        // Single-day weekly (legacy weeklyOne) — RepeatWeekdaysCsv must
        // round-trip as null and the iterator must keep its single-day
        // behavior (one occurrence on the start-of-week anchor day).
        var monday = GetNextMonday();
        var seeded = await SeedTask(
            startDate: monday,
            arpRepeatType: 2,
            arpRepeatEvery: 1,
            repeatWeekdaysCsv: null,
            dayOfWeek: (int)monday.DayOfWeek);

        var tasks = await FetchWeek(seeded.PropertyId, monday);

        Assert.That(tasks, Has.Count.EqualTo(1));
        var task = tasks.First();
        Assert.Multiple(() =>
        {
            Assert.That(task.RepeatType, Is.EqualTo(2));
            Assert.That(task.RepeatEvery, Is.EqualTo(1));
            Assert.That(task.RepeatWeekdaysCsv, Is.Null);
            Assert.That(task.RepeatEndMode, Is.Null);
            Assert.That(task.RepeatOccurrences, Is.Null);
            Assert.That(task.RepeatUntilDate, Is.Null);
        });
    }

    [Test]
    public async Task MultiDayWeeklyExpandsToMultipleOccurrences()
    {
        // [1,3,5] = Mon/Wed/Fri every 2 weeks starting from `monday`. Week 0
        // emits 3 occurrences, week 1 emits 0 (off-cycle), week 2 emits 3
        // again. Locks in the iterator-fix contract.
        var monday = GetNextMonday();
        var seeded = await SeedTask(
            startDate: monday,
            arpRepeatType: 2,
            arpRepeatEvery: 2,
            repeatWeekdaysCsv: "1,3,5",
            dayOfWeek: (int)monday.DayOfWeek);

        var week0 = await FetchWeek(seeded.PropertyId, monday);
        Assert.That(week0, Has.Count.EqualTo(3),
            "week of startDate should emit Mon+Wed+Fri (3 occurrences)");
        var week0Dates = week0.Select(t => t.TaskDate).OrderBy(s => s).ToList();
        Assert.That(week0Dates, Is.EqualTo(new[]
        {
            monday.ToString("yyyy-MM-dd"),
            monday.AddDays(2).ToString("yyyy-MM-dd"),
            monday.AddDays(4).ToString("yyyy-MM-dd")
        }));

        var week1 = await FetchWeek(seeded.PropertyId, monday.AddDays(7));
        Assert.That(week1, Is.Empty,
            "week 1 is off-cycle for repeatEvery=2 — must be empty");

        var week2 = await FetchWeek(seeded.PropertyId, monday.AddDays(14));
        Assert.That(week2, Has.Count.EqualTo(3),
            "week 2 is on-cycle again — must emit Mon+Wed+Fri");
        var week2Dates = week2.Select(t => t.TaskDate).OrderBy(s => s).ToList();
        Assert.That(week2Dates, Is.EqualTo(new[]
        {
            monday.AddDays(14).ToString("yyyy-MM-dd"),
            monday.AddDays(16).ToString("yyyy-MM-dd"),
            monday.AddDays(18).ToString("yyyy-MM-dd")
        }));
    }

    [Test]
    public async Task MultiDayWeeklyAfterCapStopsAtTotalOccurrences()
    {
        // Mon+Wed+Fri every 2 weeks, capped at 10 occurrences. The cap counts
        // total occurrences across all matched weekdays (not weeks). Locks the
        // GetTasksForWeek bug fix where the previous code used the week-scoped
        // iterator for the cumulative count and never reached the cap.
        //
        // Expected emit pattern:
        //   week 0: Mon, Wed, Fri  (occ 1..3)
        //   week 1: empty (off-cycle)
        //   week 2: Mon, Wed, Fri  (occ 4..6)
        //   week 3: empty
        //   week 4: Mon, Wed, Fri  (occ 7..9)
        //   week 5: empty
        //   week 6: Mon            (occ 10 — cap reached, Wed/Fri trimmed)
        //   week 7+: empty
        var monday = GetNextMonday();
        var seeded = await SeedTask(
            startDate: monday,
            arpRepeatType: 2,
            arpRepeatEvery: 2,
            repeatWeekdaysCsv: "1,3,5",
            repeatEndMode: 1,
            repeatOccurrences: 10,
            dayOfWeek: (int)monday.DayOfWeek);

        var week6 = await FetchWeek(seeded.PropertyId, monday.AddDays(42));
        Assert.That(week6, Has.Count.EqualTo(1),
            "week 6 is on-cycle but cap=10 leaves only Monday");
        Assert.That(week6.Select(t => t.TaskDate).Single(),
            Is.EqualTo(monday.AddDays(42).ToString("yyyy-MM-dd")));

        var week8 = await FetchWeek(seeded.PropertyId, monday.AddDays(56));
        Assert.That(week8, Is.Empty,
            "after cap is reached at occ 10, no further occurrences emit");
    }
}
