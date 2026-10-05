#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BackendConfiguration.Pn.Services.TailBite;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Test.TailBite;

[TestFixture]
public class OutbreakEvaluatorTests
{
    // Tree: 1 root (depth 0) > 2 Stald A (1) > 3 Sektion 4 (2) > 4 Sti 309, 5 Sti 310 (3)
    //                        > 6 Stald B (1) > 7 Sti 501 (2)
    private static readonly DateTime D0 = new(2026, 10, 1, 6, 0, 0, DateTimeKind.Utc);
    private static readonly Dictionary<int, EvalLocation> Tree = new()
    {
        [1] = new(1, null), [2] = new(2, 1), [3] = new(3, 2), [4] = new(4, 3), [5] = new(5, 3),
        [6] = new(6, 1), [7] = new(7, 6)
    };
    private static EvalRule RootRule(int minBitten = 5, int? minSevere = null, int window = 7, int depth = 1)
        => new(100, 1, 1, minBitten, minSevere, window, depth);
    private static EvalRow Row(int id, int loc, double dayOffset, int minor, int severe = 0, bool cancelled = false, bool linkedToClosed = false)
        => new(id, loc, D0.AddDays(dayOffset), minor, severe, cancelled, linkedToClosed);
    private static EvaluationSnapshot Snap(IEnumerable<EvalRule> rules, IEnumerable<EvalRow> rows, IEnumerable<EvalOpenOutbreak>? open = null)
        => new(Tree, rules.ToList(), rows.ToList(), (open ?? []).ToList());

    [Test]
    public void BelowThreshold_NoDecision()
    {
        var s = Snap([RootRule()], [Row(1, 4, 0, 2), Row(2, 5, 1, 2)]);
        Assert.That(OutbreakEvaluator.Evaluate(s, D0.AddDays(1), [2]), Is.Empty);
    }

    [Test]
    public void ReachesThreshold_OpensOnStableNode_LinksWindowRows()
    {
        var s = Snap([RootRule()], [Row(1, 4, 0, 2), Row(2, 5, 1, 2), Row(3, 4, 2, 1)]);
        var d = OutbreakEvaluator.Evaluate(s, D0.AddDays(2), [3]).Single() as OpenNewOutbreak;
        Assert.That(d, Is.Not.Null);
        Assert.That(d!.SummingLocationId, Is.EqualTo(2));
        Assert.That(d.OpenedAt, Is.EqualTo(D0.AddDays(2)));
        Assert.That(d.RowIds, Is.EquivalentTo(new[] { 1, 2, 3 }));
        Assert.That((d.RuleId, d.RuleVersion), Is.EqualTo((100, 1)));
    }

    [Test]
    public void MinSevere_FiresOnOneSevere()
    {
        var s = Snap([RootRule(minBitten: 99, minSevere: 1)], [Row(1, 4, 0, 0, 1)]);
        Assert.That(OutbreakEvaluator.Evaluate(s, D0, [1]).Single(), Is.TypeOf<OpenNewOutbreak>());
    }

    [Test]
    public void WindowEdge_RowExactlyWindowOld_IsExcluded()
    {
        // window (T-7d, T]: a row at T-7d exactly is outside
        var s = Snap([RootRule()], [Row(1, 4, 0, 4), Row(2, 4, 7, 1)]);
        Assert.That(OutbreakEvaluator.Evaluate(s, D0.AddDays(7), [2]), Is.Empty);
    }

    [Test]
    public void NearestRule_DeeperRuleWins_AndSumsAtItsDepth()
    {
        var sectionRule = new EvalRule(200, 3, 3, 3, null, 7, 3); // on Sektion 4, per pen
        // Root rule alone would fire (2+2=4 per stable, threshold 3); the deeper per-pen rule governs these pens and does not.
        var s = Snap([RootRule(minBitten: 3), sectionRule], [Row(1, 4, 0, 2), Row(2, 5, 0, 2)]);
        Assert.That(OutbreakEvaluator.Evaluate(s, D0, [2]), Is.Empty);
    }

    [Test]
    public void LocationAboveCountDepth_SumsAtItself()
    {
        var penRule = RootRule(minBitten: 2, depth: 3);
        var s = Snap([penRule], [Row(1, 2, 0, 2)]); // row on Stald A (depth 1) with CountDepth 3
        var d = (OpenNewOutbreak)OutbreakEvaluator.Evaluate(s, D0, [1]).Single();
        Assert.That(d.SummingLocationId, Is.EqualTo(2));
    }

    [Test]
    public void OpenOutbreakOnNode_Joins_EvenWhenLate()
    {
        var s = Snap([RootRule()], [Row(9, 4, -20, 1)], [new EvalOpenOutbreak(55, 2)]);
        var d = (JoinOutbreak)OutbreakEvaluator.Evaluate(s, D0.AddDays(-20), [9]).Single();
        Assert.That(d.OutbreakId, Is.EqualTo(55));
        Assert.That(d.RowIds, Is.EqualTo(new[] { 9 }));
    }

