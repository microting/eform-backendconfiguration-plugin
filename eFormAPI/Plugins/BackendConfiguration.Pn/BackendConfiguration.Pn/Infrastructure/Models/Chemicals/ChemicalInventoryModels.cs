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

namespace BackendConfiguration.Pn.Infrastructure.Models.Chemicals;

using System;
using System.Collections.Generic;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;

/// <summary>
/// A property the caller may see, with EFFECTIVE flags (Admin expanded). CallerWorkerId is the
/// app caller's SDK worker id on the property; null for a web admin.
/// </summary>
public sealed record ChemicalPropertyAccessModel(
    int PropertyId, string Name, ChemicalPermissionFlagsModel Permissions, bool StockEnabled, int? CallerWorkerId);

public sealed record ChemicalLocationModel(
    int Id, int PropertyId, string Name, string Description, string PhotoFileName,
    int SortOrder, bool Archived, DateTime UpdatedAt);

public sealed record ChemicalPlacementModel(
    int Id, int LocationId, int PropertyId, int ChemicalId, int? ProductId, string PlacementNote,
    int RegisteredByUserId, string RegisteredByName, DateTime RegisteredAt,
    int? RemovedByUserId, string RemovedByName, DateTime? RemovedAt,
    ChemicalRemovalReasonEnum? RemovalReason, string RemovalNote, int? MovedFromPlacementId,
    decimal Balance, ChemicalStockUnitEnum? Unit, DateTime UpdatedAt, int? WriteOffEntryId);

/// <summary>
/// BalanceAfter: the placement's balance right after this entry, in write (id) order.
/// CounterpartPlacementId: the other placement of a move (MovedOut → created, MovedIn → source).
/// </summary>
public sealed record ChemicalStockEntryModel(
    int Id, int PlacementId, ChemicalStockEntryKindEnum Kind, decimal? ContainerSize, ChemicalStockUnitEnum Unit,
    decimal Amount, int? ContainerCount, string BatchLot, string Note, int ByUserId, string ByName, DateTime At,
    decimal BalanceAfter, ChemicalStockEntryOriginEnum Origin, int? CounterpartPlacementId);

/// <summary>Why the server wrote a stock entry; numbers match chemicals.proto ChemicalStockEntryOrigin.</summary>
public enum ChemicalStockEntryOriginEnum
{
    /// <summary>AddStockEntry, or initial stock on RegisterPlacement.</summary>
    Manual = 1,

    /// <summary>RemovePlacement zeroing the balance.</summary>
    RemovalWriteOff = 2,

    /// <summary>MovePlacement (MovedOut / MovedIn).</summary>
    Move = 3,
}

/// <summary>What a write returns: the touched placements, all their entries, their chemicals.</summary>
public sealed record ChemicalPlacementChangeModel(
    IReadOnlyList<ChemicalPlacementModel> Placements,
    IReadOnlyList<ChemicalStockEntryModel> StockEntries,
    IReadOnlyList<ChemicalRegisterEntryModel> RegisterEntries);

public sealed record ChemicalInventoryModel(
    IReadOnlyList<ChemicalPropertyAccessModel> Properties,
    IReadOnlyList<ChemicalLocationModel> Locations,
    IReadOnlyList<ChemicalPlacementModel> Placements,
    IReadOnlyList<ChemicalStockEntryModel> StockEntries,
    IReadOnlyList<ChemicalRegisterEntryModel> RegisterEntries,
    string SyncToken,
    bool Full);

public sealed record ChemicalSettingsModel(int PropertyId, bool StockEnabled, IReadOnlyList<string> DigestRecipients);
