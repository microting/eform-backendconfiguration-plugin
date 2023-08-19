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
    public List<CommonTagModel> Tags { get; set; } = new();
    public string TaskName { get; set; }
    public string Eform { get; set; }
    public DateTime StartDate { get; set; }
    public RepeatType RepeatType { get; set; }
    public int RepeatEvery { get; set; }
    public TaskWizardStatuses Status { get; set; }
    public List<string> AssignedTo { get; set; } = new();

    public bool CreatedInGuide { get; set; }
}