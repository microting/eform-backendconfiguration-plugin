using System.Collections.Generic;
using Microting.eFormApi.BasePn.Infrastructure.Models.Common;

namespace BackendConfiguration.Pn.Infrastructure.Models.AssignmentWorker;

public class PropertyWorkersFiltrationModel : FilterAndSortModel
{
    public List<int> PropertyIds { get; set; }
    public bool ShowResigned { get; set; }
}