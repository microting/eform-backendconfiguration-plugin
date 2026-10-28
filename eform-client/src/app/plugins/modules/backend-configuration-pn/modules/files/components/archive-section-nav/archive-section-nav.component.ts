import {Component, inject} from '@angular/core';
import {map} from 'rxjs';
import {NgIf, AsyncPipe} from '@angular/common';
import {TranslatePipe} from '@ngx-translate/core';
import {RouterLink, RouterLinkActive} from '@angular/router';
import {ArchiveCapabilitiesService} from '../../services/archive-capabilities.service';

/**
 * Indbakke | Arkiv | Indstillinger e-mail. The inbox tabs show to admins and Leders (capabilities.canUseInbox);
 * the API enforces the same with [ArchiveManager] on every inbox route.
 */
@Component({
  selector: 'app-archive-section-nav',
  imports: [NgIf, AsyncPipe, TranslatePipe, RouterLink, RouterLinkActive],
  templateUrl: './archive-section-nav.component.html',
})
export class ArchiveSectionNavComponent {
  readonly canUseInbox$ = inject(ArchiveCapabilitiesService).capabilities$.pipe(map(c => c.canUseInbox));
}
