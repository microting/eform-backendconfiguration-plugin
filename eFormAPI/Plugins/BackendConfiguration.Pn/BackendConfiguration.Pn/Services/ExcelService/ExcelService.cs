using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Report;
using BackendConfiguration.Pn.Infrastructure.Models.TaskManagement;
using BackendConfiguration.Pn.Infrastructure.Models.TaskTracker;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;

namespace BackendConfiguration.Pn.Services.ExcelService;

public class ExcelService(
    ILogger<ExcelService> logger,
    IBackendConfigurationLocalizationService localizationService,
    BackendConfigurationPnDbContext backendConfigurationPnDbContext,
    IUserService userService,
    IEFormCoreService coreHelper)
    : IExcelService
{
    private readonly IUserService _userService = userService;

    public async Task<Stream> GenerateWorkOrderCaseReport(TaskManagementFiltersModel filtersModel,
        List<WorkorderCaseModel> workOrderCaseModels)
    {
        try
        {
            var filtersLastAssignedTo = "";
            if (filtersModel.LastAssignedTo.HasValue && filtersModel.LastAssignedTo.Value != 0)
            {
                var core = await coreHelper.GetCore();
                var sdkDbContext = core.DbContextHelper.GetDbContext();
                filtersLastAssignedTo = await sdkDbContext.Sites
                    .Where(x => x.Id == filtersModel.LastAssignedTo.Value)
                    .Select(x => x.Name)
                    .FirstOrDefaultAsync();
            }

            var propertyName = await backendConfigurationPnDbContext.Properties
                .Where(x => x.Id == filtersModel.PropertyId)
                .Select(x => x.Name)
                .FirstOrDefaultAsync().ConfigureAwait(false);

            var resultDocument = Path.Combine(Path.GetTempPath(), "results",
                $"{propertyName}_{filtersModel.AreaName}.xlsx");

            using (var spreadsheetDocument =
                   SpreadsheetDocument.Create(resultDocument, SpreadsheetDocumentType.Workbook))
            {
                var workbookPart = spreadsheetDocument.AddWorkbookPart();
                workbookPart.Workbook = new Workbook();

                var sheets = workbookPart.Workbook.AppendChild(new Sheets());

                var sheetName = CreateSafeSheetName($"{propertyName}_{filtersModel.AreaName}");
                var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
                worksheetPart.Worksheet = new Worksheet(new SheetData());

                var sheet = new Sheet
                {
                    Id = workbookPart.GetIdOfPart(worksheetPart),
                    SheetId = 1,
                    Name = sheetName
                };
                sheets.Append(sheet);

                var sheetData = worksheetPart.Worksheet.GetFirstChild<SheetData>();

                // Create header row
                CreateHeaderRow(sheetData, localizationService);

                // Add data rows
                PopulateDataRows(sheetData, workOrderCaseModels);

                workbookPart.Workbook.Save();
            }

            Stream result = File.Open(resultDocument, FileMode.Open);
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex.Message);
            throw;
        }
    }

    public async Task<OperationDataResult<Stream>> GenerateExcelDashboard(List<OldReportEformModel> reportModel)
    {
        try
        {
            // Create directory for results if it doesn't exist
            Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "results"));
            var timeStamp = $"{DateTime.UtcNow:yyyyMMdd}_{DateTime.UtcNow:hhmmss}";
            var resultDocument = Path.Combine(Path.GetTempPath(), "results", $"{timeStamp}_.xlsx");

            using (var spreadsheetDocument =
                   SpreadsheetDocument.Create(resultDocument, SpreadsheetDocumentType.Workbook))
            {
                var workbookPart = spreadsheetDocument.AddWorkbookPart();
                workbookPart.Workbook = new Workbook();

                var sheets = workbookPart.Workbook.AppendChild(new Sheets());

                foreach (var eformModel in reportModel)
                {
                    if (eformModel.FromDate != null)
                    {
                        var sheetName = $"{eformModel.CheckListId} - {eformModel.CheckListName}";
                        sheetName = CreateSafeSheetName(sheetName);

                        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
                        worksheetPart.Worksheet = new Worksheet(new SheetData());
                        var sheetData = worksheetPart.Worksheet.GetFirstChild<SheetData>();

                        var sheet = new Sheet
                        {
                            Id = workbookPart.GetIdOfPart(worksheetPart),
                            SheetId = (uint)(sheets.Count() + 1), // Increment sheet ID
                            Name = sheetName
                        };
                        sheets.Append(sheet);

                        // Create header row
                        var headerRow = new Row();
                        headerRow.Append(
                            ConstructCell(localizationService.GetString("Id"), CellValues.String),
                            ConstructCell(localizationService.GetString("Property"), CellValues.String),
                            ConstructCell(localizationService.GetString("SubmittedDate"), CellValues.String),
                            ConstructCell(localizationService.GetString("DoneBy"), CellValues.String),
                            ConstructCell(localizationService.GetString("ItemName"), CellValues.String)
                        );

                        foreach (var itemHeader in eformModel.ItemHeaders)
                        {
                            headerRow.Append(ConstructCell(itemHeader.Value, CellValues.String));
                        }

                        sheetData.AppendChild(headerRow);

                        // Populate data rows
                        foreach (var dataModel in eformModel.Items)
                        {
                            var dataRow = new Row();
                            dataRow.Append(
                                ConstructCell(dataModel.MicrotingSdkCaseId.ToString(), CellValues.String),
                                ConstructCell(dataModel.PropertyName, CellValues.String),
                                ConstructCell(dataModel.MicrotingSdkCaseDoneAt?.ToString("dd.MM.yyyy HH:mm:ss"),
                                    CellValues.String),
                                ConstructCell(dataModel.DoneBy, CellValues.String),
                                ConstructCell(dataModel.ItemName, CellValues.String)
                            );

                            foreach (var dataModelCaseField in dataModel.CaseFields)
                            {
                                var value = dataModelCaseField.Value switch
                                {
                                    "checked" => "1",
                                    "unchecked" => "0",
                                    _ => dataModelCaseField.Value
                                };

                                switch (dataModelCaseField.Key)
                                {
                                    case "date":
                                        if (DateTime.TryParse(value, out var dateValue))
                                        {
                                            dataRow.Append(ConstructCell(dateValue.ToString("dd.MM.yyyy"),
                                                CellValues.String));
                                        }
                                        else
                                        {
                                            dataRow.Append(ConstructCell(value, CellValues.String));
                                        }

                                        break;
                                    case "number":
                                        if (double.TryParse(value, out var numberValue))
                                        {
                                            dataRow.Append(ConstructCell(
                                                numberValue.ToString(CultureInfo.InvariantCulture),
                                                CellValues.Number));
                                        }
                                        else
                                        {
                                            dataRow.Append(ConstructCell(value, CellValues.String));
                                        }

                                        break;
                                    default:
                                        dataRow.Append(ConstructCell(value, CellValues.String));
                                        break;
                                }
                            }

                            sheetData.AppendChild(dataRow);
                        }
                    }
                }

                workbookPart.Workbook.Save();
            }

            Stream result = File.Open(resultDocument, FileMode.Open);
            return new OperationDataResult<Stream>(true, result);
        }
        catch (Exception e)
        {
            logger.LogError(e.Message);
            return new OperationDataResult<Stream>(false,
                localizationService.GetString("ErrorWhileCreatingWordFile"));
        }
    }

    public async Task<OperationDataResult<Stream>> GenerateExcelDashboard(List<ReportEformModel> reportModel)
{
    try
    {
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "results"));
        var timeStamp = $"{DateTime.UtcNow:yyyyMMdd}_{DateTime.UtcNow:hhmmss}";
        var resultDocument = Path.Combine(Path.GetTempPath(), "results", $"{timeStamp}_.xlsx");

        using (var spreadsheetDocument = SpreadsheetDocument.Create(resultDocument, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = spreadsheetDocument.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();

            // Create Stylesheet for bold headers and date format
            var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
            stylesPart.Stylesheet = CreateStylesheet();
            stylesPart.Stylesheet.Save();

            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            var worksheetNames = new List<string>();
            var duplicateNumber = 0;

            foreach (var eformModel in reportModel)
            {
                foreach (var reportEformGroupModel in eformModel.GroupEform)
                {
                    if (eformModel.FromDate != null)
                    {
                        var sheetName = eformModel.GroupEform.Count > 1
                            ? $"{eformModel.GroupTagName} - {reportEformGroupModel.CheckListId}"
                            : $"{eformModel.GroupTagName}";

                        sheetName = CreateSafeSheetName(sheetName);

                        // Check for duplicate sheet names
                        if (worksheetNames.Contains(sheetName))
                        {
                            duplicateNumber++;
                            sheetName = $"({duplicateNumber}){sheetName}";
                            sheetName = sheetName.Substring(0, Math.Min(31, sheetName.Length));
                        }
                        else
                        {
                            worksheetNames.Add(sheetName);
                        }

                        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
                        worksheetPart.Worksheet = new Worksheet(new SheetData());
                        var sheetData = worksheetPart.Worksheet.GetFirstChild<SheetData>();

                        var sheet = new Sheet
                        {
                            Id = workbookPart.GetIdOfPart(worksheetPart),
                            SheetId = (uint)(sheets.Count() + 1),
                            Name = sheetName
                        };
                        sheets.Append(sheet);

                        // Create header row with bold formatting
                        var headerRow = new Row();
                        var headers = new List<Cell>
                        {
                            ConstructCell(localizationService.GetString("Id"), CellValues.String, 1),
                            ConstructCell(localizationService.GetString("Property"), CellValues.String, 1),
                            ConstructCell(localizationService.GetString("SubmittedDate"), CellValues.String, 1),
                            ConstructCell(localizationService.GetString("DoneBy"), CellValues.String, 1),
                            ConstructCell(localizationService.GetString("EmployeeNo"), CellValues.String, 1),
                            ConstructCell(localizationService.GetString("ItemName"), CellValues.String, 1)
                        };

                        foreach (var itemHeader in reportEformGroupModel.ItemHeaders)
                        {
                            headers.Add(ConstructCell(itemHeader.Value, CellValues.String, 1));
                        }

                        headerRow.Append(headers);
                        sheetData.AppendChild(headerRow);

                        // Populate data rows
                        foreach (var dataModel in reportEformGroupModel.Items)
                        {
                            var dataRow = new Row();
                            dataRow.Append(
                                ConstructCell(dataModel.MicrotingSdkCaseId.ToString(), CellValues.String),
                                ConstructCell(dataModel.PropertyName, CellValues.String),
                                ConstructCell(dataModel.MicrotingSdkCaseDoneAt?.ToString("dd.MM.yyyy HH:mm:ss"),
                                    CellValues.String),
                                ConstructCell(dataModel.DoneBy, CellValues.String),
                                ConstructCell(dataModel.EmployeeNo, CellValues.String),
                                ConstructCell(dataModel.ItemName, CellValues.String)
                            );

                            foreach (var dataModelCaseField in dataModel.CaseFields)
                            {
                                var value = dataModelCaseField.Value switch
                                {
                                    "checked" => "1",
                                    "unchecked" => "0",
                                    _ => dataModelCaseField.Value
                                };

                                switch (dataModelCaseField.Key)
                                {
                                    case "date":
                                        if (DateTime.TryParse(value, out var dateValue))
                                        {
                                            dataRow.Append(ConstructCell(dateValue.ToString("dd.MM.yyyy"),
                                                CellValues.String));
                                        }
                                        else
                                        {
                                            dataRow.Append(ConstructCell(value, CellValues.String));
                                        }

                                        break;
                                    case "number":
                                        if (double.TryParse(value, out var numberValue))
                                        {
                                            dataRow.Append(ConstructCell(
                                                numberValue.ToString(CultureInfo.InvariantCulture),
                                                CellValues.Number));
                                        }
                                        else
                                        {
                                            dataRow.Append(ConstructCell(value, CellValues.String));
                                        }

                                        break;
                                    default:
                                        dataRow.Append(ConstructCell(value, CellValues.String));
                                        break;
                                }
                            }

                            sheetData.AppendChild(dataRow);
                        }

                        // Apply autofilter and table formatting
                        ApplyTableFormatting(sheet, worksheetPart, sheetData);
                    }
                }
            }

            workbookPart.Workbook.Save();
        }

        Stream result = File.Open(resultDocument, FileMode.Open);
        return new OperationDataResult<Stream>(true, result);
    }
    catch (Exception e)
    {
        logger.LogError(e.Message);
        return new OperationDataResult<Stream>(false,
            localizationService.GetString("ErrorWhileCreatingExcelFile"));
    }
}



    private Stylesheet CreateStylesheet()
    {
        return new Stylesheet(
            new Fonts(
                new Font( // Default font
                    new FontSize() { Val = 11 },
                    new FontName() { Val = "Calibri" }),
                new Font( // Bold font
                    new Bold(),
                    new FontSize() { Val = 11 },
                    new FontName() { Val = "Calibri" })
            ),
            new Fills(
                new Fill(new PatternFill() { PatternType = PatternValues.None }), // Default fill
                new Fill(new PatternFill() { PatternType = PatternValues.Gray125 }) // Gray fill
            ),
            new Borders(new Border()), // Default border
            new CellFormats(
                new CellFormat(), // Default format
                new CellFormat { FontId = 1, ApplyFont = true } // Bold format
            ),
            new NumberingFormats( // Custom number format for date
                new NumberingFormat()
                {
                    NumberFormatId = 164, // Number format IDs between 164 and 255 are custom
                    FormatCode = "dd/MM/yyyy"
                }
            )
        );
    }

    private void ApplyTableFormatting(Sheet sheet, WorksheetPart worksheetPart, SheetData sheetData)
    {
        // Define the range for the table
        var columns = sheetData.Elements<Row>().First().Elements<Cell>().Count();
        var rows = sheetData.Elements<Row>().Count();
        string range = $"A1:{GetColumnLetter(columns)}{rows}";

        // Apply auto filter
        AutoFilter autoFilter = new AutoFilter() { Reference = range };
        worksheetPart.Worksheet.InsertAfter(autoFilter, sheetData);

        // Define table
        TableDefinitionPart tablePart = worksheetPart.AddNewPart<TableDefinitionPart>();
        Table table = new Table()
        {
            Id = (uint)new Random().Next(1, 10000),
            Name = "Table1",
            DisplayName = "Table1",
            Reference = range,
            AutoFilter = new AutoFilter() { Reference = range }
        };

        TableColumns tableColumns = new TableColumns() { Count = (uint)columns };
        for (uint i = 1; i <= columns; i++)
        {
            tableColumns.Append(new TableColumn() { Id = i, Name = $"Column{i}" });
        }

        table.Append(tableColumns);
        table.Append(new TableStyleInfo() { Name = "TableStyleMedium2", ShowFirstColumn = false, ShowLastColumn = false, ShowRowStripes = true, ShowColumnStripes = false });
        tablePart.Table = table;
        table.Save();
    }

    private string GetColumnLetter(int columnIndex)
    {
        string columnLetter = "";
        while (columnIndex > 0)
        {
            int modulo = (columnIndex - 1) % 26;
            columnLetter = Convert.ToChar(65 + modulo) + columnLetter;
            columnIndex = (columnIndex - modulo) / 26;
        }
        return columnLetter;
    }

    private static string CreateSafeSheetName(string sheetName)
    {
        sheetName = sheetName
            .Replace(":", "")
            .Replace("\\", "")
            .Replace("/", "")
            .Replace("?", "")
            .Replace("*", "")
            .Replace("[", "")
            .Replace("]", "");

        return sheetName.Length > 31 ? sheetName.Substring(0, 31) : sheetName;
    }

    private void CreateHeaderRow(SheetData sheetData, IBackendConfigurationLocalizationService localizationService)
    {
        var headerRow = new Row();
        headerRow.Append(
            ConstructCell(localizationService.GetString("Id"), CellValues.String),
            ConstructCell(localizationService.GetString("Created"), CellValues.String),
            ConstructCell(localizationService.GetString("Location"), CellValues.String),
            ConstructCell(localizationService.GetString("Area"), CellValues.String),
            ConstructCell(localizationService.GetString("CreatedBy1"), CellValues.String),
            ConstructCell(localizationService.GetString("CreatedBy2"), CellValues.String),
            ConstructCell(localizationService.GetString("LastAssignedTo"), CellValues.String),
            ConstructCell(localizationService.GetString("Description"), CellValues.String),
            ConstructCell(localizationService.GetString("LastUpdateDate"), CellValues.String),
            ConstructCell(localizationService.GetString("LastUpdatedBy"), CellValues.String),
            ConstructCell(localizationService.GetString("Priority"), CellValues.String),
            ConstructCell(localizationService.GetString("Status"), CellValues.String)
        );
        sheetData.AppendChild(headerRow);
    }

    private void PopulateDataRows(SheetData sheetData, List<WorkorderCaseModel> workOrderCaseModels)
    {
        foreach (var workOrderCaseModel in workOrderCaseModels)
        {
            var row = new Row();
            row.Append(
                ConstructCell(workOrderCaseModel.Id.ToString(), CellValues.Number),
                ConstructCell(workOrderCaseModel.CaseInitiated.ToString(), CellValues.String),
                ConstructCell(workOrderCaseModel.PropertyName, CellValues.String),
                ConstructCell(workOrderCaseModel.AreaName, CellValues.String),
                ConstructCell(workOrderCaseModel.CreatedByName, CellValues.String),
                ConstructCell(workOrderCaseModel.CreatedByText, CellValues.String),
                ConstructCell(workOrderCaseModel.LastAssignedTo, CellValues.String),
                ConstructCell(workOrderCaseModel.Description ?? "", CellValues.String),
                ConstructCell(workOrderCaseModel.LastUpdateDate?.ToString() ?? "", CellValues.String),
                ConstructCell(workOrderCaseModel.LastUpdatedBy, CellValues.String),
                ConstructCell(GetPriorityText(workOrderCaseModel.Priority), CellValues.String),
                ConstructCell(workOrderCaseModel.Status, CellValues.String)
            );
            sheetData.AppendChild(row);
        }
    }

    private string GetPriorityText(int? priority)
    {
        return priority switch
        {
            1 => localizationService.GetString("Urgent"),
            2 => localizationService.GetString("High"),
            3 => localizationService.GetString("Medium"),
            4 => localizationService.GetString("Low"),
            _ => ""
        };
    }

    private Cell ConstructCell(string value, CellValues dataType)
    {
        return new Cell
        {
            CellValue = new CellValue(value),
            DataType = new EnumValue<CellValues>(dataType)
        };
    }

    private Cell ConstructCell(string value, CellValues dataType, uint styleIndex)
    {
        return new Cell
        {
            CellValue = new CellValue(value),
            DataType = new EnumValue<CellValues>(dataType),
            StyleIndex = styleIndex // Applies the formatting (e.g., bold)
        };
    }

    public Task<Stream> GenerateTaskTracker(List<TaskTrackerModel> model)
    {
        try
        {
            var columns = new List<string>
            {
                localizationService.GetString("Property"),
                localizationService.GetString("Folder"),
                localizationService.GetString("Task"),
                localizationService.GetString("Tags"),
                localizationService.GetString("Worker"),
                localizationService.GetString("Start"),
                localizationService.GetString("Repeat"),
                localizationService.GetString("Deadline")
            };

            var resultDocument = Path.Combine(Path.GetTempPath(), "results",
                $"{localizationService.GetString("Task calendar")}.xlsx");

            using (var spreadsheetDocument =
                   SpreadsheetDocument.Create(resultDocument, SpreadsheetDocumentType.Workbook))
            {
                var workbookPart = spreadsheetDocument.AddWorkbookPart();
                workbookPart.Workbook = new Workbook();

                var sheets = workbookPart.Workbook.AppendChild(new Sheets());

                var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
                worksheetPart.Worksheet = new Worksheet(new SheetData());

                var sheet = new Sheet
                {
                    Id = workbookPart.GetIdOfPart(worksheetPart),
                    SheetId = 1,
                    Name = localizationService.GetString("Task calendar")
                };
                sheets.Append(sheet);

                var sheetData = worksheetPart.Worksheet.GetFirstChild<SheetData>();

                // Create header row
                var headerRow = new Row();
                foreach (var column in columns)
                {
                    headerRow.Append(ConstructCell(column, CellValues.String));
                }

                sheetData.AppendChild(headerRow);

                // Populate rows
                foreach (var taskTrackerModel in model)
                {
                    var row = new Row();
                    row.Append(
                        ConstructCell(taskTrackerModel.Property, CellValues.String),
                        ConstructCell(taskTrackerModel.SdkFolderName, CellValues.String),
                        ConstructCell(taskTrackerModel.TaskName, CellValues.String),
                        ConstructCell(string.Join(", ", taskTrackerModel.Tags.Select(q => q.Name)),
                            CellValues.String),
                        ConstructCell(string.Join(", ", taskTrackerModel.WorkerNames), CellValues.String),
                        ConstructCell(taskTrackerModel.StartTask.ToString("dd.MM.yyyy"), CellValues.String),
                        ConstructCell(
                            $"{(taskTrackerModel.RepeatEvery == 0 ? "" : taskTrackerModel.RepeatEvery)} {localizationService.GetString(taskTrackerModel.RepeatType.ToString())}",
                            CellValues.String),
                        ConstructCell(taskTrackerModel.DeadlineTask.ToString("dd.MM.yyyy"), CellValues.String)
                    );

                    sheetData.AppendChild(row);
                }

                workbookPart.Workbook.Save();
            }

            Stream result = File.Open(resultDocument, FileMode.Open);
            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex.Message);
            throw;
        }
    }
}