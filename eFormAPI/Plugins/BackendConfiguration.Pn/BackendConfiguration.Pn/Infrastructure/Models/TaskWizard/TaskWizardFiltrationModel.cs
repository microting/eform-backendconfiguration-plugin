using BackendConfiguration.Pn.Infrastructure.Enums;
using System.Collections.Generic;

namespace BackendConfiguration.Pn.Infrastructure.Models.TaskWizard;

public class TaskWizardFiltrationModel
{
    public List<int> PropertyIds { get; set; } = [];
    public List<int> TagIds { get; set; } = [];
    public List<int> FolderIds { get; set; } = [];
    public List<int> AssignToIds { get; set; } = [];
    public TaskWizardStatuses? Status { get; set; }
}
