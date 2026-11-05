#nullable enable
using BackendConfiguration.Pn.Services.Leder;
using Microsoft.EntityFrameworkCore;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>H2: a non-admin with device_users_update cannot take over, unlink or orphan a Leder's login.</summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class LederLoginProtectionTests : LederWorkerTestBase
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task NonAdmin_ChangesALedersEmail_IsRefused_LoginAndWorkerUnchanged(bool callerIsLeder)
    {
        var w = await CreateWorkerAsync(isLeader: true);
        var loginId = await LoginIdOfAsync(w);

        var result = await UpdateAsync(w, callerIsLeder ? LederNonAdmin : NonAdmin, email: NewEmail("example.org"));

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo(LederMessages.LederLoginChangeRequiresAdmin));
        var after = await LoginByIdAsync(loginId);
        Assert.That(after.Email, Is.EqualTo(w.Email));
        Assert.That(after.UserName, Is.EqualTo(w.Email));
        Assert.That(await MicrotingDbContext!.Workers.AsNoTracking().AnyAsync(x => x.Email == w.Email), Is.True,
            "the SDK worker keeps its address too");
        Assert.That(await IsLederAsync(loginId), Is.True);
    }

    [Test]
    public async Task NonAdmin_ClearsALedersEmail_IsRefused()
    {
        // An empty address becomes user_…@microting.invalid: that is an address change too.
        var w = await CreateWorkerAsync(isLeader: true);

        Assert.That((await UpdateAsync(w, NonAdmin, email: "")).Message,
            Is.EqualTo(LederMessages.LederLoginChangeRequiresAdmin));
        Assert.That((await LoginOfAsync(w)).Email, Is.EqualTo(w.Email));
    }

    [Test]
    public async Task NonAdmin_SavesALeder_WhoseStoredAddressNeededTheSanitizerFallback_Unchanged_IsAllowed()
    {
        // The stored worker address carries an invisible mark the login's does not; the visible address is unchanged.
        var w = await CreateWorkerAsync(isLeader: true);
        var loginId = await LoginIdOfAsync(w);
        var worker = await MicrotingDbContext!.Workers.SingleAsync(x => x.Email == w.Email);
        worker.Email = $"‎{w.Email}";
        await worker.Update(MicrotingDbContext);

        var result = await UpdateAsync(w, NonAdmin, lastName: NewLastName("Roe"));

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(await IsLederAsync(loginId), Is.True);
        Assert.That((await LoginByIdAsync(loginId)).Email, Is.EqualTo(w.Email));
    }

    [Test]
    public async Task LederLoginWithUserNameOtherThanEmail_NonAdminRefused_UntilAnAdminSaves()
    {
        // A legacy login whose UserName differs from its Email: the save rewrites the UserName, a change of a Leder's
        // login that only an admin may make (H2). An admin's save aligns it; non-admin saves pass after that.
        var w = await CreateWorkerAsync(isLeader: true);
        var loginId = await LoginIdOfAsync(w);
        var legacyUserName = NewEmail();
        var tracked = await UserManager.FindByIdAsync(loginId.ToString());
        tracked!.UserName = legacyUserName;
        var renamed = await UserManager.UpdateAsync(tracked);
        Assert.That(renamed.Succeeded, Is.True, "precondition");

        var refused = await UpdateAsync(w, NonAdmin, lastName: NewLastName("Roe"));

        Assert.That(refused.Message, Is.EqualTo(LederMessages.LederLoginChangeRequiresAdmin));
        Assert.That((await LoginByIdAsync(loginId)).UserName, Is.EqualTo(legacyUserName));

        var byAdmin = await UpdateAsync(w, Admin);
        Assert.That(byAdmin.Success, Is.True, byAdmin.Message);
        Assert.That((await LoginByIdAsync(loginId)).UserName, Is.EqualTo(w.Email));

        var afterwards = await UpdateAsync(w, NonAdmin, lastName: NewLastName("Roe"));
        Assert.That(afterwards.Success, Is.True, afterwards.Message);
        Assert.That(await IsLederAsync(loginId), Is.True);
    }

    [Test]
    public async Task NonAdmin_TurnsWebAndArchiveOffOnALeder_IsRefused_FlagAndGroupsKept()
    {
        var w = await CreateWorkerAsync(isLeader: true);
        var loginId = await LoginIdOfAsync(w);

        var result = await UpdateAsync(w, NonAdmin, web: false, archive: false);

        Assert.That(result.Message, Is.EqualTo(LederMessages.LederLoginChangeRequiresAdmin));
        Assert.That(await GroupNamesOfAsync(loginId), Is.EqualTo(new[] { "eForm users" }));
        Assert.That(await IsLederAsync(loginId), Is.True);
    }

    [Test]
    public async Task NonAdmin_SwitchesWebToArchive_OnALeder_IsAllowed_LederKept()
    {
        var w = await CreateWorkerAsync(isLeader: true);
        var loginId = await LoginIdOfAsync(w);

        var result = await UpdateAsync(w, NonAdmin, web: false, archive: true);

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(await GroupNamesOfAsync(loginId), Is.EqualTo(new[] { "Kun arkiv" }));
        Assert.That(await IsLederAsync(loginId), Is.True);
    }

    [Test]
    public async Task NonAdmin_RenamesALeder_IsAllowed()
    {
        var w = await CreateWorkerAsync(isLeader: true);

        var result = await UpdateAsync(w, NonAdmin, lastName: NewLastName("Roe"));

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(await IsLederAsync(await LoginIdOfAsync(w)), Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CreateDeviceUser_LinkingToALedersLogin_OnlyAnAdmin_AndTheFlagStays(bool byAdmin)
    {
        // A Leder login with no live worker, in a linkable group ("Kun arkiv"), e.g. after a core device-user delete.
        var login = await LederTestData.LoginAsync(BaseDbContext, UserManager, "Kun arkiv");
        await LederAssignmentStore.GrantAsync(BackendConfigurationPnDbContext!, login.Id, ActorId);
        var lastName = NewLastName();

        var result = await TryCreateAsync(byAdmin ? Admin : NonAdmin, lastName, login.Email!, web: false, archive: true);

        var siteCreated = await SiteExistsAsync(lastName);
        if (byAdmin)
        {
            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(siteCreated, Is.True);
        }
        else
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Is.EqualTo(LederMessages.LederLoginChangeRequiresAdmin));
            Assert.That(siteCreated, Is.False, "refused before core.SiteCreate");
        }

        Assert.That(await IsLederAsync(login.Id), Is.True);
        Assert.That(await BaseDbContext.Users.CountAsync(u => u.Email == login.Email), Is.EqualTo(1));
    }

    [Test]
    public async Task CreateDeviceUser_KunArkivLoginWithoutLeder_IsStillLinkedByANonAdmin()
    {
        var login = await LederTestData.LoginAsync(BaseDbContext, UserManager, "Kun arkiv");

        var result = await TryCreateAsync(NonAdmin, NewLastName(), login.Email!, web: false, archive: true);

        Assert.That(result.Success, Is.True, result.Message);
    }
}
