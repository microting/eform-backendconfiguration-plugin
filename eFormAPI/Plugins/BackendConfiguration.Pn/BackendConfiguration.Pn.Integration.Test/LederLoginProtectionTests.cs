#nullable enable
using BackendConfiguration.Pn.Services.Leder;
using Microsoft.EntityFrameworkCore;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>H2 re-stated (amendment §4): a non-admin cannot change a Leder worker's e-mail or its login's username.</summary>
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
        Assert.That((after.Email, after.UserName), Is.EqualTo((w.Email, w.Email)));
        Assert.That(await MicrotingDbContext!.Workers.AsNoTracking().AnyAsync(x => x.Email == w.Email), Is.True,
            "the SDK worker keeps its address too");
        Assert.That(await IsLederAsync(w), Is.True);
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
    public async Task AddresslessLeder_NonAdminRename_IsSaved()
    {
        // Blank and the generated user_…@microting.invalid placeholder are both unlisted: no login changes hands.
        var w = await CreateWorkerAsync(Admin, isLeader: true, email: "");

        var result = await UpdateAsync(w, NonAdmin, email: "", lastName: NewLastName("Roe"));

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(await IsLederAsync(w), Is.True);
    }

    [Test]
    public async Task NonAdmin_ChangesTheEmailOfANonLeder_IsAllowed()
    {
        var w = await CreateWorkerAsync();
        var newEmail = NewEmail("example.org");

        var result = await UpdateAsync(w, NonAdmin, email: newEmail);

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(await BaseDbContext.Users.AsNoTracking().AnyAsync(u => u.Email == newEmail), Is.True);
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
        Assert.That(await IsLederAsync(w), Is.True);
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
        Assert.That((await UserManager.UpdateAsync(tracked)).Succeeded, Is.True, "precondition");

        var refused = await UpdateAsync(w, NonAdmin, lastName: NewLastName("Roe"));

        Assert.That(refused.Message, Is.EqualTo(LederMessages.LederLoginChangeRequiresAdmin));
        Assert.That((await LoginByIdAsync(loginId)).UserName, Is.EqualTo(legacyUserName));

        var byAdmin = await UpdateAsync(w, Admin);
        Assert.That(byAdmin.Success, Is.True, byAdmin.Message);
        Assert.That((await LoginByIdAsync(loginId)).UserName, Is.EqualTo(w.Email));

        var afterwards = await UpdateAsync(w, NonAdmin, lastName: NewLastName("Roe"));
        Assert.That(afterwards.Success, Is.True, afterwards.Message);
        Assert.That(await IsLederAsync(w), Is.True);
    }

    [Test]
    public async Task NonAdmin_TurnsWebAndArchiveOffOnALeder_IsAllowed_FlagKept()
    {
        // The "loses access" refusal of H2 is gone (amendment §4): the flag stays and is inert while there is no access.
        var w = await CreateWorkerAsync(isLeader: true);
        var loginId = await LoginIdOfAsync(w);

        var result = await UpdateAsync(w, NonAdmin, web: false, archive: false);

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(await GroupNamesOfAsync(loginId), Is.EqualTo(new[] { "none" }));
        Assert.That(await IsLederAsync(w), Is.True);
    }

    [Test]
    public async Task NonAdmin_SwitchesWebToArchive_OnALeder_IsAllowed_LederKept()
    {
        var w = await CreateWorkerAsync(isLeader: true);
        var loginId = await LoginIdOfAsync(w);

        var result = await UpdateAsync(w, NonAdmin, web: false, archive: true);

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(await GroupNamesOfAsync(loginId), Is.EqualTo(new[] { "Kun arkiv" }));
        Assert.That(await IsLederAsync(w), Is.True);
    }

    [Test]
    public async Task NonAdmin_RenamesALeder_IsAllowed()
    {
        var w = await CreateWorkerAsync(isLeader: true);

        var result = await UpdateAsync(w, NonAdmin, lastName: NewLastName("Roe"));

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(await IsLederAsync(w), Is.True);
    }

    [Test]
    public async Task CreateDeviceUser_ByANonAdmin_LinkingToAnExistingLogin_NeedsNoLederRule_AndTheNewSiteHasNoFlag()
    {
        // Linking a new worker to an existing login no longer needs a rule: the new site has no flag (amendment §4).
        var login = await LederTestData.LoginAsync(BaseDbContext, UserManager, "Kun arkiv");
        var lastName = NewLastName();

        var result = await TryCreateAsync(NonAdmin, lastName, login.Email!, web: false, archive: true);

        Assert.That(result.Success, Is.True, result.Message);
        var siteId = await MicrotingDbContext!.Sites.AsNoTracking().Where(x => x.Name == $"Jane {lastName}")
            .Select(x => x.Id).SingleAsync();
        Assert.That(await LederAssignmentStore.IsSiteLederAsync(BackendConfigurationPnDbContext!, siteId), Is.False);
    }
}
