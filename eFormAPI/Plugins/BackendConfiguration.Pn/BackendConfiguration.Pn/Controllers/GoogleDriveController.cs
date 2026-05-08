namespace BackendConfiguration.Pn.Controllers;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Infrastructure.Models.Calendar;
using Infrastructure.Models.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Services.BackendConfigurationLocalizationService;
using Services.GoogleDrive;

/// <summary>
/// Customer-side endpoints for the Google Drive integration. Three flows:
///
/// <list type="number">
///   <item><description><c>POST /start</c> — frontend asks for the proxy
///     URL to open in a popup. Server generates a CSRF nonce, stashes it in
///     a short-lived signed cookie (<see cref="IDataProtector"/>), and
///     builds the proxy <c>/google-drive/start</c> URL with the four
///     required query params + HMAC.</description></item>
///   <item><description><c>GET /oauth-finish</c> — the proxy 302s the
///     browser back here with <c>?envelope=&lt;jwt&gt;</c>. We pull the
///     nonce out of the cookie, hand both to the auth service, and 302 to
///     the calendar page with a success/err query param.</description></item>
///   <item><description><c>GET /status</c> + <c>POST /attach</c> — used by
///     the modal: status to decide whether to show the connect banner,
///     attach to download a Picker-returned file into the calendar
///     attachment list.</description></item>
/// </list>
/// </summary>
[Authorize]
[Route("api/backend-configuration-pn/google-drive")]
public class GoogleDriveController : Controller
{
    private const string NonceCookieName = "gd_nonce";
    private const string DataProtectionPurpose = "BackendConfiguration.GoogleDrive.NonceCookie.v1";

    private readonly IGoogleDriveAuthService _authService;
    private readonly IGoogleDriveFileService _fileService;
    private readonly IUserService _userService;
    private readonly IBackendConfigurationLocalizationService _localizationService;
    private readonly BackendConfigurationPnDbContext _dbContext;
    private readonly IDataProtector _dataProtector;
    private readonly GoogleDriveOptions _options;
    private readonly ILogger<GoogleDriveController> _logger;

