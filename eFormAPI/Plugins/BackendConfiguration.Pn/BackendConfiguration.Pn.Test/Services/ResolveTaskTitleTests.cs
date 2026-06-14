using System.Collections.Generic;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Test.Services;

[TestFixture]
public class ResolveTaskTitleTests
{
    private static AreaRuleTranslation T(int languageId, string name) =>
        new() { LanguageId = languageId, Name = name };

    [Test]
    public void UserLanguageNonEmpty_Wins()
    {
        var translations = new List<AreaRuleTranslation>
        {
            T(1, "English"),
            T(2, "Dansk")
        };

        var title = BackendConfigurationCalendarService.ResolveTaskTitle(translations, 2, null);

        Assert.That(title, Is.EqualTo("Dansk"));
    }

    [Test]
    public void UserLanguageEmptyString_FallsBackToOtherLanguage()
    {
        // Reproduces the original `??`-only bug: an empty-string name in the
        // user's language must NOT win; we must fall through to another row.
        var translations = new List<AreaRuleTranslation>
        {
            T(1, "English"),
            T(2, "")
        };

        var title = BackendConfigurationCalendarService.ResolveTaskTitle(translations, 2, null);

        Assert.That(title, Is.EqualTo("English"));
    }

    [Test]
    public void UserLanguageWhitespace_FallsBackToOtherLanguage()
    {
        var translations = new List<AreaRuleTranslation>
        {
            T(1, "English"),
            T(2, "   ")
        };

        var title = BackendConfigurationCalendarService.ResolveTaskTitle(translations, 2, null);

        Assert.That(title, Is.EqualTo("English"));
    }

    [Test]
    public void NoTranslations_UsesFinalFallback()
    {
        var title = BackendConfigurationCalendarService.ResolveTaskTitle(
            new List<AreaRuleTranslation>(), 2, "Compliance Item");

        Assert.That(title, Is.EqualTo("Compliance Item"));
    }

    [Test]
    public void NullTranslations_UsesFinalFallback()
    {
        var title = BackendConfigurationCalendarService.ResolveTaskTitle(null, 2, "Compliance Item");

        Assert.That(title, Is.EqualTo("Compliance Item"));
    }

    [Test]
    public void AllEmptyTranslations_FallsBackToFinalFallback()
    {
        var translations = new List<AreaRuleTranslation>
        {
            T(1, ""),
            T(2, "   ")
        };

        var title = BackendConfigurationCalendarService.ResolveTaskTitle(translations, 2, "Compliance Item");

        Assert.That(title, Is.EqualTo("Compliance Item"));
    }

    [Test]
    public void AllEmpty_AndNullFallback_ReturnsEmptyString()
    {
        var translations = new List<AreaRuleTranslation>
        {
            T(1, ""),
            T(2, null)
        };

        var title = BackendConfigurationCalendarService.ResolveTaskTitle(translations, 2, null);

        Assert.That(title, Is.EqualTo(""));
    }

    [Test]
    public void AllEmpty_AndWhitespaceFallback_ReturnsEmptyString()
    {
        var title = BackendConfigurationCalendarService.ResolveTaskTitle(
            new List<AreaRuleTranslation>(), 2, "   ");

        Assert.That(title, Is.EqualTo(""));
    }

    [Test]
    public void UserLanguageMissing_CrossLanguageFallback_WinsOverFinalFallback()
    {
        // Event 113 scenario: translation exists but keyed to a language the
        // reader didn't query — must degrade to the available title, not the
        // (often null) compliance fallback.
        var translations = new List<AreaRuleTranslation>
        {
            T(1, "Available Title")
        };

        var title = BackendConfigurationCalendarService.ResolveTaskTitle(translations, 2, null);

        Assert.That(title, Is.EqualTo("Available Title"));
    }
}
