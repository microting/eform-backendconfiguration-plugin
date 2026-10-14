import {
  NO_CHEMICAL_FLAGS,
  buildPermissionRows,
  changedPermissions,
  copyPermissionsFrom,
  effectiveFlags,
  isImpliedFlag,
  resetPermissionRows,
  sameFlags,
  toggleFlag,
} from './chemical-permissions.helper';

const flags = (partial: Partial<typeof NO_CHEMICAL_FLAGS>) => ({...NO_CHEMICAL_FLAGS, ...partial});

describe('chemical permissions helper', () => {
  it('Admin implies every flag (spec §8) without changing the stored flags', () => {
    const stored = flags({admin: true, view: true});

    expect(effectiveFlags(stored)).toEqual({view: true, register: true, remove: true, stock: true, manageLocations: true, admin: true});
    expect(stored.register).toBe(false);
  });

  it('marks every non-Admin flag as implied while Admin is set', () => {
    const admin = flags({admin: true});

    expect(isImpliedFlag(admin, 'stock')).toBe(true);
    expect(isImpliedFlag(admin, 'admin')).toBe(false);
    expect(isImpliedFlag(flags({stock: true}), 'stock')).toBe(false);
  });

  it('toggles a free flag and refuses to toggle an implied one', () => {
    expect(toggleFlag(flags({}), 'register').register).toBe(true);
    expect(toggleFlag(flags({register: true}), 'register').register).toBe(false);
    const admin = flags({admin: true});
    expect(toggleFlag(admin, 'register')).toBe(admin);
  });

  it('removing Admin restores the flags that were set on their own', () => {
    const withAdmin = toggleFlag(flags({view: true}), 'admin');
    const withoutAdmin = toggleFlag(withAdmin, 'admin');

    expect(effectiveFlags(withoutAdmin)).toEqual(flags({view: true}));
  });

  it('builds rows sorted by name with current equal to stored', () => {
    const rows = buildPermissionRows([
      {workerId: 2, workerName: 'Ole Nielsen', flags: flags({view: true})},
      {workerId: 1, workerName: 'Jens Hansen', flags: flags({admin: true})},
    ]);

    expect(rows.map((r) => r.workerName)).toEqual(['Jens Hansen', 'Ole Nielsen']);
    expect(changedPermissions(rows)).toEqual([]);
    expect(rows[0].current).not.toBe(rows[0].stored);
  });

  it('reports only workers whose EFFECTIVE flags changed', () => {
    const rows = buildPermissionRows([
      {workerId: 1, workerName: 'A', flags: flags({view: true})},
      {workerId: 2, workerName: 'B', flags: flags({})},
    ]);
    rows[0].current = toggleFlag(rows[0].current, 'register');

    expect(changedPermissions(rows)).toEqual([{workerId: 1, flags: flags({view: true, register: true})}]);
  });

  it('sends the stored-with-Admin flags, never the expanded ones', () => {
    const rows = buildPermissionRows([{workerId: 1, workerName: 'A', flags: flags({view: true})}]);
    rows[0].current = toggleFlag(rows[0].current, 'admin');

    expect(changedPermissions(rows)).toEqual([{workerId: 1, flags: flags({view: true, admin: true})}]);
  });

  it('copies flags only for workers present on both properties and leaves them unsaved', () => {
    const rows = buildPermissionRows([
      {workerId: 1, workerName: 'A', flags: flags({})},
      {workerId: 2, workerName: 'B', flags: flags({view: true})},
    ]);

    const copied = copyPermissionsFrom(rows, [
      {workerId: 1, workerName: 'A', flags: flags({view: true, stock: true})},
      {workerId: 99, workerName: 'Elsewhere', flags: flags({admin: true})},
    ]);

    expect(copied.map((r) => r.workerId)).toEqual([1, 2]);
    expect(copied[0].current).toEqual(flags({view: true, stock: true}));
    expect(copied[1].current).toEqual(flags({view: true}));
    expect(changedPermissions(copied).map((c) => c.workerId)).toEqual([1]);
  });

  it('undo resets every row to its stored flags', () => {
    const rows = buildPermissionRows([{workerId: 1, workerName: 'A', flags: flags({})}]);
    rows[0].current = flags({admin: true});

    expect(changedPermissions(resetPermissionRows(rows))).toEqual([]);
  });

  it('compares flags field by field', () => {
    expect(sameFlags(flags({view: true}), flags({view: true}))).toBe(true);
    expect(sameFlags(flags({view: true}), flags({}))).toBe(false);
  });
});
