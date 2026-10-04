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

using System;
using System.Linq;
using BackendConfiguration.Pn.Infrastructure.Models.Chemicals;
using BackendConfiguration.Pn.Services.ChemicalInventoryService;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Test.Services;

[TestFixture]
public class ChemicalPrimitivesTests
{
    // ---- permission flags (spec §8: Admin implies every flag) ----

    [TestCase(ChemicalPermission.View)]
    [TestCase(ChemicalPermission.Register)]
    [TestCase(ChemicalPermission.Remove)]
    [TestCase(ChemicalPermission.Stock)]
    [TestCase(ChemicalPermission.ManageLocations)]
    [TestCase(ChemicalPermission.Admin)]
    public void AdminFlag_AllowsEveryPermission(ChemicalPermission permission)
    {
        var flags = ChemicalPermissionFlagsModel.None with { Admin = true };
        Assert.That(flags.Allows(permission), Is.True);
    }

    [Test]
    public void ViewOnly_AllowsViewAndNothingElse()
    {
        var flags = ChemicalPermissionFlagsModel.None with { View = true };

        Assert.That(flags.Allows(ChemicalPermission.View), Is.True);
        Assert.That(flags.Allows(ChemicalPermission.Register), Is.False);
        Assert.That(flags.Allows(ChemicalPermission.Admin), Is.False);
        Assert.That(flags.Effective(), Is.EqualTo(flags));
    }

    [Test]
    public void Effective_ExpandsAdmin()
    {
        Assert.That((ChemicalPermissionFlagsModel.None with { Admin = true }).Effective(), Is.EqualTo(ChemicalPermissionFlagsModel.All));
    }

    // ---- caller ----

    [Test]
    public void Caller_WebAdminHasNoWorker()
    {
        Assert.That(ChemicalCaller.Web(5).IsWebAdmin, Is.True);
        Assert.That(ChemicalCaller.App(5, 42).IsWebAdmin, Is.False);
    }

    // ---- quantities ----

    [Test]
    public void ResolveMovedAmount_ExplicitAmountWins()
    {
        var amount = new ChemicalStockAmountModel(0.5m, 4, 1.25m, ChemicalStockUnitEnum.L, null, null, null);
        Assert.That(ChemicalQuantity.ResolveMovedAmount(amount), Is.EqualTo(1.25m));
    }

    [Test]
    public void ResolveMovedAmount_ContainerSizeTimesCount()
    {
        var amount = new ChemicalStockAmountModel(0.5m, 4, null, ChemicalStockUnitEnum.Kg, null, null, null);
        Assert.That(ChemicalQuantity.ResolveMovedAmount(amount), Is.EqualTo(2.0m));
    }

