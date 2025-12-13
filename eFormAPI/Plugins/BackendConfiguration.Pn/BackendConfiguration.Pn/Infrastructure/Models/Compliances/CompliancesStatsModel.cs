using System;

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
    public int NumberOfPlannedTasks { get; set; }
    public DateTime? DateOfOldestPlannedTask { get; set; }
    public int NumberOfPlannedEnvironmentInspectionTagTasks { get; set; }
    public DateTime? DateOfOldestEnvironmentInspectionTagPlannedTask { get; set; }
    public int NumberOfAdHocTasks { get; set; }
    public DateTime? DateOfOldestAdHocTask { get; set; }
    public int NumberOfCompletedEnvironmentInspectionTagPlanningsLast30Days { get; set; }
    public int NumberOfPlannedEnvironmentInspectionTagPlanningsLast30Days { get; set; }
    public int NumberOfWorkersWithTimeRegistrationEnabled { get; set; }
    public int NumberOfFullDayTimeRegistrationsLastWeek { get; set; }
}