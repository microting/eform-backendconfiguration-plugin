namespace BackendConfiguration.Pn.Infrastructure.Models.Calendar;

public class CalendarTaskAttachmentDto
{
    public int Id { get; set; }
    public string OriginalFileName { get; set; } = "";
    public string MimeType { get; set; } = "";
    public long SizeBytes { get; set; }
    public string DownloadUrl { get; set; } = "";
}
