#nullable enable
using BackendConfiguration.Pn.Services.Leder;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>D-L13: one LederAssignments row per login for all time; grant and revoke flip it, with a version row each.</summary>
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

    private Task<EformUser> NewLoginAsync() =>
        LederTestData.LoginAsync(BaseDbContext, _userManager);

    private Task<List<LederAssignment>> RowsAsync(int eformUserId) =>
        Db.LederAssignments.AsNoTracking()
            .Where(x => x.EformUserId == eformUserId).ToListAsync();

    private Task<int> VersionCountAsync(int rowId) =>
        Db.LederAssignmentVersions.AsNoTracking()
            .CountAsync(x => x.LederAssignmentId == rowId);

    [Test]
    public async Task Grant_NewLogin_WritesOneLiveRow_WithTheActor()
    {
        var login = await NewLoginAsync();

        await LederAssignmentStore.GrantAsync(Db, login.Id, _actorId);

        var rows = await RowsAsync(login.Id);
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
        Assert.That(rows[0].CreatedByUserId, Is.EqualTo(_actorId));
        Assert.That(rows[0].UpdatedByUserId, Is.EqualTo(_actorId));
        Assert.That(await LederAssignmentStore.HasLiveRowAsync(Db, login.Id), Is.True);
    }

    [Test]
    public async Task Grant_Twice_LeavesOneRow_AndWritesNoSecondVersion()
    {
        var login = await NewLoginAsync();

        await LederAssignmentStore.GrantAsync(Db, login.Id, _actorId);
        await LederAssignmentStore.GrantAsync(Db, login.Id, _actorId);

        var rows = await RowsAsync(login.Id);
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(await VersionCountAsync(rows[0].Id), Is.EqualTo(1), "an already live row is left as it is");
    }

    [Test]
    public async Task Revoke_SoftDeletes_WithTheActor()
    {
        var login = await NewLoginAsync();
        var revoker = (await NewLoginAsync()).Id;
        await LederAssignmentStore.GrantAsync(Db, login.Id, _actorId);

        await LederAssignmentStore.RevokeAsync(Db, login.Id, revoker);

        var row = (await RowsAsync(login.Id)).Single();
        Assert.That(row.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
        Assert.That(row.UpdatedByUserId, Is.EqualTo(revoker));
        Assert.That(await LederAssignmentStore.HasLiveRowAsync(Db, login.Id), Is.False);
    }

    [Test]
    public async Task Revoke_WithoutARow_CreatesNothing()
    {
        var login = await NewLoginAsync();

        await LederAssignmentStore.RevokeAsync(Db, login.Id, _actorId);

        Assert.That(await RowsAsync(login.Id), Is.Empty, "a revoke never creates a row");
    }

    [Test]
    public async Task Revoke_Twice_WritesNoSecondVersion()
    {
        var login = await NewLoginAsync();
        await LederAssignmentStore.GrantAsync(Db, login.Id, _actorId);

        await LederAssignmentStore.RevokeAsync(Db, login.Id, _actorId);
        await LederAssignmentStore.RevokeAsync(Db, login.Id, _actorId);

        Assert.That(await VersionCountAsync((await RowsAsync(login.Id)).Single().Id), Is.EqualTo(2));
    }

    [Test]
    public async Task GrantRevokeGrant_ReusesTheSameRow()
    {
        // D-L13: the unique index has no WorkflowState filter, so a second Create would throw; the store upserts.
        var login = await NewLoginAsync();

        await LederAssignmentStore.GrantAsync(Db, login.Id, _actorId);
        var firstId = (await RowsAsync(login.Id)).Single().Id;
        await LederAssignmentStore.RevokeAsync(Db, login.Id, _actorId);
        var regranter = (await NewLoginAsync()).Id;
        await LederAssignmentStore.GrantAsync(Db, login.Id, regranter);

        var rows = await RowsAsync(login.Id);
        Assert.That(rows.Select(r => r.Id), Is.EqualTo(new[] { firstId }));
        Assert.That(rows[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
        Assert.That(rows[0].UpdatedByUserId, Is.EqualTo(regranter));
        Assert.That(rows[0].CreatedByUserId, Is.EqualTo(_actorId));
        Assert.That(await VersionCountAsync(firstId), Is.EqualTo(3), "grant, revoke, grant: the audit trail");
    }

    [Test]
    public async Task LiveEformUserIds_HoldsOnlyLiveRows()
    {
        var live = await NewLoginAsync();
        var revoked = await NewLoginAsync();
        var never = await NewLoginAsync();
        await LederAssignmentStore.GrantAsync(Db, live.Id, _actorId);
        await LederAssignmentStore.GrantAsync(Db, revoked.Id, _actorId);
        await LederAssignmentStore.RevokeAsync(Db, revoked.Id, _actorId);

        var ids = await LederAssignmentStore.LiveEformUserIdsAsync(Db);

        Assert.That(ids, Does.Contain(live.Id));
        Assert.That(ids, Does.Not.Contain(revoked.Id));
        Assert.That(ids, Does.Not.Contain(never.Id));
    }
}
