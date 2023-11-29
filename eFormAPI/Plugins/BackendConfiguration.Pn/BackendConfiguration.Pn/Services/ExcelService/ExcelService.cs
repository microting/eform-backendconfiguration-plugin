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


namespace BackendConfiguration.Pn.Services.ExcelService;

using Infrastructure.Models.Report;
using Infrastructure.Models.TaskTracker;
using BackendConfigurationLocalizationService;
using ClosedXML.Excel;
using Infrastructure.Models.TaskManagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Infrastructure.Enums;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;

public class ExcelService : IExcelService
{
	private readonly ILogger<ExcelService> _logger;
	private readonly IBackendConfigurationLocalizationService _localizationService;
	private readonly BackendConfigurationPnDbContext _backendConfigurationPnDbContext;
	private readonly IUserService _userService;
    private readonly IEFormCoreService _coreHelper;

    public ExcelService(ILogger<ExcelService> logger,
		IBackendConfigurationLocalizationService localizationService,
		BackendConfigurationPnDbContext backendConfigurationPnDbContext,
		IUserService userService, IEFormCoreService coreHelper)
	{
		_logger = logger;
		_localizationService = localizationService;
		_backendConfigurationPnDbContext = backendConfigurationPnDbContext;
		_userService = userService;
        _coreHelper = coreHelper;
    }

