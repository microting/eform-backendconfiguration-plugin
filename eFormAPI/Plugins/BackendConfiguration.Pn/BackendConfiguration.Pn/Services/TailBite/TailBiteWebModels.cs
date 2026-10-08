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

// Web-admin read models (sub-project 3). Every timestamp is UTC with DateTimeKind.Utc set, so Newtonsoft writes a trailing Z.

public sealed record TailBitePropertyStatus(int PropertyId, string Name, bool Enabled);

// A worker can hold more than one PropertyWorker row on a property; the manager toggle sets every one of them.
public sealed record TailBiteWorker(int SiteId, string Name, bool IsManager, IReadOnlyList<int> PropertyWorkerIds);

public sealed record RuleDto(int Id, int LocationId, int? MinBittenPigs, int? MinSevere, int WindowDays, int CountDepth, int Version,
    DateTime? UpdatedAt);

public sealed record RuleVersionDto(int Version, int LocationId, int? MinBittenPigs, int? MinSevere, int WindowDays, int CountDepth,
    DateTime? ChangedAt);

public sealed record OccupancyDto(int LocationId, int PigCount, TailBiteOccupancySource Source, DateTime ValidFrom);

public sealed record OutbreakRegistrationRow(int RegistrationId, int RowId, int LocationId, DateTime EffectiveAt, int Minor, int Severe,
    IReadOnlyList<int> ActionTypeIds, int SiteId, string SiteName, bool Cancelled, string? CancelReason, int PhotoCount);

// The outbreak's property travels with its rows, so the page can load the tree, workers and pig counts it needs.
public sealed record OutbreakRegistrations(int PropertyId, IReadOnlyList<OutbreakRegistrationRow> Rows);
