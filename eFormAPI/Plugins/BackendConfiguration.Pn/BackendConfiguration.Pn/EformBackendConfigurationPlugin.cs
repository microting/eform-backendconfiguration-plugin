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


using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microting.EformAngularFrontendBase.Infrastructure.Data.Factories;
using QuestPDF.Infrastructure;
using Sentry;

namespace BackendConfiguration.Pn
{
    using ChemicalsBase.Infrastructure;
    using ChemicalsBase.Infrastructure.Data.Factories;
    using eFormCore;
    using Infrastructure.Data.Seed;
    using Infrastructure.Data.Seed.Data;
    using Infrastructure.Models.Settings;
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
    using Services.BackendConfigurationFilesService;
    using Services.BackendConfigurationFileTagsService;
    using Services.BackendConfigurationLocalizationService;
    using Services.BackendConfigurationPropertiesService;
    using Services.BackendConfigurationPropertyAreasService;
    using Services.BackendConfigurationReportService;
    using Services.BackendConfigurationStatsService;
    using Services.BackendConfigurationTaskManagementService;
    using Services.BackendConfigurationTaskTrackerService;
    using Services.BackendConfigurationTaskWizardService;
    using Services.ChemicalService;
    using Services.ExcelService;
    using Services.RebusService;
    using Services.WordService;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Threading.Tasks;
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
            services.AddTransient<IBackendConfigurationAreaRulePlanningsService, BackendConfigurationAreaRulePlanningsService>();
            services.AddTransient<IBackendConfigurationAssignmentWorkerService, BackendConfigurationAssignmentWorkerService>();
            services.AddTransient<IBackendConfigurationTaskManagementService, BackendConfigurationTaskManagementService>();
            services.AddTransient<IBackendConfigurationPropertyAreasService, BackendConfigurationPropertyAreasService>();
            services.AddSingleton<IBackendConfigurationLocalizationService, BackendConfigurationLocalizationService>();
            services.AddTransient<IBackendConfigurationCompliancesService, BackendConfigurationCompliancesService>();
            services.AddTransient<IBackendConfigurationTaskTrackerService, BackendConfigurationTaskTrackerService>();
            services.AddTransient<IBackendConfigurationPropertiesService, BackendConfigurationPropertiesService>();
            services.AddTransient<IBackendConfigurationTaskWizardService, BackendConfigurationTaskWizardService>();
            services.AddTransient<IBackendConfigurationAreaRulesService, BackendConfigurationAreaRulesService>();
            services.AddTransient<IBackendConfigurationDocumentService, BackendConfigurationDocumentService>();
            services.AddTransient<IBackendConfigurationReportService, BackendConfigurationReportService>();
            services.AddTransient<IBackendConfigurationFilesService, BackendConfigurationFilesService>();
            services.AddTransient<IBackendConfigurationStatsService, BackendConfigurationStatsService>();
            services.AddTransient<IBackendConfigurationCaseService, BackendConfigurationCaseService>();
            services.AddTransient<IBackendConfigurationTagsService, BackendConfigurationTagsService>();
            services.AddTransient<IChemicalService, ChemicalService>();
            services.AddSingleton<IRebusService, RebusService>();
            services.AddTransient<IExcelService, ExcelService>();
            services.AddTransient<IWordService, WordService>();
            services.AddControllers();
            SeedEForms(services);
            QuestPDF.Settings.License = LicenseType.Community;
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

            var orgFields = await sdkDbContext.Fields.Where(x => x.OriginalId == "376935").ToListAsync();

            var englishLanguage = await sdkDbContext.Languages.FirstOrDefaultAsync(x => x.LanguageCode == "en-US");
            var germanLanguage = await sdkDbContext.Languages.FirstOrDefaultAsync(x => x.LanguageCode == "de-DE");

