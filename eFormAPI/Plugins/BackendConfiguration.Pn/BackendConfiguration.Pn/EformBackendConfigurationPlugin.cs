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


using BackendConfiguration.Pn.Services.ChemicalService;
using ChemicalsBase.Infrastructure;
using ChemicalsBase.Infrastructure.Data.Entities;
using ChemicalsBase.Infrastructure.Data.Factories;
using Microting.TimePlanningBase.Infrastructure.Data;

namespace BackendConfiguration.Pn
{
    using Infrastructure.Data.Seed;
    using Infrastructure.Data.Seed.Data;
    using Infrastructure.Models.Settings;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microting.eForm.Infrastructure.Constants;
    using Microting.eForm.Infrastructure.Data.Entities;
    using Microting.eForm.Infrastructure.Models;
    using Microting.eFormApi.BasePn;
    using Microting.eFormApi.BasePn.Abstractions;
    using Microting.eFormApi.BasePn.Infrastructure.Consts;
    using Microting.eFormApi.BasePn.Infrastructure.Database.Extensions;
    using Microting.eFormApi.BasePn.Infrastructure.Helpers;
    using Microting.eFormApi.BasePn.Infrastructure.Models.Application;
    using Microting.eFormApi.BasePn.Infrastructure.Models.Application.NavigationMenu;
    using Microting.eFormApi.BasePn.Infrastructure.Settings;
    using Microting.EformBackendConfigurationBase.Infrastructure.Const;
    using Microting.EformBackendConfigurationBase.Infrastructure.Data;
    using Microting.EformBackendConfigurationBase.Infrastructure.Data.Factories;
    using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
    using Microting.ItemsPlanningBase.Infrastructure.Data;
    using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
    using Services.BackendConfigurationAreaRulePlanningsService;
    using Services.BackendConfigurationAreaRulesService;
    using Services.BackendConfigurationAssignmentWorkerService;
    using Services.BackendConfigurationCompliancesService;
    using Services.BackendConfigurationLocalizationService;
    using Services.BackendConfigurationPropertiesService;
    using Services.BackendConfigurationPropertyAreasService;
    using Services.BackendConfigurationTaskManagementService;
    using Services.ExcelService;
    using Services.RebusService;
    using Services.WordService;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text.RegularExpressions;

    public class EformBackendConfigurationPlugin : IEformPlugin
    {
        public string Name => "Microting Backend Configuration Plugin";
        public string PluginId => "eform-backend-configuration-plugin";
        public string PluginPath => PluginAssembly().Location;
        public string PluginBaseUrl => "backend-configuration-pn";

        private string _connectionString;

        public Assembly PluginAssembly()
        {
            return typeof(EformBackendConfigurationPlugin).GetTypeInfo().Assembly;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<IBackendConfigurationLocalizationService, BackendConfigurationLocalizationService>();
            services
                .AddTransient<IBackendConfigurationAssignmentWorkerService,
                    BackendConfigurationAssignmentWorkerService>();
            services.AddTransient<IBackendConfigurationPropertiesService, BackendConfigurationPropertiesService>();
            services
                .AddTransient<IBackendConfigurationPropertyAreasService, BackendConfigurationPropertyAreasService>();
            services.AddTransient<IBackendConfigurationAreaRulesService, BackendConfigurationAreaRulesService>();
            services
                .AddTransient<IBackendConfigurationAreaRulePlanningsService,
                    BackendConfigurationAreaRulePlanningsService>();
            services.AddTransient<IBackendConfigurationCompliancesService, BackendConfigurationCompliancesService>();
            services
                .AddTransient<IBackendConfigurationTaskManagementService, BackendConfigurationTaskManagementService>();
            services.AddSingleton<IRebusService, RebusService>();
            services.AddTransient<IWordService, WordService>();
            services.AddTransient<IExcelService, ExcelService>();
            services.AddTransient<IChemicalService, ChemicalService>();
            services.AddControllers();
            SeedEForms(services);
        }

        public void AddPluginConfig(IConfigurationBuilder builder, string connectionString)
        {
            var seedData = new BackendConfigurationSeedData();
            var contextFactory = new BackendConfigurationPnContextFactory();
            builder.AddPluginConfiguration(
                connectionString,
                seedData,
                contextFactory);
        }

        public void ConfigureOptionsServices(
            IServiceCollection services,
            IConfiguration configuration)
        {
            services.ConfigurePluginDbOptions<BackendConfigurationBaseSettings>(
                configuration.GetSection("BackendConfigurationSettings"));
        }