    public GoogleDriveController(
        IGoogleDriveAuthService authService,
        IGoogleDriveFileService fileService,
        IUserService userService,
        IBackendConfigurationLocalizationService localizationService,
        BackendConfigurationPnDbContext dbContext,
        IDataProtectionProvider dataProtectionProvider,
        IOptions<GoogleDriveOptions> options,
        ILogger<GoogleDriveController> logger)
    {
        _authService = authService;
        _fileService = fileService;
        _userService = userService;
        _localizationService = localizationService;
        _dbContext = dbContext;
        _dataProtector = dataProtectionProvider.CreateProtector(DataProtectionPurpose);
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Returns the proxy /start URL the frontend should open in a popup.
    /// Generates a fresh nonce and stashes it (data-protector-signed) in a
    /// 5-minute HttpOnly cookie. The /oauth-finish handler reads the same
    /// cookie back to verify the envelope's nonce claim — the CSRF defence
    /// described in the spec's "Security" section.
    /// </summary>
    [HttpPost("start")]
    public async Task<OperationDataResult<string>> Start()
    {
        try
        {
            if (string.IsNullOrEmpty(_options.MicrotingOAuthProxyUrl))
            {
                return new OperationDataResult<string>(false,
                    "GoogleDrive:MicrotingOAuthProxyUrl is not configured.");
            }

            var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

            // Derive the customer base URL from the inbound request — the
            // proxy needs it to know where to 302 back. Honor the standard
            // forwarded headers so this works behind the platform's
            // reverse proxy.
            var customerUrl = ResolveCustomerBaseUrl();
            var userId = _userService.UserId;
            var date = DateTime.UtcNow.ToString("R", CultureInfo.InvariantCulture);
            var canonical = $"{customerUrl}|{userId}|{nonce}|{date}";
            var hex = HmacHex(canonical);

            var proxyUrl = TrimTrailingSlash(_options.MicrotingOAuthProxyUrl);
            var startUrl =
                $"{proxyUrl}/google-drive/start"
                + $"?customer={Uri.EscapeDataString(customerUrl)}"
                + $"&user={userId}"
                + $"&nonce={nonce}"
                + $"&date={Uri.EscapeDataString(date)}"
                + $"&hmac={hex}";

            // Sign the nonce against an inner payload that also binds the
            // user — prevents a "swap nonce cookie between users" trick.
            var protectedPayload = _dataProtector.Protect(
                Encoding.UTF8.GetBytes($"{userId}|{nonce}"));
            var cookieValue = Convert.ToBase64String(protectedPayload);
            // Honor X-Forwarded-Proto so the Secure flag is set when the
            // public scheme is HTTPS even though the inbound hop from the
            // reverse proxy is plain HTTP. Mirrors the scheme detection in
            // ResolveCustomerBaseUrl below.
            var isHttps = string.Equals(Request.Scheme, "https", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(Request.Headers["X-Forwarded-Proto"].ToString(), "https", StringComparison.OrdinalIgnoreCase);
            Response.Cookies.Append(NonceCookieName, cookieValue, new CookieOptions
            {
                HttpOnly = true,
                Secure = isHttps,
                SameSite = SameSiteMode.Lax,
                MaxAge = TimeSpan.FromMinutes(5),
                IsEssential = true,
            });

            await Task.CompletedTask;
            return new OperationDataResult<string>(true, startUrl);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GoogleDriveController.Start failed: {Message}", e.Message);
            return new OperationDataResult<string>(false,
                _localizationService.GetString("GenericError"));
        }
    }

    /// <summary>
    /// Proxy redirects the browser here after the Google OAuth dance. Verify
    /// envelope + nonce-cookie, persist the token, then 302 to the calendar
    /// page with success=true (or err=...). Always 302 — never JSON — so
    /// the browser lands on a real page even if the popup closes itself.
    /// </summary>
    [HttpGet("oauth-finish")]
    public async Task<IActionResult> OAuthFinish([FromQuery] string envelope)
    {
        // Dedicated single-purpose page: in `ngOnInit` it does
        // `window.opener.postMessage({type: 'gd_oauth_done', ...}, origin)`
        // and `window.close()`. Burying that logic in the calendar route
        // would fire the postMessage on every navigation; a dedicated route
        // keeps the contract narrow.
        var finishPage = "/plugins/backend-configuration-pn/google-drive-oauth-finish";

        if (string.IsNullOrWhiteSpace(envelope))
        {
            return Redirect($"{finishPage}?gdrive_err=missing_envelope");
        }

        // Pull the nonce from the cookie and clear it — single-use.
        if (!Request.Cookies.TryGetValue(NonceCookieName, out var cookieValue)
            || string.IsNullOrEmpty(cookieValue))
        {
            return Redirect($"{finishPage}?gdrive_err=nonce_missing");
        }
        Response.Cookies.Delete(NonceCookieName);

        string expectedNonce;
        int cookieUserId;
        try
        {
            var bytes = _dataProtector.Unprotect(Convert.FromBase64String(cookieValue));
            var raw = Encoding.UTF8.GetString(bytes);
            var pipe = raw.IndexOf('|');
            if (pipe <= 0)
            {
                return Redirect($"{finishPage}?gdrive_err=nonce_corrupt");
            }
            cookieUserId = int.Parse(raw[..pipe], CultureInfo.InvariantCulture);
            expectedNonce = raw[(pipe + 1)..];
        }
        catch (Exception)
        {
            return Redirect($"{finishPage}?gdrive_err=nonce_corrupt");
        }

        var currentUserId = _userService.UserId;
        if (cookieUserId != currentUserId)
        {
            return Redirect($"{finishPage}?gdrive_err=user_mismatch");
        }

        try
        {
            await _authService.StoreEnvelopeAsync(envelope, currentUserId, expectedNonce)
                .ConfigureAwait(false);
            return Redirect($"{finishPage}?gdrive_success=true");
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "OAuthFinish rejected envelope: {Message}", e.Message);
            return Redirect($"{finishPage}?gdrive_err={Uri.EscapeDataString(e.GetType().Name)}");
        }
    }

    /// <summary>
    /// Returns whether the current user has an active Google connection,
    /// so the modal can decide whether to launch the OAuth flow before
    /// showing the Picker.
    /// </summary>
    [HttpGet("status")]
    public async Task<OperationDataResult<GoogleDriveStatusModel>> GetStatus()
    {
        try
        {
            var userId = _userService.UserId;
            var token = await _dbContext.GoogleOAuthTokens
                .Where(x => x.UserId == userId)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.RevokedAt == null)
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync()
                .ConfigureAwait(false);

            if (token == null)
            {
                return new OperationDataResult<GoogleDriveStatusModel>(true,
                    new GoogleDriveStatusModel { Connected = false });
            }

            return new OperationDataResult<GoogleDriveStatusModel>(true, new GoogleDriveStatusModel
            {
                Connected = true,
                Email = token.GoogleAccountEmail,
                ConnectedAt = token.ConnectedAt
            });
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GoogleDriveController.GetStatus failed: {Message}", e.Message);
            return new OperationDataResult<GoogleDriveStatusModel>(false,
                _localizationService.GetString("GenericError"));
        }
    }

