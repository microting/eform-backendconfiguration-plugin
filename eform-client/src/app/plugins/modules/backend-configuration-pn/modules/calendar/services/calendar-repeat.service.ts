import {Injectable} from '@angular/core';
import {TranslateService} from '@ngx-translate/core';
import {CalendarRepeatMeta, CalendarTaskModel} from '../../../models/calendar';
import {getCurrentLocale} from './calendar-locale.helper';

export interface RepeatSelectOption {
  value: string;
  label: string;
  meta?: CalendarRepeatMeta;
}

/**
 * Port of getAllOccurrencesFromMeta(), getCustomOccurrenceCopies(),
 * and getRepeatSelectHtmlForDate() from JS.js ~line 2398.
 * Pure TypeScript — no DOM, no API calls.
 */
@Injectable({providedIn: 'root'})
export class CalendarRepeatService {

  constructor(private translate: TranslateService) {}

  private getDayNames(): string[] {
    return [
      this.translate.instant('Monday'),
      this.translate.instant('Tuesday'),
      this.translate.instant('Wednesday'),
      this.translate.instant('Thursday'),
      this.translate.instant('Friday'),
      this.translate.instant('Saturday'),
      this.translate.instant('Sunday'),
    ];
  }

  private getMonthNames(): string[] {
    return [
      this.translate.instant('January'),
      this.translate.instant('February'),
      this.translate.instant('March'),
      this.translate.instant('April'),
      this.translate.instant('May'),
      this.translate.instant('June'),
      this.translate.instant('July'),
      this.translate.instant('August'),
      this.translate.instant('September'),
      this.translate.instant('October'),
      this.translate.instant('November'),
      this.translate.instant('December'),
    ];
  }

  private getMondayOfWeek(d: Date): Date {
    const date = new Date(d);
    const day = date.getDay();
    const diff = day === 0 ? -6 : 1 - day;
    date.setDate(date.getDate() + diff);
    date.setHours(0, 0, 0, 0);
    return date;
  }

  private dayOffsetFromMonday(weekday: number): number {
    return weekday === 0 ? 6 : weekday - 1;
  }

  private trimOccurrencesByEnd(raw: number[], meta: CalendarRepeatMeta): number[] {
    const unique = [...new Set(raw.map(t => {
      const d = new Date(t);
      d.setHours(0, 0, 0, 0);
      return d.getTime();
    }))].sort((a, b) => a - b);

    if (meta.endMode === 'after') return unique.slice(0, meta.afterCount ?? 10);
    if (meta.endMode === 'until' && meta.untilTs != null) {
      const until = new Date(meta.untilTs);
      until.setHours(23, 59, 59, 999);
      return unique.filter(t => t <= until.getTime());
    }
    return unique.slice(0, 250);
  }

