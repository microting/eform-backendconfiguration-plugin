import {ChangeDetectionStrategy, Component, EventEmitter, Input, OnInit, Output, ViewChild,} from '@angular/core';
import {AdvEntitySelectableItemModel, Paged, TableHeaderElementModel} from 'src/app/common/models';
import {PropertyModel} from '../../../../models/properties';
import {applicationLanguages} from 'src/app/common/const';
import * as R from 'ramda';
import {PropertiesStateService} from '../../store';
import {PropertyCompliancesColorBadgesEnum} from 'src/app/plugins/modules/backend-configuration-pn/enums';
import {AuthStateService} from 'src/app/common/store';
import {CompliancesStateService} from 'src/app/plugins/modules/backend-configuration-pn/modules/compliance/components/store';
import {
  BackendConfigurationPnCompliancesMethods,
  BackendConfigurationPnCompliancesService,
  BackendConfigurationPnPropertiesService
} from 'src/app/plugins/modules/backend-configuration-pn/services';
import {Observable, Subscription} from 'rxjs';
import {AreaRuleEntityListModalComponent} from 'src/app/plugins/modules/backend-configuration-pn/components';
import {EntitySelectService} from 'src/app/common/services';

@Component({
  selector: 'app-properties-table',
  templateUrl: './properties-table.component.html',
  styleUrls: ['./properties-table.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PropertiesTableComponent implements OnInit {
  @Input() propertiesModel: Paged<PropertyModel> = new Paged<PropertyModel>();
  @Input() tableHeaders: TableHeaderElementModel[];
  @Input() isFarms: boolean = false;
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
  @Output()
  showDocxReportModal: EventEmitter<number> = new EventEmitter<number>();
  @Output()
  showEditEntityListModal: EventEmitter<PropertyModel> = new EventEmitter<PropertyModel>();

  get propertyCompliancesColorBadgesEnum() {
    return PropertyCompliancesColorBadgesEnum;
  }

  constructor(public propertiesStateService: PropertiesStateService,
              private entitySelectService: EntitySelectService,
              public authStateService: AuthStateService,) {}

  ngOnInit(): void { }

  onShowDeletePropertyModal(propertyModel: PropertyModel) {
    this.showDeletePropertyModal.emit(propertyModel);
  }

  onShowEditPropertyModal(propertyModel: PropertyModel) {
    this.showEditPropertyModal.emit(propertyModel);
  }

  sortTable(sort: string) {
    this.propertiesStateService.onSortTable(sort);
    this.sortUpdated.emit();
  }


  getColorBadge(compliance: PropertyCompliancesColorBadgesEnum): string {
    switch (compliance) {
      case PropertyCompliancesColorBadgesEnum.Success:
        return 'btn-success';
      case PropertyCompliancesColorBadgesEnum.Danger:
        return 'btn-danger';
      case PropertyCompliancesColorBadgesEnum.Warning:
        return 'btn-warning';
      default:
        return 'btn-success';
    }
  }

  onShowDocxReportModal(propertyId: number){
    this.showDocxReportModal.emit(propertyId);
  }

  onShowEditEntityListModal(propertyModel: PropertyModel) {
    this.showEditEntityListModal.emit(propertyModel);
  }
}
