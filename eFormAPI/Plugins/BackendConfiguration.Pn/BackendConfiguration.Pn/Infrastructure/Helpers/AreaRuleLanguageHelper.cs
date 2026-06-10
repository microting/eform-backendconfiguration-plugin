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
    private static async Task<Dictionary<int, int>> BuildSeedLanguageIdToSdkIdAsync(
        MicrotingDbContext sdkDbContext)
    {
        var sdkLanguages = await sdkDbContext.Languages.ToListAsync().ConfigureAwait(false);
        var seedLanguageIdToCode = new Dictionary<int, string> { { 1, "da" }, { 2, "en-US" }, { 3, "de-DE" } };
        var seedLanguageIdToSdkId = new Dictionary<int, int>();
        foreach (var (seedLanguageId, code) in seedLanguageIdToCode)
        {
            var lang = sdkLanguages.FirstOrDefault(x => x.LanguageCode == code);
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

    private static void RemapTranslations(
        IEnumerable<AreaRuleTranslation> translations,
        IReadOnlyDictionary<int, int> seedLanguageIdToSdkId)
    {
        if (translations == null)
        {
            return;
        }

        foreach (var translation in translations)
        {
            if (seedLanguageIdToSdkId.TryGetValue(translation.LanguageId, out var sdkId))
            {
                translation.LanguageId = sdkId;
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
    /// be id 1), so the hardcoded app-locale id 1 only happens to be correct when the tenant's
    /// Danish row landed on id 1. This remaps each incoming translate's LanguageId IN PLACE to the
    /// SDK Languages.Id that ACTUALLY carries the intended language code, but only when the incoming
    /// id is one of the frontend's static app-locale ids {1->da, 2->en-US, 3->de-DE} AND the SDK row
    /// currently at that id is NOT already the matching language. Concretely:
    ///   * Danish source comes in as app-locale id 1. If SDK id 1 is the Danish row, it is left as-is
    ///     (correct no-op). If SDK id 1 is some other language (or missing), it is remapped to the
    ///     SDK id whose LanguageCode == "da".
    ///   * Target languages already carry their true SDK id whose LanguageCode matches, so they are
    ///     left untouched (never corrupted), even when that id is 2 or 3.
    /// Resolution mirrors the #985 seed remap and #982 (resolve by LanguageCode). A translate whose
    /// app-locale code cannot be resolved to any SDK language is left unchanged (never dropped) and
    /// logged.
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

        var sdkLanguages = await sdkDbContext.Languages.ToListAsync().ConfigureAwait(false);
        var sdkLanguageById = sdkLanguages.ToDictionary(x => x.Id);
        // Mirror of the frontend applicationLanguages app-locale ids (1=da, 2=en-US, 3=de-DE).
        var appLocaleIdToCode = new Dictionary<int, string> { { 1, "da" }, { 2, "en-US" }, { 3, "de-DE" } };

        foreach (var translation in translationList)
        {
            // Only the static app-locale ids carry the buggy hardcoded convention; any other id is
            // a true SDK Languages.Id sent by the modal's target languages -> leave it untouched.
            if (!appLocaleIdToCode.TryGetValue(translation.LanguageId, out var code))
            {
                continue;
            }

            // The SDK row currently at this id already IS the intended language -> correct no-op
            // (e.g. a default-seeded tenant where da=1). Avoids corrupting a target that legitimately
            // carries SDK id 2 or 3.
            if (sdkLanguageById.TryGetValue(translation.LanguageId, out var sdkAtSameId)
                && sdkAtSameId.LanguageCode == code)
            {
                continue;
            }

            // The id points at the wrong (or no) language: resolve the intended language by code.
            var sdkLanguage = sdkLanguages.FirstOrDefault(x => x.LanguageCode == code);
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
