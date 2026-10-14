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
    private const string PermissionsE2EId = "backend-configuration-pn-chemicals-permissions";
    private const string LaterChildE2EId = "backend-configuration-pn-chemicals-later-child";

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
    }

    [Test]
    public async Task ExistingTenantWithoutKemi_GetsTheGroupAndItsChildOnTheNextStartup()
    {
        await InstallAsTheHostDoesAsync(WithoutChemicals(Menu()));
        var lastRootPosition = await BaseDbContext.MenuItems.Where(x => x.ParentId == null).MaxAsync(x => x.Position);

        Assert.That(Run(Menu()), Is.EqualTo(2), "the group and its one child");

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

        var child = await BaseDbContext.MenuItems.AsNoTracking()
            .Include(x => x.Translations).SingleAsync(x => x.E2EId == PermissionsE2EId);
        Assert.That(child.ParentId, Is.EqualTo(group.Id));
        Assert.That(child.Type, Is.EqualTo(MenuItemTypeEnum.Link));
        Assert.That(child.Link, Is.EqualTo("/plugins/backend-configuration-pn/chemicals/permissions"));
        Assert.That(child.Position, Is.EqualTo(2));
        Assert.That(child.IsInternalLink, Is.True, "PluginMenuItemModel.IsInternalLink defaults to false; copying it would open Kemi in a new tab");
        AssertNames(child.Translations.Select(t => (t.LocaleName, t.Name, t.Language)),
            "Rettigheder", "Permissions", "Berechtigungen", "Дозволи");

        var template = await BaseDbContext.MenuTemplates.AsNoTracking()
            .Include(x => x.Translations).Include(x => x.MenuTemplatePermissions)
            .SingleAsync(x => x.E2EId == PermissionsE2EId);
        Assert.That(child.MenuTemplateId, Is.EqualTo(template.Id));
        Assert.That(template.EformPluginId, Is.EqualTo(_pluginRowId));
        Assert.That(template.DefaultLink, Is.EqualTo(child.Link));
        Assert.That(template.MenuTemplatePermissions, Is.Empty);
        AssertNames(template.Translations.Select(t => (t.LocaleName, t.Name, t.Language)),
            "Rettigheder", "Permissions", "Berechtigungen", "Дозволи");
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

        var group = await BaseDbContext.MenuItems.AsNoTracking().SingleAsync(x => x.E2EId == GroupE2EId);
        var children = await BaseDbContext.MenuItems.AsNoTracking()
            .Where(x => x.ParentId == group.Id).OrderBy(x => x.Position).Select(x => x.E2EId).ToListAsync();
        Assert.That(children, Is.EqualTo(new[] { PermissionsE2EId, LaterChildE2EId }));
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
        Assert.That(Run(Menu()), Is.EqualTo(2), "the group and its one child");
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

    private int Run(IReadOnlyList<PluginMenuItemModel> menu) => new PluginMenuSyncService(BaseDbContext).Run(PluginId, menu);

    private static List<PluginMenuItemModel> Menu() => new EformBackendConfigurationPlugin().GetNavigationMenu(null!);

    /// <summary>The plugin's menu as it was before the Kemi group: what an existing tenant has.</summary>
    private static List<PluginMenuItemModel> WithoutChemicals(List<PluginMenuItemModel> menu) =>
        menu.Where(x => x.E2EId != GroupE2EId).ToList();

    /// <summary>A future release that adds a second Kemi screen.</summary>
    private static List<PluginMenuItemModel> MenuWithLaterChild()
    {
        var menu = Menu();
        menu.Single(x => x.E2EId == GroupE2EId).ChildItems.Add(Link(LaterChildE2EId, 3));
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
