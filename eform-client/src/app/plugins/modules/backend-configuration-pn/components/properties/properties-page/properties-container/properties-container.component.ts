import {Component, OnDestroy, OnInit} from '@angular/core';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {Subject, Subscription} from 'rxjs';
import {EntityItemModel, Paged} from 'src/app/common/models';
import {AuthStateService} from 'src/app/common/store';
import {
  PropertyCreateModalComponent,
  PropertyDeleteModalComponent,
  PropertyDocxReportModalComponent,
  PropertyEditModalComponent,
  AreaRuleEntityListModalComponent
} from '../../../';
import {
  PropertyCreateModel,
  PropertyModel,
  PropertyUpdateModel,
} from '../../../../models';
import {BackendConfigurationPnPropertiesService} from '../../../../services';
import {PropertiesStateService} from '../../store';
import {debounceTime} from 'rxjs/operators';
import {EntitySelectService} from 'src/app/common/services';
import {MtxGridColumn} from '@ng-matero/extensions/grid';
import {TranslateService} from '@ngx-translate/core';
import {MatDialog, MatDialogRef} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {dialogConfigHelper} from 'src/app/common/helpers';

@AutoUnsubscribe()
@Component({
    selector: 'app-properties-container',
    templateUrl: './properties-container.component.html',
    styleUrls: ['./properties-container.component.scss'],
    standalone: false
})
export class PropertiesContainerComponent implements OnInit, OnDestroy {
  isFarms: boolean = false;
  tableHeaders: MtxGridColumn[] = [
    {
      header: this.translateService.stream('Id'),
      field: 'id',
      sortProp: {id: 'Id'},
      sortable: true,
    },
    {
      header: this.translateService.stream('Property name'),
      sortProp: {id: 'Name'},
      field: 'name',
      sortable: true,
    },
    {
      header: this.translateService.stream('CVR Number'),
      field: 'cvr',
      sortable: true,
      sortProp: {id: 'CVR'},
    },
    {
      header: this.translateService.stream('CHR Number'),
      field: 'chr',
      sortProp: {id: 'CHR'},
      sortable: true,
      hide: this.isFarms,
    },
    {
      header: this.translateService.stream('Property address'),
      field: 'address',
      sortProp: {id: 'Address'},
      sortable: true,
    },
    {
      header: this.translateService.stream('Actions'),
      field: 'actions',
    },
  ];
  adminTableHeaders: MtxGridColumn[] = [
    {
      header: this.translateService.stream('Id'),
      field: 'id',
      sortProp: {id: 'Id'},
      sortable: true,
    },
    {
      header: this.translateService.stream('Property name'),
      sortProp: {id: 'Name'},
      field: 'name',
      sortable: true,
    },
    {
      header: this.translateService.stream('CVR Number'),
      field: 'cvr',
      sortable: true,
      sortProp: {id: 'CVR'},
    },
    {
      header: this.translateService.stream('CHR Number'),
      field: 'chr',
      sortProp: {id: 'CHR'},
      sortable: true,
      hide: this.isFarms,
    },
    {
      header: this.translateService.stream('Property address'),
      field: 'address',
      sortProp: {id: 'Address'},
      sortable: true,
    },
    {
      header: this.translateService.stream('Compliance'),
      field: 'compliance',
    },
    {
      header: this.translateService.stream('Actions'),
      field: 'actions',
    },
  ];

  nameSearchSubject = new Subject();
  propertiesModel: Paged<PropertyModel> = new Paged<PropertyModel>();

  getPropertiesSub$: Subscription;
  deletePropertySub$: Subscription;
  createPropertySub$: Subscription;
  editPropertySub$: Subscription;
  propertyDeleteSub$: Subscription;
  propertyCreateSub$: Subscription;
  propertyUpdateSub$: Subscription;
  getEntitySelectableGroupSub$: Subscription;
  updateEntitySelectableGroupSub$: Subscription;
  nameSearchSubjectSub$: Subscription;