            foreach (var orgField in orgFields)
            {
	            var englishFt = await sdkDbContext.FieldTranslations.FirstOrDefaultAsync(x =>
		            x.FieldId == orgField.Id && x.LanguageId == englishLanguage.Id);

	            if (englishFt == null)
	            {
		            var fieldTranslation = new FieldTranslation
		            {
			            FieldId = orgField.Id,
			            LanguageId = englishLanguage.Id,
			            Text = "Priority",
			            Description = ""
		            };
		            await fieldTranslation.Create(sdkDbContext);
	            }

	            var germanFt = await sdkDbContext.FieldTranslations.FirstOrDefaultAsync(x =>
		            x.FieldId == orgField.Id && x.LanguageId == germanLanguage.Id);

	            if (germanFt == null)
	            {
		            var fieldTranslation = new FieldTranslation
		            {
			            FieldId = orgField.Id,
			            LanguageId = germanLanguage.Id,
			            Text = "Priorität",
			            Description = ""
		            };
		            await fieldTranslation.Create(sdkDbContext);
	            }

            }


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
                        List<string> hiddenIds =
                        [
                            "1412",
                            "142663new",
                            "142663new2",
                            "142664new",
                            "142664new2",
                            "142665",
                            "8756",
                            "142720",
                            "142663",
                            "142352",
                            "142664",
                            "142778m",
                            "142779m",
                            "22",
                            "142782m",
                            "142786m",
                            "142780m",
                            "142781m",
                            "11142660",
                            "11142661",
                            "142673",
                            "111142690",
                            "142698",
                            "142674",
                            "142691",
                            "142699",
                            "142675",
                            "142692",
                            "142670o",
                            "142676",
                            "142693",
                            "142701",
                            "142677",
                            "142694",
                            "142702",
                            "142678",
                            "142695",
                            "142679",
                            "142696",
                            "142704",
                            "142680",
                            "142697",
                            "142705",
                            "142681",
                            "142706",
                            "142682",
                            "142707",
                            "142683",
                            "142708",
                            "142684",
                            "142709",
                            "142685",
                            "142710",
                            "142686",
                            "142711",
                            "142687",
                            "142712",
                            "142688",
                            "142713",
                            "11142689",
                            "142714",
                            "142715",
                            "11142716",
                            "142717",
                            "142718",
                            "142703",
                            "142207",
                            "142266",
                            "142798",
                            "142722",
                            "142269",
                            "142267",
                            "142180",
                            "142060",
                            "142799",
                            "142801",
                            "142288",
                            "142803",
                            "142226",
                            "142271",
                            "142804",
                            "142270",
                            "142589",
                            "142237",
                            "142236",
                            "142805",
                            "142590",
                            "142256",
                            "142252",
                            "142806",
                            "142807",
                            "142592",
                            "142591",
                            "142445",
                            "142399",
                            "142809",
                            "142265",
                            "142262",
                            "142264",
                            "142810",
                            "142263",
                            "142811",
                            "142813",
                            "142401a",
                            "142593",
                            "142594",
                            "142595",
                            "142348",
                            "142425",
                            "142381",
                            "142212",
                            "142243",
                            "142742",
                            "142621",
                            "142622",
                            "142623",
                            "142624",
                            "142625",
                            "142626",
                            "142627",
                            "142628",
                            "142629",
                            "142630",
                            "142631",
                            "142632",
                            "142633",
                            "142634",
                            "142635",
                            "142636",
                            "142637",
                            "142638",
                            "142639",
                            "142640",
                            "142641",
                            "142642",
                            "142660",
                            "142661",
                            "A142642",
                            "B142642",
                            "C142642",
                            "D142642",
                            "E142642",
                            "F142642",
                            "G142642",
                            "H142642",
                            "I142642",
                            "142401",
                            "142401a",
                            "142244",
                            "142242",
                            "142700",
                            "142721",
                            "1412",
                            "142802",
                            "142142"
                        ];
                        string contents;
                        using (var sr = new StreamReader(resourceStream))
                        {
                            contents = await sr.ReadToEndAsync().ConfigureAwait(false);
                        }

                        switch (eformName)
                        {
                            case "05. Halebid og risikovurdering":
                                contents = contents.Replace("SOURCE_REPLACE_ME", "123");
                                break;
                            case "01. Aflæsninger":
                            case "02. Fækale uheld":
                                contents = contents.Replace("REPLACE_ME", "123");
                                break;
                            case "25.01 Registrer produkter":
                                contents = contents.Replace("SOURCE_REPLACE_ME_2", "123");
                                contents = contents.Replace("SOURCE_REPLACE_ME", "456");
                                break;
                        }

