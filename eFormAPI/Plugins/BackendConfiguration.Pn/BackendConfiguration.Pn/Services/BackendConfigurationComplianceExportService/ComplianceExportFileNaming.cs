using System;
using System.Globalization;
using System.Text;

namespace BackendConfiguration.Pn.Services.BackendConfigurationComplianceExportService;

/// <summary>
/// Download file names and the RFC 6266 <c>Content-Disposition</c> value (#1169 §4).
///
/// <para>
/// ONE scheme for all three formats —
/// <c>{Oversigt|Detaljer|Rapport}-{property}-{board}-{from}-{to}.{ext}</c> — rather
/// than the prototype's two (a descriptive name for PDF, a
/// <c>compliance-oversigt-{date}</c> one for CSV/Excel). Non-ASCII is PRESERVED,
/// so a property named <c>Miljøtilsyn</c> keeps its <c>ø</c>.
/// </para>
///
/// <para>
/// The platform has no convention to follow here:
/// <c>ReportController.GenerateReportFile</c> sets no <c>Content-Disposition</c> at
/// all, so its file name is whatever the browser derives from the URL. The one
/// correct precedent is <c>CalendarController.DownloadFile</c>
/// (<c>CalendarController.cs:148-153</c>), whose ASCII fallback plus
/// <c>filename*=UTF-8''…</c> pair is what makes a non-ASCII name survive; it is
/// reproduced here with <c>attachment</c> instead of <c>inline</c>.
/// </para>
/// </summary>
public static class ComplianceExportFileNaming
{
    /// <summary>
    /// Sanitises one path segment for use inside a file name: the Windows-reserved
    /// characters <c>\ / : * ? " &lt; &gt; |</c> become <c>-</c>, runs of
    /// whitespace collapse to one space, and trailing dots are trimmed (Windows
    /// silently drops them, which would otherwise change the extension).
    /// Control characters go too — they are legal in a Linux file name and would
    /// end up inside a header value.
    /// </summary>
    public static string SanitiseFileNamePart(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var sb = new StringBuilder(value.Length);
        var lastWasSpace = false;
        foreach (var c in value.Trim())
        {
            if (char.IsControl(c) || char.IsWhiteSpace(c))
            {
                if (!lastWasSpace) sb.Append(' ');
                lastWasSpace = true;
                continue;
            }

            lastWasSpace = false;
            sb.Append(c switch
            {
                '\\' or '/' or ':' or '*' or '?' or '"' or '<' or '>' or '|' => '-',
                _ => c
            });
        }

        return sb.ToString().TrimEnd('.', ' ');
    }

    /// <summary>
    /// <c>{view}-{property}-{board}-{dd.MM.yyyy}-{dd.MM.yyyy}.{extension}</c>.
    /// Empty parts are dropped rather than leaving a double hyphen.
    /// </summary>
    public static string BuildFileName(
        string viewLabel, string propertyLabel, string boardLabel,
        DateTime dateFrom, DateTime dateTo, string extension)
    {
        var parts = new[]
        {
            SanitiseFileNamePart(viewLabel),
            SanitiseFileNamePart(propertyLabel),
            SanitiseFileNamePart(boardLabel),
            dateFrom.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
            dateTo.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)
        };

        var stem = string.Join("-", Array.FindAll(parts, p => !string.IsNullOrEmpty(p)));
        if (string.IsNullOrEmpty(stem)) stem = "compliance";
        return $"{stem}.{extension}";
    }

    /// <summary>
    /// The full header value:
    /// <c>attachment; filename="…"; filename*=UTF-8''…</c>. Built by hand rather
    /// than through <c>ContentDispositionHeaderValue</c>, which is opinionated
    /// about which of the two forms to emit — the same reasoning
    /// <c>CalendarController</c> records.
    /// </summary>
    public static string BuildContentDisposition(string fileName)
    {
        var safe = fileName ?? string.Empty;
        return $"attachment; filename=\"{MakeAsciiFallback(safe)}\"; " +
               $"filename*=UTF-8''{Uri.EscapeDataString(safe)}";
    }

    /// <summary>
    /// ASCII-only fallback for the legacy <c>filename=</c> parameter: any
    /// non-ASCII, quote or control character becomes <c>_</c> so the value is safe
    /// inside the quoted string.
    /// </summary>
    public static string MakeAsciiFallback(string fileName)
    {
        var sb = new StringBuilder((fileName ?? string.Empty).Length);
        foreach (var c in fileName ?? string.Empty)
        {
            sb.Append(c >= 0x20 && c < 0x7F && c != '"' && c != '\\' ? c : '_');
        }

        var result = sb.ToString();
        return string.IsNullOrEmpty(result) ? "compliance" : result;
    }
}
