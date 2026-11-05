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
using System.Linq;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;

/// <summary>
/// Every Resources/localization.json key the chemical web code reads
/// (ChemicalsController, ChemicalDigestController, ChemicalReportsController and
/// the ChemicalReporting services). ChemicalLocalizationCompletenessTests ties this
/// list to the file; add a key here in the same change that first reads it.
/// </summary>
public static class ChemicalLocalizationKeys
{
    // Plan A ChemicalsController (flutter-chemistry Plan A Task 15).
    public const string ErrorWhileReadingChemicals = "ErrorWhileReadingChemicals";
    public const string ErrorWhileSearchingChemicalRegister = "ErrorWhileSearchingChemicalRegister";
    public const string ErrorWhileSavingChemicalPlacement = "ErrorWhileSavingChemicalPlacement";
    public const string ErrorWhileSavingChemicalStock = "ErrorWhileSavingChemicalStock";
    public const string ErrorWhileSavingChemicalLocation = "ErrorWhileSavingChemicalLocation";
    public const string ErrorWhileReadingChemicalPermissions = "ErrorWhileReadingChemicalPermissions";
    public const string ErrorWhileSavingChemicalPermissions = "ErrorWhileSavingChemicalPermissions";
    public const string ErrorWhileReadingChemicalSettings = "ErrorWhileReadingChemicalSettings";
    public const string ErrorWhileSavingChemicalSettings = "ErrorWhileSavingChemicalSettings";

    // ChemicalDigestController.
    public const string ErrorWhileReadingChemicalDigest = "ErrorWhileReadingChemicalDigest";

    public static readonly string[] ControllerErrors =
    [
        ErrorWhileReadingChemicals, ErrorWhileSearchingChemicalRegister, ErrorWhileSavingChemicalPlacement,
        ErrorWhileSavingChemicalStock, ErrorWhileSavingChemicalLocation, ErrorWhileReadingChemicalPermissions,
        ErrorWhileSavingChemicalPermissions, ErrorWhileReadingChemicalSettings, ErrorWhileSavingChemicalSettings,
        ErrorWhileReadingChemicalDigest,
    ];

    // Exports and history text (Plan E Tasks 10-11).
    public const string ErrorWhileReadingChemicalHistory = "ErrorWhileReadingChemicalHistory";
    public const string ErrorWhileExportingChemicals = "ErrorWhileExportingChemicals";
    public const string ChemicalAuthorityListTitle = "ChemicalAuthorityListTitle";
    public const string ChemicalInventoryExportTitle = "ChemicalInventoryExportTitle";
    public const string ChemicalRemovedPlacementsTitle = "ChemicalRemovedPlacementsTitle";
    public const string ChemicalHistoryTitle = "ChemicalHistoryTitle";
    public const string ChemicalPropertyLabel = "ChemicalPropertyLabel";
    public const string ChemicalGeneratedLabel = "ChemicalGeneratedLabel";
    public const string ChemicalColumnProduct = "ChemicalColumnProduct";
    public const string ChemicalColumnRegistrationNo = "ChemicalColumnRegistrationNo";
    public const string ChemicalColumnStatus = "ChemicalColumnStatus";
    public const string ChemicalColumnSignalWord = "ChemicalColumnSignalWord";
    public const string ChemicalColumnPictograms = "ChemicalColumnPictograms";
    public const string ChemicalColumnHazardStatements = "ChemicalColumnHazardStatements";
    public const string ChemicalColumnLocation = "ChemicalColumnLocation";
    public const string ChemicalColumnPlacement = "ChemicalColumnPlacement";
    public const string ChemicalColumnDeadlines = "ChemicalColumnDeadlines";
    public const string ChemicalColumnQuantity = "ChemicalColumnQuantity";
    public const string ChemicalColumnSds = "ChemicalColumnSds";
    public const string ChemicalColumnUnit = "ChemicalColumnUnit";
    public const string ChemicalColumnSalesDeadline = "ChemicalColumnSalesDeadline";
    public const string ChemicalColumnUseAndPossessionDeadline = "ChemicalColumnUseAndPossessionDeadline";
    public const string ChemicalColumnRequiresAction = "ChemicalColumnRequiresAction";
    public const string ChemicalColumnRegisteredBy = "ChemicalColumnRegisteredBy";
    public const string ChemicalColumnRegisteredAt = "ChemicalColumnRegisteredAt";
    public const string ChemicalColumnRemovalReason = "ChemicalColumnRemovalReason";
    public const string ChemicalColumnRemovedAt = "ChemicalColumnRemovedAt";
    public const string ChemicalColumnRemovedBy = "ChemicalColumnRemovedBy";
    public const string ChemicalColumnNote = "ChemicalColumnNote";
    public const string ChemicalColumnTime = "ChemicalColumnTime";
    public const string ChemicalColumnUser = "ChemicalColumnUser";
    public const string ChemicalColumnAction = "ChemicalColumnAction";
    public const string ChemicalColumnDetails = "ChemicalColumnDetails";
    public const string ChemicalYes = "ChemicalYes";
    public const string ChemicalNo = "ChemicalNo";
    public const string ChemicalSdsLink = "ChemicalSdsLink";
    public const string ChemicalSdsNote = "ChemicalSdsNote";
    public const string ChemicalSystemUser = "ChemicalSystemUser";
    public const string ChemicalBalance = "ChemicalBalance";
    public const string ChemicalStockOn = "ChemicalStockOn";
    public const string ChemicalStockOff = "ChemicalStockOff";
    public const string ChemicalDigestRecipients = "ChemicalDigestRecipients";
    public const string ChemicalDeadlineSales = "ChemicalDeadlineSales";
    public const string ChemicalDeadlineUseAndPossession = "ChemicalDeadlineUseAndPossession";
    public const string ChemicalReasonUsed = "ChemicalReasonUsed";
    public const string ChemicalReasonDisposed = "ChemicalReasonDisposed";
    public const string ChemicalReasonMoved = "ChemicalReasonMoved";
    public const string ChemicalThresholdSixMonths = "ChemicalThresholdSixMonths";
    public const string ChemicalThresholdOneMonth = "ChemicalThresholdOneMonth";
    public const string ChemicalThresholdPassed = "ChemicalThresholdPassed";
    public const string ChemicalThresholdStatusChanged = "ChemicalThresholdStatusChanged";
    public const string ChemicalFlagView = "ChemicalFlagView";
    public const string ChemicalFlagRegister = "ChemicalFlagRegister";
    public const string ChemicalFlagRemove = "ChemicalFlagRemove";
    public const string ChemicalFlagStock = "ChemicalFlagStock";
    public const string ChemicalFlagManageLocations = "ChemicalFlagManageLocations";
    public const string ChemicalFlagAdmin = "ChemicalFlagAdmin";

