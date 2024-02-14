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

namespace BackendConfiguration.Pn.Controllers;

using Infrastructure.Helpers;
using Infrastructure.Models.Properties;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.eFormApi.BasePn.Infrastructure.Models.Common;
using Services.BackendConfigurationPropertiesService;
using System.Collections.Generic;
using System.Threading.Tasks;

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
    public Task<OperationDataResult<Paged<PropertiesModel>>> Index([FromBody]PropertiesRequestModel request)
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
    public Task<OperationDataResult<List<CommonDictionaryModel>>> GetCommonDictionary([FromQuery] bool fullNames)
    {
        return _backendConfigurationPropertiesService.GetCommonDictionary(fullNames);
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

    [HttpGet]
    [Route("get-folder-dtos")]
    public async Task<OperationDataResult<List<PropertyFolderModel>>> GetFolderDtos(int propertyId)
    {
        return await _backendConfigurationPropertiesService.GetLinkedFolderDtos(propertyId);
    }

    [HttpPost]
    [Route("get-folder-dtos")]
    public async Task<OperationDataResult<List<PropertyFolderModel>>> GetFolderDtos([FromBody] List<int> propertyIds)
    {
        return await _backendConfigurationPropertiesService.GetLinkedFolderDtos(propertyIds);
    }

    [HttpGet]
    [Route("get-folder-list")]
    public async Task<OperationDataResult<List<CommonDictionaryModel>>> GetFolderList(int propertyId)
    {
        return await _backendConfigurationPropertiesService.GetLinkedFoldersList(propertyId);
    }

    [HttpPost]
    [Route("get-folder-list")]
    public async Task<OperationDataResult<List<CommonDictionaryModel>>> GetFolderList([FromBody] List<int> propertyIds)
    {
        return await _backendConfigurationPropertiesService.GetLinkedFoldersList(propertyIds);
    }

    [HttpGet]
    [Route("get-linked-sites")]
    public async Task<OperationDataResult<List<CommonDictionaryModel>>> GetLinkedSites(int propertyId)
    {
        return await _backendConfigurationPropertiesService.GetLinkedSites(propertyId);
    }

    [HttpPost]
    [Route("get-linked-sites")]
    public async Task<OperationDataResult<List<CommonDictionaryModel>>> GetLinkedSites([FromBody] List<int> propertyIds)
    {
        return await _backendConfigurationPropertiesService.GetLinkedSites(propertyIds);
    }
}