                        var newTemplate = await core.TemplateFromXml(contents).ConfigureAwait(false);
                        if (!await sdkDbContext.CheckLists
                                .AnyAsync(x => x.OriginalId == newTemplate.OriginalId
                                               && x.WorkflowState != Microting.eForm.Infrastructure.Constants.Constants
                                                   .WorkflowStates.Removed).ConfigureAwait(false))
                        {
                            var clId = await core.TemplateCreate(newTemplate).ConfigureAwait(false);
                            var cl = await sdkDbContext.CheckLists.SingleAsync(x => x.Id == clId).ConfigureAwait(false);
                            cl.IsHidden = hiddenIds.Contains(cl.OriginalId);
                            cl.IsLocked = true;
                            cl.IsEditable = false;
                            cl.ReportH1 = eform[0];
                            cl.ReportH2 = eform[1];
                            cl.ReportH3 = eform.Count == 3 ? eform[2] : "";
                            cl.ReportH4 = eform.Count == 4 ? eform[3] : "";
                            cl.IsDoneAtEditable = true;
                            cl.QuickSyncEnabled = 1;
                            await cl.Update(sdkDbContext).ConfigureAwait(false);
                            var subCl = await sdkDbContext.CheckLists.SingleAsync(x => x.ParentId == cl.Id)
                                .ConfigureAwait(false);
                            subCl.QuickSyncEnabled = 1;
                            await subCl.Update(sdkDbContext).ConfigureAwait(false);
                        }
                        else
                        {
                            try
                            {
                                var cl = await sdkDbContext.CheckLists.SingleAsync(x =>
                                    x.OriginalId == newTemplate.OriginalId && x.ParentId == null &&
                                    x.WorkflowState != Microting.eForm.Infrastructure.Constants.Constants.WorkflowStates
                                        .Removed).ConfigureAwait(false);
                                cl.IsHidden = hiddenIds.Contains(cl.OriginalId);
                                cl.IsLocked = true;
                                cl.IsEditable = false;
                                cl.ReportH1 = eform[0];
                                cl.ReportH2 = eform[1];
                                cl.ReportH3 = eform.Count == 3 ? eform[2] : "";
                                cl.ReportH4 = eform.Count == 4 ? eform[3] : "";
                                cl.IsDoneAtEditable = true;
                                cl.QuickSyncEnabled = 1;
                                await cl.Update(sdkDbContext).ConfigureAwait(false);
                                var subCl = await sdkDbContext.CheckLists.SingleAsync(x => x.ParentId == cl.Id)
                                    .ConfigureAwait(false);
                                subCl.QuickSyncEnabled = 1;
                                await subCl.Update(sdkDbContext).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine(ex.Message);
                            }
                        }

                        foreach (var hiddenId in hiddenIds)
                        {
                            var hiddenCls = await sdkDbContext.CheckLists.Where(x =>
                                x.OriginalId == hiddenId && x.IsHidden == false).ToListAsync();
                            foreach (var hiddenCl in hiddenCls)
                            {
                                if (hiddenCl != null)
                                {
                                    hiddenCl.IsHidden = true;
                                    await hiddenCl.Update(sdkDbContext).ConfigureAwait(false);
                                }
                            }
                        }

                        var clsToHide = new List<string>
                        {
                            "eform-angular-work-orders-plugin-tasklist",
                            "eform-angular-work-orders-plugin-newtask"
                        };

