/*
The MIT License (MIT)

Copyright (c) 2007 - 2023 Microting A/S

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

namespace BackendConfiguration.Pn.Infrastructure.Helpers;

using Models.TaskTracker;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.ItemsPlanningBase.Infrastructure.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using eFormCore;
using Enums;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Infrastructure.Helpers;
using System.Globalization;
using Microting.eFormApi.BasePn.Infrastructure.Models.Common;

public static class BackendConfigurationTaskTrackerHelper
{
	public static async Task<OperationDataResult<List<TaskTrackerModel>>> Index(
		TaskTrackerFiltrationModel filtersModel,
		BackendConfigurationPnDbContext backendConfigurationPnDbContext,
		Core core,
		int userLanguageId,
		ItemsPlanningPnDbContext itemsPlanningPnDbContext)
	{
		try
		{
			var result = new List<TaskTrackerModel>();
			var dateTimeNow = DateTime.UtcNow;

			var sdkDbContext = core.DbContextHelper.GetDbContext();
			var query = backendConfigurationPnDbContext.Compliances
				.Where(x => x.WorkflowState != Constants.WorkflowStates.Removed);

			if (filtersModel.PropertyIds.Any() /* && !filtersModel.PropertyIds.Contains(-1)*/)
			{
				query = query.Where(x => filtersModel.PropertyIds.Contains(x.PropertyId));
			}

			var complianceList = await query
				.AsNoTracking()
				.OrderBy(x => x.Deadline)
				.Select(x => new
				{
					x.PropertyId, x.PlanningId, x.Deadline, x.MicrotingSdkCaseId, x.MicrotingSdkeFormId, x.Id, x.AreaId
				})
				.ToListAsync();


			var newDate = DateTime.Now;
			var currentDate = new DateTime(newDate.Year, newDate.Month, newDate.Day, 0, 0, 0);
			var endDate = currentDate.AddDays(28);
			var weeks = new List<TaskTrackerWeeksListModel>();
			var localCurrentDate = currentDate;
			while (localCurrentDate < endDate) // get week numbers
			{
				var weekNumber = CultureInfo.CurrentCulture.Calendar.GetWeekOfYear(localCurrentDate,
					CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
				var weekRange = 8 - (int)localCurrentDate.DayOfWeek;
				if (weekRange == 8) // if current day of week - sunday
				{
					weekRange = 1;
				}

				var dateList = new List<TaskTrackerDateListModel>();

				for (var i = 0; i < weekRange; i++)
				{
					var date = localCurrentDate.AddDays(i);
					if (date < endDate)
					{
						dateList.Add(new TaskTrackerDateListModel
							{ Date = date, IsTask = false }); // IsTask = false is default value
					}
					else
					{
						break;
					}
				}

				weeks.Add(new TaskTrackerWeeksListModel
					{ WeekNumber = weekNumber, DateList = dateList, WeekRange = dateList.Count });
				localCurrentDate = localCurrentDate.AddDays(weekRange);
			}

			foreach (var compliance in complianceList)
			{
				var propertyName = await backendConfigurationPnDbContext.Properties
					.Where(x => x.Id == compliance.PropertyId)
					.Select(x => x.Name)
					.FirstOrDefaultAsync();

				var planning = await itemsPlanningPnDbContext.Plannings
					.Where(x => x.Id == compliance.PlanningId)
					.Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
					//.Include(x => x.PlanningsTags)
					//.ThenInclude(x => x.PlanningTag)
					.FirstOrDefaultAsync();

				if (planning == null)
				{
					continue;
				}

				var planningSiteIds = await itemsPlanningPnDbContext.PlanningSites
					.Where(x => x.PlanningId == compliance.PlanningId)
					.Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
					.Select(x => x.SiteId)
					.Distinct()
					.ToListAsync();

				var sitesWithNames = await sdkDbContext.Sites
					.Where(x => planningSiteIds.Contains(x.Id))
					.Select(site => new KeyValuePair<int, string>(site.Id, site.Name))
					.ToListAsync();

				if (filtersModel.WorkerIds.Any() /* && !filtersModel.WorkerIds.Contains(-1)*/) // filtration by workers
				{
					if (!sitesWithNames.Any(siteWithNames => filtersModel.WorkerIds.Contains(siteWithNames.Key)))
					{
						continue;
					}
				}

				/*if (filtersModel.TagIds.Any()) // filtration by planning(?) tags
				{
					if (!planning.PlanningsTags.Any(x => filtersModel.TagIds.Contains(x.PlanningTagId)))
					{
						continue;
					}
				}*/

				var taskName = await itemsPlanningPnDbContext.PlanningNameTranslation
					.Where(x => x.LanguageId == userLanguageId)
					.Where(x => x.PlanningId == planning.Id)
					.Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
					.Select(x => x.Name)
					.FirstOrDefaultAsync();

				var workerNames = planningSiteIds
					.Select(x => sitesWithNames.Where(y => y.Key == x).Select(y => y.Value).FirstOrDefault())
					.ToList();

				var areaRulePlanning = await backendConfigurationPnDbContext.AreaRulePlannings
					.Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
					.Where(x => x.ItemPlanningId == compliance.PlanningId)
					.Select(x => new { x.AreaRuleId, x.StartDate })
					.FirstOrDefaultAsync();

				if (areaRulePlanning == null) continue;

				var startDate = areaRulePlanning.StartDate ?? planning.StartDate;
				var deadlineDate = compliance.Deadline.AddDays(-1);

				var listWithDateTasks = planning.RepeatType switch
				{
					Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType.Day => GetDaysBetween(deadlineDate, planning.RepeatEvery),
					Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType.Week => GetWeeksBetween(deadlineDate, planning.RepeatEvery),
					Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType.Month => GetMonthsBetween(deadlineDate, planning.RepeatEvery),
					_ => throw new ArgumentOutOfRangeException($"{planning.RepeatType} is not support")
				};

				var weeksThisCompliance = weeks
					.Select(week => new TaskTrackerWeeksListModel
					{
						WeekRange = week.WeekRange,
						WeekNumber = week.WeekNumber,
						DateList = week.DateList
							.Select(date => new TaskTrackerDateListModel
							{
								Date = date.Date,
								IsTask = listWithDateTasks
									.Exists(dateTask => dateTask.ToString("d") == date.Date.ToString("d"))
							}).ToList(),
					}).ToList();

				var complianceModel = new TaskTrackerModel
				{
					Property = propertyName,
					Tags = new(), //planning.PlanningsTags.Select(x => new CommonTagModel{Id = x.PlanningTag.Id, Name = x.PlanningTag.Name}).ToList(),
					DeadlineTask = deadlineDate,
					Workers = workerNames,
					StartTask = startDate,
					RepeatEvery = planning.RepeatEvery,
					RepeatType = (RepeatType)planning.RepeatType,
					NextExecutionTime = (DateTime)planning.NextExecutionTime,
					TaskName = taskName,
					TaskIsExpired = dateTimeNow > compliance.Deadline,
					PropertyId = compliance.PropertyId,
					SdkCaseId = compliance.MicrotingSdkCaseId,
					TemplateId = compliance.MicrotingSdkeFormId,
					ComplianceId = compliance.Id,
					AreaId = compliance.AreaId,
					AreaRuleId = areaRulePlanning!.AreaRuleId,
					Weeks = weeksThisCompliance,
				};

				result.Add(complianceModel);
			}

			//if (filtersModel.TagIds.Any() && !filtersModel.TagIds.Contains(-1))
			//{
			//	foreach (var tagId in filtersModel.TagIds)
			//	{
			//		query = query.Where(x => x.PropertyWorker.Id == tagId);
			//	}
			//}


			return new OperationDataResult<List<TaskTrackerModel>>(true, result);
		}
		catch (Exception e)
		{
			Log.LogException(e.Message);
			Log.LogException(e.StackTrace);
			return new OperationDataResult<List<TaskTrackerModel>>(false,
				$"ErrorWhileReadAllTasks: {e.Message}");
		}
	}

	private static List<DateTime> GetDaysBetween(DateTime endDate, int interval)
	{
		// Generate a list of dates, which starts from the endDate and increment with the interva in days up to 4 weeks ahead of Todays date
		var days = new List<DateTime> { endDate };

		var maxDays = DateTime.Now.AddDays(31);

		while (endDate < maxDays)
		{
			endDate = endDate.AddDays(interval);
			days.Add(endDate);
		}

		return days;
	}

	private static List<DateTime> GetWeeksBetween(DateTime endDate, int interval)
	{
		// Generate a list of dates, which starts from the endDate and increment with the interva * 7 days up to 4 weeks ahead of Todays date
		var weeks = new List<DateTime> { endDate };

		while (weeks.Count < 4)
		{
			endDate = endDate.AddDays(7 * interval);
			weeks.Add(endDate);
		}

		return weeks;
	}

	private static List<DateTime> GetMonthsBetween(DateTime endDate, int interval)
	{
		var months = new List<DateTime> { endDate };

		while (months.Count < 2)
		{
			endDate = endDate.AddMonths(interval);
			months.Add(endDate);
		}
		return months;
	}
}