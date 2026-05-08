// Allow the integration-test project to peek at the internal helpers
// (Decrypt, EnvelopePayload). This is intentionally narrow — the auth
// service exposes the production surface as Task<GoogleOAuthToken>; the
// internals are only test-observable so a refactor doesn't bloat the
// public API just to make crypto round-trips assertable.
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("BackendConfiguration.Pn.Integration.Test")]

namespace BackendConfiguration.Pn.Services.GoogleDrive;

using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Infrastructure.Models.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;

/// <summary>
/// Handles the customer-side half of the Google OAuth flow: verifies the
/// envelope JWT minted by the proxy, persists the user's
/// <see cref="GoogleOAuthToken"/> row, and asks the proxy to refresh access
/// tokens on demand.
///
/// Crypto layout:
/// <list type="bullet">
///   <item><description>JWT signature: HS256 with <c>ProxySigningKey</c>.
///     The proxy and the customer share this key — same one used for HMAC
///     on /start and /refresh.</description></item>
///   <item><description>Refresh token at-rest: AES-GCM with
///     <c>RefreshTokenEncryptionKey</c> (base64-encoded 32 bytes). DIFFERENT
///     KEY from the JWT signing key — compromise of one does not cascade
///     into the other.</description></item>
///   <item><description>Encrypted-blob format on
///     <c>EncryptedRefreshToken</c>: <c>base64(nonce(12) || ciphertext || tag(16))</c>
///     — keeps everything in a single VARCHAR(2048) column without
///     introducing a sibling table just for the IV/tag.</description></item>
/// </list>
/// </summary>
public class GoogleDriveAuthService : IGoogleDriveAuthService
{
    private const int AesGcmNonceSize = 12;
    private const int AesGcmTagSize = 16;

    private readonly BackendConfigurationPnDbContext _dbContext;
    private readonly IOAuthProxyClient _oauthProxyClient;
    private readonly IMemoryCache _accessTokenCache;
    private readonly GoogleDriveOptions _options;
    private readonly ILogger<GoogleDriveAuthService> _logger;

    public GoogleDriveAuthService(
        BackendConfigurationPnDbContext dbContext,
        IOAuthProxyClient oauthProxyClient,
        IMemoryCache accessTokenCache,
        IOptions<GoogleDriveOptions> options,
        ILogger<GoogleDriveAuthService> logger)
    {
        _dbContext = dbContext;
        _oauthProxyClient = oauthProxyClient;
        _accessTokenCache = accessTokenCache;
        _options = options.Value;
        _logger = logger;
    }

    private static string AccessTokenCacheKey(int userId) =>
        string.Create(CultureInfo.InvariantCulture, $"gdrive_access:{userId}");

