namespace BackendConfiguration.Pn.Infrastructure.Models.Stats;

public class PlannedTaskDays
{
    public int Exceeded { get; set; }
    public int Today { get; set; }
    public int FromFirstToSeventhDays { get; set; }
    public int FromEighthToThirtiethDays { get; set; }
    public int OverThirtiethDays { get; set; }
}