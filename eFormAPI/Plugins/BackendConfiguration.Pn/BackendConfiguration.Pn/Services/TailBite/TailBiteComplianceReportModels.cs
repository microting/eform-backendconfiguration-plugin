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

#nullable enable

namespace BackendConfiguration.Pn.Services.TailBite;

using System;
using System.Collections.Generic;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;

// The Halebid section of the compliance report's Rapport (#1517). Every DateTime is UTC with DateTimeKind.Utc; the web
// formats in the browser's zone, the export converts with ComplianceFutureTaskGuard.CopenhagenTimeOf.

/// <summary>A live (not withdrawn) action of the outbreak's risk assessment.</summary>
public sealed record TailBiteReportAction(TailBiteFactor Factor, string Description, string ResponsibleName,
    DateTime FollowUpDate, DateTime? DoneAt, string? DoneByName);

/// <summary>
/// A non-cancelled registration behind the outbreak, once however many pens it covers: Pens are its linked rows' location
/// names in row order joined ", ", Minor/Severe the sums over those rows, ActionTypes the type names joined ", " (empty = none).
/// </summary>
public sealed record TailBiteReportRegistration(int Id, DateTime EffectiveAt, string Pens, int Minor, int Severe,
    string ActionTypes, string RegisteredByName, IReadOnlyList<RegistrationPhoto> Photos);

/// <summary>
/// An outbreak open at some point in the report period. Answers is null when not assessed. Actions total/done and the photo
/// count are derived where shown: Actions.Count, Actions.Count(a => a.DoneAt != null), Registrations.Sum(r => r.Photos.Count).
/// </summary>
public sealed record TailBiteReportOutbreak(int Id, int PropertyId, string PropertyName, string LocationPath, int RuleVersion,
    DateTime OpenedAt, DateTime? AssessedAt, DateTime? ClosedAt, int BittenPigs, int SeverePigs, FactorAnswers? Answers,
    IReadOnlyList<TailBiteReportAction> Actions, IReadOnlyList<TailBiteReportRegistration> Registrations);

public sealed record TailBiteReportTotals(int Outbreaks, int ClosedInPeriod, int BittenPigs, int SeverePigs, int ActionsDone, int ActionsTotal);

public sealed record TailBiteComplianceReport(IReadOnlyList<TailBiteReportOutbreak> Outbreaks, TailBiteReportTotals Totals);
