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

using BackendConfiguration.Pn.Infrastructure.Models;

namespace BackendConfiguration.Pn.Controllers
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Infrastructure.Models.AssignmentWorker;
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Mvc;
    using Microting.eFormApi.BasePn.Infrastructure.Models.API;
    using Services.BackendConfigurationAssignmentWorkerService;

    [Authorize]
    [Route("api/backend-configuration-pn/properties/assignment")]
    public class AssignmentWorkerController : Controller
    {
        private readonly IBackendConfigurationAssignmentWorkerService _backendConfigurationAssignmentWorkerService;

        public AssignmentWorkerController(IBackendConfigurationAssignmentWorkerService backendConfigurationAssignmentWorkerService)
        {
            _backendConfigurationAssignmentWorkerService = backendConfigurationAssignmentWorkerService;
        }

        [HttpGet]
        public Task<OperationDataResult<List<PropertyAssignWorkersModel>>> GetPropertiesAssignment()
        {
            return _backendConfigurationAssignmentWorkerService.GetPropertiesAssignment();
        }

        [HttpPost]
        public Task<OperationResult> Create([FromBody] PropertyAssignWorkersModel createModel)
        {
            return _backendConfigurationAssignmentWorkerService.Create(createModel);
        }

        [HttpPut]
        public Task<OperationResult> Update([FromBody] PropertyAssignWorkersModel updateModel)
        {
            return _backendConfigurationAssignmentWorkerService.Update(updateModel);
        }

        [HttpDelete]
        public Task<OperationResult> Delete(int deviceUserId)
        {
            return _backendConfigurationAssignmentWorkerService.Delete(deviceUserId);
        }

        [HttpPost]
        [Route("update-device-user")]
        public async Task<OperationResult> UpdateDeviceUser([FromBody] DeviceUserModel deviceUserModel)
        {
            return await _backendConfigurationAssignmentWorkerService.UpdateDeviceUser(deviceUserModel);
        }
    }
}