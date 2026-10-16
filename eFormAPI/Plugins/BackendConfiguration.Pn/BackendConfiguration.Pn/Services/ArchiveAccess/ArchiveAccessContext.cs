#nullable enable
using System.Collections.Generic;

namespace BackendConfiguration.Pn.Services.ArchiveAccess;

/// <summary>
/// Who is calling, for archive access (spec section 3). <see cref="IsDisabled"/> wins over everything, including
/// the admin role (D7). <see cref="UserGroupIds"/> are the live SDK tag ids (medarbejdergrupper) of the caller's
/// sites; empty when no worker is linked to the caller's email.
/// </summary>
public sealed record ArchiveAccessContext(bool IsDisabled, bool IsAdmin, IReadOnlyCollection<int> UserGroupIds)
{
    public static ArchiveAccessContext Disabled { get; } = new(true, false, []);
}
