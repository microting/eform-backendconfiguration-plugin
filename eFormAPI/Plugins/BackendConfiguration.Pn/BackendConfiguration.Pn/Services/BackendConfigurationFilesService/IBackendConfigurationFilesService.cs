using System.Collections.Generic;

namespace BackendConfiguration.Pn.Services.BackendConfigurationFilesService;

using System.Threading.Tasks;
using Infrastructure.Models.Files;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.eFormApi.BasePn.Infrastructure.Models.Common;

public interface IBackendConfigurationFilesService
{
	Task<OperationDataResult<Paged<BackendConfigurationFileModel>>> Index(BackendConfigurationFileRequestModel request);

	Task<OperationResult> UpdateName(BackendConfigurationFileUpdateFilenameModel model);

	Task<OperationResult> UpdateTags(BackendConfigurationFileUpdateFileTags model);

	Task<OperationResult> Create(List<BackendConfigurationFileCreate> model);

	Task<OperationResult> Delete(int id);

	Task<OperationDataResult<BackendConfigurationFileModel>> GetById(int id);

	Task<string> GetUploadedDataByFileId(int id);
}