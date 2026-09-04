using System.IO;

namespace BackendConfiguration.Pn.Infrastructure.Models.ComplianceReport;

/// <summary>
/// A rendered compliance export: the bytes, the download name and the MIME type.
///
/// <para>
/// The name travels WITH the file rather than being left for the browser to derive
/// from the URL, which is what <c>ReportController.GenerateReportFile</c> does (it
/// sets no <c>Content-Disposition</c> at all). See
/// <c>ComplianceExportFileNaming</c>.
/// </para>
/// </summary>
public class ComplianceExportFileModel
{
    /// <summary>Rewound and ready to copy to the response body.</summary>
    public Stream Content { get; set; }

    /// <summary>
    /// Including the extension, non-ASCII preserved. The controller emits it as
    /// both an ASCII fallback and an RFC 5987 <c>filename*</c>.
    /// </summary>
    public string FileName { get; set; }

    public string MimeType { get; set; }
}
