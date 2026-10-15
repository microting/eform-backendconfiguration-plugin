import {CanDeactivateFn} from '@angular/router';
import {Observable, of} from 'rxjs';

/** A Kemi screen that can hold unsaved edits asks before they are dropped. */
export interface ChemicalUnsavedChangesAware {
  /** Emits once: true when the screen may be left (nothing unsaved, or the user chose to discard). */
  confirmDiscardChanges(): Observable<boolean>;
  /** True while a request the screen cannot cancel is in flight: the container locks the property picker. */
  readonly busy?: boolean;
}

/** Asks `screen` when it holds unsaved edits; any other screen (or none) may be left at once. */
export function confirmDiscardOf(screen: unknown): Observable<boolean> {
  const aware = screen as Partial<ChemicalUnsavedChangesAware> | null;
  return typeof aware?.confirmDiscardChanges === 'function' ? aware.confirmDiscardChanges() : of(true);
}

/** On every Kemi screen route: leaving a screen with unsaved edits asks first (menu links, Back, other plugins). */
export const chemicalUnsavedChangesGuard: CanDeactivateFn<unknown> = (screen) => confirmDiscardOf(screen);

/**
 * The leave/switch answer of a screen with unsaved edits: a request already issued cannot be cancelled,
 * so "discard" would race its commit and the user stays (no dialog); nothing unsaved lets go at once;
 * otherwise `ask` opens the discard confirm.
 */
export function confirmDiscardWhenDirty(busy: boolean, dirty: boolean, ask: () => Observable<boolean>): Observable<boolean> {
  if (busy) {
    return of(false);
  }
  return dirty ? ask() : of(true);
}

/** Reload or tab close with unsaved edits: the browser shows its own "leave site?" prompt. */
export function warnBeforeUnload(event: BeforeUnloadEvent, dirty: boolean): void {
  if (dirty) {
    event.preventDefault();
    event.returnValue = '';
  }
}
