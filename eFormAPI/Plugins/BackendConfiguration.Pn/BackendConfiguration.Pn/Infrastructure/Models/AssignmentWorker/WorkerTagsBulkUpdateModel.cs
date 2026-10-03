using System.Collections.Generic;

namespace BackendConfiguration.Pn.Infrastructure.Models.AssignmentWorker;

/// <summary>
/// #1380: what a bulk worker-tag change does. There is deliberately no
/// "replace": a bulk action that sets the tag list would silently wipe the
/// tags each worker already had.
/// </summary>
public enum WorkerTagsBulkMode
{
    Add = 0,
    Remove = 1,
}

/// <summary>Body of <c>PUT properties/assignment/bulk-tags</c> (#1380).</summary>
public class WorkerTagsBulkUpdateModel
{
    /// <summary>SDK <c>Sites.Id</c> of the workers (the <c>siteId</c> the Medarbejdere grid shows).</summary>
    public List<int> SiteIds { get; set; } = [];

    /// <summary>SDK <c>Tags.Id</c> to add to, or remove from, every listed worker.</summary>
    public List<int> TagIds { get; set; } = [];

    public WorkerTagsBulkMode Mode { get; set; }
}
