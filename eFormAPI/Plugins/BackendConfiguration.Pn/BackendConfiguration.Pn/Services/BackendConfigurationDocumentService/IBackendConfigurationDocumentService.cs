using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Documents;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;

namespace BackendConfiguration.Pn.Services.BackendConfigurationDocumentService;

public interface IBackendConfigurationDocumentService
{
    Task<OperationDataResult<BackendConfigurationDocumentsModel>> Index(BackendConfigurationDocumentRequestModel pnRequestModel);

    Task<OperationDataResult<BackendConfigurationDocumentModel>> GetDocumentAsync(int id);

    Task<OperationResult> UpdateDocumentAsync(BackendConfigurationDocumentModel model);

    Task<OperationResult> CreateDocumentAsync(BackendConfigurationDocumentModel model);

    Task<OperationResult> DeleteDocumentAsync(int id);

    Task<OperationDataResult<BackendConfigurationDocumentFoldersModel>> GetFolders(BackendConfigurationDocumentFolderRequestModel pnRequestModel);

    Task<OperationResult> CreateFolder(BackendConfigurationDocumentFolderModel model);

    Task<OperationResult> UpdateFolder(BackendConfigurationDocumentFolderModel model);

    Task<OperationResult> DeleteFolder(int id);
}