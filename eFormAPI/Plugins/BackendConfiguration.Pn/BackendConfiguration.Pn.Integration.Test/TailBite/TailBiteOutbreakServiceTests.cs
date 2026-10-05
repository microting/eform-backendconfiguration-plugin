using System;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.TailBite;
using Microsoft.EntityFrameworkCore;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Integration.Test.TailBite;

[TestFixture]
public class TailBiteOutbreakServiceTests : TailBiteTestBase
{
    private static readonly FactorAnswers AllNo = new(false, false, false, false, false, false);

    private TailBiteOutbreakService Sut()
    {
        var db = BackendConfigurationPnDbContext!;
        return new TailBiteOutbreakService(db, new TailBitePropertyLock(db), NewAccess(), Clock);
    }

    private DateTime Now => Clock.GetUtcNow().UtcDateTime;

    private async Task<int> OpenOutbreakAsync(int? locationId = null)
    {
        var o = new TailBiteOutbreak { PropertyId = PropertyId, LocationId = locationId ?? StableAId, RuleId = RuleId,
            RuleVersion = 1, OpenedAt = Now, OpenedByRegistrationId = 1 };
        await o.Create(BackendConfigurationPnDbContext!);
        return o.Id;
    }

    // An outbreak with an assessment (Climate = yes) and one action assigned to site 8.
    private async Task<(int OutbreakId, int ActionId)> AssessedWithActionAsync(TailBiteOutbreakService sut, int? outbreakId = null)
    {
        var id = outbreakId ?? await OpenOutbreakAsync();
        await sut.SaveAssessmentAsync(7, id, AllNo with { Climate = true },
            [new ActionInput(TailBiteFactor.Climate, "Tjek ventil 4", 8, Now.AddDays(3))]);
        BackendConfigurationPnDbContext!.ChangeTracker.Clear();
        return (id, (await BackendConfigurationPnDbContext.TailBiteAssessmentActions.SingleAsync()).Id);
    }

    private async Task<int> SetUpManagerWithOutbreakAsync()
    {
        await SeedTreeAsync(); await SeedWorkerAsync(7, manager: true);
        return await OpenOutbreakAsync();
    }

    private async Task<TailBiteRegistration> NewRegistrationAsync(int propertyId)
    {
        var reg = new TailBiteRegistration { PropertyId = propertyId, SiteId = 7, ClientUuid = Guid.NewGuid(), RegisteredAt = Now, ReceivedAt = Now, EffectiveAt = Now };
        await reg.Create(BackendConfigurationPnDbContext!);
        return reg;
    }

    private async Task<TailBiteOutbreak> NewForeignOutbreakAsync()
    {
        var foreign = new TailBiteOutbreak { PropertyId = PropertyId + 1000, LocationId = 999, RuleId = 1, RuleVersion = 1, OpenedAt = Now, OpenedByRegistrationId = 1 };
        await foreign.Create(BackendConfigurationPnDbContext!);
        return foreign;
    }

    private async Task CloseDirectlyAsync(int outbreakId)
    {
        var o = await BackendConfigurationPnDbContext!.TailBiteOutbreaks.SingleAsync(x => x.Id == outbreakId);
        o.ClosedAt = Now;
        await o.Update(BackendConfigurationPnDbContext);
        BackendConfigurationPnDbContext.ChangeTracker.Clear();
    }

    [Test]
    public async Task Close_WithoutAssessment_Refused()
    {
        var id = await SetUpManagerWithOutbreakAsync();
        await Assert.ThrowsAsync<TailBiteConflictException>(() => Sut().CloseAsync(7, id));
    }

    [Test]
    public async Task Assessment_JaWithoutAction_Rejected()
    {
        var id = await SetUpManagerWithOutbreakAsync();
        await Assert.ThrowsAsync<TailBiteValidationException>(() => Sut().SaveAssessmentAsync(7, id, AllNo with { Climate = true }, []));
    }

    [Test]
    public async Task Assessment_ResponsibleNotOnProperty_Rejected()
    {
        var id = await SetUpManagerWithOutbreakAsync();
        await Assert.ThrowsAsync<TailBiteValidationException>(() => Sut().SaveAssessmentAsync(7, id, AllNo with { Climate = true },
            [new ActionInput(TailBiteFactor.Climate, "Tjek ventil", 999, Now.AddDays(3))]));
    }

