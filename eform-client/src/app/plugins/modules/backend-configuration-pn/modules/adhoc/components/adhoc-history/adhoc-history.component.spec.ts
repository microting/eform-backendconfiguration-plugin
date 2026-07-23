import {of} from 'rxjs';
import {AdhocHistoryComponent} from './adhoc-history.component';
import {adhocInitialState} from '../../../../state';

/**
 * Spy-store/spy-service unit test for `AdhocHistoryComponent` (M5/F8).
 * Authored, not run - jest runs from the host frontend only (repo
 * convention).
 */
describe('AdhocHistoryComponent', () => {
  let storeSpy: any;
  let adhocServiceSpy: any;
  let adhocStateServiceSpy: any;
  let dialogSpy: any;
  let overlaySpy: any;
  let component: AdhocHistoryComponent;

  function buildComponent(): AdhocHistoryComponent {
    storeSpy = {
      select: jasmine.createSpy('select').and.returnValue(of({...adhocInitialState.historyFilters})),
      dispatch: jasmine.createSpy('dispatch'),
    };
    adhocServiceSpy = jasmine.createSpyObj('BackendConfigurationPnAdhocService', [
      'getHistory', 'getTask', 'archiveTask',
    ]);
    adhocServiceSpy.getHistory.and.returnValue(of({success: true, model: {total: 0, entities: []}}));
    adhocStateServiceSpy = {
      properties: [],
      tags: [],
      getAreasForProperty: jasmine.createSpy('getAreasForProperty').and.returnValue(of([])),
    };
    dialogSpy = jasmine.createSpyObj('MatDialog', ['open']);
    overlaySpy = {};

    const component = new AdhocHistoryComponent(storeSpy, adhocServiceSpy, adhocStateServiceSpy, dialogSpy, overlaySpy);
    component.ngOnInit();
    return component;
  }

  beforeEach(() => {
    component = buildComponent();
  });

  describe('updateTable / period resolution', () => {
    it('sends AND-only tagIds + property/area + paging to getHistory', () => {
      component.currentFilters = {...component.currentFilters, propertyId: 1, areaId: 10, tagIds: [1, 2]};
      component.updateTable();
      const sentModel = adhocServiceSpy.getHistory.calls.mostRecent().args[0];
      expect(sentModel.propertyId).toBe(1);
      expect(sentModel.areaId).toBe(10);
      expect(sentModel.tagIds).toEqual([1, 2]);
      expect(sentModel.pageNumber).toBe(1);
      expect(sentModel.pageSize).toBe(25);
    });

    it('a day-count preset resolves a non-null dateFrom and a null dateTo', () => {
      component.currentFilters = {...component.currentFilters, periodPreset: '30d'};
      component.updateTable();
      const sentModel = adhocServiceSpy.getHistory.calls.mostRecent().args[0];
      expect(sentModel.dateFrom).not.toBeNull();
      expect(sentModel.dateTo).toBeNull();
    });

    it('the "custom" preset uses customFrom/customTo (both null until the user picks dates)', () => {
      component.currentFilters = {...component.currentFilters, periodPreset: 'custom'};
      component.updateTable();
      const sentModel = adhocServiceSpy.getHistory.calls.mostRecent().args[0];
      expect(sentModel.dateFrom).toBeNull();
      expect(sentModel.dateTo).toBeNull();

      component.customFrom = new Date(2026, 0, 1);
      component.updateTable();
      const sentModel2 = adhocServiceSpy.getHistory.calls.mostRecent().args[0];
      expect(sentModel2.dateFrom).not.toBeNull();
    });
  });

  describe('groupedEvents', () => {
    it('groups events by the date portion of occurredAt', () => {
      component.events = [
        {taskId: 1, taskTitle: 'A', eventType: 'created', occurredAt: '2026-04-15T09:00:00Z', actorName: 'X', detail: null, propertyName: 'P', areaName: null, tagNames: [], lastCommentText: null, lastCommentAuthor: null, lastCommentAt: null, completed: false, archived: false},
        {taskId: 2, taskTitle: 'B', eventType: 'completed', occurredAt: '2026-04-15T14:00:00Z', actorName: 'Y', detail: null, propertyName: 'P', areaName: null, tagNames: [], lastCommentText: null, lastCommentAuthor: null, lastCommentAt: null, completed: true, archived: false},
        {taskId: 3, taskTitle: 'C', eventType: 'created', occurredAt: '2026-04-14T09:00:00Z', actorName: 'Z', detail: null, propertyName: 'P', areaName: null, tagNames: [], lastCommentText: null, lastCommentAuthor: null, lastCommentAt: null, completed: false, archived: false},
      ];
      const groups = component.groupedEvents;
      expect(groups.length).toBe(2);
      expect(groups.find((g) => g.dateKey === '2026-04-15').events.length).toBe(2);
      expect(groups.find((g) => g.dateKey === '2026-04-14').events.length).toBe(1);
    });
  });

  describe('tag toggle (AND-only)', () => {
    it('onToggleTag adds/removes and dispatches + reloads', () => {
      component.onToggleTag(5);
      expect(storeSpy.dispatch).toHaveBeenCalled();
      expect(adhocServiceSpy.getHistory).toHaveBeenCalled();
    });
  });

  describe('row actions', () => {
    it('onArchive calls archiveTask(taskId) and reloads on success', () => {
      adhocServiceSpy.archiveTask.and.returnValue(of({success: true}));
      const callsBefore = adhocServiceSpy.getHistory.calls.count();
      component.onArchive({taskId: 42} as any);
      expect(adhocServiceSpy.archiveTask).toHaveBeenCalledWith(42);
      expect(adhocServiceSpy.getHistory.calls.count()).toBeGreaterThan(callsBefore);
    });

    it('onRowClick fetches the full task and opens the drawer in view mode', () => {
      const task = {id: 42, title: 'Fix roof'};
      adhocServiceSpy.getTask.and.returnValue(of({success: true, model: task}));
      dialogSpy.open.and.returnValue({afterClosed: () => of(false)});
      component.onRowClick({taskId: 42} as any);
      expect(adhocServiceSpy.getTask).toHaveBeenCalledWith(42);
      const openArgs = dialogSpy.open.calls.mostRecent().args;
      expect(openArgs[1].data.mode).toBe('view');
      expect(openArgs[1].data.task).toBe(task);
    });

    it('onCopy opens the copy modal and, on a returned copy, opens the drawer in edit mode', () => {
      const copiedTask = {id: 99, title: 'Fix roof (copy)'};
      dialogSpy.open.and.returnValues(
        {afterClosed: () => of(copiedTask)},
        {afterClosed: () => of(true)},
      );
      component.onCopy({taskId: 42, taskTitle: 'Fix roof'} as any);
      expect(dialogSpy.open).toHaveBeenCalledTimes(2);
      const drawerArgs = dialogSpy.open.calls.argsFor(1);
      expect(drawerArgs[1].data.mode).toBe('edit');
      expect(drawerArgs[1].data.task).toBe(copiedTask);
    });
  });
});
