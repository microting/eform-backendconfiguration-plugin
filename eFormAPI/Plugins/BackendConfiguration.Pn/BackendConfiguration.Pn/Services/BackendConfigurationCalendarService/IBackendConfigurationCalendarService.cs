using System.Collections.Generic;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;

namespace BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;

public interface IBackendConfigurationCalendarService
{
    Task<OperationDataResult<List<CalendarTaskResponseModel>>> GetTasksForWeek(CalendarTaskRequestModel requestModel);
    Task<OperationResult> CreateTask(CalendarTaskCreateRequestModel createModel);
    Task<OperationResult> UpdateTask(CalendarTaskUpdateRequestModel updateModel);
    Task<OperationResult> DeleteTask(int id);
    Task<OperationResult> MoveTask(CalendarTaskMoveRequestModel moveModel);
    Task<OperationResult> ToggleComplete(int id, bool completed);
}
