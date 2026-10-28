#nullable enable
using System.Threading.Tasks;

namespace BackendConfiguration.Pn.Services.Leder;

/// <summary>
/// The general Leder check (D-L1). Scoped: computed once per request. Live from the databases, never from a JWT claim
/// or a plugin-claim policy, so a pod restart does not lock admins or Leders out (spec section 1a).
/// </summary>
public interface ILederService
{
    /// <summary>
    /// For IUserService.UserId: the JWT admin role; or a live LederAssignments row and a live login (EformUser.IsActive,
    /// a non-resigned SDK worker with its address that has a live SiteWorker); or the admin role / "eForm admins" by name
    /// (Helper.IsAdminAsync). <paramref name="knownLive"/>: the caller's liveness when the caller already resolved it
    /// (ArchiveAccessService); null → this service runs the liveness queries itself. The first call's answer is cached
    /// for the request. UserId &lt; 1 → false. Never throws for "not a Leder".
    /// </summary>
    Task<bool> IsLederOrAdminAsync(bool? knownLive = null);
}
