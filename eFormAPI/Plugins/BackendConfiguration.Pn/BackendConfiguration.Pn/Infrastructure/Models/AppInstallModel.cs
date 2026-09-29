#nullable enable
namespace BackendConfiguration.Pn.Infrastructure.Models;

/// <summary>
/// What the Medarbejdere table knows about one worker's use of one mobile app
/// (Compliance, Ad-hoc, Time, Archive) - #1335.
/// <para>
/// <see cref="HasAccess"/> is the worker's permission to use the app, NOT whether it
/// is installed. <see cref="Version"/> is the last version the app reported; null
/// means nothing has been reported yet, which is not the same as "not installed" -
/// the UI shows that as "not registered", never as "no".
/// </para>
/// </summary>
public class AppInstallModel
{
    public bool HasAccess { get; set; }
    public string? Version { get; set; }
    public string? Model { get; set; }
    public string? Manufacturer { get; set; }
    public string? OsVersion { get; set; }
}
