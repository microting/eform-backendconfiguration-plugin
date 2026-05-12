namespace BackendConfiguration.Pn.Infrastructure.Models.Calendar;

using System;

/// <summary>
/// Returned by GET /api/backend-configuration-pn/google-drive/status. Lets
/// the frontend decide whether to launch the OAuth flow before showing the
/// Picker.
/// </summary>
public class GoogleDriveStatusModel
{
    public bool Connected { get; set; }
    public string? Email { get; set; }
    public DateTime? ConnectedAt { get; set; }
}
