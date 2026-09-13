# Property-worker logins and the "none" security group

**Date:** 2026-09-10
**Repo:** `eform-backendconfiguration-plugin`
**Status:** Approved design, not yet implemented

## Problem

On `/plugins/backend-configuration-pn/property-workers` an admin should be able to set a
password for a worker as soon as that worker has an email address. Today they cannot, for
three reasons:

1. **No user to set a password on.** `BackendConfigurationAssignmentWorkerServiceHelper`
   creates an `EformUser` only when
   `TimeRegistrationEnabled != null && (TimeRegistrationEnabled || ArchiveEnabled || WebAccessEnabled)`.
   A worker with an email but none of those flags has an SDK `Site` + `Worker` and nothing
   in `AspNetUsers`. The row-menu items *Set password* and *Send reset password email*
   already exist in `property-worker-table.component.html`, but are gated on those same
   three flags — and if invoked without a user behind them,
   `AccountService.AdminChangePassword` dereferences a null user.

2. **A user with no security group is unrepresentable through account-management.**
   `AdminService.Create` rejects a non-admin user whose `GroupId` does not resolve
   (`"SecurityGroupNotFound"`), and the create-user modal declares
   `groupId: [null, Validators.required]`. There is no group meaning "no permissions".

3. **Every login this plugin creates shares one known password.** Both `CreateDeviceUser`
   and `UpdateDeviceUser` call
   `userManager.CreateAsync(user, "Replace_me_with_a_proper_password_2024!")`.
   That literal is in the source, and therefore on every such account in every deployment.

## Context established by research

- **A user in no group already has zero permissions.** `ClaimsService.GetUserClaims` skips
  its entire body when `groups.Any()` is false, returning an empty claim list. Login
  succeeds, `LoginRedirectUrl` is null, the menu renders empty and every
  `[Authorize(Policy = ...)]` endpoint returns 403. So the "none" group changes no
  enforcement — its purpose is to make such a user *legal* and manageable.
- Two exceptions default to *allow* for a groupless user and are unaffected by this change:
  `EformPermissionsService.CheckEform` returns `true` when no group holds the eForm, and
  `MenuService` shows menu items that have no `MenuItemSecurityGroups` rows.
- The plugin already creates security groups on demand via `GetOrCreateSecurityGroupId`
  (`"eForm users"`, `"Kun arkiv"`, `"Kun tid"`), mirroring `deploy_and_configure.py`.
- Security-group entities live in `eform-angular-frontend-base`; the `/security` UI lives in
  core `eform-angular-frontend`. **Neither is modified by this design.**

## Decisions

| Question | Decision |
|---|---|
| What is "none" for? | Both a placeholder that makes the user legal, and a real login with zero admin permissions |
| When does a worker get a user? | Automatically, as soon as the worker has an email |
| Do synthetic `@microting.invalid` emails count? | Yes — the address is only a credential, it need not be deliverable |
| Initial password | None. The account exists but cannot be logged into until an admin sets one |
| Where does "none" live? | The plugin. Not a base repo, not core, no EF migration |
| Existing groupless users | Backfilled |
| Existing hardcoded passwords | Cleared — the literal was never handed out, so no one is locked out |

## Design

### 1. The "none" group

Created through the plugin's existing
`GetOrCreateSecurityGroupId(baseDbContext, "none")`. No new mechanism is introduced: every
`switch` in that helper already falls through to `_ =>` for an unrecognised name, producing
`RedirectLink = ""`, no core `GroupPermission` rows and no plugin `PluginGroupPermission`
rows. That is exactly "no permissions". Call it without the optional plugin `DbContext`
arguments so no plugin permission rows are created either.

The name is the literal `"none"`, matching the other programmatic groups in being an
untranslated database identity.

### 2. `EformUser` creation

In both `CreateDeviceUser` and `UpdateDeviceUser`, replace the three-flag condition with
"the worker has an email". Synthetic `user_{workerId}_{siteId}@microting.invalid`
addresses qualify.

