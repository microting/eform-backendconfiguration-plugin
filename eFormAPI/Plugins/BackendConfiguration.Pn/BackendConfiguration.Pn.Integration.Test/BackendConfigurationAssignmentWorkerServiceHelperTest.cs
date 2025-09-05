using BackendConfiguration.Pn.Infrastructure.Helpers;
using BackendConfiguration.Pn.Infrastructure.Models;
using BackendConfiguration.Pn.Infrastructure.Models.AssignmentWorker;
using BackendConfiguration.Pn.Infrastructure.Models.Properties;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using eFormCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;
using NSubstitute;

namespace BackendConfiguration.Pn.Integration.Test;

[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class BackendConfigurationAssignmentWorkerServiceHelperTest : TestBaseSetup
{
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
            UserLastName = Guid.NewGuid().ToString()
        };

        // Act
        var result = await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            TimePlanningPnDbContext);

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
            UserLastName = Guid.NewGuid().ToString()
        };

        // Act
        var result = await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            TimePlanningPnDbContext);

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
        Assert.That(timeregistrationSiteAssignments.Count, Is.EqualTo(1));
        Assert.That(timeregistrationSiteAssignments[0].SiteId, Is.EqualTo(sites[2].MicrotingUid));
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
            BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, 1,1);

        var deviceUserModel = new DeviceUserModel
        {
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = false,
            UserFirstName = Guid.NewGuid().ToString(),
            UserLastName = Guid.NewGuid().ToString()
        };

        await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            TimePlanningPnDbContext);

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
            UserLastName = Guid.NewGuid().ToString()
        };

        // Act
        var result = await BackendConfigurationAssignmentWorkerServiceHelper.UpdateDeviceUser(newDeviceUserModel, core, 1,
            BackendConfigurationPnDbContext,
            TimePlanningPnDbContext, logger);

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
        Assert.That(timeregistrationSiteAssignments.Count, Is.EqualTo(0));
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
            BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, 1, 1);

        var deviceUserModel = new DeviceUserModel
        {
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = false,
            UserFirstName = Guid.NewGuid().ToString(),
            UserLastName = Guid.NewGuid().ToString()
        };

        await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            TimePlanningPnDbContext);

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
            UserLastName = Guid.NewGuid().ToString()
        };

        // Act
        var result = await BackendConfigurationAssignmentWorkerServiceHelper.UpdateDeviceUser(newDeviceUserModel, core,
            1,
            BackendConfigurationPnDbContext,
            TimePlanningPnDbContext, logger);

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
        Assert.That(timeregistrationSiteAssignments.Count, Is.EqualTo(1));
        Assert.That(timeregistrationSiteAssignments[0].SiteId, Is.EqualTo(sites[2].MicrotingUid));
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
            BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, 1, 1);

        var deviceUserModel = new DeviceUserModel
        {
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = true,
            UserFirstName = Guid.NewGuid().ToString(),
            UserLastName = Guid.NewGuid().ToString()
        };

        await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            TimePlanningPnDbContext);

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
            UserLastName = Guid.NewGuid().ToString()
        };

        // Act
        var result = await BackendConfigurationAssignmentWorkerServiceHelper.UpdateDeviceUser(newDeviceUserModel, core,
            1,
            BackendConfigurationPnDbContext,
            TimePlanningPnDbContext, logger);

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
        Assert.That(timeregistrationSiteAssignments.Count, Is.EqualTo(1));
        Assert.That(timeregistrationSiteAssignments[0].SiteId, Is.EqualTo(sites[2].MicrotingUid));
        Assert.That(timeregistrationSiteAssignments[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));

        // Assert propertyWorkers
        Assert.That(propertyWorkers.Count, Is.EqualTo(0));
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
            BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, 1, 1);

        var deviceUserModel = new DeviceUserModel
        {
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = false,
            UserFirstName = Guid.NewGuid().ToString(),
            UserLastName = Guid.NewGuid().ToString()
        };

        /*var result = */await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            TimePlanningPnDbContext);

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

        // Act
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        var result2 = await BackendConfigurationAssignmentWorkerServiceHelper.Create(propertyAssignWorkersModel, core, userService,
             BackendConfigurationPnDbContext, CaseTemplatePnDbContext, null, Bus);

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
        Assert.That(timeregistrationSiteAssignments.Count, Is.EqualTo(0));

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
            BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, 1, 1);

        var deviceUserModel = new DeviceUserModel
        {
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = false,
            UserFirstName = Guid.NewGuid().ToString(),
            UserLastName = Guid.NewGuid().ToString()
        };

        /*var result = */await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            TimePlanningPnDbContext);

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

        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        await BackendConfigurationAssignmentWorkerServiceHelper.Create(propertyAssignWorkersModel, core, userService,
            BackendConfigurationPnDbContext, CaseTemplatePnDbContext, null, Bus);

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
            BackendConfigurationPnDbContext, CaseTemplatePnDbContext, null, Bus, ItemsPlanningPnDbContext);

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
        Assert.That(timeregistrationSiteAssignments.Count, Is.EqualTo(0));

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
            BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, 1, 1);

        var deviceUserModel = new DeviceUserModel
        {
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = false,
            UserFirstName = Guid.NewGuid().ToString(),
            UserLastName = Guid.NewGuid().ToString()
        };

        await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            TimePlanningPnDbContext);

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

        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        await BackendConfigurationAssignmentWorkerServiceHelper.Create(propertyAssignWorkersModel, core, userService,
            BackendConfigurationPnDbContext, CaseTemplatePnDbContext, null, Bus);

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
            BackendConfigurationPnDbContext, CaseTemplatePnDbContext, null, Bus, ItemsPlanningPnDbContext);

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
        Assert.That(timeregistrationSiteAssignments.Count, Is.EqualTo(0));

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
            BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, 1, 1);

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
            BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, 2, 2);

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
            TaskManagementEnabled = true
        };

        await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            TimePlanningPnDbContext);

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

        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        await BackendConfigurationAssignmentWorkerServiceHelper.Create(propertyAssignWorkersModel, core, userService,
            BackendConfigurationPnDbContext, CaseTemplatePnDbContext, null, Bus);

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
            BackendConfigurationPnDbContext, CaseTemplatePnDbContext, null, Bus, ItemsPlanningPnDbContext);

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
        Assert.That(timeregistrationSiteAssignments.Count, Is.EqualTo(0));

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