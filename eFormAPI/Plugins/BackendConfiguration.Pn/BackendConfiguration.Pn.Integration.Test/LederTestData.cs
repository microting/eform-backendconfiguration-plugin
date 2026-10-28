#nullable enable
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microting.EformAngularFrontendBase.Infrastructure.Data;
using Microting.EformAngularFrontendBase.Infrastructure.Data.Entities.Permissions;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;
using NSubstitute;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// Leder fixtures. Fictional people only. Ids are always read back from what a create returns or looked up by name,
/// never assumed (Galera auto-increment offsets).
/// </summary>
public static class LederTestData
{
    /// <summary>A new group with this exact name (SecurityGroup.Name is not unique, so it always creates one).</summary>
    public static async Task<int> NewGroupAsync(BaseDbContext baseDb, string name)
    {
        var group = new SecurityGroup { Name = name, RedirectLink = "" };
        baseDb.SecurityGroups.Add(group);
        await baseDb.SaveChangesAsync();
        return group.Id;
    }

    /// <summary>The lowest-id group with this name, created when there is none.</summary>
    public static async Task<int> GroupByNameAsync(BaseDbContext baseDb, string name) =>
        await baseDb.SecurityGroups.Where(g => g.Name == name).OrderBy(g => g.Id).Select(g => (int?)g.Id)
            .FirstOrDefaultAsync() ?? await NewGroupAsync(baseDb, name);

    public static async Task AddMemberAsync(BaseDbContext baseDb, int eformUserId, int groupId)
    {
        baseDb.SecurityGroupUsers.Add(new SecurityGroupUser { EformUserId = eformUserId, SecurityGroupId = groupId });
        await baseDb.SaveChangesAsync();
    }

    /// <summary>Group names, sorted ordinally in memory (not by the DB collation): "Kun arkiv", "eForm users".</summary>
    public static async Task<List<string>> GroupNamesOfAsync(BaseDbContext baseDb, int eformUserId) =>
        (await baseDb.SecurityGroupUsers.AsNoTracking().Where(x => x.EformUserId == eformUserId)
            .Select(x => x.SecurityGroup.Name).ToListAsync()).OrderBy(x => x, StringComparer.Ordinal).ToList();

    /// <summary>A fresh non-admin login (role "user" not added), optionally in the named groups (created when missing).</summary>
    public static async Task<EformUser> LoginAsync(BaseDbContext baseDb, UserManager<EformUser> userManager,
        params string[] groupNames)
    {
        var email = $"{Guid.NewGuid():N}@example.com";
        var account = new EformUser
        {
            Email = email, UserName = email, FirstName = "Jane", LastName = "Doe", Locale = "da",
            EmailConfirmed = true, IsActive = true, TimeZone = "Europe/Copenhagen", Formats = "de-DE"
        };
        var created = await userManager.CreateAsync(account);
        Assert.That(created.Succeeded, Is.True, string.Join(",", created.Errors.Select(e => e.Description)));
        foreach (var name in groupNames)
        {
            await AddMemberAsync(baseDb, account.Id, await GroupByNameAsync(baseDb, name));
        }

        return account;
    }

    /// <summary>A caller: id, JWT admin flag, and the first-user id (0 = none known).</summary>
    public static IUserService Caller(int userId, bool isAdmin = false, int firstUserId = 0)
    {
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(userId);
        userService.IsAdmin().Returns(isAdmin);
        userService.GetFirstUserIdInDb().Returns(Task.FromResult(firstUserId));
        return userService;
    }
}
