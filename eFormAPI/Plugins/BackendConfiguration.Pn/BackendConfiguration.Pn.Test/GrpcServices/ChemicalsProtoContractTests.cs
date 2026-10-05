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

using System.Linq;
using BackendConfiguration.Pn.Grpc.Chemicals;
using Google.Protobuf.Reflection;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Test.GrpcServices;

/// <summary>
/// Pins chemicals.proto, the contract flutter-chemistry (Plan B), the alert
/// jobs (Plan D), the web (Plan E) and the curation plugin (Plan C) copy.
/// A failing test here means a wire-breaking change: coordinate every plan
/// before changing the expectation.
/// </summary>
[TestFixture]
public class ChemicalsProtoContractTests
{
    private static readonly (string Name, bool ClientStreaming, bool ServerStreaming)[] ExpectedMethods =
    [
        ("GetMyInventory", false, false),
        ("GetSdsPdf", false, true),
        ("GetLocationPhoto", false, true),
        ("LookupBarcode", false, false),
        ("SearchRegister", false, false),
        ("RegisterPlacement", false, false),
        ("MovePlacement", false, false),
        ("RemovePlacement", false, false),
        ("UpdatePlacementNote", false, false),
        ("AddStockEntry", false, false),
        ("CreateLocation", false, false),
        ("UpdateLocation", false, false),
        ("ArchiveLocation", false, false),
        ("ReorderLocations", false, false),
        ("UploadLocationPhoto", true, false),
        ("SuggestBarcode", true, false),
        ("ListWorkerPermissions", false, false),
        ("SetWorkerPermission", false, false),
        ("GetPropertySettings", false, false),
        ("SetPropertySettings", false, false),
    ];

    [Test]
    public void Service_LivesInTheBackendConfigurationPackage()
    {
        Assert.That(ChemicalsGrpc.Descriptor.FullName, Is.EqualTo("backend_configuration.ChemicalsGrpc"));
    }

    [Test]
    public void Service_ExposesExactlyTheSpecRpcs()
    {
        var actual = ChemicalsGrpc.Descriptor.Methods
            .Select(m => (m.Name, m.IsClientStreaming, m.IsServerStreaming))
            .ToArray();

        Assert.That(actual, Is.EquivalentTo(ExpectedMethods));
    }

    [Test]
    public void EveryMessageAndEnum_CarriesTheChemicalPrefix()
    {
        // All files in Protos/ share `package backend_configuration`; the prefix
        // is what keeps these names from colliding with adhoc.proto/events.proto.
        var names = ChemicalsReflection.Descriptor.MessageTypes.Select(m => m.Name)
            .Concat(ChemicalsReflection.Descriptor.EnumTypes.Select(e => e.Name));

        Assert.That(names, Has.All.StartsWith("Chemical"));
    }

    [Test]
    public void EveryEnum_StartsWithAnUnspecifiedZero()
    {
        foreach (var enumType in ChemicalsReflection.Descriptor.EnumTypes)
        {
            var first = enumType.Values[0];
            Assert.That(first.Number, Is.EqualTo(0), enumType.Name);
            Assert.That(first.Name, Does.EndWith("_UNSPECIFIED"), enumType.Name);
        }
    }

    [Test]
    public void Quantities_AreInt64Thousandths()
    {
        var amount = ChemicalStockAmount.Descriptor;
        Assert.That(amount.FindFieldByName("amount_milli").FieldType, Is.EqualTo(FieldType.Int64));
        Assert.That(amount.FindFieldByName("amount_milli").HasPresence, Is.True,
            "ADJUSTED sends a counted balance of 0, so presence must be tracked");
        Assert.That(amount.FindFieldByName("container_size_milli").FieldType, Is.EqualTo(FieldType.Int64));
        Assert.That(ChemicalPlacementItem.Descriptor.FindFieldByName("balance_milli").FieldType,
            Is.EqualTo(FieldType.Int64));
    }

    // The 2026-10-05 additive change: older apps keep working because nothing
    // existing was renamed or renumbered, and the new fields use new numbers.

