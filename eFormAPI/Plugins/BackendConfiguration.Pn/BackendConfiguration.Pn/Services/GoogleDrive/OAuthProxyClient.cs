namespace BackendConfiguration.Pn.Services.GoogleDrive;

using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Infrastructure.Models.Settings;
using Microsoft.Extensions.Options;

/// <summary>
/// Default <see cref="IOAuthProxyClient"/> implementation.
///
/// HMAC scheme matches the proxy contract documented in PR-1:
///   canonical string = "{body}\n{date}" — UTF-8 bytes of the raw request
///   body, followed by a literal LF, followed by the RFC 1123 date sent in
///   the <c>Date</c> header. Lowercase hex SHA-256 over those bytes, key =
///   <c>ProxySigningKey</c>, sent in <c>Authorization: HMAC-SHA256 &lt;hex&gt;</c>.
///
/// The proxy validates the signature + a ±2 minute skew window against its
/// clock. If we ever reach for a third proxy endpoint we'll lift the
/// signing helper out of here; for now there is exactly one.
/// </summary>
public class OAuthProxyClient : IOAuthProxyClient
{
    private readonly HttpClient _httpClient;
    private readonly GoogleDriveOptions _options;

    public OAuthProxyClient(HttpClient httpClient, IOptions<GoogleDriveOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<RefreshResult> RefreshAsync(string refreshToken)
    {
        // Build the body as a deterministic byte sequence so the HMAC the
        // proxy recomputes matches ours exactly. Don't use an
        // interpolated string here — JSON encoding rules around the token
        // (which can technically include slashes / unicode) would diverge
        // from the JsonSerializer the receiving side uses.
        var bodyBytes = JsonSerializer.SerializeToUtf8Bytes(
            new RefreshRequestPayload { RefreshToken = refreshToken });

        var dateHeader = DateTime.UtcNow.ToString("R", CultureInfo.InvariantCulture);
        var canonical = new byte[bodyBytes.Length + 1 + Encoding.UTF8.GetByteCount(dateHeader)];
        Buffer.BlockCopy(bodyBytes, 0, canonical, 0, bodyBytes.Length);
        canonical[bodyBytes.Length] = (byte)'\n';
        var dateBytes = Encoding.UTF8.GetBytes(dateHeader);
        Buffer.BlockCopy(dateBytes, 0, canonical, bodyBytes.Length + 1, dateBytes.Length);

        var keyBytes = Encoding.UTF8.GetBytes(_options.ProxySigningKey);
        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(canonical);
        var hex = BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/google-drive/refresh")
        {
            Content = new ByteArrayContent(bodyBytes)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.TryAddWithoutValidation("Date", dateHeader);
        request.Headers.Authorization = new AuthenticationHeaderValue("HMAC-SHA256", hex);

        using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            // The proxy forwards Google's invalid_grant verbatim. We don't
            // try to introspect the body deeply — any 401 means the user
            // must reconnect.
            var errBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (errBody.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase))
            {
                throw new GoogleDriveTokenRevokedException(
                    "Google refresh token rejected by Google (invalid_grant).");
            }

            throw new GoogleDriveTokenRevokedException(
                $"OAuth proxy returned 401 with body: {errBody}");
        }

        response.EnsureSuccessStatusCode();

        var payload = await response.Content
            .ReadFromJsonAsync<RefreshResponsePayload>()
            .ConfigureAwait(false);

        if (payload == null || string.IsNullOrEmpty(payload.AccessToken))
        {
            throw new InvalidOperationException(
                "OAuth proxy returned a successful response with no access token.");
        }

        return new RefreshResult
        {
            AccessToken = payload.AccessToken,
            AccessTokenExpiry = payload.AccessTokenExpiry
        };
    }

    private class RefreshRequestPayload
    {
        [JsonPropertyName("refreshToken")]
        public string RefreshToken { get; set; } = "";
    }

    private class RefreshResponsePayload
    {
        [JsonPropertyName("accessToken")]
        public string AccessToken { get; set; } = "";

        [JsonPropertyName("accessTokenExpiry")]
        public DateTime AccessTokenExpiry { get; set; }
    }
}
