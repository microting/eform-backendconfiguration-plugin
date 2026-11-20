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

using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;

/// <summary>
/// One linked registration row of an outbreak. Init properties rather than a positional record: EF Core translates member
/// access on a member-init projection (so callers can order and filter on it), not on a constructor projection.
/// </summary>
public sealed record OutbreakRowJoin
{
    public required int OutbreakId { get; init; }
    public required TailBiteRegistrationLocation Row { get; init; }
    public required TailBiteRegistration Registration { get; init; }
}

internal static class TailBiteOutbreakRows
{
    private const string Removed = Constants.WorkflowStates.Removed;

    /// <summary>The live link → row → registration join of the given outbreaks, cancelled registrations included.</summary>
    public static IQueryable<OutbreakRowJoin> Of(BackendConfigurationPnDbContext db, IReadOnlyCollection<int> outbreakIds)
        => from link in db.TailBiteOutbreakLinks.AsNoTracking()
           join row in db.TailBiteRegistrationLocations on link.RegistrationLocationId equals row.Id
           join reg in db.TailBiteRegistrations on row.RegistrationId equals reg.Id
           where outbreakIds.Contains(link.OutbreakId)
                 && link.WorkflowState != Removed && row.WorkflowState != Removed && reg.WorkflowState != Removed
           select new OutbreakRowJoin { OutbreakId = link.OutbreakId, Row = row, Registration = reg };
}
