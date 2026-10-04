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
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService;
using BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;
using BackendConfiguration.Pn.Services.CalendarChangeNotification;
using BackendConfiguration.Pn.Services.CalendarConfigurationBackfillService;
using BackendConfiguration.Pn.Services.EventDeployService;
using BackendConfiguration.Pn.Services.WorkerTagMembership;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Enums;
using NSubstitute;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// #1375 — a single-weekday weekly rule renders once per week. When the week already
/// holds a compliance row of the planning on ANOTHER weekday (a converted legacy
/// cadence, or a deploy made before the rule's weekday changed), the compliance is
/// that week's occurrence and the rule's own weekday is not drawn next to it.
/// Multi-day weekly rules keep their per-day behaviour.
/// 2026-01-05 and 2026-02-02 are Mondays; 2026-02-05 is a Thursday.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class CalendarWeeklyComplianceWeekRenderTests : TestBaseSetup
{
    private static readonly DateTime StartMonday = new(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime WeekMonday = new(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime WeekThursday = new(2026, 2, 5, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime NextWeekMonday = new(2026, 2, 9, 0, 0, 0, DateTimeKind.Utc);

    private int _siteCounter;

    private static string IsoUtc(DateTime d) =>
        DateTime.SpecifyKind(d, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static string Key(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private sealed record Seeded(Property Property, Area Area, AreaRulePlanning Arp, Planning Planning);

    /// <summary>
    /// A weekly task from <see cref="StartMonday"/>. With <paramref name="weekdaysCsv"/>
    /// it is an already-converted calendar rule with a board link; without it, the
    /// legacy wizard shape the startup conversion picks up.
    /// </summary>
    private async Task<Seeded> SeedWeeklyTask(string? weekdaysCsv)
    {
        var property = new Property
        {
            Name = $"WeeklyComplianceWeek-{Guid.NewGuid()}", ItemPlanningTagId = 0,
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
            AreaId = area.Id, PropertyId = property.Id, CreatedInGuide = true,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await areaRule.Create(BackendConfigurationPnDbContext!);

        var planning = new Planning
        {
            Enabled = true, RepeatEvery = 1, RepeatType = RepeatType.Week, StartDate = StartMonday,
            RelatedEFormId = 0, WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = property.Id, AreaId = area.Id,
            ItemPlanningId = planning.Id, StartDate = StartMonday, Status = true,
            RepeatType = 2, RepeatEvery = 1,
            DayOfWeek = weekdaysCsv == null ? 0 : (int)DayOfWeek.Monday,
            RepeatWeekdaysCsv = weekdaysCsv,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await arp.Create(BackendConfigurationPnDbContext!);

        if (weekdaysCsv != null)
        {
            var calConfig = new CalendarConfiguration
            {
                AreaRulePlanningId = arp.Id, StartHour = 9.0, Duration = 1.0,
                WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
            };
            await BackendConfigurationPnDbContext!.CalendarConfigurations.AddAsync(calConfig);
            await BackendConfigurationPnDbContext.SaveChangesAsync();
        }

        return new Seeded(property, area, arp, planning);
    }

    /// <summary>An open (deployed, not completed) compliance with the given deadline.</summary>
    private async Task SeedOpenCompliance(Seeded task, DateTime deadline)
    {
        var language = await MicrotingDbContext!.Languages.FirstAsync();
        var sdkSite = new Site
        {
            Name = "Jane Doe", MicrotingUid = 7375000 + ++_siteCounter, LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddAsync(sdkSite);
        await MicrotingDbContext.SaveChangesAsync();
        var sdkCase = new Case { SiteId = sdkSite.Id, Status = 66, WorkflowState = Constants.WorkflowStates.Created };
        await MicrotingDbContext.Cases.AddAsync(sdkCase);
        await MicrotingDbContext.SaveChangesAsync();

        await BackendConfigurationPnDbContext!.Compliances.AddAsync(new Compliance
        {
            PlanningId = task.Planning.Id, PropertyId = task.Property.Id, AreaId = task.Area.Id,
            Deadline = deadline, StartDate = deadline.AddDays(-7),
            MicrotingSdkCaseId = sdkCase.Id, MicrotingSdkeFormId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();
    }

    private async Task<List<CalendarTaskResponseModel>> QueryWeek(Seeded task, DateTime weekStartMonday, bool actionableOnly = false)
    {
        var core = await GetCore();
        var language = MicrotingDbContext!.Languages.OrderBy(x => x.Id).First();
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        userService.GetCurrentUserLanguage().Returns(Task.FromResult(language));
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));
        var service = new BackendConfigurationCalendarService(
            new BackendConfigurationLocalizationService(), userService,
            BackendConfigurationPnDbContext!, coreHelper, Substitute.For<IEventDeployService>(),
            ItemsPlanningPnDbContext!, Substitute.For<IBackendConfigurationTaskWizardService>(),
            Substitute.For<ICalendarAssignmentReconciliationService>(),
            Substitute.For<ICalendarChangeNotifier>(),
            TestContextLogger<BackendConfigurationCalendarService>.Instance,
            Substitute.For<ICalendarOccurrenceRetractionService>(),
            Substitute.For<ICalendarPastSeriesBackfillService>(),
            new WorkerTagMembershipService(coreHelper, BackendConfigurationPnDbContext));

        var result = await service.GetTasksForWeek(new CalendarTaskRequestModel
        {
            PropertyId = task.Property.Id,
            WeekStart = IsoUtc(weekStartMonday),
            WeekEnd = IsoUtc(weekStartMonday.AddDays(6).AddHours(23).AddMinutes(59)),
            ActionableOnly = actionableOnly, BoardIds = [], TagNames = [], SiteIds = []
        });
        Assert.That(result.Success, Is.True, result.Message);
        return result.Model!.Where(t => t.Id == task.Arp.Id).ToList();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task MondayRule_WithOpenThursdayComplianceInTheWeek_RendersOnceOnTheCompliance(bool actionableOnly)
    {
        var task = await SeedWeeklyTask(weekdaysCsv: "1");
        await SeedOpenCompliance(task, WeekThursday);

        var tiles = await QueryWeek(task, WeekMonday, actionableOnly);

        Assert.That(tiles.Select(t => t.TaskDate), Is.EqualTo(new[] { Key(WeekThursday) }),
            "the week renders once, on the open compliance, not again on the rule's Monday");
        Assert.That(tiles[0].IsFromCompliance, Is.True);
    }

    [Test]
    public async Task MondayRule_WeekWithoutCompliance_StillRendersTheRulesMonday()
    {
        var task = await SeedWeeklyTask(weekdaysCsv: "1");
        await SeedOpenCompliance(task, WeekThursday);

        var tiles = await QueryWeek(task, NextWeekMonday);

        Assert.That(tiles.Select(t => t.TaskDate), Is.EqualTo(new[] { Key(NextWeekMonday) }),
            "the guard is per week: a week with no compliance draws the rule");
        Assert.That(tiles[0].IsFromCompliance, Is.False);
    }

    [Test]
    public async Task MultiDayRule_WithOpenThursdayCompliance_KeepsItsOtherDays()
    {
        // Monday + Thursday: two occurrences per week, so a Thursday compliance is
        // only Thursday's and Monday still renders from the rule.
        var task = await SeedWeeklyTask(weekdaysCsv: "1,4");
        await SeedOpenCompliance(task, WeekThursday);

        var tiles = await QueryWeek(task, WeekMonday);

        Assert.That(tiles.Select(t => t.TaskDate), Is.EquivalentTo(new[] { Key(WeekMonday), Key(WeekThursday) }));
        Assert.That(tiles.Single(t => t.TaskDate == Key(WeekThursday)).IsFromCompliance, Is.True);
    }

    [Test]
    public async Task ConvertedLegacyTaskWithThursdayCadence_RendersOnlyOnThursdays()
    {
        // The reported shape: started on a Monday, the legacy cadence on Thursdays and
        // the running week's open compliance on Thursday. After the startup
        // conversion the rule is on Thursday, so both weeks render once, on Thursday.
        var task = await SeedWeeklyTask(weekdaysCsv: null);
        task.Planning.NextExecutionTime = WeekThursday;
        task.Planning.LastExecutedTime = WeekThursday.AddDays(-7);
        await ItemsPlanningPnDbContext!.SaveChangesAsync();
        await SeedOpenCompliance(task, WeekThursday);

        await new CalendarConfigurationBackfillService(
            BackendConfigurationPnDbContext!, ItemsPlanningPnDbContext!,
            TestContextLogger<CalendarConfigurationBackfillService>.Instance).RunIfNeededAsync();

        var thisWeek = await QueryWeek(task, WeekMonday);
        var nextWeek = await QueryWeek(task, NextWeekMonday);

        Assert.Multiple(() =>
        {
            Assert.That(thisWeek.Select(t => t.TaskDate), Is.EqualTo(new[] { Key(WeekThursday) }));
            Assert.That(nextWeek.Select(t => t.TaskDate), Is.EqualTo(new[] { Key(WeekThursday.AddDays(7)) }));
        });
    }
}
