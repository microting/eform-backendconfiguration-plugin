#nullable enable
using BackendConfiguration.Pn.Infrastructure.Helpers;
using BackendConfiguration.Pn.Infrastructure.Models;
using BackendConfiguration.Pn.Infrastructure.Models.AssignmentWorker;
using BackendConfiguration.Pn.Services.Leder;
using Microsoft.EntityFrameworkCore;
using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>Contract "Medarbejdere": the list's Leder read-back (each row's own site, one query per list) and the caller flags.</summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class LederMedarbejdereListTests : LederWorkerTestBase
{
    [TestCase(true, true)]
    [TestCase(false, true)]
    [TestCase(false, false)]
    public async Task CallerFlags_OnEveryRow(bool isAdmin, bool isLeder)
    {
        await CreateWorkerAsync();

        var rows = await ListAsync(isAdmin, isLeder);

        Assert.That(rows, Is.Not.Empty);
        Assert.That(rows.Select(r => r.CanSetLeder).Distinct(), Is.EqualTo(new[] { isAdmin }));
        Assert.That(rows.Select(r => r.CanEditWorkerTags).Distinct(), Is.EqualTo(new[] { isAdmin || isLeder }));
    }

    [Test]
    public async Task IsLeader_IsTheFlagOfEachRowsOwnSite()
    {
        var granted = await CreateWorkerAsync(isLeader: true);
        var revoked = await CreateWorkerAsync(isLeader: true);
        Assert.That((await UpdateAsync(revoked, Admin, isLeader: false)).Success, Is.True);
        var never = await CreateWorkerAsync();
        var noAccess = await CreateWorkerAsync(Admin, web: false, archive: false, isLeader: true);

        var rows = await ListAsync();

        Assert.That(Row(rows, granted).IsLeader, Is.True);
        Assert.That(Row(rows, revoked).IsLeader, Is.False, "a removed row reads false");
        Assert.That(Row(rows, never).IsLeader, Is.False);
        Assert.That(Row(rows, noAccess).IsLeader, Is.True, "D-L16: a worker without access can be a Leder");
    }

    private async Task<string> StoredWorkerAddressAsync(Worker w)
    {
        await using var sdkDbContext = Core.DbContextHelper.GetDbContext();
        return await sdkDbContext.SiteWorkers.AsNoTracking()
            .Where(sw => sw.SiteId == w.SiteId)
            .OrderBy(sw => sw.Id)
            .Select(sw => sw.Worker.Email)
            .FirstAsync();
    }

    [Test]
    public async Task EmptyAddress_IsNotLookedUp_ButShowsTheSitesFlag()
    {
        // Create stores an empty address as it is (only UpdateDeviceUser generates "...@microting.invalid").
        var noAddress = await CreateWorkerAsync(web: false, archive: false, email: "");
        Assert.That(await StoredWorkerAddressAsync(noAddress), Is.Empty, "precondition: the stored SDK worker address");

        var before = Row(await ListAsync(), noAddress);
        Assert.That(before.WorkerEmail, Is.Empty);
        Assert.That((before.IsLeader, before.LoginIsManaged, before.IsAdminLogin), Is.EqualTo(((bool?)false, false, false)));

        await LederAssignmentStore.GrantSiteAsync(BackendConfigurationPnDbContext!, noAddress.SiteId, ActorId);
        var after = Row(await ListAsync(), noAddress);
        Assert.That((after.IsLeader, after.LoginIsManaged, after.IsAdminLogin), Is.EqualTo(((bool?)true, false, false)));
    }

    [Test]
    public async Task InvalidPlaceholderAddress_IsNotLookedUp_EvenWithALoginAtIt()
    {
        var w = await CreateWorkerAsync(web: false, archive: false, email: "");
        var placeholder = $"user_{Guid.NewGuid():N}@microting.invalid";
        await using (var sdkDbContext = Core.DbContextHelper.GetDbContext())
        {
            var worker = await sdkDbContext.SiteWorkers.Where(sw => sw.SiteId == w.SiteId).OrderBy(sw => sw.Id)
                .Select(sw => sw.Worker).FirstAsync();
            worker.Email = placeholder;
            await sdkDbContext.SaveChangesAsync();
        }

        // A login at that very address: the list must still not resolve it.
        var login = new EformUser
        {
            Email = placeholder, UserName = placeholder, FirstName = "Jane", LastName = "Doe", Locale = "da",
            EmailConfirmed = true, IsActive = true, TimeZone = "Europe/Copenhagen", Formats = "de-DE"
        };
        Assert.That((await UserManager.CreateAsync(login)).Succeeded, Is.True);
        await LederAssignmentStore.GrantSiteAsync(BackendConfigurationPnDbContext!, w.SiteId, ActorId);
        Assert.That(await StoredWorkerAddressAsync(w), Is.EqualTo(placeholder), "precondition: the stored SDK worker address");

        var row = Row(await ListAsync(), w);

        Assert.That(row.WorkerEmail, Is.EqualTo(placeholder));
        Assert.That((row.IsLeader, row.LoginIsManaged, row.IsAdminLogin), Is.EqualTo(((bool?)true, false, false)),
            "the site's flag shows; the placeholder login is never resolved");
    }

    [Test]
    public async Task ListedAddress_WithoutALogin_StillShowsTheSitesFlag()
    {
        var w = await CreateWorkerAsync(isLeader: true);
        // Move the worker's login to another address: the worker's own (valid, listed) address now resolves to no login.
        var login = await UserManager.FindByIdAsync((await LoginOfAsync(w)).Id.ToString());
        var elsewhere = NewEmail();
        Assert.That((await UserManager.SetUserNameAsync(login!, elsewhere)).Succeeded, Is.True);
        Assert.That((await UserManager.SetEmailAsync(login!, elsewhere)).Succeeded, Is.True);

        var row = Row(await ListAsync(), w);

        Assert.That(row.WorkerEmail, Is.EqualTo(w.Email));
        Assert.That((row.IsLeader, row.LoginIsManaged, row.IsAdminLogin), Is.EqualTo(((bool?)true, false, false)),
            "a listed address without a login: no login fields, but the site's flag");
    }

    [Test]
    public async Task RemovedForeignMembership_CountsAgainstManaged_NotForWebAccess()
    {
        var w = await CreateWorkerAsync(web: true);
        await LederTestData.AddMemberAsync(BaseDbContext, (await LoginOfAsync(w)).Id,
            await LederTestData.NewGroupAsync(BaseDbContext, "Bogholderi"), removed: true);

        var row = Row(await ListAsync(), w);

        // Same as IsPluginManagedLoginAsync, which reads every membership regardless of WorkflowState.
        Assert.That(row.LoginIsManaged, Is.False);
        Assert.That(row.WebAccessEnabled, Is.True);
    }

    [Test]
    public async Task RemovedEformAdminsMembership_ListsAsNotAdminLogin()
    {
        // IsAdminLogin follows IsAdminAsync (live memberships only); LoginIsManaged still counts the removed membership,
        // as IsPluginManagedLoginAsync does.
        var w = await CreateWorkerAsync(web: true);
        await LederTestData.AddMemberAsync(BaseDbContext, (await LoginOfAsync(w)).Id,
            await LederTestData.GroupByNameAsync(BaseDbContext,
                BackendConfigurationAssignmentWorkerServiceHelper.EformAdminsSecurityGroupName), removed: true);

        var row = Row(await ListAsync(), w);

        Assert.That(row.IsAdminLogin, Is.False);
        Assert.That(row.LoginIsManaged, Is.False, "a removed foreign membership counts against managed");
        Assert.That((row.WebAccessEnabled, row.ArchiveEnabled, row.IsLeader), Is.EqualTo((true, false, (bool?)false)));
    }

    [Test]
    public async Task LoginIsManaged_AndIsAdminLogin_PerKindOfLogin()
    {
        var managed = await CreateWorkerAsync(web: true);
        var ownerGroup = await CreateWorkerAsync(web: true);
        await LederTestData.AddMemberAsync(BaseDbContext, (await LoginOfAsync(ownerGroup)).Id,
            await LederTestData.NewGroupAsync(BaseDbContext, "Bogholderi"));
        var adminRole = await CreateWorkerAsync(web: true);
        var adminLogin = await UserManager.FindByIdAsync((await LoginOfAsync(adminRole)).Id.ToString());
        Assert.That((await UserManager.AddToRoleAsync(adminLogin!, EformRole.Admin)).Succeeded, Is.True);
        var eformAdmins = await CreateWorkerAsync(web: true);
        await LederTestData.AddMemberAsync(BaseDbContext, (await LoginOfAsync(eformAdmins)).Id,
            await LederTestData.GroupByNameAsync(BaseDbContext,
                BackendConfigurationAssignmentWorkerServiceHelper.EformAdminsSecurityGroupName));

        var rows = await ListAsync();

        Assert.That((Row(rows, managed).LoginIsManaged, Row(rows, managed).IsAdminLogin), Is.EqualTo((true, false)));
        Assert.That((Row(rows, ownerGroup).LoginIsManaged, Row(rows, ownerGroup).IsAdminLogin), Is.EqualTo((false, false)));
        Assert.That((Row(rows, adminRole).LoginIsManaged, Row(rows, adminRole).IsAdminLogin), Is.EqualTo((false, true)));
        Assert.That((Row(rows, eformAdmins).LoginIsManaged, Row(rows, eformAdmins).IsAdminLogin), Is.EqualTo((false, true)));
        Assert.That(Row(rows, managed).WebAccessEnabled, Is.True, "the Web/Arkiv read-back is unchanged");
    }

    [Test]
    public async Task UpdateDeviceUser_ThroughTheService_PassesTheCaller()
    {
        var w = await CreateWorkerAsync();
        var rows = await ListAsync();
        var row = Row(rows, w);
        row.IsLeader = true;

        var byNonAdmin = await Service(ServiceCaller(ActorId, isAdmin: false), LederTestData.Leder(false)).UpdateDeviceUser(row);
        Assert.That(byNonAdmin.Success, Is.True, byNonAdmin.Message);
        Assert.That(await IsLederAsync(w), Is.False, "a non-admin's isLeader is ignored");

        var byAdmin = await AdminService().UpdateDeviceUser(row);
        Assert.That(byAdmin.Success, Is.True, byAdmin.Message);
        Assert.That(await IsLederAsync(w), Is.True);
    }

    [Test]
    public async Task ListByNonAdmin_DoesNotRenameALederLogin_FoundOnlyByEmail()
    {
        // The read-back is read-only: IUserService.GetByUsernameAsync would rename and save a login matched by e-mail only.
        var w = await CreateWorkerAsync(isLeader: true);
        var login = await UserManager.FindByIdAsync((await LoginOfAsync(w)).Id.ToString());
        var otherUserName = NewEmail();
        Assert.That((await UserManager.SetUserNameAsync(login!, otherUserName)).Succeeded, Is.True);
        Assert.That((await LoginOfAsync(w)).UserName, Is.EqualTo(otherUserName), "precondition: only the e-mail matches");

        var row = Row(await ListAsync(callerIsAdmin: false, callerIsLeder: false), w);

        Assert.That((await LoginOfAsync(w)).UserName, Is.EqualTo(otherUserName), "a list load never renames a login");
        Assert.That(row.IsLeader, Is.True, "the site's flag shows");
    }
}
