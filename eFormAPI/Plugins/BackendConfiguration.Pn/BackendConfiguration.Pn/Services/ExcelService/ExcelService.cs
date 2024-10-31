using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Helpers;
using BackendConfiguration.Pn.Infrastructure.Models.Report;
using BackendConfiguration.Pn.Infrastructure.Models.TaskManagement;
using BackendConfiguration.Pn.Infrastructure.Models.TaskTracker;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using DocumentFormat.OpenXml.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Sentry;

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

                // Create Stylesheet for bold headers and date format
                var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
                stylesPart.Stylesheet = CreateStylesheet();
                stylesPart.Stylesheet.Save();

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
                            CreateCell(localizationService.GetString("Id")),
                            CreateCell(localizationService.GetString("Property")),
                            CreateCell(localizationService.GetString("SubmittedDate")),
                            CreateCell(localizationService.GetString("DoneBy")),
                            CreateCell(localizationService.GetString("ItemName"))
                        );

                        foreach (var itemHeader in eformModel.ItemHeaders)
                        {
                            headerRow.Append(CreateCell(itemHeader.Value));
                        }

                        sheetData.AppendChild(headerRow);

                        // Populate data rows
                        foreach (var dataModel in eformModel.Items)
                        {
                            var dataRow = new Row();
                            dataRow.Append(
                                CreateNumericCell(dataModel.MicrotingSdkCaseId),
                                CreateCell(dataModel.PropertyName),
                                CreateDateCell(dataModel.MicrotingSdkCaseDoneAt!.Value),
                                CreateCell(dataModel.DoneBy),
                                CreateCell(dataModel.ItemName)
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
                                            dataRow.Append(CreateDateCell(dateValue));
                                        }
                                        else
                                        {
                                            dataRow.Append(CreateCell(value));
                                        }

                                        break;
                                    case "number":
                                        if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var numberValue))
                                        {
                                            dataRow.Append(CreateNumericCell(
                                                numberValue));
                                        }
                                        else
                                        {
                                            dataRow.Append(CreateCell(value));
                                        }

                                        break;
                                    default:
                                        dataRow.Append(CreateCell(value));
                                        break;
                                }
                            }

                            sheetData.AppendChild(dataRow);
                        }

                        // Apply autofilter and table formatting
                        // ApplyTableFormatting(sheet, worksheetPart, sheetData);
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
        var filePath = Path.Combine(Path.GetTempPath(), "results", $"{timeStamp}_.xlsx");

        using (var document = SpreadsheetDocument.Create(filePath, SpreadsheetDocumentType.Workbook))
        {
            var worksheetNames = new List<KeyValuePair<string, string>>();

            var i = 0;
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
                        if (worksheetNames.Contains(new KeyValuePair<string, string>($"{sheetName}", $"rId{i + 1}")))
                        {
                            var duplicateNumber = 1;
                            while (worksheetNames.Contains(new KeyValuePair<string, string>($"{sheetName} ({duplicateNumber})", $"rId{i + 1}")))
                            {
                                duplicateNumber++;
                            }

                            sheetName = $"{sheetName} ({duplicateNumber})";
                            sheetName = sheetName.Substring(0, Math.Min(31, sheetName.Length));
                        }

                        worksheetNames.Add(
                            new KeyValuePair<string, string>($"{sheetName}", $"rId{i + 1}"));
                    }
                    i++;
                }
            }

            WorkbookPart workbookPart1 = document.AddWorkbookPart();
            OpenXMLHelper.GenerateWorkbookPart1Content(workbookPart1, worksheetNames);

            WorkbookStylesPart workbookStylesPart1 =
                workbookPart1.AddNewPart<WorkbookStylesPart>($"rId{worksheetNames.Count + 2}");
            OpenXMLHelper.GenerateWorkbookStylesPart1Content(workbookStylesPart1);

            ThemePart themePart1 = workbookPart1.AddNewPart<ThemePart>($"rId{worksheetNames.Count + 1}");
            OpenXMLHelper.GenerateThemePart1Content(themePart1);

            var baseHeadersForRow = new List<string>
            {
                "Id",
                "Property",
                "SubmittedDate",
                "DoneBy",
                "EmployeeNo",
                "ItemName"
            };

            var j = 0;
            foreach (var eformModel in reportModel)
            {
                foreach (var reportEformGroupModel in eformModel.GroupEform)
                {
                    if (eformModel.FromDate != null)
                    {
                        List<string> headerStrings = new List<string>();
                        foreach (var header in baseHeadersForRow)
                        {
                            headerStrings.Add(localizationService.GetString(header));
                        }

                        foreach (var itemHeader in reportEformGroupModel.ItemHeaders)
                        {
                            headerStrings.Add(itemHeader.Value);
                        }

                        WorksheetPart worksheetPart1 = workbookPart1.AddNewPart<WorksheetPart>($"rId{j + 1}");

                        Worksheet worksheet1 = new Worksheet()
                            { MCAttributes = new MarkupCompatibilityAttributes() { Ignorable = "x14ac xr xr2 xr3" } };
                        worksheet1.AddNamespaceDeclaration("r",
                            "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
                        worksheet1.AddNamespaceDeclaration("mc",
                            "http://schemas.openxmlformats.org/markup-compatibility/2006");
                        worksheet1.AddNamespaceDeclaration("x14ac",
                            "http://schemas.microsoft.com/office/spreadsheetml/2009/9/ac");
                        worksheet1.AddNamespaceDeclaration("xr",
                            "http://schemas.microsoft.com/office/spreadsheetml/2014/revision");
                        worksheet1.AddNamespaceDeclaration("xr2",
                            "http://schemas.microsoft.com/office/spreadsheetml/2015/revision2");
                        worksheet1.AddNamespaceDeclaration("xr3",
                            "http://schemas.microsoft.com/office/spreadsheetml/2016/revision3");
                        worksheet1.SetAttribute(new OpenXmlAttribute("xr", "uid",
                            "http://schemas.microsoft.com/office/spreadsheetml/2014/revision",
                            "{00000000-0001-0000-0000-000000000000}"));

                        SheetFormatProperties sheetFormatProperties1 = new SheetFormatProperties()
                            { DefaultRowHeight = 15D, DyDescent = 0.25D };

                        SheetData sheetData1 = new SheetData();

                        Row row1 = new Row()
                        {
                            RowIndex = (UInt32Value)1U, Spans = new ListValue<StringValue>() { InnerText = "1:19" },
                            DyDescent = 0.25D
                        };

                        foreach (var header in headerStrings)
                        {
                            var cell = new Cell()
                            {
                                CellValue = new CellValue(header),
                                DataType = CellValues.String,
                                StyleIndex = (UInt32Value)1U
                            };
                            row1.Append(cell);
                        }

                        sheetData1.Append(row1);

                        int rowIndex = 1;


                        // Populate data rows
                        foreach (var dataModel in reportEformGroupModel.Items)
                        {
                            var dataRow = new Row();
                            dataRow.Append(
                                CreateNumericCell(dataModel.MicrotingSdkCaseId),
                                CreateCell(dataModel.PropertyName),
                                CreateDateCell(dataModel.MicrotingSdkCaseDoneAt!.Value),
                                CreateCell(dataModel.DoneBy),
                                CreateCell(dataModel.EmployeeNo),
                                CreateCell(dataModel.ItemName)
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
                                            dataRow.Append(CreateDateCell(dateValue));
                                        }
                                        else
                                        {
                                            dataRow.Append(CreateCell(value));
                                        }

                                        break;
                                    case "number":
                                        if (double.TryParse(value.Replace(",", "."), out var numberValue))
                                        {
                                            dataRow.Append(CreateNumericCell(numberValue));
                                        }
                                        else
                                        {
                                            dataRow.Append(CreateCell(value));
                                        }

                                        break;
                                    default:
                                        dataRow.Append(CreateCell(value));
                                        break;
                                }
                            }

                            sheetData1.Append(dataRow);
                            rowIndex++;
                        }



                        var columnLetter = GetColumnLetter(headerStrings.Count);
                        AutoFilter autoFilter1 = new AutoFilter() { Reference = $"A1:{columnLetter}{rowIndex}" };
                        autoFilter1.SetAttribute(new OpenXmlAttribute("xr", "uid",
                            "http://schemas.microsoft.com/office/spreadsheetml/2014/revision",
                            "{00000000-0001-0000-0000-000000000000}"));
                        PageMargins pageMargins1 = new PageMargins()
                            { Left = 0.7D, Right = 0.7D, Top = 0.75D, Bottom = 0.75D, Header = 0.3D, Footer = 0.3D };

                        worksheet1.Append(sheetFormatProperties1);
                        worksheet1.Append(sheetData1);
                        worksheet1.Append(autoFilter1);
                        worksheet1.Append(pageMargins1);

                        worksheetPart1.Worksheet = worksheet1;
                        j++;
                    }
                }
            }

            // var workbookPart = spreadsheetDocument.AddWorkbookPart();
            // workbookPart.Workbook = new Workbook();
            //
            // // Create Stylesheet for bold headers and date format
            // var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
            // stylesPart.Stylesheet = CreateStylesheet();
            // stylesPart.Stylesheet.Save();
            //
            // var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            // var worksheetNames = new List<string>();
            // var duplicateNumber = 0;
            //
            // foreach (var eformModel in reportModel)
            // {
            //     foreach (var reportEformGroupModel in eformModel.GroupEform)
            //     {
            //         if (eformModel.FromDate != null)
            //         {
            //             var sheetName = eformModel.GroupEform.Count > 1
            //                 ? $"{eformModel.GroupTagName} - {reportEformGroupModel.CheckListId}"
            //                 : $"{eformModel.GroupTagName}";
            //
            //             sheetName = CreateSafeSheetName(sheetName);
            //
            //             // Check for duplicate sheet names
            //             if (worksheetNames.Contains(sheetName))
            //             {
            //                 duplicateNumber++;
            //                 sheetName = $"({duplicateNumber}){sheetName}";
            //                 sheetName = sheetName.Substring(0, Math.Min(31, sheetName.Length));
            //             }
            //             else
            //             {
            //                 worksheetNames.Add(sheetName);
            //             }
            //
            //             var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            //             worksheetPart.Worksheet = new Worksheet(new SheetData());
            //             var sheetData = worksheetPart.Worksheet.GetFirstChild<SheetData>();
            //
            //             var sheet = new Sheet
            //             {
            //                 Id = workbookPart.GetIdOfPart(worksheetPart),
            //                 SheetId = (uint)(sheets.Count() + 1),
            //                 Name = sheetName
            //             };
            //             sheets.Append(sheet);
            //
            //             // Create header row with bold formatting
            //             var headerRow = new Row();
            //             var headers = new List<Cell>
            //             {
            //                 ConstructCell(localizationService.GetString("Id"), CellValues.String, 1),
            //                 ConstructCell(localizationService.GetString("Property"), CellValues.String, 1),
            //                 ConstructCell(localizationService.GetString("SubmittedDate"), CellValues.String, 1),
            //                 ConstructCell(localizationService.GetString("DoneBy"), CellValues.String, 1),
            //                 ConstructCell(localizationService.GetString("EmployeeNo"), CellValues.String, 1),
            //                 ConstructCell(localizationService.GetString("ItemName"), CellValues.String, 1)
            //             };
            //
            //             foreach (var itemHeader in reportEformGroupModel.ItemHeaders)
            //             {
            //                 headers.Add(ConstructCell(itemHeader.Value, CellValues.String, 1));
            //             }
            //
            //             headerRow.Append(headers);
            //             sheetData.AppendChild(headerRow);
            //
            //             // Populate data rows
            //             foreach (var dataModel in reportEformGroupModel.Items)
            //             {
            //                 var dataRow = new Row();
            //                 dataRow.Append(
            //                     ConstructCell(dataModel.MicrotingSdkCaseId.ToString(), CellValues.String),
            //                     ConstructCell(dataModel.PropertyName, CellValues.String),
            //                     ConstructCell(dataModel.MicrotingSdkCaseDoneAt?.ToString("dd.MM.yyyy HH:mm:ss"),
            //                         CellValues.String),
            //                     ConstructCell(dataModel.DoneBy, CellValues.String),
            //                     ConstructCell(dataModel.EmployeeNo, CellValues.String),
            //                     ConstructCell(dataModel.ItemName, CellValues.String)
            //                 );
            //
            //                 foreach (var dataModelCaseField in dataModel.CaseFields)
            //                 {
            //                     var value = dataModelCaseField.Value switch
            //                     {
            //                         "checked" => "1",
            //                         "unchecked" => "0",
            //                         _ => dataModelCaseField.Value
            //                     };
            //
            //                     switch (dataModelCaseField.Key)
            //                     {
            //                         case "date":
            //                             if (DateTime.TryParse(value, out var dateValue))
            //                             {
            //                                 dataRow.Append(ConstructCell(dateValue.ToString("dd.MM.yyyy"),
            //                                     CellValues.String));
            //                             }
            //                             else
            //                             {
            //                                 dataRow.Append(ConstructCell(value, CellValues.String));
            //                             }
            //
            //                             break;
            //                         case "number":
            //                             if (double.TryParse(value, out var numberValue))
            //                             {
            //                                 dataRow.Append(ConstructCell(
            //                                     numberValue.ToString(CultureInfo.InvariantCulture),
            //                                     CellValues.Number));
            //                             }
            //                             else
            //                             {
            //                                 dataRow.Append(ConstructCell(value, CellValues.String));
            //                             }
            //
            //                             break;
            //                         default:
            //                             dataRow.Append(ConstructCell(value, CellValues.String));
            //                             break;
            //                     }
            //                 }
            //
            //                 sheetData.AppendChild(dataRow);
            //             }
            //
            //             // Apply autofilter and table formatting
            //             // ApplyTableFormatting(sheet, worksheetPart, sheetData);
            //         }
            //     }
            // }
            //
            // workbookPart.Workbook.Save();
        }
        ValidateExcel(filePath);

        Stream result = File.Open(filePath, FileMode.Open);
        return new OperationDataResult<Stream>(true, result);
    }
    catch (Exception e)
    {
        logger.LogError(e.Message);
        return new OperationDataResult<Stream>(false,
            localizationService.GetString("ErrorWhileCreatingExcelFile"));
    }
}

    private void ValidateExcel(string fileName)
    {
        try
        {
            var validator = new OpenXmlValidator();
            int count = 0;
            StringBuilder sb = new StringBuilder();
            var doc = SpreadsheetDocument.Open(fileName, true);
            foreach (ValidationErrorInfo error in validator.Validate(doc))
            {

                count++;
                sb.Append(("Error Count : " + count) + "\r\n");
                sb.Append(("Description : " + error.Description) + "\r\n");
                sb.Append(("Path: " + error.Path.XPath) + "\r\n");
                sb.Append(("Part: " + error.Part.Uri) + "\r\n");
                sb.Append("\r\n-------------------------------------------------\r\n");
            }

            doc.Dispose();
            if (count <= 0) return;
            sb.Append(("Total Errors in file: " + count));
            throw new Exception(sb.ToString());
        }
        catch (Exception ex)
        {
            SentrySdk.CaptureException(ex);
            logger.LogError(ex.Message);
        }
    }

    private Stylesheet CreateStylesheet()
    {
        return new Stylesheet(
            new NumberingFormats( // Custom number format for date
                new NumberingFormat
                {
                    NumberFormatId = 164, // Custom NumberFormatId for date format
                    FormatCode = "dd/MM/yyyy"
                }
            ),
            new Fonts(
                new Font( // Default font
                    new FontSize { Val = 11 },
                    new Color { Rgb = new HexBinaryValue { Value = "FF000000" } }, // Black color
                    new FontName { Val = "Calibri" }
                ),
                new Font( // Bold font
                    new Bold(),
                    new FontSize { Val = 11 },
                    new Color { Rgb = new HexBinaryValue { Value = "FF000000" } }, // Black color
                    new FontName { Val = "Calibri" }
                )
            ),
            new Fills(
                new Fill(new PatternFill { PatternType = PatternValues.None }), // Default fill
                new Fill(new PatternFill { PatternType = PatternValues.Gray125 }) // Gray fill
            ),
            new Borders(
                new Border( // Default border
                    new LeftBorder(),
                    new RightBorder(),
                    new TopBorder(),
                    new BottomBorder(),
                    new DiagonalBorder()
                )
            ),
            new CellStyleFormats(
                new CellFormat() // Default cell style format
            ),
            new CellFormats(
                new CellFormat(), // Default cell format
                new CellFormat { FontId = 1, ApplyFont = true }, // Bold font cell format
                new CellFormat { NumberFormatId = 164, ApplyNumberFormat = true }, // Date format
                new CellFormat { NumberFormatId = 22, ApplyNumberFormat = true } // Date-time format (dd.MM.yyyy HH:mm:ss)
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
            CreateCell(localizationService.GetString("Id")),
            CreateCell(localizationService.GetString("Created")),
            CreateCell(localizationService.GetString("Location")),
            CreateCell(localizationService.GetString("Area")),
            CreateCell(localizationService.GetString("CreatedBy1")),
            CreateCell(localizationService.GetString("CreatedBy2")),
            CreateCell(localizationService.GetString("LastAssignedTo")),
            CreateCell(localizationService.GetString("Description")),
            CreateCell(localizationService.GetString("LastUpdateDate")),
            CreateCell(localizationService.GetString("LastUpdatedBy")),
            CreateCell(localizationService.GetString("Priority")),
            CreateCell(localizationService.GetString("Status"))
        );
        sheetData.AppendChild(headerRow);
    }

    private void PopulateDataRows(SheetData sheetData, List<WorkorderCaseModel> workOrderCaseModels)
    {
        foreach (var workOrderCaseModel in workOrderCaseModels)
        {
            var row = new Row();
            row.Append(
                CreateNumericCell(workOrderCaseModel.Id),
                CreateCell(workOrderCaseModel.CaseInitiated.ToString()),
                CreateCell(workOrderCaseModel.PropertyName),
                CreateCell(workOrderCaseModel.AreaName),
                CreateCell(workOrderCaseModel.CreatedByName),
                CreateCell(workOrderCaseModel.CreatedByText),
                CreateCell(workOrderCaseModel.LastAssignedTo),
                CreateCell(workOrderCaseModel.Description ?? ""),
                CreateDateCell(workOrderCaseModel.LastUpdateDate!.Value),
                CreateCell(workOrderCaseModel.LastUpdatedBy),
                CreateCell(GetPriorityText(workOrderCaseModel.Priority)),
                CreateCell(workOrderCaseModel.Status)
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

    private Cell CreateCell(string value)
    {
        return new Cell()
        {
            CellValue = new CellValue(value),
            DataType = CellValues.String // Explicitly setting the data type to string
        };
    }

    private Cell CreateNumericCell(double value)
    {
        return new Cell()
        {
            CellValue = new CellValue(value.ToString(CultureInfo.InvariantCulture)),
            DataType = CellValues.Number
        };
    }

    private Cell CreateDateCell(DateTime dateValue)
    {
        return new Cell()
        {
            CellValue = new CellValue(dateValue.ToOADate()
                .ToString(CultureInfo.InvariantCulture)), // Excel stores dates as OLE Automation date values
            DataType = CellValues.Number, // Excel treats dates as numbers
            StyleIndex = 2 // Assuming StyleIndex 2 corresponds to the date format in the stylesheet
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
                    headerRow.Append(CreateCell(column));
                }

                sheetData.AppendChild(headerRow);

                // Populate rows
                foreach (var taskTrackerModel in model)
                {
                    var row = new Row();
                    row.Append(
                        CreateCell(taskTrackerModel.Property),
                        CreateCell(taskTrackerModel.SdkFolderName),
                        CreateCell(taskTrackerModel.TaskName),
                        CreateCell(string.Join(", ", taskTrackerModel.Tags.Select(q => q.Name))),
                        CreateCell(string.Join(", ", taskTrackerModel.WorkerNames)),
                        CreateDateCell(taskTrackerModel.StartTask),
                        CreateCell(
                            $"{(taskTrackerModel.RepeatEvery == 0 ? "" : taskTrackerModel.RepeatEvery)} {localizationService.GetString(taskTrackerModel.RepeatType.ToString())}"),
                        CreateDateCell(taskTrackerModel.DeadlineTask)
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