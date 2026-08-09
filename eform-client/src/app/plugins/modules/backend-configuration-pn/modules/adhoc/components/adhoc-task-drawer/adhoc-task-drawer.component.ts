import {Component, Inject, OnDestroy, OnInit, ViewEncapsulation} from '@angular/core';
import {FormBuilder, FormGroup, Validators} from '@angular/forms';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {Gallery, GalleryItem, ImageItem} from 'ng-gallery';
import {Lightbox} from 'ng-gallery/lightbox';
import {Subscription, forkJoin, from} from 'rxjs';
import {concatMap, toArray} from 'rxjs/operators';
import {format} from 'date-fns';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {PARSING_DATE_FORMAT} from 'src/app/common/const';
import {
  AdhocAreaModel,
  AdhocTaskCreateModel,
  AdhocTaskModel,
  AdhocTaskPhotoModel,
  AdhocWorkerModel,
} from '../../../../models';
import {BackendConfigurationPnAdhocService} from '../../../../services';
import {AdhocStateService} from '../store';
import {resolvePropertyName, resolveWorkerName} from '../../adhoc-display.util';

export type AdhocTaskDrawerMode = 'create' | 'view' | 'edit';

export interface AdhocTaskDrawerData {
  mode: AdhocTaskDrawerMode;
  task?: AdhocTaskModel;
}

/**
 * `MatDialogRef.afterClosed()`'s result shape (M5/F8): a plain `true` means
 * "task was created/updated - reload the table"; the `{action: 'complete'}`
 * variant is emitted by the drawer's own "Udfør opgave" button so the
 * caller (AdhocContainerComponent/AdhocHistoryComponent) can chain
 * `AdhocCompleteModalComponent` on top without this component needing to
 * know that modal exists.
 */
export type AdhocTaskDrawerCloseResult = boolean | {action: 'complete'; task: AdhocTaskModel};

/** 08:00 - the mockup's default reminder time for both deadline and first-visibility reminders. */
const DEFAULT_REMINDER_MINUTES = 8 * 60;

function minutesToTime(minutes: number): string {
  const h = Math.floor(minutes / 60).toString().padStart(2, '0');
  const m = (minutes % 60).toString().padStart(2, '0');
  return `${h}:${m}`;
}

function timeToMinutes(time: string): number {
  const [h, m] = (time || '08:00').split(':').map((v) => parseInt(v, 10) || 0);
  return h * 60 + m;
}

/**
 * "Ny opgave"/"Vis opgave"/"Rediger opgave" side drawer (M5/F7) - a
 * right-anchored, full-height MatDialog (see the `dialogConfigHelper`-style
 * config the callers - AdhocContainerComponent/AdhocTableComponent - open
 * this with: `position: {top: '0', right: '0'}, height: '100vh'`).
 *
 * Three modes: `create` (Opret opgave), `view` (read-only, closes via ×
 * only) and `edit` (Opdater opgave). Three always-visible flat sections
 * (calendar-module "gcal" idiom - icon-led `gcal-row`s, no expansion
 * panels) mirroring the mobile form / mockup `#ny-opgave-drawer`:
 * "Ejendom og opgave" (property/area/title/description/urgent/tags/photos/
 * comments), "Tildel til personer" (worker chips + executionRule +
 * assignment log - NO teams), "Udfør senest og visning første gang"
 * (deadline/visibleFrom + reminders).
 */
@AutoUnsubscribe()
@Component({
  selector: 'app-adhoc-task-drawer',
  templateUrl: './adhoc-task-drawer.component.html',
  styleUrls: ['./adhoc-task-drawer.component.scss'],
  standalone: false,
  // The overlay pane (`.adhoc-drawer-panel`, set via MatDialogConfig.panelClass
  // by callers) lives outside this component's own DOM subtree, so the
  // right-anchor/full-height rules in the stylesheet need global scope to
  // reach it - hence ViewEncapsulation.None instead of the default.
  encapsulation: ViewEncapsulation.None,
})
export class AdhocTaskDrawerComponent implements OnInit, OnDestroy {
  mode: AdhocTaskDrawerMode;
  task: AdhocTaskModel | null;

