using System.Collections.Generic;

namespace BackendConfiguration.Pn.Infrastructure.Models.Stats;

public class PlannedTaskWorkers
{
    public List<PlannedTaskWorker> TaskWorkers { get; set; } = new();
}

public class PlannedTaskWorker
{
    public string WorkerName { get; set; }
    public int StatValue { get; set; }
}