    [Test]
    public void StockEntryItem_KeepsItsFields_AndAddsBalanceOriginAndCounterpart()
    {
        var fields = ChemicalStockEntryItem.Descriptor.Fields.InFieldNumberOrder().Select(f => (f.FieldNumber, f.Name));

        Assert.That(fields, Is.EqualTo(new[]
        {
            (1, "id"), (2, "placement_id"), (3, "kind"), (4, "container_size_milli"), (5, "unit"), (6, "amount_milli"),
            (7, "container_count"), (8, "batch_lot"), (9, "note"), (10, "by_user_id"), (11, "by_name"), (12, "at"),
            (13, "balance_after_milli"), (14, "origin"), (15, "counterpart_placement_id"),
        }));
        var balanceAfter = ChemicalStockEntryItem.Descriptor.FindFieldByName("balance_after_milli");
        Assert.That(balanceAfter.FieldType, Is.EqualTo(FieldType.Int64));
        Assert.That(balanceAfter.HasPresence, Is.True, "unset tells the app it talks to an older server");
        Assert.That(ChemicalStockEntryItem.Descriptor.FindFieldByName("origin").EnumType.Name, Is.EqualTo("ChemicalStockEntryOrigin"));
        Assert.That(ChemicalStockEntryItem.Descriptor.FindFieldByName("counterpart_placement_id").FieldType, Is.EqualTo(FieldType.Int32));
    }

    [Test]
    public void PlacementItem_KeepsItsFields_AndAddsTheWriteOffEntryId()
    {
        var fields = ChemicalPlacementItem.Descriptor.Fields.InFieldNumberOrder().Select(f => (f.FieldNumber, f.Name));

        Assert.That(fields, Is.EqualTo(new[]
        {
            (1, "id"), (2, "location_id"), (3, "property_id"), (4, "chemical_id"), (5, "product_id"), (6, "placement_note"),
            (7, "registered_by_user_id"), (8, "registered_by_name"), (9, "registered_at"), (10, "removed_by_user_id"),
            (11, "removed_by_name"), (12, "removed_at"), (13, "removal_reason"), (14, "removal_note"),
            (15, "moved_from_placement_id"), (16, "balance_milli"), (17, "unit"), (18, "updated_at"), (19, "write_off_entry_id"),
        }));
        Assert.That(ChemicalPlacementItem.Descriptor.FindFieldByName("write_off_entry_id").FieldType, Is.EqualTo(FieldType.Int32));
    }

    [Test]
    public void PropertyAccess_KeepsItsFields_AndAddsTheCallerWorkerId()
    {
        var fields = ChemicalPropertyAccess.Descriptor.Fields.InFieldNumberOrder().Select(f => (f.FieldNumber, f.Name, f.FieldType));

        Assert.That(fields, Is.EqualTo(new[]
        {
            (1, "property_id", FieldType.Int32), (2, "name", FieldType.String), (3, "permissions", FieldType.Message),
            (4, "stock_enabled", FieldType.Bool), (5, "caller_worker_id", FieldType.Int32),
        }));
    }

    [Test]
    public void StockEntryOrigin_HasTheAgreedNumbers()
    {
        var values = ChemicalsReflection.Descriptor.EnumTypes.Single(e => e.Name == "ChemicalStockEntryOrigin").Values.Select(v => (v.Number, v.Name));

        Assert.That(values, Is.EqualTo(new[]
        {
            (0, "CHEMICAL_STOCK_ENTRY_ORIGIN_UNSPECIFIED"), (1, "CHEMICAL_STOCK_ENTRY_ORIGIN_MANUAL"),
            (2, "CHEMICAL_STOCK_ENTRY_ORIGIN_REMOVAL_WRITE_OFF"), (3, "CHEMICAL_STOCK_ENTRY_ORIGIN_MOVE"),
        }));
    }

    [Test]
    public void ReorderLocations_TakesThePropertyAndTheOrderedIds_AndReturnsLocations()
    {
        var method = ChemicalsGrpc.Descriptor.FindMethodByName("ReorderLocations");
        var request = method.InputType;

        Assert.That(request.Name, Is.EqualTo("ChemicalReorderLocationsRequest"));
        Assert.That(request.Fields.InFieldNumberOrder().Select(f => (f.FieldNumber, f.Name, f.IsRepeated)),
            Is.EqualTo(new[] { (1, "property_id", false), (2, "location_ids", true) }));
        var locations = method.OutputType.FindFieldByNumber(1);
        Assert.That((method.OutputType.Name, locations.Name, locations.IsRepeated, locations.MessageType.Name),
            Is.EqualTo(("ChemicalReorderLocationsResponse", "locations", true, "ChemicalLocationItem")));
    }

    [Test]
    public void FileChunks_CarryMetaBytesAndTrailer()
    {
        var oneof = ChemicalFileChunk.Descriptor.Oneofs.Single();
        Assert.That(oneof.Fields.Select(f => f.Name), Is.EqualTo(new[] { "meta", "chunk", "trailer" }));
    }
}
