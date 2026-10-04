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

using Infrastructure.Helpers;
using Infrastructure.Models.Files;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.eFormApi.BasePn.Infrastructure.Models.Common;
using Services.BackendConfigurationFilesService;
using Services.BackendConfigurationLocalizationService;
using Services.FileArchive;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

[Authorize]
[Route("api/backend-configuration-pn/files")]
public class FilesController : Controller
{
	private readonly IBackendConfigurationFilesService _backendConfigurationFilesService;
	private readonly IBackendConfigurationLocalizationService _localizationService;
	private readonly ILogger<FilesController> _logger;
	private readonly IArchiveStorage _archiveStorage;

	public FilesController(
		IBackendConfigurationFilesService backendConfigurationFilesService,
		IBackendConfigurationLocalizationService localizationService,
		ILogger<FilesController> logger,
		IArchiveStorage archiveStorage)
	{
		_backendConfigurationFilesService = backendConfigurationFilesService;
		_localizationService = localizationService;
		_logger = logger;
		_archiveStorage = archiveStorage;
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

	[HttpPut("properties")]
	public async Task<OperationResult> UpdateProperties([FromBody] BackendConfigurationFileUpdateProperties model)
	{
		return await _backendConfigurationFilesService.UpdateProperties(model);
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
		var uploadedData = await _backendConfigurationFilesService.GetUploadedDataByFileId(id);

		// IArchiveStorage reads S3 or local storage per the SDK s3Enabled setting; the object name is the
		// same "{checksum}.{extension}" key the S3-only read used.
		var stream = await _archiveStorage.GetAsync(FileArchiver.ObjectName(uploadedData.Checksum, uploadedData.Extension));

		if (stream != null)
		{
			if (uploadedData.Extension == "pdf")
			{
				return File(stream, "application/pdf", uploadedData.FileName);
			}

			return File(stream, $"image/{uploadedData.Extension}", uploadedData.FileName);
		}
		return new NotFoundResult();
	}

	[HttpPost]
	[Route("get-files")]
	public async Task<IActionResult> GetArchiveFiles([FromBody] BackendConfigurationArchiveFile model)
	{
		if (model.FileIds is { Count: > 0 })
		{
			using var archiveStream = new MemoryStream();
			using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, true))
			{
				foreach (var fileId in model.FileIds)
				{
					var uploadedData = await _backendConfigurationFilesService.GetUploadedDataByFileId(fileId);
					await using var stream = await _archiveStorage.GetAsync(FileArchiver.ObjectName(uploadedData.Checksum, uploadedData.Extension));
					if (stream == null)
					{
						_logger.LogWarning("Archive file {FileId} is missing from storage; no partial zip is returned", fileId);
						return new NotFoundResult();
					}

					var operationDataResult = await _backendConfigurationFilesService.GetById(fileId);
					var zipArchiveEntry = archive.CreateEntry($"{operationDataResult.Model.FileName}.{uploadedData.Extension}",
						CompressionLevel.Fastest);
					await using var zipStream = zipArchiveEntry.Open();
					await stream.CopyToAsync(zipStream);
				}
			}

			return File(archiveStream.ToArray(), "application/zip", model.ArchiveName);
		}
		return Ok(new OperationResult(false, _localizationService.GetString("NotSelectedFiles")));
	}
}