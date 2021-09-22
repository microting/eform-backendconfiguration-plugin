/*/*
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

namespace BackendConfiguration.Pn.Infrastructure.Data.Seed
{
    using System;
    using System.Linq;
    using Data;
    using Microting.eForm.Infrastructure.Constants;
    using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;
    using Microting.EformBackendConfigurationBase.Infrastructure.Data;
    using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;

    public static class BackendConfigurationPluginSeed
    {
        public static void SeedData(BackendConfigurationPnDbContext dbContext)
        {
            var seedData = new BackendConfigurationSeedData();
            var configurationList = seedData.Data;
            foreach (var configurationItem in configurationList)
            {
                if (!dbContext.PluginConfigurationValues.Any(x=> x.Name == configurationItem.Name))
                {
                    var newConfigValue = new PluginConfigurationValue
                    {
                        Name = configurationItem.Name,
                        Value = configurationItem.Value,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                        Version = 1,
                        WorkflowState = Constants.WorkflowStates.Created,
                        CreatedByUserId = 1
                    };
                    dbContext.PluginConfigurationValues.Add(newConfigValue);
                    dbContext.SaveChanges();
                }
            }

            // Seed plugin permissions
            var newPermissions = BackendConfigurationPermissionsSeedData.Data
                .Where(p => dbContext.PluginPermissions.All(x => x.ClaimName != p.ClaimName))
                .Select(p => new PluginPermission
                {
                    PermissionName = p.PermissionName,
                    ClaimName = p.ClaimName,
                    CreatedAt = DateTime.UtcNow,
                    Version = 1,
                    WorkflowState = Constants.WorkflowStates.Created,
                    CreatedByUserId = 1
                }
                );
            dbContext.PluginPermissions.AddRange(newPermissions);

            dbContext.SaveChanges();

            //// Seed areas
            //var newAreas = BackendConfigurationSeedAreas.AreasSeed
            //    .Where(p => dbContext.Areas.All(x => x.Name != p.Name))
            //    .Select(p => new Area
            //    {
            //        Id = p.Id,
            //        Name = p.Name,
            //        Description = p.Description,
            //        CreatedAt = DateTime.UtcNow,
            //        Version = 1,
            //        WorkflowState = Constants.WorkflowStates.Created,
            //        CreatedByUserId = 1,
            //        UpdatedByUserId = 1,
            //        UpdatedAt = DateTime.UtcNow,
            //        Type = p.Type,
            //    }
            //    );
            //dbContext.Areas.AddRange(newAreas);

            //dbContext.SaveChanges();


            //// Seed area rules
            //var newAreaRules = BackendConfigurationSeedAreaRules.AreaRulesSeed
            //    .Where(p => dbContext.AreaRules.All(x => x.AreaId != p.AreaId))
            //    .Select(p => new AreaRules
            //    {
            //        Id = p.Id,
            //        AreaId = p.AreaId,
            //        EformId = p.EformId,
            //        EformName = p.EformName,
            //        FolderId = p.FolderId,
            //        FolderName = p.FolderName,
            //        CreatedAt = DateTime.UtcNow,
            //        Version = 1,
            //        WorkflowState = Constants.WorkflowStates.Created,
            //        CreatedByUserId = 1,
            //        UpdatedByUserId = 1,
            //        UpdatedAt = DateTime.UtcNow,
            //        AreaRuleTranslations = p.AreaRuleTranslations,
            //    }
            //    );
            //dbContext.AreaRules.AddRange(newAreaRules);

            //dbContext.SaveChanges();
        }
    }
}