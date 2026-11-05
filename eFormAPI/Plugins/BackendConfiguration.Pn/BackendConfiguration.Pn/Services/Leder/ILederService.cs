#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BackendConfiguration.Pn.Services.Leder;

/// <summary>
/// What a caller already knows about the current login: <see cref="IsActive"/> (EformUser.IsActive) and the live SDK
/// sites its e-mail resolves to (CallerSites.ResolveAsync). ArchiveAccessService passes it, so the Leder check adds one
/// indexed plugin-DB query and no SDK query.
/// </summary>
public sealed record LederLiveness(bool IsActive, IReadOnlyCollection<int> LiveSiteIds);

/// <summary>
/// The general Leder check (D-L1). Scoped: computed once per request. Live from the databases, never from a JWT claim
/// or a plugin-claim policy, so a pod restart does not lock admins or Leders out (base spec section 1a).
/// </summary>
public interface ILederService
{
    /// <summary>
    /// For IUserService.UserId: the JWT admin role; or an active login whose live sites include one with a live
    /// LederAssignments row (D-L18); or the admin role / "eForm admins" by name (Helper.IsAdminAsync).
    /// <paramref name="known"/>: the caller's liveness when the caller already resolved it; null → this service resolves
    /// it itself (one login query and the CallerSites queries). The first call's answer is cached for the request.
    /// UserId &lt; 1 → false. Never throws for "not a Leder".
    /// </summary>
    Task<bool> IsLederOrAdminAsync(LederLiveness? known = null);
}
