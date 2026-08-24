using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Controllers;
using BackendConfiguration.Pn.Infrastructure.Models.Adhoc;
using BackendConfiguration.Pn.Services.BackendConfigurationAdhocService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microting.EformBackendConfigurationBase.Infrastructure.Const;
using NSubstitute;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Test.Controllers;

/// <summary>
/// Access-policy tests for <see cref="AdhocController"/>: the shared
/// <see cref="IBackendConfigurationAdhocService"/> is faked (NSubstitute), so
/// these exercise only what the REST façade forwards as caller identity — no
/// DB, no S3.
///
/// The pinned property (2026-08-24): every route passes the synthetic
/// dashboard worker id <c>0</c> AND full access <c>true</c>, unconditionally.
/// All 23 routes are pinned one test each - tasks (index/history/get/create/
/// update/copy/completed/archive/reopen/delete/comment), reference data
/// (properties/workers/areas + the three area mutations), tags (list/create/
/// rename/delete) and photos (upload/get) - so adding a route without a
/// matching test is the only way the claim can go stale. The controller
/// consults no user/role service to decide visibility, so nothing about the
/// caller's role can narrow — or widen — what a web user sees. What DOES
/// bound the reach is the class-level authorization policy —
/// pinned by <see cref="Controller_RequiresBackendConfigurationPluginAccessPolicy"/>
/// — which keeps a user with no backend-configuration access out entirely.
/// The mobile gRPC path is deliberately unaffected; its own hardcoded
/// <c>isAdmin == false</c> expectations live in
/// <c>GrpcServices/AdhocGrpcServiceMappingTests</c>.
/// </summary>
[TestFixture]
public class AdhocControllerTests
{
    private const int DashboardWorkerId = 0;
    private const bool FullAccess = true;

    private static AdhocController CreateSut(
        IBackendConfigurationAdhocService adhocService = null,
        IBackendConfigurationLocalizationService localizationService = null)
    {
        adhocService ??= Substitute.For<IBackendConfigurationAdhocService>();
        localizationService ??= Substitute.For<IBackendConfigurationLocalizationService>();
        return new AdhocController(adhocService, localizationService);
    }

    // ---- the controller has no user/role dependency at all ----

    [Test]
    public void Constructor_TakesNoUserOrRoleService()
    {
        var parameterTypes = typeof(AdhocController)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(p => p.ParameterType.Name)
            .ToList();

        Assert.That(parameterTypes, Is.EquivalentTo(new[]
        {
            nameof(IBackendConfigurationAdhocService),
            nameof(IBackendConfigurationLocalizationService)
        }));
    }

    // ---- tasks ----

    [Test]
    public async Task Index_PassesDashboardWorkerZeroAndFullAccess()
    {
        var adhocService = Substitute.For<IBackendConfigurationAdhocService>();
        var sut = CreateSut(adhocService);

        await sut.Index(new AdhocTaskFiltersModel());

        await adhocService.Received(1).IndexTasks(DashboardWorkerId, FullAccess, Arg.Any<AdhocTaskFiltersModel>());
    }

    [Test]
    public async Task GetTask_PassesDashboardWorkerZeroAndFullAccess()
    {
        var adhocService = Substitute.For<IBackendConfigurationAdhocService>();
        var sut = CreateSut(adhocService);

        await sut.GetTask(42);

        await adhocService.Received(1).GetTask(DashboardWorkerId, 42, FullAccess);
    }

    [Test]
    public async Task CreateTask_PassesDashboardWorkerZeroAndFullAccess()
    {
        var adhocService = Substitute.For<IBackendConfigurationAdhocService>();
        var model = new AdhocTaskCreateModel { Title = "Fix the leak", PropertyId = 10 };
        var sut = CreateSut(adhocService);

        await sut.CreateTask(model);

        await adhocService.Received(1).CreateTask(DashboardWorkerId, model, FullAccess);
    }

    [Test]
    public async Task UpdateTask_PassesDashboardWorkerZeroAndFullAccess()
    {
        var adhocService = Substitute.For<IBackendConfigurationAdhocService>();
        var model = new AdhocTaskCreateModel { Title = "Fix the leak", PropertyId = 10 };
        var sut = CreateSut(adhocService);

        await sut.UpdateTask(42, model);

        await adhocService.Received(1).UpdateTask(DashboardWorkerId, 42, model, FullAccess);
    }

    [Test]
    public async Task DeleteTask_PassesDashboardWorkerZeroAndFullAccess()
    {
        var adhocService = Substitute.For<IBackendConfigurationAdhocService>();
        var sut = CreateSut(adhocService);

        await sut.DeleteTask(42);

        await adhocService.Received(1).Delete(DashboardWorkerId, 42, FullAccess);
    }

