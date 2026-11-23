import {inject, Injectable, InjectionToken, OnDestroy} from '@angular/core';
import {Store} from '@ngrx/store';
import {Observable, Subject, Subscription} from 'rxjs';
import * as signalR from '@microsoft/signalr';
import {selectBearerToken} from 'src/app/state/auth/auth.selector';

/** The plugin's live-update hub (docs/live-updates-hub-contract.md). Same origin as the REST API. */
export const CALENDAR_HUB_PATH = '/hub/backend-configuration';

/** Builds the hub connection; a spec swaps in a fake. */
export type CalendarHubConnectionFactory = (accessTokenFactory: () => Promise<string>) => signalR.HubConnection;
export const CALENDAR_HUB_CONNECTION_FACTORY =
  new InjectionToken<CalendarHubConnectionFactory>('CALENDAR_HUB_CONNECTION_FACTORY');

const defaultHubConnectionFactory: CalendarHubConnectionFactory = (accessTokenFactory) =>
  new signalR.HubConnectionBuilder()
    // A WebSocket carries the token as ?access_token=.
    .withUrl(CALENDAR_HUB_PATH, {accessTokenFactory})
    .withAutomaticReconnect()
    // The calendar works without the hub; the library must not fill the console when it is not there.
    .configureLogging(signalR.LogLevel.None)
    .build();

/** First delay before a fresh start after a failed start or a lost connection; it doubles up to the maximum. */
export const CALENDAR_LIVE_RESTART_MS = 5_000;
export const CALENDAR_LIVE_RESTART_MAX_MS = 300_000;
/** The user already holds the maximum of live connections (contract: back off before retrying). */
export const CALENDAR_LIVE_TOO_MANY_CONNECTIONS_MS = 60_000;

/**
 * - `changed`: something on the property changed; reload it.
 * - `resync`: the feed was interrupted (a reconnect); hints may be lost.
 * - `joining` / `joined`: the FIRST join of the property was sent / answered ok. Hints flow from `joined` on; a
 *   change between the calendar's own load and the join is only covered by a load that started after `joining`.
 */
export interface CalendarLiveHint {
  propertyId: number;
  reason: 'changed' | 'resync' | 'joining' | 'joined';
}

/** The join result (`{"v":1,"code":"ok"}` or a refusal) and the `Refused` message share this shape. */
interface LiveCodePayload {
  v?: number;
  code?: string;
}

/** `TaskChanged`: ids only. The calendar reloads the property, so only `property_id` is read. */
interface TaskChangedPayload {
  v?: number;
  property_id?: string;
}

/** The server has no such hub (an older server): HttpError with a 4xx status from the negotiate. */
const isHubMissing = (err: unknown): boolean => {
  const status = (err as {statusCode?: number} | null)?.statusCode;
  return typeof status === 'number' && status >= 400 && status < 500;
};

/** The server has the hub but not the method (a server older than package 11b). */
const isMethodMissing = (err: unknown): boolean =>
  /Method does not exist|Unknown hub method/i.test(String((err as Error | null)?.message ?? err));

/**
 * Live updates for the web calendar (package 11c). One connection per calendar view (provided by the container,
 * so it ends with it). It subscribes to the ONE property on screen with `JoinCalendarProperty` and turns
 * `TaskChanged` into `hints$`.
 *
 * It is strictly optional: when the hub is missing, the method is missing, the server refuses, or there is no
 * session, the service goes quiet (one `console.debug` line at most) and the calendar behaves as before.
 *
 * It never refreshes the session: the host's refresh signs the user out when it fails, and the hub must never be
 * the cause of a sign-out. It sends whatever token the store holds; the REST interceptor keeps that fresh.
 */
@Injectable()
export class BackendConfigurationCalendarLiveService implements OnDestroy {
  private readonly hintsSubject = new Subject<CalendarLiveHint>();
  readonly hints$: Observable<CalendarLiveHint> = this.hintsSubject.asObservable();

