/**
 * Test-only: run code as if the browser sat in a FIXED UTC offset.
 *
 * `process.env.TZ` cannot be used inside a jest test — jest hands the sandbox a
 * COPY of process.env, so assigning TZ never reaches Node and Date keeps the
 * runner's zone (UTC in CI). Instead this swaps the sandbox's global `Date` for
 * a subclass whose LOCAL-time surface (the Y/M/D/h constructor, local getters
 * and setters, string parsing without a zone, getTimezoneOffset and
 * toLocaleDateString) behaves as UTC+`offsetHours`. Epoch values, UTC getters
 * and toISOString are untouched, so `new Date(y, m, d).toISOString()` shows the
 * real shift a browser in that zone produces (UTC+1 "10 Dec" → 2026-12-09T23:00Z).
 *
 * A fixed offset has no DST transitions: pass +1 for Copenhagen winter and +2
 * for Copenhagen summer.
 *
 * Returns a restore function; call it in afterEach.
 */
export function useFixedUtcOffset(offsetHours: number): () => void {
  const RealDate = Date;
  const off = offsetHours * 3600000;
  const ianaZone = offsetHours === 0
    ? 'UTC'
    : `Etc/GMT${offsetHours > 0 ? '-' : '+'}${Math.abs(offsetHours)}`;
  const hasZone = (s: string) => /(?:[zZ]|[+-]\d{2}:?\d{2})$/.test(s.trim());
  const isDateOnly = (s: string) => /^\d{4}-\d{2}-\d{2}$/.test(s.trim());

  const toEpoch = (args: unknown[]): number => {
    if (args.length === 0) return RealDate.now();
    if (args.length === 1) {
      const a = args[0];
      if (a instanceof RealDate) return a.getTime();
      if (typeof a === 'string') {
        // ECMAScript: a date-time string WITHOUT a zone is LOCAL time; a
        // date-only string is UTC.
        if (!hasZone(a) && !isDateOnly(a) && /T\d/.test(a)) {
          return RealDate.parse(a + 'Z') - off;
        }
        return RealDate.parse(a);
      }
      return Number(a);
    }
    const [y, m, d = 1, h = 0, mi = 0, s = 0, ms = 0] = args as number[];
    return RealDate.UTC(y, m, d, h, mi, s, ms) - off;
  };

  class FixedOffsetDate extends RealDate {
    constructor(...args: unknown[]) {
      super(toEpoch(args));
    }

    private local(): Date {
      return new RealDate(this.getTime() + off);
    }

    private commit(shifted: Date): number {
      return this.setTime(shifted.getTime() - off);
    }

    getFullYear(): number { return this.local().getUTCFullYear(); }
    getMonth(): number { return this.local().getUTCMonth(); }
    getDate(): number { return this.local().getUTCDate(); }
    getDay(): number { return this.local().getUTCDay(); }
    getHours(): number { return this.local().getUTCHours(); }
    getMinutes(): number { return this.local().getUTCMinutes(); }
    getSeconds(): number { return this.local().getUTCSeconds(); }
    getMilliseconds(): number { return this.local().getUTCMilliseconds(); }
    getTimezoneOffset(): number { return -offsetHours * 60; }

    setFullYear(y: number, m?: number, d?: number): number {
      const l = this.local();
      l.setUTCFullYear(y, m ?? l.getUTCMonth(), d ?? l.getUTCDate());
      return this.commit(l);
    }

    setMonth(m: number, d?: number): number {
      const l = this.local();
      l.setUTCMonth(m, d ?? l.getUTCDate());
      return this.commit(l);
    }

    setDate(d: number): number {
      const l = this.local();
      l.setUTCDate(d);
      return this.commit(l);
    }

    setHours(h: number, mi?: number, s?: number, ms?: number): number {
      const l = this.local();
      l.setUTCHours(h, mi ?? l.getUTCMinutes(), s ?? l.getUTCSeconds(), ms ?? l.getUTCMilliseconds());
      return this.commit(l);
    }

    toLocaleDateString(locales?: string | string[], options?: Intl.DateTimeFormatOptions): string {
      return super.toLocaleDateString(locales, {...options, timeZone: ianaZone});
    }
  }

  (globalThis as {Date: DateConstructor}).Date = FixedOffsetDate as unknown as DateConstructor;
  return () => {
    (globalThis as {Date: DateConstructor}).Date = RealDate;
  };
}
