# Property-worker logins and the "none" security group — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** An admin can set a password for a property worker as soon as that worker has an email address, because every such worker now has an `EformUser` that belongs to a permissionless `"none"` security group when it belongs to no other.

**Architecture:** Three changes inside `eform-backendconfiguration-plugin`, no core and no base repo edits and no EF migration. (1) `BackendConfigurationAssignmentWorkerServiceHelper` creates the `EformUser` for any worker carrying an email, with **no password**, instead of only for time-registration/archive/web-access workers with a shared hardcoded literal. (2) A new `EnsureFallbackSecurityGroupAsync` helper enforces "in `none` unless in another group", called after the existing group sync in both the create and update paths. (3) A startup backfill service, following the plugin's existing `RunIfNeededAsync` + `PluginConfigurationValues` marker pattern, assigns `none` to groupless users and strips the known hardcoded password.

**Tech Stack:** C# / .NET, ASP.NET Core Identity (`UserManager<EformUser>`), EF Core + Pomelo MySQL, NUnit integration tests, Angular 20, Playwright.

**Spec:** `docs/superpowers/specs/2026-09-10-property-worker-logins-and-none-group-design.md`

## Global Constraints

- Branch is `feat/property-worker-logins-none-group`, PR target `stable`. Never commit to `stable` directly.
- **No changes to `eform-angular-frontend`, `eform-angular-frontend-base`, `eFormApi.BasePn`, or any `-base` repo.** No EF migration anywhere.
- The `"none"` group name is the literal lowercase string `"none"`, matching the other programmatic groups in being an untranslated database identity.
- Synthetic `user_{workerId}_{siteId}@microting.invalid` addresses count as real emails — they are credentials, not mailboxes.
- New accounts get **no password**: `userManager.CreateAsync(user)`, the single-argument overload. Never reintroduce a password literal.
- Primary admin (`user.Id == 1`) is skipped by every membership change, matching the existing paths.
- Verify with `cd eFormAPI/Plugins/BackendConfiguration.Pn && dotnet build BackendConfiguration.Pn.Integration.Test/BackendConfiguration.Pn.Integration.Test.csproj -v q --nologo` → must report `0 Error(s)`. Integration tests and Playwright run in CI only; never claim they passed locally.
- Playwright rules in `CLAUDE.md` are binding: explicit timeout on every wait, `waitForApiResponse` for responses, no `page.waitForTimeout`, no retries, no soft assertions, container-scoped locators, no bare `.first()` / `force: true`.

---

### Task 1: The "none" group and the fallback-membership rule

**Files:**
- Modify: `eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn/Infrastructure/Helpers/BackendConfigurationAssignmentWorkerServiceHelper.cs` (add a method after `GetOrCreateSecurityGroupId`, which ends at line 110)
- Test: `eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn.Integration.Test/BackendConfigurationAssignmentWorkerServiceHelperTest.cs`

**Interfaces:**
- Consumes: the existing `private static async Task<int> GetOrCreateSecurityGroupId(BaseDbContext baseDbContext, string groupName, TimePlanningPnDbContext? = null, BackendConfigurationPnDbContext? = null)` at line 34.
- Produces: `public const string NoPermissionsSecurityGroupName = "none";` and
  `public static async Task EnsureFallbackSecurityGroupAsync(BaseDbContext baseDbContext, int eformUserId)` — used by Tasks 3 and 4.

**Why `GetOrCreateSecurityGroupId("none")` already yields zero permissions:** every `switch` in it falls through to `_ =>` for an unrecognised name — `redirectLink` becomes `""`, `coreClaimsToEnable` becomes `Array.Empty<string>()` so `EnsureCoreGroupPermissions` is never called, and the plugin-permission branches are skipped because we pass no plugin `DbContext`. Do not add `"none"` to any of those `switch` statements.

- [ ] **Step 1: Write the failing tests**

Append to `BackendConfigurationAssignmentWorkerServiceHelperTest.cs`:

```csharp
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
        var groupNames = await (from sgu in BaseDbContext!.SecurityGroupUsers
            join sg in BaseDbContext.SecurityGroups on sgu.SecurityGroupId equals sg.Id
            where sgu.EformUserId == user.Id
            select sg.Name).ToListAsync();
        Assert.That(groupNames, Is.EqualTo(new List<string>
            { BackendConfigurationAssignmentWorkerServiceHelper.NoPermissionsSecurityGroupName }));

        // The group grants nothing: no core GroupPermission rows.
        var noneGroupId = await BaseDbContext.SecurityGroups
            .Where(x => x.Name == BackendConfigurationAssignmentWorkerServiceHelper.NoPermissionsSecurityGroupName)
            .Select(x => x.Id).FirstAsync();
        Assert.That(await BaseDbContext.GroupPermissions.CountAsync(x => x.SecurityGroupId == noneGroupId),
            Is.Zero);
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
        var groupNames = await (from sgu in BaseDbContext.SecurityGroupUsers
            join sg in BaseDbContext.SecurityGroups on sgu.SecurityGroupId equals sg.Id
            where sgu.EformUserId == user.Id
            select sg.Name).ToListAsync();
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
```

Add any `using` the file lacks: `Microting.EformAngularFrontendBase.Infrastructure.Data.Entities.Permissions;` (for `SecurityGroupUser`) and `Microting.eFormApi.BasePn.Infrastructure.Database.Entities;` (for `EformUser`). Check the file's existing `using` block first — several may already be there.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd eFormAPI/Plugins/BackendConfiguration.Pn && dotnet build BackendConfiguration.Pn.Integration.Test/BackendConfiguration.Pn.Integration.Test.csproj -v q --nologo`
Expected: **FAIL** — `error CS0117: 'BackendConfigurationAssignmentWorkerServiceHelper' does not contain a definition for 'EnsureFallbackSecurityGroupAsync'` (and the same for `NoPermissionsSecurityGroupName`). A compile failure is the correct "red" here; these tests need containers to execute.

- [ ] **Step 3: Write the implementation**

Insert immediately after `GetOrCreateSecurityGroupId` ends (line 110, before `EnsureCoreGroupPermissions`):

```csharp
    /// <summary>
    /// The permissionless group every user falls back to. It exists because a user
    /// in NO group is unrepresentable through account-management —
    /// <c>AdminService.Create</c> rejects a non-admin whose GroupId does not
    /// resolve, and the create-user modal marks the group required. It changes no
    /// enforcement: <c>ClaimsService.GetUserClaims</c> already returns an empty
    /// claim list for a user with no groups, so "none" and "no group" grant
    /// exactly the same access.
    ///
    /// Deliberately absent from every switch in
    /// <see cref="GetOrCreateSecurityGroupId"/>: falling through to the default arm
    /// is what gives it an empty RedirectLink and no permission rows at all.
    /// </summary>
    public const string NoPermissionsSecurityGroupName = "none";

    /// <summary>
    /// Enforces "every user is in <c>none</c> unless they have another group".
    /// Call AFTER the eForm users / Kun arkiv / Kun tid membership sync, so it sees
    /// the final set: expressing the rule once downstream keeps it true regardless
    /// of which combination of flags moved.
    /// </summary>
    public static async Task EnsureFallbackSecurityGroupAsync(BaseDbContext baseDbContext, int eformUserId)
    {
        // The primary admin is skipped everywhere else in these paths; keep that.
        if (eformUserId == 1)
        {
            return;
        }

        var noneGroupId = await GetOrCreateSecurityGroupId(baseDbContext, NoPermissionsSecurityGroupName)
            .ConfigureAwait(false);

        var memberships = await baseDbContext.SecurityGroupUsers
            .Where(x => x.EformUserId == eformUserId)
            .ToListAsync().ConfigureAwait(false);

        var noneMemberships = memberships.Where(x => x.SecurityGroupId == noneGroupId).ToList();
        var hasRealGroup = memberships.Any(x => x.SecurityGroupId != noneGroupId);

        if (hasRealGroup)
        {
            if (noneMemberships.Count > 0)
            {
                baseDbContext.SecurityGroupUsers.RemoveRange(noneMemberships);
                await baseDbContext.SaveChangesAsync().ConfigureAwait(false);
            }

            return;
        }

        if (noneMemberships.Count == 0)
        {
            baseDbContext.SecurityGroupUsers.Add(new SecurityGroupUser
            {
                EformUserId = eformUserId,
                SecurityGroupId = noneGroupId
            });
            await baseDbContext.SaveChangesAsync().ConfigureAwait(false);
        }
    }
```

