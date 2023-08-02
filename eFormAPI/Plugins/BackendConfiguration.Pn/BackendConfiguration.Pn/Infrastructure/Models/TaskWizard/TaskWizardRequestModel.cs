namespace BackendConfiguration.Pn.Infrastructure.Models.TaskWizard;

public class TaskWizardRequestModel
{
    public TaskWizardFiltrationModel Filters { get; set; }

    public TaskWizardPaginationModel Pagination { get; set; }
}
