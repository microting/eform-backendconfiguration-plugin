#nullable enable
namespace BackendConfiguration.Pn.Infrastructure.Helpers;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

/// <summary>
/// The latest released version of each Microting mobile app (#1335), baked into the
/// plugin as <c>Resources/AppVersions/app-latest-versions.json</c> until Microting
/// hosts the file centrally. Each value is the <c>VERSION</c> in the app repo's
/// <c>ios/version/version.properties</c> on its default branch.
/// </summary>
public static class AppLatestVersions
{
    public const string Compliance = "compliance";
    public const string Adhoc = "adhoc";
    public const string Time = "time";
    public const string Archive = "archive";
    public const string Chemistry = "chemistry";
    public const string Halebid = "halebid";

    private const string ResourceName = "BackendConfiguration.Pn.Resources.AppVersions.app-latest-versions.json";

    private static readonly Lazy<IReadOnlyDictionary<string, string>> Versions = new(Load);

    /// <summary>The latest version of <paramref name="app"/>, or null when the file has none.</summary>
    public static string? For(string app) => Versions.Value.TryGetValue(app, out var version) ? version : null;

    /// <summary>
    /// True when <paramref name="reported"/> is lower than <paramref name="latest"/>,
    /// compared numerically. Unparseable or missing versions are never flagged.
    /// </summary>
    public static bool IsOutdated(string? reported, string? latest)
        => TryParse(reported, out var r) && TryParse(latest, out var l) && r < l;

    /// <summary>
    /// "v4.1.21", "4.1.21+40121" and "4.1.21" all parse as 4.1.21. Missing parts are
    /// padded with 0 ("4.1" equals "4.1.0"; System.Version would otherwise rank it lower),
    /// and a fourth part is compared too.
    /// </summary>
    private static bool TryParse(string? value, out Version version)
    {
        version = null!;
        if (string.IsNullOrWhiteSpace(value) || !Version.TryParse(value.Trim().TrimStart('v', 'V').Split('+')[0], out var parsed))
        {
            return false;
        }
        version = new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0), Math.Max(parsed.Revision, 0));
        return true;
    }

    /// <summary>A missing or broken file only drops the flag; it must not break the Medarbejdere page.</summary>
    private static IReadOnlyDictionary<string, string> Load()
    {
        try
        {
            using var stream = typeof(AppLatestVersions).Assembly.GetManifestResourceStream(ResourceName);
            if (stream == null)
            {
                Console.WriteLine($"[ERROR] AppLatestVersions: embedded resource {ResourceName} is missing");
                return new Dictionary<string, string>();
            }
            using var reader = new StreamReader(stream);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(reader.ReadToEnd())
                   ?? new Dictionary<string, string>();
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"[ERROR] AppLatestVersions: {ResourceName} is not valid JSON: {ex.Message}");
            return new Dictionary<string, string>();
        }
    }
}
