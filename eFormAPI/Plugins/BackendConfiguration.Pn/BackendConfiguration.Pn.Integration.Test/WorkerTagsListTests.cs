using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using eFormCore;
using BackendConfiguration.Pn.Services.BackendConfigurationWorkerTagsService;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eForm.Infrastructure;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.eFormApi.BasePn.Infrastructure.Models.Common;
using NSubstitute;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// Coverage for <see cref="BackendConfigurationWorkerTagsService"/> — the plugin-side
/// "teams" list that replaced the calendar's use of the core <c>/api/tags/index</c>
/// endpoint (#1213).
///
/// <para>
/// <b>What "pre-fix" means here.</b> The endpoint under test is NEW, so there is no
/// earlier version of it that these tests would have caught out. The behaviour they
/// are written against is the calendar's OLD source of the list,
/// <c>Core.GetAllTags(false)</c> — which returns the whole <c>Tags</c> table. That
/// baseline is not asserted by narration:
/// <see cref="GetWorkerTags_ExcludesTemplateTagThatCoreGetAllTagsReturns"/> calls
/// <c>Core.GetAllTags(false)</c> in the same test and asserts it DOES return the
/// member-less tag that the new endpoint drops, so the two lists are compared against
/// each other on one seeded fixture. Every other test in this file pins one clause of
/// the new filter and would not have failed on anything that shipped before, because
/// nothing before applied any filter at all.
/// </para>
///
/// <para>
/// <b>What none of this proves.</b> It does not prove that the calendar UI now calls
/// the new endpoint — that is the one-line wiring in
/// <c>calendar-container.component.ts</c>'s <c>loadTeams()</c>, which no backend test
/// can see. It also does not prove the SQL is a single server-side query rather than a
/// client-side filter; the query shape is readable in the service but is not asserted.
/// </para>
///
/// <para>
/// The base fixture does NOT reset the database between tests
/// (<c>ResetDatabasePerTest =&gt; false</c>) and other fixtures leave tags behind, so
/// every assertion below is scoped to the ids this test seeded. The list is never
/// asserted by count or by position.
/// </para>
/// </summary>
[TestFixture]
public class WorkerTagsListTests : TestBaseSetup
{
    /// <summary>
    /// The SDK context handed out AFTER Core has run its EF migrations. The bootstrap
    /// SQL (<c>SQL/420_SDK.sql</c>) creates <c>Workers</c> without <c>Resigned</c>, so a
    /// worker cannot be seeded through the fixture's own <c>MicrotingDbContext</c>.
    /// Same reason as <c>WorkerTagAssignmentTest.SeedSdkSiteWithWorker</c>.
    /// </summary>
    private async Task<(BackendConfigurationWorkerTagsService service, MicrotingDbContext sdk, Core core)>
        BuildAsync()
    {
        var core = await GetCore();
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));
        var service = new BackendConfigurationWorkerTagsService(
            coreHelper, NullLogger<BackendConfigurationWorkerTagsService>.Instance);
        return (service, core.DbContextHelper.GetDbContext(), core);
    }

    private static async Task<int> SeedTagAsync(MicrotingDbContext sdk, bool removed = false)
    {
        var tag = new Tag
        {
            Name = $"tag-{Guid.NewGuid()}",
            WorkflowState = removed
                ? Constants.WorkflowStates.Removed
                : Constants.WorkflowStates.Created
        };
        await sdk.Tags.AddAsync(tag);
        await sdk.SaveChangesAsync();
        return tag.Id;
    }

    /// <summary>
    /// A site plus the Worker + SiteWorker triple real device-user creation leaves
    /// behind. <paramref name="siteRemoved"/> models <c>Core.SiteDelete</c>, which
    /// soft-removes the Site but leaves its SiteTags rows intact.
    /// </summary>
    private static async Task<int> SeedSiteWithWorkerAsync(
        MicrotingDbContext sdk, bool resigned = false, bool siteRemoved = false)
    {
        var language = await sdk.Languages.FirstAsync();
        var site = new Site
        {
            Name = $"site-{Guid.NewGuid()}",
            MicrotingUid = null,
            LanguageId = language.Id,
            WorkflowState = siteRemoved
                ? Constants.WorkflowStates.Removed
                : Constants.WorkflowStates.Created
        };
        await sdk.Sites.AddAsync(site);
        await sdk.SaveChangesAsync();

        var worker = new Worker
        {
            FirstName = $"member-{Guid.NewGuid():N}",
            LastName = "Worker",
            Email = $"{Guid.NewGuid():N}@example.test",
            Resigned = resigned,
            ResignedAtDate = resigned ? DateTime.UtcNow.AddDays(-1) : default,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await sdk.Workers.AddAsync(worker);
        await sdk.SaveChangesAsync();

        await sdk.SiteWorkers.AddAsync(new SiteWorker
        {
            SiteId = site.Id,
            WorkerId = worker.Id,
            WorkflowState = Constants.WorkflowStates.Created
        });
        await sdk.SaveChangesAsync();

        return site.Id;
    }

    private static async Task LinkAsync(MicrotingDbContext sdk, int tagId, int siteId, bool removed = false)
    {
        await sdk.SiteTags.AddAsync(new SiteTag
        {
            TagId = tagId,
            SiteId = siteId,
            WorkflowState = removed
                ? Constants.WorkflowStates.Removed
                : Constants.WorkflowStates.Created
        });
        await sdk.SaveChangesAsync();
    }

    private static List<int> IdsOf(OperationDataResult<List<CommonDictionaryModel>> result)
    {
        Assert.That(result.Success, Is.True, result.Message);
        return result.Model.Where(x => x.Id.HasValue).Select(x => x.Id!.Value).ToList();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 1 — the headline: a tag WITH a member is in, a tag WITHOUT one is out,
    //     and the old source of the list disagrees on the second.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Seeds one worker group (a tag with a live member) and one template-style tag
    /// (a tag with no SiteTag rows at all — exactly what eForm template tagging
    /// creates), then asserts BOTH of:
    /// <list type="bullet">
    /// <item>the new endpoint returns the worker group and not the template tag;</item>
    /// <item><c>Core.GetAllTags(false)</c> — the call behind the core
    /// <c>/api/tags/index</c> the calendar used to read — returns BOTH.</item>
    /// </list>
    ///
    /// <para>
    /// <b>Pre-fix assertion.</b> The second half is the pre-fix behaviour, executed
    /// rather than described: it is green today and stays green, and it is what makes
    /// the first half meaningful. On the pre-fix calendar the list WAS that core list,
    /// so the failing assertion for the old behaviour is
    /// <c>Assert.That(workerTagIds, Does.Not.Contain(templateTagId))</c> — the core
    /// list contains it, as this test's own second assertion block proves on the same
    /// data.
    /// </para>
    ///
    /// <para>
    /// <b>Does not prove:</b> that the seeded member-less tag is genuinely an eForm
    /// template tag. Nothing in the schema distinguishes one, which is the premise of
    /// the whole issue; "no SiteTag rows" is the closest the data model comes.
    /// </para>
    /// </summary>
    [Test]
    public async Task GetWorkerTags_ExcludesTemplateTagThatCoreGetAllTagsReturns()
    {
        var (service, sdk, core) = await BuildAsync();

        var workerGroupId = await SeedTagAsync(sdk);
        await LinkAsync(sdk, workerGroupId, await SeedSiteWithWorkerAsync(sdk));

        // No SiteTag rows: an eForm/template tag, as far as the schema can tell.
        var templateTagId = await SeedTagAsync(sdk);

        var ids = IdsOf(await service.GetWorkerTags());

        Assert.Multiple(() =>
        {
            Assert.That(ids, Does.Contain(workerGroupId),
                "a tag with at least one live member is a worker group and must be listed");
            Assert.That(ids, Does.Not.Contain(templateTagId),
                "a tag with no SiteTag rows is not a worker group and must not be offered as a team");
        });

        // The pre-fix source of the same list, run against the same seeded rows.
        var coreTagIds = (await core.GetAllTags(false)).Select(t => t.Id).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(coreTagIds, Does.Contain(workerGroupId));
            Assert.That(coreTagIds, Does.Contain(templateTagId),
                "premise of #1213: the core tag list the calendar used to read returns template tags too");
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2 — the liveness clauses, one test each
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A tag whose only <c>SiteTags</c> row is soft-removed has no members and must not
    /// be listed. Proves the <c>SiteTag.WorkflowState</c> clause; the control tag in the
    /// same test rules out "the query returns nothing at all".
    /// <para>Does not prove anything about the pre-fix list, which had no filter.</para>
    /// </summary>
    [Test]
    public async Task GetWorkerTags_ExcludesTagWhoseOnlySiteTagIsRemoved()
    {
        var (service, sdk, _) = await BuildAsync();

        var control = await SeedTagAsync(sdk);
        await LinkAsync(sdk, control, await SeedSiteWithWorkerAsync(sdk));

        var removedLinkTag = await SeedTagAsync(sdk);
        await LinkAsync(sdk, removedLinkTag, await SeedSiteWithWorkerAsync(sdk), removed: true);

        var ids = IdsOf(await service.GetWorkerTags());

        Assert.Multiple(() =>
        {
            Assert.That(ids, Does.Contain(control));
            Assert.That(ids, Does.Not.Contain(removedLinkTag),
                "a removed SiteTag row is not membership");
        });
    }

    /// <summary>
    /// A tag whose only member's <c>Site</c> is soft-removed must not be listed.
    /// This clause is load-bearing rather than defensive: <c>Core.SiteDelete</c> removes
    /// the Site and its Worker but leaves the <c>SiteTags</c> rows behind, so deleting
    /// the last device user in a team would otherwise keep that team in the list forever.
    /// <para>
    /// Does not prove: that the same site is also excluded by
    /// <c>CalendarAssignmentResolver</c>. It is NOT — the resolver has no
    /// <c>Site.WorkflowState</c> clause, so this list is deliberately slightly stricter
    /// than the resolver on that one case.
    /// </para>
    /// </summary>
    [Test]
    public async Task GetWorkerTags_ExcludesTagWhoseOnlyMemberSiteIsRemoved()
    {
        var (service, sdk, _) = await BuildAsync();

        var control = await SeedTagAsync(sdk);
        await LinkAsync(sdk, control, await SeedSiteWithWorkerAsync(sdk));

        var deletedMemberTag = await SeedTagAsync(sdk);
        await LinkAsync(sdk, deletedMemberTag, await SeedSiteWithWorkerAsync(sdk, siteRemoved: true));

        var ids = IdsOf(await service.GetWorkerTags());

        Assert.Multiple(() =>
        {
            Assert.That(ids, Does.Contain(control));
            Assert.That(ids, Does.Not.Contain(deletedMemberTag),
                "Core.SiteDelete leaves the SiteTags row behind; a deleted device user is not a member");
        });
    }

    /// <summary>
    /// A tag whose only member has resigned must not be listed: resigned members do not
    /// receive new occurrences (#1184), so such a tag resolves to nobody — the same
    /// outcome as an empty tag. Pins that this list uses the same membership predicate
    /// as <c>CalendarAssignmentResolver</c>, so "listed as a team" implies "would reach
    /// at least one recipient".
    /// <para>
    /// Does not prove: that a tag with one resigned AND one active member is listed —
    /// see <see cref="GetWorkerTags_IncludesTagWithOneResignedAndOneActiveMember"/>.
    /// </para>
    /// </summary>
    [Test]
    public async Task GetWorkerTags_ExcludesTagWhoseOnlyMemberResigned()
    {
        var (service, sdk, _) = await BuildAsync();

        var control = await SeedTagAsync(sdk);
        await LinkAsync(sdk, control, await SeedSiteWithWorkerAsync(sdk));

        var resignedOnlyTag = await SeedTagAsync(sdk);
        await LinkAsync(sdk, resignedOnlyTag, await SeedSiteWithWorkerAsync(sdk, resigned: true));

        var ids = IdsOf(await service.GetWorkerTags());

        Assert.Multiple(() =>
        {
            Assert.That(ids, Does.Contain(control));
            Assert.That(ids, Does.Not.Contain(resignedOnlyTag),
                "a tag whose members have all resigned delivers to nobody, exactly like an empty tag");
        });
    }

    /// <summary>
    /// The boundary of the resigned rule: one resigned member does not sink a tag that
    /// still has an active one. Guards against the exclusion being written as "no
    /// resigned member anywhere in the tag" instead of "at least one live member".
    /// </summary>
    [Test]
    public async Task GetWorkerTags_IncludesTagWithOneResignedAndOneActiveMember()
    {
        var (service, sdk, _) = await BuildAsync();

        var mixedTag = await SeedTagAsync(sdk);
        await LinkAsync(sdk, mixedTag, await SeedSiteWithWorkerAsync(sdk, resigned: true));
        await LinkAsync(sdk, mixedTag, await SeedSiteWithWorkerAsync(sdk));

        var ids = IdsOf(await service.GetWorkerTags());

        Assert.That(ids, Does.Contain(mixedTag),
            "one live member is enough; the resigned member must not remove the whole tag");
    }

    /// <summary>
    /// A soft-removed <c>Tag</c> is not listed even when it still has live members —
    /// parity with <c>SqlController.GetAllTags(false)</c>, which filters on
    /// <c>WorkflowState == Created</c>. Pins that the new list is a strict SUBSET of the
    /// core list and can never surface a tag the core endpoint hid.
    /// </summary>
    [Test]
    public async Task GetWorkerTags_ExcludesRemovedTagEvenWithLiveMembers()
    {
        var (service, sdk, _) = await BuildAsync();

        var removedTag = await SeedTagAsync(sdk, removed: true);
        await LinkAsync(sdk, removedTag, await SeedSiteWithWorkerAsync(sdk));

        var ids = IdsOf(await service.GetWorkerTags());

        Assert.That(ids, Does.Not.Contain(removedTag));
    }
}
