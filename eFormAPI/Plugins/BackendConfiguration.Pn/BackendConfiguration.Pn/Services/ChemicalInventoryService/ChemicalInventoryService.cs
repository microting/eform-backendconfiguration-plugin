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
using System.Threading.Tasks;
using BackendConfigurationAdhocService;
using Infrastructure.Models.Chemicals;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;

/// <summary>
/// Shared state and helpers. The rules live in the partial files
/// ChemicalInventoryService.{Locations,Admin,Placements,Sync,Register}.cs.
/// Photos reuse IAdhocPhotoStorage (S3 or local disk by the SDK s3Enabled setting).
/// </summary>
public partial class ChemicalInventoryService(
    BackendConfigurationPnDbContext dbContext,
    IChemicalPermissionService permissions,
    IChemicalRegisterReader register,
    IChemicalNameDirectory names,
    IAdhocPhotoStorage fileStorage,
    IChemicalBaseClient chemicalBase,
    TimeProvider time) : IChemicalInventoryService
{
    internal const int MaxNoteLength = 1000;

    /// <summary>The one photo size limit; see EnsurePhotoSize.</summary>
    internal const int MaxPhotoBytes = 20 * 1024 * 1024;

    private const string Removed = Constants.WorkflowStates.Removed;

    /// <summary>Accepted photo content types and the file extension each is stored under.</summary>
    internal static readonly IReadOnlyDictionary<string, string> PhotoExtensions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["image/jpeg"] = "jpg",
            ["image/png"] = "png",
            ["image/heic"] = "heic",
        };

    private DateTime UtcNow() => time.GetUtcNow().UtcDateTime;

    private static DateTime Utc(DateTime? value) => DateTime.SpecifyKind(value ?? DateTime.MinValue, DateTimeKind.Utc);

    private static string RequireText(string raw, string what, int maxLength, bool required)
    {
        var text = raw?.Trim() ?? string.Empty;
        if (required && text.Length == 0)
        {
            throw new ArgumentException($"The {what} is required.");
        }

        if (text.Length > maxLength)
        {
            throw new ArgumentException($"The {what} has at most {maxLength} characters.");
        }

        return text;
    }

    private static void RequirePhoto(byte[] content, string contentType)
    {
        if (!PhotoExtensions.ContainsKey(contentType ?? string.Empty))
        {
            throw new ArgumentException("Photos must be image/jpeg, image/png or image/heic.");
        }

        EnsurePhotoSize(content?.Length ?? 0);
    }

    /// <summary>
    /// The one photo size rule: not empty, at most MaxPhotoBytes. Shared by this
    /// service and both adapters (ChemicalsController before the form file is read).
    /// </summary>
    internal static void EnsurePhotoSize(long length)
    {
        if (length <= 0)
        {
            throw new ArgumentException("The photo is empty.");
        }

        EnsurePhotoWithinLimit(length);
    }

    /// <summary>
    /// The upper half of <see cref="EnsurePhotoSize"/>, for a stream read so far
    /// (ChemicalsGrpcService refuses as soon as the running total is over).
    /// </summary>
    internal static void EnsurePhotoWithinLimit(long length)
    {
        if (length > MaxPhotoBytes)
        {
            throw new ArgumentException($"The photo exceeds {MaxPhotoBytes / (1024 * 1024)} MB.");
        }
    }

    /// <summary>
    /// One unit of work in a user transaction inside the execution strategy: the
    /// plugin's DbContext uses EnableRetryOnFailure, which refuses bare
    /// BeginTransaction (same pattern as GoogleDriveAuthService). A transient
    /// failure (e.g. a Galera certification failure on COMMIT) rolls the
    /// transaction back and the strategy runs <paramref name="work"/> again, so
    /// <paramref name="work"/> must be the WHOLE unit: it loads and validates
    /// everything it writes. Before a retry the change tracker is cleared, so the
    /// retry re-reads current rows instead of reusing entities that still hold the
    /// rolled-back values (PnBase.Update writes nothing for an entity it thinks is
    /// unchanged, which would half-apply a move or lose a grant).
    /// </summary>
    private async Task<T> InTransactionAsync<T>(Func<Task<T>> work)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        var attempt = 0;
        return await strategy.ExecuteAsync(async () =>
        {
            if (attempt++ > 0)
            {
                dbContext.ChangeTracker.Clear();
            }

            await using var transaction = await dbContext.Database.BeginTransactionAsync().ConfigureAwait(false);
            var result = await work().ConfigureAwait(false);
            await transaction.CommitAsync().ConfigureAwait(false);
            return result;
        }).ConfigureAwait(false);
    }

    private async Task<ChemicalLocation> LoadActiveLocationAsync(int locationId) =>
        await dbContext.ChemicalLocations
            .FirstOrDefaultAsync(l => l.Id == locationId && l.WorkflowState != Removed).ConfigureAwait(false)
        ?? throw new ChemicalNotFoundException($"Location {locationId} not found or archived.");

    private async Task RequireStockEnabledAsync(int propertyId)
    {
        var enabled = await dbContext.ChemicalPropertySettings
            .AnyAsync(s => s.PropertyId == propertyId && s.StockEnabled && s.WorkflowState != Removed)
            .ConfigureAwait(false);
        if (!enabled)
        {
            throw new ChemicalPreconditionException("Stock is not enabled on this property.");
        }
    }

    private static ChemicalLocationModel MapLocation(ChemicalLocation location) => new(
        location.Id, location.PropertyId, location.Name, location.Description ?? string.Empty,
        location.PhotoFileName ?? string.Empty, location.SortOrder, location.WorkflowState == Removed,
        Utc(location.UpdatedAt));
}
