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
/// create/edit modal hardcoded the Danish source title's LanguageId to the frontend
/// app-locale id 1, while target languages already carry the real SDK Languages.Id
/// from GET /settings/languages. The SDK Languages table is customer-specific
/// (Danish is NOT guaranteed to be id 1, and the ids are NOT guaranteed to be the
/// contiguous set {1,2,3}), so a verbatim persist stores the Danish title under a
/// language no reader queries, producing a blank event title.
///
/// <see cref="AreaRuleLanguageHelper.RemapCommonTranslationLanguageIdsAsync"/> must
/// be EXISTENCE-based: only an id that is ABSENT from the SDK Languages table is
/// remapped (via the static app-locale map) to the SDK Languages.Id that carries the
/// intended LanguageCode. Any id that EXISTS in SDK Languages is a real target id and
/// must be left untouched — even when it is 2, 3 or 4 — and unresolvable translates
/// are never dropped.
///
/// These tests reproduce a SHIFTED-ID tenant (da=2, en-US=3, de-DE=4, NO id 1) to
/// prove the safety property: a valid target id such as en-US=3 must NOT be reinterpreted
/// via the static map (which historically mis-keyed it to de-DE), which was the corruption
/// bug this change fixes.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class CalendarTranslationLanguageIdRemapTests : TestBaseSetup
{
    /// <summary>
    /// Reproduces a shifted-id tenant: removes the default-seeded da/en-US/de-DE rows,
    /// consumes id 1 with a filler so it is ABSENT for the language codes, then re-inserts
    /// da, en-US and de-DE so they land on ascending ids &gt; 1 (typically 2, 3, 4). Returns
    /// the resolved (daId, enUsId, deDeId). Guarantees no SDK Languages row has id 1.
    /// </summary>
    private async Task<(int DaId, int EnUsId, int DeDeId)> ForceShiftedIdTenant()
    {
        var codes = new[] { "da", "en-US", "de-DE" };
        var existing = await MicrotingDbContext!.Languages
            .Where(x => codes.Contains(x.LanguageCode))
            .ToListAsync();
        if (existing.Count > 0)
        {
            MicrotingDbContext.Languages.RemoveRange(existing);
            await MicrotingDbContext.SaveChangesAsync();
        }

        // Consume id 1 with a filler so that no language code resolves to id 1.
        await MicrotingDbContext.Languages.AddAsync(
            new Language { Name = "Filler", LanguageCode = "zz-filler", IsActive = false });
        await MicrotingDbContext.SaveChangesAsync();

        var da = new Language { Name = "Dansk", LanguageCode = "da", IsActive = true };
        await MicrotingDbContext.Languages.AddAsync(da);
        await MicrotingDbContext.SaveChangesAsync();

        var enUs = new Language { Name = "English", LanguageCode = "en-US", IsActive = true };
        await MicrotingDbContext.Languages.AddAsync(enUs);
        await MicrotingDbContext.SaveChangesAsync();

        var deDe = new Language { Name = "Deutsch", LanguageCode = "de-DE", IsActive = true };
        await MicrotingDbContext.Languages.AddAsync(deDe);
        await MicrotingDbContext.SaveChangesAsync();

        // The existence-based remap keys purely off whether the incoming id is present in
        // SDK Languages, so id 1 must be absent from the WHOLE table for app-locale id 1 to
        // be treated as the (absent) hardcoded source. The default seed put da on id 1, which
        // we deleted; auto-increment never reuses it, so id 1 is now free.
        var hasIdOne = await MicrotingDbContext.Languages.AnyAsync(x => x.Id == 1);
        Assert.That(hasIdOne, Is.False,
            "Test precondition: no SDK Languages row may sit on id 1 (shifted-id tenant)");

        return (da.Id, enUs.Id, deDe.Id);
    }

    [Test]
    public async Task Remap_AbsentAppLocaleId1_IsRemappedToRealSdkDanishId()
    {
        var (daId, _, _) = await ForceShiftedIdTenant();
        Assert.That(daId, Is.Not.EqualTo(1),
            "Test precondition: SDK Danish id must differ from app-locale id 1 to reproduce the bug");

        var translates = new List<CommonTranslationsModel>
        {
            // Danish source: the frontend formerly hardcoded app-locale id 1, which is
            // ABSENT from this tenant's SDK Languages and must be remapped to da's real id.
            new() { LanguageId = 1, Name = "Tank 4 inspektion", Description = "Kontrollér ventiler." },
        };

        await AreaRuleLanguageHelper.RemapCommonTranslationLanguageIdsAsync(
            translates, MicrotingDbContext!, NullLogger.Instance);

        Assert.That(translates[0].LanguageId, Is.EqualTo(daId),
            "Absent app-locale id 1 must be remapped to the real SDK Danish Languages.Id");
        Assert.That(translates[0].Name, Is.EqualTo("Tank 4 inspektion"),
            "Remap must not mutate the translation text");
    }

    [Test]
    public async Task Remap_ValidEnUsSdkId_IsLeftUnchanged_NoCorruption()
    {
        // Regression guard for the corruption bug: a valid en-US SDK id collides with
        // the static app-locale map's en-US slot (2) only by accident; the OLD value-based
        // guard remapped any present id 1/2/3 whose SDK code mismatched the static code,
        // corrupting good targets. The existence-based guard must leave any present id alone.
        var (_, enUsId, deDeId) = await ForceShiftedIdTenant();

        var translates = new List<CommonTranslationsModel>
        {
            new() { LanguageId = enUsId, Name = "English title" },
        };

        await AreaRuleLanguageHelper.RemapCommonTranslationLanguageIdsAsync(
            translates, MicrotingDbContext!, NullLogger.Instance);

        Assert.That(translates[0].LanguageId, Is.EqualTo(enUsId),
            "A valid SDK en-US id must be left unchanged — never reinterpreted via the static map");
        Assert.That(translates[0].LanguageId, Is.Not.EqualTo(deDeId),
            "The corruption bug remapped a valid en-US id onto de-DE; this must not happen");
    }

    [Test]
    public async Task Remap_ValidDeDeSdkId_IsLeftUnchanged()
    {
        var (_, _, deDeId) = await ForceShiftedIdTenant();

        var translates = new List<CommonTranslationsModel>
        {
            new() { LanguageId = deDeId, Name = "Deutscher Titel" },
        };

        await AreaRuleLanguageHelper.RemapCommonTranslationLanguageIdsAsync(
            translates, MicrotingDbContext!, NullLogger.Instance);

        Assert.That(translates[0].LanguageId, Is.EqualTo(deDeId),
            "A valid SDK de-DE id must be left unchanged");
    }

    [Test]
    public async Task Remap_MixedSourceAndTargets_OnShiftedTenant_OnlyAbsentIdRemapped()
    {
        var (daId, enUsId, deDeId) = await ForceShiftedIdTenant();

        var translates = new List<CommonTranslationsModel>
        {
            // Danish source still arrives as the absent app-locale id 1.
            new() { LanguageId = 1, Name = "Dansk titel" },
            // Targets already carry their true SDK ids (from getLanguages()).
            new() { LanguageId = enUsId, Name = "English title" },
            new() { LanguageId = deDeId, Name = "Deutscher Titel" },
        };

        await AreaRuleLanguageHelper.RemapCommonTranslationLanguageIdsAsync(
            translates, MicrotingDbContext!, NullLogger.Instance);

        Assert.Multiple(() =>
        {
            Assert.That(translates[0].LanguageId, Is.EqualTo(daId),
                "Absent Danish app-locale id 1 must be remapped to the SDK Danish id");
            Assert.That(translates[1].LanguageId, Is.EqualTo(enUsId),
                "A translate that already carries a valid SDK en-US id must be left untouched");
            Assert.That(translates[2].LanguageId, Is.EqualTo(deDeId),
                "A translate that already carries a valid SDK de-DE id must be left untouched");
        });
    }

    [Test]
    public async Task Remap_AbsentIdNotInStaticMap_IsLeftUnchanged_AndTranslationNotDropped()
    {
        await ForceShiftedIdTenant();

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
                "An absent id with no static-map entry must be left unchanged");
        });
    }
}
