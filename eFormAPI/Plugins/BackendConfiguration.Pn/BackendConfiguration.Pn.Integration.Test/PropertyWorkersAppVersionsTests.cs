using BackendConfiguration.Pn.Infrastructure.Helpers;
using BackendConfiguration.Pn.Infrastructure.Models;
using BackendConfiguration.Pn.Infrastructure.Models.AssignmentWorker;
using BackendConfiguration.Pn.Services.BackendConfigurationAssignmentWorkerService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;
using eFormCore;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using NSubstitute;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// #1335 phase 1 - the Medarbejdere columns Compliance / Ad-hoc / Time / Archive.
/// <para>
/// <c>IndexDeviceUser</c> exposes one <see cref="AppInstallModel"/> per app. The UI
/// derives three states from it: a reported Version is "Ja"; no access is "Nej";
/// access with nothing reported is "not registered". So the two things that must be
/// right here are the access flag and the version, per app and per worker - one
/// worker's data must never leak onto another, which is what the batch loading
/// (one query per source for the whole page) could get wrong.
/// </para>
/// <para>
/// Sources: Compliance = SDK <c>Units.eFormVersion</c>; Time =
/// <c>EformUser.TimeRegistration*</c>; Archive = <c>EformUser.Archive*</c>; Ad-hoc
/// has no source yet. Access: Compliance always; Ad-hoc = TaskManagementEnabled;
/// Time = an AssignedSite exists; Archive = the "Kun arkiv" group.
/// </para>
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class PropertyWorkersAppVersionsTests : TestBaseSetup
{
    [SetUp]
    public Task ReserveEformUserId1() => IdentityTestUtils.ReserveEformUserId1Async(BaseDbContext!);

    private async Task<int> CreateWorker(Core core, string email, bool timeRegistration, bool archive)
    {
        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);
        var userService = IdentityTestUtils.CreateRealUserService(BaseDbContext!, userManager);
        var result = await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(new DeviceUserModel
        {
            CustomerNo = 0,
            LanguageCode = "da",
            TimeRegistrationEnabled = timeRegistration,
            ArchiveEnabled = archive,
            UserFirstName = "Jane",
            UserLastName = $"Doe {Guid.NewGuid():N}",
            WorkerEmail = email
        }, core, 1, TimePlanningPnDbContext!, BaseDbContext!, userService, userManager);
        Assert.That(result.Success, Is.True, result.Message);
        return result.Model;
    }

    private async Task<List<DeviceUserModel>> Index(Core core)
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
            Substitute.For<ICalendarAssignmentReconciliationService>());

        var result = await service.IndexDeviceUser(new PropertyWorkersFiltrationModel { ShowResigned = false });
        Assert.That(result.Success, Is.True, result.Message);
        return result.Model;
    }

    private async Task SetComplianceVersion(int siteId, string version, string model, string osVersion)
    {
        var unit = await MicrotingDbContext!.Units.SingleAsync(x =>
            x.SiteId == siteId && x.WorkflowState != Constants.WorkflowStates.Removed);
        unit.eFormVersion = version;
        unit.Model = model;
        unit.Manufacturer = "iOS";
        unit.OsVersion = osVersion;
        await MicrotingDbContext.SaveChangesAsync();
    }

    private async Task EnableTaskManagement(int siteId)
    {
        var property = new Property
        {
            Name = $"AppVersions-{Guid.NewGuid()}", ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Properties.AddAsync(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        await BackendConfigurationPnDbContext.PropertyWorkers.AddAsync(new PropertyWorker
        {
            PropertyId = property.Id, WorkerId = siteId, TaskManagementEnabled = true,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();
    }

    [Test]
    public async Task IndexDeviceUser_ReportsPerAppAccessAndVersion_PerWorker()
    {
        // Arrange
        var core = await GetCore();

        // Worker A: every app enabled; Compliance and Time have reported, Archive and
        // Ad-hoc have not.
        var emailA = $"a-{Guid.NewGuid():N}@example.com";
        var siteA = await CreateWorker(core, emailA, timeRegistration: true, archive: true);
        await EnableTaskManagement(siteA);
        await SetComplianceVersion(siteA, "3.2.1", "iPhone 15", "18.1");
        var userA = await BaseDbContext!.Users.SingleAsync(x => x.Email == emailA);
        userA.TimeRegistrationSoftwareVersion = "4.0.36";
        userA.TimeRegistrationModel = "Pixel 8";
        userA.TimeRegistrationManufacturer = "Google";
        userA.TimeRegistrationOsVersion = "15";
        await BaseDbContext.SaveChangesAsync();

        // Worker B: no app enabled - but its Archive app has reported a version (it
        // had access before), and nothing has reported for Compliance.
        var emailB = $"b-{Guid.NewGuid():N}@example.com";
        var siteB = await CreateWorker(core, emailB, timeRegistration: false, archive: false);
        var userB = await BaseDbContext.Users.SingleAsync(x => x.Email == emailB);
        userB.ArchiveSoftwareVersion = "1.4.0";
        userB.ArchiveModel = "Galaxy S24";
        userB.ArchiveManufacturer = "Samsung";
        userB.ArchiveOsVersion = "14";
        await BaseDbContext.SaveChangesAsync();

        // Act
        var workers = await Index(core);

        // Assert
        var a = workers.Single(x => x.SiteId == siteA);
        var b = workers.Single(x => x.SiteId == siteB);

        Assert.Multiple(() =>
        {
            // Compliance: always accessible; version + device from the SDK Unit.
            Assert.That(a.ComplianceApp.HasAccess, Is.True);
            Assert.That(a.ComplianceApp.Version, Is.EqualTo("3.2.1"));
            Assert.That(a.ComplianceApp.Model, Is.EqualTo("iPhone 15"));
            Assert.That(a.ComplianceApp.Manufacturer, Is.EqualTo("iOS"));
            Assert.That(a.ComplianceApp.OsVersion, Is.EqualTo("18.1"));
            Assert.That(b.ComplianceApp.HasAccess, Is.True,
                "every device user has the Compliance app - missing data must not read as no access");
            Assert.That(b.ComplianceApp.Version, Is.Null,
                "an empty Unit.eFormVersion is 'not reported', not an empty version");

            // Ad-hoc: access from TaskManagementEnabled; no version source yet.
            Assert.That(a.AdHocApp.HasAccess, Is.True);
            Assert.That(a.AdHocApp.Version, Is.Null);
            Assert.That(b.AdHocApp.HasAccess, Is.False);

            // Time: access from the AssignedSite; version from the EformUser.
            Assert.That(a.TimeApp.HasAccess, Is.True);
            Assert.That(a.TimeApp.Version, Is.EqualTo("4.0.36"));
            Assert.That(a.TimeApp.Model, Is.EqualTo("Pixel 8"));
            Assert.That(a.TimeApp.Manufacturer, Is.EqualTo("Google"));
            Assert.That(a.TimeApp.OsVersion, Is.EqualTo("15"));
            Assert.That(b.TimeApp.HasAccess, Is.False);
            Assert.That(b.TimeApp.Version, Is.Null, "A's Time version must not leak onto B");

            // Archive: access from "Kun arkiv"; version from the EformUser.
            Assert.That(a.ArchiveApp.HasAccess, Is.True);
            Assert.That(a.ArchiveApp.Version, Is.Null, "B's Archive version must not leak onto A");
            Assert.That(b.ArchiveApp.HasAccess, Is.False);
            Assert.That(b.ArchiveApp.Version, Is.EqualTo("1.4.0"),
                "a reported version is shown even after access was taken away");
            Assert.That(b.ArchiveApp.Model, Is.EqualTo("Galaxy S24"));
        });
    }

    [Test]
    public async Task IndexDeviceUser_MatchesTheLoginCaseInsensitively()
    {
        // Arrange - the Worker's email and the login's differ in case only, as they
        // can on installations where one was typed by hand. MySQL's collation treats
        // them as equal, and so must the in-memory match after the batch load.
        var core = await GetCore();
        var email = $"c-{Guid.NewGuid():N}@example.com";
        var site = await CreateWorker(core, email, timeRegistration: true, archive: false);
        var user = await BaseDbContext!.Users.SingleAsync(x => x.Email == email);
        // Both, so neither lookup (UserName, then Email) can match on exact case.
        user.UserName = email.ToUpperInvariant();
        user.Email = email.ToUpperInvariant();
        user.TimeRegistrationSoftwareVersion = "4.1.0";
        await BaseDbContext.SaveChangesAsync();

        // Act
        var workers = await Index(core);

        // Assert
        var worker = workers.Single(x => x.SiteId == site);
        Assert.That(worker.TimeApp.Version, Is.EqualTo("4.1.0"));
    }
}
