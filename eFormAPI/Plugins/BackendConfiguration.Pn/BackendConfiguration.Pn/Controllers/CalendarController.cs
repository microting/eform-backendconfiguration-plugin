using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
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

    [HttpPut("tasks/resize")]
    public async Task<OperationResult> ResizeTask([FromBody] CalendarTaskResizeRequestModel resizeModel)
    {
        return await _backendConfigurationCalendarService.ResizeTask(resizeModel);
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

    [HttpPost("tasks/{id:int}/files")]
    [RequestSizeLimit(26_214_400)]
    public async Task<OperationDataResult<CalendarTaskAttachmentDto>> UploadFile(int id, IFormFile file)
    {
        return await _backendConfigurationCalendarService.UploadFile(id, file);
    }

    [HttpGet("tasks/{id:int}/files")]
    public async Task<OperationDataResult<List<CalendarTaskAttachmentDto>>> ListFiles(int id)
    {
        return await _backendConfigurationCalendarService.ListFiles(id);
    }

    [HttpGet("tasks/{id:int}/files/{fileId:int}")]
    public async Task<IActionResult> DownloadFile(int id, int fileId)
    {
        var result = await _backendConfigurationCalendarService.DownloadFile(id, fileId);
        if (result == null) return NotFound();

        // RFC 5987 / RFC 6266 compliant Content-Disposition: an ASCII fallback
        // for ancient clients (filename=) plus the UTF-8 percent-encoded
        // filename* parameter for everyone else. Uri.EscapeDataString is the
        // appropriate percent-encoder for filename*; building both parts by
        // hand keeps us off the unstable surface of
        // ContentDispositionHeaderValue.ToString() which is opinionated about
        // when to emit which form.
        var fileName = result.FileName ?? string.Empty;
        var asciiFallback = MakeAsciiFallback(fileName);
        var contentDisposition =
            $"inline; filename=\"{asciiFallback}\"; filename*=UTF-8''{Uri.EscapeDataString(fileName)}";
        Response.Headers.Append("Content-Disposition", contentDisposition);
        return File(result.Content, result.MimeType);
    }

    private static string MakeAsciiFallback(string fileName)
    {
        // ASCII-only fallback for the legacy filename= parameter. Replace any
        // non-ASCII or quote/control character with '_' so the value is safe
        // inside the quoted-string wrapper.
        var sb = new StringBuilder(fileName.Length);
        foreach (var c in fileName)
        {
            if (c >= 0x20 && c < 0x7F && c != '"' && c != '\\')
            {
                sb.Append(c);
            }
            else
            {
                sb.Append('_');
            }
        }
        return sb.Length == 0 ? "download" : sb.ToString();
    }

    [HttpDelete("tasks/{id:int}/files/{fileId:int}")]
    public async Task<OperationResult> DeleteFile(int id, int fileId)
    {
        return await _backendConfigurationCalendarService.DeleteFile(id, fileId);
    }
}
