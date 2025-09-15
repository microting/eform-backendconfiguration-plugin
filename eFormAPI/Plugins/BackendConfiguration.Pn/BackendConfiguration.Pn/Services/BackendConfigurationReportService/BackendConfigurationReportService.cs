/*
The MIT License (MIT)

Copyright (c) 2007 - 2019 Microting A/S

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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Report;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.ExcelService;
using BackendConfiguration.Pn.Services.WordService;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Helpers;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Helpers;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.eFormApi.BasePn.Infrastructure.Models.Application.Case.CaseEdit;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.ItemsPlanningBase.Infrastructure.Data;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using Sentry;

namespace BackendConfiguration.Pn.Services.BackendConfigurationReportService;

public class BackendConfigurationReportService(
    IBackendConfigurationLocalizationService backendConfigurationLocalizationService,
    ILogger<BackendConfigurationReportService> logger,
    IEFormCoreService coreHelper,
    IWordService wordService,
    IExcelService excelService,
    ICasePostBaseService casePostBaseService,
    ItemsPlanningPnDbContext itemsPlanningPnDbContext,
    IUserService userService,
    BackendConfigurationPnDbContext backendConfigurationPnDbContext)
    : IBackendConfigurationReportService
{
    private readonly ICasePostBaseService _casePostBaseService = casePostBaseService;

    // ReSharper disable once SuggestBaseTypeForParameter

    public async Task<OperationDataResult<List<OldReportEformModel>>> GenerateReport(GenerateReportModel model,
        bool isDocx)
    {
        try
        {
            var timeZoneInfo = await userService.GetCurrentUserTimeZoneInfo();
            var core = await coreHelper.GetCore();
            await using var sdkDbContext = core.DbContextHelper.GetDbContext();
            var fromDate = new DateTime(model.DateFrom!.Value.Year, model.DateFrom.Value.Month,
                model.DateFrom.Value.Day, 0, 0, 0);
            var toDate = new DateTime(model.DateTo!.Value.Year, model.DateTo.Value.Month,
                model.DateTo.Value.Day, 23, 59, 59);

            var planningCasesQuery = itemsPlanningPnDbContext.PlanningCases
                .Include(x => x.Planning)
                .ThenInclude(x => x.PlanningsTags)
                .Where(x => x.Status == 100)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .AsNoTracking()
                .AsQueryable();

            if (model.DateFrom != null)
            {
                planningCasesQuery = planningCasesQuery.Where(x =>
                    x.MicrotingSdkCaseDoneAt >= fromDate);
            }

            if (model.DateTo != null)
            {
                planningCasesQuery = planningCasesQuery.Where(x =>
                    x.MicrotingSdkCaseDoneAt <= toDate);
            }

            if (model.TagIds.Count > 0)
            {
                foreach (var tagId in model.TagIds)
                {
                    planningCasesQuery = planningCasesQuery.Where(x =>
                            x.Planning.PlanningsTags.Any(y =>
                                y.PlanningTagId == tagId && y.WorkflowState != Constants.WorkflowStates.Removed))
                        .AsNoTracking();
                }
            }

            var groupedCaseCheckListIds = planningCasesQuery.GroupBy(x => x.MicrotingSdkeFormId)
                .Select(x => x.Key)
                .ToList();

            var checkLists = new List<CheckList>();

            if (groupedCaseCheckListIds.Count > 0)
            {
                checkLists = await sdkDbContext.CheckLists
                    .FromSqlRaw("SELECT * FROM CheckLists WHERE" +
                                $" Id IN ({string.Join(",", groupedCaseCheckListIds)})" +
                                "  ORDER BY ReportH1, ReportH2, ReportH3, ReportH4").AsNoTracking().ToListAsync();
            }

            var itemCases = await planningCasesQuery
                .OrderBy(x => x.Planning.RelatedEFormName)
                .ToListAsync();

            var groupedCases = itemCases
                .GroupBy(x => x.MicrotingSdkeFormId)
                .Select(x => new
                {
                    templateId = x.Key,
                    cases = x.ToList()
                })
                .ToList();


            var result = new List<OldReportEformModel>();
            // Exclude field types: None, Picture, Audio, Movie, Signature, Show PDF, FieldGroup, SaveButton
            var excludedFieldTypes = new List<string>
            {
                Constants.FieldTypes.None,
                Constants.FieldTypes.Picture,
                Constants.FieldTypes.Audio,
                Constants.FieldTypes.Movie,
                Constants.FieldTypes.Signature,
                Constants.FieldTypes.ShowPdf,
                Constants.FieldTypes.FieldGroup,
                Constants.FieldTypes.SaveButton
            };
            var localeString = await userService.GetCurrentUserLocale();
            var language = sdkDbContext.Languages.Single(x => x.LanguageCode == localeString);
            //foreach (var groupedCase in groupedCases)
            foreach (var checkList in checkLists)
            {
                var hasChildCheckLists = sdkDbContext.CheckLists.Any(x => x.ParentId == checkList.Id);
                var checkListTranslation = sdkDbContext.CheckListTranslations
                    .Where(x => x.CheckListId == checkList.Id)
                    .First(x => x.LanguageId == language.Id).Text;
                //var template = await sdkDbContext.CheckLists.SingleAsync(x => x.Id == groupedCase.templateId);
                var groupedCase = groupedCases.SingleOrDefault(x => x.templateId == checkList.Id);

                if (groupedCase != null)
                {

                    var reportModel = new OldReportEformModel
                    {
                        CheckListId = checkList.Id,
                        CheckListName = checkListTranslation,
                        FromDate = $"{fromDate:yyyy-MM-dd}",
                        ToDate = $"{toDate:yyyy-MM-dd}",
                        TextHeaders = new ReportEformTextHeaderModel(),
                        TableName = checkListTranslation
                    };
                    // first pass
                    if (result.Count <= 0)
                    {
                        reportModel.TextHeaders.Header1 = !string.IsNullOrEmpty(checkList.ReportH1)
                            ? checkList.ReportH1
                            : checkListTranslation;
                        // reportModel.TableName = null;
                        // reportModel.TemplateName = null;
                        reportModel.TextHeaders.Header2 = checkList.ReportH2;
                        reportModel.TextHeaders.Header3 = checkList.ReportH3;
                        reportModel.TextHeaders.Header4 = checkList.ReportH4;
                        reportModel.TextHeaders.Header5 = checkList.ReportH5;
                    }
                    else // other pass
                    {
                        var header1 = result.LastOrDefault(x => x.TextHeaders.Header1 != null)?.TextHeaders.Header1;
                        var header2 = result.LastOrDefault(x => x.TextHeaders.Header2 != null)?.TextHeaders.Header2;
                        var header3 = result.LastOrDefault(x => x.TextHeaders.Header3 != null)?.TextHeaders.Header3;
                        var header4 = result.LastOrDefault(x => x.TextHeaders.Header4 != null)?.TextHeaders.Header4;
                        var header5 = result.LastOrDefault(x => x.TextHeaders.Header5 != null)?.TextHeaders.Header5;

                        // if not find or finded and templateHeader not equal

                        if (header1 == null || checkList.ReportH1 != header1)
                        {
                            reportModel.TextHeaders.Header1 = checkList.ReportH1 ?? checkListTranslation;
                        }

                        if (header2 == null || checkList.ReportH2 != header2)
                        {
                            reportModel.TextHeaders.Header2 = checkList.ReportH2 ?? "";
                        }

                        if (header3 == null || checkList.ReportH3 != header3)
                        {
                            reportModel.TextHeaders.Header3 = checkList.ReportH3 ?? "";
                        }

                        if (header4 == null || checkList.ReportH4 != header4)
                        {
                            reportModel.TextHeaders.Header4 = checkList.ReportH4 ?? "";
                        }

                        if (header5 == null || checkList.ReportH5 != header5)
                        {
                            reportModel.TextHeaders.Header5 = checkList.ReportH5 ?? "";
                        }

                    }

                    var fields = await core.Advanced_TemplateFieldReadAll(
                        checkList.Id, language);

                    foreach (var fieldDto in fields)
                    {
                        if (fieldDto.FieldType == Constants.FieldTypes.None)
                        {
                            var fieldTranslation =
                                await sdkDbContext.FieldTranslations.FirstAsync(x =>
                                    x.FieldId == fieldDto.Id && x.LanguageId == language.Id);
                            reportModel.DescriptionBlocks.Add(fieldTranslation.Description);
                        }

                        if (!excludedFieldTypes.Contains(fieldDto.FieldType))
                        {
                            var fieldTranslation =
                                await sdkDbContext.FieldTranslations.FirstAsync(x =>
                                    x.FieldId == fieldDto.Id && x.LanguageId == language.Id);
                            var text = fieldTranslation.Text;
                            if (hasChildCheckLists)
                            {
                                var clTranslation =
                                    await sdkDbContext.CheckListTranslations.FirstOrDefaultAsync(x =>
                                        x.CheckListId == fieldDto.CheckListId && x.LanguageId == language.Id);
                                if (checkListTranslation != clTranslation.Text)
                                {
                                    text = $"{clTranslation.Text} - {text}";
                                }
                            }

                            var kvp = new KeyValuePair<int, string>(fieldDto.Id, text);

                            reportModel.ItemHeaders.Add(kvp);
                        }
                    }

                    // images
                    var templateCaseIds = groupedCase.cases.Select(x => (int?)x.MicrotingSdkCaseId).ToArray();
                    var imagesForEform = await sdkDbContext.FieldValues
                        .Include(x => x.UploadedData)
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Where(x => x.UploadedData.WorkflowState != Constants.WorkflowStates.Removed)
                        .Where(x => x.Field.FieldTypeId == 5)
                        .Where(x => templateCaseIds.Contains(x.CaseId))
                        .Where(x => x.UploadedDataId != null)
                        .OrderBy(x => x.CaseId)
                        .ToListAsync();

                    foreach (var imageField in imagesForEform)
                    {
                        var planningCase = groupedCase.cases.First(x =>
                            x.MicrotingSdkCaseId == imageField.CaseId && x.PlanningId != 0);
                        var planningNameTranslation =
                            await itemsPlanningPnDbContext.PlanningNameTranslation.FirstOrDefaultAsync(x =>
                                x.PlanningId == planningCase.PlanningId && x.LanguageId == language.Id);

                        if (planningNameTranslation != null)
                        {
                            var label = $"{imageField.CaseId}; {planningNameTranslation.Name}";
                            var geoTag = "";
                            if (!string.IsNullOrEmpty(imageField.Latitude))
                            {
                                geoTag =
                                    $"https://www.google.com/maps/place/{imageField.Latitude},{imageField.Longitude}";
                            }

                            var keyList = new List<string> { imageField.CaseId.ToString(), label };
                            var list = new List<string>();
                            if (!string.IsNullOrEmpty(imageField.UploadedData.FileName))
                            {
                                list.Add(
                                    $"{imageField.UploadedData.Id}_700_{imageField.UploadedData.Checksum}{imageField.UploadedData.Extension}");
                                list.Add(geoTag);
                                reportModel.ImageNames.Add(
                                    new KeyValuePair<List<string>, List<string>>(keyList, list));
                            }
                        }
                    }

                    foreach (var planningCase in groupedCase.cases.OrderBy(x => x.MicrotingSdkCaseDoneAt).ToList())
                    {
                        var planningNameTranslation =
                            await itemsPlanningPnDbContext.PlanningNameTranslation.FirstOrDefaultAsync(x =>
                                x.PlanningId == planningCase.PlanningId && x.LanguageId == language.Id);
                        var propertyName = "";

                        var areaRulePlanning =
                            await backendConfigurationPnDbContext.AreaRulePlannings.FirstOrDefaultAsync(x =>
                                x.ItemPlanningId == planningCase.PlanningId);

                        if (areaRulePlanning != null)
                        {
                            propertyName = backendConfigurationPnDbContext.Properties
                                .First(x => x.Id == areaRulePlanning.PropertyId).Name;
                        }
                        else
                        {
                            var areaRulePlanningVersion = await backendConfigurationPnDbContext
                                .AreaRulesPlanningVersions
                                .Where(x => x.ItemPlanningId == planningCase.PlanningId)
                                .OrderByDescending(x => x.Version)
                                .FirstOrDefaultAsync();

                            if (areaRulePlanningVersion != null)
                            {
                                propertyName = backendConfigurationPnDbContext.Properties
                                    .First(x => x.Id == areaRulePlanningVersion.PropertyId).Name;
                            }
                        }

                        var dbCase =
                            await sdkDbContext.Cases.FirstOrDefaultAsync(x => x.Id == planningCase.MicrotingSdkCaseId);

                        if (dbCase == null)
                        {
                            logger.LogError($"Could not find case with id {planningCase.MicrotingSdkCaseId}");
                            continue;
                        }

                        if (planningNameTranslation != null)
                        {
                            var item = new ReportEformItemModel
                            {
                                Id = planningCase.Id,
                                ItemId = planningCase.PlanningId,
                                MicrotingSdkCaseId = planningCase.MicrotingSdkCaseId,
                                MicrotingSdkCaseDoneAt =
                                    TimeZoneInfo.ConvertTimeFromUtc((DateTime)planningCase.MicrotingSdkCaseDoneAt,
                                        timeZoneInfo),
                                ServerTime = TimeZoneInfo.ConvertTimeFromUtc((DateTime)dbCase.CreatedAt, timeZoneInfo),
                                eFormId = planningCase.MicrotingSdkeFormId,
                                DoneBy = planningCase.DoneByUserName,
                                ItemName = planningNameTranslation.Name,
                                ItemDescription = planningCase.Planning.Description,
                                PropertyName = propertyName
                            };


                            var caseFields = sdkDbContext.FieldValues.Where(x =>
                                x.CaseId == planningCase.MicrotingSdkCaseId &&
                                x.WorkflowState != Constants.WorkflowStates.Removed).ToList();
                            // var caseFields = await core.Advanced_FieldValueReadList(
                            //     new List<int>()
                            //     {
                            //         planningCase.MicrotingSdkCaseId
                            //     }, language);

                            foreach (var fieldDto in fields)
                            {
                                var caseField = caseFields.FirstOrDefault(x => x.FieldId == fieldDto.Id);
                                if (caseField != null)
                                {
                                    switch (fieldDto.FieldType)
                                    {
                                        case Constants.FieldTypes.MultiSelect:
                                            var keyLst = string.IsNullOrEmpty(caseField.Value)
                                                ? []
                                                : caseField.Value.Split('|').ToList();
                                            var valueReadable = "";
                                            foreach (var key in keyLst)
                                            {
                                                if (!string.IsNullOrEmpty(key))
                                                {
                                                    var fieldOption =
                                                        await sdkDbContext.FieldOptions.FirstOrDefaultAsync(x =>
                                                            x.FieldId == caseField.FieldId && x.Key == key);
                                                    if (fieldOption != null)
                                                    {
                                                        var fieldOptionTranslation =
                                                            await sdkDbContext.FieldOptionTranslations.FirstAsync(x =>
                                                                x.FieldOptionId == fieldOption.Id &&
                                                                x.LanguageId == language.Id);
                                                        if (valueReadable != "")
                                                        {
                                                            valueReadable += '|';
                                                        }

                                                        valueReadable += fieldOptionTranslation.Text;
                                                    }
                                                }
                                            }

                                            item.CaseFields.Add(
                                                new KeyValuePair<string, string>("string", valueReadable));
                                            break;
                                        case Constants.FieldTypes.SingleSelect:
                                            var fo = await sdkDbContext.FieldOptions.FirstOrDefaultAsync(x =>
                                                x.FieldId == caseField.FieldId && x.Key == caseField.Value);
                                            if (fo != null)
                                            {
                                                var fieldOptionTranslation =
                                                    await sdkDbContext.FieldOptionTranslations.FirstAsync(x =>
                                                        x.FieldOptionId == fo.Id && x.LanguageId == language.Id);
                                                item.CaseFields.Add(new KeyValuePair<string, string>("string",
                                                    fieldOptionTranslation.Text));
                                            }
                                            else
                                            {
                                                item.CaseFields.Add(new KeyValuePair<string, string>("string", ""));
                                            }

                                            break;
                                        case Constants.FieldTypes.EntitySearch or
                                            Constants.FieldTypes.EntitySelect:
                                            if (!string.IsNullOrEmpty(caseField.Value) && caseField.Value != "null")
                                            {
                                                var id = int.Parse(caseField.Value);
                                                var match =
                                                    await sdkDbContext.EntityItems.AsNoTracking()
                                                        .FirstOrDefaultAsync(x => x.Id == id);
                                                item.CaseFields.Add(
                                                    new KeyValuePair<string, string>("string", match.Name));
                                            }
                                            else
                                            {
                                                item.CaseFields.Add(new KeyValuePair<string, string>("string", ""));
                                            }

                                            break;
                                        case Constants.FieldTypes.Picture
                                            or Constants.FieldTypes.SaveButton
                                            or Constants.FieldTypes.Signature
                                            or Constants.FieldTypes.None
                                            or Constants.FieldTypes.FieldGroup:
                                            break;
                                        case Constants.FieldTypes.Number
                                            or Constants.FieldTypes.NumberStepper:
                                            item.CaseFields.Add(caseField.Value != null
                                                ? new KeyValuePair<string, string>("number",
                                                    caseField.Value.Replace(",", "."))
                                                : new KeyValuePair<string, string>("number", ""));
                                            break;
                                        case Constants.FieldTypes.Date:
                                            item.CaseFields.Add(
                                                new KeyValuePair<string, string>("date", caseField.Value));
                                            break;
                                        case Constants.FieldTypes.CheckBox:
                                            if (caseField.Value is "true" or "false")
                                            {
                                                item.CaseFields.Add(new KeyValuePair<string, string>("string",
                                                    caseField.Value == "true" ? "checked" : "unchecked"));
                                            }
                                            else
                                            {
                                                item.CaseFields.Add(
                                                    new KeyValuePair<string, string>("string", caseField.Value));
                                            }

                                            break;
                                        default:
                                            item.CaseFields.Add(
                                                new KeyValuePair<string, string>("string", caseField.Value));
                                            break;
                                    }
                                }
                                else
                                {
                                    item.CaseFields.Add(new KeyValuePair<string, string>("string", ""));
                                }
                            }

                            item.ImagesCount = await sdkDbContext.FieldValues
                                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                .Where(x => x.Field.FieldTypeId == 5)
                                .Where(x => x.CaseId == planningCase.MicrotingSdkCaseId)
                                .Where(x => x.UploadedDataId != null)
                                .Select(x => x.Id)
                                .CountAsync();

                            reportModel.Items.Add(item);
                        }
                    }

                    result.Add(reportModel);
                }

            }

            var reportEformModel = new OldReportEformModel();
            reportEformModel.NameTagsInEndPage.AddRange(itemsPlanningPnDbContext.PlanningTags
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => model.TagIds.Any(y => y == x.Id))
                .Select(x => x.Name));
            result.Add(reportEformModel);

            if (result.Any())
            {
                return new OperationDataResult<List<OldReportEformModel>>(true, result);
            }

            return new OperationDataResult<List<OldReportEformModel>>(false,
                backendConfigurationLocalizationService.GetString("NoDataInSelectedPeriod"));

        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            logger.LogError(e.Message);
            logger.LogError(e.StackTrace);
            return new OperationDataResult<List<OldReportEformModel>>(false,
                backendConfigurationLocalizationService.GetString("ErrorWhileGeneratingReport") + e.Message);
        }
    }

    public async Task<OperationDataResult<List<ReportEformModel>>> GenerateReportV2(GenerateReportModel model,
        bool isDocx)
    {
        try
        {
            var timeZoneInfo = await userService.GetCurrentUserTimeZoneInfo();
            var core = await coreHelper.GetCore();
            await using var sdkDbContext = core.DbContextHelper.GetDbContext();
            var fromDate = new DateTime(model.DateFrom!.Value.Year, model.DateFrom.Value.Month,
                model.DateFrom.Value.Day, 0, 0, 0);
            var toDate = new DateTime(model.DateTo!.Value.Year, model.DateTo.Value.Month,
                model.DateTo.Value.Day, 23, 59, 59);

            var planningCasesQuery = itemsPlanningPnDbContext.PlanningCases
                .Include(x => x.Planning)
                .ThenInclude(x => x.PlanningsTags)
                .Where(x => x.Status == 100)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .AsNoTracking()
                .AsQueryable();

            if (model.DateFrom != null)
            {
                planningCasesQuery = planningCasesQuery.Where(x =>
                    x.MicrotingSdkCaseDoneAt >= fromDate);
            }

            if (model.DateTo != null)
            {
                planningCasesQuery = planningCasesQuery.Where(x =>
                    x.MicrotingSdkCaseDoneAt <= toDate);
            }

            if (model.TagIds.Count > 0)
            {
                foreach (var tagId in model.TagIds)
                {
                    planningCasesQuery = planningCasesQuery
                        .Where(x => x.Planning.PlanningsTags
                                        .Any(y => y.PlanningTagId == tagId &&
                                                  y.WorkflowState != Constants.WorkflowStates.Removed)
                                    || x.Planning.ReportGroupPlanningTagId == tagId)
                        .AsNoTracking();
                }
            }

            var planningTagsForGroup = await itemsPlanningPnDbContext.PlanningTags
                .ToListAsync();

            var result = new List<ReportEformModel>();
            // Exclude field types: None, Picture, Audio, Movie, Signature, Show PDF, FieldGroup, SaveButton
            var excludedFieldTypes = new List<string>
            {
                Constants.FieldTypes.None,
                Constants.FieldTypes.Picture,
                Constants.FieldTypes.Audio,
                Constants.FieldTypes.Movie,
                Constants.FieldTypes.Signature,
                Constants.FieldTypes.ShowPdf,
                Constants.FieldTypes.FieldGroup,
                Constants.FieldTypes.SaveButton
            };
            var localeString = await userService.GetCurrentUserLocale();
            var language = sdkDbContext.Languages.Single(x => x.LanguageCode == localeString);

            var groupedPlanningCases = planningCasesQuery
                .ToList()
                .Select(x => new
                {
                    planningCase = x,
                    ReportGroupPlanningTagId = planningTagsForGroup
                        .FirstOrDefault(y => x.Planning.ReportGroupPlanningTagId == y.Id)
                    //    .Any(t => t.PlanningTagId == y.Id))
                })
                .GroupBy(x => x.ReportGroupPlanningTagId,
                    (tag, enumerable) => new
                    {
                        planningTag = tag,
                        casesGroupedByEfromId = enumerable
                            .Select(y => y.planningCase)
                            .GroupBy(y => y.MicrotingSdkeFormId,
                                (eFormId, cases) => new
                                {
                                    eFormId, cases
                                })
                    });
            foreach (var groupedPlanningCase in groupedPlanningCases.Where(x => x.planningTag != null))
            {

                var reportModel = new ReportEformModel
                {
                    FromDate = $"{fromDate:yyyy-MM-dd}",
                    ToDate = $"{toDate:yyyy-MM-dd}",
                    GroupTagName = groupedPlanningCase.planningTag != null ? groupedPlanningCase.planningTag.Name : ""
                };

                foreach (var eformIdAndCases in groupedPlanningCase.casesGroupedByEfromId)
                {
                    var checkList = await sdkDbContext.CheckLists
                        .Where(x => x.Id == eformIdAndCases.eFormId)
                        .FirstOrDefaultAsync();
                    var hasChildCheckLists = sdkDbContext.CheckLists.Any(x => x.ParentId == checkList.Id);
                    var checkListTranslation = sdkDbContext.CheckListTranslations
                        .Where(x => x.CheckListId == checkList.Id)
                        .First(x => x.LanguageId == language.Id).Text;
                    var group = new ReportEformGroupModel
                    {
                        CheckListId = checkList.Id,
                        CheckListName = checkListTranslation
                    };

                    var fields = await core.Advanced_TemplateFieldReadAll(
                        checkList.Id, language);

                    foreach (var fieldDto in fields.Where(fieldDto => !excludedFieldTypes.Contains(fieldDto.FieldType)))
                    {
                        string text;
                        var queryFieldTranslation = sdkDbContext.FieldTranslations
                            .Where(x => x.FieldId == fieldDto.Id)
                            .Where(x => x.Text != string.Empty)
                            .AsQueryable();
                        if (queryFieldTranslation.Any(x => x.LanguageId == language.Id))
                        {
                            text = await queryFieldTranslation
                                .Where(x => x.LanguageId == language.Id)
                                .Select(x => x.Text)
                                .FirstOrDefaultAsync() ?? "";
                        }
                        else
                        {
                            text = await queryFieldTranslation
                                .Select(x => x.Text)
                                .FirstOrDefaultAsync() ?? "";
                        }

                        if (hasChildCheckLists)
                        {
                            var clTranslation =
                                await sdkDbContext.CheckListTranslations.FirstOrDefaultAsync(x =>
                                    x.CheckListId == fieldDto.CheckListId && x.LanguageId == language.Id);
                            if (checkListTranslation != clTranslation.Text)
                            {
                                text = $"{clTranslation.Text} - {text}";
                            }
                        }

                        var kvp = new KeyValuePair<int, string>(fieldDto.Id, text);

                        group.ItemHeaders.Add(kvp);
                    }

                    var templateCaseIds = eformIdAndCases.cases.Select(x => (int?)x.MicrotingSdkCaseId).ToArray();
                    // images
                    var allImagesFromCases = await sdkDbContext.FieldValues
                        .Include(x => x.UploadedData)
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Where(x => x.UploadedData.WorkflowState != Constants.WorkflowStates.Removed)
                        .Where(x => x.Field.FieldTypeId == 5) // magic number 5 - it is FieldTypes.Picture
                        .Where(x => templateCaseIds.Contains(x.CaseId))
                        .Where(x => x.UploadedDataId != null)
                        .OrderBy(x => x.CaseId)
                        .ToListAsync();

                    foreach (var planningCase in eformIdAndCases.cases.OrderBy(x => x.MicrotingSdkCaseDoneAt).ToList())
                    {
                        var planningNameTranslation =
                            await itemsPlanningPnDbContext.PlanningNameTranslation.FirstOrDefaultAsync(x =>
                                x.PlanningId == planningCase.PlanningId && x.LanguageId == language.Id);
                        foreach (var imageField in allImagesFromCases.Where(x =>
                                     x.CaseId == planningCase.MicrotingSdkCaseId))
                        {
                            var reportImages = new ReportImages
                            {
                                CaseId = planningCase.MicrotingSdkCaseId
                            };

                            if (planningNameTranslation != null)
                            {
                                reportImages.Label = $"{imageField.CaseId}; {planningNameTranslation.Name}";
                            }

                            if (!string.IsNullOrEmpty(imageField.Latitude))
                            {
                                reportImages.GeoLink =
                                    $"https://www.google.com/maps/place/{imageField.Latitude},{imageField.Longitude}";
                            }

                            if (!string.IsNullOrEmpty(imageField.UploadedData.FileName))
                            {
                                reportImages.ImageName = $"{imageField.UploadedData.Id}_700_{imageField.UploadedData.Checksum}{imageField.UploadedData.Extension}";
                            }

                            group.ImageNames.Add(reportImages);
                        }

                        var propertyName = "";

                        var areaRulePlanning =
                            await backendConfigurationPnDbContext.AreaRulePlannings.FirstOrDefaultAsync(x =>
                                x.ItemPlanningId == planningCase.PlanningId);

                        if (areaRulePlanning != null)
                        {
                            propertyName = backendConfigurationPnDbContext.Properties
                                .First(x => x.Id == areaRulePlanning.PropertyId).Name;
                        }
                        else
                        {
                            var areaRulePlanningVersion = await backendConfigurationPnDbContext
                                .AreaRulesPlanningVersions
                                .Where(x => x.ItemPlanningId == planningCase.PlanningId)
                                .OrderByDescending(x => x.Version)
                                .FirstOrDefaultAsync();

                            if (areaRulePlanningVersion != null)
                            {
                                propertyName = backendConfigurationPnDbContext.Properties
                                    .First(x => x.Id == areaRulePlanningVersion.PropertyId).Name;
                            }
                        }

                        var dbCase =
                            await sdkDbContext.Cases.FirstOrDefaultAsync(x => x.Id == planningCase.MicrotingSdkCaseId);
                        var workerId = sdkDbContext.SiteWorkers.First(x => x.SiteId == dbCase.SiteId).WorkerId;
                        var worker = await sdkDbContext.Workers.FirstOrDefaultAsync(x => x.Id == workerId);

                        if (dbCase == null)
                        {
                            logger.LogError($"Could not find case with id {planningCase.MicrotingSdkCaseId}");
                            continue;
                        }

                        if (planningNameTranslation != null)
                        {
                            var item = new ReportEformItemModel
                            {
                                Id = planningCase.Id,
                                ItemId = planningCase.PlanningId,
                                MicrotingSdkCaseId = planningCase.MicrotingSdkCaseId,
                                MicrotingSdkCaseDoneAt =
                                    TimeZoneInfo.ConvertTimeFromUtc((DateTime)planningCase.MicrotingSdkCaseDoneAt,
                                        timeZoneInfo),
                                ServerTime = TimeZoneInfo.ConvertTimeFromUtc((DateTime)dbCase.CreatedAt, timeZoneInfo),
                                eFormId = planningCase.MicrotingSdkeFormId,
                                DoneBy = planningCase.DoneByUserName,
                                ItemName = planningNameTranslation.Name,
                                ItemDescription = planningCase.Planning.Description,
                                PropertyName = propertyName,
                                EmployeeNo = worker.EmployeeNo
                            };


                            var caseFields = await sdkDbContext.FieldValues
                                .Where(x => x.CaseId == planningCase.MicrotingSdkCaseId &&
                                            x.WorkflowState != Constants.WorkflowStates.Removed)
                                .ToListAsync();

                            foreach (var fieldDto in fields)
                            {
                                var caseField = caseFields.FirstOrDefault(x => x.FieldId == fieldDto.Id);
                                if (caseField != null)
                                {
                                    switch (fieldDto.FieldType)
                                    {
                                        case Constants.FieldTypes.MultiSelect:
                                            var keyLst = string.IsNullOrEmpty(caseField.Value)
                                                ? []
                                                : caseField.Value.Split('|').ToList();
                                            var valueReadable = "";
                                            foreach (var key in keyLst)
                                            {
                                                if (!string.IsNullOrEmpty(key))
                                                {
                                                    var fieldOption =
                                                        await sdkDbContext.FieldOptions.FirstOrDefaultAsync(x =>
                                                            x.FieldId == caseField.FieldId && x.Key == key);
                                                    if (fieldOption != null)
                                                    {
                                                        var fieldOptionTranslation =
                                                            await sdkDbContext.FieldOptionTranslations.FirstAsync(x =>
                                                                x.FieldOptionId == fieldOption.Id &&
                                                                x.LanguageId == language.Id);
                                                        if (valueReadable != "")
                                                        {
                                                            valueReadable += '|';
                                                        }

                                                        valueReadable += fieldOptionTranslation.Text;
                                                    }
                                                }
                                            }

                                            item.CaseFields.Add(
                                                new KeyValuePair<string, string>("string", valueReadable));
                                            break;
                                        case Constants.FieldTypes.SingleSelect:
                                            var fo = await sdkDbContext.FieldOptions.FirstOrDefaultAsync(x =>
                                                x.FieldId == caseField.FieldId && x.Key == caseField.Value);
                                            if (fo != null)
                                            {
                                                var fieldOptionTranslation =
                                                    await sdkDbContext.FieldOptionTranslations.FirstAsync(x =>
                                                        x.FieldOptionId == fo.Id && x.LanguageId == language.Id);
                                                item.CaseFields.Add(new KeyValuePair<string, string>("string",
                                                    fieldOptionTranslation.Text));
                                            }
                                            else
                                            {
                                                item.CaseFields.Add(new KeyValuePair<string, string>("string", ""));
                                            }

                                            break;
                                        case Constants.FieldTypes.EntitySearch or
                                            Constants.FieldTypes.EntitySelect:
                                            if (!string.IsNullOrEmpty(caseField.Value) && caseField.Value != "null")
                                            {
                                                var id = int.Parse(caseField.Value);
                                                var match =
                                                    await sdkDbContext.EntityItems.AsNoTracking()
                                                        .FirstOrDefaultAsync(x => x.Id == id);
                                                item.CaseFields.Add(
                                                    new KeyValuePair<string, string>("string", match.Name));
                                            }
                                            else
                                            {
                                                item.CaseFields.Add(new KeyValuePair<string, string>("string", ""));
                                            }

                                            break;
                                        case Constants.FieldTypes.Picture
                                            or Constants.FieldTypes.SaveButton
                                            or Constants.FieldTypes.Signature
                                            or Constants.FieldTypes.None
                                            or Constants.FieldTypes.FieldGroup:
                                            break;
                                        case Constants.FieldTypes.Number
                                            or Constants.FieldTypes.NumberStepper:
                                            item.CaseFields.Add(caseField.Value != null
                                                ? new KeyValuePair<string, string>("number",
                                                    caseField.Value.Replace(",", "."))
                                                : new KeyValuePair<string, string>("number", ""));
                                            break;
                                        case Constants.FieldTypes.Date:
                                            item.CaseFields.Add(
                                                new KeyValuePair<string, string>("date", caseField.Value));
                                            break;
                                        case Constants.FieldTypes.CheckBox:
                                            if (caseField.Value is "true" or "false")
                                            {
                                                item.CaseFields.Add(new KeyValuePair<string, string>("string",
                                                    caseField.Value == "true" ? "checked" : "unchecked"));
                                            }
                                            else
                                            {
                                                item.CaseFields.Add(
                                                    new KeyValuePair<string, string>("string", caseField.Value));
                                            }

                                            break;
                                        default:
                                            item.CaseFields.Add(
                                                new KeyValuePair<string, string>("string", caseField.Value));
                                            break;
                                    }
                                }
                                else
                                {
                                    item.CaseFields.Add(new KeyValuePair<string, string>("string", ""));
                                }
                            }

                            item.ImagesCount = allImagesFromCases
                                .Where(x => x.CaseId == planningCase.MicrotingSdkCaseId)
                                .Select(x => x.Id)
                                .Count();

                            group.Items.Add(item);
                        }
                    }

                    reportModel.GroupEform.Add(group);
                }

                reportModel.NameTagsInEndPage.AddRange(itemsPlanningPnDbContext.PlanningTags
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => model.TagIds.Any(y => y == x.Id))
                    .Select(x => x.Name));

                result.Add(reportModel);
            }

            result = result.OrderBy(x => x.GroupTagName).ToList();

            if (result.Any())
            {
                return new OperationDataResult<List<ReportEformModel>>(true, result);
            }

            return new OperationDataResult<List<ReportEformModel>>(false,
                backendConfigurationLocalizationService.GetString("NoDataInSelectedPeriod"));

        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            logger.LogError(e.Message);
            logger.LogError(e.StackTrace);
            return new OperationDataResult<List<ReportEformModel>>(false,
                backendConfigurationLocalizationService.GetString("ErrorWhileGeneratingReport") + e.Message);
        }
    }

    public async Task<OperationDataResult<Stream>> GenerateReportFile(GenerateReportModel model, bool version2)
    {

        if (version2)
        {
            try
            {
                var reportDataResult = await GenerateReportV2(model, true);
                if (!reportDataResult.Success)
                {
                    return new OperationDataResult<Stream>(false, reportDataResult.Message);
                }

                switch (model.Type)
                {
                    case "docx":
                    {
                        var wordDataResult = await wordService
                            .GenerateWordDashboard(reportDataResult.Model);
                        if (!wordDataResult.Success)
                        {
                            return new OperationDataResult<Stream>(false, wordDataResult.Message);
                        }

                        return new OperationDataResult<Stream>(true, wordDataResult.Model);
                    }
                    case "xlsx":
                    {
                        var wordDataResult = await excelService
                            .GenerateExcelDashboard(reportDataResult.Model);
                        if (!wordDataResult.Success)
                        {
                            return new OperationDataResult<Stream>(false, wordDataResult.Message);
                        }

                        return new OperationDataResult<Stream>(true, wordDataResult.Model);
                    }
                    case "pdf":
                    {
                        // get word report and save him
                        var wordDataResult = await wordService
                            .GenerateWordDashboard(reportDataResult.Model);
                        if (!wordDataResult.Success)
                        {
                            return new OperationDataResult<Stream>(false, wordDataResult.Message);
                        }

                        var directoryPath = Path.Combine(Path.GetTempPath(), "results");
                        Directory.CreateDirectory(directoryPath);
                        var resultDocumentDocx = Path.Combine(directoryPath, $"{DateTime.Now.Ticks}.docx");
                        var resultDocumentPdf = resultDocumentDocx.Replace("docx", "pdf");
                        logger.LogInformation($"Saving document to {resultDocumentDocx}");

                        await using (var fileStream = File.Create(resultDocumentDocx))
                        {
                            wordDataResult.Model.Seek(0, SeekOrigin.Begin);
                            await wordDataResult.Model.CopyToAsync(fileStream);
                        }

                        logger.LogInformation($"docx saved to {resultDocumentDocx}");
                        logger.LogInformation($"Converting to pdf {resultDocumentPdf}");
                        // convert file to pdf
                        ReportHelper.ConvertToPdf(resultDocumentDocx, directoryPath);

                        // read converted file and return
                        Stream result = File.Open(resultDocumentPdf, FileMode.Open);
                        return new OperationDataResult<Stream>(true, result);
                    }
                    default:
                    {
                        throw new NotImplementedException($"Type {reportDataResult.Model} not implemented");
                    }
                }

            }
            catch (Exception e)
            {
                SentrySdk.CaptureException(e);
                logger.LogError(e.Message);
                logger.LogError(e.StackTrace);
                return new OperationDataResult<Stream>(
                    false,
                    backendConfigurationLocalizationService.GetString("ErrorWhileGeneratingReportFile"));
            }
        }

        try
        {
            var reportDataResult = await GenerateReport(model, true);
            if (!reportDataResult.Success)
            {
                return new OperationDataResult<Stream>(false, reportDataResult.Message);
            }

            switch (model.Type)
            {
                case "docx":
                {
                    var wordDataResult = await wordService
                        .GenerateWordDashboard(reportDataResult.Model);
                    if (!wordDataResult.Success)
                    {
                        return new OperationDataResult<Stream>(false, wordDataResult.Message);
                    }

                    return new OperationDataResult<Stream>(true, wordDataResult.Model);
                }
                case "xlsx":
                {
                    var wordDataResult = await excelService
                        .GenerateExcelDashboard(reportDataResult.Model);
                    if (!wordDataResult.Success)
                    {
                        return new OperationDataResult<Stream>(false, wordDataResult.Message);
                    }

                    return new OperationDataResult<Stream>(true, wordDataResult.Model);
                }
                case "pdf":
                {
                    // get word report and save him
                    var wordDataResult = await wordService
                        .GenerateWordDashboard(reportDataResult.Model);
                    if (!wordDataResult.Success)
                    {
                        return new OperationDataResult<Stream>(false, wordDataResult.Message);
                    }

                    var directoryPath = Path.Combine(Path.GetTempPath(), "results");
                    Directory.CreateDirectory(directoryPath);
                    var resultDocumentDocx = Path.Combine(directoryPath, $"{DateTime.Now.Ticks}.docx");
                    var resultDocumentPdf = resultDocumentDocx.Replace("docx", "pdf");
                    logger.LogInformation($"Saving document to {resultDocumentDocx}");

                    await using (var fileStream = File.Create(resultDocumentDocx))
                    {
                        wordDataResult.Model.Seek(0, SeekOrigin.Begin);
                        await wordDataResult.Model.CopyToAsync(fileStream);
                    }

                    logger.LogInformation($"docx saved to {resultDocumentDocx}");
                    logger.LogInformation($"Converting to pdf {resultDocumentPdf}");
                    // convert file to pdf
                    ReportHelper.ConvertToPdf(resultDocumentDocx, directoryPath);

                    // read converted file and return
                    Stream result = File.Open(resultDocumentPdf, FileMode.Open);
                    return new OperationDataResult<Stream>(true, result);
                }
                default:
                {
                    throw new NotImplementedException($"Type {reportDataResult.Model} not implemented");
                }
            }

        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            logger.LogError(e.Message);
            logger.LogError(e.StackTrace);
            return new OperationDataResult<Stream>(
                false,
                backendConfigurationLocalizationService.GetString("ErrorWhileGeneratingReportFile"));
        }
    }

    public async Task<OperationResult> Update(ReplyRequest model)
    {
        var checkListValueList = new List<string>();
        var fieldValueList = new List<string>();
        var core = await coreHelper.GetCore();
        var language = await userService.GetCurrentUserLanguage();
        try
        {
            model.ElementList.ForEach(element =>
            {
                checkListValueList.AddRange(CaseUpdateHelper.GetCheckList(element));
                fieldValueList.AddRange(CaseUpdateHelper.GetFieldList(element));
            });
        }
        catch (Exception ex)
        {
            SentrySdk.CaptureException(ex);
            Log.LogException(ex.Message);
            Log.LogException(ex.StackTrace);
            return new OperationResult(false,
                $"{backendConfigurationLocalizationService.GetString("CaseCouldNotBeUpdated")} Exception: {ex.Message}");
        }

        try
        {
            await core.CaseUpdate(model.Id, fieldValueList, checkListValueList);
            await core.CaseUpdateFieldValues(model.Id, language);
            var sdkDbContext = core.DbContextHelper.GetDbContext();

            var foundCase = await sdkDbContext.Cases
                .Where(x => x.Id == model.Id
                            && x.WorkflowState != Constants.WorkflowStates.Removed)
                .FirstOrDefaultAsync();

            if (foundCase != null)
            {

                if (foundCase.DoneAt != null)
                {
                    var newDoneAt = new DateTime(model.DoneAt.Year, model.DoneAt.Month, model.DoneAt.Day,
                        foundCase.DoneAt.Value.Hour, foundCase.DoneAt.Value.Minute, foundCase.DoneAt.Value.Second);
                    foundCase.DoneAtUserModifiable = newDoneAt;
                }

                foundCase.Status = 100;
                await foundCase.Update(sdkDbContext);
                var planningCase =
                    await itemsPlanningPnDbContext.PlanningCases.SingleAsync(x => x.MicrotingSdkCaseId == model.Id);
                var planningCaseSite = await itemsPlanningPnDbContext.PlanningCaseSites.SingleOrDefaultAsync(x =>
                    x.MicrotingSdkCaseId == model.Id && x.PlanningCaseId == planningCase.Id);

                if (planningCaseSite == null)
                {
                    planningCaseSite = new PlanningCaseSite
                    {
                        MicrotingSdkCaseId = model.Id,
                        PlanningCaseId = planningCase.Id,
                        MicrotingSdkeFormId = planningCase.MicrotingSdkeFormId,
                        PlanningId = planningCase.PlanningId,
                        Status = 100,
                        MicrotingSdkSiteId = (int)foundCase.SiteId!
                    };
                    await planningCaseSite.Create(itemsPlanningPnDbContext);
                }

                planningCaseSite.MicrotingSdkCaseDoneAt = foundCase.DoneAtUserModifiable;
                planningCaseSite = await SetFieldValue(planningCaseSite, language);
                await planningCaseSite.Update(itemsPlanningPnDbContext);

                planningCase.MicrotingSdkCaseDoneAt = foundCase.DoneAtUserModifiable;
                planningCase = await SetFieldValue(planningCase, language);
                await planningCase.Update(itemsPlanningPnDbContext);
            }
            else
            {
                return new OperationResult(false, backendConfigurationLocalizationService.GetString("CaseNotFound"));
            }

            return new OperationResult(true, backendConfigurationLocalizationService.GetString("CaseHasBeenUpdated"));
        }
        catch (Exception ex)
        {
            SentrySdk.CaptureException(ex);
            Log.LogException(ex.Message);
            Log.LogException(ex.StackTrace);
            return new OperationResult(false,
                backendConfigurationLocalizationService.GetString("CaseCouldNotBeUpdated") +
                $" Exception: {ex.Message}");
        }
    }

    private async Task<PlanningCaseSite> SetFieldValue(PlanningCaseSite planningCaseSite, Language language)
    {
        var planning = itemsPlanningPnDbContext.Plannings.SingleOrDefault(x => x.Id == planningCaseSite.PlanningId);
        var caseIds = new List<int>
        {
            planningCaseSite.MicrotingSdkCaseId
        };

        var core = await coreHelper.GetCore();
        var fieldValues = await core.Advanced_FieldValueReadList(caseIds, language);

        if (planning == null) return planningCaseSite;
        if (planning.NumberOfImagesEnabled)
        {
            planningCaseSite.NumberOfImages = 0;
            foreach (var fieldValue in fieldValues)
            {
                if (fieldValue.FieldType == Constants.FieldTypes.Picture)
                {
                    if (fieldValue.UploadedData != null)
                    {
                        planningCaseSite.NumberOfImages += 1;
                    }
                }
            }
        }

        return planningCaseSite;
    }

    private async Task<PlanningCase> SetFieldValue(PlanningCase planningCase, Language language)
    {
        var core = await coreHelper.GetCore();
        var planning = await itemsPlanningPnDbContext.Plannings
            .SingleOrDefaultAsync(x => x.Id == planningCase.PlanningId).ConfigureAwait(false);
        var caseIds = new List<int> { planningCase.MicrotingSdkCaseId };
        var fieldValues = await core.Advanced_FieldValueReadList(caseIds, language).ConfigureAwait(false);

        if (planning == null) return planningCase;
        if (planning.NumberOfImagesEnabled)
        {
            planningCase.NumberOfImages = 0;
            foreach (var fieldValue in fieldValues)
            {
                if (fieldValue.FieldType == Constants.FieldTypes.Picture)
                {
                    if (fieldValue.UploadedData != null)
                    {
                        planningCase.NumberOfImages += 1;
                    }
                }
            }
        }

        return planningCase;
    }
}