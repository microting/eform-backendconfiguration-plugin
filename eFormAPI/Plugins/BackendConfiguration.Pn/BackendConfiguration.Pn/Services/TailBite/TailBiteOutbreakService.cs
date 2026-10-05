/*
The MIT License (MIT)

Copyright (c) 2007 - 2022 Microting A/S

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

#nullable enable

namespace BackendConfiguration.Pn.Services.TailBite;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;

/// <summary>
/// Outbreaks: list/get, the risk assessment with its rules, follow-up actions, closing and cancelling registrations.
/// Every method needs a tail-bite manager on the property derived from the entity (§7.1), never one the client names.
/// </summary>
public interface ITailBiteOutbreakService
{
    Task<IReadOnlyList<OutbreakSummary>> ListAsync(int callerSiteId, int propertyId, bool openOnly);
    Task<OutbreakDetail> GetAsync(int callerSiteId, int outbreakId);
    Task SaveAssessmentAsync(int callerSiteId, int outbreakId, FactorAnswers answers, IReadOnlyList<ActionInput> newActions);
    Task SetActionDoneAsync(int callerSiteId, int actionId, bool done);
    Task WithdrawActionAsync(int callerSiteId, int actionId, string reason);
    Task ReassignActionAsync(int callerSiteId, int actionId, int responsibleSiteId);
    Task CloseAsync(int callerSiteId, int outbreakId);
    Task CancelRegistrationAsync(int callerSiteId, int registrationId, string reason);
}

/// <remarks>
/// Every write runs under the property lock, which clears the change tracker: entities are re-queried inside the
/// locked work, never carried in from before it. Before the lock only the owning property id is read.
/// </remarks>
public class TailBiteOutbreakService(BackendConfigurationPnDbContext db, ITailBitePropertyLock propertyLock, ITailBiteAccess access,
    TimeProvider clock) : ITailBiteOutbreakService
{
    private const int MaxTextLength = 1000; // the Description / WithdrawnReason / CancelReason columns
    private const string Removed = Constants.WorkflowStates.Removed;

    // ---------- reading ----------

    public async Task<IReadOnlyList<OutbreakSummary>> ListAsync(int callerSiteId, int propertyId, bool openOnly)
    {
        await access.RequireManagerAsync(callerSiteId, propertyId);
        var outbreaks = await db.TailBiteOutbreaks.AsNoTracking()
            .Where(o => o.PropertyId == propertyId && o.WorkflowState != Removed && (!openOnly || o.ClosedAt == null))
            .OrderByDescending(o => o.OpenedAt).ThenByDescending(o => o.Id).ToListAsync();
        var ids = outbreaks.Select(o => o.Id).ToList();
        var assessments = await db.TailBiteRiskAssessments.AsNoTracking()
            .Where(a => ids.Contains(a.OutbreakId) && a.WorkflowState != Removed)
            .Select(a => new { a.Id, a.OutbreakId }).ToListAsync();
        var assessmentOf = assessments.ToDictionary(a => a.OutbreakId, a => a.Id);
        var assessmentIds = assessments.Select(a => a.Id).ToList();
        var openByAssessment = (await db.TailBiteAssessmentActions.AsNoTracking()
                .Where(a => assessmentIds.Contains(a.AssessmentId) && a.WorkflowState != Removed).Where(IsOpen)
                .Select(a => a.AssessmentId).ToListAsync())
            .GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        return outbreaks.Select(o =>
        {
            var assessed = assessmentOf.TryGetValue(o.Id, out var assessmentId);
            return Summary(o, assessed, assessed ? openByAssessment.GetValueOrDefault(assessmentId) : 0);
        }).ToList();
    }

    public async Task<OutbreakDetail> GetAsync(int callerSiteId, int outbreakId)
    {
        var propertyId = await PropertyOfOutbreakAsync(outbreakId);
        await access.RequireManagerAsync(callerSiteId, propertyId);
        var o = await db.TailBiteOutbreaks.AsNoTracking().SingleOrDefaultAsync(x => x.Id == outbreakId && x.WorkflowState != Removed)
                ?? throw new TailBiteNotFoundException("Outbreak not found.");
        var assessment = await db.TailBiteRiskAssessments.AsNoTracking()
            .SingleOrDefaultAsync(a => a.OutbreakId == outbreakId && a.WorkflowState != Removed);
        var actions = assessment is null
            ? []
            : await db.TailBiteAssessmentActions.AsNoTracking()
                .Where(a => a.AssessmentId == assessment.Id && a.WorkflowState != Removed).OrderBy(a => a.Id).ToListAsync();
        var registrationIds = await (from link in db.TailBiteOutbreakLinks
                                     join row in db.TailBiteRegistrationLocations on link.RegistrationLocationId equals row.Id
                                     where link.OutbreakId == outbreakId && link.WorkflowState != Removed
                                     orderby row.RegistrationId
                                     select row.RegistrationId).Distinct().ToListAsync();
        var summary = Summary(o, assessment is not null, actions.Count(IsOpenFunc));
        return new OutbreakDetail(summary, o.RuleId, o.RuleVersion, registrationIds,
            assessment is null ? null : FactorAnswers.FromAssessment(assessment),
            actions.Select(a => new OutbreakActionDetail(a.Id, a.Factor, a.Description, a.ResponsibleSiteId, a.FollowUpDate,
                a.DoneAt, a.WithdrawnAt)).ToList());
    }

    private static OutbreakSummary Summary(TailBiteOutbreak o, bool assessed, int openActions)
        => new(o.Id, o.LocationId, o.OpenedAt, assessed, openActions, o.ClosedAt != null);

    // ---------- assessment ----------

    public Task SaveAssessmentAsync(int callerSiteId, int outbreakId, FactorAnswers a, IReadOnlyList<ActionInput> newActions)
        => OutbreakLockedAsync(callerSiteId, outbreakId, async o =>
        {
            RequireOpen(o);
            await ValidateNewActionsAsync(o, newActions);
            var assessment = await db.TailBiteRiskAssessments.SingleOrDefaultAsync(x => x.OutbreakId == o.Id && x.WorkflowState != Removed);
            var existingActions = assessment is null
                ? []
                : await db.TailBiteAssessmentActions.Where(x => x.AssessmentId == assessment.Id && x.WorkflowState != Removed).ToListAsync();
            var answers = a.ToDictionary();
            RequireActionsForYesFactors(answers, existingActions, newActions);

            var now = clock.GetUtcNow().UtcDateTime;
            assessment = await UpsertAssessmentAsync(assessment, o.Id, a, callerSiteId, now);
            await WithdrawActionsForFactorsNowNoAsync(existingActions, answers, now);
            foreach (var na in newActions)
                await new TailBiteAssessmentAction { AssessmentId = assessment.Id, Factor = na.Factor, Description = na.Description.Trim(),
                    ResponsibleSiteId = na.ResponsibleSiteId, FollowUpDate = na.FollowUpDate }.Create(db);
        });

    private async Task ValidateNewActionsAsync(TailBiteOutbreak o, IReadOnlyList<ActionInput> newActions)
    {
        foreach (var na in newActions)
        {
            if (string.IsNullOrWhiteSpace(na.Description)) throw new TailBiteValidationException("Describe the action.");
            if (na.Description.Trim().Length > MaxTextLength) throw new TailBiteValidationException("The action description is too long.");
            if (na.FollowUpDate == default || na.FollowUpDate.Date < o.OpenedAt.Date)
                throw new TailBiteValidationException("The follow-up date must be set and not before the outbreak opened.");
            await RequireWorkerOnPropertyAsync(o.PropertyId, na.ResponsibleSiteId);
        }
    }

    private static void RequireActionsForYesFactors(IReadOnlyDictionary<TailBiteFactor, bool> answers,
        IReadOnlyList<TailBiteAssessmentAction> existingActions, IReadOnlyList<ActionInput> newActions)
    {
        foreach (var factor in answers.Where(kv => kv.Value).Select(kv => kv.Key))
        {
            var hasLive = existingActions.Any(x => x.Factor == factor && x.WithdrawnAt == null) || newActions.Any(x => x.Factor == factor);
            if (!hasLive) throw new TailBiteValidationException($"'{factor}' is answered yes and needs an action with a responsible person and a date.");
        }
        if (newActions.Any(na => !answers.GetValueOrDefault(na.Factor))) throw new TailBiteValidationException("Actions can only be added to factors answered yes.");
    }

    private async Task<TailBiteRiskAssessment> UpsertAssessmentAsync(TailBiteRiskAssessment? assessment, int outbreakId, FactorAnswers a,
        int siteId, DateTime now)
    {
        var isNew = assessment is null;
        assessment ??= new TailBiteRiskAssessment { OutbreakId = outbreakId };
        (assessment.Water, assessment.Feed, assessment.ActivityMaterial, assessment.Climate, assessment.Health, assessment.Management)
            = (a.Water, a.Feed, a.ActivityMaterial, a.Climate, a.Health, a.Management);
        assessment.AssessedBySiteId = siteId;
        assessment.AssessedAt = now;
        if (isNew) await assessment.Create(db); else await assessment.Update(db);
        return assessment;
    }

    // A revision that turns a Ja into a Nej withdraws that factor's actions (§5), except those already done.
    private async Task WithdrawActionsForFactorsNowNoAsync(IEnumerable<TailBiteAssessmentAction> existingActions,
        IReadOnlyDictionary<TailBiteFactor, bool> answers, DateTime now)
    {
        foreach (var x in existingActions.Where(x => IsOpenFunc(x) && !answers[x.Factor]))
        {
            x.WithdrawnAt = now;
            x.WithdrawnReason = "Svaret ændret til nej";
            await x.Update(db);
        }
    }

    // ---------- actions ----------

    public Task SetActionDoneAsync(int callerSiteId, int actionId, bool done)
        => ActionLockedAsync(callerSiteId, actionId, async (action, _) =>
        {
            RequireActionEditable(action, allowDone: true);
            action.DoneAt = done ? clock.GetUtcNow().UtcDateTime : null;
            action.DoneBySiteId = done ? callerSiteId : null;
            await action.Update(db);
        });

    public Task WithdrawActionAsync(int callerSiteId, int actionId, string reason)
        => ActionLockedAsync(callerSiteId, actionId, async (action, _) =>
        {
            var trimmed = ValidReason(reason);
            if (action.WithdrawnAt != null) return; // idempotent: the first withdrawal and its reason stand
            RequireActionEditable(action, allowDone: false);
            action.WithdrawnAt = clock.GetUtcNow().UtcDateTime;
            action.WithdrawnReason = trimmed;
            await action.Update(db);
        });

    public Task ReassignActionAsync(int callerSiteId, int actionId, int responsibleSiteId)
        => ActionLockedAsync(callerSiteId, actionId, async (action, outbreak) =>
        {
            RequireActionEditable(action, allowDone: false);
            await RequireWorkerOnPropertyAsync(outbreak.PropertyId, responsibleSiteId);
            action.ResponsibleSiteId = responsibleSiteId;
            await action.Update(db);
        });

    // ---------- closing and cancelling ----------

    public Task CloseAsync(int callerSiteId, int outbreakId)
        => OutbreakLockedAsync(callerSiteId, outbreakId, async o =>
        {
            if (o.ClosedAt != null) return;
            var assessment = await db.TailBiteRiskAssessments.SingleOrDefaultAsync(x => x.OutbreakId == o.Id && x.WorkflowState != Removed)
                ?? throw new TailBiteConflictException("Make the risk assessment before closing.");
            var open = await db.TailBiteAssessmentActions.Where(x => x.AssessmentId == assessment.Id && x.WorkflowState != Removed)
                .Where(IsOpen).CountAsync();
            if (open > 0) throw new TailBiteConflictException($"{open} follow-up action(s) are not done or withdrawn.");
            o.ClosedAt = clock.GetUtcNow().UtcDateTime;
            o.ClosedBySiteId = callerSiteId;
            await o.Update(db);
        });

    public async Task CancelRegistrationAsync(int callerSiteId, int registrationId, string reason)
    {
        var propertyId = await db.TailBiteRegistrations.AsNoTracking().Where(r => r.Id == registrationId && r.WorkflowState != Removed)
            .Select(r => (int?)r.PropertyId).SingleOrDefaultAsync()
            ?? throw new TailBiteNotFoundException("Registration not found.");
        await ManagerLockedAsync(callerSiteId, propertyId, async () =>
        {
            var trimmed = ValidReason(reason);
            var reg = await db.TailBiteRegistrations.SingleOrDefaultAsync(r => r.Id == registrationId && r.WorkflowState != Removed)
                      ?? throw new TailBiteNotFoundException("Registration not found.");
            if (reg.CancelledAt != null) return; // idempotent; the first reason stands
            reg.CancelledAt = clock.GetUtcNow().UtcDateTime;
            reg.CancelledBySiteId = callerSiteId;
            reg.CancelReason = trimmed;
            await reg.Update(db);
        });
    }

    // ---------- helpers ----------

    private static readonly Expression<Func<TailBiteAssessmentAction, bool>> IsOpen = a => a.DoneAt == null && a.WithdrawnAt == null;
    private static readonly Func<TailBiteAssessmentAction, bool> IsOpenFunc = IsOpen.Compile();

    private Task ManagerLockedAsync(int callerSiteId, int propertyId, Func<Task> work)
        => TailBiteManagerLock.RunAsync(propertyLock, access, callerSiteId, propertyId, work);

    // Property from the outbreak, then manager + lock, then the outbreak re-queried inside the lock.
    private async Task OutbreakLockedAsync(int callerSiteId, int outbreakId, Func<TailBiteOutbreak, Task> work)
    {
        var propertyId = await PropertyOfOutbreakAsync(outbreakId);
        await ManagerLockedAsync(callerSiteId, propertyId, async () => await work(await LiveOutbreakAsync(outbreakId)));
    }

    // Property from action -> assessment -> outbreak, then manager + lock, then the action and its outbreak re-queried
    // inside the lock; changes to an action of a closed outbreak are refused.
    private async Task ActionLockedAsync(int callerSiteId, int actionId, Func<TailBiteAssessmentAction, TailBiteOutbreak, Task> work)
    {
        var propertyId = await PropertyOfActionAsync(actionId);
        await ManagerLockedAsync(callerSiteId, propertyId, async () =>
        {
            var (action, outbreak) = await LiveActionAsync(actionId);
            RequireOpen(outbreak);
            await work(action, outbreak);
        });
    }

    private async Task<int> PropertyOfOutbreakAsync(int outbreakId)
        => await db.TailBiteOutbreaks.AsNoTracking().Where(o => o.Id == outbreakId && o.WorkflowState != Removed)
               .Select(o => (int?)o.PropertyId).SingleOrDefaultAsync()
           ?? throw new TailBiteNotFoundException("Outbreak not found.");

    private async Task<int> PropertyOfActionAsync(int actionId)
        => await (from a in db.TailBiteAssessmentActions.AsNoTracking()
                  join s in db.TailBiteRiskAssessments on a.AssessmentId equals s.Id
                  join o in db.TailBiteOutbreaks on s.OutbreakId equals o.Id
                  where a.Id == actionId && a.WorkflowState != Removed && s.WorkflowState != Removed && o.WorkflowState != Removed
                  select (int?)o.PropertyId).SingleOrDefaultAsync()
           ?? throw new TailBiteNotFoundException("Action not found.");

    private async Task<TailBiteOutbreak> LiveOutbreakAsync(int outbreakId)
        => await db.TailBiteOutbreaks.SingleOrDefaultAsync(o => o.Id == outbreakId && o.WorkflowState != Removed)
           ?? throw new TailBiteNotFoundException("Outbreak not found.");

    private async Task<(TailBiteAssessmentAction Action, TailBiteOutbreak Outbreak)> LiveActionAsync(int actionId)
    {
        var action = await db.TailBiteAssessmentActions.SingleOrDefaultAsync(a => a.Id == actionId && a.WorkflowState != Removed)
                     ?? throw new TailBiteNotFoundException("Action not found.");
        var outbreakId = await db.TailBiteRiskAssessments.Where(s => s.Id == action.AssessmentId && s.WorkflowState != Removed)
            .Select(s => (int?)s.OutbreakId).SingleOrDefaultAsync()
            ?? throw new TailBiteNotFoundException("Action not found.");
        return (action, await LiveOutbreakAsync(outbreakId));
    }

    private static void RequireOpen(TailBiteOutbreak o)
    {
        if (o.ClosedAt != null) throw new TailBiteConflictException("The outbreak is closed.");
    }

    // A withdrawn action is final; a done one can still be reopened (allowDone) but not withdrawn or reassigned.
    private static void RequireActionEditable(TailBiteAssessmentAction action, bool allowDone)
    {
        if (action.WithdrawnAt != null) throw new TailBiteConflictException("The action is withdrawn.");
        if (!allowDone && action.DoneAt != null) throw new TailBiteConflictException("The action is already done.");
    }

    private async Task RequireWorkerOnPropertyAsync(int propertyId, int siteId)
    {
        if (!await db.PropertyWorkers.AnyAsync(pw => pw.PropertyId == propertyId && pw.WorkerId == siteId && pw.WorkflowState != Removed))
            throw new TailBiteValidationException("The responsible person must be a worker on this property.");
    }

    private static string ValidReason(string reason)
    {
        var trimmed = reason?.Trim() ?? "";
        if (trimmed.Length == 0) throw new TailBiteValidationException("A reason is required.");
        if (trimmed.Length > MaxTextLength) throw new TailBiteValidationException("The reason is too long.");
        return trimmed;
    }
}
