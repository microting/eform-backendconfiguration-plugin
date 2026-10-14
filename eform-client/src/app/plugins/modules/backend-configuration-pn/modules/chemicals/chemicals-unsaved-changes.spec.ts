import {ActivatedRouteSnapshot, RouterStateSnapshot} from '@angular/router';
import {Observable, of} from 'rxjs';
import {chemicalUnsavedChangesGuard, confirmDiscardOf} from './chemicals-unsaved-changes';

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
    const screen = {confirmDiscardChanges: jest.fn().mockReturnValue(of(false))};

    expect(answer(runGuard(screen))).toEqual([false]);
    expect(screen.confirmDiscardChanges).toHaveBeenCalledTimes(1);
  });
});
