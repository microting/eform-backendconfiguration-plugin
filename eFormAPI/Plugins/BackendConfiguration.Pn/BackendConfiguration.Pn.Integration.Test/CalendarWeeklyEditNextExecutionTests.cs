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

using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Infrastructure.Models.TaskWizard;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService;
using BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;
using BackendConfiguration.Pn.Services.CalendarChangeNotification;
using BackendConfiguration.Pn.Services.EventDeployService;
using BackendConfiguration.Pn.Services.WorkerTagMembership;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.eForm.Infrastructure.Models;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Enums;
using NSubstitute;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// #1375 — a calendar edit that gives a single-weekday weekly rule another weekday also
/// moves <c>Planning.NextExecutionTime</c> onto it. The items-planning scheduler only
/// ever adds <c>RepeatEvery * 7</c> days, so the weekday the next run sits on is the
/// weekday it deploys on from then on; left behind, the calendar drew the task twice
/// a week (the rule's new weekday and the deploys on the old one).
///
/// "Today" is pinned to Tuesday 2026-10-06 through the service's clock seam. The series
/// runs every Thursday from 2026-09-03 and its next run is Thursday 2026-10-15 (week of
/// Monday 2026-10-12). 2026-10-19 is a Monday, 2026-10-20 a Tuesday, 2026-10-22 a Thursday.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class CalendarWeeklyEditNextExecutionTests : TestBaseSetup
{
    private static readonly DateTime Now = new(2026, 10, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime SeriesStart = Utc(2026, 9, 3);
    private static readonly DateTime NextRun = Utc(2026, 10, 15);
    private static readonly DateTime LaterThursday = Utc(2026, 10, 22);

    private BackendConfigurationCalendarService _service = null!;

    private static DateTime Utc(int y, int m, int d) => new(y, m, d, 0, 0, 0, DateTimeKind.Utc);

    private static string Iso(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T00:00:00Z";

    [SetUp]
    public async Task SetUpService()
    {
        var core = await GetCore();
        var language = MicrotingDbContext!.Languages.OrderBy(x => x.Id).First();
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        userService.GetCurrentUserLanguage().Returns(Task.FromResult(language));
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));
        // The wizard is mocked: it writes nothing, so the rows read back are the calendar's own.
        var taskWizardService = Substitute.For<IBackendConfigurationTaskWizardService>();
        taskWizardService.UpdateTask(Arg.Any<TaskWizardCreateModel>())
            .Returns(Task.FromResult(new OperationResult(true)));
        _service = new BackendConfigurationCalendarService(
            new BackendConfigurationLocalizationService(), userService,
            BackendConfigurationPnDbContext!, coreHelper, Substitute.For<IEventDeployService>(),
            ItemsPlanningPnDbContext!, taskWizardService,
            Substitute.For<ICalendarAssignmentReconciliationService>(),
            Substitute.For<ICalendarChangeNotifier>(),
            TestContextLogger<BackendConfigurationCalendarService>.Instance,
            Substitute.For<ICalendarOccurrenceRetractionService>(),
            Substitute.For<ICalendarPastSeriesBackfillService>(),
            new WorkerTagMembershipService(coreHelper, BackendConfigurationPnDbContext))
        {
            UtcNow = () => Now
        };
    }

    /// <summary>The Thursday series (or <paramref name="weekdaysCsv"/>), with its next run on <see cref="NextRun"/>.</summary>
    private async Task<(int ArpId, int PlanningId)> SeedWeeklySeriesAsync(string? weekdaysCsv = "4")
    {
        var property = new Property
        {
            Name = $"WeeklyEditNext-{Guid.NewGuid()}", ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await property.Create(BackendConfigurationPnDbContext!);
        var area = new Area
        {
            Type = AreaTypesEnum.Type1, ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await area.Create(BackendConfigurationPnDbContext!);
        var areaRule = new AreaRule
        {
            AreaId = area.Id, PropertyId = property.Id, EformId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await areaRule.Create(BackendConfigurationPnDbContext!);
        await new AreaRuleTranslation
        {
            AreaRuleId = areaRule.Id, LanguageId = 1, Name = "Weekly check",
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        }.Create(BackendConfigurationPnDbContext!);

        var planning = new Planning
        {
            Enabled = true, RepeatEvery = 1, RepeatType = RepeatType.Week, StartDate = SeriesStart,
            DayOfWeek = DayOfWeek.Thursday, NextExecutionTime = NextRun, LastExecutedTime = NextRun.AddDays(-7),
            RelatedEFormId = 0, WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = property.Id, AreaId = area.Id,
            ItemPlanningId = planning.Id, StartDate = SeriesStart, Status = true,
            RepeatType = 2, RepeatEvery = 1, DayOfWeek = (int)DayOfWeek.Thursday, RepeatWeekdaysCsv = weekdaysCsv,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await arp.Create(BackendConfigurationPnDbContext!);
        await new CalendarConfiguration
        {
            AreaRulePlanningId = arp.Id, StartHour = 9.0, Duration = 1.0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        }.Create(BackendConfigurationPnDbContext!);
        return (arp.Id, planning.Id);
    }

    private async Task<Planning> PlanningAsync(int planningId)
        => await ItemsPlanningPnDbContext!.Plannings.AsNoTracking().SingleAsync(x => x.Id == planningId);

    private static CalendarTaskUpdateRequestModel Edit(int arpId, string scope, DateTime originalDate,
        DateTime startDate, string weekdaysCsv)
        => new()
        {
            Id = arpId, Scope = scope, OriginalDate = Iso(originalDate), StartDate = startDate,
            StartHour = 9.0, Duration = 1.0, Status = 1, RepeatType = 2, RepeatEvery = 1,
            RepeatWeekdaysCsv = weekdaysCsv, ComplianceEnabled = false, PropertyId = 0, EformId = 0,
            Sites = [101], TagIds = [], Translates = [new CommonTranslationsModel { LanguageId = 1, Name = "Weekly check" }]
        };

    private async Task MoveAsync(int arpId, DateTime originalDate, DateTime newDate, string scope)
    {
        var result = await _service.MoveTask(new CalendarTaskMoveRequestModel
        {
            Id = arpId, OriginalDate = Iso(originalDate), NewDate = Iso(newDate), NewStartHour = 9.0, Scope = scope
        });
        Assert.That(result.Success, Is.True, result.Message);
    }

    [Test]
    public async Task MoveTask_ScopeAll_ToAMonday_MovesTheNextRunOntoTheNewWeekday()
    {
        var (arpId, planningId) = await SeedWeeklySeriesAsync();

        await MoveAsync(arpId, LaterThursday, Utc(2026, 10, 19), "all");

        var planning = await PlanningAsync(planningId);
        Assert.Multiple(() =>
        {
            Assert.That(planning.DayOfWeek, Is.EqualTo(DayOfWeek.Monday));
            // The week of the old next run (Mon 10-12) is before the moved series start.
            Assert.That(planning.NextExecutionTime, Is.EqualTo(Utc(2026, 10, 19)),
                "the series' first Monday — the scheduler's +7 days then stays on Mondays");
        });
    }

    [Test]
    public async Task MoveTask_ThisAndFollowing_ToATuesday_MovesTheNextRunOntoTheNewWeekday()
    {
        var (arpId, planningId) = await SeedWeeklySeriesAsync();

        await MoveAsync(arpId, LaterThursday, Utc(2026, 10, 20), "thisAndFollowing");

        var planning = await PlanningAsync(planningId);
        Assert.Multiple(() =>
        {
            Assert.That(planning.DayOfWeek, Is.EqualTo(DayOfWeek.Tuesday));
            Assert.That(planning.NextExecutionTime, Is.EqualTo(Utc(2026, 10, 20)));
        });
    }

    [Test]
    public async Task MoveTask_MultiDayRule_LeavesTheNextRunAlone()
    {
        var (arpId, planningId) = await SeedWeeklySeriesAsync(weekdaysCsv: "1,4");

        await MoveAsync(arpId, LaterThursday, Utc(2026, 10, 20), "all");

        Assert.That((await PlanningAsync(planningId)).NextExecutionTime, Is.EqualTo(NextRun),
            "a multi-day rule has no single weekday to move the next run to");
    }

    [TestCase("all")]
    [TestCase("thisAndFollowing")]
    public async Task UpdateTask_WeekdayChangeToMonday_MovesTheNextRunOntoMonday(string scope)
    {
        var (arpId, planningId) = await SeedWeeklySeriesAsync();

        var result = await _service.UpdateTask(Edit(arpId, scope, LaterThursday, Utc(2026, 10, 19), "1"));
        Assert.That(result.Success, Is.True, result.Message);

        var planning = await PlanningAsync(planningId);
        Assert.Multiple(() =>
        {
            Assert.That(planning.DayOfWeek, Is.EqualTo(DayOfWeek.Monday));
            Assert.That(planning.NextExecutionTime!.Value.DayOfWeek, Is.EqualTo(DayOfWeek.Monday),
                "the next run follows the rule's new weekday");
            Assert.That(planning.NextExecutionTime!.Value, Is.GreaterThanOrEqualTo(Utc(2026, 10, 12)),
                "never before the old next run's week");
            Assert.That(planning.NextExecutionTime!.Value, Is.LessThanOrEqualTo(Utc(2026, 10, 19)),
                "and no later than the rule's first Monday after it");
        });
    }

    [Test]
    public async Task UpdateTask_FieldOnlyEdit_KeepsAnAlignedNextRun()
    {
        var (arpId, planningId) = await SeedWeeklySeriesAsync();

        var result = await _service.UpdateTask(Edit(arpId, "all", LaterThursday, LaterThursday, "4"));
        Assert.That(result.Success, Is.True, result.Message);

        Assert.That((await PlanningAsync(planningId)).NextExecutionTime, Is.EqualTo(NextRun),
            "a next run already on the rule's weekday is not touched");
    }

    [Test]
    public async Task MoveTask_LegacyRuleWithoutWeekdayList_FollowsTheNewStartDatesWeekday()
    {
        // No weekday list: the week view draws StartDate's weekday, and MoveTask leaves
        // arp.DayOfWeek (Thursday) as it was — the next run must follow the new start.
        var (arpId, planningId) = await SeedWeeklySeriesAsync(weekdaysCsv: null);

        await MoveAsync(arpId, LaterThursday, Utc(2026, 10, 20), "all");

        Assert.That((await PlanningAsync(planningId)).NextExecutionTime, Is.EqualTo(Utc(2026, 10, 20)),
            "on the moved series' first Tuesday, not on the stale Thursday");
    }

    [Test]
    public async Task UpdateTask_NewWeekdayAlreadyPastInTheNextRunsWeek_DoesNotSkipThatWeek()
    {
        // Today is Wednesday 2026-10-14, the old next run Thursday 10-15. Moving the rule
        // to Mondays puts that week's occurrence on Monday 10-12, already past: the next
        // run goes there (the scheduler deploys it on its next pass) instead of jumping to
        // 10-19 and leaving the week of 10-12 without a deploy.
        _service.UtcNow = () => new DateTime(2026, 10, 14, 10, 0, 0, DateTimeKind.Utc);
        var (arpId, planningId) = await SeedWeeklySeriesAsync();

        var result = await _service.UpdateTask(Edit(arpId, "all", LaterThursday, LaterThursday, "1"));
        Assert.That(result.Success, Is.True, result.Message);

        Assert.That((await PlanningAsync(planningId)).NextExecutionTime, Is.EqualTo(Utc(2026, 10, 12)));
    }

    [Test]
    public void NextWeeklyOccurrenceOnOrAfter_FollowsTheMondayAlignedStride()
    {
        // Every 2nd week from the week of Mon 2026-01-05: the week of 2026-10-05 is week 39 (off),
        // so a Thursday on or after Mon 2026-10-05 is Thu 2026-10-15 (week 40).
        var start = Utc(2026, 1, 5);
        Assert.Multiple(() =>
        {
            Assert.That(BackendConfigurationCalendarService.NextWeeklyOccurrenceOnOrAfter(
                start, 2, DayOfWeek.Thursday, Utc(2026, 10, 5)), Is.EqualTo(Utc(2026, 10, 15)));
            Assert.That(BackendConfigurationCalendarService.NextWeeklyOccurrenceOnOrAfter(
                start, 1, DayOfWeek.Thursday, Utc(2026, 10, 5)), Is.EqualTo(Utc(2026, 10, 8)));
            Assert.That(BackendConfigurationCalendarService.NextWeeklyOccurrenceOnOrAfter(
                start, 1, DayOfWeek.Monday, Utc(2026, 10, 5)), Is.EqualTo(Utc(2026, 10, 5)), "on the day itself");
            Assert.That(BackendConfigurationCalendarService.NextWeeklyOccurrenceOnOrAfter(
                start, 1, DayOfWeek.Sunday, Utc(2026, 10, 5)), Is.EqualTo(Utc(2026, 10, 11)),
                "Sunday ends the Monday-aligned week");
        });
    }
}
