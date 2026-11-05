#nullable enable
using BackendConfiguration.Pn.Services.Leder;
using eFormCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using NSubstitute;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// D-L19: live rows keyed by login move to the sites of that login's workers and are retired. NonParallelizable: the
/// conversion handles every legacy row in the shared database, including rows other fixtures create.
/// </summary>
[NonParallelizable]
[TestFixture]
public class LederLegacyConversionTests : TestBaseSetup
{
    private Core _core = null!;
    private UserManager<EformUser> _userManager = null!;
    private int _actorId;

    [SetUp]
    public async Task Arrange()
    {
        await IdentityTestUtils.ReserveEformUserId1Async(BaseDbContext);
        _core = await GetCore();
        _userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext);
        _actorId = (await LederTestData.LoginAsync(BaseDbContext, _userManager)).Id;
    }

    [TearDown]
    public void DisposeUserManager() => _userManager.Dispose();

    private LederLegacyConversionService Sut() =>
        new(BackendConfigurationPnDbContext!, BaseDbContext, LederTestData.CoreHelper(_core),
            Substitute.For<ILogger<LederLegacyConversionService>>());

    private async Task<LederAssignment> LegacyRowAsync(int eformUserId)
    {
        var row = new LederAssignment { EformUserId = eformUserId, CreatedByUserId = _actorId, UpdatedByUserId = _actorId };
        await row.Create(BackendConfigurationPnDbContext!);
        return row;
    }

    private Task<LederAssignment> ReloadAsync(int rowId) =>
        BackendConfigurationPnDbContext!.LederAssignments.AsNoTracking().SingleAsync(x => x.Id == rowId);

    private Task<bool> IsSiteLederAsync(int siteId) =>
        LederAssignmentStore.IsSiteLederAsync(BackendConfigurationPnDbContext!, siteId);

    [Test]
    public async Task LegacyRow_MovesToTheWorkersSite_WithTheRowsActor_AndIsRetired()
    {
        var login = await LederTestData.LoginAsync(BaseDbContext, _userManager, "Kun arkiv");
        var site = await LederTestData.LiveWorkerAsync(_core, login.Email!);
        var legacy = await LegacyRowAsync(login.Id);

        await Sut().RunAsync();

        Assert.That(await IsSiteLederAsync(site.Id), Is.True);
        var siteRow = await BackendConfigurationPnDbContext!.LederAssignments.AsNoTracking().SingleAsync(x => x.SiteId == site.Id);
        Assert.That((siteRow.CreatedByUserId, siteRow.EformUserId), Is.EqualTo((_actorId, (int?)null)));
        Assert.That((await ReloadAsync(legacy.Id)).WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
        var leder = new LederService(LederTestData.Caller(login.Id), BaseDbContext, BackendConfigurationPnDbContext!,
            LederTestData.CoreHelper(_core));
        Assert.That(await leder.IsLederOrAdminAsync(), Is.True, "the login is still a Leder, now through its site");
    }

    [Test]
    public async Task LegacyRow_OfAResignedWorker_MovesToItsSite()
    {
        // Review Focus 3: a resigned Leder keeps the flag (inert) so a rehire restores it.
        var login = await LederTestData.LoginAsync(BaseDbContext, _userManager, "Kun arkiv");
        var site = await LederTestData.LiveWorkerAsync(_core, login.Email!);
        var workerUid = (await _core.SiteRead(site.Uid)).WorkerUid;
        await _core.DbContextHelper.GetDbContext().Workers.Where(w => w.MicrotingUid == workerUid)
            .ExecuteUpdateAsync(x => x.SetProperty(w => w.Resigned, true));
        var legacy = await LegacyRowAsync(login.Id);

        await Sut().RunAsync();

        Assert.That(await IsSiteLederAsync(site.Id), Is.True);
        Assert.That((await ReloadAsync(legacy.Id)).WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
    }

    [Test]
    public async Task LegacyRow_OfALoginWithoutAWorker_IsRetired_AndFlagsNothing()
    {
        var login = await LederTestData.LoginAsync(BaseDbContext, _userManager, "Kun arkiv");
        var legacy = await LegacyRowAsync(login.Id);

        await Sut().RunAsync();

        Assert.That((await ReloadAsync(legacy.Id)).WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
        Assert.That(await BackendConfigurationPnDbContext!.LederAssignments.AsNoTracking()
            .AnyAsync(x => x.SiteId != null && x.CreatedByUserId == _actorId), Is.False);
    }

    [Test]
    public async Task LegacyRow_OfADeletedLogin_IsRetired()
    {
        var login = await LederTestData.LoginAsync(BaseDbContext, _userManager);
        var legacy = await LegacyRowAsync(login.Id);
        Assert.That((await _userManager.DeleteAsync(login)).Succeeded, Is.True, "precondition");

        await Sut().RunAsync();

        Assert.That((await ReloadAsync(legacy.Id)).WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
    }

    [Test]
    public async Task LegacyRow_WhoseSiteIsAlreadyFlagged_KeepsOneSiteRow()
    {
        var login = await LederTestData.LoginAsync(BaseDbContext, _userManager, "Kun arkiv");
        var site = await LederTestData.LiveWorkerAsync(_core, login.Email!);
        await LederAssignmentStore.GrantSiteAsync(BackendConfigurationPnDbContext!, site.Id, login.Id);
        var legacy = await LegacyRowAsync(login.Id);

        await Sut().RunAsync();

        Assert.That(await BackendConfigurationPnDbContext!.LederAssignments.AsNoTracking().CountAsync(x => x.SiteId == site.Id),
            Is.EqualTo(1));
        Assert.That(await IsSiteLederAsync(site.Id), Is.True);
        Assert.That((await ReloadAsync(legacy.Id)).WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
    }

    [Test]
    public async Task SecondRun_IsANoOp()
    {
        var login = await LederTestData.LoginAsync(BaseDbContext, _userManager, "Kun arkiv");
        var site = await LederTestData.LiveWorkerAsync(_core, login.Email!);
        var legacy = await LegacyRowAsync(login.Id);
        await Sut().RunAsync();
        var legacyVersion = (await ReloadAsync(legacy.Id)).Version;
        var siteRow = await BackendConfigurationPnDbContext!.LederAssignments.AsNoTracking().SingleAsync(x => x.SiteId == site.Id);

        await Sut().RunAsync();

        Assert.That((await ReloadAsync(legacy.Id)).Version, Is.EqualTo(legacyVersion), "legacy row untouched");
        Assert.That((await BackendConfigurationPnDbContext.LederAssignments.AsNoTracking().SingleAsync(x => x.SiteId == site.Id)).Version,
            Is.EqualTo(siteRow.Version), "nothing was written");
    }
}
