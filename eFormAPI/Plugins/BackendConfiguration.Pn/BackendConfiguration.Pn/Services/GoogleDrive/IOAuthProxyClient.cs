namespace BackendConfiguration.Pn.Services.GoogleDrive;

using System.Threading.Tasks;

/// <summary>
/// Typed HTTP client wrapping the Microting OAuth proxy
/// (<c>oauth.microting.com</c>). Hides the HMAC + Date signing dance so the
/// auth service can stay focused on the token-management logic.
/// </summary>
public interface IOAuthProxyClient
{
    /// <summary>
    /// POST {proxy}/google-drive/refresh — exchanges the user's refresh
    /// token for a fresh access token via Google's /token endpoint.
    /// Returns the new access token plus its expiry.
    /// </summary>
    /// <exception cref="GoogleDriveTokenRevokedException">
    /// Thrown when Google returns 401/<c>invalid_grant</c> — the refresh
    /// token is no longer usable; the caller must mark the token revoked.
    /// </exception>
    Task<RefreshResult> RefreshAsync(string refreshToken);
}

/// <summary>
/// Response payload from POST /google-drive/refresh. Mirrors the proxy's
/// JSON shape: <c>accessToken</c> + ISO 8601 <c>accessTokenExpiry</c>.
/// </summary>
public class RefreshResult
{
    public string AccessToken { get; set; } = "";
    public System.DateTime AccessTokenExpiry { get; set; }
}
