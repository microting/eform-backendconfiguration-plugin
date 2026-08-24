/*
The MIT License (MIT)

Copyright (c) 2007 - 2021 Microting A/S

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

namespace BackendConfiguration.Pn.Infrastructure.Data.Seed.Data;

using System.Collections.Generic;
using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Const;

public static class BackendConfigurationPermissionsSeedData
{
    public static IEnumerable<PluginPermission> Data => new[]
    {
        new PluginPermission
        {
            PermissionName = "Access BackendConfiguration Plugin",
            ClaimName = BackendConfigurationClaims.AccessBackendConfigurationPlugin
        },
        new PluginPermission
        {
            PermissionName = "Create property",
            ClaimName = BackendConfigurationClaims.CreateProperties
        },
        new PluginPermission
        {
            PermissionName = "Get properties",
            ClaimName = BackendConfigurationClaims.GetProperties
        },
        new PluginPermission
        {
            PermissionName = "Edit property",
            ClaimName = BackendConfigurationClaims.EditProperties
        },
        new PluginPermission
        {
            PermissionName = "Enable chemical management",
            ClaimName = BackendConfigurationClaims.EnableChemicalManagement
        },
        new PluginPermission
        {
            PermissionName = "Enable document management",
            ClaimName = BackendConfigurationClaims.EnableDocumentManagement
        },
        new PluginPermission
        {
            PermissionName = "Enable task management",
            ClaimName = BackendConfigurationClaims.EnableTaskManagement
        },
        new PluginPermission
        {
            PermissionName = "Enable time registration",
            ClaimName = BackendConfigurationClaims.EnableTimeRegistration
        },
        new PluginPermission
        {
            // RETAINED BUT UNENFORCED (2026-08-24): ad-hoc is open to every
            // authenticated user of the plugin, so nothing reads this claim any
            // more - the route guard was dropped and no [Authorize] policy or
            // controller references it. The entry stays because removing it
            // properly means deleting the PluginPermissions /
            // PluginGroupPermissions rows through a base-repo migration, which
            // is a separate change. Until then the admin-settings toggle for it
            // has no effect.
            //
            // Verified 2026-08-24: only four claims in this plugin are actually
            // enforced anywhere. backend_configuration_plugin_access (parent
            // route PermissionGuard, plus AdhocController's [Authorize] policy),
            // properties_get, task_management_enable (route PermissionGuard and
            // a checkClaim in property-worker-create-edit-modal) and
            // document_management_enable. properties_create, property_edit,
            // chemical_management_enable and time_registration_enable are seeded
            // here but read by nothing, and the `files` route's PermissionGuard
            // is commented out. So adhoc_enable is NOT the only unenforced claim
            // - but that is drift to clean up, not a licence to add more.
            PermissionName = "Enable adhoc",
            // TODO upstream to Microting.EformBackendConfigurationBase.Infrastructure.Const.
            // BackendConfigurationClaims.EnableAdhoc once a base-repo release train is open.
            // None was open at execution time (base repo is on master, clean, its last change
            // already shipped as the 10.0.46 release this branch depends on), and the NuGet
            // publish for that very release is itself still a pending-human step per the M3
            // status - see the plan's P1 "Claim const placement" note. Plain string literal
            // used here instead, matching PluginPermission.ClaimName's plain-string contract.
            ClaimName = "adhoc_enable"
        }
    };
}