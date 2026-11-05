import {describe, it, expect} from 'vitest';
import {
  CallerFlags,
  NO_CALLER_FLAGS,
  callerFlags,
  isLeaderLoginLocked,
  leaderCell,
  leaderToSend,
  newWorkerRow,
  showLeaderToggle,
  workerTagsToSend,
} from './leader.helper';

const admin: CallerFlags = {canSetLeder: true, canEditWorkerTags: true};
const leder: CallerFlags = {canSetLeder: false, canEditWorkerTags: true};
const staff: CallerFlags = {canSetLeder: false, canEditWorkerTags: false};

describe('callerFlags', () => {
  it('reads the flags every row carries', () => {
    expect(callerFlags([leder], true)).toEqual(leder);
  });

  it('with no rows and no earlier answer the role decides, for both flags', () => {
    expect(callerFlags([], true)).toEqual(admin);
    expect(callerFlags(null, false)).toEqual(staff);
    expect(callerFlags([], false, null)).toEqual(staff);
  });

  it('with no rows the last non-empty answer still holds: a Leder keeps worker groups', () => {
    expect(callerFlags([], false, leder)).toEqual(leder);
    expect(callerFlags(null, true, staff)).toEqual(staff);
  });

  it('rows win over an earlier answer', () => {
    expect(callerFlags([admin], false, leder)).toEqual(admin);
  });

  it('a row without the flags (older API) allows nothing', () => {
    expect(callerFlags([{}], true)).toEqual(staff);
  });

  it('nothing is allowed by default', () => {
    expect(NO_CALLER_FLAGS).toEqual(staff);
  });
});

describe('showLeaderToggle', () => {
  it('admins get the toggle on every worker: no login, no access, an admin login', () => {
    expect(showLeaderToggle({...admin, isLeader: false, loginIsManaged: true, isAdminLogin: false})).toBe(true);
    expect(showLeaderToggle({...admin, isLeader: false, loginIsManaged: false, isAdminLogin: true})).toBe(true);
    expect(showLeaderToggle({...admin, isLeader: null})).toBe(true);
    expect(showLeaderToggle(newWorkerRow(admin))).toBe(true);
  });

  it('non-admins never see it, Leders included', () => {
    expect(showLeaderToggle({...leder, isLeader: true})).toBe(false);
    expect(showLeaderToggle(staff)).toBe(false);
    expect(showLeaderToggle({})).toBe(false);
  });
});

describe('leaderToSend', () => {
  it('sends the new value only when it changed', () => {
    expect(leaderToSend(false, true)).toBe(true);
    expect(leaderToSend(true, false)).toBe(false);
    expect(leaderToSend(true, true)).toBeNull();
    expect(leaderToSend(false, false)).toBeNull();
  });

  it('a missing stored value (older API) counts as off', () => {
    expect(leaderToSend(null, false)).toBeNull();
    expect(leaderToSend(null, true)).toBe(true);
  });

  it('no choice is no change', () => {
    expect(leaderToSend(true, null)).toBeNull();
    expect(leaderToSend(null, null)).toBeNull();
  });
});

describe('leaderCell', () => {
  it('a Leder reads Ja with the success tag, everyone else Nej with the error tag', () => {
    expect(leaderCell({isLeader: true})).toEqual({label: 'Yes', cssClass: 'tag-success', state: 'yes'});
    expect(leaderCell({isLeader: false})).toEqual({label: 'No', cssClass: 'tag-error', state: 'no'});
  });

  it('a row from an older API without the flag reads Nej', () => {
    expect(leaderCell({isLeader: null})).toEqual({label: 'No', cssClass: 'tag-error', state: 'no'});
    expect(leaderCell({})).toEqual({label: 'No', cssClass: 'tag-error', state: 'no'});
  });
});

describe('isLeaderLoginLocked', () => {
  it('locks a Leder\'s login for everyone but admins', () => {
    expect(isLeaderLoginLocked({...staff, isLeader: true})).toBe(true);
    expect(isLeaderLoginLocked({...leder, isLeader: true})).toBe(true);
    expect(isLeaderLoginLocked({...admin, isLeader: true})).toBe(false);
    expect(isLeaderLoginLocked({...staff, isLeader: false})).toBe(false);
    expect(isLeaderLoginLocked({...staff, isLeader: null})).toBe(false);
  });
});

describe('workerTagsToSend (D-L12)', () => {
  it('admins and Leders send what the field holds', () => {
    expect(workerTagsToSend({...admin, tags: [1]}, [1, 2])).toEqual([1, 2]);
    expect(workerTagsToSend({...leder, tags: [1]}, [])).toEqual([]);
  });

  it('a disabled field (resigned worker) is missing from the form value: the stored set goes back', () => {
    expect(workerTagsToSend({...admin, tags: [1, 2]}, undefined)).toEqual([1, 2]);
  });

  it('everyone else sends the stored set unchanged, never an empty list', () => {
    expect(workerTagsToSend({...staff, tags: [1, 2]}, [])).toEqual([1, 2]);
    expect(workerTagsToSend({...staff, tags: [1, 2]}, [3])).toEqual([1, 2]);
  });

  it('a row without tags sends an empty list', () => {
    expect(workerTagsToSend(staff, undefined)).toEqual([]);
  });

  it('a null edited value sends the stored set', () => {
    expect(workerTagsToSend({...admin, tags: [1, 2]}, null)).toEqual([1, 2]);
  });

  it('returns a copy, never the stored array itself', () => {
    const row = {...staff, tags: [1]};
    expect(workerTagsToSend(row, [2])).not.toBe(row.tags);
    const admRow = {...admin, tags: [1]};
    expect(workerTagsToSend(admRow, undefined)).not.toBe(admRow.tags);
  });
});

describe('newWorkerRow', () => {
  it('a new worker is no Leder yet, carries the caller flags and no worker groups', () => {
    expect(newWorkerRow(leder)).toEqual({canSetLeder: false, canEditWorkerTags: true, isLeader: false, tags: []});
  });
});
