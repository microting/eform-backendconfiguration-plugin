namespace BackendConfiguration.Pn.Infrastructure.Models.WorkerTags;

using System.Collections.Generic;
using Microting.eFormApi.BasePn.Infrastructure.Models.Common;

/// <summary>
/// A worker group ("team") entry of the teams list. Extends
/// <see cref="CommonDictionaryModel"/> so consumers reading only Id/Name keep working.
/// <para>
/// <see cref="MemberSiteIds"/> is filled only when the list is requested for a property
/// (#1295): the team's live members that are linked to that property — exactly the sites
/// the team deploys to there. The task modal maps them to the property's linked sites to
/// include team members' languages in its per-language Title/Description fields. It is
/// <c>null</c> on the installation-wide list.
/// </para>
/// </summary>
public class WorkerTagModel : CommonDictionaryModel
{
    public List<int>? MemberSiteIds { get; set; }
}
