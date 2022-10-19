using System.Collections.Generic;

namespace BackendConfiguration.Pn.Infrastructure.Models.Pools;

public class PoolHoursModel
{
    public List<PoolHourModel> Parrings { get; set; }
}

public class PoolHourModel
{
    public int AreaRuleId { get; set; }
    public int DayOfWeek { get; set; }
    public int Index { get; set; }
    public bool IsActive { get; set; }
    public string Name { get; set; }
}