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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.GrpcServices;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;

/// <summary>A shown photo of an outbreak, with the pens its registration covers and who registered it. EffectiveAt is UTC.</summary>
public sealed record OutbreakPhoto(Guid PhotoUuid, int RegistrationId, IReadOnlyList<int> LocationIds, DateTime EffectiveAt,
    int SiteId, string SiteName);

/// <summary>Outbreak photos for a tail-bite manager: the list for the app and the bytes for the web outbreak page.</summary>
public interface ITailBiteOutbreakPhotoService
{
    /// <summary>Manager of an enabled property: the outbreak's shown photos, newest registration first.</summary>
    Task<IReadOnlyList<OutbreakPhoto>> ListAsync(int callerSiteId, int outbreakId);

    /// <summary>Manager of the photo's enabled property; every refusal is TailBiteNotFoundException("Photo not found.").</summary>
    Task<(Stream Content, string ContentType)> OpenForManagerAsync(int callerSiteId, Guid photoUuid);
}

public class TailBiteOutbreakPhotoService(BackendConfigurationPnDbContext db, ITailBiteAccess access, ITailBitePhotoQuery photoQuery,
    ITailBitePhotoStorage storage, IGrpcSiteResolver siteResolver) : ITailBiteOutbreakPhotoService
{
    private const string Removed = Constants.WorkflowStates.Removed;

    public async Task<IReadOnlyList<OutbreakPhoto>> ListAsync(int callerSiteId, int outbreakId)
    {
        var propertyId = await db.TailBiteOutbreaks.AsNoTracking().Where(o => o.Id == outbreakId && o.WorkflowState != Removed)
                             .Select(o => (int?)o.PropertyId).SingleOrDefaultAsync()
                         ?? throw TailBiteForbiddenException.NoAccess();
        await access.RequireEnabledManagerAsync(callerSiteId, propertyId);
        var rows = await TailBiteOutbreakRows.Of(db, [outbreakId]).OrderBy(x => x.Row.Id).ToListAsync();
        var pens = rows.ToLookup(x => x.Registration.Id, x => x.Row.LocationId);
        var byId = rows.Select(x => x.Registration).DistinctBy(r => r.Id).ToDictionary(r => r.Id);
        var photos = await photoQuery.ForRegistrationsAsync(byId.Values.ToList());
        var names = await siteResolver.DisplayNamesAsync(byId.Values.Select(r => r.SiteId));
        // Newest registration first; OrderBy is stable, so photos keep their oldest-first order within one registration.
        return photos.Select(p =>
            {
                var r = byId[p.RegistrationId];
                return new OutbreakPhoto(p.PhotoUuid, r.Id, pens[r.Id].Distinct().ToList(),
                    DateTime.SpecifyKind(r.EffectiveAt, DateTimeKind.Utc), r.SiteId, names[r.SiteId]);
            })
            .OrderByDescending(p => p.EffectiveAt).ThenBy(p => p.RegistrationId)
            .ToList();
    }

    public async Task<(Stream Content, string ContentType)> OpenForManagerAsync(int callerSiteId, Guid photoUuid)
    {
        // Unknown, unstored, cancelled, foreign and switched-off answer alike, so the call cannot be used to learn which uuids exist.
        var photo = await photoQuery.FindShownAsync(photoUuid) ?? throw TailBiteNotFoundException.PhotoNotFound();
        try { await access.RequireEnabledManagerAsync(callerSiteId, photo.PropertyId); }
        catch (TailBiteForbiddenException) { throw TailBiteNotFoundException.PhotoNotFound(); }
        return await storage.OpenAsync(photo.SdkUploadedDataId);
    }
}
