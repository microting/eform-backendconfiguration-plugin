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

public class BackendConfigurationTaskTrackerService: IBackendConfigurationTaskTrackerService
{
	private readonly IBackendConfigurationLocalizationService _localizationService;
	private readonly IUserService _userService;
	private readonly BackendConfigurationPnDbContext _backendConfigurationPnDbContext;
	private readonly IEFormCoreService _coreHelper;

	public BackendConfigurationTaskTrackerService(
		IBackendConfigurationLocalizationService localizationService,
		IUserService userService,
		BackendConfigurationPnDbContext backendConfigurationPnDbContext,
		IEFormCoreService coreHelper
		)
	{
		_localizationService = localizationService;
		_userService = userService;
		_backendConfigurationPnDbContext = backendConfigurationPnDbContext;
		_coreHelper = coreHelper;
	}


	public async Task<OperationDataResult<List<TaskTrackerModel>>> Index(TaskTrackerFiltrationModel filtersModel)
	{
		try
		{
			return new OperationDataResult<List<TaskTrackerModel>>(true, new List<TaskTrackerModel>());
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
			var columns = await _backendConfigurationPnDbContext.TaskTrackerColumns.Where(p => p.UserId == userId ).Select(p => new TaskTrackerColumn { ColumnName = p.ColumnName, isColumnEnabled = p.isColumnEnabled, UserId = p.UserId}).ToListAsync();
			return new OperationDataResult<List<TaskTrackerColumn>> (true, columns);
			
			
			/*var taskTrackerColumns = columns.Select(c => new TaskTrackerColumns()
			{
				ColumnName = c.ColumnName,
				IsColumnEnabled = c.IsColumnEnabled
			}).ToList();*/
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
				// var column = taskTrackerColumns.FirstOrDefault(c => c.ColumnName == updatedColumn.ColumnName);
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