  form: FormGroup;
  tagIds: number[] = [];
  assignedWorkerIds: number[] = [];
  executionRule = 0;
  areas: AdhocAreaModel[] = [];
  workers: AdhocWorkerModel[] = [];
  removedPhotoIds = new Set<number>();
  queuedPhotoFiles: File[] = [];
  queuedPhotoPreviews: string[] = [];
  newTagName = '';
  newCommentText = '';
  saving = false;

  propertyChangeSub$: Subscription;
  saveSub$: Subscription;
  addCommentSub$: Subscription;
  createTagSub$: Subscription;
  uploadPhotoSub$: Subscription;
  viewPhotosSub$: Subscription;

  constructor(
    public dialogRef: MatDialogRef<AdhocTaskDrawerComponent, AdhocTaskDrawerCloseResult>,
    @Inject(MAT_DIALOG_DATA) public data: AdhocTaskDrawerData,
    private fb: FormBuilder,
    public adhocStateService: AdhocStateService,
    private adhocService: BackendConfigurationPnAdhocService,
    public gallery: Gallery,
    public lightbox: Lightbox,
  ) {
    this.mode = data.mode;
    this.task = data.task ?? null;
  }

  get readonly(): boolean {
    return this.mode === 'view';
  }

  get isCreate(): boolean {
    return this.mode === 'create';
  }

  get visiblePhotos(): AdhocTaskPhotoModel[] {
    return (this.task?.photos ?? []).filter((p) => !this.removedPhotoIds.has(p.id));
  }

  /**
   * #1086: the tags row (heading + chips + picker) renders only when there is
   * something to show or edit - always in create/edit mode (the picker must
   * stay reachable), but in view mode only when the task actually has tags.
   */
  get showTagsSection(): boolean {
    return this.tagIds.length > 0 || !this.readonly;
  }

  /** "Udfør opgave" (M5/F8) is offered from view/edit mode on any open (not completed, not archived) task. */
  get canComplete(): boolean {
    return !this.isCreate && !!this.task && !this.task.completed && !this.task.archived;
  }

  ngOnInit(): void {
    this.form = this.fb.group({
      propertyId: [this.task?.propertyId ?? null, Validators.required],
      areaId: [this.task?.areaId ?? null],
      title: [this.task?.title ?? '', Validators.required],
      description: [this.task?.description ?? ''],
      urgent: [this.task?.urgent ?? false],
      deadline: [this.task?.deadline ? new Date(this.task.deadline) : null],
      deadlineReminder: [this.task?.deadlineReminder ?? false],
      deadlineReminderRepeat: [this.task?.deadlineReminderRepeat ?? 0],
      deadlineReminderTime: [minutesToTime(this.task?.deadlineReminderTimeMinutes ?? DEFAULT_REMINDER_MINUTES)],
      visibleFrom: [this.task?.visibleFrom ? new Date(this.task.visibleFrom) : null],
      visibleReminder: [this.task?.visibleReminder ?? false],
      visibleReminderTime: [minutesToTime(this.task?.visibleReminderTimeMinutes ?? DEFAULT_REMINDER_MINUTES)],
    });

    if (this.readonly) {
      this.form.disable();
    }

    this.tagIds = [...(this.task?.tagIds ?? [])];
    this.assignedWorkerIds = [...(this.task?.assignedWorkerIds ?? [])];
    this.executionRule = this.task?.executionRule ?? 0;

    const propertyId = this.form.get('propertyId').value;
    if (propertyId != null) {
      this.loadAreas(propertyId);
      this.loadWorkers(propertyId);
    }

    this.propertyChangeSub$ = this.form.get('propertyId').valueChanges.subscribe((id: number | null) => {
      this.form.patchValue({areaId: null});
      this.assignedWorkerIds = [];
      this.areas = [];
      this.workers = [];
      if (id != null) {
        this.loadAreas(id);
        this.loadWorkers(id);
      }
    });
  }

