#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Models;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Abstractions.Translation;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Data;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;

namespace BackendConfiguration.Pn.Services.TaskTranslation;

/// <inheritdoc />
/// <remarks>
/// Same rules as the calendar dialog's save-time fill (#1324 v1): the targets are the
/// active, non-Danish languages of the recipients; Danish is recognised by its language
/// code, never by id, because the SDK Languages table differs per installation. A
/// language whose code is Danish is never a target, so a "da → da" request cannot happen.
///
/// <para>
/// The translator is the host's <see cref="ITranslationService"/> from eFormApi.BasePn,
/// resolved per call and optional: a host that does not register it, has no API key, or
/// ships an eFormApi.BasePn without the type, leaves the task in Danish.
/// </para>
///
/// <para>
/// Per instance (transient: one per consuming service), a translation is asked for once per
/// (text, target), successful or not, and after a call THROWS no more are attempted: a
/// translator that is unreachable then costs one failure per save, not one per field and
/// event. An unsuccessful answer is per language (e.g. an unsupported pair) and does not
/// stop the other languages.
/// </para>
/// </remarks>
public class TaskTranslationFiller(
    IEFormCoreService coreHelper,
    BackendConfigurationPnDbContext backendConfigurationPnDbContext,
    ItemsPlanningPnDbContext itemsPlanningPnDbContext,
    IServiceProvider serviceProvider,
    ILogger<TaskTranslationFiller> logger) : ITaskTranslationFiller
{
    private const string DanishCode = "da";

    // AreaRuleTranslation.Name and PlanningNameTranslation.Name are varchar(250).
    private const int NameMaxLength = 250;

    private sealed record TargetLanguage(int Id, string Code);

    private readonly Dictionary<(string Text, string Target), string?> _translated = new();
    private bool _translatorFailed;

    public async Task<bool> FillMissingAsync(
        List<CommonTranslationsModel> translates,
        IEnumerable<int> recipientSiteIds,
        IReadOnlyCollection<AreaRuleTranslation> existing,
        CancellationToken ct = default)
    {
        var (danishId, targets) = await ResolveTargetsAsync(recipientSiteIds, ct).ConfigureAwait(false);
        if (targets.Count == 0)
        {
            return true;
        }

        var danish = translates.FirstOrDefault(t => t.LanguageId == danishId);
        if (danish == null)
        {
            return true;
        }

        var stored = existing
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .GroupBy(x => x.LanguageId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Id).First());

        return await TranslateMissingAsync(danish.Name, danish.Description, targets,
            languageId =>
            {
                // A language the request carries is saved as sent; one it leaves out
                // keeps its stored text, which the upsert never touches.
                var entry = translates.FirstOrDefault(t => t.LanguageId == languageId);
                if (entry != null)
                {
                    return (entry.Name, entry.Description);
                }

                stored.TryGetValue(languageId, out var row);
                return (row?.Name, row?.Description);
            },
            (languageId, name, description) =>
            {
                var entry = translates.FirstOrDefault(t => t.LanguageId == languageId);
                if (entry == null)
                {
                    // Seed the new entry from the stored row: the upsert writes Name AND
                    // Description, so the field not being filled must keep its text.
                    stored.TryGetValue(languageId, out var row);
                    entry = new CommonTranslationsModel
                    {
                        LanguageId = languageId,
                        Name = row?.Name,
                        Description = row?.Description
                    };
                    translates.Add(entry);
                }

                if (name != null)
                {
                    entry.Name = name;
                }

                if (description != null)
                {
                    entry.Description = description;
                }

                return Task.CompletedTask;
            }, ct).ConfigureAwait(false);
    }

    public async Task<bool> FillMissingForEventAsync(int areaRuleId, int planningId,
        IReadOnlyCollection<int> recipientSiteIds, CancellationToken ct = default)
    {
        var (danishId, targets) = await ResolveTargetsAsync(recipientSiteIds, ct).ConfigureAwait(false);
        if (targets.Count == 0)
        {
            return true;
        }

        var ruleTranslations = await backendConfigurationPnDbContext.AreaRuleTranslations
            .Where(x => x.AreaRuleId == areaRuleId && x.WorkflowState != Constants.WorkflowStates.Removed)
            .OrderBy(x => x.Id)
            .ToListAsync(ct).ConfigureAwait(false);
        var nameTranslations = await itemsPlanningPnDbContext.PlanningNameTranslation
            .Where(x => x.PlanningId == planningId && x.WorkflowState != Constants.WorkflowStates.Removed)
            .OrderBy(x => x.Id)
            .ToListAsync(ct).ConfigureAwait(false);

        var danishRule = ruleTranslations.FirstOrDefault(x => x.LanguageId == danishId);
        var danishName = Blank(danishRule?.Name)
            ? nameTranslations.FirstOrDefault(x => x.LanguageId == danishId)?.Name
            : danishRule!.Name;

        var complete = await TranslateMissingAsync(danishName, danishRule?.Description, targets,
            languageId =>
            {
                var row = ruleTranslations.FirstOrDefault(x => x.LanguageId == languageId);
                return (row?.Name, row?.Description);
            },
            async (languageId, name, description) =>
            {
                var row = ruleTranslations.FirstOrDefault(x => x.LanguageId == languageId);
                if (row == null)
                {
                    row = new AreaRuleTranslation
                    {
                        AreaRuleId = areaRuleId,
                        LanguageId = languageId,
                        Name = name,
                        Description = description
                    };
                    await row.Create(backendConfigurationPnDbContext).ConfigureAwait(false);
                    ruleTranslations.Add(row);
                    return;
                }

                row.Name = name ?? row.Name;
                row.Description = description ?? row.Description;
                await row.Update(backendConfigurationPnDbContext).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);

        // The device label reads PlanningNameTranslation in the worker's language, so
        // fill an empty one from that language's title — even when the title was not
        // translated just now (written by hand on a path that only saved the AreaRule).
        foreach (var target in targets)
        {
            var title = ruleTranslations.FirstOrDefault(x => x.LanguageId == target.Id)?.Name;
            if (Blank(title))
            {
                continue;
            }

            var nameRow = nameTranslations.FirstOrDefault(x => x.LanguageId == target.Id);
            if (nameRow == null)
            {
                await new PlanningNameTranslation
                {
                    PlanningId = planningId,
                    LanguageId = target.Id,
                    Name = title
                }.Create(itemsPlanningPnDbContext).ConfigureAwait(false);
            }
            else if (Blank(nameRow.Name))
            {
                nameRow.Name = title;
                await nameRow.Update(itemsPlanningPnDbContext).ConfigureAwait(false);
            }
        }

        return complete;
    }

    /// <summary>
    /// For every target whose title or description is empty while the Danish one is not,
    /// translates the Danish text and hands it to <paramref name="apply"/> (null for a
    /// field that needs nothing). Returns false when a needed translation was not obtained.
    /// </summary>
    private async Task<bool> TranslateMissingAsync(
        string? danishName,
        string? danishDescription,
        List<TargetLanguage> targets,
        Func<int, (string? Name, string? Description)> current,
        Func<int, string?, string?, Task> apply,
        CancellationToken ct)
    {
        var jobs = targets
            .Select(target =>
            {
                var (name, description) = current(target.Id);
                return (Target: target,
                    NeedsName: Blank(name) && !Blank(danishName),
                    NeedsDescription: Blank(description) && !Blank(danishDescription));
            })
            .Where(job => job.NeedsName || job.NeedsDescription)
            .ToList();
        if (jobs.Count == 0)
        {
            return true;
        }

        var translate = ResolveTranslator();
        if (translate == null)
        {
            logger.LogWarning(
                "TaskTranslationFiller: translation is unavailable; {Count} language(s) keep the Danish text only",
                jobs.Count);
            return false;
        }

        var complete = true;
        foreach (var job in jobs)
        {
            ct.ThrowIfCancellationRequested();
            var name = job.NeedsName ? await TranslateAsync(translate, danishName!, job.Target).ConfigureAwait(false) : null;
            var description = job.NeedsDescription
                ? await TranslateAsync(translate, danishDescription!, job.Target).ConfigureAwait(false)
                : null;

            complete &= (name != null || !job.NeedsName) && (description != null || !job.NeedsDescription);
            if (name != null || description != null)
            {
                await apply(job.Target.Id, Truncate(name, NameMaxLength), description).ConfigureAwait(false);
            }
        }

        return complete;
    }

    private async Task<string?> TranslateAsync(
        Func<string, string, Task<OperationDataResult<string>>> translate, string text, TargetLanguage target)
    {
        if (_translated.TryGetValue((text, target.Code), out var cached))
        {
            return cached;
        }

        if (_translatorFailed)
        {
            return null;
        }

        string? translated = null;
        try
        {
            var result = await translate(text, target.Code).ConfigureAwait(false);
            if (result is { Success: true } && !Blank(result.Model))
            {
                translated = result.Model;
            }
            else
            {
                logger.LogWarning("TaskTranslationFiller: translation to {Code} failed: {Message}",
                    target.Code, result?.Message);
            }
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "TaskTranslationFiller: translation to {Code} threw; no more are attempted",
                target.Code);
            _translatorFailed = true;
        }

        _translated[(text, target.Code)] = translated;
        return translated;
    }

    /// <summary>
    /// The active, non-Danish languages of <paramref name="siteIds"/>, and the id of Danish
    /// (the source language), resolved by code.
    /// </summary>
    private async Task<(int? DanishId, List<TargetLanguage> Targets)> ResolveTargetsAsync(
        IEnumerable<int> siteIds, CancellationToken ct)
    {
        var ids = siteIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return (null, []);
        }

        var core = await coreHelper.GetCore().ConfigureAwait(false);
        await using var sdkDbContext = core.DbContextHelper.GetDbContext();

        var languageIds = await sdkDbContext.Sites
            .Where(x => ids.Contains(x.Id))
            .Select(x => x.LanguageId)
            .Distinct()
            .ToListAsync(ct).ConfigureAwait(false);

        var languages = await sdkDbContext.Languages
            .Where(x => languageIds.Contains(x.Id) || x.LanguageCode == DanishCode)
            .Select(x => new { x.Id, x.LanguageCode, x.IsActive })
            .ToListAsync(ct).ConfigureAwait(false);

        int? danishId = languages.FirstOrDefault(x => x.LanguageCode == DanishCode)?.Id;
        var targets = languages
            .Where(x => languageIds.Contains(x.Id) && x.IsActive && x.Id != danishId)
            .Where(x => !Blank(x.LanguageCode) && !IsDanish(x.LanguageCode))
            .Select(x => new TargetLanguage(x.Id, x.LanguageCode))
            .ToList();
        return (danishId, targets);
    }

    /// <summary>
    /// The host's translator, or null when the host has none: not registered, not
    /// configured, or an eFormApi.BasePn too old to know the type. The type is touched
    /// only inside <see cref="ResolveFromHost"/>, so on an old host its JIT failure lands
    /// in this catch instead of failing the save.
    /// </summary>
    private Func<string, string, Task<OperationDataResult<string>>>? ResolveTranslator()
    {
        try
        {
            return ResolveFromHost(serviceProvider);
        }
        catch (Exception e) when (e is TypeLoadException or FileNotFoundException or FileLoadException
                                      or MissingMethodException)
        {
            logger.LogWarning(e, "TaskTranslationFiller: the host provides no translation service");
            return null;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Func<string, string, Task<OperationDataResult<string>>>? ResolveFromHost(
        IServiceProvider serviceProvider)
    {
        var service = serviceProvider.GetService<ITranslationService>();
        if (service is not { IsConfigured: true })
        {
            return null;
        }

        return (text, targetCode) => service.TranslateText(text, DanishCode, targetCode);
    }

    private static bool IsDanish(string code) =>
        string.Equals(code.Split('-')[0], DanishCode, StringComparison.OrdinalIgnoreCase);

    private static bool Blank(string? text) => string.IsNullOrWhiteSpace(text);

    private static string? Truncate(string? text, int maxLength) =>
        text is { Length: > 0 } && text.Length > maxLength ? text[..maxLength] : text;
}
