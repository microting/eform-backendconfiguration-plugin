using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService;
using BackendConfiguration.Pn.Services.EventDeployService;
using BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;
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

/// <summary>
/// #952: an annually-repeating task, when MOVED (dragged) to a new date with
/// scope "all" or "thisAndFollowing", must carry its day-of-month to the drop
/// date. The yearly render reads <c>Planning.DayOfMonth</c> (and the edit
/// round-trip reads <c>AreaRulePlanning.DayOfMonth</c>); if a move only shifts
/// StartDate, the stale DayOfMonth re-pins the occurrence to the original day,
/// so it "snaps back".
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class CalendarYearlyMoveTests : TestBaseSetup
{
    // Yearly RepeatType value. The plugin enum defines Year = 4; the
    // items-planning enum only defines Day/Week/Month, so a yearly Planning
    // stores the same value via a cast.
    private const int YearRepeatType = 4;

    private IUserService _userService;
    private IBackendConfigurationTaskWizardService _taskWizardService;
    private BackendConfigurationCalendarService _calendarService;

    [SetUp]
    public async Task SetupCalendarService()
    {
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
            Substitute.For<IEventDeployService>(),
            ItemsPlanningPnDbContext!,
            _taskWizardService,
            Substitute.For<ICalendarAssignmentReconciliationService>(),
            NullLogger<BackendConfigurationCalendarService>.Instance
        );
    }

    /// <summary>
    /// Seeds a yearly AreaRulePlanning + Planning + CalendarConfiguration
    /// anchored on <paramref name="startDate"/> with DayOfMonth = startDate.Day.
    /// Returns the AreaRulePlanning Id.
    /// </summary>
    private async Task<int> SeedYearlyTask(DateTime startDate)
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
            RepeatType = (RepeatType)YearRepeatType,
            DayOfMonth = startDate.Day,
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
            RepeatType = YearRepeatType,
            RepeatEvery = 1,
            DayOfMonth = startDate.Day,
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

    [Test]
    public async Task MoveTask_ScopeAll_Yearly_CarriesDayOfMonthToDropDate()
    {
        // Anchored on the 4th of a far-future March; drag the whole series to the 9th.
        var anchor = new DateTime(2030, 3, 4, 0, 0, 0, DateTimeKind.Utc);
        var arpId = await SeedYearlyTask(anchor);
        var newDate = new DateTime(2030, 3, 9, 0, 0, 0, DateTimeKind.Utc);

        var result = await _calendarService.MoveTask(new CalendarTaskMoveRequestModel
        {
            Id = arpId,
            NewDate = newDate.ToString("yyyy-MM-dd") + "T00:00:00Z",
            NewStartHour = 9.0,
            Scope = "all"
        });

        Assert.That(result.Success, Is.True, result.Message);

        // Re-fetch with AsNoTracking so the assertions read the PERSISTED row,
        // not the in-memory entity MoveTask mutated.
        var arp = await BackendConfigurationPnDbContext!.AreaRulePlannings.AsNoTracking().SingleAsync(x => x.Id == arpId);
        var planning = await ItemsPlanningPnDbContext!.Plannings.AsNoTracking().SingleAsync(x => x.Id == arp.ItemPlanningId);

        Assert.Multiple(() =>
        {
            Assert.That(arp.StartDate!.Value.Date, Is.EqualTo(newDate.Date), "arp.StartDate shifted to the drop date");
            Assert.That(planning.StartDate.Date, Is.EqualTo(newDate.Date), "planning.StartDate shifted to the drop date");
            Assert.That(planning.DayOfMonth, Is.EqualTo(9), "planning.DayOfMonth (the yearly render anchor) follows the drop day");
            Assert.That(arp.DayOfMonth, Is.EqualTo(9), "arp.DayOfMonth (the edit round-trip anchor) follows the drop day");
        });
    }

    [Test]
    public async Task MoveTask_ScopeThisAndFollowing_Yearly_CarriesDayOfMonthToDropDate()
    {
        // Series anchored several years in the PAST so the move actually shifts
        // StartDate forward over real recurrence history (the snap-back the bug
        // report hits is on a long-running series, not a freshly-created one).
        var anchor = new DateTime(2024, 3, 4, 0, 0, 0, DateTimeKind.Utc);
        var arpId = await SeedYearlyTask(anchor);
        // originalDate is a future yearly occurrence of that series.
        var originalDate = new DateTime(2027, 3, 4, 0, 0, 0, DateTimeKind.Utc);
        var newDate = new DateTime(2027, 3, 9, 0, 0, 0, DateTimeKind.Utc);

        var result = await _calendarService.MoveTask(new CalendarTaskMoveRequestModel
        {
            Id = arpId,
            OriginalDate = originalDate.ToString("yyyy-MM-dd") + "T00:00:00Z",
            NewDate = newDate.ToString("yyyy-MM-dd") + "T00:00:00Z",
            NewStartHour = 9.0,
            Scope = "thisAndFollowing"
        });

        Assert.That(result.Success, Is.True, result.Message);

        var arp = await BackendConfigurationPnDbContext!.AreaRulePlannings.AsNoTracking().SingleAsync(x => x.Id == arpId);
        var planning = await ItemsPlanningPnDbContext!.Plannings.AsNoTracking().SingleAsync(x => x.Id == arp.ItemPlanningId);

        Assert.Multiple(() =>
        {
            Assert.That(arp.StartDate!.Value.Date, Is.EqualTo(newDate.Date));
            Assert.That(planning.StartDate.Date, Is.EqualTo(newDate.Date));
            Assert.That(planning.DayOfMonth, Is.EqualTo(9), "planning.DayOfMonth follows the drop day");
            Assert.That(arp.DayOfMonth, Is.EqualTo(9), "arp.DayOfMonth follows the drop day");
        });
    }

    [Test]
    public async Task MoveTask_ScopeAll_Yearly_CrossMonth_CarriesMonthAndDay()
    {
        // The month follows StartDate; the day follows DayOfMonth. A cross-month
        // move (Mar 4 -> Jul 9) must land the yearly anchor on Jul 9, not Jul 4.
        var anchor = new DateTime(2030, 3, 4, 0, 0, 0, DateTimeKind.Utc);
        var arpId = await SeedYearlyTask(anchor);
        var newDate = new DateTime(2030, 7, 9, 0, 0, 0, DateTimeKind.Utc);

        var result = await _calendarService.MoveTask(new CalendarTaskMoveRequestModel
        {
            Id = arpId,
            NewDate = newDate.ToString("yyyy-MM-dd") + "T00:00:00Z",
            NewStartHour = 9.0,
            Scope = "all"
        });

        Assert.That(result.Success, Is.True, result.Message);

        var arp = await BackendConfigurationPnDbContext!.AreaRulePlannings.AsNoTracking().SingleAsync(x => x.Id == arpId);
        var planning = await ItemsPlanningPnDbContext!.Plannings.AsNoTracking().SingleAsync(x => x.Id == arp.ItemPlanningId);

        Assert.Multiple(() =>
        {
            Assert.That(planning.StartDate.Month, Is.EqualTo(7), "month follows the drop date");
            Assert.That(planning.StartDate.Day, Is.EqualTo(9));
            Assert.That(planning.DayOfMonth, Is.EqualTo(9));
            Assert.That(arp.DayOfMonth, Is.EqualTo(9));
        });
    }
}
