#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microting.eForm.Infrastructure.Models;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;

namespace BackendConfiguration.Pn.Services.TaskTranslation;

/// <summary>
/// #1384 — fills a task's missing title/description translations for the languages of
/// the people it is assigned to, by machine-translating the Danish text. Only an empty
/// language is filled; text that is already there, typed or translated earlier, is never
/// overwritten. The host's translation service is optional: without it nothing is
/// translated and the task keeps its Danish text.
/// </summary>
public interface ITaskTranslationFiller
{
    /// <summary>
    /// Fills <paramref name="translates"/> in place, before a create or update persists it,
    /// for the languages of <paramref name="recipientSiteIds"/>. <paramref name="existing"/>
    /// are the task's stored translations (empty on create); a language whose stored text
    /// is not in the request keeps that text.
    /// </summary>
    /// <returns>False when a language needed a translation it could not get.</returns>
    Task<bool> FillMissingAsync(
        List<CommonTranslationsModel> translates,
        IEnumerable<int> recipientSiteIds,
        IReadOnlyCollection<AreaRuleTranslation> existing,
        CancellationToken ct = default);

    /// <summary>
    /// Fills the stored translations of a saved event (AreaRuleTranslation of
    /// <paramref name="areaRuleId"/>, PlanningNameTranslation of <paramref name="planningId"/>)
    /// for <paramref name="recipientSiteIds"/>, the event's effective recipients — explicit
    /// sites plus the team members linked to its property. Covers teams, team members who
    /// join later and a worker whose language changes.
    /// </summary>
    /// <returns>False when a language needed a translation it could not get.</returns>
    Task<bool> FillMissingForEventAsync(int areaRuleId, int planningId, IReadOnlyCollection<int> recipientSiteIds,
        CancellationToken ct = default);
}
