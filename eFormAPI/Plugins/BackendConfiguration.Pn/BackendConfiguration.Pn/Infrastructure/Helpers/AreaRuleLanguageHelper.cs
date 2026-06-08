using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure;
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
}
