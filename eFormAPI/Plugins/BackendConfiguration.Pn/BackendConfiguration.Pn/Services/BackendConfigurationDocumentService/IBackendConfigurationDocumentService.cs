using System.Collections.Generic;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Documents;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.eFormApi.BasePn.Infrastructure.Models.Common;

namespace BackendConfiguration.Pn.Services.BackendConfigurationDocumentService;

public interface IBackendConfigurationDocumentService
{
    Task<OperationDataResult<Paged<BackendConfigurationDocumentModel>>> Index(BackendConfigurationDocumentRequestModel pnRequestModel);

    Task<OperationDataResult<BackendConfigurationDocumentModel>> GetDocumentAsync(int id);

    Task<OperationResult> UpdateDocumentAsync(BackendConfigurationDocumentModel model);

    Task<OperationResult> CreateDocumentAsync(BackendConfigurationDocumentModel model);

    Task<OperationResult> DeleteDocumentAsync(int id);

    Task<OperationDataResult<Paged<BackendConfigurationDocumentFolderModel>>> GetFolders(BackendConfigurationDocumentFolderRequestModel pnRequestModel);

    Task<OperationDataResult<List<BackendConfigurationDocumentSimpleFolderModel>>> GetFolders(int languageId);

    Task<OperationDataResult<BackendConfigurationDocumentFolderModel>> GetFolderAsync(int id);

    Task<OperationResult> CreateFolder(BackendConfigurationDocumentFolderModel model);

    Task<OperationResult> UpdateFolder(BackendConfigurationDocumentFolderModel model);

    Task<OperationResult> DeleteFolder(int id);
}