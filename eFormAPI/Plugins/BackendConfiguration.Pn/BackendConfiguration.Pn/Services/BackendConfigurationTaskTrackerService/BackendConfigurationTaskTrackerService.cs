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

using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;

namespace BackendConfiguration.Pn.Services.BackendConfigurationTaskTrackerService;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Infrastructure.Models.TaskManagement;
using BackendConfigurationLocalizationService;
using Infrastructure.Models.TaskTracker;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Helpers;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.ItemsPlanningBase.Infrastructure.Data;
using BackendConfiguration.Pn.Infrastructure.Helpers;
using BackendConfiguration.Pn.Infrastructure.Models.Compliances.Index;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eFormApi.BasePn.Infrastructure.Models.Common;
using static Microting.EformAngularFrontendBase.Infrastructure.Const.AuthConsts.EformPolicies;
using System.Security.Policy;

public class BackendConfigurationTaskTrackerService : IBackendConfigurationTaskTrackerService
{
	private readonly IBackendConfigurationLocalizationService _localizationService;
	private readonly IUserService _userService;
	private readonly BackendConfigurationPnDbContext _backendConfigurationPnDbContext;
	private readonly IEFormCoreService _coreHelper;
	private readonly ItemsPlanningPnDbContext _itemsPlanningPnDbContext;

	public BackendConfigurationTaskTrackerService(
		IBackendConfigurationLocalizationService localizationService,
		IUserService userService,
		BackendConfigurationPnDbContext backendConfigurationPnDbContext,
		IEFormCoreService coreHelper,
		ItemsPlanningPnDbContext itemsPlanningPnDbContext
	)
	{
		_localizationService = localizationService;
		_userService = userService;
		_backendConfigurationPnDbContext = backendConfigurationPnDbContext;
		_coreHelper = coreHelper;
		_itemsPlanningPnDbContext = itemsPlanningPnDbContext;
	}


	public async Task<OperationDataResult<List<TaskTrackerModel>>> Index(TaskTrackerFiltrationModel filtersModel)
	{
		try
		{
			var language = await _userService.GetCurrentUserLanguage();
			var result = new List<TaskTrackerModel>();

			var core = await _coreHelper.GetCore();
			var sdkDbContext = core.DbContextHelper.GetDbContext();
			var query = _backendConfigurationPnDbContext.Compliances
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
				var planningNameTranslation = await _itemsPlanningPnDbContext.PlanningNameTranslation
					.SingleOrDefaultAsync(x => x.PlanningId == compliance.PlanningId && x.LanguageId == language.Id);

				if (planningNameTranslation == null)
				{
					continue;
				}
				var areaTranslation = await _backendConfigurationPnDbContext.AreaTranslations
					.SingleOrDefaultAsync(x => x.AreaId == compliance.AreaId && x.LanguageId == language.Id);

				var propertyName = await _backendConfigurationPnDbContext.Properties
					.Where(x => x.Id == compliance.PropertyId)
					.Select(x => x.Name)
					.FirstOrDefaultAsync();

				var planning = await _itemsPlanningPnDbContext.Plannings
					.Where(x => x.Id == compliance.PlanningId)
					.Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
					.FirstOrDefaultAsync();

				if (areaTranslation == null || planning == null)
				{
					continue;
				}

				var planningSitesQuery = _itemsPlanningPnDbContext.PlanningSites
					.Where(x => x.PlanningId == compliance.PlanningId)
					.Where(x => x.WorkflowState != Constants.WorkflowStates.Removed);

				//if (filtersModel.WorkerIds.Any() && !filtersModel.WorkerIds.Contains(-1))
				//{
				//	foreach (var workerId in filtersModel.WorkerIds)
				//	{
				//		planningSitesQuery = planningSitesQuery.Where(x => x.SiteId == workerId);
				//	}
				//}
				var planningSiteIds = await planningSitesQuery
					.Select(x => x.SiteId)
					.Distinct()
					.ToListAsync();

				var sitesWithNames = await sdkDbContext.Sites.Where(x => planningSiteIds.Contains(x.Id)).Select(site => new KeyValuePair<int, string>(site.Id, site.Name)).ToListAsync();

				var workerNames = planningSiteIds
					.Select(x => sitesWithNames.Where(y => y.Key == x).Select(y => y.Value).FirstOrDefault())
					.ToList();

				var complianceModel = new TaskTrackerModel
				{
					Property = propertyName,
					Tags = new (),
					DeadlineTask = compliance.Deadline,
					Workers = workerNames,
					StartTask = compliance.StartDate,
					Repeat = $"{planning.RepeatEvery} {planning.RepeatType}",
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
				$"{_localizationService.GetString("ErrorWhileReadAllTasks")}: {e.Message}");
		}
	}

	public async Task<OperationDataResult<TaskTrackerModel>> GetTaskById(int taskId)
	{
		try
		{
			var core = await _coreHelper.GetCore().ConfigureAwait(false);
			var sdkDbContext = core.DbContextHelper.GetDbContext();
			var workOrderCase = await _backendConfigurationPnDbContext.WorkorderCases
				.Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
				.Where(x => x.Id == taskId)
				.FirstOrDefaultAsync();

			return new OperationDataResult<TaskTrackerModel>(true, new TaskTrackerModel());
		}
		catch (Exception e)
		{
			Log.LogException(e.Message);
			Log.LogException(e.StackTrace);
			return new OperationDataResult<TaskTrackerModel>(false,
				$"{_localizationService.GetString("ErrorWhileReadTask")}: {e.Message}");
		}
	}

