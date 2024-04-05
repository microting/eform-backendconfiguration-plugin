using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Helpers;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.eFormApi.BasePn.Infrastructure.Models.Application.Case.CaseEdit;
using Microting.ItemsPlanningBase.Infrastructure.Data;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;

namespace BackendConfiguration.Pn.Services.BackendConfigurationCaseService;

public class BackendConfigurationCaseService : IBackendConfigurationCaseService
{
    private readonly ILogger<BackendConfigurationCaseService> _logger;
    private readonly IBackendConfigurationLocalizationService _localizationService;
    // private readonly IWordService _wordService;
    // private readonly IPlanningExcelService _excelService;
    private readonly IEFormCoreService _coreHelper;
    // private readonly ICasePostBaseService _casePostBaseService;
    private readonly ItemsPlanningPnDbContext _dbContext;
    private readonly IUserService _userService;

    public BackendConfigurationCaseService(ItemsPlanningPnDbContext dbContext, ILogger<BackendConfigurationCaseService> logger, IEFormCoreService coreHelper, IBackendConfigurationLocalizationService localizationService, IUserService userService)
    {
        _dbContext = dbContext;
        _logger = logger;
        _coreHelper = coreHelper;
        _localizationService = localizationService;
        _userService = userService;
    }

    public async Task<OperationResult> Update(ReplyRequest model)
    {
        var checkListValueList = new List<string>();
        var fieldValueList = new List<string>();
        var core = await _coreHelper.GetCore();
        var language = await _userService.GetCurrentUserLanguage();
        var currentUser = await _userService.GetCurrentUserAsync();
        try
        {
            model.ElementList.ForEach(element =>
            {
                checkListValueList.AddRange(CaseUpdateHelper.GetCheckList(element));
                fieldValueList.AddRange(CaseUpdateHelper.GetFieldList(element));
            });
        }
        catch (Exception ex)
        {
            Log.LogException(ex.Message);
            Log.LogException(ex.StackTrace);
            return new OperationResult(false, $"{_localizationService.GetString("CaseCouldNotBeUpdated")} Exception: {ex.Message}");
        }

        try
        {
            await core.CaseUpdate(model.Id, fieldValueList, checkListValueList);
            await core.CaseUpdateFieldValues(model.Id, language);
            var sdkDbContext = core.DbContextHelper.GetDbContext();

            var foundCase = await sdkDbContext.Cases
                .Where(x => x.Id == model.Id
                            && x.WorkflowState != Constants.WorkflowStates.Removed)
                .FirstOrDefaultAsync();

            if(foundCase != null) {

                if (foundCase.DoneAt != null)
                {
                    var newDoneAt = new DateTime(model.DoneAt.Year, model.DoneAt.Month, model.DoneAt.Day, foundCase.DoneAt.Value.Hour, foundCase.DoneAt.Value.Minute, foundCase.DoneAt.Value.Second);
                    foundCase.DoneAtUserModifiable = newDoneAt;
                }

                // foundCase.SiteId = sdkDbContext.Sites
                //     .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                //     .Single(x => x.Name == $"{currentUser.FirstName} {currentUser.LastName}").Id;
                foundCase.Status = 100;
                foundCase.SiteId = model.SiteId;
                await foundCase.Update(sdkDbContext);
                var planningCase = await _dbContext.PlanningCases.SingleAsync(x => x.MicrotingSdkCaseId == model.Id);
                var planningCaseSite = await _dbContext.PlanningCaseSites.FirstOrDefaultAsync(x => x.MicrotingSdkCaseId == model.Id && x.PlanningCaseId == planningCase.Id && x.Status == 100);

                if (planningCaseSite == null)
                {
                    planningCaseSite = new PlanningCaseSite
                    {
                        MicrotingSdkCaseId = model.Id,
                        PlanningCaseId = planningCase.Id,
                        MicrotingSdkeFormId = planningCase.MicrotingSdkeFormId,
                        PlanningId = planningCase.PlanningId,
                        Status = 100,
                        MicrotingSdkSiteId = (int)foundCase.SiteId
                    };
                    await planningCaseSite.Create(_dbContext);
                }

                planningCaseSite.MicrotingSdkCaseDoneAt = foundCase.DoneAtUserModifiable;
                planningCaseSite = await SetFieldValue(planningCaseSite, foundCase.Id, language);
                await planningCaseSite.Update(_dbContext);

                planningCase.MicrotingSdkCaseDoneAt = foundCase.DoneAtUserModifiable;
                planningCase = await SetFieldValue(planningCase, foundCase.Id, language);
                await planningCase.Update(_dbContext);
            }
            else
            {
                return new OperationResult(false, _localizationService.GetString("CaseNotFound"));
            }

            return new OperationResult(true, _localizationService.GetString("CaseHasBeenUpdated"));
        }
        catch (Exception ex)
        {
            Log.LogException(ex.Message);
            Log.LogException(ex.StackTrace);
            return new OperationResult(false, _localizationService.GetString("CaseCouldNotBeUpdated") + $" Exception: {ex.Message}");
        }
    }

    private async Task<PlanningCaseSite> SetFieldValue(PlanningCaseSite planningCaseSite, int caseId, Language language)
        {
            var planning = _dbContext.Plannings.SingleOrDefault(x => x.Id == planningCaseSite.PlanningId);
            var caseIds = new List<int>
            {
                planningCaseSite.MicrotingSdkCaseId
            };

            var core = await _coreHelper.GetCore();
            var fieldValues = await core.Advanced_FieldValueReadList(caseIds, language);

            if (planning == null) return planningCaseSite;
            if (planning.NumberOfImagesEnabled)
            {
                planningCaseSite.NumberOfImages = 0;
                foreach (var fieldValue in fieldValues)
                {
                    if (fieldValue.FieldType == Constants.FieldTypes.Picture)
                    {
                        if (fieldValue.UploadedData != null)
                        {
                            planningCaseSite.NumberOfImages += 1;
                        }
                    }
                }
            }

            return planningCaseSite;
        }

        private async Task<PlanningCase> SetFieldValue(PlanningCase planningCase, int caseId, Language language)
        {
            var core = await _coreHelper.GetCore();
            var planning = await _dbContext.Plannings.SingleOrDefaultAsync(x => x.Id == planningCase.PlanningId).ConfigureAwait(false);
            var caseIds = new List<int> { planningCase.MicrotingSdkCaseId };
            var fieldValues = await core.Advanced_FieldValueReadList(caseIds, language).ConfigureAwait(false);

            if (planning == null) return planningCase;
            if (planning.NumberOfImagesEnabled)
            {
                planningCase.NumberOfImages = 0;
                foreach (var fieldValue in fieldValues)
                {
                    if (fieldValue.FieldType == Constants.FieldTypes.Picture)
                    {
                        if (fieldValue.UploadedData != null)
                        {
                            planningCase.NumberOfImages += 1;
                        }
                    }
                }
            }

            return planningCase;
        }
}