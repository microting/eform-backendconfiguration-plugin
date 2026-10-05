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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Sentry;

/// <summary>Tail-bite registrations: idempotent create with outbreak evaluation, photos, recent list.</summary>
public interface ITailBiteRegistrationService
{
    /// <summary>
    /// Creates the registration once per ClientUuid. A replay by the same worker returns the stored result,
    /// which also lists outbreaks the registration's rows were linked to after the original call.
    /// </summary>
    Task<CreateRegistrationResult> CreateAsync(int callerSiteId, CreateRegistrationCommand cmd);

    /// <summary>
    /// Stores a photo (idempotent per photo uuid for the same property, worker and registration uuid; any other reuse
    /// of the uuid is a conflict). It attaches to the registration once TailBitePhotoOwnership matches.
    /// </summary>
    Task<Guid> SavePhotoAsync(int callerSiteId, int propertyId, Guid photoUuid, Guid registrationClientUuid, byte[] bytes, string contentType);

    /// <summary>Opens a photo for a worker of the photo's property.</summary>
    Task<(Stream Content, string ContentType)> GetPhotoAsync(int callerSiteId, Guid photoUuid);

    /// <summary>The caller's own registrations on the property since the given time, newest first.</summary>
    Task<IReadOnlyList<RecentRegistration>> ListRecentAsync(int callerSiteId, int propertyId, DateTime sinceUtc);
}

