namespace BackendConfiguration.Pn.Services.GoogleDrive;

using System.Threading.Tasks;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;

/// <summary>
/// Drive-side mirror of the form-data upload pipeline. Downloads a file
/// from the user's Drive (via the access token from the auth service),
/// shoves it through the same MD5 + UploadedData + S3 pipeline used by
/// <see cref="BackendConfigurationCalendarService.BackendConfigurationCalendarService.UploadFile"/>,
/// then creates an <see cref="AreaRulePlanningFile"/> with
/// <c>DriveFileId</c> populated.
/// </summary>
public interface IGoogleDriveFileService
{
    /// <summary>
    /// Downloads <paramref name="driveFileId"/> from the user's Drive and
    /// caches it as a new <see cref="AreaRulePlanningFile"/> on the given
    /// <paramref name="areaRulePlanningId"/>. Wraps validation (mime allow
    /// list + 25 MB size cap) and emits a typed
    /// <see cref="OperationDataResult{T}"/> instead of throwing on
    /// user-input errors so the controller can surface localized messages.
    /// </summary>
    Task<OperationDataResult<AreaRulePlanningFile>> DownloadAndCacheFileAsync(int userId, string driveFileId, int areaRulePlanningId);

    /// <summary>
    /// Re-fetches a previously-attached Drive file. If Drive's
    /// <c>modifiedTime</c> is newer than the cached
    /// <c>DriveModifiedTime</c>, replaces the <see cref="UploadedData"/>
    /// blob and updates name/mime/size/modifiedTime. Returns true when
    /// something changed. Used by PR-7's change processor.
    /// </summary>
    Task<bool> RefreshFileAsync(AreaRulePlanningFile file);
}
