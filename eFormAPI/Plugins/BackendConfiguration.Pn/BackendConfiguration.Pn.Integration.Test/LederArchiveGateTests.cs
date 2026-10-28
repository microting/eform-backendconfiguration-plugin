#nullable enable
using BackendConfiguration.Pn.Controllers;
using BackendConfiguration.Pn.Infrastructure.Helpers;
using BackendConfiguration.Pn.Services.ArchiveAccess;
using BackendConfiguration.Pn.Services.Leder;
using BackendConfiguration.Pn.Services.WorkerTagMembership;
using eFormCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;
using NSubstitute;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>[ArchiveManager] over the real ArchiveAccessService and LederService, against the real databases.</summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class LederArchiveGateTests : TestBaseSetup
{
    private Core _core = null!;
    private UserManager<EformUser> _userManager = null!;

    [SetUp]
    public async Task Arrange()
    {
        await IdentityTestUtils.ReserveEformUserId1Async(BaseDbContext);
        _core = await GetCore();
        _userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext);
    }

    [TearDown]
    public void DisposeUserManager() => _userManager.Dispose();

    private async Task<IActionResult?> GateAsync(int loginId)
    {
        var coreHelper = LederTestData.CoreHelper(_core);
        var userService = LederTestData.Caller(loginId);
        var login = await BaseDbContext.Users.AsNoTracking().SingleAsync(u => u.Id == loginId);
        userService.GetCurrentUserAsync().Returns(Task.FromResult(login));
        var access = new ArchiveAccessService(BackendConfigurationPnDbContext!, userService, coreHelper,
            new WorkerTagMembershipService(coreHelper, BackendConfigurationPnDbContext),
            new LederService(userService, BaseDbContext, BackendConfigurationPnDbContext!, coreHelper),
            TestContextLogger<ArchiveAccessService>.Instance);
        var context = ArchiveManagerAttributeTests.Context(ArchiveManagerAttributeTests.SignedIn(), access);
        await new ArchiveManagerAttribute().OnAuthorizationAsync(context);
        return context.Result;
    }

    [Test]
    public async Task EformAdminsMember_WithTheUserRole_NoFlag_NoWorker_Passes()
    {
        // D-L10: CanManage = true and Indbakke (class-gated by [ArchiveManager]) passes, with no LederAssignments row.
        var login = await LederTestData.LoginAsync(BaseDbContext, _userManager,
            BackendConfigurationAssignmentWorkerServiceHelper.EformAdminsSecurityGroupName);

        Assert.That(await GateAsync(login.Id), Is.Null);
        Assert.That(await LederAssignmentStore.HasLiveRowAsync(BackendConfigurationPnDbContext!, login.Id), Is.False);
    }

    [Test]
    public async Task Leder_WithALiveWorker_Passes()
    {
        var login = await LederTestData.LoginAsync(BaseDbContext, _userManager, "eForm users");
        await LederTestData.LiveWorkerAsync(_core, login.Email!);
        await LederAssignmentStore.GrantAsync(BackendConfigurationPnDbContext!, login.Id, login.Id);

        Assert.That(await GateAsync(login.Id), Is.Null);
    }

    [Test]
    public async Task NormalUser_IsAPlain403()
    {
        var login = await LederTestData.LoginAsync(BaseDbContext, _userManager, "eForm users");
        await LederTestData.LiveWorkerAsync(_core, login.Email!);

        Assert.That(await GateAsync(login.Id), Is.TypeOf<StatusCodeResult>()
            .With.Property(nameof(StatusCodeResult.StatusCode)).EqualTo(StatusCodes.Status403Forbidden));
    }

    [Test]
    public async Task ResignedLeder_IsAccountDisabled()
    {
        var login = await LederTestData.LoginAsync(BaseDbContext, _userManager, "eForm users");
        var siteUid = await LederTestData.LiveWorkerAsync(_core, login.Email!);
        await LederAssignmentStore.GrantAsync(BackendConfigurationPnDbContext!, login.Id, login.Id);
        var workerUid = (await _core.SiteRead(siteUid)).WorkerUid;
        await _core.DbContextHelper.GetDbContext().Workers.Where(w => w.MicrotingUid == workerUid)
            .ExecuteUpdateAsync(x => x.SetProperty(w => w.Resigned, true));

        var result = await GateAsync(login.Id) as ObjectResult;

        Assert.That(result?.StatusCode, Is.EqualTo(StatusCodes.Status403Forbidden));
        Assert.That(result!.Value, Is.EqualTo(new Dictionary<string, string> { ["error"] = "account_disabled" }));
    }
}
