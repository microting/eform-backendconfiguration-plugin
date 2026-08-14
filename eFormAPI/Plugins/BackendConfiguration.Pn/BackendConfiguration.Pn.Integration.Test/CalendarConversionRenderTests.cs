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
using BackendConfiguration.Pn.Services.CalendarConfigurationBackfillService;
using BackendConfiguration.Pn.Services.EventDeployService;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Enums;
using NSubstitute;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// Conversion-to-render seam for
/// <c>CalendarConfigurationBackfillService.RunIfNeededAsync</c>: the sibling
/// <c>CalendarConfigurationBackfillTest</c> proves the WRITTEN fields (ARP +
/// Planning recurrence normalization, 09:00-10:00 CalendarConfiguration); this
/// fixture proves that after conversion the real calendar week query
/// (<c>BackendConfigurationCalendarService.GetTasksForWeek</c>) actually renders
/// the converted tasks on the correct dates at 09:00-10:00 — and, via the
/// unconverted control, that the conversion is what makes them render at all.
/// All seeded dates are fixed in early-mid 2026 (past), never relative to now.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class CalendarConversionRenderTests : TestBaseSetup
{
    private CalendarConfigurationBackfillService _sut = null!;

    private static string IsoUtc(DateTime d) =>
        DateTime.SpecifyKind(d, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    [SetUp]
    public async Task SetUpConversionRender()
    {
        // FK-safe clean of the rows this fixture writes, mirroring
        // CalendarConfigurationBackfillTest's ordering (children before parents).
        BackendConfigurationPnDbContext!.AreaRuleTranslations.RemoveRange(
            BackendConfigurationPnDbContext.AreaRuleTranslations);
        BackendConfigurationPnDbContext.CalendarConfigurations.RemoveRange(
            BackendConfigurationPnDbContext.CalendarConfigurations);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.CalendarBoards.RemoveRange(
            BackendConfigurationPnDbContext.CalendarBoards);
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

        _sut = new CalendarConfigurationBackfillService(
            BackendConfigurationPnDbContext!,
            ItemsPlanningPnDbContext!,
            NullLogger<CalendarConfigurationBackfillService>.Instance);
    }

    private async Task<Property> SeedProperty()
    {
        var property = new Property
        {
            Name = $"CalendarConvRenderProp-{Guid.NewGuid()}",
            ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await property.Create(BackendConfigurationPnDbContext!);
        return property;
    }

    private async Task<Area> SeedArea()
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
        return area;
    }

    /// <summary>
    /// Seeds a full old-style wizard task: AreaRule (CreatedInGuide) +
    /// items-planning Planning carrying the old frequency encoding + the linked
    /// AreaRulePlanning — same shape as
    /// <c>CalendarConfigurationBackfillTest.SeedWizardTask</c> (frequency
    /// mirrored on both entities, ARP detail columns left at defaults).
    /// </summary>
    private async Task<(AreaRulePlanning Arp, Planning Planning)> SeedWizardTask(
        int propertyId,
        int areaId,
        int repeatType,
        int repeatEvery,
        DateTime startDate)
    {
        var areaRule = new AreaRule
        {
            AreaId = areaId,
            PropertyId = propertyId,
            CreatedInGuide = true,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await areaRule.Create(BackendConfigurationPnDbContext!);

        var planning = new Planning
        {
            Enabled = true,
            RepeatEvery = repeatEvery,
            RepeatType = (RepeatType)repeatType,
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
            PropertyId = propertyId,
            AreaId = areaId,
            ItemPlanningId = planning.Id,
            StartDate = startDate,
            Status = true,
            RepeatType = repeatType,
            RepeatEvery = repeatEvery,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await arp.Create(BackendConfigurationPnDbContext!);
        return (arp, planning);
    }

    // Same construction as CalendarMultiLanguageTitleDescriptionTests.BuildService,
    // with the caller language pinned to the first SDK-seeded language (no
    // per-language assertions here — titles/translations are not under test).
    private BackendConfigurationCalendarService BuildCalendarService(eFormCore.Core core)
    {
        var language = MicrotingDbContext!.Languages.OrderBy(x => x.Id).First();
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        userService.GetCurrentUserLanguage().Returns(Task.FromResult(language));
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));
        var taskWizardService = Substitute.For<IBackendConfigurationTaskWizardService>();
        return new BackendConfigurationCalendarService(
            new BackendConfigurationLocalizationService(), userService,
            BackendConfigurationPnDbContext!, coreHelper, Substitute.For<IEventDeployService>(),
            ItemsPlanningPnDbContext!, taskWizardService,
            Substitute.For<ICalendarAssignmentReconciliationService>(),
            NullLogger<BackendConfigurationCalendarService>.Instance);
    }

    /// <summary>
    /// Runs the real week query for the Mon-Sun week starting at
    /// <paramref name="weekStartMonday"/> — identical request shape to
    /// <c>CalendarMultiLanguageTitleDescriptionTests</c>.
    /// </summary>
    private async Task<List<CalendarTaskResponseModel>> QueryWeek(int propertyId, DateTime weekStartMonday)
    {
        var core = await GetCore();
        var svc = BuildCalendarService(core);
        var res = await svc.GetTasksForWeek(new CalendarTaskRequestModel
        {
            PropertyId = propertyId,
            WeekStart = IsoUtc(weekStartMonday),
            WeekEnd = IsoUtc(weekStartMonday.AddDays(6).AddHours(23).AddMinutes(59)),
            ActionableOnly = false, BoardIds = [], TagNames = [], SiteIds = []
        });
        Assert.That(res.Success, Is.True, res.Message);
        return res.Model!;
    }

    private static void AssertNineToTen(CalendarTaskResponseModel tile)
    {
        Assert.That(tile.StartHour, Is.EqualTo(9.0), $"StartHour on {tile.TaskDate}");
        Assert.That(tile.Duration, Is.EqualTo(1.0), $"Duration on {tile.TaskDate}");
        Assert.That(tile.IsAllDay, Is.False, $"IsAllDay on {tile.TaskDate}");
    }

    [Test]
    public async Task AltidTask_AfterConversion_RendersAllSevenDaysOfLaterWeekNineToTen()
    {
        var property = await SeedProperty();
        var area = await SeedArea();
        // Wizard "Altid" encoding (0,0); 2026-01-07 is a Wednesday.
        var (arp, _) = await SeedWizardTask(
            property.Id, area.Id, repeatType: 0, repeatEvery: 0,
            startDate: new DateTime(2026, 1, 7));

        await _sut.RunIfNeededAsync();

        // A later week entirely past the StartDate: Mon 2026-02-02 .. Sun 2026-02-08.
        var tiles = await QueryWeek(property.Id, new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc));

        var mine = tiles.Where(t => t.Id == arp.Id).ToList();
        Assert.That(mine, Has.Count.EqualTo(7), "converted Altid task must render every day of the week");
        Assert.That(mine.Select(t => t.TaskDate),
            Is.EquivalentTo(Enumerable.Range(2, 7).Select(d => $"2026-02-{d:00}")));
        foreach (var tile in mine)
        {
            AssertNineToTen(tile);
        }
    }

    [Test]
    public async Task DayEveryThreeTask_AfterConversion_RendersOnStrideDatesOnly()
    {
        var property = await SeedProperty();
        var area = await SeedArea();
        // (Day, 3) from Wed 2026-01-07: occurrences 07, 10, 13, 16, 19, 22, 25 Jan ...
        var (arp, _) = await SeedWizardTask(
            property.Id, area.Id, repeatType: (int)RepeatType.Day, repeatEvery: 3,
            startDate: new DateTime(2026, 1, 7));

        await _sut.RunIfNeededAsync();

        // Week Mon 2026-01-19 .. Sun 2026-01-25 starts exactly on the +12-day
        // occurrence, so the in-week pattern is +0/+3/+6: Mon 19, Thu 22, Sun 25.
        var tiles = await QueryWeek(property.Id, new DateTime(2026, 1, 19, 0, 0, 0, DateTimeKind.Utc));

        var mine = tiles.Where(t => t.Id == arp.Id).ToList();
        Assert.That(mine.Select(t => t.TaskDate),
            Is.EquivalentTo(new[] { "2026-01-19", "2026-01-22", "2026-01-25" }),
            "every-3-days must render only on the stride dates; the days between have none");
        foreach (var tile in mine)
        {
            AssertNineToTen(tile);
        }
    }

    [Test]
    public async Task WeeklyTaskStartingWednesday_AfterConversion_RendersExactlyOnWednesdayOfLaterWeek()
    {
        var property = await SeedProperty();
        var area = await SeedArea();
        // (Week, 1) anchored on Wed 2026-01-07.
        var (arp, _) = await SeedWizardTask(
            property.Id, area.Id, repeatType: (int)RepeatType.Week, repeatEvery: 1,
            startDate: new DateTime(2026, 1, 7));

        await _sut.RunIfNeededAsync();

        // Later week Mon 2026-02-02 .. Sun 2026-02-08; its Wednesday is 2026-02-04.
        var tiles = await QueryWeek(property.Id, new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc));

        var mine = tiles.Where(t => t.Id == arp.Id).ToList();
        Assert.That(mine, Has.Count.EqualTo(1), "weekly task must render exactly once per week");
        Assert.That(mine[0].TaskDate, Is.EqualTo("2026-02-04"), "on the anchor weekday (Wednesday)");
        AssertNineToTen(mine[0]);
    }

    [Test]
    public async Task BiweeklyTaskStartingSunday_AfterConversion_RendersOnlyInAnchorParityWeeks()
    {
        var property = await SeedProperty();
        var area = await SeedArea();
        // (Week, 2) anchored on Sun 2026-01-04. Monday-aligned week buckets put
        // the anchor Sunday in week Mon 2025-12-29 .. Sun 2026-01-04, so the
        // even-parity occurrence Sundays are 04 Jan, 18 Jan, 01 Feb ...
        var (arp, _) = await SeedWizardTask(
            property.Id, area.Id, repeatType: (int)RepeatType.Week, repeatEvery: 2,
            startDate: new DateTime(2026, 1, 4));

        await _sut.RunIfNeededAsync();

        // Off-parity week Mon 2026-01-05 .. Sun 2026-01-11: no occurrence.
        var offParity = await QueryWeek(property.Id, new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc));
        Assert.That(offParity.Where(t => t.Id == arp.Id), Is.Empty,
            "every-2nd-week task must skip the off-parity week");

        // Anchor-parity week Mon 2026-01-12 .. Sun 2026-01-18: Sunday 2026-01-18.
        var parity = await QueryWeek(property.Id, new DateTime(2026, 1, 12, 0, 0, 0, DateTimeKind.Utc));
        var mine = parity.Where(t => t.Id == arp.Id).ToList();
        Assert.That(mine, Has.Count.EqualTo(1), "exactly one occurrence in the anchor-parity week");
        Assert.That(mine[0].TaskDate, Is.EqualTo("2026-01-18"), "on the anchor weekday (Sunday)");
        AssertNineToTen(mine[0]);
    }

    [Test]
    public async Task MonthlyTaskStartingJan31_AfterConversion_RendersFirstSaturdayNotDayOfMonth()
    {
        var property = await SeedProperty();
        var area = await SeedArea();
        // (Month, 1) from Sat 2026-01-31 — the conversion rewrites it to
        // "1st Saturday of every month" (ordinal-weekday), dropping the
        // day-of-month anchor entirely.
        var (arp, _) = await SeedWizardTask(
            property.Id, area.Id, repeatType: (int)RepeatType.Month, repeatEvery: 1,
            startDate: new DateTime(2026, 1, 31));

        await _sut.RunIfNeededAsync();

        // February's 1st Saturday is 2026-02-07 (Feb 1st 2026 is a Sunday);
        // its week is Mon 2026-02-02 .. Sun 2026-02-08.
        var febWeek = await QueryWeek(property.Id, new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc));
        var febMine = febWeek.Where(t => t.Id == arp.Id).ToList();
        Assert.That(febMine, Has.Count.EqualTo(1), "one occurrence in the first-Saturday week of February");
        Assert.That(febMine[0].TaskDate, Is.EqualTo("2026-02-07"), "on February's 1st Saturday");
        AssertNineToTen(febMine[0]);

        // Day-of-month semantics are gone: the week containing the next
        // 31st (Tue 2026-03-31; week Mon 2026-03-30 .. Sun 2026-04-05) has NO
        // occurrence on the 31st — only April's 1st Saturday (2026-04-04).
        var day31Week = await QueryWeek(property.Id, new DateTime(2026, 3, 30, 0, 0, 0, DateTimeKind.Utc));
        var day31Mine = day31Week.Where(t => t.Id == arp.Id).ToList();
        Assert.That(day31Mine.Select(t => t.TaskDate), Does.Not.Contain("2026-03-31"),
            "no occurrence on the 31st — day-of-month semantics gone");
        Assert.That(day31Mine.Select(t => t.TaskDate), Is.EquivalentTo(new[] { "2026-04-04" }),
            "the only occurrence in that week is April's 1st Saturday");
    }

    [Test]
    public async Task AltidTask_WithoutConversion_DoesNotRenderInLaterWeek()
    {
        var property = await SeedProperty();
        var area = await SeedArea();
        // Same Altid seed as the converted test — but RunIfNeededAsync is NOT
        // run. The raw (0,0) Planning only renders on its exact StartDate
        // (non-recurring branch of the week query), so a later week is empty:
        // the conversion is what makes the task render.
        var (arp, _) = await SeedWizardTask(
            property.Id, area.Id, repeatType: 0, repeatEvery: 0,
            startDate: new DateTime(2026, 1, 7));

        var tiles = await QueryWeek(property.Id, new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc));

        Assert.That(tiles.Where(t => t.Id == arp.Id), Is.Empty,
            "unconverted Altid task must not render in a later week");
    }
}
