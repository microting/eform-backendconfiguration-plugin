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
}
