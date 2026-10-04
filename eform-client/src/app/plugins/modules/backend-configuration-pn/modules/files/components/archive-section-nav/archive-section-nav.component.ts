import {Component, inject} from '@angular/core';
import {AuthStateService} from 'src/app/common/store';
import {BackendConfigurationPnClaims} from '../../../../enums';

/**
 * Indbakke | Arkiv | Indstillinger e-mail. The inbox tabs show only with the inbox_enable permission;
 * the API enforces the same permission on every inbox route.
 */
@Component({
  selector: 'app-archive-section-nav',
  templateUrl: './archive-section-nav.component.html',
  standalone: false,
})
export class ArchiveSectionNavComponent {
  private authStateService = inject(AuthStateService);

  get canUseInbox(): boolean {
    return this.authStateService.checkClaim(BackendConfigurationPnClaims.enableInbox);
  }
}
