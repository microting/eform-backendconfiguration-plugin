using System.Collections.Generic;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.TaskWizard;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.eFormApi.BasePn.Infrastructure.Models.Common;

namespace BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService;

public interface IBackendConfigurationTaskWizardService
{
    Task<OperationDataResult<List<TaskWizardModel>>> Index(TaskWizardRequestModel requestModel);
    Task<OperationDataResult<List<CommonDictionaryModel>>> GetProperties(bool fullNames);
    Task<OperationDataResult<TaskWizardTaskModel>> GetTaskById(int id, bool compliance);
    Task<OperationResult> CreateTask(TaskWizardCreateModel createModel);
    Task<OperationResult> DeactivateList(List<int> ids);
    Task<OperationResult> UpdateTask(TaskWizardCreateModel updateModel);

    /// <summary>
    /// Applies ONLY an eForm change to a whole task/event series: rewrites
    /// <c>AreaRule.EformId</c>/<c>EformName</c> and
    /// <c>Planning.RelatedEFormId</c>/<c>RelatedEFormName</c>, then repairs
    /// every deployed-but-not-completed occurrence through
    /// <c>IEventDeployService.RepairEformForOpenOccurrencesAsync</c>.
    ///
    /// Used by the calendar's scope="this" edit, which otherwise writes only a
    /// per-occurrence <c>CalendarOccurrenceException</c>: the eForm is a
    /// series-level property, so a "this occurrence" edit that changes it must
    /// still apply the change to the series (the frontend confirms this with
    /// the user first). No-op when the series already uses
    /// <paramref name="eformId"/>.
    /// </summary>
    Task<OperationResult> ApplyEformChangeToSeries(int areaRulePlanningId, int eformId);

    Task<OperationResult> DeleteTask(int id);

    /// <summary>
    /// Same DB soft-deletes as <see cref="DeleteTask"/>, but retracts the SDK
    /// cases fire-and-forget so the call returns immediately even when
    /// core.CaseDelete blocks (dev has no eform-core consumer). Used by the
    /// task-list batch delete.
    /// </summary>
    Task<OperationResult> DeleteTaskDeferredRetraction(int id);
}