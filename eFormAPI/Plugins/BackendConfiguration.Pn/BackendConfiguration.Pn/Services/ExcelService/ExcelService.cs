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

using System;
using System.Diagnostics;
using System.Globalization;
using BackendConfiguration.Pn.Infrastructure.Models.Report;
using Microsoft.Extensions.Logging;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;

namespace BackendConfiguration.Pn.Services.ExcelService;

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BackendConfigurationLocalizationService;
using ClosedXML.Excel;
using Infrastructure.Models.TaskManagement;
using Microsoft.EntityFrameworkCore;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;

public class ExcelService: IExcelService
{
    private readonly ILogger<ExcelService> _logger;
    private readonly IBackendConfigurationLocalizationService _localizationService;
    private readonly BackendConfigurationPnDbContext _backendConfigurationPnDbContext;

    public ExcelService(ILogger<ExcelService> logger,
        IBackendConfigurationLocalizationService localizationService,
        BackendConfigurationPnDbContext backendConfigurationPnDbContext)
    {
        _logger = logger;
        _localizationService = localizationService;
        _backendConfigurationPnDbContext = backendConfigurationPnDbContext;
    }

    public async Task<Stream> GenerateWorkOrderCaseReport(TaskManagementFiltersModel filtersModel, List<WorkorderCaseModel> workOrderCaseModels)
    {
        try
        {
            var propertyName = await _backendConfigurationPnDbContext.Properties
                .Where(x => x.Id == filtersModel.PropertyId)
                .Select(x => x.Name)
                .FirstOrDefaultAsync().ConfigureAwait(false);
            Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "results"));

            var resultDocument = Path.Combine(Path.GetTempPath(), "results",
                $"{propertyName}_{filtersModel.AreaName}.xlsx");

            IXLWorkbook wb = new XLWorkbook();
            var sheetName = $"{propertyName}_{filtersModel.AreaName}"
                .Replace(":", "")
                .Replace("\\", "")
                .Replace("/", "")
                .Replace("?", "")
                .Replace("*", "")
                .Replace("[", "")
                .Replace("]", "");
            if (sheetName.Length > 31)
            {
                sheetName = sheetName.Substring(0, 31);
            }
            var worksheet = wb.Worksheets.Add(sheetName);

            // table with selected filters
            //* header table
            const int startColumnForHeaderTable = 1;
            var currentRow = 1;
            var currentColumn = startColumnForHeaderTable;
            worksheet.Range(currentRow, currentColumn, currentRow, currentColumn + 5).Cells().Style.Font.Bold = true;
            // worksheet.Range(currentRow, currentColumn, currentRow, currentColumn).Cells().Style.Alignment
            //     .Horizontal = XLAlignmentHorizontalValues.Center;
            SetBorders(worksheet.Range(currentRow, currentColumn, currentRow + 1, currentColumn + 5));
            worksheet.Cell(currentRow, currentColumn++).Value = _localizationService.GetString("Property");
            worksheet.Cell(currentRow, currentColumn++).Value = _localizationService.GetString("PropertyArea");
            worksheet.Cell(currentRow, currentColumn++).Value = _localizationService.GetString("CreatedBy");
            worksheet.Cell(currentRow, currentColumn++).Value = _localizationService.GetString("LastAssignedTo");
            worksheet.Cell(currentRow, currentColumn++).Value = _localizationService.GetString("Status");
            worksheet.Cell(currentRow++, currentColumn).Value = _localizationService.GetString("Date");

            currentColumn = startColumnForHeaderTable;
            //* table data
            worksheet.Cell(currentRow, currentColumn++).Value = await _backendConfigurationPnDbContext.Properties
                .Where(x => x.Id == filtersModel.PropertyId).Select(x => x.Name).FirstAsync().ConfigureAwait(false);
            worksheet.Cell(currentRow, currentColumn++).Value =
                string.IsNullOrEmpty(filtersModel.AreaName) ? "" : filtersModel.AreaName;
            worksheet.Cell(currentRow, currentColumn++).Value =
                string.IsNullOrEmpty(filtersModel.CreatedBy) ? "" : filtersModel.CreatedBy;
            worksheet.Cell(currentRow, currentColumn++).Value = string.IsNullOrEmpty(filtersModel.LastAssignedTo)
                ? ""
                : filtersModel.LastAssignedTo;
            worksheet.Cell(currentRow, currentColumn++).Value = string.IsNullOrEmpty(filtersModel.GetStringStatus())
                ? ""
                : _localizationService.GetString(filtersModel.GetStringStatus());
            var dateValue = !filtersModel.DateFrom.HasValue ? "" : filtersModel.DateFrom.Value.ToString("dd.MM.yyyy");
            dateValue += !filtersModel.DateTo.HasValue ? "" : "-";
            dateValue+= !filtersModel.DateTo.HasValue
                ? ""
                : filtersModel.DateTo.Value.ToString("dd.MM.yyyy");
            worksheet.Cell(currentRow, currentColumn).Value = dateValue;

