namespace BackendConfiguration.Pn.Services.GoogleDrive;

using System.Threading.Tasks;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;

/// <summary>
/// Owns the Google OAuth token lifecycle: persists the proxy-minted
/// envelope, refreshes access tokens on demand, marks the row revoked when
/// Google says the grant is gone.
/// </summary>
public interface IGoogleDriveAuthService
{
    /// <summary>
    /// Verifies the envelope JWT minted by the OAuth proxy and persists (or
    /// updates) the user's <see cref="GoogleOAuthToken"/> row.
    ///
    /// Verifications performed:
    /// <list type="bullet">
    ///   <item><description>HS256 signature with the shared
    ///     <c>ProxySigningKey</c>.</description></item>
    ///   <item><description><c>typ</c> claim equals <c>"envelope"</c> —
    ///     defends against type-confusion across the state / envelope /
    ///     channel JWTs that share the signing key.</description></item>
    ///   <item><description><c>exp</c> not yet elapsed.</description></item>
    ///   <item><description><c>nonce</c> matches <paramref name="expectedNonce"/>
    ///     (CSRF defence — an attacker who controls a redirect cannot race a
    ///     victim's flow into the attacker's account).</description></item>
    ///   <item><description><c>user</c> claim equals
    ///     <paramref name="currentUserId"/> (an envelope minted for one user
    ///     cannot be replayed under another's session).</description></item>
    /// </list>
    ///
    /// The refresh token is encrypted at-rest with AES-GCM (key from
    /// <c>RefreshTokenEncryptionKey</c>, NOT the proxy signing key) before
    /// being persisted.
    /// </summary>
    Task<GoogleOAuthToken> StoreEnvelopeAsync(string envelopeJwt, int currentUserId, string expectedNonce);

    /// <summary>
    /// Returns a fresh access token for the user. Asks the proxy to refresh
    /// if our cached one is missing/expired. Updates <c>LastUsedAt</c>.
    /// Marks the row revoked + throws
    /// <see cref="GoogleDriveTokenRevokedException"/> if Google says the
    /// grant is gone.
    /// </summary>
    Task<string> GetAccessTokenAsync(int userId);

    /// <summary>
    /// Drops the cached access token for the user, forcing the next call to
    /// <see cref="GetAccessTokenAsync"/> to hit the proxy again. Used by the
    /// settings/revocation flow so a disconnected user doesn't keep handing
    /// out a still-valid access token from cache.
    /// </summary>
    void ClearCachedAccessToken(int userId);
}
