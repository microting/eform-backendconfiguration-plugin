namespace BackendConfiguration.Pn.Infrastructure.Models.Properties;

using Microting.eFormApi.BasePn.Infrastructure.Models.Common;

/// <summary>
/// A linked-site dictionary entry that also carries the worker's SDK language id,
/// so the calendar task modal can derive which languages the assigned workers use
/// (multi-language Title/Description). Extends <see cref="CommonDictionaryModel"/>
/// so existing consumers that read only Id/Name keep working; the extra
/// LanguageId is simply an additional JSON field for callers that want it.
/// </summary>
public class SiteLanguageDictionaryModel : CommonDictionaryModel
{
    public int LanguageId { get; set; }
}
