#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.ArchiveFolders;
using BackendConfiguration.Pn.Services.ArchiveFolders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;

namespace BackendConfiguration.Pn.Controllers;

/// <summary>The archive folder tree. Everyone signed in reads it; only admins and Leders change it (D5, D-L3).</summary>
[Authorize]
[ArchiveAccessFilter]
[Route("api/backend-configuration-pn/archive-folders")]
public class ArchiveFoldersController(IArchiveFoldersService service) : Controller
{
    [HttpGet]
    public Task<OperationDataResult<List<ArchiveFolderModel>>> Index([FromQuery] List<int> propertyIds) =>
        service.IndexAsync(propertyIds);

    [HttpPost]
    [ArchiveManager]
    public Task<OperationDataResult<ArchiveFolderModel>> Create([FromBody] ArchiveFolderCreateModel model) =>
        service.CreateAsync(model);

    [HttpPut("{id:int}")]
    [ArchiveManager]
    public Task<OperationDataResult<AccessChangeModel>> Update(int id, [FromBody] ArchiveFolderUpdateModel model) =>
        service.UpdateAsync(id, model);

    [HttpDelete("{id:int}")]
    [ArchiveManager]
    public Task<OperationResult> Delete(int id) => service.DeleteAsync(id);

    [HttpPut("{id:int}/access")]
    [ArchiveManager]
    public Task<OperationDataResult<AccessChangeModel>> UpdateAccess(int id, [FromBody] ArchiveFolderAccessModel model) =>
        service.UpdateAccessAsync(id, model);
}
