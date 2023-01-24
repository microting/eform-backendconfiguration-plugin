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

using System.Threading.Tasks;
using Infrastructure.Models.Files;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.eFormApi.BasePn.Infrastructure.Models.Common;
using Services.BackendConfigurationFilesService;

[Authorize]
[Route("api/backend-configuration-pn/files")]
public class FilesController : Controller
{
	private readonly IBackendConfigurationFilesService _backendConfigurationTagsService;

	public FilesController(IBackendConfigurationFilesService backendConfigurationTagsService)
	{
		_backendConfigurationTagsService = backendConfigurationTagsService;
	}

	[HttpGet]
	public async Task<OperationDataResult<Paged<BackendConfigurationFileModel>>> Index([FromQuery] BackendConfigurationFileRequestModel request)
	{
		return await _backendConfigurationTagsService.Index(request);
	}

	/// <summary>Updates the name file.</summary>
	/// <param name="model">The model.</param>
	[HttpPut]
	public async Task<OperationResult> UpdateName([FromBody] BackendConfigurationFileUpdateFilenameModel model)
	{
		return await _backendConfigurationTagsService.UpdateName(model);
	}

	/// <summary>Updates the tags.</summary>
	/// <param name="model">The model.</param>
	[HttpPut("tags")]
	public async Task<OperationResult> UpdateTags([FromBody] BackendConfigurationFileUpdateFileTags model)
	{
		return await _backendConfigurationTagsService.UpdateTags(model);
	}

	[HttpPost]
	public async Task<OperationResult> Create([FromBody] object model)
	{
		return await _backendConfigurationTagsService.Create(model);
	}

	[HttpDelete]
	[Route("{id}")]
	public async Task<OperationResult> Delete(int id)
	{
		return await _backendConfigurationTagsService.Delete(id);
	}

	[HttpGet]
	[Route("{id}")]
	public async Task<OperationDataResult<BackendConfigurationFileModel>> GetById(int id)
	{
		return await _backendConfigurationTagsService.GetById(id);
	}
}