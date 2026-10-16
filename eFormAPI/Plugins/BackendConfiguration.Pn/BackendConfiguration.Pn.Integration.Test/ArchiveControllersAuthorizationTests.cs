#nullable enable
using System.Reflection;
using System.Security.Claims;
using BackendConfiguration.Pn.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// D5/D17: folder management and read access are admin-only; reading the tree, moving and dating documents stay
/// open to the user role. Evaluates the effective policy (class + action attributes), as
/// PropertyAdminActionsAuthorizationTests does. No database.
/// </summary>
[TestFixture]
public class ArchiveControllersAuthorizationTests
{
    private IAuthorizationService _authorizationService = null!;
    private IAuthorizationPolicyProvider _policyProvider = null!;
    private ServiceProvider _serviceProvider = null!;

    [OneTimeSetUp]
    public void BuildAuthorization()
    {
        _serviceProvider = new ServiceCollection().AddLogging().AddAuthorizationCore().BuildServiceProvider();
        _authorizationService = _serviceProvider.GetRequiredService<IAuthorizationService>();
        _policyProvider = new DefaultAuthorizationPolicyProvider(Options.Create(new AuthorizationOptions()));
    }

    [OneTimeTearDown]
    public void DisposeAuthorization() => _serviceProvider.Dispose();

    private static ClaimsPrincipal Principal(string role) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Name, $"{role}@example.test"), new Claim(ClaimTypes.Role, role)],
            authenticationType: "Test"));

    private static MethodInfo Action<TVerb>(Type controller, string name) where TVerb : HttpMethodAttribute =>
        controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Single(m => m.Name == name && m.GetCustomAttributes<TVerb>().Any());

    private async Task<bool> IsAllowed(Type controller, MethodInfo action, string role)
    {
        var authorizeData = controller.GetCustomAttributes(true).OfType<IAuthorizeData>()
            .Concat(action.GetCustomAttributes(true).OfType<IAuthorizeData>()).ToList();
        Assert.That(authorizeData, Is.Not.Empty, $"{controller.Name}.{action.Name} would be anonymous.");
        var policy = await AuthorizationPolicy.CombineAsync(_policyProvider, authorizeData);
        return (await _authorizationService.AuthorizeAsync(Principal(role), null, policy!)).Succeeded;
    }

    private static IEnumerable<TestCaseData> AdminOnly()
    {
        var c = typeof(ArchiveFoldersController);
        yield return new TestCaseData(c, Action<HttpPostAttribute>(c, nameof(ArchiveFoldersController.Create)))
            .SetName("AdminOnly_POST_archive-folders");
        yield return new TestCaseData(c, Action<HttpPutAttribute>(c, nameof(ArchiveFoldersController.Update)))
            .SetName("AdminOnly_PUT_archive-folders-id");
        yield return new TestCaseData(c, Action<HttpDeleteAttribute>(c, nameof(ArchiveFoldersController.Delete)))
            .SetName("AdminOnly_DELETE_archive-folders-id");
        yield return new TestCaseData(c, Action<HttpPutAttribute>(c, nameof(ArchiveFoldersController.UpdateAccess)))
            .SetName("AdminOnly_PUT_archive-folders-id-access");
        var files = typeof(FilesController);
        yield return new TestCaseData(files, Action<HttpPutAttribute>(files, nameof(FilesController.UpdateAccess)))
            .SetName("AdminOnly_PUT_files-access");
    }

    private static IEnumerable<TestCaseData> OpenToUser()
    {
        var c = typeof(ArchiveFoldersController);
        yield return new TestCaseData(c, Action<HttpGetAttribute>(c, nameof(ArchiveFoldersController.Index)))
            .SetName("OpenToUser_GET_archive-folders");
        var files = typeof(FilesController);
        yield return new TestCaseData(files, Action<HttpPutAttribute>(files, nameof(FilesController.Move)))
            .SetName("OpenToUser_PUT_files-folder");
        yield return new TestCaseData(files, Action<HttpPutAttribute>(files, nameof(FilesController.UpdateDate)))
            .SetName("OpenToUser_PUT_files-id-date");
    }

    [TestCaseSource(nameof(AdminOnly))]
    public async Task AdminOnlyAction_RejectsUser_AcceptsAdmin(Type controller, MethodInfo action)
    {
        Assert.That(await IsAllowed(controller, action, EformRole.User), Is.False);
        Assert.That(await IsAllowed(controller, action, EformRole.Admin), Is.True);
    }

    [TestCaseSource(nameof(OpenToUser))]
    public async Task UserAction_AcceptsUserAndAdmin(Type controller, MethodInfo action)
    {
        Assert.That(await IsAllowed(controller, action, EformRole.User), Is.True);
        Assert.That(await IsAllowed(controller, action, EformRole.Admin), Is.True);
    }

    /// <summary>Spec section 13: no archive action may answer an anonymous caller (401 regression, no HTTP host).</summary>
    [TestCaseSource(typeof(ArchiveTestData), nameof(ArchiveTestData.ArchiveControllers))]
    public void ArchiveController_IsAuthorized_AndHasNoAnonymousAction(Type controller)
    {
        Assert.That(controller.GetCustomAttributes<AuthorizeAttribute>(), Is.Not.Empty,
            $"{controller.Name} must carry [Authorize] on the class.");
        Assert.That(controller.GetCustomAttributes<AllowAnonymousAttribute>(), Is.Empty);
        Assert.That(controller.GetMethods().Where(m => m.GetCustomAttributes<AllowAnonymousAttribute>().Any()),
            Is.Empty);
    }
}
