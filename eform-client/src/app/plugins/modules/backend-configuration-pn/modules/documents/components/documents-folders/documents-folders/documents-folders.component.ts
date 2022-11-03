import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  OnInit,
  Output,
  ViewChild,
} from '@angular/core';
import {
  CommonDictionaryModel, FolderDto, Paged,
} from 'src/app/common/models';
import {
  DocumentsFolderCreateComponent,
  DocumentsFolderEditComponent
} from 'src/app/plugins/modules/backend-configuration-pn/modules/documents/components';
import {DocumentFolderModel, DocumentFolderRequestModel} from 'src/app/plugins/modules/backend-configuration-pn/models';
import {BackendConfigurationPnDocumentsService} from 'src/app/plugins/modules/backend-configuration-pn/services';
import {MatDialogRef} from "@angular/material/dialog";
// import { SharedTagDeleteComponent } from '../shared-tag-delete/shared-tag-delete.component';
// import { SharedTagCreateComponent } from '../shared-tag-create/shared-tag-create.component';
// import { SharedTagEditComponent } from '../shared-tag-edit/shared-tag-edit.component';

@Component({
  selector: 'app-documents-folders',
  templateUrl: './documents-folders.component.html',
  styleUrls: ['./documents-folders.component.scss']
})
export class DocumentsFoldersComponent implements OnInit {
  @ViewChild('frame') frame;
  @ViewChild('createFolderModal') createFolderModal: DocumentsFolderCreateComponent;
  @ViewChild('editFolderModal') editFolderModal: DocumentsFolderEditComponent;
  // @ViewChild('tagEditModal') tagEditModal: SharedTagEditComponent;
  // @ViewChild('tagDeleteModal') tagDeleteModal: SharedTagDeleteComponent;
  folders: Paged<DocumentFolderModel>;
  @Output() foldersChanged: EventEmitter<void> = new EventEmitter<void>();
  // selectedFolders: Paged<DocumentFolderModel>;
  // folders: Paged<DocumentFolderModel>;
  @Output() createFolder: EventEmitter<DocumentFolderModel> = new EventEmitter<
    DocumentFolderModel
  >();
  @Output() updateFolder: EventEmitter<DocumentFolderModel> = new EventEmitter<
    DocumentFolderModel
  >();
  // @Output() deleteTag: EventEmitter<SharedTagModel> = new EventEmitter<
  //   SharedTagModel
  // >();

  constructor(
    public dialogRef: MatDialogRef<DocumentsFoldersComponent>,
    public backendConfigurationPnDocumentsService: BackendConfigurationPnDocumentsService) {}

  show() {
    this.getFolders();
    // this.selectedFolders = this.folders;
    this.frame.show();
  }

  hide() {
    //this.frame.hide();
    this.dialogRef.close();
    this.foldersChanged.emit();
  }

  ngOnInit() {}

  showCreateTagModal() {
    this.frame.hide();
    this.createFolderModal.show();
  }

  showEditTagModal(model: DocumentFolderModel) {
    this.frame.hide();
    this.editFolderModal.show(model);
  }

  onChildrenModalHide() {
    this.frame.show();
  }

  onFolderUpdate(model: DocumentFolderModel) {
    this.updateFolder.emit(model);
    this.getFolders();
    this.frame.show();
  }

  onFolderCreate(model: DocumentFolderModel) {
    this.createFolder.emit(model);
    this.getFolders();
    this.frame.show();
  }

  getFolders() {
    const requestModel = new DocumentFolderRequestModel();

    this.backendConfigurationPnDocumentsService.getAllFolders(requestModel).subscribe((data) => {
      if (data && data.success) {
        this.folders = data.model;
      }
    });
  }
}
