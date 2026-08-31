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
*/

namespace BackendConfiguration.Pn.Integration.Test;

using System.Globalization;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Infrastructure.Models.TaskWizard;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService;
using BackendConfiguration.Pn.Services.EventDeployService;
using BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;
using BackendConfiguration.Pn.Services.CalendarChangeNotification;
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

/// <summary>
/// #952 follow-up — <c>EnumerateOccurrences</c> had no Year branch (its
/// <c>default:</c> did <c>yield break</c>), so it yielded nothing for yearly
/// tasks (RepeatType cast <c>(RepeatType)4</c>). That broke the "after N
/// occurrences" cap (which counts via EnumerateOccurrences) and every
/// <c>thisAndFollowing</c> past-anchor backfill for yearly series. Adding the
/// Year branch (mirroring <c>GetOccurrencesInWeek</c>) fixes both.
///
/// Test A exercises the after-count cap through <c>GetTasksForWeek</c>.
/// Test B exercises the <c>thisAndFollowing</c> past-anchor backfill through
/// <c>UpdateTask</c>.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class CalendarYearlyEnumerateTests : TestBaseSetup
{
    // Yearly RepeatType value. The plugin enum defines Year = 4; the
    // items-planning enum only defines Day/Week/Month, so a yearly Planning
    // stores the same value via a cast.
    private const int YearRepeatType = 4;

    private static string IsoUtc(DateTime d) =>
        DateTime.SpecifyKind(d, DateTimeKind.Utc)
            .ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static string Key(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // Monday (UTC) of the ISO week containing the given date.
    private static DateTime MondayOfWeek(DateTime target)
    {
        var t = target.Date;
        return DateTime.SpecifyKind(t.AddDays(-(((int)t.DayOfWeek + 6) % 7)), DateTimeKind.Utc);
    }

    /// <summary>
    /// Builds a calendar service whose <see cref="IEFormCoreService"/> returns the
    /// real SDK core (so GetTasksForWeek's case reads work) and whose task wizard
    /// is mocked to succeed (so UpdateTask's series-field delegation succeeds).
    /// </summary>
    private BackendConfigurationCalendarService BuildService(eFormCore.Core core)
    {
        var language = MicrotingDbContext!.Languages.First();
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        userService.GetCurrentUserLanguage().Returns(Task.FromResult(language));
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));
        var taskWizardService = Substitute.For<IBackendConfigurationTaskWizardService>();
        taskWizardService.DeleteTask(Arg.Any<int>()).Returns(Task.FromResult(new OperationResult(true)));
        taskWizardService.UpdateTask(Arg.Any<TaskWizardCreateModel>())
            .Returns(Task.FromResult(new OperationResult(true)));

        return new BackendConfigurationCalendarService(
            new BackendConfigurationLocalizationService(), userService,
            BackendConfigurationPnDbContext!, coreHelper, Substitute.For<IEventDeployService>(),
            ItemsPlanningPnDbContext!, taskWizardService,
            Substitute.For<ICalendarAssignmentReconciliationService>(),
            Substitute.For<ICalendarChangeNotifier>(),
            NullLogger<BackendConfigurationCalendarService>.Instance,
            Substitute.For<ICalendarOccurrenceRetractionService>(),
            Substitute.For<ICalendarPastSeriesBackfillService>());
    }

    /// <summary>
    /// Test A — yearly "after N occurrences" cap now applies.
    ///
    /// Pre-fix EnumerateOccurrences yielded nothing for yearly tasks, so the
    /// cumulative count never exceeded the cap and the cap never fired: the 3rd
    /// occurrence (2022) rendered even though the series is capped at 2.
    /// </summary>
    [Test]
    public async Task GetTasksForWeek_Yearly_AfterCountCap_SuppressesOccurrencesBeyondCap()
    {
        var core = await GetCore();

        var area = new Area
        {
            Type = AreaTypesEnum.Type1, ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Areas.AddAsync(area);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var property = new Property
        {
            Name = $"YearlyAfterCap-{Guid.NewGuid()}", ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.Properties.AddAsync(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var areaRule = new AreaRule
        {
            AreaId = area.Id, PropertyId = property.Id, EformId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRules.AddAsync(areaRule);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        // Yearly series anchored 2020-06-04, capped after 2 occurrences:
        // only 2020-06-04 and 2021-06-04 may render; 2022-06-04 must not.
        var startDate = new DateTime(2020, 6, 4, 0, 0, 0, DateTimeKind.Utc);
        var planning = new Planning
        {
            Enabled = true, RepeatEvery = 1, RepeatType = (RepeatType)YearRepeatType,
            DayOfMonth = 4, StartDate = startDate, RelatedEFormId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = property.Id, AreaId = area.Id,
            ItemPlanningId = planning.Id, StartDate = startDate, Status = true,
            RepeatType = YearRepeatType, RepeatEvery = 1, DayOfMonth = 4,
            RepeatEndMode = 1, RepeatOccurrences = 2, // after 2 occurrences
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

        var service = BuildService(core);

        // --- Week containing the 1st occurrence (2020-06-04): within the cap.
        var firstOcc = new DateTime(2020, 6, 4, 0, 0, 0, DateTimeKind.Utc);
        var firstWeekStart = MondayOfWeek(firstOcc);
        var firstResult = await service.GetTasksForWeek(new CalendarTaskRequestModel
        {
            PropertyId = property.Id,
            WeekStart = IsoUtc(firstWeekStart),
            WeekEnd = IsoUtc(firstWeekStart.AddDays(6).AddHours(23).AddMinutes(59)),
            ActionableOnly = false,
            BoardIds = [], TagNames = [], SiteIds = []
        });

        Assert.That(firstResult.Success, Is.True, firstResult.Message);
        var firstTiles = firstResult.Model!.Where(t => t.TaskDate == Key(firstOcc)).ToList();
        Assert.That(firstTiles, Has.Count.EqualTo(1),
            "1st yearly occurrence (within the cap of 2) renders");

        // --- Week containing the 3rd occurrence (2022-06-04): beyond the cap.
        var thirdOcc = new DateTime(2022, 6, 4, 0, 0, 0, DateTimeKind.Utc);
        var thirdWeekStart = MondayOfWeek(thirdOcc);
        var thirdResult = await service.GetTasksForWeek(new CalendarTaskRequestModel
        {
            PropertyId = property.Id,
            WeekStart = IsoUtc(thirdWeekStart),
            WeekEnd = IsoUtc(thirdWeekStart.AddDays(6).AddHours(23).AddMinutes(59)),
            ActionableOnly = false,
            BoardIds = [], TagNames = [], SiteIds = []
        });

        Assert.That(thirdResult.Success, Is.True, thirdResult.Message);
        var thirdTiles = thirdResult.Model!.Where(t => t.TaskDate == Key(thirdOcc)).ToList();
        // The regression: pre-fix the cap never fired (count stayed 0) so 2022 showed.
        Assert.That(thirdTiles, Is.Empty,
            "3rd yearly occurrence (2022) must be suppressed by the after-2 cap");
    }

    /// <summary>
    /// Test B — yearly <c>thisAndFollowing</c> creates past-year anchors.
    ///
    /// Editing "this and following" at a future yearly occurrence must freeze
    /// every PAST occurrence (strictly before originalDate) with the OLD values
    /// by writing a CalendarOccurrenceException per past date. Pre-fix
    /// EnumerateOccurrences yielded nothing for yearly, so zero anchors were
    /// created and the past occurrences silently inherited the edit.
    ///
    /// Dates are derived from <see cref="DateTime.UtcNow"/> so the test is not
    /// pinned to a calendar year, and the edit's StartDate is placed in next
    /// year so UpdateTask's "not in the past" validation always passes.
    /// </summary>
    [Test]
    public async Task UpdateTask_ThisAndFollowing_Yearly_AnchorsPastYearlyOccurrences()
    {
        var core = await GetCore();
        var language = await MicrotingDbContext!.Languages.FirstAsync();

        var sdkSite = new Site
        {
            Name = "yearly-taf-test-site", MicrotingUid = 7676,
            LanguageId = language.Id, WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddAsync(sdkSite);
        await MicrotingDbContext.SaveChangesAsync();

        var area = new Area
        {
            Type = AreaTypesEnum.Type1, ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Areas.AddAsync(area);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var property = new Property
        {
            Name = $"YearlyTaf-{Guid.NewGuid()}", ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.Properties.AddAsync(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var areaRule = new AreaRule
        {
            AreaId = area.Id, PropertyId = property.Id, EformId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRules.AddAsync(areaRule);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        // Anchor three years in the past so several real yearly occurrences exist
        // before the (future) edited occurrence. Derived from UtcNow so the test
        // is year-agnostic.
        var nowYear = DateTime.UtcNow.Year;
        var startDate = new DateTime(nowYear - 3, 6, 4, 0, 0, 0, DateTimeKind.Utc);
        // The edited occurrence is in NEXT year so UpdateTask's "StartDate must
        // not be in the past" validation always passes regardless of today.
        var originalDate = new DateTime(nowYear + 1, 6, 4, 0, 0, 0, DateTimeKind.Utc);
        var editStartDate = new DateTime(nowYear + 1, 6, 5, 0, 0, 0, DateTimeKind.Utc);

        var planning = new Planning
        {
            Enabled = true, RepeatEvery = 1, RepeatType = (RepeatType)YearRepeatType,
            DayOfMonth = 4, StartDate = startDate, RelatedEFormId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = property.Id, AreaId = area.Id,
            ItemPlanningId = planning.Id, StartDate = startDate, Status = true,
            RepeatType = YearRepeatType, RepeatEvery = 1, DayOfMonth = 4,
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

        var service = BuildService(core);

        var model = new CalendarTaskUpdateRequestModel
        {
            Id = arp.Id,
            Scope = "thisAndFollowing",
            OriginalDate = IsoUtc(originalDate),
            StartDate = editStartDate,
            StartHour = 11.0,
            Duration = 2.0,
            Status = 1,
            RepeatType = YearRepeatType,
            RepeatEvery = 1,
            RepeatOrdinalWeek = null,
            DayOfMonth = 5,
            ComplianceEnabled = false,
            PropertyId = property.Id,
            EformId = 0,
            Sites = [(int)sdkSite.Id],
            TagIds = [],
            Translates = []
        };

        var result = await service.UpdateTask(model);
        Assert.That(result.Success, Is.True, result.Message);

        // Past yearly occurrences strictly before originalDate ((nowYear+1)-06-04)
        // are June 4 of (nowYear-3), (nowYear-2), (nowYear-1) and (nowYear) = 4 anchors.
        var expectedPastDates = new[]
        {
            new DateTime(nowYear - 3, 6, 4),
            new DateTime(nowYear - 2, 6, 4),
            new DateTime(nowYear - 1, 6, 4),
            new DateTime(nowYear, 6, 4),
        };

        var anchors = await BackendConfigurationPnDbContext.CalendarOccurrenceExceptions
            .Where(x => x.AreaRulePlanningId == arp.Id)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .OrderBy(x => x.OriginalDate)
            .ToListAsync();

        var anchorDates = anchors.Select(a => a.OriginalDate.Date).ToList();

        Assert.Multiple(() =>
        {
            // Exactly one anchor per past year — no more, no less. A clean DB
            // (no compliances/exceptions seeded) has no path to extra anchors.
            Assert.That(anchors, Has.Count.EqualTo(expectedPastDates.Length),
                "exactly one frozen anchor per past yearly occurrence");
            // Pre-fix: zero anchors. Post-fix: one frozen anchor per past year.
            foreach (var d in expectedPastDates)
                Assert.That(anchorDates, Does.Contain(d),
                    $"a past-year anchor must exist for {d:yyyy-MM-dd}");
            foreach (var a in anchors)
            {
                Assert.That(a.OriginalDate.Date, Is.LessThan(originalDate.Date),
                    "anchors only cover occurrences before the edited date");
                Assert.That(a.IsDeleted, Is.False, "past anchors freeze (not delete) the occurrence");
            }
        });
    }
}
