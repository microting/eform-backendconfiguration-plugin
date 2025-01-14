namespace BackendConfiguration.Pn.Integration.Test;

[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class CanaryInAColeMine
{
    [Test]
    public void CanPeep()
    {
        Assert.That(true);
    }
}