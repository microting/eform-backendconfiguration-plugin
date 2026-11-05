#nullable enable
using System.Collections.Generic;
using BackendConfiguration.Pn.Infrastructure.Models;

namespace BackendConfiguration.Pn.Services.Leder;

/// <summary>
/// The Leder rules of a Medarbejdere save, as pure functions the Helper asks before any write. A refusal
/// is a <see cref="LederMessages"/> key, null = allowed. A null caller is an internal call: no rule applies, nothing
/// is written. There is no access condition (D-L16): an admin may flag any worker.
/// </summary>
public static class LederSaveRules
{
    /// <summary>
    /// An empty address or the synthetic placeholder user_…@microting.invalid (what UpdateDeviceUser generates) shows no
    /// login in the list. Any other address is listed, whatever it contains.
    /// </summary>
    public static bool IsListedLoginAddress(string? email) =>
        !string.IsNullOrWhiteSpace(email) &&
        !email.Trim().EndsWith("@microting.invalid", System.StringComparison.OrdinalIgnoreCase);

    /// <param name="workerIsLeder">Any live site of the edited SDK worker has a live flag.</param>
    /// <param name="emailOrUserNameChanged">This save changes the worker's e-mail or renames its login.</param>
    public static string? UpdateRefusal(LederCaller? caller, DeviceUserModel model,
        IReadOnlyCollection<int> liveStoredTagIds, bool workerIsLeder, bool emailOrUserNameChanged)
    {
        if (caller == null)
        {
            return null;
        }

        // Only a Leder or admin may change which worker tags a worker has (D-L12); an unchanged set passes.
        if (!(caller.IsAdmin || caller.IsLederOrAdmin) && !new HashSet<int>(model.Tags ?? []).SetEquals(liveStoredTagIds))
        {
            return LederMessages.WorkerTagsRequireLeder;
        }

        // Only an admin may move a Leder worker to another address or rename its login (H2): the address decides
        // which login holds the Leder rights of its sites.
        return !caller.IsAdmin && workerIsLeder && emailOrUserNameChanged
            ? LederMessages.LederLoginChangeRequiresAdmin
            : null;
    }

    /// <summary>A new site has no flag, so only the worker-tag rule (D-L12) applies.</summary>
    public static string? CreateRefusal(LederCaller? caller, DeviceUserModel model) =>
        caller != null && !(caller.IsAdmin || caller.IsLederOrAdmin) && model.Tags is { Count: > 0 }
            ? LederMessages.WorkerTagsRequireLeder
            : null;

    /// <summary>The flag write of an allowed save, admins only (D-L2): true = grant, false = revoke, null = leave it.</summary>
    public static bool? FlagChange(LederCaller? caller, DeviceUserModel model) =>
        caller is { IsAdmin: true } ? model.IsLeader : null;
}
