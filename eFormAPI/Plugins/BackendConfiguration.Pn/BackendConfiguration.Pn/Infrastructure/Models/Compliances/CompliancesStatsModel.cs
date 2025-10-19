namespace BackendConfiguration.Pn.Controllers;

public class CompliancesStatsModel
{
    public int OneWeekInTheFutureCount { get; set; }
    public int TodayCount { get; set; }
    public int TodayCountEnvironmentInspectionTag { get; set; }
    public int TotalCount { get; set; }
    public int OneWeekCount { get; set; }
    public int TwoWeeksCount { get; set; }
    public int OneMonthCount { get; set; }
    public int TwoMonthsCount { get; set; }
    public int ThreeMonthsCount { get; set; }
    public int SixMonthsCount { get; set; }
    public int MoreThanSixMonthsCount { get; set; }
}