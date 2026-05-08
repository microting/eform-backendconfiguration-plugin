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
            return new OperationDataResult<string>(false, e.Message);
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
        var calendarPage = "/plugins/backend-configuration-pn/calendar";

        if (string.IsNullOrWhiteSpace(envelope))
        {
            return Redirect($"{calendarPage}?gdrive_err=missing_envelope");
        }

        // Pull the nonce from the cookie and clear it — single-use.
        if (!Request.Cookies.TryGetValue(NonceCookieName, out var cookieValue)
            || string.IsNullOrEmpty(cookieValue))
        {
            return Redirect($"{calendarPage}?gdrive_err=nonce_missing");
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
                return Redirect($"{calendarPage}?gdrive_err=nonce_corrupt");
            }
            cookieUserId = int.Parse(raw[..pipe], CultureInfo.InvariantCulture);
            expectedNonce = raw[(pipe + 1)..];
        }
        catch (Exception)
        {
            return Redirect($"{calendarPage}?gdrive_err=nonce_corrupt");
        }

        var currentUserId = _userService.UserId;
        if (cookieUserId != currentUserId)
        {
            return Redirect($"{calendarPage}?gdrive_err=user_mismatch");
        }

        try
        {
            await _authService.StoreEnvelopeAsync(envelope, currentUserId, expectedNonce)
                .ConfigureAwait(false);
            return Redirect($"{calendarPage}?gdrive_success=true");
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "OAuthFinish rejected envelope: {Message}", e.Message);
            return Redirect($"{calendarPage}?gdrive_err={Uri.EscapeDataString(e.GetType().Name)}");
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
            return new OperationDataResult<GoogleDriveStatusModel>(false, e.Message);
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
            return new OperationDataResult<CalendarTaskAttachmentDto>(false, e.Message);
        }
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
