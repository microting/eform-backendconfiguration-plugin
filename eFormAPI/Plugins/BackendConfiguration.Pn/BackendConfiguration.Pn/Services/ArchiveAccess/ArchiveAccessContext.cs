#nullable enable
using System.Collections.Generic;

namespace BackendConfiguration.Pn.Services.ArchiveAccess;

/// <summary>
/// Who is calling. <see cref="IsDisabled"/> wins over everything, including admin. <see cref="CanManage"/> (enabled and
/// admin or Leder) is what every access rule reads; <see cref="IsAdmin"/> is for display only.
/// </summary>
public sealed record ArchiveAccessContext(bool IsDisabled, bool IsAdmin, IReadOnlyCollection<int> UserGroupIds,
    bool CanManage)
{
    public static ArchiveAccessContext Disabled { get; } = new(true, false, [], false);
}
