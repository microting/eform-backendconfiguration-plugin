using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure;
using Microting.eForm.Infrastructure.Models;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;

namespace BackendConfiguration.Pn.Infrastructure.Helpers;

/// <summary>
/// Seed data hardcodes AreaRuleTranslation.LanguageId as 1=da, 2=en-US, 3=de-DE, but the
/// SDK Languages table uses environment-specific auto-increment ids. These helpers resolve the
/// real SDK language id by code so area-rule name translations point at languages that actually
/// exist (mirrors the AreaTranslation remap added in PR #982 for SeedEForms).
/// </summary>
public static class AreaRuleLanguageHelper
{
    /// <summary>
    /// What the seed data's (and the frontend app-locale's) literal LanguageIds mean: 1=da,
    /// 2=en-US, 3=de-DE. The SDK ids for those codes differ per install.
    /// </summary>
    private static readonly IReadOnlyDictionary<int, string> SeedLanguageIdToCode =
        new Dictionary<int, string> { { 1, "da" }, { 2, "en-US" }, { 3, "de-DE" } };

    private static async Task<Dictionary<int, int>> BuildSeedLanguageIdToSdkIdAsync(
        MicrotingDbContext sdkDbContext)
    {
        var sdkLanguages = await sdkDbContext.Languages.ToListAsync().ConfigureAwait(false);
        var seedLanguageIdToSdkId = new Dictionary<int, int>();
        foreach (var (seedLanguageId, code) in SeedLanguageIdToCode)
        {
            var lang = sdkLanguages.FirstOrDefault(x => string.Equals(x.LanguageCode, code, StringComparison.OrdinalIgnoreCase));
            if (lang != null)
            {
                seedLanguageIdToSdkId[seedLanguageId] = lang.Id;
            }
        }

        return seedLanguageIdToSdkId;
    }

    /// <summary>
    /// Remaps the hardcoded seed LanguageId (1/2/3) on each area rule's translations to the real
    /// SDK language id resolved by LanguageCode. In-place mutation is safe: each translation is
    /// visited once and remapped from its original seed id via the static {1->da,2->en-US,3->de-DE} map.
    /// </summary>
    public static async Task RemapSeedLanguageIdsAsync(
        IEnumerable<AreaRule> areaRules,
        MicrotingDbContext sdkDbContext)
    {
        var seedLanguageIdToSdkId = await BuildSeedLanguageIdToSdkIdAsync(sdkDbContext).ConfigureAwait(false);
        foreach (var areaRule in areaRules)
        {
            RemapTranslations(areaRule.AreaRuleTranslations, seedLanguageIdToSdkId);
        }
    }

    /// <summary>
    /// Single-area-rule overload of <see cref="RemapSeedLanguageIdsAsync(IEnumerable{AreaRule}, MicrotingDbContext)"/>.
    /// </summary>
    public static async Task RemapSeedLanguageIdsAsync(
        AreaRule areaRule,
        MicrotingDbContext sdkDbContext)
    {
        var seedLanguageIdToSdkId = await BuildSeedLanguageIdToSdkIdAsync(sdkDbContext).ConfigureAwait(false);
        RemapTranslations(areaRule.AreaRuleTranslations, seedLanguageIdToSdkId);
    }

    /// <summary>
    /// Remaps the hardcoded seed LanguageId (1/2/3) on a freshly built list of translations to the
    /// real SDK language id resolved by LanguageCode (used by the Type7/Type8 build path).
    /// </summary>
    public static async Task RemapSeedLanguageIdsAsync(
        IEnumerable<AreaRuleTranslation> translations,
        MicrotingDbContext sdkDbContext)
    {
        var seedLanguageIdToSdkId = await BuildSeedLanguageIdToSdkIdAsync(sdkDbContext).ConfigureAwait(false);
        RemapTranslations(translations, seedLanguageIdToSdkId);
    }

    /// <summary>
    /// Remaps a code-built list of folder translations written with the seed LanguageIds
    /// (1=da, 2=en-US, 3=de-DE) to the real SDK language ids, before it is handed to
    /// Core.FolderCreate. Value-based like the AreaRuleTranslation overloads, so only pass
    /// literal seed lists — never ids that came from a client.
    /// </summary>
    public static async Task RemapSeedLanguageIdsAsync(
        IEnumerable<CommonTranslationsModel> translations,
        MicrotingDbContext sdkDbContext)
    {
        var seedLanguageIdToSdkId = await BuildSeedLanguageIdToSdkIdAsync(sdkDbContext).ConfigureAwait(false);
        Remap(translations, x => x.LanguageId, (x, id) => x.LanguageId = id, seedLanguageIdToSdkId);
    }

    /// <summary>
    /// Remaps the seed LanguageIds (1=da, 2=en-US, 3=de-DE) on area translations taken from
    /// <c>BackendConfigurationSeedAreas.AreasSeed</c>, as the plugin-start area seed does.
    /// </summary>
    public static async Task RemapSeedLanguageIdsAsync(
        IEnumerable<AreaTranslation> translations,
        MicrotingDbContext sdkDbContext)
    {
        var seedLanguageIdToSdkId = await BuildSeedLanguageIdToSdkIdAsync(sdkDbContext).ConfigureAwait(false);
        Remap(translations, x => x.LanguageId, (x, id) => x.LanguageId = id, seedLanguageIdToSdkId);
    }

