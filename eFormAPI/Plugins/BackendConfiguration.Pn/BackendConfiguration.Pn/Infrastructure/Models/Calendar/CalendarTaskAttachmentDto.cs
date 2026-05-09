namespace BackendConfiguration.Pn.Infrastructure.Models.Calendar;

using System;

public class CalendarTaskAttachmentDto
{
    public int Id { get; set; }
    public string OriginalFileName { get; set; } = "";
    public string MimeType { get; set; } = "";
    public long SizeBytes { get; set; }
    public string DownloadUrl { get; set; } = "";

    /// <summary>
    /// Set when the attachment is a Google Drive-mirrored file. Null for
    /// regular form-data uploads. Lets the frontend render the "Drive" badge
    /// and synthesize the "view source" link as
    /// <c>https://drive.google.com/file/d/{DriveFileId}/view</c>.
    /// </summary>
    public string? DriveFileId { get; set; }

    /// <summary>
    /// Last-seen <c>modifiedTime</c> for the Drive-sourced file. Updated by
    /// the change-processor when the file is re-fetched. Null for non-Drive
    /// attachments.
    /// </summary>
    public DateTime? DriveModifiedTime { get; set; }

    /// <summary>
    /// PR-8: timestamp of the most recent successful refresh of the
    /// Drive-sourced file. We use <see cref="DriveModifiedTime"/> as the
    /// proxy here — the change-processor advances it on every accepted
    /// refetch — so the frontend can render "Last refreshed N ago"
    /// without a separate column. Null for non-Drive attachments.
    /// </summary>
    public DateTime? LastRefreshedAt { get; set; }

    /// <summary>
    /// PR-8: true when the <see cref="GoogleOAuthToken"/> backing this
    /// Drive-sourced attachment has been revoked (the user disconnected
    /// the account, or Google reported <c>invalid_grant</c>). Null/false
    /// for non-Drive attachments. The frontend renders a red
    /// "Drive disconnected — reconnect to resume sync" badge over the
    /// regular Drive badge when this is true.
    /// </summary>
    public bool DriveRevoked { get; set; }
}
