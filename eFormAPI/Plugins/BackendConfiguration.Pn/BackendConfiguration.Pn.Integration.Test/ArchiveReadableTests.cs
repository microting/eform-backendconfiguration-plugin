#nullable enable
using BackendConfiguration.Pn.Services.ArchiveAccess;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// The read rule of spec section 3, in SQL (Readable) and in memory (snapshot + rows), on one tree:
/// Arkiv (default, open) · Drift (open) › Fakturaer (G1), Serviceaftaler (inherits open)
/// · Ledelse (G1) › Referater (inherits G1), Løn (G2, replaces G1).
/// Document "h" has a NULL FolderId (pre-10.0.59 rows): it lies in the default folder and is open.
/// Soft-deleted read tags on Drift and on "e" must not restrict anything; "i" (Serviceaftaler) reads through Drift.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class ArchiveReadableTests : TestBaseSetup
{
    private const int G1 = 601;
    private const int G2 = 602;
    private readonly Dictionary<string, int> _file = new();
    private readonly Dictionary<string, int> _folder = new();
    private FixedArchiveAccess _access = null!;

    private BackendConfigurationPnDbContext Db => BackendConfigurationPnDbContext!;

    /// <summary><paramref name="manage"/> = admin or Leder (CanManage); the rules never read IsAdmin.</summary>
    private static ArchiveAccessContext Ctx(int[] groups, bool manage, bool disabled) =>
        new(disabled, false, groups, manage && !disabled);

    [SetUp]
    public async Task SeedTree()
    {
        var db = Db;
        await ArchiveTestData.ClearAsync(db);
        _file.Clear();
        _folder.Clear();
        _folder["Arkiv"] = await ArchiveTestData.DefaultFolderIdAsync(db);
        _folder["Drift"] = (await ArchiveTestData.FolderAsync(db, "Drift", null, G1)).Id;
        await (await db.ArchiveFolderReadTags.SingleAsync(t => t.ArchiveFolderId == _folder["Drift"])).Delete(db);
        _folder["Fakturaer"] = (await ArchiveTestData.FolderAsync(db, "Fakturaer", _folder["Drift"], G1)).Id;
        _folder["Serviceaftaler"] = (await ArchiveTestData.FolderAsync(db, "Serviceaftaler", _folder["Drift"])).Id;
        _folder["Ledelse"] = (await ArchiveTestData.FolderAsync(db, "Ledelse", null, G1)).Id;
        _folder["Referater"] = (await ArchiveTestData.FolderAsync(db, "Referater", _folder["Ledelse"])).Id;
        _folder["Løn"] = (await ArchiveTestData.FolderAsync(db, "Løn", _folder["Ledelse"], G2)).Id;

        _file["a"] = (await ArchiveTestData.FileAsync(db, _folder["Arkiv"], "a")).Id;
        _file["b"] = (await ArchiveTestData.FileAsync(db, _folder["Fakturaer"], "b")).Id;
        _file["c"] = (await ArchiveTestData.FileAsync(db, _folder["Referater"], "c")).Id;
        _file["d"] = (await ArchiveTestData.FileAsync(db, _folder["Løn"], "d")).Id;
        _file["e"] = (await ArchiveTestData.FileAsync(db, _folder["Referater"], "e", ownAccess: true, ownGroups: [G1])).Id;
        await (await db.FileReadTags.SingleAsync(t => t.FileId == _file["e"])).Delete(db);
        _file["f"] = (await ArchiveTestData.FileAsync(db, _folder["Arkiv"], "f", ownAccess: true, ownGroups: [G2])).Id;
        _file["g"] = (await ArchiveTestData.FileAsync(db, _folder["Ledelse"], "g")).Id;
        _file["h"] = (await ArchiveTestData.FileAsync(db, null, "h")).Id;
        _file["i"] = (await ArchiveTestData.FileAsync(db, _folder["Serviceaftaler"], "i")).Id;
        _access = new FixedArchiveAccess(ArchiveTestData.User(), db);
    }

    private async Task<List<string>> ReadableNames(ArchiveAccessContext ctx) =>
        (await _access.Readable(Db.Files
                .Where(f => f.WorkflowState != Constants.WorkflowStates.Removed), ctx)
            .Select(f => f.FileName).ToListAsync()).Order().ToList();

    private static IEnumerable<TestCaseData> Cases()
    {
        yield return new TestCaseData(Array.Empty<int>(), false, false, new[] { "a", "e", "h", "i" }).SetName("NoGroups_SeesOpenOnly");
        yield return new TestCaseData(new[] { G1 }, false, false, new[] { "a", "b", "c", "e", "g", "h", "i" }).SetName("G1_SeesSubfolderOwnAndInherited");
        yield return new TestCaseData(new[] { G2 }, false, false, new[] { "a", "d", "e", "f", "h", "i" }).SetName("G2_SubfolderReplacesParent_OwnAccessDoc");
        yield return new TestCaseData(Array.Empty<int>(), true, false, new[] { "a", "b", "c", "d", "e", "f", "g", "h", "i" }).SetName("Admin_SeesEverything");
        yield return new TestCaseData(new[] { G2 }, true, false, new[] { "a", "b", "c", "d", "e", "f", "g", "h", "i" }).SetName("Leder_NonAdminWhoManages_SeesEverything");
        yield return new TestCaseData(new[] { G1, G2 }, true, true, Array.Empty<string>()).SetName("DisabledAdmin_SeesNothing");
    }

    [TestCaseSource(nameof(Cases))]
    public async Task Readable_AppliesTheRule(int[] groups, bool manage, bool disabled, string[] expected)
    {
        var ctx = Ctx(groups, manage, disabled);
        Assert.That(await ReadableNames(ctx), Is.EqualTo(expected.Order().ToList()));
    }

    [TestCaseSource(nameof(Cases))]
    public async Task Readable_MatchesInMemoryRule_ForEveryContext(int[] groups, bool manage, bool disabled, string[] _)
    {
        var ctx = Ctx(groups, manage, disabled);
        var folders = await ArchiveFolderSnapshot.LoadAsync(Db);
        var rows = await ArchiveAccessRules.LoadFileRowsAsync(Db, _file.Values.ToList());
        var inMemory = rows.Where(r => ArchiveAccessRules.CanRead(ctx, r.Effective(folders)))
            .Select(r => _file.Single(kv => kv.Value == r.FileId).Key).Order().ToList();

        Assert.That(await ReadableNames(ctx), Is.EqualTo(inMemory));
    }

    [Test]
    public async Task Readable_RemovedFolderReadTag_NoLongerRestricts()
    {
        var tag = await Db.ArchiveFolderReadTags
            .SingleAsync(t => t.ArchiveFolderId == _folder["Fakturaer"]);
        await tag.Delete(Db);

        Assert.That(await ReadableNames(ArchiveTestData.User()), Does.Contain("b"));
    }

    [Test]
    public async Task VisibleFolders_NoGroups_ShowsParentsOfReadableContentOnly()
    {
        var folders = await ArchiveFolderSnapshot.LoadAsync(Db);

        var visible = await _access.VisibleFolderIdsAsync(ArchiveTestData.User(), folders);

        // Referater holds "e" (own access, open) → Referater and its restricted parent Ledelse are visible.
        Assert.That(visible, Is.EquivalentTo(new[]
        {
            _folder["Arkiv"], _folder["Drift"], _folder["Serviceaftaler"], _folder["Ledelse"], _folder["Referater"]
        }));
    }

    [Test]
    public async Task VisibleFolders_G2_ShowsRestrictedParentOfReadableSubfolder()
    {
        var folders = await ArchiveFolderSnapshot.LoadAsync(Db);

        var visible = await _access.VisibleFolderIdsAsync(ArchiveTestData.User(G2), folders);

        Assert.That(visible, Does.Contain(_folder["Løn"]));
        Assert.That(visible, Does.Contain(_folder["Ledelse"]));
        Assert.That(visible, Does.Not.Contain(_folder["Fakturaer"]));
    }

    [Test]
    public async Task VisibleFolders_AdminSeesAll_DisabledSeesNone()
    {
        var folders = await ArchiveFolderSnapshot.LoadAsync(Db);

        Assert.That(await _access.VisibleFolderIdsAsync(ArchiveTestData.Admin, folders), Has.Count.EqualTo(_folder.Count));
        Assert.That(await _access.VisibleFolderIdsAsync(ArchiveTestData.Disabled, folders), Is.Empty);
    }

    [Test]
    public async Task Snapshot_PathAndEffectiveGroups()
    {
        var folders = await ArchiveFolderSnapshot.LoadAsync(Db);

        Assert.That(folders.Path(_folder["Fakturaer"]), Is.EqualTo(new[] { "Drift", "Fakturaer" }));
        Assert.That(folders.EffectiveGroups(_folder["Referater"]), Is.EqualTo(new[] { G1 }));
        Assert.That(folders.EffectiveGroups(_folder["Løn"]), Is.EqualTo(new[] { G2 }));
        Assert.That(folders.EffectiveGroups(_folder["Serviceaftaler"]), Is.Empty);
    }

    [Test]
    public async Task CanReadFolderOrContents_MatchesVisibility()
    {
        Assert.That(await _access.CanReadFolderOrContentsAsync(_folder["Ledelse"], ArchiveTestData.User()), Is.True);
        Assert.That(await _access.CanReadFolderOrContentsAsync(_folder["Fakturaer"], ArchiveTestData.User()), Is.False);
    }

    [Test]
    public async Task GetEnabledContext_Disabled_Throws()
    {
        _access.Context = ArchiveTestData.Disabled;

        await Assert.ThrowsAsync<ArchiveAccountDisabledException>(() => _access.GetEnabledContextAsync());
    }
}