    [Test]
    public async Task Assessment_ActionOnFactorAnsweredNo_Rejected()
    {
        var id = await SetUpManagerWithOutbreakAsync();
        await Assert.ThrowsAsync<TailBiteValidationException>(() => Sut().SaveAssessmentAsync(7, id, AllNo,
            [new ActionInput(TailBiteFactor.Water, "Tjek vand", 7, Now.AddDays(3))]));
    }

    [Test]
    public async Task Close_WithOpenAction_Refused_AfterDoneOrWithdrawn_Allowed()
    {
        await SeedTreeAsync(); await SeedWorkerAsync(7, manager: true); await SeedWorkerAsync(8); var id = await OpenOutbreakAsync();
        var sut = Sut();
        await sut.SaveAssessmentAsync(7, id, AllNo with { Climate = true, Feed = true }, [
            new ActionInput(TailBiteFactor.Climate, "Tjek ventil 4", 8, Now.AddDays(3)),
            new ActionInput(TailBiteFactor.Feed, "Tjek foderautomat", 7, Now.AddDays(5))]);
        await Assert.ThrowsAsync<TailBiteConflictException>(() => sut.CloseAsync(7, id));
        var actions = await BackendConfigurationPnDbContext!.TailBiteAssessmentActions.OrderBy(a => a.Id).ToListAsync();
        await sut.SetActionDoneAsync(7, actions[0].Id, true);
        await sut.WithdrawActionAsync(7, actions[1].Id, "Ikke længere relevant");
        await sut.CloseAsync(7, id);
        BackendConfigurationPnDbContext.ChangeTracker.Clear();
        var closed = BackendConfigurationPnDbContext.TailBiteOutbreaks.Single(o => o.Id == id);
        Assert.That(closed.ClosedAt, Is.Not.Null);
        Assert.That(closed.ClosedBySiteId, Is.EqualTo(7));
    }

    [Test]
    public async Task RevisionJaToNej_WithdrawsThatFactorsActions()
    {
        var id = await SetUpManagerWithOutbreakAsync();
        var sut = Sut();
        await sut.SaveAssessmentAsync(7, id, AllNo with { Climate = true }, [new ActionInput(TailBiteFactor.Climate, "Tjek ventil", 7, Now.AddDays(1))]);
        await sut.SaveAssessmentAsync(7, id, AllNo, []);
        BackendConfigurationPnDbContext!.ChangeTracker.Clear();
        Assert.That(BackendConfigurationPnDbContext.TailBiteAssessmentActions.Single().WithdrawnAt, Is.Not.Null);
        Assert.That(BackendConfigurationPnDbContext.TailBiteRiskAssessments.Single().Climate, Is.False);
    }

    [Test]
    public async Task Revision_KeepsLiveActions_AndDoesNotDuplicateAssessment()
    {
        var id = await SetUpManagerWithOutbreakAsync();
        var sut = Sut();
        await sut.SaveAssessmentAsync(7, id, AllNo with { Climate = true }, [new ActionInput(TailBiteFactor.Climate, "Tjek ventil", 7, Now.AddDays(1))]);
        await sut.SaveAssessmentAsync(7, id, AllNo with { Climate = true, Water = true }, [new ActionInput(TailBiteFactor.Water, "Tjek vand", 7, Now.AddDays(2))]);
        BackendConfigurationPnDbContext!.ChangeTracker.Clear();
        Assert.That(BackendConfigurationPnDbContext.TailBiteRiskAssessments.Count(), Is.EqualTo(1));
        Assert.That(BackendConfigurationPnDbContext.TailBiteAssessmentActions.Count(a => a.WithdrawnAt == null), Is.EqualTo(2));
    }

    [Test]
    public async Task RevisionJaToNej_KeepsDoneActionsDone()
    {
        await SeedTreeAsync(); await SeedWorkerAsync(7, manager: true); await SeedWorkerAsync(8);
        var sut = Sut();
        var (id, actionId) = await AssessedWithActionAsync(sut);
        await sut.SetActionDoneAsync(7, actionId, true);
        await sut.SaveAssessmentAsync(7, id, AllNo, []);
        BackendConfigurationPnDbContext!.ChangeTracker.Clear();
        var action = BackendConfigurationPnDbContext.TailBiteAssessmentActions.Single();
        Assert.That(action.DoneAt, Is.Not.Null);
        Assert.That(action.WithdrawnAt, Is.Null);
    }