    /// <summary>One key per ChemicalHistoryActionEnum member: "ChemicalAction" + member name. Declared above Reporting: static initialisers run in textual order.</summary>
    public static readonly string[] HistoryActions = Enum.GetNames<ChemicalHistoryActionEnum>().Select(n => $"ChemicalAction{n}").ToArray();

    public static readonly string[] Reporting =
    [
        ErrorWhileReadingChemicalHistory, ErrorWhileExportingChemicals, ChemicalAuthorityListTitle,
        ChemicalInventoryExportTitle, ChemicalRemovedPlacementsTitle, ChemicalHistoryTitle, ChemicalPropertyLabel,
        ChemicalGeneratedLabel, ChemicalColumnProduct, ChemicalColumnRegistrationNo, ChemicalColumnStatus,
        ChemicalColumnSignalWord, ChemicalColumnPictograms, ChemicalColumnHazardStatements, ChemicalColumnLocation,
        ChemicalColumnPlacement, ChemicalColumnDeadlines, ChemicalColumnQuantity, ChemicalColumnSds,
        ChemicalColumnUnit, ChemicalColumnSalesDeadline, ChemicalColumnUseAndPossessionDeadline,
        ChemicalColumnRequiresAction, ChemicalColumnRegisteredBy, ChemicalColumnRegisteredAt,
        ChemicalColumnRemovalReason, ChemicalColumnRemovedAt, ChemicalColumnRemovedBy, ChemicalColumnNote,
        ChemicalColumnTime, ChemicalColumnUser, ChemicalColumnAction, ChemicalColumnDetails, ChemicalYes, ChemicalNo,
        ChemicalSdsLink, ChemicalSdsNote, ChemicalSystemUser, ChemicalBalance, ChemicalStockOn, ChemicalStockOff,
        ChemicalDigestRecipients, ChemicalDeadlineSales, ChemicalDeadlineUseAndPossession, ChemicalReasonUsed,
        ChemicalReasonDisposed, ChemicalReasonMoved, ChemicalThresholdSixMonths, ChemicalThresholdOneMonth,
        ChemicalThresholdPassed, ChemicalThresholdStatusChanged, ChemicalFlagView, ChemicalFlagRegister,
        ChemicalFlagRemove, ChemicalFlagStock, ChemicalFlagManageLocations, ChemicalFlagAdmin,
        .. HistoryActions,
    ];

    public static IReadOnlyList<string> All { get; } = [.. ControllerErrors, .. Reporting];
}
