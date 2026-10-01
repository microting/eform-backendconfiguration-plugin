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
/// Received/Consumed/initial stock: Amount, or ContainerSize × ContainerCount.
/// Adjusted: Amount is the newly counted balance.
/// </summary>
public sealed record ChemicalStockAmountModel(
    decimal? ContainerSize, int? ContainerCount, decimal? Amount, ChemicalStockUnitEnum Unit,
    string BatchLot, string Note, DateTime? At);

public sealed record ChemicalRegisterPlacementCommand(
    int LocationId, int ChemicalId, int? ProductId, string PlacementNote, ChemicalStockAmountModel InitialStock);

/// <summary>Amount null = move everything.</summary>
public sealed record ChemicalMovePlacementCommand(
    int PlacementId, int TargetLocationId, string TargetPlacementNote, decimal? Amount);

public sealed record ChemicalRemovePlacementCommand(
    int PlacementId, ChemicalRemovalReasonEnum Reason, DateTime? RemovedAt, string Note);

public sealed record ChemicalAddStockEntryCommand(
    int PlacementId, ChemicalStockEntryKindEnum Kind, ChemicalStockAmountModel Amount);

/// <summary>SortOrder null or &lt;= 0 = after the last location.</summary>
public sealed record ChemicalCreateLocationCommand(int PropertyId, string Name, string Description, int? SortOrder);

/// <summary>SortOrder null or &lt;= 0 = unchanged.</summary>
public sealed record ChemicalUpdateLocationCommand(int LocationId, string Name, string Description, int? SortOrder);

public sealed record ChemicalSetWorkerPermissionCommand(int WorkerId, ChemicalPermissionFlagsModel Flags);

public sealed record ChemicalSetSettingsCommand(int PropertyId, bool StockEnabled, IReadOnlyList<string> DigestRecipients);
