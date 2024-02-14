using System;
using System.Collections.Generic;
using BackendConfiguration.Pn.Infrastructure.Enums;
using Microting.eForm.Infrastructure.Models;

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
}