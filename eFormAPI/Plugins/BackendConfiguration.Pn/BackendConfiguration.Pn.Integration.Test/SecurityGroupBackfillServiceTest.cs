using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Helpers;
using BackendConfiguration.Pn.Services.SecurityGroupBackfillService;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;
using Microting.EformAngularFrontendBase.Infrastructure.Data.Entities.Permissions;
using NSubstitute;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Integration.Test;

[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class SecurityGroupBackfillServiceTest : TestBaseSetup
{
    /// <summary>
    /// The run-once gate records a marker in PluginConfigurationValues, and
    /// TestBaseSetup.ResetDatabasePerTest is false, so without this every test after
    /// the first would short-circuit on the marker the first one wrote — one failing
    /// outright and the no-op test passing against an implementation that did
    /// nothing at all. Same shape as AreaRulePlanningTagPurgeTest and
    /// CalendarConfigurationBackfillTest. Setting ResetDatabasePerTest would NOT fix
    /// it: the plugin seed files are data-only and never drop this row.
    /// </summary>
    [SetUp]
    public async Task ClearBackfillMarker()
    {
        BackendConfigurationPnDbContext!.PluginConfigurationValues.RemoveRange(
            BackendConfigurationPnDbContext.PluginConfigurationValues
                .Where(x => x.Name == SecurityGroupBackfillService.BackfillMarkerName));
        await BackendConfigurationPnDbContext.SaveChangesAsync();
    }

    [SetUp]
    public Task ReserveEformUserId1() => IdentityTestUtils.ReserveEformUserId1Async(BaseDbContext!);

    // A user never saved through the fallback rule can be groupless; the
    // backfill is what makes "every user is in none unless they have another
    // group" true of the whole database rather than only of users saved through
    // the helper.
    [Test]
    public async Task SecurityGroupBackfillService_AssignsNoneToGrouplessUsersOnly()
    {
        // Arrange
        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);

        var grouplessUser = await CreateUserAsync(userManager);
        var groupedUser = await CreateUserAsync(userManager);
        var realGroupId = await BaseDbContext!.SecurityGroups
            .Where(x => x.Name == "eForm users").Select(x => x.Id).FirstAsync();
        BaseDbContext.SecurityGroupUsers.Add(new SecurityGroupUser
            { EformUserId = groupedUser.Id, SecurityGroupId = realGroupId });
        await BaseDbContext.SaveChangesAsync();

        var sut = await BuildSutAsync(userManager);

        // Act
        await sut.RunIfNeededAsync();

        // Assert
        Assert.That(await GroupNamesFor(grouplessUser.Id),
            Is.EqualTo(new List<string> { BackendConfigurationAssignmentWorkerServiceHelper.NoPermissionsSecurityGroupName }));
        Assert.That(await GroupNamesFor(groupedUser.Id), Is.EqualTo(new List<string> { "eForm users" }));
    }

    // Core strips group memberships from admins, so admins are the groupless
    // population a production database actually has. Putting them in "none" would
    // write a state the system immediately un-writes and would list every admin
    // under /security.
    [Test]
    public async Task SecurityGroupBackfillService_LeavesAdminsGroupless()
    {
        // Arrange
        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);

        // ReserveEformUserId1 keeps adminUser off id 1, which EnsureFallbackSecurityGroupAsync
        // skips by id alone - so only the admin-ROLE check can leave it groupless.
        var adminUser = await CreateUserAsync(userManager);
        var addToRole = await userManager.AddToRoleAsync(adminUser, EformRole.Admin);
        Assert.That(addToRole.Succeeded, Is.True,
            string.Join(",", addToRole.Errors.Select(e => e.Description)));

        var sut = await BuildSutAsync(userManager);

        // Act
        await sut.RunIfNeededAsync();

        // Assert
        Assert.That(await GroupNamesFor(adminUser.Id), Is.Empty,
            "an admin must not be dragged into the fallback group");
    }

    // The affordance in property-workers is gated on the worker's EMAIL, which is
    // projected off the SDK Worker row and not off AspNetUsers. A worker with an
    // email but no account therefore shows "Set password" — and without this step
    // there is no account behind it, so the click reaches core's
    // AccountService.AdminChangePassword, which calls RemovePasswordAsync on a null
    // user. The sweep gives every such worker a login with no password set.
    [Test]
    public async Task SecurityGroupBackfillService_CreatesLoginWithNoPasswordForWorkerWithEmail()
    {
        // Arrange
        var core = await GetCore();
        await using var sdkDbContext = core.DbContextHelper.GetDbContext();
        var (workerEmail, languageCode) = await SeedSdkWorkerWithEmailAsync(sdkDbContext);

        Assert.That(await BaseDbContext!.Users.AnyAsync(x => x.Email == workerEmail), Is.False,
            "the fixture must start with no account for this worker");

        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext);
        var sut = BuildSut(userManager, core);

        // Act
        await sut.RunIfNeededAsync();

        // Assert
        var createdUser = await BaseDbContext.Users.SingleAsync(x => x.Email == workerEmail);
        Assert.That(createdUser.PasswordHash, Is.Null,
            "the account must exist but not be signable into until an admin sets a password");
        Assert.That(await userManager.CheckPasswordAsync(createdUser, ""), Is.False,
            "an account with no password set must not accept a blank password");
        Assert.That(createdUser.UserName, Is.EqualTo(workerEmail));
        Assert.That(createdUser.Locale, Is.EqualTo(languageCode),
            "the locale comes from the language of the site the worker is assigned to");
        Assert.That(createdUser.EmailConfirmed, Is.True);
        Assert.That(await userManager.IsInRoleAsync(createdUser, EformRole.User), Is.True);
        Assert.That(await GroupNamesFor(createdUser.Id),
            Is.EqualTo(new List<string>
                { BackendConfigurationAssignmentWorkerServiceHelper.NoPermissionsSecurityGroupName }),
            "a backfilled login must land in the same permissionless group the save paths give it");
    }

    // Pins the invariant the whole backfill exists for, not just the single
    // worker the test above covers: every state a worker can be in must be
    // swept correctly in ONE pass. Without this, a change that broke e.g. "removed
    // workers must not be resurrected" or "Identity's refusal must not abort
    // the sweep" could land and nothing here would catch it - the single-worker
    // test above cannot fail on any of those, because it never seeds the states
    // that would expose them.
    [Test]
    public async Task SecurityGroupBackfillService_PopulatesEveryWorkerStateCorrectly()
    {
        // Arrange
        var core = await GetCore();
        await using var sdkDbContext = core.DbContextHelper.GetDbContext();

        var liveEmail = $"{Guid.NewGuid()}@example.test";
        var syntheticEmail = $"user_{Guid.NewGuid():N}_1@microting.invalid";
        var removedEmail = $"{Guid.NewGuid()}@removed.test";
        var existingAccountEmail = $"{Guid.NewGuid()}@already-has-account.test";
        // Non-ASCII local part: outside the default AllowedUserNameCharacters, so
        // Identity's CreateAsync refuses it - same shape as
        // BackendConfigurationAssignmentWorkerServiceHelperTest's
        // CreateDeviceUser_IdentityRejectsEmail_KeepsWorkerAndCreatesNoUser.
        var refusedEmail = $"s\u00f8ren-{Guid.NewGuid():N}@firma.dk";
        var lateValidEmail = $"{Guid.NewGuid()}@example.test";

        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);

        // Pre-existing account for the "email already has a login" case -
        // created BEFORE the sweep runs and before the SDK worker sharing its
        // email even exists.
        //
        // UserName is deliberately NOT the email (an admin's own login name, or
        // a leftover from a removed worker), and given a real password.
        // CreateUserAsync's default UserName == Email, but that would make the
        // "no duplicate" assertions below pass vacuously: Identity enforces
        // unique USERNAMEs unconditionally (regardless of RequireUniqueEmail),
        // so a second CreateAsync for this same email-as-username would already
        // be refused by Identity itself even if the service's own
        // already-taken skip (SecurityGroupBackfillService.
        // CreateMissingUsersForWorkersWithEmailAsync's `takenEmails.Add(...)`
        // check) were deleted - and the untouched PasswordHash check can't
        // move either when it is null both before and after. A distinct
        // UserName plus a real password means only the service's own skip
        // keeps a second account from being created for this address.
        // Generated at runtime, not a literal: GitGuardian flags any committed
        // password-shaped string, even a fabricated test one.
        var existingAccountPassword = $"Aa1!{Guid.NewGuid():N}";
        var existingUser = await CreateUserAsync(userManager, password: existingAccountPassword,
            email: existingAccountEmail, userName: $"existing-{Guid.NewGuid()}");
        var existingUserId = existingUser.Id;
        var existingUserPasswordHash = existingUser.PasswordHash;

        // Seeded in this exact order so the refused worker's id is LOWER than
        // the late-valid worker's: the sweep keyset-paginates by worker id
        // ascending, so this is the only ordering that can prove a refusal
        // does not abort the rest of the batch.
        await SeedSdkWorkerAsync(sdkDbContext, liveEmail);
        await SeedSdkWorkerAsync(sdkDbContext, existingAccountEmail);
        await SeedSdkWorkerAsync(sdkDbContext, removedEmail, Constants.WorkflowStates.Removed);
        await SeedSdkWorkerAsync(sdkDbContext, refusedEmail);
        await SeedSdkWorkerAsync(sdkDbContext, lateValidEmail);
        await SeedSdkWorkerAsync(sdkDbContext, syntheticEmail);

        var sut = BuildSut(userManager, core);

        // Act
        await sut.RunIfNeededAsync();

        // Assert - live worker, real email, no pre-existing account: gets a login.
        var liveUser = await BaseDbContext!.Users.SingleAsync(x => x.Email == liveEmail);
        Assert.That(liveUser.PasswordHash, Is.Null);
        Assert.That(await GroupNamesFor(liveUser.Id),
            Is.EqualTo(new List<string> { BackendConfigurationAssignmentWorkerServiceHelper.NoPermissionsSecurityGroupName }));

        // Assert - synthetic @microting.invalid address: treated as a real email.
        var syntheticUser = await BaseDbContext.Users.SingleAsync(x => x.Email == syntheticEmail);
        Assert.That(syntheticUser.PasswordHash, Is.Null);
        Assert.That(await GroupNamesFor(syntheticUser.Id),
            Is.EqualTo(new List<string> { BackendConfigurationAssignmentWorkerServiceHelper.NoPermissionsSecurityGroupName }));

        // Assert - removed worker: must not be resurrected with a login.
        Assert.That(await BaseDbContext.Users.AnyAsync(x => x.Email == removedEmail), Is.False,
            "a removed worker must never get a login from the backfill");

        // Assert - email that already had an AspNetUsers row: no duplicate is
        // created, and the pre-existing account is left exactly as it was.
        Assert.That(await BaseDbContext.Users.CountAsync(x => x.Email == existingAccountEmail), Is.EqualTo(1),
            "a worker whose email already has an account must not get a second one");
        var reloadedExisting = await BaseDbContext.Users.SingleAsync(x => x.Email == existingAccountEmail);
        Assert.That(reloadedExisting.Id, Is.EqualTo(existingUserId));
        Assert.That(reloadedExisting.PasswordHash, Is.EqualTo(existingUserPasswordHash));
        Assert.That(await userManager.CheckPasswordAsync(reloadedExisting, existingAccountPassword), Is.True,
            "the pre-existing account's own password must still work - the sweep must not have touched or replaced it");

        // Assert - Identity refuses the address: no login for it, but the sweep
        // must not abort - the worker seeded after it in id order still gets
        // one, and the marker is still written.
        Assert.That(await BaseDbContext.Users.AnyAsync(x => x.Email == refusedEmail), Is.False);
        var lateValidUser = await BaseDbContext.Users.SingleAsync(x => x.Email == lateValidEmail);
        Assert.That(lateValidUser.PasswordHash, Is.Null,
            "a refusal earlier in the keyset page must not stop a later worker in the same batch from getting a login");
        Assert.That(await BackendConfigurationPnDbContext!.PluginConfigurationValues
                .CountAsync(x => x.Name == SecurityGroupBackfillService.BackfillMarkerName), Is.EqualTo(1),
            "one bad row must not stop the marker from being written");

        // Headline: every live worker seeded above with a non-empty email
        // has an AspNetUsers row, except the one Identity itself refused - a
        // documented, structural exception. Queried generically
        // across the whole seeded set rather than per worker, so a case added
        // to this test later is covered automatically without a new assert.
        var expectedLoginEmails = new[] { liveEmail, syntheticEmail, existingAccountEmail, lateValidEmail };
        var actualLoginEmails = await BaseDbContext.Users
            .Where(x => expectedLoginEmails.Contains(x.Email))
            .Select(x => x.Email)
            .ToListAsync();
        Assert.That(actualLoginEmails, Is.EquivalentTo(expectedLoginEmails));
    }

    // CreatesLoginWithNoPasswordForWorkerWithEmail seeds the site's language via
    // Languages.FirstAsync(), which is id 1 = "da" - identical to the SDK's own
    // default. That makes its Locale assertion pass against an implementation
    // that ignores the SiteWorker -> Site -> Language join entirely and always
    // writes the default. Seeding a non-default language is what actually
    // exercises the join.
    [Test]
    public async Task SecurityGroupBackfillService_CreatesLoginWithSitesLanguage_NotJustTheDefault()
    {
        // Arrange
        var core = await GetCore();
        await using var sdkDbContext = core.DbContextHelper.GetDbContext();
        var nonDefaultLanguage = await sdkDbContext.Languages.SingleAsync(x => x.LanguageCode == "en-US");
        Assert.That(nonDefaultLanguage.Id, Is.Not.EqualTo(1),
            "this only proves anything if it is not the SDK's own default language");
        var (workerEmail, languageCode) = await SeedSdkWorkerWithEmailAsync(sdkDbContext, nonDefaultLanguage.Id);
        Assert.That(languageCode, Is.EqualTo("en-US"), "sanity check on the seeding helper itself");

        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);
        var sut = BuildSut(userManager, core);

        // Act
        await sut.RunIfNeededAsync();

        // Assert
        var createdUser = await BaseDbContext!.Users.SingleAsync(x => x.Email == workerEmail);
        Assert.That(createdUser.Locale, Is.EqualTo("en-US"));
    }

    // A worker with no site at all must still get a login - falling back to
    // the SDK's own default rather than throwing when the SiteWorker join has
    // nothing to return.
    [Test]
    public async Task SecurityGroupBackfillService_CreatesLoginForWorkerWithNoSite_FallsBackToDefaultLocale()
    {
        // Arrange
        var core = await GetCore();
        await using var sdkDbContext = core.DbContextHelper.GetDbContext();
        var worker = await SeedSdkWorkerAsync(sdkDbContext, $"{Guid.NewGuid()}@no-site.test");
        var workerEmail = worker.Email;

        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);
        var sut = BuildSut(userManager, core);

        // Act - must not throw despite there being no SiteWorker row to join through.
        await sut.RunIfNeededAsync();

        // Assert
        var createdUser = await BaseDbContext!.Users.SingleAsync(x => x.Email == workerEmail);
        Assert.That(createdUser.Locale, Is.EqualTo("da"));
    }

    // The literal was in source and therefore identical across deployments.
    // Clearing it leaves the account in place but unsignable-into.
    [Test]
    public async Task SecurityGroupBackfillService_ClearsRetiredHardcodedPassword()
    {
        // Arrange
        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);

        // otherUserPassword is generated at runtime, not a literal: GitGuardian
        // flags any committed password-shaped string, even a fabricated test
        // one. The retired literal below stays a literal on purpose - the
        // backfill compares against that exact string, so it is allowlisted.
        var otherUserPassword = $"Aa1!{Guid.NewGuid():N}";
        var legacyUser = await CreateUserAsync(userManager, "Replace_me_with_a_proper_password_2024!");
        var otherUser = await CreateUserAsync(userManager, otherUserPassword);

        var sut = await BuildSutAsync(userManager);

        // Act
        await sut.RunIfNeededAsync();

        // Assert
        var reloadedLegacy = await BaseDbContext!.Users.SingleAsync(x => x.Id == legacyUser.Id);
        Assert.That(reloadedLegacy.PasswordHash, Is.Null, "the retired literal must be cleared");

        var reloadedOther = await BaseDbContext.Users.SingleAsync(x => x.Id == otherUser.Id);
        Assert.That(await userManager.CheckPasswordAsync(reloadedOther, otherUserPassword),
            Is.True, "a password an admin actually set must survive");
    }

    // Startup calls this on every boot; the marker is what stops it re-scanning.
    [Test]
    public async Task SecurityGroupBackfillService_SecondRunIsANoOp()
    {
        // Arrange
        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);
        var sut = await BuildSutAsync(userManager);

        // A user that exists BEFORE the first run must be swept, so the assertions
        // below distinguish "the gate stopped the second run" from "the sweep never
        // does anything".
        var earlyUser = await CreateUserAsync(userManager);
        await sut.RunIfNeededAsync();
        Assert.That(await GroupNamesFor(earlyUser.Id),
            Is.EqualTo(new List<string>
                { BackendConfigurationAssignmentWorkerServiceHelper.NoPermissionsSecurityGroupName }),
            "the first run must actually assign the fallback group");
        Assert.That(await BackendConfigurationPnDbContext!.PluginConfigurationValues
                .CountAsync(x => x.Name == SecurityGroupBackfillService.BackfillMarkerName), Is.EqualTo(1),
            "the first run must record the marker that gates every later boot");

        // A user created AFTER the first run must be left alone: the marker means
        // the one-time sweep is done, and the save paths cover new users.
        var lateUser = await CreateUserAsync(userManager);

        // Act
        await sut.RunIfNeededAsync();

        // Assert
        Assert.That(await GroupNamesFor(lateUser.Id), Is.Empty,
            "the marker must short-circuit the second run");
        Assert.That(await BackendConfigurationPnDbContext.PluginConfigurationValues
            .CountAsync(x => x.Name == SecurityGroupBackfillService.BackfillMarkerName), Is.EqualTo(1));
    }

    private async Task<SecurityGroupBackfillService> BuildSutAsync(UserManager<EformUser> userManager)
        => BuildSut(userManager, await GetCore());

    private SecurityGroupBackfillService BuildSut(UserManager<EformUser> userManager, eFormCore.Core core)
    {
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));
        return new SecurityGroupBackfillService(BackendConfigurationPnDbContext!, BaseDbContext!,
            userManager, coreHelper, Substitute.For<ILogger<SecurityGroupBackfillService>>());
    }

    /// <summary>
    /// Seeds an SDK Site + Worker + SiteWorker through a context handed out AFTER
    /// the SDK Core migrations, because SQL/420_SDK.sql creates Workers without
    /// Resigned/ResignedAtDate. Same shape as WorkerTagAssignmentTest.
    /// <paramref name="languageId"/> defaults to the SDK's first language (id 1,
    /// "da") - the same, indistinguishable-from-the-fallback value every
    /// existing caller relied on - so pass it explicitly whenever a test needs
    /// to prove the SiteWorker -> Site -> Language join is actually exercised.
    /// </summary>
    private static async Task<(string email, string languageCode)> SeedSdkWorkerWithEmailAsync(
        MicrotingDbContext sdkDbContext, int? languageId = null)
    {
        var language = languageId.HasValue
            ? await sdkDbContext.Languages.SingleAsync(x => x.Id == languageId.Value)
            : await sdkDbContext.Languages.FirstAsync();
        var site = new Site
        {
            Name = $"site-{Guid.NewGuid()}",
            MicrotingUid = null,
            LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await sdkDbContext.Sites.AddAsync(site);
        await sdkDbContext.SaveChangesAsync();

        var worker = await SeedSdkWorkerAsync(sdkDbContext, $"{Guid.NewGuid():N}@example.test");

        await sdkDbContext.SiteWorkers.AddAsync(new SiteWorker
        {
            SiteId = site.Id,
            WorkerId = worker.Id,
            WorkflowState = Constants.WorkflowStates.Created
        });
        await sdkDbContext.SaveChangesAsync();

        return (worker.Email, language.LanguageCode);
    }

    /// <summary>
    /// Seeds a bare SDK Worker row - no Site, no SiteWorker - with a given email
    /// and workflow state. Used by the population test to cover the states that
    /// don't need a site (removed, synthetic-address, already-has-an-account),
    /// by the no-site locale test, where the absence of a SiteWorker row is the
    /// point, and by <see cref="SeedSdkWorkerWithEmailAsync"/> for its Worker row.
    /// </summary>
    private static async Task<Worker> SeedSdkWorkerAsync(
        MicrotingDbContext sdkDbContext,
        string email,
        string workflowState = Constants.WorkflowStates.Created)
    {
        var worker = new Worker
        {
            FirstName = $"backfill-{Guid.NewGuid():N}",
            LastName = "Worker",
            Email = email,
            WorkflowState = workflowState
        };
        await sdkDbContext.Workers.AddAsync(worker);
        await sdkDbContext.SaveChangesAsync();
        return worker;
    }

    private async Task<EformUser> CreateUserAsync(UserManager<EformUser> userManager, string? password = null,
        string? email = null, string? userName = null)
    {
        email ??= $"{Guid.NewGuid()}@test.com";
        var user = new EformUser
        {
            Email = email,
            UserName = userName ?? email,
            FirstName = Guid.NewGuid().ToString(),
            LastName = Guid.NewGuid().ToString(),
            Locale = "da",
            EmailConfirmed = true,
            TimeZone = "Europe/Copenhagen",
            Formats = "de-DE"
        };
        var result = password == null
            ? await userManager.CreateAsync(user)
            : await userManager.CreateAsync(user, password);
        Assert.That(result.Succeeded, Is.True, string.Join(",", result.Errors.Select(e => e.Description)));
        return user;
    }

    private async Task<List<string>> GroupNamesFor(int eformUserId)
        => await (from sgu in BaseDbContext!.SecurityGroupUsers
            join sg in BaseDbContext.SecurityGroups on sgu.SecurityGroupId equals sg.Id
            where sgu.EformUserId == eformUserId
            select sg.Name).ToListAsync();
}
