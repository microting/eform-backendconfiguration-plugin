namespace BackendConfiguration.Pn.Infrastructure.Models.Stats;

using System.Collections.Generic;

public class AdHocTaskWorkers
{
    public List<AdHocTaskWorker> TaskWorkers { get; set; } = new();
}

public class AdHocTaskWorker
{
    public string WorkerName { get; set; }
    public int StatValue { get; set; }
}