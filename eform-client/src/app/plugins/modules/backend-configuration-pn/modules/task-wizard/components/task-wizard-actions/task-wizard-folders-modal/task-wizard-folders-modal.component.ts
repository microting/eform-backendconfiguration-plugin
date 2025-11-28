import {
  Component,
  EventEmitter,
  OnDestroy,
  OnInit,
  inject
} from '@angular/core';
import {FolderDto} from 'src/app/common/models';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {MatDialogRef} from '@angular/material/dialog';

@AutoUnsubscribe()
@Component({
    selector: 'app-task-wizard-folders-modal',
    templateUrl: './task-wizard-folders-modal.component.html',
    styleUrls: ['./task-wizard-folders-modal.component.scss'],
    standalone: false
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