    [Test]
    public async Task Assessment_FollowUpDate_DefaultOrBeforeOpened_Rejected()
    {
        var id = await SetUpManagerWithOutbreakAsync();
        var sut = Sut();
        await Assert.ThrowsAsync<TailBiteValidationException>(() => sut.SaveAssessmentAsync(7, id, AllNo with { Climate = true },
            [new ActionInput(TailBiteFactor.Climate, "Tjek ventil", 7, default)]));
        await Assert.ThrowsAsync<TailBiteValidationException>(() => sut.SaveAssessmentAsync(7, id, AllNo with { Climate = true },
            [new ActionInput(TailBiteFactor.Climate, "Tjek ventil", 7, Now.AddDays(-1))]));
        await Assert.DoesNotThrowAsync(() => sut.SaveAssessmentAsync(7, id, AllNo with { Climate = true },
            [new ActionInput(TailBiteFactor.Climate, "Tjek ventil", 7, Now)]));
    }

    [Test]
    public async Task ReanswerJaAfterWithdrawal_RequiresNewAction()
    {
        var id = await SetUpManagerWithOutbreakAsync();
        var sut = Sut();
        await sut.SaveAssessmentAsync(7, id, AllNo with { Climate = true }, [new ActionInput(TailBiteFactor.Climate, "Tjek ventil", 7, Now.AddDays(1))]);
        await sut.SaveAssessmentAsync(7, id, AllNo, []);
        await Assert.ThrowsAsync<TailBiteValidationException>(() => sut.SaveAssessmentAsync(7, id, AllNo with { Climate = true }, []));
        await Assert.DoesNotThrowAsync(() => sut.SaveAssessmentAsync(7, id, AllNo with { Climate = true },
            [new ActionInput(TailBiteFactor.Climate, "Tjek ventil igen", 7, Now.AddDays(2))]));
    }

    [Test]
    public async Task ConcurrentSaveAssessment_BothSucceed_OneAssessmentRow()
    {
        var id = await SetUpManagerWithOutbreakAsync();
        var (db1, db2) = (CreateFreshBackendConfigurationDbContext(), CreateFreshBackendConfigurationDbContext());
        TailBiteOutbreakService Fresh(BackendConfigurationPnDbContext db)
            => new(db, new TailBitePropertyLock(db), NewAccess(db), Clock);
        var answers = AllNo with { Climate = true };
        await Task.WhenAll(
            Fresh(db1).SaveAssessmentAsync(7, id, answers, [new ActionInput(TailBiteFactor.Climate, "Tjek ventil", 7, Now.AddDays(1))]),
            Fresh(db2).SaveAssessmentAsync(7, id, answers, [new ActionInput(TailBiteFactor.Climate, "Tjek ventil", 7, Now.AddDays(1))]));
        Assert.That(CreateFreshBackendConfigurationDbContext().TailBiteRiskAssessments.Count(), Is.EqualTo(1));
    }

    [Test]
    public async Task Assessment_OnClosedOutbreak_Refused()
    {
        var id = await SetUpManagerWithOutbreakAsync();
        await CloseDirectlyAsync(id);
        await Assert.ThrowsAsync<TailBiteConflictException>(() => Sut().SaveAssessmentAsync(7, id, AllNo, []));
    }

    [Test]
    public async Task NonManager_Forbidden_EverywhereIncludingList()
    {
        await SeedTreeAsync(); await SeedWorkerAsync(7); var id = await OpenOutbreakAsync();
        var sut = Sut();
        await Assert.ThrowsAsync<TailBiteForbiddenException>(() => sut.ListAsync(7, PropertyId, true));
        await Assert.ThrowsAsync<TailBiteForbiddenException>(() => sut.GetAsync(7, id));
        await Assert.ThrowsAsync<TailBiteForbiddenException>(() => sut.CloseAsync(7, id));
    }

