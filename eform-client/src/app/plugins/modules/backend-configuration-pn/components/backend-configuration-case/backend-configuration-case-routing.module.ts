import {NgModule} from '@angular/core';
import {RouterModule, Routes} from '@angular/router';
import {
  BackendConfigurationCasePageComponent
} from './backend-configuration-case-page/backend-configuration-case-page.component';

const routes: Routes = [
  {path: ':id/:templateId/:planningId', component: BackendConfigurationCasePageComponent}
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule]
})
export class BackendConfigurationCaseRoutingModule { }
