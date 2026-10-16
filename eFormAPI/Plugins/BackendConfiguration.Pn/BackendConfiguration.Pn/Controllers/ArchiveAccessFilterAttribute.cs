#nullable enable
namespace BackendConfiguration.Pn.Controllers;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Services.ArchiveAccess;

/// <summary>
/// Archive endpoints (files, archive folders, inbox): a disabled caller (D7 - resigned or IsActive=false, admins
/// included) gets 403 <c>{"error":"account_disabled"}</c> before the action runs, so the app can sign out. A
/// service that throws <see cref="ArchiveNotFoundException"/> becomes a plain 404 (unreadable looks like missing),
/// and <see cref="ArchiveAccountDisabledException"/> thrown later in the request becomes the same 403.
/// Authorization ([Authorize], roles) runs before this filter.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ArchiveAccessFilterAttribute : Attribute, IAsyncActionFilter
{
    public const string AccountDisabled = "account_disabled";

    public static ObjectResult AccountDisabledResult() =>
        new(new Dictionary<string, string> { ["error"] = AccountDisabled })
        {
            StatusCode = StatusCodes.Status403Forbidden
        };

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var access = context.HttpContext.RequestServices.GetRequiredService<IArchiveAccessService>();
        if ((await access.GetContextAsync()).IsDisabled)
        {
            context.Result = AccountDisabledResult();
            return;
        }

        var executed = await next();
        switch (executed.Exception)
        {
            case ArchiveAccountDisabledException:
                executed.Result = AccountDisabledResult();
                executed.ExceptionHandled = true;
                break;
            case ArchiveNotFoundException:
                executed.Result = new NotFoundResult();
                executed.ExceptionHandled = true;
                break;
        }
    }
}
