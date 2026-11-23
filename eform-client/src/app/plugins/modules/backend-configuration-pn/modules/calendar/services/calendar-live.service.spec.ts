import {afterEach, beforeEach, describe, expect, it, vi} from 'vitest';
import type {Mock, MockInstance} from 'vitest';
import {Injector} from '@angular/core';
import {Store} from '@ngrx/store';
import {BehaviorSubject, Observable, of} from 'rxjs';
import * as signalR from '@microsoft/signalr';
import {AuthStateService} from 'src/app/common/store';
import {
  BackendConfigurationCalendarLiveService,
  CALENDAR_HUB_CONNECTION_FACTORY,
  CALENDAR_LIVE_RESTART_MS,
  CALENDAR_LIVE_TOO_MANY_CONNECTIONS_MS,
  CalendarHubConnectionFactory,
  CalendarLiveHint,
} from './calendar-live.service';

type Handler = (...args: unknown[]) => void;

/** The part of signalR.HubConnection the service uses, scripted by the spec. */
class FakeHubConnection {
  state = signalR.HubConnectionState.Disconnected;
  readonly invocations: {method: string; args: unknown[]}[] = [];
  stopCalls = 0;
  tokenSent: string | null = null;
  startError: unknown = null;
  /** When set, start() waits for it (a slow negotiate). */
  startGate: Promise<void> | null = null;
  /** Result per invoked method; a function may reject. */
  joinResult: () => Promise<unknown> = () => Promise.resolve({v: 1, code: 'ok'});
  private readonly handlers = new Map<string, Handler>();
  private reconnecting?: Handler;
  private reconnected?: Handler;
  private closed?: Handler;

  constructor(readonly accessTokenFactory: () => Promise<string>) {}

  async start(): Promise<void> {
    this.tokenSent = await this.accessTokenFactory();
    if (this.startGate) {
      await this.startGate;
    }
    if (this.startError) {
      throw this.startError;
    }
    this.state = signalR.HubConnectionState.Connected;
  }

  async stop(): Promise<void> {
    this.stopCalls++;
    this.state = signalR.HubConnectionState.Disconnected;
  }

  invoke(method: string, ...args: unknown[]): Promise<unknown> {
    this.invocations.push({method, args});
    return method === 'JoinCalendarProperty' ? this.joinResult() : Promise.resolve(undefined);
  }

  on(name: string, handler: Handler): void {
    this.handlers.set(name, handler);
  }

  onreconnecting(cb: Handler): void {
    this.reconnecting = cb;
  }

  onreconnected(cb: Handler): void {
    this.reconnected = cb;
  }

  onclose(cb: Handler): void {
    this.closed = cb;
  }

  // --- driven by the spec -------------------------------------------------
  serverSends(name: string, payload: unknown): void {
    this.handlers.get(name)?.(payload);
  }

  dropAndReconnect(): void {
    this.state = signalR.HubConnectionState.Reconnecting;
    this.reconnecting?.();
    this.state = signalR.HubConnectionState.Connected;
    this.reconnected?.();
  }

  /** The server closes (after a Refused, or a drop the automatic reconnect gave up on). */
  serverCloses(): void {
    this.state = signalR.HubConnectionState.Disconnected;
    this.closed?.();
  }

  joins(): string[] {
    return this.invocations.filter(i => i.method === 'JoinCalendarProperty').map(i => String(i.args[0]));
  }

  leaves(): string[] {
    return this.invocations.filter(i => i.method === 'LeaveCalendarProperty').map(i => String(i.args[0]));
  }
}

/** Lets the service's promise chains (start, invoke) run. */
async function flush(): Promise<void> {
  for (let i = 0; i < 10; i++) {
    await Promise.resolve();
  }
}

