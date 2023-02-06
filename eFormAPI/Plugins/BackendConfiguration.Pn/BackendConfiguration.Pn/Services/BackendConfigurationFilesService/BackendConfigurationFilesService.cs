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

using System.Collections.Generic;

namespace BackendConfiguration.Pn.Services.BackendConfigurationFilesService;

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BackendConfigurationFileTagsService;
using BackendConfigurationLocalizationService;
using Infrastructure.Models.Files;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Helpers;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.eFormApi.BasePn.Infrastructure.Models.Common;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using File = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.File;

public class BackendConfigurationFilesService : IBackendConfigurationFilesService
{
	private readonly ILogger<BackendConfigurationTagsService> _logger;
	private readonly IBackendConfigurationLocalizationService _localizationService;
	private readonly BackendConfigurationPnDbContext _dbContext;
	private readonly IUserService _userService;
	private readonly IEFormCoreService _coreHelper;

	public BackendConfigurationFilesService(
		ILogger<BackendConfigurationTagsService> logger,
		IBackendConfigurationLocalizationService localizationService,
		BackendConfigurationPnDbContext dbContext,
		IUserService userService, IEFormCoreService coreHelper)
	{
		_logger = logger;
		_localizationService = localizationService;
		_dbContext = dbContext;
		_userService = userService;
		_coreHelper = coreHelper;
	}

	public async Task<OperationDataResult<Paged<BackendConfigurationFileModel>>> Index(BackendConfigurationFileRequestModel request)
	{
		try
		{
			var query = _dbContext.Files
				.Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
				.Include(x => x.Property)
				.Include(x => x.FileTags)
				.ThenInclude(x => x.FileTag)
				.AsQueryable();
			// filtration
			if(!string.IsNullOrEmpty(request.NameFilter))
			{
				query = query.Where(x => x.FileName.Contains(request.NameFilter) || x.UploadedData.Extension.Contains(request.NameFilter));
			}
			if (request.DateFrom.HasValue && request.DateTo.HasValue)
			{
				// if dates is equal need set date to end this date(23h59m59s)
				if (request.DateFrom == request.DateTo)
				{
					request.DateTo = request.DateTo.Value
						.AddHours(23)
						.AddMinutes(59)
						.AddSeconds(59)
						.AddMilliseconds(999)
						.AddMicroseconds(999);
					request.DateTo = new DateTime(request.DateTo.Value.Year, request.DateTo.Value.Month, request.DateTo.Value.Day, 23, 59, 59);
				}

				query = query
					.Where(x => request.DateFrom <= x.CreatedAt)
					.Where(x => request.DateTo >= x.CreatedAt);
			}
			if (request.PropertyIds.Count > 0)
			{
				query = query.Where(x => request.PropertyIds.Contains(x.PropertyId));
			}
			if (request.TagIds.Count > 0)
			{
				query = query.Where(x => x.FileTags
					.Where(y => y.WorkflowState != Constants.WorkflowStates.Removed)
					.Any(y => request.TagIds.Contains(y.FileTagId)));
			}
			// sort
			query = QueryHelper.AddSortToQuery(query, request.Sort, request.IsSortDsc);

			// total
			var total = await query.Select(x => x.Id).CountAsync();

			// select
			var files = await query.Select(x => new BackendConfigurationFileModel
			{
				CreateDate = x.CreatedAt,
				FileName = x.FileName,
				FileExtension = x.UploadedData.Extension,
				Id = x.Id,
				Property = x.Property.Name,
				Tags = x.FileTags
					.Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
					.Select(tag => new CommonTagModel
					{
						Id = tag.FileTagId,
						Name = tag.FileTag.Name,
					}).ToList(),
			}).ToListAsync();

			var pagedFilesModel = new Paged<BackendConfigurationFileModel>
			{
				Entities = files,
				Total = total,
			};

			return new OperationDataResult<Paged<BackendConfigurationFileModel>>(true, pagedFilesModel);
		}
		catch (Exception e)
		{
			Console.WriteLine(e);
			_logger.LogError(e.Message);
			return new OperationDataResult<Paged<BackendConfigurationFileModel>>(false, _localizationService.GetString("ErrorWhileObtainingFiles"));
		}
	}

	public async Task<OperationResult> UpdateName(BackendConfigurationFileUpdateFilenameModel model)
	{
		try
		{
			var file = await _dbContext.Files
				.Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
				.Where(x => x.Id == model.Id)
				.FirstOrDefaultAsync();

			if (file == null)
			{
				return new OperationResult(false, _localizationService.GetString("FileNotFound"));
			}

			file.FileName = model.NewName;
			file.UpdatedByUserId = _userService.UserId;
			await file.Update(_dbContext);

			return new OperationResult(true);
		}
		catch (Exception e)
		{
			Console.WriteLine(e);
			_logger.LogError(e.Message);
			return new OperationResult(false, _localizationService.GetString("ErrorWhileUpdateFile"));
		}
	}

	public async Task<OperationResult> UpdateTags(BackendConfigurationFileUpdateFileTags model)
	{
		try
		{
			var fileTags = await _dbContext.FilesTags
				.Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
				.Where(x => x.FileId == model.Id)
				.ToListAsync();

			var tagsForDelete = fileTags
				.Where(x => !model.Tags.Contains(x.FileTagId)).ToList();
			var tagsForCreate = model.Tags
				.Where(x => !fileTags.Select(y => y.FileTagId).Contains(x))
				.Select(tagForCreateId => new FileTags
				{
					FileId = model.Id,
					FileTagId = tagForCreateId,
					CreatedByUserId = _userService.UserId,
					UpdatedByUserId = _userService.UserId,
				})
				.ToList();

			foreach (var newTag in tagsForCreate)
			{
				await newTag.Create(_dbContext);
			}

			foreach (var tag in tagsForDelete)
			{
				tag.UpdatedByUserId = _userService.UserId;
				await tag.Delete(_dbContext);
			}

			return new OperationResult(true);
		}
		catch (Exception e)
		{
			Console.WriteLine(e);
			_logger.LogError(e.Message);
			return new OperationResult(false, _localizationService.GetString("ErrorWhileUpdateFileTags"));
		}
	}

