using System.IO;

namespace BackendConfiguration.Pn.Infrastructure.Models.Calendar;

/// <summary>
/// Carries the data the controller needs to materialize a FileStreamResult
/// for an attachment download — the binary stream, the original filename
/// (used in Content-Disposition) and the MIME type to announce.
/// </summary>
public class CalendarFileDownload
{
    public Stream Content { get; set; } = null!;
    public string MimeType { get; set; } = "";
    public string FileName { get; set; } = "";
}