describe('BackendConfigurationCalendarLiveService', () => {
  let token$: BehaviorSubject<string>;
  let connections: FakeHubConnection[];
  let configure: (c: FakeHubConnection) => void;
  let authState: {refreshToken: Mock<() => Observable<null>>};
  let service: BackendConfigurationCalendarLiveService;
  let hints: CalendarLiveHint[];
  let debug: MockInstance<typeof console.debug>;
  let warn: MockInstance<typeof console.warn>;
  let error: MockInstance<typeof console.error>;

  const last = () => connections[connections.length - 1];
  /** The reasons emitted so far, in order. */
  const reasons = () => hints.map(h => `${h.reason}:${h.propertyId}`);

  function create(): BackendConfigurationCalendarLiveService {
    const factory: CalendarHubConnectionFactory = accessTokenFactory => {
      const connection = new FakeHubConnection(accessTokenFactory);
      configure(connection);
      connections.push(connection);
      return connection as unknown as signalR.HubConnection;
    };
    const injector = Injector.create({providers: [
      {provide: Store, useValue: {select: () => token$}},
      {provide: AuthStateService, useValue: authState},
      {provide: CALENDAR_HUB_CONNECTION_FACTORY, useValue: factory},
      {provide: BackendConfigurationCalendarLiveService, useClass: BackendConfigurationCalendarLiveService, deps: []},
    ]});
    const created = injector.get(BackendConfigurationCalendarLiveService);
    created.hints$.subscribe(hint => hints.push(hint));
    return created;
  }

  beforeEach(() => {
    vi.useFakeTimers();
    token$ = new BehaviorSubject<string>('token-1');
    connections = [];
    configure = () => undefined;
    hints = [];
    authState = {refreshToken: vi.fn<() => Observable<null>>(() => of(null))};
    debug = vi.spyOn(console, 'debug').mockImplementation(() => undefined);
    warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    error = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    service = create();
  });

  afterEach(() => {
    // The hub must never be the cause of a sign-out: it never asks the host to refresh the session.
    expect(authState.refreshToken).not.toHaveBeenCalled();
    service.ngOnDestroy();
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it('does not connect before a property is shown', async () => {
    await flush();
    expect(connections.length).toBe(0);
    expect(service.isLive).toBe(false);
  });

  it('does not connect without a session', async () => {
    token$.next('');
    service.joinProperty(5);
    await flush();
    expect(connections.length).toBe(0);
  });

  it('joins the property on screen with JoinCalendarProperty, and is then live', async () => {
    service.joinProperty(5);
    await flush();
    expect(connections.length).toBe(1);
    expect(last().joins()).toEqual(['5']);
    expect(service.isLive).toBe(true);
    // The first join is announced as `joined`: the calendar catches up once.
    expect(reasons()).toEqual(['joined:5']);
  });

  it('announces the first join only once it is answered ok', async () => {
    let answer!: (value: unknown) => void;
    configure = c => (c.joinResult = () => new Promise(resolve => (answer = resolve)));
    service.joinProperty(5);
    await flush();
    expect(reasons()).toEqual([]);
    answer({v: 1, code: 'ok'});
    await flush();
    expect(reasons()).toEqual(['joined:5']);
  });

  it('leaves the previous property and joins the new one on the same connection', async () => {
    service.joinProperty(5);
    await flush();
    service.joinProperty(7);
    await flush();
    expect(connections.length).toBe(1);
    expect(last().leaves()).toEqual(['5']);
    expect(last().joins()).toEqual(['5', '7']);
    expect(service.isLive).toBe(true);
  });

  it('ignores a repeated join of the property already followed', async () => {
    service.joinProperty(5);
    await flush();
    service.joinProperty(5);
    await flush();
    expect(last().joins()).toEqual(['5']);
  });

  it('leaves a property whose join answered after the user had moved on', async () => {
    const answers: ((value: unknown) => void)[] = [];
    configure = c => (c.joinResult = () => new Promise(resolve => answers.push(resolve)));
    service.joinProperty(5);
    await flush();
    service.joinProperty(7); // while the join of 5 is still under way
    await flush();
    answers[0]({v: 1, code: 'ok'}); // the join of 5
    answers[1]({v: 1, code: 'ok'}); // the join of 7
    await flush();
    expect(last().leaves()).toEqual(['5']);
    expect(service.isLive).toBe(true);
  });

  it('turns TaskChanged for the followed property into a hint, and ignores other properties', async () => {
    service.joinProperty(5);
    await flush();
    last().serverSends('TaskChanged', {v: 1, property_id: '5', event_id: '7', kind: 'changed'});
    last().serverSends('TaskChanged', {v: 1, property_id: '6', kind: 'changed'});
    last().serverSends('TaskChanged', {v: 1});
    expect(reasons().filter(r => r.startsWith('changed'))).toEqual(['changed:5']);
  });

  it('re-joins after an automatic reconnect and emits one resync', async () => {
    service.joinProperty(5);
    await flush();
    last().dropAndReconnect();
    await flush();
    expect(last().joins()).toEqual(['5', '5']);
    expect(reasons()).toEqual(['joined:5', 'resync:5']);
  });

  it('starts again with back-off after the connection is lost, then re-joins and resyncs', async () => {
    service.joinProperty(5);
    await flush();
    last().serverCloses(); // a close without Refused: an ordinary drop
    await vi.advanceTimersByTimeAsync(CALENDAR_LIVE_RESTART_MS - 1);
    expect(connections.length).toBe(1);
    await vi.advanceTimersByTimeAsync(1);
    await flush();
    expect(connections.length).toBe(2);
    expect(last().joins()).toEqual(['5']);
    expect(reasons()).toEqual(['joined:5', 'resync:5']);
  });

  it('retries an unreachable hub with a growing delay and logs one debug line only', async () => {
    configure = c => (c.startError = new Error('Failed to fetch'));
    service.joinProperty(5);
    await flush();
    expect(connections.length).toBe(1);
    await vi.advanceTimersByTimeAsync(CALENDAR_LIVE_RESTART_MS);
    expect(connections.length).toBe(2);
    await vi.advanceTimersByTimeAsync(CALENDAR_LIVE_RESTART_MS); // the second delay is twice as long
    expect(connections.length).toBe(2);
    await vi.advanceTimersByTimeAsync(CALENDAR_LIVE_RESTART_MS);
    expect(connections.length).toBe(3);
    expect(debug).toHaveBeenCalledTimes(1);
    expect(warn).not.toHaveBeenCalled();
    expect(error).not.toHaveBeenCalled();
    expect(service.isLive).toBe(false);
  });

  it('goes quiet against a server without the hub (negotiate 404)', async () => {
    configure = c => (c.startError = Object.assign(new Error('Not Found'), {statusCode: 404}));
    service.joinProperty(5);
    await flush();
    await vi.advanceTimersByTimeAsync(10 * CALENDAR_LIVE_RESTART_MS);
    expect(connections.length).toBe(1);
    expect(debug).toHaveBeenCalledTimes(1);
    expect(error).not.toHaveBeenCalled();
    service.joinProperty(7);
    await flush();
    expect(connections.length).toBe(1);
  });

  it('goes quiet against a server without JoinCalendarProperty and closes the connection', async () => {
    configure = c => (c.joinResult = () =>
      Promise.reject(new Error('Failed to invoke \'JoinCalendarProperty\' due to an error on the server. HubException: Method does not exist.')));
    service.joinProperty(5);
    await flush();
    expect(last().stopCalls).toBe(1);
    expect(service.isLive).toBe(false);
    await vi.advanceTimersByTimeAsync(10 * CALENDAR_LIVE_RESTART_MS);
    expect(connections.length).toBe(1);
    expect(debug).toHaveBeenCalledTimes(1);
  });

  it('does not ask again for a property the server refused, and keeps the connection', async () => {
    configure = c => (c.joinResult = () => Promise.resolve({v: 1, code: 'property_not_available'}));
    service.joinProperty(5);
    await flush();
    await vi.advanceTimersByTimeAsync(10 * CALENDAR_LIVE_RESTART_MS);
    expect(last().joins()).toEqual(['5']);
    expect(last().stopCalls).toBe(0);
    expect(service.isLive).toBe(false);
    last().joinResult = () => Promise.resolve({v: 1, code: 'ok'});
    service.joinProperty(7);
    await flush();
    expect(last().joins()).toEqual(['5', '7']);
    expect(service.isLive).toBe(true);
  });

  it('goes quiet after Refused account_disabled: no reconnect', async () => {
    service.joinProperty(5);
    await flush();
    last().serverSends('Refused', {v: 1, code: 'account_disabled'});
    last().serverCloses();
    await vi.advanceTimersByTimeAsync(20 * CALENDAR_LIVE_RESTART_MS);
    expect(connections.length).toBe(1);
    expect(service.isLive).toBe(false);
    expect(debug).toHaveBeenCalledTimes(1);
  });

  it('reconnects once after Refused not_authenticated, with the token the store holds then, and never refreshes', async () => {
    service.joinProperty(5);
    await flush();
    configure = c => (c.joinResult = () => Promise.resolve({v: 1, code: 'not_authenticated'}));
    last().serverSends('Refused', {v: 1, code: 'not_authenticated'});
    last().serverCloses();
    token$.next('token-2'); // the REST interceptor refreshed the session meanwhile
    await vi.advanceTimersByTimeAsync(CALENDAR_LIVE_RESTART_MS);
    expect(connections.length).toBe(2);
    expect(last().tokenSent).toBe('token-2');
    last().serverSends('Refused', {v: 1, code: 'not_authenticated'});
    last().serverCloses();
    await vi.advanceTimersByTimeAsync(20 * CALENDAR_LIVE_RESTART_MS);
    expect(connections.length).toBe(2);
    expect(service.isLive).toBe(false);
  });

  it('waits at least a minute after Refused too_many_connections', async () => {
    service.joinProperty(5);
    await flush();
    last().serverSends('Refused', {v: 1, code: 'too_many_connections'});
    last().serverCloses();
    await vi.advanceTimersByTimeAsync(CALENDAR_LIVE_TOO_MANY_CONNECTIONS_MS - 1);
    expect(connections.length).toBe(1);
    await vi.advanceTimersByTimeAsync(1);
    expect(connections.length).toBe(2);
  });

  it('sends the current token on every connect, without refreshing it', async () => {
    service.joinProperty(5);
    await flush();
    expect(last().tokenSent).toBe('token-1');
    token$.next('token-2');
    last().serverCloses();
    await vi.advanceTimersByTimeAsync(CALENDAR_LIVE_RESTART_MS);
    expect(last().tokenSent).toBe('token-2');
  });

  it('stops the connection on sign-out', async () => {
    service.joinProperty(5);
    await flush();
    token$.next('');
    expect(last().stopCalls).toBe(1);
    await vi.advanceTimersByTimeAsync(10 * CALENDAR_LIVE_RESTART_MS);
    expect(connections.length).toBe(1);
  });

  it('stops the connection and every timer on destroy', async () => {
    configure = c => (c.startError = new Error('Failed to fetch'));
    service.joinProperty(5);
    await flush();
    const completed = vi.fn();
    service.hints$.subscribe({complete: completed});
    service.ngOnDestroy();
    expect(vi.getTimerCount()).toBe(0);
    expect(completed).toHaveBeenCalled();
    service.joinProperty(7);
    await vi.advanceTimersByTimeAsync(10 * CALENDAR_LIVE_RESTART_MS);
    expect(connections.length).toBe(1);
  });

  it('discards a start that was still pending when the view was destroyed', async () => {
    let open!: () => void;
    configure = c => (c.startGate = new Promise<void>(resolve => (open = resolve)));
    service.joinProperty(5);
    await flush();
    service.ngOnDestroy();
    open();
    await flush();
    expect(last().joins()).toEqual([]);
    expect(last().stopCalls).toBeGreaterThanOrEqual(1);
    expect(last().state).toBe(signalR.HubConnectionState.Disconnected);
    expect(vi.getTimerCount()).toBe(0);
  });

  it('stops a live connection on destroy', async () => {
    service.joinProperty(5);
    await flush();
    service.ngOnDestroy();
    expect(last().stopCalls).toBe(1);
  });
});