  getAllOccurrences(meta: CalendarRepeatMeta, firstTs: number): number[] {
    const start = new Date(firstTs);
    start.setHours(0, 0, 0, 0);
    const raw: number[] = [];

    switch (meta.kind) {
      case 'daily':
        for (let i = 0; i < 400; i++) {
          const d = new Date(start);
          d.setDate(d.getDate() + i);
          raw.push(d.getTime());
        }
        break;

      case 'everyNd':
        for (let i = 0; i < 200; i++) {
          const d = new Date(start);
          d.setDate(d.getDate() + i * (meta.n ?? 1));
          raw.push(d.getTime());
        }
        break;

      case 'weeklyOne': {
        const W = meta.weekday ?? 1;
        const d = new Date(start);
        while (d.getDay() !== W) d.setDate(d.getDate() + 1);
        for (let i = 0; i < 120; i++) {
          raw.push(new Date(d).getTime());
          d.setDate(d.getDate() + 7);
        }
        break;
      }

      case 'weeklyMulti': {
        const days = meta.weekdays ?? [];
        const d = new Date(start);
        for (let g = 0; g < 600; g++) {
          if (days.includes(d.getDay())) raw.push(new Date(d).getTime());
          d.setDate(d.getDate() + 1);
        }
        break;
      }

      case 'weeklyAll': {
        const d = new Date(start);
        for (let g = 0; g < 600; g++) {
          raw.push(new Date(d).getTime());
          d.setDate(d.getDate() + 1);
        }
        break;
      }

      case 'everyNWeekOne': {
        const anchor = this.getMondayOfWeek(start);
        const W = meta.weekday ?? 1;
        const step = meta.n ?? 1;
        for (let wk = 0; wk < 150; wk += step) {
          const mon = new Date(anchor);
          mon.setDate(mon.getDate() + wk * 7);
          const tgt = new Date(mon);
          tgt.setDate(mon.getDate() + this.dayOffsetFromMonday(W));
          if (tgt.getTime() >= start.getTime()) raw.push(tgt.getTime());
        }
        break;
      }

      case 'everyNWeekAll': {
        const anchor = this.getMondayOfWeek(start);
        const step = meta.n ?? 1;
        for (let wk = 0; wk < 100; wk += step) {
          const mon = new Date(anchor);
          mon.setDate(mon.getDate() + wk * 7);
          for (let i = 0; i < 7; i++) {
            const tgt = new Date(mon);
            tgt.setDate(mon.getDate() + i);
            if (tgt.getTime() >= start.getTime()) raw.push(tgt.getTime());
          }
        }
        break;
      }

      case 'everyNWeekMulti': {
        const anchor = this.getMondayOfWeek(start);
        const step = meta.n ?? 1;
        const days = meta.weekdays ?? [];
        for (let wk = 0; wk < 100; wk += step) {
          const mon = new Date(anchor);
          mon.setDate(mon.getDate() + wk * 7);
          for (let i = 0; i < 7; i++) {
            const tgt = new Date(mon);
            tgt.setDate(mon.getDate() + i);
            if (tgt.getTime() >= start.getTime() && days.includes(tgt.getDay()))
              raw.push(tgt.getTime());
          }
        }
        break;
      }

      case 'monthlyDom': {
        let iter = new Date(start.getFullYear(), start.getMonth(), 1);
        for (let i = 0; i < 120; i++) {
          const last = new Date(iter.getFullYear(), iter.getMonth() + 1, 0).getDate();
          const day = Math.min(meta.dom ?? 1, last);
          const t = new Date(iter.getFullYear(), iter.getMonth(), day);
          if (t.getTime() >= start.getTime()) raw.push(t.getTime());
          iter.setMonth(iter.getMonth() + 1);
        }
        break;
      }

      case 'everyNMonthDom': {
        const dom = meta.dom ?? 1;
        const n = meta.n ?? 1;
        for (let i = 0; i < 100; i++) {
          const cand = new Date(start.getFullYear(), start.getMonth() + i * n, 1);
          const lastD = new Date(cand.getFullYear(), cand.getMonth() + 1, 0).getDate();
          const t = new Date(cand.getFullYear(), cand.getMonth(), Math.min(dom, lastD));
          if (t.getTime() >= start.getTime()) raw.push(t.getTime());
        }
        break;
      }

      case 'yearlyOne': {
        const mo = meta.month ?? 0;
        const dom = meta.dom ?? 1;
        for (let k = 0; k < 40; k++) {
          const yy = start.getFullYear() + k;
          const last = new Date(yy, mo + 1, 0).getDate();
          const t = new Date(yy, mo, Math.min(dom, last));
          if (t.getTime() >= start.getTime()) raw.push(t.getTime());
        }
        break;
      }

      case 'everyNYear': {
        const mo = meta.month ?? 0;
        const dom = meta.dom ?? 1;
        const n = meta.n ?? 1;
        for (let k = 0; k < 40; k++) {
          const yy = start.getFullYear() + k * n;
          const last = new Date(yy, mo + 1, 0).getDate();
          const t = new Date(yy, mo, Math.min(dom, last));
          if (t.getTime() >= start.getTime()) raw.push(t.getTime());
        }
        break;
      }
    }

    return this.trimOccurrencesByEnd(raw, meta);
  }

