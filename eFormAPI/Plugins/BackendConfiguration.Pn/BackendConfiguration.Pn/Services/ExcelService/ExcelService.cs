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
    private readonly IBackendConfigurationLocalizationService _localizationService;
    private readonly BackendConfigurationPnDbContext _backendConfigurationPnDbContext;

    public ExcelService(
        IBackendConfigurationLocalizationService localizationService,
        BackendConfigurationPnDbContext backendConfigurationPnDbContext)
    {
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
                .FirstOrDefaultAsync();
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
                .Where(x => x.Id == filtersModel.PropertyId).Select(x => x.Name).FirstAsync();
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
                worksheet.Cell(currentRow, currentColumn++).Value = workOrderCaseModel.Description;
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
}