using BackendConfiguration.Pn.Infrastructure.Helpers;
using BackendConfiguration.Pn.Infrastructure.Models;
using BackendConfiguration.Pn.Infrastructure.Models.AssignmentWorker;
using BackendConfiguration.Pn.Infrastructure.Models.Properties;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using eFormCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformAngularFrontendBase.Infrastructure.Data.Entities.Permissions;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;
using Microting.TimePlanningBase.Infrastructure.Data.Entities;
using Microting.TimePlanningBase.Infrastructure.Helpers;
using NSubstitute;

namespace BackendConfiguration.Pn.Integration.Test;

[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class BackendConfigurationAssignmentWorkerServiceHelperTest : TestBaseSetup
{
    // Opted out of the fixture-scoped schema replay:
    // ~90 whole-table counts and positional indexes (sites[2], properties[0], entityItems[7]).
    protected override bool ResetDatabasePerTest => true;

    // Should test the CreateDeviceUser method and return success
    [Test]
    public async Task BackendConfigurationAssignmentWorkerServiceHelper_CreateDeviceUser_ReturnsSuccess()
    {
        // Arrange
        var core = await GetCore();

        var deviceUserModel = new DeviceUserModel
        {
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = false,
            UserFirstName = Guid.NewGuid().ToString(),
            UserLastName = Guid.NewGuid().ToString(),
            WorkerEmail = $"{Guid.NewGuid()}@test.com"
        };

        // Act
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);

        var result = await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            TimePlanningPnDbContext!, BaseDbContext!,
        userService,
        userManager);

        // Assert
        var sites = await MicrotingDbContext!.Sites.AsNoTracking().ToListAsync();
        var workers = await MicrotingDbContext.Workers.AsNoTracking().ToListAsync();
        var siteWorkers = await MicrotingDbContext.SiteWorkers.AsNoTracking().ToListAsync();
        var units = await MicrotingDbContext.Units.AsNoTracking().ToListAsync();

        Assert.That(result, Is.Not.Null);
        Assert.That(sites.Count, Is.EqualTo(3));
        Assert.That(workers.Count, Is.EqualTo(3));
        Assert.That(units.Count, Is.EqualTo(3));

        // Assert site
        Assert.That(sites[2].Name, Is.EqualTo(deviceUserModel.UserFirstName + " " + deviceUserModel.UserLastName));

        // Assert worker
        Assert.That(workers[2].FirstName, Is.EqualTo(deviceUserModel.UserFirstName));
        Assert.That(workers[2].LastName, Is.EqualTo(deviceUserModel.UserLastName));

        // Assert siteWorker
        Assert.That(siteWorkers[2].WorkerId, Is.EqualTo(workers[2].Id));
        Assert.That(siteWorkers[2].SiteId, Is.EqualTo(sites[2].Id));

        // Assert unit
        Assert.That(units[2].SiteId, Is.EqualTo(sites[2].Id));
    }


    // Should test the CreateDeviceUser method with TimeRegistrationEnabled set to true and return success
    [Test]
    public async Task BackendConfigurationAssignmentWorkerServiceHelper_CreateDeviceUser_TimeRegistrationEnabled_ReturnsSuccess()
    {
        // Arrange
        var core = await GetCore();

        var deviceUserModel = new DeviceUserModel
        {
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = true,
            UserFirstName = Guid.NewGuid().ToString(),
            UserLastName = Guid.NewGuid().ToString(),
            WorkerEmail = $"{Guid.NewGuid()}@test.com"
        };

        // Act
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);

        // Act
        var result = await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            TimePlanningPnDbContext!, BaseDbContext!,
        userService,
        userManager);

        // Assert
        var sites = await MicrotingDbContext!.Sites.ToListAsync();
        var workers = await MicrotingDbContext.Workers.ToListAsync();
        var siteWorkers = await MicrotingDbContext.SiteWorkers.AsNoTracking().ToListAsync();
        var units = await MicrotingDbContext.Units.ToListAsync();
        var timeregistrationSiteAssignments = await TimePlanningPnDbContext!.AssignedSites.ToListAsync();

        Assert.That(result, Is.Not.Null);
        Assert.That(sites.Count, Is.EqualTo(3));
        Assert.That(workers.Count, Is.EqualTo(3));
        Assert.That(units.Count, Is.EqualTo(3));

        // Assert site
        Assert.That(sites[2].Name, Is.EqualTo(deviceUserModel.UserFirstName + " " + deviceUserModel.UserLastName));

        // Assert worker
        Assert.That(workers[2].FirstName, Is.EqualTo(deviceUserModel.UserFirstName));
        Assert.That(workers[2].LastName, Is.EqualTo(deviceUserModel.UserLastName));

        // Assert siteWorker
        Assert.That(siteWorkers[2].WorkerId, Is.EqualTo(workers[2].Id));
        Assert.That(siteWorkers[2].SiteId, Is.EqualTo(sites[2].Id));

        // Assert unit
        Assert.That(units[2].SiteId, Is.EqualTo(sites[2].Id));

        // Assert timeregistrationSiteAssignments
        Assert.That(timeregistrationSiteAssignments.Count, Is.EqualTo(31));
        Assert.That(timeregistrationSiteAssignments[30].SiteId, Is.EqualTo(sites[2].MicrotingUid));

        // Create hardcodes UseOneMinuteIntervals to true even when the DeviceUserModel leaves it unset,
        // and leaves UseOneMinuteIntervalsFrom NULL so OneMinuteModeTimeline reports one-minute mode for
        // the site's whole history.
        Assert.That(timeregistrationSiteAssignments[30].UseOneMinuteIntervals, Is.True);
        Assert.That(timeregistrationSiteAssignments[30].UseOneMinuteIntervalsFrom, Is.Null);
    }

    // Should test the CreateDeviceUser method with TimeRegistrationEnabled and PayRuleSetId set,
    // verifying PayRuleSetId passes through while a client-sent UseOneMinuteIntervals=false is IGNORED
    [Test]
    public async Task
        BackendConfigurationAssignmentWorkerServiceHelper_CreateDeviceUser_TimeRegistrationEnabled_ClientSentUseOneMinuteIntervalsIgnoredAndPayRuleSetId_ReturnsSuccess()
    {
        // Arrange
        var core = await GetCore();

        // PayRuleSetId is a real FK on AssignedSite, so seed a PayRuleSet row to
        // reference rather than an arbitrary int.
        var payRuleSet = new PayRuleSet { Name = Guid.NewGuid().ToString() };
        await payRuleSet.Create(TimePlanningPnDbContext!);
        var payRuleSetId = payRuleSet.Id;

        var deviceUserModel = new DeviceUserModel
        {
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = true,
            // Deliberately false: the create path must ignore it and still persist true.
            UseOneMinuteIntervals = false,
            PayRuleSetId = payRuleSetId,
            UserFirstName = Guid.NewGuid().ToString(),
            UserLastName = Guid.NewGuid().ToString(),
            WorkerEmail = $"{Guid.NewGuid()}@test.com"
        };

        // Act
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);

        // Act
        var result = await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            TimePlanningPnDbContext!, BaseDbContext!,
        userService,
        userManager);

        // Assert
        var sites = await MicrotingDbContext!.Sites.ToListAsync();
        var timeregistrationSiteAssignments = await TimePlanningPnDbContext!.AssignedSites.ToListAsync();

        Assert.That(result, Is.Not.Null);
        Assert.That(sites.Count, Is.EqualTo(3));

        // Assert timeregistrationSiteAssignments
        Assert.That(timeregistrationSiteAssignments.Count, Is.EqualTo(31));
        Assert.That(timeregistrationSiteAssignments[30].SiteId, Is.EqualTo(sites[2].MicrotingUid));

        // True even though the client explicitly sent false — create hardcodes the flag.
        Assert.That(timeregistrationSiteAssignments[30].UseOneMinuteIntervals, Is.True);
        Assert.That(timeregistrationSiteAssignments[30].PayRuleSetId, Is.EqualTo(payRuleSetId));
    }

    // Should test the UpdateDeviceUser method and return success
    [Test]
    public async Task BackendConfigurationAssignmentWorkerServiceHelper_UpdateDeviceUser_ReturnsSuccess()
    {
        // Arrange
        var core = await GetCore();
        var logger = Substitute.For<ILogger>();

        var propertyCreateModel = new PropertyCreateModel
        {
            Address = Guid.NewGuid().ToString(),
            Chr = Guid.NewGuid().ToString(),
            IndustryCode = Guid.NewGuid().ToString(),
            Cvr = Guid.NewGuid().ToString(),
            IsFarm = true,
            LanguagesIds = [1],
            MainMailAddress = Guid.NewGuid().ToString(),
            Name = Guid.NewGuid().ToString(),
            WorkorderEnable = false
        };

        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1,
            BackendConfigurationPnDbContext!, ItemsPlanningPnDbContext!, 1,1);

        var deviceUserModel = new DeviceUserModel
        {
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = false,
            UserFirstName = Guid.NewGuid().ToString(),
            UserLastName = Guid.NewGuid().ToString(),
            WorkerEmail = $"{Guid.NewGuid()}@test.com"
        };

        // Act
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);

        await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            TimePlanningPnDbContext!, BaseDbContext!,
        userService,
        userManager);

        var currentSite = await MicrotingDbContext!.Sites.OrderByDescending(x => x.Id).FirstAsync();

        var newDeviceUserModel = new DeviceUserModel
        {
            SiteMicrotingUid = (int)currentSite.MicrotingUid!,
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = false,
            UserFirstName = Guid.NewGuid().ToString(),
            UserLastName = Guid.NewGuid().ToString(),
            WorkerEmail = $"{Guid.NewGuid()}@test.com"
        };

        var result = await BackendConfigurationAssignmentWorkerServiceHelper.UpdateDeviceUser(newDeviceUserModel, core, 1,
            userService, userManager,
            BackendConfigurationPnDbContext!,
            TimePlanningPnDbContext!, BaseDbContext!, logger, ItemsPlanningPnDbContext!);

        // Assert
        var sites = await MicrotingDbContext!.Sites.AsNoTracking().ToListAsync();
        var workers = await MicrotingDbContext.Workers.AsNoTracking().ToListAsync();
        var siteWorkers = await MicrotingDbContext.SiteWorkers.AsNoTracking().ToListAsync();
        var units = await MicrotingDbContext.Units.AsNoTracking().ToListAsync();
        var timeregistrationSiteAssignments = await TimePlanningPnDbContext!.AssignedSites.ToListAsync();

        Assert.That(result, Is.Not.Null);
        Assert.That(sites.Count, Is.EqualTo(3));
        Assert.That(workers.Count, Is.EqualTo(3));
        Assert.That(units.Count, Is.EqualTo(3));

        // Assert site
        Assert.That(sites[2].Name, Is.EqualTo(newDeviceUserModel.UserFirstName + " " + newDeviceUserModel.UserLastName));

        // Assert worker
        Assert.That(workers[2].FirstName, Is.EqualTo(newDeviceUserModel.UserFirstName));
        Assert.That(workers[2].LastName, Is.EqualTo(newDeviceUserModel.UserLastName));

        // Assert siteWorker
        Assert.That(siteWorkers[2].WorkerId, Is.EqualTo(workers[2].Id));
        Assert.That(siteWorkers[2].SiteId, Is.EqualTo(sites[2].Id));

        // Assert unit
        Assert.That(units[2].SiteId, Is.EqualTo(sites[2].Id));

        // Assert timeregistrationSiteAssignments
        Assert.That(timeregistrationSiteAssignments.Count, Is.EqualTo(30));
    }

    // Should test the UpdateDeviceUser method with timeRegistration set to true and return success
    [Test]
    public async Task
        BackendConfigurationAssignmentWorkerServiceHelper_UpdateDeviceUser_TimeRegistrationEnabled_ReturnsSuccess()
    {
        // Arrange
        var core = await GetCore();
        var logger = Substitute.For<ILogger>();

        var propertyCreateModel = new PropertyCreateModel
        {
            Address = Guid.NewGuid().ToString(),
            Chr = Guid.NewGuid().ToString(),
            IndustryCode = Guid.NewGuid().ToString(),
            Cvr = Guid.NewGuid().ToString(),
            IsFarm = true,
            LanguagesIds = [1],
            MainMailAddress = Guid.NewGuid().ToString(),
            Name = Guid.NewGuid().ToString(),
            WorkorderEnable = false
        };

        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1,
            BackendConfigurationPnDbContext!, ItemsPlanningPnDbContext!, 1, 1);

        var deviceUserModel = new DeviceUserModel
        {
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = false,
            UserFirstName = Guid.NewGuid().ToString(),
            UserLastName = Guid.NewGuid().ToString(),
            WorkerEmail = $"{Guid.NewGuid()}@test.com"
        };

        // Act

        // Act
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);

        await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            TimePlanningPnDbContext!, BaseDbContext!,
        userService,
        userManager);

        var currentSite = await MicrotingDbContext!.Sites.OrderByDescending(x => x.Id).FirstAsync();

        var newDeviceUserModel = new DeviceUserModel
        {
            SiteMicrotingUid = (int)currentSite.MicrotingUid!,
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = true,
            UserFirstName = Guid.NewGuid().ToString(),
            UserLastName = Guid.NewGuid().ToString(),
            WorkerEmail = $"{Guid.NewGuid()}@test.com"
        };

        var result = await BackendConfigurationAssignmentWorkerServiceHelper.UpdateDeviceUser(newDeviceUserModel, core,
            1,
            userService,
            userManager,
            BackendConfigurationPnDbContext!,
            TimePlanningPnDbContext!, BaseDbContext!, logger, ItemsPlanningPnDbContext!);

        // Assert
        var sites = await MicrotingDbContext!.Sites.AsNoTracking().ToListAsync();
        var workers = await MicrotingDbContext.Workers.AsNoTracking().ToListAsync();
        var siteWorkers = await MicrotingDbContext.SiteWorkers.AsNoTracking().ToListAsync();
        var units = await MicrotingDbContext.Units.AsNoTracking().ToListAsync();
        var timeregistrationSiteAssignments = await TimePlanningPnDbContext!.AssignedSites.AsNoTracking().ToListAsync();

        Assert.That(result, Is.Not.Null);
        Assert.That(sites.Count, Is.EqualTo(3));
        Assert.That(workers.Count, Is.EqualTo(3));
        Assert.That(units.Count, Is.EqualTo(3));

        // Assert site
        Assert.That(sites[2].Name, Is.EqualTo(newDeviceUserModel.UserFirstName + " " + newDeviceUserModel.UserLastName));

        // Assert worker
        Assert.That(workers[2].FirstName, Is.EqualTo(newDeviceUserModel.UserFirstName));
        Assert.That(workers[2].LastName, Is.EqualTo(newDeviceUserModel.UserLastName));

        // Assert siteWorker
        Assert.That(siteWorkers[2].WorkerId, Is.EqualTo(workers[2].Id));
        Assert.That(siteWorkers[2].SiteId, Is.EqualTo(sites[2].Id));

        // Assert unit
        Assert.That(units[2].SiteId, Is.EqualTo(sites[2].Id));

        // Assert timeregistrationSiteAssignments
        Assert.That(timeregistrationSiteAssignments.Count, Is.EqualTo(31));
        Assert.That(timeregistrationSiteAssignments[30].SiteId, Is.EqualTo(sites[2].MicrotingUid));

        // The UpdateDeviceUser create path hardcodes UseOneMinuteIntervals to true. This site has no
        // earlier assignment, so no effective date is stamped and the derived timeline covers its whole
        // history. The re-enable case, where a stamp IS required, is covered by
        // ..._TimeRegistrationReEnabledOnFiveMinuteSite_StampsEffectiveDate below.
        Assert.That(timeregistrationSiteAssignments[30].UseOneMinuteIntervals, Is.True);
        Assert.That(timeregistrationSiteAssignments[30].UseOneMinuteIntervalsFrom, Is.Null);
    }

    // Should test the UpdateDeviceUser method with timeRegistration set to false and return success
    [Test]
    public async Task
        BackendConfigurationAssignmentWorkerServiceHelper_UpdateDeviceUser_TimeRegistrationDisabled_ReturnsSuccess()
    {
        // Arrange
        var core = await GetCore();
        var logger = Substitute.For<ILogger>();

        var propertyCreateModel = new PropertyCreateModel
        {
            Address = Guid.NewGuid().ToString(),
            Chr = Guid.NewGuid().ToString(),
            IndustryCode = Guid.NewGuid().ToString(),
            Cvr = Guid.NewGuid().ToString(),
            IsFarm = true,
            LanguagesIds = [1],
            MainMailAddress = Guid.NewGuid().ToString(),
            Name = Guid.NewGuid().ToString(),
            WorkorderEnable = false
        };

        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1,
            BackendConfigurationPnDbContext!, ItemsPlanningPnDbContext!, 1, 1);

        var deviceUserModel = new DeviceUserModel
        {
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = true,
            UserFirstName = Guid.NewGuid().ToString(),
            UserLastName = Guid.NewGuid().ToString(),
            WorkerEmail = $"{Guid.NewGuid()}@test.com"
        };

        // Act

        // Act
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);

        await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            TimePlanningPnDbContext!, BaseDbContext!,
        userService,
        userManager);

        var currentSite = await MicrotingDbContext!.Sites.OrderByDescending(x => x.Id).FirstAsync();

        var newDeviceUserModel = new DeviceUserModel
        {
            SiteMicrotingUid = (int)currentSite.MicrotingUid!,
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = false,
            UserFirstName = Guid.NewGuid().ToString(),
            UserLastName = Guid.NewGuid().ToString(),
            WorkerEmail = $"{Guid.NewGuid()}@test.com"
        };

        var result = await BackendConfigurationAssignmentWorkerServiceHelper.UpdateDeviceUser(newDeviceUserModel, core,
            1,
            userService,
            userManager,
            BackendConfigurationPnDbContext!,
            TimePlanningPnDbContext!, BaseDbContext!, logger, ItemsPlanningPnDbContext!);

        // Assert
        var sites = await MicrotingDbContext!.Sites.AsNoTracking().ToListAsync();
        var workers = await MicrotingDbContext.Workers.AsNoTracking().ToListAsync();
        var siteWorkers = await MicrotingDbContext.SiteWorkers.AsNoTracking().ToListAsync();
        var units = await MicrotingDbContext.Units.AsNoTracking().ToListAsync();
        var timeregistrationSiteAssignments = await TimePlanningPnDbContext!.AssignedSites.AsNoTracking().ToListAsync();
        var propertyWorkers = await BackendConfigurationPnDbContext!.PropertyWorkers.AsNoTracking().ToListAsync();

        Assert.That(result, Is.Not.Null);
        Assert.That(sites.Count, Is.EqualTo(3));
        Assert.That(workers.Count, Is.EqualTo(3));
        Assert.That(units.Count, Is.EqualTo(3));

        // Assert site
        Assert.That(sites[2].Name, Is.EqualTo(newDeviceUserModel.UserFirstName + " " + newDeviceUserModel.UserLastName));

        // Assert worker
        Assert.That(workers[2].FirstName, Is.EqualTo(newDeviceUserModel.UserFirstName));
        Assert.That(workers[2].LastName, Is.EqualTo(newDeviceUserModel.UserLastName));

        // Assert siteWorker
        Assert.That(siteWorkers[2].WorkerId, Is.EqualTo(workers[2].Id));
        Assert.That(siteWorkers[2].SiteId, Is.EqualTo(sites[2].Id));

        // Assert unit
        Assert.That(units[2].SiteId, Is.EqualTo(sites[2].Id));

        // Assert timeregistrationSiteAssignments
        Assert.That(timeregistrationSiteAssignments.Count, Is.EqualTo(31));
        Assert.That(timeregistrationSiteAssignments[30].SiteId, Is.EqualTo(sites[2].MicrotingUid));
        Assert.That(timeregistrationSiteAssignments[30].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
        Assert.That(timeregistrationSiteAssignments[30].Resigned, Is.False);
        // Assert.That(timeregistrationSiteAssignments[0].ResignedAtDate.Year, Is.EqualTo(DateTime.Now.Year));
        // Assert.That(timeregistrationSiteAssignments[0].ResignedAtDate.Month, Is.EqualTo(DateTime.Now.Month));
        // Assert.That(timeregistrationSiteAssignments[0].ResignedAtDate.Day, Is.EqualTo(DateTime.Now.Day));

        // Assert propertyWorkers
        Assert.That(propertyWorkers.Count, Is.EqualTo(0));
    }

    // Disabling time registration soft-deletes the AssignedSite, so re-enabling it creates a NEW row
    // for a site that keeps its old PlanRegistrations (they hang off SdkSitId). The new row is
    // hardcoded to one-minute mode, but for a site that was previously in 5-minute mode the effective
    // date must be stamped, otherwise OneMinuteModeTimeline — which sees no version rows on the new
    // row — would derive one-minute mode all the way back and reinterpret every historical tick row.
    [Test]
    public async Task
        BackendConfigurationAssignmentWorkerServiceHelper_UpdateDeviceUser_TimeRegistrationReEnabledOnFiveMinuteSite_StampsEffectiveDate()
    {
        // Arrange
        var core = await GetCore();
        var logger = Substitute.For<ILogger>();

        var propertyCreateModel = new PropertyCreateModel
        {
            Address = Guid.NewGuid().ToString(),
            Chr = Guid.NewGuid().ToString(),
            IndustryCode = Guid.NewGuid().ToString(),
            Cvr = Guid.NewGuid().ToString(),
            IsFarm = true,
            LanguagesIds = [1],
            MainMailAddress = Guid.NewGuid().ToString(),
            Name = Guid.NewGuid().ToString(),
            WorkorderEnable = false
        };

        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1,
            BackendConfigurationPnDbContext!, ItemsPlanningPnDbContext!, 1, 1);

        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);

        var deviceUserModel = new DeviceUserModel
        {
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = true,
            UserFirstName = Guid.NewGuid().ToString(),
            UserLastName = Guid.NewGuid().ToString(),
            WorkerEmail = $"{Guid.NewGuid()}@test.com"
        };

        await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            TimePlanningPnDbContext!, BaseDbContext!, userService, userManager);

        var currentSite = await MicrotingDbContext!.Sites.OrderByDescending(x => x.Id).FirstAsync();
        var siteMicrotingUid = (int)currentSite.MicrotingUid!;

        // Backdate the assignment to 5-minute mode, standing in for a site that was set up before
        // one-minute intervals became the default for every new time registration setup.
        var legacyAssignment = await TimePlanningPnDbContext!.AssignedSites
            .OrderByDescending(x => x.Id)
            .FirstAsync(x => x.SiteId == siteMicrotingUid);
        legacyAssignment.UseOneMinuteIntervals = false;
        legacyAssignment.UseOneMinuteIntervalsFrom = null;
        await legacyAssignment.Update(TimePlanningPnDbContext!);

        var userFirstName = Guid.NewGuid().ToString();
        var userLastName = Guid.NewGuid().ToString();
        var workerEmail = $"{Guid.NewGuid()}@test.com";

        DeviceUserModel UpdateModel(bool timeRegistrationEnabled) => new()
        {
            SiteMicrotingUid = siteMicrotingUid,
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = timeRegistrationEnabled,
            UserFirstName = userFirstName,
            UserLastName = userLastName,
            WorkerEmail = workerEmail
        };

        // Act — disable, then re-enable
        await BackendConfigurationAssignmentWorkerServiceHelper.UpdateDeviceUser(UpdateModel(false), core, 1,
            userService, userManager, BackendConfigurationPnDbContext!, TimePlanningPnDbContext!, BaseDbContext!,
            logger, ItemsPlanningPnDbContext!);

        var result = await BackendConfigurationAssignmentWorkerServiceHelper.UpdateDeviceUser(UpdateModel(true), core, 1,
            userService, userManager, BackendConfigurationPnDbContext!, TimePlanningPnDbContext!, BaseDbContext!,
            logger, ItemsPlanningPnDbContext!);

        // Assert
        // The create branch swallows exceptions into a failure result, so Is.Not.Null would pass on a
        // broken run; assert the operation actually succeeded.
        Assert.That(result.Success, Is.True, result.Message);

        var assignmentsForSite = await TimePlanningPnDbContext!.AssignedSites.AsNoTracking()
            .Where(x => x.SiteId == siteMicrotingUid)
            .OrderBy(x => x.Id)
            .ToListAsync();

        // The old row was soft-deleted and a brand new one minted alongside it.
        Assert.That(assignmentsForSite.Count, Is.EqualTo(2));
        Assert.That(assignmentsForSite[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
        Assert.That(assignmentsForSite[0].UseOneMinuteIntervals, Is.False);

        var reEnabled = assignmentsForSite[1];
        Assert.That(reEnabled.WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Removed));
        Assert.That(reEnabled.UseOneMinuteIntervals, Is.True);
        // Stamped, so the site's pre-existing registrations keep reading as 5-minute mode.
        Assert.That(reEnabled.UseOneMinuteIntervalsFrom, Is.Not.Null);
        // Not-null alone is too weak: DateTime.MinValue is non-null and would reinterpret every
        // historical row exactly as before the fix. Assert the behaviour the stamp exists for.
        var timeline = await OneMinuteModeTimeline.BuildAsync(TimePlanningPnDbContext!, reEnabled);
        Assert.That(timeline.WasOneMinuteAt(DateTime.UtcNow.AddDays(-1)), Is.False,
            "Registrations from before the re-enable must still resolve as 5-minute mode.");
        Assert.That(timeline.WasOneMinuteAt(DateTime.UtcNow), Is.True,
            "From the re-enable onwards the site is in one-minute mode.");
    }

    // The legacy un-backfilled shape: an AssignedSite switched from 5-minute to one-minute mode BEFORE
    // UseOneMinuteIntervalsFrom existed, so the flag is true but the stamp is NULL and the transition
    // survives ONLY as an AssignedSiteVersions row. Those version rows are keyed to the OLD
    // AssignedSiteId and therefore do not follow the site into the new row that re-enabling time
    // registration mints, so carrying the NULL across would make the new row's timeline read one-minute
    // mode all the way back. The create path must recover the transition date from the old row's
    // version rows instead.
    [Test]
    public async Task
        BackendConfigurationAssignmentWorkerServiceHelper_UpdateDeviceUser_TimeRegistrationReEnabledOnUnstampedOneMinuteSite_RecoversEffectiveDateFromVersions()
    {
        // Arrange
        var core = await GetCore();
        var logger = Substitute.For<ILogger>();

        var propertyCreateModel = new PropertyCreateModel
        {
            Address = Guid.NewGuid().ToString(),
            Chr = Guid.NewGuid().ToString(),
            IndustryCode = Guid.NewGuid().ToString(),
            Cvr = Guid.NewGuid().ToString(),
            IsFarm = true,
            LanguagesIds = [1],
            MainMailAddress = Guid.NewGuid().ToString(),
            Name = Guid.NewGuid().ToString(),
            WorkorderEnable = false
        };

        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1,
            BackendConfigurationPnDbContext!, ItemsPlanningPnDbContext!, 1, 1);

        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);

        // Time registration deliberately OFF at creation: the legacy AssignedSite is built by hand
        // below so its FIRST version row carries UseOneMinuteIntervals = false, the way a site set up
        // before one-minute intervals became the default looks. (CreateDeviceUser would hardcode true,
        // which is a different history entirely.)
        var deviceUserModel = new DeviceUserModel
        {
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = false,
            UserFirstName = Guid.NewGuid().ToString(),
            UserLastName = Guid.NewGuid().ToString(),
            WorkerEmail = $"{Guid.NewGuid()}@test.com"
        };

        await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            TimePlanningPnDbContext!, BaseDbContext!, userService, userManager);

        var currentSite = await MicrotingDbContext!.Sites.OrderByDescending(x => x.Id).FirstAsync();
        var siteMicrotingUid = (int)currentSite.MicrotingUid!;

        var legacyAssignment = new AssignedSite
        {
            SiteId = siteMicrotingUid,
            CreatedByUserId = 1,
            UpdatedByUserId = 1,
            UseOneMinuteIntervals = false
        };
        await legacyAssignment.Create(TimePlanningPnDbContext!);

        // The legacy switch: the flag flips to true and NO stamp is written, so the false→true
        // transition exists only in the version row this Update writes.
        legacyAssignment.UseOneMinuteIntervals = true;
        legacyAssignment.UseOneMinuteIntervalsFrom = null;
        await legacyAssignment.Update(TimePlanningPnDbContext!);

        // Backdate that version row so the recovered date is provably the transition's own and not
        // simply "now" — a stamp of today would be indistinguishable from the 5-minute-site case.
        var transitionVersion = await TimePlanningPnDbContext!.AssignedSiteVersions
            .Where(x => x.AssignedSiteId == legacyAssignment.Id && x.UseOneMinuteIntervals)
            .OrderBy(x => x.Id)
            .FirstAsync();
        var transitionDate = DateTime.UtcNow.AddDays(-30);
        transitionVersion.UpdatedAt = transitionDate;
        await TimePlanningPnDbContext!.SaveChangesAsync();

        var userFirstName = Guid.NewGuid().ToString();
        var userLastName = Guid.NewGuid().ToString();
        var workerEmail = $"{Guid.NewGuid()}@test.com";

        DeviceUserModel UpdateModel(bool timeRegistrationEnabled) => new()
        {
            SiteMicrotingUid = siteMicrotingUid,
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = timeRegistrationEnabled,
            UserFirstName = userFirstName,
            UserLastName = userLastName,
            WorkerEmail = workerEmail
        };

        // Act — disable, then re-enable
        await BackendConfigurationAssignmentWorkerServiceHelper.UpdateDeviceUser(UpdateModel(false), core, 1,
            userService, userManager, BackendConfigurationPnDbContext!, TimePlanningPnDbContext!, BaseDbContext!,
            logger, ItemsPlanningPnDbContext!);

        var result = await BackendConfigurationAssignmentWorkerServiceHelper.UpdateDeviceUser(UpdateModel(true), core, 1,
            userService, userManager, BackendConfigurationPnDbContext!, TimePlanningPnDbContext!, BaseDbContext!,
            logger, ItemsPlanningPnDbContext!);

        // Assert
        Assert.That(result.Success, Is.True, result.Message);

        var assignmentsForSite = await TimePlanningPnDbContext!.AssignedSites.AsNoTracking()
            .Where(x => x.SiteId == siteMicrotingUid)
            .OrderBy(x => x.Id)
            .ToListAsync();

        // The old row was soft-deleted and a brand new one minted alongside it.
        Assert.That(assignmentsForSite.Count, Is.EqualTo(2));
        Assert.That(assignmentsForSite[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));

        var reEnabled = assignmentsForSite[1];
        Assert.That(reEnabled.WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Removed));
        Assert.That(reEnabled.UseOneMinuteIntervals, Is.True);
        // Recovered from the OLD row's version rows — the new row has none of its own.
        Assert.That(reEnabled.UseOneMinuteIntervalsFrom, Is.Not.Null);
        Assert.That(reEnabled.UseOneMinuteIntervalsFrom!.Value.Date, Is.EqualTo(transitionDate.Date),
            "The stamp must be the legacy false→true transition date, not the re-enable date.");

        var timeline = await OneMinuteModeTimeline.BuildAsync(TimePlanningPnDbContext!, reEnabled);
        Assert.That(timeline.WasOneMinuteAt(transitionDate.AddDays(-1)), Is.False,
            "Registrations from before the legacy switch must still resolve as 5-minute mode.");
        Assert.That(timeline.WasOneMinuteAt(transitionDate.AddDays(1)), Is.True,
            "Registrations from after the legacy switch resolve as one-minute mode.");
    }

    // Should test the CreateDeviceUser method with TimeRegistrationEnabled and OverMidnight set,
    // verifying the flag passes through onto the created AssignedSite
    [Test]
    public async Task
        BackendConfigurationAssignmentWorkerServiceHelper_CreateDeviceUser_TimeRegistrationEnabled_OverMidnight_ReturnsSuccess()
    {
        // Arrange
        var core = await GetCore();

        var deviceUserModel = new DeviceUserModel
        {
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = true,
            OverMidnight = true,
            UserFirstName = Guid.NewGuid().ToString(),
            UserLastName = Guid.NewGuid().ToString(),
            WorkerEmail = $"{Guid.NewGuid()}@test.com"
        };

        // Act
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);

        var result = await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            TimePlanningPnDbContext!, BaseDbContext!,
        userService,
        userManager);

        // Assert
        var sites = await MicrotingDbContext!.Sites.ToListAsync();
        var timeregistrationSiteAssignments = await TimePlanningPnDbContext!.AssignedSites.ToListAsync();

        Assert.That(result, Is.Not.Null);
        Assert.That(sites.Count, Is.EqualTo(3));

        // Assert timeregistrationSiteAssignments
        Assert.That(timeregistrationSiteAssignments.Count, Is.EqualTo(31));
        Assert.That(timeregistrationSiteAssignments[30].SiteId, Is.EqualTo(sites[2].MicrotingUid));

        // Newly-created AssignedSite must pass through OverMidnight from the DeviceUserModel
        Assert.That(timeregistrationSiteAssignments[30].OverMidnight, Is.True);
    }

    // Should test the UpdateDeviceUser method with OverMidnight: the create-from-update path must
    // pass the flag onto the new AssignedSite, a later update without the flag must clear it
    [Test]
    public async Task
        BackendConfigurationAssignmentWorkerServiceHelper_UpdateDeviceUser_OverMidnight_PersistsAndClears()
    {
        // Arrange
        var core = await GetCore();
        var logger = Substitute.For<ILogger>();

        var propertyCreateModel = new PropertyCreateModel
        {
            Address = Guid.NewGuid().ToString(),
            Chr = Guid.NewGuid().ToString(),
            IndustryCode = Guid.NewGuid().ToString(),
            Cvr = Guid.NewGuid().ToString(),
            IsFarm = true,
            LanguagesIds = [1],
            MainMailAddress = Guid.NewGuid().ToString(),
            Name = Guid.NewGuid().ToString(),
            WorkorderEnable = false
        };

        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1,
            BackendConfigurationPnDbContext!, ItemsPlanningPnDbContext!, 1, 1);

        var deviceUserModel = new DeviceUserModel
        {
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = false,
            UserFirstName = Guid.NewGuid().ToString(),
            UserLastName = Guid.NewGuid().ToString(),
            WorkerEmail = $"{Guid.NewGuid()}@test.com"
        };

        // Act
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);

        await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            TimePlanningPnDbContext!, BaseDbContext!,
        userService,
        userManager);

        var currentSite = await MicrotingDbContext!.Sites.OrderByDescending(x => x.Id).FirstAsync();

        // First update: enable time registration with OverMidnight set — the
        // create-from-update path must carry the flag onto the new AssignedSite.
        var newDeviceUserModel = new DeviceUserModel
        {
            SiteMicrotingUid = (int)currentSite.MicrotingUid!,
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = true,
            OverMidnight = true,
            UserFirstName = Guid.NewGuid().ToString(),
            UserLastName = Guid.NewGuid().ToString(),
            WorkerEmail = $"{Guid.NewGuid()}@test.com"
        };

        var result = await BackendConfigurationAssignmentWorkerServiceHelper.UpdateDeviceUser(newDeviceUserModel, core,
            1,
            userService,
            userManager,
            BackendConfigurationPnDbContext!,
            TimePlanningPnDbContext!, BaseDbContext!, logger, ItemsPlanningPnDbContext!);

        // Assert first update
        var sites = await MicrotingDbContext!.Sites.AsNoTracking().ToListAsync();
        var timeregistrationSiteAssignments = await TimePlanningPnDbContext!.AssignedSites.AsNoTracking().ToListAsync();

        Assert.That(result, Is.Not.Null);
        Assert.That(timeregistrationSiteAssignments.Count, Is.EqualTo(31));
        Assert.That(timeregistrationSiteAssignments[30].SiteId, Is.EqualTo(sites[2].MicrotingUid));
        Assert.That(timeregistrationSiteAssignments[30].OverMidnight, Is.True);

        // Second update: OverMidnight not set on the model — the existing
        // AssignedSite update path must clear it (null coalesces to false).
        var clearDeviceUserModel = new DeviceUserModel
        {
            SiteMicrotingUid = (int)currentSite.MicrotingUid!,
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = true,
            UserFirstName = Guid.NewGuid().ToString(),
            UserLastName = Guid.NewGuid().ToString(),
            WorkerEmail = $"{Guid.NewGuid()}@test.com"
        };

        var clearResult = await BackendConfigurationAssignmentWorkerServiceHelper.UpdateDeviceUser(clearDeviceUserModel,
            core,
            1,
            userService,
            userManager,
            BackendConfigurationPnDbContext!,
            TimePlanningPnDbContext!, BaseDbContext!, logger, ItemsPlanningPnDbContext!);

        // Assert second update
        timeregistrationSiteAssignments = await TimePlanningPnDbContext!.AssignedSites.AsNoTracking().ToListAsync();

        Assert.That(clearResult, Is.Not.Null);
        Assert.That(timeregistrationSiteAssignments.Count, Is.EqualTo(31));
        Assert.That(timeregistrationSiteAssignments[30].OverMidnight, Is.False);
        // The sibling punch-clock flag must be unaffected by the OverMidnight writes
        Assert.That(timeregistrationSiteAssignments[30].UsePunchClockWithAllowRegisteringInHistory, Is.False);
    }

    // Should test the Create method and return success
    [Test]
    public async Task BackendConfigurationAssignmentWorkerServiceHelper_Create_ReturnsSuccess()
    {
        // Arrange
        var core = await GetCore();

        var propertyCreateModel = new PropertyCreateModel
        {
            Address = Guid.NewGuid().ToString(),
            Chr = Guid.NewGuid().ToString(),
            IndustryCode = Guid.NewGuid().ToString(),
            Cvr = Guid.NewGuid().ToString(),
            IsFarm = true,
            LanguagesIds = [1],
            MainMailAddress = Guid.NewGuid().ToString(),
            Name = Guid.NewGuid().ToString(),
            WorkorderEnable = false
        };

        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1,
            BackendConfigurationPnDbContext!, ItemsPlanningPnDbContext!, 1, 1);

        var deviceUserModel = new DeviceUserModel
        {
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = false,
            UserFirstName = Guid.NewGuid().ToString(),
            UserLastName = Guid.NewGuid().ToString(),
            WorkerEmail = $"{Guid.NewGuid()}@test.com"
        };

        // Act

        // Act
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);

        /*var result = */await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            TimePlanningPnDbContext!, BaseDbContext!,
        userService,
        userManager);

        var properties = await BackendConfigurationPnDbContext!.Properties.ToListAsync();
        var sites = await MicrotingDbContext!.Sites.AsNoTracking().ToListAsync();

        var propertyAssignWorkersModel = new PropertyAssignWorkersModel
        {
            Assignments =
            [
                new()
                {
                    PropertyId = properties[0].Id,
                    IsChecked = true
                }
            ],
            SiteId = sites[2].Id
        };
        var result2 = await BackendConfigurationAssignmentWorkerServiceHelper.Create(propertyAssignWorkersModel, core, userService,
             BackendConfigurationPnDbContext!, CaseTemplatePnDbContext!, null);

        // Assert
        var workers = await MicrotingDbContext.Workers.AsNoTracking().ToListAsync();
        var siteWorkers = await MicrotingDbContext.SiteWorkers.AsNoTracking().ToListAsync();
        var units = await MicrotingDbContext.Units.AsNoTracking().ToListAsync();
        var timeregistrationSiteAssignments =
            await TimePlanningPnDbContext!.AssignedSites.AsNoTracking().ToListAsync();
        var propertyWorkers = await BackendConfigurationPnDbContext!.PropertyWorkers.AsNoTracking().ToListAsync();

        Assert.That(result2, Is.Not.Null);
        Assert.That(result2.Success, Is.True);
        Assert.That(sites.Count, Is.EqualTo(3));
        Assert.That(workers.Count, Is.EqualTo(3));
        Assert.That(units.Count, Is.EqualTo(3));

        // Assert site
        Assert.That(sites[2].Name, Is.EqualTo(deviceUserModel.UserFirstName + " " + deviceUserModel.UserLastName));

        // Assert worker
        Assert.That(workers[2].FirstName, Is.EqualTo(deviceUserModel.UserFirstName));

        // Assert siteWorker
        Assert.That(siteWorkers[2].WorkerId, Is.EqualTo(workers[2].Id));

        // Assert unit
        Assert.That(units[2].SiteId, Is.EqualTo(sites[2].Id));

        // Assert timeregistrationSiteAssignments
        Assert.That(timeregistrationSiteAssignments.Count, Is.EqualTo(30));

        // Assert propertyWorkers
        Assert.That(propertyWorkers.Count, Is.EqualTo(1));
        Assert.That(propertyWorkers[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(propertyWorkers[0].WorkerId, Is.EqualTo(workers[2].Id));
    }

    // Should test the Update method and return success
    [Test]
    public async Task BackendConfigurationAssignmentWorkerServiceHelper_Update_ReturnsSuccess()
    {
        // Arrange
        var core = await GetCore();

        var propertyCreateModel = new PropertyCreateModel
        {
            Address = Guid.NewGuid().ToString(),
            Chr = Guid.NewGuid().ToString(),
            IndustryCode = Guid.NewGuid().ToString(),
            Cvr = Guid.NewGuid().ToString(),
            IsFarm = true,
            LanguagesIds = [1],
            MainMailAddress = Guid.NewGuid().ToString(),
            Name = Guid.NewGuid().ToString(),
            WorkorderEnable = false
        };

        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1,
            BackendConfigurationPnDbContext!, ItemsPlanningPnDbContext!, 1, 1);

        var deviceUserModel = new DeviceUserModel
        {
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = false,
            UserFirstName = Guid.NewGuid().ToString(),
            UserLastName = Guid.NewGuid().ToString(),
            WorkerEmail = $"{Guid.NewGuid()}@test.com"
        };

        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);

        /*var result = */await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            TimePlanningPnDbContext!, BaseDbContext!,
        userService,
        userManager);

        var properties = await BackendConfigurationPnDbContext!.Properties.ToListAsync();
        var sites = await MicrotingDbContext!.Sites.AsNoTracking().ToListAsync();

        var propertyAssignWorkersModel = new PropertyAssignWorkersModel
        {
            Assignments =
            [
                new()
                {
                    PropertyId = properties[0].Id,
                    IsChecked = true
                }
            ],
            SiteId = sites[2].Id
        };
        await BackendConfigurationAssignmentWorkerServiceHelper.Create(propertyAssignWorkersModel, core, userService,
            BackendConfigurationPnDbContext!, CaseTemplatePnDbContext!, null);

        var propertyAssignWorkersModel2 = new PropertyAssignWorkersModel
        {
            Assignments =
            [
                new()
                {
                    PropertyId = properties[0].Id,
                    IsChecked = false
                }
            ],
            TaskManagementEnabled = false,
            SiteId = sites[2].Id
        };

        // Act
        // var userService = Substitute.For<IUserService>();
        // userService.UserId.Returns(1);
        var result2 = await BackendConfigurationAssignmentWorkerServiceHelper.Update(propertyAssignWorkersModel2, core, userService,
            BackendConfigurationPnDbContext!, CaseTemplatePnDbContext!, null, ItemsPlanningPnDbContext!);

        // Assert
        var workers = await MicrotingDbContext.Workers.AsNoTracking().ToListAsync();
        var siteWorkers = await MicrotingDbContext.SiteWorkers.AsNoTracking().ToListAsync();
        var units = await MicrotingDbContext.Units.AsNoTracking().ToListAsync();
        var timeregistrationSiteAssignments =
            await TimePlanningPnDbContext!.AssignedSites.AsNoTracking().ToListAsync();
        var propertyWorkers = await BackendConfigurationPnDbContext!.PropertyWorkers.AsNoTracking().ToListAsync();

        Assert.That(result2, Is.Not.Null);
        Assert.That(result2.Success, Is.True);
        Assert.That(sites.Count, Is.EqualTo(3));
        Assert.That(workers.Count, Is.EqualTo(3));
        Assert.That(units.Count, Is.EqualTo(3));

        // Assert site
        Assert.That(sites[2].Name, Is.EqualTo(deviceUserModel.UserFirstName + " " + deviceUserModel.UserLastName));

        // Assert worker
        Assert.That(workers[2].FirstName, Is.EqualTo(deviceUserModel.UserFirstName));

        // Assert siteWorker
        Assert.That(siteWorkers[2].WorkerId, Is.EqualTo(workers[2].Id));

        // Assert unit
        Assert.That(units[2].SiteId, Is.EqualTo(sites[2].Id));

        // Assert timeregistrationSiteAssignments
        Assert.That(timeregistrationSiteAssignments.Count, Is.EqualTo(30));

        // Assert propertyWorkers
        Assert.That(propertyWorkers.Count, Is.EqualTo(1));
        Assert.That(propertyWorkers[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(propertyWorkers[0].WorkerId, Is.EqualTo(workers[2].Id));
    }

    // Should test the Update method with TaskManagementEnabled set to true  and return success
    [Test]
    public async Task BackendConfigurationAssignmentWorkerServiceHelper_Update_TaskManagementEnabled_ReturnsSuccess()
    {
        // Arrange
        var core = await GetCore();

        var propertyCreateModel = new PropertyCreateModel
        {
            Address = Guid.NewGuid().ToString(),
            Chr = Guid.NewGuid().ToString(),
            IndustryCode = Guid.NewGuid().ToString(),
            Cvr = Guid.NewGuid().ToString(),
            IsFarm = true,
            LanguagesIds = [1],
            MainMailAddress = Guid.NewGuid().ToString(),
            Name = Guid.NewGuid().ToString(),
            WorkorderEnable = true
        };

        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1,
            BackendConfigurationPnDbContext!, ItemsPlanningPnDbContext!, 1, 1);

        var deviceUserModel = new DeviceUserModel
        {
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = false,
            UserFirstName = Guid.NewGuid().ToString(),
            UserLastName = Guid.NewGuid().ToString(),
            WorkerEmail = $"{Guid.NewGuid()}@test.com"
        };

        // Act
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);

        await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            TimePlanningPnDbContext!, BaseDbContext!,
        userService,
        userManager);

        var properties = await BackendConfigurationPnDbContext!.Properties.ToListAsync();
        var sites = await MicrotingDbContext!.Sites.AsNoTracking().ToListAsync();

        var propertyAssignWorkersModel = new PropertyAssignWorkersModel
        {
            Assignments =
            [
                new()
                {
                    PropertyId = properties[0].Id,
                    IsChecked = true
                }
            ],
            TaskManagementEnabled = false,
            TimeRegistrationEnabled = false,
            SiteId = sites[2].Id
        };

        await BackendConfigurationAssignmentWorkerServiceHelper.Create(propertyAssignWorkersModel, core, userService,
            BackendConfigurationPnDbContext!, CaseTemplatePnDbContext!, null);

        var propertyAssignWorkersModel2 = new PropertyAssignWorkersModel
        {
            Assignments =
            [
                new()
                {
                    PropertyId = properties[0].Id,
                    IsChecked = true
                }
            ],
            TaskManagementEnabled = true,
            TimeRegistrationEnabled = false,
            SiteId = sites[2].Id
        };


        // Act
        // var userService = Substitute.For<IUserService>();
        // userService.UserId.Returns(1);
        var result2 = await BackendConfigurationAssignmentWorkerServiceHelper.Update(propertyAssignWorkersModel2, core, userService,
            BackendConfigurationPnDbContext!, CaseTemplatePnDbContext!, null, ItemsPlanningPnDbContext!);

        // Assert
        var workers = await MicrotingDbContext.Workers.AsNoTracking().ToListAsync();
        var siteWorkers = await MicrotingDbContext.SiteWorkers.AsNoTracking().ToListAsync();
        var units = await MicrotingDbContext.Units.AsNoTracking().ToListAsync();
        var timeregistrationSiteAssignments =
            await TimePlanningPnDbContext!.AssignedSites.AsNoTracking().ToListAsync();
        var propertyWorkers = await BackendConfigurationPnDbContext!.PropertyWorkers.AsNoTracking().ToListAsync();
        var workOrders = await BackendConfigurationPnDbContext!.WorkorderCases.AsNoTracking().ToListAsync();
        var sdkCases = await MicrotingDbContext!.Cases.AsNoTracking().ToListAsync();
        var checkListSites = await MicrotingDbContext!.CheckListSites.AsNoTracking().ToListAsync();

        Assert.That(result2, Is.Not.Null);
        Assert.That(result2.Success, Is.True);
        Assert.That(sites.Count, Is.EqualTo(3));
        Assert.That(workers.Count, Is.EqualTo(3));
        Assert.That(units.Count, Is.EqualTo(3));

        // Assert site
        Assert.That(sites[2].Name, Is.EqualTo(deviceUserModel.UserFirstName + " " + deviceUserModel.UserLastName));

        // Assert worker
        Assert.That(workers[2].FirstName, Is.EqualTo(deviceUserModel.UserFirstName));

        // Assert siteWorker
        Assert.That(siteWorkers[2].WorkerId, Is.EqualTo(workers[2].Id));

        // Assert unit
        Assert.That(units[2].SiteId, Is.EqualTo(sites[2].Id));

        // Assert timeregistrationSiteAssignments
        Assert.That(timeregistrationSiteAssignments.Count, Is.EqualTo(30));

        // Assert propertyWorkers
        Assert.That(propertyWorkers.Count, Is.EqualTo(1));
        Assert.That(propertyWorkers[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(propertyWorkers[0].WorkerId, Is.EqualTo(workers[2].Id));

        // Assert workOrders
        Assert.That(workOrders.Count, Is.EqualTo(1));
        Assert.That(workOrders[0].PropertyWorkerId, Is.EqualTo(propertyWorkers[0].Id));
        Assert.That(workOrders[0].LeadingCase, Is.EqualTo(false));

        // Assert sdkCases
        Assert.That(sdkCases.Count, Is.EqualTo(0));

        // Assert checkListSites
        Assert.That(checkListSites.Count, Is.EqualTo(1));
        Assert.That(checkListSites[0].SiteId, Is.EqualTo(sites[2].Id));
        Assert.That(checkListSites[0].MicrotingUid, Is.EqualTo(workOrders[0].CaseId));
    }

    // Should test Update method and reassign from one property to another
    [Test]
    public async Task BackendConfigurationAssignmentWorkerServiceHelper_Update_ReassignFromOnePropertyToAnother()
    {
        // Arrange
        var core = await GetCore();

        var propertyCreateModel = new PropertyCreateModel
        {
            Address = Guid.NewGuid().ToString(),
            Chr = Guid.NewGuid().ToString(),
            IndustryCode = Guid.NewGuid().ToString(),
            Cvr = Guid.NewGuid().ToString(),
            IsFarm = true,
            LanguagesIds = [1],
            MainMailAddress = Guid.NewGuid().ToString(),
            Name = Guid.NewGuid().ToString(),
            WorkorderEnable = true
        };

        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1,
            BackendConfigurationPnDbContext!, ItemsPlanningPnDbContext!, 1, 1);

        // create another propertycreateModel
        var propertyCreateModel2 = new PropertyCreateModel
        {
            Address = Guid.NewGuid().ToString(),
            Chr = Guid.NewGuid().ToString(),
            IndustryCode = Guid.NewGuid().ToString(),
            Cvr = Guid.NewGuid().ToString(),
            IsFarm = true,
            LanguagesIds = [1],
            MainMailAddress = Guid.NewGuid().ToString(),
            Name = Guid.NewGuid().ToString(),
            WorkorderEnable = true
        };

        await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel2, core, 1,
            BackendConfigurationPnDbContext!, ItemsPlanningPnDbContext!, 2, 2);

        // Act
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);

        var deviceUserModel = new DeviceUserModel
        {
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = false,
            UserFirstName = Guid.NewGuid().ToString(),
            UserLastName = Guid.NewGuid().ToString(),
            TaskManagementEnabled = true,
            WorkerEmail = $"{Guid.NewGuid()}@test.com"
        };

        await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            TimePlanningPnDbContext!, BaseDbContext!,
        userService,
        userManager);

        var properties = await BackendConfigurationPnDbContext!.Properties.ToListAsync();
        var sites = await MicrotingDbContext!.Sites.AsNoTracking().ToListAsync();

        var propertyAssignWorkersModel = new PropertyAssignWorkersModel
        {
            Assignments =
            [
                new()
                {
                    PropertyId = properties[0].Id,
                    IsChecked = true
                }
            ],
            TaskManagementEnabled = true,
            TimeRegistrationEnabled = false,
            SiteId = sites[2].Id
        };

        await BackendConfigurationAssignmentWorkerServiceHelper.Create(propertyAssignWorkersModel, core, userService,
            BackendConfigurationPnDbContext!, CaseTemplatePnDbContext!, null);

        var workOrders = await BackendConfigurationPnDbContext!.WorkorderCases.AsNoTracking().ToListAsync();
        Assert.That(workOrders.Count, Is.EqualTo(1)); // TODO: fix this

        var propertyAssignWorkersModel2 = new PropertyAssignWorkersModel
        {
            Assignments =
            [
                new()
                {
                    PropertyId = properties[0].Id,
                    IsChecked = false
                },

                new()
                {
                    PropertyId = properties[1].Id,
                    IsChecked = true
                }
            ],
            TaskManagementEnabled = true,
            TimeRegistrationEnabled = false,
            SiteId = sites[2].Id
        };

        // Act
        // var userService = Substitute.For<IUserService>();
        // userService.UserId.Returns(1);
        var result2 = await BackendConfigurationAssignmentWorkerServiceHelper.Update(propertyAssignWorkersModel2, core,
            userService,
            BackendConfigurationPnDbContext!, CaseTemplatePnDbContext!, null, ItemsPlanningPnDbContext!);

        // Assert
        var workers = await MicrotingDbContext.Workers.AsNoTracking().ToListAsync();
        var siteWorkers = await MicrotingDbContext.SiteWorkers.AsNoTracking().ToListAsync();
        var units = await MicrotingDbContext.Units.AsNoTracking().ToListAsync();
        var timeregistrationSiteAssignments =
            await TimePlanningPnDbContext!.AssignedSites.AsNoTracking().ToListAsync();
        var propertyWorkers = await BackendConfigurationPnDbContext!.PropertyWorkers.AsNoTracking().ToListAsync();
        workOrders = await BackendConfigurationPnDbContext!.WorkorderCases.AsNoTracking().ToListAsync();
        var sdkCases = await MicrotingDbContext!.Cases.AsNoTracking().ToListAsync();
        var checkListSites = await MicrotingDbContext!.CheckListSites.AsNoTracking().ToListAsync();
        var entityItems = await MicrotingDbContext!.EntityItems.AsNoTracking().ToListAsync();

        Assert.That(result2, Is.Not.Null);
        Assert.That(result2.Success, Is.True);
        Assert.That(sites.Count, Is.EqualTo(3));
        Assert.That(workers.Count, Is.EqualTo(3));
        Assert.That(units.Count, Is.EqualTo(3));

        // Assert site
        Assert.That(sites[2].Name, Is.EqualTo(deviceUserModel.UserFirstName + " " + deviceUserModel.UserLastName));

        // Assert worker
        Assert.That(workers[2].FirstName, Is.EqualTo(deviceUserModel.UserFirstName));

        // Assert siteWorker
        Assert.That(siteWorkers[2].WorkerId, Is.EqualTo(workers[2].Id));

        // Assert unit
        Assert.That(units[2].SiteId, Is.EqualTo(sites[2].Id));

        // Assert timeregistrationSiteAssignments
        Assert.That(timeregistrationSiteAssignments.Count, Is.EqualTo(30));

        // Assert propertyWorkers
        Assert.That(propertyWorkers.Count, Is.EqualTo(2));
        Assert.That(propertyWorkers[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
        Assert.That(propertyWorkers[0].PropertyId, Is.EqualTo(properties[0].Id));
        Assert.That(propertyWorkers[0].WorkerId, Is.EqualTo(workers[2].Id));
        Assert.That(propertyWorkers[1].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
        Assert.That(propertyWorkers[1].PropertyId, Is.EqualTo(properties[1].Id));
        Assert.That(propertyWorkers[1].WorkerId, Is.EqualTo(workers[2].Id));

        // Assert workOrders
        Assert.That(workOrders.Count, Is.EqualTo(2)); // TODO: fix this
        Assert.That(workOrders[0].PropertyWorkerId, Is.EqualTo(propertyWorkers[0].Id));
        Assert.That(workOrders[0].LeadingCase, Is.EqualTo(false));
        Assert.That(workOrders[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
        Assert.That(workOrders[1].PropertyWorkerId, Is.EqualTo(propertyWorkers[1].Id));
        Assert.That(workOrders[1].LeadingCase, Is.EqualTo(false));
        Assert.That(workOrders[1].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));

        // Assert sdkCases
        Assert.That(sdkCases.Count, Is.EqualTo(0));

        // Assert checkListSites
        Assert.That(checkListSites.Count, Is.EqualTo(2));
        Assert.That(checkListSites[0].SiteId, Is.EqualTo(sites[2].Id));
        Assert.That(checkListSites[0].MicrotingUid, Is.EqualTo(workOrders[0].CaseId));
        Assert.That(checkListSites[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
        Assert.That(checkListSites[1].SiteId, Is.EqualTo(sites[2].Id));
        Assert.That(checkListSites[1].MicrotingUid, Is.EqualTo(workOrders[1].CaseId));
        Assert.That(checkListSites[1].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));

        // Assert entityItems
        Assert.That(entityItems.Count, Is.EqualTo(8));
        Assert.That(entityItems[7].Name, Is.EqualTo(sites[2].Name));
        Assert.That(entityItems[7].EntityGroupId, Is.EqualTo(properties[1].EntitySelectListDeviceUsers));
    }
}

public class EFormCoreService : IEFormCoreService
{

    private readonly string _connectionString;
    public EFormCoreService(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<Core> GetCore()
    {
        var core = new Core();
        await core.StartSqlOnly(_connectionString);
        return core;
    }

    public void LogEvent(string appendText)
    {
        Console.WriteLine(appendText);
    }

    public void LogException(string appendText)
    {
        Console.WriteLine(appendText);
    }
}

public class BackendConfigurationLocalizationService : IBackendConfigurationLocalizationService
{
    public string GetString(string key)
    {
        return key;
    }

    public string GetString(string format, params object[] args)
    {
        return format;
    }

    public string GetStringWithFormat(string format, params object[] args)
    {
        return format;
    }
}