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

using System.Threading.Tasks;

namespace BackendConfiguration.Pn
{
	using ChemicalsBase.Infrastructure;
	using ChemicalsBase.Infrastructure.Data.Factories;
	using eFormCore;
	using Infrastructure.Data.Seed;
	using Infrastructure.Data.Seed.Data;
	using Infrastructure.Models.Settings;
	using Messages;
	using Microsoft.AspNetCore.Builder;
	using Microsoft.EntityFrameworkCore;
	using Microsoft.Extensions.Configuration;
	using Microsoft.Extensions.DependencyInjection;
	using Microting.eForm.Infrastructure;
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
	using Microting.eFormCaseTemplateBase.Infrastructure.Data;
	using Microting.eFormCaseTemplateBase.Infrastructure.Data.Factories;
	using Microting.ItemsPlanningBase.Infrastructure.Data;
	using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
	using Microting.TimePlanningBase.Infrastructure.Data;
	using Rebus.Bus;
	using Services.BackendConfigurationAreaRulePlanningsService;
	using Services.BackendConfigurationAreaRulesService;
	using Services.BackendConfigurationAssignmentWorkerService;
	using Services.BackendConfigurationCaseService;
	using Services.BackendConfigurationCompliancesService;
	using Services.BackendConfigurationDocumentService;
	using Services.BackendConfigurationLocalizationService;
	using Services.BackendConfigurationPropertiesService;
	using Services.BackendConfigurationPropertyAreasService;
	using Services.BackendConfigurationReportService;
	using Services.BackendConfigurationTaskManagementService;
	using Services.ChemicalService;
	using Services.ExcelService;
	using Services.RebusService;
	using Services.WordService;
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using System.Reflection;
	using Services.BackendConfigurationFilesService;
	using Services.BackendConfigurationFileTagsService;
	using FolderTranslation = Microting.eForm.Infrastructure.Data.Entities.FolderTranslation;

	public class EformBackendConfigurationPlugin : IEformPlugin
    {
        public string Name => "Microting Backend Configuration Plugin";
        public string PluginId => "eform-backend-configuration-plugin";
        public string PluginPath => PluginAssembly().Location;
        public string PluginBaseUrl => "backend-configuration-pn";
        private static IBus _bus;

        private string _connectionString;