    /// <summary>
    /// Returns a fresh OAuth access token + the (optional) developer key so
    /// the frontend can instantiate the Google Picker JS SDK. The Picker is
    /// rendered fully client-side; the backend can't render it for the user,
    /// but the access token only lives server-side, so we expose it through
    /// this read-only endpoint.
    ///
    /// The access token is short-lived (≤1 hour) and limited to <c>drive.file</c>
    /// scope — a leaked token grants per-file Picker-granted access only,
    /// not whole-Drive enumeration. Frontend MUST fetch a fresh one each
    /// time it opens the Picker rather than caching.
    /// </summary>
    [HttpGet("picker-token")]
    public async Task<OperationDataResult<GoogleDrivePickerTokenModel>> GetPickerToken()
    {
        try
        {
            var userId = _userService.UserId;
            var accessToken = await _authService.GetAccessTokenAsync(userId).ConfigureAwait(false);

            return new OperationDataResult<GoogleDrivePickerTokenModel>(true, new GoogleDrivePickerTokenModel
            {
                AccessToken = accessToken,
                DeveloperKey = _options.PickerDeveloperKey ?? string.Empty,
            });
        }
        catch (GoogleDriveTokenRevokedException)
        {
            return new OperationDataResult<GoogleDrivePickerTokenModel>(false,
                _localizationService.GetString("GoogleDriveTokenRevoked"));
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GoogleDriveController.GetPickerToken failed: {Message}", e.Message);
            return new OperationDataResult<GoogleDrivePickerTokenModel>(false,
                _localizationService.GetString("GenericError"));
        }
    }

    /// <summary>
    /// Downloads a Picker-returned Drive file into the calendar's
    /// attachment list. Returns the same DTO shape used by the form-data
    /// upload path (with the Drive-specific fields populated).
    /// </summary>
    [HttpPost("attach")]
    public async Task<OperationDataResult<CalendarTaskAttachmentDto>> Attach([FromBody] AttachDriveFileRequest request)
    {
        if (request == null || string.IsNullOrEmpty(request.DriveFileId) || request.AreaRulePlanningId <= 0)
        {
            return new OperationDataResult<CalendarTaskAttachmentDto>(false,
                _localizationService.GetString("FieldRequired"));
        }

        try
        {
            var userId = _userService.UserId;
            var result = await _fileService
                .DownloadAndCacheFileAsync(userId, request.DriveFileId, request.AreaRulePlanningId)
                .ConfigureAwait(false);

            if (!result.Success || result.Model == null)
            {
                return new OperationDataResult<CalendarTaskAttachmentDto>(false, result.Message);
            }

            var arpFile = result.Model;
            return new OperationDataResult<CalendarTaskAttachmentDto>(true, new CalendarTaskAttachmentDto
            {
                Id = arpFile.Id,
                OriginalFileName = arpFile.OriginalFileName ?? string.Empty,
                MimeType = arpFile.MimeType ?? string.Empty,
                SizeBytes = arpFile.SizeBytes,
                DownloadUrl = $"/api/backend-configuration-pn/calendar/tasks/{request.AreaRulePlanningId}/files/{arpFile.Id}",
                DriveFileId = arpFile.DriveFileId,
                DriveModifiedTime = arpFile.DriveModifiedTime
            });
        }
        catch (GoogleDriveTokenRevokedException)
        {
            return new OperationDataResult<CalendarTaskAttachmentDto>(false,
                _localizationService.GetString("GoogleDriveTokenRevoked"));
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GoogleDriveController.Attach failed: {Message}", e.Message);
            return new OperationDataResult<CalendarTaskAttachmentDto>(false,
                _localizationService.GetString("GenericError"));
        }
    }

