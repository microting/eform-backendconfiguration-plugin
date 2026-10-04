#nullable enable
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Amazon.S3;
using Microting.eFormApi.BasePn.Abstractions;

namespace BackendConfiguration.Pn.Services.FileArchive;

public class CoreArchiveStorage(IEFormCoreService coreService) : IArchiveStorage
{
    public async Task PutAsync(string localPath, string objectName)
    {
        var core = await coreService.GetCore();
        await core.PutFileToStorageSystem(localPath, objectName);
    }

    public async Task<Stream?> GetAsync(string objectName)
    {
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
}
