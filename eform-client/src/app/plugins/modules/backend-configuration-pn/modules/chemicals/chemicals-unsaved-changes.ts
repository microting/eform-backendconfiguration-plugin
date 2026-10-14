import {CanDeactivateFn} from '@angular/router';
import {Observable, of} from 'rxjs';

/** A Kemi screen that can hold unsaved edits asks before they are dropped. */
export interface ChemicalUnsavedChangesAware {
  /** Emits once: true when the screen may be left (nothing unsaved, or the user chose to discard). */
  confirmDiscardChanges(): Observable<boolean>;
}

/** Asks `screen` when it holds unsaved edits; any other screen (or none) may be left at once. */
export function confirmDiscardOf(screen: unknown): Observable<boolean> {
  const aware = screen as Partial<ChemicalUnsavedChangesAware> | null;
  return typeof aware?.confirmDiscardChanges === 'function' ? aware.confirmDiscardChanges() : of(true);
}

/** On every Kemi screen route: leaving a screen with unsaved edits asks first (menu links, Back, other plugins). */
export const chemicalUnsavedChangesGuard: CanDeactivateFn<unknown> = (screen) => confirmDiscardOf(screen);
