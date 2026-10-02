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
using System.Net.Mail;
using System.Threading.Tasks;
using Infrastructure.Models.Chemicals;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;

public partial class ChemicalInventoryService
{
    public async Task<ChemicalSettingsModel> GetSettingsAsync(ChemicalCaller caller, int propertyId)
    {
        await permissions.RequireAsync(caller, propertyId, ChemicalPermission.Admin).ConfigureAwait(false);
        var settings = await dbContext.ChemicalPropertySettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.PropertyId == propertyId && s.WorkflowState != Removed).ConfigureAwait(false);
        return MapSettings(propertyId, settings);
    }

    public async Task<ChemicalSettingsModel> SetSettingsAsync(ChemicalCaller caller, ChemicalSetSettingsCommand command)
    {
        await permissions.RequireAsync(caller, command.PropertyId, ChemicalPermission.Admin).ConfigureAwait(false);
        var recipients = string.Join(",", NormalizeRecipients(command.DigestRecipients));

        // Unique on PropertyId including soft-deleted rows: upsert, never a second Create.
        var settings = await dbContext.ChemicalPropertySettings
            .FirstOrDefaultAsync(s => s.PropertyId == command.PropertyId).ConfigureAwait(false);
        if (settings == null)
        {
            settings = new ChemicalPropertySettings
            {
                PropertyId = command.PropertyId, StockEnabled = command.StockEnabled, DigestRecipients = recipients,
                CreatedByUserId = caller.UserId, UpdatedByUserId = caller.UserId,
            };
            await settings.Create(dbContext).ConfigureAwait(false);
        }
        else
        {
            settings.StockEnabled = command.StockEnabled;
            settings.DigestRecipients = recipients;
            settings.WorkflowState = Constants.WorkflowStates.Created;
            settings.UpdatedByUserId = caller.UserId;
            await settings.Update(dbContext).ConfigureAwait(false);
        }

        return MapSettings(command.PropertyId, settings);
    }

    public async Task<IReadOnlyList<ChemicalWorkerPermissionModel>> ListWorkerPermissionsAsync(ChemicalCaller caller, int propertyId)
    {
        await permissions.RequireAsync(caller, propertyId, ChemicalPermission.Admin).ConfigureAwait(false);
        return await ReadWorkerPermissionsAsync(propertyId).ConfigureAwait(false);
    }

    /// <summary>The worker permission list, without an authorisation check: callers authorise first.</summary>
    private async Task<IReadOnlyList<ChemicalWorkerPermissionModel>> ReadWorkerPermissionsAsync(int propertyId)
    {
        var workerIds = await AssignedWorkerIdsAsync(propertyId).ConfigureAwait(false);
        var stored = await dbContext.ChemicalWorkerPermissions.AsNoTracking()
            .Where(p => p.PropertyId == propertyId && p.WorkflowState != Removed)
            .ToDictionaryAsync(p => p.WorkerId).ConfigureAwait(false);
        var workerNames = await names.WorkerNamesAsync(workerIds).ConfigureAwait(false);

        return workerIds
            .Select(id => new ChemicalWorkerPermissionModel(
                id,
                workerNames.GetValueOrDefault(id, string.Empty),
                stored.TryGetValue(id, out var row) ? ChemicalPermissionService.ToFlags(row) : ChemicalPermissionFlagsModel.None))
            .OrderBy(w => w.WorkerName, StringComparer.CurrentCulture)
            .ThenBy(w => w.WorkerId)
            .ToList();
    }

    public async Task<IReadOnlyList<ChemicalWorkerPermissionModel>> SetWorkerPermissionsAsync(
        ChemicalCaller caller, int propertyId, IReadOnlyList<ChemicalSetWorkerPermissionCommand> changes)
    {
        await InTransactionAsync(async () =>
        {
            await permissions.RequireAsync(caller, propertyId, ChemicalPermission.Admin).ConfigureAwait(false);
            if (changes.Select(c => c.WorkerId).Distinct().Count() != changes.Count)
            {
                throw new ArgumentException("Each worker may appear only once.");
            }

            var assigned = (await AssignedWorkerIdsAsync(propertyId).ConfigureAwait(false)).ToHashSet();
            var unknown = changes.FirstOrDefault(c => !assigned.Contains(c.WorkerId));
            if (unknown != null)
            {
                throw new ArgumentException($"Worker {unknown.WorkerId} is not assigned to property {propertyId}.");
            }

            foreach (var change in changes)
            {
                // A missing flags object means no flags, as ChemicalsProtoMapper maps it for gRPC.
                var flags = change.Flags ?? ChemicalPermissionFlagsModel.None;

                // Unique on (PropertyId, WorkerId) including soft-deleted rows: upsert.
                var row = await dbContext.ChemicalWorkerPermissions
                    .FirstOrDefaultAsync(p => p.PropertyId == propertyId && p.WorkerId == change.WorkerId)
                    .ConfigureAwait(false);
                if (row == null)
                {
                    row = new ChemicalWorkerPermission
                    {
                        PropertyId = propertyId, WorkerId = change.WorkerId,
                        CreatedByUserId = caller.UserId, UpdatedByUserId = caller.UserId,
                    };
                    ChemicalPermissionService.Apply(row, flags);
                    await row.Create(dbContext).ConfigureAwait(false);
                    continue;
                }

                ChemicalPermissionService.Apply(row, flags);
                row.WorkflowState = Constants.WorkflowStates.Created;
                row.UpdatedByUserId = caller.UserId;
                await row.Update(dbContext).ConfigureAwait(false);
            }

            return true;
        }).ConfigureAwait(false);

        // Authorised inside the transaction above. Re-authorising here would turn
        // an admin's committed self-revocation into a PermissionDenied (#1363).
        return await ReadWorkerPermissionsAsync(propertyId).ConfigureAwait(false);
    }

    /// <summary>Trimmed, lower-cased, de-duplicated plain addresses ("Name &lt;a@b&gt;" and lists are refused).</summary>
    internal static IReadOnlyList<string> NormalizeRecipients(IEnumerable<string> raw)
    {
        var result = new List<string>();
        foreach (var candidate in raw ?? [])
        {
            var address = candidate?.Trim().ToLowerInvariant() ?? string.Empty;
            if (address.Length == 0)
            {
                continue;
            }

            if (address.Contains(',') || !MailAddress.TryCreate(address, out var parsed) || parsed.Address != address)
            {
                throw new ArgumentException($"\"{candidate}\" is not an e-mail address.");
            }

            if (!result.Contains(address))
            {
                result.Add(address);
            }
        }

        return result;
    }

    private async Task<List<int>> AssignedWorkerIdsAsync(int propertyId) =>
        await dbContext.PropertyWorkers.AsNoTracking()
            .Where(w => w.PropertyId == propertyId && w.WorkflowState != Removed)
            .Select(w => w.WorkerId)
            .Distinct()
            .ToListAsync().ConfigureAwait(false);

    private static ChemicalSettingsModel MapSettings(int propertyId, ChemicalPropertySettings settings) => new(
        propertyId,
        settings?.StockEnabled ?? false,
        (settings?.DigestRecipients ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
