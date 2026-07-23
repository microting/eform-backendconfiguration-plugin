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
/// One row of the Historik timeline (M5/P2), returned by
/// <see cref="Services.BackendConfigurationAdhocService.IBackendConfigurationAdhocService.ListHistory"/>.
///
/// Derivation (VERIFY-AT-EXECUTION resolved against the mockup's
/// <c>renderHistoryTable</c>/<c>getHistoryFilteredSorted</c> - which turned
/// out to filter to tasks currently <c>resolved</c>/<c>archived</c> only,
/// sorted by completion date): events are derived from
/// <see cref="Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.AdhocTaskEntity"/>'s
/// own scalar timestamps (<c>CreatedAt</c>/<c>CompletedAt</c>/<c>ArchivedAt</c>)
/// plus <c>AdhocTaskAssignmentLog</c> and <c>AdhocTaskComment</c> rows - NOT
/// by diffing <c>AdhocTaskEntityVersion</c> snapshots. This means a task that
/// was completed and later reopened will not surface a standalone
/// "completed" event once reopened (its <c>CompletedAt</c> is cleared by
/// <c>Reopen</c>) and "reopened" itself is not one of the event types this
/// model emits - both are accepted v1 limitations per the plan's own default
/// ("the plan defaults to scalar-timestamps + assignment-log + comments"),
/// and match the mockup's own behavior: a reopened task goes back to the
/// "open" bucket and simply disappears from Historik entirely, so no row is
/// lost from the UI's perspective.
/// </summary>
public class AdhocTaskHistoryEventModel
{
    public int TaskId { get; set; }
    public string TaskTitle { get; set; } = "";

    /// <summary>One of: created, assigned, completed, archived, commented.</summary>
    public string EventType { get; set; } = "";

    public DateTime OccurredAt { get; set; }

    public string ActorName { get; set; } = "";

    public string? Detail { get; set; }

    public string PropertyName { get; set; } = "";
    public string? AreaName { get; set; }

    public List<string> TagNames { get; set; } = [];

    public string? LastCommentText { get; set; }
    public string? LastCommentAuthor { get; set; }
    public DateTime? LastCommentAt { get; set; }

    public bool Completed { get; set; }
    public bool Archived { get; set; }
}
