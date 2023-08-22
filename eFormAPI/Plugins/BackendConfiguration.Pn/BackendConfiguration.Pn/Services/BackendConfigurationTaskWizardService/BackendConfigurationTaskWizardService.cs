namespace BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService;

using Infrastructure.Helpers;
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
                .Where(x => x.AreaId == areaId);

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
                query = query.Where(x => x.PlanningSites.Any(y => request.Filters.AssignToIds.Contains(y.SiteId)));
            }

            if (request.Filters.Status != null)
            {
                var booleanStatus = request.Filters.Status == TaskWizardStatuses.Active;
                query = query.Where(x => x.Status == booleanStatus);
            }

            if (request.Filters.TagIds.Any())
            {
                if (request.Filters.TagIds.Any())
                {
                    query = query.Where(x => x.AreaRulePlanningTags
                                                 .Where(y => y.WorkflowState != Constants.WorkflowStates.Removed)
                                                 .Any(y => request.Filters.TagIds.Contains(y.ItemPlanningTagId)) ||
                                             x.ItemPlanningTagId.HasValue && request.Filters.TagIds.Contains(x.ItemPlanningTagId.Value));
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
                    x.Name,
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
                        Name = itemPlanningTagNames.ContainsKey(y.ItemPlanningTagId) ? itemPlanningTagNames[y.ItemPlanningTagId] : "",
                    })
                    .ToList(),
                // add report tag to all tags
                TagReport = new CommonTagModel
                {
                    Id = x.ItemPlanningTagId,
                    Name = itemPlanningTagNames.ContainsKey((int)x.ItemPlanningTagId) ? itemPlanningTagNames[(int)x.ItemPlanningTagId] : "",
                },
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
                    Tags = areaRule.Tags.Append(areaRule.TagReport).ToList(),
                    AssignedTo = siteNamesQuery.Where(x => areaRule.PlanningSites.Contains(x.Id)).Select(x => x.Name)
                        .ToList(),
                    Eform = eformNamesQuery.Where(x => x.CheckListId == areaRule.EformId).Select(x => x.Text)
                        .FirstOrDefault(),
                    Folder = folderNamesQuery.Where(x => x.FolderId == areaRule.FolderId).Select(x => x.Name)
                        .FirstOrDefault(),
                    CreatedInGuide = areaRule.CreatedInGuide,
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
                }).ToListAsync().ConfigureAwait(false);
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
                .Where(x => x.Id == id);

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
                        .ToList(),
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

            createModel.StartDate = createModel.StartDate == null
                ? new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, DateTime.UtcNow.Day, 0, 0, 0)
                : new DateTime(createModel.StartDate.Value.Year, createModel.StartDate.Value.Month,
                    createModel.StartDate.Value.Day, 0, 0, 0);

            if (createModel.RepeatType == RepeatType.Day && createModel.RepeatEvery == 1)
            {
                createModel.RepeatEvery = 0;
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
                Enabled = true,
                IsLocked = true,
                IsHidden = false,
                StartDate = (DateTime)createModel.StartDate,
                RepeatType = (Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType)createModel.RepeatType,
                RelatedEFormId = createModel.EformId,
                RelatedEFormName = eformName,
                RepeatEvery = createModel.RepeatEvery,
                SdkFolderId = createModel.FolderId,
                SdkFolderName = folderName,
                DayOfWeek = dayOfWeek,
                DayOfMonth = dayOfMonth,
                // PlanningsTags = createModel.TagIds
                //     .Select(x => new PlanningsTags
                //     {
                //         PlanningTagId = x,
                //         UpdatedByUserId = _userService.UserId,
                //         CreatedByUserId = _userService.UserId,
                //     })
                //     .ToList(),
                // PlanningSites = createModel.Sites
                //     .Select(x => new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.PlanningSite
                //     {
                //         SiteId = x,
                //         UpdatedByUserId = _userService.UserId,
                //         CreatedByUserId = _userService.UserId,
                //     })
                //     .ToList(),
                // NameTranslations = createModel.Translates
                //     .Select(x => new PlanningNameTranslation
                //     {
                //         LanguageId = x.LanguageId,
                //         Name = x.Name,
                //         UpdatedByUserId = _userService.UserId,
                //         CreatedByUserId = _userService.UserId,
                //     })
                //     .ToList(),
                UpdatedByUserId = _userService.UserId,
                CreatedByUserId = _userService.UserId,
            };

            if (createModel.ItemPlanningTagId.HasValue)
            {
                planning.PlanningsTags.Add(new PlanningsTags
                {
                    PlanningTagId = createModel.ItemPlanningTagId.Value,
                    UpdatedByUserId = _userService.UserId,
                    CreatedByUserId = _userService.UserId
                });
            }

            await planning.Create(_itemsPlanningPnDbContext);

            foreach (var assignedSite in createModel.Sites)
            {
                var planningSite = new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.PlanningSite
                {
                    SiteId = assignedSite,
                    PlanningId = planning.Id,
                    CreatedByUserId = _userService.UserId,
                    UpdatedByUserId = _userService.UserId
                };
                await planningSite.Create(_itemsPlanningPnDbContext).ConfigureAwait(false);
            }

            foreach (var tagId in createModel.TagIds)
            {
                var planningsTags = new PlanningsTags
                {
                    PlanningId = planning.Id,
                    PlanningTagId = tagId,
                    CreatedByUserId = _userService.UserId,
                    UpdatedByUserId = _userService.UserId
                };
                await planningsTags.Create(_itemsPlanningPnDbContext).ConfigureAwait(false);
            }

            foreach (var translationsModel in createModel.Translates)
            {
                var planningNameTranslation = new PlanningNameTranslation
                {
                    PlanningId = planning.Id,
                    LanguageId = translationsModel.LanguageId,
                    Name = translationsModel.Name,
                    CreatedByUserId = _userService.UserId,
                    UpdatedByUserId = _userService.UserId
                };
                await planningNameTranslation.Create(_itemsPlanningPnDbContext).ConfigureAwait(false);
            }

            foreach (var assignedSite in createModel.Sites)
            {
                var planningSite = new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.PlanningSite
                {
                    SiteId = assignedSite,
                    PlanningId = planning.Id,
                    CreatedByUserId = _userService.UserId,
                    UpdatedByUserId = _userService.UserId
                };
                await planningSite.Create(_itemsPlanningPnDbContext).ConfigureAwait(false);
            }

            foreach (var tagId in createModel.TagIds)
            {
                var planningsTags = new PlanningsTags
                {
                    PlanningId = planning.Id,
                    PlanningTagId = tagId,
                    CreatedByUserId = _userService.UserId,
                    UpdatedByUserId = _userService.UserId
                };
                await planningsTags.Create(_itemsPlanningPnDbContext).ConfigureAwait(false);
            }

            foreach (var translationsModel in createModel.Translates)
            {
                var planningNameTranslation = new PlanningNameTranslation
                {
                    PlanningId = planning.Id,
                    LanguageId = translationsModel.LanguageId,
                    Name = translationsModel.Name,
                    CreatedByUserId = _userService.UserId,
                    UpdatedByUserId = _userService.UserId
                };
                await planningNameTranslation.Create(_itemsPlanningPnDbContext).ConfigureAwait(false);
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
                        CreatedByUserId = _userService.UserId,
                    })
                    .ToList(),
                EformId = createModel.EformId,
                EformName = eformName,
                PropertyId = createModel.PropertyId,
                FolderId = createModel.FolderId,
                FolderName = folderName,
                RepeatEvery = createModel.RepeatEvery,
                RepeatType = (int?)createModel.RepeatType,
                Notifications = true,
                NotificationsModifiable = false,
                ComplianceEnabled = true,
                AreaRulesPlannings = new List<AreaRulePlanning>
                {
                    new()
                    {
                        AreaId = areaId,
                        FolderId = createModel.FolderId,
                        ItemPlanningId = planning.Id,
                        PlanningSites = createModel.Sites
                            .Select(x =>
                                new Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.PlanningSite
                                {
                                    AreaId = areaId,
                                    SiteId = x,
                                    Status = 0,
                                    UpdatedByUserId = _userService.UserId,
                                    CreatedByUserId = _userService.UserId,
                                })
                            .ToList(),
                        Status = createModel.Status == TaskWizardStatuses.Active,
                        StartDate = createModel.StartDate,
                        SendNotifications = true,
                        RepeatEvery = createModel.RepeatEvery,
                        RepeatType = (int?)createModel.RepeatType,
                        PropertyId = createModel.PropertyId,
                        ItemPlanningTagId = createModel.ItemPlanningTagId,
                        UpdatedByUserId = _userService.UserId,
                        CreatedByUserId = _userService.UserId,
                        AreaRulePlanningTags = createModel.TagIds
                            .Select(x => new AreaRulePlanningTag
                            {
                                ItemPlanningTagId = x,
                                UpdatedByUserId = _userService.UserId,
                                CreatedByUserId = _userService.UserId,
                            })
                            .ToList(),
                    }
                },
                UpdatedByUserId = _userService.UserId,
                CreatedByUserId = _userService.UserId,
            };
            await areRule.Create(_backendConfigurationPnDbContext);

            await PairItemWithSiteHelper.Pair(
                createModel.Sites,
                createModel.EformId,
                planning.Id,
                createModel.FolderId, core, _itemsPlanningPnDbContext, true, _localizationService);
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
                .Include(x => x.AreaRule)
                .ThenInclude(x => x.AreaRuleTranslations)
                .Include(x => x.PlanningSites)
                .FirstOrDefault(x => x.WorkflowState != Constants.WorkflowStates.Removed);

            if (areaRulePlanning == null)
            {
                return new OperationResult(false,
                    _localizationService.GetString("TaskNotFound"));
            }

            var currentSiteIds = areaRulePlanning.PlanningSites.Select(ps => ps.SiteId).ToList();
            var sitesToAdd = updateModel.Sites.Except(currentSiteIds).ToList();
            var sitesToRemove = currentSiteIds.Except(updateModel.Sites).ToList();

            var areaId = await GetLogBooksAreaId();

            // update area rule plannings and area rule with translations
            var oldStatus = areaRulePlanning.Status;
            areaRulePlanning.FolderId = updateModel.FolderId;
            areaRulePlanning.Status = updateModel.Status == TaskWizardStatuses.Active;
            areaRulePlanning.StartDate = updateModel.StartDate;
            areaRulePlanning.RepeatEvery = updateModel.RepeatEvery;
            areaRulePlanning.RepeatType = (int?)updateModel.RepeatType;
            areaRulePlanning.PropertyId = updateModel.PropertyId;
            var oldItemPlanningTagId = areaRulePlanning.ItemPlanningTagId;
            areaRulePlanning.ItemPlanningTagId = updateModel.ItemPlanningTagId;
            areaRulePlanning.UpdatedByUserId = _userService.UserId;
            await areaRulePlanning.Update(_backendConfigurationPnDbContext);

            foreach (var site in sitesToAdd
                         .Select(x =>
                             new Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.PlanningSite
                             {
                                 SiteId = x,
                                 AreaRulePlanningsId = areaRulePlanning.Id,
                                 AreaId = areaId,
                                 AreaRuleId = areaRulePlanning.AreaId,
                                 CreatedByUserId = _userService.UserId,
                                 UpdatedByUserId = _userService.UserId,
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
            areaRulePlanning.AreaRule.FolderId = updateModel.FolderId;
            areaRulePlanning.AreaRule.FolderName = folderName;
            areaRulePlanning.AreaRule.RepeatEvery = updateModel.RepeatEvery;
            areaRulePlanning.AreaRule.RepeatType = (int?)updateModel.RepeatType;
            areaRulePlanning.AreaRule.UpdatedByUserId = _userService.UserId;
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
                             UpdatedByUserId = _userService.UserId,
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
                    if (areaRulePlanning.AreaRule.FolderId == 0)
                    {
                        var folderId = await _backendConfigurationPnDbContext
                            .ProperyAreaFolders
                            .Include(x => x.AreaProperty)
                            .Where(x => x.AreaProperty.PropertyId ==
                                        areaRulePlanning.PropertyId)
                            .Where(x => x.AreaProperty.AreaId == areaRulePlanning.AreaId)
                            .Select(x => x.FolderId)
                            .FirstOrDefaultAsync().ConfigureAwait(false);
                        if (folderId != 0)
                        {
                            areaRulePlanning.AreaRule.FolderId = folderId;
                            areaRulePlanning.AreaRule.FolderName = await sdkDbContext.FolderTranslations
                                .Where(x => x.FolderId == folderId)
                                .Where(x => x.LanguageId == 1) // danish
                                .Select(x => x.Name)
                                .FirstAsync().ConfigureAwait(false);
                            await areaRulePlanning.AreaRule.Update(_backendConfigurationPnDbContext)
                                .ConfigureAwait(false);
                        }
                    }

                    var planning = await CreateItemPlanningObject(updateModel.EformId,
                        areaRulePlanning.AreaRule.EformName, areaRulePlanning.AreaRule.FolderId, updateModel,
                        areaRulePlanning.AreaRule, areaRulePlanning.Id);
                    planning.NameTranslations = areaRulePlanning.AreaRule.AreaRuleTranslations.Select(
                        areaRuleAreaRuleTranslation => new PlanningNameTranslation
                        {
                            LanguageId = areaRuleAreaRuleTranslation.LanguageId,
                            Name = areaRuleAreaRuleTranslation.Name
                        }).ToList();
                    planning.DayOfMonth = 1;
                    planning.DayOfWeek = DayOfWeek.Monday;
                    planning.RepeatEvery = updateModel.RepeatEvery;
                    planning.RepeatType =
                        (Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType)updateModel.RepeatType;

                    foreach (var planningSite in areaRulePlanning.PlanningSites.Where(x => x.Status == 0))
                    {
                        planningSite.Status = 33;
                        await planningSite.Update(_backendConfigurationPnDbContext);
                    }

                    await planning.Update(_itemsPlanningPnDbContext).ConfigureAwait(false);
                    await PairItemWithSiteHelper.Pair(
                            areaRulePlanning.PlanningSites
                                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                .Select(x => x.SiteId).ToList(),
                            updateModel.EformId,
                            planning.Id,
                            areaRulePlanning.AreaRule.FolderId, core, _itemsPlanningPnDbContext,
                            areaRulePlanning.UseStartDateAsStartOfPeriod, _localizationService)
                        .ConfigureAwait(false);
                    areaRulePlanning.ItemPlanningId = planning.Id;
                    await areaRulePlanning.Update(_backendConfigurationPnDbContext)
                        .ConfigureAwait(false);
                    break;
                }
                // delete item planning
                case true when !areaRulePlanning.Status:
                    if (areaRulePlanning.ItemPlanningId != 0)
                    {

                        // update planning tags
                        var planning = await _itemsPlanningPnDbContext.Plannings
                            .Where(x => x.Id == areaRulePlanning.ItemPlanningId)
                            .Include(x => x.NameTranslations)
                            .Include(x => x.PlanningsTags)
                            .Include(x => x.PlanningSites)
                            .FirstAsync(x => x.WorkflowState != Constants.WorkflowStates.Removed);

                        var tagsToDelete = planning.PlanningsTags
                            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                            .Where(x => !updateModel.TagIds.Contains(x.PlanningTagId))
                            .Select(x => x.PlanningTagId)
                            .ToList();

                        var tagsToCreate = updateModel.TagIds
                            .Where(x => !planning.PlanningsTags
                                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                .Select(y => y.PlanningTagId)
                                .Contains(x))
                            .Select(x => new AreaRulePlanningTag
                            {
                                AreaRulePlanningId = areaRulePlanning.Id,
                                ItemPlanningTagId = x,
                                CreatedByUserId = _userService.UserId,
                                UpdatedByUserId = _userService.UserId,
                            })
                            .ToList();

                        if (updateModel.ItemPlanningTagId.HasValue && oldItemPlanningTagId.HasValue &&
                            !oldItemPlanningTagId.Value.Equals(updateModel.ItemPlanningTagId.Value))
                        {
                            tagsToDelete.Add(planning.PlanningsTags.Where(x =>
                                x.PlanningTagId == oldItemPlanningTagId.Value)
                                .Select(x => x.PlanningTagId)
                                .First());
                        }

                        foreach (var tagId in tagsToDelete)
                        {
                            await _backendConfigurationPnDbContext.AreaRulePlanningTags
                                .First(x => x.AreaRulePlanningId == areaRulePlanning.Id && x.ItemPlanningTagId == tagId)
                                .Delete(_backendConfigurationPnDbContext);
                        }

                        foreach (var tags in tagsToCreate)
                        {
                            await tags.Create(_backendConfigurationPnDbContext);
                        }

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

                        await BackendConfigurationAreaRulePlanningsServiceHelper.DeleteItemPlanning(
                                areaRulePlanning.ItemPlanningId, core, _userService.UserId,
                                _backendConfigurationPnDbContext, _itemsPlanningPnDbContext)
                            .ConfigureAwait(false);

                        areaRulePlanning.ItemPlanningId = 0;
                        areaRulePlanning.Status = false;
                        await areaRulePlanning.Update(_backendConfigurationPnDbContext)
                            .ConfigureAwait(false);

                        var planningSites = await _backendConfigurationPnDbContext.PlanningSites
                            .Where(x => x.AreaRulePlanningsId == areaRulePlanning.Id)
                            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                            .ToListAsync().ConfigureAwait(false);

                        foreach (var planningSite in planningSites) // delete all planning sites
                        {
                            planningSite.Status = 0;
                            await planningSite.Update(_backendConfigurationPnDbContext)
                                .ConfigureAwait(false);
                            // await planningSite.Delete(_backendConfigurationPnDbContext).ConfigureAwait(false);
                        }
                    }

                    break;
                // update item planning
                case true when areaRulePlanning.Status:
                    // TODO, this is not possible to do, since the web interface does not allow to update active plannings
                    if (areaRulePlanning.ItemPlanningId !=
                        0) // Since ItemPlanningId is not 0, we already have a planning and therefore just update it
                    {
                        var planning = await _itemsPlanningPnDbContext.Plannings
                            .Where(x => x.Id == areaRulePlanning.ItemPlanningId)
                            .Include(x => x.NameTranslations)
                            .Include(x => x.PlanningsTags)
                            .Include(x => x.PlanningSites)
                            .FirstAsync(x => x.WorkflowState != Constants.WorkflowStates.Removed);
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
                                         UpdatedByUserId = _userService.UserId,
                                     })
                                     .ToList())
                        {
                            await translation.Create(_itemsPlanningPnDbContext);
                        }

                        if (updateModel.ItemPlanningTagId.HasValue)
                        {
                            updateModel.TagIds.Add(updateModel.ItemPlanningTagId.Value);
                        }

                        // update planning tags
                        var tagsToDelete = planning.PlanningsTags
                            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                            .Where(x => !updateModel.TagIds.Contains(x.PlanningTagId))
                            .ToList();

                        var tagsToCreate = updateModel.TagIds
                            .Where(x => !planning.PlanningsTags
                                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                .Select(y => y.PlanningTagId)
                                .Contains(x))
                            .Select(x => new PlanningsTags
                            {
                                PlanningTagId = x,
                                PlanningId = planning.Id,
                                CreatedByUserId = _userService.UserId,
                                UpdatedByUserId = _userService.UserId,
                            })
                            .ToList();

                        if (updateModel.ItemPlanningTagId.HasValue && oldItemPlanningTagId.HasValue &&
                            !oldItemPlanningTagId.Value.Equals(updateModel.ItemPlanningTagId.Value))
                        {
                            tagsToDelete.Add(planning.PlanningsTags.First(x =>
                                x.PlanningTagId == oldItemPlanningTagId.Value));
                        }

                        foreach (var tags in tagsToDelete)
                        {
                            tags.UpdatedByUserId = _userService.UserId;
                            await tags.Delete(_itemsPlanningPnDbContext);
                            await _backendConfigurationPnDbContext.AreaRulePlanningTags
                                .First(x => x.AreaRulePlanningId == areaRulePlanning.Id && x.ItemPlanningTagId == tags.PlanningTagId)
                                .Delete(_backendConfigurationPnDbContext);
                        }

                        foreach (var tags in tagsToCreate)
                        {
                            await tags.Create(_itemsPlanningPnDbContext);

                            var areaRulePlanningTag = new AreaRulePlanningTag
                            {
                                AreaRulePlanningId = areaRulePlanning.Id,
                                ItemPlanningTagId = tags.PlanningTagId,
                                CreatedByUserId = _userService.UserId,
                                UpdatedByUserId = _userService.UserId
                            };
                            await areaRulePlanningTag.Create(_backendConfigurationPnDbContext);
                        }

                        // update planning sites
                        currentSiteIds = planning.PlanningSites.Select(ps => ps.SiteId).ToList();

                        sitesToAdd = updateModel.Sites.Except(currentSiteIds).ToList();
                        foreach (var site in sitesToAdd
                                     .Select(x =>
                                         new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.PlanningSite
                                         {
                                             SiteId = x,
                                             PlanningId = planning.Id,
                                             CreatedByUserId = _userService.UserId,
                                             UpdatedByUserId = _userService.UserId,
                                         }))
                        {
                            await site.Create(_itemsPlanningPnDbContext);
                        }

                        if (sitesToAdd.Count > 0)
                        {
                            await PairItemWithSiteHelper.Pair(
                                sitesToAdd,
                                updateModel.EformId,
                                planning.Id,
                                (int)planning.SdkFolderId, core, _itemsPlanningPnDbContext,
                                areaRulePlanning.UseStartDateAsStartOfPeriod, _localizationService);
                        }

                        sitesToRemove = currentSiteIds.Except(updateModel.Sites).ToList();
                        foreach (var site in sitesToRemove.Select(siteId =>
                                     planning.PlanningSites.First(x => x.SiteId == siteId)))
                        {
                            site.UpdatedByUserId = _userService.UserId;
                            await site.Delete(_itemsPlanningPnDbContext);
                            var someList = await _itemsPlanningPnDbContext.PlanningCaseSites
                                .Where(x => x.PlanningId == planning.Id)
                                .Where(x => x.MicrotingSdkSiteId == site.SiteId)
                                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                .ToListAsync();

                            foreach (var planningCaseSite in someList)
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
                // nothing to do
                case false when !areaRulePlanning.Status:
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
            var core = await _coreHelper.GetCore().ConfigureAwait(false);
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

            var planning = _itemsPlanningPnDbContext.Plannings
                .Where(x => x.Id == areaRulePlanning.ItemPlanningId)
                .Include(x => x.NameTranslations)
                .Include(x => x.PlanningsTags)
                .Include(x => x.PlanningSites)
                .Include(x => x.PlanningCases)
                .First(x => x.WorkflowState != Constants.WorkflowStates.Removed);

            foreach (var translation in planning.NameTranslations)
            {
                translation.UpdatedByUserId = _userService.UserId;
                await translation.Delete(_itemsPlanningPnDbContext);
            }

            foreach (var planningSite in planning.PlanningSites)
            {
                planningSite.UpdatedByUserId = _userService.UserId;
                await planningSite.Delete(_itemsPlanningPnDbContext);
            }

            foreach (var planningsTag in planning.PlanningsTags)
            {
                planningsTag.UpdatedByUserId = _userService.UserId;
                await planningsTag.Delete(_itemsPlanningPnDbContext);
            }

            planning.UpdatedByUserId = _userService.UserId;
            await planning.Delete(_itemsPlanningPnDbContext);

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

            var planningCases = planning.PlanningCases
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .ToList();

            foreach (var planningCase in planningCases)
            {
                var planningCaseSites = await _itemsPlanningPnDbContext.PlanningCaseSites
                    .Where(x => x.PlanningCaseId == planningCase.Id)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .ToListAsync().ConfigureAwait(false);
                foreach (var planningCaseSite in planningCaseSites)
                {
                    var result =
                        await sdkDbContext.Cases.SingleOrDefaultAsync(x => x.Id == planningCaseSite.MicrotingSdkCaseId).ConfigureAwait(false);
                    if (result is { MicrotingUid: { } })
                    {
                        await core.CaseDelete((int)result.MicrotingUid).ConfigureAwait(false);
                    }
                    else
                    {
                        var clSites = await sdkDbContext.CheckListSites.SingleOrDefaultAsync(x =>
                            x.Id == planningCaseSite.MicrotingCheckListSitId).ConfigureAwait(false);

                        if (clSites != null)
                        {
                            await core.CaseDelete(clSites.MicrotingUid).ConfigureAwait(false);
                        }
                    }
                }

                // Delete planning case
                await planningCase.Delete(_itemsPlanningPnDbContext).ConfigureAwait(false);
            }

            areaRulePlanning.AreaRule.UpdatedByUserId = _userService.UserId;
            await areaRulePlanning.AreaRule.Delete(_backendConfigurationPnDbContext);

            areaRulePlanning.UpdatedByUserId = _userService.UserId;
            await areaRulePlanning.Delete(_backendConfigurationPnDbContext);

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
        TaskWizardCreateModel taskWizardCreateModel, AreaRule areaRule, int areaRulePlanningId)
    {
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
            StartDate = new DateTime(taskWizardCreateModel.StartDate.Value.Year, taskWizardCreateModel.StartDate.Value.Month,
                taskWizardCreateModel.StartDate.Value.Day, 0, 0, 0),
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

        var itemPlanningTagIdsForPairWithPlanning = new List<int>()
            { areaRule.Area.ItemPlanningTagId, propertyItemPlanningTagId };

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

            var areaRulePlanningTag = new AreaRulePlanningTag
            {
                AreaRulePlanningId = areaRulePlanningId,
                ItemPlanningTagId = planningTag.PlanningTagId,
                CreatedByUserId = _userService.UserId,
                UpdatedByUserId = _userService.UserId
            };
            await areaRulePlanningTag.Create(_backendConfigurationPnDbContext);
        }

        return planning;
    }
}