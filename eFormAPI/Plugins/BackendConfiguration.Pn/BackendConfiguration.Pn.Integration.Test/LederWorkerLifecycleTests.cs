#nullable enable
using BackendConfiguration.Pn.Infrastructure.Helpers;
using BackendConfiguration.Pn.Infrastructure.Models.AssignmentWorker;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using NSubstitute;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>Amendment §4: a deleted worker's site loses the flag; removal from one property and resignation keep it.</summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class LederWorkerLifecycleTests : LederWorkerTestBase
{
    [Test]
    public async Task DeleteWorker_RevokesTheSitesFlag_KeepsTheLoginsGroups()
    {
        var w = await CreateWorkerAsync(isLeader: true);
        var login = await LoginOfAsync(w);
        // Granted by the actor, deleted by a second login: UpdatedByUserId proves the revoke wrote the row.
        var deleterId = (await LederTestData.LoginAsync(BaseDbContext, UserManager)).Id;

        // The caller is the first user (Delete's own rule); ServiceCaller stubs that.
        var result = await Service(ServiceCaller(deleterId, isAdmin: true), LederTestData.Leder(true)).Delete(w.SiteId);

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(await IsLederAsync(w), Is.False);
        Assert.That(await GroupNamesOfAsync(login.Id), Is.EqualTo(new[] { "eForm users" }));
        var row = await BackendConfigurationPnDbContext!.LederAssignments.AsNoTracking().SingleAsync(x => x.SiteId == w.SiteId);
        Assert.That((row.WorkflowState, row.UpdatedByUserId), Is.EqualTo((Constants.WorkflowStates.Removed, deleterId)));
    }

    [Test]
    public async Task DeleteWorker_RefusedAsStillAssigned_KeepsTheFlag()
    {
        // The revoke runs before the deleting steps but after every refusal: a refused delete leaves the flag alone.
        var w = await CreateWorkerAsync(isLeader: true);
        var property = await PropertyAsync();
        await LinkAsync(property.Id, w.SiteId);
        await ActiveEventNamingAsync(property.Id, w.SiteId);

        var result = await AdminService().Delete(w.SiteId);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message,
            Is.EqualTo(BackendConfigurationAssignmentWorkerServiceHelper.WorkerStillAssignedToEventsCannotDeleteKey));
        Assert.That(await IsLederAsync(w), Is.True, "a refused delete keeps the Leder flag");
        Assert.That(await SiteExistsAsync(w.LastName), Is.True);
    }

    [Test]
    public async Task DeleteWorker_RefusedForANonFirstUser_KeepsTheFlag()
    {
        var w = await CreateWorkerAsync(isLeader: true);
        var caller = ServiceCaller(ActorId, isAdmin: true);
        var otherId = (await LederTestData.LoginAsync(BaseDbContext, UserManager)).Id;
        caller.GetFirstUserIdInDb().Returns(Task.FromResult(otherId));

        var result = await Service(caller, LederTestData.Leder(true)).Delete(w.SiteId);

        Assert.That(result.Success, Is.False);
        Assert.That(await IsLederAsync(w), Is.True, "a refused delete keeps the Leder flag");
    }

    [Test]
    public async Task DeleteWorker_WithoutAnAddress_StillRevokesTheFlag()
    {
        // The revoke is by site: a worker that never had a login loses its flag too.
        var w = await CreateWorkerAsync(Admin, web: false, archive: false, email: "", isLeader: true);
        Assert.That(await IsLederAsync(w), Is.True, "precondition");

        var result = await AdminService().Delete(w.SiteId);

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(await IsLederAsync(w), Is.False);
    }

    [Test]
    public async Task List_WebFlag_IgnoresARemovedMembership_LikeTheServerDecision()
    {
        var w = await CreateWorkerAsync(web: true);
        var login = await LoginOfAsync(w);
        Assert.That(Row(await ListAsync(), w).WebAccessEnabled, Is.True);

        var membership = await BaseDbContext.SecurityGroupUsers
            .SingleAsync(x => x.EformUserId == login.Id && x.SecurityGroup.Name == "eForm users");
        membership.WorkflowState = Constants.WorkflowStates.Removed;
        await BaseDbContext.SaveChangesAsync();

        var row = Row(await ListAsync(), w);
        Assert.That(row.WebAccessEnabled, Is.False, "a removed membership is no web access");
        Assert.That(row.ArchiveEnabled, Is.False);
    }

    [Test]
    public async Task DeleteWorker_ThatIsNotALeder_WritesNoRow()
    {
        var w = await CreateWorkerAsync();

        var result = await AdminService().Delete(w.SiteId);

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(await BackendConfigurationPnDbContext!.LederAssignments.AnyAsync(x => x.SiteId == w.SiteId), Is.False);
    }

    [Test]
    public async Task RemovedFromOneOfTwoProperties_KeepsTheFlag()
    {
        var w = await CreateWorkerAsync(isLeader: true);
        var p1 = await PropertyAsync();
        var p2 = await PropertyAsync();
        await LinkAsync(p1.Id, w.SiteId);
        var keep = await LinkAsync(p2.Id, w.SiteId);

        var result = await BackendConfigurationAssignmentWorkerServiceHelper.Update(new PropertyAssignWorkersModel
            {
                SiteId = w.SiteId,
                Assignments =
                [
                    new PropertyAssignmentWorkerModel { PropertyId = p1.Id, IsChecked = false },
                    new PropertyAssignmentWorkerModel { PropertyId = p2.Id, IsChecked = true }
                ]
            }, Core, ServiceCaller(ActorId, isAdmin: true), BackendConfigurationPnDbContext!, CaseTemplatePnDbContext!, null,
            ItemsPlanningPnDbContext!);

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(await BackendConfigurationPnDbContext!.PropertyWorkers.AsNoTracking()
            .AnyAsync(x => x.Id == keep.Id && x.WorkflowState != Constants.WorkflowStates.Removed), Is.True);
        Assert.That(await IsLederAsync(w), Is.True, "removing one property is not a delete");
    }

    [Test]
    public async Task ResignedLeder_KeepsTheFlag_IsNoLederWhileResigned_AndIsAgainOnRehire()
    {
        var w = await CreateWorkerAsync(isLeader: true);
        var login = await LoginOfAsync(w);
        var asLeder = LederTestData.Caller(login.Id);

        var resign = await UpdateAsync(w, Admin, resigned: true);
        Assert.That(resign.Success, Is.True, resign.Message);
        Assert.That(await IsLederAsync(w), Is.True, "kept: D7 disables the account instead");
        Assert.That((await LoginByIdAsync(login.Id)).IsActive, Is.False);
        Assert.That(await RealLeder(asLeder).IsLederOrAdminAsync(), Is.False, "D-L18: an inactive login is no Leder");
        Assert.That(Row(await ListAsync(showResigned: true), w).IsLeader, Is.True, "the list shows the stored flag");

        var rehire = await UpdateAsync(w, Admin, resigned: false);
        Assert.That(rehire.Success, Is.True, rehire.Message);
        Assert.That(await RealLeder(asLeder).IsLederOrAdminAsync(), Is.True, "same site, Leder again");
    }
}
