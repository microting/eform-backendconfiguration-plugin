# Google Drive integration for calendar event attachments

## Context

Calendar event attachments (PDF / PNG / JPG / JPEG) shipped in Layer 1–3 of `2026-05-06-calendar-event-attachments-design.md`. The follow-up — letting the user attach files **from their Google Drive** with automatic re-sync when they edit those files in Drive — was deferred because it requires a Google Cloud project and OAuth proxy infrastructure that doesn't exist yet.

Key user-stated requirements that shape the design:

- **Files change on a 3–6 month cycle.** Polling is wasteful; push notifications scale better.
- **Users may not open the calendar for weeks** while editing the source file in Drive — the next time they (or anyone) opens the event, the attached file must already reflect the latest Drive content.
- **No re-auth prompts.** First-time OAuth grant must persist for years (refresh tokens auto-renewed; channels auto-renewed via cron).
- **Multi-tenant SaaS.** Customer instances live at different URLs; we must NOT require a Google Cloud project per customer.

The prior file-attachments design's storage / DTO / mapper / endpoint scaffolding (the `AreaRulePlanningFile` join, `UploadedData`, S3/Swift pipeline, modal UI) is reused as-is. Drive attachments slot in via two new fields on `AreaRulePlanningFile` (`DriveFileId`, `DriveModifiedTime`) and one new flow path (download from Drive instead of from form-data upload).

## High-level architecture (3 systems)

```
   ┌────────────────────────┐                   ┌─────────────────────┐
   │  Customer eForm tenant │                   │ Microting OAuth     │
   │ (per-customer URL)     │                   │ proxy service       │
   │                        │                   │ oauth.microting.com │
   │  ┌──────────────────┐  │   redirect via    │ (stateless, ~150   │
   │  │ Calendar plugin  │──┼──── proxy ───────▶│  lines, deployed   │
   │  │ + frontend modal │  │                   │  once for all      │
   │  └──────────────────┘  │   webhook routed  │  customers)        │
   │           ▲            │ ◀── via signed    │                     │
   │           │ tokens     │     channel token │                     │
   └───────────┼────────────┘                   └──────────┬──────────┘
               │                                            │
               │ direct Drive API calls (after refresh)     │ initial OAuth
               │                                            │ + token refresh
               ▼                                            ▼
        ┌────────────────────────────── Google ──────────────────────────────┐
        │    Drive API     │   Picker API    │    OAuth 2.0 + changes.watch  │
        └─────────────────────────────────────────────────────────────────────┘
```

Three concerns separated:

1. **Customer plugin** — picker UI, file-cache management, change-handling. Holds per-user refresh tokens (encrypted), per-attachment metadata, watch-channel state.
2. **Microting OAuth proxy** (`oauth.microting.com`) — single stateless service that owns the OAuth client + secret. Handles initial-auth callback, token-refresh, webhook fan-out. Never persists customer data; every routing decision is signed-token-encoded.
3. **Google** — Drive API, Picker JS, OAuth 2.0, push notifications.

## Ops prerequisites (BLOCKERS — not implementable until done)

These are gates for PR-1; nothing else can ship until they're complete. They are *not* code; someone with admin access to Microting infrastructure does them.

### 0.1 Domain + TLS

