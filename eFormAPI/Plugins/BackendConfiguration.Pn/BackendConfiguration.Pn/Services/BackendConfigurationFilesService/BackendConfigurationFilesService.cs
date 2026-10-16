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
using Microting.EformAngularFrontendBase.Infrastructure.Data;

namespace BackendConfiguration.Pn.Services.BackendConfigurationFilesService;

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ArchiveAccess;
using ArchiveFolders;
using BackendConfigurationFileTagsService;
using BackendConfigurationLocalizationService;
using FileArchive;
using Infrastructure.Models.ArchiveFolders;
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
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using UploadedData = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.UploadedData;

/// <summary>
/// The archive documents. Every read and write goes through <see cref="IArchiveAccessService"/>: a disabled caller
/// gets <see cref="ArchiveAccountDisabledException"/> (403), a missing or unreadable document
/// <see cref="ArchiveNotFoundException"/> (404). Refusals are thrown outside the catch filters (ArchiveExceptions.IsUnexpected), so
/// they never become success=false.
/// </summary>
public class BackendConfigurationFilesService : IBackendConfigurationFilesService
{
	private const string Removed = Constants.WorkflowStates.Removed;
	private readonly ILogger<BackendConfigurationTagsService> _logger;
	private readonly IBackendConfigurationLocalizationService _localizationService;
	private readonly BackendConfigurationPnDbContext _dbContext;
	private readonly BaseDbContext _baseDbContext;
	private readonly IUserService _userService;
	private readonly IFileArchiver _fileArchiver;
	private readonly IArchiveAccessService _archiveAccess;

	public BackendConfigurationFilesService(
		ILogger<BackendConfigurationTagsService> logger,
		IBackendConfigurationLocalizationService localizationService,
		BackendConfigurationPnDbContext dbContext,
		IUserService userService, IFileArchiver fileArchiver, BaseDbContext baseDbContext,
		IArchiveAccessService archiveAccess)
	{
		_logger = logger;
		_localizationService = localizationService;
		_dbContext = dbContext;
		_userService = userService;
		_fileArchiver = fileArchiver;
		_baseDbContext = baseDbContext;
		_archiveAccess = archiveAccess;
	}

