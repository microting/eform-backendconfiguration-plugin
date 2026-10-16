#nullable enable
using BackendConfiguration.Pn.Services.ArchiveAccess;
using BackendConfiguration.Pn.Services.WorkerTagMembership;
using eFormCore;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;
using NSubstitute;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// D7 and userGroups against the real SDK database: who is disabled (same choice as GrpcSiteResolver, #1326) and
/// which medarbejdergrupper a user is in (live membership, #1213). Only the current user is stubbed.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class ArchiveAccessContextTests : TestBaseSetup
{
    private Core _core = null!;

    [SetUp]
    public async Task StartCore() => _core = await GetCore();

    private MicrotingDbContext Sdk() => _core.DbContextHelper.GetDbContext();

    private async Task<int> TagAsync(string name)
    {
        var sdk = Sdk();
        var tag = new Tag { Name = name, WorkflowState = Constants.WorkflowStates.Created };
        await sdk.Tags.AddAsync(tag);
        await sdk.SaveChangesAsync();
        return tag.Id;
    }

    private async Task<int> WorkerAsync(string email, int[]? tagIds = null, bool resigned = false)
    {
        var sdk = Sdk();
        var language = await sdk.Languages.FirstAsync();
        var site = new Site { Name = $"site-{Guid.NewGuid():N}", LanguageId = language.Id, WorkflowState = Constants.WorkflowStates.Created };
        await sdk.Sites.AddAsync(site);
        await sdk.SaveChangesAsync();
        var worker = new Worker
        {
            FirstName = "Jane", LastName = "Doe", Email = email, Resigned = resigned,
            ResignedAtDate = resigned ? DateTime.UtcNow.AddDays(-1) : default,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await sdk.Workers.AddAsync(worker);
        await sdk.SaveChangesAsync();
        await sdk.SiteWorkers.AddAsync(new SiteWorker { SiteId = site.Id, WorkerId = worker.Id, WorkflowState = Constants.WorkflowStates.Created });
        foreach (var tagId in tagIds ?? [])
        {
            await sdk.SiteTags.AddAsync(new SiteTag { SiteId = site.Id, TagId = tagId, WorkflowState = Constants.WorkflowStates.Created });
        }

        await sdk.SaveChangesAsync();
        return site.Id;
    }

    private ArchiveAccessService Service(string? email, bool isActive = true, bool isAdmin = false)
    {
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(_core));
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        userService.IsAdmin().Returns(isAdmin);
        userService.GetCurrentUserAsync().Returns(Task.FromResult(new EformUser { Id = 1, Email = email, IsActive = isActive }));
        return new ArchiveAccessService(BackendConfigurationPnDbContext!, userService, coreHelper,
            new WorkerTagMembershipService(coreHelper, BackendConfigurationPnDbContext),
            TestContextLogger<ArchiveAccessService>.Instance);
    }

    private static string Email() => $"{Guid.NewGuid():N}@example.test";

    [Test]
    public async Task Context_LiveWorkerInGroup_HasTheGroup()
    {
        var ledelse = await TagAsync("Ledelse");
        var email = Email();
        await WorkerAsync(email, [ledelse]);

        var ctx = await Service(email).GetContextAsync();

        Assert.That(ctx.IsDisabled, Is.False);
        Assert.That(ctx.IsAdmin, Is.False);
        Assert.That(ctx.UserGroupIds, Is.EquivalentTo(new[] { ledelse }));
    }

    [Test]
    public async Task Context_AdminComesFromTheUserService()
    {
        var ctx = await Service(Email(), isAdmin: true).GetContextAsync();

        Assert.That(ctx.IsAdmin, Is.True);
        Assert.That(ctx.IsDisabled, Is.False);
    }

    [Test]
    public async Task Context_IsActiveFalse_IsDisabledEvenForAdmin()
    {
        var email = Email();
        await WorkerAsync(email);

        var ctx = await Service(email, isActive: false, isAdmin: true).GetContextAsync();

        Assert.That(ctx.IsDisabled, Is.True);
    }

    [Test]
    public async Task Context_ResignedWorker_IsDisabled()
    {
        var email = Email();
        await WorkerAsync(email, resigned: true);

        var ctx = await Service(email, isAdmin: true).GetContextAsync();

        Assert.That(ctx.IsDisabled, Is.True);
    }

    [Test]
    public async Task Context_ReHiredWorker_IsNotDisabled_AndGetsTheLiveSitesGroups()
    {
        var bogholderi = await TagAsync("Bogholderi");
        var email = Email();
        await WorkerAsync(email, resigned: true);
        await WorkerAsync(email, [bogholderi]);

        var ctx = await Service(email).GetContextAsync();

        Assert.That(ctx.IsDisabled, Is.False);
        Assert.That(ctx.UserGroupIds, Is.EquivalentTo(new[] { bogholderi }));
    }

    [Test]
    public async Task Context_NoLinkedWorker_HasNoGroups_IsNotDisabled()
    {
        var ctx = await Service(Email()).GetContextAsync();

        Assert.That(ctx.IsDisabled, Is.False);
        Assert.That(ctx.UserGroupIds, Is.Empty);
    }

    [Test]
    public async Task Context_RemovedSiteTag_IsNotAGroup()
    {
        var tagId = await TagAsync("Udgået gruppe");
        var email = Email();
        var siteId = await WorkerAsync(email, [tagId]);
        var sdk = Sdk();
        var link = await sdk.SiteTags.SingleAsync(st => st.SiteId == siteId && st.TagId == tagId);
        link.WorkflowState = Constants.WorkflowStates.Removed;
        await sdk.SaveChangesAsync();

        var ctx = await Service(email).GetContextAsync();

        Assert.That(ctx.UserGroupIds, Is.Empty);
    }

    [Test]
    public async Task Context_IsResolvedOncePerService()
    {
        var service = Service(Email());

        var first = await service.GetContextAsync();
        var second = await service.GetContextAsync();

        Assert.That(second, Is.SameAs(first));
    }

    [Test]
    public async Task GroupNames_AndLiveGroups_ComeFromTheSdk()
    {
        var drift = await TagAsync("Drift");
        var empty = await TagAsync("Uden medlemmer");
        // A colleague in Drift makes Drift live; the caller is someone else and plays no part.
        await WorkerAsync(Email(), [drift]);
        var service = Service(Email());

        var names = await service.GetGroupNamesAsync([drift, empty]);
        var live = await service.GetLiveGroupIdsAsync();

        Assert.That(names[drift], Is.EqualTo("Drift"));
        Assert.That(live, Does.Contain(drift));
        Assert.That(live, Does.Not.Contain(empty));
        Assert.That(await service.AreAssignableGroupsAsync([drift, empty], [empty]), Is.True);
        Assert.That(await service.AreAssignableGroupsAsync([empty], []), Is.False);
    }
}
