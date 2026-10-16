using System.Collections.Generic;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;

namespace BackendConfiguration.Pn.Services.BackendConfigurationFilesService;

using System.Threading.Tasks;
using Infrastructure.Models.ArchiveFolders;
using Infrastructure.Models.Files;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.eFormApi.BasePn.Infrastructure.Models.Common;

public interface IBackendConfigurationFilesService
{
	Task<OperationDataResult<Paged<BackendConfigurationFilesModel>>> Index(BackendConfigurationFileRequestModel request);

	Task<OperationResult> UpdateName(BackendConfigurationFileUpdateFilenameModel model);

	Task<OperationResult> UpdateTags(BackendConfigurationFileUpdateFileTags model);

	Task<OperationResult> UpdateProperties(BackendConfigurationFileUpdateProperties model);

	Task<OperationResult> Create(List<BackendConfigurationFileCreate> model);

	Task<OperationResult> Delete(int id);

	Task<OperationDataResult<BackendConfigurationFileModel>> GetById(int id);

	Task<UploadedData> GetUploadedDataByFileId(int id);

	Task<OperationDataResult<FilesMoveResultModel>> Move(FilesMoveModel model);

	/// <summary>Sets and approves the document date (date only), or clears it.</summary>
	Task<OperationResult> UpdateDate(int id, FileDateModel model);

	/// <summary>Own read access per document; OwnAccess false returns the documents to their folder's access.</summary>
	Task<OperationDataResult<AccessChangeModel>> UpdateAccess(FilesAccessModel model);

	/// <summary>
	/// Name and stored file of every id, in one query; null when any id is missing or unreadable (the zip is all or
	/// nothing).
	/// </summary>
	Task<Dictionary<int, ArchiveFileDownload>> GetDownloadsAsync(IReadOnlyCollection<int> fileIds);
}