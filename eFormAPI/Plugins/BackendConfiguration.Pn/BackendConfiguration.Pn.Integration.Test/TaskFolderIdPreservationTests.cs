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
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eForm.Infrastructure.Models;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using NSubstitute;

/// <summary>
/// Issue #1135 — a null <c>FolderId</c> on the update payload must mean
/// "unchanged", never "clear the folder" and never a crash.
///
/// <para>
/// The task-list edit modal hard-coded <c>folderId: null</c>, and since #1239
/// the calendar container deliberately sends null when its Logbøger lookup
/// fails (rather than reusing the PREVIOUS property's folder, which silently
/// refiled the task across properties). Both entity columns
/// (<c>AreaRulePlanning.FolderId</c>, <c>AreaRule.FolderId</c>) are
/// non-nullable <c>int</c>, so the wizard's unconditional <c>(int)</c> unbox
/// threw "Nullable object must have a value" and every save failed.
/// </para>
///
/// <para>
/// Two independent defences are covered here:
/// the wizard keeps the current folder (the tests running the REAL
/// <see cref="BackendConfigurationTaskWizardService"/>), and the calendar
/// service resolves the folder BEFORE dispatching on scope — which is what
/// stops <c>thisAndFollowing</c> from failing half-applied, its past-occurrence
/// backfill and series re-anchor having already committed by the time the
/// wizard is called.
/// </para>
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class TaskFolderIdPreservationTests : TestBaseSetup
{
    private IUserService _userService;
    private IBackendConfigurationTaskWizardService _taskWizardService;
    private BackendConfigurationCalendarService _calendarService;

    [SetUp]
    public async Task SetupServices()
    {
        // FK-safe cleanup so each test starts fresh (mirrors
        // CalendarUpdateTaskScopeTests, which shares this fixture's shape).
        BackendConfigurationPnDbContext!.CalendarOccurrenceExceptionSites.RemoveRange(
            BackendConfigurationPnDbContext.CalendarOccurrenceExceptionSites);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.CalendarOccurrenceExceptions.RemoveRange(
            BackendConfigurationPnDbContext.CalendarOccurrenceExceptions);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.CalendarConfigurations.RemoveRange(
            BackendConfigurationPnDbContext.CalendarConfigurations);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.PlanningSites.RemoveRange(
            BackendConfigurationPnDbContext.PlanningSites);
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

        BackendConfigurationPnDbContext.Areas.RemoveRange(BackendConfigurationPnDbContext.Areas);
        BackendConfigurationPnDbContext.Properties.RemoveRange(BackendConfigurationPnDbContext.Properties);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        ItemsPlanningPnDbContext!.Plannings.RemoveRange(ItemsPlanningPnDbContext.Plannings);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        _userService = Substitute.For<IUserService>();
        _userService.UserId.Returns(1);
        _userService.GetCurrentUserLanguage()
            .Returns(Task.FromResult(new Language { Id = 1, Name = "English", LanguageCode = "en-US" }));

        // Mocked for the calendar-level tests: what they assert is the model the
        // calendar service HANDS the wizard, plus what it wrote before doing so.
        _taskWizardService = Substitute.For<IBackendConfigurationTaskWizardService>();
        _taskWizardService.UpdateTask(Arg.Any<TaskWizardCreateModel>())
            .Returns(Task.FromResult(new OperationResult(true)));

        _calendarService = new BackendConfigurationCalendarService(
            new BackendConfigurationLocalizationService(),
            _userService,
            BackendConfigurationPnDbContext,
            null,
            Substitute.For<IEventDeployService>(),
            ItemsPlanningPnDbContext,
            _taskWizardService,
            Substitute.For<ICalendarAssignmentReconciliationService>(),
            Substitute.For<ICalendarChangeNotifier>(),
            NullLogger<BackendConfigurationCalendarService>.Instance,
            Substitute.For<ICalendarOccurrenceRetractionService>(),
            Substitute.For<ICalendarPastSeriesBackfillService>(),
            new WorkerTagMembershipService(null));
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
    /// CalendarConfiguration for a weekly series. <paramref name="arpFolderId"/>
    /// and <paramref name="areaRuleFolderId"/> are set independently so the
    /// "planning row has no folder, fall back to the AreaRule" case can be
    /// reproduced.
    /// </summary>
    private async Task<(int ArpId, int PlanningId)> SeedWeeklyTask(
        DateTime startDate, int arpFolderId, int areaRuleFolderId, bool active = true)
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
            Name = $"FolderProp-{Guid.NewGuid()}", ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.Properties.AddAsync(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var areaRule = new AreaRule
        {
            AreaId = area.Id, PropertyId = property.Id, EformId = 0, FolderId = areaRuleFolderId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRules.AddAsync(areaRule);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var areaRuleTranslation = new AreaRuleTranslation
        {
            AreaRuleId = areaRule.Id, LanguageId = 1, Name = "Folder preservation task",
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRuleTranslations.AddAsync(areaRuleTranslation);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var planning = new Planning
        {
            Enabled = active, RepeatEvery = 1,
            RepeatType = Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType.Week,
            StartDate = startDate, SdkFolderId = arpFolderId > 0 ? arpFolderId : areaRuleFolderId,
            Description = "Original description",
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = property.Id, AreaId = area.Id,
            ItemPlanningId = planning.Id, StartDate = startDate, Status = active,
            RepeatType = 2, RepeatEvery = 1, FolderId = arpFolderId,
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

        return (arp.Id, planning.Id);
    }

    /// <summary>
    /// The exact payload the task-list edit modal produced: every field filled
    /// in, <c>FolderId</c> absent.
    /// </summary>
    private static CalendarTaskUpdateRequestModel BuildEditWithoutFolder(
        int arpId, DateTime startDate, string scope, DateTime originalDate, int? folderId = null)
    {
        return new CalendarTaskUpdateRequestModel
        {
            Id = arpId,
            Scope = scope,
            OriginalDate = originalDate.ToString("yyyy-MM-dd") + "T00:00:00Z",
            StartDate = DateTime.SpecifyKind(startDate, DateTimeKind.Utc),
            StartHour = 11.0,
            Duration = 2.0,
            Status = 1,
            RepeatType = 2,
            RepeatEvery = 1,
            ComplianceEnabled = false,
            PropertyId = 0,
            EformId = 0,
            // #1135's payload: the one field the task-list page never filled in.
            // The batch actions send 0 instead of null for the same "unset"
            // state — see BuildUpdateModel, which copies a non-nullable int.
            FolderId = folderId,
            Sites = [101],
            TagIds = [],
            BoardId = 42,
            Color = "#abcdef",
            DescriptionHtml = "Edited description",
            Translates = [new CommonTranslationsModel { LanguageId = 1, Name = "Edited Title" }]
        };
    }

    // ------------------------------------------------------------------
    // Calendar service — the folder is resolved before the scope dispatch.
    // ------------------------------------------------------------------

    [Test]
    public async Task UpdateTask_ScopeAll_NullFolderId_HandsTheCurrentFolderToTheWizard()
    {
        const int existingFolderId = 1289;
        var nextMonday = GetNextMonday();
        var seriesStart = DateTime.SpecifyKind(nextMonday, DateTimeKind.Utc);
        var (arpId, _) = await SeedWeeklyTask(seriesStart, existingFolderId, existingFolderId);

        var result = await _calendarService.UpdateTask(
            BuildEditWithoutFolder(arpId, nextMonday, "all", nextMonday));

        Assert.That(result.Success, Is.True, result.Message);
        await _taskWizardService.Received(1).UpdateTask(
            Arg.Is<TaskWizardCreateModel>(m => m.FolderId == existingFolderId));
    }

    [Test]
    public async Task UpdateTask_ScopeAll_NullFolderId_FallsBackToTheAreaRuleFolder()
    {
        const int areaRuleFolderId = 1290;
        var nextMonday = GetNextMonday();
        var seriesStart = DateTime.SpecifyKind(nextMonday, DateTimeKind.Utc);
        // Older rows left AreaRulePlanning.FolderId at its 0 default while the
        // AreaRule carried the real folder.
        var (arpId, _) = await SeedWeeklyTask(seriesStart, 0, areaRuleFolderId);

        var result = await _calendarService.UpdateTask(
            BuildEditWithoutFolder(arpId, nextMonday, "all", nextMonday));

        Assert.That(result.Success, Is.True, result.Message);
        await _taskWizardService.Received(1).UpdateTask(
            Arg.Is<TaskWizardCreateModel>(m => m.FolderId == areaRuleFolderId));
    }

    /// <summary>
    /// The task-list BATCH actions (Assign, Reassign, ChangeEform,
    /// ChangeStartDate) go through
    /// <c>BackendConfigurationTaskListService.BuildUpdateModel</c>, which copies
    /// <c>FolderId = arp.FolderId</c> — a non-nullable <c>int</c>. On a legacy
    /// row whose planning folder was never set, that sends 0, not null.
    ///
    /// Treating 0 as a real id would write it over <c>AreaRule.FolderId</c>,
    /// destroying the only surviving folder on exactly the row the fallback
    /// exists for, and leaving it unhealable — both fallback candidates would
    /// then be 0 and every later edit refused. CreateTask has always read 0 as
    /// unset (<c>resolvedFolderId is null or 0</c>); update now matches.
    /// </summary>
    [Test]
    public async Task UpdateTask_BatchShapedZeroFolderId_KeepsTheAreaRuleFolder()
    {
        const int areaRuleFolderId = 1293;
        var nextMonday = GetNextMonday();
        var seriesStart = DateTime.SpecifyKind(nextMonday, DateTimeKind.Utc);
        // The legacy shape: nothing on the planning row, the real id on the rule.
        var (arpId, _) = await SeedWeeklyTask(seriesStart, 0, areaRuleFolderId);

        var result = await _calendarService.UpdateTask(
            BuildEditWithoutFolder(arpId, nextMonday, "all", nextMonday, folderId: 0));

        Assert.That(result.Success, Is.True, result.Message);
        await _taskWizardService.Received(1).UpdateTask(
            Arg.Is<TaskWizardCreateModel>(m => m.FolderId == areaRuleFolderId));
    }

    [Test]
    public async Task UpdateTask_ScopeThisAndFollowing_NullFolderId_HandsTheCurrentFolderToTheWizard()
    {
        const int existingFolderId = 1291;
        var nextMonday = GetNextMonday();
        // Four occurrences already behind the edited one, so the backfill runs.
        var seriesStart = DateTime.SpecifyKind(nextMonday.AddDays(-28), DateTimeKind.Utc);
        var (arpId, _) = await SeedWeeklyTask(seriesStart, existingFolderId, existingFolderId);

        var result = await _calendarService.UpdateTask(
            BuildEditWithoutFolder(arpId, nextMonday, "thisAndFollowing", nextMonday));

        Assert.That(result.Success, Is.True, result.Message);
        await _taskWizardService.Received(1).UpdateTask(
            Arg.Is<TaskWizardCreateModel>(m => m.FolderId == existingFolderId));

        // The scope's own work still happened — the fix resolves the folder, it
        // does not skip the backfill.
        var anchors = await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions
            .Where(x => x.AreaRulePlanningId == arpId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync();
        Assert.That(anchors, Has.Count.EqualTo(4));
    }

    [Test]
    public async Task UpdateTask_ScopeThis_NullFolderId_SucceedsAndLeavesTheFolderUntouched()
    {
        const int existingFolderId = 1292;
        var nextMonday = GetNextMonday();
        var seriesStart = DateTime.SpecifyKind(nextMonday, DateTimeKind.Utc);
        var (arpId, _) = await SeedWeeklyTask(seriesStart, existingFolderId, existingFolderId);

        var result = await _calendarService.UpdateTask(
            BuildEditWithoutFolder(arpId, nextMonday, "this", nextMonday));

        Assert.That(result.Success, Is.True, result.Message);
        // UpdateTaskThisOccurrence never reads FolderId; the occurrence override
        // is written and the series folder is left exactly as it was.
        await _taskWizardService.DidNotReceive().UpdateTask(Arg.Any<TaskWizardCreateModel>());
        var exception = await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions
            .Where(x => x.AreaRulePlanningId == arpId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .SingleAsync();
        Assert.That(exception.OriginalDate.Date, Is.EqualTo(nextMonday.Date));

        var arp = await BackendConfigurationPnDbContext.AreaRulePlannings
            .AsNoTracking().Include(x => x.AreaRule).FirstAsync(x => x.Id == arpId);
        Assert.Multiple(() =>
        {
            Assert.That(arp.FolderId, Is.EqualTo(existingFolderId));
            Assert.That(arp.AreaRule.FolderId, Is.EqualTo(existingFolderId));
        });
    }

    [Test]
    public async Task UpdateTask_ScopeThisAndFollowing_UnresolvableFolder_RefusesBeforeAnythingIsWritten()
    {
        var nextMonday = GetNextMonday();
        var seriesStart = DateTime.SpecifyKind(nextMonday.AddDays(-28), DateTimeKind.Utc);
        // Neither row carries a folder, so null cannot be resolved to anything.
        var (arpId, planningId) = await SeedWeeklyTask(seriesStart, 0, 0);

        // A DATE change, which is what makes thisAndFollowing re-anchor the
        // series on top of writing the past-occurrence backfill.
        var movedTo = nextMonday.AddDays(1);
        var result = await _calendarService.UpdateTask(
            BuildEditWithoutFolder(arpId, movedTo, "thisAndFollowing", nextMonday));

        Assert.That(result.Success, Is.False);

        // The point of resolving the folder before the scope dispatch: a refusal
        // must leave the series exactly as it was, not re-anchored with the task
        // itself un-updated.
        var anchors = await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions
            .Where(x => x.AreaRulePlanningId == arpId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync();
        var arp = await BackendConfigurationPnDbContext.AreaRulePlannings
            .AsNoTracking().FirstAsync(x => x.Id == arpId);
        var planning = await ItemsPlanningPnDbContext!.Plannings
            .AsNoTracking().FirstAsync(x => x.Id == planningId);

        var calConfig = await BackendConfigurationPnDbContext.CalendarConfigurations
            .AsNoTracking().FirstAsync(x => x.AreaRulePlanningId == arpId);

        Assert.Multiple(() =>
        {
            Assert.That(anchors, Is.Empty, "no past-occurrence backfill may have been committed");
            Assert.That(arp.StartDate!.Value.Date, Is.EqualTo(seriesStart.Date),
                "the series must not have been re-anchored");
            Assert.That(planning.StartDate.Date, Is.EqualTo(seriesStart.Date),
                "the items-planning anchor must not have been moved either");
            // The payload carries 11.0/2.0; the seed is 9.0/1.0.
            Assert.That(calConfig.StartHour, Is.EqualTo(9.0), "the edit's own fields must not have landed");
            Assert.That(calConfig.Duration, Is.EqualTo(1.0));
        });
        await _taskWizardService.DidNotReceive().UpdateTask(Arg.Any<TaskWizardCreateModel>());
    }

    // ------------------------------------------------------------------
    // Task wizard — the defence that also covers direct wizard callers.
    // ------------------------------------------------------------------

    private async Task<BackendConfigurationTaskWizardService> BuildRealWizardServiceAsync()
    {
        var core = await GetCore();
        var language = await MicrotingDbContext!.Languages.FirstAsync();

        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        userService.GetCurrentUserLanguage().Returns(Task.FromResult(language));

        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));

        return new BackendConfigurationTaskWizardService(
            new BackendConfigurationLocalizationService(),
            userService,
            BackendConfigurationPnDbContext!,
            coreHelper,
            ItemsPlanningPnDbContext!,
            Substitute.For<IEventDeployService>(),
            Substitute.For<ICalendarOccurrenceRetractionService>(),
            NullLogger<BackendConfigurationTaskWizardService>.Instance);
    }

    /// <summary>
    /// ORDERING CONSTRAINT: a Core must already have been obtained (i.e.
    /// <see cref="BuildRealWizardServiceAsync"/> called) before this runs.
    ///
    /// SQL/420_SDK.sql drops and recreates <c>Folders</c> with the column set
    /// of the migration its __EFMigrationsHistory stops at, which predates
    /// <c>AddChildrenProhibitedToFolders</c>. The entity model maps
    /// <c>ChildrenProhibited</c>, so an EF insert before
    /// <c>Core.StartSqlOnly</c> has run <c>Database.Migrate()</c> emits it
    /// against a table that has no such column and dies with
    /// "Unknown column". The same holds for any SDK entity whose columns
    /// arrive after the dump.
    /// </summary>
    private async Task<Folder> SeedSdkFolderAsync(string name, int microtingUid)
    {
        var folder = new Folder
        {
            Name = name, MicrotingUid = microtingUid,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext!.Folders.AddAsync(folder);
        await MicrotingDbContext.SaveChangesAsync();
        return folder;
    }

    [Test]
    public async Task WizardUpdateTask_NullFolderId_KeepsTheFolderOnBothRows()
    {
        // Core FIRST — see SeedSdkFolderAsync. Never seed an SDK row before it.
        var wizardService = await BuildRealWizardServiceAsync();
        var folder = await SeedSdkFolderAsync("folder-preservation-inactive", 810_001);

        // Inactive → inactive: the branch with no deploy, so the test measures
        // only the AreaRulePlanning/AreaRule writes the unbox used to sit on.
        var startDate = DateTime.UtcNow.Date.AddDays(30);
        var (arpId, _) = await SeedWeeklyTask(startDate, folder.Id, folder.Id, active: false);

        var result = await wizardService.UpdateTask(new TaskWizardCreateModel
        {
            Id = arpId,
            PropertyId = 0,
            FolderId = null,
            EformId = 0,
            StartDate = startDate,
            RepeatType = BackendConfiguration.Pn.Infrastructure.Enums.RepeatType.Week,
            RepeatEvery = 1,
            Status = BackendConfiguration.Pn.Infrastructure.Enums.TaskWizardStatuses.NotActive,
            Sites = [],
            TagIds = [],
            Translates = [],
            ComplianceEnabled = false
        });

        Assert.That(result.Success, Is.True, result.Message);

        var arp = await BackendConfigurationPnDbContext!.AreaRulePlannings
            .AsNoTracking().Include(x => x.AreaRule).FirstAsync(x => x.Id == arpId);
        Assert.Multiple(() =>
        {
            Assert.That(arp.FolderId, Is.EqualTo(folder.Id),
                "a null FolderId means unchanged, not cleared");
            Assert.That(arp.AreaRule.FolderId, Is.EqualTo(folder.Id),
                "the second unbox site, on the AreaRule, must keep the folder too");
        });
    }

    /// <summary>
    /// The wizard's own half of the 0-is-unset rule, for the direct callers the
    /// calendar pre-flight does not sit in front of.
    /// </summary>
    [Test]
    public async Task WizardUpdateTask_ZeroFolderId_KeepsTheAreaRuleFolder()
    {
        // Core FIRST — see SeedSdkFolderAsync. Never seed an SDK row before it.
        var wizardService = await BuildRealWizardServiceAsync();
        var folder = await SeedSdkFolderAsync("folder-preservation-zero", 810_003);

        var startDate = DateTime.UtcNow.Date.AddDays(30);
        // Planning row has no folder; the rule carries the real one.
        var (arpId, _) = await SeedWeeklyTask(startDate, 0, folder.Id, active: false);

        var result = await wizardService.UpdateTask(new TaskWizardCreateModel
        {
            Id = arpId,
            PropertyId = 0,
            // What BuildUpdateModel sends for this row, since arp.FolderId is int.
            FolderId = 0,
            EformId = 0,
            StartDate = startDate,
            RepeatType = BackendConfiguration.Pn.Infrastructure.Enums.RepeatType.Week,
            RepeatEvery = 1,
            Status = BackendConfiguration.Pn.Infrastructure.Enums.TaskWizardStatuses.NotActive,
            Sites = [],
            TagIds = [],
            Translates = [],
            ComplianceEnabled = false
        });

        Assert.That(result.Success, Is.True, result.Message);

        var arp = await BackendConfigurationPnDbContext!.AreaRulePlannings
            .AsNoTracking().Include(x => x.AreaRule).FirstAsync(x => x.Id == arpId);
        Assert.Multiple(() =>
        {
            Assert.That(arp.AreaRule.FolderId, Is.EqualTo(folder.Id),
                "a 0 on the wire must not overwrite the only surviving folder id");
            Assert.That(arp.FolderId, Is.EqualTo(folder.Id),
                "and the planning row is healed onto it rather than left at 0");
        });
    }

    [Test]
    public async Task WizardUpdateTask_NullFolderId_KeepsTheSdkFolderOnTheLinkedPlanning()
    {
        // Core FIRST — see SeedSdkFolderAsync. Never seed an SDK row before it.
        var wizardService = await BuildRealWizardServiceAsync();
        var folder = await SeedSdkFolderAsync("folder-preservation-active", 810_002);

        // Active → active. Nothing is (re)deployed because the assignee set is
        // unchanged, but Planning.SdkFolderId IS rewritten from updateModel —
        // a null there would blank the folder the scheduler files cases into.
        var startDate = DateTime.UtcNow.Date.AddDays(30);
        var (arpId, planningId) = await SeedWeeklyTask(startDate, folder.Id, folder.Id);

        var arpBefore = await BackendConfigurationPnDbContext!.AreaRulePlannings
            .FirstAsync(x => x.Id == arpId);
        await BackendConfigurationPnDbContext.PlanningSites.AddAsync(
            new Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.PlanningSite
            {
                SiteId = 101, AreaRulePlanningsId = arpBefore.Id, AreaId = arpBefore.AreaId,
                AreaRuleId = arpBefore.AreaRuleId,
                WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
            });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var result = await wizardService.UpdateTask(new TaskWizardCreateModel
        {
            Id = arpId,
            PropertyId = 0,
            FolderId = null,
            EformId = 0,
            StartDate = startDate,
            RepeatType = BackendConfiguration.Pn.Infrastructure.Enums.RepeatType.Week,
            RepeatEvery = 1,
            Status = BackendConfiguration.Pn.Infrastructure.Enums.TaskWizardStatuses.Active,
            // The site already assigned, so sitesToAdd is empty and no deploy runs.
            Sites = [101],
            TagIds = [],
            Translates = [],
            ComplianceEnabled = false
        });

        Assert.That(result.Success, Is.True, result.Message);

        var arp = await BackendConfigurationPnDbContext.AreaRulePlannings
            .AsNoTracking().Include(x => x.AreaRule).FirstAsync(x => x.Id == arpId);
        var planning = await ItemsPlanningPnDbContext!.Plannings
            .AsNoTracking().FirstAsync(x => x.Id == planningId);

        Assert.Multiple(() =>
        {
            Assert.That(arp.FolderId, Is.EqualTo(folder.Id));
            Assert.That(arp.AreaRule.FolderId, Is.EqualTo(folder.Id));
            Assert.That(planning.SdkFolderId, Is.EqualTo(folder.Id));
        });
    }

    [Test]
    public async Task WizardUpdateTask_NullFolderId_WithNoFolderAnywhere_IsRefused()
    {
        var wizardService = await BuildRealWizardServiceAsync();

        var startDate = DateTime.UtcNow.Date.AddDays(30);
        var (arpId, _) = await SeedWeeklyTask(startDate, 0, 0, active: false);

        var result = await wizardService.UpdateTask(new TaskWizardCreateModel
        {
            Id = arpId,
            PropertyId = 0,
            FolderId = null,
            EformId = 0,
            StartDate = startDate,
            RepeatType = BackendConfiguration.Pn.Infrastructure.Enums.RepeatType.Week,
            RepeatEvery = 1,
            Status = BackendConfiguration.Pn.Infrastructure.Enums.TaskWizardStatuses.NotActive,
            Sites = [],
            TagIds = [],
            Translates = [],
            ComplianceEnabled = false
        });

        // Success alone does NOT discriminate: on the unfixed code the same
        // input reaches the `(int)updateModel.FolderId` unbox, throws, and
        // UpdateTask's own catch returns Success=false with
        // "ErrorWhileUpdatingTask". So assert WHICH refusal this is — the
        // deliberate FolderIsRequired guard, the same key CreateTask uses —
        // and that the guard returned before writing anything.
        var localizationService = new BackendConfigurationLocalizationService();
        Assert.Multiple(() =>
        {
            Assert.That(result.Message,
                Is.EqualTo(localizationService.GetString("FolderIsRequired")));
            Assert.That(result.Message,
                Is.Not.EqualTo(localizationService.GetString("ErrorWhileUpdatingTask")),
                "a crash-shaped failure would mean the unbox is still live");
        });

        var arp = await BackendConfigurationPnDbContext!.AreaRulePlannings
            .AsNoTracking().Include(x => x.AreaRule).FirstAsync(x => x.Id == arpId);
        Assert.Multiple(() =>
        {
            Assert.That(arp.FolderId, Is.Zero, "the refusal must not have half-written a folder");
            Assert.That(arp.AreaRule.FolderId, Is.Zero);
        });
    }

    /// <summary>
    /// The pre-flight runs for EVERY scope, but <c>UpdateTaskThisOccurrence</c>
    /// never reads <c>FolderId</c> — so on a row with no resolvable folder a
    /// single-occurrence edit that used to succeed is now refused.
    ///
    /// That is a deliberate choice (a task whose folder cannot be resolved
    /// fails identically on every scope, rather than one scope silently
    /// diverging), but it IS a functional narrowing on exactly the legacy rows
    /// the fallback exists for. Pinned here so reversing it has to be
    /// deliberate rather than accidental.
    /// </summary>
    [Test]
    public async Task UpdateTask_ScopeThis_UnresolvableFolder_IsRefused_DeliberateNarrowing()
    {
        var nextMonday = GetNextMonday();
        var seriesStart = DateTime.SpecifyKind(nextMonday, DateTimeKind.Utc);
        var (arpId, _) = await SeedWeeklyTask(seriesStart, 0, 0);

        var result = await _calendarService.UpdateTask(
            BuildEditWithoutFolder(arpId, nextMonday, "this", nextMonday));

        var localizationService = new BackendConfigurationLocalizationService();
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message,
                Is.EqualTo(localizationService.GetString("FolderIsRequired")));
        });

        // And the occurrence override the scope would have written is absent,
        // because the refusal happens before the dispatch.
        var exceptions = await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions
            .Where(x => x.AreaRulePlanningId == arpId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync();
        Assert.That(exceptions, Is.Empty);
    }
}
