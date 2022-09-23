import {RouterModule, Routes} from '@angular/router';
import {
  DocumentsContainerComponent
} from 'src/app/plugins/modules/backend-configuration-pn/modules/documents/components/documents-container/documents-container.component';
import {NgModule} from '@angular/core';

export const routes: Routes = [
  {
    path: '',
    component: DocumentsContainerComponent,
  },
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule],
})
export class DocumentsRouting {}
