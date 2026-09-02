using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;
using BackendConfiguration.Pn.Services.CalendarChangeNotification;
using BackendConfiguration.Pn.Services.EventDeployService;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Enums;
using NSubstitute;
using NUnit.Framework;
using SdkCase = Microting.eForm.Infrastructure.Data.Entities.Case;
using BcCompliance = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.Compliance;
using BcPlanningSite = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.PlanningSite;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// Integration coverage for the calendar worker-tags assignment feature:
/// <see cref="CalendarAssignmentResolver"/> (effective recipient resolution from
/// explicit PlanningSites ∪ live SDK SiteTag members) and
/// <see cref="CalendarAssignmentReconciliationService"/> (retroactive add/remove of
/// future already-deployed occurrences, past-occurrence skipping, completed-case
/// immutability).
///
/// NB: the test container/DBs are shared across the tests in this fixture (the base
/// fixture starts the container in [SetUp] but only EnsureCreated()s the schema, so
/// rows accumulate). Every test therefore seeds its OWN entities (auto-generated SDK
/// site ids, GUID-named property/tag) and scopes every assertion to those ids — no
/// hard-coded SDK ids that could collide or leak between tests.
/// </summary>
[TestFixture]
public class WorkerTagAssignmentTest : TestBaseSetup
{
    private static readonly DateTime SeriesStart =
        new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// The shared test container is NOT fully reset between tests: the backend-config
    /// seed file only truncates the tables it lists. <c>AreaRulePlanningWorkerTags</c> is
    /// not one of them, so it is never emptied and its rows accumulate across tests while
    /// AreaRulePlannings/Tags ids restart at 1 — causing prior tests' links to attach
    /// to this test's arp/tag. Clear it explicitly after the base [SetUp] (NUnit runs
    /// the base-class [SetUp] before this derived one).
    /// </summary>
    [SetUp]
    public async Task ClearAccumulatingTables()
    {
        await BackendConfigurationPnDbContext!.Database
            .ExecuteSqlRawAsync("DELETE FROM `AreaRulePlanningWorkerTags`;");
    }

    /// <summary>Seeds Area/Property/AreaRule/Planning/AreaRulePlanning and returns the arp + planning.</summary>
    private async Task<(AreaRulePlanning arp, Planning planning, Property property)> SeedEvent(bool status = true)
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
            Name = $"WorkerTagTest-{Guid.NewGuid()}", ItemPlanningTagId = 0,
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

        var planning = new Planning
        {
            Enabled = true, RepeatEvery = 1, RepeatType = RepeatType.Week, StartDate = SeriesStart,
            RelatedEFormId = 0, WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = property.Id, AreaId = area.Id,
            ItemPlanningId = planning.Id, StartDate = SeriesStart, Status = status,
            RepeatType = 2, RepeatEvery = 1,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(arp);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        return (arp, planning, property);
    }

