using System;
using System.Collections.Generic;

namespace BackendConfiguration.Pn.Infrastructure.Models.TaskList;

public class TaskListBatchRequestModel
{
    public List<int> TaskIds { get; set; } = [];
}

public class TaskListBatchAssignModel : TaskListBatchRequestModel
{
    public int SiteId { get; set; }
}

public class TaskListBatchReassignModel : TaskListBatchRequestModel
{
    public int FromSiteId { get; set; }
    public int ToSiteId { get; set; }
}

public class TaskListBatchChangeEformModel : TaskListBatchRequestModel
{
    public int EformId { get; set; }
}

public class TaskListBatchTagsModel : TaskListBatchRequestModel
{
    public List<int> TagIds { get; set; } = [];
}

public class TaskListBatchComplianceModel : TaskListBatchRequestModel
{
    /// <summary>
    /// true  = "Overskredet opgave vises i app"  — an overdue case is moved
    ///          into the property's "00. Overdue tasks" folder by the nightly job.
    /// false = "Overskredet opgave vises ikke i app" — the job skips the task.
    /// </summary>
    public bool ComplianceEnabled { get; set; }
}

public class TaskListBatchCopyModel : TaskListBatchRequestModel
{
    public int TargetPropertyId { get; set; }
    public int TargetBoardId { get; set; }
    public DateTime StartDate { get; set; }
    public int SiteId { get; set; }
}
