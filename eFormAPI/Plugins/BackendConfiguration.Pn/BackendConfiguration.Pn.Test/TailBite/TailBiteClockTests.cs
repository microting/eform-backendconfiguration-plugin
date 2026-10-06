#nullable enable
using System;
using BackendConfiguration.Pn.Services.TailBite;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Test.TailBite;

[TestFixture]
public class TailBiteClockTests
{
    private static readonly DateTime Rx = new(2026, 10, 1, 7, 0, 0, DateTimeKind.Utc);

    [Test] public void FutureBeyondTolerance_ClampsToReceived()
        => Assert.That(TailBiteClock.EffectiveAt(Rx.AddMinutes(11), Rx), Is.EqualTo(Rx));
    [Test] public void FutureWithinTolerance_Kept()
        => Assert.That(TailBiteClock.EffectiveAt(Rx.AddMinutes(9), Rx), Is.EqualTo(Rx.AddMinutes(9)));
    [Test] public void Past_Kept_AndLateAfter7Days()
    {
        var reg = Rx.AddDays(-8);
        Assert.That(TailBiteClock.EffectiveAt(reg, Rx), Is.EqualTo(reg));
        Assert.That(TailBiteClock.IsLate(reg, Rx), Is.True);
        Assert.That(TailBiteClock.IsLate(Rx.AddDays(-6), Rx), Is.False);
    }

    [Test] public void FutureExactlyAtTolerance_NotClamped()
    {
        Assert.That(TailBiteClock.IsClamped(Rx.AddMinutes(10), Rx), Is.False);
        Assert.That(TailBiteClock.EffectiveAt(Rx.AddMinutes(10), Rx), Is.EqualTo(Rx.AddMinutes(10)));
    }
    [Test] public void IsClamped_OnlyBeyondTolerance()
    {
        Assert.That(TailBiteClock.IsClamped(Rx.AddMinutes(11), Rx), Is.True);
        Assert.That(TailBiteClock.IsClamped(Rx.AddMinutes(-5), Rx), Is.False);
    }
    [Test] public void ExactlySevenDaysOld_NotLate()
        => Assert.That(TailBiteClock.IsLate(Rx.AddDays(-7), Rx), Is.False);
}