  /**
   * Build the repeat dropdown options for a given base date.
   * Mirrors getRepeatSelectHtmlForDate() from JS.js.
   *
   * When `customMeta` is provided (user has configured a custom repeat rule),
   * a synthesized 'customCurrent' option is spliced in BEFORE the 'Custom…'
   * trigger so the dropdown shows a human-readable summary as the selected
   * value, with 'Custom…' remaining below as the re-open/edit affordance.
   */
  buildRepeatSelectOptions(
    date: Date,
    customMeta?: CalendarRepeatMeta | null,
  ): RepeatSelectOption[] {
    const dayNames = this.getDayNames();
    const monthNames = this.getMonthNames();

    const weekday = date.getDay();
    const dom = date.getDate();
    const month = date.getMonth();
    // dayNames is Monday-indexed (Mon=0..Sun=6); JS getDay() is Sunday-indexed.
    const dayName = dayNames[(weekday + 6) % 7];
    const monthName = monthNames[month];

    const options: RepeatSelectOption[] = [
      {value: 'none', label: this.translate.instant('Does not repeat')},
      {
        value: 'daily',
        label: this.translate.instant('Daily'),
        meta: {kind: 'daily', endMode: 'never'},
      },
      {
        value: 'weeklyOne',
        label: this.translate.instant('Weekly on {{day}}', {day: dayName}),
        meta: {kind: 'weeklyOne', weekday, endMode: 'never'},
      },
      {
        value: 'weeklyAll',
        label: this.translate.instant('Every weekday'),
        meta: {kind: 'weeklyAll', endMode: 'never'},
      },
      {
        value: 'monthlyDom',
        label: this.translate.instant('Monthly on day {{day}}', {day: dom}),
        meta: {kind: 'monthlyDom', dom, endMode: 'never'},
      },
      {
        value: 'yearlyOne',
        label: this.translate.instant('Yearly on {{day}} {{month}}', {day: dom, month: monthName}),
        meta: {kind: 'yearlyOne', dom, month, endMode: 'never'},
      },
    ];

    if (customMeta) {
      const locale = getCurrentLocale(this.translate);
      options.push({
        value: 'customCurrent',
        label: this.formatCustomRepeatLabel(customMeta, locale),
        meta: customMeta,
      });
    }

    options.push({value: 'custom', label: this.translate.instant('Custom…')});

    return options;
  }

  /**
   * Build a human-readable label summarising a custom repeat rule.
   *
   * Weekday names come from `toLocaleDateString(locale, {weekday: 'long'})`
   * and lists are joined with `Intl.ListFormat` (conjunction, long), so we
   * get correctly-localised "Monday, Tuesday and Wednesday" / "mandag,
   * tirsdag og onsdag" without adding 7 weekday translation keys per
   * language.
   *
   * Weekdays are output Monday-first regardless of input order (input
   * uses JS `getDay()` indices: 0=Sunday..6=Saturday).
   */
  formatCustomRepeatLabel(meta: CalendarRepeatMeta, locale: string): string {
    const n = meta.n ?? 1;

    switch (meta.kind) {
      case 'daily':
        return this.translate.instant('Daily');

      case 'everyNd':
        return this.translate.instant('Every {{n}} days', {n});

      case 'weeklyOne':
      case 'weeklyMulti':
      case 'weeklyAll':
      case 'everyNWeekOne':
      case 'everyNWeekMulti':
      case 'everyNWeekAll': {
        const rawDays =
          meta.kind === 'weeklyOne' || meta.kind === 'everyNWeekOne'
            ? meta.weekday != null ? [meta.weekday] : []
            : meta.weekdays ?? [];
        const sortedDays = this.sortWeekdaysMondayFirst(rawDays);
        const isWeekly = n === 1;
        const isAllDays = sortedDays.length === 7
          || meta.kind === 'weeklyAll'
          || meta.kind === 'everyNWeekAll';

        if (isAllDays) {
          return isWeekly
            ? this.translate.instant('Weekly on all days')
            : this.translate.instant('Every {{n}} weeks: all days', {n});
        }

        const days = this.formatWeekdayList(sortedDays, locale);
        return isWeekly
          ? this.translate.instant('Weekly every {{days}}', {days})
          : this.translate.instant('Every {{n}} weeks: {{days}}', {n, days});
      }

      case 'monthlyDom': {
        const dom = meta.dom ?? 1;
        return this.translate.instant('Monthly on day {{day}}', {day: dom});
      }

      case 'everyNMonthDom': {
        const dom = meta.dom ?? 1;
        return this.translate.instant('Every {{n}} months on day {{dom}}', {n, dom});
      }

      case 'yearlyOne': {
        const dom = meta.dom ?? 1;
        const monthName = this.getMonthNames()[meta.month ?? 0];
        return this.translate.instant('Yearly on {{day}} {{month}}', {day: dom, month: monthName});
      }

      case 'everyNYear': {
        const dom = meta.dom ?? 1;
        const monthName = this.getMonthNames()[meta.month ?? 0];
        return this.translate.instant('Every {{n}} years on {{dom}}. {{month}}', {n, dom, month: monthName});
      }

      default:
        return this.translate.instant('Custom…');
    }
  }