Note on `WorkflowState`: memberships are read unfiltered on purpose — `ClaimsService.GetUserClaims` also reads `SecurityGroupUsers` without a `WorkflowState` filter, so a soft-removed row still grants claims there. Filtering here would let a user be in `none` while still holding a real group's claims.

- [ ] **Step 4: Build to verify it compiles**

Run: `cd eFormAPI/Plugins/BackendConfiguration.Pn && dotnet build BackendConfiguration.Pn.Integration.Test/BackendConfiguration.Pn.Integration.Test.csproj -v q --nologo`
Expected: `0 Error(s)`

- [ ] **Step 5: Commit**

```bash
git add eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn/Infrastructure/Helpers/BackendConfigurationAssignmentWorkerServiceHelper.cs \
        eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn.Integration.Test/BackendConfigurationAssignmentWorkerServiceHelperTest.cs
git commit -m 'feat(property-workers): add permissionless "none" security group fallback'
```

---

### Task 2: Create the login for any worker with an email, with no password

**Files:**
- Modify: `eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn/Infrastructure/Helpers/BackendConfigurationAssignmentWorkerServiceHelper.cs` — the `else` block at lines 1031–1058 (`CreateDeviceUser`) and the `else` block at lines 527–551 (`UpdateDeviceUser`)
- Test: `eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn.Integration.Test/BackendConfigurationAssignmentWorkerServiceHelperTest.cs`

**Interfaces:**
- Consumes: nothing from Task 1.
- Produces: the invariant later tasks rely on — after `CreateDeviceUser` or `UpdateDeviceUser`, a worker with a non-empty `WorkerEmail` has an `EformUser` whose `PasswordHash` is `null`.

- [ ] **Step 1: Write the failing test**

```csharp
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
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: the `dotnet build` command above, then reason about the assertion: the test compiles, but with today's code no `EformUser` row is created for a worker with `TimeRegistrationEnabled = false` and no archive/web flags, so `SingleAsync` would throw. Record that this is the expected red state; CI executes it.

- [ ] **Step 3: Write the implementation**

In `CreateDeviceUser`, replace the condition and the `CreateAsync` call:

```csharp
                else
                {
                    // Any worker carrying an email gets a login, so an admin can set a
                    // password for them from property-workers. Synthetic
                    // user_{id}_{site}@microting.invalid addresses count: the address is
                    // a credential, not a mailbox.
                    if (!string.IsNullOrEmpty(deviceUserModel.WorkerEmail))
                    {
                        user = new EformUser
                        {
                            Email = deviceUserModel.WorkerEmail,
                            UserName = deviceUserModel.WorkerEmail,
                            FirstName = deviceUserModel.UserFirstName.Trim(),
                            LastName = deviceUserModel.UserLastName.Trim(),
                            Locale = deviceUserModel.LanguageCode,
                            EmailConfirmed = true,
                            TwoFactorEnabled = false,
                            IsGoogleAuthenticatorEnabled = false,
                            TimeZone = "Europe/Copenhagen",
                            Formats = "de-DE"
                        };

                        // No password on purpose: the single-argument overload leaves
                        // PasswordHash null, so CheckPasswordAsync fails and the account
                        // cannot be signed into until an admin sets one. This replaces a
                        // hardcoded literal that was identical across every deployment.
                        var result = await userManager.CreateAsync(user).ConfigureAwait(false);
                        Console.WriteLine($"[CreateDeviceUser] CreateAsync result={result.Succeeded} errors={string.Join(",", result.Errors.Select(e => e.Description))}");
                        if (result.Succeeded)
                        {
                            await userManager.AddToRoleAsync(user, EformRole.User);
                        }
                    }
                }
```

Apply the identical change to the `else` block in `UpdateDeviceUser` (lines 527–551) — same condition, same single-argument `CreateAsync`, same comment, but keep that block's existing absence of the `Console.WriteLine`.

This also removes a pre-existing guard bug: `TimeRegistrationEnabled != null` meant a client sending `timeRegistrationEnabled: null` with `WebAccessEnabled` true created no user at all.

- [ ] **Step 4: Build**

Run: the `dotnet build` command above. Expected: `0 Error(s)`. Then confirm the literal is gone: `grep -rn "Replace_me_with_a_proper_password_2024" eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn/` must return nothing.

- [ ] **Step 5: Commit**

```bash
git add eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn/Infrastructure/Helpers/BackendConfigurationAssignmentWorkerServiceHelper.cs \
        eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn.Integration.Test/BackendConfigurationAssignmentWorkerServiceHelperTest.cs