    public async Task<GoogleOAuthToken> StoreEnvelopeAsync(string envelopeJwt, int currentUserId, string expectedNonce)
    {
        var payload = VerifyEnvelopeJwt(envelopeJwt);

        if (!string.Equals(payload.Type, "envelope", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Envelope JWT typ must be 'envelope' but was '{payload.Type}'.");
        }

        if (!string.Equals(payload.Nonce, expectedNonce, StringComparison.Ordinal))
        {
            // CSRF defence — bail without leaking which side was wrong.
            throw new InvalidOperationException("Envelope nonce mismatch.");
        }

        if (payload.User != currentUserId)
        {
            throw new UnauthorizedAccessException(
                "Envelope user claim does not match the authenticated user.");
        }

        var encryptedRefreshToken = Encrypt(payload.RefreshToken);
        var now = DateTime.UtcNow;

        // Application-layer uniqueness — see the spec's "Uniqueness" note.
        // MariaDB does not support filtered uniques over WorkflowState so
        // we look up *any* row for the user, including soft-deleted, and
        // upsert/undelete in place rather than inserting a new one. The
        // db context uses MySqlRetryingExecutionStrategy, which requires
        // user-initiated transactions to run inside CreateExecutionStrategy
        // (otherwise EF refuses with InvalidOperationException).
        var strategy = _dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _dbContext.Database.BeginTransactionAsync().ConfigureAwait(false);

            var existing = await _dbContext.GoogleOAuthTokens
                .Where(x => x.UserId == currentUserId)
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync()
                .ConfigureAwait(false);

            GoogleOAuthToken row;
            if (existing == null)
            {
                row = new GoogleOAuthToken
                {
                    UserId = currentUserId,
                    GoogleAccountEmail = payload.Email ?? "",
                    EncryptedRefreshToken = encryptedRefreshToken,
                    ConnectedAt = now,
                    LastUsedAt = now,
                    RevokedAt = null,
                    CreatedByUserId = currentUserId,
                    UpdatedByUserId = currentUserId
                };
                await row.Create(_dbContext).ConfigureAwait(false);
            }
            else
            {
                row = existing;
                row.GoogleAccountEmail = payload.Email ?? "";
                row.EncryptedRefreshToken = encryptedRefreshToken;
                row.ConnectedAt = now;
                row.LastUsedAt = now;
                row.RevokedAt = null;
                row.UpdatedByUserId = currentUserId;
                // Soft-undelete: the entity's UpdateInternal will stamp
                // WorkflowState=Created when we set it explicitly.
                row.WorkflowState = Constants.WorkflowStates.Created;
                await row.Update(_dbContext).ConfigureAwait(false);
            }

            await tx.CommitAsync().ConfigureAwait(false);
            return row;
        }).ConfigureAwait(false);
    }

    public async Task<string> GetAccessTokenAsync(int userId)
    {
        // Hot path: serve a cached access token if it has > 60s of life
        // left. Saves a /refresh round-trip + database write per call. The
        // 60s cushion guards against the cached token expiring mid-call
        // against the Drive API.
        var cacheKey = AccessTokenCacheKey(userId);
        if (_accessTokenCache.TryGetValue<CachedAccessToken>(cacheKey, out var cached)
            && cached != null
            && cached.Expiry > DateTime.UtcNow.AddSeconds(60))
        {
            return cached.AccessToken;
        }

        var token = await _dbContext.GoogleOAuthTokens
            .Where(x => x.UserId == userId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .Where(x => x.RevokedAt == null)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);

        if (token == null)
        {
            throw new InvalidOperationException(
                $"No active Google OAuth token for user {userId}.");
        }

        var refreshToken = Decrypt(token.EncryptedRefreshToken);

        try
        {
            var refreshed = await _oauthProxyClient.RefreshAsync(refreshToken).ConfigureAwait(false);
            token.LastUsedAt = DateTime.UtcNow;
            await token.Update(_dbContext).ConfigureAwait(false);

            // Cache the access token until just shy of its expiry. The
            // proxy's accessTokenExpiry is the wall-clock instant Google
            // says the token expires; we knock 60s off so the cached value
            // is never within the same window we already filter on the
            // hot path. If the proxy didn't supply an expiry (DateTime
            // default / unset), fall back to a conservative ~50 minutes.
            var expiry = refreshed.AccessTokenExpiry == default
                ? DateTime.UtcNow.AddMinutes(50)
                : refreshed.AccessTokenExpiry;
            var absoluteExpiry = expiry.AddSeconds(-60);
            if (absoluteExpiry > DateTime.UtcNow)
            {
                _accessTokenCache.Set(cacheKey,
                    new CachedAccessToken(refreshed.AccessToken, expiry),
                    new MemoryCacheEntryOptions
                    {
                        AbsoluteExpiration = new DateTimeOffset(absoluteExpiry, TimeSpan.Zero)
                    });
            }

            return refreshed.AccessToken;
        }
        catch (GoogleDriveTokenRevokedException)
        {
            // The grant is gone — drop any cached access token before we
            // surface to the caller, otherwise the next hit on the hot path
            // would happily return a token that Google has already invalidated.
            _accessTokenCache.Remove(cacheKey);
            token.RevokedAt = DateTime.UtcNow;
            await token.Update(_dbContext).ConfigureAwait(false);
            _logger.LogWarning("Google OAuth token revoked for user {UserId} (id={TokenId})",
                userId, token.Id);
            throw;
        }
    }

    public void ClearCachedAccessToken(int userId)
    {
        _accessTokenCache.Remove(AccessTokenCacheKey(userId));
    }

    private sealed record CachedAccessToken(string AccessToken, DateTime Expiry);

    /// <summary>
    /// Verifies the JWT signature against the shared signing key and
    /// deserializes the payload. Returns the payload claims; the caller
    /// applies the additional <c>typ</c>/<c>nonce</c>/<c>user</c> checks.
    /// </summary>
    internal EnvelopePayload VerifyEnvelopeJwt(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length != 3)
        {
            throw new FormatException("Envelope JWT must have three parts.");
        }

        var headerJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[0]));
        var payloadBytes = Base64UrlDecode(parts[1]);
        var signature = Base64UrlDecode(parts[2]);

        var headerDoc = JsonDocument.Parse(headerJson);
        var alg = headerDoc.RootElement.TryGetProperty("alg", out var algEl)
            ? algEl.GetString() : null;
        if (!string.Equals(alg, "HS256", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Envelope JWT alg must be HS256 but was '{alg}'.");
        }

        var signingInput = Encoding.UTF8.GetBytes(parts[0] + "." + parts[1]);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.ProxySigningKey));
        var expected = hmac.ComputeHash(signingInput);

        if (!CryptographicOperations.FixedTimeEquals(signature, expected))
        {
            throw new InvalidOperationException("Envelope JWT signature is invalid.");
        }

        var payload = JsonSerializer.Deserialize<EnvelopePayload>(payloadBytes)
                      ?? throw new InvalidOperationException("Envelope JWT payload could not be parsed.");

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (payload.Expiry <= now)
        {
            throw new InvalidOperationException("Envelope JWT has expired.");
        }

        return payload;
    }

    private string Encrypt(string plaintext)
    {
        // Allocate the sensitive heap buffers once so we can wipe them in a
        // finally regardless of which branch throws. The plaintext bytes
        // and the resolved AES key are both treated as secret material.
        var key = ResolveEncryptionKey();
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        try
        {
            var nonce = RandomNumberGenerator.GetBytes(AesGcmNonceSize);
            var ciphertext = new byte[plaintextBytes.Length];
            var tag = new byte[AesGcmTagSize];

            using var aes = new AesGcm(key, AesGcmTagSize);
            aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

            var combined = new byte[AesGcmNonceSize + ciphertext.Length + AesGcmTagSize];
            Buffer.BlockCopy(nonce, 0, combined, 0, AesGcmNonceSize);
            Buffer.BlockCopy(ciphertext, 0, combined, AesGcmNonceSize, ciphertext.Length);
            Buffer.BlockCopy(tag, 0, combined, AesGcmNonceSize + ciphertext.Length, AesGcmTagSize);
            return Convert.ToBase64String(combined);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintextBytes);
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>
    /// Inverse of <see cref="Encrypt"/>. Made <c>internal</c> so the
    /// integration tests can verify the round-trip without poking the
    /// implementation.
    /// </summary>
    internal string Decrypt(string ciphertextB64)
    {
        var key = ResolveEncryptionKey();
        var plaintext = Array.Empty<byte>();
        try
        {
            var combined = Convert.FromBase64String(ciphertextB64);
            if (combined.Length < AesGcmNonceSize + AesGcmTagSize)
            {
                throw new InvalidOperationException("Encrypted blob is malformed.");
            }

            var ctLen = combined.Length - AesGcmNonceSize - AesGcmTagSize;
            var nonce = new byte[AesGcmNonceSize];
            var ciphertext = new byte[ctLen];
            var tag = new byte[AesGcmTagSize];
            Buffer.BlockCopy(combined, 0, nonce, 0, AesGcmNonceSize);
            Buffer.BlockCopy(combined, AesGcmNonceSize, ciphertext, 0, ctLen);
            Buffer.BlockCopy(combined, AesGcmNonceSize + ctLen, tag, 0, AesGcmTagSize);

            plaintext = new byte[ctLen];
            using var aes = new AesGcm(key, AesGcmTagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private byte[] ResolveEncryptionKey()
    {
        if (string.IsNullOrEmpty(_options.RefreshTokenEncryptionKey))
        {
            throw new InvalidOperationException(
                "GoogleDrive:RefreshTokenEncryptionKey is not configured.");
        }

        var key = Convert.FromBase64String(_options.RefreshTokenEncryptionKey);
        if (key.Length != 32)
        {
            throw new InvalidOperationException(
                $"GoogleDrive:RefreshTokenEncryptionKey must decode to 32 bytes (got {key.Length}).");
        }

        return key;
    }

    private static byte[] Base64UrlDecode(string s)
    {
        // RFC 7515 §2: base64url, no padding. Pad on read.
        var pad = 4 - (s.Length % 4);
        if (pad < 4) s += new string('=', pad);
        return Convert.FromBase64String(s.Replace('-', '+').Replace('_', '/'));
    }

    /// <summary>
    /// Wire shape of the envelope JWT payload — matches the proxy contract
    /// in PR-1.
    /// </summary>
    internal class EnvelopePayload
    {
        [JsonPropertyName("typ")]
        public string Type { get; set; } = "";

        [JsonPropertyName("refreshToken")]
        public string RefreshToken { get; set; } = "";

        [JsonPropertyName("accessToken")]
        public string AccessToken { get; set; } = "";

        [JsonPropertyName("accessTokenExpiry")]
        public DateTime AccessTokenExpiry { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("user")]
        public int User { get; set; }

        [JsonPropertyName("nonce")]
        public string Nonce { get; set; } = "";

        [JsonPropertyName("exp")]
        public long Expiry { get; set; }
    }
}
