#nullable enable
using BackendConfiguration.Pn.Infrastructure.Models;
using BackendConfiguration.Pn.Services.Leder;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>The Leder refusals and the flag decision of a Medarbejdere save, before any write (spec section 4). No database.</summary>
[TestFixture]
public class LederSaveRulesTests
{
    private const int Actor = 7;
    private static readonly LederCaller Admin = new(true, true, Actor);
    private static readonly LederCaller NonAdmin = new(false, false, Actor);
    private static readonly LederCaller LederNonAdmin = new(false, true, Actor);

    private static DeviceUserModel Model(bool? isLeader = null, List<int>? tags = null) =>
        new() { IsLeader = isLeader, Tags = tags ?? [] };

    /// <summary>A plugin-managed login (eForm users / Kun arkiv / Kun tid / none).</summary>
    private static LoginFacts Managed(bool isLeder = false, bool had = true, bool will = true, bool emailChanged = false) =>
        new(Exists: true, IsManaged: true, IsLeder: isLeder, HadLoginAccess: had, WillHaveLoginAccess: will,
            EmailOrUserNameChanged: emailChanged);

    /// <summary>A login in an owner's own group: the plugin never touches its groups, so had == will.</summary>
    private static LoginFacts Unmanaged(bool isLeder = false, bool had = false, bool will = false) =>
        new(Exists: true, IsManaged: false, IsLeder: isLeder, HadLoginAccess: had, WillHaveLoginAccess: will,
            EmailOrUserNameChanged: false);

    private static string? Update(LederCaller? caller, DeviceUserModel model, LoginFacts login,
        IReadOnlyCollection<int>? stored = null) =>
        LederSaveRules.UpdateRefusal(caller, model, stored ?? [], login);

    [Test]
    public void NoCaller_NoRuleApplies_AndNothingIsWritten()
    {
        Assert.That(Update(null, Model(true, [5]), LoginFacts.NoLogin), Is.Null);
        Assert.That(LederSaveRules.CreateRefusal(null, Model(true, [5]), null), Is.Null);
        Assert.That(LederSaveRules.FlagChange(null, Model(true), Managed()), Is.Null);
    }

    [TestCase(true)]
    [TestCase(false)]
    [TestCase(null)]
    public void NonAdmin_IsLeaderIsIgnored_NeitherRefusedNorWritten(bool? isLeader)
    {
        Assert.That(Update(NonAdmin, Model(isLeader), Managed()), Is.Null);
        Assert.That(LederSaveRules.FlagChange(NonAdmin, Model(isLeader), Managed()), Is.Null);
        Assert.That(LederSaveRules.FlagChange(LederNonAdmin, Model(isLeader), Managed()), Is.Null);
    }

    [Test]
    public void NonAdmin_Grant_IsNeverRefusedForMissingLoginOrAccess()
    {
        Assert.That(Update(NonAdmin, Model(true), LoginFacts.NoLogin), Is.Null);
        Assert.That(Update(NonAdmin, Model(true), Managed(will: false)), Is.Null);
        Assert.That(LederSaveRules.CreateRefusal(NonAdmin, Model(true), null), Is.Null);
    }

    [Test]
    public void Admin_GrantOnAManagedLoginWithAccess_PassesAndGrants()
    {
        Assert.That(Update(Admin, Model(true), Managed()), Is.Null);
        Assert.That(LederSaveRules.FlagChange(Admin, Model(true), Managed()), Is.True);
    }

    [Test]
    public void Admin_Grant_NoLogin_IsLederRequiresLogin_RevokeIsANoOpSuccess()
    {
        Assert.That(Update(Admin, Model(true), LoginFacts.NoLogin), Is.EqualTo(LederMessages.LederRequiresLogin));
        Assert.That(Update(Admin, Model(false), LoginFacts.NoLogin), Is.Null);
        Assert.That(LederSaveRules.FlagChange(Admin, Model(false), LoginFacts.NoLogin), Is.Null);
    }

    [Test]
    public void Admin_Grant_ManagedLoginWithNeitherGroupAfterTheSave_IsRefused() =>
        Assert.That(Update(Admin, Model(true), Managed(had: true, will: false)), Is.EqualTo(LederMessages.LederRequiresLogin));

