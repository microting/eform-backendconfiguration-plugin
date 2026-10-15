/*
The MIT License (MIT)
Copyright (c) 2007 - 2026 Microting A/S
Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:
The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.
THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

using BackendConfiguration.Pn.Infrastructure.Models.Chemicals;
using BackendConfiguration.Pn.Services.ChemicalInventoryService;
using BackendConfiguration.Pn.Services.ChemicalReporting;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.EformBackendConfigurationBase.Infrastructure.Helpers;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// W5 digest preview = Plan D Contract C5's four sections for one property,
/// from Plan D's ChemicalAlertRules. Deadlines are placed relative to the
/// Copenhagen date of a real "now" because placements carry real timestamps.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class ChemicalDigestPreviewBuilderTests : ChemicalTestBase
{
    private ChemicalDigestPreviewBuilder CreateSut() =>
        new(BackendConfigurationPnDbContext!, new ChemicalRegisterReader(ChemicalsDbContext!), Names);

    private static DateTime Utc(DateOnly date) => date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    [Test]
    public async Task Build_SortsOpenPlacementsIntoTheFourSections()
    {
        var now = DateTime.UtcNow;
        var today = ChemicalAlertRules.CopenhagenToday(now);
        var property = await CreatePropertyAsync();
        var lade = await CreateLocationAsync(property.Id, "Lade");
        var bothPassed = await ChemicalRegisterSeed.AddChemicalAsync(ChemicalsDbContext!, "Stalddesinfektion", "409-12", status: 7,
            salesDeadline: Utc(today.AddDays(-40)), useAndPossessionDeadline: Utc(today.AddDays(-1)));
        var upcoming = await ChemicalRegisterSeed.AddChemicalAsync(ChemicalsDbContext!, "Karate", "1-212", status: 5,
            useAndPossessionDeadline: Utc(today.AddMonths(2)));
        var deregistered = await ChemicalRegisterSeed.AddChemicalAsync(ChemicalsDbContext!, "Hussar", "18-487", status: 6);
        var clean = await ChemicalRegisterSeed.AddChemicalAsync(ChemicalsDbContext!, "Virkon S", "390-2", status: 5,
            useAndPossessionDeadline: Utc(today.AddYears(3)));
        await CreatePlacementAsync(lade.Id, bothPassed.ChemicalId);
        await CreatePlacementAsync(lade.Id, upcoming.ChemicalId);
        await CreatePlacementAsync(lade.Id, deregistered.ChemicalId);
        await CreatePlacementAsync(lade.Id, clean.ChemicalId);
        await CreatePlacementAsync(lade.Id, deregistered.ChemicalId, removedAt: now.AddDays(-2)); // closed: never listed

        var digest = await CreateSut().BuildAsync(property.Id, now);

        Assert.That(digest.PassedDeadlines.Select(i => i.DeadlineKind),
            Is.EquivalentTo(new ChemicalDeadlineKindEnum?[] { ChemicalDeadlineKindEnum.Sales, ChemicalDeadlineKindEnum.UseAndPossession }));
        Assert.That(digest.PassedDeadlines.All(i => i.ProductName == "Stalddesinfektion" && i.Threshold == ChemicalAlertThresholdEnum.Passed));
        Assert.That(digest.UpcomingDeadlines.Single().ProductName, Is.EqualTo("Karate"));
        Assert.That(digest.UpcomingDeadlines.Single().Threshold, Is.EqualTo(ChemicalAlertThresholdEnum.SixMonths));
        Assert.That(digest.DeregisteredOrExpired.Select(i => i.ProductName), Is.EquivalentTo(new[] { "Stalddesinfektion", "Hussar" }));
        Assert.That(digest.DeregisteredOrExpired.Single(i => i.ProductName == "Hussar").StatusText, Is.EqualTo("Produkt afmeldt"));
        Assert.That(digest.PassedDeadlines.Concat(digest.UpcomingDeadlines).All(i => i.LocationName == "Lade"));
        Assert.That(digest.HasActionItems, Is.True);
        Assert.That(digest.PropertyName, Is.EqualTo(property.Name));
    }

    // E-11: the preview stands for the NEXT digest, so its week is the ISO week of the next
    // Monday 07:00 Europe/Copenhagen slot after utcNow (not the week of today).
    [TestCase("2026-10-28T10:00:00Z", 45)] // Wednesday: next slot is Monday 2026-11-02
    [TestCase("2026-10-26T05:30:00Z", 44)] // Monday 06:30 local: the slot is still ahead, same week
    [TestCase("2026-10-26T06:00:00Z", 45)] // exactly the slot: the slot is not "after" utcNow
    [TestCase("2026-12-30T10:00:00Z", 1)] // Wednesday: next slot is Monday 2027-01-04, ISO week 1
    public async Task Build_IsoWeek_IsTheWeekOfTheNextDigestSlot(string utcNowIso, int expectedWeek)
    {
        var utcNow = DateTime.Parse(utcNowIso, null, System.Globalization.DateTimeStyles.AdjustToUniversal);
        var property = await CreatePropertyAsync();

        var digest = await CreateSut().BuildAsync(property.Id, utcNow);

        Assert.That(digest.IsoWeek, Is.EqualTo(expectedWeek));
    }

    [Test]
    public async Task Build_NewRegistrations_AreRecentAndExcludeMoves()
    {
        var now = DateTime.UtcNow;
        var property = await CreatePropertyAsync();
        var lade = await CreateLocationAsync(property.Id, "Lade");
        var chemical = await ChemicalRegisterSeed.AddChemicalAsync(ChemicalsDbContext!, "Virkon S", "390-2");
        var old = await CreatePlacementAsync(lade.Id, chemical.ChemicalId); // registered 30 days ago
        var since = ChemicalDigestPreviewBuilder.NewRegistrationsSince(now);
        var inWindow = since + (now - since) / 2;
        var change = await CreateInventoryService(new FixedTimeProvider(inWindow)).RegisterPlacementAsync(
            ChemicalCaller.Web(TestUserId),
            new ChemicalRegisterPlacementCommand(lade.Id, chemical.ChemicalId, null, "Reol 2", null));
        await new ChemicalPlacement
        {
            LocationId = lade.Id, ChemicalId = chemical.ChemicalId, PlacementNote = "moved in", MovedFromPlacementId = old.Id,
            RegisteredByUserId = TestUserId, RegisteredAt = inWindow, CreatedByUserId = TestUserId, UpdatedByUserId = TestUserId,
        }.Create(BackendConfigurationPnDbContext!);

        var digest = await CreateSut().BuildAsync(property.Id, now);

        var item = digest.NewRegistrations.Single();
        Assert.That(item.PlacementId, Is.EqualTo(change.Placements.Single().Id));
        Assert.That(item.PlacementNote, Is.EqualTo("Reol 2"));
        Assert.That(item.RegisteredByName, Is.EqualTo($"User {TestUserId}"));
        Assert.That(digest.HasActionItems, Is.False, "new registrations are informational only");
    }

    // Plan D decision 16: "denne uge" starts at the most recent Monday 07:00 Europe/Copenhagen.
    [TestCase("2026-10-28T10:00:00Z", "2026-10-26T06:00:00Z")] // Wednesday after the DST switch (CET)
    [TestCase("2026-10-26T06:00:00Z", "2026-10-26T06:00:00Z")] // exactly the slot
    [TestCase("2026-10-26T05:30:00Z", "2026-10-19T05:00:00Z")] // Monday 06:30 local: previous slot (CEST)
    [TestCase("2026-10-25T21:00:00Z", "2026-10-19T05:00:00Z")] // Sunday evening
    public void NewRegistrationsSince_IsTheMostRecentMondaySlot(string utcNowIso, string expectedIso)
    {
        var utcNow = DateTime.Parse(utcNowIso, null, System.Globalization.DateTimeStyles.AdjustToUniversal);
        var expected = DateTime.Parse(expectedIso, null, System.Globalization.DateTimeStyles.AdjustToUniversal);

        Assert.That(ChemicalDigestPreviewBuilder.NewRegistrationsSince(utcNow), Is.EqualTo(expected));
    }

    [Test]
    public async Task Build_EmptyProperty_HasNoActionItems()
    {
        var property = await CreatePropertyAsync();

        var digest = await CreateSut().BuildAsync(property.Id, DateTime.UtcNow);

        Assert.That(digest.PassedDeadlines, Is.Empty);
        Assert.That(digest.UpcomingDeadlines, Is.Empty);
        Assert.That(digest.DeregisteredOrExpired, Is.Empty);
        Assert.That(digest.NewRegistrations, Is.Empty);
        Assert.That(digest.Recipients, Is.Empty);
        Assert.That(digest.HasActionItems, Is.False);
    }

    [Test]
    public async Task Build_ReadsTheRecipientsFromTheSettings()
    {
        var property = await CreatePropertyAsync();
        await CreateInventoryService().SetSettingsAsync(ChemicalCaller.Web(TestUserId),
            new ChemicalSetSettingsCommand(property.Id, false, ["first@example.com", "second@example.com"]));

        var digest = await CreateSut().BuildAsync(property.Id, DateTime.UtcNow);

        Assert.That(digest.Recipients, Is.EquivalentTo(new[] { "first@example.com", "second@example.com" }));
    }

    [Test]
    public void Build_UnknownProperty_IsNotFound()
    {
        Assert.That(async () => await CreateSut().BuildAsync(int.MaxValue, DateTime.UtcNow),
            Throws.InstanceOf<ChemicalNotFoundException>());
    }

    [Test]
    public async Task Build_ChemicalMissingFromTheRegister_IsSkippedNotFatal()
    {
        var property = await CreatePropertyAsync();
        var lade = await CreateLocationAsync(property.Id, "Lade");
        await CreatePlacementAsync(lade.Id, int.MaxValue - 1);

        var digest = await CreateSut().BuildAsync(property.Id, DateTime.UtcNow);

        Assert.That(digest.HasActionItems, Is.False);
    }
}
