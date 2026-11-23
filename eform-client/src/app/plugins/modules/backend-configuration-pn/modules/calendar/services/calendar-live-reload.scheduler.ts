/** How long a live hint waits before the calendar reloads, so a burst of hints costs one reload. */
export const CALENDAR_LIVE_RELOAD_DEBOUNCE_MS = 1000;

export interface CalendarLiveReloadOptions {
  /** The calendar's own task load. */
  reload: () => void;
  /** True while a reload would disturb the user: a task dialog or popover is open, a drag, a resize or a save. */
  isBusy: () => boolean;
  debounceMs?: number;
}

/**
 * Decides WHEN a live hint reloads the calendar (package 11c). Plain class, no Angular, so a spec drives it with
 * fake timers.
 *
 * - `request()` (a hint, a resync, the tab shown again) owes one reload. The reload runs once the debounce window
 *   has passed; every request inside the window joins the same reload.
 * - When the window ends while the calendar is busy, the reload stays owed. `settle()`, called when an interaction
 *   ends, starts a new window, so it runs once, after the interaction.
 * - `loadStarted()`: the calendar is loading anyway (navigation, a filter, the reload after the user's own save).
 *   That load reads everything the owed reload would read, so the owed reload is dropped.
 *
 * There is no timer while nothing is owed: no polling.
 */
export class CalendarLiveReloadScheduler {
  private owed = false;
  private timer: ReturnType<typeof setTimeout> | null = null;
  private disposed = false;
  private readonly debounceMs: number;

  constructor(private readonly options: CalendarLiveReloadOptions) {
    this.debounceMs = options.debounceMs ?? CALENDAR_LIVE_RELOAD_DEBOUNCE_MS;
  }

  /** A reload is owed and has not run yet. */
  get reloadOwed(): boolean {
    return this.owed;
  }

  request(): void {
    if (this.disposed) {
      return;
    }
    this.owed = true;
    this.arm();
  }

  settle(): void {
    if (this.owed) {
      this.arm();
    }
  }

  loadStarted(): void {
    this.owed = false;
    this.cancel();
  }

  /** After this, nothing is owed and request() is ignored, so no timer can start again. */
  dispose(): void {
    this.disposed = true;
    this.owed = false;
    this.cancel();
  }

  private arm(): void {
    if (this.timer !== null) {
      return;
    }
    this.timer = setTimeout(() => this.fire(), this.debounceMs);
  }

  private fire(): void {
    this.timer = null;
    if (!this.owed || this.options.isBusy()) {
      return;
    }
    this.owed = false;
    this.options.reload();
  }

  private cancel(): void {
    if (this.timer !== null) {
      clearTimeout(this.timer);
    }
    this.timer = null;
  }
}
