#nullable enable
using BackendConfiguration.Pn.Services.Leder;
using Microsoft.EntityFrameworkCore;
using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>Spec section 4 "Leder toggle" and D-L13: admin-only grant and revoke on the resolved login, nothing else written.</summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class LederToggleTests : LederWorkerTestBase
{
    [Test]
    public async Task Grant_ByAdmin_WritesOnlyTheFlag()
    {
        // D-L13: no SecurityGroupUsers, PluginGroupPermissions or PluginConfigurationValues row for Leder.
        var w = await CreateWorkerAsync(web: true);
        var loginId = await LoginIdOfAsync(w);
        var sgu = await BaseDbContext.SecurityGroupUsers.CountAsync();
        var pgp = await BackendConfigurationPnDbContext!.PluginGroupPermissions.CountAsync();
        var pcv = await BackendConfigurationPnDbContext.PluginConfigurationValues.CountAsync();

        var result = await UpdateAsync(w, Admin, isLeader: true);

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(await IsLederAsync(loginId), Is.True);
        Assert.That(await GroupNamesOfAsync(loginId), Is.EqualTo(new[] { "eForm users" }));
        Assert.That(await BaseDbContext.SecurityGroupUsers.CountAsync(), Is.EqualTo(sgu));
        Assert.That(await BackendConfigurationPnDbContext.PluginGroupPermissions.CountAsync(), Is.EqualTo(pgp));
        Assert.That(await BackendConfigurationPnDbContext.PluginConfigurationValues.CountAsync(), Is.EqualTo(pcv));
        var row = await BackendConfigurationPnDbContext.LederAssignments.AsNoTracking().SingleAsync(x => x.EformUserId == loginId);
        Assert.That(row.UpdatedByUserId, Is.EqualTo(ActorId));
    }

    [Test]
    public async Task GrantRevokeGrant_ThroughMedarbejdere_ReusesOneRow()
    {
        var w = await CreateWorkerAsync();
        var loginId = await LoginIdOfAsync(w);

        Assert.That((await UpdateAsync(w, Admin, isLeader: true)).Success, Is.True);
        Assert.That((await UpdateAsync(w, Admin, isLeader: false)).Success, Is.True);
        Assert.That(await IsLederAsync(loginId), Is.False);
        Assert.That((await UpdateAsync(w, Admin, isLeader: true)).Success, Is.True);

        var rows = await BackendConfigurationPnDbContext!.LederAssignments.AsNoTracking()
            .Where(x => x.EformUserId == loginId).ToListAsync();
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(await BackendConfigurationPnDbContext.LederAssignmentVersions.CountAsync(x => x.LederAssignmentId == rows[0].Id),
            Is.EqualTo(3));
    }

    [Test]
    public async Task Null_ChangesNothing_AndASecondGrantIsANoOp()
    {
        var w = await CreateWorkerAsync();
        var loginId = await LoginIdOfAsync(w);
        Assert.That((await UpdateAsync(w, Admin, isLeader: null)).Success, Is.True);
        Assert.That(await IsLederAsync(loginId), Is.False);

        Assert.That((await UpdateAsync(w, Admin, isLeader: true)).Success, Is.True);
        Assert.That((await UpdateAsync(w, Admin, isLeader: true)).Success, Is.True);
        Assert.That((await UpdateAsync(w, Admin, isLeader: null)).Success, Is.True);

        Assert.That(await IsLederAsync(loginId), Is.True);
    }

    [TestCase(true)]
    [TestCase(false)]
    [TestCase(null)]
    public async Task NonAdmin_IsLeaderIsIgnored_AndTheSaveSucceeds(bool? isLeader)
    {
        // Granting: start as a non-Leder. Revoking or leaving it: start as a Leder (created by an admin).
        var w = await CreateWorkerAsync(isLeader: isLeader == true ? null : true);
        var loginId = await LoginIdOfAsync(w);
        var before = await IsLederAsync(loginId);

        var result = await UpdateAsync(w, NonAdmin, isLeader: isLeader);

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(await IsLederAsync(loginId), Is.EqualTo(before), "only admins grant or revoke (D-L2)");
    }

    [Test]
    public async Task Create_ByAdmin_WithLeder_MakesALeder_ByALederNonAdmin_DoesNot()
    {
        var byAdmin = await CreateWorkerAsync(Admin, isLeader: true);
        var byOther = await CreateWorkerAsync(LederNonAdmin, isLeader: true);

        Assert.That(await IsLederAsync(await LoginIdOfAsync(byAdmin)), Is.True);
        Assert.That(await IsLederAsync(await LoginIdOfAsync(byOther)), Is.False);
    }

    [Test]
    public async Task Grant_WithWebAndArchiveOff_IsRefused_AndWritesNothing()
    {
        var w = await CreateWorkerAsync(web: true);
        var loginId = await LoginIdOfAsync(w);

        var result = await UpdateAsync(w, Admin, isLeader: true, web: false, archive: false, lastName: NewLastName("Roe"));

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo(LederMessages.LederRequiresLogin));
        Assert.That(await GroupNamesOfAsync(loginId), Is.EqualTo(new[] { "eForm users" }), "refused before any write");
        Assert.That(await IsLederAsync(loginId), Is.False);
        Assert.That(await SiteExistsAsync(w.LastName), Is.True, "the SDK site keeps its name");
        Assert.That(await MicrotingDbContext!.Workers.AsNoTracking().AnyAsync(x => x.Email == w.Email && x.LastName == w.LastName),
            Is.True, "the SDK worker is unchanged");
    }

    [Test]
    public async Task Grant_TogetherWithSwitchingWebOn_InOneSave()
    {
        var w = await CreateWorkerAsync(web: false, archive: false);
        var loginId = await LoginIdOfAsync(w);
        Assert.That(await GroupNamesOfAsync(loginId), Is.EqualTo(new[] { "none" }), "precondition");

        var result = await UpdateAsync(w, Admin, isLeader: true, web: true);

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(await GroupNamesOfAsync(loginId), Is.EqualTo(new[] { "eForm users" }));
        Assert.That(await IsLederAsync(loginId), Is.True);
    }

    [Test]
    public async Task Grant_WorkerWithNoAddress_IsRefused_RevokeIsANoOpSuccess()
    {
        // M5: no address → a synthetic user_…@microting.invalid login, which the list shows as "no login".
        var w = await CreateWorkerAsync(web: false, archive: false, email: "");

        Assert.That((await UpdateAsync(w, Admin, isLeader: true, email: "")).Message,
            Is.EqualTo(LederMessages.LederRequiresLogin));
        Assert.That((await UpdateAsync(w, Admin, isLeader: false, email: "")).Success, Is.True);
        var syntheticLoginIds = await BaseDbContext.Users.AsNoTracking()
            .Where(u => u.Email!.Contains("invalid")).Select(u => u.Id).ToListAsync();
        var lederIds = await LederAssignmentStore.LiveEformUserIdsAsync(BackendConfigurationPnDbContext!);
        Assert.That(lederIds.Intersect(syntheticLoginIds), Is.Empty);
    }

    [Test]
    public async Task Grant_OnAnOwnerGroupLogin_Passes_GroupsUntouched()
    {
        // The flag touches no field of the login, so an unmanaged (owner-group) login can be a Leder.
        var w = await CreateWorkerAsync(web: true);
        var loginId = await LoginIdOfAsync(w);
        await MakeOwnerGroupMemberAsync(loginId);
        var groups = await GroupNamesOfAsync(loginId);

        var result = await UpdateAsync(w, Admin, isLeader: true);

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(await IsLederAsync(loginId), Is.True);
        Assert.That(await GroupNamesOfAsync(loginId), Is.EqualTo(groups));
    }

    [Test]
    public async Task UnmanagedLeder_AdminSavesWithWebAndArchiveOff_StaysLeder_GroupsUntouched()
    {
        // Review Focus 2: an owner-group login reads Web = Arkiv = off; an ordinary save changes no membership.
        var w = await CreateWorkerAsync(web: true);
        var loginId = await LoginIdOfAsync(w);
        await MakeOwnerGroupMemberAsync(loginId);
        Assert.That((await UpdateAsync(w, Admin, isLeader: true)).Success, Is.True);
        var groups = await GroupNamesOfAsync(loginId);

        var result = await UpdateAsync(w, Admin, web: false, archive: false, lastName: NewLastName("Roe"));

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(await IsLederAsync(loginId), Is.True, "no auto-revoke for an unmanaged login");
        Assert.That(await GroupNamesOfAsync(loginId), Is.EqualTo(groups));
    }

    [Test]
    public async Task AddressChangeAndGrant_InOneSave_LandOnTheOldLoginId()
    {
        // Review Focus 4: the login found by the OLD address is renamed in place and gets the flag.
        var w = await CreateWorkerAsync();
        var loginId = await LoginIdOfAsync(w);
        var newEmail = NewEmail("example.org");

        var result = await UpdateAsync(w, Admin, isLeader: true, email: newEmail);

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That((await LoginByIdAsync(loginId)).Email, Is.EqualTo(newEmail));
        Assert.That(await IsLederAsync(loginId), Is.True);
        Assert.That(await BaseDbContext.Users.CountAsync(u => u.Email == newEmail), Is.EqualTo(1));
    }

    [Test]
    public async Task Admin_ChangesALedersEmail_StillLeder()
    {
        // D-L13: the flag follows the login id, not the address.
        var w = await CreateWorkerAsync(isLeader: true);
        var loginId = await LoginIdOfAsync(w);

        var result = await UpdateAsync(w, Admin, email: NewEmail("example.org"));

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(await IsLederAsync(loginId), Is.True);
    }

    [Test]
    public async Task Admin_TurnsWebAndArchiveOff_OnAManagedLeder_AlsoRevokes()
    {
        var w = await CreateWorkerAsync(isLeader: true);
        var loginId = await LoginIdOfAsync(w);

        var result = await UpdateAsync(w, Admin, web: false, archive: false);

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(await IsLederAsync(loginId), Is.False, "no managed Leder without login access");
        Assert.That(await GroupNamesOfAsync(loginId), Is.EqualTo(new[] { "none" }));
    }

    [Test]
    public async Task NoCaller_KeepsTodaysBehaviour_NoFlagWritten()
    {
        var w = await CreateWorkerAsync();
        var loginId = await LoginIdOfAsync(w);

        var result = await UpdateAsync(w, caller: null, isLeader: true);

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(await IsLederAsync(loginId), Is.False);
    }
}
