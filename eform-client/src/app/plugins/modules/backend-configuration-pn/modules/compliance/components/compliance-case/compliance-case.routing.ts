import {NgModule} from '@angular/core';
import {RouterModule, Routes} from '@angular/router';
import {
  ComplianceCasePageComponent
} from '../compliance-case/compliance-case-page/compliance-case-page.component';

const routes: Routes = [
  {path: ':sdkCaseId/:templateId/:propertyId/:deadline/:thirtyDays/:complianceId/:siteId', component: ComplianceCasePageComponent}
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule]
})
export class ComplianceCaseRouting { }
