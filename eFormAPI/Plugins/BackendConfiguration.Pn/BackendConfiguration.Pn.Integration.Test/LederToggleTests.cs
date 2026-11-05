#nullable enable
using Microsoft.EntityFrameworkCore;
using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// Amendment §4 (D-L15, D-L16): an admin's grant and revoke land on the worker's site whatever its access, and nothing
/// else is written: no group, no Web/Arkiv, no login.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class LederToggleTests : LederWorkerTestBase
{
    [Test]
    public async Task Grant_ByAdmin_WritesOnlyTheSitesFlag()
    {
        var w = await CreateWorkerAsync(web: true);
        var loginId = await LoginIdOfAsync(w);
        var sgu = await BaseDbContext.SecurityGroupUsers.CountAsync();
        var pgp = await BackendConfigurationPnDbContext!.PluginGroupPermissions.CountAsync();
        var pcv = await BackendConfigurationPnDbContext.PluginConfigurationValues.CountAsync();

        var result = await UpdateAsync(w, Admin, isLeader: true);

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(await IsLederAsync(w), Is.True);
        Assert.That(await GroupNamesOfAsync(loginId), Is.EqualTo(new[] { "eForm users" }));
        Assert.That(await BaseDbContext.SecurityGroupUsers.CountAsync(), Is.EqualTo(sgu));
        Assert.That(await BackendConfigurationPnDbContext.PluginGroupPermissions.CountAsync(), Is.EqualTo(pgp));
        Assert.That(await BackendConfigurationPnDbContext.PluginConfigurationValues.CountAsync(), Is.EqualTo(pcv));
        var row = await BackendConfigurationPnDbContext.LederAssignments.AsNoTracking().SingleAsync(x => x.SiteId == w.SiteId);
        Assert.That(row.EformUserId, Is.Null, "new rows carry SiteId only");
        Assert.That(row.UpdatedByUserId, Is.EqualTo(ActorId));
    }

    [Test]
    public async Task GrantRevokeGrant_ThroughMedarbejdere_ReusesOneRow()
    {
        var w = await CreateWorkerAsync();

        Assert.That((await UpdateAsync(w, Admin, isLeader: true)).Success, Is.True);
        Assert.That((await UpdateAsync(w, Admin, isLeader: false)).Success, Is.True);
        Assert.That(await IsLederAsync(w), Is.False);
        Assert.That((await UpdateAsync(w, Admin, isLeader: true)).Success, Is.True);

        var rows = await BackendConfigurationPnDbContext!.LederAssignments.AsNoTracking()
            .Where(x => x.SiteId == w.SiteId).ToListAsync();
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(await BackendConfigurationPnDbContext.LederAssignmentVersions.CountAsync(x => x.LederAssignmentId == rows[0].Id),
            Is.EqualTo(3));
    }

    [Test]
    public async Task Null_ChangesNothing_AndASecondGrantIsANoOp()
    {
        var w = await CreateWorkerAsync();
        Assert.That((await UpdateAsync(w, Admin, isLeader: null)).Success, Is.True);
        Assert.That(await IsLederAsync(w), Is.False);

        Assert.That((await UpdateAsync(w, Admin, isLeader: true)).Success, Is.True);
        Assert.That((await UpdateAsync(w, Admin, isLeader: true)).Success, Is.True);
        Assert.That((await UpdateAsync(w, Admin, isLeader: null)).Success, Is.True);

        Assert.That(await IsLederAsync(w), Is.True);
    }

    [TestCase(true)]
    [TestCase(false)]
    [TestCase(null)]
    public async Task NonAdmin_IsLeaderIsIgnored_AndTheSaveSucceeds(bool? isLeader)
    {
        // Granting: start as a non-Leder. Revoking or leaving it: start as a Leder (created by an admin).
        var w = await CreateWorkerAsync(isLeader: isLeader == true ? null : true);
        var before = await IsLederAsync(w);

        var result = await UpdateAsync(w, NonAdmin, isLeader: isLeader);

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(await IsLederAsync(w), Is.EqualTo(before), "only admins grant or revoke (D-L2)");
    }

    [Test]
    public async Task Create_ByAdmin_WithLeder_FlagsTheNewSite_ByALederNonAdmin_DoesNot()
    {
        var byAdmin = await CreateWorkerAsync(Admin, isLeader: true);
        var byOther = await CreateWorkerAsync(LederNonAdmin, isLeader: true);

        Assert.That(await IsLederAsync(byAdmin), Is.True);
        Assert.That(await IsLederAsync(byOther), Is.False);
    }

    [Test]
    public async Task Grant_WorkerWithNoAccess_IsStored_AndNoAccessIsSwitchedOn()
    {
        // D-L16 ("just store Leder"): Web and Arkiv stay off, the login keeps its groups.
        var w = await CreateWorkerAsync(web: false, archive: false);
        var loginId = await LoginIdOfAsync(w);
        var groups = await GroupNamesOfAsync(loginId);

        var result = await UpdateAsync(w, Admin, isLeader: true, web: false, archive: false);

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(await IsLederAsync(w), Is.True);
        Assert.That(await GroupNamesOfAsync(loginId), Is.EqualTo(groups));
    }

    [Test]
    public async Task Create_ByAdmin_WithLederAndNoAccess_IsStored()
    {
        var w = await CreateWorkerAsync(Admin, web: false, archive: false, isLeader: true);

        Assert.That(await IsLederAsync(w), Is.True);
        Assert.That(await GroupNamesOfAsync(await LoginIdOfAsync(w)), Does.Not.Contain("eForm users").And.Not.Contain("Kun arkiv"));
    }

    [Test]
    public async Task Grant_WorkerWithNoAddress_IsStored_RevokeClearsIt()
    {
        var w = await CreateWorkerAsync(web: false, archive: false, email: "");

        var grant = await UpdateAsync(w, Admin, isLeader: true, web: false, archive: false, email: "");
        Assert.That(grant.Success, Is.True, grant.Message);
        Assert.That(await IsLederAsync(w), Is.True);

        var revoke = await UpdateAsync(w, Admin, isLeader: false, web: false, archive: false, email: "");
        Assert.That(revoke.Success, Is.True, revoke.Message);
        Assert.That(await IsLederAsync(w), Is.False);
    }

    [Test]
    public async Task Grant_OnAnAdminLogin_IsStored()
    {
        // The old modal hid the toggle on admin logins; an admin can now flag them (harmless: they are Leders anyway).
        var w = await CreateWorkerAsync(web: true);
        var login = await UserManager.FindByIdAsync((await LoginIdOfAsync(w)).ToString());
        Assert.That((await UserManager.AddToRoleAsync(login!, EformRole.Admin)).Succeeded, Is.True);

        var result = await UpdateAsync(w, Admin, isLeader: true);

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(await IsLederAsync(w), Is.True);
    }

    [Test]
    public async Task Grant_OnAnOwnerGroupLogin_Passes_GroupsUntouched()
    {
        var w = await CreateWorkerAsync(web: true);
        var loginId = await LoginIdOfAsync(w);
        await MakeOwnerGroupMemberAsync(loginId);
        var groups = await GroupNamesOfAsync(loginId);

        var result = await UpdateAsync(w, Admin, isLeader: true);

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(await IsLederAsync(w), Is.True);
        Assert.That(await GroupNamesOfAsync(loginId), Is.EqualTo(groups));
    }

    [Test]
    public async Task Admin_TurnsWebAndArchiveOff_OnALeder_KeepsTheFlag()
    {
        // The auto-revoke is gone (D-L16): the flag is simply inert while the worker cannot log in.
        var w = await CreateWorkerAsync(isLeader: true);
        var loginId = await LoginIdOfAsync(w);

        var result = await UpdateAsync(w, Admin, web: false, archive: false);

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(await IsLederAsync(w), Is.True);
        Assert.That(await GroupNamesOfAsync(loginId), Is.EqualTo(new[] { "none" }));
    }

    [Test]
    public async Task AddressChangeAndGrant_InOneSave_FlagTheSite_AndTheRenamedLoginIsALeder()
    {
        var w = await CreateWorkerAsync();
        var loginId = await LoginIdOfAsync(w);
        var newEmail = NewEmail("example.org");

        var result = await UpdateAsync(w, Admin, isLeader: true, email: newEmail);

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That((await LoginByIdAsync(loginId)).Email, Is.EqualTo(newEmail));
        Assert.That(await IsLederAsync(w), Is.True);
        Assert.That(await RealLeder(LederTestData.Caller(loginId)).IsLederOrAdminAsync(), Is.True);
    }

    [Test]
    public async Task Admin_ChangesALedersEmail_SiteStaysLeder_AndTheLoginIsALeder()
    {
        // Review Focus 2: the flag follows the site; the renamed login resolves to it through the new address.
        var w = await CreateWorkerAsync(isLeader: true);
        var loginId = await LoginIdOfAsync(w);

        var result = await UpdateAsync(w, Admin, email: NewEmail("example.org"));

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(await IsLederAsync(w), Is.True);
        Assert.That(await RealLeder(LederTestData.Caller(loginId)).IsLederOrAdminAsync(), Is.True);
    }

    [Test]
    public async Task NoCaller_KeepsTodaysBehaviour_NoFlagWritten()
    {
        var w = await CreateWorkerAsync();

        var result = await UpdateAsync(w, caller: null, isLeader: true);

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(await IsLederAsync(w), Is.False);
    }
}
