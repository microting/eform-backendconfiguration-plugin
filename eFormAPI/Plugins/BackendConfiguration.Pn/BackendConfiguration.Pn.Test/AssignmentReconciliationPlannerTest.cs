using System.Collections.Generic;
using BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Test;

[TestFixture]
public class AssignmentReconciliationPlannerTest
{
    private static HashSet<int> S(params int[] xs) => new(xs);

    [Test]
    public void Add_sites_in_desired_but_not_present()
    {
        var plan = AssignmentReconciliationPlanner.Plan(
            desired: S(1, 2, 3), actualNonCompleted: S(1), completed: S());
        Assert.That(plan.ToAdd, Is.EquivalentTo(new[] { 2, 3 }));
        Assert.That(plan.ToRemove, Is.Empty);
    }

    [Test]
    public void Remove_sites_present_but_not_desired()
    {
        var plan = AssignmentReconciliationPlanner.Plan(
            desired: S(1), actualNonCompleted: S(1, 2, 3), completed: S());
        Assert.That(plan.ToRemove, Is.EquivalentTo(new[] { 2, 3 }));
        Assert.That(plan.ToAdd, Is.Empty);
    }

    [Test]
    public void Never_recreate_completed_sites()
    {
        var plan = AssignmentReconciliationPlanner.Plan(
            desired: S(1, 2), actualNonCompleted: S(1), completed: S(2));
        Assert.That(plan.ToAdd, Is.Empty);
        Assert.That(plan.ToRemove, Is.Empty);
    }

    [Test]
    public void Never_remove_completed_sites_even_if_undesired()
    {
        var plan = AssignmentReconciliationPlanner.Plan(
            desired: S(1), actualNonCompleted: S(1), completed: S(9));
        Assert.That(plan.ToRemove, Is.Empty);
        Assert.That(plan.ToAdd, Is.Empty);
    }

    [Test]
    public void Noop_when_actual_equals_desired()
    {
        var plan = AssignmentReconciliationPlanner.Plan(
            desired: S(1, 2), actualNonCompleted: S(1, 2), completed: S());
        Assert.That(plan.ToAdd, Is.Empty);
        Assert.That(plan.ToRemove, Is.Empty);
    }

    [Test]
    public void Add_and_remove_simultaneously()
    {
        var plan = AssignmentReconciliationPlanner.Plan(
            desired: S(1, 4), actualNonCompleted: S(1, 2), completed: S(3));
        Assert.That(plan.ToAdd, Is.EquivalentTo(new[] { 4 }));
        Assert.That(plan.ToRemove, Is.EquivalentTo(new[] { 2 }));
    }
}
