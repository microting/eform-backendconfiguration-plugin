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
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
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

    // ---------------------------------------------------------------------------------------------
    // The effective-date carry-over in UpdateDeviceUser's create branch, one test per outcome.
    //
    // Re-enabling time registration mints a NEW AssignedSite hardcoded to one-minute mode. Its own
    // version trail starts at true, so OneMinuteModeTimeline reads one-minute mode all the way back
    // UNLESS UseOneMinuteIntervalsFrom says otherwise. Every branch therefore decides how the site's
    // pre-existing registrations are paid, and each test below asserts both the stored value and the
    // timeline verdict on dates either side of the expected boundary — the timeline is what payroll
    // reads. The 5-minute and the recovered-transition outcomes are the two tests above.
    // ---------------------------------------------------------------------------------------------

    // Branch: no earlier AssignedSite at all — genuinely the site's first time registration setup.
    [Test]
    public async Task
        BackendConfigurationAssignmentWorkerServiceHelper_UpdateDeviceUser_TimeRegistrationEnabledOnSiteWithNoEarlierAssignment_LeavesEffectiveDateNull()
    {
        // Arrange
        var worker = await ArrangeWorkerWithoutTimeRegistration();
        Assert.That(await TimePlanningPnDbContext!.AssignedSites.AnyAsync(x => x.SiteId == worker.SiteMicrotingUid),
            Is.False, "Precondition: the site must never have had an AssignedSite, removed or not.");

        // Act
        var result = await SetTimeRegistration(worker, true);

        // Assert
        Assert.That(result.Success, Is.True, result.Message);

        var assignmentsForSite = await AssignmentsForSite(worker.SiteMicrotingUid);
        Assert.That(assignmentsForSite.Count, Is.EqualTo(1));
        var created = assignmentsForSite[0];
        Assert.That(created.UseOneMinuteIntervals, Is.True);
        Assert.That(created.UseOneMinuteIntervalsFrom, Is.Null,
            "No earlier history exists to protect, so nothing may be stamped.");

        // WRONG outcomes this pins: deleting the null-check branch dereferences the missing row, the
        // create branch swallows the exception and the enable silently fails; treating "no earlier row"
        // like a 5-minute site stamps now, which would push everything registered before today into
        // 5-minute mode on a site that never had any 5-minute history.
        var timeline = await OneMinuteModeTimeline.BuildAsync(TimePlanningPnDbContext!, created);
        Assert.That(timeline.WasOneMinuteAt(DateTime.UtcNow.AddYears(-5)), Is.True,
            "A first setup is one-minute mode from the beginning of time.");
        Assert.That(timeline.WasOneMinuteAt(DateTime.UtcNow.AddDays(-1)), Is.True,
            "Yesterday must not read as 5-minute mode — nothing was ever registered under it.");
        Assert.That(timeline.WasOneMinuteAt(DateTime.UtcNow), Is.True);
    }

    // Branch: the earlier row was already one-minute WITH a recorded effective date — carried unchanged.
    [Test]
    public async Task
        BackendConfigurationAssignmentWorkerServiceHelper_UpdateDeviceUser_TimeRegistrationReEnabledOnStampedOneMinuteSite_CarriesStampUnchanged()
    {
        // Arrange
        var worker = await ArrangeWorkerWithoutTimeRegistration();

        // Backdated 30 days so a carried stamp is distinguishable from a fresh "now" stamp.
        var recordedFrom = DateTime.UtcNow.AddDays(-30);
        // Created true, so version row #0 is already true: were the stamp branch deleted, the
        // version-trail branch would see "earliest row already true" and carry NULL instead.
        var legacyAssignment = await CreateLegacyAssignment(worker, useOneMinuteIntervals: true,
            useOneMinuteIntervalsFrom: recordedFrom);
        // Read back what the database actually holds (column precision), which is what gets carried.
        var storedFrom = (await TimePlanningPnDbContext!.AssignedSites.AsNoTracking()
            .FirstAsync(x => x.Id == legacyAssignment.Id)).UseOneMinuteIntervalsFrom;
        Assert.That(storedFrom, Is.Not.Null, "Precondition: the earlier row must carry a stamp.");

        // Act — disable, then re-enable
        var disableResult = await SetTimeRegistration(worker, false);
        Assert.That(disableResult.Success, Is.True, disableResult.Message);
        var result = await SetTimeRegistration(worker, true);

        // Assert
        Assert.That(result.Success, Is.True, result.Message);

        var assignmentsForSite = await AssignmentsForSite(worker.SiteMicrotingUid);
        Assert.That(assignmentsForSite.Count, Is.EqualTo(2));
        Assert.That(assignmentsForSite[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));

        var reEnabled = assignmentsForSite[1];
        Assert.That(reEnabled.WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Removed));
        Assert.That(reEnabled.UseOneMinuteIntervals, Is.True);
        Assert.That(reEnabled.UseOneMinuteIntervalsFrom, Is.EqualTo(storedFrom),
            "The recorded effective date must be carried across exactly, not re-derived or re-stamped.");

        // WRONG outcomes this pins: carrying NULL (stamp branch deleted, or the version trail consulted
        // first) makes one-minute mode hold from the beginning of time and reinterprets the 5-minute
        // history before the recorded date; re-stamping now pushes the 30 one-minute days since the
        // recorded date back into 5-minute mode.
        var timeline = await OneMinuteModeTimeline.BuildAsync(TimePlanningPnDbContext!, reEnabled);
        Assert.That(timeline.WasOneMinuteAt(recordedFrom.AddDays(-1)), Is.False,
            "Registrations from before the recorded date must still resolve as 5-minute mode.");
        Assert.That(timeline.WasOneMinuteAt(recordedFrom), Is.True,
            "The recorded date itself is the first one-minute day.");
        Assert.That(timeline.WasOneMinuteAt(recordedFrom.AddDays(1)), Is.True,
            "Registrations after the recorded date — but before the re-enable — stay one-minute mode.");
    }

    // Branch: one-minute, no stamp, and NO version rows — nothing to recover, so NULL is carried.
    [Test]
    public async Task
        BackendConfigurationAssignmentWorkerServiceHelper_UpdateDeviceUser_TimeRegistrationReEnabledOnUnstampedOneMinuteSiteWithoutVersions_LeavesEffectiveDateNull()
    {
        // Arrange
        var worker = await ArrangeWorkerWithoutTimeRegistration();

        var legacyAssignment = await CreateLegacyAssignment(worker, useOneMinuteIntervals: true);

        // A row older than AssignedSiteVersions itself: no audit trail at all. Drop the version row
        // Create just wrote, and soft-delete the row outside PnBase so no removal row exists either.
        // (Through the app's disable path the removal row is written but filtered out of the trail,
        // which lands on this same branch.)
        TimePlanningPnDbContext!.AssignedSiteVersions.RemoveRange(
            await TimePlanningPnDbContext.AssignedSiteVersions
                .Where(x => x.AssignedSiteId == legacyAssignment.Id)
                .ToListAsync());
        legacyAssignment.WorkflowState = Constants.WorkflowStates.Removed;
        await TimePlanningPnDbContext.SaveChangesAsync();
        Assert.That(await TimePlanningPnDbContext.AssignedSiteVersions
                .AnyAsync(x => x.AssignedSiteId == legacyAssignment.Id),
            Is.False, "Precondition: the earlier row must have no version rows.");

        // Act
        var result = await SetTimeRegistration(worker, true);

        // Assert
        Assert.That(result.Success, Is.True, result.Message);

        var assignmentsForSite = await AssignmentsForSite(worker.SiteMicrotingUid);
        Assert.That(assignmentsForSite.Count, Is.EqualTo(2));
        Assert.That(assignmentsForSite[0].Id, Is.EqualTo(legacyAssignment.Id));

        var reEnabled = assignmentsForSite[1];
        Assert.That(reEnabled.WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Removed));
        Assert.That(reEnabled.UseOneMinuteIntervals, Is.True);
        Assert.That(reEnabled.UseOneMinuteIntervalsFrom, Is.Null,
            "An empty trail records no transition, so there is no date to carry.");

        // WRONG outcomes this pins: without the Count == 0 guard the branch indexes an empty list, the
        // create branch swallows the exception and the enable silently fails; stamping now instead
        // would move the site's whole one-minute history — which the old row's timeline read as
        // one-minute throughout (no version rows means "current flag for all dates") — into 5-minute.
        var timeline = await OneMinuteModeTimeline.BuildAsync(TimePlanningPnDbContext!, reEnabled);
        Assert.That(timeline.WasOneMinuteAt(DateTime.UtcNow.AddYears(-5)), Is.True,
            "The site read as one-minute mode for all dates before the re-enable, and must still.");
        Assert.That(timeline.WasOneMinuteAt(DateTime.UtcNow.AddDays(-1)), Is.True);
        Assert.That(timeline.WasOneMinuteAt(DateTime.UtcNow), Is.True);
    }

    // Branch: one-minute, no stamp, and the EARLIEST version row is already true — the site was
    // one-minute from its creation, so there is no transition and NULL is carried.
    [Test]
    public async Task
        BackendConfigurationAssignmentWorkerServiceHelper_UpdateDeviceUser_TimeRegistrationReEnabledOnSiteOneMinuteSinceCreation_LeavesEffectiveDateNull()
    {
        // Arrange
        var worker = await ArrangeWorkerWithoutTimeRegistration();

        // Created true on purpose: PnBase.Create writes version row #0, so the trail starts at true.
        var legacyAssignment = await CreateLegacyAssignment(worker, useOneMinuteIntervals: true);

        // Backdate version row #0 so that mistaking it for a false→true transition produces a date
        // 30 days back — visible on the timeline — rather than one indistinguishable from "now".
        var firstVersion = await TimePlanningPnDbContext!.AssignedSiteVersions
            .Where(x => x.AssignedSiteId == legacyAssignment.Id)
            .OrderBy(x => x.Id)
            .FirstAsync();
        Assert.That(firstVersion.UseOneMinuteIntervals, Is.True, "Precondition: version row #0 must be true.");
        var createdAt = DateTime.UtcNow.AddDays(-30);
        firstVersion.UpdatedAt = createdAt;
        await TimePlanningPnDbContext.SaveChangesAsync();

        // Act — disable, then re-enable
        var disableResult = await SetTimeRegistration(worker, false);
        Assert.That(disableResult.Success, Is.True, disableResult.Message);
        var result = await SetTimeRegistration(worker, true);

        // Assert
        Assert.That(result.Success, Is.True, result.Message);

        var assignmentsForSite = await AssignmentsForSite(worker.SiteMicrotingUid);
        Assert.That(assignmentsForSite.Count, Is.EqualTo(2));
        Assert.That(assignmentsForSite[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));

        var reEnabled = assignmentsForSite[1];
        Assert.That(reEnabled.WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Removed));
        Assert.That(reEnabled.UseOneMinuteIntervals, Is.True);
        Assert.That(reEnabled.UseOneMinuteIntervalsFrom, Is.Null,
            "A trail that starts at true records no transition, so there is no date to carry.");

        // WRONG outcome this pins: without the "earliest row already true" clause the branch takes the
        // first true row as the transition and stamps its date — 30 days back — which would push the
        // site's one-minute history before that day into 5-minute mode. The old row's timeline read it
        // as one-minute from the beginning of time (its _initialValue was true).
        var timeline = await OneMinuteModeTimeline.BuildAsync(TimePlanningPnDbContext!, reEnabled);
        Assert.That(timeline.WasOneMinuteAt(createdAt.AddDays(-1)), Is.True,
            "The day before version row #0 must still resolve as one-minute mode.");
        Assert.That(timeline.WasOneMinuteAt(DateTime.UtcNow.AddYears(-5)), Is.True,
            "One-minute mode holds from the beginning of time.");
        Assert.That(timeline.WasOneMinuteAt(DateTime.UtcNow), Is.True);
    }

    // Branch: one-minute, no stamp, and the trail NEVER records true — the flag was flipped outside the
    // audited path, so the LAST audited save is the earliest possible flip point and is carried.
    [Test]
    public async Task
        BackendConfigurationAssignmentWorkerServiceHelper_UpdateDeviceUser_TimeRegistrationReEnabledOnSiteFlippedOutsideAudit_CarriesLastAuditedSave()
    {
        // Arrange
        var worker = await ArrangeWorkerWithoutTimeRegistration();

        var legacyAssignment = await CreateLegacyAssignment(worker, useOneMinuteIntervals: false);
        // A second audited save, still in 5-minute mode, so the trail has a FIRST and a LAST row with
        // different dates — carrying the first one instead of the last must show on the timeline.
        legacyAssignment.UseOnlyPlanHours = true;
        await legacyAssignment.Update(TimePlanningPnDbContext!);

        var versions = await TimePlanningPnDbContext!.AssignedSiteVersions
            .Where(x => x.AssignedSiteId == legacyAssignment.Id)
            .OrderBy(x => x.Id)
            .ToListAsync();
        Assert.That(versions.Count, Is.EqualTo(2), "Precondition: exactly two audited saves.");
        Assert.That(versions.All(x => !x.UseOneMinuteIntervals), Is.True,
            "Precondition: no audited save may record one-minute mode.");
        var firstAuditedSave = DateTime.UtcNow.AddDays(-60);
        var lastAuditedSave = DateTime.UtcNow.AddDays(-30);
        versions[0].UpdatedAt = firstAuditedSave;
        versions[1].UpdatedAt = lastAuditedSave;
        await TimePlanningPnDbContext.SaveChangesAsync();

        // The un-audited flip, and an un-audited removal: plain SaveChanges writes no version row,
        // standing in for a raw-SQL ops change. The same history disabled through the app — whose
        // removal row DOES record true — is ..._DisabledThroughApp_CarriesLastLiveSave below.
        legacyAssignment.UseOneMinuteIntervals = true;
        legacyAssignment.WorkflowState = Constants.WorkflowStates.Removed;
        await TimePlanningPnDbContext.SaveChangesAsync();
        Assert.That(await TimePlanningPnDbContext.AssignedSiteVersions
                .CountAsync(x => x.AssignedSiteId == legacyAssignment.Id),
            Is.EqualTo(2), "Precondition: the flip and the removal must not have been audited.");

        // Act
        var result = await SetTimeRegistration(worker, true);

        // Assert
        Assert.That(result.Success, Is.True, result.Message);

        var assignmentsForSite = await AssignmentsForSite(worker.SiteMicrotingUid);
        Assert.That(assignmentsForSite.Count, Is.EqualTo(2));
        Assert.That(assignmentsForSite[0].Id, Is.EqualTo(legacyAssignment.Id));

        var reEnabled = assignmentsForSite[1];
        Assert.That(reEnabled.WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Removed));
        Assert.That(reEnabled.UseOneMinuteIntervals, Is.True);
        Assert.That(reEnabled.UseOneMinuteIntervalsFrom, Is.Not.Null);
        Assert.That(reEnabled.UseOneMinuteIntervalsFrom!.Value.Date, Is.EqualTo(lastAuditedSave.Date),
            "The stamp must be the LAST audited save — the earliest possible un-audited flip point.");

        // WRONG outcomes this pins: dropping the "?? previousVersions[^1]" fallback dereferences a null
        // transition and the enable silently fails; taking the FIRST row instead stamps 60 days back
        // and turns 30 audited 5-minute days into one-minute mode; carrying NULL makes one-minute hold
        // from the beginning of time; stamping now turns the 30 days since the last audited save —
        // which OneMinuteModeTimeline's divergence correction read as one-minute — into 5-minute.
        var timeline = await OneMinuteModeTimeline.BuildAsync(TimePlanningPnDbContext!, reEnabled);
        Assert.That(timeline.WasOneMinuteAt(firstAuditedSave.AddDays(-1)), Is.False,
            "Before the first audited save the site was in 5-minute mode.");
        Assert.That(timeline.WasOneMinuteAt(lastAuditedSave.AddDays(-1)), Is.False,
            "Between the two audited saves the site was provably still in 5-minute mode.");
        Assert.That(timeline.WasOneMinuteAt(lastAuditedSave), Is.True,
            "From the last audited save onwards the un-audited flip may already have happened.");
        Assert.That(timeline.WasOneMinuteAt(lastAuditedSave.AddDays(1)), Is.True);
    }

    // The divergence case again, but disabled through the REAL path: UpdateDeviceUser with
    // TimeRegistrationEnabled = false soft-deletes the row via PnBase.Delete, which writes a version row
    // (WorkflowState = removed) copying the CURRENT flag — true. That removal row is the first row in the
    // trail to record true, so unless the carry-over ignores it, the DISABLE date passes for the switch.
    [Test]
    public async Task
        BackendConfigurationAssignmentWorkerServiceHelper_UpdateDeviceUser_TimeRegistrationReEnabledOnSiteFlippedOutsideAudit_DisabledThroughApp_CarriesLastLiveSave()
    {
        // Arrange
        var worker = await ArrangeWorkerWithoutTimeRegistration();

        var legacyAssignment = await CreateLegacyAssignment(worker, useOneMinuteIntervals: false);
        legacyAssignment.UseOnlyPlanHours = true;
        await legacyAssignment.Update(TimePlanningPnDbContext!);

        var liveVersions = await TimePlanningPnDbContext!.AssignedSiteVersions
            .Where(x => x.AssignedSiteId == legacyAssignment.Id)
            .OrderBy(x => x.Id)
            .ToListAsync();
        Assert.That(liveVersions.Count, Is.EqualTo(2), "Precondition: exactly two live audited saves.");
        Assert.That(liveVersions.All(x => !x.UseOneMinuteIntervals), Is.True,
            "Precondition: no live audited save may record one-minute mode.");
        var lastLiveSave = DateTime.UtcNow.AddDays(-30);
        liveVersions[0].UpdatedAt = DateTime.UtcNow.AddDays(-60);
        liveVersions[1].UpdatedAt = lastLiveSave;
        await TimePlanningPnDbContext.SaveChangesAsync();

        // The un-audited flip (raw-SQL ops change stand-in): the row is live and true, its trail says false.
        legacyAssignment.UseOneMinuteIntervals = true;
        await TimePlanningPnDbContext.SaveChangesAsync();

        // Act — disable through the app, then re-enable
        var disableResult = await SetTimeRegistration(worker, false);
        Assert.That(disableResult.Success, Is.True, disableResult.Message);

        // Precondition, asserted: the disable wrote the removal row this test is about, and it records true.
        var removalVersions = await TimePlanningPnDbContext.AssignedSiteVersions.AsNoTracking()
            .Where(x => x.AssignedSiteId == legacyAssignment.Id
                        && x.WorkflowState == Constants.WorkflowStates.Removed)
            .ToListAsync();
        Assert.That(removalVersions.Count, Is.EqualTo(1), "The app's disable must write exactly one removal row.");
        Assert.That(removalVersions[0].UseOneMinuteIntervals, Is.True,
            "The removal row copies the current flag — the true the live trail never recorded.");

        var result = await SetTimeRegistration(worker, true);

        // Assert
        Assert.That(result.Success, Is.True, result.Message);

        var assignmentsForSite = await AssignmentsForSite(worker.SiteMicrotingUid);
        Assert.That(assignmentsForSite.Count, Is.EqualTo(2));
        Assert.That(assignmentsForSite[0].Id, Is.EqualTo(legacyAssignment.Id));
        Assert.That(assignmentsForSite[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));

        var reEnabled = assignmentsForSite[1];
        Assert.That(reEnabled.WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Removed));
        Assert.That(reEnabled.UseOneMinuteIntervals, Is.True);
        Assert.That(reEnabled.UseOneMinuteIntervalsFrom, Is.Not.Null);
        Assert.That(reEnabled.UseOneMinuteIntervalsFrom!.Value.Date, Is.EqualTo(lastLiveSave.Date),
            "The stamp must be the last LIVE audited save, not the disable date the removal row carries.");

        // WRONG outcome this pins: counting the removal row as the transition stamps the disable date
        // (today), which turns the 30 days between the last live save and the disable — one-minute
        // mode by the old row's own divergence correction before it was disabled — into 5-minute.
        var timeline = await OneMinuteModeTimeline.BuildAsync(TimePlanningPnDbContext!, reEnabled);
        Assert.That(timeline.WasOneMinuteAt(lastLiveSave.AddDays(-1)), Is.False,
            "Before the last live save the site was provably in 5-minute mode.");
        Assert.That(timeline.WasOneMinuteAt(lastLiveSave.AddDays(15)), Is.True,
            "A registration between the last live save and the disable resolves as one-minute mode.");
        Assert.That(timeline.WasOneMinuteAt(DateTime.UtcNow), Is.True);
    }

    /// <summary>
    /// A worker created WITHOUT time registration — so CreateDeviceUser mints no AssignedSite — plus
    /// everything <see cref="SetTimeRegistration"/> needs to drive UpdateDeviceUser for it. The tests
    /// above then hand-build whatever earlier AssignedSite history their branch needs.
    /// </summary>
    private async Task<ArrangedWorker> ArrangeWorkerWithoutTimeRegistration()
    {
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

        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);

        var createResult = await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(new DeviceUserModel
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
        }, core, 1, TimePlanningPnDbContext!, BaseDbContext!, userService, userManager);
        Assert.That(createResult.Success, Is.True, createResult.Message);

        var currentSite = await MicrotingDbContext!.Sites.OrderByDescending(x => x.Id).FirstAsync();

        return new ArrangedWorker(core, userService, userManager, Substitute.For<ILogger>(),
            (int)currentSite.MicrotingUid!, Guid.NewGuid().ToString(), Guid.NewGuid().ToString(),
            $"{Guid.NewGuid()}@test.com");
    }

    /// <summary>
    /// Hand-builds an earlier AssignedSite for <paramref name="worker"/>'s site. PnBase.Create writes its
    /// version row #0, so the trail starts at <paramref name="useOneMinuteIntervals"/>.
    /// </summary>
    private async Task<AssignedSite> CreateLegacyAssignment(ArrangedWorker worker, bool useOneMinuteIntervals,
        DateTime? useOneMinuteIntervalsFrom = null)
    {
        var legacyAssignment = new AssignedSite
        {
            SiteId = worker.SiteMicrotingUid,
            CreatedByUserId = 1,
            UpdatedByUserId = 1,
            UseOneMinuteIntervals = useOneMinuteIntervals,
            UseOneMinuteIntervalsFrom = useOneMinuteIntervalsFrom
        };
        await legacyAssignment.Create(TimePlanningPnDbContext!);
        return legacyAssignment;
    }

    /// <summary>Switches time registration on or off for <paramref name="worker"/> through UpdateDeviceUser.</summary>
    private async Task<OperationResult> SetTimeRegistration(ArrangedWorker worker, bool timeRegistrationEnabled)
    {
        return await BackendConfigurationAssignmentWorkerServiceHelper.UpdateDeviceUser(new DeviceUserModel
            {
                SiteMicrotingUid = worker.SiteMicrotingUid,
                CustomerNo = 0,
                HasWorkOrdersAssigned = false,
                IsBackendUser = false,
                IsLocked = false,
                LanguageCode = "da",
                TimeRegistrationEnabled = timeRegistrationEnabled,
                UserFirstName = worker.FirstName,
                UserLastName = worker.LastName,
                WorkerEmail = worker.WorkerEmail
            }, worker.Core, 1, worker.UserService, worker.UserManager, BackendConfigurationPnDbContext!,
            TimePlanningPnDbContext!, BaseDbContext!, worker.Logger, ItemsPlanningPnDbContext!);
    }

    /// <summary>Every AssignedSite ever minted for the site, removed ones included, oldest first.</summary>
    private async Task<List<AssignedSite>> AssignmentsForSite(int siteMicrotingUid)
    {
        return await TimePlanningPnDbContext!.AssignedSites.AsNoTracking()
            .Where(x => x.SiteId == siteMicrotingUid)
            .OrderBy(x => x.Id)
            .ToListAsync();
    }

    private sealed record ArrangedWorker(
        Core Core,
        IUserService UserService,
        UserManager<EformUser> UserManager,
        ILogger Logger,
        int SiteMicrotingUid,
        string FirstName,
        string LastName,
        string WorkerEmail);

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