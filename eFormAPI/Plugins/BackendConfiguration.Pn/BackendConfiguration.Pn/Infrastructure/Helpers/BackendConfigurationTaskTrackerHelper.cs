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
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Infrastructure.Helpers;

public static class BackendConfigurationTaskTrackerHelper
{
	public static async Task<OperationDataResult<List<TaskTrackerModel>>> Index(
		TaskTrackerFiltrationModel filtersModel,
		BackendConfigurationPnDbContext backendConfigurationPnDbContext,
		Core core,
		ItemsPlanningPnDbContext itemsPlanningPnDbContext)
	{

		try
		{
			var result = new List<TaskTrackerModel>();
			
			var sdkDbContext = core.DbContextHelper.GetDbContext();
			var query = backendConfigurationPnDbContext.Compliances
				.Where(x => x.WorkflowState != Constants.WorkflowStates.Removed);

			if (filtersModel.PropertyIds.Any() && !filtersModel.PropertyIds.Contains(-1))
			{
				query = query.Where(x => filtersModel.PropertyIds.Contains(x.PropertyId));
			}

			var complianceList = await query
				.AsNoTracking()
				.OrderBy(x => x.Deadline)
				.ToListAsync();

			foreach (var compliance in complianceList)
			{
				/*var areaTranslation = await backendConfigurationPnDbContext.AreaTranslations
					.SingleOrDefaultAsync(x => x.AreaId == compliance.AreaId && x.LanguageId == language.Id);*/

				var propertyName = await backendConfigurationPnDbContext.Properties
					.Where(x => x.Id == compliance.PropertyId)
					.Select(x => x.Name)
					.FirstOrDefaultAsync();

				var planning = await itemsPlanningPnDbContext.Plannings
					.Where(x => x.Id == compliance.PlanningId)
					.Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
				.FirstOrDefaultAsync();

				if (/*areaTranslation == null || */planning == null)
				{
					continue;
				}

				var planningSitesQuery = itemsPlanningPnDbContext.PlanningSites
					.Where(x => x.PlanningId == compliance.PlanningId)
					.Where(x => x.WorkflowState != Constants.WorkflowStates.Removed);

				var planningSiteIds = await planningSitesQuery
					.Select(x => x.SiteId)
					.Distinct()
					.ToListAsync();

				var sitesWithNames = await sdkDbContext.Sites
					.Where(x => planningSiteIds.Contains(x.Id))
					.Select(site => new KeyValuePair<int, string>(site.Id, site.Name))
					.ToListAsync();

				if (filtersModel.WorkerIds.Any() && !filtersModel.WorkerIds.Contains(-1))
				{
					if (!sitesWithNames.Any(siteWithNames => filtersModel.WorkerIds.Contains(siteWithNames.Key)))
					{
						continue;
					}
				}

				var workerNames = planningSiteIds
					.Select(x => sitesWithNames.Where(y => y.Key == x).Select(y => y.Value).FirstOrDefault())
					.ToList();

				var complianceModel = new TaskTrackerModel
				{
					Property = propertyName,
					Tags = new(),
					DeadlineTask = compliance.Deadline,
					Workers = workerNames,
					StartTask = compliance.StartDate,
					RepeatEvery = planning.RepeatEvery,
					RepeatType = planning.RepeatType,
					NextExecutionTime = (DateTime)planning.NextExecutionTime,
					TaskName = ""
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
}