        private static async void SeedEForms(IServiceCollection services)
        {
            var serviceProvider = services.BuildServiceProvider();

            var core = await serviceProvider.GetRequiredService<IEFormCoreService>().GetCore().ConfigureAwait(false);
            var eforms = BackendConfigurationSeedEforms.GetForms();
            var sdkDbContext = core.DbContextHelper.GetDbContext();

            var context = serviceProvider.GetRequiredService<BackendConfigurationPnDbContext>();
            var itemsPlanningContext = serviceProvider.GetRequiredService<ItemsPlanningPnDbContext>();
            // seed eforms
            var assembly = Assembly.GetExecutingAssembly();
            foreach (var (eformName, eform) in eforms)
            {
                try
                {
                    var resourceStream =
                        assembly.GetManifestResourceStream($"BackendConfiguration.Pn.Resources.eForms.{eformName}.xml");

                    if (eformName == "00. Info boks")
                    {
                        Console.WriteLine("weffw");
                    }
                    if (resourceStream == null)
                    {
                        Console.WriteLine(eformName);
                    }
                    else
                    {
                        string contents;
                        using (var sr = new StreamReader(resourceStream))
                        {
                            contents = await sr.ReadToEndAsync().ConfigureAwait(false);
                        }

                        if (eformName == "05. Halebid og risikovurdering")
                        {
                            contents = contents.Replace("SOURCE_REPLACE_ME", "123");
                        }

                        if (eformName == "01. Aflæsninger")
                        {
                            contents = contents.Replace("REPLACE_ME", "123");
                        }

                        if (eformName == "02. Fækale uheld")
                        {
                            contents = contents.Replace("REPLACE_ME", "123");
                        }

                        if (eformName == "25.01 Registrer produkter")
                        {
                            contents = contents.Replace("SOURCE_REPLACE_ME_2", "123");
                            contents = contents.Replace("SOURCE_REPLACE_ME", "456");
                        }

                        var newTemplate = await core.TemplateFromXml(contents).ConfigureAwait(false);
                        if (!await sdkDbContext.CheckLists
                                .AnyAsync(x => x.OriginalId == newTemplate.OriginalId
                                               && x.WorkflowState != Microting.eForm.Infrastructure.Constants.Constants.WorkflowStates.Removed).ConfigureAwait(false))
                        {
                            var clId = await core.TemplateCreate(newTemplate).ConfigureAwait(false);
                            var cl = await sdkDbContext.CheckLists.SingleAsync(x => x.Id == clId).ConfigureAwait(false);
                            cl.IsLocked = true;
                            cl.IsEditable = false;
                            cl.ReportH1 = eform[0];
                            cl.ReportH2 = eform[1];
                            cl.ReportH3 = eform.Count == 3 ? eform[2] : "";
                            cl.ReportH4 = eform.Count == 4 ? eform[3] : "";
                            cl.IsDoneAtEditable = true;
                            cl.QuickSyncEnabled = 1;
                            await cl.Update(sdkDbContext).ConfigureAwait(false);
                            var subCl = await sdkDbContext.CheckLists.SingleAsync(x => x.ParentId == cl.Id).ConfigureAwait(false);
                            subCl.QuickSyncEnabled = 1;
                            await subCl.Update(sdkDbContext).ConfigureAwait(false);
                        }
                        else
                        {
                            try
                            {
                                var cl = await sdkDbContext.CheckLists.SingleAsync(x =>
                                    x.OriginalId == newTemplate.OriginalId && x.ParentId == null &&
                                    x.WorkflowState != Microting.eForm.Infrastructure.Constants.Constants.WorkflowStates.Removed).ConfigureAwait(false);
                                cl.IsLocked = true;
                                cl.IsEditable = false;
                                cl.ReportH1 = eform[0];
                                cl.ReportH2 = eform[1];
                                cl.ReportH3 = eform.Count == 3 ? eform[2] : "";
                                cl.ReportH4 = eform.Count == 4 ? eform[3] : "";
                                cl.IsDoneAtEditable = true;
                                cl.QuickSyncEnabled = 1;
                                await cl.Update(sdkDbContext).ConfigureAwait(false);
                                var subCl = await sdkDbContext.CheckLists.SingleAsync(x => x.ParentId == cl.Id).ConfigureAwait(false);
                                subCl.QuickSyncEnabled = 1;
                                await subCl.Update(sdkDbContext).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine(ex.Message);
                            }
                        }
                    }
                }
                catch (Exception exception)
                {
                    Console.WriteLine(exception.Message);
                }
            }

            // Seed areas
            foreach (var newArea in BackendConfigurationSeedAreas.AreasSeed.Where(newArea =>
                         !context.Areas.Any(x => x.Id == newArea.Id)))
            {
                // create tag for area
                var planningTag = new PlanningTag
                {
                    Name = newArea.AreaTranslations.First(x => x.LanguageId == 1).Name, // danish
                };
                await planningTag.Create(itemsPlanningContext).ConfigureAwait(false);
                newArea.ItemPlanningTagId = planningTag.Id;
                await newArea.Create(context).ConfigureAwait(false);
            }

            /*
            if (context.AreaTranslations.Any(x => x.Name == "11. Pillefyr" && x.LanguageId == 1))
            {
                var planningTag = itemsPlanningContext.PlanningTags.SingleOrDefault(x => x.Name == "11. Pillefyr");
                if (planningTag != null)
                {
                    planningTag.Name = "11. Varmekilder";
                    await planningTag.Update(itemsPlanningContext);
                }

                var areaTranslation = context.AreaTranslations.Single(x => x.Name == "11. Pillefyr" && x.LanguageId == 1);
                areaTranslation.Name = "11. Varmekilder";
                await areaTranslation.Update(context);

                areaTranslation = context.AreaTranslations.Single(x => x.Name == "11. Pellet burners" && x.LanguageId == 2);
                areaTranslation.Name = "11. Heat sources";
                await areaTranslation.Update(context);

                areaTranslation = context.AreaTranslations.Single(x => x.Name == "11. Pelletbrenner" && x.LanguageId == 3);
                areaTranslation.Name = "11. Wärmequellen";
                await areaTranslation.Update(context);
            }

            if (context.AreaTranslations.Any(x => x.Name == "23. IE Reporting" && x.LanguageId == 1))
            {
                var planningTag = itemsPlanningContext.PlanningTags.SingleOrDefault(x => x.Name == "23. IE Reporting");
                if (planningTag != null)
                {
                    planningTag.Name = "23. IE-indberetning";
                    await planningTag.Update(itemsPlanningContext);
                }

                var areaTranslation = context.AreaTranslations.Single(x => x.Name == "23. IE Reporting" && x.LanguageId == 1);
                areaTranslation.Name = "23. IE-indberetning";
                await areaTranslation.Update(context);
            }*/

            // foreach (var translation in context.AreaTranslations)
            // {
            //     var translationFromSeed = BackendConfigurationSeedAreas.AreasSeed
            //         .Where(x => translation.AreaId == x.Id)
            //         .SelectMany(x => x.AreaTranslations)
            //         .FirstOrDefault(x => translation.LanguageId == x.LanguageId);
            //     var needToUpdate = false;
            //     if (translation.InfoBox != translationFromSeed.InfoBox)
            //     {
            //         translation.InfoBox = translationFromSeed.InfoBox;
            //         needToUpdate = true;
            //     }
            //
            //     if (translation.Placeholder != translationFromSeed.Placeholder)
            //     {
            //         translation.Placeholder = translationFromSeed.Placeholder;
            //         needToUpdate = true;
            //     }
            //
            //     if (translation.NewItemName != translationFromSeed.NewItemName)
            //     {
            //         translation.NewItemName = translationFromSeed.NewItemName;
            //         needToUpdate = true;
            //     }
            //
            //     if (translation.Description != translationFromSeed.Description)
            //     {
            //         translation.Description = translationFromSeed.Description;
            //         needToUpdate = true;
            //     }
            //
            //     if (translation.Name != translationFromSeed.Name)
            //     {
            //         translation.Name = translationFromSeed.Name;
            //         needToUpdate = true;
            //     }
            //
            //     if (needToUpdate)
            //     {
            //         await translation.Update(context).ConfigureAwait(false);
            //     }
            // }
            //
            // foreach (var areaInitialFieldFromDb in context.AreaInitialFields)
            // {
            //     var areaInitialFieldFromSeed = BackendConfigurationSeedAreas.AreasSeed
            //         .Where(x => areaInitialFieldFromDb.AreaId == x.Id)
            //         .Select(x => x.AreaInitialField)
            //         .FirstOrDefault(x => areaInitialFieldFromDb.Id == x.Id);
            //     if (areaInitialFieldFromSeed != null)
            //     {
            //         var needToUpdate = false;
            //         if (areaInitialFieldFromDb.ComplianceEnabled != areaInitialFieldFromSeed.ComplianceEnabled)
            //         {
            //             areaInitialFieldFromDb.ComplianceEnabled = areaInitialFieldFromSeed.ComplianceEnabled;
            //             needToUpdate = true;
            //         }
            //
            //         if (areaInitialFieldFromDb.Type != areaInitialFieldFromSeed.Type)
            //         {
            //             areaInitialFieldFromDb.Type = areaInitialFieldFromSeed.Type;
            //             needToUpdate = true;
            //         }
            //
            //         if (areaInitialFieldFromDb.Alarm != areaInitialFieldFromSeed.Alarm)
            //         {
            //             areaInitialFieldFromDb.Alarm = areaInitialFieldFromSeed.Alarm;
            //             needToUpdate = true;
            //         }
            //
            //         if (areaInitialFieldFromDb.DayOfWeek != areaInitialFieldFromSeed.DayOfWeek)
            //         {
            //             areaInitialFieldFromDb.DayOfWeek = areaInitialFieldFromSeed.DayOfWeek;
            //             needToUpdate = true;
            //         }
            //
            //         if (areaInitialFieldFromDb.RepeatEvery != areaInitialFieldFromSeed.RepeatEvery)
            //         {
            //             areaInitialFieldFromDb.RepeatEvery = areaInitialFieldFromSeed.RepeatEvery;
            //             needToUpdate = true;
            //         }
            //
            //         if (areaInitialFieldFromDb.RepeatType != areaInitialFieldFromSeed.RepeatType)
            //         {
            //             areaInitialFieldFromDb.RepeatType = areaInitialFieldFromSeed.RepeatType;
            //             needToUpdate = true;
            //         }
            //
            //         if (areaInitialFieldFromDb.EformName != areaInitialFieldFromSeed.EformName)
            //         {
            //             areaInitialFieldFromDb.EformName = areaInitialFieldFromSeed.EformName;
            //             needToUpdate = true;
            //         }
            //
            //         if (areaInitialFieldFromDb.EndDate != areaInitialFieldFromSeed.EndDate)
            //         {
            //             areaInitialFieldFromDb.EndDate = areaInitialFieldFromSeed.EndDate;
            //             needToUpdate = true;
            //         }
            //
            //         if (areaInitialFieldFromDb.Notifications != areaInitialFieldFromSeed.Notifications)
            //         {
            //             areaInitialFieldFromDb.Notifications = areaInitialFieldFromSeed.Notifications;
            //             needToUpdate = true;
            //         }
            //
            //         if (needToUpdate)
            //         {
            //             await areaInitialFieldFromDb.Update(context).ConfigureAwait(false);
            //         }
            //     }
            // }
            //
            // // Upgrade AreaRules
            // var areaRulePlannings = await context.AreaRulePlannings.Where(x => x.PropertyId == 0).ToListAsync().ConfigureAwait(false);
            //
            // foreach (var areaRulePlanning in areaRulePlannings)
            // {
            //     var areaRule = await context.AreaRules.SingleOrDefaultAsync(x => x.Id == areaRulePlanning.AreaRuleId).ConfigureAwait(false);
            //     areaRulePlanning.PropertyId = areaRule.PropertyId;
            //     areaRulePlanning.AreaId = areaRule.AreaId;
            //
            //     await areaRulePlanning.Update(context).ConfigureAwait(false);
            // }
            //
            // var areaRuleTranslations = await context.AreaRuleTranslations
            //     .Where(x => x.Name == "23.03.01 Skabelon Miljøledelse").ToListAsync().ConfigureAwait(false);
            //
            // foreach (var areaRuleTranslation in areaRuleTranslations)
            // {
            //     areaRuleTranslation.Name = "23.03.01 Miljøledelse";
            //     await areaRuleTranslation.Update(context).ConfigureAwait(false);
            // }
            //
            // areaRuleTranslations = await context.AreaRuleTranslations
            //     .Where(x => x.Name == "23.03.01 Template Environmental Management").ToListAsync().ConfigureAwait(false);
            //
            // foreach (var areaRuleTranslation in areaRuleTranslations)
            // {
            //     areaRuleTranslation.Name = "23.03.01 Environmental Management";
            //     await areaRuleTranslation.Update(context).ConfigureAwait(false);
            // }
            //
            // var clTranslations = await sdkDbContext.CheckListTranslations.Where(x =>
            //         x.Text ==
            //         "23.02.05 Siloer og materiel i transportudstyr i forbindelse med foderanlæg (Rør, snegle mv.)")
            //     .ToListAsync().ConfigureAwait(false);
            //
            // foreach (var clTranslation in clTranslations)
            // {
            //     clTranslation.Text =
            //         "23.02.05 Siloer og materiel i transportudstyr i forbindelse med foderanlæg - rør, snegle mv.";
            //     await clTranslation.Update(sdkDbContext).ConfigureAwait(false);
            // }
            //
            // clTranslations = await sdkDbContext.CheckListTranslations
            //     .Where(x => x.Text == "23.02.04 Varmekøle- og ventilationssystemer").ToListAsync().ConfigureAwait(false);
            //
            // foreach (var clTranslation in clTranslations)
            // {
            //     clTranslation.Text = "23.02.04 Varme-, køle- og ventilationssystemer";
            //     await clTranslation.Update(sdkDbContext).ConfigureAwait(false);
            // }
            //
            // clTranslations = await sdkDbContext.CheckListTranslations
            //     .Where(x => x.Text == "23.04.03 Tilsætningsstoffer i foder (Fytase eller andet)").ToListAsync().ConfigureAwait(false);
            //
            // foreach (var clTranslation in clTranslations)
            // {
            //     clTranslation.Text = "23.04.03 Tilsætningsstoffer i foder - fytase eller andet";
            //     await clTranslation.Update(sdkDbContext).ConfigureAwait(false);
            // }
            //
            // clTranslations = await sdkDbContext.CheckListTranslations.Where(x => x.Text == "23.02.01 Gyllebeholdere")
            //     .ToListAsync().ConfigureAwait(false);
            //
            // foreach (var clTranslation in clTranslations)
            // {
            //     clTranslation.Text = "23.02.01 Årlig visuel kontrol af gyllebeholdere";
            //     await clTranslation.Update(sdkDbContext).ConfigureAwait(false);
            // }
            //
            // clTranslations = await sdkDbContext.CheckListTranslations
            //     .Where(x => x.Text == "23.01.01 Fast overdækning af gyllebeholder").ToListAsync().ConfigureAwait(false);
            //
            // foreach (var clTranslation in clTranslations)
            // {
            //     clTranslation.Text = "23.01.01 Fast overdækning gyllebeholder";
            //     await clTranslation.Update(sdkDbContext).ConfigureAwait(false);
            // }
            //
            // clTranslations = await sdkDbContext.CheckListTranslations
            //     .Where(x => x.Text == "23.03.01 Skabelon Miljøledelse").ToListAsync().ConfigureAwait(false);
            //
            // foreach (var clTranslation in clTranslations)
            // {
            //     clTranslation.Text = "23.03.01 Miljøledelse";
            //     await clTranslation.Update(sdkDbContext).ConfigureAwait(false);
            // }
            //
            // clTranslations = await sdkDbContext.CheckListTranslations.Where(x => x.Text == "01. Miljøledelse skabelon")
            //     .ToListAsync().ConfigureAwait(false);
            //
            // foreach (var clTranslation in clTranslations)
            // {
            //     clTranslation.Text = "01. Miljøledelse";
            //     await clTranslation.Update(sdkDbContext).ConfigureAwait(false);
            // }
            //
            // clTranslations = await sdkDbContext.CheckListTranslations.Where(x => x.Text == "17. Håndildslukkere")
            //     .ToListAsync().ConfigureAwait(false);
            //
            // foreach (var clTranslation in clTranslations)
            // {
            //     clTranslation.Text = "17. Brandslukkere";
            //     await clTranslation.Update(sdkDbContext).ConfigureAwait(false);
            // }
            //
            // // Removing the old info fields for eForm 15,16,17
            // var fieldOriginalIds = new List<string>
            // {
            //     "375221",
            //     "375220",
            //     "375208",
            //     "375209",
            //     "375236",
            //     "375237"
            // };
            //
            // var fields = await sdkDbContext.Fields.Where(x =>
            //         fieldOriginalIds.Contains(x.OriginalId) && x.WorkflowState != Microting.eForm.Infrastructure.Constants.Constants.WorkflowStates.Removed)
            //     .ToListAsync().ConfigureAwait(false);
            //
            // foreach (var field in fields)
            // {
            //     await field.Delete(sdkDbContext).ConfigureAwait(false);
            // }
            //
            // // Removing the old info fields for eForm 15,16,17
            // fieldOriginalIds = new List<string>
            // {
            //     "372091",
            //     "372092",
            //     "372093",
            //     "372094",
            //     "372095",
            //     "372096",
            //     "372097",
            //     "372098",
            //     "372099",
            //     "372100",
            //     "372101",
            //     "372102",
            //     "372103",
            //     "372104",
            //     "372105",
            //     "372106",
            //     "372107",
            //     "372108",
            //     "372109",
            //     "372110",
            //     "372112"
            // };
            //
            // fields = await sdkDbContext.Fields.Where(x =>
            //         fieldOriginalIds.Contains(x.OriginalId) && x.WorkflowState != Microting.eForm.Infrastructure.Constants.Constants.WorkflowStates.Removed)
            //     .ToListAsync().ConfigureAwait(false);
            //
            // foreach (var field in fields)
            // {
            //     field.Mandatory = 1;
            //     await field.Update(sdkDbContext).ConfigureAwait(false);
            // }
            //
            // var areaTranslation2 =
            //     await context.AreaTranslations.SingleOrDefaultAsync(x => x.Name == "17. Håndildslukkere").ConfigureAwait(false);
            // if (areaTranslation2 != null)
            // {
            //     areaTranslation2.Name = "17. Brandslukkere";
            //     await areaTranslation2.Update(context).ConfigureAwait(false);
            //     // var area = await context.Areas.SingleOrDefaultAsync(x => x.Id == areaTranslation2.AreaId);
            //
            //     var folderTranslations = await sdkDbContext.FolderTranslations
            //         .Where(x => x.Name == "17. Håndildslukkere").ToListAsync().ConfigureAwait(false);
            //
            //     foreach (var folderTranslation2 in folderTranslations)
            //     {
            //         var folder = await sdkDbContext.Folders.SingleAsync(x => x.Id == folderTranslation2.FolderId).ConfigureAwait(false);
            //         var folderTranslationList = new List<CommonTranslationsModel>();
            //         var folderTranslation = new CommonTranslationsModel()
            //         {
            //             Description = "",
            //             LanguageId = 1,
            //             Name = "17. Brandslukkere",
            //         };
            //         folderTranslationList.Add(folderTranslation);
            //
            //         await core.FolderUpdate(folderTranslation2.FolderId, folderTranslationList, folder.ParentId).ConfigureAwait(false);
            //     }
            //
            //     var areaRules = await context.AreaRules.Where(x => x.AreaId == areaTranslation2.AreaId).ToListAsync().ConfigureAwait(false);
            //
            //     var eFormId = sdkDbContext.CheckListTranslations.FirstOrDefault(x => x.Text == "17. Håndildslukkere")
            //         ?.CheckListId;
            //
            //     foreach (var areaRule in areaRules)
            //     {
            //         areaRule.EformId = eFormId;
            //         areaRule.EformName = "17. Brandslukkere";
            //         await areaRule.Update(context).ConfigureAwait(false);
            //     }
            // }
            //
            // areaTranslation2 =
            //     await context.AreaTranslations.SingleOrDefaultAsync(x => x.Name == "23. IE-indberetning").ConfigureAwait(false);
            // if (areaTranslation2 != null)
            // {
            //     areaTranslation2.Name = "23. IE-indberetning (Gammel)";
            //     await areaTranslation2.Update(context).ConfigureAwait(false);
            //     // var area = await context.Areas.SingleOrDefaultAsync(x => x.Id == areaTranslation2.AreaId);
            //
            //     var folderTranslations = await sdkDbContext.FolderTranslations
            //         .Where(x => x.Name == "23. IE-indberetning").ToListAsync().ConfigureAwait(false);
            //
            //     foreach (var folderTranslation2 in folderTranslations)
            //     {
            //         var folder = await sdkDbContext.Folders.SingleAsync(x => x.Id == folderTranslation2.FolderId).ConfigureAwait(false);
            //         var folderTranslationList = new List<CommonTranslationsModel>();
            //         var folderTranslation = new CommonTranslationsModel()
            //         {
            //             Description = "",
            //             LanguageId = 1,
            //             Name = "23. IE-indberetning (Gammel)",
            //         };
            //         folderTranslationList.Add(folderTranslation);
            //
            //         await core.FolderUpdate(folderTranslation2.FolderId, folderTranslationList, folder.ParentId).ConfigureAwait(false);
            //     }
            // }
            //
            // List<KeyValuePair<string, string>> tags = new List<KeyValuePair<string, string>>();
            // tags.Add(new KeyValuePair<string, string>("100. Diverse", "99. Diverse"));
            // tags.Add(new KeyValuePair<string, string>("01. Registreringer til Miljøledelse",
            //     "01. Fokusområder Miljøledelse"));
            // tags.Add(new KeyValuePair<string, string>("04. Fodringskrav (kun IE-husdyrbrug)",
            //     "04. Foderindlægssedler"));
            // tags.Add(new KeyValuePair<string, string>("05. Klargøring af stalde og dokumentation af halebid",
            //     "05. Stalde: Halebid og klargøring"));
            // tags.Add(new KeyValuePair<string, string>("13. Arbejdstilsynets Landbrugs APV", "13. APV Landbrug"));
            // tags.Add(new KeyValuePair<string, string>("20. Tilbagevendende opgaver (man-søn)",
            //     "20. Ugentlige rutineopgaver"));
            // tags.Add(new KeyValuePair<string, string>("23. IE-indberetning", "23. IE-indberetning (Gammel)"));
            //
            // foreach (var keyValuePair in tags)
            // {
            //     var theTag = itemsPlanningContext.PlanningTags.SingleOrDefault(x => x.Name == keyValuePair.Key);
            //     {
            //         if (theTag != null)
            //         {
            //             theTag.Name = keyValuePair.Value;
            //             await theTag.Update(itemsPlanningContext).ConfigureAwait(false);
            //         }
            //     }
            // }
            //
            // areaTranslation2 = await context.AreaTranslations.SingleOrDefaultAsync(x => x.Name == "100. Diverse").ConfigureAwait(false);
            // if (areaTranslation2 != null)
            // {
            //     areaTranslation2.Name = "99. Diverse";
            //     await areaTranslation2.Update(context).ConfigureAwait(false);
            //
            //     var folderTranslations =
            //         await sdkDbContext.FolderTranslations.Where(x => x.Name == "100. Diverse").ToListAsync().ConfigureAwait(false);
            //
            //     foreach (var folderTranslation2 in folderTranslations)
            //     {
            //         var folder = await sdkDbContext.Folders.SingleAsync(x => x.Id == folderTranslation2.FolderId).ConfigureAwait(false);
            //         var folderTranslationList = new List<CommonTranslationsModel>();
            //         var folderTranslation = new CommonTranslationsModel()
            //         {
            //             Description = "",
            //             LanguageId = 1,
            //             Name = "99. Diverse",
            //         };
            //         folderTranslationList.Add(folderTranslation);
            //
            //         await core.FolderUpdate(folderTranslation2.FolderId, folderTranslationList, folder.ParentId).ConfigureAwait(false);
            //     }
            //
            //     var areaRules = await context.AreaRules.Where(x => x.AreaId == areaTranslation2.AreaId).ToListAsync().ConfigureAwait(false);
            //
            //     var eFormId = sdkDbContext.CheckListTranslations.FirstOrDefault(x => x.Text == "100. Diverse")
            //         ?.CheckListId;
            //
            //     foreach (var areaRule in areaRules)
            //     {
            //         areaRule.EformId = eFormId;
            //         areaRule.EformName = "99. Diverse";
            //         await areaRule.Update(context).ConfigureAwait(false);
            //     }
            // }
            //
            // areaTranslation2 = await context.AreaTranslations.SingleOrDefaultAsync(x => x.Name == "100. Miscellaneous").ConfigureAwait(false);
            // if (areaTranslation2 != null)
            // {
            //     areaTranslation2.Name = "99. Miscellaneous";
            //     await areaTranslation2.Update(context).ConfigureAwait(false);
            //
            //     var folderTranslations = await sdkDbContext.FolderTranslations
            //         .Where(x => x.Name == "100. Miscellaneous").ToListAsync().ConfigureAwait(false);
            //
            //     foreach (var folderTranslation2 in folderTranslations)
            //     {
            //         var folder = await sdkDbContext.Folders.SingleAsync(x => x.Id == folderTranslation2.FolderId).ConfigureAwait(false);
            //         var folderTranslationList = new List<CommonTranslationsModel>();
            //         var folderTranslation = new CommonTranslationsModel()
            //         {
            //             Description = "",
            //             LanguageId = 2,
            //             Name = "99. Miscellaneous",
            //         };
            //         folderTranslationList.Add(folderTranslation);
            //
            //         await core.FolderUpdate(folderTranslation2.FolderId, folderTranslationList, folder.ParentId).ConfigureAwait(false);
            //     }
            //
            //     var areaRules = await context.AreaRules.Where(x => x.AreaId == areaTranslation2.AreaId).ToListAsync().ConfigureAwait(false);
            //
            //     var eFormId = sdkDbContext.CheckListTranslations.FirstOrDefault(x => x.Text == "100. Miscellaneous")
            //         ?.CheckListId;
            //
            //     foreach (var areaRule in areaRules)
            //     {
            //         areaRule.EformId = eFormId;
            //         areaRule.EformName = "99. Miscellaneous";
            //         await areaRule.Update(context).ConfigureAwait(false);
            //     }
            // }
            //
            // areaTranslation2 =
            //     await context.AreaTranslations.SingleOrDefaultAsync(x => x.Name == "05. Stalde: Halebid og klargøring").ConfigureAwait(false);
            // var area = await context.Areas.SingleAsync(x => x.Id == areaTranslation2.AreaId).ConfigureAwait(false);
            // area.Type = AreaTypesEnum.Type3;
            // await area.Update(context).ConfigureAwait(false);
            //
            // var areaTranslations = await context.AreaTranslations.Where(x => x.Name.Contains("23.")).ToListAsync().ConfigureAwait(false);
            // foreach (var areaTranslation in areaTranslations)
            // {
            //     areaTranslation.Description = "https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.8kzwebwrj4gz";
            //     await areaTranslation.Update(context).ConfigureAwait(false);
            // }
            //
            // var ftList = await sdkDbContext.FolderTranslations.Where(x => x.Name == "23.00 Aflæsninger miljøledelse")
            //     .ToListAsync().ConfigureAwait(false);
            // foreach (var folderTranslation2 in ftList)
            // {
            //     var folder = await sdkDbContext.Folders.SingleAsync(x => x.Id == folderTranslation2.FolderId).ConfigureAwait(false);
            //     var folderTranslationList = new List<CommonTranslationsModel>();
            //     var folderTranslation = new CommonTranslationsModel()
            //     {
            //         Description = "",
            //         LanguageId = 1,
            //         Name = "23.00 Aflæsninger",
            //     };
            //     folderTranslationList.Add(folderTranslation);
            //
            //     await core.FolderUpdate(folderTranslation2.FolderId, folderTranslationList, folder.ParentId).ConfigureAwait(false);
            // }
            //
            // ftList = await sdkDbContext.FolderTranslations.Where(x =>
            //     x.Name == "23.01 Logbøger for alle miljøteknologier" &&
            //     x.WorkflowState != Microting.eForm.Infrastructure.Constants.Constants.WorkflowStates.Removed).ToListAsync().ConfigureAwait(false);
            // foreach (var folderTranslation2 in ftList)
            // {
            //     var folder = await sdkDbContext.Folders.SingleAsync(x => x.Id == folderTranslation2.FolderId).ConfigureAwait(false);
            //     var folderTranslationList = new List<CommonTranslationsModel>()
            //     {
            //         new CommonTranslationsModel()
            //         {
            //             Description = "",
            //             LanguageId = 1,
            //             Name = "23.01 Logbøger miljøteknologier",
            //         }
            //     };
            //
            //     await core.FolderUpdate(folderTranslation2.FolderId, folderTranslationList, folder.ParentId).ConfigureAwait(false);
            // }
            //
            // ftList = await sdkDbContext.FolderTranslations.Where(x =>
            //     x.Name == "23.02 Dokumentation af afsluttede inspektioner" &&
            //     x.WorkflowState != Microting.eForm.Infrastructure.Constants.Constants.WorkflowStates.Removed).ToListAsync().ConfigureAwait(false);
            // foreach (var folderTranslation2 in ftList)
            // {
            //     var folder = await sdkDbContext.Folders.SingleAsync(x => x.Id == folderTranslation2.FolderId).ConfigureAwait(false);
            //     var folderTranslationList = new List<CommonTranslationsModel>()
            //     {
            //         new CommonTranslationsModel()
            //         {
            //             Description = "",
            //             LanguageId = 1,
            //             Name = "23.02 Dokumentation afsluttede inspektioner",
            //         }
            //     };
            //
            //     await core.FolderUpdate(folderTranslation2.FolderId, folderTranslationList, folder.ParentId).ConfigureAwait(false);
            // }
            //
            // ftList = await sdkDbContext.FolderTranslations.Where(x =>
            //         x.Name == "23.03 Dokumentation for miljøledelse" &&
            //         x.WorkflowState != Microting.eForm.Infrastructure.Constants.Constants.WorkflowStates.Removed)
            //     .ToListAsync().ConfigureAwait(false);
            // foreach (var folderTranslation2 in ftList)
            // {
            //     var folder = await sdkDbContext.Folders.SingleAsync(x => x.Id == folderTranslation2.FolderId).ConfigureAwait(false);
            //     var folderTranslationList = new List<CommonTranslationsModel>();
            //     var folderTranslation = new CommonTranslationsModel()
            //     {
            //         Description = "",
            //         LanguageId = 1,
            //         Name = "23.03 Dokumentation Miljøledelse",
            //     };
            //     folderTranslationList.Add(folderTranslation);
            //
            //     await core.FolderUpdate(folderTranslation2.FolderId, folderTranslationList, folder.ParentId).ConfigureAwait(false);
            // }
            //
            // ftList = await sdkDbContext.FolderTranslations.Where(x =>
            //         x.Name == "23.04 Overholdelse af fodringskrav" &&
            //         x.WorkflowState != Microting.eForm.Infrastructure.Constants.Constants.WorkflowStates.Removed)
            //     .ToListAsync().ConfigureAwait(false);
            // foreach (var folderTranslation2 in ftList)
            // {
            //     var folder = await sdkDbContext.Folders.SingleAsync(x => x.Id == folderTranslation2.FolderId).ConfigureAwait(false);
            //     var folderTranslationList = new List<CommonTranslationsModel>();
            //     var folderTranslation = new CommonTranslationsModel()
            //     {
            //         Description = "",
            //         LanguageId = 1,
            //         Name = "23.04 Overholdelse fodringskrav",
            //     };
            //     folderTranslationList.Add(folderTranslation);
            //
            //     await core.FolderUpdate(folderTranslation2.FolderId, folderTranslationList, folder.ParentId).ConfigureAwait(false);
            // }
            //
            // ftList = await sdkDbContext.FolderTranslations
            //     .Where(x => x.Name == "Mandag" && x.WorkflowState != Microting.eForm.Infrastructure.Constants.Constants.WorkflowStates.Removed).ToListAsync().ConfigureAwait(false);
            // foreach (var folderTranslation2 in ftList)
            // {
            //     var folder = await sdkDbContext.Folders.SingleAsync(x => x.Id == folderTranslation2.FolderId).ConfigureAwait(false);
            //     var folderTranslationList = new List<CommonTranslationsModel>();
            //     var folderTranslation = new CommonTranslationsModel
            //     {
            //         Description = "",
            //         LanguageId = 1,
            //         Name = "20.01 Mandag"
            //     };
            //     folderTranslationList.Add(folderTranslation);
            //     folderTranslation = new CommonTranslationsModel
            //     {
            //         Description = "",
            //         LanguageId = 2,
            //         Name = "20.01 Monday"
            //     };
            //     folderTranslationList.Add(folderTranslation);
            //     folderTranslation = new CommonTranslationsModel
            //     {
            //         Description = "",
            //         LanguageId = 3,
            //         Name = "20.01 Montag"
            //     };
            //     folderTranslationList.Add(folderTranslation);
            //
            //     await core.FolderUpdate(folderTranslation2.FolderId, folderTranslationList, folder.ParentId).ConfigureAwait(false);
            // }
            //
            // ftList = await sdkDbContext.FolderTranslations
            //     .Where(x => x.Name == "Tirsdag" && x.WorkflowState != Microting.eForm.Infrastructure.Constants.Constants.WorkflowStates.Removed).ToListAsync().ConfigureAwait(false);
            // foreach (var folderTranslation2 in ftList)
            // {
            //     var folder = await sdkDbContext.Folders.SingleAsync(x => x.Id == folderTranslation2.FolderId).ConfigureAwait(false);
            //     var folderTranslationList = new List<CommonTranslationsModel>();
            //     var folderTranslation = new CommonTranslationsModel
            //     {
            //         Description = "",
            //         LanguageId = 1,
            //         Name = "20.02 Tirsdag"
            //     };
            //     folderTranslationList.Add(folderTranslation);
            //     folderTranslation = new CommonTranslationsModel
            //     {
            //         Description = "",
            //         LanguageId = 2,
            //         Name = "20.02 Tuesday"
            //     };
            //     folderTranslationList.Add(folderTranslation);
            //     folderTranslation = new CommonTranslationsModel
            //     {
            //         Description = "",
            //         LanguageId = 3,
            //         Name = "20.02 Diwstag"
            //     };
            //     folderTranslationList.Add(folderTranslation);
            //
            //     await core.FolderUpdate(folderTranslation2.FolderId, folderTranslationList, folder.ParentId).ConfigureAwait(false);
            // }
            //
            // ftList = await sdkDbContext.FolderTranslations
            //     .Where(x => x.Name == "Onsdag" && x.WorkflowState != Microting.eForm.Infrastructure.Constants.Constants.WorkflowStates.Removed).ToListAsync().ConfigureAwait(false);
            // foreach (var folderTranslation2 in ftList)
            // {
            //     var folder = await sdkDbContext.Folders.SingleAsync(x => x.Id == folderTranslation2.FolderId).ConfigureAwait(false);
            //     var folderTranslationList = new List<CommonTranslationsModel>();
            //     var folderTranslation = new CommonTranslationsModel
            //     {
            //         Description = "",
            //         LanguageId = 1,
            //         Name = "20.03 Onsdag"
            //     };
            //     folderTranslationList.Add(folderTranslation);
            //     folderTranslation = new CommonTranslationsModel
            //     {
            //         Description = "",
            //         LanguageId = 2,
            //         Name = "20.03 Wednesday"
            //     };
            //     folderTranslationList.Add(folderTranslation);
            //     folderTranslation = new CommonTranslationsModel
            //     {
            //         Description = "",
            //         LanguageId = 3,
            //         Name = "20.03 Mittwoch"
            //     };
            //     folderTranslationList.Add(folderTranslation);
            //
            //     await core.FolderUpdate(folderTranslation2.FolderId, folderTranslationList, folder.ParentId).ConfigureAwait(false);
            // }
            //
            // ftList = await sdkDbContext.FolderTranslations
            //     .Where(x => x.Name == "Torsdag" && x.WorkflowState != Microting.eForm.Infrastructure.Constants.Constants.WorkflowStates.Removed).ToListAsync().ConfigureAwait(false);
            // foreach (var folderTranslation2 in ftList)
            // {
            //     var folder = await sdkDbContext.Folders.SingleAsync(x => x.Id == folderTranslation2.FolderId).ConfigureAwait(false);
            //     var folderTranslationList = new List<CommonTranslationsModel>();
            //     var folderTranslation = new CommonTranslationsModel
            //     {
            //         Description = "",
            //         LanguageId = 1,
            //         Name = "20.04 Torsdag"
            //     };
            //     folderTranslationList.Add(folderTranslation);
            //     folderTranslation = new CommonTranslationsModel
            //     {
            //         Description = "",
            //         LanguageId = 2,
            //         Name = "20.04 Thursday"
            //     };
            //     folderTranslationList.Add(folderTranslation);
            //     folderTranslation = new CommonTranslationsModel
            //     {
            //         Description = "",
            //         LanguageId = 3,
            //         Name = "20.04 Donnerstag"
            //     };
            //     folderTranslationList.Add(folderTranslation);
            //
            //     await core.FolderUpdate(folderTranslation2.FolderId, folderTranslationList, folder.ParentId).ConfigureAwait(false);
            // }
            //
            // ftList = await sdkDbContext.FolderTranslations
            //     .Where(x => x.Name == "Fredag" && x.WorkflowState != Microting.eForm.Infrastructure.Constants.Constants.WorkflowStates.Removed).ToListAsync().ConfigureAwait(false);
            // foreach (var folderTranslation2 in ftList)
            // {
            //     var folder = await sdkDbContext.Folders.SingleAsync(x => x.Id == folderTranslation2.FolderId).ConfigureAwait(false);
            //     var folderTranslationList = new List<CommonTranslationsModel>();
            //     var folderTranslation = new CommonTranslationsModel
            //     {
            //         Description = "",
            //         LanguageId = 1,
            //         Name = "20.05 Fredag"
            //     };
            //     folderTranslationList.Add(folderTranslation);
            //     folderTranslation = new CommonTranslationsModel
            //     {
            //         Description = "",
            //         LanguageId = 2,
            //         Name = "20.05 Friday"
            //     };
            //     folderTranslationList.Add(folderTranslation);
            //     folderTranslation = new CommonTranslationsModel
            //     {
            //         Description = "",
            //         LanguageId = 3,
            //         Name = "20.05 Freitag"
            //     };
            //     folderTranslationList.Add(folderTranslation);
            //
            //     await core.FolderUpdate(folderTranslation2.FolderId, folderTranslationList, folder.ParentId).ConfigureAwait(false);
            // }
            //
            // ftList = await sdkDbContext.FolderTranslations
            //     .Where(x => x.Name == "Lørdag" && x.WorkflowState != Microting.eForm.Infrastructure.Constants.Constants.WorkflowStates.Removed).ToListAsync().ConfigureAwait(false);
            // foreach (var folderTranslation2 in ftList)
            // {
            //     var folder = await sdkDbContext.Folders.SingleAsync(x => x.Id == folderTranslation2.FolderId).ConfigureAwait(false);
            //     var folderTranslationList = new List<CommonTranslationsModel>();
            //     var folderTranslation = new CommonTranslationsModel
            //     {
            //         Description = "",
            //         LanguageId = 1,
            //         Name = "20.06 Lørdag"
            //     };
            //     folderTranslationList.Add(folderTranslation);
            //     folderTranslation = new CommonTranslationsModel
            //     {
            //         Description = "",
            //         LanguageId = 2,
            //         Name = "20.06 Saturday"
            //     };
            //     folderTranslationList.Add(folderTranslation);
            //     folderTranslation = new CommonTranslationsModel
            //     {
            //         Description = "",
            //         LanguageId = 3,
            //         Name = "20.06 Samstag"
            //     };
            //     folderTranslationList.Add(folderTranslation);
            //
            //     await core.FolderUpdate(folderTranslation2.FolderId, folderTranslationList, folder.ParentId).ConfigureAwait(false);
            // }
            //
            // ftList = await sdkDbContext.FolderTranslations
            //     .Where(x => x.Name == "Søndag" && x.WorkflowState != Microting.eForm.Infrastructure.Constants.Constants.WorkflowStates.Removed).ToListAsync().ConfigureAwait(false);
            // foreach (var folderTranslation2 in ftList)
            // {
            //     var folder = await sdkDbContext.Folders.SingleAsync(x => x.Id == folderTranslation2.FolderId).ConfigureAwait(false);
            //     var folderTranslationList = new List<CommonTranslationsModel>();
            //     var folderTranslation = new CommonTranslationsModel
            //     {
            //         Description = "",
            //         LanguageId = 1,
            //         Name = "20.07 Søndag"
            //     };
            //     folderTranslationList.Add(folderTranslation);
            //     folderTranslation = new CommonTranslationsModel
            //     {
            //         Description = "",
            //         LanguageId = 2,
            //         Name = "20.07 Sunday"
            //     };
            //     folderTranslationList.Add(folderTranslation);
            //     folderTranslation = new CommonTranslationsModel
            //     {
            //         Description = "",
            //         LanguageId = 3,
            //         Name = "20.07 Sonntag"
            //     };
            //     folderTranslationList.Add(folderTranslation);
            //
            //     await core.FolderUpdate(folderTranslation2.FolderId, folderTranslationList, folder.ParentId).ConfigureAwait(false);
            // }
            //
            // var dbField = await sdkDbContext.Fields.SingleAsync(x => x.OriginalId == "375734").ConfigureAwait(false);
            // var dbFieldOptions = await sdkDbContext.FieldOptions.Where(x => x.FieldId == dbField.Id).ToListAsync().ConfigureAwait(false);
            // foreach (var dbFieldOption in dbFieldOptions)
            // {
            //     var dbFieldOptionTranslation =
            //         await sdkDbContext.FieldOptionTranslations.SingleOrDefaultAsync(x =>
            //             x.Text == "Færdig" && x.FieldOptionId == dbFieldOption.Id).ConfigureAwait(false);
            //     if (dbFieldOptionTranslation != null)
            //     {
            //         dbFieldOptionTranslation.Text = "Afsluttet";
            //         await dbFieldOptionTranslation.Update(sdkDbContext).ConfigureAwait(false);
            //     }
            // }
            //
            // dbField = await sdkDbContext.Fields.SingleAsync(x => x.OriginalId == "371900").ConfigureAwait(false);
            // var theDbFieldOption =
            //     await sdkDbContext.FieldOptions.SingleOrDefaultAsync(x => x.Key == "0" && x.FieldId == dbField.Id).ConfigureAwait(false);
            // if (theDbFieldOption == null)
            // {
            //     theDbFieldOption = new FieldOption()
            //     {
            //         Key = "0",
            //         DisplayOrder = "0",
            //         Selected = false,
            //         FieldId = dbField.Id
            //     };
            //     await theDbFieldOption.Create(sdkDbContext).ConfigureAwait(false);
            //
            //     var dbFieldOptionTranslation = new FieldOptionTranslation()
            //     {
            //         LanguageId = 1,
            //         Text = " - ",
            //         FieldOptionId = theDbFieldOption.Id
            //     };
            //     await dbFieldOptionTranslation.Create(sdkDbContext).ConfigureAwait(false);
            //
            //     dbFieldOptionTranslation = new FieldOptionTranslation()
            //     {
            //         LanguageId = 2,
            //         Text = " - ",
            //         FieldOptionId = theDbFieldOption.Id
            //     };
            //     await dbFieldOptionTranslation.Create(sdkDbContext).ConfigureAwait(false);
            //
            //     dbFieldOptionTranslation = new FieldOptionTranslation()
            //     {
            //         LanguageId = 3,
            //         Text = " - ",
            //         FieldOptionId = theDbFieldOption.Id
            //     };
            //     await dbFieldOptionTranslation.Create(sdkDbContext).ConfigureAwait(false);
            // }
            //
            // var tag =
            //     await itemsPlanningContext.PlanningTags.SingleOrDefaultAsync(x => x.Name == "17. Håndildslukkere").ConfigureAwait(false);
            // if (tag != null)
            // {
            //     tag.Name = "17. Brandslukkere";
            //     await tag.Update(itemsPlanningContext).ConfigureAwait(false);
            // }
            //
            // foreach (var planningSite in context.PlanningSites.Where(x => x.AreaId == null).ToList())
            // {
            //     var areaRulePlanning = await context.AreaRulePlannings.SingleAsync(x => x.Id == planningSite.AreaRulePlanningsId).ConfigureAwait(false);
            //     planningSite.AreaRuleId = areaRulePlanning.AreaRuleId;
            //     planningSite.AreaId = areaRulePlanning.AreaId;
            //     await planningSite.Update(context).ConfigureAwait(false);
            // }

            var cltranslation = await sdkDbContext.CheckListTranslations.FirstAsync(x => x.Text == "25.01 Registrer produkter");
            var clCheckList = await sdkDbContext.CheckLists.FirstAsync(x => x.ParentId == cltranslation.CheckListId);
            
            if (!sdkDbContext.Fields.Any(x => x.OriginalId == "376999"))
            {
                var field = new Microting.eForm.Infrastructure.Data.Entities.Field
                {
                    CheckListId = clCheckList.Id,
                    Color = Microting.eForm.Infrastructure.Constants.Constants.FieldColors.Yellow,
                    BarcodeEnabled = 0,
                    BarcodeType = "",
                    DisplayIndex = 0,
                    FieldType = await sdkDbContext.FieldTypes.FirstAsync(x => x.Type == "EntitySelect"),
                    EntityGroupId = 12345,
                    Mandatory = 1,
                    ReadOnly = 0,
                    Dummy = 0,
                    OriginalId = "376999",
                    Translations = new List<FieldTranslation>
                    {
                        new()
                        {
                            LanguageId = 1,
                            Text = "Vælg lokation",
                            Description = ""
                        },
                        new()
                        {
                            LanguageId = 2,
                            Text = "Select location",
                            Description = ""
                        },
                        new()
                        {
                            LanguageId = 3,
                            Text = "Ort auswählen",
                            Description = ""
                        }
                    }
                };
                await field.Create(sdkDbContext);
            }
        }

