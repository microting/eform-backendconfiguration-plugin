import {
  Component,
  EventEmitter,
  Input,
  OnInit,
  Output,
} from '@angular/core';
import {TableHeaderElementModel} from 'src/app/common/models';
import {DocumentFolderModel, DocumentModel, DocumentTranslationModel,} from '../../../../models';
import {BackendConfigurationPnDocumentsService} from 'src/app/plugins/modules/backend-configuration-pn/services';
import {MtxGridColumn} from '@ng-matero/extensions/grid';
import {TranslateService} from '@ngx-translate/core';

@Component({
  selector: 'app-documents-table',
  templateUrl: './documents-table.component.html',
  styleUrls: ['./documents-table.component.scss'],
})
export class DocumentsTableComponent implements OnInit {
  tableHeaders: MtxGridColumn[] = [
    { field: 'id', header: this.translateService.stream('Id')},
    { field: 'propertyNames', header: this.translateService.stream('Properties'), formatter: (document: DocumentModel) => document.propertyNames},
    { field: 'documentTranslations[0].name', header: this.translateService.stream('Document name'), formatter: (document: DocumentModel) => this.getDocumentTranslationName(document)},
    { field: 'documentTranslations[0].description', header: this.translateService.stream('Document description'), formatter: (document: DocumentModel) => this.getDocumentTranslationDescription(document)},
    { field: 'endDate', header: this.translateService.stream('End date'), type: 'date', typeParameter: {format: 'dd.MM.y'}},
    { field: 'status', header: this.translateService.stream('Status'), formatter: (document: DocumentModel) => this.translateService.instant(document.status ? 'ON' : 'OFF')},
    {
      field: 'actions',
      header: this.translateService.stream('Actions'),
      type: 'button',
      width: '160px',
      pinned: 'right',
      right: '0px',
      buttons: [
        {
          color: 'accent',
          type: 'icon',
          icon: 'edit',
          tooltip: this.translateService.stream('Edit document'),
          click: (document: DocumentModel) => this.onShowEditDocumentModal(document),
          class: 'editDocumentBtn',
        },
        {
          color: 'warn',
          type: 'icon',
          icon: 'delete',
          tooltip: this.translateService.stream('Delete document'),
          click: (document: DocumentModel) => this.onOpenDeleteModal(document),
          class: 'deleteDocumentBtn',
        },
      ],
    },
  ]
  @Input() documents: DocumentModel[] = [];
  @Output() updateTable: EventEmitter<void> = new EventEmitter<void>();
  @Output() openViewModal: EventEmitter<number> = new EventEmitter<number>();
  @Output() openDeleteModal: EventEmitter<DocumentModel> = new EventEmitter<DocumentModel>();
  @Output() openEditModal: EventEmitter<DocumentModel> = new EventEmitter<DocumentModel>();

  constructor(
    public documentsService: BackendConfigurationPnDocumentsService,
    private translateService: TranslateService,
  ) {}

  ngOnInit(): void {}

  onOpenViewModal(id: number) {
    this.openViewModal.emit(id);
  }

  onOpenDeleteModal(documentModel: DocumentModel) {
    this.openDeleteModal.emit(documentModel);
  }

  onShowEditDocumentModal(propertyModel: DocumentModel) {
    this.openEditModal.emit(propertyModel);
  }

  getDocumentTranslationName(documentModel: DocumentModel) {
    if(documentModel.documentTranslations[0].name) {
      return documentModel.documentTranslations[0].name;
    } else if(documentModel.documentTranslations.some(x => x.name)) {
      return documentModel.documentTranslations.filter(x => x.name)[0].name
    }
    return '';
  }

  getDocumentTranslationDescription(documentModel: DocumentModel) {
    const emptyString = '<div></div>';
    const predicate = (value: DocumentTranslationModel) => value.description && value.description !== emptyString
    if(documentModel.documentTranslations[0].description && documentModel.documentTranslations[0].description !== emptyString) {
      return documentModel.documentTranslations[0].description;
    } else if(documentModel.documentTranslations.some(predicate)) {
      return documentModel.documentTranslations.filter(predicate)[0].description
    }
    return '';
  }
}
