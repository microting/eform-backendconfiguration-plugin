#nullable enable
using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BackendConfiguration.Pn.Hubs;

/// <summary>
/// Who is on the other end of a hub connection. A copy of kanban's
/// <c>KanbanHubAuthenticator</c> (eform-kanban-plugin Hubs/), so both hubs in
/// the host accept exactly the same tokens: the host JWT that the gRPC login
/// (and the REST login) returns.
/// <para>
/// A long-polling connection sends the Authorization header, and the host's
/// UseAuthentication has already turned it into the connection's user. A
/// WebSocket cannot carry headers, so the client sends the token as
/// <c>?access_token=</c>, which the host's JwtBearer setup does not read. That
/// token is validated here with the host's own "Bearer" handler, in a fresh
/// scope: the request's handler has already cached its empty result.
/// </para>
/// <para>
/// The handler is called directly, not through IAuthenticationService: the
/// service would also run the host's IClaimsTransformation, which writes a
/// response header on the hub request; that response has already started, so
/// the write throws and a valid token would be refused. The transformer's
/// rejections are kept by <see cref="HasHostClaims"/>.
/// </para>
/// </summary>
/// <summary>
/// An authenticated hub caller: the host user id and the instant its token
/// stops being accepted (expiry plus the bearer scheme's ClockSkew, the
/// host's own rule for gRPC and REST).
/// </summary>
public sealed record LiveHubAuthentication(ClaimsPrincipal Principal, int UserId, DateTimeOffset ValidUntil);

public class LiveHubAuthenticator(IServiceScopeFactory scopeFactory, TimeProvider timeProvider, ILogger<LiveHubAuthenticator> logger)
{
    public const string BearerScheme = "Bearer";

    /// <summary>
    /// How long after <c>exp</c> the host still accepts a token: JwtBearer's
    /// default <c>TokenValidationParameters.ClockSkew</c> (5 minutes). The host
    /// does not override it (eform-angular-frontend
    /// eFormAPI.Web/Hosting/Security/AuthServiceCollectionExtensions.cs,
    /// AddEFormAuth: its TokenValidationParameters set only issuer, audience
    /// and signing key).
    /// </summary>
    public static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The lifetime of a host login token (eform-angular-frontend
    /// eFormAPI.Web/Services/AuthService.cs, GenerateToken:
    /// <c>expires: DateTime.Now.AddHours(24)</c>). Only the fallback cut-off
    /// when a validated token's expiry cannot be read.
    /// </summary>
    public static readonly TimeSpan HostTokenLifetime = TimeSpan.FromHours(24);

    /// <summary>1 once the missing-expiry Warning was written in this process.</summary>
    private static int _missingExpiryWarned;

    internal static void ResetMissingExpiryWarningForTests() => Interlocked.Exchange(ref _missingExpiryWarned, 0);

    /// <summary>The host's last-update claim (host AuthConsts.ClaimLastUpdateKey, kanban's <c>ApiKeyDefaults.LastUpdateClaim</c>).</summary>
    private const string LastUpdateClaim = "updated_at";

    private const string AccessTokenQueryKey = "access_token";

    /// <summary>The raw JWT expiry claim (seconds since the Unix epoch). The host maps inbound claims but its map has no entry for it.</summary>
    private const string ExpiryClaim = "exp";

    /// <summary>The name <c>exp</c> would get from a claim map that renames it.</summary>
    private const string MappedExpiryClaim = ClaimTypes.Expiration;

    /// <summary>
    /// The caller, or null when it is not authenticated or lacks the host's
    /// claims. The expiry is the validated token's (the ticket's ExpiresUtc,
    /// which JwtBearer sets from the token's ValidTo), else the <c>exp</c>
    /// claim under its raw or mapped name. The bearer handler refuses a token
    /// without exp, so no readable expiry means the extraction failed on this
    /// host: the caller is accepted with a cut-off of now +
    /// <see cref="HostTokenLifetime"/>, and one Warning per process says so.
    /// Expiry is not checked here; the hub compares ValidUntil with its clock.
    /// </summary>
    public async Task<LiveHubAuthentication?> AuthenticateAsync(ClaimsPrincipal? current, HttpContext? http)
    {
        ClaimsPrincipal? principal;
        DateTimeOffset? expires;
        if (current?.Identity?.IsAuthenticated == true)
        {
            // A header-authenticated user has passed the host's handler and
            // transformer already; only its claims are at hand.
            principal = current;
            expires = ExpiryOf(current);
        }
        else
        {
            var bearer = http?.Request.Query[AccessTokenQueryKey].ToString();
            if (string.IsNullOrWhiteSpace(bearer)) return null;

            await using var scope = scopeFactory.CreateAsyncScope();
            var probe = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            probe.Request.Headers.Authorization = $"{BearerScheme} {bearer}";
            var handlers = scope.ServiceProvider.GetRequiredService<IAuthenticationHandlerProvider>();
            var handler = await handlers.GetHandlerAsync(probe, BearerScheme);
            if (handler is null) return null;
            var result = await handler.AuthenticateAsync();
            principal = result.Succeeded ? result.Principal : null;
            expires = result.Succeeded ? result.Properties?.ExpiresUtc ?? ExpiryOf(principal) : null;
        }

        if (principal?.Identity?.IsAuthenticated != true || !HasHostClaims(principal)) return null;
        if (expires is null)
        {
            expires = timeProvider.GetUtcNow() + HostTokenLifetime;
            if (Interlocked.Exchange(ref _missingExpiryWarned, 1) == 0)
                logger.LogWarning(
                    "Live hub: a validated token carried no readable expiry (no ExpiresUtc, no exp claim); "
                    + "connections are cut off {Lifetime} after connect instead. Logged once per process.",
                    HostTokenLifetime);
        }

        return new LiveHubAuthentication(principal, UserIdOf(principal)!.Value, expires.Value + ClockSkew);
    }

    /// <summary>
    /// The host's ClaimsTransformer refuses a principal with no, or a
    /// non-numeric, user id claim, or no numeric "updated_at" claim; this is
    /// never more lenient than the host.
    /// </summary>
    private static bool HasHostClaims(ClaimsPrincipal principal) =>
        UserIdOf(principal) is not null
        && long.TryParse(principal.FindFirst(LastUpdateClaim)?.Value, out _);

    /// <summary>The token's expiry from its <c>exp</c> claim (raw or mapped name), or null when it has none or it is not a number.</summary>
    internal static DateTimeOffset? ExpiryOf(ClaimsPrincipal? principal) =>
        long.TryParse((principal?.FindFirst(ExpiryClaim) ?? principal?.FindFirst(MappedExpiryClaim))?.Value, out var seconds)
        && seconds is >= 0 and <= 253402300799 // DateTimeOffset.MaxValue
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;

    /// <summary>The host user id (NameIdentifier claim), or null.</summary>
    public static int? UserIdOf(ClaimsPrincipal principal) =>
        int.TryParse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;
}
