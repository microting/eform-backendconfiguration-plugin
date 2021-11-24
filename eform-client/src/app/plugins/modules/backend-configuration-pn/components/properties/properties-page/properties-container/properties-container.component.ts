import { Component, OnDestroy, OnInit, ViewChild } from '@angular/core';
import { AutoUnsubscribe } from 'ngx-auto-unsubscribe';
import { Subject, Subscription } from 'rxjs';
import { Paged } from 'src/app/common/models';
import { AuthStateService } from 'src/app/common/store';
import { PropertyAreasViewModalComponent } from 'src/app/plugins/modules/backend-configuration-pn/components';
import {
  PropertyCreateModalComponent,
  PropertyDeleteModalComponent,
  PropertyAreasEditModalComponent,
  PropertyEditModalComponent,
} from '../../property-actions';
import { BackendConfigurationPnClaims } from '../../../../enums';
import {
  PropertyAreaModel,
  PropertyAreasUpdateModel,
  PropertyCreateModel,
  PropertyModel,
  PropertyUpdateModel,
} from '../../../../models';
import { BackendConfigurationPnPropertiesService } from '../../../../services';
import { PropertiesStateService } from '../../store';
import { debounceTime } from 'rxjs/operators';

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
  @ViewChild('viewPropertyAreasModal', { static: false })
  viewPropertyAreasModal: PropertyAreasViewModalComponent;
  @ViewChild('editPropertyAreasModal', { static: false })
  editPropertyAreasModal: PropertyAreasEditModalComponent;

  // descriptionSearchSubject = new Subject();
  nameSearchSubject = new Subject();
  propertiesModel: Paged<PropertyModel> = new Paged<PropertyModel>();

  getPropertiesSub$: Subscription;
  getPropertyAreasSub$: Subscription;
  updatePropertyAreasSub$: Subscription;
  createPropertySub$: Subscription;
  editPropertySub$: Subscription;
  deletePropertySub$: Subscription;

  constructor(
    private propertiesService: BackendConfigurationPnPropertiesService,
    public propertiesStateService: PropertiesStateService,
    public authStateService: AuthStateService
  ) {
    this.nameSearchSubject.pipe(debounceTime(500)).subscribe((val) => {
      this.propertiesStateService.updateNameFilter(val.toString());
      this.getProperties();
    });
  }

  get backendConfigurationPnClaims() {
    return BackendConfigurationPnClaims;
  }

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

  getAndShowPropertyAreas(property: PropertyModel, showModal: 'view' | 'edit') {
    this.getPropertyAreasSub$ = this.propertiesService
      .getPropertyAreas(property.id)
      .subscribe((data) => {
        if (showModal === 'view') {
          this.viewPropertyAreasModal.show(property, data.model);
        } else if (showModal === 'edit') {
          this.editPropertyAreasModal.show(property, data.model);
        }
      });
  }

  onUpdatePropertyAreas(model: PropertyAreasUpdateModel) {
    this.updatePropertyAreasSub$ = this.propertiesService
      .updatePropertyAreas(model)
      .subscribe(() => {
        this.editPropertyAreasModal.hide();
      });
  }

  showPropertyCreateModal() {
    this.createPropertyModal.show();
  }

  showEditPropertyAreasModal(property: PropertyModel) {
    this.getAndShowPropertyAreas(property, 'edit');
  }

  showViewPropertyAreasModal(property: PropertyModel) {
    this.getAndShowPropertyAreas(property, 'view');
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
}
