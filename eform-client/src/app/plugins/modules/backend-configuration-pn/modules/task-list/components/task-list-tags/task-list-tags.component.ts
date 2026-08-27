import {
  Component,
  EventEmitter,
  Input,
  OnChanges,
  OnDestroy,
  Output,
  SimpleChanges,
  inject,
} from '@angular/core';
import {MatDialog, MatDialogRef} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {Subscription} from 'rxjs';
import {
  SharedTagCreateComponent,
  SharedTagDeleteComponent,
  SharedTagEditComponent,
  SharedTagMultipleCreateComponent,
  SharedTagsComponent,
} from 'src/app/common/modules/eform-shared-tags/components';
import {
  SharedTagCreateModel,
  SharedTagModel,
  SharedTagMultipleCreateModel,
} from 'src/app/common/models';
import {dialogConfigHelper} from 'src/app/common/helpers';
import {ItemsPlanningPnTagsService} from 'src/app/plugins/modules/items-planning-pn/services';

/**
 * Headless controller for the shared tag-management dialogs
 * (list / create / rename / delete / bulk-create) on the admin task list.
 *
 * Renders nothing — `template: ''` on purpose. It exists only to own the
 * dialog wiring and the four `ItemsPlanningPnTagsService` calls, so
 * `TaskListPageComponent` keeps to page state + grid concerns. It is a
 * deliberate port of
 * `items-planning-pn/.../planning-additions/planning-tags/planning-tags.component.ts`
 * (the same controller the task WIZARD uses) — same dialog flow, same
 * service, same `tagsChanged` contract.
 *
 * NOT reused directly from there because `PlanningTagsComponent` is only
 * exported by `PlanningsModule`, and that module ships a
 * `RouterModule.forChild([...])` declaring a `''` route. Importing it into
 * `TaskListModule` would merge a second `''` route into the task list's lazy
 * child config (plus 11 unrelated components, ng2-file-upload and @ng-select).
 * `TaskWizardModule` gets away with it only on first-match-wins.
 */
@Component({
  selector: 'app-task-list-tags',
  template: '',
  standalone: false,
})
export class TaskListTagsComponent implements OnChanges, OnDestroy {
  private tagsService = inject(ItemsPlanningPnTagsService);
  private dialog = inject(MatDialog);
  private overlay = inject(Overlay);

  @Input() availableTags: SharedTagModel[] = [];
  @Output() tagsChanged: EventEmitter<void> = new EventEmitter<void>();

  private dialogRef: MatDialogRef<SharedTagsComponent> | null = null;
  private subs: Subscription[] = [];

  show(showMultipleCreateBtn: boolean = true) {
    if (this.dialogRef) {
      // Already open. Not reachable by mouse (the modal backdrop covers the
      // toolbar button), but a second open would orphan the first dialog's
      // handle and let ITS afterClosed tear down the second one's wiring.
      return;
    }
    this.dialogRef = this.dialog.open(
      SharedTagsComponent,
      dialogConfigHelper(this.overlay, this.availableTags),
    );
    this.dialogRef.componentInstance.showMultipleCreateBtn = showMultipleCreateBtn;

    this.subs.push(this.dialogRef.componentInstance.showCreateTag.subscribe(() => {
      const ref = this.dialog.open(SharedTagCreateComponent, dialogConfigHelper(this.overlay));
      this.subs.push(ref.componentInstance.createdTag.subscribe(tag => this.onTagCreate(tag, ref)));
    }));

    this.subs.push(this.dialogRef.componentInstance.showEditTag.subscribe(tag => {
      const ref = this.dialog.open(SharedTagEditComponent, dialogConfigHelper(this.overlay, tag));
      this.subs.push(ref.componentInstance.updatedTag.subscribe(updated => this.onTagUpdate(updated, ref)));
    }));

    this.subs.push(this.dialogRef.componentInstance.showDeleteTag.subscribe(tag => {
      const ref = this.dialog.open(SharedTagDeleteComponent, dialogConfigHelper(this.overlay, tag));
      this.subs.push(ref.componentInstance.deletedTag.subscribe(deleted => this.onTagDelete(deleted, ref)));
    }));

    this.subs.push(this.dialogRef.componentInstance.showMultipleCreateTag.subscribe(() => {
      const ref = this.dialog.open(
        SharedTagMultipleCreateComponent,
        {...dialogConfigHelper(this.overlay), minWidth: 500},
      );
      this.subs.push(ref.componentInstance.createdTags.subscribe(tags => this.onTagsCreate(tags, ref)));
    }));

    // The list dialog stays open across create/rename/delete, so drop our
    // handle (and the accumulated per-dialog subscriptions) only when it goes.
    const listRef = this.dialogRef;
    this.subs.push(listRef.afterClosed().subscribe(() => {
      if (this.dialogRef === listRef) {
        this.dialogRef = null;
      }
      this.unsubscribeAll();
    }));
  }

  // The four service calls below are deliberately NOT tracked in `subs`:
  // they are one-shot HttpClient observables that complete on their own, and
  // `subs` is torn down when the LIST dialog closes — tracking them there
  // would abort an in-flight POST/PUT/DELETE if the user closed the list while
  // it was still running. This matches how every other call in
  // TaskListPageComponent is made.
  onTagCreate(model: SharedTagCreateModel, ref: MatDialogRef<SharedTagCreateComponent>) {
    this.tagsService.createPlanningTag(model).subscribe(data => {
      if (data && data.success) {
        ref.close();
        this.tagsChanged.emit();
      }
    });
  }

  onTagsCreate(model: SharedTagMultipleCreateModel, ref: MatDialogRef<SharedTagMultipleCreateComponent>) {
    this.tagsService.createPlanningTags(model).subscribe(data => {
      if (data && data.success) {
        ref.close();
        this.tagsChanged.emit();
      }
    });
  }

  onTagUpdate(model: SharedTagModel, ref: MatDialogRef<SharedTagEditComponent>) {
    this.tagsService.updatePlanningTag(model).subscribe(data => {
      if (data && data.success) {
        ref.close();
        this.tagsChanged.emit();
      }
    });
  }

  onTagDelete(model: SharedTagModel, ref: MatDialogRef<SharedTagDeleteComponent>) {
    this.tagsService.deletePlanningTag(model.id).subscribe(data => {
      if (data && data.success) {
        ref.close();
        this.tagsChanged.emit();
      }
    });
  }

  /**
   * The list dialog is handed `availableTags` once, via MAT_DIALOG_DATA. Every
   * successful change re-runs the page's `loadTags()`, which assigns a NEW
   * array — push it into the still-open dialog so the list reflects the change
   * without the user having to close and reopen it (this is what makes
   * bulk-create visibly land).
   */
  ngOnChanges(changes: SimpleChanges): void {
    const change = changes['availableTags'];
    if (change && !change.firstChange && this.dialogRef) {
      this.dialogRef.componentInstance.setAvailableTags(change.currentValue ?? []);
    }
  }

  ngOnDestroy(): void {
    this.unsubscribeAll();
  }

  private unsubscribeAll(): void {
    this.subs.forEach(sub => sub.unsubscribe());
    this.subs = [];
  }
}
