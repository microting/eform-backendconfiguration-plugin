import {TailBiteLocationNode, TailBiteOutbreakSummary} from '../../../../models';
import {outbreakRows} from './tail-bite-outbreak-rows';

const summary = (over: Partial<TailBiteOutbreakSummary> = {}): TailBiteOutbreakSummary =>
  ({id: 1, locationId: 3, openedAt: '2026-10-01T06:12:00', assessed: false, openActions: 0, closed: false, bittenPigs: 0, severePigs: 0, ...over});

describe('outbreakRows', () => {
  const nodes: TailBiteLocationNode[] = [
    {id: 1, parentId: null, name: 'Ejendom', depth: 0, sortOrder: 0, qrCode: 'a', removed: false},
    {id: 2, parentId: 1, name: 'Stald A', depth: 1, sortOrder: 0, qrCode: 'b', removed: false},
    {id: 3, parentId: 2, name: 'Sektion 4', depth: 2, sortOrder: 0, qrCode: 'c', removed: true},
  ];

  it('titles by path (removed locations included), parses UTC and keeps the status', () => {
    const [row] = outbreakRows([summary({assessed: true, openActions: 1})], nodes);
    expect(row.title).toBe('Stald A › Sektion 4');
    expect(row.openedAt!.toISOString()).toBe('2026-10-01T06:12:00.000Z');
    expect(row.status).toBe('followUp');
  });

  it('falls back to the id when the location is unknown', () => {
    expect(outbreakRows([summary({locationId: 99})], nodes)[0].title).toBe('#99');
  });

  it('marks a closed outbreak as closed whatever else it carries', () => {
    expect(outbreakRows([summary({closed: true, assessed: true, openActions: 2})], nodes)[0].status).toBe('closed');
  });
});
