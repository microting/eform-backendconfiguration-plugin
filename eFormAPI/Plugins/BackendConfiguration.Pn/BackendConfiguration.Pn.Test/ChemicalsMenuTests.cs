using System.Collections.Generic;
using System.Linq;
using Microting.eFormApi.BasePn.Infrastructure.Consts;
using Microting.eFormApi.BasePn.Infrastructure.Models.Application.NavigationMenu;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Test;

/// <summary>
/// The Kemi web screens (flutter-chemistry spec §11, mockup web-admin.html) hang under
/// one "Kemi" menu group. Each screen adds its own child in the release that ships the
/// screen (Plan E ruling E-03), so a release never shows a dead link. E1 ships the
/// group with its first child, Rettigheder (W4).
/// </summary>
[TestFixture]
public class ChemicalsMenuTests
{
    private const string GroupE2EId = "backend-configuration-pn-chemicals";
    private const string InventoryE2EId = "backend-configuration-pn-chemicals-inventory";
    private const string LocationsE2EId = "backend-configuration-pn-chemicals-locations";
    private const string PermissionsE2EId = "backend-configuration-pn-chemicals-permissions";
    private const string SettingsE2EId = "backend-configuration-pn-chemicals-settings";
    private const string HistoryE2EId = "backend-configuration-pn-chemicals-history";
    private const string SettingsLink = "/plugins/backend-configuration-pn/chemicals/settings";
    private const string PermissionsLink = "/plugins/backend-configuration-pn/chemicals/permissions";
    // W1 has its own path (Task 12 ruling I-4), so the host's prefix-matching routerLinkActive marks
    // Beholdning only on W1; the Angular routing redirects the bare Kemi root here.
    private const string InventoryLink = "/plugins/backend-configuration-pn/chemicals/inventory";
    private const string LocationsLink = "/plugins/backend-configuration-pn/chemicals/locations";
    private const string HistoryLink = "/plugins/backend-configuration-pn/chemicals/history";

    private static List<PluginMenuItemModel> Menu() =>
        new EformBackendConfigurationPlugin().GetNavigationMenu(null!);

    private static PluginMenuItemModel Group() => Menu().Single(i => i.E2EId == GroupE2EId);

    private static PluginMenuItemModel PermissionsChild() =>
        Group().ChildItems.Single(c => c.E2EId == PermissionsE2EId);

    private static PluginMenuItemModel InventoryChild() =>
        Group().ChildItems.Single(c => c.E2EId == InventoryE2EId);

    private static PluginMenuItemModel LocationsChild() =>
        Group().ChildItems.Single(c => c.E2EId == LocationsE2EId);

    private static PluginMenuItemModel SettingsChild() =>
        Group().ChildItems.Single(c => c.E2EId == SettingsE2EId);

    private static PluginMenuItemModel HistoryChild() =>
        Group().ChildItems.Single(c => c.E2EId == HistoryE2EId);

    private static string Name(IEnumerable<PluginMenuTranslationModel> translations, string locale) =>
        translations.Single(t => t.LocaleName == locale).Name;

    [Test]
    public void Menu_HasExactlyOneChemicalsGroup()
    {
        var items = Menu().Where(i => i.E2EId == GroupE2EId).ToList();

        Assert.That(items, Has.Count.EqualTo(1));
        var group = items.Single();
        Assert.That(group.Type, Is.EqualTo(MenuItemTypeEnum.Dropdown));
        Assert.That(group.Name, Is.EqualTo("Dropdown"), "the host names every dropdown row \"Dropdown\"; the label comes from Translations");
        Assert.That(group.Link, Is.Empty);
        Assert.That(group.MenuTemplate, Is.Null, "a dropdown has no template; the host loaders never read one");
    }

