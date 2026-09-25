using System;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.BackendConfigurationAdhocService;
using BackendConfiguration.Pn.Services.GrpcServices;
using eFormCore;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;
using NSubstitute;
using AdhocGrpc = BackendConfiguration.Pn.Grpc.Adhoc;
using TemplatesGrpc = BackendConfiguration.Pn.Grpc;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// #1326: a resigned/disabled worker with a still-valid JWT must be refused by
/// every gRPC façade with <c>Unauthenticated</c> + <c>account_disabled</c>, so
/// the app can sign out. Runs the real <see cref="GrpcSiteResolver"/> against
/// the SDK database; only the current user is stubbed.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class GrpcSiteResolverAccountDisabledTests : TestBaseSetup
{
    private sealed record SeededWorker(string Email, int SiteId, int WorkerId);

    private Core? _core;

    private async Task<Core> SharedCore() => _core ??= await GetCore();

    private async Task<SeededWorker> SeedWorkerAsync(
        string? email = null,
        bool resigned = false,
        string workflowState = Constants.WorkflowStates.Created)
    {
        // Starting Core runs the SDK migrations. Until then Workers still has the
        // 420_SDK.sql seed's shape, which predates columns such as EmployeeNo,
        // so the first test of the fixture would fail to insert a Worker.
        await SharedCore();

        email ??= $"{Guid.NewGuid():N}@example.test";
        var language = await MicrotingDbContext!.Languages.FirstAsync();
        var site = new Site
        {
            Name = "Worker A",
            LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created,
        };
        await MicrotingDbContext.Sites.AddAsync(site);
        await MicrotingDbContext.SaveChangesAsync();

        var worker = new Worker
        {
            FirstName = "Worker",
            LastName = "A",
            Email = email,
            Resigned = resigned,
            ResignedAtDate = resigned ? DateTime.UtcNow.AddDays(-1) : default,
            WorkflowState = workflowState,
        };
        await MicrotingDbContext.Workers.AddAsync(worker);
        await MicrotingDbContext.SaveChangesAsync();

        await MicrotingDbContext.SiteWorkers.AddAsync(new SiteWorker
        {
            SiteId = site.Id,
            WorkerId = worker.Id,
            WorkflowState = Constants.WorkflowStates.Created,
        });
        await MicrotingDbContext.SaveChangesAsync();

        return new SeededWorker(email, site.Id, worker.Id);
    }

    private async Task<GrpcSiteResolver> CreateResolverAsync(string email, bool isActive = true)
    {
        var core = await SharedCore();
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));

        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        userService.GetCurrentUserAsync().Returns(Task.FromResult(new EformUser
        {
            Id = 1,
            Email = email,
            IsActive = isActive,
        }));

        return new GrpcSiteResolver(userService, coreHelper, TestContextLogger<GrpcSiteResolver>.Instance);
    }

    private static void AssertAccountDisabled(RpcException? ex)
    {
        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Status.StatusCode, Is.EqualTo(StatusCode.Unauthenticated));
        Assert.That(ex.Status.Detail, Is.EqualTo("account_disabled"));
    }

    [Test]
    public async Task GetSdkSiteId_ActiveUser_ResolvesSite()
    {
        var seeded = await SeedWorkerAsync();
        var resolver = await CreateResolverAsync(seeded.Email);

        Assert.That(await resolver.GetSdkSiteIdAsync(), Is.EqualTo(seeded.SiteId));
    }

    [Test]
    public async Task GetSdkSiteId_UserIsActiveFalse_ThrowsAccountDisabled()
    {
        var seeded = await SeedWorkerAsync();
        var resolver = await CreateResolverAsync(seeded.Email, isActive: false);

        AssertAccountDisabled(Assert.ThrowsAsync<RpcException>(() => resolver.GetSdkSiteIdAsync()));
    }

    [Test]
    public async Task GetSdkSiteId_WorkerResigned_ThrowsAccountDisabled()
    {
        var seeded = await SeedWorkerAsync(resigned: true);
        var resolver = await CreateResolverAsync(seeded.Email);

        AssertAccountDisabled(Assert.ThrowsAsync<RpcException>(() => resolver.GetSdkSiteIdAsync()));
    }

    [Test]
    public async Task GetSdkSiteId_RemovedWorker_ResolvesToZero()
    {
        var seeded = await SeedWorkerAsync(workflowState: Constants.WorkflowStates.Removed);
        var resolver = await CreateResolverAsync(seeded.Email);

        Assert.That(await resolver.GetSdkSiteIdAsync(), Is.EqualTo(0));
    }

    /// <summary>
    /// A re-hired worker keeps an old, resigned Worker row with the same email.
    /// The live row must win; the caller is not disabled.
    /// </summary>
    [Test]
    public async Task GetSdkSiteId_ResignedAndActiveWorkerShareEmail_ResolvesActiveSite()
    {
        var email = $"{Guid.NewGuid():N}@example.test";
        await SeedWorkerAsync(email, resigned: true);
        var active = await SeedWorkerAsync(email);
        var resolver = await CreateResolverAsync(email);

        Assert.That(await resolver.GetSdkSiteIdAsync(), Is.EqualTo(active.SiteId));
    }

    [Test]
    public async Task EnsureCallerActive_NoWorkerForActiveUser_DoesNotThrow()
    {
        var resolver = await CreateResolverAsync($"{Guid.NewGuid():N}@example.test");

        Assert.DoesNotThrowAsync(() => resolver.EnsureCallerActiveAsync());
    }

    /// <summary>
    /// The check lives in the shared resolver, so it reaches every façade —
    /// proved here on an Adhoc RPC. The Adhoc service is never called.
    /// </summary>
    [Test]
    public async Task AdhocGetCurrentWorker_DisabledUser_ThrowsAccountDisabled()
    {
        var seeded = await SeedWorkerAsync();
        var resolver = await CreateResolverAsync(seeded.Email, isActive: false);
        var adhocService = Substitute.For<IBackendConfigurationAdhocService>();
        var sut = new AdhocGrpcService(adhocService, resolver, TestContextLogger<AdhocGrpcService>.Instance);

        AssertAccountDisabled(Assert.ThrowsAsync<RpcException>(() =>
            sut.GetCurrentWorker(new AdhocGrpc.GetCurrentWorkerRequest(), Substitute.For<ServerCallContext>())));
        Assert.That(adhocService.ReceivedCalls(), Is.Empty);
    }

    /// <summary>
    /// GetTemplate is not site-scoped and never went through the resolver's
    /// site lookup; it must refuse a resigned worker too.
    /// </summary>
    [Test]
    public async Task TemplatesGetTemplate_ResignedWorker_ThrowsAccountDisabled()
    {
        var seeded = await SeedWorkerAsync(resigned: true);
        var resolver = await CreateResolverAsync(seeded.Email);
        var sut = new TemplatesGrpcService(Substitute.For<IEFormCoreService>(), resolver);

        AssertAccountDisabled(Assert.ThrowsAsync<RpcException>(() =>
            sut.GetTemplate(new TemplatesGrpc.GetTemplateRequest(), Substitute.For<ServerCallContext>())));
    }
}
