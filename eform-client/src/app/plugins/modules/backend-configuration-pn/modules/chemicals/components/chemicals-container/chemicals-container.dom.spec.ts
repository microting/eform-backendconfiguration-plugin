import {describe, it, expect, beforeEach, afterEach, vi} from 'vitest';
import {NO_ERRORS_SCHEMA} from '@angular/core';
import {ComponentFixture, TestBed} from '@angular/core/testing';
import {FormsModule} from '@angular/forms';
import {By} from '@angular/platform-browser';
import {NoopAnimationsModule} from '@angular/platform-browser/animations';
import {ActivatedRoute, convertToParamMap, Router} from '@angular/router';
import {MtxSelectModule} from '@ng-matero/extensions/select';
import {Store} from '@ngrx/store';
import {TranslateModule} from '@ngx-translate/core';
import {BehaviorSubject, of, Subject} from 'rxjs';
import {selectCurrentUserIsFirstUser} from 'src/app/state';
import {BackendConfigurationPnPropertiesService} from '../../../../services';
import {ChemicalsScope, chemicalsSelectAllProperties, chemicalsSelectProperty} from '../../../../state';
import {ChemicalsContainerComponent} from './chemicals-container.component';
import {detectChanges} from '../../../../testing/detect-changes';
import {CommonModule} from '@angular/common';

/**
 * Rendered-level checks for the property picker: what the select shows and whether it is
 * usable, not just the container's fields. A real mtx-select is used because the bug these
 * guard against (a picker stuck on the user's pick) only exists in how ngModel feeds it.
 */
describe('ChemicalsContainerComponent (picker)', () => {
  let fixture: ComponentFixture<ChemicalsContainerComponent>;
  let component: ChemicalsContainerComponent;
  let scope$: BehaviorSubject<ChemicalsScope>;
  const list = [{id: 7, name: 'Hovedgaarden', description: ''}, {id: 8, name: 'Engvej 12', description: ''}];

  const shown = (): string =>
    (fixture.debugElement.query(By.css('#chemicalsPropertySelect .ng-value-label'))?.nativeElement as HTMLElement)
      ?.textContent?.trim() ?? '';
  const disabled = (): boolean => !!fixture.debugElement.query(By.css('#chemicalsPropertySelect.mtx-select-disabled'));

  async function settle(): Promise<void> {
    // ngModel hands a value over in a microtask, and mtx-select hands it on to ng-select the same way.
    // The container puts a refused pick back in a setTimeout, which `ng test` (zoneless) does not
    // wait for, so each pass also lets one macrotask run.
    for (let pass = 0; pass < 3; pass++) {
      detectChanges(fixture);
      await fixture.whenStable();
      await new Promise((resolve) => setTimeout(resolve));
    }
  }

  /** What a user does: open the select and click an option, so the select holds the pick itself. */
  async function pick(name: string): Promise<void> {
    const container = fixture.debugElement.query(By.css('#chemicalsPropertySelect .ng-select-container'));
    container.nativeElement.dispatchEvent(new MouseEvent('mousedown', {bubbles: true}));
    await settle();
    const option = Array.from(document.querySelectorAll('.ng-option'))
      .find((el) => el.textContent?.includes(name)) as HTMLElement;
    option.click();
    await settle();
  }

  /** `allowsAll`: the routed screen offers "All" (Beholdning, Historik). */
  async function setUp(allowsAll: boolean): Promise<void> {
    scope$ = new BehaviorSubject<ChemicalsScope>({propertyId: null, allProperties: false});
    const store = {
      select: vi.fn().mockImplementation((selector) =>
        selector === selectCurrentUserIsFirstUser ? new BehaviorSubject(true) : scope$),
      dispatch: vi.fn().mockImplementation((action: {type: string; payload: number | null}) => {
        if (action.type === chemicalsSelectProperty.type) {
          scope$.next({propertyId: action.payload, allProperties: false});
        } else if (action.type === chemicalsSelectAllProperties.type) {
          scope$.next({propertyId: null, allProperties: true});
        }
      }),
    };
    await TestBed.configureTestingModule({
      imports: [ChemicalsContainerComponent, FormsModule, NoopAnimationsModule, MtxSelectModule, TranslateModule.forRoot()],
      providers: [
        {provide: Store, useValue: store},
        {provide: BackendConfigurationPnPropertiesService, useValue: {
          getAllPropertiesDictionary: () => of({success: true, model: list}),
        }},
        {provide: Router, useValue: {
          navigate: vi.fn().mockResolvedValue(true), events: new Subject(), currentNavigation: vi.fn().mockReturnValue(null),
        }},
        {provide: ActivatedRoute, useValue: {
          snapshot: {queryParamMap: convertToParamMap({})},
          firstChild: allowsAll ? {snapshot: {data: {allProperties: true}}} : null,
        }},
      ],
      schemas: [NO_ERRORS_SCHEMA],
    })
      // Keep the scope these specs were written for (the old TestBed module), not the
      // component's own Material imports.
      .overrideComponent(ChemicalsContainerComponent, {set: {imports: [CommonModule, FormsModule, MtxSelectModule, TranslateModule], schemas: [NO_ERRORS_SCHEMA]}}).compileComponents();
    fixture = TestBed.createComponent(ChemicalsContainerComponent);
    component = fixture.componentInstance;
    await settle();
  }

  beforeEach(() => setUp(false));

  afterEach(() => fixture?.destroy());

  it('starts on the first property', () => {
    expect(shown()).toBe('Hovedgaarden');
    expect(disabled()).toBe(false);
  });

  it('disables the picker while the active screen is busy', async () => {
    const screen = {busy: true, confirmDiscardChanges: () => of(true)};
    component.onScreenActivate(screen);
    await settle();
    expect(disabled()).toBe(true);

    screen.busy = false;
    await settle();
    expect(disabled()).toBe(false);
  });

  it('shows the old property again when the screen refuses the switch at once', async () => {
    component.onScreenActivate({confirmDiscardChanges: () => of(false)});

    await pick('Engvej 12');
    await settle();

    expect(component.propertyId).toBe(7);
    expect(shown()).toBe('Hovedgaarden');
  });
  it('offers no "All" on a screen without it', async () => {
    const container = fixture.debugElement.query(By.css('#chemicalsPropertySelect .ng-select-container'));
    container.nativeElement.dispatchEvent(new MouseEvent('mousedown', {bubbles: true}));
    await settle();

    const options = Array.from(document.querySelectorAll('.ng-option')).map((el) => el.textContent?.trim());
    expect(options).toEqual(['Hovedgaarden', 'Engvej 12']);
  });

  describe('on Beholdning and Historik', () => {
    beforeEach(async () => {
      fixture?.destroy();
      TestBed.resetTestingModule();
      await setUp(true);
    });

    it('starts on "All", listed first, and switches to a picked property', async () => {
      expect(shown()).toBe('Chemicals: All');

      await pick('Engvej 12');

      expect(component.propertyId).toBe(8);
      expect(component.allProperties).toBe(false);
      expect(shown()).toBe('Engvej 12');

      await pick('Chemicals: All');

      expect(component.allProperties).toBe(true);
      expect(shown()).toBe('Chemicals: All');
    });
  });
});
