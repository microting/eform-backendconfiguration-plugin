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
using System.Threading;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.BackendConfigurationWorkerTagsService;
using BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;
using BackendConfiguration.Pn.Services.CalendarChangeNotification;
using BackendConfiguration.Pn.Services.EventDeployService;
using BackendConfiguration.Pn.Services.WorkerTagMembership;
using Microsoft.EntityFrameworkCore;
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
/// #1295 (resolves #1256): teams ("worker tags") are scoped to the task's property.
///
/// <list type="bullet">
/// <item><b>Deploy.</b> <see cref="CalendarAssignmentResolver"/> expands a team only to
/// its live members that are linked to the EVENT'S property (active
/// <c>PropertyWorker</c>). Before, a team with members on property A, assigned to an
/// event on property B, deployed cases to A's workers.</item>
/// <item><b>Offer.</b> <see cref="BackendConfigurationWorkerTagsService.GetWorkerTags"/>
/// with a <c>propertyId</c> lists only teams with at least one live member linked to
/// that property, and returns those members per team. Without a propertyId the list is
/// the installation-wide list, unchanged.</item>
/// <item><b>Live link.</b> The team is not snapshotted: membership / property-link
/// changes flow into the next resolution.</item>
/// </list>
///
/// Every test seeds its own property/sites/tags (the fixture does not reset between tests)
/// and asserts by containment on its own ids only.
/// </summary>
[TestFixture]
public class WorkerTagPropertyScopeTests : TestBaseSetup
{
    private static readonly DateTime SeriesStart =
        new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Same reason as <c>WorkerTagAssignmentTest.ClearAccumulatingTables</c>: this table
    /// is in no seed file, so links from earlier fixtures could attach to this fixture's
    /// restarted AreaRulePlanning ids.
    /// </summary>
    [SetUp]
    public async Task ClearAccumulatingTables()
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
            Name = $"TeamScope-{Guid.NewGuid()}", ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Properties.AddAsync(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        return property;
    }

