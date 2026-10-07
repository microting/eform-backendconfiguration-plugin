import {Component, inject} from '@angular/core';
import {Store} from '@ngrx/store';
import {selectCurrentUserIsAdmin} from 'src/app/state';

/**
 * Indbakke | Arkiv | Indstillinger e-mail. The inbox tabs show only to an admin;
 * the API enforces the same admin role on every inbox route.
 */
@Component({
  selector: 'app-archive-section-nav',
  templateUrl: './archive-section-nav.component.html',
  standalone: false,
})
export class ArchiveSectionNavComponent {
  private store = inject(Store);

  readonly canUseInbox$ = this.store.select(selectCurrentUserIsAdmin);
}