    [Test]
    public async Task HistoryIndex_PassesDashboardWorkerZeroAndFullAccess()
    {
        var adhocService = Substitute.For<IBackendConfigurationAdhocService>();
        var sut = CreateSut(adhocService);

        await sut.HistoryIndex(new AdhocHistoryFiltersModel());

        await adhocService.Received(1)
            .ListHistory(DashboardWorkerId, FullAccess, Arg.Any<AdhocHistoryFiltersModel>());
    }

    [Test]
    public async Task CopyTask_PassesDashboardWorkerZeroAndFullAccess()
    {
        var adhocService = Substitute.For<IBackendConfigurationAdhocService>();
        var sut = CreateSut(adhocService);

        await sut.CopyTask(42, new AdhocCopyTaskModel { IncludeComments = true });

        await adhocService.Received(1).CopyTask(DashboardWorkerId, FullAccess, 42, true);
    }

    [Test]
    public async Task SetCompleted_PassesDashboardWorkerZeroAndFullAccess()
    {
        var adhocService = Substitute.For<IBackendConfigurationAdhocService>();
        var sut = CreateSut(adhocService);

        await sut.SetCompleted(42, new AdhocSetCompletedModel { Completed = true, CompletedByWorkerId = 7 });

        await adhocService.Received(1).SetCompleted(DashboardWorkerId, 42, true, FullAccess, 7);
    }

    [Test]
    public async Task Archive_PassesDashboardWorkerZeroAndFullAccess()
    {
        var adhocService = Substitute.For<IBackendConfigurationAdhocService>();
        var sut = CreateSut(adhocService);

        await sut.Archive(42);

        await adhocService.Received(1).Archive(DashboardWorkerId, 42, FullAccess);
    }

    [Test]
    public async Task Reopen_PassesDashboardWorkerZeroAndFullAccess()
    {
        var adhocService = Substitute.For<IBackendConfigurationAdhocService>();
        var sut = CreateSut(adhocService);

        await sut.Reopen(42);

        await adhocService.Received(1).Reopen(DashboardWorkerId, 42, FullAccess);
    }

    [Test]
    public async Task AddComment_PassesDashboardWorkerZeroAndFullAccess()
    {
        var adhocService = Substitute.For<IBackendConfigurationAdhocService>();
        var sut = CreateSut(adhocService);

        await sut.AddComment(42, new AdhocCommentCreateModel { Text = "looked at it" });

        await adhocService.Received(1).AddComment(DashboardWorkerId, 42, "looked at it", FullAccess);
    }

    // ---- reference data ----
    //
    // properties/workers are the "sees more than their own" surface: full
    // access makes them return every property and every worker name for the
    // customer, regardless of the caller's PropertyWorker rows. The area
    // mutations reach the same way.

    [Test]
    public async Task ListProperties_PassesDashboardWorkerZeroAndFullAccess()
    {
        var adhocService = Substitute.For<IBackendConfigurationAdhocService>();
        var sut = CreateSut(adhocService);

        await sut.ListProperties();

        await adhocService.Received(1).ListProperties(DashboardWorkerId, FullAccess);
    }

    [Test]
    public async Task ListWorkers_PassesDashboardWorkerZeroAndFullAccess()
    {
        var adhocService = Substitute.For<IBackendConfigurationAdhocService>();
        var sut = CreateSut(adhocService);

        await sut.ListWorkers(10);

        await adhocService.Received(1).ListWorkers(DashboardWorkerId, 10, FullAccess);
    }

    [Test]
    public async Task ListAreas_PassesDashboardWorkerZeroAndFullAccess()
    {
        var adhocService = Substitute.For<IBackendConfigurationAdhocService>();
        var sut = CreateSut(adhocService);

        await sut.ListAreas(10);

        await adhocService.Received(1).ListAreas(DashboardWorkerId, 10, FullAccess);
    }

    [Test]
    public async Task CreateAreas_PassesDashboardWorkerZeroAndFullAccess()
    {
        var adhocService = Substitute.For<IBackendConfigurationAdhocService>();
        var model = new AdhocAreaCreateModel { PropertyId = 10, Names = ["Barn", "Stald"] };
        var sut = CreateSut(adhocService);

        await sut.CreateAreas(model);

        await adhocService.Received(1).CreateAreas(DashboardWorkerId, 10, model.Names, FullAccess);
    }

