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

public enum ChemicalHistoryActionEnum
{
    Registered = 1,
    Moved = 2,
    Removed = 3,
    NoteChanged = 4,
    StockReceived = 5,
    StockConsumed = 6,
    StockAdjusted = 7,
    LocationCreated = 8,
    LocationRenamed = 9,
    LocationArchived = 10,
    LocationPhotoChanged = 11,
    PermissionChanged = 12,
    SettingsChanged = 13,
    StatusChanged = 14,
    AlertSent = 15
}

/// <summary>W6 filter. From/To are dates only (no time of day), both inclusive, at most 366 days apart. UserId 0 = System.</summary>
public sealed record ChemicalHistoryQuery(
    DateTime From, DateTime To, int? UserId, ChemicalHistoryActionEnum? Action, int? LocationId, int Page = 0, int PageSize = 25);

/// <summary>
/// The W6 filter as the client sends it: From/To are exactly "yyyy-MM-dd" strings. A time, an
/// offset or "Z" is rejected here (a DateTime cannot carry an offset, so the model binder would
/// swallow it silently); ChemicalHistoryService re-checks the resulting dates.
/// </summary>
public sealed record ChemicalHistoryRequest(
    string From, string To, int? UserId, ChemicalHistoryActionEnum? Action, int? LocationId, int Page = 0, int PageSize = 25)
{
    private const string DateFormat = "yyyy-MM-dd";

    /// <exception cref="ArgumentException">From or To is not exactly yyyy-MM-dd.</exception>
    public ChemicalHistoryQuery ToQuery() =>
        new(ParseDate(From, nameof(From)), ParseDate(To, nameof(To)), UserId, Action, LocationId, Page, PageSize);

    private static DateTime ParseDate(string value, string field) =>
        DateTime.TryParseExact(value, DateFormat, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var date)
            ? date
            : throw new ArgumentException($"{field} must be a date formatted as yyyy-MM-dd.");
}

/// <summary>
/// One immutable history line. Which fields are set depends on Action:
/// stock: Amount (signed), Unit, BalanceAfter; Moved: Location → TargetLocation, Amount;
/// Removed: RemovalReason, Note, and for a stocked placement the written-off Amount (signed) and Unit;
/// NoteChanged/LocationRenamed/SettingsChanged: OldValue → NewValue
/// (SettingsChanged also Field = "StockEnabled" | "DigestRecipients"); PermissionChanged:
/// WorkerName, PermissionsAdded/Removed (flag names View, Register, Remove, Stock,
/// ManageLocations, Admin); StatusChanged/AlertSent: Status(Text), Threshold, DeadlineKind,
/// PushCount, EmailCount and UserId 0.
/// </summary>
public sealed record ChemicalHistoryEventModel
{
    public DateTime At { get; init; }
    public int UserId { get; init; }
    public string UserName { get; init; } = string.Empty;
    public ChemicalHistoryActionEnum Action { get; init; }
    public int? PlacementId { get; init; }
    public int? ChemicalId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string RegistrationNo { get; init; } = string.Empty;
    public int? LocationId { get; init; }
    public string LocationName { get; init; } = string.Empty;
    public int? TargetLocationId { get; init; }
    public string TargetLocationName { get; init; } = string.Empty;
    public decimal? Amount { get; init; }
    public ChemicalStockUnitEnum? Unit { get; init; }
    public decimal? BalanceAfter { get; init; }
    public ChemicalRemovalReasonEnum? RemovalReason { get; init; }
    public string Note { get; init; } = string.Empty;
    public string Field { get; init; } = string.Empty;
    public string OldValue { get; init; } = string.Empty;
    public string NewValue { get; init; } = string.Empty;
    public int? Status { get; init; }
    public string StatusText { get; init; } = string.Empty;
    public ChemicalAlertThresholdEnum? Threshold { get; init; }
    public ChemicalDeadlineKindEnum? DeadlineKind { get; init; }
    public int PushCount { get; init; }
    public int EmailCount { get; init; }
    public string WorkerName { get; init; } = string.Empty;
    public IReadOnlyList<string> PermissionsAdded { get; init; } = [];
    public IReadOnlyList<string> PermissionsRemoved { get; init; } = [];
}

public sealed record ChemicalHistoryUserModel(int UserId, string Name);

/// <summary>One page of events (newest first), the total after filtering, and every user seen in the period (for the W6 user filter).</summary>
public sealed record ChemicalHistoryPageModel(
    IReadOnlyList<ChemicalHistoryEventModel> Events, int Total, IReadOnlyList<ChemicalHistoryUserModel> Users);
