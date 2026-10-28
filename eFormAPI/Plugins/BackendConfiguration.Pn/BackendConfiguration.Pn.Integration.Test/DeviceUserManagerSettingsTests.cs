using BackendConfiguration.Pn.Infrastructure.Helpers;
using BackendConfiguration.Pn.Infrastructure.Models;
using eFormCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.EformAngularFrontendBase.Infrastructure.Data.Entities.Permissions;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.TimePlanningBase.Infrastructure.Data.Entities;
using NSubstitute;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// A worker's manager settings (AssignedSite.IsManager and its managing tags) are only
/// changed by administrators: the "admin" role or the "eForm admins" security group.
/// Anyone else may save the worker; the manager settings then keep their stored values
/// (update) or start empty (create).
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class DeviceUserManagerSettingsTests : TestBaseSetup
{
    public enum Caller
    {
        AdminRole,
        EformAdminsGroup
    }

    [SetUp]
    public Task ReserveEformUserId1() => IdentityTestUtils.ReserveEformUserId1Async(BaseDbContext!);

    [Test]
    public async Task CreateDeviceUser_NonAdmin_DoesNotSetManagerOrManagingTags()
    {
        var ctx = await ArrangeAsync();
        var callerId = await CreateNonAdminAsync(ctx.UserManager);
        var tagId = await SeedSdkTag();

        var siteUid = await CreateWorkerAsync(ctx, callerId, isManager: true, managingTagIds: [tagId]);

        var assignedSite = await ActiveAssignedSiteAsync(siteUid);
        Assert.That(assignedSite.IsManager, Is.False);
        Assert.That(await ManagingTagIdsAsync(assignedSite.Id), Is.Empty);
    }

    [TestCase(Caller.AdminRole)]
    [TestCase(Caller.EformAdminsGroup)]
    public async Task CreateDeviceUser_Admin_SetsManagerAndManagingTags(Caller caller)
    {
        var ctx = await ArrangeAsync();
        var callerId = await CreateAdminAsync(ctx.UserManager, caller);
        var tagId = await SeedSdkTag();

        var siteUid = await CreateWorkerAsync(ctx, callerId, isManager: true, managingTagIds: [tagId]);

        var assignedSite = await ActiveAssignedSiteAsync(siteUid);
        Assert.That(assignedSite.IsManager, Is.True);
        Assert.That(await ManagingTagIdsAsync(assignedSite.Id), Is.EquivalentTo(new[] { tagId }));
    }

    [Test]
    public async Task UpdateDeviceUser_NonAdmin_KeepsStoredManagerAndTags_ButSavesOtherSettings()
    {
        var ctx = await ArrangeAsync();
        var adminId = await CreateAdminAsync(ctx.UserManager, Caller.AdminRole);
        var nonAdminId = await CreateNonAdminAsync(ctx.UserManager);
        var tagId = await SeedSdkTag();
        var siteUid = await CreateWorkerAsync(ctx, adminId, isManager: true, managingTagIds: [tagId]);
        var storedId = (await ActiveAssignedSiteAsync(siteUid)).Id;

        var otherTagId = await SeedSdkTag();
        var result = await UpdateWorkerAsync(ctx, nonAdminId, siteUid, timeRegistrationEnabled: true,
            isManager: false, managingTagIds: [otherTagId]);

        Assert.That(result.Success, Is.True, result.Message);
        var assignedSite = await ActiveAssignedSiteAsync(siteUid);
        Assert.That(assignedSite.Id, Is.EqualTo(storedId));
        Assert.That(assignedSite.IsManager, Is.True, "a non-admin save does not clear the stored manager role");
        Assert.That(assignedSite.OverMidnight, Is.True, "the other time settings are still saved");
        Assert.That(await ManagingTagIdsAsync(assignedSite.Id), Is.EquivalentTo(new[] { tagId }));
    }

    [Test]
    public async Task UpdateDeviceUser_NonAdmin_DoesNotSetManager()
    {
        var ctx = await ArrangeAsync();
        var nonAdminId = await CreateNonAdminAsync(ctx.UserManager);
        var siteUid = await CreateWorkerAsync(ctx, nonAdminId, isManager: false, managingTagIds: []);

        var tagId = await SeedSdkTag();
        var result = await UpdateWorkerAsync(ctx, nonAdminId, siteUid, timeRegistrationEnabled: true,
            isManager: true, managingTagIds: [tagId]);

        Assert.That(result.Success, Is.True, result.Message);
        var assignedSite = await ActiveAssignedSiteAsync(siteUid);
        Assert.That(assignedSite.IsManager, Is.False);
        Assert.That(await ManagingTagIdsAsync(assignedSite.Id), Is.Empty);
    }

    [TestCase(Caller.AdminRole)]
    [TestCase(Caller.EformAdminsGroup)]
    public async Task UpdateDeviceUser_Admin_SetsManager(Caller caller)
    {
        var ctx = await ArrangeAsync();
        var adminId = await CreateAdminAsync(ctx.UserManager, caller);
        var siteUid = await CreateWorkerAsync(ctx, adminId, isManager: false, managingTagIds: []);

        var result = await UpdateWorkerAsync(ctx, adminId, siteUid, timeRegistrationEnabled: true,
            isManager: true, managingTagIds: []);

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That((await ActiveAssignedSiteAsync(siteUid)).IsManager, Is.True);
    }

    /// <summary>
    /// Switching time registration off and on again creates a new AssignedSite. For a
    /// non-admin the manager role is carried across from the earlier row, not taken from
    /// the request.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task UpdateDeviceUser_NonAdminReEnablesTimeRegistration_CarriesStoredManagerAcross(bool storedIsManager)
    {
        var ctx = await ArrangeAsync();
        var adminId = await CreateAdminAsync(ctx.UserManager, Caller.AdminRole);
        var nonAdminId = await CreateNonAdminAsync(ctx.UserManager);
        var siteUid = await CreateWorkerAsync(ctx, adminId, isManager: storedIsManager, managingTagIds: []);
        var firstId = (await ActiveAssignedSiteAsync(siteUid)).Id;

        var disabled = await UpdateWorkerAsync(ctx, nonAdminId, siteUid, timeRegistrationEnabled: false,
            isManager: storedIsManager, managingTagIds: []);
        Assert.That(disabled.Success, Is.True, disabled.Message);
        var reEnabled = await UpdateWorkerAsync(ctx, nonAdminId, siteUid, timeRegistrationEnabled: true,
            isManager: !storedIsManager, managingTagIds: []);

        Assert.That(reEnabled.Success, Is.True, reEnabled.Message);
        var assignedSite = await ActiveAssignedSiteAsync(siteUid);
        Assert.That(assignedSite.Id, Is.Not.EqualTo(firstId), "precondition: a new AssignedSite was created");
        Assert.That(assignedSite.IsManager, Is.EqualTo(storedIsManager));
    }

    [Test]
    public async Task IsAdminAsync_NoBaseContext_IsFalse()
    {
        Assert.That(await BackendConfigurationAssignmentWorkerServiceHelper.IsAdminAsync(null, 1), Is.False);
    }

    private sealed record Context(Core Core, IUserService UserService, UserManager<EformUser> UserManager,
        ILogger Logger, Dictionary<int, (string FirstName, string LastName, string Email)> Workers);

    private async Task<Context> ArrangeAsync()
    {
        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);
        return new Context(await GetCore(), IdentityTestUtils.CreateRealUserService(BaseDbContext!, userManager),
            userManager, Substitute.For<ILogger>(), new());
    }

    private async Task<int> CreateWorkerAsync(Context ctx, int callerId, bool isManager, List<int> managingTagIds)
    {
        var firstName = "Jane";
        var lastName = $"Doe {Guid.NewGuid():N}";
        var email = $"{Guid.NewGuid()}@example.com";
        var result = await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(new DeviceUserModel
        {
            LanguageCode = "da",
            TimeRegistrationEnabled = true,
            UserFirstName = firstName,
            UserLastName = lastName,
            WorkerEmail = email,
            IsManager = isManager,
            ManagingTagIds = managingTagIds
        }, ctx.Core, callerId, TimePlanningPnDbContext!, BaseDbContext!, ctx.UserService, ctx.UserManager);
        Assert.That(result.Success, Is.True, result.Message);

        var siteName = $"{firstName} {lastName}";
        var site = await MicrotingDbContext!.Sites.AsNoTracking().SingleAsync(x => x.Name == siteName);
        var siteUid = (int)site.MicrotingUid!;
        ctx.Workers[siteUid] = (firstName, lastName, email);
        return siteUid;
    }

    private async Task<OperationResult> UpdateWorkerAsync(Context ctx, int callerId, int siteUid,
        bool timeRegistrationEnabled, bool isManager, List<int> managingTagIds)
    {
        var (firstName, lastName, email) = ctx.Workers[siteUid];
        return await BackendConfigurationAssignmentWorkerServiceHelper.UpdateDeviceUser(new DeviceUserModel
            {
                SiteMicrotingUid = siteUid,
                LanguageCode = "da",
                TimeRegistrationEnabled = timeRegistrationEnabled,
                OverMidnight = true,
                UserFirstName = firstName,
                UserLastName = lastName,
                WorkerEmail = email,
                IsManager = isManager,
                ManagingTagIds = managingTagIds
            }, ctx.Core, callerId, ctx.UserService, ctx.UserManager, BackendConfigurationPnDbContext!,
            TimePlanningPnDbContext!, BaseDbContext!, ctx.Logger, ItemsPlanningPnDbContext!);
    }

    private async Task<int> CreateNonAdminAsync(UserManager<EformUser> userManager)
    {
        var account = NewAccount();
        Assert.That((await userManager.CreateAsync(account)).Succeeded, Is.True);
        await AddToSecurityGroupAsync(account.Id, "eForm users");
        return account.Id;
    }

    private async Task<int> CreateAdminAsync(UserManager<EformUser> userManager, Caller caller)
    {
        var account = NewAccount();
        Assert.That((await userManager.CreateAsync(account)).Succeeded, Is.True);
        if (caller == Caller.AdminRole)
        {
            var addToRole = await userManager.AddToRoleAsync(account, EformRole.Admin);
            Assert.That(addToRole.Succeeded, Is.True, string.Join(",", addToRole.Errors.Select(e => e.Description)));
        }
        else
        {
            await AddToSecurityGroupAsync(account.Id,
                BackendConfigurationAssignmentWorkerServiceHelper.EformAdminsSecurityGroupName);
        }

        return account.Id;
    }

    private static EformUser NewAccount() => new()
    {
        Email = $"{Guid.NewGuid()}@example.com",
        UserName = $"caller-{Guid.NewGuid()}",
        FirstName = "John",
        LastName = "Doe",
        Locale = "da",
        EmailConfirmed = true,
        TimeZone = "Europe/Copenhagen",
        Formats = "de-DE"
    };

    private async Task AddToSecurityGroupAsync(int eformUserId, string groupName)
    {
        var group = await BaseDbContext!.SecurityGroups.FirstOrDefaultAsync(x => x.Name == groupName);
        if (group == null)
        {
            group = new SecurityGroup { Name = groupName };
            BaseDbContext.SecurityGroups.Add(group);
            await BaseDbContext.SaveChangesAsync();
        }

        BaseDbContext.SecurityGroupUsers.Add(new SecurityGroupUser
        {
            EformUserId = eformUserId,
            SecurityGroupId = group.Id
        });
        await BaseDbContext.SaveChangesAsync();
    }

    private async Task<int> SeedSdkTag()
    {
        var tag = new Tag
        {
            Name = $"managing-tag-{Guid.NewGuid()}",
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext!.Tags.AddAsync(tag);
        await MicrotingDbContext.SaveChangesAsync();
        return tag.Id;
    }

    private async Task<AssignedSite> ActiveAssignedSiteAsync(int siteUid) =>
        await TimePlanningPnDbContext!.AssignedSites.AsNoTracking()
            .SingleAsync(x => x.SiteId == siteUid && x.WorkflowState != Constants.WorkflowStates.Removed);

    private async Task<List<int>> ManagingTagIdsAsync(int assignedSiteId) =>
        await TimePlanningPnDbContext!.AssignedSiteManagingTags.AsNoTracking()
            .Where(x => x.AssignedSiteId == assignedSiteId && x.WorkflowState != Constants.WorkflowStates.Removed)
            .Select(x => x.TagId)
            .ToListAsync();
}
