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

using System;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.TaskList;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskListService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService;
using BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;
using BackendConfiguration.Pn.Services.CalendarChangeNotification;
using BackendConfiguration.Pn.Services.EventDeployService;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Enums;
using NSubstitute;
using BcPlanningSite = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.PlanningSite;
using IpPlanningSite = Microting.ItemsPlanningBase.Infrastructure.Data.Entities.PlanningSite;
using SdkSite = Microting.eForm.Infrastructure.Data.Entities.Site;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// #1126 — inline rename of a task name from the task-list grid row, through
/// <see cref="BackendConfigurationTaskListService.Rename"/>.
///
/// WHY THE CALENDAR SERVICE AND THE TASK WIZARD ARE BOTH REAL HERE. The whole
/// point of the feature is that the name is stored TWICE and both copies stay
/// in sync:
///   * <c>AreaRuleTranslation.Name</c> — the read source of truth for the
///     task-list grid (BackendConfigurationCalendarService.Index resolves the
///     row title from it, matching the user's language with a first-row
///     fallback);
///   * <c>PlanningNameTranslation.Name</c> — what the items-planning Plannings
///     list renders.
/// Only <c>BackendConfigurationTaskWizardService.UpdateTask</c> writes both,
/// and only the calendar service routes to it. The sibling
/// <c>TaskListBatch*Test</c> fixtures substitute
/// <see cref="IBackendConfigurationCalendarService"/> and assert on the
/// CAPTURED update model, which is the right shape for actions whose contract
/// is "one field of the model changed" — but it would prove nothing at all
/// here: a captured model says nothing about whether two tables were written.
/// So this fixture wires the REAL calendar service to the REAL wizard and
/// asserts on DB ROWS in both databases.
///
/// WHAT IS SUBSTITUTED, AND WHY NONE OF IT CAN HIDE THE BUG:
///   * <see cref="IEventDeployService"/> — deployment/repair, never a
///     translation writer. A pure rename does not deploy anything.
///   * <see cref="ICalendarAssignmentReconciliationService"/> — assignment
///     reconciliation only.
///   * <see cref="ICalendarPastSeriesBackfillService"/> — only reached by the
///     #1122 re-anchor branch, which a rename never takes (the anchor is
///     round-tripped unchanged).
///   * <see cref="IUserService"/> — supplies UserId and the caller's language.
///     The language it returns is a REAL row from the SDK Languages table, so
///     AreaRuleLanguageHelper's existence-based remap leaves the ids alone and
///     the test is not silently exercising the remap fallback.
/// The occurrence-retraction service is REAL for the same reason as in
/// <see cref="TaskWizardDeactivateRetractionTests"/>.
///
/// NO CASE DEPLOYMENT HAPPENS. Each seeded task's items-planning PlanningSite
/// set already matches its BC PlanningSite set, so the wizard's still-active
/// branch computes <c>sitesToAdd = []</c> and never calls
/// <c>PairItemWithSiteHelper.Pair</c> — which is what would otherwise try to
/// reach an eform-core consumer that does not exist in CI.
///
/// Every date is derived from a single <c>_today</c> snapshot taken in SetUp;
/// no absolute dates (they rot, and a hardcoded past date silently changes
/// which branch the wizard takes). No <c>Compliances</c> rows are seeded, so
/// the UNIQUE index on (PlanningId, Deadline) is not in play.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class TaskListRenameTest : TestBaseSetup
{
    /// <summary>Snapshotted once per test — never re-derived mid-test.</summary>
    private DateTime _today;

    /// <summary>The caller's language: a real SDK Languages row.</summary>
    private int _userLanguageId;

    /// <summary>A DIFFERENT real SDK language, used to prove other translations survive.</summary>
    private int _otherLanguageId;

    private BackendConfigurationTaskListService _taskListService = null!;

    [SetUp]
    public async Task SetupRenameFixture()
    {
        _today = DateTime.UtcNow.Date;

        // ══ FK-ORDERED CLEANUP — the order below is load-bearing ═══════════
        // Every foreign key named in the comments is ON DELETE RESTRICT in the
        // schema these tests actually run against (built from the entity model
        // by the base [SetUp]). The base [SetUp] bootstraps and seeds
        // only ONCE PER FIXTURE, so rows seeded by test N are still present
        // when test N+1 cleans up. A child table cleared AFTER its parent
        // therefore does not merely leak state: it aborts [SetUp] with
        // MySqlException 1451 (RowIsReferenced2) and every test after the
        // first fails before reaching a single assertion. Do not "tidy" this
        // into alphabetical or seeding order.

        // AreaRulePlanningWorkerTags → AreaRulePlannings (RESTRICT). Raw SQL
        // because this table is in no seed file's TRUNCATE list: the base
        // [SetUp] never empties it, so its rows outlive the
        // AreaRulePlannings whose ids restart at 1. It must therefore run
        // BEFORE the AreaRulePlannings delete below — same guard, and same
        // position, as CalendarUpdateTaskRetractGateTests.
        await BackendConfigurationPnDbContext!.Database
            .ExecuteSqlRawAsync("DELETE FROM `AreaRulePlanningWorkerTags`;");

        // No FK of their own, but Compliances carry PropertyId/AreaId/
        // PlanningId, so a surviving row would dangle off a Property this
        // method is about to delete (same reasoning as CalendarComplianceMoveTests).
        BackendConfigurationPnDbContext.Compliances.RemoveRange(
            BackendConfigurationPnDbContext.Compliances);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        // → AreaRulePlannings (RESTRICT).
        BackendConfigurationPnDbContext.AreaRulePlanningTags.RemoveRange(
            BackendConfigurationPnDbContext.AreaRulePlanningTags);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        // BC PlanningSites → AreaRulePlannings (RESTRICT). SeedTask seeds one
        // per task.
        BackendConfigurationPnDbContext.PlanningSites.RemoveRange(
            BackendConfigurationPnDbContext.PlanningSites);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        // → AreaRulePlannings. Not seeded here, but the calendar service under
        // test upserts one per renamed task.
        BackendConfigurationPnDbContext.CalendarConfigurations.RemoveRange(
            BackendConfigurationPnDbContext.CalendarConfigurations);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        // → AreaRules (RESTRICT).
        BackendConfigurationPnDbContext.AreaRulePlannings.RemoveRange(
            BackendConfigurationPnDbContext.AreaRulePlannings);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        // AreaRuleTranslation → AreaRule is DeleteBehavior.Restrict, so the
        // children must go first (the comment CalendarComplianceMoveTests
        // carries for the same line).
        BackendConfigurationPnDbContext.AreaRuleTranslations.RemoveRange(
            BackendConfigurationPnDbContext.AreaRuleTranslations);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        // → Areas (RESTRICT). AreaRules → Properties is ON DELETE CASCADE, so
        // this line is ordered against Areas, not against Properties.
        BackendConfigurationPnDbContext.AreaRules.RemoveRange(
            BackendConfigurationPnDbContext.AreaRules);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        // → Properties (RESTRICT): FK_PropertyWorkers_Properties_PropertyId —
        // the constraint that made every test after the first die in [SetUp].
        // SeedTask no longer seeds a PropertyWorker (nothing on the rename
        // path reads one — PropertyWorkers is only consulted by Copy's
        // target-property guard and ToggleComplete's worker guard), but this
        // clear stays: it is one statement, and it is what stops a future
        // seed from silently reintroducing the same fixture-wide failure.
        BackendConfigurationPnDbContext.PropertyWorkers.RemoveRange(
            BackendConfigurationPnDbContext.PropertyWorkers);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.Areas.RemoveRange(
            BackendConfigurationPnDbContext.Areas);
        BackendConfigurationPnDbContext.Properties.RemoveRange(
            BackendConfigurationPnDbContext.Properties);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        // → Plannings (RESTRICT): FK_PlanningNameTranslation_Plannings_PlanningId.
        // SeedTask seeds TWO of these per task and the wizard under test writes
        // more — this fixture is the only one in the suite that populates the
        // table at all, which is why no sibling's cleanup lists it and why the
        // sibling ordering could not be copied verbatim. Its own children
        // (PlanningNameTranslationVersions) are ON DELETE CASCADE, as are the
        // items-planning PlanningSites and PlanningsTags that hang off
        // Plannings, so none of those need a line of their own.
        ItemsPlanningPnDbContext!.PlanningNameTranslation.RemoveRange(
            ItemsPlanningPnDbContext.PlanningNameTranslation);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        ItemsPlanningPnDbContext.Plannings.RemoveRange(
            ItemsPlanningPnDbContext.Plannings);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        // Real SDK language rows, so RemapCommonTranslationLanguageIdsAsync's
        // existence guard leaves every LanguageId untouched. Taking the second
        // row (rather than inventing an id) keeps the "other language survives"
        // assertion honest on any tenant's id numbering.
        var languages = await MicrotingDbContext!.Languages.OrderBy(x => x.Id).Take(2).ToListAsync();
        _userLanguageId = languages[0].Id;
        _otherLanguageId = languages[1].Id;

        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        userService.GetCurrentUserLanguage().Returns(Task.FromResult(languages[0]));

        var core = await GetCore();
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));

        // REAL — see the class comment. Substituting either of these two is
        // exactly what would make the dual-write assertion vacuous.
        var retraction = new CalendarOccurrenceRetractionService(
            BackendConfigurationPnDbContext!, ItemsPlanningPnDbContext!, coreHelper,
            NullLogger<CalendarOccurrenceRetractionService>.Instance);

        var wizard = new BackendConfigurationTaskWizardService(
            new BackendConfigurationLocalizationService(),
            userService,
            BackendConfigurationPnDbContext!,
            coreHelper,
            ItemsPlanningPnDbContext!,
            Substitute.For<IEventDeployService>(),
            retraction,
            NullLogger<BackendConfigurationTaskWizardService>.Instance);

        var calendarService = new BackendConfigurationCalendarService(
            new BackendConfigurationLocalizationService(),
            userService,
            BackendConfigurationPnDbContext!,
            coreHelper,
            Substitute.For<IEventDeployService>(),
            ItemsPlanningPnDbContext!,
            wizard,
            Substitute.For<ICalendarAssignmentReconciliationService>(),
            Substitute.For<ICalendarChangeNotifier>(),
            NullLogger<BackendConfigurationCalendarService>.Instance,
            retraction,
            Substitute.For<ICalendarPastSeriesBackfillService>());

        // Echoes the key back, matching the plugin's convention where
        // GetString("SomeKey") is itself the message under test — so the
        // empty-title assertions can name the key rather than a translation.
        var localizationService = Substitute.For<IBackendConfigurationLocalizationService>();
        localizationService.GetString(Arg.Any<string>())
            .Returns(callInfo => (string)callInfo[0]);

        _taskListService = new BackendConfigurationTaskListService(
            localizationService,
            userService,
            BackendConfigurationPnDbContext!,
            ItemsPlanningPnDbContext!,
            calendarService,
            wizard,
            retraction,
            Substitute.For<ICalendarPastSeriesBackfillService>(),
            NullLogger<BackendConfigurationTaskListService>.Instance);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Seeding
    // ─────────────────────────────────────────────────────────────────────────

    private sealed record Seeded(int ArpId, int PlanningId, int AreaRuleId);

    /// <summary>
    /// Seeds Folder → SdkSite → Area → Property → AreaRule(+two translations,
    /// CreatedInGuide) → Planning(+two name translations) → AreaRulePlanning,
    /// with the worker wired on BOTH sides (a BC PlanningSite, which
    /// BuildUpdateModel reads for <c>Sites</c>, and an items-planning
    /// PlanningSite, which is what makes the wizard's <c>sitesToAdd</c> empty
    /// so no case deployment is attempted).
    ///
    /// <paramref name="anchorDaysFromToday"/> is negative for an established
    /// (past-anchored) series — the case #1122 unblocked.
    /// </summary>
    private async Task<Seeded> SeedTask(
        string tag, string title, int anchorDaysFromToday = 7, bool status = true)
    {
        var folder = new Microting.eForm.Infrastructure.Data.Entities.Folder
        {
            Name = $"rename-{tag}-folder-{Guid.NewGuid()}", MicrotingUid = null,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext!.Folders.AddAsync(folder);
        await MicrotingDbContext.SaveChangesAsync();

        var sdkSite = new SdkSite
        {
            Name = $"rename-{tag}-site-{Guid.NewGuid()}", MicrotingUid = null,
            LanguageId = _userLanguageId, WorkflowState = Constants.WorkflowStates.Created
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
            Name = $"Rename-{tag}-{Guid.NewGuid()}", ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.Properties.AddAsync(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var areaRule = new AreaRule
        {
            AreaId = area.Id, PropertyId = property.Id, EformId = 0, FolderId = folder.Id,
            CreatedInGuide = true,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRules.AddAsync(areaRule);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        // Two languages: the caller's, and one that must come through the
        // rename untouched.
        await BackendConfigurationPnDbContext.AreaRuleTranslations.AddRangeAsync(
            new AreaRuleTranslation
            {
                AreaRuleId = areaRule.Id, LanguageId = _userLanguageId,
                Name = title, Description = $"{title} description",
                WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
            },
            new AreaRuleTranslation
            {
                AreaRuleId = areaRule.Id, LanguageId = _otherLanguageId,
                Name = OtherLanguageName(title), Description = $"{title} other description",
                WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
            });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var anchor = DateTime.SpecifyKind(_today.AddDays(anchorDaysFromToday), DateTimeKind.Utc);

        var planning = new Planning
        {
            Enabled = status, RepeatEvery = 1, RepeatType = RepeatType.Week,
            StartDate = anchor, DayOfWeek = anchor.DayOfWeek, RelatedEFormId = 0,
            SdkFolderId = folder.Id, Description = $"{title} planning description",
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        await ItemsPlanningPnDbContext.PlanningNameTranslation.AddRangeAsync(
            new PlanningNameTranslation
            {
                PlanningId = planning.Id, LanguageId = _userLanguageId, Name = title,
                WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
            },
            new PlanningNameTranslation
            {
                PlanningId = planning.Id, LanguageId = _otherLanguageId, Name = OtherLanguageName(title),
                WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
            });
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = property.Id, AreaId = area.Id,
            ItemPlanningId = planning.Id, StartDate = anchor, Status = status,
            RepeatType = 2, RepeatEvery = 1, FolderId = folder.Id,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(arp);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        // No PropertyWorker is seeded on purpose. Nothing the rename path
        // touches reads that table — BuildUpdateModel takes Sites from the BC
        // PlanningSites below, and PropertyWorkers is only consulted by Copy's
        // target-property guard and ToggleComplete's worker guard, neither of
        // which this fixture exercises. Seeding one bought nothing and cost
        // the whole fixture: PropertyWorker → Property is ON DELETE RESTRICT,
        // so the leftover row made [SetUp]'s Properties delete fail for every
        // test after the first.
        await BackendConfigurationPnDbContext.PlanningSites.AddAsync(new BcPlanningSite
        {
            AreaRulePlanningsId = arp.Id, SiteId = sdkSite.Id,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        // Mirrors the BC PlanningSite above, so the wizard's still-active branch
        // computes sitesToAdd = [] and skips PairItemWithSiteHelper.Pair.
        await ItemsPlanningPnDbContext.PlanningSites.AddAsync(new IpPlanningSite
        {
            PlanningId = planning.Id, SiteId = sdkSite.Id,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        return new Seeded(arp.Id, planning.Id, areaRule.Id);
    }

    private static string OtherLanguageName(string title) => $"{title} (other language)";

    // ─────────────────────────────────────────────────────────────────────────
    // Readback helpers — always AsNoTracking, and always filtered exactly as
    // the production readers filter (non-removed only).
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<string> AreaRuleName(int areaRuleId, int languageId) =>
        await BackendConfigurationPnDbContext!.AreaRuleTranslations
            .AsNoTracking()
            .Where(x => x.AreaRuleId == areaRuleId && x.LanguageId == languageId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .Select(x => x.Name)
            .FirstAsync();

    private async Task<string> PlanningName(int planningId, int languageId) =>
        await ItemsPlanningPnDbContext!.PlanningNameTranslation
            .AsNoTracking()
            .Where(x => x.PlanningId == planningId && x.LanguageId == languageId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .Select(x => x.Name)
            .FirstAsync();

    private async Task<int> AreaRuleTranslationCount(int areaRuleId) =>
        await BackendConfigurationPnDbContext!.AreaRuleTranslations
            .AsNoTracking()
            .Where(x => x.AreaRuleId == areaRuleId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .CountAsync();

    private async Task<int> PlanningNameTranslationCount(int planningId) =>
        await ItemsPlanningPnDbContext!.PlanningNameTranslation
            .AsNoTracking()
            .Where(x => x.PlanningId == planningId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .CountAsync();

    private async Task<string> AreaRuleDescription(int areaRuleId, int languageId) =>
        await BackendConfigurationPnDbContext!.AreaRuleTranslations
            .AsNoTracking()
            .Where(x => x.AreaRuleId == areaRuleId && x.LanguageId == languageId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .Select(x => x.Description)
            .FirstAsync();

    // ═════════════════════════════════════════════════════════════════════════
    // 1. THE CORE CONTRACT — both tables, the caller's language only.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The assertion the whole issue turns on: one Rename call must move BOTH
    /// AreaRuleTranslation.Name (what the grid reads) and
    /// PlanningNameTranslation.Name (what the items-planning Plannings list
    /// reads) to the new title, for the CALLER's language and no other.
    /// </summary>
    [Test]
    public async Task Rename_WritesBothTranslationTables_ForTheCallersLanguageOnly()
    {
        var seeded = await SeedTask("dual", "Original title");

        var result = await _taskListService.Rename(new TaskListRenameModel
        {
            TaskIds = [seeded.ArpId],
            Title = "Renamed inline"
        });

        Assert.That(result.Success, Is.True, result.Message);

        // Both tables moved…
        Assert.That(await AreaRuleName(seeded.AreaRuleId, _userLanguageId),
            Is.EqualTo("Renamed inline"),
            "AreaRuleTranslation.Name is what the task-list grid renders");
        Assert.That(await PlanningName(seeded.PlanningId, _userLanguageId),
            Is.EqualTo("Renamed inline"),
            "PlanningNameTranslation.Name is what the items-planning Plannings list renders");

        // …and the other language is untouched in BOTH of them.
        Assert.That(await AreaRuleName(seeded.AreaRuleId, _otherLanguageId),
            Is.EqualTo(OtherLanguageName("Original title")));
        Assert.That(await PlanningName(seeded.PlanningId, _otherLanguageId),
            Is.EqualTo(OtherLanguageName("Original title")));
    }

    /// <summary>
    /// A rename must UPDATE the caller's language row, never insert a second
    /// one. The wizard matches translations by LanguageId precisely because an
    /// Id-match once inserted a duplicate on every edit, leaving the original
    /// row still fronting reads (FirstOrDefault returns the lowest-Id row) —
    /// which would make a rename look like it silently did nothing.
    /// </summary>
    [Test]
    public async Task Rename_UpdatesInPlace_AndNeverAddsATranslationRow()
    {
        var seeded = await SeedTask("nodup", "Original title");

        var areaRuleRowsBefore = await AreaRuleTranslationCount(seeded.AreaRuleId);
        var planningRowsBefore = await PlanningNameTranslationCount(seeded.PlanningId);

        var result = await _taskListService.Rename(new TaskListRenameModel
        {
            TaskIds = [seeded.ArpId],
            Title = "Renamed once"
        });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(await AreaRuleTranslationCount(seeded.AreaRuleId), Is.EqualTo(areaRuleRowsBefore));
        Assert.That(await PlanningNameTranslationCount(seeded.PlanningId), Is.EqualTo(planningRowsBefore));
    }

    /// <summary>
    /// Rename replaces the NAME only. The description travels in the same
    /// CommonTranslationsModel entry, so a rename that built the entry from
    /// scratch instead of mutating the round-tripped one would silently blank it.
    /// </summary>
    [Test]
    public async Task Rename_PreservesTheDescriptionOfTheRenamedLanguage()
    {
        var seeded = await SeedTask("desc", "Original title");

        var result = await _taskListService.Rename(new TaskListRenameModel
        {
            TaskIds = [seeded.ArpId],
            Title = "Renamed with description intact"
        });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(await AreaRuleDescription(seeded.AreaRuleId, _userLanguageId),
            Is.EqualTo("Original title description"));
    }

    /// <summary>
    /// The server trims, so the stored name, the grid text and any exact-match
    /// assertion agree. Leading/trailing whitespace in a display name is never
    /// intentional.
    /// </summary>
    [Test]
    public async Task Rename_TrimsTheTitle()
    {
        var seeded = await SeedTask("trim", "Original title");

        var result = await _taskListService.Rename(new TaskListRenameModel
        {
            TaskIds = [seeded.ArpId],
            Title = "   Padded name   "
        });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(await AreaRuleName(seeded.AreaRuleId, _userLanguageId), Is.EqualTo("Padded name"));
        Assert.That(await PlanningName(seeded.PlanningId, _userLanguageId), Is.EqualTo("Padded name"));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 2. VALIDATION — rejected BEFORE the loop, so nothing is half-applied.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Empty/whitespace/null titles are refused by the pre-loop guard. The
    /// "before the loop" half is the part that matters and is asserted
    /// explicitly: TWO tasks are submitted and NEITHER is touched, so the guard
    /// cannot be satisfied by a per-task check that would have renamed the
    /// first task before failing on validation.
    /// </summary>
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("\t\n ")]
    public async Task Rename_EmptyOrWhitespaceTitle_IsRejectedBeforeAnyTaskIsTouched(string title)
    {
        var first = await SeedTask("empty1", "First title");
        var second = await SeedTask("empty2", "Second title");

        var result = await _taskListService.Rename(new TaskListRenameModel
        {
            TaskIds = [first.ArpId, second.ArpId],
            Title = title
        });

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo("TaskNameIsRequired"));

        Assert.That(await AreaRuleName(first.AreaRuleId, _userLanguageId), Is.EqualTo("First title"));
        Assert.That(await PlanningName(first.PlanningId, _userLanguageId), Is.EqualTo("First title"));
        Assert.That(await AreaRuleName(second.AreaRuleId, _userLanguageId), Is.EqualTo("Second title"));
        Assert.That(await PlanningName(second.PlanningId, _userLanguageId), Is.EqualTo("Second title"));
    }

    /// <summary>
    /// A null Title is a real wire case — the JSON field simply absent — and is
    /// refused by the same pre-loop guard. It gets its own test rather than a
    /// fourth [TestCase] because NUnit1001 rejects a null literal for a
    /// non-nullable string parameter.
    /// </summary>
    [Test]
    public async Task Rename_NullTitle_IsRejectedBeforeAnyTaskIsTouched()
    {
        var seeded = await SeedTask("null", "Untouched title");

        var result = await _taskListService.Rename(new TaskListRenameModel
        {
            TaskIds = [seeded.ArpId],
            Title = null
        });

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo("TaskNameIsRequired"));
        Assert.That(await AreaRuleName(seeded.AreaRuleId, _userLanguageId), Is.EqualTo("Untouched title"));
        Assert.That(await PlanningName(seeded.PlanningId, _userLanguageId), Is.EqualTo("Untouched title"));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 3. EDGE CASES
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Submitting the CURRENT name is a legal request that must round-trip
    /// safely rather than throw or corrupt anything. The frontend suppresses
    /// this call (an unchanged value closes the editor without a request), but
    /// the API is public and must not depend on that.
    /// </summary>
    [Test]
    public async Task Rename_UnchangedValue_RoundTripsSafely()
    {
        var seeded = await SeedTask("same", "Unchanged title");

        var result = await _taskListService.Rename(new TaskListRenameModel
        {
            TaskIds = [seeded.ArpId],
            Title = "Unchanged title"
        });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(await AreaRuleName(seeded.AreaRuleId, _userLanguageId), Is.EqualTo("Unchanged title"));
        Assert.That(await PlanningName(seeded.PlanningId, _userLanguageId), Is.EqualTo("Unchanged title"));
        Assert.That(await AreaRuleName(seeded.AreaRuleId, _otherLanguageId),
            Is.EqualTo(OtherLanguageName("Unchanged title")));
        // The wizard's upsert skips the write entirely when nothing differs, so
        // this also pins that "no write" does not mean "row removed".
        Assert.That(await AreaRuleTranslationCount(seeded.AreaRuleId), Is.EqualTo(2));
        Assert.That(await PlanningNameTranslationCount(seeded.PlanningId), Is.EqualTo(2));
    }

    /// <summary>
    /// THE #1122 UNBLOCK. Established series routinely have anchors months in
    /// the past, and until #1122 removed the CannotCreateTaskInThePast guard
    /// from UpdateTask, ANY edit of such a task — rename included — was
    /// rejected outright. This is the case the issue was blocked on, so it gets
    /// its own test with a deliberately deep past anchor rather than being
    /// folded into the happy path.
    /// </summary>
    [Test]
    public async Task Rename_PastDatedTask_Succeeds()
    {
        var seeded = await SeedTask("past", "Past anchored title", anchorDaysFromToday: -120);

        var result = await _taskListService.Rename(new TaskListRenameModel
        {
            TaskIds = [seeded.ArpId],
            Title = "Past anchored renamed"
        });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(await AreaRuleName(seeded.AreaRuleId, _userLanguageId),
            Is.EqualTo("Past anchored renamed"));
        Assert.That(await PlanningName(seeded.PlanningId, _userLanguageId),
            Is.EqualTo("Past anchored renamed"));

        // The rename must not have moved the series while it was at it:
        // BuildUpdateModel sends a synthetic future anchor, and UpdateTask's
        // dateChanged=false path is what puts the REAL anchor back. If that ever
        // regresses, a rename would silently re-anchor every established series.
        var anchorAfter = await BackendConfigurationPnDbContext!.AreaRulePlannings
            .AsNoTracking().Where(x => x.Id == seeded.ArpId).Select(x => x.StartDate).FirstAsync();
        Assert.That(anchorAfter!.Value.Date, Is.EqualTo(_today.AddDays(-120)),
            "a rename must not relocate the series anchor");
    }

    /// <summary>
    /// Partial failure: one good id and one that names no task at all. The good
    /// task is still renamed, the bad one is reported by id, and Aggregate's
    /// partial-failure shape (Success = ok > 0) is preserved.
    /// </summary>
    [Test]
    public async Task Rename_PartialFailure_RenamesTheGoodTaskAndReportsTheBadId()
    {
        var good = await SeedTask("partial", "Good title");
        const int missingId = 987654321;

        var result = await _taskListService.Rename(new TaskListRenameModel
        {
            TaskIds = [good.ArpId, missingId],
            Title = "Renamed despite the bad id"
        });

        // Aggregate: Success is true while at least one task succeeded.
        Assert.That(result.Success, Is.True);
        Assert.That(result.Message, Does.Contain("PartiallyCompleted"));
        Assert.That(result.Message, Does.Contain("1/2"));
        Assert.That(result.Message, Does.Contain($"#{missingId}"));

        Assert.That(await AreaRuleName(good.AreaRuleId, _userLanguageId),
            Is.EqualTo("Renamed despite the bad id"));
        Assert.That(await PlanningName(good.PlanningId, _userLanguageId),
            Is.EqualTo("Renamed despite the bad id"));
    }

    /// <summary>
    /// A task whose AreaRule is not CreatedInGuide is not on the task list at
    /// all, so the rename endpoint must not be usable to rename it — the same
    /// eligibility rule every other batch action enforces through
    /// BuildUpdateModel.
    /// </summary>
    [Test]
    public async Task Rename_IneligibleTask_IsReportedAsNotFoundAndLeftAlone()
    {
        var seeded = await SeedTask("ineligible", "Untouchable title");
        var rule = await BackendConfigurationPnDbContext!.AreaRules.FirstAsync(x => x.Id == seeded.AreaRuleId);
        rule.CreatedInGuide = false;
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var result = await _taskListService.Rename(new TaskListRenameModel
        {
            TaskIds = [seeded.ArpId],
            Title = "Should not land"
        });

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("Task not found"));
        Assert.That(await AreaRuleName(seeded.AreaRuleId, _userLanguageId), Is.EqualTo("Untouchable title"));
        Assert.That(await PlanningName(seeded.PlanningId, _userLanguageId), Is.EqualTo("Untouchable title"));
    }
}
