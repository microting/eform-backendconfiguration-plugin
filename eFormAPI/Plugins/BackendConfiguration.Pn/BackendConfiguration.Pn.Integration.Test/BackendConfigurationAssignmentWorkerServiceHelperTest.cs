using BackendConfiguration.Pn.Infrastructure.Helpers;
using BackendConfiguration.Pn.Infrastructure.Models;
using BackendConfiguration.Pn.Infrastructure.Models.AssignmentWorker;
using BackendConfiguration.Pn.Infrastructure.Models.Properties;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;
using eFormCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.EformAngularFrontendBase.Infrastructure.Data.Entities.Permissions;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.ItemsPlanningBase.Infrastructure.Enums;
using Microting.TimePlanningBase.Infrastructure.Data.Entities;
using NSubstitute;
using BcPlanning = Microting.ItemsPlanningBase.Infrastructure.Data.Entities.Planning;

namespace BackendConfiguration.Pn.Integration.Test;

[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class BackendConfigurationAssignmentWorkerServiceHelperTest : TestBaseSetup
{
    // Opted out of the fixture-scoped schema replay:
    // ~90 whole-table counts and positional indexes (sites[2], properties[0], entityItems[7]).
    protected override bool ResetDatabasePerTest => true;

    [SetUp]
    public Task ReserveEformUserId1() => IdentityTestUtils.ReserveEformUserId1Async(BaseDbContext!);

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

    // A worker with only an email — no time registration, no archive, no web
    // access — must still get a login, so "Set password" in property-workers has
    // something to act on. It is created WITHOUT a password: the account exists
    // but cannot be signed into until an admin sets one.
    [Test]
    public async Task BackendConfigurationAssignmentWorkerServiceHelper_CreateDeviceUser_EmailOnly_CreatesUserWithNoPassword()
    {
        // Arrange
        var core = await GetCore();
        var workerEmail = $"{Guid.NewGuid()}@test.com";
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
            WorkerEmail = workerEmail
        };

        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);

        // Act
        var result = await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            TimePlanningPnDbContext!, BaseDbContext!, userService, userManager);

        // Assert
        Assert.That(result.Success, Is.True, result.Message);

        var createdUser = await BaseDbContext!.Users.SingleAsync(x => x.Email == workerEmail);
        Assert.That(createdUser.PasswordHash, Is.Null,
            "a new device user must have no usable password until an admin sets one");
        Assert.That(await userManager.CheckPasswordAsync(createdUser, "Replace_me_with_a_proper_password_2024!"),
            Is.False, "the retired hardcoded literal must not be accepted");
        Assert.That(await userManager.CheckPasswordAsync(createdUser, ""), Is.False,
            "an account with no password set must not accept a blank password");
    }

    // The synthetic address UpdateDeviceUser generates is a credential, not a
    // mailbox, so it counts as an email and still yields a login.
    [Test]
    public async Task BackendConfigurationAssignmentWorkerServiceHelper_CreateDeviceUser_SyntheticInvalidEmail_CreatesUserWithNoPassword()
    {
        // Arrange
        var core = await GetCore();
        var workerEmail = $"user_{Guid.NewGuid():N}@microting.invalid";
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
            WorkerEmail = workerEmail
        };

        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);

        // Act
        var result = await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            TimePlanningPnDbContext!, BaseDbContext!, userService, userManager);

        // Assert
        Assert.That(result.Success, Is.True, result.Message);
        var createdUser = await BaseDbContext!.Users.SingleAsync(x => x.Email == workerEmail);
        Assert.That(createdUser.PasswordHash, Is.Null);

        // "Treated identically" is the whole decision about synthetic addresses, and
        // the group is half of what a real address gets: a login with no password set,
        // which is legal through account-management because it belongs to "none".
        var groupNames = await GetSecurityGroupNamesAsync(createdUser.Id);
        Assert.That(groupNames, Is.EqualTo(new List<string>
                { BackendConfigurationAssignmentWorkerServiceHelper.NoPermissionsSecurityGroupName }),
            "a synthetic address must land in the fallback group exactly like a real one");
    }

    // Identity validates BEFORE it touches the store, so a rejected CreateAsync
    // leaves `user` non-null with Id still 0. Every guard downstream keys off
    // `user != null`, so an unreset reference sends EformUserId 0 into the
    // unconditional EnsureFallbackSecurityGroupAsync, whose FK to EformUser
    // throws — and that exception reaches the outer catch, which runs
    // CleanupOrphanSiteAsync and DELETES the Site and Worker just created. The admin
    // would get DeviceUserCouldNotBeCreated and no worker, for an input whose worker
    // must still be saved. A non-ASCII local part triggers it: the default
    // AllowedUserNameCharacters is untouched by this app's Identity configuration.
    [Test]
    public async Task BackendConfigurationAssignmentWorkerServiceHelper_CreateDeviceUser_IdentityRejectsEmail_KeepsWorkerAndCreatesNoUser()
    {
        // Arrange
        var core = await GetCore();
        var sdkDbContext = core.DbContextHelper.GetDbContext();
        var workerEmail = $"s\u00f8ren-{Guid.NewGuid():N}@firma.dk";
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
            WorkerEmail = workerEmail
        };

        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);

        // Act
        var result = await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            TimePlanningPnDbContext!, BaseDbContext!, userService, userManager);

        // Assert — no login, but the worker the admin asked for still exists.
        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(await BaseDbContext!.Users.AnyAsync(x => x.Email == workerEmail), Is.False,
            "Identity refused the address, so there must be no account");
        Assert.That(await sdkDbContext.Workers.AnyAsync(x => x.Email == workerEmail), Is.True,
            "the worker must survive a login Identity refused to create");
        Assert.That(await BaseDbContext.SecurityGroupUsers.CountAsync(x => x.EformUserId == 0), Is.EqualTo(0),
            "no SecurityGroupUser row should ever be written against a non-existent user");
    }

    // Update-side twin of CreateDeviceUser_IdentityRejectsEmail_
    // KeepsWorkerAndCreatesNoUser. UpdateDeviceUser resolves the worker's
    // existing login and validates the target address with Identity's own
    // validators BEFORE core.SiteUpdate/worker.Update ever run, so a refused
    // address (here: non-ASCII, outside Identity's default
    // AllowedUserNameCharacters) fails the WHOLE update up front - the worker
    // keeps its prior field values, not just "no login created".
    //
    // Uses IdentityTestUtils.CreateRealUserService for userService.UserId; the
    // login itself is resolved via FindLoginWithoutSideEffectsAsync, not
    // through the IUserService.
    // The worker is created with an EMPTY WorkerEmail ("", not null -
    // SQL/420_SDK.sql's seeded Worker id 2 already has a NULL email, so only ""
    // singles this worker out when the Assert step looks it up by address), so
    // it starts with no login at all -
    // this pins the refusal for a worker with NO prior login; the sibling test
    // TargetEmailOwnedByAnotherAccount_RefusesBeforeAnyWrite below pins it for a
    // worker that already HAS one.
    [Test]
    public async Task BackendConfigurationAssignmentWorkerServiceHelper_UpdateDeviceUser_IdentityRejectsEmail_KeepsWorkerAndCreatesNoUser()
    {
        // Arrange
        var core = await GetCore();
        var logger = Substitute.For<ILogger>();

        var originalFirstName = Guid.NewGuid().ToString();
        var originalLastName = Guid.NewGuid().ToString();
        var deviceUserModel = new DeviceUserModel
        {
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = false,
            UserFirstName = originalFirstName,
            UserLastName = originalLastName,
            WorkerEmail = ""
        };

        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);
        var userService = IdentityTestUtils.CreateRealUserService(BaseDbContext!, userManager);

        await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            TimePlanningPnDbContext!, BaseDbContext!, userService, userManager);

        var currentSite = await MicrotingDbContext!.Sites.OrderByDescending(x => x.Id).FirstAsync();
        var usersBefore = await BaseDbContext!.Users.CountAsync();

        var refusedEmail = $"s\u00f8ren-{Guid.NewGuid():N}@firma.dk";

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
            PhoneNumber = Guid.NewGuid().ToString("N")[..8],
            WorkerEmail = refusedEmail
        };

        // Act
        var result = await BackendConfigurationAssignmentWorkerServiceHelper.UpdateDeviceUser(newDeviceUserModel, core, 1,
            userService, userManager,
            BackendConfigurationPnDbContext!,
            TimePlanningPnDbContext!, BaseDbContext!, logger, ItemsPlanningPnDbContext!);

        // Assert — the whole update is refused before any write: the worker
        // keeps its ORIGINAL field values, not the ones from this refused call.
        Assert.That(result.Success, Is.False, result.Message);
        var untouchedWorker = await MicrotingDbContext.Workers.SingleAsync(x => x.Email == "");
        Assert.That(untouchedWorker.FirstName, Is.EqualTo(originalFirstName));
        Assert.That(untouchedWorker.LastName, Is.EqualTo(originalLastName));
        Assert.That(untouchedWorker.PhoneNumber, Is.Null.Or.Empty);

        Assert.That(await BaseDbContext.Users.AnyAsync(x => x.Email == refusedEmail), Is.False,
            "Identity refused the address, so there must be no account");
        Assert.That(await BaseDbContext.Users.CountAsync(), Is.EqualTo(usersBefore),
            "no login must have been created for the refused address");
        Assert.That(await BaseDbContext.SecurityGroupUsers.CountAsync(x => x.EformUserId == 0), Is.EqualTo(0),
            "no SecurityGroupUser row should ever be written against a non-existent user");
    }

    // CreateDeviceUser looks the address up by USERNAME, falling back to an
    // EMAIL match (FindLoginWithoutSideEffectsAsync), and links the new worker
    // to the non-admin account it finds - overwriting that account's UserName,
    // FirstName, LastName and Locale - instead of creating a second login for
    // the same address. This test pins that adoption. The cases where
    // CreateDeviceUser refuses the address instead are pinned by the sibling
    // tests: an ADMIN or id-1 account (the tests below) and an address split
    // across two accounts (SplitEmailCollision_RefusesBeforeAnyWrite).
    [Test]
    public async Task BackendConfigurationAssignmentWorkerServiceHelper_CreateDeviceUser_EmailAlreadyOwnedByAnotherAccount_AdoptsIt()
    {
        // Arrange
        var core = await GetCore();
        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);
        var userService = IdentityTestUtils.CreateRealUserService(BaseDbContext!, userManager);

        var sharedEmail = $"{Guid.NewGuid()}@already-owned.test";
        var existingAccount = new EformUser
        {
            Email = sharedEmail,
            UserName = $"existing-{Guid.NewGuid()}",
            FirstName = "Existing",
            LastName = "Owner",
            Locale = "da",
            EmailConfirmed = true,
            TimeZone = "Europe/Copenhagen",
            Formats = "de-DE"
        };
        Assert.That((await userManager.CreateAsync(existingAccount)).Succeeded, Is.True);
        var existingAccountId = existingAccount.Id;

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
            WorkerEmail = sharedEmail
        };

        // Act
        var result = await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            TimePlanningPnDbContext!, BaseDbContext!, userService, userManager);

        // Assert — pinning the adopt behaviour: no second row, the pre-existing
        // account is the one linked to the new worker.
        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(await BaseDbContext!.Users.CountAsync(x => x.Email == sharedEmail), Is.EqualTo(1),
            "no second AspNetUsers row must be created for an address that is already owned");
        var linkedUser = await BaseDbContext.Users.SingleAsync(x => x.Email == sharedEmail);
        Assert.That(linkedUser.Id, Is.EqualTo(existingAccountId),
            "the pre-existing account is the one adopted, not a fresh row");
        Assert.That(linkedUser.UserName, Is.EqualTo(sharedEmail),
            "the adopt path overwrites the pre-existing account's UserName to match the lookup address");
    }

    // Refuse to adopt an ADMIN account, with NO write at all - not even the SDK
    // site/worker. Production's GetByUsernameAsync is not a pure read: its
    // email fallback renames and SAVES whatever account it finds, so the
    // admin's UserName would be overwritten before any refusal could run.
    // FindLoginWithoutSideEffectsAsync is a NON-mutating lookup
    // (FindByNameAsync ?? FindByEmailAsync) done BEFORE core.SiteCreate, so a
    // refused create leaves everything untouched: no SDK site, no SDK worker,
    // no SiteTags, no ReconcileEventsForWorkerTagsAsync deployment, and the
    // admin account exactly as it was - there is nothing to orphan or clean up.
    [Test]
    public async Task BackendConfigurationAssignmentWorkerServiceHelper_CreateDeviceUser_EmailOwnedByAdmin_RefusesWithNoWrite()
    {
        // Arrange
        var core = await GetCore();
        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);
        var userService = IdentityTestUtils.CreateRealUserService(BaseDbContext!, userManager);
        var reconciliationService = Substitute.For<ICalendarAssignmentReconciliationService>();
        var tagId = await SeedSdkTag();

        // ReserveEformUserId1 keeps adminAccount off id 1, so only the IsInRoleAsync(Admin)
        // half of the gate can explain the refusal below.
        var adminEmail = $"{Guid.NewGuid()}@already-owned-admin.test";
        var adminAccount = new EformUser
        {
            Email = adminEmail,
            UserName = $"admin-{Guid.NewGuid()}",
            FirstName = "Admin",
            LastName = "Owner",
            Locale = "da",
            EmailConfirmed = true,
            TimeZone = "Europe/Copenhagen",
            Formats = "de-DE"
        };
        Assert.That((await userManager.CreateAsync(adminAccount)).Succeeded, Is.True);
        var addToRole = await userManager.AddToRoleAsync(adminAccount, EformRole.Admin);
        Assert.That(addToRole.Succeeded, Is.True, string.Join(",", addToRole.Errors.Select(e => e.Description)));
        var adminAccountId = adminAccount.Id;
        var adminAccountUserName = adminAccount.UserName;
        var adminAccountFirstName = adminAccount.FirstName;
        var adminAccountLocale = adminAccount.Locale;

        var workerFirstName = Guid.NewGuid().ToString();
        var workerLastName = Guid.NewGuid().ToString();
        var deviceUserModel = new DeviceUserModel
        {
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = false,
            UserFirstName = workerFirstName,
            UserLastName = workerLastName,
            Tags = [tagId],
            WorkerEmail = adminEmail
        };

        // Act
        var result = await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            TimePlanningPnDbContext!, BaseDbContext!, userService, userManager, reconciliationService);

        // Assert
        Assert.That(result.Success, Is.False, result.Message);

        var sdkDbContext = core.DbContextHelper.GetDbContext();
        var siteName = $"{workerFirstName} {workerLastName}";
        // No WorkflowState filter: a site created then cleaned up by
        // CleanupOrphanSiteAsync would still pass a "!= Removed" check as long
        // as its WorkflowState ends up Removed - this must pin that NO row
        // (of either state) exists at all, not merely that none is active.
        Assert.That(await sdkDbContext.Sites.AnyAsync(x => x.Name == siteName), Is.False,
            "no SDK site may ever be created for a refused admin-adoption attempt");
        Assert.That(await sdkDbContext.Workers.AnyAsync(x => x.Email == adminEmail), Is.False,
            "no SDK worker may ever be created for a refused admin-adoption attempt");
        Assert.That(await sdkDbContext.SiteTags.AnyAsync(x => x.TagId == tagId), Is.False,
            "no SiteTag may ever be written for a refused admin-adoption attempt");
        await reconciliationService.DidNotReceive()
            .ReconcileEventsForWorkerTagsAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>());

        Assert.That(await BaseDbContext!.Users.CountAsync(x => x.Email == adminEmail), Is.EqualTo(1),
            "no second AspNetUsers row must be created for the admin's address");
        var reloadedAdmin = await BaseDbContext.Users.SingleAsync(x => x.Email == adminEmail);
        Assert.That(reloadedAdmin.Id, Is.EqualTo(adminAccountId));
        Assert.That(reloadedAdmin.UserName, Is.EqualTo(adminAccountUserName),
            "the admin account's UserName must be untouched - the lookup that finds it must not be the mutating one");
        Assert.That(reloadedAdmin.FirstName, Is.EqualTo(adminAccountFirstName),
            "the admin account's FirstName must be untouched");
        Assert.That(reloadedAdmin.Locale, Is.EqualTo(adminAccountLocale),
            "the admin account's Locale must be untouched");
    }

    // Pins the OTHER half of the gate (`existingAccountForEmail.Id == 1 ||
    // IsInRoleAsync(Admin)`): an account that IS EformUser id 1 but carries no
    // admin ROLE membership at all must still be refused. The subject is put on
    // id 1 with IdentityTestUtils.ForceCreateAsEformUserId1Async (see its docs).
    [Test]
    public async Task BackendConfigurationAssignmentWorkerServiceHelper_CreateDeviceUser_EmailOwnedByAccountId1_RefusesWithNoWrite()
    {
        // Arrange
        var core = await GetCore();
        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);
        var userService = IdentityTestUtils.CreateRealUserService(BaseDbContext!, userManager);
        var reconciliationService = Substitute.For<ICalendarAssignmentReconciliationService>();
        var tagId = await SeedSdkTag();

        var ownerEmail = $"{Guid.NewGuid()}@id1-owned.test";
        var id1Account = await IdentityTestUtils.ForceCreateAsEformUserId1Async(BaseDbContext!, userManager,
            new EformUser
            {
                Email = ownerEmail,
                UserName = ownerEmail,
                FirstName = "Id1",
                LastName = "Owner",
                Locale = "da",
                EmailConfirmed = true,
                TimeZone = "Europe/Copenhagen",
                Formats = "de-DE"
            });
        Assert.That(id1Account.Id, Is.EqualTo(1), "sanity: the subject must genuinely be id 1");
        Assert.That(await userManager.IsInRoleAsync(id1Account, EformRole.Admin), Is.False,
            "this test pins the Id==1 half specifically - the admin-role half must play no part");
        var id1UserName = id1Account.UserName;

        var workerFirstName = Guid.NewGuid().ToString();
        var workerLastName = Guid.NewGuid().ToString();
        var deviceUserModel = new DeviceUserModel
        {
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = false,
            UserFirstName = workerFirstName,
            UserLastName = workerLastName,
            Tags = [tagId],
            WorkerEmail = ownerEmail
        };

        // Act
        var result = await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            TimePlanningPnDbContext!, BaseDbContext!, userService, userManager, reconciliationService);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo("EmailIsAlreadyInUse"));

        var sdkDbContext = core.DbContextHelper.GetDbContext();
        var siteName = $"{workerFirstName} {workerLastName}";
        Assert.That(await sdkDbContext.Sites.AnyAsync(x => x.Name == siteName), Is.False,
            "no SDK site may ever be created for a refused id-1-owned create");
        Assert.That(await sdkDbContext.SiteTags.AnyAsync(x => x.TagId == tagId), Is.False,
            "no SiteTag may ever be written for a refused id-1-owned create");
        await reconciliationService.DidNotReceive()
            .ReconcileEventsForWorkerTagsAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>());

        var reloaded = await BaseDbContext!.Users.SingleAsync(x => x.Id == 1);
        Assert.That(reloaded.UserName, Is.EqualTo(id1UserName),
            "the id-1 account's UserName must be untouched");
    }

    // Pins the upfront collision pre-check (before core.SiteCreate): deleting it
    // still returns EmailIsAlreadyInUse via RefuseAdoptionAndCleanUpAsync, but only
    // AFTER SiteCreate/SiteTags/reconcile/worker.Update have all run - so what pins
    // it is that nothing was written, not the final Message. Account A's UserName is
    // the target address and account B's EMAIL is. A resolves by UserName and becomes
    // existingUserId, so the unique-USERNAME check excludes it; only the
    // RequireUniqueEmail check against B fires - hence CreateProductionLikeUserManager.
    [Test]
    public async Task BackendConfigurationAssignmentWorkerServiceHelper_CreateDeviceUser_SplitEmailCollision_RefusesBeforeAnyWrite()
    {
        // Arrange
        var core = await GetCore();
        var userManager = IdentityTestUtils.CreateProductionLikeUserManager(BaseDbContext!);
        var userService = IdentityTestUtils.CreateRealUserService(BaseDbContext!, userManager);
        var reconciliationService = Substitute.For<ICalendarAssignmentReconciliationService>();
        var tagId = await SeedSdkTag();

        var sharedAddress = $"{Guid.NewGuid()}@split-collision.test";

        // Account A's USERNAME is the target address...
        var accountA = new EformUser
        {
            Email = $"{Guid.NewGuid()}@account-a.test",
            UserName = sharedAddress,
            FirstName = "A",
            LastName = "Owner",
            Locale = "da",
            EmailConfirmed = true,
            TimeZone = "Europe/Copenhagen",
            Formats = "de-DE"
        };
        Assert.That((await userManager.CreateAsync(accountA)).Succeeded, Is.True);

        // ...while account B's EMAIL is the same address, under a DIFFERENT
        // username.
        var accountB = new EformUser
        {
            Email = sharedAddress,
            UserName = $"account-b-{Guid.NewGuid()}",
            FirstName = "B",
            LastName = "Owner",
            Locale = "da",
            EmailConfirmed = true,
            TimeZone = "Europe/Copenhagen",
            Formats = "de-DE"
        };
        Assert.That((await userManager.CreateAsync(accountB)).Succeeded, Is.True);

        var workerFirstName = Guid.NewGuid().ToString();
        var workerLastName = Guid.NewGuid().ToString();
        var deviceUserModel = new DeviceUserModel
        {
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = false,
            UserFirstName = workerFirstName,
            UserLastName = workerLastName,
            Tags = [tagId],
            WorkerEmail = sharedAddress
        };

        // Act
        var result = await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            TimePlanningPnDbContext!, BaseDbContext!, userService, userManager, reconciliationService);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo("EmailIsAlreadyInUse"));

        var sdkDbContext = core.DbContextHelper.GetDbContext();
        var siteName = $"{workerFirstName} {workerLastName}";
        // No WorkflowState filter: a site created then cleaned up still has a
        // row (just Removed) - this must pin that NO row exists at all.
        Assert.That(await sdkDbContext.Sites.AnyAsync(x => x.Name == siteName), Is.False,
            "no SDK site row - of any WorkflowState - may exist for a refused split-collision create");
        Assert.That(await sdkDbContext.SiteTags.AnyAsync(x => x.TagId == tagId), Is.False,
            "no SiteTag may ever be written for a refused split-collision create");
        await reconciliationService.DidNotReceive()
            .ReconcileEventsForWorkerTagsAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>());
        Assert.That(await BaseDbContext!.Users.CountAsync(x => x.UserName == sharedAddress || x.Email == sharedAddress),
            Is.EqualTo(2), "only the two pre-existing accounts may exist under this address - the refused create must not add a third");
    }

    // UpdateDeviceUser's own SDK-worker uniqueness pre-check only scans
    // non-removed SDK WORKERS for the email, never AspNetUsers, so an address
    // already owned by an account with no live worker behind it (an admin, or
    // one left behind by a removed worker) sails through THAT check untouched -
    // ValidateCandidateEmailAsync, run before any SDK or Identity write, is
    // what actually catches it via RequireUniqueEmail.
    //
    // The worker is created with an EMPTY WorkerEmail ("", not null - see the
    // comment on the non-ASCII Update test above) so
    // FindLoginWithoutSideEffectsAsync(oldEmail) resolves to NO account - this
    // pins the refusal for a worker with NO prior login; the sibling test
    // TargetEmailOwnedByAnotherAccount_RefusesBeforeAnyWrite below pins it for a
    // worker that already HAS one.
    [Test]
    public async Task BackendConfigurationAssignmentWorkerServiceHelper_UpdateDeviceUser_DuplicateEmailIdentityRefuses_KeepsWorkerAndCreatesNoUser()
    {
        // Arrange
        var core = await GetCore();
        var logger = Substitute.For<ILogger>();
        var userManager = IdentityTestUtils.CreateProductionLikeUserManager(BaseDbContext!);
        var userService = IdentityTestUtils.CreateRealUserService(BaseDbContext!, userManager);

        var originalFirstName = Guid.NewGuid().ToString();
        var originalLastName = Guid.NewGuid().ToString();
        var deviceUserModel = new DeviceUserModel
        {
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = false,
            UserFirstName = originalFirstName,
            UserLastName = originalLastName,
            WorkerEmail = ""
        };
        await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            TimePlanningPnDbContext!, BaseDbContext!, userService, userManager);

        var currentSite = await MicrotingDbContext!.Sites.OrderByDescending(x => x.Id).FirstAsync();
        var usersBeforeCollision = await BaseDbContext!.Users.CountAsync();

        var sharedEmail = $"{Guid.NewGuid()}@already-taken.test";
        var existingAccount = new EformUser
        {
            Email = sharedEmail,
            UserName = $"existing-{Guid.NewGuid()}",
            FirstName = Guid.NewGuid().ToString(),
            LastName = Guid.NewGuid().ToString(),
            Locale = "da",
            EmailConfirmed = true,
            TimeZone = "Europe/Copenhagen",
            Formats = "de-DE"
        };
        Assert.That((await userManager.CreateAsync(existingAccount)).Succeeded, Is.True);

        // A REAL tag, not a fake id: the SDK's FK_SiteTags_tags_TagId means a
        // fake id would throw on SiteTag.Create regardless of write order, so
        // the tag-untouched assertion below would pass even if the tag block
        // ran BEFORE validation - it would just fail via the outer catch's
        // generic "DeviceUserCouldNotBeUpdated" instead. A real id makes the
        // assertion fail loudly (a written SiteTag row) if that order regresses.
        var tagId = await SeedSdkTag();

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
            PhoneNumber = Guid.NewGuid().ToString("N")[..8],
            Tags = [tagId],
            WorkerEmail = sharedEmail
        };

        // Act
        var result = await BackendConfigurationAssignmentWorkerServiceHelper.UpdateDeviceUser(newDeviceUserModel, core, 1,
            userService, userManager,
            BackendConfigurationPnDbContext!,
            TimePlanningPnDbContext!, BaseDbContext!, logger, ItemsPlanningPnDbContext!);

        // Assert — the whole update is refused before any write. The exact
        // message pins this as the collision refusal specifically - the
        // generic catch's "DeviceUserCouldNotBeUpdated" would also satisfy a
        // bare `Success is False` check, so that alone cannot distinguish "the
        // collision check refused it" from "the tag write blew up".
        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo("EmailIsAlreadyInUse"));
        var untouchedWorker = await MicrotingDbContext.Workers.SingleAsync(x => x.Email == "");
        Assert.That(untouchedWorker.FirstName, Is.EqualTo(originalFirstName));
        Assert.That(untouchedWorker.LastName, Is.EqualTo(originalLastName));

        var sdkDbContext = core.DbContextHelper.GetDbContext();
        await AssertNoActiveSiteTagsAsync(sdkDbContext, currentSite.Id);

        Assert.That(await BaseDbContext.Users.CountAsync(x => x.Email == sharedEmail), Is.EqualTo(1),
            "RequireUniqueEmail must have blocked a second account for the same address");
        var reloadedExisting = await BaseDbContext.Users.SingleAsync(x => x.Email == sharedEmail);
        Assert.That(reloadedExisting.Id, Is.EqualTo(existingAccount.Id));
        Assert.That(await BaseDbContext.Users.CountAsync(), Is.EqualTo(usersBeforeCollision + 1),
            "only the pre-existing account for the shared address may exist; the refused update must not add a row");
        Assert.That(await BaseDbContext.SecurityGroupUsers.CountAsync(x => x.EformUserId == 0), Is.EqualTo(0),
            "no SecurityGroupUser row should ever be written against a non-existent user");
    }

    // UpdateDeviceUser's own pre-check only looks at SDK workers, never
    // AspNetUsers, so an address B owned by another account (an admin's, say)
    // passes it. UpdateDeviceUser therefore resolves the worker's login and
    // validates the FINAL target address with Identity's own validators
    // BEFORE core.SiteUpdate or worker.Update runs. A collision refuses the
    // WHOLE update and leaves the worker, its SDK site and its login
    // unchanged, so the worker's address and its login's address never
    // diverge. They must match because later saves and Set password look the
    // login up by the worker's address. The check-and-Reload guard on
    // UpdateAsync covers a race between the up-front check and the write.
    [Test]
    public async Task BackendConfigurationAssignmentWorkerServiceHelper_UpdateDeviceUser_TargetEmailOwnedByAnotherAccount_RefusesBeforeAnyWrite()
    {
        // Arrange
        var core = await GetCore();
        var logger = Substitute.For<ILogger>();
        var userManager = IdentityTestUtils.CreateProductionLikeUserManager(BaseDbContext!);
        var userService = IdentityTestUtils.CreateRealUserService(BaseDbContext!, userManager);

        var emailA = $"{Guid.NewGuid()}@test.com";
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
            WorkerEmail = emailA
        };
        await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            TimePlanningPnDbContext!, BaseDbContext!, userService, userManager);

        var currentSite = await MicrotingDbContext!.Sites.OrderByDescending(x => x.Id).FirstAsync();

        // A different account owns email B under a DIFFERENT username, so this
        // can only be caught by RequireUniqueEmail - never by Identity's
        // unconditional unique-USERNAME check.
        var emailB = $"{Guid.NewGuid()}@already-taken.test";
        var otherAccount = new EformUser
        {
            Email = emailB,
            UserName = $"other-{Guid.NewGuid()}",
            FirstName = "Other",
            LastName = "Owner",
            Locale = "da",
            EmailConfirmed = true,
            TimeZone = "Europe/Copenhagen",
            Formats = "de-DE"
        };
        Assert.That((await userManager.CreateAsync(otherAccount)).Succeeded, Is.True);
        var otherAccountId = otherAccount.Id;
        var otherAccountUserName = otherAccount.UserName;
        var otherAccountFirstName = otherAccount.FirstName;

        // A REAL tag - see SeedSdkTag's doc comment for why a fake id like
        // 999999 cannot distinguish "refused before the tag write" from
        // "the tag write itself threw".
        var tagId = await SeedSdkTag();

        var firstUpdateModel = new DeviceUserModel
        {
            SiteMicrotingUid = (int)currentSite.MicrotingUid!,
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = false,
            WebAccessEnabled = true,
            UserFirstName = Guid.NewGuid().ToString(),
            UserLastName = Guid.NewGuid().ToString(),
            Tags = [tagId],
            WorkerEmail = emailB
        };

        // Act
        var firstResult = await BackendConfigurationAssignmentWorkerServiceHelper.UpdateDeviceUser(firstUpdateModel, core, 1,
            userService, userManager,
            BackendConfigurationPnDbContext!,
            TimePlanningPnDbContext!, BaseDbContext!, logger, ItemsPlanningPnDbContext!);

        // Assert — the whole update is refused, BEFORE any SDK or Identity
        // write. The exact message pins this as the collision refusal, not
        // merely "something threw".
        Assert.That(firstResult.Success, Is.False);
        Assert.That(firstResult.Message, Is.EqualTo("EmailIsAlreadyInUse"));
        var sdkDbContext = core.DbContextHelper.GetDbContext();
        var workerAfterFirstCall = await sdkDbContext.Workers.SingleAsync(x => x.Email == emailA);
        Assert.That(workerAfterFirstCall.Email, Is.EqualTo(emailA),
            "the SDK worker must still show its OLD email - nothing may be written once the target is known to be taken");
        await AssertNoActiveSiteTagsAsync(sdkDbContext, currentSite.Id);
        var ownAccountBeforeSecondCall = await BaseDbContext!.Users.SingleAsync(x => x.Email == emailA);
        Assert.That(ownAccountBeforeSecondCall.UserName, Is.EqualTo(emailA),
            "the worker's own login must be untouched by the refused first call");

        // Second call: a benign update on the SAME worker (WorkerEmail back to
        // A, WebAccessEnabled toggled so a group-sync SaveChanges runs). It
        // resolves the worker's own login from the unchanged address A and
        // applies the group change there; the other account is untouched.
        var secondUpdateModel = new DeviceUserModel
        {
            SiteMicrotingUid = (int)currentSite.MicrotingUid!,
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = false,
            WebAccessEnabled = true,
            UserFirstName = Guid.NewGuid().ToString(),
            UserLastName = Guid.NewGuid().ToString(),
            WorkerEmail = emailA
        };
        var secondResult = await BackendConfigurationAssignmentWorkerServiceHelper.UpdateDeviceUser(secondUpdateModel, core, 1,
            userService, userManager,
            BackendConfigurationPnDbContext!,
            TimePlanningPnDbContext!, BaseDbContext!, logger, ItemsPlanningPnDbContext!);
        Assert.That(secondResult.Success, Is.True, secondResult.Message);

        var ownAccountAfterSecondCall = await BaseDbContext.Users.SingleAsync(x => x.Email == emailA);
        var ownGroupNames = await GetSecurityGroupNamesAsync(ownAccountAfterSecondCall.Id);
        Assert.That(ownGroupNames, Does.Contain("eForm users"),
            "the second call must reach the worker's own login");

        var reloadedOther = await BaseDbContext.Users.SingleAsync(x => x.Id == otherAccountId);
        Assert.That(reloadedOther.UserName, Is.EqualTo(otherAccountUserName),
            "the other account's UserName must be untouched by either call");
        Assert.That(reloadedOther.FirstName, Is.EqualTo(otherAccountFirstName),
            "the other account's FirstName must be untouched by either call");
        Assert.That(await BaseDbContext.SecurityGroupUsers.CountAsync(x => x.EformUserId == otherAccountId), Is.Zero,
            "the other account's group membership must be unchanged by either call");
    }

    // The format-refusal counterpart of
    // TargetEmailOwnedByAnotherAccount_RefusesBeforeAnyWrite. The worker has
    // login A and the request changes its address to B, which Identity
    // refuses for its format (non-ASCII). Because the address is changing,
    // the whole update is refused before core.SiteUpdate or worker.Update
    // runs, so the worker and its login both keep A.
    [Test]
    public async Task BackendConfigurationAssignmentWorkerServiceHelper_UpdateDeviceUser_InvalidAddress_WithExistingLogin_DoesNotDivergeFromWorker()
    {
        // Arrange
        var core = await GetCore();
        var logger = Substitute.For<ILogger>();
        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);
        var userService = IdentityTestUtils.CreateRealUserService(BaseDbContext!, userManager);

        var emailA = $"{Guid.NewGuid()}@test.com";
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
            WorkerEmail = emailA
        };
        await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            TimePlanningPnDbContext!, BaseDbContext!, userService, userManager);

        var currentSite = await MicrotingDbContext!.Sites.OrderByDescending(x => x.Id).FirstAsync();
        var refusedEmail = $"s\u00f8ren-{Guid.NewGuid():N}@firma.dk";
        var tagId = await SeedSdkTag();

        // Capture the login's group membership BEFORE the refused update.
        // CreateDeviceUser above already ran EnsureFallbackSecurityGroupAsync,
        // which lands a groupless login in "none" - so a raw
        // SecurityGroupUsers.CountAsync == 0 check is not meaningful here: the
        // account already carries exactly one row before the update runs.
        // The refusal must leave that set of group NAMES unchanged, in
        // particular never adding "eForm users" for WebAccessEnabled=true.
        var ownAccount = await BaseDbContext!.Users.SingleAsync(x => x.Email == emailA);
        var groupNamesBefore = await GetSecurityGroupNamesAsync(ownAccount.Id);

        var newDeviceUserModel = new DeviceUserModel
        {
            SiteMicrotingUid = (int)currentSite.MicrotingUid!,
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = false,
            WebAccessEnabled = true,
            UserFirstName = Guid.NewGuid().ToString(),
            UserLastName = Guid.NewGuid().ToString(),
            Tags = [tagId],
            WorkerEmail = refusedEmail
        };

        // Act
        var result = await BackendConfigurationAssignmentWorkerServiceHelper.UpdateDeviceUser(newDeviceUserModel, core, 1,
            userService, userManager,
            BackendConfigurationPnDbContext!,
            TimePlanningPnDbContext!, BaseDbContext!, logger, ItemsPlanningPnDbContext!);

        // Assert — refused before any write: worker and login both stay at A.
        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo("EmailIsNotValid"));
        var untouchedWorker = await MicrotingDbContext!.Workers.SingleAsync(x => x.Email == emailA);
        Assert.That(untouchedWorker.Email, Is.EqualTo(emailA));
        var sdkDbContext = core.DbContextHelper.GetDbContext();
        await AssertNoActiveSiteTagsAsync(sdkDbContext, currentSite.Id);
        var reloadedOwnAccount = await BaseDbContext.Users.SingleAsync(x => x.Email == emailA);
        Assert.That(reloadedOwnAccount.UserName, Is.EqualTo(emailA));
        Assert.That(await BaseDbContext.Users.AnyAsync(x => x.Email == refusedEmail), Is.False,
            "Identity refused the address, so there must be no account under it");
        var groupNamesAfter = await GetSecurityGroupNamesAsync(ownAccount.Id);
        Assert.That(groupNamesAfter, Is.EquivalentTo(groupNamesBefore),
            "the refused update must not change the login's group membership at all");
        Assert.That(groupNamesAfter, Does.Not.Contain("eForm users"),
            "WebAccessEnabled must not have been applied to the login when the whole update was refused");
    }

    // A refusal that is neither a collision nor a format failure (here a
    // UserValidator outside Identity's own email codes) is reported as the
    // generic DeviceUserCouldNotBeUpdated - not as EmailIsNotValid or
    // EmailIsAlreadyInUse, which would send the operator hunting for a problem
    // with the address. ValidateCandidateEmailAsync runs every UserValidator,
    // so the save is refused before any write. UpdateDeviceUser's outer catch
    // returns the same key for any exception, so the refusal must also show up
    // as the warning the validation step logs.
    [Test]
    public async Task BackendConfigurationAssignmentWorkerServiceHelper_UpdateDeviceUser_NonEmailValidationFailure_ReportsCouldNotBeUpdated()
    {
        // Arrange
        var core = await GetCore();
        var logger = Substitute.For<ILogger>();
        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);
        var userService = IdentityTestUtils.CreateRealUserService(BaseDbContext!, userManager);

        var emailA = $"{Guid.NewGuid()}@test.com";
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
            WorkerEmail = emailA
        };
        var createResult = await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel,
            core, 1, TimePlanningPnDbContext!, BaseDbContext!, userService, userManager);
        Assert.That(createResult.Success, Is.True, createResult.Message);
        Assert.That(await BaseDbContext!.Users.AnyAsync(x => x.UserName == emailA), Is.True,
            "sanity: the worker must have a login for the refused update to leave untouched");

        var (refusingUserManager, refusingUserService) = CreateRefusingUserManagerAndService();

        var currentSite = await MicrotingDbContext!.Sites.OrderByDescending(x => x.Id).FirstAsync();
        var emailB = $"{Guid.NewGuid()}@test.com";
        var newDeviceUserModel = new DeviceUserModel
        {
            SiteMicrotingUid = (int)currentSite.MicrotingUid!,
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = false,
            UserFirstName = deviceUserModel.UserFirstName,
            UserLastName = deviceUserModel.UserLastName,
            WorkerEmail = emailB
        };

        // Act
        var result = await BackendConfigurationAssignmentWorkerServiceHelper.UpdateDeviceUser(newDeviceUserModel, core, 1,
            refusingUserService, refusingUserManager,
            BackendConfigurationPnDbContext!,
            TimePlanningPnDbContext!, BaseDbContext, logger, ItemsPlanningPnDbContext!);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo("DeviceUserCouldNotBeUpdated"),
            "a non-email validation failure must not be reported as an email problem");
        Assert.That(logger.ReceivedCalls().Any(call => call.GetMethodInfo().Name == nameof(ILogger.Log)
                && call.GetArguments()[0] is LogLevel.Warning
                && call.GetArguments()[2]?.ToString()?.Contains("refusing to save email") == true), Is.True,
            "the refusal must come from the email validation step, not from UpdateDeviceUser's outer catch");
        Assert.That(await MicrotingDbContext.Workers.AsNoTracking().CountAsync(x => x.Email == emailA), Is.EqualTo(1),
            "the worker must keep its address");
        Assert.That(await MicrotingDbContext.Workers.AsNoTracking().AnyAsync(x => x.Email == emailB), Is.False,
            "the refused address must not have reached the worker");
        var untouchedLogin = await BaseDbContext.Users.AsNoTracking().SingleAsync(x => x.Email == emailA);
        Assert.That(untouchedLogin.UserName, Is.EqualTo(emailA));
        Assert.That(await BaseDbContext.Users.AnyAsync(x => x.Email == emailB || x.UserName == emailB), Is.False,
            "the refused address must not have reached any account");
    }

    // A create that adopts an existing non-admin account and then has that
    // account's UpdateAsync refused for a reason that is neither a collision nor
    // a format failure reports DeviceUserCouldNotBeCreated, removes the site and
    // worker it created, and leaves the account as it was. The up-front
    // collision check lets a non-collision refusal through, so it is the
    // adoption UpdateAsync that fails here.
    [Test]
    public async Task BackendConfigurationAssignmentWorkerServiceHelper_CreateDeviceUser_NonEmailValidationFailureOnAdoption_ReportsCouldNotBeCreated()
    {
        // Arrange
        var core = await GetCore();
        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);

        var ownedEmail = $"{Guid.NewGuid()}@adoption-refused.test";
        var existingAccount = new EformUser
        {
            Email = ownedEmail,
            UserName = $"owner-{Guid.NewGuid()}",
            FirstName = "Existing",
            LastName = "Owner",
            Locale = "da",
            EmailConfirmed = true,
            TimeZone = "Europe/Copenhagen",
            Formats = "de-DE"
        };
        Assert.That((await userManager.CreateAsync(existingAccount)).Succeeded, Is.True);
        var existingAccountId = existingAccount.Id;
        var originalUserName = existingAccount.UserName;
        var originalFirstName = existingAccount.FirstName;

        var (refusingUserManager, refusingUserService) = CreateRefusingUserManagerAndService();

        var workerFirstName = Guid.NewGuid().ToString();
        var workerLastName = Guid.NewGuid().ToString();
        var deviceUserModel = new DeviceUserModel
        {
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = false,
            UserFirstName = workerFirstName,
            UserLastName = workerLastName,
            WorkerEmail = ownedEmail
        };

        // Act
        var result = await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            TimePlanningPnDbContext!, BaseDbContext!, refusingUserService, refusingUserManager);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo("DeviceUserCouldNotBeCreated"),
            "a failed create must be reported as a create failure, not as an update failure or an email problem");

        var siteName = $"{workerFirstName} {workerLastName}";
        Assert.That(await MicrotingDbContext!.Sites.AsNoTracking().AnyAsync(x => x.Name == siteName
                && x.WorkflowState != Constants.WorkflowStates.Removed), Is.False,
            "the site created for the refused adoption must not stay live");
        Assert.That(await MicrotingDbContext.Sites.AsNoTracking().AnyAsync(x => x.Name == siteName
                && x.WorkflowState == Constants.WorkflowStates.Removed), Is.True,
            "the site must have been created and then removed, so the adoption UpdateAsync is the step that failed");
        Assert.That(await MicrotingDbContext.Workers.AsNoTracking().AnyAsync(x => x.Email == ownedEmail
                && x.WorkflowState != Constants.WorkflowStates.Removed), Is.False,
            "the worker created for the refused adoption must not stay live");

        var untouchedAccount = await BaseDbContext!.Users.AsNoTracking().SingleAsync(x => x.Id == existingAccountId);
        Assert.That(untouchedAccount.UserName, Is.EqualTo(originalUserName),
            "the refused adoption must not rename the account");
        Assert.That(untouchedAccount.FirstName, Is.EqualTo(originalFirstName),
            "the refused adoption must not overwrite the account's name");
    }

    // An UNCHANGED, previously-saved address Identity would refuse (format
    // failure) must not block every OTHER edit forever - real population for a
    // Danish customer base, where rows with æøå addresses were saved without
    // this validation. CreateDeviceUser refuses to create a LOGIN for a
    // non-ASCII address while still saving the worker
    // (CreateDeviceUser_IdentityRejectsEmail_KeepsWorkerAndCreatesNoUser), so
    // that same call reproduces exactly the "pre-existing, never-validated"
    // row that must stay editable.
    [Test]
    public async Task BackendConfigurationAssignmentWorkerServiceHelper_UpdateDeviceUser_UnchangedInvalidAddress_StillAllowsOtherEdits()
    {
        // Arrange
        var core = await GetCore();
        var logger = Substitute.For<ILogger>();
        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);
        var userService = IdentityTestUtils.CreateRealUserService(BaseDbContext!, userManager);

        var invalidEmail = $"s\u00f8ren-{Guid.NewGuid():N}@firma.dk";
        var originalPhone = Guid.NewGuid().ToString("N")[..8];
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
            PhoneNumber = originalPhone,
            WorkerEmail = invalidEmail
        };
        var createResult = await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            TimePlanningPnDbContext!, BaseDbContext!, userService, userManager);
        Assert.That(createResult.Success, Is.True, createResult.Message);
        Assert.That(await BaseDbContext!.Users.AnyAsync(x => x.Email == invalidEmail), Is.False,
            "precondition: this worker must start with no login - Identity already refused this address once");

        var currentSite = await MicrotingDbContext!.Sites.OrderByDescending(x => x.Id).FirstAsync();
        var usersBefore = await BaseDbContext.Users.CountAsync();

        var newFirstName = Guid.NewGuid().ToString();
        var newLastName = Guid.NewGuid().ToString();
        var newPhone = Guid.NewGuid().ToString("N")[..8];
        var newDeviceUserModel = new DeviceUserModel
        {
            SiteMicrotingUid = (int)currentSite.MicrotingUid!,
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = false,
            UserFirstName = newFirstName,
            UserLastName = newLastName,
            PhoneNumber = newPhone,
            WorkerEmail = invalidEmail // UNCHANGED
        };

        // Act
        var result = await BackendConfigurationAssignmentWorkerServiceHelper.UpdateDeviceUser(newDeviceUserModel, core, 1,
            userService, userManager,
            BackendConfigurationPnDbContext!,
            TimePlanningPnDbContext!, BaseDbContext!, logger, ItemsPlanningPnDbContext!);

        // Assert - the save succeeds: an unchanged, already-invalid address
        // must not block every other edit forever.
        Assert.That(result.Success, Is.True, result.Message);
        var updatedWorker = await MicrotingDbContext.Workers.SingleAsync(x => x.Email == invalidEmail);
        Assert.That(updatedWorker.FirstName, Is.EqualTo(newFirstName));
        Assert.That(updatedWorker.LastName, Is.EqualTo(newLastName));
        Assert.That(updatedWorker.PhoneNumber, Is.EqualTo(newPhone));
        Assert.That(await BaseDbContext.Users.AnyAsync(x => x.Email == invalidEmail), Is.False,
            "no login must be created on the Identity side for an address Identity refuses, even on a successful save");
        Assert.That(await BaseDbContext.Users.CountAsync(), Is.EqualTo(usersBefore));
    }

    // When a worker's resolved login is an admin account or user id 1,
    // UpdateDeviceUser saves the worker's own fields and never writes to or
    // re-groups that login. Such rows exist in production, because core can
    // promote a worker's login to admin independently of this plugin. The
    // rule holds even though ValidateCandidateEmailAsync's "is this the
    // worker's own account" exclusion would accept the update.
    [Test]
    public async Task BackendConfigurationAssignmentWorkerServiceHelper_UpdateDeviceUser_ResolvedLoginIsAdmin_SkipsLoginWorkButSavesWorker()
    {
        // Arrange
        var core = await GetCore();
        var logger = Substitute.For<ILogger>();
        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);
        var userService = IdentityTestUtils.CreateRealUserService(BaseDbContext!, userManager);

        // ReserveEformUserId1 keeps loginAccount off id 1, so only the IsInRoleAsync(Admin)
        // half of the guard can explain the assertions below.
        var workerEmail = $"{Guid.NewGuid()}@test.com";
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
            WorkerEmail = workerEmail
        };
        await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            TimePlanningPnDbContext!, BaseDbContext!, userService, userManager);

        var loginAccount = await BaseDbContext!.Users.SingleAsync(x => x.Email == workerEmail);
        var originalUserName = loginAccount.UserName;
        var originalFirstName = loginAccount.FirstName;
        var originalLocale = loginAccount.Locale;

        // Capture group membership BEFORE promoting to admin / updating.
        // CreateDeviceUser above already ran EnsureFallbackSecurityGroupAsync,
        // landing this groupless login in "none" - so a raw
        // SecurityGroupUsers.CountAsync == 0 check on loginAccount.Id is NOT
        // meaningful: it fails even when the guard works, because the
        // account already carries that one "none" row from setup, unrelated
        // to the WebAccessEnabled flag this test means to prove was ignored.
        var groupNamesBefore = await GetSecurityGroupNamesAsync(loginAccount.Id);

        // core promotes this worker's login to admin, independently of this
        // save path - exactly the scenario this test exists for.
        var addToRole = await userManager.AddToRoleAsync(loginAccount, EformRole.Admin);
        Assert.That(addToRole.Succeeded, Is.True, string.Join(",", addToRole.Errors.Select(e => e.Description)));

        var currentSite = await MicrotingDbContext!.Sites.OrderByDescending(x => x.Id).FirstAsync();
        var newFirstName = Guid.NewGuid().ToString();
        var newLastName = Guid.NewGuid().ToString();
        var newDeviceUserModel = new DeviceUserModel
        {
            SiteMicrotingUid = (int)currentSite.MicrotingUid!,
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = false,
            WebAccessEnabled = true, // would normally add real group membership
            UserFirstName = newFirstName,
            UserLastName = newLastName,
            WorkerEmail = workerEmail // unchanged
        };

        // Act
        var result = await BackendConfigurationAssignmentWorkerServiceHelper.UpdateDeviceUser(newDeviceUserModel, core, 1,
            userService, userManager,
            BackendConfigurationPnDbContext!,
            TimePlanningPnDbContext!, BaseDbContext!, logger, ItemsPlanningPnDbContext!);

        // Assert - the worker's own SDK fields still save...
        Assert.That(result.Success, Is.True, result.Message);
        var updatedWorker = await MicrotingDbContext!.Workers.SingleAsync(x => x.Email == workerEmail);
        Assert.That(updatedWorker.FirstName, Is.EqualTo(newFirstName));
        Assert.That(updatedWorker.LastName, Is.EqualTo(newLastName));

        // ...but the promoted-to-admin login is completely untouched, and
        // gains no group membership from this save - in particular not
        // "eForm users", even though WebAccessEnabled=true was requested.
        var reloadedLogin = await BaseDbContext.Users.SingleAsync(x => x.Id == loginAccount.Id);
        Assert.That(reloadedLogin.UserName, Is.EqualTo(originalUserName));
        Assert.That(reloadedLogin.FirstName, Is.EqualTo(originalFirstName));
        Assert.That(reloadedLogin.Locale, Is.EqualTo(originalLocale));
        var groupNamesAfter = await GetSecurityGroupNamesAsync(loginAccount.Id);
        Assert.That(groupNamesAfter, Is.EquivalentTo(groupNamesBefore),
            "the admin login's group membership must be completely unchanged by this save");
        Assert.That(groupNamesAfter, Does.Not.Contain("eForm users"),
            "WebAccessEnabled must not have added group membership to an admin login");
    }

    // Pins the OTHER half of the same gate as the test above: a resolved
    // login that IS EformUser id 1 but carries no admin ROLE membership must
    // still skip login/group work. The login is put on id 1 with
    // IdentityTestUtils.ForceCreateAsEformUserId1Async (see its docs) - the
    // worker is attached to it by mutating the SDK worker's Email directly
    // rather than through CreateDeviceUser, since CreateDeviceUser's OWN admin
    // gate (already pinned separately) would otherwise refuse to adopt an id-1
    // account.
    [Test]
    public async Task BackendConfigurationAssignmentWorkerServiceHelper_UpdateDeviceUser_ResolvedLoginIsId1_SkipsLoginWorkButSavesWorker()
    {
        // Arrange
        var core = await GetCore();
        var logger = Substitute.For<ILogger>();
        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);
        var userService = IdentityTestUtils.CreateRealUserService(BaseDbContext!, userManager);

        var workerEmail = $"{Guid.NewGuid()}@test.com";
        var loginAccount = await IdentityTestUtils.ForceCreateAsEformUserId1Async(BaseDbContext!, userManager,
            new EformUser
            {
                Email = workerEmail,
                UserName = workerEmail,
                FirstName = "Id1",
                LastName = "Login",
                Locale = "da",
                EmailConfirmed = true,
                TimeZone = "Europe/Copenhagen",
                Formats = "de-DE"
            });
        Assert.That(loginAccount.Id, Is.EqualTo(1), "sanity: the subject must genuinely be id 1");
        Assert.That(await userManager.IsInRoleAsync(loginAccount, EformRole.Admin), Is.False,
            "this test pins the Id==1 half specifically - the admin-role half must play no part");
        var originalUserName = loginAccount.UserName;
        var originalFirstName = loginAccount.FirstName;
        var originalLocale = loginAccount.Locale;

        // Create the worker with NO email at all, so CreateDeviceUser's own
        // admin gate never runs against the id-1 account - then attach the
        // worker to it directly on the SDK side, exactly like the NullEmail
        // test's "simulate a real population directly" approach.
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
            WorkerEmail = ""
        };
        await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            TimePlanningPnDbContext!, BaseDbContext!, userService, userManager);

        var sdkDbContext = core.DbContextHelper.GetDbContext();
        var worker = await sdkDbContext.Workers.SingleAsync(x => x.Email == "");
        worker.Email = workerEmail;
        await worker.Update(sdkDbContext).ConfigureAwait(false);
        var workerId = worker.Id;

        var groupNamesBefore = await GetSecurityGroupNamesAsync(loginAccount.Id);

        var currentSite = await MicrotingDbContext!.Sites.OrderByDescending(x => x.Id).FirstAsync();
        var newFirstName = Guid.NewGuid().ToString();
        var newLastName = Guid.NewGuid().ToString();
        var newDeviceUserModel = new DeviceUserModel
        {
            SiteMicrotingUid = (int)currentSite.MicrotingUid!,
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = false,
            WebAccessEnabled = true, // would normally add real group membership
            UserFirstName = newFirstName,
            UserLastName = newLastName,
            WorkerEmail = workerEmail // unchanged
        };

        // Act
        var result = await BackendConfigurationAssignmentWorkerServiceHelper.UpdateDeviceUser(newDeviceUserModel, core, 1,
            userService, userManager,
            BackendConfigurationPnDbContext!,
            TimePlanningPnDbContext!, BaseDbContext!, logger, ItemsPlanningPnDbContext!);

        // Assert - the worker's own SDK fields still save...
        Assert.That(result.Success, Is.True, result.Message);
        var updatedWorker = await MicrotingDbContext.Workers.SingleAsync(x => x.Id == workerId);
        Assert.That(updatedWorker.FirstName, Is.EqualTo(newFirstName));
        Assert.That(updatedWorker.LastName, Is.EqualTo(newLastName));

        // ...but the id-1 login is completely untouched, and gains no group
        // membership from this save.
        var reloadedLogin = await BaseDbContext!.Users.SingleAsync(x => x.Id == loginAccount.Id);
        Assert.That(reloadedLogin.UserName, Is.EqualTo(originalUserName));
        Assert.That(reloadedLogin.FirstName, Is.EqualTo(originalFirstName));
        Assert.That(reloadedLogin.Locale, Is.EqualTo(originalLocale));
        var groupNamesAfter = await GetSecurityGroupNamesAsync(loginAccount.Id);
        Assert.That(groupNamesAfter, Is.EquivalentTo(groupNamesBefore),
            "the id-1 login's group membership must be completely unchanged by this save");
        Assert.That(groupNamesAfter, Does.Not.Contain("eForm users"),
            "WebAccessEnabled must not have added group membership to the id-1 login");
    }

    // A worker whose resolved login is an admin account still has a CHANGING
    // address validated: the admin branch runs ValidateCandidateEmailAsync
    // with existingUserId: 0 and refuses a collision, even though no login
    // write follows. Same rule as
    // TargetEmailOwnedByAnotherAccount_RefusesBeforeAnyWrite on the non-admin path.
    [Test]
    public async Task BackendConfigurationAssignmentWorkerServiceHelper_UpdateDeviceUser_ResolvedLoginIsAdmin_ChangingToOwnedEmail_Refuses()
    {
        // Arrange
        var core = await GetCore();
        var logger = Substitute.For<ILogger>();
        // CreateProductionLikeUserManager, NOT CreateRealUserManager: otherAccount
        // owns ownedEmail only as its EMAIL, under a different UserName. With
        // Identity's default RequireUniqueEmail = false that address is never
        // detected as taken, so this test would fail even on correct code.
        // Production sets RequireUniqueEmail = true, which this factory mirrors.
        var userManager = IdentityTestUtils.CreateProductionLikeUserManager(BaseDbContext!);
        var userService = IdentityTestUtils.CreateRealUserService(BaseDbContext!, userManager);

        var workerEmail = $"{Guid.NewGuid()}@test.com";
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
            WorkerEmail = workerEmail
        };
        await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            TimePlanningPnDbContext!, BaseDbContext!, userService, userManager);

        var loginAccount = await BaseDbContext!.Users.SingleAsync(x => x.Email == workerEmail);
        var addToRole = await userManager.AddToRoleAsync(loginAccount, EformRole.Admin);
        Assert.That(addToRole.Succeeded, Is.True, string.Join(",", addToRole.Errors.Select(e => e.Description)));

        // A second, unrelated account already owns the address this update
        // requests for the admin-owned row's worker.
        var ownedEmail = $"{Guid.NewGuid()}@already-owned.test";
        var otherAccount = new EformUser
        {
            Email = ownedEmail,
            UserName = $"other-{Guid.NewGuid()}",
            FirstName = "Other",
            LastName = "Owner",
            Locale = "da",
            EmailConfirmed = true,
            TimeZone = "Europe/Copenhagen",
            Formats = "de-DE"
        };
        Assert.That((await userManager.CreateAsync(otherAccount)).Succeeded, Is.True);
        var otherAccountUserName = otherAccount.UserName;

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
            WorkerEmail = ownedEmail // CHANGING, and already owned by otherAccount
        };

        // Act
        var result = await BackendConfigurationAssignmentWorkerServiceHelper.UpdateDeviceUser(newDeviceUserModel, core, 1,
            userService, userManager,
            BackendConfigurationPnDbContext!,
            TimePlanningPnDbContext!, BaseDbContext!, logger, ItemsPlanningPnDbContext!);

        // Assert - refused before any write: the SDK worker keeps its old
        // address and otherAccount is unchanged.
        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo("EmailIsAlreadyInUse"));
        var untouchedWorker = await MicrotingDbContext!.Workers.SingleAsync(x => x.Email == workerEmail);
        Assert.That(untouchedWorker.Email, Is.EqualTo(workerEmail),
            "the SDK worker must keep its OLD email - a changing address that collides is refused even " +
            "when login work is skipped for the admin account");
        var reloadedOther = await BaseDbContext.Users.SingleAsync(x => x.Id == otherAccount.Id);
        Assert.That(reloadedOther.UserName, Is.EqualTo(otherAccountUserName),
            "the other account must be completely untouched by a refused save");
    }

    // Resigning a worker that is still assigned is refused before any write.
    // If this check ran after the SiteTag delete/create, the reconciliation
    // dispatch and core.SiteUpdate/worker.Update, "change email A->B and tick
    // Resigned while still assigned" would be refused only after the SDK
    // worker already held B while the login stayed at A. So the check runs
    // above every write and is computed from the REQUESTED tag set
    // (deviceUserModel.Tags), not the post-write SiteTags, and a refusal
    // leaves the worker's email, tags and login all unchanged.
    [Test]
    public async Task BackendConfigurationAssignmentWorkerServiceHelper_UpdateDeviceUser_ResignWhileStillAssigned_RefusesBeforeAnyWrite()
    {
        // Arrange
        var core = await GetCore();
        var logger = Substitute.For<ILogger>();
        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);
        var userService = IdentityTestUtils.CreateRealUserService(BaseDbContext!, userManager);

        var emailA = $"{Guid.NewGuid()}@test.com";
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
            WorkerEmail = emailA
        };
        await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            TimePlanningPnDbContext!, BaseDbContext!, userService, userManager);

        var currentSite = await MicrotingDbContext!.Sites.OrderByDescending(x => x.Id).FirstAsync();
        var loginAccount = await BaseDbContext!.Users.SingleAsync(x => x.Email == emailA);
        var originalUserName = loginAccount.UserName;

        // A real, active AreaRulePlanning whose AreaRulePlanningWorkerTag
        // references a tag this worker is about to REQUEST (not one it
        // already carries as a SiteTag) - "still assigned" must be computed
        // from the requested set, so seeding it this way (rather
        // than as a pre-existing SiteTag) is what proves that half of it.
        var tagId = await SeedSdkTag();
        var arpId = await SeedActiveAreaRulePlanning();
        await new AreaRulePlanningWorkerTag { AreaRulePlanningId = arpId, TagId = tagId }
            .Create(BackendConfigurationPnDbContext!);

        var emailB = $"{Guid.NewGuid()}@test.com";
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
            Tags = [tagId],
            WorkerEmail = emailB, // CHANGING
            Resigned = true // and resigning, while still effectively assigned via the requested tag
        };

        // Act
        var result = await BackendConfigurationAssignmentWorkerServiceHelper.UpdateDeviceUser(newDeviceUserModel, core, 1,
            userService, userManager,
            BackendConfigurationPnDbContext!,
            TimePlanningPnDbContext!, BaseDbContext!, logger, ItemsPlanningPnDbContext!);

        // Assert - refused, and NOTHING moved: not the worker's email, not its
        // tags, not its login.
        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo("WorkerStillAssignedToEventsCannotResign"));
        var untouchedWorker = await MicrotingDbContext!.Workers.SingleAsync(x => x.Email == emailA);
        Assert.That(untouchedWorker.Email, Is.EqualTo(emailA),
            "a refused resign must not have written the new email to the SDK worker");
        Assert.That(untouchedWorker.Resigned, Is.False);
        var sdkDbContext = core.DbContextHelper.GetDbContext();
        await AssertNoActiveSiteTagsAsync(sdkDbContext, currentSite.Id);
        var reloadedLogin = await BaseDbContext.Users.SingleAsync(x => x.Id == loginAccount.Id);
        Assert.That(reloadedLogin.UserName, Is.EqualTo(originalUserName),
            "the worker's own login must be untouched - it must still resolve via the OLD email on the next save");
        Assert.That(await BaseDbContext.Users.AnyAsync(x => x.Email == emailB), Is.False,
            "no login may exist under the address the refused save tried to move to");
    }

    // No login means no "Kun tid" membership write. A worker can have
    // TimeRegistrationEnabled == true with no WorkerEmail — no ModelState
    // validation stops it, DeviceUserModel.WorkerEmail is nullable — in which
    // case no EformUser is created and `user` stays null, so the "Kun tid"
    // SecurityGroupUser write for time registration must be skipped rather
    // than dereference it.
    [Test]
    public async Task BackendConfigurationAssignmentWorkerServiceHelper_CreateDeviceUser_TimeRegistrationEnabledNoEmail_DoesNotThrowAndCreatesNoUser()
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
            WorkerEmail = ""
        };

        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);
        var userService = IdentityTestUtils.CreateRealUserService(BaseDbContext!, userManager);

        var usersBefore = await BaseDbContext!.Users.CountAsync();

        // Act — must not throw an NRE while writing the "Kun tid" membership for a
        // worker that never got an EformUser.
        var result = await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            TimePlanningPnDbContext!, BaseDbContext!, userService, userManager);

        // Assert — once guarded, CreateDeviceUser still completes the AssignedSite
        // creation and reports success; it just skips the login and its membership.
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True, result.Message);

        var usersAfter = await BaseDbContext.Users.CountAsync();
        Assert.That(usersAfter, Is.EqualTo(usersBefore),
            "a worker with no email must not get an EformUser, even with time registration enabled");

        var zeroIdMemberships = await BaseDbContext.SecurityGroupUsers
            .CountAsync(x => x.EformUserId == 0);
        Assert.That(zeroIdMemberships, Is.EqualTo(0),
            "no SecurityGroupUser row should ever be written against a non-existent user");
    }

    // A worker whose Email is NULL (not "") must remain editable. Real
    // population, not hypothetical: core's own DeviceUsersService passed a
    // null email to core.SiteCreate until eform-angular-frontend commit
    // 36f3a82c8 (Jan 2026), and SQL/420_SDK.sql seeds one too (Worker id 2).
    // userManager.FindByNameAsync throws ArgumentNullException on a null
    // username, which the outer catch would turn into
    // "DeviceUserCouldNotBeUpdated", leaving the worker permanently uneditable.
    // FindLoginWithoutSideEffectsAsync returns null for a null/empty email
    // instead of calling FindByNameAsync at all.
    [Test]
    public async Task BackendConfigurationAssignmentWorkerServiceHelper_UpdateDeviceUser_NullEmail_IsStillEditable()
    {
        // Arrange
        var core = await GetCore();
        var logger = Substitute.For<ILogger>();
        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);
        var userService = IdentityTestUtils.CreateRealUserService(BaseDbContext!, userManager);

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
            WorkerEmail = "" // forced to NULL below, once the worker is found by this "" address
        };
        await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            TimePlanningPnDbContext!, BaseDbContext!, userService, userManager);

        // Simulate the real population directly: force the SDK worker's Email
        // to NULL, exactly like SQL/420_SDK.sql's seeded Worker id 2.
        var sdkDbContext = core.DbContextHelper.GetDbContext();
        var worker = await sdkDbContext.Workers.SingleAsync(x => x.Email == "");
        worker.Email = null;
        await worker.Update(sdkDbContext).ConfigureAwait(false);

        var currentSite = await MicrotingDbContext!.Sites.OrderByDescending(x => x.Id).FirstAsync();
        var newFirstName = Guid.NewGuid().ToString();
        var newDeviceUserModel = new DeviceUserModel
        {
            SiteMicrotingUid = (int)currentSite.MicrotingUid!,
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = false,
            UserFirstName = newFirstName,
            UserLastName = Guid.NewGuid().ToString(),
            WorkerEmail = "" // still no real address - not the point under test
        };

        // Act - must not throw, and must not report the generic
        // "DeviceUserCouldNotBeUpdated" an unguarded ArgumentNullException
        // would produce via the outer catch.
        var result = await BackendConfigurationAssignmentWorkerServiceHelper.UpdateDeviceUser(newDeviceUserModel, core, 1,
            userService, userManager,
            BackendConfigurationPnDbContext!,
            TimePlanningPnDbContext!, BaseDbContext!, logger, ItemsPlanningPnDbContext!);

        // Assert
        Assert.That(result.Success, Is.True, result.Message);
        // Read back through MicrotingDbContext, NOT the sdkDbContext instance
        // through which the Arrange step fetched and mutated `worker` - that
        // instance's change tracker still holds the ORIGINAL FirstName on the
        // exact same tracked entity, so re-querying it (identity map hit)
        // would return that stale in-memory value rather than what the
        // Act call actually persisted, regardless of whether the save worked.
        var updatedWorker = await MicrotingDbContext!.Workers.SingleAsync(x => x.Id == worker.Id);
        Assert.That(updatedWorker.FirstName, Is.EqualTo(newFirstName),
            "a worker whose Email was NULL must still be editable");
    }

    // UpdateDeviceUser's SDK duplicate-email check compares against the
    // substituted synthetic address, never the raw "": a request with
    // WorkerEmail == "" must not be refused EmailIsAlreadyInUse just because
    // ANOTHER live worker's Email is ALSO "". SQL/420_SDK.sql's seeded
    // null-email Worker id 2 cannot exercise this ("" != NULL at the SQL
    // level), so this needs a SECOND worker whose Email is the literal empty
    // string.
    [Test]
    public async Task BackendConfigurationAssignmentWorkerServiceHelper_UpdateDeviceUser_SecondLiveWorkerWithEmptyEmail_StillEditable()
    {
        // Arrange
        var core = await GetCore();
        var logger = Substitute.For<ILogger>();
        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);
        var userService = IdentityTestUtils.CreateRealUserService(BaseDbContext!, userManager);

        // First worker: left at WorkerEmail == "" and never touched again -
        // its ROLE here is purely to be the "another live worker with Email
        // == ''" that a duplicate check on the raw "" would collide against.
        var otherDeviceUserModel = new DeviceUserModel
        {
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = false,
            UserFirstName = Guid.NewGuid().ToString(),
            UserLastName = Guid.NewGuid().ToString(),
            WorkerEmail = ""
        };
        var otherCreateResult = await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(
            otherDeviceUserModel, core, 1, TimePlanningPnDbContext!, BaseDbContext!, userService, userManager);
        Assert.That(otherCreateResult.Success, Is.True, otherCreateResult.Message);

        // Second worker: this is the one under test. It is created with a
        // real address, then forced to "" directly on the SDK side afterwards,
        // exactly like the NullEmail test's "simulate a real population
        // directly" approach - so this test depends only on UpdateDeviceUser.
        var subjectDeviceUserModel = new DeviceUserModel
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
        var subjectCreateResult = await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(
            subjectDeviceUserModel, core, 1, TimePlanningPnDbContext!, BaseDbContext!, userService, userManager);
        Assert.That(subjectCreateResult.Success, Is.True, subjectCreateResult.Message);

        var sdkDbContext = core.DbContextHelper.GetDbContext();
        var subjectWorker = await sdkDbContext.Workers
            .SingleAsync(x => x.Email == subjectDeviceUserModel.WorkerEmail);
        subjectWorker.Email = "";
        await subjectWorker.Update(sdkDbContext).ConfigureAwait(false);
        var subjectWorkerId = subjectWorker.Id;
        // The subject was created SECOND, so it is the most recently created
        // site - the same pattern every other test in this file uses to find
        // "the site just created".
        var subjectSite = await MicrotingDbContext!.Sites.OrderByDescending(x => x.Id).FirstAsync();

        var newFirstName = Guid.NewGuid().ToString();
        var newDeviceUserModel = new DeviceUserModel
        {
            SiteMicrotingUid = (int)subjectSite.MicrotingUid!,
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = false,
            UserFirstName = newFirstName,
            UserLastName = Guid.NewGuid().ToString(),
            WorkerEmail = "" // still empty - the OTHER worker's Email is ALSO ""
        };

        // Act - a second live worker sharing the raw, pre-substitution ""
        // address must not block this save.
        var result = await BackendConfigurationAssignmentWorkerServiceHelper.UpdateDeviceUser(newDeviceUserModel, core, 1,
            userService, userManager,
            BackendConfigurationPnDbContext!,
            TimePlanningPnDbContext!, BaseDbContext!, logger, ItemsPlanningPnDbContext!);

        // Assert
        Assert.That(result.Success, Is.True, result.Message);
        var updatedWorker = await MicrotingDbContext.Workers.SingleAsync(x => x.Id == subjectWorkerId);
        Assert.That(updatedWorker.FirstName, Is.EqualTo(newFirstName),
            "a worker with an empty Email must stay editable even when another live worker also has Email == \"\"");
    }

    // An empty address cannot collide, so CreateDeviceUser's SDK duplicate-email
    // check must not refuse a second worker just because another live worker's
    // Email is also "".
    [Test]
    public async Task BackendConfigurationAssignmentWorkerServiceHelper_CreateDeviceUser_SecondWorkerWithEmptyEmail_IsCreated()
    {
        // Arrange
        var core = await GetCore();
        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);
        var userService = IdentityTestUtils.CreateRealUserService(BaseDbContext!, userManager);

        DeviceUserModel EmptyEmailWorker() => new()
        {
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = false,
            UserFirstName = Guid.NewGuid().ToString(),
            UserLastName = Guid.NewGuid().ToString(),
            WorkerEmail = ""
        };
        var firstResult = await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(
            EmptyEmailWorker(), core, 1, TimePlanningPnDbContext!, BaseDbContext!, userService, userManager);
        Assert.That(firstResult.Success, Is.True, firstResult.Message);

        // Act
        var secondResult = await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(
            EmptyEmailWorker(), core, 1, TimePlanningPnDbContext!, BaseDbContext!, userService, userManager);

        // Assert
        Assert.That(secondResult.Success, Is.True, secondResult.Message);
        var sdkDbContext = core.DbContextHelper.GetDbContext();
        Assert.That(await sdkDbContext.Workers.CountAsync(x => x.Email == ""
                && x.WorkflowState != Constants.WorkflowStates.Removed), Is.EqualTo(2),
            "both workers with an empty Email must be live");
    }

    // The same rule for a NULL address. SQL/420_SDK.sql seeds a live Worker (id 2)
    // whose Email is NULL, so a single create with a null WorkerEmail already has
    // another live worker sharing its address.
    [Test]
    public async Task BackendConfigurationAssignmentWorkerServiceHelper_CreateDeviceUser_SecondWorkerWithNullEmail_IsCreated()
    {
        // Arrange
        var core = await GetCore();
        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);
        var userService = IdentityTestUtils.CreateRealUserService(BaseDbContext!, userManager);
        Assert.That(await MicrotingDbContext!.Workers.AsNoTracking().AnyAsync(x => x.Email == null
                && x.WorkflowState != Constants.WorkflowStates.Removed), Is.True,
            "sanity: a live worker with a NULL Email must already exist");

        var workerFirstName = Guid.NewGuid().ToString();
        var deviceUserModel = new DeviceUserModel
        {
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = false,
            UserFirstName = workerFirstName,
            UserLastName = Guid.NewGuid().ToString(),
            WorkerEmail = null
        };

        // Act
        var result = await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(
            deviceUserModel, core, 1, TimePlanningPnDbContext!, BaseDbContext!, userService, userManager);

        // Assert
        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(await MicrotingDbContext.Workers.AsNoTracking().AnyAsync(x => x.FirstName == workerFirstName
                && x.WorkflowState != Constants.WorkflowStates.Removed), Is.True,
            "a worker with a null Email must be created even though another live worker's Email is also NULL");
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

        // Newly-created AssignedSite defaults UseOneMinuteIntervals to false when the
        // DeviceUserModel does not specify it (CreateDeviceUser path)
        Assert.That(timeregistrationSiteAssignments[30].UseOneMinuteIntervals, Is.False);
    }

    // Should test the CreateDeviceUser method with TimeRegistrationEnabled, UseOneMinuteIntervals and
    // PayRuleSetId set, verifying both new fields pass through onto the created AssignedSite
    [Test]
    public async Task
        BackendConfigurationAssignmentWorkerServiceHelper_CreateDeviceUser_TimeRegistrationEnabled_UseOneMinuteIntervalsAndPayRuleSetId_ReturnsSuccess()
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
            UseOneMinuteIntervals = true,
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

        // Newly-created AssignedSite must pass through UseOneMinuteIntervals and PayRuleSetId from the DeviceUserModel
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

        // Newly-created AssignedSite defaults UseOneMinuteIntervals to false when the
        // DeviceUserModel does not specify it (UpdateDeviceUser create path)
        Assert.That(timeregistrationSiteAssignments[30].UseOneMinuteIntervals, Is.False);
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

    // "none" exists so a worker with no other group is still a legal
    // account-management user (AdminService.Create rejects a non-admin whose
    // GroupId does not resolve). It must carry no permissions of its own.
    [Test]
    public async Task BackendConfigurationAssignmentWorkerServiceHelper_EnsureFallbackSecurityGroup_AddsNoneWhenGroupless()
    {
        // Arrange
        var user = new EformUser
        {
            Email = $"{Guid.NewGuid()}@test.com",
            UserName = $"{Guid.NewGuid()}@test.com",
            FirstName = Guid.NewGuid().ToString(),
            LastName = Guid.NewGuid().ToString(),
            Locale = "da",
            EmailConfirmed = true,
            TimeZone = "Europe/Copenhagen",
            Formats = "de-DE"
        };
        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);
        Assert.That((await userManager.CreateAsync(user)).Succeeded, Is.True);

        // Act
        await BackendConfigurationAssignmentWorkerServiceHelper
            .EnsureFallbackSecurityGroupAsync(BaseDbContext!, user.Id);

        // Assert
        var groupNames = await GetSecurityGroupNamesAsync(user.Id);
        Assert.That(groupNames, Is.EqualTo(new List<string>
            { BackendConfigurationAssignmentWorkerServiceHelper.NoPermissionsSecurityGroupName }));

        // The group grants nothing: no core GroupPermission rows.
        var noneGroupId = await BaseDbContext.SecurityGroups
            .Where(x => x.Name == BackendConfigurationAssignmentWorkerServiceHelper.NoPermissionsSecurityGroupName)
            .Select(x => x.Id).FirstAsync();
        Assert.That(await BaseDbContext.GroupPermissions.CountAsync(x => x.SecurityGroupId == noneGroupId),
            Is.Zero);
    }

    // Core strips group memberships from admins, so a groupless admin is
    // expected, ordinary state - not "no permissions granted yet". The primary
    // admin (id 1) was already skipped; this pins that the SAME rule applies to
    // any OTHER admin, matching SecurityGroupBackfillService.
    // AssignFallbackGroupToGrouplessUsersAsync's own admin exclusion. Without
    // the role check this call added, a groupless secondary admin saved through
    // CreateDeviceUser/UpdateDeviceUser would be put in "none" - a state the
    // backfill deliberately avoids and would list that admin under /security.
    [Test]
    public async Task BackendConfigurationAssignmentWorkerServiceHelper_EnsureFallbackSecurityGroup_LeavesOtherAdminsGroupless()
    {
        // Arrange
        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);

        // ReserveEformUserId1 keeps admin off id 1, which EnsureFallbackSecurityGroupAsync
        // skips by id alone - so only the admin-ROLE check can leave it groupless.
        var admin = new EformUser
        {
            Email = $"{Guid.NewGuid()}@test.com",
            UserName = $"{Guid.NewGuid()}@test.com",
            FirstName = Guid.NewGuid().ToString(),
            LastName = Guid.NewGuid().ToString(),
            Locale = "da",
            EmailConfirmed = true,
            TimeZone = "Europe/Copenhagen",
            Formats = "de-DE"
        };
        Assert.That((await userManager.CreateAsync(admin)).Succeeded, Is.True);
        var addToRole = await userManager.AddToRoleAsync(admin, EformRole.Admin);
        Assert.That(addToRole.Succeeded, Is.True,
            string.Join(",", addToRole.Errors.Select(e => e.Description)));
        Assert.That(admin.Id, Is.Not.EqualTo(1),
            "precondition: this must be a SECONDARY admin, not the one already skipped by id");

        // Act
        var added = await BackendConfigurationAssignmentWorkerServiceHelper
            .EnsureFallbackSecurityGroupAsync(BaseDbContext!, admin.Id);

        // Assert
        Assert.That(added, Is.False, "an admin must never be reported as moved into the fallback group");
        var groupNames = await GetSecurityGroupNamesAsync(admin.Id);
        Assert.That(groupNames, Is.Empty, "an admin must not be dragged into \"none\"");
    }

    // The rule is "none UNLESS another group": a user who has a real group must
    // not also sit in none, and must have it taken away if they had it.
    [Test]
    public async Task BackendConfigurationAssignmentWorkerServiceHelper_EnsureFallbackSecurityGroup_RemovesNoneWhenOtherGroupPresent()
    {
        // Arrange
        var user = new EformUser
        {
            Email = $"{Guid.NewGuid()}@test.com",
            UserName = $"{Guid.NewGuid()}@test.com",
            FirstName = Guid.NewGuid().ToString(),
            LastName = Guid.NewGuid().ToString(),
            Locale = "da",
            EmailConfirmed = true,
            TimeZone = "Europe/Copenhagen",
            Formats = "de-DE"
        };
        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);
        Assert.That((await userManager.CreateAsync(user)).Succeeded, Is.True);

        // Starts groupless, so gains "none".
        await BackendConfigurationAssignmentWorkerServiceHelper
            .EnsureFallbackSecurityGroupAsync(BaseDbContext!, user.Id);

        // Then gains a real group, the way the WebAccessEnabled sync does.
        var realGroupId = await BaseDbContext!.SecurityGroups
            .Where(x => x.Name == "eForm users").Select(x => x.Id).FirstAsync();
        BaseDbContext.SecurityGroupUsers.Add(new SecurityGroupUser
            { EformUserId = user.Id, SecurityGroupId = realGroupId });
        await BaseDbContext.SaveChangesAsync();

        // Act
        await BackendConfigurationAssignmentWorkerServiceHelper
            .EnsureFallbackSecurityGroupAsync(BaseDbContext, user.Id);

        // Assert
        var groupNames = await GetSecurityGroupNamesAsync(user.Id);
        Assert.That(groupNames, Is.EqualTo(new List<string> { "eForm users" }));
    }

    // Calling twice must not create a second membership row: both device-user
    // paths call this on every save.
    [Test]
    public async Task BackendConfigurationAssignmentWorkerServiceHelper_EnsureFallbackSecurityGroup_IsIdempotent()
    {
        // Arrange
        var user = new EformUser
        {
            Email = $"{Guid.NewGuid()}@test.com",
            UserName = $"{Guid.NewGuid()}@test.com",
            FirstName = Guid.NewGuid().ToString(),
            LastName = Guid.NewGuid().ToString(),
            Locale = "da",
            EmailConfirmed = true,
            TimeZone = "Europe/Copenhagen",
            Formats = "de-DE"
        };
        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);
        Assert.That((await userManager.CreateAsync(user)).Succeeded, Is.True);

        // Act
        await BackendConfigurationAssignmentWorkerServiceHelper
            .EnsureFallbackSecurityGroupAsync(BaseDbContext!, user.Id);
        await BackendConfigurationAssignmentWorkerServiceHelper
            .EnsureFallbackSecurityGroupAsync(BaseDbContext!, user.Id);

        // Assert
        Assert.That(await BaseDbContext!.SecurityGroupUsers.CountAsync(x => x.EformUserId == user.Id),
            Is.EqualTo(1));
    }

    // SecurityGroup.Name carries no index at all, so two "none" rows with distinct
    // ids can coexist (GetOrCreateSecurityGroupId's check-then-act is not the only
    // way to get there — a stray row is enough). EnsureFallbackSecurityGroupAsync
    // must remove EVERY "none" membership once a real group is present, not just
    // the one whose id happens to match what GetOrCreateSecurityGroupId resolves
    // this call. This test deliberately attaches the user's "none" membership to
    // the row the lookup does NOT return, so a naive id-based comparison
    // (SecurityGroupId == resolvedNoneGroupId) would find no "none" memberships to
    // remove and leave the user in both "eForm users" and the stale "none" group.
    [Test]
    public async Task BackendConfigurationAssignmentWorkerServiceHelper_EnsureFallbackSecurityGroup_RemovesNoneMembershipPointingAtDuplicateGroup()
    {
        // Arrange
        var user = new EformUser
        {
            Email = $"{Guid.NewGuid()}@test.com",
            UserName = $"{Guid.NewGuid()}@test.com",
            FirstName = Guid.NewGuid().ToString(),
            LastName = Guid.NewGuid().ToString(),
            Locale = "da",
            EmailConfirmed = true,
            TimeZone = "Europe/Copenhagen",
            Formats = "de-DE"
        };
        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);
        Assert.That((await userManager.CreateAsync(user)).Succeeded, Is.True);

        // Two "none" rows coexist before the helper ever runs.
        var noneGroupA = new SecurityGroup { Name = BackendConfigurationAssignmentWorkerServiceHelper.NoPermissionsSecurityGroupName };
        var noneGroupB = new SecurityGroup { Name = BackendConfigurationAssignmentWorkerServiceHelper.NoPermissionsSecurityGroupName };
        BaseDbContext!.SecurityGroups.Add(noneGroupA);
        BaseDbContext.SecurityGroups.Add(noneGroupB);
        await BaseDbContext.SaveChangesAsync();

        // Replicate GetOrCreateSecurityGroupId's own lookup (FirstOrDefaultAsync,
        // no OrderBy) so the test does not assume which of the two rows it returns.
        var resolvedNoneGroupId = await BaseDbContext.SecurityGroups
            .Where(x => x.Name == BackendConfigurationAssignmentWorkerServiceHelper.NoPermissionsSecurityGroupName)
            .Select(x => x.Id)
            .FirstOrDefaultAsync();
        Assert.That(noneGroupA.Id, Is.Not.EqualTo(noneGroupB.Id));

        // Attach the user's "none" membership to whichever row the lookup will NOT
        // return, plus a real group — a combination that matching "none" by a single
        // resolved group id would miss.
        var unresolvedNoneGroupId = resolvedNoneGroupId == noneGroupA.Id ? noneGroupB.Id : noneGroupA.Id;
        Assert.That(unresolvedNoneGroupId, Is.Not.EqualTo(resolvedNoneGroupId));

        var realGroupId = await BaseDbContext.SecurityGroups
            .Where(x => x.Name == "eForm users").Select(x => x.Id).FirstAsync();

        BaseDbContext.SecurityGroupUsers.Add(new SecurityGroupUser
            { EformUserId = user.Id, SecurityGroupId = unresolvedNoneGroupId });
        BaseDbContext.SecurityGroupUsers.Add(new SecurityGroupUser
            { EformUserId = user.Id, SecurityGroupId = realGroupId });
        await BaseDbContext.SaveChangesAsync();

        // Act
        await BackendConfigurationAssignmentWorkerServiceHelper
            .EnsureFallbackSecurityGroupAsync(BaseDbContext, user.Id);

        // Assert: the stale "none" membership is gone; only the real group remains.
        var groupNamesAfterCall = await GetSecurityGroupNamesAsync(user.Id);
        Assert.That(groupNamesAfterCall, Is.EqualTo(new List<string> { "eForm users" }));
        Assert.That(groupNamesAfterCall, Has.None.EqualTo(BackendConfigurationAssignmentWorkerServiceHelper.NoPermissionsSecurityGroupName));
    }

    // The end state of an email-only worker: a login that belongs to "none" and
    // nothing else.
    [Test]
    public async Task BackendConfigurationAssignmentWorkerServiceHelper_CreateDeviceUser_EmailOnly_LandsInNoneGroup()
    {
        // Arrange
        var core = await GetCore();
        var workerEmail = $"{Guid.NewGuid()}@test.com";
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
            WorkerEmail = workerEmail
        };

        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);

        // Act
        var result = await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            TimePlanningPnDbContext!, BaseDbContext!, userService, userManager);

        // Assert
        Assert.That(result.Success, Is.True, result.Message);
        var createdUser = await BaseDbContext!.Users.SingleAsync(x => x.Email == workerEmail);
        var groupNames = await GetSecurityGroupNamesAsync(createdUser.Id);
        Assert.That(groupNames, Is.EqualTo(new List<string>
            { BackendConfigurationAssignmentWorkerServiceHelper.NoPermissionsSecurityGroupName }));
    }

    // Granting web access must move the user out of "none" into the real group,
    // which is what makes "none UNLESS another group" true over time and not just
    // at creation.
    [Test]
    public async Task BackendConfigurationAssignmentWorkerServiceHelper_CreateDeviceUser_WebAccessEnabled_DoesNotLandInNoneGroup()
    {
        // Arrange
        var core = await GetCore();
        var workerEmail = $"{Guid.NewGuid()}@test.com";
        var deviceUserModel = new DeviceUserModel
        {
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = false,
            WebAccessEnabled = true,
            UserFirstName = Guid.NewGuid().ToString(),
            UserLastName = Guid.NewGuid().ToString(),
            WorkerEmail = workerEmail
        };

        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);

        // Act
        var result = await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            TimePlanningPnDbContext!, BaseDbContext!, userService, userManager);

        // Assert
        Assert.That(result.Success, Is.True, result.Message);
        var createdUser = await BaseDbContext!.Users.SingleAsync(x => x.Email == workerEmail);
        var groupNames = await GetSecurityGroupNamesAsync(createdUser.Id);
        Assert.That(groupNames, Does.Contain("eForm users"));
        Assert.That(groupNames, Does.Not.Contain(
            BackendConfigurationAssignmentWorkerServiceHelper.NoPermissionsSecurityGroupName));
    }

    // Pins the fallback call at UpdateDeviceUser's TAIL return (the final
    // `return isUpdated ? ... : ...`). Reached here via the path where
    // TimeRegistrationEnabled stays false and no AssignedSite exists yet, so
    // the `TimeRegistrationEnabled == false && AssignedSites.Any(...)` branch
    // is false and the else-branch's inner "TimeRegistrationEnabled == true"
    // check is also false — control falls straight through to the tail
    // return.
    //
    // Deliberately does NOT create with an empty flag set: that would seed
    // "none" at creation time (CreateDeviceUser's own fallback call), so the
    // update call's own fallback call would be redundant and deleting it
    // would leave the assertion passing unchanged, making the test vacuous.
    // Instead the worker is created holding a
    // real group ("eForm users", via WebAccessEnabled), which keeps "none"
    // OFF (guard-asserted below), then the update strips that real group via
    // WebAccessEnabled = false — a mechanism the shared eForm-users sync
    // block provides, not the tail-return fallback under test — leaving the
    // user in NO group at all when the tail return is reached. Only the
    // fallback call under test can turn that into ["none"]; without it the
    // user would be left with an empty group list.
    [Test]
    public async Task BackendConfigurationAssignmentWorkerServiceHelper_UpdateDeviceUser_TailReturn_NothingChanged_LandsInNoneGroup()
    {
        // Arrange
        var core = await GetCore();
        var logger = Substitute.For<ILogger>();
        var workerEmail = $"{Guid.NewGuid()}@test.com";

        var deviceUserModel = new DeviceUserModel
        {
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = false,
            WebAccessEnabled = true,
            UserFirstName = Guid.NewGuid().ToString(),
            UserLastName = Guid.NewGuid().ToString(),
            WorkerEmail = workerEmail
        };

        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);
        var userService = IdentityTestUtils.CreateRealUserService(BaseDbContext!, userManager);

        await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            TimePlanningPnDbContext!, BaseDbContext!, userService, userManager);

        var createdUser = await BaseDbContext!.Users.SingleAsync(x => x.Email == workerEmail);
        var currentSite = await MicrotingDbContext!.Sites.OrderByDescending(x => x.Id).FirstAsync();

        // Guard: the worker must hold the real "eForm users" group and NOT
        // "none" before the call under test — otherwise this test cannot
        // distinguish "the fallback ran" from "none was already sitting
        // there from creation".
        var groupNamesBeforeUpdate = await GetSecurityGroupNamesAsync(createdUser.Id);
        Assert.That(groupNamesBeforeUpdate, Is.EqualTo(new List<string> { "eForm users" }),
            "precondition: the worker must hold a real group and not \"none\" before UpdateDeviceUser runs");

        var newDeviceUserModel = new DeviceUserModel
        {
            SiteMicrotingUid = (int)currentSite.MicrotingUid!,
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = false,
            WebAccessEnabled = false,
            UserFirstName = Guid.NewGuid().ToString(),
            UserLastName = Guid.NewGuid().ToString(),
            WorkerEmail = $"{Guid.NewGuid()}@test.com"
        };

        // Act
        var result = await BackendConfigurationAssignmentWorkerServiceHelper.UpdateDeviceUser(newDeviceUserModel, core, 1,
            userService, userManager,
            BackendConfigurationPnDbContext!,
            TimePlanningPnDbContext!, BaseDbContext!, logger, ItemsPlanningPnDbContext!);

        // Assert
        Assert.That(result.Success, Is.True, result.Message);
        var groupNames = await GetSecurityGroupNamesAsync(createdUser.Id);
        Assert.That(groupNames, Is.EqualTo(new List<string>
            { BackendConfigurationAssignmentWorkerServiceHelper.NoPermissionsSecurityGroupName }));
    }

    // Pins the fallback call at UpdateDeviceUser's TAIL return in the
    // opposite direction from the test above: granting WebAccessEnabled
    // must remove "none" once the "eForm users" sync (which runs upstream of
    // the same tail return) adds a real group. This is the one that would
    // catch a fallback call placed above the eForm users/archive sync block
    // instead of below it.
    [Test]
    public async Task BackendConfigurationAssignmentWorkerServiceHelper_UpdateDeviceUser_WebAccessEnabled_DoesNotLandInNoneGroup()
    {
        // Arrange
        var core = await GetCore();
        var logger = Substitute.For<ILogger>();
        var workerEmail = $"{Guid.NewGuid()}@test.com";

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
            WorkerEmail = workerEmail
        };

        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);
        var userService = IdentityTestUtils.CreateRealUserService(BaseDbContext!, userManager);

        await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            TimePlanningPnDbContext!, BaseDbContext!, userService, userManager);

        var createdUser = await BaseDbContext!.Users.SingleAsync(x => x.Email == workerEmail);
        var currentSite = await MicrotingDbContext!.Sites.OrderByDescending(x => x.Id).FirstAsync();

        // Guard: the worker must hold "none" and NOT a real group before the
        // call under test - otherwise this test cannot distinguish "the
        // fallback removed none" from "none was never there to begin with".
        var groupNamesBeforeUpdate = await GetSecurityGroupNamesAsync(createdUser.Id);
        Assert.That(groupNamesBeforeUpdate, Is.EqualTo(new List<string>
                { BackendConfigurationAssignmentWorkerServiceHelper.NoPermissionsSecurityGroupName }),
            "precondition: the worker must start in \"none\" before UpdateDeviceUser runs");

        var newDeviceUserModel = new DeviceUserModel
        {
            SiteMicrotingUid = (int)currentSite.MicrotingUid!,
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = false,
            WebAccessEnabled = true,
            UserFirstName = Guid.NewGuid().ToString(),
            UserLastName = Guid.NewGuid().ToString(),
            WorkerEmail = $"{Guid.NewGuid()}@test.com"
        };

        // Act
        var result = await BackendConfigurationAssignmentWorkerServiceHelper.UpdateDeviceUser(newDeviceUserModel, core, 1,
            userService, userManager,
            BackendConfigurationPnDbContext!,
            TimePlanningPnDbContext!, BaseDbContext!, logger, ItemsPlanningPnDbContext!);

        // Assert
        Assert.That(result.Success, Is.True, result.Message);
        var groupNames = await GetSecurityGroupNamesAsync(createdUser.Id);
        Assert.That(groupNames, Does.Contain("eForm users"));
        Assert.That(groupNames, Does.Not.Contain(
            BackendConfigurationAssignmentWorkerServiceHelper.NoPermissionsSecurityGroupName));
    }

    // Pins the fallback call at UpdateDeviceUser's "assignments.Count != 0"
    // early return (the EnableMobileAccess update-in-place branch inside
    // `TimeRegistrationEnabled == true`). Reached by two successive
    // TimeRegistrationEnabled == true updates on the same site: the first
    // creates the AssignedSite (takes the sibling create-a-new-AssignedSite
    // return, not the one under test); the second finds that AssignedSite
    // (assignments.Count != 0) and takes THIS return.
    //
    // Deliberately does NOT create with an empty flag set: that would seed
    // "none" at creation time (CreateDeviceUser's own fallback call), so
    // "none" would already be present when this branch is entered and
    // deleting the fallback call under test would leave the assertion
    // passing unchanged, making the test vacuous. Instead the worker is
    // created holding a real group ("Kun arkiv",
    // via ArchiveEnabled), kept through the first update (guard-asserted
    // below), then stripped by the SECOND update via ArchiveEnabled = false
    // — a mechanism the shared eForm-users/archive sync block provides, not
    // the assignments.Count != 0 fallback under test — leaving the user in
    // NO group at all by the time that return is reached (EnableMobileAccess
    // stays false throughout, so "Kun tid" is never added either). Only the
    // fallback call under test can turn that into ["none"]; without it the
    // user would be left with an empty group list.
    //
    // CreateDeviceUser cannot reach the assignments.Count != 0 branch at all
    // (it always creates the AssignedSite from nothing), so this is
    // exclusive to UpdateDeviceUser.
    [Test]
    public async Task BackendConfigurationAssignmentWorkerServiceHelper_UpdateDeviceUser_AssignmentsCountNotZero_NoRealGroup_LandsInNoneGroup()
    {
        // Arrange
        var core = await GetCore();
        var logger = Substitute.For<ILogger>();
        var workerEmail = $"{Guid.NewGuid()}@test.com";

        var deviceUserModel = new DeviceUserModel
        {
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = false,
            ArchiveEnabled = true,
            UserFirstName = Guid.NewGuid().ToString(),
            UserLastName = Guid.NewGuid().ToString(),
            WorkerEmail = workerEmail
        };

        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);
        var userService = IdentityTestUtils.CreateRealUserService(BaseDbContext!, userManager);

        await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            TimePlanningPnDbContext!, BaseDbContext!, userService, userManager);

        var createdUser = await BaseDbContext!.Users.SingleAsync(x => x.Email == workerEmail);
        var currentSite = await MicrotingDbContext!.Sites.OrderByDescending(x => x.Id).FirstAsync();

        // First update: TimeRegistrationEnabled true, no AssignedSite exists yet for
        // this site — takes the "create a new AssignedSite" return, not the one
        // under test. ArchiveEnabled stays true so the real group survives this call.
        var firstUpdateModel = new DeviceUserModel
        {
            SiteMicrotingUid = (int)currentSite.MicrotingUid!,
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = true,
            EnableMobileAccess = false,
            ArchiveEnabled = true,
            UserFirstName = Guid.NewGuid().ToString(),
            UserLastName = Guid.NewGuid().ToString(),
            WorkerEmail = $"{Guid.NewGuid()}@test.com"
        };

        var firstResult = await BackendConfigurationAssignmentWorkerServiceHelper.UpdateDeviceUser(firstUpdateModel, core, 1,
            userService, userManager,
            BackendConfigurationPnDbContext!,
            TimePlanningPnDbContext!, BaseDbContext!, logger, ItemsPlanningPnDbContext!);
        Assert.That(firstResult.Success, Is.True, firstResult.Message);

        // Guard: the worker must hold the real "Kun arkiv" group and NOT
        // "none" right before the call under test — otherwise this test
        // cannot distinguish "the fallback ran" from "none was already
        // sitting there from creation".
        var groupNamesBeforeSecondUpdate = await GetSecurityGroupNamesAsync(createdUser.Id);
        Assert.That(groupNamesBeforeSecondUpdate, Is.EqualTo(new List<string> { "Kun arkiv" }),
            "precondition: the worker must hold a real group and not \"none\" before the assignments.Count != 0 return is reached");

        // Second update: TimeRegistrationEnabled true again, and the AssignedSite
        // the first update created exists for this site — takes the
        // "assignments.Count != 0" return under test. ArchiveEnabled flips to false
        // here, stripping the real group via the shared eForm-users/archive sync
        // (not the fallback under test) ahead of that branch. EnableMobileAccess
        // stays false, so no replacement real group ("Kun tid") is added.
        var secondUpdateModel = new DeviceUserModel
        {
            SiteMicrotingUid = (int)currentSite.MicrotingUid!,
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = true,
            EnableMobileAccess = false,
            ArchiveEnabled = false,
            UserFirstName = Guid.NewGuid().ToString(),
            UserLastName = Guid.NewGuid().ToString(),
            WorkerEmail = $"{Guid.NewGuid()}@test.com"
        };

        // Act
        var result = await BackendConfigurationAssignmentWorkerServiceHelper.UpdateDeviceUser(secondUpdateModel, core, 1,
            userService, userManager,
            BackendConfigurationPnDbContext!,
            TimePlanningPnDbContext!, BaseDbContext!, logger, ItemsPlanningPnDbContext!);

        // Assert
        Assert.That(result.Success, Is.True, result.Message);
        var timeregistrationSiteAssignments = await TimePlanningPnDbContext!.AssignedSites
            .Where(x => x.SiteId == (int)currentSite.MicrotingUid! && x.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync();
        Assert.That(timeregistrationSiteAssignments, Has.Count.EqualTo(1),
            "the second update must have hit the assignments.Count != 0 branch, not created a second AssignedSite");
        var groupNames = await GetSecurityGroupNamesAsync(createdUser.Id);
        Assert.That(groupNames, Is.EqualTo(new List<string>
            { BackendConfigurationAssignmentWorkerServiceHelper.NoPermissionsSecurityGroupName }));
    }

    // Pins the same "assignments.Count != 0" early return as the test above,
    // in the opposite direction: the second update turns EnableMobileAccess
    // on, which adds "Kun tid" membership INSIDE this branch, immediately
    // before the return under test. The fallback must see that add and keep
    // the user out of "none". This is the case that would catch the fallback
    // call being placed above the "Kun tid" sync in this branch instead of
    // below it.
    [Test]
    public async Task BackendConfigurationAssignmentWorkerServiceHelper_UpdateDeviceUser_AssignmentsCountNotZero_EnableMobileAccess_DoesNotLandInNoneGroup()
    {
        // Arrange
        var core = await GetCore();
        var logger = Substitute.For<ILogger>();
        var workerEmail = $"{Guid.NewGuid()}@test.com";

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
            WorkerEmail = workerEmail
        };

        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);
        var userService = IdentityTestUtils.CreateRealUserService(BaseDbContext!, userManager);

        await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            TimePlanningPnDbContext!, BaseDbContext!, userService, userManager);

        var createdUser = await BaseDbContext!.Users.SingleAsync(x => x.Email == workerEmail);
        var currentSite = await MicrotingDbContext!.Sites.OrderByDescending(x => x.Id).FirstAsync();

        // First update: TimeRegistrationEnabled true, no AssignedSite exists yet for
        // this site — takes the "create a new AssignedSite" return, not the one
        // under test. EnableMobileAccess stays false here on purpose, so the
        // "Kun tid" add below happens only on the second call.
        var firstUpdateModel = new DeviceUserModel
        {
            SiteMicrotingUid = (int)currentSite.MicrotingUid!,
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = true,
            EnableMobileAccess = false,
            UserFirstName = Guid.NewGuid().ToString(),
            UserLastName = Guid.NewGuid().ToString(),
            WorkerEmail = $"{Guid.NewGuid()}@test.com"
        };

        var firstResult = await BackendConfigurationAssignmentWorkerServiceHelper.UpdateDeviceUser(firstUpdateModel, core, 1,
            userService, userManager,
            BackendConfigurationPnDbContext!,
            TimePlanningPnDbContext!, BaseDbContext!, logger, ItemsPlanningPnDbContext!);
        Assert.That(firstResult.Success, Is.True, firstResult.Message);

        // Guard: the worker must still be in "none" right before the call
        // under test - otherwise this test cannot distinguish "the fallback
        // saw the new Kun tid membership and removed none" from "none was
        // never there to begin with".
        var groupNamesBeforeSecondUpdate = await GetSecurityGroupNamesAsync(createdUser.Id);
        Assert.That(groupNamesBeforeSecondUpdate, Is.EqualTo(new List<string>
                { BackendConfigurationAssignmentWorkerServiceHelper.NoPermissionsSecurityGroupName }),
            "precondition: the worker must still be in \"none\" before the assignments.Count != 0 return is reached");

        // Second update: TimeRegistrationEnabled true again, and the AssignedSite
        // the first update created exists for this site — takes the
        // "assignments.Count != 0" return under test. EnableMobileAccess flips to
        // true, so "Kun tid" membership is added inside this branch.
        var secondUpdateModel = new DeviceUserModel
        {
            SiteMicrotingUid = (int)currentSite.MicrotingUid!,
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = true,
            EnableMobileAccess = true,
            UserFirstName = Guid.NewGuid().ToString(),
            UserLastName = Guid.NewGuid().ToString(),
            WorkerEmail = $"{Guid.NewGuid()}@test.com"
        };

        // Act
        var result = await BackendConfigurationAssignmentWorkerServiceHelper.UpdateDeviceUser(secondUpdateModel, core, 1,
            userService, userManager,
            BackendConfigurationPnDbContext!,
            TimePlanningPnDbContext!, BaseDbContext!, logger, ItemsPlanningPnDbContext!);

        // Assert
        Assert.That(result.Success, Is.True, result.Message);
        var timeregistrationSiteAssignments = await TimePlanningPnDbContext!.AssignedSites
            .Where(x => x.SiteId == (int)currentSite.MicrotingUid! && x.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync();
        Assert.That(timeregistrationSiteAssignments, Has.Count.EqualTo(1),
            "the second update must have hit the assignments.Count != 0 branch, not created a second AssignedSite");
        var groupNames = await GetSecurityGroupNamesAsync(createdUser.Id);
        Assert.That(groupNames, Does.Contain("Kun tid"));
        Assert.That(groupNames, Does.Not.Contain(
            BackendConfigurationAssignmentWorkerServiceHelper.NoPermissionsSecurityGroupName));
    }

    // Pins the THIRD UpdateDeviceUser fallback call site: the return reached
    // after creating a brand-new AssignedSite (TimeRegistrationEnabled flips to
    // true for the first time on this site, so assignments.Count == 0 and control
    // takes the "create one" branch, not the "update it in place" branch the two
    // *_AssignmentsCountNotZero_* tests above already cover). Without this test
    // that create-new-AssignedSite branch only runs as incidental SETUP inside
    // those two tests - the first of their two UpdateDeviceUser calls always
    // passes through it before the SECOND call reaches the branch actually under
    // test there - so nothing would fail if this particular
    // EnsureFallbackSecurityGroupAsync call were deleted.
    //
    // FindLoginWithoutSideEffectsAsync(oldEmail) finds and adopts createdUser's
    // own account across the update, exactly like production - the same
    // account is the one the fallback call under test runs against.
    [Test]
    public async Task BackendConfigurationAssignmentWorkerServiceHelper_UpdateDeviceUser_CreateNewAssignedSite_NoRealGroup_LandsInNoneGroup()
    {
        // Arrange
        var core = await GetCore();
        var logger = Substitute.For<ILogger>();
        var workerEmail = $"{Guid.NewGuid()}@test.com";

        var deviceUserModel = new DeviceUserModel
        {
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = false,
            ArchiveEnabled = true,
            UserFirstName = Guid.NewGuid().ToString(),
            UserLastName = Guid.NewGuid().ToString(),
            WorkerEmail = workerEmail
        };

        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);
        var userService = IdentityTestUtils.CreateRealUserService(BaseDbContext!, userManager);

        await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
            TimePlanningPnDbContext!, BaseDbContext!, userService, userManager);

        var createdUser = await BaseDbContext!.Users.SingleAsync(x => x.Email == workerEmail);
        var currentSite = await MicrotingDbContext!.Sites.OrderByDescending(x => x.Id).FirstAsync();

        // Guard: the worker's own account holds the real "Kun arkiv" group and
        // NOT "none" immediately before the call under test - the "started in a
        // real group" precondition the task requires, proven on the entity that
        // actually exists at this point.
        var groupNamesBeforeUpdate = await GetSecurityGroupNamesAsync(createdUser.Id);
        Assert.That(groupNamesBeforeUpdate, Is.EqualTo(new List<string> { "Kun arkiv" }),
            "precondition: the worker must hold a real group and not \"none\" before the create-new-AssignedSite return is reached");
        Assert.That(await TimePlanningPnDbContext!.AssignedSites
                .CountAsync(x => x.SiteId == (int)currentSite.MicrotingUid! && x.WorkflowState != Constants.WorkflowStates.Removed),
            Is.Zero,
            "precondition: no AssignedSite may exist yet, or the update would take the assignments.Count != 0 branch instead");

        var updatedEmail = $"{Guid.NewGuid()}@test.com";
        var newDeviceUserModel = new DeviceUserModel
        {
            SiteMicrotingUid = (int)currentSite.MicrotingUid!,
            CustomerNo = 0,
            HasWorkOrdersAssigned = false,
            IsBackendUser = false,
            IsLocked = false,
            LanguageCode = "da",
            TimeRegistrationEnabled = true,
            EnableMobileAccess = false,
            ArchiveEnabled = false,
            UserFirstName = Guid.NewGuid().ToString(),
            UserLastName = Guid.NewGuid().ToString(),
            WorkerEmail = updatedEmail
        };

        // Act
        var result = await BackendConfigurationAssignmentWorkerServiceHelper.UpdateDeviceUser(newDeviceUserModel, core, 1,
            userService, userManager,
            BackendConfigurationPnDbContext!,
            TimePlanningPnDbContext!, BaseDbContext!, logger, ItemsPlanningPnDbContext!);

        // Assert
        Assert.That(result.Success, Is.True, result.Message);
        var timeregistrationSiteAssignments = await TimePlanningPnDbContext!.AssignedSites
            .Where(x => x.SiteId == (int)currentSite.MicrotingUid! && x.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync();
        Assert.That(timeregistrationSiteAssignments, Has.Count.EqualTo(1),
            "the update must have taken the create-new-AssignedSite branch, proving control passed through the return under test");

        // ArchiveEnabled = false removed "Kun arkiv" earlier in this same call,
        // and EnableMobileAccess = false never added "Kun tid" - createdUser
        // reaches the fallback with NO real group left at all. Only the call
        // under test can turn that into ["none"]; deleting it would leave the
        // list empty.
        var groupNames = await GetSecurityGroupNamesAsync(createdUser.Id);
        Assert.That(groupNames, Is.EqualTo(new List<string>
            { BackendConfigurationAssignmentWorkerServiceHelper.NoPermissionsSecurityGroupName }));
    }

    /// <summary>
    /// Refuses every user with an error code outside Identity's own email codes,
    /// standing in for any non-email validation failure.
    /// </summary>
    private sealed class PolicyRefusalUserValidator : IUserValidator<EformUser>
    {
        public Task<IdentityResult> ValidateAsync(UserManager<EformUser> manager, EformUser user) =>
            Task.FromResult(IdentityResult.Failed(
                new IdentityError { Code = "PolicyRefusal", Description = "Refused by policy" }));
    }

    /// <summary>
    /// A real UserManager with <see cref="PolicyRefusalUserValidator"/> added,
    /// and a UserService over it, so every UpdateAsync/CreateAsync is refused
    /// with a non-email error code.
    /// </summary>
    private (UserManager<EformUser> Manager, IUserService Service) CreateRefusingUserManagerAndService()
    {
        var manager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);
        manager.UserValidators.Add(new PolicyRefusalUserValidator());
        var service = IdentityTestUtils.CreateRealUserService(BaseDbContext!, manager);
        return (manager, service);
    }

    private async Task<List<string>> GetSecurityGroupNamesAsync(int eformUserId) =>
        await (from sgu in BaseDbContext!.SecurityGroupUsers
            join sg in BaseDbContext.SecurityGroups on sgu.SecurityGroupId equals sg.Id
            where sgu.EformUserId == eformUserId
            select sg.Name).ToListAsync();

    /// <summary>
    /// Creates a real SDK Tag (auto id) so a "refused save writes no tag"
    /// assertion is meaningful. A fake id like 999999 proves nothing: the SDK's
    /// FK_SiteTags_tags_TagId means SiteTag.Create would throw on a fake id
    /// regardless of whether the code under test refused the save BEFORE or
    /// AFTER attempting the tag write, so the assertion would pass either way.
    /// </summary>
    private async Task<int> SeedSdkTag()
    {
        var tag = new Tag
        {
            Name = $"assignment-worker-tag-{Guid.NewGuid()}",
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext!.Tags.AddAsync(tag);
        await MicrotingDbContext.SaveChangesAsync();
        return tag.Id;
    }

    /// <summary>
    /// Shared shape for "a refused save must not have written the requested
    /// tag either". Only meaningful once the tag id passed through
    /// deviceUserModel.Tags is a REAL row from <see cref="SeedSdkTag"/> - a
    /// fake id would make this pass regardless of write order, since
    /// SiteTag.Create would throw on the FK before ever reaching here.
    /// </summary>
    private static async Task AssertNoActiveSiteTagsAsync(
        Microting.eForm.Infrastructure.MicrotingDbContext sdkDbContext, int siteId)
    {
        Assert.That(await sdkDbContext.SiteTags.CountAsync(x => x.SiteId == siteId
                && x.WorkflowState != Constants.WorkflowStates.Removed), Is.Zero,
            "a refused save must not write the requested tag either - the tag block must run AFTER validation");
    }

    /// <summary>
    /// Seeds Area → Property → AreaRule → Planning → AreaRulePlanning
    /// (Status = true, live WorkflowState) and returns the AreaRulePlanning
    /// id - the minimal chain UpdateDeviceUser's resign-refusal query walks
    /// via AreaRulePlanningWorkerTag. Mirrors
    /// AreaRulePlanningTagPurgeTest.SeedAreaRulePlanning.
    /// </summary>
    private async Task<int> SeedActiveAreaRulePlanning()
    {
        var area = new Area
        {
            Type = AreaTypesEnum.Type1, ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await area.Create(BackendConfigurationPnDbContext!);

        var property = new Property
        {
            Name = $"ResignRefusalProp-{Guid.NewGuid()}", ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await property.Create(BackendConfigurationPnDbContext!);

        var areaRule = new AreaRule
        {
            AreaId = area.Id, PropertyId = property.Id, EformId = 7, CreatedInGuide = true,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await areaRule.Create(BackendConfigurationPnDbContext!);

        var planning = new BcPlanning
        {
            Enabled = true, RepeatEvery = 1, RepeatType = RepeatType.Week,
            StartDate = DateTime.UtcNow.Date, RelatedEFormId = 7, Description = "Task",
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await planning.Create(ItemsPlanningPnDbContext!);

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = property.Id, AreaId = area.Id,
            ItemPlanningId = planning.Id, StartDate = DateTime.UtcNow.Date, Status = true,
            RepeatType = 2, RepeatEvery = 1,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await arp.Create(BackendConfigurationPnDbContext!);

        return arp.Id;
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