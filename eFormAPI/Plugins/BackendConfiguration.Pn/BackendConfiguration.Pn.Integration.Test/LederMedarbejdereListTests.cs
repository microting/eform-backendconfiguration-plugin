#nullable enable
using BackendConfiguration.Pn.Infrastructure.Helpers;
using BackendConfiguration.Pn.Infrastructure.Models;
using BackendConfiguration.Pn.Infrastructure.Models.AssignmentWorker;
using BackendConfiguration.Pn.Services.Leder;
using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>Contract "Medarbejdere": the list's Leder read-back (the stored flag, by login id) and the caller flags.</summary>
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
    public async Task IsLeader_IsTheStoredFlagOfTheResolvedLogin()
    {
        var granted = await CreateWorkerAsync(isLeader: true);
        var revoked = await CreateWorkerAsync(isLeader: true);
        Assert.That((await UpdateAsync(revoked, Admin, isLeader: false)).Success, Is.True);
        var never = await CreateWorkerAsync();

        var rows = await ListAsync();

        Assert.That(Row(rows, granted).IsLeader, Is.True);
        Assert.That(Row(rows, revoked).IsLeader, Is.False, "a removed row reads false");
        Assert.That(Row(rows, never).IsLeader, Is.False);
    }

    [Test]
    public async Task UnlistedInvalidAddress_IsNotLookedUp_ReadsUnknown()
    {
        // Create replaces an empty address with an "...@microting.invalid" one; the list never looks that up.
        var invalidAddress = await CreateWorkerAsync(web: false, archive: false, email: "");

        var row = Row(await ListAsync(), invalidAddress);

        Assert.That(row.WorkerEmail, Does.Contain("invalid"));
        Assert.That((row.IsLeader, row.LoginIsManaged, row.IsAdminLogin), Is.EqualTo(((bool?)null, false, false)));
    }

    [Test]
    public async Task ListedAddress_WithoutALogin_ReadsUnknown()
    {
        var w = await CreateWorkerAsync(isLeader: true);
        // Move the worker's login to another address: the worker's own (valid, listed) address now resolves to no login.
        var login = await UserManager.FindByIdAsync((await LoginOfAsync(w)).Id.ToString());
        var elsewhere = NewEmail();
        Assert.That((await UserManager.SetUserNameAsync(login!, elsewhere)).Succeeded, Is.True);
        Assert.That((await UserManager.SetEmailAsync(login!, elsewhere)).Succeeded, Is.True);

        var row = Row(await ListAsync(), w);

        Assert.That(row.WorkerEmail, Is.EqualTo(w.Email));
        Assert.That((row.IsLeader, row.LoginIsManaged, row.IsAdminLogin), Is.EqualTo(((bool?)null, false, false)),
            "a listed address without a login: unknown, even though that login holds the flag");
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
        var login = await LoginOfAsync(w);
        var rows = await ListAsync();
        var row = Row(rows, w);
        row.IsLeader = true;

        var byNonAdmin = await Service(ServiceCaller(ActorId, isAdmin: false), LederTestData.Leder(false)).UpdateDeviceUser(row);
        Assert.That(byNonAdmin.Success, Is.True, byNonAdmin.Message);
        Assert.That(await IsLederAsync(login.Id), Is.False, "a non-admin's isLeader is ignored");

        var byAdmin = await AdminService().UpdateDeviceUser(row);
        Assert.That(byAdmin.Success, Is.True, byAdmin.Message);
        Assert.That(await IsLederAsync(login.Id), Is.True);
    }
}