Create with **no password**:

```csharp
var result = await userManager.CreateAsync(user).ConfigureAwait(false);
```

The single-argument overload leaves `PasswordHash` null, so `CheckPasswordAsync` fails and
the account cannot be signed into until an admin sets a password. Role assignment
(`AddToRoleAsync(user, EformRole.User)`) is unchanged. The hardcoded literal is deleted
from both call sites.

Note the pre-existing guard bug this also removes: `TimeRegistrationEnabled != null` meant
a client sending `timeRegistrationEnabled: null` with `WebAccessEnabled` true created no
user at all.

### 3. Group membership rule

A new helper — `EnsureFallbackSecurityGroupAsync(BaseDbContext, int eformUserId)` — is
called at the end of both paths, *after* the existing `eForm users` / `Kun arkiv` /
`Kun tid` membership sync:

- the user belongs to no non-removed group → add `none`
- the user belongs to any other group → remove `none`

Expressing the rule once, downstream of the flag sync, is what keeps "all users are in
`none` unless they have other groups" true regardless of which combination of flags moved.
As elsewhere in these paths, user id 1 (the primary admin) is skipped.

### 4. Password affordance in property-workers

`property-worker-table.component.html` — the `*ngIf` on `setPasswordBtn-{{i}}` and
`sendResetPasswordEmailBtn-{{i}}` changes from
`row.timeRegistrationEnabled || row.webAccessEnabled || row.archiveEnabled`
to the worker having an email. Because every such worker now has a backing `EformUser`,
these items cannot resolve to a null user.

### 5. Backfill

An idempotent routine in the plugin's startup, following the pattern
`EformMyMicrotingPlugin` already uses for its portal groups, guarded by a plugin config key
so it performs its work once:

1. assign `none` to every `EformUser` with no non-removed `SecurityGroupUser` row;
2. for every user, `CheckPasswordAsync` against the known literal and, on a match,
   `RemovePasswordAsync`.

This satisfies both constraints at once: it lives in the plugin, and it touches core tables
through the injected `BaseDbContext` rather than adding schema — so it does not violate
"migrations live only in `-base` repos".

**No lockout risk.** The literal was never handed out — no account has ever been signed
into with it. Every account that carries it is one an admin has not yet given a real
password to, so clearing it removes a credential nobody is using. Step 2 is pure hardening
and needs no announced deploy or coordination.

**Performance:** step 2 is one PBKDF2 verification per user — the hash is per-user salted,
so matching accounts cannot be found by comparing hash values directly. On a large
deployment this makes one boot noticeably slower. The config-key guard confines it to a
single run.

## Testing

Integration tests (`BackendConfiguration.Pn.Integration.Test`):

- a worker created with only an email gets an `EformUser` whose `PasswordHash` is null
- that user is in `none` and in no other group
- enabling web access moves the user into `eForm users` and removes `none`
- disabling it again restores `none`
- a worker with a synthetic `@microting.invalid` email is treated identically
- the backfill assigns `none` only to groupless users, and is a no-op on a second run
- the backfill clears a password matching the literal and leaves other passwords intact

Playwright (`eform-client/playwright/e2e/plugins/backend-configuration-pn/`):

- *Set password* is offered for a worker that has an email and none of the three flags
- setting a password through it succeeds

Per the repo's Playwright rules: explicit timeouts on every wait, `waitForApiResponse` for
responses, no `page.waitForTimeout`, no retries, container-scoped locators.

## Out of scope

- `AccountService.AdminChangePassword`'s missing null check (core repo). This design makes
  the NRE unreachable from property-workers, but the latent bug remains; fixing it is a
  separate core change.
- Surfacing password state as a column in the property-workers list.
- The dead `openResetPasswordModal` / `GET /api/admin/reset-password/{id}` pair in core,
  which calls an endpoint that does not exist.
