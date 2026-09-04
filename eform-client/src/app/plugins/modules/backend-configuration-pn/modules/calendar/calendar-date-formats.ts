import {MatDateFormats} from '@angular/material/core';

/**
 * The calendar's own MAT_DATE_FORMATS: the long Danish display form
 * ("Mandag, 21. april") for the event modals' date inputs, while parsing stays
 * on the short 'P' token so users can still TYPE a short date.
 *
 * Exported rather than inlined in `CalendarModule.providers` because the
 * completion modal is opened from OTHER lazy modules too (the standalone
 * Compliance page, #1165), which do not — and must not — inherit this override
 * for their own components' datepickers. Those consumers give the modal this
 * format at component level; see
 * `calendar-complete-event-modal.component.ts`.
 */
export const CALENDAR_MAT_DATE_FORMATS: MatDateFormats = {
  parse: {dateInput: 'P'},
  display: {
    dateInput: 'EEEE, d. MMMM',
    monthYearLabel: 'LLLL y',
    dateA11yLabel: 'PPP',
    monthYearA11yLabel: 'LLLL y',
  },
};
