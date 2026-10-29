#nullable enable
namespace BackendConfiguration.Pn.Controllers;

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Services.ArchiveAccess;

/// <summary>
/// Archive management (folders, Læseadgang, Indbakke, delete documents, rename/delete etiketter): admins and Leders
/// only (D-L3, D-L6). A live check on <see cref="ArchiveAccessContext.CanManage"/>, never a role or policy. An
/// authorization filter, like FirstUserOnlyAttribute, so a refused request is stopped before model binding.
/// <list type="number">
///   <item>Not authenticated: does nothing, so [Authorize] answers 401 and the app renews its token; a 403 here would
///   read as "signed out for good". Because it is a no-op, it is safe in any filter order relative to [Authorize].</item>
///   <item>Disabled (D7): 403 {"error":"account_disabled"}, the same body as <see cref="ArchiveAccessFilterAttribute"/>,
///   which would otherwise only run after this gate.</item>
///   <item>Not admin or Leder: a plain 403.</item>
/// </list>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ArchiveManagerAttribute : Attribute, IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (context.HttpContext.User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        var access = context.HttpContext.RequestServices.GetRequiredService<IArchiveAccessService>();
        var ctx = await access.GetContextAsync();
        if (ctx.IsDisabled)
        {
            context.Result = ArchiveAccessFilterAttribute.AccountDisabledResult();
            return;
        }

        if (!ctx.CanManage)
        {
            context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
        }
    }
}