    [Test]
    public async Task RenameArea_PassesDashboardWorkerZeroAndFullAccess()
    {
        var adhocService = Substitute.For<IBackendConfigurationAdhocService>();
        var sut = CreateSut(adhocService);

        await sut.RenameArea(5, new AdhocAreaRenameModel { Name = "Lade" });

        await adhocService.Received(1).RenameArea(DashboardWorkerId, 5, "Lade", FullAccess);
    }

    [Test]
    public async Task DeleteArea_PassesDashboardWorkerZeroAndFullAccess()
    {
        var adhocService = Substitute.For<IBackendConfigurationAdhocService>();
        var sut = CreateSut(adhocService);

        await sut.DeleteArea(5);

        await adhocService.Received(1).DeleteArea(DashboardWorkerId, 5, FullAccess);
    }

    // ---- tags ----
    //
    // The widest-reaching consequence of the 2026-08-24 change: full access
    // makes ListTags return every worker's personal tags, makes CreateTag write
    // a global tag (OwnerWorkerId = null) that shows up in every mobile
    // worker's tag list, and lets rename/delete act on a tag a worker created
    // on their phone. These pin that the controller forwards full access on all
    // four - if one ever stops, the web tag semantics change silently.

    [Test]
    public async Task ListTags_PassesDashboardWorkerZeroAndFullAccess()
    {
        var adhocService = Substitute.For<IBackendConfigurationAdhocService>();
        var sut = CreateSut(adhocService);

        await sut.ListTags();

        await adhocService.Received(1).ListTags(DashboardWorkerId, FullAccess);
    }

    [Test]
    public async Task CreateTag_PassesDashboardWorkerZeroAndFullAccess()
    {
        var adhocService = Substitute.For<IBackendConfigurationAdhocService>();
        var sut = CreateSut(adhocService);

        await sut.CreateTag(new AdhocTagCreateModel { Name = "Roof" });

        await adhocService.Received(1).CreateTag(DashboardWorkerId, "Roof", FullAccess);
    }

    [Test]
    public async Task RenameTag_PassesDashboardWorkerZeroAndFullAccess()
    {
        var adhocService = Substitute.For<IBackendConfigurationAdhocService>();
        var sut = CreateSut(adhocService);

        await sut.RenameTag(7, new AdhocTagCreateModel { Name = "Facade" });

        await adhocService.Received(1).RenameTag(DashboardWorkerId, 7, "Facade", FullAccess);
    }

    [Test]
    public async Task DeleteTag_PassesDashboardWorkerZeroAndFullAccess()
    {
        var adhocService = Substitute.For<IBackendConfigurationAdhocService>();
        var sut = CreateSut(adhocService);

        await sut.DeleteTag(7);

        await adhocService.Received(1).DeleteTag(DashboardWorkerId, 7, FullAccess);
    }

    // ---- the plugin boundary is enforced server-side ----

    [Test]
    public void Controller_RequiresBackendConfigurationPluginAccessPolicy()
    {
        var authorize = typeof(AdhocController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: false)
            .Cast<AuthorizeAttribute>()
            .Single();

        // "Unrestricted" means unrestricted for users OF THIS PLUGIN. Without
        // the policy, any authenticated eForm user could call these routes
        // directly and get customer-wide tasks, properties and worker names -
        // the Angular route guard does not stop a REST call.
        Assert.That(authorize.Policy,
            Is.EqualTo(BackendConfigurationClaims.AccessBackendConfigurationPlugin));
    }

    // ---- photos ----

    [Test]
    public async Task UploadPhoto_PassesDashboardWorkerZeroAndFullAccess()
    {
        var adhocService = Substitute.For<IBackendConfigurationAdhocService>();
        var file = Substitute.For<IFormFile>();
        file.Length.Returns(3L);
        file.ContentType.Returns("image/png");
        file.CopyToAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var sut = CreateSut(adhocService);

        await sut.UploadPhoto(42, file);

        await adhocService.Received(1)
            .SavePhoto(DashboardWorkerId, 42, Arg.Any<byte[]>(), "image/png", FullAccess);
    }

    [Test]
    public async Task GetPhoto_PassesDashboardWorkerZeroAndFullAccess()
    {
        var adhocService = Substitute.For<IBackendConfigurationAdhocService>();
        adhocService.GetPhoto(DashboardWorkerId, 99, FullAccess)
            .Returns(((Stream)new MemoryStream([1, 2, 3]), "image/png"));
        var sut = CreateSut(adhocService);

        await sut.GetPhoto(99);

        await adhocService.Received(1).GetPhoto(DashboardWorkerId, 99, FullAccess);
    }
}