	public async Task<OperationResult> DeleteTaskById(int taskId)
	{
		try
		{
			var core = await _coreHelper.GetCore().ConfigureAwait(false);
			var sdkDbContext = core.DbContextHelper.GetDbContext();
			var workOrderCase = await _backendConfigurationPnDbContext.WorkorderCases
				.Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
				.Where(x => x.Id == taskId)
				.FirstOrDefaultAsync();

			if (workOrderCase == null)
			{
				return new OperationDataResult<WorkOrderCaseReadModel>(false,
					_localizationService.GetString("TaskNotFound"));
			}

			return new OperationResult(true, _localizationService.GetString("TaskDeletedSuccessful"));
		}
		catch (Exception e)
		{
			Log.LogException(e.Message);
			Log.LogException(e.StackTrace);
			return new OperationDataResult<TaskTrackerModel>(false,
				$"{_localizationService.GetString("ErrorWhileDeleteTask")}: {e.Message}");
		}
	}

	public async Task<OperationResult> CreateTask(TaskTrackerCreateModel createModel)
	{
		try
		{
			return new OperationResult(true, _localizationService.GetString("TaskCreatedSuccessful"));
		}
		catch (Exception e)
		{
			Log.LogException(e.Message);
			Log.LogException(e.StackTrace);
			return new OperationDataResult<TaskTrackerModel>(false,
				$"{_localizationService.GetString("ErrorWhileCreateTask")}: {e.Message}");
		}
	}

	public async Task<OperationResult> UpdateTask(TaskTrackerUpdateModel updateModel)
	{
		try
		{
			var core = await _coreHelper.GetCore().ConfigureAwait(false);
			var sdkDbContext = core.DbContextHelper.GetDbContext();
			var workOrderCase = await _backendConfigurationPnDbContext.WorkorderCases
				.Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
				.Where(x => x.Id == updateModel.Id)
				.FirstOrDefaultAsync();

			if (workOrderCase == null)
			{
				return new OperationDataResult<WorkOrderCaseReadModel>(false,
					_localizationService.GetString("TaskNotFound"));
			}

			return new OperationResult(true, _localizationService.GetString("TaskUpdatedSuccessful"));
		}
		catch (Exception e)
		{
			Log.LogException(e.Message);
			Log.LogException(e.StackTrace);
			return new OperationDataResult<TaskTrackerModel>(false,
				$"{_localizationService.GetString("ErrorWhileUpdateTask")}: {e.Message}");
		}
	}

	public async Task<OperationDataResult<List<TaskTrackerColumn>>> GetColumns()
	{
		var userId = _userService.UserId;
		try
		{
			var columns = await _backendConfigurationPnDbContext.TaskTrackerColumns
				.Where(p => p.UserId == userId )
				.Select(p => new TaskTrackerColumn { ColumnName = p.ColumnName, isColumnEnabled = p.isColumnEnabled, UserId = p.UserId})
				.ToListAsync();
			return new OperationDataResult<List<TaskTrackerColumn>> (true, columns);
			
		}
		catch (Exception e)
		{
			Log.LogException(e.Message);
			Log.LogException(e.StackTrace);
			return new OperationDataResult<List<TaskTrackerColumn>>(false,
				$"{_localizationService.GetString("ErrorWhileUpdateTask")}: {e.Message}");
		}
	}
	
	public async Task<OperationResult> UpdateColumns(List<TaskTrackerColumns> updatedColumns)
	{
		try
		{

			var userId = _userService.UserId;
			
			foreach (var updatedColumn in updatedColumns)
			{
				var columnFromDb = await _backendConfigurationPnDbContext.TaskTrackerColumns
					.Where(p => p.UserId == userId)
					.Where(p => p.ColumnName == updatedColumn.ColumnName).FirstOrDefaultAsync();
				if (columnFromDb is null)
				{
					columnFromDb = new TaskTrackerColumn();
					columnFromDb.isColumnEnabled = updatedColumn.IsColumnEnabled;
					columnFromDb.ColumnName = updatedColumn.ColumnName;
					columnFromDb.UserId = userId;
					columnFromDb.CreatedByUserId = userId;
					columnFromDb.UpdatedByUserId = userId;
					await columnFromDb.Create(_backendConfigurationPnDbContext);
					
					continue;
				} 
				if (columnFromDb.isColumnEnabled != updatedColumn.IsColumnEnabled)
				{
					columnFromDb.isColumnEnabled = updatedColumn.IsColumnEnabled;
					columnFromDb.UpdatedByUserId = userId;
					await columnFromDb.Update(_backendConfigurationPnDbContext);
				}
			}
			await _backendConfigurationPnDbContext.SaveChangesAsync();
			return new OperationDataResult<List<TaskTrackerColumns>>(true,$"{_localizationService.GetString("ColumnsUpdated")}");
		}
		catch (Exception e)
		{
			Log.LogException(e.Message);
			Log.LogException(e.StackTrace);
			return new OperationResult(false,
				$"{_localizationService.GetString("ErrorWhileUpdateTask")}: {e.Message}");
		}
	}
}