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

using BackendConfiguration.Pn.Infrastructure.Helpers;

namespace BackendConfiguration.Pn.Controllers
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Infrastructure.Models.Properties;
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Mvc;
    using Microting.eFormApi.BasePn.Infrastructure.Models.API;
    using Microting.eFormApi.BasePn.Infrastructure.Models.Common;
    using Services.BackendConfigurationPropertiesService;

    [Authorize]
    [Route("api/backend-configuration-pn/properties")]
    public class PropertiesController : Controller
    {
        private readonly IBackendConfigurationPropertiesService _backendConfigurationPropertiesService;

        public PropertiesController(IBackendConfigurationPropertiesService backendConfigurationPropertiesService)
        {
            _backendConfigurationPropertiesService = backendConfigurationPropertiesService;
        }

        [HttpPost]
        [Route("index")]
        public Task<OperationDataResult<Paged<PropertiesModel>>> Index([FromBody]ProperiesRequesModel request)
        {
            return _backendConfigurationPropertiesService.Index(request);
        }

        [HttpPost]
        public Task<OperationResult> Create([FromBody] PropertyCreateModel createModel)
        {
            return _backendConfigurationPropertiesService.Create(createModel);
        }

        [HttpGet]
        public Task<OperationDataResult<PropertiesModel>> Read(int id)
        {
            return _backendConfigurationPropertiesService.Read(id);
        }

        [HttpPut]
        public Task<OperationResult> Update([FromBody] PropertiesUpdateModel updateModel)
        {
            return _backendConfigurationPropertiesService.Update(updateModel);
        }

        [HttpDelete]
        public Task<OperationResult> Delete(int propertyId)
        {
            return _backendConfigurationPropertiesService.Delete(propertyId);
        }

        [HttpGet]
        [Route("dictionary")]
        public Task<OperationDataResult<List<CommonDictionaryModel>>> GetCommonDictionary()
        {
            return _backendConfigurationPropertiesService.GetCommonDictionary();
        }

        [HttpGet]
        [AllowAnonymous]
        [Route("get-company-type")]
        public async Task<OperationDataResult<Result>> GetCompanyType(int cvrNumber)
        {
            return await _backendConfigurationPropertiesService.GetCompanyType(cvrNumber).ConfigureAwait(false);
        }

        [HttpGet]
        [AllowAnonymous]
        [Route("get-chr-information")]
        public async Task<OperationDataResult<ChrResult>> GetChrInformation(int cvrNumber)
        {
            return await _backendConfigurationPropertiesService.GetChrInformation(cvrNumber).ConfigureAwait(false);
        }
    }
}