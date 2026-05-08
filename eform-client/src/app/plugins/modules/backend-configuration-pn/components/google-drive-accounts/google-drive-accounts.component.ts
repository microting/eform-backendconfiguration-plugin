import {Component, OnInit, inject} from '@angular/core';
import {TranslateService} from '@ngx-translate/core';
import {ToastrService} from 'ngx-toastr';
import {GoogleDriveAccount} from '../../models/calendar';
import {BackendConfigurationPnGoogleDriveService} from '../../services';

/**
 * PR-8 settings panel — lists the user's connected Google Drive accounts
 * and lets them disconnect one.
 *
 * Routed via <code>/plugins/backend-configuration-pn/google-drive-accounts</code>.
 * The panel is intentionally a plain page (not a modal) so the URL is
 * shareable + back-button-friendly: a user who hits "Disconnect" mid-flow
 * shouldn't lose state if the modal closes unexpectedly.
 *
 * Disconnect uses <code>window.confirm</code> (mirroring the existing
 * delete-attachment pattern in <code>task-create-edit-modal</code>) rather
 * than a custom MatDialog. The plugin doesn't have a shared confirm-modal
 * convention — adding one for a single call site would balloon the surface.
 */
@Component({
  standalone: false,
  selector: 'app-google-drive-accounts',
  template: `
    <div class="gd-accounts">
      <h2>{{ 'Connected Google Drive accounts' | translate }}</h2>

      <div *ngIf="loading" class="gd-accounts__loading">
        <mat-spinner diameter="32"></mat-spinner>
      </div>

      <p *ngIf="!loading && accounts.length === 0" class="gd-accounts__empty">
        {{ 'No Google Drive accounts connected.' | translate }}
      </p>

      <mat-list *ngIf="!loading && accounts.length > 0">
        <mat-list-item *ngFor="let a of accounts"
                       class="gd-accounts__row"
                       [class.gd-accounts__row--revoked]="!a.isActive">
          <mat-icon matListItemIcon>add_to_drive</mat-icon>
          <div matListItemTitle class="gd-accounts__title">
            <span>{{ a.email }}</span>
            <span *ngIf="!a.isActive" class="gd-accounts__badge gd-accounts__badge--revoked">
              {{ 'Google Drive disconnected' | translate }}
            </span>
          </div>
          <div matListItemLine class="gd-accounts__meta">
            <span>{{ 'Connected on' | translate }} {{ a.connectedAt | date:'medium' }}</span>
            <span *ngIf="a.lastUsedAt"> · {{ 'Last used' | translate }} {{ a.lastUsedAt | date:'medium' }}</span>
          </div>
          <button mat-button color="warn"
                  *ngIf="a.isActive"
                  [disabled]="disconnectingId === a.id"
                  (click)="onDisconnect(a)">
            {{ 'Disconnect' | translate }}
          </button>
        </mat-list-item>
      </mat-list>
    </div>
  `,
  styles: [`
    .gd-accounts { padding: 24px; max-width: 800px; }
    .gd-accounts h2 { margin-bottom: 16px; }
    .gd-accounts__loading { display: flex; justify-content: center; padding: 32px; }
    .gd-accounts__empty { color: rgba(0, 0, 0, 0.6); padding: 16px 0; }
    .gd-accounts__row--revoked { opacity: 0.65; }
    .gd-accounts__title { display: flex; align-items: center; gap: 8px; }
    .gd-accounts__meta { color: rgba(0, 0, 0, 0.6); font-size: 12px; }
    .gd-accounts__badge {
      display: inline-block;
      padding: 2px 8px;
      border-radius: 8px;
      font-size: 11px;
      font-weight: 500;
    }
    .gd-accounts__badge--revoked {
      background-color: #fde7e7;
      color: #c62828;
    }
  `],
})
export class GoogleDriveAccountsComponent implements OnInit {
  private googleDriveService = inject(BackendConfigurationPnGoogleDriveService);
  private translate = inject(TranslateService);
  private toastr = inject(ToastrService);

  accounts: GoogleDriveAccount[] = [];
  loading = false;
  disconnectingId: number | null = null;

  ngOnInit(): void {
    this.reload();
  }

  private reload(): void {
    this.loading = true;
    this.googleDriveService.getAccounts().subscribe({
      next: (op) => {
        this.loading = false;
        if (op?.success && op.model) {
          this.accounts = op.model;
        } else {
          this.accounts = [];
          if (op?.message) {
            this.toastr.error(op.message);
          }
        }
      },
      error: () => {
        this.loading = false;
        this.accounts = [];
      },
    });
  }

  onDisconnect(account: GoogleDriveAccount): void {
    const message = this.translate.instant(
      'Disconnecting will stop sync for any Drive-attached files.',
    );
    const title = this.translate.instant('Disconnect Google Drive?');
    if (!window.confirm(`${title}\n\n${account.email}\n\n${message}`)) return;

    this.disconnectingId = account.id;
    this.googleDriveService.disconnect(account.id).subscribe({
      next: (op) => {
        this.disconnectingId = null;
        if (op?.success) {
          this.toastr.success(this.translate.instant('Google Drive disconnected'));
          this.reload();
        } else if (op?.message) {
          this.toastr.error(op.message);
        }
      },
      error: () => {
        this.disconnectingId = null;
        this.toastr.error(this.translate.instant('GenericError'));
      },
    });
  }
}