    [Test]
    public void Admin_Grant_TogetherWithSwitchingWebOn_Passes() =>
        Assert.That(Update(Admin, Model(true), Managed(had: false, will: true)), Is.Null);

    [Test]
    public void Admin_Grant_UnmanagedLogin_HasNoWebOrArchiveCondition()
    {
        Assert.That(Update(Admin, Model(true), Unmanaged()), Is.Null);
        Assert.That(LederSaveRules.FlagChange(Admin, Model(true), Unmanaged()), Is.True);
    }

    [Test]
    public void Admin_NullIsUnchanged()
    {
        Assert.That(LederSaveRules.FlagChange(Admin, Model(null), Managed(isLeder: true)), Is.Null);
        Assert.That(Update(Admin, Model(null), LoginFacts.NoLogin), Is.Null);
    }

    [Test]
    public void Admin_ManagedLederLosesWebAndArchive_AutoRevokes()
    {
        var losing = Managed(isLeder: true, had: true, will: false);
        Assert.That(Update(Admin, Model(null), losing), Is.Null);
        Assert.That(LederSaveRules.FlagChange(Admin, Model(null), losing), Is.False);
    }

    [Test]
    public void Admin_UnmanagedLeder_IsNeverAutoRevoked()
    {
        Assert.That(LederSaveRules.FlagChange(Admin, Model(null), Unmanaged(isLeder: true)), Is.Null);

        // Even if the facts claim a transition, an unmanaged login's groups are not the plugin's to judge.
        var claimsTransition = Unmanaged(isLeder: true, had: true, will: false);
        Assert.That(LederSaveRules.FlagChange(Admin, Model(null), claimsTransition), Is.Null);
        Assert.That(Update(Admin, Model(null), claimsTransition), Is.Null);
        Assert.That(Update(NonAdmin, Model(null), claimsTransition), Is.Null);
    }

    [Test]
    public void Admin_LederWhoseWorkerGetsAnInvalidAddress_IsRevoked()
    {
        var unlisted = new LoginFacts(Exists: false, IsManaged: true, IsLeder: true, HadLoginAccess: true,
            WillHaveLoginAccess: true, EmailOrUserNameChanged: true);
        Assert.That(LederSaveRules.FlagChange(Admin, Model(null), unlisted), Is.False);
        Assert.That(LederSaveRules.FlagChange(Admin, Model(true), unlisted), Is.False);
    }

    [Test]
    public void NonAdminOnALeder_AddressChange_IsRefused()
    {
        Assert.That(Update(NonAdmin, Model(), Managed(isLeder: true, emailChanged: true)),
            Is.EqualTo(LederMessages.LederLoginChangeRequiresAdmin));
        Assert.That(Update(LederNonAdmin, Model(), Managed(isLeder: true, emailChanged: true)),
            Is.EqualTo(LederMessages.LederLoginChangeRequiresAdmin), "a Leder is not an admin either");
    }

    [Test]
    public void NonAdminOnAManagedLeder_LosingWebAndArchive_IsRefused() =>
        Assert.That(Update(NonAdmin, Model(), Managed(isLeder: true, had: true, will: false)),
            Is.EqualTo(LederMessages.LederLoginChangeRequiresAdmin));

    [Test]
    public void NonAdminOnALeder_SwitchingWebToArchive_OrRenaming_Passes()
    {
        Assert.That(Update(NonAdmin, Model(), Managed(isLeder: true)), Is.Null);
        Assert.That(Update(NonAdmin, Model(), Unmanaged(isLeder: true)), Is.Null);
    }

    [Test]
    public void NonAdminOnANonLeder_AddressChange_IsNotALederRule() =>
        Assert.That(Update(NonAdmin, Model(), Managed(isLeder: false, emailChanged: true)), Is.Null);

    [Test]
    public void AdminOnALeder_AddressChange_Passes() =>
        Assert.That(Update(Admin, Model(), Managed(isLeder: true, emailChanged: true)), Is.Null);

