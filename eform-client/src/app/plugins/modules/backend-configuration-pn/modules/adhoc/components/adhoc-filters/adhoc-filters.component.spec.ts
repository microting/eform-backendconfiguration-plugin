import {of} from 'rxjs';
import {AdhocFiltersComponent} from './adhoc-filters.component';
import {adhocInitialState} from '../../../../state';

/**
 * Spy-service unit test for `AdhocFiltersComponent` (M5/F6). Authored, not
 * run - jest runs from the host frontend only (repo convention).
 */
describe('AdhocFiltersComponent', () => {
  let component: AdhocFiltersComponent;
  let adhocStateServiceSpy: any;
  let adhocServiceSpy: any;
  let translateSpy: any;
  let elementRefSpy: any;
  let dialogSpy: any;
  let overlaySpy: any;

  beforeEach(() => {
    adhocStateServiceSpy = {
      currentFilters: {...adhocInitialState.filters},
      properties: [{id: 1, name: 'Gård Nord'}],
      tags: [
        {id: 10, name: 'Vedligehold', isUserTag: false},
        {id: 11, name: 'Sikkerhed', isUserTag: false},
      ],
      updateFilters: jest.fn().mockImplementation((partial: any) => {
        adhocStateServiceSpy.currentFilters = {...adhocStateServiceSpy.currentFilters, ...partial};
      }),
      getAreasForProperty: jest.fn().mockReturnValue(of([{id: 100, propertyId: 1, name: 'Stald 1'}])),
      loadTags: jest.fn().mockReturnValue(of([])),
    };
    adhocServiceSpy = {
      createTag: jest.fn().mockReturnValue(of({success: true, model: {id: 12, name: 'Ny', isUserTag: false}})),
      deleteTag: jest.fn().mockReturnValue(of({success: true})),
    };
    translateSpy = {instant: (key: string) => key};
    elementRefSpy = {nativeElement: {contains: () => true}};
    dialogSpy = {open: jest.fn()};
    // dialogConfigHelper reads overlay.scrollStrategies.reposition() — a bare
    // {} makes every dialog-opening action throw before dialog.open is hit.
    overlaySpy = {scrollStrategies: {reposition: jest.fn().mockReturnValue({})}};

    component = new AdhocFiltersComponent(adhocStateServiceSpy, adhocServiceSpy, translateSpy, elementRefSpy, dialogSpy, overlaySpy);
  });

  describe('statusOptions', () => {
    it('renders each option label with its count', () => {
      component.counts = {open: 12, completed: 4, archived: 1};
      // statusOptions is recomputed from ngOnChanges (not a getter - see the
      // component's own comment on why an `[items]`-bound array must stay a
      // stable reference across change-detection ticks), so a direct
      // `component.counts =` assignment in this non-TestBed spec needs an
      // explicit ngOnChanges call to mirror what Angular would otherwise
      // trigger on the real `@Input()` binding.
      component.ngOnChanges({counts: {currentValue: component.counts, previousValue: null, firstChange: false, isFirstChange: () => false}});
      const options = component.statusOptions;
      expect(options.find((o) => o.value === 'open').label).toBe('Open (12)');
      expect(options.find((o) => o.value === 'completed').label).toBe('Solved (4)');
      expect(options.find((o) => o.value === 'archived').label).toBe('Archived (1)');
    });
  });

  describe('tag selection', () => {
    it('isTagSelected reflects currentFilters.tagIds', () => {
      adhocStateServiceSpy.currentFilters.tagIds = [10];
      expect(component.isTagSelected(10)).toBe(true);
      expect(component.isTagSelected(11)).toBe(false);
    });

    it('onToggleTag adds an unselected tag and removes a selected one', () => {
      component.onToggleTag(10);
      expect(adhocStateServiceSpy.updateFilters).toHaveBeenCalledWith(expect.objectContaining({tagIds: [10]}));

      component.onToggleTag(10);
      expect(adhocStateServiceSpy.updateFilters).toHaveBeenCalledWith(expect.objectContaining({tagIds: []}));
    });

    it('onTagLogicChange dispatches the new logic', () => {
      component.onTagLogicChange('and');
      expect(adhocStateServiceSpy.updateFilters).toHaveBeenCalledWith(expect.objectContaining({tagLogic: 'and'}));
    });

    it('onCreateTag ignores blank input and does not call the service', () => {
      component.newTagName = '   ';
      component.onCreateTag();
      expect(adhocServiceSpy.createTag).not.toHaveBeenCalled();
    });

    it('onCreateTag creates the tag, clears the input, and reloads tags on success', () => {
      component.newTagName = 'Ny tag';
      component.onCreateTag();
      expect(adhocServiceSpy.createTag).toHaveBeenCalledWith('Ny tag');
      expect(component.newTagName).toBe('');
      expect(adhocStateServiceSpy.loadTags).toHaveBeenCalled();
    });

    it('onDeleteTag deselects the tag if it was selected, then reloads tags', () => {
      adhocStateServiceSpy.currentFilters.tagIds = [10];
      component.onDeleteTag({id: 10, name: 'Vedligehold', isUserTag: false});
      expect(adhocServiceSpy.deleteTag).toHaveBeenCalledWith(10);
      expect(adhocStateServiceSpy.currentFilters.tagIds).toEqual([]);
      expect(adhocStateServiceSpy.loadTags).toHaveBeenCalled();
    });
  });

  describe('property/area cascade', () => {
    it('onPropertyChange clears areaId and loads the new property\'s areas', () => {
      component.onPropertyChange(1);
      expect(adhocStateServiceSpy.updateFilters).toHaveBeenCalledWith(expect.objectContaining({propertyId: 1, areaId: null}));
      expect(adhocStateServiceSpy.getAreasForProperty).toHaveBeenCalledWith(1);
      expect(component.areas).toEqual([{id: 100, propertyId: 1, name: 'Stald 1'}]);
    });

    it('onPropertyChange(null) clears the local areas list without fetching', () => {
      component.areas = [{id: 100, propertyId: 1, name: 'Stald 1'}];
      component.onPropertyChange(null);
      expect(component.areas).toEqual([]);
    });
  });

  describe('area create/admin modals', () => {
    it('openAreaCreateModal is a no-op without a selected property', () => {
      adhocStateServiceSpy.currentFilters.propertyId = null;
      component.openAreaCreateModal();
      expect(dialogSpy.open).not.toHaveBeenCalled();
    });

    it('openAreaCreateModal opens with the selected property and reloads areas when changed', () => {
      adhocStateServiceSpy.currentFilters.propertyId = 1;
      const afterClosedSpy = {afterClosed: jest.fn()};
      afterClosedSpy.afterClosed.mockReturnValue(of(true));
      dialogSpy.open.mockReturnValue(afterClosedSpy);

      component.openAreaCreateModal();

      expect(dialogSpy.open).toHaveBeenCalledWith(
        expect.any(Function),
        expect.objectContaining({data: {propertyId: 1, propertyName: 'Gård Nord'}}),
      );
      expect(adhocStateServiceSpy.getAreasForProperty).toHaveBeenCalledWith(1);
    });

    it('openAreaAdminModal is a no-op without a selected property', () => {
      adhocStateServiceSpy.currentFilters.propertyId = null;
      component.openAreaAdminModal();
      expect(dialogSpy.open).not.toHaveBeenCalled();
    });

    it('openAreaAdminModal opens with the selected property and only reloads areas when something changed', () => {
      adhocStateServiceSpy.currentFilters.propertyId = 1;
      const afterClosedSpy = {afterClosed: jest.fn()};
      afterClosedSpy.afterClosed.mockReturnValue(of(false));
      dialogSpy.open.mockReturnValue(afterClosedSpy);

      component.openAreaAdminModal();

      expect(dialogSpy.open).toHaveBeenCalledWith(
        expect.any(Function),
        expect.objectContaining({data: {propertyId: 1, propertyName: 'Gård Nord'}}),
      );
      expect(adhocStateServiceSpy.getAreasForProperty).not.toHaveBeenCalled();
    });
  });
});
