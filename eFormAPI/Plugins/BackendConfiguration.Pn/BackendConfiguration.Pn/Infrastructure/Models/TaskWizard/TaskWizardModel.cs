#nullable enable
namespace BackendConfiguration.Pn.Infrastructure.Models.TaskWizard;

using Enums;
using Microting.eFormApi.BasePn.Infrastructure.Models.Common;
using System;
using System.Collections.Generic;

public class TaskWizardModel
{
    public int Id { get; set; }
    public string Property { get; set; }
    public string Folder { get; set; }
    public List<CommonTagModel> Tags { get; set; } = [];
    public string TaskName { get; set; }
    public string Eform { get; set; }
    public DateTime StartDate { get; set; }
    public RepeatType RepeatType { get; set; }
    public int RepeatEvery { get; set; }
    public TaskWizardStatuses Status { get; set; }
    public List<string> AssignedTo { get; set; } = [];
    public bool CreatedInGuide { get; set; }
    public int? PlanningId { get; set; }
    public int? EformId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public CommonTagModel? TagReport { get; set; }
}