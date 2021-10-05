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

namespace BackendConfiguration.Pn.Services.BackendConfigurationPropertyAreasService
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using BackendConfigurationLocalizationService;
    using Infrastructure.Models.PropertyAreas;
    using Microsoft.EntityFrameworkCore;
    using Microting.eForm.Dto;
    using Microting.eForm.Infrastructure.Constants;
    using Microting.eForm.Infrastructure.Data.Entities;
    using Microting.eFormApi.BasePn.Abstractions;
    using Microting.eFormApi.BasePn.Infrastructure.Helpers;
    using Microting.eFormApi.BasePn.Infrastructure.Models.API;
    using Microting.eFormApi.BasePn.Infrastructure.Models.Common;
    using Microting.EformBackendConfigurationBase.Infrastructure.Data;
    using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
    using Microting.EformBackendConfigurationBase.Infrastructure.Enum;

    public class BackendConfigurationPropertyAreasService : IBackendConfigurationPropertyAreasService
    {
        private readonly IEFormCoreService _coreHelper;
        private readonly IBackendConfigurationLocalizationService _backendConfigurationLocalizationService;
        private readonly IUserService _userService;
        private readonly BackendConfigurationPnDbContext _backendConfigurationPnDbContext;

        public BackendConfigurationPropertyAreasService(
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

        public async Task<OperationDataResult<List<PropertyAreaModel>>> Read(int propertyId)
        {
            try
            {
                var propertyAreas = new List<PropertyAreaModel>();

                var propertyAreasQuery = _backendConfigurationPnDbContext.AreaProperties
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.PropertyId == propertyId)
                    .Include(x => x.Area);

                var areas = _backendConfigurationPnDbContext.Areas
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .ToList();

                List<PropertyAreaModel> areasForAdd;

                if (propertyAreasQuery.Any())
                {
                    propertyAreas = await propertyAreasQuery
                        .Select(x => new PropertyAreaModel
                        {
                            Id = x.Id,
                            Activated = x.Checked,
                            Description = x.Area.Description,
                            Name = x.Area.Name,
                            Status = false,
                        })
                        .ToListAsync();
                    areasForAdd = areas.Where(x => !propertyAreasQuery.Any(y => y.AreaId == x.Id))
                        .Select(x => new PropertyAreaModel
                        {
                            Id = null,
                            Activated = false,
                            Description = x.Description,
                            Name = x.Name,
                            Status = false,
                        })
                        .ToList();
                }
                else
                {
                    areasForAdd = areas
                        .Select(x => new PropertyAreaModel
                        {
                            Id = null,
                            Activated = false,
                            Description = x.Description,
                            Name = x.Name,
                            Status = false,
                        })
                        .ToList();
                }

                propertyAreas.AddRange(areasForAdd);

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
                var core = await _coreHelper.GetCore();
                var sdkDbContext = core.DbContextHelper.GetDbContext();

                var assignments = await _backendConfigurationPnDbContext.AreaProperties
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.PropertyId == updateModel.PropertyId)
                    .ToListAsync();

                var assignmentsForCreate = updateModel.Areas
                    .Where(x => x.Id == null)
                    .ToList();

                var assignmentsForUpdate = updateModel.Areas
                    .Where(x => x.Id.HasValue)
                    .Where(x => assignments.First(y => y.Id == x.Id).Checked != x.Activated)
                    .ToList();

                var assignmentsForDelete = assignments
                    .Where(x => !updateModel.Areas.Where(y => y.Id.HasValue).Select(y => y.Id).Contains(x.Id))
                    .ToList();


                foreach (var assignmentForCreate in assignmentsForCreate)
                {
                    var area = await _backendConfigurationPnDbContext.Areas
                        .Include(x => x.AreaRules)
                        .FirstOrDefaultAsync(x =>
                        x.Name == assignmentForCreate.Name && x.Description == assignmentForCreate.Description);

                    var newAssignment = new AreaProperty
                    {
                        CreatedByUserId = _userService.UserId,
                        UpdatedByUserId = _userService.UserId,
                        AreaId = area.Id,
                        PropertyId = updateModel.PropertyId,
                        Checked = assignmentForCreate.Activated,
                    };
                    await newAssignment.Create(_backendConfigurationPnDbContext);

                    var property = await _backendConfigurationPnDbContext.Properties
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Where(x => x.Id == updateModel.PropertyId)
                        .FirstAsync();
                    if (assignmentForCreate.Activated)
                    {
                        switch (area.Type)
                        {
                            case AreaTypesEnum.Type3:
                                {
                                    var folderId = await core.FolderCreate(
                                        new List<KeyValuePair<string, string>>
                                        {
                                        new("da", "05. Stables"),
                                        },
                                        new List<KeyValuePair<string, string>> { new("da", ""), },
                                        property.FolderId);
                                    var assignmentWithOneFolder = new ProperyAreaFolder
                                    {
                                        FolderId = folderId,
                                        ProperyAreaAsignmentId = newAssignment.Id,
                                    };

                                    var assignmentWithTwoFolder = new ProperyAreaFolder
                                    {
                                        FolderId = await core.FolderCreate(
                                            new List<KeyValuePair<string, string>>
                                            {
                                            new("da", "24. Tail bite"),
                                            },
                                            new List<KeyValuePair<string, string>>
                                            {
                                            new("da", ""),
                                            },
                                            property.FolderId),
                                        ProperyAreaAsignmentId = newAssignment.Id,
                                    };

                                    await assignmentWithOneFolder.Create(_backendConfigurationPnDbContext);
                                    await assignmentWithTwoFolder.Create(_backendConfigurationPnDbContext);

                                    foreach (var areaRule in area.AreaRules.Where(x => x.FolderId != 0))
                                    {
                                        areaRule.FolderId = folderId;
                                        await areaRule.Update(_backendConfigurationPnDbContext);
                                    }

                                    break;
                                }
                            case AreaTypesEnum.Type5:
                                {
                                    //create 7 folders
                                    var folderIds = new List<int>
                                    {
                                        await core.FolderCreate( new List<KeyValuePair<string, string>> {new("da", "Monday"),}, new List<KeyValuePair<string, string>> {new("da", ""),}, property.FolderId),
                                        await core.FolderCreate( new List<KeyValuePair<string, string>> {new("da", "Tuesday"),}, new List<KeyValuePair<string, string>> {new("da", ""),}, property.FolderId),
                                        await core.FolderCreate( new List<KeyValuePair<string, string>> {new("da", "Wednesday"),}, new List<KeyValuePair<string, string>> {new("da", ""),}, property.FolderId),
                                        await core.FolderCreate( new List<KeyValuePair<string, string>> {new("da", "Thursday"),}, new List<KeyValuePair<string, string>> {new("da", ""),}, property.FolderId),
                                        await core.FolderCreate( new List<KeyValuePair<string, string>> {new("da", "Friday"),}, new List<KeyValuePair<string, string>> {new("da", ""),}, property.FolderId),
                                        await core.FolderCreate( new List<KeyValuePair<string, string>> {new("da", "Saturday"),}, new List<KeyValuePair<string, string>> {new("da", ""),}, property.FolderId),
                                        await core.FolderCreate( new List<KeyValuePair<string, string>> {new("da", "Sunday"),}, new List<KeyValuePair<string, string>> {new("da", ""),} ,property.FolderId),
                                    };

                                    foreach (var assignmentWithFolder in folderIds.Select(folderId => new ProperyAreaFolder
                                    {
                                        FolderId = folderId,
                                        ProperyAreaAsignmentId = newAssignment.Id,
                                    }))
                                    {
                                        await assignmentWithFolder.Create(_backendConfigurationPnDbContext);
                                    }

                                    foreach (var areaRule in area.AreaRules.Where(x => x.FolderId != 0))
                                    {
                                        areaRule.FolderId = folderIds.First();
                                        await areaRule.Update(_backendConfigurationPnDbContext);
                                    }
                                    break;
                                }
                            default:
                                {
                                    var folderId = await core.FolderCreate(
                                        new List<KeyValuePair<string, string>>
                                        {
                                        new("da", area.Name),
                                        },
                                        new List<KeyValuePair<string, string>>
                                        {
                                        new("da", ""),
                                        },
                                        property.FolderId);
                                    var assignmentWithFolder = new ProperyAreaFolder
                                    {
                                        FolderId = folderId,
                                        ProperyAreaAsignmentId = newAssignment.Id,
                                    };
                                    await assignmentWithFolder.Create(_backendConfigurationPnDbContext);

                                    foreach (var areaRule in area.AreaRules.Where(x => x.FolderId != 0))
                                    {
                                        areaRule.FolderId = folderId;
                                        await areaRule.Update(_backendConfigurationPnDbContext);
                                    }

                                    break;
                                }
                        }
                    }
                }

                foreach (var areaProperty in assignmentsForUpdate)
                {
                    var assignmentForUpdate = assignments.First(x => x.Id == areaProperty.Id);
                    assignmentForUpdate.Checked = areaProperty.Activated;
                    assignmentForUpdate.UpdatedByUserId = _userService.UserId;
                    await assignmentForUpdate.Update(_backendConfigurationPnDbContext);
                }

                foreach (var areaPropertyForDelete in assignmentsForDelete)
                {
                    await areaPropertyForDelete.Delete(_backendConfigurationPnDbContext);
                    var foldersIdForDelete = _backendConfigurationPnDbContext.ProperyAreaFolders
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Where(x => x.ProperyAreaAsignmentId == areaPropertyForDelete.Id)
                        .Select(x => x.FolderId)
                        .ToList();

                    foreach (var folderIdForDelete in foldersIdForDelete)
                    {
                        var folder = await sdkDbContext.Folders
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Where(x => x.Id == folderIdForDelete)
                        .FirstAsync();
                        await folder.Delete(sdkDbContext);
                    }
                }

                return new OperationResult(true, _backendConfigurationLocalizationService.GetString("SuccessfullyUpdatePropertyAreas"));
            }
            catch (Exception e)
            {
                Log.LogException(e.Message);
                Log.LogException(e.StackTrace);
                return new OperationResult(false, _backendConfigurationLocalizationService.GetString("ErrorWhileUpdatePropertyAreas"));
            }
        }

        public async Task<OperationDataResult<AreaModel>> ReadArea(int propertyAreaId)
        {
            try
            {
                var areaProperties = await _backendConfigurationPnDbContext.AreaProperties
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Id == propertyAreaId)
                    .Include(x => x.Property)
                    .ThenInclude(x => x.SelectedLanguages)
                    .FirstOrDefaultAsync();

                var area = await _backendConfigurationPnDbContext.Areas
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Id == areaProperties.AreaId)
                    .FirstOrDefaultAsync();


                var workers = await _backendConfigurationPnDbContext.PropertyWorkers
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.PropertyId == areaProperties.PropertyId)
                    .Select(x => x.WorkerId)
                    .ToListAsync();

                if (!workers.Any())
                {
                    return new OperationDataResult<AreaModel>(false, _backendConfigurationLocalizationService.GetString("NotFoundPropertyWorkerAssignments"));
                }

                var core = await _coreHelper.GetCore();
                var sdkDbContex = core.DbContextHelper.GetDbContext();
                var sites = new List<SiteDto>();

                foreach (var worker in workers)
                {
                    sites.Add(await core.SiteRead(worker));
                }

                var languages = await sdkDbContex.Languages.AsNoTracking().ToListAsync();

                var areaModel = new AreaModel
                {
                    Name = area.Name,
                    Id = area.Id,
                    Languages = areaProperties.Property.SelectedLanguages.Select(x => new CommonDictionaryModel
                    {
                        Id = x.LanguageId,
                        Name = languages.First(y => y.Id == x.LanguageId).Name,
                    }).ToList(),
                    AvailableWorkers = sites,
                    Type = area.Type,
                };
                return new OperationDataResult<AreaModel>(true, areaModel);
            }
            catch (Exception e)
            {
                Log.LogException(e.Message);
                Log.LogException(e.StackTrace);
                return new OperationDataResult<AreaModel>(false, _backendConfigurationLocalizationService.GetString("ErrorWhileReadArea"));
            }
        }
    }
}
