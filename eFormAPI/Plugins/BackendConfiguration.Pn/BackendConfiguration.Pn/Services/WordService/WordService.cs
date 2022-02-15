/*
The MIT License (MIT)

Copyright (c) 2007 - 2022 Microting A/S

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

namespace BackendConfiguration.Pn.Services.WordService
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text;
    using System.Threading.Tasks;
    using BackendConfigurationLocalizationService;
    using Infrastructure.Data.Seed.Data;
    using Infrastructure.Models.AreaRules;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;
    using Microting.eForm.Infrastructure.Constants;
    using Microting.eFormApi.BasePn.Abstractions;
    using Microting.eFormApi.BasePn.Infrastructure.Models.API;
    using Microting.EformBackendConfigurationBase.Infrastructure.Data;
    using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
    using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
    using Microting.ItemsPlanningBase.Infrastructure.Enums;

    public class WordService : IWordService
    {
        private readonly ILogger<WordService> _logger;
        private readonly IBackendConfigurationLocalizationService  _localizationService;
        private readonly IEFormCoreService _coreHelper;
        private readonly BackendConfigurationPnDbContext _dbContext;
        private readonly IUserService _userService;

        public WordService(
            ILogger<WordService> logger,
            IBackendConfigurationLocalizationService localizationService,
            IEFormCoreService coreHelper,
            BackendConfigurationPnDbContext dbContext,
            IUserService userService)
        {
            _logger = logger;
            _localizationService = localizationService;
            _coreHelper = coreHelper;
            _dbContext = dbContext;
            _userService = userService;
        }

        public async Task<OperationDataResult<Stream>> GenerateReport(int propertyId, int areaId, int year)
        {
            try
            {
                var property = await _dbContext.Properties
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .FirstOrDefaultAsync(x => x.Id == propertyId);
                var area = await _dbContext.Areas
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .FirstOrDefaultAsync(x => x.Id == areaId);
                var isPropertyAndAreaLinked = await _dbContext.AreaProperties
                    .Where(x => x.AreaId == areaId)
                    .Where(x => x.PropertyId == propertyId)
                    .AnyAsync();
                if (property == null)
                {
                    return new OperationDataResult<Stream>(
                        false,
                        _localizationService.GetString("PropertyNotFound"));
                }
                if (area == null)
                {
                    return new OperationDataResult<Stream>(
                        false,
                        _localizationService.GetString("AreaNotFound"));
                }
                if (!isPropertyAndAreaLinked)
                {
                    return new OperationDataResult<Stream>(
                        false,
                        _localizationService.GetString("PropertyAndAreaNotLinked"));
                }

                return area.Type switch
                {
                    AreaTypesEnum.Type7 => await GenerateReportType7(property, area, year),
                    _ => new OperationDataResult<Stream>(false,
                        _localizationService.GetString($"ReportFor{area.Type}NotSupported"))
                };
            }
            catch (Exception e)
            {
                Trace.TraceError(e.Message);
                _logger.LogError(e.Message);
                return new OperationDataResult<Stream>(
                    false,
                    _localizationService.GetString("ErrorWhileGenerateWordFile"));
            }
        }

        private async Task<OperationDataResult<Stream>> GenerateReportType7(Property property, Area area, int year)
        {
            var core = await _coreHelper.GetCore();
            var sdkDbContext = core.DbContextHelper.GetDbContext();
            var curentLanguage = await _userService.GetCurrentUserLanguage();
            if (curentLanguage.Name != "English" && curentLanguage.Name != "Danish") // reports only eng and da langs
            {
                curentLanguage = await sdkDbContext.Languages.FirstAsync(x => x.Name == "Danish");
            }
            var areaRulesForType7 = BackendConfigurationSeedAreas.AreaRulesForType7
                .GroupBy(x => x.FolderName)
                .Select(x => new AreaRulesForType7
                {
                    FolderName = x.Key,
                    AreaRuleNames = x.Select(y => y)
                        .Where(y => y.FolderName == x.Key)
                        .SelectMany(y => y.AreaRuleTranslations
                            .Where(z => z.LanguageId == curentLanguage.Id)
                            .Select(z => z.Name))
                        .ToList(),
                })
                .ToList();

            foreach (var areaRuleForType7 in areaRulesForType7)
            {
                areaRuleForType7.FolderName = await sdkDbContext.FolderTranslations
                    .OrderBy(x => x.Id)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Name == areaRuleForType7.FolderName)
                    .SelectMany(x => x.Folder.FolderTranslations)
                    .Where(x => x.LanguageId == curentLanguage.Id)
                    .Select(x => x.Name)
                    .LastOrDefaultAsync();
            }

            var areaRuleTranslations = await _dbContext.AreaRuleTranslations
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                //.Where(x => areaRulesForType7.SelectMany(y => y.AreaRuleNames).Any(y => y == x.Name))
                .Include(x => x.AreaRule)
                .ThenInclude(x => x.AreaRulesPlannings)
                .Where(x => x.AreaRule.PropertyId == property.Id)
                .Where(x => x.AreaRule.AreaId == area.Id)
                //.Select(x => x.AreaRule)
                .ToListAsync();
            
            // Read html and template
            var resourceString = "BackendConfiguration.Pn.Resources.Templates.WordExport.page.html";
            var assembly = Assembly.GetExecutingAssembly();
            var resourceStream = assembly.GetManifestResourceStream(resourceString);
            string html;
            using (var reader = new StreamReader(resourceStream ?? throw new InvalidOperationException($"{nameof(resourceStream)} is null")))
            {
                html = await reader.ReadToEndAsync();
            }

            resourceString = "BackendConfiguration.Pn.Resources.Templates.WordExport.file.docx";
            var docxFileResourceStream = assembly.GetManifestResourceStream(resourceString);
            if (docxFileResourceStream == null)
            {
                throw new InvalidOperationException($"{nameof(docxFileResourceStream)} is null");
            }
            var docxFileStream = new MemoryStream();
            await docxFileResourceStream.CopyToAsync(docxFileStream);

            var itemsHtml = new StringBuilder();
            itemsHtml.Append(@"<body style='font-family:Calibri;'>");
            itemsHtml.Append($@"<p style='font-size:11pt;text-align:left;font-weight:bold;'>23. {_localizationService.GetString("Controlplan IE-reporting")}</p>");
            itemsHtml.Append(@"<table width=""100%"" border=""1"">");
            itemsHtml.Append(@"<tr style='font-weight:bold;font-size:11pt;'>");
            itemsHtml.Append($@"<td>{_localizationService.GetString("Year")}</td>");
            itemsHtml.Append($@"<td>{_localizationService.GetString("CVR-no")}</td>");
            itemsHtml.Append($@"<td>{_localizationService.GetString("CHR-no")}</td>");
            itemsHtml.Append($@"<td>{_localizationService.GetString("Name")}</td>");
            itemsHtml.Append($@"<td>{_localizationService.GetString("Address")}</td>");
            itemsHtml.Append(@"</tr>");
            itemsHtml.Append(@"<tr style='font-size:11pt;'>");
            itemsHtml.Append($@"<td>{year}</td>");
            itemsHtml.Append($@"<td>{(string.IsNullOrEmpty(property.CVR) ? "" : property.CVR)}</td>");
            itemsHtml.Append($@"<td>{(string.IsNullOrEmpty(property.CHR) ? "" : property.CHR)}</td>");
            itemsHtml.Append($@"<td>{(string.IsNullOrEmpty(property.Name) ? "" : property.Name)}</td>");
            itemsHtml.Append($@"<td>{(string.IsNullOrEmpty(property.Address) ? "" : property.Address)}</td>");
            itemsHtml.Append(@"</tr>");
            itemsHtml.Append(@"</table>");
            itemsHtml.Append(@"<p></p>"); // enter

            itemsHtml.Append(@"<table width=""100%"" border=""1"">");
            // Table header
            itemsHtml.Append(@"<tr style='font-weight:bold;font-size:9pt;'>");
            itemsHtml.Append(@"<td></td>");
            itemsHtml.Append($@"<td>{_localizationService.GetString("StartDate")}</td>");
            itemsHtml.Append($@"<td>{_localizationService.GetString("Frequence")}</td>");
            itemsHtml.Append(@"</tr>");

            foreach (var areaRuleForType7 in areaRulesForType7)
            {
                itemsHtml.Append(@"<tr style='background-color:#d0cece;font-weight:bold;font-size:9pt;'>");
                itemsHtml.Append($@"<td>{areaRuleForType7.FolderName}</td>");
                itemsHtml.Append(@"<td></td>");
                itemsHtml.Append(@"<td></td>");
                itemsHtml.Append(@"</tr>");
                foreach (var areaRuleName in areaRuleForType7.AreaRuleNames)
                {
                    var areaRulePlanning = areaRuleTranslations
                        .Where(x => x.Name == areaRuleName)
                        .Select(x => x.AreaRule)
                        .SelectMany(x => x.AreaRulesPlannings)
                        .FirstOrDefault();
                    itemsHtml.Append(@"<tr style='font-size:9pt;'>");
                    itemsHtml.Append($@"<td>{areaRuleName}</td>");
                    if (areaRulePlanning == null)
                    {
                        itemsHtml.Append(@"<td></td>");
                        itemsHtml.Append(@"<td></td>");
                    }
                    else
                    {
                        itemsHtml.Append($@"<td>{areaRulePlanning.StartDate:dd.MM.yyyy}</td>");
                        // ReSharper disable once PossibleInvalidOperationException
                        itemsHtml.Append($@"<td>{areaRulePlanning.RepeatEvery} - {(RepeatType)areaRulePlanning.RepeatType}</td>");
                    }
                    //itemsHtml.Append(@"<tr><td></td><td></td><td></td></tr>");
                }
                itemsHtml.Append(@"</tr>");
            }
            itemsHtml.Append(@"</table>");
            itemsHtml.Append("</body>");

            html = html.Replace("{%ItemList%}", itemsHtml.ToString());

            var word = new WordProcessor(docxFileStream);
            word.AddHtml(html);
            word.Dispose();
            docxFileStream.Position = 0;
            return new OperationDataResult<Stream>(true, docxFileStream);
        }
    }
}