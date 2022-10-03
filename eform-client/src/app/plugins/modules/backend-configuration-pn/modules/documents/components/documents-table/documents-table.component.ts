import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  OnInit,
  Output,
} from '@angular/core';
import {Paged, TableHeaderElementModel} from 'src/app/common/models';
import {DocumentModel, PropertyModel, WorkOrderCaseModel} from '../../../../models';
import {BackendConfigurationPnDocumentsService} from 'src/app/plugins/modules/backend-configuration-pn/services';
// import {
//   TaskManagementStateService
// } from '../store';

@Component({
  selector: 'app-documents-table',
  templateUrl: './documents-table.component.html',
  styleUrls: ['./documents-table.component.scss'],
})
export class DocumentsTableComponent implements OnInit {
  tableHeaders: TableHeaderElementModel[] = [
    { name: 'Id', sortable: false },
    { name: 'PropertyNames', visibleName: 'Properties', sortable: false },
    { name: 'Name', visibleName: 'Document name', sortable: false },
    { name: 'Description', visibleName: 'Document description', sortable: false },
    { name: 'EndDate', visibleName: 'End date',sortable: false },
    // { name: 'Status', visibleName: 'Status', sortable: false },
    { name: 'Actions', elementId: '', sortable: false },
  ];
  @Input() documents: Paged<DocumentModel>;
  @Input() folderId: number;
  @Output() updateTable: EventEmitter<void> = new EventEmitter<void>();
  @Output() openViewModal: EventEmitter<number> = new EventEmitter<number>();
  @Output() openDeleteModal: EventEmitter<DocumentModel> = new EventEmitter<DocumentModel>();
  @Output() openEditModal: EventEmitter<DocumentModel> = new EventEmitter<DocumentModel>();

  constructor(public documentsService: BackendConfigurationPnDocumentsService) {}

  ngOnInit(): void {}

  sortTable(sort: string) {
    // this.documentsService.onSortTable(sort);
    this.updateTable.emit();
  }

  onOpenViewModal(id: number) {
    this.openViewModal.emit(id);
  }

  onOpenDeleteModal(documentModel: DocumentModel) {
    this.openDeleteModal.emit(documentModel);
  }

  onShowEditDocumentModal(propertyModel: DocumentModel) {
    this.openEditModal.emit(propertyModel);
  }
}
