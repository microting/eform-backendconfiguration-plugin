import {describe, it, expect, afterEach, vi} from 'vitest';
import type {Mock} from 'vitest';
import {ComponentFixture, TestBed} from '@angular/core/testing';
import {Store, StoreModule, combineReducers} from '@ngrx/store';
import {TranslateService} from '@ngx-translate/core';
import {Subject, take} from 'rxjs';
import {BackendConfigurationPnPropertiesService} from '../../../../services';
import {FilesFiltrationModel, filesReducer, filesSelectFolder, selectFilesFilters} from '../../../../state';
import {FilesStateService} from '../../store';
import {FilesFiltersComponent} from './files-filters.component';

describe('FilesFiltersComponent — search debounce', () => {
  let fixture: ComponentFixture<FilesFiltersComponent>;
  let component: FilesFiltersComponent;
  let store: Store;
  let properties$: Subject<unknown>;
  let updateTable: Mock;

  const filters = (): FilesFiltrationModel => {
    let current!: FilesFiltrationModel;
    store.select(selectFilesFilters).pipe(take(1)).subscribe(f => (current = f));
    return current;
  };

  /** The property list arrives after the form exists and cleans its selection: that first change is skipped by design. */
  function setUp(): void {
    vi.useFakeTimers();
    properties$ = new Subject();
    TestBed.configureTestingModule({
      imports: [FilesFiltersComponent, StoreModule.forRoot({backendConfigurationPn: combineReducers({filesState: filesReducer}) as any})],
      providers: [
        {provide: BackendConfigurationPnPropertiesService, useValue: {getAllPropertiesDictionary: () => properties$}},
        {provide: FilesStateService, useValue: {}},
        {provide: TranslateService, useValue: {}},
      ],
    });
    // Only the debounce logic is under test, not the Material form fields.
    TestBed.overrideTemplate(FilesFiltersComponent, '');
    fixture = TestBed.createComponent(FilesFiltersComponent);
    component = fixture.componentInstance;
    store = TestBed.inject(Store);
    updateTable = vi.fn();
    component.updateTable.subscribe(updateTable);
    fixture.detectChanges();
    properties$.next({success: true, model: []});
    vi.advanceTimersByTime(500);
  }

  afterEach(() => {
    fixture?.destroy();
    vi.useRealTimers();
  });

  it('applies a search once the typing pauses', () => {
    setUp();
    component.nameFilterControl.setValue('faktura');
    vi.advanceTimersByTime(500);
    expect(filters().nameFilter).toBe('faktura');
    expect(updateTable).toHaveBeenCalledTimes(1);
  });

  it('a folder selected while the search is still debouncing wins: the stale text is not applied', () => {
    setUp();
    component.nameFilterControl.setValue('faktura');
    vi.advanceTimersByTime(300);
    store.dispatch(filesSelectFolder(4));
    vi.advanceTimersByTime(500);
    expect(filters().folderId).toBe(4);
    expect(filters().nameFilter).toBe('');
    expect(component.nameFilterControl.value).toBe('');
    expect(updateTable).not.toHaveBeenCalled();
  });

  it('re-clicking the folder already selected also drops a search that is still debouncing', () => {
    setUp();
    store.dispatch(filesSelectFolder(4));
    component.nameFilterControl.setValue('faktura');
    vi.advanceTimersByTime(300);
    store.dispatch(filesSelectFolder(4)); // same folder: the stored filters do not change at all
    vi.advanceTimersByTime(500);
    expect(filters().folderId).toBe(4);
    expect(filters().nameFilter).toBe('');
    expect(component.nameFilterControl.value).toBe('');
    expect(updateTable).not.toHaveBeenCalled();
  });
});
