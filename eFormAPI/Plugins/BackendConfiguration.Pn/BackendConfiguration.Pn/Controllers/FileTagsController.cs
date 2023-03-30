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

using Services.BackendConfigurationFileTagsService;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.eFormApi.BasePn.Infrastructure.Models.Common;
using System.Threading.Tasks;
using System.Collections.Generic;
using Infrastructure.Models.Files;

[Authorize]
[Route("api/backend-configuration-pn/file-tags")]
public class FileTagsController : Controller
{
	private readonly IBackendConfigurationTagsService _backendConfigurationTagsService;

	public FileTagsController(IBackendConfigurationTagsService backendConfigurationTagsService)
	{
		_backendConfigurationTagsService = backendConfigurationTagsService;
	}

	[HttpGet]
	public async Task<OperationDataResult<List<CommonTagModel>>> Index()
	{
		return await _backendConfigurationTagsService.GetTags();
	}

	[HttpPut]
	public async Task<OperationResult> Update([FromBody] CommonTagModel tag)
	{
		return await _backendConfigurationTagsService.UpdateTag(tag);
	}

	[HttpPost]
	public async Task<OperationResult> Create([FromBody] CommonTagModel tag)
	{
		return await _backendConfigurationTagsService.CreateTag(tag);
	}

	[HttpDelete]
	[Route("{id}")]
	public async Task<OperationResult> Delete(int id)
	{
		return await _backendConfigurationTagsService.DeleteTag(id);
	}

	[HttpGet]
	[Route("{id}")]
	public async Task<OperationDataResult<CommonTagModel>> GetById(int id)
	{
		return await _backendConfigurationTagsService.GetById(id);
	}


	[HttpPost]
	[Route("bulk")]
	public async Task<OperationResult> BulkFileTags([FromBody] BackendConfigurationFileBulkTags tags)
	{
		return await _backendConfigurationTagsService.BulkFileTags(tags);
	}
}