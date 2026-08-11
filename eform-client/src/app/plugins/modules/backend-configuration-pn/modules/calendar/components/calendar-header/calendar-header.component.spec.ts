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

  it('offers all five view-mode options to every user', () => {
    // ngOnInit builds the option list.
    component.ngOnInit();

    const values = component.viewModeOptions.map(o => o.value);
    // Month and Compliance are available to everyone, exactly like
    // day/week/schedule — no admin gating.
    expect(values).toEqual(['day', 'week', 'month', 'schedule', 'compliance']);
  });
});
