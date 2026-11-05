#nullable enable
using BackendConfiguration.Pn.Infrastructure.Models;
using BackendConfiguration.Pn.Services.Leder;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>The Leder refusals and the flag decision of a Medarbejdere save, before any write. No database.</summary>
[TestFixture]
public class LederSaveRulesTests
{
    private const int Actor = 7;
    private static readonly LederCaller Admin = new(true, true, Actor);
    private static readonly LederCaller NonAdmin = new(false, false, Actor);
    private static readonly LederCaller LederNonAdmin = new(false, true, Actor);

    private static DeviceUserModel Model(bool? isLeader = null, List<int>? tags = null) =>
        new() { IsLeader = isLeader, Tags = tags ?? [] };

    private static string? Update(LederCaller? caller, DeviceUserModel model, bool siteIsLeder = false,
        bool changed = false, IReadOnlyCollection<int>? stored = null) =>
        LederSaveRules.UpdateRefusal(caller, model, stored ?? [], siteIsLeder, changed);

    [Test]
    public void NoCaller_NoRuleApplies_AndNothingIsWritten()
    {
        Assert.That(Update(null, Model(true, [5]), siteIsLeder: true, changed: true), Is.Null);
        Assert.That(LederSaveRules.CreateRefusal(null, Model(true, [5])), Is.Null);
        Assert.That(LederSaveRules.FlagChange(null, Model(true)), Is.Null);
    }

    [TestCase(true)]
    [TestCase(false)]
    [TestCase(null)]
    public void Admin_FlagChange_IsTheRequestedValue_WithNoAccessCondition(bool? isLeader)
    {
        // D-L16: no login, no Web/Arkiv and an admin login are all fine; nothing is refused for them.
        Assert.That(Update(Admin, Model(isLeader)), Is.Null);
        Assert.That(LederSaveRules.CreateRefusal(Admin, Model(isLeader)), Is.Null);
        Assert.That(LederSaveRules.FlagChange(Admin, Model(isLeader)), Is.EqualTo(isLeader));
    }

    [TestCase(true)]
    [TestCase(false)]
    [TestCase(null)]
    public void NonAdmin_IsLeaderIsIgnored_NeitherRefusedNorWritten(bool? isLeader)
    {
        Assert.That(Update(NonAdmin, Model(isLeader)), Is.Null);
        Assert.That(LederSaveRules.CreateRefusal(NonAdmin, Model(isLeader)), Is.Null);
        Assert.That(LederSaveRules.FlagChange(NonAdmin, Model(isLeader)), Is.Null);
        Assert.That(LederSaveRules.FlagChange(LederNonAdmin, Model(isLeader)), Is.Null);
    }

    [Test]
    public void H2_NonAdmin_AddressOrUserNameChangeOfALeder_IsRefused()
    {
        Assert.That(Update(NonAdmin, Model(), siteIsLeder: true, changed: true),
            Is.EqualTo(LederMessages.LederLoginChangeRequiresAdmin));
        Assert.That(Update(LederNonAdmin, Model(), siteIsLeder: true, changed: true),
            Is.EqualTo(LederMessages.LederLoginChangeRequiresAdmin));
    }

    [Test]
    public void H2_OnlyLedersAndOnlyAddressChanges_AndNeverForAnAdmin()
    {
        Assert.That(Update(NonAdmin, Model(), siteIsLeder: false, changed: true), Is.Null, "not a Leder");
        Assert.That(Update(NonAdmin, Model(), siteIsLeder: true, changed: false), Is.Null, "no address change");
        Assert.That(Update(Admin, Model(true), siteIsLeder: true, changed: true), Is.Null, "an admin may");
    }

    [Test]
    public void Tags_NonLeder_ChangedSet_IsRefused_SameSetPasses()
    {
        Assert.That(Update(NonAdmin, Model(null, [1, 2]), stored: [2, 1]), Is.Null);
        Assert.That(Update(NonAdmin, Model(null, [1]), stored: [1, 2]), Is.EqualTo(LederMessages.WorkerTagsRequireLeder));
    }

    [Test]
    public void Tags_LederOrAdmin_MayChange()
    {
        Assert.That(Update(LederNonAdmin, Model(null, [3]), stored: [1]), Is.Null);
        Assert.That(Update(Admin, Model(null, [3]), stored: [1]), Is.Null);
    }

    [Test]
    public void Tags_RefusalComesBeforeH2()
    {
        Assert.That(Update(NonAdmin, Model(null, [3]), siteIsLeder: true, changed: true, stored: [1]),
            Is.EqualTo(LederMessages.WorkerTagsRequireLeder));
    }

    [Test]
    public void Create_NonLederWithTags_IsRefused_EverythingElsePasses()
    {
        Assert.That(LederSaveRules.CreateRefusal(NonAdmin, Model(null, [1])), Is.EqualTo(LederMessages.WorkerTagsRequireLeder));
        Assert.That(LederSaveRules.CreateRefusal(NonAdmin, Model(true)), Is.Null);
        Assert.That(LederSaveRules.CreateRefusal(LederNonAdmin, Model(null, [1])), Is.Null);
        Assert.That(LederSaveRules.CreateRefusal(Admin, Model(true, [1])), Is.Null);
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
