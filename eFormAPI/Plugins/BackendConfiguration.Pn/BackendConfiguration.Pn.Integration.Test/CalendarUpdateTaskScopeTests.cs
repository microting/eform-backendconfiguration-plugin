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
using Microting.ItemsPlanningBase.Infrastructure.Enums;
using NSubstitute;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// Scope-aware UpdateTask behavior for issue #885. Mirrors the fixture/seed
/// pattern of <see cref="CalendarOccurrenceExceptionTests"/>. The task wizard
/// is mocked, so the helpers' own DB writes are what these tests assert.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class CalendarUpdateTaskScopeTests : TestBaseSetup
{
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
        // scope=this widens an eForm change to the whole series through this
        // call (the eForm has no per-occurrence column).
        _taskWizardService.ApplyEformChangeToSeries(Arg.Any<int>(), Arg.Any<int>())
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

    private static DateTime GetNextMonday()
    {
        var today = DateTime.UtcNow.Date;
        var daysUntilMonday = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        if (daysUntilMonday == 0) daysUntilMonday = 7; // always future
        return today.AddDays(daysUntilMonday);
    }

    /// <summary>
    /// Seeds Area→Property→AreaRule(+translation)→Planning→AreaRulePlanning→
    /// CalendarConfiguration for a weekly series. Returns the ARP Id.
    /// </summary>
    private async Task<int> SeedWeeklyTask(DateTime startDate, string title = "Original Title",
        int arpRepeatType = 2, int eformId = 0)
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
            AreaId = area.Id, PropertyId = property.Id, EformId = eformId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRules.AddAsync(areaRule);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var areaRuleTranslation = new AreaRuleTranslation
        {
            AreaRuleId = areaRule.Id, LanguageId = 1, Name = title,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRuleTranslations.AddAsync(areaRuleTranslation);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var planning = new Planning
        {
            Enabled = true, RepeatEvery = 1, RepeatType = RepeatType.Week, StartDate = startDate,
            RelatedEFormId = eformId, Description = "Original description",
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = property.Id, AreaId = area.Id,
            ItemPlanningId = planning.Id, StartDate = startDate, Status = true,
            RepeatType = arpRepeatType, RepeatEvery = 1,
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

    private static CalendarTaskUpdateRequestModel BuildEdit(int arpId, DateTime startDate, string scope,
        DateTime originalDate, string title = "Edited Title", double startHour = 11.0, double duration = 2.0,
        int propertyId = 0, int eformId = 0)
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
            RepeatType = 2,
            RepeatEvery = 1,
            ComplianceEnabled = false,
            PropertyId = propertyId,
            EformId = eformId,
            Sites = [101],
            TagIds = [],
            BoardId = 42,
            Color = "#abcdef",
            DescriptionHtml = "Edited description",
            Translates = [new CommonTranslationsModel { LanguageId = 1, Name = title }]
        };
    }

    [Test]
    public async Task UpdateTask_ScopeThis_DoesNotCallWizard_NorMoveSeriesStart()
    {
        var baseMonday = GetNextMonday();
        var startDate = DateTime.SpecifyKind(baseMonday, DateTimeKind.Utc);
        var arpId = await SeedWeeklyTask(startDate);

        var model = BuildEdit(arpId, baseMonday, "this", baseMonday);
        var result = await _calendarService.UpdateTask(model);

        Assert.That(result.Success, Is.True, result.Message);
        // The bug: relocating the series. A "this" edit must not touch the series anchor.
        await _taskWizardService.DidNotReceive().UpdateTask(Arg.Any<TaskWizardCreateModel>());
        var arp = await BackendConfigurationPnDbContext!.AreaRulePlannings.FirstAsync(x => x.Id == arpId);
        Assert.That(arp.StartDate!.Value.Date, Is.EqualTo(baseMonday.Date));
    }

    [Test]
    public async Task UpdateTask_ScopeThis_CreatesExceptionWithFieldOverrides()
    {
        var baseMonday = GetNextMonday();
        var startDate = DateTime.SpecifyKind(baseMonday, DateTimeKind.Utc);
        var arpId = await SeedWeeklyTask(startDate);

        var model = BuildEdit(arpId, baseMonday, "this", baseMonday,
            title: "Edited Title", startHour: 11.0, duration: 2.0);
        var result = await _calendarService.UpdateTask(model);

        Assert.That(result.Success, Is.True, result.Message);
        var exception = await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions
            .Include(x => x.ExceptionSites)
            .Where(x => x.AreaRulePlanningId == arpId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .SingleAsync();

        Assert.Multiple(() =>
        {
            Assert.That(exception.OriginalDate.Date, Is.EqualTo(baseMonday.Date));
            Assert.That(exception.Title, Is.EqualTo("Edited Title"));
            Assert.That(exception.DescriptionHtml, Is.EqualTo("Edited description"));
            Assert.That(exception.BoardId, Is.EqualTo(42));
            Assert.That(exception.Color, Is.EqualTo("#abcdef"));
            Assert.That(exception.StartHour, Is.EqualTo(11.0));
            Assert.That(exception.Duration, Is.EqualTo(2.0));
            Assert.That(exception.IsDeleted, Is.False);
            // NewDate stays null when the occurrence date is unchanged.
            Assert.That(exception.NewDate, Is.Null);
            // Per-occurrence assignees recorded without touching the series.
            Assert.That(exception.ExceptionSites
                .Where(s => s.WorkflowState != Constants.WorkflowStates.Removed)
                .Select(s => s.SiteId), Is.EquivalentTo(new[] { 101 }));
        });
    }

    [Test]
    public async Task UpdateTask_ScopeThis_ReusesExistingExceptionForDate()
    {
        var baseMonday = GetNextMonday();
        var startDate = DateTime.SpecifyKind(baseMonday, DateTimeKind.Utc);
        var arpId = await SeedWeeklyTask(startDate);

        // Pre-existing move-style exception for the same occurrence.
        var existing = new CalendarOccurrenceException
        {
            AreaRulePlanningId = arpId, OriginalDate = baseMonday, IsDeleted = false,
            StartHour = 8.0, WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions.AddAsync(existing);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var model = BuildEdit(arpId, baseMonday, "this", baseMonday, startHour: 13.0);
        var result = await _calendarService.UpdateTask(model);

        Assert.That(result.Success, Is.True, result.Message);
        var exceptions = await BackendConfigurationPnDbContext.CalendarOccurrenceExceptions
            .Where(x => x.AreaRulePlanningId == arpId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync();
        Assert.That(exceptions, Has.Count.EqualTo(1), "should update the existing row, not create a duplicate");
        Assert.That(exceptions[0].StartHour, Is.EqualTo(13.0));
        Assert.That(exceptions[0].Title, Is.EqualTo("Edited Title"));
    }

    [Test]
    public async Task UpdateTask_ScopeThis_OnNonRecurringTask_UpdatesSeries_NoException()
    {
        var baseMonday = GetNextMonday();
        var startDate = DateTime.SpecifyKind(baseMonday, DateTimeKind.Utc);
        // One-off event (RepeatType=0). The frontend sends scope="this" for
        // non-recurring edits — the backend must treat it as a full update.
        var arpId = await SeedWeeklyTask(startDate, arpRepeatType: 0);

        var model = BuildEdit(arpId, baseMonday, "this", baseMonday, startHour: 15.0);
        model.RepeatType = 0;
        var result = await _calendarService.UpdateTask(model);

        Assert.That(result.Success, Is.True, result.Message);
        // Must NOT create an occurrence exception for a one-off...
        var exceptions = await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions
            .Where(x => x.AreaRulePlanningId == arpId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync();
        Assert.That(exceptions, Is.Empty);
        // ...and must update the event itself (wizard + CalendarConfiguration).
        await _taskWizardService.Received().UpdateTask(Arg.Any<TaskWizardCreateModel>());
        var calConfig = await BackendConfigurationPnDbContext.CalendarConfigurations
            .FirstAsync(x => x.AreaRulePlanningId == arpId);
        Assert.That(calConfig.StartHour, Is.EqualTo(15.0));
    }

    [Test]
    public async Task UpdateTask_ScopeThis_OnPreviouslyDeletedOccurrence_UnDeletesIt()
    {
        var baseMonday = GetNextMonday();
        var startDate = DateTime.SpecifyKind(baseMonday, DateTimeKind.Utc);
        var arpId = await SeedWeeklyTask(startDate);

        // A prior "delete this occurrence" left an IsDeleted exception.
        var deleted = new CalendarOccurrenceException
        {
            AreaRulePlanningId = arpId, OriginalDate = baseMonday, IsDeleted = true,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions.AddAsync(deleted);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var model = BuildEdit(arpId, baseMonday, "this", baseMonday);
        var result = await _calendarService.UpdateTask(model);

        Assert.That(result.Success, Is.True, result.Message);
        var exception = await BackendConfigurationPnDbContext.CalendarOccurrenceExceptions
            .Where(x => x.AreaRulePlanningId == arpId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .SingleAsync();
        // Editing the occurrence must bring it back so the edit is visible.
        Assert.That(exception.IsDeleted, Is.False);
        Assert.That(exception.Title, Is.EqualTo("Edited Title"));
    }

    [Test]
    public async Task UpdateTask_ScopeAll_DoesNotCreateException_AndUpdatesCalConfig()
    {
        var baseMonday = GetNextMonday();
        var startDate = DateTime.SpecifyKind(baseMonday, DateTimeKind.Utc);
        var arpId = await SeedWeeklyTask(startDate);

        var model = BuildEdit(arpId, baseMonday, "all", baseMonday, startHour: 14.0, duration: 1.5);
        var result = await _calendarService.UpdateTask(model);

        Assert.That(result.Success, Is.True, result.Message);
        // "all" goes through the wizard for field updates...
        await _taskWizardService.Received().UpdateTask(Arg.Any<TaskWizardCreateModel>());
        // ...and writes no per-occurrence exception.
        var exceptions = await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions
            .Where(x => x.AreaRulePlanningId == arpId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync();
        Assert.That(exceptions, Is.Empty);
        var calConfig = await BackendConfigurationPnDbContext.CalendarConfigurations
            .FirstAsync(x => x.AreaRulePlanningId == arpId);
        Assert.That(calConfig.StartHour, Is.EqualTo(14.0));
        Assert.That(calConfig.Duration, Is.EqualTo(1.5));
        Assert.That(calConfig.BoardId, Is.EqualTo(42));
    }

    [Test]
    public async Task UpdateTask_ScopeAll_TimeOnlyEdit_PreservesSeriesAnchor()
    {
        var nextMonday = GetNextMonday();
        var seriesStart = DateTime.SpecifyKind(nextMonday, DateTimeKind.Utc);
        var arpId = await SeedWeeklyTask(seriesStart);

        // Edit a LATER occurrence (week +2) with scope=all, changing only the
        // start hour — the occurrence date is unchanged (StartDate==originalDate).
        var laterOccurrence = nextMonday.AddDays(14);
        var model = BuildEdit(arpId, laterOccurrence, "all", laterOccurrence, startHour: 8.0);
        var result = await _calendarService.UpdateTask(model);

        Assert.That(result.Success, Is.True, result.Message);
        // The wizard must receive the ORIGINAL series anchor, not the clicked
        // occurrence's date — otherwise earlier occurrences would be dropped.
        await _taskWizardService.Received().UpdateTask(
            Arg.Is<TaskWizardCreateModel>(m => m.StartDate == seriesStart));
        var calConfig = await BackendConfigurationPnDbContext!.CalendarConfigurations
            .FirstAsync(x => x.AreaRulePlanningId == arpId);
        Assert.That(calConfig.StartHour, Is.EqualTo(8.0));
    }

    [Test]
    public async Task UpdateTask_ScopeThisAndFollowing_AnchorsPastOccurrences_KeepsSeriesStart()
    {
        var nextMonday = GetNextMonday();
        // Series started four weeks before the edited (future) occurrence.
        var seriesStart = DateTime.SpecifyKind(nextMonday.AddDays(-28), DateTimeKind.Utc);
        var arpId = await SeedWeeklyTask(seriesStart);

        // Edit "this and following" at the upcoming (future) Monday.
        var model = BuildEdit(arpId, nextMonday, "thisAndFollowing", nextMonday, startHour: 11.0, duration: 2.0);
        var result = await _calendarService.UpdateTask(model);

        Assert.That(result.Success, Is.True, result.Message);

        // Four past occurrences (seriesStart, +7, +14, +21) anchored with OLD values.
        var anchors = await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions
            .Where(x => x.AreaRulePlanningId == arpId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .OrderBy(x => x.OriginalDate)
            .ToListAsync();
        Assert.That(anchors, Has.Count.EqualTo(4), "one anchor per past occurrence before originalDate");
        Assert.Multiple(() =>
        {
            foreach (var a in anchors)
            {
                Assert.That(a.OriginalDate.Date, Is.LessThan(nextMonday.Date));
                Assert.That(a.StartHour, Is.EqualTo(9.0), "past anchors keep the OLD start hour");
                Assert.That(a.Duration, Is.EqualTo(1.0), "past anchors keep the OLD duration");
                Assert.That(a.IsDeleted, Is.False);
            }
        });

        // Series anchor must NOT have been relocated onto the edited occurrence.
        var arp = await BackendConfigurationPnDbContext.AreaRulePlannings.FirstAsync(x => x.Id == arpId);
        Assert.That(arp.StartDate!.Value.Date, Is.EqualTo(seriesStart.Date));

        // Series (and therefore future occurrences) reflect the NEW values.
        var calConfig = await BackendConfigurationPnDbContext.CalendarConfigurations
            .FirstAsync(x => x.AreaRulePlanningId == arpId);
        Assert.That(calConfig.StartHour, Is.EqualTo(11.0));
        Assert.That(calConfig.Duration, Is.EqualTo(2.0));

        // The wizard is used for the series field update, anchored at the
        // ORIGINAL start date (not the edited occurrence's date) — the #885 fix.
        await _taskWizardService.Received().UpdateTask(
            Arg.Is<TaskWizardCreateModel>(m => m.StartDate == arp.StartDate));
    }

    // ------------------------------------------------------------------
    // eForm propagation (spec 2026-08-19-calendar-eform-change-propagation).
    // The eForm is a SERIES-level property — there is no per-occurrence column
    // for it and the completion path resolves the eForm from the occurrence's
    // own SDK case, so a scope="this" edit that changes it must be widened to
    // the whole series instead of being silently discarded.
    // ------------------------------------------------------------------

    [Test]
    public async Task UpdateTask_ScopeThis_WithEformChange_AppliesEformToSeries_AndStillWritesTheException()
    {
        var baseMonday = GetNextMonday();
        var startDate = DateTime.SpecifyKind(baseMonday, DateTimeKind.Utc);
        const int originalEformId = 4711;
        const int newEformId = 4712;
        var arpId = await SeedWeeklyTask(startDate, eformId: originalEformId);

        var model = BuildEdit(arpId, baseMonday, "this", baseMonday,
            startHour: 11.0, duration: 2.0, eformId: newEformId);
        var result = await _calendarService.UpdateTask(model);

        Assert.That(result.Success, Is.True, result.Message);

        // 1. The eForm change is applied at SERIES level...
        await _taskWizardService.Received(1).ApplyEformChangeToSeries(arpId, newEformId);

        // 2. ...and the per-occurrence exception is STILL written, so the
        //    occurrence's own field overrides are not lost to the widening.
        var exception = await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions
            .Include(x => x.ExceptionSites)
            .Where(x => x.AreaRulePlanningId == arpId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .SingleAsync();
        Assert.Multiple(() =>
        {
            Assert.That(exception.OriginalDate.Date, Is.EqualTo(baseMonday.Date));
            Assert.That(exception.Title, Is.EqualTo("Edited Title"));
            Assert.That(exception.StartHour, Is.EqualTo(11.0));
            Assert.That(exception.Duration, Is.EqualTo(2.0));
            Assert.That(exception.IsDeleted, Is.False);
        });

        // 3. Widening the eForm must not turn a "this" edit into a series
        //    field update — the wizard's full UpdateTask stays untouched.
        await _taskWizardService.DidNotReceive().UpdateTask(Arg.Any<TaskWizardCreateModel>());
        var arp = await BackendConfigurationPnDbContext.AreaRulePlannings.FirstAsync(x => x.Id == arpId);
        Assert.That(arp.StartDate!.Value.Date, Is.EqualTo(baseMonday.Date));
    }

    [Test]
    public async Task UpdateTask_ScopeThis_WithoutEformChange_DoesNotTouchTheSeriesEform()
    {
        var baseMonday = GetNextMonday();
        var startDate = DateTime.SpecifyKind(baseMonday, DateTimeKind.Utc);
        const int eformId = 4711;
        var arpId = await SeedWeeklyTask(startDate, eformId: eformId);

        // Same eForm as the series already carries — the edit only moves the
        // start hour. Zero retract, zero redeploy: the widening path (and with
        // it EventDeployService.RepairEformForOpenOccurrencesAsync) must not
        // run at all.
        var model = BuildEdit(arpId, baseMonday, "this", baseMonday, startHour: 13.0, eformId: eformId);
        var result = await _calendarService.UpdateTask(model);

        Assert.That(result.Success, Is.True, result.Message);
        await _taskWizardService.DidNotReceive().ApplyEformChangeToSeries(Arg.Any<int>(), Arg.Any<int>());

        var exception = await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions
            .Where(x => x.AreaRulePlanningId == arpId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .SingleAsync();
        Assert.That(exception.StartHour, Is.EqualTo(13.0));
    }
}
