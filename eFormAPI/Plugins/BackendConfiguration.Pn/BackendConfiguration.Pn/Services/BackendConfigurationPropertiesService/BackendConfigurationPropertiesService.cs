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
    using Infrastructure.Models.Properties;
    using Microsoft.EntityFrameworkCore;
    using Microting.eForm.Infrastructure.Constants;
    using Microting.eFormApi.BasePn.Abstractions;
    using Microting.eFormApi.BasePn.Infrastructure.Helpers;
    using Microting.eFormApi.BasePn.Infrastructure.Models.API;
    using Microting.eFormApi.BasePn.Infrastructure.Models.Common;
    using Microting.EformBackendConfigurationBase.Infrastructure.Data;
    using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
    using CommonTranslationsModel = Microting.eForm.Infrastructure.Models.CommonTranslationsModel;

    public class BackendConfigurationPropertiesService: IBackendConfigurationPropertiesService
    {
        private readonly IEFormCoreService _coreHelper;
        private readonly IBackendConfigurationLocalizationService _backendConfigurationLocalizationService;
        private readonly IUserService _userService;
        private readonly BackendConfigurationPnDbContext _backendConfigurationPnDbContext;

        public BackendConfigurationPropertiesService(
            IEFormCoreService coreHelper,
            IUserService userService,
            BackendConfigurationPnDbContext backendConfigurationPnDbContext,
            IBackendConfigurationLocalizationService backendConfigurationLocalizationService)
        {
            _coreHelper = coreHelper;
            _userService = userService;
            _backendConfigurationLocalizationService = backendConfigurationLocalizationService;
            _backendConfigurationPnDbContext = backendConfigurationPnDbContext;
        }

        public async Task<OperationDataResult<Paged<PropertiesModel>>> Index(ProperiesRequesModel request)
        {
            try
            {
                // get query
                var propertiesQuery = _backendConfigurationPnDbContext.Properties
                    .Include(x => x.SelectedLanguages)
                    .Include(x => x.PropertyWorkers)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed);

                // add sort
                //propertiesQuery = QueryHelper.AddSortToQuery(propertiesQuery, request.Sort, request.IsSortDsc);

                // add filtering
                //if (!string.IsNullOrEmpty(request.NameFilter))
                //{
                //    propertiesQuery = QueryHelper
                //        .AddFilterToQuery(propertiesQuery, new List<string>
                //        {
                //            "Name",
                //            "CHR",
                //            "Address",
                //        }, request.NameFilter);
                //}

                // get total
                var total = await propertiesQuery.Select(x => x.Id).CountAsync();

                var properties = new List<PropertiesModel>();

                if (total > 0)
                {
                    // pagination
                    //propertiesQuery = propertiesQuery
                    //    .Skip(request.Offset)
                    //    .Take(request.PageSize);

                    // add select to query and get from db
                    properties = await propertiesQuery
                        .Select(x => new PropertiesModel
                        {
                            Id = x.Id,
                            Address = x.Address,
                            Chr = x.CHR,
                            Name = x.Name,
                            Languages = x.SelectedLanguages
                                .Where(y => y.WorkflowState != Constants.WorkflowStates.Removed)
                                .Select(y => new CommonDictionaryModel {Id = y.LanguageId})
                                .ToList(),
                            IsWorkersAssigned = x.PropertyWorkers.Any(y => y.WorkflowState != Constants.WorkflowStates.Removed),
                        }).ToListAsync();
                }

                return new OperationDataResult<Paged<PropertiesModel>>(true,
                    new Paged<PropertiesModel> {Entities = properties, Total = total});
            }
            catch (Exception ex)
            {
                Log.LogException(ex.Message);
                Log.LogException(ex.StackTrace);
                return new OperationDataResult<Paged<PropertiesModel>>(false, _backendConfigurationLocalizationService.GetString("ErrorWhileObtainingProperties"));
            }
        }

        public async Task<OperationResult> Create(PropertyCreateModel propertyCreateModel)
        {
            try
            {
                var core = await _coreHelper.GetCore();
                var sdkDbContext = core.DbContextHelper.GetDbContext();

                var newProperty = new Property
                {
                    Address = propertyCreateModel.Address,
                    CHR = propertyCreateModel.Chr,
                    Name = propertyCreateModel.Name,
                    CreatedByUserId = _userService.UserId,
                    UpdatedByUserId = _userService.UserId,
                };
                await newProperty.Create(_backendConfigurationPnDbContext);

                var selectedTranslates = propertyCreateModel.LanguagesIds
                    .Select(x => new PropertySelectedLanguage
                    {
                        LanguageId = x,
                        PropertyId = newProperty.Id,
                        CreatedByUserId = _userService.UserId,
                        UpdatedByUserId = _userService.UserId,
                    });

                foreach (var selectedTranslate in selectedTranslates)
                {
                    await selectedTranslate.Create(_backendConfigurationPnDbContext);
                }

                var translatesForFolder = await sdkDbContext.Languages
                    .Select(
                        x => new CommonTranslationsModel
                        {
                            LanguageId = x.Id,
                            Name = propertyCreateModel.Name,
                            Description = ""
                        })
                    .ToListAsync();
                newProperty.FolderId = await core.FolderCreate(translatesForFolder, null);
                await newProperty.Update(_backendConfigurationPnDbContext);

                return new OperationResult(true, _backendConfigurationLocalizationService.GetString("SuccessfullyCreatingProperties"));
            }
            catch (Exception e)
            {
                Log.LogException(e.Message);
                Log.LogException(e.StackTrace);
                return new OperationResult(false, _backendConfigurationLocalizationService.GetString("ErrorWhileCreatingProperties"));
            }
        }

        public async Task<OperationDataResult<PropertiesModel>> Read(int id)
        {
            try
            {
                var property = await _backendConfigurationPnDbContext.Properties
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Id == id)
                    .Include(x => x.SelectedLanguages)
                    .Select(x => new PropertiesModel
                    {
                        Id = x.Id,
                        Address = x.Address,
                        Chr = x.CHR,
                        Name = x.Name,
                        Languages = x.SelectedLanguages
                            .Where(y => y.WorkflowState != Constants.WorkflowStates.Removed)
                            .Select(y => new CommonDictionaryModel {Id = y.LanguageId})
                            .ToList()
                    })
                    .FirstOrDefaultAsync();

                if (property == null)
                {
                    return new OperationDataResult<PropertiesModel>(false, _backendConfigurationLocalizationService.GetString("PropertyNotFound"));
                }

                return new OperationDataResult<PropertiesModel>(true, property);
            }
            catch (Exception e)
            {
                Log.LogException(e.Message);
                Log.LogException(e.StackTrace);
                return new OperationDataResult<PropertiesModel>(false, _backendConfigurationLocalizationService.GetString("ErrorWhileReadProperty"));
            }
        }

        public async Task<OperationResult> Update(PropertiesUpdateModel updateModel)
        {
            try
            {
                var property = await _backendConfigurationPnDbContext.Properties
                    .Where(x => x.Id == updateModel.Id)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Include(x => x.SelectedLanguages)
                    .FirstOrDefaultAsync();

                if (property == null)
                {
                    return new OperationResult(false, _backendConfigurationLocalizationService.GetString("PropertyNotFound"));
                }

                property.Address = updateModel.Address;
                property.CHR = updateModel.Chr;
                property.Name = updateModel.Name;
                property.UpdatedByUserId = _userService.UserId;

                await property.Update(_backendConfigurationPnDbContext);

                property.SelectedLanguages = property.SelectedLanguages
                    .Where(y => y.WorkflowState != Constants.WorkflowStates.Removed)
                    .ToList();

                var selectedLanguagesForDelete = property.SelectedLanguages
                    .Where(x => !updateModel.LanguagesIds.Contains(x.LanguageId))
                    .ToList();

                var selectedLanguagesForCreate = updateModel.LanguagesIds
                    .Where(x => !property.SelectedLanguages.Exists(y => y.LanguageId == x))
                    .Select(x => new PropertySelectedLanguage
                    {
                        LanguageId = x,
                        PropertyId = property.Id,
                        CreatedByUserId = _userService.UserId,
                        UpdatedByUserId = _userService.UserId,
                    })
                    .ToList();

                foreach (var selectedLanguageForDelete in selectedLanguagesForDelete)
                {
                    selectedLanguageForDelete.UpdatedByUserId = _userService.UserId;
                    await selectedLanguageForDelete.Delete(_backendConfigurationPnDbContext);
                }


                foreach (var selectedLanguageForCreate in selectedLanguagesForCreate)
                {
                    await selectedLanguageForCreate.Create(_backendConfigurationPnDbContext);
                }

                return new OperationResult(true, _backendConfigurationLocalizationService.GetString("SuccessfullyUpdateProperties"));
            }
            catch (Exception e)
            {
                Log.LogException(e.Message);
                Log.LogException(e.StackTrace);
                return new OperationResult(false, _backendConfigurationLocalizationService.GetString("ErrorWhileUpdateProperties"));
            }
        }

        public async Task<OperationResult> Delete(int id)
        {
            try
            {
                var property = await _backendConfigurationPnDbContext.Properties
                    .Where(x => x.Id == id)
                    .Include(x => x.SelectedLanguages)
                    .Include(x => x.PropertyWorkers)
                    .Include(x => x.AreaProperties)
                    .ThenInclude(x => x.ProperyAreaFolders)
                    .FirstOrDefaultAsync();

                if (property == null)
                {
                    return new OperationResult(false, _backendConfigurationLocalizationService.GetString("PropertyNotFound"));
                }

                foreach (var propertyWorker in property.PropertyWorkers)
                {
                    propertyWorker.UpdatedByUserId = _userService.UserId;
                    await propertyWorker.Delete(_backendConfigurationPnDbContext);
                }

                var core = await _coreHelper.GetCore();
                await using var sdkDbContext = core.DbContextHelper.GetDbContext();

                foreach (var areaProperty in property.AreaProperties)
                {
                    foreach (var properyAreaFolder in areaProperty.ProperyAreaFolders)
                    {
                        var folder = await sdkDbContext.Folders
                            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                            .Where(x => x.Id == properyAreaFolder.FolderId)
                            .Include(x => x.Children)
                            .FirstAsync();
                        await folder.Delete(sdkDbContext);
                    }

                    if (areaProperty.GroupMicrotingUuid != 0)
                    {
                        await core.EntityGroupDelete(areaProperty.GroupMicrotingUuid.ToString());
                    }
                    areaProperty.UpdatedByUserId = _userService.UserId;
                    await areaProperty.Delete(_backendConfigurationPnDbContext);
                }

                foreach (var selectedLanguage in property.SelectedLanguages)
                {
                    selectedLanguage.UpdatedByUserId = _userService.UserId;
                    await selectedLanguage.Delete(_backendConfigurationPnDbContext);
                }

                property.UpdatedByUserId = _userService.UserId;
                await property.Delete(_backendConfigurationPnDbContext);

                return new OperationResult(true, _backendConfigurationLocalizationService.GetString("SuccessfullyDeleteProperties"));
            }
            catch (Exception e)
            {
                Log.LogException(e.Message);
                Log.LogException(e.StackTrace);
                return new OperationResult(false, _backendConfigurationLocalizationService.GetString("ErrorWhileDeleteProperties"));
            }
        }

        public async Task<OperationDataResult<List<CommonDictionaryModel>>> GetCommonDictionary()
        {
            try
            {
                var properties = await _backendConfigurationPnDbContext.Properties
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Select(x => new CommonDictionaryModel
                    {
                        Id = x.Id,
                        Name = x.Name,
                        Description = "",
                    }).ToListAsync();
                return new OperationDataResult<List<CommonDictionaryModel>>(true, properties);
            }
            catch (Exception ex)
            {
                Log.LogException(ex.Message);
                Log.LogException(ex.StackTrace);
                return new OperationDataResult<List<CommonDictionaryModel>>(false, _backendConfigurationLocalizationService.GetString("ErrorWhileObtainingProperties"));
            }
        }
    }
}
