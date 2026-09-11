using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Helpers;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;
using Microting.EformAngularFrontendBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;

namespace BackendConfiguration.Pn.Services.SecurityGroupBackfillService;

/// <summary>
/// One-time sweep bringing an existing database up to the three invariants the
/// device-user save paths maintain:
///   1. every worker carrying an email has an EformUser, so the property-workers
///      "Set password" affordance - which is gated on the worker's email, read off
///      the SDK Worker row rather than off AspNetUsers - always has an account
///      behind it;
///   2. every non-admin user belongs to a security group - "none" when they have
///      no other, because a groupless user is unrepresentable through
///      account-management;
///   3. no account carries the retired hardcoded password literal.
///
/// Lives in the plugin rather than a -base migration on purpose: it touches core
/// tables (AspNetUsers, SecurityGroupUsers) through BaseDbContext without changing
/// schema, so it needs no EF migration and no base-repo edit.
///
/// Every step persists incrementally, and the first two steps' driving queries are
/// self-narrowing - a user that has been given a group no longer matches, a worker
/// whose email exists in AspNetUsers no longer matches - so a pod killed before the
/// marker is written repeats strictly less of them.
///
/// The password sweep is the exception: an account whose password is NOT the
/// retired literal still matches `PasswordHash != null`, so it is verified again
/// on the next boot. That step therefore repeats in full after an interrupted run,
/// and it is the one to suspect if a large first boot ever trips a liveness probe.
/// Batching bounds its memory, not its total time.
/// </summary>
public class SecurityGroupBackfillService(
    BackendConfigurationPnDbContext dbContext,
    BaseDbContext baseDbContext,
    UserManager<EformUser> userManager,
    IEFormCoreService coreHelper,
    ILogger<SecurityGroupBackfillService> logger)
{
    /// <summary>
    /// The retired hardcoded password. The save paths set no password, so only
    /// pre-existing accounts carry this one. It is in source, and therefore identical
    /// across every deployment. It is not handed out — accounts carrying it are ones
    /// no admin has yet given a real password to — so clearing it locks nobody out.
    /// </summary>
    private const string RetiredHardcodedPassword = "Replace_me_with_a_proper_password_2024!";

    /// <summary>
    /// How many rows each step holds in memory, and how much work is lost if the
    /// process dies mid-step. Also bounds the parameter count of the per-batch
    /// email lookup: MySQL caps a prepared statement at 65535 parameters, so an
    /// IN list built from a whole table is a query that stops working once the
    /// deployment is big enough - which is exactly the deployment that needs this.
    /// </summary>
    private const int BatchSize = 200;

    /// <summary>
    /// The SDK's own default language, used when a worker has no site to read one
    /// from. <c>Site.AddLanguage</c> assigns <c>"da"</c> to every site missing a
    /// language, so this is the code such a worker's site would end up with.
    /// </summary>
    private const string DefaultLanguageCode = "da";

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
        var created = await CreateMissingUsersForWorkersWithEmailAsync();
        var cleared = await ClearRetiredPasswordsAsync();

        // Written even when nothing matched, so every later boot skips the scan.
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

        if (assigned > 0 || created > 0 || cleared > 0)
        {
            logger.LogInformation(
                "SecurityGroupBackfill: assigned the fallback group to {Assigned} users, created {Created} logins for workers with an email and cleared {Cleared} retired passwords at startup",
                assigned, created, cleared);
        }
    }

    private async Task<int> AssignFallbackGroupToGrouplessUsersAsync()
    {
        // Anti-join evaluated by the server. Materialising every grouped user id and
        // filtering with Contains renders NOT IN (…) with one parameter per id,
        // which stops working at MySQL's 65535-parameter ceiling.
        //
        // Admins are excluded because core strips group memberships from admins:
        // they are most of the groupless population a real database has, putting
        // them in "none" writes a state the system immediately un-writes, and it
        // would list every admin under /security.
        var grouplessUserIds = await baseDbContext.Users
            .Where(u => !baseDbContext.SecurityGroupUsers.Any(s => s.EformUserId == u.Id))
            .Where(u => !baseDbContext.UserRoles.Any(ur => ur.UserId == u.Id
                                                          && baseDbContext.Roles.Any(r =>
                                                              r.Id == ur.RoleId && r.Name == EformRole.Admin)))
            .Select(u => u.Id)
            .ToListAsync();

        var assigned = 0;
        foreach (var userId in grouplessUserIds)
        {
            // Count what was actually written: the helper no-ops for the primary
            // admin, so the size of the candidate list overstates the work done.
            if (await BackendConfigurationAssignmentWorkerServiceHelper
                    .EnsureFallbackSecurityGroupAsync(baseDbContext, userId))
            {
                assigned++;
            }
        }

        return assigned;
    }

    /// <summary>
    /// Gives every pre-existing worker with an email the login the save paths
    /// create on save.
    ///
    /// WHY: property-workers offers "Set password" for any row whose
    /// <c>workerEmail</c> is set, and that field is projected off the SDK Worker
    /// row, not off AspNetUsers. Without this step the affordance appears for every
    /// worker that existed before deploy - exactly the population the feature is
    /// for - and clicking it reaches core's AccountService.AdminChangePassword,
    /// which calls RemovePasswordAsync on the result of GetByUsernameAsync with no
    /// null check: an ArgumentNullException and an HTTP 500.
    /// </summary>
    private async Task<int> CreateMissingUsersForWorkersWithEmailAsync()
    {
        var core = await coreHelper.GetCore().ConfigureAwait(false);
        await using var sdkDbContext = core.DbContextHelper.GetDbContext();

        var created = 0;
        var lastWorkerId = 0;

        while (true)
        {
            // Keyset pagination on the worker id: bounded memory, and every batch
            // that completes is progress a killed pod keeps, because a worker whose
            // email resolves to a user is skipped on the next run.
            var workers = await sdkDbContext.Workers
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.Email != null && x.Email != "")
                .Where(x => x.Id > lastWorkerId)
                .OrderBy(x => x.Id)
                .Take(BatchSize)
                .Select(x => new
                {
                    x.Id,
                    x.Email,
                    x.FirstName,
                    x.LastName,
                    // The save paths take the locale from the model's LanguageCode,
                    // which is the site's language. A Worker carries no language of
                    // its own, so read it off the site it is assigned to and fall
                    // back to the SDK's own default when it has none.
                    LanguageCode = (from sw in sdkDbContext.SiteWorkers
                        join s in sdkDbContext.Sites on sw.SiteId equals (int?)s.Id
                        join l in sdkDbContext.Languages on s.LanguageId equals l.Id
                        where sw.WorkerId == x.Id
                              && sw.WorkflowState != Constants.WorkflowStates.Removed
                        select l.LanguageCode).FirstOrDefault()
                })
                .ToListAsync();

            if (workers.Count == 0)
            {
                break;
            }

            lastWorkerId = workers[^1].Id;

            // One round trip per batch to find which of these emails already have an
            // account, instead of pulling every worker email and every user email
            // into memory to compare them.
            var batchEmails = workers.Select(x => x.Email).Distinct().ToList();
            var takenEmails = new HashSet<string>(
                await baseDbContext.Users
                    .Where(u => u.Email != null && batchEmails.Contains(u.Email))
                    .Select(u => u.Email)
                    .ToListAsync(),
                StringComparer.OrdinalIgnoreCase);

            foreach (var worker in workers)
            {
                if (!takenEmails.Add(worker.Email))
                {
                    // Already has a login, or an earlier worker in this batch shares
                    // the address and has just been given one.
                    continue;
                }

                var user = new EformUser
                {
                    Email = worker.Email,
                    UserName = worker.Email,
                    FirstName = (worker.FirstName ?? string.Empty).Trim(),
                    LastName = (worker.LastName ?? string.Empty).Trim(),
                    Locale = string.IsNullOrEmpty(worker.LanguageCode)
                        ? DefaultLanguageCode
                        : worker.LanguageCode,
                    EmailConfirmed = true,
                    TwoFactorEnabled = false,
                    IsGoogleAuthenticatorEnabled = false,
                    TimeZone = "Europe/Copenhagen",
                    Formats = "de-DE"
                };

                // No password, exactly as the save paths do it: the single-argument
                // overload leaves PasswordHash null, so the account exists and can be
                // given a password by an admin but cannot be signed into before that.
                var result = await userManager.CreateAsync(user).ConfigureAwait(false);
                if (!result.Succeeded)
                {
                    // A legacy row Identity refuses - a non-ASCII local part, an
                    // address another user already holds. Log it and keep going: one
                    // bad row must not cost the whole sweep, which would otherwise
                    // restart from scratch on the next boot.
                    logger.LogWarning(
                        "SecurityGroupBackfill: could not create a login for worker {WorkerId}: {Errors}",
                        worker.Id, string.Join(", ", result.Errors.Select(e => e.Description)));
                    continue;
                }

                await userManager.AddToRoleAsync(user, EformRole.User).ConfigureAwait(false);
                await BackendConfigurationAssignmentWorkerServiceHelper
                    .EnsureFallbackSecurityGroupAsync(baseDbContext, user.Id)
                    .ConfigureAwait(false);
                created++;
            }

            if (workers.Count < BatchSize)
            {
                break;
            }
        }

        return created;
    }

    private async Task<int> ClearRetiredPasswordsAsync()
    {
        // The hash is per-user salted, so accounts carrying the literal cannot be
        // found by comparing hash values — each needs its own verification. That
        // makes this the expensive half of the sweep, which is exactly what the
        // marker confines to a single boot. Paged for the same reason as the step
        // above: bounded memory, and a cleared password stops matching, so a pod
        // killed mid-sweep resumes with less to do.
        var cleared = 0;
        var lastUserId = 0;

        while (true)
        {
            var users = await baseDbContext.Users
                .Where(x => x.PasswordHash != null)
                .Where(x => x.Id > lastUserId)
                .OrderBy(x => x.Id)
                .Take(BatchSize)
                .ToListAsync();

            if (users.Count == 0)
            {
                break;
            }

            lastUserId = users[^1].Id;

            foreach (var user in users)
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

            if (users.Count < BatchSize)
            {
                break;
            }
        }

        return cleared;
    }
}
