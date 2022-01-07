import { Component, OnDestroy, OnInit } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { AutoUnsubscribe } from 'ngx-auto-unsubscribe';
import { Subscription } from 'rxjs';
import { AuthStateService } from 'src/app/common/store';
import { CompliancesModel, PropertyModel } from '../../../../models';
import {
  BackendConfigurationPnPropertiesService,
} from '../../../../services';
import { TranslateService } from '@ngx-translate/core';
import { CompliancesStateService } from '../store';

@AutoUnsubscribe()
@Component({
  selector: 'app-compliances-container',
  templateUrl: './compliances-container.component.html',
  styleUrls: ['./compliances-container.component.scss'],
})
export class CompliancesContainerComponent implements OnInit, OnDestroy {
  breadcrumbs = [
    {
      name: '',
      href: '/plugins/backend-configuration-pn/properties',
    },
    // { name: '', href: '' },
    { name: '' },
  ];
  selectedProperty: PropertyModel;
  compliances: CompliancesModel[] = [];

  getAllPropertiesDictionarySub$: Subscription;
  getTranslateSub$: Subscription;
  routerSub$: Subscription;
  getAllCompliancesSub$: Subscription;

  constructor(
    public authStateService: AuthStateService,
    private route: ActivatedRoute,
    private backendConfigurationPnPropertiesService: BackendConfigurationPnPropertiesService,
    private translateService: TranslateService,
    private compliancesStateService: CompliancesStateService
  ) {}

  ngOnInit() {
    this.routerSub$ = this.route.params.subscribe((params) => {
      this.getTranslateSub$ = this.translateService
        .get('Properties')
        .subscribe((translate) => (this.breadcrumbs[0].name = translate));
      const selectedPropertyId = +params['propertyId'];
      this.getProperty(selectedPropertyId);
      if (params['complianceStatusThirty'] !== undefined) {
        this.getCompliances(selectedPropertyId, true);
      } else {
        this.getCompliances(selectedPropertyId, false);
      }
    });
  }

  private getProperty(selectedPropertyId: number) {
    this.getAllPropertiesDictionarySub$ = this.backendConfigurationPnPropertiesService
      .readProperty(selectedPropertyId)
      .subscribe((data) => {
        if (data && data.success) {
          this.selectedProperty = data.model;
          this.breadcrumbs[1] = { name: this.selectedProperty.name };
          // this.breadcrumbs[2] = {
          //   name: this.selectedProperty.chr,
          // };
        }
      });
  }

  getCompliances(selectedPropertyId: number, thirtyDays: boolean = false) {
    this.getAllCompliancesSub$ = this.compliancesStateService
      .getAllCompliances(selectedPropertyId, thirtyDays)
      .subscribe((data) => {
        if (data && data.success) {
          this.compliances = data.model.entities;
        }
      });
  }

  ngOnDestroy(): void {}
}
