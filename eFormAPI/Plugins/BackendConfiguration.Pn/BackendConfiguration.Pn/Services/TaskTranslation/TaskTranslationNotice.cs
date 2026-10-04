#nullable enable
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;

namespace BackendConfiguration.Pn.Services.TaskTranslation;

/// <summary>
/// #1384 — the notice a save carries when a recipient language kept the Danish text
/// only. Appended to a success message by the wizard and the calendar, and recognised
/// again by the task-list batch summary, which otherwise drops per-task messages.
/// </summary>
public static class TaskTranslationNotice
{
    private const string Key = "TaskSavedWithoutTranslation";

    public static string Append(IBackendConfigurationLocalizationService localizationService, string message,
        bool translationsIncomplete) =>
        translationsIncomplete ? $"{message} {localizationService.GetString(Key)}" : message;

    public static bool IsIn(IBackendConfigurationLocalizationService localizationService, string? message) =>
        message != null && localizationService.GetString(Key) is { Length: > 0 } notice && message.Contains(notice);
}