        public Assembly PluginAssembly()
        {
            return typeof(EformBackendConfigurationPlugin).GetTypeInfo().Assembly;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<IBackendConfigurationLocalizationService, BackendConfigurationLocalizationService>();
            services.AddTransient<IBackendConfigurationAssignmentWorkerService, BackendConfigurationAssignmentWorkerService>();
            services.AddTransient<IBackendConfigurationPropertiesService, BackendConfigurationPropertiesService>();
            services.AddTransient<IBackendConfigurationPropertyAreasService, BackendConfigurationPropertyAreasService>();
            services.AddTransient<IBackendConfigurationAreaRulesService, BackendConfigurationAreaRulesService>();
            services.AddTransient<IBackendConfigurationAreaRulePlanningsService, BackendConfigurationAreaRulePlanningsService>();
            services.AddTransient<IBackendConfigurationCompliancesService, BackendConfigurationCompliancesService>();
            services.AddTransient<IBackendConfigurationTaskManagementService, BackendConfigurationTaskManagementService>();
            services.AddTransient<IBackendConfigurationDocumentService, BackendConfigurationDocumentService>();
            services.AddTransient<IBackendConfigurationReportService, BackendConfigurationReportService>();
            services.AddTransient<IBackendConfigurationCaseService, BackendConfigurationCaseService>();
            services.AddTransient<IBackendConfigurationTagsService, BackendConfigurationTagsService>();
            services.AddTransient<IBackendConfigurationFilesService, BackendConfigurationFilesService>();
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
            var caseTemplateContext = serviceProvider.GetRequiredService<CaseTemplatePnDbContext>();
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
            var cls = await sdkDbContext.CheckLists.Where(x =>
                x.OriginalId == "142719" && x.WorkflowState != Microting.eForm.Infrastructure.Constants
                    .Constants.WorkflowStates.Removed).ToListAsync();
            foreach (var checkList in cls)
            {
                await checkList.Delete(sdkDbContext);
                var clts = await sdkDbContext.CheckListTranslations.Where(x =>
                    x.CheckListId == checkList.Id).ToListAsync();

                foreach (var clt in clts)
                {
                    await clt.Delete(sdkDbContext);
                }
            }

            // Fix old machines to use new eform
            cls = await sdkDbContext.CheckLists.Where(x =>
	            x.OriginalId == "142401" && x.WorkflowState != Microting.eForm.Infrastructure.Constants
		            .Constants.WorkflowStates.Removed).ToListAsync();
            foreach (var checkList in cls)
            {
	            var clts = await sdkDbContext.CheckListTranslations.Where(x =>
		            x.CheckListId == checkList.Id).ToListAsync();

	            foreach (var clt in clts)
	            {
		            clt.Text += " (old)";
		            await clt.Update(sdkDbContext);
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

            var pT = await itemsPlanningContext.PlanningTags.FirstOrDefaultAsync(x => x.Name.Contains("- fækale uheld")).ConfigureAwait(false);

            if (pT != null)
            {
                var planningTags =
                    await itemsPlanningContext.PlanningsTags
                        .Where(x => x.WorkflowState !=
                                    Microting.eForm.Infrastructure.Constants.Constants.WorkflowStates.Removed)
                        .Where(x => x.PlanningTagId == pT.Id).ToListAsync();

                foreach (var planningTag in planningTags)
                {
                    var planningNameTranslation = await itemsPlanningContext.PlanningNameTranslation
                        .FirstAsync(x => x.PlanningId == planningTag.PlanningId).ConfigureAwait(false);
                    if (!planningNameTranslation.Name.Contains("24. Fækale uheld"))
                    {
                        await planningTag.Delete(itemsPlanningContext);
                    }
                }
            }

            await UpdateAreaTranslations(context);

            await CreateFoldersTranslations(core);

			await UpdateCheckLists(sdkDbContext);

			var propertyWorkers = await context.PropertyWorkers
                .Where(x => x.WorkflowState != Microting.eForm.Infrastructure.Constants.Constants.WorkflowStates.Removed)
				.Where(x => x.TaskManagementEnabled == null)
                .ToListAsync();

            foreach (var propertyWorker in propertyWorkers)
            {
                propertyWorker.TaskManagementEnabled = true;
                await propertyWorker.Update(context);
            }

            var planningSites = await context.PlanningSites
                .Where(x => x.Status == 0)
                .Where(x =>
                    x.WorkflowState != Microting.eForm.Infrastructure.Constants.Constants.WorkflowStates.Removed)
                .ToListAsync();

            foreach (var planningSite in planningSites)
            {
                var areaRulePlanning =
                    await context.AreaRulePlannings
                        .FirstAsync(x => x.Id == planningSite.AreaRulePlanningsId)
                        .ConfigureAwait(false);
                var itemPlanningCaseSite = await itemsPlanningContext.PlanningCaseSites
                    .Where(x => x.MicrotingSdkSiteId == planningSite.SiteId)
                    .Where(x => x.WorkflowState != Microting.eForm.Infrastructure.Constants.Constants.WorkflowStates.Removed)
                    .Where(x => x.PlanningId == areaRulePlanning.ItemPlanningId)
                    .OrderBy(x => x.Id)
                    .LastOrDefaultAsync().ConfigureAwait(false);
                if (itemPlanningCaseSite == null) continue;
                planningSite.Status = itemPlanningCaseSite.Status;
                await planningSite.Update(context);
            }

            var documents = await caseTemplateContext.Documents
                .Where(x => x.WorkflowState != Microting.eForm.Infrastructure.Constants.Constants.WorkflowStates.Removed)
                .Where(x => x.UpdatedAt < new DateTime(2022, 12, 22, 14, 0, 0))
                .ToListAsync();

            foreach (var document in documents)
            {
                var documentSites = await caseTemplateContext.DocumentSites
                    .Where(x => x.DocumentId == document.Id)
                    .ToListAsync();

                foreach (var documentSite in documentSites)
                {
                    if (documentSite.SdkCaseId != 0)
                    {
                        await core.CaseDelete(documentSite.SdkCaseId);
                    }

                    await documentSite.Delete(caseTemplateContext);
                }

                await _bus.SendLocal(new DocumentUpdated(document.Id)).ConfigureAwait(false);
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
            var documentsConnectionString = connectionString.Replace(
                "eform-backend-configuration-plugin",
                "eform-angular-case-template-plugin");

            _connectionString = connectionString;
            services.AddDbContext<BackendConfigurationPnDbContext>(o =>
                o.UseMySql(connectionString, new MariaDbServerVersion(
                    ServerVersion.AutoDetect(connectionString)), mySqlOptionsAction: builder =>
                {
                    builder.EnableRetryOnFailure();
                    builder.MigrationsAssembly(PluginAssembly().FullName);
                }));

            services.AddDbContext<ItemsPlanningPnDbContext>(o =>
                o.UseMySql(itemsPlannigConnectionString, new MariaDbServerVersion(
                    ServerVersion.AutoDetect(itemsPlannigConnectionString)), mySqlOptionsAction: builder =>
                {
                    builder.EnableRetryOnFailure();
                    builder.MigrationsAssembly(PluginAssembly().FullName);
                }));

            services.AddDbContext<TimePlanningPnDbContext>(o =>
                o.UseMySql(timeRegistrationConnectionString, new MariaDbServerVersion(
                    ServerVersion.AutoDetect(timeRegistrationConnectionString)), mySqlOptionsAction: builder =>
                {
                    builder.EnableRetryOnFailure();
                    builder.MigrationsAssembly(PluginAssembly().FullName);
                }));

            services.AddDbContext<ChemicalsDbContext>(o =>
                o.UseMySql(chemicalBaseConnectionString, new MariaDbServerVersion(
                    ServerVersion.AutoDetect(chemicalBaseConnectionString)), mySqlOptionsAction: builder =>
                {
                    builder.EnableRetryOnFailure();
                    builder.MigrationsAssembly(PluginAssembly().FullName);
                }));

            services.AddDbContext<CaseTemplatePnDbContext>(o =>
                o.UseMySql(documentsConnectionString, new MariaDbServerVersion(
                    ServerVersion.AutoDetect(documentsConnectionString)), mySqlOptionsAction: builder =>
                {
                    builder.EnableRetryOnFailure();
                    builder.MigrationsAssembly(PluginAssembly().FullName);
                }));

            var chemicalsContextFactory = new ChemicalsContextFactory();
            var chemicalsDbContext = chemicalsContextFactory.CreateDbContext(new[] { chemicalBaseConnectionString });
            chemicalsDbContext.Database.Migrate();

            var caseTemplatePnContextFactory = new CaseTemplatePnContextFactory();
            var caseTemplateContext = caseTemplatePnContextFactory.CreateDbContext(new[] { documentsConnectionString });
            caseTemplateContext.Database.Migrate();

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

            IRebusService rebusService = serviceProvider.GetService<IRebusService>();
            rebusService!.Start(_connectionString).GetAwaiter().GetResult();
            _bus = rebusService.GetBus();
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
                        new()
                            {
                                Name = "Reports",
                                E2EId = "backend-configuration-pn-reports",
                                Link = "/plugins/backend-configuration-pn/reports",
                                Type = MenuItemTypeEnum.Link,
                                Position = 1,
                                MenuTemplate = new PluginMenuTemplateModel
                                {
                                    Name = "Reports",
                                    E2EId = "backend-configuration-pn-reports",
                                    DefaultLink = "/plugins/backend-configuration-pn/reports",
                                    Permissions = new List<PluginMenuTemplatePermissionModel>(),
                                    Translations = new List<PluginMenuTranslationModel>
                                    {
                                        new PluginMenuTranslationModel
                                        {
                                            LocaleName = LocaleNames.English,
                                            Name = "Reports",
                                            Language = LanguageNames.English,
                                        },
                                        new PluginMenuTranslationModel
                                        {
                                            LocaleName = LocaleNames.German,
                                            Name = "Berichte",
                                            Language = LanguageNames.German,
                                        },
                                        new PluginMenuTranslationModel
                                        {
                                            LocaleName = LocaleNames.Danish,
                                            Name = "Rapporter",
                                            Language = LanguageNames.Danish,
                                        },
                                        new PluginMenuTranslationModel
                                        {
                                            LocaleName = LocaleNames.Ukrainian,
                                            Name = "Звіти",
                                            Language = LanguageNames.Ukrainian,
                                        }
                                    }
                                },
                                Translations = new List<PluginMenuTranslationModel>
                                {
                                    new PluginMenuTranslationModel
                                    {
                                        LocaleName = LocaleNames.English,
                                        Name = "Reports",
                                        Language = LanguageNames.English,
                                    },
                                    new PluginMenuTranslationModel
                                    {
                                        LocaleName = LocaleNames.German,
                                        Name = "Berichte",
                                        Language = LanguageNames.German,
                                    },
                                    new PluginMenuTranslationModel
                                    {
                                        LocaleName = LocaleNames.Danish,
                                        Name = "Rapporter",
                                        Language = LanguageNames.Danish,
                                    },
                                    new PluginMenuTranslationModel
                                    {
                                        LocaleName = LocaleNames.Ukrainian,
                                        Name = "Звіти",
                                        Language = LanguageNames.Ukrainian,
                                    }
                                }
                            },
                        new()
                        {
                            Name = "Documents",
                            E2EId = "backend-configuration-pn-documents",
                            Link = "/plugins/backend-configuration-pn/documents",
                            Type = MenuItemTypeEnum.Link,
                            Position = 1,
                            MenuTemplate = new PluginMenuTemplateModel
                            {
                                Name = "Documents",
                                E2EId = "backend-configuration-pn-documents",
                                DefaultLink = "/plugins/backend-configuration-pn/documents",
                                Permissions = new List<PluginMenuTemplatePermissionModel>(),
                                Translations = new List<PluginMenuTranslationModel>
                                {
                                    new()
                                    {
                                        LocaleName = LocaleNames.English,
                                        Name = "Documents",
                                        Language = LanguageNames.English,
                                    },
                                    new()
                                    {
                                        LocaleName = LocaleNames.German,
                                        Name = "Unterlagen",
                                        Language = LanguageNames.German,
                                    },
                                    new()
                                    {
                                        LocaleName = LocaleNames.Danish,
                                        Name = "Dokumenter",
                                        Language = LanguageNames.Danish,
                                    },
                                    new()
                                    {
                                        LocaleName = LocaleNames.Ukrainian,
                                        Name = "Документи",
                                        Language = LanguageNames.Ukrainian,
                                    }
                                }
                            },
                            Translations = new List<PluginMenuTranslationModel>
                            {
                                new()
                                {
                                    LocaleName = LocaleNames.English,
                                    Name = "Documents",
                                    Language = LanguageNames.English,
                                },
                                new()
                                {
                                    LocaleName = LocaleNames.German,
                                    Name = "Unterlagen",
                                    Language = LanguageNames.German,
                                },
                                new()
                                {
                                    LocaleName = LocaleNames.Danish,
                                    Name = "Dokumenter",
                                    Language = LanguageNames.Danish,
                                },
                                new()
                                {
                                    LocaleName = LocaleNames.Ukrainian,
                                    Name = "Документи",
                                    Language = LanguageNames.Ukrainian,
                                }
                            }
                        },
						new()
						{
							Name = "Files",
							E2EId = "backend-configuration-pn-files",
							Link = "/plugins/backend-configuration-pn/files",
							Type = MenuItemTypeEnum.Link,
							Position = 1,
							MenuTemplate = new PluginMenuTemplateModel
							{
								Name = "Files",
								E2EId = "backend-configuration-pn-files",
								DefaultLink = "/plugins/backend-configuration-pn/files",
								Permissions = new List<PluginMenuTemplatePermissionModel>(),
								Translations = new List<PluginMenuTranslationModel>
								{
									new()
									{
										LocaleName = LocaleNames.English,
										Name = "Files",
										Language = LanguageNames.English,
									},
									new()
									{
										LocaleName = LocaleNames.German,
										Name = "Datei",
										Language = LanguageNames.German,
									},
									new()
									{
										LocaleName = LocaleNames.Danish,
										Name = "Fil",
										Language = LanguageNames.Danish,
									},
									new()
									{
										LocaleName = LocaleNames.Ukrainian,
										Name = "Файли",
										Language = LanguageNames.Ukrainian,
									}
								}
							},
							Translations = new List<PluginMenuTranslationModel>
							{
								new()
								{
									LocaleName = LocaleNames.English,
									Name = "Files",
									Language = LanguageNames.English,
								},
								new()
								{
									LocaleName = LocaleNames.German,
									Name = "Datei",
									Language = LanguageNames.German,
								},
								new()
								{
									LocaleName = LocaleNames.Danish,
									Name = "Fil",
									Language = LanguageNames.Danish,
								},
								new()
								{
									LocaleName = LocaleNames.Ukrainian,
									Name = "Файли",
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
                    new()
                    {
                        Name = localizationService?.GetString("Documents"),
                        E2EId = "backend-configuration-documents",
                        Link = "/plugins/backend-configuration/documents",
                        Guards = new List<string>(),
                        Position = 3,
                    },
                    new()
                    {
	                    Name = localizationService?.GetString("Files"),
	                    E2EId = "backend-configuration-files",
	                    Link = "/plugins/backend-configuration/files",
	                    Guards = new List<string>(),
	                    Position = 4,
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

        private static async Task CreateFolderTranslations(Core core, MicrotingDbContext sdkDbContext, IEnumerable<FolderTranslation> folderTranslations, List<CommonTranslationsModel> translations)
        {
	        foreach (var folderTranslation in folderTranslations.Where(x => x != null))
	        {
		        var folder = await sdkDbContext.Folders
			        .Where(x => x.Id == folderTranslation.FolderId)
			        .Where(x => x.WorkflowState != Microting.eForm.Infrastructure.Constants.Constants.WorkflowStates.Removed)
					.FirstOrDefaultAsync();

		        if (folder != null)
		        {
			        await core.FolderUpdate(folder.Id, translations, folder.ParentId);
		        }
	        }
		}

        private static async Task CreateFoldersTranslations(Core core)
		{
			var sdkDbContext = core.DbContextHelper.GetDbContext();
			await CreateFolderTranslations(core, sdkDbContext,
				await sdkDbContext.FolderTranslations
			        .Where(x => x.WorkflowState != Microting.eForm.Infrastructure.Constants.Constants.WorkflowStates.Removed)
			        .Where(x => x.Name == "01. Fokusområder Miljøledelse").ToListAsync(),
		        new List<CommonTranslationsModel>
		        {
			        new()
			        {
				        Name = "01. Logbøger Miljøledelse",
				        LanguageId = 1, // da
				        Description = "",
			        },
			        new()
			        {
				        Name = "01. Log books Environmental management",
				        LanguageId = 2, // en
				        Description = "",
			        },
			        new()
			        {
				        Name = "01. Logbücher Umweltmanagement",
				        LanguageId = 3, // de
				        Description = "",
			        },
		        }
	        );
			await CreateFolderTranslations(core, sdkDbContext,
				await sdkDbContext.FolderTranslations.Where(x =>
					x.WorkflowState != Microting.eForm.Infrastructure.Constants.Constants.WorkflowStates.Removed
					&& x.Name == "24.01 Logbøger miljøteknologier").ToListAsync(),
				new List<CommonTranslationsModel>
				{
					new()
					{
						LanguageId = 1, // da
						Name = "24.01 Logbøger og bilag",
						Description = "",
					},
					new()
					{
						LanguageId = 2, // en
						Name = "24.01 Logbooks and appendices",
						Description = "",
					},
					new()
					{
						LanguageId = 3, // ge
						Name = "24.01 Logbücher und Anhänge",
						Description = "",
					},
				}
			);

			await CreateFolderTranslations(core, sdkDbContext,
				await sdkDbContext.FolderTranslations.Where(x =>
					x.WorkflowState != Microting.eForm.Infrastructure.Constants.Constants.WorkflowStates.Removed
					&& x.Name == "24.02 Dokumentation afsluttede inspektioner").ToListAsync(),
				new List<CommonTranslationsModel>
				{
					new()
					{
						LanguageId = 1, // da
						Name = "24.02 Kontroller og bilag",
						Description = "",
					},
					new()
					{
						LanguageId = 2, // en
						Name = "24.02 Checks and attachments",
						Description = "",
					},
					new()
					{
						LanguageId = 3, // ge
						Name = "24.02 Schecks und Anhänge",
						Description = "",
					},
				}
			);

			await CreateFolderTranslations(core, sdkDbContext,
				await sdkDbContext.FolderTranslations.Where(x =>
					x.WorkflowState != Microting.eForm.Infrastructure.Constants.Constants.WorkflowStates.Removed
					&& x.Name == "24.03 Dokumentation miljøledelse").ToListAsync(),
				new List<CommonTranslationsModel>
				{
					new()
					{
						LanguageId = 1, // da
						Name = "24.03 Miljøledelse",
						Description = "",
					},
					new()
					{
						LanguageId = 2, // en
						Name = "24.03 Environmental management",
						Description = "",
					},
					new()
					{
						LanguageId = 3, // ge
						Name = "24.03 Umweltmanagement",
						Description = "",
					},
				}
			);

			await CreateFolderTranslations(core, sdkDbContext,
				await sdkDbContext.FolderTranslations
					.Where(x => x.WorkflowState != Microting.eForm.Infrastructure.Constants.Constants.WorkflowStates.Removed)
					.Where(x => x.Name == "24.04 Overholdelse fodringskrav")
					.ToListAsync(),
				new List<CommonTranslationsModel>
				{
					new()
					{
						LanguageId = 1, // da
						Name = "24.04 Fodringskrav",
						Description = "",
					},
					new()
					{
						LanguageId = 2, // en
						Name = "24.04 Feeding requirements",
						Description = "",
					},
					new()
					{
						LanguageId = 3, // ge
						Name = "24.04 Fütterungsanforderungen",
						Description = "",
					},
				}
			);
		}

        private static async Task UpdateAreaTranslations(BackendConfigurationPnDbContext context)
        {
            var listWithOldAndNewTranslates = new List<KeyValuePair<string, string>>
            { // old value - key, new value - value
                new("25. Kemisk APV", "25. Kemikontrol"),
                new("25. Chemical APV", "25. Chemical control"),
                new("25. Chemisches APV", "25. Chemische Kontrolle"),
                new("01. Fokusområder Miljøledelse", "01. Logbøger Miljøledelse"),
                new("01. Focus areas Environmental management", "01. Log books Environmental management"),
                new("01. Schwerpunkte Umweltverwaltung", "01. Logbücher Umweltmanagement"),
			};

            foreach (var (oldValue, newValue) in listWithOldAndNewTranslates)
			{
				var areaTranslation = await context.AreaTranslations.FirstOrDefaultAsync(x => x.Name == oldValue);
				if (areaTranslation == null) continue;
				areaTranslation.Name = newValue;
				await areaTranslation.Update(context);
			}
		}

		private static async Task UpdateCheckLists(MicrotingDbContext sdkDbContext)
		{
			var clTranslation = await sdkDbContext.CheckListTranslations.FirstAsync(x => x.Text == "25.01 Registrer produkter");
			var clCheckList = await sdkDbContext.CheckLists.FirstAsync(x => x.ParentId == clTranslation.CheckListId);

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

			//string text = $"05. Halebid og risikovurdering - {propertyName}|05. Tail bite and risc assessment - {propertyName}";
			var clTranslations = await sdkDbContext.CheckListTranslations
				.Where(x => x.Text.Contains("05. Halebid og risikovurdering")).ToListAsync().ConfigureAwait(false);

			var engLanguage = await sdkDbContext.Languages.FirstAsync(x => x.LanguageCode == "en-US").ConfigureAwait(false);

			foreach (var clTranslation1 in clTranslations)
			{
				var engClTranslation = await sdkDbContext.CheckListTranslations.Where(x => x.LanguageId == engLanguage.Id)
					.Where(x => x.CheckListId == clTranslation1.CheckListId).FirstOrDefaultAsync().ConfigureAwait(false);

				if (engClTranslation == null)
				{
					var propertyParts = clTranslation1.Text.Split(" - ");
					var propertyName = propertyParts[1];
					var newClTranslation = new CheckListTranslation
					{
						CheckListId = clTranslation1.CheckListId,
						LanguageId = engLanguage.Id,
						Text = $"05. Tail bite and risc assessment - {propertyName}",
						Description = ""
					};
					await newClTranslation.Create(sdkDbContext).ConfigureAwait(false);
				}
			}

			/*cltranslation = await sdkDbContext.CheckListTranslations.FirstOrDefaultAsync(x => x.Text == "01. Elforbrug")
                .ConfigureAwait(false);
            if (cltranslation != null)
            {
                clCheckList = await sdkDbContext.CheckLists.FirstOrDefaultAsync(x => x.Id == cltranslation.CheckListId)
                    .ConfigureAwait(false);
                if (clCheckList != null)
                {
                    clCheckList.ReportH1 = "24.00Aflæsninger";
                    clCheckList.ReportH2 = "24.00.02Aflæsning el";
                    await clCheckList.Update(sdkDbContext).ConfigureAwait(false);
                }
            }

            cltranslation = await sdkDbContext.CheckListTranslations.FirstOrDefaultAsync(x => x.Text == "01. Vandforbrug")
                .ConfigureAwait(false);
            if (cltranslation != null)
            {
                clCheckList = await sdkDbContext.CheckLists.FirstOrDefaultAsync(x => x.Id == cltranslation.CheckListId)
                    .ConfigureAwait(false);

                if (clCheckList != null)
                {
                    clCheckList.ReportH1 = "24.00Aflæsninger";
                    clCheckList.ReportH2 = "24.00.01Aflæsning vand";
                    await clCheckList.Update(sdkDbContext).ConfigureAwait(false);
                }
            }*/
		}
	}
}