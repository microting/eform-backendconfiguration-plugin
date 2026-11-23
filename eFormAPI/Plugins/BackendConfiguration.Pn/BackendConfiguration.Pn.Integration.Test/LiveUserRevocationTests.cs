#nullable enable
using BackendConfiguration.Pn.Infrastructure.Models;
using BackendConfiguration.Pn.Infrastructure.Models.AssignmentWorker;
using BackendConfiguration.Pn.Services.Leder;
using BackendConfiguration.Pn.Services.BackendConfigurationAssignmentWorkerService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;
using BackendConfiguration.Pn.Services.LiveChanges;
using NSubstitute;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// Package 11b: the one place this plugin deactivates a login (resigning a
/// worker sets <c>EformUser.IsActive = false</c>) also ends that login's
/// connections by user id, so a connection of the same login that has no
/// site (a web calendar connection) ends too. The host's own user admin is
/// outside the plugin (docs/live-updates-hub-contract.md).
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class LiveUserRevocationTests : LederWorkerTestBase
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

    private static DeviceUserModel EditOf(Worker w, bool resigned) => new()
    {
        LanguageCode = "da", UserFirstName = "Jane", UserLastName = w.LastName, WorkerEmail = w.Email,
        WebAccessEnabled = true, Tags = [], PinCode = "****", EmployeeNo = "", SiteMicrotingUid = w.SiteUid,
        Resigned = resigned, ResignedAtDate = resigned ? DateTime.UtcNow.Date : DateTime.MinValue
    };

    [Test]
    public async Task ResigningAWorker_AlsoEndsItsLoginsConnections_ByUserId()
    {
        var w = await CreateWorkerAsync();
        var loginId = await LoginIdOfAsync(w);

        var result = await LiveService().UpdateDeviceUser(EditOf(w, resigned: true));

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That((await LoginByIdAsync(loginId)).IsActive, Is.False);
        _live.Received(1).RevokeUser(loginId);
        _live.Received(1).RevokeSite(w.SiteId);
    }

    [Test]
    public async Task EditingAWorker_RevokesNoUser()
    {
        var w = await CreateWorkerAsync();

        var result = await LiveService().UpdateDeviceUser(EditOf(w, resigned: false));

        Assert.That(result.Success, Is.True, result.Message);
        _live.DidNotReceiveWithAnyArgs().RevokeUser(default);
    }
}