    [Test]
    public void ChemicalsGroupComesAfterEveryExistingItem()
    {
        var menu = Menu();
        var group = menu.Single(i => i.E2EId == GroupE2EId);

        Assert.That(group.Position, Is.EqualTo(15), "Tail biting holds 14 (ruling E-02)");
        Assert.That(menu.Where(i => i.E2EId != GroupE2EId).Select(i => i.Position), Has.All.LessThan(group.Position));
    }

    [Test]
    public void ChemicalsGroupIsNamedInTheFourMenuLanguages()
    {
        var translations = Group().Translations;

        Assert.That(translations, Has.Count.EqualTo(4));
        Assert.That(Name(translations, LocaleNames.Danish), Is.EqualTo("Kemi"));
        Assert.That(Name(translations, LocaleNames.English), Is.EqualTo("Chemicals"));
        Assert.That(Name(translations, LocaleNames.German), Is.EqualTo("Chemikalien"));
        Assert.That(Name(translations, LocaleNames.Ukrainian), Is.EqualTo("Хімікати"));
    }

    [Test]
    public void PermissionsChildLinksToTheW4Screen()
    {
        var group = Group();
        Assert.That(group.ChildItems.Select(c => c.E2EId),
            Is.EqualTo(new[] { InventoryE2EId, LocationsE2EId, PermissionsE2EId, SettingsE2EId, HistoryE2EId }),
            "E1 shipped Rettigheder, E2 Indstillinger, E3 Beholdning, Lokationer and Historik; later releases add their own children");

        var child = PermissionsChild();
        Assert.That(child.Type, Is.EqualTo(MenuItemTypeEnum.Link));
        Assert.That(child.Link, Is.EqualTo(PermissionsLink));
        Assert.That(child.Position, Is.EqualTo(2), "mockup order: Beholdning 0, Lokationer 1, Rettigheder 2, Indstillinger 3, Historik 4");
        Assert.That(child.MenuTemplate, Is.Not.Null);
        Assert.That(child.MenuTemplate.E2EId, Is.EqualTo(PermissionsE2EId));
        Assert.That(child.MenuTemplate.DefaultLink, Is.EqualTo(PermissionsLink));
        Assert.That(child.MenuTemplate.Permissions, Is.Empty, "open to every plugin user, like Adhoc and Compliance");
    }

    [Test]
    public void PermissionsChildIsNamedInTheFourMenuLanguages()
    {
        var child = PermissionsChild();

        foreach (var translations in new[] { child.Translations, child.MenuTemplate.Translations })
        {
            Assert.That(translations, Has.Count.EqualTo(4));
            Assert.That(Name(translations, LocaleNames.Danish), Is.EqualTo("Rettigheder"));
            Assert.That(Name(translations, LocaleNames.English), Is.EqualTo("Permissions"));
            Assert.That(Name(translations, LocaleNames.German), Is.EqualTo("Berechtigungen"));
            Assert.That(Name(translations, LocaleNames.Ukrainian), Is.EqualTo("Дозволи"));
        }
    }

    [Test]
    public void InventoryChildLinksToTheW1ScreenAtItsOwnPath()
    {
        var child = InventoryChild();

        Assert.That(child.Type, Is.EqualTo(MenuItemTypeEnum.Link));
        Assert.That(child.Link, Is.EqualTo(InventoryLink), "not the bare Kemi root, which every Kemi page starts with");
        Assert.That(child.Position, Is.EqualTo(0), "mockup order: Beholdning 0, Lokationer 1, Rettigheder 2, Indstillinger 3, Historik 4");
        Assert.That(child.MenuTemplate, Is.Not.Null);
        Assert.That(child.MenuTemplate.E2EId, Is.EqualTo(InventoryE2EId));
        Assert.That(child.MenuTemplate.DefaultLink, Is.EqualTo(InventoryLink));
        Assert.That(child.MenuTemplate.Permissions, Is.Empty, "open to every plugin user, like Rettigheder");
    }

