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

using Microting.eFormApi.BasePn.Abstractions;

namespace BackendConfiguration.Pn.Controllers;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Services.BackendConfigurationLocalizationService;
using Infrastructure.Models.Files;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.eFormApi.BasePn.Infrastructure.Models.Common;
using Services.BackendConfigurationFilesService;
using BackendConfiguration.Pn.Services.BackendConfigurationFileTagsService;
using Microsoft.Extensions.Logging;
using BackendConfiguration.Pn.Infrastructure.Helpers;

[Authorize]
[Route("api/backend-configuration-pn/files")]
public class FilesController : Controller
{
	private readonly IBackendConfigurationFilesService _backendConfigurationFilesService;
	private readonly IBackendConfigurationLocalizationService _localizationService;
	private ILogger<FilesController> logger;
	private readonly IEFormCoreService _coreHelper;

	public FilesController(
		IBackendConfigurationFilesService backendConfigurationFilesService,
		IBackendConfigurationLocalizationService localizationService,
		ILogger<FilesController> logger, IEFormCoreService coreHelper)
	{
		_backendConfigurationFilesService = backendConfigurationFilesService;
		_localizationService = localizationService;
		this.logger = logger;
		_coreHelper = coreHelper;
	}

	[HttpPost]
	public async Task<OperationDataResult<Paged<BackendConfigurationFilesModel>>> Index([FromBody] BackendConfigurationFileRequestModel request)
	{
		return await _backendConfigurationFilesService.Index(request);
	}

	/// <summary>Updates the name file.</summary>
	/// <param name="model">The model.</param>
	[HttpPut]
	public async Task<OperationResult> UpdateName([FromBody] BackendConfigurationFileUpdateFilenameModel model)
	{
		return await _backendConfigurationFilesService.UpdateName(model);
	}

	/// <summary>Updates the tags.</summary>
	/// <param name="model">The model.</param>
	[HttpPut("tags")]
	public async Task<OperationResult> UpdateTags([FromBody] BackendConfigurationFileUpdateFileTags model)
	{
		return await _backendConfigurationFilesService.UpdateTags(model);
	}

	[HttpPost]
	[Route("create")]
	public async Task<OperationResult> Create([FromForm] BackendConfigurationFileCreateList model)
	{
		foreach (var formFile in HttpContext.Request.Form.Files)
		{
			ReflectionSetProperty.SetProperty(model, formFile.Name.Replace("][", ".").Replace("[", ".").Replace("]", ""), formFile);
		}
		return await _backendConfigurationFilesService.Create(model.FilesForCreate);
	}

	[HttpDelete]
	[Route("{id}")]
	public async Task<OperationResult> Delete(int id)
	{
		return await _backendConfigurationFilesService.Delete(id);
	}

	[HttpGet]
	[Route("{id}")]
	public async Task<OperationDataResult<BackendConfigurationFileModel>> GetById(int id)
	{
		return await _backendConfigurationFilesService.GetById(id);
	}

	[HttpGet]
	[AllowAnonymous]
	[Route("get-file/{id}")]
	public async Task<IActionResult> GetLoginPageImage(int id)
	{
		var core = await _coreHelper.GetCore();
		var uploadedData = await _backendConfigurationFilesService.GetUploadedDataByFileId(id);

		var ss = await core.GetFileFromS3Storage($"{uploadedData.Checksum}.pdf");

		if (ss != null)
		{
			return File(ss.ResponseStream, "application/pdf", uploadedData.FileName);
		}
		return new NotFoundResult();
	}

	[HttpPost]
	[Route("get-files")]
	public async Task<IActionResult> GetArchiveFiles([FromBody] BackendConfigurationArchiveFile model)
	{
		var core = await _coreHelper.GetCore();
		if (model.FileIds is { Count: > 0 })
		{
			using var archiveStream = new MemoryStream();
			using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, true))
			{
				foreach (var fileId in model.FileIds)
				{
					var uploadedData = await _backendConfigurationFilesService.GetUploadedDataByFileId(fileId);
					var ss = await core.GetFileFromS3Storage($"{uploadedData.Checksum}.pdf");
					var operationDataResult = await _backendConfigurationFilesService.GetById(fileId);
					var filePath = uploadedData.FileLocation;
					if (filePath != _localizationService.GetString("FileNotFound") &&
					    filePath != _localizationService.GetString("ErrorWhileGetFile") &&
					    !string.IsNullOrEmpty(filePath) &&
					    System.IO.File.Exists(filePath))
					{
						var zipArchiveEntry = archive.CreateEntry($"{operationDataResult.Model.FileName}.pdf",
							CompressionLevel.Fastest);
						await using var zipStream = zipArchiveEntry.Open();
						await ss.ResponseStream.CopyToAsync(zipStream);
					}
					else
					{
						logger.LogWarning($"File not found. File path: {filePath}; FileId in db: {operationDataResult.Model.Id}; FileId in http query: {fileId}.");
					}
				}
			}

			return File(archiveStream.ToArray(), "application/zip", model.ArchiveName);
		}
		return Ok(new OperationResult(false, "NotSelectedFiles"));
	}

	// static byte[] LoadFromFile(string path)
	// {
	// 	using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
	// 	using var memFile = new MemoryStream();
	// 	fs.CopyTo(memFile);
	//
	// 	memFile.Seek(0, SeekOrigin.Begin);
	//
	// 	return memFile.ToArray();
	// }
}