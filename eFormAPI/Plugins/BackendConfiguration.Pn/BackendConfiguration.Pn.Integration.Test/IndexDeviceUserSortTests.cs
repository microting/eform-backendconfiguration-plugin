using BackendConfiguration.Pn.Infrastructure.Helpers;
using BackendConfiguration.Pn.Infrastructure.Models;
using BackendConfiguration.Pn.Infrastructure.Models.AssignmentWorker;
using BackendConfiguration.Pn.Services.BackendConfigurationAssignmentWorkerService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;
using eFormCore;
using Microting.eFormApi.BasePn.Abstractions;
using NSubstitute;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// #1442 - the Angular worker pickers ask <c>IndexDeviceUser</c> to sort by "Name",
/// which <see cref="DeviceUserModel"/> does not have (its name is <c>SiteName</c>).
/// The sort threw, was swallowed and the list came back unsorted. A sort field the
/// model does not have must fall back to SiteName; a real one must still be used.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class IndexDeviceUserSortTests : TestBaseSetup
{
    [SetUp]
    public Task ReserveEformUserId1() => IdentityTestUtils.ReserveEformUserId1Async(BaseDbContext!);

    private async Task<int> CreateWorker(Core core, string firstName, string lastName, string employeeNo)
    {
        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);
        var userService = IdentityTestUtils.CreateRealUserService(BaseDbContext!, userManager);
        var result = await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(new DeviceUserModel
        {
            CustomerNo = 0,
            LanguageCode = "da",
            UserFirstName = firstName,
            UserLastName = lastName,
            EmployeeNo = employeeNo,
            WorkerEmail = $"sort-{Guid.NewGuid():N}@example.com"
        }, core, 1, TimePlanningPnDbContext!, BaseDbContext!, userService, userManager);
        Assert.That(result.Success, Is.True, result.Message);
        return result.Model;
    }

    private async Task<List<DeviceUserModel>> Index(Core core, string? sort)
    {
        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);
        var userService = IdentityTestUtils.CreateRealUserService(BaseDbContext!, userManager);
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));

        var service = new BackendConfigurationAssignmentWorkerService(
            coreHelper, userManager, userService, BackendConfigurationPnDbContext!,
            new BackendConfigurationLocalizationService(), ItemsPlanningPnDbContext!,
            TimePlanningPnDbContext!, CaseTemplatePnDbContext!, BaseDbContext!,
            TestContextLogger<BackendConfigurationAssignmentWorkerService>.Instance,
            Substitute.For<ICalendarAssignmentReconciliationService>(), LederTestData.Leder(false));

        var result = await service.IndexDeviceUser(new PropertyWorkersFiltrationModel
        {
            Sort = sort, IsSortDsc = false, ShowResigned = false
        });
        Assert.That(result.Success, Is.True, result.Message);
        return result.Model;
    }

    [TestCase("Name", TestName = "Name, sent by the Angular pickers, sorts by SiteName")]
    [TestCase("Foo", TestName = "An unknown sort field falls back to SiteName")]
    [TestCase("", TestName = "An empty sort field falls back to SiteName")]
    [TestCase("SiteName")]
    public async Task IndexDeviceUser_SortsBySiteName(string sort)
    {
        // Arrange - created (so Id-ordered) Charlie, Alpha, Bravo; EmployeeNo in yet
        // another order, so neither the unsorted nor an EmployeeNo order passes.
        var core = await GetCore();
        var suffix = Guid.NewGuid().ToString("N");
        var charlie = await CreateWorker(core, $"Charlie{suffix}", "Sort", "E1");
        var alpha = await CreateWorker(core, $"Alpha{suffix}", "Sort", "E3");
        var bravo = await CreateWorker(core, $"Bravo{suffix}", "Sort", "E2");
        var ours = new[] { charlie, alpha, bravo };

        // Act
        var workers = await Index(core, sort);

        // Assert
        var order = workers.Where(x => ours.Contains(x.SiteId)).Select(x => x.SiteId).ToList();
        Assert.That(order, Is.EqualTo(new[] { alpha, bravo, charlie }));
    }

    [Test]
    public async Task IndexDeviceUser_SortsByEmployeeNo()
    {
        // Arrange
        var core = await GetCore();
        var suffix = Guid.NewGuid().ToString("N");
        var charlie = await CreateWorker(core, $"Charlie{suffix}", "Sort", $"E1{suffix}");
        var alpha = await CreateWorker(core, $"Alpha{suffix}", "Sort", $"E3{suffix}");
        var bravo = await CreateWorker(core, $"Bravo{suffix}", "Sort", $"E2{suffix}");
        var ours = new[] { charlie, alpha, bravo };

        // Act
        var workers = await Index(core, "EmployeeNo");

        // Assert
        var order = workers.Where(x => ours.Contains(x.SiteId)).Select(x => x.SiteId).ToList();
        Assert.That(order, Is.EqualTo(new[] { charlie, bravo, alpha }));
    }
}
