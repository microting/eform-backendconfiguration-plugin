import {TailBiteFactor, TailBiteOutbreakAction, TailBiteOutbreakDetail} from '../../../../models';
import {
  assessmentErrors,
  buildSaveRequest,
  draftFromDetail,
  emptyDraftAction,
  TailBiteFactorDraft,
} from './tail-bite-assessment';

const detail = (answers: TailBiteOutbreakDetail['answers'], actions: TailBiteOutbreakAction[] = []): TailBiteOutbreakDetail => ({
  summary: {id: 5, locationId: 3, openedAt: '2026-10-01T06:12:00', assessed: answers !== null, openActions: 0, bittenPigs: 0, severePigs: 0, closed: false},
  ruleId: 1, ruleVersion: 1, registrationIds: [21], answers, actions,
});
const allNo = {water: false, feed: false, activityMaterial: false, climate: false, health: false, management: false};
const action = (factor: TailBiteFactor, over: Partial<TailBiteOutbreakAction> = {}): TailBiteOutbreakAction =>
  ({id: 1, factor, description: 'Tjek', responsibleSiteId: 7, followUpDate: '2026-10-06T00:00:00', doneAt: null, withdrawnAt: null, ...over});
const answerAll = (drafts: TailBiteFactorDraft[], yes: TailBiteFactor[] = []) =>
  drafts.forEach((d) => (d.answer = yes.includes(d.factor)));

describe('tail-bite assessment form', () => {
  it('starts unanswered before the first assessment and from the saved answers after it', () => {
    expect(draftFromDetail(detail(null)).every((d) => d.answer === null)).toBe(true);
    const drafts = draftFromDetail(detail({...allNo, climate: true}));
    expect(drafts.find((d) => d.factor === TailBiteFactor.Climate)!.answer).toBe(true);
    expect(drafts.find((d) => d.factor === TailBiteFactor.Water)!.answer).toBe(false);
  });

  it('requires all six answers', () => {
    const errors = assessmentErrors(draftFromDetail(detail(null)), detail(null));
    expect(errors.size).toBe(6);
    expect(errors.get(TailBiteFactor.Feed)).toBe('unanswered');
  });

  it('requires an action for a yes, ignoring an untouched blank row', () => {
    const d = detail(null);
    const drafts = draftFromDetail(d);
    answerAll(drafts, [TailBiteFactor.Feed]);
    drafts[1].newActions.push(emptyDraftAction());
    expect([...assessmentErrors(drafts, d)]).toEqual([[TailBiteFactor.Feed, 'needsAction']]);
  });

  it('flags a half-filled action even when a saved one covers the factor', () => {
    const d = detail({...allNo, feed: true}, [action(TailBiteFactor.Feed)]);
    const drafts = draftFromDetail(d);
    drafts[1].newActions.push({description: 'Mere halm', responsibleSiteId: null, followUpDate: null});
    expect(assessmentErrors(drafts, d).get(TailBiteFactor.Feed)).toBe('incompleteAction');
  });

  it('accepts a yes covered by a saved action, but not by a withdrawn one', () => {
    const live = detail({...allNo, feed: true}, [action(TailBiteFactor.Feed, {doneAt: '2026-10-02T07:00:00'})]);
    expect(assessmentErrors(draftFromDetail(live), live).size).toBe(0);
    const withdrawn = detail({...allNo, feed: true}, [action(TailBiteFactor.Feed, {withdrawnAt: '2026-10-02T07:00:00'})]);
    expect(assessmentErrors(draftFromDetail(withdrawn), withdrawn).get(TailBiteFactor.Feed)).toBe('needsAction');
  });

  it('builds the request with trimmed, date-only actions and drops actions on factors answered no', () => {
    const d = detail(null);
    const drafts = draftFromDetail(d);
    answerAll(drafts, [TailBiteFactor.Feed]);
    drafts[1].newActions.push({description: '  Tjek foderautomat ', responsibleSiteId: 7, followUpDate: new Date(2026, 9, 6, 15)});
    drafts[0].newActions.push({description: 'Glemt', responsibleSiteId: 7, followUpDate: new Date(2026, 9, 6)});
    expect(assessmentErrors(drafts, d).size).toBe(0);
    expect(buildSaveRequest(drafts)).toEqual({
      answers: {...allNo, feed: true},
      newActions: [{factor: TailBiteFactor.Feed, description: 'Tjek foderautomat', responsibleSiteId: 7, followUpDate: '2026-10-06'}],
    });
  });
});
