#nullable enable
using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Amazon.S3;
using Microting.eForm.Dto;
using Microting.eFormApi.BasePn.Abstractions;

namespace BackendConfiguration.Pn.Services.FileArchive;

/// <summary>
/// Production <see cref="IArchiveStorage"/>: S3 through the SDK when its <c>s3Enabled</c> setting is true,
/// otherwise <see cref="LocalArchiveStorage"/>. Decided per call, like
/// <see cref="BackendConfigurationAdhocService.AdhocPhotoStorage"/>, because the Core (and its settings)
/// does not exist in ConfigureServices.
/// </summary>
public class CoreArchiveStorage(IEFormCoreService coreService) : IArchiveStorage
{
    private readonly LocalArchiveStorage _local = new();

    public async Task PutAsync(string localPath, string objectName)
    {
        if (!await UseS3Async())
        {
            await _local.PutAsync(localPath, objectName);
            return;
        }

        var core = await coreService.GetCore();
        await core.PutFileToStorageSystem(localPath, objectName);
    }

    public async Task<Stream?> GetAsync(string objectName)
    {
        if (!await UseS3Async())
        {
            return await _local.GetAsync(objectName);
        }

        var core = await coreService.GetCore();
        try
        {
            var response = await core.GetFileFromS3Storage(objectName);
            return response?.ResponseStream;
        }
        catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<bool> UseS3Async()
    {
        var core = await coreService.GetCore();
        var s3Setting = await core.GetSdkSetting(Settings.s3Enabled);
        if (string.Equals(s3Setting, "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrEmpty(s3Setting) || string.Equals(s3Setting, "false", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // GetSdkSetting answers "N/A" when the setting can't be read. Guessing local there would put an S3
        // install's archive file in this pod's temp dir, lost on restart and unreadable from other replicas.
        throw new InvalidOperationException($"Cannot choose archive file storage: s3Enabled is '{s3Setting}'.");
    }
}
