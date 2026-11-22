using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using BackendConfiguration.Pn.Hubs;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.Tokens;
using Microting.EformAngularFrontendBase.Infrastructure.Const;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// The authenticator against a token minted exactly as the host's login does
/// (eform-angular-frontend AuthService.GenerateToken: HS256, issuer =
/// audience, claims sub / jti / updated_at, expires) and validated with the
/// same JWT library rules the host's JwtBearer applies: inbound claim mapping
/// on (sub becomes NameIdentifier), lifetime with ClockSkew, and the
/// validated token's ValidTo on AuthenticationProperties.ExpiresUtc. No
/// database; the host itself is not running (see the report for what that
/// leaves unverified).
/// </summary>
[TestFixture]
public class LiveHubAuthenticatorTests
{
    private const string Issuer = "eform-test-issuer";
    private const string SigningKey = "a-test-signing-key-that-is-long-enough-for-hs256-0123456789";
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    private static string MintHostLoginToken(int userId, DateTime expiresUtc)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(AuthConsts.ClaimLastUpdateKey, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture)),
        };
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(Issuer, Issuer, claims, expires: expiresUtc, signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static LiveHubAuthenticator Authenticator(bool withoutExpiry = false, TimeProvider? clock = null,
        ILogger<LiveHubAuthenticator>? logger = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var auth = services.AddAuthentication();
        if (withoutExpiry)
            auth.AddScheme<AuthenticationSchemeOptions, NoExpiryHandler>(LiveHubAuthenticator.BearerScheme, _ => { });
        else
            // The host's TokenValidationParameters (AuthServiceCollectionExtensions.cs): no ClockSkew set.
            auth.AddScheme<HostLikeJwtOptions, HostLikeJwtHandler>(LiveHubAuthenticator.BearerScheme, o =>
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = Issuer,
                    ValidAudience = Issuer,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                });
        return new LiveHubAuthenticator(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            clock ?? TimeProvider.System, logger ?? NullLogger<LiveHubAuthenticator>.Instance);
    }

    private static HttpContext WithQueryToken(string token)
    {
        var http = new DefaultHttpContext();
        http.Request.QueryString = new QueryString("?access_token=" + token);
        return http;
    }

    [Test]
    public void TheClockSkew_IsJwtBearersDefault_WhichTheHostDoesNotOverride()
    {
        Assert.That(LiveHubAuthenticator.ClockSkew, Is.EqualTo(new TokenValidationParameters().ClockSkew));
        Assert.That(LiveHubAuthenticator.ClockSkew, Is.EqualTo(TimeSpan.FromMinutes(5)));
    }

    [Test]
    public async Task AHostLoginToken_ThroughTheMappedJwtPath_YieldsTheUserAndItsExpiryPlusTheSkew()
    {
        // Whole seconds: a JWT exp has no fractions.
        var expires = DateTime.UtcNow.AddHours(24);
        expires = new DateTime(expires.Ticks - expires.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
        var token = MintHostLoginToken(1234, expires);

        var result = await Authenticator().AuthenticateAsync(null, WithQueryToken(token));

        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.UserId, Is.EqualTo(1234), "sub is mapped to NameIdentifier");
            Assert.That(result.ValidUntil, Is.EqualTo(new DateTimeOffset(expires) + LiveHubAuthenticator.ClockSkew));
        });
    }

    /// <summary>
    /// A header-authenticated principal has no AuthenticationProperties here,
    /// only claims: the expiry is read from the raw <c>exp</c> claim or from
    /// its mapped name (<see cref="ClaimTypes.Expiration"/>).
    /// </summary>
    [TestCase("exp")]
    [TestCase(ClaimTypes.Expiration)]
    public async Task AHeaderPrincipal_ReadsTheExpiryFromTheRawOrTheMappedClaimName(string claimType)
    {
        var expires = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "7"),
            new Claim(AuthConsts.ClaimLastUpdateKey, "638000000000000000"),
            new Claim(claimType, expires.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture)),
        ], "Bearer"));

        var result = await Authenticator().AuthenticateAsync(principal, new DefaultHttpContext());

        Assert.That(result?.ValidUntil, Is.EqualTo(expires + LiveHubAuthenticator.ClockSkew));
    }

    /// <summary>
    /// The bearer handler refuses a token without exp, so a validated
    /// principal without any readable expiry means the extraction failed on
    /// this host. The connection is accepted with the host's token lifetime
    /// from now, and ONE Warning per process says so.
    /// </summary>
    [Test]
    [NonParallelizable] // the warning flag is per process
    public async Task AValidatedPrincipalWithoutAnyExpiry_IsAccepted_WithTheLifetimeFallback_AndOneWarning()
    {
        LiveHubAuthenticator.ResetMissingExpiryWarningForTests();
        var clock = new FakeTimeProvider(Now);
        var logger = new RecordingLogger<LiveHubAuthenticator>();
        var authenticator = Authenticator(withoutExpiry: true, clock: clock, logger: logger);

        var first = await authenticator.AuthenticateAsync(null, WithQueryToken("any"));
        clock.Advance(TimeSpan.FromMinutes(10));
        var second = await authenticator.AuthenticateAsync(null, WithQueryToken("any"));
        var header = await authenticator.AuthenticateAsync(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "9"), new Claim(AuthConsts.ClaimLastUpdateKey, "638000000000000000")],
            "Bearer")), new DefaultHttpContext());

        Assert.Multiple(() =>
        {
            Assert.That(first?.UserId, Is.EqualTo(9));
            Assert.That(first?.ValidUntil, Is.EqualTo(Now + LiveHubAuthenticator.HostTokenLifetime + LiveHubAuthenticator.ClockSkew));
            Assert.That(second?.ValidUntil,
                Is.EqualTo(Now.AddMinutes(10) + LiveHubAuthenticator.HostTokenLifetime + LiveHubAuthenticator.ClockSkew));
            Assert.That(header, Is.Not.Null);
            Assert.That(LiveHubAuthenticator.HostTokenLifetime, Is.EqualTo(TimeSpan.FromHours(24)));
            Assert.That(logger.Entries.Count(e => e.Level == LogLevel.Warning), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task TheLastUpdateClaim_IsTheHostsOwnKey()
    {
        // The authenticator's private constant must stay the host's AuthConsts value.
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "7"),
            new Claim(AuthConsts.ClaimLastUpdateKey, "638000000000000000"),
            new Claim("exp", "1893456000"),
        ], "Bearer"));

        Assert.That(await Authenticator().AuthenticateAsync(principal, new DefaultHttpContext()), Is.Not.Null);
    }

    /// <summary>JwtBearerOptions' shape, for the host-like handler below.</summary>
    internal sealed class HostLikeJwtOptions : AuthenticationSchemeOptions
    {
        public TokenValidationParameters TokenValidationParameters { get; set; } = new();
    }

    /// <summary>
    /// What the host's JwtBearerHandler does with a bearer token, reduced to
    /// the parts the hub depends on: validate with the scheme's parameters
    /// (inbound claim mapping on, as MapInboundClaims defaults to), and put
    /// the validated token's ValidTo on the ticket's ExpiresUtc.
    /// </summary>
    private sealed class HostLikeJwtHandler(IOptionsMonitor<HostLikeJwtOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<HostLikeJwtOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers.Authorization.ToString();
            if (!header.StartsWith("Bearer ")) return Task.FromResult(AuthenticateResult.NoResult());
            try
            {
                var principal = new JwtSecurityTokenHandler { MapInboundClaims = true }
                    .ValidateToken(header["Bearer ".Length..], Options.TokenValidationParameters, out var validated);
                var properties = new AuthenticationProperties { ExpiresUtc = new DateTimeOffset(validated.ValidTo, TimeSpan.Zero) };
                return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, properties, Scheme.Name)));
            }
            catch (Exception e)
            {
                return Task.FromResult(AuthenticateResult.Fail(e));
            }
        }
    }

    /// <summary>Validates anyone as user 9 with the host's claims, but no expiry anywhere (no exp claim, no ExpiresUtc).</summary>
    private sealed class NoExpiryHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "9"),
                new Claim(AuthConsts.ClaimLastUpdateKey, "638000000000000000"),
            ], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
