using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Helpers;
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
using Sentry;

namespace BackendConfiguration.Pn.Services.BackendConfigurationCaseService;

public class BackendConfigurationCaseService(
    ItemsPlanningPnDbContext dbContext,
    ILogger<BackendConfigurationCaseService> logger,
    IEFormCoreService coreHelper,
    IBackendConfigurationLocalizationService localizationService,
    IUserService userService)
    : IBackendConfigurationCaseService
{
    /// <summary>
    /// Clock seam for the done-date check (#1373), instance-level like the other services'
    /// so a test can pin "now" around Copenhagen midnight.
    /// </summary>
    internal Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

    public async Task<OperationResult> Update(ReplyRequest model)
    {
        var checkListValueList = new List<string>();
        var fieldValueList = new List<string>();
        var core = await coreHelper.GetCore();
        var language = await userService.GetCurrentUserLanguage();
        var currentUser = await userService.GetCurrentUserAsync();
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
            SentrySdk.CaptureException(ex);
            logger.LogError(ex.Message);
            logger.LogTrace(ex.StackTrace);
            return new OperationResult(false, $"{localizationService.GetString("CaseCouldNotBeUpdated")} Exception: {ex.Message}");
        }

        try
        {
            var sdkDbContext = core.DbContextHelper.GetDbContext();

            // Read-only pre-flight: the new done instant is computed, and refused, BEFORE
            // core.CaseUpdate writes anything. The tracked instance that is mutated is
            // re-read below, after core.CaseUpdate, so it carries that update's state.
            var originalDoneAt = await sdkDbContext.Cases
                .AsNoTracking()
                .Where(x => x.Id == model.Id)
                .Select(x => new { x.DoneAt })
                .FirstOrDefaultAsync();
            if (originalDoneAt == null)
            {
                return new OperationResult(false, localizationService.GetString("CaseNotFound"));
            }

            // #1373 — the picked DAY (a picked day arrives as its UTC midnight, an unchanged
            // one as the stored instant; both read back as the Danish date the user saw) at
            // the original completion's Danish time of day, or now's when there is none.
            // Built in Danish time, so the stored instant falls on the picked Danish day.
            var utcNow = UtcNow();
            var timeOfDay = ComplianceFutureTaskGuard
                .CopenhagenTimeOf(originalDoneAt.DoneAt ?? utcNow).TimeOfDay;
            var newDoneAt = DateTime.SpecifyKind(
                ComplianceFutureTaskGuard.UtcFromCopenhagen(
                    ComplianceFutureTaskGuard.DateInCopenhagen(model.DoneAt) + timeOfDay),
                DateTimeKind.Unspecified);

            // A completed log is placed on its done date everywhere (Rapport, Detaljer,
            // Oversigt, Kalender), so a done date after today would file it under a day
            // that has not happened yet. The date pickers stop at today; this is the
            // server's half, judged on the instant that would be stored.
            if (ComplianceFutureTaskGuard.DateInCopenhagen(newDoneAt)
                > ComplianceFutureTaskGuard.TodayInCopenhagen(utcNow))
            {
                logger.LogWarning(
                    "BackendConfigurationCaseService.Update: case {CaseId} done date {DoneAt:yyyy-MM-dd} is after today - nothing was mutated",
                    model.Id, newDoneAt);
                return new OperationResult(false, localizationService.GetString("DoneDateCannotBeInTheFuture"));
            }

            await core.CaseUpdate(model.Id, fieldValueList, checkListValueList);
            await core.CaseUpdateFieldValues(model.Id, language);

            var foundCase = await sdkDbContext.Cases
                .Where(x => x.Id == model.Id)
                .FirstOrDefaultAsync();

            if(foundCase != null) {

                foundCase.DoneAtUserModifiable = newDoneAt;

                foundCase.Status = 100;
                if (model.SiteId != 0)
                {
                    foundCase.SiteId = model.SiteId;
                }
                foundCase.WorkflowState = Constants.WorkflowStates.Created;
                await foundCase.Update(sdkDbContext);
                var planningCase = await dbContext.PlanningCases.SingleAsync(x => x.MicrotingSdkCaseId == model.Id);
                var planningCaseSite = await dbContext.PlanningCaseSites.FirstOrDefaultAsync(x => x.MicrotingSdkCaseId == model.Id && x.PlanningCaseId == planningCase.Id && x.Status == 100);

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
                    await planningCaseSite.Create(dbContext);
                }

                planningCaseSite.MicrotingSdkCaseDoneAt = foundCase.DoneAtUserModifiable;
                planningCaseSite = await SetFieldValue(planningCaseSite, foundCase.Id, language);
                await planningCaseSite.Update(dbContext);

                planningCase.MicrotingSdkCaseDoneAt = foundCase.DoneAtUserModifiable;
                planningCase = await SetFieldValue(planningCase, foundCase.Id, language);
                await planningCase.Update(dbContext);
            }
            else
            {
                return new OperationResult(false, localizationService.GetString("CaseNotFound"));
            }

            return new OperationResult(true, localizationService.GetString("CaseHasBeenUpdated"));
        }
        catch (Exception ex)
        {
            SentrySdk.CaptureException(ex);
            logger.LogError(ex.Message);
            logger.LogTrace(ex.StackTrace);
            return new OperationResult(false, localizationService.GetString("CaseCouldNotBeUpdated") + $" Exception: {ex.Message}");
        }
    }

    private async Task<PlanningCaseSite> SetFieldValue(PlanningCaseSite planningCaseSite, int caseId, Language language)
        {
            var planning = dbContext.Plannings.SingleOrDefault(x => x.Id == planningCaseSite.PlanningId);
            var caseIds = new List<int>
            {
                planningCaseSite.MicrotingSdkCaseId
            };

            var core = await coreHelper.GetCore();
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
            var core = await coreHelper.GetCore();
            var planning = await dbContext.Plannings.SingleOrDefaultAsync(x => x.Id == planningCase.PlanningId).ConfigureAwait(false);
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