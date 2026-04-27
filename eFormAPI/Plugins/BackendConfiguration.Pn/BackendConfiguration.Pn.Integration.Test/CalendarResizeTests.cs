using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService;
using Microsoft.EntityFrameworkCore;
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
public class CalendarResizeTests : TestBaseSetup
{
    private IUserService _userService;
    private IBackendConfigurationTaskWizardService _taskWizardService;
    private BackendConfigurationCalendarService _calendarService;

    [SetUp]
    public async Task SetupCalendarService()
    {
        // Mirror CalendarOccurrenceExceptionTests.SetupCalendarService — clean
        // up FK-safe so each test starts fresh.
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

    private static DateTime GetNextMonday()
    {
        var today = DateTime.UtcNow.Date;
        var daysUntilMonday = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        if (daysUntilMonday == 0) daysUntilMonday = 7;
        return today.AddDays(daysUntilMonday);
    }

    /// <summary>
    /// Seeds a weekly recurring task starting on the given date with default
    /// StartHour=9.0, Duration=1.0. Returns the AreaRulePlanning Id.
    /// Mirrors CalendarOccurrenceExceptionTests.SeedWeeklyTask.
    /// </summary>
    private async Task<int> SeedWeeklyTask(DateTime startDate)
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

        var planning = new Planning
        {
            Enabled = true,
            RepeatEvery = 1,
            RepeatType = RepeatType.Week,
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
            RepeatType = 2,
            RepeatEvery = 1,
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

        return arp.Id;
    }

    private async Task SeedException(int arpId, DateTime originalDate, double startHour, double duration)
    {
        var ex = new CalendarOccurrenceException
        {
            AreaRulePlanningId = arpId,
            OriginalDate = originalDate.Date,
            IsDeleted = false,
            NewDate = null,
            StartHour = startHour,
            Duration = duration,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions.AddAsync(ex);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
    }

    // ---------------- 'this' scope ----------------

    [Test]
    public async Task ResizeTask_ScopeThis_Expand_WritesException()
    {
        // Default seed: StartHour=9, Duration=1. Expand: keep start, push end to 11:00 (Duration=2).
        var monday = GetNextMonday();
        var arpId = await SeedWeeklyTask(DateTime.SpecifyKind(monday, DateTimeKind.Utc));

        var result = await _calendarService.ResizeTask(new CalendarTaskResizeRequestModel
        {
            Id = arpId,
            OriginalDate = monday.ToString("yyyy-MM-dd") + "T00:00:00Z",
            NewStartHour = 9.0,
            NewDuration = 2.0,
            Scope = "this"
        });

        Assert.That(result.Success, Is.True, result.Message);

        var ex = await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions
            .Where(x => x.AreaRulePlanningId == arpId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .SingleAsync();

        Assert.That(ex.OriginalDate.Date, Is.EqualTo(monday.Date));
        Assert.That(ex.StartHour, Is.EqualTo(9.0));
        Assert.That(ex.Duration, Is.EqualTo(2.0));
        Assert.That(ex.IsDeleted, Is.False);
        Assert.That(ex.NewDate, Is.Null);
    }

    [Test]
    public async Task ResizeTask_ScopeThis_Shrink_WritesException()
    {
        // Shrink: pull start to 09:30, end stays at 10:00. StartHour=9.5, Duration=0.5.
        var monday = GetNextMonday();
        var arpId = await SeedWeeklyTask(DateTime.SpecifyKind(monday, DateTimeKind.Utc));

        var result = await _calendarService.ResizeTask(new CalendarTaskResizeRequestModel
        {
            Id = arpId,
            OriginalDate = monday.ToString("yyyy-MM-dd") + "T00:00:00Z",
            NewStartHour = 9.5,
            NewDuration = 0.5,
            Scope = "this"
        });

        Assert.That(result.Success, Is.True, result.Message);

        var ex = await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions
            .Where(x => x.AreaRulePlanningId == arpId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .SingleAsync();

        Assert.That(ex.StartHour, Is.EqualTo(9.5));
        Assert.That(ex.Duration, Is.EqualTo(0.5));
    }

    [Test]
    public async Task ResizeTask_ScopeThis_UpdatesExistingException()
    {
        // Pre-existing exception for the same OriginalDate must be updated in place,
        // not duplicated.
        var monday = GetNextMonday();
        var arpId = await SeedWeeklyTask(DateTime.SpecifyKind(monday, DateTimeKind.Utc));
        await SeedException(arpId, monday, 14.0, 0.5);

        var result = await _calendarService.ResizeTask(new CalendarTaskResizeRequestModel
        {
            Id = arpId,
            OriginalDate = monday.ToString("yyyy-MM-dd") + "T00:00:00Z",
            NewStartHour = 11.0,
            NewDuration = 1.5,
            Scope = "this"
        });

        Assert.That(result.Success, Is.True, result.Message);

        var exceptions = await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions
            .Where(x => x.AreaRulePlanningId == arpId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync();

        Assert.That(exceptions, Has.Count.EqualTo(1));
        Assert.That(exceptions[0].StartHour, Is.EqualTo(11.0));
        Assert.That(exceptions[0].Duration, Is.EqualTo(1.5));
    }

    // ---------------- 'all' scope ----------------

    [Test]
    public async Task ResizeTask_ScopeAll_UpdatesCalConfigStartHourAndDuration()
    {
        var monday = GetNextMonday();
        var arpId = await SeedWeeklyTask(DateTime.SpecifyKind(monday, DateTimeKind.Utc));

        var result = await _calendarService.ResizeTask(new CalendarTaskResizeRequestModel
        {
            Id = arpId,
            NewStartHour = 13.0,
            NewDuration = 2.0,
            Scope = "all"
        });

        Assert.That(result.Success, Is.True, result.Message);

        var calConfig = await BackendConfigurationPnDbContext!.CalendarConfigurations
            .Where(x => x.AreaRulePlanningId == arpId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .SingleAsync();

        Assert.That(calConfig.StartHour, Is.EqualTo(13.0));
        Assert.That(calConfig.Duration, Is.EqualTo(2.0));
    }

    [Test]
    public async Task ResizeTask_ScopeAll_DeletesEveryException()
    {
        var monday = GetNextMonday();
        var arpId = await SeedWeeklyTask(DateTime.SpecifyKind(monday, DateTimeKind.Utc));
        await SeedException(arpId, monday.AddDays(-7), 8.0, 0.5);   // past
        await SeedException(arpId, monday.AddDays(7), 14.0, 1.5);   // future

        var result = await _calendarService.ResizeTask(new CalendarTaskResizeRequestModel
        {
            Id = arpId,
            NewStartHour = 10.0,
            NewDuration = 1.0,
            Scope = "all"
        });

        Assert.That(result.Success, Is.True, result.Message);

        var remaining = await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions
            .Where(x => x.AreaRulePlanningId == arpId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync();

        Assert.That(remaining, Is.Empty);
    }

    [Test]
    public async Task ResizeTask_ScopeAll_DoesNotTouchSeriesStartDate()
    {
        // Resize is not a move: the recurrence anchor must not shift.
        var monday = GetNextMonday();
        var arpId = await SeedWeeklyTask(DateTime.SpecifyKind(monday, DateTimeKind.Utc));

        await _calendarService.ResizeTask(new CalendarTaskResizeRequestModel
        {
            Id = arpId,
            NewStartHour = 13.0,
            NewDuration = 2.0,
            Scope = "all"
        });

        var arp = await BackendConfigurationPnDbContext!.AreaRulePlannings.SingleAsync(x => x.Id == arpId);
        var planning = await ItemsPlanningPnDbContext!.Plannings.SingleAsync(x => x.Id == arp.ItemPlanningId);

        Assert.That(arp.StartDate!.Value.Date, Is.EqualTo(monday.Date));
        Assert.That(planning.StartDate.Date, Is.EqualTo(monday.Date));
    }

    // ---------------- 'thisAndFollowing' scope ----------------

    [Test]
    public async Task ResizeTask_ScopeThisAndFollowing_UpdatesCalConfig()
    {
        // Series started 2 weeks ago; resize on the upcoming Monday with
        // 'thisAndFollowing' updates calConfig (and anchors past — covered
        // separately).
        var monday = GetNextMonday();
        var seriesStart = monday.AddDays(-14);
        var arpId = await SeedWeeklyTask(DateTime.SpecifyKind(seriesStart, DateTimeKind.Utc));

        var result = await _calendarService.ResizeTask(new CalendarTaskResizeRequestModel
        {
            Id = arpId,
            OriginalDate = monday.ToString("yyyy-MM-dd") + "T00:00:00Z",
            NewStartHour = 10.0,
            NewDuration = 2.0,
            Scope = "thisAndFollowing"
        });

        Assert.That(result.Success, Is.True, result.Message);

        var calConfig = await BackendConfigurationPnDbContext!.CalendarConfigurations
            .Where(x => x.AreaRulePlanningId == arpId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .SingleAsync();

        Assert.That(calConfig.StartHour, Is.EqualTo(10.0));
        Assert.That(calConfig.Duration, Is.EqualTo(2.0));
    }

    [Test]
    public async Task ResizeTask_ScopeThisAndFollowing_DeletesExceptionsAtOrAfterOriginalDate()
    {
        var monday = GetNextMonday();
        var seriesStart = monday.AddDays(-14);
        var arpId = await SeedWeeklyTask(DateTime.SpecifyKind(seriesStart, DateTimeKind.Utc));

        await SeedException(arpId, seriesStart, 7.0, 0.5);          // past — should survive
        await SeedException(arpId, monday, 14.0, 0.5);              // on originalDate — gets deleted
        await SeedException(arpId, monday.AddDays(7), 15.0, 0.5);   // after — gets deleted

        var result = await _calendarService.ResizeTask(new CalendarTaskResizeRequestModel
        {
            Id = arpId,
            OriginalDate = monday.ToString("yyyy-MM-dd") + "T00:00:00Z",
            NewStartHour = 10.0,
            NewDuration = 2.0,
            Scope = "thisAndFollowing"
        });

        Assert.That(result.Success, Is.True, result.Message);

        var remaining = await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions
            .Where(x => x.AreaRulePlanningId == arpId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync();

        // Past exception kept + 1 backfill anchor for the second past Monday
        // (the seeded one anchors itself). The seeded past exception sits on
        // seriesStart; the backfill skips it (already present) and adds an
        // anchor for seriesStart + 7 days (the remaining past Monday before
        // originalDate). So 2 rows remain: the seeded one and the new anchor.
        Assert.That(remaining, Has.Count.EqualTo(2));
        Assert.That(remaining.All(r => r.OriginalDate.Date < monday.Date), Is.True);
    }

    [Test]
    public async Task ResizeTask_ScopeThisAndFollowing_AnchorsPastOccurrencesWithOldValues()
    {
        // BUG-FIX REGRESSION: series started 4 weeks ago. Resize the upcoming
        // Monday with 'thisAndFollowing' -> the 4 prior Mondays must be
        // anchored with new exception rows holding the OLD calConfig values
        // (StartHour=9.0, Duration=1.0), so they keep displaying their
        // original times instead of inheriting the new calConfig.
        var monday = GetNextMonday();
        var seriesStart = monday.AddDays(-28);
        var arpId = await SeedWeeklyTask(DateTime.SpecifyKind(seriesStart, DateTimeKind.Utc));

        var result = await _calendarService.ResizeTask(new CalendarTaskResizeRequestModel
        {
            Id = arpId,
            OriginalDate = monday.ToString("yyyy-MM-dd") + "T00:00:00Z",
            NewStartHour = 13.0,
            NewDuration = 3.0,
            Scope = "thisAndFollowing"
        });

        Assert.That(result.Success, Is.True, result.Message);

        var anchors = await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions
            .Where(x => x.AreaRulePlanningId == arpId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .OrderBy(x => x.OriginalDate)
            .ToListAsync();

        // 4 past Mondays (week 0..3, exclusive of monday at week 4)
        Assert.That(anchors, Has.Count.EqualTo(4));
        for (var i = 0; i < 4; i++)
        {
            var expectedDate = seriesStart.AddDays(i * 7).Date;
            Assert.That(anchors[i].OriginalDate.Date, Is.EqualTo(expectedDate));
            Assert.That(anchors[i].StartHour, Is.EqualTo(9.0), "old StartHour preserved");
            Assert.That(anchors[i].Duration, Is.EqualTo(1.0), "old Duration preserved");
            Assert.That(anchors[i].IsDeleted, Is.False);
            Assert.That(anchors[i].NewDate, Is.Null);
        }
    }

    [Test]
    public async Task ResizeTask_ScopeThisAndFollowing_DoesNotOverwriteExistingPastException()
    {
        // A past occurrence with a user-customized override must be left
        // alone — no new anchor row created on top, no field mutation.
        var monday = GetNextMonday();
        var seriesStart = monday.AddDays(-28);
        var arpId = await SeedWeeklyTask(DateTime.SpecifyKind(seriesStart, DateTimeKind.Utc));

        // User customized week 1 of the past
        var customDate = seriesStart.AddDays(7);
        await SeedException(arpId, customDate, 14.0, 0.5);

        await _calendarService.ResizeTask(new CalendarTaskResizeRequestModel
        {
            Id = arpId,
            OriginalDate = monday.ToString("yyyy-MM-dd") + "T00:00:00Z",
            NewStartHour = 10.0,
            NewDuration = 2.0,
            Scope = "thisAndFollowing"
        });

        var rowsForCustomDate = await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions
            .Where(x => x.AreaRulePlanningId == arpId)
            .Where(x => x.OriginalDate == customDate.Date)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync();

        Assert.That(rowsForCustomDate, Has.Count.EqualTo(1), "no duplicate anchor on top of an existing override");
        Assert.That(rowsForCustomDate[0].StartHour, Is.EqualTo(14.0), "user override preserved");
        Assert.That(rowsForCustomDate[0].Duration, Is.EqualTo(0.5), "user override preserved");
    }

    [Test]
    public async Task ResizeTask_ScopeThisAndFollowing_OriginalDateEqualsSeriesStart_NoBackfill()
    {
        // No past occurrences exist yet — backfill loop must run zero times.
        var monday = GetNextMonday();
        var arpId = await SeedWeeklyTask(DateTime.SpecifyKind(monday, DateTimeKind.Utc));

        await _calendarService.ResizeTask(new CalendarTaskResizeRequestModel
        {
            Id = arpId,
            OriginalDate = monday.ToString("yyyy-MM-dd") + "T00:00:00Z",
            NewStartHour = 10.0,
            NewDuration = 2.0,
            Scope = "thisAndFollowing"
        });

        var anchors = await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions
            .Where(x => x.AreaRulePlanningId == arpId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync();

        Assert.That(anchors, Is.Empty);
    }

    // ---------------- General ----------------

    [Test]
    public async Task ResizeTask_AllowsResizeOfPastOccurrence_NoTimeCheckError()
    {
        // The old UpdateTask flow rejected past start times with
        // "CannotCreateTaskInThePast". ResizeTask intentionally has no such
        // check — the task already exists, we are not creating a new one.
        var monday = GetNextMonday();
        var seriesStart = monday.AddDays(-30);
        var pastOccurrence = monday.AddDays(-7);
        var arpId = await SeedWeeklyTask(DateTime.SpecifyKind(seriesStart, DateTimeKind.Utc));

        var result = await _calendarService.ResizeTask(new CalendarTaskResizeRequestModel
        {
            Id = arpId,
            OriginalDate = pastOccurrence.ToString("yyyy-MM-dd") + "T00:00:00Z",
            NewStartHour = 9.0,
            NewDuration = 2.0,
            Scope = "this"
        });

        Assert.That(result.Success, Is.True, result.Message);
    }
}