    [Test]
    public async Task RemovedManager_CannotAct_NewManagerCan()
    {
        await SeedTreeAsync(); await SeedWorkerAsync(7, manager: true); await SeedWorkerAsync(8); var id = await OpenOutbreakAsync();
        var (_, actionId) = await AssessedWithActionAsync(Sut(), outbreakId: id);
        // Re-query: the service's lock cleared the change tracker, so a pre-lock instance would be detached.
        var old = BackendConfigurationPnDbContext!.PropertyWorkers.Single(p => p.WorkerId == 7 && p.PropertyId == PropertyId);
        await old.Delete(BackendConfigurationPnDbContext);
        var newManager = BackendConfigurationPnDbContext!.PropertyWorkers.Single(p => p.WorkerId == 8 && p.PropertyId == PropertyId);
        newManager.TailBiteManager = true; await newManager.Update(BackendConfigurationPnDbContext);
        var sut = Sut();
        await Assert.ThrowsAsync<TailBiteForbiddenException>(() => sut.SaveAssessmentAsync(7, id, AllNo, []));
        await Assert.ThrowsAsync<TailBiteForbiddenException>(() => sut.SetActionDoneAsync(7, actionId, true));
        await Assert.ThrowsAsync<TailBiteForbiddenException>(() => sut.CloseAsync(7, id));
        await Assert.DoesNotThrowAsync(() => sut.SaveAssessmentAsync(8, id, AllNo, []));
    }

    [Test]
    public async Task CancelRegistration_SetsCancelled_DoesNotCloseOutbreak()
    {
        var id = await SetUpManagerWithOutbreakAsync();
        var reg = await NewRegistrationAsync(PropertyId);
        await Sut().CancelRegistrationAsync(7, reg.Id, "Registreret på forkert sti");
        BackendConfigurationPnDbContext!.ChangeTracker.Clear();
        var cancelled = BackendConfigurationPnDbContext.TailBiteRegistrations.Single(r => r.Id == reg.Id);
        Assert.That(cancelled.CancelledAt, Is.Not.Null);
        Assert.That(cancelled.CancelledBySiteId, Is.EqualTo(7));
        Assert.That(cancelled.CancelReason, Is.EqualTo("Registreret på forkert sti"));
        Assert.That(BackendConfigurationPnDbContext.TailBiteOutbreaks.Single(o => o.Id == id).ClosedAt, Is.Null);
    }

    [Test]
    public async Task CancelRegistration_IsIdempotent_AndNeedsReason()
    {
        await SeedTreeAsync(); await SeedWorkerAsync(7, manager: true);
        var reg = await NewRegistrationAsync(PropertyId);
        var sut = Sut();
        await Assert.ThrowsAsync<TailBiteValidationException>(() => sut.CancelRegistrationAsync(7, reg.Id, "  "));
        await sut.CancelRegistrationAsync(7, reg.Id, "Første årsag");
        await sut.CancelRegistrationAsync(7, reg.Id, "Anden årsag");
        BackendConfigurationPnDbContext!.ChangeTracker.Clear();
        Assert.That(BackendConfigurationPnDbContext.TailBiteRegistrations.Single(r => r.Id == reg.Id).CancelReason, Is.EqualTo("Første årsag"));
    }

    [Test]
    public async Task CancelRegistration_OtherPropertysRegistration_Forbidden()
    {
        await SeedTreeAsync(); await SeedWorkerAsync(7, manager: true);
        var reg = await NewRegistrationAsync(PropertyId + 1000);
        await Assert.ThrowsAsync<TailBiteForbiddenException>(() => Sut().CancelRegistrationAsync(7, reg.Id, "Forkert"));
    }

    [Test]
    public async Task OtherPropertysOutbreak_Forbidden()
    {
        await SeedTreeAsync(); await SeedWorkerAsync(7, manager: true);
        var foreign = await NewForeignOutbreakAsync();
        await Assert.ThrowsAsync<TailBiteForbiddenException>(() => Sut().GetAsync(7, foreign.Id));
    }

    [Test]
    public async Task GetForAction_ReturnsOwningOutbreak_NonManagerForbidden_UnknownNotFound()
    {
        await SeedTreeAsync(); await SeedWorkerAsync(7, manager: true); await SeedWorkerAsync(8);
        var sut = Sut();
        var (outbreakId, actionId) = await AssessedWithActionAsync(sut);
        var detail = await sut.GetForActionAsync(7, actionId);
        Assert.That(detail.Summary.Id, Is.EqualTo(outbreakId));
        Assert.That(detail.Actions.Single().Id, Is.EqualTo(actionId));
        await Assert.ThrowsAsync<TailBiteForbiddenException>(() => sut.GetForActionAsync(8, actionId));
        await Assert.ThrowsAsync<TailBiteForbiddenException>(() => sut.GetForActionAsync(7, actionId + 999));
    }