Stand up `oauth.microting.com` (or reuse an existing Microting-owned subdomain — `api.microting.com` works too, with the OAuth paths under `/google-drive/...`). DNS A/AAAA + valid TLS cert (Let's Encrypt is fine). The host doesn't need to do much; a small Linux VM, App Service plan, or Azure Function with a custom domain all work.

### 0.2 Google Cloud project

In a NEW Google Cloud project owned by Microting:

1. Enable **Google Drive API** + **Google Picker API**.
2. Create OAuth 2.0 credentials → application type "Web application".
   - Authorized JavaScript origins: `https://oauth.microting.com`
   - Authorized redirect URI: `https://oauth.microting.com/google-drive/callback`
   - Scopes: `https://www.googleapis.com/auth/drive.file`, `openid`, `email`
3. OAuth consent screen:
   - Application type: External
   - Authorized domains: `microting.com`
   - Scopes: `drive.file`, `openid`, `email` (NOT `drive.readonly` — `drive.file` is non-sensitive and skips Google verification entirely; only files explicitly granted via the Picker are accessible. `openid email` are also non-sensitive and required to call `/oauth2/v2/userinfo` for the `email` claim that ends up in the envelope.)
4. Save the Client ID + Client Secret; ship them to the proxy service as env vars `OAuthProxy__GoogleOAuthClientId` and `OAuthProxy__GoogleOAuthClientSecret` (the `OAuthProxy__` prefix matches ASP.NET's section-binding convention). Customer instances NEVER see the secret.
5. Generate a 32-byte random key for envelope signing (`OAuthProxy__ProxySigningKey`); ship it to BOTH the proxy AND every customer instance via secret store.

**Why `drive.file` and not `drive.readonly`:**
- `drive.file` is "non-sensitive" — no Google verification process required; ship to customers immediately.
- Per-file access only — user grants access through the Picker on a file-by-file basis.
- Compromised tokens cannot enumerate the user's whole Drive.
- Trade-off: app cannot list files outside what the user explicitly picked. Acceptable since calendar attachments are explicit per-file picks.

**Why `openid email` alongside `drive.file`:**
- The proxy's `/google-drive/callback` calls Google's `/oauth2/v2/userinfo` to capture the user's Google account email (so it can be persisted on the `GoogleOAuthToken` row and shown in the user-settings UI from PR-8). That endpoint requires the `openid` and `email` scopes.
- Both are on Google's "non-sensitive" scope list; no Google verification process is triggered.
- The user-facing consent screen will list `drive.file`, "associate you with your personal info on Google", and "see your primary email" — three lines, all minimal.

### 0.3 Customer-instance config

Each customer instance gets two values added to its existing config (e.g. `appsettings.json` or env vars):

- `MicrotingOAuthProxyUrl=https://oauth.microting.com`
- `ProxySigningKey=<the 32-byte key from 0.2>`

That's the entire customer-side ops change. No per-customer Google project, no per-customer redirect URI registration.

## Component breakdown — 8 PRs in order

Each row depends on the rows above. PRs ship sequentially because later rows reference base columns / OAuth wiring / proxy endpoints introduced earlier.

| # | Repo | Title | Depends on |
|---|---|---|---|
| 1 | Microting/oauth-proxy (NEW repo) | OAuth proxy service: initial-auth + token-refresh + webhook fan-out | Ops 0.1, 0.2 |
| 2 | `eform-backendconfiguration-base` | DB schema: GoogleOAuthToken + DriveFileId/DriveModifiedTime + DriveWatchChannel | PR-1 (so we know the proxy contract) |
| 3 | `eform-backendconfiguration-plugin` (C#) | OAuth-finish callback + token-refresh client + Drive download service | PR-2 |
| 4 | `eform-backendconfiguration-plugin` (frontend) | Picker integration in attach-file modal + connection-status UI | PR-3 |
| 5 | `eform-backendconfiguration-plugin` (C#) | `changes.watch` subscription on first attach + webhook receiver | PR-3 |
| 6 | `eform-debian-service` | Cron job: daily channel renewal + monthly token-keepalive | PR-5 |
| 7 | `eform-backendconfiguration-plugin` (C#) | Change processor: refetch changed files into S3, update `DriveModifiedTime` | PR-5 |
| 8 | `eform-backendconfiguration-plugin` (frontend + C#) | "Disconnect Google Drive" + per-attachment "view source" + revocation handling | PR-7 |

Total ≈8 PRs. PR-1 is its own repo and the longest pole — without it, nothing else can be tested end-to-end. PR-2 to PR-7 can be reviewed in parallel against a stubbed proxy in dev.

## Data model

All in `eform-backendconfiguration-base`, mirroring the `AreaRulePlanningFile` pattern shipped in `v10.0.32`.

### New entity `GoogleOAuthToken` (+ `GoogleOAuthTokenVersion`)

```csharp
public class GoogleOAuthToken : PnBase
{
    public int UserId { get; set; }   // FK to platform user — stays scoped to one human
    [StringLength(255)] public string GoogleAccountEmail { get; set; } = "";
    [StringLength(2048)] public string EncryptedRefreshToken { get; set; } = "";  // AES-GCM, key from cluster secret
    public DateTime ConnectedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }     // updated on every refresh — used by the keep-alive cron
    public DateTime? RevokedAt { get; set; }      // set when refresh fails 401/invalid_grant
}
```

One row per user. If a user has multiple Google accounts they want to use, that's v2.

**Uniqueness is enforced at the application layer, not the DB.** MariaDB does not natively support filtered/partial unique indexes (the WorkflowState soft-delete pattern means a naive `UNIQUE(UserId)` would block re-connection after disconnect). PR-3's `/oauth-finish` endpoint MUST: (a) look up the existing `GoogleOAuthToken` for the user, (b) if one exists with `WorkflowState = Created`, update it in place rather than inserting a new row, (c) if one exists only with `WorkflowState = Removed`, soft-undelete it and reset `EncryptedRefreshToken / RevokedAt / ConnectedAt`. Wrap this in a transaction.

### Extend `AreaRulePlanningFile`

```csharp
[StringLength(64)] public string? DriveFileId { get; set; }    // null for normal uploads
public DateTime? DriveModifiedTime { get; set; }                // last seen modifiedTime in Drive
public int? GoogleOAuthTokenId { get; set; }                    // FK to the user's token row
public GoogleOAuthToken? GoogleOAuthToken { get; set; }
```

A non-null `DriveFileId` flips the row's behaviour from "uploaded file" to "Drive-mirrored file". Same `UploadedData` chain; the `Checksum` is recomputed each refetch, the `OriginalFileName` is updated if the user renamed the file in Drive.

### New entity `DriveWatchChannel` (+ Version)

```csharp
public class DriveWatchChannel : PnBase
{
    public int GoogleOAuthTokenId { get; set; }
    [StringLength(64)] public string ChannelId { get; set; } = "";    // we generate this
    [StringLength(64)] public string ResourceId { get; set; } = "";   // returned by Google
    [StringLength(2048)] public string SignedToken { get; set; } = "";  // the JWT we send as Drive's `token` field; encodes customerInstanceURL + tokenId
    public DateTime ExpiresAt { get; set; }   // ≤7 days for drive.file scope
}
```

One per `GoogleOAuthToken`. Renewed daily (well before 7-day expiry) by the cron.

**Uniqueness is enforced at the application layer, not the DB** (same MariaDB / soft-delete reason as `GoogleOAuthToken`). PR-5's `EnsureWatchChannel` and PR-6's daily renewal MUST query for an existing `DriveWatchChannel` (with `WorkflowState = Created`) for the token before inserting; if the existing channel is still valid, return it; if it is being renewed, soft-delete the old row first.

### EF migration

`AddGoogleDriveIntegration` — adds three tables (token + version + channel + channel version) and three columns on `AreaRulePlanningFiles` + same on `AreaRulePlanningFileVersions`. Use the factory class as in prior migrations (CLAUDE.md memory).

## PR-1 — Microting OAuth proxy service

**Repo:** new repo `microting/oauth-proxy` (or under an existing infra repo).

**Stack:** ~150 lines of C# minimal-API or Node/Express — your team's call. **Stateless.** No database. Three endpoints + signing-key middleware.

### Endpoints

`GET /google-drive/start?customer=<urlencoded customer base URL>&user=<id>&nonce=<rand>`

- Verifies the request HMAC against the shared signing key (no DB lookup).
- Builds Google OAuth URL with `redirect_uri=https://oauth.microting.com/google-drive/callback`, `scope=drive.file`, `access_type=offline`, `prompt=consent` (so refresh token always issued), and a signed `state` JWT carrying `{customer, user, nonce, exp: now+5min}`.
- Returns 302 to Google.

`GET /google-drive/start?customer=<urlencoded customer base URL>&user=<id>&nonce=<rand>`

- Customer-side caller MUST send `hmac=<hex>` as a query param plus a `date` value (RFC 1123). The `date` MAY be supplied either as a `?date=<RFC1123>` query parameter or as a `Date` HTTP header — the proxy reads the query param first and falls back to the header. Browser-driven flows (`window.open(...)` top-level navigation) cannot attach custom headers and so MUST use the query param; programmatic server-to-server callers MAY use either.
- HMAC canonical string: `{customer}|{user}|{nonce}|{date}` — UTF-8, lowercase hex SHA-256 over the raw bytes. The `{date}` value is the same regardless of whether it travelled in the query string or the header.
- Proxy verifies HMAC + skew (±2 min) on the resolved date. Rejects 401 otherwise (including when neither query param nor header is present).
- Builds a signed `state` JWT (HS256 via `OAuthProxy__ProxySigningKey`) containing `{ typ: "state", customer, user, nonce, exp: now+5min }`.
- Returns 302 to Google.

`GET /google-drive/callback?code=...&state=...`

- Verifies signed `state` JWT (signature + `typ` claim must equal `"state"` + `exp`).
- Exchanges `code` for tokens (uses `client_id + client_secret` env vars).
- Calls Google's `/oauth2/v2/userinfo` to get `email` (requires `openid` and `email` scopes — see Ops 0.2).
- Builds a signed envelope JWT containing `{ typ: "envelope", refreshToken, accessToken, accessTokenExpiry, email, user, nonce, exp: now+5min }`.
- 302s to `<customer>/api/backend-configuration-pn/google-drive/oauth-finish?envelope=<jwt>`.

`POST /google-drive/refresh`

- Body: `{refreshToken}`. Customer-instance HMAC in `Authorization: HMAC-SHA256 <hex>` header, `Date: <RFC1123>` header.
- HMAC canonical string: `{body}\n{date}` — raw request body bytes UTF-8 + literal newline + RFC1123 date.
- Server validates HMAC + skew (±2 min). Rejects 401 otherwise.
- Server calls Google's `/token` with grant_type=`refresh_token`. Forwards new access token + expiry (augmented with an ISO 8601 `accessTokenExpiry` field) to customer. If Google returns 401 / `invalid_grant`, forwards that error verbatim so the customer instance can mark the token revoked.

`POST /google-drive/notify`

- Drive POSTs change notifications here. Headers include `X-Goog-Channel-ID`, `X-Goog-Channel-Token` (the JWT we set when subscribing), `X-Goog-Resource-State`, `X-Goog-Resource-ID`, `X-Goog-Message-Number`. Body may be empty.
- Channel-token JWT claims (PR-5 must mint these): `{ typ: "channel", customerInstanceUrl, channelId, exp }`. The proxy validates signature + `typ == "channel"` + `exp`.
- 200 immediately to Drive (Google retries on non-2xx; never block on customer-side processing).
- Asynchronously POSTs to `<customerInstanceUrl>/api/backend-configuration-pn/google-drive/notify` with all `X-Goog-*` headers preserved, `Date: <RFC1123>`, an empty JSON body (`{}`), and `Authorization: HMAC-SHA256 <hex>`.
- HMAC canonical string for the customer fan-out: `{channelId}|{resourceState}|{resourceId}|{messageNumber}|{date}` — UTF-8, lowercase hex SHA-256 over the raw bytes. PR-5's webhook receiver MUST verify exactly this string or HMAC checks will fail.

### Why the proxy is stateless

All routing decisions come from signed JWTs (`state` for OAuth flow, `token` for webhooks). The proxy needs ONLY the signing key + Google client_secret. Restart, scale, swap — no migration. No customer data ever lives at `oauth.microting.com`.

### Operational hooks

- Health endpoint `GET /health` (200).
- Structured logs via the platform's existing Sentry/log shipping.
- Rate limit on `/refresh` (per-customer-HMAC-key; 100 req/min) — protects against runaway loops on the customer side.

### Deployment

Single Docker image. Env vars: `OAuthProxy__GoogleOAuthClientId`, `OAuthProxy__GoogleOAuthClientSecret`, `OAuthProxy__ProxySigningKey`, `OAuthProxy__PublicBaseUrl`. The double-underscore prefix is required so ASP.NET's environment-variable provider binds to the `OAuthProxy` configuration section. All three secrets are sourced from the Kubernetes secret `oauth-secret`. Fronted by the existing Microting reverse proxy / load balancer, with the cert for `oauth.microting.com` terminated there.

## PR-2 — Base schema

Single migration adding the three entities + the `AreaRulePlanningFile` columns described above. Five unit tests covering round-trip on each new field, soft-delete of `GoogleOAuthToken`, and FK constraint on `DriveWatchChannel.GoogleOAuthTokenId`. Tag a new base NuGet (`v10.0.33` or whatever's free).

## PR-3 — Plugin C# OAuth-finish + Drive download

### Endpoint `GET /api/backend-configuration-pn/google-drive/oauth-finish?envelope=<jwt>`

- Verify envelope JWT signature with `OAuthProxy__ProxySigningKey` AND require the `typ` claim to equal `"envelope"` (defense against type-confusion across state / envelope / channel JWTs that share the signing key).
- Read envelope payload `{ refreshToken, accessToken, accessTokenExpiry, email, user, nonce }`.
- **Verify the `nonce` against the value the frontend stored when initiating `/google-drive/start`** (e.g. session cookie or short-lived row keyed by user). Reject 400 on mismatch. This is the CSRF defence — without it, an attacker who controls a redirect can race a victim's OAuth flow into the attacker's Google account.
- **Verify the envelope's `user` claim matches the currently-authenticated platform user.** Reject 403 on mismatch.
- Encrypt refreshToken at-rest (AES-GCM key from cluster secret, NOT the proxy signing key).
- Persist `GoogleOAuthToken` row.
- 302 to the calendar page with a success toast.

### Service `GoogleDriveAuthService`

Responsibilities:
- `GetAccessToken(int userId): string` — returns a valid access token for a user. Reads cached access token + expiry; if expired, calls the proxy's `/refresh` endpoint with the user's refresh token, persists the new access token, returns it. On `invalid_grant`, sets `RevokedAt` and throws a typed exception.
- `EnsureWatchChannel(int userId)` — used in PR-5.

### Service `GoogleDriveFileService`

- `DownloadAndCacheFile(int userId, string driveFileId, int areaRulePlanningId): AreaRulePlanningFile` — calls Drive `files.get` for metadata + content, computes MD5, persists `UploadedData` via the existing pipeline, then creates an `AreaRulePlanningFile` with `DriveFileId` populated. Mirrors the existing local-upload path in `BackendConfigurationCalendarService.UploadFile`.
- `RefreshFile(AreaRulePlanningFile file): bool` — used in PR-7. Re-downloads from Drive, updates the cached `UploadedData`, bumps `DriveModifiedTime`. Returns true if changed.

Tests: integration test (Testcontainers MariaDB + WireMock for the proxy + Drive API) covering the full attach flow with a fake Drive file.

## PR-4 — Frontend Picker integration

Modal already has a "Tilføj Google Drev-fil" link (currently disabled with "Coming soon" tooltip). Now:

1. Click the link → check whether the user has a `GoogleOAuthToken` (call `GET /api/backend-configuration-pn/google-drive/status`).
   - If not connected: window.open `oauth.microting.com/google-drive/start?customer=...&user=...&nonce=...&hmac=...`. After the OAuth dance, the proxy 302s back to our `oauth-finish` endpoint; that page closes itself via `postMessage` to the modal, which proceeds.
   - If connected: skip to step 2.
2. Load the Google Picker JS (`https://apis.google.com/js/api.js`); initialize with the user's access token and the `DocsView` filter for `application/pdf|image/png|image/jpeg`.
3. On user pick → POST `/api/backend-configuration-pn/google-drive/attach { areaRulePlanningId, driveFileId }`. Backend uses `GoogleDriveFileService.DownloadAndCacheFile`. Modal updates the attachment list with the new row.

Reuses existing `gcal-attachment-row` rendering — Drive-sourced rows additionally show a small "Drive" badge + "view source" link (`webViewLink`).

Frontend gets a `GoogleDriveStatusComponent` for the user-settings page (PR-8) showing connected accounts + disconnect button.

## PR-5 — Watch subscription + webhook receiver

After `DownloadAndCacheFile` lands the first Drive-sourced attachment for a user, ensure a watch channel exists:

- `POST drive/v3/changes/watch` with `id=<new uuid>`, `type=web_hook`, `address=https://oauth.microting.com/google-drive/notify`, `token=<JWT containing customerInstanceUrl + channelId>`.
- Persist `DriveWatchChannel`.

New endpoint `POST /api/backend-configuration-pn/google-drive/notify` (called by the proxy):

- Verify HMAC.
- Verify JWT in `X-Goog-Channel-Token` against the persisted `DriveWatchChannel.SignedToken`.
- Enqueue a refresh job for the channel's user. The actual refetch happens in PR-7's processor.
- Return 200.

The webhook is *fire-and-forget*: respond fast, do work asynchronously. If the customer instance is offline when the notification fires, that change is lost (Drive doesn't retry on application errors past the webhook 200) — but **the daily reconcile cron in PR-6** picks up missed changes by calling `changes.list(pageToken)` with the saved `startPageToken`.

## PR-6 — Cron job (renewal + keep-alive + reconcile)

Lives in `eform-debian-service` (existing service plugin pattern).

- **Daily channel renewal** — for every `DriveWatchChannel` whose `ExpiresAt < now + 24h`, call `channels.stop` on the old channel + `changes.watch` for a new one. Persist new channel ID + expiry.
- **Monthly token keep-alive** — for every `GoogleOAuthToken` where `LastUsedAt < now - 30d` and `RevokedAt is null`, call `/refresh` to keep the token "live" against Google's 6-month-inactivity policy.
- **Daily reconcile** — for every `GoogleOAuthToken`, call Drive `changes.list(pageToken)` with the saved `startPageToken`; for each change matching a tracked `DriveFileId`, enqueue a refresh job. Catches anything missed if a webhook delivery failed.

These are three small loops, all sharing the same SDK plumbing. ~200 lines total.

## PR-7 — Change processor

Service `GoogleDriveChangeProcessor` consumed both by PR-5's webhook and PR-6's reconcile cron.

For each `(GoogleOAuthTokenId, DriveFileId)` in the work queue:

1. Get an access token via `GoogleDriveAuthService.GetAccessToken`.
2. Call `files.get(driveFileId, fields: 'modifiedTime,name,size,mimeType')`.
3. If `modifiedTime > AreaRulePlanningFile.DriveModifiedTime`: call `files.get?alt=media`, stream the new bytes through the existing S3 upload path, update `UploadedData.Checksum / FileLocation / FileName`, bump `DriveModifiedTime`, `OriginalFileName` (in case renamed), `MimeType` (in case re-converted).
4. Audit-log the change.
5. If Google returns 404: file deleted by user. Mark the `AreaRulePlanningFile` with `WorkflowState=Removed`. Log; don't auto-reattach (user must re-pick).
6. If Google returns 403: permission revoked. Same as 404, but with a different status message.

Concurrency: if two notifications arrive close together for the same file, second one is a near-instant no-op (modifiedTime hasn't moved). No locking needed because each file is processed independently in the queue.

Idempotency: yes — repeated runs converge.

## PR-8 — Settings UI + revocation handling

User settings page:

- "Connected Google accounts" list — each `GoogleOAuthToken` shows email, ConnectedAt, LastUsedAt.
- Disconnect button — calls `DELETE /api/backend-configuration-pn/google-drive/disconnect/{tokenId}`. Backend revokes the token at Google (`POST /revoke`), stops all watch channels, sets `RevokedAt`. Drive-sourced attachments that depend on this token become "orphaned" (rows stay but stop refreshing); the modal displays them with a "Drive disconnected — reconnect to resume sync" badge.

Per-attachment UI:

- Drive badge + "open in Drive" link (uses cached `webViewLink`).
- "Last refreshed: 2 days ago" tooltip.
- If revoked: red badge "Drive disconnected".

## Security

- **Refresh tokens** AES-GCM-encrypted at rest with a key from the cluster secret store (not in source). The proxy's signing key and Google's client secret are *separate* keys.
- **JWTs (state / envelope / channel-token)** all share the same signing key, so each carries a `typ` claim (`"state"`, `"envelope"`, `"channel"`) that is verified on every read. Without this, a state JWT could be replayed against the channel-token endpoint, and vice-versa. The proxy and the customer-side `/oauth-finish` MUST both check the `typ`.
- **State JWT replay protection** is enforced ONLY by the 5-min `exp`. A stateless proxy cannot maintain a "used nonces" set. The downstream defence is the customer-side `/oauth-finish`: it MUST verify the envelope's `nonce` against the value the frontend stored when initiating `/start`, otherwise an attacker who controls a redirect can race a victim's OAuth flow into the attacker's account. PR-3 owns this check.
- **HMAC on `/start`, `/refresh`, and `/notify` fan-out**: HS256, time-bounded (request `Date` header within ±2 minutes). The Date is part of the canonical string so a captured request cannot be replayed indefinitely.
- **Stateless proxy** — compromising the proxy reveals client_secret + signing key but not customer refresh tokens (those live in customer DBs only).
- **Per-customer signing-key compromise containment**: this design uses a SHARED signing key. If you want stricter containment, give each customer instance its own key and have the proxy hold a registry mapping customer URL → signing key. Adds DB to the proxy. Out of scope for v1; revisit if compliance demands it.
- `drive.file` scope means a compromised refresh token grants access ONLY to files the user explicitly picked via the Picker — not their entire Drive. (`openid` and `email` are also requested but only expose the user's email; they cannot be used to access Drive content.)

## Testing strategy

- **Proxy unit tests** — JWT sign/verify, HMAC verify, state-replay rejection, Google `/token` mocking via WireMock.
- **Plugin C# integration tests** — mock proxy + mock Drive API via WireMock, full attach + refresh + revoke round-trips. Mirrors `CalendarAttachmentTests`.
- **End-to-end Playwright** — staging environment with the real Microting proxy hitting real Google. Mark as `@manual` rather than CI-default; the whole point of staging is to validate cross-service integration end-to-end.
- **Cron job integration test** — runs the renewal/keep-alive/reconcile loops against a seeded DB + WireMock proxy, asserts the right calls happen at the right times.

## Operational considerations

- **Quota:** Drive API gives 1B requests/day per project by default. Even at 10k users with 100 files each (1M total), polling `files.get` once a day would be 1M calls/day. Push notifications + the daily reconcile cron together stay under 100k/day in steady state. Plenty of headroom.
- **Webhook reliability:** Google retries non-2xx for ~30 days but stops on persistent failure. The reconcile cron is the safety net.
- **Cost of the proxy:** trivially small. The whole service runs on a single 1-vCPU VM with room to spare.
- **Monitoring:** alert if `GoogleOAuthToken` revocations exceed 1% of active tokens in a day (signals OAuth misconfig), alert if proxy `/refresh` 401 rate spikes (signals client_secret rotation needed), alert if any watch channel expires before being renewed (cron failure).

## Out of scope

- **Multiple Google accounts per platform user.** v1 = one Google account per user. If user picks a file that's owned by a different Google account, the picker auth-flow returns to that user prompt. v2.
- **Folder-level subscriptions.** Watching an entire Drive folder for new files. Different scope (`drive.readonly`), Google verification required, much more complex. v3+.
- **Drive comments / collaboration metadata.** We sync file content only.
- **Conflict resolution if a user edits the file simultaneously through our calendar (we don't — read-only) AND in Drive.** N/A given read-only model.
- **Microsoft OneDrive / Dropbox / etc.** Different OAuth, different APIs. The proxy architecture generalizes (same envelope/JWT pattern), but the Drive-specific code does not — each new provider is its own PR-1-through-PR-7 chain.
- **Per-customer signing keys** — see Security note above.
- **The OAuth proxy service repo bootstrapping** (CI/CD, monitoring, cert renewal) — your DevOps team's existing patterns; this spec covers what the proxy *does*, not how it's built/deployed.

## Pickup checklist for the next session

1. Read this spec end-to-end.
2. Verify ops 0.1, 0.2, 0.3 are done. If not — ops first.
3. Start with PR-1 (the proxy). Until that's deployed and the URL is reachable, customer-side work has nothing to point at.
4. Use the same workflow as the file-attachments feature (subagent + code-review per PR; tag/push base; bump NuGet; Playwright e2e).
5. PRs 3–8 can be drafted in parallel against a dev-stub proxy once PR-2 (schema) lands.

## References

- Calendar event attachments design (prior PR): `docs/superpowers/specs/2026-05-06-calendar-event-attachments-design.md`
- Calendar custom-repeat reconstruction (related schema work): `docs/superpowers/specs/2026-04-30-calendar-edit-mode-meta-reconstruction-design.md`
- Google Drive API push notifications: `https://developers.google.com/workspace/drive/api/guides/push`
- Google Drive `drive.file` scope (non-sensitive): `https://developers.google.com/identity/protocols/oauth2/scopes#drive`
- Google Picker API: `https://developers.google.com/drive/picker/guides/overview`
