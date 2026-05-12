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
public class CalendarOccurrenceExceptionTests : TestBaseSetup
{
    private IUserService _userService;
    private IBackendConfigurationTaskWizardService _taskWizardService;
    private BackendConfigurationCalendarService _calendarService;

    [SetUp]
    public async Task SetupCalendarService()
    {
        // Clean up all test data in FK-safe order so each test starts with a fresh state
        // (base [SetUp] runs first, so contexts are already available here)
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

        // NUnit calls base.Setup() automatically
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
        if (daysUntilMonday == 0) daysUntilMonday = 7; // always future
        return today.AddDays(daysUntilMonday);
    }

    /// <summary>
    /// Seeds an AreaRulePlanning with a linked Planning and CalendarConfiguration.
    /// Returns the AreaRulePlanning Id.
    /// </summary>
    private async Task<int> SeedWeeklyTask(DateTime startDate)
    {
        // 1. Seed Area
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

        // 2. Seed Property
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

        // 3. Seed AreaRule
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

        // 4. Seed Planning (ItemsPlanning)
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

        // 5. Seed AreaRulePlanning
        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id,
            PropertyId = property.Id,
            AreaId = area.Id,
            ItemPlanningId = planning.Id,
            StartDate = startDate,
            Status = true,
            RepeatType = 2, // weeks
            RepeatEvery = 1,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(arp);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        // 6. Seed CalendarConfiguration
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

    [Test]
    public async Task MoveTask_ScopeThis_CreatesException()
    {
        // Arrange
        var baseMonday = GetNextMonday();
        var startDate = DateTime.SpecifyKind(baseMonday, DateTimeKind.Utc);
        var arpId = await SeedWeeklyTask(startDate);

        var originalDate = baseMonday.ToString("yyyy-MM-dd") + "T00:00:00Z";
        var newDate = baseMonday.AddDays(2).ToString("yyyy-MM-dd") + "T00:00:00Z";

        var moveModel = new CalendarTaskMoveRequestModel
        {
            Id = arpId,
            OriginalDate = originalDate,
            NewDate = newDate,
            NewStartHour = 10.0,
            Scope = "this"
        };

        // Act
        var result = await _calendarService.MoveTask(moveModel);

        // Assert
        Assert.That(result.Success, Is.True, result.Message);

        var exception = await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions
            .Where(x => x.AreaRulePlanningId == arpId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .FirstOrDefaultAsync();

        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.OriginalDate.Date, Is.EqualTo(baseMonday.Date));
        Assert.That(exception.NewDate!.Value.Date, Is.EqualTo(baseMonday.AddDays(2).Date));
        Assert.That(exception.StartHour, Is.EqualTo(10.0));
        Assert.That(exception.IsDeleted, Is.False);
    }

    [Test]
    public async Task MoveTask_ScopeThis_UpdatesExistingException()
    {
        // Arrange
        var baseMonday = GetNextMonday();
        var startDate = DateTime.SpecifyKind(baseMonday, DateTimeKind.Utc);
        var arpId = await SeedWeeklyTask(startDate);

        // Pre-create an exception for that occurrence
        var preExisting = new CalendarOccurrenceException
        {
            AreaRulePlanningId = arpId,
            OriginalDate = baseMonday,
            NewDate = baseMonday.AddDays(1),
            StartHour = 8.0,
            IsDeleted = false,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions.AddAsync(preExisting);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var moveModel = new CalendarTaskMoveRequestModel
        {
            Id = arpId,
            OriginalDate = baseMonday.ToString("yyyy-MM-dd") + "T00:00:00Z",
            NewDate = baseMonday.AddDays(3).ToString("yyyy-MM-dd") + "T00:00:00Z",
            NewStartHour = 14.0,
            Scope = "this"
        };

        // Act
        var result = await _calendarService.MoveTask(moveModel);

        // Assert
        Assert.That(result.Success, Is.True, result.Message);

        var exceptions = await BackendConfigurationPnDbContext.CalendarOccurrenceExceptions
            .Where(x => x.AreaRulePlanningId == arpId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync();

        Assert.That(exceptions, Has.Count.EqualTo(1));
        Assert.That(exceptions[0].NewDate!.Value.Date, Is.EqualTo(baseMonday.AddDays(3).Date));
        Assert.That(exceptions[0].StartHour, Is.EqualTo(14.0));
    }

    [Test]
    public async Task MoveTask_ScopeAll_ClearsAllExceptions()
    {
        // Arrange
        var baseMonday = GetNextMonday();
        var startDate = DateTime.SpecifyKind(baseMonday, DateTimeKind.Utc);
        var arpId = await SeedWeeklyTask(startDate);

        // Create two exceptions
        for (var i = 0; i < 2; i++)
        {
            var ex = new CalendarOccurrenceException
            {
                AreaRulePlanningId = arpId,
                OriginalDate = baseMonday.AddDays(i),
                IsDeleted = false,
                WorkflowState = Constants.WorkflowStates.Created,
                CreatedByUserId = 1,
                UpdatedByUserId = 1
            };
            await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions.AddAsync(ex);
        }
        await BackendConfigurationPnDbContext!.SaveChangesAsync();

        var newDate = baseMonday.AddDays(7);
        var moveModel = new CalendarTaskMoveRequestModel
        {
            Id = arpId,
            NewDate = newDate.ToString("yyyy-MM-dd") + "T00:00:00Z",
            NewStartHour = 9.0,
            Scope = "all"
        };

        // Act
        var result = await _calendarService.MoveTask(moveModel);

        // Assert
        Assert.That(result.Success, Is.True, result.Message);

        var remainingExceptions = await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions
            .Where(x => x.AreaRulePlanningId == arpId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync();

        Assert.That(remainingExceptions, Is.Empty);

        var arp = await BackendConfigurationPnDbContext.AreaRulePlannings
            .FirstAsync(x => x.Id == arpId);
        Assert.That(arp.StartDate!.Value.Date, Is.EqualTo(newDate.Date));
    }

    [Test]
    public async Task MoveTask_ScopeThisAndFollowing_ClearsFutureExceptions()
    {
        // Arrange
        var baseMonday = GetNextMonday();
        var startDate = DateTime.SpecifyKind(baseMonday, DateTimeKind.Utc);
        var arpId = await SeedWeeklyTask(startDate);

        // Create exceptions: one before originalDate (should survive), two after (should be removed)
        var pastEx = new CalendarOccurrenceException
        {
            AreaRulePlanningId = arpId,
            OriginalDate = baseMonday.AddDays(-6),
            IsDeleted = false,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions.AddAsync(pastEx);

        var futureEx1 = new CalendarOccurrenceException
        {
            AreaRulePlanningId = arpId,
            OriginalDate = baseMonday,
            IsDeleted = false,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.CalendarOccurrenceExceptions.AddAsync(futureEx1);

        var futureEx2 = new CalendarOccurrenceException
        {
            AreaRulePlanningId = arpId,
            OriginalDate = baseMonday.AddDays(7),
            IsDeleted = false,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.CalendarOccurrenceExceptions.AddAsync(futureEx2);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var moveModel = new CalendarTaskMoveRequestModel
        {
            Id = arpId,
            OriginalDate = baseMonday.ToString("yyyy-MM-dd") + "T00:00:00Z",
            NewDate = baseMonday.AddDays(2).ToString("yyyy-MM-dd") + "T00:00:00Z",
            NewStartHour = 10.0,
            Scope = "thisAndFollowing"
        };

        // Act
        var result = await _calendarService.MoveTask(moveModel);

        // Assert
        Assert.That(result.Success, Is.True, result.Message);

        var remaining = await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions
            .Where(x => x.AreaRulePlanningId == arpId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync();

        // Only the exception BEFORE originalDate should remain
        Assert.That(remaining, Has.Count.EqualTo(1));
        Assert.That(remaining[0].OriginalDate.Date, Is.EqualTo(baseMonday.AddDays(-6).Date));
    }

    [Test]
    public async Task MoveTask_ScopeThisAndFollowing_AnchorsPastOccurrencesWithOldValues()
    {
        // Series started 4 weeks ago; user drags an upcoming Monday occurrence
        // to a different day (Wed) at a new time. With 'thisAndFollowing' scope
        // the past 4 Mondays must keep their original (Mon, 09:00, 1h) state
        // — without the backfill they'd vanish (planning.StartDate shifts
        // forward, so the recurrence rule no longer generates them).
        var monday = GetNextMonday();
        var seriesStart = monday.AddDays(-28);
        var arpId = await SeedWeeklyTask(DateTime.SpecifyKind(seriesStart, DateTimeKind.Utc));

        var moveModel = new CalendarTaskMoveRequestModel
        {
            Id = arpId,
            OriginalDate = monday.ToString("yyyy-MM-dd") + "T00:00:00Z",
            NewDate = monday.AddDays(2).ToString("yyyy-MM-dd") + "T00:00:00Z", // Wed
            NewStartHour = 14.0,
            Scope = "thisAndFollowing"
        };

        var result = await _calendarService.MoveTask(moveModel);
        Assert.That(result.Success, Is.True, result.Message);

        var anchors = await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions
            .Where(x => x.AreaRulePlanningId == arpId)
            .Where(x => x.OriginalDate < monday.Date)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .OrderBy(x => x.OriginalDate)
            .ToListAsync();

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

        // Series itself should have shifted forward to the new date.
        var arp = await BackendConfigurationPnDbContext.AreaRulePlannings.SingleAsync(x => x.Id == arpId);
        var planning = await ItemsPlanningPnDbContext!.Plannings.SingleAsync(x => x.Id == arp.ItemPlanningId);
        Assert.That(arp.StartDate!.Value.Date, Is.EqualTo(monday.AddDays(2).Date));
        Assert.That(planning.StartDate.Date, Is.EqualTo(monday.AddDays(2).Date));
    }

    [Test]
    public async Task DeleteTask_ScopeThis_CreatesDeletedExceptionRow()
    {
        // Arrange
        var baseMonday = GetNextMonday();
        var startDate = DateTime.SpecifyKind(baseMonday, DateTimeKind.Utc);
        var arpId = await SeedWeeklyTask(startDate);

        var deleteModel = new CalendarTaskDeleteRequestModel
        {
            Id = arpId,
            OriginalDate = baseMonday.ToString("yyyy-MM-dd") + "T00:00:00Z",
            Scope = "this"
        };

        // Act
        var result = await _calendarService.DeleteTask(deleteModel);

        // Assert
        Assert.That(result.Success, Is.True, result.Message);

        var exception = await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions
            .Where(x => x.AreaRulePlanningId == arpId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .FirstOrDefaultAsync();

        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.IsDeleted, Is.True);
        Assert.That(exception.OriginalDate.Date, Is.EqualTo(baseMonday.Date));
    }

    [Test]
    public async Task DeleteTask_ScopeThisAndFollowing_SetsEndDateToDayBefore()
    {
        // Arrange
        var baseMonday = GetNextMonday();
        var startDate = DateTime.SpecifyKind(baseMonday, DateTimeKind.Utc);
        var arpId = await SeedWeeklyTask(startDate);

        // Seed an exception after the originalDate that should be deleted
        var futureEx = new CalendarOccurrenceException
        {
            AreaRulePlanningId = arpId,
            OriginalDate = baseMonday.AddDays(14),
            IsDeleted = false,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions.AddAsync(futureEx);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var originalDate = baseMonday.AddDays(7);
        var deleteModel = new CalendarTaskDeleteRequestModel
        {
            Id = arpId,
            OriginalDate = originalDate.ToString("yyyy-MM-dd") + "T00:00:00Z",
            Scope = "thisAndFollowing"
        };

        // Act
        var result = await _calendarService.DeleteTask(deleteModel);

        // Assert
        Assert.That(result.Success, Is.True, result.Message);

        var arp = await BackendConfigurationPnDbContext!.AreaRulePlannings
            .FirstAsync(x => x.Id == arpId);

        Assert.That(arp.EndDate!.Value.Date, Is.EqualTo(originalDate.AddDays(-1).Date));

        var planning = await ItemsPlanningPnDbContext!.Plannings
            .FirstAsync(x => x.Id == arp.ItemPlanningId);
        Assert.That(planning.RepeatUntil!.Value.Date, Is.EqualTo(originalDate.AddDays(-1).Date));

        // Stale exceptions (>= originalDate) should be removed
        var remainingExceptions = await BackendConfigurationPnDbContext.CalendarOccurrenceExceptions
            .Where(x => x.AreaRulePlanningId == arpId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync();

        Assert.That(remainingExceptions, Is.Empty);
    }

    [Test]
    public async Task DeleteTask_ScopeAll_CallsDeleteEntireSeries()
    {
        // Arrange
        var baseMonday = GetNextMonday();
        var startDate = DateTime.SpecifyKind(baseMonday, DateTimeKind.Utc);
        var arpId = await SeedWeeklyTask(startDate);

        // Seed an exception that should be removed
        var ex = new CalendarOccurrenceException
        {
            AreaRulePlanningId = arpId,
            OriginalDate = baseMonday,
            IsDeleted = false,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions.AddAsync(ex);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var deleteModel = new CalendarTaskDeleteRequestModel
        {
            Id = arpId,
            Scope = "all"
        };

        // Act
        var result = await _calendarService.DeleteTask(deleteModel);

        // Assert
        Assert.That(result.Success, Is.True, result.Message);

        // CalendarConfiguration should be removed (WorkflowState = Removed)
        var calConfig = await BackendConfigurationPnDbContext!.CalendarConfigurations
            .FirstOrDefaultAsync(x => x.AreaRulePlanningId == arpId && x.WorkflowState != Constants.WorkflowStates.Removed);
        Assert.That(calConfig, Is.Null);

        // All exceptions should be removed
        var remainingExceptions = await BackendConfigurationPnDbContext.CalendarOccurrenceExceptions
            .Where(x => x.AreaRulePlanningId == arpId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync();
        Assert.That(remainingExceptions, Is.Empty);

        // DeleteTask on the wizard service should have been called
        await _taskWizardService.Received(1).DeleteTask(arpId);
    }
}