    [Test]
    public async Task MissingIds_RefusedLikeForeignIds()
    {
        await SeedTreeAsync(); await SeedWorkerAsync(7, manager: true); await SeedWorkerAsync(8);
        var sut = Sut();
        var foreign = await NewForeignOutbreakAsync();
        var foreignAssessment = new TailBiteRiskAssessment { OutbreakId = foreign.Id, Climate = true, AssessedBySiteId = 9, AssessedAt = Now };
        await foreignAssessment.Create(BackendConfigurationPnDbContext!);
        var foreignAction = new TailBiteAssessmentAction { AssessmentId = foreignAssessment.Id, Factor = TailBiteFactor.Climate,
            Description = "Tjek ventil 4", ResponsibleSiteId = 9, FollowUpDate = Now.AddDays(3) };
        await foreignAction.Create(BackendConfigurationPnDbContext!);
        var foreignRegistration = await NewRegistrationAsync(PropertyId + 1000);
        const int missing = int.MaxValue;

        await AssertRefusedAlike(() => sut.GetAsync(7, missing), () => sut.GetAsync(7, foreign.Id));
        await AssertRefusedAlike(() => sut.SaveAssessmentAsync(7, missing, AllNo, []), () => sut.SaveAssessmentAsync(7, foreign.Id, AllNo, []));
        await AssertRefusedAlike(() => sut.CloseAsync(7, missing), () => sut.CloseAsync(7, foreign.Id));
        await AssertRefusedAlike(() => sut.GetForActionAsync(7, missing), () => sut.GetForActionAsync(7, foreignAction.Id));
        await AssertRefusedAlike(() => sut.SetActionDoneAsync(7, missing, true), () => sut.SetActionDoneAsync(7, foreignAction.Id, true));
        await AssertRefusedAlike(() => sut.WithdrawActionAsync(7, missing, "Dublet"), () => sut.WithdrawActionAsync(7, foreignAction.Id, "Dublet"));
        await AssertRefusedAlike(() => sut.ReassignActionAsync(7, missing, 8), () => sut.ReassignActionAsync(7, foreignAction.Id, 8));
        await AssertRefusedAlike(() => sut.CancelRegistrationAsync(7, missing, "Forkert"),
            () => sut.CancelRegistrationAsync(7, foreignRegistration.Id, "Forkert"));
        // A plain worker on the property gets the same refusal for an id that does exist there.
        var own = await OpenOutbreakAsync();
        await AssertRefusedAlike(() => sut.GetAsync(8, missing), () => sut.GetAsync(8, own));
    }

    [Test]
    public async Task Done_SetsAndClears()
    {
        await SeedTreeAsync(); await SeedWorkerAsync(7, manager: true); await SeedWorkerAsync(8);
        var sut = Sut();
        var (_, actionId) = await AssessedWithActionAsync(sut);
        await sut.SetActionDoneAsync(7, actionId, true);
        BackendConfigurationPnDbContext!.ChangeTracker.Clear();
        var done = BackendConfigurationPnDbContext.TailBiteAssessmentActions.Single();
        Assert.That(done.DoneAt, Is.Not.Null);
        Assert.That(done.DoneBySiteId, Is.EqualTo(7));
        await sut.SetActionDoneAsync(7, actionId, false);
        BackendConfigurationPnDbContext.ChangeTracker.Clear();
        var undone = BackendConfigurationPnDbContext.TailBiteAssessmentActions.Single();
        Assert.That(undone.DoneAt, Is.Null);
        Assert.That(undone.DoneBySiteId, Is.Null);
    }

    [Test]
    public async Task Withdraw_RequiresReason_AndRecordsIt()
    {
        await SeedTreeAsync(); await SeedWorkerAsync(7, manager: true); await SeedWorkerAsync(8);
        var sut = Sut();
        var (_, actionId) = await AssessedWithActionAsync(sut);
        await Assert.ThrowsAsync<TailBiteValidationException>(() => sut.WithdrawActionAsync(7, actionId, " "));
        await sut.WithdrawActionAsync(7, actionId, "Droppet");
        BackendConfigurationPnDbContext!.ChangeTracker.Clear();
        var a = BackendConfigurationPnDbContext.TailBiteAssessmentActions.Single();
        Assert.That(a.WithdrawnAt, Is.Not.Null);
        Assert.That(a.WithdrawnReason, Is.EqualTo("Droppet"));
    }

