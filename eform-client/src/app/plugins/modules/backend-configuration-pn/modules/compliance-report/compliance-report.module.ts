import {CommonModule} from '@angular/common';
import {NgModule} from '@angular/core';
import {FormsModule} from '@angular/forms';
import {RouterModule} from '@angular/router';
import {TranslateModule} from '@ngx-translate/core';
import {OverlayModule} from '@angular/cdk/overlay';
import {PortalModule} from '@angular/cdk/portal';
import {MatButtonModule} from '@angular/material/button';
import {MatCardModule} from '@angular/material/card';
import {MAT_DATE_FORMATS} from '@angular/material/core';
import {MatDatepickerModule} from '@angular/material/datepicker';
import {MatDialogModule} from '@angular/material/dialog';
import {MatFormFieldModule} from '@angular/material/form-field';
import {MatIconModule} from '@angular/material/icon';
import {MatInputModule} from '@angular/material/input';
import {MatProgressSpinnerModule} from '@angular/material/progress-spinner';
import {MatTooltipModule} from '@angular/material/tooltip';
import {MtxSelectModule} from '@ng-matero/extensions/select';
import {EFORM_MAT_DATEFNS_DATE_FORMATS} from 'src/app/common/modules/eform-date-adapter/eform-mat-datefns-date-formats';
import {EformSharedModule} from 'src/app/common/modules/eform-shared/eform-shared.module';
import {CalendarModule} from '../calendar/calendar.module';
import {ComplianceReportRouting} from './compliance-report.routing';
import {
  ComplianceDetailsViewComponent,
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
  declarations: [
    ComplianceReportPageComponent,
    ComplianceReportFiltersComponent,
    ComplianceDetailsViewComponent,
  ],
  imports: [
    CommonModule,
    FormsModule,
    RouterModule,
    TranslateModule,
    ComplianceReportRouting,
    EformSharedModule,
    OverlayModule,
    PortalModule,
    MatButtonModule,
    MatCardModule,
    MatDatepickerModule,
    MatDialogModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatTooltipModule,
    MtxSelectModule,
    // Detaljer (#1165) opens the calendar's completion modal
    // (CalendarCompleteEventModalComponent) rather than re-declaring the whole
    // prepare-complete pipeline. The import is for the COMPONENT only — its
    // providers are an unwanted side effect: an imported NgModule's providers
    // merge into the importer's own injector, so CalendarModule's
    // MAT_DATE_FORMATS override would otherwise reach every component declared
    // here, including the filter bar's date-range input (see the re-provide
    // below).
    //
    // Listed LAST on purpose. CalendarModule pulls in CalendarRouting, whose
    // `path: ''` route would otherwise compete with this module's own; the
    // sibling modules that already import CalendarModule (task-list,
    // calendar-task-list) order it the same way.
    CalendarModule,
  ],
  providers: [
    // Module-scoped, not providedIn:'root' — the page's state belongs to this
    // lazy module and nothing outside it has any business reading it.
    ComplianceReportStateService,
    // Undo CalendarModule's MAT_DATE_FORMATS override for THIS module's own
    // components. Angular flattens an imported module's providers into the
    // importer's injector, imports first and the importing def's own
    // `providers` last (`walkProviderTree`: "First, include providers from any
    // imports" … "Next, include providers listed on the definition itself"),
    // and `R3Injector.processProvider` overwrites the record for a token it has
    // already seen — so this line wins over CalendarModule's.
    //
    // Without it "Sæt periode" (`mat-date-range-input`) renders
    // "tirsdag 11. august" — no YEAR — on a retrospective report whose custom
    // range is the one control that can span a year boundary. The completion
    // modal keeps the calendar's format regardless: it declares it as a
    // COMPONENT provider (calendar-complete-event-modal.component.ts).
    {provide: MAT_DATE_FORMATS, useValue: EFORM_MAT_DATEFNS_DATE_FORMATS},
  ],
})
export class ComplianceReportModule {}
