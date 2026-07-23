using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.BackendConfigurationAdhocService;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// In-memory <see cref="IAdhocPhotoStorage"/> test double. There is no S3 (or
/// MinIO) available in this repo's local/CI test environment (confirmed: no
/// S3-related container in the dotnet-core-pr.yml workflow, and the
/// Testcontainers-seeded 420_SDK.sql fixture ships <c>s3Enabled=false</c>
/// with placeholder credentials), so exercising
/// <c>BackendConfigurationAdhocService.SavePhoto</c>/<c>GetPhoto</c> against
/// the real SDK Core's S3 client would fail outside a fully-configured
/// deployment. <see cref="IAdhocPhotoStorage"/> is the seam that lets these
/// tests verify the actual round-trip (bytes in, same bytes back out) plus
/// the authorization/row-reconciliation logic around it, without a real
/// bucket. <see cref="AdhocPhotoStorage"/> (production) still calls the exact
/// same <c>Core.PutFileToS3Storage</c>/<c>GetFileFromS3Storage</c> methods
/// <c>EventsGrpcService.UploadPhoto</c> uses.
/// </summary>
public class FakeAdhocPhotoStorage : IAdhocPhotoStorage
{
    private readonly ConcurrentDictionary<string, byte[]> _blobs = new();

    public Task PutAsync(string fileName, Stream content)
    {
        using var ms = new MemoryStream();
        content.CopyTo(ms);
        _blobs[fileName] = ms.ToArray();
        return Task.CompletedTask;
    }

    public Task<Stream> GetAsync(string fileName)
    {
        if (!_blobs.TryGetValue(fileName, out var bytes))
        {
            throw new FileNotFoundException($"No fake-stored blob for '{fileName}'.", fileName);
        }

        return Task.FromResult<Stream>(new MemoryStream(bytes));
    }
}
