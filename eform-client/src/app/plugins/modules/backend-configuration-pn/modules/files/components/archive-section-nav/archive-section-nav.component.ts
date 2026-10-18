import {Component, inject} from '@angular/core';
import {Store} from '@ngrx/store';
import {selectCurrentUserIsAdmin} from 'src/app/state';
import {NgIf, AsyncPipe} from '@angular/common';
import {TranslatePipe} from '@ngx-translate/core';
import {RouterLink, RouterLinkActive} from '@angular/router';

/**
 * Indbakke | Arkiv | Indstillinger e-mail. The inbox tabs show only to an admin;
 * the API enforces the same admin role on every inbox route.
 */
@Component({
  selector: 'app-archive-section-nav',
  imports: [NgIf, AsyncPipe, TranslatePipe, RouterLink, RouterLinkActive],
  templateUrl: './archive-section-nav.component.html',
})
export class ArchiveSectionNavComponent {
  private store = inject(Store);

  readonly canUseInbox$ = this.store.select(selectCurrentUserIsAdmin);
}
