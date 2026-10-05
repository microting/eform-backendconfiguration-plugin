/*
The MIT License (MIT)

Copyright (c) 2007 - 2026 Microting A/S

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

namespace BackendConfiguration.Pn.Services.TailBite;

using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.BackendConfigurationAdhocService;
using Microsoft.EntityFrameworkCore;
using Microting.eFormApi.BasePn.Abstractions;
using Sentry;
using SdkUploadedData = Microting.eForm.Infrastructure.Data.Entities.UploadedData;

/// <summary>Owns the SDK UploadedData row and the stored bytes of a tail-bite photo.</summary>
public interface ITailBitePhotoStorage
{
    /// <summary>Stores the bytes and returns the id of the SDK UploadedData row that points at them.</summary>
    Task<int> StoreAsync(byte[] bytes, string contentType);

    /// <summary>Opens the stored bytes of an UploadedData row, with the content type its extension implies.</summary>
    Task<(Stream Content, string ContentType)> OpenAsync(int uploadedDataId);
}

// Mirrors the storage sequence of BackendConfigurationAdhocService.SavePhoto so both land in the same store; keep the two in sync.
public class TailBitePhotoStorage(IAdhocPhotoStorage inner, IEFormCoreService coreHelper) : ITailBitePhotoStorage
{
    public async Task<int> StoreAsync(byte[] bytes, string contentType)
    {
        if (bytes is null || bytes.Length == 0) throw new TailBiteValidationException("The photo is empty.");
        var extension = PhotoExtension(contentType);

        var core = await coreHelper.GetCore();
        var sdkDbContext = core.DbContextHelper.GetDbContext();

        string checksum;
        using (var md5 = MD5.Create())
        {
            checksum = BitConverter.ToString(md5.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
        }

        // Two-phase FileName write: create first to get the row's Id, then fold it into the final storage key
        // so rows sharing a checksum (re-uploads) don't collide. FileLocation stays blank: the bytes are reached
        // only through the photo storage by FileName.
        var uploadedData = new SdkUploadedData
        {
            Checksum = checksum,
            FileName = $"{checksum}.{extension}",
            FileLocation = "",
            Extension = $".{extension}",
        };
        await uploadedData.Create(sdkDbContext);

        var fileName = $"{uploadedData.Id}_{uploadedData.FileName}";
        try
        {
            uploadedData.FileName = fileName;
            await uploadedData.Update(sdkDbContext);

            using var stream = new MemoryStream(bytes);
            await inner.PutAsync(fileName, stream);
        }
        catch
        {
            // With no bytes behind it the row would be an orphan; a failed cleanup must not replace the real error.
            try
            {
                await uploadedData.Delete(sdkDbContext);
            }
            catch (Exception cleanupException)
            {
                SentrySdk.CaptureException(cleanupException);
            }

            throw;
        }

        return uploadedData.Id;
    }

    public async Task<(Stream Content, string ContentType)> OpenAsync(int uploadedDataId)
    {
        var core = await coreHelper.GetCore();
        var sdkDbContext = core.DbContextHelper.GetDbContext();
        var uploadedData = await sdkDbContext.UploadedDatas.AsNoTracking().FirstOrDefaultAsync(u => u.Id == uploadedDataId)
                           ?? throw new TailBiteNotFoundException("Photo file not found.");
        var content = await inner.GetAsync(uploadedData.FileName);
        // TailBiteRegistrationPhoto has no ContentType column; StoreAsync only ever writes .jpg or .png.
        return (content, uploadedData.Extension == ".png" ? "image/png" : "image/jpeg");
    }

    private static string PhotoExtension(string contentType) => contentType?.Trim().ToLowerInvariant() switch
    {
        "image/jpeg" or "image/jpg" => "jpg",
        "image/png" => "png",
        _ => throw new TailBiteValidationException($"contentType must be image/jpeg, image/jpg or image/png (was '{contentType}').")
    };
}
