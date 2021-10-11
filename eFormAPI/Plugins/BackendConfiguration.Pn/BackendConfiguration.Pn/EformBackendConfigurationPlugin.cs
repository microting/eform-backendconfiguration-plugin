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

namespace BackendConfiguration.Pn
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Text.RegularExpressions;
    using Infrastructure.Data.Seed;
    using Infrastructure.Data.Seed.Data;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microting.eFormApi.BasePn;
    using Microting.eFormApi.BasePn.Abstractions;
    using Microting.eFormApi.BasePn.Infrastructure.Consts;
    using Microting.eFormApi.BasePn.Infrastructure.Helpers;
    using Microting.eFormApi.BasePn.Infrastructure.Models.Application;
    using Microting.eFormApi.BasePn.Infrastructure.Models.Application.NavigationMenu;
    using Microting.eFormApi.BasePn.Infrastructure.Settings;
    using Microting.EformBackendConfigurationBase.Infrastructure.Const;
    using Microting.EformBackendConfigurationBase.Infrastructure.Data;
    using Microting.EformBackendConfigurationBase.Infrastructure.Data.Factories;
    using Microting.ItemsPlanningBase.Infrastructure.Data;
    using Services.BackendConfigurationAreaRules;
    using Services.BackendConfigurationAssignmentWorkerService;
    using Services.BackendConfigurationLocalizationService;
    using Services.BackendConfigurationPropertiesService;
    using Services.BackendConfigurationPropertyAreasService;
    using Services.RebusService;

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
            services.AddTransient<IBackendConfigurationAssignmentWorkerService, BackendConfigurationAssignmentWorkerService>();
            services.AddTransient<IBackendConfigurationPropertiesService, BackendConfigurationPropertiesService>();
            services.AddTransient<IBackendConfigurationPropertyAreasService, BackendConfigurationPropertyAreasService>();
            services.AddTransient<IBackendConfigurationAreaRulesService, BackendConfigurationAreaRulesServiceService>();
            services.AddSingleton<IRebusService, RebusService>();
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
            //services.ConfigurePluginDbOptions<BackendConfigurationsBaseSettings>(
            //    configuration.GetSection("BackendConfigurationsBaseSettings"));
        }

        private static async void SeedEForms(IServiceCollection services)
        {
            var serviceProvider = services.BuildServiceProvider();

            var core = await serviceProvider.GetRequiredService<IEFormCoreService>().GetCore();
            var eforms = BackendConfigurationSeedEforms.EformsSeed;
            var sdkDbContext = core.DbContextHelper.GetDbContext();

            var context = serviceProvider.GetRequiredService<BackendConfigurationPnDbContext>();
            // seed eforms
            foreach (var eform in eforms)
            {
                var newTemplate = await core.TemplateFromXml(eform);
                if (!await sdkDbContext.CheckLists.AnyAsync(x => x.OriginalId == newTemplate.OriginalId))
                {
                    await core.TemplateCreate(newTemplate);
                }
            }

            // Seed areas and rules
            foreach (var newArea in BackendConfigurationSeedAreas.AreasSeed.Where(newArea => !context.Areas.Any(x => x.Id == newArea.Id)))
            {
                foreach (var areaRule in newArea.AreaRules.Where(areaRule => !areaRule.EformId.HasValue && !string.IsNullOrEmpty(areaRule.EformName)))
                {
                    var eformId = await sdkDbContext.CheckListTranslations
                        .Where(x => x.Text == areaRule.EformName)
                        .Select(x => x.CheckListId)
                        .FirstAsync();
                    areaRule.EformId = eformId;
                }
                //context.Areas.Add(newArea);
                    
                //context.SaveChanges();
                await newArea.Create(context);
            }
        }

        public void ConfigureDbContext(IServiceCollection services, string connectionString)
        {
            var itemsPlannigConnectionString = connectionString.Replace(
                "eform-backend-configuration-plugin",
                "eform-angular-items-planning-plugin");

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
