import {CommonModule} from '@angular/common';
import {NgModule} from '@angular/core';
import {FormsModule} from '@angular/forms';
import {RouterModule} from '@angular/router';
import {TranslateModule} from '@ngx-translate/core';
import {MatButtonModule} from '@angular/material/button';
import {MatCardModule} from '@angular/material/card';
import {MatDatepickerModule} from '@angular/material/datepicker';
import {MatFormFieldModule} from '@angular/material/form-field';
import {MatIconModule} from '@angular/material/icon';
import {MatInputModule} from '@angular/material/input';
import {MatProgressSpinnerModule} from '@angular/material/progress-spinner';
import {MatTooltipModule} from '@angular/material/tooltip';
import {MtxSelectModule} from '@ng-matero/extensions/select';
import {EformSharedModule} from 'src/app/common/modules/eform-shared/eform-shared.module';
import {ComplianceReportRouting} from './compliance-report.routing';
import {
  ComplianceReportFiltersComponent,
  ComplianceReportPageComponent,
} from './components';
import {ComplianceReportStateService} from './store';

/**
 * The standalone Compliance page (#1160). Lazy child of the plugin root route
 * at `compliance-report` — deliberately NOT under `compliances`, whose routing
 * table declares `:propertyId` first and would swallow any new literal segment.
 */
@NgModule({
  declarations: [ComplianceReportPageComponent, ComplianceReportFiltersComponent],
  imports: [
    CommonModule,
    FormsModule,
    RouterModule,
    TranslateModule,
    ComplianceReportRouting,
    EformSharedModule,
    MatButtonModule,
    MatCardModule,
    MatDatepickerModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatTooltipModule,
    MtxSelectModule,
  ],
  // Module-scoped, not providedIn:'root' — the page's state belongs to this
  // lazy module and nothing outside it has any business reading it.
  providers: [ComplianceReportStateService],
})
export class ComplianceReportModule {}