  private readonly store = inject(Store);
  private readonly factory = inject(CALENDAR_HUB_CONNECTION_FACTORY, {optional: true}) ?? defaultHubConnectionFactory;
  private readonly tokenSubscription: Subscription;
  private token = '';
  private connection: signalR.HubConnection | null = null;
  /** The property on screen. */
  private wantedPropertyId: number | null = null;
  /** The property joined on the current connection. */
  private joinedPropertyId: number | null = null;
  /** The wanted property was joined on some connection since it became wanted: a later join is a resync. */
  private joinedOnceFor: number | null = null;
  /** Properties the server refused to join (`property_not_available`, `too_many_properties`): not asked again. */
  private readonly refusedProperties = new Set<number>();
  /** The `Refused` code the current connection received before its close. */
  private refusal: string | null = null;
  private restartTimer: ReturnType<typeof setTimeout> | null = null;
  private restartAttempt = 0;
  /** `revoked` and `not_authenticated` reconnect once (with the store's token at that time). Reset by a join. */
  private refusalReconnectUsed = false;
  /** Given up for the life of this view. */
  private quiet = false;
  private destroyed = false;
  private logged = false;

  constructor() {
    // In the constructor, after every field above is set: the store emits the current token synchronously.
    this.tokenSubscription = this.store.select(selectBearerToken).subscribe(token => this.onToken(token));
  }

  /** True while the property on screen is subscribed (or being re-subscribed after a drop) and hints can arrive. */
  get isLive(): boolean {
    return !this.quiet && !this.destroyed && this.wantedPropertyId !== null
      && this.joinedOnceFor === this.wantedPropertyId;
  }

  /** Follow `propertyId` (null: none). Leaves the previous property; a no-op for the property already followed. */
  joinProperty(propertyId: number | null): void {
    if (this.destroyed || propertyId === this.wantedPropertyId) {
      return;
    }
    const previous = this.joinedPropertyId;
    this.wantedPropertyId = propertyId;
    this.joinedPropertyId = null;
    this.joinedOnceFor = null;
    const connection = this.connection;
    if (previous !== null && connection) {
      void connection.invoke('LeaveCalendarProperty', String(previous)).catch(() => undefined);
    }
    if (propertyId === null) {
      this.stop();
      return;
    }
    if (this.quiet || this.refusedProperties.has(propertyId)) {
      return;
    }
    if (connection?.state === signalR.HubConnectionState.Connected) {
      void this.join(connection);
    } else if (this.restartTimer === null) {
      // Connecting or reconnecting: that connection joins the wanted property once it is up. A pending restart
      // keeps its back-off and joins it too.
      void this.start();
    }
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    this.tokenSubscription.unsubscribe();
    this.stop();
    this.hintsSubject.complete();
  }

  private onToken(token: string | null | undefined): void {
    this.token = typeof token === 'string' ? token : '';
    // Signed out: no session, no connection. A new sign-in reconnects when a property is still wanted.
    if (!this.token) {
      this.stop();
    } else if (!this.connection && this.restartTimer === null) {
      void this.start();
    }
  }

  private async start(): Promise<void> {
    if (this.destroyed || this.quiet || this.connection || !this.token || this.wantedPropertyId === null
        || this.refusedProperties.has(this.wantedPropertyId)) {
      return;
    }
    // Read on every (re)connect, so a token the REST side refreshed meanwhile is the one sent.
    const connection = this.factory(() => Promise.resolve(this.token));
    this.connection = connection;
    this.refusal = null;
    connection.on('TaskChanged', (payload: TaskChangedPayload) => this.onTaskChanged(connection, payload));
    connection.on('Refused', (payload: LiveCodePayload) => {
      if (connection === this.connection) {
        this.refusal = payload?.code ?? 'unknown';
      }
    });
    connection.onreconnecting(() => {
      if (connection === this.connection) {
        this.joinedPropertyId = null;
      }
    });
    connection.onreconnected(() => {
      // A reconnect is a new connection id: the membership is gone, and so is whatever happened meanwhile.
      if (connection === this.connection) {
        void this.join(connection);
      }
    });
    connection.onclose(() => this.onClosed(connection));
    try {
      await connection.start();
    } catch (err) {
      if (connection !== this.connection) { // a stop we asked for
        return;
      }
      this.connection = null;
      if (isHubMissing(err)) {
        this.goQuiet('the server has no live hub');
        return;
      }
      this.debugOnce('cannot reach the live hub, retrying with back-off');
      this.scheduleRestart();
      return;
    }
    if (connection !== this.connection) {
      void connection.stop().catch(() => undefined);
      return;
    }
    await this.join(connection);
  }

