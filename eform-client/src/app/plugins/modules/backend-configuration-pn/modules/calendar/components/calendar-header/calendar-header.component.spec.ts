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

  it('offers exactly the four calendar view modes', () => {
    // ngOnInit builds the option list.
    component.ngOnInit();

    const values = component.viewModeOptions.map(o => o.value);
    // The list is unconditional: every option is available to every user, and
    // the component no longer takes an isAdmin input to gate one with (#1170 —
    // compliance moved to its own page and is no longer a calendar view mode).
    expect(values).toEqual(['day', 'week', 'month', 'schedule']);
  });
});
