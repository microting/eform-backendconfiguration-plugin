#nullable enable
using System.Reflection;
using BackendConfiguration.Pn.Controllers;
using BackendConfiguration.Pn.Infrastructure.Models.ArchiveFolders;
using BackendConfiguration.Pn.Services.ArchiveAccess;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using NSubstitute;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>D-L7: what the caller may do in the archive, for the web and the app. No database.</summary>
[TestFixture]
public class ArchiveCapabilitiesTests
{
    private const int FirstUser = 3;

    private static ArchiveCapabilitiesController Controller(ArchiveAccessContext ctx, int userId, int firstUserId = FirstUser)
    {
        var access = Substitute.For<IArchiveAccessService>();
        access.GetEnabledContextAsync().Returns(Task.FromResult(ctx));
        return new ArchiveCapabilitiesController(access, LederTestData.Caller(userId, ctx.IsAdmin, firstUserId));
    }

    private static IEnumerable<TestCaseData> Roles()
    {
        yield return new TestCaseData(ArchiveTestData.User(4), 9, false, false).SetName("NormalUser_NothingToManage");
        // Kept both: Leder is a non-admin (IsAdmin=false on the caller), Admin is the JWT admin; both must report CanManage.
        yield return new TestCaseData(ArchiveTestData.Leder, 9, true, false).SetName("Leder_ManagesButIsNotFirstUser");
        yield return new TestCaseData(ArchiveTestData.Admin, 9, true, false).SetName("Admin_Manages");
        yield return new TestCaseData(ArchiveTestData.Admin, FirstUser, true, true).SetName("FirstUser_IsFirstUser");
        yield return new TestCaseData(ArchiveTestData.User(), FirstUser, false, true).SetName("FirstUserWithoutManage_StillFirstUser");
    }

    [TestCaseSource(nameof(Roles))]
    public async Task Get_ReportsThePerRoleFlags(ArchiveAccessContext ctx, int userId, bool manage, bool firstUser)
    {
        var result = await Controller(ctx, userId).Get();

        Assert.That(result.Success, Is.True);
        Assert.That(result.Model.CanManage, Is.EqualTo(manage));
        Assert.That(result.Model.CanDelete, Is.EqualTo(manage), "today canDelete == canManage");
        Assert.That(result.Model.CanUseInbox, Is.EqualTo(manage), "today canUseInbox == canManage");
        Assert.That(result.Model.IsFirstUser, Is.EqualTo(firstUser));
    }

    [Test]
    public async Task Get_NoUserId_IsNotFirstUser_EvenOnAnEmptyUsersTable()
    {
        var result = await Controller(ArchiveTestData.User(), userId: 0, firstUserId: 0).Get();
        Assert.That(result.Model.IsFirstUser, Is.False);
    }

    [Test]
    public void Envelope_SerialisesToTheContractShape()
    {
        var json = JsonConvert.SerializeObject(
            new OperationDataResult<ArchiveCapabilitiesModel>(true,
                new ArchiveCapabilitiesModel { CanManage = true, CanDelete = true, CanUseInbox = true, IsFirstUser = false }),
            new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() });

        var root = JObject.Parse(json);
        Assert.That(root["success"]!.Value<bool>(), Is.True);
        Assert.That(root["model"]!.ToString(Formatting.None),
            Is.EqualTo("{\"canManage\":true,\"canDelete\":true,\"canUseInbox\":true,\"isFirstUser\":false}"));
    }

    [Test]
    public void Controller_IsSignedIn_D7Filtered_NotCached_AndRoutedPerContract()
    {
        var c = typeof(ArchiveCapabilitiesController);
        Assert.That(c.GetCustomAttributes<AuthorizeAttribute>().Single().Roles, Is.Null.Or.Empty);
        Assert.That(c.IsDefined(typeof(ArchiveAccessFilterAttribute)), Is.True);
        Assert.That(c.IsDefined(typeof(ArchiveManagerAttribute)), Is.False, "everyone may ask what they may do");
        Assert.That(c.GetCustomAttribute<RouteAttribute>()!.Template,
            Is.EqualTo("api/backend-configuration-pn/archive/capabilities"));
        var cache = c.GetMethod(nameof(ArchiveCapabilitiesController.Get))!.GetCustomAttribute<ResponseCacheAttribute>();
        Assert.That(cache?.NoStore, Is.True);
    }
}
