/*
The MIT License (MIT)
Copyright (c) 2007 - 2026 Microting A/S
Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:
The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.
THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

namespace BackendConfiguration.Pn.Services.PluginMenuSyncService;

using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microting.eFormApi.BasePn.Infrastructure.Models.Application.NavigationMenu;
using Microting.EformAngularFrontendBase.Infrastructure.Data;
using Microting.EformAngularFrontendBase.Infrastructure.Data.Entities.Menu;
using Microting.EformAngularFrontendBase.Infrastructure.Data.Entities.Permissions;

/// <summary>
/// Adds this plugin's new menu entries to an existing tenant (flutter-chemistry Plan E
/// ruling E-04). The host writes a plugin's MenuItem rows on first install only; at later
/// boots it adds a missing MenuTemplate but no MenuItem, so a new entry never reaches the
/// menu of a tenant that already has the plugin.
///
/// <para>Runs from <c>SeedDatabase</c> at every startup, which is before the host's own
/// startup menu pass (<c>LoadNavigationMenuEnabledPlugins</c>) in the same process.</para>
///
/// <para>The MenuTemplate is the "offered once" marker, keyed on its E2EId: an entry is
/// added only when the tenant has never had its template. The menu editor deletes
/// MenuItem rows and keeps templates, so an entry an admin deleted stays deleted, and a
/// group whose children were all deleted is not recreated for a later child either.
/// Nothing is written to PluginConfigurationValues (Plan D: a duplicate marker row there
/// takes the plugin down).</para>
///
/// <para>A plugin without any template is a fresh install, which the host's full loader
/// owns later in the same boot, so the sync leaves it alone.</para>
///
/// <para>The row shapes copy the host's SimpleLinkLoader / IsNotSimpleLinkLoader
/// (eFormAPI.Web, which a plugin cannot reference), with two deliberate differences: a
/// new root entry goes to the end of the menu (max root position + 1), and
/// IsInternalLink keeps the entity default (true) because the model's false default is
/// one the host loaders ignore too.</para>
///
/// <para>Not atomic across processes: two pods booting against one tenant database at
/// the same moment can both add an entry. The cost is a duplicate menu row, which the
/// admin removes in the menu editor; nothing reads menu rows with Single.</para>
/// </summary>
public class PluginMenuSyncService(BaseDbContext dbContext)
{
    /// <summary>eFormAPI.Web.Hosting.Enums.PluginStatus.Enabled (host enum, not referenceable).</summary>
    private const int EnabledStatus = 2;

    /// <summary>Adds the missing entries of <paramref name="menu"/>; returns how many menu items it wrote.</summary>
    public int Run(string pluginId, IReadOnlyList<PluginMenuItemModel> menu)
    {
        var plugin = dbContext.EformPlugins.AsNoTracking().FirstOrDefault(x => x.PluginId == pluginId);
        if (plugin is not { Status: EnabledStatus })
        {
            return 0;
        }

        var offered = dbContext.MenuTemplates.AsNoTracking()
            .Where(x => x.EformPluginId == plugin.Id)
            .Select(x => x.E2EId)
            .ToHashSet();
        if (offered.Count == 0)
        {
            return 0;
        }

        var added = 0;
        foreach (var item in menu.OrderBy(x => x.Position))
        {
            if (item.Type == MenuItemTypeEnum.Link)
            {
                if (!offered.Contains(item.MenuTemplate.E2EId))
                {
                    added += InTransaction(() =>
                    {
                        AddLink(item, plugin.Id, null, NextRootPosition());
                        return 1;
                    });
                }

                continue;
            }

            if (item.Type != MenuItemTypeEnum.Dropdown)
            {
                continue;
            }

            var links = item.ChildItems.Where(x => x.Type == MenuItemTypeEnum.Link).ToList();
            var missing = links.Where(x => !offered.Contains(x.MenuTemplate.E2EId)).OrderBy(x => x.Position).ToList();
            if (missing.Count == 0)
            {
                continue;
            }

            var groupWasOffered = links.Count > missing.Count;
            added += InTransaction(() => AddChildren(item, missing, plugin.Id, groupWasOffered));
        }

        return added;
    }

    private int AddChildren(PluginMenuItemModel group, List<PluginMenuItemModel> children, int pluginRowId,
        bool groupWasOffered)
    {
        // Any parent: the admin may have moved the group.
        var groupId = dbContext.MenuItems
            .Where(x => x.E2EId == group.E2EId && x.Type == MenuItemTypeEnum.Dropdown)
            .OrderBy(x => x.Id)
            .Select(x => (int?)x.Id)
            .FirstOrDefault();
        var written = 0;
        if (groupId == null)
        {
            if (groupWasOffered)
            {
                // The tenant had this group and the admin deleted it. The host's own
                // startup pass adds the new children's templates later in this boot,
                // so the menu editor still offers them.
                return 0;
            }

            var newGroup = new MenuItem
            {
                Name = "Dropdown",
                E2EId = group.E2EId,
                Link = group.Link,
                Type = MenuItemTypeEnum.Dropdown,
                Position = NextRootPosition(),
                Translations = group.Translations.Select(ToItemTranslation).ToList()
            };
            dbContext.MenuItems.Add(newGroup);
            dbContext.SaveChanges();
            groupId = newGroup.Id;
            written++;
        }

        foreach (var child in children)
        {
            AddLink(child, pluginRowId, groupId, child.Position);
            written++;
        }

        return written;
    }

    private void AddLink(PluginMenuItemModel item, int pluginRowId, int? parentId, int position)
    {
        dbContext.MenuItems.Add(new MenuItem
        {
            Name = item.Name,
            E2EId = item.E2EId,
            Link = item.Link,
            Type = MenuItemTypeEnum.Link,
            Position = position,
            ParentId = parentId,
            MenuTemplate = AddTemplate(item.MenuTemplate, pluginRowId),
            Translations = item.Translations.Select(ToItemTranslation).ToList()
        });
        dbContext.SaveChanges();
    }

    private MenuTemplate AddTemplate(PluginMenuTemplateModel model, int pluginRowId)
    {
        var template = new MenuTemplate
        {
            Name = model.Name,
            E2EId = model.E2EId,
            DefaultLink = model.DefaultLink,
            EformPluginId = pluginRowId,
            Translations = model.Translations
                .Select(t => new MenuTemplateTranslation { Name = t.Name, LocaleName = t.LocaleName, Language = t.Language })
                .ToList()
        };
        foreach (var permission in model.Permissions)
        {
            // The host's SimpleLinkLoader: reuse the permission type by name, always a new Permission row.
            var type = dbContext.PermissionTypes.Local.FirstOrDefault(x => x.Name == permission.PermissionTypeName)
                       ?? dbContext.PermissionTypes.FirstOrDefault(x => x.Name == permission.PermissionTypeName)
                       ?? new PermissionType { Name = permission.PermissionTypeName };
            template.MenuTemplatePermissions.Add(new MenuTemplatePermission
            {
                Permission = new Permission
                {
                    PermissionName = permission.PermissionName,
                    ClaimName = permission.ClaimName,
                    PermissionType = type
                }
            });
        }

        dbContext.MenuTemplates.Add(template);
        return template;
    }

    private static MenuItemTranslation ToItemTranslation(PluginMenuTranslationModel t) =>
        new() { Name = t.Name, LocaleName = t.LocaleName, Language = t.Language };

    private int NextRootPosition() =>
        (dbContext.MenuItems.Where(x => x.ParentId == null).Max(x => (int?)x.Position) ?? 0) + 1;

    /// <summary>
    /// One menu entry (a link, or a group with its new children) per transaction, so a
    /// crash never leaves a template without its item: the template alone would mark the
    /// entry as offered and the sync would never add it. Inside the execution strategy
    /// because BaseDbContextFactory enables retry on failure; the change tracker is
    /// cleared so a retried attempt starts from what the database holds.
    /// </summary>
    private int InTransaction(System.Func<int> work) =>
        dbContext.Database.CreateExecutionStrategy().Execute(() =>
        {
            dbContext.ChangeTracker.Clear();
            using var transaction = dbContext.Database.BeginTransaction();
            var written = work();
            transaction.Commit();
            return written;
        });
}