    /// <summary>
    /// Receives Drive change notifications fanned out by the OAuth proxy.
    /// Verifies the HMAC the proxy attaches (canonical
    /// <c>{channelId}|{resourceState}|{resourceId}|{messageNumber}|{date}</c>),
    /// the <c>Date</c> header (±2 min skew), and the channel-token JWT
    /// (<c>typ == "channel"</c>, signature against <c>ProxySigningKey</c>),
    /// then logs and returns 200. The actual refetch is deferred to PR-7's
    /// processor; PR-6's daily reconcile cron is the safety net for
    /// missed deliveries. Anonymous because the inbound caller is the
    /// stateless proxy, not an authenticated platform user — auth is the
    /// HMAC + JWT pair instead.
    /// </summary>
    [HttpPost("notify")]
    [AllowAnonymous]
    public async Task<IActionResult> Notify()
    {
        // Headers we need. The proxy fans these through verbatim from
        // Drive plus the HMAC + Date pair it adds to the customer-side
        // request.
        var authHeader = Request.Headers["Authorization"].ToString();
        var dateHeader = Request.Headers["Date"].ToString();
        var channelId = Request.Headers["X-Goog-Channel-ID"].ToString();
        var channelToken = Request.Headers["X-Goog-Channel-Token"].ToString();
        var resourceState = Request.Headers["X-Goog-Resource-State"].ToString();
        var resourceId = Request.Headers["X-Goog-Resource-ID"].ToString();
        var messageNumber = Request.Headers["X-Goog-Message-Number"].ToString();

        // Quick structural rejects — short-circuit before doing crypto.
        const string hmacScheme = "HMAC-SHA256 ";
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith(hmacScheme, StringComparison.Ordinal))
        {
            return Unauthorized();
        }
        if (string.IsNullOrEmpty(dateHeader) || string.IsNullOrEmpty(channelId)
            || string.IsNullOrEmpty(channelToken))
        {
            return Unauthorized();
        }

        // Skew check (±2 min). Anything older than that and we treat it
        // as a replay; anything newer and the customer clock is out of
        // sync with the proxy clock — same outcome.
        if (!DateTime.TryParseExact(dateHeader, "R",
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var requestDateUtc))
        {
            return Unauthorized();
        }
        var skew = DateTime.UtcNow - requestDateUtc;
        if (skew.Duration() > TimeSpan.FromMinutes(2))
        {
            return Unauthorized();
        }

        // HMAC verify against the canonical string the proxy signs.
        var canonical = $"{channelId}|{resourceState}|{resourceId}|{messageNumber}|{dateHeader}";
        var expected = HmacHex(canonical);
        var supplied = authHeader.Substring(hmacScheme.Length).Trim();
        if (!FixedTimeStringEquals(expected, supplied))
        {
            return Unauthorized();
        }

        // Channel-token JWT verify: signature + typ == "channel" + exp.
        if (!TryVerifyChannelJwt(channelToken, out var failureReason))
        {
            _logger.LogInformation(
                "GoogleDriveController.Notify rejected channel JWT (channelId={ChannelId}, reason={Reason})",
                channelId, failureReason);
            return Unauthorized();
        }

        // Channel lookup. If the row is missing (e.g. stale subscription
        // we never finished cleaning up) we still return 200 — Drive
        // should not retry, and PR-6's reconcile cron is the safety
        // net. Log so an operator can spot a leak.
        var channel = await _dbContext.DriveWatchChannels
            .Where(x => x.ChannelId == channelId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);
        if (channel == null)
        {
            _logger.LogWarning(
                "GoogleDriveController.Notify received notification for unknown channel {ChannelId} (resourceState={ResourceState}, msg={Message})",
                channelId, resourceState, messageNumber);
            return Ok();
        }

