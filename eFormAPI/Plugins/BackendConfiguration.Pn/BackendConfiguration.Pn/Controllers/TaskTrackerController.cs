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

namespace BackendConfiguration.Pn.Controllers;

using Infrastructure.Models.TaskTracker;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Services.BackendConfigurationTaskTrackerService;
using System.Collections.Generic;
using System.Net;
using System.Text;
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
	public async Task<OperationDataResult<List<TaskTrackerModel>>> Index([FromBody] TaskTrackerFiltrationModel filtersModel)
	{
		return await _backendConfigurationTaskTrackerService.Index(filtersModel);
	}

	[HttpGet("columns")]
	public async Task<OperationDataResult<List<TaskTrackerColumn>>> GetColumns()
	{
		return await _backendConfigurationTaskTrackerService.GetColumns();
	}

	[HttpPost("columns")]
	public async Task<OperationResult> UpdateColumns([FromBody] List<TaskTrackerColumns> updatedColumns)
	{
		return await _backendConfigurationTaskTrackerService.UpdateColumns(updatedColumns);
	}

	[HttpPost("excel")]
	public async Task GenerateExcelReport([FromBody] TaskTrackerFiltrationModel filtersModel)
	{
		var result = await _backendConfigurationTaskTrackerService.GenerateExcelReport(filtersModel);
		const int bufferSize = 4086;
		byte[] buffer = new byte[bufferSize];
		Response.OnStarting(async () =>
		{
			if (!result.Success)
			{
				Response.ContentLength = result.Message.Length;
				Response.ContentType = "text/plain";
				Response.StatusCode = (int)HttpStatusCode.InternalServerError;
				byte[] bytes = Encoding.UTF8.GetBytes(result.Message);
				await Response.Body.WriteAsync(bytes, 0, result.Message.Length);
				await Response.Body.FlushAsync();
			}
			else
			{
				await using var excelReportStream = result.Model;
				int bytesRead;
				Response.StatusCode = (int)HttpStatusCode.OK;
				Response.ContentLength = excelReportStream.Length;
				Response.ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

				while ((bytesRead = excelReportStream.Read(buffer, 0, buffer.Length)) > 0 &&
				       !HttpContext.RequestAborted.IsCancellationRequested)
				{
					await Response.Body.WriteAsync(buffer, 0, bytesRead);
					await Response.Body.FlushAsync();
				}
			}
		});
	}
}