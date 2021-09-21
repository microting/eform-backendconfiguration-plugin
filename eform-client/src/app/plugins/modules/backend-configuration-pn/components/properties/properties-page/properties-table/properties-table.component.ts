import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  OnInit,
  Output,
} from '@angular/core';
import { Paged, TableHeaderElementModel } from 'src/app/common/models';
import { BackendConfigurationPnClaims } from '../../../../enums';
import { PropertyModel } from '../../../../models/properties';
import { applicationLanguages } from 'src/app/common/const';

@Component({
  selector: 'app-properties-table',
  templateUrl: './properties-table.component.html',
  styleUrls: ['./properties-table.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PropertiesTableComponent implements OnInit {
  @Input() propertiesModel: Paged<PropertyModel> = new Paged<PropertyModel>();
  @Output()
  showEditPropertyModal: EventEmitter<PropertyModel> = new EventEmitter<PropertyModel>();
  @Output()
  showEditPropertyAreasModal: EventEmitter<PropertyModel> = new EventEmitter<PropertyModel>();
  @Output()
  showPropertyAreasModal: EventEmitter<PropertyModel> = new EventEmitter<PropertyModel>();
  @Output()
  showDeletePropertyModal: EventEmitter<PropertyModel> = new EventEmitter<PropertyModel>();

  tableHeaders: TableHeaderElementModel[] = [
    { name: 'Id', elementId: 'idTableHeader', sortable: false },
    { name: 'Name', elementId: 'nameTableHeader', sortable: false },
    {
      name: 'CHR Number',
      elementId: 'chrNumberTableHeader',
      sortable: false,
    },
    {
      name: 'Address',
      elementId: 'addressTableHeader',
      sortable: false,
    },
    { name: 'Languages', elementId: 'languagesTableHeader', sortable: false },
    { name: 'Actions', elementId: '', sortable: false },
  ];

  get backendConfigurationPnClaims() {
    return BackendConfigurationPnClaims;
  }

  constructor() {}

  ngOnInit(): void {}

  onShowDeletePropertyModal(planning: PropertyModel) {
    this.showDeletePropertyModal.emit(planning);
  }

  onShowEditPropertyModal(planning: PropertyModel) {
    this.showEditPropertyModal.emit(planning);
  }

  onShowEditPropertyAreasModal(planning: PropertyModel) {
    this.showEditPropertyAreasModal.emit(planning);
  }

  onShowPropertyAreasModal(planning: PropertyModel) {
    this.showPropertyAreasModal.emit(planning);
  }

  getLanguageNameById(languageId: number): string {
    return applicationLanguages.find((x) => x.id === languageId).text;
  }
}