    [Test]
    public void NonLeder_ChangedTagSet_IsRefused_SameSetInAnyOrderPasses()
    {
        Assert.That(Update(NonAdmin, Model(tags: [1, 3]), Managed(), stored: [1, 2]),
            Is.EqualTo(LederMessages.WorkerTagsRequireLeder));
        Assert.That(Update(NonAdmin, Model(tags: [2, 1, 2]), Managed(), stored: [1, 2]), Is.Null);
        Assert.That(Update(NonAdmin, Model(tags: []), Managed(), stored: []), Is.Null);
    }

    [Test]
    public void Leder_ChangedTagSet_Passes() =>
        Assert.That(Update(LederNonAdmin, Model(tags: [9]), Managed(), stored: [1]), Is.Null);

    [Test]
    public void TagRule_ComesFirst() =>
        Assert.That(Update(NonAdmin, Model(tags: [9]), Managed(isLeder: true, emailChanged: true), stored: [1]),
            Is.EqualTo(LederMessages.WorkerTagsRequireLeder));

    [Test]
    public void Create_NonLederWithTags_IsRefused_WithoutTagsPasses_LederWithTagsPasses()
    {
        Assert.That(LederSaveRules.CreateRefusal(NonAdmin, Model(tags: [4]), null), Is.EqualTo(LederMessages.WorkerTagsRequireLeder));
        Assert.That(LederSaveRules.CreateRefusal(NonAdmin, Model(), null), Is.Null);
        Assert.That(LederSaveRules.CreateRefusal(LederNonAdmin, Model(tags: [4]), null), Is.Null);
    }

    [Test]
    public void Admin_ChangedTagSet_Passes() =>
        Assert.That(Update(Admin, Model(tags: [9]), Managed(), stored: [1]), Is.Null);

    [Test]
    public void Admin_AlwaysMayChangeTags_EvenWhenTheCallerRecordIsInconsistent()
    {
        var inconsistent = new LederCaller(IsAdmin: true, IsLederOrAdmin: false, UserId: Actor);
        Assert.That(Update(inconsistent, Model(tags: [9]), Managed(), stored: [1]), Is.Null);
        Assert.That(LederSaveRules.CreateRefusal(inconsistent, Model(tags: [4]), null), Is.Null);
    }

    [Test]
    public void Create_TagRuleComesBeforeTheLinkRule() =>
        Assert.That(LederSaveRules.CreateRefusal(NonAdmin, Model(tags: [4]), Managed(isLeder: true)),
            Is.EqualTo(LederMessages.WorkerTagsRequireLeder));

    [Test]
    public void Create_LinkingToALedersLogin_NonAdminIsRefused_AdminMayAndTheFlagStays()
    {
        var lederLogin = Managed(isLeder: true);
        Assert.That(LederSaveRules.CreateRefusal(NonAdmin, Model(), lederLogin),
            Is.EqualTo(LederMessages.LederLoginChangeRequiresAdmin));
        Assert.That(LederSaveRules.CreateRefusal(LederNonAdmin, Model(), lederLogin),
            Is.EqualTo(LederMessages.LederLoginChangeRequiresAdmin));
        Assert.That(LederSaveRules.CreateRefusal(Admin, Model(), lederLogin), Is.Null);
        Assert.That(LederSaveRules.FlagChange(Admin, Model(), lederLogin), Is.Null, "the flag stays with the login");
    }

    [Test]
    public void Create_AdminGrant_NeedsALoginWithWebOrArchive()
    {
        Assert.That(LederSaveRules.CreateRefusal(Admin, Model(true), null), Is.EqualTo(LederMessages.LederRequiresLogin));
        Assert.That(LederSaveRules.CreateRefusal(Admin, Model(true), Managed(had: false, will: false)),
            Is.EqualTo(LederMessages.LederRequiresLogin));
        Assert.That(LederSaveRules.CreateRefusal(Admin, Model(true), Managed(had: false, will: true)), Is.Null);
    }

    [TestCase(null, false)]
    [TestCase("", false)]
    [TestCase("   ", false)]
    [TestCase("user_12_34@microting.invalid", false)]
    [TestCase("USER_12_34@MICROTING.INVALID", false)]
    [TestCase("invalid@example.com", true)]
    [TestCase("jane.doe@example.com", true)]
    public void IsListedLoginAddress_OnlyThePlaceholderIsUnlisted(string? email, bool expected) =>
        Assert.That(LederSaveRules.IsListedLoginAddress(email), Is.EqualTo(expected));
}
