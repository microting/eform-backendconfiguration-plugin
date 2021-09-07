import { Component, OnDestroy, OnInit, ViewChild } from '@angular/core';
import { AutoUnsubscribe } from 'ngx-auto-unsubscribe';
import { Subscription } from 'rxjs';
import { Paged } from 'src/app/common/models';
import { AuthStateService } from 'src/app/common/store';
import {
  PropertyCreateModalComponent,
  PropertyDeleteModalComponent,
  PropertyEditModalComponent,
} from '../../property-actions';
import { BackendConfigurationPnClaims } from '../../../../enums';
import {
  PropertyCreateModel,
  PropertyModel,
  PropertyUpdateModel,
} from '../../../../models';
import { BackendConfigurationPnPropertiesService } from '../../../../services';
import { PropertiesStateService } from '../../store';

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

  // descriptionSearchSubject = new Subject();
  // nameSearchSubject = new Subject();
  propertiesModel: Paged<PropertyModel> = new Paged<PropertyModel>();

  getPropertiesSub$: Subscription;
  createPropertySub$: Subscription;
  editPropertySub$: Subscription;
  deletePropertySub$: Subscription;

  constructor(
    private propertiesService: BackendConfigurationPnPropertiesService,
    public propertiesStateService: PropertiesStateService,
    public authStateService: AuthStateService
  ) {
    // this.nameSearchSubject.pipe(debounceTime(500)).subscribe((val) => {
    //   this.propertiesStateService.updateNameFilter(val.toString());
    //   this.getProperties();
    // });
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

  // onNameFilterChanged(name: string) {
  //   this.nameSearchSubject.next(name);
  // }
}