    [Test]
    public void InventoryChildIsNamedInTheFourMenuLanguages()
    {
        var child = InventoryChild();

        foreach (var translations in new[] { child.Translations, child.MenuTemplate.Translations })
        {
            Assert.That(translations, Has.Count.EqualTo(4));
            Assert.That(Name(translations, LocaleNames.Danish), Is.EqualTo("Beholdning"));
            Assert.That(Name(translations, LocaleNames.English), Is.EqualTo("Inventory"));
            Assert.That(Name(translations, LocaleNames.German), Is.EqualTo("Bestand"));
            Assert.That(Name(translations, LocaleNames.Ukrainian), Is.EqualTo("Запаси"));
        }
    }

    [Test]
    public void LocationsChildLinksToTheW3Screen()
    {
        var child = LocationsChild();

        Assert.That(child.Type, Is.EqualTo(MenuItemTypeEnum.Link));
        Assert.That(child.Link, Is.EqualTo(LocationsLink));
        Assert.That(child.Position, Is.EqualTo(1), "mockup order: Beholdning 0, Lokationer 1, Rettigheder 2, Indstillinger 3, Historik 4");
        Assert.That(child.MenuTemplate, Is.Not.Null);
        Assert.That(child.MenuTemplate.E2EId, Is.EqualTo(LocationsE2EId));
        Assert.That(child.MenuTemplate.DefaultLink, Is.EqualTo(LocationsLink));
        Assert.That(child.MenuTemplate.Permissions, Is.Empty, "open to every plugin user, like Rettigheder");
    }

    [Test]
    public void LocationsChildIsNamedInTheFourMenuLanguages()
    {
        var child = LocationsChild();

        foreach (var translations in new[] { child.Translations, child.MenuTemplate.Translations })
        {
            Assert.That(translations, Has.Count.EqualTo(4));
            Assert.That(Name(translations, LocaleNames.Danish), Is.EqualTo("Lokationer"));
            Assert.That(Name(translations, LocaleNames.English), Is.EqualTo("Locations"));
            Assert.That(Name(translations, LocaleNames.German), Is.EqualTo("Standorte"));
            Assert.That(Name(translations, LocaleNames.Ukrainian), Is.EqualTo("Локації"));
        }
    }

    [Test]
    public void SettingsChildLinksToTheW5Screen()
    {
        var child = SettingsChild();

        Assert.That(child.Type, Is.EqualTo(MenuItemTypeEnum.Link));
        Assert.That(child.Link, Is.EqualTo(SettingsLink));
        Assert.That(child.Position, Is.EqualTo(3), "mockup order: Beholdning 0, Lokationer 1, Rettigheder 2, Indstillinger 3, Historik 4");
        Assert.That(child.MenuTemplate, Is.Not.Null);
        Assert.That(child.MenuTemplate.E2EId, Is.EqualTo(SettingsE2EId));
        Assert.That(child.MenuTemplate.DefaultLink, Is.EqualTo(SettingsLink));
        Assert.That(child.MenuTemplate.Permissions, Is.Empty, "open to every plugin user, like Rettigheder");
    }

    [Test]
    public void SettingsChildIsNamedInTheFourMenuLanguages()
    {
        var child = SettingsChild();

        foreach (var translations in new[] { child.Translations, child.MenuTemplate.Translations })
        {
            Assert.That(translations, Has.Count.EqualTo(4));
            Assert.That(Name(translations, LocaleNames.Danish), Is.EqualTo("Indstillinger"));
            Assert.That(Name(translations, LocaleNames.English), Is.EqualTo("Settings"));
            Assert.That(Name(translations, LocaleNames.German), Is.EqualTo("Einstellungen"));
            Assert.That(Name(translations, LocaleNames.Ukrainian), Is.EqualTo("Налаштування"));
        }
    }