    [Test]
    public async Task Reassign_ValidatesWorkerOnProperty()
    {
        await SeedTreeAsync(); await SeedWorkerAsync(7, manager: true); await SeedWorkerAsync(8); await SeedWorkerAsync(9);
        var sut = Sut();
        var (_, actionId) = await AssessedWithActionAsync(sut);
        await Assert.ThrowsAsync<TailBiteValidationException>(() => sut.ReassignActionAsync(7, actionId, 999));
        await sut.ReassignActionAsync(7, actionId, 9);
        BackendConfigurationPnDbContext!.ChangeTracker.Clear();
        Assert.That(BackendConfigurationPnDbContext.TailBiteAssessmentActions.Single().ResponsibleSiteId, Is.EqualTo(9));
    }

    [Test]
    public async Task ActionChanges_OnClosedOutbreak_Refused()
    {
        await SeedTreeAsync(); await SeedWorkerAsync(7, manager: true); await SeedWorkerAsync(8);
        var sut = Sut();
        var (outbreakId, actionId) = await AssessedWithActionAsync(sut);
        await sut.SetActionDoneAsync(7, actionId, true);
        await sut.CloseAsync(7, outbreakId);
        await Assert.ThrowsAsync<TailBiteConflictException>(() => sut.SetActionDoneAsync(7, actionId, false));
        await Assert.ThrowsAsync<TailBiteConflictException>(() => sut.WithdrawActionAsync(7, actionId, "Sent"));
        await Assert.ThrowsAsync<TailBiteConflictException>(() => sut.ReassignActionAsync(7, actionId, 8));
    }

    [Test]
    public async Task Action_OfOtherPropertysOutbreak_Forbidden()
    {
        await SeedTreeAsync(); await SeedWorkerAsync(7, manager: true);
        var foreign = await NewForeignOutbreakAsync();
        var assessment = new TailBiteRiskAssessment { OutbreakId = foreign.Id, AssessedBySiteId = 1, AssessedAt = Now };
        await assessment.Create(BackendConfigurationPnDbContext);
        var action = new TailBiteAssessmentAction { AssessmentId = assessment.Id, Factor = TailBiteFactor.Water, Description = "x", ResponsibleSiteId = 1, FollowUpDate = Now };
        await action.Create(BackendConfigurationPnDbContext);
        await Assert.ThrowsAsync<TailBiteForbiddenException>(() => Sut().SetActionDoneAsync(7, action.Id, true));
    }

    [Test]
    public async Task ListAndGet_ReportSummaryAnswersActionsAndRegistrations()
    {
        await SeedTreeAsync(); await SeedWorkerAsync(7, manager: true); await SeedWorkerAsync(8);
        var sut = Sut();
        var (reg, rowIds) = await SeedRegistrationAsync(Now, false, (Pen309Id, 3, 1));
        var (outbreakId, _) = await AssessedWithActionAsync(sut);
        await new TailBiteOutbreakLink { OutbreakId = outbreakId, RegistrationLocationId = rowIds[0] }.Create(BackendConfigurationPnDbContext!);
        var other = await OpenOutbreakAsync(StableBId); // a different location: at most one open outbreak per location
        await CloseDirectlyAsync(other);

        var open = await sut.ListAsync(7, PropertyId, openOnly: true);
        Assert.That(open.Select(o => o.Id), Is.EqualTo(new[] { outbreakId }));
        Assert.That(open[0].Assessed, Is.True);
        Assert.That(open[0].OpenActions, Is.EqualTo(1));
        Assert.That((await sut.ListAsync(7, PropertyId, openOnly: false)).Count, Is.EqualTo(2));

        var detail = await sut.GetAsync(7, outbreakId);
        Assert.That(detail.RuleId, Is.EqualTo(RuleId));
        Assert.That(detail.RegistrationIds, Is.EqualTo(new[] { reg.Id }));
        Assert.That(detail.Answers!.Climate, Is.True);
        Assert.That(detail.Actions.Single().Description, Is.EqualTo("Tjek ventil 4"));
    }
}
