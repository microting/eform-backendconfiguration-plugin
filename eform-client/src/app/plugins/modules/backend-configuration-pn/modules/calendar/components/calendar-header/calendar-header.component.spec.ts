import {SimpleChange} from '@angular/core';
import {TranslateService} from '@ngx-translate/core';
import {CalendarHeaderComponent} from './calendar-header.component';

// Class-level instantiation (matching the module's other specs, e.g.
// calendar-layout.service.spec.ts): the assertion is on the options array
// produced by buildViewModeOptions, which needs no Angular TestBed.
function makeTranslate(): TranslateService {
  // instant passthrough — the label is the key itself, which is all the
  // option-list assertions rely on.
  return {instant: (key: string) => key} as unknown as TranslateService;
}

describe('CalendarHeaderComponent', () => {
  let component: CalendarHeaderComponent;

  beforeEach(() => {
    component = new CalendarHeaderComponent(makeTranslate());
  });

  it('hides the Compliance option from non-admin users', () => {
    // ngOnInit builds the option list; isAdmin defaults to false.
    component.ngOnInit();

    const values = component.viewModeOptions.map(o => o.value);
    // Month is available to everyone, exactly like day/week/schedule —
    // only Compliance is admin-gated.
    expect(values).toEqual(['day', 'week', 'month', 'schedule']);
  });

  it('offers the Compliance option once isAdmin arrives from the store', () => {
    component.ngOnInit();

    // isAdmin is delivered asynchronously by the store after init, arriving
    // as an @Input change — ngOnChanges must rebuild the option list.
    component.isAdmin = true;
    component.ngOnChanges({isAdmin: new SimpleChange(false, true, false)});

    const values = component.viewModeOptions.map(o => o.value);
    expect(values).toEqual(['day', 'week', 'month', 'schedule', 'compliance']);
  });
});