    private static void RemapTranslations(
        IEnumerable<AreaRuleTranslation> translations,
        IReadOnlyDictionary<int, int> seedLanguageIdToSdkId) =>
        Remap(translations, x => x.LanguageId, (x, id) => x.LanguageId = id, seedLanguageIdToSdkId);

    private static void Remap<T>(
        IEnumerable<T> translations,
        Func<T, int> getLanguageId,
        Action<T, int> setLanguageId,
        IReadOnlyDictionary<int, int> seedLanguageIdToSdkId)
    {
        foreach (var translation in translations ?? [])
        {
            if (seedLanguageIdToSdkId.TryGetValue(getLanguageId(translation), out var sdkId))
            {
                setLanguageId(translation, sdkId);
            }
        }
    }

    /// <summary>
    /// The calendar create/edit modal hardcodes the Danish source title's LanguageId to the
    /// frontend app-locale id 1 (see task-create-edit-modal.component.ts), while target-language
    /// translates already carry the real SDK Languages.Id returned by GET /settings/languages.
    /// Persisting the verbatim id stores the Danish title under LanguageId=1, which no reader ever
    /// queries (readers match against the user's SDK Languages.Id resolved by LanguageCode), so the
    /// event title renders blank.
    ///
    /// The SDK Languages table is customer-specific (auto-increment ids; Danish is NOT guaranteed to
    /// be id 1, and ids are NOT guaranteed to be the contiguous set {1,2,3}). This remaps each
    /// incoming translate's LanguageId IN PLACE — but the guard is purely EXISTENCE-based, never
    /// value-based:
    ///   * If the incoming id EXISTS in the SDK Languages table, it is a real SDK Languages.Id
    ///     (sent by the modal's target languages, resolved from getLanguages()) -> leave it
    ///     untouched. This is the critical safety property: a valid target id such as en-US=3 on a
    ///     shifted-id tenant must NOT be reinterpreted via the static app-locale map (which would
    ///     mis-key it to de-DE) -> no corruption of good data.
    ///   * If the incoming id is ABSENT from the SDK Languages table, it can only be the frontend's
    ///     hardcoded app-locale id {1->da, 2->en-US, 3->de-DE}. Resolve the intended language code
    ///     from that static map, look up the SDK row by LanguageCode, and remap to its real id.
    ///   * If an absent id is not in the static map, or its code has no matching SDK row, the
    ///     translate is left unchanged (never dropped) and a warning is logged.
    /// Resolution mirrors the #985 seed remap and #982 (resolve by LanguageCode).
    /// </summary>
    public static async Task RemapCommonTranslationLanguageIdsAsync(
        IEnumerable<CommonTranslationsModel> translations,
        MicrotingDbContext sdkDbContext,
        ILogger logger = null)
    {
        if (translations == null)
        {
            return;
        }

        var translationList = translations.ToList();
        if (translationList.Count == 0)
        {
            return;
        }

        var sdkLanguages = await sdkDbContext.Languages.AsNoTracking().ToListAsync().ConfigureAwait(false);
        var sdkIds = sdkLanguages.Select(x => x.Id).ToHashSet();

        foreach (var translation in translationList)
        {
            // EXISTENCE-based guard: a present id is a real SDK Languages.Id (a target language sent
            // by the modal). Never touch it — reinterpreting it via the static app-locale map would
            // corrupt valid data on shifted-id tenants (e.g. en-US=3 -> wrongly remapped to de-DE).
            if (sdkIds.Contains(translation.LanguageId))
            {
                continue;
            }

            // The id is absent from SDK Languages, so it can only be the frontend's hardcoded
            // app-locale id. Resolve the intended language code from the static map.
            if (!SeedLanguageIdToCode.TryGetValue(translation.LanguageId, out var code))
            {
                logger?.LogWarning(
                    "RemapCommonTranslationLanguageIdsAsync: translate LanguageId {LanguageId} is absent from SDK Languages and is not a known app-locale id; leaving unchanged",
                    translation.LanguageId);
                continue;
            }

            // Resolve the intended language by code to its real SDK id.
            var sdkLanguage = sdkLanguages.FirstOrDefault(x => string.Equals(x.LanguageCode, code, StringComparison.OrdinalIgnoreCase));
            if (sdkLanguage != null)
            {
                translation.LanguageId = sdkLanguage.Id;
                continue;
            }

            // Could not resolve the intended language to a real SDK row: keep the original id (never
            // drop the translation) and warn so the mis-keyed row is at least traceable.
            logger?.LogWarning(
                "RemapCommonTranslationLanguageIdsAsync: could not resolve translate LanguageId {LanguageId} (code {Code}) to a valid SDK Languages.Id; leaving unchanged",
                translation.LanguageId, code);
        }
    }
}
