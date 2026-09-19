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
using System.Collections.Generic;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.BackendConfigurationWorkerTagsService;
using BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;
using BackendConfiguration.Pn.Services.CalendarChangeNotification;
using BackendConfiguration.Pn.Services.EventDeployService;
using BackendConfiguration.Pn.Services.WorkerTagMembership;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
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
    /// retracted on the next reconcile, while the own-property member is left deployed and
    /// a COMPLETED case of the other property's member is left untouched.
    /// <para>
    /// "Retracted" is asserted where the engine records it
    /// (<c>RetractSiteForOccurrenceAsync</c>): the member's items-planning
    /// <c>PlanningCaseSite</c> is soft-deleted, its owning <c>PlanningCase</c> is set to
    /// Retracted, and the occurrence's Compliance row no longer names the member's case.
    /// The Compliance row itself is NOT removed: it belongs to the occurrence, and while
    /// another assignee remains, step (g) of <c>ReconcileEventAsync</c> keeps it live
    /// (repointed/released) — it is only deleted when nobody is left.
    /// </para>
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
        var outOfProperty = await SeedDeployedOccurrence(
            planning.Id, futureDate.AddHours(9), memberOnA, OpenCaseStatus);
        var ownProperty = await SeedDeployedOccurrence(
            planning.Id, futureDate.AddHours(10), memberOnB, OpenCaseStatus);

        // A later occurrence where the other property's member already COMPLETED the case:
        // completed cases are immutable and must survive the property restriction.
        var laterDate = futureDate.AddDays(7);
        var completedOutOfProperty = await SeedDeployedOccurrence(
            planning.Id, laterDate.AddHours(9), memberOnA, CompletedCaseStatus);
        var laterOwnProperty = await SeedDeployedOccurrence(
            planning.Id, laterDate.AddHours(10), memberOnB, OpenCaseStatus);

        var deploy = Substitute.For<IEventDeployService>();
        var engine = new CalendarAssignmentReconciliationService(
            BackendConfigurationPnDbContext!, ItemsPlanningPnDbContext!, coreHelper,
            deploy, resolver, Substitute.For<ICalendarChangeNotifier>(),
            TestContextLogger<CalendarAssignmentReconciliationService>.Instance);

        await engine.ReconcileEventAsync(arp.Id);

        async Task<PlanningCaseSite> PcsOf(DeployedOccurrence o) =>
            await ItemsPlanningPnDbContext!.PlanningCaseSites.AsNoTracking()
                .FirstAsync(x => x.Id == o.PlanningCaseSiteId);
        async Task<PlanningCase> PcOf(DeployedOccurrence o) =>
            await ItemsPlanningPnDbContext!.PlanningCases.AsNoTracking()
                .FirstAsync(x => x.Id == o.PlanningCaseId);
        async Task<BcCompliance> ComplianceOf(DeployedOccurrence o) =>
            await BackendConfigurationPnDbContext!.Compliances.AsNoTracking()
                .FirstAsync(x => x.Id == o.ComplianceId);

        var outPcs = await PcsOf(outOfProperty);
        var outPc = await PcOf(outOfProperty);
        var outCompliance = await ComplianceOf(outOfProperty);
        var ownPcs = await PcsOf(ownProperty);
        var ownPc = await PcOf(ownProperty);
        var ownCompliance = await ComplianceOf(ownProperty);
        var completedPcs = await PcsOf(completedOutOfProperty);
        var completedPc = await PcOf(completedOutOfProperty);
        var completedCompliance = await ComplianceOf(completedOutOfProperty);
        var laterOwnPcs = await PcsOf(laterOwnProperty);

        Assert.Multiple(() =>
        {
            // The other property's member: future, not completed -> retracted.
            Assert.That(outPcs.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed),
                "the other property's member's future case is retracted on reconcile "
                + "(PlanningCaseSite soft-deleted)");
            Assert.That(outPc.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Retracted),
                "the retracted member's owning PlanningCase has no live sites left -> Retracted");
            Assert.That(outCompliance.MicrotingSdkCaseId, Is.Not.EqualTo(outOfProperty.SdkCaseId),
                "the occurrence's Compliance row no longer names the retracted member's case");
            Assert.That(outCompliance.WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Removed),
                "the occurrence still has an assignee (the own-property member), so its "
                + "Compliance row is kept (released/repointed), not deleted");

            // Control: the own-property member stays deployed.
            Assert.That(ownPcs.WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Removed),
                "control: the own-property member's case stays deployed");
            Assert.That(ownPc.WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Retracted));
            Assert.That(ownCompliance.WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Removed));
            Assert.That(ownCompliance.MicrotingSdkCaseId, Is.EqualTo(ownProperty.SdkCaseId),
                "control: the own-property member's Compliance row still names their case");
            Assert.That(laterOwnPcs.WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Removed));

            // Completed cases are immutable, even for the out-of-property member.
            Assert.That(completedPcs.WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Removed),
                "a COMPLETED case of the other property's member is never retracted");
            Assert.That(completedPc.WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Retracted));
            Assert.That(completedCompliance.WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Removed));
            Assert.That(completedCompliance.MicrotingSdkCaseId, Is.EqualTo(completedOutOfProperty.SdkCaseId),
                "the completed case's Compliance row is untouched");
        });
        await deploy.DidNotReceive().EnsureComplianceForOccurrenceAsync(
            Arg.Any<AreaRulePlanning>(), Arg.Any<DateTime>(), memberOnA, Arg.Any<CancellationToken>());
    }

    /// <summary>SDK <c>Case.Status</c> of a live, unanswered case.</summary>
    private const int OpenCaseStatus = 66;

    /// <summary>SDK <c>Case.Status</c> of a completed (immutable) case.</summary>
    private const int CompletedCaseStatus = 100;

    private sealed record DeployedOccurrence(
        int SdkCaseId, int PlanningCaseId, int PlanningCaseSiteId, int ComplianceId);

    /// <summary>
    /// The full deployed shape the reconcile retraction path acts on: SDK Case, its
    /// items-planning PlanningCase + PlanningCaseSite, and the occurrence's Compliance row
    /// (shaped like <c>ComplianceReassignmentTests</c>' seeds). MicrotingUid is null so the
    /// SDK CaseDelete cloud call is skipped; the bookkeeping still runs.
    /// </summary>
    private async Task<DeployedOccurrence> SeedDeployedOccurrence(
        int planningId, DateTime deadline, int siteId, int caseStatus)
    {
        var sdkCase = new SdkCase
        {
            SiteId = siteId, Status = caseStatus, MicrotingUid = null,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext!.Cases.AddAsync(sdkCase);
        await MicrotingDbContext.SaveChangesAsync();

        var planningCase = new PlanningCase
        {
            PlanningId = planningId, Status = caseStatus, MicrotingSdkeFormId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.PlanningCases.AddAsync(planningCase);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var planningCaseSite = new PlanningCaseSite
        {
            PlanningId = planningId, PlanningCaseId = planningCase.Id,
            MicrotingSdkSiteId = siteId, MicrotingSdkeFormId = 0,
            MicrotingSdkCaseId = sdkCase.Id, Status = caseStatus,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext.PlanningCaseSites.AddAsync(planningCaseSite);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var compliance = new BcCompliance
        {
            PlanningId = planningId, Deadline = deadline, MicrotingSdkCaseId = sdkCase.Id,
            PlanningCaseSiteId = planningCase.Id,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Compliances.AddAsync(compliance);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        return new DeployedOccurrence(sdkCase.Id, planningCase.Id, planningCaseSite.Id, compliance.Id);
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

    // ─────────────────────────────────────────────────────────────────────────
    // gRPC deploy path (EventDeployService) — #1256
    //
    // EnsureDeployedAsync's C4 narrowing and ResolveSiteLinkageAsync's WorkerTag probe
    // used to read SDK SiteTags directly: no property clause and no live-membership
    // clauses (removed Site, resigned Worker). They now go through the membership
    // service's property-scoped lookup, so a site reaches an event through a team only
    // when the resolver would deploy the team's event to it.
    //
    // The deploy signal asserted is the PlanningCaseSite for (planning, site): the
    // pipeline writes it right after the linkage guard and BEFORE the SDK case (which
    // then fails here — there is no real eForm), so it exists exactly when the site
    // passed candidate narrowing + linkage.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>A fake eForm id: the pipeline must get past linkage, not build a case.</summary>
    private const int DeployEformId = 987_654;

    private sealed class CapturingLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
    {
        public System.Collections.Concurrent.ConcurrentQueue<string> Messages { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Enqueue(formatter(state, exception));
    }

    private EventDeployService BuildDeployService(
        IEFormCoreService coreHelper, WorkerTagMembershipService membership,
        CalendarTaskResponseModel[] rotations, Microsoft.Extensions.Logging.ILogger<EventDeployService> logger)
    {
        var calendar = Substitute.For<IBackendConfigurationCalendarService>();
        calendar.GetTasksForWeek(Arg.Any<CalendarTaskRequestModel>())
            .Returns(new OperationDataResult<List<CalendarTaskResponseModel>>(true, rotations.ToList()));
        var sp = new ServiceCollection().AddSingleton(calendar).BuildServiceProvider();
        return new EventDeployService(
            BackendConfigurationPnDbContext!, ItemsPlanningPnDbContext!, coreHelper, sp, logger, membership);
    }

    private static CalendarTaskResponseModel FutureRotation(AreaRulePlanning arp, Planning planning, DateTime date) =>
        new()
        {
            Id = arp.Id,
            PlanningId = planning.Id,
            EformId = DeployEformId,
            TaskDate = date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            IsFromCompliance = false
        };

    private async Task<bool> DeployedTo(int planningId, int siteId) =>
        await ItemsPlanningPnDbContext!.PlanningCaseSites.AsNoTracking()
            .AnyAsync(x => x.PlanningId == planningId && x.MicrotingSdkSiteId == siteId);

    /// <summary>
    /// Site + Worker + SiteWorker with the given Resigned flag, through a context the Core
    /// handed out (<c>Workers.Resigned</c> only exists after the Core migrated the schema).
    /// </summary>
    private static async Task<int> SeedSdkSiteWithWorker(eFormCore.Core core, bool resigned)
    {
        await using var sdk = core.DbContextHelper.GetDbContext();
        var language = await sdk.Languages.FirstAsync();
        var site = new Site
        {
            Name = $"site-{Guid.NewGuid()}", MicrotingUid = null, LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await sdk.Sites.AddAsync(site);
        await sdk.SaveChangesAsync();
        var worker = new Worker
        {
            FirstName = $"member-{Guid.NewGuid():N}", LastName = "Worker",
            Email = $"{Guid.NewGuid():N}@example.test", Resigned = resigned,
            ResignedAtDate = resigned ? DateTime.UtcNow.AddDays(-1) : default,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await sdk.Workers.AddAsync(worker);
        await sdk.SaveChangesAsync();
        await sdk.SiteWorkers.AddAsync(new SiteWorker
        {
            SiteId = site.Id, WorkerId = worker.Id, WorkflowState = Constants.WorkflowStates.Created
        });
        await sdk.SaveChangesAsync();
        return site.Id;
    }

    /// <summary>
    /// C4 narrowing. A team member linked only to ANOTHER property is not a deploy
    /// candidate for the team's event: the pass ends at "no future-day recurrence rows"
    /// and nothing is written. <b>Fails on the old code</b>: the direct SiteTags read had
    /// no property clause, so the event became a candidate and the WorkerTag linkage let
    /// the deploy through (PlanningCaseSite written).
    /// </summary>
    [Test]
    public async Task EnsureDeployed_TeamMemberLinkedOnlyToAnotherProperty_IsNotDeployed()
    {
        var (membership, _, _, coreHelper) = await BuildAsync();
        var (_, b, tagId, memberOnA, _) = await SeedCrossPropertyTeam();
        var (arp, planning) = await SeedEvent(b);
        await AddWorkerTagLink(arp.Id, tagId);

        var logger = new CapturingLogger<EventDeployService>();
        var date = DateTime.UtcNow.Date.AddDays(3);
        var service = BuildDeployService(coreHelper, membership, [FutureRotation(arp, planning, date)], logger);
        var key = date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        await service.EnsureDeployedAsync(b.Id.ToString(), [], key, key, memberOnA, CancellationToken.None);

        Assert.That(await DeployedTo(planning.Id, memberOnA), Is.False,
            "a team member who only works on another property must not get the team's event deployed (#1256)");
        Assert.That(logger.Messages.Any(m => m.Contains("no future-day recurrence rows to deploy")), Is.True,
            "the candidate must be dropped by the C4 narrowing, not later");
    }

    /// <summary>
    /// C4 narrowing, liveness. A RESIGNED team member linked to the event's property is
    /// not a deploy candidate through the team. <b>Fails on the old code</b>: the direct
    /// SiteTags read had no resigned clause, so the event became a candidate, and the
    /// member's PropertyWorker link then passed the deploy guard.
    /// </summary>
    [Test]
    public async Task EnsureDeployed_ResignedTeamMemberOnProperty_IsNotDeployed()
    {
        var (membership, _, _, coreHelper) = await BuildAsync();
        var property = await SeedProperty();
        var tagId = await SeedSdkTag();
        var resigned = await SeedSdkSiteWithWorker(await coreHelper.GetCore(), resigned: true);
        await LinkSiteToTag(tagId, resigned);
        await LinkSiteToProperty(property.Id, resigned);
        var (arp, planning) = await SeedEvent(property);
        await AddWorkerTagLink(arp.Id, tagId);

        var logger = new CapturingLogger<EventDeployService>();
        var date = DateTime.UtcNow.Date.AddDays(3);
        var service = BuildDeployService(coreHelper, membership, [FutureRotation(arp, planning, date)], logger);
        var key = date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        await service.EnsureDeployedAsync(property.Id.ToString(), [], key, key, resigned, CancellationToken.None);

        Assert.That(await DeployedTo(planning.Id, resigned), Is.False,
            "a resigned worker is no longer a team member and must not be deployed the team's event");
        Assert.That(logger.Messages.Any(m => m.Contains("no future-day recurrence rows to deploy")), Is.True);
    }

    /// <summary>
    /// C4 narrowing, removed property link. A live team member whose only link to the
    /// event's property is a REMOVED PropertyWorker is not a candidate. <b>Fails on the
    /// old code</b> (no property clause at all).
    /// </summary>
    [Test]
    public async Task EnsureDeployed_TeamMemberWithRemovedPropertyLink_IsNotDeployed()
    {
        var (membership, _, _, coreHelper) = await BuildAsync();
        var property = await SeedProperty();
        var tagId = await SeedSdkTag();
        var member = await SeedSdkSite();
        await LinkSiteToTag(tagId, member);
        var link = await LinkSiteToProperty(property.Id, member);
        link.WorkflowState = Constants.WorkflowStates.Removed;
        await BackendConfigurationPnDbContext!.SaveChangesAsync();
        var (arp, planning) = await SeedEvent(property);
        await AddWorkerTagLink(arp.Id, tagId);

        var logger = new CapturingLogger<EventDeployService>();
        var date = DateTime.UtcNow.Date.AddDays(3);
        var service = BuildDeployService(coreHelper, membership, [FutureRotation(arp, planning, date)], logger);
        var key = date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        await service.EnsureDeployedAsync(property.Id.ToString(), [], key, key, member, CancellationToken.None);

        Assert.That(await DeployedTo(planning.Id, member), Is.False);
        Assert.That(logger.Messages.Any(m => m.Contains("no future-day recurrence rows to deploy")), Is.True);
    }

    /// <summary>
    /// Positive control for the three above: a live team member linked to the event's
    /// property IS a candidate and passes the deploy guard — the PlanningCaseSite is
    /// written (the SDK case after it then fails on the fake eForm, which the per-rotation
    /// catch swallows). If this fails, the negatives above prove nothing.
    /// </summary>
    [Test]
    public async Task EnsureDeployed_LiveTeamMemberOnProperty_IsDeployed()
    {
        var (membership, _, _, coreHelper) = await BuildAsync();
        var (_, b, tagId, _, memberOnB) = await SeedCrossPropertyTeam();
        var (arp, planning) = await SeedEvent(b);
        await AddWorkerTagLink(arp.Id, tagId);

        var logger = new CapturingLogger<EventDeployService>();
        var date = DateTime.UtcNow.Date.AddDays(3);
        var service = BuildDeployService(coreHelper, membership, [FutureRotation(arp, planning, date)], logger);
        var key = date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        await service.EnsureDeployedAsync(b.Id.ToString(), [], key, key, memberOnB, CancellationToken.None);

        Assert.That(logger.Messages.Any(m => m.Contains("no future-day recurrence rows to deploy")), Is.False,
            "a live member on the event's property must survive the C4 narrowing");
        Assert.That(await DeployedTo(planning.Id, memberOnB), Is.True,
            "and pass the deploy guard (PlanningCaseSite written before the SDK case)");
    }

    /// <summary>
    /// ResolveSiteLinkageAsync's WorkerTag probe, via the on-demand path
    /// (<c>EnsureComplianceForOccurrenceAsync</c>, which does not pre-narrow). A team
    /// member who is neither a PlanningSite nor linked to the event's property is refused
    /// — the guard throws and writes nothing. <b>Fails on the old code</b>: the direct
    /// SiteTags probe accepted the member as a WorkerTag linkage and the PlanningCaseSite
    /// was written.
    /// </summary>
    [Test]
    public async Task EnsureComplianceForOccurrence_TeamMemberNotLinkedToEventProperty_IsRefused()
    {
        var (membership, _, _, coreHelper) = await BuildAsync();
        var (_, b, tagId, memberOnA, _) = await SeedCrossPropertyTeam();
        var (arp, planning) = await SeedEvent(b);
        planning.RelatedEFormId = DeployEformId;
        await ItemsPlanningPnDbContext!.SaveChangesAsync();
        await AddWorkerTagLink(arp.Id, tagId);

        var service = BuildDeployService(coreHelper, membership, [], new CapturingLogger<EventDeployService>());

        var ex = Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.EnsureComplianceForOccurrenceAsync(arp, DateTime.UtcNow.Date.AddDays(3), memberOnA,
                CancellationToken.None));
        Assert.That(ex!.Message, Does.Contain("refused to deploy"));
        Assert.That(await DeployedTo(planning.Id, memberOnA), Is.False,
            "no PlanningCaseSite may be written for a team member on another property (#1256)");
    }

    /// <summary>
    /// C4 narrowing, removed Site. A team member linked to the event's property whose SDK
    /// <c>Site</c> was deleted (<c>Core.SiteDelete</c> soft-removes the Site and leaves its
    /// SiteTags rows behind) is not a deploy candidate through the team: the pass ends at
    /// "no future-day recurrence rows" and no PlanningCaseSite is written. <b>Fails on the
    /// old code</b>: the direct SiteTags read had no Site clause, so the event became a
    /// candidate; the removed Site row is still found by id, and the member's active
    /// PropertyWorker link then passed the deploy guard (PlanningCaseSite written).
    /// </summary>
    [Test]
    public async Task EnsureDeployed_TeamMemberWithRemovedSite_OnProperty_IsNotDeployed()
    {
        var (membership, _, _, coreHelper) = await BuildAsync();
        var property = await SeedProperty();
        var tagId = await SeedSdkTag();
        var removedSite = await SeedSdkSite();
        await LinkSiteToTag(tagId, removedSite);
        await LinkSiteToProperty(property.Id, removedSite);
        var site = await MicrotingDbContext!.Sites.FirstAsync(x => x.Id == removedSite);
        site.WorkflowState = Constants.WorkflowStates.Removed;
        await MicrotingDbContext.SaveChangesAsync();
        var (arp, planning) = await SeedEvent(property);
        await AddWorkerTagLink(arp.Id, tagId);

        var logger = new CapturingLogger<EventDeployService>();
        var date = DateTime.UtcNow.Date.AddDays(3);
        var service = BuildDeployService(coreHelper, membership, [FutureRotation(arp, planning, date)], logger);
        var key = date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        await service.EnsureDeployedAsync(property.Id.ToString(), [], key, key, removedSite, CancellationToken.None);

        Assert.That(await DeployedTo(planning.Id, removedSite), Is.False,
            "a deleted device user is no longer a live team member and must not be deployed the team's event");
        Assert.That(logger.Messages.Any(m => m.Contains("no future-day recurrence rows to deploy")), Is.True,
            "the candidate must be dropped by the C4 narrowing, not later");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Parity of the #1256 batched / reverse lookups with the per-property rule
    // ─────────────────────────────────────────────────────────────────────────

    private sealed record MembershipMatrix(
        int A, int B, int C, int T1, int T2, int T3,
        int OnlyA, int OnlyB, int Both, int RemovedLinkB, int ResignedB, int RemovedSiteTagT1B, int RemovedSiteB)
    {
        public int[] Properties => [A, B, C];
        public int[] Tags => [T1, T2, T3];
        public int[] Sites => [OnlyA, OnlyB, Both, RemovedLinkB, ResignedB, RemovedSiteTagT1B, RemovedSiteB];
    }

    /// <summary>
    /// Every member state the property-scoped rule distinguishes, against two teams (T1,
    /// T2) plus a team with no members (T3) and a property with no links (C):
    /// <list type="bullet">
    ///   <item><c>OnlyA</c> / <c>OnlyB</c> / <c>Both</c> — live, in T1 and T2, linked to A, B, both.</item>
    ///   <item><c>RemovedLinkB</c> — in T1 and T2, linked to B only by a REMOVED PropertyWorker.</item>
    ///   <item><c>ResignedB</c> — in T1 and T2, linked to B, worker resigned.</item>
    ///   <item><c>RemovedSiteTagT1B</c> — linked to B; its T1 SiteTag is REMOVED, its T2 one live.</item>
    ///   <item><c>RemovedSiteB</c> — in T1 and T2, linked to B, Site soft-deleted.</item>
    /// </list>
    /// Every site, tag and property is fresh, so results can be compared exactly.
    /// </summary>
    private async Task<MembershipMatrix> SeedMembershipMatrix(IEFormCoreService coreHelper)
    {
        var a = await SeedProperty();
        var b = await SeedProperty();
        var c = await SeedProperty();
        var t1 = await SeedSdkTag();
        var t2 = await SeedSdkTag();
        var t3 = await SeedSdkTag();

        var onlyA = await SeedSdkSite();
        var onlyB = await SeedSdkSite();
        var both = await SeedSdkSite();
        var removedLinkB = await SeedSdkSite();
        var resignedB = await SeedSdkSiteWithWorker(await coreHelper.GetCore(), resigned: true);
        var removedSiteTagT1B = await SeedSdkSite();
        var removedSiteB = await SeedSdkSite();

        foreach (var site in new[] { onlyA, onlyB, both, removedLinkB, resignedB, removedSiteB })
        {
            await LinkSiteToTag(t1, site);
            await LinkSiteToTag(t2, site);
        }

        await MicrotingDbContext!.SiteTags.AddAsync(new SiteTag
        {
            TagId = t1, SiteId = removedSiteTagT1B, WorkflowState = Constants.WorkflowStates.Removed
        });
        await MicrotingDbContext.SaveChangesAsync();
        await LinkSiteToTag(t2, removedSiteTagT1B);

        var removed = await MicrotingDbContext.Sites.FirstAsync(x => x.Id == removedSiteB);
        removed.WorkflowState = Constants.WorkflowStates.Removed;
        await MicrotingDbContext.SaveChangesAsync();

        await LinkSiteToProperty(a.Id, onlyA);
        await LinkSiteToProperty(b.Id, onlyB);
        await LinkSiteToProperty(a.Id, both);
        await LinkSiteToProperty(b.Id, both);
        var removedLink = await LinkSiteToProperty(b.Id, removedLinkB);
        removedLink.WorkflowState = Constants.WorkflowStates.Removed;
        await BackendConfigurationPnDbContext!.SaveChangesAsync();
        await LinkSiteToProperty(b.Id, resignedB);
        await LinkSiteToProperty(b.Id, removedSiteTagT1B);
        await LinkSiteToProperty(b.Id, removedSiteB);

        return new MembershipMatrix(a.Id, b.Id, c.Id, t1, t2, t3,
            onlyA, onlyB, both, removedLinkB, resignedB, removedSiteTagT1B, removedSiteB);
    }

    /// <summary>
    /// The batched forward lookup agrees, pair by pair, with the single-property lookup
    /// the deploy resolver uses, and with the hand-derived expectation for every member
    /// state. Every requested pair is a key — including a member-less team (T3) and a
    /// property with no links at all (C). <b>New API (#1256)</b>: does not exist on the
    /// old code; it is what the display paths switched to, so a drift from
    /// <c>GetLiveMemberSiteIdsOnPropertyAsync</c> would make a tile name someone the
    /// team never deploys to.
    /// </summary>
    [Test]
    public async Task ByPropertyAndTag_AgreesWithOnPropertyLookup_ForEveryMemberState()
    {
        var (membership, _, _, coreHelper) = await BuildAsync();
        var m = await SeedMembershipMatrix(coreHelper);

        var pairs = (from p in m.Properties from t in m.Tags select (p, t)).ToList();
        var byPair = await membership.GetLiveMemberSiteIdsByPropertyAndTagAsync(pairs);

        Assert.That(byPair.Keys, Is.EquivalentTo(pairs), "every requested pair is a key, none extra");

        foreach (var (p, t) in pairs)
        {
            var single = await membership.GetLiveMemberSiteIdsOnPropertyAsync([t], p);
            Assert.That(byPair[(p, t)], Is.EquivalentTo(single),
                $"pair ({p}, {t}) must equal the deploy resolver's single-property lookup");
        }

        Assert.Multiple(() =>
        {
            Assert.That(byPair[(m.A, m.T1)], Is.EquivalentTo(new[] { m.OnlyA, m.Both }));
            Assert.That(byPair[(m.A, m.T2)], Is.EquivalentTo(new[] { m.OnlyA, m.Both }));
            Assert.That(byPair[(m.B, m.T1)], Is.EquivalentTo(new[] { m.OnlyB, m.Both }),
                "B/T1: no A-only member, no removed link, no resigned worker, no removed SiteTag, no removed Site");
            Assert.That(byPair[(m.B, m.T2)], Is.EquivalentTo(new[] { m.OnlyB, m.Both, m.RemovedSiteTagT1B }),
                "B/T2: the T1-removed member is still a live T2 member — removal is per team");
            Assert.That(byPair[(m.A, m.T3)], Is.Empty);
            Assert.That(byPair[(m.B, m.T3)], Is.Empty);
            Assert.That(byPair[(m.C, m.T1)], Is.Empty, "a property with no links has no members");
            Assert.That(byPair[(m.C, m.T2)], Is.Empty);
        });
    }

    /// <summary>
    /// The multi-property reverse lookup is the exact inverse of the forward one: for any
    /// set of sites S, <c>T ∈ result[P]</c> iff some site of S is in
    /// <c>forward[(P, T)]</c>; a property none of S is (live-)linked to through any team
    /// is not a key; and the single-property reverse lookup equals <c>result[P]</c>.
    /// Checked for every site alone and for all of them together. <b>New API (#1256)</b>:
    /// the worker filters over multi-property lists use it, so an asymmetry would let a
    /// filter match an event whose tile does not list the filtered worker, or vice versa.
    /// </summary>
    [Test]
    public async Task TagIdsForSitesByProperty_IsTheInverseOfTheForwardLookup()
    {
        var (membership, _, _, coreHelper) = await BuildAsync();
        var m = await SeedMembershipMatrix(coreHelper);

        var pairs = (from p in m.Properties from t in m.Tags select (p, t)).ToList();
        var forward = await membership.GetLiveMemberSiteIdsByPropertyAndTagAsync(pairs);

        var siteSets = m.Sites.Select(s => new[] { s }).Append(m.Sites).ToList();
        foreach (var sites in siteSets)
        {
            var byProperty = await membership.GetTagIdsForSitesByPropertyAsync(sites);
            var label = $"sites [{string.Join(",", sites)}]";

            foreach (var p in m.Properties)
            {
                var expected = m.Tags.Where(t => forward[(p, t)].Overlaps(sites)).ToList();
                if (expected.Count == 0)
                {
                    Assert.That(byProperty.ContainsKey(p), Is.False,
                        $"{label}: property {p} carries none of their live teams and must not be a key");
                }
                else
                {
                    Assert.That(byProperty[p], Is.EquivalentTo(expected), $"{label}: property {p}");
                }

                var onProperty = await membership.GetTagIdsForSitesOnPropertyAsync(sites, p);
                Assert.That(onProperty, Is.EquivalentTo(expected),
                    $"{label}: the single-property reverse lookup must agree on property {p}");
            }
        }

        // Spot checks, so the parity above cannot pass vacuously.
        var removedStates = await membership.GetTagIdsForSitesByPropertyAsync(
            [m.RemovedLinkB, m.ResignedB, m.RemovedSiteB]);
        var t1Removed = await membership.GetTagIdsForSitesOnPropertyAsync([m.RemovedSiteTagT1B], m.B);
        var onlyA = await membership.GetTagIdsForSitesByPropertyAsync([m.OnlyA]);
        Assert.Multiple(() =>
        {
            Assert.That(removedStates, Is.Empty,
                "a removed property link, a resigned worker and a removed Site reach no team on any property");
            Assert.That(t1Removed, Is.EquivalentTo(new[] { m.T2 }));
            Assert.That(onlyA.Keys, Is.EquivalentTo(new[] { m.A }));
            Assert.That(onlyA[m.A], Is.EquivalentTo(new[] { m.T1, m.T2 }));
        });
    }

    /// <summary>
    /// Null/empty input short-circuits all three #1256 lookups to an empty result without
    /// touching either database: the instance below has NO plugin DbContext (any property
    /// read would throw) and a Core helper that must never be asked for a Core.
    /// </summary>
    [Test]
    public async Task PropertyScopedBatchLookups_EmptyOrNullInput_ReturnEmptyWithoutQuerying()
    {
        var coreHelper = Substitute.For<IEFormCoreService>();
        var membership = new WorkerTagMembershipService(coreHelper);

        var pairsEmpty = await membership.GetLiveMemberSiteIdsByPropertyAndTagAsync([]);
        var pairsNull = await membership.GetLiveMemberSiteIdsByPropertyAndTagAsync(null!);
        var byPropertyEmpty = await membership.GetTagIdsForSitesByPropertyAsync([]);
        var byPropertyNull = await membership.GetTagIdsForSitesByPropertyAsync(null!);
        var onPropertyEmpty = await membership.GetTagIdsForSitesOnPropertyAsync([], 1);
        var onPropertyNull = await membership.GetTagIdsForSitesOnPropertyAsync(null!, 1);

        Assert.Multiple(() =>
        {
            Assert.That(pairsEmpty, Is.Empty);
            Assert.That(pairsNull, Is.Empty);
            Assert.That(byPropertyEmpty, Is.Empty);
            Assert.That(byPropertyNull, Is.Empty);
            Assert.That(onPropertyEmpty, Is.Empty);
            Assert.That(onPropertyNull, Is.Empty);
        });
        _ = coreHelper.DidNotReceive().GetCore();
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
