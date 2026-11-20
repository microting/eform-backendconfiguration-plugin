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
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;

/// <summary>A photo an outbreak shows. UploadedDataId is server-only: the host serialises with Newtonsoft.</summary>
public sealed record RegistrationPhoto(Guid PhotoUuid, int RegistrationId, [property: Newtonsoft.Json.JsonIgnore] int UploadedDataId);

/// <summary>
/// The one rule for which tail-bite photos are shown, used by the app, the web and the compliance report alike.
/// </summary>
public interface ITailBitePhotoQuery
{
    /// <summary>
    /// The photos of the given registrations that pass TailBitePhotoOwnership, are not Removed, and whose
    /// registration is not cancelled; oldest first (CreatedAt, then Id). Cancelled registrations get none.
    /// </summary>
    Task<IReadOnlyList<RegistrationPhoto>> ForRegistrationsAsync(IReadOnlyCollection<TailBiteRegistration> registrations);

    /// <summary>The photo by uuid when it passes the same rule; null otherwise (unknown, unstored, removed, orphan, cancelled).</summary>
    Task<TailBiteRegistrationPhoto?> FindShownAsync(Guid photoUuid);
}

public class TailBitePhotoQuery(BackendConfigurationPnDbContext db) : ITailBitePhotoQuery
{
    private const string Removed = Constants.WorkflowStates.Removed;

    public async Task<IReadOnlyList<RegistrationPhoto>> ForRegistrationsAsync(IReadOnlyCollection<TailBiteRegistration> registrations)
    {
        // BelongsTo is per registration; for a batch the same rule is applied in two steps: stored and live in the query,
        // uuid, property and uploading site through the lookup key.
        var shown = registrations.Where(r => r.CancelledAt == null && r.WorkflowState != Removed).DistinctBy(r => r.Id).ToList();
        if (shown.Count == 0) return [];
        // ClientUuid is unique, so the key cannot collide.
        var byOwner = shown.ToDictionary(r => (r.ClientUuid, r.PropertyId, r.SiteId), r => r.Id);
        var uuids = shown.Select(r => r.ClientUuid).ToList();
        var photos = await db.TailBiteRegistrationPhotos.AsNoTracking()
            .Where(p => uuids.Contains(p.RegistrationClientUuid) && p.WorkflowState != Removed)
            .Where(TailBitePhotoOwnership.IsStored)
            .OrderBy(p => p.CreatedAt).ThenBy(p => p.Id)
            .Select(p => new { p.PhotoUuid, p.RegistrationClientUuid, p.PropertyId, p.UploadedBySiteId, p.SdkUploadedDataId })
            .ToListAsync();
        return photos
            .Where(p => byOwner.ContainsKey((p.RegistrationClientUuid, p.PropertyId, p.UploadedBySiteId)))
            .Select(p => new RegistrationPhoto(p.PhotoUuid, byOwner[(p.RegistrationClientUuid, p.PropertyId, p.UploadedBySiteId)],
                p.SdkUploadedDataId))
            .ToList();
    }

    public async Task<TailBiteRegistrationPhoto?> FindShownAsync(Guid photoUuid)
        => await db.TailBiteRegistrationPhotos.AsNoTracking().Where(TailBitePhotoOwnership.IsStored)
            .Where(p => p.PhotoUuid == photoUuid && p.WorkflowState != Removed)
            .Where(p => db.TailBiteRegistrations.Any(r => r.ClientUuid == p.RegistrationClientUuid && r.PropertyId == p.PropertyId
                && r.SiteId == p.UploadedBySiteId && r.CancelledAt == null && r.WorkflowState != Removed))
            .FirstOrDefaultAsync();
}