    /// <summary>Adds an explicit PlanningSite assignee for the event. Returns the site id.</summary>
    private async Task<int> AddExplicitSite(int arpId, int siteId)
    {
        await BackendConfigurationPnDbContext!.PlanningSites.AddAsync(new BcPlanningSite
        {
            AreaRulePlanningsId = arpId, SiteId = siteId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        return siteId;
    }

    /// <summary>Adds a worker-tag assignment link for the event.</summary>
    private async Task AddWorkerTagLink(int arpId, int tagId, string workflowState = null)
    {
        var link = new AreaRulePlanningWorkerTag
        {
            AreaRulePlanningId = arpId, TagId = tagId,
            WorkflowState = workflowState ?? Constants.WorkflowStates.Created,
            CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.AreaRulePlanningWorkerTags.AddAsync(link);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
    }

    /// <summary>Creates an SDK Site (auto id) and returns its generated id.</summary>
    private async Task<int> SeedSdkSite()
    {
        var language = await MicrotingDbContext!.Languages.FirstAsync();
        var site = new Site
        {
            Name = $"site-{Guid.NewGuid()}",
            MicrotingUid = null,
            LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddAsync(site);
        await MicrotingDbContext.SaveChangesAsync();
        return site.Id;
    }

    /// <summary>
    /// Creates an SDK Tag (auto id) into the SAME SDK db the resolver reads through
    /// (core.DbContextHelper.GetDbContext()). The resolver's core db and the test
    /// MicrotingDbContext both map to the 420_SDK schema, so seeding through
    /// MicrotingDbContext is visible to the resolver. Returns the generated tag id.
    /// </summary>
    private async Task<int> SeedSdkTag()
    {
        var tag = new Tag
        {
            Name = $"worker-tag-{Guid.NewGuid()}",
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext!.Tags.AddAsync(tag);
        await MicrotingDbContext.SaveChangesAsync();
        return tag.Id;
    }

    /// <summary>Links a site to a tag via SDK SiteTag (live or removed).</summary>
    private async Task LinkSiteToTag(int tagId, int siteId, bool removed = false)
    {
        await MicrotingDbContext!.SiteTags.AddAsync(new SiteTag
        {
            TagId = tagId, SiteId = siteId,
            WorkflowState = removed
                ? Constants.WorkflowStates.Removed
                : Constants.WorkflowStates.Created
        });
        await MicrotingDbContext.SaveChangesAsync();
    }

    private async Task<CalendarAssignmentResolver> BuildResolver()
    {
        var core = await GetCore();
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));
        return new CalendarAssignmentResolver(BackendConfigurationPnDbContext!, coreHelper);
    }

    /// <summary>
    /// Builds the engine; <c>batches</c> collects every batch the engine hands
    /// to the notifier, in order.
    /// </summary>
    private async Task<(CalendarAssignmentReconciliationService engine, IEventDeployService deploy,
            List<CalendarChangeBatch> batches)>
        BuildEngine()
    {
        var core = await GetCore();
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));
        var resolver = new CalendarAssignmentResolver(BackendConfigurationPnDbContext!, coreHelper);
        var deploy = Substitute.For<IEventDeployService>();
        var batches = new List<CalendarChangeBatch>();
        var notifier = Substitute.For<ICalendarChangeNotifier>();
        notifier.When(x => x.NotifyInBackground(Arg.Any<CalendarChangeBatch>()))
            .Do(ci => batches.Add(ci.Arg<CalendarChangeBatch>()));
        var engine = new CalendarAssignmentReconciliationService(
            BackendConfigurationPnDbContext!, ItemsPlanningPnDbContext!, coreHelper,
            deploy, resolver, notifier,
            NullLogger<CalendarAssignmentReconciliationService>.Instance);
        return (engine, deploy, batches);
    }

    /// <summary>Deploys a Compliance + backing SDK Case for one (planning, date, site).</summary>
    private async Task<(int caseId, int complianceId)> SeedDeployedOccurrence(
        int planningId, DateTime deadline, int siteId, int status, int? microtingUid)
    {
        var sdkCase = new SdkCase
        {
            SiteId = siteId, Status = status, MicrotingUid = microtingUid,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext!.Cases.AddAsync(sdkCase);
        await MicrotingDbContext.SaveChangesAsync();

        var compliance = new BcCompliance
        {
            PlanningId = planningId, Deadline = deadline, MicrotingSdkCaseId = sdkCase.Id,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Compliances.AddAsync(compliance);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        return (sdkCase.Id, compliance.Id);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 1 — Resolver: effective set = explicit ∪ tag members
    // ─────────────────────────────────────────────────────────────────────────
    [Test]
    public async Task ResolveEffectiveSiteIds_ReturnsExplicitUnionTagMembers()
    {
        var (arp, _, _) = await SeedEvent();
        var explicitSite = await AddExplicitSite(arp.Id, await SeedSdkSite());

        var memberA = await SeedSdkSite();
        var memberB = await SeedSdkSite();
        var tagId = await SeedSdkTag();
        await LinkSiteToTag(tagId, memberA);
        await LinkSiteToTag(tagId, memberB);
        await AddWorkerTagLink(arp.Id, tagId);

        var resolver = await BuildResolver();
        var result = await resolver.ResolveEffectiveSiteIdsAsync(arp.Id);

        Assert.That(result, Is.EquivalentTo(new[] { explicitSite, memberA, memberB }));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 2 — Resolver excludes removed SiteTags and removed worker-tag links
    // ─────────────────────────────────────────────────────────────────────────
    [Test]
    public async Task ResolveEffectiveSiteIds_ExcludesRemovedSiteTag()
    {
        var (arp, _, _) = await SeedEvent();
        var explicitSite = await AddExplicitSite(arp.Id, await SeedSdkSite());

        var liveMember = await SeedSdkSite();
        var removedMember = await SeedSdkSite();
        var tagId = await SeedSdkTag();
        await LinkSiteToTag(tagId, liveMember);
        await LinkSiteToTag(tagId, removedMember, removed: true);
        await AddWorkerTagLink(arp.Id, tagId);

        var resolver = await BuildResolver();
        var result = await resolver.ResolveEffectiveSiteIdsAsync(arp.Id);

        Assert.That(result, Is.EquivalentTo(new[] { explicitSite, liveMember }),
            "removed SiteTag member must be excluded");
    }

    [Test]
    public async Task ResolveEffectiveSiteIds_RemovedWorkerTagLink_YieldsOnlyExplicit()
    {
        var (arp, _, _) = await SeedEvent();
        var explicitSite = await AddExplicitSite(arp.Id, await SeedSdkSite());

        var memberA = await SeedSdkSite();
        var memberB = await SeedSdkSite();
        var tagId = await SeedSdkTag();
        await LinkSiteToTag(tagId, memberA);
        await LinkSiteToTag(tagId, memberB);
        // The worker-tag link itself is Removed → no tag members contribute.
        await AddWorkerTagLink(arp.Id, tagId, Constants.WorkflowStates.Removed);

        var resolver = await BuildResolver();
        var result = await resolver.ResolveEffectiveSiteIdsAsync(arp.Id);

        Assert.That(result, Is.EquivalentTo(new[] { explicitSite }),
            "removed worker-tag link must contribute no tag members");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 3 — Engine retroactive ADD decision (mock deploy service)
    // ─────────────────────────────────────────────────────────────────────────
    [Test]
    public async Task ReconcileEvent_AddsTagMemberToAlreadyDeployedFutureOccurrence()
    {
        var (arp, planning, _) = await SeedEvent();

        // Assigned ONLY via a worker tag whose member is desiredSite (no explicit sites).
        var desiredSite = await SeedSdkSite();
        var alreadyDeployedSite = await SeedSdkSite();
        var tagId = await SeedSdkTag();
        await LinkSiteToTag(tagId, desiredSite);
        await AddWorkerTagLink(arp.Id, tagId);

        // A FUTURE already-deployed occurrence for a DIFFERENT site (Status 66).
        var futureDate = DateTime.UtcNow.Date.AddDays(7);
        await SeedDeployedOccurrence(planning.Id, futureDate, alreadyDeployedSite,
            status: 66, microtingUid: 555001);

        var (engine, deploy, _) = await BuildEngine();
        await engine.ReconcileEventAsync(arp.Id);

        // desiredSite is desired but not deployed → engine must add it for the future date.
        await deploy.Received().EnsureComplianceForOccurrenceAsync(
            Arg.Is<AreaRulePlanning>(a => a.Id == arp.Id),
            Arg.Is<DateTime>(d => d.Date == futureDate.Date),
            desiredSite,
            Arg.Any<CancellationToken>());

        // The already-deployed (non-completed) site → no add for it.
        await deploy.DidNotReceive().EnsureComplianceForOccurrenceAsync(
            Arg.Any<AreaRulePlanning>(), Arg.Any<DateTime>(), alreadyDeployedSite,
            Arg.Any<CancellationToken>());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 4 — Engine skips PAST occurrences and respects future-only
    // ─────────────────────────────────────────────────────────────────────────
    [Test]
    public async Task ReconcileEvent_DoesNotTouchPastOccurrences()
    {
        var (arp, planning, _) = await SeedEvent();

        var desiredSite = await SeedSdkSite();
        var deployedSite = await SeedSdkSite();
        var tagId = await SeedSdkTag();
        await LinkSiteToTag(tagId, desiredSite);
        await AddWorkerTagLink(arp.Id, tagId);

        // PAST occurrence deployed for deployedSite.
        var pastDate = DateTime.UtcNow.Date.AddDays(-7);
        await SeedDeployedOccurrence(planning.Id, pastDate, deployedSite,
            status: 66, microtingUid: 555002);

        var (engine, deploy, _) = await BuildEngine();
        await engine.ReconcileEventAsync(arp.Id);

        // Past date must never be reconciled (no add of desiredSite for the past date).
        await deploy.DidNotReceive().EnsureComplianceForOccurrenceAsync(
            Arg.Any<AreaRulePlanning>(),
            Arg.Is<DateTime>(d => d.Date == pastDate.Date),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 5 — Engine retroactive REMOVE + completed immutable
    // ─────────────────────────────────────────────────────────────────────────
    [Test]
    public async Task ReconcileEvent_RemovesUnassignedNonCompleted_KeepsCompleted()
    {
        // Effective set is EMPTY: a worker tag with no live members, no explicit sites.
        var (arp, planning, _) = await SeedEvent();
        var tagId = await SeedSdkTag();
        await AddWorkerTagLink(arp.Id, tagId);

        var futureDate = DateTime.UtcNow.Date.AddDays(7);

        var nonCompletedSite = await SeedSdkSite();
        var completedSite = await SeedSdkSite();

        // The Compliances table has a UNIQUE index on (PlanningId, Deadline), so the
        // two sites for the same occurrence DATE must carry distinct Deadline times
        // (the engine groups occurrences by Deadline.Date, so both still belong to the
        // same occurrence date).
        // Non-completed deployed case (Status 66) → must be retracted. MicrotingUid=null
        // so the SDK CaseDelete call is skipped, but the bookkeeping (Compliance
        // soft-delete) still runs.
        var (_, nonCompletedComplianceId) = await SeedDeployedOccurrence(
            planning.Id, futureDate.AddHours(9), nonCompletedSite, status: 66, microtingUid: null);

        // Completed deployed case (Status 100) → immutable.
        var (_, completedComplianceId) = await SeedDeployedOccurrence(
            planning.Id, futureDate.AddHours(10), completedSite, status: 100, microtingUid: null);

        var (engine, _, _) = await BuildEngine();
        await engine.ReconcileEventAsync(arp.Id);

        // The non-completed compliance must be soft-removed.
        var reloadedNonCompleted = await BackendConfigurationPnDbContext!.Compliances
            .AsNoTracking().FirstAsync(x => x.Id == nonCompletedComplianceId);
        Assert.That(reloadedNonCompleted.WorkflowState,
            Is.EqualTo(Constants.WorkflowStates.Removed),
            "non-completed compliance for unassigned site must be soft-removed");

        // The completed compliance must be untouched.
        var reloadedCompleted = await BackendConfigurationPnDbContext.Compliances
            .AsNoTracking().FirstAsync(x => x.Id == completedComplianceId);
        Assert.That(reloadedCompleted.WorkflowState,
            Is.Not.EqualTo(Constants.WorkflowStates.Removed),
            "completed compliance (Status 100) is immutable and must survive");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Tests 6-8 — the calendar-change push hook
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The reassignment the push exists for: the loser's app must stop showing
    /// the event and the gainer's must start showing it, and neither happens
    /// while the app sits backgrounded unless it is told. BOTH sides of the
    /// delta must be notified — notifying only gainers is the easy half and
    /// leaves the actual reported bug (a lost event still on screen) unfixed.
    /// </summary>
    [Test]
    public async Task ReconcileEvent_ReassignsWorker_NotifiesLoserAndGainer()
    {
        var (arp, planning, _) = await SeedEvent();

        var gainer = await SeedSdkSite();
        var loser = await SeedSdkSite();
        await AddExplicitSite(arp.Id, gainer);

        var futureDate = DateTime.UtcNow.Date.AddDays(7);
        await SeedDeployedOccurrence(planning.Id, futureDate.AddHours(9), loser,
            status: 66, microtingUid: null);

        var (engine, _, batches) = await BuildEngine();

        await engine.ReconcileEventAsync(arp.Id);

        Assert.That(batches, Has.Count.EqualTo(1),
            "one operation notifies once, after the work — not per occurrence");
        Assert.That(batches[0].Pairs, Is.EquivalentTo(new[]
        {
            new CalendarChangePair(gainer, arp.Id),
            new CalendarChangePair(loser, arp.Id)
        }));
    }

    /// <summary>
    /// The volume guard at the source. ReconcileEventAsync plans the SAME
    /// (worker, event) delta once per future occurrence, so a year of a weekly
    /// event is ~50 identical deltas for one reassignment. The batch must
    /// collapse them; a per-occurrence push here is the incident.
    /// </summary>
    [Test]
    public async Task ReconcileEvent_ManyFutureOccurrences_RecordsOnePairPerWorker()
    {
        var (arp, planning, _) = await SeedEvent();

        var loser = await SeedSdkSite();
        // Effective set is empty (no explicit sites, no tags) — every occurrence
        // plans the same single removal.
        var firstDate = DateTime.UtcNow.Date.AddDays(7);
        for (var week = 0; week < 4; week++)
        {
            await SeedDeployedOccurrence(planning.Id, firstDate.AddDays(7 * week).AddHours(9),
                loser, status: 66, microtingUid: null);
        }

        var (engine, _, batches) = await BuildEngine();

        await engine.ReconcileEventAsync(arp.Id);

        Assert.That(batches, Has.Count.EqualTo(1));
        Assert.That(batches[0].Pairs, Is.EquivalentTo(new[]
        {
            new CalendarChangePair(loser, arp.Id)
        }), "four future occurrences of one event are still one push for that worker");
    }

    /// <summary>
    /// The tag fan-out walks every event carrying the changed tag. It must
    /// accumulate into ONE batch for the whole operation, so the cap and the
    /// dedupe apply across events — flushing per event would put the volume
    /// ceiling back where it cannot bound anything.
    /// </summary>
    [Test]
    public async Task ReconcileEventsForWorkerTags_AccumulatesEveryEventIntoOneBatch()
    {
        var tagId = await SeedSdkTag();

        var (arpA, planningA, _) = await SeedEvent();
        var loserA = await SeedSdkSite();
        await AddWorkerTagLink(arpA.Id, tagId);
        await SeedDeployedOccurrence(planningA.Id, DateTime.UtcNow.Date.AddDays(7).AddHours(9),
            loserA, status: 66, microtingUid: null);

        var (arpB, planningB, _) = await SeedEvent();
        var loserB = await SeedSdkSite();
        await AddWorkerTagLink(arpB.Id, tagId);
        await SeedDeployedOccurrence(planningB.Id, DateTime.UtcNow.Date.AddDays(8).AddHours(9),
            loserB, status: 66, microtingUid: null);

        var (engine, _, batches) = await BuildEngine();

        await engine.ReconcileEventsForWorkerTagsAsync(new[] { tagId });

        Assert.That(batches, Has.Count.EqualTo(1),
            "the whole tag fan-out is one operation and notifies once");
        Assert.That(batches[0].Pairs, Is.EquivalentTo(new[]
        {
            new CalendarChangePair(loserA, arpA.Id),
            new CalendarChangePair(loserB, arpB.Id)
        }));
    }
}
