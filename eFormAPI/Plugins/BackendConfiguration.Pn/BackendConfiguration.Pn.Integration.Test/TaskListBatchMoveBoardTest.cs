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
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Infrastructure.Models.ComplianceReport;
using BackendConfiguration.Pn.Infrastructure.Models.TaskList;
using BackendConfiguration.Pn.Infrastructure.Models.TaskWizard;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskListService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService;
using BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;
using BackendConfiguration.Pn.Services.CalendarChangeNotification;
using BackendConfiguration.Pn.Services.EventDeployService;
using BackendConfiguration.Pn.Services.WorkerTagMembership;
using eFormCore;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eForm.Infrastructure.Models;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Enums;
using NSubstitute;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// #1297 — task-list batch action "Flyt til kalender"
/// (<c>BackendConfigurationTaskListService.MoveToBoard</c>, <c>POST task-list/move-to-board</c>)
/// plus the board/property defence-in-depth guard it added to
/// <c>BackendConfigurationCalendarService.UpdateTask</c>.
///
/// Decisions under test (product owner, 2026-09-18): the task adopts the target
/// calendar's colour; ALL per-occurrence board overrides are cleared, past and
/// future; history follows the task (the compliance report resolves the board at
/// read time); a board of another property is a per-task failure, an unknown or
/// removed board fails the whole batch before anything is written.
///
/// MoveToBoard is a direct write (it never calls the calendar service), so the
/// task-list service gets a substitute calendar service and the tests assert the
/// REAL rows. The report / week-read / UpdateTask tests build the real services.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class TaskListBatchMoveBoardTest : TestBaseSetup
{
    private const string TargetColor = "#2e7d32";

    private int _uidCounter = 970_000;
    private IBackendConfigurationCalendarService _calendarServiceSubstitute = null!;
    private BackendConfigurationTaskListService _taskListService = null!;
    private IUserService _userService = null!;

    [SetUp]
    public async Task SetupTaskListService()
    {
        // FK-safe cleanup, children before parents.
        BackendConfigurationPnDbContext!.CalendarOccurrenceExceptionSites.RemoveRange(
            BackendConfigurationPnDbContext.CalendarOccurrenceExceptionSites);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.CalendarOccurrenceExceptions.RemoveRange(
            BackendConfigurationPnDbContext.CalendarOccurrenceExceptions);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.PlanningSites.RemoveRange(
            BackendConfigurationPnDbContext.PlanningSites);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.Compliances.RemoveRange(
            BackendConfigurationPnDbContext.Compliances);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.CalendarConfigurations.RemoveRange(
            BackendConfigurationPnDbContext.CalendarConfigurations);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.CalendarBoards.RemoveRange(
            BackendConfigurationPnDbContext.CalendarBoards);
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

        // Echoes the key back so assertions can match the localization key.
        var localizationService = Substitute.For<IBackendConfigurationLocalizationService>();
        localizationService.GetString(Arg.Any<string>())
            .Returns(callInfo => (string)callInfo[0]);

        _calendarServiceSubstitute = Substitute.For<IBackendConfigurationCalendarService>();

        _taskListService = new BackendConfigurationTaskListService(
            localizationService,
            _userService,
            BackendConfigurationPnDbContext!,
            ItemsPlanningPnDbContext!,
            _calendarServiceSubstitute,
            Substitute.For<IBackendConfigurationTaskWizardService>(),
            Substitute.For<ICalendarOccurrenceRetractionService>(),
            Substitute.For<ICalendarPastSeriesBackfillService>(),
            TestContextLogger<BackendConfigurationTaskListService>.Instance);
    }

    // ------------------------------------------------------------------
    // Seeding
    // ------------------------------------------------------------------

    private static string Key(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string IsoUtc(DateTime d) =>
        DateTime.SpecifyKind(d, DateTimeKind.Utc)
            .ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static DateTime NextMonday()
    {
        var today = DateTime.UtcNow.Date;
        var daysUntilMonday = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        if (daysUntilMonday == 0) daysUntilMonday = 7;
        return DateTime.SpecifyKind(today.AddDays(daysUntilMonday), DateTimeKind.Utc);
    }

    private async Task<(int PropertyId, int AreaId)> SeedProperty()
    {
        var area = new Area
        {
            Type = AreaTypesEnum.Type1, ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await area.Create(BackendConfigurationPnDbContext!);

        var property = new Property
        {
            Name = $"MoveBoardProp-{Guid.NewGuid()}", ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await property.Create(BackendConfigurationPnDbContext!);
        return (property.Id, area.Id);
    }

    private async Task<int> SeedBoard(int propertyId, string name, string color = "#112233", bool removed = false)
    {
        var board = new CalendarBoard
        {
            Name = name, Color = color, PropertyId = propertyId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.CalendarBoards.AddAsync(board);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        if (removed)
        {
            board.WorkflowState = Constants.WorkflowStates.Removed;
            await BackendConfigurationPnDbContext.SaveChangesAsync();
        }
        return board.Id;
    }

    /// <summary>
    /// A weekly-on-Monday task-wizard task (CreatedInGuide) anchored at
    /// <paramref name="anchor"/>. <paramref name="boardId"/> = null with
    /// <paramref name="withCalendarConfiguration"/> = true seeds a configuration
    /// without a board; false seeds none at all. Returns (arpId, planningId).
    /// </summary>
    private async Task<(int ArpId, int PlanningId)> SeedTask(
        int propertyId, int areaId, DateTime anchor, int? boardId,
        bool withCalendarConfiguration = true, bool createdInGuide = true,
        double startHour = 14.5, double duration = 2.0, string color = "#ff0000")
    {
        var areaRule = new AreaRule
        {
            AreaId = areaId, PropertyId = propertyId, EformId = 0, CreatedInGuide = createdInGuide,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await areaRule.Create(BackendConfigurationPnDbContext!);

        await new AreaRuleTranslation
        {
            AreaRuleId = areaRule.Id, LanguageId = 1, Name = "Move me",
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        }.Create(BackendConfigurationPnDbContext!);

        var planning = new Planning
        {
            Enabled = true, RepeatEvery = 1, RepeatType = RepeatType.Week,
            StartDate = DateTime.SpecifyKind(anchor, DateTimeKind.Utc), DayOfWeek = DayOfWeek.Monday,
            RelatedEFormId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = propertyId, AreaId = areaId,
            ItemPlanningId = planning.Id, StartDate = DateTime.SpecifyKind(anchor, DateTimeKind.Utc),
            Status = true, RepeatType = 2, RepeatEvery = 1, RepeatWeekdaysCsv = "1", DayOfWeek = 1,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.AreaRulePlannings.AddAsync(arp);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        if (withCalendarConfiguration)
        {
            await BackendConfigurationPnDbContext.CalendarConfigurations.AddAsync(new CalendarConfiguration
            {
                AreaRulePlanningId = arp.Id, BoardId = boardId, StartHour = startHour, Duration = duration,
                Color = color,
                WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
            });
            await BackendConfigurationPnDbContext.SaveChangesAsync();
        }

        return (arp.Id, planning.Id);
    }

    private async Task<int> SeedOverride(int arpId, DateTime originalDate, int? boardId,
        double? startHour = null, bool isDeleted = false, bool removed = false)
    {
        var exception = new CalendarOccurrenceException
        {
            AreaRulePlanningId = arpId,
            OriginalDate = DateTime.SpecifyKind(originalDate.Date, DateTimeKind.Utc),
            BoardId = boardId,
            StartHour = startHour,
            IsDeleted = isDeleted,
            WorkflowState = removed ? Constants.WorkflowStates.Removed : Constants.WorkflowStates.Created,
            CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions.AddAsync(exception);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        return exception.Id;
    }

    private async Task<CalendarConfiguration> Config(int arpId) =>
        await BackendConfigurationPnDbContext!.CalendarConfigurations
            .AsNoTracking()
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .SingleAsync(x => x.AreaRulePlanningId == arpId);

    private async Task<CalendarOccurrenceException> Override(int id) =>
        await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions
            .AsNoTracking()
            .SingleAsync(x => x.Id == id);

    private async Task<int> SeedSdkCase(int status)
    {
        var language = await MicrotingDbContext!.Languages.FirstAsync();
        var uid = ++_uidCounter;
        var site = new Site
        {
            Name = $"move-board-site-{uid}", MicrotingUid = uid, LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddAsync(site);
        await MicrotingDbContext.SaveChangesAsync();

        var sdkCase = new Case
        {
            SiteId = site.Id, Status = status, WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Cases.AddAsync(sdkCase);
        await MicrotingDbContext.SaveChangesAsync();
        return sdkCase.Id;
    }

    private async Task<int> SeedCompliance(int planningId, int propertyId, int areaId, DateTime deadline,
        int sdkCaseId)
    {
        var compliance = new Compliance
        {
            ItemName = "Move me", PlanningId = planningId, PropertyId = propertyId, AreaId = areaId,
            Deadline = DateTime.SpecifyKind(deadline.Date, DateTimeKind.Utc),
            StartDate = DateTime.SpecifyKind(deadline.Date.AddDays(-7), DateTimeKind.Utc),
            MicrotingSdkCaseId = sdkCaseId, MicrotingSdkeFormId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Compliances.AddAsync(compliance);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        return compliance.Id;
    }

    private IEFormCoreService CoreHelper(Core core)
    {
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));
        return coreHelper;
    }

    private BackendConfigurationCalendarService BuildCalendarService(
        IEFormCoreService coreHelper, IBackendConfigurationTaskWizardService taskWizardService)
        => new(
            new BackendConfigurationLocalizationService(),
            _userService,
            BackendConfigurationPnDbContext!,
            coreHelper,
            Substitute.For<IEventDeployService>(),
            ItemsPlanningPnDbContext!,
            taskWizardService,
            Substitute.For<ICalendarAssignmentReconciliationService>(),
            Substitute.For<ICalendarChangeNotifier>(),
            TestContextLogger<BackendConfigurationCalendarService>.Instance,
            Substitute.For<ICalendarOccurrenceRetractionService>(),
            Substitute.For<ICalendarPastSeriesBackfillService>(),
            new WorkerTagMembershipService(coreHelper));

    // ------------------------------------------------------------------
    // MoveToBoard — the write
    // ------------------------------------------------------------------

    /// <summary>
    /// The happy path, one assertion per decision: BoardId and Color come from the
    /// target board, StartHour/Duration stay, and EVERY board override is cleared —
    /// a past one, a future one, a deleted-occurrence one and a soft-removed one —
    /// while the overrides' other per-occurrence fields survive.
    /// </summary>
    [Test]
    public async Task MoveToBoard_WithinProperty_SetsBoardAndColour_KeepsTime_ClearsEveryBoardOverride()
    {
        var (propertyId, areaId) = await SeedProperty();
        var oldBoard = await SeedBoard(propertyId, "Old calendar");
        var otherBoard = await SeedBoard(propertyId, "Other calendar");
        var targetBoard = await SeedBoard(propertyId, "Target calendar", TargetColor);
        var monday = NextMonday();
        var (arpId, _) = await SeedTask(propertyId, areaId, monday.AddDays(-28), oldBoard,
            startHour: 14.5, duration: 2.0, color: "#ff0000");

        var pastOverride = await SeedOverride(arpId, monday.AddDays(-14), otherBoard, startHour: 7.0);
        var futureOverride = await SeedOverride(arpId, monday.AddDays(7), otherBoard);
        var deletedOverride = await SeedOverride(arpId, monday.AddDays(14), oldBoard, isDeleted: true);
        var removedOverride = await SeedOverride(arpId, monday.AddDays(21), otherBoard, removed: true);
        var noBoardOverride = await SeedOverride(arpId, monday.AddDays(28), null, startHour: 11.0);

        var result = await _taskListService.MoveToBoard(new TaskListBatchMoveBoardModel
        {
            TaskIds = [arpId], BoardId = targetBoard
        });

        Assert.That(result.Success, Is.True, result.Message);
        var config = await Config(arpId);
        var past = await Override(pastOverride);
        var future = await Override(futureOverride);
        var deleted = await Override(deletedOverride);
        var removed = await Override(removedOverride);
        var noBoard = await Override(noBoardOverride);
        Assert.Multiple(() =>
        {
            Assert.That(config.BoardId, Is.EqualTo(targetBoard));
            Assert.That(config.Color, Is.EqualTo(TargetColor), "the task adopts the target calendar's colour");
            Assert.That(config.StartHour, Is.EqualTo(14.5), "StartHour must not move");
            Assert.That(config.Duration, Is.EqualTo(2.0), "Duration must not move");

            Assert.That(past.BoardId, Is.Null, "past override must follow the task (history follows)");
            Assert.That(future.BoardId, Is.Null, "future override must follow the task");
            Assert.That(deleted.BoardId, Is.Null);
            Assert.That(removed.BoardId, Is.Null, "ALL overrides are cleared, soft-removed rows included");

            // Only BoardId is touched — the other per-occurrence edits stay.
            Assert.That(past.StartHour, Is.EqualTo(7.0));
            Assert.That(deleted.IsDeleted, Is.True);
            Assert.That(removed.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
            Assert.That(noBoard.StartHour, Is.EqualTo(11.0));
        });
        await _calendarServiceSubstitute.DidNotReceive().UpdateTask(Arg.Any<CalendarTaskUpdateRequestModel>());
    }

    /// <summary>
    /// A task with no CalendarConfiguration (e.g. created by the task wizard) gets
    /// one, at the 09:00 / 1h the read path renders it at, so the move does not
    /// also move it in time.
    /// </summary>
    [Test]
    public async Task MoveToBoard_TaskWithoutCalendarConfiguration_CreatesOneAtTheRenderedTime()
    {
        var (propertyId, areaId) = await SeedProperty();
        var targetBoard = await SeedBoard(propertyId, "Target calendar", TargetColor);
        var (arpId, _) = await SeedTask(propertyId, areaId, NextMonday(), null, withCalendarConfiguration: false);

        var result = await _taskListService.MoveToBoard(new TaskListBatchMoveBoardModel
        {
            TaskIds = [arpId], BoardId = targetBoard
        });

        Assert.That(result.Success, Is.True, result.Message);
        var config = await Config(arpId);
        Assert.Multiple(() =>
        {
            Assert.That(config.BoardId, Is.EqualTo(targetBoard));
            Assert.That(config.Color, Is.EqualTo(TargetColor));
            Assert.That(config.StartHour, Is.EqualTo(9.0));
            Assert.That(config.Duration, Is.EqualTo(1.0));
        });
    }

    /// <summary>
    /// Per-task property check: a selection spanning two properties moves the task
    /// whose property owns the board and reports the other one as a failure —
    /// leaving that task, and its override, untouched.
    /// </summary>
    [Test]
    public async Task MoveToBoard_BoardOfAnotherProperty_FailsThatTaskOnly_OthersSucceed()
    {
        var (propertyA, areaA) = await SeedProperty();
        var (propertyB, areaB) = await SeedProperty();
        var boardA = await SeedBoard(propertyA, "A calendar");
        var targetBoardA = await SeedBoard(propertyA, "A target", TargetColor);
        var boardB = await SeedBoard(propertyB, "B calendar", "#0000ff");
        var monday = NextMonday();
        var (arpA, _) = await SeedTask(propertyA, areaA, monday, boardA);
        var (arpB, _) = await SeedTask(propertyB, areaB, monday, boardB, color: "#0000ff");
        var overrideB = await SeedOverride(arpB, monday.AddDays(7), boardB);

        var result = await _taskListService.MoveToBoard(new TaskListBatchMoveBoardModel
        {
            TaskIds = [arpA, arpB], BoardId = targetBoardA
        });

        var configA = await Config(arpA);
        var configB = await Config(arpB);
        Assert.Multiple(async () =>
        {
            Assert.That(result.Success, Is.True, "one of two succeeded — a partial result");
            Assert.That(result.Message, Does.Contain("PartiallyCompleted"));
            Assert.That(result.Message, Does.Contain($"#{arpB}: SelectedBoardDoesNotBelongToTaskProperty"));
            Assert.That(result.Message, Does.Not.Contain($"#{arpA}:"));
            Assert.That(configA.BoardId, Is.EqualTo(targetBoardA));
            Assert.That(configB.BoardId, Is.EqualTo(boardB), "the other property's task must not move");
            Assert.That(configB.Color, Is.EqualTo("#0000ff"));
            Assert.That((await Override(overrideB)).BoardId, Is.EqualTo(boardB),
                "a rejected task's overrides must not be cleared either");
        });
    }

    /// <summary>
    /// Whole-batch validation: an unknown or soft-removed board is refused BEFORE
    /// the loop, so nothing is written for any task.
    /// </summary>
    [TestCase(true, TestName = "MoveToBoard_RemovedBoard_RejectedBeforeLoop_NothingWritten")]
    [TestCase(false, TestName = "MoveToBoard_UnknownBoard_RejectedBeforeLoop_NothingWritten")]
    public async Task MoveToBoard_BoardNotLive_RejectedBeforeLoop_NothingWritten(bool boardExistsButRemoved)
    {
        var (propertyId, areaId) = await SeedProperty();
        var oldBoard = await SeedBoard(propertyId, "Old calendar");
        var target = boardExistsButRemoved
            ? await SeedBoard(propertyId, "Deleted calendar", TargetColor, removed: true)
            : int.MaxValue;
        var monday = NextMonday();
        var (arp1, _) = await SeedTask(propertyId, areaId, monday, oldBoard);
        var (arp2, _) = await SeedTask(propertyId, areaId, monday, oldBoard);
        var overrideId = await SeedOverride(arp1, monday.AddDays(7), oldBoard);

        var result = await _taskListService.MoveToBoard(new TaskListBatchMoveBoardModel
        {
            TaskIds = [arp1, arp2], BoardId = target
        });

        Assert.Multiple(async () =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Is.EqualTo("SelectedBoardNotFound"));
            Assert.That((await Config(arp1)).BoardId, Is.EqualTo(oldBoard));
            Assert.That((await Config(arp2)).BoardId, Is.EqualTo(oldBoard));
            Assert.That((await Config(arp1)).Color, Is.EqualTo("#ff0000"));
            Assert.That((await Override(overrideId)).BoardId, Is.EqualTo(oldBoard));
        });
    }

    /// <summary>Same eligibility rule as every other batch action.</summary>
    [Test]
    public async Task MoveToBoard_NonTaskWizardPlanning_IsTaskNotFound_AndUntouched()
    {
        var (propertyId, areaId) = await SeedProperty();
        var oldBoard = await SeedBoard(propertyId, "Old calendar");
        var targetBoard = await SeedBoard(propertyId, "Target calendar", TargetColor);
        var (arpId, _) = await SeedTask(propertyId, areaId, NextMonday(), oldBoard, createdInGuide: false);

        var result = await _taskListService.MoveToBoard(new TaskListBatchMoveBoardModel
        {
            TaskIds = [arpId], BoardId = targetBoard
        });

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain($"#{arpId}: Task not found"));
        Assert.That((await Config(arpId)).BoardId, Is.EqualTo(oldBoard));
    }

    // ------------------------------------------------------------------
    // MoveToBoard — who reads the board afterwards
    // ------------------------------------------------------------------

    /// <summary>
    /// History follows the task: the compliance report resolves the board at read
    /// time, so after the move BOTH past rows — the one that used the series'
    /// board and the one pinned by an override to a third calendar — appear under
    /// the NEW calendar's filter and under neither old one.
    /// </summary>
    [Test]
    public async Task MoveToBoard_ComplianceReport_PastRowsFollowToTheNewCalendar()
    {
        var core = await GetCore();
        var today = DateTime.UtcNow.Date;

        var (propertyId, areaId) = await SeedProperty();
        var oldBoard = await SeedBoard(propertyId, "Old calendar");
        var overrideBoard = await SeedBoard(propertyId, "Override calendar");
        var targetBoard = await SeedBoard(propertyId, "Target calendar", TargetColor);
        var (arpId, planningId) = await SeedTask(propertyId, areaId, today.AddDays(-35), oldBoard);

        var seriesRow = await SeedCompliance(planningId, propertyId, areaId, today.AddDays(-7), await SeedSdkCase(100));
        var overriddenDate = today.AddDays(-14);
        var overriddenRow = await SeedCompliance(planningId, propertyId, areaId, overriddenDate, await SeedSdkCase(50));
        await SeedOverride(arpId, overriddenDate, overrideBoard);

        var report = new BackendConfigurationComplianceReportService(
            new BackendConfigurationLocalizationService(), _userService,
            BackendConfigurationPnDbContext!, CoreHelper(core), ItemsPlanningPnDbContext!,
            TestContextLogger<BackendConfigurationComplianceReportService>.Instance,
            new WorkerTagMembershipService(CoreHelper(core)));

        async Task<List<int>> RowsOn(int boardId)
        {
            var res = await report.Index(new ComplianceReportRequestModel
            {
                DateFrom = today.AddDays(-21), DateTo = today, Status = "all",
                PropertyId = propertyId, BoardIds = [boardId], TagIds = [], SiteIds = [],
                PageIndex = 0, PageSize = 0
            });
            Assert.That(res.Success, Is.True, res.Message);
            return res.Model!.Entities.Select(r => r.ComplianceId).ToList();
        }

        // Premise: before the move the rows are split over the two old calendars.
        Assert.That(await RowsOn(oldBoard), Is.EquivalentTo(new[] { seriesRow }));
        Assert.That(await RowsOn(overrideBoard), Is.EquivalentTo(new[] { overriddenRow }));
        Assert.That(await RowsOn(targetBoard), Is.Empty);

        var result = await _taskListService.MoveToBoard(new TaskListBatchMoveBoardModel
        {
            TaskIds = [arpId], BoardId = targetBoard
        });
        Assert.That(result.Success, Is.True, result.Message);

        Assert.Multiple(async () =>
        {
            Assert.That(await RowsOn(targetBoard), Is.EquivalentTo(new[] { seriesRow, overriddenRow }),
                "history follows the task, the overridden past row included");
            Assert.That(await RowsOn(oldBoard), Is.Empty);
            Assert.That(await RowsOn(overrideBoard), Is.Empty);
        });
    }

    /// <summary>
    /// EventDeployService scopes a deploy pass by board only by handing BoardIds to
    /// GetTasksForWeek, which matches each task's CURRENT board at read time — the
    /// exact enumeration pinned here. After the move the week's occurrence is
    /// enumerated for the NEW calendar and no longer for the old one, while the
    /// already-deployed Compliance row (the deploy idempotence key) is left exactly
    /// as it was: no reconcile is needed and none is done.
    /// </summary>
    [Test]
    public async Task MoveToBoard_BoardScopedWeekEnumeration_FollowsTheMove_DeployedRowsUntouched()
    {
        var core = await GetCore();
        var monday = NextMonday();

        var (propertyId, areaId) = await SeedProperty();
        var oldBoard = await SeedBoard(propertyId, "Old calendar");
        var targetBoard = await SeedBoard(propertyId, "Target calendar", TargetColor);
        var (arpId, planningId) = await SeedTask(propertyId, areaId, monday.AddDays(-14), oldBoard);
        var deployedDeadline = monday.AddDays(7);
        var deployedCase = await SeedSdkCase(50);
        var deployedRow = await SeedCompliance(planningId, propertyId, areaId, deployedDeadline, deployedCase);

        var calendar = BuildCalendarService(CoreHelper(core), Substitute.For<IBackendConfigurationTaskWizardService>());

        async Task<bool> EnumeratedFor(int boardId)
        {
            var res = await calendar.GetTasksForWeek(new CalendarTaskRequestModel
            {
                PropertyId = propertyId,
                WeekStart = IsoUtc(monday),
                WeekEnd = IsoUtc(monday.AddDays(6).AddHours(23).AddMinutes(59)),
                BoardIds = [boardId], TagNames = [], SiteIds = [],
                ActionableOnly = false
            });
            Assert.That(res.Success, Is.True, res.Message);
            return res.Model!.Any(t => t.Id == arpId && t.TaskDate == Key(monday));
        }

        Assert.That(await EnumeratedFor(oldBoard), Is.True, "premise: enumerated for the old calendar");
        Assert.That(await EnumeratedFor(targetBoard), Is.False, "premise: not for the target yet");

        var result = await _taskListService.MoveToBoard(new TaskListBatchMoveBoardModel
        {
            TaskIds = [arpId], BoardId = targetBoard
        });
        Assert.That(result.Success, Is.True, result.Message);

        var deployed = await BackendConfigurationPnDbContext!.Compliances.AsNoTracking()
            .SingleAsync(x => x.Id == deployedRow);
        Assert.Multiple(async () =>
        {
            Assert.That(await EnumeratedFor(targetBoard), Is.True);
            Assert.That(await EnumeratedFor(oldBoard), Is.False);
            Assert.That(deployed.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
            Assert.That(deployed.MicrotingSdkCaseId, Is.EqualTo(deployedCase));
            Assert.That(deployed.Deadline.Date, Is.EqualTo(deployedDeadline.Date));
            Assert.That(await BackendConfigurationPnDbContext.Compliances.CountAsync(x => x.PlanningId == planningId),
                Is.EqualTo(1), "the move neither retracts nor creates deploy rows");
        });
    }

    // ------------------------------------------------------------------
    // UpdateTask — defence in depth
    // ------------------------------------------------------------------

    private static CalendarTaskUpdateRequestModel Edit(int arpId, int propertyId, int? boardId, DateTime date,
        string scope = "all")
        => new()
        {
            Id = arpId,
            Scope = scope,
            OriginalDate = Key(date),
            StartDate = DateTime.SpecifyKind(date, DateTimeKind.Utc),
            StartHour = 14.5,
            Duration = 2.0,
            Status = 1,
            RepeatType = 2,
            RepeatEvery = 1,
            RepeatWeekdaysCsv = "1",
            ComplianceEnabled = false,
            PropertyId = propertyId,
            EformId = 0,
            Sites = [101],
            WorkerTagIds = [],
            TagIds = [],
            BoardId = boardId,
            Color = "#abcdef",
            DescriptionHtml = string.Empty,
            Translates = [new CommonTranslationsModel { LanguageId = 1, Name = "Move me" }]
        };

    private static IBackendConfigurationTaskWizardService SucceedingWizard()
    {
        var wizard = Substitute.For<IBackendConfigurationTaskWizardService>();
        wizard.UpdateTask(Arg.Any<TaskWizardCreateModel>()).Returns(Task.FromResult(new OperationResult(true)));
        wizard.ApplyEformChangeToSeries(Arg.Any<int>(), Arg.Any<int>())
            .Returns(Task.FromResult(new OperationResult(true)));
        return wizard;
    }

    /// <summary>
    /// Before #1297 UpdateTask wrote <c>calConfig.BoardId = updateModel.BoardId</c> (and the
    /// "this"-scope exception's BoardId) without checking the board's property. Both
    /// scopes must now refuse it before writing anything.
    /// </summary>
    [TestCase("all")]
    [TestCase("this")]
    public async Task UpdateTask_BoardOfAnotherProperty_IsRejected_NothingWritten(string scope)
    {
        var (propertyA, areaA) = await SeedProperty();
        var (propertyB, _) = await SeedProperty();
        var boardA = await SeedBoard(propertyA, "A calendar");
        var boardB = await SeedBoard(propertyB, "B calendar");
        var monday = NextMonday();
        var (arpId, _) = await SeedTask(propertyA, areaA, monday, boardA);
        var wizard = SucceedingWizard();
        var calendar = BuildCalendarService(null!, wizard);

        var result = await calendar.UpdateTask(Edit(arpId, propertyA, boardB, monday, scope));

        Assert.Multiple(async () =>
        {
            Assert.That(result.Success, Is.False);
            // The fixture's BackendConfigurationLocalizationService echoes the key.
            Assert.That(result.Message, Is.EqualTo("SelectedBoardDoesNotBelongToTaskProperty"));
            Assert.That((await Config(arpId)).BoardId, Is.EqualTo(boardA));
            Assert.That(await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions
                .CountAsync(x => x.AreaRulePlanningId == arpId), Is.Zero);
        });
        await wizard.DidNotReceive().UpdateTask(Arg.Any<TaskWizardCreateModel>());
    }

    [Test]
    public async Task UpdateTask_RemovedBoard_IsRejected()
    {
        var (propertyId, areaId) = await SeedProperty();
        var board = await SeedBoard(propertyId, "Live calendar");
        var removedBoard = await SeedBoard(propertyId, "Deleted calendar", removed: true);
        var monday = NextMonday();
        var (arpId, _) = await SeedTask(propertyId, areaId, monday, board);
        var wizard = SucceedingWizard();
        var calendar = BuildCalendarService(null!, wizard);

        var result = await calendar.UpdateTask(Edit(arpId, propertyId, removedBoard, monday));

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo("SelectedBoardNotFound"));
        Assert.That((await Config(arpId)).BoardId, Is.EqualTo(board));
        await wizard.DidNotReceive().UpdateTask(Arg.Any<TaskWizardCreateModel>());
    }

    /// <summary>Positive control: the guard lets a same-property board through.</summary>
    [Test]
    public async Task UpdateTask_BoardOfTheSameProperty_IsAccepted()
    {
        var (propertyId, areaId) = await SeedProperty();
        var board = await SeedBoard(propertyId, "Old calendar");
        var target = await SeedBoard(propertyId, "Target calendar", TargetColor);
        var monday = NextMonday();
        var (arpId, _) = await SeedTask(propertyId, areaId, monday, board);
        var calendar = BuildCalendarService(null!, SucceedingWizard());

        var result = await calendar.UpdateTask(Edit(arpId, propertyId, target, monday));

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That((await Config(arpId)).BoardId, Is.EqualTo(target));
    }

    /// <summary>
    /// The guard only judges a CHANGE. A task already carrying another property's
    /// board (legacy data) must stay editable by a round-trip that leaves the board
    /// alone — every task-list batch action sends the stored BoardId back verbatim.
    /// </summary>
    [Test]
    public async Task UpdateTask_UnchangedLegacyMismatchedBoard_IsNotRejected()
    {
        var (propertyA, areaA) = await SeedProperty();
        var (propertyB, _) = await SeedProperty();
        var boardB = await SeedBoard(propertyB, "B calendar");
        var monday = NextMonday();
        var (arpId, _) = await SeedTask(propertyA, areaA, monday, boardB);
        var calendar = BuildCalendarService(null!, SucceedingWizard());

        var result = await calendar.UpdateTask(Edit(arpId, propertyA, boardB, monday));

        Assert.That(result.Success, Is.True, result.Message);
    }
}
