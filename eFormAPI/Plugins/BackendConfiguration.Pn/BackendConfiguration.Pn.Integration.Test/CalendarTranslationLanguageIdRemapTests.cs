/*
The MIT License (MIT)

Copyright (c) 2007 - 2026 Microting A/S

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction.
*/

namespace BackendConfiguration.Pn.Integration.Test;

using BackendConfiguration.Pn.Infrastructure.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eForm.Infrastructure.Models;

/// <summary>
/// Part A regression coverage for the calendar translation LanguageId bug. The
/// create/edit modal hardcodes the Danish source title's LanguageId to the
/// frontend app-locale id 1, while target languages already carry the real SDK
/// Languages.Id from GET /settings/languages. The SDK Languages table is
/// customer-specific (Danish is NOT guaranteed to be id 1), so a verbatim persist
/// stores the Danish title under a language no reader queries, producing a blank
/// event title.
///
/// <see cref="AreaRuleLanguageHelper.RemapCommonTranslationLanguageIdsAsync"/> must
/// remap the hardcoded app-locale ids to the SDK Languages.Id that actually carries
/// the intended LanguageCode, while leaving true SDK target ids untouched and never
/// dropping an unresolvable translate.
///
/// The SDK pre-seeds default languages as da=1, en-US=2, de-DE=3, so the harness
/// must first force Danish onto a NON-1 id to reproduce the real-tenant condition.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class CalendarTranslationLanguageIdRemapTests : TestBaseSetup
{
    /// <summary>
    /// Forces the SDK Danish row onto a non-1 auto-increment id (reproducing the
    /// real customer 420 condition where da=2), and returns (daId, enUsId). The SDK
    /// default-seeds da=1/en-US=2/de-DE=3; we remove the Danish row and re-insert it
    /// after a filler so it lands well above 1.
    /// </summary>
    private async Task<(int DaId, int EnUsId)> ForceDanishOntoNonOneSdkId()
    {
        var existingDa = await MicrotingDbContext!.Languages
            .FirstOrDefaultAsync(x => x.LanguageCode == "da");
        if (existingDa != null)
        {
            MicrotingDbContext.Languages.Remove(existingDa);
            await MicrotingDbContext.SaveChangesAsync();
        }

        // Bump the auto-increment so the re-inserted Danish row is not id 1.
        await MicrotingDbContext.Languages.AddAsync(
            new Language { Name = "Filler", LanguageCode = "zz-filler", IsActive = false });
        await MicrotingDbContext.SaveChangesAsync();

        var da = new Language { Name = "Dansk", LanguageCode = "da", IsActive = true };
        await MicrotingDbContext.Languages.AddAsync(da);
        await MicrotingDbContext.SaveChangesAsync();

        var enUs = await MicrotingDbContext.Languages.FirstAsync(x => x.LanguageCode == "en-US");
        return (da.Id, enUs.Id);
    }

    [Test]
    public async Task Remap_HardcodedDanishAppLocaleId1_IsRemappedToRealSdkDanishId()
    {
        var (daId, _) = await ForceDanishOntoNonOneSdkId();
        Assert.That(daId, Is.Not.EqualTo(1),
            "Test precondition: SDK Danish id must differ from app-locale id 1 to reproduce the bug");

        var translates = new List<CommonTranslationsModel>
        {
            // Danish source: the frontend hardcodes app-locale id 1.
            new() { LanguageId = 1, Name = "Tank 4 inspektion", Description = "Kontrollér ventiler." },
        };

        await AreaRuleLanguageHelper.RemapCommonTranslationLanguageIdsAsync(
            translates, MicrotingDbContext!, NullLogger.Instance);

        Assert.That(translates[0].LanguageId, Is.EqualTo(daId),
            "Danish translate must be remapped from app-locale id 1 to the real SDK Danish Languages.Id");
        Assert.That(translates[0].Name, Is.EqualTo("Tank 4 inspektion"),
            "Remap must not mutate the translation text");
    }

    [Test]
    public async Task Remap_ValidSdkTargetLanguageId_IsLeftUntouched()
    {
        var (daId, enUsId) = await ForceDanishOntoNonOneSdkId();

        var translates = new List<CommonTranslationsModel>
        {
            // Danish source still arrives as the hardcoded app-locale id 1.
            new() { LanguageId = 1, Name = "Dansk titel" },
            // A target language already carries its real SDK id (from getLanguages()).
            new() { LanguageId = enUsId, Name = "English title" },
        };

        await AreaRuleLanguageHelper.RemapCommonTranslationLanguageIdsAsync(
            translates, MicrotingDbContext!, NullLogger.Instance);

        Assert.Multiple(() =>
        {
            Assert.That(translates[0].LanguageId, Is.EqualTo(daId),
                "Hardcoded Danish app-locale id 1 must be remapped to the SDK Danish id");
            Assert.That(translates[1].LanguageId, Is.EqualTo(enUsId),
                "A translate that already carries a valid SDK id must be left untouched");
        });
    }

    [Test]
    public async Task Remap_DefaultSeededTenant_DanishAlreadyAtId1_IsCorrectNoOp()
    {
        // Default SDK seed leaves Danish at id 1, so the hardcoded app-locale id 1 is
        // already correct: the remap must be a no-op (not corrupt it).
        var existingDa = await MicrotingDbContext!.Languages.FirstAsync(x => x.LanguageCode == "da");
        Assert.That(existingDa.Id, Is.EqualTo(1),
            "Test precondition: default-seeded SDK Danish id is 1");

        var translates = new List<CommonTranslationsModel>
        {
            new() { LanguageId = 1, Name = "Dansk titel" },
        };

        await AreaRuleLanguageHelper.RemapCommonTranslationLanguageIdsAsync(
            translates, MicrotingDbContext, NullLogger.Instance);

        Assert.That(translates[0].LanguageId, Is.EqualTo(1),
            "When SDK Danish already is id 1, app-locale id 1 must be left unchanged");
    }

    [Test]
    public async Task Remap_UnresolvableLanguageId_IsLeftUnchanged_AndTranslationNotDropped()
    {
        // 9999 is neither a known app-locale id nor a valid SDK id.
        var translates = new List<CommonTranslationsModel>
        {
            new() { LanguageId = 9999, Name = "Untouched" },
        };

        await AreaRuleLanguageHelper.RemapCommonTranslationLanguageIdsAsync(
            translates, MicrotingDbContext!, NullLogger.Instance);

        Assert.Multiple(() =>
        {
            Assert.That(translates, Has.Count.EqualTo(1),
                "An unresolvable translate must never be dropped");
            Assert.That(translates[0].LanguageId, Is.EqualTo(9999),
                "An unresolvable translate's LanguageId must be left unchanged");
        });
    }
}
