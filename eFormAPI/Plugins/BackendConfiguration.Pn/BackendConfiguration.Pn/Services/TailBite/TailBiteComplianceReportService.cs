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
using BackendConfiguration.Pn.Infrastructure.Helpers;
using BackendConfiguration.Pn.Infrastructure.Models.ComplianceReport;
using BackendConfiguration.Pn.Services.GrpcServices;
using global::Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;

public interface ITailBiteComplianceReportService
{
    /// <summary>
    /// The Halebid section for the caller, or null when the caller sees none (no tail-bite identity, or no property in
    /// the filter where they are a manager and tail biting is enabled). Never throws TailBiteException.
    /// </summary>
    Task<TailBiteComplianceReport?> BuildAsync(ComplianceReportRequestModel request);
}

/// <summary>
/// The outbreaks open at some point in the report period <c>[DateFrom 00:00, DateTo+1 00:00)</c> Copenhagen time, on the
/// properties in the filter where the caller is a tail-bite manager and tail biting is enabled. BoardIds, TagIds, SiteIds and
/// Status do not apply.
/// </summary>
public class TailBiteComplianceReportService(BackendConfigurationPnDbContext db, ITailBiteAccess access,
    ITailBitePhotoQuery photoQuery, IGrpcSiteResolver siteResolver) : ITailBiteComplianceReportService
{
    private const string Removed = Constants.WorkflowStates.Removed;
    internal const string PathSeparator = " › ";
    private const string ListSeparator = ", ";

    private static DateTime Utc(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc);
    private static DateTime? Utc(DateTime? d) => d is { } v ? Utc(v) : null;

    public async Task<TailBiteComplianceReport?> BuildAsync(ComplianceReportRequestModel request)
    {
        int site;
        // RpcException: GrpcSiteResolver refuses an inactive or resigned account with Unauthenticated (ruling 10).
        try { site = await access.RequireCallerSiteAsync(); }
        catch (Exception e) when (e is TailBiteForbiddenException or RpcException) { return null; }

        var propertyIds = await ManagedEnabledPropertiesAsync(site, request.PropertyId);
        if (propertyIds.Count == 0) return null;

        var start = ComplianceFutureTaskGuard.UtcFromCopenhagen(request.DateFrom.Date);
        // An open-ended filter (DateTo at DateTime.MaxValue) has no next day to stop before.
        var end = request.DateTo.Date == DateTime.MaxValue.Date
            ? DateTime.MaxValue
            : ComplianceFutureTaskGuard.UtcFromCopenhagen(request.DateTo.Date.AddDays(1));
        var outbreaks = await db.TailBiteOutbreaks.AsNoTracking()
            .Where(o => propertyIds.Contains(o.PropertyId) && o.WorkflowState != Removed
                        && o.OpenedAt < end && (o.ClosedAt == null || o.ClosedAt >= start))
            .OrderBy(o => o.OpenedAt).ThenBy(o => o.Id)
            .ToListAsync();
        if (outbreaks.Count == 0) return new TailBiteComplianceReport([], new TailBiteReportTotals(0, 0, 0, 0, 0, 0));
        var outbreakIds = outbreaks.Select(o => o.Id).ToList();

        // Cancelled registrations stay linked for the audit trail but are neither listed nor counted.
        var rows = await TailBiteOutbreakRows.Of(db, outbreakIds)
            .Where(x => x.Registration.CancelledAt == null)
            .OrderBy(x => x.Registration.EffectiveAt).ThenBy(x => x.Registration.Id).ThenBy(x => x.Row.Id)
            .ToListAsync();
        var registrations = rows.Select(x => x.Registration).DistinctBy(r => r.Id).ToList();
        var registrationIds = registrations.Select(r => r.Id).ToList();

        var assessments = await db.TailBiteRiskAssessments.AsNoTracking()
            .Where(a => outbreakIds.Contains(a.OutbreakId) && a.WorkflowState != Removed)
            .ToDictionaryAsync(a => a.OutbreakId);
        var assessmentIds = assessments.Values.Select(a => a.Id).ToList();
        var actions = (await db.TailBiteAssessmentActions.AsNoTracking()
                .Where(a => assessmentIds.Contains(a.AssessmentId) && a.WithdrawnAt == null && a.WorkflowState != Removed)
                .OrderBy(a => a.FollowUpDate).ThenBy(a => a.Id)
                .ToListAsync())
            .ToLookup(a => a.AssessmentId);

        // Names for every type a registration names, deleted types included.
        var actionTypeNames = (await (from ra in db.TailBiteRegistrationActions.AsNoTracking()
                                      join t in db.TailBiteActionTypes on ra.ActionTypeId equals t.Id
                                      where registrationIds.Contains(ra.RegistrationId) && ra.WorkflowState != Removed
                                      orderby t.SortOrder, t.Id
                                      select new { ra.RegistrationId, t.Name }).ToListAsync())
            .ToLookup(x => x.RegistrationId, x => x.Name);

        var photos = (await photoQuery.ForRegistrationsAsync(registrations)).ToLookup(p => p.RegistrationId);

        // Removed locations included: an outbreak on a since-deleted pen keeps its name.
        var locations = await db.TailBiteLocations.AsNoTracking()
            .Where(l => propertyIds.Contains(l.PropertyId))
            .Select(l => new { l.Id, l.ParentId, l.Name })
            .ToDictionaryAsync(l => l.Id, l => (l.ParentId, l.Name));
        var propertyNames = await db.Properties.AsNoTracking()
            .Where(p => propertyIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name ?? "");

        var names = await siteResolver.DisplayNamesAsync(registrations.Select(r => r.SiteId)
            .Concat(actions.SelectMany(g => g).Select(a => a.ResponsibleSiteId))
            .Concat(actions.SelectMany(g => g).Where(a => a.DoneBySiteId != null).Select(a => a.DoneBySiteId!.Value)));

        string LocationName(int id) => locations.TryGetValue(id, out var l) ? l.Name : $"#{id}";

        // The location's ancestors below the root plus itself (ruling 5); bounded so a broken parent chain cannot loop.
        string LocationPath(int locationId)
        {
            var path = new List<string> { LocationName(locationId) };
            var parentId = locations.TryGetValue(locationId, out var self) ? self.ParentId : null;
            for (var hops = 0; parentId is { } id && locations.TryGetValue(id, out var parent) && hops < locations.Count; hops++)
            {
                if (parent.ParentId == null) break;
                path.Add(parent.Name);
                parentId = parent.ParentId;
            }
            path.Reverse();
            return string.Join(PathSeparator, path);
        }

        var rowsByOutbreak = rows.ToLookup(x => x.OutbreakId);
        var report = outbreaks.Select(o =>
        {
            // Rows arrive ordered by registration, then row: one entry per registration, pens in row order.
            var outbreakRegistrations = rowsByOutbreak[o.Id].GroupBy(x => x.Registration.Id).Select(g =>
            {
                var reg = g.First().Registration;
                return new TailBiteReportRegistration(reg.Id, Utc(reg.EffectiveAt),
                    string.Join(ListSeparator, g.Select(x => LocationName(x.Row.LocationId)).Distinct()),
                    g.Sum(x => x.Row.MinorCount), g.Sum(x => x.Row.SevereCount),
                    string.Join(ListSeparator, actionTypeNames[reg.Id]), names[reg.SiteId], photos[reg.Id].ToList());
            }).ToList();
            var assessment = assessments.GetValueOrDefault(o.Id);
            var outbreakActions = assessment is null
                ? []
                : actions[assessment.Id].Select(a => new TailBiteReportAction(a.Factor, a.Description, names[a.ResponsibleSiteId],
                    Utc(a.FollowUpDate), Utc(a.DoneAt), a.DoneBySiteId is { } doneBy ? names[doneBy] : null)).ToList();
            return new TailBiteReportOutbreak(o.Id, o.PropertyId, propertyNames.GetValueOrDefault(o.PropertyId, ""),
                LocationPath(o.LocationId), o.RuleVersion, Utc(o.OpenedAt), Utc(assessment?.AssessedAt), Utc(o.ClosedAt),
                outbreakRegistrations.Sum(r => r.Minor + r.Severe), outbreakRegistrations.Sum(r => r.Severe),
                assessment is null ? null : FactorAnswers.FromAssessment(assessment), outbreakActions, outbreakRegistrations);
        }).ToList();

        var totals = new TailBiteReportTotals(report.Count,
            report.Count(o => o.ClosedAt >= start && o.ClosedAt < end),
            report.Sum(o => o.BittenPigs), report.Sum(o => o.SeverePigs),
            report.Sum(o => o.Actions.Count(a => a.DoneAt != null)), report.Sum(o => o.Actions.Count));
        return new TailBiteComplianceReport(report, totals);
    }

    // PropertyId (or every property when null) ∩ tail biting enabled ∩ the caller a tail-bite manager there — one query;
    // the manager test is TailBiteAccess.IsManagerAsync's (an active PropertyWorker row with TailBiteManager).
    private Task<List<int>> ManagedEnabledPropertiesAsync(int site, int? propertyId) =>
        db.TailBiteProperties.AsNoTracking()
            .Where(t => t.Enabled && (propertyId == null || t.PropertyId == propertyId))
            .Where(t => db.PropertyWorkers.Any(pw => pw.PropertyId == t.PropertyId && pw.WorkerId == site
                                                     && pw.TailBiteManager && pw.WorkflowState != Removed))
            .Select(t => t.PropertyId).Distinct().OrderBy(id => id)
            .ToListAsync();
}
