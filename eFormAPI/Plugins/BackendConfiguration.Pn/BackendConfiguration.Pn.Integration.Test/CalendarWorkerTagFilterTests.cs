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
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService;
using BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;
using BackendConfiguration.Pn.Services.CalendarChangeNotification;
using BackendConfiguration.Pn.Services.EventDeployService;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eForm.Infrastructure;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Enums;
using NSubstitute;
using BcPlanningSite = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.PlanningSite;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// #1212 — worker-tag ("team") awareness of the calendar's assignee filter, exercised
/// through the real week query
/// (<see cref="BackendConfigurationCalendarService.GetTasksForWeek"/>) rather than
/// against the private predicate, because the fix has two halves that only meet in
/// that method: the per-request expansion of the filter's sites to the worker tags
/// they belong to, and the OR inside <c>ShouldIncludeTask</c>.
///
/// <para>
/// The pre-fix defect: <c>ShouldIncludeTask</c> compared <c>filter.SiteIds</c> against
/// <c>task.AssigneeIds</c> and nothing else, and <c>AssigneeIds</c> is built from
/// explicit <c>PlanningSites</c> only. An event assigned to a worker tag therefore had
/// an EMPTY <c>AssigneeIds</c> and vanished the moment anyone filtered by a person —
/// including the team's own members. There was also no way to filter by team at all.
/// </para>
///
/// <para>
/// Fixture hygiene: <c>TestBaseSetup.ResetDatabasePerTest</c> is <c>false</c>, so rows
/// accumulate across the tests in this fixture. Every test seeds its OWN property,
/// SDK sites and SDK tag (GUID names, auto-generated ids) and every assertion is
/// scoped to the ids it seeded. No whole-table counts, no hard-coded ids.
/// </para>
///
/// <para>
/// Deliberately drives the RECURRENCE render path only (weekly ARP, no
/// <c>Compliance</c> rows). <c>Compliances</c> is UNIQUE on
/// <c>(PlanningId, Deadline)</c>, and the filter is shared by both render paths, so
/// seeding compliance rows would add a collision hazard without adding coverage.
/// </para>
///
/// All dates are fixed in 2026-06 (Mon 2026-06-01 .. Sun 2026-06-07), never relative
/// to now.
/// </summary>
[TestFixture]
public class CalendarWorkerTagFilterTests : TestBaseSetup
{
    private static readonly DateTime WeekMonday =
        new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    private static string IsoUtc(DateTime d) =>
        DateTime.SpecifyKind(d, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    /// <summary>
    /// <c>AreaRulePlanningWorkerTags</c> is in no seed file, so its rows survive every
    /// bootstrap while AreaRulePlanning ids can restart — the same accumulation guard
    /// <see cref="WorkerTagAssignmentTest"/> carries. NUnit runs the base-class
    /// [SetUp] first, so the contexts already exist here.
    /// </summary>
    [SetUp]
    public async Task ClearAccumulatingWorkerTagLinks()
    {
        await BackendConfigurationPnDbContext!.Database
            .ExecuteSqlRawAsync("DELETE FROM `AreaRulePlanningWorkerTags`;");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Seeding
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<Property> SeedProperty()
    {
        var property = new Property
        {
            Name = $"WorkerTagFilterProp-{Guid.NewGuid()}",
            ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Properties.AddAsync(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        return property;
    }

    /// <summary>
    /// Seeds a weekly event anchored on <see cref="WeekMonday"/> with no
    /// <c>RepeatWeekdaysCsv</c>, so the legacy single-day weekly path emits exactly
    /// one occurrence (the Monday) inside the queried week.
    /// </summary>
    private async Task<AreaRulePlanning> SeedWeeklyEvent(int propertyId)
    {
        var area = new Area
        {
            Type = AreaTypesEnum.Type1,
            ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Areas.AddAsync(area);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var areaRule = new AreaRule
        {
            AreaId = area.Id,
            PropertyId = propertyId,
            EformId = 0,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRules.AddAsync(areaRule);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var planning = new Planning
        {
            Enabled = true,
            RepeatEvery = 1,
            RepeatType = RepeatType.Week,
            StartDate = WeekMonday,
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
            AreaId = area.Id,
            ItemPlanningId = planning.Id,
            StartDate = WeekMonday,
            Status = true,
            RepeatType = 2,
            RepeatEvery = 1,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(arp);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        return arp;
    }

    /// <summary>Explicit (named-individual) assignment — the only kind the pre-fix filter could see.</summary>
    private async Task AssignSite(int arpId, int siteId)
    {
        await BackendConfigurationPnDbContext!.PlanningSites.AddAsync(new BcPlanningSite
        {
            AreaRulePlanningsId = arpId,
            SiteId = siteId,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();
    }

    /// <summary>Team assignment — an AreaRulePlanningWorkerTag link and NO PlanningSites row.</summary>
    private async Task AssignWorkerTag(int arpId, int tagId)
    {
        await BackendConfigurationPnDbContext!.AreaRulePlanningWorkerTags.AddAsync(new AreaRulePlanningWorkerTag
        {
            AreaRulePlanningId = arpId,
            TagId = tagId,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();
    }

    /// <param name="removed">
    /// Seeds the Site already soft-deleted, the state <c>Core.SiteDelete</c> leaves
    /// behind (<c>PnBase.Delete</c> only flips <c>WorkflowState</c>). No SiteWorker is
    /// created either way, so the resigned-worker clause is not what such a site is
    /// being excluded by.
    /// </param>
    private async Task<int> SeedSdkSite(bool removed = false)
    {
        var language = await MicrotingDbContext!.Languages.FirstAsync();
        var site = new Site
        {
            Name = $"site-{Guid.NewGuid()}",
            MicrotingUid = null,
            LanguageId = language.Id,
            WorkflowState = removed
                ? Constants.WorkflowStates.Removed
                : Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddAsync(site);
        await MicrotingDbContext.SaveChangesAsync();
        return site.Id;
    }

    /// <summary>An SDK Tag is what the product calls a worker tag / team.</summary>
    private async Task<int> SeedSdkWorkerTag()
    {
        var tag = new Tag
        {
            Name = $"team-{Guid.NewGuid()}",
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext!.Tags.AddAsync(tag);
        await MicrotingDbContext.SaveChangesAsync();
        return tag.Id;
    }

    private async Task LinkSiteToTag(int tagId, int siteId, bool removed = false)
    {
        await MicrotingDbContext!.SiteTags.AddAsync(new SiteTag
        {
            TagId = tagId,
            SiteId = siteId,
            WorkflowState = removed
                ? Constants.WorkflowStates.Removed
                : Constants.WorkflowStates.Created
        });
        await MicrotingDbContext.SaveChangesAsync();
    }

    /// <summary>
    /// Site + Worker + SiteWorker triple, with the worker's <c>Resigned</c> flag set as
    /// asked. Must be seeded through an SDK context handed out AFTER the Core has run
    /// its migrations — <c>SQL/420_SDK.sql</c> creates <c>Workers</c> without
    /// <c>Resigned</c>. Same shape as <c>WorkerTagAssignmentTest.SeedSdkSiteWithWorker</c>.
    /// </summary>
    private static async Task<int> SeedSdkSiteWithWorker(MicrotingDbContext sdkDbContext, bool resigned)
    {
        var language = await sdkDbContext.Languages.FirstAsync();
        var site = new Site
        {
            Name = $"site-{Guid.NewGuid()}",
            MicrotingUid = null,
            LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await sdkDbContext.Sites.AddAsync(site);
        await sdkDbContext.SaveChangesAsync();

        var worker = new Worker
        {
            FirstName = $"member-{Guid.NewGuid():N}",
            LastName = "Worker",
            Email = $"{Guid.NewGuid():N}@example.test",
            Resigned = resigned,
            ResignedAtDate = resigned ? DateTime.UtcNow.AddDays(-1) : default,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await sdkDbContext.Workers.AddAsync(worker);
        await sdkDbContext.SaveChangesAsync();

        await sdkDbContext.SiteWorkers.AddAsync(new SiteWorker
        {
            SiteId = site.Id,
            WorkerId = worker.Id,
            WorkflowState = Constants.WorkflowStates.Created
        });
        await sdkDbContext.SaveChangesAsync();

        return site.Id;
    }

    private async Task<int> SeedPlanningTag(string name)
    {
        var tag = new PlanningTag
        {
            Name = name,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.PlanningTags.AddAsync(tag);
        await ItemsPlanningPnDbContext.SaveChangesAsync();
        return tag.Id;
    }

    private async Task LinkPlanningTag(int arpId, int planningTagId)
    {
        await BackendConfigurationPnDbContext!.AreaRulePlanningTags.AddAsync(new AreaRulePlanningTag
        {
            AreaRulePlanningId = arpId,
            ItemPlanningTagId = planningTagId,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Service under test
    // ─────────────────────────────────────────────────────────────────────────

    private BackendConfigurationCalendarService BuildCalendarService(eFormCore.Core core)
    {
        var language = MicrotingDbContext!.Languages.OrderBy(x => x.Id).First();
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        userService.GetCurrentUserLanguage().Returns(Task.FromResult(language));
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));
        return new BackendConfigurationCalendarService(
            new BackendConfigurationLocalizationService(), userService,
            BackendConfigurationPnDbContext!, coreHelper, Substitute.For<IEventDeployService>(),
            ItemsPlanningPnDbContext!, Substitute.For<IBackendConfigurationTaskWizardService>(),
            Substitute.For<ICalendarAssignmentReconciliationService>(),
            Substitute.For<ICalendarChangeNotifier>(),
            NullLogger<BackendConfigurationCalendarService>.Instance,
            Substitute.For<ICalendarOccurrenceRetractionService>(),
            Substitute.For<ICalendarPastSeriesBackfillService>(),
            Substitute.For<IBackendConfigurationComplianceReportService>());
    }

    /// <summary>
    /// Runs the real Mon-Sun week query and returns the DISTINCT AreaRulePlanning ids
    /// that rendered. Distinct ids, not tile counts: the assertions here are about
    /// which events survive the assignee filter, not how many occurrences each emits.
    /// </summary>
    private async Task<List<int>> QueryWeekIds(
        int propertyId, List<int>? siteIds = null, List<int>? workerTagIds = null,
        List<string>? tagNames = null)
    {
        var core = await GetCore();
        var svc = BuildCalendarService(core);
        var res = await svc.GetTasksForWeek(new CalendarTaskRequestModel
        {
            PropertyId = propertyId,
            WeekStart = IsoUtc(WeekMonday),
            WeekEnd = IsoUtc(WeekMonday.AddDays(6).AddHours(23).AddMinutes(59)),
            ActionableOnly = false,
            BoardIds = [],
            TagNames = tagNames ?? [],
            SiteIds = siteIds ?? [],
            WorkerTagIds = workerTagIds ?? []
        });
        Assert.That(res.Success, Is.True, res.Message);
        return res.Model!.Select(t => t.Id).Distinct().ToList();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 1 — team-assigned event found by a TEAM filter (the new capability)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Pre-fix this test cannot even compile — <c>CalendarTaskRequestModel</c> had no
    /// <c>WorkerTagIds</c>. Behaviourally, against a pre-fix service handed the same
    /// intent (an ignored tag filter, i.e. no filter at all), the assertion that FAILS
    /// is <c>Does.Not.Contain(individualEvent.Id)</c>: the individually-assigned
    /// control event would come back too, because a tag filter had no effect.
    /// </summary>
    [Test]
    public async Task TeamAssignedEvent_IsFoundByWorkerTagFilter()
    {
        var property = await SeedProperty();
        var teamTagId = await SeedSdkWorkerTag();
        var memberSiteId = await SeedSdkSite();
        await LinkSiteToTag(teamTagId, memberSiteId);

        var teamEvent = await SeedWeeklyEvent(property.Id);
        await AssignWorkerTag(teamEvent.Id, teamTagId);

        var individualEvent = await SeedWeeklyEvent(property.Id);
        await AssignSite(individualEvent.Id, await SeedSdkSite());

        var ids = await QueryWeekIds(property.Id, workerTagIds: [teamTagId]);

        Assert.That(ids, Does.Contain(teamEvent.Id),
            "an event assigned to a worker tag must be returned when filtering by that tag");
        Assert.That(ids, Does.Not.Contain(individualEvent.Id),
            "a worker-tag filter must still exclude events that carry neither that tag "
            + "nor an assignee in it — if this fails, WorkerTagIds is being ignored");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2 — THE REPRO: team-assigned event found by a MEMBER filter
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The bug from #1212, verbatim: assign an event to a team, then filter the
    /// employee list by one of that team's members. Pre-fix the assertion
    /// <c>Does.Contain(teamEvent.Id)</c> FAILS — the event's <c>AssigneeIds</c> is
    /// empty (no PlanningSites row), so <c>ShouldIncludeTask</c>'s
    /// <c>filter.SiteIds</c> intersection never matched and the event vanished.
    ///
    /// The <c>nonMemberSiteId</c> half is the positive control: it proves the site
    /// filter is still doing work and the event is found BECAUSE of the membership,
    /// not because filtering silently degraded to "return everything".
    /// </summary>
    [Test]
    public async Task TeamAssignedEvent_IsFoundByMemberSiteFilter()
    {
        var property = await SeedProperty();
        var teamTagId = await SeedSdkWorkerTag();
        var memberSiteId = await SeedSdkSite();
        var nonMemberSiteId = await SeedSdkSite();
        await LinkSiteToTag(teamTagId, memberSiteId);

        var teamEvent = await SeedWeeklyEvent(property.Id);
        await AssignWorkerTag(teamEvent.Id, teamTagId);

        var byMember = await QueryWeekIds(property.Id, siteIds: [memberSiteId]);
        Assert.That(byMember, Does.Contain(teamEvent.Id),
            "#1212 repro: filtering by a member of the assigned team must return the "
            + "team's event — pre-fix AssigneeIds was empty and the event disappeared");

        var byNonMember = await QueryWeekIds(property.Id, siteIds: [nonMemberSiteId]);
        Assert.That(byNonMember, Does.Not.Contain(teamEvent.Id),
            "positive control: a site outside the team must NOT see the team's event");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3 — individually-assigned events are unaffected
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Tripwire — passes on pre-fix code. Named-individual assignment is the path the
    /// pre-fix filter already handled correctly; this locks it against regression from
    /// the new OR branch (a sloppy fix that widened the site match, or that started
    /// requiring a worker tag, would break here).
    /// </summary>
    [Test]
    public async Task Tripwire_IndividuallyAssignedEvent_UnaffectedBySiteFilter()
    {
        var property = await SeedProperty();
        var assignedSiteId = await SeedSdkSite();
        var otherSiteId = await SeedSdkSite();

        var individualEvent = await SeedWeeklyEvent(property.Id);
        await AssignSite(individualEvent.Id, assignedSiteId);

        var teamTagId = await SeedSdkWorkerTag();
        var teamEvent = await SeedWeeklyEvent(property.Id);
        await AssignWorkerTag(teamEvent.Id, teamTagId);

        var byAssignee = await QueryWeekIds(property.Id, siteIds: [assignedSiteId]);
        Assert.That(byAssignee, Does.Contain(individualEvent.Id),
            "an explicitly assigned site must still find its own event");
        Assert.That(byAssignee, Does.Not.Contain(teamEvent.Id),
            "a site that belongs to no team must not pick up team-assigned events");

        var byOther = await QueryWeekIds(property.Id, siteIds: [otherSiteId]);
        Assert.That(byOther, Does.Not.Contain(individualEvent.Id),
            "an unrelated site must not see the individually-assigned event");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4 — no filter at all
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Tripwire — passes on pre-fix code. Guards the "empty or absent behaves exactly
    /// as today" requirement: with neither SiteIds nor WorkerTagIds the assignee filter
    /// must not run at all, so a team-assigned event (empty AssigneeIds) still renders
    /// alongside an individually-assigned one.
    /// </summary>
    [Test]
    public async Task Tripwire_NoAssigneeFilter_ReturnsBothEvents()
    {
        var property = await SeedProperty();
        var teamTagId = await SeedSdkWorkerTag();
        await LinkSiteToTag(teamTagId, await SeedSdkSite());

        var teamEvent = await SeedWeeklyEvent(property.Id);
        await AssignWorkerTag(teamEvent.Id, teamTagId);

        var individualEvent = await SeedWeeklyEvent(property.Id);
        await AssignSite(individualEvent.Id, await SeedSdkSite());

        var ids = await QueryWeekIds(property.Id);

        Assert.That(ids, Does.Contain(teamEvent.Id));
        Assert.That(ids, Does.Contain(individualEvent.Id));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 5 — planning-tag (TagNames) filter is a different concept and unchanged
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Tripwire — passes on pre-fix code. <c>TagNames</c> filters PLANNING/eForm tags
    /// (<c>AreaRulePlanningTag</c> → <c>PlanningTag.Name</c>); <c>WorkerTagIds</c>
    /// filters worker tags (SDK <c>Tag</c> via <c>AreaRulePlanningWorkerTag</c>). The
    /// two must stay disjoint — this fails if anyone ever overloads <c>TagNames</c>
    /// to carry worker tags. The worker-tag-assigned event deliberately carries NO
    /// planning tag, so a planning-tag filter must drop it.
    /// </summary>
    [Test]
    public async Task Tripwire_PlanningTagNamesFilter_Unchanged()
    {
        var property = await SeedProperty();
        var planningTagName = $"planning-tag-{Guid.NewGuid()}";
        var planningTagId = await SeedPlanningTag(planningTagName);

        var taggedEvent = await SeedWeeklyEvent(property.Id);
        await LinkPlanningTag(taggedEvent.Id, planningTagId);

        var teamTagId = await SeedSdkWorkerTag();
        var teamEvent = await SeedWeeklyEvent(property.Id);
        await AssignWorkerTag(teamEvent.Id, teamTagId);

        var ids = await QueryWeekIds(property.Id, tagNames: [planningTagName]);

        Assert.That(ids, Does.Contain(taggedEvent.Id),
            "the planning-tag filter must still match on PlanningTag.Name");
        Assert.That(ids, Does.Not.Contain(teamEvent.Id),
            "a worker-tag-assigned event carries no planning tag and must not match "
            + "a TagNames filter — the two tag concepts must stay disjoint");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 6 — SiteIds and WorkerTagIds combine with OR, never AND
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Product decision recorded on #1211: the two selections are independent lists
    /// that combine server-side with OR. Pre-fix the assertion
    /// <c>Does.Contain(teamEvent.Id)</c> FAILS — WorkerTagIds did not exist, so the
    /// request degenerated to SiteIds=[individualSite] and the team event was dropped.
    /// An AND implementation would fail BOTH Contains assertions (neither event
    /// satisfies both halves).
    /// </summary>
    [Test]
    public async Task SiteIdsAndWorkerTagIds_CombineWithOr()
    {
        var property = await SeedProperty();

        var teamTagId = await SeedSdkWorkerTag();
        await LinkSiteToTag(teamTagId, await SeedSdkSite());
        var teamEvent = await SeedWeeklyEvent(property.Id);
        await AssignWorkerTag(teamEvent.Id, teamTagId);

        var individualSiteId = await SeedSdkSite();
        var individualEvent = await SeedWeeklyEvent(property.Id);
        await AssignSite(individualEvent.Id, individualSiteId);

        // A third event matching NEITHER half — proves OR widens rather than disables.
        var unrelatedEvent = await SeedWeeklyEvent(property.Id);
        await AssignSite(unrelatedEvent.Id, await SeedSdkSite());

        var ids = await QueryWeekIds(
            property.Id, siteIds: [individualSiteId], workerTagIds: [teamTagId]);

        Assert.That(ids, Does.Contain(teamEvent.Id),
            "the worker-tag half of the OR must contribute");
        Assert.That(ids, Does.Contain(individualEvent.Id),
            "the site half of the OR must contribute");
        Assert.That(ids, Does.Not.Contain(unrelatedEvent.Id),
            "OR must widen the result, not disable filtering altogether");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 7 — removed SiteTag membership does not match
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Tripwire — passes on pre-fix code (pre-fix nothing matched a team event at all).
    /// It locks the <c>WorkflowState != Removed</c> gate on the new SiteTags read: a
    /// worker removed from a team keeps a soft-deleted SiteTag row, and must stop
    /// matching that team's events. Same live-membership semantics
    /// <c>CalendarAssignmentResolver</c> uses for deployment.
    /// </summary>
    [Test]
    public async Task Tripwire_RemovedTeamMembership_DoesNotMatch()
    {
        var property = await SeedProperty();
        var teamTagId = await SeedSdkWorkerTag();
        var currentMemberId = await SeedSdkSite();
        var formerMemberId = await SeedSdkSite();
        await LinkSiteToTag(teamTagId, currentMemberId);
        await LinkSiteToTag(teamTagId, formerMemberId, removed: true);

        var teamEvent = await SeedWeeklyEvent(property.Id);
        await AssignWorkerTag(teamEvent.Id, teamTagId);

        var byFormerMember = await QueryWeekIds(property.Id, siteIds: [formerMemberId]);
        Assert.That(byFormerMember, Does.Not.Contain(teamEvent.Id),
            "a soft-deleted SiteTag row must not make a former member match the team's events");

        // Positive control: the seed shape resolves at all.
        var byCurrentMember = await QueryWeekIds(property.Id, siteIds: [currentMemberId]);
        Assert.That(byCurrentMember, Does.Contain(teamEvent.Id),
            "a live team member must match — if this fails the assertion above proved nothing");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 8 — resigned workers are no longer team members
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Team membership ends when the member resigns. A site whose SDK
    /// <c>Worker.Resigned</c> is true no longer resolves to the tags its (stale)
    /// SiteTags rows still point at, so filtering by that person does not surface the
    /// team's events.
    ///
    /// <para>
    /// This is the same membership rule
    /// <c>BackendConfigurationWorkerTagsService.GetWorkerTags</c> applies
    /// when it decides which teams this filter's dropdown may offer, and the same one
    /// <c>CalendarAssignmentResolver</c> applies when deciding who an occurrence
    /// deploys to (#1184). The three must not drift.
    /// </para>
    ///
    /// <para>
    /// The <c>SiteIds</c> half of the OR is deliberately NOT changed: an explicit,
    /// named assignment to a since-resigned worker is a fact about that event and
    /// still matches. The third assertion pins that, so the asymmetry is a recorded
    /// choice rather than an oversight.
    /// </para>
    ///
    /// Without the strict membership clauses in <c>GetTasksForWeek</c>'s SiteTags
    /// lookup, the FIRST assertion fails: the resigned member's site still resolves to
    /// the team tag and the team event comes back.
    /// </summary>
    [Test]
    public async Task ResignedTeamMember_DoesNotMatchTeamEvent()
    {
        var property = await SeedProperty();
        var teamTagId = await SeedSdkWorkerTag();

        // Workers.Resigned only exists after the Core has migrated the SDK schema,
        // so seed through a context handed out by the Core, not MicrotingDbContext.
        var core = await GetCore();
        await using var sdkDbContext = core.DbContextHelper.GetDbContext();

        var resignedMemberId = await SeedSdkSiteWithWorker(sdkDbContext, resigned: true);
        var activeMemberId = await SeedSdkSiteWithWorker(sdkDbContext, resigned: false);
        await LinkSiteToTag(teamTagId, resignedMemberId);
        await LinkSiteToTag(teamTagId, activeMemberId);

        var teamEvent = await SeedWeeklyEvent(property.Id);
        await AssignWorkerTag(teamEvent.Id, teamTagId);

        // An event assigned to the resigned worker BY NAME — the untouched half of
        // the OR.
        var explicitEvent = await SeedWeeklyEvent(property.Id);
        await AssignSite(explicitEvent.Id, resignedMemberId);

        var byResigned = await QueryWeekIds(property.Id, siteIds: [resignedMemberId]);
        Assert.That(byResigned, Does.Not.Contain(teamEvent.Id),
            "a resigned worker is no longer a member of the team, so their site must "
            + "not resolve to the team's tag and pull in the team's events");
        Assert.That(byResigned, Does.Contain(explicitEvent.Id),
            "the SiteIds half of the OR is unchanged: an event assigned to this "
            + "person by name still matches, resigned or not");

        var byActive = await QueryWeekIds(property.Id, siteIds: [activeMemberId]);
        Assert.That(byActive, Does.Contain(teamEvent.Id),
            "control: an employed member of the same team still matches — if this "
            + "fails, the assertion above proved nothing");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 9 — a soft-deleted filter site does not resolve to its old team
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The other half of the strict membership rule, and the reason it cannot be
    /// folded into the resigned check. <c>Core.SiteDelete</c> soft-removes the
    /// <c>Site</c> and its <c>Worker</c> (<c>PnBase.Delete</c> flips
    /// <c>WorkflowState</c> only — it does NOT set <c>Resigned</c>) and leaves the
    /// <c>SiteTags</c> rows behind entirely. Without the
    /// <c>Site.WorkflowState != Removed</c> clause a deleted device user therefore
    /// keeps resolving to their old team forever.
    ///
    /// <para>
    /// Test 7 covers the removed <c>SiteTag</c> ROW; this covers the removed
    /// <c>Site</c>, which is a different row in a different table and was not
    /// otherwise exercised. Neither site here has a SiteWorker at all, so the
    /// resigned clause cannot be what excludes the deleted one.
    /// </para>
    ///
    /// Without the Site clause the first assertion fails.
    /// </summary>
    [Test]
    public async Task Tripwire_SoftDeletedFilterSite_DoesNotMatchTeamEvent()
    {
        var property = await SeedProperty();
        var teamTagId = await SeedSdkWorkerTag();
        var liveMemberId = await SeedSdkSite();
        var deletedMemberId = await SeedSdkSite(removed: true);

        // Both SiteTags rows stay Created — that is exactly what SiteDelete leaves.
        await LinkSiteToTag(teamTagId, liveMemberId);
        await LinkSiteToTag(teamTagId, deletedMemberId);

        var teamEvent = await SeedWeeklyEvent(property.Id);
        await AssignWorkerTag(teamEvent.Id, teamTagId);

        var byDeleted = await QueryWeekIds(property.Id, siteIds: [deletedMemberId]);
        Assert.That(byDeleted, Does.Not.Contain(teamEvent.Id),
            "a soft-deleted Site must not resolve to the team its surviving SiteTags "
            + "row still points at");

        var byLive = await QueryWeekIds(property.Id, siteIds: [liveMemberId]);
        Assert.That(byLive, Does.Contain(teamEvent.Id),
            "control: the live member of the same team matches — if this fails the "
            + "assertion above proved nothing");
    }
}
