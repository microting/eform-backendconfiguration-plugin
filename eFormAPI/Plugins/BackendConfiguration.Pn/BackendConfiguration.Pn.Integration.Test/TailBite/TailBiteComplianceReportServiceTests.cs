using System;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.ComplianceReport;
using BackendConfiguration.Pn.Services.GrpcServices;
using BackendConfiguration.Pn.Services.TailBite;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using NSubstitute;
using NUnit.Framework;
using GrpcCore = global::Grpc.Core;

namespace BackendConfiguration.Pn.Integration.Test.TailBite;

[TestFixture]
public class TailBiteComplianceReportServiceTests : TailBiteTestBase
{
    private const int CallerSiteId = 42;
    private const int RegisteringSiteId = 7; // the site SeedRegistrationAsync registers as

    // November 2026: CET, UTC+1.
    private static readonly DateTime InPeriod = new(2026, 11, 10, 9, 0, 0, DateTimeKind.Utc);

    private static ComplianceReportRequestModel Request(int? propertyId) => new()
    {
        PropertyId = propertyId, DateFrom = new DateTime(2026, 11, 1), DateTo = new DateTime(2026, 11, 30)
    };

    private TailBiteComplianceReportService Sut(int callerSite = CallerSiteId, int workersWithEmail = 1, bool inactiveAccount = false)
    {
        var resolver = Substitute.For<IGrpcSiteResolver>();
        if (inactiveAccount)
            resolver.GetSdkSiteIdAsync().Returns<Task<int>>(_ =>
                throw new GrpcCore.RpcException(new GrpcCore.Status(GrpcCore.StatusCode.Unauthenticated, "account disabled")));
        else
            resolver.GetSdkSiteIdAsync().Returns(callerSite);
        resolver.GetDisplayNameAsync(Arg.Any<int>()).Returns(ci => ci.Arg<int>() switch
        {
            RegisteringSiteId => "Jonas Skov", 8 => "Mette Holm", 9 => "Jane Doe", _ => ""
        });
        var counter = Substitute.For<ITailBiteWorkerEmailCounter>();
        counter.CountWorkersWithCallerEmailAsync().Returns(workersWithEmail);
        return new TailBiteComplianceReportService(Db, new TailBiteAccess(Db, resolver, counter), new TailBitePhotoQuery(Db), resolver);
    }

    private async Task SeedManagerAsync(bool enabled = true)
    {
        await SeedTreeAsync(enabled);
        await SeedWorkerAsync(CallerSiteId, manager: true);
    }

    private async Task<TailBiteOutbreak> OutbreakAsync(DateTime openedAt, DateTime? closedAt = null, int? locationId = null,
        int? propertyId = null)
    {
        var o = new TailBiteOutbreak
        {
            PropertyId = propertyId ?? PropertyId, LocationId = locationId ?? StableAId, RuleId = RuleId, RuleVersion = 1,
            OpenedAt = openedAt, OpenedByRegistrationId = 1, ClosedAt = closedAt, ClosedBySiteId = closedAt is null ? null : 9
        };
        await o.Create(Db);
        return o;
    }

    private async Task<TailBiteRegistration> LinkedRegistrationAsync(TailBiteOutbreak outbreak, bool cancelled,
        params (int LocationId, int Minor, int Severe)[] rows)
    {
        var (reg, rowIds) = await SeedRegistrationAsync(InPeriod, cancelled, rows);
        foreach (var rowId in rowIds)
            await new TailBiteOutbreakLink { OutbreakId = outbreak.Id, RegistrationLocationId = rowId }.Create(Db);
        return reg;
    }

    private async Task<Guid> PhotoAsync(TailBiteRegistration reg)
    {
        var photo = new TailBiteRegistrationPhoto
        {
            PhotoUuid = Guid.NewGuid(), PropertyId = reg.PropertyId, UploadedBySiteId = reg.SiteId,
            RegistrationClientUuid = reg.ClientUuid, SdkUploadedDataId = 101
        };
        await photo.Create(Db);
        return photo.PhotoUuid;
    }

