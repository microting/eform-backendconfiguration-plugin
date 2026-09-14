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
using Microting.eForm.Dto;
using Microting.eFormApi.BasePn.Abstractions;

/// <summary>
/// Production <see cref="IAdhocPhotoStorage"/> - forwards each call to
/// <see cref="S3AdhocPhotoStorage"/> or <see cref="LocalAdhocPhotoStorage"/>
/// by the SDK's <c>s3Enabled</c> setting, the same signal
/// <c>BackendConfigurationCalendarService.DownloadFile</c> and the host's
/// <c>ImagesController</c> branch on. Decided per call rather than at DI time
/// because the Core (and its settings) does not exist in ConfigureServices.
/// </summary>
public class AdhocPhotoStorage(IEFormCoreService coreHelper) : IAdhocPhotoStorage
{
    private readonly S3AdhocPhotoStorage _s3 = new(coreHelper);
    private readonly LocalAdhocPhotoStorage _local = new();

    public async Task PutAsync(string fileName, Stream content)
    {
        var storage = await SelectAsync().ConfigureAwait(false);
        await storage.PutAsync(fileName, content).ConfigureAwait(false);
    }

    public async Task<Stream> GetAsync(string fileName)
    {
        var storage = await SelectAsync().ConfigureAwait(false);
        return await storage.GetAsync(fileName).ConfigureAwait(false);
    }

    private async Task<IAdhocPhotoStorage> SelectAsync()
    {
        var core = await coreHelper.GetCore().ConfigureAwait(false);
        var s3Setting = await core.GetSdkSetting(Settings.s3Enabled).ConfigureAwait(false);
        if (string.Equals(s3Setting, "true", StringComparison.OrdinalIgnoreCase))
        {
            return _s3;
        }

        if (string.IsNullOrEmpty(s3Setting)
            || string.Equals(s3Setting, "false", StringComparison.OrdinalIgnoreCase))
        {
            return _local;
        }

        // GetSdkSetting answers "N/A" when the setting can't be read. Guessing
        // local there would put an S3 install's photo in this pod's temp dir,
        // lost on restart and unreadable from other replicas.
        throw new InvalidOperationException(
            $"Cannot choose adhoc photo storage: s3Enabled is '{s3Setting}'.");
    }
}
