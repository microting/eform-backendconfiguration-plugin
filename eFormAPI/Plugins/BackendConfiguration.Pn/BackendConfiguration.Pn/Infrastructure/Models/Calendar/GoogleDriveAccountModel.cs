namespace BackendConfiguration.Pn.Infrastructure.Models.Calendar;

using System;

/// <summary>
/// Returned by <c>GET /api/backend-configuration-pn/google-drive/accounts</c>.
/// Powers the connected-accounts settings panel introduced in PR-8 of the
/// Google Drive integration design. One row per <c>GoogleOAuthToken</c> the
/// authenticated user has ever owned, including soft-deleted/revoked rows so
/// the UI can show the disconnect history (a revoked account renders with a
/// disabled state + red "Revoked" badge).
/// </summary>
public class GoogleDriveAccountModel
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public DateTime ConnectedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    /// <summary>
    /// True when the row is in <c>WorkflowState = Created</c> AND has not
    /// been revoked at Google. Lets the frontend gate the "Disconnect"
    /// button without re-deriving the rules client-side.
    /// </summary>
    public bool IsActive { get; set; }
}