  /**
   * Sort an array of JS getDay() indices (0=Sunday..6=Saturday) so the
   * output starts with Monday and ends with Sunday — independent of input
   * order or duplicates.
   */
  private sortWeekdaysMondayFirst(days: number[]): number[] {
    const unique = Array.from(new Set(days));
    return unique.sort((a, b) => this.dayOffsetFromMonday(a) - this.dayOffsetFromMonday(b));
  }

  /**
   * Format an ordered list of JS getDay() indices into a localised
   * "Monday, Tuesday and Wednesday" string using Intl.ListFormat plus
   * toLocaleDateString for the weekday names.
   */
  private formatWeekdayList(days: number[], locale: string): string {
    // Pick any known Monday as the anchor and add the day-from-Monday offset
    // to land on the right weekday for toLocaleDateString.
    const monday = new Date(2024, 0, 1);  // 2024-01-01 is a Monday
    const names = days.map(d => {
      const offset = this.dayOffsetFromMonday(d);
      const target = new Date(monday);
      target.setDate(monday.getDate() + offset);
      return target.toLocaleDateString(locale, {weekday: 'long'});
    });

    const formatter = new Intl.ListFormat(locale, {style: 'long', type: 'conjunction'});
    return formatter.format(names);
  }

  /**
   * Inverse of `buildMetaFromCustomConfig` — decompose an existing
   * `CalendarRepeatMeta` back into the modal's editable fields so the
   * Custom-repeat modal can hydrate from a previously configured rule.
   *
   * Returns step / unit / weekdays (JS getDay() format: 0=Sun..6=Sat) /
   * endMode / afterCount / untilTs. The endMode trio is passed through
   * directly; the modal converts `untilTs` → its `untilDateObj` itself.
   *
   * Defensive defaults: an unknown `kind` yields a weekly-empty config
   * so the modal stays usable instead of throwing.
   */
  decomposeCustomMeta(meta: CalendarRepeatMeta): {
    step: number;
    unit: 'day' | 'week' | 'month' | 'year';
    weekdays: number[];
    endMode: 'never' | 'after' | 'until';
    afterCount?: number;
    untilTs?: number;
  } {
    let step = 1;
    let unit: 'day' | 'week' | 'month' | 'year' = 'week';
    let weekdays: number[] = [];

    switch (meta.kind) {
      case 'daily':
        step = 1; unit = 'day'; weekdays = [];
        break;
      case 'everyNd':
        step = meta.n ?? 1; unit = 'day'; weekdays = [];
        break;
      case 'weeklyOne':
        step = 1; unit = 'week';
        weekdays = meta.weekday != null ? [meta.weekday] : [];
        break;
      case 'weeklyMulti':
        step = 1; unit = 'week'; weekdays = meta.weekdays ?? [];
        break;
      // 'weeklyAll' is supported here for metas emitted by the backend or by
      // future builders; the in-app builder collapses empty-weekday weekly
      // configs to 'everyNWeekAll' regardless of step.
      case 'weeklyAll':
        step = 1; unit = 'week'; weekdays = [];
        break;
      case 'everyNWeekOne':
        step = meta.n ?? 1; unit = 'week';
        weekdays = meta.weekday != null ? [meta.weekday] : [];
        break;
      case 'everyNWeekMulti':
        step = meta.n ?? 1; unit = 'week'; weekdays = meta.weekdays ?? [];
        break;
      case 'everyNWeekAll':
        step = meta.n ?? 1; unit = 'week'; weekdays = [];
        break;
      case 'monthlyDom':
        step = 1; unit = 'month'; weekdays = [];
        break;
      case 'everyNMonthDom':
        step = meta.n ?? 1; unit = 'month'; weekdays = [];
        break;
      case 'yearlyOne':
        step = 1; unit = 'year'; weekdays = [];
        break;
      case 'everyNYear':
        step = meta.n ?? 1; unit = 'year'; weekdays = [];
        break;
      default:
        step = 1; unit = 'week'; weekdays = [];
        break;
    }

    return {
      step,
      unit,
      weekdays,
      endMode: meta.endMode ?? 'never',
      afterCount: meta.afterCount,
      untilTs: meta.untilTs,
    };
  }