    [Test]
    public void HistoryChildLinksToTheW6ScreenAsTheLastChild()
    {
        var group = Group();
        var child = HistoryChild();

        Assert.That(group.ChildItems.Last().E2EId, Is.EqualTo(HistoryE2EId), "Historik is the last Kemi screen (mockup order)");
        Assert.That(child.Type, Is.EqualTo(MenuItemTypeEnum.Link));
        Assert.That(child.Link, Is.EqualTo(HistoryLink));
        Assert.That(child.Position, Is.EqualTo(4), "mockup order: Beholdning 0, Lokationer 1, Rettigheder 2, Indstillinger 3, Historik 4");
        Assert.That(group.ChildItems.Where(c => c.E2EId != HistoryE2EId).Select(c => c.Position), Has.All.LessThan(child.Position));
        Assert.That(child.MenuTemplate, Is.Not.Null);
        Assert.That(child.MenuTemplate.E2EId, Is.EqualTo(HistoryE2EId));
        Assert.That(child.MenuTemplate.DefaultLink, Is.EqualTo(HistoryLink));
        Assert.That(child.MenuTemplate.Permissions, Is.Empty, "open to every plugin user, like Rettigheder");
    }

    [Test]
    public void HistoryChildIsNamedInTheFourMenuLanguages()
    {
        var child = HistoryChild();

        foreach (var translations in new[] { child.Translations, child.MenuTemplate.Translations })
        {
            Assert.That(translations, Has.Count.EqualTo(4));
            Assert.That(Name(translations, LocaleNames.Danish), Is.EqualTo("Historik"));
            Assert.That(Name(translations, LocaleNames.English), Is.EqualTo("History"));
            Assert.That(Name(translations, LocaleNames.German), Is.EqualTo("Verlauf"));
            Assert.That(Name(translations, LocaleNames.Ukrainian), Is.EqualTo("Історія"));
        }
    }

    /// <summary>
    /// The host's startup template sync and the plugin's menu sync both key on the
    /// template E2EId, and the sync finds a group by its E2EId, so no two templates or
    /// groups may share one, and every link row carries its template's id.
    /// </summary>
    [Test]
    public void EveryMenuTemplateAndGroupE2EIdIsUnique()
    {
        var menu = Menu();
        var links = menu.SelectMany(i => i.Type == MenuItemTypeEnum.Dropdown ? i.ChildItems : [i]).ToList();
        var groupIds = menu.Where(i => i.Type == MenuItemTypeEnum.Dropdown).Select(i => i.E2EId);

        Assert.That(links.Select(i => i.MenuTemplate.E2EId).Concat(groupIds), Is.Unique);
        Assert.That(links.Where(i => i.E2EId != i.MenuTemplate.E2EId).Select(i => i.E2EId), Is.Empty,
            "a link's E2EId must equal its template's");
    }

    /// <summary>
    /// Kemi was seeded as a top-level entry without a glyph. PluginMenuItemModel has no icon, so the
    /// plugin names one per E2EId and the menu sync writes it; the glyph is a Material Symbols name like its siblings'.
    /// </summary>
    [Test]
    public void TheKemiGroupHasAMaterialSymbolsIcon()
    {
        Assert.That(EformBackendConfigurationPlugin.DefaultMenuIcons.TryGetValue(GroupE2EId, out var icon), Is.True);
        Assert.That(icon, Is.EqualTo("science"));
    }

    /// <summary>Halebid (tail biting) was seeded without a glyph too; a paw, like the animal-welfare topic it covers.</summary>
    [Test]
    public void TheTailBitingEntryHasAMaterialSymbolsIcon()
    {
        Assert.That(EformBackendConfigurationPlugin.DefaultMenuIcons.TryGetValue("backend-configuration-pn-tail-bite", out var icon), Is.True);
        Assert.That(icon, Is.EqualTo("pets"));
    }

    [Test]
    public void EveryDefaultIconBelongsToATopLevelEntry()
    {
        var topLevel = Menu().Select(i => i.E2EId).ToHashSet();

        Assert.That(EformBackendConfigurationPlugin.DefaultMenuIcons.Keys, Is.SubsetOf(topLevel));
    }
}
