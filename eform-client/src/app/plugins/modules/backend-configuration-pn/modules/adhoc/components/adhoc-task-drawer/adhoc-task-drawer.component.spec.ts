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
  let matDialogSpy: any;
  let overlaySpy: any;

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
    dialogRefSpy = {close: jest.fn()};
    adhocStateServiceSpy = {
      properties: [{id: 1, name: 'Gård Nord'}],
      tags: [{id: 1000, name: 'Vedligehold', isUserTag: false}],
      getAreasForProperty: jest.fn().mockReturnValue(of([{id: 10, propertyId: 1, name: 'Stald 1'}])),
      getWorkersForProperty: jest.fn().mockReturnValue(of([{workerId: 100, displayName: 'Mette Hansen', propertyIds: [1]}])),
      loadTags: jest.fn().mockReturnValue(of([])),
    };
    adhocServiceSpy = {
      createTask: jest.fn(),
      updateTask: jest.fn(),
      createTag: jest.fn(),
      uploadPhoto: jest.fn(),
      addComment: jest.fn(),
      getPhotoBlob: jest.fn(),
    };
    gallerySpy = {ref: jest.fn().mockReturnValue({load: jest.fn()})};
    lightboxSpy = {open: jest.fn()};
    matDialogSpy = {open: jest.fn()};
    // dialogConfigHelper(overlay) only touches scrollStrategies.reposition().
    overlaySpy = {scrollStrategies: {reposition: jest.fn().mockReturnValue({})}};

    const component = new AdhocTaskDrawerComponent(
      dialogRefSpy,
      data,
      new FormBuilder(),
      adhocStateServiceSpy,
      adhocServiceSpy,
      gallerySpy,
      lightboxSpy,
      matDialogSpy,
      overlaySpy,
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
      expect(component.form.get('urgent').value).toBe(false);
      expect(component.executionRule).toBe(0);
      expect(component.tagIds).toEqual([]);
      expect(component.assignedWorkerIds).toEqual([]);
    });

    it('onSave calls createTask with the built model and closes on success', () => {
      adhocServiceSpy.createTask.mockReturnValue(of({success: true, model: {id: 99, photos: []}}));
      component.form.patchValue({title: 'New task', propertyId: 1});
      component.onSave();

      expect(adhocServiceSpy.createTask).toHaveBeenCalled();
      const sentModel = adhocServiceSpy.createTask.mock.lastCall[0];
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

    // #1086: the tag picker must stay available while creating, even though
    // a fresh task has no tags yet.
    it('showTagsSection is true in create mode despite tagIds being empty', () => {
      expect(component.tagIds).toEqual([]);
      expect(component.showTagsSection).toBe(true);
    });

    // #1100: queued (create-mode) previews get the same confirm gate as
    // existing photos.
    it('onDeleteQueuedPhoto removes the queued file only after the modal confirms', () => {
      (URL as any).revokeObjectURL = jest.fn();
      component.queuedPhotoFiles = [new File([''], 'a.png', {type: 'image/png'})];
      component.queuedPhotoPreviews = ['blob:a'];
      matDialogSpy.open.mockReturnValue({afterClosed: () => of(true)});
      component.onDeleteQueuedPhoto(0);
      expect(component.queuedPhotoFiles).toEqual([]);
      expect(component.queuedPhotoPreviews).toEqual([]);
      expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:a');
    });

    it('onDeleteQueuedPhoto keeps the queued file when the modal is cancelled', () => {
      (URL as any).revokeObjectURL = jest.fn();
      component.queuedPhotoFiles = [new File([''], 'a.png', {type: 'image/png'})];
      component.queuedPhotoPreviews = ['blob:a'];
      matDialogSpy.open.mockReturnValue({afterClosed: () => of(false)});
      component.onDeleteQueuedPhoto(0);
      expect(component.queuedPhotoFiles.length).toBe(1);
      expect(component.queuedPhotoPreviews).toEqual(['blob:a']);
      expect(URL.revokeObjectURL).not.toHaveBeenCalled();
    });
  });

  describe('view mode', () => {
    it('disables the form and exposes the existing task/tags/assignees read-only', () => {
      const component = buildComponent({mode: 'view', task: existingTask});
      expect(component.form.disabled).toBe(true);
      expect(component.readonly).toBe(true);
      expect(component.tagIds).toEqual([1000]);
      expect(component.assignedWorkerIds).toEqual([100]);
      expect(component.visiblePhotos).toEqual([{id: 5000, contentType: 'image/png'}]);
    });

    // #1086: the "Etiketter" heading and the (empty) chip container must not
    // render at all when a read-only task has no tags - no heading, no grey box.
    it('showTagsSection is false when the task has no tags', () => {
      const component = buildComponent({mode: 'view', task: {...existingTask, tagIds: []}});
      expect(component.showTagsSection).toBe(false);
    });

    it('showTagsSection is true when the task has tags', () => {
      const component = buildComponent({mode: 'view', task: existingTask});
      expect(component.showTagsSection).toBe(true);
    });
  });

  describe('edit mode', () => {
    let component: AdhocTaskDrawerComponent;

    beforeEach(() => {
      component = buildComponent({mode: 'edit', task: existingTask});
    });

    it('populates the form from the existing task', () => {
      expect(component.form.get('title').value).toBe('Fix roof');
      expect(component.form.get('urgent').value).toBe(true);
      expect(component.form.get('deadlineReminderTime').value).toBe('09:00');
    });

    it('onToggleTag removes a selected tag and re-adds it', () => {
      component.onToggleTag(1000);
      expect(component.tagIds).toEqual([]);
      component.onToggleTag(1000);
      expect(component.tagIds).toEqual([1000]);
    });

    // #1086: in edit mode the picker stays visible even after the last tag
    // is removed, so the user can still add tags back.
    it('showTagsSection stays true in edit mode after removing the last tag', () => {
      component.onToggleTag(1000);
      expect(component.tagIds).toEqual([]);
      expect(component.showTagsSection).toBe(true);
    });

    // #1088: executionRule and assignedWorkerIds are independent fields -
    // toggling "Everyone"/"Assigned only" must never mutate the assignees,
    // otherwise a single toggle click during an edit session silently
    // deletes named assignees server-side on save.
    it('onExecutionRuleChange(1) ("Everyone") preserves a non-empty assignedWorkerIds', () => {
      expect(component.assignedWorkerIds).toEqual([100]);
      component.onExecutionRuleChange(1);
      expect(component.executionRule).toBe(1);
      expect(component.assignedWorkerIds).toEqual([100]);
    });

    it('onExecutionRuleChange back to 0 ("Assigned only") also preserves assignedWorkerIds', () => {
      component.onExecutionRuleChange(1);
      component.onExecutionRuleChange(0);
      expect(component.executionRule).toBe(0);
      expect(component.assignedWorkerIds).toEqual([100]);
    });

    it('onSave after toggling to "Everyone" sends executionRule 1 AND the original assignedWorkerIds', () => {
      adhocServiceSpy.updateTask.mockReturnValue(of({success: true}));
      component.onExecutionRuleChange(1);
      component.onSave();

      const sentModel = adhocServiceSpy.updateTask.mock.lastCall[1];
      expect(sentModel.executionRule).toBe(1);
      expect(sentModel.assignedWorkerIds).toEqual([100]);
    });

    // #1100: the delete-X never removes a photo directly - the "Slet
    // billede?" confirm modal gates both the existing- and queued-photo
    // flavours, and only a confirmed close performs the removal.
    it('onDeleteExistingPhoto removes the photo only after the modal confirms', () => {
      matDialogSpy.open.mockReturnValue({afterClosed: () => of(true)});
      component.onDeleteExistingPhoto({id: 5000, contentType: 'image/png'});
      expect(matDialogSpy.open).toHaveBeenCalled();
      expect(component.visiblePhotos).toEqual([]);
    });

    it('onDeleteExistingPhoto keeps the photo when the modal is cancelled', () => {
      matDialogSpy.open.mockReturnValue({afterClosed: () => of(false)});
      component.onDeleteExistingPhoto({id: 5000, contentType: 'image/png'});
      expect(matDialogSpy.open).toHaveBeenCalled();
      expect(component.visiblePhotos).toEqual([{id: 5000, contentType: 'image/png'}]);
    });

    // Design-spec Surface 2: ESC/scrim-click must cancel (the module-default
    // dialogConfigHelper sets disableClose: true, which this flow overrides)
    // and the dialog is announced as an alertdialog.
    it('opens the photo-delete confirm as a cancellable alertdialog', () => {
      matDialogSpy.open.mockReturnValue({afterClosed: () => of(false)});
      component.onDeleteExistingPhoto({id: 5000, contentType: 'image/png'});
      const config = matDialogSpy.open.mock.lastCall[1];
      expect(config.disableClose).toBe(false);
      expect(config.role).toBe('alertdialog');
    });

    it('removeExistingPhoto excludes the photo from visiblePhotos and the saved photoIds', () => {
      adhocServiceSpy.updateTask.mockReturnValue(of({success: true}));
      component.removeExistingPhoto({id: 5000, contentType: 'image/png'});
      expect(component.visiblePhotos).toEqual([]);

      component.onSave();
      const sentModel = adhocServiceSpy.updateTask.mock.lastCall[1];
      expect(sentModel.photoIds).toEqual([]);
    });

    it('onSave calls updateTask(taskId, model) and closes on success', () => {
      adhocServiceSpy.updateTask.mockReturnValue(of({success: true}));
      component.onSave();
      expect(adhocServiceSpy.updateTask).toHaveBeenCalled();
      expect(adhocServiceSpy.updateTask.mock.lastCall[0]).toBe(42);
      expect(dialogRefSpy.close).toHaveBeenCalledWith(true);
    });

    it('canComplete is true for an open task, and onCompleteTask closes with the {action, task} signal', () => {
      expect(component.canComplete).toBe(true);
      component.onCompleteTask();
      expect(dialogRefSpy.close).toHaveBeenCalledWith({action: 'complete', task: existingTask});
    });

    it('canComplete is false once the task is completed or archived', () => {
      const completed = buildComponent({mode: 'edit', task: {...existingTask, completed: true}});
      expect(completed.canComplete).toBe(false);
      const archived = buildComponent({mode: 'edit', task: {...existingTask, archived: true}});
      expect(archived.canComplete).toBe(false);
    });

    it('onAddComment posts the trimmed text and replaces the local task with the response', () => {
      const updated = {...existingTask, comments: [{authorWorkerId: 100, createdAt: '2026-04-02T00:00:00Z', text: 'Looks fixed'}]};
      adhocServiceSpy.addComment.mockReturnValue(of({success: true, model: updated}));
      component.newCommentText = '  Looks fixed  ';
      component.onAddComment();
      expect(adhocServiceSpy.addComment).toHaveBeenCalledWith(42, 'Looks fixed');
      expect(component.task).toEqual(updated);
      expect(component.newCommentText).toBe('');
    });
  });
});
