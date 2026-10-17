#nullable enable
using System.Text;
using BackendConfiguration.Pn.Infrastructure.Models.ArchiveFolders;
using BackendConfiguration.Pn.Infrastructure.Models.Files;
using BackendConfiguration.Pn.Services.ArchiveAccess;
using BackendConfiguration.Pn.Services.ArchiveFolders;
using BackendConfiguration.Pn.Services.BackendConfigurationFileTagsService;
using BackendConfiguration.Pn.Services.BackendConfigurationFilesService;
using BackendConfiguration.Pn.Services.FileArchive;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using NSubstitute;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// The folder-locking protocol (ArchiveFolderLock) under real concurrency: a second context holds a folder lock the
/// way a concurrent request would, the request under test starts and is seen waiting on that lock, then the holder
/// changes the folder and commits. The request must decide on the committed state, not on what it read before its
/// transaction. Deterministic: the change is made only once the request's own connection is observed in LOCK WAIT
/// (INNODB_TRX), see <see cref="WaitUntilBlockedAsync"/>.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class ArchiveConcurrencyTests : TestBaseSetup
{
    private const int G1 = 901;
    private const int G2 = 902;
    private static readonly TimeSpan BlockedTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// InnoDB serves INNODB_TRX from a buffer it refreshes only when the previous read of it ended more than 0.1 s ago
    /// (trx0i_s.cc, CACHE_MIN_IDLE_TIME_NS; trx_i_s_cache_end_read stamps last_read on every read). Polling faster
    /// than that keeps re-reading the snapshot taken before the request started waiting, forever. The fixture owns its
    /// container, so the observer is the only reader and every poll this far apart sees live state.
    /// </summary>
    private static readonly TimeSpan InnodbTrxPollInterval = TimeSpan.FromMilliseconds(250);
    private FixedArchiveAccess _access = null!;
    private BackendConfigurationFilesService _files = null!;
    private ArchiveFoldersService _folders = null!;
    private IArchiveStorage _storage = null!;

    [SetUp]
    public async Task SetUpServices()
    {
        var db = BackendConfigurationPnDbContext!;
        await ArchiveTestData.ClearAsync(db);
        await ArchiveTestData.DefaultFolderIdAsync(db);
        _access = new FixedArchiveAccess(ArchiveTestData.Admin, db, G1, G2);
        _storage = Substitute.For<IArchiveStorage>();
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(4);
        _files = new BackendConfigurationFilesService(NullLogger<BackendConfigurationTagsService>.Instance,
            new BackendConfigurationLocalizationService(), db, userService, new FileArchiver(db, _storage),
            BaseDbContext, _access);
        _folders = new ArchiveFoldersService(db, _access, new BackendConfigurationLocalizationService(), userService);
    }

    /// <summary>
    /// On a second context: opens a transaction holding an exclusive lock on <paramref name="lockedFolderIds"/>,
    /// starts <paramref name="request"/> (on the fixture's context), waits until it is blocked on a lock, applies
    /// <paramref name="change"/> and commits. Returns the request's outcome.
    /// </summary>
    private async Task<T> RaceAsync<T>(int[] lockedFolderIds, Func<Task<T>> request,
        Func<BackendConfigurationPnDbContext, Task> change)
    {
        await using var holder = CreateFreshBackendConfigurationDbContext();
        await using var observer = CreateFreshBackendConfigurationDbContext();
        // The request runs on the fixture's context. Opened here, EF keeps this one connection for the context until
        // TearDown disposes it, so its id is the server thread the request's transaction will wait on.
        var requestDb = BackendConfigurationPnDbContext!;
        await requestDb.Database.OpenConnectionAsync();
        var requestConnectionId = await requestDb.Database
            .SqlQueryRaw<long>("SELECT CAST(CONNECTION_ID() AS SIGNED) AS Value")
            .SingleAsync();
        Task<T>? running = null;
        try
        {
            await ArchiveTransaction.RunAsync(holder, async () =>
            {
                await ArchiveFolderLock.ExclusiveAsync(holder, lockedFolderIds.Select(id => (int?)id));
                // Started once: a retried holder attempt must not start a second request on the fixture context.
                running ??= Task.Run(request);
                await WaitUntilBlockedAsync(observer, running, requestConnectionId);
                await change(holder);
            });
        }
        finally
        {
            // Even when the holder failed (its rollback releases the lock): never leave the request running on the
            // fixture context into TearDown.
            if (running != null)
            {
                await Task.WhenAny(running, Task.Delay(BlockedTimeout));
            }
        }

        return await running!;
    }

    private static async Task MoveUnderAsync(BackendConfigurationPnDbContext db, int folderId, int parentId)
    {
        var live = await db.ArchiveFolders.SingleAsync(f => f.Id == folderId);
        live.ParentId = parentId;
        live.UpdatedByUserId = 1;
        await live.Update(db);
    }

    /// <summary>
    /// Returns once the request's connection (<paramref name="requestConnectionId"/>) is in an InnoDB lock wait on the
    /// folder-lock statement itself (ArchiveFolderLock's <c>SELECT Id FROM `ArchiveFolders` WHERE Id IN (...)</c>).
    /// Only that thread and that statement count: the holder's own transaction cannot satisfy the wait, and neither
    /// can a later statement that happens to queue behind the holder's lock. An upload's INSERT, for one, waits on the
    /// folder row through the Files.FolderId foreign key even without the shared lock. If it never blocks there, the
    /// failure says what that thread was doing instead.
    /// </summary>
    private static async Task WaitUntilBlockedAsync(BackendConfigurationPnDbContext observer, Task request,
        long requestConnectionId)
    {
        // ArchiveFolderLock's statement; MySqlConnector inlines the ids, so trx_query holds the literal text.
        var folderTable = observer.Model.FindEntityType(typeof(ArchiveFolder))!.GetTableName();
        var folderLockQuery = $"SELECT Id FROM `{folderTable}` WHERE Id IN (%";
        var deadline = DateTime.UtcNow + BlockedTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (request.IsCompleted)
            {
                Assert.Fail("the request finished without waiting for the folder lock");
            }

            var waiting = await observer.Database
                .SqlQueryRaw<long>(
                    "SELECT COUNT(*) AS Value FROM information_schema.INNODB_TRX " +
                    "WHERE trx_mysql_thread_id = {0} AND trx_state = 'LOCK WAIT' AND trx_query LIKE {1}",
                    requestConnectionId, folderLockQuery)
                .SingleAsync();
            if (waiting > 0)
            {
                return;
            }

            await Task.Delay(InnodbTrxPollInterval);
        }

        var thread = await observer.Database
            .SqlQueryRaw<string>(
                "SELECT CONCAT_WS(' | ', CONCAT('command=', p.COMMAND), CONCAT('state=', p.STATE), " +
                "CONCAT('trx=', IFNULL(t.trx_state, 'none')), CONCAT('trx_query=', IFNULL(t.trx_query, 'none')), " +
                "CONCAT('sql=', IFNULL(p.INFO, 'none'))) AS Value " +
                "FROM information_schema.PROCESSLIST p " +
                "LEFT JOIN information_schema.INNODB_TRX t ON t.trx_mysql_thread_id = p.ID WHERE p.ID = {0}",
                requestConnectionId)
            .SingleOrDefaultAsync();
        Assert.Fail($"the request (connection {requestConnectionId}) was not blocked on the folder-lock statement within " +
                    $"{BlockedTimeout.TotalSeconds}s; request task {request.Status}; its connection: " +
                    $"{thread ?? "gone from PROCESSLIST"}");
    }

    [Test]
    public async Task Upload_WhileTheFolderIsDeleted_IsRefusedAndNoDocumentLiesInTheRemovedFolder()
    {
        var folder = await ArchiveTestData.FolderAsync(BackendConfigurationPnDbContext!, "Midlertidig");

        // The holder plays DeleteAsync: exclusive lock, folder found empty, soft-deleted, committed.
        var result = await RaceAsync([folder.Id], () => _files.Create([ArchiveTestData.Upload("Kvittering.pdf", folder.Id)]),
            async holder =>
            {
                var live = await holder.ArchiveFolders.SingleAsync(f => f.Id == folder.Id);
                live.UpdatedByUserId = 1;
                await live.Delete(holder);
            });

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo("ArchiveFolderNotFound"));
        Assert.That(await BackendConfigurationPnDbContext!.Files.AsNoTracking()
            .AnyAsync(f => f.FolderId == folder.Id && f.WorkflowState != Constants.WorkflowStates.Removed), Is.False);
        Assert.That(await BackendConfigurationPnDbContext!.Files.AsNoTracking().AnyAsync(f => f.FileName == "Kvittering"),
            Is.False, "the refused upload leaves no row at all");
    }

    [Test]
    public async Task FolderAccess_TwoConcurrentChanges_ApplyOneAfterTheOtherInsteadOfAddingUp()
    {
        var folder = await ArchiveTestData.FolderAsync(BackendConfigurationPnDbContext!, "Personale");

        // The holder plays the first admin, giving the folder G1; the request is the second admin, giving it G2.
        var result = await RaceAsync([folder.Id],
            () => _folders.UpdateAccessAsync(folder.Id, new ArchiveFolderAccessModel { TagIds = [G2] }),
            holder => ArchiveReadTagSync.SyncFolderAsync(holder, folder.Id, [G1], 1));

        Assert.That(result.Success, Is.True, result.Message);
        var live = await BackendConfigurationPnDbContext!.ArchiveFolderReadTags.AsNoTracking()
            .Where(t => t.ArchiveFolderId == folder.Id && t.WorkflowState != Constants.WorkflowStates.Removed)
            .Select(t => t.SdkTagId)
            .ToListAsync();
        Assert.That(live, Is.EqualTo(new[] { G2 }), "the later change replaces the earlier one, not a union");
    }

    [Test]
    public async Task FolderMove_ConcurrentOppositeMove_IsRefusedAndNoCycleIsSaved()
    {
        var db = BackendConfigurationPnDbContext!;
        var a = await ArchiveTestData.FolderAsync(db, "Mappe A");
        var b = await ArchiveTestData.FolderAsync(db, "Mappe B");

        // The holder moves A under B; the request moves B under A.
        var result = await RaceAsync([a.Id, b.Id],
            () => _folders.UpdateAsync(b.Id, new ArchiveFolderUpdateModel { ParentId = a.Id }),
            holder => MoveUnderAsync(holder, a.Id, b.Id));

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo("ArchiveMaxTwoLevels"));
        var parents = await db.ArchiveFolders.AsNoTracking()
            .Where(f => f.Id == a.Id || f.Id == b.Id)
            .ToDictionaryAsync(f => f.Id, f => f.ParentId);
        Assert.That(parents[a.Id], Is.EqualTo(b.Id));
        Assert.That(parents[b.Id], Is.Null);
    }

    [Test]
    public async Task FolderCreate_UnderAParentMovedMeanwhile_IsRefusedAsAThirdLevel()
    {
        var db = BackendConfigurationPnDbContext!;
        var top = await ArchiveTestData.FolderAsync(db, "Drift");
        var parent = await ArchiveTestData.FolderAsync(db, "Leverandører");

        // The holder moves the parent under another folder; the request creates a folder under the parent.
        var result = await RaceAsync([parent.Id],
            () => _folders.CreateAsync(new ArchiveFolderCreateModel { Name = "Fakturaer", ParentId = parent.Id }),
            holder => MoveUnderAsync(holder, parent.Id, top.Id));

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo("ArchiveMaxTwoLevels"));
        Assert.That(await db.ArchiveFolders.AnyAsync(f => f.Name == "Fakturaer"), Is.False);
    }

    [Test]
    public async Task DocumentMove_TargetRestrictedMeanwhile_IsNotFoundAndNothingMoves()
    {
        var db = BackendConfigurationPnDbContext!;
        var source = await ArchiveTestData.FolderAsync(db, "Drift");
        var target = await ArchiveTestData.FolderAsync(db, "Kontrakter");
        var document = await ArchiveTestData.FileAsync(db, source.Id, "Lejekontrakt");
        _access.Context = ArchiveTestData.User();

        // The holder restricts the target to a group the caller is not in.
        await Assert.ThrowsAsync<ArchiveNotFoundException>(async () => await RaceAsync([target.Id],
            () => _files.Move(new FilesMoveModel { FileIds = [document.Id], FolderId = target.Id }),
            holder => ArchiveReadTagSync.SyncFolderAsync(holder, target.Id, [G1], 1)));
        var stored = await db.Files.AsNoTracking().SingleAsync(f => f.Id == document.Id);
        Assert.That(stored.FolderId, Is.EqualTo(source.Id));
    }

    [Test]
    public async Task DocumentMove_SourceRestrictedMeanwhile_NonAdminGetsTheD17RefusalAndNothingMoves()
    {
        var db = BackendConfigurationPnDbContext!;
        var source = await ArchiveTestData.FolderAsync(db, "Drift");
        var target = await ArchiveTestData.FolderAsync(db, "Kontrakter");
        var document = await ArchiveTestData.FileAsync(db, source.Id, "Lejekontrakt");
        // Both folders open when the request checks before its transaction: a move with no access change.
        _access.Context = ArchiveTestData.User(G1);

        // The holder restricts the source to G1, so the move would now open the document to everyone (D17).
        var result = await RaceAsync([source.Id],
            () => _files.Move(new FilesMoveModel { FileIds = [document.Id], FolderId = target.Id }),
            holder => ArchiveReadTagSync.SyncFolderAsync(holder, source.Id, [G1], 1));

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo("ArchiveMoveChangesAccess"));
        var stored = await db.Files.AsNoTracking().SingleAsync(f => f.Id == document.Id);
        Assert.That(stored.FolderId, Is.EqualTo(source.Id));
    }

    [Test]
    public async Task FileArchiver_FolderNotAllowedOnTheLiveTree_ThrowsAndWritesNoRow()
    {
        var db = BackendConfigurationPnDbContext!;
        var folder = await ArchiveTestData.FolderAsync(db, "Drift");
        var archiver = new FileArchiver(db, _storage);
        var uploadedBefore = await db.UploadedDatas.CountAsync();

        await Assert.ThrowsAsync<ArchiveFolderUnavailableException>(async () =>
            await archiver.ArchiveAsync(new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.7 x")), "Afvist", "pdf", [], [],
                folder.Id, null, DocumentDateState.None, 4, folderAllowed: _ => false));

        Assert.That(await db.Files.AsNoTracking().AnyAsync(f => f.FileName == "Afvist"), Is.False);
        Assert.That(await db.UploadedDatas.CountAsync(), Is.EqualTo(uploadedBefore));
    }
}
