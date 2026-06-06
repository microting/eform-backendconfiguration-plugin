/*
The MIT License (MIT)

Copyright (c) 2007 - 2026 Microting A/S

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction.
*/

namespace BackendConfiguration.Pn.Integration.Test;

using System.Globalization;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Infrastructure.Models.TaskWizard;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService;
using BackendConfiguration.Pn.Services.EventDeployService;
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
/// REPRODUCTION (not a fix) for the reported regression: editing a monthly
/// "1st Saturday" task's date to Friday (an earlier day in the SAME period)
/// with scope "thisAndFollowing" lands the routine on Thursday and/or copies it.
///
/// Scenario: monthly 1st Saturday, anchored Sat 6 Jun 2026. User edits the
/// July occurrence (Sat 4 Jul) → Fri 3 Jul, scope=thisAndFollowing.
/// July 2026: 1st Thu=Jul 2, 1st Fri=Jul 3, 1st Sat=Jul 4.
///
/// A UTC+2 browser serialises the picked "Fri 3 Jul" (local-midnight Date)
/// as 2026-07-02T22:00:00Z (toISOString). On a UTC server the BE reads that
/// StartDate as Thu 2 Jul (it does NOT apply the AssumeUniversal|AdjustToUniversal
/// normalisation it uses for OriginalDate), so the series re-anchors to the
/// 1st THURSDAY.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class CalendarThisAndFollowingMoveBackReproTests : TestBaseSetup
{
    private static string IsoUtc(DateTime d) =>
        DateTime.SpecifyKind(d, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
    private static string Key(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // The fixture shares one MariaDb container across its test methods and
    // TestBaseSetup only disposes the contexts between tests (no DB reset), so
    // seeded rows accumulate. GetTasksForWeek then trips on duplicate
    // dictionary keys once a second test runs. Wipe the tables each test
    // touches up-front for isolation, mirroring CalendarYearlyMoveTests.
    [SetUp]
    public async Task CleanState()
    {
        BackendConfigurationPnDbContext!.CalendarOccurrenceExceptionSites.RemoveRange(
            BackendConfigurationPnDbContext.CalendarOccurrenceExceptionSites);
        BackendConfigurationPnDbContext.CalendarOccurrenceExceptions.RemoveRange(
            BackendConfigurationPnDbContext.CalendarOccurrenceExceptions);
        BackendConfigurationPnDbContext.CalendarConfigurations.RemoveRange(
            BackendConfigurationPnDbContext.CalendarConfigurations);
        BackendConfigurationPnDbContext.Compliances.RemoveRange(
            BackendConfigurationPnDbContext.Compliances);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.AreaRulePlannings.RemoveRange(
            BackendConfigurationPnDbContext.AreaRulePlannings);
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

        ItemsPlanningPnDbContext!.Plannings.RemoveRange(ItemsPlanningPnDbContext.Plannings);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        MicrotingDbContext!.Cases.RemoveRange(MicrotingDbContext.Cases);
        await MicrotingDbContext.SaveChangesAsync();
    }

    private sealed record Seed(int PropertyId, int ArpId, int SiteId, int PlanningId, int AreaId);

    // Seeds a monthly 1st-Saturday series anchored on Sat 6 Jun 2026.
    private async Task<Seed> SeedFirstSaturdaySeries()
    {
        var language = await MicrotingDbContext!.Languages.FirstAsync();
        var sdkSite = new Site
        {
            Name = $"taf-moveback-{Guid.NewGuid()}", MicrotingUid = 9911,
            LanguageId = language.Id, WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddAsync(sdkSite);
        await MicrotingDbContext.SaveChangesAsync();

        var area = new Area { Type = AreaTypesEnum.Type1, ItemPlanningTagId = 0, WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1 };
        await BackendConfigurationPnDbContext!.Areas.AddAsync(area);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var property = new Property { Name = $"TafMoveBack-{Guid.NewGuid()}", ItemPlanningTagId = 0, WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1 };
        await BackendConfigurationPnDbContext.Properties.AddAsync(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var areaRule = new AreaRule { AreaId = area.Id, PropertyId = property.Id, EformId = 0, WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1 };
        await BackendConfigurationPnDbContext.AreaRules.AddAsync(areaRule);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        await BackendConfigurationPnDbContext.AreaRuleTranslations.AddAsync(new AreaRuleTranslation
        { AreaRuleId = areaRule.Id, LanguageId = language.Id, Name = "Tank 4", WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1 });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var startDate = new DateTime(2026, 6, 6, 0, 0, 0, DateTimeKind.Utc); // 1st Saturday of June 2026
        var planning = new Planning
        {
            Enabled = true, RepeatEvery = 1, RepeatType = RepeatType.Month, StartDate = startDate,
            DayOfMonth = 0, RelatedEFormId = 0, WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = property.Id, AreaId = area.Id, ItemPlanningId = planning.Id,
            StartDate = startDate, Status = true, RepeatType = 3, RepeatEvery = 1,
            RepeatOrdinalWeek = 1, DayOfWeek = 6, // 1st Saturday (Sat = 6)
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(arp);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        await BackendConfigurationPnDbContext.CalendarConfigurations.AddAsync(new CalendarConfiguration
        { AreaRulePlanningId = arp.Id, StartHour = 9.0, Duration = 1.0, WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1 });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        return new Seed(property.Id, arp.Id, sdkSite.Id, planning.Id, area.Id);
    }

    private CalendarTaskUpdateRequestModel BuildEdit(Seed s, DateTime startDateUtc, string scope = "thisAndFollowing") => new()
    {
        Id = s.ArpId, Scope = scope,
        OriginalDate = "2026-07-04T00:00:00Z", // clicked occurrence = 1st Saturday of July
        StartDate = startDateUtc,
        StartHour = 9.0, Duration = 1.0, Status = 1,
        RepeatType = 3, RepeatEvery = 1, RepeatOrdinalWeek = 1, DayOfMonth = 0,
        ComplianceEnabled = false, PropertyId = s.PropertyId, EformId = 0,
        Sites = [s.SiteId], TagIds = [], Translates = []
    };

    private async Task<List<string>> JulyWeekTiles(BackendConfigurationCalendarService svc, int propertyId)
    {
        var weekStart = new DateTime(2026, 6, 29, 0, 0, 0, DateTimeKind.Utc); // Mon containing Jul 2-4
        var res = await svc.GetTasksForWeek(new CalendarTaskRequestModel
        {
            PropertyId = propertyId, WeekStart = IsoUtc(weekStart),
            WeekEnd = IsoUtc(weekStart.AddDays(6).AddHours(23).AddMinutes(59)),
            ActionableOnly = false, BoardIds = [], TagNames = [], SiteIds = []
        });
        Assert.That(res.Success, Is.True, res.Message);
        return res.Model!.Select(t => $"{t.TaskDate}{(t.IsFromCompliance ? "(compliance)" : "")}").OrderBy(x => x).ToList();
    }

    // ---- Repro 1: tz-shifted StartDate (what a UTC+2 browser sends) → wrong weekday ----
    [Test]
    public async Task Repro_ThisAndFollowing_TzShiftedFriday_LandsOnThursday()
    {
        var core = await GetCore();
        var s = await SeedFirstSaturdaySeries();
        var svc = BuildService(core);

        // 2026-07-02T22:00:00Z == Fri 3 Jul local-midnight in UTC+2 (Denmark).
        await svc.UpdateTask(BuildEdit(s, new DateTime(2026, 7, 2, 22, 0, 0, DateTimeKind.Utc)));

        var tiles = await JulyWeekTiles(svc, s.PropertyId);
        TestContext.WriteLine("Rendered July-week tiles (tz-shifted): " + string.Join(", ", tiles));

        // EXPECTED (correct) behaviour: a single tile on Friday 3 Jul.
        Assert.That(tiles, Does.Contain(Key(new DateTime(2026, 7, 3))),
            "routine should move to Friday 3 Jul");
        Assert.That(tiles, Does.Not.Contain(Key(new DateTime(2026, 7, 2))),
            "routine must NOT land on Thursday 2 Jul");
    }

    // ---- Control: tz-stable StartDate (Fri 3 Jul 00:00Z) → correct weekday ----
    [Test]
    public async Task Control_ThisAndFollowing_TzStableFriday_LandsOnFriday()
    {
        var core = await GetCore();
        var s = await SeedFirstSaturdaySeries();
        var svc = BuildService(core);

        await svc.UpdateTask(BuildEdit(s, new DateTime(2026, 7, 3, 0, 0, 0, DateTimeKind.Utc)));

        var tiles = await JulyWeekTiles(svc, s.PropertyId);
        TestContext.WriteLine("Rendered July-week tiles (tz-stable): " + string.Join(", ", tiles));

        Assert.That(tiles, Does.Contain(Key(new DateTime(2026, 7, 3))), "Friday 3 Jul present");
        Assert.That(tiles, Does.Not.Contain(Key(new DateTime(2026, 7, 2))), "no Thursday");
        Assert.That(tiles.Count(t => t.StartsWith("2026-07")), Is.EqualTo(1),
            "exactly one July tile (no copy)");
    }

    // ---- Repro 2: deployed (not completed) compliance at Jul 4 → duplicate? ----
    [Test]
    public async Task Repro_ThisAndFollowing_WithDeployedComplianceAtJul4_NoDuplicate()
    {
        var core = await GetCore();
        var s = await SeedFirstSaturdaySeries();

        // A deployed-but-not-completed occurrence already exists at Sat 4 Jul.
        var sdkCase = new Microting.eForm.Infrastructure.Data.Entities.Case
        { SiteId = s.SiteId, Status = 66, WorkflowState = Constants.WorkflowStates.Created };
        await MicrotingDbContext!.Cases.AddAsync(sdkCase);
        await MicrotingDbContext.SaveChangesAsync();
        await BackendConfigurationPnDbContext!.Compliances.AddAsync(new Compliance
        {
            PlanningId = s.PlanningId, PropertyId = s.PropertyId, AreaId = s.AreaId,
            Deadline = new DateTime(2026, 7, 4, 0, 0, 0, DateTimeKind.Utc), StartDate = new DateTime(2026, 6, 4, 0, 0, 0, DateTimeKind.Utc),
            MicrotingSdkCaseId = sdkCase.Id, MicrotingSdkeFormId = 0, WorkflowState = Constants.WorkflowStates.Created
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var svc = BuildService(core);
        // Use the tz-stable Friday to isolate the duplicate from the weekday bug.
        await svc.UpdateTask(BuildEdit(s, new DateTime(2026, 7, 3, 0, 0, 0, DateTimeKind.Utc)));

        var tiles = await JulyWeekTiles(svc, s.PropertyId);
        TestContext.WriteLine("Rendered July-week tiles (compliance@Jul4): " + string.Join(", ", tiles));

        Assert.That(tiles.Count(t => t.StartsWith("2026-07")), Is.EqualTo(1),
            "exactly one July tile after move — original Sat 4 Jul must not remain as a copy");
    }

    // ---- RC1 parity: the tz-shift bug affects every entry point that reads
    // StartDate by wall-clock, not just thisAndFollowing. These guard the
    // "this" and "all" scopes against the same off-by-one. (CreateTask shares
    // the same NormalizeStartDateToLocalDay entry-point fix but cannot be
    // rendered here — its ARP is produced by the task wizard, which is mocked.)

    // scope="this" records the move as a per-occurrence exception (NewDate =
    // StartDate.Date). A tz-shifted StartDate would set NewDate to Thursday.
    [Test]
    public async Task Repro_ThisOccurrence_TzShiftedFriday_LandsOnFriday()
    {
        var core = await GetCore();
        var s = await SeedFirstSaturdaySeries();
        var svc = BuildService(core);

        await svc.UpdateTask(BuildEdit(s, new DateTime(2026, 7, 2, 22, 0, 0, DateTimeKind.Utc), "this"));

        var tiles = await JulyWeekTiles(svc, s.PropertyId);
        TestContext.WriteLine("Rendered July-week tiles (this, tz-shifted): " + string.Join(", ", tiles));

        Assert.That(tiles, Does.Contain(Key(new DateTime(2026, 7, 3))), "occurrence should move to Friday 3 Jul");
        Assert.That(tiles, Does.Not.Contain(Key(new DateTime(2026, 7, 2))), "must NOT land on Thursday 2 Jul");
        Assert.That(tiles.Count(t => t.StartsWith("2026-07")), Is.EqualTo(1), "exactly one July tile");
    }

    // scope="all" re-anchors the whole series; arp.DayOfWeek is derived from
    // StartDate.DayOfWeek, so a tz-shifted StartDate re-patterns to "1st
    // Thursday" instead of "1st Friday".
    [Test]
    public async Task Repro_AllScope_TzShiftedFriday_RepatternsToFriday()
    {
        var core = await GetCore();
        var s = await SeedFirstSaturdaySeries();
        var svc = BuildService(core);

        await svc.UpdateTask(BuildEdit(s, new DateTime(2026, 7, 2, 22, 0, 0, DateTimeKind.Utc), "all"));

        var tiles = await JulyWeekTiles(svc, s.PropertyId);
        TestContext.WriteLine("Rendered July-week tiles (all, tz-shifted): " + string.Join(", ", tiles));

        Assert.That(tiles, Does.Contain(Key(new DateTime(2026, 7, 3))), "series should re-pattern to 1st Friday 3 Jul");
        Assert.That(tiles, Does.Not.Contain(Key(new DateTime(2026, 7, 2))), "must NOT re-pattern to 1st Thursday 2 Jul");
    }

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
        taskWizardService.UpdateTask(Arg.Any<TaskWizardCreateModel>()).Returns(Task.FromResult(new OperationResult(true)));
        return new BackendConfigurationCalendarService(
            new BackendConfigurationLocalizationService(), userService,
            BackendConfigurationPnDbContext!, coreHelper, Substitute.For<IEventDeployService>(),
            ItemsPlanningPnDbContext!, taskWizardService,
            NullLogger<BackendConfigurationCalendarService>.Instance);
    }
}
