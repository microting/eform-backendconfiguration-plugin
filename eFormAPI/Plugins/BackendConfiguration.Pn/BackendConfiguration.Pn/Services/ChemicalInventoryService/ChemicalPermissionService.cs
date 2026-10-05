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

namespace BackendConfiguration.Pn.Services.ChemicalInventoryService;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Infrastructure.Models.Chemicals;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using UserPropertyAccess;

/// <summary>
/// Server-side enforcement of the chemical permissions (spec §8). A worker's
/// flags count only while the worker is still assigned to the property (an
/// active PropertyWorker row) and the property is not removed. Web admins
/// (WorkerId null) pass every check.
/// </summary>
public class ChemicalPermissionService(BackendConfigurationPnDbContext dbContext) : IChemicalPermissionService
{
    private const string Removed = Constants.WorkflowStates.Removed;

    /// <summary>AccessChangedAt when nothing is known to have changed; UTC like every other value.</summary>
    private static readonly DateTime NeverChanged = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);

    private readonly BackendConfigurationUserPropertyAccess _propertyAccess = new(dbContext);

    /// <summary>The single mapping from a stored permission row to its (unexpanded) flags.</summary>
    internal static ChemicalPermissionFlagsModel ToFlags(ChemicalWorkerPermission row) =>
        new(row.View, row.Register, row.Remove, row.Stock, row.ManageLocations, row.Admin);

    /// <summary>The single mapping from flags onto a stored permission row (the reverse of <see cref="ToFlags"/>).</summary>
    internal static void Apply(ChemicalWorkerPermission row, ChemicalPermissionFlagsModel flags)
    {
        row.View = flags.View;
        row.Register = flags.Register;
        row.Remove = flags.Remove;
        row.Stock = flags.Stock;
        row.ManageLocations = flags.ManageLocations;
        row.Admin = flags.Admin;
    }

    public async Task<IReadOnlyList<ChemicalPropertyAccessRow>> ListVisiblePropertiesAsync(ChemicalCaller caller)
    {
        var stockEnabled = await dbContext.ChemicalPropertySettings.AsNoTracking()
            .Where(s => s.WorkflowState != Removed)
            .ToDictionaryAsync(s => s.PropertyId, s => s.StockEnabled).ConfigureAwait(false);

        if (caller.IsWebAdmin)
        {
            var all = await dbContext.Properties.AsNoTracking()
                .Where(p => p.WorkflowState != Removed)
                .OrderBy(p => p.Name)
                .Select(p => new { p.Id, p.Name })
                .ToListAsync().ConfigureAwait(false);
            return all.Select(p => new ChemicalPropertyAccessRow(
                    new ChemicalPropertyAccessModel(p.Id, p.Name, ChemicalPermissionFlagsModel.All, stockEnabled.GetValueOrDefault(p.Id), null),
                    NeverChanged))
                .ToList();
        }

        // The join needs the PropertyWorker row itself (its UpdatedAt feeds
        // AccessChangedAt), so this cannot go through the UserPropertyAccess
        // helper; its predicate (same worker, same property, not removed) is
        // repeated here.
        var workerId = caller.WorkerId!.Value;
        var rows = await (
                from permission in dbContext.ChemicalWorkerPermissions.AsNoTracking()
                join worker in dbContext.PropertyWorkers.AsNoTracking()
                    on new { permission.PropertyId, permission.WorkerId } equals new { worker.PropertyId, worker.WorkerId }
                join property in dbContext.Properties.AsNoTracking() on permission.PropertyId equals property.Id
                where permission.WorkerId == workerId
                      && permission.WorkflowState != Removed
                      && worker.WorkflowState != Removed
                      && property.WorkflowState != Removed
                      && (permission.View || permission.Admin)
                select new { property.Id, property.Name, Permission = permission, WorkerUpdatedAt = worker.UpdatedAt })
            .ToListAsync().ConfigureAwait(false);

        // A worker can hold more than one active PropertyWorker row for the same property.
        return rows
            .GroupBy(r => r.Id)
            .Select(g => g.OrderByDescending(r => r.WorkerUpdatedAt).First())
            .OrderBy(r => r.Name)
            .Select(r => new ChemicalPropertyAccessRow(
                new ChemicalPropertyAccessModel(
                    r.Id, r.Name, ToFlags(r.Permission).Effective(), stockEnabled.GetValueOrDefault(r.Id), null),
                Later(r.Permission.UpdatedAt, r.WorkerUpdatedAt)))
            .ToList();
    }

    public async Task RequireAsync(ChemicalCaller caller, int propertyId, ChemicalPermission permission)
    {
        var propertyExists = await dbContext.Properties
            .AnyAsync(p => p.Id == propertyId && p.WorkflowState != Removed).ConfigureAwait(false);
        if (!propertyExists)
        {
            throw new ChemicalNotFoundException($"Property {propertyId} not found.");
        }

        if (caller.IsWebAdmin)
        {
            return;
        }

        var flags = await StoredFlagsAsync(caller.WorkerId!.Value, propertyId).ConfigureAwait(false);
        if (!flags.Allows(permission))
        {
            throw new ChemicalPermissionDeniedException(
                $"The chemical permission {permission} is missing on property {propertyId}.");
        }
    }

    public async Task RequireViewOnAnyPropertyAsync(ChemicalCaller caller)
    {
        if (caller.IsWebAdmin)
        {
            return;
        }

        var visible = await ListVisiblePropertiesAsync(caller).ConfigureAwait(false);
        if (visible.Count == 0)
        {
            throw new ChemicalPermissionDeniedException("No property grants the chemical View permission.");
        }
    }

    private async Task<ChemicalPermissionFlagsModel> StoredFlagsAsync(int workerId, int propertyId)
    {
        if (!await _propertyAccess.HasAccessAsync(workerId, propertyId).ConfigureAwait(false))
        {
            return ChemicalPermissionFlagsModel.None;
        }

        var row = await dbContext.ChemicalWorkerPermissions.AsNoTracking()
            .FirstOrDefaultAsync(p => p.PropertyId == propertyId && p.WorkerId == workerId && p.WorkflowState != Removed)
            .ConfigureAwait(false);
        return row == null ? ChemicalPermissionFlagsModel.None : ToFlags(row);
    }

    private static DateTime Later(DateTime? first, DateTime? second)
    {
        var a = first ?? NeverChanged;
        var b = second ?? NeverChanged;
        return DateTime.SpecifyKind(a > b ? a : b, DateTimeKind.Utc);
    }
}