	public async Task<OperationResult> Create(List<BackendConfigurationFileCreate> model)
	{
		try
		{
			var core = await _coreHelper.GetCore();
			foreach (var fileCreate in model.Where(x => x.File != null))
			{
				// create file in db
				var fileForDb = new File
				{
					CreatedByUserId = _userService.UserId,
					FileName = fileCreate.File.FileName.Replace(".pdf", ""), // save only filename, without extension
					PropertyId = fileCreate.PropertyId,
					UpdatedByUserId = _userService.UserId,
				};
				await fileForDb.Create(_dbContext);

				// add tags to file
				foreach (var fileTag in fileCreate.TagIds.Select(tagId => new FileTags
				         {
					         CreatedByUserId = _userService.UserId,
					         FileId = fileForDb.Id,
					         FileTagId = tagId,
					         UpdatedByUserId = _userService.UserId,
				         }))
				{
					await fileTag.Create(_dbContext);
				}

				// upload file and save in db file info
				var folder = Path.Combine(Path.GetTempPath(), "backend-configuration-pdf");
				Directory.CreateDirectory(folder);
				var fileName = $"{DateTime.Now.Ticks}_{DateTime.Now.Microsecond}";
				var filePath = Path.Combine(folder, $"{fileName}.pdf");
				// if you replace using to await using - stream not start copy until it goes beyond the current block
				using (var stream = new FileStream(filePath, FileMode.Create))
				{
					await fileCreate.File.CopyToAsync(stream);
				}
				await core.PutFileToStorageSystem(filePath, fileCreate.File.FileName);
				var checkSum = await core.PdfUpload(filePath);
				var uploadedData = new UploadedData
				{
					Extension = "pdf",
					FileName = fileName,
					Checksum = checkSum,
					CreatedByUserId = _userService.UserId,
					UpdatedByUserId = _userService.UserId,
					FileLocation = filePath,
					FileId = fileForDb.Id,
				};
				await uploadedData.Create(_dbContext);
			}
			return new OperationResult(true);
		}
		catch (Exception e)
		{
			Console.WriteLine(e);
			_logger.LogError(e.Message);
			return new OperationResult(false, _localizationService.GetString("ErrorWhileCreateFiles"));
		}
	}

	public async Task<OperationResult> Delete(int id)
	{
		try
		{
			var file = await _dbContext.Files
				.Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
				.Where(x => x.Id == id)
				.Include(x => x.FileTags)
				.Include(x => x.UploadedData)
				.FirstOrDefaultAsync();

			if (file == null)
			{
				return new OperationResult(false, _localizationService.GetString("FileNotFound"));
			}

			// delete all links from db
			file.UploadedData.UpdatedByUserId = _userService.UserId;
			await file.UploadedData.Delete(_dbContext);

			// delete link file with tag from FilesTags table
			foreach (var fileTag in file.FileTags)
			{
				fileTag.UpdatedByUserId = _userService.UserId;
				await fileTag.Delete(_dbContext);
			}

			file.UpdatedByUserId = _userService.UserId;
			await file.Delete(_dbContext);

			return new OperationResult(true);
		}
		catch (Exception e)
		{
			Console.WriteLine(e);
			_logger.LogError(e.Message);
			return new OperationResult(false, _localizationService.GetString("ErrorWhileDeleteFile"));
		}
	}

	public async Task<OperationDataResult<BackendConfigurationFileModel>> GetById(int id)
	{
		try
		{
			var file = await _dbContext.Files
				.Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
				.Where(x => x.Id == id)
				.Include(x => x.FileTags)
				.Select(x => new BackendConfigurationFileModel
				{
					CreateDate = x.CreatedAt,
					FileName = x.FileName,
					FileExtension = x.UploadedData.Extension,
					Id = x.Id,
					Property = x.Property.Name,
					Tags = x.FileTags
						.Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
						.Select(tag => new CommonTagModel
						{
							Id = tag.FileTagId,
							Name = tag.FileTag.Name,
						}).ToList(),
				})
				.FirstOrDefaultAsync();

			if (file == null)
			{
				return new OperationDataResult<BackendConfigurationFileModel>(false, _localizationService.GetString("FileNotFound"));
			}

			return new OperationDataResult<BackendConfigurationFileModel>(true, file);
		}
		catch (Exception e)
		{
			Console.WriteLine(e);
			_logger.LogError(e.Message);
			return new OperationDataResult<BackendConfigurationFileModel>(false, _localizationService.GetString("ErrorWhileGetFile"));
		}
	}

	public async Task<string> GetUploadedDataByFileId(int id)
	{
		try
		{
			var filePath = await _dbContext.UploadedDatas
				.Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
				.Where(x => x.FileId == id)
				.Select(x => x.FileLocation)
				.FirstOrDefaultAsync();

			if (string.IsNullOrEmpty(filePath))
			{
				return _localizationService.GetString("FileNotFound");
			}

			return filePath;
		}
		catch (Exception e)
		{
			Console.WriteLine(e);
			_logger.LogError(e.Message);
			return _localizationService.GetString("ErrorWhileGetFile");
		}
	}
}