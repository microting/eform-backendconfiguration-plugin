using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Documents;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.eFormCaseTemplateBase.Infrastructure.Data;

namespace BackendConfiguration.Pn.Services.BackendConfigurationDocumentService;

public class BackendConfigurationDocumentService : IBackendConfigurationDocumentService
{
    private readonly CaseTemplatePnDbContext _caseTemplatePnDbContext;
    private readonly IEFormCoreService _coreHelper;
    private readonly IBackendConfigurationLocalizationService _backendConfigurationLocalizationService;
    private readonly IUserService _userService;
    private readonly BackendConfigurationPnDbContext _backendConfigurationPnDbContext;

    public BackendConfigurationDocumentService(CaseTemplatePnDbContext caseTemplatePnDbContext, IEFormCoreService coreHelper, IBackendConfigurationLocalizationService backendConfigurationLocalizationService, IUserService userService, BackendConfigurationPnDbContext backendConfigurationPnDbContext)
    {
        _caseTemplatePnDbContext = caseTemplatePnDbContext;
        _coreHelper = coreHelper;
        _backendConfigurationLocalizationService = backendConfigurationLocalizationService;
        _userService = userService;
        _backendConfigurationPnDbContext = backendConfigurationPnDbContext;
    }

    public Task<OperationDataResult<BackendConfigurationDocumentsModel>> Index(BackendConfigurationDocumentRequestModel pnRequestModel)
    {
        throw new System.NotImplementedException();
    }

    public Task<OperationDataResult<BackendConfigurationDocumentModel>> GetDocumentAsync(int id)
    {
        throw new System.NotImplementedException();
    }

    public Task<OperationResult> UpdateDocumentAsync(BackendConfigurationDocumentModel model)
    {
        throw new System.NotImplementedException();
    }

    public Task<OperationResult> CreateDocumentAsync(BackendConfigurationDocumentModel model)
    {
        throw new System.NotImplementedException();
    }

    public Task<OperationResult> DeleteDocumentAsync(int id)
    {
        throw new System.NotImplementedException();
    }

    public Task<OperationDataResult<BackendConfigurationDocumentFoldersModel>> GetFolders(BackendConfigurationDocumentFolderRequestModel pnRequestModel)
    {
        throw new System.NotImplementedException();
    }

    public Task<OperationResult> CreateFolder(BackendConfigurationDocumentFolderModel model)
    {
        throw new System.NotImplementedException();
    }

    public Task<OperationResult> UpdateFolder(BackendConfigurationDocumentFolderModel model)
    {
        throw new System.NotImplementedException();
    }

    public Task<OperationResult> DeleteFolder(int id)
    {
        throw new System.NotImplementedException();
    }
}