import {Injectable, inject} from '@angular/core';
import {Store} from '@ngrx/store';
import {
  BehaviorSubject, Observable, catchError, combineLatest, defaultIfEmpty, finalize, map, of, shareReplay, switchMap, take, tap, timeout,
} from 'rxjs';
import {selectCurrentUserIsAdmin, selectCurrentUserIsFirstUser} from 'src/app/state';
import {selectAuthIsAuth} from 'src/app/state/auth/auth.selector';
import {ArchiveCapabilitiesModel} from '../../../models';
import {ArchiveAccountDisabledHandler, BackendConfigurationPnArchiveFoldersService} from '../../../services';

/** Bound for GET archive/capabilities: an older plugin's 404 is retried by the host (5 x 15 s) and never answers. */
export const CAPABILITIES_TIMEOUT_MS = 10_000;

export const NO_ARCHIVE_CAPABILITIES: Readonly<ArchiveCapabilitiesModel> = Object.freeze({
  canManage: false, canDelete: false, canUseInbox: false, isFirstUser: false,
});

function withDefaults(model: Partial<ArchiveCapabilitiesModel>): ArchiveCapabilitiesModel {
  return {
    canManage: model.canManage === true,
    canDelete: model.canDelete === true,
    canUseInbox: model.canUseInbox === true,
    isFirstUser: model.isFirstUser === true,
  };
}

/**
 * What the signed-in user may do in Arkiv (GET archive/capabilities); a Leder manages the archive without being an
 * admin. The server enforces every rule. load() emits once, shares one request, and reloads after an [ArchiveManager]
 * refusal. It is bounded by CAPABILITIES_TIMEOUT_MS; a timeout, failure or older plugin falls back to the role
 * (a failed reload keeps the last answer). Sign-out forgets everything, also an answer still in flight.
 */
@Injectable({providedIn: 'root'})
export class ArchiveCapabilitiesService {
  private foldersService = inject(BackendConfigurationPnArchiveFoldersService);
  private store = inject(Store);
  private current = new BehaviorSubject<ArchiveCapabilitiesModel>(NO_ARCHIVE_CAPABILITIES);
  private inFlight: Observable<ArchiveCapabilitiesModel> | null = null;
  private lastLoaded: ArchiveCapabilitiesModel | null = null;
  /** Bumped on sign-out: an answer from an older generation belongs to the previous user and is ignored. */
  private generation = 0;

  readonly capabilities$: Observable<ArchiveCapabilitiesModel> = this.current.asObservable();

  constructor() {
    // Root singleton: both subscriptions intentionally live for the whole app.
    inject(ArchiveAccountDisabledHandler).forbidden$.subscribe(() => this.load().subscribe());
    this.store.select(selectAuthIsAuth).subscribe(isAuth => {
      if (!isAuth) {
        this.generation++;
        this.inFlight = null;
        this.lastLoaded = null;
        this.current.next(NO_ARCHIVE_CAPABILITIES);
      }
    });
  }

  load(): Observable<ArchiveCapabilitiesModel> {
    if (!this.inFlight) {
      const generation = this.generation;
      const isCurrent = () => generation === this.generation;
      const request: Observable<ArchiveCapabilitiesModel> = this.foldersService.getCapabilities().pipe(
        timeout(CAPABILITIES_TIMEOUT_MS),
        map((res): ArchiveCapabilitiesModel | null => (res?.success && res.model ? withDefaults(res.model) : this.lastLoaded)),
        tap(model => {
          if (model && isCurrent()) {
            this.lastLoaded = model;
          }
        }),
        defaultIfEmpty<ArchiveCapabilitiesModel | null>(null),
        catchError((): Observable<ArchiveCapabilitiesModel | null> => of(this.lastLoaded)),
        switchMap((model): Observable<ArchiveCapabilitiesModel> => (model ? of(model) : this.roleFallback())),
        catchError((): Observable<ArchiveCapabilitiesModel> => of({...NO_ARCHIVE_CAPABILITIES})),
        tap(model => {
          if (isCurrent()) {
            this.current.next(model);
          }
        }),
        finalize(() => {
          if (this.inFlight === request) {
            this.inFlight = null;
          }
        }),
        shareReplay(1),
      );
      this.inFlight = request;
    }
    return this.inFlight;
  }

  /** Before the Leder concept: the admin role managed the archive; the first user may rotate the address. */
  private roleFallback(): Observable<ArchiveCapabilitiesModel> {
    return combineLatest([
      this.store.select(selectCurrentUserIsAdmin),
      this.store.select(selectCurrentUserIsFirstUser),
    ]).pipe(
      take(1),
      map(([isAdmin, isFirstUser]): ArchiveCapabilitiesModel => ({canManage: isAdmin, canDelete: isAdmin, canUseInbox: isAdmin, isFirstUser: isFirstUser === true})),
    );
  }
}
