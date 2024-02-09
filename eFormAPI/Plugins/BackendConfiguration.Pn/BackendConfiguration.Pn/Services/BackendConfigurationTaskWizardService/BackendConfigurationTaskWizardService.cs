namespace BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService;

using BackendConfigurationLocalizationService;
using Infrastructure;
using Infrastructure.Enums;
using Infrastructure.Models.TaskWizard;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Helpers;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.eFormApi.BasePn.Infrastructure.Models.Common;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Data;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public class BackendConfigurationTaskWizardService : IBackendConfigurationTaskWizardService
{
    private readonly IBackendConfigurationLocalizationService _localizationService;
    private readonly IUserService _userService;
    private readonly BackendConfigurationPnDbContext _backendConfigurationPnDbContext;
    private readonly IEFormCoreService _coreHelper;
    private readonly ItemsPlanningPnDbContext _itemsPlanningPnDbContext;
    private readonly ILogger<BackendConfigurationTaskWizardService> _logger;

    public BackendConfigurationTaskWizardService(
        IBackendConfigurationLocalizationService localizationService,
        IUserService userService,
        BackendConfigurationPnDbContext backendConfigurationPnDbContext,
        IEFormCoreService coreHelper,
        ItemsPlanningPnDbContext itemsPlanningPnDbContext,
        ILogger<BackendConfigurationTaskWizardService> logger)
    {
        _localizationService = localizationService;
        _userService = userService;
        _backendConfigurationPnDbContext = backendConfigurationPnDbContext;
        _coreHelper = coreHelper;
        _itemsPlanningPnDbContext = itemsPlanningPnDbContext;
        _logger = logger;
    }


    /// <inheritdoc />
    public async Task<OperationDataResult<List<TaskWizardModel>>> Index(TaskWizardRequestModel request)
    {
        try
        {
            var core = await _coreHelper.GetCore();
            var sdkDbContext = core.DbContextHelper.GetDbContext();
            var userLanguage = await _userService.GetCurrentUserLanguage();
            var areaId = await GetLogBooksAreaId();
            var query = _backendConfigurationPnDbContext.AreaRulePlannings
                .Include(x => x.PlanningSites)
                .Include(x => x.AreaRule.AreaRuleTranslations)
                .Include(x => x.AreaRulePlanningTags)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.AreaRule.CreatedInGuide)
                .Where(x => x.AreaId == areaId)
                .AsNoTracking();

            // filtration
            if (request.Filters.PropertyIds.Any())
            {
                query = query.Where(x => request.Filters.PropertyIds.Contains(x.PropertyId));
            }

            if (request.Filters.FolderIds.Any())
            {
                query = query.Where(x => request.Filters.FolderIds.Contains(x.FolderId));
            }

            if (request.Filters.AssignToIds.Any())
            {
                query = query.Where(x => x.PlanningSites.Where(z => z.WorkflowState != Constants.WorkflowStates.Removed).Any(y => request.Filters.AssignToIds.Contains(y.SiteId)));
            }

            if (request.Filters.Status != null)
            {
                var booleanStatus = request.Filters.Status == TaskWizardStatuses.Active;
                query = query.Where(x => x.Status == booleanStatus);
            }

            if (request.Filters.TagIds.Any())
            {
                foreach (var tagId in request.Filters.TagIds)
                {
                    query = query.Where(x => x.AreaRulePlanningTags
                        .Where(y => y.WorkflowState != Constants.WorkflowStates.Removed)
                        .Any(y => y.ItemPlanningTagId == tagId) ||
                    x.ItemPlanningTagId.HasValue && tagId == x.ItemPlanningTagId.Value);
                }
            }

            var itemPlanningTagIds = await query
                .SelectMany(x => x.AreaRulePlanningTags
                    .Where(y => y.WorkflowState != Constants.WorkflowStates.Removed)
                    .Select(y => y.ItemPlanningTagId))
                .Distinct()
                .ToListAsync();
            itemPlanningTagIds.AddRange(await query
                .Where(x => x.ItemPlanningTagId.HasValue)
                .Select(x => x.ItemPlanningTagId.Value)
                .Distinct()
                .ToListAsync());

            var itemPlanningTags = await _itemsPlanningPnDbContext.PlanningTags
                .Where(x => itemPlanningTagIds.Contains(x.Id))
                .Where(y => y.WorkflowState != Constants.WorkflowStates.Removed)
                .Select(x => new
                {
                    x.Id,
                    x.Name
                })
                .ToListAsync();
            var itemPlanningTagNames = itemPlanningTags.ToDictionary(x => x.Id, x => x.Name);

            var areaRulePlannings = query.Select(x => new
            {
                x.Id,
                PlanningSites = x.PlanningSites
                    .Where(y => y.WorkflowState != Constants.WorkflowStates.Removed)
                    .Select(y => y.SiteId)
                    .ToList(),
                x.StartDate,
                x.ItemPlanningId,
                x.FolderId,
                x.AreaRule.EformId,
                x.AreaRule.CreatedInGuide,
                x.RepeatEvery,
                x.RepeatType,
                x.Status,
                TaskName = x.AreaRule.AreaRuleTranslations
                    .Where(y => y.LanguageId == userLanguage.Id)
                    .Select(y => y.Name)
                    .FirstOrDefault(),
                PropertyName = _backendConfigurationPnDbContext.Properties.Where(y => y.Id == x.PropertyId)
                    .Select(y => y.Name).FirstOrDefault(),

                Tags = x.AreaRulePlanningTags
                    .Where(y => y.WorkflowState != Constants.WorkflowStates.Removed)
                    .Select(y => new CommonTagModel
                    {
                        Id = y.ItemPlanningTagId,
                        Name = itemPlanningTagNames.ContainsKey(y.ItemPlanningTagId) ? itemPlanningTagNames[y.ItemPlanningTagId] : ""
                    })
                    .ToList(),
                // add report tag to all tags
                TagReport = x.ItemPlanningTagId.HasValue ? new CommonTagModel
                {
                    Id = x.ItemPlanningTagId.Value,
                    Name = itemPlanningTagNames.ContainsKey(x.ItemPlanningTagId.Value) ? itemPlanningTagNames[x.ItemPlanningTagId.Value] : ""
                } : null
            }).ToList();

            // add eForm names, folder names and site names
            var eformNamesQuery = await sdkDbContext.CheckListTranslations
                .Where(x => x.LanguageId == userLanguage.Id)
                .Where(x => areaRulePlannings.Select(y => (int)y.EformId).Distinct().Contains(x.CheckListId))
                .Select(x => new { x.CheckListId, x.Text })
                .ToListAsync();

            var folderNamesQuery = await sdkDbContext.FolderTranslations
                .Where(x => x.LanguageId == userLanguage.Id)
                .Where(x => areaRulePlannings.Select(y => y.FolderId).Distinct().Contains(x.FolderId))
                .Select(x => new { x.FolderId, x.Name })
                .ToListAsync();

            var siteIds = areaRulePlannings.SelectMany(y => y.PlanningSites).Distinct().ToList();

            var siteNamesQuery = await sdkDbContext.Sites
                .Where(x => siteIds.Contains(x.Id))
                .Select(x => new { x.Id, x.Name })
                .ToListAsync();

            var fulfilledQuery = areaRulePlannings
                .Select(areaRule => new TaskWizardModel
                {
                    StartDate = (DateTime)areaRule.StartDate,
                    RepeatType = (RepeatType)areaRule.RepeatType,
                    RepeatEvery = (int)areaRule.RepeatEvery,
                    Status = areaRule.Status ? TaskWizardStatuses.Active : TaskWizardStatuses.NotActive,
                    TaskName = areaRule.TaskName,
                    Id = areaRule.Id,
                    Property = areaRule.PropertyName,
                    Tags = areaRule.TagReport != null
                        ? areaRule.Tags.Any(x => x.Id == areaRule.TagReport.Id)
                            ? areaRule.Tags.ToList() : areaRule.Tags.Append(areaRule.TagReport).ToList()
                        : areaRule.Tags.ToList(),
                    AssignedTo = siteNamesQuery.Where(x => areaRule.PlanningSites.Contains(x.Id)).Select(x => x.Name)
                        .ToList(),
                    Eform = eformNamesQuery.Where(x => x.CheckListId == areaRule.EformId).Select(x => x.Text)
                        .FirstOrDefault(),
                    Folder = folderNamesQuery.Where(x => x.FolderId == areaRule.FolderId).Select(x => x.Name)
                        .FirstOrDefault(),
                    CreatedInGuide = areaRule.CreatedInGuide
                })
                .AsQueryable();


            // sort
            var fulfilledList = QueryHelper.AddSortToQuery(fulfilledQuery, request.Pagination).ToList();

            return new OperationDataResult<List<TaskWizardModel>>(true, fulfilledList);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            _logger.LogError(e.Message);
            return new OperationDataResult<List<TaskWizardModel>>(false,
                _localizationService.GetString("ErrorWhileObtainingTasks"));
        }
    }

    /// <inheritdoc />
    public async Task<OperationDataResult<List<CommonDictionaryModel>>> GetProperties(bool fullNames)
    {
        try
        {
            var areaId = await GetLogBooksAreaId();

            var properties = await _backendConfigurationPnDbContext.Properties
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.AreaProperties.Select(y => y.AreaId).Contains(areaId))
                .Select(x => new CommonDictionaryModel
                {
                    Id = x.Id,
                    Name = fullNames ? $"{x.CVR} - {x.CHR} - {x.Name}" : x.Name,
                    Description = ""
                }).OrderBy(x => x.Name).AsNoTracking().ToListAsync().ConfigureAwait(false);
            return new OperationDataResult<List<CommonDictionaryModel>>(true, properties);
        }
        catch (Exception ex)
        {
            Log.LogException(ex.Message);
            Log.LogException(ex.StackTrace);
            return new OperationDataResult<List<CommonDictionaryModel>>(false,
                $"{_localizationService.GetString("ErrorWhileObtainingProperties")}: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OperationDataResult<TaskWizardTaskModel>> GetTaskById(int id)
    {
        try
        {
            var core = await _coreHelper.GetCore();
            var sdkDbContext = core.DbContextHelper.GetDbContext();
            var userLanguage = await _userService.GetCurrentUserLanguage();
            var query = _backendConfigurationPnDbContext.AreaRulePlannings
                .Include(x => x.PlanningSites)
                .Include(x => x.AreaRule.AreaRuleTranslations)
                .Include(x => x.AreaRulePlanningTags)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.Id == id)
                .AsNoTracking();

            if (!await query.Select(x => x.Id).AnyAsync())
            {
                return new OperationDataResult<TaskWizardTaskModel>(false,
                    _localizationService.GetString("TaskNotFound"));
            }

            var areaRulePlanning = await query
                .Select(x => new TaskWizardTaskModel
                {
                    FolderId = x.FolderId,
                    EformId = (int)x.AreaRule.EformId,
                    Id = x.Id,
                    AssignedTo = x.PlanningSites
                        .Where(y => y.WorkflowState != Constants.WorkflowStates.Removed)
                        .Select(y => y.SiteId)
                        .ToList(),
                    PropertyId = x.PropertyId,
                    Translations = x.AreaRule.AreaRuleTranslations
                        .Select(y => new CommonTranslationsModel()
                            { Id = y.Id, LanguageId = y.LanguageId, Name = y.Name })
                        .ToList(),
                    RepeatEvery = (int)x.RepeatEvery,
                    StartDate = (DateTime)x.StartDate,
                    RepeatType = (RepeatType)x.RepeatType,
                    ItemPlanningTagId = x.ItemPlanningTagId,
                    Status = x.Status ? TaskWizardStatuses.Active : TaskWizardStatuses.NotActive,
                    Tags = x.AreaRulePlanningTags
                        .Where(y => y.WorkflowState != Constants.WorkflowStates.Removed)
                        .Select(y => y.ItemPlanningTagId)
                        // .Where(y => !x.ItemPlanningTagId.HasValue || y != x.ItemPlanningTagId) // delete report tag from all tags
                        .ToList()
                })
                .FirstOrDefaultAsync();

            var eFormName = await sdkDbContext.CheckListTranslations
                .Where(x => x.LanguageId == userLanguage.Id)
                .Where(x => areaRulePlanning.EformId == x.CheckListId)
                .Select(x => x.Text )
                .FirstOrDefaultAsync();

            areaRulePlanning.EformName = eFormName;

            return new OperationDataResult<TaskWizardTaskModel>(true, areaRulePlanning);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            _logger.LogError(e.Message);
            return new OperationDataResult<TaskWizardTaskModel>(false,
                _localizationService.GetString("ErrorWhileObtainingTask"));
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult> CreateTask(TaskWizardCreateModel createModel)
    {
        if (!createModel.ItemPlanningTagId.HasValue) // This is the report table header tag
        {
            return new OperationResult(false,
                _localizationService.GetString("ReportTableHeaderTagIsRequired"));
        }

        if (createModel.FolderId == null)
        {
            return new OperationResult(false,
                _localizationService.GetString("FolderIsRequired"));
        }

        try
        {
            var core = await _coreHelper.GetCore();
            var sdkDbContext = core.DbContextHelper.GetDbContext();

            var eformName = sdkDbContext.CheckListTranslations
                .Where(x => x.CheckListId == createModel.EformId)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Select(x => x.Text)
                .FirstOrDefault();
            var folderName = sdkDbContext.FolderTranslations
                .Where(x => x.FolderId == createModel.FolderId)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Select(x => x.Name)
                .FirstOrDefault();

            if (createModel.Status == TaskWizardStatuses.Active && createModel.Sites.Count == 0)
            {
             createModel.Status = TaskWizardStatuses.NotActive;
            }

            if (createModel.StartDate != null)
            {
                if (createModel.StartDate!.Value.Hour != 0)
                {
                    createModel.StartDate = createModel.StartDate.Value.AddHours(24 - createModel.StartDate.Value.Hour);
                }
            }
            else
            {
                createModel.StartDate = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, DateTime.UtcNow.Day, 0, 0, 0);
            }

            var dayOfWeek = DayOfWeek.Monday;
            if (createModel.RepeatType == RepeatType.Week)
            {
                // get day of week from start date
                dayOfWeek = createModel.StartDate.Value.DayOfWeek;
            }

            var dayOfMonth = 1;
            if (createModel.RepeatType == RepeatType.Month)
            {
                // get day of month from start date
                dayOfMonth = createModel.StartDate.Value.Day;
                if (dayOfMonth > 28)
                {
                    dayOfMonth = 28;
                }
            }

            // create planning
            var planning = new Planning
            {
                CreatedAt = DateTime.UtcNow,
                IsEditable = false,
                Enabled = createModel.Status == TaskWizardStatuses.Active,
                IsLocked = true,
                IsHidden = false,
                StartDate = (DateTime)createModel.StartDate,
                RepeatType = (Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType)createModel.RepeatType,
                RelatedEFormId = createModel.EformId,
                RelatedEFormName = eformName,
                RepeatEvery = createModel.RepeatEvery,
                SdkFolderId = createModel.FolderId,
                SdkFolderName = folderName,
                ShowExpireDate = true,
                DayOfWeek = dayOfWeek,
                DayOfMonth = dayOfMonth,
                UpdatedByUserId = _userService.UserId,
                CreatedByUserId = _userService.UserId,
                ReportGroupPlanningTagId = (int)createModel.ItemPlanningTagId!
            };


            await planning.Create(_itemsPlanningPnDbContext);

            foreach (var planningSite in createModel.Sites.Select(assignedSite => new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.PlanningSite
                     {
                         SiteId = assignedSite,
                         PlanningId = planning.Id,
                         CreatedByUserId = _userService.UserId,
                         UpdatedByUserId = _userService.UserId
                     }))
            {
                await planningSite.Create(_itemsPlanningPnDbContext).ConfigureAwait(false);
            }

            var propertyItemPlanningTagId = await _backendConfigurationPnDbContext.Properties
                .Where(x => x.Id == createModel.PropertyId)
                .Select(x => x.ItemPlanningTagId)
                .FirstAsync();

            // PlanningsTags propertyPlanningsTags = new PlanningsTags
            // {
            //     PlanningId = planning.Id,
            //     PlanningTagId = propertyItemPlanningTagId,
            //     CreatedByUserId = _userService.UserId,
            //     UpdatedByUserId = _userService.UserId
            // };
            // await propertyPlanningsTags.Create(_itemsPlanningPnDbContext).ConfigureAwait(false);


            var tagIds = createModel.TagIds.Distinct().ToList(); // ToList() need for not update createModel.TagIds
            tagIds.Add(propertyItemPlanningTagId);
            if (createModel.ItemPlanningTagId.HasValue)
            {
                tagIds.Add(createModel.ItemPlanningTagId.Value);
            }

            foreach (var planningsTags in tagIds.Select(tagId => new PlanningsTags
                     {
                         PlanningId = planning.Id,
                         PlanningTagId = tagId,
                         CreatedByUserId = _userService.UserId,
                         UpdatedByUserId = _userService.UserId
                     }))
            {
                await planningsTags.Create(_itemsPlanningPnDbContext).ConfigureAwait(false);
            }

            foreach (var planningNameTranslation in createModel.Translates.Select(translationsModel => new PlanningNameTranslation
                     {
                         PlanningId = planning.Id,
                         LanguageId = translationsModel.LanguageId,
                         Name = translationsModel.Name,
                         CreatedByUserId = _userService.UserId,
                         UpdatedByUserId = _userService.UserId
                     }))
            {
                await planningNameTranslation.Create(_itemsPlanningPnDbContext).ConfigureAwait(false);
            }

            foreach (var planningSite in createModel.Sites.Select(assignedSite => new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.PlanningSite
                     {
                         SiteId = assignedSite,
                         PlanningId = planning.Id,
                         CreatedByUserId = _userService.UserId,
                         UpdatedByUserId = _userService.UserId
                     }))
            {
                await planningSite.Create(_itemsPlanningPnDbContext).ConfigureAwait(false);
            }

            var areaId = await GetLogBooksAreaId();
            // create area rule with translations and area rule plannings
            var areRule = new AreaRule
            {
                CreatedInGuide = true,
                AreaId = areaId,
                AreaRuleTranslations = createModel.Translates
                    .Select(t => new AreaRuleTranslation
                    {
                        Name = t.Name,
                        LanguageId = t.LanguageId,
                        UpdatedByUserId = _userService.UserId,
                        CreatedByUserId = _userService.UserId
                    })
                    .ToList(),
                EformId = createModel.EformId,
                EformName = eformName,
                PropertyId = createModel.PropertyId,
                FolderId = (int) createModel.FolderId,
                FolderName = folderName,
                RepeatEvery = createModel.RepeatEvery,
                RepeatType = (int?)createModel.RepeatType,
                Notifications = true,
                NotificationsModifiable = false,
                ComplianceEnabled = true,
                AreaRulesPlannings =
                [
                    new()
                    {
                        AreaId = areaId,
                        FolderId = (int)createModel.FolderId,
                        ItemPlanningId = planning.Id,
                        ComplianceEnabled = true,
                        PlanningSites = createModel.Sites
                            .Select(x =>
                                new Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.PlanningSite
                                {
                                    AreaId = areaId,
                                    SiteId = x,
                                    Status = 0,
                                    UpdatedByUserId = _userService.UserId,
                                    CreatedByUserId = _userService.UserId
                                })
                            .ToList(),
                        Status = createModel.Status == TaskWizardStatuses.Active,
                        StartDate = createModel.StartDate,
                        SendNotifications = true,
                        RepeatEvery = createModel.RepeatEvery,
                        RepeatType = (int?)createModel.RepeatType,
                        PropertyId = createModel.PropertyId,
                        ItemPlanningTagId = createModel.ItemPlanningTagId, // This is the report table header tag
                        UpdatedByUserId = _userService.UserId,
                        CreatedByUserId = _userService.UserId,
                        AreaRulePlanningTags = createModel.TagIds.Distinct()
                            .ToList() // These are the tags for filtering
                            .Select(x => new AreaRulePlanningTag
                            {
                                ItemPlanningTagId = x,
                                UpdatedByUserId = _userService.UserId,
                                CreatedByUserId = _userService.UserId
                            })
                            .ToList()
                    }
                ],
                UpdatedByUserId = _userService.UserId,
                CreatedByUserId = _userService.UserId
            };
            await areRule.Create(_backendConfigurationPnDbContext);

            if (createModel.Status == TaskWizardStatuses.Active && createModel.StartDate <= DateTime.UtcNow)
            {
                await PairItemWithSiteHelper.Pair(
                    createModel.Sites,
                    createModel.EformId,
                    planning.Id,
                    (int) createModel.FolderId, core, _itemsPlanningPnDbContext, true, _localizationService);
            }
            return new OperationResult(true, _localizationService.GetString("TaskCreatedSuccessful"));
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            Console.WriteLine(e.StackTrace);
            _logger.LogError(e.Message);
            return new OperationResult(false,
                _localizationService.GetString("ErrorWhileCreatingTask"));
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult> UpdateTask(TaskWizardCreateModel updateModel)
    {
        try
        {
            var core = await _coreHelper.GetCore();
            var sdkDbContext = core.DbContextHelper.GetDbContext();

            var eformName = sdkDbContext.CheckListTranslations
                .Where(x => x.CheckListId == updateModel.EformId)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Select(x => x.Text)
                .FirstOrDefault();
            var folderName = sdkDbContext.FolderTranslations
                .Where(x => x.FolderId == updateModel.FolderId)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Select(x => x.Name)
                .FirstOrDefault();

            var areaRulePlanning = _backendConfigurationPnDbContext.AreaRulePlannings
                .Where(x => x.Id == updateModel.Id)
                .Include(z => z.AreaRule)
                .ThenInclude(x => x.AreaRuleTranslations)
                .Include(x => x.PlanningSites.Where(y => y.WorkflowState != Constants.WorkflowStates.Removed))
                .FirstOrDefault(x => x.WorkflowState != Constants.WorkflowStates.Removed);

            if (areaRulePlanning == null)
            {
                return new OperationResult(false,
                    _localizationService.GetString("TaskNotFound"));
            }

            if (updateModel.Status == TaskWizardStatuses.Active && updateModel.Sites.Count == 0)
            {
                updateModel.Status = TaskWizardStatuses.NotActive;
            }

            if (updateModel.StartDate != null)
            {
                if (updateModel.StartDate!.Value.Hour != 0)
                {
                    updateModel.StartDate = updateModel.StartDate.Value.AddHours(24 - updateModel.StartDate.Value.Hour);
                }
            }
            else
            {
                updateModel.StartDate = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, DateTime.UtcNow.Day, 0, 0, 0);
            }

            var currentSiteIds = areaRulePlanning.PlanningSites.Select(ps => ps.SiteId).ToList();
            var sitesToAdd = updateModel.Sites.Except(currentSiteIds).ToList();
            var sitesToRemove = currentSiteIds.Except(updateModel.Sites).ToList();

            var areaId = await GetLogBooksAreaId();

            // update area rule plannings and area rule with translations
            var oldStatus = areaRulePlanning.Status;
            areaRulePlanning.FolderId = (int) updateModel.FolderId;
            areaRulePlanning.Status = updateModel.Status == TaskWizardStatuses.Active;
            areaRulePlanning.StartDate = updateModel.StartDate;
            areaRulePlanning.RepeatEvery = updateModel.RepeatEvery;
            areaRulePlanning.RepeatType = (int?)updateModel.RepeatType;
            areaRulePlanning.PropertyId = updateModel.PropertyId;
            var oldItemPlanningTagId = areaRulePlanning.ItemPlanningTagId;
            areaRulePlanning.ItemPlanningTagId = updateModel.ItemPlanningTagId;
            areaRulePlanning.UpdatedByUserId = _userService.UserId;
            areaRulePlanning.ComplianceEnabled = true;
            areaRulePlanning.SendNotifications = true;
            await areaRulePlanning.Update(_backendConfigurationPnDbContext);

            var planning = await _itemsPlanningPnDbContext.Plannings
                .Where(x => x.Id == areaRulePlanning.ItemPlanningId)
                .Include(x => x.NameTranslations)
                .Include(x => x.PlanningsTags)
                .Include(x => x.PlanningSites)
                .FirstAsync(x => x.WorkflowState != Constants.WorkflowStates.Removed);
            foreach (var site in sitesToAdd
                         .Select(x =>
                             new Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.PlanningSite
                             {
                                 SiteId = x,
                                 AreaRulePlanningsId = areaRulePlanning.Id,
                                 AreaId = areaId,
                                 AreaRuleId = areaRulePlanning.AreaId,
                                 CreatedByUserId = _userService.UserId,
                                 UpdatedByUserId = _userService.UserId
                             }))
            {
                await site.Create(_backendConfigurationPnDbContext);
            }

            foreach (var site in sitesToRemove.Select(siteId =>
                         areaRulePlanning.PlanningSites.First(x => x.SiteId == siteId)))
            {
                site.UpdatedByUserId = _userService.UserId;
                await site.Delete(_backendConfigurationPnDbContext);
            }

            // update area rule
            areaRulePlanning.AreaRule.EformId = updateModel.EformId;
            areaRulePlanning.AreaRule.EformName = eformName;
            areaRulePlanning.AreaRule.PropertyId = updateModel.PropertyId;
            areaRulePlanning.AreaRule.FolderId = (int) updateModel.FolderId;
            areaRulePlanning.AreaRule.FolderName = folderName;
            areaRulePlanning.AreaRule.RepeatEvery = updateModel.RepeatEvery;
            areaRulePlanning.AreaRule.RepeatType = (int?)updateModel.RepeatType;
            areaRulePlanning.AreaRule.UpdatedByUserId = _userService.UserId;
            if (!oldStatus && areaRulePlanning.Status)
            {
                areaRulePlanning.StartDate = updateModel.StartDate;
            }
            await areaRulePlanning.AreaRule.Update(_backendConfigurationPnDbContext);

            // update area rule translations
            var translationsToUpdate = areaRulePlanning.AreaRule.AreaRuleTranslations
                .Where(nt => updateModel.Translates.Any(t => t.Id == nt.Id && t.Name != nt.Name))
                .ToList();
            var translationsToAdd = updateModel.Translates
                .Where(t => t.Id == null)
                .AsQueryable();

            foreach (var translation in translationsToUpdate)
            {
                translation.Name = updateModel.Translates.Where(x => x.Id == translation.Id).Select(x => x.Name)
                    .FirstOrDefault();
                translation.UpdatedByUserId = _userService.UserId;
                await translation.Update(_backendConfigurationPnDbContext);
            }

            foreach (var translation in translationsToAdd.Select(t => new AreaRuleTranslation
                         {
                             Name = t.Name,
                             LanguageId = t.LanguageId,
                             AreaRuleId = areaRulePlanning.AreaRuleId,
                             CreatedByUserId = _userService.UserId,
                             UpdatedByUserId = _userService.UserId
                         })
                         .ToList())
            {
                await translation.Create(_backendConfigurationPnDbContext);
            }

            switch (oldStatus)
            {
                // create item planning
                case false when areaRulePlanning.Status:
                {
                    planning.DayOfMonth = 1;
                    planning.StartDate = updateModel.StartDate!.Value;
                    planning.Enabled = true;
                    planning.DayOfWeek = DayOfWeek.Monday;
                    planning.RepeatEvery = updateModel.RepeatEvery;
                    planning.ReportGroupPlanningTagId = (int)updateModel.ItemPlanningTagId!;
                    planning.RepeatType =
                        (Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType)updateModel.RepeatType;

                    areaRulePlanning = _backendConfigurationPnDbContext.AreaRulePlannings
                        .Where(x => x.Id == updateModel.Id)
                        .Include(x => x.PlanningSites.Where(y => y.WorkflowState != Constants.WorkflowStates.Removed))
                        .FirstOrDefault(x => x.WorkflowState != Constants.WorkflowStates.Removed);

                    foreach (var planningSite in areaRulePlanning.PlanningSites
                                 .Where(x => x.Status == 0)
                                 .Where(y => y.WorkflowState != Constants.WorkflowStates.Removed))
                    {
                        planningSite.Status = 33;
                        planningSite.WorkflowState = Constants.WorkflowStates.Created;
                        await planningSite.Update(_backendConfigurationPnDbContext);
                    }

                    foreach (var planningSite in areaRulePlanning.PlanningSites
                                 .Where(y => y.WorkflowState != Constants.WorkflowStates.Removed))
                    {
                        var itemsPlanningSite = await _itemsPlanningPnDbContext.PlanningSites.FirstOrDefaultAsync(x =>
                            x.PlanningId == planning.Id && x.SiteId == planningSite.SiteId);

                        if (itemsPlanningSite != null)
                        {
                            itemsPlanningSite.WorkflowState = Constants.WorkflowStates.Created;
                            await itemsPlanningSite.Update(_itemsPlanningPnDbContext);
                        } else
                        {
                            await new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.PlanningSite
                            {
                                PlanningId = planning.Id,
                                SiteId = planningSite.SiteId,
                                CreatedByUserId = _userService.UserId,
                                UpdatedByUserId = _userService.UserId
                            }.Create(_itemsPlanningPnDbContext);
                        }
                    }

                    await planning.Update(_itemsPlanningPnDbContext).ConfigureAwait(false);
                    foreach (PlanningNameTranslation planningNameTranslation in planning.NameTranslations)
                    {
                        foreach (AreaRuleTranslation areaRuleTranslation in areaRulePlanning.AreaRule.AreaRuleTranslations)
                        {
                            if (planningNameTranslation.LanguageId == areaRuleTranslation.LanguageId)
                            {
                                planningNameTranslation.Name = areaRuleTranslation.Name;
                                await planningNameTranslation.Update(_itemsPlanningPnDbContext)
                                    .ConfigureAwait(false);
                            }
                        }
                    }
                    await PairItemWithSiteHelper.Pair(
                            areaRulePlanning.PlanningSites
                                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                .Select(x => x.SiteId).ToList(),
                            updateModel.EformId,
                            planning.Id,
                            areaRulePlanning.AreaRule.FolderId, core, _itemsPlanningPnDbContext,
                            areaRulePlanning.UseStartDateAsStartOfPeriod, _localizationService)
                        .ConfigureAwait(false);
                    // areaRulePlanning.ItemPlanningId = planning.Id;
                    await areaRulePlanning.Update(_backendConfigurationPnDbContext)
                        .ConfigureAwait(false);
                    // await UpdateTags(planning.Id, updateModel, areaRulePlanning.Id, oldItemPlanningTagId, false);
                    break;
                }
                // delete item planning but not delete task
                case true when !areaRulePlanning.Status:
                    //if (areaRulePlanning.ItemPlanningId != 0)
                    {

                    await UpdateTags(planning.Id, updateModel, areaRulePlanning.Id, oldItemPlanningTagId).ConfigureAwait(false);
                    planning.Enabled = false;
                    await planning.Update(_itemsPlanningPnDbContext).ConfigureAwait(false);

                    var complianceList = await _backendConfigurationPnDbContext.Compliances
                        .Where(x => x.PlanningId == areaRulePlanning.ItemPlanningId
                                    && x.WorkflowState != Constants.WorkflowStates.Removed)
                        .ToListAsync().ConfigureAwait(false);
                    foreach (var compliance in complianceList)
                    {
                        if (compliance != null)
                        {
                            await compliance.Delete(_backendConfigurationPnDbContext)
                                .ConfigureAwait(false);
                        }
                    }
                    //
                    // await BackendConfigurationAreaRulePlanningsServiceHelper.DeleteItemPlanning(
                    //         areaRulePlanning.ItemPlanningId, core, _userService.UserId,
                    //         _backendConfigurationPnDbContext, _itemsPlanningPnDbContext)
                    //     .ConfigureAwait(false);
                    //
                    //     areaRulePlanning.ItemPlanningId = 0;
                    var planningCases = await _itemsPlanningPnDbContext.PlanningCases
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Where(x => x.PlanningId == planning.Id)
                        .ToListAsync().ConfigureAwait(false);

                    foreach (var planningCase in planningCases)
                    {
                        var planningCaseSites = await _itemsPlanningPnDbContext.PlanningCaseSites
                            .Where(x => x.PlanningCaseId == planningCase.Id)
                            .Where(planningCaseSite => planningCaseSite.MicrotingSdkCaseId != 0 ||
                                                       planningCaseSite.MicrotingCheckListSitId != 0)
                            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                            .ToListAsync().ConfigureAwait(false);
                        foreach (var planningCaseSite in planningCaseSites)
                        {
                            var result =
                                await sdkDbContext.Cases.SingleOrDefaultAsync(x => x.Id == planningCaseSite.MicrotingSdkCaseId)
                                    .ConfigureAwait(false);
                            if (result is { MicrotingUid: { } })
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
                    }
                    areaRulePlanning.Status = false;
                    await areaRulePlanning.Update(_backendConfigurationPnDbContext)
                        .ConfigureAwait(false);

                    var planningSites =
                        await _itemsPlanningPnDbContext.PlanningSites.Where(x => x.PlanningId == planning.Id).ToListAsync().ConfigureAwait(false);

                    foreach (var planningSite in planningSites)
                    {
                        await planningSite.Delete(_itemsPlanningPnDbContext).ConfigureAwait(false);
                    }

                    //
                    //     var planningSites = await _backendConfigurationPnDbContext.PlanningSites
                    //         .Where(x => x.AreaRulePlanningsId == areaRulePlanning.Id)
                    //         .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    //         .ToListAsync().ConfigureAwait(false);
                    //
                    //     foreach (var planningSite in planningSites) // delete all planning sites
                    //     {
                    //         planningSite.Status = 0;
                    //         await planningSite.Update(_backendConfigurationPnDbContext)
                    //             .ConfigureAwait(false);
                    //         // await planningSite.Delete(_backendConfigurationPnDbContext).ConfigureAwait(false);
                    //     }
                    }

                    break;
                // update item planning
                case true when areaRulePlanning.Status:
                    // TODO, this is not possible to do, since the web interface does not allow to update active plannings
                    //if (areaRulePlanning.ItemPlanningId != 0) // Since ItemPlanningId is not 0, we already have a planning and therefore just update it
                    {
                        planning.Enabled = areaRulePlanning.Status;
                        planning.PushMessageOnDeployment = areaRulePlanning.SendNotifications;
                        planning.StartDate = new DateTime((int)(areaRulePlanning.StartDate?.Year),
                            (int)(areaRulePlanning.StartDate?.Month), (int)(areaRulePlanning.StartDate?.Day), 0, 0, 0);
                        planning.DayOfMonth = 1;
                        planning.DayOfWeek = DayOfWeek.Friday;
                        planning.RepeatType =
                            (Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType)updateModel.RepeatType;
                        planning.RelatedEFormId = updateModel.EformId;
                        planning.RelatedEFormName = eformName;
                        planning.RepeatEvery = updateModel.RepeatEvery;
                        planning.SdkFolderId = updateModel.FolderId;
                        planning.SdkFolderName = folderName;
                        planning.UpdatedByUserId = _userService.UserId;
                        planning.ReportGroupPlanningTagId = (int)updateModel.ItemPlanningTagId!;
                        await planning.Update(_itemsPlanningPnDbContext);

                        // update planning names
                        var planningTranslationsToUpdate = planning.NameTranslations
                            .Where(nt =>
                                translationsToUpdate.Any(t => t.LanguageId == nt.LanguageId && t.Name != nt.Name))
                            .ToList();

                        foreach (var translation in planningTranslationsToUpdate)
                        {
                            translation.Name = updateModel.Translates
                                .Where(t => t.LanguageId == translation.LanguageId)
                                .Select(x => x.Name)
                                .FirstOrDefault();
                            translation.UpdatedByUserId = _userService.UserId;
                            await translation.Update(_itemsPlanningPnDbContext);
                        }

                        foreach (var translation in translationsToAdd.Select(t => new PlanningNameTranslation
                                     {
                                         Name = t.Name,
                                         LanguageId = t.LanguageId,
                                         PlanningId = planning.Id,
                                         CreatedByUserId = _userService.UserId,
                                         UpdatedByUserId = _userService.UserId
                                     })
                                     .ToList())
                        {
                            await translation.Create(_itemsPlanningPnDbContext);
                        }

                        // update planning tags
                        await UpdateTags(planning.Id, updateModel, areaRulePlanning.Id, oldItemPlanningTagId);

                        // update planning sites
                        currentSiteIds = planning.PlanningSites.Where(x => x.WorkflowState != Constants.WorkflowStates.Removed).Select(ps => ps.SiteId).ToList();

                        sitesToAdd = updateModel.Sites.Except(currentSiteIds).ToList();
                        foreach (var site in sitesToAdd
                                     .Select(x =>
                                         new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.PlanningSite
                                         {
                                             SiteId = x,
                                             PlanningId = planning.Id,
                                             CreatedByUserId = _userService.UserId,
                                             UpdatedByUserId = _userService.UserId
                                         }))
                        {
                            await site.Create(_itemsPlanningPnDbContext);
                        }

                        sitesToRemove = currentSiteIds.Except(updateModel.Sites).ToList();
                        var planningSitesToRemove =
                            await _itemsPlanningPnDbContext.PlanningSites.Where(x => sitesToRemove.Contains(x.SiteId) && x.PlanningId == planning.Id).ToListAsync().ConfigureAwait(false);
                        foreach (var planningSite in planningSitesToRemove)
                        {
                            planningSite.UpdatedByUserId = _userService.UserId;
                            await planningSite.Delete(_itemsPlanningPnDbContext);
                            var planningCaseSites = await _itemsPlanningPnDbContext.PlanningCaseSites
                                .Where(x => x.PlanningId == planning.Id)
                                .Where(x => x.MicrotingSdkSiteId == planningSite.SiteId)
                                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                .ToListAsync();

                            foreach (var planningCaseSite in planningCaseSites)
                            {
                                var result =
                                    await sdkDbContext.Cases.SingleOrDefaultAsync(x =>
                                            x.Id == planningCaseSite.MicrotingSdkCaseId)
                                        .ConfigureAwait(false);
                                if (result is { MicrotingUid: not null })
                                {
                                    await core.CaseDelete((int)result.MicrotingUid)
                                        .ConfigureAwait(false);
                                }
                                else
                                {
                                    var clSites = await sdkDbContext.CheckListSites
                                        .SingleOrDefaultAsync(
                                            x =>
                                                x.Id == planningCaseSite.MicrotingCheckListSitId)
                                        .ConfigureAwait(false);

                                    if (clSites != null)
                                    {
                                        await core.CaseDelete(clSites.MicrotingUid)
                                            .ConfigureAwait(false);
                                    }
                                }
                            }
                        }

                        await planning.Update(_itemsPlanningPnDbContext);

                        if (sitesToAdd.Count > 0)
                        {
                            await PairItemWithSiteHelper.Pair(
                                sitesToAdd,
                                updateModel.EformId,
                                planning.Id,
                                (int)planning.SdkFolderId, core, _itemsPlanningPnDbContext,
                                areaRulePlanning.UseStartDateAsStartOfPeriod, _localizationService);
                        }

                        await planning.Update(_itemsPlanningPnDbContext);

                        if (!_itemsPlanningPnDbContext.PlanningSites.Any(x =>
                                x.PlanningId == planning.Id &&
                                x.WorkflowState != Constants.WorkflowStates.Removed) ||
                            !areaRulePlanning.ComplianceEnabled)
                        {
                            var complianceList = await _backendConfigurationPnDbContext.Compliances
                                .Where(x => x.PlanningId == planning.Id)
                                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                .ToListAsync().ConfigureAwait(false);
                            foreach (var compliance in complianceList)
                            {
                                await compliance.Delete(_backendConfigurationPnDbContext)
                                    .ConfigureAwait(false);
                                if (_backendConfigurationPnDbContext.Compliances.Any(x =>
                                        x.PropertyId == areaRulePlanning.PropertyId &&
                                        x.Deadline < DateTime.UtcNow &&
                                        x.WorkflowState != Constants.WorkflowStates.Removed))
                                {
                                    areaRulePlanning.AreaRule.Property.ComplianceStatusThirty = 2;
                                    areaRulePlanning.AreaRule.Property.ComplianceStatus = 2;
                                }
                                else
                                {
                                    if (!_backendConfigurationPnDbContext.Compliances.Any(x =>
                                            x.PropertyId == areaRulePlanning.AreaRule.Property.Id && x.WorkflowState !=
                                            Constants.WorkflowStates.Removed))
                                    {
                                        areaRulePlanning.AreaRule.Property.ComplianceStatusThirty = 0;
                                        areaRulePlanning.AreaRule.Property.ComplianceStatus = 0;
                                    }
                                }

                                await areaRulePlanning.AreaRule.Property.Update(_backendConfigurationPnDbContext);
                            }
                        }
                    }

                    break;
                // nothing to do, but update tags
                case false when !areaRulePlanning.Status:
                {
                    await UpdateTags(areaRulePlanning.ItemPlanningId, updateModel, areaRulePlanning.Id, oldItemPlanningTagId, true);
                }
                    break;
            }

            return new OperationResult(true, _localizationService.GetString("TaskUpdatedSuccessful"));
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            _logger.LogError(e.Message);
            return new OperationResult(false,
                _localizationService.GetString("ErrorWhileUpdatingTask"));
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult> DeleteTask(int id)
    {
        try
        {
            var core = await _coreHelper.GetCore();
            var sdkDbContext = core.DbContextHelper.GetDbContext();
            var areaRulePlanning = _backendConfigurationPnDbContext.AreaRulePlannings
                .Where(x => x.Id == id)
                .Include(x => x.AreaRule)
                .ThenInclude(x => x.AreaRuleTranslations)
                .Include(x => x.PlanningSites)
                .FirstOrDefault(x => x.WorkflowState != Constants.WorkflowStates.Removed);

            if (areaRulePlanning == null)
            {
                return new OperationResult(false,
                    _localizationService.GetString("TaskNotFound"));
            }

            if (areaRulePlanning.ItemPlanningId != 0)
            {
                var planning = _itemsPlanningPnDbContext.Plannings
                    .Where(x => x.Id == areaRulePlanning.ItemPlanningId)
                    .First(x => x.WorkflowState != Constants.WorkflowStates.Removed);

                planning.UpdatedByUserId = _userService.UserId;
                await planning.Delete(_itemsPlanningPnDbContext);
            }

            // delete area rule planning and linked object
            foreach (var areaRuleAreaRuleTranslation in areaRulePlanning.AreaRule.AreaRuleTranslations)
            {
                areaRuleAreaRuleTranslation.UpdatedByUserId = _userService.UserId;
                await areaRuleAreaRuleTranslation.Delete(_backendConfigurationPnDbContext);
            }

            foreach (var planningSite in areaRulePlanning.PlanningSites)
            {
                planningSite.UpdatedByUserId = _userService.UserId;
                await planningSite.Delete(_backendConfigurationPnDbContext);
            }

            var planningCases = await _itemsPlanningPnDbContext.PlanningCases
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.PlanningId == areaRulePlanning.ItemPlanningId)
                .ToListAsync().ConfigureAwait(false);

            foreach (var planningCase in planningCases)
            {
                var planningCaseSites = await _itemsPlanningPnDbContext.PlanningCaseSites
                    .Where(x => x.PlanningCaseId == planningCase.Id)
                    .Where(planningCaseSite => planningCaseSite.MicrotingSdkCaseId != 0 ||
                                               planningCaseSite.MicrotingCheckListSitId != 0)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .ToListAsync().ConfigureAwait(false);
                foreach (var planningCaseSite in planningCaseSites)
                {
                    var result =
                        await sdkDbContext.Cases.SingleOrDefaultAsync(x => x.Id == planningCaseSite.MicrotingSdkCaseId)
                            .ConfigureAwait(false);
                    if (result is { MicrotingUid: { } })
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
            }

            areaRulePlanning.AreaRule.UpdatedByUserId = _userService.UserId;
            await areaRulePlanning.AreaRule.Delete(_backendConfigurationPnDbContext);

            areaRulePlanning.UpdatedByUserId = _userService.UserId;
            await areaRulePlanning.Delete(_backendConfigurationPnDbContext);

            var complianceList = await _backendConfigurationPnDbContext.Compliances
                .Where(x => x.PlanningId == areaRulePlanning.ItemPlanningId
                            && x.WorkflowState != Constants.WorkflowStates.Removed)
                .ToListAsync().ConfigureAwait(false);
            foreach (var compliance in complianceList)
            {
                if (compliance != null)
                {
                    await compliance.Delete(_backendConfigurationPnDbContext)
                        .ConfigureAwait(false);
                }
            }

            return new OperationResult(true, _localizationService.GetString("TaskDeletedSuccessful"));
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            _logger.LogError(e.Message);
            return new OperationResult(false,
                _localizationService.GetString("ErrorWhileDeletingTask"));
        }
    }

    private async Task<int> GetLogBooksAreaId()
    {
        return await _backendConfigurationPnDbContext.AreaTranslations
            .Where(x => x.Name == "01. Log books")
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .Select(x => x.AreaId)
            .FirstOrDefaultAsync();
    }

    private async Task<Planning> CreateItemPlanningObject(int eformId, string eformName, int folderId,
        TaskWizardCreateModel taskWizardCreateModel, int areaRuleId, int areaRulePlanningId)
    {
        var areaRule = await _backendConfigurationPnDbContext.AreaRules
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .Where(x => x.Id == areaRuleId)
            .Include(x => x.Area)
            .FirstAsync();

        var propertyItemPlanningTagId = await _backendConfigurationPnDbContext.Properties
            .Where(x => x.Id == areaRule.PropertyId)
            .Select(x => x.ItemPlanningTagId)
            .FirstAsync().ConfigureAwait(false);
        var planning = new Planning
        {
            CreatedByUserId = _userService.UserId,
            Enabled = taskWizardCreateModel.Status == TaskWizardStatuses.Active,
            RelatedEFormId = eformId,
            RelatedEFormName = eformName,
            SdkFolderId = folderId,
            DaysBeforeRedeploymentPushMessageRepeat = false,
            DaysBeforeRedeploymentPushMessage = 5,
            PushMessageOnDeployment = true,
            StartDate = taskWizardCreateModel.StartDate.HasValue ?
                new DateTime(taskWizardCreateModel.StartDate.Value.Year, taskWizardCreateModel.StartDate.Value.Month, taskWizardCreateModel.StartDate.Value.Day, 0, 0, 0) :
                new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, DateTime.UtcNow.Day, 0, 0, 0),
            IsLocked = true,
            IsEditable = false,
            IsHidden = true
        };

        await planning.Create(_itemsPlanningPnDbContext).ConfigureAwait(false);

        foreach (var planningSite in taskWizardCreateModel.Sites.Select(assignedSite =>
                     new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.PlanningSite
                     {
                         SiteId = assignedSite,
                         PlanningId = planning.Id,
                         CreatedByUserId = _userService.UserId,
                         UpdatedByUserId = _userService.UserId
                     }))
        {
            await planningSite.Create(_itemsPlanningPnDbContext).ConfigureAwait(false);
        }

        var itemPlanningTagIdsForPairWithPlanning = new List<int>
            { areaRule.Area.ItemPlanningTagId, propertyItemPlanningTagId };
            //{ propertyItemPlanningTagId };

        if (taskWizardCreateModel.ItemPlanningTagId.HasValue)
        {
            itemPlanningTagIdsForPairWithPlanning.Add(taskWizardCreateModel.ItemPlanningTagId.Value);
        }

        foreach (var planningTag in itemPlanningTagIdsForPairWithPlanning.Select(x => new PlanningsTags
                 {
                     PlanningId = planning.Id,
                     PlanningTagId = x,
                     CreatedByUserId = _userService.UserId,
                     UpdatedByUserId = _userService.UserId
                 }))
        {
            await planningTag.Create(_itemsPlanningPnDbContext);

            var areaRulePlanningTag = await _backendConfigurationPnDbContext.AreaRulePlanningTags
                .Where(x => x.AreaRulePlanningId == areaRulePlanningId)
                .Where(x => x.ItemPlanningTagId == planningTag.PlanningTagId)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .FirstOrDefaultAsync();

            if (areaRulePlanningTag == null)
            {
                areaRulePlanningTag = new AreaRulePlanningTag
                {
                    AreaRulePlanningId = areaRulePlanningId,
                    ItemPlanningTagId = planningTag.PlanningTagId,
                    CreatedByUserId = _userService.UserId,
                    UpdatedByUserId = _userService.UserId
                };
                await areaRulePlanningTag.Create(_backendConfigurationPnDbContext);
            }
        }

        return planning;
    }

    private async Task UpdateTags(int planningId, TaskWizardCreateModel updateModel, int areaRulePlanningId, int? oldItemPlanningTagId, bool updateItemPlanningTags = true)
    {
        var tagsQuery = _backendConfigurationPnDbContext.AreaRulePlanningTags
            .Where(x => x.AreaRulePlanningId == areaRulePlanningId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .AsQueryable();

        var tagsToDelete = await tagsQuery
            .Where(x => !updateModel.TagIds.Distinct().Contains(x.ItemPlanningTagId))
            .ToListAsync();

        var tagsToCreate = updateModel.TagIds.Distinct()
            .Except(tagsQuery
                .Select(tag => tag.ItemPlanningTagId)
                .ToList())
            .Select(tagId => new PlanningsTags
            {
                PlanningTagId = tagId,
                PlanningId = planningId,
                CreatedByUserId = _userService.UserId,
                UpdatedByUserId = _userService.UserId
            })
            .ToList();

        foreach (var tags in tagsToDelete)
        {
            if(updateItemPlanningTags)
            {
                var itemPlanningTag = await _itemsPlanningPnDbContext.PlanningsTags
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.PlanningId == planningId)
                    .Where(x => x.PlanningTagId == tags.ItemPlanningTagId)
                    .FirstOrDefaultAsync();
                if(itemPlanningTag != null)
                {
                    itemPlanningTag.UpdatedByUserId = _userService.UserId;
                    await itemPlanningTag.Delete(_itemsPlanningPnDbContext);
                }
            }
            tags.UpdatedByUserId = _userService.UserId;
            await tags.Delete(_backendConfigurationPnDbContext);
        }

        foreach (var tags in tagsToCreate)
        {
            if (updateItemPlanningTags)
            {
                PlanningsTags planningTagsTags = new()
                {
                    PlanningId = tags.PlanningId,
                    PlanningTagId = tags.PlanningTagId,
                    CreatedByUserId = _userService.UserId,
                    UpdatedByUserId = _userService.UserId
                };
                await planningTagsTags.Create(_itemsPlanningPnDbContext);
            }

            var areaRulePlanningTag = new AreaRulePlanningTag
            {
                AreaRulePlanningId = areaRulePlanningId,
                ItemPlanningTagId = tags.PlanningTagId,
                CreatedByUserId = _userService.UserId,
                UpdatedByUserId = _userService.UserId
            };
            await areaRulePlanningTag.Create(_backendConfigurationPnDbContext);
        }

        if (updateItemPlanningTags)
        {
            if (oldItemPlanningTagId.HasValue && updateModel.ItemPlanningTagId.HasValue)
            {
                var tagForDelete = await _itemsPlanningPnDbContext.PlanningsTags
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.PlanningId == planningId)
                    .Where(x => x.PlanningTagId == oldItemPlanningTagId.Value)
                    .FirstOrDefaultAsync();
                var tagForCreate = new PlanningsTags
                {
                    PlanningTagId = updateModel.ItemPlanningTagId.Value,
                    PlanningId = planningId,
                    CreatedByUserId = _userService.UserId,
                    UpdatedByUserId = _userService.UserId
                };

                if (tagForDelete != null)
                {
                    tagForDelete.UpdatedByUserId = _userService.UserId;
                    await tagForDelete.Delete(_itemsPlanningPnDbContext);
                }
                await tagForCreate.Create(_itemsPlanningPnDbContext);
            }
            else if (!oldItemPlanningTagId.HasValue && updateModel.ItemPlanningTagId.HasValue)
            {
                var tagForCreate = new PlanningsTags
                {
                    PlanningTagId = updateModel.ItemPlanningTagId.Value,
                    PlanningId = planningId,
                    CreatedByUserId = _userService.UserId,
                    UpdatedByUserId = _userService.UserId
                };
                await tagForCreate.Create(_itemsPlanningPnDbContext);
            }
            else if (oldItemPlanningTagId.HasValue && !updateModel.ItemPlanningTagId.HasValue)
            {
                var tagForDelete = await _itemsPlanningPnDbContext.PlanningsTags
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.PlanningId == planningId)
                    .Where(x => x.PlanningTagId == oldItemPlanningTagId.Value)
                    .FirstOrDefaultAsync();
                if (tagForDelete != null)
                {
                    tagForDelete.UpdatedByUserId = _userService.UserId;
                    await tagForDelete.Delete(_itemsPlanningPnDbContext);
                }
            }
            else if (!oldItemPlanningTagId.HasValue && !updateModel.ItemPlanningTagId.HasValue)
            {
                // nothing to do
            }
        }
    }
}