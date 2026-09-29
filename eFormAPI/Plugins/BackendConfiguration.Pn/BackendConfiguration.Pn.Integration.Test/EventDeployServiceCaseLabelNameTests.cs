/*
The MIT License (MIT)

Copyright (c) 2007 - 2026 Microting A/S

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.
*/

namespace BackendConfiguration.Pn.Integration.Test;

using System;
using System.Threading;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.EventDeployService;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Enums;
using NSubstitute;

/// <summary>
/// #1324 — the task name on a device case (<c>MainElement.Label</c>, built in
/// <c>EventDeployService.CreateSdkCaseForRotationAsync</c>) comes from
/// <see cref="EventDeployService.ResolveCaseLabelNameAsync"/>: the worker's language,
/// else Danish, else the first non-empty translation.
///
/// The resolver is tested directly because the label is not observable after a deploy:
/// <c>Core.CaseCreateLocalOnly</c> only persists a Cases row keyed on
/// <c>mainElement.Id</c>, and never writes the label anywhere.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class EventDeployServiceCaseLabelNameTests : TestBaseSetup
{
    private async Task<(EventDeployService Service, int PlanningId)> SeedPlanningAsync(
        params (string LanguageCode, string Name)[] translations)
    {
        await GetCore(); // seeds the SDK languages

        var planning = new Planning
        {
            Enabled = true, RepeatEvery = 1, RepeatType = RepeatType.Week, StartDate = DateTime.UtcNow.Date,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        foreach (var (languageCode, name) in translations)
        {
            await ItemsPlanningPnDbContext.PlanningNameTranslation.AddAsync(new PlanningNameTranslation
            {
                PlanningId = planning.Id, LanguageId = await LanguageIdAsync(languageCode), Name = name,
                WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
            });
        }
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var service = new EventDeployService(
            BackendConfigurationPnDbContext!, ItemsPlanningPnDbContext, Substitute.For<IEFormCoreService>(),
            new ServiceCollection().BuildServiceProvider(), TestContextLogger<EventDeployService>.Instance);
        return (service, planning.Id);
    }

    private async Task<int> LanguageIdAsync(string languageCode) =>
        (await MicrotingDbContext!.Languages.FirstAsync(x => x.LanguageCode == languageCode)).Id;

    private async Task<string?> ResolveForEnglishWorkerAsync(EventDeployService service, int planningId) =>
        await service.ResolveCaseLabelNameAsync(
            planningId, await LanguageIdAsync("en-US"), MicrotingDbContext!, CancellationToken.None);

    [Test]
    public async Task EnglishWorker_OnlyDanishName_GetsTheDanishName()
    {
        var (service, planningId) = await SeedPlanningAsync(("da", "Tjek ventilation"));

        Assert.That(await ResolveForEnglishWorkerAsync(service, planningId), Is.EqualTo("Tjek ventilation"));
    }

    [Test]
    public async Task EnglishWorker_EnglishNameExists_GetsTheEnglishName()
    {
        var (service, planningId) = await SeedPlanningAsync(
            ("da", "Tjek ventilation"), ("en-US", "Check ventilation"));

        Assert.That(await ResolveForEnglishWorkerAsync(service, planningId), Is.EqualTo("Check ventilation"));
    }

    [Test]
    public async Task EnglishWorker_EmptyEnglishName_FallsBackToDanish()
    {
        var (service, planningId) = await SeedPlanningAsync(
            ("en-US", ""), ("da", "Tjek ventilation"));

        Assert.That(await ResolveForEnglishWorkerAsync(service, planningId), Is.EqualTo("Tjek ventilation"));
    }

    [Test]
    public async Task EnglishWorker_NoDanishName_GetsTheOtherTranslation()
    {
        var (service, planningId) = await SeedPlanningAsync(("de-DE", "Lüftung prüfen"));

        Assert.That(await ResolveForEnglishWorkerAsync(service, planningId), Is.EqualTo("Lüftung prüfen"));
    }

    [Test]
    public async Task EnglishWorker_DanishPreferredOverOtherTranslation()
    {
        var (service, planningId) = await SeedPlanningAsync(
            ("de-DE", "Lüftung prüfen"), ("da", "Tjek ventilation"));

        Assert.That(await ResolveForEnglishWorkerAsync(service, planningId), Is.EqualTo("Tjek ventilation"));
    }

    [Test]
    public async Task NoTranslations_ReturnsNull()
    {
        var (service, planningId) = await SeedPlanningAsync();

        Assert.That(await ResolveForEnglishWorkerAsync(service, planningId), Is.Null);
    }
}