    [Test]
    public void RowsLinkedToClosed_NeverRetrigger()
    {
        var s = Snap([RootRule()], [Row(1, 4, 0, 5, linkedToClosed: true), Row(2, 4, 1, 1)]);
        Assert.That(OutbreakEvaluator.Evaluate(s, D0.AddDays(1), [2]), Is.Empty);
    }

    [Test]
    public void CancelledRows_DoNotCount()
    {
        var s = Snap([RootRule()], [Row(1, 4, 0, 4, cancelled: true), Row(2, 4, 1, 1)]);
        Assert.That(OutbreakEvaluator.Evaluate(s, D0.AddDays(1), [2]), Is.Empty);
    }

    [Test]
    public void OutOfOrderArrival_ForwardWindowOpensAtLaterT()
    {
        // B (day 6, 3 pigs) arrived first, then A (day 5, 2 pigs) arrives late.
        var s = Snap([RootRule()], [Row(1, 4, 6, 3), Row(2, 4, 5, 2)]);
        var d = (OpenNewOutbreak)OutbreakEvaluator.Evaluate(s, D0.AddDays(5), [2]).Single();
        Assert.That(d.OpenedAt, Is.EqualTo(D0.AddDays(6)));
        Assert.That(d.RowIds, Is.EquivalentTo(new[] { 1, 2 }));
    }

    [Test]
    public void LinksEveryEligibleRowUpToTPlusWindow()
    {
        var s = Snap([RootRule()], [Row(1, 4, 0, 5), Row(2, 4, 3, 1), Row(3, 4, 8, 1)]); // rows 2, 3 already stored, later
        var d = (OpenNewOutbreak)OutbreakEvaluator.Evaluate(s, D0, [1]).Single();
        Assert.That(d.RowIds, Is.EquivalentTo(new[] { 1, 2 })); // day 8 is beyond t+7d
    }

    [Test]
    public void TwoNodes_OneRegistration_OpensBothIndependently()
    {
        var s = Snap([RootRule(minBitten: 2)], [Row(1, 4, 0, 2), Row(2, 7, 0, 2)]);
        var ds = OutbreakEvaluator.Evaluate(s, D0, [1, 2]).Cast<OpenNewOutbreak>().ToList();
        Assert.That(ds.Select(x => x.SummingLocationId), Is.EquivalentTo(new[] { 2, 6 }));
    }

    [Test]
    public void TwoNodes_OneJoinedOneBelow_ProducesOnlyJoin()
    {
        var s = Snap([RootRule(minBitten: 5)], [Row(1, 4, 0, 1), Row(2, 7, 0, 1)], [new EvalOpenOutbreak(55, 2)]);
        var d = (JoinOutbreak)OutbreakEvaluator.Evaluate(s, D0, [1, 2]).Single();
        Assert.That(d.OutbreakId, Is.EqualTo(55));
        Assert.That(d.RowIds, Is.EqualTo(new[] { 1 }));
    }

    [Test]
    public void LateRow_InsideClosedSpan_DoesNotReopen_ButCanStartNew()
    {
        // rows 1..3 were consumed by a closed outbreak; a late row 4 lands inside that span
        var s = Snap([RootRule(minBitten: 2)],
            [Row(1, 4, 0, 2, linkedToClosed: true), Row(4, 5, 1, 1), Row(5, 5, 2, 1)]);
        var d = (OpenNewOutbreak)OutbreakEvaluator.Evaluate(s, D0.AddDays(1), [4]).Single();
        Assert.That(d.RowIds, Is.EquivalentTo(new[] { 4, 5 })); // closed rows never re-linked
    }

    [Test]
    public void NoRuleAnywhere_NoDecision()
    {
        var s = Snap([], [Row(1, 4, 0, 50)]);
        Assert.That(OutbreakEvaluator.Evaluate(s, D0, [1]), Is.Empty);
    }

    [Test]
    public void RowGovernedByDeeperRule_NotCountedAtShallowerNode()
    {
        var sectionRule = new EvalRule(200, 3, 3, 99, null, 7, 3);
        var s = Snap([RootRule(minBitten: 3), sectionRule], [Row(1, 4, 0, 2), Row(2, 2, 0, 2)]);
        Assert.That(OutbreakEvaluator.Evaluate(s, D0, [2]), Is.Empty);
    }

    [Test]
    public void RowAboveCountDepth_AndPenRows_NotLinkedTwice()
    {
        var s = Snap([RootRule(minBitten: 2, depth: 3)], [Row(1, 2, 0, 2), Row(2, 4, 0, 2)]);
        var ds = OutbreakEvaluator.Evaluate(s, D0, [1, 2]).Cast<OpenNewOutbreak>().ToList();
        Assert.That(ds, Has.Count.EqualTo(2));
        Assert.That(ds.Single(x => x.SummingLocationId == 2).RowIds, Is.EqualTo(new[] { 1 }));
        Assert.That(ds.Single(x => x.SummingLocationId == 4).RowIds, Is.EqualTo(new[] { 2 }));
    }
}
