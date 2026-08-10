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
/// One row of the Historik table (mockup parity, #1095) - one row per task
/// currently Completed or Archived, sorted by <see cref="CompletedAt"/>
/// descending. Replaces the old per-event <c>AdhocTaskHistoryEventModel</c>
/// (created/assigned/completed/archived/commented event explosion) - see
/// <see cref="Services.BackendConfigurationAdhocService.IBackendConfigurationAdhocService.ListHistory"/>.
/// </summary>
public class AdhocTaskHistoryRowModel
{
    public int TaskId { get; set; }
    public string TaskTitle { get; set; } = "";

    /// <summary>
    /// The "Løst" date - always populated (a row is Completed by invariant:
    /// Archived ⇒ Completed ⇒ CompletedAt set). Never the archive date.
    /// </summary>
    public DateTime CompletedAt { get; set; }

    /// <summary>
    /// Display name of the recorded performer; empty string when the task
    /// was completed with no recorded performer (a dashboard admin
    /// completing without selecting one stamps a null CompletedByWorkerId).
    /// </summary>
    public string CompletedByName { get; set; } = "";

    /// <summary>Only Completed or Archived occur in Historik - Open never does.</summary>
    public AdhocTaskStatusFilter Status { get; set; }

    public string PropertyName { get; set; } = "";
    public string? AreaName { get; set; }

    public List<string> TagNames { get; set; } = [];

    public string? LastCommentText { get; set; }
    public string? LastCommentAuthor { get; set; }
    public DateTime? LastCommentAt { get; set; }
}
