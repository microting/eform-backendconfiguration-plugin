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
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;

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

    private BackendConfigurationAssignmentWorkerService LiveService(
        UserManager<EformUser>? users = null, Microting.EformAngularFrontendBase.Infrastructure.Data.BaseDbContext? baseDb = null)
    {
        var localization = Substitute.For<IBackendConfigurationLocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(ci => ci.Arg<string>());
        return new BackendConfigurationAssignmentWorkerService(LederTestData.CoreHelper(Core), users ?? UserManager,
            ServiceCaller(ActorId, isAdmin: true), BackendConfigurationPnDbContext!, localization,
            ItemsPlanningPnDbContext!, TimePlanningPnDbContext!, CaseTemplatePnDbContext!, baseDb ?? BaseDbContext,
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
        // By SDK site id, known in the handler: no later lookup that could fail and drop it.
        _live.Received(1).RevokeSite(w.SiteId);
        _live.DidNotReceiveWithAnyArgs().AssignmentsChanged(default);
    }

    [Test]
    public async Task AResignationSaved_ThenTheLoginUpdateFails_StillEndsTheConnections()
    {
        // Security review: the worker row is saved resigned, then the login's account update
        // fails and the method reports failure. The revoke follows the saved row, not the result,
        // and is issued only once the resignation is stored.
        var w = await CreateWorkerAsync();
        var angular = new DbContextOptionsBuilder<Microting.EformAngularFrontendBase.Infrastructure.Data.BaseDbContext>()
            .UseMySql(ConnectionStringFor("420_Angular"), ServerVersion.AutoDetect(ConnectionStringFor("420_Angular")))
            .AddInterceptors(new FailAccountUpdates())
            .Options;
        await using var failingBase = new Microting.EformAngularFrontendBase.Infrastructure.Data.BaseDbContext(angular);
        using var failingUsers = IdentityTestUtils.CreateRealUserManager(failingBase);
        bool? resignedWhenRevoked = null;
        _live.When(x => x.RevokeSite(w.SiteId)).Do(_ =>
        {
            using var check = new Microting.eForm.Infrastructure.MicrotingDbContext(
                new DbContextOptionsBuilder<Microting.eForm.Infrastructure.MicrotingDbContext>()
                    .UseMySql(ConnectionStringFor("420_SDK"), ServerVersion.AutoDetect(ConnectionStringFor("420_SDK"))).Options);
            resignedWhenRevoked = check.Workers.AsNoTracking().Single(x => x.Email == w.Email).Resigned;
        });

        var result = await LiveService(failingUsers, failingBase).UpdateDeviceUser(EditOf(w, resigned: true));

        Assert.That(result.Success, Is.False, "the partial-success path: " + result.Message);
        var worker = await MicrotingDbContext!.Workers.AsNoTracking().SingleAsync(x => x.Email == w.Email);
        Assert.That(worker.Resigned, Is.True, "the resignation was saved");
        _live.Received(1).RevokeSite(w.SiteId);
        Assert.That(resignedWhenRevoked, Is.True, "revoked after the resignation was committed, never before");
    }

    /// <summary>Fails every write to the login table (`Users`): the account step after the worker row.</summary>
    private sealed class FailAccountUpdates : Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
    {
        private static void Check(System.Data.Common.DbCommand command)
        {
            var text = command.CommandText;
            // The provider prefixes writes with "SET AUTOCOMMIT = 1;", so match anywhere.
            if (text.Contains("UPDATE `Users`", StringComparison.OrdinalIgnoreCase)
                || text.Contains("INSERT INTO `Users`", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("simulated account update failure");
        }

        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> NonQueryExecutingAsync(
            System.Data.Common.DbCommand command, Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Check(command);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
            System.Data.Common.DbCommand command, Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Check(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    [Test]
    public async Task ARefusedResignation_RevokesNothing()
    {
        var w = await CreateWorkerAsync();
        var property = await PropertyAsync();
        await LinkAsync(property.Id, w.SiteId);
        await ActiveEventNamingAsync(property.Id, w.SiteId);

        var result = await LiveService().UpdateDeviceUser(EditOf(w, resigned: true));

        Assert.That(result.Success, Is.False);
        _live.DidNotReceiveWithAnyArgs().RevokeSite(default);
    }



    [Test]
    public async Task EditingAWorker_TellsItsSiteToResync()
    {
        var w = await CreateWorkerAsync();

        var result = await LiveService().UpdateDeviceUser(EditOf(w, resigned: false));

        Assert.That(result.Success, Is.True, result.Message);
        _live.Received(1).AssignmentsChanged(w.SiteId);
        _live.DidNotReceiveWithAnyArgs().RevokeSite(default);
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
