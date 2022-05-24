import { Component, OnDestroy, OnInit, ViewChild } from '@angular/core';
import { AutoUnsubscribe } from 'ngx-auto-unsubscribe';
import { Subject, Subscription } from 'rxjs';
import {AdvEntitySelectableItemModel, Paged} from 'src/app/common/models';
import { AuthStateService } from 'src/app/common/store';
import {
  PropertyCreateModalComponent,
  PropertyDeleteModalComponent, PropertyDocxReportModalComponent,
  PropertyEditModalComponent,
} from '../../property-actions';
import {
  PropertyCreateModel,
  PropertyModel,
  PropertyUpdateModel,
} from '../../../../models';
import { BackendConfigurationPnPropertiesService} from '../../../../services';
import { PropertiesStateService } from '../../store';
import { debounceTime } from 'rxjs/operators';
import {EntitySelectService} from 'src/app/common/services';
import {AreaRuleEntityListModalComponent} from 'src/app/plugins/modules/backend-configuration-pn/components';

@AutoUnsubscribe()
@Component({
  selector: 'app-properties-container',
  templateUrl: './properties-container.component.html',
  styleUrls: ['./properties-container.component.scss'],
})
export class PropertiesContainerComponent implements OnInit, OnDestroy {
  @ViewChild('createPropertyModal', { static: false })
  createPropertyModal: PropertyCreateModalComponent;
  @ViewChild('editPropertyModal', { static: false })
  editPropertyModal: PropertyEditModalComponent;
  @ViewChild('deletePropertyModal', { static: false })
  deletePropertyModal: PropertyDeleteModalComponent;
  @ViewChild('docxReportModal', { static: false })
  docxReportModal: PropertyDocxReportModalComponent;
  @ViewChild('entityListEditModal', { static: false })
  entityListEditModal: AreaRuleEntityListModalComponent;
  selectedProperty: PropertyModel;

  nameSearchSubject = new Subject();
  propertiesModel: Paged<PropertyModel> = new Paged<PropertyModel>();

  getPropertiesSub$: Subscription;
  createPropertySub$: Subscription;
  editPropertySub$: Subscription;
  deletePropertySub$: Subscription;

  constructor(
    private propertiesService: BackendConfigurationPnPropertiesService,
    public propertiesStateService: PropertiesStateService,
    public authStateService: AuthStateService,
  private entitySelectService: EntitySelectService
  ) {
    this.nameSearchSubject.pipe(debounceTime(500)).subscribe((val) => {
      this.propertiesStateService.updateNameFilter(val.toString());
      this.getProperties();
    });
  }

  // get backendConfigurationPnClaims() {
  //   return BackendConfigurationPnClaims;
  // }

  ngOnInit() {
    this.getProperties();
  }

  getProperties() {
    this.getPropertiesSub$ = this.propertiesStateService
      .getAllProperties()
      .subscribe((data) => {
        this.propertiesModel = data.model;
      });
  }

  showPropertyCreateModal() {
    this.createPropertyModal.show();
  }

  showEditPropertyModal(property: PropertyModel) {
    this.editPropertyModal.show(property);
  }

  showDeletePropertyModal(property: PropertyModel) {
    this.deletePropertyModal.show(property);
  }

  onPropertyCreate(model: PropertyCreateModel) {
    this.createPropertySub$ = this.propertiesService
      .createProperty(model)
      .subscribe((data) => {
        if (data && data.success) {
          this.getProperties();
          this.createPropertyModal.hide();
        }
      });
  }

  onPropertyUpdate(model: PropertyUpdateModel) {
    this.editPropertySub$ = this.propertiesService
      .updateProperty(model)
      .subscribe((data) => {
        if (data && data.success) {
          this.getProperties();
          this.editPropertyModal.hide();
        }
      });
  }

  onPropertyDelete(propertyId: number) {
    this.deletePropertySub$ = this.propertiesService
      .deleteProperty(propertyId)
      .subscribe((data) => {
        if (data && data.success) {
          this.getProperties();
          this.deletePropertyModal.hide();
        }
      });
  }

  onShowDocxReportModal(propertyId: number) {
    this.docxReportModal.show(propertyId);
  }

  ngOnDestroy(): void {}

  // onPageSizeChanged(newPageSize: number) {
  //   this.propertiesStateService.updatePageSize(newPageSize);
  //   this.getProperties();
  // }

  // sortTable(sort: string) {
  //   this.propertiesStateService.onSortTable(sort);
  //   this.getProperties();
  // }
  //
  // changePage(newPage: any) {
  //   this.propertiesStateService.changePage(newPage);
  //   this.getProperties();
  // }

  onNameFilterChanged(name: string) {
    this.nameSearchSubject.next(name);
  }

  onShowEditEntityListModal(propertyModel: PropertyModel) {
    this.selectedProperty = propertyModel;
    this.entityListEditModal.show(propertyModel.workorderEntityListId);
  }

  updateEntityList(model: Array<AdvEntitySelectableItemModel>) {
    this.entitySelectService.getEntitySelectableGroup(this.selectedProperty.workorderEntityListId)
      .subscribe(data => {
        if (data.success) {
          this.entitySelectService.updateEntitySelectableGroup({
            advEntitySelectableItemModels: model,
            groupUid: +data.model.microtingUUID,
            ...data.model
          }).subscribe(x => {
            if (x.success) {
              this.entityListEditModal.hide();
            }
          });
        }
      });
  }
}