        // Spec PR-5: bind the JWT to the persisted SignedToken row.
        // Defence-in-depth: a future renewal path that re-uses a
        // ChannelId would otherwise accept a stale JWT. Compare in
        // constant time so a near-miss can't be timing-probed.
        var presentedTokenBytes = Encoding.UTF8.GetBytes(channelToken);
        var persistedTokenBytes = Encoding.UTF8.GetBytes(channel.SignedToken ?? string.Empty);
        if (presentedTokenBytes.Length != persistedTokenBytes.Length
            || !CryptographicOperations.FixedTimeEquals(presentedTokenBytes, persistedTokenBytes))
        {
            _logger.LogWarning(
                "GoogleDriveController.Notify channel-token mismatch for channel {ChannelId}",
                channelId);
            return Unauthorized();
        }

        // Spec PR-5: enqueue a refresh-files job. For now we just log;
        // PR-7's processor + PR-6's reconcile cron own the actual
        // refetch.
        _logger.LogInformation(
            "Drive change notification accepted (channelId={ChannelId}, tokenId={TokenId}, resourceState={ResourceState}, msg={Message})",
            channelId, channel.GoogleOAuthTokenId, resourceState, messageNumber);

        return Ok();
    }

    /// <summary>
    /// Verifies the X-Goog-Channel-Token JWT against the shared
    /// <c>ProxySigningKey</c>. Returns true on a valid <c>typ == "channel"</c>
    /// envelope; <paramref name="reason"/> is set to a non-sensitive
    /// label on rejection (logged, never returned to the caller).
    /// </summary>
    private bool TryVerifyChannelJwt(string jwt, out string reason)
    {
        reason = string.Empty;
        var parts = jwt.Split('.');
        if (parts.Length != 3)
        {
            reason = "shape";
            return false;
        }

        try
        {
            var headerJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[0]));
            var payloadBytes = Base64UrlDecode(parts[1]);
            var signature = Base64UrlDecode(parts[2]);

            using var headerDoc = System.Text.Json.JsonDocument.Parse(headerJson);
            var alg = headerDoc.RootElement.TryGetProperty("alg", out var algEl)
                ? algEl.GetString() : null;
            if (!string.Equals(alg, "HS256", StringComparison.Ordinal))
            {
                reason = "alg";
                return false;
            }

            var signingInput = Encoding.UTF8.GetBytes(parts[0] + "." + parts[1]);
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.ProxySigningKey));
            var expected = hmac.ComputeHash(signingInput);
            if (!CryptographicOperations.FixedTimeEquals(signature, expected))
            {
                reason = "signature";
                return false;
            }

            using var payloadDoc = System.Text.Json.JsonDocument.Parse(payloadBytes);
            var typ = payloadDoc.RootElement.TryGetProperty("typ", out var typEl)
                ? typEl.GetString() : null;
            if (!string.Equals(typ, "channel", StringComparison.Ordinal))
            {
                reason = "typ";
                return false;
            }

            if (payloadDoc.RootElement.TryGetProperty("exp", out var expEl)
                && expEl.TryGetInt64(out var expSec))
            {
                var nowSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (expSec <= nowSec)
                {
                    reason = "exp";
                    return false;
                }
            }

            return true;
        }
        catch (Exception)
        {
            reason = "parse";
            return false;
        }
    }

    private static byte[] Base64UrlDecode(string s)
    {
        var pad = 4 - (s.Length % 4);
        if (pad < 4) s += new string('=', pad);
        return Convert.FromBase64String(s.Replace('-', '+').Replace('_', '/'));
    }

    private static bool FixedTimeStringEquals(string a, string b)
    {
        // String forms come in as lowercase hex; FixedTimeEquals on the
        // raw bytes guards against early-exit timing leaks.
        if (a.Length != b.Length) return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
    }

    private string HmacHex(string canonical)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.ProxySigningKey));
        var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private string ResolveCustomerBaseUrl()
    {
        // Honor X-Forwarded-Proto / X-Forwarded-Host when present so the
        // value the proxy 302s back to matches the public URL the user
        // actually sees, not the internal hop.
        var proto = Request.Headers["X-Forwarded-Proto"].FirstOrDefault()
                    ?? Request.Scheme;
        var host = Request.Headers["X-Forwarded-Host"].FirstOrDefault()
                   ?? Request.Host.ToString();
        return $"{proto}://{host}";
    }

    private static string TrimTrailingSlash(string url)
        => url.EndsWith('/') ? url[..^1] : url;
}
