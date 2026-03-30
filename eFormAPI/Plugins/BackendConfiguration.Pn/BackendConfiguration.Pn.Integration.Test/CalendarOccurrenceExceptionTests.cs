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
    public new async Task Setup()
    {
        await base.Setup();

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
        var startDate = new DateTime(2027, 6, 7, 0, 0, 0, DateTimeKind.Utc);
        var arpId = await SeedWeeklyTask(startDate);

        var originalDate = "2027-06-07T00:00:00Z";
        var newDate = "2027-06-09T00:00:00Z";

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
        Assert.That(exception!.OriginalDate.Date, Is.EqualTo(new DateTime(2027, 6, 7)));
        Assert.That(exception.NewDate!.Value.Date, Is.EqualTo(new DateTime(2027, 6, 9)));
        Assert.That(exception.StartHour, Is.EqualTo(10.0));
        Assert.That(exception.IsDeleted, Is.False);
    }

    [Test]
    public async Task MoveTask_ScopeThis_UpdatesExistingException()
    {
        // Arrange
        var startDate = new DateTime(2027, 6, 7, 0, 0, 0, DateTimeKind.Utc);
        var arpId = await SeedWeeklyTask(startDate);

        // Pre-create an exception for that occurrence
        var preExisting = new CalendarOccurrenceException
        {
            AreaRulePlanningId = arpId,
            OriginalDate = new DateTime(2027, 6, 7),
            NewDate = new DateTime(2027, 6, 8),
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
            OriginalDate = "2027-06-07T00:00:00Z",
            NewDate = "2027-06-10T00:00:00Z",
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
        Assert.That(exceptions[0].NewDate!.Value.Date, Is.EqualTo(new DateTime(2027, 6, 10)));
        Assert.That(exceptions[0].StartHour, Is.EqualTo(14.0));
    }

    [Test]
    public async Task MoveTask_ScopeAll_ClearsAllExceptions()
    {
        // Arrange
        var startDate = new DateTime(2027, 6, 7, 0, 0, 0, DateTimeKind.Utc);
        var arpId = await SeedWeeklyTask(startDate);

        // Create two exceptions
        for (var i = 0; i < 2; i++)
        {
            var ex = new CalendarOccurrenceException
            {
                AreaRulePlanningId = arpId,
                OriginalDate = new DateTime(2027, 6, 7 + i),
                IsDeleted = false,
                WorkflowState = Constants.WorkflowStates.Created,
                CreatedByUserId = 1,
                UpdatedByUserId = 1
            };
            await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions.AddAsync(ex);
        }
        await BackendConfigurationPnDbContext!.SaveChangesAsync();

        var moveModel = new CalendarTaskMoveRequestModel
        {
            Id = arpId,
            NewDate = "2027-06-14T00:00:00Z",
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
        Assert.That(arp.StartDate!.Value.Date, Is.EqualTo(new DateTime(2027, 6, 14)));
    }

    [Test]
    public async Task MoveTask_ScopeThisAndFollowing_ClearsFutureExceptions()
    {
        // Arrange
        var startDate = new DateTime(2027, 6, 7, 0, 0, 0, DateTimeKind.Utc);
        var arpId = await SeedWeeklyTask(startDate);

        // Create exceptions: one before originalDate (should survive), two after (should be removed)
        var pastEx = new CalendarOccurrenceException
        {
            AreaRulePlanningId = arpId,
            OriginalDate = new DateTime(2027, 6, 1),
            IsDeleted = false,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions.AddAsync(pastEx);

        var futureEx1 = new CalendarOccurrenceException
        {
            AreaRulePlanningId = arpId,
            OriginalDate = new DateTime(2027, 6, 7),
            IsDeleted = false,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.CalendarOccurrenceExceptions.AddAsync(futureEx1);

        var futureEx2 = new CalendarOccurrenceException
        {
            AreaRulePlanningId = arpId,
            OriginalDate = new DateTime(2027, 6, 14),
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
            OriginalDate = "2027-06-07T00:00:00Z",
            NewDate = "2027-06-09T00:00:00Z",
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
        Assert.That(remaining[0].OriginalDate.Date, Is.EqualTo(new DateTime(2027, 6, 1)));
    }

    [Test]
    public async Task DeleteTask_ScopeThis_CreatesDeletedExceptionRow()
    {
        // Arrange
        var startDate = new DateTime(2027, 6, 7, 0, 0, 0, DateTimeKind.Utc);
        var arpId = await SeedWeeklyTask(startDate);

        var deleteModel = new CalendarTaskDeleteRequestModel
        {
            Id = arpId,
            OriginalDate = "2027-06-07T00:00:00Z",
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
        Assert.That(exception.OriginalDate.Date, Is.EqualTo(new DateTime(2027, 6, 7)));
    }

    [Test]
    public async Task DeleteTask_ScopeThisAndFollowing_SetsEndDateToDayBefore()
    {
        // Arrange
        var startDate = new DateTime(2027, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var arpId = await SeedWeeklyTask(startDate);

        // Seed an exception after the originalDate that should be deleted
        var futureEx = new CalendarOccurrenceException
        {
            AreaRulePlanningId = arpId,
            OriginalDate = new DateTime(2027, 6, 14),
            IsDeleted = false,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions.AddAsync(futureEx);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var deleteModel = new CalendarTaskDeleteRequestModel
        {
            Id = arpId,
            OriginalDate = "2027-06-07T00:00:00Z",
            Scope = "thisAndFollowing"
        };

        // Act
        var result = await _calendarService.DeleteTask(deleteModel);

        // Assert
        Assert.That(result.Success, Is.True, result.Message);

        var arp = await BackendConfigurationPnDbContext!.AreaRulePlannings
            .FirstAsync(x => x.Id == arpId);

        Assert.That(arp.EndDate!.Value.Date, Is.EqualTo(new DateTime(2027, 6, 6)));

        var planning = await ItemsPlanningPnDbContext!.Plannings
            .FirstAsync(x => x.Id == arp.ItemPlanningId);
        Assert.That(planning.RepeatUntil!.Value.Date, Is.EqualTo(new DateTime(2027, 6, 6)));

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
        var startDate = new DateTime(2027, 6, 7, 0, 0, 0, DateTimeKind.Utc);
        var arpId = await SeedWeeklyTask(startDate);

        // Seed an exception that should be removed
        var ex = new CalendarOccurrenceException
        {
            AreaRulePlanningId = arpId,
            OriginalDate = new DateTime(2027, 6, 7),
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
