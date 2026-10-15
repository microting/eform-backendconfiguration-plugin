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

namespace BackendConfiguration.Pn.Services.ChemicalReporting;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ChemicalInventoryService;
using Infrastructure.Models.Chemicals;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.EformBackendConfigurationBase.Infrastructure.Helpers;

/// <summary>
/// Rebuilds Plan D's weekly digest sections (Contract C5) for one property so W5
/// can show "Eksempel på næste oversigt". Plan D's ChemicalDigestPlanner lives in
/// the service container and cannot be referenced here; both use the same
/// ChemicalAlertRules, and "Nye registreringer denne uge" follows Plan D
/// decision 16 (NewRegistrationsSince; moves excluded; open placements only).
/// </summary>
public class ChemicalDigestPreviewBuilder(
    BackendConfigurationPnDbContext dbContext,
    IChemicalRegisterReader register,
    IChemicalNameDirectory names) : IChemicalDigestPreviewBuilder
{
    private static readonly TimeSpan DigestSendAt = new(7, 0, 0);

    private const string Removed = Constants.WorkflowStates.Removed;

    /// <summary>The most recent Monday 07:00 Europe/Copenhagen at or before utcNow, as UTC.</summary>
    public static DateTime NewRegistrationsSince(DateTime utcNow)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), ChemicalAlertRules.Copenhagen);
        var daysSinceMonday = ((int)local.DayOfWeek + 6) % 7;
        var slotLocal = local.Date.AddDays(-daysSinceMonday) + DigestSendAt;
        if (slotLocal > local)
        {
            slotLocal = slotLocal.AddDays(-7);
        }

        // 07:00 is never inside a DST gap or overlap, so the conversion is unambiguous.
        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(slotLocal, DateTimeKind.Unspecified), ChemicalAlertRules.Copenhagen);
    }

    /// <summary>The Copenhagen date of the next digest slot: the Monday 07:00 slot strictly after utcNow.</summary>
    public static DateOnly NextSlotDate(DateTime utcNow)
    {
        var mostRecentSlot = TimeZoneInfo.ConvertTimeFromUtc(NewRegistrationsSince(utcNow), ChemicalAlertRules.Copenhagen);
        return DateOnly.FromDateTime(mostRecentSlot.Date.AddDays(7));
    }

    public async Task<ChemicalDigestModel> BuildAsync(int propertyId, DateTime utcNow)
    {
        var property = await dbContext.Properties.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == propertyId && p.WorkflowState != Removed).ConfigureAwait(false)
            ?? throw new ChemicalNotFoundException($"Property {propertyId} not found.");
        // The preview stands for the next digest, which Plan D evaluates on the slot's Monday, not today.
        var slotDate = NextSlotDate(utcNow);

        var locations = await dbContext.ChemicalLocations.AsNoTracking()
            .Where(l => l.PropertyId == propertyId)
            .ToDictionaryAsync(l => l.Id, l => l.Name).ConfigureAwait(false);
        var locationIds = locations.Keys.ToArray();
        var open = await dbContext.ChemicalPlacements.AsNoTracking()
            .Where(p => locationIds.Contains(p.LocationId) && p.WorkflowState != Removed && p.RemovedAt == null)
            .ToListAsync().ConfigureAwait(false);
        var since = NewRegistrationsSince(utcNow);
        var recent = open.Where(p => p.MovedFromPlacementId == null && p.RegisteredAt >= since && p.RegisteredAt <= utcNow).ToList();

        var entries = (await register.GetByIdsAsync(open.Select(p => p.ChemicalId).Distinct().ToList()).ConfigureAwait(false))
            .ToDictionary(e => e.ChemicalId);
        var userNames = await names.UserNamesAsync(recent.Select(p => p.RegisteredByUserId)).ConfigureAwait(false);

        var passed = new List<ChemicalDigestItemModel>();
        var upcoming = new List<ChemicalDigestItemModel>();
        var statuses = new List<ChemicalDigestItemModel>();
        foreach (var placement in open)
        {
            if (!entries.TryGetValue(placement.ChemicalId, out var entry))
            {
                continue;
            }

            foreach (var (kind, deadline) in new[]
                     {
                         (ChemicalDeadlineKindEnum.UseAndPossession, entry.UseAndPossessionDeadline),
                         (ChemicalDeadlineKindEnum.Sales, entry.SalesDeadline),
                     })
            {
                var threshold = ChemicalAlertRules.ReachedThreshold(deadline, slotDate);
                if (threshold is null)
                {
                    continue;
                }

                var item = Item(placement, entry, locations) with { DeadlineKind = kind, Deadline = deadline, Threshold = threshold };
                (threshold == ChemicalAlertThresholdEnum.Passed ? passed : upcoming).Add(item);
            }

            if (ChemicalAlertRules.IsActionStatus(entry.Status))
            {
                statuses.Add(Item(placement, entry, locations));
            }
        }

        var fresh = recent
            .Where(p => entries.ContainsKey(p.ChemicalId))
            .Select(p => Item(p, entries[p.ChemicalId], locations) with
            {
                RegisteredByName = userNames.TryGetValue(p.RegisteredByUserId, out var name) ? name : string.Empty,
                RegisteredAt = DateTime.SpecifyKind(p.RegisteredAt, DateTimeKind.Utc),
            })
            .ToList();

        // Recipients are read the way the settings screen reads them (Plan A's MapSettings), not re-split here.
        var settings = await dbContext.ChemicalPropertySettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.PropertyId == propertyId && s.WorkflowState != Removed).ConfigureAwait(false);
        var recipients = ChemicalInventoryService.MapSettings(propertyId, settings).DigestRecipients;

        return new ChemicalDigestModel(
            propertyId, property.Name, ISOWeek.GetWeekOfYear(slotDate.ToDateTime(TimeOnly.MinValue)), utcNow,
            ByDeadline(passed), ByDeadline(upcoming),
            ByName(statuses), ByName(fresh), recipients);
    }

    // The order of Plan D's e-mail (ChemicalDigestPlanner): deadline DATE then name; the other sections by name; ordinal, case-insensitive.
    private static List<ChemicalDigestItemModel> ByDeadline(IEnumerable<ChemicalDigestItemModel> items) =>
        items.OrderBy(i => i.Deadline.HasValue ? ChemicalAlertRules.DeadlineDate(i.Deadline.Value) : (DateOnly?)null)
            .ThenBy(i => i.ProductName, StringComparer.OrdinalIgnoreCase).ToList();

    private static List<ChemicalDigestItemModel> ByName(IEnumerable<ChemicalDigestItemModel> items) =>
        items.OrderBy(i => i.ProductName, StringComparer.OrdinalIgnoreCase).ToList();

    private static ChemicalDigestItemModel Item(
        ChemicalPlacement placement,
        ChemicalRegisterEntryModel entry,
        IReadOnlyDictionary<int, string> locations) =>
        new(placement.Id, placement.ChemicalId, entry.Name, entry.RegistrationNo,
            locations.TryGetValue(placement.LocationId, out var location) ? location : string.Empty,
            placement.PlacementNote ?? string.Empty,
            null, null, null, entry.Status, entry.StatusText, string.Empty, null);
}
