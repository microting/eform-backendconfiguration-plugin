#nullable enable
using BackendConfiguration.Pn.Services.Leder;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>D-L15: one LederAssignments row per SDK site for all time; grant and revoke flip it, with a version row each.</summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class LederAssignmentStoreTests : TestBaseSetup
{
    private int _actorId;
    private UserManager<EformUser> _userManager = null!;

    private BackendConfigurationPnDbContext Db => BackendConfigurationPnDbContext!;

    [SetUp]
    public async Task Arrange()
    {
        await IdentityTestUtils.ReserveEformUserId1Async(BaseDbContext);
        _userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext);
        _actorId = (await NewLoginAsync()).Id;
    }

    [TearDown]
    public void DisposeUserManager() => _userManager.Dispose();

    private Task<EformUser> NewLoginAsync() => LederTestData.LoginAsync(BaseDbContext, _userManager);

    /// <summary>A fresh SDK site; its id is read back, never assumed.</summary>
    private async Task<int> NewSiteIdAsync()
    {
        var language = await MicrotingDbContext!.Languages.FirstAsync();
        var site = new Site
        {
            Name = $"site-{Guid.NewGuid():N}", LanguageId = language.Id, WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddAsync(site);
        await MicrotingDbContext.SaveChangesAsync();
        return site.Id;
    }

    private Task<List<LederAssignment>> RowsAsync(int siteId) =>
        Db.LederAssignments.AsNoTracking().Where(x => x.SiteId == siteId).ToListAsync();

    private Task<int> VersionCountAsync(int rowId) =>
        Db.LederAssignmentVersions.AsNoTracking().CountAsync(x => x.LederAssignmentId == rowId);

    [Test]
    public async Task GrantSite_NewSite_WritesOneLiveRow_WithTheActor_AndNoLoginKey()
    {
        var siteId = await NewSiteIdAsync();

        await LederAssignmentStore.GrantSiteAsync(Db, siteId, _actorId);

        var rows = await RowsAsync(siteId);
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
        Assert.That(rows[0].EformUserId, Is.Null);
        Assert.That((rows[0].CreatedByUserId, rows[0].UpdatedByUserId), Is.EqualTo((_actorId, _actorId)));
        Assert.That(await LederAssignmentStore.IsSiteLederAsync(Db, siteId), Is.True);
    }

    [Test]
    public async Task GrantSite_Twice_LeavesOneRow_AndWritesNoSecondVersion()
    {
        var siteId = await NewSiteIdAsync();

        await LederAssignmentStore.GrantSiteAsync(Db, siteId, _actorId);
        await LederAssignmentStore.GrantSiteAsync(Db, siteId, _actorId);

        var rows = await RowsAsync(siteId);
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(await VersionCountAsync(rows[0].Id), Is.EqualTo(1), "an already live row is left as it is");
    }

    [Test]
    public async Task RevokeSite_SoftDeletes_WithTheActor()
    {
        var siteId = await NewSiteIdAsync();
        var revoker = (await NewLoginAsync()).Id;
        await LederAssignmentStore.GrantSiteAsync(Db, siteId, _actorId);

        await LederAssignmentStore.RevokeSiteAsync(Db, siteId, revoker);

        var row = (await RowsAsync(siteId)).Single();
        Assert.That(row.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
        Assert.That(row.UpdatedByUserId, Is.EqualTo(revoker));
        Assert.That(await LederAssignmentStore.IsSiteLederAsync(Db, siteId), Is.False);
    }

    [Test]
    public async Task RevokeSite_WithoutARow_CreatesNothing()
    {
        var siteId = await NewSiteIdAsync();

        await LederAssignmentStore.RevokeSiteAsync(Db, siteId, _actorId);

        Assert.That(await RowsAsync(siteId), Is.Empty, "a revoke never creates a row");
    }

    [Test]
    public async Task RevokeSite_Twice_WritesNoSecondVersion()
    {
        var siteId = await NewSiteIdAsync();
        await LederAssignmentStore.GrantSiteAsync(Db, siteId, _actorId);

        await LederAssignmentStore.RevokeSiteAsync(Db, siteId, _actorId);
        await LederAssignmentStore.RevokeSiteAsync(Db, siteId, _actorId);

        Assert.That(await VersionCountAsync((await RowsAsync(siteId)).Single().Id), Is.EqualTo(2));
    }

    [Test]
    public async Task GrantRevokeGrant_ReusesTheSameRow()
    {
        // The unique SiteId index has no WorkflowState filter, so a second Create would throw; the store upserts.
        var siteId = await NewSiteIdAsync();

        await LederAssignmentStore.GrantSiteAsync(Db, siteId, _actorId);
        var firstId = (await RowsAsync(siteId)).Single().Id;
        await LederAssignmentStore.RevokeSiteAsync(Db, siteId, _actorId);
        var regranter = (await NewLoginAsync()).Id;
        await LederAssignmentStore.GrantSiteAsync(Db, siteId, regranter);

        var rows = await RowsAsync(siteId);
        Assert.That(rows.Select(r => r.Id), Is.EqualTo(new[] { firstId }));
        Assert.That(rows[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
        Assert.That((rows[0].CreatedByUserId, rows[0].UpdatedByUserId), Is.EqualTo((_actorId, regranter)));
        Assert.That(await VersionCountAsync(firstId), Is.EqualTo(3), "grant, revoke, grant: the audit trail");
    }

    [Test]
    public async Task LiveSiteIds_HoldsOnlyLiveRowsOfTheAskedSites()
    {
        var live = await NewSiteIdAsync();
        var revoked = await NewSiteIdAsync();
        var never = await NewSiteIdAsync();
        var notAsked = await NewSiteIdAsync();
        await LederAssignmentStore.GrantSiteAsync(Db, live, _actorId);
        await LederAssignmentStore.GrantSiteAsync(Db, revoked, _actorId);
        await LederAssignmentStore.RevokeSiteAsync(Db, revoked, _actorId);
        await LederAssignmentStore.GrantSiteAsync(Db, notAsked, _actorId);

        var ids = await LederAssignmentStore.LiveSiteIdsAsync(Db, [live, revoked, never]);

        Assert.That(ids, Is.EquivalentTo(new[] { live }));
    }

    [Test]
    public async Task AnyLiveSite_TrueWhenOneOfSeveralIsFlagged_FalseForNoneOrAnEmptyList()
    {
        var flagged = await NewSiteIdAsync();
        var other = await NewSiteIdAsync();
        await LederAssignmentStore.GrantSiteAsync(Db, flagged, _actorId);

        Assert.That(await LederAssignmentStore.AnyLiveSiteAsync(Db, [other, flagged]), Is.True);
        Assert.That(await LederAssignmentStore.AnyLiveSiteAsync(Db, [other]), Is.False);
        Assert.That(await LederAssignmentStore.AnyLiveSiteAsync(Db, []), Is.False);
    }

    [Test]
    public async Task LegacyLoginRow_IsListedTrackedForConversion()
    {
        var login = await NewLoginAsync();
        var legacy = new LederAssignment { EformUserId = login.Id, CreatedByUserId = _actorId, UpdatedByUserId = _actorId };
        await legacy.Create(Db);
        Db.ChangeTracker.Clear(); // so a tracked result proves the query tracks, not our own insert

        var legacyRows = await LederAssignmentStore.LiveLegacyRowsAsync(Db);

        var listed = legacyRows.Single(x => x.Id == legacy.Id);
        Assert.That(legacyRows.All(x => x.SiteId == null), Is.True);
        Assert.That(Db.Entry(listed).State, Is.EqualTo(EntityState.Unchanged));

        // Retire it: other fixtures share the DB and must not meet a stray legacy row.
        await listed.Delete(Db);
    }
}
