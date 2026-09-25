using System;
using System.Collections.Generic;
using BackendConfiguration.Pn.Infrastructure.Enums;
using Microting.eForm.Infrastructure.Models;
using Newtonsoft.Json;

namespace BackendConfiguration.Pn.Infrastructure.Models.TaskWizard;

public class TaskWizardCreateModel
{
    public int Id { get; set; }
    public int PropertyId { get; set; }
    public int? FolderId { get; set; }
    public int? ItemPlanningTagId { get; set; }
    public List<int> TagIds { get; set; } = [];
    public List<CommonTranslationsModel> Translates { get; set; } = [];
    public int EformId { get; set; }
    public DateTime? StartDate { get; set; }
    public RepeatType RepeatType { get; set; }
    public int RepeatEvery { get; set; }
    public TaskWizardStatuses Status { get; set; }
    public List<int> Sites { get; set; } = [];

    /// <summary>
    /// True when the task is also assigned to at least one team (worker tag).
    /// Set only by the calendar, which persists the team links itself; a task
    /// with no <see cref="Sites"/> is saved inactive only when this is false
    /// (#1322). Never read from the request body, so the Task Wizard page keeps
    /// downgrading a site-less task to NotActive.
    /// </summary>
    [JsonIgnore]
    public bool HasWorkerTags { get; set; }

    public bool ComplianceEnabled { get; set; }
}