  /**
   * Reconstruct a `CalendarRepeatMeta` from a backend-loaded task so the
   * edit-modal can land on the synthesized `customCurrent` dropdown option
   * instead of resetting to a default rule.
   *
   * Mirrors `buildMetaFromCustomConfig`'s kind-selection logic. Returns
   * `null` for `none`, malformed legacy rows (missing required fields per
   * rule), and unknown `repeatRule` values — the caller treats `null` as
   * "fall back to the raw `repeatRule` string and skip reconstruction".
   *
   * The `weekdays` rule maps to `weeklyMulti`/`everyNWeekMulti` with
   * weekdays=[1..5] so the formatter renders the explicit Mon-Fri list
   * rather than collapsing to "all days".
   */
  reconstructMetaFromTask(task: CalendarTaskModel): CalendarRepeatMeta | null {
    // Cast to string so the switch can include `'weekdays'` even though it
    // isn't currently in the CalendarRepeatRule union — the backend may
    // start emitting it once the controller maps RepeatType=5 explicitly,
    // and the helper stays forward-compatible.
    // Cast to string so the switch can include `'weekdays'` even though it
    // isn't currently in the CalendarRepeatRule union — the backend may
    // start emitting it once the controller maps RepeatType=5 explicitly,
    // and the helper stays forward-compatible.
    const r = task.repeatRule as string | undefined;
    // Defensive coalesce: backend should never send 0 or negative, but guard
    // anyway so a bogus value doesn't cascade into an iterator with step=0.
    const n = (task.repeatEvery ?? 0) > 0 ? task.repeatEvery! : 1;
    // Clamp end-mode lookup so an out-of-range index doesn't yield undefined.
    const endModes = ['never', 'after', 'until'] as const;
    const endMode = endModes[task.repeatEndMode ?? 0] ?? 'never';
    const afterCount = endMode === 'after' ? task.repeatOccurrences ?? undefined : undefined;
    const untilTs = endMode === 'until' && task.repeatUntilDate
      ? new Date(task.repeatUntilDate).getTime() : undefined;

    if (!r || r === 'none') return null;

    // Helper for yearly month — getUTCMonth avoids the local-tz off-by-one
    // for ISO timestamps with a `Z` suffix.
    const monthFromTaskDate = () => new Date(task.taskDate).getUTCMonth();

    // Parse CSV up front so weekly branches can promote single-day to
    // multi-day when a multi-day rule was saved at step=1 (which routes
    // through `mapRepeatType` as 'weeklyOne' and would otherwise lose the
    // CSV-derived day list).
    const csvDays = (task.repeatWeekdaysCsv ?? '')
      .split(',').map(s => s.trim()).filter(Boolean).map(Number)
      .filter(d => d >= 0 && d <= 6);
    const weeklyFromCsv = (): CalendarRepeatMeta => {
      if (csvDays.length === 1) {
        return {
          kind: n === 1 ? 'weeklyOne' : 'everyNWeekOne', n,
          weekday: csvDays[0], endMode, afterCount, untilTs,
        };
      }
      // Length 0 (legacy fallback) and length >=2 both go to all/multi.
      if (csvDays.length === 0) {
        return {kind: n === 1 ? 'weeklyAll' : 'everyNWeekAll', n, endMode, afterCount, untilTs};
      }
      return {
        kind: n === 1 ? 'weeklyMulti' : 'everyNWeekMulti', n,
        weekdays: csvDays, endMode, afterCount, untilTs,
      };
    };

    switch (r) {
      case 'daily':
        return {kind: n === 1 ? 'daily' : 'everyNd', n, endMode, afterCount, untilTs};

      case 'weeklyOne':
        // Promote to multi-day if a CSV is present — covers the common case
        // where mapRepeatType collapses any weekly rule with step=1 to
        // 'weeklyOne' regardless of CSV presence.
        if (csvDays.length > 0) return weeklyFromCsv();
        if (task.dayOfWeek == null) return null;
        return {
          kind: n === 1 ? 'weeklyOne' : 'everyNWeekOne', n,
          weekday: task.dayOfWeek, endMode, afterCount, untilTs,
        };

      case 'weeklyAll':
        // Same promotion path: CSV wins if present.
        if (csvDays.length > 0) return weeklyFromCsv();
        return {kind: n === 1 ? 'weeklyAll' : 'everyNWeekAll', n, endMode, afterCount, untilTs};

      case 'weekdays':
        // Mon-Fri. Map to weeklyMulti so the formatter renders the explicit list
        // ("Weekly every Mon, Tue, Wed, Thu and Fri") instead of "all days".
        return {
          kind: n === 1 ? 'weeklyMulti' : 'everyNWeekMulti', n,
          weekdays: [1, 2, 3, 4, 5], endMode, afterCount, untilTs,
        };

      case 'monthlyDom':
        if (task.dayOfMonth == null) return null;
        return {
          kind: n === 1 ? 'monthlyDom' : 'everyNMonthDom', n,
          dom: task.dayOfMonth, endMode, afterCount, untilTs,
        };

      case 'yearlyOne':
        if (task.dayOfMonth == null) return null;
        return {
          kind: n === 1 ? 'yearlyOne' : 'everyNYear', n,
          dom: task.dayOfMonth, month: monthFromTaskDate(),
          endMode, afterCount, untilTs,
        };

      case 'custom': {
        switch (task.repeatType) {
          case 1:
            return {kind: n === 1 ? 'daily' : 'everyNd', n, endMode, afterCount, untilTs};
          case 2:
            // Pre-migration row with no CSV → weeklyAll/everyNWeekAll fallback.
            return weeklyFromCsv();
          case 3:
            if (task.dayOfMonth == null) return null;
            return {
              kind: n === 1 ? 'monthlyDom' : 'everyNMonthDom', n,
              dom: task.dayOfMonth, endMode, afterCount, untilTs,
            };
          case 4:
            if (task.dayOfMonth == null) return null;
            return {
              kind: n === 1 ? 'yearlyOne' : 'everyNYear', n,
              dom: task.dayOfMonth, month: monthFromTaskDate(),
              endMode, afterCount, untilTs,
            };
          default:
            return null;
        }
      }

      default:
        return null;
    }
  }

