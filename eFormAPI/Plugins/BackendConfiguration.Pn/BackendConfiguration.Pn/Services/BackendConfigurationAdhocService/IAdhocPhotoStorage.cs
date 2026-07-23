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

using System.IO;
using System.Threading.Tasks;

/// <summary>
/// Thin seam around the SDK Core's S3 byte transfer
/// (<c>Core.PutFileToS3Storage</c>/<c>Core.GetFileFromS3Storage</c> - the
/// same calls <c>EventsGrpcService.UploadPhoto</c> makes). <c>Core</c> is a
/// concrete SDK type with no public S3 test double and non-virtual members,
/// so it cannot be substituted directly; this interface is the mockable
/// boundary that lets <c>BackendConfigurationAdhocService</c>'s photo methods
/// be unit-tested without a real S3 bucket (there is neither S3 nor a MinIO
/// container in this repo's local or CI test environment - confirmed via the
/// dotnet-core-pr.yml workflow and the Testcontainers-seeded 420_SDK.sql
/// fixture, which ships <c>s3Enabled=false</c> with placeholder credentials),
/// while <see cref="AdhocPhotoStorage"/> still calls the exact same Core
/// methods in production.
/// </summary>
public interface IAdhocPhotoStorage
{
    Task PutAsync(string fileName, Stream content);

    Task<Stream> GetAsync(string fileName);
}
