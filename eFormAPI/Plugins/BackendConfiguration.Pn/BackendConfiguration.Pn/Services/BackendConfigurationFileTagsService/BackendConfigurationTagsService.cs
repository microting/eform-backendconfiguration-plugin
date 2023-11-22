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

namespace BackendConfiguration.Pn.Services.BackendConfigurationFileTagsService
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Threading.Tasks;
	using BackendConfiguration.Pn.Infrastructure.Models.Files;
	using BackendConfigurationLocalizationService;
	using Microsoft.EntityFrameworkCore;
	using Microsoft.Extensions.Logging;
	using Microting.eForm.Infrastructure.Constants;
	using Microting.eFormApi.BasePn.Abstractions;
	using Microting.eFormApi.BasePn.Infrastructure.Models.API;
	using Microting.eFormApi.BasePn.Infrastructure.Models.Common;
	using Microting.EformBackendConfigurationBase.Infrastructure.Data;
	using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;

	public class BackendConfigurationTagsService : IBackendConfigurationTagsService
	{
		private readonly ILogger<BackendConfigurationTagsService> _logger;
		private readonly IBackendConfigurationLocalizationService _localizationService;
		private readonly BackendConfigurationPnDbContext _dbContext;
		private readonly IUserService _userService;

		public BackendConfigurationTagsService(
			IBackendConfigurationLocalizationService itemsPlanningLocalizationService,
			ILogger<BackendConfigurationTagsService> logger,
			BackendConfigurationPnDbContext dbContext,
			IUserService userService
		)
		{
			_localizationService = itemsPlanningLocalizationService;
			_logger = logger;
			_dbContext = dbContext;
			_userService = userService;
		}

		public async Task<OperationDataResult<List<CommonTagModel>>> GetTags()
		{
			try
			{
				var tags = await _dbContext.FileTags
					.Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
					.OrderBy(x => x.Name)
					.Select(x => new CommonTagModel
					{
						Id = x.Id,
						Name = x.Name
					}).ToListAsync();

				return new OperationDataResult<List<CommonTagModel>>(true, tags);
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
				_logger.LogError(e.Message);
				return new OperationDataResult<List<CommonTagModel>>(false,
					_localizationService.GetString("ErrorWhileObtainingFileTags"));
			}
		}

		public async Task<OperationResult> UpdateTag(CommonTagModel requestModel)
		{
			try
			{
				var tag = await _dbContext.FileTags
					.Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
					.FirstOrDefaultAsync(x => x.Id == requestModel.Id);

				if (tag == null)
				{
					return new OperationResult(false, _localizationService.GetString("FileTagNotFound"));
				}

				tag.Name = requestModel.Name;
				tag.UpdatedByUserId = _userService.UserId;

				await tag.Update(_dbContext);

				return new OperationResult(true, _localizationService.GetString("FileTagUpdatedSuccessfully"));
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
				_logger.LogError(e.Message);
				return new OperationResult(false, _localizationService.GetString("ErrorWhileUpdatingFileTag"));
			}
		}

		public async Task<OperationResult> DeleteTag(int id)
		{
			try
			{
				var tag = await _dbContext.FileTags
					.Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
					.FirstOrDefaultAsync(x => x.Id == id);

				if (tag == null)
				{
					return new OperationResult(
						false,
						_localizationService.GetString("FileTagNotFound"));
				}

				var fileTagsList = await _dbContext.FilesTags
					.Where(x => x.FileTagId == id).ToListAsync();

				foreach (var fileTags in fileTagsList)
				{
					fileTags.UpdatedByUserId = _userService.UserId;
					await fileTags.Delete(_dbContext);
				}

				tag.UpdatedByUserId = _userService.UserId;
				await tag.Delete(_dbContext);

				return new OperationResult(true, _localizationService.GetString("FileTagRemovedSuccessfully"));
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
				_logger.LogError(e.Message);
				return new OperationResult(false, _localizationService.GetString("ErrorWhileRemovingFileTag"));
			}
		}

		public async Task<OperationResult> CreateTag(CommonTagModel requestModel)
		{
			var currentTag = await _dbContext.FileTags
				.FirstOrDefaultAsync(x => x.Name == requestModel.Name);

			if (currentTag != null)
			{
				if (currentTag.WorkflowState != Constants.WorkflowStates.Removed)
				{
					return new OperationResult(true, _localizationService.GetString("FileTagCreatedSuccessfully"));
				}
				currentTag.WorkflowState = Constants.WorkflowStates.Created;
				currentTag.UpdatedByUserId = _userService.UserId;
				await currentTag.Update(_dbContext);
				return new OperationResult(true, _localizationService.GetString("FileTagCreatedSuccessfully"));
			}
			try
			{
				var tag = new FileTag
				{
					Name = requestModel.Name,
					CreatedByUserId = _userService.UserId,
					UpdatedByUserId = _userService.UserId
				};

				await tag.Create(_dbContext);

				return new OperationResult(true, _localizationService.GetString("FileTagCreatedSuccessfully"));
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
				_logger.LogError(e.Message);
				return new OperationResult(false, _localizationService.GetString("ErrorWhileCreatingFileTag"));
			}
		}

		public async Task<OperationDataResult<CommonTagModel>> GetById(int id)
		{
			try
			{
				var tag = await _dbContext.FileTags
					.Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
					.Select(x => new CommonTagModel
					{
						Id = x.Id,
						Name = x.Name
					})
					.FirstOrDefaultAsync(x => x.Id == id);

				if (tag == null)
				{
					return new OperationDataResult<CommonTagModel>(false,
						_localizationService.GetString("FileTagNotFound"));
				}

				return new OperationDataResult<CommonTagModel>(true, tag);
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
				_logger.LogError(e.Message);
				return new OperationDataResult<CommonTagModel>(false,
					_localizationService.GetString("ErrorWhileObtainingFileTag"));
			}
		}

		public async Task<OperationResult> BulkFileTags(BackendConfigurationFileBulkTags requestModel)
		{
			try
			{
				foreach (var tagName in requestModel.TagNames)
				{
					if (await _dbContext.FileTags.AnyAsync(x =>
						    x.Name == tagName && x.WorkflowState != Constants.WorkflowStates.Removed))
					{
						continue; // skip replies
					}

					var itemsPlanningTag = new FileTag
					{
						Name = tagName,
						CreatedByUserId = _userService.UserId,
						UpdatedByUserId = _userService.UserId
					};

					await itemsPlanningTag.Create(_dbContext);
				}

				return new OperationResult(
					true,
					_localizationService.GetString("FileTagsCreatedSuccessfully"));
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
				_logger.LogError(e.Message);
				return new OperationResult(
					false,
					_localizationService.GetString("ErrorWhileCreatingFileTags"));
			}
		}

	}
}