#nullable enable
using System.Collections.Generic;
using BackendConfiguration.Pn.Infrastructure.Models;

namespace BackendConfiguration.Pn.Services.Leder;

/// <summary>
/// What a Medarbejdere save knows about the login the worker resolves to, read before any write.
/// </summary>
/// <param name="Exists">The worker resolves to a login after this save whose address the list shows; false = no login.</param>
/// <param name="IsManaged">The plugin keeps this login's groups (a login this save creates is managed).</param>
/// <param name="IsLeder">The login has a live stored Leder flag.</param>
/// <param name="HadLoginAccess">The login is in "eForm users" or "Kun arkiv" before this save.</param>
/// <param name="WillHaveLoginAccess">The login is in one of them after this save (memberships and the plugin's sync, not the toggles).</param>
/// <param name="EmailOrUserNameChanged">This save changes the login's e-mail or username.</param>
public sealed record LoginFacts(bool Exists, bool IsManaged, bool IsLeder, bool HadLoginAccess,
    bool WillHaveLoginAccess, bool EmailOrUserNameChanged)
{
    public static LoginFacts NoLogin { get; } = new(false, false, false, false, false, false);

    /// <summary>A managed login that had web or archive access and has neither after this save.</summary>
    public bool LosesLoginAccess => IsManaged && HadLoginAccess && !WillHaveLoginAccess;
}

/// <summary>
/// The Leder rules of a Medarbejdere save, as pure functions the Helper asks before any write. A refusal is a
/// <see cref="LederMessages"/> key, null = allowed. A null caller is an internal call: no rule applies, nothing is written.
/// </summary>
public static class LederSaveRules
{
    /// <summary>An empty address or one containing "invalid" (user_…@microting.invalid) shows no login in the list.</summary>
    public static bool IsListedLoginAddress(string? email) =>
        !string.IsNullOrWhiteSpace(email) && !email.Contains("invalid");

    public static string? UpdateRefusal(LederCaller? caller, DeviceUserModel model,
        IReadOnlyCollection<int> liveStoredTagIds, LoginFacts login)
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

        // Only an admin may move a Leder's login to another address or leave a managed one without login access (H2).
        if (!caller.IsAdmin && login.IsLeder && (login.EmailOrUserNameChanged || login.LosesLoginAccess))
        {
            return LederMessages.LederLoginChangeRequiresAdmin;
        }

        return GrantRefusal(caller, model, login);
    }

    /// <summary><paramref name="linkTarget"/>: the login this create links to or creates; null = the worker gets none.</summary>
    public static string? CreateRefusal(LederCaller? caller, DeviceUserModel model, LoginFacts? linkTarget)
    {
        if (caller == null)
        {
            return null;
        }

        if (!(caller.IsAdmin || caller.IsLederOrAdmin) && model.Tags is { Count: > 0 })
        {
            return LederMessages.WorkerTagsRequireLeder;
        }

        var login = linkTarget ?? LoginFacts.NoLogin;
        // Linking a new worker to a Leder's login would attach the new worker to that login, so only an admin may (the flag stays) (H2).
        if (!caller.IsAdmin && login.IsLeder)
        {
            return LederMessages.LederLoginChangeRequiresAdmin;
        }

        return GrantRefusal(caller, model, login);
    }

    /// <summary>
    /// The flag write of an allowed save, admins only (D-L2): true = grant, false = revoke, null = leave it. A Leder
    /// login that no longer shows in the list, or a managed one that loses both login groups, is revoked so no Leder
    /// stays invisible.
    /// </summary>
    public static bool? FlagChange(LederCaller? caller, DeviceUserModel model, LoginFacts login)
    {
        if (caller is not { IsAdmin: true })
        {
            return null;
        }

        if (!login.Exists)
        {
            return login.IsLeder ? false : null;
        }

        if (login.IsLeder && login.LosesLoginAccess)
        {
            return false;
        }

        return model.IsLeader;
    }

    /// <summary>An admin's grant needs a login, and a managed login needs "eForm users" or "Kun arkiv" after the save (M5).</summary>
    private static string? GrantRefusal(LederCaller caller, DeviceUserModel model, LoginFacts login) =>
        caller.IsAdmin && model.IsLeader == true && (!login.Exists || (login.IsManaged && !login.WillHaveLoginAccess))
            ? LederMessages.LederRequiresLogin
            : null;
}
