import {RouterModule, Routes} from '@angular/router';
import {ReportContainerComponent} from './components';
import {AuthGuard} from 'src/app/common/guards';
import {NgModule} from '@angular/core';

const routes: Routes = [
  {
    path: '',
    component: ReportContainerComponent,
    canActivate: [AuthGuard],
  }
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule]
})
export class ReportsRouting {
}
