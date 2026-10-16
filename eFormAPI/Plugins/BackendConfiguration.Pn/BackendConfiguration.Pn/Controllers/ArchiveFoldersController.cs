#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.ArchiveFolders;
using BackendConfiguration.Pn.Services.ArchiveFolders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;

namespace BackendConfiguration.Pn.Controllers;

/// <summary>The archive folder tree. Everyone signed in reads it; only administrators change it (D5).</summary>
[Authorize]
[ArchiveAccessFilter]
[Route("api/backend-configuration-pn/archive-folders")]
public class ArchiveFoldersController(IArchiveFoldersService service) : Controller
{
    [HttpGet]
    public Task<OperationDataResult<List<ArchiveFolderModel>>> Index([FromQuery] List<int> propertyIds) =>
        service.IndexAsync(propertyIds);

    [HttpPost]
    [Authorize(Roles = EformRole.Admin)]
    public Task<OperationDataResult<ArchiveFolderModel>> Create([FromBody] ArchiveFolderCreateModel model) =>
        service.CreateAsync(model);
}
