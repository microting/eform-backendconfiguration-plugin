import {Injectable} from '@angular/core';
import {HttpErrorResponse} from '@angular/common/http';
import {EMPTY, MonoTypeOperatorFunction, Observable, Subject, catchError, from, switchMap, throwError} from 'rxjs';
import {ToastrService} from 'ngx-toastr';
import {TranslateService} from '@ngx-translate/core';
import {Store} from '@ngrx/store';
import {AuthStateService} from 'src/app/common/store';
import {selectAuthIsAuth} from 'src/app/state/auth/auth.selector';

/** The 403 body code the archive API sends for a resigned or deactivated user (spec D7). */
export const ARCHIVE_ACCOUNT_DISABLED = 'account_disabled';

/** The toast for an [ArchiveManager] refusal (403 with an empty body): admin/Leder-only actions. */
export const ARCHIVE_MANAGER_REFUSED = 'Only administrators and leaders can do this';

/** `StatusCodeResult(403)` has no body: null/'' for JSON calls, an empty Blob for downloads. */
function isEmptyBody(body: unknown): boolean {
  return body == null || body === '' || (body instanceof Blob && body.size === 0);
}

function blobText(blob: Blob): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result));
    reader.onerror = () => reject(reader.error);
    reader.readAsText(blob);
  });
}

/** `error` of a `{"error": "..."}` body. Blob bodies (file downloads) and JSON strings are parsed first. */
export async function archiveErrorCode(body: unknown): Promise<string | null> {
  try {
    if (body instanceof Blob) {
      return archiveErrorCode(JSON.parse(await blobText(body)));
    }
    if (typeof body === 'string') {
      return archiveErrorCode(JSON.parse(body));
    }
  } catch {
    return null;
  }
  const code = (body as {error?: unknown} | null)?.error;
  return typeof code === 'string' ? code : null;
}

/**
 * Every archive call goes through `handle()`. It ends the session for a 403 whose body is exactly
 * `{"error": "account_disabled"}` (JSON or, for get-file / get-files, a Blob). It toasts an [ArchiveManager] refusal
 * (403 with an empty body: admin/Leder-only), emits it on `forbidden$`, and lets that and every other error through.
 *
 * This is NOT the main logout path for a disabled user. Both resign paths set EformUser.IsActive = false; the host's
 * HttpErrorInterceptor answers any 403 with a token refresh first, AuthService.RefreshToken refuses an inactive user,
 * and the host then logs out and completes EMPTY, so this handler never runs. It matters when the archive's own
 * disabled check (ArchiveAccessFilterAttribute) says disabled while IsActive is still true; the retried request's
 * 403 then reaches it.
 *
 * Parallel archive calls (folders and files load together) can all fail at once; only the first one toasts and
 * logs out (`logout()` is not idempotent), later ones just complete empty. The handler is a root singleton and
 * logout does not reload the page, so a NEW authenticated session (isAuth turning true) re-arms it. A late success from a
 * request that was already in flight must not re-arm it, or a later disabled response would log out and toast twice.
 *
 * 404s: the host interceptor retries a 404 (5 x 15 s). Some host versions then complete EMPTY (a method tagged
 * "May complete empty (404)" returns an observable that COMPLETES WITHOUT A VALUE for a missing or unreadable
 * document or folder), others never emit or complete at all. Do not rely on an `error:` callback or on an empty
 * completion; a caller that chains on a result must bound the wait (e.g. `timeout`) and treat "no value" as "not
 * done" (e.g. `defaultIfEmpty(false)`). Task-3's ArchiveCapabilitiesService does this for getCapabilities.
 */
@Injectable({providedIn: 'root'})
export class ArchiveAccountDisabledHandler {
  /** Set by the first account_disabled hit (later hits do nothing); cleared when a new authenticated session starts. */
  private signingOut = false;

  private readonly forbidden = new Subject<void>();
  /**
   * Emits on every [ArchiveManager] refusal. The user's rights changed since the page loaded (a revoke) or the page
   * offered something it should not have; ArchiveCapabilitiesService reloads on it so the controls follow.
   */
  readonly forbidden$: Observable<void> = this.forbidden.asObservable();

  constructor(
    private authStateService: AuthStateService,
    private toastrService: ToastrService,
    private translateService: TranslateService,
    store: Store,
  ) {
    // Root singleton: this subscription intentionally lives for the whole app.
    store.select(selectAuthIsAuth).subscribe(isAuth => {
      if (isAuth) {
        this.signingOut = false;
      }
    });
  }

  /**
   * `announceForbidden` false: an empty-body 403 is an ordinary error (no toast, no `forbidden$`). For endpoints whose
   * empty 403 is not an [ArchiveManager] refusal, e.g. [FirstUserOnly] (inbox rotate-address).
   */
  handle<T>(announceForbidden = true): MonoTypeOperatorFunction<T> {
    return (source: Observable<T>) => source.pipe(
      catchError((error: unknown) => {
        if (!(error instanceof HttpErrorResponse) || error.status !== 403) {
          return throwError(() => error);
        }
        return from(archiveErrorCode(error.error)).pipe(
          switchMap(code => {
            if (code !== ARCHIVE_ACCOUNT_DISABLED) {
              if (announceForbidden && isEmptyBody(error.error)) {
                this.announceManagerRefused();
              }
              return throwError(() => error);
            }
            if (!this.signingOut) {
              this.signingOut = true;
              this.toastrService.error(this.translateService.instant('Your account is disabled'));
              this.authStateService.logout();
            }
            return EMPTY;
          }),
        );
      }),
    );
  }
  private announceManagerRefused(): void {
    this.toastrService.error(this.translateService.instant(ARCHIVE_MANAGER_REFUSED));
    this.forbidden.next();
  }
}