  ngOnDestroy(): void {
    this.queuedPhotoPreviews.forEach((url) => URL.revokeObjectURL(url));
  }

  private loadAreas(propertyId: number): void {
    this.adhocStateService.getAreasForProperty(propertyId).subscribe((areas) => (this.areas = areas));
  }

  private loadWorkers(propertyId: number): void {
    this.adhocStateService.getWorkersForProperty(propertyId).subscribe((workers) => (this.workers = workers));
  }

  workerName(workerId: number): string {
    return resolveWorkerName(this.workers, workerId);
  }

  propertyName(propertyId: number | null | undefined): string {
    return resolvePropertyName(this.adhocStateService.properties, propertyId);
  }

  tagName(tagId: number): string {
    return this.adhocStateService.tags.find((t) => t.id === tagId)?.name ?? '';
  }

  // -----------------------------------------------------------------
  // Tags
  // -----------------------------------------------------------------

  isTagSelected(tagId: number): boolean {
    return this.tagIds.includes(tagId);
  }

  onToggleTag(tagId: number): void {
    this.tagIds = this.tagIds.includes(tagId)
      ? this.tagIds.filter((id) => id !== tagId)
      : [...this.tagIds, tagId];
  }

  onCreateTag(): void {
    const name = this.newTagName.trim();
    if (!name) {
      return;
    }
    this.createTagSub$ = this.adhocService.createTag(name).subscribe((res) => {
      if (res && res.success && res.model) {
        this.newTagName = '';
        this.tagIds = [...this.tagIds, res.model.id];
        this.adhocStateService.loadTags().subscribe();
      }
    });
  }

  // -----------------------------------------------------------------
  // Worker assignment
  // -----------------------------------------------------------------

  isWorkerAssigned(workerId: number): boolean {
    return this.assignedWorkerIds.includes(workerId);
  }

  onToggleWorker(workerId: number): void {
    this.assignedWorkerIds = this.assignedWorkerIds.includes(workerId)
      ? this.assignedWorkerIds.filter((id) => id !== workerId)
      : [...this.assignedWorkerIds, workerId];
  }

  // #1088: executionRule and assignedWorkerIds are independent fields (a
  // task can be assigned to persons AND visible to everyone, mobile
  // parity). Toggling the rule must never mutate the assignees - clearing
  // them here would silently delete named assignees server-side on save.
  onExecutionRuleChange(rule: number): void {
    this.executionRule = rule;
  }

  // -----------------------------------------------------------------
  // Photos
  // -----------------------------------------------------------------

  onFilesSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const files = Array.from(input.files ?? []);
    input.value = '';
    if (!files.length) {
      return;
    }
    if (this.isCreate) {
      this.queuedPhotoFiles = [...this.queuedPhotoFiles, ...files];
      this.queuedPhotoPreviews = [...this.queuedPhotoPreviews, ...files.map((f) => URL.createObjectURL(f))];
      return;
    }
    // Edit mode: the task already exists server-side, so upload immediately.
    for (const file of files) {
      this.uploadPhotoSub$ = this.adhocService.uploadPhoto(this.task.id, file).subscribe((res) => {
        if (res && res.success && res.model && this.task) {
          this.task = {...this.task, photos: [...this.task.photos, res.model]};
        }
      });
    }
  }

  removeQueuedPhoto(index: number): void {
    URL.revokeObjectURL(this.queuedPhotoPreviews[index]);
    this.queuedPhotoFiles = this.queuedPhotoFiles.filter((_, i) => i !== index);
    this.queuedPhotoPreviews = this.queuedPhotoPreviews.filter((_, i) => i !== index);
  }

  removeExistingPhoto(photo: AdhocTaskPhotoModel): void {
    this.removedPhotoIds.add(photo.id);
  }

  photoUrl(photo: AdhocTaskPhotoModel): string {
    return this.adhocService.photoUrl(photo.id);
  }

  onViewPhotos(startPhoto: AdhocTaskPhotoModel): void {
    const photos = this.visiblePhotos;
    this.viewPhotosSub$ = forkJoin(photos.map((p) => this.adhocService.getPhotoBlob(p.id))).subscribe((blobs) => {
      const items: GalleryItem[] = blobs.map((blob) => {
        const url = URL.createObjectURL(blob);
        return new ImageItem({src: url, thumb: url});
      });
      const ref = this.gallery.ref('lightbox', {counter: items.length > 1, counterPosition: 'bottom'});
      ref.load(items);
      const startIndex = Math.max(0, photos.findIndex((p) => p.id === startPhoto.id));
      this.lightbox.open(startIndex);
    });
  }

  // -----------------------------------------------------------------
  // Comments
  // -----------------------------------------------------------------

  onAddComment(): void {
    const text = this.newCommentText.trim();
    if (!text || !this.task) {
      return;
    }
    this.addCommentSub$ = this.adhocService.addComment(this.task.id, text).subscribe((res) => {
      if (res && res.success && res.model) {
        this.task = res.model;
        this.newCommentText = '';
      }
    });
  }

  // -----------------------------------------------------------------
  // Save / close
  // -----------------------------------------------------------------

  private buildModel(): AdhocTaskCreateModel {
    const v = this.form.getRawValue();
    return {
      title: v.title,
      description: v.description || '',
      urgent: !!v.urgent,
      propertyId: v.propertyId,
      areaId: v.areaId ?? null,
      tagIds: [...this.tagIds],
      photoIds: this.visiblePhotos.map((p) => p.id),
      visibleFrom: v.visibleFrom ? format(v.visibleFrom, PARSING_DATE_FORMAT) : null,
      deadline: v.deadline ? format(v.deadline, PARSING_DATE_FORMAT) : null,
      visibleReminder: !!v.visibleReminder,
      deadlineReminder: !!v.deadlineReminder,
      deadlineReminderRepeat: v.deadlineReminderRepeat ?? 0,
      visibleReminderTimeMinutes: timeToMinutes(v.visibleReminderTime),
      deadlineReminderTimeMinutes: timeToMinutes(v.deadlineReminderTime),
      executionRule: this.executionRule,
      assignedWorkerIds: [...this.assignedWorkerIds],
    };
  }

  onSave(): void {
    if (this.form.invalid || this.saving) {
      this.form.markAllAsTouched();
      return;
    }
    this.saving = true;
    const model = this.buildModel();

    if (this.isCreate) {
      this.saveSub$ = this.adhocService.createTask(model).subscribe((res) => {
        if (res && res.success && res.model) {
          const taskId = res.model.id;
          if (this.queuedPhotoFiles.length) {
            this.uploadQueuedPhotos(taskId).subscribe(() => this.dialogRef.close(true));
          } else {
            this.dialogRef.close(true);
          }
        } else {
          this.saving = false;
        }
      });
      return;
    }

    if (!this.task) {
      this.saving = false;
      return;
    }
    this.saveSub$ = this.adhocService.updateTask(this.task.id, model).subscribe((res) => {
      this.saving = false;
      if (res && res.success) {
        this.dialogRef.close(true);
      }
    });
  }

  /**
   * Uploads every queued (create-mode) photo file once the task exists
   * server-side. Sequenced via `concatMap` over the file list (rather than
   * `forkJoin`, which would fire them in parallel) so upload order matches
   * selection order.
   */
  private uploadQueuedPhotos(taskId: number) {
    return from(this.queuedPhotoFiles).pipe(
      concatMap((file) => this.adhocService.uploadPhoto(taskId, file)),
      toArray()
    );
  }

  onClose(): void {
    this.dialogRef.close(false);
  }

  /**
   * Closes the drawer with `{action: 'complete', task}` instead of a plain
   * boolean - AdhocContainerComponent/AdhocHistoryComponent's `afterClosed`
   * handler recognizes this shape and opens `AdhocCompleteModalComponent`
   * on top, satisfying the plan's "triggered ... from the drawer"
   * requirement (F8) without this component needing to know that modal
   * exists.
   */
  onCompleteTask(): void {
    if (!this.task) {
      return;
    }
    this.dialogRef.close({action: 'complete', task: this.task});
  }
}