  constructor(
    private propertiesService: BackendConfigurationPnPropertiesService,
    public propertiesStateService: PropertiesStateService,
    public authStateService: AuthStateService,
    private entitySelectService: EntitySelectService,
    private translateService: TranslateService,
    private dialog: MatDialog,
    private overlay: Overlay,
  ) {
    this.nameSearchSubjectSub$ = this.nameSearchSubject.pipe(debounceTime(500)).subscribe((val) => {
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
        for (let i = 0; i < this.propertiesModel.entities.length; i++) {
          if (this.propertiesModel.entities[i].isFarm) {
            this.isFarms = true;
          }
        }
      });
  }

  showPropertyCreateModal() {
    const modal = this.dialog.open(PropertyCreateModalComponent, {...dialogConfigHelper(this.overlay), minWidth: 400});
    this.propertyCreateSub$ = modal.componentInstance.propertyCreate.subscribe(x => this.onPropertyCreate(x, modal));
  }

  showEditPropertyModal(property: PropertyModel) {
    const modal = this.dialog.open(PropertyEditModalComponent, {...dialogConfigHelper(this.overlay, property), minWidth: 400});
    this.propertyUpdateSub$ = modal.componentInstance.propertyUpdate.subscribe(x => this.onPropertyUpdate(x, modal));
  }

  showDeletePropertyModal(property: PropertyModel) {
    const modal = this.dialog.open(PropertyDeleteModalComponent, {...dialogConfigHelper(this.overlay, property)});
    this.propertyDeleteSub$ = modal.componentInstance.propertyDelete.subscribe(x => this.onPropertyDelete(x, modal));
  }

  onShowDocxReportModal(propertyId: number) {
    this.dialog.open(PropertyDocxReportModalComponent, {...dialogConfigHelper(this.overlay, propertyId)});
  }

  onShowEditEntityListModal(propertyModel: PropertyModel) {
    const modal = this.dialog
      .open(AreaRuleEntityListModalComponent, {...dialogConfigHelper(this.overlay, propertyModel.workorderEntityListId)});
    this.propertyUpdateSub$ = modal.componentInstance.entityListChanged.subscribe(x => this.updateEntityList(propertyModel, x, modal));
  }

  onPropertyCreate(model: PropertyCreateModel, modal: MatDialogRef<PropertyCreateModalComponent>) {
    this.createPropertySub$ = this.propertiesService
      .createProperty(model)
      .subscribe((data) => {
        if (data && data.success) {
          modal.close();
          this.getProperties();
        }
      });
  }

  onPropertyUpdate(model: PropertyUpdateModel, modal: MatDialogRef<PropertyEditModalComponent>) {
    this.editPropertySub$ = this.propertiesService
      .updateProperty(model)
      .subscribe((data) => {
        if (data && data.success) {
          modal.close();
          this.getProperties();
        }
      });
  }

  onPropertyDelete(propertyId: number, modal: MatDialogRef<PropertyDeleteModalComponent>) {
    this.deletePropertySub$ = this.propertiesService
      .deleteProperty(propertyId)
      .subscribe((data) => {
        if (data && data.success) {
          this.getProperties();
          modal.close();
          this.isFarms = false;
        }
      });
  }

  onNameFilterChanged(name: string) {
    this.nameSearchSubject.next(name);
  }

  updateEntityList(propertyModel: PropertyModel, model: Array<EntityItemModel>, modal: MatDialogRef<AreaRuleEntityListModalComponent>) {
    if(propertyModel.workorderEntityListId) {
      this.getEntitySelectableGroupSub$ = this.entitySelectService.getEntitySelectableGroup(propertyModel.workorderEntityListId)
        .subscribe(data => {
          if (data.success) {
            this.updateEntitySelectableGroupSub$ = this.entitySelectService.updateEntitySelectableGroup({
              entityItemModels: model,
              groupUid: +data.model.microtingUUID,
              ...data.model
            }).subscribe(x => {
              if (x.success) {
                modal.close();
              }
            });
          }
        });
    }
  }

  ngOnDestroy(): void {}
}
