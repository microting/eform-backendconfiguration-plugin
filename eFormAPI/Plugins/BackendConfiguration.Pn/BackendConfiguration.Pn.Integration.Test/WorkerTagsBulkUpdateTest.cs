using BackendConfiguration.Pn.Infrastructure.Helpers;
using BackendConfiguration.Pn.Infrastructure.Models.AssignmentWorker;
using BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eForm.Infrastructure;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using NSubstitute;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// #1380 — <see cref="WorkerTagsBulkUpdateHelper"/>: adding or removing worker tags
/// (SDK <c>SiteTags</c>) on several workers in one request. Add and Remove are
/// idempotent, never touch the workers' other tags, refuse the whole request when a
/// worker or tag does not exist, and reconcile team-assigned events once per request
/// with exactly the tags that changed.
///
/// The SDK schema is shared across the fixture's tests, so every test seeds its own
/// sites and tags and scopes its assertions to their ids.
/// </summary>
[TestFixture]
public class WorkerTagsBulkUpdateTest : TestBaseSetup
{
    private MicrotingDbContext _sdk = null!;
    private ICalendarAssignmentReconciliationService _reconciliation = null!;

    [SetUp]
    public async Task SetUpSdk()
    {
        // The core's context is handed out after the SDK migrations ran.
        var core = await GetCore();
        _sdk = core.DbContextHelper.GetDbContext();
        _reconciliation = Substitute.For<ICalendarAssignmentReconciliationService>();
    }

    [TearDown]
    public async Task DisposeSdk() => await _sdk.DisposeAsync();

