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

using BackendConfiguration.Pn.Infrastructure.Models.Chemicals;
using BackendConfiguration.Pn.Services.BackendConfigurationAdhocService;
using BackendConfiguration.Pn.Services.ChemicalInventoryService;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using NSubstitute;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// Data helpers for the chemical inventory tests. Rows accumulate within a
/// fixture (see TestBaseSetup.ResetDatabasePerTest), so every helper creates
/// fresh, uniquely named rows and tests assert on the ids they created.
/// </summary>
public abstract class ChemicalTestBase : TestBaseSetup
{
    protected const int TestUserId = 1;

    protected readonly FakeAdhocPhotoStorage PhotoStorage = new();
    protected readonly FakeChemicalNameDirectory Names = new();
    protected IChemicalBaseClient ChemicalBase = Substitute.For<IChemicalBaseClient>();

    /// <summary>Real permission service and register reader over the fixture databases.</summary>
    protected ChemicalInventoryService CreateInventoryService(TimeProvider? time = null) => new(
        BackendConfigurationPnDbContext!,
        new ChemicalPermissionService(BackendConfigurationPnDbContext!),
        new ChemicalRegisterReader(ChemicalsDbContext!),
        Names,
        PhotoStorage,
        ChemicalBase,
        time ?? TimeProvider.System);

    protected async Task<Property> CreatePropertyAsync()
    {
        var property = new Property
        {
            Name = $"Property {Guid.NewGuid():N}", CreatedByUserId = TestUserId, UpdatedByUserId = TestUserId,
        };
        await property.Create(BackendConfigurationPnDbContext!);
        return property;
    }

    /// <summary>Assigns a worker (SDK site id) to the property and returns the id.</summary>
    protected async Task<int> AddWorkerAsync(int propertyId, int? workerId = null)
    {
        var id = workerId ?? Random.Shared.Next(100_000, 999_999);
        await new PropertyWorker
        {
            PropertyId = propertyId, WorkerId = id, CreatedByUserId = TestUserId, UpdatedByUserId = TestUserId,
        }.Create(BackendConfigurationPnDbContext!);
        return id;
    }

    protected async Task RemoveWorkerAsync(int propertyId, int workerId)
    {
        var rows = BackendConfigurationPnDbContext!.PropertyWorkers
            .Where(x => x.PropertyId == propertyId && x.WorkerId == workerId && x.WorkflowState != Constants.WorkflowStates.Removed)
            .ToList();
        foreach (var row in rows)
        {
            await row.Delete(BackendConfigurationPnDbContext);
        }
    }

    protected async Task GrantAsync(int propertyId, int workerId, ChemicalPermissionFlagsModel flags)
    {
        var row = BackendConfigurationPnDbContext!.ChemicalWorkerPermissions
            .SingleOrDefault(x => x.PropertyId == propertyId && x.WorkerId == workerId);
        if (row == null)
        {
            row = new ChemicalWorkerPermission
            {
                PropertyId = propertyId, WorkerId = workerId, CreatedByUserId = TestUserId, UpdatedByUserId = TestUserId,
            };
            Apply(row, flags);
            await row.Create(BackendConfigurationPnDbContext);
            return;
        }

        Apply(row, flags);
        await row.Update(BackendConfigurationPnDbContext);
    }

    protected async Task EnableStockAsync(int propertyId, bool enabled = true)
    {
        var row = BackendConfigurationPnDbContext!.ChemicalPropertySettings.SingleOrDefault(x => x.PropertyId == propertyId);
        if (row == null)
        {
            await new ChemicalPropertySettings
            {
                PropertyId = propertyId, StockEnabled = enabled, DigestRecipients = string.Empty,
                CreatedByUserId = TestUserId, UpdatedByUserId = TestUserId,
            }.Create(BackendConfigurationPnDbContext);
            return;
        }

        row.StockEnabled = enabled;
        await row.Update(BackendConfigurationPnDbContext);
    }

    protected async Task<ChemicalLocation> CreateLocationAsync(int propertyId, string? name = null, int sortOrder = 1)
    {
        var location = new ChemicalLocation
        {
            PropertyId = propertyId, Name = name ?? $"Location {Guid.NewGuid():N}", Description = string.Empty,
            SortOrder = sortOrder, CreatedByUserId = TestUserId, UpdatedByUserId = TestUserId,
        };
        await location.Create(BackendConfigurationPnDbContext!);
        return location;
    }

    protected async Task<ChemicalPlacement> CreatePlacementAsync(int locationId, int chemicalId, DateTime? removedAt = null)
    {
        var placement = new ChemicalPlacement
        {
            LocationId = locationId, ChemicalId = chemicalId, PlacementNote = string.Empty,
            RegisteredByUserId = TestUserId, RegisteredAt = DateTime.UtcNow.AddDays(-30),
            RemovedAt = removedAt, RemovedByUserId = removedAt.HasValue ? TestUserId : null,
            RemovalReason = removedAt.HasValue ? ChemicalRemovalReasonEnum.Used : null,
            CreatedByUserId = TestUserId, UpdatedByUserId = TestUserId,
        };
        await placement.Create(BackendConfigurationPnDbContext!);
        return placement;
    }

    private static void Apply(ChemicalWorkerPermission row, ChemicalPermissionFlagsModel flags)
    {
        row.View = flags.View;
        row.Register = flags.Register;
        row.Remove = flags.Remove;
        row.Stock = flags.Stock;
        row.ManageLocations = flags.ManageLocations;
        row.Admin = flags.Admin;
    }
}
