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


// Usings stay OUTSIDE the namespace in both gRPC files: inside
// BackendConfiguration.Pn.Services.GrpcServices the simple name "Grpc" would bind
// to BackendConfiguration.Pn.Grpc, and "ChemicalInventoryService" to the namespace
// of that name rather than the class.
using System;
using System.Linq;
using BackendConfiguration.Pn.Grpc.Chemicals;
using BackendConfiguration.Pn.Infrastructure.Models.Chemicals;
using BackendConfiguration.Pn.Services.ChemicalInventoryService;
using Google.Protobuf.WellKnownTypes;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;

namespace BackendConfiguration.Pn.Services.GrpcServices;

/// <summary>
/// Model ⇄ chemicals.proto. Base enums and proto enums share their numbers
/// (proto 0 = UNSPECIFIED = null), so enum casts are exact; every enum cast lives
/// in the enum helpers at the end of this class.
/// </summary>
internal static class ChemicalsProtoMapper
{
    public static ChemicalInventoryResponse ToProto(ChemicalInventoryModel model)
    {
        var response = new ChemicalInventoryResponse { SyncToken = model.SyncToken, Full = model.Full };
        response.Properties.AddRange(model.Properties.Select(ToProto));
        response.Locations.AddRange(model.Locations.Select(ToProto));
        response.Placements.AddRange(model.Placements.Select(ToProto));
        response.StockEntries.AddRange(model.StockEntries.Select(ToProto));
        response.RegisterEntries.AddRange(model.RegisterEntries.Select(ToProto));
        return response;
    }

    public static ChemicalPlacementChangeResponse ToProto(ChemicalPlacementChangeModel model)
    {
        var response = new ChemicalPlacementChangeResponse();
        response.Placements.AddRange(model.Placements.Select(ToProto));
        response.StockEntries.AddRange(model.StockEntries.Select(ToProto));
        response.RegisterEntries.AddRange(model.RegisterEntries.Select(ToProto));
        return response;
    }

    public static ChemicalRegisterSearchResponse ToProto(ChemicalRegisterPageModel page)
    {
        var response = new ChemicalRegisterSearchResponse { Total = page.Total };
        response.Entries.AddRange(page.Entries.Select(ToProto));
        return response;
    }

    public static ChemicalPropertyAccess ToProto(ChemicalPropertyAccessModel model) => new()
    {
        PropertyId = model.PropertyId, Name = model.Name, Permissions = ToProto(model.Permissions), StockEnabled = model.StockEnabled,
        CallerWorkerId = model.CallerWorkerId ?? 0,
    };

    public static ChemicalPermissionFlags ToProto(ChemicalPermissionFlagsModel flags) => new()
    {
        View = flags.View, Register = flags.Register, Remove = flags.Remove, Stock = flags.Stock,
        ManageLocations = flags.ManageLocations, Admin = flags.Admin,
    };

    public static ChemicalPermissionFlagsModel FromProto(ChemicalPermissionFlags flags) => flags == null
        ? ChemicalPermissionFlagsModel.None
        : new ChemicalPermissionFlagsModel(flags.View, flags.Register, flags.Remove, flags.Stock, flags.ManageLocations, flags.Admin);

    public static ChemicalLocationItem ToProto(ChemicalLocationModel model) => new()
    {
        Id = model.Id, PropertyId = model.PropertyId, Name = model.Name, Description = model.Description,
        PhotoFileName = model.PhotoFileName, SortOrder = model.SortOrder, Archived = model.Archived,
        UpdatedAt = Ts(model.UpdatedAt),
    };

    public static ChemicalPlacementItem ToProto(ChemicalPlacementModel model) => new()
    {
        Id = model.Id,
        LocationId = model.LocationId,
        PropertyId = model.PropertyId,
        ChemicalId = model.ChemicalId,
        ProductId = model.ProductId ?? 0,
        PlacementNote = model.PlacementNote,
        RegisteredByUserId = model.RegisteredByUserId,
        RegisteredByName = model.RegisteredByName,
        RegisteredAt = Ts(model.RegisteredAt),
        RemovedByUserId = model.RemovedByUserId ?? 0,
        RemovedByName = model.RemovedByName,
        RemovedAt = OptionalTs(model.RemovedAt),
        RemovalReason = ToProto(model.RemovalReason),
        RemovalNote = model.RemovalNote,
        MovedFromPlacementId = model.MovedFromPlacementId ?? 0,
        BalanceMilli = ChemicalQuantity.ToMilli(model.Balance),
        Unit = ToProto(model.Unit),
        UpdatedAt = Ts(model.UpdatedAt),
        WriteOffEntryId = model.WriteOffEntryId ?? 0,
    };

    public static ChemicalStockEntryItem ToProto(ChemicalStockEntryModel model) => new()
    {
        Id = model.Id,
        PlacementId = model.PlacementId,
        Kind = ToProto(model.Kind),
        ContainerSizeMilli = model.ContainerSize is { } size ? ChemicalQuantity.ToMilli(size) : 0,
        Unit = ToProto(model.Unit),
        AmountMilli = ChemicalQuantity.ToMilli(model.Amount),
        ContainerCount = model.ContainerCount ?? 0,
        BatchLot = model.BatchLot,
        Note = model.Note,
        ByUserId = model.ByUserId,
        ByName = model.ByName,
        At = Ts(model.At),
        BalanceAfterMilli = ChemicalQuantity.ToMilli(model.BalanceAfter),
        Origin = ToProto(model.Origin),
        CounterpartPlacementId = model.CounterpartPlacementId ?? 0,
    };

