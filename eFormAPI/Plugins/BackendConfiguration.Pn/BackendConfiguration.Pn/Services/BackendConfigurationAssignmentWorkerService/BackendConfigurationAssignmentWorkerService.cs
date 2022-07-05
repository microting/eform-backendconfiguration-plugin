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
using BackendConfiguration.Pn.Infrastructure.Models;
using eFormCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure;
using Microting.eFormApi.BasePn.Infrastructure.Helpers.PluginDbOptions;
using Microting.eFormApi.BasePn.Infrastructure.Models.Common;
using Microting.ItemsPlanningBase.Infrastructure.Data;
using Microting.TimePlanningBase.Infrastructure.Data;
using Microting.TimePlanningBase.Infrastructure.Data.Entities;
using Microting.TimePlanningBase.Infrastructure.Data.Models;

namespace BackendConfiguration.Pn.Services.BackendConfigurationAssignmentWorkerService
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using BackendConfigurationLocalizationService;
    using Infrastructure.Models.AssignmentWorker;
    using Microsoft.EntityFrameworkCore;
    using Microting.eForm.Infrastructure.Constants;
    using Microting.eFormApi.BasePn.Abstractions;
    using Microting.eFormApi.BasePn.Infrastructure.Helpers;
    using Microting.eFormApi.BasePn.Infrastructure.Models.API;
    using Microting.EformBackendConfigurationBase.Infrastructure.Data;
    using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;

    public class BackendConfigurationAssignmentWorkerService : IBackendConfigurationAssignmentWorkerService
    {
        private readonly IEFormCoreService _coreHelper;
        private readonly IBackendConfigurationLocalizationService _backendConfigurationLocalizationService;
        private readonly IUserService _userService;
        private readonly BackendConfigurationPnDbContext _backendConfigurationPnDbContext;
        private readonly ItemsPlanningPnDbContext _itemsPlanningPnDbContext;
        private readonly WorkOrderHelper _workOrderHelper;
        // private readonly IDeviceUsersService _deviceUsersService;
        private readonly TimePlanningPnDbContext _timePlanningDbContext;
        private readonly ILogger<BackendConfigurationAssignmentWorkerService> _logger;
        private readonly IPluginDbOptions<TimePlanningBaseSettings> _options;

        public BackendConfigurationAssignmentWorkerService(
            IEFormCoreService coreHelper,
            IUserService userService,
            BackendConfigurationPnDbContext backendConfigurationPnDbContext,
            IBackendConfigurationLocalizationService backendConfigurationLocalizationService, ItemsPlanningPnDbContext itemsPlanningPnDbContext, TimePlanningPnDbContext timePlanningDbContext)
        {
            _coreHelper = coreHelper;
            _userService = userService;
            _backendConfigurationLocalizationService = backendConfigurationLocalizationService;
            _itemsPlanningPnDbContext = itemsPlanningPnDbContext;
            // _deviceUsersService = deviceUsersService;
            _timePlanningDbContext = timePlanningDbContext;
            _backendConfigurationPnDbContext = backendConfigurationPnDbContext;
            _workOrderHelper = new WorkOrderHelper(_coreHelper, _backendConfigurationPnDbContext, _backendConfigurationLocalizationService, _userService);
        }

        public async Task<OperationDataResult<List<PropertyAssignWorkersModel>>> GetPropertiesAssignment()
        {
            try
            {
                var assignWorkersModels = new List<PropertyAssignWorkersModel>();
                var query = _backendConfigurationPnDbContext.PropertyWorkers.AsQueryable();
                query = query
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed);

                if (query.Any())
                {
                    var listWorkerId = await query.Select(x => x.WorkerId).Distinct().ToListAsync();

                    foreach (var workerId in listWorkerId)
                    {
                        var assignments = await query
                            .Where(x => x.WorkerId == workerId)
                            .Select(x => new PropertyAssignmentWorkerModel
                                { PropertyId = x.PropertyId, IsChecked = true })
                            .ToListAsync();
                        assignWorkersModels.Add(new PropertyAssignWorkersModel
                            { SiteId = workerId, Assignments = assignments });
                    }

                    var properties = await _backendConfigurationPnDbContext.Properties
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Select(x => new PropertyAssignmentWorkerModel { PropertyId = x.Id, IsChecked = false })
                        .ToListAsync();

                    foreach (var propertyAssignWorkersModel in assignWorkersModels)
                    {
                        var missingProperties = properties
                            .Where(x => !propertyAssignWorkersModel.Assignments.Select(y => y.PropertyId)
                                .Contains(x.PropertyId))
                            .ToList();
                        propertyAssignWorkersModel.Assignments.AddRange(missingProperties);
                    }
                }

                return new OperationDataResult<List<PropertyAssignWorkersModel>>(true, assignWorkersModels);
            }
            catch (Exception ex)
            {
                Log.LogException(ex.Message);
                Log.LogException(ex.StackTrace);
                return new OperationDataResult<List<PropertyAssignWorkersModel>>(false,
                    $"{_backendConfigurationLocalizationService.GetString("ErrorWhileObtainingAssignmentsProperties")}: {ex.Message}");
            }
        }

        public async Task<OperationResult> Create(PropertyAssignWorkersModel createModel)
        {
            try
            {
                // var propertyIds = createModel.Assignments
                //     .Select(x => x.PropertyId)
                //     .Distinct()
                //     .ToList();
                List<PropertyWorker> propertyWorkers = new List<PropertyWorker>();
                foreach (var propertyAssignment in createModel.Assignments
                             .Select(propertyAssignmentWorkerModel => new PropertyWorker
                             {
                                 WorkerId = createModel.SiteId,
                                 PropertyId = propertyAssignmentWorkerModel.PropertyId,
                                 CreatedByUserId = _userService.UserId,
                                 UpdatedByUserId = _userService.UserId
                             }))
                {
                    await propertyAssignment.Create(_backendConfigurationPnDbContext);
                    propertyWorkers.Add(propertyAssignment);
                }

                await _workOrderHelper.WorkorderFlowDeployEform(propertyWorkers);

                return new OperationResult(true,
                    _backendConfigurationLocalizationService.GetString("SuccessfullyAssignmentsCreatingProperties"));
            }
            catch (Exception e)
            {
                Log.LogException(e.Message);
                Log.LogException(e.StackTrace);
                return new OperationResult(false,
                    $"{_backendConfigurationLocalizationService.GetString("ErrorWhileAssignmentsCreatingProperties")}: {e.Message}");
            }
        }

        public async Task<OperationResult> Update(PropertyAssignWorkersModel updateModel)
        {
            try
            {
                var core = await _coreHelper.GetCore();
                var sdkDbContext = core.DbContextHelper.GetDbContext();
                updateModel.Assignments = updateModel.Assignments.Where(x => x.IsChecked).ToList();

                var assignments = await _backendConfigurationPnDbContext.PropertyWorkers
                    .Where(x => x.WorkerId == updateModel.SiteId)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .ToListAsync();

                var assignmentsForCreate = updateModel.Assignments
                    .Select(x => x.PropertyId)
                    .Where(x => !assignments.Select(y => y.PropertyId).Contains(x))
                    .ToList();
                List<PropertyWorker> propertyWorkers = new List<PropertyWorker>();

                foreach (var propertyAssignment in assignmentsForCreate
                             .Select(propertyAssignmentWorkerModel => new PropertyWorker
                             {
                                 WorkerId = updateModel.SiteId,
                                 PropertyId = propertyAssignmentWorkerModel,
                                 CreatedByUserId = _userService.UserId,
                                 UpdatedByUserId = _userService.UserId
                             }))
                {
                    await propertyAssignment.Create(_backendConfigurationPnDbContext);
                    propertyWorkers.Add(propertyAssignment);
                }

                var assignmentsForDelete = assignments
                    .Where(x => !updateModel.Assignments.Select(y => y.PropertyId).Contains(x.PropertyId))
                    .ToList();

                foreach (var propertyAssignment in assignmentsForDelete)
                {
                    propertyAssignment.UpdatedByUserId = _userService.UserId;
                    await propertyAssignment.Delete(_backendConfigurationPnDbContext);
                    if (propertyAssignment.EntityItemId != null)
                    {
                        await core.EntityItemDelete((int)propertyAssignment.EntityItemId);
                    }

                    var property = await _backendConfigurationPnDbContext.Properties
                        .Where(x => x.Id == propertyAssignment.PropertyId)
                        .SingleAsync();

                    var entityItems = await sdkDbContext.EntityItems
                        .Where(x => x.EntityGroupId == property.EntitySelectListDeviceUsers)
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .OrderBy(x => x.Name)
                        .AsNoTracking()
                        .ToListAsync();

                    int entityItemIncrementer = 0;
                    foreach (var entity in entityItems)
                    {
                        await core.EntityItemUpdate(entity.Id, entity.Name, entity.Description,
                            entity.EntityItemUid, entityItemIncrementer);
                        entityItemIncrementer++;
                    }

                    await DeleteAllEntriesForPropertyAssignment(propertyAssignment, core, property, sdkDbContext);
                }

                if(assignmentsForDelete.Any())
                {
                    await _workOrderHelper.RetractEform(assignmentsForDelete, true);
                    await _workOrderHelper.RetractEform(assignmentsForDelete, false);
                    await _workOrderHelper.RetractEform(assignmentsForDelete, false);
                }

                await _workOrderHelper.WorkorderFlowDeployEform(propertyWorkers);
                return new OperationResult(true,
                    _backendConfigurationLocalizationService.GetString("SuccessfullyUpdateAssignmentsProperties"));
            }
            catch (Exception e)
            {
                Log.LogException(e.Message);
                Log.LogException(e.StackTrace);
                return new OperationResult(false,
                    $"{_backendConfigurationLocalizationService.GetString("ErrorWhileUpdateAssignmentsProperties")}: {e.Message}");
            }
        }

        public async Task<OperationResult> Delete(int deviceUserId)
        {
            try
            {
                var core = await _coreHelper.GetCore();
                var sdkDbContext = core.DbContextHelper.GetDbContext();
                var propertyWorkers = await _backendConfigurationPnDbContext.PropertyWorkers
                    // .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.WorkerId == deviceUserId)
                    .ToListAsync();

                var eformIdForNewTasks = await sdkDbContext.CheckListTranslations
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Text == "01. New task")
                    .Select(x => x.CheckListId)
                    .FirstAsync();

                var eformIdForOngoingTasks = await sdkDbContext.CheckListTranslations
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Text == "02. Ongoing task")
                    .Select(x => x.CheckListId)
                    .FirstAsync();

                var eformIdForCompletedTasks = await sdkDbContext.CheckListTranslations
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Text == "03. Completed task")
                    .Select(x => x.CheckListId)
                    .FirstAsync();


                foreach (var propertyAssignment in propertyWorkers)
                {
                    propertyAssignment.UpdatedByUserId = _userService.UserId;
                    await propertyAssignment.Delete(_backendConfigurationPnDbContext);
                    if (propertyAssignment.EntityItemId != null)
                    {
                        await core.EntityItemDelete((int)propertyAssignment.EntityItemId);
                    }

                    var property = await _backendConfigurationPnDbContext.Properties
                        .Where(x => x.Id == propertyAssignment.PropertyId)
                        .SingleOrDefaultAsync();

                    var entityItems = await sdkDbContext.EntityItems
                        .Where(x => x.EntityGroupId == property.EntitySelectListDeviceUsers)
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .OrderBy(x => x.Name)
                        .AsNoTracking()
                        .ToListAsync();

                    int entityItemIncrementer = 0;
                    foreach (var entity in entityItems)
                    {
                        await core.EntityItemUpdate(entity.Id, entity.Name, entity.Description,
                            entity.EntityItemUid, entityItemIncrementer);
                        entityItemIncrementer++;
                    }
                    await DeleteAllEntriesForPropertyAssignment(propertyAssignment, core, property, sdkDbContext);
                }

                await _workOrderHelper.RetractEform(propertyWorkers, true);
                await _workOrderHelper.RetractEform(propertyWorkers, false);
                await _workOrderHelper.RetractEform(propertyWorkers, false);
                foreach (var assignment in propertyWorkers)
                {
                    await assignment.Delete(_backendConfigurationPnDbContext);
                }

                return new OperationResult(true,
                    _backendConfigurationLocalizationService.GetString("SuccessfullyDeleteAssignmentsProperties"));
            }
            catch (Exception e)
            {
                Log.LogException(e.Message);
                Log.LogException(e.StackTrace);
                return new OperationResult(false,
                    $"{_backendConfigurationLocalizationService.GetString("ErrorWhilDeleteAssignmentsProperties")}: {e.Message}");
            }
        }


        public async Task<OperationDataResult<List<DeviceUserModel>>> IndexDeviceUser(FilterAndSortModel requestModel)
        {
            try
            {
                var core = await _coreHelper.GetCore();
                var sdkDbContext = core.DbContextHelper.GetDbContext();
                // var deviceUsers = new List<DeviceUser>();

                var sitesQuery = sdkDbContext.Sites
                    .Include(x => x.Units)
                    .Include(x => x.SiteWorkers)
                    .ThenInclude(x => x.Worker)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed);

                try
                {
                    sitesQuery = QueryHelper.AddFilterAndSortToQuery(sitesQuery, requestModel, new List<string> { "Name" });
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }

                var deviceUsers = await sitesQuery
                    .Select(x => new DeviceUserModel
                    {
                        CustomerNo = x.Units
                            .Where(y => y.WorkflowState != Constants.WorkflowStates.Removed)
                            .Select(y => y.CustomerNo)
                            .FirstOrDefault(),
                        UserFirstName = x.SiteWorkers.FirstOrDefault(y => y.WorkflowState != Constants.WorkflowStates.Removed).Worker.FirstName,
                        UserLastName = x.SiteWorkers.FirstOrDefault(y => y.WorkflowState != Constants.WorkflowStates.Removed).Worker.LastName,
                        LanguageId = x.LanguageId,
                        OtpCode = x.Units
                            .Where(y => y.WorkflowState != Constants.WorkflowStates.Removed)
                            .Select(y => y.OtpCode)
                            .FirstOrDefault(),
                        SiteId = x.Id,
                        SiteUid = x.MicrotingUid,
                        SiteName = x.Name,
                        UnitId = x.Units
                            .Where(y => y.WorkflowState != Constants.WorkflowStates.Removed)
                            .Select(y => y.MicrotingUid)
                            .FirstOrDefault(),
                        WorkerUid = x.SiteWorkers.FirstOrDefault(y => y.WorkflowState != Constants.WorkflowStates.Removed).Worker.MicrotingUid,
                        Language = sdkDbContext.Languages.Where(y => y.Id == x.LanguageId).Select(y => y.Name).SingleOrDefault() ?? "Danish",
                        LanguageCode = sdkDbContext.Languages.Where(y => y.Id == x.LanguageId).Select(y => y.LanguageCode).SingleOrDefault() ?? "da",
                        IsLocked = x.IsLocked
                    })
                    .ToListAsync();

                foreach (var deviceUserModel in deviceUsers)
                {
                    deviceUserModel.TimeRegistrationEnabled = _timePlanningDbContext.AssignedSites.Any(x =>
                        x.SiteId == deviceUserModel.SiteUid && x.WorkflowState != Constants.WorkflowStates.Removed);
                }

                return new OperationDataResult<List<DeviceUserModel>>(true, deviceUsers);
            }
            catch (Exception ex)
            {
                return new OperationDataResult<List<DeviceUserModel>>(false, _backendConfigurationLocalizationService.GetStringWithFormat("ErrorWhileGetDeviceUsers") + " " + ex.Message);
            }
        }

        public async Task<OperationResult> UpdateDeviceUser(DeviceUserModel deviceUserModel)
        {
            try
            {
                var core = await _coreHelper.GetCore();
                await using var sdkDbContext = core.DbContextHelper.GetDbContext();
                var language = sdkDbContext.Languages.Single(x => x.LanguageCode == deviceUserModel.LanguageCode);
                var siteDto = await core.SiteRead(deviceUserModel.Id);
                if (siteDto.WorkerUid != null)
                {
                    // var workerDto = await core.Advanced_WorkerRead((int)siteDto.WorkerUid);
                    var worker = await sdkDbContext.Workers.SingleOrDefaultAsync(x => x.MicrotingUid == siteDto.WorkerUid);
                    if (worker != null)
                    {
                        var fullName = deviceUserModel.UserFirstName + " " + deviceUserModel.UserLastName;
                        var isUpdated = await core.SiteUpdate(deviceUserModel.Id, fullName, deviceUserModel.UserFirstName,
                            deviceUserModel.UserLastName, worker.Email, deviceUserModel.LanguageCode);

                        if (isUpdated)
                        {
                            var propertyWorkers = await _backendConfigurationPnDbContext.PropertyWorkers
                                .Where(x => x.WorkerId == worker.Id)
                                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                .ToListAsync();

                            int? propertyId = null;
                            foreach (var propertyWorker in propertyWorkers)
                            {
                                var et = sdkDbContext.EntityItems.Single(x => x.Id == propertyWorker.EntityItemId);
                                await core.EntityItemUpdate((int)propertyWorker.EntityItemId, fullName, "", et.EntityItemUid,
                                    et.DisplayIndex);
                                propertyId = propertyWorker.PropertyId;
                            }

                            if (propertyId != null)
                            {
                                var property = await _backendConfigurationPnDbContext.Properties.SingleAsync(x => x.Id == propertyId);

                                var entityItems = await sdkDbContext.EntityItems
                                    .Where(x => x.EntityGroupId == property.EntitySelectListDeviceUsers)
                                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                    .OrderBy(x => x.Name)
                                    .AsNoTracking()
                                    .ToListAsync();

                                int entityItemIncrementer = 0;
                                foreach (var entityItem in entityItems)
                                {
                                    await core.EntityItemUpdate(entityItem.Id, entityItem.Name, entityItem.Description,
                                        entityItem.EntityItemUid, entityItemIncrementer);
                                    entityItemIncrementer++;
                                }
                            }
                        }
                        //var siteId = await sdkDbContext.Sites.Where(x => x.MicrotingUid == siteDto.SiteId).Select(x => x.Id).FirstAsync();
                        if (deviceUserModel.TimeRegistrationEnabled == false && _timePlanningDbContext.AssignedSites.Any(x => x.SiteId == siteDto.SiteId && x.WorkflowState != Constants.WorkflowStates.Removed))
                        {
                            var assignmentForDelete = await _timePlanningDbContext.AssignedSites.SingleAsync(x =>
                                x.SiteId == siteDto.SiteId && x.WorkflowState != Constants.WorkflowStates.Removed);

                            if (assignmentForDelete.CaseMicrotingUid != null)
                            {
                                await core.CaseDelete((int) assignmentForDelete.CaseMicrotingUid);
                            }
                        }
                        else
                        {
                            if (deviceUserModel.TimeRegistrationEnabled == true)
                            {
                                try
                                {
                                    var assignmentSite = new AssignedSite
                                    {
                                        SiteId = siteDto.SiteId,
                                        CreatedByUserId = _userService.UserId,
                                        UpdatedByUserId = _userService.UserId,
                                    };
                                    await assignmentSite.Create(_timePlanningDbContext);
                                    // var option =
                                    var newTaskId = await _timePlanningDbContext.PluginConfigurationValues.SingleAsync(x => x.Name == "TimePlanningBaseSettings:EformId");
                                    var folderId = await _timePlanningDbContext.PluginConfigurationValues.SingleAsync(x => x.Name == "TimePlanningBaseSettings:FolderId");
                                    var folder = await sdkDbContext.Folders.SingleAsync(x => x.Id == int.Parse(folderId.Value));
                                    var mainElement = await core.ReadeForm(int.Parse(newTaskId.Value), language);
                                    mainElement.CheckListFolderName = folder.MicrotingUid.ToString();
                                    mainElement.EndDate = DateTime.UtcNow.AddYears(10);
                                    mainElement.DisplayOrder = int.MinValue;
                                    mainElement.Repeated = 0;
                                    mainElement.PushMessageTitle = mainElement.Label;
                                    mainElement.EnableQuickSync = true;
                                    var caseId = await core.CaseCreate(mainElement, "", siteDto.SiteId, int.Parse(folderId.Value));
                                    assignmentSite.CaseMicrotingUid = caseId;
                                    await assignmentSite.Update(_timePlanningDbContext);

                                    return new OperationDataResult<int>(true, siteDto.SiteId);
                                }
                                catch (Exception e)
                                {
                                    Console.WriteLine(e);
                                    // _logger.LogError(e.Message);
                                    return new OperationDataResult<int>(false, "");
                                }
                            }
                        }
                        // {
                        //     Site site = await db.Sites.SingleAsync(x => x.MicrotingUid == deviceUserModel.Id);
                        //     site.LanguageId = language.Id;
                        //     await site.Update(db);
                        // }
                        return isUpdated
                            ? new OperationResult(true, _backendConfigurationLocalizationService.GetString("DeviceUserUpdatedSuccessfully"))
                            : new OperationResult(false,
                                _backendConfigurationLocalizationService.GetString("DeviceUserParamCouldNotBeUpdated"));
                    }

                    return new OperationResult(false, _backendConfigurationLocalizationService.GetString("DeviceUserCouldNotBeObtained"));
                }

                return new OperationResult(false, _backendConfigurationLocalizationService.GetString("DeviceUserNotFound"));
            }
            catch (Exception ex)
            {
                return new OperationResult(false, _backendConfigurationLocalizationService.GetString("DeviceUserCouldNotBeUpdated") + $" {ex.Message}");
            }
        }

        public async Task<OperationDataResult<int>> CreateDeviceUser(DeviceUserModel deviceUserModel)
        {
            // var result = await _deviceUsersService.Create(deviceUserModel);
            var core = await _coreHelper.GetCore();
            var siteName = deviceUserModel.UserFirstName + " " + deviceUserModel.UserLastName;
            await using var db = core.DbContextHelper.GetDbContext();
            var siteDto = await core.SiteCreate(siteName, deviceUserModel.UserFirstName, deviceUserModel.UserLastName,
                null, deviceUserModel.LanguageCode);

            var theCore = await _coreHelper.GetCore();
            await using var sdkDbContext = theCore.DbContextHelper.GetDbContext();
            var siteId = await sdkDbContext.Sites.Where(x => x.MicrotingUid == siteDto.SiteId).Select(x => x.Id).FirstAsync();

            if (deviceUserModel.TimeRegistrationEnabled == true)
            {
                try
                {
                    var assignmentSite = new AssignedSite
                    {
                        SiteId = siteId,
                        CreatedByUserId = _userService.UserId,
                        UpdatedByUserId = _userService.UserId,
                    };
                    await assignmentSite.Create(_timePlanningDbContext);
                    // var option =
                    var newTaskId = await _timePlanningDbContext.PluginConfigurationValues.SingleAsync(x => x.Name == "TimePlanningBaseSettings:EformId");
                    var folderId = await _timePlanningDbContext.PluginConfigurationValues.SingleAsync(x => x.Name == "TimePlanningBaseSettings:FolderId");;
                    var folder = await sdkDbContext.Folders.SingleAsync(x => x.Id == int.Parse(folderId.Value));
                    var site = await sdkDbContext.Sites.SingleOrDefaultAsync(x => x.Id == siteId);
                    var language = await sdkDbContext.Languages.SingleAsync(x => x.Id == site.LanguageId);
                    var mainElement = await theCore.ReadeForm(int.Parse(newTaskId.Value), language);
                    mainElement.CheckListFolderName = folder.MicrotingUid.ToString();
                    mainElement.EndDate = DateTime.UtcNow.AddYears(10);
                    mainElement.DisplayOrder = int.MinValue;
                    mainElement.Repeated = 0;
                    mainElement.PushMessageTitle = mainElement.Label;
                    mainElement.EnableQuickSync = true;
                    var caseId = await theCore.CaseCreate(mainElement, "", (int)site.MicrotingUid, int.Parse(folderId.Value));
                    assignmentSite.CaseMicrotingUid = caseId;
                    await assignmentSite.Update(_timePlanningDbContext);

                    return new OperationDataResult<int>(true, siteId);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    // _logger.LogError(e.Message);
                    return new OperationDataResult<int>(false, "");
                }
            }

            return new OperationDataResult<int>(true, siteId);
        }

        private async Task DeleteAllEntriesForPropertyAssignment(PropertyWorker propertyAssignment, Core core, Property property, MicrotingDbContext sdkDbContext)
        {
            var planningSites = await _backendConfigurationPnDbContext.PlanningSites
                .Join(_backendConfigurationPnDbContext.AreaRulePlannings,
                    ps => ps.AreaRulePlanningsId,
                    arp => arp.Id,
                    (ps, arp) => new
                    {
                        ps.Id,
                        ps.SiteId,
                        arp.PropertyId,
                        ps.WorkflowState,
                        arp.ItemPlanningId,
                        ArpId = arp.Id
                    })
                .Where(x => x.SiteId == propertyAssignment.WorkerId)
                .Where(x => x.PropertyId == propertyAssignment.PropertyId)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .ToListAsync();

            foreach (var planningSite in planningSites)
            {
                var itemPlanningSites = await _itemsPlanningPnDbContext.PlanningSites
                    .SingleOrDefaultAsync(x => x.SiteId == propertyAssignment.WorkerId
                                               && x.PlanningId == planningSite.ItemPlanningId
                                               && x.WorkflowState != Constants.WorkflowStates.Removed);

                if (itemPlanningSites != null)
                {
                    await itemPlanningSites.Delete(_itemsPlanningPnDbContext);
                }

                var itemPlanningCaseSites = await _itemsPlanningPnDbContext.PlanningCaseSites
                    .Where(x => x.PlanningId == planningSite.ItemPlanningId
                                && x.MicrotingSdkSiteId == propertyAssignment.WorkerId
                                && x.WorkflowState != Constants.WorkflowStates.Removed)
                    .ToListAsync();

                foreach (var planningCaseSite in itemPlanningCaseSites)
                {
                    var result =
                        await sdkDbContext.Cases.SingleOrDefaultAsync(x => x.Id == planningCaseSite.MicrotingSdkCaseId);
                    if (result is {MicrotingUid: { }})
                    {
                        await core.CaseDelete((int)result.MicrotingUid);
                    }
                    else
                    {
                        var clSites = await sdkDbContext.CheckListSites.SingleAsync(x =>
                            x.Id == planningCaseSite.MicrotingCheckListSitId);

                        await core.CaseDelete(clSites.MicrotingUid);
                    }
                }

                var dbPlanningSite = await _backendConfigurationPnDbContext.PlanningSites
                    .SingleAsync(x => x.Id == planningSite.Id);

                await dbPlanningSite.Delete(_backendConfigurationPnDbContext);

                itemPlanningCaseSites = await _itemsPlanningPnDbContext.PlanningCaseSites
                    .Where(x => x.PlanningId == planningSite.ItemPlanningId
                                && x.MicrotingSdkSiteId != propertyAssignment.WorkerId
                                && x.WorkflowState != Constants.WorkflowStates.Removed)
                    .ToListAsync();

                if (itemPlanningCaseSites.Count == 0)
                {
                    var itemPlanning = await _itemsPlanningPnDbContext.Plannings
                        .SingleAsync(x => x.Id == planningSite.ItemPlanningId);

                    await itemPlanning.Delete(_itemsPlanningPnDbContext);
                    var compliance = await _backendConfigurationPnDbContext.Compliances.SingleOrDefaultAsync(x => x.PlanningId == itemPlanning.Id);
                    if (compliance != null)
                    {
                        await compliance.Delete(_backendConfigurationPnDbContext);
                    }
                    // var property = await _backendConfigurationPnDbContext.Properties.SingleAsync(x => x.Id == compliance.PropertyId);
                    if (_backendConfigurationPnDbContext.Compliances.Any(x => x.PropertyId == property.Id && x.Deadline < DateTime.UtcNow && x.WorkflowState != Constants.WorkflowStates.Removed))
                    {
                        property.ComplianceStatusThirty = 2;
                        property.ComplianceStatus = 2;
                    }
                    else
                    {
                        if (!_backendConfigurationPnDbContext.Compliances.Any(x =>
                                x.PropertyId == property.Id && x.WorkflowState != Constants.WorkflowStates.Removed))
                        {
                            property.ComplianceStatusThirty = 0;
                            property.ComplianceStatus = 0;
                        }
                    }

                    await property.Update(_backendConfigurationPnDbContext);

                    var areaRulePlanning = await _backendConfigurationPnDbContext.AreaRulePlannings
                        .Where(x => x.Id == planningSite.ArpId)
                        .SingleAsync();
                    areaRulePlanning.ItemPlanningId = 0;
                    areaRulePlanning.Status = false;
                    await areaRulePlanning.Update(_backendConfigurationPnDbContext);
                }
            }
        }

    }
}