  private async join(connection: signalR.HubConnection): Promise<void> {
    const propertyId = this.wantedPropertyId;
    if (propertyId === null) {
      return;
    }
    const firstJoin = this.joinedOnceFor !== propertyId;
    if (firstJoin) {
      this.hintsSubject.next({propertyId, reason: 'joining'});
    }
    let result: LiveCodePayload | null;
    try {
      result = await connection.invoke<LiveCodePayload | null>('JoinCalendarProperty', String(propertyId));
    } catch (err) {
      if (connection !== this.connection) {
        return;
      }
      if (isMethodMissing(err)) {
        this.goQuiet('the server has no JoinCalendarProperty');
        return;
      }
      // An unexpected server fault: no retry loop; the next reconnect joins again.
      this.debugOnce('JoinCalendarProperty failed');
      return;
    }
    if (connection !== this.connection || this.destroyed) {
      return;
    }
    const code = result?.code;
    if (propertyId !== this.wantedPropertyId) {
      // The user moved on while this join was under way.
      if (code === 'ok') {
        void connection.invoke('LeaveCalendarProperty', String(propertyId)).catch(() => undefined);
      }
      return;
    }
    if (code === 'ok') {
      this.joinedPropertyId = propertyId;
      this.restartAttempt = 0;
      this.refusalReconnectUsed = false;
      this.joinedOnceFor = propertyId;
      this.hintsSubject.next({propertyId, reason: firstJoin ? 'joined' : 'resync'});
      return;
    }
    if (code === 'property_not_available' || code === 'too_many_properties') {
      this.refusedProperties.add(propertyId);
      this.debugOnce(`property refused (${code})`);
      return;
    }
    // account_disabled, not_authenticated or a code this client does not know. For the first two the server ends
    // the connection itself (Refused, then close); onClosed acts on that.
    if (code !== 'account_disabled' && code !== 'not_authenticated') {
      this.goQuiet(`join refused (${code})`);
    }
  }

  private onTaskChanged(connection: signalR.HubConnection, payload: TaskChangedPayload): void {
    if (connection !== this.connection) {
      return;
    }
    const propertyId = Number(payload?.property_id);
    if (!Number.isFinite(propertyId) || propertyId !== this.wantedPropertyId) {
      return;
    }
    this.hintsSubject.next({propertyId, reason: 'changed'});
  }

  private onClosed(connection: signalR.HubConnection): void {
    if (connection !== this.connection) { // a stop we asked for
      return;
    }
    this.connection = null;
    this.joinedPropertyId = null;
    const code = this.refusal;
    this.refusal = null;
    if (code === null) {
      // An ordinary drop (the automatic reconnect gave up, or a close without Refused).
      this.scheduleRestart();
      return;
    }
    if (code === 'too_many_connections') {
      this.debugOnce('too many live connections, retrying later');
      this.scheduleRestart(CALENDAR_LIVE_TOO_MANY_CONNECTIONS_MS);
      return;
    }
    if ((code === 'revoked' || code === 'not_authenticated') && !this.refusalReconnectUsed) {
      this.refusalReconnectUsed = true;
      this.scheduleRestart();
      return;
    }
    this.goQuiet(`refused (${code})`);
  }

  private scheduleRestart(minimumMs = 0): void {
    if (this.destroyed || this.quiet || this.restartTimer !== null || this.wantedPropertyId === null) {
      return;
    }
    const delay = Math.max(minimumMs,
      Math.min(CALENDAR_LIVE_RESTART_MAX_MS, CALENDAR_LIVE_RESTART_MS * 2 ** this.restartAttempt));
    this.restartAttempt++;
    this.restartTimer = setTimeout(() => {
      this.restartTimer = null;
      void this.start();
    }, delay);
  }

  private clearRestart(): void {
    if (this.restartTimer !== null) {
      clearTimeout(this.restartTimer);
    }
    this.restartTimer = null;
  }

  private goQuiet(reason: string): void {
    this.quiet = true;
    this.debugOnce(reason);
    this.stop();
  }

  private stop(): void {
    this.clearRestart();
    this.joinedPropertyId = null;
    const connection = this.connection;
    this.connection = null; // so its onclose is not taken for a drop
    void connection?.stop().catch(() => undefined);
  }

  private debugOnce(reason: string): void {
    if (this.logged) {
      return;
    }
    this.logged = true;
    console.debug(`Calendar live updates off: ${reason}`);
  }
}