            // worksheet.Cell(currentRow, currentColumn).Value
            // worksheet.Cell(currentRow++, currentColumn).Value

            const int startColumnForDataTable = 1;
            currentRow++;
            currentRow++;
            currentColumn = startColumnForDataTable;
            SetBorders(worksheet.Range(currentRow, startColumnForDataTable, workOrderCaseModels.Count + currentRow,
                startColumnForDataTable + 10));
            worksheet.Range(currentRow, startColumnForDataTable, currentRow, startColumnForDataTable + 10).Cells().Style
                .Font.Bold = true;
            worksheet.Range(currentRow, startColumnForDataTable, currentRow, startColumnForDataTable + 10).Cells().Style
                .Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

            // table report
            //* header table
            worksheet.Cell(currentRow, currentColumn++).Value = _localizationService.GetString("Id");
            worksheet.Cell(currentRow, currentColumn++).Value = _localizationService.GetString("CreatedDate");
            worksheet.Cell(currentRow, currentColumn++).Value = _localizationService.GetString("Property");
            worksheet.Cell(currentRow, currentColumn++).Value = _localizationService.GetString("Area");
            worksheet.Cell(currentRow, currentColumn++).Value = _localizationService.GetString("CreatedBy1");
            worksheet.Cell(currentRow, currentColumn++).Value = _localizationService.GetString("CreatedBy2");
            worksheet.Cell(currentRow, currentColumn++).Value = _localizationService.GetString("LastAssignedTo");
            worksheet.Cell(currentRow, currentColumn++).Value = _localizationService.GetString("Description");
            worksheet.Cell(currentRow, currentColumn++).Value = _localizationService.GetString("LastUpdateDate");
            worksheet.Cell(currentRow, currentColumn++).Value = _localizationService.GetString("LastUpdatedBy");
            worksheet.Cell(currentRow++, currentColumn).Value = _localizationService.GetString("Status");


            //* table data
            foreach (var workOrderCaseModel in workOrderCaseModels)
            {
                currentColumn = startColumnForDataTable;

                worksheet.Cell(currentRow, currentColumn++).Value = workOrderCaseModel.Id;
                worksheet.Cell(currentRow, currentColumn++).Value = workOrderCaseModel.CaseInitiated.ToString("dd.MM.yyyy");
                worksheet.Cell(currentRow, currentColumn++).Value = workOrderCaseModel.PropertyName;
                worksheet.Cell(currentRow, currentColumn++).Value = workOrderCaseModel.AreaName;
                worksheet.Cell(currentRow, currentColumn++).Value = workOrderCaseModel.CreatedByName;
                worksheet.Cell(currentRow, currentColumn++).Value = workOrderCaseModel.CreatedByText;
                worksheet.Cell(currentRow, currentColumn++).Value = workOrderCaseModel.LastAssignedTo;
                worksheet.Cell(currentRow, currentColumn++).Value = workOrderCaseModel.Description.Replace("<br>", "").Replace("<br />", "\n");
                worksheet.Cell(currentRow, currentColumn++).Value = workOrderCaseModel.LastUpdateDate.HasValue
                    ? workOrderCaseModel.LastUpdateDate.Value.ToString("dd.MM.yyyy")
                    : "";
                worksheet.Cell(currentRow, currentColumn++).Value = workOrderCaseModel.LastUpdatedBy;
                worksheet.Cell(currentRow++, currentColumn).Value = _localizationService.GetString(workOrderCaseModel.Status);
            }

            // worksheet.Columns(startColumnForDataTable, currentColumn).AdjustToContents(); // This does not work inside Docker container

            wb.SaveAs(resultDocument);

            Stream result = File.Open(resultDocument, FileMode.Open);
            return result;
        } catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            throw;
        }
    }

    private static void SetBorders(IXLRangeBase range, XLBorderStyleValues valueStyle = XLBorderStyleValues.Thin)
    {
        range.Cells().Style.Border.SetBottomBorder(valueStyle);
        range.Cells().Style.Border.SetLeftBorder(valueStyle);
        range.Cells().Style.Border.SetRightBorder(valueStyle);
        range.Cells().Style.Border.SetTopBorder(valueStyle);
    }

    #pragma warning disable CS1998
        public async Task<OperationDataResult<Stream>> GenerateExcelDashboard(List<ReportEformModel> reportModel)
