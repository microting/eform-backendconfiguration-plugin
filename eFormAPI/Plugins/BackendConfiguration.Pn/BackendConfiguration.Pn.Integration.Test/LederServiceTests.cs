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
/// D-L18, D-L10: JWT admin; or an active login whose e-mail resolves (CallerSites) to a live site with a live flag; or
/// the admin role / "eForm admins" by name.
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

    private Task GrantAsync(int siteId, int actorId) =>
        LederAssignmentStore.GrantSiteAsync(BackendConfigurationPnDbContext!, siteId, actorId);

    /// <summary>A login with a live worker (same address) whose site is flagged.</summary>
    private async Task<(EformUser Login, LederTestData.LiveSite Site)> LiveLederAsync(params string[] groups)
    {
        var login = await LederTestData.LoginAsync(BaseDbContext, _userManager, groups);
        var site = await LederTestData.LiveWorkerAsync(_core, login.Email!);
        await GrantAsync(site.Id, login.Id);
        return (login, site);
    }

    /// <summary>A flagged site of some other worker, for the known-liveness cases.</summary>
    private async Task<int> FlaggedSiteIdAsync(int actorId)
    {
        var site = await LederTestData.LiveWorkerAsync(_core, $"{Guid.NewGuid():N}@example.com");
        await GrantAsync(site.Id, actorId);
        return site.Id;
    }

    private async Task<int> UnflaggedSiteIdAsync() =>
        (await LederTestData.LiveWorkerAsync(_core, $"{Guid.NewGuid():N}@example.com")).Id;

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
    public async Task FlaggedLiveSite_IsLeder()
    {
        var (login, _) = await LiveLederAsync("eForm users");
        Assert.That(await Service(login.Id).IsLederOrAdminAsync(), Is.True);
    }

    [Test]
    public async Task FlaggedLiveSite_NoLoginGroupAtAll_IsLeder()
    {
        // D-L16: the flag needs no Web/Arkiv; a login without any group is a Leder once it can log in.
        var (login, _) = await LiveLederAsync();
        Assert.That(await Service(login.Id).IsLederOrAdminAsync(), Is.True);
    }

    [Test]
    public async Task NoFlag_IsNotLeder()
    {
        var login = await LederTestData.LoginAsync(BaseDbContext, _userManager, "eForm users");
        await LederTestData.LiveWorkerAsync(_core, login.Email!);

        Assert.That(await Service(login.Id).IsLederOrAdminAsync(), Is.False);
    }

    [Test]
    public async Task FlagOnAnotherWorkersSite_IsNotLeder()
    {
        var login = await LederTestData.LoginAsync(BaseDbContext, _userManager, "eForm users");
        await LederTestData.LiveWorkerAsync(_core, login.Email!);
        await FlaggedSiteIdAsync(login.Id);

        Assert.That(await Service(login.Id).IsLederOrAdminAsync(), Is.False);
    }

    [Test]
    public async Task RevokedFlag_IsNotLeder()
    {
        var (login, site) = await LiveLederAsync();
        await LederAssignmentStore.RevokeSiteAsync(BackendConfigurationPnDbContext!, site.Id, login.Id);

        Assert.That(await Service(login.Id).IsLederOrAdminAsync(), Is.False);
    }

    [Test]
    public async Task FlaggedSite_IsActiveFalse_IsNotLeder()
    {
        var (login, _) = await LiveLederAsync();
        await BaseDbContext.Users.Where(u => u.Id == login.Id)
            .ExecuteUpdateAsync(x => x.SetProperty(u => u.IsActive, false));

        Assert.That(await Service(login.Id).IsLederOrAdminAsync(), Is.False);
    }

    [Test]
    public async Task FlaggedSite_WorkerResigned_IsNotLeder_AndRehireRestoresIt()
    {
        var (login, site) = await LiveLederAsync();
        await using var sdk = _core.DbContextHelper.GetDbContext();
        var workerUid = (await _core.SiteRead(site.Uid)).WorkerUid;
        await sdk.Workers.Where(w => w.MicrotingUid == workerUid)
            .ExecuteUpdateAsync(x => x.SetProperty(w => w.Resigned, true));

        Assert.That(await Service(login.Id).IsLederOrAdminAsync(), Is.False, "the flag is inert while resigned");

        await sdk.Workers.Where(w => w.MicrotingUid == workerUid)
            .ExecuteUpdateAsync(x => x.SetProperty(w => w.Resigned, false));

        Assert.That(await Service(login.Id).IsLederOrAdminAsync(), Is.True, "rehire on the same site restores Leder (D-L18)");
    }

    [Test]
    public async Task FlaggedSite_RemovedBySiteDelete_WorkerRowLive_IsNotLeder()
    {
        // The core device-users delete calls SiteDelete (Sites + SiteWorkers removed); the Workers row is forced live
        // so the case holds whatever SiteDelete does to it. The row stays flagged but its site is not live (inert).
        var (login, site) = await LiveLederAsync();
        var workerUid = (await _core.SiteRead(site.Uid)).WorkerUid;
        await _core.SiteDelete(site.Uid);
        await using var sdk = _core.DbContextHelper.GetDbContext();
        await sdk.Workers.Where(w => w.MicrotingUid == workerUid)
            .ExecuteUpdateAsync(x => x.SetProperty(w => w.WorkflowState, Constants.WorkflowStates.Created));

        Assert.That(await LederAssignmentStore.IsSiteLederAsync(BackendConfigurationPnDbContext!, site.Id), Is.True,
            "precondition: the flag is still stored");
        Assert.That(await Service(login.Id).IsLederOrAdminAsync(), Is.False);
    }

    [Test]
    public async Task FlaggedSite_SiteRemoved_SiteWorkerStillLive_IsNotLeder()
    {
        var (login, site) = await LiveLederAsync();
        await using var sdk = _core.DbContextHelper.GetDbContext();
        await sdk.Sites.Where(s => s.Id == site.Id)
            .ExecuteUpdateAsync(x => x.SetProperty(s => s.WorkflowState, Constants.WorkflowStates.Removed));

        Assert.That(await Service(login.Id).IsLederOrAdminAsync(), Is.False);
    }

    [Test]
    public async Task EformAdminsMember_NoWorker_NoFlag_IsLeder()
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
    public async Task Known_IsTrusted_WithoutSdkQueries()
    {
        var login = await LederTestData.LoginAsync(BaseDbContext, _userManager);
        var flagged = await FlaggedSiteIdAsync(login.Id);
        var unflagged = await UnflaggedSiteIdAsync();
        var coreHelper = LederTestData.CoreHelper();

        // One service per call: each caches its first answer.
        Assert.That(await Service(login.Id, coreHelper: coreHelper)
            .IsLederOrAdminAsync(new LederLiveness(true, [flagged])), Is.True);
        Assert.That(await Service(login.Id, coreHelper: coreHelper)
            .IsLederOrAdminAsync(new LederLiveness(true, [unflagged])), Is.False);
        Assert.That(await Service(login.Id, coreHelper: coreHelper)
            .IsLederOrAdminAsync(new LederLiveness(false, [flagged])), Is.False, "an inactive login is never a Leder");
        Assert.That(await Service(login.Id, coreHelper: coreHelper)
            .IsLederOrAdminAsync(new LederLiveness(true, [])), Is.False);
        await coreHelper.DidNotReceive().GetCore();
    }

    [Test]
    public async Task Known_TwoLiveSites_OneFlagged_IsLeder()
    {
        // A login can resolve to several live sites; one flagged site is enough.
        var login = await LederTestData.LoginAsync(BaseDbContext, _userManager);
        var flagged = await FlaggedSiteIdAsync(login.Id);
        var unflagged = await UnflaggedSiteIdAsync();

        Assert.That(await Service(login.Id).IsLederOrAdminAsync(new LederLiveness(true, [unflagged, flagged])), Is.True);
    }

    [Test]
    public async Task DirectSecurityGroupRewrite_KeepsTheLeder()
    {
        // What the core Users page does (delete the memberships, insert one group) cannot touch the flag.
        var (login, _) = await LiveLederAsync("eForm users");
        await BaseDbContext.SecurityGroupUsers.Where(x => x.EformUserId == login.Id).ExecuteDeleteAsync();
        await LederTestData.AddMemberAsync(BaseDbContext, login.Id,
            await LederTestData.NewGroupAsync(BaseDbContext, "Bogholderi"));

        Assert.That(await Service(login.Id).IsLederOrAdminAsync(), Is.True);
    }

    [Test]
    public async Task Result_IsComputedOncePerService()
    {
        var login = await LederTestData.LoginAsync(BaseDbContext, _userManager);
        var flagged = await FlaggedSiteIdAsync(login.Id);
        var service = Service(login.Id);

        Assert.That(await service.IsLederOrAdminAsync(new LederLiveness(true, [])), Is.False);
        // Uncached, this second call would be true: it passes a site the test flagged.
        Assert.That(await service.IsLederOrAdminAsync(new LederLiveness(true, [flagged])), Is.False,
            "the first answer is cached");
    }

    [Test]
    public async Task NotLive_ButEformAdminsMember_IsLeder()
    {
        var login = await LederTestData.LoginAsync(BaseDbContext, _userManager,
            BackendConfigurationAssignmentWorkerServiceHelper.EformAdminsSecurityGroupName);

        Assert.That(await Service(login.Id).IsLederOrAdminAsync(new LederLiveness(false, [])), Is.True);
    }

    [Test]
    public async Task IsAdminAsync_OneQuery_KeepsItsMeaning()
    {
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
