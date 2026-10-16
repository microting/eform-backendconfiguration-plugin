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

namespace BackendConfiguration.Pn.Services.ChemicalReporting;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Infrastructure.Models.Chemicals;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using K = Infrastructure.Models.Chemicals.ChemicalLocalizationKeys;

/// <summary>The W6 "Detaljer" text for the Excel export (the web page formats the same fields in TypeScript).</summary>
public static class ChemicalHistoryDetailsFormatter
{
    public static string ActionKey(ChemicalHistoryActionEnum action) => $"ChemicalAction{action}";

    public static string Unit(ChemicalStockUnitEnum? unit) => unit switch
    {
        ChemicalStockUnitEnum.L => "L",
        ChemicalStockUnitEnum.Kg => "kg",
        _ => string.Empty,
    };

    public static string Number(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    public static string Format(ChemicalHistoryEventModel e, Func<string, string> text)
    {
        string Join(params string[] parts) => string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        string Amount(decimal? amount) => amount is { } a ? $"{Number(a)} {Unit(e.Unit)}" : string.Empty;

        return e.Action switch
        {
            ChemicalHistoryActionEnum.Registered => e.Note,
            ChemicalHistoryActionEnum.Moved => Join($"{e.LocationName} → {e.TargetLocationName}", Amount(e.Amount), e.Note),
            ChemicalHistoryActionEnum.Removed => Join($"{text(K.ChemicalColumnRemovalReason)}: {text(ReasonKey(e.RemovalReason))}", Amount(e.Amount), e.Note),
            ChemicalHistoryActionEnum.NoteChanged or ChemicalHistoryActionEnum.LocationRenamed => $"{e.OldValue} → {e.NewValue}",
            ChemicalHistoryActionEnum.StockReceived or ChemicalHistoryActionEnum.StockConsumed =>
                Join(Amount(e.Amount), $"{text(K.ChemicalBalance)} {Amount(e.BalanceAfter)}", e.Note),
            ChemicalHistoryActionEnum.StockAdjusted =>
                Join($"{text(K.ChemicalBalance)} {Number((e.BalanceAfter ?? 0) - (e.Amount ?? 0))} → {Number(e.BalanceAfter ?? 0)} {Unit(e.Unit)}", e.Note),
            ChemicalHistoryActionEnum.LocationCreated or ChemicalHistoryActionEnum.LocationArchived
                or ChemicalHistoryActionEnum.LocationPhotoChanged => e.LocationName,
            ChemicalHistoryActionEnum.PermissionChanged => $"{e.WorkerName}: " + string.Join(", ",
                e.PermissionsAdded.Select(f => "+" + text($"ChemicalFlag{f}"))
                    .Concat(e.PermissionsRemoved.Select(f => "−" + text($"ChemicalFlag{f}")))),
            ChemicalHistoryActionEnum.SettingsChanged => e.Field == "StockEnabled"
                ? text(e.NewValue == "true" ? K.ChemicalStockOn : K.ChemicalStockOff)
                : $"{text(K.ChemicalDigestRecipients)}: {e.NewValue}",
            ChemicalHistoryActionEnum.StatusChanged => e.StatusText,
            ChemicalHistoryActionEnum.AlertSent => Join(
                $"{text(ThresholdKey(e.Threshold))} ({text(DeadlineKey(e.DeadlineKind))})", $"push {e.PushCount}", $"e-mail {e.EmailCount}"),
            _ => string.Empty,
        };
    }

    public static string ReasonKey(ChemicalRemovalReasonEnum? reason) => reason switch
    {
        ChemicalRemovalReasonEnum.Used => K.ChemicalReasonUsed,
        ChemicalRemovalReasonEnum.Disposed => K.ChemicalReasonDisposed,
        _ => K.ChemicalReasonMoved,
    };

    private static string ThresholdKey(ChemicalAlertThresholdEnum? threshold) => threshold switch
    {
        ChemicalAlertThresholdEnum.SixMonths => K.ChemicalThresholdSixMonths,
        ChemicalAlertThresholdEnum.OneMonth => K.ChemicalThresholdOneMonth,
        ChemicalAlertThresholdEnum.Passed => K.ChemicalThresholdPassed,
        _ => K.ChemicalThresholdStatusChanged,
    };

    public static string DeadlineKey(ChemicalDeadlineKindEnum? kind) =>
        kind == ChemicalDeadlineKindEnum.Sales ? K.ChemicalDeadlineSales : K.ChemicalDeadlineUseAndPossession;
}
