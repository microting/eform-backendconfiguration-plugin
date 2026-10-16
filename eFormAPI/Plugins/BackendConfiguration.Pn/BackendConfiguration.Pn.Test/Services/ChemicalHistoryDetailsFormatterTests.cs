using System;
using BackendConfiguration.Pn.Infrastructure.Models.Chemicals;
using BackendConfiguration.Pn.Services.ChemicalReporting;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Test.Services;

/// <summary>
/// E-13: the TypeScript history page (Plan E Task 15) formats the same fields. Every
/// expected string below is a vector the TS formatter must reproduce verbatim; the text
/// callback is a key passthrough ("&lt;Key&gt;") so only the structure is pinned.
/// </summary>
[TestFixture]
public class ChemicalHistoryDetailsFormatterTests
{
    private static string Key(string key) => $"<{key}>";

    private static string Format(ChemicalHistoryEventModel e) => ChemicalHistoryDetailsFormatter.Format(e, Key);

    [Test]
    public void EveryActionHasAKey()
    {
        foreach (var action in Enum.GetValues<ChemicalHistoryActionEnum>())
        {
            Assert.That(ChemicalHistoryDetailsFormatter.ActionKey(action), Is.EqualTo($"ChemicalAction{action}"));
        }
    }

    [Test]
    public void Moved() => Assert.That(Format(new ChemicalHistoryEventModel
    {
        Action = ChemicalHistoryActionEnum.Moved, LocationName = "Lade", TargetLocationName = "Kemirum",
        Amount = 5m, Unit = ChemicalStockUnitEnum.L, Note = "Reol 1",
    }), Is.EqualTo("Lade → Kemirum · 5 L · Reol 1"));

    [Test]
    public void Consumed() => Assert.That(Format(new ChemicalHistoryEventModel
    {
        Action = ChemicalHistoryActionEnum.StockConsumed, Amount = -0.5m, Unit = ChemicalStockUnitEnum.Kg, BalanceAfter = 3.5m,
    }), Is.EqualTo("-0.5 kg · <ChemicalBalance> 3.5 kg"));

    [Test]
    public void Received() => Assert.That(Format(new ChemicalHistoryEventModel
    {
        Action = ChemicalHistoryActionEnum.StockReceived, Amount = 10m, Unit = ChemicalStockUnitEnum.L, BalanceAfter = 12.25m, Note = "Levering",
    }), Is.EqualTo("10 L · <ChemicalBalance> 12.25 L · Levering"));

    [Test]
    public void Adjusted_ShowsTheCountBeforeAndAfter() => Assert.That(Format(new ChemicalHistoryEventModel
    {
        Action = ChemicalHistoryActionEnum.StockAdjusted, Amount = -0.5m, Unit = ChemicalStockUnitEnum.L, BalanceAfter = 8m,
    }), Is.EqualTo("<ChemicalBalance> 8.5 → 8 L"));

    [Test]
    public void Removed_WithoutAmount() => Assert.That(Format(new ChemicalHistoryEventModel
    {
        Action = ChemicalHistoryActionEnum.Removed, RemovalReason = ChemicalRemovalReasonEnum.Used, Note = "tom",
    }), Is.EqualTo("<ChemicalColumnRemovalReason>: <ChemicalReasonUsed> · tom"));

    [Test]
    public void Removed_WithTheFoldedWriteOffAmount() => Assert.That(Format(new ChemicalHistoryEventModel
    {
        Action = ChemicalHistoryActionEnum.Removed, RemovalReason = ChemicalRemovalReasonEnum.Disposed,
        Amount = -4m, Unit = ChemicalStockUnitEnum.L, Note = "tom",
    }), Is.EqualTo("<ChemicalColumnRemovalReason>: <ChemicalReasonDisposed> · -4 L · tom"));

    [Test]
    public void Removed_WithAmountAndNoNote() => Assert.That(Format(new ChemicalHistoryEventModel
    {
        Action = ChemicalHistoryActionEnum.Removed, RemovalReason = ChemicalRemovalReasonEnum.Used,
        Amount = -1.5m, Unit = ChemicalStockUnitEnum.Kg,
    }), Is.EqualTo("<ChemicalColumnRemovalReason>: <ChemicalReasonUsed> · -1.5 kg"));

    [Test]
    public void PermissionChanged() => Assert.That(Format(new ChemicalHistoryEventModel
    {
        Action = ChemicalHistoryActionEnum.PermissionChanged, WorkerName = "Ole Nielsen",
        PermissionsAdded = ["Register"], PermissionsRemoved = ["Stock"],
    }), Is.EqualTo("Ole Nielsen: +<ChemicalFlagRegister>, −<ChemicalFlagStock>"));

    [Test]
    public void StockSetting_On() => Assert.That(Format(new ChemicalHistoryEventModel
    {
        Action = ChemicalHistoryActionEnum.SettingsChanged, Field = "StockEnabled", OldValue = "false", NewValue = "true",
    }), Is.EqualTo("<ChemicalStockOn>"));

    [Test]
    public void StockSetting_Off() => Assert.That(Format(new ChemicalHistoryEventModel
    {
        Action = ChemicalHistoryActionEnum.SettingsChanged, Field = "StockEnabled", OldValue = "true", NewValue = "false",
    }), Is.EqualTo("<ChemicalStockOff>"));

    [Test]
    public void DigestRecipientsSetting() => Assert.That(Format(new ChemicalHistoryEventModel
    {
        Action = ChemicalHistoryActionEnum.SettingsChanged, Field = "DigestRecipients", OldValue = "", NewValue = "a@example.com",
    }), Is.EqualTo("<ChemicalDigestRecipients>: a@example.com"));

    [Test]
    public void AlertSent() => Assert.That(Format(new ChemicalHistoryEventModel
    {
        Action = ChemicalHistoryActionEnum.AlertSent, Threshold = ChemicalAlertThresholdEnum.Passed,
        DeadlineKind = ChemicalDeadlineKindEnum.UseAndPossession, PushCount = 2, EmailCount = 3,
    }), Is.EqualTo("<ChemicalThresholdPassed> (<ChemicalDeadlineUseAndPossession>) · push 2 · e-mail 3"));

    [Test]
    public void RenamedAndNote() => Assert.That(
        (Format(new ChemicalHistoryEventModel { Action = ChemicalHistoryActionEnum.LocationRenamed, OldValue = "Kemirum", NewValue = "Kemirum 2" }),
         Format(new ChemicalHistoryEventModel { Action = ChemicalHistoryActionEnum.StatusChanged, StatusText = "Produkt afmeldt" })),
        Is.EqualTo(("Kemirum → Kemirum 2", "Produkt afmeldt")));
}
