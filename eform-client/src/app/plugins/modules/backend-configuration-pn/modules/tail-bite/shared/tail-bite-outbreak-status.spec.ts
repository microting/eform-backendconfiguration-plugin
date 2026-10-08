import {TailBiteOutbreakSummary} from '../../../models';
import {outbreakStatus} from './tail-bite-outbreak-status';

const summary = (over: Partial<TailBiteOutbreakSummary> = {}): TailBiteOutbreakSummary =>
  ({id: 1, locationId: 3, openedAt: '2026-10-01T06:12:00', assessed: false, openActions: 0, closed: false, bittenPigs: 0, severePigs: 0, ...over});

describe('outbreakStatus', () => {
  it.each([
    [{}, 'needsAssessment'],
    [{assessed: true, openActions: 2}, 'followUp'],
    [{assessed: true, openActions: 0}, 'readyToClose'],
    [{assessed: true, openActions: 1, closed: true}, 'closed'],
  ])('%j -> %s', (over, expected) => {
    expect(outbreakStatus(summary(over))).toBe(expected);
  });
});
