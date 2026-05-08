import {Component, OnInit} from '@angular/core';

/**
 * Tiny landing page rendered inside the Google OAuth popup after the proxy
 * redirects back to <code>/plugins/backend-configuration-pn/google-drive-oauth-finish</code>.
 *
 * Purpose: post a `gd_oauth_done` message back to <code>window.opener</code> (the
 * calendar attach-file modal) and close itself, completing the popup-OAuth
 * dance. Burying this in the calendar route would fire the postMessage on
 * every navigation; a dedicated route keeps the contract narrow.
 *
 * Query params (set by <c>GoogleDriveController.OAuthFinish</c>):
 *   - <c>?gdrive_success=true</c>            — happy path
 *   - <c>?gdrive_err=&lt;reason&gt;</c>      — error path (nonce_missing, etc.)
 */
@Component({
  standalone: false,
  selector: 'app-google-drive-oauth-finish',
  template: `
    <div class="gd-oauth-finish">
      <h3 *ngIf="success">{{ 'Google Drive connected' | translate }}</h3>
      <h3 *ngIf="!success">{{ 'Could not connect to Google Drive' | translate }}</h3>
      <p *ngIf="errorReason">{{ errorReason }}</p>
      <p>{{ 'You can close this window' | translate }}</p>
    </div>
  `,
  styles: [`
    .gd-oauth-finish {
      padding: 32px;
      font-family: var(--font-family, sans-serif);
      text-align: center;
    }
  `],
})
export class GoogleDriveOAuthFinishComponent implements OnInit {
  success = false;
  errorReason: string | null = null;

  ngOnInit(): void {
    const params = new URLSearchParams(window.location.search);
    this.success = params.get('gdrive_success') === 'true';
    this.errorReason = params.get('gdrive_err');

    // Notify the modal that opened us. Restrict the target origin so a
    // hijacked window.opener can't intercept the message — only the same
    // origin (the modal's host) ever needs to receive this.
    if (window.opener && !window.opener.closed) {
      try {
        window.opener.postMessage(
          {
            type: 'gd_oauth_done',
            success: this.success,
            error: this.errorReason ?? undefined,
          },
          window.location.origin,
        );
      } catch {
        // window.opener access can throw if the opener navigated cross-origin
        // between popup-open and our postMessage call. Ignore — the modal
        // will fall back to its own timeout, the user closes the popup
        // manually, and the next click on the link re-checks /status.
      }
    }

    // Auto-close after a short delay so the user briefly sees the success
    // message. Some browsers refuse window.close() on a script-opened
    // window past a navigation; we tolerate failure (user closes manually).
    setTimeout(() => {
      try {
        window.close();
      } catch {
        // Ignore — see comment above.
      }
    }, 800);
  }
}
