// Allow the integration-test project to peek at the internal helpers
// (Decrypt, EnvelopePayload). This is intentionally narrow — the auth
// service exposes the production surface as Task<GoogleOAuthToken>; the
// internals are only test-observable so a refactor doesn't bloat the
// public API just to make crypto round-trips assertable.
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("BackendConfiguration.Pn.Integration.Test")]

namespace BackendConfiguration.Pn.Services.GoogleDrive;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Infrastructure.Models.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
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
    private readonly IConfiguration? _configuration;
    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly ILogger<GoogleDriveAuthService> _logger;

    public GoogleDriveAuthService(
        BackendConfigurationPnDbContext dbContext,
        IOAuthProxyClient oauthProxyClient,
        IMemoryCache accessTokenCache,
        IOptions<GoogleDriveOptions> options,
        ILogger<GoogleDriveAuthService> logger,
        IConfiguration? configuration = null,
        IHttpClientFactory? httpClientFactory = null)
    {
        _dbContext = dbContext;
        _oauthProxyClient = oauthProxyClient;
        _accessTokenCache = accessTokenCache;
        _options = options.Value;
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
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

    /// <summary>
    /// PR-5 watch-channel registration. See
    /// <see cref="IGoogleDriveAuthService.EnsureWatchChannelAsync"/> for the
    /// behavioural contract. Drive's <c>changes.watch</c> endpoint returns
    /// <c>{ id, resourceId, expiration (unix ms as string) }</c>; we
    /// persist all three plus the JWT we minted (so PR-6's renewal cron
    /// can stop the channel by id and PR-5's webhook can verify the
    /// X-Goog-Channel-Token against the stored signed token).
    /// </summary>
    public async Task<DriveWatchChannel> EnsureWatchChannelAsync(int userId)
    {
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

        // Reuse a still-fresh channel: > 1 day of life left. Anything
        // tighter and we risk the renewal happening in the same window
        // PR-6's daily cron is targeting, so the cron and the watch-on-
        // attach race against each other.
        var existing = await _dbContext.DriveWatchChannels
            .Where(x => x.GoogleOAuthTokenId == token.Id)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);
        if (existing != null && existing.ExpiresAt > DateTime.UtcNow.AddDays(1))
        {
            return existing;
        }

        // Soft-delete the old row before inserting the new one — keeps the
        // application-layer uniqueness invariant the spec calls out (one
        // active channel per token).
        if (existing != null)
        {
            await existing.Delete(_dbContext).ConfigureAwait(false);
        }

        var customerInstanceUrl = ResolveCustomerInstanceUrl();
        if (string.IsNullOrEmpty(customerInstanceUrl))
        {
            throw new InvalidOperationException(
                "CustomerInstanceUrl is not configured (BackendConfigurationSettings:CustomerInstanceUrl or GoogleDrive:CustomerInstanceUrl).");
        }

        var channelId = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        var exp = now.AddDays(7);
        var expUnixMillis = ((DateTimeOffset)exp).ToUnixTimeMilliseconds();
        var channelJwt = MintChannelJwt(customerInstanceUrl, channelId, exp);

        var accessToken = await GetAccessTokenAsync(userId).ConfigureAwait(false);

        var proxyUrl = TrimTrailingSlash(_options.MicrotingOAuthProxyUrl);
        var notifyAddress = $"{proxyUrl}/google-drive/notify";

        var watchClient = (_httpClientFactory ?? throw new InvalidOperationException(
            "IHttpClientFactory was not supplied to GoogleDriveAuthService."))
            .CreateClient(nameof(GoogleDriveFileService));
        // Drive accepts "expiration" as a string of unix-millis. We send
        // it as a string explicitly to side-step the JSON serializer's
        // tendency to write longs as numbers (Drive accepts both, but the
        // public API doc shows the string form so we mirror it).
        var requestBody = new DriveWatchRequest
        {
            Id = channelId,
            Type = "web_hook",
            Address = notifyAddress,
            Token = channelJwt,
            Expiration = expUnixMillis.ToString(CultureInfo.InvariantCulture)
        };

        using var request = new HttpRequestMessage(HttpMethod.Post,
            "https://www.googleapis.com/drive/v3/changes/watch?supportsAllDrives=false");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = JsonContent.Create(requestBody, options: new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        using var response = await watchClient.SendAsync(request).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            throw new InvalidOperationException(
                $"Drive changes.watch failed ({(int)response.StatusCode}): {body}");
        }

        var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var parsed = JsonSerializer.Deserialize<DriveWatchResponse>(responseBody)
                     ?? throw new InvalidOperationException(
                         "Drive changes.watch returned an unparseable body.");

        // Drive's "expiration" is unix-millis as a string. Fall back to
        // our own +7d if Drive omits it (defensive — the public API always
        // sends it but a stub might not).
        DateTime expiresAt;
        if (!string.IsNullOrEmpty(parsed.Expiration)
            && long.TryParse(parsed.Expiration, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var expirationUnixMs))
        {
            expiresAt = DateTimeOffset.FromUnixTimeMilliseconds(expirationUnixMs).UtcDateTime;
        }
        else
        {
            expiresAt = exp;
        }

        var watchChannel = new DriveWatchChannel
        {
            GoogleOAuthTokenId = token.Id,
            ChannelId = parsed.Id ?? channelId,
            ResourceId = parsed.ResourceId ?? string.Empty,
            SignedToken = channelJwt,
            ExpiresAt = expiresAt,
            CreatedByUserId = userId,
            UpdatedByUserId = userId
        };
        await watchChannel.Create(_dbContext).ConfigureAwait(false);

        return watchChannel;
    }

    private string ResolveCustomerInstanceUrl()
    {
        // Spec PR-5: "BackendConfigurationSettings:CustomerInstanceUrl
        // first, then a new GoogleDriveOptions.CustomerInstanceUrl". We
        // honour both so the platform-wide value can live alongside the
        // rest of the BackendConfigurationSettings bundle while a
        // GoogleDrive-scoped override is still possible.
        var fromConfig = _configuration?["BackendConfigurationSettings:CustomerInstanceUrl"];
        if (!string.IsNullOrEmpty(fromConfig))
        {
            return TrimTrailingSlash(fromConfig);
        }
        if (!string.IsNullOrEmpty(_options.CustomerInstanceUrl))
        {
            return TrimTrailingSlash(_options.CustomerInstanceUrl);
        }
        return string.Empty;
    }

    private static string TrimTrailingSlash(string url)
        => url.EndsWith('/') ? url[..^1] : url;

    /// <summary>
    /// Mints a <c>{typ:"channel", customerInstanceUrl, channelId, exp}</c>
    /// JWT signed with the same shared <c>ProxySigningKey</c> the proxy
    /// uses for envelope/state verification. The proxy validates this
    /// JWT (signature + <c>typ == "channel"</c> + <c>exp</c>) before
    /// fanning a Drive notification back to <c>customerInstanceUrl</c>.
    /// </summary>
    internal string MintChannelJwt(string customerInstanceUrl, string channelId, DateTime exp)
    {
        var header = new Dictionary<string, object>
        {
            ["alg"] = "HS256",
            ["typ"] = "JWT"
        };
        var payload = new Dictionary<string, object>
        {
            ["typ"] = "channel",
            ["customerInstanceUrl"] = customerInstanceUrl,
            ["channelId"] = channelId,
            ["exp"] = ((DateTimeOffset)exp).ToUnixTimeSeconds()
        };

        var headerB64 = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(header));
        var payloadB64 = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload));
        var signingInput = Encoding.UTF8.GetBytes($"{headerB64}.{payloadB64}");
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.ProxySigningKey));
        var signature = Base64UrlEncode(hmac.ComputeHash(signingInput));
        return $"{headerB64}.{payloadB64}.{signature}";
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class DriveWatchRequest
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("type")]
        public string Type { get; set; } = "";

        [JsonPropertyName("address")]
        public string Address { get; set; } = "";

        [JsonPropertyName("token")]
        public string? Token { get; set; }

        [JsonPropertyName("expiration")]
        public string? Expiration { get; set; }
    }

    private sealed class DriveWatchResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("resourceId")]
        public string? ResourceId { get; set; }

        [JsonPropertyName("expiration")]
        public string? Expiration { get; set; }
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
