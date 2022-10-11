using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.BackendConfigurationCaseService;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microting.eFormApi.BasePn.Infrastructure.Models.Application.Case.CaseEdit;

namespace BackendConfiguration.Pn.Controllers;

[Authorize]
[Route("api/backend-configuration-pn")]
public class BackendConfigurationCaseController : Controller
{
    private readonly IBackendConfigurationCaseService _backendConfigurationCaseService;

    public BackendConfigurationCaseController(IBackendConfigurationCaseService backendConfigurationCaseService)
    {
        _backendConfigurationCaseService = backendConfigurationCaseService;
    }
    [HttpPut]
    [Route("cases")]
    public async Task<IActionResult> Update([FromBody] ReplyRequest model)
    {
        return Ok(await _backendConfigurationCaseService.Update(model));
    }
}