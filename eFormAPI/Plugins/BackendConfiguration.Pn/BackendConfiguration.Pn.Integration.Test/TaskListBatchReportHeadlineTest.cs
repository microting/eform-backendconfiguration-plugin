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
using BackendConfiguration.Pn.Infrastructure.Enums;
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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using NSubstitute;
using BcPlanningSite = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.PlanningSite;
using IpPlanningSite = Microting.ItemsPlanningBase.Infrastructure.Data.Entities.PlanningSite;
using IpRepeatType = Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType;
using SdkSite = Microting.eForm.Infrastructure.Data.Entities.Site;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// #1298 — "Skift rapportoverskrift": the <c>ChangeReportHeadline</c> batch
/// action on <see cref="BackendConfigurationTaskListService"/>, and the task
/// wizard divergence it would otherwise have exposed.
///
/// The report headline is stored THREE times and the two reports read
/// different copies:
///   * <c>AreaRulePlanning.ItemPlanningTagId</c> — the Compliance Rapport
///     groups by it;
///   * <c>Planning.ReportGroupPlanningTagId</c> — the old report
///     (<c>GenerateReportV2</c>) groups by it;
///   * a live items-planning <c>PlanningsTags</c> row for the headline tag.
/// Only <c>BackendConfigurationTaskWizardService.UpdateTask</c> writes all
/// three, and only the calendar service routes to it — so, like
/// <see cref="TaskListRenameTest"/>, this fixture wires the REAL calendar
/// service to the REAL wizard and asserts on DB rows in both databases. A
/// substituted calendar service would prove nothing about the agreement.
///
/// THE BUG (pre-#1298): the wizard wrote the ARP column in every status branch
/// but <c>Planning.ReportGroupPlanningTagId</c> only in <c>false→true</c> and
/// <c>true→true</c>. A task that is inactive and stays inactive, or one being
/// deactivated, kept its OLD Planning headline, so the two reports disagreed.
/// The inactive-task tests below fail on that code.
///
/// Substitutions (none can write a headline): <see cref="IEventDeployService"/>,
/// <see cref="ICalendarAssignmentReconciliationService"/>,
/// <see cref="ICalendarChangeNotifier"/> and the past-series backfill (only the
/// re-anchor branch reaches it, and this action round-trips the anchor). The
/// occurrence-retraction service is REAL, as in
/// <see cref="TaskWizardDeactivateRetractionTests"/>, because the
/// <c>true→false</c> wizard branch calls it.
///
/// NO CASE DEPLOYMENT HAPPENS: an ACTIVE task's items-planning PlanningSite set
/// mirrors its BC set, so the still-active branch computes <c>sitesToAdd = []</c>
/// and never calls <c>PairItemWithSiteHelper.Pair</c>; an inactive task deploys
/// nothing by construction. No Compliances rows are seeded, so the UNIQUE index
/// on (PlanningId, Deadline) is not in play. Every date derives from one
/// <c>_today</c> snapshot.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class TaskListBatchReportHeadlineTest : TestBaseSetup
{
    private DateTime _today;
    private int _languageId;

    private BackendConfigurationTaskListService _taskListService = null!;
    private BackendConfigurationTaskWizardService _wizard = null!;

    [SetUp]
    public async Task SetupReportHeadlineFixture()
    {
        _today = DateTime.UtcNow.Date;

        // FK-ordered cleanup — same order, for the same reasons, as
        // TaskListRenameTest (the base [SetUp] seeds only once per fixture, so a
        // child cleared after its parent aborts every later test with 1451).
        await BackendConfigurationPnDbContext!.Database
            .ExecuteSqlRawAsync("DELETE FROM `AreaRulePlanningWorkerTags`;");

        BackendConfigurationPnDbContext.Compliances.RemoveRange(
            BackendConfigurationPnDbContext.Compliances);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.AreaRulePlanningTags.RemoveRange(
            BackendConfigurationPnDbContext.AreaRulePlanningTags);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

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

        BackendConfigurationPnDbContext.PropertyWorkers.RemoveRange(
            BackendConfigurationPnDbContext.PropertyWorkers);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.Areas.RemoveRange(
            BackendConfigurationPnDbContext.Areas);
        BackendConfigurationPnDbContext.Properties.RemoveRange(
            BackendConfigurationPnDbContext.Properties);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        // → Plannings (RESTRICT). The wizard writes name translations when a
        // task is (re)activated or still active. PlanningSites and PlanningsTags
        // hang off Plannings with ON DELETE CASCADE.
        ItemsPlanningPnDbContext!.PlanningNameTranslation.RemoveRange(
            ItemsPlanningPnDbContext.PlanningNameTranslation);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        ItemsPlanningPnDbContext.Plannings.RemoveRange(
            ItemsPlanningPnDbContext.Plannings);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        // PlanningTags are left in place on purpose: every test creates its own
        // uniquely named tags, and nothing here counts the table.

        // A real SDK Languages row, so the wizard's language-id remap leaves
        // the round-tripped translations alone.
        var language = await MicrotingDbContext!.Languages.OrderBy(x => x.Id).FirstAsync();
        _languageId = language.Id;

        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        userService.GetCurrentUserLanguage().Returns(Task.FromResult(language));

        var core = await GetCore();
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));

        var retraction = new CalendarOccurrenceRetractionService(
            BackendConfigurationPnDbContext!, ItemsPlanningPnDbContext!, coreHelper,
            TestContextLogger<CalendarOccurrenceRetractionService>.Instance);

        _wizard = new BackendConfigurationTaskWizardService(
            new BackendConfigurationLocalizationService(),
            userService,
            BackendConfigurationPnDbContext!,
            coreHelper,
            ItemsPlanningPnDbContext!,
            Substitute.For<IEventDeployService>(),
            retraction,
            TestContextLogger<BackendConfigurationTaskWizardService>.Instance);

        var calendarService = new BackendConfigurationCalendarService(
            new BackendConfigurationLocalizationService(),
            userService,
            BackendConfigurationPnDbContext!,
            coreHelper,
            Substitute.For<IEventDeployService>(),
            ItemsPlanningPnDbContext!,
            _wizard,
            Substitute.For<ICalendarAssignmentReconciliationService>(),
            Substitute.For<ICalendarChangeNotifier>(),
            TestContextLogger<BackendConfigurationCalendarService>.Instance,
            retraction,
            Substitute.For<ICalendarPastSeriesBackfillService>(),
            new WorkerTagMembershipService(coreHelper));

        // Echoes the key back, so the rejection assertion can name the key.
        var localizationService = Substitute.For<IBackendConfigurationLocalizationService>();
        localizationService.GetString(Arg.Any<string>())
            .Returns(callInfo => (string)callInfo[0]);

        _taskListService = new BackendConfigurationTaskListService(
            localizationService,
            userService,
            BackendConfigurationPnDbContext!,
            ItemsPlanningPnDbContext!,
            calendarService,
            _wizard,
            retraction,
            Substitute.For<ICalendarPastSeriesBackfillService>(),
            TestContextLogger<BackendConfigurationTaskListService>.Instance);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Seeding
    // ─────────────────────────────────────────────────────────────────────────

    private sealed record Seeded(int ArpId, int PlanningId, int PropertyId, int FolderId, int SdkSiteId);

    /// <summary>
    /// A live items-planning PlanningTag — what the dialog's addTag creates
    /// through <c>api/items-planning-pn/tags</c>. BBB/YYY-style unique names, so
    /// nothing here depends on collation or on the seed's own tags.
    /// </summary>
    private async Task<int> SeedHeadline(string label, bool removed = false)
    {
        var tag = new PlanningTag
        {
            Name = $"{label}-{Guid.NewGuid()}",
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await tag.Create(ItemsPlanningPnDbContext!);
        if (removed)
        {
            await tag.Delete(ItemsPlanningPnDbContext!);
        }
        return tag.Id;
    }

    /// <summary>
    /// Seeds a CreatedInGuide task whose headline is <paramref name="headlineTagId"/>
    /// in ALL THREE places (ARP column, Planning column, live PlanningsTags
    /// row) — the consistent starting state the wizard produces on create — or
    /// in none of them when it is null.
    ///
    /// An ACTIVE task gets an items-planning PlanningSite mirroring its BC one
    /// (so no deployment is attempted); an INACTIVE task gets none, which is
    /// the shape deactivation leaves behind.
    /// </summary>
    private async Task<Seeded> SeedTask(string tag, bool status, int? headlineTagId)
    {
        var folder = new Microting.eForm.Infrastructure.Data.Entities.Folder
        {
            Name = $"headline-{tag}-folder-{Guid.NewGuid()}", MicrotingUid = null,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext!.Folders.AddAsync(folder);
        await MicrotingDbContext.SaveChangesAsync();

        var sdkSite = new SdkSite
        {
            Name = $"headline-{tag}-site-{Guid.NewGuid()}", MicrotingUid = null,
            LanguageId = _languageId, WorkflowState = Constants.WorkflowStates.Created
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
            Name = $"Headline-{tag}-{Guid.NewGuid()}", ItemPlanningTagId = 0,
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

        await BackendConfigurationPnDbContext.AreaRuleTranslations.AddAsync(new AreaRuleTranslation
        {
            AreaRuleId = areaRule.Id, LanguageId = _languageId,
            Name = $"Headline task {tag}", Description = "description",
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var anchor = DateTime.SpecifyKind(_today.AddDays(7), DateTimeKind.Utc);

        var planning = new Planning
        {
            Enabled = status, RepeatEvery = 1, RepeatType = IpRepeatType.Week,
            StartDate = anchor, DayOfWeek = anchor.DayOfWeek, RelatedEFormId = 0,
            SdkFolderId = folder.Id, Description = "planning description",
            ReportGroupPlanningTagId = headlineTagId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        await ItemsPlanningPnDbContext.PlanningNameTranslation.AddAsync(new PlanningNameTranslation
        {
            PlanningId = planning.Id, LanguageId = _languageId, Name = $"Headline task {tag}",
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        if (headlineTagId.HasValue)
        {
            await ItemsPlanningPnDbContext.PlanningsTags.AddAsync(new PlanningsTags
            {
                PlanningId = planning.Id, PlanningTagId = headlineTagId.Value,
                WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
            });
            await ItemsPlanningPnDbContext.SaveChangesAsync();
        }

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = property.Id, AreaId = area.Id,
            ItemPlanningId = planning.Id, StartDate = anchor, Status = status,
            RepeatType = 2, RepeatEvery = 1, FolderId = folder.Id,
            ItemPlanningTagId = headlineTagId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(arp);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        await BackendConfigurationPnDbContext.PlanningSites.AddAsync(new BcPlanningSite
        {
            AreaRulePlanningsId = arp.Id, SiteId = sdkSite.Id,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        if (status)
        {
            await ItemsPlanningPnDbContext.PlanningSites.AddAsync(new IpPlanningSite
            {
                PlanningId = planning.Id, SiteId = sdkSite.Id,
                WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
            });
            await ItemsPlanningPnDbContext.SaveChangesAsync();
        }

        return new Seeded(arp.Id, planning.Id, property.Id, folder.Id, sdkSite.Id);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Readback — AsNoTracking, filtered exactly as the production readers are.
    // ─────────────────────────────────────────────────────────────────────────

    private sealed record HeadlineState(
        int? ArpHeadline, bool ArpStatus, int? PlanningHeadline, bool PlanningEnabled, int[] LivePlanningsTagIds);

    private async Task<HeadlineState> ReadState(Seeded seeded)
    {
        var arp = await BackendConfigurationPnDbContext!.AreaRulePlannings
            .AsNoTracking().FirstAsync(x => x.Id == seeded.ArpId);
        var planning = await ItemsPlanningPnDbContext!.Plannings
            .AsNoTracking().FirstAsync(x => x.Id == seeded.PlanningId);
        var liveTags = await ItemsPlanningPnDbContext.PlanningsTags
            .AsNoTracking()
            .Where(x => x.PlanningId == seeded.PlanningId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .Select(x => x.PlanningTagId)
            .ToArrayAsync();
        return new HeadlineState(arp.ItemPlanningTagId, arp.Status, planning.ReportGroupPlanningTagId,
            planning.Enabled, liveTags);
    }

    /// <summary>
    /// The three copies agree on <paramref name="expected"/>, and the OLD
    /// headline's PlanningsTags row is gone (so the Plannings list does not show
    /// both).
    /// </summary>
    private async Task AssertAllThreeAgree(Seeded seeded, int expected, int? previous, string because)
    {
        var state = await ReadState(seeded);
        Assert.Multiple(() =>
        {
            Assert.That(state.ArpHeadline, Is.EqualTo(expected),
                $"{because}: AreaRulePlanning.ItemPlanningTagId (Compliance Rapport)");
            Assert.That(state.PlanningHeadline, Is.EqualTo(expected),
                $"{because}: Planning.ReportGroupPlanningTagId (old report) must follow the ARP");
            Assert.That(state.LivePlanningsTagIds, Does.Contain(expected),
                $"{because}: a live PlanningsTags row for the new headline");
            if (previous.HasValue && previous.Value != expected)
            {
                Assert.That(state.LivePlanningsTagIds, Does.Not.Contain(previous.Value),
                    $"{because}: the old headline's PlanningsTags row is retired");
            }
        });
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 1. THE BATCH ACTION — active and inactive tasks alike
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// One batch over an ACTIVE and an INACTIVE task. Both end with the three
    /// copies agreeing. The inactive half is the regression: before #1298 the
    /// wizard's <c>false→false</c> branch left <c>Planning.ReportGroupPlanningTagId</c>
    /// on the old headline, so this test fails on the old code.
    /// </summary>
    [Test]
    public async Task ChangeReportHeadline_ActiveAndInactiveTasks_AllThreeCopiesAgree()
    {
        var oldHeadline = await SeedHeadline("BBB-old");
        var newHeadline = await SeedHeadline("YYY-new");
        var active = await SeedTask("active", status: true, headlineTagId: oldHeadline);
        var inactive = await SeedTask("inactive", status: false, headlineTagId: oldHeadline);

        var result = await _taskListService.ChangeReportHeadline(new TaskListBatchReportHeadlineModel
        {
            TaskIds = [active.ArpId, inactive.ArpId],
            ItemPlanningTagId = newHeadline
        });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Message, Is.EqualTo("Tasks updated"), "no per-task failure");
        await AssertAllThreeAgree(active, newHeadline, oldHeadline, "active task");
        await AssertAllThreeAgree(inactive, newHeadline, oldHeadline, "inactive task");
    }

    /// <summary>
    /// The action changes the headline and nothing else: an active task stays
    /// active (and enabled), an inactive one stays inactive (and disabled) —
    /// BuildUpdateModel round-trips Status.
    /// </summary>
    [Test]
    public async Task ChangeReportHeadline_LeavesStatusUntouched()
    {
        var oldHeadline = await SeedHeadline("BBB-old");
        var newHeadline = await SeedHeadline("YYY-new");
        var active = await SeedTask("active", status: true, headlineTagId: oldHeadline);
        var inactive = await SeedTask("inactive", status: false, headlineTagId: oldHeadline);

        var result = await _taskListService.ChangeReportHeadline(new TaskListBatchReportHeadlineModel
        {
            TaskIds = [active.ArpId, inactive.ArpId],
            ItemPlanningTagId = newHeadline
        });

        Assert.That(result.Success, Is.True, result.Message);
        var activeState = await ReadState(active);
        var inactiveState = await ReadState(inactive);
        Assert.Multiple(() =>
        {
            Assert.That(activeState.ArpStatus, Is.True);
            Assert.That(activeState.PlanningEnabled, Is.True);
            Assert.That(inactiveState.ArpStatus, Is.False);
            Assert.That(inactiveState.PlanningEnabled, Is.False);
        });
    }

    /// <summary>
    /// A headline created moments ago (the dialog's addTag path) works, also on
    /// tasks that had NO headline before — UpdateTags' "no old, new" branch —
    /// for an active and an inactive task.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task ChangeReportHeadline_NewlyCreatedTag_OnTaskWithoutHeadline_AllThreeAgree(bool status)
    {
        var task = await SeedTask("no-headline", status, headlineTagId: null);
        var created = await SeedHeadline("YYY-created-in-dialog");

        var result = await _taskListService.ChangeReportHeadline(new TaskListBatchReportHeadlineModel
        {
            TaskIds = [task.ArpId],
            ItemPlanningTagId = created
        });

        Assert.That(result.Success, Is.True, result.Message);
        await AssertAllThreeAgree(task, created, previous: null,
            status ? "active task, freshly created headline" : "inactive task, freshly created headline");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 2. VALIDATION — rejected BEFORE the loop, nothing written
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// An id that names no tag, and a SOFT-DELETED tag (what the Manage-tags
    /// dialog's delete leaves), are both refused by the pre-loop guard. Two
    /// tasks are submitted and NEITHER is touched, which a per-task check could
    /// not satisfy.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task ChangeReportHeadline_UnknownOrRemovedTag_IsRejectedBeforeAnyTaskIsTouched(bool removedTag)
    {
        var oldHeadline = await SeedHeadline("BBB-old");
        var first = await SeedTask("first", status: true, headlineTagId: oldHeadline);
        var second = await SeedTask("second", status: false, headlineTagId: oldHeadline);
        var badTagId = removedTag ? await SeedHeadline("YYY-removed", removed: true) : 987_654_321;

        var result = await _taskListService.ChangeReportHeadline(new TaskListBatchReportHeadlineModel
        {
            TaskIds = [first.ArpId, second.ArpId],
            ItemPlanningTagId = badTagId
        });

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo("SelectedReportHeadlineNotFound"));
        foreach (var seeded in new[] { first, second })
        {
            var state = await ReadState(seeded);
            Assert.Multiple(() =>
            {
                Assert.That(state.ArpHeadline, Is.EqualTo(oldHeadline));
                Assert.That(state.PlanningHeadline, Is.EqualTo(oldHeadline));
                Assert.That(state.LivePlanningsTagIds, Is.EquivalentTo(new[] { oldHeadline }));
            });
        }
    }

    /// <summary>
    /// Per-task failures keep the batch shape: the good task is re-headlined,
    /// the unknown id is reported by id.
    /// </summary>
    [Test]
    public async Task ChangeReportHeadline_UnknownTaskId_PartialFailure_TheGoodOneStillRuns()
    {
        var oldHeadline = await SeedHeadline("BBB-old");
        var newHeadline = await SeedHeadline("YYY-new");
        var good = await SeedTask("good", status: false, headlineTagId: oldHeadline);
        const int missingId = 987_654_321;

        var result = await _taskListService.ChangeReportHeadline(new TaskListBatchReportHeadlineModel
        {
            TaskIds = [missingId, good.ArpId],
            ItemPlanningTagId = newHeadline
        });

        Assert.That(result.Success, Is.True, "one of two succeeded");
        Assert.That(result.Message, Does.Contain("1/2"));
        Assert.That(result.Message, Does.Contain($"#{missingId}"));
        await AssertAllThreeAgree(good, newHeadline, oldHeadline, "the good task");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 3. THE WIZARD FIX — single-task edits, every status branch that was wrong
    // ═════════════════════════════════════════════════════════════════════════

    private TaskWizardCreateModel WizardModel(Seeded seeded, TaskWizardStatuses status, int headline) =>
        new()
        {
            Id = seeded.ArpId,
            PropertyId = seeded.PropertyId,
            FolderId = seeded.FolderId,
            EformId = 0,
            ItemPlanningTagId = headline,
            StartDate = DateTime.SpecifyKind(_today.AddDays(7), DateTimeKind.Utc),
            RepeatType = BackendConfiguration.Pn.Infrastructure.Enums.RepeatType.Week,
            RepeatEvery = 1,
            Status = status,
            Sites = [seeded.SdkSiteId],
            TagIds = [],
            Translates = [],
            ComplianceEnabled = false
        };

    /// <summary>
    /// REGRESSION, <c>false→false</c> ("nothing to do, but update tags"): a
    /// single-task edit of an inactive task that STAYS inactive. The old code
    /// moved the ARP and the PlanningsTags row but left
    /// <c>Planning.ReportGroupPlanningTagId</c> on the old headline — this
    /// fails on it.
    /// </summary>
    [Test]
    public async Task WizardUpdateTask_InactiveStaysInactive_SyncsPlanningHeadline()
    {
        var oldHeadline = await SeedHeadline("BBB-old");
        var newHeadline = await SeedHeadline("YYY-new");
        var seeded = await SeedTask("false-false", status: false, headlineTagId: oldHeadline);

        var result = await _wizard.UpdateTask(WizardModel(seeded, TaskWizardStatuses.NotActive, newHeadline));

        Assert.That(result.Success, Is.True, result.Message);
        await AssertAllThreeAgree(seeded, newHeadline, oldHeadline, "false→false");
        var state = await ReadState(seeded);
        Assert.That(state.ArpStatus, Is.False, "and the task stayed inactive");
    }

    /// <summary>
    /// REGRESSION, <c>true→false</c> (deactivation in the same save as a
    /// headline change): the old code never wrote the Planning column in this
    /// branch either.
    /// </summary>
    [Test]
    public async Task WizardUpdateTask_Deactivate_SyncsPlanningHeadline()
    {
        var oldHeadline = await SeedHeadline("BBB-old");
        var newHeadline = await SeedHeadline("YYY-new");
        var seeded = await SeedTask("true-false", status: true, headlineTagId: oldHeadline);

        var result = await _wizard.UpdateTask(WizardModel(seeded, TaskWizardStatuses.NotActive, newHeadline));

        Assert.That(result.Success, Is.True, result.Message);
        await AssertAllThreeAgree(seeded, newHeadline, oldHeadline, "true→false");
        var state = await ReadState(seeded);
        Assert.Multiple(() =>
        {
            Assert.That(state.ArpStatus, Is.False, "the task really was deactivated");
            Assert.That(state.PlanningEnabled, Is.False);
        });
    }

    /// <summary>
    /// Control, <c>true→true</c>: already correct before #1298 and must stay so.
    /// </summary>
    [Test]
    public async Task WizardUpdateTask_ActiveStaysActive_SyncsPlanningHeadline()
    {
        var oldHeadline = await SeedHeadline("BBB-old");
        var newHeadline = await SeedHeadline("YYY-new");
        var seeded = await SeedTask("true-true", status: true, headlineTagId: oldHeadline);

        var result = await _wizard.UpdateTask(WizardModel(seeded, TaskWizardStatuses.Active, newHeadline));

        Assert.That(result.Success, Is.True, result.Message);
        await AssertAllThreeAgree(seeded, newHeadline, oldHeadline, "true→true");
    }
}