public class TailBiteRegistrationService(
    BackendConfigurationPnDbContext db,
    ITailBitePropertyLock propertyLock,
    ITailBiteSnapshotLoader loader,
    ITailBiteDecisionWriter writer,
    ITailBiteAccess access,
    ITailBitePhotoStorage photoStorage,
    TimeProvider clock,
    ITailBiteOutbreakNotifier? notifier = null,
    ILogger<TailBiteRegistrationService>? logger = null) : ITailBiteRegistrationService
{
    private const int RecentLimit = 50;

    public async Task<CreateRegistrationResult> CreateAsync(int callerSiteId, CreateRegistrationCommand cmd)
    {
        await access.RequireWorkerAsync(callerSiteId, cmd.PropertyId);
        Validate(cmd);

        CreateRegistrationResult result;
        try
        {
            bool created;
            (result, created) = await propertyLock.RunLockedAsync(cmd.PropertyId, () => CreateLockedAsync(callerSiteId, cmd));
            if (!created) return result; // a replay must not re-send the push
        }
        catch (DbUpdateException)
        {
            // Backstop for a duplicate-key race on ClientUuid (§7.2). The property lock normally serializes
            // duplicates so the second call replays inside CreateLockedAsync; this path covers anything else.
            db.ChangeTracker.Clear();
            var replay = await ExistingAsync(callerSiteId, cmd);
            if (replay is null) throw;
            return replay;
        }

        if (notifier is not null && result.Outbreaks.Any(o => o.Opened))
            await NotifySafelyAsync(cmd.PropertyId, result.Outbreaks); // after commit (§7.4)
        return result;
    }

    // The registration is committed by now; a failing push must not turn the request into an error.
    private async Task NotifySafelyAsync(int propertyId, IReadOnlyList<OutbreakOutcome> outcomes)
    {
        try
        {
            await notifier!.NotifyOpenedAsync(propertyId, outcomes);
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            logger?.LogWarning(e, "Outbreak notification failed for property {PropertyId}", propertyId);
        }
    }

    private const int MaxCountPerLocation = 10000;
    private const int MaxCommentLength = 2000; // the Comment column limit

    private static void Validate(CreateRegistrationCommand cmd)
    {
        if (cmd.Locations.Count > 0 && cmd.Locations.Any(l => l.Minor < 0 || l.Severe < 0))
            throw new TailBiteValidationException("Count at least one bitten pig; counts cannot be negative.");
        // Bounded before any summing, so the totals below and the evaluator's cannot overflow.
        if (cmd.Locations.Any(l => l.Minor > MaxCountPerLocation || l.Severe > MaxCountPerLocation))
            throw new TailBiteValidationException($"A count cannot exceed {MaxCountPerLocation} pigs per location.");
        if (cmd.Locations.Count == 0 || cmd.Locations.Sum(l => (long)l.Minor + l.Severe) == 0)
            throw new TailBiteValidationException("Count at least one bitten pig; counts cannot be negative.");
        if (cmd.Comment is { Length: > MaxCommentLength })
            throw new TailBiteValidationException($"A comment cannot exceed {MaxCommentLength} characters.");
        if (cmd.Locations.Select(l => l.LocationId).Distinct().Count() != cmd.Locations.Count)
            throw new TailBiteValidationException("A location can only be listed once per registration.");
    }

    private async Task<(CreateRegistrationResult Result, bool Created)> CreateLockedAsync(int siteId, CreateRegistrationCommand cmd)
    {
        // The lock cleared the change tracker, so the pre-lock worker check is stale; repeat it, and re-query everything below.
        await access.RequireWorkerAsync(siteId, cmd.PropertyId);
        if (await ExistingAsync(siteId, cmd) is { } replay) return (replay, false);

        var prop = await db.TailBiteProperties.SingleAsync(p => p.PropertyId == cmd.PropertyId);
        if (!prop.Enabled) throw new TailBiteConflictException("Tail bite is not enabled for this property.");
        await ValidateOwnershipAsync(cmd);

        var received = clock.GetUtcNow().UtcDateTime;
        var effective = TailBiteClock.EffectiveAt(DateTime.SpecifyKind(cmd.RegisteredAtUtc, DateTimeKind.Utc), received);
        var reg = new TailBiteRegistration
        {
            PropertyId = cmd.PropertyId, SiteId = siteId, ClientUuid = cmd.ClientUuid, Comment = cmd.Comment,
            RegisteredAt = cmd.RegisteredAtUtc, ReceivedAt = received, EffectiveAt = effective
        };
        await reg.Create(db);
        var newRowIds = new List<int>();
        foreach (var l in cmd.Locations.Where(l => l.Minor + l.Severe > 0))
        {
            var row = new TailBiteRegistrationLocation { RegistrationId = reg.Id, LocationId = l.LocationId, MinorCount = l.Minor, SevereCount = l.Severe };
            await row.Create(db);
            newRowIds.Add(row.Id);
        }
        foreach (var a in cmd.ActionTypeIds.Distinct())
            await new TailBiteRegistrationAction { RegistrationId = reg.Id, ActionTypeId = a }.Create(db);

        var maxWindow = await ComputeMaxWindowAsync(cmd.PropertyId);
        var snapshot = await loader.LoadAsync(cmd.PropertyId, effective - maxWindow, effective + maxWindow);
        var decisions = OutbreakEvaluator.Evaluate(snapshot, effective, newRowIds);
        var outcomes = await writer.ApplyAsync(cmd.PropertyId, reg.Id, decisions);
        return (new CreateRegistrationResult(reg.Id, outcomes.Select(o => new OutbreakOutcome(o.OutbreakId, o.Opened)).ToList()), true);
    }

    private async Task ValidateOwnershipAsync(CreateRegistrationCommand cmd)
    {
        // Soft-deleted locations of the property are accepted (§7.2): a stable removed after the worker
        // synced offline must not lose the registration. Ownership is by PropertyId only.
        var locIds = cmd.Locations.Select(l => l.LocationId).ToList();
        if (await db.TailBiteLocations.CountAsync(l => locIds.Contains(l.Id) && l.PropertyId == cmd.PropertyId) != locIds.Count)
            throw new TailBiteValidationException("A location does not belong to this property.");
        // Unlike locations, a deleted action type is not accepted: the farm removed it from the list, so nothing new may use it.
        var actionIds = cmd.ActionTypeIds.Distinct().ToList();
        if (await db.TailBiteActionTypes.CountAsync(a => actionIds.Contains(a.Id) && a.PropertyId == cmd.PropertyId
                                                         && a.WorkflowState != Constants.WorkflowStates.Removed) != actionIds.Count)
            throw new TailBiteValidationException("An action type does not belong to this property or has been deleted.");
    }

    // Loaded and reduced in memory: an empty Max over a translated join is not portable across providers.
    private async Task<TimeSpan> ComputeMaxWindowAsync(int propertyId)
    {
        var windows = await db.TailBiteRules.Where(r => r.WorkflowState != Constants.WorkflowStates.Removed)
            .Join(db.TailBiteLocations.Where(l => l.PropertyId == propertyId), r => r.LocationId, l => l.Id, (r, _) => r.WindowDays)
            .ToListAsync();
        return TimeSpan.FromDays(Math.Max(1, windows.DefaultIfEmpty(1).Max()));
    }

    // Replays a registration this caller already made. A uuid owned by another worker or property is not a replay.
    private async Task<CreateRegistrationResult?> ExistingAsync(int siteId, CreateRegistrationCommand cmd)
    {
        var reg = await db.TailBiteRegistrations.AsNoTracking().FirstOrDefaultAsync(r => r.ClientUuid == cmd.ClientUuid);
        if (reg is null) return null;
        if (reg.SiteId != siteId || reg.PropertyId != cmd.PropertyId)
            throw new TailBiteForbiddenException("This registration id belongs to another worker.");
        var pairs = await (from row in db.TailBiteRegistrationLocations.AsNoTracking()
                           join link in db.TailBiteOutbreakLinks.AsNoTracking() on row.Id equals link.RegistrationLocationId
                           join o in db.TailBiteOutbreaks.AsNoTracking() on link.OutbreakId equals o.Id
                           where row.RegistrationId == reg.Id
                           select new { o.Id, o.OpenedByRegistrationId }).ToListAsync();
        var outcomes = pairs.Select(p => new OutbreakOutcome(p.Id, p.OpenedByRegistrationId == reg.Id))
            .Distinct().OrderBy(o => o.OutbreakId).ToList();
        return new CreateRegistrationResult(reg.Id, outcomes);
    }

    // A reservation older than this is taken to be abandoned by an upload that died before storing its bytes.
    private static readonly TimeSpan AbandonedReservationAge = TimeSpan.FromMinutes(5);

    private Task<TailBiteRegistrationPhoto?> PhotoByUuidAsync(Guid photoUuid)
        => db.TailBiteRegistrationPhotos.AsNoTracking().FirstOrDefaultAsync(p => p.PhotoUuid == photoUuid);

    // A photo uuid is replayed only by the upload that claimed it: same property, same worker, same registration.
    private static void RequireReplayOf(TailBiteRegistrationPhoto photo, int propertyId, int siteId, Guid registrationClientUuid)
    {
        if (photo.PropertyId != propertyId || photo.UploadedBySiteId != siteId || photo.RegistrationClientUuid != registrationClientUuid)
            throw new TailBiteConflictException("This photo id is already in use.");
    }

    // Retriable: another upload of the same photo holds the reservation, and success is never reported before the bytes are stored.
    private static TailBiteConflictException UploadInProgress() => new("Photo upload in progress; retry shortly.");

    public async Task<Guid> SavePhotoAsync(int callerSiteId, int propertyId, Guid photoUuid, Guid registrationClientUuid, byte[] bytes, string contentType)
    {
        await access.RequireWorkerAsync(callerSiteId, propertyId);
        var existing = await PhotoByUuidAsync(photoUuid);
        if (existing is not null)
        {
            RequireReplayOf(existing, propertyId, callerSiteId, registrationClientUuid);
            if (TailBitePhotoOwnership.IsStoredFunc(existing)) return photoUuid; // idempotent
            if (!TailBitePhotoOwnership.FailedReservationFunc(existing)
                && clock.GetUtcNow().UtcDateTime - (existing.UpdatedAt ?? DateTime.MinValue) < AbandonedReservationAge)
                throw UploadInProgress();
        }
        var linkedReg = await db.TailBiteRegistrations.AsNoTracking().FirstOrDefaultAsync(r => r.ClientUuid == registrationClientUuid);
        if (linkedReg is not null && (linkedReg.PropertyId != propertyId || linkedReg.SiteId != callerSiteId))
            throw new TailBiteForbiddenException("Photo does not belong to that registration.");
        // Before the registration exists a mismatching photo cannot be refused; it is simply never attached,
        // because TailBitePhotoOwnership also matches PropertyId and UploadedBySiteId.

        // Reserve the uuid before storing anything, so a concurrent upload of the same photo stores no bytes.
        var claim = existing is null
            ? await ReservePhotoAsync(callerSiteId, propertyId, photoUuid, registrationClientUuid)
            : await ClaimReservationAsync(existing);
        if (claim is null) return photoUuid; // a concurrent upload of the same photo had already stored it

        try
        {
            var uploadedDataId = await photoStorage.StoreAsync(bytes, contentType);
            if (await CompleteReservationAsync(claim, uploadedDataId)) return photoUuid;
        }
        catch
        {
            await ReleaseReservationAsync(claim);
            throw;
        }
        // Another upload of this photo took the reservation over while this one stored its bytes; its outcome stands.
        var current = await PhotoByUuidAsync(photoUuid);
        if (current is not null && TailBitePhotoOwnership.IsStoredFunc(current)) return photoUuid;
        throw UploadInProgress();
    }

    // Who holds a photo reservation: the row id and the UpdatedAt stamp the holder wrote. Every later write is
    // conditional on that stamp, so an upload whose reservation was taken over cannot overwrite the new holder's work.
    private sealed record PhotoClaim(int Id, DateTime Stamp);

    // A fresh stamp at the column's microsecond precision, later than the one it replaces, so it is never mistaken for it.
    private static DateTime NewStamp(DateTime? replaces)
    {
        var now = DateTime.UtcNow;
        var stamp = new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc);
        return replaces is { } previous && stamp <= previous ? previous.AddTicks(10) : stamp;
    }

    // Null when a concurrent upload of the same photo won the unique PhotoUuid index and has stored its bytes.
    private async Task<PhotoClaim?> ReservePhotoAsync(int siteId, int propertyId, Guid photoUuid, Guid registrationClientUuid)
    {
        var photo = new TailBiteRegistrationPhoto
        {
            PhotoUuid = photoUuid, PropertyId = propertyId, UploadedBySiteId = siteId,
            RegistrationClientUuid = registrationClientUuid, SdkUploadedDataId = 0
        };
        try
        {
            await photo.Create(db);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var winner = await PhotoByUuidAsync(photoUuid);
            if (winner is null) throw; // not the duplicate-key race
            RequireReplayOf(winner, propertyId, siteId, registrationClientUuid);
            if (!TailBitePhotoOwnership.IsStoredFunc(winner)) throw UploadInProgress();
            return null;
        }
        // Every later write to the row is a conditional update; a tracked copy would only go stale.
        db.Entry(photo).State = EntityState.Detached;
        // The stamp as the column holds it; PnBase.Create wrote it with sub-microsecond ticks.
        var stamp = await db.TailBiteRegistrationPhotos.AsNoTracking().Where(p => p.Id == photo.Id).Select(p => p.UpdatedAt).SingleAsync();
        return new PhotoClaim(photo.Id, stamp!.Value);
    }

    // Takes over a released or abandoned reservation. The update is conditional on the row being unchanged since it
    // was read (every write moves UpdatedAt), so exactly one concurrent retry wins; the others are told to retry.
    private async Task<PhotoClaim> ClaimReservationAsync(TailBiteRegistrationPhoto seen)
    {
        var (seenAt, stamp) = (seen.UpdatedAt, NewStamp(seen.UpdatedAt));
        var claimed = await db.TailBiteRegistrationPhotos
            .Where(p => p.Id == seen.Id && p.UpdatedAt == seenAt)
            .ExecuteUpdateAsync(x => x
                .SetProperty(p => p.WorkflowState, Constants.WorkflowStates.Created)
                .SetProperty(p => p.UpdatedAt, stamp)
                .SetProperty(p => p.Version, p => p.Version + 1));
        if (claimed == 0) throw UploadInProgress();
        await AddVersionAsync(seen.Id);
        return new PhotoClaim(seen.Id, stamp);
    }

    // Records the stored bytes, while this upload still holds the reservation or nobody does (a reservation released
    // by an upload that took it over and then failed). False when another upload holds it now.
    private async Task<bool> CompleteReservationAsync(PhotoClaim claim, int uploadedDataId)
    {
        var stamp = NewStamp(claim.Stamp);
        var completed = await db.TailBiteRegistrationPhotos
            .Where(p => p.Id == claim.Id)
            .Where(TailBitePhotoOwnership.HeldByOrFree(claim.Stamp))
            .ExecuteUpdateAsync(x => x
                .SetProperty(p => p.SdkUploadedDataId, uploadedDataId)
                .SetProperty(p => p.WorkflowState, Constants.WorkflowStates.Created)
                .SetProperty(p => p.UpdatedAt, stamp)
                .SetProperty(p => p.Version, p => p.Version + 1));
        if (completed == 0) return false;
        await AddVersionAsync(claim.Id);
        return true;
    }

    // With no bytes behind it the reservation must not stay live, but only the holder may release it: once another
    // upload took it over, this does nothing. A failed cleanup must not replace the real error.
    private async Task ReleaseReservationAsync(PhotoClaim claim)
    {
        try
        {
            var stamp = NewStamp(claim.Stamp);
            var released = await db.TailBiteRegistrationPhotos
                .Where(p => p.Id == claim.Id && p.UpdatedAt == claim.Stamp)
                .ExecuteUpdateAsync(x => x
                    .SetProperty(p => p.SdkUploadedDataId, 0)
                    .SetProperty(p => p.WorkflowState, Constants.WorkflowStates.Removed)
                    .SetProperty(p => p.UpdatedAt, stamp)
                    .SetProperty(p => p.Version, p => p.Version + 1));
            if (released == 1) await AddVersionAsync(claim.Id);
        }
        catch (Exception cleanupException)
        {
            SentrySdk.CaptureException(cleanupException);
            logger?.LogWarning(cleanupException, "Releasing the reservation of photo row {PhotoId} failed", claim.Id);
        }
    }

    // The conditional updates bypass PnBase, so the version row PnBase.Update would add is written here, from the
    // row as it now stands and with the same field mapping as PnBase.MapVersion.
    private async Task AddVersionAsync(int photoId)
    {
        var p = await db.TailBiteRegistrationPhotos.AsNoTracking().SingleAsync(x => x.Id == photoId);
        await db.TailBiteRegistrationPhotoVersions.AddAsync(new TailBiteRegistrationPhotoVersion
        {
            TailBiteRegistrationPhotoId = p.Id, PhotoUuid = p.PhotoUuid, PropertyId = p.PropertyId, UploadedBySiteId = p.UploadedBySiteId,
            RegistrationClientUuid = p.RegistrationClientUuid, SdkUploadedDataId = p.SdkUploadedDataId,
            CreatedAt = p.CreatedAt, UpdatedAt = p.UpdatedAt, WorkflowState = p.WorkflowState, Version = p.Version,
            CreatedByUserId = p.CreatedByUserId, UpdatedByUserId = p.UpdatedByUserId
        });
        await db.SaveChangesAsync();
    }

    public async Task<(Stream Content, string ContentType)> GetPhotoAsync(int callerSiteId, Guid photoUuid)
    {
        var photo = await db.TailBiteRegistrationPhotos.AsNoTracking().Where(TailBitePhotoOwnership.IsStored)
                        .FirstOrDefaultAsync(p => p.PhotoUuid == photoUuid && p.WorkflowState != Constants.WorkflowStates.Removed)
                    ?? throw new TailBiteNotFoundException("Photo not found.");
        await access.RequireWorkerAsync(callerSiteId, photo.PropertyId);
        return await photoStorage.OpenAsync(photo.SdkUploadedDataId);
    }

    public async Task<IReadOnlyList<RecentRegistration>> ListRecentAsync(int callerSiteId, int propertyId, DateTime sinceUtc)
    {
        await access.RequireWorkerAsync(callerSiteId, propertyId);
        var regs = await db.TailBiteRegistrations.AsNoTracking()
            .Where(r => r.PropertyId == propertyId && r.SiteId == callerSiteId && r.EffectiveAt >= sinceUtc)
            .OrderByDescending(r => r.EffectiveAt).Take(RecentLimit).ToListAsync();
        var ids = regs.Select(r => r.Id).ToList();
        var rows = await db.TailBiteRegistrationLocations.AsNoTracking().Where(x => ids.Contains(x.RegistrationId)).ToListAsync();
        return regs.Select(r => new RecentRegistration(r.Id, r.EffectiveAt,
            rows.Where(x => x.RegistrationId == r.Id).Select(x => new RegistrationLocationInput(x.LocationId, x.MinorCount, x.SevereCount)).ToList(),
            r.CancelledAt != null)).ToList();
    }
}
