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

namespace BackendConfiguration.Pn.Infrastructure.Models.Adhoc;

using System;
using System.Collections.Generic;

/// <summary>
/// Filters for <c>POST history/index</c> (M5/P2) - period chips
/// (30d/60d/90d/6m/12m/24m + custom range, resolved client-side to
/// <see cref="DateFrom"/>/<see cref="DateTo"/>), property/area, and tags
/// (<see cref="TagIds"/> is AND-only per the mockup's «Tags i historik (OG)»
/// - unlike <see cref="AdhocTaskFiltersModel"/>'s toggleable AND/OR).
/// </summary>
public class AdhocHistoryFiltersModel
{
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }

    public int? PropertyId { get; set; }
    public int? AreaId { get; set; }

    /// <summary>AND-only: a matching event's task must have ALL of these tags.</summary>
    public List<int> TagIds { get; set; } = [];

    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}
