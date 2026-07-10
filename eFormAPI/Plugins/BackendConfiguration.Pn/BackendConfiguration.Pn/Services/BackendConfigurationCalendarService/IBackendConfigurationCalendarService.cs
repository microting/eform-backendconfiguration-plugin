using System.Collections.Generic;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using Microsoft.AspNetCore.Http;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;

namespace BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;

public interface IBackendConfigurationCalendarService
{
    Task<OperationDataResult<List<CalendarTaskResponseModel>>> GetTasksForWeek(CalendarTaskRequestModel requestModel);
    Task<OperationDataResult<List<CalendarTaskResponseModel>>> Index(CalendarTaskIndexRequestModel requestModel);

    /// <summary>
    /// Returns a flat, filterable compliance report row list for the given
    /// date window (deadline-scoped, exception-aware). Status classification:
    /// done = backing SDK case Status == 100; open = row not soft-removed and
    /// not done; soft-removed rows that are not done were user-deleted and
    /// are never shown.
    /// </summary>
    Task<OperationDataResult<List<CalendarComplianceReportRowModel>>> GetComplianceReport(
        CalendarComplianceReportRequestModel requestModel);

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
        int propertyId, int? sdkSiteIdForFilter, int? languageId = null);
    Task<OperationDataResult<int>> CreateTask(CalendarTaskCreateRequestModel createModel);
    Task<OperationResult> UpdateTask(CalendarTaskUpdateRequestModel updateModel);
    Task<OperationResult> DeleteTask(CalendarTaskDeleteRequestModel deleteModel);
    Task<OperationResult> MoveTask(CalendarTaskMoveRequestModel moveModel);
    Task<OperationResult> ResizeTask(CalendarTaskResizeRequestModel resizeModel);
    Task<OperationDataResult<CalendarToggleCompleteResult>> ToggleComplete(
        int id, bool completed, int? complianceId, string? occurrenceDate, int? workerId = null);
    Task<OperationDataResult<List<CalendarBoardModel>>> GetBoards(int propertyId);
    Task<OperationResult> CreateBoard(CalendarBoardCreateModel model);
    Task<OperationResult> UpdateBoard(CalendarBoardUpdateModel model);
    Task<OperationResult> DeleteBoard(int id);
    Task<OperationDataResult<int>> GetBoardEventCount(int id);
    Task<OperationDataResult<CalendarTaskAttachmentDto>> UploadFile(int taskId, IFormFile file);
    Task<OperationDataResult<List<CalendarTaskAttachmentDto>>> ListFiles(int taskId);
    Task<CalendarFileDownload?> DownloadFile(int taskId, int fileId);
    Task<OperationResult> DeleteFile(int taskId, int fileId);
}
