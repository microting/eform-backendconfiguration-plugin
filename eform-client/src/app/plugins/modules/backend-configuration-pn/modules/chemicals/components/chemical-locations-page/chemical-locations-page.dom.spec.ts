import {describe, it, expect, afterEach, vi} from 'vitest';
import {CdkDrag, CdkDropList, DragDropModule} from '@angular/cdk/drag-drop';
import {Overlay} from '@angular/cdk/overlay';
import {NO_ERRORS_SCHEMA, Pipe, PipeTransform} from '@angular/core';
import {ComponentFixture, TestBed} from '@angular/core/testing';
import {FormsModule} from '@angular/forms';
import {MatCheckboxModule} from '@angular/material/checkbox';
import {MatDialog} from '@angular/material/dialog';
import {By} from '@angular/platform-browser';
import {NoopAnimationsModule} from '@angular/platform-browser/animations';
import {Store} from '@ngrx/store';
import {AuthImagePipe} from 'src/app/common/pipes';
import {TranslateModule} from '@ngx-translate/core';
import {BehaviorSubject, Observable, Subject, of} from 'rxjs';
import {BackendConfigurationPnChemicalsService} from '../../../../services';
import {inventoryOf, location, placement} from '../../helpers/chemical-inventory.fixtures';
import {ChemicalLocationsPageComponent} from './chemical-locations-page.component';
import {detectChanges} from '../../../../testing/detect-changes';

/** The host's authImage pipe needs the auth store and HttpClient; the template only needs a URL back. */
@Pipe({name: 'authImage'})
class FakeAuthImagePipe implements PipeTransform {
  transform(src: string): Promise<string> {
    return Promise.resolve(src);
  }
}

/** Template-level checks for W3: the ids the e2e specs use, drag and archive rules, load states. */
/** Lets pending promises and zero-delay timers (the authImage pipe, the focus restore) run. */
const flush = (): Promise<void> => new Promise((resolve) => setTimeout(resolve));

