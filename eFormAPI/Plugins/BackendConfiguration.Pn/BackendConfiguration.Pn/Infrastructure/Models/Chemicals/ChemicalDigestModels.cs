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

/// <summary>One line of the weekly digest (Plan D Contract C5). Deadline fields are set in the two deadline sections.</summary>
public sealed record ChemicalDigestItemModel(
    int PlacementId, int ChemicalId, string ProductName, string RegistrationNo, string LocationName, string PlacementNote,
    ChemicalDeadlineKindEnum? DeadlineKind, DateTime? Deadline, ChemicalAlertThresholdEnum? Threshold,
    int? Status, string StatusText, string RegisteredByName, DateTime? RegisteredAt);

/// <summary>
/// W5 preview of the next weekly digest for one property: the four sections of
/// Plan D Contract C5 ("Frist overskredet", "Frist inden for 6 måneder",
/// "Afmeldt eller udløbet", "Nye registreringer denne uge").
/// </summary>
public sealed record ChemicalDigestModel(
    int PropertyId, string PropertyName, int IsoWeek, DateTime GeneratedAt,
    IReadOnlyList<ChemicalDigestItemModel> PassedDeadlines,
    IReadOnlyList<ChemicalDigestItemModel> UpcomingDeadlines,
    IReadOnlyList<ChemicalDigestItemModel> DeregisteredOrExpired,
    IReadOnlyList<ChemicalDigestItemModel> NewRegistrations,
    IReadOnlyList<string> Recipients)
{
    /// <summary>The e-mail is sent only when this is true (spec §9); new registrations alone do not count.</summary>
    public bool HasActionItems => PassedDeadlines.Count > 0 || UpcomingDeadlines.Count > 0 || DeregisteredOrExpired.Count > 0;
}
