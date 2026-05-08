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

    /// <summary>
    /// Ensures a Drive <c>changes.watch</c> subscription exists for the
    /// user's <see cref="GoogleOAuthToken"/>:
    /// <list type="bullet">
    ///   <item><description>If a <c>WorkflowState=Created</c>
    ///     <see cref="DriveWatchChannel"/> exists with
    ///     <c>ExpiresAt &gt; now + 1 day</c>, returns it unchanged
    ///     (no Drive call).</description></item>
    ///   <item><description>Otherwise soft-deletes any existing channel for
    ///     this token, mints a fresh <c>{typ:"channel", customerInstanceUrl,
    ///     channelId, exp}</c> JWT, calls <c>POST drive/v3/changes/watch</c>,
    ///     and persists the response into a new
    ///     <see cref="DriveWatchChannel"/>.</description></item>
    /// </list>
    /// May throw on Drive 4xx/5xx — callers (e.g. the file-attach path)
    /// MUST decide whether the failure is fatal or whether the daily
    /// reconcile cron in PR-6 should pick it up later.
    /// </summary>
    Task<DriveWatchChannel> EnsureWatchChannelAsync(int userId);
}