        public void ConfigureDbContext(IServiceCollection services, string connectionString)
        {
            var itemsPlannigConnectionString = connectionString.Replace(
                "eform-backend-configuration-plugin",
                "eform-angular-items-planning-plugin");
            var timeRegistrationConnectionString = connectionString.Replace(
                "eform-backend-configuration-plugin",
                "eform-angular-time-planning-plugin");
            var chemicalBaseConnectionString = connectionString.Replace(
                "eform-backend-configuration-plugin",
                "chemical-base-plugin");

            _connectionString = connectionString;
            services.AddDbContext<BackendConfigurationPnDbContext>(o =>
                o.UseMySql(connectionString, new MariaDbServerVersion(
                    new Version(10, 4, 0)), mySqlOptionsAction: builder =>
                {
                    builder.EnableRetryOnFailure();
                    builder.MigrationsAssembly(PluginAssembly().FullName);
                }));

            services.AddDbContext<ItemsPlanningPnDbContext>(o =>
                o.UseMySql(itemsPlannigConnectionString, new MariaDbServerVersion(
                    new Version(10, 4, 0)), mySqlOptionsAction: builder =>
                {
                    builder.EnableRetryOnFailure();
                    builder.MigrationsAssembly(PluginAssembly().FullName);
                }));

            services.AddDbContext<TimePlanningPnDbContext>(o =>
                o.UseMySql(timeRegistrationConnectionString, new MariaDbServerVersion(
                    new Version(10, 4, 0)), mySqlOptionsAction: builder =>
                {
                    builder.EnableRetryOnFailure();
                    builder.MigrationsAssembly(PluginAssembly().FullName);
                }));

            services.AddDbContext<ChemicalsDbContext>(o =>
                o.UseMySql(chemicalBaseConnectionString, new MariaDbServerVersion(
                    new Version(10, 4, 0)), mySqlOptionsAction: builder =>
                {
                    builder.EnableRetryOnFailure();
                    builder.MigrationsAssembly(PluginAssembly().FullName);
                }));

            var chemicalsContextFactory = new ChemicalsContextFactory();
            var chemicalsDbContext = chemicalsContextFactory.CreateDbContext(new[] { chemicalBaseConnectionString });
            chemicalsDbContext.Database.Migrate();

            var contextFactory = new BackendConfigurationPnContextFactory();
            var context = contextFactory.CreateDbContext(new[] { connectionString });
            context.Database.Migrate();

            // Seed database
            SeedDatabase(connectionString);

            //SeedEForms(services, connectionString);
        }