    private async Task<int> SeedSite()
    {
        var language = await _sdk.Languages.FirstAsync();
        var site = new Site
        {
            Name = $"bulk-tag-site-{Guid.NewGuid()}",
            LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await _sdk.Sites.AddAsync(site);
        await _sdk.SaveChangesAsync();
        return site.Id;
    }

    private async Task<int> SeedTag(bool removed = false)
    {
        var tag = new Tag
        {
            Name = $"bulk-tag-{Guid.NewGuid()}",
            WorkflowState = removed ? Constants.WorkflowStates.Removed : Constants.WorkflowStates.Created
        };
        await _sdk.Tags.AddAsync(tag);
        await _sdk.SaveChangesAsync();
        return tag.Id;
    }

    private async Task LinkSiteToTag(int siteId, int tagId)
    {
        await new SiteTag { SiteId = siteId, TagId = tagId }.Create(_sdk);
    }

    private Task<List<SiteTag>> LiveRows(IEnumerable<int> siteIds, int tagId)
    {
        var ids = siteIds.ToList();
        return _sdk.SiteTags.AsNoTracking()
            .Where(x => x.SiteId != null && ids.Contains(x.SiteId.Value) && x.TagId == tagId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync();
    }

    private Task<OperationResult> Run(
        List<int> siteIds, List<int> tagIds, WorkerTagsBulkMode mode) =>
        WorkerTagsBulkUpdateHelper.BulkUpdate(
            new WorkerTagsBulkUpdateModel { SiteIds = siteIds, TagIds = tagIds, Mode = mode },
            _sdk, _reconciliation, NullLogger.Instance);

    private Task ExpectReconciledOnceWith(params int[] tagIds) =>
        _reconciliation.Received(1).ReconcileEventsForWorkerTagsAsync(
            Arg.Is<IReadOnlyCollection<int>>(c => c.Count == tagIds.Length && tagIds.All(c.Contains)),
            Arg.Any<CancellationToken>());

    private Task ExpectNotReconciled() =>
        _reconciliation.DidNotReceive().ReconcileEventsForWorkerTagsAsync(
            Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>());

    [Test]
    public async Task Add_ThenRemove_ThreeWorkers_IsIdempotent_AndReconcilesOncePerChange()
    {
        var sites = new List<int> { await SeedSite(), await SeedSite(), await SeedSite() };
        var tag = await SeedTag();

        var added = await Run(sites, [tag], WorkerTagsBulkMode.Add);

        Assert.That(added.Success, Is.True);
        Assert.That(added.Message, Is.EqualTo(WorkerTagsBulkUpdateHelper.SuccessKey));
        var rows = await LiveRows(sites, tag);
        Assert.That(rows.Select(x => x.SiteId!.Value), Is.EquivalentTo(sites), "one live SiteTag per worker");
        await ExpectReconciledOnceWith(tag);

        _reconciliation.ClearReceivedCalls();
        var addedAgain = await Run(sites, [tag], WorkerTagsBulkMode.Add);

        Assert.That(addedAgain.Success, Is.True);
        Assert.That(await LiveRows(sites, tag), Has.Count.EqualTo(3), "adding again creates no duplicates");
        await ExpectNotReconciled();

        _reconciliation.ClearReceivedCalls();
        var removed = await Run(sites, [tag], WorkerTagsBulkMode.Remove);

        Assert.That(removed.Success, Is.True);
        Assert.That(await LiveRows(sites, tag), Is.Empty);
        var removedRows = await _sdk.SiteTags.AsNoTracking()
            .Where(x => x.TagId == tag && x.WorkflowState == Constants.WorkflowStates.Removed)
            .CountAsync();
        Assert.That(removedRows, Is.EqualTo(3), "Remove soft-deletes, like the one-worker edit");
        await ExpectReconciledOnceWith(tag);

        _reconciliation.ClearReceivedCalls();
        var removedAgain = await Run(sites, [tag], WorkerTagsBulkMode.Remove);

        Assert.That(removedAgain.Success, Is.True);
        await ExpectNotReconciled();
    }

    [Test]
    public async Task Add_SkipsWorkersThatAlreadyHaveTheTag_AndReconcilesOnlyChangedTags()
    {
        var siteA = await SeedSite();
        var siteB = await SeedSite();
        var tagNew = await SeedTag();
        var tagEveryoneHas = await SeedTag();
        await LinkSiteToTag(siteA, tagNew);
        await LinkSiteToTag(siteA, tagEveryoneHas);
        await LinkSiteToTag(siteB, tagEveryoneHas);

        var result = await Run([siteA, siteB], [tagNew, tagEveryoneHas], WorkerTagsBulkMode.Add);

        Assert.That(result.Success, Is.True);
        Assert.That((await LiveRows([siteA, siteB], tagNew)).Select(x => x.SiteId!.Value),
            Is.EquivalentTo(new[] { siteA, siteB }));
        Assert.That(await LiveRows([siteA, siteB], tagEveryoneHas), Has.Count.EqualTo(2));
        await ExpectReconciledOnceWith(tagNew);
    }

    [Test]
    public async Task Remove_LeavesTheWorkersOtherTagsAlone()
    {
        var site = await SeedSite();
        var tagToRemove = await SeedTag();
        var tagToKeep = await SeedTag();
        await LinkSiteToTag(site, tagToRemove);
        await LinkSiteToTag(site, tagToKeep);

        var result = await Run([site], [tagToRemove], WorkerTagsBulkMode.Remove);

        Assert.That(result.Success, Is.True);
        Assert.That(await LiveRows([site], tagToRemove), Is.Empty);
        Assert.That(await LiveRows([site], tagToKeep), Has.Count.EqualTo(1));
        await ExpectReconciledOnceWith(tagToRemove);
    }

    [Test]
    public async Task ReconciliationFailure_IsReported_ButTheCommittedTagsStay()
    {
        var site = await SeedSite();
        var tag = await SeedTag();
        _reconciliation.ReconcileEventsForWorkerTagsAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("reconcile failed")));

        var result = await Run([site], [tag], WorkerTagsBulkMode.Add);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo(WorkerTagsBulkUpdateHelper.FailureKey));
        Assert.That(await LiveRows([site], tag), Has.Count.EqualTo(1), "the tag write was committed before reconciling");
    }

    [Test]
    public async Task UnknownWorker_RefusesTheWholeRequest()
    {
        var site = await SeedSite();
        var tag = await SeedTag();
        var missingSiteId = await _sdk.Sites.MaxAsync(x => x.Id) + 1000;

        var result = await Run([site, missingSiteId], [tag], WorkerTagsBulkMode.Add);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo(WorkerTagsBulkUpdateHelper.FailureKey));
        Assert.That(await LiveRows([site], tag), Is.Empty, "nothing is written for the valid worker either");
        await ExpectNotReconciled();
    }

    [Test]
    public async Task RemovedTag_RefusesTheWholeRequest()
    {
        var site = await SeedSite();
        var liveTag = await SeedTag();
        var removedTag = await SeedTag(removed: true);

        var result = await Run([site], [liveTag, removedTag], WorkerTagsBulkMode.Add);

        Assert.That(result.Success, Is.False);
        Assert.That(await LiveRows([site], liveTag), Is.Empty);
        await ExpectNotReconciled();
    }

    [Test]
    public async Task EmptySelectionOrUnknownMode_IsRefused()
    {
        var site = await SeedSite();
        var tag = await SeedTag();

        Assert.That((await Run([], [tag], WorkerTagsBulkMode.Add)).Success, Is.False);
        Assert.That((await Run([site], [], WorkerTagsBulkMode.Add)).Success, Is.False);
        Assert.That((await Run([site], [tag], (WorkerTagsBulkMode)7)).Success, Is.False);
        await ExpectNotReconciled();
    }
}