    public static ChemicalRegisterEntry ToProto(ChemicalRegisterEntryModel model)
    {
        var entry = new ChemicalRegisterEntry
        {
            ChemicalId = model.ChemicalId,
            Name = model.Name,
            RegistrationNo = model.RegistrationNo,
            Status = (ChemicalRegisterStatus)(model.Status ?? 0),
            StatusText = model.StatusText,
            SalesDeadline = OptionalTs(model.SalesDeadline),
            UseAndPossessionDeadline = OptionalTs(model.UseAndPossessionDeadline),
            AuthorisationDate = OptionalTs(model.AuthorisationDate),
            AuthorisationExpirationDate = OptionalTs(model.AuthorisationExpirationDate),
            AuthorisationTerminationDate = OptionalTs(model.AuthorisationTerminationDate),
            SignalWord = (ChemicalSignalWord)(model.SignalWord ?? 0),
            SignalWordText = model.SignalWordText,
            AuthorisationHolder = model.AuthorisationHolder,
            UpdatedAt = Ts(model.UpdatedAt),
        };
        entry.HazardPictograms.AddRange(model.HazardPictograms);
        entry.HazardStatements.AddRange(model.HazardStatements.Select(h => new ChemicalHazardStatement { Code = h.Code, Text = h.Text }));
        entry.ActiveSubstances.AddRange(model.ActiveSubstances.Select(a =>
            new ChemicalActiveSubstance { Name = a.Name, CasNo = a.CasNo, Concentration = a.Concentration }));
        entry.Products.AddRange(model.Products.Select(p => new ChemicalProductItem
        {
            ProductId = p.ProductId, Name = p.Name, Barcode = p.Barcode, SdsFileName = p.SdsFileName, SdsChecksum = p.SdsChecksum,
        }));
        return entry;
    }

    public static ChemicalWorkerPermissionEntry ToProto(ChemicalWorkerPermissionModel model) => new()
    {
        WorkerId = model.WorkerId, WorkerName = model.WorkerName, Flags = ToProto(model.Flags),
    };

    public static ChemicalSettings ToProto(ChemicalSettingsModel model)
    {
        var settings = new ChemicalSettings { PropertyId = model.PropertyId, StockEnabled = model.StockEnabled };
        settings.DigestRecipients.AddRange(model.DigestRecipients);
        return settings;
    }

    public static ChemicalStockAmountModel FromProto(ChemicalStockAmount amount) => amount == null
        ? null
        : new ChemicalStockAmountModel(
            // 0 = not given; anything else (negatives included) goes to ChemicalQuantity to be validated.
            amount.ContainerSizeMilli != 0 ? ChemicalQuantity.FromMilli(amount.ContainerSizeMilli) : null,
            amount.ContainerCount != 0 ? amount.ContainerCount : null,
            amount.HasAmountMilli ? ChemicalQuantity.FromMilli(amount.AmountMilli) : null,
            FromProto(amount.Unit),
            amount.BatchLot,
            amount.Note,
            FromProto(amount.At));

    public static int? OptionalId(int id) => id == 0 ? null : id;

    public static ChemicalLocationResponse ToLocationResponse(ChemicalLocationModel model) => new() { Location = ToProto(model) };

    /// <summary>UTC; a malformed or out-of-range timestamp is an ArgumentException (INVALID_ARGUMENT).</summary>
    public static DateTime? FromProto(Timestamp timestamp)
    {
        if (timestamp == null)
        {
            return null;
        }

        try
        {
            return timestamp.ToDateTime();
        }
        catch (InvalidOperationException e)
        {
            throw new ArgumentException("The timestamp is malformed or out of range.", e);
        }
    }

    // ---- enums: proto and base share their numbers; proto 0 (UNSPECIFIED) = null ----

    public static ChemicalRemovalReasonEnum FromProto(ChemicalRemovalReason reason) => (ChemicalRemovalReasonEnum)(int)reason;

    public static ChemicalStockEntryKindEnum FromProto(ChemicalStockEntryKind kind) => (ChemicalStockEntryKindEnum)(int)kind;

    public static ChemicalStockUnitEnum FromProto(ChemicalStockUnit unit) => (ChemicalStockUnitEnum)(int)unit;

    private static ChemicalRemovalReason ToProto(ChemicalRemovalReasonEnum? reason) => (ChemicalRemovalReason)(int)(reason ?? 0);

    private static ChemicalStockEntryKind ToProto(ChemicalStockEntryKindEnum kind) => (ChemicalStockEntryKind)(int)kind;

    private static ChemicalStockEntryOrigin ToProto(ChemicalStockEntryOriginEnum origin) => (ChemicalStockEntryOrigin)(int)origin;

    private static ChemicalStockUnit ToProto(ChemicalStockUnitEnum? unit) => (ChemicalStockUnit)(int)(unit ?? 0);

    private static Timestamp Ts(DateTime value) => Timestamp.FromDateTime(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static Timestamp OptionalTs(DateTime? value) => value.HasValue ? Ts(value.Value) : null;
}