	public async Task<Stream> GenerateWorkOrderCaseReport(TaskManagementFiltersModel filtersModel, List<WorkorderCaseModel> workOrderCaseModels)
	{
		try
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
			SetBorders(worksheet.Range(currentRow, currentColumn, currentRow + 1, currentColumn + 6));
			worksheet.Cell(currentRow, currentColumn++).Value = _localizationService.GetString("Property");
			worksheet.Cell(currentRow, currentColumn++).Value = _localizationService.GetString("PropertyArea");
			worksheet.Cell(currentRow, currentColumn++).Value = _localizationService.GetString("CreatedBy");
			worksheet.Cell(currentRow, currentColumn++).Value = _localizationService.GetString("LastAssignedTo");
			worksheet.Cell(currentRow, currentColumn++).Value = _localizationService.GetString("Priority");
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
			worksheet.Cell(currentRow, currentColumn++).Value = filtersLastAssignedTo;
			worksheet.Cell(currentRow, currentColumn++).Value = string.IsNullOrEmpty(filtersModel.GetStringStatus())
				? ""
				: _localizationService.GetString(filtersModel.GetStringStatus());
			var dateValue = !filtersModel.DateFrom.HasValue ? "" : filtersModel.DateFrom.Value.ToString("dd.MM.yyyy");
			dateValue += !filtersModel.DateTo.HasValue ? "" : "-";
			dateValue += !filtersModel.DateTo.HasValue
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
				startColumnForDataTable + 11));
			worksheet.Range(currentRow, startColumnForDataTable, currentRow, startColumnForDataTable + 11).Cells().Style
				.Font.Bold = true;
			worksheet.Range(currentRow, startColumnForDataTable, currentRow, startColumnForDataTable + 11).Cells().Style
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
			worksheet.Cell(currentRow, currentColumn++).Value = _localizationService.GetString("Priority");
			worksheet.Cell(currentRow++, currentColumn).Value = _localizationService.GetString("Status");


			//* table data
			foreach (var workOrderCaseModel in workOrderCaseModels)
			{
				currentColumn = startColumnForDataTable;

				worksheet.Cell(currentRow, currentColumn++).Value = workOrderCaseModel.Id;
				worksheet.Cell(currentRow, currentColumn++).SetValue(workOrderCaseModel.CaseInitiated);
				worksheet.Cell(currentRow, currentColumn++).Value = workOrderCaseModel.PropertyName;
				worksheet.Cell(currentRow, currentColumn++).Value = workOrderCaseModel.AreaName;
				worksheet.Cell(currentRow, currentColumn++).Value = workOrderCaseModel.CreatedByName;
				worksheet.Cell(currentRow, currentColumn++).Value = workOrderCaseModel.CreatedByText;
				worksheet.Cell(currentRow, currentColumn++).Value = workOrderCaseModel.LastAssignedTo;
				worksheet.Cell(currentRow, currentColumn++).Value = workOrderCaseModel.Description != null
					? workOrderCaseModel.Description.Replace("<br>", "").Replace("<br />", "\n")
					: "";
				worksheet.Cell(currentRow, currentColumn++).SetValue(workOrderCaseModel.LastUpdateDate.HasValue
					? workOrderCaseModel.LastUpdateDate.Value
					: "");
				worksheet.Cell(currentRow, currentColumn++).Value = workOrderCaseModel.LastUpdatedBy;
				var priorityText = workOrderCaseModel.Priority switch
				{
					1 => _localizationService.GetString("Urgent"),
					2 => _localizationService.GetString("High"),
					3 => _localizationService.GetString("Medium"),
					4 => _localizationService.GetString("Low"),
					_ => ""
				};
				worksheet.Cell(currentRow, currentColumn++).Value = _localizationService.GetString(priorityText);
				worksheet.Cell(currentRow++, currentColumn).Value = _localizationService.GetString(workOrderCaseModel.Status);
			}
			worksheet.RangeUsed().SetAutoFilter();

			// worksheet.Columns(startColumnForDataTable, currentColumn).AdjustToContents(); // This does not work inside Docker container

			wb.SaveAs(resultDocument);

			Stream result = File.Open(resultDocument, FileMode.Open);
			return result;
		}
		catch (Exception ex)
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
	public async Task<OperationDataResult<Stream>> GenerateExcelDashboard(List<OldReportEformModel> reportModel)
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
						worksheet.Cell(x + 1, y + 1).Value = _localizationService.GetString("SubmittedDate");
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
						worksheet.Cell(x + 1, y + 1).Value = dataModel.MicrotingSdkCaseDoneAt;
						worksheet.Cell(x + 1, y + 1).Style.DateFormat.Format = "dd.MM.yyyy HH:mm:ss";
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
										try
										{
											var date = DateTime.Parse(value);
											worksheet.Cell(x + 1, y + 1).SetValue(date);
											worksheet.Cell(x + 1, y + 1).Style.DateFormat.Format = "dd.MM.yyyy";
										}
										catch (Exception e)
										{
											Console.WriteLine(e);
											worksheet.Cell(x + 1, y + 1).SetValue(value);
										}
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
											worksheet.Cell(x + 1, y + 1).SetValue(value);
										}
										break;
									default:
									{
										if (Double.TryParse(value, out var number))
										{
											worksheet.Cell(x + 1, y + 1).SetValue(number);
										}
										else
										{
											worksheet.Cell(x + 1, y + 1).SetValue("'" + value);
										}
										break;
									}
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
				foreach (var reportEformGroupModel in eformModel.GroupEform)
				{
					if (eformModel.FromDate != null)
					{
						var x = 0;
						var y = 0;
						var sheetName = eformModel.GroupEform.Count > 1
							? $"{eformModel.GroupTagName} - {reportEformGroupModel.CheckListId}"
							: $"{eformModel.GroupTagName}";

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


						if (reportEformGroupModel.Items.Any())
						{
							worksheet.Cell(x + 1, y + 1).Value = _localizationService.GetString("Id");
							worksheet.Cell(x + 1, y + 1).Style.Font.Bold = true;
							y++;
							worksheet.Cell(x + 1, y + 1).Value = _localizationService.GetString("Property");
							worksheet.Cell(x + 1, y + 1).Style.Font.Bold = true;
							y++;
							worksheet.Cell(x + 1, y + 1).Value = _localizationService.GetString("SubmittedDate");
							worksheet.Cell(x + 1, y + 1).Style.Font.Bold = true;
							y++;
							worksheet.Cell(x + 1, y + 1).Value = _localizationService.GetString("DoneBy");
							worksheet.Cell(x + 1, y + 1).Style.Font.Bold = true;
							y++;
							worksheet.Cell(x + 1, y + 1).Value = _localizationService.GetString("ItemName");
							worksheet.Cell(x + 1, y + 1).Style.Font.Bold = true;
							foreach (var itemHeader in reportEformGroupModel.ItemHeaders)
							{
								y++;
								worksheet.Cell(x + 1, y + 1).Value = itemHeader.Value;
								worksheet.Cell(x + 1, y + 1).Style.Font.Bold = true;
							}
						}

						x = 1;
						foreach (var dataModel in reportEformGroupModel.Items)
						{
							y = 0;
							worksheet.Cell(x + 1, y + 1).Value = dataModel.MicrotingSdkCaseId;
							y++;
							worksheet.Cell(x + 1, y + 1).Value = dataModel.PropertyName;
							y++;
							worksheet.Cell(x + 1, y + 1).Value = dataModel.MicrotingSdkCaseDoneAt;
							worksheet.Cell(x + 1, y + 1).Style.DateFormat.Format = "dd.MM.yyyy HH:mm:ss";
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
									var value = dataModelCaseField.Value == "unchecked" ? "0" :
										dataModelCaseField.Value == "checked" ? "1" : dataModelCaseField.Value;

									switch (dataModelCaseField.Key)
									{
										case "date":
											try
											{
												var date = DateTime.Parse(value);
												worksheet.Cell(x + 1, y + 1).SetValue(date);
												worksheet.Cell(x + 1, y + 1).Style.DateFormat.Format = "dd.MM.yyyy";
											}
											catch (Exception e)
											{
												Console.WriteLine(e);
												worksheet.Cell(x + 1, y + 1).SetValue(value);
											}

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
												worksheet.Cell(x + 1, y + 1).SetValue(value);
											}
											break;
										default:
										{
											if (Double.TryParse(value, out var number))
											{
												worksheet.Cell(x + 1, y + 1).SetValue(number);
											}
											else
											{
												worksheet.Cell(x + 1, y + 1).SetValue("'" + value);
											}
											break;
										}
									}
								}

								y++;
							}

							x++;
						}
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

	public async Task<Stream> GenerateTaskTracker(List<TaskTrackerModel> model)
	{
		try
		{
			var enabledColumns = await _backendConfigurationPnDbContext.TaskTrackerColumns
				.Where(x => x.UserId == _userService.UserId)
				.Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
				.Select(x => new
				{
					x.isColumnEnabled,
					x.ColumnName
				})
				.ToListAsync();

			bool GetEnabledColumn(string columnName)
			{
				var columnEnabled = enabledColumns
					.FirstOrDefault(q => string.Equals(q.ColumnName, columnName, StringComparison.CurrentCultureIgnoreCase));
				return columnEnabled == null || columnEnabled.isColumnEnabled;
			}

			var columns = new List<string>
			{
				GetEnabledColumn("property") ? _localizationService.GetString("Property") : "",
				GetEnabledColumn("task") ? _localizationService.GetString("Task") : "",
				GetEnabledColumn("tags") ? _localizationService.GetString("Tags") : "",
				GetEnabledColumn("workers") ? _localizationService.GetString("Worker") : "",
				GetEnabledColumn("start") ? _localizationService.GetString("Start") : "",
				GetEnabledColumn("repeat") ? _localizationService.GetString("Repeat") : "",
				GetEnabledColumn("deadline") ? _localizationService.GetString("Deadline") : ""
			}.Where(q => !string.IsNullOrEmpty(q)).ToList();
			var newDate = DateTime.Now;
			var currentDate = new DateTime(newDate.Year, newDate.Month, newDate.Day, 0, 0, 0);
			var endDate = currentDate.AddDays(28);
			var timeStamp = $"{currentDate:yyyyMMdd}";

			var resultDocument = Path.Combine(Path.GetTempPath(), "results", $"{_localizationService.GetString("Task calendar")}.xlsx");
			IXLWorkbook wb = new XLWorkbook();

			var invalidChars = new[] { ":", "\\", "/", "?", "*", "[", "]" };
			var sheetName = _localizationService.GetString("Task calendar");
			var ws = wb.Worksheets.Add(sheetName);
			// cell(x, y) => cell(1, 2) => cell 1B
			const int startY = 2;
			const int startX = 2;
			var x = startX;
			var y = startY;
			var startCell = ws.Cell(x, y);
			var skipCols = columns.Count + startY;

			/* table headers */
			foreach (var column in columns)
			{
				var range = ws.Range(ws.Cell(x, y), ws.Cell(x + 2, y));
				range.Merge();
				range.Value = column;
				range.Style.Font.Bold = true;
				range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
				range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
				y++;
			}
			y = skipCols;
			foreach (var week in model[^1].Weeks) // render weeks
			{
				if (!GetEnabledColumn("calendar"))
				{
					break;
				}
				var rangeStart = ws.Cell(x, y);
				var rangeEnd = ws.Cell(x, y + week.WeekRange - 1);
				var range = ws.Range(rangeStart, rangeEnd).Merge();
				range.Value = $"{_localizationService.GetString("Week")} {week.WeekNumber}";
				range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
				range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
				ws.Columns(y, y + week.WeekRange - 1).Width = 2.29D;
				x++;
				for (var i = 0; i < week.DateList.Count; i++) // render days in week
				{
					var date = week.DateList[i];
					var cell = ws.Cell(x, y + i);
					cell.Value = date.Date.Day;
					cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
					cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
					if (date.Date == currentDate) // make current day bold
					{
						cell.Style.Font.Bold = true;
					}

					// render days of week
					x++;
					cell = ws.Cell(x, y + i);
					cell.Value = _localizationService.GetString($"Short{date.Date.DayOfWeek}");
					cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
					cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
					if (date.Date == currentDate) // make current day bold
					{
						cell.Style.Font.Bold = true;
					}

					x--;
				}
				x--;
				y += week.WeekRange;
			}
			var endCell = ws.Cell(startX + 2 + model.Count, y - 1); // after render all head table current value in y - last column

			// render data
			x = startX + 3; // startX + weeks columns + day columns + day of week columns
			y = startY;
			foreach (var taskTrackerModel in model)
			{
				var startCellData = ws.Cell(x, y);
				if (GetEnabledColumn("property"))
				{
					ws.Cell(x, y).Value = taskTrackerModel.Property;
					y++;
				}
				if (GetEnabledColumn("task"))
				{
					ws.Cell(x, y).Value = taskTrackerModel.TaskName;
					y++;
				}
				if (GetEnabledColumn("tags"))
				{
					ws.Cell(x, y).Value = string.Join(", ", taskTrackerModel.Tags.Select(q => q.Name));
					y++;
				}
				if (GetEnabledColumn("workers"))
				{
					ws.Cell(x, y).Value = string.Join(", ", taskTrackerModel.Workers);
					y++;
				}
				if (GetEnabledColumn("start"))
				{
					ws.Cell(x, y).Value = taskTrackerModel.StartTask.ToString("dd.MM.yyyy");
					y++;
				}
				if (GetEnabledColumn("repeat"))
				{
					ws.Cell(x, y).Value = $"{(taskTrackerModel.RepeatEvery == 0 ? "" : taskTrackerModel.RepeatEvery)} {_localizationService.GetString(taskTrackerModel.RepeatType.ToString())}";
					y++;
				}
				if (GetEnabledColumn("deadline"))
				{
					ws.Cell(x, y).Value = taskTrackerModel.DeadlineTask.ToString("dd.MM.yyyy");
				}
				if (taskTrackerModel.TaskIsExpired) // make red expired task (calendar part not need)
				{
					ws.Range(startCellData, ws.Cell(x, y)).Style.Fill.BackgroundColor = XLColor.Red;
				}

				y = skipCols;
				foreach (var week in taskTrackerModel.Weeks)
				{
					if (taskTrackerModel.TaskIsExpired || !GetEnabledColumn("calendar"))
					{
						break;
					}

					foreach (var date in week.DateList)
					{
						var color = date.IsTask ? XLColor.Yellow : XLColor.FromArgb(255, 242, 204);
						ws.Cell(x, y).Style.Fill.BackgroundColor = color;
						y++;
					}
				}
				y = startY;
				x++;
			}


			SetBorders(ws.Range(startCell, endCell));

			wb.SaveAs(resultDocument);

			Stream result = File.Open(resultDocument, FileMode.Open);
			return result;
		}
		catch (Exception ex)
		{
			Console.WriteLine(ex.Message);
			throw;
		}
	}
}