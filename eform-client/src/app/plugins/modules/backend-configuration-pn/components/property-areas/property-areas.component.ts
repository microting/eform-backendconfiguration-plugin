import { Component, OnDestroy, OnInit, ViewChild } from '@angular/core';
import {
  PropertyAreaModel,
  PropertyAreasUpdateModel,
  PropertyModel,
} from '../../models';
import { Subscription } from 'rxjs';
import { ActivatedRoute } from '@angular/router';
import { BackendConfigurationPnPropertiesService } from '../../services';
import { TranslateService } from '@ngx-translate/core';
import { AutoUnsubscribe } from 'ngx-auto-unsubscribe';
import { PropertyAreasEditModalComponent } from 'src/app/plugins/modules/backend-configuration-pn/components';
import {AuthStateService} from 'src/app/common/store';

@AutoUnsubscribe()
@Component({
  selector: 'app-property-areas',
  templateUrl: './property-areas.component.html',
  styleUrls: ['./property-areas.component.scss'],
})
export class PropertyAreasComponent implements OnInit, OnDestroy {
  @ViewChild('editPropertyAreasModal', { static: false })
  editPropertyAreasModal: PropertyAreasEditModalComponent;
  selectedProperty: PropertyModel = new PropertyModel();
  selectedPropertyAreas: PropertyAreaModel[] = [];
  breadcrumbs = [
    {
      name: '',
      href: '/plugins/backend-configuration-pn/properties',
    },
    { name: '' },
  ];
  disabledAreas: string[] = [
    '13. APV Landbrug',
    '05. Stalde: Halebid og klargøring',
    '21. DANISH Standard',
    '100. Diverse',
  ]

  getTranslateSub$: Subscription;
  routerSub$: Subscription;
  getAllPropertiesDictionarySub$: Subscription;
  getPropertyAreasSub$: Subscription;
  updatePropertyAreasSub$: Subscription;

  constructor(
    public authStateService: AuthStateService,
    private propertiesService: BackendConfigurationPnPropertiesService,
    private route: ActivatedRoute,
    private backendConfigurationPnPropertiesService: BackendConfigurationPnPropertiesService,
    private translateService: TranslateService
  ) {}

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

  show(model: PropertyModel, propertyAreas: PropertyAreaModel[]) {
    this.selectedPropertyAreas = [...propertyAreas];
  }

  private getProperty(selectedPropertyId: number) {
    this.getAllPropertiesDictionarySub$ = this.backendConfigurationPnPropertiesService
      .readProperty(selectedPropertyId)
      .subscribe((data) => {
        if (data && data.success) {
          this.selectedProperty = data.model;
          this.breadcrumbs[1] = { name: this.selectedProperty.name };
          this.getPropertyAreas(selectedPropertyId);
        }
      });
  }

  private getPropertyAreas(selectedPropertyId: number) {
    this.getPropertyAreasSub$ = this.propertiesService
      .getPropertyAreas(selectedPropertyId)
      .subscribe((data) => {
        if (data && data.success) {
          this.selectedPropertyAreas = data.model;
        }
      });
  }

  onUpdatePropertyAreas(model: PropertyAreasUpdateModel) {
    this.updatePropertyAreasSub$ = this.propertiesService
      .updatePropertyAreas(model)
      .subscribe((data) => {
        if (data && data.success) {
          this.editPropertyAreasModal.hide();
          this.getPropertyAreas(this.selectedProperty.id);
        }
      });
  }

  showConfigurePropertyAreas() {
    this.getPropertyAreasSub$ = this.propertiesService
      .getPropertyAreas(this.selectedProperty.id)
      .subscribe((data) => {
        this.editPropertyAreasModal.show(this.selectedProperty, data.model);
      });
  }

  ngOnDestroy() {}
}
