#nullable enable
using System.Reflection;
using System.Security.Claims;
using BackendConfiguration.Pn.Controllers;
using BackendConfiguration.Pn.Services.ArchiveAccess;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microting.eFormApi.BasePn.Abstractions;
using NSubstitute;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>D-L6, H4: the management gate. A live CanManage check, never a role or a policy. No database.</summary>
[TestFixture]
public class ArchiveManagerAttributeTests
{
    internal static ClaimsPrincipal SignedIn() =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "jane.doe@example.com")], authenticationType: "Test"));

    private static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());

    internal static AuthorizationFilterContext Context(ClaimsPrincipal user, IArchiveAccessService access,
        IUserService? userService = null)
    {
        var services = new ServiceCollection().AddSingleton(access);
        if (userService != null)
        {
            services.AddSingleton(userService);
        }

        var http = new DefaultHttpContext { User = user, RequestServices = services.BuildServiceProvider() };
        return new AuthorizationFilterContext(new ActionContext(http, new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>());
    }

    private static IArchiveAccessService Access(ArchiveAccessContext ctx)
    {
        var access = Substitute.For<IArchiveAccessService>();
        access.GetContextAsync().Returns(Task.FromResult(ctx));
        return access;
    }

    [Test]
    public async Task ArchiveManager_Unauthenticated_DoesNothing()
    {
        // H4: [Authorize] must answer 401 so the app renews its token; a 403 account_disabled would sign it out.
        var access = Access(ArchiveTestData.Disabled);
        var context = Context(Anonymous(), access);

        await new ArchiveManagerAttribute().OnAuthorizationAsync(context);

        Assert.That(context.Result, Is.Null);
        await access.DidNotReceive().GetContextAsync();
    }

    [Test]
    public async Task ArchiveManager_DisabledCaller_Is403AccountDisabled()
    {
        var context = Context(SignedIn(), Access(ArchiveTestData.Disabled));

        await new ArchiveManagerAttribute().OnAuthorizationAsync(context);

        var result = context.Result as ObjectResult;
        Assert.That(result?.StatusCode, Is.EqualTo(StatusCodes.Status403Forbidden));
        Assert.That(result!.Value, Is.EqualTo(new Dictionary<string, string> { ["error"] = "account_disabled" }));
    }

    [Test]
    public async Task ArchiveManager_NormalUser_IsAPlain403()
    {
        var context = Context(SignedIn(), Access(ArchiveTestData.User(5)));

        await new ArchiveManagerAttribute().OnAuthorizationAsync(context);

        Assert.That(context.Result, Is.TypeOf<StatusCodeResult>()
            .With.Property(nameof(StatusCodeResult.StatusCode)).EqualTo(StatusCodes.Status403Forbidden));
    }

    [Test]
    public async Task ArchiveManager_LederAndAdmin_Pass()
    {
        foreach (var ctx in new[] { ArchiveTestData.Leder, ArchiveTestData.Admin })
        {
            var context = Context(SignedIn(), Access(ctx));
            await new ArchiveManagerAttribute().OnAuthorizationAsync(context);
            Assert.That(context.Result, Is.Null);
        }
    }

    private static readonly Type[] ArchiveRouteControllers =
        [typeof(ArchiveFoldersController), typeof(FilesController), typeof(FileTagsController), typeof(InboxController)];

    private static MethodInfo FindAction(Type controller, string action) =>
        controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Single(m => m.Name == action);

    private static bool HasManagerGate(Type controller, string action) =>
        controller.IsDefined(typeof(ArchiveManagerAttribute)) || FindAction(controller, action).IsDefined(typeof(ArchiveManagerAttribute));

    private static IEnumerable<(Type Controller, string Action)> ManagerList()
    {
        var folders = typeof(ArchiveFoldersController);
        yield return (folders, nameof(ArchiveFoldersController.Create));
        yield return (folders, nameof(ArchiveFoldersController.Update));
        yield return (folders, nameof(ArchiveFoldersController.Delete));
        yield return (folders, nameof(ArchiveFoldersController.UpdateAccess));
        yield return (typeof(FilesController), nameof(FilesController.UpdateAccess));
        yield return (typeof(FilesController), nameof(FilesController.Delete));
        yield return (typeof(FileTagsController), nameof(FileTagsController.Update));
        yield return (typeof(FileTagsController), nameof(FileTagsController.Delete));
        var inbox = typeof(InboxController);
        yield return (inbox, nameof(InboxController.List));
        yield return (inbox, nameof(InboxController.Get));
        yield return (inbox, nameof(InboxController.Pdf));
        yield return (inbox, nameof(InboxController.FileDocument));
        yield return (inbox, nameof(InboxController.Undo));
        yield return (inbox, nameof(InboxController.Reject));
        yield return (inbox, nameof(InboxController.GetSettings));
        yield return (inbox, nameof(InboxController.PutSettings));
        yield return (inbox, nameof(InboxController.Rotate));
    }

    private static IEnumerable<(Type Controller, string Action)> OpenList()
    {
        yield return (typeof(ArchiveFoldersController), nameof(ArchiveFoldersController.Index));
        var files = typeof(FilesController);
        yield return (files, nameof(FilesController.Index));
        yield return (files, nameof(FilesController.UpdateName));
        yield return (files, nameof(FilesController.UpdateTags));
        yield return (files, nameof(FilesController.UpdateProperties));
        yield return (files, nameof(FilesController.Move));
        yield return (files, nameof(FilesController.UpdateDate));
        yield return (files, nameof(FilesController.Create));
        yield return (files, nameof(FilesController.GetById));
        yield return (files, nameof(FilesController.GetLoginPageImage));
        yield return (files, nameof(FilesController.GetArchiveFiles));
        var tags = typeof(FileTagsController);
        yield return (tags, nameof(FileTagsController.Index));
        yield return (tags, nameof(FileTagsController.Create));
        yield return (tags, nameof(FileTagsController.GetById));
        yield return (tags, nameof(FileTagsController.BulkFileTags));
    }

    private static IEnumerable<TestCaseData> ManagerRoutes() =>
        ManagerList().Select(r => new TestCaseData(r.Controller, r.Action).SetName($"Manager_{r.Controller.Name}_{r.Action}"));

    private static IEnumerable<TestCaseData> OpenRoutes() =>
        OpenList().Select(r => new TestCaseData(r.Controller, r.Action).SetName($"Open_{r.Controller.Name}_{r.Action}"));

    [TestCaseSource(nameof(ManagerRoutes))]
    public void ManagerRoute_CarriesTheGate_AndNoAdminRole(Type controller, string action)
    {
        Assert.That(HasManagerGate(controller, action), Is.True);
        var roles = controller.GetCustomAttributes(true).OfType<IAuthorizeData>()
            .Concat(FindAction(controller, action).GetCustomAttributes(true).OfType<IAuthorizeData>())
            .Select(a => a.Roles).Where(r => !string.IsNullOrEmpty(r));
        Assert.That(roles, Is.Empty, "D-L6: Roles = Admin is replaced, not kept beside the gate");
    }

    [TestCaseSource(nameof(OpenRoutes))]
    public void OpenRoute_HasNoGate(Type controller, string action) =>
        Assert.That(HasManagerGate(controller, action), Is.False);

    [Test]
    public void EveryRouteIsClassifiedExactlyOnce_SoANewActionForcesADecision()
    {
        var classified = ManagerList().Concat(OpenList()).ToList();
        Assert.That(classified, Is.Unique, "an action is listed as both manager and open, or twice");

        var declared = ArchiveRouteControllers
            .SelectMany(c => c.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes(true).OfType<IActionHttpMethodProvider>().Any())
                .Select(m => (Controller: c, Action: m.Name)));
        Assert.That(classified, Is.EquivalentTo(declared),
            "add the new action to ManagerList (needs [ArchiveManager]) or OpenList");
    }

    [Test]
    public void WorkerTags_HasNoArchiveFilter_SoCalendarPagesKeepWorking()
    {
        // M1 / S3: worker-tags also serves the task list, calendar and task modal.
        Assert.That(typeof(WorkerTagsController).IsDefined(typeof(ArchiveAccessFilterAttribute)), Is.False);
        Assert.That(typeof(WorkerTagsController).IsDefined(typeof(ArchiveManagerAttribute)), Is.False);
    }

    [Test]
    public void Rotate_CarriesBothTheManagerGateAndTheFirstUserGate()
    {
        var rotate = FindAction(typeof(InboxController), nameof(InboxController.Rotate));
        Assert.That(typeof(InboxController).IsDefined(typeof(ArchiveManagerAttribute)), Is.True);
        Assert.That(rotate.IsDefined(typeof(FirstUserOnlyAttribute)), Is.True);
    }

    [Test]
    public async Task Rotate_LederWhoIsNotTheFirstUser_PassesTheGate_ButFirstUserOnlyRefuses()
    {
        var access = Access(ArchiveTestData.Leder);
        var users = LederTestData.Caller(7, firstUserId: 3);

        var gate = Context(SignedIn(), access, users);
        await new ArchiveManagerAttribute().OnAuthorizationAsync(gate);
        Assert.That(gate.Result, Is.Null);

        var first = Context(SignedIn(), access, users);
        await new FirstUserOnlyAttribute().OnAuthorizationAsync(first);
        Assert.That(first.Result, Is.TypeOf<StatusCodeResult>()
            .With.Property(nameof(StatusCodeResult.StatusCode)).EqualTo(StatusCodes.Status403Forbidden));
    }

    [Test]
    public async Task Rotate_LederWhoIsTheFirstUser_PassesBoth()
    {
        var access = Access(ArchiveTestData.Leder);
        var users = LederTestData.Caller(3, firstUserId: 3);

        foreach (var filter in new IAsyncAuthorizationFilter[] { new ArchiveManagerAttribute(), new FirstUserOnlyAttribute() })
        {
            var context = Context(SignedIn(), access, users);
            await filter.OnAuthorizationAsync(context);
            Assert.That(context.Result, Is.Null, filter.GetType().Name);
        }
    }

    [Test]
    public async Task Rotate_DisabledCaller_GetsTheAccountDisabledBodyFromTheGate()
    {
        var context = Context(SignedIn(), Access(ArchiveTestData.Disabled), LederTestData.Caller(3, firstUserId: 3));

        await new ArchiveManagerAttribute().OnAuthorizationAsync(context);

        var result = context.Result as ObjectResult;
        Assert.That(result?.StatusCode, Is.EqualTo(StatusCodes.Status403Forbidden));
        Assert.That(result!.Value, Is.EqualTo(new Dictionary<string, string> { ["error"] = "account_disabled" }));
    }
}
