#nullable enable
using System.IO;
using System.Threading.Tasks;

namespace BackendConfiguration.Pn.Services.FileArchive;

/// <summary>Seam over the SDK file storage (S3 or local) so filing can be tested without it.</summary>
public interface IArchiveStorage
{
    Task PutAsync(string localPath, string objectName);
    Task<Stream?> GetAsync(string objectName);
}
