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
    using Microting.eForm.Infrastructure.Data.Entities;
    using Microting.eForm.Infrastructure.Models;
    using Microting.eFormApi.BasePn.Abstractions;
    using Microting.eFormApi.BasePn.Infrastructure.Consts;
    using Microting.eFormApi.BasePn.Infrastructure.Helpers;
    using Microting.eFormApi.BasePn.Infrastructure.Models.API;
    using Microting.EformBackendConfigurationBase.Infrastructure.Data;
    using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
    using Microting.EformBackendConfigurationBase.Infrastructure.Enum;

    public class BackendConfigurationAssignmentWorkerService : IBackendConfigurationAssignmentWorkerService
    {
        private readonly IEFormCoreService _coreHelper;
        private readonly IBackendConfigurationLocalizationService _backendConfigurationLocalizationService;
        private readonly IUserService _userService;
        private readonly BackendConfigurationPnDbContext _backendConfigurationPnDbContext;

        public BackendConfigurationAssignmentWorkerService(
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
                var propertyIds = createModel.Assignments
                    .Select(x => x.PropertyId)
                    .Distinct()
                    .ToList();
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

                await WorkorderFlowDeployEform(propertyIds, propertyWorkers);

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
                var propertyIds = updateModel.Assignments
                    .Select(x => x.PropertyId)
                    .Distinct()
                    .ToList();

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

                foreach (var propertyAssignment in assignmentsForDelete)
                {
                    propertyAssignment.UpdatedByUserId = _userService.UserId;
                    await propertyAssignment.Delete(_backendConfigurationPnDbContext);
                }

                if(assignmentsForDelete.Any())
                {
                    await RetractEform(assignmentsForDelete, eformIdForNewTasks);
                    await RetractEform(assignmentsForDelete, eformIdForOngoingTasks);
                    await RetractEform(assignmentsForDelete, eformIdForCompletedTasks);
                }

                await WorkorderFlowDeployEform(propertyIds, propertyWorkers);
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
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
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

                await RetractEform(propertyWorkers, eformIdForNewTasks);
                await RetractEform(propertyWorkers, eformIdForOngoingTasks);
                await RetractEform(propertyWorkers, eformIdForCompletedTasks);
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


        private async Task WorkorderFlowDeployEform(List<int> propertyIds, List<PropertyWorker> propertyWorkers)
        {
            var core = await _coreHelper.GetCore();
            var sdkDbContext = core.DbContextHelper.GetDbContext();
            foreach (var propertyId in propertyIds)
            {
                var property = await _backendConfigurationPnDbContext.Properties
                    .Where(x => x.WorkorderEnable)
                    .Where(x => x.Id == propertyId)
                    .Include(x => x.PropertyWorkers)
                    .ThenInclude(x => x.WorkorderCases)
                    .ThenInclude(x => x.ParentWorkorderCase)
                    .FirstOrDefaultAsync();

                if (property == null)
                {
                    continue;
                }

                int? folderIdForNewTasks;
                int? folderIdForOngoingTasks;
                int? folderIdForCompletedTasks;
                if (property.FolderIdForTasks == null)
                {
                    var translatesFolderForTasks = new List<CommonTranslationsModel>
                    {
                        new()
                        {
                            Name = "00. Opgavestyring",
                            LanguageId = 1, // da
                            Description = "",
                        },
                        new()
                        {
                            Name = "00. Tasks",
                            LanguageId = 2, // en
                            Description = "",
                        },
                        //new ()
                        //{
                        //    Name = "00. Tasks",
                        //    LanguageId = 3, // de
                        //    Description = "",
                        //},
                    };
                    property.FolderIdForTasks =
                        await core.FolderCreate(translatesFolderForTasks, property.FolderId);

                    await property.Update(_backendConfigurationPnDbContext);

                    var translateFolderForNewTask = new List<CommonTranslationsModel>
                    {
                        new()
                        {
                            Name = "01. Ny opgave",
                            LanguageId = 1, // da
                            Description = "",
                        },
                        new()
                        {
                            Name = "01. New tasks",
                            LanguageId = 2, // en
                            Description = "",
                        },
                        //new ()
                        //{
                        //    Name = "01. New task",
                        //    LanguageId = 3, // de
                        //    Description = "",
                        //},
                    };
                    folderIdForNewTasks = await core.FolderCreate(translateFolderForNewTask,
                        property.FolderIdForTasks);

                    var translateFolderForOngoingTask = new List<CommonTranslationsModel>
                    {
                        new()
                        {
                            Name = "02. Igangværende opgaver",
                            LanguageId = 1, // da
                            Description = "",
                        },
                        new()
                        {
                            Name = "02. Ongoing tasks",
                            LanguageId = 2, // en
                            Description = "",
                        },
                        //new ()
                        //{
                        //    Name = "02. Ongoing tasks",
                        //    LanguageId = 3, // de
                        //    Description = "",
                        //},
                    };
                    folderIdForOngoingTasks =
                        await core.FolderCreate(translateFolderForOngoingTask, property.FolderIdForTasks);

                    var translateFolderForCompletedTask = new List<CommonTranslationsModel>
                    {
                        new()
                        {
                            Name = "03. Afsluttede opgaver",
                            LanguageId = 1, // da
                            Description = "",
                        },
                        new()
                        {
                            Name = "03. Completed tasks",
                            LanguageId = 2, // en
                            Description = "",
                        },
                        //new ()
                        //{
                        //    Name = "03. Completed tasks",
                        //    LanguageId = 3, // de
                        //    Description = "",
                        //},
                    };
                    folderIdForCompletedTasks =
                        await core.FolderCreate(translateFolderForCompletedTask, property.FolderIdForTasks);
                }
                else
                {
                    folderIdForNewTasks = await sdkDbContext.Folders
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Where(x => x.ParentId == property.FolderIdForTasks)
                        .Where(x => x.Children.Any(y => y.Name == "01. New tasks"))
                        .Select(x => x.Id)
                        .FirstAsync();
                    folderIdForOngoingTasks = await sdkDbContext.Folders
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Where(x => x.ParentId == property.FolderIdForTasks)
                        .Where(x => x.Children.Any(y => y.Name == "02. Ongoing tasks"))
                        .Select(x => x.Id)
                        .FirstAsync();
                    folderIdForCompletedTasks = await sdkDbContext.Folders
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Where(x => x.ParentId == property.FolderIdForTasks)
                        .Where(x => x.Children.Any(y => y.Name == "03. Completed tasks"))
                        .Select(x => x.Id)
                        .FirstAsync();
                }

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
                var eformIdForComplitedTasks = await sdkDbContext.CheckListTranslations
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Text == "03. Completed task")
                    .Select(x => x.CheckListId)
                    .FirstAsync();

                var workorderCasesCompleted = property.PropertyWorkers
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .SelectMany(x => x.WorkorderCases)
                    .Where(y => y.CaseStatusesEnum == CaseStatusesEnum.Completed)
                    .ToList();
                var workorderCasesOngoing = property.PropertyWorkers
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .SelectMany(x => x.WorkorderCases)
                    .Where(y => y.CaseStatusesEnum == CaseStatusesEnum.Ongoing)
                    .Where(x => workorderCasesCompleted.All(y => y.ParentWorkorderCaseId != x.ParentWorkorderCaseId))
                    .ToList();
                foreach (var workorderCaseCompleted in workorderCasesCompleted)
                {
                    var cls = await sdkDbContext.Cases
                        .Where(x => x.Id == workorderCaseCompleted.CaseId)
                        .OrderBy(x => x.DoneAt)
                        .Include(x => x.Site)
                        .LastAsync();

                    var language =
                        await sdkDbContext.Languages.SingleOrDefaultAsync(x => x.Id == cls.Site.LanguageId) ??
                        await sdkDbContext.Languages.SingleOrDefaultAsync(x => x.LanguageCode == LocaleNames.Danish);

                    var fieldValues = await core.Advanced_FieldValueReadList(new() { cls.Id }, language);

                    var caseWithCreatedBy = await sdkDbContext.Cases
                        .Where(x => x.Id == workorderCaseCompleted.ParentWorkorderCase.CaseId)
                        .OrderBy(x => x.DoneAt)
                        .Include(x => x.Site)
                        .FirstAsync();

                    var fieldValuesWithCreatedBy = await core.Advanced_FieldValueReadList(new() { cls.Id },
                        await sdkDbContext.Languages.SingleOrDefaultAsync(
                            x => x.Id == caseWithCreatedBy.Site.LanguageId) ??
                        await sdkDbContext.Languages.SingleOrDefaultAsync(x => x.LanguageCode == LocaleNames.Danish));

                    var area = fieldValues.First().Value;
                    var descriptionFromCase = fieldValues[2].Value;
                    var assignedTo = fieldValues[3].Value;
                    var status = fieldValues[4].Value;
                    var createdBy = fieldValuesWithCreatedBy[4].Value;

                    var label = $"<strong>Location:</strong>{property.Name}<br>" +
                                $"<strong>Assigned to:</strong> {assignedTo}<br>" +
                                (string.IsNullOrEmpty(area)
                                    ? $"<strong>Area:</strong> {area}<br>"
                                    : "") +
                                $"<strong>Description:</strong> {descriptionFromCase}<br><br>" +
                                $"<strong>Created by:</strong> {assignedTo}<br>" +
                                (string.IsNullOrEmpty(createdBy)
                                    ? $"<strong>Created by:</strong> {createdBy}<br>"
                                    : "") +
                                $"<strong>Created date:</strong> {caseWithCreatedBy.DoneAt: dd.MM.yyyy}<br><br>" +
                                $"<strong>Last updated by:</strong>{cls.Site.Name}<br>" +
                                $"<strong>Last updated date:</strong>{DateTime.UtcNow: dd.MM.yyyy}<br><br>" +
                                $"<strong>Status:</strong> {status};";
                    await DeployEform(propertyWorkers, eformIdForComplitedTasks, folderIdForCompletedTasks, label);
                }

                foreach (var workorderCaseOngoing in workorderCasesOngoing
                             .GroupBy(x => x.ParentWorkorderCaseId,
                                 (i, cases) => new { parentWorkorderCaseId = i, workorderCases = cases.ToList() })
                             .ToList())
                {
                    var cls = await sdkDbContext.Cases
                        .Where(x => x.Id == workorderCaseOngoing.workorderCases.Last().CaseId)
                        .OrderBy(x => x.DoneAt)
                        .Include(x => x.Site)
                        .LastAsync();

                    var language =
                        await sdkDbContext.Languages.SingleOrDefaultAsync(x => x.Id == cls.Site.LanguageId) ??
                        await sdkDbContext.Languages.SingleOrDefaultAsync(x => x.LanguageCode == LocaleNames.Danish);

                    var fieldValues = await core.Advanced_FieldValueReadList(new() { cls.Id }, language);

                    var workorderOngoingCases = _backendConfigurationPnDbContext.WorkorderCases
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Where(x => x.ParentWorkorderCaseId == workorderCaseOngoing.parentWorkorderCaseId)
                        .Where(x => x.CaseStatusesEnum == CaseStatusesEnum.Ongoing)
                        .ToList();
                    Case lastOngoingCase = null;
                    if (workorderOngoingCases.Count > 1)
                    {
                        lastOngoingCase = await sdkDbContext.Cases
                            .Where(x => x.Id == workorderOngoingCases.Last().CaseId)
                            .OrderBy(x => x.DoneAt)
                            .Include(x => x.Site)
                            .FirstAsync();
                    }

                    var caseWithCreatedBy = await sdkDbContext.Cases
                        .Where(x => x.Id == workorderCaseOngoing.workorderCases.First().ParentWorkorderCase.CaseId)
                        .OrderBy(x => x.DoneAt)
                        .Include(x => x.Site)
                        .FirstAsync();

                    var fieldValuesWithCreatedBy = await core.Advanced_FieldValueReadList(new() { cls.Id },
                        await sdkDbContext.Languages.SingleOrDefaultAsync(
                            x => x.Id == caseWithCreatedBy.Site.LanguageId) ??
                        await sdkDbContext.Languages.SingleOrDefaultAsync(x => x.LanguageCode == LocaleNames.Danish));

                    var area = fieldValues.First().Value;
                    var descriptionFromCase = fieldValues[2].Value;
                    var assignedTo = fieldValues[3].Value;
                    var status = fieldValues[4].Value;
                    var createdBy = fieldValuesWithCreatedBy[4].Value;
                    // todo need change language to site language for correct translates and change back after end translate
                    var label = $"<strong>{_backendConfigurationLocalizationService.GetString("Location")}:</strong>{property.Name}<br>" +
                                $"<strong>{_backendConfigurationLocalizationService.GetString("Assigned to")}:</strong> {assignedTo}<br>" +
                                (string.IsNullOrEmpty(area)
                                    ? $"<strong>{_backendConfigurationLocalizationService.GetString("Area")}:</strong> {area}<br>"
                                    : "") +
                                $"<strong>{_backendConfigurationLocalizationService.GetString("Description")}:</strong> {descriptionFromCase}<br><br>" +
                                $"<strong>{_backendConfigurationLocalizationService.GetString("Created by")}:</strong> {assignedTo}<br>" +
                                (string.IsNullOrEmpty(createdBy)
                                    ? $"<strong>{_backendConfigurationLocalizationService.GetString("Created by")}:</strong> {createdBy}<br>"
                                    : "") +
                                $"<strong>{_backendConfigurationLocalizationService.GetString("Created date")}:</strong> {caseWithCreatedBy.DoneAt: dd.MM.yyyy}<br><br>" +
                                (lastOngoingCase == null
                                    ? ""
                                    : $"<strong>{_backendConfigurationLocalizationService.GetString("Last updated by")}:</strong>{lastOngoingCase.Site.Name}<br>") +
                                (lastOngoingCase == null
                                    ? ""
                                    : $"<strong>{_backendConfigurationLocalizationService.GetString("Last updated date")}:</strong>{lastOngoingCase.DoneAt: dd.MM.yyyy}<br><br>") +
                                $"<strong>{_backendConfigurationLocalizationService.GetString("Status")}:</strong> {status};";
                    await DeployEform(propertyWorkers, eformIdForOngoingTasks, folderIdForOngoingTasks, label);
                }

                await DeployEform(propertyWorkers, eformIdForNewTasks, folderIdForNewTasks,
                    $"<strong>{_backendConfigurationLocalizationService.GetString("Location")}:</strong> {property.Name}");
            }
        }

        private async Task DeployEform(List<PropertyWorker> propertyWorkers, int eformId, int? folderId,
            string description)
        {
            var core = await _coreHelper.GetCore();
            await using var sdkDbContext = core.DbContextHelper.GetDbContext();
            foreach (var propertyWorker in propertyWorkers)
            {
                var site = await sdkDbContext.Sites.SingleAsync(x => x.Id == propertyWorker.WorkerId);
                var language = await sdkDbContext.Languages.SingleAsync(x => x.Id == site.LanguageId);
                var mainElement = await core.ReadeForm(eformId, language);
                mainElement.Repeated = 0;
                if (folderId != null)
                {
                    mainElement.CheckListFolderName = await sdkDbContext.Folders
                        .Where(x => x.Id == folderId)
                        .Select(x => x.MicrotingUid.ToString())
                        .FirstOrDefaultAsync();
                }

                if (!string.IsNullOrEmpty(description))
                {
                    ((DataElement)mainElement.ElementList[0]).DataItemList[0].Description.InderValue = description;
                }

                mainElement.EndDate = DateTime.Now.AddYears(10).ToUniversalTime();
                mainElement.StartDate = DateTime.Now.ToUniversalTime();
                var caseId = await core.CaseCreate(mainElement, "", (int)site.MicrotingUid, folderId);
                await new WorkorderCase
                {
                    CaseId = (int)caseId,
                    PropertyWorkerId = propertyWorker.Id,
                    CaseStatusesEnum = CaseStatusesEnum.NewTask,
                }.Create(_backendConfigurationPnDbContext);
            }
        }

        private async Task RetractEform(List<PropertyWorker> propertyWorkers, int eformId)
        {
            var core = await _coreHelper.GetCore();
            await using var sdkDbContext = core.DbContextHelper.GetDbContext();
            foreach (var propertyWorker in propertyWorkers)
            {
                var site = await sdkDbContext.Sites.SingleAsync(x => x.Id == propertyWorker.WorkerId);
                await core.CaseDelete(eformId, (int)site.MicrotingUid);
                var workorderCase = await _backendConfigurationPnDbContext.WorkorderCases
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.PropertyWorkerId == propertyWorker.Id)
                    .FirstOrDefaultAsync();
                if (workorderCase != null)
                {
                    await workorderCase.Delete(_backendConfigurationPnDbContext);
                }
            }
        }
    }
}