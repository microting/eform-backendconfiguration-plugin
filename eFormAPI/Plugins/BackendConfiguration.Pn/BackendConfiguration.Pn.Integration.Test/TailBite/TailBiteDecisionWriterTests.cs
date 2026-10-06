using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.TailBite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Integration.Test.TailBite;

[TestFixture]
public class TailBiteDecisionWriterTests : TailBiteTestBase
{
    [Test]
    public async Task ApplyAsync_OpenThenJoin_LinksRowsOnce()
    {
        await SeedTreeAsync();
        var db = Db;
        var t = Clock.GetUtcNow().UtcDateTime;
        var (reg1, rows1) = await SeedRegistrationAsync(t, false, (Pen309Id, 3, 0), (Pen310Id, 2, 0));
        var writer = new TailBiteDecisionWriter(db);

        var opened = await writer.ApplyAsync(PropertyId, reg1.Id, [new OpenNewOutbreak(StableAId, RuleId, 1, t, rows1)]);
        var (reg2, rows2) = await SeedRegistrationAsync(t.AddHours(1), false, (Pen309Id, 1, 0));
        // the join repeats the already-linked rows: they must not be linked twice
        var joined = await writer.ApplyAsync(PropertyId, reg2.Id, [new JoinOutbreak(opened[0].OutbreakId, rows1.Concat(rows2).ToList())]);

        Assert.That(opened.Single().Opened, Is.True);
        Assert.That(joined.Single(), Is.EqualTo((opened[0].OutbreakId, false)));
        var links = await db.TailBiteOutbreakLinks.Where(k => k.OutbreakId == opened[0].OutbreakId).Select(k => k.RegistrationLocationId).ToListAsync();
        Assert.That(links, Is.EquivalentTo(rows1.Concat(rows2)));
        Assert.That(db.TailBiteOutbreaks.Single().OpenedByRegistrationId, Is.EqualTo(reg1.Id));
    }
}
