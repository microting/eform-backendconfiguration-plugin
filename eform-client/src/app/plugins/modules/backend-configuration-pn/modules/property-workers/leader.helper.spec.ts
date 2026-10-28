import {describe, it, expect} from 'vitest';
import {
  CallerFlags,
  NO_CALLER_FLAGS,
  callerFlags,
  isLeaderLoginLocked,
  leaderChoice,
  leaderToSend,
  leaderToggleState,
  newWorkerRow,
  workerTagsToSend,
} from './leader.helper';

const admin: CallerFlags = {canSetLeder: true, canEditWorkerTags: true};
const leder: CallerFlags = {canSetLeder: false, canEditWorkerTags: true};
const staff: CallerFlags = {canSetLeder: false, canEditWorkerTags: false};

/** A worker whose login the plugin manages ("eForm users" / "Kun arkiv"), not a Leder yet. */
const managed = {...admin, isLeader: false, loginIsManaged: true, isAdminLogin: false};
/** A worker whose login sits in an owner's own group: Web and Arkiv always read off. */
const unmanaged = {...admin, isLeader: false, loginIsManaged: false, isAdminLogin: false};

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

describe('leaderToggleState', () => {
  it('is hidden for everyone but admins', () => {
    expect(leaderToggleState({...managed, canSetLeder: false}, true, true)).toBe('hidden');
    expect(leaderToggleState({}, true, true)).toBe('hidden');
  });

  it('hidden beats disabled', () => {
    expect(leaderToggleState({...managed, canSetLeder: false, isLeader: null}, false, false)).toBe('hidden');
    expect(leaderToggleState({...managed, isAdminLogin: true}, false, false)).toBe('hidden');
  });

  it('is hidden on an admin login (admin role or "eForm admins": a Leder anyway)', () => {
    expect(leaderToggleState({...managed, isAdminLogin: true}, true, false)).toBe('hidden');
  });

  it('is disabled when the row resolves to no login', () => {
    expect(leaderToggleState({...managed, isLeader: null}, true, true)).toBe('disabled');
    expect(leaderToggleState({...managed, isLeader: undefined}, true, false)).toBe('disabled');
  });

  it('a managed login needs Web or Arkiv', () => {
    expect(leaderToggleState(managed, true, false)).toBe('enabled');
    expect(leaderToggleState(managed, false, true)).toBe('enabled');
    expect(leaderToggleState(managed, false, false)).toBe('disabled');
    expect(leaderToggleState({...managed, isLeader: true}, false, false)).toBe('disabled');
  });

  it('an unmanaged (owner-group) login can be made Leder whatever Web and Arkiv show', () => {
    expect(leaderToggleState(unmanaged, false, false)).toBe('enabled');
    expect(leaderToggleState({...unmanaged, isLeader: true}, false, false)).toBe('enabled');
  });

  it('a new worker (managed once created) follows Web and Arkiv', () => {
    expect(leaderToggleState(newWorkerRow(admin), false, false)).toBe('disabled');
    expect(leaderToggleState(newWorkerRow(admin), false, true)).toBe('enabled');
    expect(leaderToggleState(newWorkerRow(staff), true, true)).toBe('hidden');
  });
});

describe('leaderToSend', () => {
  it('sends only a change', () => {
    expect(leaderToSend(false, true)).toBe(true);
    expect(leaderToSend(true, false)).toBe(false);
    expect(leaderToSend(true, true)).toBeNull();
    expect(leaderToSend(false, false)).toBeNull();
  });

  it('no login (null) counts as off: untouched sends null', () => {
    expect(leaderToSend(null, false)).toBeNull();
    expect(leaderToSend(null, true)).toBe(true);
  });

  it('no current value sends null', () => {
    expect(leaderToSend(true, null)).toBeNull();
    expect(leaderToSend(null, null)).toBeNull();
  });
});

describe('leaderChoice', () => {
  it('an enabled toggle is a choice', () => {
    expect(leaderChoice('enabled', 'enabled', true)).toBe(true);
    expect(leaderChoice('enabled', 'disabled', false)).toBe(false);
  });

  it('a hidden toggle is no choice', () => {
    expect(leaderChoice('hidden', 'hidden', false)).toBeNull();
  });

  it('access already off at open and still off: unchanged, so an unrelated save keeps the Leder', () => {
    expect(leaderChoice('disabled', 'disabled', false)).toBeNull();
    expect(leaderToSend(true, leaderChoice('disabled', 'disabled', false))).toBeNull();
  });

  it('this edit switched Web and Arkiv off: false, so the API revokes', () => {
    expect(leaderChoice('disabled', 'enabled', false)).toBe(false);
    expect(leaderToSend(true, leaderChoice('disabled', 'enabled', false))).toBe(false);
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
    expect(newWorkerRow(leder)).toEqual({
      canSetLeder: false, canEditWorkerTags: true, isLeader: false, loginIsManaged: true, isAdminLogin: false, tags: [],
    });
  });
});
