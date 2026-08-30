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

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
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
using NSubstitute;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// Focused coverage for <c>BackendConfigurationCalendarService.Index</c>, the new
/// calendar task-list endpoint. <c>Index</c> queries <c>AreaRulePlannings</c> (one
/// row per recurrence series) and applies the request's filtration model. This
/// fixture exercises the <c>Filters.Status</c> filter: with two seeded series for
/// one property (one active, one inactive), an <c>Index</c> call filtered on
/// <c>Status = true</c> must return only the active series.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class CalendarTaskListIndexTest : TestBaseSetup
{
    private IUserService _userService = null!;
    private IBackendConfigurationTaskWizardService _taskWizardService = null!;
    private IEventDeployService _eventDeployService = null!;
    private BackendConfigurationCalendarService _calendarService = null!;

    [SetUp]
    public async Task SetupCalendarService()
    {
        // FK-safe clean of the rows this fixture writes, mirroring the
        // pattern in BackendConfigurationCalendarServiceTaskTrackerListTest.
        BackendConfigurationPnDbContext!.AreaRulePlannings.RemoveRange(
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

        var sdkConnectionString = MicrotingDbContext!.Database.GetConnectionString()!;

        // GetCurrentUserLanguage() drives translation resolution in Index; a
        // Language with Id = 1 is enough since the seeded series carry no
        // AreaRuleTranslations.
        _userService = Substitute.For<IUserService>();
        _userService.UserId.Returns(1);
        var mockLanguage = new Language { Id = 1, Name = "English", LanguageCode = "en-US" };
        _userService.GetCurrentUserLanguage().Returns(Task.FromResult(mockLanguage));

        _taskWizardService = Substitute.For<IBackendConfigurationTaskWizardService>();
        _eventDeployService = Substitute.For<IEventDeployService>();

        // The real EFormCoreService provides GetCore(); Index uses it only to
        // resolve worker names from the SDK Sites set. The seeded series carry
        // no PlanningSites, so an empty Sites set is sufficient.
        _calendarService = new BackendConfigurationCalendarService(
            new BackendConfigurationLocalizationService(),
            _userService,
            BackendConfigurationPnDbContext!,
            new EFormCoreService(sdkConnectionString),
            _eventDeployService,
            ItemsPlanningPnDbContext!,
            _taskWizardService,
            Substitute.For<ICalendarAssignmentReconciliationService>(),
            NullLogger<BackendConfigurationCalendarService>.Instance,
            Substitute.For<ICalendarOccurrenceRetractionService>(),
            Substitute.For<ICalendarPastSeriesBackfillService>()
        );
    }

    /// <summary>
    /// Seeds Property → Area → AreaRule → AreaRulePlanning using the real
    /// entities' <c>.Create(BackendConfigurationPnDbContext!)</c>, returning the
    /// new AreaRulePlanning's Id. The <paramref name="status"/> flag becomes the
    /// series' <c>Status</c>, which is the column <c>Index</c>'s
    /// <c>Filters.Status</c> filter targets.
    /// </summary>
    private async Task<int> SeedSeries(int propertyId, int areaId, bool status,
        int repeatType = 2, int repeatEvery = 1)
    {
        var areaRule = new AreaRule
        {
            AreaId = areaId,
            PropertyId = propertyId,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await areaRule.Create(BackendConfigurationPnDbContext!);

        var areaRulePlanning = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id,
            PropertyId = propertyId,
            AreaId = areaId,
            StartDate = DateTime.UtcNow.Date,
            Status = status,
            RepeatType = repeatType,
            RepeatEvery = repeatEvery,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await areaRulePlanning.Create(BackendConfigurationPnDbContext!);

        return areaRulePlanning.Id;
    }

    /// <summary>
    /// Two series for one property: one active, one inactive. An Index call
    /// filtered on Status = true must return the active series and exclude the
    /// inactive one; every returned row must carry Status == true.
    /// </summary>
    [Test]
    public async Task Index_FiltersByStatus_ReturnsOnlyActiveSeries()
    {
        var (property, area) = await SeedPropertyAndArea();

        var activeArpId = await SeedSeries(property.Id, area.Id, status: true);
        var inactiveArpId = await SeedSeries(property.Id, area.Id, status: false);

        var requestModel = new CalendarTaskIndexRequestModel
        {
            Filters = new CalendarTaskListFiltrationModel
            {
                PropertyIds = [property.Id],
                Status = true
            }
        };

        var result = await _calendarService.Index(requestModel);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Model.Select(x => x.Id), Does.Contain(activeArpId));
        Assert.That(result.Model.Select(x => x.Id), Does.Not.Contain(inactiveArpId));
        Assert.That(result.Model.All(x => x.Status), Is.True,
            "Index filtered on Status = true must return only active series.");
    }

    /// <summary>
    /// A series with no CalendarConfiguration must report the same default time as
    /// the week grid (BackendConfigurationCalendarService.GetTasksForWeek, `?? 9.0`).
    /// Index used `?? 0`, so the task list showed 00:00-01:00 and -- because the edit
    /// modal saves back whatever it was handed -- UpdateTask then persisted midnight.
    /// </summary>
    [Test]
    public async Task Index_SeriesWithoutConfiguration_DefaultsToNineToTenLikeWeekGrid()
    {
        var (property, area) = await SeedPropertyAndArea();
        var arpId = await SeedSeries(property.Id, area.Id, status: true);

        var result = await _calendarService.Index(new CalendarTaskIndexRequestModel
        {
            Filters = new CalendarTaskListFiltrationModel { PropertyIds = [property.Id] }
        });

        Assert.That(result.Success, Is.True, result.Message);
        var row = result.Model.Single(x => x.Id == arpId);
        Assert.That(row.StartHour, Is.EqualTo(9.0));
        Assert.That(row.Duration, Is.EqualTo(1.0));
        Assert.That(row.IsAllDay, Is.False);
    }

    /// <summary>
    /// The one case the week grid does render at hour 0: an "always" series (RepeatType
    /// 1, RepeatEvery 0) with no configuration is all-day, not a midnight timeslot.
    /// Index never set IsAllDay, so the frontend placed it in the 00:00 row instead of
    /// the all-day strip.
    /// </summary>
    [Test]
    public async Task Index_AlwaysSeriesWithoutConfiguration_IsAllDay()
    {
        var (property, area) = await SeedPropertyAndArea();
        var arpId = await SeedSeries(property.Id, area.Id, status: true,
            repeatType: 1, repeatEvery: 0);

        var result = await _calendarService.Index(new CalendarTaskIndexRequestModel
        {
            Filters = new CalendarTaskListFiltrationModel { PropertyIds = [property.Id] }
        });

        Assert.That(result.Success, Is.True, result.Message);
        var row = result.Model.Single(x => x.Id == arpId);
        Assert.That(row.IsAllDay, Is.True);
        Assert.That(row.StartHour, Is.EqualTo(0.0));
        Assert.That(row.Duration, Is.EqualTo(0.0));
    }

    private async Task<(Property Property, Area Area)> SeedPropertyAndArea()
    {
        var area = new Area
        {
            Type = AreaTypesEnum.Type1,
            ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await area.Create(BackendConfigurationPnDbContext!);

        var property = new Property
        {
            Name = $"CalendarIndexProp-{Guid.NewGuid()}",
            ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await property.Create(BackendConfigurationPnDbContext!);
        return (property, area);
    }
}
