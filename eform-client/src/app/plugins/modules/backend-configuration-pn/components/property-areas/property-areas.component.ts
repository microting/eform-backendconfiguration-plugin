import {Component, OnDestroy, OnInit,} from '@angular/core';
import {
  PropertyAreaModel,
  PropertyAreasUpdateModel,
  PropertyModel,
} from '../../models';
import {Subscription} from 'rxjs';
import {ActivatedRoute, Router} from '@angular/router';
import {BackendConfigurationPnPropertiesService} from '../../services';
import {TranslateService} from '@ngx-translate/core';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {PropertyAreasEditModalComponent} from '../../components';
import {AuthStateService} from 'src/app/common/store';
import {MtxGridColumn} from '@ng-matero/extensions/grid';
import {MatDialog, MatDialogRef} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {dialogConfigHelper} from 'src/app/common/helpers';
import {selectAuthIsAuth} from 'src/app/state/auth/auth.selector';
import {Store} from '@ngrx/store';

@AutoUnsubscribe()
@Component({
    selector: 'app-property-areas',
    templateUrl: './property-areas.component.html',
    styleUrls: ['./property-areas.component.scss'],
    standalone: false
})
export class PropertyAreasComponent implements OnInit, OnDestroy {
  selectedProperty: PropertyModel = new PropertyModel();
  selectedPropertyAreas: Array<PropertyAreaModel> = new Array<PropertyAreaModel>();
  breadcrumbs = [
    {
      name: '',
      href: '/plugins/backend-configuration-pn/properties',
    },
    {name: ''},
  ];
  disabledAreas: string[] = [
    '21. DANISH Standard',
    '100. Diverse',
    '25. Kemisk APV',
    '00. Aflæsninger, målinger, forbrug og fækale uheld'
  ];

  tableHeaders: MtxGridColumn[] = [
    {
      header: this.translateService.stream('Control area'),
      field: 'name',
    },
    // {
    //   header: this.translateService.stream('Description'),
    //   field: 'description',
    //   formatter: (rowData: PropertyAreaModel) =>
    //     rowData.description ? `<a href="${rowData.description}" target="_blank">${this.translateService.instant('Read more')}</a>` : ``,
    // },
    {
      header: this.translateService.stream('Status'),
      field: 'status',
      formatter: (rowData: PropertyAreaModel) => this.translateService.instant(this.getAreaPlanningStatus(rowData)),
    },
    {
      header: this.translateService.stream('Actions'),
      field: 'book',
      // buttons: [
      //   {
      //     type: 'icon',
      //     color: 'accent',
      //     icon: 'menu_book',
      //     click: (rowData: PropertyAreaModel) => this.router
      //       .navigate(['../../area-rules/', rowData.id, this.selectedProperty.id], {relativeTo: this.route}),
      //     tooltip: this.translateService.stream('Open area planning'),
      //   }
      // ]
    },
  ];

  getTranslateSub$: Subscription;
  routerSub$: Subscription;
  getAllPropertiesDictionarySub$: Subscription;
  getPropertyAreasSub$: Subscription;
  updatePropertyAreasSub$: Subscription;
  onUpdatePropertyAreasSub$: Subscription;
  public isAuth$ = this.store.select(selectAuthIsAuth);
  public selectAuthIsAdmin$ = this.store.select(selectAuthIsAuth);

  constructor(
    private store: Store,
    public route: ActivatedRoute,
    public router: Router,
    private dialog: MatDialog,
    private overlay: Overlay,
    private translateService: TranslateService,
    public authStateService: AuthStateService,
    private propertiesService: BackendConfigurationPnPropertiesService,
    private backendConfigurationPnPropertiesService: BackendConfigurationPnPropertiesService,
  ) {
  }

  ngOnInit() {
    this.routerSub$ = this.route.params.subscribe((params) => {
      this.getTranslateSub$ = this.translateService
        .get('Properties')
        .subscribe((translate) => (this.breadcrumbs[0].name = translate));
      const selectedPropertyId = +params['propertyId'];
      this.getProperty(selectedPropertyId);
    });
  }

  getAreaPlanningStatus(area: PropertyAreaModel) {
    return area.status ? 'ON' : 'OFF';
  }

  getProperty(selectedPropertyId: number) {
    this.getAllPropertiesDictionarySub$ = this.backendConfigurationPnPropertiesService
      .readProperty(selectedPropertyId)
      .subscribe((data) => {
        if (data && data.success) {
          this.selectedProperty = data.model;
          this.breadcrumbs[1] = {name: this.selectedProperty.name};
          this.getPropertyAreas(selectedPropertyId);
        }
      });
  }

  getPropertyAreas(selectedPropertyId: number) {
    this.getPropertyAreasSub$ = this.propertiesService
      .getPropertyAreas(selectedPropertyId)
      .subscribe((data) => {
        if (data && data.success) {
          let isAdmin = false;
          this.selectAuthIsAdmin$.subscribe(x => isAdmin = x);
          this.selectedPropertyAreas = data.model
            .filter(x => (!this.disabledAreas.includes(x.name) || isAdmin) && x.activated);
        }
      });
  }

  onUpdatePropertyAreas(model: PropertyAreasUpdateModel, modal: MatDialogRef<PropertyAreasEditModalComponent>) {
    this.updatePropertyAreasSub$ = this.propertiesService
      .updatePropertyAreas(model)
      .subscribe((data) => {
        if (data && data.success) {
          modal.close();
          this.getPropertyAreas(this.selectedProperty.id);
        }
      });
  }

  showConfigurePropertyAreas() {
    this.getPropertyAreasSub$ = this.propertiesService
      .getPropertyAreas(this.selectedProperty.id)
      .subscribe((data) => {
        const modal = this.dialog.open(PropertyAreasEditModalComponent,
          {...dialogConfigHelper(this.overlay, {selectedProperty: this.selectedProperty, propertyAreas: data.model}), minWidth: 400});
        this.onUpdatePropertyAreasSub$ = modal.componentInstance.updatePropertyAreas
          .subscribe(x => this.onUpdatePropertyAreas(x, modal));
      });
  }

  ngOnDestroy() {
  }
}