	public async Task<OperationDataResult<Paged<BackendConfigurationFilesModel>>> Index(
		BackendConfigurationFileRequestModel request)
	{
		var ctx = await _archiveAccess.GetEnabledContextAsync();
		try
		{
			var (folders, defaultFolderId) = await LoadFoldersAsync();
			var query = _archiveAccess.Readable(_dbContext.Files.Where(x => x.WorkflowState != Removed), ctx)
				.Include(x => x.FileTags)
				.ThenInclude(x => x.FileTag)
				.Include(x => x.PropertyFiles)
				.AsQueryable();
			// filtration
			if (!string.IsNullOrEmpty(request.NameFilter))
			{
				query = query.Where(x =>
					x.FileName.Contains(request.NameFilter) || x.UploadedData.Extension.Contains(request.NameFilter));
			}

			if (request.DateFrom.HasValue && request.DateTo.HasValue)
			{
				// if dates is equal need set date to end this date(23h59m59s)
				if (request.DateFrom == request.DateTo)
				{
					request.DateTo = new DateTime(request.DateTo.Value.Year, request.DateTo.Value.Month,
						request.DateTo.Value.Day, 23, 59, 59);
				}

				query = query
					.Where(x => request.DateFrom <= x.CreatedAt)
					.Where(x => request.DateTo >= x.CreatedAt);
			}

			if (request.PropertyIds.Count > 0)
			{
				query = query.Where(x => x.PropertyFiles
					.Where(y => y.WorkflowState != Removed)
					.Where(y => y.FileId == x.Id)
					.Select(y => y.PropertyId)
					.Any(y => request.PropertyIds.Contains(y)));
			}

			if (request.TagIds.Count > 0)
			{
				foreach (var tagId in request.TagIds)
				{
					query = query.Where(x =>
							x.FileTags.Any(y => y.FileTagId == tagId && y.WorkflowState != Removed))
						.AsNoTracking();
				}
			}

			if (request.FolderId is { } folderId)
			{
				var isDefaultFolder = defaultFolderId > 0 && folderId == defaultFolderId;
				query = query.Where(x => x.FolderId == folderId || (isDefaultFolder && x.FolderId == null));
			}

			if (request.DateState is { } dateState)
			{
				query = query.Where(x => x.DocumentDateState == dateState);
			}

			// sort
			query = QueryHelper.AddSortToQuery(query, request.Sort, request.IsSortDsc);

			// total
			var total = await query.Select(x => x.Id).CountAsync();

			// select
			var files = await query.Select(x => new BackendConfigurationFilesModel
			{
				CreateDate = x.CreatedAt,
				FileName = x.FileName,
				FileExtension = x.UploadedData.Extension,
				FileHash = x.UploadedData.Checksum,
				Id = x.Id,
				Properties = x.PropertyFiles
					.Where(y => y.WorkflowState != Removed)
					.Where(y => y.FileId == x.Id)
					.Select(y => y.Property).Select(y => y.Name)
					.ToList(),
				Tags = x.FileTags
					.Where(y => y.WorkflowState != Removed)
					.Select(tag => new CommonTagModel
					{
						Id = tag.FileTagId,
						Name = tag.FileTag.Name
					}).ToList(),
				FolderId = x.FolderId ?? defaultFolderId,
				DocumentDate = x.DocumentDate,
				DocumentDateState = x.DocumentDateState,
				OwnAccess = x.OwnAccess
			}).ToListAsync();
			await FillArchiveFieldsAsync(files, folders);

			var pagedFilesModel = new Paged<BackendConfigurationFilesModel>
			{
				Entities = files,
				Total = total
			};

			if (request.Model != null)
			{
				var currentUserAsync = await _userService.GetCurrentUserAsync();
				var currentUser = _baseDbContext.Users
					.Single(x => x.Id == currentUserAsync.Id);
				currentUser.ArchiveModel = request.Model;
				currentUser.ArchiveManufacturer = request.Manufacturer;
				currentUser.ArchiveSoftwareVersion = request.SoftwareVersion;
				currentUser.ArchiveOsVersion = request.OsVersion;
				await _baseDbContext.SaveChangesAsync();
			}

			return new OperationDataResult<Paged<BackendConfigurationFilesModel>>(true, pagedFilesModel);
		}
		catch (Exception e) when (ArchiveExceptions.IsUnexpected(e))
		{
			Console.WriteLine(e);
			_logger.LogError(e, e.Message);
			return new OperationDataResult<Paged<BackendConfigurationFilesModel>>(false,
				_localizationService.GetString("ErrorWhileObtainingFiles"));
		}
	}

	public async Task<OperationResult> UpdateName(BackendConfigurationFileUpdateFilenameModel model)
	{
		await EnsureReadableAsync([model.Id]);
		try
		{
			var file = await _dbContext.Files
				.Where(x => x.WorkflowState != Removed)
				.Where(x => x.Id == model.Id)
				.Include(x => x.PropertyFiles)
				.FirstAsync();

			file.FileName = model.NewName;
			file.UpdatedByUserId = _userService.UserId;
			await file.Update(_dbContext);

			var propertiesForDeleteFromFile =
				file.PropertyFiles.Where(y => y.WorkflowState != Removed)
					.Where(x => !model.PropertyIds.Contains(x.PropertyId)).ToList();
			var propertiesForAddToFile =
				model.PropertyIds.Where(x =>
					!file.PropertyFiles.Where(y => y.WorkflowState != Removed)
						.Select(y => y.PropertyId).Contains(x)).ToList();

			foreach (var propertyFile in propertiesForDeleteFromFile)
			{
				propertyFile.UpdatedByUserId = _userService.UserId;
				await propertyFile.Delete(_dbContext);
			}

			foreach (var propertyFile in propertiesForAddToFile.Select(x => new PropertyFile
			         {
				         FileId = model.Id,
				         PropertyId = x,
				         CreatedByUserId = _userService.UserId,
				         UpdatedByUserId = _userService.UserId
			         }))
			{
				await propertyFile.Create(_dbContext);
			}

			return new OperationResult(true);
		}
		catch (Exception e) when (ArchiveExceptions.IsUnexpected(e))
		{
			Console.WriteLine(e);
			_logger.LogError(e, e.Message);
			return new OperationResult(false, _localizationService.GetString("ErrorWhileUpdateFile"));
		}
	}