    private async Task<(AreaRulePlanning arp, Planning planning)> SeedEvent(Property property)
    {
        var area = new Area
        {
            Type = AreaTypesEnum.Type1, ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Areas.AddAsync(area);
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
            ItemPlanningId = planning.Id, StartDate = SeriesStart, Status = true,
            RepeatType = 2, RepeatEvery = 1,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(arp);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        return (arp, planning);
    }

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

    private async Task<int> SeedSdkTag()
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

    private async Task LinkSiteToTag(int tagId, int siteId)
    {
        await MicrotingDbContext!.SiteTags.AddAsync(new SiteTag
        {
            TagId = tagId, SiteId = siteId, WorkflowState = Constants.WorkflowStates.Created
        });
        await MicrotingDbContext.SaveChangesAsync();
    }

    private async Task<PropertyWorker> LinkSiteToProperty(int propertyId, int siteId)
    {
        var link = new PropertyWorker
        {
            PropertyId = propertyId, WorkerId = siteId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.PropertyWorkers.AddAsync(link);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        return link;
    }

    private async Task AddWorkerTagLink(int arpId, int tagId)
    {
        await BackendConfigurationPnDbContext!.AreaRulePlanningWorkerTags.AddAsync(new AreaRulePlanningWorkerTag
        {
            AreaRulePlanningId = arpId, TagId = tagId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();
    }

    /// <summary>
    /// A team with one member linked ONLY to property A and one linked ONLY to
    /// property B — the #1256 shape.
    /// </summary>
    private async Task<(Property a, Property b, int tagId, int memberOnA, int memberOnB)> SeedCrossPropertyTeam()
    {
        var a = await SeedProperty();
        var b = await SeedProperty();
        var tagId = await SeedSdkTag();

        var memberOnA = await SeedSdkSite();
        var memberOnB = await SeedSdkSite();
        await LinkSiteToTag(tagId, memberOnA);
        await LinkSiteToTag(tagId, memberOnB);
        await LinkSiteToProperty(a.Id, memberOnA);
        await LinkSiteToProperty(b.Id, memberOnB);

        return (a, b, tagId, memberOnA, memberOnB);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Services under test (one Core per test — GetCore() is expensive)
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<(WorkerTagMembershipService membership, CalendarAssignmentResolver resolver,
            BackendConfigurationWorkerTagsService tagsList, IEFormCoreService coreHelper)> BuildAsync()
    {
        var core = await GetCore();
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));
        var membership = new WorkerTagMembershipService(coreHelper, BackendConfigurationPnDbContext);
        return (membership,
            new CalendarAssignmentResolver(BackendConfigurationPnDbContext!, membership),
            new BackendConfigurationWorkerTagsService(
                coreHelper, membership, TestContextLogger<BackendConfigurationWorkerTagsService>.Instance),
            coreHelper);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Deploy-time expansion (resolver)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// THE #1256 regression. A team with members on A and B, assigned to an event on B,
    /// resolves to B's member only. <b>Fails on the old code</b>: the unscoped resolver
    /// also returned <c>memberOnA</c>. The <c>memberOnB</c> half is the positive control.
    /// </summary>
    [Test]
    public async Task Resolver_TeamWithMembersOnAAndB_AssignedOnB_ResolvesOnlyBsMembers()
    {
        var (_, resolver, _, _) = await BuildAsync();
        var (_, b, tagId, memberOnA, memberOnB) = await SeedCrossPropertyTeam();
        var (arp, _) = await SeedEvent(b);
        await AddWorkerTagLink(arp.Id, tagId);

        var effective = await resolver.ResolveEffectiveSiteIdsAsync(arp.Id);

        Assert.Multiple(() =>
        {
            Assert.That(effective, Does.Contain(memberOnB),
                "control: the team's member on the event's own property must receive it");
            Assert.That(effective, Does.Not.Contain(memberOnA),
                "a team member linked only to ANOTHER property must not receive the event (#1256)");
        });
    }

    /// <summary>
    /// The mirror direction on the same dataset: assigned on A, only A's member. Rules out
    /// a resolver that happens to prefer one property for any other reason.
    /// </summary>
    [Test]
    public async Task Resolver_SameTeamAssignedOnA_ResolvesOnlyAsMembers()
    {
        var (_, resolver, _, _) = await BuildAsync();
        var (a, _, tagId, memberOnA, memberOnB) = await SeedCrossPropertyTeam();
        var (arp, _) = await SeedEvent(a);
        await AddWorkerTagLink(arp.Id, tagId);

        var effective = await resolver.ResolveEffectiveSiteIdsAsync(arp.Id);

        Assert.That(effective, Is.EquivalentTo(new[] { memberOnA }));
        Assert.That(effective, Does.Not.Contain(memberOnB));
    }

    /// <summary>
    /// A member linked to BOTH properties is a member on either — the property clause is a
    /// link test, not an exclusive-home test.
    /// </summary>
    [Test]
    public async Task Resolver_MemberLinkedToBothProperties_IsIncluded()
    {
        var (_, resolver, _, _) = await BuildAsync();
        var (a, b, tagId, _, memberOnB) = await SeedCrossPropertyTeam();
        var shared = await SeedSdkSite();
        await LinkSiteToTag(tagId, shared);
        await LinkSiteToProperty(a.Id, shared);
        await LinkSiteToProperty(b.Id, shared);
        var (arp, _) = await SeedEvent(b);
        await AddWorkerTagLink(arp.Id, tagId);

        var effective = await resolver.ResolveEffectiveSiteIdsAsync(arp.Id);

        Assert.That(effective, Is.EquivalentTo(new[] { memberOnB, shared }));
    }

    /// <summary>
    /// A removed <c>PropertyWorker</c> row is not a property link (the same rule the
    /// property's worker picker reads). <b>Fails on the old code</b>: the unscoped
    /// resolver returned the member regardless.
    /// </summary>
    [Test]
    public async Task Resolver_RemovedPropertyWorkerLink_IsNotAPropertyMember()
    {
        var (_, resolver, _, _) = await BuildAsync();
        var property = await SeedProperty();
        var tagId = await SeedSdkTag();
        var stillLinked = await SeedSdkSite();
        var unlinked = await SeedSdkSite();
        await LinkSiteToTag(tagId, stillLinked);
        await LinkSiteToTag(tagId, unlinked);
        await LinkSiteToProperty(property.Id, stillLinked);
        var removedLink = await LinkSiteToProperty(property.Id, unlinked);
        removedLink.WorkflowState = Constants.WorkflowStates.Removed;
        await BackendConfigurationPnDbContext!.SaveChangesAsync();

        var (arp, _) = await SeedEvent(property);
        await AddWorkerTagLink(arp.Id, tagId);

        var effective = await resolver.ResolveEffectiveSiteIdsAsync(arp.Id);

        Assert.That(effective, Is.EquivalentTo(new[] { stillLinked }));
    }

    /// <summary>
    /// The explicit half is untouched: a worker named directly on the event is a recipient
    /// even when not linked to the property (that is the #1184/#932 domain of the
    /// deploy-side guards, not of this rule). Only the TEAM-derived half is scoped.
    /// </summary>
    [Test]
    public async Task Resolver_ExplicitAssigneeIsNotPropertyScoped()
    {
        var (_, resolver, _, _) = await BuildAsync();
        var (_, b, tagId, memberOnA, memberOnB) = await SeedCrossPropertyTeam();
        var (arp, _) = await SeedEvent(b);
        await AddWorkerTagLink(arp.Id, tagId);
        var explicitUnlinked = await SeedSdkSite();
        await BackendConfigurationPnDbContext!.PlanningSites.AddAsync(new BcPlanningSite
        {
            AreaRulePlanningsId = arp.Id, SiteId = explicitUnlinked,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var effective = await resolver.ResolveEffectiveSiteIdsAsync(arp.Id);

        Assert.That(effective, Is.EquivalentTo(new[] { explicitUnlinked, memberOnB }));
        Assert.That(effective, Does.Not.Contain(memberOnA));
    }

    /// <summary>
    /// The team stays a LIVE link (decision: no snapshot). Linking the A-only member to B
    /// after the assignment makes them a recipient on the next resolution; unlinking takes
    /// them away again. Also asserts the stored team assignment itself
    /// (<c>AreaRulePlanningWorkerTags</c>) is never rewritten by resolution.
    /// </summary>
    [Test]
    public async Task Resolver_TeamIsALiveLink_PropertyLinkChangesFlowThrough_AndAssignmentRowsUntouched()
    {
        var (_, resolver, _, _) = await BuildAsync();
        var (_, b, tagId, memberOnA, memberOnB) = await SeedCrossPropertyTeam();
        var (arp, _) = await SeedEvent(b);
        await AddWorkerTagLink(arp.Id, tagId);

        var before = await resolver.ResolveEffectiveSiteIdsAsync(arp.Id);
        Assert.That(before, Is.EquivalentTo(new[] { memberOnB }));

        var newLink = await LinkSiteToProperty(b.Id, memberOnA);
        var afterLink = await resolver.ResolveEffectiveSiteIdsAsync(arp.Id);
        Assert.That(afterLink, Is.EquivalentTo(new[] { memberOnA, memberOnB }),
            "a member newly linked to the property must flow into the team's recipients");

        newLink.WorkflowState = Constants.WorkflowStates.Removed;
        await BackendConfigurationPnDbContext!.SaveChangesAsync();
        var afterUnlink = await resolver.ResolveEffectiveSiteIdsAsync(arp.Id);
        Assert.That(afterUnlink, Is.EquivalentTo(new[] { memberOnB }));

        var links = await BackendConfigurationPnDbContext.AreaRulePlanningWorkerTags
            .AsNoTracking()
            .Where(x => x.AreaRulePlanningId == arp.Id)
            .ToListAsync();
        Assert.That(links, Has.Count.EqualTo(1));
        Assert.That(links[0].TagId, Is.EqualTo(tagId));
        Assert.That(links[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created),
            "existing AreaRulePlanningWorkerTags are kept as-is — the team assignment is data, "
            + "the property scope is applied at resolution time");
    }

    /// <summary>
    /// The release-note risk, pinned: an event already assigned to a cross-property team
    /// that had deployed a FUTURE case to the other property's member gets that case
    /// retracted on the next reconcile, while the own-property member is left deployed.
    /// <b>Fails on the old code</b>: the A member stayed in the desired set, so nothing was
    /// retracted.
    /// </summary>
    [Test]
    public async Task Reconcile_CrossPropertyTeam_RetractsOtherPropertysFutureCase()
    {
        var (_, resolver, _, coreHelper) = await BuildAsync();
        var (_, b, tagId, memberOnA, memberOnB) = await SeedCrossPropertyTeam();
        var (arp, planning) = await SeedEvent(b);
        await AddWorkerTagLink(arp.Id, tagId);

        var futureDate = DateTime.UtcNow.Date.AddDays(7);
        // Compliances is UNIQUE on (PlanningId, Deadline): distinct times, same date.
        var outOfPropertyCompliance = await SeedDeployedOccurrence(
            planning.Id, futureDate.AddHours(9), memberOnA);
        var ownPropertyCompliance = await SeedDeployedOccurrence(
            planning.Id, futureDate.AddHours(10), memberOnB);

        var deploy = Substitute.For<IEventDeployService>();
        var engine = new CalendarAssignmentReconciliationService(
            BackendConfigurationPnDbContext!, ItemsPlanningPnDbContext!, coreHelper,
            deploy, resolver, Substitute.For<ICalendarChangeNotifier>(),
            TestContextLogger<CalendarAssignmentReconciliationService>.Instance);

        await engine.ReconcileEventAsync(arp.Id);

        var reloadedOut = await BackendConfigurationPnDbContext!.Compliances
            .AsNoTracking().FirstAsync(x => x.Id == outOfPropertyCompliance);
        var reloadedOwn = await BackendConfigurationPnDbContext.Compliances
            .AsNoTracking().FirstAsync(x => x.Id == ownPropertyCompliance);

        Assert.Multiple(() =>
        {
            Assert.That(reloadedOut.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed),
                "the other property's member's future case is retracted on reconcile");
            Assert.That(reloadedOwn.WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Removed),
                "control: the own-property member's case stays deployed");
        });
        await deploy.DidNotReceive().EnsureComplianceForOccurrenceAsync(
            Arg.Any<AreaRulePlanning>(), Arg.Any<DateTime>(), memberOnA, Arg.Any<CancellationToken>());
    }

    private async Task<int> SeedDeployedOccurrence(int planningId, DateTime deadline, int siteId)
    {
        var sdkCase = new SdkCase
        {
            SiteId = siteId, Status = 66, MicrotingUid = null,
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
        return compliance.Id;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Teams list (GetWorkerTags with propertyId)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The picker's team list is filtered by property: a team with a live member on the
    /// property is offered, with exactly its property-linked members; a team whose only
    /// member is on another property is not. <b>Fails on the old code</b> (no property
    /// filter existed — the old endpoint took no propertyId and would offer both).
    /// </summary>
    [Test]
    public async Task GetWorkerTags_WithPropertyId_OffersOnlyTeamsWithAMemberOnThatProperty()
    {
        var (_, _, tagsList, _) = await BuildAsync();
        var (a, b, crossTeam, _, memberOnB) = await SeedCrossPropertyTeam();

        var aOnlyTeam = await SeedSdkTag();
        var aOnlyMember = await SeedSdkSite();
        await LinkSiteToTag(aOnlyTeam, aOnlyMember);
        await LinkSiteToProperty(a.Id, aOnlyMember);

        var result = await tagsList.GetWorkerTags(b.Id);

        Assert.That(result.Success, Is.True, result.Message);
        var offered = result.Model.Where(x => x.Id.HasValue).ToDictionary(x => x.Id!.Value);
        Assert.Multiple(() =>
        {
            Assert.That(offered.Keys, Does.Contain(crossTeam),
                "a team with a live member on the property is offered");
            Assert.That(offered.Keys, Does.Not.Contain(aOnlyTeam),
                "a team whose members are all on another property is not offered");
            Assert.That(offered[crossTeam].MemberSiteIds, Is.EquivalentTo(new[] { memberOnB }),
                "MemberSiteIds are the property-linked members only — the sites the team deploys to here");
        });
    }

    /// <summary>
    /// Without a propertyId the list is the installation-wide list, unchanged (the header
    /// filter and tile name maps still use it), and carries no member ids.
    /// </summary>
    [Test]
    public async Task GetWorkerTags_WithoutPropertyId_IsInstallationWide()
    {
        var (_, _, tagsList, _) = await BuildAsync();
        var (a, _, crossTeam, _, _) = await SeedCrossPropertyTeam();
        var aOnlyTeam = await SeedSdkTag();
        var aOnlyMember = await SeedSdkSite();
        await LinkSiteToTag(aOnlyTeam, aOnlyMember);
        await LinkSiteToProperty(a.Id, aOnlyMember);

        var result = await tagsList.GetWorkerTags();

        Assert.That(result.Success, Is.True, result.Message);
        var ids = result.Model.Where(x => x.Id.HasValue).Select(x => x.Id!.Value).ToList();
        Assert.That(ids, Does.Contain(crossTeam));
        Assert.That(ids, Does.Contain(aOnlyTeam));
        Assert.That(result.Model.Where(x => x.Id == crossTeam || x.Id == aOnlyTeam)
            .All(x => x.MemberSiteIds == null), Is.True);
    }

    /// <summary>
    /// A property with no linked workers at all offers no teams (and answers without an
    /// SDK round trip).
    /// </summary>
    [Test]
    public async Task GetWorkerTags_PropertyWithNoWorkers_OffersNoTeams()
    {
        var (_, _, tagsList, _) = await BuildAsync();
        await SeedCrossPropertyTeam();
        var empty = await SeedProperty();

        var result = await tagsList.GetWorkerTags(empty.Id);

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Model, Is.Empty);
    }

    /// <summary>
    /// Offer and deploy agree: every team offered for a property resolves, when assigned
    /// on that property, to exactly the MemberSiteIds the list reported.
    /// </summary>
    [Test]
    public async Task OfferedMembers_EqualDeployTargets_OnSameProperty()
    {
        var (_, resolver, tagsList, _) = await BuildAsync();
        var (_, b, crossTeam, _, _) = await SeedCrossPropertyTeam();
        var (arp, _) = await SeedEvent(b);
        await AddWorkerTagLink(arp.Id, crossTeam);

        var offered = (await tagsList.GetWorkerTags(b.Id)).Model.Single(x => x.Id == crossTeam);
        var effective = await resolver.ResolveEffectiveSiteIdsAsync(arp.Id);

        Assert.That(effective, Is.EquivalentTo(offered.MemberSiteIds!));
    }

    /// <summary>
    /// The property-scoped lookups refuse to run without the plugin DbContext rather than
    /// silently dropping the property clause.
    /// </summary>
    [Test]
    public void PropertyScopedLookup_WithoutPluginDbContext_Throws()
    {
        var membership = new WorkerTagMembershipService(Substitute.For<IEFormCoreService>());

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await membership.GetLiveMemberSiteIdsOnPropertyAsync([1], 1));
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await membership.GetLiveMemberSiteIdsByTagOnPropertyAsync(1));
    }
}