  /** Convert a custom repeat config to a CalendarRepeatMeta */
  buildMetaFromCustomConfig(
    step: number,
    unit: string,
    weekdays: number[],
    endMode: 'never' | 'after' | 'until',
    afterCount?: number,
    untilTs?: number
  ): CalendarRepeatMeta {
    const base: Partial<CalendarRepeatMeta> = {endMode, afterCount, untilTs, n: step};

    if (unit === 'day') {
      return {...base, kind: step === 1 ? 'daily' : 'everyNd'} as CalendarRepeatMeta;
    } else if (unit === 'week') {
      const kind = weekdays.length === 0 ? 'everyNWeekAll'
        : weekdays.length === 1 ? (step === 1 ? 'weeklyOne' : 'everyNWeekOne')
          : (step === 1 ? 'weeklyMulti' : 'everyNWeekMulti');
      return {
        ...base,
        kind,
        weekday: weekdays.length === 1 ? weekdays[0] : undefined,
        weekdays: weekdays.length !== 1 ? weekdays : undefined,
      } as CalendarRepeatMeta;
    } else if (unit === 'month') {
      return {...base, kind: step === 1 ? 'monthlyDom' : 'everyNMonthDom', dom: 1} as CalendarRepeatMeta;
    } else {
      return {...base, kind: step === 1 ? 'yearlyOne' : 'everyNYear', dom: 1, month: 0} as CalendarRepeatMeta;
    }
  }
}
