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
import * as R from 'ramda';
import { PropertiesStateService } from 'src/app/plugins/modules/backend-configuration-pn/components/properties/store';

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
  @Output()
  sortUpdated: EventEmitter<void> = new EventEmitter<void>();

  tableHeaders: TableHeaderElementModel[] = [
    { name: 'Id', elementId: 'idTableHeader', sortable: true },
    { name: 'Name', elementId: 'nameTableHeader', sortable: true },
    {
      name: 'CVR',
      visibleName: 'CVR Number',
      elementId: 'cvrNumberTableHeader',
      sortable: true,
    },
    {
      name: 'CHR',
      visibleName: 'CHR Number',
      elementId: 'chrNumberTableHeader',
      sortable: true,
    },
    {
      name: 'Address',
      elementId: 'addressTableHeader',
      sortable: true,
    },
    { name: 'Languages', elementId: 'languagesTableHeader', sortable: false },
    { name: 'Actions', elementId: '', sortable: false },
  ];

  get backendConfigurationPnClaims() {
    return BackendConfigurationPnClaims;
  }

  constructor(public propertiesStateService: PropertiesStateService) {}

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

  getLanguages(property: PropertyModel): string {
    let languages = [];
    for (const language of property.languages) {
      languages = [...languages, this.getLanguageNameById(language.id)];
    }
    return R.join(' | ', languages);
  }

  sortTable(sort: string) {
    this.propertiesStateService.onSortTable(sort);
    this.sortUpdated.emit();
  }
}
