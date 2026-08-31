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
using BackendConfiguration.Pn.Infrastructure.Models.TaskList;
using BackendConfiguration.Pn.Infrastructure.Models.TaskWizard;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskListService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService;
using BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;
using BackendConfiguration.Pn.Services.CalendarChangeNotification;
using BackendConfiguration.Pn.Services.EventDeployService;
using BackendConfiguration.Pn.Services.CalendarOccurrenceRetraction;
using BackendConfiguration.Pn.Services.CalendarPastSeriesBackfill;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Enums;
using NSubstitute;
using BcCompliance = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.Compliance;
using BcPlanningSite = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.PlanningSite;
using SdkCase = Microting.eForm.Infrastructure.Data.Entities.Case;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// #1122 §4/§5 — <c>BackendConfigurationTaskListService.ChangeStartDate</c> and
/// its read-only companion <c>ChangeStartDatePreview</c>.
///
/// WHY THIS FIXTURE EXISTS.
///
/// 1. THE APPLY IS THREE OVERRIDES, AND EACH IS SILENTLY DESTRUCTIVE IF LOST.
///    <c>BuildUpdateModel</c> deliberately fabricates a "nearest future
///    same-weekday" StartDate so that the OTHER batch actions (worker / eForm /
///    tag) cannot accidentally move a series. This action's entire purpose is to
///    move it, so it must override StartDate, null OriginalDate (which is what
///    keeps <c>dateChanged</c> true — leaving it equal to StartDate makes
///    UpdateTask re-fetch the CURRENT anchor and quietly discard the user's
///    date) and force Scope = "all". Nothing downstream complains if one of the
///    three is dropped; the save simply does nothing visible. So the assertions
///    are on the captured <c>CalendarTaskUpdateRequestModel</c> — the same
///    isolation TaskListBatchEformTagsTest uses, and the only place the loss is
///    observable.
///
/// 2. THE PREVIEW IS A PROMISE. The modal disables Save until the preview
///    resolves and then shows the admin "M åbne forekomster tilbagekaldes · K
///    gennemførte bevares · L overskredne opgaver oprettes" before they commit to
///    an uncapped, synchronous, partly irreversible operation. A preview that
///    over- or under-counts is worse than no preview. Every preview test below
///    therefore compares the projection against what the corresponding WRITE
///    actually does on the same seeded data, rather than against a
///    hand-computed constant.
///
/// 3. ROWS, NOT DATES. <c>Compliance</c> has no site column, so one occurrence
///    deployed to two workers is TWO rows sharing a day, and they can disagree —
///    worker A answered, worker B did not. The write path retracts B and
///    preserves A (invariant R2). A preview counting distinct dates would report
///    one occurrence where the apply touches one and skips one, and the two
///    numbers would never add up. <see cref="Preview_MixedOccurrence_CountsRowsNotDates"/>
///    pins that down.
///
/// DATES ARE RELATIVE TO UtcNow throughout. Absolute dates rot: a hard-coded
/// "past" date eventually stops being in the past. The user's headline example
/// ("i dag 25.08.26 … startdato … til 01.01.2026 med årlig frekvens" → one red
/// task) is therefore expressed as "a yearly series re-anchored seven months
/// back", which yields exactly the same single past occurrence — the anchor
/// itself — on any day the suite happens to run.
///
/// NB: Compliances carries a UNIQUE index on (PlanningId, Deadline), so two rows
/// for the SAME day must differ in time-of-day. Every seeded case uses
/// MicrotingUid = null so the SDK CaseDelete cloud call is skipped (there is no
/// cloud in CI) while all local bookkeeping still runs — the same trick
/// CalendarOccurrenceRetractionTests uses.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class TaskListBatchStartDateTest : TestBaseSetup
{
    /// <summary>
    /// Snapshotted ONCE per test in [SetUp], never re-derived. As a computed
    /// property this read <c>DateTime.UtcNow.Date</c> afresh on every access —
    /// at seed time and again when building the expectation after the call — so
    /// any test straddling 00:00 UTC seeded against one day and asserted against
    /// the next.
    /// </summary>
    private DateTime _today;

    private DateTime Today => _today;

    private IUserService _userService = null!;
    private IBackendConfigurationTaskWizardService _taskWizardService = null!;
    private IBackendConfigurationCalendarService _calendarService = null!;
    private IBackendConfigurationLocalizationService _localizationService = null!;
    private ICalendarOccurrenceRetractionService _retractionService = null!;
    private ICalendarPastSeriesBackfillService _backfillService = null!;
    private IEventDeployService _deployService = null!;
    private BackendConfigurationTaskListService _taskListService = null!;
    private List<CalendarTaskUpdateRequestModel> _updateCalls = null!;
    private eFormCore.Core _core = null!;
    private IEFormCoreService _coreHelper = null!;

    [SetUp]
    public async Task SetupTaskListService()
    {
        _today = DateTime.UtcNow.Date;

        // FK-safe cleanup so each test starts fresh (mirrors
        // TaskListBatchEformTagsTest / CalendarOccurrenceRetractionTests).
        BackendConfigurationPnDbContext!.Compliances.RemoveRange(
            BackendConfigurationPnDbContext.Compliances);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        ItemsPlanningPnDbContext!.PlanningCaseSites.RemoveRange(
            ItemsPlanningPnDbContext.PlanningCaseSites);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        ItemsPlanningPnDbContext.PlanningCases.RemoveRange(
            ItemsPlanningPnDbContext.PlanningCases);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        MicrotingDbContext!.Cases.RemoveRange(MicrotingDbContext.Cases);
        await MicrotingDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.PlanningSites.RemoveRange(
            BackendConfigurationPnDbContext.PlanningSites);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.CalendarConfigurations.RemoveRange(
            BackendConfigurationPnDbContext.CalendarConfigurations);
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

        ItemsPlanningPnDbContext.Plannings.RemoveRange(
            ItemsPlanningPnDbContext.Plannings);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        // AreaRulePlanningWorkerTags is newer than the backend-config snapshot
        // SQL the base [SetUp] replays, so it is never dropped and its rows
        // accumulate while AreaRulePlanning ids restart at 1 — a previous
        // fixture's link would otherwise add phantom recipients to every
        // resolved site set. Same guard as CalendarPastSeriesBackfillTests.
        await BackendConfigurationPnDbContext.Database
            .ExecuteSqlRawAsync("DELETE FROM `AreaRulePlanningWorkerTags`;");

        _userService = Substitute.For<IUserService>();
        _userService.UserId.Returns(1);
        _userService.GetCurrentUserLanguage()
            .Returns(Task.FromResult(new Language { Id = 1, Name = "English", LanguageCode = "en-US" }));

        _taskWizardService = Substitute.For<IBackendConfigurationTaskWizardService>();

        // Echoes the key back, matching the plugin's convention where
        // GetString("SomeKey") is itself the message under test.
        _localizationService = Substitute.For<IBackendConfigurationLocalizationService>();
        _localizationService.GetString(Arg.Any<string>())
            .Returns(callInfo => (string)callInfo[0]);

        _updateCalls = [];
        _calendarService = Substitute.For<IBackendConfigurationCalendarService>();
        _calendarService.UpdateTask(Arg.Do<CalendarTaskUpdateRequestModel>(m => _updateCalls.Add(m)))
            .Returns(Task.FromResult(new OperationResult(true, "CalendarTaskUpdatedSuccessfully")));

        // The retraction and backfill services are REAL. They are the whole
        // point of the preview: substituting them would test that the preview
        // adds up numbers it was handed, not that those numbers match the apply.
        _core = await GetCore();
        _coreHelper = Substitute.For<IEFormCoreService>();
        _coreHelper.GetCore().Returns(Task.FromResult(_core));
        var coreHelper = _coreHelper;

        _retractionService = new CalendarOccurrenceRetractionService(
            BackendConfigurationPnDbContext!, ItemsPlanningPnDbContext!, coreHelper,
            NullLogger<CalendarOccurrenceRetractionService>.Instance);

        // Only the WRITE half of the backfill touches the deploy service; the
        // preview's PlanPastSeriesBackfillAsync never calls it. Substituted so
        // the "preview equals apply" test can count the fan-out.
        _deployService = Substitute.For<IEventDeployService>();
        _deployService.EnsureComplianceForOccurrenceAsync(
                Arg.Any<AreaRulePlanning>(), Arg.Any<DateTime>(), Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => new EnsureComplianceResult { Created = true, ComplianceId = 1, SdkCaseId = 1 });

        _backfillService = new CalendarPastSeriesBackfillService(
            ItemsPlanningPnDbContext!, BackendConfigurationPnDbContext!, coreHelper,
            _deployService,
            new CalendarAssignmentResolver(BackendConfigurationPnDbContext!, coreHelper),
            NullLogger<CalendarPastSeriesBackfillService>.Instance);

        _taskListService = new BackendConfigurationTaskListService(
            _localizationService,
            _userService,
            BackendConfigurationPnDbContext!,
            ItemsPlanningPnDbContext!,
            _calendarService,
            _taskWizardService,
            _retractionService,
            _backfillService,
            NullLogger<BackendConfigurationTaskListService>.Instance
        );
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Seeding
    // ─────────────────────────────────────────────────────────────────────────

    private sealed record Seeded(int ArpId, int PlanningId);

    /// <summary>
    /// Seeds Area → Property → AreaRule(+translation, CreatedInGuide=true) →
    /// Planning → AreaRulePlanning(+PlanningSites) → CalendarConfiguration —
    /// the exact shape BuildUpdateModel and the preview both read.
    /// <paramref name="repeatType"/> is written to BOTH the planning (which the
    /// recurrence enumerator reads) and the arp, as the wizard does.
    /// </summary>
    private async Task<Seeded> SeedTask(
        DateTime startDate,
        int repeatType,
        IEnumerable<int> siteIds,
        bool complianceEnabled = false,
        int? dayOfMonth = null)
    {
        var area = new Area
        {
            Type = AreaTypesEnum.Type1, ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await area.Create(BackendConfigurationPnDbContext!);

        var property = new Property
        {
            Name = $"StartDateProp-{Guid.NewGuid()}", ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await property.Create(BackendConfigurationPnDbContext!);

        var areaRule = new AreaRule
        {
            AreaId = area.Id, PropertyId = property.Id, EformId = 7, CreatedInGuide = true,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await areaRule.Create(BackendConfigurationPnDbContext!);

        var areaRuleTranslation = new AreaRuleTranslation
        {
            AreaRuleId = areaRule.Id, LanguageId = 1, Name = "Task", Description = "Task description",
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await areaRuleTranslation.Create(BackendConfigurationPnDbContext!);

        var anchor = DateTime.SpecifyKind(startDate.Date, DateTimeKind.Utc);

        var planning = new Planning
        {
            Enabled = true, RepeatEvery = 1, RepeatType = (RepeatType)repeatType,
            StartDate = anchor, DayOfWeek = anchor.DayOfWeek, DayOfMonth = dayOfMonth,
            RelatedEFormId = 7, Description = "Original description",
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await planning.Create(ItemsPlanningPnDbContext!);

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = property.Id, AreaId = area.Id,
            ItemPlanningId = planning.Id, StartDate = anchor, Status = true,
            RepeatType = repeatType, RepeatEvery = 1,
            DayOfWeek = (int)anchor.DayOfWeek, DayOfMonth = dayOfMonth ?? 0,
            ComplianceEnabled = complianceEnabled,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await arp.Create(BackendConfigurationPnDbContext!);

        foreach (var siteId in siteIds)
        {
            var planningSite = new BcPlanningSite
            {
                AreaRulePlanningsId = arp.Id, SiteId = siteId,
                WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
            };
            await planningSite.Create(BackendConfigurationPnDbContext!);
        }

        var calConfig = new CalendarConfiguration
        {
            AreaRulePlanningId = arp.Id, StartHour = 9.0, Duration = 1.0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await calConfig.Create(BackendConfigurationPnDbContext!);

        return new Seeded(arp.Id, planning.Id);
    }

    /// <summary>Creates an SDK Site and returns its generated id.</summary>
    private async Task<int> SeedSdkSite()
    {
        var language = await MicrotingDbContext!.Languages.FirstAsync();
        var site = new Site
        {
            Name = $"start-date-site-{Guid.NewGuid()}", MicrotingUid = null,
            LanguageId = language.Id, WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddAsync(site);
        await MicrotingDbContext.SaveChangesAsync();
        return site.Id;
    }

    /// <summary>
    /// Seeds one deployed occurrence: SDK Case (MicrotingUid null so CaseDelete
    /// is skipped) + PlanningCase + PlanningCaseSite + Compliance.
    /// <paramref name="status"/> 100 == completed (the R2 gate), anything else
    /// (66 in-progress) == open.
    /// </summary>
    private async Task SeedDeployedOccurrence(
        int planningId, DateTime deadline, int status, int? sdkSiteId = null)
    {
        // A caller that also seeds PlanningSites needs the occurrence to belong
        // to one of THOSE sites, or the idempotence guard (which matches on the
        // backing case's site) can never see it.
        var siteId = sdkSiteId ?? await SeedSdkSite();

        var sdkCase = new SdkCase
        {
            SiteId = siteId, Status = status, MicrotingUid = null,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext!.Cases.AddAsync(sdkCase);
        await MicrotingDbContext.SaveChangesAsync();

        var planningCase = new PlanningCase
        {
            PlanningId = planningId, Status = status, MicrotingSdkeFormId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.PlanningCases.AddAsync(planningCase);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var planningCaseSite = new PlanningCaseSite
        {
            PlanningId = planningId, PlanningCaseId = planningCase.Id,
            MicrotingSdkSiteId = siteId, MicrotingSdkeFormId = 0,
            MicrotingSdkCaseId = sdkCase.Id, Status = status,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext.PlanningCaseSites.AddAsync(planningCaseSite);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var compliance = new BcCompliance
        {
            PlanningId = planningId,
            Deadline = DateTime.SpecifyKind(deadline, DateTimeKind.Utc),
            StartDate = DateTime.SpecifyKind(deadline.Date.AddDays(-7), DateTimeKind.Utc),
            MicrotingSdkCaseId = sdkCase.Id, MicrotingSdkeFormId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Compliances.AddAsync(compliance);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
    }

    private async Task<AreaRulePlanning> ReloadArp(int arpId) =>
        await BackendConfigurationPnDbContext!.AreaRulePlannings
            .AsNoTracking().FirstAsync(x => x.Id == arpId);

    private const int CompletedStatus = 100;
    private const int OpenStatus = 66;

    // ─────────────────────────────────────────────────────────────────────────
    // Wiring for the tests that drive the REAL calendar service
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Makes the substituted wizard do the two writes the real one does to the
    /// anchor (BackendConfigurationTaskWizardService.UpdateTask:821 and :1105),
    /// so a test can drive the REAL BackendConfigurationCalendarService.UpdateTask
    /// end to end. Without it the anchor never moves and the backfill — which
    /// reads planning.StartDate, by contract, never a request model — has nothing
    /// to enumerate.
    /// </summary>
    private void ConfigureWizardToPersistTheAnchor()
    {
        _taskWizardService.UpdateTask(Arg.Any<TaskWizardCreateModel>())
            .Returns(ci =>
            {
                var model = ci.Arg<TaskWizardCreateModel>();

                var arp = BackendConfigurationPnDbContext!.AreaRulePlannings
                    .First(x => x.Id == model.Id);
                arp.StartDate = model.StartDate;
                BackendConfigurationPnDbContext.SaveChanges();

                var planning = ItemsPlanningPnDbContext!.Plannings
                    .First(x => x.Id == arp.ItemPlanningId);
                var anchor = arp.StartDate!.Value;
                planning.StartDate = new DateTime(
                    anchor.Year, anchor.Month, anchor.Day, 0, 0, 0, DateTimeKind.Utc);
                planning.DayOfMonth = BackendConfigurationTaskWizardService
                    .DeriveDayOfMonth(model.RepeatType, planning.StartDate);
                planning.DayOfWeek = planning.StartDate.DayOfWeek;
                planning.RepeatType = (RepeatType)(int)model.RepeatType;
                planning.RepeatEvery = model.RepeatEvery;
                ItemsPlanningPnDbContext.SaveChanges();

                return Task.FromResult(new OperationResult(true));
            });
    }

    /// <summary>
    /// The batch service wired to the REAL calendar service (which in turn holds
    /// the REAL retraction and backfill services). Everything below the calendar
    /// service that talks to the cloud — deploy, reconciliation — stays
    /// substituted.
    /// </summary>
    private BackendConfigurationTaskListService BuildTaskListServiceWithRealCalendar()
    {
        var realCalendarService = new BackendConfigurationCalendarService(
            _localizationService,
            _userService,
            BackendConfigurationPnDbContext!,
            _coreHelper,
            _deployService,
            ItemsPlanningPnDbContext!,
            _taskWizardService,
            Substitute.For<ICalendarAssignmentReconciliationService>(),
            Substitute.For<ICalendarChangeNotifier>(),
            NullLogger<BackendConfigurationCalendarService>.Instance,
            _retractionService,
            _backfillService);

        return new BackendConfigurationTaskListService(
            _localizationService,
            _userService,
            BackendConfigurationPnDbContext!,
            ItemsPlanningPnDbContext!,
            realCalendarService,
            _taskWizardService,
            _retractionService,
            _backfillService,
            NullLogger<BackendConfigurationTaskListService>.Instance);
    }

    /// <summary>The deadlines the substituted deploy service was asked for, ascending.</summary>
    private List<DateTime> DeployedDeadlines() =>
        _deployService.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name
                        == nameof(IEventDeployService.EnsureComplianceForOccurrenceAsync))
            .Select(c => ((DateTime)c.GetArguments()[1]!).Date)
            .Distinct()
            .OrderBy(d => d)
            .ToList();

    // ─────────────────────────────────────────────────────────────────────────
    // The headline behaviour, through the REAL calendar service
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// "hvis jeg i dag 25.08.26 fx sætter startdato på en opgave til 01.01.2026
    /// med årlig frekvens, så skal der oprettes en rød opgave 01.01.2026."
    ///
    /// <see cref="ChangeStartDate_OverridesStartDateOriginalDateAndScope"/> proves
    /// the picked date REACHES UpdateTask, but its calendar service is a stub that
    /// returns success unconditionally — it would pass verbatim with the old
    /// CannotCreateTaskInThePast guard still in place, i.e. with the entire
    /// feature broken. This test drives the REAL service instead, so the guard's
    /// removal, the retract-vs-relocate gate, the scheduler neutralisation and the
    /// overdue materialisation are all on the hook.
    /// </summary>
    [Test]
    public async Task ChangeStartDate_PastAnchor_RealCalendarService_ReAnchorsAndBackfillsTheOverdueTask()
    {
        ConfigureWizardToPersistTheAnchor();

        var sdkSiteId = await SeedSdkSite();
        var seeded = await SeedTask(Today, repeatType: 4, siteIds: [sdkSiteId],
            complianceEnabled: true, dayOfMonth: Today.Day);
        var pastAnchor = Today.AddMonths(-7);

        var service = BuildTaskListServiceWithRealCalendar();
        var result = await service.ChangeStartDate(new TaskListBatchStartDateModel
        {
            TaskIds = [seeded.ArpId], StartDate = pastAnchor
        });

        Assert.That(result.Success, Is.True, result.Message);

        var arp = await ReloadArp(seeded.ArpId);
        var planning = await ItemsPlanningPnDbContext!.Plannings
            .AsNoTracking().FirstAsync(x => x.Id == seeded.PlanningId);

        Assert.Multiple(() =>
        {
            Assert.That(arp.StartDate!.Value.Date, Is.EqualTo(pastAnchor),
                "the past anchor must actually be persisted — the removed guard would have rejected the save outright");
            Assert.That(planning.StartDate.Date, Is.EqualTo(pastAnchor));
            Assert.That(DeployedDeadlines(), Is.EqualTo(new List<DateTime> { pastAnchor }),
                "a yearly series has exactly one occurrence in [anchor, today) — the anchor itself, the user's single red task");
            Assert.That(planning.NextExecutionTime, Is.Not.Null,
                "the scheduler must be neutralised, or SearchListJob back-deploys the missed occurrences one per hour");
            Assert.That(planning.LastExecutedTime, Is.Not.Null,
                "ExecuteCleanUp re-arms NextExecutionTime = null whenever LastExecutedTime is null");
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // OverdueToCreate must equal what the apply creates, with rows already there
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// OverdueToCreate used to be a bare (occurrences x sites) product, but the
    /// apply is retract-then-backfill: the retraction PRESERVES answered
    /// occurrences (R2) and EnsureComplianceForOccurrenceAsync's site-aware
    /// idempotence guard then reports Created = false for the (deadline, site)
    /// pairs they already cover. Preview said 6 where the apply created 5.
    ///
    /// The deploy substitute is replaced here by a stand-in for that REAL guard —
    /// a live Compliance row on the day whose backing SDK case belongs to the
    /// site short-circuits — so the assertion is not circular: nothing in the
    /// double consults the plan.
    /// </summary>
    [Test]
    public async Task Preview_OverdueCount_ExcludesPairsAnAnsweredOccurrenceAlreadyCovers()
    {
        var sdkSiteA = await SeedSdkSite();
        var sdkSiteB = await SeedSdkSite();
        var pastAnchor = Today.AddDays(-21);

        var seeded = await SeedTask(Today, repeatType: 2, siteIds: [sdkSiteA, sdkSiteB],
            complianceEnabled: true);

        // Answered occurrence on the -14 grid day, for site A only.
        await SeedDeployedOccurrence(
            seeded.PlanningId, Today.AddDays(-14), CompletedStatus, sdkSiteId: sdkSiteA);

        var planningId = seeded.PlanningId;
        _deployService.EnsureComplianceForOccurrenceAsync(
                Arg.Any<AreaRulePlanning>(), Arg.Any<DateTime>(), Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var day = ci.ArgAt<DateTime>(1).Date;
                var siteId = ci.ArgAt<int>(2);
                var caseIds = BackendConfigurationPnDbContext!.Compliances
                    .Where(c => c.PlanningId == planningId
                                && c.WorkflowState != Constants.WorkflowStates.Removed
                                && c.MicrotingSdkCaseId > 0
                                && c.Deadline >= day && c.Deadline < day.AddDays(1))
                    .Select(c => c.MicrotingSdkCaseId)
                    .ToList();
                var alreadyThere = caseIds.Count > 0
                                   && MicrotingDbContext!.Cases
                                       .Any(sc => caseIds.Contains(sc.Id) && sc.SiteId == siteId);
                return new EnsureComplianceResult
                {
                    Created = !alreadyThere, ComplianceId = 1, SdkCaseId = 1
                };
            });

        var preview = await _taskListService.ChangeStartDatePreview(new TaskListBatchStartDateModel
        {
            TaskIds = [seeded.ArpId], StartDate = pastAnchor
        });
        Assert.That(preview.Success, Is.True, preview.Message);

        // Now do what the apply does, in the apply's order and with the apply's
        // retraction bound.
        await _retractionService.RetractNonCompletedOccurrencesAsync(
            await ReloadArp(seeded.ArpId), pastAnchor);

        var planning = await ItemsPlanningPnDbContext!.Plannings.FirstAsync(x => x.Id == seeded.PlanningId);
        planning.StartDate = DateTime.SpecifyKind(pastAnchor, DateTimeKind.Utc);
        planning.DayOfWeek = pastAnchor.DayOfWeek;
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var arpRow = await BackendConfigurationPnDbContext!.AreaRulePlannings
            .FirstAsync(x => x.Id == seeded.ArpId);
        arpRow.StartDate = DateTime.SpecifyKind(pastAnchor, DateTimeKind.Utc);
        arpRow.DayOfWeek = (int)pastAnchor.DayOfWeek;
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var applied = await _backfillService.BackfillPastSeriesAsync(await ReloadArp(seeded.ArpId));

        Assert.Multiple(() =>
        {
            Assert.That(preview.Model.OverdueToCreate, Is.EqualTo(5),
                "3 weekly occurrences x 2 recipients, minus the one pair the answered occurrence already covers");
            Assert.That(applied.Created, Is.EqualTo(preview.Model.OverdueToCreate),
                "the preview promise and the apply must be the same number");
            Assert.That(applied.AlreadyPresent, Is.EqualTo(1));
            Assert.That(preview.Model.CompletedPreserved, Is.EqualTo(1));
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // The apply — the three overrides
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The whole action, reduced to its irreducible core: the picked date, a
    /// nulled OriginalDate and Scope="all" must ALL reach UpdateTask. Drop any
    /// one and the save becomes a silent no-op (StartDate → BuildUpdateModel's
    /// synthetic anchor wins; OriginalDate → dateChanged goes false and
    /// UpdateTask re-fetches the current anchor; Scope → the edit could be
    /// recorded as a per-occurrence exception instead of a re-anchor).
    /// </summary>
    [Test]
    public async Task ChangeStartDate_OverridesStartDateOriginalDateAndScope()
    {
        var seeded = await SeedTask(Today.AddDays(7), repeatType: 2, siteIds: [100]);
        var picked = Today.AddMonths(-7);

        var result = await _taskListService.ChangeStartDate(new TaskListBatchStartDateModel
        {
            TaskIds = [seeded.ArpId], StartDate = picked
        });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(_updateCalls, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(_updateCalls[0].Id, Is.EqualTo(seeded.ArpId));
            Assert.That(_updateCalls[0].StartDate, Is.EqualTo(picked),
                "the user's date must survive BuildUpdateModel's synthetic anchor");
            Assert.That(_updateCalls[0].OriginalDate, Is.Null,
                "a non-null OriginalDate makes UpdateTask discard the new date");
            Assert.That(_updateCalls[0].Scope, Is.EqualTo("all"));
        });
    }

    /// <summary>
    /// Everything BuildUpdateModel round-trips must still round-trip: this action
    /// changes the anchor and nothing else. A regression here would silently
    /// strip workers or the eForm off every task in the batch.
    /// </summary>
    [Test]
    public async Task ChangeStartDate_RoundTripsEverythingElse()
    {
        var seeded = await SeedTask(Today.AddDays(7), repeatType: 2, siteIds: [100, 101]);

        var result = await _taskListService.ChangeStartDate(new TaskListBatchStartDateModel
        {
            TaskIds = [seeded.ArpId], StartDate = Today.AddDays(30)
        });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(_updateCalls, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(_updateCalls[0].Sites, Is.EquivalentTo(new[] { 100, 101 }));
            Assert.That(_updateCalls[0].EformId, Is.EqualTo(7));
            Assert.That(_updateCalls[0].RepeatType, Is.EqualTo(2));
            Assert.That(_updateCalls[0].StartHour, Is.EqualTo(9.0));
        });
    }

    /// <summary>
    /// One unknown id among good ones must not abort the batch: the good task is
    /// still re-anchored and the failure is reported per task. Same
    /// RunPerTask/Aggregate contract the other batch actions have.
    /// </summary>
    [Test]
    public async Task ChangeStartDate_UnknownPlanningId_PartialFailure_GoodTaskStillApplied()
    {
        var seeded = await SeedTask(Today.AddDays(7), repeatType: 2, siteIds: [100]);
        const int unknownArpId = 999_999;

        var result = await _taskListService.ChangeStartDate(new TaskListBatchStartDateModel
        {
            TaskIds = [unknownArpId, seeded.ArpId], StartDate = Today.AddMonths(-2)
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, "one of two succeeded, so overall Success is true");
            Assert.That(result.Message, Does.Contain("1/2"));
            Assert.That(result.Message, Does.Contain("Task not found"));
        });
        Assert.That(_updateCalls, Has.Count.EqualTo(1));
        Assert.That(_updateCalls[0].Id, Is.EqualTo(seeded.ArpId));
    }

    /// <summary>
    /// The one batch-wide, pre-loop guard. An absent/unparsable `startDate` in
    /// the request body deserialises to default(DateTime) == 0001-01-01, which no
    /// picker can produce but which would re-anchor the series to year 1 and make
    /// the (uncapped, synchronous) backfill enumerate two millennia of
    /// occurrences. Mirroring Copy's pre-loop guards, it must fail BEFORE the
    /// loop so no partial batch is produced.
    /// </summary>
    [Test]
    public async Task ChangeStartDate_UnsetStartDate_RejectedBeforeAnyTaskIsTouched()
    {
        var seeded = await SeedTask(Today.AddDays(7), repeatType: 2, siteIds: [100]);

        var result = await _taskListService.ChangeStartDate(new TaskListBatchStartDateModel
        {
            TaskIds = [seeded.ArpId], StartDate = default
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Is.EqualTo("StartDateIsRequired"));
            Assert.That(_updateCalls, Is.Empty, "no task may be touched when the input is rejected");
        });
    }

    [Test]
    public async Task ChangeStartDatePreview_UnsetStartDate_IsRejected()
    {
        var seeded = await SeedTask(Today.AddDays(7), repeatType: 2, siteIds: [100]);

        var result = await _taskListService.ChangeStartDatePreview(new TaskListBatchStartDateModel
        {
            TaskIds = [seeded.ArpId], StartDate = default
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Is.EqualTo("StartDateIsRequired"));
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // The user's headline example — yearly into the past, compliance on/off
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// "hvis jeg i dag 25.08.26 fx sætter startdato på en opgave til 01.01.2026
    /// med årlig frekvens, så skal der oprettes en rød opgave 01.01.2026."
    ///
    /// Expressed relatively (see the fixture doc): a yearly series re-anchored
    /// seven months back has exactly one occurrence in [anchor, today) — the
    /// anchor itself — so exactly ONE overdue row per recipient is created. With
    /// a single recipient that is the user's single red task.
    /// </summary>
    [Test]
    public async Task Preview_YearlyIntoThePast_ComplianceOn_ReportsExactlyOneOverdue()
    {
        var pastAnchor = Today.AddMonths(-7);
        var seeded = await SeedTask(Today, repeatType: 4, siteIds: [100],
            complianceEnabled: true, dayOfMonth: Today.Day);

        var result = await _taskListService.ChangeStartDatePreview(new TaskListBatchStartDateModel
        {
            TaskIds = [seeded.ArpId], StartDate = pastAnchor
        });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.Multiple(() =>
        {
            Assert.That(result.Model.TaskCount, Is.EqualTo(1));
            Assert.That(result.Model.OverdueToCreate, Is.EqualTo(1),
                "one past occurrence (the new anchor) x one recipient");
        });
    }

    /// <summary>
    /// The other half of the acceptance criterion: compliance NEJ means the
    /// series simply re-anchors and runs from today — no red tasks exist for such
    /// an event at all, so none can be back-created. Identical seed to the test
    /// above apart from the flag, so the flag is provably what drives it.
    /// </summary>
    [Test]
    public async Task Preview_YearlyIntoThePast_ComplianceOff_ReportsNoOverdue()
    {
        var pastAnchor = Today.AddMonths(-7);
        var seeded = await SeedTask(Today, repeatType: 4, siteIds: [100],
            complianceEnabled: false, dayOfMonth: Today.Day);

        var result = await _taskListService.ChangeStartDatePreview(new TaskListBatchStartDateModel
        {
            TaskIds = [seeded.ArpId], StartDate = pastAnchor
        });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.Multiple(() =>
        {
            Assert.That(result.Model.TaskCount, Is.EqualTo(1));
            Assert.That(result.Model.OverdueToCreate, Is.Zero);
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Preview == apply
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// PAST case. The preview's retract/preserve split must equal what the write
    /// path actually does to the same rows — the projection and the write share
    /// one query and one completion predicate, and this is the test that would
    /// fail if someone ever forked them.
    ///
    /// Seeds three deployed weekly occurrences, one of them answered
    /// (Case.Status == 100). Re-anchoring into the past always takes the retract
    /// branch, so the two open rows go and the answered one is frozen (R2).
    /// </summary>
    [Test]
    public async Task Preview_PastAnchor_RetractCountsEqualWhatTheWriteActuallyDoes()
    {
        var seeded = await SeedTask(Today.AddDays(-28), repeatType: 2, siteIds: [100],
            complianceEnabled: true);
        await SeedDeployedOccurrence(seeded.PlanningId, Today.AddDays(-21), OpenStatus);
        await SeedDeployedOccurrence(seeded.PlanningId, Today.AddDays(-14), CompletedStatus);
        await SeedDeployedOccurrence(seeded.PlanningId, Today.AddDays(-7), OpenStatus);

        var preview = await _taskListService.ChangeStartDatePreview(new TaskListBatchStartDateModel
        {
            TaskIds = [seeded.ArpId], StartDate = Today.AddDays(-56)
        });
        Assert.That(preview.Success, Is.True, preview.Message);

        // Now do what the retract branch of UpdateTask does, on the same data.
        var applied = await _retractionService
            .RetractNonCompletedOccurrencesAsync(await ReloadArp(seeded.ArpId));

        Assert.Multiple(() =>
        {
            Assert.That(preview.Model.OccurrencesToRetract, Is.EqualTo(applied.Retracted));
            Assert.That(preview.Model.CompletedPreserved, Is.EqualTo(applied.CompletedPreserved));
            Assert.That(applied.Retracted, Is.EqualTo(2));
            Assert.That(applied.CompletedPreserved, Is.EqualTo(1));
            Assert.That(applied.Failed, Is.Zero);
        });
    }

    /// <summary>
    /// FUTURE case, different recurrence period. A weekly series moved four weeks
    /// out leaves its own week, so the apply retracts rather than relocates — and
    /// the preview must say so. Same equality assertion as the past case, on a
    /// path the past clause of the gate does not cover.
    /// </summary>
    [Test]
    public async Task Preview_FutureDifferentPeriod_RetractCountsEqualWhatTheWriteActuallyDoes()
    {
        var seeded = await SeedTask(Today, repeatType: 2, siteIds: [100], complianceEnabled: true);
        await SeedDeployedOccurrence(seeded.PlanningId, Today.AddDays(7), OpenStatus);
        await SeedDeployedOccurrence(seeded.PlanningId, Today.AddDays(14), CompletedStatus);

        var preview = await _taskListService.ChangeStartDatePreview(new TaskListBatchStartDateModel
        {
            TaskIds = [seeded.ArpId], StartDate = Today.AddDays(28)
        });
        Assert.That(preview.Success, Is.True, preview.Message);

        var applied = await _retractionService
            .RetractNonCompletedOccurrencesAsync(await ReloadArp(seeded.ArpId));

        Assert.Multiple(() =>
        {
            Assert.That(preview.Model.OccurrencesToRetract, Is.EqualTo(applied.Retracted));
            Assert.That(preview.Model.CompletedPreserved, Is.EqualTo(applied.CompletedPreserved));
            Assert.That(applied.Retracted, Is.EqualTo(1));
            Assert.That(applied.CompletedPreserved, Is.EqualTo(1));
            // A future re-anchor has no past range, so nothing is backfilled.
            Assert.That(preview.Model.OverdueToCreate, Is.Zero);
        });
    }

    /// <summary>
    /// The relocate branch. A weekly series moved WITHIN its own week (and still
    /// in the future) keeps its open occurrences — RelocateNonCompletedCompliance-
    /// RowsToNewPattern just moves their deadlines, the #960 fix. Nothing is
    /// retracted, so a preview that counted every open row regardless of branch
    /// would frighten the admin with a number that never materialises.
    ///
    /// The anchor moves from tomorrow to the day after: both fall in the same
    /// Monday-aligned week unless tomorrow is a Sunday, so the seed is nudged to
    /// keep the pair inside one week on every day of the year.
    /// </summary>
    [Test]
    public async Task Preview_FutureSamePeriod_ReportsNoRetractionBecauseTheApplyRelocates()
    {
        // Monday of next week, so "+1 day" is always still the same week.
        var nextMonday = Today.AddDays(1);
        while (nextMonday.DayOfWeek != DayOfWeek.Monday)
        {
            nextMonday = nextMonday.AddDays(1);
        }

        var seeded = await SeedTask(nextMonday, repeatType: 2, siteIds: [100], complianceEnabled: true);
        await SeedDeployedOccurrence(seeded.PlanningId, nextMonday, OpenStatus);
        await SeedDeployedOccurrence(seeded.PlanningId, nextMonday.AddDays(7), OpenStatus);

        var preview = await _taskListService.ChangeStartDatePreview(new TaskListBatchStartDateModel
        {
            TaskIds = [seeded.ArpId], StartDate = nextMonday.AddDays(2)
        });

        Assert.That(preview.Success, Is.True, preview.Message);
        Assert.Multiple(() =>
        {
            Assert.That(preview.Model.TaskCount, Is.EqualTo(1),
                "the task is still in the batch even though it only relocates");
            Assert.That(preview.Model.OccurrencesToRetract, Is.Zero);
            Assert.That(preview.Model.CompletedPreserved, Is.Zero);
            Assert.That(preview.Model.OverdueToCreate, Is.Zero);
        });
    }

    /// <summary>
    /// The overdue half of "preview counts match what the apply actually does".
    ///
    /// The preview projects the backfill against the PROSPECTIVE anchor; the
    /// apply runs it against the anchor the wizard has already persisted. This
    /// test performs that persist by hand — exactly the two writes
    /// BackendConfigurationTaskWizardService.UpdateTask makes (Planning.StartDate
    /// and Planning.DayOfMonth via DeriveDayOfMonth) — and then asserts the real
    /// backfill materialises precisely the number the preview promised.
    /// Two recipients, so the (occurrence x site) product is exercised rather
    /// than a count of dates.
    /// </summary>
    [Test]
    public async Task Preview_OverdueCount_EqualsWhatTheBackfillActuallyCreates()
    {
        var pastAnchor = Today.AddDays(-21);
        var seeded = await SeedTask(Today, repeatType: 2, siteIds: [100, 101], complianceEnabled: true);

        var preview = await _taskListService.ChangeStartDatePreview(new TaskListBatchStartDateModel
        {
            TaskIds = [seeded.ArpId], StartDate = pastAnchor
        });
        Assert.That(preview.Success, Is.True, preview.Message);

        // Persist the anchor exactly as the wizard would, then run the write.
        var planning = await ItemsPlanningPnDbContext!.Plannings.FirstAsync(x => x.Id == seeded.PlanningId);
        planning.StartDate = DateTime.SpecifyKind(pastAnchor, DateTimeKind.Utc);
        planning.DayOfWeek = pastAnchor.DayOfWeek;
        planning.DayOfMonth = BackendConfigurationTaskWizardService.DeriveDayOfMonth(
            BackendConfiguration.Pn.Infrastructure.Enums.RepeatType.Week, pastAnchor);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var arp = await BackendConfigurationPnDbContext!.AreaRulePlannings
            .FirstAsync(x => x.Id == seeded.ArpId);
        arp.StartDate = DateTime.SpecifyKind(pastAnchor, DateTimeKind.Utc);
        arp.DayOfWeek = (int)pastAnchor.DayOfWeek;
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var applied = await _backfillService.BackfillPastSeriesAsync(await ReloadArp(seeded.ArpId));

        Assert.Multiple(() =>
        {
            Assert.That(applied.Created, Is.EqualTo(preview.Model.OverdueToCreate));
            Assert.That(applied.Failed, Is.Zero);
            // 3 weekly occurrences in [-21d, today) x 2 recipients.
            Assert.That(preview.Model.OverdueToCreate, Is.EqualTo(6));
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Rows, not dates
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// THE counting trap. Compliance has no site column, so an occurrence
    /// deployed to two workers is two rows with the SAME rotation day — and the
    /// pair can disagree: one worker answered, the other did not. The write path
    /// walks rows, so it retracts one and preserves one. A preview counting
    /// DISTINCT DATES would report "1 occurrence retracted, 0 preserved" (or 0/1)
    /// and disagree with the apply on the very case the split exists for.
    ///
    /// The two rows differ only in time-of-day, because Compliances carries a
    /// UNIQUE index on (PlanningId, Deadline) — which is also exactly how the
    /// production rows for a shared day end up looking.
    /// </summary>
    [Test]
    public async Task Preview_MixedOccurrence_CountsRowsNotDates()
    {
        var sharedDay = Today.AddDays(-7);
        var seeded = await SeedTask(Today.AddDays(-28), repeatType: 2, siteIds: [100, 101],
            complianceEnabled: true);

        // Same DAY, two rows, one answered and one open.
        await SeedDeployedOccurrence(seeded.PlanningId, sharedDay.AddHours(9), CompletedStatus);
        await SeedDeployedOccurrence(seeded.PlanningId, sharedDay.AddHours(10), OpenStatus);

        var preview = await _taskListService.ChangeStartDatePreview(new TaskListBatchStartDateModel
        {
            TaskIds = [seeded.ArpId], StartDate = Today.AddDays(-56)
        });
        Assert.That(preview.Success, Is.True, preview.Message);

        var applied = await _retractionService
            .RetractNonCompletedOccurrencesAsync(await ReloadArp(seeded.ArpId));

        Assert.Multiple(() =>
        {
            Assert.That(preview.Model.OccurrencesToRetract, Is.EqualTo(1),
                "one ROW is open on that day, not zero and not the whole day");
            Assert.That(preview.Model.CompletedPreserved, Is.EqualTo(1),
                "the answered row on the same day is frozen (R2)");
            Assert.That(preview.Model.OccurrencesToRetract, Is.EqualTo(applied.Retracted));
            Assert.That(preview.Model.CompletedPreserved, Is.EqualTo(applied.CompletedPreserved));
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // The preview is read-only
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A preview that mutated anything would be a trap: the modal fires it on
    /// every debounced keystroke of the date picker, before the admin has
    /// committed to anything, and it runs on the SAME scoped DbContext the rest
    /// of the request uses — so a speculatively mutated entity left in the change
    /// tracker would be flushed by an unrelated later SaveChanges.
    ///
    /// Asserted over everything the apply would have touched: the series anchor,
    /// both scheduler fields (which the backfill writes) and every Compliance
    /// row's workflow state.
    /// </summary>
    [Test]
    public async Task Preview_WritesNothing()
    {
        var seeded = await SeedTask(Today.AddDays(-28), repeatType: 2, siteIds: [100, 101],
            complianceEnabled: true);
        await SeedDeployedOccurrence(seeded.PlanningId, Today.AddDays(-21), OpenStatus);
        await SeedDeployedOccurrence(seeded.PlanningId, Today.AddDays(-14), CompletedStatus);

        var planningBefore = await ItemsPlanningPnDbContext!.Plannings
            .AsNoTracking().FirstAsync(x => x.Id == seeded.PlanningId);
        var arpBefore = await ReloadArp(seeded.ArpId);
        var complianceBefore = await BackendConfigurationPnDbContext!.Compliances
            .AsNoTracking().Where(x => x.PlanningId == seeded.PlanningId)
            .OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.WorkflowState, x.Deadline })
            .ToListAsync();

        var preview = await _taskListService.ChangeStartDatePreview(new TaskListBatchStartDateModel
        {
            TaskIds = [seeded.ArpId], StartDate = Today.AddDays(-56)
        });
        Assert.That(preview.Success, Is.True, preview.Message);

        // Force a flush: if the preview left a speculatively mutated entity in
        // either tracker, THIS is what would persist it in production.
        await ItemsPlanningPnDbContext.SaveChangesAsync();
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var planningAfter = await ItemsPlanningPnDbContext.Plannings
            .AsNoTracking().FirstAsync(x => x.Id == seeded.PlanningId);
        var arpAfter = await ReloadArp(seeded.ArpId);
        var complianceAfter = await BackendConfigurationPnDbContext.Compliances
            .AsNoTracking().Where(x => x.PlanningId == seeded.PlanningId)
            .OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.WorkflowState, x.Deadline })
            .ToListAsync();

        Assert.Multiple(() =>
        {
            Assert.That(planningAfter.StartDate, Is.EqualTo(planningBefore.StartDate));
            Assert.That(planningAfter.DayOfMonth, Is.EqualTo(planningBefore.DayOfMonth));
            Assert.That(planningAfter.NextExecutionTime, Is.EqualTo(planningBefore.NextExecutionTime));
            Assert.That(planningAfter.LastExecutedTime, Is.EqualTo(planningBefore.LastExecutedTime));
            Assert.That(arpAfter.StartDate, Is.EqualTo(arpBefore.StartDate));
            Assert.That(arpAfter.DayOfWeek, Is.EqualTo(arpBefore.DayOfWeek));
            Assert.That(complianceAfter, Is.EqualTo(complianceBefore));
            // ...and the deploy service, the only thing that could create a red
            // task, was never invoked.
            Assert.That(
                _deployService.ReceivedCalls().Any(c => c.GetMethodInfo().Name
                    == nameof(IEventDeployService.EnsureComplianceForOccurrenceAsync)),
                Is.False);
        });
    }

    /// <summary>
    /// Aggregation across a selection: three tasks, and the counts are sums over
    /// the ones that actually take the retract branch. The relocating task
    /// contributes to TaskCount only, and the unknown id contributes to nothing —
    /// the apply reports it as a per-task failure and changes nothing for it, so
    /// counting it would over-promise.
    /// </summary>
    [Test]
    public async Task Preview_AggregatesAcrossSelection_AndSkipsIneligibleIds()
    {
        var past = await SeedTask(Today.AddDays(-28), repeatType: 2, siteIds: [100],
            complianceEnabled: true);
        await SeedDeployedOccurrence(past.PlanningId, Today.AddDays(-21), OpenStatus);

        var alsoPast = await SeedTask(Today.AddDays(-28), repeatType: 2, siteIds: [100],
            complianceEnabled: false);
        await SeedDeployedOccurrence(alsoPast.PlanningId, Today.AddDays(-20), OpenStatus);
        await SeedDeployedOccurrence(alsoPast.PlanningId, Today.AddDays(-13), CompletedStatus);

        var preview = await _taskListService.ChangeStartDatePreview(new TaskListBatchStartDateModel
        {
            TaskIds = [past.ArpId, alsoPast.ArpId, 999_999], StartDate = Today.AddDays(-56)
        });

        Assert.That(preview.Success, Is.True, preview.Message);
        Assert.Multiple(() =>
        {
            Assert.That(preview.Model.TaskCount, Is.EqualTo(2), "the unknown id is not counted");
            Assert.That(preview.Model.OccurrencesToRetract, Is.EqualTo(2));
            Assert.That(preview.Model.CompletedPreserved, Is.EqualTo(1));
            // 8 weekly occurrences in [-56d, today) x 1 site, for the
            // compliance-ON task only; the compliance-OFF one adds nothing.
            Assert.That(preview.Model.OverdueToCreate, Is.EqualTo(8));
        });
    }
}
