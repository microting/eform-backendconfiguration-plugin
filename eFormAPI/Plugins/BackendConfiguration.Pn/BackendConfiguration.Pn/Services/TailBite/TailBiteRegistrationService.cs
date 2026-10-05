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

    /// <summary>Stores a photo (idempotent per photo uuid); it attaches to the registration once TailBitePhotoOwnership matches.</summary>
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

    private static void Validate(CreateRegistrationCommand cmd)
    {
        if (cmd.Locations.Count == 0 || cmd.Locations.Any(l => l.Minor < 0 || l.Severe < 0) || cmd.Locations.Sum(l => l.Minor + l.Severe) == 0)
            throw new TailBiteValidationException("Count at least one bitten pig; counts cannot be negative.");
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

    private Task<bool> PhotoExistsAsync(Guid photoUuid) => db.TailBiteRegistrationPhotos.AnyAsync(p => p.PhotoUuid == photoUuid);

    public async Task<Guid> SavePhotoAsync(int callerSiteId, int propertyId, Guid photoUuid, Guid registrationClientUuid, byte[] bytes, string contentType)
    {
        await access.RequireWorkerAsync(callerSiteId, propertyId);
        if (await PhotoExistsAsync(photoUuid)) return photoUuid; // idempotent
        var linkedReg = await db.TailBiteRegistrations.AsNoTracking().FirstOrDefaultAsync(r => r.ClientUuid == registrationClientUuid);
        if (linkedReg is not null && (linkedReg.PropertyId != propertyId || linkedReg.SiteId != callerSiteId))
            throw new TailBiteForbiddenException("Photo does not belong to that registration.");
        // Before the registration exists a mismatching photo cannot be refused; it is simply never attached,
        // because TailBitePhotoOwnership also matches PropertyId and UploadedBySiteId.
        var uploadedDataId = await photoStorage.StoreAsync(bytes, contentType);
        try
        {
            await new TailBiteRegistrationPhoto
            {
                PhotoUuid = photoUuid, PropertyId = propertyId, UploadedBySiteId = callerSiteId,
                RegistrationClientUuid = registrationClientUuid, SdkUploadedDataId = uploadedDataId
            }.Create(db);
        }
        catch (DbUpdateException)
        {
            // A concurrent retry of the same photo won the unique PhotoUuid index. Its row is the answer; the
            // UploadedData row this call stored stays behind.
            db.ChangeTracker.Clear();
            if (await PhotoExistsAsync(photoUuid)) return photoUuid;
            throw;
        }
        return photoUuid;
    }

    public async Task<(Stream Content, string ContentType)> GetPhotoAsync(int callerSiteId, Guid photoUuid)
    {
        var photo = await db.TailBiteRegistrationPhotos.AsNoTracking()
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
