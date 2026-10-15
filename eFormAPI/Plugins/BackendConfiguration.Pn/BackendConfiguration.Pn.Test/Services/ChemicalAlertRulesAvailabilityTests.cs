using System;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.EformBackendConfigurationBase.Infrastructure.Helpers;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Test.Services;

/// <summary>
/// The plugin's W1/W5 code uses Plan D's rule from the base package. Pins the
/// package version (the type exists) and the two behaviours the web relies on.
/// The full vector set is tested in the base repo (ChemicalAlertRulesUTest).
/// </summary>
[TestFixture]
public class ChemicalAlertRulesAvailabilityTests
{
    [Test]
    public void TheRuleIsAvailableWithPlanDsSemantics()
    {
        var today = new DateOnly(2026, 9, 29);

        Assert.That(ChemicalAlertRules.RequiresAction(6, null, null, today), Is.True);
        Assert.That(ChemicalAlertRules.ReachedThreshold(new DateTime(2027, 3, 31), new DateOnly(2027, 4, 1)),
            Is.EqualTo(ChemicalAlertThresholdEnum.Passed));
    }
}
