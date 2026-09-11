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
        DateTime startDate, int arpFolderId, int areaRuleFolderId, bool active = true,
        int? planningSdkFolderId = null)
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
            StartDate = startDate,
            // Defaults to whichever ARP column holds the folder, but can be set
            // independently: the two are NOT kept in lockstep in production.
            SdkFolderId = planningSdkFolderId
                          ?? (arpFolderId > 0 ? arpFolderId : areaRuleFolderId),
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

    /// <summary>
    /// The half-applied guarantee, stated positively.
    ///
    /// <c>UpdateTaskThisAndFollowing</c> commits its past-occurrence backfill
    /// and (on a date change) the series re-anchor BEFORE it calls the wizard.
    /// Resolving the folder in the pre-flight — ahead of the scope dispatch —
    /// is what removes the folder as a possible failure between those two
    /// halves. The proof that the pre-flight really does run first is that the
    /// wizard receives a NON-NULL FolderId: the request carried null, and
    /// nothing after the dispatch would have filled it in.
    ///
    /// The row here has no folder at all, which resolves to 0 rather than being
    /// refused — a task that was never filed under a folder is an ordinary
    /// shape, and this update must still go through end to end.
    /// </summary>
    [Test]
    public async Task UpdateTask_ScopeThisAndFollowing_FolderlessRow_ResolvesBeforeDispatch_AndCommitsBothHalves()
    {
        var nextMonday = GetNextMonday();
        var seriesStart = DateTime.SpecifyKind(nextMonday.AddDays(-28), DateTimeKind.Utc);
        var (arpId, planningId) = await SeedWeeklyTask(seriesStart, 0, 0);

        // A DATE change, which is what makes thisAndFollowing re-anchor the
        // series on top of writing the past-occurrence backfill.
        var movedTo = nextMonday.AddDays(1);
        var result = await _calendarService.UpdateTask(
            BuildEditWithoutFolder(arpId, movedTo, "thisAndFollowing", nextMonday));

        Assert.That(result.Success, Is.True, result.Message);

        // Ordering proof: null on the wire, non-null at the wizard.
        await _taskWizardService.Received(1).UpdateTask(
            Arg.Is<TaskWizardCreateModel>(m => m.FolderId != null));

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

        // BOTH halves landed — the failure mode this replaces was the first
        // half committing and the second throwing.
        Assert.Multiple(() =>
        {
            Assert.That(anchors, Has.Count.EqualTo(4), "first half: past occurrences anchored");
            Assert.That(arp.StartDate!.Value.Date, Is.EqualTo(movedTo.Date), "first half: series re-anchored");
            Assert.That(planning.StartDate.Date, Is.EqualTo(movedTo.Date));
            // The payload carries 11.0/2.0; the seed was 9.0/1.0.
            Assert.That(calConfig.StartHour, Is.EqualTo(11.0), "second half: the edit's own fields landed");
            Assert.That(calConfig.Duration, Is.EqualTo(2.0));
        });
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

    /// <summary>
    /// No folder anywhere is an ordinary shape — plenty of AreaRulePlannings
    /// were never filed under one — so the save must SUCCEED and simply leave
    /// both columns as they were. "Unchanged" is the contract; "must have a
    /// folder" would be a different and much stronger rule, and refusing here
    /// blocked every update to such a row on every scope.
    ///
    /// Success alone is not enough to discriminate, so the message is pinned
    /// too: the unfixed code reaches the `(int)` unbox, throws, and returns
    /// Success=false with "ErrorWhileUpdatingTask".
    /// </summary>
    [Test]
    public async Task WizardUpdateTask_NullFolderId_WithNoFolderAnywhere_SucceedsAndChangesNothing()
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

        var localizationService = new BackendConfigurationLocalizationService();
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(result.Message,
                Is.EqualTo(localizationService.GetString("TaskUpdatedSuccessful")));
            Assert.That(result.Message,
                Is.Not.EqualTo(localizationService.GetString("ErrorWhileUpdatingTask")),
                "a crash-shaped failure would mean the unbox is still live");
        });

        var arp = await BackendConfigurationPnDbContext!.AreaRulePlannings
            .AsNoTracking().Include(x => x.AreaRule).FirstAsync(x => x.Id == arpId);
        Assert.Multiple(() =>
        {
            Assert.That(arp.FolderId, Is.Zero, "left exactly as it was");
            Assert.That(arp.AreaRule.FolderId, Is.Zero);
        });
    }

    /// <summary>
    /// The reason an unresolvable folder SKIPS the folder writes instead of
    /// substituting 0 into them.
    ///
    /// <c>Planning.SdkFolderId</c> is not kept in lockstep with the two ARP
    /// columns: <c>BackendConfigurationAreaRulePlanningsServiceHelper</c>
    /// overwrites it with a folder of its own after
    /// <c>CreateItemPlanningObject</c> seeded it from <c>areaRule.FolderId</c>
    /// (the chemicals/BMD path at :2240), so a Planning can hold a real
    /// SdkFolderId while both ARP columns are 0. Writing a substituted 0 into
    /// it would clear that folder and orphan the planning's deploys.
    /// </summary>
    [Test]
    public async Task WizardUpdateTask_FolderlessRow_DoesNotClearARealSdkFolderOnThePlanning()
    {
        var wizardService = await BuildRealWizardServiceAsync();
        var folder = await SeedSdkFolderAsync("folder-preservation-divergent", 810_004);

        var startDate = DateTime.UtcNow.Date.AddDays(30);
        // The divergent shape: nothing on either ARP column, a REAL folder on
        // the linked Planning.
        var (arpId, planningId) = await SeedWeeklyTask(
            startDate, 0, 0, planningSdkFolderId: folder.Id);

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

        // Active → active, the branch that rewrites Planning.SdkFolderId. The
        // assignee set is unchanged, so nothing is (re)deployed.
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
            Sites = [101],
            TagIds = [],
            Translates = [],
            ComplianceEnabled = false
        });

        Assert.That(result.Success, Is.True, result.Message);

        var planning = await ItemsPlanningPnDbContext!.Plannings
            .AsNoTracking().FirstAsync(x => x.Id == planningId);
        Assert.That(planning.SdkFolderId, Is.EqualTo(folder.Id),
            "an unresolvable ARP folder must not blank the Planning's own SDK folder");
    }

    /// <summary>
    /// Scope "this" never reads FolderId, and a row with no folder is ordinary,
    /// so a single-occurrence edit on one goes through untouched. This pins
    /// that the pre-flight — which runs for every scope — did not narrow what
    /// scope "this" accepts.
    /// </summary>
    [Test]
    public async Task UpdateTask_ScopeThis_FolderlessRow_SucceedsAndWritesTheOccurrenceOverride()
    {
        var nextMonday = GetNextMonday();
        var seriesStart = DateTime.SpecifyKind(nextMonday, DateTimeKind.Utc);
        var (arpId, _) = await SeedWeeklyTask(seriesStart, 0, 0);

        var result = await _calendarService.UpdateTask(
            BuildEditWithoutFolder(arpId, nextMonday, "this", nextMonday));

        Assert.That(result.Success, Is.True, result.Message);

        // The scope did its own work rather than being short-circuited.
        var exception = await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions
            .Where(x => x.AreaRulePlanningId == arpId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .SingleAsync();
        var arp = await BackendConfigurationPnDbContext.AreaRulePlannings
            .AsNoTracking().Include(x => x.AreaRule).FirstAsync(x => x.Id == arpId);
        Assert.Multiple(() =>
        {
            Assert.That(exception.OriginalDate.Date, Is.EqualTo(nextMonday.Date));
            Assert.That(exception.StartHour, Is.EqualTo(11.0));
            Assert.That(arp.FolderId, Is.Zero, "and the folder columns stay as they were");
            Assert.That(arp.AreaRule.FolderId, Is.Zero);
        });
    }
}
