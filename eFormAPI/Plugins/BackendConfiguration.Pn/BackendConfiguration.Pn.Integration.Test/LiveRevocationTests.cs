#nullable enable
using BackendConfiguration.Pn.Infrastructure.Models;
using BackendConfiguration.Pn.Infrastructure.Models.AssignmentWorker;
using BackendConfiguration.Pn.Services.BackendConfigurationAssignmentWorkerService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;
using BackendConfiguration.Pn.Services.Leder;
using BackendConfiguration.Pn.Services.LiveChanges;
using Microting.eFormApi.BasePn.Abstractions;
using NSubstitute;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// The worker paths the hub contract requires to revoke (docs/live-updates-hub-contract.md,
/// "Before any publisher is added"): deleting a worker and resigning one end its
/// site's connections; other worker edits and tag changes tell its site to resync.
/// (Removing a PropertyWorker row, which unassigning and property deletion do,
/// revokes through the interceptor: see LiveChangeInterceptorTests.)
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class LiveRevocationTests : LederWorkerTestBase
{
    private ILiveChanges _live = null!;

    [SetUp]
    public void SetUpLive() => _live = Substitute.For<ILiveChanges>();

    private BackendConfigurationAssignmentWorkerService LiveService()
    {
        var localization = Substitute.For<IBackendConfigurationLocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(ci => ci.Arg<string>());
        return new BackendConfigurationAssignmentWorkerService(LederTestData.CoreHelper(Core), UserManager,
            ServiceCaller(ActorId, isAdmin: true), BackendConfigurationPnDbContext!, localization,
            ItemsPlanningPnDbContext!, TimePlanningPnDbContext!, CaseTemplatePnDbContext!, BaseDbContext,
            TestContextLogger<BackendConfigurationAssignmentWorkerService>.Instance,
            Substitute.For<ICalendarAssignmentReconciliationService>(), LederTestData.Leder(true),
            liveChanges: _live);
    }

    private DeviceUserModel EditOf(Worker w, bool resigned) => new()
    {
        LanguageCode = "da", UserFirstName = "Jane", UserLastName = w.LastName, WorkerEmail = w.Email,
        WebAccessEnabled = true, Tags = [], PinCode = "****", EmployeeNo = "", SiteMicrotingUid = w.SiteUid,
        Resigned = resigned, ResignedAtDate = resigned ? DateTime.UtcNow.Date : DateTime.MinValue
    };

    [Test]
    public async Task DeletingAWorker_EndsItsSitesConnections()
    {
        var w = await CreateWorkerAsync();

        var result = await LiveService().Delete(w.SiteId);

        Assert.That(result.Success, Is.True, result.Message);
        _live.Received(1).RevokeSite(w.SiteId);
    }

    [Test]
    public async Task ARefusedDelete_RevokesNothing()
    {
        var w = await CreateWorkerAsync();
        var property = await PropertyAsync();
        await LinkAsync(property.Id, w.SiteId);
        await ActiveEventNamingAsync(property.Id, w.SiteId);

        var result = await LiveService().Delete(w.SiteId);

        Assert.That(result.Success, Is.False);
        _live.DidNotReceiveWithAnyArgs().RevokeSite(default);
    }

    [Test]
    public async Task ResigningAWorker_EndsItsSitesConnections()
    {
        var w = await CreateWorkerAsync();

        var result = await LiveService().UpdateDeviceUser(EditOf(w, resigned: true));

        Assert.That(result.Success, Is.True, result.Message);
        _live.Received(1).RevokeSiteForSiteUid(w.SiteUid);
        _live.DidNotReceiveWithAnyArgs().AssignmentsChangedForSiteUid(default);
    }

    [Test]
    public async Task EditingAWorker_TellsItsSiteToResync()
    {
        var w = await CreateWorkerAsync();

        var result = await LiveService().UpdateDeviceUser(EditOf(w, resigned: false));

        Assert.That(result.Success, Is.True, result.Message);
        _live.Received(1).AssignmentsChangedForSiteUid(w.SiteUid);
        _live.DidNotReceiveWithAnyArgs().RevokeSiteForSiteUid(default);
    }

    [Test]
    public async Task ChangingWorkerTagsInBulk_TellsEachSite()
    {
        var drift = await SdkTagAsync("Drift");
        var a = await CreateWorkerAsync();
        var b = await CreateWorkerAsync();

        var result = await LiveService().BulkUpdateWorkerTags(new WorkerTagsBulkUpdateModel
            { SiteIds = [a.SiteId, b.SiteId, a.SiteId], TagIds = [drift], Mode = WorkerTagsBulkMode.Add });

        Assert.That(result.Success, Is.True, result.Message);
        _live.Received(1).AssignmentsChanged(a.SiteId);
        _live.Received(1).AssignmentsChanged(b.SiteId);
    }
}