    [TestCase("0")]
    [TestCase("-1")]
    [TestCase("1.0005")]
    [TestCase("1000000.001")]
    public void ResolveMovedAmount_RejectsZeroNegativeTooPreciseOrHuge(string raw)
    {
        var amount = new ChemicalStockAmountModel(null, null, decimal.Parse(raw, System.Globalization.CultureInfo.InvariantCulture),
            ChemicalStockUnitEnum.L, null, null, null);
        Assert.That(() => ChemicalQuantity.ResolveMovedAmount(amount), Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public void ResolveMovedAmount_RequiresAUnit()
    {
        var amount = new ChemicalStockAmountModel(null, null, 1m, UndefinedUnit, null, null, null);
        Assert.That(() => ChemicalQuantity.ResolveMovedAmount(amount), Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public void ResolveMovedAmount_WithoutAmountOrContainers_Throws()
    {
        var amount = new ChemicalStockAmountModel(0.5m, null, null, ChemicalStockUnitEnum.L, null, null, null);
        Assert.That(() => ChemicalQuantity.ResolveMovedAmount(amount), Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public void ResolveCountedBalance_AllowsZero()
    {
        var amount = new ChemicalStockAmountModel(null, null, 0m, ChemicalStockUnitEnum.L, null, null, null);
        Assert.That(ChemicalQuantity.ResolveCountedBalance(amount), Is.EqualTo(0m));
    }

    [TestCase("2.5", 2500L)]
    [TestCase("-1.125", -1125L)]
    [TestCase("0.001", 1L)]
    public void Milli_RoundTrips(string raw, long milli)
    {
        var value = decimal.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
        Assert.That(ChemicalQuantity.ToMilli(value), Is.EqualTo(milli));
        Assert.That(ChemicalQuantity.FromMilli(milli), Is.EqualTo(value));
    }

    private static readonly ChemicalStockUnitEnum UndefinedUnit =
        (ChemicalStockUnitEnum)(Enum.GetValues<ChemicalStockUnitEnum>().Max(u => (int)u) + 1);

    [Test]
    public void RequireUnit_RejectsUndefinedUnit()
    {
        Assert.That(() => ChemicalQuantity.RequireUnit(UndefinedUnit), Throws.InstanceOf<ArgumentException>());
        Assert.That(() => ChemicalQuantity.RequireUnit(ChemicalStockUnitEnum.L), Throws.Nothing);
    }

    [TestCase("0")]
    [TestCase("-1")]
    [TestCase("1.0005")]
    [TestCase("1000000.001")]
    public void RequireMoveAmount_RejectsInvalid(string raw)
    {
        var value = decimal.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
        Assert.That(() => ChemicalQuantity.RequireMoveAmount(value), Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public void RequireMoveAmount_ReturnsValidAmount()
    {
        Assert.That(ChemicalQuantity.RequireMoveAmount(1.5m), Is.EqualTo(1.5m));
    }

    [TestCase(0, 2)]
    [TestCase(-1, 2)]
    [TestCase(1, 0)]
    [TestCase(1, -3)]
    public void ResolveMovedAmount_RejectsNonPositiveContainerSizeOrCount(int size, int count)
    {
        var amount = new ChemicalStockAmountModel(size, count, null, ChemicalStockUnitEnum.L, null, null, null);
        Assert.That(() => ChemicalQuantity.ResolveMovedAmount(amount), Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public void ResolveMovedAmount_NullAmount_Throws()
    {
        Assert.That(() => ChemicalQuantity.ResolveMovedAmount(null), Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public void ResolveCountedBalance_RejectsNegativeNullAndMissingAmount()
    {
        var negative = new ChemicalStockAmountModel(null, null, -0.5m, ChemicalStockUnitEnum.L, null, null, null);
        var missing = new ChemicalStockAmountModel(null, null, null, ChemicalStockUnitEnum.L, null, null, null);
        Assert.That(() => ChemicalQuantity.ResolveCountedBalance(negative), Throws.InstanceOf<ArgumentException>());
        Assert.That(() => ChemicalQuantity.ResolveCountedBalance(missing), Throws.InstanceOf<ArgumentException>());
        Assert.That(() => ChemicalQuantity.ResolveCountedBalance(null), Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public void ToMilli_RoundsHalfAwayFromZero()
    {
        Assert.That(ChemicalQuantity.ToMilli(0.0005m), Is.EqualTo(1L));
        Assert.That(ChemicalQuantity.ToMilli(-0.0005m), Is.EqualTo(-1L));
    }

    [Test]
    public void Allows_UndefinedPermission_IsFalse()
    {
        Assert.That(ChemicalPermissionFlagsModel.All.Allows((ChemicalPermission)99), Is.False);
    }

    // ---- barcodes (Review Focus 5) ----

    // Candidates and GS1 spellings: Gs1Tests.

    [Test]
    public void Normalize_TrimsWhitespace()
    {
        Assert.That(ChemicalBarcode.Normalize(" 5701234567899 "), Is.EqualTo("5701234567899"));
    }

    [TestCase("")]
    [TestCase("12345")]
    [TestCase("123456789012345")]
    [TestCase("57012A4567892")]
    [TestCase("5701 234567892")]
    [TestCase("00000000")]
    public void Normalize_RejectsNonBarcodes(string raw)
    {
        Assert.That(() => ChemicalBarcode.Normalize(raw), Throws.InstanceOf<ArgumentException>());
    }

    // ---- sync token (Review Focus 1) ----

    private static readonly DateTime Now = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public void SyncToken_RoundTripsWithOverlap()
    {
        var token = ChemicalSyncToken.Create(Now);
        Assert.That(ChemicalSyncToken.Parse(token, Now), Is.EqualTo(Now - ChemicalSyncToken.Overlap));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("garbage")]
    [TestCase("v1:")]
    [TestCase("v1:abc")]
    [TestCase("v2:638000000000000000")]
    [TestCase("v1:-5")]
    public void SyncToken_UnusableTokens_MeanFullLoad(string token)
    {
        Assert.That(ChemicalSyncToken.Parse(token, Now), Is.Null);
    }

    [Test]
    public void SyncToken_ExactlyNow_IsAccepted()
    {
        var token = "v1:" + Now.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Assert.That(ChemicalSyncToken.Parse(token, Now), Is.EqualTo(Now));
    }

    [Test]
    public void SyncToken_OneTickInTheFuture_MeansFullLoad()
    {
        var token = "v1:" + (Now.Ticks + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        Assert.That(ChemicalSyncToken.Parse(token, Now), Is.Null);
    }

    [Test]
    public void SyncToken_FromTheFuture_MeansFullLoad()
    {
        var future = ChemicalSyncToken.Create(Now.AddDays(1));
        Assert.That(ChemicalSyncToken.Parse(future, Now), Is.Null);
    }

    // ---- BMD hazard statement texts: "<text> (<code>)" ----

    [TestCase("Ustabilt eksplosiv (H200)", "H200", "Ustabilt eksplosiv")]
    [TestCase("Kan skade forplantningsevnen (H360Fd)", "H360Fd", "Kan skade forplantningsevnen")]
    [TestCase("Eksplosiv i tør tilstand (EUH 001)", "EUH001", "Eksplosiv i tør tilstand")]
    [TestCase("Text without a code", "", "Text without a code")]
    public void SplitHazardStatement_SeparatesCodeFromText(string bmdText, string code, string text)
    {
        var statement = ChemicalRegisterReader.SplitHazardStatement(bmdText);
        Assert.That(statement.Code, Is.EqualTo(code));
        Assert.That(statement.Text, Is.EqualTo(text));
    }
}
