import { ChangeDetectorRef } from '@angular/core';
import { ComponentFixture } from '@angular/core/testing';

/**
 * `fixture.detectChanges()` for specs that change component or service state
 * directly between checks. `ng test` runs zoneless, so a check only refreshes
 * views marked dirty, while its no-changes pass looks at every view and throws
 * NG0100 on the state change it skipped. Marking the component first gives the
 * refresh the app gets from zone.js after an event.
 */
export function detectChanges<T>(fixture: ComponentFixture<T>): void {
  fixture.componentRef.injector.get(ChangeDetectorRef).markForCheck();
  fixture.detectChanges();
}
