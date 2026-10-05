#nullable enable
using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace BackendConfiguration.Pn.Services.FileArchive;

/// <summary>A stream copied to a temp file, with its MD5 (lower-case hex).</summary>
public sealed record StagedUpload(string TempPath, string TempName, string Md5)
{
    /// <summary>
    /// Copies <paramref name="content"/> to a temp file (<c>{ticks}_{guid}.{extension}</c> under
    /// <c>backend-configuration-files</c>) and computes its MD5. Shared by every path that stores
    /// a file in the archive. <paramref name="extension"/> is used as given (no normalisation).
    /// </summary>
    public static async Task<StagedUpload> StageAsync(Stream content, string extension)
    {
        var folder = Path.Combine(Path.GetTempPath(), "backend-configuration-files");
        Directory.CreateDirectory(folder);
        var tempName = $"{DateTime.UtcNow.Ticks}_{Guid.NewGuid():N}";
        var tempPath = Path.Combine(folder, $"{tempName}.{extension}");

        await using (var target = new FileStream(tempPath, FileMode.Create))
        {
            await content.CopyToAsync(target);
        }

        string md5;
        await using (var read = System.IO.File.OpenRead(tempPath))
        {
            md5 = Convert.ToHexString(await MD5.HashDataAsync(read)).ToLowerInvariant();
        }

        return new StagedUpload(tempPath, tempName, md5);
    }
}
