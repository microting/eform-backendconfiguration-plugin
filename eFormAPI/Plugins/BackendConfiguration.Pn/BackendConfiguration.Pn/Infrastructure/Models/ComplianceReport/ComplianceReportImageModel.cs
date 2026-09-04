namespace BackendConfiguration.Pn.Infrastructure.Models.ComplianceReport;

/// <summary>
/// A reference to one image answer. References ONLY — this endpoint never
/// downloads or base64-encodes image bytes (#1166 §6).
/// </summary>
public class ComplianceReportImageModel
{
    /// <summary>The SDK <c>FieldValue.Id</c> the image hangs off.</summary>
    public int FieldValueId { get; set; }

    public int UploadedDataId { get; set; }

    /// <summary>
    /// The DISPLAY file name, <c>$"{UploadedData.Id}_700_{Checksum}{Extension}"</c>.
    /// It is DERIVED, never stored: <c>UploadedData.FileName</c> is used only as
    /// an existence check, exactly as
    /// <c>BackendConfigurationReportService.cs:741-744</c> does, and is
    /// <c>null</c> here when that check fails. <c>700</c> is the thumbnail width
    /// baked into the SDK's naming.
    /// </summary>
    public string FileName { get; set; }

    /// <summary>
    /// The 300px-wide derivative of <see cref="FileName"/>,
    /// <c>$"{UploadedData.Id}_300_{Checksum}{Extension}"</c>. Same derivation,
    /// same existence check, same <c>null</c> outcome as <see cref="FileName"/>:
    /// the <c>_300_</c> object is written to S3 by the very same ImageMagick
    /// resize+crop pass that writes the <c>_700_</c> one (see
    /// <c>EventsGrpcService.cs:2861-2862</c> and
    /// <c>BackendConfigurationTaskManagementService.cs:526-527</c>), so whenever
    /// the <c>_700_</c> name is derivable the <c>_300_</c> one is too.
    /// Consumers that only need a thumbnail should prefer this and fall back to
    /// <see cref="FileName"/> when it is <c>null</c>.
    /// </summary>
    public string ThumbnailFileName { get; set; }

    /// <summary>
    /// <c>https://www.google.com/maps/place/{Latitude},{Longitude}</c>, emitted
    /// only when both coordinates are present.
    /// </summary>
    public string GeoLink { get; set; }
}