        public void Configure(IApplicationBuilder appBuilder)
        {
            var serviceProvider = appBuilder.ApplicationServices;

            var rabbitMqHost = "localhost";

            if (_connectionString.Contains("frontend"))
            {
                var dbPrefix = Regex.Match(_connectionString, @"atabase=(\d*)_").Groups[1].Value;
                rabbitMqHost = $"frontend-{dbPrefix}-rabbitmq";
            }

            var rebusService = serviceProvider.GetService<IRebusService>();
            rebusService?.Start(_connectionString, "admin", "password", rabbitMqHost);

            //_bus = rebusService.GetBus();
        }

        public List<PluginMenuItemModel> GetNavigationMenu(IServiceProvider serviceProvider)
        {
            var pluginMenu = new List<PluginMenuItemModel>
            {
                new()
                {
                    Name = "Dropdown",
                    E2EId = "backend-configuration-pn",
                    Link = "",
                    Type = MenuItemTypeEnum.Dropdown,
                    Position = 0,
                    Translations = new List<PluginMenuTranslationModel>
                    {
                        new()
                        {
                            LocaleName = LocaleNames.English,
                            Name = "Backend Configuration",
                            Language = LanguageNames.English,
                        },
                        new()
                        {
                            LocaleName = LocaleNames.German,
                            Name = "Aufbau",
                            Language = LanguageNames.German,
                        },
                        new()
                        {
                            LocaleName = LocaleNames.Danish,
                            Name = "Konfiguration",
                            Language = LanguageNames.Danish,
                        },
                        new()
                        {
                            LocaleName = LocaleNames.Ukrainian,
                            Name = "Конфігурація серверної частини",
                            Language = LanguageNames.Ukrainian,
                        }
                    },
                    ChildItems = new List<PluginMenuItemModel>
                    {
                        new()
                        {
                            Name = "Properties",
                            E2EId = "backend-configuration-pn-properties",
                            Link = "/plugins/backend-configuration-pn/properties",
                            Type = MenuItemTypeEnum.Link,
                            Position = 0,
                            MenuTemplate = new PluginMenuTemplateModel
                            {
                                Name = "Properties",
                                E2EId = "backend-configuration-pn-properties",
                                DefaultLink = "/plugins/backend-configuration-pn/properties",
                                Permissions = new List<PluginMenuTemplatePermissionModel>(),
                                Translations = new List<PluginMenuTranslationModel>
                                {
                                    new()
                                    {
                                        LocaleName = LocaleNames.English,
                                        Name = "Properties",
                                        Language = LanguageNames.English,
                                    },
                                    new()
                                    {
                                        LocaleName = LocaleNames.German,
                                        Name = "Eigenschaften",
                                        Language = LanguageNames.German,
                                    },
                                    new()
                                    {
                                        LocaleName = LocaleNames.Danish,
                                        Name = "Ejendomme",
                                        Language = LanguageNames.Danish,
                                    },
                                    new()
                                    {
                                        LocaleName = LocaleNames.Ukrainian,
                                        Name = "Властивості",
                                        Language = LanguageNames.Ukrainian,
                                    },
                                }
                            },
                            Translations = new List<PluginMenuTranslationModel>
                            {
                                new()
                                {
                                    LocaleName = LocaleNames.English,
                                    Name = "Properties",
                                    Language = LanguageNames.English,
                                },
                                new()
                                {
                                    LocaleName = LocaleNames.German,
                                    Name = "Eigenschaften",
                                    Language = LanguageNames.German,
                                },
                                new()
                                {
                                    LocaleName = LocaleNames.Danish,
                                    Name = "Ejendomme",
                                    Language = LanguageNames.Danish,
                                },
                                new()
                                {
                                    LocaleName = LocaleNames.Ukrainian,
                                    Name = "Властивості",
                                    Language = LanguageNames.Ukrainian,
                                },
                            },
                        },
                        new()
                        {
                            Name = "Workers",
                            E2EId = "backend-configuration-pn-property-workers",
                            Link = "/plugins/backend-configuration-pn/property-workers",
                            Type = MenuItemTypeEnum.Link,
                            Position = 1,
                            MenuTemplate = new PluginMenuTemplateModel
                            {
                                Name = "Workers",
                                E2EId = "backend-configuration-pn-property-workers",
                                DefaultLink = "/plugins/backend-configuration-pn/property-workers",
                                Permissions = new List<PluginMenuTemplatePermissionModel>(),
                                Translations = new List<PluginMenuTranslationModel>
                                {
                                    new()
                                    {
                                        LocaleName = LocaleNames.English,
                                        Name = "Workers",
                                        Language = LanguageNames.English,
                                    },
                                    new()
                                    {
                                        LocaleName = LocaleNames.German,
                                        Name = "Mitarbeiter",
                                        Language = LanguageNames.German,
                                    },
                                    new()
                                    {
                                        LocaleName = LocaleNames.Danish,
                                        Name = "Medarbejdere",
                                        Language = LanguageNames.Danish,
                                    },
                                    new()
                                    {
                                        LocaleName = LocaleNames.Ukrainian,
                                        Name = "працівників",
                                        Language = LanguageNames.Ukrainian,
                                    }
                                }
                            },
                            Translations = new List<PluginMenuTranslationModel>
                            {
                                new()
                                {
                                    LocaleName = LocaleNames.English,
                                    Name = "Workers",
                                    Language = LanguageNames.English,
                                },
                                new()
                                {
                                    LocaleName = LocaleNames.German,
                                    Name = "Mitarbeiter",
                                    Language = LanguageNames.German,
                                },
                                new()
                                {
                                    LocaleName = LocaleNames.Danish,
                                    Name = "Medarbejdere",
                                    Language = LanguageNames.Danish,
                                },
                                new()
                                {
                                    LocaleName = LocaleNames.Ukrainian,
                                    Name = "працівників",
                                    Language = LanguageNames.Ukrainian,
                                }
                            }
                        },
                        new()
                        {
                            Name = "Task management",
                            E2EId = "backend-configuration-pn-task-management",
                            Link = "/plugins/backend-configuration-pn/task-management",
                            Type = MenuItemTypeEnum.Link,
                            Position = 1,
                            MenuTemplate = new PluginMenuTemplateModel
                            {
                                Name = "Task management",
                                E2EId = "backend-configuration-pn-task-management",
                                DefaultLink = "/plugins/backend-configuration-pn/task-management",
                                Permissions = new List<PluginMenuTemplatePermissionModel>(),
                                Translations = new List<PluginMenuTranslationModel>
                                {
                                    new()
                                    {
                                        LocaleName = LocaleNames.English,
                                        Name = "Task management",
                                        Language = LanguageNames.English,
                                    },
                                    new()
                                    {
                                        LocaleName = LocaleNames.German,
                                        Name = "Aufgabenverwaltung",
                                        Language = LanguageNames.German,
                                    },
                                    new()
                                    {
                                        LocaleName = LocaleNames.Danish,
                                        Name = "Opgavestyring",
                                        Language = LanguageNames.Danish,
                                    },
                                    new()
                                    {
                                        LocaleName = LocaleNames.Ukrainian,
                                        Name = "Управління завданнями",
                                        Language = LanguageNames.Ukrainian,
                                    }
                                }
                            },
                            Translations = new List<PluginMenuTranslationModel>
                            {
                                new()
                                {
                                    LocaleName = LocaleNames.English,
                                    Name = "Task management",
                                    Language = LanguageNames.English,
                                },
                                new()
                                {
                                    LocaleName = LocaleNames.German,
                                    Name = "Aufgabenverwaltung",
                                    Language = LanguageNames.German,
                                },
                                new()
                                {
                                    LocaleName = LocaleNames.Danish,
                                    Name = "Opgavestyring",
                                    Language = LanguageNames.Danish,
                                },
                                new()
                                {
                                    LocaleName = LocaleNames.Ukrainian,
                                    Name = "Управління завданнями",
                                    Language = LanguageNames.Ukrainian,
                                }
                            }
                        },
                    }
                }
            };

            return pluginMenu;
        }