#pragma warning restore CS1998
        {
            try
            {
                Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "results"));

                var timeStamp = $"{DateTime.UtcNow:yyyyMMdd}_{DateTime.UtcNow:hhmmss}";

                var resultDocument = Path.Combine(Path.GetTempPath(), "results",
                    $"{timeStamp}_.xlsx");

                IXLWorkbook wb = new XLWorkbook();

                foreach (var eformModel in reportModel)
                {
                    if (eformModel.FromDate != null)
                    {
                        var x = 0;
                        var y = 0;
                        var sheetName = $"{eformModel.CheckListId} - {eformModel.CheckListName}";

                        sheetName = sheetName
                            .Replace(":", "")
                            .Replace("\\", "")
                            .Replace("/", "")
                            .Replace("?", "")
                            .Replace("*", "")
                            .Replace("[", "")
                            .Replace("]", "");

                        if (sheetName.Length > 30)
                        {
                            sheetName = sheetName.Substring(0, 30);
                        }
                        var worksheet = wb.Worksheets.Add(sheetName);


                        if (eformModel.Items.Any())
                        {
                            worksheet.Cell(x + 1, y + 1).Value = _localizationService.GetString("Id");
                            worksheet.Cell(x + 1, y + 1).Style.Font.Bold = true;
                            y++;
                            worksheet.Cell(x + 1, y + 1).Value = _localizationService.GetString("Property");
                            worksheet.Cell(x + 1, y + 1).Style.Font.Bold = true;
                            y++;
                            worksheet.Cell(x + 1, y + 1).Value = _localizationService.GetString("CreatedAt");
                            worksheet.Cell(x + 1, y + 1).Style.Font.Bold = true;
                            y++;
                            worksheet.Cell(x + 1, y + 1).Value = _localizationService.GetString("DoneBy");
                            worksheet.Cell(x + 1, y + 1).Style.Font.Bold = true;
                            y++;
                            worksheet.Cell(x + 1, y + 1).Value = _localizationService.GetString("ItemName");
                            worksheet.Cell(x + 1, y + 1).Style.Font.Bold = true;
                            foreach (var itemHeader in eformModel.ItemHeaders)
                            {
                                y++;
                                worksheet.Cell(x + 1, y + 1).Value = itemHeader.Value;
                                worksheet.Cell(x + 1, y + 1).Style.Font.Bold = true;
                            }
                        }

                        x = 1;
                        foreach (var dataModel in eformModel.Items)
                        {
                            y = 0;
                            worksheet.Cell(x + 1, y + 1).Value = dataModel.MicrotingSdkCaseId;
                            y++;
                            worksheet.Cell(x + 1, y + 1).Value = dataModel.PropertyName;
                            y++;
                            worksheet.Cell(x + 1, y + 1).Value = $"{dataModel.MicrotingSdkCaseDoneAt:dd.MM.yyyy HH:mm:ss}";
                            y++;
                            worksheet.Cell(x + 1, y + 1).Value = dataModel.DoneBy;
                            y++;
                            worksheet.Cell(x + 1, y + 1).Value = dataModel.ItemName;
                            y++;
                            foreach (var dataModelCaseField in dataModel.CaseFields)
                            {
                                if (dataModelCaseField.Value == "checked")
                                {
                                    worksheet.Cell(x + 1, y + 1).Value = 1;
                                }
                                else
                                {
                                    var value = dataModelCaseField.Value == "unchecked" ? "0" : dataModelCaseField.Value == "checked" ? "1" : dataModelCaseField.Value;

                                    switch (dataModelCaseField.Key)
                                    {
                                        case "date":
                                            worksheet.Cell(x + 1, y + 1).SetValue(value);
                                            //worksheet.Cell(x + 1, y + 1).DataType = XLDataType.DateTime;
                                            break;
                                        case "number":
                                            try
                                            {
                                                if (!string.IsNullOrEmpty(value))
                                                {
                                                    var number = Double.Parse(value, CultureInfo.InvariantCulture);
                                                    worksheet.Cell(x + 1, y + 1).SetValue(number);
                                                }
                                            }
                                            catch (Exception e)
                                            {
                                                Console.WriteLine(e);
                                                throw;
                                            }

                                            //worksheet.Cell(x+1, y+1).Style.NumberFormat.Format = "0.00";
                                            //worksheet.Cell(x + 1, y + 1).DataType = XLDataType.Number;
                                            break;
                                        default:
                                            worksheet.Cell(x + 1, y + 1).SetValue("'" + value);
                                            //worksheet.Cell(x + 1, y + 1).DataType = XLDataType.Text;
                                            break;
                                    }
                                }

                                y++;
                            }

                            x++;
                        }
                    }
                }
                wb.SaveAs(resultDocument);

                Stream result = File.Open(resultDocument, FileMode.Open);
                return new OperationDataResult<Stream>(true, result);
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

}