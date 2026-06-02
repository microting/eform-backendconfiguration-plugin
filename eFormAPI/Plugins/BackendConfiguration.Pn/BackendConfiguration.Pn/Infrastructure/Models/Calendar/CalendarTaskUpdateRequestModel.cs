namespace BackendConfiguration.Pn.Infrastructure.Models.Calendar;

public class CalendarTaskUpdateRequestModel : CalendarTaskCreateRequestModel
{
    public int Id { get; set; }

    // Edit scope (issue #885): "this" | "thisAndFollowing" | "all".
    // Null/empty is treated as "all" for backward compatibility.
    public string Scope { get; set; }

    // The pre-edit occurrence date ("yyyy-MM-dd") the user opened. Required
    // for "this"/"thisAndFollowing" so the edit targets the right occurrence
    // instead of relocating the whole series. StartDate carries the new date.
    public string OriginalDate { get; set; }
}
