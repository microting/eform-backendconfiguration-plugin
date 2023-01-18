import {
  Component,
  EventEmitter,
  OnDestroy,
  OnInit,
  Output,
} from '@angular/core';
import {
  Paged,
} from 'src/app/common/models';
import {
  DocumentsFolderCreateComponent,
  DocumentsFolderEditComponent
} from '../../';
import {DocumentFolderModel, DocumentFolderRequestModel} from '../../../../../models';
import {BackendConfigurationPnDocumentsService} from '../../../../../services';
import {MatDialog, MatDialogRef} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {dialogConfigHelper} from 'src/app/common/helpers';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';

@AutoUnsubscribe()
@Component({
  selector: 'app-documents-folders',
  templateUrl: './documents-folders.component.html',
  styleUrls: ['./documents-folders.component.scss']
})
export class DocumentsFoldersComponent implements OnInit, OnDestroy {
  folders: Paged<DocumentFolderModel> = new Paged<DocumentFolderModel>();
  @Output() foldersChanged: EventEmitter<void> = new EventEmitter<void>();
  folderCreateSub$: any;

  constructor(
    public dialogRef: MatDialogRef<DocumentsFoldersComponent>,
    public backendConfigurationPnDocumentsService: BackendConfigurationPnDocumentsService,
    public dialog: MatDialog,
    private overlay: Overlay,) {}

  ngOnDestroy(): void {
  }

  hide() {
    this.dialogRef.close();
    this.foldersChanged.emit();
  }

  ngOnInit() {
    this.getFolders();
  }

  showCreateTagModal() {
    const createFolderModal = this.dialog.open(DocumentsFolderCreateComponent, {...dialogConfigHelper(this.overlay), minWidth: 500});
    this.folderCreateSub$ = createFolderModal.componentInstance.folderCreate.subscribe(_ => {
      createFolderModal.close();
      this.onFolderCreate();
    });
  }

  showEditFolderModal(model: DocumentFolderModel) {
    const editFolderModal = this.dialog.open(DocumentsFolderEditComponent, {...dialogConfigHelper(this.overlay, model), minWidth: 500});
    this.folderCreateSub$ = editFolderModal.componentInstance.folderUpdate.subscribe(_ => {
      editFolderModal.close();
      this.onFolderUpdate();
    });
  }

  onFolderUpdate() {
    this.foldersChanged.emit();
    this.getFolders();
    // this.frame.show();
  }

  onFolderCreate() {
    this.foldersChanged.emit();
    this.getFolders();
    // this.frame.show();
  }

  getFolders() {
    const requestModel = new DocumentFolderRequestModel();

    this.backendConfigurationPnDocumentsService.getAllFolders(requestModel).subscribe((data) => {
      if (data && data.success && data.model) {
        this.folders = data.model;
      }
    });
  }

  getFolderTranslation(folder: DocumentFolderModel) {
    if(folder.documentFolderTranslations[0].name) {
      return folder.documentFolderTranslations[0].name;
    } else if(folder.documentFolderTranslations.some(x => x.name)) {
      return folder.documentFolderTranslations.filter(x => x.name)[0].name
    }
    return '';
  }
}
