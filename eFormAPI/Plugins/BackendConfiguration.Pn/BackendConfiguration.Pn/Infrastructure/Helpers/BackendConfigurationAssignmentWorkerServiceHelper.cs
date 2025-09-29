#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models;
using BackendConfiguration.Pn.Infrastructure.Models.AssignmentWorker;
using BackendConfiguration.Pn.Messages;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using eFormCore;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.eFormCaseTemplateBase.Infrastructure.Data;
using Microting.ItemsPlanningBase.Infrastructure.Data;
using Microting.TimePlanningBase.Infrastructure.Data;
using Microting.TimePlanningBase.Infrastructure.Data.Entities;
using Rebus.Bus;
using Microsoft.Extensions.Logging;
using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;

namespace BackendConfiguration.Pn.Infrastructure.Helpers;

public static class BackendConfigurationAssignmentWorkerServiceHelper
{
    public static async Task<OperationResult> Create(PropertyAssignWorkersModel createModel,
        Core core,
        IUserService userService,
        BackendConfigurationPnDbContext backendConfigurationPnDbContext,
        CaseTemplatePnDbContext caseTemplatePnDbContext,
        IBackendConfigurationLocalizationService? localizationService,
        IBus bus)
        {
            try
            {
                List<PropertyWorker> propertyWorkers = [];
                List<int> documentIds = [];
                foreach (var propertyAssignment in createModel.Assignments
                             .Select(propertyAssignmentWorkerModel => new PropertyWorker
                             {
                                 WorkerId = createModel.SiteId,
                                 PropertyId = propertyAssignmentWorkerModel.PropertyId,
                                 CreatedByUserId = userService.UserId,
                                 UpdatedByUserId = userService.UserId,
                             }))
                {
                    propertyAssignment.TaskManagementEnabled = createModel.TaskManagementEnabled;
                    await propertyAssignment.Create(backendConfigurationPnDbContext).ConfigureAwait(false);
                    var documents = await caseTemplatePnDbContext.DocumentProperties
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Where(x => x.PropertyId == propertyAssignment.PropertyId).ToListAsync();
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
                    var document = await caseTemplatePnDbContext.Documents
                        .Include(x => x.DocumentSites)
                        .FirstOrDefaultAsync(x => x.Id == documentId).ConfigureAwait(false);
                    if (document == null)
                    {
                        continue;
                    }
                    foreach (var documentSite in document.DocumentSites.Where(x => x.WorkflowState != Constants.WorkflowStates.Removed))
                    {
                        if (documentSite.SdkCaseId != 0)
                        {
                            await core.CaseDelete(documentSite.SdkCaseId);
                        }

                        await documentSite.Delete(caseTemplatePnDbContext);
                    }

                    await bus.SendLocal(new DocumentUpdated(documentId)).ConfigureAwait(false);
                }

                await WorkOrderHelper.WorkorderFlowDeployEform(propertyWorkers, core, userService,
                    backendConfigurationPnDbContext, localizationService, bus, false).ConfigureAwait(false);

                return new OperationResult(true,"SuccessfullyAssignmentsCreatingProperties");
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                Console.WriteLine(e.StackTrace);
                // Log.LogException(e.Message);
                // Log.LogException(e.StackTrace);
                return new OperationResult(false, "ErrorWhileAssignmentsCreatingProperties");
            }
        }

