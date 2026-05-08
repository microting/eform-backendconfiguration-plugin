namespace BackendConfiguration.Pn.Services.GoogleDrive;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using BackendConfigurationLocalizationService;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using SdkUploadedData = Microting.eForm.Infrastructure.Data.Entities.UploadedData;
using IoFile = System.IO.File;

/// <summary>
/// Mirrors
/// <see cref="BackendConfigurationCalendarService.BackendConfigurationCalendarService.UploadFile"/>
/// but pulls bytes from Drive instead of a multipart form. The persistence
/// chain is identical: stage to a checksum-deterministic file under the
/// shared <c>calendar-attachments</c> tempdir, create a SDK
/// <see cref="SdkUploadedData"/> row, push to S3, then create a
/// <see cref="AreaRulePlanningFile"/> with <c>DriveFileId</c> populated so
/// the change-processor can later refetch.
///
/// MIME / size validation matches the form-data path: PDF + PNG + JPEG
/// only, ≤25 MB. Anything else is rejected before bytes hit disk.
/// </summary>
public class GoogleDriveFileService : IGoogleDriveFileService
{
    private const long MaxAttachmentBytes = 25L * 1024 * 1024;

    private static readonly Dictionary<string, string[]> AllowedMimeExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            { "application/pdf", new[] { ".pdf" } },
            { "image/png", new[] { ".png" } },
            { "image/jpeg", new[] { ".jpg", ".jpeg" } }
        };

    private readonly BackendConfigurationPnDbContext _dbContext;
    private readonly IGoogleDriveAuthService _authService;
    private readonly IEFormCoreService _coreHelper;
    private readonly IUserService _userService;
    private readonly IBackendConfigurationLocalizationService _localizationService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GoogleDriveFileService> _logger;

    public GoogleDriveFileService(
        BackendConfigurationPnDbContext dbContext,
        IGoogleDriveAuthService authService,
        IEFormCoreService coreHelper,
        IUserService userService,
        IBackendConfigurationLocalizationService localizationService,
        IHttpClientFactory httpClientFactory,
        ILogger<GoogleDriveFileService> logger)
    {
        _dbContext = dbContext;
        _authService = authService;
        _coreHelper = coreHelper;
        _userService = userService;
        _localizationService = localizationService;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<OperationDataResult<AreaRulePlanningFile>> DownloadAndCacheFileAsync(int userId, string driveFileId, int areaRulePlanningId)
    {
        try
        {
            // 1. Refuse work for plannings that don't exist (or are
            // soft-deleted). Keep the symmetric guard with the form-data
            // path: it guards against an attacker forging an
            // areaRulePlanningId for a planning that lives on another
            // tenant.
            var planning = await _dbContext.AreaRulePlannings
                .Where(x => x.Id == areaRulePlanningId)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .FirstOrDefaultAsync()
                .ConfigureAwait(false);
            if (planning == null)
            {
                return new OperationDataResult<AreaRulePlanningFile>(false,
                    _localizationService.GetString("AreaRulePlanningNotFound"));
            }

            // 2. Look up the user's active Google token (we need its Id
            // for the AreaRulePlanningFile FK).
            var oauthToken = await _dbContext.GoogleOAuthTokens
                .Where(x => x.UserId == userId)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.RevokedAt == null)
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync()
                .ConfigureAwait(false);
            if (oauthToken == null)
            {
                return new OperationDataResult<AreaRulePlanningFile>(false,
                    _localizationService.GetString("GoogleDriveConnectFailed"));
            }

            // 3. Get a fresh access token. May throw a typed exception when
            // the grant is gone — we let it bubble up to the controller so
            // the user sees the "reconnect" message.
            var accessToken = await _authService.GetAccessTokenAsync(userId).ConfigureAwait(false);

            using var http = _httpClientFactory.CreateClient(nameof(GoogleDriveFileService));
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", accessToken);

            // 4. Metadata first — cheap, lets us reject by mime/size before
            // pulling bytes.
            var metadata = await GetDriveMetadataAsync(http, driveFileId).ConfigureAwait(false);
            if (metadata == null)
            {
                return new OperationDataResult<AreaRulePlanningFile>(false,
                    _localizationService.GetString("DriveFileNotFound"));
            }

            var mime = (metadata.MimeType ?? string.Empty).Trim().ToLowerInvariant();
            if (!AllowedMimeExtensions.TryGetValue(mime, out var allowedExts))
            {
                return new OperationDataResult<AreaRulePlanningFile>(false,
                    _localizationService.GetString("DriveAttachmentMimeRejected"));
            }

            if (metadata.Size > MaxAttachmentBytes)
            {
                return new OperationDataResult<AreaRulePlanningFile>(false,
                    _localizationService.GetString("FileTooLarge"));
            }

            // 5. Stage + persist. We mirror the form-data path: stream into
            // an intermediate file, MD5 the canonical move target, hand it
            // to the SDK for upload to S3, then create the join row.
            var ext = ChooseExtension(allowedExts, metadata.Name);
            var folder = Path.Combine(Path.GetTempPath(), "calendar-attachments");
            Directory.CreateDirectory(folder);
            var intermediatePath = Path.Combine(folder, $"{DateTime.UtcNow.Ticks}_{Guid.NewGuid():N}{ext}");

            try
            {
                long downloadedBytes;
                using (var downloadResponse = await http.GetAsync(
                           $"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(driveFileId)}?alt=media",
                           HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                {
                    if (downloadResponse.StatusCode == HttpStatusCode.NotFound)
                    {
                        return new OperationDataResult<AreaRulePlanningFile>(false,
                            _localizationService.GetString("DriveFileNotFound"));
                    }
                    downloadResponse.EnsureSuccessStatusCode();

                    await using var src = await downloadResponse.Content
                        .ReadAsStreamAsync().ConfigureAwait(false);
                    await using var dst = new FileStream(intermediatePath, FileMode.Create);
                    await src.CopyToAsync(dst).ConfigureAwait(false);
                    downloadedBytes = dst.Length;
                }

                // Defence-in-depth: enforce the size cap on the actual
                // stream too. metadata.Size can lie (large generated docs).
                if (downloadedBytes > MaxAttachmentBytes)
                {
                    return new OperationDataResult<AreaRulePlanningFile>(false,
                        _localizationService.GetString("FileTooLarge"));
                }

                string checksum;
                using (var md5 = MD5.Create())
                {
                    await using var s = IoFile.OpenRead(intermediatePath);
                    var hash = await md5.ComputeHashAsync(s).ConfigureAwait(false);
                    checksum = BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
                }

                var storageFileName = $"{checksum}{ext}";
                var canonicalPath = Path.Combine(folder, storageFileName);
                if (IoFile.Exists(canonicalPath))
                {
                    IoFile.Delete(intermediatePath);
                }
                else
                {
                    IoFile.Move(intermediatePath, canonicalPath);
                }

                var core = await _coreHelper.GetCore().ConfigureAwait(false);
                var sdkDbContext = core.DbContextHelper.GetDbContext();

                var uploadedData = new SdkUploadedData
                {
                    Checksum = checksum,
                    FileName = storageFileName,
                    FileLocation = canonicalPath,
                    Extension = ext.TrimStart('.'),
                    CurrentFile = storageFileName,
                    UploaderId = _userService.UserId
                };
                await uploadedData.Create(sdkDbContext).ConfigureAwait(false);

                await core.PutFileToStorageSystem(canonicalPath, storageFileName).ConfigureAwait(false);

                var arpFile = new AreaRulePlanningFile
                {
                    AreaRulePlanningId = areaRulePlanningId,
                    UploadedDataId = uploadedData.Id,
                    OriginalFileName = metadata.Name ?? storageFileName,
                    MimeType = mime,
                    SizeBytes = downloadedBytes,
                    DriveFileId = driveFileId,
                    DriveModifiedTime = metadata.ModifiedTime,
                    GoogleOAuthTokenId = oauthToken.Id,
                    CreatedByUserId = _userService.UserId,
                    UpdatedByUserId = _userService.UserId
                };
                await arpFile.Create(_dbContext).ConfigureAwait(false);

                // PR-5 watch-channel registration. Only on the *first*
                // Drive-sourced attachment for this token — if any other
                // AreaRulePlanningFile already references this token a
                // channel exists already and any expiry-driven renewal is
                // owned by PR-6's daily cron. If the watch call fails we
                // log a Warning and let the file persist anyway: PR-6's
                // reconcile cron is the safety net.
                var hadEarlierDriveFile = await _dbContext.AreaRulePlanningFiles
                    .Where(f => f.Id != arpFile.Id)
                    .Where(f => f.GoogleOAuthTokenId == oauthToken.Id)
                    .Where(f => f.WorkflowState != Constants.WorkflowStates.Removed)
                    .AnyAsync()
                    .ConfigureAwait(false);
                if (!hadEarlierDriveFile)
                {
                    try
                    {
                        await _authService.EnsureWatchChannelAsync(userId).ConfigureAwait(false);
                    }
                    catch (Exception watchEx)
                    {
                        // Don't fail the attach. PR-6's daily reconcile
                        // cron will pick up missed changes; PR-6's daily
                        // renewal cron will keep the channel rolling once
                        // it's been seeded by a later attach.
                        _logger.LogWarning(watchEx,
                            "EnsureWatchChannelAsync failed for user {UserId} after first Drive attach; reconcile cron will recover",
                            userId);
                    }
                }

                return new OperationDataResult<AreaRulePlanningFile>(true, arpFile);
            }
            finally
            {
                try
                {
                    if (IoFile.Exists(intermediatePath)) IoFile.Delete(intermediatePath);
                }
                catch
                {
                    // Cleanup is best-effort.
                }
            }
        }
        catch (GoogleDriveTokenRevokedException)
        {
            // Let the controller wrap this in a localized message — we
            // don't try to swallow it here because the auth service has
            // already stamped RevokedAt on the row.
            throw;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GoogleDriveFileService.DownloadAndCacheFileAsync failed: {Message}", e.Message);
            return new OperationDataResult<AreaRulePlanningFile>(false, e.Message);
        }
    }

    public async Task<bool> RefreshFileAsync(AreaRulePlanningFile file)
    {
        if (string.IsNullOrEmpty(file.DriveFileId) || file.GoogleOAuthTokenId == null)
        {
            return false;
        }

        // Resolve the owning user via the FK row.
        var token = await _dbContext.GoogleOAuthTokens
            .FirstOrDefaultAsync(x => x.Id == file.GoogleOAuthTokenId.Value)
            .ConfigureAwait(false);
        if (token == null || token.WorkflowState == Constants.WorkflowStates.Removed || token.RevokedAt != null)
        {
            return false;
        }

        var accessToken = await _authService.GetAccessTokenAsync(token.UserId).ConfigureAwait(false);

        using var http = _httpClientFactory.CreateClient(nameof(GoogleDriveFileService));
        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

        // PR-7: separate 404 / 403 / other-error so the change processor
        // can map them to DriveChangeOutcome.DriveNotFound /
        // PermissionDenied / Error and stamp WorkflowState accordingly.
        // The file service stays a pure transport — it does NOT mutate
        // the AreaRulePlanningFile workflow-state itself; that's the
        // processor's job.
        var metadata = await GetDriveMetadataForRefreshAsync(http, file.DriveFileId).ConfigureAwait(false);
        if (metadata == null)
        {
            // Other transient/5xx error — surface as a "no change" no-op
            // to RefreshFileAsync's bool-returning legacy contract; the
            // processor sees Error from the explicit metadata fetch it
            // does up-front instead.
            return false;
        }

        if (metadata.ModifiedTime.HasValue && file.DriveModifiedTime.HasValue
            && metadata.ModifiedTime.Value <= file.DriveModifiedTime.Value)
        {
            // Not changed since last fetch.
            return false;
        }

        var mime = (metadata.MimeType ?? string.Empty).Trim().ToLowerInvariant();
        if (!AllowedMimeExtensions.TryGetValue(mime, out var allowedExts))
        {
            // The file's mime drifted to something we no longer accept —
            // treat that as a no-op refresh; the operator will need to
            // re-pick. (Same conservative posture as the form-data path.)
            return false;
        }

        var ext = ChooseExtension(allowedExts, metadata.Name);
        var folder = Path.Combine(Path.GetTempPath(), "calendar-attachments");
        Directory.CreateDirectory(folder);
        var intermediatePath = Path.Combine(folder, $"{DateTime.UtcNow.Ticks}_{Guid.NewGuid():N}{ext}");
        try
        {
            long downloadedBytes;
            using (var downloadResponse = await http.GetAsync(
                       $"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(file.DriveFileId)}?alt=media",
                       HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
            {
                if (!downloadResponse.IsSuccessStatusCode)
                {
                    return false;
                }

                await using var src = await downloadResponse.Content.ReadAsStreamAsync().ConfigureAwait(false);
                await using var dst = new FileStream(intermediatePath, FileMode.Create);
                await src.CopyToAsync(dst).ConfigureAwait(false);
                downloadedBytes = dst.Length;
            }

            if (downloadedBytes > MaxAttachmentBytes)
            {
                return false;
            }

            string checksum;
            using (var md5 = MD5.Create())
            {
                await using var s = IoFile.OpenRead(intermediatePath);
                var hash = await md5.ComputeHashAsync(s).ConfigureAwait(false);
                checksum = BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            }

            var storageFileName = $"{checksum}{ext}";
            var canonicalPath = Path.Combine(folder, storageFileName);
            if (IoFile.Exists(canonicalPath))
            {
                IoFile.Delete(intermediatePath);
            }
            else
            {
                IoFile.Move(intermediatePath, canonicalPath);
            }

            var core = await _coreHelper.GetCore().ConfigureAwait(false);
            var sdkDbContext = core.DbContextHelper.GetDbContext();

            // Create a NEW SDK UploadedData (we do NOT mutate the existing
            // one — preserves audit chain to the previous blob, matching
            // the existing platform pattern in
            // BackendConfigurationCalendarService).
            var uploadedData = new SdkUploadedData
            {
                Checksum = checksum,
                FileName = storageFileName,
                FileLocation = canonicalPath,
                Extension = ext.TrimStart('.'),
                CurrentFile = storageFileName,
                UploaderId = token.UserId
            };
            await uploadedData.Create(sdkDbContext).ConfigureAwait(false);

            await core.PutFileToStorageSystem(canonicalPath, storageFileName).ConfigureAwait(false);

            file.UploadedDataId = uploadedData.Id;
            file.OriginalFileName = metadata.Name ?? storageFileName;
            file.MimeType = mime;
            file.SizeBytes = downloadedBytes;
            file.DriveModifiedTime = metadata.ModifiedTime;
            file.UpdatedByUserId = token.UserId;
            await file.Update(_dbContext).ConfigureAwait(false);

            return true;
        }
        finally
        {
            try
            {
                if (IoFile.Exists(intermediatePath)) IoFile.Delete(intermediatePath);
            }
            catch
            {
                // best-effort
            }
        }
    }

    private static string ChooseExtension(string[] allowedExts, string? fileName)
    {
        if (!string.IsNullOrEmpty(fileName))
        {
            var fromName = Path.GetExtension(fileName).ToLowerInvariant();
            if (allowedExts.Contains(fromName)) return fromName;
        }
        return allowedExts[0];
    }

    private static async Task<DriveMetadata?> GetDriveMetadataAsync(HttpClient http, string driveFileId)
    {
        var url = $"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(driveFileId)}"
                  + "?fields=id,name,size,mimeType,modifiedTime";
        using var resp = await http.GetAsync(url).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        return JsonSerializer.Deserialize<DriveMetadata>(json);
    }

    /// <summary>
    /// Refresh-path metadata fetch. Differs from <see cref="GetDriveMetadataAsync"/>
    /// by surfacing 404 / 403 as typed exceptions instead of nulls so the
    /// PR-7 change processor can map them to <c>DriveChangeOutcome</c>
    /// values without re-probing the response. 5xx/transient errors still
    /// surface as a null return (caller treats as a no-op).
    /// </summary>
    public static async Task<DriveMetadataResult?> ProbeDriveMetadataAsync(HttpClient http, string driveFileId)
    {
        var url = $"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(driveFileId)}"
                  + "?fields=id,name,size,mimeType,modifiedTime";
        using var resp = await http.GetAsync(url).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.NotFound)
        {
            throw new DriveFileNotFoundException(driveFileId);
        }
        if (resp.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new DriveFilePermissionDeniedException(driveFileId);
        }
        if (!resp.IsSuccessStatusCode)
        {
            // Transient (5xx / unexpected 4xx) — caller treats as Error.
            return null;
        }
        var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        var meta = JsonSerializer.Deserialize<DriveMetadata>(json);
        if (meta == null)
        {
            return null;
        }
        return new DriveMetadataResult(meta.Id, meta.Name, meta.Size, meta.MimeType, meta.ModifiedTime);
    }

    /// <summary>
    /// Internal helper used by <see cref="RefreshFileAsync"/>. Maps 404/403
    /// to typed exceptions (which propagate up to the change processor) and
    /// any other failure to a null return so the existing bool-returning
    /// <c>RefreshFileAsync</c> contract continues to work.
    /// </summary>
    private static async Task<DriveMetadata?> GetDriveMetadataForRefreshAsync(HttpClient http, string driveFileId)
    {
        var url = $"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(driveFileId)}"
                  + "?fields=id,name,size,mimeType,modifiedTime";
        using var resp = await http.GetAsync(url).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.NotFound)
        {
            throw new DriveFileNotFoundException(driveFileId);
        }
        if (resp.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new DriveFilePermissionDeniedException(driveFileId);
        }
        if (!resp.IsSuccessStatusCode)
        {
            return null;
        }
        var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        return JsonSerializer.Deserialize<DriveMetadata>(json);
    }

    /// <summary>
    /// Public-shape projection of Drive's <c>files.get</c> metadata response,
    /// returned by <see cref="ProbeDriveMetadataAsync"/>. Mirrors the internal
    /// <c>DriveMetadata</c> record but kept separate so the internal type
    /// can stay nested + private to the file service.
    /// </summary>
    public sealed record DriveMetadataResult(string? Id, string? Name, long Size, string? MimeType, DateTime? ModifiedTime);

    private class DriveMetadata
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        // Drive returns size as a string; allow either.
        [JsonPropertyName("size")]
        [JsonConverter(typeof(NumberOrStringLongConverter))]
        public long Size { get; set; }

        [JsonPropertyName("mimeType")]
        public string? MimeType { get; set; }

        [JsonPropertyName("modifiedTime")]
        public DateTime? ModifiedTime { get; set; }
    }

    private class NumberOrStringLongConverter : JsonConverter<long>
    {
        public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var s = reader.GetString();
                return long.TryParse(s, out var v) ? v : 0;
            }
            if (reader.TokenType == JsonTokenType.Number)
            {
                return reader.GetInt64();
            }
            return 0;
        }

        public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options)
        {
            writer.WriteNumberValue(value);
        }
    }
}
