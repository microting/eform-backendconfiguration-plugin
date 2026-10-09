#nullable enable
using BackendConfiguration.Pn.Infrastructure.Models.Files;
using BackendConfiguration.Pn.Services.BackendConfigurationFileTagsService;
using BackendConfiguration.Pn.Services.InboundMail;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.Common;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using NSubstitute;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>The archive's own tag creation: one live tag per name, serialised with the inbox tag lock.</summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class BackendConfigurationTagsServiceTests : TestBaseSetup
{
    private const int UserId = 5;

    [SetUp]
    public Task ClearTags() => InboxTestData.ClearAsync(BackendConfigurationPnDbContext!);

    private static BackendConfigurationTagsService NewService(BackendConfigurationPnDbContext db)
    {
        var user = Substitute.For<IUserService>();
        user.UserId.Returns(UserId);
        return new BackendConfigurationTagsService(new BackendConfigurationLocalizationService(),
            NullLogger<BackendConfigurationTagsService>.Instance, db, user);
    }

    private Task<List<FileTag>> RowsAsync(string name) =>
        BackendConfigurationPnDbContext!.FileTags.AsNoTracking().Where(t => t.Name == name).OrderBy(t => t.Id).ToListAsync();

    private static bool Live(FileTag t) => t.WorkflowState != Constants.WorkflowStates.Removed;

    [Test]
    public async Task CreateTag_NewName_CreatesOneTag_ByTheCaller()
    {
        var res = await NewService(BackendConfigurationPnDbContext!).CreateTag(new CommonTagModel { Name = "Skadedyr" });

        Assert.That(res.Success, Is.True, res.Message);
        var row = (await RowsAsync("Skadedyr")).Single();
        Assert.That(Live(row), Is.True);
        Assert.That(row.CreatedByUserId, Is.EqualTo(UserId));
    }

    [Test]
    public async Task CreateTag_LiveTagExists_CreatesNothing()
    {
        var existing = await InboxTestData.TagAsync(BackendConfigurationPnDbContext!, "Skadedyr");

        var res = await NewService(BackendConfigurationPnDbContext!).CreateTag(new CommonTagModel { Name = "Skadedyr" });

        Assert.That(res.Success, Is.True, res.Message);
        Assert.That((await RowsAsync("Skadedyr")).Select(t => t.Id), Is.EqualTo(new[] { existing.Id }));
    }

    [Test]
    public async Task CreateTag_OnlyRemovedTag_RestoresIt()
    {
        var removed = await InboxTestData.TagAsync(BackendConfigurationPnDbContext!, "Skadedyr");
        await removed.Delete(BackendConfigurationPnDbContext!);

        var res = await NewService(BackendConfigurationPnDbContext!).CreateTag(new CommonTagModel { Name = "Skadedyr" });

        Assert.That(res.Success, Is.True, res.Message);
        var row = (await RowsAsync("Skadedyr")).Single();
        Assert.That(row.Id, Is.EqualTo(removed.Id));
        Assert.That(Live(row), Is.True);
        Assert.That(row.UpdatedByUserId, Is.EqualTo(UserId));
    }

    [Test]
    public async Task CreateTag_LiveAndRemovedDuplicate_KeepsOneLiveTag()
    {
        // The removed row has the lower id, so a first-by-name lookup would find it and restore it beside the live one.
        var removed = await InboxTestData.TagAsync(BackendConfigurationPnDbContext!, "Skadedyr");
        await removed.Delete(BackendConfigurationPnDbContext!);
        var live = await InboxTestData.TagAsync(BackendConfigurationPnDbContext!, "Skadedyr");

        var res = await NewService(BackendConfigurationPnDbContext!).CreateTag(new CommonTagModel { Name = "Skadedyr" });

        Assert.That(res.Success, Is.True, res.Message);
        var rows = await RowsAsync("Skadedyr");
        Assert.That(rows.Where(Live).Select(t => t.Id), Is.EqualTo(new[] { live.Id }));
        Assert.That(Live(rows.Single(t => t.Id == removed.Id)), Is.False);
    }

    [Test]
    public async Task CreateTag_TwoConcurrentRequests_SameName_OneTag()
    {
        await using var db1 = CreateFreshBackendConfigurationDbContext();
        await using var db2 = CreateFreshBackendConfigurationDbContext();

        var results = await Task.WhenAll(
            NewService(db1).CreateTag(new CommonTagModel { Name = "Skadedyr" }),
            NewService(db2).CreateTag(new CommonTagModel { Name = "Skadedyr" }));

        Assert.That(results.All(r => r.Success), Is.True, string.Join(" | ", results.Select(r => r.Message)));
        Assert.That(await RowsAsync("Skadedyr"), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task BulkFileTags_CreatesMissing_SkipsLive()
    {
        var existing = await InboxTestData.TagAsync(BackendConfigurationPnDbContext!, "Ventilation");

        var res = await NewService(BackendConfigurationPnDbContext!).BulkFileTags(
            new BackendConfigurationFileBulkTags { TagNames = ["Ventilation", "Skadedyr"] });

        Assert.That(res.Success, Is.True, res.Message);
        Assert.That((await RowsAsync("Ventilation")).Select(t => t.Id), Is.EqualTo(new[] { existing.Id }));
        Assert.That((await RowsAsync("Skadedyr")).Single(Live).CreatedByUserId, Is.EqualTo(UserId));
    }

    [Test]
    public async Task BulkFileTags_OnlyRemovedTag_RestoresIt()
    {
        var removed = await InboxTestData.TagAsync(BackendConfigurationPnDbContext!, "Skadedyr");
        await removed.Delete(BackendConfigurationPnDbContext!);

        var res = await NewService(BackendConfigurationPnDbContext!).BulkFileTags(
            new BackendConfigurationFileBulkTags { TagNames = ["Skadedyr"] });

        Assert.That(res.Success, Is.True, res.Message);
        var row = (await RowsAsync("Skadedyr")).Single();
        Assert.That(row.Id, Is.EqualTo(removed.Id));
        Assert.That(Live(row), Is.True);
    }

    [Test]
    public async Task BulkFileTags_ConcurrentWithCreateTag_SameName_OneTag()
    {
        await using var db1 = CreateFreshBackendConfigurationDbContext();
        await using var db2 = CreateFreshBackendConfigurationDbContext();

        var results = await Task.WhenAll(
            NewService(db1).BulkFileTags(new BackendConfigurationFileBulkTags { TagNames = ["Skadedyr"] }),
            NewService(db2).CreateTag(new CommonTagModel { Name = "Skadedyr" }));

        Assert.That(results.All(r => r.Success), Is.True, string.Join(" | ", results.Select(r => r.Message)));
        Assert.That((await RowsAsync("Skadedyr")).Count(Live), Is.EqualTo(1));
    }

    [Test]
    public async Task CreateTag_LockHeldElsewhere_FailsWithTryAgain_CreatesNothing()
    {
        // The holder keeps the lock until its connection is disposed (the 5 in GET_LOCK is only the holder's own
        // acquire timeout), so CreateTag waits out InboxNamedLock's 15 s and gives up.
        await using var holder = CreateFreshBackendConfigurationDbContext();
        await holder.Database.OpenConnectionAsync();
        await using (var cmd = holder.Database.GetDbConnection().CreateCommand())
        {
            cmd.CommandText = $"SELECT GET_LOCK(CONCAT('{InboxNamedLock.TagCreate}', ':', DATABASE()), 5)";
            Assert.That(Convert.ToInt64(await cmd.ExecuteScalarAsync()), Is.EqualTo(1));
        }

        var res = await NewService(BackendConfigurationPnDbContext!).CreateTag(new CommonTagModel { Name = "Skadedyr" });

        Assert.That(res.Success, Is.False);
        Assert.That(await RowsAsync("Skadedyr"), Is.Empty);
    }
}
