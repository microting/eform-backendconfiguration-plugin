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

namespace BackendConfiguration.Pn.Services.BackendConfigurationPropertiesService
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using BackendConfigurationLocalizationService;
    using Infrastructure.Models.PropertyAreas;
    using Microsoft.EntityFrameworkCore;
    using Microting.eForm.Infrastructure.Constants;
    using Microting.eFormApi.BasePn.Abstractions;
    using Microting.eFormApi.BasePn.Infrastructure.Helpers;
    using Microting.eFormApi.BasePn.Infrastructure.Models.API;
    using Microting.EformBackendConfigurationBase.Infrastructure.Data;
    using Services.BackendConfigurationPropertyAreasService;

    public class BackendConfigurationPropertyAreasService : IBackendConfigurationPropertyAreasService
    {
        //private readonly IEFormCoreService _coreHelper;
        private readonly IBackendConfigurationLocalizationService _backendConfigurationLocalizationService;
        private readonly IUserService _userService;
        private readonly BackendConfigurationPnDbContext _backendConfigurationPnDbContext;

        public BackendConfigurationPropertyAreasService(
            //IEFormCoreService coreHelper,
            IUserService userService,
            BackendConfigurationPnDbContext backendConfigurationPnDbContext,
            IBackendConfigurationLocalizationService backendConfigurationLocalizationService)
        {
            //_coreHelper = coreHelper;
            _userService = userService;
            _backendConfigurationLocalizationService = backendConfigurationLocalizationService;
            _backendConfigurationPnDbContext = backendConfigurationPnDbContext;
        }

        public async Task<OperationDataResult<List<PropertyAreaModel>>> Read(int id)
        {
            try
            {
                var propertyAreas = new List<PropertyAreaModel>();

                var propertyAreasQuery = _backendConfigurationPnDbContext.AreaProperty
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.ProperyId == id)
                    .Include(x => x.Area);
                if(propertyAreasQuery.Any())
                {
                    propertyAreas = await propertyAreasQuery
                        .Select(x => new PropertyAreaModel
                        {
                            Id = x.Id,
                            Activated = x.Checked,
                            Descriprion = x.Area.Description,
                            Name = x.Area.Name,
                            // Status = 
                        })
                        .ToListAsync();
                }

                return new OperationDataResult<List<PropertyAreaModel>>(true, propertyAreas);
            }
            catch (Exception e)
            {
                Log.LogException(e.Message);
                Log.LogException(e.StackTrace);
                return new OperationDataResult<List<PropertyAreaModel>>(false, _backendConfigurationLocalizationService.GetString("ErrorWhileReadPropertyAreas"));
            }
        }

        public async Task<OperationResult> Update(PropertyAreasUpdateModel updateModel)
        {
            try
            {
                //var property = await _backendConfigurationPnDbContext.Properties
                //    .Where(x => x.Id == updateModel.Id)
                //    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                //    .FirstOrDefaultAsync();

                //if (property == null)
                //{
                //    return new OperationResult(false, _backendConfigurationLocalizationService.GetString("PropertyNotFound"));
                //}

                //property.Address = updateModel.Address;
                //property.CHR = updateModel.Chr;
                //property.Name = updateModel.Name;
                //property.UpdatedByUserId = _userService.UserId;

                //await property.Update(_backendConfigurationPnDbContext);

                return new OperationResult(true, _backendConfigurationLocalizationService.GetString("SuccessfullyUpdateProperties"));
            }
            catch (Exception e)
            {
                Log.LogException(e.Message);
                Log.LogException(e.StackTrace);
                return new OperationResult(false, _backendConfigurationLocalizationService.GetString("ErrorWhileUpdateProperties"));
            }
        }
    }
}