    [Test]
    public async Task Period_EdgesInCopenhagenTime()
    {
        await SeedManagerAsync();
        await OutbreakAsync(new DateTime(2026, 11, 30, 23, 10, 0));                                     // 1 Dec 00:10 local → out
        await OutbreakAsync(new DateTime(2026, 10, 10, 8, 0, 0), new DateTime(2026, 10, 31, 22, 50, 0)); // closed 31 Oct 23:50 local → out
        var openAcross = await OutbreakAsync(new DateTime(2026, 10, 15, 8, 0, 0));
        var closedFirstDay = await OutbreakAsync(new DateTime(2026, 10, 20, 8, 0, 0), new DateTime(2026, 10, 31, 23, 5, 0));
        var openedLastEvening = await OutbreakAsync(new DateTime(2026, 11, 30, 22, 30, 0));               // 30 Nov 23:30 local → in

        var report = await Sut().BuildAsync(Request(PropertyId));

        Assert.That(report, Is.Not.Null);
        Assert.That(report!.Outbreaks.Select(o => o.Id), Is.EqualTo(new[] { openAcross.Id, closedFirstDay.Id, openedLastEvening.Id }));
        Assert.That(report.Totals.Outbreaks, Is.EqualTo(3));
        Assert.That(report.Totals.ClosedInPeriod, Is.EqualTo(1));
        Assert.That(report.Outbreaks.Select(o => o.OpenedAt.Kind), Is.All.EqualTo(DateTimeKind.Utc));
        Assert.That(report.Outbreaks[1].ClosedAt, Is.EqualTo(new DateTime(2026, 10, 31, 23, 5, 0, DateTimeKind.Utc)));
        Assert.That(report.Outbreaks[1].ClosedAt!.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
    }

    [Test]
    public async Task Access_Manager_GetsReport()
    {
        await SeedManagerAsync();
        var outbreak = await OutbreakAsync(InPeriod);

        var report = await Sut().BuildAsync(Request(PropertyId));

        Assert.That(report!.Outbreaks.Select(o => (o.Id, o.PropertyId, o.PropertyName)),
            Is.EqualTo(new[] { (outbreak.Id, PropertyId, "Ejendom Test") }));
    }

    [Test]
    public async Task Access_WorkerWithoutManagerFlag_Null()
    {
        await SeedTreeAsync();
        await SeedWorkerAsync(CallerSiteId, manager: false);
        await OutbreakAsync(InPeriod);

        Assert.That(await Sut().BuildAsync(Request(PropertyId)), Is.Null);
    }

    [Test]
    public async Task Access_NoIdentity_Null()
    {
        await SeedManagerAsync();
        Assert.That(await Sut(callerSite: 0).BuildAsync(Request(PropertyId)), Is.Null);
    }

    [Test]
    public async Task Access_InactiveAccount_Null()
    {
        await SeedManagerAsync();
        Assert.That(await Sut(inactiveAccount: true).BuildAsync(Request(PropertyId)), Is.Null);
    }

    [Test]
    public async Task Access_AmbiguousEmail_Null()
    {
        await SeedManagerAsync();
        Assert.That(await Sut(workersWithEmail: 2).BuildAsync(Request(PropertyId)), Is.Null);
    }

    [Test]
    public async Task Access_SwitchedOffProperty_Null()
    {
        await SeedManagerAsync(enabled: false);
        await OutbreakAsync(InPeriod);

        Assert.That(await Sut().BuildAsync(Request(PropertyId)), Is.Null);
    }

    [Test]
    public async Task Access_AllProperties_OnlyThoseTheCallerManages()
    {
        await SeedManagerAsync();
        var managed = await OutbreakAsync(InPeriod);
        // A second enabled property where the caller is a worker without the manager flag.
        var other = new Property { Name = "Eksempelgården I/S" };
        await other.Create(Db);
        await new TailBiteProperty { PropertyId = other.Id, Enabled = true, EnabledAt = InPeriod }.Create(Db);
        var otherRoot = new TailBiteLocation { PropertyId = other.Id, Name = "Ejendom", QrCode = TailBiteDefaults.NewQrCode() };
        await otherRoot.Create(Db);
        var otherStable = new TailBiteLocation { PropertyId = other.Id, ParentId = otherRoot.Id, Name = "Stald C", QrCode = TailBiteDefaults.NewQrCode() };
        await otherStable.Create(Db);
        await new PropertyWorker { PropertyId = other.Id, WorkerId = CallerSiteId, TailBiteManager = false }.Create(Db);
        await OutbreakAsync(InPeriod, locationId: otherStable.Id, propertyId: other.Id);

        var report = await Sut().BuildAsync(Request(null));

        Assert.That(report!.Outbreaks.Select(o => o.Id), Is.EqualTo(new[] { managed.Id }));
    }

    [Test]
    public async Task ManagerWithoutOutbreaksInPeriod_EmptyNotNull()
    {
        await SeedManagerAsync();
        await OutbreakAsync(new DateTime(2026, 12, 5, 8, 0, 0));

        var report = await Sut().BuildAsync(Request(PropertyId));

        Assert.That(report, Is.Not.Null);
        Assert.That(report!.Outbreaks, Is.Empty);
        Assert.That(report.Totals, Is.EqualTo(new TailBiteReportTotals(0, 0, 0, 0, 0, 0)));
    }

    [Test]
    public async Task WithdrawnActionsAndCancelledRegistrations_Excluded()
    {
        await SeedManagerAsync();
        var outbreak = await OutbreakAsync(InPeriod);
        var live = await LinkedRegistrationAsync(outbreak, false, (Pen309Id, 2, 1));
        var cancelled = await LinkedRegistrationAsync(outbreak, true, (Pen310Id, 5, 5));
        var shown = await PhotoAsync(live);
        await PhotoAsync(cancelled);
        var assessment = new TailBiteRiskAssessment { OutbreakId = outbreak.Id, Climate = true, AssessedBySiteId = 9, AssessedAt = InPeriod.AddDays(1) };
        await assessment.Create(Db);
        var followUp = InPeriod.AddDays(5);
        await new TailBiteAssessmentAction { AssessmentId = assessment.Id, Factor = TailBiteFactor.Climate, Description = "Tjek ventil 4",
            ResponsibleSiteId = 8, FollowUpDate = followUp, DoneAt = followUp, DoneBySiteId = 9 }.Create(Db);
        await new TailBiteAssessmentAction { AssessmentId = assessment.Id, Factor = TailBiteFactor.Feed, Description = "Skift foder",
            ResponsibleSiteId = 8, FollowUpDate = followUp.AddDays(1) }.Create(Db);
        await new TailBiteAssessmentAction { AssessmentId = assessment.Id, Factor = TailBiteFactor.Water, Description = "Trukket tilbage",
            ResponsibleSiteId = 8, FollowUpDate = followUp, WithdrawnAt = followUp, WithdrawnReason = "Fejl" }.Create(Db);

        var report = await Sut().BuildAsync(Request(PropertyId));

        var o = report!.Outbreaks.Single();
        Assert.That(o.Actions, Is.EqualTo(new[]
        {
            new TailBiteReportAction(TailBiteFactor.Climate, "Tjek ventil 4", "Mette Holm", followUp, followUp, "Jane Doe"),
            new TailBiteReportAction(TailBiteFactor.Feed, "Skift foder", "Mette Holm", followUp.AddDays(1), null, null)
        }));
        Assert.That(o.Answers, Is.EqualTo(new FactorAnswers(false, false, false, true, false, false)));
        Assert.That(o.AssessedAt, Is.EqualTo(InPeriod.AddDays(1)));
        Assert.That(o.Registrations.Select(r => r.Id), Is.EqualTo(new[] { live.Id }));
        Assert.That(o.Registrations.Single().Photos.Select(p => p.PhotoUuid), Is.EqualTo(new[] { shown }));
        Assert.That((o.BittenPigs, o.SeverePigs), Is.EqualTo((3, 1)));
        Assert.That(report.Totals, Is.EqualTo(new TailBiteReportTotals(1, 0, 3, 1, 1, 2)));
    }

    [Test]
    public async Task TwoPenRegistration_IsOneEntry()
    {
        await SeedManagerAsync();
        var outbreak = await OutbreakAsync(InPeriod, locationId: SectionId);
        var reg = await LinkedRegistrationAsync(outbreak, false, (Pen309Id, 2, 0), (Pen310Id, 1, 1));
        var type = new TailBiteActionType { PropertyId = PropertyId, Code = "rope", Name = "Reb i stien", SortOrder = 1 };
        await type.Create(Db);
        await new TailBiteRegistrationAction { RegistrationId = reg.Id, ActionTypeId = type.Id }.Create(Db);

        var report = await Sut().BuildAsync(Request(PropertyId));

        var o = report!.Outbreaks.Single();
        Assert.That(o.Answers, Is.Null);
        Assert.That(o.AssessedAt, Is.Null);
        Assert.That(o.Registrations, Has.Count.EqualTo(1));
        var r = o.Registrations.Single();
        Assert.That((r.Id, r.Pens, r.Minor, r.Severe, r.ActionTypes, r.RegisteredByName),
            Is.EqualTo((reg.Id, "Sti 309, Sti 310", 3, 1, "Reb i stien", "Jonas Skov")));
        Assert.That(r.EffectiveAt.Kind, Is.EqualTo(DateTimeKind.Utc));
        Assert.That((o.BittenPigs, o.SeverePigs), Is.EqualTo((4, 1)));
    }

    [Test]
    public async Task LocationPath_AncestorsBelowRootPlusItself()
    {
        await SeedManagerAsync();
        await OutbreakAsync(InPeriod, locationId: SectionId);

        var report = await Sut().BuildAsync(Request(PropertyId));

        Assert.That(report!.Outbreaks.Single().LocationPath, Is.EqualTo("Stald A › Sektion 4"));
    }
}
