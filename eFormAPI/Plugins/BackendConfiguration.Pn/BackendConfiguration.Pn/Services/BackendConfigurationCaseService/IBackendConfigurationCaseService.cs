using System.Threading.Tasks;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.eFormApi.BasePn.Infrastructure.Models.Application.Case.CaseEdit;

namespace BackendConfiguration.Pn.Services.BackendConfigurationCaseService;

public interface IBackendConfigurationCaseService
{
    Task<OperationResult> Update(ReplyRequest model);
}