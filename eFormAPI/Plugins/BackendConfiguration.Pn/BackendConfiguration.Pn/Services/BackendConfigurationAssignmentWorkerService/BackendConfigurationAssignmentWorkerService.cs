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
using BackendConfiguration.Pn.Messages;
using BackendConfiguration.Pn.Services.RebusService;
using eFormCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure;
using Microting.eFormApi.BasePn.Infrastructure.Helpers.PluginDbOptions;
using Microting.eFormApi.BasePn.Infrastructure.Models.Common;
using Microting.eFormCaseTemplateBase.Infrastructure.Data;
using Microting.ItemsPlanningBase.Infrastructure.Data;
using Microting.TimePlanningBase.Infrastructure.Data;
using Microting.TimePlanningBase.Infrastructure.Data.Entities;
using Microting.TimePlanningBase.Infrastructure.Data.Models;
using Rebus.Bus;

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
        private readonly CaseTemplatePnDbContext _caseTemplatePnDbContext;
        // private readonly ILogger<BackendConfigurationAssignmentWorkerService> _logger;
        // private readonly IPluginDbOptions<TimePlanningBaseSettings> _options;
        private readonly IBus _bus;

        public BackendConfigurationAssignmentWorkerService(
            IEFormCoreService coreHelper,
            IUserService userService,
            BackendConfigurationPnDbContext backendConfigurationPnDbContext,
            IBackendConfigurationLocalizationService backendConfigurationLocalizationService, ItemsPlanningPnDbContext itemsPlanningPnDbContext, TimePlanningPnDbContext timePlanningDbContext, CaseTemplatePnDbContext caseTemplatePnDbContext, IRebusService rebusService)
        {
            _coreHelper = coreHelper;
            _userService = userService;
            _backendConfigurationLocalizationService = backendConfigurationLocalizationService;
            _itemsPlanningPnDbContext = itemsPlanningPnDbContext;
            // _deviceUsersService = deviceUsersService;
            _timePlanningDbContext = timePlanningDbContext;
            _caseTemplatePnDbContext = caseTemplatePnDbContext;
            _backendConfigurationPnDbContext = backendConfigurationPnDbContext;
            _workOrderHelper = new WorkOrderHelper(_coreHelper, _backendConfigurationPnDbContext, _backendConfigurationLocalizationService, _userService);
            _bus = rebusService.GetBus();
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
                    var listWorkerId = await query.Select(x => x.WorkerId).Distinct().ToListAsync().ConfigureAwait(false);

                    foreach (var workerId in listWorkerId)
                    {
                        var assignments = await query
                            .Where(x => x.WorkerId == workerId)
                            .Select(x => new PropertyAssignmentWorkerModel
                                { PropertyId = x.PropertyId, IsChecked = true })
                            .ToListAsync().ConfigureAwait(false);
                        assignWorkersModels.Add(new PropertyAssignWorkersModel
                            { SiteId = workerId, Assignments = assignments });
                    }

                    var properties = await _backendConfigurationPnDbContext.Properties
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Select(x => new PropertyAssignmentWorkerModel { PropertyId = x.Id, IsChecked = false })
                        .ToListAsync().ConfigureAwait(false);

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
                var core = await _coreHelper.GetCore().ConfigureAwait(false);
                List<PropertyWorker> propertyWorkers = new List<PropertyWorker>();
                List<int> documentIds = new List<int>();
                foreach (var propertyAssignment in createModel.Assignments
                             .Select(propertyAssignmentWorkerModel => new PropertyWorker
                             {
                                 WorkerId = createModel.SiteId,
                                 PropertyId = propertyAssignmentWorkerModel.PropertyId,
                                 CreatedByUserId = _userService.UserId,
                                 UpdatedByUserId = _userService.UserId
                             }))
                {
                    await propertyAssignment.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);
                    var documents = await _caseTemplatePnDbContext.DocumentProperties.Where(x => x.PropertyId == propertyAssignment.PropertyId).ToListAsync();
                    foreach (var document in documents)
                    {
                        if (!documentIds.Contains(document.DocumentId))
                        {
                            documentIds.Add(document.Id);
                        }
                    }
                    propertyWorkers.Add(propertyAssignment);

                }
                foreach (var documentId in documentIds)
                {
                    var document = await _caseTemplatePnDbContext.Documents
                        .Include(x => x.DocumentSites)
                        .FirstAsync(x => x.Id == documentId).ConfigureAwait(false);
                    foreach (var documentSite in document.DocumentSites.Where(x => x.WorkflowState != Constants.WorkflowStates.Removed))
                    {
                        if (documentSite.SdkCaseId != 0)
                        {
                            await core.CaseDelete(documentSite.SdkCaseId);
                        }

                        await documentSite.Delete(_caseTemplatePnDbContext);
                    }

                    await _bus.SendLocal(new DocumentUpdated(documentId)).ConfigureAwait(false);
                }

                await _workOrderHelper.WorkorderFlowDeployEform(propertyWorkers).ConfigureAwait(false);

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
                var core = await _coreHelper.GetCore().ConfigureAwait(false);
                var sdkDbContext = core.DbContextHelper.GetDbContext();
                updateModel.Assignments = updateModel.Assignments.Where(x => x.IsChecked).ToList();

                var assignments = await _backendConfigurationPnDbContext.PropertyWorkers
                    .Where(x => x.WorkerId == updateModel.SiteId)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .ToListAsync().ConfigureAwait(false);

                var assignmentsForCreate = updateModel.Assignments
                    .Select(x => x.PropertyId)
                    .Where(x => !assignments.Select(y => y.PropertyId).Contains(x))
                    .ToList();
                List<PropertyWorker> propertyWorkers = new List<PropertyWorker>();
                List<int> documentIds = new List<int>();

                foreach (var propertyAssignment in assignmentsForCreate
                             .Select(propertyAssignmentWorkerModel => new PropertyWorker
                             {
                                 WorkerId = updateModel.SiteId,
                                 PropertyId = propertyAssignmentWorkerModel,
                                 CreatedByUserId = _userService.UserId,
                                 UpdatedByUserId = _userService.UserId
                             }))
                {
                    await propertyAssignment.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);

                    var documents = await _caseTemplatePnDbContext.DocumentProperties.Where(x => x.PropertyId == propertyAssignment.PropertyId).ToListAsync();
                    foreach (var document in documents)
                    {
                        if (!documentIds.Contains(document.DocumentId))
                        {
                            documentIds.Add(document.Id);
                        }
                    }
                    propertyWorkers.Add(propertyAssignment);
                }

                var assignmentsForDelete = assignments
                    .Where(x => !updateModel.Assignments.Select(y => y.PropertyId).Contains(x.PropertyId))
                    .ToList();

                foreach (var propertyAssignment in assignmentsForDelete)
                {
                    propertyAssignment.UpdatedByUserId = _userService.UserId;
                    await propertyAssignment.Delete(_backendConfigurationPnDbContext).ConfigureAwait(false);
                    if (propertyAssignment.EntityItemId != null)
                    {
                        await core.EntityItemDelete((int)propertyAssignment.EntityItemId).ConfigureAwait(false);
                    }

                    var property = await _backendConfigurationPnDbContext.Properties
                        .Where(x => x.Id == propertyAssignment.PropertyId)
                        .SingleAsync().ConfigureAwait(false);

                    var entityItems = await sdkDbContext.EntityItems
                        .Where(x => x.EntityGroupId == property.EntitySelectListDeviceUsers)
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .OrderBy(x => x.Name)
                        .AsNoTracking()
                        .ToListAsync().ConfigureAwait(false);

                    int entityItemIncrementer = 0;
                    foreach (var entity in entityItems)
                    {
                        await core.EntityItemUpdate(entity.Id, entity.Name, entity.Description,
                            entity.EntityItemUid, entityItemIncrementer).ConfigureAwait(false);
                        entityItemIncrementer++;
                    }

                    await DeleteAllEntriesForPropertyAssignment(propertyAssignment, core, property, sdkDbContext).ConfigureAwait(false);
                }

                if(assignmentsForDelete.Any())
                {
                    await _workOrderHelper.RetractEform(assignmentsForDelete, true).ConfigureAwait(false);
                    await _workOrderHelper.RetractEform(assignmentsForDelete, false).ConfigureAwait(false);
                    await _workOrderHelper.RetractEform(assignmentsForDelete, false).ConfigureAwait(false);

                    foreach (var propertyWorker in assignmentsForDelete)
                    {
                        var documentSites = await _caseTemplatePnDbContext.DocumentSites.Where(x => x.PropertyId == propertyWorker.PropertyId
                        && x.SdkSiteId == propertyWorker.WorkerId).ToListAsync();
                        foreach (var documentSite in documentSites)
                        {
                            if (documentSite.SdkCaseId != 0)
                            {
                                await core.CaseDelete(documentSite.SdkCaseId);
                            }
                        }
                    }

                }

                foreach (var documentId in documentIds)
                {
                    var document = await _caseTemplatePnDbContext.Documents
                        .Include(x => x.DocumentSites)
                        .FirstAsync(x => x.Id == documentId).ConfigureAwait(false);
                    foreach (var documentSite in document.DocumentSites.Where(x => x.WorkflowState != Constants.WorkflowStates.Removed))
                    {
                        if (documentSite.SdkCaseId != 0)
                        {
                            await core.CaseDelete(documentSite.SdkCaseId);
                        }

                        await documentSite.Delete(_caseTemplatePnDbContext);
                    }

                    await _bus.SendLocal(new DocumentUpdated(documentId)).ConfigureAwait(false);
                }

                await _workOrderHelper.WorkorderFlowDeployEform(propertyWorkers).ConfigureAwait(false);
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
                var core = await _coreHelper.GetCore().ConfigureAwait(false);
                var sdkDbContext = core.DbContextHelper.GetDbContext();
                var propertyWorkers = await _backendConfigurationPnDbContext.PropertyWorkers
                    // .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.WorkerId == deviceUserId)
                    .ToListAsync().ConfigureAwait(false);

                var eformIdForNewTasks = await sdkDbContext.CheckListTranslations
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Text == "01. New task")
                    .Select(x => x.CheckListId)
                    .FirstAsync().ConfigureAwait(false);

                var eformIdForOngoingTasks = await sdkDbContext.CheckListTranslations
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Text == "02. Ongoing task")
                    .Select(x => x.CheckListId)
                    .FirstAsync().ConfigureAwait(false);

                var eformIdForCompletedTasks = await sdkDbContext.CheckListTranslations
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Text == "03. Completed task")
                    .Select(x => x.CheckListId)
                    .FirstAsync().ConfigureAwait(false);


                foreach (var propertyAssignment in propertyWorkers)
                {
                    propertyAssignment.UpdatedByUserId = _userService.UserId;
                    await propertyAssignment.Delete(_backendConfigurationPnDbContext).ConfigureAwait(false);
                    if (propertyAssignment.EntityItemId != null)
                    {
                        await core.EntityItemDelete((int)propertyAssignment.EntityItemId).ConfigureAwait(false);
                    }

                    var property = await _backendConfigurationPnDbContext.Properties
                        .Where(x => x.Id == propertyAssignment.PropertyId)
                        .SingleOrDefaultAsync().ConfigureAwait(false);

                    var entityItems = await sdkDbContext.EntityItems
                        .Where(x => x.EntityGroupId == property.EntitySelectListDeviceUsers)
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .OrderBy(x => x.Name)
                        .AsNoTracking()
                        .ToListAsync().ConfigureAwait(false);

                    int entityItemIncrementer = 0;
                    foreach (var entity in entityItems)
                    {
                        await core.EntityItemUpdate(entity.Id, entity.Name, entity.Description,
                            entity.EntityItemUid, entityItemIncrementer).ConfigureAwait(false);
                        entityItemIncrementer++;
                    }
                    await DeleteAllEntriesForPropertyAssignment(propertyAssignment, core, property, sdkDbContext).ConfigureAwait(false);
                }

                await _workOrderHelper.RetractEform(propertyWorkers, true).ConfigureAwait(false);
                await _workOrderHelper.RetractEform(propertyWorkers, false).ConfigureAwait(false);
                await _workOrderHelper.RetractEform(propertyWorkers, false).ConfigureAwait(false);
                foreach (var assignment in propertyWorkers)
                {
                    await assignment.Delete(_backendConfigurationPnDbContext).ConfigureAwait(false);
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
                var core = await _coreHelper.GetCore().ConfigureAwait(false);
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
                    .ToListAsync().ConfigureAwait(false);

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
                var core = await _coreHelper.GetCore().ConfigureAwait(false);
                var sdkDbContext = core.DbContextHelper.GetDbContext();
                await using var _ = sdkDbContext.ConfigureAwait(false);
                var language = sdkDbContext.Languages.Single(x => x.LanguageCode == deviceUserModel.LanguageCode);
                var siteDto = await core.SiteRead(deviceUserModel.Id).ConfigureAwait(false);
                if (siteDto.WorkerUid != null)
                {
                    // var workerDto = await core.Advanced_WorkerRead((int)siteDto.WorkerUid);
                    var worker = await sdkDbContext.Workers.SingleOrDefaultAsync(x => x.MicrotingUid == siteDto.WorkerUid).ConfigureAwait(false);
                    if (worker != null)
                    {
                        var fullName = deviceUserModel.UserFirstName + " " + deviceUserModel.UserLastName;
                        var isUpdated = await core.SiteUpdate(deviceUserModel.Id, fullName, deviceUserModel.UserFirstName,
                            deviceUserModel.UserLastName, worker.Email, deviceUserModel.LanguageCode).ConfigureAwait(false);

                        if (isUpdated)
                        {
                            var propertyWorkers = await _backendConfigurationPnDbContext.PropertyWorkers
                                .Where(x => x.WorkerId == worker.Id)
                                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                .ToListAsync().ConfigureAwait(false);

                            int? propertyId = null;
                            foreach (var propertyWorker in propertyWorkers)
                            {
                                var et = sdkDbContext.EntityItems.Single(x => x.Id == propertyWorker.EntityItemId);
                                await core.EntityItemUpdate((int)propertyWorker.EntityItemId, fullName, "", et.EntityItemUid,
                                    et.DisplayIndex).ConfigureAwait(false);
                                propertyId = propertyWorker.PropertyId;
                            }

                            if (propertyId != null)
                            {
                                var property = await _backendConfigurationPnDbContext.Properties.SingleAsync(x => x.Id == propertyId).ConfigureAwait(false);

                                var entityItems = await sdkDbContext.EntityItems
                                    .Where(x => x.EntityGroupId == property.EntitySelectListDeviceUsers)
                                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                    .OrderBy(x => x.Name)
                                    .AsNoTracking()
                                    .ToListAsync().ConfigureAwait(false);

                                int entityItemIncrementer = 0;
                                foreach (var entityItem in entityItems)
                                {
                                    await core.EntityItemUpdate(entityItem.Id, entityItem.Name, entityItem.Description,
                                        entityItem.EntityItemUid, entityItemIncrementer).ConfigureAwait(false);
                                    entityItemIncrementer++;
                                }
                            }
                        }
                        //var siteId = await sdkDbContext.Sites.Where(x => x.MicrotingUid == siteDto.SiteId).Select(x => x.Id).FirstAsync();
                        if (deviceUserModel.TimeRegistrationEnabled == false && _timePlanningDbContext.AssignedSites.Any(x => x.SiteId == siteDto.SiteId && x.WorkflowState != Constants.WorkflowStates.Removed))
                        {
                            var assignmentForDelete = await _timePlanningDbContext.AssignedSites.SingleAsync(x =>
                                x.SiteId == siteDto.SiteId && x.WorkflowState != Constants.WorkflowStates.Removed).ConfigureAwait(false);

                            if (assignmentForDelete.CaseMicrotingUid != null)
                            {
                                await core.CaseDelete((int) assignmentForDelete.CaseMicrotingUid).ConfigureAwait(false);
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
                                    await assignmentSite.Create(_timePlanningDbContext).ConfigureAwait(false);
                                    // var option =
                                    var newTaskId = await _timePlanningDbContext.PluginConfigurationValues.SingleAsync(x => x.Name == "TimePlanningBaseSettings:EformId").ConfigureAwait(false);
                                    var folderId = await _timePlanningDbContext.PluginConfigurationValues.SingleAsync(x => x.Name == "TimePlanningBaseSettings:FolderId").ConfigureAwait(false);
                                    var folder = await sdkDbContext.Folders.SingleAsync(x => x.Id == int.Parse(folderId.Value)).ConfigureAwait(false);
                                    var mainElement = await core.ReadeForm(int.Parse(newTaskId.Value), language).ConfigureAwait(false);
                                    mainElement.CheckListFolderName = folder.MicrotingUid.ToString();
                                    mainElement.EndDate = DateTime.UtcNow.AddYears(10);
                                    mainElement.DisplayOrder = int.MinValue;
                                    mainElement.Repeated = 0;
                                    mainElement.PushMessageTitle = mainElement.Label;
                                    mainElement.EnableQuickSync = true;
                                    var caseId = await core.CaseCreate(mainElement, "", siteDto.SiteId, int.Parse(folderId.Value)).ConfigureAwait(false);
                                    assignmentSite.CaseMicrotingUid = caseId;
                                    await assignmentSite.Update(_timePlanningDbContext).ConfigureAwait(false);

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
            var core = await _coreHelper.GetCore().ConfigureAwait(false);
            var siteName = deviceUserModel.UserFirstName + " " + deviceUserModel.UserLastName;
            var db = core.DbContextHelper.GetDbContext();
            await using var _ = db.ConfigureAwait(false);
            var siteDto = await core.SiteCreate(siteName, deviceUserModel.UserFirstName, deviceUserModel.UserLastName,
                null, deviceUserModel.LanguageCode).ConfigureAwait(false);

            var theCore = await _coreHelper.GetCore().ConfigureAwait(false);
            var sdkDbContext = theCore.DbContextHelper.GetDbContext();
            await using var __ = sdkDbContext.ConfigureAwait(false);
            var siteId = await sdkDbContext.Sites.Where(x => x.MicrotingUid == siteDto.SiteId).Select(x => x.Id).FirstAsync().ConfigureAwait(false);

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
                    await assignmentSite.Create(_timePlanningDbContext).ConfigureAwait(false);
                    // var option =
                    var newTaskId = await _timePlanningDbContext.PluginConfigurationValues.SingleAsync(x => x.Name == "TimePlanningBaseSettings:EformId").ConfigureAwait(false);
                    var folderId = await _timePlanningDbContext.PluginConfigurationValues.SingleAsync(x => x.Name == "TimePlanningBaseSettings:FolderId").ConfigureAwait(false);;
                    var folder = await sdkDbContext.Folders.SingleAsync(x => x.Id == int.Parse(folderId.Value)).ConfigureAwait(false);
                    var site = await sdkDbContext.Sites.SingleOrDefaultAsync(x => x.Id == siteId).ConfigureAwait(false);
                    var language = await sdkDbContext.Languages.SingleAsync(x => x.Id == site.LanguageId).ConfigureAwait(false);
                    var mainElement = await theCore.ReadeForm(int.Parse(newTaskId.Value), language).ConfigureAwait(false);
                    mainElement.CheckListFolderName = folder.MicrotingUid.ToString();
                    mainElement.EndDate = DateTime.UtcNow.AddYears(10);
                    mainElement.DisplayOrder = int.MinValue;
                    mainElement.Repeated = 0;
                    mainElement.PushMessageTitle = mainElement.Label;
                    mainElement.EnableQuickSync = true;
                    var caseId = await theCore.CaseCreate(mainElement, "", (int)site.MicrotingUid, int.Parse(folderId.Value)).ConfigureAwait(false);
                    assignmentSite.CaseMicrotingUid = caseId;
                    await assignmentSite.Update(_timePlanningDbContext).ConfigureAwait(false);

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
                .ToListAsync().ConfigureAwait(false);

            foreach (var planningSite in planningSites)
            {
                var itemPlanningSites = await _itemsPlanningPnDbContext.PlanningSites
                    .SingleOrDefaultAsync(x => x.SiteId == propertyAssignment.WorkerId
                                               && x.PlanningId == planningSite.ItemPlanningId
                                               && x.WorkflowState != Constants.WorkflowStates.Removed).ConfigureAwait(false);

                if (itemPlanningSites != null)
                {
                    await itemPlanningSites.Delete(_itemsPlanningPnDbContext).ConfigureAwait(false);
                }

                var itemPlanningCaseSites = await _itemsPlanningPnDbContext.PlanningCaseSites
                    .Where(x => x.PlanningId == planningSite.ItemPlanningId
                                && x.MicrotingSdkSiteId == propertyAssignment.WorkerId
                                && x.WorkflowState != Constants.WorkflowStates.Removed)
                    .ToListAsync().ConfigureAwait(false);

                foreach (var planningCaseSite in itemPlanningCaseSites)
                {
                    var result =
                        await sdkDbContext.Cases.SingleOrDefaultAsync(x => x.Id == planningCaseSite.MicrotingSdkCaseId).ConfigureAwait(false);
                    if (result is {MicrotingUid: { }})
                    {
                        await core.CaseDelete((int)result.MicrotingUid).ConfigureAwait(false);
                    }
                    else
                    {
                        var clSites = await sdkDbContext.CheckListSites.SingleAsync(x =>
                            x.Id == planningCaseSite.MicrotingCheckListSitId).ConfigureAwait(false);

                        await core.CaseDelete(clSites.MicrotingUid).ConfigureAwait(false);
                    }
                }

                var dbPlanningSite = await _backendConfigurationPnDbContext.PlanningSites
                    .SingleAsync(x => x.Id == planningSite.Id).ConfigureAwait(false);

                await dbPlanningSite.Delete(_backendConfigurationPnDbContext).ConfigureAwait(false);

                itemPlanningCaseSites = await _itemsPlanningPnDbContext.PlanningCaseSites
                    .Where(x => x.PlanningId == planningSite.ItemPlanningId
                                && x.MicrotingSdkSiteId != propertyAssignment.WorkerId
                                && x.WorkflowState != Constants.WorkflowStates.Removed)
                    .ToListAsync().ConfigureAwait(false);

                if (itemPlanningCaseSites.Count == 0)
                {
                    var itemPlanning = await _itemsPlanningPnDbContext.Plannings
                        .SingleAsync(x => x.Id == planningSite.ItemPlanningId).ConfigureAwait(false);

                    await itemPlanning.Delete(_itemsPlanningPnDbContext).ConfigureAwait(false);
                    var compliance = await _backendConfigurationPnDbContext.Compliances.SingleOrDefaultAsync(x => x.PlanningId == itemPlanning.Id).ConfigureAwait(false);
                    if (compliance != null)
                    {
                        await compliance.Delete(_backendConfigurationPnDbContext).ConfigureAwait(false);
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

                    await property.Update(_backendConfigurationPnDbContext).ConfigureAwait(false);

                    var areaRulePlanning = await _backendConfigurationPnDbContext.AreaRulePlannings
                        .Where(x => x.Id == planningSite.ArpId)
                        .SingleAsync().ConfigureAwait(false);
                    areaRulePlanning.ItemPlanningId = 0;
                    areaRulePlanning.Status = false;
                    await areaRulePlanning.Update(_backendConfigurationPnDbContext).ConfigureAwait(false);
                }
            }
        }

    }
}