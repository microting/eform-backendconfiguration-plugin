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
import {chemicalsSelectProperty} from '../../../../state';
import {ChemicalsContainerComponent} from './chemicals-container.component';

/**
 * Rendered-level checks for the property picker: what the select shows and whether it is
 * usable, not just the container's fields. A real mtx-select is used because the bug these
 * guard against (a picker stuck on the user's pick) only exists in how ngModel feeds it.
 */
describe('ChemicalsContainerComponent (picker)', () => {
  let fixture: ComponentFixture<ChemicalsContainerComponent>;
  let component: ChemicalsContainerComponent;
  let selectedProperty$: BehaviorSubject<number | null>;
  const list = [{id: 7, name: 'Hovedgaarden', description: ''}, {id: 8, name: 'Engvej 12', description: ''}];

  const shown = (): string =>
    (fixture.debugElement.query(By.css('#chemicalsPropertySelect .ng-value-label'))?.nativeElement as HTMLElement)
      ?.textContent?.trim() ?? '';
  const disabled = (): boolean => !!fixture.debugElement.query(By.css('#chemicalsPropertySelect .ng-select-disabled'));

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
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

  beforeEach(async () => {
    selectedProperty$ = new BehaviorSubject<number | null>(null);
    const store = {
      select: jest.fn().mockImplementation((selector) =>
        selector === selectCurrentUserIsFirstUser ? new BehaviorSubject(true) : selectedProperty$),
      dispatch: jest.fn().mockImplementation((action: {type: string; payload: number | null}) => {
        if (action.type === chemicalsSelectProperty.type) {
          selectedProperty$.next(action.payload);
        }
      }),
    };
    await TestBed.configureTestingModule({
      declarations: [ChemicalsContainerComponent],
      imports: [FormsModule, NoopAnimationsModule, MtxSelectModule, TranslateModule.forRoot()],
      providers: [
        {provide: Store, useValue: store},
        {provide: BackendConfigurationPnPropertiesService, useValue: {
          getAllPropertiesDictionary: () => of({success: true, model: list}),
        }},
        {provide: Router, useValue: {navigate: jest.fn().mockResolvedValue(true), events: new Subject()}},
        {provide: ActivatedRoute, useValue: {snapshot: {queryParamMap: convertToParamMap({})}, firstChild: null}},
      ],
      schemas: [NO_ERRORS_SCHEMA],
    }).compileComponents();
    fixture = TestBed.createComponent(ChemicalsContainerComponent);
    component = fixture.componentInstance;
    await settle();
  });

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
});
