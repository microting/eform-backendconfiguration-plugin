using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.TaskList;
using BackendConfiguration.Pn.Services.AreaRulePlanningTagPurgeService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskListService;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;

namespace BackendConfiguration.Pn.Controllers;

/// <summary>
/// #1302 — the task-list endpoints open to EVERY authenticated user, not just
/// admins: the inline title rename (#1126) and the purge that runs after the
/// Manage-tags dialog closes. Both were reachable from the page by a `user` role
/// once the page itself opened to them, and both 403'd while they sat on the
/// admin-only <see cref="TaskListController"/>.
///
/// A separate controller rather than a method-level [Authorize] on the admin
/// one, because ASP.NET Core combines authorization attributes: a class-level
/// [Authorize(Roles = EformRole.Admin)] cannot be relaxed by anything on the
/// method. Same route prefix, so the URLs the client calls are unchanged.
///
/// Bare [Authorize] matches <c>CalendarController</c>, which already lets every
/// authenticated user rename a task through <c>PUT calendar/tasks</c> — rename
/// here goes through the same <c>UpdateTask</c>. Do NOT add anything else to this
/// class without a product decision: the batch actions stay admin-only
/// (TaskListControllerAuthorizationTests pins the exact action set).
/// </summary>
[Authorize]
[Route("api/backend-configuration-pn/task-list")]
public class TaskListUserController(
    IBackendConfigurationTaskListService taskListService,
    AreaRulePlanningTagPurgeService tagPurgeService) : Controller
{
    /// <summary>
    /// #1126 — inline rename from the task-list grid row. Single-row action on
    /// the batch rail (one-element TaskIds), so it applies the same empty-TaskIds
    /// guard as the batch endpoints; the empty/whitespace TITLE guard lives in
    /// the service, pre-loop.
    /// </summary>
    [HttpPost("rename")]
    public async Task<OperationResult> Rename([FromBody] TaskListRenameModel model)
        => model == null || model.TaskIds == null || model.TaskIds.Count == 0
            ? new OperationResult(false, "TaskIds must not be empty")
            : await taskListService.Rename(model);

    /// <summary>
    /// Soft-deletes AreaRulePlanningTag rows whose ItemPlanningTagId names a
    /// PlanningTag that has been removed (or never existed). Called by the task-list
    /// page right after the Manage-tags dialog closes, so a tag deleted there stops
    /// being referenced immediately instead of waiting for the next plugin start.
    ///
    /// A dedicated endpoint rather than folding the purge into the task index: the
    /// index is a read path and must not write.
    ///
    /// Takes no body, so there is no caller-supplied input to validate. Open to
    /// every authenticated user (#1302): the call is parameterless and idempotent,
    /// and can only remove rows that already point at a tag that no longer exists.
    /// </summary>
    [HttpPost("purge-orphan-tags")]
    public async Task<OperationDataResult<int>> PurgeOrphanTags()
        => new OperationDataResult<int>(
            true,
            await tagPurgeService.PurgeOrphanedAreaRulePlanningTagsAsync());
}
