using System;
using System.Collections.Generic;
using Microting.eForm.Infrastructure.Models;

namespace BackendConfiguration.Pn.Infrastructure.Models.Calendar;

public class CalendarTaskCreateRequestModel
{
    public int PropertyId { get; set; }
    public int? FolderId { get; set; }
    public int? ItemPlanningTagId { get; set; }
    public List<int> TagIds { get; set; } = [];
    public List<CommonTranslationsModel> Translates { get; set; } = [];
    public int EformId { get; set; }
    public DateTime StartDate { get; set; }
    public int RepeatType { get; set; }
    public int RepeatEvery { get; set; }
    public int Status { get; set; }
    public List<int> Sites { get; set; } = [];
    public bool ComplianceEnabled { get; set; }
    public double StartHour { get; set; }
    public double Duration { get; set; }
    public int? BoardId { get; set; }
    public string Color { get; set; }
    public int? RepeatEndMode { get; set; }
    public int? RepeatOccurrences { get; set; }
    public DateTime? RepeatUntilDate { get; set; }
    public string? RepeatWeekdaysCsv { get; set; }
    // Day-of-month for monthly + yearly rules. Nullable: null means "no DOM
    // for this rule" (e.g. weekly / daily). The backend writes
    // `arp.DayOfMonth = createModel.DayOfMonth ?? 0`, mirroring how
    // RepeatWeekdaysCsv is always written: switching kinds clears the stale
    // value (e.g. moving from monthly back to weekly resets DayOfMonth to 0).
    public int? DayOfMonth { get; set; }
    public string? DescriptionHtml { get; set; }
}
