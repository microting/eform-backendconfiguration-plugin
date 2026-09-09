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
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.BackendConfigurationWorkerTagsService;
using BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;
using BackendConfiguration.Pn.Services.WorkerTagMembership;
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
using NUnit.Framework;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// Coverage for <see cref="WorkerTagMembershipService"/> — the extraction of the live
/// worker-tag ("team") membership rule that used to be hand-written in three places:
/// <see cref="CalendarAssignmentResolver"/> (who a tag-assigned event DEPLOYS to),
/// <see cref="BackendConfigurationWorkerTagsService"/> (which teams the calendar OFFERS)
/// and <c>BackendConfigurationCalendarService.GetTasksForWeek</c> (which events a team
/// member's FILTER shows).
///
/// <para>
/// <b>The one production behaviour change under test.</b> The predicate was adopted
/// clause for clause from <c>BackendConfigurationWorkerTagsService</c> — the correlated
/// <c>st.TagId == t.Id</c> became <c>st.TagId != null</c> plus a call-site filter, and
/// <c>AsNoTracking()</c> was added, so the clause SET is identical but the text is not
/// literally the same. The only change to production behaviour is that
/// <see cref="CalendarAssignmentResolver"/> had NO
/// <c>Site.WorkflowState != Removed</c> clause and now inherits it. (The extraction made
/// one further change that no running system can observe: the membership dependency
/// became required rather than optional, so a fixture that omits it now fails loudly
/// instead of silently degrading.) That is what
/// <see cref="Resolver_DoesNotReturnSoftDeletedSite_AsDeployTarget"/> pins head-on, but
/// it is not alone: <see cref="AllThreeConsumers_AgreeOnLiveness"/> asserts the same
/// thing through the resolver, and <see cref="ForwardAndReverseLookups_Agree"/> asserts
/// it through both membership lookups. Three tests here reject a rule without that
/// clause, not one.
/// </para>
///
/// <para>
/// Said plainly once, since several comments below lean on it: "fails pre-change" is a
/// REASONED claim throughout this file, never an executed one. No test here can literally
/// be run against the pre-change code — <see cref="WorkerTagMembershipService"/> did not
/// exist and <see cref="CalendarAssignmentResolver"/> took an <c>IEFormCoreService</c>
/// instead of it, so the fixture would not compile.
/// </para>
///
/// <para>
/// <b>What the parity test is for.</b> Nothing used to compare the three copies, so an
/// edit to one could not be caught by the others.
/// <see cref="AllThreeConsumers_AgreeOnLiveness"/> seeds ONE dataset and asks the deploy
/// resolver, the teams dropdown and the shared rule itself about it, so a future edit
/// that changes the answer for one and not the others fails here rather than in
/// production.
/// </para>
///
/// <para>
/// <b>Two of the three consumers, not three.</b> This file constructs
/// <see cref="CalendarAssignmentResolver"/> and
/// <see cref="BackendConfigurationWorkerTagsService"/> for real, but never
/// <c>BackendConfigurationCalendarService</c>: the calendar's assignee filter is
/// represented here only by the membership call its set-construction makes. Its REAL path
/// — <c>GetTasksForWeek</c> end to end — is covered in <c>CalendarWorkerTagFilterTests</c>
/// (<c>Tripwire_RemovedTeamMembership_DoesNotMatch</c>,
/// <c>ResignedTeamMember_DoesNotMatchTeamEvent</c>,
/// <c>Tripwire_SoftDeletedFilterSite_DoesNotMatchTeamEvent</c>), which builds that service
/// with the real membership service and queries a real week. Constructing it here as well
/// would mean duplicating that fixture's whole week-rendering setup for no extra coverage.
/// </para>
///
/// <para>
/// Fixture hygiene: <c>TestBaseSetup.ResetDatabasePerTest</c> is <c>false</c>, so rows
/// accumulate across the tests OF THIS FIXTURE. Not across fixtures —
/// <c>TestBaseSetup</c> declares its <c>MariaDbContainer</c> as an instance field, so
/// every fixture gets its own database. What holds for every test is the part that
/// matters under accumulation: every assertion is scoped to ids the test itself seeded,
/// under GUID-based names — no whole-table counts, no hard-coded ids, no ordering
/// assertions (Danish collation sorts <c>aa</c> after <c>z</c>, so ordering on generated
/// names is a trap). WHAT each test seeds varies: three of the seven seed no property at
/// all, and <see cref="EmptyOrNullInput_ReturnsEmptySet"/> seeds nothing.
/// </para>
///
/// <para>
/// Deliberately seeds no <c>Compliance</c> rows: <c>Compliances</c> is UNIQUE on
/// <c>(PlanningId, Deadline)</c> and this file is about membership, not rendering, so
/// they would add a CI-only collision hazard and no coverage.
/// </para>
///
/// <para>
/// <b>Not covered on purpose.</b> Two workers on one site (one resigned, one live), and
/// a soft-deleted <c>SiteWorker</c> row. <c>Site</c> is 1:1 with <c>Worker</c> in this
/// product, so neither shape occurs; the shipped negated predicate
/// (<c>!Any(sw =&gt; sw.Worker.Resigned)</c>) is deliberately kept rather than rewritten,
/// and asserting on shapes it was never designed for would freeze accidental behaviour.
/// </para>
/// </summary>
[TestFixture]
public class WorkerTagMembershipParityTests : TestBaseSetup
{
    private static readonly DateTime SeriesStart = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// <c>AreaRulePlanningWorkerTags</c> is in no seed file, so its rows survive every
    /// bootstrap while AreaRulePlanning ids can restart — the same accumulation guard
    /// <see cref="WorkerTagAssignmentTest"/> and <see cref="CalendarWorkerTagFilterTests"/>
    /// carry. NUnit runs the base-class [SetUp] first, so the contexts already exist here.
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

