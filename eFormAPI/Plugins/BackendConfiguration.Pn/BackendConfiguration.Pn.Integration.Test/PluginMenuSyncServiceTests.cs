namespace BackendConfiguration.Pn.Integration.Test;

using BackendConfiguration.Pn.Services.PluginMenuSyncService;
using Microsoft.EntityFrameworkCore;
using Microting.eFormApi.BasePn.Infrastructure.Consts;
using Microting.eFormApi.BasePn.Infrastructure.Models.Application.NavigationMenu;
using Microting.EformAngularFrontendBase.Infrastructure.Data.Entities;
using Microting.EformAngularFrontendBase.Infrastructure.Data.Entities.Menu;

/// <summary>
/// PluginMenuSyncService (Plan E ruling E-04): the host seeds a plugin's menu entries
/// only on install, so an existing tenant would never see the Kemi group. The sync runs
/// from SeedDatabase at every startup and adds a menu entry whose template the tenant
/// has never had. The template is the "offered once" marker: an admin who deletes Kemi
/// in the menu editor keeps its template, so the sync leaves it deleted.
///
/// <para>Each fixture owns its MariaDB container, so the Angular menu tables are this
/// fixture's alone; every test starts from one core menu entry and the plugin row.
/// Positions are asserted relative to the existing menu only.</para>
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class PluginMenuSyncServiceTests : TestBaseSetup
{
    private const string PluginId = "eform-backend-configuration-plugin";
    private const string GroupE2EId = "backend-configuration-pn-chemicals";
    private const string InventoryE2EId = "backend-configuration-pn-chemicals-inventory";
    private const string LocationsE2EId = "backend-configuration-pn-chemicals-locations";
    private const string PermissionsE2EId = "backend-configuration-pn-chemicals-permissions";
    private const string SettingsE2EId = "backend-configuration-pn-chemicals-settings";
    private const string HistoryE2EId = "backend-configuration-pn-chemicals-history";
    private const string LaterChildE2EId = "backend-configuration-pn-chemicals-later-child";

    /// <summary>The five Kemi children in menu order.</summary>
    private static readonly string[] AllChildren = [InventoryE2EId, LocationsE2EId, PermissionsE2EId, SettingsE2EId, HistoryE2EId];

    // eFormAPI.Web.Hosting.Enums.PluginStatus lives in the host, which the tests cannot reference.
    private const int Disabled = 1;
    private const int Enabled = 2;

    private int _pluginRowId;

    [SetUp]
    public async Task ResetTheMenuTables()
    {
        await BaseDbContext.MenuItemSecurityGroups.ExecuteDeleteAsync();
        await BaseDbContext.MenuItemTranslations.ExecuteDeleteAsync();
        await BaseDbContext.MenuItems.Where(x => x.ParentId != null).ExecuteDeleteAsync();
        await BaseDbContext.MenuItems.ExecuteDeleteAsync();
        await BaseDbContext.MenuTemplateTranslations.ExecuteDeleteAsync();
        await BaseDbContext.MenuTemplatePermissions.ExecuteDeleteAsync();
        await BaseDbContext.MenuTemplates.ExecuteDeleteAsync();
        await BaseDbContext.EformPlugins.ExecuteDeleteAsync();

        // The core menu the host seeds before any plugin.
        BaseDbContext.MenuItems.Add(new MenuItem
        {
            Name = "Core entry", E2EId = "core-entry", Link = "/core", Type = MenuItemTypeEnum.Link, Position = 3
        });
        var plugin = new EformPlugin { PluginId = PluginId, ConnectionString = "test", Status = Enabled };
        BaseDbContext.EformPlugins.Add(plugin);
        await BaseDbContext.SaveChangesAsync();
        _pluginRowId = plugin.Id;
        BaseDbContext.ChangeTracker.Clear();
    }

    [Test]
    public async Task FreshInstall_TheSyncWritesNothing_AndTheHostLoadGivesKemiOnce()
    {
        var before = await SnapshotAsync();

        Assert.That(Run(Menu()), Is.EqualTo(0));
        Assert.That(await SnapshotAsync(), Is.EqualTo(before), "a fresh install belongs to the host's full loader");

        await InstallAsTheHostDoesAsync(Menu());
        Assert.That(Run(Menu()), Is.EqualTo(0), "the host loaded every template, so nothing is missing");

        Assert.That(await BaseDbContext.MenuItems.CountAsync(x => x.E2EId == GroupE2EId), Is.EqualTo(1));
        Assert.That(await BaseDbContext.MenuItems.CountAsync(x => x.E2EId == PermissionsE2EId), Is.EqualTo(1));
        Assert.That(await BaseDbContext.MenuItems.CountAsync(x => x.E2EId == SettingsE2EId), Is.EqualTo(1));
        Assert.That(await BaseDbContext.MenuItems.CountAsync(x => x.E2EId == InventoryE2EId), Is.EqualTo(1));
        Assert.That(await BaseDbContext.MenuItems.CountAsync(x => x.E2EId == LocationsE2EId), Is.EqualTo(1));
        Assert.That(await BaseDbContext.MenuItems.CountAsync(x => x.E2EId == HistoryE2EId), Is.EqualTo(1));
    }

    /// <summary>E3: a tenant with Rettigheder and Indstillinger (E2) gets Beholdning and Lokationer in front of them, and Historik after them, on its next startup.</summary>
    [Test]
    public async Task ExistingTenantWithKemiButWithoutInventory_GetsBeholdningFirstUnderTheGroup()
    {
        await InstallAsTheHostDoesAsync(WithoutChildren(Menu(), InventoryE2EId, LocationsE2EId, HistoryE2EId));
        Assert.That(await BaseDbContext.MenuItems.AnyAsync(x => x.E2EId == InventoryE2EId), Is.False, "arrange: an E2 tenant");

        Assert.That(Run(Menu()), Is.EqualTo(3), "only the three new children");

        await AssertLinkChildAsync(InventoryE2EId, "inventory", 0, "Beholdning", "Inventory", "Bestand", "Запаси");
        await AssertChildOrderAsync(AllChildren);
        Assert.That(Run(Menu()), Is.EqualTo(0), "and a second startup adds nothing");
    }

    /// <summary>A tenant that has Beholdning but not yet Lokationer gets Lokationer second, under the group.</summary>
    [Test]
    public async Task ExistingTenantWithInventoryButWithoutLocations_GetsLokationerSecondUnderTheGroup()
    {
        await InstallAsTheHostDoesAsync(WithoutChildren(Menu(), LocationsE2EId, HistoryE2EId));
        Assert.That(await BaseDbContext.MenuItems.AnyAsync(x => x.E2EId == LocationsE2EId), Is.False, "arrange: no Lokationer yet");

        Assert.That(Run(Menu()), Is.EqualTo(2), "only Lokationer and Historik, which that tenant has not seen either");

        await AssertLinkChildAsync(LocationsE2EId, "locations", 1, "Lokationer", "Locations", "Standorte", "Локації");
        await AssertChildOrderAsync(AllChildren);
        Assert.That(Run(Menu()), Is.EqualTo(0), "and a second startup adds nothing");
    }

    /// <summary>A tenant that has the four other Kemi screens but not Historik gets Historik last, under the group.</summary>
    [Test]
    public async Task ExistingTenantWithoutHistory_GetsHistorikLastUnderTheGroup()
    {
        await InstallAsTheHostDoesAsync(WithoutChildren(Menu(), HistoryE2EId));
        Assert.That(await BaseDbContext.MenuItems.AnyAsync(x => x.E2EId == HistoryE2EId), Is.False, "arrange: no Historik yet");

        Assert.That(Run(Menu()), Is.EqualTo(1), "only the new child");

        await AssertLinkChildAsync(HistoryE2EId, "history", 4, "Historik", "History", "Verlauf", "Історія");
        await AssertChildOrderAsync(AllChildren);
        Assert.That(Run(Menu()), Is.EqualTo(0), "and a second startup adds nothing");
    }

    /// <summary>
    /// The admin deleted Lokationer and saved the menu, so the editor renumbered the rest 0, 1, 2. Historik (4)
    /// still lands last, and the sync neither brings Lokationer back nor moves the admin's rows.
    /// </summary>
    [Test]
    public async Task ANewLastChild_StaysLast_WhenTheAdminRemovedAChildAndRenumberedTheGroup()
    {
        await InstallAsTheHostDoesAsync(WithoutChildren(Menu(), HistoryE2EId));
        var locations = await BaseDbContext.MenuItems.SingleAsync(x => x.E2EId == LocationsE2EId);
        await BaseDbContext.MenuItemTranslations.Where(x => x.MenuItemId == locations.Id).ExecuteDeleteAsync();
        await BaseDbContext.MenuItems.Where(x => x.Id == locations.Id).ExecuteDeleteAsync();
        BaseDbContext.ChangeTracker.Clear();
        await RenumberAsync((InventoryE2EId, 0), (PermissionsE2EId, 1), (SettingsE2EId, 2));

        Assert.That(Run(Menu()), Is.EqualTo(1), "Historik only: Lokationer's template is still there, so it stays deleted");

        await AssertChildOrderAsync(InventoryE2EId, PermissionsE2EId, SettingsE2EId, HistoryE2EId);
        Assert.That((await ChildrenOfTheGroupAsync()).Select(x => x.Position), Is.EqualTo(new[] { 0, 1, 2, 4 }),
            "the admin's rows keep their places");
    }

    /// <summary>
    /// m-13 for a child in the middle: the admin saved the menu, so Beholdning 0, Rettigheder 1, Indstillinger 2.
    /// Lokationer (1) must land between Beholdning and Rettigheder, not tie with Rettigheder.
    /// </summary>
    [Test]
    public async Task ANewMiddleChild_TakesItsPlace_WhenTheAdminRenumberedTheGroup()
    {
        await InstallAsTheHostDoesAsync(WithoutChildren(Menu(), LocationsE2EId, HistoryE2EId));
        await RenumberAsync((InventoryE2EId, 0), (PermissionsE2EId, 1), (SettingsE2EId, 2));

        Assert.That(Run(Menu()), Is.EqualTo(2), "Lokationer and Historik");

        await AssertChildOrderAsync(AllChildren);
    }

    [Test]
    public async Task ExistingTenantWithoutKemi_GetsTheGroupAndItsChildOnTheNextStartup()
    {
        await InstallAsTheHostDoesAsync(WithoutChemicals(Menu()));
        var lastRootPosition = await BaseDbContext.MenuItems.Where(x => x.ParentId == null).MaxAsync(x => x.Position);

        Assert.That(Run(Menu()), Is.EqualTo(6), "the group and its five children");

        var group = await BaseDbContext.MenuItems.AsNoTracking()
            .Include(x => x.Translations).SingleAsync(x => x.E2EId == GroupE2EId);
        Assert.That(group.Type, Is.EqualTo(MenuItemTypeEnum.Dropdown));
        Assert.That(group.Name, Is.EqualTo("Dropdown"));
        Assert.That(group.ParentId, Is.Null);
        Assert.That(group.MenuTemplateId, Is.Null);
        Assert.That(group.IsInternalLink, Is.True);
        Assert.That(group.Position, Is.GreaterThan(lastRootPosition), "appended after every existing entry");
        AssertNames(group.Translations.Select(t => (t.LocaleName, t.Name, t.Language)),
            "Kemi", "Chemicals", "Chemikalien", "Хімікати");

        await AssertLinkChildAsync(PermissionsE2EId, "permissions", 2, "Rettigheder", "Permissions", "Berechtigungen", "Дозволи");
        await AssertChildOrderAsync(AllChildren);
    }

    /// <summary>A tenant that already got Kemi with Rettigheder only (E1) gets Beholdning, Lokationer, Indstillinger and Historik on its next startup.</summary>
    [Test]
    public async Task ExistingTenantWithKemiButWithoutSettings_GetsTheNewChildUnderTheGroup()
    {
        await InstallAsTheHostDoesAsync(WithoutChildren(Menu(), InventoryE2EId, LocationsE2EId, SettingsE2EId, HistoryE2EId));
        Assert.That(await BaseDbContext.MenuItems.AnyAsync(x => x.E2EId == SettingsE2EId), Is.False, "arrange: an E1 tenant");

        Assert.That(Run(Menu()), Is.EqualTo(4), "only the four new children");

        await AssertLinkChildAsync(SettingsE2EId, "settings", 3, "Indstillinger", "Settings", "Einstellungen", "Налаштування");
        Assert.That(await BaseDbContext.MenuItems.CountAsync(x => x.E2EId == GroupE2EId), Is.EqualTo(1));
        await AssertChildOrderAsync(AllChildren);
        Assert.That(Run(Menu()), Is.EqualTo(0), "and a second startup adds nothing");
    }

    /// <summary>
    /// m-13: the menu editor renumbers a group's children from 0, so on an E2 tenant whose admin saved the
    /// menu Rettigheder holds 0 and Indstillinger 1. Beholdning (0) must still come first, not tie with
    /// Rettigheder and sort after it by id.
    /// </summary>
    [Test]
    public async Task ANewFirstChild_StaysFirst_WhenTheAdminRenumberedTheGroup()
    {
        await InstallAsTheHostDoesAsync(WithoutChildren(Menu(), InventoryE2EId, LocationsE2EId, HistoryE2EId));
        await RenumberAsync((PermissionsE2EId, 0), (SettingsE2EId, 1));

        Assert.That(Run(Menu()), Is.EqualTo(3));

        await AssertChildOrderAsync(AllChildren);
    }

    /// <summary>
    /// The sync keys on the template E2EId, never on the link: a tenant whose Beholdning row carries an older
    /// link (as an unreleased build of this branch wrote it) gets no second Beholdning, and its row is kept.
    /// </summary>
    [Test]
    public async Task AnExistingChildWithAnOlderLink_IsNotAddedAgain()
    {
        await InstallAsTheHostDoesAsync(Menu());
        await BaseDbContext.MenuItems.Where(x => x.E2EId == InventoryE2EId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Link, "/plugins/backend-configuration-pn/chemicals"));
        await BaseDbContext.MenuTemplates.Where(x => x.E2EId == InventoryE2EId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.DefaultLink, "/plugins/backend-configuration-pn/chemicals"));

        Assert.That(Run(Menu()), Is.EqualTo(0));

        var row = await BaseDbContext.MenuItems.AsNoTracking().SingleAsync(x => x.E2EId == InventoryE2EId);
        Assert.That(row.Link, Is.EqualTo("/plugins/backend-configuration-pn/chemicals"), "the sync never rewrites a kept row");
        Assert.That(await BaseDbContext.MenuTemplates.CountAsync(x => x.E2EId == InventoryE2EId), Is.EqualTo(1));
    }

    [Test]
    public async Task SecondStartup_AddsNothing()
    {
        await InstallAsTheHostDoesAsync(WithoutChemicals(Menu()));
        Assert.That(Run(Menu()), Is.GreaterThan(0));
        var afterFirst = await SnapshotAsync();

        Assert.That(Run(Menu()), Is.EqualTo(0));

        Assert.That(await SnapshotAsync(), Is.EqualTo(afterFirst));
        Assert.That(await BaseDbContext.MenuItems.CountAsync(x => x.E2EId == GroupE2EId), Is.EqualTo(1));
    }

    [Test]
    public async Task KemiDeletedInTheMenuEditor_IsNotAddedBack()
    {
        await InstallAsTheHostDoesAsync(WithoutChemicals(Menu()));
        Run(Menu());
        await DeleteTheKemiMenuItemsAsync();
        var afterDelete = await SnapshotAsync();

        Assert.That(Run(Menu()), Is.EqualTo(0));

        Assert.That(await SnapshotAsync(), Is.EqualTo(afterDelete));
        Assert.That(await BaseDbContext.MenuItems.AnyAsync(x => x.E2EId == GroupE2EId), Is.False);
    }

    [Test]
    public async Task KemiDeletedInTheMenuEditor_IsNotRecreatedWhenALaterReleaseAddsAChild()
    {
        await InstallAsTheHostDoesAsync(WithoutChemicals(Menu()));
        Run(Menu());
        await DeleteTheKemiMenuItemsAsync();

        Assert.That(Run(MenuWithLaterChild()), Is.EqualTo(0),
            "the admin removed the whole group; a new child must not bring it back");

        Assert.That(await BaseDbContext.MenuItems.AnyAsync(x => x.E2EId == GroupE2EId), Is.False);
        Assert.That(await BaseDbContext.MenuItems.AnyAsync(x => x.E2EId == LaterChildE2EId), Is.False);
    }

    [Test]
    public async Task ALaterReleaseChild_IsAppendedUnderTheExistingGroup()
    {
        await InstallAsTheHostDoesAsync(WithoutChemicals(Menu()));
        Run(Menu());

        Assert.That(Run(MenuWithLaterChild()), Is.EqualTo(1));

        await AssertChildOrderAsync([.. AllChildren, LaterChildE2EId]);
        Assert.That(await BaseDbContext.MenuTemplates.CountAsync(x => x.E2EId == LaterChildE2EId), Is.EqualTo(1));
    }

    [Test]
    public async Task DisabledPlugin_WritesNothing()
    {
        await InstallAsTheHostDoesAsync(WithoutChemicals(Menu()));
        await BaseDbContext.EformPlugins.ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, Disabled));
        var before = await SnapshotAsync();

        Assert.That(Run(Menu()), Is.EqualTo(0));

        Assert.That(await SnapshotAsync(), Is.EqualTo(before));

        // Positive control: the same tenant, enabled, does get Kemi. Without it this test
        // would also pass against a sync that ignores the plugin status or does nothing.
        await BaseDbContext.EformPlugins.ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, Enabled));
        Assert.That(Run(Menu()), Is.EqualTo(6), "the group and its five children");
    }

    /// <summary>
    /// The host's menu editor copies a group's E2EId onto each of its children
    /// (eFormAPI.Web MenuService, <c>E2EId = x.E2EId</c>), so after an admin saves the
    /// menu the Rettigheder link carries the group's id too. A later child must still
    /// land under the Dropdown row, not under that link.
    /// </summary>
    [Test]
    public async Task ALaterReleaseChild_LandsUnderTheGroup_WhenAChildCarriesTheGroupE2EId()
    {
        await InstallAsTheHostDoesAsync(WithoutChemicals(Menu()));
        Run(Menu());
        await BaseDbContext.MenuItems.Where(x => x.E2EId == PermissionsE2EId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.E2EId, GroupE2EId));

        Assert.That(Run(MenuWithLaterChild()), Is.EqualTo(1));

        var group = await BaseDbContext.MenuItems.AsNoTracking()
            .SingleAsync(x => x.E2EId == GroupE2EId && x.Type == MenuItemTypeEnum.Dropdown);
        var later = await BaseDbContext.MenuItems.AsNoTracking().SingleAsync(x => x.E2EId == LaterChildE2EId);
        Assert.That(later.ParentId, Is.EqualTo(group.Id));
    }

    /// <summary>The sync is generic: a missing root link gets its template permissions too.</summary>
    [Test]
    public async Task AMissingRootLink_GetsItsTemplateAndItsPermissions()
    {
        await InstallAsTheHostDoesAsync(Menu());
        var lastRootPosition = await BaseDbContext.MenuItems.Where(x => x.ParentId == null).MaxAsync(x => x.Position);
        var menu = Menu();
        menu.Add(Link("backend-configuration-pn-sync-test-link", 16, new PluginMenuTemplatePermissionModel
        {
            ClaimName = "sync_test_claim", PermissionName = "Sync test permission", PermissionTypeName = "Sync test type"
        }));

        Assert.That(Run(menu), Is.EqualTo(1));

        var item = await BaseDbContext.MenuItems.AsNoTracking()
            .SingleAsync(x => x.E2EId == "backend-configuration-pn-sync-test-link");
        Assert.That(item.ParentId, Is.Null);
        Assert.That(item.Position, Is.GreaterThan(lastRootPosition));
        var claims = await BaseDbContext.MenuTemplatePermissions.AsNoTracking()
            .Where(x => x.MenuTemplate.E2EId == "backend-configuration-pn-sync-test-link")
            .Select(x => new { x.Permission.ClaimName, x.Permission.PermissionName, Type = x.Permission.PermissionType.Name })
            .ToListAsync();
        Assert.That(claims, Has.Count.EqualTo(1));
        Assert.That(claims[0].ClaimName, Is.EqualTo("sync_test_claim"));
        Assert.That(claims[0].PermissionName, Is.EqualTo("Sync test permission"));
        Assert.That(claims[0].Type, Is.EqualTo("Sync test type"));
    }

    /// <summary>
    /// Another process (or an earlier attempt of a retried execution) writes the same entry
    /// between the "is it missing" check and the transaction: the work must not add it again.
    /// </summary>
    [Test]
    public async Task ARowWrittenAfterTheMissingCheck_IsNotDuplicated_ForAMissingRootLink()
    {
        await InstallAsTheHostDoesAsync(Menu());
        var menu = Menu();
        menu.Add(Link("backend-configuration-pn-sync-test-link", 16));
        var service = new PluginMenuSyncService(BaseDbContext)
        {
            BeforeTransaction = () => new PluginMenuSyncService(BaseDbContext).Run(PluginId, menu)
        };

        service.Run(PluginId, menu);

        Assert.That(await BaseDbContext.MenuItems.CountAsync(x => x.E2EId == "backend-configuration-pn-sync-test-link"), Is.EqualTo(1));
        Assert.That(await BaseDbContext.MenuTemplates.CountAsync(x => x.E2EId == "backend-configuration-pn-sync-test-link"), Is.EqualTo(1));
    }

    [Test]
    public async Task ARowWrittenAfterTheMissingCheck_IsNotDuplicated_ForAGroupAndItsChild()
    {
        await InstallAsTheHostDoesAsync(WithoutChemicals(Menu()));
        var service = new PluginMenuSyncService(BaseDbContext)
        {
            BeforeTransaction = () => new PluginMenuSyncService(BaseDbContext).Run(PluginId, Menu())
        };

        service.Run(PluginId, Menu());

        Assert.That(await BaseDbContext.MenuItems.CountAsync(x => x.E2EId == GroupE2EId), Is.EqualTo(1));
        Assert.That(await BaseDbContext.MenuItems.CountAsync(x => x.E2EId == PermissionsE2EId), Is.EqualTo(1));
        Assert.That(await BaseDbContext.MenuTemplates.CountAsync(x => x.E2EId == PermissionsE2EId), Is.EqualTo(1));
    }

    /// <summary>The 855 check: Kemi was the only top-level entry without an icon; the sync gives it its glyph.</summary>
    [Test]
    public async Task ApplyDefaultIcons_GivesTheKemiGroupItsIcon_WhenItHasNone()
    {
        await InstallAsTheHostDoesAsync(Menu());

        Assert.That(ApplyIcons(), Is.EqualTo(1));

        Assert.That(await IconOfTheGroupAsync(), Is.EqualTo("science"));
        Assert.That(ApplyIcons(), Is.EqualTo(0), "a second startup changes nothing");
    }

    /// <summary>A glyph an admin chose in the menu editor stays.</summary>
    [Test]
    public async Task ApplyDefaultIcons_KeepsAnIconTheAdminChose()
    {
        await InstallAsTheHostDoesAsync(Menu());
        await BaseDbContext.MenuItems.Where(x => x.E2EId == GroupE2EId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IconName, "inventory_2"));

        Assert.That(ApplyIcons(), Is.EqualTo(0));

        Assert.That(await IconOfTheGroupAsync(), Is.EqualTo("inventory_2"));
    }

    /// <summary>The menu editor copies the group's E2EId onto its children; only the group gets the icon.</summary>
    [Test]
    public async Task ApplyDefaultIcons_LeavesAChildThatCarriesTheGroupE2EIdAlone()
    {
        await InstallAsTheHostDoesAsync(Menu());
        await BaseDbContext.MenuItems.Where(x => x.E2EId == PermissionsE2EId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.E2EId, GroupE2EId));

        Assert.That(ApplyIcons(), Is.EqualTo(1));

        var icons = await BaseDbContext.MenuItems.AsNoTracking().Where(x => x.E2EId == GroupE2EId)
            .Select(x => new { x.Type, x.IconName }).ToListAsync();
        Assert.That(icons.Single(x => x.Type == MenuItemTypeEnum.Dropdown).IconName, Is.EqualTo("science"));
        Assert.That(icons.Single(x => x.Type == MenuItemTypeEnum.Link).IconName, Is.Null);
    }

    [Test]
    public async Task ApplyDefaultIcons_DoesNothingWhileThePluginIsDisabled()
    {
        await InstallAsTheHostDoesAsync(Menu());
        await BaseDbContext.EformPlugins.ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, Disabled));

        Assert.That(ApplyIcons(), Is.EqualTo(0));

        Assert.That(await IconOfTheGroupAsync(), Is.Null);
    }

    private int ApplyIcons() =>
        new PluginMenuSyncService(BaseDbContext).ApplyDefaultIcons(PluginId, EformBackendConfigurationPlugin.DefaultMenuIcons);

    private Task<string> IconOfTheGroupAsync() =>
        BaseDbContext.MenuItems.AsNoTracking().Where(x => x.E2EId == GroupE2EId && x.Type == MenuItemTypeEnum.Dropdown)
            .Select(x => x.IconName).SingleAsync();

    private int Run(IReadOnlyList<PluginMenuItemModel> menu) => new PluginMenuSyncService(BaseDbContext).Run(PluginId, menu);

    private static List<PluginMenuItemModel> Menu() => new EformBackendConfigurationPlugin().GetNavigationMenu(null!);

    /// <summary>The plugin's menu as it was before the Kemi group: what an existing tenant has.</summary>
    private static List<PluginMenuItemModel> WithoutChemicals(List<PluginMenuItemModel> menu) =>
        menu.Where(x => x.E2EId != GroupE2EId).ToList();

    /// <summary>
    /// The plugin's menu with some Kemi screens not shipped yet, e.g. what an E1 tenant (Rettigheder only) or an
    /// E2 tenant (Rettigheder and Indstillinger) has.
    /// </summary>
    private static List<PluginMenuItemModel> WithoutChildren(List<PluginMenuItemModel> menu, params string[] e2eIds)
    {
        menu.Single(x => x.E2EId == GroupE2EId).ChildItems.RemoveAll(c => e2eIds.Contains(c.E2EId));
        return menu;
    }

    /// <summary>A future release that adds a second Kemi screen.</summary>
    private static List<PluginMenuItemModel> MenuWithLaterChild()
    {
        var menu = Menu();
        menu.Single(x => x.E2EId == GroupE2EId).ChildItems.Add(Link(LaterChildE2EId, 9));
        return menu;
    }

    private static PluginMenuItemModel Link(string e2eId, int position, params PluginMenuTemplatePermissionModel[] permissions)
    {
        var link = $"/plugins/backend-configuration-pn/{e2eId}";
        return new PluginMenuItemModel
        {
            Name = e2eId, E2EId = e2eId, Link = link, Type = MenuItemTypeEnum.Link, Position = position,
            MenuTemplate = new PluginMenuTemplateModel
            {
                Name = e2eId, E2EId = e2eId, DefaultLink = link, Permissions = [.. permissions],
                Translations = [new() { LocaleName = LocaleNames.English, Name = e2eId, Language = LanguageNames.English }]
            },
            Translations = [new() { LocaleName = LocaleNames.English, Name = e2eId, Language = LanguageNames.English }]
        };
    }

    private static void AssertNames(IEnumerable<(string LocaleName, string Name, string Language)> rows,
        string danish, string english, string german, string ukrainian)
    {
        var byLocale = rows.ToList();
        Assert.That(byLocale, Has.Count.EqualTo(4));
        Assert.That(byLocale.Single(r => r.LocaleName == LocaleNames.Danish), Is.EqualTo((LocaleNames.Danish, danish, LanguageNames.Danish)));
        Assert.That(byLocale.Single(r => r.LocaleName == LocaleNames.English), Is.EqualTo((LocaleNames.English, english, LanguageNames.English)));
        Assert.That(byLocale.Single(r => r.LocaleName == LocaleNames.German), Is.EqualTo((LocaleNames.German, german, LanguageNames.German)));
        Assert.That(byLocale.Single(r => r.LocaleName == LocaleNames.Ukrainian), Is.EqualTo((LocaleNames.Ukrainian, ukrainian, LanguageNames.Ukrainian)));
    }

    /// <summary>
    /// A Kemi child as the sync writes it: a link to its own screen under the group, internal, at its position,
    /// named in the four menu languages, with a template of the plugin that carries the same link and no permissions.
    /// </summary>
    private async Task AssertLinkChildAsync(string e2eId, string screen, int position,
        string danish, string english, string german, string ukrainian)
    {
        var group = await BaseDbContext.MenuItems.AsNoTracking().SingleAsync(x => x.E2EId == GroupE2EId);
        var child = await BaseDbContext.MenuItems.AsNoTracking()
            .Include(x => x.Translations).SingleAsync(x => x.E2EId == e2eId);
        Assert.That(child.ParentId, Is.EqualTo(group.Id));
        Assert.That(child.Type, Is.EqualTo(MenuItemTypeEnum.Link));
        Assert.That(child.Link, Is.EqualTo($"/plugins/backend-configuration-pn/chemicals/{screen}"));
        Assert.That(child.Position, Is.EqualTo(position));
        Assert.That(child.IsInternalLink, Is.True, "PluginMenuItemModel.IsInternalLink defaults to false; copying it would open Kemi in a new tab");
        AssertNames(child.Translations.Select(t => (t.LocaleName, t.Name, t.Language)), danish, english, german, ukrainian);

        var template = await BaseDbContext.MenuTemplates.AsNoTracking()
            .Include(x => x.Translations).Include(x => x.MenuTemplatePermissions)
            .SingleAsync(x => x.E2EId == e2eId);
        Assert.That(child.MenuTemplateId, Is.EqualTo(template.Id));
        Assert.That(template.EformPluginId, Is.EqualTo(_pluginRowId));
        Assert.That(template.DefaultLink, Is.EqualTo(child.Link));
        Assert.That(template.MenuTemplatePermissions, Is.Empty);
        AssertNames(template.Translations.Select(t => (t.LocaleName, t.Name, t.Language)), danish, english, german, ukrainian);
    }

    /// <summary>The group's children in menu order (position, then id as the host breaks ties).</summary>
    private async Task<List<(string E2EId, int Position)>> ChildrenOfTheGroupAsync()
    {
        var group = await BaseDbContext.MenuItems.AsNoTracking().SingleAsync(x => x.E2EId == GroupE2EId);
        var children = await BaseDbContext.MenuItems.AsNoTracking()
            .Where(x => x.ParentId == group.Id).OrderBy(x => x.Position).ThenBy(x => x.Id)
            .Select(x => new { x.E2EId, x.Position }).ToListAsync();
        return children.Select(x => (x.E2EId, x.Position)).ToList();
    }

    /// <summary>The group shows exactly these children in this order, with no tie left for the host to break by id.</summary>
    private async Task AssertChildOrderAsync(params string[] expected)
    {
        var children = await ChildrenOfTheGroupAsync();
        Assert.That(children.Select(x => x.E2EId), Is.EqualTo(expected));
        Assert.That(children.Select(x => x.Position), Is.Unique, "no tie left for the host to break by id");
    }

    /// <summary>What the menu editor's save does to positions: sets the given children's positions.</summary>
    private async Task RenumberAsync(params (string E2EId, int Position)[] rows)
    {
        foreach (var (e2eId, position) in rows)
        {
            await BaseDbContext.MenuItems.Where(x => x.E2EId == e2eId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Position, position));
        }
    }

    /// <summary>What the admin's "delete" in the menu editor leaves: the Kemi rows gone, every template kept.</summary>
    private async Task DeleteTheKemiMenuItemsAsync()
    {
        var group = await BaseDbContext.MenuItems.SingleAsync(x => x.E2EId == GroupE2EId);
        var ids = await BaseDbContext.MenuItems.Where(x => x.Id == group.Id || x.ParentId == group.Id)
            .Select(x => x.Id).ToListAsync();
        await BaseDbContext.MenuItemTranslations.Where(x => ids.Contains(x.MenuItemId)).ExecuteDeleteAsync();
        await BaseDbContext.MenuItems.Where(x => x.ParentId == group.Id).ExecuteDeleteAsync();
        await BaseDbContext.MenuItems.Where(x => x.Id == group.Id).ExecuteDeleteAsync();
        BaseDbContext.ChangeTracker.Clear();
    }

    private async Task<(int Items, int ItemTranslations, int Templates, int TemplateTranslations, int TemplatePermissions)> SnapshotAsync() =>
    (
        await BaseDbContext.MenuItems.CountAsync(),
        await BaseDbContext.MenuItemTranslations.CountAsync(),
        await BaseDbContext.MenuTemplates.CountAsync(),
        await BaseDbContext.MenuTemplateTranslations.CountAsync(),
        await BaseDbContext.MenuTemplatePermissions.CountAsync()
    );

    /// <summary>
    /// The host's install path (eFormAPI.Web PluginMenuItemsLoader: SimpleLinkLoader and
    /// IsNotSimpleLinkLoader), reduced to the rows the sync reads. Used to build the
    /// "fresh install" and "existing tenant" states the sync starts from.
    /// </summary>
    private async Task InstallAsTheHostDoesAsync(List<PluginMenuItemModel> menu)
    {
        foreach (var item in menu.OrderBy(x => x.Position))
        {
            var rootPosition = await BaseDbContext.MenuItems.Where(x => x.ParentId == null).MaxAsync(x => x.Position)
                               + item.Position + 1;
            if (item.Type == MenuItemTypeEnum.Link)
            {
                await AddHostLinkAsync(item, null, rootPosition);
                continue;
            }

            var group = new MenuItem
            {
                Name = "Dropdown", E2EId = item.E2EId, Link = item.Link, Type = item.Type, Position = rootPosition,
                Translations = item.Translations
                    .Select(t => new MenuItemTranslation { Name = t.Name, LocaleName = t.LocaleName, Language = t.Language })
                    .ToList()
            };
            BaseDbContext.MenuItems.Add(group);
            await BaseDbContext.SaveChangesAsync();
            foreach (var child in item.ChildItems)
            {
                await AddHostLinkAsync(child, group.Id, child.Position);
            }
        }

        BaseDbContext.ChangeTracker.Clear();
    }

    private async Task AddHostLinkAsync(PluginMenuItemModel item, int? parentId, int position)
    {
        var template = new MenuTemplate
        {
            Name = item.MenuTemplate.Name, E2EId = item.MenuTemplate.E2EId, DefaultLink = item.MenuTemplate.DefaultLink,
            EformPluginId = _pluginRowId,
            Translations = item.MenuTemplate.Translations
                .Select(t => new MenuTemplateTranslation { Name = t.Name, LocaleName = t.LocaleName, Language = t.Language })
                .ToList()
        };
        BaseDbContext.MenuTemplates.Add(template);
        BaseDbContext.MenuItems.Add(new MenuItem
        {
            Name = item.Name, E2EId = item.E2EId, Link = item.Link, Type = item.Type, Position = position,
            MenuTemplate = template, ParentId = parentId,
            Translations = item.Translations
                .Select(t => new MenuItemTranslation { Name = t.Name, LocaleName = t.LocaleName, Language = t.Language })
                .ToList()
        });
        await BaseDbContext.SaveChangesAsync();
    }
}