	public async Task<OperationResult> UpdateTags(BackendConfigurationFileUpdateFileTags model)
	{
		await EnsureReadableAsync([model.Id]);
		try
		{
			var fileTags = await _dbContext.FilesTags
				.Where(x => x.WorkflowState != Removed)
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
					UpdatedByUserId = _userService.UserId
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
		catch (Exception e) when (ArchiveExceptions.IsUnexpected(e))
		{
			Console.WriteLine(e);
			_logger.LogError(e, e.Message);
			return new OperationResult(false, _localizationService.GetString("ErrorWhileUpdateFileTags"));
		}
	}

	public async Task<OperationResult> UpdateProperties(BackendConfigurationFileUpdateProperties model)
	{
		await EnsureReadableAsync([model.Id]);
		var fileProperties = await _dbContext.PropertyFiles
			.Where(x => x.WorkflowState != Removed)
			.Where(x => x.FileId == model.Id)
			.ToListAsync();

		var propertiesForDelete = fileProperties
			.Where(x => !model.Properties.Contains(x.PropertyId)).ToList();

		var propertiesForCreate = model.Properties
			.Where(x => !fileProperties.Select(y => y.PropertyId).Contains(x))
			.Select(propertyForCreateId => new PropertyFile
			{
				FileId = model.Id,
				PropertyId = propertyForCreateId,
				CreatedByUserId = _userService.UserId,
				UpdatedByUserId = _userService.UserId
			})
			.ToList();

		foreach (var newProperty in propertiesForCreate)
		{
			await newProperty.Create(_dbContext);
		}

		foreach (var property in propertiesForDelete)
		{
			property.UpdatedByUserId = _userService.UserId;
			await property.Delete(_dbContext);
		}

		return new OperationResult(true);
	}

	public async Task<OperationResult> Create(List<BackendConfigurationFileCreate> model)
	{
		var ctx = await _archiveAccess.GetEnabledContextAsync();
		try
		{
			var defaultFolderId = await ArchiveDefaultFolder.GetOrCreateIdAsync(_dbContext, _userService.UserId);
			var folders = await ArchiveFolderSnapshot.LoadAsync(_dbContext);
			var uploads = model.Where(x => x.File != null).ToList();
			// Older apps send no FolderId, or 0: the default folder. Any other id (negative included) must be a
			// folder the caller can write to.
			int FolderOf(BackendConfigurationFileCreate upload) =>
				upload.FolderId is null or 0 ? defaultFolderId : upload.FolderId.Value;

			// All folders first: one refused folder stores nothing (D17: only into folders the caller can read).
			if (uploads.Any(u => !ArchiveAccessRules.IsWritableFolder(ctx, folders, FolderOf(u))))
			{
				return FolderNotFound();
			}

			foreach (var fileCreate in uploads)
			{
				var fileExtension = Path.GetExtension(fileCreate.File.FileName).TrimStart('.').ToLower();
				var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileCreate.File.FileName);
				var documentDate = fileCreate.DocumentDate?.Date;
				var folderId = FolderOf(fileCreate);
				await using var stream = fileCreate.File.OpenReadStream();
				// The archiver re-checks the folder under its lock (the check above is only the early refusal).
				await _fileArchiver.ArchiveAsync(stream, fileNameWithoutExtension, fileExtension,
					fileCreate.PropertyIds, fileCreate.TagIds, folderId, documentDate,
					documentDate == null ? DocumentDateState.None : DocumentDateState.Approved,
					_userService.UserId,
					folderAllowed: live => ArchiveAccessRules.IsWritableFolder(ctx, live, folderId));
			}

			return new OperationResult(true);
		}
		catch (ArchiveFolderUnavailableException)
		{
			// Deleted or restricted after the check above. Uploads of this request committed before it stay.
			return FolderNotFound();
		}
		catch (Exception e) when (ArchiveExceptions.IsUnexpected(e))
		{
			Console.WriteLine(e);
			_logger.LogError(e, e.Message);
			return new OperationResult(false, _localizationService.GetString("ErrorWhileCreateFiles"));
		}
	}

	private OperationResult FolderNotFound() =>
		new(false, _localizationService.GetString("ArchiveFolderNotFound"));

