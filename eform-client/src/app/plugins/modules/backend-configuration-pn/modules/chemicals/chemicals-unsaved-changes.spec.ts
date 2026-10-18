import {describe, it, expect, vi} from 'vitest';
import {ActivatedRouteSnapshot, RouterStateSnapshot} from '@angular/router';
import {Observable, of} from 'rxjs';
import {chemicalUnsavedChangesGuard, confirmDiscardOf, confirmDiscardWhenDirty, warnBeforeUnload} from './chemicals-unsaved-changes';

describe('Kemi unsaved-changes guard (I-3)', () => {
  const answer = (value: unknown): boolean[] => {
    const answers: boolean[] = [];
    (value as Observable<boolean>).subscribe((ok) => answers.push(ok));
    return answers;
  };
  const runGuard = (component: unknown) =>
    chemicalUnsavedChangesGuard(component, {} as ActivatedRouteSnapshot, {} as RouterStateSnapshot, {} as RouterStateSnapshot);

  it('lets a screen without unsaved-changes support go at once', () => {
    expect(answer(confirmDiscardOf({}))).toEqual([true]);
    expect(answer(confirmDiscardOf(null))).toEqual([true]);
  });

  it('asks a screen that can hold unsaved changes', () => {
    const screen = {confirmDiscardChanges: vi.fn().mockReturnValue(of(false))};

    expect(answer(runGuard(screen))).toEqual([false]);
    expect(screen.confirmDiscardChanges).toHaveBeenCalledTimes(1);
  });

  describe('confirmDiscardWhenDirty', () => {
    it('keeps the user on the page while a request is in flight, without asking', () => {
      const ask = vi.fn().mockReturnValue(of(true));

      expect(answer(confirmDiscardWhenDirty(true, true, ask))).toEqual([false]);
      expect(ask).not.toHaveBeenCalled();
    });

    it('lets go at once when nothing is unsaved', () => {
      const ask = vi.fn().mockReturnValue(of(false));

      expect(answer(confirmDiscardWhenDirty(false, false, ask))).toEqual([true]);
      expect(ask).not.toHaveBeenCalled();
    });

    it('asks when there are unsaved edits and returns the answer', () => {
      expect(answer(confirmDiscardWhenDirty(false, true, () => of(false)))).toEqual([false]);
      expect(answer(confirmDiscardWhenDirty(false, true, () => of(true)))).toEqual([true]);
    });
  });

  describe('warnBeforeUnload', () => {
    const eventOf = () => ({preventDefault: vi.fn(), returnValue: undefined as unknown});

    it('asks the browser to confirm only while edits are unsaved', () => {
      const clean = eventOf();
      warnBeforeUnload(clean as unknown as BeforeUnloadEvent, false);
      expect(clean.preventDefault).not.toHaveBeenCalled();

      const dirty = eventOf();
      warnBeforeUnload(dirty as unknown as BeforeUnloadEvent, true);
      expect(dirty.preventDefault).toHaveBeenCalled();
      expect(dirty.returnValue).toBe('');
    });
  });
});
