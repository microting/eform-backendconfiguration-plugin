#nullable enable
using BackendConfiguration.Pn.Infrastructure.Helpers;
using BackendConfiguration.Pn.Services.Leder;
using eFormCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;
using NSubstitute;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// Spec section 3 "Leder rule", D-L10, D-L13: JWT admin; or a live LederAssignments row on a live login (IsActive, a
/// non-resigned worker with its address, a live SiteWorker); or the admin role / "eForm admins" by name.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class LederServiceTests : TestBaseSetup
{
    private Core _core = null!;
    private IEFormCoreService _coreHelper = null!;
    private UserManager<EformUser> _userManager = null!;

    [SetUp]
    public async Task Arrange()
    {
        await IdentityTestUtils.ReserveEformUserId1Async(BaseDbContext);
        _core = await GetCore();
        _coreHelper = LederTestData.CoreHelper(_core);
        _userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext);
    }

    [TearDown]
    public void DisposeUserManager() => _userManager.Dispose();

    private LederService Service(int userId, bool isAdmin = false, IEFormCoreService? coreHelper = null) =>
        new(LederTestData.Caller(userId, isAdmin), BaseDbContext, BackendConfigurationPnDbContext!,
            coreHelper ?? _coreHelper);

    /// <summary>A login with a live worker (same address) and a live flag.</summary>
    private async Task<(EformUser Login, int SiteUid)> LiveLederAsync(params string[] groups)
    {
        var login = await LederTestData.LoginAsync(BaseDbContext, _userManager, groups);
        var siteUid = await LederTestData.LiveWorkerAsync(_core, login.Email!);
        await LederAssignmentStore.GrantAsync(BackendConfigurationPnDbContext!, login.Id, login.Id);
        return (login, siteUid);
    }

    [Test]
    public async Task JwtAdmin_IsLeder_WithoutTouchingTheSdk()
    {
        var coreHelper = LederTestData.CoreHelper();
        var login = await LederTestData.LoginAsync(BaseDbContext, _userManager);

        Assert.That(await Service(login.Id, isAdmin: true, coreHelper).IsLederOrAdminAsync(), Is.True);
        await coreHelper.DidNotReceive().GetCore();
    }

    [TestCase(0)]
    [TestCase(-1)]
    public async Task NoUserId_IsNotLeder_EvenWithTheAdminRole(int userId) =>
        Assert.That(await Service(userId, isAdmin: true).IsLederOrAdminAsync(), Is.False);

    [Test]
    public async Task LiveRow_AndLiveWorker_IsLeder()
    {
        var (login, _) = await LiveLederAsync("eForm users");
        Assert.That(await Service(login.Id).IsLederOrAdminAsync(), Is.True);
    }

    [Test]
    public async Task NoRow_IsNotLeder()
    {
        var login = await LederTestData.LoginAsync(BaseDbContext, _userManager, "eForm users");
        await LederTestData.LiveWorkerAsync(_core, login.Email!);

        Assert.That(await Service(login.Id).IsLederOrAdminAsync(), Is.False);
    }

    [Test]
    public async Task RevokedRow_IsNotLeder()
    {
        var (login, _) = await LiveLederAsync();
        await LederAssignmentStore.RevokeAsync(BackendConfigurationPnDbContext!, login.Id, login.Id);

        Assert.That(await Service(login.Id).IsLederOrAdminAsync(), Is.False);
    }

    [Test]
    public async Task LiveRow_IsActiveFalse_IsNotLeder()
    {
        var (login, _) = await LiveLederAsync();
        await BaseDbContext.Users.Where(u => u.Id == login.Id)
            .ExecuteUpdateAsync(x => x.SetProperty(u => u.IsActive, false));

        Assert.That(await Service(login.Id).IsLederOrAdminAsync(), Is.False);
    }

    [Test]
    public async Task LiveRow_LoginWithNoWorker_IsNotLeder()
    {
        var login = await LederTestData.LoginAsync(BaseDbContext, _userManager, "Kun arkiv");
        await LederAssignmentStore.GrantAsync(BackendConfigurationPnDbContext!, login.Id, login.Id);

        Assert.That(await Service(login.Id).IsLederOrAdminAsync(), Is.False);
    }

    [Test]
    public async Task LiveRow_WorkerResigned_IsNotLeder()
    {
        var (login, siteUid) = await LiveLederAsync();
        await using var sdk = _core.DbContextHelper.GetDbContext();
        var workerUid = (await _core.SiteRead(siteUid)).WorkerUid;
        await sdk.Workers.Where(w => w.MicrotingUid == workerUid)
            .ExecuteUpdateAsync(x => x.SetProperty(w => w.Resigned, true));

        Assert.That(await Service(login.Id).IsLederOrAdminAsync(), Is.False);
    }

    [Test]
    public async Task LiveRow_SiteRemovedBySiteDelete_WorkerRowLive_IsNotLeder()
    {
        // Review Focus 5: the core device-users delete calls SiteDelete (Sites + SiteWorkers removed). The Workers row
        // is forced live here, so the case holds whatever SiteDelete does to it.
        var (login, siteUid) = await LiveLederAsync();
        var workerUid = (await _core.SiteRead(siteUid)).WorkerUid;
        await _core.SiteDelete(siteUid);
        await using var sdk = _core.DbContextHelper.GetDbContext();
        await sdk.Workers.Where(w => w.MicrotingUid == workerUid)
            .ExecuteUpdateAsync(x => x.SetProperty(w => w.WorkflowState, Constants.WorkflowStates.Created));
        Assert.That(await sdk.SiteWorkers.AnyAsync(sw => sw.Worker.MicrotingUid == workerUid
            && sw.WorkflowState != Constants.WorkflowStates.Removed), Is.False, "precondition: no live SiteWorker");

        Assert.That(await Service(login.Id).IsLederOrAdminAsync(), Is.False);
    }

    [Test]
    public async Task EformAdminsMember_NoWorker_NoRow_IsLeder()
    {
        // D-L10, found by name (no group id is assumed); not subject to liveness.
        var login = await LederTestData.LoginAsync(BaseDbContext, _userManager,
            BackendConfigurationAssignmentWorkerServiceHelper.EformAdminsSecurityGroupName);

        Assert.That(await Service(login.Id).IsLederOrAdminAsync(), Is.True);
    }

    [Test]
    public async Task AdminRoleInTheDatabase_WithoutTheJwtRole_IsLeder()
    {
        var login = await LederTestData.LoginAsync(BaseDbContext, _userManager);
        Assert.That((await _userManager.AddToRoleAsync(login, EformRole.Admin)).Succeeded, Is.True);

        Assert.That(await Service(login.Id).IsLederOrAdminAsync(), Is.True);
    }

    [Test]
    public async Task KnownLive_IsTrusted_WithoutSdkQueries()
    {
        var login = await LederTestData.LoginAsync(BaseDbContext, _userManager);
        await LederAssignmentStore.GrantAsync(BackendConfigurationPnDbContext!, login.Id, login.Id);
        var coreHelper = LederTestData.CoreHelper();

        // Two services: each caches its first answer.
        Assert.That(await Service(login.Id, coreHelper: coreHelper).IsLederOrAdminAsync(knownLive: true), Is.True);
        Assert.That(await Service(login.Id, coreHelper: coreHelper).IsLederOrAdminAsync(knownLive: false), Is.False);
        await coreHelper.DidNotReceive().GetCore();
    }

    [Test]
    public async Task DirectSecurityGroupRewrite_KeepsTheLeder()
    {
        // D-L13: what the core Users page does (delete the memberships, insert one group) cannot touch the flag.
        var (login, _) = await LiveLederAsync("eForm users");
        await BaseDbContext.SecurityGroupUsers.Where(x => x.EformUserId == login.Id).ExecuteDeleteAsync();
        await LederTestData.AddMemberAsync(BaseDbContext, login.Id,
            await LederTestData.NewGroupAsync(BaseDbContext, "Bogholderi"));

        Assert.That(await Service(login.Id).IsLederOrAdminAsync(), Is.True);
    }

    [Test]
    public async Task Result_IsComputedOncePerService()
    {
        // A live row, so an uncached second call with knownLive: true would answer true.
        var login = await LederTestData.LoginAsync(BaseDbContext, _userManager);
        await LederAssignmentStore.GrantAsync(BackendConfigurationPnDbContext!, login.Id, login.Id);
        var service = Service(login.Id);

        Assert.That(await service.IsLederOrAdminAsync(knownLive: false), Is.False);
        Assert.That(await service.IsLederOrAdminAsync(knownLive: true), Is.False, "the first answer is cached");
    }

    [Test]
    public async Task NoRow_KnownLiveTrue_IsNotLeder()
    {
        // The row check runs before knownLive: a live worker alone never makes a Leder.
        var login = await LederTestData.LoginAsync(BaseDbContext, _userManager, "eForm users");

        Assert.That(await Service(login.Id).IsLederOrAdminAsync(knownLive: true), Is.False);
    }

    [Test]
    public async Task LiveRow_NotLive_ButEformAdminsMember_IsLeder()
    {
        // A flagged but not live login still falls through to the admin-by-database rule.
        var login = await LederTestData.LoginAsync(BaseDbContext, _userManager,
            BackendConfigurationAssignmentWorkerServiceHelper.EformAdminsSecurityGroupName);
        await LederAssignmentStore.GrantAsync(BackendConfigurationPnDbContext!, login.Id, login.Id);

        Assert.That(await Service(login.Id).IsLederOrAdminAsync(knownLive: false), Is.True);
    }

    [Test]
    public async Task IsAdminAsync_OneQuery_KeepsItsMeaning()
    {
        // #1484's helper after the merge into one query: role, "eForm admins" by name, nobody else.
        var role = await LederTestData.LoginAsync(BaseDbContext, _userManager);
        Assert.That((await _userManager.AddToRoleAsync(role, EformRole.Admin)).Succeeded, Is.True);
        var group = await LederTestData.LoginAsync(BaseDbContext, _userManager,
            BackendConfigurationAssignmentWorkerServiceHelper.EformAdminsSecurityGroupName);
        var plain = await LederTestData.LoginAsync(BaseDbContext, _userManager, "eForm users");

        Assert.That(await BackendConfigurationAssignmentWorkerServiceHelper.IsAdminAsync(BaseDbContext, role.Id), Is.True);
        Assert.That(await BackendConfigurationAssignmentWorkerServiceHelper.IsAdminAsync(BaseDbContext, group.Id), Is.True);
        Assert.That(await BackendConfigurationAssignmentWorkerServiceHelper.IsAdminAsync(BaseDbContext, plain.Id), Is.False);
    }

    [Test]
    public async Task RemovedEformAdminsMembership_IsNotAdmin_NorLeder()
    {
        // IsAdminAsync applies IsLiveMembership's rule: a soft-deleted "eForm admins" membership grants nothing.
        var login = await LederTestData.LoginAsync(BaseDbContext, _userManager);
        await LederTestData.AddMemberAsync(BaseDbContext, login.Id,
            await LederTestData.GroupByNameAsync(BaseDbContext,
                BackendConfigurationAssignmentWorkerServiceHelper.EformAdminsSecurityGroupName), removed: true);

        Assert.That(await BackendConfigurationAssignmentWorkerServiceHelper.IsAdminAsync(BaseDbContext, login.Id), Is.False);
        Assert.That(await Service(login.Id).IsLederOrAdminAsync(), Is.False);
    }

    [Test]
    public async Task LiveMembership_OfARemovedEformAdminsGroup_IsNotAdmin()
    {
        // The live "eForm admins" group keeps the lowest id, so the other tests' GroupByNameAsync never picks the removed one.
        _ = await LederTestData.GroupByNameAsync(BaseDbContext,
            BackendConfigurationAssignmentWorkerServiceHelper.EformAdminsSecurityGroupName);
        var removedGroupId = await LederTestData.NewGroupAsync(BaseDbContext,
            BackendConfigurationAssignmentWorkerServiceHelper.EformAdminsSecurityGroupName);
        await BaseDbContext.SecurityGroups.Where(g => g.Id == removedGroupId)
            .ExecuteUpdateAsync(x => x.SetProperty(g => g.WorkflowState, Constants.WorkflowStates.Removed));
        var login = await LederTestData.LoginAsync(BaseDbContext, _userManager);
        await LederTestData.AddMemberAsync(BaseDbContext, login.Id, removedGroupId);

        Assert.That(await BackendConfigurationAssignmentWorkerServiceHelper.IsAdminAsync(BaseDbContext, login.Id), Is.False);
        Assert.That(await Service(login.Id).IsLederOrAdminAsync(), Is.False);
    }
}
