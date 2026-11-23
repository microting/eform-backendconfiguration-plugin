import {afterEach, beforeEach, describe, expect, it, vi} from 'vitest';
import type {Mock} from 'vitest';
import {CALENDAR_LIVE_RELOAD_DEBOUNCE_MS, CalendarLiveReloadScheduler} from './calendar-live-reload.scheduler';

describe('CalendarLiveReloadScheduler', () => {
  let reload: Mock<() => void>;
  let busy: boolean;
  let scheduler: CalendarLiveReloadScheduler;

  beforeEach(() => {
    vi.useFakeTimers();
    reload = vi.fn<() => void>();
    busy = false;
    scheduler = new CalendarLiveReloadScheduler({reload, isBusy: () => busy});
  });

  afterEach(() => {
    scheduler.dispose();
    vi.useRealTimers();
  });

  it('reloads once, after the debounce window', () => {
    scheduler.request();
    vi.advanceTimersByTime(CALENDAR_LIVE_RELOAD_DEBOUNCE_MS - 1);
    expect(reload).not.toHaveBeenCalled();
    vi.advanceTimersByTime(1);
    expect(reload).toHaveBeenCalledTimes(1);
    expect(scheduler.reloadOwed).toBe(false);
  });

  it('merges a burst inside the window into one reload', () => {
    scheduler.request();
    vi.advanceTimersByTime(300);
    scheduler.request();
    vi.advanceTimersByTime(300);
    scheduler.request();
    vi.advanceTimersByTime(CALENDAR_LIVE_RELOAD_DEBOUNCE_MS);
    expect(reload).toHaveBeenCalledTimes(1);
  });

  it('keeps no timer while nothing is owed', () => {
    expect(vi.getTimerCount()).toBe(0);
    scheduler.settle();
    expect(vi.getTimerCount()).toBe(0);
    scheduler.request();
    vi.advanceTimersByTime(CALENDAR_LIVE_RELOAD_DEBOUNCE_MS);
    expect(vi.getTimerCount()).toBe(0);
  });

  it('holds the reload while busy and runs it once after settle()', () => {
    busy = true;
    scheduler.request();
    scheduler.request();
    vi.advanceTimersByTime(10 * CALENDAR_LIVE_RELOAD_DEBOUNCE_MS);
    expect(reload).not.toHaveBeenCalled();
    expect(scheduler.reloadOwed).toBe(true);
    expect(vi.getTimerCount()).toBe(0); // owed, but no polling

    busy = false;
    scheduler.settle();
    vi.advanceTimersByTime(CALENDAR_LIVE_RELOAD_DEBOUNCE_MS);
    expect(reload).toHaveBeenCalledTimes(1);
    scheduler.settle();
    vi.advanceTimersByTime(CALENDAR_LIVE_RELOAD_DEBOUNCE_MS);
    expect(reload).toHaveBeenCalledTimes(1);
  });

  it('stays owed when a settle() lands while another interaction already started', () => {
    busy = true;
    scheduler.request();
    vi.advanceTimersByTime(CALENDAR_LIVE_RELOAD_DEBOUNCE_MS);
    scheduler.settle(); // the first interaction ended, but the next one is open again before the window ends
    vi.advanceTimersByTime(CALENDAR_LIVE_RELOAD_DEBOUNCE_MS);
    expect(reload).not.toHaveBeenCalled();
    busy = false;
    scheduler.settle();
    vi.advanceTimersByTime(CALENDAR_LIVE_RELOAD_DEBOUNCE_MS);
    expect(reload).toHaveBeenCalledTimes(1);
  });

  it('drops the owed reload when the calendar starts a load of its own', () => {
    scheduler.request();
    vi.advanceTimersByTime(500);
    scheduler.loadStarted(); // e.g. the reload after the user's own save
    vi.advanceTimersByTime(CALENDAR_LIVE_RELOAD_DEBOUNCE_MS);
    expect(reload).not.toHaveBeenCalled();
    expect(scheduler.reloadOwed).toBe(false);
  });

  it('drops a reload owed during an interaction when the interaction ends in a load of its own', () => {
    busy = true;
    scheduler.request();
    vi.advanceTimersByTime(CALENDAR_LIVE_RELOAD_DEBOUNCE_MS);
    busy = false;
    scheduler.settle(); // the dialog closed ...
    scheduler.loadStarted(); // ... and its save reloads
    vi.advanceTimersByTime(CALENDAR_LIVE_RELOAD_DEBOUNCE_MS);
    expect(reload).not.toHaveBeenCalled();
  });

  it('does nothing after dispose()', () => {
    scheduler.request();
    scheduler.dispose();
    scheduler.request();
    scheduler.settle();
    vi.advanceTimersByTime(CALENDAR_LIVE_RELOAD_DEBOUNCE_MS * 2);
    expect(reload).not.toHaveBeenCalled();
    expect(vi.getTimerCount()).toBe(0);
  });
});
