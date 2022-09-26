using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Helpers;
using BackendConfiguration.Pn.Infrastructure.Models.Documents;
using BackendConfiguration.Pn.Infrastructure.Models.Properties;
using BackendConfiguration.Pn.Services.BackendConfigurationDocumentService;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.eFormApi.BasePn.Infrastructure.Models.Common;

namespace BackendConfiguration.Pn.Controllers;

[Authorize]
[Route("api/backend-configuration-pn/documents")]
public class DocumentController : Controller
{
    private readonly IBackendConfigurationDocumentService _backendConfigurationDocumentService;

    public DocumentController(IBackendConfigurationDocumentService backendConfigurationDocumentService)
    {
        _backendConfigurationDocumentService = backendConfigurationDocumentService;
    }

    [HttpPost]
    public async Task<OperationDataResult<Paged<BackendConfigurationDocumentModel>>> Index([FromBody]BackendConfigurationDocumentRequestModel request)
    {
        return await _backendConfigurationDocumentService.Index(request);
    }

    [HttpPost]
    [Route("create")]
    public async Task<OperationResult> Create([FromForm] BackendConfigurationDocumentModel model)
    {
        // set files with help reflection. for some unknown reason, the field with the file in a deeply nested object is not set,
        // unlike the adjacent fields. if you know what it can be replaced,
        // or the reason why the files are not set and you know how to eliminate this reason,
        // then fix this **crutch**
        foreach (var formFile in HttpContext.Request.Form.Files)
        {
            // path to property(formFile.Name) can be 'Fields[0][PdfFiles][0][File]' or 'Fields[1]Fields[0][PdfFiles][0][File]'
            // or 'Checklists[1]Fields[0][PdfFiles][0][File]' or 'Checklists[1]Fields[1]Fields[0][PdfFiles][0][File]' or a **deeper nesting**
            ReflectionSetProperty.SetProperty(model, formFile.Name.Replace("][", ".").Replace("[", ".").Replace("]", ""), formFile);
        }
        return await _backendConfigurationDocumentService.CreateDocumentAsync(model);
    }

    [HttpGet]
    [Route("{id}")]
    public async Task<OperationDataResult<BackendConfigurationDocumentModel>> Read(int id)
    {
        return await _backendConfigurationDocumentService.GetDocumentAsync(id);
    }

    [HttpPut]
    [Route("update/{id}")]
    public async Task<OperationResult> Update([FromForm] BackendConfigurationDocumentModel model)
    {
        foreach (var formFile in HttpContext.Request.Form.Files)
        {
            // path to property(formFile.Name) can be 'Fields[0][PdfFiles][0][File]' or 'Fields[1]Fields[0][PdfFiles][0][File]'
            // or 'Checklists[1]Fields[0][PdfFiles][0][File]' or 'Checklists[1]Fields[1]Fields[0][PdfFiles][0][File]' or a **deeper nesting**
            ReflectionSetProperty.SetProperty(model, formFile.Name.Replace("][", ".").Replace("[", ".").Replace("]", ""), formFile);
        }
        return await _backendConfigurationDocumentService.UpdateDocumentAsync(model);
    }

    [HttpDelete]
    [Route("delete/{id}")]
    public async Task<OperationResult> Delete(int id)
    {
        return await _backendConfigurationDocumentService.DeleteDocumentAsync(id);
    }

    [HttpPost]
    [Route("folders")]
    public async Task<OperationDataResult<Paged<BackendConfigurationDocumentFolderModel>>> IndexFolder([FromBody]BackendConfigurationDocumentFolderRequestModel request)
    {
        return await _backendConfigurationDocumentService.GetFolders(request);
    }

    [HttpGet]
    [Route("folders/{id}")]
    public async Task<OperationDataResult<BackendConfigurationDocumentFolderModel>> ReadFolder(int id)
    {
        return await _backendConfigurationDocumentService.GetFolderAsync(id);
    }

    [HttpPost]
    [Route("folders/create")]
    public async Task<OperationResult> CreateFolder([FromBody] BackendConfigurationDocumentFolderModel createModel)
    {
        return await _backendConfigurationDocumentService.CreateFolder(createModel);
    }

    [HttpPut]
    [Route("folders/update/{id}")]
    public async Task<OperationResult> UpdateFolder([FromBody] BackendConfigurationDocumentFolderModel updateModel)
    {
        return await _backendConfigurationDocumentService.UpdateFolder(updateModel);
    }

    [HttpDelete]
    [Route("folders/delete/{id}")]
    public async Task<OperationResult> DeleteFolder(int id)
    {
        return await _backendConfigurationDocumentService.DeleteFolder(id);
    }


}