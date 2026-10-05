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

namespace BackendConfiguration.Pn.Controllers;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Services.TailBite;

/// <summary>
/// Shared by both tail-bite controllers: a <see cref="TailBiteException"/> becomes a failed result carrying the
/// message; anything else propagates to the host's error handling.
/// </summary>
internal static class TailBiteRunner
{
    /// <summary>Runs <paramref name="work"/> without resolving a caller (web-admin routes).</summary>
    internal static async Task<OperationResult> Run(Func<Task> work)
    {
        try { await work(); return new OperationResult(true); }
        catch (TailBiteException e) { return new OperationResult(false, e.Message); }
    }

    /// <summary>Resolves the caller's SDK site inside the guard, then runs <paramref name="work"/> with it.</summary>
    internal static Task<OperationResult> Run(ITailBiteAccess access, Func<int, Task> work)
        => Run(async () => await work(await access.RequireCallerSiteAsync()));

    internal static async Task<OperationDataResult<T>> Run<T>(Func<Task<T>> work)
    {
        try { return new OperationDataResult<T>(true, await work()); }
        catch (TailBiteException e) { return new OperationDataResult<T>(false, e.Message); }
    }

    internal static Task<OperationDataResult<T>> Run<T>(ITailBiteAccess access, Func<int, Task<T>> work)
        => Run(async () => await work(await access.RequireCallerSiteAsync()));
}

// Request bodies. Validation is the services' job.
public sealed record SetManagerRequest(bool? IsManager);
public sealed record CreateLocationRequest(int ParentId, string Name);
public sealed record CreatePenRangeRequest(int ParentId, string Prefix, int From, int To);
public sealed record NameRequest(string Name);
public sealed record MoveLocationRequest(int NewParentId);
public sealed record OccupancyRequest(int PigCount, DateTime ValidFromUtc);
// REST-only action shape: Factor is nullable so a missing property is told apart from Water (enum 0).
public sealed record NewActionRequest(TailBiteFactor? Factor, string Description, int ResponsibleSiteId, DateTime FollowUpDate);
public sealed record SaveAssessmentRequest(FactorAnswers? Answers, IReadOnlyList<NewActionRequest>? NewActions);
public sealed record ActionDoneRequest(bool? Done);
public sealed record ReasonRequest(string Reason);
public sealed record ReassignRequest(int ResponsibleSiteId);