git commit -m 'feat(property-workers): give every worker with an email a login with no password set'
```

---

### Task 3: Wire the fallback group into both device-user paths

**Files:**
- Modify: `eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn/Infrastructure/Helpers/BackendConfigurationAssignmentWorkerServiceHelper.cs` — after the `"Kun tid"` sync in `UpdateDeviceUser` (the block ending near line 770) and after the `"Kun tid"` block in `CreateDeviceUser` (near line 1195)
- Test: `eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn.Integration.Test/BackendConfigurationAssignmentWorkerServiceHelperTest.cs`

**Interfaces:**
- Consumes: `EnsureFallbackSecurityGroupAsync(BaseDbContext, int)` and `NoPermissionsSecurityGroupName` from Task 1; the no-password-set invariant from Task 2.
- Produces: nothing new.

- [ ] **Step 1: Write the failing tests**

```csharp
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
        var groupNames = await (from sgu in BaseDbContext.SecurityGroupUsers
            join sg in BaseDbContext.SecurityGroups on sgu.SecurityGroupId equals sg.Id
            where sgu.EformUserId == createdUser.Id
            select sg.Name).ToListAsync();
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
        var groupNames = await (from sgu in BaseDbContext.SecurityGroupUsers
            join sg in BaseDbContext.SecurityGroups on sgu.SecurityGroupId equals sg.Id
            where sgu.EformUserId == createdUser.Id
            select sg.Name).ToListAsync();
        Assert.That(groupNames, Does.Contain("eForm users"));
        Assert.That(groupNames, Does.Not.Contain(
            BackendConfigurationAssignmentWorkerServiceHelper.NoPermissionsSecurityGroupName));
    }
```

- [ ] **Step 2: Build, then record the expected red**

Run the `dotnet build` command. It compiles; the assertions fail in CI because nothing yet adds or removes `none`.

- [ ] **Step 3: Write the implementation**

In `CreateDeviceUser`, after the `"Kun tid"` membership block closes and before the method's success return, add:

```csharp
                        // Runs after every flag-driven group sync above, so it sees the
                        // final membership set and can apply "none unless another group".
                        if (user != null)
                        {
                            await EnsureFallbackSecurityGroupAsync(baseDbContext, user.Id).ConfigureAwait(false);
                        }
```

Add the identical block in `UpdateDeviceUser` after its `"Kun tid"` sync block closes.

Placement matters: it must be downstream of every `SecurityGroupUsers` add/remove in the method, or a freshly added real group will not yet be visible and `none` will be left in place.

- [ ] **Step 4: Build**

Run the `dotnet build` command. Expected: `0 Error(s)`.

- [ ] **Step 5: Commit**

```bash
git add eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn/Infrastructure/Helpers/BackendConfigurationAssignmentWorkerServiceHelper.cs \
        eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn.Integration.Test/BackendConfigurationAssignmentWorkerServiceHelperTest.cs