                        foreach (var hiddenId in clsToHide)
                        {
                            var hiddenClts = await sdkDbContext.CheckListTranslations.Where(x =>
                                x.Text == hiddenId ).ToListAsync();
                            foreach (var hiddenCl in hiddenClts)
                            {
                                var cl = await sdkDbContext.CheckLists.FirstOrDefaultAsync(x => x.Id == hiddenCl.CheckListId);
                                if (cl != null)
                                {
                                    cl.IsHidden = true;
                                    await cl.Update(sdkDbContext).ConfigureAwait(false);
                                }
                            }
                        }
                    }
                }
                catch (Exception exception)
                {
                    Console.WriteLine(exception.Message);
                }
            }

            var powerToolCheckList = await sdkDbContext.CheckListTranslations.FirstOrDefaultAsync(x => x.Text == "Elvætktøj");
            if (powerToolCheckList != null)
            {
                powerToolCheckList.Text = "Elværktøj";
                await powerToolCheckList.Update(sdkDbContext);
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

            var fieldLabels = await sdkDbContext.FieldTranslations.Where(x => x.Text == "VÃ¦rktÃ¸jshus og hÃ¥ndtag OK").ToListAsync();
            foreach (var fieldLabel in fieldLabels)
            {
                fieldLabel.Text = "Værktøjshus og håndtag OK";
                await fieldLabel.Update(sdkDbContext);
            }

            fieldLabels = await sdkDbContext.FieldTranslations.Where(x => x.Text == "KÃ¦der OK").ToListAsync();
            foreach (var fieldLabel in fieldLabels)
            {
                fieldLabel.Text = "Kæder OK";
                await fieldLabel.Update(sdkDbContext);
            }

            // Seed areas
            foreach (var newArea in BackendConfigurationSeedAreas.AreasSeed
	                     .Where(newArea => !context.Areas.Any(x => x.Id == newArea.Id))
	                     .Where(x => x.IsDisabled == false))
            {
                await newArea.Create(context).ConfigureAwait(false);
            }

            var tailBitPlanningTag = await itemsPlanningContext.PlanningTags.Where(x => x.Name == "Halebid").FirstOrDefaultAsync();

            if (tailBitPlanningTag != null)
            {
                var plannings = await itemsPlanningContext.Plannings
                    .Where(x => x.RelatedEFormName.Contains("Halebid"))
                    .Where(x => x.ReportGroupPlanningTagId == null)
                    .ToListAsync();

                foreach (var planning in plannings)
                {
                    planning.ReportGroupPlanningTagId = tailBitPlanningTag.Id;
                    await planning.Update(itemsPlanningContext);
                }
            }
        }

		public void ConfigureDbContext(IServiceCollection services, string connectionString)
        {
            SentrySdk.Init(options =>
            {
                // A Sentry Data Source Name (DSN) is required.
                // See https://docs.sentry.io/product/sentry-basics/dsn-explainer/
                // You can set it in the SENTRY_DSN environment variable, or you can set it in code here.
                options.Dsn = "https://d07e105f7f60b749142c7883f0b9f2df@o4506241219428352.ingest.sentry.io/4506285252739072";

                // When debug is enabled, the Sentry client will emit detailed debugging information to the console.
                // This might be helpful, or might interfere with the normal operation of your application.
                // We enable it here for demonstration purposes when first trying Sentry.
                // You shouldn't do this in your applications unless you're troubleshooting issues with Sentry.
                options.Debug = false;

                // This option is recommended. It enables Sentry's "Release Health" feature.
                options.AutoSessionTracking = true;

                // This option is recommended for client applications only. It ensures all threads use the same global scope.
                // If you're writing a background service of any kind, you should remove this.
                options.IsGlobalModeEnabled = true;

                // This option will enable Sentry's tracing features. You still need to start transactions and spans.
                options.EnableTracing = true;
            });

            string pattern = @"Database=(\d+)_eform-backend-configuration-plugin;";
            Match match = Regex.Match(connectionString!, pattern);

            if (match.Success)
            {
                string numberString = match.Groups[1].Value;
                int number = int.Parse(numberString);
                SentrySdk.ConfigureScope(scope =>
                {
                    scope.SetTag("customerNo", number.ToString());
                    Console.WriteLine("customerNo: " + number);
                    scope.SetTag("osVersion", Environment.OSVersion.ToString());
                    Console.WriteLine("osVersion: " + Environment.OSVersion);
                    scope.SetTag("osArchitecture", RuntimeInformation.OSArchitecture.ToString());
                    Console.WriteLine("osArchitecture: " + RuntimeInformation.OSArchitecture);
                    scope.SetTag("osName", RuntimeInformation.OSDescription);
                    Console.WriteLine("osName: " + RuntimeInformation.OSDescription);
                });
            }

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
            var chemicalsDbContext = chemicalsContextFactory.CreateDbContext([chemicalBaseConnectionString]);
            chemicalsDbContext.Database.Migrate();

            var caseTemplatePnContextFactory = new CaseTemplatePnContextFactory();
            var caseTemplateContext = caseTemplatePnContextFactory.CreateDbContext([documentsConnectionString]);
            caseTemplateContext.Database.Migrate();

            var contextFactory = new BackendConfigurationPnContextFactory();
            var context = contextFactory.CreateDbContext([connectionString]);
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
                    Name = "Properties",
                    E2EId = "backend-configuration-pn-properties",
                    Link = "/plugins/backend-configuration-pn/properties",
                    Type = MenuItemTypeEnum.Link,
                    Position = 8,
                    MenuTemplate = new PluginMenuTemplateModel
                    {
                        Name = "Properties",
                        E2EId = "backend-configuration-pn-properties",
                        DefaultLink = "/plugins/backend-configuration-pn/properties",
                        Permissions = [],
                        Translations =
                        [
                            new()
                            {
                                LocaleName = LocaleNames.English,
                                Name = "Properties",
                                Language = LanguageNames.English
                            },

                            new()
                            {
                                LocaleName = LocaleNames.German,
                                Name = "Eigenschaften",
                                Language = LanguageNames.German
                            },

                            new()
                            {
                                LocaleName = LocaleNames.Danish,
                                Name = "Ejendomme",
                                Language = LanguageNames.Danish
                            },

                            new()
                            {
                                LocaleName = LocaleNames.Ukrainian,
                                Name = "Властивості",
                                Language = LanguageNames.Ukrainian
                            }
                        ]
                    },
                    Translations =
                    [
                        new()
                        {
                            LocaleName = LocaleNames.English,
                            Name = "Properties",
                            Language = LanguageNames.English
                        },

                        new()
                        {
                            LocaleName = LocaleNames.German,
                            Name = "Eigenschaften",
                            Language = LanguageNames.German
                        },

                        new()
                        {
                            LocaleName = LocaleNames.Danish,
                            Name = "Ejendomme",
                            Language = LanguageNames.Danish
                        },

                        new()
                        {
                            LocaleName = LocaleNames.Ukrainian,
                            Name = "Властивості",
                            Language = LanguageNames.Ukrainian
                        }
                    ]
                },
                new()
                {
                    Name = "Workers",
                    E2EId = "backend-configuration-pn-property-workers",
                    Link = "/plugins/backend-configuration-pn/property-workers",
                    Type = MenuItemTypeEnum.Link,
                    Position = 7,
                    MenuTemplate = new PluginMenuTemplateModel
                    {
                        Name = "Workers",
                        E2EId = "backend-configuration-pn-property-workers",
                        DefaultLink = "/plugins/backend-configuration-pn/property-workers",
                        Permissions = [],
                        Translations =
                        [
                            new()
                            {
                                LocaleName = LocaleNames.English,
                                Name = "Workers",
                                Language = LanguageNames.English
                            },

                            new()
                            {
                                LocaleName = LocaleNames.German,
                                Name = "Mitarbeiter",
                                Language = LanguageNames.German
                            },

                            new()
                            {
                                LocaleName = LocaleNames.Danish,
                                Name = "Medarbejdere",
                                Language = LanguageNames.Danish
                            },

                            new()
                            {
                                LocaleName = LocaleNames.Ukrainian,
                                Name = "Працівники",
                                Language = LanguageNames.Ukrainian
                            }
                        ]
                    },
                    Translations =
                    [
                        new()
                        {
                            LocaleName = LocaleNames.English,
                            Name = "Workers",
                            Language = LanguageNames.English
                        },

                        new()
                        {
                            LocaleName = LocaleNames.German,
                            Name = "Mitarbeiter",
                            Language = LanguageNames.German
                        },

                        new()
                        {
                            LocaleName = LocaleNames.Danish,
                            Name = "Medarbejdere",
                            Language = LanguageNames.Danish
                        },

                        new()
                        {
                            LocaleName = LocaleNames.Ukrainian,
                            Name = "Працівники",
                            Language = LanguageNames.Ukrainian
                        }
                    ]
                },
                new()
                {
                    Name = "Task management",
                    E2EId = "backend-configuration-pn-task-management",
                    Link = "/plugins/backend-configuration-pn/task-management",
                    Type = MenuItemTypeEnum.Link,
                    Position = 4,
                    MenuTemplate = new PluginMenuTemplateModel
                    {
                        Name = "Task management",
                        E2EId = "backend-configuration-pn-task-management",
                        DefaultLink = "/plugins/backend-configuration-pn/task-management",
                        Permissions = [],
                        Translations =
                        [
                            new()
                            {
                                LocaleName = LocaleNames.English,
                                Name = "Ad-hoc-tasks",
                                Language = LanguageNames.English
                            },

                            new()
                            {
                                LocaleName = LocaleNames.German,
                                Name = "Ad-hoc-Aufgaben",
                                Language = LanguageNames.German
                            },

                            new()
                            {
                                LocaleName = LocaleNames.Danish,
                                Name = "Ad-hoc-opgaver",
                                Language = LanguageNames.Danish
                            },

                            new()
                            {
                                LocaleName = LocaleNames.Ukrainian,
                                Name = "Адміністративні завдання",
                                Language = LanguageNames.Ukrainian
                            }
                        ]
                    },
                    Translations =
                    [
                        new()
                        {
                            LocaleName = LocaleNames.English,
                            Name = "Ad-hoc-tasks",
                            Language = LanguageNames.English
                        },

                        new()
                        {
                            LocaleName = LocaleNames.German,
                            Name = "Ad-hoc-Aufgaben",
                            Language = LanguageNames.German
                        },

                        new()
                        {
                            LocaleName = LocaleNames.Danish,
                            Name = "Ad-hoc-opgaver",
                            Language = LanguageNames.Danish
                        },

                        new()
                        {
                            LocaleName = LocaleNames.Ukrainian,
                            Name = "Адміністративні завдання",
                            Language = LanguageNames.Ukrainian
                        }
                    ]
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
                        Permissions = [],
                        Translations =
                        [
                            new()
                            {
                                LocaleName = LocaleNames.English,
                                Name = "Reports",
                                Language = LanguageNames.English
                            },

                            new()
                            {
                                LocaleName = LocaleNames.German,
                                Name = "Berichte",
                                Language = LanguageNames.German
                            },

                            new()
                            {
                                LocaleName = LocaleNames.Danish,
                                Name = "Rapporter",
                                Language = LanguageNames.Danish
                            },

                            new()
                            {
                                LocaleName = LocaleNames.Ukrainian,
                                Name = "Звіти",
                                Language = LanguageNames.Ukrainian
                            }
                        ]
                    },
                    Translations =
                    [
                        new()
                        {
                            LocaleName = LocaleNames.English,
                            Name = "Reports",
                            Language = LanguageNames.English
                        },

                        new()
                        {
                            LocaleName = LocaleNames.German,
                            Name = "Berichte",
                            Language = LanguageNames.German
                        },

                        new()
                        {
                            LocaleName = LocaleNames.Danish,
                            Name = "Rapporter",
                            Language = LanguageNames.Danish
                        },

                        new()
                        {
                            LocaleName = LocaleNames.Ukrainian,
                            Name = "Звіти",
                            Language = LanguageNames.Ukrainian
                        }
                    ]
                },
                new()
                {
                    Name = "Documents",
                    E2EId = "backend-configuration-pn-documents",
                    Link = "/plugins/backend-configuration-pn/documents",
                    Type = MenuItemTypeEnum.Link,
                    Position = 6,
                    MenuTemplate = new PluginMenuTemplateModel
                    {
                        Name = "Documents",
                        E2EId = "backend-configuration-pn-documents",
                        DefaultLink = "/plugins/backend-configuration-pn/documents",
                        Permissions = [],
                        Translations =
                        [
                            new()
                            {
                                LocaleName = LocaleNames.English,
                                Name = "Word-documents",
                                Language = LanguageNames.English
                            },

                            new()
                            {
                                LocaleName = LocaleNames.German,
                                Name = "Word-dokumente",
                                Language = LanguageNames.German
                            },

                            new()
                            {
                                LocaleName = LocaleNames.Danish,
                                Name = "Word-dokumenter",
                                Language = LanguageNames.Danish
                            },

                            new()
                            {
                                LocaleName = LocaleNames.Ukrainian,
                                Name = "Word-документи",
                                Language = LanguageNames.Ukrainian
                            }
                        ]
                    },
                    Translations =
                    [
                        new()
                        {
                            LocaleName = LocaleNames.English,
                            Name = "Word-documents",
                            Language = LanguageNames.English
                        },

                        new()
                        {
                            LocaleName = LocaleNames.German,
                            Name = "Word-dokumente",
                            Language = LanguageNames.German
                        },

                        new()
                        {
                            LocaleName = LocaleNames.Danish,
                            Name = "Word-dokumenter",
                            Language = LanguageNames.Danish
                        },

                        new()
                        {
                            LocaleName = LocaleNames.Ukrainian,
                            Name = "Word-документи",
                            Language = LanguageNames.Ukrainian
                        }
                    ]
                },
                new()
                {
                    Name = "PDF-archive",
                    E2EId = "backend-configuration-pn-files",
                    Link = "/plugins/backend-configuration-pn/files",
                    Type = MenuItemTypeEnum.Link,
                    Position = 5,
                    MenuTemplate = new PluginMenuTemplateModel
                    {
                        Name = "PDF-archive",
                        E2EId = "backend-configuration-pn-files",
                        DefaultLink = "/plugins/backend-configuration-pn/files",
                        Permissions = [],
                        Translations =
                        [
                            new()
                            {
                                LocaleName = LocaleNames.English,
                                Name = "PDF-archive",
                                Language = LanguageNames.English
                            },

                            new()
                            {
                                LocaleName = LocaleNames.German,
                                Name = "PDF-arkiv",
                                Language = LanguageNames.German
                            },

                            new()
                            {
                                LocaleName = LocaleNames.Danish,
                                Name = "PDF-arkiv",
                                Language = LanguageNames.Danish
                            },

                            new()
                            {
                                LocaleName = LocaleNames.Ukrainian,
                                Name = "PDF-архів",
                                Language = LanguageNames.Ukrainian
                            }
                        ]
                    },
                    Translations =
                    [
                        new()
                        {
                            LocaleName = LocaleNames.English,
                            Name = "PDF-archive",
                            Language = LanguageNames.English
                        },

                        new()
                        {
                            LocaleName = LocaleNames.German,
                            Name = "PDF-arkiv",
                            Language = LanguageNames.German
                        },

                        new()
                        {
                            LocaleName = LocaleNames.Danish,
                            Name = "PDF-arkiv",
                            Language = LanguageNames.Danish
                        },

                        new()
                        {
                            LocaleName = LocaleNames.Ukrainian,
                            Name = "PDF-архів",
                            Language = LanguageNames.Ukrainian
                        }
                    ]
                },
                new()
                {
                    Name = "Active planned tasks",
                    E2EId = "backend-configuration-pn-task-tracker",
                    Link = "/plugins/backend-configuration-pn/task-tracker",
                    Type = MenuItemTypeEnum.Link,
                    Position = 3,
                    MenuTemplate = new PluginMenuTemplateModel
                    {
                        Name = "Active planned tasks",
                        E2EId = "backend-configuration-pn-task-tracker",
                        DefaultLink = "/plugins/backend-configuration-pn/task-tracker",
                        Permissions = [],
                        Translations =
                        [
                            new()
                            {
                                LocaleName = LocaleNames.English,
                                Name = "Active planned tasks",
                                Language = LanguageNames.English
                            },

                            new()
                            {
                                LocaleName = LocaleNames.German,
                                Name = "Aktive geplante Aufgaben",
                                Language = LanguageNames.German
                            },

                            new()
                            {
                                LocaleName = LocaleNames.Danish,
                                Name = "Aktive planlagte opgaver",
                                Language = LanguageNames.Danish
                            },

                            new()
                            {
                                LocaleName = LocaleNames.Ukrainian,
                                Name = "Активні заплановані завдання",
                                Language = LanguageNames.Ukrainian
                            }
                        ]
                    },
                    Translations =
                    [
                        new()
                        {
                            LocaleName = LocaleNames.English,
                            Name = "Task tracker",
                            Language = LanguageNames.English
                        },

                        new()
                        {
                            LocaleName = LocaleNames.German,
                            Name = "Aufgabenverfolgung",
                            Language = LanguageNames.German
                        },

                        new()
                        {
                            LocaleName = LocaleNames.Danish,
                            Name = "Aktive planlagte opgaver",
                            Language = LanguageNames.Danish
                        },

                        new()
                        {
                            LocaleName = LocaleNames.Ukrainian,
                            Name = "Відстежувач завдань",
                            Language = LanguageNames.Ukrainian
                        }
                    ]
                },
                new()
                {
                    Name = "All planned tasks",
                    E2EId = "backend-configuration-pn-task-wizard",
                    Link = "/plugins/backend-configuration-pn/task-wizard",
                    Type = MenuItemTypeEnum.Link,
                    Position = 2,
                    MenuTemplate = new PluginMenuTemplateModel
                    {
                        Name = "All planned tasks",
                        E2EId = "backend-configuration-pn-task-wizard",
                        DefaultLink = "/plugins/backend-configuration-pn/task-wizard",
                        Permissions = [],
                        Translations =
                        [
                            new()
                            {
                                LocaleName = LocaleNames.English,
                                Name = "All planned tasks",
                                Language = LanguageNames.English
                            },

                            new()
                            {
                                LocaleName = LocaleNames.German,
                                Name = "Alle geplanten Aufgaben",
                                Language = LanguageNames.German
                            },

                            new()
                            {
                                LocaleName = LocaleNames.Danish,
                                Name = "Alle planlagte opgaver",
                                Language = LanguageNames.Danish
                            },

                            new()
                            {
                                LocaleName = LocaleNames.Ukrainian,
                                Name = "Всі заплановані завдання",
                                Language = LanguageNames.Ukrainian
                            }
                        ]
                    },
                    Translations =
                    [
                        new()
                        {
                            LocaleName = LocaleNames.English,
                            Name = "All planned tasks",
                            Language = LanguageNames.English
                        },

                        new()
                        {
                            LocaleName = LocaleNames.German,
                            Name = "Alle geplanten Aufgaben",
                            Language = LanguageNames.German
                        },

                        new()
                        {
                            LocaleName = LocaleNames.Danish,
                            Name = "Alle planlagte opgaver",
                            Language = LanguageNames.Danish
                        },

                        new()
                        {
                            LocaleName = LocaleNames.Ukrainian,
                            Name = "Всі заплановані завдання",
                            Language = LanguageNames.Ukrainian
                        }
                    ]
                },
                new()
                {
                    Name = "Dashboard",
                    E2EId = "backend-configuration-pn-statistics",
                    Link = "/plugins/backend-configuration-pn/statistics",
                    Type = MenuItemTypeEnum.Link,
                    Position = 0,
                    MenuTemplate = new PluginMenuTemplateModel
                    {
                        Name = "Dashboard",
                        E2EId = "backend-configuration-pn-statistics",
                        DefaultLink = "/plugins/backend-configuration-pn/statistics",
                        Permissions = [],
                        Translations =
                        [
                            new()
                            {
                                LocaleName = LocaleNames.English,
                                Name = "Dashboard",
                                Language = LanguageNames.English
                            },

                            new()
                            {
                                LocaleName = LocaleNames.German,
                                Name = "Ûberblick",
                                Language = LanguageNames.German
                            },

                            new()
                            {
                                LocaleName = LocaleNames.Danish,
                                Name = "Overblik",
                                Language = LanguageNames.Danish
                            },

                            new()
                            {
                                LocaleName = LocaleNames.Ukrainian,
                                Name = "Дашборд",
                                Language = LanguageNames.Ukrainian
                            }
                        ]
                    },
                    Translations =
                    [
                        new()
                        {
                            LocaleName = LocaleNames.English,
                            Name = "Dashboard",
                            Language = LanguageNames.English
                        },

                        new()
                        {
                            LocaleName = LocaleNames.German,
                            Name = "Ûberblick",
                            Language = LanguageNames.German
                        },

                        new()
                        {
                            LocaleName = LocaleNames.Danish,
                            Name = "Overblik",
                            Language = LanguageNames.Danish
                        },

                        new()
                        {
                            LocaleName = LocaleNames.Ukrainian,
                            Name = "Дашборд",
                            Language = LanguageNames.Ukrainian
                        }
                    ]
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
                Guards = [BackendConfigurationClaims.AccessBackendConfigurationPlugin],
                MenuItems =
                [
                    new()
                    {
                        Name = localizationService?.GetString("Properties"),
                        E2EId = "backend-configuration-properties",
                        Link = "/plugins/backend-configuration/properties",
                        Guards = [],
                        Position = 0
                    },

                    new()
                    {
                        Name = localizationService?.GetString("Property workers"),
                        E2EId = "backend-configuration-workers",
                        Link = "/plugins/backend-configuration/workers",
                        Guards = [],
                        Position = 1
                    },

                    new()
                    {
                        Name = localizationService?.GetString("Task management"),
                        E2EId = "backend-configuration-task-management",
                        Link = "/plugins/backend-configuration/task-management",
                        Guards = [],
                        Position = 2
                    },

                    new()
                    {
                        Name = localizationService?.GetString("Documents"),
                        E2EId = "backend-configuration-documents",
                        Link = "/plugins/backend-configuration/documents",
                        Guards = [],
                        Position = 3
                    },

                    new()
                    {
                        Name = localizationService?.GetString("Files"),
                        E2EId = "backend-configuration-files",
                        Link = "/plugins/backend-configuration/files",
                        Guards = [],
                        Position = 4
                    },

                    new()
                    {
                        Name = localizationService?.GetString("Task tracker"),
                        E2EId = "backend-configuration-task-tracker",
                        Link = "/plugins/backend-configuration/task-tracker",
                        Guards = [],
                        Position = 5
                    },

                    new()
                    {
                        Name = localizationService?.GetString("Task wizard"),
                        E2EId = "backend-configuration-task-wizard",
                        Link = "/plugins/backend-configuration/task-wizard",
                        Guards = [],
                        Position = 6
                    },

                    new()
                    {
                        Name = localizationService?.GetString("Statistics"),
                        E2EId = "backend-configuration-statistics",
                        Link = "/plugins/backend-configuration/statistics",
                        Guards = [],
                        Position = 7
                    }
                ]
            });
            return result;
        }

        public void SeedDatabase(string connectionString)
        {
            // Get DbContext
            var contextFactory = new BackendConfigurationPnContextFactory();
            using var context = contextFactory.CreateDbContext([connectionString]);
            // Seed configuration
            BackendConfigurationPluginSeed.SeedData(context);

            var angularDbConnectionString = connectionString.Replace(
                "eform-backend-configuration-plugin",
                "Angular");
            var baseDbContext = new BaseDbContextFactory();
            using var baseDb = baseDbContext.CreateDbContext([angularDbConnectionString]);

            // lookup the user role
            var userRole = baseDb.Roles.Single(x => x.Name == "user");
            // lookup "Access BackendConfiguration Plugin" permission for the plugin and give the user role access
            var permissionsToEnable = new List<string>
            {
                "Access BackendConfiguration Plugin",
                "Get properties",
                "Enable document management",
                "Enable task management",
                "Enable time registration"
            };
            foreach (var permissionToEnable in permissionsToEnable)
            {
                var backendConfigurationPluginPermission =
                    context.PluginPermissions.SingleOrDefault(x =>
                        x.PermissionName == permissionToEnable);
                if (backendConfigurationPluginPermission != null)
                {
                    // set PluginGroupPermissions to enabled for the user role
                    var userRolePluginPermissions = context.PluginGroupPermissions.FirstOrDefault(x =>
                        x.PermissionId == backendConfigurationPluginPermission.Id &&
                        x.GroupId == userRole.Id);
                    if (userRolePluginPermissions != null)
                    {
                        userRolePluginPermissions.IsEnabled = true;
                        context.SaveChanges();
                    }
                }
            }

        }

        public PluginPermissionsManager GetPermissionsManager(string connectionString)
        {
            var contextFactory = new BackendConfigurationPnContextFactory();
            var context = contextFactory.CreateDbContext([connectionString]);
			return new PluginPermissionsManager(context);
		}
	}
}