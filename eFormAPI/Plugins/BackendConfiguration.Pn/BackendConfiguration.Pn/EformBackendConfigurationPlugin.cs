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
using Castle.MicroKernel.Registration;
using Castle.Windsor;
using ChemicalsBase.Infrastructure;
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

            var pT = await itemsPlanningContext.PlanningTags.FirstAsync(x => x.Name.Contains("- fækale uheld")).ConfigureAwait(false);

            var planningTags =
                await itemsPlanningContext.PlanningsTags
                    .Where(x => x.WorkflowState != Microting.eForm.Infrastructure.Constants.Constants.WorkflowStates.Removed)
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

            /*
            if (context.AreaTranslations.Any(x => x.Name == "11. Pillefyr" && x.LanguageId == 1))
            {
                var planningTag = itemsPlanningContext.PlanningTags.SingleOrDefault(x => x.Name == "11. Pillefyr");
                if (planningTag != null)
                {
                    planningTag.Name = "11. Varmekilder";
                    await planningTag.Update(itemsPlanningContext);
                }

            var areaTranslation = await context.AreaTranslations.FirstOrDefaultAsync(x => x.Name == "25. Kemisk APV");
            if (areaTranslation != null)
            {
                areaTranslation.Name = "25. Kemikontrol";
                await areaTranslation.Update(context).ConfigureAwait(false);
            }
            areaTranslation = await context.AreaTranslations.FirstOrDefaultAsync(x => x.Name == "25. Chemical APV");
            if (areaTranslation != null)
            {
                areaTranslation.Name = "25. Chemical control";
                await areaTranslation.Update(context).ConfigureAwait(false);
            }
            areaTranslation = await context.AreaTranslations.FirstOrDefaultAsync(x => x.Name == "25. Chemisches APV");
            if (areaTranslation != null)
            {
                areaTranslation.Name = "25. Chemische Kontrolle";
                await areaTranslation.Update(context).ConfigureAwait(false);
            }

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

            IRebusService rebusService = serviceProvider.GetService<IRebusService>();

            WindsorContainer container = rebusService.GetContainer();
            container.Register(Component.For<EformBackendConfigurationPlugin>().Instance(this));
            rebusService.Start(_connectionString, "admin", "password", rabbitMqHost);
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