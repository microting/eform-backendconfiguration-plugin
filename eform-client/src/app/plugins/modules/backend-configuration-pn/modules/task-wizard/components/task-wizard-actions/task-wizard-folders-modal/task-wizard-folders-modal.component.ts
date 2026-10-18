import {
  Component,
  EventEmitter,
  OnDestroy,
  OnInit,
  inject
} from '@angular/core';
import {FolderDto} from 'src/app/common/models';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {MatDialogRef, MatDialogTitle, MatDialogContent} from '@angular/material/dialog';
import {TranslatePipe} from '@ngx-translate/core';
import {MatCard, MatCardContent} from '@angular/material/card';
import {EformTreeViewPickerComponent} from 'src/app/common/modules/eform-shared/components/eform-tree-view-picker/eform-tree-view-picker.component';

@AutoUnsubscribe()
@Component({
    selector: 'app-task-wizard-folders-modal',
    imports: [TranslatePipe, MatCard, MatCardContent, MatDialogTitle, MatDialogContent, EformTreeViewPickerComponent],
    templateUrl: './task-wizard-folders-modal.component.html',
    styleUrls: ['./task-wizard-folders-modal.component.scss'],
})
export class TaskWizardFoldersModalComponent implements OnInit, OnDestroy {
  public dialogRef = inject(MatDialogRef<TaskWizardFoldersModalComponent>);

  folderSelected: EventEmitter<FolderDto> = new EventEmitter<FolderDto>();
  eFormSdkFolderId: number;
  folders: FolderDto[] = [];

  

  ngOnInit() {
  }

  select(folder: FolderDto) {
    this.folderSelected.emit(folder);
    this.dialogRef.close();
  }

  ngOnDestroy(): void {
  }
}
