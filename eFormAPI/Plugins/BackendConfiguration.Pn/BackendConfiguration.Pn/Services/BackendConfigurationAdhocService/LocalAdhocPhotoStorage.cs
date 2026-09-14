/*
The MIT License (MIT)

Copyright (c) 2007 - 2022 Microting A/S

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

#nullable enable

namespace BackendConfiguration.Pn.Services.BackendConfigurationAdhocService;

using System;
using System.IO;
using System.Threading.Tasks;

/// <summary>
/// Local-disk <see cref="IAdhocPhotoStorage"/>, picked by
/// <see cref="AdhocPhotoStorage"/> when <c>s3Enabled</c> is false. Files live
/// under <see cref="DefaultRootDirectory"/> (the container's temp dir) and do
/// not survive its reset - the same limitation calendar attachments have
/// (<c>GetTempPath()/calendar-attachments</c>).
/// </summary>
public class LocalAdhocPhotoStorage(string rootDirectory) : IAdhocPhotoStorage
{
    public static readonly string DefaultRootDirectory = Path.Combine(Path.GetTempPath(), "adhoc-photos");

    public LocalAdhocPhotoStorage() : this(DefaultRootDirectory)
    {
    }

    public async Task PutAsync(string fileName, Stream content)
    {
        var path = ResolvePath(fileName);
        Directory.CreateDirectory(rootDirectory);
        await using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(file).ConfigureAwait(false);
    }

    public Task<Stream> GetAsync(string fileName)
    {
        var path = ResolvePath(fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"No locally stored adhoc photo '{fileName}'.", fileName);
        }

        return Task.FromResult<Stream>(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read));
    }

    // The name becomes a path segment, so only a bare file name is accepted -
    // anything rooted or carrying a separator / ".." could escape the root.
    private string ResolvePath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)
            || Path.IsPathRooted(fileName)
            || fileName == "."
            || fileName.Contains("..")
            || fileName.IndexOfAny(['/', '\\']) >= 0)
        {
            throw new ArgumentException(
                $"Adhoc photo file name must be a bare file name (was '{fileName}').", nameof(fileName));
        }

        return Path.Combine(rootDirectory, fileName);
    }
}
