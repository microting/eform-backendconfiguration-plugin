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
using BackendConfiguration.Pn.Services.RebusService;
using Microsoft.Extensions.Logging;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.eFormCaseTemplateBase.Infrastructure.Data;
using Microting.ItemsPlanningBase.Infrastructure.Data;
using Microting.TimePlanningBase.Infrastructure.Data;
using Rebus.Bus;
using Sentry;

namespace BackendConfiguration.Pn.Services.BackendConfigurationAssignmentWorkerService;

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

public class BackendConfigurationAssignmentWorkerService(
    IEFormCoreService coreHelper,
    IUserService userService,
    BackendConfigurationPnDbContext backendConfigurationPnDbContext,
    IBackendConfigurationLocalizationService backendConfigurationLocalizationService,
    ItemsPlanningPnDbContext itemsPlanningPnDbContext,
    TimePlanningPnDbContext timePlanningDbContext,
    CaseTemplatePnDbContext caseTemplatePnDbContext,
    IRebusService rebusService,
    ILogger<BackendConfigurationAssignmentWorkerService> logger)
    : IBackendConfigurationAssignmentWorkerService
{
    private readonly IBus _bus = rebusService.GetBus();

    public async Task<OperationDataResult<List<PropertyAssignWorkersModel>>> GetPropertiesAssignment(List<int> propertyIds)
    {
        try
        {
            var core = await coreHelper.GetCore().ConfigureAwait(false);
            var sdkDbContext = core.DbContextHelper.GetDbContext();
            var assignWorkersModels = new List<PropertyAssignWorkersModel>();
            var query = backendConfigurationPnDbContext.PropertyWorkers.AsQueryable();
            query = query
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed);

            if (propertyIds != null && propertyIds.Any())
            {
                query = query.Where(x => propertyIds.Contains(x.PropertyId));
            }

            if (query.Count() > 0)
            {
                var listWorkerId = await query.Select(x => new PropertyWorker
                {
                    WorkerId = x.WorkerId,
                    TaskManagementEnabled = x.TaskManagementEnabled
                }).Distinct().ToListAsync().ConfigureAwait(false);

                foreach (var workerId in listWorkerId)
                {
                    var assignments = await query
                        .Where(x => x.WorkerId == workerId.WorkerId)
                        .Select(x => new PropertyAssignmentWorkerModel
                            { PropertyId = x.PropertyId, IsChecked = true })
                        .ToListAsync().ConfigureAwait(false);

                    foreach (var assignmentWorkerModel in assignments)
                    {
                        var numberOfAssignements = await backendConfigurationPnDbContext.AreaRulePlannings
                            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                            .Where(x => x.Status)
                            .Where(x => x.PropertyId == assignmentWorkerModel.PropertyId)
                            .Where(x => x.PlanningSites.Where(y => y.WorkflowState != Constants.WorkflowStates.Removed && y.SiteId == workerId.WorkerId).Select(y => y.SiteId).Any())
                            .CountAsync().ConfigureAwait(false);

                        assignmentWorkerModel.IsLocked = numberOfAssignements > 0;
                        assignmentWorkerModel.NumberOfTasksAssigned = numberOfAssignements;

                        var siteName = await sdkDbContext.Sites
                            .Where(x => x.Id == workerId.WorkerId)
                            .Select(x => x.Name)
                            .SingleOrDefaultAsync().ConfigureAwait(false);


                        var numberOfWorkOrderCases = await backendConfigurationPnDbContext.WorkorderCases
                            .Join(backendConfigurationPnDbContext.PropertyWorkers,
                                workorderCase => workorderCase.PropertyWorkerId,
                                propertyWorker => propertyWorker.Id,
                                (workorderCase, propertyWorker) => new
                                {
                                    workorderCase.Id,
                                    workorderCase.LeadingCase,
                                    workorderCase.WorkflowState,
                                    workorderCase.LastAssignedToName,
                                    workorderCase.CaseStatusesEnum,
                                    propertyWorker.PropertyId
                                })
                            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                            .Where(x => x.CaseStatusesEnum != CaseStatusesEnum.Completed)
                            .Where(x => x.LeadingCase == true)
                            .Where(x => x.LastAssignedToName == siteName)
                            .Where(x => x.PropertyId == assignmentWorkerModel.PropertyId)
                            .ToListAsync();

                        assignmentWorkerModel.IsLocked = assignmentWorkerModel.IsLocked ? assignmentWorkerModel.IsLocked : numberOfWorkOrderCases.Count() > 0;
                        assignmentWorkerModel.NUmberOfWorkOrderCasesAssigned = numberOfWorkOrderCases.Count();

                    }

                    assignWorkersModels.Add(new PropertyAssignWorkersModel
                        { SiteId = workerId.WorkerId, Assignments = assignments, TaskManagementEnabled = workerId.TaskManagementEnabled });
                }

                var properties = await backendConfigurationPnDbContext.Properties
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
            SentrySdk.CaptureException(ex);
            logger.LogError(ex.Message);
            logger.LogTrace(ex.StackTrace);
            return new OperationDataResult<List<PropertyAssignWorkersModel>>(false,
                $"{backendConfigurationLocalizationService.GetString("ErrorWhileObtainingAssignmentsProperties")}: {ex.Message}");
        }
    }
    public async Task<OperationDataResult<List<PropertyAssignWorkersModel>>> GetSimplePropertiesAssignment(List<int> propertyIds)
    {
        try
        {
            var core = await coreHelper.GetCore().ConfigureAwait(false);
            var sdkDbContext = core.DbContextHelper.GetDbContext();
            var assignWorkersModels = new List<PropertyAssignWorkersModel>();
            var query = backendConfigurationPnDbContext.PropertyWorkers.AsQueryable();
            query = query
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed);

            if (propertyIds != null && propertyIds.Any())
            {
                query = query.Where(x => propertyIds.Contains(x.PropertyId));
            }

            if (query.Count() > 0)
            {
                var listWorkerId = await query.Select(x => new PropertyWorker
                {
                    WorkerId = x.WorkerId,
                    TaskManagementEnabled = x.TaskManagementEnabled
                }).Distinct().ToListAsync().ConfigureAwait(false);

                foreach (var workerId in listWorkerId)
                {
                    var assignments = await query
                        .Where(x => x.WorkerId == workerId.WorkerId)
                        .Select(x => new PropertyAssignmentWorkerModel
                            { PropertyId = x.PropertyId, IsChecked = true })
                        .ToListAsync().ConfigureAwait(false);

                    assignWorkersModels.Add(new PropertyAssignWorkersModel
                        { SiteId = workerId.WorkerId, Assignments = assignments, TaskManagementEnabled = workerId.TaskManagementEnabled });
                }

                var properties = await backendConfigurationPnDbContext.Properties
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
            SentrySdk.CaptureException(ex);
            logger.LogError(ex.Message);
            logger.LogTrace(ex.StackTrace);
            return new OperationDataResult<List<PropertyAssignWorkersModel>>(false,
                $"{backendConfigurationLocalizationService.GetString("ErrorWhileObtainingAssignmentsProperties")}: {ex.Message}");
        }
    }

    public async Task<OperationResult> Create(PropertyAssignWorkersModel createModel)
    {
        var core = await coreHelper.GetCore();

        var result = await BackendConfigurationAssignmentWorkerServiceHelper
            .Create(createModel, core, userService, backendConfigurationPnDbContext,
                caseTemplatePnDbContext, backendConfigurationLocalizationService, _bus)
            .ConfigureAwait(false);

        return new OperationResult(result.Success, backendConfigurationLocalizationService.GetString(result.Message));
    }

    public async Task<OperationResult> Update(PropertyAssignWorkersModel updateModel)
    {
        var core = await coreHelper.GetCore().ConfigureAwait(false);
        var result = await BackendConfigurationAssignmentWorkerServiceHelper
            .Update(updateModel, core, userService, backendConfigurationPnDbContext, caseTemplatePnDbContext,
                backendConfigurationLocalizationService, _bus, itemsPlanningPnDbContext)
            .ConfigureAwait(false);

        return new OperationResult(result.Success, backendConfigurationLocalizationService.GetString(result.Message));
    }

    public async Task<OperationResult> Delete(int deviceUserId)
    {
        try
        {
            var core = await coreHelper.GetCore().ConfigureAwait(false);
            var sdkDbContext = core.DbContextHelper.GetDbContext();
            var propertyWorkers = await backendConfigurationPnDbContext.PropertyWorkers
                .Where(x => x.WorkerId == deviceUserId)
                .ToListAsync().ConfigureAwait(false);
            var site = await sdkDbContext.Sites
                .Where(x => x.Id == deviceUserId)
                .SingleOrDefaultAsync().ConfigureAwait(false);

            foreach (var propertyAssignment in propertyWorkers)
            {
                propertyAssignment.UpdatedByUserId = userService.UserId;
                await propertyAssignment.Delete(backendConfigurationPnDbContext).ConfigureAwait(false);
                if (propertyAssignment.EntityItemId != null)
                {
                    await core.EntityItemDelete((int)propertyAssignment.EntityItemId).ConfigureAwait(false);
                }

                var property = await backendConfigurationPnDbContext.Properties
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
                await BackendConfigurationAssignmentWorkerServiceHelper
                    .DeleteAllEntriesForPropertyAssignment(propertyAssignment, core, property, sdkDbContext,
                        caseTemplatePnDbContext, backendConfigurationPnDbContext, itemsPlanningPnDbContext)
                    .ConfigureAwait(false);
            }

            await WorkOrderHelper.RetractEform(propertyWorkers, true, core, userService.UserId, backendConfigurationPnDbContext).ConfigureAwait(false);
            await WorkOrderHelper.RetractEform(propertyWorkers, false, core, userService.UserId, backendConfigurationPnDbContext).ConfigureAwait(false);
            await WorkOrderHelper.RetractEform(propertyWorkers, false, core, userService.UserId, backendConfigurationPnDbContext).ConfigureAwait(false);
            foreach (var assignment in propertyWorkers)
            {
                await assignment.Delete(backendConfigurationPnDbContext).ConfigureAwait(false);
            }

            if (timePlanningDbContext.AssignedSites.Any(x => x.SiteId == site.MicrotingUid && x.WorkflowState != Constants.WorkflowStates.Removed))
            {
                var assignmentForDeletes = await timePlanningDbContext.AssignedSites.Where(x =>
                    x.SiteId == site.MicrotingUid && x.WorkflowState != Constants.WorkflowStates.Removed).ToListAsync().ConfigureAwait(false);

                foreach (var assignmentForDelete in assignmentForDeletes)
                {
                    await assignmentForDelete.Delete(timePlanningDbContext).ConfigureAwait(false);
                    if (assignmentForDelete.CaseMicrotingUid != null)
                    {
                        await core.CaseDelete((int) assignmentForDelete.CaseMicrotingUid).ConfigureAwait(false);
                    }
                }
            }

            return new OperationResult(true,
                backendConfigurationLocalizationService.GetString("SuccessfullyDeleteAssignmentsProperties"));
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            logger.LogError(e.Message);
            logger.LogTrace(e.StackTrace);
            return new OperationResult(false,
                $"{backendConfigurationLocalizationService.GetString("ErrorWhilDeleteAssignmentsProperties")}: {e.Message}");
        }
    }

    public async Task<OperationDataResult<List<DeviceUserModel>>> IndexDeviceUser(PropertyWorkersFiltrationModel requestModel)
    {
        if (requestModel.Sort == "MicrotingUid")
        {
            requestModel.Sort = "SiteMicrotingUid";
        }

        try
        {
            var core = await coreHelper.GetCore().ConfigureAwait(false);
            var sdkDbContext = core.DbContextHelper.GetDbContext();
            var sitesQuery = from site in sdkDbContext.Sites
                join siteWorker in sdkDbContext.SiteWorkers on site.Id equals siteWorker.SiteId
                join worker in sdkDbContext.Workers on siteWorker.WorkerId equals worker.Id
                select new
                {
                    site.Name,
                    worker.EmployeeNo,
                    site.LanguageId,
                    site.Id,
                    site.MicrotingUid,
                    site.IsLocked,
                    WorkerUid = worker.MicrotingUid,
                    UserFirstName = worker.FirstName,
                    UserLastName = worker.LastName,
                    site.WorkflowState

                };
            sitesQuery = sitesQuery.Where(x => x.WorkflowState != Constants.WorkflowStates.Removed);

            var deviceUsers = await sitesQuery
                .Select(x => new DeviceUserModel
                {
                    UserFirstName = x.UserFirstName,
                    UserLastName = x.UserLastName,
                    EmployeeNo = x.EmployeeNo,
                    WorkerUid = x.WorkerUid,
                    LanguageId = x.LanguageId,
                    SiteId = x.Id,
                    SiteUid = x.MicrotingUid,
                    SiteName = x.Name,
                    Language = sdkDbContext.Languages.Where(y => y.Id == x.LanguageId).Select(y => y.Name).SingleOrDefault() ?? "Danish",
                    LanguageCode = sdkDbContext.Languages.Where(y => y.Id == x.LanguageId).Select(y => y.LanguageCode).SingleOrDefault() ?? "da",
                    IsLocked = x.IsLocked
                })
                .ToListAsync().ConfigureAwait(false);

            var timeRegistrationEnabledSites =await
                timePlanningDbContext.AssignedSites.Where(x => x.WorkflowState != Constants.WorkflowStates.Removed).ToListAsync().ConfigureAwait(false);

            foreach (var deviceUserModel in deviceUsers)
            {
                var unit = await sdkDbContext.Units
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .FirstOrDefaultAsync(x => x.SiteId == deviceUserModel.SiteId);
                if (unit != null)
                {
                    deviceUserModel.Version = unit.eFormVersion;
                    deviceUserModel.OsVersion = unit.OsVersion;
                    deviceUserModel.Model = unit.Model;
                    deviceUserModel.Manufacturer = unit.Manufacturer;
                    deviceUserModel.CustomerNo = unit.CustomerNo;
                    deviceUserModel.OtpCode = unit.OtpCode;
                    deviceUserModel.UnitId = unit.MicrotingUid;
                }

                deviceUserModel.PinCode = "****";
                deviceUserModel.TimeRegistrationEnabled =
                    timeRegistrationEnabledSites.Any(x => x.SiteId == deviceUserModel.SiteUid);
                if (deviceUserModel.TimeRegistrationEnabled != false)
                {
                    var assignedSite = timeRegistrationEnabledSites
                        .First(x => x.SiteId == deviceUserModel.SiteUid);
                    deviceUserModel.StartMonday = assignedSite.StartMonday;
                    deviceUserModel.EndMonday = assignedSite.EndMonday;
                    deviceUserModel.BreakMonday = assignedSite.BreakMonday;
                    deviceUserModel.StartTuesday = assignedSite.StartTuesday;
                    deviceUserModel.EndTuesday = assignedSite.EndTuesday;
                    deviceUserModel.BreakTuesday = assignedSite.BreakTuesday;
                    deviceUserModel.StartWednesday = assignedSite.StartWednesday;
                    deviceUserModel.EndWednesday = assignedSite.EndWednesday;
                    deviceUserModel.BreakWednesday = assignedSite.BreakWednesday;
                    deviceUserModel.StartThursday = assignedSite.StartThursday;
                    deviceUserModel.EndThursday = assignedSite.EndThursday;
                    deviceUserModel.BreakThursday = assignedSite.BreakThursday;
                    deviceUserModel.StartFriday = assignedSite.StartFriday;
                    deviceUserModel.EndFriday = assignedSite.EndFriday;
                    deviceUserModel.BreakFriday = assignedSite.BreakFriday;
                    deviceUserModel.StartSaturday = assignedSite.StartSaturday;
                    deviceUserModel.EndSaturday = assignedSite.EndSaturday;
                    deviceUserModel.BreakSaturday = assignedSite.BreakSaturday;
                    deviceUserModel.StartSunday = assignedSite.StartSunday;
                    deviceUserModel.EndSunday = assignedSite.EndSunday;
                    deviceUserModel.BreakSunday = assignedSite.BreakSunday;
                }

                deviceUserModel.TaskManagementEnabled = backendConfigurationPnDbContext.PropertyWorkers.Any(x =>
                    x.WorkflowState != Constants.WorkflowStates.Removed
                    && x.WorkerId == deviceUserModel.SiteId
                    && x.TaskManagementEnabled == true);

                var propertyIds = await backendConfigurationPnDbContext.PropertyWorkers
                    .Where(x =>
                        x.WorkerId == deviceUserModel.SiteId
                        && x.WorkflowState != Constants.WorkflowStates.Removed).Select(x => x.PropertyId).ToListAsync().ConfigureAwait(false);

                var properties = backendConfigurationPnDbContext.Properties
                    .Where(x => propertyIds.Contains(x.Id))
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .OrderBy(x => x.Name)
                    .Select(x => x.Name)
                    .ToList();

                deviceUserModel.PropertyNames = string.Join(", ", properties);
                deviceUserModel.PropertyIds = propertyIds;

                var numberOfAssignements = await backendConfigurationPnDbContext.AreaRulePlannings
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Status)
                    .Where(x => propertyIds.Contains(x.PropertyId))
                    .Where(x => x.PlanningSites.Where(y => y.WorkflowState != Constants.WorkflowStates.Removed && y.SiteId == deviceUserModel.SiteId).Select(y => y.SiteId).Any())
                    .CountAsync().ConfigureAwait(false);

                deviceUserModel.IsBackendUser = deviceUserModel.IsLocked;

                deviceUserModel.IsLocked = deviceUserModel.IsLocked ? deviceUserModel.IsLocked : numberOfAssignements > 0;

                var numberOfWorkOrderCases = await backendConfigurationPnDbContext.WorkorderCases
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.CaseStatusesEnum != CaseStatusesEnum.Completed)
                    .Where(x => x.LeadingCase == true)
                    .Where(x => x.LastAssignedToName == deviceUserModel.SiteName)
                    .CountAsync();

                deviceUserModel.IsLocked = deviceUserModel.IsLocked ? deviceUserModel.IsLocked : numberOfWorkOrderCases > 0;
                deviceUserModel.HasWorkOrdersAssigned = numberOfWorkOrderCases > 0;
            }

            if (requestModel.PropertyIds != null)
            {
                if (requestModel.PropertyIds.Any())
                {
                    deviceUsers = deviceUsers
                        .Where(x => x.PropertyIds.Any(y => requestModel.PropertyIds.Contains(y))).ToList();
                }
            }

            // Convert deviceUsers to IQueryable
            var deviceUsersQuery = deviceUsers.AsQueryable();

            try
            {
                deviceUsersQuery = QueryHelper.AddFilterAndSortToQuery(deviceUsersQuery, requestModel, new List<string> { "SiteName" });
            }
            catch (Exception e)
            {
                SentrySdk.CaptureException(e);
                logger.LogError(e.Message);
                logger.LogTrace(e.StackTrace);
            }

            deviceUsers = deviceUsersQuery.ToList();


            return new OperationDataResult<List<DeviceUserModel>>(true, deviceUsers);
        }
        catch (Exception ex)
        {
            SentrySdk.CaptureException(ex);
            logger.LogError(ex.Message);
            logger.LogTrace(ex.StackTrace);
            return new OperationDataResult<List<DeviceUserModel>>(false, backendConfigurationLocalizationService.GetStringWithFormat("ErrorWhileGetDeviceUsers") + " " + ex.Message);
        }
    }

    public async Task<OperationResult> UpdateDeviceUser(DeviceUserModel deviceUserModel)
    {
        var core = await coreHelper.GetCore().ConfigureAwait(false);
        var result = await BackendConfigurationAssignmentWorkerServiceHelper.UpdateDeviceUser(deviceUserModel, core,
            userService.UserId, backendConfigurationPnDbContext,
            timePlanningDbContext, logger);

        return new OperationResult(result.Success, backendConfigurationLocalizationService.GetString(result.Message));
    }

    public async Task<OperationDataResult<int>> CreateDeviceUser(DeviceUserModel deviceUserModel)
    {
        var core = await coreHelper.GetCore().ConfigureAwait(false);
        var result = await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core,
            userService.UserId,
            timePlanningDbContext);

        return new OperationDataResult<int>(result.Success,
            backendConfigurationLocalizationService.GetString(result.Message), result.Model);
    }

    public async Task<OperationResult> UpdateSimplifiedDeviceUser(SimpleDeviceUserModel deviceUserModel)
    {
        var core = await coreHelper.GetCore().ConfigureAwait(false);
        var result = await BackendConfigurationAssignmentWorkerServiceHelper.UpdateSimplifiedDeviceUser(deviceUserModel, core,
            userService.UserId, backendConfigurationPnDbContext,
            timePlanningDbContext);

        return new OperationResult(result.Success, backendConfigurationLocalizationService.GetString(result.Message));
    }
}