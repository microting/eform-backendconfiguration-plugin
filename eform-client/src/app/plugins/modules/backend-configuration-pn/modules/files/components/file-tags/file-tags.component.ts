import {
  Component,
  EventEmitter,
  Input,
  OnChanges,
  OnDestroy,
  OnInit,
  Output,
  SimpleChanges,
  inject
} from '@angular/core';
import {
  SharedTagCreateComponent,
  SharedTagDeleteComponent,
  SharedTagEditComponent,
  SharedTagMultipleCreateComponent,
  SharedTagsComponent
} from 'src/app/common/modules/eform-shared-tags/components';
import {SharedTagCreateModel, SharedTagModel, SharedTagMultipleCreateModel,} from 'src/app/common/models';
import {Subscription} from 'rxjs';
import {BackendConfigurationPnFileTagsService} from '../../../../services';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {MatDialog, MatDialogRef} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {dialogConfigHelper} from 'src/app/common/helpers';
import {fileTagsForDialog} from '../../file-tags-access';

@AutoUnsubscribe()
@Component({
    selector: 'app-file-tags',
    templateUrl: './file-tags.component.html',
    styleUrls: ['./file-tags.component.html'],
})
export class FileTagsComponent implements OnInit, OnDestroy, OnChanges {
  private tagsService = inject(BackendConfigurationPnFileTagsService);
  public dialog = inject(MatDialog);
  private overlay = inject(Overlay);

  @Input() availableTags: SharedTagModel[] = [];
  /** Admin or Leder: rename and delete are offered (D-L11). */
  @Input() canManage = false;
  @Output() tagsChanged: EventEmitter<void> = new EventEmitter<void>();
  /** The open tags dialog; null once it closes (Material clears componentInstance then). */
  dialogRef: MatDialogRef<SharedTagsComponent> | null = null;
  deleteTag$: Subscription;
  createTag$: Subscription;
  updateTag$: Subscription;
  showCreateTagSub$: Subscription;
  showEditTagSub$: Subscription;
  showDeleteTagSub$: Subscription;
  deletedTagSub$: Subscription;
  updatedTagSub$: Subscription;
  createTags$: Subscription;
  showMultipleTagTagSub$: Subscription;
  createdTagsSub$: Subscription;

  

  ngOnInit() {}

  show() {
    const dialogRef = this.dialog.open(SharedTagsComponent, dialogConfigHelper(this.overlay, fileTagsForDialog(this.availableTags, this.canManage)));
    this.dialogRef = dialogRef;
    dialogRef.afterClosed().subscribe(() => {
      if (this.dialogRef === dialogRef) {
        this.dialogRef = null;
      }
    });
    dialogRef.componentInstance.showMultipleCreateBtn = true;
    this.showCreateTagSub$ = dialogRef.componentInstance.showCreateTag.subscribe(() => {
      const dialogRefCreateTag = this.dialog.open(SharedTagCreateComponent, dialogConfigHelper(this.overlay));
      this.updatedTagSub$ = dialogRefCreateTag.componentInstance.createdTag.subscribe(tag => this.onTagCreate(tag, dialogRefCreateTag));
    });
    this.showEditTagSub$ = dialogRef.componentInstance.showEditTag.subscribe((x) => {
      const dialogRefUpdateTag = this.dialog.open(SharedTagEditComponent, dialogConfigHelper(this.overlay, x));
      this.updatedTagSub$ = dialogRefUpdateTag.componentInstance.updatedTag.subscribe(tag => this.onTagUpdate(tag, dialogRefUpdateTag));
    });
    this.showDeleteTagSub$ = dialogRef.componentInstance.showDeleteTag.subscribe((x) => {
      const dialogRefUpdateTag = this.dialog.open(SharedTagDeleteComponent, dialogConfigHelper(this.overlay, x));
      this.deletedTagSub$ = dialogRefUpdateTag.componentInstance.deletedTag.subscribe(tag => this.onTagDelete(tag, dialogRefUpdateTag));
    });
    this.showMultipleTagTagSub$ = dialogRef.componentInstance.showMultipleCreateTag.subscribe(() => {
      const dialogRefUpdateTag = this.dialog.open(SharedTagMultipleCreateComponent, {...dialogConfigHelper(this.overlay), minWidth: 500});
      this.createdTagsSub$ = dialogRefUpdateTag.componentInstance.createdTags
        .subscribe(tags => this.onTagsCreate(tags, dialogRefUpdateTag));
    });
  }

  onTagUpdate(model: SharedTagModel, dialogRefUpdateTag: MatDialogRef<SharedTagEditComponent>) {
    this.updateTag$ = this.tagsService
      .updateTag(model)
      .subscribe((data) => {
        if (data && data.success) {
          dialogRefUpdateTag.close();
          this.tagsChanged.emit();
        }
      });
  }

  onTagCreate(model: SharedTagCreateModel, dialogRefUpdateTag: MatDialogRef<SharedTagCreateComponent>) {
    this.createTag$ = this.tagsService
      .createTag(model)
      .subscribe((data) => {
        if (data && data.success) {
          dialogRefUpdateTag.close();
          this.tagsChanged.emit();
        }
      });
  }

  onTagsCreate(tags: SharedTagMultipleCreateModel, dialogRefUpdateTag: MatDialogRef<SharedTagMultipleCreateComponent>) {
    this.createTags$ = this.tagsService
      .createTags(tags)
      .subscribe((data) => {
        if (data && data.success) {
          dialogRefUpdateTag.close();
          this.tagsChanged.emit();
        }
      });
  }

  onTagDelete(model: SharedTagModel, dialogRefUpdateTag: MatDialogRef<SharedTagDeleteComponent>) {
    this.deleteTag$ = this.tagsService
      .deleteTag(model.id)
      .subscribe((data) => {
        if (data && data.success) {
          dialogRefUpdateTag.close();
          this.tagsChanged.emit();
        }
      });
  }

  ngOnChanges(changes: SimpleChanges): void {
    const dialog = this.dialogRef?.componentInstance;
    if ((changes.availableTags || changes.canManage) && dialog) {
      dialog.availableTags = fileTagsForDialog(this.availableTags, this.canManage);
    }
  }

  ngOnDestroy(): void {}
}
