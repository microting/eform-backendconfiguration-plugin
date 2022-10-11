import {NgModule} from '@angular/core';
import {RouterModule, Routes} from '@angular/router';
import {
  BackendConfigurationCasePageComponent
} from 'src/app/plugins/modules/backend-configuration-pn/components/backend-configuration-case/backend-configuration-case-page/backend-configuration-case-page.component';

const routes: Routes = [
  {path: ':id/:templateId/:planningId/:dateFrom/:dateTo', component: BackendConfigurationCasePageComponent}
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule]
})
export class BackendConfigurationCaseRoutingModule { }
