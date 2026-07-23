import {FormBuilder} from '@angular/forms';
import {of} from 'rxjs';
import {AdhocTaskDrawerComponent, AdhocTaskDrawerData} from './adhoc-task-drawer.component';
import {AdhocTaskModel} from '../../../../models';

/**
 * Spy-service unit test for `AdhocTaskDrawerComponent` (M5/F7). Authored,
 * not run - jest runs from the host frontend only (repo convention).
 */
describe('AdhocTaskDrawerComponent', () => {
  let dialogRefSpy: any;
  let adhocStateServiceSpy: any;
  let adhocServiceSpy: any;
  let gallerySpy: any;
  let lightboxSpy: any;

  const existingTask: AdhocTaskModel = {
    id: 42,
    createdByWorkerId: 100,
    createdAt: '2026-04-01T00:00:00Z',
    title: 'Fix roof',
    description: 'Leak near the chimney',
    urgent: true,
    propertyId: 1,
    areaId: 10,
    tagIds: [1000],
    photos: [{id: 5000, contentType: 'image/png'}],
    visibleFrom: null,
    deadline: '2026-04-20T00:00:00Z',
    visibleReminder: false,
    deadlineReminder: true,
    deadlineReminderRepeat: 1,
    visibleReminderTimeMinutes: 480,
    deadlineReminderTimeMinutes: 540,
    executionRule: 0,
    assignedWorkerIds: [100],
    assignmentLog: [],
    completed: false,
    completedByWorkerId: null,
    completedAt: null,
    archived: false,
    archivedAt: null,
    comments: [],
  };

  function buildComponent(data: AdhocTaskDrawerData): AdhocTaskDrawerComponent {
    dialogRefSpy = jasmine.createSpyObj('MatDialogRef', ['close']);
    adhocStateServiceSpy = {
      properties: [{id: 1, name: 'Gård Nord'}],
      tags: [{id: 1000, name: 'Vedligehold', isUserTag: false}],
      getAreasForProperty: jasmine.createSpy('getAreasForProperty').and.returnValue(of([{id: 10, propertyId: 1, name: 'Stald 1'}])),
      getWorkersForProperty: jasmine.createSpy('getWorkersForProperty').and.returnValue(of([{workerId: 100, displayName: 'Mette Hansen', propertyIds: [1]}])),
      loadTags: jasmine.createSpy('loadTags').and.returnValue(of([])),
    };
    adhocServiceSpy = jasmine.createSpyObj('BackendConfigurationPnAdhocService', [
      'createTask', 'updateTask', 'createTag', 'uploadPhoto', 'addComment', 'getPhotoBlob',
    ]);
    gallerySpy = {ref: jasmine.createSpy('ref').and.returnValue({load: jasmine.createSpy('load')})};
    lightboxSpy = {open: jasmine.createSpy('open')};

    const component = new AdhocTaskDrawerComponent(
      dialogRefSpy,
      data,
      new FormBuilder(),
      adhocStateServiceSpy,
      adhocServiceSpy,
      gallerySpy,
      lightboxSpy,
    );
    component.ngOnInit();
    return component;
  }

  describe('create mode', () => {
    let component: AdhocTaskDrawerComponent;

    beforeEach(() => {
      component = buildComponent({mode: 'create'});
    });

    it('defaults to an empty/blank form, executionRule 0, no tags/assignees', () => {
      expect(component.form.get('title').value).toBe('');
      expect(component.form.get('urgent').value).toBeFalse();
      expect(component.executionRule).toBe(0);
      expect(component.tagIds).toEqual([]);
      expect(component.assignedWorkerIds).toEqual([]);
    });

    it('onSave calls createTask with the built model and closes on success', () => {
      adhocServiceSpy.createTask.and.returnValue(of({success: true, model: {id: 99, photos: []}}));
      component.form.patchValue({title: 'New task', propertyId: 1});
      component.onSave();

      expect(adhocServiceSpy.createTask).toHaveBeenCalled();
      const sentModel = adhocServiceSpy.createTask.calls.mostRecent().args[0];
      expect(sentModel.title).toBe('New task');
      expect(sentModel.propertyId).toBe(1);
      expect(sentModel.executionRule).toBe(0);
      expect(sentModel.deadlineReminderTimeMinutes).toBe(480); // default 08:00
      expect(dialogRefSpy.close).toHaveBeenCalledWith(true);
    });

    it('does not call createTask when the form is invalid (missing required title/propertyId)', () => {
      component.onSave();
      expect(adhocServiceSpy.createTask).not.toHaveBeenCalled();
    });
  });

  describe('view mode', () => {
    it('disables the form and exposes the existing task/tags/assignees read-only', () => {
      const component = buildComponent({mode: 'view', task: existingTask});
      expect(component.form.disabled).toBeTrue();
      expect(component.readonly).toBeTrue();
      expect(component.tagIds).toEqual([1000]);
      expect(component.assignedWorkerIds).toEqual([100]);
      expect(component.visiblePhotos).toEqual([{id: 5000, contentType: 'image/png'}]);
    });
  });

  describe('edit mode', () => {
    let component: AdhocTaskDrawerComponent;

    beforeEach(() => {
      component = buildComponent({mode: 'edit', task: existingTask});
    });

    it('populates the form from the existing task', () => {
      expect(component.form.get('title').value).toBe('Fix roof');
      expect(component.form.get('urgent').value).toBeTrue();
      expect(component.form.get('deadlineReminderTime').value).toBe('09:00');
    });

    it('onToggleTag removes a selected tag and re-adds it', () => {
      component.onToggleTag(1000);
      expect(component.tagIds).toEqual([]);
      component.onToggleTag(1000);
      expect(component.tagIds).toEqual([1000]);
    });

    it('onExecutionRuleChange(1) clears assignedWorkerIds', () => {
      expect(component.assignedWorkerIds).toEqual([100]);
      component.onExecutionRuleChange(1);
      expect(component.executionRule).toBe(1);
      expect(component.assignedWorkerIds).toEqual([]);
    });

    it('removeExistingPhoto excludes the photo from visiblePhotos and the saved photoIds', () => {
      adhocServiceSpy.updateTask.and.returnValue(of({success: true}));
      component.removeExistingPhoto({id: 5000, contentType: 'image/png'});
      expect(component.visiblePhotos).toEqual([]);

      component.onSave();
      const sentModel = adhocServiceSpy.updateTask.calls.mostRecent().args[1];
      expect(sentModel.photoIds).toEqual([]);
    });

    it('onSave calls updateTask(taskId, model) and closes on success', () => {
      adhocServiceSpy.updateTask.and.returnValue(of({success: true}));
      component.onSave();
      expect(adhocServiceSpy.updateTask).toHaveBeenCalled();
      expect(adhocServiceSpy.updateTask.calls.mostRecent().args[0]).toBe(42);
      expect(dialogRefSpy.close).toHaveBeenCalledWith(true);
    });

    it('onAddComment posts the trimmed text and replaces the local task with the response', () => {
      const updated = {...existingTask, comments: [{authorWorkerId: 100, createdAt: '2026-04-02T00:00:00Z', text: 'Looks fixed'}]};
      adhocServiceSpy.addComment.and.returnValue(of({success: true, model: updated}));
      component.newCommentText = '  Looks fixed  ';
      component.onAddComment();
      expect(adhocServiceSpy.addComment).toHaveBeenCalledWith(42, 'Looks fixed');
      expect(component.task).toEqual(updated);
      expect(component.newCommentText).toBe('');
    });
  });
});
