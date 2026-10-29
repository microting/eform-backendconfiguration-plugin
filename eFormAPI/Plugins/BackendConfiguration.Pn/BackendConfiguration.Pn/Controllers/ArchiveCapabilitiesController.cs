#nullable enable
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.ArchiveFolders;
using BackendConfiguration.Pn.Services.ArchiveAccess;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;

namespace BackendConfiguration.Pn.Controllers;

/// <summary>
/// D-L7: the archive UI (web and app) asks this instead of reading the role. A disabled caller gets the D7 403 from the
/// filter. Never cached: a grant or revoke shows on the next call.
/// </summary>
[Authorize]
[ArchiveAccessFilter]
[Route("api/backend-configuration-pn/archive/capabilities")]
public class ArchiveCapabilitiesController(IArchiveAccessService access, IUserService userService) : Controller
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<OperationDataResult<ArchiveCapabilitiesModel>> Get()
    {
        var ctx = await access.GetEnabledContextAsync();
        var userId = userService.UserId;
        // 0 = not signed in; an empty users table also yields 0, which must never read as "first user".
        var isFirstUser = userId >= 1 && userId == await userService.GetFirstUserIdInDb();
        return new OperationDataResult<ArchiveCapabilitiesModel>(true, new ArchiveCapabilitiesModel
        {
            CanManage = ctx.CanManage,
            CanDelete = ctx.CanManage,
            CanUseInbox = ctx.CanManage,
            IsFirstUser = isFirstUser
        });
    }
}
