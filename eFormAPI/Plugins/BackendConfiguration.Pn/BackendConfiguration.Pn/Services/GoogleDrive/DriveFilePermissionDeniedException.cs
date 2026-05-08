namespace BackendConfiguration.Pn.Services.GoogleDrive;

using System;

/// <summary>
/// Thrown by <see cref="IGoogleDriveFileService.RefreshFileAsync"/> when
/// Google's <c>files.get</c> returns 403 for a previously-attached
/// <see cref="Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.AreaRulePlanningFile"/>.
///
/// Distinct from <see cref="GoogleDriveTokenRevokedException"/>: the user's
/// OAuth grant is still alive (otherwise <c>GetAccessTokenAsync</c> would have
/// thrown earlier), but the per-file <c>drive.file</c> permission has been
/// revoked at the Drive layer — the picker session that authorized this
/// file ID has been cleaned up, or the file was moved into a folder the
/// token can no longer see. Either way the row is unrecoverable from our
/// side and the processor soft-deletes it, same as the 404 path.
/// </summary>
public class DriveFilePermissionDeniedException : Exception
{
    public string DriveFileId { get; }

    public DriveFilePermissionDeniedException(string driveFileId)
        : base($"Google Drive file '{driveFileId}' returned 403 (permission denied).")
    {
        DriveFileId = driveFileId;
    }

    public DriveFilePermissionDeniedException(string driveFileId, Exception inner)
        : base($"Google Drive file '{driveFileId}' returned 403 (permission denied).", inner)
    {
        DriveFileId = driveFileId;
    }
}
