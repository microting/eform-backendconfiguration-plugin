using BackendConfiguration.Pn.Infrastructure.Enums;
using System.Collections.Generic;

namespace BackendConfiguration.Pn.Infrastructure.Models.TaskWizard;

public class TaskWizardFiltrationModel
{
    public List<int> PropertyIds { get; set; } = new();
    public List<int> TagIds { get; set; } = new();
    public List<int> FolderIds { get; set; } = new();
    public List<int> AssignToIds { get; set; } = new();
    public TaskWizardStatuses? Status { get; set; }
}