        public MenuModel HeaderMenu(IServiceProvider serviceProvider)
        {
            var localizationService = serviceProvider
                .GetService<IBackendConfigurationLocalizationService>();

            var result = new MenuModel();
            result.LeftMenu.Add(new MenuItemModel
            {
                Name = localizationService?.GetString("BackendConfiguration"),
                E2EId = "backend-configuration-pn",
                Link = "",
                Guards = new List<string> { BackendConfigurationClaims.AccessBackendConfigurationPlugin },
                MenuItems = new List<MenuItemModel>
                {
                    new()
                    {
                        Name = localizationService?.GetString("Properties"),
                        E2EId = "backend-configuration-properties",
                        Link = "/plugins/backend-configuration/properties",
                        Guards = new List<string>(),
                        Position = 0,
                    },
                    new()
                    {
                        Name = localizationService?.GetString("Property workers"),
                        E2EId = "backend-configuration-workers",
                        Link = "/plugins/backend-configuration/workers",
                        Guards = new List<string>(),
                        Position = 1,
                    },
                    new()
                    {
                        Name = localizationService?.GetString("Task management"),
                        E2EId = "backend-configuration-task-management",
                        Link = "/plugins/backend-configuration/task-management",
                        Guards = new List<string>(),
                        Position = 2,
                    },
                }
            });
            return result;
        }

        public void SeedDatabase(string connectionString)
        {
            // Get DbContext
            var contextFactory = new BackendConfigurationPnContextFactory();
            using var context = contextFactory.CreateDbContext(new[] { connectionString });
            // Seed configuration
            BackendConfigurationPluginSeed.SeedData(context);
        }

        public PluginPermissionsManager GetPermissionsManager(string connectionString)
        {
            var contextFactory = new BackendConfigurationPnContextFactory();
            var context = contextFactory.CreateDbContext(new[] { connectionString });

            return new PluginPermissionsManager(context);
        }
    }
}