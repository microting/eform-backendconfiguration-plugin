import {NgModule} from '@angular/core';
import {MAT_DATE_FORMATS} from '@angular/material/core';
import {RouterModule, Routes} from '@angular/router';
import {EFORM_MAT_DATEFNS_DATE_FORMATS} from 'src/app/common/modules/eform-date-adapter/eform-mat-datefns-date-formats';
import {TailBiteShellComponent} from './components/tail-bite-shell/tail-bite-shell.component';

/**
 * `tail-bite/<propertyId>/<tab>`. The pages are standalone components loaded on demand; the `:propertyId` route
 * is componentless, so the pages inherit its parameter.
 */
export const routes: Routes = [
  {
    path: '',
    component: TailBiteShellComponent,
    // The platform's date format for every date picker in the area (the host provides the adapter).
    providers: [{provide: MAT_DATE_FORMATS, useValue: EFORM_MAT_DATEFNS_DATE_FORMATS}],
    children: [
      {
        path: ':propertyId',
        children: [
          {path: '', pathMatch: 'full', redirectTo: 'outbreaks'},
          {
            path: 'outbreaks',
            loadComponent: () => import('./components/tail-bite-outbreaks-page/tail-bite-outbreaks-page.component')
              .then((m) => m.TailBiteOutbreaksPageComponent),
          },
          {
            path: 'outbreaks/:id',
            loadComponent: () => import('./components/tail-bite-outbreak-detail/tail-bite-outbreak-detail.component')
              .then((m) => m.TailBiteOutbreakDetailComponent),
          },
          {
            path: 'locations',
            loadComponent: () => import('./components/tail-bite-locations-page/tail-bite-locations-page.component')
              .then((m) => m.TailBiteLocationsPageComponent),
          },
          {
            path: 'rules',
            loadComponent: () => import('./components/tail-bite-rules-page/tail-bite-rules-page.component')
              .then((m) => m.TailBiteRulesPageComponent),
          },
          {
            path: 'action-types',
            loadComponent: () => import('./components/tail-bite-action-types-page/tail-bite-action-types-page.component')
              .then((m) => m.TailBiteActionTypesPageComponent),
          },
        ],
      },
    ],
  },
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule],
})
export class TailBiteRouting {}
