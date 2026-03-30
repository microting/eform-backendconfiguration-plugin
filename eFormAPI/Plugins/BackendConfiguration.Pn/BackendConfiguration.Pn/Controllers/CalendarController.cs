using System.Collections.Generic;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;

namespace BackendConfiguration.Pn.Controllers;

[Authorize]
[Route("api/backend-configuration-pn/calendar")]
public class CalendarController : Controller
{
    private readonly IBackendConfigurationCalendarService _backendConfigurationCalendarService;

    public CalendarController(IBackendConfigurationCalendarService backendConfigurationCalendarService)
    {
        _backendConfigurationCalendarService = backendConfigurationCalendarService;
    }

    [HttpPost("tasks/week")]
    public async Task<OperationDataResult<List<CalendarTaskResponseModel>>> GetTasksForWeek(
        [FromBody] CalendarTaskRequestModel requestModel)
    {
        return await _backendConfigurationCalendarService.GetTasksForWeek(requestModel);
    }

    [HttpPost("tasks")]
    public async Task<OperationResult> CreateTask([FromBody] CalendarTaskCreateRequestModel createModel)
    {
        return await _backendConfigurationCalendarService.CreateTask(createModel);
    }

    [HttpPut("tasks")]
    public async Task<OperationResult> UpdateTask([FromBody] CalendarTaskUpdateRequestModel updateModel)
    {
        return await _backendConfigurationCalendarService.UpdateTask(updateModel);
    }

    [HttpPut("tasks/delete")]
    public async Task<OperationResult> DeleteTask([FromBody] CalendarTaskDeleteRequestModel deleteModel)
    {
        return await _backendConfigurationCalendarService.DeleteTask(deleteModel);
    }

    [HttpPut("tasks/move")]
    public async Task<OperationResult> MoveTask([FromBody] CalendarTaskMoveRequestModel moveModel)
    {
        return await _backendConfigurationCalendarService.MoveTask(moveModel);
    }

    [HttpGet("boards/{propertyId:int}")]
    public async Task<OperationDataResult<List<CalendarBoardModel>>> GetBoards(int propertyId)
    {
        return await _backendConfigurationCalendarService.GetBoards(propertyId);
    }

    [HttpPost("boards")]
    public async Task<OperationResult> CreateBoard([FromBody] CalendarBoardCreateModel model)
    {
        return await _backendConfigurationCalendarService.CreateBoard(model);
    }

    [HttpPut("boards")]
    public async Task<OperationResult> UpdateBoard([FromBody] CalendarBoardUpdateModel model)
    {
        return await _backendConfigurationCalendarService.UpdateBoard(model);
    }

    [HttpDelete("boards/{id:int}")]
    public async Task<OperationResult> DeleteBoard(int id)
    {
        return await _backendConfigurationCalendarService.DeleteBoard(id);
    }

    [HttpPut("tasks/{id:int}/complete")]
    public async Task<OperationResult> ToggleComplete(int id, [FromBody] CalendarToggleCompleteModel model)
    {
        return await _backendConfigurationCalendarService.ToggleComplete(id, model.Completed);
    }
}
