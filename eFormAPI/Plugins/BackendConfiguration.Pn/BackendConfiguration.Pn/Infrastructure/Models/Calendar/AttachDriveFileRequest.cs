namespace BackendConfiguration.Pn.Infrastructure.Models.Calendar;

/// <summary>
/// POST body for /api/backend-configuration-pn/google-drive/attach. The
/// frontend sends the Picker-returned Drive file id and the target
/// AreaRulePlanning so the backend can download the file and create the
/// AreaRulePlanningFile join row.
/// </summary>
public class AttachDriveFileRequest
{
    public int AreaRulePlanningId { get; set; }

    public string DriveFileId { get; set; } = "";
}
