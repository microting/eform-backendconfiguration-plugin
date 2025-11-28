import { Component, OnDestroy, OnInit, inject} from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { AutoUnsubscribe } from 'ngx-auto-unsubscribe';
import { Subscription } from 'rxjs';
import { AuthStateService } from 'src/app/common/store';
import { ComplianceModel, PropertyModel } from '../../../../models';
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
    standalone: false
})
export class CompliancesContainerComponent implements OnInit, OnDestroy {
  public authStateService = inject(AuthStateService);
  private route = inject(ActivatedRoute);
  private backendConfigurationPnPropertiesService = inject(BackendConfigurationPnPropertiesService);
  private translateService = inject(TranslateService);
  private compliancesStateService = inject(CompliancesStateService);

  breadcrumbs = [
    {
      name: '',
      href: '/plugins/backend-configuration-pn/properties',
    },
    // { name: '', href: '' },
    { name: '' },
  ];
  selectedProperty: PropertyModel;
  complianceList: ComplianceModel[] = [];
  isComplianceThirtyDays: boolean;

  getAllPropertiesDictionarySub$: Subscription;
  getTranslateSub$: Subscription;
  routerSub$: Subscription;
  getAllCompliancesSub$: Subscription;

  

  ngOnInit() {
    this.routerSub$ = this.route.params.subscribe((params) => {
      this.getTranslateSub$ = this.translateService
        .get('Properties')
        .subscribe((translate) => (this.breadcrumbs[0].name = translate));
      const selectedPropertyId = +params['propertyId'];
      this.getProperty(selectedPropertyId);
      if (params['complianceStatusThirty'] !== undefined) {
        this.isComplianceThirtyDays = true;
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
          this.complianceList = data.model.entities;
        }
      });
  }

  ngOnDestroy(): void {}
}
