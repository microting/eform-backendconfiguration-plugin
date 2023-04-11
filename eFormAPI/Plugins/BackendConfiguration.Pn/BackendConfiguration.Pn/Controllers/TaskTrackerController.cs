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

namespace BackendConfiguration.Pn.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.BackendConfigurationTaskTrackerService;
using Infrastructure.Models.TaskTracker;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using System.Collections.Generic;
using System.Threading.Tasks;

[Authorize]
[Route("api/backend-configuration-pn/task-tracker")]
public class TaskTrackerController : Controller
{
	private readonly IBackendConfigurationTaskTrackerService _backendConfigurationTaskTrackerService;

	public TaskTrackerController(
		IBackendConfigurationTaskTrackerService backendConfigurationTaskTrackerService
		)
	{
		_backendConfigurationTaskTrackerService = backendConfigurationTaskTrackerService;
	}


	[HttpPost("index")]
	public async Task<OperationDataResult<List<TaskTrackerModel>>> Index([FromBody]TaskTrackerFiltrationModel filtersModel)
	{
			return await _backendConfigurationTaskTrackerService.Index(filtersModel);
	}

	[HttpPost]
	public async Task<OperationResult> CreateTask([FromBody] TaskTrackerCreateModel filtersModel)
	{
		return await _backendConfigurationTaskTrackerService.CreateTask(filtersModel);
	}

	[HttpDelete]
	public async Task<OperationResult> DeleteTaskById([FromQuery] int taskId)
	{
		return await _backendConfigurationTaskTrackerService.DeleteTaskById(taskId);
	}

	[HttpPut]
	public async Task<OperationResult> UpdateTask([FromBody] TaskTrackerUpdateModel filtersModel)
	{
		return await _backendConfigurationTaskTrackerService.UpdateTask(filtersModel);
	}

	[HttpGet]
	public async Task<OperationDataResult<TaskTrackerModel>> GetTaskById([FromQuery] int taskId)
	{
		return await _backendConfigurationTaskTrackerService.GetTaskById(taskId);
	}

	[HttpGet("columns-get")]
	public async Task<OperationDataResult<List<TaskTrackerColumn>>> GetColumns()
	{
		return await _backendConfigurationTaskTrackerService.GetColumns();
	}
	
	[HttpPost("columns-update")]
	public async Task<OperationResult> UpdateColumns([FromBody] List<TaskTrackerColumns> updatedColumns)
	{
		return await _backendConfigurationTaskTrackerService.UpdateColumns(updatedColumns);
	}
}