using BackendConfiguration.Pn.Infrastructure.Helpers;
using BackendConfiguration.Pn.Infrastructure.Models;
using BackendConfiguration.Pn.Infrastructure.Models.AssignmentWorker;
using BackendConfiguration.Pn.Services.BackendConfigurationAssignmentWorkerService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;
using eFormCore;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Communication;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using NSubstitute;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// #1335 phase 1 - the Medarbejdere columns Compliance / Ad-hoc / Time / Archive /
/// Chemistry / Halebid, and the "Skal opdateres" flag against the baked-in latest versions.
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
/// Time = an AssignedSite exists; Archive = the "Kun arkiv" group; Chemistry = a live
/// View/Admin permission on a property the worker works on; Halebid = works on a
/// property with tail-bite enabled. Chemistry and Halebid have no version source yet.
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

    private async Task<int> LinkToNewProperty(int siteId)
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
            PropertyId = property.Id, WorkerId = siteId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        return property.Id;
    }

    private async Task AddChemicalPermission(int propertyId, int siteId, bool view, bool admin)
    {
        await BackendConfigurationPnDbContext!.ChemicalWorkerPermissions.AddAsync(new ChemicalWorkerPermission
        {
            PropertyId = propertyId, WorkerId = siteId, View = view, Admin = admin, Register = true,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();
    }

    private async Task SetTailBite(int propertyId, bool enabled)
    {
        await BackendConfigurationPnDbContext!.TailBiteProperties.AddAsync(new TailBiteProperty
        {
            PropertyId = propertyId, Enabled = enabled,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();
    }

    [Test]
    public async Task IndexDeviceUser_ChemistryAndHalebidAccess_PerWorker()
    {
        // Arrange
        var core = await GetCore();

        // Worker A: may view chemicals on its property, which has tail-bite enabled.
        var siteA = await CreateWorker(core, $"a-{Guid.NewGuid():N}@example.com", timeRegistration: false, archive: false);
        var propertyA = await LinkToNewProperty(siteA);
        await AddChemicalPermission(propertyA, siteA, view: true, admin: false);
        await SetTailBite(propertyA, enabled: true);

        // Worker B: a chemical permission without View or Admin, on a property whose
        // tail-bite is switched off.
        var siteB = await CreateWorker(core, $"b-{Guid.NewGuid():N}@example.com", timeRegistration: false, archive: false);
        var propertyB = await LinkToNewProperty(siteB);
        await AddChemicalPermission(propertyB, siteB, view: false, admin: false);
        await SetTailBite(propertyB, enabled: false);

        // Worker C: Admin alone grants Chemistry.
        var siteC = await CreateWorker(core, $"c-{Guid.NewGuid():N}@example.com", timeRegistration: false, archive: false);
        var propertyC = await LinkToNewProperty(siteC);
        await AddChemicalPermission(propertyC, siteC, view: false, admin: true);

        // Worker D: had View and tail-bite on a property it no longer works on.
        var siteD = await CreateWorker(core, $"d-{Guid.NewGuid():N}@example.com", timeRegistration: false, archive: false);
        var propertyD = await LinkToNewProperty(siteD);
        await AddChemicalPermission(propertyD, siteD, view: true, admin: false);
        await SetTailBite(propertyD, enabled: true);
        await BackendConfigurationPnDbContext!.PropertyWorkers
            .Where(x => x.PropertyId == propertyD && x.WorkerId == siteD)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.WorkflowState, Constants.WorkflowStates.Removed));

        // Act
        var workers = await Index(core);

        // Assert
        var a = workers.Single(x => x.SiteId == siteA);
        var b = workers.Single(x => x.SiteId == siteB);
        var c = workers.Single(x => x.SiteId == siteC);
        var d = workers.Single(x => x.SiteId == siteD);
        Assert.Multiple(() =>
        {
            Assert.That(a.ChemistryApp.HasAccess, Is.True);
            Assert.That(a.HalebidApp.HasAccess, Is.True);
            Assert.That(a.ChemistryApp.Version, Is.Null, "no version source yet");
            Assert.That(b.ChemistryApp.HasAccess, Is.False, "a permission without View or Admin is no access");
            Assert.That(b.HalebidApp.HasAccess, Is.False, "tail-bite is disabled on B's property");
            Assert.That(c.ChemistryApp.HasAccess, Is.True, "Admin alone grants access");
            Assert.That(d.ChemistryApp.HasAccess, Is.False, "the permission is on a property D no longer works on");
            Assert.That(d.HalebidApp.HasAccess, Is.False, "D no longer works on the tail-bite property");
        });
    }

    [Test]
    public async Task IndexDeviceUser_FlagsAnOutdatedReportedVersion()
    {
        // Arrange - Compliance reports an old version, Time the latest one.
        var core = await GetCore();
        var email = $"d-{Guid.NewGuid():N}@example.com";
        var site = await CreateWorker(core, email, timeRegistration: true, archive: false);
        await SetComplianceVersion(site, "3.2.1", "iPhone 15", "18.1");
        var user = await BaseDbContext!.Users.SingleAsync(x => x.Email == email);
        user.TimeRegistrationSoftwareVersion = AppLatestVersions.For(AppLatestVersions.Time);
        await BaseDbContext.SaveChangesAsync();

        // Act
        var worker = (await Index(core)).Single(x => x.SiteId == site);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(worker.ComplianceApp.LatestVersion, Is.EqualTo(AppLatestVersions.For(AppLatestVersions.Compliance)));
            Assert.That(worker.ComplianceApp.NeedsUpdate, Is.True, "3.2.1 is older than the latest Compliance");
            Assert.That(worker.TimeApp.NeedsUpdate, Is.False, "the latest version needs no update");
            Assert.That(worker.ArchiveApp.NeedsUpdate, Is.False, "nothing reported is never flagged");
        });
    }

    [Test]
    public async Task IndexDeviceUser_ShowsTheComplianceVersionMicrotingReports_WithoutACaseEvent()
    {
        // Arrange - the Unit was last written on an older version; the app has since
        // been updated and synced, so Microting reports the latest one.
        var core = await GetCore();
        var email = $"u-{Guid.NewGuid():N}@example.com";
        var site = await CreateWorker(core, email, timeRegistration: false, archive: false);
        await SetComplianceVersion(site, "3.2.1", "iPhone 15", "18.1");
        var unit = await MicrotingDbContext!.Units.AsNoTracking().SingleAsync(x =>
            x.SiteId == site && x.WorkflowState != Constants.WorkflowStates.Removed);
        var latest = AppLatestVersions.For(AppLatestVersions.Compliance);
        HttpFake.ReportedUnits[(int)unit.MicrotingUid!] = new Microting.eForm.Infrastructure.Data.Entities.Unit
        {
            eFormVersion = latest, Model = "iPhone 15", Manufacturer = "iOS", OsVersion = "18.1"
        };

        List<DeviceUserModel> workers;
        try
        {
            // Act
            workers = await Index(core);
        }
        finally
        {
            HttpFake.ReportedUnits.TryRemove((int)unit.MicrotingUid!, out _);
        }

        // Assert
        var worker = workers.Single(x => x.SiteId == site);
        Assert.Multiple(() =>
        {
            Assert.That(worker.ComplianceApp.Version, Is.EqualTo(latest));
            Assert.That(worker.ComplianceApp.NeedsUpdate, Is.False);
        });
    }

    [Test]
    public void LatestVersions_AreBakedIn_ForEveryApp()
    {
        foreach (var app in new[]
                 {
                     AppLatestVersions.Compliance, AppLatestVersions.Adhoc, AppLatestVersions.Time,
                     AppLatestVersions.Archive, AppLatestVersions.Chemistry, AppLatestVersions.Halebid
                 })
        {
            Assert.That(Version.TryParse(AppLatestVersions.For(app), out _), Is.True, app);
        }
    }

    [TestCase("4.1.11", "4.1.21", true)]
    [TestCase("4.1.21", "4.1.21", false)]
    [TestCase("4.1.22", "4.1.21", false)]
    [TestCase("4.1.9", "4.1.21", true, TestName = "Numeric, not lexical: 4.1.9 < 4.1.21")]
    [TestCase("v4.1.11", "4.1.21", true)]
    [TestCase("4.1.21+40121", "4.1.21", false)]
    [TestCase("4.1", "4.1.0", false, TestName = "A missing patch part equals .0")]
    [TestCase("4.1.21.1", "4.1.21.2", true, TestName = "A fourth part is compared too")]
    [TestCase("4.1.21", "4.1.21.0", false, TestName = "A missing fourth part equals .0")]
    [TestCase(null, "4.1.21", false)]
    [TestCase("garbage", "4.1.21", false)]
    [TestCase("4.1.11", null, false)]
    public void IsOutdated_ComparesNumerically(string? reported, string? latest, bool expected)
        => Assert.That(AppLatestVersions.IsOutdated(reported, latest), Is.EqualTo(expected));

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
