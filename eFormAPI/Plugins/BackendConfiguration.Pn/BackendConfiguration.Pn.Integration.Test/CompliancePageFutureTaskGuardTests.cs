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
using eFormCore;
using BackendConfiguration.Pn.Infrastructure.Helpers;
using BackendConfiguration.Pn.Services.BackendConfigurationCompliancesService;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.Application.Case.CaseEdit;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Enums;
using NSubstitute;

/// <summary>
/// #1300 — the compliance pages must not complete or delete an UNCOMPLETED task dated after
/// today in Danish local time (Europe/Copenhagen date, date-level). COMPLETED future rows
/// stay deletable; the calendar keeps its early completion (no <c>source</c> flag).
///
/// <para>Every service under test gets a PINNED clock through its instance-level
/// <c>UtcNow</c> seam, so the Copenhagen-midnight boundary cells are deterministic. The
/// absolute dates in the boundary cells are only ever compared to that pinned clock, never
/// to the real one (Delete and the pre-flight read no other date). The relative cells pin
/// the clock to one captured instant, so a run that straddles midnight cannot flip a
/// cell.</para>
///
/// <para>SDK cases on the Delete path are seeded with <c>MicrotingUid = null</c> (no
/// platform retraction is attempted). On the completion path they get a decoy row sharing
/// the MicrotingUid so <c>Core.CaseDelete</c> throws locally instead of dialling the
/// platform — the same offline trick as <c>ComplianceCompletionLegacyPathsTests</c>.</para>
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class CompliancePageFutureTaskGuardTests : TestBaseSetup
{
    private const int CompletedStatus = 100;
    private const int OpenCaseStatus = 33;
    private const int OpenPlanningStatus = 66;

    private Core? _core;
    private int _uidCounter = 981_000;

    private async Task<Core> SharedCore() => _core ??= await GetCore();

    // ------------------------------------------------------------------
    // Service construction
    // ------------------------------------------------------------------

    private static IEFormCoreService CoreHelper(Core core)
    {
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));
        return coreHelper;
    }

    private BackendConfigurationCompliancesService BuildService(Core core, Language language, DateTime pinnedUtcNow)
    {
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        userService.GetCurrentUserLanguage().Returns(Task.FromResult(language));
        userService.GetCurrentUserLocale().Returns(Task.FromResult("en-US"));

        return new BackendConfigurationCompliancesService(
            ItemsPlanningPnDbContext!,
            BackendConfigurationPnDbContext!,
            userService,
            // Test-project stub that echoes the resource key.
            new BackendConfigurationLocalizationService(),
            CoreHelper(core),
            TimePlanningPnDbContext!)
        {
            UtcNow = () => pinnedUtcNow
        };
    }

    private static DateTime Utc(string iso) =>
        DateTime.SpecifyKind(DateTime.Parse(iso, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal), DateTimeKind.Utc);

    private static DateTime Day(string iso) =>
        DateTime.SpecifyKind(DateTime.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture), DateTimeKind.Utc);

    // ------------------------------------------------------------------
    // Seeding
    // ------------------------------------------------------------------

    private sealed class Series
    {
        public int PropertyId;
        public int AreaId;
        public int ArpId;
        public int PlanningId;
        public int CheckListId;
        public Site Site = null!;
        public Language Language = null!;
    }

    private sealed class Occurrence
    {
        public int ComplianceId;
        public int SdkCaseId;
        public int PlanningCaseId;
        public int PlanningCaseSiteId;
    }

    /// <summary>
    /// Area → Property → AreaRule → Planning → AreaRulePlanning (live, so the effective-date
    /// resolution and the completed-log marker both have their ARP), plus an SDK site and a
    /// bare CheckList.
    /// </summary>
    private async Task<Series> SeedSeries(string tag)
    {
        await SharedCore();
        var language = await MicrotingDbContext!.Languages.FirstAsync();

        var checkList = new CheckList
        {
            Label = $"future-guard-{tag}-{Guid.NewGuid()}",
            ParentId = null,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.CheckLists.AddAsync(checkList);
        await MicrotingDbContext.SaveChangesAsync();

        var site = new Site
        {
            Name = $"future-guard-{tag}-{Guid.NewGuid()}",
            MicrotingUid = ++_uidCounter,
            LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddAsync(site);
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
            Name = $"FutureGuard-{tag}-{Guid.NewGuid()}", ItemPlanningTagId = 0,
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

        var startDate = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(-60), DateTimeKind.Utc);
        var planning = new Planning
        {
            Enabled = true, RepeatEvery = 1, RepeatType = RepeatType.Week,
            StartDate = startDate, DayOfWeek = DayOfWeek.Monday, RelatedEFormId = checkList.Id,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = property.Id, AreaId = area.Id,
            ItemPlanningId = planning.Id, StartDate = startDate, Status = true,
            RepeatType = 2, RepeatEvery = 1, RepeatWeekdaysCsv = "1", DayOfWeek = 1,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(arp);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        return new Series
        {
            PropertyId = property.Id, AreaId = area.Id, ArpId = arp.Id, PlanningId = planning.Id,
            CheckListId = checkList.Id, Site = site, Language = language
        };
    }

    /// <summary>
    /// One occurrence on <paramref name="taskDate"/>. <paramref name="completed"/> seeds the
    /// shape completion leaves behind (case <c>Status = 100</c>, Compliance soft-removed).
    /// <paramref name="withDecoy"/> adds a second case sharing the MicrotingUid, for the
    /// completion path (see the class remarks); otherwise MicrotingUid stays null.
    /// </summary>
    private async Task<Occurrence> SeedOccurrence(Series series, DateTime taskDate, bool completed,
        bool withDecoy = false)
    {
        int? microtingUid = withDecoy ? ++_uidCounter : null;
        var sdkCase = new Case
        {
            SiteId = series.Site.Id,
            CheckListId = series.CheckListId,
            Status = completed ? CompletedStatus : OpenCaseStatus,
            DoneAt = completed ? DateTime.SpecifyKind(taskDate, DateTimeKind.Utc) : null,
            MicrotingUid = microtingUid,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext!.Cases.AddAsync(sdkCase);
        if (withDecoy)
        {
            await MicrotingDbContext.Cases.AddAsync(new Case
            {
                SiteId = series.Site.Id,
                CheckListId = series.CheckListId,
                Status = OpenCaseStatus,
                MicrotingUid = microtingUid,
                WorkflowState = Constants.WorkflowStates.Created
            });
        }
        await MicrotingDbContext.SaveChangesAsync();

        var planningCase = new PlanningCase
        {
            PlanningId = series.PlanningId,
            MicrotingSdkeFormId = series.CheckListId,
            MicrotingSdkCaseId = completed ? sdkCase.Id : 0,
            Status = completed ? CompletedStatus : OpenPlanningStatus,
            WorkflowState = completed ? Constants.WorkflowStates.Processed : Constants.WorkflowStates.Created,
            CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.PlanningCases.AddAsync(planningCase);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var planningCaseSite = new PlanningCaseSite
        {
            PlanningId = series.PlanningId,
            PlanningCaseId = planningCase.Id,
            MicrotingSdkSiteId = series.Site.Id,
            MicrotingSdkeFormId = series.CheckListId,
            MicrotingSdkCaseId = sdkCase.Id,
            Status = completed ? CompletedStatus : OpenPlanningStatus,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext.PlanningCaseSites.AddAsync(planningCaseSite);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var compliance = new Compliance
        {
            ItemName = "Future guard item",
            PlanningId = series.PlanningId,
            PropertyId = series.PropertyId,
            AreaId = series.AreaId,
            Deadline = DateTime.SpecifyKind(taskDate.Date, DateTimeKind.Utc),
            StartDate = DateTime.SpecifyKind(taskDate.Date.AddDays(-7), DateTimeKind.Utc),
            MicrotingSdkCaseId = sdkCase.Id,
            MicrotingSdkeFormId = series.CheckListId,
            WorkflowState = completed ? Constants.WorkflowStates.Removed : Constants.WorkflowStates.Created,
            CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Compliances.AddAsync(compliance);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        return new Occurrence
        {
            ComplianceId = compliance.Id, SdkCaseId = sdkCase.Id,
            PlanningCaseId = planningCase.Id, PlanningCaseSiteId = planningCaseSite.Id
        };
    }

    /// <summary>A "this"-scope move of the occurrence on <paramref name="originalDate"/>.</summary>
    private async Task SeedMove(Series series, DateTime originalDate, DateTime newDate)
    {
        await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions.AddAsync(new CalendarOccurrenceException
        {
            AreaRulePlanningId = series.ArpId,
            OriginalDate = DateTime.SpecifyKind(originalDate.Date, DateTimeKind.Utc),
            NewDate = DateTime.SpecifyKind(newDate.Date, DateTimeKind.Utc),
            IsDeleted = false,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();
    }

    private static ReplyRequest Reply(Series series, Occurrence occ, int? siteId = null) => new()
    {
        Id = occ.SdkCaseId,
        Label = "future-guard",
        DoneAt = new DateTime(2026, 3, 17, 10, 0, 0, DateTimeKind.Unspecified),
        IsDoneAtEditable = true,
        ExtraId = occ.ComplianceId,
        SiteId = siteId ?? series.Site.Id,
        ElementList = []
    };

    private void ClearTrackers()
    {
        BackendConfigurationPnDbContext!.ChangeTracker.Clear();
        ItemsPlanningPnDbContext!.ChangeTracker.Clear();
        MicrotingDbContext!.ChangeTracker.Clear();
    }

    private async Task<Compliance> ReadCompliance(int id)
    {
        ClearTrackers();
        return await BackendConfigurationPnDbContext!.Compliances.AsNoTracking().FirstAsync(x => x.Id == id);
    }

    private async Task<Case> ReadCase(int id) =>
        await MicrotingDbContext!.Cases.AsNoTracking().FirstAsync(x => x.Id == id);

    /// <summary>One captured instant, and the Copenhagen date it falls on.</summary>
    private static (DateTime Now, DateTime Today) PinnedNow()
    {
        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
        return (now, ComplianceFutureTaskGuard.TodayInCopenhagen(now));
    }

    // ==================================================================
    // 0. The pure helper — Copenhagen date around midnight and both DST switches.
    // ==================================================================

    [TestCase("2026-01-15T22:59:00Z", "2026-01-15", TestName = "Today_Cet_2359Local_IsSameDay")]
    [TestCase("2026-01-15T23:00:00Z", "2026-01-16", TestName = "Today_Cet_0000Local_IsNextDay_WhileUtcDateIsStillThe15th")]
    [TestCase("2026-01-15T23:01:00Z", "2026-01-16", TestName = "Today_Cet_0001Local_IsNextDay")]
    [TestCase("2026-07-15T21:59:00Z", "2026-07-15", TestName = "Today_Cest_2359Local_IsSameDay")]
    [TestCase("2026-07-15T22:00:00Z", "2026-07-16", TestName = "Today_Cest_0000Local_IsNextDay_WhileUtcDateIsStillThe15th")]
    [TestCase("2026-07-15T22:01:00Z", "2026-07-16", TestName = "Today_Cest_0001Local_IsNextDay")]
    [TestCase("2026-03-28T22:59:00Z", "2026-03-28", TestName = "Today_EveOfSpringForward_2359Cet")]
    [TestCase("2026-03-28T23:00:00Z", "2026-03-29", TestName = "Today_SpringForwardDay_0000Cet")]
    [TestCase("2026-03-29T21:59:00Z", "2026-03-29", TestName = "Today_SpringForwardDay_2359Cest")]
    [TestCase("2026-03-29T22:00:00Z", "2026-03-30", TestName = "Today_AfterSpringForward_0000Cest")]
    [TestCase("2026-10-24T21:59:00Z", "2026-10-24", TestName = "Today_EveOfFallBack_2359Cest")]
    [TestCase("2026-10-24T22:00:00Z", "2026-10-25", TestName = "Today_FallBackDay_0000Cest")]
    [TestCase("2026-10-25T22:59:00Z", "2026-10-25", TestName = "Today_FallBackDay_2359Cet")]
    [TestCase("2026-10-25T23:00:00Z", "2026-10-26", TestName = "Today_AfterFallBack_0000Cet")]
    public void TodayInCopenhagen_IsTheDanishDate(string utcNow, string expectedToday)
    {
        Assert.That(ComplianceFutureTaskGuard.TodayInCopenhagen(Utc(utcNow)), Is.EqualTo(Day(expectedToday).Date));
    }

    [Test]
    public void TodayInCopenhagen_UnspecifiedKind_IsTreatedAsUtc()
    {
        var unspecified = DateTime.SpecifyKind(new DateTime(2026, 1, 15, 23, 30, 0), DateTimeKind.Unspecified);
        Assert.That(ComplianceFutureTaskGuard.TodayInCopenhagen(unspecified), Is.EqualTo(new DateTime(2026, 1, 16)));
    }

    [TestCase("compliance", true)]
    [TestCase("Compliance", true)]
    [TestCase(" compliance ", true)]
    [TestCase(null, false)]
    [TestCase("", false)]
    [TestCase("calendar", false)]
    [TestCase("compliances", false)]
    public void IsCompliancePageSource_OnlyTheExactFlag(string? source, bool expected)
    {
        Assert.That(ComplianceFutureTaskGuard.IsCompliancePageSource(source!), Is.EqualTo(expected));
    }

    // ==================================================================
    // 1. Delete — uncompleted future is refused; everything else as before.
    // ==================================================================

    [TestCase(1)]
    [TestCase(7)]
    [TestCase(400)]
    public async Task Delete_UncompletedFutureTask_IsRejected_AndMutatesNothing(int daysAhead)
    {
        var core = await SharedCore();
        var (now, today) = PinnedNow();
        var series = await SeedSeries($"del-future-{daysAhead}");
        var occ = await SeedOccurrence(series, today.AddDays(daysAhead), completed: false);

        var result = await BuildService(core, series.Language, now).Delete(occ.ComplianceId);

        var compliance = await ReadCompliance(occ.ComplianceId);
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Is.EqualTo("FutureTaskCannotBeDeleted"));
            Assert.That(compliance.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created),
                "a refused delete must leave the row live");
        });
    }

    [TestCase(0, TestName = "Delete_UncompletedTask_Today_IsDeleted")]
    [TestCase(-1, TestName = "Delete_UncompletedTask_Yesterday_IsDeleted")]
    [TestCase(-30, TestName = "Delete_UncompletedTask_30DaysAgo_IsDeleted")]
    public async Task Delete_UncompletedTask_TodayOrPast_IsDeleted(int daysAhead)
    {
        var core = await SharedCore();
        var (now, today) = PinnedNow();
        var series = await SeedSeries($"del-past-{daysAhead}");
        var occ = await SeedOccurrence(series, today.AddDays(daysAhead), completed: false);

        var result = await BuildService(core, series.Language, now).Delete(occ.ComplianceId);

        var compliance = await ReadCompliance(occ.ComplianceId);
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(result.Message, Is.EqualTo("TaskDeletedSuccessful"));
            Assert.That(compliance.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
        });
    }

    /// <summary>A task completed EARLY (e.g. from the calendar) stays deletable (#1290 path).</summary>
    [TestCase(1)]
    [TestCase(30)]
    public async Task Delete_CompletedFutureTask_IsStillDeletable(int daysAhead)
    {
        var core = await SharedCore();
        var (now, today) = PinnedNow();
        var series = await SeedSeries($"del-completed-future-{daysAhead}");
        var taskDate = today.AddDays(daysAhead);
        var occ = await SeedOccurrence(series, taskDate, completed: true);

        var result = await BuildService(core, series.Language, now).Delete(occ.ComplianceId);

        ClearTrackers();
        var marker = await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions.AsNoTracking()
            .Where(x => x.AreaRulePlanningId == series.ArpId
                        && x.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync();
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(marker.Any(x => x.IsDeleted && x.OriginalDate.Date == taskDate.Date), Is.True,
                "the completed log is deleted through the #1290 marker");
        });
    }

    /// <summary>
    /// Copenhagen-midnight boundary, both CET and CEST. The task is dated the 16th; the
    /// clock sits one minute either side of Danish midnight. The 00:00/00:01 cells are
    /// where a UTC "today" (still the 15th) would wrongly treat the task as future.
    /// </summary>
    [TestCase("2026-01-15T22:59:00Z", "2026-01-16", false, TestName = "Delete_Boundary_Cet_2359_TaskTomorrow_IsRejected")]
    [TestCase("2026-01-15T23:00:00Z", "2026-01-16", true, TestName = "Delete_Boundary_Cet_0000_TaskToday_IsDeleted")]
    [TestCase("2026-01-15T23:01:00Z", "2026-01-16", true, TestName = "Delete_Boundary_Cet_0001_TaskToday_IsDeleted")]
    [TestCase("2026-01-15T22:59:00Z", "2026-01-15", true, TestName = "Delete_Boundary_Cet_2359_TaskToday_IsDeleted")]
    [TestCase("2026-07-15T21:59:00Z", "2026-07-16", false, TestName = "Delete_Boundary_Cest_2359_TaskTomorrow_IsRejected")]
    [TestCase("2026-07-15T22:00:00Z", "2026-07-16", true, TestName = "Delete_Boundary_Cest_0000_TaskToday_IsDeleted")]
    [TestCase("2026-07-15T22:01:00Z", "2026-07-16", true, TestName = "Delete_Boundary_Cest_0001_TaskToday_IsDeleted")]
    [TestCase("2026-07-15T21:59:00Z", "2026-07-15", true, TestName = "Delete_Boundary_Cest_2359_TaskToday_IsDeleted")]
    public async Task Delete_UncompletedTask_AtCopenhagenMidnight(string utcNow, string taskDate, bool expectDeleted)
    {
        var core = await SharedCore();
        var series = await SeedSeries($"del-boundary-{utcNow}-{taskDate}");
        var occ = await SeedOccurrence(series, Day(taskDate), completed: false);

        var result = await BuildService(core, series.Language, Utc(utcNow)).Delete(occ.ComplianceId);

        var compliance = await ReadCompliance(occ.ComplianceId);
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.EqualTo(expectDeleted), result.Message);
            Assert.That(compliance.WorkflowState, Is.EqualTo(expectDeleted
                ? Constants.WorkflowStates.Removed
                : Constants.WorkflowStates.Created));
            if (!expectDeleted)
            {
                Assert.That(result.Message, Is.EqualTo("FutureTaskCannotBeDeleted"));
            }
        });
    }

    /// <summary>
    /// The guard judges the date the compliance pages SHOW: a "this"-scope move applies its
    /// NewDate (as BuildCandidateSet does). Today→tomorrow is refused; tomorrow→today is not.
    /// </summary>
    [TestCase(0, 1, false, TestName = "Delete_MovedFromTodayToTomorrow_IsRejected")]
    [TestCase(1, 0, true, TestName = "Delete_MovedFromTomorrowToToday_IsDeleted")]
    public async Task Delete_MovedOccurrence_UsesTheEffectiveTaskDate(int originalOffset, int newOffset, bool expectDeleted)
    {
        var core = await SharedCore();
        var (now, today) = PinnedNow();
        var series = await SeedSeries($"del-moved-{originalOffset}-{newOffset}");
        var occ = await SeedOccurrence(series, today.AddDays(originalOffset), completed: false);
        await SeedMove(series, today.AddDays(originalOffset), today.AddDays(newOffset));

        var result = await BuildService(core, series.Language, now).Delete(occ.ComplianceId);

        Assert.That(result.Success, Is.EqualTo(expectDeleted), result.Message);
    }

    // ==================================================================
    // 2. Completion — blocked only for the compliance-page source.
    // ==================================================================

    [TestCase(1)]
    [TestCase(10)]
    public async Task UpdateFromCalendar_CompliancePageSource_FutureTask_IsRejected_AndMutatesNothing(int daysAhead)
    {
        var core = await SharedCore();
        var (now, today) = PinnedNow();
        var series = await SeedSeries($"ufc-future-{daysAhead}");
        var occ = await SeedOccurrence(series, today.AddDays(daysAhead), completed: false, withDecoy: true);

        var result = await BuildService(core, series.Language, now)
            .UpdateFromCalendar(Reply(series, occ), ComplianceFutureTaskGuard.ComplianceSource);

        var compliance = await ReadCompliance(occ.ComplianceId);
        var sdkCase = await ReadCase(occ.SdkCaseId);
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Is.EqualTo("FutureTaskCannotBeCompleted"));
            Assert.That(compliance.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
            Assert.That(sdkCase.Status, Is.EqualTo(OpenCaseStatus));
        });
    }

    /// <summary>Also pins that the flag on a legitimate request changes nothing.</summary>
    [TestCase(0, TestName = "UpdateFromCalendar_CompliancePageSource_Today_Completes")]
    [TestCase(-1, TestName = "UpdateFromCalendar_CompliancePageSource_Yesterday_Completes")]
    public async Task UpdateFromCalendar_CompliancePageSource_TodayOrPast_Completes(int daysAhead)
    {
        var core = await SharedCore();
        var (now, today) = PinnedNow();
        var series = await SeedSeries($"ufc-past-{daysAhead}");
        var occ = await SeedOccurrence(series, today.AddDays(daysAhead), completed: false, withDecoy: true);

        var result = await BuildService(core, series.Language, now)
            .UpdateFromCalendar(Reply(series, occ), ComplianceFutureTaskGuard.ComplianceSource);

        var compliance = await ReadCompliance(occ.ComplianceId);
        var sdkCase = await ReadCase(occ.SdkCaseId);
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(compliance.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
            Assert.That(sdkCase.Status, Is.EqualTo(CompletedStatus));
        });
    }

    /// <summary>
    /// The CALENDAR keeps its early completion: no source (or any other source) completes a
    /// future occurrence exactly as before #1300.
    /// </summary>
    [TestCase(null, TestName = "UpdateFromCalendar_NoSource_FutureTask_StillCompletes_CalendarEarlyCompletion")]
    [TestCase("calendar", TestName = "UpdateFromCalendar_OtherSource_FutureTask_StillCompletes")]
    public async Task UpdateFromCalendar_WithoutCompliancePageSource_FutureTask_Completes(string? source)
    {
        var core = await SharedCore();
        var (now, today) = PinnedNow();
        var series = await SeedSeries($"ufc-calendar-{source ?? "none"}");
        var occ = await SeedOccurrence(series, today.AddDays(5), completed: false, withDecoy: true);

        var result = await BuildService(core, series.Language, now).UpdateFromCalendar(Reply(series, occ), source!);

        var sdkCase = await ReadCase(occ.SdkCaseId);
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(sdkCase.Status, Is.EqualTo(CompletedStatus));
        });
    }

    /// <summary>The flag never RELAXES another pre-flight check: an unknown site still fails.</summary>
    [Test]
    public async Task UpdateFromCalendar_CompliancePageSource_DoesNotRelaxOtherChecks()
    {
        var core = await SharedCore();
        var (now, today) = PinnedNow();
        var series = await SeedSeries("ufc-no-relax");
        var occ = await SeedOccurrence(series, today, completed: false, withDecoy: true);

        var result = await BuildService(core, series.Language, now)
            .UpdateFromCalendar(Reply(series, occ, siteId: int.MaxValue), ComplianceFutureTaskGuard.ComplianceSource);

        var compliance = await ReadCompliance(occ.ComplianceId);
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Is.EqualTo("SiteNotFound"));
            Assert.That(compliance.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
        });
    }

    [TestCase("2026-01-15T22:59:00Z", false, TestName = "UpdateFromCalendar_Boundary_Cet_2359_TaskTomorrow_IsRejected")]
    [TestCase("2026-01-15T23:01:00Z", true, TestName = "UpdateFromCalendar_Boundary_Cet_0001_TaskToday_Completes")]
    [TestCase("2026-07-15T21:59:00Z", false, TestName = "UpdateFromCalendar_Boundary_Cest_2359_TaskTomorrow_IsRejected")]
    [TestCase("2026-07-15T22:01:00Z", true, TestName = "UpdateFromCalendar_Boundary_Cest_0001_TaskToday_Completes")]
    public async Task UpdateFromCalendar_CompliancePageSource_AtCopenhagenMidnight(string utcNow, bool expectCompleted)
    {
        var core = await SharedCore();
        var taskDate = Utc(utcNow).Date.AddDays(1); // the 16th
        var series = await SeedSeries($"ufc-boundary-{utcNow}");
        var occ = await SeedOccurrence(series, taskDate, completed: false, withDecoy: true);

        var result = await BuildService(core, series.Language, Utc(utcNow))
            .UpdateFromCalendar(Reply(series, occ), ComplianceFutureTaskGuard.ComplianceSource);

        Assert.That(result.Success, Is.EqualTo(expectCompleted), result.Message);
    }

    // The compliance case page (legacy /compliances table + task tracker) completes through
    // PUT compliances/cases → Update.

    [Test]
    public async Task Update_CompliancePageSource_FutureTask_IsRejected_AndMutatesNothing()
    {
        var core = await SharedCore();
        var (now, today) = PinnedNow();
        var series = await SeedSeries("upd-future");
        var occ = await SeedOccurrence(series, today.AddDays(1), completed: false, withDecoy: true);

        var result = await BuildService(core, series.Language, now)
            .Update(Reply(series, occ), ComplianceFutureTaskGuard.ComplianceSource);

        var compliance = await ReadCompliance(occ.ComplianceId);
        var sdkCase = await ReadCase(occ.SdkCaseId);
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Is.EqualTo("FutureTaskCannotBeCompleted"));
            Assert.That(compliance.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
            Assert.That(sdkCase.Status, Is.EqualTo(OpenCaseStatus));
        });
    }

    [TestCase(0, TestName = "Update_CompliancePageSource_Today_Completes")]
    [TestCase(-3, TestName = "Update_CompliancePageSource_Past_Completes")]
    public async Task Update_CompliancePageSource_TodayOrPast_Completes(int daysAhead)
    {
        var core = await SharedCore();
        var (now, today) = PinnedNow();
        var series = await SeedSeries($"upd-past-{daysAhead}");
        var occ = await SeedOccurrence(series, today.AddDays(daysAhead), completed: false, withDecoy: true);

        var result = await BuildService(core, series.Language, now)
            .Update(Reply(series, occ), ComplianceFutureTaskGuard.ComplianceSource);

        var sdkCase = await ReadCase(occ.SdkCaseId);
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(sdkCase.Status, Is.EqualTo(CompletedStatus));
        });
    }

    /// <summary>Without the flag, Update behaves exactly as before #1300.</summary>
    [Test]
    public async Task Update_NoSource_FutureTask_StillCompletes()
    {
        var core = await SharedCore();
        var (now, today) = PinnedNow();
        var series = await SeedSeries("upd-nosource");
        var occ = await SeedOccurrence(series, today.AddDays(3), completed: false, withDecoy: true);

        var result = await BuildService(core, series.Language, now).Update(Reply(series, occ));

        Assert.That(result.Success, Is.True, result.Message);
    }
}
