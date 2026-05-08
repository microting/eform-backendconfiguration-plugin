namespace BackendConfiguration.Pn.Services.GoogleDrive;

using System;

/// <summary>
/// Thrown by <see cref="IGoogleDriveFileService.RefreshFileAsync"/> when
/// Google's <c>files.get</c> returns 404 for a previously-attached
/// <see cref="Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.AreaRulePlanningFile"/>.
///
/// The user has deleted (or trashed past the recovery window) the file in
/// their Drive. The change processor catches this and soft-deletes the
/// <c>AreaRulePlanningFile</c> row so the calendar attachment list stops
/// surfacing it. We deliberately do NOT auto-reattach to a different file —
/// the user must re-pick.
///
/// Kept separate from the file service's row-mutation logic so the
/// processor can decide the workflow-state transition; the file service's
/// only job is to map the Drive HTTP status to a typed signal.
/// </summary>
public class DriveFileNotFoundException : Exception
{
    public string DriveFileId { get; }

    public DriveFileNotFoundException(string driveFileId)
        : base($"Google Drive file '{driveFileId}' returned 404.")
    {
        DriveFileId = driveFileId;
    }

    public DriveFileNotFoundException(string driveFileId, Exception inner)
        : base($"Google Drive file '{driveFileId}' returned 404.", inner)
    {
        DriveFileId = driveFileId;
    }
}
