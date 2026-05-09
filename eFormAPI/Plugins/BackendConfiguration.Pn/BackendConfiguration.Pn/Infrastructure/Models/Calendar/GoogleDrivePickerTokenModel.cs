namespace BackendConfiguration.Pn.Infrastructure.Models.Calendar;

/// <summary>
/// Returned by GET /api/backend-configuration-pn/google-drive/picker-token.
/// The frontend uses these values to instantiate the Google Picker JS SDK
/// client-side. The access token is short-lived (≤1 hour) — the frontend
/// fetches a fresh one each time it opens the Picker.
/// </summary>
public class GoogleDrivePickerTokenModel
{
    /// <summary>
    /// Fresh OAuth access token for the user's Google account. Acquired via
    /// the proxy's /refresh endpoint if the cached one is missing/expired.
    /// </summary>
    public string AccessToken { get; set; } = "";

    /// <summary>
    /// Google Picker API browser key. May be empty — the Picker works with
    /// just OAuth in many configurations. Comes from
    /// <c>GoogleDriveOptions.PickerDeveloperKey</c>.
    /// </summary>
    public string DeveloperKey { get; set; } = "";
}
