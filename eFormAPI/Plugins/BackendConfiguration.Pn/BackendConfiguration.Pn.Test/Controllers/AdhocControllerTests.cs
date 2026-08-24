using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Controllers;
using BackendConfiguration.Pn.Infrastructure.Models.Adhoc;
using BackendConfiguration.Pn.Services.BackendConfigurationAdhocService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using Microsoft.AspNetCore.Http;
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
/// The controller consults no user/role service to decide visibility, so
/// nothing about the caller's role can narrow — or widen — what a web user
/// sees. The mobile gRPC path is deliberately unaffected; its own hardcoded
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
