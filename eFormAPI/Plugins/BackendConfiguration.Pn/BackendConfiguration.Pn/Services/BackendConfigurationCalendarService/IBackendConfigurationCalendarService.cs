using System.Collections.Generic;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using Microsoft.AspNetCore.Http;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;

namespace BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;

public interface IBackendConfigurationCalendarService
{
    Task<OperationDataResult<List<CalendarTaskResponseModel>>> GetTasksForWeek(CalendarTaskRequestModel requestModel);

    /// <summary>
    /// Returns the FULL property-scoped compliance list (no deadline window):
    /// actionable + missed + completed rotations, each annotated with
    /// <see cref="CalendarTaskResponseModel.Completed"/> (Case.Status=100)
    /// and <see cref="CalendarTaskResponseModel.TaskIsExpired"/> (deadline
    /// passed AND case retracted or not yet completed).
    ///
    /// Mirror of <c>BackendConfigurationTaskTrackerHelper.Index</c>
    /// (Infrastructure/Helpers/BackendConfigurationTaskTrackerHelper.cs:46-351).
    /// Sibling to <see cref="GetTasksForWeek"/> — does NOT modify the
    /// calendar-week query path.
    ///
    /// When <paramref name="sdkSiteIdForFilter"/> is non-null, only
    /// compliances whose planning sites include that site are returned —
    /// parity with the angular per-row Worker filter
    /// (BackendConfigurationTaskTrackerHelper.cs:178-192). Pass null to
    /// disable site filtering (admin context).
    /// </summary>
    Task<OperationDataResult<List<CalendarTaskResponseModel>>> GetTaskTrackerList(
        int propertyId, int? sdkSiteIdForFilter);
    Task<OperationDataResult<int>> CreateTask(CalendarTaskCreateRequestModel createModel);
    Task<OperationResult> UpdateTask(CalendarTaskUpdateRequestModel updateModel);
    Task<OperationResult> DeleteTask(CalendarTaskDeleteRequestModel deleteModel);
    Task<OperationResult> MoveTask(CalendarTaskMoveRequestModel moveModel);
    Task<OperationResult> ResizeTask(CalendarTaskResizeRequestModel resizeModel);
    Task<OperationResult> ToggleComplete(int id, bool completed);
    Task<OperationDataResult<List<CalendarBoardModel>>> GetBoards(int propertyId);
    Task<OperationResult> CreateBoard(CalendarBoardCreateModel model);
    Task<OperationResult> UpdateBoard(CalendarBoardUpdateModel model);
    Task<OperationResult> DeleteBoard(int id);
    Task<OperationDataResult<CalendarTaskAttachmentDto>> UploadFile(int taskId, IFormFile file);
    Task<OperationDataResult<List<CalendarTaskAttachmentDto>>> ListFiles(int taskId);
    Task<CalendarFileDownload?> DownloadFile(int taskId, int fileId);
    Task<OperationResult> DeleteFile(int taskId, int fileId);
}
