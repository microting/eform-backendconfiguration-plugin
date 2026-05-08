/**
 * Frontend model returned by GET /api/backend-configuration-pn/google-drive/status.
 * Lets the calendar attach-file modal decide whether to launch the OAuth flow
 * before showing the Picker. See PR-4 of the Google Drive integration design:
 * docs/superpowers/specs/2026-05-07-google-drive-integration-design.md.
 */
export interface GoogleDriveStatus {
  connected: boolean;
  email?: string;
  connectedAt?: string;
}

/**
 * Returned by GET /api/backend-configuration-pn/google-drive/picker-token.
 * The frontend uses this to instantiate the Google Picker — it cannot call
 * Google directly because the access token only lives server-side.
 */
export interface GoogleDrivePickerToken {
  accessToken: string;
  developerKey: string;
}