describe('ChemicalLocationsPageComponent (template)', () => {
  let fixture: ComponentFixture<ChemicalLocationsPageComponent>;
  let component: ChemicalLocationsPageComponent;
  const find = (selector: string): HTMLElement | null => fixture.nativeElement.querySelector(selector);
  const disabled = (selector: string) => (find(selector) as HTMLButtonElement).disabled;

  let reorder: Subject<unknown>;

  function render(answer?: Observable<unknown>): void {
    reorder = new Subject<unknown>();
    const inventory = inventoryOf({
      propertyId: 3,
      locations: [
        location(1, 'Lade', {propertyId: 3, sortOrder: 1}),
        location(2, 'Kemirum', {propertyId: 3, sortOrder: 2, photoFileName: 'p.jpg'}),
        location(3, 'Gammel', {propertyId: 3, sortOrder: 3, archived: true}),
      ],
      placements: [placement(9, 2, 100, {propertyId: 3})],
    });
    TestBed.configureTestingModule({
      imports: [ChemicalLocationsPageComponent, FormsModule, DragDropModule, MatCheckboxModule, NoopAnimationsModule, TranslateModule.forRoot()],
      providers: [
        {provide: Store, useValue: {select: vi.fn().mockReturnValue(new BehaviorSubject<number | null>(3))}},
        {provide: BackendConfigurationPnChemicalsService, useValue: {
          getPropertyInventory: vi.fn().mockReturnValue(answer ?? of({success: true, model: inventory})),
          locationPhotoUrl: (id: number) => `/photo/${id}`,
          reorderLocations: vi.fn().mockImplementation(() => reorder),
        }},
        {provide: MatDialog, useValue: {open: vi.fn()}},
        {provide: Overlay, useValue: {scrollStrategies: {reposition: vi.fn().mockReturnValue({})}}},
      ],
      schemas: [NO_ERRORS_SCHEMA],
    });
    TestBed.overrideComponent(ChemicalLocationsPageComponent, {remove: {imports: [AuthImagePipe]}, add: {imports: [FakeAuthImagePipe]}});
    fixture = TestBed.createComponent(ChemicalLocationsPageComponent);
    component = fixture.componentInstance;
    detectChanges(fixture);
  }

  afterEach(() => fixture?.destroy());

  it('renders every control the e2e page object addresses', () => {
    render();

    for (const id of ['#chemicalLocationsPage', '#chemicalLocationsNewBtn', '#chemicalLocationsShowArchived', '#chemicalLocationsList',
      '#chemicalLocationRow-1', '#chemicalLocationDrag-1', '#chemicalLocationEdit-1', '#chemicalLocationArchive-1',
      '#chemicalLocationUp-1', '#chemicalLocationDown-1']) {
      expect(find(id)).not.toBeNull();
    }
    expect(find('#chemicalLocationRow-3')).toBeNull();
  });

  it('numbers the active rows in their order and shows the open placements', () => {
    render();

    expect(find('#chemicalLocationRow-1 .col-no')?.textContent?.trim()).toBe('1');
    expect(find('#chemicalLocationRow-2 .col-no')?.textContent?.trim()).toBe('2');
    expect(find('#chemicalLocationRow-2 .col-count')?.textContent?.trim()).toBe('1');
  });

  it('blocks the archive button while a location still holds placements', () => {
    render();

    expect(disabled('#chemicalLocationArchive-1')).toBe(false);
    expect(disabled('#chemicalLocationArchive-2')).toBe(true);
  });

  it('shows archived rows on request, undraggable and without edit or archive', () => {
    render();
    component.showArchived = true;
    detectChanges(fixture);

    expect(find('#chemicalLocationRow-3')?.classList).toContain('chemicals-location-row--archived');
    expect(disabled('#chemicalLocationEdit-3')).toBe(true);
    expect(disabled('#chemicalLocationArchive-3')).toBe(true);
    const drags = fixture.debugElement.queryAll(By.directive(CdkDrag)).map((d) => d.injector.get(CdkDrag));
    expect(drags.map((d) => d.disabled)).toEqual([false, false, true]);
    expect(find('#chemicalLocationDrag-3')).toBeNull();
  });

  it('stops dragging while a new order is being saved', () => {
    render();
    const list = fixture.debugElement.query(By.directive(CdkDropList)).injector.get(CdkDropList);
    expect(list.disabled).toBe(false);
    expect(list.sortPredicate).toBe(component.canSortTo);

    component.reordering = true;
    detectChanges(fixture);

    expect(list.disabled).toBe(true);
  });

  it('names the icon-only buttons for assistive technology; the drag handle is decorative (I2)', () => {
    render();

    expect(find('#chemicalLocationEdit-1')?.getAttribute('aria-label')).toBe('Chemicals: Edit location');
    expect(find('#chemicalLocationArchive-1')?.getAttribute('aria-label')).toBe('Chemicals: Archive');
    expect(find('#chemicalLocationUp-2')?.getAttribute('aria-label')).toBe('Chemicals: Move location up');
    expect(find('#chemicalLocationDown-1')?.getAttribute('aria-label')).toBe('Chemicals: Move location down');
    expect(find('#chemicalLocationDrag-1')?.getAttribute('aria-hidden')).toBe('true');
    expect(find('#chemicalLocationDrag-1')?.hasAttribute('aria-label')).toBe(false);
  });

  it('offers up and down buttons on active rows, disabled at the ends and while an order is saved (I2)', () => {
    render();
    component.showArchived = true;
    detectChanges(fixture);

    expect(disabled('#chemicalLocationUp-1')).toBe(true);
    expect(disabled('#chemicalLocationDown-1')).toBe(false);
    expect(disabled('#chemicalLocationUp-2')).toBe(false);
    expect(disabled('#chemicalLocationDown-2')).toBe(true);
    expect(find('#chemicalLocationUp-3')).toBeNull();
    expect(find('#chemicalLocationDown-3')).toBeNull();

    component.reordering = true;
    detectChanges(fixture);

    // aria-disabled, not disabled, while an order saves: a disabled button would drop the keyboard focus (N-I1).
    expect(find('#chemicalLocationDown-1')?.getAttribute('aria-disabled')).toBe('true');
    expect(find('#chemicalLocationUp-2')?.getAttribute('aria-disabled')).toBe('true');
    expect(disabled('#chemicalLocationUp-2')).toBe(false);
  });

  it.each([
    ['saved', {success: true, model: []}],
    ['refused', {success: false, message: 'Nej'}],
  ])('keeps the keyboard focus on the moved row after an up press (%s) (N-I1)', async (_name: string, answer: unknown) => {
    render();
    const up = find('#chemicalLocationUp-2') as HTMLButtonElement;
    up.focus();

    up.click();
    detectChanges(fixture);
    reorder.next(answer);
    reorder.complete();
    detectChanges(fixture);
    await flush();
    detectChanges(fixture);

    // Kemirum is first after the save (or back at its place after a refusal): focus is on one of its move buttons.
    expect(['chemicalLocationUp-2', 'chemicalLocationDown-2']).toContain((document.activeElement as HTMLElement).id);
  });

  it('keeps the row elements across a saved order (rows are tracked by location id)', async () => {
    render();
    const before = find('#chemicalLocationRow-2');

    (find('#chemicalLocationUp-2') as HTMLButtonElement).click();
    reorder.next({success: true, model: [location(2, 'Kemirum', {propertyId: 3, sortOrder: 1, photoFileName: 'p.jpg'})]});
    reorder.complete();
    detectChanges(fixture);
    await flush();

    expect(find('#chemicalLocationRow-2')).toBe(before);
  });

  it('shows a loading state, not an empty list, while the locations load', () => {
    render(new Subject());

    expect(find('#chemicalLocationsLoading')).not.toBeNull();
    expect(find('#chemicalLocationsEmpty')).toBeNull();
    expect(disabled('#chemicalLocationsNewBtn')).toBe(true);
  });

  it('says when the locations could not be loaded', () => {
    render(of({success: false, message: 'Ingen adgang'}));

    expect(find('#chemicalLocationsLoadFailed')?.getAttribute('role')).toBe('alert');
    expect(find('#chemicalLocationsEmpty')).toBeNull();
  });

  it('invites the first location on a property without any', () => {
    render(of({success: true, model: inventoryOf({propertyId: 3})}));

    expect(find('#chemicalLocationsEmpty')?.textContent).toContain('Chemicals: No locations');
    expect(disabled('#chemicalLocationsNewBtn')).toBe(false);
  });
});