	public async Task<OperationResult> Delete(int id)
	{
		await EnsureReadableAsync([id]);
		try
		{
			var file = await _dbContext.Files
				.Where(x => x.WorkflowState != Removed)
				.Where(x => x.Id == id)
				.Include(x => x.FileTags)
				.Include(x => x.UploadedData)
				.FirstAsync();
			// todo add delete file from storage

			// delete link to property
			var propertyFiles = await _dbContext.PropertyFiles
				.Where(x => x.WorkflowState != Removed)
				.Where(x => x.FileId == id)
				.ToListAsync();
			foreach (var propertyFile in propertyFiles)
			{
				propertyFile.UpdatedByUserId = _userService.UserId;
				await propertyFile.Delete(_dbContext);
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
		catch (Exception e) when (ArchiveExceptions.IsUnexpected(e))
		{
			Console.WriteLine(e);
			_logger.LogError(e, e.Message);
			return new OperationResult(false, _localizationService.GetString("ErrorWhileDeleteFile"));
		}
	}

	public async Task<OperationDataResult<BackendConfigurationFileModel>> GetById(int id)
	{
		await EnsureReadableAsync([id]);
		try
		{
			var (folders, defaultFolderId) = await LoadFoldersAsync();
			var file = await _dbContext.Files
				.Where(x => x.WorkflowState != Removed)
				.Where(x => x.Id == id)
				.Select(x => new BackendConfigurationFileModel
				{
					Id = x.Id,
					CreateDate = x.CreatedAt,
					FileName = x.FileName,
					FileExtension = x.UploadedData.Extension,
					FileHash = x.UploadedData.Checksum,
					Properties = x.PropertyFiles
						.Where(y => y.FileId == x.Id)
						.Where(y => y.WorkflowState != Removed)
						.Select(y => y.PropertyId)
						.ToList(),
					Tags = x.FileTags
						.Where(y => y.WorkflowState != Removed)
						.Select(tag => new CommonTagModel
						{
							Id = tag.FileTagId,
							Name = tag.FileTag.Name
						}).ToList(),
					FolderId = x.FolderId ?? defaultFolderId,
					DocumentDate = x.DocumentDate,
					DocumentDateState = x.DocumentDateState,
					OwnAccess = x.OwnAccess
				})
				.FirstAsync();
			await FillArchiveFieldsAsync([file], folders);
			return new OperationDataResult<BackendConfigurationFileModel>(true, file);
		}
		catch (Exception e) when (ArchiveExceptions.IsUnexpected(e))
		{
			Console.WriteLine(e);
			_logger.LogError(e, e.Message);
			return new OperationDataResult<BackendConfigurationFileModel>(false,
				_localizationService.GetString("ErrorWhileGetFile"));
		}
	}

	public async Task<OperationDataResult<FilesMoveResultModel>> Move(FilesMoveModel model)
	{
		var ids = DistinctIds(model.FileIds);
		if (ids.Count == 0)
		{
			return new OperationDataResult<FilesMoveResultModel>(true, new FilesMoveResultModel());
		}

		var ctx = await EnsureReadableAsync(ids);
		// Early refusal: an unknown target id would otherwise hold a gap lock on ArchiveFolders for the whole
		// transaction. The authoritative check is repeated inside, under the lock.
		if (!ArchiveAccessRules.IsWritableFolder(ctx, await ArchiveFolderSnapshot.LoadAsync(_dbContext), model.FolderId))
		{
			throw new ArchiveNotFoundException();
		}

		// The source folders as read now, to lock them; a document moved elsewhere meanwhile is re-read below.
		var sourceFolderIds = (await ArchiveAccessRules.LoadFileRowsAsync(_dbContext, ids)).Select(r => (int?)r.FolderId);
		// All or nothing, as the D17 refusal promises: a partial move must never widen access. Every decision is made
		// inside, under the locks, on freshly loaded rows: target folder, readability, D17 and the D11 count.
		var moved = await ArchiveTransaction.RunAsync(_dbContext, async () =>
		{
			await ArchiveFolderLock.SharedAsync(_dbContext, sourceFolderIds.Append(model.FolderId));
			await ArchiveFolderLock.DocumentsExclusiveAsync(_dbContext, ids);
			var folders = await ArchiveFolderSnapshot.LoadAsync(_dbContext);
			// Resolved rows: a NULL FolderId already reads as the default folder.
			var rows = await ArchiveAccessRules.LoadFileRowsAsync(_dbContext, ids);
			if (rows.Count != ids.Count
			    || rows.Any(r => !ArchiveAccessRules.CanRead(ctx, r.Effective(folders)))
			    || !ArchiveAccessRules.IsWritableFolder(ctx, folders, model.FolderId))
			{
				throw new ArchiveNotFoundException();
			}

			var moving = rows.Where(r => r.FolderId != model.FolderId).ToList();
			var accessChanged = ArchiveAccessRules.CountAccessChanged(moving,
				r => r.Effective(folders), r => r.EffectiveIn(model.FolderId, folders));
			if (accessChanged > 0 && !ctx.IsAdmin)
			{
				// D17: changing who can read a document is an administrator's decision. Nothing is written.
				return null;
			}

			var movingIds = moving.Select(r => r.FileId).ToList();
			var files = await _dbContext.Files.Where(f => movingIds.Contains(f.Id)).ToListAsync();
			foreach (var file in files)
			{
				file.FolderId = model.FolderId;
				file.UpdatedByUserId = _userService.UserId;
				await file.Update(_dbContext);
			}

			return new FilesMoveResultModel { Moved = movingIds.Count, AccessChanged = accessChanged };
		});

		return moved == null
			? new OperationDataResult<FilesMoveResultModel>(false,
				_localizationService.GetString("ArchiveMoveChangesAccess"))
			: new OperationDataResult<FilesMoveResultModel>(true, moved);
	}

	public async Task<OperationResult> UpdateDate(int id, FileDateModel model)
	{
		await EnsureReadableAsync([id]);
		var file = await _dbContext.Files.SingleOrDefaultAsync(f => f.Id == id && f.WorkflowState != Removed)
		           ?? throw new ArchiveNotFoundException();
		file.DocumentDate = model.Date?.Date;
		file.DocumentDateState = model.Date == null ? DocumentDateState.None : DocumentDateState.Approved;
		file.UpdatedByUserId = _userService.UserId;
		await file.Update(_dbContext);
		return new OperationResult(true);
	}

	public async Task<OperationDataResult<AccessChangeModel>> UpdateAccess(FilesAccessModel model)
	{
		var ids = DistinctIds(model.FileIds);
		if (ids.Count == 0)
		{
			return new OperationDataResult<AccessChangeModel>(true, new AccessChangeModel());
		}

		var ctx = await EnsureReadableAsync(ids);
		if (!ctx.IsAdmin)
		{
			// The controller is admin-only; this keeps any other caller to the same rule.
			throw new ArchiveNotFoundException();
		}

		var tagIds = model.OwnAccess ? DistinctIds(model.TagIds) : [];
		// One transaction: own access saved with the old groups deleted but the new ones missing would read as open.
		// The documents are locked first, so the group rule and the D11 count rest on their rows as they are now.
		var accessChanged = await ArchiveTransaction.RunAsync(_dbContext, async () =>
		{
			await ArchiveFolderLock.DocumentsExclusiveAsync(_dbContext, ids);
			var before = await ArchiveAccessRules.LoadFileRowsAsync(_dbContext, ids);
			if (before.Count != ids.Count)
			{
				throw new ArchiveNotFoundException();
			}

			// Only groups new to some document need live members (the folder rule, per document).
			var groupsOnEveryDocument = before
				.Select(r => r.OwnAccess ? r.OwnGroupIds : [])
				.Aggregate<IEnumerable<int>>((a, b) => a.Intersect(b))
				.ToList();
			if (!await _archiveAccess.AreAssignableGroupsAsync(tagIds, groupsOnEveryDocument))
			{
				return (int?)null;
			}

			var files = await _dbContext.Files.Where(f => ids.Contains(f.Id)).ToListAsync();
			foreach (var file in files)
			{
				file.OwnAccess = model.OwnAccess;
				file.UpdatedByUserId = _userService.UserId;
				await file.Update(_dbContext);
				await ArchiveReadTagSync.SyncFileAsync(_dbContext, file.Id, tagIds, _userService.UserId);
			}

			var folders = await ArchiveFolderSnapshot.LoadAsync(_dbContext);
			var beforeById = before.ToDictionary(r => r.FileId);
			var after = await ArchiveAccessRules.LoadFileRowsAsync(_dbContext, ids);
			return ArchiveAccessRules.CountAccessChanged(after,
				r => beforeById[r.FileId].Effective(folders), r => r.Effective(folders));
		});

		return accessChanged is { } changed
			? new OperationDataResult<AccessChangeModel>(true, new AccessChangeModel { AccessChanged = changed })
			: new OperationDataResult<AccessChangeModel>(false, _localizationService.GetString("ArchiveUnknownGroup"));
	}

	/// <summary>The stored file of a readable document; null for a missing or unreadable one (the controller answers 404).</summary>
	public async Task<UploadedData> GetUploadedDataByFileId(int id)
	{
		var ctx = await _archiveAccess.GetEnabledContextAsync();
		var readable = _archiveAccess.Readable(_dbContext.Files.Where(f => f.Id == id && f.WorkflowState != Removed), ctx)
			.Select(f => f.Id);
		return await _dbContext.UploadedDatas
			.Where(x => x.WorkflowState != Removed)
			.Where(x => readable.Contains(x.FileId))
			.FirstOrDefaultAsync();
	}

	public async Task<Dictionary<int, ArchiveFileDownload>> GetDownloadsAsync(IReadOnlyCollection<int> fileIds)
	{
		var ctx = await _archiveAccess.GetEnabledContextAsync();
		var ids = fileIds.Distinct().ToList();
		var downloads = await _archiveAccess
			.Readable(_dbContext.Files.AsNoTracking().Where(f => ids.Contains(f.Id) && f.WorkflowState != Removed), ctx)
			.Where(f => f.UploadedData != null && f.UploadedData.WorkflowState != Removed)
			.Select(f => new { f.Id, f.FileName, f.UploadedData })
			.ToDictionaryAsync(f => f.Id, f => new ArchiveFileDownload(f.FileName, f.UploadedData));
		return ids.Count > 0 && downloads.Count == ids.Count ? downloads : null;
	}

	/// <summary>The live folders and the default folder's id (0 when none), where a NULL FolderId lies (pre-10.0.59 rows).</summary>
	private async Task<(ArchiveFolderSnapshot Folders, int DefaultFolderId)> LoadFoldersAsync()
	{
		var folders = await ArchiveFolderSnapshot.LoadAsync(_dbContext);
		return (folders, folders.DefaultFolderId ?? 0);
	}

	private static List<int> DistinctIds(IEnumerable<int> ids) => (ids ?? []).Distinct().ToList();

	/// <summary>
	/// Every id is a live document the caller can read, else <see cref="ArchiveNotFoundException"/> (the same for
	/// missing and unreadable, so a restricted document's existence does not leak). Returns the caller's context.
	/// </summary>
	private async Task<ArchiveAccessContext> EnsureReadableAsync(IReadOnlyCollection<int> fileIds)
	{
		var ctx = await _archiveAccess.GetEnabledContextAsync();
		var ids = fileIds.Distinct().ToList();
		if (ids.Count == 0 || await _archiveAccess
			    .Readable(_dbContext.Files.Where(f => ids.Contains(f.Id) && f.WorkflowState != Removed), ctx)
			    .CountAsync() != ids.Count)
		{
			throw new ArchiveNotFoundException();
		}

		return ctx;
	}

	/// <summary>Sets FolderPath and the effective ReadGroups (with names) on the rows.</summary>
	private async Task FillArchiveFieldsAsync(IReadOnlyCollection<IArchiveFileRow> rows, ArchiveFolderSnapshot folders)
	{
		if (rows.Count == 0)
		{
			return;
		}

		var access = (await ArchiveAccessRules.LoadFileRowsAsync(_dbContext, rows.Select(r => r.Id).ToList()))
			.ToDictionary(r => r.FileId);
		var effective = rows.ToDictionary(r => r.Id,
			r => access.TryGetValue(r.Id, out var a) ? a.Effective(folders) : []);
		var names = await _archiveAccess.GetGroupNamesAsync(effective.Values.SelectMany(g => g).Distinct().ToList());
		foreach (var row in rows)
		{
			row.FolderPath = folders.Path(row.FolderId).ToList();
			row.ReadGroups = effective[row.Id]
				.Select(id => new CommonTagModel { Id = id, Name = names.GetValueOrDefault(id, "") })
				.ToList();
		}
	}
}
