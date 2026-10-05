#nullable enable
using System;
using System.IO;
using System.Threading.Tasks;

namespace BackendConfiguration.Pn.Services.FileArchive;

/// <summary>
/// Local-disk <see cref="IArchiveStorage"/>, picked by <see cref="CoreArchiveStorage"/> when the SDK's
/// <c>s3Enabled</c> is false (CI, self-hosted installs). Files live under <see cref="DefaultRootDirectory"/>
/// (the container's temp dir) and do not survive its reset - the same limitation adhoc photos
/// (<see cref="BackendConfigurationAdhocService.LocalAdhocPhotoStorage"/>) and calendar attachments have.
/// </summary>
public class LocalArchiveStorage(string rootDirectory) : IArchiveStorage
{
    public static readonly string DefaultRootDirectory = Path.Combine(Path.GetTempPath(), "archive-files");

    public LocalArchiveStorage() : this(DefaultRootDirectory)
    {
    }

    public Task PutAsync(string localPath, string objectName)
    {
        var path = ResolvePath(objectName);
        Directory.CreateDirectory(rootDirectory);
        // Copy beside the target, then rename over it: a rename replaces the file even while a reader
        // (a PDF download) still has the old one open, and a half-written copy never carries the real name.
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.Copy(localPath, temp);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }

        return Task.CompletedTask;
    }

    public Task<Stream?> GetAsync(string objectName)
    {
        var path = ResolvePath(objectName);
        return Task.FromResult<Stream?>(File.Exists(path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)
            : null);
    }

    // The name becomes a path segment, so only a bare object name is accepted -
    // anything rooted or carrying a separator / ".." could escape the root.
    private string ResolvePath(string objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName)
            || Path.IsPathRooted(objectName)
            || objectName == "."
            || objectName.Contains("..")
            || objectName.IndexOfAny(['/', '\\']) >= 0)
        {
            throw new ArgumentException(
                $"Archive object name must be a bare file name (was '{objectName}').", nameof(objectName));
        }

        return Path.Combine(rootDirectory, objectName);
    }
}