    /// <summary>
    /// An SDK Site, optionally already soft-deleted — the state <c>Core.SiteDelete</c>
    /// leaves behind (<c>PnBase.Delete</c> soft-deletes: it sets <c>WorkflowState</c> to
    /// <c>Removed</c>, bumps <c>Version</c>, stamps <c>UpdatedAt</c> and writes a
    /// <c>*Version</c> audit row — the Site row itself stays, and the SiteTags rows are
    /// not touched at all). No SiteWorker either way, so the resigned clause cannot
    /// be what excludes such a site.
    /// </summary>
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

    /// <summary>
    /// Site + Worker + SiteWorker triple, with the worker's <c>Resigned</c> flag set as
    /// asked. Must be seeded through an SDK context handed out AFTER the Core has run its
    /// EF migrations — <c>SQL/420_SDK.sql</c> creates <c>Workers</c> without
    /// <c>Resigned</c>, so the column does not exist on the fixture's own
    /// <c>MicrotingDbContext</c>. Same seeding shape as
    /// <c>WorkerTagAssignmentTest.SeedSdkSiteWithWorker</c>, different return: that one
    /// hands back <c>(int siteId, int workerId)</c>, this one just the site id.
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
            FirstName = $"parity-member-{Guid.NewGuid():N}",
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

