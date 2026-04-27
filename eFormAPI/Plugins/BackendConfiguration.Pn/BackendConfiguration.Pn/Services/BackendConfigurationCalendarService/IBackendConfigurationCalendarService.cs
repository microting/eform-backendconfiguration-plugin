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
    Task<OperationResult> DeleteTask(CalendarTaskDeleteRequestModel deleteModel);
    Task<OperationResult> MoveTask(CalendarTaskMoveRequestModel moveModel);
    Task<OperationResult> ResizeTask(CalendarTaskResizeRequestModel resizeModel);
    Task<OperationResult> ToggleComplete(int id, bool completed);
    Task<OperationDataResult<List<CalendarBoardModel>>> GetBoards(int propertyId);
    Task<OperationResult> CreateBoard(CalendarBoardCreateModel model);
    Task<OperationResult> UpdateBoard(CalendarBoardUpdateModel model);
    Task<OperationResult> DeleteBoard(int id);
}
