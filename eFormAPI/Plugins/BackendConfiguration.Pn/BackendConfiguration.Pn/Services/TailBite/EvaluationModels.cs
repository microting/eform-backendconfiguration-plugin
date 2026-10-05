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

public sealed record EvalLocation(int Id, int? ParentId);
public sealed record EvalRule(int Id, int Version, int LocationId, int? MinBittenPigs, int? MinSevere, int WindowDays, int CountDepth);
public sealed record EvalRow(int RowId, int LocationId, DateTime EffectiveAt, int Minor, int Severe, bool Cancelled, bool LinkedToClosed);
public sealed record EvalOpenOutbreak(int OutbreakId, int LocationId);
public sealed record EvaluationSnapshot(
    IReadOnlyDictionary<int, EvalLocation> Locations,   // ALL locations of the property, incl. soft-deleted
    IReadOnlyList<EvalRule> Rules,                       // non-removed rules
    IReadOnlyList<EvalRow> Rows,                         // rows incl. the new ones
    IReadOnlyList<EvalOpenOutbreak> OpenOutbreaks);
public abstract record EvalDecision;
public sealed record JoinOutbreak(int OutbreakId, IReadOnlyList<int> RowIds) : EvalDecision;
public sealed record OpenNewOutbreak(int SummingLocationId, int RuleId, int RuleVersion, DateTime OpenedAt, IReadOnlyList<int> RowIds) : EvalDecision;