        public static async Task<OperationResult> Update(PropertyAssignWorkersModel updateModel, Core core,
            IUserService userService, BackendConfigurationPnDbContext backendConfigurationPnDbContext,
            CaseTemplatePnDbContext caseTemplatePnDbContext, IBackendConfigurationLocalizationService? localizationService, IBus bus, ItemsPlanningPnDbContext itemsPlanningPnDbContext)
        {
            try
            {
                var sdkDbContext = core.DbContextHelper.GetDbContext();
                updateModel.Assignments = updateModel.Assignments.Where(x => x.IsChecked).ToList();
                List<int> documentIds = [];

                var assignments = await backendConfigurationPnDbContext.PropertyWorkers
                    .Where(x => x.WorkerId == updateModel.SiteId)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .ToListAsync().ConfigureAwait(false);

                foreach (var propertyWorker in assignments)
                {
                    propertyWorker.TaskManagementEnabled = updateModel.TaskManagementEnabled;
                    await propertyWorker.Update(backendConfigurationPnDbContext).ConfigureAwait(false);
                }

                var assignmentsForCreate = updateModel.Assignments
                    .Select(x => x.PropertyId)
                    .Where(x => !assignments.Select(y => y.PropertyId).Contains(x))
                    .ToList();
                List<PropertyWorker> propertyWorkers = [];

                foreach (var propertyAssignment in assignmentsForCreate
                             .Select(propertyAssignmentWorkerModel => new PropertyWorker
                             {
                                 WorkerId = updateModel.SiteId,
                                 PropertyId = propertyAssignmentWorkerModel,
                                 CreatedByUserId = userService.UserId,
                                 UpdatedByUserId = userService.UserId,
                                 TaskManagementEnabled = updateModel.TaskManagementEnabled
                             }))
                {
                    await propertyAssignment.Create(backendConfigurationPnDbContext).ConfigureAwait(false);

                    propertyWorkers.Add(propertyAssignment);
                    var documents = await caseTemplatePnDbContext.DocumentProperties
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Where(x => x.PropertyId == propertyAssignment.PropertyId)
                        .ToListAsync();
                    foreach (var document in documents)
                    {
                        if (!documentIds.Contains(document.DocumentId))
                        {
                            documentIds.Add(document.DocumentId);
                        }
                    }
                }

                var assignmentsForDelete = assignments
                    .Where(x => !updateModel.Assignments.Select(y => y.PropertyId).Contains(x.PropertyId))
                    .ToList();

                foreach (var propertyAssignment in assignmentsForDelete)
                {
                    propertyAssignment.UpdatedByUserId = userService.UserId;
                    await propertyAssignment.Delete(backendConfigurationPnDbContext).ConfigureAwait(false);
                    if (propertyAssignment.EntityItemId != null)
                    {
                        await core.EntityItemDelete((int)propertyAssignment.EntityItemId).ConfigureAwait(false);
                    }

                    var property = await backendConfigurationPnDbContext.Properties
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

                    await DeleteAllEntriesForPropertyAssignment(propertyAssignment, core, property, sdkDbContext, caseTemplatePnDbContext, backendConfigurationPnDbContext, itemsPlanningPnDbContext).ConfigureAwait(false);
                }

                if(assignmentsForDelete.Any())
                {
                    await WorkOrderHelper.RetractEform(assignmentsForDelete, true, core, userService.UserId, backendConfigurationPnDbContext).ConfigureAwait(false);
                    await WorkOrderHelper.RetractEform(assignmentsForDelete, false, core, userService.UserId, backendConfigurationPnDbContext).ConfigureAwait(false);
                }

                foreach (var documentId in documentIds)
                {
                    var documentSites = caseTemplatePnDbContext.DocumentSites
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Where(x => x.DocumentId == documentId).ToList();

                    foreach (var documentSite in documentSites)
                    {
                        if (documentSite.SdkCaseId != 0)
                        {
                            await core.CaseDelete(documentSite.SdkCaseId);
                        }

                        await documentSite.Delete(caseTemplatePnDbContext);
                    }

                    await bus.SendLocal(new DocumentUpdated(documentId)).ConfigureAwait(false);
                }

                if (!updateModel.TaskManagementEnabled)
                {
                    foreach (var propertyWorker in assignments)
                    {
                        if (propertyWorker.EntityItemId != null)
                        {
                            await core.EntityItemDelete((int)propertyWorker.EntityItemId).ConfigureAwait(false);

                            await WorkOrderHelper.RetractEform([propertyWorker], true, core, userService.UserId, backendConfigurationPnDbContext).ConfigureAwait(false);
                        }
                    }
                }
                else
                {
                    foreach (var propertyWorker in assignments)
                    {
                        if (!propertyWorkers.Any(x =>
                                x.WorkerId == propertyWorker.WorkerId && x.PropertyId == propertyWorker.PropertyId))
                        {
                            propertyWorkers.Add(propertyWorker);
                        }
                    }
                }

                await WorkOrderHelper.WorkorderFlowDeployEform(propertyWorkers, core, userService,
                    backendConfigurationPnDbContext, localizationService, bus, false).ConfigureAwait(false);
                return new OperationResult(true,"SuccessfullyUpdateAssignmentsProperties");
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                Console.WriteLine(e.StackTrace);
                return new OperationResult(false,
                    "ErrorWhileUpdateAssignmentsProperties");
            }
        }

