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

    [Test]
    public void FileChunks_CarryMetaBytesAndTrailer()
    {
        var oneof = ChemicalFileChunk.Descriptor.Oneofs.Single();
        Assert.That(oneof.Fields.Select(f => f.Name), Is.EqualTo(new[] { "meta", "chunk", "trailer" }));
    }
}
