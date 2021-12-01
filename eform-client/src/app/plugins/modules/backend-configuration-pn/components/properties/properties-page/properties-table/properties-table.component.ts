import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  OnInit,
  Output,
} from '@angular/core';
import { Paged, TableHeaderElementModel } from 'src/app/common/models';
import { PropertyModel } from '../../../../models/properties';
import { applicationLanguages } from 'src/app/common/const';
import * as R from 'ramda';
import { PropertiesStateService } from '../../store';
import { PropertyCompliancesColorBadgesEnum } from 'src/app/plugins/modules/backend-configuration-pn/enums';

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
    { name: 'Compliance', sortable: false },
    { name: 'Actions', elementId: '', sortable: false },
  ];

  get propertyCompliancesColorBadgesEnum() {
    return PropertyCompliancesColorBadgesEnum;
  }

  constructor(public propertiesStateService: PropertiesStateService) {}

  ngOnInit(): void {}

  onShowDeletePropertyModal(planning: PropertyModel) {
    this.showDeletePropertyModal.emit(planning);
  }

  onShowEditPropertyModal(planning: PropertyModel) {
    this.showEditPropertyModal.emit(planning);
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

  getColorBadge(property: PropertyModel): string {
    switch (property.compliance) {
      case PropertyCompliancesColorBadgesEnum.Success:
        return 'bg-success';
      case PropertyCompliancesColorBadgesEnum.Danger:
        return 'bg-danger';
      case PropertyCompliancesColorBadgesEnum.Warning:
        return 'bg-warning';
      default:
        return 'bg-success';
    }
  }
}