git commit -m 'feat(property-workers): apply the none-group fallback on device-user save'
```

---

### Task 4: Startup backfill — assign "none", clear the retired password

**Files:**
- Create: `eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn/Services/SecurityGroupBackfillService/SecurityGroupBackfillService.cs`
- Modify: `eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn/EformBackendConfigurationPlugin.cs` — `using` block (~line 98), DI registration (~line 185), `Configure` (~line 880)
- Test: `eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn.Integration.Test/SecurityGroupBackfillServiceTest.cs`

**Interfaces:**
- Consumes: `EnsureFallbackSecurityGroupAsync` and `NoPermissionsSecurityGroupName` from Task 1.
- Produces: `public class SecurityGroupBackfillService(BackendConfigurationPnDbContext dbContext, BaseDbContext baseDbContext, UserManager<EformUser> userManager, ILogger<SecurityGroupBackfillService> logger)` with `public const string BackfillMarkerName` and `public async Task RunIfNeededAsync()`.

Follow `Services/AreaRulePlanningTagPurgeService/AreaRulePlanningTagPurgeService.cs` exactly for the marker: same `"BackendConfigurationBaseSettings:"` prefix (a section nothing binds), same conditional `INSERT ... WHERE NOT EXISTS` raw SQL, and the same reason — `PluginConfigurationValues.Name` is an unindexed longtext with no unique constraint, and `PluginConfigurationProvider.Load` throws on a duplicate key, which would make the plugin fail to load on every later boot.

- [ ] **Step 1: Write the failing test**

Create `SecurityGroupBackfillServiceTest.cs`, modelled on the existing integration-test base class (copy the `TestBaseSetup` inheritance and fixture attributes from `BackendConfigurationAssignmentWorkerServiceHelperTest.cs`):

```csharp
    // Existing groupless users predate the fallback rule; the backfill is what
    // makes "every user is in none unless they have another group" true of the
    // whole database rather than only of users saved since.
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

        var sut = new SecurityGroupBackfillService(BackendConfigurationPnDbContext!, BaseDbContext,
            userManager, Substitute.For<ILogger<SecurityGroupBackfillService>>());

        // Act
        await sut.RunIfNeededAsync();

        // Assert
        Assert.That(await GroupNamesFor(grouplessUser.Id),
            Is.EqualTo(new List<string> { BackendConfigurationAssignmentWorkerServiceHelper.NoPermissionsSecurityGroupName }));
        Assert.That(await GroupNamesFor(groupedUser.Id), Is.EqualTo(new List<string> { "eForm users" }));
    }

    // The literal was in source and therefore identical across deployments.
    // Clearing it leaves the account in place but unsignable-into.
    [Test]
    public async Task SecurityGroupBackfillService_ClearsRetiredHardcodedPassword()
    {
        // Arrange
        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);

        var legacyUser = await CreateUserAsync(userManager, "Replace_me_with_a_proper_password_2024!");
        var otherUser = await CreateUserAsync(userManager, <generated at runtime>);

        var sut = new SecurityGroupBackfillService(BackendConfigurationPnDbContext!, BaseDbContext!,
            userManager, Substitute.For<ILogger<SecurityGroupBackfillService>>());

        // Act
        await sut.RunIfNeededAsync();

        // Assert
        var reloadedLegacy = await BaseDbContext!.Users.SingleAsync(x => x.Id == legacyUser.Id);
        Assert.That(reloadedLegacy.PasswordHash, Is.Null, "the retired literal must be cleared");

        var reloadedOther = await BaseDbContext.Users.SingleAsync(x => x.Id == otherUser.Id);
        Assert.That(await userManager.CheckPasswordAsync(reloadedOther, <generated at runtime>),
            Is.True, "a password an admin actually set must survive");
    }

    // Startup calls this on every boot; the marker is what stops it re-scanning.
    [Test]
    public async Task SecurityGroupBackfillService_SecondRunIsANoOp()
    {
        // Arrange
        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);
        var sut = new SecurityGroupBackfillService(BackendConfigurationPnDbContext!, BaseDbContext!,
            userManager, Substitute.For<ILogger<SecurityGroupBackfillService>>());
        await sut.RunIfNeededAsync();

        // A user created AFTER the first run must be left alone: the marker means
        // the one-time sweep is done, and the save paths cover new users.
        var lateUser = await CreateUserAsync(userManager);

        // Act
        await sut.RunIfNeededAsync();

        // Assert
        Assert.That(await GroupNamesFor(lateUser.Id), Is.Empty);
        Assert.That(await BackendConfigurationPnDbContext!.PluginConfigurationValues
            .CountAsync(x => x.Name == SecurityGroupBackfillService.BackfillMarkerName), Is.EqualTo(1));
    }

    private async Task<EformUser> CreateUserAsync(UserManager<EformUser> userManager, string? password = null)
    {
        var email = $"{Guid.NewGuid()}@test.com";
        var user = new EformUser
        {
            Email = email,
            UserName = email,
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
```

- [ ] **Step 2: Run to verify it fails**

Run the `dotnet build` command.
Expected: **FAIL** — `error CS0246: The type or namespace name 'SecurityGroupBackfillService' could not be found`.

- [ ] **Step 3: Write the implementation**

Create `Services/SecurityGroupBackfillService/SecurityGroupBackfillService.cs`:

```csharp
using System;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Helpers;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;
using Microting.EformAngularFrontendBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;

namespace BackendConfiguration.Pn.Services.SecurityGroupBackfillService;

/// <summary>
/// One-time sweep bringing an existing database up to the two invariants the
/// device-user save paths now maintain:
///   1. every user belongs to a security group — "none" when they have no other,
///      because a groupless user is unrepresentable through account-management;
///   2. no account carries the retired hardcoded password literal.
///
/// Lives in the plugin rather than a -base migration on purpose: it touches core
/// tables (AspNetUsers, SecurityGroupUsers) through BaseDbContext without changing
/// schema, so it needs no EF migration and no base-repo edit.
/// </summary>
public class SecurityGroupBackfillService(
    BackendConfigurationPnDbContext dbContext,
    BaseDbContext baseDbContext,
    UserManager<EformUser> userManager,
    ILogger<SecurityGroupBackfillService> logger)
{
    /// <summary>
    /// The retired hardcoded password. The save paths set no password, so only
    /// pre-existing accounts carry this one. It is in source, and therefore
    /// identical across every deployment. It is never handed out — accounts
    /// carrying it are ones no admin has yet given a real password to — so
    /// clearing it locks nobody out.
    /// </summary>
    private const string RetiredHardcodedPassword = "Replace_me_with_a_proper_password_2024!";

    /// <summary>
    /// Marker recording that the one-time sweep has run. The prefix is the settings
    /// CLASS name, not the bound configuration section, matching
    /// AreaRulePlanningTagPurgeService.BacklogPurgeMarkerName verbatim: the key
    /// lands in a section nothing reads, which keeps PluginConfigurationProvider
    /// from trying to bind it to a real property.
    /// Public so the integration fixture can clear it between tests.
    /// </summary>
    public const string BackfillMarkerName =
        "BackendConfigurationBaseSettings:SecurityGroupFallbackBackfilled";

    public async Task RunIfNeededAsync()
    {
        var alreadyRun = await dbContext.PluginConfigurationValues
            .AnyAsync(x => x.Name == BackfillMarkerName);

        if (alreadyRun)
        {
            return;
        }

        var assigned = await AssignFallbackGroupToGrouplessUsersAsync();
        var cleared = await ClearRetiredPasswordsAsync();

        // Written even when nothing matched, so the scan is skipped from now on.
        // Inserted as one conditional statement rather than Add + SaveChanges:
        // PluginConfigurationValues.Name is an unindexed longtext with no unique
        // constraint, so two instances starting together would both see no marker
        // and both insert one. PluginConfigurationProvider.Load builds its
        // dictionary with ToDictionary(c => c.Name, ...), which throws on a
        // duplicate key — a second row would make the plugin fail to load on every
        // subsequent start, with nothing inside the plugin able to repair it.
        await dbContext.Database.ExecuteSqlRawAsync(
            @"INSERT INTO `PluginConfigurationValues`
                  (`Name`, `Value`, `CreatedAt`, `UpdatedAt`, `Version`,
                   `WorkflowState`, `CreatedByUserId`, `UpdatedByUserId`)
              SELECT {0}, 'true', {1}, {1}, 1, {2}, 1, 0 FROM DUAL
              WHERE NOT EXISTS (
                  SELECT 1 FROM `PluginConfigurationValues` `existing`
                  WHERE `existing`.`Name` = {0})",
            BackfillMarkerName,
            DateTime.UtcNow,
            Constants.WorkflowStates.Created);

        if (assigned > 0 || cleared > 0)
        {
            logger.LogInformation(
                "SecurityGroupBackfill: assigned the fallback group to {Assigned} users and cleared {Cleared} retired passwords at startup",
                assigned, cleared);
        }
    }

    private async Task<int> AssignFallbackGroupToGrouplessUsersAsync()
    {
        var groupedUserIds = await baseDbContext.SecurityGroupUsers
            .Select(x => x.EformUserId)
            .Distinct()
            .ToListAsync();

        var grouplessUserIds = await baseDbContext.Users
            .Where(x => !groupedUserIds.Contains(x.Id))
            .Select(x => x.Id)
            .ToListAsync();

        foreach (var userId in grouplessUserIds)
        {
            await BackendConfigurationAssignmentWorkerServiceHelper
                .EnsureFallbackSecurityGroupAsync(baseDbContext, userId);
        }

        return grouplessUserIds.Count;
    }

    private async Task<int> ClearRetiredPasswordsAsync()
    {
        // The hash is per-user salted, so accounts carrying the literal cannot be
        // found by comparing hash values — each needs its own verification. That
        // makes this the expensive half of the sweep, which is exactly what the
        // marker confines to a single boot.
        var usersWithPasswords = await baseDbContext.Users
            .Where(x => x.PasswordHash != null)
            .ToListAsync();

        var cleared = 0;
        foreach (var user in usersWithPasswords)
        {
            if (!await userManager.CheckPasswordAsync(user, RetiredHardcodedPassword))
            {
                continue;
            }

            var result = await userManager.RemovePasswordAsync(user);
            if (result.Succeeded)
            {
                cleared++;
            }
            else
            {
                logger.LogWarning(
                    "SecurityGroupBackfill: could not clear the retired password for user {UserId}: {Errors}",
                    user.Id, string.Join(", ", result.Errors.Select(e => e.Description)));
            }
        }

        return cleared;
    }
}
```

Then in `EformBackendConfigurationPlugin.cs`:
- add `using Services.SecurityGroupBackfillService;` beside the other backfill usings (~line 98);
- add `services.AddTransient<SecurityGroupBackfillService>();` beside the others (~line 185);
- in `Configure`, after the `AreaRulePlanningTagPurgeService` try/catch, add:

```csharp
        // One-time sweep bringing existing users up to the "none unless another
        // group" rule and clearing the retired hardcoded password. Gated on its own
        // PluginConfigurationValues marker. Wrapped because this hook blocks startup
        // synchronously: a backfill failure must not stop the plugin from loading.
        try
        {
            var securityGroupBackfill = scope.ServiceProvider
                .GetRequiredService<SecurityGroupBackfillService>();
            securityGroupBackfill.RunIfNeededAsync().GetAwaiter().GetResult();
        }
        catch (Exception e)
        {
            Console.WriteLine($"SecurityGroupBackfill failed at startup: {e}");
        }
```

- [ ] **Step 4: Build**

Run the `dotnet build` command. Expected: `0 Error(s)`.

- [ ] **Step 5: Commit**

```bash
git add eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn/Services/SecurityGroupBackfillService/SecurityGroupBackfillService.cs \
        eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn/EformBackendConfigurationPlugin.cs \
        eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn.Integration.Test/SecurityGroupBackfillServiceTest.cs
git commit -m 'feat(property-workers): backfill the none group and clear the retired password'
```

---

### Task 5: Offer "Set password" as soon as a worker has an email

**Files:**
- Modify: `eform-client/src/app/plugins/modules/backend-configuration-pn/modules/property-workers/components/property-worker-table/property-worker-table.component.html:149-167`
- Create: `eform-client/playwright/e2e/plugins/backend-configuration-pn/r/property-worker-set-password-gate.spec.ts`

**Interfaces:**
- Consumes: the invariant from Tasks 2–3 — a worker with an email has an `EformUser`, so these menu items cannot resolve to a null user.
- Produces: nothing.

- [ ] **Step 1: Write the failing Playwright spec**

Create `r/property-worker-set-password-gate.spec.ts`:

```ts
import { expect, test } from '@playwright/test';
import { LoginPage } from '../../../Page objects/Login.page';
import { generateRandmString } from '../../../helper-functions';
import {
  BackendConfigurationPropertiesPage,
  PropertyCreateUpdate,
} from '../BackendConfigurationProperties.page';
import {
  BackendConfigurationPropertyWorkersPage,
  PropertyWorker,
} from '../BackendConfigurationPropertyWorkers.page';
import { openRowActionMenu } from '../row-action-menu';
import { UI_TIMEOUT } from '../wait-helpers';

// "Set password" / "Send reset password email" in the property-worker row menu.
//
// WHAT THIS PROTECTS: the server now creates an EformUser for ANY worker carrying
// an email — not just time-registration/archive/web-access workers — so there is
// something to set a password on in every one of those cases. The menu items must
// therefore be offered for exactly that condition.
//
// WHY IT MATTERS: gated on the old three flags, an admin had no way to give a
// plain worker a login even though the account existed. Gated too widely, the
// items would resolve to a null user and AccountService.AdminChangePassword would
// throw rather than report anything useful.

const rand = generateRandmString(4);

const property: PropertyCreateUpdate = {
  name: `Pwd ${rand}`,
  chrNumber: generateRandmString(5),
  address: generateRandmString(5),
  cvrNumber: '1111111',
};

// Email, and none of time registration / archive / web access.
const emailOnlyWorker: PropertyWorker = {
  name: `Pwo${rand}`,
  surname: 'Emailonly',
  language: 'Dansk',
  properties: [property.name],
  workerEmail: `${generateRandmString(5)}@test.com`,
};
const emailOnlyWorkerFullName = `${emailOnlyWorker.name} ${emailOnlyWorker.surname}`;

test.describe('Property-worker password affordance follows the email', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:4200');
    // login() already waits for #newEFormBtn, so the app is loaded when it returns.
    await new LoginPage(page).login();
  });

  test('a worker with only an email is offered Set password', async ({ page }) => {
    // 5 min: login (up to 2 min on a cold app), one property create, and one
    // device-user create whose SDK provisioning call is the slow part, then a
    // single menu open. Every wait inside is individually bounded; this is only
    // their sum.
    test.setTimeout(300000);

    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(property);

    const workersPage = new BackendConfigurationPropertyWorkersPage(page);
    await workersPage.goToPropertyWorkers();
    await workersPage.create(emailOnlyWorker);

    const row = page.locator('.mat-mdc-row').filter({ hasText: emailOnlyWorkerFullName });
    await expect(row, 'the created worker must show up in the device-user table')
      .toHaveCount(1, { timeout: UI_TIMEOUT });

    // mat-menu content is projected into a CDK overlay on <body>, so the helper
    // addresses items by the row's action-items-<i> index rather than scoping to
    // the row, which would find nothing at all.
    const menuItem = await openRowActionMenu(page, row, emailOnlyWorkerFullName);

    await expect(
      menuItem('setPasswordBtn'),
      'a worker with an email must be offered "Set password"'
    ).toBeVisible({ timeout: UI_TIMEOUT });

    await expect(
      menuItem('sendResetPasswordEmailBtn'),
      'a worker with an email must be offered "Send reset password email"'
    ).toBeVisible({ timeout: UI_TIMEOUT });
  });
});
```

Before writing it, read `r/property-worker-one-minute-intervals.spec.ts` and confirm the page-object method names above still match (`goToProperties`, `createProperty`, `goToPropertyWorkers`, `create`) — reuse them rather than writing new flows.

- [ ] **Step 2: Confirm it would fail today**

Read the current `*ngIf` — `row.timeRegistrationEnabled || row.webAccessEnabled || row.archiveEnabled` — and confirm a worker with none of those flags renders neither button, so the assertion fails. Playwright runs in CI only; do not attempt a local run.

- [ ] **Step 3: Change the gate**

In `property-worker-table.component.html`, change the `*ngIf` on **both** `setPasswordBtn-{{i}}` and `sendResetPasswordEmailBtn-{{i}}` from

```html
*ngIf="row.timeRegistrationEnabled || row.webAccessEnabled || row.archiveEnabled"
```

to

```html
*ngIf="row.workerEmail"
```

Change nothing else in the menu — the surrounding items (Show assignments, Edit employee, Delete employee) and the OTP/QR device-pairing items keep their conditions.

- [ ] **Step 4: Verify no other gate depends on the old condition**

Run: `grep -n "setPasswordBtn\|sendResetPasswordEmailBtn" -r eform-client/src eform-client/playwright eform-client/e2e`
Expected: the two template ids, the two `.ts` handlers, and the new spec. Confirm no existing spec asserts the buttons are *absent* for an email-only worker; if one does, update it to the new contract rather than weakening the new spec.

- [ ] **Step 5: Commit**

```bash
git add eform-client/src/app/plugins/modules/backend-configuration-pn/modules/property-workers/components/property-worker-table/property-worker-table.component.html \
        eform-client/playwright/e2e/plugins/backend-configuration-pn/r/property-worker-set-password-gate.spec.ts
git commit -m 'feat(property-workers): offer Set password once a worker has an email'
```

---

## Finishing

After Task 5, run the cycle's remaining steps from the workspace `CLAUDE.md`:

1. Remind the user to test in the browser.
2. **Dual review gate, both dispatched in parallel:** `superpowers:requesting-code-review` and a `code-simplifier` subagent, over the whole branch diff (`git diff stable...HEAD`). Act on the findings.
3. Push the branch and open a PR toward `stable` with `gh pr create`.

The PR description must state that the backfill clears the retired hardcoded password on first boot after deploy, and that no one is locked out by it because the literal was never handed out.
