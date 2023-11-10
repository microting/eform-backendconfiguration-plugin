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
	using Infrastructure.Models.Report;
	using BackendConfigurationLocalizationService;
	using eFormCore;
	using ImageMagick;
	using Infrastructure.Data.Seed.Data;
	using Infrastructure.Models.AreaRules;
	using Infrastructure.Models.TaskManagement;
	using Microsoft.EntityFrameworkCore;
	using Microsoft.Extensions.Logging;
	using Microting.eForm.Dto;
	using Microting.eForm.Infrastructure.Constants;
	using Microting.eFormApi.BasePn.Abstractions;
	using Microting.eFormApi.BasePn.Infrastructure.Models.API;
	using Microting.EformBackendConfigurationBase.Infrastructure.Data;
	using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
	using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
	using Microting.ItemsPlanningBase.Infrastructure.Data;
	using Microting.ItemsPlanningBase.Infrastructure.Enums;
	using System;
	using System.Collections.Generic;
	using System.Diagnostics;
	using System.IO;
	using System.Linq;
	using System.Reflection;
	using System.Text;
	using System.Text.RegularExpressions;
	using System.Threading.Tasks;

	public class WordService : IWordService
    {
        private readonly ILogger<WordService> _logger;
        private readonly IBackendConfigurationLocalizationService  _localizationService;
        private readonly IEFormCoreService _coreHelper;
        private readonly BackendConfigurationPnDbContext _dbContext;
        private readonly ItemsPlanningPnDbContext _itemsPlanningPnDbContext;
        private readonly IUserService _userService;
        private bool _s3Enabled;
        private bool _swiftEnabled;

        public WordService(
            ILogger<WordService> logger,
            IBackendConfigurationLocalizationService localizationService,
            IEFormCoreService coreHelper,
            BackendConfigurationPnDbContext dbContext,
            IUserService userService, ItemsPlanningPnDbContext itemsPlanningPnDbContext)
        {
            _logger = logger;
            _localizationService = localizationService;
            _coreHelper = coreHelper;
            _dbContext = dbContext;
            _userService = userService;
            _itemsPlanningPnDbContext = itemsPlanningPnDbContext;
        }

        public async Task<OperationDataResult<Stream>> GenerateReport(int propertyId, int areaId, int year)
        {
            try
            {
                var property = await _dbContext.Properties
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .FirstOrDefaultAsync(x => x.Id == propertyId).ConfigureAwait(false);
                var area = await _dbContext.Areas
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .FirstOrDefaultAsync(x => x.Id == areaId).ConfigureAwait(false);
                var isPropertyAndAreaLinked = await _dbContext.AreaProperties
                    .Where(x => x.AreaId == areaId)
                    .Where(x => x.PropertyId == propertyId)
                    .AnyAsync().ConfigureAwait(false);
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
                    AreaTypesEnum.Type7 => await GenerateReportType7(property, area, year).ConfigureAwait(false),
                    AreaTypesEnum.Type8 => await GenerateReportType8(property, area, year).ConfigureAwait(false),
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

        public async Task<Stream> GenerateWorkOrderCaseReport(TaskManagementFiltersModel filtersModel, List<WorkorderCaseModel> workOrderCaseModels)
        {
            var filtersLastAssignedTo = "";
            if (filtersModel.LastAssignedTo.HasValue && filtersModel.LastAssignedTo.Value != 0)
            {
                var core = await _coreHelper.GetCore();
                var sdkDbContext = core.DbContextHelper.GetDbContext();
                filtersLastAssignedTo = await sdkDbContext.Sites
                    .Where(x => x.Id == filtersModel.LastAssignedTo.Value)
                    .Select(x => x.Name)
                    .FirstOrDefaultAsync();
            }
            // Read html and template
            var resourceString = "BackendConfiguration.Pn.Resources.Templates.WordExport.page.html";
            var assembly = Assembly.GetExecutingAssembly();
            var resourceStream = assembly.GetManifestResourceStream(resourceString);
            string html;
            using (var reader = new StreamReader(resourceStream ?? throw new InvalidOperationException($"{nameof(resourceStream)} is null")))
            {
                html = await reader.ReadToEndAsync().ConfigureAwait(false);
            }

            resourceString = "BackendConfiguration.Pn.Resources.Templates.WordExport.file.docx";
            var docxFileResourceStream = assembly.GetManifestResourceStream(resourceString);
            if (docxFileResourceStream == null)
            {
                throw new InvalidOperationException($"{nameof(docxFileResourceStream)} is null");
            }
            var docxFileStream = new MemoryStream();
            await docxFileResourceStream.CopyToAsync(docxFileStream).ConfigureAwait(false);

            var itemsHtml = new StringBuilder();
            itemsHtml.Append(@"<div style='font-family:Calibri;'>");
            itemsHtml.Append(@"<table width=""100%"" border=""1"">");
            itemsHtml.Append(@"<tr style='font-weight:bold;font-size:6pt;'>");
            itemsHtml.Append($@"<td>{_localizationService.GetString("Property")}</td>");
            itemsHtml.Append($@"<td>{_localizationService.GetString("PropertyArea")}</td>");
            itemsHtml.Append($@"<td>{_localizationService.GetString("CreatedBy")}</td>");
            itemsHtml.Append($@"<td>{_localizationService.GetString("LastAssignedTo")}</td>");
            itemsHtml.Append($@"<td>{_localizationService.GetString("Status")}</td>");
            itemsHtml.Append($@"<td>{_localizationService.GetString("Date")}</td>");
            itemsHtml.Append(@"</tr>");
            itemsHtml.Append(@"<tr style='font-size:6pt;'>");
            itemsHtml.Append($@"<td>{await _dbContext.Properties.Where(x => x.Id == filtersModel.PropertyId).Select(x => x.Name).FirstAsync().ConfigureAwait(false)}</td>");
            itemsHtml.Append($@"<td>{(string.IsNullOrEmpty(filtersModel.AreaName) ? "" : filtersModel.AreaName)}</td>");
            itemsHtml.Append($@"<td>{(string.IsNullOrEmpty(filtersModel.CreatedBy) ? "" : filtersModel.CreatedBy)}</td>");
            itemsHtml.Append($@"<td>{filtersLastAssignedTo}</td>");
            itemsHtml.Append($@"<td>{(string.IsNullOrEmpty(filtersModel.GetStringStatus()) ? "" : _localizationService.GetString(filtersModel.GetStringStatus()))}</td>");
            itemsHtml.Append($@"<td>{(!filtersModel.DateFrom.HasValue ? "" : filtersModel.DateFrom.Value.ToString("d"))}");
            itemsHtml.Append($@"{(!filtersModel.DateTo.HasValue ? "" : " - " + filtersModel.DateTo.Value.ToString("d"))}</td>");
            itemsHtml.Append(@"</tr>");
            itemsHtml.Append(@"</table>");
            itemsHtml.Append(@"<p></p>"); // enter

            itemsHtml.Append(@"<table width=""100%"" border=""1"">");
            // Table header
            itemsHtml.Append(@"<tr style='font-weight:bold;font-size:6pt;'>");
            itemsHtml.Append($@"<td>{_localizationService.GetString("Id")}</td>");
            itemsHtml.Append($@"<td>{_localizationService.GetString("CreatedDate")}</td>");
            itemsHtml.Append($@"<td>{_localizationService.GetString("Property")}</td>");
            itemsHtml.Append($@"<td>{_localizationService.GetString("Area")}</td>");
            itemsHtml.Append($@"<td>{_localizationService.GetString("CreatedBy1")}</td>");
            itemsHtml.Append($@"<td>{_localizationService.GetString("CreatedBy2")}</td>");
            itemsHtml.Append($@"<td>{_localizationService.GetString("LastAssignedTo")}</td>");
            itemsHtml.Append($@"<td>{_localizationService.GetString("Description")}</td>");
            itemsHtml.Append($@"<td>{_localizationService.GetString("LastUpdateDate")}</td>");
            itemsHtml.Append($@"<td>{_localizationService.GetString("LastUpdatedBy")}</td>");
            itemsHtml.Append($@"<td>{_localizationService.GetString("Status")}</td>");
            itemsHtml.Append(@"</tr>");

            foreach (var workOrderCaseModel in workOrderCaseModels)
            {
                itemsHtml.Append(@"<tr style='font-size:6pt;'>");
                itemsHtml.Append($@"<td>{workOrderCaseModel.Id}</td>");
                itemsHtml.Append($@"<td>{workOrderCaseModel.CaseInitiated:dd.MM.yyyy}</td>");
                itemsHtml.Append($@"<td>{workOrderCaseModel.PropertyName}</td>");
                itemsHtml.Append($@"<td>{workOrderCaseModel.AreaName}</td>");
                itemsHtml.Append($@"<td>{workOrderCaseModel.CreatedByName}</td>");
                itemsHtml.Append($@"<td>{workOrderCaseModel.CreatedByText}</td>");
                itemsHtml.Append($@"<td>{workOrderCaseModel.LastAssignedTo}</td>");
                itemsHtml.Append($@"<td>{workOrderCaseModel.Description}</td>");
                itemsHtml.Append($@"<td>{(workOrderCaseModel.LastUpdateDate.HasValue ? workOrderCaseModel.LastUpdateDate.Value.ToString("dd.MM.yyyy") : "")}</td>");
                itemsHtml.Append($@"<td>{workOrderCaseModel.LastUpdatedBy}</td>");
                itemsHtml.Append($@"<td>{_localizationService.GetString(workOrderCaseModel.Status)}</td>");
                itemsHtml.Append(@"</tr>");
            }
            itemsHtml.Append(@"</table>");
            itemsHtml.Append("</div>");

            html = html.Replace("{%Content%}", itemsHtml.ToString());

            var word = new WordProcessor(docxFileStream);
            word.AddHtml(html, 284);
            word.Dispose();
            docxFileStream.Position = 0;
            return docxFileStream;
        }

        private async Task<OperationDataResult<Stream>> GenerateReportType7(Property property, Area area, int year)
        {
            var core = await _coreHelper.GetCore().ConfigureAwait(false);
            var sdkDbContext = core.DbContextHelper.GetDbContext();
            var curentLanguage = await _userService.GetCurrentUserLanguage().ConfigureAwait(false);
            if (curentLanguage.Name != "English" && curentLanguage.Name != "Danish") // reports only eng and da langs
            {
                curentLanguage = await sdkDbContext.Languages.FirstAsync(x => x.Name == "Danish").ConfigureAwait(false);
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
                        .ToList()
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
                    .LastOrDefaultAsync().ConfigureAwait(false);
            }

            var areaRuleTranslations = await _dbContext.AreaRuleTranslations
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                //.Where(x => areaRulesForType7.SelectMany(y => y.AreaRuleNames).Any(y => y == x.Name))
                .Include(x => x.AreaRule)
                .ThenInclude(x => x.AreaRulesPlannings)
                .Where(x => x.AreaRule.PropertyId == property.Id)
                .Where(x => x.AreaRule.AreaId == area.Id)
                //.Select(x => x.AreaRule)
                .ToListAsync().ConfigureAwait(false);

            // Read html and template
            var resourceString = "BackendConfiguration.Pn.Resources.Templates.WordExport.page.html";
            var assembly = Assembly.GetExecutingAssembly();
            var resourceStream = assembly.GetManifestResourceStream(resourceString);
            string html;
            using (var reader = new StreamReader(resourceStream ?? throw new InvalidOperationException($"{nameof(resourceStream)} is null")))
            {
                html = await reader.ReadToEndAsync().ConfigureAwait(false);
            }

            resourceString = "BackendConfiguration.Pn.Resources.Templates.WordExport.file.docx";
            var docxFileResourceStream = assembly.GetManifestResourceStream(resourceString);
            if (docxFileResourceStream == null)
            {
                throw new InvalidOperationException($"{nameof(docxFileResourceStream)} is null");
            }
            var docxFileStream = new MemoryStream();
            await docxFileResourceStream.CopyToAsync(docxFileStream).ConfigureAwait(false);

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
            itemsHtml.Append($@"<td>{_localizationService.GetString("Frequency")}</td>");
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
                        var repeatType = ((RepeatType)areaRulePlanning.RepeatType).ToString();
                        var firstChar = repeatType.First().ToString();
                        repeatType = repeatType.Replace(firstChar, firstChar.ToLower());
                        itemsHtml.Append($@"<td>{areaRulePlanning.RepeatEvery} - {_localizationService.GetString(repeatType)}</td>");
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

        private async Task<OperationDataResult<Stream>> GenerateReportType8(Property property, Area area, int year)
        {
            var core = await _coreHelper.GetCore().ConfigureAwait(false);
            var sdkDbContext = core.DbContextHelper.GetDbContext();
            var currentLanguage = await _userService.GetCurrentUserLanguage().ConfigureAwait(false);
            if (currentLanguage.Name != "English" && currentLanguage.Name != "Danish") // reports only eng and da langs
            {
                currentLanguage = await sdkDbContext.Languages.FirstAsync(x => x.Name == "Danish").ConfigureAwait(false);
            }

            var areaProperty =
                await _dbContext.AreaProperties.FirstOrDefaultAsync(x =>
                        x.PropertyId == property.Id && x.AreaId == area.Id)
                    .ConfigureAwait(false);

            var propertyAreaFolders = await _dbContext.ProperyAreaFolders
                .Where(x => x.ProperyAreaAsignmentId == areaProperty.Id)
                .Select(x => x.FolderId)
                .ToListAsync()
                .ConfigureAwait(false);

            var folderTranslations = await sdkDbContext.FolderTranslations
                .Where(x => propertyAreaFolders.Contains(x.FolderId))
                .Where(x => x.LanguageId == currentLanguage.Id)
                .Select(x => x.Name)
                .ToListAsync();


            var areaRulesForType8 = BackendConfigurationSeedAreas.AreaRulesForType8
                .GroupBy(x => x.FolderName)
                .Select(x => new AreaRulesForType8
                {
                    FolderName = x.Key,
                    AreaRuleNames = x.Select(y => y)
                        .Where(y => y.FolderName == x.Key)
                        .SelectMany(y => y.AreaRuleTranslations
                            .Where(z => z.LanguageId == currentLanguage.Id)
                            .Select(z => z.Name))
                        .ToList()
                })
                .ToList();

            foreach (var areaRuleForType8 in areaRulesForType8)
            {
                areaRuleForType8.FolderName = await sdkDbContext.FolderTranslations
                    .OrderBy(x => x.Id)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Name == areaRuleForType8.FolderName)
                    .SelectMany(x => x.Folder.FolderTranslations)
                    .Where(x => x.LanguageId == currentLanguage.Id)
                    .Select(x => x.Name)
                    .LastOrDefaultAsync().ConfigureAwait(false);
            }

            var areaRuleTranslations = await _dbContext.AreaRuleTranslations
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                //.Where(x => areaRulesForType7.SelectMany(y => y.AreaRuleNames).Any(y => y == x.Name))
                .Include(x => x.AreaRule)
                .ThenInclude(x => x.AreaRulesPlannings)
                .Where(x => x.AreaRule.PropertyId == property.Id)
                .Where(x => x.AreaRule.AreaId == area.Id)
                //.Select(x => x.AreaRule)
                .ToListAsync().ConfigureAwait(false);

            // Read html and template
            var resourceString = "BackendConfiguration.Pn.Resources.Templates.WordExport.page.html";
            var assembly = Assembly.GetExecutingAssembly();
            var resourceStream = assembly.GetManifestResourceStream(resourceString);
            string html;
            using (var reader = new StreamReader(resourceStream ?? throw new InvalidOperationException($"{nameof(resourceStream)} is null")))
            {
                html = await reader.ReadToEndAsync().ConfigureAwait(false);
            }

            resourceString = "BackendConfiguration.Pn.Resources.Templates.WordExport.file.docx";
            var docxFileResourceStream = assembly.GetManifestResourceStream(resourceString);
            if (docxFileResourceStream == null)
            {
                throw new InvalidOperationException($"{nameof(docxFileResourceStream)} is null");
            }
            var docxFileStream = new MemoryStream();
            await docxFileResourceStream.CopyToAsync(docxFileStream).ConfigureAwait(false);

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
            itemsHtml.Append(@"<tr style='background-color:#d0cece;font-weight:bold;font-size:9pt;'>");
            itemsHtml.Append($@"<td>{_localizationService.GetString("IE-Control Areas")}</td>");
            itemsHtml.Append($@"<td>{_localizationService.GetString("StartDate")}</td>");
            itemsHtml.Append($@"<td>{_localizationService.GetString("Frequency")}</td>");
            itemsHtml.Append(@"</tr>");

            folderTranslations.RemoveAt(0);

            foreach (var folderTranslation in folderTranslations)
            {
                itemsHtml.Append(folderTranslation.Count(x => x == '.') > 1
                    ? @"<tr style='font-weight:bold;font-size:9pt;'>"
                    : @"<tr style='background-color:#e2efd9;font-weight:bold;font-size:9pt;'>");
                itemsHtml.Append($@"<td>{folderTranslation}</td>");
                itemsHtml.Append(@"<td></td>");
                itemsHtml.Append(@"<td></td>");
                itemsHtml.Append(@"</tr>");

                var areaRules = areaRulesForType8
                    .Where(x => x.FolderName == folderTranslation)
                    .ToList();

                foreach (var areaRule in areaRules)
                {
                    foreach (var areaRuleName in areaRule.AreaRuleNames)
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
                            string repeatEvery = "";
                            var repeatType = "";
                            if (areaRulePlanning.RepeatType != null)
                            {
                                repeatType = ((RepeatType)areaRulePlanning.RepeatType).ToString();
                                var firstChar = repeatType.First().ToString();
                                switch (areaRulePlanning.RepeatEvery)
                                {
                                    case 0:
                                    case 1:
                                        repeatEvery = _localizationService.GetString("every");
                                        break;
                                    default:
                                        repeatEvery = _localizationService.GetString("every") + " " + areaRulePlanning.RepeatEvery;
                                        break;
                                }
                                repeatType = repeatType.Replace(firstChar, firstChar.ToLower());
                                repeatType = _localizationService.GetString(repeatType);
                            }

                            itemsHtml.Append($@"<td>{repeatEvery} - {repeatType}</td>");
                        }
                    }
                    itemsHtml.Append(@"</tr>");
                }
            }
            itemsHtml.Append(@"</table>");
            itemsHtml.Append("</body>");

            html = html.Replace("{%Content%}", itemsHtml.ToString());

            var word = new WordProcessor(docxFileStream);
            word.AddHtml(html);
            word.Dispose();
            docxFileStream.Position = 0;
            return new OperationDataResult<Stream>(true, docxFileStream);
        }

        public async Task<OperationDataResult<Stream>> GenerateWordDashboard(List<OldReportEformModel> reportModel)
        {
            try
            {
                // get core
                var core = await _coreHelper.GetCore();
                // var headerImageName = _dbContext.PluginConfigurationValues.Single(x => x.Name == "ItemsPlanningBaseSettings:ReportImageName").Value;

                _s3Enabled = core.GetSdkSetting(Settings.s3Enabled).Result.ToLower() == "true";
                _swiftEnabled = core.GetSdkSetting(Settings.swiftEnabled).Result.ToLower() == "true";
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
                string basePicturePath = await core.GetSdkSetting(Settings.fileLocationPicture);

                var word = new WordProcessor(docxFileStream);

                var itemsHtml = new StringBuilder();
                var header = _itemsPlanningPnDbContext.PluginConfigurationValues.Single(x => x.Name == "ItemsPlanningBaseSettings:ReportHeaderName").Value;
                var subHeader = _itemsPlanningPnDbContext.PluginConfigurationValues.Single(x => x.Name == "ItemsPlanningBaseSettings:ReportSubHeaderName").Value;
                itemsHtml.Append("<body>");
                itemsHtml.Append(@"<p style='display:flex;align-content:center;justify-content:center;flex-wrap:wrap;'>");
                for (var i = 0; i < 8; i++)
                {
                    itemsHtml.Append(@"<p style='font-size:24px;text-align:center;color:#fff;'>Enter</p>");
                }
                itemsHtml.Append($@"<p style='font-size:24px;text-align:center;'>{header}</p>");
                itemsHtml.Append($@"<p style='font-size:20px;text-align:center;'>{subHeader}</p>");
                itemsHtml.Append($@"<p style='font-size:15px;text-align:center;'>{_localizationService.GetString("ReportPeriod")}: {reportModel.First().FromDate} - {reportModel.First().ToDate}</p>");

                itemsHtml.Append(@"</p>");

                // moving the cursor to the end of the page
                for (var i = 0; i < 5; i++)
                {
                    itemsHtml.Append(@"<p style='font-size:24px;text-align:center;color:#fff;'>Enter</p>");
                }
                // add tag names in end document
                foreach (var nameTage in reportModel.Last().NameTagsInEndPage)
                {
                    itemsHtml.Append($@"<p style='font-size:24px;text-align:center;'>{nameTage}</p>");
                }
                itemsHtml.Append(@"<div style='page-break-before:always;'>");
                for (var i = 0; i < reportModel.Count; i++)
                {
                    var reportEformModel = reportModel[i];
                    if (reportEformModel.TextHeaders != null)
                    {
                        if (!string.IsNullOrEmpty(reportEformModel.TextHeaders.Header1))
                        {
                            itemsHtml.Append($@"<h1>{Regex.Replace(reportEformModel.TextHeaders.Header1, @"\. ", ".")}</h1>");
                            // We do this, even thought some would look at it and find it looking stupid. But if we don't do it,
                            // Word WILL mess up the header titles, because it thinks it needs to fix the number order.
                        }

                        if (!string.IsNullOrEmpty(reportEformModel.TextHeaders.Header2))
                        {
                            itemsHtml.Append($@"<h2>{reportEformModel.TextHeaders.Header2}</h2>");
                        }

                        if (!string.IsNullOrEmpty(reportEformModel.TextHeaders.Header3))
                        {
                            itemsHtml.Append($@"<h3>{reportEformModel.TextHeaders.Header3}</h3>");
                        }

                        if (!string.IsNullOrEmpty(reportEformModel.TextHeaders.Header4))
                        {
                            itemsHtml.Append($@"<h4>{reportEformModel.TextHeaders.Header4}</h4>");
                        }

                        if (!string.IsNullOrEmpty(reportEformModel.TextHeaders.Header5))
                        {
                            itemsHtml.Append($@"<h5>{reportEformModel.TextHeaders.Header5}</h5>");
                        }
                    }

                    foreach (var description in reportEformModel.DescriptionBlocks)
                    {
                        itemsHtml.Append($@"<p style='font-size: 7pt;'>{description}</p>");
                    }

                    if (reportEformModel.Items.Any())
                    {
                        itemsHtml.Append(@"<table width=""100%"" border=""1"">"); // TODO change font-size 7

                        // Table header
                        itemsHtml.Append(@"<tr style='background-color:#f5f5f5;font-weight:bold;font-size: 7pt;'>");
                        itemsHtml.Append($@"<td>{_localizationService.GetString("Id")}</td>");
                        itemsHtml.Append($@"<td>{_localizationService.GetString("Property")}</td>");
                        itemsHtml.Append($@"<td>{_localizationService.GetString("SubmittedDate")}</td>");
                        itemsHtml.Append($@"<td>{_localizationService.GetString("DoneBy")}</td>");
                        itemsHtml.Append($@"<td>{_localizationService.GetString("ItemName")}</td>");

                        foreach (var itemHeader in reportEformModel.ItemHeaders)
                        {
                            itemsHtml.Append($@"<td>{itemHeader.Value}</td>");
                        }

                        itemsHtml.Append(@"</tr>");

                        foreach (var dataModel in reportEformModel.Items)
                        {
                            itemsHtml.Append(@"<tr style='font-size: 7pt;'>");
                            itemsHtml.Append($@"<td>{dataModel.MicrotingSdkCaseId}</td>");
                            itemsHtml.Append($@"<td>{dataModel.PropertyName}</td>");

                            itemsHtml.Append($@"<td>{dataModel.MicrotingSdkCaseDoneAt:dd.MM.yyyy}</td>");
                            itemsHtml.Append($@"<td>{dataModel.DoneBy}</td>");
                            itemsHtml.Append($@"<td>{dataModel.ItemName}</td>");

                            foreach (var dataModelCaseField in dataModel.CaseFields)
                            {
                                if (dataModelCaseField.Value == "checked")
                                {
                                    itemsHtml.Append($@"<td>&#10004;</td>");
                                }
                                else
                                {
                                    if (dataModelCaseField.Value == "unchecked")
                                    {
                                        itemsHtml.Append($@"<td></td>");
                                    } else
                                    {
                                        if (dataModelCaseField.Key == "date")
                                        {
                                            if (DateTime.TryParse(dataModelCaseField.Value, out var date))
                                            {
                                                itemsHtml.Append($@"<td>{date:dd.MM.yyyy}</td>");
                                            }
                                            else
                                            {
                                                itemsHtml.Append($@"<td>{dataModelCaseField.Value}</td>");
                                            }
                                        }
                                        else
                                        {
                                            itemsHtml.Append(dataModelCaseField.Key == "number"
                                                ? $@"<td>{dataModelCaseField.Value.Replace(".", ",")}</td>"
                                                : $@"<td>{dataModelCaseField.Value}</td>");
                                        }
                                    }
                                }
                            }
                            itemsHtml.Append(@"</tr>");
                        }

                        itemsHtml.Append(@"</table>");
                    }

                    itemsHtml.Append(@"<br/>");


                    foreach (var imagesName in reportEformModel.ImageNames)
                    {
                        itemsHtml.Append($@"<p style='font-size: 7pt; page-break-before:always'>{_localizationService.GetString("Id")}: {imagesName.Key[1]}</p>"); // TODO change to ID: {id}; imagesName.Key[1]

                        itemsHtml = await InsertImage(imagesName.Value[0], itemsHtml, 600, 650, core, basePicturePath);

                        if (!string.IsNullOrEmpty(imagesName.Value[1]))
                        {
                            itemsHtml.Append($@"<p style='font-size: 7pt;'>{_localizationService.GetString("Position")}:<a href=""{imagesName.Value[1]}"">{imagesName.Value[1]}</a></p>"); // TODO change to Position : URL
                        }
                    }
                }


                itemsHtml.Append(@"</div>");
                itemsHtml.Append("</body>");

                html = html.Replace("{%Content%}", itemsHtml.ToString());

                word.AddHtml(html);
                word.Dispose();
                docxFileStream.Position = 0;
                return new OperationDataResult<Stream>(true, docxFileStream);
            }
            catch (Exception e)
            {
                Trace.TraceError(e.Message);
                _logger.LogError(e.Message);
                return new OperationDataResult<Stream>(
                    false,
                    _localizationService.GetString("ErrorWhileCreatingWordFile"));
            }
        }

        public async Task<OperationDataResult<Stream>> GenerateWordDashboard(List<ReportEformModel> reportModel)
        {
            try
            {
                // get core
                var core = await _coreHelper.GetCore();
                // var headerImageName = _dbContext.PluginConfigurationValues.Single(x => x.Name == "ItemsPlanningBaseSettings:ReportImageName").Value;

                _s3Enabled = core.GetSdkSetting(Settings.s3Enabled).Result.ToLower() == "true";
                _swiftEnabled = core.GetSdkSetting(Settings.swiftEnabled).Result.ToLower() == "true";
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
                string basePicturePath = await core.GetSdkSetting(Settings.fileLocationPicture);

                var word = new WordProcessor(docxFileStream);

                var itemsHtml = new StringBuilder();
                var header = _itemsPlanningPnDbContext.PluginConfigurationValues.Single(x => x.Name == "ItemsPlanningBaseSettings:ReportHeaderName").Value;
                var subHeader = _itemsPlanningPnDbContext.PluginConfigurationValues.Single(x => x.Name == "ItemsPlanningBaseSettings:ReportSubHeaderName").Value;
                itemsHtml.Append("<body>");
                itemsHtml.Append(@"<p style='display:flex;align-content:center;justify-content:center;flex-wrap:wrap;'>");
                for (var i = 0; i < 8; i++)
                {
                    itemsHtml.Append(@"<p style='font-size:24px;text-align:center;color:#fff;'>Enter</p>");
                }
                itemsHtml.Append($@"<p style='font-size:24px;text-align:center;'>{header}</p>");
                itemsHtml.Append($@"<p style='font-size:20px;text-align:center;'>{subHeader}</p>");
                itemsHtml.Append($@"<p style='font-size:15px;text-align:center;'>{_localizationService.GetString("ReportPeriod")}: {reportModel.First().FromDate} - {reportModel.First().ToDate}</p>");

                itemsHtml.Append(@"</p>");

                // moving the cursor to the end of the page
                for (var i = 0; i < 5; i++)
                {
                    itemsHtml.Append(@"<p style='font-size:24px;text-align:center;color:#fff;'>Enter</p>");
                }
                // add tag names in end document
                foreach (var nameTage in reportModel.Last().NameTagsInEndPage)
                {
                    itemsHtml.Append($@"<p style='font-size:24px;text-align:center;'>{nameTage}</p>");
                }
                itemsHtml.Append(@"<div style='page-break-before:always;'>");
                foreach (var reportEformModel in reportModel)
                {
                    if (!string.IsNullOrEmpty(reportEformModel.GroupTagName))
                    {
                        itemsHtml.Append($@"<h1>{Regex.Replace(reportEformModel.GroupTagName, @"\. ", ".")}</h1>");
                        // We do this, even thought some would look at it and find it looking stupid. But if we don't do it,
                        // Word WILL mess up the header titles, because it thinks it needs to fix the number order.
                    }

                    foreach (var groupeForm in reportEformModel.GroupEform)
                    {
                        itemsHtml.Append(@"<table width=""100%"" border=""1"">"); // TODO change font-size 7

                        // Table header
                        itemsHtml.Append(@"<tr style='background-color:#f5f5f5;font-weight:bold;font-size: 7pt;'>");
                        itemsHtml.Append($@"<td>{_localizationService.GetString("Id")}</td>");
                        itemsHtml.Append($@"<td>{_localizationService.GetString("Property")}</td>");
                        itemsHtml.Append($@"<td>{_localizationService.GetString("SubmittedDate")}</td>");
                        itemsHtml.Append($@"<td>{_localizationService.GetString("DoneBy")}</td>");
                        itemsHtml.Append($@"<td>{_localizationService.GetString("ItemName")}</td>");

                        foreach (var itemHeader in groupeForm.ItemHeaders)
                        {
                            itemsHtml.Append($@"<td>{itemHeader.Value}</td>");
                        }

                        itemsHtml.Append(@"</tr>");

                        foreach (var dataModel in groupeForm.Items)
                        {
                            itemsHtml.Append(@"<tr style='font-size: 7pt;'>");
                            itemsHtml.Append($@"<td>{dataModel.MicrotingSdkCaseId}</td>");
                            itemsHtml.Append($@"<td>{dataModel.PropertyName}</td>");

                            itemsHtml.Append($@"<td>{dataModel.MicrotingSdkCaseDoneAt:dd.MM.yyyy}</td>");
                            itemsHtml.Append($@"<td>{dataModel.DoneBy}</td>");
                            itemsHtml.Append($@"<td>{dataModel.ItemName}</td>");

                            foreach (var dataModelCaseField in dataModel.CaseFields)
                            {
                                if (dataModelCaseField.Value == "checked")
                                {
                                    itemsHtml.Append($@"<td>&#10004;</td>");
                                }
                                else
                                {
                                    if (dataModelCaseField.Value == "unchecked")
                                    {
                                        itemsHtml.Append($@"<td></td>");
                                    } else
                                    {
                                        if (dataModelCaseField.Key == "date")
                                        {
                                            if (DateTime.TryParse(dataModelCaseField.Value, out var date))
                                            {
                                                itemsHtml.Append($@"<td>{date:dd.MM.yyyy}</td>");
                                            }
                                            else
                                            {
                                                itemsHtml.Append($@"<td>{dataModelCaseField.Value}</td>");
                                            }
                                        }
                                        else
                                        {
                                            itemsHtml.Append(dataModelCaseField.Key == "number"
                                                ? $@"<td>{dataModelCaseField.Value.Replace(".", ",")}</td>"
                                                : $@"<td>{dataModelCaseField.Value}</td>");
                                        }
                                    }
                                }
                            }
                            itemsHtml.Append(@"</tr>");
                        }

                        itemsHtml.Append(@"</table>");

                        itemsHtml.Append(@"<br/>");


                        foreach (var imagesName in groupeForm.ImageNames)
                        {
                            itemsHtml.Append($@"<p style='font-size: 7pt; page-break-before:always'>{_localizationService.GetString("Id")}: {imagesName.CaseId}</p>"); // TODO change to ID: {id}; imagesName.Key[1]

                            itemsHtml = await InsertImage(imagesName.ImageName, itemsHtml, 600, 650, core, basePicturePath);

                            if (!string.IsNullOrEmpty(imagesName.ImageName))
                            {
                                itemsHtml.Append($@"<p style='font-size: 7pt;'>{_localizationService.GetString("Position")}:<a href=""{imagesName.GeoLink}"">{imagesName.GeoLink}</a></p>"); // TODO change to Position : URL
                            }
                        }
                    }
                }


                itemsHtml.Append(@"</div>");
                itemsHtml.Append("</body>");

                html = html.Replace("{%Content%}", itemsHtml.ToString());

                word.AddHtml(html);
                word.Dispose();
                docxFileStream.Position = 0;
                return new OperationDataResult<Stream>(true, docxFileStream);
            }
            catch (Exception e)
            {
                Trace.TraceError(e.Message);
                _logger.LogError(e.Message);
                return new OperationDataResult<Stream>(
                    false,
                    _localizationService.GetString("ErrorWhileCreatingWordFile"));
            }
        }

        private async Task<StringBuilder> InsertImage(string imageName, StringBuilder itemsHtml, int imageSize, int imageWidth, Core core, string basePicturePath)
		{
			var filePath = Path.Combine(basePicturePath, imageName);
			Stream stream;
			if (_s3Enabled)
			{
				Console.WriteLine("Getting file from S3 " + imageName);
				var storageResult = await core.GetFileFromS3Storage(imageName);
				stream = storageResult.ResponseStream;
			}
			else if (!System.IO.File.Exists(filePath))
			{
				return itemsHtml;
				// return new OperationDataResult<Stream>(
				//     false,
				//     _localizationService.GetString($"{imagesName} not found"));
			}
			else
			{
				stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
			}

			using (var image = new MagickImage(stream))
			{
				Console.WriteLine("Opening file from stream " + imageName);
				decimal currentRation = image.Height / (decimal)image.Width;
				int newWidth = imageSize;
				int newHeight = (int)Math.Round((currentRation * newWidth));

				Console.WriteLine("Resizing file from stream " + imageName);
				image.Resize(newWidth, newHeight);
				Console.WriteLine("Cropping file from stream " + imageName);
				image.Crop(newWidth, newHeight);

				Console.WriteLine("converting to base64 " + imageName);
				var base64String = image.ToBase64();
				Console.WriteLine("Appending to itemsHtml file from stream " + imageName);
				itemsHtml.Append($@"<p><img src=""data:image/png;base64,{base64String}"" width=""{imageWidth}px"" alt="""" /></p>");
			}

			await stream.DisposeAsync();

			return itemsHtml;
		}
	}
}