    /// <summary>Seeds Area/Property/AreaRule/Planning/AreaRulePlanning; returns the arp.</summary>
    private async Task<AreaRulePlanning> SeedEvent()
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
            Name = $"MembershipParity-{Guid.NewGuid()}", ItemPlanningTagId = 0,
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
            ItemPlanningId = planning.Id, StartDate = SeriesStart, Status = true,
            RepeatType = 2, RepeatEvery = 1,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(arp);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        return arp;
    }

    private async Task AddWorkerTagLink(int arpId, int tagId)
    {
        await BackendConfigurationPnDbContext!.AreaRulePlanningWorkerTags.AddAsync(
            new AreaRulePlanningWorkerTag
            {
                AreaRulePlanningId = arpId, TagId = tagId,
                WorkflowState = Constants.WorkflowStates.Created,
                CreatedByUserId = 1, UpdatedByUserId = 1
            });
        await BackendConfigurationPnDbContext.SaveChangesAsync();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Services under test
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<(WorkerTagMembershipService membership, CalendarAssignmentResolver resolver,
            BackendConfigurationWorkerTagsService tagsList, MicrotingDbContext sdk)>
        BuildAsync()
    {
        var core = await GetCore();
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));

        var membership = new WorkerTagMembershipService(coreHelper);
        return (
            membership,
            new CalendarAssignmentResolver(BackendConfigurationPnDbContext!, membership),
            new BackendConfigurationWorkerTagsService(
                coreHelper, membership, NullLogger<BackendConfigurationWorkerTagsService>.Instance),
            core.DbContextHelper.GetDbContext());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 1 — THE REGRESSION: the resolver no longer deploys to a soft-deleted site
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The only intended behaviour change of the extraction, asserted at the level where
    /// it was reachable.
    ///
    /// <para>
    /// <c>Core.SiteDelete</c> soft-removes the <c>Site</c> and leaves its <c>SiteTags</c>
    /// rows behind entirely, so a deleted device user stayed a member of their old team
    /// forever as far as <see cref="CalendarAssignmentResolver"/> was concerned — and the
    /// resolver decides who actually RECEIVES deployed eForm cases, not merely who is
    /// offered in a picker. The teams list and the calendar filter both already had the
    /// <c>Site.WorkflowState != Removed</c> clause; the resolver did not.
    /// </para>
    ///
    /// <para>
    /// <b>Pre-change this test would FAIL</b> on the first assertion: the removed site came
    /// back as a deploy target. The <c>liveSite</c> half is the positive control — it
    /// proves the removed site is withheld BECAUSE it is removed, and not because the
    /// seed shape resolves to nothing at all (an assertion that only checked for absence
    /// would also pass against a resolver that returned an empty set).
    /// </para>
    /// </summary>
    [Test]
    public async Task Resolver_DoesNotReturnSoftDeletedSite_AsDeployTarget()
    {
        var (membership, resolver, _, _) = await BuildAsync();

        var arp = await SeedEvent();
        var tagId = await SeedSdkTag();

        var liveSite = await SeedSdkSite();
        var deletedSite = await SeedSdkSite(removed: true);

        // Both SiteTags rows stay Created — exactly what SiteDelete leaves behind.
        await LinkSiteToTag(tagId, liveSite);
        await LinkSiteToTag(tagId, deletedSite);

        await AddWorkerTagLink(arp.Id, tagId);

        var effective = await resolver.ResolveEffectiveSiteIdsAsync(arp.Id);

        Assert.Multiple(() =>
        {
            Assert.That(effective, Does.Not.Contain(deletedSite),
                "a soft-deleted Site must not be handed to the deploy path just because "
                + "Core.SiteDelete left its SiteTags row behind");
            Assert.That(effective, Does.Contain(liveSite),
                "control: the live member of the same tag must still resolve — if this "
                + "fails the assertion above proved nothing");
        });

        // Same rule, asked directly of its owner: the resolver is not adding a filter of
        // its own on top, it is inheriting this one.
        var memberSites = await membership.GetLiveMemberSiteIdsAsync([tagId]);

        Assert.Multiple(() =>
        {
            Assert.That(memberSites, Does.Not.Contain(deletedSite));
            Assert.That(memberSites, Does.Contain(liveSite));
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2 — an explicitly named assignee survives deletion (the untouched half)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Tripwire — passes before and after. Only the TAG-derived half of the resolver is
    /// membership-gated. Naming a site on an event via <c>PlanningSites</c> is a fact
    /// about the event and is deliberately NOT filtered, so the new
    /// <c>Site.WorkflowState</c> clause must not leak across into the explicit half. This
    /// pins the asymmetry as a choice rather than an oversight.
    /// </summary>
    [Test]
    public async Task Tripwire_ExplicitlyAssignedSite_IsNotMembershipGated()
    {
        var (_, resolver, _, _) = await BuildAsync();

        var arp = await SeedEvent();
        var deletedSite = await SeedSdkSite(removed: true);

        await BackendConfigurationPnDbContext!.PlanningSites.AddAsync(
            new Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.PlanningSite
            {
                AreaRulePlanningsId = arp.Id, SiteId = deletedSite,
                WorkflowState = Constants.WorkflowStates.Created,
                CreatedByUserId = 1, UpdatedByUserId = 1
            });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var effective = await resolver.ResolveEffectiveSiteIdsAsync(arp.Id);

        Assert.That(effective, Does.Contain(deletedSite),
            "explicit PlanningSites assignment is not membership-gated: the tag-derived "
            + "half of the union is the only half the liveness rule applies to");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3 — empty input short-circuits
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// An empty or absent id list must produce an empty set. EF Core would answer that
    /// correctly anyway — a <c>Contains</c> over an empty collection matches nothing, not
    /// everything — so the short-circuit is about not making a database round trip whose
    /// answer is already known, not about avoiding a wrong answer. HOW EF reaches that
    /// answer is a provider- and version-dependent implementation detail, deliberately
    /// not named here and not asserted on. Both
    /// directions are checked, plus the null overload the resolver relies on when an event
    /// has no worker tags at all.
    /// </summary>
    [Test]
    public async Task EmptyOrNullInput_ReturnsEmptySet()
    {
        var (membership, _, _, _) = await BuildAsync();

        // Awaited individually rather than inside Assert.Multiple: an async lambda there
        // would be an async void the assertion scope cannot wait on.
        var emptyTags = await membership.GetLiveMemberSiteIdsAsync([]);
        var emptySites = await membership.GetTagIdsForSitesAsync([]);
        var nullTags = await membership.GetLiveMemberSiteIdsAsync(null);
        var nullSites = await membership.GetTagIdsForSitesAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(emptyTags, Is.Empty);
            Assert.That(emptySites, Is.Empty);
            Assert.That(nullTags, Is.Empty);
            Assert.That(nullSites, Is.Empty);
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4 — forward and reverse lookups are inverses of one another
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>GetLiveMemberSiteIdsAsync</c> (tag → sites) and <c>GetTagIdsForSitesAsync</c>
    /// (site → tags) read the same rows through the same predicate and must agree. The
    /// resolver uses the first, the calendar's assignee filter uses the second; if they
    /// ever disagree, filtering by a person stops showing the very events that person is
    /// deployed. Exercised on one tag with a live member, a resigned member and a
    /// soft-deleted member, so all three exclusion clauses are in play at once.
    /// </summary>
    [Test]
    public async Task ForwardAndReverseLookups_Agree()
    {
        var (membership, _, _, sdk) = await BuildAsync();

        var tagId = await SeedSdkTag();
        var liveSite = await SeedSdkSiteWithWorker(sdk, resigned: false);
        var resignedSite = await SeedSdkSiteWithWorker(sdk, resigned: true);
        var deletedSite = await SeedSdkSite(removed: true);

        await LinkSiteToTag(tagId, liveSite);
        await LinkSiteToTag(tagId, resignedSite);
        await LinkSiteToTag(tagId, deletedSite);

        var forward = await membership.GetLiveMemberSiteIdsAsync([tagId]);

        Assert.Multiple(() =>
        {
            Assert.That(forward, Does.Contain(liveSite));
            Assert.That(forward, Does.Not.Contain(resignedSite),
                "a resigned member is not a live member (#1184)");
            Assert.That(forward, Does.Not.Contain(deletedSite),
                "a soft-deleted Site is not a live member");
        });

        // Reverse: only the live site resolves back to the tag.
        var tagsOfLive = await membership.GetTagIdsForSitesAsync([liveSite]);
        var tagsOfResigned = await membership.GetTagIdsForSitesAsync([resignedSite]);
        var tagsOfDeleted = await membership.GetTagIdsForSitesAsync([deletedSite]);

        Assert.Multiple(() =>
        {
            Assert.That(tagsOfLive, Does.Contain(tagId),
                "the live member must resolve back to the tag it is a member of");
            Assert.That(tagsOfResigned, Does.Not.Contain(tagId),
                "reverse lookup must apply the same resigned clause as the forward one");
            Assert.That(tagsOfDeleted, Does.Not.Contain(tagId),
                "reverse lookup must apply the same Site.WorkflowState clause");
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 5 — a removed SiteTag row is not membership, in either direction
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Tripwire — passes before and after. Removing a worker from a team soft-deletes the
    /// <c>SiteTags</c> row and nothing else, so that row must stop counting in both
    /// lookups. Locked here because the extraction moved the clause; the control site in
    /// the same tag rules out "the query returns nothing".
    /// </summary>
    [Test]
    public async Task RemovedSiteTagRow_IsNotMembership()
    {
        var (membership, _, _, _) = await BuildAsync();

        var tagId = await SeedSdkTag();
        var currentMember = await SeedSdkSite();
        var formerMember = await SeedSdkSite();

        await LinkSiteToTag(tagId, currentMember);
        await LinkSiteToTag(tagId, formerMember, removed: true);

        var forward = await membership.GetLiveMemberSiteIdsAsync([tagId]);
        var reverse = await membership.GetTagIdsForSitesAsync([formerMember]);

        Assert.Multiple(() =>
        {
            Assert.That(forward, Does.Contain(currentMember));
            Assert.That(forward, Does.Not.Contain(formerMember),
                "a soft-deleted SiteTag row is not membership");
            Assert.That(reverse, Does.Not.Contain(tagId),
                "and the former member must not resolve back to the team either");
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 6 — THE PARITY TEST: two consumers plus the rule itself, one dataset, one answer
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The reason this file exists. The deploy resolver, the teams dropdown and the
    /// calendar's assignee filter each used to carry their own copy of the membership
    /// rule. The dropdown's and the filter's copies were clause-for-clause identical; the
    /// resolver's was one clause short (no <c>Site.WorkflowState</c>), by a decision that
    /// was recorded and deferred. Nothing compared the three, so any LATER edit to one of
    /// them could have gone unnoticed. This test is that comparison.
    ///
    /// <para>
    /// One seeded dataset, three questions:
    /// <list type="bullet">
    /// <item><c>liveTag</c> has a live member — it must be OFFERED as a team, its member
    /// must be a DEPLOY target, and that member must resolve BACK to the tag (which is
    /// what makes the calendar filter show the team's events to them).</item>
    /// <item><c>deadTag</c>'s only member is a soft-deleted Site — it must be absent from
    /// all three answers.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>Pre-change this test would FAIL</b> on the resolver assertion for
    /// <c>deadTag</c>: the teams list already hid it while the resolver still deployed to
    /// it. That disagreement, on one dataset, is exactly the known gap the extraction
    /// closes. (Reasoned, not executed — see the class remarks.)
    /// </para>
    ///
    /// <para>
    /// The teams list is asserted by containment only, never by count or position:
    /// earlier tests in this fixture leave their own tags behind in the same database.
    /// </para>
    /// </summary>
    [Test]
    public async Task AllThreeConsumers_AgreeOnLiveness()
    {
        var (membership, resolver, tagsList, _) = await BuildAsync();

        var liveTag = await SeedSdkTag();
        var liveMember = await SeedSdkSite();
        await LinkSiteToTag(liveTag, liveMember);

        var deadTag = await SeedSdkTag();
        var deletedMember = await SeedSdkSite(removed: true);
        await LinkSiteToTag(deadTag, deletedMember);

        var arp = await SeedEvent();
        await AddWorkerTagLink(arp.Id, liveTag);
        await AddWorkerTagLink(arp.Id, deadTag);

        // Consumer 1 — the deploy resolver.
        var deployTargets = await resolver.ResolveEffectiveSiteIdsAsync(arp.Id);

        // Consumer 2 — the teams dropdown.
        var offeredResult = await tagsList.GetWorkerTags();
        Assert.That(offeredResult.Success, Is.True, offeredResult.Message);
        var offeredTagIds = offeredResult.Model
            .Where(x => x.Id.HasValue).Select(x => x.Id!.Value).ToList();

        // The membership lookup the calendar's assignee filter is built on. This is the
        // shared service being asked about itself, NOT a third consumer:
        // BackendConfigurationCalendarService is never constructed in this file, so a
        // future re-inlining of the rule inside GetTasksForWeek would not fail here. That
        // path is covered end to end in CalendarWorkerTagFilterTests (see the class
        // remarks). What these two calls do pin is that the rule answers the filter's
        // question — site -> tags — the same way it answers the resolver's.
        var tagsOfLiveMember = await membership.GetTagIdsForSitesAsync([liveMember]);
        var tagsOfDeletedMember = await membership.GetTagIdsForSitesAsync([deletedMember]);

        Assert.Multiple(() =>
        {
            // The live team: yes from all three.
            Assert.That(deployTargets, Does.Contain(liveMember),
                "deploy: a live member of an assigned team must receive the event");
            Assert.That(offeredTagIds, Does.Contain(liveTag),
                "dropdown: a team with a live member must be offered");
            Assert.That(tagsOfLiveMember, Does.Contain(liveTag),
                "filter: the live member must resolve to their team, or filtering by "
                + "them hides the team's events");

            // The dead team: no from all three.
            Assert.That(deployTargets, Does.Not.Contain(deletedMember),
                "deploy: a soft-deleted site must not receive the event — this is the "
                + "assertion that failed before the rule was shared");
            Assert.That(offeredTagIds, Does.Not.Contain(deadTag),
                "dropdown: a team whose only member is deleted must not be offered");
            Assert.That(tagsOfDeletedMember, Does.Not.Contain(deadTag),
                "filter: a soft-deleted site must not resolve to its old team");
        });

        // And the shared rule's own answer about the same two tags. Note this is NOT the
        // same set as the dropdown's: GetWorkerTags additionally requires
        // Tag.WorkflowState == Created, which the membership rule does not apply at all,
        // so a soft-removed tag with live members is in liveTagIds and absent from the
        // dropdown (WorkerTagsListTests.GetWorkerTags_ExcludesRemovedTagEvenWithLiveMembers
        // pins exactly that). "Offered as a team" therefore implies "has a deploy-eligible
        // member", but not the reverse — so this asserts the two answers directly rather
        // than asserting they are equal.
        var liveTagIds = await membership.GetTagIdsWithLiveMembersAsync();

        Assert.Multiple(() =>
        {
            Assert.That(liveTagIds, Does.Contain(liveTag));
            Assert.That(liveTagIds, Does.Not.Contain(deadTag));
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 7 — a member-less tag has no live members
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Tripwire — passes before and after. A tag with no <c>SiteTags</c> rows at all is
    /// what eForm TEMPLATE tagging creates; the SDK has one <c>Tags</c> table and no
    /// discriminator column, which is the whole premise of #1213. Such a tag must not be
    /// reported as having live members, and must not resolve to any deploy target.
    /// </summary>
    [Test]
    public async Task TagWithNoMembers_IsNotLive()
    {
        var (membership, resolver, _, _) = await BuildAsync();

        var templateTag = await SeedSdkTag();

        var arp = await SeedEvent();
        await AddWorkerTagLink(arp.Id, templateTag);

        var liveTagIds = await membership.GetTagIdsWithLiveMembersAsync();
        var memberSites = await membership.GetLiveMemberSiteIdsAsync([templateTag]);
        var deployTargets = await resolver.ResolveEffectiveSiteIdsAsync(arp.Id);

        Assert.Multiple(() =>
        {
            Assert.That(liveTagIds, Does.Not.Contain(templateTag));
            Assert.That(memberSites, Is.Empty);
            Assert.That(deployTargets, Is.Empty,
                "an event whose only assignment is a member-less tag deploys to nobody");
        });
    }
}
