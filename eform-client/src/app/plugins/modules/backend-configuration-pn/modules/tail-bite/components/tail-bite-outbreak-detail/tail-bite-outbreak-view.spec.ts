import {buildOutbreakView} from './tail-bite-outbreak-view';

describe('buildOutbreakView', () => {
  const tree = {propertyId: 3, treeVersion: 1, actionTypes: [{id: 30, code: 'REB', name: 'Reb', sortOrder: 0}], locations: [
    {id: 1, parentId: null, name: 'Ejendom', depth: 0, sortOrder: 0, qrCode: 'a', removed: false},
    {id: 2, parentId: 1, name: 'Stald A', depth: 1, sortOrder: 0, qrCode: 'b', removed: false},
    {id: 3, parentId: 2, name: 'Sti 309', depth: 2, sortOrder: 0, qrCode: 'c', removed: false},
    {id: 4, parentId: 2, name: 'Sti 310', depth: 2, sortOrder: 1, qrCode: 'd', removed: true},
  ]};
  const detail = {
    summary: {id: 5, locationId: 2, openedAt: '2026-10-01T06:12:00', assessed: true, openActions: 1, closed: false, bittenPigs: 3, severePigs: 1},
    ruleId: 9, ruleVersion: 3, registrationIds: [21, 22], answers: null, actions: [],
  };
  const row = (registrationId: number, locationId: number, minor: number, severe: number, cancelled = false) => ({
    registrationId, rowId: registrationId * 10, locationId, effectiveAt: '2026-09-30T05:58:00Z', minor, severe,
    actionTypeIds: registrationId === 21 ? [30, 99] : [], siteId: 7, siteName: 'Jane Doe', cancelled, cancelReason: null, photoCount: 1,
  });
  const history = [
    {version: 3, locationId: 1, minBittenPigs: 5, minSevere: 1, windowDays: 7, countDepth: 1, changedAt: '2026-08-12T10:00:00Z'},
    {version: 2, locationId: 1, minBittenPigs: 8, minSevere: null, windowDays: 14, countDepth: 1, changedAt: null},
  ];
  const occupancy = [
    {locationId: 3, pigCount: 30, source: 0, validFrom: '2026-09-15T00:00:00Z'},
    {locationId: 4, pigCount: 20, source: 0, validFrom: '2026-09-20T00:00:00Z'},
  ];

  it('counts locations over rows that are not cancelled, and names actions', () => {
    const view = buildOutbreakView(detail, {propertyId: 3, actionTypes: [], rows: [row(21, 3, 2, 1), row(22, 4, 1, 0), row(23, 3, 4, 0, true)]},
      tree, occupancy, history, 'Deleted location');
    expect(view.locationCount).toBe(2);
    expect(view.rows[0].actions).toBe('Reb, #99');
    expect(view.rows[1].actions).toBe('–');
    expect(view.rows[1].location).toBe('Sti 310');
    expect(view.title).toBe('Stald A');
    expect(view.status).toBe('followUp');
  });

  it('names action types deleted since, from the registrations\' own list', () => {
    const view = buildOutbreakView(detail, {propertyId: 3, actionTypes: [{id: 99, name: 'Kæder'}], rows: [row(21, 3, 2, 1)]},
      tree, occupancy, history, 'Deleted location');
    expect(view.rows[0].actions).toBe('Reb, Kæder');
  });

  it('dates the pig count from the counts it is made of, not from counts an own count overrides', () => {
    // Stald A has its own count (September 1); the count on Sti 309 below it (September 20) does not take part.
    const counts = [
      {locationId: 2, pigCount: 400, source: 0, validFrom: '2026-09-01T00:00:00Z'},
      {locationId: 3, pigCount: 30, source: 0, validFrom: '2026-09-20T00:00:00Z'},
    ];
    const view = buildOutbreakView(detail, {propertyId: 3, actionTypes: [], rows: []}, tree, counts, history, 'Deleted location');
    expect(view.pigs).toBe(400);
    expect(view.pigsFrom!.toISOString()).toBe('2026-09-01T00:00:00.000Z');
  });

  it('picks the rule version that opened the outbreak', () => {
    const view = buildOutbreakView(detail, {propertyId: 3, actionTypes: [], rows: []}, tree, occupancy, history, 'Deleted location');
    expect(view.rule!.version).toBe(3);
    expect(view.ruleLocation).toBe('Ejendom');
  });

  it('gives a rate per 100 pigs from the live counts below the summing location', () => {
    const view = buildOutbreakView(detail, {propertyId: 3, actionTypes: [], rows: [row(21, 3, 2, 1)]}, tree, occupancy, history, 'Deleted location');
    expect(view.pigs).toBe(30);
    expect(view.pigsFrom!.toISOString()).toBe('2026-09-15T00:00:00.000Z');
    expect(view.ratePer100).toBe(10);
  });

  it('reads every server date the same when the host DateInterceptor already turned it into a Date', () => {
    const served = {...detail, summary: {...detail.summary, openedAt: new Date(Date.UTC(2026, 9, 1, 6, 12))}};
    const regs = {propertyId: 3, actionTypes: [], rows: [{...row(21, 3, 2, 1), effectiveAt: new Date(Date.UTC(2026, 8, 30, 5, 58))}]};
    const counts = occupancy.map((o) => ({...o, validFrom: new Date(o.validFrom)}));
    const view = buildOutbreakView(served, regs, tree, counts, history, 'Deleted location');
    expect(view.openedAt!.toISOString()).toBe('2026-10-01T06:12:00.000Z');
    expect(view.rows[0].effectiveAt!.toISOString()).toBe('2026-09-30T05:58:00.000Z');
    expect(view.pigsFrom!.toISOString()).toBe('2026-09-15T00:00:00.000Z');
  });

  it('has no rate without a pig count, and labels unknown locations', () => {
    const view = buildOutbreakView(detail, {propertyId: 3, actionTypes: [], rows: [row(21, 77, 1, 0)]}, tree, [], [], 'Deleted location');
    expect([view.pigs, view.ratePer100, view.rule]).toEqual([null, null, null]);
    expect(view.rows[0].location).toBe('Deleted location');
  });

  it('takes the pig count from the server summary, not from the rows, so page and list cannot disagree', () => {
    const served = {...detail, summary: {...detail.summary, bittenPigs: 9, severePigs: 2}};
    const view = buildOutbreakView(served, {propertyId: 3, actionTypes: [], rows: [row(21, 3, 2, 1)]}, tree, occupancy, history, 'Deleted location');
    expect(view.bittenPigs).toBe(9);
    expect(view.ratePer100).toBe(30);
  });

  it('marks only the first row of a registration, so per-registration controls and ids are unique', () => {
    const second = {...row(21, 4, 1, 0), rowId: 211};
    const view = buildOutbreakView(detail, {propertyId: 3, actionTypes: [], rows: [row(21, 3, 2, 1), second, row(22, 3, 1, 0)]},
      tree, occupancy, history, 'Deleted location');
    expect(view.rows.map((r) => r.firstOfRegistration)).toEqual([true, false, true]);
  });
});
