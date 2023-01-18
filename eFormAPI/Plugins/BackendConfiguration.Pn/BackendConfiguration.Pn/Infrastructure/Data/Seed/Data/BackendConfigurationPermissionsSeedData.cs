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

namespace BackendConfiguration.Pn.Infrastructure.Data.Seed.Data
{
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
        };
    }
}