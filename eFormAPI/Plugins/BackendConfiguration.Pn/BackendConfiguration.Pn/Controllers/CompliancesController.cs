/*
The MIT License (MIT)

Copyright (c) 2007 - 2021 Microting A/S

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

using Microting.eFormApi.BasePn.Infrastructure.Models.Application.Case.CaseEdit;

namespace BackendConfiguration.Pn.Controllers
{
    using Infrastructure.Models.Compliances.Index;
    using Services.BackendConfigurationCompliancesService;
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Mvc;
    using Microting.eFormApi.BasePn.Infrastructure.Models.API;
    using Microting.eFormApi.BasePn.Infrastructure.Models.Common;
    using System.Threading.Tasks;

    [Authorize]
    [Route("api/backend-configuration-pn/compliances")]
    public class CompliancesController : Controller
    {
        private readonly IBackendConfigurationCompliancesService _backendConfigurationCompliancesService;

        public CompliancesController(IBackendConfigurationCompliancesService backendConfigurationCompliancesService)
        {
            _backendConfigurationCompliancesService = backendConfigurationCompliancesService;
        }

        [HttpPost]
        [Route("index")]
        public Task<OperationDataResult<Paged<CompliancesModel>>> Index([FromBody] CompliancesRequestModel request)
        {
            return _backendConfigurationCompliancesService.Index(request);
        }

        [HttpGet]
        [Route("compliance")]
        public Task<OperationDataResult<int>> Compliance(int propertyId)
        {
            return _backendConfigurationCompliancesService.ComplianceStatus(propertyId);
        }

        [HttpGet]
        // [Authorize(Policy = AuthConsts.EformPolicies.Cases.CaseRead)]
        [Route("cases")]
        public async Task<IActionResult> Read(int id, int templateId)
        {
            // if (! await _permissionsService.CheckEform(templateId,
                    // AuthConsts.EformClaims.CasesClaims.CaseRead))
            // {
                // return Forbid();
            // }

            return Ok(await _backendConfigurationCompliancesService.Read(id));
        }

        [HttpPut]
        [Route("cases")]
        // [Authorize(Policy = AuthConsts.EformPolicies.Cases.CaseUpdate)]
        public async Task<IActionResult> Update([FromBody] ReplyRequest model)
        {
            // if (!await _permissionsService.CheckEform(templateId,
            //         AuthConsts.EformClaims.CasesClaims.CaseUpdate))
            // {
            //     return Forbid();
            // }

            return Ok(await _backendConfigurationCompliancesService.Update(model));
        }
    }
}