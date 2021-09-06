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
    using System.Reflection;
    using System.Text.RegularExpressions;
    using Infrastructure.Data.Seed;
    using Infrastructure.Data.Seed.Data;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microting.eFormApi.BasePn;
    using Microting.eFormApi.BasePn.Infrastructure.Consts;
    using Microting.eFormApi.BasePn.Infrastructure.Helpers;
    using Microting.eFormApi.BasePn.Infrastructure.Models.Application;
    using Microting.eFormApi.BasePn.Infrastructure.Models.Application.NavigationMenu;
    using Microting.eFormApi.BasePn.Infrastructure.Settings;
    using Microting.EformBackendConfigurationBase.Infrastructure.Const;
    using Microting.EformBackendConfigurationBase.Infrastructure.Data;
    using Microting.EformBackendConfigurationBase.Infrastructure.Data.Factories;
    using Services.BackendConfigurationLocalizationService;
    using Services.BackendConfigurationPropertiesService;
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
            services.AddTransient<IBackendConfigurationPropertiesService, BackendConfigurationPropertiesService>();
            services.AddSingleton<IRebusService, RebusService>();
            services.AddControllers();
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
            //services.ConfigurePluginDbOptions<ItemsPlanningBaseSettings>(
            //    configuration.GetSection("ItemsPlanningBaseSettings"));
        }

        public void ConfigureDbContext(IServiceCollection services, string connectionString)
        {
            _connectionString = connectionString;
            services.AddDbContext<BackendConfigurationPnDbContext>(o =>
                o.UseMySql(connectionString, new MariaDbServerVersion(
                    new Version(10, 4, 0)), mySqlOptionsAction: builder =>
                {
                    builder.EnableRetryOnFailure();
                    builder.MigrationsAssembly(PluginAssembly().FullName);
                }));

            BackendConfigurationPnContextFactory contextFactory = new BackendConfigurationPnContextFactory();
            var context = contextFactory.CreateDbContext(new[] { connectionString });
            context.Database.Migrate();

            // Seed database
            SeedDatabase(connectionString);
        }

        public void Configure(IApplicationBuilder appBuilder)
        {
            var serviceProvider = appBuilder.ApplicationServices;

            string rabbitMqHost = "localhost";

            if (_connectionString.Contains("frontend"))
            {
                var dbPrefix = Regex.Match(_connectionString, @"atabase=(\d*)_").Groups[1].Value;
                rabbitMqHost = $"frontend-{dbPrefix}-rabbitmq";
            }

            IRebusService rebusService = serviceProvider.GetService<IRebusService>();
            rebusService.Start(_connectionString, "admin", "password", rabbitMqHost);

            //_bus = rebusService.GetBus();
        }

        public List<PluginMenuItemModel> GetNavigationMenu(IServiceProvider serviceProvider)
        {
            var pluginMenu = new List<PluginMenuItemModel>
            {
                new PluginMenuItemModel
                {
                    Name = "Dropdown",
                    E2EId = "backend-configuration-pn",
                    Link = "",
                    Type = MenuItemTypeEnum.Dropdown,
                    Position = 0,
                    Translations = new List<PluginMenuTranslationModel>
                    {
                        new PluginMenuTranslationModel
                        {
                             LocaleName = LocaleNames.English,
                             Name = "Backend Configuration",
                             Language = LanguageNames.English,
                        },
                        new PluginMenuTranslationModel
                        {
                             LocaleName = LocaleNames.German,
                             Name = "",//todo
                             Language = LanguageNames.German,
                        },
                        new PluginMenuTranslationModel
                        {
                             LocaleName = LocaleNames.Danish,
                             Name = "",//todo
                             Language = LanguageNames.Danish,
                        },
                        new PluginMenuTranslationModel
                        {
                            LocaleName = LocaleNames.Ukrainian,
                            Name = "Конфігурація серверної частини",
                            Language = LanguageNames.Ukrainian,
                        }
                    },
                    ChildItems = new List<PluginMenuItemModel>
                    {
                        new PluginMenuItemModel
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
                                    new PluginMenuTranslationModel
                                    {
                                        LocaleName = LocaleNames.English,
                                        Name = "Properties",
                                        Language = LanguageNames.English,
                                    },
                                    new PluginMenuTranslationModel
                                    {
                                        LocaleName = LocaleNames.German,
                                        Name = "",//todo
                                        Language = LanguageNames.German,
                                    },
                                    new PluginMenuTranslationModel
                                    {
                                        LocaleName = LocaleNames.Danish,
                                        Name = "",//todo
                                        Language = LanguageNames.Danish,
                                    },
                                    new PluginMenuTranslationModel
                                    {
                                        LocaleName = LocaleNames.Ukrainian,
                                        Name = "Властивості",
                                        Language = LanguageNames.Ukrainian,
                                    },
                                }
                            },
                            Translations = new List<PluginMenuTranslationModel>
                            {
                                new PluginMenuTranslationModel
                                {
                                    LocaleName = LocaleNames.English,
                                    Name = "Properties",
                                    Language = LanguageNames.English,
                                },
                                new PluginMenuTranslationModel
                                {
                                    LocaleName = LocaleNames.German,
                                    Name = "",//todo
                                    Language = LanguageNames.German,
                                },
                                new PluginMenuTranslationModel
                                {
                                    LocaleName = LocaleNames.Danish,
                                    Name = "",//todo
                                    Language = LanguageNames.Danish,
                                },
                                new PluginMenuTranslationModel
                                {
                                    LocaleName = LocaleNames.Ukrainian,
                                    Name = "Властивості",
                                    Language = LanguageNames.Ukrainian,
                                },
                            },
                        },
                        new PluginMenuItemModel
                        {
                            Name = "Workers",
                            E2EId = "backend-configuration-pn-workers",
                            Link = "/plugins/backend-configuration-pn/workers",
                            Type = MenuItemTypeEnum.Link,
                            Position = 1,
                            MenuTemplate = new PluginMenuTemplateModel
                            {
                                Name = "Workers",
                                E2EId = "backend-configuration-pn-workers",
                                DefaultLink = "/plugins/backend-configuration-pn/workers",
                                Permissions = new List<PluginMenuTemplatePermissionModel>(),
                                Translations = new List<PluginMenuTranslationModel>
                                {
                                    new PluginMenuTranslationModel
                                    {
                                        LocaleName = LocaleNames.English,
                                        Name = "Workers",
                                        Language = LanguageNames.English,
                                    },
                                    new PluginMenuTranslationModel
                                    {
                                        LocaleName = LocaleNames.German,
                                        Name = "",//todo
                                        Language = LanguageNames.German,
                                    },
                                    new PluginMenuTranslationModel
                                    {
                                        LocaleName = LocaleNames.Danish,
                                        Name = "",//todo
                                        Language = LanguageNames.Danish,
                                    },
                                    new PluginMenuTranslationModel
                                    {
                                        LocaleName = LocaleNames.Ukrainian,
                                        Name = "Працівники",
                                        Language = LanguageNames.Ukrainian,
                                    }
                                }
                            },
                            Translations = new List<PluginMenuTranslationModel>
                            {
                                new PluginMenuTranslationModel
                                {
                                    LocaleName = LocaleNames.English,
                                    Name = "Workers",
                                    Language = LanguageNames.English,
                                },
                                new PluginMenuTranslationModel
                                {
                                    LocaleName = LocaleNames.German,
                                    Name = "",//todo
                                    Language = LanguageNames.German,
                                },
                                new PluginMenuTranslationModel
                                {
                                    LocaleName = LocaleNames.Danish,
                                    Name = "",//todo
                                    Language = LanguageNames.Danish,
                                },
                                new PluginMenuTranslationModel
                                {
                                    LocaleName = LocaleNames.Ukrainian,
                                    Name = "Працівники",
                                    Language = LanguageNames.Ukrainian,
                                }
                            }
                        },
                        new PluginMenuItemModel
                        {
                            Name = "Areas",
                            E2EId = "backend-configuration-pn-areas",
                            Link = "/plugins/backend-configuration-pn/areas",
                            Type = MenuItemTypeEnum.Link,
                            Position = 2,
                            MenuTemplate = new PluginMenuTemplateModel
                            {
                                Name = "Areas",
                                E2EId = "backend-configuration-pn-areas",
                                DefaultLink = "/plugins/backend-configuration-pn/areas",
                                Permissions = new List<PluginMenuTemplatePermissionModel>(),
                                Translations = new List<PluginMenuTranslationModel>
                                {
                                    new PluginMenuTranslationModel
                                    {
                                        LocaleName = LocaleNames.English,
                                        Name = "Areas",
                                        Language = LanguageNames.English,
                                    },
                                    new PluginMenuTranslationModel
                                    {
                                        LocaleName = LocaleNames.German,
                                        Name = "",//todo
                                        Language = LanguageNames.German,
                                    },
                                    new PluginMenuTranslationModel
                                    {
                                        LocaleName = LocaleNames.Danish,
                                        Name = "",//todo
                                        Language = LanguageNames.Danish,
                                    },
                                    new PluginMenuTranslationModel
                                    {
                                        LocaleName = LocaleNames.Ukrainian,
                                        Name = "Області",
                                        Language = LanguageNames.Ukrainian,
                                    }
                                }
                            },
                            Translations = new List<PluginMenuTranslationModel>
                            {
                                new PluginMenuTranslationModel
                                {
                                    LocaleName = LocaleNames.English,
                                    Name = "Areas",
                                    Language = LanguageNames.English,
                                },
                                new PluginMenuTranslationModel
                                {
                                    LocaleName = LocaleNames.German,
                                    Name = "",//todo
                                    Language = LanguageNames.German,
                                },
                                new PluginMenuTranslationModel
                                {
                                    LocaleName = LocaleNames.Danish,
                                    Name = "",//todo
                                    Language = LanguageNames.Danish,
                                },
                                new PluginMenuTranslationModel
                                {
                                    LocaleName = LocaleNames.Ukrainian,
                                    Name = "Області",
                                    Language = LanguageNames.Ukrainian,
                                }
                            }
                        }
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
                    new MenuItemModel
                    {
                        Name = localizationService?.GetString("Properties"),
                        E2EId = "backend-configuration-properties",
                        Link = "/plugins/backend-configuration/properties",
                        Guards = new List<string>(),
                        Position = 0,
                    },
                    new MenuItemModel
                    {
                        Name = localizationService?.GetString("Workers"),
                        E2EId = "backend-configuration-workers",
                        Link = "/plugins/backend-configuration/workers",
                        Guards = new List<string>(),
                        Position = 1,
                    },
                    new MenuItemModel
                    {
                        Name = localizationService?.GetString("Areas"),
                        E2EId = "backend-configuration-areas",
                        Link = "/plugins/backend-configuration/areas",
                        Position = 2,
                    }
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