        public static async Task<OperationResult> UpdateDeviceUser(DeviceUserModel deviceUserModel, Core core,
            int userId,
            IUserService userService,
            UserManager<EformUser> userManager,
            BackendConfigurationPnDbContext backendConfigurationPnDbContext,
            TimePlanningPnDbContext timePlanningDbContext,
            ILogger logger,
            ItemsPlanningPnDbContext itemsPlanningPnDbContext)
        {
            deviceUserModel.UserFirstName = deviceUserModel.UserFirstName.Trim();
            deviceUserModel.UserLastName = deviceUserModel.UserLastName.Trim();
            try
            {
                if (deviceUserModel.SiteMicrotingUid == 0)
                {
                    deviceUserModel.SiteMicrotingUid = (int) deviceUserModel.SiteUid!;
                }
                var sdkDbContext = core.DbContextHelper.GetDbContext();
                var language = sdkDbContext.Languages.Single(x => x.LanguageCode == deviceUserModel.LanguageCode);
                var siteDto = await core.SiteRead(deviceUserModel.SiteMicrotingUid).ConfigureAwait(false);
                var oldSiteName = siteDto.SiteName;
                if (siteDto.WorkerUid == null) return new OperationResult(false, "DeviceUserNotFound");
                {
                    // var workerDto = await core.Advanced_WorkerRead((int)siteDto.WorkerUid);
                    var worker = await sdkDbContext.Workers.SingleOrDefaultAsync(x => x.MicrotingUid == siteDto.WorkerUid).ConfigureAwait(false);
                    if (worker == null) return new OperationResult(false, "DeviceUserCouldNotBeObtained");
                    {
                        var oldEmail = worker.Email;
                        if (sdkDbContext.Workers.Any(x => x.Email == deviceUserModel.WorkerEmail && x.MicrotingUid != siteDto.WorkerUid))
                        {
                            // this email is already in use
                            return new OperationDataResult<int>(false, "EmailIsAlreadyInUse");
                        }
                        var fullName = deviceUserModel.UserFirstName + " " + deviceUserModel.UserLastName;
                        var isUpdated = await core.SiteUpdate(deviceUserModel.SiteMicrotingUid, fullName, deviceUserModel.UserFirstName,
                            deviceUserModel.UserLastName, worker.Email, deviceUserModel.LanguageCode).ConfigureAwait(false);

                        if (deviceUserModel.PinCode != "****") {
                            worker.PinCode = deviceUserModel.PinCode;
                        }
                        worker.EmployeeNo = deviceUserModel.EmployeeNo;
                        worker.Email = deviceUserModel.WorkerEmail;
                        worker.PhoneNumber = deviceUserModel.PhoneNumber;
                        await worker.Update(sdkDbContext).ConfigureAwait(false);

                        var user = await userService.GetByUsernameAsync(oldEmail).ConfigureAwait(false);
                        if (user != null)
                        {
                            user.Email = deviceUserModel.WorkerEmail;
                            user.FirstName = deviceUserModel.UserFirstName;
                            user.LastName = deviceUserModel.UserLastName;
                            user.Locale = language.LanguageCode;
                            var result = await userManager.UpdateAsync(user);
                        }

                        if (isUpdated)
                        {
                            if (deviceUserModel.TaskManagementEnabled == true)
                            {
                                var tasksAssigned = await backendConfigurationPnDbContext.WorkorderCases
                                    .Where(x => x.LastAssignedToName == siteDto.SiteName)
                                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                    .ToListAsync().ConfigureAwait(false);
                                foreach (var taskAssigned in tasksAssigned)
                                {
                                    taskAssigned.LastAssignedToName = fullName;
                                    await taskAssigned.Update(backendConfigurationPnDbContext).ConfigureAwait(false);
                                }
                                var createdByTasks = await backendConfigurationPnDbContext.WorkorderCases
                                    .Where(x => x.CreatedByName == siteDto.SiteName)
                                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                    .ToListAsync().ConfigureAwait(false);
                                foreach (var createdByTask in createdByTasks)
                                {
                                    createdByTask.CreatedByName = fullName;
                                    await createdByTask.Update(backendConfigurationPnDbContext).ConfigureAwait(false);
                                }
                            }

                            var propertyWorkers = await backendConfigurationPnDbContext.PropertyWorkers
                                .Where(x => x.WorkerId == worker.Id)
                                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                .ToListAsync().ConfigureAwait(false);

                            int? propertyId = null;
                            foreach (var propertyWorker in propertyWorkers)
                            {
                                if (propertyWorker.EntityItemId != null)
                                {
                                    var et = sdkDbContext.EntityItems.Single(x => x.Id == propertyWorker.EntityItemId);
                                    await core.EntityItemUpdate((int)propertyWorker.EntityItemId, fullName, "", et.EntityItemUid,
                                        et.DisplayIndex).ConfigureAwait(false);
                                }
                                propertyId = propertyWorker.PropertyId;
                                propertyWorker.TaskManagementEnabled = deviceUserModel.TaskManagementEnabled;
                                await propertyWorker.Update(backendConfigurationPnDbContext);
                            }

                            if (propertyId != null)
                            {
                                var property = await backendConfigurationPnDbContext.Properties.SingleAsync(x => x.Id == propertyId).ConfigureAwait(false);

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

                        // find all PlanningCases where the DoneByUserId is the same as the worker.Id and update the DoneByUserName to the new name
                        var planningCases = await itemsPlanningPnDbContext.PlanningCases
                            .Where(x => x.DoneByUserId == worker.Id)
                            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                            .Where(x => x.Status == 100)
                            .ToListAsync().ConfigureAwait(false);
                        foreach (var planningCase in planningCases)
                        {
                            planningCase.DoneByUserName = fullName;
                            await planningCase.Update(itemsPlanningPnDbContext).ConfigureAwait(false);
                        }

                        // find all PlanningCaseSites where the DoneByUserId is the same as the SiteId and update the DoneByUserName to the new name
                        var planningCaseSites = await itemsPlanningPnDbContext.PlanningCaseSites
                            .Where(x => x.DoneByUserId == worker.Id)
                            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                            .Where(x => x.Status == 100)
                            .ToListAsync().ConfigureAwait(false);
                        foreach (var planningCaseSite in planningCaseSites)
                        {
                            planningCaseSite.DoneByUserName = fullName;
                            await planningCaseSite.Update(itemsPlanningPnDbContext).ConfigureAwait(false);
                        }

                        //var siteId = await sdkDbContext.Sites.Where(x => x.MicrotingUid == siteDto.SiteId).Select(x => x.Id).FirstAsync();
                        if (deviceUserModel.TimeRegistrationEnabled == false && timePlanningDbContext.AssignedSites.Any(x => x.SiteId == siteDto.SiteId && x.WorkflowState != Constants.WorkflowStates.Removed))
                        {
                            var assignmentForDeletes = await timePlanningDbContext.AssignedSites.Where(x =>
                                x.SiteId == siteDto.SiteId && x.WorkflowState != Constants.WorkflowStates.Removed).ToListAsync().ConfigureAwait(false);

                            foreach (var assignmentForDelete in assignmentForDeletes)
                            {
                                assignmentForDelete.Resigned = true;
                                assignmentForDelete.ResignedAtDate = DateTime.UtcNow;
                                await assignmentForDelete.Update(timePlanningDbContext).ConfigureAwait(false);
                            }
                        }
                        else
                        {
                            if (deviceUserModel.TimeRegistrationEnabled == true)
                            {
                                var assignments = await timePlanningDbContext.AssignedSites.Where(x =>
                                    x.SiteId == siteDto.SiteId && x.WorkflowState != Constants.WorkflowStates.Removed).ToListAsync().ConfigureAwait(false);

                                if (assignments.Count != 0)
                                {
                                    await GoogleSheetHelper.PushToGoogleSheet(core, timePlanningDbContext, logger, oldSiteName, fullName).ConfigureAwait(false);
                                    return new OperationDataResult<int>(true, siteDto.SiteId);
                                }

                                try
                                {
                                    var assignmentSite = new AssignedSite
                                    {
                                        SiteId = siteDto.SiteId,
                                        CreatedByUserId = userId,
                                        UpdatedByUserId = userId
                                    };
                                    await assignmentSite.Create(timePlanningDbContext).ConfigureAwait(false);
                                    await GoogleSheetHelper.PushToGoogleSheet(core, timePlanningDbContext, logger, oldSiteName, fullName).ConfigureAwait(false);
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

                        return isUpdated
                            ? new OperationResult(true, "DeviceUserUpdatedSuccessfully")
                            : new OperationResult(false,
                                "DeviceUserParamCouldNotBeUpdated");
                    }

                }

            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return new OperationResult(false, "DeviceUserCouldNotBeUpdated");
            }
        }

        public static async Task<OperationDataResult<int>> CreateDeviceUser(DeviceUserModel deviceUserModel, Core core,
            int userId, TimePlanningPnDbContext timePlanningDbContext)
        {

            var sdkDbContext = core.DbContextHelper.GetDbContext();

            if (sdkDbContext.Workers.AsNoTracking().Any(x => x.Email == deviceUserModel.WorkerEmail && x.WorkflowState != Constants.WorkflowStates.Removed))
            {
                // this email is already in use
                return new OperationDataResult<int>(false, "EmailIsAlreadyInUse");
            }

            deviceUserModel.UserFirstName = deviceUserModel.UserFirstName.Trim();
            deviceUserModel.UserLastName = deviceUserModel.UserLastName.Trim();
            // var result = await _deviceUsersService.Create(deviceUserModel);
            var siteName = deviceUserModel.UserFirstName + " " + deviceUserModel.UserLastName;

            var site = await sdkDbContext.Sites.AsNoTracking().SingleOrDefaultAsync(x => x.Name == deviceUserModel.UserFirstName + " " + deviceUserModel.UserLastName && x.WorkflowState != Constants.WorkflowStates.Removed);

            if (site != null)
            {
                return new OperationDataResult<int>(false, "TheEmployeeAlreadyExist");
            }

            var siteDto = await core.SiteCreate(siteName, deviceUserModel.UserFirstName, deviceUserModel.UserLastName,
                null, deviceUserModel.LanguageCode).ConfigureAwait(false);

            site = await sdkDbContext.Sites.Where(x => x.MicrotingUid == siteDto.SiteId).FirstAsync().ConfigureAwait(false);

            var worker = await sdkDbContext.Workers.SingleAsync(x => x.MicrotingUid == siteDto.WorkerUid).ConfigureAwait(false);

            worker.EmployeeNo = deviceUserModel.EmployeeNo;
            worker.PinCode = deviceUserModel.PinCode;
            worker.Email = deviceUserModel.WorkerEmail;
            worker.PhoneNumber = deviceUserModel.PhoneNumber;
            await worker.Update(sdkDbContext).ConfigureAwait(false);

            if (deviceUserModel.TimeRegistrationEnabled == true)
            {
                try
                {
                    var assignmentSite = new AssignedSite
                    {
                        SiteId = (int)site.MicrotingUid!,
                        CreatedByUserId = userId,
                        UpdatedByUserId = userId
                    };
                    await assignmentSite.Create(timePlanningDbContext).ConfigureAwait(false);

                    return new OperationDataResult<int>(true, site.Id);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    // _logger.LogError(e.Message);
                    return new OperationDataResult<int>(false, "");
                }
            }

            return new OperationDataResult<int>(true, site.Id);
        }


        public static async Task DeleteAllEntriesForPropertyAssignment(PropertyWorker propertyAssignment, Core core,
            Property property, MicrotingDbContext sdkDbContext,
            CaseTemplatePnDbContext caseTemplatePnDbContext,
            BackendConfigurationPnDbContext backendConfigurationPnDbContext, ItemsPlanningPnDbContext itemsPlanningPnDbContext)
        {
            var documentSites = await caseTemplatePnDbContext.DocumentSites.Where(x => x.SdkSiteId == propertyAssignment.WorkerId).ToListAsync();

            // Delete all entries from DocumentSites
            foreach (var documentSite in documentSites)
            {
                if (documentSite.SdkCaseId != 0)
                {
                    await core.CaseDelete(documentSite.SdkCaseId);
                }
                await documentSite.Delete(caseTemplatePnDbContext).ConfigureAwait(false);
            }

            var planningSites = await backendConfigurationPnDbContext.PlanningSites
                .Join(backendConfigurationPnDbContext.AreaRulePlannings,
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
                var itemPlanningSites = await itemsPlanningPnDbContext.PlanningSites
                    .SingleOrDefaultAsync(x => x.SiteId == propertyAssignment.WorkerId
                                               && x.PlanningId == planningSite.ItemPlanningId
                                               && x.WorkflowState != Constants.WorkflowStates.Removed).ConfigureAwait(false);

                if (itemPlanningSites != null)
                {
                    await itemPlanningSites.Delete(itemsPlanningPnDbContext).ConfigureAwait(false);
                }

                var itemPlanningCaseSites = await itemsPlanningPnDbContext.PlanningCaseSites
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

                var dbPlanningSite = await backendConfigurationPnDbContext.PlanningSites
                    .FirstAsync(x => x.Id == planningSite.Id).ConfigureAwait(false);

                await dbPlanningSite.Delete(backendConfigurationPnDbContext).ConfigureAwait(false);

                itemPlanningCaseSites = await itemsPlanningPnDbContext.PlanningCaseSites
                    .Where(x => x.PlanningId == planningSite.ItemPlanningId
                                && x.MicrotingSdkSiteId != propertyAssignment.WorkerId
                                && x.WorkflowState != Constants.WorkflowStates.Removed)
                    .ToListAsync().ConfigureAwait(false);

                if (itemPlanningCaseSites.Count == 0)
                {
                    var itemPlanning = await itemsPlanningPnDbContext.Plannings
                        .FirstAsync(x => x.Id == planningSite.ItemPlanningId).ConfigureAwait(false);

                    await itemPlanning.Delete(itemsPlanningPnDbContext).ConfigureAwait(false);
                    var compliance = await backendConfigurationPnDbContext.Compliances.SingleOrDefaultAsync(x => x.PlanningId == itemPlanning.Id).ConfigureAwait(false);
                    if (compliance != null)
                    {
                        await compliance.Delete(backendConfigurationPnDbContext).ConfigureAwait(false);
                    }
                    // var property = await _backendConfigurationPnDbContext.Properties.SingleAsync(x => x.Id == compliance.PropertyId);
                    if (backendConfigurationPnDbContext.Compliances.Any(x => x.PropertyId == property.Id && x.Deadline < DateTime.UtcNow && x.WorkflowState != Constants.WorkflowStates.Removed))
                    {
                        property.ComplianceStatusThirty = 2;
                        property.ComplianceStatus = 2;
                    }
                    else
                    {
                        if (!backendConfigurationPnDbContext.Compliances.Any(x =>
                                x.PropertyId == property.Id && x.WorkflowState != Constants.WorkflowStates.Removed))
                        {
                            property.ComplianceStatusThirty = 0;
                            property.ComplianceStatus = 0;
                        }
                    }

                    await property.Update(backendConfigurationPnDbContext).ConfigureAwait(false);

                    var areaRulePlanning = await backendConfigurationPnDbContext.AreaRulePlannings
                        .Where(x => x.Id == planningSite.ArpId)
                        .FirstAsync().ConfigureAwait(false);
                    areaRulePlanning.Status = false;
                    await areaRulePlanning.Update(backendConfigurationPnDbContext).ConfigureAwait(false);
                }
            }
        }

        public static async Task<OperationResult> UpdateSimplifiedDeviceUser(SimpleDeviceUserModel deviceUserModel, Core core, int userServiceUserId, BackendConfigurationPnDbContext backendConfigurationPnDbContext, TimePlanningPnDbContext timePlanningDbContext)
        {
            var sdkDbContext = core.DbContextHelper.GetDbContext();
            var siteDto = await core.SiteRead(deviceUserModel.SiteMicrotingUid).ConfigureAwait(false);
            var worker = await sdkDbContext.Workers.FirstAsync(x => x.MicrotingUid == siteDto.WorkerUid).ConfigureAwait(false);

            var propertyWorkers = await backendConfigurationPnDbContext.PropertyWorkers
                .Where(x => x.WorkerId == worker.Id)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .ToListAsync().ConfigureAwait(false);

            foreach (var propertyWorker in propertyWorkers)
            {
                propertyWorker.PinCode = deviceUserModel.PinCode;
                propertyWorker.EmployeeNo = deviceUserModel.EmployeeNo;
                // TODO add employee number
                await propertyWorker.Update(backendConfigurationPnDbContext).ConfigureAwait(false);
            }

            return new OperationResult(true, "DeviceUserUpdatedSuccessfully");
        }
}