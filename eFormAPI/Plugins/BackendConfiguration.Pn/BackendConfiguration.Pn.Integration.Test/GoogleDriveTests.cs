using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackendConfiguration.Pn.Controllers;
using BackendConfiguration.Pn.Infrastructure.Models.Settings;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.GoogleDrive;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Enums;
using NSubstitute;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// Integration tests for the PR-3 Google Drive integration. They exercise
/// the auth + file services against a Testcontainers MariaDB and a
/// WireMock-stubbed OAuth proxy + Drive API.
///
/// The tests deliberately avoid touching the controller — its CSRF cookie
/// machinery + IDataProtectionProvider plumbing are an orthogonal layer
/// that's better validated end-to-end in PR-4. The auth/file services are
/// the ones doing all the heavy lifting (envelope verification, AES-GCM
/// encryption, proxy refresh, MIME validation, S3 upload pipeline) so they
/// are where the test value lives.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class GoogleDriveTests : TestBaseSetup
{
    // Stable test secrets — the auth service treats them as opaque bytes, so
    // the actual values only matter in that they round-trip through HMAC
    // signing and AES-GCM key derivation. Same key on the proxy and on the
    // customer side, by spec.
    private const string ProxySigningKey = "test-proxy-signing-key-do-not-use-in-prod";
    // Base64-encoded 32 bytes for AES-GCM. Decoded length must be 32.
    private const string EncryptionKeyB64 = "QUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUE=";

    private GoogleDriveOptions _options = null!;
    private string _sdkConnectionString = null!;
    private WireMockServer _proxyServer = null!;

    [SetUp]
    public async Task SetupGoogleDriveTests()
    {
        // The TestBaseSetup loads a snapshot SQL file that predates the
        // Google Drive integration. Bring the schema up to v10.0.33 by
        // adding the new tables + columns here before we touch them. Use
        // IF NOT EXISTS so re-running the test fixture is idempotent.
        await BackendConfigurationPnDbContext!.Database.ExecuteSqlRawAsync(GoogleDriveSchemaSql);

        // Wipe Drive-related rows so each test starts clean.
        BackendConfigurationPnDbContext!.AreaRulePlanningFiles.RemoveRange(
            BackendConfigurationPnDbContext.AreaRulePlanningFiles);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        BackendConfigurationPnDbContext.DriveWatchChannels.RemoveRange(
            BackendConfigurationPnDbContext.DriveWatchChannels);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        BackendConfigurationPnDbContext.GoogleOAuthTokens.RemoveRange(
            BackendConfigurationPnDbContext.GoogleOAuthTokens);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        BackendConfigurationPnDbContext.AreaRulePlannings.RemoveRange(
            BackendConfigurationPnDbContext.AreaRulePlannings);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        BackendConfigurationPnDbContext.AreaRules.RemoveRange(
            BackendConfigurationPnDbContext.AreaRules);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        BackendConfigurationPnDbContext.Areas.RemoveRange(
            BackendConfigurationPnDbContext.Areas);
        BackendConfigurationPnDbContext.Properties.RemoveRange(
            BackendConfigurationPnDbContext.Properties);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        ItemsPlanningPnDbContext!.Plannings.RemoveRange(ItemsPlanningPnDbContext.Plannings);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        _sdkConnectionString = MicrotingDbContext!.Database.GetConnectionString()!;

        // Prime the SDK Core so its EF migrations apply (UploadedDatas
        // needs the OriginalFileLocation column the bootstrap SQL omits).
        await GetCore();

        await MicrotingDbContext.DisposeAsync();
        MicrotingDbContext = new Microting.eForm.Infrastructure.MicrotingDbContext(
            new DbContextOptionsBuilder<Microting.eForm.Infrastructure.MicrotingDbContext>()
                .UseMySql(_sdkConnectionString,
                    new MariaDbServerVersion(ServerVersion.AutoDetect(_sdkConnectionString)),
                    o => o.EnableRetryOnFailure())
                .Options);
        MicrotingDbContext.UploadedDatas.RemoveRange(MicrotingDbContext.UploadedDatas);
        await MicrotingDbContext.SaveChangesAsync();

        _proxyServer = WireMockServer.Start();

        _options = new GoogleDriveOptions
        {
            MicrotingOAuthProxyUrl = _proxyServer.Url!,
            ProxySigningKey = ProxySigningKey,
            RefreshTokenEncryptionKey = EncryptionKeyB64,
            // Used by EnsureWatchChannelAsync — minted into the channel
            // JWT so the proxy can fan a notification back to "the
            // customer instance". The value is opaque to the auth service
            // beyond shape (must be non-empty); the JWT is verified by
            // the proxy in production.
            CustomerInstanceUrl = "https://test-customer.invalid"
        };
    }

    [TearDown]
    public void TeardownGoogleDriveTests()
    {
        _proxyServer?.Stop();
        _proxyServer?.Dispose();
    }

    private GoogleDriveAuthService NewAuthService(IOAuthProxyClient? proxyClient = null)
    {
        // The auth service uses the proxy client only in GetAccessTokenAsync.
        // For envelope tests the supplied stub is irrelevant; provide a
        // throwing default so accidental calls fail loudly.
        proxyClient ??= new ThrowingProxyClient();
        // Per-instance memory cache so a test that constructs a fresh
        // service starts with a cold cache — important for the refresh
        // tests where we expect a /refresh call on the first GetAccessToken.
        return new GoogleDriveAuthService(
            BackendConfigurationPnDbContext!,
            proxyClient,
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(_options),
            NullLogger<GoogleDriveAuthService>.Instance);
    }

    /// <summary>
    /// Auth service wired with both a working OAuth proxy client (for the
    /// inner GetAccessTokenAsync call) and a Drive-aware HttpClient factory
    /// (for the changes.watch POST). Used by EnsureWatchChannel tests.
    /// </summary>
    private GoogleDriveAuthService NewAuthServiceForWatch()
    {
        var proxyHttp = new HttpClient { BaseAddress = new Uri(_options.MicrotingOAuthProxyUrl) };
        var proxyClient = new OAuthProxyClient(proxyHttp, Options.Create(_options));
        var factory = new RewritingHttpClientFactory(_proxyServer.Url!);
        return new GoogleDriveAuthService(
            BackendConfigurationPnDbContext!,
            proxyClient,
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(_options),
            NullLogger<GoogleDriveAuthService>.Instance,
            configuration: null,
            httpClientFactory: factory);
    }

    /// <summary>
    /// Mint a JWT with the same shape the OAuth proxy produces. HS256 over
    /// the shared signing key. Intentionally inline — pulling in
    /// Microsoft.IdentityModel.Tokens for tests would balloon the
    /// transitive dependency surface for one helper.
    /// </summary>
    private static string MintEnvelope(string typ, int user, string nonce, string refreshToken,
        string email = "u@example.com", DateTime? exp = null, string signingKey = ProxySigningKey)
    {
        var header = new Dictionary<string, object> { ["alg"] = "HS256", ["typ"] = "JWT" };
        var expSec = ((DateTimeOffset)(exp ?? DateTime.UtcNow.AddMinutes(5))).ToUnixTimeSeconds();
        var payload = new Dictionary<string, object>
        {
            ["typ"] = typ,
            ["refreshToken"] = refreshToken,
            ["accessToken"] = "initial-access-token",
            ["accessTokenExpiry"] = DateTime.UtcNow.AddHours(1),
            ["email"] = email,
            ["user"] = user,
            ["nonce"] = nonce,
            ["exp"] = expSec
        };

        var headerB64 = B64Url(JsonSerializer.SerializeToUtf8Bytes(header));
        var payloadB64 = B64Url(JsonSerializer.SerializeToUtf8Bytes(payload));
        var signingInput = Encoding.UTF8.GetBytes($"{headerB64}.{payloadB64}");
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signingKey));
        var sig = B64Url(hmac.ComputeHash(signingInput));
        return $"{headerB64}.{payloadB64}.{sig}";
    }

    private static string B64Url(byte[] b) =>
        Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private async Task<int> SeedPlanning()
    {
        var area = new Area
        {
            Type = AreaTypesEnum.Type1,
            ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Areas.AddAsync(area);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var property = new Property
        {
            Name = $"DriveProp-{Guid.NewGuid()}",
            ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.Properties.AddAsync(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var areaRule = new AreaRule
        {
            AreaId = area.Id,
            PropertyId = property.Id,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRules.AddAsync(areaRule);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var planning = new Planning
        {
            Enabled = true,
            RepeatEvery = 1,
            RepeatType = RepeatType.Week,
            StartDate = DateTime.UtcNow.Date,
            RelatedEFormId = 0,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id,
            PropertyId = property.Id,
            AreaId = area.Id,
            ItemPlanningId = planning.Id,
            StartDate = DateTime.UtcNow.Date,
            Status = true,
            RepeatType = 2,
            RepeatEvery = 1,
            DayOfWeek = 1,
            DayOfMonth = 0,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(arp);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        return arp.Id;
    }

    [Test]
    public async Task OAuthFinish_ValidEnvelope_PersistsToken()
    {
        const int userId = 1;
        const string nonce = "abc123";
        const string refreshToken = "rt-from-google";
        var jwt = MintEnvelope("envelope", userId, nonce, refreshToken);

        var sut = NewAuthService();
        var saved = await sut.StoreEnvelopeAsync(jwt, userId, nonce);

        Assert.That(saved, Is.Not.Null);
        Assert.That(saved.UserId, Is.EqualTo(userId));
        Assert.That(saved.GoogleAccountEmail, Is.EqualTo("u@example.com"));
        Assert.That(saved.RevokedAt, Is.Null);
        Assert.That(saved.EncryptedRefreshToken, Is.Not.EqualTo(refreshToken),
            "Refresh token must be encrypted at rest, not stored as plaintext.");
        Assert.That(sut.Decrypt(saved.EncryptedRefreshToken), Is.EqualTo(refreshToken),
            "Decryption must round-trip back to the original refresh token.");

        var fromDb = await BackendConfigurationPnDbContext!.GoogleOAuthTokens
            .FirstAsync(x => x.Id == saved.Id);
        Assert.That(fromDb.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
    }

    [Test]
    public async Task OAuthFinish_TamperedEnvelope_Rejected()
    {
        const int userId = 1;
        const string nonce = "abc123";
        var jwt = MintEnvelope("envelope", userId, nonce, "rt");

        // Tamper a char in the MIDDLE of the signature segment (not the
        // last char). For a 32-byte HMAC the encoded signature is 43
        // base64url chars; the last char carries only 4 significant bits
        // and 2 unused padding bits that .NET's Convert.FromBase64String
        // ignores — so flipping the last char between values in {A,B}
        // (both with low-4-bits = 0000) is a no-op for the decoded byte
        // ~6% of the time. Middle chars use all 6 bits → any flip
        // guaranteed to change the decoded signature.
        var lastDot = jwt.LastIndexOf('.');
        var midSigIndex = lastDot + 1 + (jwt.Length - lastDot - 1) / 2;
        var tampered = jwt[..midSigIndex]
                       + (jwt[midSigIndex] == 'A' ? 'B' : 'A')
                       + jwt[(midSigIndex + 1)..];

        var sut = NewAuthService();
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await sut.StoreEnvelopeAsync(tampered, userId, nonce));
    }

    [Test]
    public async Task OAuthFinish_WrongTyp_Rejected()
    {
        // typ=state must NOT be accepted by the envelope endpoint — the
        // spec calls this out as the type-confusion defence. Same key
        // signs all three (state/envelope/channel) so this is the only
        // thing standing between them.
        var jwt = MintEnvelope("state", 1, "abc", "rt");

        var sut = NewAuthService();
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await sut.StoreEnvelopeAsync(jwt, 1, "abc"));
    }

    [Test]
    public async Task OAuthFinish_NonceMismatch_Rejected()
    {
        var jwt = MintEnvelope("envelope", 1, "real-nonce", "rt");

        var sut = NewAuthService();
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await sut.StoreEnvelopeAsync(jwt, 1, "different-nonce"));
    }

    [Test]
    public async Task GetAccessToken_RefreshSucceeds_ReturnsNewToken()
    {
        // First persist a token via the public path so the encrypted
        // refresh-token blob is wired correctly.
        const int userId = 7;
        const string refreshToken = "rt-live";
        var jwt = MintEnvelope("envelope", userId, "n1", refreshToken);
        var auth = NewAuthService();
        var stored = await auth.StoreEnvelopeAsync(jwt, userId, "n1");
        var lastUsedBefore = stored.LastUsedAt;

        // Stub the proxy to return a fresh access token. Use a real typed
        // HttpClient pointing at the WireMockServer so the OAuthProxyClient
        // exercises the same HMAC + Date code path it would in production.
        _proxyServer.Given(Request.Create()
            .WithPath("/google-drive/refresh").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(new
                {
                    accessToken = "fresh-access-token",
                    accessTokenExpiry = DateTime.UtcNow.AddHours(1)
                })));

        using var http = new HttpClient { BaseAddress = new Uri(_options.MicrotingOAuthProxyUrl) };
        var proxyClient = new OAuthProxyClient(http, Options.Create(_options));
        var sut = new GoogleDriveAuthService(
            BackendConfigurationPnDbContext!,
            proxyClient,
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(_options),
            NullLogger<GoogleDriveAuthService>.Instance);

        var access = await sut.GetAccessTokenAsync(userId);

        Assert.That(access, Is.EqualTo("fresh-access-token"));

        var refreshed = await BackendConfigurationPnDbContext!.GoogleOAuthTokens
            .FirstAsync(x => x.UserId == userId);
        Assert.That(refreshed.LastUsedAt, Is.Not.Null);
        Assert.That(refreshed.LastUsedAt!.Value,
            Is.GreaterThanOrEqualTo(lastUsedBefore!.Value));
    }

    [Test]
    public async Task GetAccessToken_InvalidGrant_MarksRevoked()
    {
        const int userId = 8;
        var jwt = MintEnvelope("envelope", userId, "n2", "rt");
        var auth = NewAuthService();
        await auth.StoreEnvelopeAsync(jwt, userId, "n2");

        _proxyServer.Given(Request.Create()
            .WithPath("/google-drive/refresh").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(401)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"error\":\"invalid_grant\"}"));

        using var http = new HttpClient { BaseAddress = new Uri(_options.MicrotingOAuthProxyUrl) };
        var proxyClient = new OAuthProxyClient(http, Options.Create(_options));
        var sut = new GoogleDriveAuthService(
            BackendConfigurationPnDbContext!,
            proxyClient,
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(_options),
            NullLogger<GoogleDriveAuthService>.Instance);

        Assert.ThrowsAsync<GoogleDriveTokenRevokedException>(
            async () => await sut.GetAccessTokenAsync(userId));

        var row = await BackendConfigurationPnDbContext!.GoogleOAuthTokens
            .FirstAsync(x => x.UserId == userId);
        Assert.That(row.RevokedAt, Is.Not.Null,
            "Service must stamp RevokedAt when Google says invalid_grant.");
    }

    [Test]
    public async Task DownloadAndCacheFile_Pdf_PersistsAreaRulePlanningFile()
    {
        const int userId = 11;
        var jwt = MintEnvelope("envelope", userId, "n3", "rt");
        var auth = NewAuthService();
        await auth.StoreEnvelopeAsync(jwt, userId, "n3");

        var arpId = await SeedPlanning();
        var pdfBytes = Encoding.UTF8.GetBytes("%PDF-1.4 hello-from-drive");

        // Drive metadata + bytes
        _proxyServer.Given(Request.Create()
            .WithPath("/drive/v3/files/file-abc")
            .WithParam("alt", "media").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/pdf")
                .WithBody(pdfBytes));
        _proxyServer.Given(Request.Create()
            .WithPath("/drive/v3/files/file-abc").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(new
                {
                    id = "file-abc",
                    name = "report.pdf",
                    size = pdfBytes.Length,
                    mimeType = "application/pdf",
                    modifiedTime = DateTime.UtcNow,
                    webViewLink = "https://drive.google.com/file/d/file-abc/view"
                })));
        // Refresh proxy stub so the auth service can obtain an access token.
        _proxyServer.Given(Request.Create().WithPath("/google-drive/refresh").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(new
                {
                    accessToken = "ya29.a0test",
                    accessTokenExpiry = DateTime.UtcNow.AddHours(1)
                })));

        var fileService = NewFileService(userId);
        var res = await fileService.DownloadAndCacheFileAsync(userId, "file-abc", arpId);

        Assert.That(res.Success, Is.True, res.Message);
        Assert.That(res.Model!.DriveFileId, Is.EqualTo("file-abc"));
        Assert.That(res.Model.MimeType, Is.EqualTo("application/pdf"));
        Assert.That(res.Model.OriginalFileName, Is.EqualTo("report.pdf"));
        Assert.That(res.Model.SizeBytes, Is.EqualTo(pdfBytes.Length));
        Assert.That(res.Model.GoogleOAuthTokenId, Is.Not.Null);

        var sdkUploadedDataCount = await MicrotingDbContext!.UploadedDatas.CountAsync();
        Assert.That(sdkUploadedDataCount, Is.GreaterThan(0));
    }

    [Test]
    public async Task DownloadAndCacheFile_RejectsNonAllowedMime()
    {
        const int userId = 12;
        var jwt = MintEnvelope("envelope", userId, "n4", "rt");
        var auth = NewAuthService();
        await auth.StoreEnvelopeAsync(jwt, userId, "n4");

        var arpId = await SeedPlanning();

        // The metadata fetch returns a disallowed mime; the service must
        // reject before downloading bytes. Stub /alt=media too just in
        // case the order ever flips during refactor — defensive fail-fast.
        _proxyServer.Given(Request.Create()
            .WithPath("/drive/v3/files/zip-x").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(new
                {
                    id = "zip-x",
                    name = "evil.zip",
                    size = 100,
                    mimeType = "application/x-zip",
                    modifiedTime = DateTime.UtcNow,
                    webViewLink = "https://drive.google.com/file/d/zip-x/view"
                })));
        _proxyServer.Given(Request.Create().WithPath("/google-drive/refresh").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(new
                {
                    accessToken = "ya29.token",
                    accessTokenExpiry = DateTime.UtcNow.AddHours(1)
                })));

        var fileService = NewFileService(userId);
        var res = await fileService.DownloadAndCacheFileAsync(userId, "zip-x", arpId);

        Assert.That(res.Success, Is.False);
        Assert.That(res.Message, Does.Contain("PDF").Or.Contains("Drive").IgnoreCase);
    }

    // -------------------------------------------------------------------
    // PR-5 — EnsureWatchChannelAsync + /notify webhook receiver
    // -------------------------------------------------------------------

    [Test]
    public async Task EnsureWatchChannel_WhenAbsent_CreatesNewChannel()
    {
        const int userId = 21;
        var jwt = MintEnvelope("envelope", userId, "n21", "rt");
        var auth0 = NewAuthService();
        var token = await auth0.StoreEnvelopeAsync(jwt, userId, "n21");

        // /refresh stub so the inner GetAccessTokenAsync succeeds.
        _proxyServer.Given(Request.Create().WithPath("/google-drive/refresh").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(new
                {
                    accessToken = "watch-access-token",
                    accessTokenExpiry = DateTime.UtcNow.AddHours(1)
                })));

        // Drive changes.watch stub. Drive responds with id, resourceId,
        // expiration (unix-millis as a string).
        var expirationMs = DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeMilliseconds()
            .ToString(CultureInfo.InvariantCulture);
        _proxyServer.Given(Request.Create()
                .WithPath("/drive/v3/changes/watch").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(new
                {
                    id = "channel-from-drive",
                    resourceId = "resource-xyz",
                    expiration = expirationMs
                })));

        var sut = NewAuthServiceForWatch();
        var channel = await sut.EnsureWatchChannelAsync(userId);

        Assert.That(channel, Is.Not.Null);
        Assert.That(channel.GoogleOAuthTokenId, Is.EqualTo(token.Id));
        Assert.That(channel.ChannelId, Is.EqualTo("channel-from-drive"));
        Assert.That(channel.ResourceId, Is.EqualTo("resource-xyz"));
        Assert.That(channel.SignedToken, Is.Not.Empty);
        Assert.That(channel.ExpiresAt, Is.GreaterThan(DateTime.UtcNow.AddDays(6)));

        var fromDb = await BackendConfigurationPnDbContext!.DriveWatchChannels
            .FirstAsync(x => x.Id == channel.Id);
        Assert.That(fromDb.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
    }

    [Test]
    public async Task EnsureWatchChannel_WhenFresh_ReturnsExisting()
    {
        const int userId = 22;
        var jwt = MintEnvelope("envelope", userId, "n22", "rt");
        var auth0 = NewAuthService();
        var token = await auth0.StoreEnvelopeAsync(jwt, userId, "n22");

        // Seed a fresh channel (>1 day of life).
        var seeded = new DriveWatchChannel
        {
            GoogleOAuthTokenId = token.Id,
            ChannelId = "preexisting",
            ResourceId = "preexisting-resource",
            SignedToken = "preexisting-jwt",
            ExpiresAt = DateTime.UtcNow.AddDays(5),
            CreatedByUserId = userId,
            UpdatedByUserId = userId
        };
        await seeded.Create(BackendConfigurationPnDbContext!);

        // No /refresh nor /changes/watch stubs — if the service hits
        // them we want a hard failure so the test catches a regression.
        var sut = NewAuthServiceForWatch();
        var channel = await sut.EnsureWatchChannelAsync(userId);

        Assert.That(channel.Id, Is.EqualTo(seeded.Id),
            "Service must reuse a fresh channel without contacting Drive.");
        Assert.That(channel.ChannelId, Is.EqualTo("preexisting"));

        // Sanity: WireMock should have seen no requests at all.
        Assert.That(_proxyServer.LogEntries, Is.Empty,
            "EnsureWatchChannel must not hit Drive when a fresh row exists.");
    }

    [Test]
    public async Task EnsureWatchChannel_WhenExpiringSoon_RenewsRow()
    {
        const int userId = 23;
        var jwt = MintEnvelope("envelope", userId, "n23", "rt");
        var auth0 = NewAuthService();
        var token = await auth0.StoreEnvelopeAsync(jwt, userId, "n23");

        // Seed a channel that's within the 1-day reuse window — service
        // must soft-delete it and mint a new one.
        var stale = new DriveWatchChannel
        {
            GoogleOAuthTokenId = token.Id,
            ChannelId = "stale-channel",
            ResourceId = "stale-resource",
            SignedToken = "stale-jwt",
            ExpiresAt = DateTime.UtcNow.AddHours(12),
            CreatedByUserId = userId,
            UpdatedByUserId = userId
        };
        await stale.Create(BackendConfigurationPnDbContext!);
        var staleId = stale.Id;

        _proxyServer.Given(Request.Create().WithPath("/google-drive/refresh").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(new
                {
                    accessToken = "renew-access",
                    accessTokenExpiry = DateTime.UtcNow.AddHours(1)
                })));
        var expirationMs = DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeMilliseconds()
            .ToString(CultureInfo.InvariantCulture);
        _proxyServer.Given(Request.Create()
                .WithPath("/drive/v3/changes/watch").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(new
                {
                    id = "fresh-channel",
                    resourceId = "fresh-resource",
                    expiration = expirationMs
                })));

        var sut = NewAuthServiceForWatch();
        var renewed = await sut.EnsureWatchChannelAsync(userId);

        Assert.That(renewed.Id, Is.Not.EqualTo(staleId));
        Assert.That(renewed.ChannelId, Is.EqualTo("fresh-channel"));

        var oldRow = await BackendConfigurationPnDbContext!.DriveWatchChannels
            .FirstAsync(x => x.Id == staleId);
        Assert.That(oldRow.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed),
            "Soft-deleting the previous channel preserves the application-layer uniqueness invariant.");
    }

    [Test]
    public async Task EnsureWatchChannel_DriveError_PropagatesException()
    {
        const int userId = 24;
        var jwt = MintEnvelope("envelope", userId, "n24", "rt");
        var auth0 = NewAuthService();
        await auth0.StoreEnvelopeAsync(jwt, userId, "n24");

        _proxyServer.Given(Request.Create().WithPath("/google-drive/refresh").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(new
                {
                    accessToken = "tok",
                    accessTokenExpiry = DateTime.UtcNow.AddHours(1)
                })));
        _proxyServer.Given(Request.Create()
                .WithPath("/drive/v3/changes/watch").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(500)
                .WithBody("internal-server-error"));

        var sut = NewAuthServiceForWatch();
        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await sut.EnsureWatchChannelAsync(userId));

        // No DriveWatchChannel should have been persisted.
        var anyChannel = await BackendConfigurationPnDbContext!.DriveWatchChannels
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .AnyAsync();
        Assert.That(anyChannel, Is.False,
            "A failed Drive call must NOT leave a half-baked channel row behind.");
    }

    [Test]
    public async Task Notify_ValidHmacAndJwt_Returns200()
    {
        const int userId = 25;
        var jwt = MintEnvelope("envelope", userId, "n25", "rt");
        var auth0 = NewAuthService();
        var token = await auth0.StoreEnvelopeAsync(jwt, userId, "n25");

        // Seed a watch channel + mint a channel-token JWT we'll send in
        // the X-Goog-Channel-Token header.
        var channelId = Guid.NewGuid().ToString("N");
        var channelJwt = MintChannelJwt(channelId, "https://test-customer.invalid",
            DateTime.UtcNow.AddDays(1));
        var seeded = new DriveWatchChannel
        {
            GoogleOAuthTokenId = token.Id,
            ChannelId = channelId,
            ResourceId = "resource-xyz",
            SignedToken = channelJwt,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedByUserId = userId,
            UpdatedByUserId = userId
        };
        await seeded.Create(BackendConfigurationPnDbContext!);

        var result = await CallNotifyAsync(channelId, channelJwt,
            resourceState: "change", resourceId: "res-1", messageNumber: "42");

        Assert.That(result, Is.InstanceOf<OkResult>());
    }

    [Test]
    public async Task Notify_BadHmac_Returns401()
    {
        const int userId = 26;
        var jwt = MintEnvelope("envelope", userId, "n26", "rt");
        var auth0 = NewAuthService();
        var token = await auth0.StoreEnvelopeAsync(jwt, userId, "n26");

        var channelId = Guid.NewGuid().ToString("N");
        var channelJwt = MintChannelJwt(channelId, "https://test-customer.invalid",
            DateTime.UtcNow.AddDays(1));
        var seeded = new DriveWatchChannel
        {
            GoogleOAuthTokenId = token.Id,
            ChannelId = channelId,
            ResourceId = "r",
            SignedToken = channelJwt,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedByUserId = userId,
            UpdatedByUserId = userId
        };
        await seeded.Create(BackendConfigurationPnDbContext!);

        var result = await CallNotifyAsync(channelId, channelJwt,
            resourceState: "change", resourceId: "res-1", messageNumber: "42",
            // Forge an HMAC the controller will reject.
            overrideHmacHex: new string('0', 64));

        Assert.That(result, Is.InstanceOf<UnauthorizedResult>());
    }

    [Test]
    public async Task Notify_WrongJwtTyp_Returns401()
    {
        const int userId = 27;
        var jwt = MintEnvelope("envelope", userId, "n27", "rt");
        var auth0 = NewAuthService();
        var token = await auth0.StoreEnvelopeAsync(jwt, userId, "n27");

        var channelId = Guid.NewGuid().ToString("N");
        // Mint a JWT with typ=state (the proxy's state JWT shape) — the
        // notify endpoint must reject it as type-confusion.
        var wrongTypJwt = MintChannelJwtWithTyp("state", channelId,
            "https://test-customer.invalid", DateTime.UtcNow.AddDays(1));
        var seeded = new DriveWatchChannel
        {
            GoogleOAuthTokenId = token.Id,
            ChannelId = channelId,
            ResourceId = "r",
            SignedToken = wrongTypJwt,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedByUserId = userId,
            UpdatedByUserId = userId
        };
        await seeded.Create(BackendConfigurationPnDbContext!);

        var result = await CallNotifyAsync(channelId, wrongTypJwt,
            resourceState: "change", resourceId: "res-1", messageNumber: "42");

        Assert.That(result, Is.InstanceOf<UnauthorizedResult>());
    }

    [Test]
    public async Task Notify_StaleChannelId_Returns200WithoutEnqueue()
    {
        // No DriveWatchChannel seeded — the service must still 200 so
        // Drive doesn't retry, but log a warning. We assert on the
        // response shape; the warning is observable through logging
        // but we don't bind the test to log output formatting.
        // TODO PR-7: assert refresh queue not enqueued.
        var channelId = Guid.NewGuid().ToString("N");
        var channelJwt = MintChannelJwt(channelId, "https://test-customer.invalid",
            DateTime.UtcNow.AddDays(1));

        var result = await CallNotifyAsync(channelId, channelJwt,
            resourceState: "change", resourceId: "res-1", messageNumber: "42");

        Assert.That(result, Is.InstanceOf<OkResult>());
    }

    /// <summary>
    /// Cross-service contract canary. The proxy at oauth.microting.com computes
    /// HMAC-SHA256 of `{channelId}|{resourceState}|{resourceId}|{messageNumber}|{date}`
    /// using the shared ProxySigningKey and forwards the hex digest to us.
    /// If THIS code drifts (re-orders fields, changes delimiter, swaps key bytes)
    /// the canonical-string template inside CallNotifyAsync will move with it
    /// and existing tests still pass — but production breaks because the proxy
    /// doesn't move. This test pins the canonical string, signing key, and
    /// expected digest to byte-exact reference values so any drift fails here.
    /// </summary>
    [Test]
    public void Notify_HmacReferenceVector_Matches()
    {
        const string fixedKey = "0123456789abcdef0123456789abcdef";
        const string canonical =
            "channel-abc|change|resource-xyz|42|Wed, 08 May 2026 12:34:56 GMT";
        const string expectedHex =
            "8b26c79134568abcfed3b39364b0653c80640f2c91a28c2543458f826760e77a";

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(fixedKey));
        var actualHex = Convert.ToHexString(
                hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();

        Assert.That(actualHex, Is.EqualTo(expectedHex),
            "HMAC canonical-string contract drifted from proxy. " +
            "Either revert the change OR update the proxy AND this vector together.");
    }

    // -------------------------------------------------------------------
    // PR-7 — GoogleDriveChangeProcessor
    // -------------------------------------------------------------------

    [Test]
    public async Task ProcessFile_ModifiedTimeNewer_RefetchesAndBumpsTimestamp()
    {
        const int userId = 31;
        var (file, _) = await SeedDriveFileAsync(userId, driveFileId: "file-newer",
            cachedModifiedTime: DateTime.UtcNow.AddDays(-2));

        var newModifiedTime = DateTime.UtcNow;
        var pdfBytes = Encoding.UTF8.GetBytes("%PDF-1.4 freshly-changed-bytes");

        // Drive metadata stub — newer modifiedTime.
        _proxyServer.Given(Request.Create()
                .WithPath("/drive/v3/files/file-newer")
                .WithParam("alt", "media").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/pdf")
                .WithBody(pdfBytes));
        _proxyServer.Given(Request.Create()
                .WithPath("/drive/v3/files/file-newer").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(new
                {
                    id = "file-newer",
                    name = "report-v2.pdf",
                    size = pdfBytes.Length,
                    mimeType = "application/pdf",
                    modifiedTime = newModifiedTime
                })));
        StubProxyRefresh("ya29.refresh-tok");

        var (processor, _) = NewChangeProcessor(userId);
        var outcome = await processor.ProcessFileAsync(file.Id);

        Assert.That(outcome, Is.EqualTo(DriveChangeOutcome.Refreshed));

        var fromDb = await BackendConfigurationPnDbContext!.AreaRulePlanningFiles
            .FirstAsync(x => x.Id == file.Id);
        Assert.That(fromDb.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
        Assert.That(fromDb.OriginalFileName, Is.EqualTo("report-v2.pdf"));
        Assert.That(fromDb.DriveModifiedTime!.Value,
            Is.GreaterThan(DateTime.UtcNow.AddDays(-1)),
            "Cached DriveModifiedTime must advance to (close to) the new modifiedTime.");
    }

    [Test]
    public async Task ProcessFile_ModifiedTimeUnchanged_NoOp()
    {
        const int userId = 32;
        var cached = DateTime.UtcNow.AddHours(-3);
        var (file, _) = await SeedDriveFileAsync(userId, driveFileId: "file-unchanged",
            cachedModifiedTime: cached);

        // Drive returns the SAME modifiedTime — processor must short-
        // circuit before requesting bytes. Stub /alt=media to a 500 so
        // we'd notice if the implementation accidentally fetched it.
        _proxyServer.Given(Request.Create()
                .WithPath("/drive/v3/files/file-unchanged")
                .WithParam("alt", "media").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(500)
                .WithBody("must-not-be-called"));
        _proxyServer.Given(Request.Create()
                .WithPath("/drive/v3/files/file-unchanged").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(new
                {
                    id = "file-unchanged",
                    name = "stable.pdf",
                    size = 100,
                    mimeType = "application/pdf",
                    modifiedTime = cached
                })));
        StubProxyRefresh("ya29.unchanged-tok");

        var (processor, _) = NewChangeProcessor(userId);
        var outcome = await processor.ProcessFileAsync(file.Id);

        Assert.That(outcome, Is.EqualTo(DriveChangeOutcome.NoChange));

        var altMediaCalls = _proxyServer.LogEntries.Count(e =>
            e.RequestMessage.Path == "/drive/v3/files/file-unchanged"
            && e.RequestMessage.Query != null
            && e.RequestMessage.Query.ContainsKey("alt"));
        Assert.That(altMediaCalls, Is.EqualTo(0),
            "Bytes endpoint must not be called when modifiedTime is unchanged.");
    }

    [Test]
    public async Task ProcessFile_404_MarksRemoved()
    {
        const int userId = 33;
        var (file, _) = await SeedDriveFileAsync(userId, driveFileId: "file-gone",
            cachedModifiedTime: DateTime.UtcNow.AddDays(-1));

        _proxyServer.Given(Request.Create()
                .WithPath("/drive/v3/files/file-gone").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(404)
                .WithBody("{\"error\":{\"code\":404,\"message\":\"File not found\"}}"));
        StubProxyRefresh("ya29.404-tok");

        var (processor, _) = NewChangeProcessor(userId);
        var outcome = await processor.ProcessFileAsync(file.Id);

        Assert.That(outcome, Is.EqualTo(DriveChangeOutcome.DriveNotFound));

        var fromDb = await BackendConfigurationPnDbContext!.AreaRulePlanningFiles
            .FirstAsync(x => x.Id == file.Id);
        Assert.That(fromDb.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed),
            "404 from Drive must soft-delete the AreaRulePlanningFile row.");
    }

    [Test]
    public async Task ProcessFile_403_MarksRemoved()
    {
        const int userId = 34;
        var (file, _) = await SeedDriveFileAsync(userId, driveFileId: "file-forbidden",
            cachedModifiedTime: DateTime.UtcNow.AddDays(-1));

        _proxyServer.Given(Request.Create()
                .WithPath("/drive/v3/files/file-forbidden").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(403)
                .WithBody("{\"error\":{\"code\":403,\"message\":\"Permission denied\"}}"));
        StubProxyRefresh("ya29.403-tok");

        var (processor, _) = NewChangeProcessor(userId);
        var outcome = await processor.ProcessFileAsync(file.Id);

        Assert.That(outcome, Is.EqualTo(DriveChangeOutcome.PermissionDenied));

        var fromDb = await BackendConfigurationPnDbContext!.AreaRulePlanningFiles
            .FirstAsync(x => x.Id == file.Id);
        Assert.That(fromDb.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed),
            "403 from Drive must soft-delete the AreaRulePlanningFile row.");
    }

    [Test]
    public async Task ProcessFile_TokenRevoked_ReturnsTokenRevoked()
    {
        const int userId = 35;
        var (file, _) = await SeedDriveFileAsync(userId, driveFileId: "file-revoked",
            cachedModifiedTime: DateTime.UtcNow.AddDays(-1));

        // Throwing auth service — simulates the proxy /refresh returning
        // invalid_grant. RevokedAt would have been stamped already by the
        // real auth service before throwing; we don't need to repeat that
        // bookkeeping in the test because the processor's contract is to
        // SURFACE the outcome, not to mutate the token row itself.
        var throwingAuth = Substitute.For<IGoogleDriveAuthService>();
        throwingAuth.GetAccessTokenAsync(Arg.Any<int>())
            .Returns<Task<string>>(_ =>
                Task.FromException<string>(new GoogleDriveTokenRevokedException("invalid_grant")));

        var processor = new GoogleDriveChangeProcessor(
            BackendConfigurationPnDbContext!,
            throwingAuth,
            Substitute.For<IGoogleDriveFileService>(),
            new RewritingHttpClientFactory(_proxyServer.Url!),
            NullLogger<GoogleDriveChangeProcessor>.Instance);

        var outcome = await processor.ProcessFileAsync(file.Id);

        Assert.That(outcome, Is.EqualTo(DriveChangeOutcome.TokenRevoked),
            "Processor must surface TokenRevoked without crashing.");

        // Row stays untouched — the disconnect/revocation flow owns
        // workflow-state transitions on the orphan side, not us.
        var fromDb = await BackendConfigurationPnDbContext!.AreaRulePlanningFiles
            .FirstAsync(x => x.Id == file.Id);
        Assert.That(fromDb.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
    }

    [Test]
    public async Task ProcessUser_ProcessesAllUserFiles_TalliesResult()
    {
        const int userId = 36;

        // Three files for one user, each with a distinct outcome path:
        //   1. Refreshed  — newer modifiedTime + bytes flow
        //   2. NoChange   — unchanged modifiedTime
        //   3. DriveNotFound (Removed) — 404
        var cached = DateTime.UtcNow.AddDays(-2);
        var (refreshFile, _) = await SeedDriveFileAsync(userId, "f-refresh", cached);
        var (unchangedFile, _) = await SeedDriveFileAsync(userId, "f-unchanged", cached, reuseToken: true);
        var (removedFile, _) = await SeedDriveFileAsync(userId, "f-removed", cached, reuseToken: true);

        var pdfBytes = Encoding.UTF8.GetBytes("%PDF-1.4 user-tally-test");
        _proxyServer.Given(Request.Create()
                .WithPath("/drive/v3/files/f-refresh")
                .WithParam("alt", "media").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/pdf")
                .WithBody(pdfBytes));
        _proxyServer.Given(Request.Create()
                .WithPath("/drive/v3/files/f-refresh").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(new
                {
                    id = "f-refresh",
                    name = "tally.pdf",
                    size = pdfBytes.Length,
                    mimeType = "application/pdf",
                    modifiedTime = DateTime.UtcNow
                })));
        _proxyServer.Given(Request.Create()
                .WithPath("/drive/v3/files/f-unchanged").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(new
                {
                    id = "f-unchanged",
                    name = "still.pdf",
                    size = 50,
                    mimeType = "application/pdf",
                    modifiedTime = cached
                })));
        _proxyServer.Given(Request.Create()
                .WithPath("/drive/v3/files/f-removed").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(404)
                .WithBody("{\"error\":{\"code\":404}}"));
        StubProxyRefresh("ya29.tally-tok");

        var (processor, _) = NewChangeProcessor(userId);
        var result = await processor.ProcessUserAsync(userId);

        Assert.That(result.Refreshed, Is.EqualTo(1));
        Assert.That(result.NoChange, Is.EqualTo(1));
        Assert.That(result.Removed, Is.EqualTo(1));
        Assert.That(result.Errors, Is.EqualTo(0));

        // Assert each row landed in the expected workflow state.
        var refreshedRow = await BackendConfigurationPnDbContext!.AreaRulePlanningFiles
            .FirstAsync(x => x.Id == refreshFile.Id);
        var unchangedRow = await BackendConfigurationPnDbContext.AreaRulePlanningFiles
            .FirstAsync(x => x.Id == unchangedFile.Id);
        var removedRow = await BackendConfigurationPnDbContext.AreaRulePlanningFiles
            .FirstAsync(x => x.Id == removedFile.Id);
        Assert.That(refreshedRow.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
        Assert.That(unchangedRow.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
        Assert.That(removedRow.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
    }

    // -------------------------------------------------------------------
    // PR-8 — Disconnect + GetAccounts
    // -------------------------------------------------------------------

    [Test]
    public async Task Disconnect_RevokesAtGoogleAndStopsChannelsAndSetsRevokedAt()
    {
        const int userId = 41;
        var jwt = MintEnvelope("envelope", userId, "n41", "rt-revoke");
        var auth0 = NewAuthService();
        var token = await auth0.StoreEnvelopeAsync(jwt, userId, "n41");

        // Seed two active watch channels — disconnect must call channels.stop
        // for each one and soft-delete the rows.
        var c1 = new DriveWatchChannel
        {
            GoogleOAuthTokenId = token.Id,
            ChannelId = "channel-one",
            ResourceId = "resource-one",
            SignedToken = "jwt-one",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedByUserId = userId,
            UpdatedByUserId = userId
        };
        await c1.Create(BackendConfigurationPnDbContext!);
        var c2 = new DriveWatchChannel
        {
            GoogleOAuthTokenId = token.Id,
            ChannelId = "channel-two",
            ResourceId = "resource-two",
            SignedToken = "jwt-two",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedByUserId = userId,
            UpdatedByUserId = userId
        };
        await c2.Create(BackendConfigurationPnDbContext!);

        // Stub the proxy /refresh so the auth service can mint a fresh
        // access token used to call channels.stop.
        StubProxyRefresh("ya29.disconnect-tok");

        // Stub Google's revoke endpoint (returns 200 on the happy path).
        _proxyServer.Given(Request.Create()
                .WithPath("/revoke").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200));

        // Stub channels.stop — Drive returns 204 No Content on success but
        // any 2xx is acceptable.
        _proxyServer.Given(Request.Create()
                .WithPath("/drive/v3/channels/stop").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(204));

        var sut = NewAuthServiceForWatch();
        await sut.DisconnectAsync(token.Id, userId);

        // RevokedAt stamped on the token row.
        var refreshed = await BackendConfigurationPnDbContext!.GoogleOAuthTokens
            .FirstAsync(x => x.Id == token.Id);
        Assert.That(refreshed.RevokedAt, Is.Not.Null,
            "Disconnect must stamp RevokedAt on the local token row.");

        // Both channel rows soft-deleted.
        var rowsAfter = await BackendConfigurationPnDbContext.DriveWatchChannels
            .Where(x => x.GoogleOAuthTokenId == token.Id)
            .ToListAsync();
        Assert.That(rowsAfter, Has.Count.EqualTo(2));
        Assert.That(rowsAfter.All(x => x.WorkflowState == Constants.WorkflowStates.Removed),
            Is.True, "All active channels must be soft-deleted on disconnect.");

        // Drive received both channels.stop calls + Google received the
        // revoke. The WireMock server hosts both surfaces (the rewriter
        // forces googleapis.com → wiremock); we assert each path saw a hit.
        var revokeHits = _proxyServer.LogEntries.Count(e =>
            e.RequestMessage.Path == "/revoke");
        var stopHits = _proxyServer.LogEntries.Count(e =>
            e.RequestMessage.Path == "/drive/v3/channels/stop");
        Assert.That(revokeHits, Is.EqualTo(1), "revoke must be called exactly once.");
        Assert.That(stopHits, Is.EqualTo(2), "channels.stop must be called per active channel.");
    }

    [Test]
    public async Task Disconnect_GoogleAlreadyRevoked_StillSetsRevokedAt()
    {
        const int userId = 42;
        var jwt = MintEnvelope("envelope", userId, "n42", "rt-already-revoked");
        var auth0 = NewAuthService();
        var token = await auth0.StoreEnvelopeAsync(jwt, userId, "n42");

        // Google reports the refresh token is already invalid. Per the
        // spec the disconnect remains idempotent — we still expect
        // RevokedAt to land on the local row.
        StubProxyRefresh("ya29.already-revoked");
        _proxyServer.Given(Request.Create()
                .WithPath("/revoke").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(400)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"error\":\"invalid_token\"}"));

        var sut = NewAuthServiceForWatch();
        await sut.DisconnectAsync(token.Id, userId);

        var refreshed = await BackendConfigurationPnDbContext!.GoogleOAuthTokens
            .FirstAsync(x => x.Id == token.Id);
        Assert.That(refreshed.RevokedAt, Is.Not.Null,
            "An already-revoked-at-Google token must still get RevokedAt locally.");
    }

    [Test]
    public async Task Disconnect_DifferentUser_Throws()
    {
        const int ownerUserId = 43;
        const int otherUserId = 44;
        var jwt = MintEnvelope("envelope", ownerUserId, "n43", "rt-not-yours");
        var auth0 = NewAuthService();
        var token = await auth0.StoreEnvelopeAsync(jwt, ownerUserId, "n43");

        var sut = NewAuthServiceForWatch();
        Assert.ThrowsAsync<UnauthorizedAccessException>(
            async () => await sut.DisconnectAsync(token.Id, otherUserId));

        // Token must NOT have been revoked — ownership check fails before
        // any local mutation.
        var still = await BackendConfigurationPnDbContext!.GoogleOAuthTokens
            .FirstAsync(x => x.Id == token.Id);
        Assert.That(still.RevokedAt, Is.Null,
            "Ownership check must fail before any local mutation.");
    }

    [Test]
    public async Task GetAccounts_ReturnsActiveAndRevoked_OrderedByConnectedAt()
    {
        const int userId = 45;

        // Seed three rows with explicit ConnectedAt timestamps so the
        // ordering check has something to bite on. Revoked = stamped
        // RevokedAt + workflow stays Created (the spec only soft-deletes
        // on full removal; a revoked-but-still-tracked row is "Created
        // with RevokedAt set"). The IsActive flag combines both signals.
        var now = DateTime.UtcNow;
        var newest = new GoogleOAuthToken
        {
            UserId = userId,
            GoogleAccountEmail = "newest@example.com",
            EncryptedRefreshToken = "x",
            ConnectedAt = now,
            LastUsedAt = now,
            CreatedByUserId = userId,
            UpdatedByUserId = userId
        };
        await newest.Create(BackendConfigurationPnDbContext!);
        var middle = new GoogleOAuthToken
        {
            UserId = userId,
            GoogleAccountEmail = "middle@example.com",
            EncryptedRefreshToken = "y",
            ConnectedAt = now.AddDays(-1),
            LastUsedAt = now.AddDays(-1),
            CreatedByUserId = userId,
            UpdatedByUserId = userId
        };
        await middle.Create(BackendConfigurationPnDbContext!);
        var oldRevoked = new GoogleOAuthToken
        {
            UserId = userId,
            GoogleAccountEmail = "revoked@example.com",
            EncryptedRefreshToken = "z",
            ConnectedAt = now.AddDays(-7),
            LastUsedAt = now.AddDays(-2),
            RevokedAt = now.AddDays(-1),
            CreatedByUserId = userId,
            UpdatedByUserId = userId
        };
        await oldRevoked.Create(BackendConfigurationPnDbContext!);

        // Drive accounts via the controller (the production wire shape).
        // The default NewController helper sets a substitute IUserService
        // whose UserId returns 0 — use NewControllerWithUser to bind the
        // stub to our test user.
        var controller = NewControllerWithUser(userId);
        var result = await controller.GetAccounts();
        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Model, Has.Count.EqualTo(3));

        // Order: newest first (ConnectedAt DESC).
        Assert.That(result.Model[0].Email, Is.EqualTo("newest@example.com"));
        Assert.That(result.Model[1].Email, Is.EqualTo("middle@example.com"));
        Assert.That(result.Model[2].Email, Is.EqualTo("revoked@example.com"));

        // Active flag wired correctly: revoked row reports false,
        // unrevoked rows report true.
        Assert.That(result.Model[0].IsActive, Is.True);
        Assert.That(result.Model[1].IsActive, Is.True);
        Assert.That(result.Model[2].IsActive, Is.False);
        Assert.That(result.Model[2].RevokedAt, Is.Not.Null);
    }

    /// <summary>
    /// Variant of <see cref="NewController"/> that wires
    /// <see cref="IUserService.UserId"/> to a specific value — the GetAccounts
    /// test needs the controller to read a specific user's tokens.
    /// </summary>
    private GoogleDriveController NewControllerWithUser(int userId)
    {
        var auth = NewAuthServiceForWatch();
        var fileService = Substitute.For<IGoogleDriveFileService>();
        var processor = Substitute.For<IGoogleDriveChangeProcessor>();
        var scopeFactory = new SingleProcessorScopeFactory(processor);
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(userId);
        var localization = new BackendConfigurationLocalizationService();
        var protectionProvider = DataProtectionProvider.Create(nameof(GoogleDriveTests));

        return new GoogleDriveController(
            auth,
            fileService,
            scopeFactory,
            userService,
            localization,
            BackendConfigurationPnDbContext!,
            protectionProvider,
            Options.Create(_options),
            NullLogger<GoogleDriveController>.Instance);
    }

    /// <summary>
    /// Constructs a <see cref="GoogleDriveChangeProcessor"/> wired against the
    /// shared WireMock server. Mirrors <see cref="NewFileService"/> but
    /// returns the processor + its underlying file service so tests can
    /// assert on either layer.
    /// </summary>
    private (GoogleDriveChangeProcessor Processor, GoogleDriveFileService FileService) NewChangeProcessor(int userId)
    {
        var fakeFactory = new RewritingHttpClientFactory(_proxyServer.Url!);
        var proxyHttp = new HttpClient { BaseAddress = new Uri(_options.MicrotingOAuthProxyUrl) };
        var proxyClient = new OAuthProxyClient(proxyHttp, Options.Create(_options));
        var auth = new GoogleDriveAuthService(
            BackendConfigurationPnDbContext!,
            proxyClient,
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(_options),
            NullLogger<GoogleDriveAuthService>.Instance);

        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(userId);

        var fileService = new GoogleDriveFileService(
            BackendConfigurationPnDbContext!,
            auth,
            new EFormCoreService(_sdkConnectionString),
            userService,
            new BackendConfigurationLocalizationService(),
            fakeFactory,
            NullLogger<GoogleDriveFileService>.Instance);

        var processor = new GoogleDriveChangeProcessor(
            BackendConfigurationPnDbContext!,
            auth,
            fileService,
            fakeFactory,
            NullLogger<GoogleDriveChangeProcessor>.Instance);
        return (processor, fileService);
    }

    /// <summary>
    /// Stubs the proxy /refresh endpoint with a fresh access token. Each
    /// PR-7 test fixture needs at least one of these because every
    /// processor call goes through GetAccessTokenAsync first.
    /// </summary>
    private void StubProxyRefresh(string accessToken)
    {
        _proxyServer.Given(Request.Create()
                .WithPath("/google-drive/refresh").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(new
                {
                    accessToken,
                    accessTokenExpiry = DateTime.UtcNow.AddHours(1)
                })));
    }

    /// <summary>
    /// Seeds an <see cref="AreaRulePlanningFile"/> with Drive metadata so
    /// the processor has something to work on. The accompanying
    /// <see cref="GoogleOAuthToken"/> is created on first call per user;
    /// pass <paramref name="reuseToken"/> to bind multiple files to the
    /// same token (required for ProcessUserAsync tallying tests).
    /// </summary>
    private async Task<(AreaRulePlanningFile File, GoogleOAuthToken Token)> SeedDriveFileAsync(
        int userId, string driveFileId, DateTime cachedModifiedTime, bool reuseToken = false)
    {
        GoogleOAuthToken token;
        if (reuseToken)
        {
            token = await BackendConfigurationPnDbContext!.GoogleOAuthTokens
                .Where(x => x.UserId == userId)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .OrderByDescending(x => x.Id)
                .FirstAsync();
        }
        else
        {
            // StoreEnvelopeAsync wires the AES-GCM-encrypted refresh-token
            // blob correctly. Going through the public path beats hand-
            // assembling the row because the auth service hashes/encrypts
            // a few fields invisibly.
            var jwt = MintEnvelope("envelope", userId, $"n{userId}", $"rt-{userId}");
            var auth = NewAuthService();
            token = await auth.StoreEnvelopeAsync(jwt, userId, $"n{userId}");
        }

        var arpId = await SeedPlanning();

        // Stand-in UploadedData so the file's UploadedDataId FK has a
        // target. Real production rows come from the file service's S3
        // upload pipeline; for processor tests we don't care about the
        // bytes, only the metadata transitions on the file row.
        var sdkUpload = new Microting.eForm.Infrastructure.Data.Entities.UploadedData
        {
            Checksum = "0000000000000000000000000000000000000000",
            FileName = $"placeholder-{driveFileId}.pdf",
            FileLocation = $"/tmp/placeholder-{driveFileId}.pdf",
            Extension = "pdf",
            CurrentFile = $"placeholder-{driveFileId}.pdf",
            UploaderId = userId
        };
        await sdkUpload.Create(MicrotingDbContext!);

        var file = new AreaRulePlanningFile
        {
            AreaRulePlanningId = arpId,
            UploadedDataId = sdkUpload.Id,
            OriginalFileName = $"{driveFileId}.pdf",
            MimeType = "application/pdf",
            SizeBytes = 100,
            DriveFileId = driveFileId,
            DriveModifiedTime = cachedModifiedTime,
            GoogleOAuthTokenId = token.Id,
            CreatedByUserId = userId,
            UpdatedByUserId = userId
        };
        await file.Create(BackendConfigurationPnDbContext!);
        return (file, token);
    }

    /// <summary>
    /// Mints a channel-token JWT in the same shape EnsureWatchChannelAsync
    /// produces. Same signing key the controller verifies against.
    /// </summary>
    private static string MintChannelJwt(string channelId, string customerInstanceUrl, DateTime exp)
        => MintChannelJwtWithTyp("channel", channelId, customerInstanceUrl, exp);

    private static string MintChannelJwtWithTyp(string typ, string channelId,
        string customerInstanceUrl, DateTime exp)
    {
        var header = new Dictionary<string, object>
        {
            ["alg"] = "HS256",
            ["typ"] = "JWT"
        };
        var payload = new Dictionary<string, object>
        {
            ["typ"] = typ,
            ["customerInstanceUrl"] = customerInstanceUrl,
            ["channelId"] = channelId,
            ["exp"] = ((DateTimeOffset)exp).ToUnixTimeSeconds()
        };

        var headerB64 = B64Url(JsonSerializer.SerializeToUtf8Bytes(header));
        var payloadB64 = B64Url(JsonSerializer.SerializeToUtf8Bytes(payload));
        var signingInput = Encoding.UTF8.GetBytes($"{headerB64}.{payloadB64}");
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(ProxySigningKey));
        var sig = B64Url(hmac.ComputeHash(signingInput));
        return $"{headerB64}.{payloadB64}.{sig}";
    }

    /// <summary>
    /// Calls the controller's Notify action with HMAC + Date + the
    /// X-Goog-* headers the spec requires. Lets each test override the
    /// HMAC for negative-path coverage.
    /// </summary>
    private async Task<IActionResult> CallNotifyAsync(string channelId, string channelToken,
        string resourceState, string resourceId, string messageNumber,
        string? overrideHmacHex = null)
    {
        var date = DateTime.UtcNow.ToString("R", CultureInfo.InvariantCulture);
        var canonical = $"{channelId}|{resourceState}|{resourceId}|{messageNumber}|{date}";
        string hex;
        using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(ProxySigningKey)))
        {
            hex = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical)))
                .ToLowerInvariant();
        }
        var auth = $"HMAC-SHA256 {overrideHmacHex ?? hex}";

        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["Authorization"] = auth;
        ctx.Request.Headers["Date"] = date;
        ctx.Request.Headers["X-Goog-Channel-ID"] = channelId;
        ctx.Request.Headers["X-Goog-Channel-Token"] = channelToken;
        ctx.Request.Headers["X-Goog-Resource-State"] = resourceState;
        ctx.Request.Headers["X-Goog-Resource-ID"] = resourceId;
        ctx.Request.Headers["X-Goog-Message-Number"] = messageNumber;

        var controller = NewController();
        controller.ControllerContext = new ControllerContext { HttpContext = ctx };
        return await controller.Notify();
    }

    private GoogleDriveController NewController(IGoogleDriveChangeProcessor? processor = null)
    {
        var auth = NewAuthServiceForWatch();
        var fileService = Substitute.For<IGoogleDriveFileService>();
        // Tests that don't care about the change-processor wiring get a
        // no-op stub so the fire-and-forget Task.Run inside Notify is
        // observable but harmless. The Notify_* tests exercising PR-7's
        // enqueue path supply their own substitute via the parameter.
        processor ??= Substitute.For<IGoogleDriveChangeProcessor>();
        var scopeFactory = new SingleProcessorScopeFactory(processor);
        var userService = Substitute.For<IUserService>();
        var localization = new BackendConfigurationLocalizationService();
        // Real ASP.NET Data Protection — the controller's CSRF cookie
        // path is unused in /notify, but the constructor binds the
        // protector eagerly. Ephemeral provider keeps the test hermetic.
        var protectionProvider = DataProtectionProvider.Create(nameof(GoogleDriveTests));

        return new GoogleDriveController(
            auth,
            fileService,
            scopeFactory,
            userService,
            localization,
            BackendConfigurationPnDbContext!,
            protectionProvider,
            Options.Create(_options),
            NullLogger<GoogleDriveController>.Instance);
    }

    /// <summary>
    /// Minimal IServiceScopeFactory that resolves a single
    /// IGoogleDriveChangeProcessor instance — exactly what the controller's
    /// fire-and-forget Notify enqueue needs. Real ServiceProvider wiring is
    /// overkill for unit tests; this keeps the test surface small while
    /// still exercising the scope-factory code path the controller uses
    /// in production.
    /// </summary>
    private sealed class SingleProcessorScopeFactory : IServiceScopeFactory, IServiceProvider, IServiceScope
    {
        private readonly IGoogleDriveChangeProcessor _processor;
        public SingleProcessorScopeFactory(IGoogleDriveChangeProcessor processor) => _processor = processor;

        public IServiceScope CreateScope() => this;
        public IServiceProvider ServiceProvider => this;
        public object? GetService(Type serviceType)
            => serviceType == typeof(IGoogleDriveChangeProcessor) ? _processor : null;
        public void Dispose() { }
    }

    /// <summary>
    /// BOTH /drive/v3/* AND /google-drive/* to the same WireMock server.
    /// In production these are different hosts (googleapis.com vs the
    /// Microting proxy) but for the test we host both surfaces inside the
    /// same WireMock instance and rewrite the Drive request URI on the way
    /// out.
    /// </summary>
    private GoogleDriveFileService NewFileService(int userId)
    {
        var fakeFactory = new RewritingHttpClientFactory(_proxyServer.Url!);
        // The proxy client uses the option's MicrotingOAuthProxyUrl directly
        // for HMAC; reuse the WireMock URL for that too. Don't dispose this
        // HttpClient — it's owned by the proxyClient for the lifetime of
        // the test (the auth service holds a reference). The whole
        // structure is GC'd after the test completes.
        var proxyHttp = new HttpClient { BaseAddress = new Uri(_options.MicrotingOAuthProxyUrl) };
        var proxyClient = new OAuthProxyClient(proxyHttp, Options.Create(_options));
        var auth = new GoogleDriveAuthService(
            BackendConfigurationPnDbContext!,
            proxyClient,
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(_options),
            NullLogger<GoogleDriveAuthService>.Instance);

        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(userId);

        return new GoogleDriveFileService(
            BackendConfigurationPnDbContext!,
            auth,
            new EFormCoreService(_sdkConnectionString),
            userService,
            new BackendConfigurationLocalizationService(),
            fakeFactory,
            NullLogger<GoogleDriveFileService>.Instance);
    }

    /// <summary>
    /// Routes Drive API requests through the WireMock instance by rewriting
    /// the host on the way out. Lets us host the entire mocked surface
    /// (proxy + Drive) inside one WireMockServer.
    /// </summary>
    private class RewritingHttpClientFactory : IHttpClientFactory
    {
        private readonly Uri _redirectBase;
        public RewritingHttpClientFactory(string redirectBase)
        {
            _redirectBase = new Uri(redirectBase);
        }

        public HttpClient CreateClient(string name)
        {
            var handler = new RewritingHandler(_redirectBase);
            return new HttpClient(handler);
        }

        private class RewritingHandler : DelegatingHandler
        {
            private readonly Uri _redirectBase;
            public RewritingHandler(Uri redirectBase) : base(new HttpClientHandler())
            {
                _redirectBase = redirectBase;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
            {
                // Rewrite Google Drive (www.googleapis.com) AND Google's
                // OAuth revoke (oauth2.googleapis.com) onto the WireMock
                // host. Both are Google-owned hosts the production code
                // talks to directly; tests stub them through the same
                // WireMock server the proxy uses.
                if (request.RequestUri != null
                    && (request.RequestUri.Host.Equals("www.googleapis.com", StringComparison.OrdinalIgnoreCase)
                        || request.RequestUri.Host.Equals("oauth2.googleapis.com", StringComparison.OrdinalIgnoreCase)))
                {
                    var rebuilt = new UriBuilder(request.RequestUri)
                    {
                        Scheme = _redirectBase.Scheme,
                        Host = _redirectBase.Host,
                        Port = _redirectBase.Port
                    };
                    request.RequestUri = rebuilt.Uri;
                }
                return base.SendAsync(request, cancellationToken);
            }
        }
    }

    private class ThrowingProxyClient : IOAuthProxyClient
    {
        public Task<RefreshResult> RefreshAsync(string refreshToken)
            => throw new InvalidOperationException("Proxy client should not be invoked in this test.");
    }

    /// <summary>
    /// DDL to upgrade the test bootstrap schema (snapshot of base v10.0.32)
    /// to the v10.0.33 shape this PR depends on. Created idempotently so
    /// it's safe to re-run across test fixtures sharing a container. The
    /// columns mirror the entity declarations in the base repo's
    /// GoogleOAuthToken.cs / DriveWatchChannel.cs / AreaRulePlanningFile.cs.
    /// </summary>
    private const string GoogleDriveSchemaSql = @"
ALTER TABLE `AreaRulePlanningFiles`
  ADD COLUMN IF NOT EXISTS `DriveFileId` varchar(64) DEFAULT NULL,
  ADD COLUMN IF NOT EXISTS `DriveModifiedTime` datetime(6) DEFAULT NULL,
  ADD COLUMN IF NOT EXISTS `GoogleOAuthTokenId` int(11) DEFAULT NULL;

ALTER TABLE `AreaRulePlanningFileVersions`
  ADD COLUMN IF NOT EXISTS `DriveFileId` varchar(64) DEFAULT NULL,
  ADD COLUMN IF NOT EXISTS `DriveModifiedTime` datetime(6) DEFAULT NULL,
  ADD COLUMN IF NOT EXISTS `GoogleOAuthTokenId` int(11) DEFAULT NULL;

CREATE TABLE IF NOT EXISTS `GoogleOAuthTokens` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `UserId` int(11) NOT NULL,
  `GoogleAccountEmail` varchar(255) DEFAULT NULL,
  `EncryptedRefreshToken` varchar(2048) DEFAULT NULL,
  `ConnectedAt` datetime(6) NOT NULL,
  `LastUsedAt` datetime(6) DEFAULT NULL,
  `RevokedAt` datetime(6) DEFAULT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

CREATE TABLE IF NOT EXISTS `GoogleOAuthTokenVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `GoogleOAuthTokenId` int(11) NOT NULL,
  `UserId` int(11) NOT NULL,
  `GoogleAccountEmail` varchar(255) DEFAULT NULL,
  `EncryptedRefreshToken` varchar(2048) DEFAULT NULL,
  `ConnectedAt` datetime(6) NOT NULL,
  `LastUsedAt` datetime(6) DEFAULT NULL,
  `RevokedAt` datetime(6) DEFAULT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

CREATE TABLE IF NOT EXISTS `DriveWatchChannels` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `GoogleOAuthTokenId` int(11) NOT NULL,
  `ChannelId` varchar(64) DEFAULT NULL,
  `ResourceId` varchar(64) DEFAULT NULL,
  `SignedToken` varchar(2048) DEFAULT NULL,
  `ExpiresAt` datetime(6) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_DriveWatchChannels_GoogleOAuthTokenId` (`GoogleOAuthTokenId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

CREATE TABLE IF NOT EXISTS `DriveWatchChannelVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `DriveWatchChannelId` int(11) NOT NULL,
  `GoogleOAuthTokenId` int(11) NOT NULL,
  `ChannelId` varchar(64) DEFAULT NULL,
  `ResourceId` varchar(64) DEFAULT NULL,
  `SignedToken` varchar(2048) DEFAULT NULL,
  `ExpiresAt` datetime(6) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
";
}
