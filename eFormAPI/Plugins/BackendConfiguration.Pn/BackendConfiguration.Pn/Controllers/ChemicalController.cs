using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Chemical;
using BackendConfiguration.Pn.Services.ChemicalService;
using Chemicals.Pn.Infrastructure.Models.Chemical;
using Chemicals.Pn.Infrastructure.Models.Planning;
using ChemicalsBase.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.eFormApi.BasePn.Infrastructure.Models.Common;

namespace BackendConfiguration.Pn.Controllers;

[Authorize]
public class ChemicalController : Controller
{
    private readonly IChemicalService _chemicalService;
    private readonly ChemicalsDbContext _dbContext;

    public ChemicalController(IChemicalService chemicalService, ChemicalsDbContext dbContext)
    {
        _chemicalService = chemicalService;
        _dbContext = dbContext;
    }
    
    [HttpPost]
    [Route("api/chemicals-pn/chemicals/index")]
    public async Task<OperationDataResult<Paged<ChemicalPnModel>>> Index([FromBody] ChemicalsRequestModel requestModel)
    {
        return await _chemicalService.Index(requestModel).ConfigureAwait(false);
    }

}